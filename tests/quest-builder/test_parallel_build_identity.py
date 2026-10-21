"""Actual shipped producer rows retain recovery/preparation after scheduling fixes."""
import copy
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import editor_overlay
import native_bank_resume
import preparation_identity as identity
import recovery_resume
import storage
import update_driver

BASE = "79a53d673149b92752c62c638382f812685d6394"  # Delivered B693, before current-dev media.


class ParallelBuildIdentityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.old = []
        names = subprocess.check_output(["git", "ls-tree", "-r", "--name-only", BASE], cwd=ROOT, text=True).splitlines()
        cls.selected = [name for name in names if name.startswith(("tools/quest-builder/", "tools/quest-recovery/",
            "tools/quest-procedural-runtime/", "unity/GloomhavenVR.Quest/Assets/Quest/Editor/"))
            and name.endswith((".py", ".cs", ".c", ".h", ".json", ".meta")) and "/tests/" not in name]
        cls.old_builder = None
        for name in cls.selected:
            raw = subprocess.check_output(["git", "show", BASE + ":" + name], cwd=ROOT)
            cls.old.append({"path": name, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()})
            if name == identity.BUILDER: cls.old_builder = raw
        cls.new = [storage.record_file(ROOT / name, name) for name in cls.selected]
        cls.new += [storage.record_file(ROOT / name, name) for name in (
            "tools/quest-builder/runtime_overlay.py",) if name not in cls.selected]
        cls.new.sort(key=lambda row: row["path"])

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.old_source, self.new_source = self.root / "old", self.root / "new"
        for source, raw in ((self.old_source, self.old_builder),
                            (self.new_source, (ROOT / identity.BUILDER).read_bytes())):
            path = source / identity.BUILDER; path.parent.mkdir(parents=True); path.write_bytes(raw)
        common = {"target": "game", "recipe": 77, "game": {"key": "a" * 64, "unityVersion": "2021.3.5f1"},
                  "profile": {"displayName": "Fixture", "steamId": "76561197960265729"},
                  "proceduralBackend": "proton-arm64ec-fex"}
        self.before = {**common, "mod": {"files": self.old, "modBuild": 672}}
        self.after = {**common, "mod": {"files": self.new, "modBuild": 672}}

    def test_actual_release_original_recovery_and_complete_preparation_scopes_remain_equal(self):
        self.assertEqual(recovery_resume.recipe_key(self.before, 77), recovery_resume.recipe_key(self.after, 77))
        self.assertEqual(identity._scope(self.before, self.old_source, recovery_resume,
                                        original_prefix=True, completed=True),
                         identity._scope(self.after, self.new_source, recovery_resume,
                                        original_prefix=True, completed=True))
        # This baseline already carries the current Editor profile. Host-only
        # changes may retain an incomplete prefix too; actual Editor mutations
        # are tested separately by the closed27-owner overlay fixtures.
        self.assertEqual(identity._scope(self.before, self.old_source, recovery_resume, original_prefix=True),
                         identity._scope(self.after, self.new_source, recovery_resume, original_prefix=True))

    def test_actual_late_editor_overlay_retains_previous_whole_source_profile(self):
        edits = editor_overlay.changes(self.before, self.after)
        self.assertEqual([row[0] for row in edits], [])
        self.assertEqual(identity._completed_editor_rows([row for row in self.old if row["path"] in editor_overlay.TARGETS]),
                         identity._completed_editor_rows([row for row in self.new if row["path"] in editor_overlay.TARGETS]))

    def test_unknown_or_missing_staging_scheduler_loses_derived_cache_compatibility(self):
        for change in ("changed", "missing"):
            current = copy.deepcopy(self.after)
            name = "tools/quest-builder/staging_copy.py"
            if change == "changed": next(row for row in current["mod"]["files"] if row["path"] == name)["sha256"] = "0" * 64
            else: current["mod"]["files"] = [row for row in current["mod"]["files"] if row["path"] != name]
            with self.subTest(change=change):
                self.assertNotEqual(recovery_resume.recipe_key(self.before, 77), recovery_resume.recipe_key(current, 77))

    def test_unknown_bank_host_helper_is_not_hidden_from_preparation(self):
        current = copy.deepcopy(self.after)
        next(row for row in current["mod"]["files"] if row["path"] == identity.NATIVE_BANK_HOST_ROW["path"])["sha256"] = "0" * 64
        self.assertNotEqual(identity._scope(self.after, self.new_source, recovery_resume, original_prefix=True, completed=True),
                            identity._scope(current, self.new_source, recovery_resume, original_prefix=True, completed=True))

    def test_reviewed_large_control_selector_retains_previous_preparation_scope(self):
        actual = storage.record_file(ROOT / identity.NATIVE_BANK_HOST_ROW["path"],
                                     identity.NATIVE_BANK_HOST_ROW["path"])
        self.assertIn(actual, identity.NATIVE_BANK_HOST_ROWS)
        reference = identity._scope(self.after, self.new_source, recovery_resume,
                                    original_prefix=True, completed=True)
        for admitted in identity.NATIVE_BANK_HOST_ROWS:
            previous = copy.deepcopy(self.after)
            previous["mod"]["files"] = [admitted if row["path"] == admitted["path"] else row
                                         for row in previous["mod"]["files"]]
            with self.subTest(admitted=admitted["sha256"]):
                self.assertEqual(reference, identity._scope(previous, self.new_source, recovery_resume,
                                                            original_prefix=True, completed=True))

    def test_actual_update_producer_aliases_keep_original_payload_and_native_code(self):
        before = identity.builder_producer_digest(self.old_builder)
        after = identity.builder_producer_digest((ROOT / identity.BUILDER).read_bytes())
        self.assertEqual(before, after)
        self.assertEqual(update_driver.rebuild_reasons(self.new, self.old, payload_producer={"sha256": after},
                                                       previous_producer={"sha256": before}), [])
        self.assertEqual(update_driver._code_rows(self.old, normalizer=identity),
                         update_driver._code_rows(self.new, normalizer=identity))


if __name__ == "__main__": unittest.main()
