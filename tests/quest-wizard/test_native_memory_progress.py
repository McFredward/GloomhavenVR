"""Actual child observation keeps native retries inside the qualified frontier."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import ProgressParser, Supervisor
import stage_plan
import state
import wizard


class NativeMemoryProgressTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory(); self.addCleanup(folder.cleanup)
        self.store = state.Store(Path(folder.name) / "owned")
        self.saved = self.store.create(wizard.choices({"gameRoot": str(Path(folder.name) / "Game")}))
        self.session = self.saved["session"]
        interval = mock.patch("state.PROGRESS_INTERVAL", 0)
        interval.start(); self.addCleanup(interval.stop)
        self.store.begin_stage(self.session, "build", "native-test")

    def row(self):
        return self.store.load(self.session)["stages"][5]

    def observe(self, phase, **fields):
        parsed = ProgressParser().parse("GHVRQ_PROGRESS " + json.dumps(dict(schema=1, phase=phase, **fields)), "build.log")
        self.assertIsNotNone(parsed)
        return self.store.progress(self.session, "build", **parsed)

    def test_resource_observations_preserve_real_owner_and_closed_preparation(self):
        self.store.operation(self.session, "build", "weave", complete=True)
        before = self.row()["progress"]["stagePercent"]
        # This actual compiler strategy has no asset-work denominator. A
        # malformed ownership hint cannot reopen weave or close Player.
        result = self.observe("native-compiler-profile", done=1, total=1, unit="checks",
                              operation="player", status="complete", nativeMemory={
            "compilerProfile": "release-line-tables", "jobs": 4, "attempt": 1,
            "reason": "capacity", "resources": {"availableMemoryBytes": 38776823808,
            "commitHeadroomBytes": 29501243392, "workspaceRoot": "untrusted-path"}})
        self.assertEqual(result["stagePercent"], before)
        self.assertIsNone(result["percent"])
        self.assertEqual(result["stageOperation"], "weave")
        self.assertNotIn("workspaceRoot", result["nativeMemory"]["resources"])
        self.assertEqual(result["nativeMemory"]["resources"]["commitHeadroomBytes"], 29501243392)
        self.assertNotIn("player", self.row()["progressPlan"]["completed"])
        self.store.operation(self.session, "build", "player")
        self.store.progress(self.session, "build", "bee-actions:fixture:10:0", 6, 10, "actions")
        before = self.row()["progress"]["stagePercent"]
        self.store.progress(self.session, "build", "native-memory-check", status="progress")
        self.assertEqual(self.row()["progress"]["stageOperation"], "player")
        for phase, status in (("native-memory-retry", "start"), ("native-memory-wait", "progress"),
                              ("native-memory-wait", "complete"), ("native-compiler-profile", "progress")):
            value = self.observe(phase, status=status, nativeMemory={"jobs": 1, "attempt": 2, "reason": "pressure"})
            self.assertEqual(value["stagePercent"], before)
            self.assertEqual(value["stageOperation"], "player")
            self.assertIsNone(value["percent"])
            self.assertNotIn("player", self.row()["progressPlan"]["completed"])
            operations = [item for group in value["buildOverview"]["groups"] for item in group["operations"]]
            self.assertTrue(next(item for item in operations if item["id"] == "weave")["closed"])
            self.assertFalse(any(item["status"] == "failed" for item in operations))
        self.store.progress(self.session, "build", "bee-actions:fixture:10:1", 1, 10, "actions")
        self.assertEqual(self.row()["progress"]["stagePercent"], before)
        self.store.operation(self.session, "build", "player", complete=True)
        self.assertGreater(self.row()["progress"]["stagePercent"], before)

    def test_asset_pressure_keeps_open_producer_and_saved_checkpoints(self):
        self.store.operation(self.session, "build", "textures")
        self.store.progress(self.session, "build", "prepare-substage:native-texture2d", 2, 3, "checkpoints",
                            operation="textures", status="start")
        before = self.row()["progress"]["stagePercent"]
        for phase in ("asset-memory-retry", "asset-memory-wait"):
            result = self.observe(phase, status="progress", nativeMemory={"jobs": 1, "reason": "allocation-failed"})
            self.assertEqual(result["stageOperation"], "textures")
            self.assertEqual(result["stagePercent"], before)
            self.assertEqual(self.row()["progressPlan"]["preparationScopes"]["textures"]["name"], "native-texture2d")

    def test_untrusted_resource_fields_are_bounded_without_hiding_actual_event(self):
        event = self.observe("native-memory-retry", status="progress", nativeMemory={
            "jobs": True, "attempt": 1000001, "compilerProfile": "x" * 65, "reason": "../../private",
            "resources": {"availableMemoryBytes": -1, "commitHeadroomBytes": True,
                          "processWorkingSetBytes": 2 ** 53, "processPrivateCommitBytes": 0},
            "command": "do not expose"})
        self.assertEqual(event["nativeMemory"], {"resources": {"processPrivateCommitBytes": 0}})
        for value in ([], "path", None, {"reason": "x" * 81}):
            self.assertIsNone(state.native_memory_progress(value))

    def test_live_child_wait_retry_then_finish_never_becomes_blocked_or_failed(self):
        self.store.operation(self.session, "build", "player")
        supervisor = Supervisor(self.store, self.session, poll=.01); supervisor.set_stage("build")
        events = [{"schema": 1, "phase": phase, "status": status, "nativeMemory": {"jobs": 1, "attempt": 2}}
                  for phase, status in (("native-memory-retry", "start"), ("native-memory-wait", "progress"),
                                        ("native-memory-wait", "complete"), ("native-compiler-profile", "progress"))]
        script = "import json,sys,time;[(print('GHVRQ_PROGRESS '+json.dumps(event),flush=True),time.sleep(.03)) for event in json.loads(sys.argv[1])]"
        live = self.store.load(self.session); live["status"] = live["stages"][5]["status"] = "running"
        with state.file_lock(self.store.root / "run.lock"), self.store.active(live):
            supervisor.run([sys.executable, "-I", "-c", script, json.dumps(events)],
                           self.store.session_dir(self.session) / "logs/build.log")
            saved = self.store.load(self.session)
        self.assertEqual(saved["status"], "running")
        self.assertEqual(saved["needsActions"], [])
        self.assertEqual(saved["stages"][5]["progress"]["nativeMemory"]["attempt"], 2)
        self.assertNotIn("waiting", saved["stages"][5])
        self.assertNotIn("player", saved["stages"][5]["progressPlan"]["completed"])
        logged = (self.store.session_dir(self.session) / "logs/progress.log").read_text()
        self.assertIn('"phase":"native-memory-wait"', logged)


if __name__ == "__main__": unittest.main()
