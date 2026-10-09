"""Executable ADB command controls for wireless install and retained app data."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_wireless_installer", ROOT / "tools/quest-installer/installer.py")
installer = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = installer
SPEC.loader.exec_module(installer)


class FakeAdb:
    """A strict subprocess substitute: unknown commands and global changes fail."""
    def __init__(self, usb=True):
        self.calls = []
        self.timeouts = []
        self.states = {"USB-QUEST": "device"} if usb else {}
        self.models = {"USB-QUEST": "Quest 3"}
        self.identities = {"USB-QUEST": "HARDWARE-1"}
        self.routes = {"USB-QUEST": "192.168.1.0/24 dev wlan0 proto kernel scope link src 192.168.1.42"}
        self.connections = {}
        self.install_output = "Success"
        self.launch_output = "Starting: Intent { ... }\nStatus: ok\nActivity: " + installer.ACTIVITY
        self.on_connect = None
        self.timeout_command = None
        self.package_output = None
        self.package_returncode = 0
        self.pushed = {}
        self.upload_hash_override = None

    def wireless(self, address="192.168.1.42:5555", hardware="HARDWARE-1", model="Quest 3"):
        self.connections[address] = True
        self.states[address] = "device"
        self.identities[address] = hardware
        self.models[address] = model

    def __call__(self, command, **kwargs):
        self.calls.append(command[1:])
        self.timeouts.append((command[1:], kwargs["timeout"]))
        self.assertions(command, kwargs)
        args = command[1:]
        if self.timeout_command and self.timeout_command in args:
            raise subprocess.TimeoutExpired(command, kwargs["timeout"])
        raw, code = "", 0
        if args == ["devices", "-l"]:
            raw = "List of devices attached\n" + "\n".join(
                serial + "\t" + state + " model:Quest_3" for serial, state in self.states.items())
        elif args[0] == "connect":
            if self.on_connect:
                self.on_connect()
            address = args[1]
            if self.connections.get(address):
                raw = "already connected to " + address
            else:
                raw = "failed to connect to " + address
        elif args[0] == "-s":
            serial, action = args[1], args[2:]
            if action[:2] == ["shell", "getprop"]:
                key = action[2]
                raw = {"ro.product.model": self.models.get(serial, "Pixel 8"),
                       "ro.product.name": "eureka", "ro.serialno": self.identities.get(serial, "unknown")}[key]
            elif action == ["shell", "ip", "route"]:
                raw = self.routes[serial]
            elif action[0] == "tcpip":
                ip = self.routes[serial].split("src ")[-1].split()[0]
                address = ip + ":" + action[1]
                if address not in self.connections:
                    self.wireless(address, self.identities[serial], self.models[serial])
                raw = "restarting in TCP mode port: " + action[1]
            elif action[:2] == ["install", "-r"]:
                self.installed = Path(action[2]).read_bytes()
                raw = self.install_output
            elif action[:3] == ["shell", "am", "force-stop"]:
                assert action[3:] == [installer.PACKAGE]
            elif action[:3] == ["shell", "mkdir", "-p"]:
                assert action[3].startswith("/sdcard/Android/data/" + installer.PACKAGE + "/files/quest-install-input/")
            elif action[0] == "push":
                self.pushed[action[2]] = Path(action[1]).read_bytes()
                raw = "1 file pushed"
            elif action[:2] == ["shell", "sha256sum"]:
                import hashlib
                if action[2] not in self.pushed:
                    return subprocess.CompletedProcess(command, 1, "", "No such file or directory")
                raw = (self.upload_hash_override or hashlib.sha256(self.pushed[action[2]]).hexdigest()) + "  " + action[2]
            elif action[:3] == ["shell", "mv", "-f"]:
                self.pushed[action[4]] = self.pushed.pop(action[3])
            elif action[:3] == ["shell", "rm", "-f"]:
                assert action[3].startswith("/sdcard/Android/data/" + installer.PACKAGE + "/files/quest-install-input/")
                self.pushed.pop(action[3], None)
            elif action == ["shell", "dumpsys", "package", installer.PACKAGE]:
                code = self.package_returncode
                identity = installer.apk_identity(Path(next(call[4] for call in reversed(self.calls) if call[2:4] == ["install", "-r"])))
                raw = self.package_output if self.package_output is not None else (
                    "versionCode=" + str(identity["modBuild"]) + " minSdk=29\nversionName=0.1.0.B"
                    + str(identity["modBuild"]) + "." + identity["inputKey"][:12])
            elif action == ["shell", "am", "start", "-W", "-n", installer.ACTIVITY]:
                raw = self.launch_output
            else:
                raise AssertionError("Unexpected scoped ADB command: " + repr(action))
        else:
            raise AssertionError("Unexpected ADB command: " + repr(args))
        return subprocess.CompletedProcess(command, code, raw, "")

    @staticmethod
    def assertions(command, kwargs):
        assert isinstance(command, list) and kwargs["shell"] is False
        maximum = 1800 if len(command) > 3 and command[3] == "install" else 180
        if len(command) > 3 and command[3] == "push": maximum = 21600
        if len(command) > 4 and command[4] == "sha256sum": maximum = 600
        assert 0 < kwargs["timeout"] <= maximum
        assert not any(word in command for word in ("uninstall", "kill-server", "disconnect", "-d"))


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.apk = self.root / "owned diagnostic.apk"
        self.make_apk()
        self.handoff = self.root / "handoff.json"
        self.metadata = {"schema": 1, "apk": self.apk.name, "apkSha256": installer.digest(self.apk),
                         "package": installer.PACKAGE, "isDiagnostic": True, "isDummy": True,
                         "buildReport": {"buildResult": "Succeeded", "target": "probe"}}
        self.write(self.handoff, self.metadata)
        self.config = self.root / "private/wireless-install.json"
        self.adb = self.root / "platform tools/adb.exe"
        self.adb.parent.mkdir()
        self.adb.write_bytes(b"fake executable used by injected subprocess")
        self.fake = FakeAdb()

    def make_apk(self, payload=b"local APK fixture"):
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("AndroidManifest.xml", payload)
            archive.writestr("lib/arm64-v8a/libil2cpp.so", b"fixture")

    @staticmethod
    def write(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value), encoding="utf-8")

    def run_cli(self, *args):
        output, error = io.StringIO(), io.StringIO()
        base = ["--handoff", str(self.handoff), "--config", str(self.config), "--adb", str(self.adb)]
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(error), mock.patch.object(installer.time, "sleep"):
            code = installer.main(base + list(args), runner=self.fake)
        self.output, self.error = output.getvalue(), error.getvalue()
        return code

    def remember(self, address="192.168.1.42:5555", hardware="HARDWARE-1"):
        self.write(self.config, {"schema": 1, "source": {"kind": "handoff", "path": str(self.handoff)},
                                 "endpoint": address, "deviceSerial": hardware, "adb": str(self.adb)})

    def mutations(self):
        return [call for call in self.fake.calls if "install" in call or "tcpip" in call or "start" in call]

    def campaign_bank(self, file="GloomhavenVR-Quest-content.zip", key=None):
        bank = self.root / "GloomhavenVR-Quest-content.zip"
        with zipfile.ZipFile(bank, "w") as archive:
            archive.writestr("StreamingAssets/Rulebase/Campaign.ruleset", b"original rules fixture")
        key = key or "c" * 64
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("AndroidManifest.xml", b"fixture")
            archive.writestr("assets/Quest/input-manifest.json", json.dumps({"schema": 1,
                "inputKey": key, "target": "game", "mod": {"modBuild": 623}, "profile": {"isDummy": True}}))
            archive.writestr("assets/Quest/content-delivery.json", json.dumps({"schema": 1,
                "inputKey": key, "package": installer.PACKAGE, "files": [{"file": file,
                "archive": "quest-startup-content.zip", "sha256": installer.digest(bank), "size": bank.stat().st_size}]}))
        self.metadata.update(apkSha256=installer.digest(self.apk), isDiagnostic=False)
        self.write(self.handoff, self.metadata)
        return bank

    def test_complete_campaign_bank_is_installed_verified_then_launched(self):
        bank = self.campaign_bank()
        self.assertEqual(self.run_cli(), 0, self.error)
        final = "/sdcard/Android/data/" + installer.PACKAGE + "/files/quest-install-input/" + "c" * 64 + "/quest-startup-content.zip"
        self.assertEqual(self.fake.pushed, {final: bank.read_bytes()})
        receipt = json.loads((self.config.parent / "wireless-last-install.json").read_text())
        self.assertEqual(receipt["content"][0]["sha256"], installer.digest(bank))
        self.assertFalse(receipt["isDiagnostic"])
        actions = [call[2:] for call in self.fake.calls if call[0] == "-s"]
        self.assertLess(next(i for i,c in enumerate(actions) if c[:3] == ["shell", "mv", "-f"]),
                        next(i for i,c in enumerate(actions) if c[:3] == ["shell", "am", "start"]))

    def test_missing_or_changed_campaign_bank_stops_before_adb(self):
        bank = self.campaign_bank(); bank.write_bytes(b"wrong bank")
        self.assertEqual(self.run_cli(), 1)
        self.assertEqual(self.fake.calls, [])
        self.assertIn("differs from this APK", self.error)

    def test_campaign_update_reuses_only_verified_bank_on_same_headset(self):
        bank = self.campaign_bank()
        self.assertEqual(self.run_cli(), 0, self.error)
        pushes = sum("push" in call for call in self.fake.calls)
        self.campaign_bank(key="d" * 64)
        self.assertEqual(self.run_cli(), 0, self.error)
        self.assertEqual(sum("push" in call for call in self.fake.calls), pushes)
        self.assertIn("Reusing the verified Campaign", self.output)
        self.assertEqual(list(self.fake.pushed.values()), [bank.read_bytes()])
        self.assertTrue(all("/" + "d" * 64 + "/" in path for path in self.fake.pushed))

    def test_corrupt_old_device_bank_is_reuploaded_before_old_bank_cleanup(self):
        self.campaign_bank()
        self.assertEqual(self.run_cli(), 0, self.error)
        old = next(iter(self.fake.pushed)); self.fake.pushed[old] = b"changed on device"
        self.campaign_bank(key="e" * 64)
        start = len(self.fake.calls)
        self.assertEqual(self.run_cli(), 0, self.error)
        actions = [call[2:] for call in self.fake.calls[start:] if call[0] == "-s"]
        self.assertLess(next(i for i,c in enumerate(actions) if c[:3] == ["shell", "mv", "-f"]),
                        next(i for i,c in enumerate(actions) if c[:3] == ["shell", "rm", "-f"]))
        self.assertNotIn(old, self.fake.pushed)

    def test_uploaded_campaign_hash_failure_never_launches_or_publishes_success(self):
        self.campaign_bank(); self.fake.upload_hash_override = "0" * 64
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(any("start" in call for call in self.fake.calls))
        self.assertFalse(self.config.exists())
        self.assertFalse((self.config.parent / "wireless-last-install.json").exists())

    def test_campaign_payload_cannot_escape_the_selected_apk_folder(self):
        self.campaign_bank(file="../another-bank.zip")
        self.assertEqual(self.run_cli(), 1)
        self.assertEqual(self.fake.calls, [])

    def test_usb_bootstrap_installs_scoped_retaining_update_and_launches(self):
        self.assertEqual(self.run_cli(), 0, self.error)
        self.assertIn(["-s", "USB-QUEST", "tcpip", "5555"], self.fake.calls)
        self.assertIn(["-s", "192.168.1.42:5555", "install", "-r", str(self.apk)], self.fake.calls)
        value = json.loads(self.config.read_text())
        self.assertEqual((value["deviceSerial"], value["endpoint"]), ("HARDWARE-1", "192.168.1.42:5555"))
        receipt = json.loads((self.config.parent / "wireless-last-install.json").read_text())
        self.assertEqual(receipt["apkSha256"], self.metadata["apkSha256"])
        self.assertTrue(receipt["launched"] and receipt["isDiagnostic"] and receipt["isDummy"])
        self.assertEqual(self.fake.installed, self.apk.read_bytes())

    def test_remembered_wireless_without_usb(self):
        self.remember()
        self.fake = FakeAdb(usb=False)
        self.fake.wireless()
        self.assertEqual(self.run_cli(), 0, self.error)
        self.assertFalse(any("tcpip" in call for call in self.fake.calls))

    def test_large_apk_install_has_bounded_transfer_budget_and_retains_update_flow(self):
        real_stat = Path.stat
        # Actual B612 size; an oversized future artifact must still stay bounded.
        for size in (1_109_713_887, 10 * 1024 * 1024 * 1024):
            with self.subTest(apk_bytes=size):
                self.remember()
                self.fake = FakeAdb(usb=False)
                self.fake.wireless()

                def source_size(path, *args, **kwargs):
                    value = real_stat(path, *args, **kwargs)
                    if path == self.apk:
                        fields = list(value)
                        fields[6] = size
                        return os.stat_result(fields)
                    return value

                with mock.patch.object(Path, "stat", source_size):
                    self.assertEqual(self.run_cli("--no-launch"), 0, self.error)
                installs = [(command, timeout) for command, timeout in self.fake.timeouts if command[2:4] == ["install", "-r"]]
                self.assertEqual(len(installs), 1)
                self.assertGreater(installs[0][1], 1000)
                self.assertLessEqual(installs[0][1], 1800)
                if size > 2 * 1024 * 1024 * 1024:
                    self.assertEqual(installs[0][1], 1800)
                self.assertEqual(installs[0][0], ["-s", "192.168.1.42:5555", "install", "-r", str(self.apk)])
                self.assertFalse(any("tcpip" in command or "start" in command for command in self.fake.calls))
                self.assertEqual(self.fake.installed, self.apk.read_bytes())

    def test_setup_refreshes_stale_ip_and_accepts_deliberately_changed_usb_hardware(self):
        self.remember("192.168.1.10:5555", "OLD-HARDWARE")
        self.assertEqual(self.run_cli("--setup"), 0, self.error)
        self.assertNotIn(["connect", "192.168.1.10:5555"], self.fake.calls)
        self.assertEqual(json.loads(self.config.read_text())["deviceSerial"], "HARDWARE-1")

    def test_missing_wireless_recovers_same_headset_from_usb(self):
        self.remember("192.168.1.10:5555")
        self.assertEqual(self.run_cli(), 0, self.error)
        self.assertEqual(json.loads(self.config.read_text())["endpoint"], "192.168.1.42:5555")

    def test_host_change_requires_same_hardware_and_preserves_custom_port(self):
        self.remember()
        self.fake.wireless("quest-headset:7777")
        self.assertEqual(self.run_cli("--host", "quest-headset:7777"), 0, self.error)
        self.assertEqual(json.loads(self.config.read_text())["endpoint"], "quest-headset:7777")

    def test_wireless_identity_mismatch_never_falls_back_to_install(self):
        self.remember()
        self.fake.wireless(hardware="DIFFERENT-QUEST")
        original = self.config.read_bytes()
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("different headset", self.error)
        self.assertFalse(self.mutations())
        self.assertEqual(self.config.read_bytes(), original)

    def test_usb_identity_mismatch_needs_explicit_setup(self):
        self.remember("192.168.1.10:5555", "DIFFERENT-QUEST")
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("differs from the remembered", self.error)
        self.assertFalse(self.mutations())

    def test_multiple_quests_require_serial_and_selected_usb_only_is_enabled(self):
        self.fake.states["USB-QUEST-2"] = "device"
        self.fake.models["USB-QUEST-2"] = "Quest 3S"
        self.fake.identities["USB-QUEST-2"] = "HARDWARE-2"
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("Multiple USB Quests", self.error)
        self.assertFalse(self.mutations())
        self.assertEqual(self.run_cli("--serial", "USB-QUEST"), 0, self.error)
        self.assertNotIn(["-s", "USB-QUEST-2", "tcpip", "5555"], self.fake.calls)

    def test_unauthorized_usb_is_actionable_and_no_mutation(self):
        self.fake.states["USB-QUEST"] = "unauthorized"
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("USB debugging prompt", self.error)
        self.assertFalse(self.mutations())

    def test_offline_usb_is_not_selected(self):
        self.fake.states["USB-QUEST"] = "offline"
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(self.mutations())

    def test_unauthorized_phone_does_not_block_one_authorized_quest(self):
        self.fake.states["PHONE"] = "unauthorized"
        self.assertEqual(self.run_cli(), 0, self.error)

    def test_authorized_phone_is_not_enabled(self):
        self.fake.states = {"PHONE": "device"}
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(self.mutations())

    def test_phone_at_explicit_host_is_rejected_without_usb_fallback(self):
        self.fake.wireless(model="Pixel 8")
        self.assertEqual(self.run_cli("--host", "192.168.1.42"), 1)
        self.assertIn("not a recognized Meta Quest", self.error)
        self.assertFalse(self.mutations())

    def test_unknown_hardware_serial_never_installs(self):
        self.fake.identities["USB-QUEST"] = "unknown"
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("stable ro.serialno", self.error)
        self.assertFalse(self.mutations())

    def test_dry_run_verifies_actual_source_without_adb_or_writes(self):
        self.adb.unlink()
        self.assertEqual(self.run_cli("--dry-run"), 0, self.error)
        self.assertIn("DIAGNOSTIC", self.output)
        self.assertFalse(self.fake.calls)
        self.assertFalse(self.config.parent.exists())

    def test_tampered_handoff_hash_rejected_before_adb(self):
        self.metadata["apkSha256"] = "0" * 64
        self.write(self.handoff, self.metadata)
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("SHA-256 differs", self.error)
        self.assertFalse(self.fake.calls)

    def test_handoff_relative_and_symlink_path_escape_rejected(self):
        outside = self.root / "outside.apk"
        outside.write_bytes(self.apk.read_bytes())
        inside = self.root / "nested/handoff.json"
        for name in ("../outside.apk", "linked.apk"):
            self.write(inside, {**self.metadata, "apk": name})
            if name == "linked.apk":
                try:
                    (inside.parent / name).symlink_to(outside)
                except OSError:
                    continue  # Windows environments may require developer mode.
            with self.assertRaisesRegex(installer.InstallError, "escapes"):
                installer.resolve_source("handoff", inside)

    def test_handoff_absolute_paths_are_rejected_including_windows_paths(self):
        for name in (str(self.apk.resolve()), r"C:\\other\\outside.apk", r"\\\\host\\share\\outside.apk"):
            self.write(self.handoff, {**self.metadata, "apk": name})
            with self.assertRaisesRegex(installer.InstallError, "relative APK"):
                installer.resolve_source("handoff", self.handoff)

    def test_concurrent_apk_change_before_install_is_rejected(self):
        self.fake.on_connect = lambda: self.make_apk(b"new build arrived during setup")
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("SHA-256 differs", self.error)
        self.assertFalse(any("install" in call for call in self.fake.calls))
        self.assertFalse(self.config.exists())

    def test_concurrent_handoff_change_with_same_apk_is_rejected(self):
        self.fake.on_connect = lambda: self.write(self.handoff, {**self.metadata, "newEvidence": True})
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("source evidence changed", self.error)
        self.assertFalse(any("install" in call for call in self.fake.calls))

    def test_exit_zero_install_failure_is_rejected_and_never_launches(self):
        self.fake.install_output = "Failure [INSTALL_FAILED_INVALID_APK]"
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(any("start" in call for call in self.fake.calls))
        self.assertFalse(self.config.exists())

    def test_signature_mismatch_retains_data_and_does_not_offer_uninstall(self):
        self.remember()
        self.fake.wireless()
        original = self.config.read_bytes()
        marker = self.root / "existing-app-data"
        marker.write_text("valuable save data")
        self.fake.install_output = "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]"
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("Signing-key mismatch", self.error)
        self.assertIn("data were retained", self.error)
        self.assertEqual(marker.read_text(), "valuable save data")
        self.assertEqual(self.config.read_bytes(), original)
        self.assertFalse((self.config.parent / "wireless-last-install.json").exists())

    def test_exit_zero_launch_error_is_not_success(self):
        self.fake.launch_output = "Error type 3\nError: Activity class does not exist."
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(self.config.exists())
        self.assertFalse((self.config.parent / "wireless-last-install.json").exists())

    def test_missing_install_positive_response_is_not_success(self):
        self.fake.install_output = "Performing Streamed Install"
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(self.config.exists())

    def test_connect_retries_are_bounded_when_usb_endpoint_is_unavailable(self):
        self.fake.connections["192.168.1.42:5555"] = False
        self.assertEqual(self.run_cli(), 1)
        self.assertEqual(sum(call[0] == "connect" for call in self.fake.calls), 3)
        self.assertFalse(any("install" in call for call in self.fake.calls))

    def test_subprocess_timeout_is_actionable_and_never_changes_app(self):
        self.fake.timeout_command = "devices"
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("timed out", self.error)
        self.assertFalse(self.mutations())

    def test_no_launch_records_successful_install_without_start_command(self):
        self.assertEqual(self.run_cli("--no-launch"), 0, self.error)
        self.assertFalse(any("start" in call for call in self.fake.calls))
        self.assertFalse(json.loads((self.config.parent / "wireless-last-install.json").read_text())["launched"])

    def test_json_and_schema_failures_are_actionable_without_tracebacks(self):
        for raw in ("{", "[]", '{"schema":2}', '{"schema":1}'):
            self.handoff.write_text(raw)
            self.assertEqual(self.run_cli(), 1)
            self.assertIn("installation stopped", self.error)
            self.assertNotIn("Traceback", self.error)
            self.assertFalse(self.fake.calls)

    def test_invalid_build_report_shape_or_failed_build_is_rejected(self):
        for report in ("Succeeded", [], {"buildResult": "Failed"}):
            self.write(self.handoff, {**self.metadata, "buildReport": report})
            self.assertEqual(self.run_cli(), 1)
            self.assertIn("successful Android build", self.error)
            self.assertNotIn("Traceback", self.error)
            self.assertFalse(self.fake.calls)

    def test_explicit_manual_apk_has_no_builder_or_handoff_requirement(self):
        self.handoff.unlink()
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(installer.main(["--apk", str(self.apk), "--config", str(self.config),
                                             "--adb", str(self.adb), "--no-launch"], runner=self.fake), 0)
        self.assertEqual(json.loads(self.config.read_text())["source"]["kind"], "apk")

    def test_remembered_source_has_priority_over_repository_defaults(self):
        self.remember()
        args = installer.parser().parse_args(["--config", str(self.config)])
        result = installer.select_source(args, json.loads(self.config.read_text()))
        self.assertEqual(result.path, self.handoff)

    def stamped_apk(self, name, build, key="a" * 64):
        apk = self.root / name
        with zipfile.ZipFile(apk, "w") as archive:
            archive.writestr("AndroidManifest.xml", b"fixture manifest")
            archive.writestr("assets/Quest/input-manifest.json", json.dumps({"schema": 1,
                "inputKey": key, "mod": {"modBuild": build}, "profile": {"isDummy": True}}))
        return apk

    def test_merged_archives_choose_newest_embedded_build_over_remembered_handoff(self):
        old = self.stamped_apk("GloomhavenVR-Quest-B999.apk", 616)
        new = self.stamped_apk("GloomhavenVR-Quest-B001.apk", 618)
        self.write(self.handoff, {**self.metadata, "apk": old.name, "apkSha256": installer.digest(old)})
        self.remember()
        args = installer.parser().parse_args(["--config", str(self.config)])
        with mock.patch.object(installer, "REPO", self.root / "repository"):
            result = installer.select_source(args, json.loads(self.config.read_text()))
        self.assertEqual((result.apk, result.mod_build, result.input_key, result.dummy), (new, 618, "a" * 64, True))

    def test_fresh_default_handoff_supersedes_remembered_older_build(self):
        old = self.stamped_apk("old.apk", 616)
        self.write(self.handoff, {**self.metadata, "apk": old.name, "apkSha256": installer.digest(old)})
        self.remember()
        repo = self.root / "repository"
        directory = repo / ".planning/debug/quest3"
        directory.mkdir(parents=True)
        new = self.stamped_apk("new.apk", 618)
        target = directory / new.name
        target.write_bytes(new.read_bytes())
        self.write(directory / "handoff.json", {**self.metadata, "apk": target.name, "apkSha256": installer.digest(target)})
        args = installer.parser().parse_args(["--config", str(self.config)])
        with mock.patch.object(installer, "REPO", repo):
            result = installer.select_source(args, json.loads(self.config.read_text()))
        self.assertEqual((result.apk, result.mod_build), (target, 618))

    def test_explicit_selection_preserves_intended_older_build(self):
        old = self.stamped_apk("old.apk", 616)
        self.stamped_apk("GloomhavenVR-Quest-B618.apk", 618)
        args = installer.parser().parse_args(["--apk", str(old)])
        self.assertEqual(installer.select_source(args, {}).apk, old)

    def test_unavailable_obsolete_automatic_candidates_do_not_block_current_handoff(self):
        repo = self.root / "repository"
        directory = repo / ".planning/debug/quest3"
        directory.mkdir(parents=True)
        new = self.stamped_apk("latest.apk", 618)
        target = directory / new.name
        target.write_bytes(new.read_bytes())
        self.write(directory / "handoff.json", {**self.metadata, "apk": target.name, "apkSha256": installer.digest(target)})
        old = directory / "GloomhavenVR-Quest-B616.apk"
        old.write_bytes(b"interrupted old ZIP download")
        output = repo / ".planning/quest3-local"
        self.write(output / "latest-build.json", {"schema": 1, "apk": "deleted.apk", "receipt": "receipts/deleted.json"})
        self.remember()
        self.handoff.unlink()
        args = installer.parser().parse_args(["--config", str(self.config)])
        with mock.patch.object(installer, "REPO", repo), contextlib.redirect_stdout(io.StringIO()) as console:
            result = installer.select_source(args, json.loads(self.config.read_text()))
        self.assertEqual((result.apk, result.mod_build), (target, 618))
        self.assertIn("Ignored 3 unavailable", console.getvalue())
        args = installer.parser().parse_args(["--apk", str(old)])
        with self.assertRaises(zipfile.BadZipFile):
            installer.select_source(args, {})
        args = installer.parser().parse_args(["--handoff", str(self.handoff)])
        with self.assertRaises(OSError):
            installer.select_source(args, {})

    def test_confirmed_android_build_and_input_are_saved_before_success(self):
        self.apk = self.stamped_apk("GloomhavenVR-Quest-B618.apk", 618)
        self.write(self.handoff, {**self.metadata, "apk": self.apk.name, "apkSha256": installer.digest(self.apk)})
        self.assertEqual(self.run_cli(), 0, self.error)
        receipt = json.loads((self.config.parent / "wireless-last-install.json").read_text())
        self.assertEqual((receipt["modBuild"], receipt["installedVersionCode"], receipt["inputKey"]), (618, 618, "a" * 64))
        self.assertIn("Confirmed installed build: B618", self.output)
        commands = self.fake.calls
        self.assertLess(next(i for i, row in enumerate(commands) if "dumpsys" in row), next(i for i, row in enumerate(commands) if "start" in row))

    def test_wrong_installed_build_or_input_does_not_launch_or_save_success(self):
        self.apk = self.stamped_apk("GloomhavenVR-Quest-B618.apk", 618)
        self.write(self.handoff, {**self.metadata, "apk": self.apk.name, "apkSha256": installer.digest(self.apk)})
        for package in ("versionCode=616\nversionName=0.1.0", "versionCode=618\nversionName=0.1.0.B618.bbbbbbbbbbbb", "", "Error: Can't find package: " + installer.PACKAGE):
            self.fake = FakeAdb()
            self.fake.package_output = package
            self.assertEqual(self.run_cli(), 1)
            self.assertFalse(any("start" in call for call in self.fake.calls))
            self.assertFalse(self.config.exists())
            self.assertFalse((self.config.parent / "wireless-last-install.json").exists())

    def test_package_diagnostic_error_field_does_not_reject_confirmed_installation(self):
        self.apk = self.stamped_apk("GloomhavenVR-Quest-B618.apk", 618)
        self.write(self.handoff, {**self.metadata, "apk": self.apk.name, "apkSha256": installer.digest(self.apk)})
        self.fake.package_output = (
            "Packages:\n  Package [" + installer.PACKAGE + "]:\n"
            "    versionCode=618 minSdk=29 targetSdk=30\n"
            "    versionName=0.1.0.B618.aaaaaaaaaaaa\n"
            "Dexopt state:\n  [" + installer.PACKAGE + "]\n"
            "    path: /data/app/fixture/base.apk\n"
            "      arm64: [status=run-from-apk] [reason=unknown] [primary-abi]\n"
            "        [location is error]\n")
        self.assertEqual(self.run_cli(), 0, self.error)
        self.assertIn("Confirmed installed build: B618", self.output)
        self.assertTrue(any("start" in call for call in self.fake.calls))
        receipt = json.loads((self.config.parent / "wireless-last-install.json").read_text())
        self.assertEqual(receipt["installedVersionCode"], 618)
        self.assertTrue(receipt["launched"])

    def test_package_query_nonzero_exit_still_rejects_matching_build(self):
        self.apk = self.stamped_apk("GloomhavenVR-Quest-B618.apk", 618)
        self.write(self.handoff, {**self.metadata, "apk": self.apk.name, "apkSha256": installer.digest(self.apk)})
        self.fake.package_returncode = 1
        self.assertEqual(self.run_cli(), 1)
        self.assertIn("ADB failed:", self.error)
        self.assertFalse(any("start" in call for call in self.fake.calls))
        self.assertFalse(self.config.exists())
        self.assertFalse((self.config.parent / "wireless-last-install.json").exists())

    def test_handoff_cannot_claim_different_embedded_build(self):
        self.apk = self.stamped_apk("build.apk", 618)
        self.write(self.handoff, {**self.metadata, "apk": self.apk.name, "apkSha256": installer.digest(self.apk), "modBuild": 617})
        self.assertEqual(self.run_cli(), 1)
        self.assertFalse(self.fake.calls)
        self.assertIn("embedded build differs", self.error)

    def test_settings_cannot_overwrite_selected_source(self):
        self.assertEqual(self.run_cli("--config", str(self.handoff)), 1)
        self.assertIn("must not overwrite", self.error)
        self.assertEqual(json.loads(self.handoff.read_text())["apkSha256"], self.metadata["apkSha256"])
        self.assertFalse(self.fake.calls)

    def test_adb_discovery_supports_windows_sdk_paths_and_explicit_precedence(self):
        sdk = self.root / "Android SDK"
        executable = sdk / "platform-tools/adb.exe"
        executable.parent.mkdir(parents=True)
        executable.write_bytes(b"fixture")
        with mock.patch.dict(os.environ, {"ANDROID_HOME": str(sdk)}), mock.patch.object(installer.shutil, "which", return_value=None):
            self.assertEqual(installer.adb_path(None), executable)
            self.assertEqual(installer.adb_path(self.adb), self.adb)
            with self.assertRaises(installer.InstallError):
                installer.adb_path(self.root / "missing-adb.exe")

    def test_builder_import_preserves_preexisting_profile_storage_and_startup_modules(self):
        names = ("profile", "storage", "script_order", "startup", "media", "shaders", "dlcs", "audio", "sprites", "ui_assets",
                 "build_provenance", "_ghvr_wireless_build_provenance", "native_plugins", "_ghvr_wireless_native_plugins",
                 "staging_resume", "_ghvr_wireless_staging_resume", "editor_overlay", "_ghvr_wireless_editor_overlay")
        previous = {name: types.ModuleType("existing_" + name) for name in names}
        profile, storage, startup = (previous[name] for name in ("profile", "storage", "startup"))
        with mock.patch.dict(sys.modules, previous):
            module = installer.builder_module()
            self.assertTrue(callable(module.verified_latest_build))
            self.assertIs(sys.modules["profile"], profile)
            self.assertIs(sys.modules["storage"], storage)
            self.assertIs(sys.modules["startup"], startup)
            self.assertIsNot(module.startup, startup)
            self.assertIs(module.startup.BuildError, module.BuildError)
            self.assertTrue(callable(module.build_provenance.capture))
            self.assertIs(module.build_provenance.BuildError, module.BuildError)
            self.assertIsNot(module.build_provenance, previous["build_provenance"])
            self.assertIsNot(module.native_plugins, previous["native_plugins"])
            self.assertIs(module.native_plugins.BuildError, module.BuildError)
            self.assertIs(module.preparation_identity.editor_overlay.BuildError, module.BuildError)
            self.assertIsNot(module.preparation_identity.editor_overlay, previous["editor_overlay"])
            self.assertTrue(callable(module.full_assets.Journal))
            for name, value in previous.items(): self.assertIs(sys.modules[name], value)

    def test_builder_import_does_not_leave_new_global_dependency_aliases(self):
        with mock.patch.dict(sys.modules):
            names = ("profile", "storage", "script_order", "startup", "media", "shaders", "dlcs", "audio", "sprites", "ui_assets",
                     "build_provenance", "_ghvr_wireless_build_provenance", "native_plugins", "_ghvr_wireless_native_plugins",
                     "staging_resume", "_ghvr_wireless_staging_resume", "editor_overlay", "_ghvr_wireless_editor_overlay")
            for name in names:
                sys.modules.pop(name, None)
            module = installer.builder_module()
            self.assertTrue(callable(module.startup.inspect_project))
            self.assertIs(module.startup.BuildError, module.BuildError)
            self.assertTrue(callable(module.build_provenance.capture))
            self.assertIs(module.build_provenance.BuildError, module.BuildError)
            self.assertTrue(all(name not in sys.modules for name in names))

    def test_builder_import_loads_provenance_in_fresh_isolated_process(self):
        script = """
from pathlib import Path
import sys
import types
sys.path.insert(0, sys.argv[1])
import installer
names = ("profile", "storage", "script_order", "media", "shaders", "dlcs", "audio",
         "sprites", "ui_assets", "staging_resume", "full_assets", "campaign", "mod_assets", "build_provenance", "import_workspace", "native_plugins", "editor_overlay", "preparation_identity", "preparation_metadata", "startup")
aliases = names + tuple("_ghvr_wireless_" + name for name in (*names, "builder"))
assert all(name not in sys.modules for name in aliases), "test dependencies already loaded"
if sys.argv[2] == "preexisting":
    for name in aliases:
        sys.modules[name] = types.ModuleType("sentinel_" + name)
elif sys.argv[2] == "stdlib":
    import profile
missing = object()
previous = {name: sys.modules.get(name, missing) for name in aliases}
paths = list(sys.path)
module = installer.builder_module()
assert callable(module.verified_latest_build)
assert callable(module.build_provenance.capture)
assert Path(module.build_provenance.__file__) == installer.REPO / "tools/quest-builder/build_provenance.py"
assert module.build_provenance.BuildError is module.BuildError
assert module.build_provenance.record_file is module.record_file
assert module.startup.BuildError is module.BuildError
assert callable(module.native_plugins.verify_staged)
assert Path(module.native_plugins.__file__) == installer.REPO / "tools/quest-builder/native_plugins.py"
assert module.native_plugins.BuildError is module.BuildError
assert module.native_plugins.digest is module.digest
assert module.preparation_identity.editor_overlay is module.prepare_resume.preparation_metadata.editor_overlay
assert module.preparation_identity.editor_overlay.BuildError is module.BuildError
assert Path(module.preparation_identity.editor_overlay.__file__) == installer.REPO / "tools/quest-builder/editor_overlay.py"
assert callable(module.full_assets.Journal)
assert sys.path == paths, "builder loader changed import search paths"
for name, value in previous.items():
    assert sys.modules.get(name, missing) is value, "dependency alias was not restored: " + name
print("isolated builder provenance and module restoration passed")
"""
        for state in ("absent", "preexisting", "stdlib"):
            with self.subTest(state=state):
                result = subprocess.run(
                    [sys.executable, "-I", "-B", "-c", script, str(ROOT / "tools/quest-installer"), state],
                    cwd=self.root, text=True, capture_output=True, timeout=60)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn("isolated builder provenance and module restoration passed", result.stdout)

    def test_builder_provenance_import_failure_restores_dependency_modules(self):
        repo = self.root / "isolated provenance repo"
        tools = repo / "tools/quest-builder"
        tools.mkdir(parents=True)
        (tools / "profile.py").write_text("PROFILE_FIXTURE = True\n")
        (tools / "storage.py").write_text("class BuildError(RuntimeError): pass\n")
        (tools / "build_provenance.py").write_text(
            "from storage import BuildError\nraise BuildError('fixture provenance rejected')\n")
        for name in ("startup", "builder"):
            (tools / (name + ".py")).write_text("raise AssertionError('later dependency must not execute')\n")
        names = ("profile", "storage", "build_provenance", "startup", "builder")
        aliases = tuple(name for name in names if name != "builder") + tuple("_ghvr_wireless_" + name for name in names)
        for state in ("absent", "preexisting"):
            with self.subTest(state=state), mock.patch.object(installer, "REPO", repo), mock.patch.dict(sys.modules):
                previous = {}
                for name in aliases:
                    if state == "preexisting":
                        previous[name] = sys.modules[name] = types.ModuleType("sentinel_" + name)
                    else:
                        sys.modules.pop(name, None)
                with self.assertRaisesRegex(RuntimeError, "fixture provenance rejected"):
                    installer.builder_module()
                for name in aliases:
                    if state == "preexisting":
                        self.assertIs(sys.modules[name], previous[name])
                    else:
                        self.assertNotIn(name, sys.modules)

    def test_builder_startup_import_failure_restores_preexisting_dependency_modules(self):
        repo = self.root / "isolated builder repo"
        tools = repo / "tools/quest-builder"
        tools.mkdir(parents=True)
        (tools / "profile.py").write_text("PROFILE_FIXTURE = True\n")
        (tools / "storage.py").write_text("class BuildError(RuntimeError): pass\n")
        (tools / "startup.py").write_text("from storage import BuildError\nraise BuildError('fixture startup rejected')\n")
        (tools / "builder.py").write_text("raise AssertionError('builder must not execute after startup failure')\n")
        previous = {name: types.ModuleType("existing_" + name) for name in ("profile", "storage", "startup")}
        with mock.patch.object(installer, "REPO", repo), mock.patch.dict(sys.modules, previous):
            with self.assertRaisesRegex(RuntimeError, "fixture startup rejected"):
                installer.builder_module()
            for name, module in previous.items():
                self.assertIs(sys.modules[name], module)

    def test_builder_source_verifies_real_stage_receipt_and_rejects_output_escape(self):
        output = self.root / "output"
        output.mkdir()
        target = output / "signed.apk"
        target.write_bytes(self.apk.read_bytes())
        module = installer.builder_module()
        key = "f" * 64
        details = {"apkSha256": installer.digest(target), "package": installer.PACKAGE}
        with contextlib.redirect_stdout(io.StringIO()):
            receipt = module.Stages(output).run("build", key, lambda: ([target], details))
        latest = {"schema": 1, "receipt": "receipts/build/" + key + ".json", "apk": target.name}
        self.write(output / "latest-build.json", latest)
        self.assertEqual(installer.resolve_source("output-root", output).apk, target)
        self.write(output / "latest-build.json", {**latest, "apk": "../owned diagnostic.apk"})
        with self.assertRaisesRegex(RuntimeError, "changed"):
            installer.resolve_source("output-root", output)
        self.write(output / "latest-build.json", {**latest, "receipt": "../../unrelated.json"})
        with self.assertRaisesRegex(RuntimeError, "outside"):
            installer.resolve_source("output-root", output)

    def test_host_syntax_rejects_shell_syntax_or_invalid_ports(self):
        for value in ("10.0.0.1:0", "host:70000", "host;echo x", "--help", "[::1]:5555"):
            with self.assertRaises(installer.InstallError):
                installer.endpoint(value)
        self.assertEqual(installer.endpoint("quest.local"), "quest.local:5555")

    @contextlib.contextmanager
    def missing_windows_adb(self):
        with mock.patch.object(installer.sys, "platform", "win32"), \
                mock.patch.object(installer.shutil, "which", return_value=None), \
                mock.patch.dict(os.environ, {"LOCALAPPDATA": str(self.root / "local"),
                                             "APPDATA": str(self.root / "roaming")}, clear=True):
            yield

    def test_existing_explicit_or_remembered_adb_avoids_download(self):
        with self.missing_windows_adb(), mock.patch.object(installer, "managed_windows_adb") as provision:
            self.assertEqual(installer.adb_path(self.adb), self.adb)
            self.assertEqual(installer.adb_path(None, self.adb), self.adb)
            provision.assert_not_called()

    def test_windows_missing_adb_provisions_local_tools(self):
        with self.missing_windows_adb(), \
                mock.patch.object(installer, "managed_windows_adb", return_value=self.adb) as provision:
            self.assertEqual(installer.adb_path(None), self.adb)
            provision.assert_called_once_with()

    def test_explicit_missing_adb_stops_without_download(self):
        with self.missing_windows_adb(), mock.patch.object(installer, "managed_windows_adb") as provision:
            with self.assertRaisesRegex(installer.InstallError, "supplied ADB"):
                installer.adb_path(self.root / "missing adb.exe")
            provision.assert_not_called()

    def test_remembered_managed_adb_is_revalidated_before_use(self):
        checkout = self.root / "checkout"
        managed = checkout / "scripts/.quest-adb/platform-tools/adb.exe"
        managed.parent.mkdir(parents=True)
        managed.write_bytes(b"cached executable requiring integrity validation")
        with self.missing_windows_adb(), mock.patch.object(installer, "REPO", checkout), \
                mock.patch.object(installer, "managed_windows_adb", return_value=managed) as provision:
            self.assertEqual(installer.adb_path(None, managed), managed)
            provision.assert_called_once_with()

    def test_windows_dry_run_does_not_provision_or_contact_adb(self):
        with self.missing_windows_adb(), mock.patch.object(installer, "managed_windows_adb") as provision, \
                contextlib.redirect_stdout(io.StringIO()):
            code = installer.main(["--handoff", str(self.handoff), "--config", str(self.config),
                                   "--dry-run"], runner=self.fake)
            self.assertEqual(code, 0)
            provision.assert_not_called()
        self.assertFalse(self.config.exists())
        self.assertFalse(self.fake.calls)

    def test_invalid_apk_stops_before_automatic_adb_setup(self):
        self.make_apk(b"changed after handoff")
        with self.missing_windows_adb(), mock.patch.object(installer, "managed_windows_adb") as provision, \
                contextlib.redirect_stderr(io.StringIO()), contextlib.redirect_stdout(io.StringIO()):
            code = installer.main(["--handoff", str(self.handoff), "--config", str(self.config)], runner=self.fake)
            self.assertEqual(code, 1)
            provision.assert_not_called()
        self.assertFalse(self.config.exists())
        self.assertFalse(self.fake.calls)

    def test_automatic_adb_failure_leaves_device_and_settings_untouched(self):
        with self.missing_windows_adb(), \
                mock.patch.object(installer, "managed_windows_adb", side_effect=installer.InstallError("download failed")), \
                contextlib.redirect_stderr(io.StringIO()) as error, contextlib.redirect_stdout(io.StringIO()):
            code = installer.main(["--handoff", str(self.handoff), "--config", str(self.config)], runner=self.fake)
            self.assertEqual(code, 1)
            self.assertIn("download failed", error.getvalue())
        self.assertFalse(self.config.exists())
        self.assertFalse(self.fake.calls)

    def test_automatic_adb_continues_verified_scoped_install_and_remembers_path(self):
        with self.missing_windows_adb(), \
                mock.patch.object(installer, "managed_windows_adb", return_value=self.adb), \
                contextlib.redirect_stdout(io.StringIO()):
            code = installer.main(["--handoff", str(self.handoff), "--config", str(self.config)], runner=self.fake)
            self.assertEqual(code, 0)
        self.assertEqual(json.loads(self.config.read_text())["adb"], str(self.adb))
        self.assertEqual(self.fake.installed, self.apk.read_bytes())


if __name__ == "__main__":
    unittest.main()
