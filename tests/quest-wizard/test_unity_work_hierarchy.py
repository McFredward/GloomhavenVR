"""Count scoped Unity work without closing a command at a nested counter's 100%."""
import copy
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from state import Store
import wizard


class UnityWorkHierarchyTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(); self.addCleanup(temp.cleanup)
        patch = mock.patch("state.PROGRESS_INTERVAL", 0); patch.start(); self.addCleanup(patch.stop)
        self.store = Store(Path(temp.name) / "owned")
        self.session = self.store.create(wizard.choices({"gameRoot": str(Path(temp.name) / "Game")}))["session"]
        self.store.begin_stage(self.session, "build", "same-input")

    def row(self): return next(row for row in self.store.load(self.session)["stages"] if row["id"] == "build")

    def report(self, phase, done=0, total=1, unit="steps", owner="player", detail="Actual counted work", status="progress"):
        return self.store.progress(self.session, "build", phase, done, total, unit, detail,
            operation=owner, status=status)

    def start(self, owner="player", retained=0, total=100):
        self.store.operation(self.session, "build", owner)
        self.report("unity-work-invocation", 0, 1, "invocations", owner, status="start")
        return self.report("unity-work-plan", retained, total, "assets", owner,
            "[asset-coverage] Known project/package assets", "start")

    def work(self, value, owner="player"):
        return next(row for group in value["buildOverview"]["groups"] for row in group["operations"] if row["id"] == owner)["unityWork"]

    def test_all_known_unity_invocations_have_finite_logical_phase_plans(self):
        for owner in ("unity-import", "package-api", "unity-validation", "content-bank", "player"):
            with self.subTest(owner=owner):
                value = self.start(owner)
                work = self.work(value, owner)
                self.assertEqual((work["done"], work["total"]), (0, 3))
                self.assertEqual(work["phases"][-1]["counter"]["total"], 1)
                self.assertFalse(work["phases"][-1]["closed"])

    def test_known_asset_coverage_moves_parent_but_complete_census_does_not_close_invocation(self):
        first = self.start("unity-import", 20, 100)
        last = self.report("unity-asset-import", 100, 100, "assets", "unity-import", "[asset-coverage] All witnessed assets")
        self.assertGreater(last["stagePercent"], first["stagePercent"])
        self.assertEqual((last["activeWork"]["unity"]["done"], last["activeWork"]["unity"]["total"]), (0, 3))
        self.assertNotIn("unity-import", self.row()["progressPlan"]["completed"])
        ready = self.report("unity-work-invocation", 1, 1, "invocations", "unity-import", status="complete")
        self.assertEqual(self.work(ready, "unity-import")["done"], 3)
        self.assertNotIn("unity-import", self.row()["progressPlan"]["completed"])

    def test_native_batch_denominator_does_not_inherit_larger_coverage_numerator(self):
        self.start("unity-import", 900, 1000)
        native = self.report("unity-asset-import", 2, 10, "assets", "unity-import", "[unity-native-total] Importing 2 assets of 10")
        self.assertEqual((native["done"], native["total"], native["percent"]), (2, 10, 20.))
        next_batch = self.report("unity-asset-import", 1, 5, "assets", "unity-import", "[unity-native-total] Importing 1 asset of 5")
        self.assertEqual((next_batch["done"], next_batch["total"]), (1, 5))
        coverage = self.report("unity-asset-import", 920, 1000, "assets", "unity-import", "[asset-coverage] Known assets")
        self.assertEqual((coverage["done"], coverage["total"]), (920, 1000))
        self.assertGreaterEqual(coverage["stagePercent"], native["stagePercent"])

    def test_refresh_heartbeat_keeps_known_asset_denominator(self):
        self.start("unity-import", 500, 1000)
        value = self.report("unity-import-activity", 0, None, "assets", "unity-import", "Refreshing asset database")
        self.assertEqual((value["done"], value["total"]), (500, 1000))

    def test_method_schedule_and_final_return_remain_distinct(self):
        self.start("unity-validation", 100, 100)
        configured = self.report("unity-configuration", 6, 6, owner="unity-validation", status="complete")
        self.assertEqual(self.work(configured, "unity-validation")["done"], 1)
        first = self.report("unity-validation-tasks", 7, 17, owner="unity-validation", detail="Validate startup Sprite geometry")
        child = self.report("unity-validation-sprites", 12, 20, "sprites", "unity-validation")
        self.assertGreater(child["stagePercent"], first["stagePercent"])
        checked = self.report("unity-validation-tasks", 17, 17, owner="unity-validation", status="complete")
        self.assertEqual(self.work(checked, "unity-validation")["done"], 1)
        method = self.report("unity-work-stage:method", 1, 1, owner="unity-validation", status="complete")
        self.assertEqual(self.work(method, "unity-validation")["done"], 2)
        ready = self.report("unity-work-invocation", 1, 1, "invocations", "unity-validation", status="complete")
        self.assertEqual(self.work(ready, "unity-validation")["done"], 3)
        self.assertNotIn("unity-validation", self.row()["progressPlan"]["completed"])

    def test_repeated_bee_graph_and_shader_pass_reset_only_secondary_counter(self):
        self.start()
        self.report("unity-work-stage:method", status="start")
        first = self.report("bee-actions:native:200:1", 100, 200, "actions")
        graph = self.report("bee-actions:native:200:1", 200, 200, "actions", status="complete")
        restart = self.report("bee-actions:native:300:2", 0, 300, "actions")
        self.assertEqual(graph["stagePercent"], first["stagePercent"], "A completed local copy graph cannot consume the whole native-build budget")
        self.assertEqual(restart["stagePercent"], graph["stagePercent"])
        native = next(part for part in self.work(restart)["phases"][1]["parts"] if part["id"] == "native")
        self.assertEqual((native["localCounter"]["done"], native["localCounter"]["total"]), (0, 300))
        self.assertEqual(native["percent"], 0.)
        self.assertFalse(native["closed"])
        previous = restart["stagePercent"]
        package = self.report("unity-gradle-tasks", 20, 40, "tasks")
        self.assertGreater(package["stagePercent"], previous)
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])

    def test_task_local_public_100_percent_does_not_consume_whole_player_share(self):
        self.start()
        self.report("unity-work-stage:method", status="start")
        self.report("unity-player-scenes", 1, 10, "scenes")
        value = self.report("unity-progress", 100, 100, "steps", detail="Unity: current task is done")
        self.assertLess(self.row()["progressPlan"]["fractions"]["player"], .3)
        self.assertFalse(self.work(value)["phases"][1]["closed"])

    def test_long_indivisible_milestone_has_explicit_logical_denominator(self):
        self.start()
        self.report("unity-work-stage:method", status="start")
        value = self.report("unity-il2cpp", None, None, None, detail="Invoking il2cpp")
        self.assertEqual((value["done"], value["total"], value["unit"], value["percent"]), (0, 1, "tasks", 0.))
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])

    def test_retry_keeps_parent_high_water_and_discards_old_invocation_detail(self):
        self.start(retained=20)
        self.report("unity-work-stage:method", status="start")
        old = self.report("bee-actions:first:200:1", 100, 200, "actions")
        self.report("unity-work-invocation", 0, 1, "invocations", status="failed")
        fresh = self.start(retained=10)
        self.assertGreaterEqual(fresh["stagePercent"], old["stagePercent"])
        self.assertEqual(self.work(fresh)["done"], 0)
        self.assertEqual(next(part for part in self.work(fresh)["phases"][1]["parts"] if part["id"] == "native")["counter"]["done"], 0)

    def test_finished_foreign_invocation_cannot_replace_current_owner(self):
        self.start("unity-import")
        self.report("unity-work-invocation", 1, 1, "invocations", "unity-import", status="complete")
        self.store.operation(self.session, "build", "unity-import", complete=True)
        self.store.operation(self.session, "build", "package-api")
        previous = copy.deepcopy(self.row()["progress"])
        self.assertEqual(self.report("unity-work-plan", 20, 100, "assets", "unity-import", status="start"), previous)

    def test_status_poll_does_not_restart_same_invocation(self):
        self.start()
        first = copy.deepcopy(self.row()["progressPlan"]["unityWork"])
        self.assertEqual(self.row()["progressPlan"]["unityWork"], first)
        self.report("unity-work-stage:method", status="start")
        counter = self.report("unity-gradle-tasks", 10, 40, "tasks")
        before = copy.deepcopy(self.row()["progressPlan"]["unityWork"])
        self.assertEqual(self.row()["progressPlan"]["unityWork"], before)
        self.assertEqual(self.row()["progress"]["stagePercent"], counter["stagePercent"])

    def test_sdk_compilation_is_counted_inside_real_first_import(self):
        self.start("unity-import")
        first = self.report("unity-sdk-tasks", 1, 5, owner="unity-import")
        last = self.report("unity-sdk-tasks", 4, 5, owner="unity-import")
        self.assertGreater(last["stagePercent"], first["stagePercent"])
        self.assertEqual(self.work(last, "unity-import")["phases"][1]["parts"][0]["counter"]["total"], 5)
        self.assertNotIn("unity-import", self.row()["progressPlan"]["completed"])

    def test_one_build_process_inherits_asset_readiness_for_actual_next_owners_only(self):
        self.start("unity-validation", 90, 100)
        self.report("unity-work-stage:method", owner="unity-validation", status="start")
        self.store.operation(self.session, "build", "unity-validation", complete=True)
        content = self.store.operation(self.session, "build", "content-bank")
        work = self.work(content, "content-bank")
        self.assertEqual((work["done"], work["total"]), (1, 3))
        self.assertEqual(work["phases"][0]["status"], "reused")
        self.assertEqual(work["phases"][0]["counter"]["done"], 90)
        self.store.operation(self.session, "build", "content-bank", complete=True)
        player = self.store.operation(self.session, "build", "player")
        self.assertEqual(self.work(player)["done"], 1)

    def test_python_api_binder_does_not_inherit_a_fictitious_sdk_invocation(self):
        self.start("unity-import")
        self.report("unity-work-invocation", 1, 1, "invocations", "unity-import", status="complete")
        self.store.operation(self.session, "build", "unity-import", complete=True)
        value = self.store.operation(self.session, "build", "package-api")
        api = next(row for group in value["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "package-api")
        self.assertNotIn("unityWork", api)

    def test_scoped_public_task_ids_are_children_and_cannot_close_player(self):
        self.start()
        self.report("unity-work-stage:method", status="start")
        self.report("bee-actions:code:200:1", 50, 200, "actions")
        first = self.report("unity-progress:51:-1", 30, 100, "steps")
        last = self.report("unity-progress:51:-1", 100, 100, "steps", status="complete")
        self.assertEqual(last["stagePercent"], first["stagePercent"])
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])
        self.assertFalse(self.work(last)["phases"][1]["closed"])

    def test_shader_header_has_logical_task_before_variant_denominator_arrives(self):
        self.start()
        value = self.report("unity-shader-compile", None, None, "variants", detail='Compiling shader "Original" pass "FORWARD"')
        compiler = next(row for group in value["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "player")["compiler"]
        self.assertEqual((compiler["done"], compiler["total"], compiler["unit"], compiler["percent"]), (0, 1, "tasks", 0.))
        variants = self.report("unity-shader-compile", 4, 10, "variants")
        compiler = next(row for group in variants["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "player")["compiler"]
        self.assertEqual((compiler["done"], compiler["total"], compiler["unit"]), (4, 10, "variants"))

    def test_scoped_shader_task_id_remains_visible_and_does_not_close_player(self):
        self.start()
        self.report("unity-work-stage:method", status="start")
        value = self.report("unity-shader-task:81:70", 3, 10, "steps")
        compiler = next(row for group in value["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "player")["compiler"]
        self.assertEqual((compiler["phase"], compiler["done"], compiler["total"]), ("unity-shader-task:81:70", 3, 10))
        self.report("unity-shader-task:81:70", 10, 10, "steps", status="complete")
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])

    def test_completed_addressables_parts_stay_green_after_later_audit_failure(self):
        self.start("content-bank")
        self.report("unity-work-stage:method", owner="content-bank", status="start")
        # Actual B664 capture215313 reported these final entry/Shader counts.
        for phase, total, unit in (("unity-addressables-keys", 6531, "assets"),
                                  ("unity-addressables-entries", 6531, "assets"),
                                  ("unity-addressables-shaders", 688, "shaders"),
                                  ("unity-addressables-shader-roots", 688, "shaders")):
            self.report(phase, total, total, unit, "content-bank", status="complete")
        self.report("unity-shader-compile", 128, 128, "variants", "content-bank")
        self.report("unity-addressables-build", 1, 1, owner="content-bank", status="complete")
        self.report("unity-native-shader-audit", 0, 5, "bundles", "content-bank", status="start")
        saved = self.store.load(self.session)
        next(row for row in saved["stages"] if row["id"] == "build")["status"] = "failed"
        self.store.save(saved)
        value = self.row()["progress"]
        parts = {row["id"]: row for row in self.work(value, "content-bank")["phases"][1]["parts"]}
        for name in ("catalog", "shaders", "build"):
            self.assertEqual((parts[name]["closed"], parts[name]["status"], parts[name]["percent"]), (True, "complete", 100.))
        self.assertEqual((parts["audit"]["closed"], parts["audit"]["status"]), (False, "failed"))
        self.assertEqual((parts["postprocess"]["closed"], parts["postprocess"]["status"]), (False, "pending"))
        owner = next(row for group in value["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "content-bank")
        self.assertEqual(owner["compiler"]["status"], "complete")
        self.assertNotIn("content-bank", self.row()["progressPlan"]["completed"])

    def test_n_over_n_without_terminal_or_missing_sibling_cannot_close_part(self):
        self.start("content-bank")
        self.report("unity-work-stage:method", owner="content-bank", status="start")
        self.report("unity-addressables-keys", 4, 4, owner="content-bank", status="complete")
        pending = self.report("unity-addressables-entries", 4, 4, owner="content-bank")
        parts = {row["id"]: row for row in self.work(pending, "content-bank")["phases"][1]["parts"]}
        self.assertFalse(parts["catalog"]["closed"])
        self.assertLess(parts["catalog"]["percent"], 100.)
        complete = self.report("unity-addressables-entries", 4, 4, owner="content-bank", status="complete")
        self.assertTrue(self.work(complete, "content-bank")["phases"][1]["parts"][0]["closed"])
        root_only = self.report("unity-addressables-shader-roots", 688, 688, "shaders", "content-bank", status="complete")
        self.assertFalse(self.work(root_only, "content-bank")["phases"][1]["parts"][1]["closed"])
        native = self.report("unity-addressables-build", 1, 1, owner="content-bank")
        self.assertFalse(self.work(native, "content-bank")["phases"][1]["parts"][2]["closed"])

    def test_audit_bytes_and_objects_move_parent_without_consuming_receipt_gate(self):
        self.start("content-bank")
        self.report("unity-work-stage:method", owner="content-bank", status="start")
        self.report("unity-addressables-build", 1, 1, owner="content-bank", status="complete")
        base = self.report("unity-native-shader-audit", 0, 7, "bundles", "content-bank", status="start")
        previous = base["stagePercent"]
        for done in (1, 100, 1000, 5000, 10000):
            value = self.report("unity-native-shader-audit-read", done, 10000, "bytes", "content-bank")
            self.assertGreater(value["stagePercent"], previous)
            previous = value["stagePercent"]
        read = self.report("unity-native-shader-audit-read", 10000, 10000, "bytes", "content-bank", status="complete")
        for done in (1, 2, 8, 14, 20):
            value = self.report("unity-native-shader-audit-objects", done, 20, "objects", "content-bank")
            self.assertGreater(value["stagePercent"], previous)
            previous = value["stagePercent"]
        read_done = self.report("unity-native-shader-audit", 7, 7, "bundles", "content-bank")
        part = self.work(read_done, "content-bank")["phases"][1]["parts"][3]
        self.assertFalse(part["closed"])
        self.assertLess(part["percent"], 91.)
        receipt = self.report("unity-native-shader-audit", 7, 7, "bundles", "content-bank", status="complete")
        part = self.work(receipt, "content-bank")["phases"][1]["parts"][3]
        self.assertEqual((part["closed"], part["status"], part["percent"]), (True, "complete", 100.))
        self.assertEqual(part["counter"]["total"], 7)
        self.assertNotIn("content-bank", self.row()["progressPlan"]["completed"])

    def test_successful_native_report_closes_its_parts_but_not_final_player_evidence(self):
        self.start()
        self.report("unity-work-stage:method", status="start")
        self.report("unity-player-scenes", 13, 13, "scenes")
        self.report("unity-il2cpp", 1, 1)
        self.report("bee-actions:native:200:1", 200, 200, "actions")
        self.report("unity-gradle-tasks", 40, 40, "tasks")
        observed = self.report("unity-player-native-result", 1, 1)
        parts = {part["id"]: part for part in self.work(observed)["phases"][1]["parts"]}
        self.assertFalse(parts["scenes"]["closed"])
        self.assertFalse(parts["native"]["closed"])
        returned = self.report("unity-player-native-result", 1, 1, status="complete")
        method = self.work(returned)["phases"][1]
        for part in method["parts"]:
            self.assertEqual((part["closed"], part["status"], part["percent"]), (True, "complete", 100.))
        self.assertFalse(method["closed"])
        self.assertEqual(self.work(returned)["percent"], 95.)
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])
        saved = self.store.load(self.session)
        next(row for row in saved["stages"] if row["id"] == "build")["status"] = "failed"
        self.store.save(saved)
        self.assertTrue(all(part["status"] == "complete" for part in self.work(self.row()["progress"])["phases"][1]["parts"]))

    def test_successful_native_bank_closes_compiler_display_with_stale_partial_counter(self):
        for done, total, unit in ((0, 1, "tasks"), (90, 100, "variants")):
            with self.subTest(done=done, total=total):
                self.start("content-bank")
                self.report("unity-work-stage:method", owner="content-bank", status="start")
                self.report("unity-shader-compile", done, total, unit, "content-bank")
                value = self.report("unity-addressables-build", 1, 1, owner="content-bank", status="complete")
                compiler = next(row for group in value["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "content-bank")["compiler"]
                self.assertEqual((compiler["status"], compiler["percent"]), ("complete", 100.))
                self.assertEqual((compiler["done"], compiler["total"], compiler["unit"]), (done, total, unit))
                self.assertNotIn("content-bank", self.row()["progressPlan"]["completed"])

    def test_failed_native_bank_keeps_its_actual_partial_compiler_counter(self):
        self.start("content-bank")
        self.report("unity-work-stage:method", owner="content-bank", status="start")
        self.report("unity-shader-compile", 90, 100, "variants", "content-bank")
        self.report("unity-addressables-build", 0, 1, owner="content-bank", status="failed")
        saved = self.store.load(self.session)
        next(row for row in saved["stages"] if row["id"] == "build")["status"] = "failed"
        self.store.save(saved)
        compiler = next(row for group in self.row()["progress"]["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "content-bank")["compiler"]
        self.assertEqual((compiler["status"], compiler["percent"], compiler["done"], compiler["total"]), ("failed", 90., 90, 100))

    def test_successful_native_player_closes_compiler_before_final_evidence(self):
        self.start()
        self.report("unity-work-stage:method", status="start")
        self.report("unity-shader-compile", 60, 100, "variants")
        native = self.report("unity-player-native-result", 1, 1, status="complete")
        compiler = next(row for group in native["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "player")["compiler"]
        self.assertEqual((compiler["status"], compiler["percent"]), ("complete", 100.))
        self.assertEqual((compiler["done"], compiler["total"]), (60, 100))
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])
        self.assertFalse(self.work(native)["phases"][1]["closed"])


if __name__ == "__main__": unittest.main()
