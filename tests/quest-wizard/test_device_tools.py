"""The Builder reuses identity-checked ADB and bounded standalone collection."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import device_tools
import state


class ProtocolRunner:
    def __init__(self):
        self.commands = []; self.hardware = "QUEST_TEST_123"; self.authorized = True

    def __call__(self, command, **options):
        self.commands.append(list(map(str, command)))
        args = list(map(str, command[1:])); code = 0; error = ""
        if args[:1] == ["devices"]:
            output = "List of devices attached\nUSB_TEST " + ("device" if self.authorized else "unauthorized") + " product:quest model:Quest_3\n"
            if self.authorized: output += "192.168.1.5:5555 device model:Quest_3\n"
        elif args[:1] == ["connect"]: output = "connected to " + args[1]
        else:
            action = args[2:]
            if action[:2] == ["shell", "getprop"]:
                output = {"ro.product.model": "Quest 3", "ro.product.name": "quest", "ro.serialno": self.hardware}.get(action[2], "Android 12")
            elif action[:3] == ["shell", "ip", "route"]: output = "192.168.1.0/24 dev wlan0 src 192.168.1.5"
            elif action[:1] == ["tcpip"]: output = "restarting in TCP mode port: " + action[1]
            elif action[:2] == ["shell", "pidof"]: output = "123"
            elif action[:2] == ["shell", "logcat"]:
                output = "10-11 15:00:00.000 123 123 I Unity: [Quest startup] ModBuild=671 input=" + "a" * 64 + "\n"
            elif action[:2] == ["shell", "dumpsys"]: output = "versionName=0.1.0.B671.aaaaaaaaaaaa\nversionCode=671"
            elif action[:3] == ["shell", "pm", "path"]: output = "package:/data/app/~~owned-test/base.apk"
            elif action[:2] == ["shell", "sha256sum"]: output = "b" * 64 + "  " + action[-1]
            elif action[:2] == ["shell", "stat"] and action[-1].endswith("base.apk"): output = "3"
            else:
                code = 1; output = ""; error = "stat: No such file or directory"
        if not options.get("text", False): output, error = output.encode(), error.encode()
        return subprocess.CompletedProcess(command, code, output, error)


class DeviceToolsTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.temp = Path(temporary.name); self.store = state.Store(self.temp / "workspace")
        self.runner = ProtocolRunner()
        self.installer, self.collector = device_tools._modules(ROOT)
        self.adb = self.temp / "adb"; self.adb.write_bytes(b"test adb executable")
        self.module_mock = mock.patch.object(device_tools, "_modules", return_value=(self.installer, self.collector))
        self.module_mock.start(); self.addCleanup(self.module_mock.stop)
        self.path_mock = mock.patch.object(self.installer, "adb_path", return_value=self.adb)
        self.path_mock.start(); self.addCleanup(self.path_mock.stop)
        self.tools = device_tools.DeviceTools(ROOT, self.store.root, self.runner)

    def test_status_does_not_start_adb_or_provision_tools(self):
        self.assertEqual(self.tools.status()["status"], "idle")
        self.tools.status(); self.tools.status()
        self.assertEqual(self.runner.commands, []); self.installer.adb_path.assert_not_called()

    def test_connect_reuses_existing_authorized_wifi_and_remembers_identity(self):
        events = []; result = self.tools.connect({}, events.append)
        self.assertEqual(result["status"], "connected")
        remembered = json.loads(self.tools.config.read_text())
        self.assertEqual(remembered["endpoint"], "192.168.1.5:5555")
        self.assertEqual(remembered["deviceSerial"], self.runner.hardware)
        self.assertEqual(result["progress"]["percent"], 100)
        self.assertEqual([event["progress"]["done"] for event in events], sorted(event["progress"]["done"] for event in events))

    def test_explicit_usb_setup_reuses_installers_tcpip_flow(self):
        result = self.tools.connect({"setupWireless": True, "serial": "USB_TEST"})
        self.assertEqual(result["status"], "connected")
        self.assertTrue(any(command[1:] == ["-s", "USB_TEST", "tcpip", "5555"] for command in self.runner.commands))

    def test_wifi_device_picker_uses_identity_guarded_address_selection(self):
        result = self.tools.connect({"serial": "192.168.1.5:5555"})
        self.assertEqual(result["status"], "connected")
        self.assertFalse(any("tcpip" in command for command in self.runner.commands))

    def test_conflicting_picker_and_host_is_a_wait_without_tcpip_change(self):
        result = self.tools.connect({"serial": "192.168.1.5:5555", "host": "192.168.1.6"})
        self.assertEqual(result["status"], "waiting")
        self.assertFalse(any("connect" in command or "tcpip" in command for command in self.runner.commands))

    def test_existing_config_fields_survive_connection(self):
        state.atomic_json(self.tools.config, {"schema": 1, "deviceSerial": self.runner.hardware, "profile": "retain"})
        self.tools.connect({})
        self.assertEqual(json.loads(self.tools.config.read_text())["profile"], "retain")

    def test_wrong_remembered_headset_is_not_overridden(self):
        config = {"schema": 1, "endpoint": "192.168.1.5:5555", "deviceSerial": "ANOTHER_QUEST"}
        state.atomic_json(self.tools.config, config)
        result = self.tools.connect({})
        self.assertEqual(result["status"], "waiting")
        self.assertEqual(json.loads(self.tools.config.read_text()), config)
        self.assertFalse(any("tcpip" in command for command in self.runner.commands))

    def test_unauthorized_usb_is_a_retryable_wait_with_observed_devices(self):
        self.runner.authorized = False
        result = self.tools.connect({})
        self.assertEqual(result["status"], "waiting")
        self.assertEqual(result["devices"][0]["state"], "unauthorized")
        self.assertIn("USB-Debugging", result["message"]["de"])

    def test_auto_adb_provisioning_is_reused_without_browser_executable_argument(self):
        self.tools.connect({})
        self.installer.adb_path.assert_called_once_with(None, None)
        with self.assertRaises(state.WizardError): self.tools.connect({"adb": "/bin/anything"})

    def test_capture_actual_collector_writes_logs_manifest_and_no_game_save_data(self):
        events = []; result = self.tools.capture({}, events.append)
        self.assertEqual(result["status"], "complete")
        path = self.tools.download(result["capture"]["id"])
        with zipfile.ZipFile(path) as zipped:
            names = zipped.namelist()
            self.assertIn("manifest.json", names); self.assertIn("logcat.txt", names)
            self.assertIn("adb-commands.log", names)
            manifest = json.loads(zipped.read("manifest.json"))
            self.assertEqual(manifest["app"]["modBuild"], 671)
            self.assertFalse(any(name.endswith((".apk", ".bundle", ".sav")) for name in names))
        self.assertFalse(any("install" in command or "uninstall" in command or "tcpip" in command for command in self.runner.commands))
        self.assertEqual(events[-1]["progress"]["percent"], 100)
        self.assertTrue(all(event["progress"]["percent"] < 100 for event in events[:-1]))

    def test_capture_available_after_server_restart(self):
        result = self.tools.capture({})
        restarted = device_tools.DeviceTools(ROOT, self.store.root, self.runner)
        self.assertEqual(restarted.download(result["capture"]["id"]), Path(result["capture"]["path"]))

    def test_capture_rejects_foreign_path_id_and_modified_size(self):
        result = self.tools.capture({}); identity = result["capture"]["id"]
        for invalid in ("../../foreign", "x" * 32, None):
            with self.assertRaises(state.WizardError): self.tools.download(invalid)
        Path(result["capture"]["path"]).write_bytes(b"corrupt")
        with self.assertRaises(state.WizardError): self.tools.download(identity)

    def test_capture_rejects_forged_foreign_filename_receipt(self):
        result = self.tools.capture({}); identity = result["capture"]["id"]
        receipt = self.tools.captures / (identity + ".json")
        value = json.loads(receipt.read_text()); value["name"] = "../../foreign.zip"
        state.atomic_json(receipt, value)
        with self.assertRaises(state.WizardError): self.tools.download(identity)

    def test_capture_no_logs_still_offers_bounded_diagnostic_zip(self):
        runner = self.runner
        def no_logs(command, **options):
            result = runner(command, **options)
            if "logcat" in command: result.stdout = "" if options.get("text") else b""
            return result
        self.tools.runner = no_logs
        result = self.tools.capture({})
        self.assertEqual(result["capture"]["status"], "no-readable-logs")
        self.assertTrue(self.tools.download(result["capture"]["id"]).is_file())

    def test_capture_links_are_rejected(self):
        if sys.platform == "win32": self.skipTest("symlink privilege varies")
        result = self.tools.capture({}); path = Path(result["capture"]["path"])
        foreign = self.temp / "foreign.zip"; path.rename(foreign); path.symlink_to(foreign)
        with self.assertRaises(state.WizardError): self.tools.download(result["capture"]["id"])

    def test_invalid_address_never_reaches_shell_or_changes_settings(self):
        result = self.tools.connect({"host": "192.168.1.5; anything"})
        self.assertEqual(result["status"], "waiting")
        self.assertFalse(any("connect" in command for command in self.runner.commands))
        self.assertFalse(self.tools.config.exists())

    def test_pre_cancel_does_not_start_device_process(self):
        cancelled = threading.Event(); cancelled.set()
        result = self.tools.capture({}, cancel=cancelled)
        self.assertEqual(result["status"], "idle"); self.assertEqual(self.runner.commands, [])

    def test_real_process_cancellation_terminates_and_reaps_child(self):
        tools = device_tools.DeviceTools(ROOT, self.store.root)
        cancelled = threading.Event(); timer = threading.Timer(.15, cancelled.set)
        timer.start(); self.addCleanup(timer.join)
        with mock.patch.object(device_tools.subprocess, "Popen", wraps=subprocess.Popen) as started:
            before = time.monotonic()
            with self.assertRaises(state.Cancelled):
                tools._runner(cancelled)([sys.executable, "-c", "import time;time.sleep(30)"], capture_output=True, timeout=30)
            self.assertLess(time.monotonic() - before, 3)
            self.assertEqual(started.call_count, 1)

    def test_real_runner_preserves_binary_and_text_streams(self):
        tools = device_tools.DeviceTools(ROOT, self.store.root)
        result = tools._runner(None)([sys.executable, "-c", "import sys;sys.stdout.buffer.write(b'a\\n\\0b')"],
                                    capture_output=True, text=False, timeout=5)
        self.assertEqual(result.stdout, b"a\n\0b"); self.assertEqual(result.returncode, 0)

    def test_invalid_options_do_not_invoke_adb(self):
        for value in ({"command": "anything"}, {"setupWireless": "yes"}, {"serial": "bad;serial"}):
            with self.assertRaises(state.WizardError): self.tools.connect(value)
        self.assertEqual(self.runner.commands, [])

    def test_loader_does_not_replace_cached_application_modules(self):
        sentinel = object()
        with mock.patch.dict(sys.modules, {"installer": sentinel}):
            original = sys.modules["installer"]
            real = self.module_mock.get_original()[0]
            installer, collector = real(ROOT)
            self.assertIs(sys.modules["installer"], original)
            self.assertIs(collector.installer, installer)


if __name__ == "__main__": unittest.main()
