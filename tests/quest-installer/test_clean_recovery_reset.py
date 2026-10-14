"""Explicit, resumable clean reset with durable host originals and no replay."""
import importlib.util
import json
from pathlib import Path
import subprocess
import types
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("clean_reset_fixture", Path(__file__).with_name("test_signing_recovery.py"))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
recovery = fixture.recovery


class CleanResetTests(unittest.TestCase):
    def setUp(self):
        fixture.TransactionTests.setUp(self)
        self.source.content = ()
        self.initial = self.engine()
        blocked = recovery.CaptureRepairError("Unsupported automatic library start", root="external",
            reason="restore-app-launch-unavailable", attempts=2, total=2)
        with mock.patch.object(self.initial, "_restore", side_effect=blocked):
            with self.assertRaises(recovery.RecoveryError): self.initial.recover(detected=True)
        self.transaction = self.initial.journal["transaction"]
        self.archives = {item["archive"]: (self.initial.backup / item["archive"]).read_bytes() for item in self.initial.journal["roots"]}
        self.device.calls.clear()

    def engine(self, **choices):
        return recovery.Recovery(self.adb, "192.0.2.10:5555", "QUEST-LOCAL", self.source, self.root / "workspace",
            decode_receipt=fixture.installer.decode_content_receipt, data_policy="saves", **choices)

    def assert_originals_retained(self):
        for name, value in self.archives.items(): self.assertEqual((self.initial.backup / name).read_bytes(), value)

    def assert_no_old_device_data(self):
        self.assertFalse(self.device.path(recovery.EXTERNAL + "/files/GloomSaves/Campaign/party_76561198000000001.save").exists())
        self.assertFalse((self.device.path(self.device.private) / "no_backup/native.bin").exists())

    def test_explicit_reset_removes_only_once_and_never_reads_or_replays_backups(self):
        engine = self.engine(reset_app_data=self.transaction)
        with mock.patch.object(engine, "_repair_retained_archives", side_effect=AssertionError("backup repair")), \
                mock.patch.object(engine, "_restore", side_effect=AssertionError("restore")), \
                mock.patch.object(recovery, "archive_inventory", side_effect=AssertionError("archive read")), \
                mock.patch.object(recovery, "file_hash", side_effect=AssertionError("full hash")):
            value = engine.recover()
        self.assertFalse(value["verified"])
        self.assertTrue(value["restorationSkipped"])
        self.assertTrue(value["saveDataReset"])
        self.assertEqual(engine.journal["state"], "reset-installed")
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
        self.assertEqual(sum(row[0] == "install" for row in self.device.calls), 1)
        self.assert_no_old_device_data(); self.assert_originals_retained()
        engine.complete()
        self.assertEqual(json.loads(engine.path.read_text())["state"], "reset-complete")
        self.assertIsNone(self.engine().recover())
        self.assert_no_old_device_data()

    def test_absent_package_exit_one_installs_finished_apk_without_signing_repair(self):
        # Actual Adb.run sees the same code1/empty reply as Capture194342.
        engine = self.engine(reset_app_data=self.transaction)
        self.assertTrue(engine.recover()["saveDataReset"])
        log = self.log.read_text()
        self.assertIn('"path", "--user", "0", "' + recovery.PACKAGE + '"]\n{"exitCode": 1}\n', log)
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
        self.assertEqual(sum(row[0] == "install" for row in self.device.calls), 1)
        self.assertEqual(engine.journal["state"], "reset-installed")
        self.assert_no_old_device_data(); self.assert_originals_retained()

    def test_interrupted_reset_installing_resumes_absent_package_without_second_removal(self):
        engine = self.engine(reset_app_data=self.transaction)
        original = engine.commit
        def commit(state=None, **fields):
            original(state, **fields)
            if state == "reset-installing": raise KeyboardInterrupt()
        engine.commit = commit
        with self.assertRaises(KeyboardInterrupt): engine.recover()
        self.assertEqual(json.loads(engine.path.read_text())["state"], "reset-installing")
        self.assertFalse(self.device.installed)
        self.assertFalse(any(row[0] == "install" for row in self.device.calls))
        # Durable approval is sufficient; do not ask for deletion again.
        resumed = self.engine()
        with mock.patch.object(resumed, "_restore", side_effect=AssertionError("old restore")), \
                mock.patch.object(resumed, "_repair_retained_archives", side_effect=AssertionError("backup repair")):
            self.assertTrue(resumed.recover()["saveDataReset"])
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
        self.assertEqual(sum(row[0] == "install" for row in self.device.calls), 1)
        self.assert_no_old_device_data(); self.assert_originals_retained()

    def test_absent_probe_never_masks_nonempty_errors_stderr_or_malformed_paths(self):
        replies = [
            (1, "", "Permission denied"),
            (1, "package:" + self.device.apk, ""),
            (1, "Error: package manager unavailable", ""),
            (1, "", "error: device not found"),
            (0, "Error: package manager unavailable", ""),
            (0, "package:/data/app/foreign/split.apk", ""),
            (0, "package:" + self.device.apk + "\npackage:/data/app/foreign/base.apk", ""),
            (0, "package:" + self.device.apk, "unexpected stderr"),
        ]
        original = self.adb.runner
        for code, output, error in replies:
            def runner(command, **options):
                if command[3:6] == ["shell", "pm", "path"] and not self.device.installed:
                    return subprocess.CompletedProcess(command, code, output, error)
                return original(command, **options)
            with self.subTest(code=code, output=output, stderr=error), \
                    mock.patch.object(self.adb, "runner", side_effect=runner):
                engine = self.engine(reset_app_data=self.transaction)
                with self.assertRaisesRegex(recovery.RecoveryError, "^Clean installation could not safely continue:"):
                    engine.recover()
                self.assertEqual(engine.journal["state"], "reset-installing")
                self.assertFalse(any(row[0] == "install" for row in self.device.calls))
                self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
                self.assert_originals_retained()

    def test_absent_probe_preserves_actual_adb_connection_errors(self):
        original = self.adb.runner
        for message in ("error: device offline", "error: device unauthorized", "error: device '192.0.2.10:5555' not found"):
            def runner(command, **options):
                if command[3:6] == ["shell", "pm", "path"] and not self.device.installed:
                    return subprocess.CompletedProcess(command, 1, "", message)
                return original(command, **options)
            with self.subTest(message=message), mock.patch.object(self.adb, "runner", side_effect=runner):
                engine = self.engine(reset_app_data=self.transaction)
                with self.assertRaisesRegex(recovery.RecoveryError, "^Clean installation could not safely continue:") as raised:
                    engine.recover()
                self.assertIsInstance(raised.exception.__cause__, fixture.installer.ConnectionRequired)
                self.assertEqual(engine.journal["state"], "reset-installing")
                self.assertFalse(any(row[0] == "install" for row in self.device.calls))
                self.assert_originals_retained()

    def test_different_headset_after_removal_cannot_receive_the_finished_apk(self):
        engine = self.engine(reset_app_data=self.transaction)
        original = engine.commit
        def commit(state=None, **fields):
            original(state, **fields)
            if state == "reset-installing": self.device.hardware = "OTHER-QUEST"
        engine.commit = commit
        with self.assertRaisesRegex(recovery.RecoveryError, "different headset"): engine.recover()
        self.assertEqual(engine.journal["state"], "reset-installing")
        self.assertFalse(any(row[0] == "install" for row in self.device.calls))
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
        self.assert_originals_retained()

    def test_required_installed_apk_guard_still_rejects_absent_package_exit_one(self):
        engine = self.engine(reset_app_data=self.transaction)
        engine.recover()
        self.device.installed = False
        with self.assertRaisesRegex(fixture.installer.InstallError, "exit 1.*no output"):
            engine._installed_apk_guard(self.source.sha256)
        self.assert_originals_retained()

    def test_wrong_transaction_and_mutually_exclusive_choices_cannot_remove_data(self):
        with self.assertRaisesRegex(recovery.RecoveryError, "exact pending"):
            self.engine(reset_app_data="d" * 32).recover()
        with self.assertRaisesRegex(recovery.RecoveryError, "either manual"):
            self.engine(reset_app_data=self.transaction, manual_storage_launch=self.transaction)
        self.assertFalse(any(row[0] == "uninstall" for row in self.device.calls))
        self.assert_originals_retained()

    def test_missing_local_bank_refuses_reset_before_removal(self):
        self.source.content = (types.SimpleNamespace(package=None, path=self.root / "missing.bank", size=42),)
        with self.assertRaisesRegex(recovery.RecoveryError, "missing"):
            self.engine(reset_app_data=self.transaction).recover()
        self.assertFalse(any(row[0] == "uninstall" for row in self.device.calls))
        self.assert_originals_retained()

    def test_reset_unavailable_flag_declines_before_removal(self):
        with self.assertRaisesRegex(recovery.RecoveryError, "complete local"):
            self.engine(reset_app_data=self.transaction, reset_source_available=False).recover()
        self.assertFalse(any(row[0] == "uninstall" for row in self.device.calls))

    def test_local_bank_disappearing_after_approval_is_rechecked_before_uninstall(self):
        bank = self.root / "local.bank"; bank.write_bytes(b"owned contents")
        self.source.content = (types.SimpleNamespace(package=None, path=bank, size=bank.stat().st_size),)
        engine = self.engine(reset_app_data=self.transaction)
        original = engine.commit
        def commit(state=None, **fields):
            original(state, **fields)
            if state == "reset-removing": bank.unlink()
        engine.commit = commit
        with self.assertRaisesRegex(recovery.RecoveryError, "missing"): engine.recover()
        self.assertFalse(any(row[0] == "uninstall" for row in self.device.calls))

    def test_interruption_before_uninstall_resumes_approved_state_without_restore(self):
        engine = self.engine(reset_app_data=self.transaction)
        original = engine.commit
        def commit(state=None, **fields):
            original(state, **fields)
            if state == "reset-removing": raise KeyboardInterrupt()
        engine.commit = commit
        with self.assertRaises(KeyboardInterrupt): engine.recover()
        self.assertEqual(json.loads(engine.path.read_text())["state"], "reset-removing")
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 0)
        resumed = self.engine()
        with mock.patch.object(resumed, "_restore", side_effect=AssertionError("old restore")):
            self.assertTrue(resumed.recover()["saveDataReset"])
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
        self.assert_originals_retained()

    def test_lost_uninstall_reply_resumes_without_second_removal(self):
        engine = self.engine(reset_app_data=self.transaction)
        original = self.adb.run
        def run(*args, **options):
            value = original(*args, **options)
            if "uninstall" in args: raise RuntimeError("lost uninstall acknowledgement")
            return value
        with mock.patch.object(self.adb, "run", side_effect=run):
            with self.assertRaisesRegex(recovery.RecoveryError, "lost uninstall"): engine.recover()
        self.assertFalse(self.device.installed)
        self.assertTrue(self.engine().recover()["saveDataReset"])
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
        self.assert_originals_retained()

    def test_lost_install_reply_adopts_exact_finished_apk_without_second_install(self):
        engine = self.engine(reset_app_data=self.transaction)
        original = self.adb.run
        def run(*args, **options):
            value = original(*args, **options)
            if "install" in args: raise RuntimeError("lost install acknowledgement")
            return value
        with mock.patch.object(self.adb, "run", side_effect=run):
            with self.assertRaisesRegex(recovery.RecoveryError, "lost install"): engine.recover()
        self.assertTrue(self.device.installed)
        self.assertTrue(self.engine().recover()["saveDataReset"])
        self.assertEqual(sum(row[0] == "uninstall" for row in self.device.calls), 1)
        self.assertEqual(sum(row[0] == "install" for row in self.device.calls), 1)
        self.assert_no_old_device_data(); self.assert_originals_retained()

    def test_other_profile_appearing_on_resumed_uninstall_is_retained(self):
        engine = self.engine(reset_app_data=self.transaction)
        original = engine.commit
        def commit(state=None, **fields):
            original(state, **fields)
            if state == "reset-removing": raise KeyboardInterrupt()
        engine.commit = commit
        with self.assertRaises(KeyboardInterrupt): engine.recover()
        self.device.others = True
        with self.assertRaisesRegex(recovery.RecoveryError, "another Android profile"):
            self.engine().recover()
        self.assertFalse(any(row[0] == "uninstall" for row in self.device.calls))

    def test_existing_reset_result_retries_content_stage_without_reinstall(self):
        engine = self.engine(reset_app_data=self.transaction)
        engine.recover()
        self.device.calls.clear()
        with mock.patch.object(recovery, "file_hash", side_effect=AssertionError("hash")), \
                mock.patch.object(recovery, "archive_inventory", side_effect=AssertionError("read")):
            value = self.engine(reset_app_data=self.transaction).recover()
        self.assertTrue(value["restorationSkipped"])
        self.assertFalse(any(row[0] in ("install", "uninstall") for row in self.device.calls))
        self.assert_originals_retained()

    def test_terminal_explicit_reset_retry_is_harmless_without_old_save_replay(self):
        engine = self.engine(reset_app_data=self.transaction)
        engine.recover(); engine.complete()
        new_save = self.device.path(recovery.EXTERNAL + "/files/GloomSaves/Campaign/new.save")
        new_save.parent.mkdir(parents=True); new_save.write_bytes(b"new progress")
        self.assertTrue(self.engine(reset_app_data=self.transaction).recover()["saveDataReset"])
        self.assertEqual(new_save.read_bytes(), b"new progress")
        self.assert_no_old_device_data()

    def test_manual_capability_survives_verified_resume_without_old_data_replay(self):
        self.initial.commit("verified")
        engine = self.engine(manual_storage_launch=self.transaction)
        with mock.patch.object(engine, "_restore", side_effect=AssertionError("replay")):
            value = engine.recover()
        self.assertTrue(value["manualStorageLaunch"])
        self.assertTrue(value["verified"])

    def test_action_contains_exact_choice_binding_and_local_reset_availability(self):
        actions = [json.loads(line.split(" ", 1)[1]) for line in self.output.getvalue().splitlines() if line.startswith("GHVRQ_ACTION ")]
        self.assertTrue(actions[-1]["requiresRestoreChoice"])
        self.assertEqual(actions[-1]["transaction"], self.transaction)
        self.assertEqual(actions[-1]["deviceSerial"], self.device.hardware)
        self.assertTrue(actions[-1]["manualLaunchAvailable"])
        self.assertTrue(actions[-1]["resetAppDataAvailable"])


if __name__ == "__main__": unittest.main()
