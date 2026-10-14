"""Explicit headset-library launch controls; no Meta policy or ART claim."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("manual_launch_fixture", Path(__file__).with_name("test_signing_recovery.py"))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
recovery = fixture.recovery


class ManualDevice(fixture.Device):
    """AOSP transient settings with a user-originated library start."""
    def __init__(self, root):
        super().__init__(root)
        self.debug_wait = self.debug_original_wait = False
        self.persistent_debug = "null"
        self.persistent_wait = "0"
        self.manual_armed = False
        self.user_launch_after = 2
        self.manual_polls = 0
        self.clear_denied = False

    def __call__(self, command, **options):
        action = command[3:] if command[1:2] == ["-s"] else command[1:]
        script = options.get("input", b"").decode("utf-8", errors="replace")
        def result(text="", code=0):
            self.calls.append(tuple(action))
            return fixture.subprocess.CompletedProcess(command, code, text, "")
        if action == ["shell", "am", "set-debug-app", "-w", recovery.PACKAGE]:
            self.debug_original, self.debug_original_wait = self.debug_current, self.debug_wait
            self.debug_current, self.debug_wait, self.debug_transient = recovery.PACKAGE, True, True
            self.process_started = self.process_stopped = False
            self.manual_armed = True
            return result()
        if action == ["shell", "am", "clear-debug-app"]:
            if not self.clear_denied:
                # AOSP clears current values while retaining inactive orig fields.
                self.debug_current = "null"
                self.debug_wait = self.debug_transient = False
            self.manual_armed = False
            return result()
        if "settings get global debug_app" in script:
            return result(self.persistent_debug + "\n" + self.persistent_wait + "\n")
        if "dumpsys activity processes" in script:
            if self.debug_current == self.debug_original == "null" and not self.debug_transient: return result("")
            return result("  mDebugApp=" + self.debug_current + "/orig=" + self.debug_original
                + " mDebugTransient=" + str(self.debug_transient).lower()
                + " mOrigWaitForDebugger=" + str(self.debug_original_wait).lower() + "\n")
        if action[:2] == ["shell", "pidof"] and self.manual_armed and not self.process_started:
            self.manual_polls += 1
            if self.manual_polls >= self.user_launch_after:
                # AOSP attach restores values but leaves mDebugTransient=true.
                self.process_started = True
                self.manual_armed = False  # One user library click, not a loop.
                self.process_ticks = str(int(self.process_ticks) + 100)
                self.debug_current, self.debug_wait = self.debug_original, self.debug_original_wait
        return super().__call__(command, **options)


class ManualLaunchTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.device = ManualDevice(self.root / "device")
        self.log = self.root / "adb.log"
        self.adb = fixture.installer.Adb("adb", self.log, self.device)
        self.adb.manual_storage_identity = {"apkSha256": "b" * 64, "inputKey": "a" * 64, "transaction": "c" * 32}
        self.view = recovery.AppStorageView(self.adb, "usb", self.device.hardware)
        self.view._manual_storage_launch_enabled = True
        self.addCleanup(self.view._close_storage_process)

    def run_manual(self):
        with mock.patch.object(recovery.time, "sleep"), contextlib.redirect_stdout(io.StringIO()) as output:
            value = self.view._process_storage_root()
        return value, output.getvalue()

    def test_library_launch_arms_durable_wait_and_proves_exact_pause(self):
        value, output = self.run_manual()
        self.assertEqual(value, "/proc/" + self.device.process_pid + "/root/storage/emulated/0")
        self.assertTrue(self.device.process_stopped)
        self.assertFalse(any(row[:3] == ("shell", "am", "start") for row in self.device.calls))
        receipt = self.view._manual_debug_record()
        self.assertEqual(receipt["source"], self.adb.manual_storage_identity)
        progress = [json.loads(row.split(" ", 1)[1]) for row in output.splitlines() if row.startswith("GHVRQ_PROGRESS ")]
        self.assertEqual(progress[0]["status"], "waiting")
        self.assertEqual(progress[0]["done"], 0)
        self.assertEqual(progress[-1]["done"], 1)
        self.view._close_storage_process()
        self.assertFalse(self.device.process_started)
        self.assertEqual(self.device.debug_current, "null")
        self.assertFalse(self.view._manual_debug_receipt().exists())

    def test_normal_install_policy_never_arms_a_storage_debugger_on_denial(self):
        self.adb.storage_debugger_allowed = False
        with self.assertRaises(recovery.CaptureRepairError) as failure:
            self.view._process_storage_root()
        self.assertEqual(failure.exception.action["reason"], "restore-app-storage-unavailable")
        self.assertEqual(self.device.calls, [])
        self.assertFalse(self.view._manual_debug_receipt().exists())

    def test_rearming_old_quest_pending_selection_is_released_after_attach(self):
        self.device.debug_current = recovery.PACKAGE
        self.device.debug_wait = self.device.debug_transient = True
        self.run_manual()
        self.assertEqual(self.device.debug_current, recovery.PACKAGE)
        self.assertTrue(self.device.debug_wait)
        self.view._close_storage_process()
        self.assertEqual(self.device.debug_current, "null")
        self.assertFalse(self.device.debug_wait)

    def test_current_capture_inactive_history_is_cleared_and_allows_next_start(self):
        self.device.debug_current = recovery.PACKAGE
        self.device.debug_wait = self.device.debug_transient = True
        self.run_manual()
        self.view._close_storage_process()
        self.assertEqual(self.device.debug_current, "null")
        self.assertEqual(self.device.debug_original, recovery.PACKAGE)
        self.assertTrue(self.device.debug_original_wait)
        self.assertFalse(self.view._manual_debug_receipt().exists())
        self.run_manual()
        self.assertTrue(self.device.process_stopped)
        self.view._close_storage_process()
        self.assertFalse(self.device.debug_wait)

    def test_host_retry_accepts_already_cleared_owned_wait_without_clearing_again(self):
        self.device.debug_current = recovery.PACKAGE
        self.device.debug_wait = self.device.debug_transient = True
        self.run_manual()
        self.adb.run("-s", "usb", "shell", "am", "force-stop", recovery.PACKAGE)
        self.adb.run("-s", "usb", "shell", "am", "clear-debug-app")
        resumed = recovery.AppStorageView(self.adb, "usb", self.device.hardware)
        count = sum("clear-debug-app" in row for row in self.device.calls)
        resumed._reconcile_manual_debug_wait()
        self.assertEqual(sum("clear-debug-app" in row for row in self.device.calls), count)
        self.assertFalse(self.view._manual_debug_receipt().exists())
        self.view._storage_process = None

    def test_normal_install_releases_only_its_retained_wait_without_rearming(self):
        self.run_manual()
        self.view._storage_process = None  # Simulate an interrupted host.
        self.device.calls.clear()
        with mock.patch.object(fixture.installer, "signing_recovery_module", return_value=recovery):
            fixture.installer.release_retained_storage_wait(self.adb, "usb", self.device.hardware)
        self.assertFalse(self.view._manual_debug_receipt().exists())
        self.assertFalse(self.device.debug_wait)
        self.assertFalse(self.device.process_started)
        self.assertFalse(any("set-debug-app" in row or "start" in row or "forward" in row
                             for row in self.device.calls))
        self.device.calls.clear()
        fixture.installer.release_retained_storage_wait(self.adb, "usb", self.device.hardware)
        self.assertEqual(self.device.calls, [])

    def test_inactive_foreign_history_is_not_used_to_arm_or_clear_debugger(self):
        self.device.debug_original = "another.debug.app"
        self.device.debug_original_wait = True
        with self.assertRaises(recovery.CaptureRepairError): self.run_manual()
        self.assertFalse(any("set-debug-app" in row or "clear-debug-app" in row for row in self.device.calls))

    def test_foreign_developer_selection_refused_without_change(self):
        self.device.debug_current = "another.debug.app"
        with self.assertRaises(recovery.CaptureRepairError) as failure: self.run_manual()
        self.assertEqual(failure.exception.action["reason"], "restore-app-launch-unavailable")
        self.assertFalse(any("set-debug-app" in row or "clear-debug-app" in row for row in self.device.calls))
        self.assertEqual(self.device.debug_current, "another.debug.app")

    def test_foreign_persistent_setting_is_retained(self):
        self.device.persistent_debug = "another.debug.app"
        with self.assertRaises(recovery.CaptureRepairError): self.run_manual()
        self.assertFalse(any("set-debug-app" in row or "clear-debug-app" in row for row in self.device.calls))

    def test_stale_wait_log_cannot_authorize_restore(self):
        self.device.waiting_marker = False
        with self.assertRaises(recovery.CaptureRepairError) as failure: self.run_manual()
        self.assertEqual(failure.exception.action["reason"], "restore-app-storage-unavailable")
        self.assertIsNone(self.view._storage_process)
        self.assertFalse(self.device.process_started)
        self.assertFalse(any("-STOP" in row for row in self.device.calls))
        self.assertFalse(self.view._manual_debug_receipt().exists())

    def test_host_restart_reconciles_durable_owned_wait_before_new_request(self):
        self.run_manual()
        self.view._storage_process = None  # Simulate lost host memory, not cleanup.
        resumed = recovery.AppStorageView(self.adb, "usb", self.device.hardware)
        resumed._reconcile_manual_debug_wait()
        self.assertFalse(self.device.process_started)
        self.assertFalse(self.view._manual_debug_receipt().exists())
        self.assertEqual(self.device.debug_current, "null")

    def test_changed_source_declines_old_debugger_receipt(self):
        self.run_manual()
        self.adb.manual_storage_identity = {**self.adb.manual_storage_identity, "apkSha256": "d" * 64}
        resumed = recovery.AppStorageView(self.adb, "usb", self.device.hardware)
        with self.assertRaisesRegex(recovery.RecoveryError, "differs"): resumed._reconcile_manual_debug_wait()
        self.adb.manual_storage_identity["apkSha256"] = "b" * 64
        self.assertTrue(self.view._manual_debug_receipt().exists())

    def test_foreign_setting_after_wait_is_not_cleared(self):
        self.run_manual()
        self.device.persistent_debug = "another.debug.app"
        with self.assertRaisesRegex(recovery.RecoveryError, "Persistent developer"): self.view._close_storage_process()
        self.assertTrue(self.view._manual_debug_receipt().exists())
        self.assertFalse(any("clear-debug-app" in row for row in self.device.calls))
        self.device.persistent_debug = "null"

    def test_no_process_while_user_waiting_has_zero_progress_and_safe_timeout(self):
        self.device.user_launch_after = 10000
        with mock.patch.object(recovery.time, "sleep"), contextlib.redirect_stdout(io.StringIO()) as output:
            with self.assertRaises(recovery.CaptureRepairError): self.view._process_storage_root()
        progress = [json.loads(row.split(" ", 1)[1]) for row in output.getvalue().splitlines() if row.startswith("GHVRQ_PROGRESS ")]
        self.assertTrue(all(row["done"] == 0 for row in progress))
        self.assertFalse(self.view._manual_debug_receipt().exists())
        self.assertEqual(self.device.debug_current, "null")

    def test_failed_clear_keeps_durable_owner_until_confirmed_retry(self):
        self.run_manual()
        self.device.clear_denied = True
        with self.assertRaisesRegex(recovery.RecoveryError, "confirm release"): self.view._close_storage_process()
        self.assertTrue(self.view._manual_debug_receipt().exists())
        self.device.clear_denied = False
        self.view._close_storage_process()
        self.assertFalse(self.view._manual_debug_receipt().exists())

    def test_recovery_journal_wait_survives_host_memory_loss(self):
        source = types.SimpleNamespace(apk=self.device.new_apk, sha256=recovery.file_hash(self.device.new_apk), input_key="a" * 64)
        def engine(**options):
            return recovery.Recovery(self.adb, "usb", self.device.hardware, source, self.root / "workspace", data_policy="saves", **options)
        original = engine()
        blocked = recovery.CaptureRepairError("Library launch required", root="external", reason="restore-app-launch-unavailable", attempts=2, total=2)
        with mock.patch.object(original, "_restore", side_effect=blocked), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(recovery.RecoveryError): original.recover(detected=True)
        current = engine(manual_storage_launch=original.journal["transaction"])
        current.load()
        current._manual_storage_launch_enabled = True
        with mock.patch.object(recovery.time, "sleep"), contextlib.redirect_stdout(io.StringIO()): current._process_storage_root()
        self.assertIsInstance(json.loads(current.path.read_text())["manualDebugWait"], dict)
        current._storage_process = None
        current._manual_debug_armed = False
        resumed = engine()
        resumed.load(); resumed._reconcile_manual_debug_wait()
        self.assertIsNone(json.loads(current.path.read_text())["manualDebugWait"])
        self.assertFalse(self.device.process_started)
        self.assertEqual(self.device.debug_current, "null")

    def test_deadline_uses_real_clock_without_claiming_work_percentage(self):
        self.device.user_launch_after = 10000
        clock = iter((0, 0, 301))
        with mock.patch.object(recovery.time, "monotonic", side_effect=lambda: next(clock)), \
                mock.patch.object(recovery.time, "sleep"), contextlib.redirect_stdout(io.StringIO()) as output:
            with self.assertRaises(recovery.CaptureRepairError): self.view._process_storage_root()
        self.assertEqual(sum(row[:2] == ("shell", "pidof") for row in self.device.calls), 1)
        progress = [json.loads(row.split(" ", 1)[1]) for row in output.getvalue().splitlines() if row.startswith("GHVRQ_PROGRESS ")]
        self.assertTrue(all(row["done"] == 0 for row in progress))
        self.assertFalse(self.view._manual_debug_receipt().exists())


if __name__ == "__main__": unittest.main()
