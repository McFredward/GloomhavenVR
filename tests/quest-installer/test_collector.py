"""Scoped read-only Quest captures, partial failures and real Legacy launcher checks."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools/quest-installer"
sys.path.insert(0, str(TOOLS))
SPEC = importlib.util.spec_from_file_location("quest_hardware_collector", TOOLS / "collector.py")
collector = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(collector)
PWSH = os.environ.get("GHVR_PWSH_PATH") or shutil.which("pwsh")
KEY = "a" * 64
APK_SHA = "b" * 64


class CaptureAdb:
    def __init__(self):
        self.calls = []
        self.devices = {"USB-QUEST": "device"}
        self.models = {"USB-QUEST": "Quest 3"}
        self.hardware = {"USB-QUEST": "HARDWARE-1"}
        self.connected = {}
        self.logcat = "01-01 12:00:00 100 200 E Unity: Error fixture\n[GloomhavenVR Quest] ModBuild=609 input=" + KEY + "\n"
        self.logcat_error = False
        self.logcat_timeout = False
        self.pids = "1234"
        self.app_files = {name: ("[GloomhavenVR Quest] ModBuild=609 input=" + KEY + "\n" if name.endswith(".log") else '{"diagnostic":true}\n') for name in collector.APP_FILES}
        self.pull_denied = False
        self.pull_stream = "stdout"
        self.pull_code = 0
        self.pull_payload = None
        self.pull_receipt = None
        self.pull_create_file = True
        self.run_as_allowed = False
        self.apk_path = "package:/data/app/~~fixture/dev.gloomhavenvr.quest-abc/base.apk"
        self.apk_sha = APK_SHA
        self.oversized = False
        self.fail_metadata = False

    def wifi(self, hardware="HARDWARE-1", model="Quest 3"):
        address = "192.168.1.42:5555"
        self.devices[address] = "device"
        self.models[address], self.hardware[address] = model, hardware
        self.connected[address] = True

    def __call__(self, command, **options):
        args = command[1:]
        self.calls.append(args)
        assert isinstance(command, list) and options["shell"] is False and options["timeout"] <= 30
        assert not any(value in args for value in ("install", "uninstall", "tcpip", "kill-server", "disconnect", "force-stop", "start", "reboot", "rm"))
        if "-c" in args:
            assert "stat" in args or "head" in args  # Never logcat -c.
        out, err, code = "", "", 0
        if args == ["devices", "-l"]:
            out = "List of devices attached\n" + "\n".join(serial + " " + state for serial, state in self.devices.items())
        elif args[0] == "connect":
            if self.connected.get(args[1]):
                out = "connected to " + args[1]
            else:
                out, code = "failed to connect", 1
        elif args[0] == "-s":
            serial, action = args[1], args[2:]
            assert serial in self.devices and self.devices[serial] == "device"
            if action[:2] == ["shell", "getprop"]:
                out = {"ro.product.model": self.models.get(serial, "Pixel 8"), "ro.product.name": "eureka",
                       "ro.serialno": self.hardware.get(serial, "UNKNOWN"), "ro.build.version.release": "14",
                       "ro.build.version.incremental": "firmware-fixture", "ro.build.fingerprint": "Meta/eureka/fixture"}[action[2]]
            elif action == ["shell", "pidof", collector.installer.PACKAGE]:
                out, code = self.pids, 0 if self.pids else 1
            elif action[:2] == ["shell", "logcat"]:
                assert "-d" in action and "3000" in action and "threadtime" in action
                if self.logcat_timeout:
                    raise subprocess.TimeoutExpired(command, options["timeout"], output=self.logcat.encode())
                out, code, err = self.logcat, 1 if self.logcat_error else 0, "fixture interrupted logcat" if self.logcat_error else ""
            elif action == ["shell", "dumpsys", "package", collector.installer.PACKAGE]:
                if self.fail_metadata:
                    raise OSError("fixture metadata unavailable")
                out = "Package [dev.gloomhavenvr.quest]\nversionCode=609 minSdk=29\nversionName=0.1-probe\n"
            elif action == ["shell", "pm", "path", collector.installer.PACKAGE]:
                out = self.apk_path
            elif action[:4] == ["shell", "stat", "-c", "%s"]:
                path = action[4]
                if path.startswith(collector.REMOTE_FILES):
                    name = Path(path).name
                    if self.pull_denied or name not in self.app_files:
                        out, code = "stat: Permission denied", 1
                    else:
                        out = str(collector.MAX_FILE + 1 if self.oversized else len(self.app_files[name].encode()))
                else:
                    assert path == self.apk_path.removeprefix("package:")
                    out = "100000"
            elif action[0] == "pull":
                source, destination = action[1:]
                assert source.startswith(collector.REMOTE_FILES + "/") and Path(source).name in collector.APP_FILES
                content = self.app_files[Path(source).name].encode() if self.pull_payload is None else self.pull_payload
                if self.pull_create_file:
                    Path(destination).write_bytes(content)
                receipt = self.pull_receipt if self.pull_receipt is not None else source + ": 1 file pulled, 0 skipped. 1.2 MB/s (" + str(len(content)) + " bytes in 0.009s)\n"
                if self.pull_stream == "stderr":
                    err = receipt
                else:
                    out = receipt
                code = self.pull_code
            elif action[:2] == ["shell", "sha256sum"]:
                assert action[2] == self.apk_path.removeprefix("package:")
                out = self.apk_sha + "  " + action[2]
            elif action[:3] == ["exec-out", "run-as", collector.installer.PACKAGE]:
                assert action[3:6] == ["head", "-c", str(collector.MAX_FILE + 1)]
                name = Path(action[6]).name
                assert name in collector.APP_FILES
                assert action[6] in (collector.REMOTE_FILES + "/" + name, "files/" + name)
                if self.run_as_allowed and name in self.app_files:
                    out = self.app_files[name]
                else:
                    out, code = "run-as: package not debuggable", 1
            else:
                raise AssertionError("Unexpected capture command " + repr(action))
        else:
            raise AssertionError("Unexpected global command " + repr(args))
        return subprocess.CompletedProcess(command, code, out, err)


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest capture spaces ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.config = self.root / "settings/wireless-install.json"
        self.output = self.root / "Quest Error Logs"
        self.adb = self.root / "adb.exe"
        self.adb.write_text("fixture executable")
        self.fake = CaptureAdb()

    def remember(self, hardware="HARDWARE-1"):
        self.config.parent.mkdir(parents=True, exist_ok=True)
        self.config.write_text(json.dumps({"schema": 1, "adb": str(self.adb), "endpoint": "192.168.1.42:5555", "deviceSerial": hardware}))

    def run_cli(self, *extra):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            result = collector.main(["--config", str(self.config), "--output-root", str(self.output), "--adb", str(self.adb), *extra], runner=self.fake)
        self.stdout, self.stderr = out.getvalue(), err.getvalue()
        return result

    def capture(self):
        archives = list(self.output.glob("quest-capture-*.zip"))
        self.assertEqual(len(archives), 1)
        with zipfile.ZipFile(archives[0]) as zipped:
            files = {name: zipped.read(name) for name in zipped.namelist()}
        return json.loads(files["manifest.json"]), files

    def test_authorized_usb_capture_is_read_only_and_needs_no_apk(self):
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertEqual(manifest["status"], "complete")
        self.assertEqual(manifest["device"]["transport"], "usb")
        self.assertEqual(manifest["device"]["hardwareSerial"], "HARDWARE-1")
        self.assertEqual(manifest["app"]["versionCode"], "609")
        self.assertIn("Error fixture", files["logcat.txt"].decode())
        self.assertTrue(set(collector.APP_FILES).issubset(files))
        self.assertFalse(self.config.exists())

    def test_remembered_wifi_identity_is_verified_and_settings_unchanged(self):
        self.remember()
        self.fake.wifi()
        old = self.config.read_bytes()
        self.assertEqual(self.run_cli(), 0, self.stderr)
        self.assertEqual(self.capture()[0]["device"]["transport"], "wifi")
        self.assertEqual(self.config.read_bytes(), old)

    def test_windows_pull_success_receipt_on_stderr_retains_transferred_files(self):
        self.fake.pull_stream = "stderr"
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertEqual(manifest["status"], "complete")
        for name, content in self.fake.app_files.items():
            self.assertEqual(files[name], content.encode())
        self.assertFalse(any("run-as" in call for call in self.fake.calls))
        self.assertTrue(all(row["kind"] == "app-file-pull" for row in manifest["files"] if row["path"] in collector.APP_FILES))

    def test_actual_b611_transfer_sizes_with_stderr_receipts_are_retained(self):
        self.fake.pull_stream = "stderr"
        self.fake.app_files = {"quest-hardware.log": "x" * 11346, "quest-hardware-state.json": "x" * 10728,
                               "quest-hardware-storage.json": "x" * 74}
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        for name, content in self.fake.app_files.items():
            self.assertEqual(len(files[name]), len(content))
        self.assertEqual([row["item"] for row in manifest["errors"]], ["quest-hardware.log.previous"])

    def test_failed_transfer_does_not_keep_partial_file_even_with_success_text(self):
        self.fake.pull_stream = "stderr"
        self.fake.pull_code = 1
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertEqual(manifest["status"], "partial")
        self.assertTrue(any("exit=1" in row["reason"] for row in manifest["errors"]))
        self.assertFalse(set(collector.APP_FILES).intersection(files))

    def test_exit_zero_success_text_plus_error_is_rejected(self):
        self.fake.pull_receipt = "1 file pulled, 0 skipped.\nadb: error: incomplete transfer"
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertTrue(any("transfer error" in row["reason"] for row in manifest["errors"]))
        self.assertFalse(set(collector.APP_FILES).intersection(files))

    def test_empty_transfer_is_rejected_when_remote_file_is_not_empty(self):
        self.fake.pull_payload = b""
        self.fake.pull_stream = "stderr"
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertTrue(any("did not match the remote" in row["reason"] for row in manifest["errors"]))
        self.assertFalse(set(collector.APP_FILES).intersection(files))

    def test_partial_or_oversized_transfer_is_rejected_against_remote_size(self):
        for payload in (b"partial", b"x" * 1000):
            self.fake.pull_payload = payload
            self.output = self.root / ("capture-size-" + str(len(payload)))
            self.assertEqual(self.run_cli(), 0, self.stderr)
            manifest, files = self.capture()
            self.assertTrue(any("did not match the remote" in row["reason"] for row in manifest["errors"]))
            self.assertFalse(set(collector.APP_FILES).intersection(files))

    def test_missing_transfer_file_is_rejected_even_with_success_receipt(self):
        self.fake.pull_create_file = False
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertTrue(any("missing or too large" in row["reason"] for row in manifest["errors"]))
        self.assertFalse(set(collector.APP_FILES).intersection(files))

    def test_receipt_with_wrong_byte_count_is_rejected(self):
        self.fake.pull_receipt = "1 file pulled, 0 skipped. 1.2 MB/s (1 bytes in 0.009s)"
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertTrue(any("receipt byte count" in row["reason"] for row in manifest["errors"]))
        self.assertFalse(set(collector.APP_FILES).intersection(files))

    def test_empty_remote_file_is_reported_without_pull(self):
        self.fake.app_files["quest-hardware.log"] = ""
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertNotIn("quest-hardware.log", files)
        self.assertTrue(any("remote file is empty" in row["reason"] for row in manifest["errors"]))
        self.assertFalse(any(call[2] == "pull" and call[3].endswith("/quest-hardware.log") for call in self.fake.calls if len(call) > 3))

    def test_empty_receipt_does_not_silently_accept_existing_transfer_file(self):
        self.fake.pull_receipt = ""
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertTrue(any("did not confirm success" in row["reason"] for row in manifest["errors"]))
        self.assertFalse(set(collector.APP_FILES).intersection(files))

    def test_wrong_wifi_identity_fails_without_capturing_other_headset(self):
        self.remember()
        self.fake.wifi(hardware="OTHER-QUEST")
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("different headset", self.stderr)
        self.assertFalse(list(self.output.glob("*.zip")))
        self.assertFalse(any("logcat" in call for call in self.fake.calls))

    def test_missing_wifi_falls_back_to_usb_without_enabling_tcpip(self):
        self.remember()
        self.assertEqual(self.run_cli(), 0, self.stderr)
        self.assertEqual(self.capture()[0]["device"]["transport"], "usb")
        self.assertFalse(any("tcpip" in call for call in self.fake.calls))

    def test_two_transports_for_same_headset_are_one_physical_choice(self):
        self.fake.wifi()
        self.assertEqual(self.run_cli(), 0, self.stderr)
        self.assertEqual(self.capture()[0]["device"]["adbSerial"], "USB-QUEST")

    def test_multiple_actual_quests_require_serial(self):
        self.fake.devices["USB-2"] = "device"
        self.fake.models["USB-2"] = "Quest 3"
        self.fake.hardware["USB-2"] = "HARDWARE-2"
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("Multiple Quests", self.stderr)
        self.assertEqual(self.run_cli("--serial", "USB-QUEST"), 0, self.stderr)

    def test_setup_prefers_usb_without_connecting_or_changing_remembered_identity(self):
        self.remember("OLD-QUEST")
        self.fake.wifi(hardware="OLD-QUEST")
        old = self.config.read_bytes()
        self.assertEqual(self.run_cli("--setup"), 0, self.stderr)
        self.assertFalse(any(call[0] == "connect" for call in self.fake.calls))
        self.assertEqual(self.config.read_bytes(), old)

    def test_unauthorized_quest_reports_debugging_prompt(self):
        self.fake.devices["USB-QUEST"] = "unauthorized"
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("USB debugging prompt", self.stderr)
        self.assertFalse(list(self.output.glob("*.zip")))

    def test_phone_is_not_selected(self):
        self.fake.models["USB-QUEST"] = "Pixel 8"
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(any("logcat" in call for call in self.fake.calls))

    def test_no_pid_and_inaccessible_app_files_still_save_recent_logcat(self):
        self.fake.pids = ""
        self.fake.app_files = {}
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertEqual(manifest["status"], "partial")
        self.assertEqual(manifest["app"]["processIds"], [])
        self.assertEqual(len(manifest["errors"]), 4)
        self.assertIn("logcat.txt", files)

    def test_run_as_fixed_allowlist_fallback_preserves_app_logs(self):
        self.fake.pull_denied = True
        self.fake.run_as_allowed = True
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertEqual(manifest["status"], "complete")
        self.assertTrue(all(row["kind"] == "app-file-run-as" for row in manifest["files"] if row["path"] in collector.APP_FILES))
        self.assertIn("quest-hardware-state.json", files)

    def test_partial_nonzero_logcat_is_retained_with_manifest_error(self):
        self.fake.logcat_error = True
        self.fake.app_files = {}
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertIn("logcat.txt", files)
        self.assertTrue(any(row["item"] == "logcat" for row in manifest["errors"]))

    def test_timed_out_logcat_keeps_partial_bytes(self):
        self.fake.logcat_timeout = True
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertIn(b"Error fixture", files["logcat.txt"])
        self.assertTrue(any("timed out" in row["reason"] for row in manifest["errors"]))

    def test_metadata_launch_failure_does_not_discard_logs(self):
        self.fake.fail_metadata = True
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertIn("logcat.txt", files)
        self.assertTrue(any(row["item"] == "package" for row in manifest["errors"]))

    def test_installed_apk_path_injection_is_not_sent_to_shell(self):
        self.fake.apk_path = "package:/data/app/fixture/base.apk;rm -rf /"
        self.assertEqual(self.run_cli(), 0, self.stderr)
        self.assertFalse(any("sha256sum" in call for call in self.fake.calls))
        self.assertTrue(any(row["item"] == "installed-apk-path" for row in self.capture()[0]["errors"]))

    def test_local_provenance_is_distinct_and_corroboration_explicit(self):
        self.config.parent.mkdir()
        handoff = {"schema": 1, "apk": "fixture.apk", "apkSha256": APK_SHA, "package": collector.installer.PACKAGE,
                   "buildReport": {"inputKey": KEY}, "doNotCopy": "private unexpected data"}
        (self.config.parent / "handoff.json").write_text(json.dumps(handoff))
        self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        row = manifest["localProvenance"][0]
        self.assertTrue(row["matchesInstalledApkHash"])
        self.assertTrue(row["matchesObservedBannerInput"])
        self.assertNotIn("doNotCopy", row)
        self.assertIn("can be historical", manifest["provenanceNote"])
        self.assertFalse(any(name.endswith(".apk") for name in files))

    def test_oversized_app_files_are_not_pulled(self):
        self.fake.oversized = True
        self.assertEqual(self.run_cli(), 0, self.stderr)
        self.assertFalse(any("pull" in call for call in self.fake.calls))
        self.assertEqual(len(self.capture()[0]["errors"]), 4)

    def test_logcat_is_capped_with_explicit_partial_report(self):
        self.fake.logcat = "x" * 4096
        with mock.patch.object(collector, "MAX_LOGCAT", 1024):
            self.assertEqual(self.run_cli(), 0, self.stderr)
        manifest, files = self.capture()
        self.assertEqual(len(files["logcat.txt"]), 1024)
        self.assertTrue(any("truncated" in row["reason"] for row in manifest["errors"]))

    def test_no_readable_logs_yields_failure_with_private_error_manifest(self):
        self.fake.logcat = ""
        self.fake.app_files = {}
        self.assertEqual(self.run_cli(), 1)
        self.assertEqual(self.capture()[0]["status"], "no-readable-logs")


@unittest.skipUnless(PWSH, "PowerShell unavailable; set GHVR_PWSH_PATH for launcher checks")
class CollectorLauncherTests(unittest.TestCase):
    def test_actual_legacy_bootstrap_forwards_space_paths_flags_and_exit_code(self):
        with tempfile.TemporaryDirectory(prefix="collector wrapper spaces ") as directory:
            root = Path(directory)
            scripts, tools = root / "scripts", root / "tools/quest-installer"
            scripts.mkdir()
            tools.mkdir(parents=True)
            shutil.copyfile(ROOT / "scripts/collect-quest-logs.ps1", scripts / "collect-quest-logs.ps1")
            for name in ("bootstrap.ps1", "requirements.txt"):
                shutil.copyfile(TOOLS / name, tools / name)
            record = root / "args.json"
            (scripts / "collect-quest-logs.py").write_text(
                "import sys,json\nfrom pathlib import Path\n"
                + "Path(" + repr(str(record)) + ").write_text(json.dumps({'args':sys.argv[1:],'isolated':sys.flags.isolated,'utf8':sys.flags.utf8_mode,'noBytecode':sys.dont_write_bytecode}))\n"
                + "raise SystemExit(13)\n")
            quote = lambda value: "'" + str(value).replace("'", "''") + "'"
            command = "$PSNativeCommandArgumentPassing='Legacy'; & " + quote(scripts / "collect-quest-logs.ps1")
            options = {"OutputRoot": root / "error captures", "Config": root / "settings path/config.json",
                       "Adb": root / "platform tools/adb.exe", "QuestHost": "quest.local:5555", "Serial": "USB-QUEST"}
            for key, value in options.items():
                command += " -" + key + " " + quote(value)
            command += " -Setup; exit $LASTEXITCODE"
            result = subprocess.run([PWSH, "-NoProfile", "-Command", command], capture_output=True, text=True, timeout=30)
            self.assertEqual(result.returncode, 13, result.stdout + result.stderr)
            value = json.loads(record.read_text())
            self.assertTrue(value["isolated"] and value["utf8"] and value["noBytecode"])
            flags = {"OutputRoot": "--output-root", "Config": "--config", "Adb": "--adb", "QuestHost": "--host", "Serial": "--serial"}
            for key, flag in flags.items():
                self.assertEqual(value["args"][value["args"].index(flag) + 1], str(options[key]))
            self.assertIn("--setup", value["args"])
            self.assertTrue((scripts / ".quest-venv").is_dir())


if __name__ == "__main__":
    unittest.main()
