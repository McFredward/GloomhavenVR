"""Real compact update owners survive ingestion and retain measured scopes."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import stage_plan
import state
from processes import LogTail, ProgressParser, Supervisor


class CompactUpdateProgressTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        interval = patch("state.PROGRESS_INTERVAL", 0); interval.start(); self.addCleanup(interval.stop)
        self.store = state.Store(Path(temporary.name) / "owned")
        self.session = self.store.create({"mode": "update-mod", "gameRoot": "owned"})["session"]
        self.store.begin_stage(self.session, "build", "same-retained-apk")
        self.supervisor = Supervisor(self.store, self.session); self.supervisor.set_stage("build")
        self.parser = ProgressParser(); self.addCleanup(self.parser.close_coverage)
        self.log = self.store.session_dir(self.session) / "build.log"; self.log.write_text("")
        self.tails = {self.log: LogTail(self.log)}

    def progress(self): return self.store.load(self.session)["stages"][5]["progress"]

    def feed(self, phase, done=None, total=None, operation=None, status="progress", detail="Measured task"):
        value = dict(schema=1, phase=phase, done=done, total=total, unit="tasks", status=status, detail=detail)
        if operation: value["operation"] = operation
        with self.log.open("a") as stream: stream.write("GHVRQ_PROGRESS " + json.dumps(value) + "\n")
        self.supervisor._tail_progress(self.tails, self.parser, 0)
        return self.progress()

    def start_code(self):
        for name in ("update-owned-game", "update-mod-source"):
            self.feed("operation:" + name, 1, 1, name, "complete")
        return self.feed("operation:update-code", operation="update-code", status="start")

    def owner(self):
        return next(row for group in self.progress()["buildOverview"]["groups"] for row in group["operations"] if row["id"] == "update-code")

    def test_support_operation_boundaries_are_not_stripped_by_full_build_plan(self):
        value = self.start_code()
        self.assertEqual(value["stagePercent"], 10)
        self.assertEqual(value["stageOperation"], "update-code")
        self.assertEqual(value["buildOverview"]["done"], 2)
        self.assertEqual(value["buildOverview"]["total"], 6)
        self.assertEqual(value["buildOverview"]["workflow"], "update-mod")
        self.assertEqual(self.owner()["status"], "running")
        self.assertEqual(value["buildOverview"]["recovery"]["sections"], [])

    def test_sdk_return_closes_its_stage_without_finishing_native_update(self):
        self.start_code()
        for name in ("weave", "standalone", "update-sdk"):
            self.feed("stage:" + name, status="start")
            if name == "update-sdk":
                self.feed("unity-work-invocation", 0, 1, "update-code", "start")
                self.feed("unity-work-stage:method", 0, 1, "update-code", "start")
                self.feed("unity-sdk-tasks", 1, 2, "update-code")
                method = self.owner()["unityWork"]["phases"][1]
                self.assertEqual([part["id"] for part in method["parts"]], ["sdk"])
                self.feed("unity-work-invocation", 1, 1, "update-code", "complete")
            self.feed("stage:" + name, 1, 1, status="complete")
        before = self.progress()["stagePercent"]
        self.assertLess(before, 25)
        self.feed("stage:update-package-api", status="start")
        self.feed("package-api-bind", 50, 100, "package-api")
        self.assertGreater(self.progress()["stagePercent"], before)
        self.feed("stage:update-package-api", 1, 1, status="complete")
        self.feed("stage:update-code", status="start")
        owner = self.owner()
        self.assertEqual(owner["updateCode"]["done"], 4)
        self.assertEqual(owner["updateCode"]["total"], 6)
        self.assertEqual(owner["updateCode"]["passes"][4]["percent"], 0)
        self.assertFalse(owner["closed"])
        self.assertNotIn("unityWork", owner)

    def test_finite_roster_and_order_counters_move_parent_and_close_only_their_scope(self):
        self.start_code(); self.feed("stage:update-code", status="start")
        before = self.progress()["stagePercent"]
        for name in stage_plan.UPDATE_PREPARATION_SHARES:
            self.feed(name, 0, 128, "update-code")
            partial = self.feed(name, 64, 128, "update-code")
            self.assertGreater(partial["stagePercent"], before)
            self.feed(name, 128, 128, "update-code", "complete")
            self.assertFalse(self.owner()["closed"], "A preparation return must not finish the native compiler")
            before = self.progress()["stagePercent"]
        prepared = self.owner()["updateCode"]["preparation"]
        self.assertEqual(prepared["done"], 5)
        self.assertEqual(prepared["percent"], 100)
        self.assertTrue(all(row["closed"] and row["status"] == "complete" for row in prepared["passes"]))
        self.assertLess(self.owner()["updateCode"]["percent"], 100)

    def test_activity_refresh_does_not_reset_last_real_progress_time(self):
        self.start_code()
        with patch("state.time.time", return_value=1000):
            first = self.store.progress(self.session, "build", "update-code-roster", 32, 128, "tasks", operation="update-code")
        self.assertEqual(first["lastProgressAt"], 1000)
        for now in (1015, 1030, 1090):
            with patch("state.time.time", return_value=now):
                refreshed = self.store.progress(self.session, "build", "unity-refresh", detail="Asset Pipeline Refresh: Total: 14.672 seconds", operation="update-code")
            self.assertEqual(refreshed["lastProgressAt"], 1000)
            self.assertEqual(refreshed["lastActivityAt"], now)
            self.assertEqual(refreshed["stagePercent"], first["stagePercent"])
        with patch("state.time.time", return_value=1100):
            increased = self.store.progress(self.session, "build", "update-code-roster", 64, 128, "tasks", operation="update-code")
        self.assertEqual(increased["lastProgressAt"], 1100)
        with patch("state.time.time", return_value=1115):
            repeated = self.store.progress(self.session, "build", "update-code-roster", 64, 128, "tasks", operation="update-code")
        self.assertEqual(repeated["lastProgressAt"], 1100)

    def test_reused_complete_update_stays_green_without_fabricated_pending_children(self):
        self.start_code(); self.feed("stage:update-code", status="start")
        self.feed("update-code-catalog", 32, 128, "update-code")
        self.feed("operation:update-code", 1, 1, "update-code", "reuse")
        owner = self.owner()
        self.assertTrue(owner["closed"])
        self.assertEqual(owner["updateCode"]["done"], owner["updateCode"]["total"])
        self.assertTrue(all(part["closed"] and part["percent"] == 100 for part in owner["updateCode"]["passes"]))

    def test_foreign_nested_operation_cannot_skip_current_work(self):
        self.start_code(); before = self.progress()["stagePercent"]
        value = self.feed("tool:foreign", 1, 1, "recovery", "complete")
        self.assertEqual(value["stageOperation"], "update-code")
        self.assertEqual(value["stagePercent"], before)
        self.assertEqual(value["buildOverview"]["done"], 2)

    def test_completed_sdk_tail_cannot_complete_the_next_native_invocation(self):
        self.start_code()
        self.feed("stage:update-sdk", status="start")
        self.feed("stage:update-sdk", 1, 1, status="complete")
        self.feed("stage:update-code", status="start")
        self.feed("update-code-catalog", 32, 128, "update-code")
        late = "GHVRQ_PROGRESS " + json.dumps(dict(schema=1, phase="unity-work-invocation", done=1, total=1,
            operation="update-code", status="complete"))
        self.assertIsNone(self.parser.parse(late, "update-sdk-unity.log"))
        self.assertIsNone(self.parser.parse("[340/344 0s] Csc Library/Bee/old.dll", "update-sdk-unity.log"))
        self.assertFalse(self.owner()["closed"])

    def test_openxr_retry_preserves_completed_hosts_and_keeps_native_publication_open(self):
        self.start_code()
        for name in ("weave", "standalone", "update-sdk", "update-package-api"):
            self.feed("stage:" + name, 1, 1, status="complete")
        self.feed("stage:update-code", status="start")
        self.feed("unity-work-invocation", 0, 1, "update-code", "start")
        for name in stage_plan.UPDATE_PREPARATION_SHARES:
            self.feed(name, 1, 1, "update-code", "complete")
        self.feed("unity-work-invocation", 0, 1, "update-code", "failed")
        before = self.progress()["stagePercent"]
        value = self.feed("update-code-openxr-retry", operation="update-code", status="start")
        self.assertEqual(value["stagePercent"], before)
        self.assertEqual(self.owner()["updateCode"]["done"], 4)
        self.assertFalse(self.owner()["closed"])
        self.feed("unity-work-invocation", 0, 1, "update-code", "start")
        self.assertEqual(self.owner()["status"], "running")
        self.assertFalse(self.owner()["closed"])
        self.assertEqual(self.progress()["stagePercent"], before)


if __name__ == "__main__": unittest.main()
