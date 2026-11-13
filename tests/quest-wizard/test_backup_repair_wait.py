"""Exhausted reversible root repairs wait without reopening the finished Player."""
import json
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_installation_recovery as fixture
from test_installation_recovery import failures, state


def refusal(**changes):
    payload = {"schema": 1, "kind": "signing-repair-wait", "reason": "backup-census-mismatch",
               "saveDataProtected": True, "attempts": 3, "totalAttempts": 3, "root": "private"}
    payload.update(changes)
    return ("GHVRQ_ACTION " + json.dumps(payload) + "\nQuest installation stopped: Signing migration could not safely continue: "
            "The captured archive does not match its complete mutable app-data census.. "
            "Original recovery data is retained at /owned/backup. Retry with the same APK and headset.\nADB log: current.log\n")


class BackupRepairWaitTests(unittest.TestCase):
    setUp = fixture.InstallationRecoveryTests.setUp
    forbid_earlier_work = fixture.InstallationRecoveryTests.forbid_earlier_work
    assert_prior_retained = fixture.InstallationRecoveryTests.assert_prior_retained
    installer = fixture.InstallationRecoveryTests.installer

    def test_current_exhausted_root_repair_reports_only_a_retained_install_wait(self):
        for reason in ("backup-census-mismatch", "backup-transfer-unavailable"):
            for root in ("private", "device-protected", "external"):
                with self.subTest(reason=reason, root=root):
                    error = failures.installation_failure(refusal(reason=reason, root=root), {"failureStage": "install"})
                    self.assertEqual(error.code, "quest_signing_repair_wait")
                    self.assertEqual(error.parameters["repairReason"], reason)
                    self.assertEqual(error.parameters["repairRoot"], root)
                    self.assertEqual(error.parameters["repairAttempts"], 3)
                    self.assertTrue(error.parameters["saveDataProtected"])
                    self.assertTrue(error.parameters["completedBuildRetained"])
                    self.assertTrue(error.parameters["canSkipInstall"])
                    self.assertIn("complete mutable app-data census", error.parameters["cause"])
                    self.assertEqual(error.parameters["backupPath"], "/owned/backup")
                    self.assertNotIn("recoveryDataReason", error.parameters)

    def test_unknown_partial_or_unbounded_repairs_cannot_claim_completed_automatic_attempts(self):
        for changes in ({"attempts": True}, {"attempts": 0}, {"attempts": 2}, {"totalAttempts": 65},
                        {"totalAttempts": False}, {"reason": "unknown"}, {"root": "other-app"},
                        {"saveDataProtected": False}, {"schema": True}, {"resetSaves": True}, {"policy": "saves"}):
            with self.subTest(changes=changes):
                self.assertEqual(failures.installation_failure(refusal(**changes), {}).code, "quest_signing_recovery_required")
        stale = refusal() + "Quest installation stopped: Signing migration could not safely continue: actual later refusal\n"
        self.assertEqual(failures.installation_failure(stale, {}).code, "quest_signing_recovery_required")
        invalid = refusal().replace("\nQuest installation stopped:", "\nGHVRQ_ACTION invalid\nQuest installation stopped:")
        self.assertEqual(failures.installation_failure(invalid, {}).code, "quest_signing_recovery_required")

    def test_old_capture_without_repair_receipt_does_not_claim_new_attempts(self):
        captured = refusal().split("\n", 1)[1]
        error = failures.installation_failure(captured, {})
        self.assertEqual(error.code, "quest_signing_recovery_required")
        self.assertNotIn("repairAttempts", error.parameters)

    def test_real_supervised_repair_counts_wait_without_failing_or_rebuilding(self):
        self.forbid_earlier_work()
        backup = self.store.root / "device/retained-current-and-history.tar"
        backup.parent.mkdir(parents=True); backup.write_bytes(b"current and archived saves")
        def supervisor(store, session):
            actual = fixture.Supervisor(store, session, poll=.01)
            def run(argv, log, **kwargs):
                actual.set_stage("install")
                progress = "\n".join("GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "install-recovery-repair",
                    "operation": "apk", "done": index, "total": 3, "unit": "attempts", "status": "progress",
                    "detail": "[repair-root:private] Repair only the affected root"}) for index in range(1, 4))
                actual.run([sys.executable, "-I", "-B", "-c", "import sys;print(sys.argv[1]);sys.exit(1)",
                            progress + "\n" + refusal()], log)
            return SimpleNamespace(set_stage=lambda *_: None, run=run)
        self.engine.supervisor_factory = supervisor
        waiting = self.engine.run(self.session)
        self.assertEqual(waiting["status"], "blocked")
        self.assertEqual(waiting["stages"][-1]["status"], "blocked")
        self.assertEqual(waiting["needsActions"][0]["code"], "quest_signing_repair_wait")
        self.assertEqual(waiting["stages"][-1]["progress"]["phase"], "install-recovery-repair")
        self.assertEqual(waiting["stages"][-1]["progress"]["done"], 3)
        self.assertLess(waiting["stages"][-1]["progress"]["stagePercent"], 100)
        self.assertNotIn("installationAction", waiting)
        self.assertEqual(waiting["installationRecoveryPolicy"]["policy"], "saves")
        self.assert_prior_retained(waiting)
        commands = self.installer(); completed = self.engine.run(self.session)
        self.assertEqual(completed["status"], "complete"); self.assertEqual(len(commands), 1)
        self.assertEqual(backup.read_bytes(), b"current and archived saves")
        self.assert_prior_retained(completed)

    def test_explicit_finish_leaves_all_backups_and_completed_build_retained(self):
        self.forbid_earlier_work()
        self.installer(failure=failures.installation_failure(refusal(reason="backup-transfer-unavailable"), {"failureStage": "install"}))
        paused = self.engine.run(self.session)
        self.assertEqual(paused["status"], "blocked")
        current = self.engine.skip_installation(self.session)
        self.assertEqual(current["status"], "complete")
        self.assertEqual(current["stages"][-1]["details"], {"requested": False})
        self.assert_prior_retained(current)


if __name__ == "__main__": unittest.main()
