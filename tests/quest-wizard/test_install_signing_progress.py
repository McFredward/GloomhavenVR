"""Optional signing repair contributes real work without closing installation."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import ProgressParser
from state import Store, file_lock
import wizard


class SigningProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.store = Store(Path(self.temp.name) / "owned")
        self.state = self.store.create(wizard.choices({"gameRoot": str(Path(self.temp.name) / "Game")}))
        self.session = self.state["session"]
        for row in self.state["stages"][:-1]: row["status"] = "complete"
        self.state["status"] = self.state["stages"][-1]["status"] = "running"
        self.store.save(self.state)
        # A live Wizard owns this lock. Without it, a status reader correctly
        # classifies a persisted 'running' fixture as an interrupted process.
        owner = file_lock(self.store.root / "run.lock")
        owner.__enter__()
        self.addCleanup(owner.__exit__, None, None, None)
        self.store.operation(self.session, "install", "connect")
        self.parser = ProgressParser()

    def progress(self, phase, done, total, unit="tasks", status="progress"):
        line = "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": phase, "operation": "apk",
            "status": status, "done": done, "total": total, "unit": unit, "detail": "Measured signing migration work"})
        fields = self.parser.parse(line, "install.log")
        self.assertIsNotNone(fields)
        return self.store.progress(self.session, "install", **fields)

    def test_child_backup_and_restore_counters_do_not_complete_or_inflate_apk_parent(self):
        before = self.progress("install-signature-backup", 1, 7)["stagePercent"]
        for phase in ("install-recovery-backup", "install-recovery-restore"):
            current = self.progress(phase, 32, 32, "files", status="complete")
            self.assertEqual(current["percent"], 100)
            self.assertEqual(current["stagePercent"], before)
            self.assertEqual(current["stageOperation"], "apk")
            self.assertNotIn("apk", self.store.load(self.session)["stages"][-1]["progressPlan"]["completed"])

    def test_manual_library_launch_is_a_visible_wait_without_false_install_completion(self):
        before = self.progress("install-signature-restore", 4, 7)["stagePercent"]
        waiting = self.progress("install-recovery-manual-launch", 0, 1, status="waiting")
        self.assertEqual(waiting["operationStatus"], "waiting")
        self.assertEqual(waiting["stagePercent"], before)
        opened = self.progress("install-recovery-manual-launch", 1, 1, status="complete")
        self.assertEqual(opened["stagePercent"], before)
        self.assertNotIn("apk", self.store.load(self.session)["stages"][-1]["progressPlan"]["completed"])

    def test_saved_recovery_milestones_remain_below_final_installation_and_do_not_reopen_build(self):
        percentages = []
        for phase, done in (("detect", 0), ("backup", 1), ("backup", 3),
                            ("replace", 4), ("restore", 5), ("verify", 7)):
            current = self.progress("install-signature-" + phase, done, 7, status="complete" if done == 7 else "progress")
            percentages.append(current["stagePercent"])
        self.assertEqual(percentages, sorted(percentages))
        self.assertGreater(percentages[-1], percentages[0])
        self.assertLess(percentages[-1], 40)
        row = self.store.load(self.session)["stages"][-1]
        self.assertEqual(row["progressPlan"]["completed"], ["connect"])
        fields = self.parser.parse("Confirmed installed build: B665 (input aaaaaaaaaaaa)", "install.log")
        current = self.store.progress(self.session, "install", **fields)
        self.assertEqual(current["stagePercent"], 40)
        self.store.operation(self.session, "install", "content", complete=True)
        self.store.operation(self.session, "install", "launch", complete=True)
        self.assertLess(self.store.load(self.session)["stages"][-1]["progress"]["stagePercent"], 100)
        prior = self.store.load(self.session)["stages"][:-1]
        self.assertTrue(all(row["status"] == "complete" for row in prior))
        self.assertFalse(self.store.receipt(self.session, "install").exists())

    def test_finite_global_backup_work_advances_only_the_reserved_backup_share(self):
        self.progress("install-signature-backup", 0, 7)
        percentages = []
        for done in (0, 1, 20, 40, 41):
            current = self.progress("install-recovery-backup-work", done, 41)
            percentages.append(current["stagePercent"])
            self.assertAlmostEqual(current["stagePercent"], 20 + (40 / 7) * done / 41, places=5)
            self.assertEqual(current["activeWork"]["done"], 0)
            self.assertEqual(current["activeWork"]["total"], 7)
            self.assertNotIn("apk", self.store.load(self.session)["stages"][-1]["progressPlan"]["completed"])
        self.assertTrue(all(left < right for left, right in zip(percentages, percentages[1:])))
        self.assertAlmostEqual(percentages[-1], 20 + 40 / 7, places=5)
        closed = self.progress("install-signature-backup", 2, 7)
        self.assertEqual(closed["activeWork"]["done"], 2)
        self.assertEqual(closed["activeWork"]["total"], 7)
        self.assertLess(closed["stagePercent"], 40)

    def test_access_and_root_local_stream_counters_cannot_spend_the_backup_share(self):
        before = self.progress("install-signature-backup", 0, 7)["stagePercent"]
        for phase, done, total, unit in (("install-recovery-access", 8, 8, "files"),
            ("install-recovery-backup", 11425341374, None, "bytes"),
            ("install-recovery-backup-work", 5, 5, "files")):
            current = self.progress(phase, done, total, unit)
            self.assertEqual(current["stagePercent"], before)
            self.assertEqual(current["stageOperation"], "apk")
        self.assertEqual(self.progress("install-recovery-backup-work", 20, 40)["activeWork"]["total"], 7)

    def test_retry_retains_stage_high_water_without_the_previous_live_migration_counter(self):
        from stage_plan import begin_attempt
        self.progress("install-signature-backup", 2, 7)
        row = self.store.load(self.session)["stages"][-1]
        before = row["progressPlan"]["percent"]
        begin_attempt(row, 123)
        self.assertNotIn("installMigration", row["progressPlan"])
        self.assertEqual(row["progressPlan"]["percent"], before)

    def test_bank_local_text_cannot_complete_global_game_delivery(self):
        self.assertIsNone(self.parser.parse("Installed content files: 4/4. Bank: quest-mod-resources.", "install.log"))
        self.assertIsNone(self.parser.parse("Installed content files: 688/688. Bank: quest-owned-game.", "install.log"))

    def test_content_bytes_move_global_stage_without_premature_completion(self):
        self.store.operation(self.session, "install", "connect", complete=True)
        self.store.operation(self.session, "install", "apk", complete=True)
        percentages = []
        for done in (0, 20, 300, 700, 1000):
            fields = self.parser.parse("GHVRQ_PROGRESS " + json.dumps({"schema": 1,
                "phase": "install-content", "operation": "content", "status": "progress",
                "done": done, "total": 1000, "unit": "bytes", "detail": "Transferring and verifying"}), "install.log")
            current = self.store.progress(self.session, "install", **fields)
            percentages.append(current["stagePercent"])
            self.assertNotIn("content", self.store.load(self.session)["stages"][-1]["progressPlan"]["completed"])
        self.assertTrue(all(left < right for left, right in zip(percentages, percentages[1:])))
        self.assertLess(percentages[-1], 100)

    def test_connection_wait_keeps_measured_work_live_and_resumes_in_place(self):
        self.store.operation(self.session, "install", "connect", complete=True)
        self.store.operation(self.session, "install", "apk", complete=True)
        def event(phase, status, done):
            fields = self.parser.parse("GHVRQ_PROGRESS " + json.dumps({"schema": 1,
                "phase": phase, "operation": "content", "status": status,
                "done": done, "total": 1000, "unit": "bytes"}), "install.log")
            self.assertIsNotNone(fields)
            return self.store.progress(self.session, "install", **fields)
        before = event("install-content", "progress", 650)
        waiting = event("install-connection-wait", "waiting", 650)
        self.assertEqual(waiting["stagePercent"], before["stagePercent"])
        self.assertEqual(waiting["percent"], 65)
        self.assertEqual(waiting["operationStatus"], "waiting")
        state = self.store.load(self.session)
        self.assertEqual(state["status"], "running")
        self.assertEqual(state["stages"][-1]["status"], "running")
        self.assertEqual(state["needsActions"], [])
        self.assertTrue(all(row["status"] == "complete" for row in state["stages"][:-1]))
        resumed = event("install-content", "progress", 660)
        self.assertGreater(resumed["stagePercent"], waiting["stagePercent"])
        self.assertEqual(resumed["operationStatus"], "progress")

    def test_wait_without_a_byte_total_cannot_invent_progress_or_finish_install(self):
        fields = self.parser.parse('GHVRQ_PROGRESS {"schema":1,"phase":"install-connection-wait",'
                                  '"operation":"connect","status":"waiting"}', "install.log")
        current = self.store.progress(self.session, "install", **fields)
        self.assertIsNone(current["percent"])
        self.assertEqual(current["stagePercent"], 0)
        self.assertEqual(self.store.load(self.session)["stages"][-1]["status"], "running")

    def test_waiting_is_rejected_outside_the_install_connection_phase(self):
        for phase in ("install-content", "import", "complete"):
            line = "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": phase,
                "operation": "content", "status": "waiting"})
            self.assertIsNone(self.parser.parse(line, "install.log"))


if __name__ == "__main__": unittest.main()
