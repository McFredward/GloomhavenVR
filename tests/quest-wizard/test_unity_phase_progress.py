"""Real Editor task schedules remain children of their actual Unity invocation."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import ProgressParser
from state import Store
import wizard


def event(phase, owner, done=None, total=None, status="progress"):
    return "GHVRQ_PROGRESS " + json.dumps(dict(schema=1, phase=phase, operation=owner,
        done=done, total=total, unit="steps", status=status))


class UnityTaskParserTests(unittest.TestCase):
    def test_final_entry_cannot_reopen_closed_first_import(self):
        parser = ProgressParser()
        parser.parse(event("operation:unity-import", "unity-import", 1, 1, "complete"), "build.log")
        parser.parse(event("operation:unity-validation", "unity-validation", status="start"), "build.log")
        self.assertIsNone(parser.parse(event("operation:unity-import", "unity-import", 1, 1, "complete"), "unity-build-current.log"))
        task = parser.parse(event("unity-validation-tasks", "unity-validation", 7, 17), "unity-build-current.log")
        self.assertEqual((task["operation"], task["done"], task["total"]), ("unity-validation", 7, 17))

    def test_gradle_reports_actual_task_names_without_invented_graph_total(self):
        parser = ProgressParser()
        parser.parse(event("operation:player", "player", status="start"), "build.log")
        first = parser.parse("> Task :launcher:compileDebugJavaWithJavac", "unity-build-current.log")
        cached = parser.parse("> Task :unityLibrary:mergeDebugResources UP-TO-DATE", "unity-build-current.log")
        repeated = parser.parse("> Task :unityLibrary:mergeDebugResources UP-TO-DATE", "unity-build-current.log")
        self.assertEqual((first["done"], cached["done"], repeated["done"]), (1, 2, 2))
        self.assertEqual((cached["total"], cached["operation"], cached["unit"]), (None, "player", "tasks"))
        final = parser.parse("38 actionable tasks: 3 executed, 35 up-to-date", "unity-build-current.log")
        self.assertEqual((final["done"], final["total"], final["status"]), (38, 38, "progress"))
        self.assertIsNone(parser.parse("38 actionable tasks: 3 executed, 34 up-to-date", "unity-build-current.log"))
        self.assertIsNone(parser.parse("> Task :launcher:compileDebugJavaWithJavac", "arbitrary.log")["done"])


class UnityTaskHierarchyTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(); self.addCleanup(temp.cleanup)
        interval = mock.patch("state.PROGRESS_INTERVAL", 0); interval.start(); self.addCleanup(interval.stop)
        self.store = Store(Path(temp.name) / "owned")
        self.session = self.store.create(wizard.choices({"gameRoot": str(Path(temp.name) / "Game")}))["session"]
        self.store.begin_stage(self.session, "build", "same-input")

    def row(self): return next(row for row in self.store.load(self.session)["stages"] if row["id"] == "build")

    def report(self, phase, done, total, owner="unity-validation", **extra):
        return self.store.progress(self.session, "build", phase, done, total, "steps",
            "Validate startup Sprite geometry", operation=owner, **extra)

    def test_configuration_and_original_validation_move_one_continuous_parent(self):
        self.store.operation(self.session, "build", "unity-validation")
        first = self.report("unity-configuration", 3, 6)
        configured = self.report("unity-configuration", 6, 6, status="complete")
        self.assertGreater(configured["stagePercent"], first["stagePercent"])
        starting = self.report("unity-validation-tasks", 0, 17, status="start")
        seventh = self.report("unity-validation-tasks", 7, 17)
        self.assertGreater(seventh["stagePercent"], starting["stagePercent"])
        self.assertEqual((seventh["activeWork"]["done"], seventh["activeWork"]["total"]), (7, 17))
        final = self.report("unity-validation-tasks", 17, 17, status="complete")
        self.assertNotIn("unity-validation", self.row()["progressPlan"]["completed"])
        self.assertEqual(final["unityTask"]["done"], 17)
        self.store.operation(self.session, "build", "unity-validation", complete=True)
        self.assertIn("unity-validation", self.row()["progressPlan"]["completed"])

    def test_inner_sprite_and_public_editor_task_do_not_complete_validation_plan(self):
        self.store.operation(self.session, "build", "unity-validation")
        self.report("unity-configuration", 6, 6, status="complete")
        task = self.report("unity-validation-tasks", 7, 17)
        sprites = self.report("unity-validation-sprites", 8, 8, status="complete")
        public = self.report("unity-progress", 100, 100)
        self.assertGreater(sprites["stagePercent"], task["stagePercent"])
        self.assertEqual(public["stagePercent"], sprites["stagePercent"])
        self.assertNotIn("unity-validation", self.row()["progressPlan"]["completed"])

    def test_large_texture_parts_advance_parent_inside_current_task_only(self):
        self.store.operation(self.session, "build", "unity-validation")
        self.store.progress(self.session, "build", "unity-validation-tasks", 13, 17, "steps",
            "Validate original Campaign textures", operation="unity-validation")
        parts = ("unity-texture-cubes", "unity-texture-platform", "unity-texture-floating", "unity-texture-targets", "unity-texture-owners")
        previous = self.row()["progress"]["stagePercent"]
        for phase in parts:
            first = self.report(phase, 1, 10)
            self.assertGreater(first["stagePercent"], previous)
            last = self.report(phase, 10, 10, status="complete")
            self.assertGreater(last["stagePercent"], first["stagePercent"])
            self.assertEqual(last["activeWork"]["done"], 13)
            self.assertNotIn("unity-validation", self.row()["progressPlan"]["completed"])
            previous = last["stagePercent"]
        before = self.row()["progressPlan"]["fractions"]["unity-validation"]
        next_task = self.store.progress(self.session, "build", "unity-validation-tasks", 14, 17, "steps",
            "Validate original Campaign Sprite geometry", operation="unity-validation")
        self.assertGreater(self.row()["progressPlan"]["fractions"]["unity-validation"], before)
        empty = self.report("unity-validation-campaign-sprites", 0, 200)
        later = self.report("unity-validation-campaign-sprites", 100, 200)
        self.assertGreater(later["stagePercent"], empty["stagePercent"])
        self.assertEqual(later["activeWork"]["done"], 14)

    def test_failure_names_current_task_with_completed_predecessor_count(self):
        self.store.operation(self.session, "build", "unity-validation")
        self.report("unity-configuration", 6, 6, status="complete")
        self.report("unity-validation-tasks", 7, 17, status="failed")
        saved = self.store.load(self.session)
        next(row for row in saved["stages"] if row["id"] == "build")["status"] = "failed"
        self.store.save(saved)
        rows = [row for group in self.row()["progress"]["buildOverview"]["groups"] for row in group["operations"]]
        current = next(row for row in rows if row["id"] == "unity-validation")
        self.assertEqual(current["unityTask"]["status"], "failed")
        self.assertEqual(current["unityTask"]["detail"], "Validate startup Sprite geometry")
        self.assertEqual((current["unityTask"]["done"], current["unityTask"]["total"]), (7, 17))

    def test_closed_child_logs_cannot_replace_current_player_work(self):
        self.store.operation(self.session, "build", "unity-validation", complete=True)
        self.store.operation(self.session, "build", "content-bank", complete=True)
        self.store.operation(self.session, "build", "player")
        before = copy.deepcopy(self.row()["progress"])
        for phase, owner in (("unity-validation-tasks", "unity-validation"), ("unity-addressables-entries", "content-bank")):
            self.assertEqual(self.report(phase, 1, 4, owner=owner), before)
        self.assertEqual(self.row()["progressPlan"]["liveOperation"], "player")

    def test_native_success_is_a_child_and_byte_copy_does_not_close_player(self):
        self.store.operation(self.session, "build", "player")
        copied = self.report("unity-content-delivery", 4, 4, owner="player", status="complete")
        native = self.report("unity-player-native-result", 1, 1, owner="player", status="complete")
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])
        self.assertEqual(native["stagePercent"], copied["stagePercent"])
        self.store.operation(self.session, "build", "player", complete=True)
        self.assertIn("player", self.row()["progressPlan"]["completed"])

    def test_resume_keeps_frontier_without_old_live_task_or_failure(self):
        self.store.operation(self.session, "build", "unity-validation")
        before = self.report("unity-validation-tasks", 7, 17)["stagePercent"]
        self.store.progress(self.session, "build", "starting")
        self.store.operation(self.session, "build", "unity-validation")
        fresh = self.report("unity-validation-tasks", 1, 17)
        self.assertEqual(fresh["unityTask"]["done"], 1)
        self.assertGreaterEqual(fresh["stagePercent"], before)


if __name__ == "__main__": unittest.main()
