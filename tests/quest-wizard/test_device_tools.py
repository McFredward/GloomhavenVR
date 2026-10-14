"""The Builder reuses identity-checked ADB and bounded standalone collection."""
import importlib.util
import json
from pathlib import Path
import subprocess
import shutil
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock
from types import SimpleNamespace
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

    def test_connect_prefers_usb_and_remembers_identity_without_network_endpoint(self):
        events = []; result = self.tools.connect({}, events.append)
        self.assertEqual(result["status"], "connected")
        remembered = json.loads(self.tools.config.read_text())
        self.assertEqual(remembered["adbSerial"], "USB_TEST")
        self.assertEqual(remembered["transport"], "usb")
        self.assertNotIn("endpoint", remembered)
        self.assertIn("USB", result["message"]["de"])
        self.assertFalse(any("connect" in command or "tcpip" in command for command in self.runner.commands))
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
        self.assertEqual(events[-1]["progress"]["total"], len(self.tools._capture_targets(self.collector)))
        observed = [event["progress"]["done"] for event in events]
        self.assertEqual(observed, sorted(observed))
        self.assertEqual(max(observed[:-1]), observed[-1] - 1)
        self.assertTrue(any(event.get("activity", {}).get("label") == "installed-apk-hash" for event in events if event.get("activity")))
        self.assertTrue(any(event.get("activity", {}).get("totalBytes", 0) > 0 for event in events if event.get("activity")))

    def test_running_adb_exposes_real_received_bytes_elapsed_and_unchanged_global_progress(self):
        tools = device_tools.DeviceTools(ROOT, self.store.root)
        tools.started = time.monotonic()
        events = []
        tools._step(events.append, 2, 4, "quest-connect", status="connecting")
        command = [sys.executable, "-c", "import sys,time;sys.stdout.buffer.write(b'x'*4096);sys.stdout.flush();time.sleep(1.2)"]
        result = tools._runner(None, events.append)(command, capture_output=True, timeout=5)
        self.assertEqual(len(result.stdout), 4096)
        active = [row["activity"] for row in events if row.get("activity")]
        self.assertTrue(any(row.get("receivedBytes") == 4096 and row["elapsedSeconds"] >= .5 for row in active))
        self.assertTrue(all(row["progress"]["percent"] == 50 for row in events))
        self.assertTrue(all("percent" not in row and "totalBytes" not in row for row in active))
        self.assertFalse(any(thread.name == "quest-device-activity" for thread in threading.enumerate()))

    def test_private_adb_download_reports_measured_bytes_without_raising_global_completion(self):
        repo = self.temp / "repo"
        (repo / "scripts/.quest-adb").mkdir(parents=True)
        (repo / "tools/quest-installer").mkdir(parents=True)
        shutil.copyfile(ROOT / "tools/quest-installer/adb_bootstrap.py", repo / "tools/quest-installer/adb_bootstrap.py")
        tools = device_tools.DeviceTools(repo, self.store.root, self.runner)
        def provision(*_):
            partial = repo / "scripts/.quest-adb/download-owned.partial"
            partial.write_bytes(b"d" * 1024)
            time.sleep(.7)
            partial.write_bytes(b"d" * 8192)
            time.sleep(.7)
            partial.unlink()
            return self.adb
        self.installer.adb_path.side_effect = provision
        events = []; result = tools.connect({}, events.append)
        self.assertEqual(result["status"], "connected")
        downloads = [event for event in events if (event.get("activity") or {}).get("label") == "adb-download"]
        self.assertTrue(any(row["activity"]["receivedBytes"] == 1024 for row in downloads))
        self.assertTrue(any(row["activity"]["receivedBytes"] == 8192 for row in downloads))
        self.assertTrue(all(row["activity"]["totalBytes"] > 8192 and row["progress"]["percent"] == 0 for row in downloads))

    def test_device_support_state_selects_this_jobs_command_log_and_bounded_progress(self):
        self.tools.connect({})
        saved = json.loads(self.tools.state_path.read_text())
        log = Path(saved["log"])
        self.assertEqual(log.parent, self.tools.directory)
        self.assertRegex(log.name, r"^device-tools-[0-9a-f]{32}\.log$")
        self.assertTrue(log.is_file())
        records = [json.loads(line) for line in self.tools.progress_log.read_text().splitlines()]
        self.assertEqual(records[-1]["status"], "connected")
        self.assertTrue(any(row.get("activity") for row in records))
        self.assertTrue(all("devices" not in row and "message" not in row for row in records))
        with mock.patch.object(device_tools, "MAX_PROGRESS_LOG_BYTES", 1):
            self.tools._publish(status="idle")
        self.assertEqual(len(self.tools.progress_log.read_text().splitlines()), 1)

    def test_cancel_during_capture_zip_removes_only_the_unfinished_capture(self):
        cancelled = threading.Event()
        retained = self.tools.captures / "quest-capture-retained.zip"
        retained.write_bytes(b"previous completed capture")
        def stop_at_archive(event):
            if (event.get("activity") or {}).get("label") == "capture-archive": cancelled.set()
        result = self.tools.capture({}, progress=stop_at_archive, cancel=cancelled)
        self.assertEqual(result["status"], "idle")
        self.assertEqual(list(self.tools.captures.iterdir()), [retained])
        self.assertEqual(retained.read_bytes(), b"previous completed capture")
        self.assertIsNone(result.get("capture"))

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


class DeviceStorageTests(unittest.TestCase):
    setUp = DeviceToolsTests.setUp

    def helper(self):
        owner = self
        class Cleanup:
            def __init__(self, adb, serial, hardware, directory):
                self.hardware, self.serial, self.adb = hardware, serial, adb
                self.calls = []
                owner.cleanup = self
            def preview(self, *, progress, cancel):
                cancel(); progress({"done": 1, "total": 2, "detail": "temporary-apk"})
                progress({"done": 2, "total": 2, "detail": "temporary-apk"})
                return {"schema": 1, "complete": False, "planId": "c" * 32,
                        "deviceSerial": self.hardware, "bytes": 1024, "files": 1,
                        "candidates": [{"id": "owned", "bytes": 1024, "files": 1}],
                        "protected": [], "unknown": [{"bytes": None, "files": None}]}
            def reconnect(self, adb, serial):
                self.calls.append("reconnect")
                if adb.identity(serial) != self.hardware: raise ValueError("Different Quest")
                self.adb, self.serial = adb, serial
            def clean(self, plan, *, progress, cancel):
                cancel(); self.calls.append(("clean", plan["planId"]))
                progress({"done": 1, "total": 1, "detail": "temporary-apk"})
                return {"schema": 1, "deviceSerial": self.hardware, "freedBytes": 1024,
                        "deletedFiles": 1, "removed": [], "retained": []}
        patch = mock.patch.object(device_tools, "_cleanup_module", return_value=SimpleNamespace(DeviceCleanup=Cleanup))
        patch.start(); self.addCleanup(patch.stop)

    def test_preview_uses_usb_no_mutation_and_actual_completion_owns_100_percent(self):
        self.helper(); events = []
        result = self.tools.storage_preview({}, events.append)
        self.assertEqual(result["status"], "storage-preview")
        self.assertEqual(result["storagePlan"]["unknown"][0]["bytes"], None)
        self.assertEqual(self.cleanup.calls, [])
        self.assertEqual(self.cleanup.serial, "USB_TEST")
        self.assertFalse(any("connect" in command or "tcpip" in command for command in self.runner.commands))
        self.assertTrue(all(row["progress"]["percent"] is None or row["progress"]["percent"] < 100 for row in events[:-1]))
        self.assertEqual(events[-1]["progress"]["percent"], 100)

    def test_clean_reuses_exact_preview_helper_and_consumes_nonce_once(self):
        self.helper(); self.tools.storage_preview({}); helper = self.cleanup
        result = self.tools.storage_clean({"planId": "c" * 32})
        self.assertEqual(result["status"], "storage-complete")
        self.assertIs(self.cleanup, helper)
        self.assertEqual(helper.calls, ["reconnect", ("clean", "c" * 32)])
        self.assertEqual(result["storageResult"]["freedBytes"], 1024)
        with self.assertRaises(state.WizardError): self.tools.storage_clean({"planId": "c" * 32})

    def test_wrong_nonce_or_path_never_probes_device_or_cleans(self):
        self.helper(); self.tools.storage_preview({}); count = len(self.runner.commands)
        for value in ({"planId": "d" * 32}, {"planId": "c" * 32, "path": "/"}, {"planId": "../"}):
            with self.assertRaises(state.WizardError): self.tools.storage_clean(value)
        self.assertEqual(len(self.runner.commands), count)
        self.assertEqual(self.cleanup.calls, [])

    def test_new_connection_invalidates_previous_preview_and_unknown_data_is_never_sized_zero(self):
        self.helper(); self.tools.storage_preview({}); self.tools.connect({})
        self.assertIsNone(self.tools.status()["storagePlan"])
        with self.assertRaises(state.WizardError): self.tools.storage_clean({"planId": "c" * 32})

    def test_different_quest_refuses_mutation_and_requires_new_preview(self):
        self.helper(); self.tools.storage_preview({}); self.runner.hardware = "OTHER_QUEST"
        result = self.tools.storage_clean({"planId": "c" * 32})
        self.assertEqual(result["status"], "waiting")
        self.assertIsNone(result["storagePlan"])
        self.assertEqual(self.cleanup.calls, ["reconnect"])
        self.assertIn("Quest-Speicher prüfen", result["message"]["de"])

    def test_cancelled_preview_retains_no_deletion_authority(self):
        self.helper(); cancelled = threading.Event(); cancelled.set()
        result = self.tools.storage_preview({}, cancel=cancelled)
        self.assertEqual(result["status"], "cancelled")
        self.assertIsNone(self.tools.cleanup_plan)
        self.assertEqual(self.runner.commands, [])


if __name__ == "__main__": unittest.main()
