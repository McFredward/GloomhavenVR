"""Explicit, resumable clean reset with durable host originals and no replay."""
import importlib.util
import json
from pathlib import Path
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
