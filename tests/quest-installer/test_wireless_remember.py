"""Real ADB wrapper controls for post-install wireless endpoint retention."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools/quest-installer" / filename)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


installer = load("quest_remember_installer", "installer.py")
wireless = load("quest_remember_wireless", "wireless.py")
USB = "USB_QUEST"
HARDWARE = "QUEST_WIRELESS_123"
ADDRESS = "192.168.1.42:5555"


class Clock:
    def __init__(self): self.now = 100
    def monotonic(self): return self.now
    def sleep(self, seconds): self.now += seconds


class Protocol:
    """Stateful USB/TCP transport; restarting adbd drops the cable immediately."""
    def __init__(self):
        self.calls = []
        self.usb = True
        self.listening = None
        self.connected = set()
        self.hardware = HARDWARE
        self.wifi_hardware = HARDWARE
        self.route = "192.168.1.0/24 dev wlan0 proto kernel scope link src 192.168.1.42"
        self.refused = False
        self.unauthorized = False
        self.clock = None
        self.cost = 0
        self.cancel = None

    def __call__(self, command, **options):
        args = list(map(str, command[1:]))
        self.calls.append((args, dict(options)))
        if self.cancel is not None: raise self.cancel
        if self.clock is not None: self.clock.now += min(self.cost, options["timeout"])
        output, error, code = "", "", 0
        if args == ["devices", "-l"]:
            output = "List of devices attached\n"
            if self.usb: output += USB + " device model:Quest_3\n"
            output += "\n".join(endpoint + " device model:Quest_3" for endpoint in sorted(self.connected))
        elif args[:1] == ["connect"]:
            address = args[1]
            if not self.refused and self.listening and address.endswith(":" + str(self.listening)):
                self.connected.add(address)
                output = "connected to " + address
            else: output = "failed to connect to " + address
        elif args[:1] == ["-s"]:
            address, action = args[1], args[2:]
            if address == USB and not self.usb or address != USB and address not in self.connected:
                code, error = 1, "error: device '" + address + "' not found"
            elif address != USB and self.unauthorized:
                code, error = 1, "error: device unauthorized"
            elif action == ["shell", "ip", "route"]: output = self.route
            elif action[:2] == ["shell", "getprop"]:
                output = {"ro.product.model": "Quest 3", "ro.product.name": "eureka",
                          "ro.serialno": self.hardware if address == USB else self.wifi_hardware,
                          "service.adb.tcp.port": str(self.listening or -1)}[action[2]]
            elif action == ["tcpip", "5555"]:
                self.listening = 5555
                self.usb = False
                output = "restarting in TCP mode port: 5555"
            else: raise AssertionError("Unexpected ADB command: " + repr(args))
        else: raise AssertionError("Unexpected ADB command: " + repr(args))
        return subprocess.CompletedProcess(command, code, output, error)


class WirelessRememberTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.protocol = Protocol()
        self.adb = installer.Adb("adb", self.directory / "adb.log", self.protocol)
        self.settings = {"schema": 1, "adbSerial": USB, "deviceSerial": HARDWARE,
                         "transport": "usb", "source": {"kind": "keep-source"}}

    def remember(self, **options):
        return wireless.remember_after_usb(self.adb, USB, HARDWARE, self.settings, **options)

    def commands(self): return [args for args, _ in self.protocol.calls]

    def test_completed_usb_work_enables_and_verifies_wireless_without_switching_transport(self):
        settings, result = self.remember()
        self.assertEqual(result["status"], "ready")
        self.assertEqual(settings["endpoint"], ADDRESS)
        self.assertEqual(settings["adbSerial"], USB)
        self.assertEqual(settings["transport"], "usb")
        self.assertEqual(settings["source"], self.settings["source"])
        self.assertNotIn("endpoint", self.settings)
        self.assertEqual(self.adb.transport(USB), USB)
        self.assertFalse(self.protocol.usb)
        self.assertTrue(result["setupAttempted"])
        self.assertEqual(self.commands().count(["-s", USB, "tcpip", "5555"]), 1)
        after_restart = self.commands()[self.commands().index(["-s", USB, "tcpip", "5555"]) + 1:]
        self.assertTrue(all(args[:2] != ["-s", USB] for args in after_restart))
        self.assertEqual(self.commands()[-1], ["-s", ADDRESS, "shell", "getprop", "ro.serialno"])
        self.assertTrue(all(0 < options["timeout"] <= 2 for _, options in self.protocol.calls))
        self.assertIn("ohne USB", result["message"]["de"])
        self.assertIn("TCP mode", self.adb.log.read_text())

    def test_reuses_connected_endpoint_without_tcpip_or_connect(self):
        self.protocol.listening = 5555
        self.protocol.connected.add(ADDRESS)
        settings, result = self.remember()
        self.assertEqual(settings["endpoint"], ADDRESS)
        self.assertEqual(result["status"], "ready")
        self.assertFalse(result["setupAttempted"])
        self.assertFalse(any("tcpip" in args or args[:1] == ["connect"] for args in self.commands()))

    def test_reconnects_current_listener_without_restarting_adbd(self):
        self.protocol.listening = 4444
        settings, result = self.remember()
        self.assertEqual(result["status"], "ready")
        self.assertEqual(settings["endpoint"], "192.168.1.42:4444")
        self.assertFalse(any("tcpip" in args for args in self.commands()))

    def test_probe_only_remembers_authorized_endpoint_without_network_connect(self):
        self.protocol.connected.add(ADDRESS)
        settings, result = self.remember(enable=False)
        self.assertEqual(result["status"], "ready")
        self.assertEqual(settings["endpoint"], ADDRESS)
        self.assertFalse(any(args[:1] == ["connect"] or "tcpip" in args for args in self.commands()))

    def test_probe_only_keeps_previous_hint_when_wireless_not_connected(self):
        self.settings["endpoint"] = "192.168.1.40:5555"
        settings, result = self.remember(enable=False)
        self.assertEqual(settings, self.settings)
        self.assertEqual(result["status"], "unavailable")
        self.assertNotIn("endpoint", result)
        self.assertFalse(any(args[:1] == ["connect"] or "tcpip" in args for args in self.commands()))

    def test_no_wifi_never_enables_tcpip_or_discards_previous_hint(self):
        self.settings["endpoint"] = "192.168.1.40:5555"
        self.protocol.route = "10.0.0.0/8 dev tun0 src 10.0.0.5"
        settings, result = self.remember()
        self.assertEqual(settings, self.settings)
        self.assertEqual(result["reason"], "wifi-address-unavailable")
        self.assertFalse(any(args[:1] == ["connect"] or "tcpip" in args for args in self.commands()))

    def test_new_dhcp_address_replaces_hint_only_after_actual_hardware_proof(self):
        self.settings["endpoint"] = "192.168.1.2:5555"
        self.protocol.listening = 5555
        settings, result = self.remember()
        self.assertEqual(result["status"], "ready")
        self.assertEqual(settings["endpoint"], ADDRESS)
        self.assertFalse(any("192.168.1.2:5555" in args for args in self.commands()))

    def test_other_headset_at_ip_is_never_saved_or_reconfigured(self):
        self.settings["endpoint"] = "192.168.1.40:5555"
        self.protocol.listening = 5555
        self.protocol.wifi_hardware = "ANOTHER_QUEST"
        settings, result = self.remember()
        self.assertEqual(settings, self.settings)
        self.assertEqual(result["reason"], "identity-changed")
        self.assertFalse(any("tcpip" in args for args in self.commands()))

    def test_config_for_another_headset_is_not_touched(self):
        self.settings.update(deviceSerial="ANOTHER_QUEST", endpoint="192.168.1.40:5555")
        settings, result = self.remember()
        self.assertEqual(settings, self.settings)
        self.assertEqual(result["reason"], "different-headset")
        self.assertEqual(self.commands(), [])

    def test_unreachable_existing_listener_is_not_restarted(self):
        self.settings["endpoint"] = "192.168.1.40:5555"
        self.protocol.listening = 5555
        self.protocol.refused = True
        settings, result = self.remember()
        self.assertEqual(settings, self.settings)
        self.assertEqual(result["reason"], "listener-unreachable")
        self.assertFalse(any("tcpip" in args for args in self.commands()))

    def test_unauthorized_wifi_is_not_saved(self):
        self.protocol.listening = 5555
        self.protocol.unauthorized = True
        settings, result = self.remember()
        self.assertEqual(settings, self.settings)
        self.assertEqual(result["status"], "unavailable")
        self.assertNotIn("endpoint", result)

    def test_total_budget_is_bounded_and_never_enters_reconnect_wait(self):
        clock = Clock()
        self.protocol.clock, self.protocol.cost = clock, 2
        self.adb.wait_enabled = True
        with mock.patch.object(wireless.time, "monotonic", clock.monotonic), mock.patch.object(wireless.time, "sleep", clock.sleep), \
                mock.patch.object(self.adb, "wait_for_connection", side_effect=AssertionError("must not wait")):
            settings, result = self.remember(budget_seconds=1200)
        self.assertEqual(settings, self.settings)
        self.assertEqual(result["status"], "unavailable")
        self.assertLessEqual(clock.now, 112)
        self.assertLessEqual(result["elapsedSeconds"], 12)

    def test_cancellation_is_not_swallowed_as_optional_setup_failure(self):
        error = RuntimeError("Cancelled")
        error.code = "cancelled"
        self.protocol.cancel = error
        with self.assertRaisesRegex(RuntimeError, "Cancelled"):
            self.remember()

    def test_wifi_transport_never_changes_adbd(self):
        settings, result = wireless.remember_after_usb(self.adb, ADDRESS, HARDWARE, self.settings)
        self.assertEqual(result["status"], "not-usb")
        self.assertEqual(settings, self.settings)
        self.assertEqual(self.commands(), [])


if __name__ == "__main__": unittest.main()
