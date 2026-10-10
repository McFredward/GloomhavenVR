"""Large completed project overviews do not multiply inside polling history."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import stage_plan
from state import DERIVED_PROGRESS_FIELDS, Store, canonical, read_json
import wizard


class ProgressEventBoundsTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        patch = mock.patch("state.PROGRESS_INTERVAL", 0); patch.start(); self.addCleanup(patch.stop)
        self.store = Store(Path(temporary.name) / "owned")
        self.saved = self.store.create(wizard.choices({"gameRoot": str(Path(temporary.name) / "Game")}))
        self.session = self.saved["session"]
        self.store.begin_stage(self.session, "build", "same-input")

    def report(self, phase, done, total, owner, *, unit="steps", status="progress", detail="Counted retained work"):
        return self.store.progress(self.session, "build", phase, done, total, unit, detail,
            operation=owner, status=status)

    def launch(self, owner):
        self.report("unity-work-invocation", 0, 1, owner, unit="invocations", status="start")
        self.report("unity-work-plan", 900, 1000, owner, unit="assets", status="start", detail="[asset-coverage] Known assets")
        self.report("unity-work-stage:method", 0, 1, owner, status="start")

    def test_current_response_and_full_log_remain_detailed_but_event_history_is_compact(self):
        with self.store.active(self.saved):
            self.store.operation(self.session, "build", "unity-import")
            self.launch("unity-import")
            current = self.report("unity-sdk-tasks", 2, 5, "unity-import")
            returned = self.store.event(self.saved, "stage_progress", "build", **current)
            self.assertIn("buildOverview", current)
            self.assertIn("activeWork", current)
            self.assertIn("buildOverview", returned["parameters"])
            self.assertIn("buildOverview", self.saved["stages"][5]["progress"])
            compact = self.saved["events"][-1]["parameters"]
            self.assertFalse(DERIVED_PROGRESS_FIELDS.intersection(compact))
            for key in ("phase", "done", "total", "unit", "detail", "operationStatus", "reportedOperation", "stagePercent"):
                self.assertEqual(compact[key], current[key])
            logged = json.loads((self.store.session_dir(self.session) / "logs/progress.log").read_text().splitlines()[-1])
            self.assertEqual(logged, returned)
        reloaded = self.store.load(self.session)
        self.assertIn("buildOverview", reloaded["stages"][5]["progress"])
        self.assertEqual(reloaded["stages"][5]["progress"]["unityTask"]["done"], 2)

    def test_save_compacts_old_compatible_history_without_mutating_original_events(self):
        event = {"sequence": 7, "time": 10, "code": "stage_progress", "stage": "build",
            "parameters": {"phase": "prepare-substage:campaign-compute", "done": 1, "total": 2,
                "reportedOperation": "graphics", "operationStatus": "complete", "detail": "Completed checkpoint",
                "buildOverview": {"old": "x" * 50000}, "activeWork": {"old": True}, "unityImport": {"done": 90}}}
        original = copy.deepcopy(event)
        self.saved["events"] = [event]
        self.store.save(self.saved)
        self.assertEqual(event, original)
        persisted = read_json(self.store.session_dir(self.session) / "state.json")
        self.assertEqual(persisted["events"][0]["parameters"]["done"], 1)
        self.assertEqual(persisted["events"][0]["parameters"]["reportedOperation"], "graphics")
        self.assertFalse(DERIVED_PROGRESS_FIELDS.intersection(persisted["events"][0]["parameters"]))

    def test_failure_and_lifecycle_event_contract_is_unchanged(self):
        parameters = {"error": "ValueError", "traceback": "actual failure stack", "cause": "native compiler stopped",
            "resources": {"availableMemoryBytes": 123}, "buildOverview": {"explicit_failure_context": True}}
        result = self.store.event(self.saved, "stage_exception", "build", **parameters)
        self.assertEqual(self.saved["events"][-1], result)
        for key, value in parameters.items(): self.assertEqual(result["parameters"][key], value)

    def test_all_27_checkpoints_five_unity_plans_and_128_updates_remain_readable_on_retry(self):
        saved = self.store.load(self.session)
        with self.store.active(saved):
            checkpoint_count = 0
            for owner, planned in stage_plan.PREPARATION_CHECKPOINTS.items():
                names = tuple(name for name in planned if name != "startup-compute")
                self.store.operation(self.session, "build", owner)
                for index, checkpoint in enumerate(names):
                    self.report("prepare-substage:" + checkpoint, index, len(names), owner, status="start")
                    for part in stage_plan.STARTUP_ITEM_SCHEDULES.get(checkpoint, {}):
                        self.report("prepare-items:" + part, 10, 10, owner, unit="files", status="complete")
                    if checkpoint == "mod-resource-banks":
                        self.launch(owner)
                        self.report("unity-shader-task:42:-1", 10, 10, owner, status="complete")
                        self.report("unity-work-invocation", 1, 1, owner, unit="invocations", status="complete")
                    self.report("prepare-substage:" + checkpoint, index + 1, len(names), owner, status="complete")
                    checkpoint_count += 1
                self.store.operation(self.session, "build", owner, complete=True)
            self.assertEqual(checkpoint_count, 27)
            for owner in ("unity-import", "unity-validation", "content-bank", "player"):
                self.store.operation(self.session, "build", owner)
                if owner in ("unity-import", "unity-validation"): self.launch(owner)
                if owner != "player":
                    self.report("unity-work-stage:method", 1, 1, owner, status="complete")
                    self.store.operation(self.session, "build", owner, complete=True)
            for count in range(128):
                self.report("bee-actions:native:200:1", count, 200, "player", unit="actions", detail="Actual native code compilation")
            progress = saved["stages"][5]["progress"]
            before = progress["stagePercent"]
            self.assertEqual(len(saved["stages"][5]["progressPlan"]["unityWork"]), 5)
            self.assertEqual(len(saved["events"]), 128)
            self.assertTrue(all(not DERIVED_PROGRESS_FIELDS.intersection(item["parameters"]) for item in saved["events"]))
            self.assertLess(len(canonical(saved)), 512 * 1024, "all scheduled work fits well below the reader's 2 MiB limit")
            self.assertEqual(progress["buildOverview"]["active"], "player")
            self.assertTrue(progress["buildOverview"]["groups"][2]["operations"][0]["preparation"]["checkpoints"])
        persisted = read_json(self.store.session_dir(self.session) / "state.json")
        self.assertLess(len(canonical(persisted)), 512 * 1024)
        logged = json.loads((self.store.session_dir(self.session) / "logs/progress.log").read_text().splitlines()[-1])
        self.assertEqual(logged["parameters"]["phase"], "bee-actions:native:200:1")
        self.assertIn("buildOverview", logged["parameters"])
        self.assertIn("activeWork", logged["parameters"])
        reloaded = Store(self.store.root)
        observed = reloaded.load(self.session)
        self.assertEqual(observed["stages"][5]["progress"]["buildOverview"]["active"], "player")
        reloaded.begin_run(self.session)
        reloaded.begin_stage(self.session, "build", "same-input")
        value = reloaded.operation(self.session, "build", "player")
        self.assertGreaterEqual(value["stagePercent"], before)
        self.assertIn("graphics", reloaded.load(self.session)["stages"][5]["progressPlan"]["completed"])


if __name__ == "__main__": unittest.main()
