"""Exact delivered source aliases preserve completed imports across this repair."""
import copy
import hashlib
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import editor_overlay
import preparation_identity
import recovery_resume


class ImportMutationProfileTests(unittest.TestCase):
    def test_current_reader_profile_matches_actual_delivered_source_bytes(self):
        expected = recovery_resume.OBSERVATION_EDITOR_OVERLAY
        actual = {name: {"path": name, "size": (ROOT / name).stat().st_size,
                         "sha256": hashlib.sha256((ROOT / name).read_bytes()).hexdigest()}
                  for name in expected}
        self.assertEqual(actual, expected)

    def test_previous_and_fixed_reader_profiles_keep_the_same_original_producers(self):
        previous = recovery_resume.OBSERVATION_EDITOR_OVERLAY_PREVIOUS
        current = recovery_resume.OBSERVATION_EDITOR_OVERLAY
        canonical = recovery_resume.preparation_source_rows(list(previous.values()))
        for profile in (recovery_resume.OBSERVATION_EDITOR_IMPORT_PREVIOUS, current):
            self.assertEqual(canonical, recovery_resume.preparation_source_rows(list(profile.values())))
        for name in current:
            with self.subTest(changed=name):
                changed = copy.deepcopy(current)
                changed[name]["sha256"] = "f" * 64
                rows = recovery_resume.preparation_source_rows(list(changed.values()))
                self.assertIn(changed[name], rows)
                self.assertIn(changed["tools/quest-builder/script_remap_resume.py"], rows)

    def test_preceding_shipped_import_reader_profile_is_qualified_as_a_whole(self):
        previous = recovery_resume.OBSERVATION_EDITOR_IMPORT_PREVIOUS
        current = recovery_resume.OBSERVATION_EDITOR_OVERLAY
        self.assertEqual(recovery_resume.preparation_source_rows(list(previous.values())),
                         recovery_resume.preparation_source_rows(list(current.values())))
        for name in previous:
            with self.subTest(changed=name):
                changed = copy.deepcopy(previous)
                changed[name]["sha256"] = "f" * 64
                rows = recovery_resume.preparation_source_rows(list(changed.values()))
                self.assertIn(changed[name], rows)
                self.assertIn(changed["tools/quest-builder/script_remap_resume.py"], rows)

    def test_previous_profile_cannot_exclude_an_added_unknown_remap_reader(self):
        rows = list(recovery_resume.OBSERVATION_EDITOR_OVERLAY_PREVIOUS.values())
        extra = {"path": "tools/quest-builder/script_remap_resume.py", "size": 1, "sha256": "f" * 64}
        self.assertIn(extra, recovery_resume.preparation_source_rows([*rows, extra]))

    def test_both_complete_late_editor_profiles_have_the_same_closed_owner_scope(self):
        canonical = [pair[0] for pair in editor_overlay.REVIEWED.values()]
        for profile in editor_overlay.source_profiles():
            rows = list(profile.values())
            self.assertEqual(preparation_identity._completed_editor_rows(rows), canonical)
            changed = copy.deepcopy(rows)
            changed[-1]["sha256"] = "f" * 64
            self.assertEqual(preparation_identity._completed_editor_rows(changed), changed)
        actual = {name: {"path": name, "size": (ROOT / name).stat().st_size,
                         "sha256": hashlib.sha256((ROOT / name).read_bytes()).hexdigest()}
                  for name in editor_overlay.TARGETS}
        self.assertEqual(actual, editor_overlay.source_profiles()[0])

    def test_preceding_bindings_script_can_update_without_republishing_other_scripts(self):
        current = editor_overlay.source_profiles()[0]
        previous = editor_overlay.source_profiles()[1]
        result = editor_overlay.changes({"mod": {"files": list(previous.values())}},
                                       {"mod": {"files": list(current.values())}})
        self.assertEqual(result, [(editor_overlay.PREVIOUS_BINDINGS["path"],
                                  editor_overlay.PREVIOUS_BINDINGS,
                                  current[editor_overlay.PREVIOUS_BINDINGS["path"]])])


if __name__ == "__main__": unittest.main()
