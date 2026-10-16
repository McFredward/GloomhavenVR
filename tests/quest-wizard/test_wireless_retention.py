"""An authorized USB connection remembers Wi-Fi for later cable-free captures."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import device_tools
import state
from test_device_tools import ProtocolRunner


class CableRunner(ProtocolRunner):
    def __init__(self):
        super().__init__()
        self.usb = True
        self.wifi = True

    def __call__(self, command, **options):
        args = list(map(str, command[1:]))
        if args[:1] == ["devices"]:
            self.commands.append(list(map(str, command)))
            output = "List of devices attached\n"
            if self.usb: output += "USB_TEST device model:Quest_3\n"
            if self.wifi: output += "192.168.1.5:5555 device model:Quest_3\n"
            return subprocess.CompletedProcess(command, 0,
                output if options.get("text", False) else output.encode(),
                "" if options.get("text", False) else b"")
        return super().__call__(command, **options)


class WirelessRetentionTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.store = state.Store(self.directory / "workspace")
        self.runner = CableRunner()
        self.installer, self.collector = device_tools._modules(ROOT)
        executable = self.directory / "adb"
        executable.write_bytes(b"test executable")
        for patch in (mock.patch.object(device_tools, "_modules", return_value=(self.installer, self.collector)),
                      mock.patch.object(self.installer, "adb_path", return_value=executable)):
            patch.start()
            self.addCleanup(patch.stop)
        self.tools = device_tools.DeviceTools(ROOT, self.store.root, self.runner)

    def test_usb_connect_then_capture_without_cable_reuses_verified_saved_endpoint(self):
        connected = self.tools.connect({})
        self.assertEqual(connected["status"], "connected")
        self.assertEqual(connected["transport"], "usb")
        self.assertEqual(connected["serial"], "USB_TEST")
        self.assertEqual(connected["endpoint"], "192.168.1.5:5555")
        self.runner.usb = False
        self.runner.commands.clear()
        # Recreate tools exactly as reopening the Builder does; only persisted
        # connection settings, not a live Python object, carry the Wi-Fi hint.
        tools = device_tools.DeviceTools(ROOT, self.store.root, self.runner)
        captured = tools.capture({})
        self.assertEqual(captured["status"], "complete")
        self.assertEqual(captured["transport"], "wifi")
        self.assertEqual(captured["serial"], "192.168.1.5:5555")
        with zipfile.ZipFile(tools.download(captured["capture"]["id"])) as archive:
            manifest = json.loads(archive.read("manifest.json"))
            self.assertEqual(manifest["device"]["hardwareSerial"], self.runner.hardware)
            self.assertEqual(manifest["device"]["transport"], "wifi")
        self.assertFalse(any("tcpip" in command or "USB_TEST" in command for command in self.runner.commands))

    def test_connect_without_wifi_stays_successfully_on_usb_and_explains_unverified_wireless(self):
        self.runner.wifi = False
        result = self.tools.connect({})
        self.assertEqual(result["status"], "connected")
        self.assertEqual(result["wireless"]["status"], "unavailable")
        self.assertEqual(result["serial"], "USB_TEST")
        self.assertNotIn("endpoint", json.loads(self.tools.config.read_text()))
        self.assertIn("konnte nicht bestätigt", result["message"]["de"])
        self.assertFalse(any("tcpip" in command or command[1:2] == ["connect"] for command in self.runner.commands))


if __name__ == "__main__": unittest.main()
