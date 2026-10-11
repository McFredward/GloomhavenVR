"""Optional signing repair contributes real work without closing installation."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import ProgressParser
from state import Store
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


if __name__ == "__main__": unittest.main()
