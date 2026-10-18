"""Real import observations stay with their Editor job and durable frontier."""
import copy
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import stage_plan
from state import Store
import wizard


class UnityImportHierarchyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        interval = mock.patch("state.PROGRESS_INTERVAL", 0)
        interval.start()
        self.addCleanup(interval.stop)
        self.store = Store(Path(self.temp.name) / "owned")
        self.session = self.store.create(wizard.choices({"gameRoot": str(Path(self.temp.name) / "Game")}))["session"]
        self.store.begin_stage(self.session, "build", "input-a")

    def row(self):
        return next(row for row in self.store.load(self.session)["stages"] if row["id"] == "build")

    def imports(self, done, total=None, owner="unity-import", phase="unity-asset-import", **extra):
        return self.store.progress(self.session, "build", phase, done, total, "assets",
                                   "Actual Unity artifact: Character.asset", operation=owner, **extra)

    def test_import_is_before_api_with_unchanged_shares_and_one_owner_group(self):
        operations = stage_plan.PLANS["build"]
        self.assertLess(operations.index("unity-import"), operations.index("package-api"))
        self.assertEqual(stage_plan.BUILD_SHARES["unity-import"], 18)
        self.assertEqual(stage_plan.BUILD_SHARES["package-api"], 1)
        self.assertEqual(stage_plan.BUILD_GROUPS["code"], ("weave",))
        self.assertEqual(stage_plan.BUILD_GROUPS["import"], ("unity-import", "package-api", "unity-validation"))
        self.assertEqual(len(operations), len(set(operations)))

    def test_unknown_total_exposes_completed_operations_without_false_percentage(self):
        self.store.operation(self.session, "build", "unity-import")
        before = self.row()["progress"]["stagePercent"]
        value = self.imports(521)
        self.assertEqual((value["done"], value["total"], value["percent"]), (521, None, None))
        self.assertEqual(value["stagePercent"], before, "unknown totals cannot create a global fraction")
        self.assertEqual(value["unityImport"]["done"], 521)
        operations = [item for group in value["buildOverview"]["groups"] for item in group["operations"]]
        imported = next(item for item in operations if item["id"] == "unity-import")
        self.assertEqual(imported["import"]["done"], 521)
        self.assertFalse(imported["closed"])
        self.assertFalse(next(item for item in operations if item["id"] == "package-api")["closed"])

    def test_activity_and_repeated_refresh_cannot_reset_completed_import_count(self):
        self.store.operation(self.session, "build", "unity-import")
        first = self.imports(521)
        latest = self.imports(524)
        refreshed = self.imports(0, phase="unity-import-activity")
        self.assertEqual(refreshed["done"], 524)
        self.assertEqual(refreshed["unityImport"]["startedAt"], first["unityImport"]["startedAt"])
        self.assertEqual(refreshed["stagePercent"], latest["stagePercent"])

    def test_known_current_task_count_moves_global_bar_but_does_not_close_owner(self):
        self.store.operation(self.session, "build", "unity-import")
        first = self.imports(1, 4)
        second = self.imports(2, 4)
        self.assertGreater(second["stagePercent"], first["stagePercent"])
        ready = self.imports(4, 4, status="complete")
        self.assertNotIn("unity-import", self.row()["progressPlan"]["completed"])
        self.assertEqual(ready["operationStatus"], "complete")
        self.assertLess(ready["stagePercent"], 100)
        self.store.operation(self.session, "build", "unity-import", complete=True)
        self.store.operation(self.session, "build", "package-api")
        self.assertEqual(self.row()["progress"]["stageOperation"], "package-api")

    def test_closed_child_log_cannot_reopen_import_or_replace_actual_sdk_activity(self):
        self.store.operation(self.session, "build", "unity-import")
        self.imports(20)
        self.store.operation(self.session, "build", "unity-import", complete=True)
        self.store.operation(self.session, "build", "package-api")
        before = copy.deepcopy(self.row()["progress"])
        after = self.imports(200, owner="unity-import")
        self.assertEqual(after, before)
        self.assertEqual(self.row()["progress"], before)
        self.assertEqual(self.row()["progressPlan"]["liveOperation"], "package-api")

    def test_new_attempt_keeps_frontier_and_opens_fresh_import_observation(self):
        self.store.operation(self.session, "build", "unity-import")
        self.imports(200, 400)
        before = self.row()["progress"]["stagePercent"]
        self.store.progress(self.session, "build", "starting")
        self.store.operation(self.session, "build", "unity-import")
        fresh = self.imports(1)
        self.assertEqual(fresh["unityImport"]["done"], 1)
        self.assertGreaterEqual(fresh["stagePercent"], before)
        self.assertFalse(next(item for group in fresh["buildOverview"]["groups"] for item in group["operations"]
                              if item["id"] == "unity-import")["closed"])

    def test_status_read_repairs_a_legacy_stale_child_without_clearing_its_own_dictionary(self):
        self.store.operation(self.session, "build", "unity-import", complete=True)
        self.store.operation(self.session, "build", "package-api")
        saved = self.store.load(self.session)
        row = next(row for row in saved["stages"] if row["id"] == "build")
        row["progress"].update(phase="unity-asset-import", reportedOperation="unity-import", done=521,
                               total=None, percent=None, unit="assets")
        self.store.save(saved)
        recovered = self.row()["progress"]
        self.assertEqual(recovered["phase"], "operation:package-api")
        self.assertEqual(recovered["stageOperation"], "package-api")
        self.assertIn("buildOverview", recovered)
        self.assertIsNone(recovered["done"])

    def test_old_api_start_migrates_to_import_without_claiming_it_finished(self):
        saved = self.store.load(self.session)
        row = next(row for row in saved["stages"] if row["id"] == "build")
        before_import = list(stage_plan.PLANS["build"][:stage_plan.PLANS["build"].index("unity-import")])
        row["progressPlan"] = {"version": 2, "workRevision": 6, "workflow": "build", "current": "package-api",
                               "liveOperation": "package-api", "completed": before_import, "fractions": {}, "percent": 74.8}
        row["progress"].update(phase="operation:package-api", reportedOperation="package-api", operationStatus="start",
                               done=None, total=None, percent=None)
        self.store.save(saved)
        migrated = self.row()
        self.assertEqual(migrated["progress"]["stageOperation"], "unity-import")
        self.assertEqual(migrated["progress"]["stagePercent"], 74.8)
        self.assertNotIn("unity-import", migrated["progressPlan"]["completed"])
        self.assertNotIn("package-api", migrated["progressPlan"]["completed"])
        self.store.progress(self.session, "build", "starting")
        self.store.operation(self.session, "build", "unity-import", complete=True)
        sdk = self.store.operation(self.session, "build", "package-api")
        self.assertEqual(sdk["stageOperation"], "package-api")
        self.assertGreaterEqual(sdk["stagePercent"], 74.8)

    def test_actual_pack_passes_move_one_parent_fraction_and_never_close_it(self):
        self.store.operation(self.session, "build", "content-bank")
        first = self.store.progress(self.session, "build", "unity-progress", 10, 100, "steps", operation="content-bank")
        second = self.store.progress(self.session, "build", "unity-progress", 50, 100, "steps", operation="content-bank")
        self.assertGreater(second["stagePercent"], first["stagePercent"])
        self.assertLessEqual(self.row()["progressPlan"]["fractions"]["content-bank"], .5)
        previous = second["stagePercent"]
        for index, name in enumerate(stage_plan.CONTENT_PACK_SHARES):
            started = self.store.progress(self.session, "build", name, 0, 100, "bytes", operation="content-bank", status="start")
            part = self.store.progress(self.session, "build", name, 50, 100, "bytes", operation="content-bank", status="progress")
            self.assertGreater(part["stagePercent"], started["stagePercent"])
            done = self.store.progress(self.session, "build", name, 100, 100, "bytes", operation="content-bank", status="complete")
            self.assertGreater(done["stagePercent"], previous)
            self.assertEqual((done["activeWork"]["done"], done["activeWork"]["total"]), (index + 1, 4))
            self.assertNotIn("content-bank", self.row()["progressPlan"]["completed"])
            pack = next(item for group in done["buildOverview"]["groups"] for item in group["operations"] if item["id"] == "content-bank")["pack"]
            self.assertEqual((pack["done"], pack["total"]), (index + 1, 4))
            self.assertEqual(pack["passes"][index]["counter"]["unit"], "bytes")
            previous = done["stagePercent"]
        self.store.operation(self.session, "build", "content-bank", complete=True)
        self.assertIn("content-bank", self.row()["progressPlan"]["completed"])

    def test_retained_archive_zero_byte_final_boundary_is_one_closed_pack_pass(self):
        self.store.operation(self.session, "build", "content-bank")
        for name in tuple(stage_plan.CONTENT_PACK_SHARES)[:3]:
            self.store.progress(self.session, "build", name, 100, 100, "bytes", operation="content-bank", status="complete")
        ready = self.store.progress(self.session, "build", "native-content-final-hash", 0, 0, "bytes",
                                    operation="content-bank", status="complete")
        self.assertEqual(ready["activeWork"]["done"], 4)
        self.assertNotIn("content-bank", self.row()["progressPlan"]["completed"])
        self.store.operation(self.session, "build", "content-bank", complete=True)
        self.store.operation(self.session, "build", "player")
        before = self.row()["progress"]
        stale = self.store.progress(self.session, "build", "native-content-write", 50, 100, "bytes", operation="content-bank")
        self.assertEqual(stale, before)
        self.assertEqual(stale["stageOperation"], "player")

    def test_api_bind_output_and_publish_have_one_continuous_parent_share(self):
        self.store.operation(self.session, "build", "unity-import", complete=True)
        self.store.operation(self.session, "build", "package-api")
        previous = self.row()["progress"]["stagePercent"]
        for index, name in enumerate(stage_plan.PACKAGE_API_SHARES):
            started = self.store.progress(self.session, "build", name, 0, 20, "assemblies",
                                          operation="package-api", status="start")
            progressed = self.store.progress(self.session, "build", name, 10, 20, "assemblies",
                                             operation="package-api", status="progress")
            self.assertGreater(progressed["stagePercent"], started["stagePercent"])
            closed = self.store.progress(self.session, "build", name, 20, 20, "assemblies",
                                         operation="package-api", status="complete")
            self.assertGreater(closed["stagePercent"], previous)
            self.assertEqual((closed["activeWork"]["done"], closed["activeWork"]["total"]), (index + 1, 3))
            self.assertNotIn("package-api", self.row()["progressPlan"]["completed"])
            api = next(item for group in closed["buildOverview"]["groups"] for item in group["operations"] if item["id"] == "package-api")["api"]
            self.assertEqual((api["done"], api["total"]), (index + 1, 3))
            previous = closed["stagePercent"]
        self.store.operation(self.session, "build", "package-api", complete=True)
        self.store.operation(self.session, "build", "unity-validation")
        before = self.row()["progress"]
        stale = self.store.progress(self.session, "build", "package-api-bind", 10, 20, "assemblies", operation="package-api")
        self.assertEqual(stale, before)


if __name__ == "__main__": unittest.main()
