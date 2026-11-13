"""Read a full game's large preparation frontier without rereading its assets.

The output census and PC inputs are synthetic. The selector, immutable source
checks, full current Builder AST and complete observer profile are production
code. These checks do not replace the actual preparation/migration pipeline or
claim Windows timing: they pin the captured large-journal eligibility boundary.
"""
import copy
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import import_workspace
import preparation_identity as identity
import recovery_resume as recovery
import storage

CAPTURED_JOURNAL_BYTES = 25_864_174
PREPARATION_LIMIT = 64 * 1024 * 1024
SCHEDULE = [
    ("base-project", "project-files"),
    *[(name, "startup-content") for name in (
        "post-effects", "loading-resources", "startup-movies", "native-sprites",
        "loading-sprite", "startup-audio", "ui-recipes", "startup-ui",
        "startup-blur", "dlc-selection", "file-extras")],
    ("native-runtime", "native-runtime"), ("bundled-audio", "audio"),
    *[(name, "textures") for name in (
        "native-cubemaps", "ordinary-texture-audit", "native-texture2d")],
    ("campaign-compute", "graphics"), ("campaign-shaders", "graphics"),
    *[(name, "mod-banks") for name in (
        "startup-archive", "archive-cleanup", "package-settings", "mod-resource-banks")],
    *[(name, "preparation-contracts") for name in (
        "compiler-contracts", "case-paths", "script-orders", "final-settings")],
]


class LargePreparationJournalTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Every row has distinct ordinary path, size and SHA provenance; no
        # padding field is used to artificially inflate a tiny receipt.
        cls.output_count = 150_000
        steps = [{"name": name, "operation": operation, "outputs": []}
                 for name, operation in SCHEDULE]
        for number in range(cls.output_count):
            relative = ("Assets/QuestOriginalCampaign/Native/RecoveredObjects/"
                        f"collection_{number // 1000:04d}/object_{number:08d}.asset")
            steps[min(number * len(steps) // cls.output_count, len(steps) - 1)]["outputs"].append({
                "path": relative, "size": number + 1,
                "sha256": hashlib.sha256(relative.encode()).hexdigest()})
        cls.large_steps = steps

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="quest-large-journal-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.output, self.source = self.root / "output", self.root / "current-source"
        for name in (identity.BUILDER, identity.IDENTITY, identity.RECIPE_IDENTITY,
                     *recovery.OBSERVATION_COMPLETED_METADATA,
                     "tools/quest-builder/campaign_shaders.py"):
            destination = self.source / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / name, destination)
        self.builder_bytes = (self.source / identity.BUILDER).read_bytes()
        self.template_name = "tools/quest-builder/project/Assets/Quest/Runtime/Probe.cs"
        self.put(self.source / self.template_name, b"// Synthetic consumed template.\n")
        self.put(self.source / "src/GloomhavenVR/Net/RuntimeOnly.cs", b"// Runtime revision one.\n")
        self.game = self.root / "original-game"
        self.put(self.game / "resources.assets", b"synthetic owned original payload")
        game_files = storage.inventory(self.game)
        self.base = {
            "schema": 1, "recipe": 1, "target": "game",
            "proceduralBackend": "fex-proton9",
            "profile": {"displayName": "DUMMY TEST ACCOUNT", "steamId64": "76561198000000000"},
            "game": {"files": game_files, "key": storage.value_hash({"files": game_files}),
                     "unityVersion": "2021.3.5f1"},
        }
        self.previous = self.inputs()
        self.previous_source = self.output / "inputs/mod" / self.previous["mod"]["key"]
        shutil.copytree(self.source, self.previous_source)
        self.project = self.output / "projects" / import_workspace.workspace_key(self.previous, "game")
        self.library = self.project / "Library/already-imported"
        self.asset = self.project / "Assets/retained-native.asset"
        self.put(self.library, b"retained expensive Unity import")
        self.put(self.asset, b"retained prepared original asset")
        self.asset_states = {path: (path.read_bytes(), path.stat().st_mtime_ns)
                             for path in (self.asset, self.library, self.game / "resources.assets")}
        self.journal = self.output / "cache/prepare-resume" / self.project.name / "journal.json"
        self.value = {
            "schema": 1, "owner": "Quest preparation substage journal",
            "project": self.project.relative_to(self.output).as_posix(),
            "inputKey": self.previous["inputKey"], "target": "game", "recipe": 1,
            "sources": {}, "pending": None, "steps": self.large_steps,
        }
        storage.write_json(self.journal, self.value)
        self.journal_state = (storage.digest(self.journal), self.journal.stat().st_mtime_ns,
                              self.journal.stat().st_size)
        self.assertGreaterEqual(self.journal_state[2], CAPTURED_JOURNAL_BYTES)
        self.assertLess(self.journal_state[2], PREPARATION_LIMIT)
        self.assertTrue(identity._completed_game_preparation(self.value, "game"))

    @staticmethod
    def put(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)

    def inputs(self, base=None):
        value = copy.deepcopy(self.base if base is None else base)
        value.pop("inputKey", None)
        files = storage.inventory(self.source)
        value["mod"] = {"files": files, "key": storage.value_hash({"files": files}),
                        "modBuild": 654, "commit": "synthetic-source654", "dirty": False}
        value["inputKey"] = storage.value_hash(value)
        storage.write_json(self.output / "manifests" / (value["inputKey"] + ".json"), value)
        return value

    def updated_runtime(self):
        self.put(self.source / "src/GloomhavenVR/Net/RuntimeOnly.cs", b"// Runtime revision two.\n")
        value = self.inputs()
        self.assertNotEqual(value["inputKey"], self.previous["inputKey"])
        return value

    def select(self, inputs):
        real_open = Path.open
        forbidden = (self.project, self.game)
        def bounded_open(path, *args, **kwargs):
            if any(path == root or root in path.parents for root in forbidden):
                raise AssertionError("Eligibility must not read retained asset or Unity Library payloads: " + str(path))
            return real_open(path, *args, **kwargs)
        with patch.object(Path, "open", bounded_open):
            return identity.rebind_key(self.output, self.project, inputs, self.source,
                                       target="game", recipe=1, recovery=recovery)

    def assert_unchanged(self):
        self.assertEqual((storage.digest(self.journal), self.journal.stat().st_mtime_ns,
                          self.journal.stat().st_size), self.journal_state)
        for path, state in self.asset_states.items():
            self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), state)

    def test_same_input_reads_captured_scale_journal_and_keeps_all_payloads(self):
        self.assertEqual(identity.MAX_JOURNAL_BYTES, PREPARATION_LIMIT)
        self.assertIsNone(self.select(self.previous))
        self.assert_unchanged()

    def test_runtime_only_rebind_reads_all_27_closed_owners_without_asset_scan(self):
        self.assertEqual(self.select(self.updated_runtime()), self.previous["inputKey"])
        self.assert_unchanged()

    def test_larger_preparation_bound_does_not_raise_generic_manifest_limit(self):
        self.assertEqual(recovery.MAX_JSON_BYTES, 16 * 1024 * 1024)
        with patch.object(Path, "open", side_effect=AssertionError("Oversized generic receipt must not open")):
            with self.assertRaisesRegex(storage.BuildError, "oversized.*limit 16777216 bytes"):
                recovery._read(self.journal)
        self.assert_unchanged()

    def test_wrong_journal_identity_is_rejected_after_large_read_without_changes(self):
        current = self.updated_runtime()
        for field, changed in (("owner", "another owner"), ("project", "projects/another-project"),
                               ("target", "startup"), ("recipe", True)):
            with self.subTest(field=field):
                value = {**self.value, field: changed}
                storage.write_json(self.journal, value)
                self.journal_state = (storage.digest(self.journal), self.journal.stat().st_mtime_ns,
                                      self.journal.stat().st_size)
                with self.assertRaisesRegex(storage.BuildError, "Preparation journal identity differs"):
                    self.select(current)
                self.assert_unchanged()

    def test_game_profile_template_graphics_helper_and_unknown_ast_stay_scoped(self):
        baseline = self.updated_runtime()
        for change in ("game", "profile", "template", "graphics", "metadata-helper", "unknown-builder-consumer"):
            with self.subTest(change=change):
                value = copy.deepcopy(baseline)
                changed_path = None
                if change == "game":
                    value["game"]["files"][0]["sha256"] = "f" * 64
                    value["game"]["key"] = storage.value_hash({"files": value["game"]["files"]})
                elif change == "profile":
                    value["profile"]["displayName"] = "DUMMY DIFFERENT ACCOUNT"
                else:
                    changed_path = self.source / {
                        "template": self.template_name,
                        "graphics": "tools/quest-builder/campaign_shaders.py",
                        "metadata-helper": identity.METADATA_IDENTITY,
                        "unknown-builder-consumer": identity.BUILDER,
                    }[change]
                    original = changed_path.read_bytes()
                    addition = (b"\ndef unknown_prepare_consumer(project):\n    project.unlink()\n"
                                if change == "unknown-builder-consumer" else b"\n# Unknown producer change.\n")
                    changed_path.write_bytes(original + addition)
                try:
                    self.assertIsNone(self.select(self.inputs(value)))
                    self.assert_unchanged()
                finally:
                    if changed_path is not None: changed_path.write_bytes(original)

    def test_unknown_immutable_builder_bytes_are_not_hidden_by_larger_bound(self):
        current = self.updated_runtime()
        (self.previous_source / identity.BUILDER).write_bytes(b"corrupt preceding immutable Builder")
        with self.assertRaisesRegex(storage.BuildError, "differs from its immutable source manifest"):
            self.select(current)
        self.assert_unchanged()

    def test_pending_or_missing_final_owner_never_claims_complete_frontier(self):
        current = self.updated_runtime()
        for change in ("pending", "missing-final-owner"):
            with self.subTest(change=change):
                value = {**self.value}
                if change == "pending":
                    value["pending"] = {"name": "final-settings", "operation": "preparation-contracts", "undo": []}
                else:
                    value["steps"] = self.large_steps[:-1]
                storage.write_json(self.journal, value)
                self.journal_state = (storage.digest(self.journal), self.journal.stat().st_mtime_ns,
                                      self.journal.stat().st_size)
                self.assertIsNone(self.select(current))
                self.assert_unchanged()

    def test_over_64_mib_sparse_journal_fails_before_any_content_read(self):
        with self.journal.open("wb") as stream: stream.truncate(PREPARATION_LIMIT + 1)
        before = (self.journal.stat().st_size, self.journal.stat().st_mtime_ns)
        with patch.object(Path, "open", side_effect=AssertionError("Oversized preparation evidence must not open")):
            with self.assertRaisesRegex(storage.BuildError, "oversized.*67108865 bytes; limit 67108864 bytes"):
                self.select(self.previous)
        self.assertEqual((self.journal.stat().st_size, self.journal.stat().st_mtime_ns), before)

    def test_large_malformed_json_is_actionable_and_does_not_change_payloads(self):
        with self.journal.open("r+b") as stream: stream.write(b"!")
        self.journal_state = (storage.digest(self.journal), self.journal.stat().st_mtime_ns,
                              self.journal.stat().st_size)
        with self.assertRaisesRegex(storage.BuildError, "unreadable.*JSONDecodeError"):
            self.select(self.previous)
        self.assert_unchanged()

    def test_linked_preparation_journal_is_rejected_before_content_read(self):
        outside = self.root / "other-journal.json"
        self.journal.rename(outside)
        try:
            self.journal.symlink_to(outside)
        except OSError as error:
            self.skipTest("This filesystem does not support test symlinks: " + str(error))
        with patch.object(Path, "open", side_effect=AssertionError("Linked preparation evidence must not open")):
            with self.assertRaises(storage.BuildError): self.select(self.previous)
        self.assertEqual(storage.digest(outside), self.journal_state[0])
        self.assertTrue(self.journal.is_symlink())


if __name__ == "__main__": unittest.main()
