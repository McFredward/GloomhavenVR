"""Optional Android debugging is confined to an explicit standalone launch."""
import subprocess
import threading
import unittest

import test_device_tools as fixture


class GameRunnerTests(unittest.TestCase):
    setUp = fixture.DeviceToolsTests.setUp

    def prepare(self, *, running=False, observed=True, package=True, resolver=None, pid_error=None):
        original = self.runner
        self.launched = False
        def run(command, **options):
            args = list(map(str, command[3:]))
            handled = True; code = 0; output = ""; error = ""
            if args[:3] == ["shell", "cmd", "package"]:
                output = resolver or "dev.gloomhavenvr.quest/com.unity3d.player.UnityPlayerActivity"
            elif args[:2] == ["shell", "pidof"]:
                if pid_error is not None: code = 1; error = pid_error
                elif running or self.launched and observed: output = "321"
                else: code = 1
            elif args[:3] == ["shell", "am", "start"]:
                self.launched = True
                output = "Starting: Intent { cmp=dev.gloomhavenvr.quest/com.unity3d.player.UnityPlayerActivity }"
            elif not package and args[:3] == ["shell", "pm", "path"]: code = 1
            else: handled = False
            if not handled: return original(command, **options)
            original.commands.append(list(map(str, command)))
            if not options.get("text", False): output, error = output.encode(), error.encode()
            return subprocess.CompletedProcess(command, code, output, error)
        self.tools.runner = run

    def options(self, debug=False):
        return {"host": "", "serial": "", "waitForDebugger": debug}

    def launches(self):
        return [row for row in self.runner.commands if "am" in row and "start" in row]

    def assert_no_storage_or_debug_mutations(self):
        self.assertFalse(any(set(row) & {"set-debug-app", "clear-debug-app", "force-stop", "forward", "run-as", "install", "uninstall", "sha256sum"}
                             for row in self.runner.commands))

    def test_normal_start_uses_usb_canonical_owned_intent_without_debugger_or_hash(self):
        self.prepare(); events = []
        result = self.tools.start(self.options(), events.append)
        self.assertEqual(result["status"], "started")
        self.assertEqual(result["serial"], "USB_TEST")
        self.assertEqual(result["launch"], {"waitForDebugger": False, "processId": 321, "requestAcknowledged": True})
        command = self.launches()[0]
        self.assertNotIn("-D", command)
        self.assertIn("android.intent.action.MAIN", command)
        self.assertIn("android.intent.category.LAUNCHER", command)
        self.assertIn("com.oculus.intent.category.VR", command)
        self.assertTrue(command[-1].startswith("dev.gloomhavenvr.quest/"))
        self.assert_no_storage_or_debug_mutations()
        self.assertFalse(any("tcpip" in row or "connect" in row for row in self.runner.commands))
        self.assertEqual(events[-1]["progress"]["percent"], 100)
        self.assertTrue(all(row["progress"]["percent"] < 100 for row in events[:-1]))
        self.assertEqual([row["progress"]["done"] for row in events], sorted(row["progress"]["done"] for row in events))

    def test_only_explicit_debug_start_adds_transient_flag_without_attaching(self):
        self.prepare()
        result = self.tools.start(self.options(True))
        self.assertEqual(result["status"], "debug-wait")
        self.assertTrue(result["launch"]["waitForDebugger"])
        self.assertEqual(self.launches()[0].count("-D"), 1)
        self.assertIn("nicht automatisch", result["message"]["de"])
        self.assertIn("Unity", result["message"]["en"])
        self.assert_no_storage_or_debug_mutations()
        self.assertNotIn("waitForDebugger", self.tools._config())

    def test_debug_request_never_restarts_running_game_and_guides_save_and_close(self):
        self.prepare(running=True)
        result = self.tools.start(self.options(True))
        self.assertEqual(result["status"], "waiting")
        self.assertEqual(result["error"]["code"], "device_debug_game_running")
        self.assertIn("Spielstand speichern", result["message"]["de"])
        self.assertEqual(self.launches(), [])
        self.assert_no_storage_or_debug_mutations()

    def test_normal_request_can_bring_running_game_forward(self):
        self.prepare(running=True)
        self.assertEqual(self.tools.start(self.options())["status"], "started")
        self.assertEqual(len(self.launches()), 1)
        self.assertNotIn("-D", self.launches()[0])

    def test_intent_acknowledgment_without_pid_guides_user_instead_of_claiming_game_started(self):
        self.prepare(observed=False)
        result = self.tools.start(self.options())
        self.assertEqual(result["status"], "waiting")
        self.assertIsNone(result["launch"]["processId"])
        self.assertTrue(result["launch"]["requestAcknowledged"])
        self.assertIn("kein Spielprozess", result["message"]["de"])
        self.assert_no_storage_or_debug_mutations()

    def test_absent_package_or_foreign_launcher_never_launches(self):
        self.prepare(package=False)
        result = self.tools.start(self.options())
        self.assertEqual(result["error"]["code"], "device_game_missing")
        self.assertEqual(self.launches(), [])
        self.prepare(resolver="another.package/.Launcher")
        result = self.tools.start(self.options())
        self.assertEqual(result["error"]["code"], "device_game_launcher")
        self.assertEqual(self.launches(), [])

    def test_pid_probe_permission_error_is_not_absent_process(self):
        self.prepare(pid_error="pidof: Permission denied")
        result = self.tools.start(self.options(True))
        self.assertEqual(result["status"], "waiting")
        self.assertIn("Permission denied", result["message"]["en"])
        self.assertEqual(self.launches(), [])

    def test_invalid_debug_options_are_rejected_before_adb(self):
        for invalid in ({"host": "", "serial": ""}, {**self.options(), "waitForDebugger": 1},
                        {**self.options(), "waitForDebugger": "true"}, {**self.options(), "command": "anything"}):
            with self.assertRaises(fixture.state.WizardError): self.tools.start(invalid)
        self.assertEqual(self.runner.commands, [])

    def test_cancelled_start_does_not_start_or_stop_an_app(self):
        cancelled = threading.Event(); cancelled.set()
        result = self.tools.start(self.options(True), cancel=cancelled)
        self.assertEqual(result["status"], "idle")
        self.assertEqual(self.runner.commands, [])


if __name__ == "__main__": unittest.main()
