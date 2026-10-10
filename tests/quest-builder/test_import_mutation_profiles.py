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
        for profile in (recovery_resume.OBSERVATION_EDITOR_IMPORT_PREVIOUS,
                        recovery_resume.OBSERVATION_EDITOR_OWNERS_PREVIOUS,
                        recovery_resume.OBSERVATION_TARGETED_REPAIR_PREVIOUS, current):
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

    def test_preceding_profiles_cannot_hide_added_repair_or_editor_access_helpers(self):
        for profile in (recovery_resume.OBSERVATION_EDITOR_OVERLAY_PREVIOUS,
                        recovery_resume.OBSERVATION_EDITOR_IMPORT_PREVIOUS,
                        recovery_resume.OBSERVATION_EDITOR_OWNERS_PREVIOUS):
            for name in ("preparation_repair", "project_access"):
                with self.subTest(profile=profile, helper=name):
                    extra = {"path": "tools/quest-builder/" + name + ".py",
                             "size": 1, "sha256": "f" * 64}
                    self.assertIn(extra, recovery_resume.preparation_source_rows([*profile.values(), extra]))

    def test_previous_profiles_cannot_hide_an_unknown_unity_work_observer(self):
        extra = {"path": "tools/quest-builder/unity_work.py", "size": 1, "sha256": "f" * 64}
        for profile in (recovery_resume.OBSERVATION_EDITOR_OVERLAY_PREVIOUS,
                        recovery_resume.OBSERVATION_EDITOR_IMPORT_PREVIOUS,
                        recovery_resume.OBSERVATION_EDITOR_OWNERS_PREVIOUS,
                        recovery_resume.OBSERVATION_TARGETED_REPAIR_PREVIOUS):
            self.assertIn(extra, recovery_resume.preparation_source_rows([*profile.values(), extra]))

    def test_only_exact_reviewed_builder_repair_orchestration_keeps_producer_identity(self):
        from unittest.mock import patch
        raw = (ROOT / "tools/quest-builder/builder.py").read_bytes()
        for prefix in (False, True):
            observed, previous = preparation_identity.BUILDER_UNITY_WORK_AST[prefix]
            with patch.dict(preparation_identity.BUILDER_UNITY_WORK_AST,
                            {prefix: ("f" * 64, previous)}):
                self.assertEqual(preparation_identity.builder_producer_digest(raw, original_prefix=prefix), observed)
            self.assertEqual(preparation_identity.builder_producer_digest(raw, original_prefix=prefix), previous)
        changed = raw.replace(b'repair_guard=lambda: project_access.wait_for_editor(project, build_progress)',
                              b'repair_guard=lambda: project_access.wait_for_editor(project, None)')
        self.assertNotEqual(changed, raw)
        for prefix in (False, True):
            previous = preparation_identity.BUILDER_TARGETED_REPAIR_AST[prefix][1]
            self.assertNotEqual(preparation_identity.builder_producer_digest(changed, original_prefix=prefix), previous)

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

    def test_preceding_complete_task_profile_updates_only_three_existing_editor_scripts(self):
        current, previous = editor_overlay.source_profiles()[0], editor_overlay.source_profiles()[2]
        result = editor_overlay.changes({"mod": {"files": list(previous.values())}},
                                       {"mod": {"files": list(current.values())}})
        self.assertEqual({row[0] for row in result}, set(editor_overlay.PREVIOUS_TASK_SCRIPTS))

    def test_completed_loading_producer_alias_requires_exact_bytes_and_whole_editor_profile(self):
        actual = ROOT / preparation_identity.LOADING_DRAWING_FIXED["path"]
        self.assertEqual(preparation_identity.LOADING_DRAWING_FIXED,
            {"path": preparation_identity.LOADING_DRAWING_FIXED["path"], "size": actual.stat().st_size,
             "sha256": hashlib.sha256(actual.read_bytes()).hexdigest()})
        rows = [*editor_overlay.source_profiles()[0].values(), preparation_identity.LOADING_DRAWING_FIXED]
        self.assertIn(preparation_identity.LOADING_DRAWING_PREVIOUS, preparation_identity._completed_editor_rows(rows))
        changed = copy.deepcopy(rows); changed[-1]["sha256"] = "f" * 64
        self.assertIn(changed[-1], preparation_identity._completed_editor_rows(changed))
        incomplete = [preparation_identity.LOADING_DRAWING_FIXED]
        self.assertEqual(preparation_identity._completed_editor_rows(incomplete), incomplete)


if __name__ == "__main__": unittest.main()
