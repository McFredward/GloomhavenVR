"""Launcher-contract controls; no actual headset launch or policy claim."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_launcher_fixture", Path(__file__).with_name("test_signing_recovery.py"))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
recovery = fixture.recovery


class LauncherDevice(fixture.Device):
    """Model a platform accepting only the documented immersive launcher intent.

    This is an admission negative control, not an emulation of Meta's policy.
    An external success reply without a process follows the source-proven AOSP
    START_ABORTED mapping. Actual FUSE, ART and Meta behavior are not tested here.
    """
    def __call__(self, command, **options):
        action = command[3:] if command[1:2] == ["-s"] else command[1:]
        script = options.get("input", b"").decode("utf-8", errors="replace")
        if script.startswith("set -eu\nprintf 'GHVRQ_STORAGE_POWER"):
            self.calls.append(tuple(action))
            return fixture.subprocess.CompletedProcess(command, 0, "GHVRQ_STORAGE_POWER\n  mWakefulness=Awake\n", "")
        if action[:3] == ["shell", "am", "start"]:
            canonical = ("android.intent.action.MAIN" in action
                         and "android.intent.category.LAUNCHER" in action
                         and "com.oculus.intent.category.VR" in action
                         and "0x10200000" in action)
            if not canonical:
                self.calls.append(tuple(action))
                return fixture.subprocess.CompletedProcess(command, 0, "Starting: Intent { cmp=" + self.launcher + " }", "")
        return super().__call__(command, **options)


class QuestLauncherTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.device = LauncherDevice(self.root / "device")
        self.device.process_enabled = True
        self.log = self.root / "adb.log"
        self.adb = fixture.installer.Adb("adb", self.log, self.device)
        self.view = recovery.AppStorageView(self.adb, "usb", self.device.hardware)
        self.addCleanup(self.view._close_storage_process)

    def test_canonical_request_reaches_exact_process_wait_and_native_pause(self):
        with mock.patch.object(recovery.time, "sleep"), contextlib.redirect_stdout(io.StringIO()):
            actual = self.view._process_storage_root()
        self.assertEqual(actual, "/proc/" + self.device.process_pid + "/root/storage/emulated/0")
        self.assertTrue(self.device.process_stopped)
        starts = [row for row in self.device.calls if row[:3] == ("shell", "am", "start")]
        self.assertEqual(len(starts), 1)
        self.assertIn("-D", starts[0])
        self.assertIn(self.device.launcher, starts[0])
        rows = [json.loads(line) for line in self.log.read_text().splitlines() if line.startswith('{"storageLaunchRequest"')]
        self.assertEqual(rows[0]["storageLaunchRequest"]["processStartProved"], False)
        self.assertTrue(rows[0]["storageLaunchRequest"]["canonicalLauncherIntent"])
        self.assertFalse(any("clear-debug-app" in row or "set-debug-app" in row for row in self.device.calls))

    def test_bare_start_success_cannot_authorize_restore_or_claim_sleep_on_awake_device(self):
        with mock.patch.object(recovery, "launcher_arguments", return_value=["-n", self.device.launcher]), \
                mock.patch.object(recovery.time, "sleep"), contextlib.redirect_stdout(io.StringIO()) as output:
            with self.assertRaises(recovery.CaptureRepairError) as error:
                self.view._process_storage_root()
        self.assertEqual(error.exception.action["reason"], "restore-app-launch-unavailable")
        self.assertIsNone(self.view._storage_process)
        self.assertFalse(any("-STOP" in row for row in self.device.calls))
        self.assertFalse(any("KEYCODE_WAKEUP" in row for row in self.device.calls))
        self.assertEqual(sum(row[:2] == ("shell", "pidof") for row in self.device.calls), 40)
        progress = [json.loads(line.split(' ', 1)[1]) for line in output.getvalue().splitlines() if line.startswith("GHVRQ_PROGRESS ")]
        self.assertTrue(all(row["done"] < row["total"] for row in progress))

    def test_unknown_or_foreign_component_is_refused_before_adb(self):
        for component in (None, "another.app/.Main", recovery.PACKAGE + "-foreign/.Main",
                          recovery.PACKAGE + "/Main\nanything", recovery.PACKAGE + "/Main;reboot"):
            with self.subTest(component=component), self.assertRaises(recovery.RecoveryError):
                recovery.launcher_arguments(component)
        self.assertEqual(self.device.calls, [])
