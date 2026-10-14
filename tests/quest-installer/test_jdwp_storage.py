"""Bounded debugger protocol and real event-suspended JVM storage invocation."""
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import time
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_jdwp_storage_test", ROOT / "tools/quest-installer/jdwp_storage.py")
bridge = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bridge)


class BufferSocket:
    def __init__(self, data=b""):
        self.data, self.sent, self.closed = data, [], False

    def recv(self, length):
        result, self.data = self.data[:length], self.data[length:]
        return result

    def sendall(self, value):
        self.sent.append(value)

    def settimeout(self, value):
        pass

    def close(self):
        self.closed = True


def reply(identity=2, payload=b"", error=0):
    return struct.pack(">IIBH", 11 + len(payload), identity, 0x80, error) + payload


class ProtocolTests(unittest.TestCase):
    def test_reader_rejects_truncated_or_extra_fields(self):
        for data in (b"", b"\0\0\0\x03ab"):
            with self.subTest(data=data), self.assertRaises(bridge.StorageError):
                bridge._Reader(data).string()
        with self.assertRaises(bridge.StorageError):
            bridge._Reader(b"unexpected").done()

    def test_reader_retains_full_string_and_rejects_invalid_utf8(self):
        value = bridge._Reader(bridge._string("a\0b"))
        self.assertEqual(value.string(), "a\0b")
        value.done()
        with self.assertRaises(bridge.StorageError):
            bridge._Reader(b"\0\0\0\x01\xff").string()

    def test_variable_widths_are_negotiated(self):
        widths = (1, 2, 3, 4, 8)
        protocol = bridge._Protocol(BufferSocket(reply(payload=struct.pack(">IIIII", *widths))))
        protocol.widths()
        self.assertEqual(protocol.id(0xabcdef), b"\xab\xcd\xef")
        self.assertEqual(protocol.id(0xffff, "method"), b"\xff\xff")
        with self.assertRaises(bridge.StorageError):
            protocol.id(1 << 24)

    def test_invalid_widths_are_rejected(self):
        for value in (0, 9, 0xffffffff):
            with self.subTest(value=value), self.assertRaises(bridge.StorageError):
                bridge._Protocol(BufferSocket(reply(payload=struct.pack(">IIIII", value, 8, 8, 8, 8)))).widths()

    def test_foreign_replies_and_protocol_errors_fail_closed(self):
        for data in (reply(identity=3), reply(error=13)):
            with self.subTest(data=data), self.assertRaises(bridge.StorageError):
                bridge._Protocol(BufferSocket(data)).command(1, 7)

    def test_oversized_and_invalid_flags_rejected_without_payload_read(self):
        for length, flags in ((10, 0x80), (bridge.MAX_PACKET + 1, 0x80), (11, 1)):
            with self.subTest(length=length, flags=flags), self.assertRaises(bridge.StorageError):
                data = struct.pack(">IIBH", length, 2, flags, 0)
                bridge._Protocol(BufferSocket(data)).command(1, 7)

    def test_ddms_wait_is_not_an_invocation_event(self):
        event = struct.pack(">IIBBB", 15, 91, 0, 199, 1) + b"WAIT"
        stream = BufferSocket(event + reply(payload=b"result"))
        protocol = bridge._Protocol(stream)
        self.assertEqual(protocol.command(1, 1), b"result")
        self.assertFalse(protocol.events)
        self.assertEqual(stream.sent[1], struct.pack(">IIBH", 11, 91, 0x80, 99))

    def test_unknown_value_tag_is_rejected(self):
        protocol = bridge._Protocol(BufferSocket())
        protocol.sizes = {"object": 8}
        with self.assertRaises(bridge.StorageError):
            protocol.value(bridge._Reader(b"?"))

    def test_step_event_requires_exact_request_main_thread_and_suspend_all(self):
        for policy, thread, identity in ((0, 4, 9), (1, 4, 9), (2, 5, 9), (2, 4, 10)):
            stream = BufferSocket(reply(payload=bridge._u32(9)) + reply(identity=3))
            protocol = bridge._Protocol(stream)
            protocol.sizes = {"object": 8, "type": 8, "method": 8}
            event = bytes([policy]) + bridge._u32(1) + b"\x01" + bridge._u32(identity)
            event += thread.to_bytes(8, "big") + b"\x01" + b"\0" * 24
            protocol.events.append(event)
            with self.subTest(policy=policy, thread=thread, identity=identity), self.assertRaises(bridge.StorageError):
                protocol.step_event(4)


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.home = tempfile.TemporaryDirectory()
        self.addCleanup(self.home.cleanup)
        self.adb = mock.Mock(log=Path(self.home.name) / "adb.log", executable="adb")
        self.adb.identity.return_value = "QUEST_SERIAL"
        self.adb.transport.side_effect = lambda address: address
        self.value = bridge.AppCommand.__new__(bridge.AppCommand)
        self.value.adb, self.value.address, self.value.hardware = self.adb, "USB", "QUEST_SERIAL"
        self.value.identity = ("123", "10061", "421")
        self.value.closed, self.value.event_owned = False, True
        self.value.stream, self.value.forward = BufferSocket(), "tcp:23456"
        self.value.protocol, self.value.thread = mock.Mock(), 7

    def snapshot(self, *, pid="123", uid="10061", start="421", state="T", package=bridge.PACKAGE):
        fields = [state] + ["0"] * 18 + [start]
        return pid + " (ghvr) " + " ".join(fields) + "\n" + package + "\n" + uid + "\n" + uid

    def test_process_snapshot_checks_pid_uid_package_start_and_all_task_pause(self):
        self.adb.run.return_value = self.snapshot()
        self.value._process_guard(stopped=True)
        script = self.adb.run.call_args.kwargs["input_text"]
        self.assertIn("/proc/123/task/*", script)
        self.assertIn("exit 53", script)
        self.assertNotIn("/sdcard", script)

    def test_foreign_or_recycled_identity_is_rejected(self):
        for change in ({"pid": "124"}, {"uid": "10062"}, {"start": "422"}, {"package": "other.app"}, {"state": "S"}):
            self.adb.run.return_value = self.snapshot(**change)
            with self.subTest(change=change), self.assertRaises(bridge.StorageError):
                self.value._process_guard(stopped=True)

    def test_foreign_hardware_prevents_any_process_access(self):
        self.adb.identity.return_value = "OTHER_QUEST"
        with self.assertRaises(bridge.StorageError):
            self.value._process_guard()
        self.adb.run.assert_not_called()

    def test_wait_boundary_refuses_gameplay_or_missing_debug_wait(self):
        safe = [("Ljava/lang/Thread;", "sleep"), ("Landroid/os/Debug;", "waitForDebugger"),
                ("Landroid/app/ActivityThread;", "handleBindApplication")]
        self.value.protocol.frames.return_value = safe
        self.value._wait_boundary()
        for frames in (safe[:-2], safe + [("Lcom/unity3d/player/UnityPlayer;", "start")]):
            self.value.protocol.frames.return_value = frames
            with self.subTest(frames=frames), self.assertRaises(bridge.StorageError):
                self.value._wait_boundary()

    def test_close_stops_only_matching_owned_process_before_socket_detach(self):
        self.adb.run.return_value = self.snapshot(state="S")
        stream = self.value.stream
        order = []
        stream.close = lambda: order.append("detach")
        original = self.adb.run.side_effect
        def run(*args, **kwargs):
            if "force-stop" in args or "-KILL" in args:
                order.append("kill")
            if "--remove" in args:
                order.append("forward")
            if "GHVRQ_APP_EXITED" in kwargs.get("input_text", ""):
                return "GHVRQ_APP_EXITED"
            return self.snapshot(state="S")
        self.adb.run.side_effect = run
        self.value.close()
        self.assertTrue(self.value.closed)
        self.assertLess(order.index("kill"), order.index("detach"))
        self.assertLess(order.index("detach"), order.index("forward"))
        calls = repr(self.adb.run.call_args_list)
        self.assertNotIn("--remove-all", calls)
        self.assertNotIn("clear-debug-app", calls)
        self.value.protocol.command.assert_not_called()  # No Dispose/Resume.

    def test_foreign_process_cleanup_does_not_stop_foreign_app(self):
        self.adb.run.return_value = self.snapshot(start="422")
        self.value.close()
        self.assertFalse(any("-KILL" in call.args or "force-stop" in call.args for call in self.adb.run.call_args_list))
        self.assertTrue(any("--remove" in call.args for call in self.adb.run.call_args_list))

    def test_unconfirmed_cleanup_retains_socket_without_resume_or_forward_removal(self):
        self.adb.identity.side_effect = bridge.StorageError("connection gone")
        stream = self.value.stream
        with self.assertRaises(bridge.StorageError):
            self.value.close()
        self.assertFalse(stream.closed)
        self.assertFalse(self.value.closed)
        self.assertIn(self.value, bridge._PENDING_CLOSE)
        self.adb.run.assert_not_called()
        bridge._PENDING_CLOSE.remove(self.value)

    def test_incomplete_identity_is_not_a_proof_that_original_vm_died(self):
        self.adb.run.return_value = ""
        stream = self.value.stream
        with self.assertRaises(bridge.StorageError):
            self.value.close()
        self.assertFalse(stream.closed)
        self.assertFalse(self.value.closed)
        self.assertFalse(any("force-stop" in call.args for call in self.adb.run.call_args_list))
        bridge._PENDING_CLOSE.remove(self.value)

    def test_live_owned_debugger_exit_fallback_terminates_original_vm(self):
        self.adb.identity.side_effect = bridge.StorageError("connection gone")
        self.value.socket_owned = True
        stream = self.value.stream
        self.value.close()
        self.value.protocol.command.assert_called_once_with(1, 10, bridge._u32(0), timeout=5)
        self.assertTrue(stream.closed)
        self.assertTrue(self.value.closed)

    def test_failed_debugger_exit_retains_original_suspension(self):
        self.adb.identity.side_effect = bridge.StorageError("connection gone")
        self.value.socket_owned = True
        self.value.protocol.command.side_effect = bridge.StorageError("socket gone")
        stream = self.value.stream
        with self.assertRaises(bridge.StorageError):
            self.value.close()
        self.assertFalse(stream.closed)
        self.assertFalse(self.value.closed)
        bridge._PENDING_CLOSE.remove(self.value)

    def test_script_bounds_rejected_before_any_device_work(self):
        for script in ("a\0b", "x" * (bridge.MAX_SCRIPT + 1), None):
            with self.subTest(script=type(script)), self.assertRaises(bridge.StorageError):
                self.value.shell(script)
        self.adb.identity.assert_not_called()

    def test_binary_reader_preserves_nul_and_line_endings(self):
        raw = b"\0first\r\nlast\0\xff"
        def run(command, **kwargs):
            kwargs["stdout"].write(raw)
            return types.SimpleNamespace(returncode=0)
        self.adb.runner.side_effect = run
        self.assertEqual(self.value._binary("cache/x/stdout", maximum=1024), raw)
        arguments = self.adb.runner.call_args.kwargs
        self.assertNotIn("text", arguments)
        self.assertFalse(arguments["shell"])

    def test_binary_reader_rejects_oversized_output(self):
        def run(command, **kwargs):
            kwargs["stdout"].write(b"x" * 101)
            return types.SimpleNamespace(returncode=0)
        self.adb.runner.side_effect = run
        with self.assertRaises(bridge.StorageError):
            self.value._binary("cache/x/stdout", maximum=100)

    def test_binary_connection_loss_keeps_actual_installer_action_exception(self):
        spec = importlib.util.spec_from_file_location("jdwp_actual_installer", ROOT / "tools/quest-installer/installer.py")
        installer = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = installer
        self.addCleanup(sys.modules.pop, spec.name, None)
        spec.loader.exec_module(installer)
        def disconnected(command, **kwargs):
            kwargs["stderr"].write(b"error: device offline")
            return types.SimpleNamespace(returncode=1)
        self.value.adb = installer.Adb("adb", Path(self.home.name) / "adb.log", runner=disconnected)
        with self.assertRaises(installer.ConnectionRequired) as caught:
            self.value._binary("cache/x/stdout", maximum=100)
        self.assertEqual(caught.exception.reason, "offline-or-unauthorized")
        def timeout(command, **kwargs):
            raise subprocess.TimeoutExpired(command, 30)
        self.value.adb.runner = timeout
        with self.assertRaises(installer.ConnectionRequired):
            self.value._binary("cache/x/stdout", maximum=100)

    def test_constructor_socket_error_becomes_bounded_storage_error(self):
        self.adb.run.return_value = "23456"
        with mock.patch.object(bridge.AppCommand, "_process_guard"), \
                mock.patch.object(bridge.AppCommand, "close"), \
                mock.patch.object(socket, "create_connection", side_effect=OSError("refused")):
            with self.assertRaisesRegex(bridge.StorageError, "socket could not be opened"):
                bridge.AppCommand(self.adb, "USB", "QUEST_SERIAL", ("123", "10061", "421"))


@unittest.skipUnless(os.name == "posix" and shutil.which("java") and shutil.which("javac"), "Real JDWP fixture needs Java on POSIX")
class RealJvmTests(unittest.TestCase):
    """Actual Java wire protocol/invocation; Android namespaces remain hardware-only."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="ghvr-jdwp-")
        self.addCleanup(self.temporary.cleanup)
        self.home = Path(self.temporary.name)
        debug = self.home / "android/os/Debug.java"
        activity = self.home / "android/app/ActivityThread.java"
        debug.parent.mkdir(parents=True)
        activity.parent.mkdir(parents=True)
        debug.write_text('''package android.os;
public class Debug {
  public static void waitForDebugger() throws Exception {
    new java.io.FileOutputStream(System.getProperty("fixture.wait")).close();
    while (true) { Thread.sleep(20); }
  }
}''')
        activity.write_text('''package android.app;
public class ActivityThread {
  public static void handleBindApplication() throws Exception { android.os.Debug.waitForDebugger(); }
  public static void main(String[] args) throws Exception {
    Runtime.getRuntime();
    handleBindApplication();
    new java.io.FileOutputStream(System.getProperty("fixture.game")).close();
  }
}''')
        subprocess.run(["javac", "-g", "-d", str(self.home), str(debug), str(activity)], check=True, capture_output=True, timeout=30)
        output = (self.home / "java.log").open("w+b")
        self.addCleanup(output.close)
        self.process = subprocess.Popen(["java", "-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=127.0.0.1:0",
                                         "-Dfixture.wait=" + str(self.home / "wait"), "-Dfixture.game=" + str(self.home / "game"),
                                         "-cp", str(self.home), "android.app.ActivityThread"], stdout=output, stderr=output)
        self.addCleanup(self.reap)
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            text = (self.home / "java.log").read_text(errors="replace")
            match = re.search(r"Listening for transport dt_socket at address: (\d+)", text)
            if match and (self.home / "wait").is_file():
                self.port = match.group(1)
                break
            if self.process.poll() is not None:
                self.fail("Fixture JVM exited: " + text)
            time.sleep(0.02)
        else:
            self.fail("Fixture JVM did not reach its debugger-wait stack")
        os.kill(self.process.pid, signal.SIGSTOP)
        self.start = Path("/proc/" + str(self.process.pid) + "/stat").read_text().rsplit(") ", 1)[1].split()[19]

    def reap(self):
        if self.process.poll() is None:
            self.process.kill()
        self.process.wait(timeout=10)
        # Standalone fixture follows the central completed-run retention API.
        spec = importlib.util.spec_from_file_location("jdwp_fixture_retention", ROOT / "scripts/town_run_retention.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        cleanup = module.cleanup_completed_run("quest-jdwp-storage", self.process.returncode, self.home / "java.log", ROOT, process_reaped=True)
        if os.environ.get("GHVR_JDWP_EVIDENCE_DIR"):
            evidence = Path(os.environ["GHVR_JDWP_EVIDENCE_DIR"]) / self._testMethodName
            evidence.mkdir(parents=True, exist_ok=True)
            for name in ("java.log", "adb.log", "android/os/Debug.java", "android/app/ActivityThread.java"):
                source = self.home / name
                if source.is_file():
                    shutil.copyfile(source, evidence / source.name)
            (evidence / "fixture-result.json").write_text(json.dumps({
                "processReaped": self.process.poll() is not None, "processExitCode": self.process.returncode,
                "gameplayMarkerExists": (self.home / "game").exists(), "cleanup": cleanup,
                "androidUidAndPathAdapted": True, "realJdwpProtocolAndRuntimeExec": True}, indent=2))

    def fixture(self):
        home, process, start, port = self.home, self.process, self.start, self.port
        absolute = "/data/user/0/" + bridge.PACKAGE
        class Adb:
            executable, log = "fixture-adb", home / "adb.log"
            def transport(self, address):
                return address
            def identity(self, address):
                return "FIXTURE"
            def run(self, *args, **kwargs):
                if "forward" in args:
                    return port if "--remove" not in args else ""
                if "-CONT" in args:
                    os.kill(process.pid, signal.SIGCONT)
                    return ""
                if "-KILL" in args or "force-stop" in args:
                    if process.poll() is None:
                        process.kill()
                    return ""
                if kwargs.get("input_text", "").strip() == "set -eu\nid -u":
                    return "10061"
                script = kwargs["input_text"].replace(absolute, str(home)).replace("/system/bin/sh", "/bin/sh")
                result = subprocess.run(["sh", "-s"], input=script.encode(), cwd=home, capture_output=True, timeout=kwargs.get("timeout", 20))
                if result.returncode:
                    raise bridge.StorageError(result.stderr.decode(errors="replace"))
                return result.stdout.decode().strip()
            def runner(self, command, **kwargs):
                return subprocess.run(["sh", "-c", command[-1]], cwd=home, **kwargs)
        class LocalAppCommand(bridge.AppCommand):
            def _process_guard(self, *, stopped=False):
                if process.poll() is not None:
                    raise bridge.StorageError("Fixture process stopped")
                values = Path("/proc/" + str(process.pid) + "/stat").read_text().rsplit(") ", 1)[1].split()
                if values[19] != start or (stopped and values[0] != "T"):
                    raise bridge.StorageError("Fixture actual PID/starttime changed")
        original_string = bridge._string
        def local_string(value):
            return original_string(value.replace(absolute, str(home)).replace("/system/bin/sh", "/bin/sh"))
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            app = LocalAppCommand(Adb(), "LOCAL", "FIXTURE", (str(process.pid), "10061", start))
        self.addCleanup(app.close, suppress=True)
        return app, local_string

    def test_actual_event_invocation_shell_nul_and_no_gameplay(self):
        app, local_string = self.fixture()
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            actual = app.shell("printf 'before\\000after\\n'", timeout=20)
            self.assertEqual(actual, "before\0after\n")
            app.guard()
            second = app.shell("printf 'second'", timeout=20)
            self.assertEqual(second, "second")
        self.assertFalse((self.home / "game").exists())
        self.assertFalse(list((self.home / "cache").glob(".ghvrq-jdwp-*")))
        self.assertIn('"mainThreadEventSuspended": true', (self.home / "adb.log").read_text())
        app.close()
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())

    def test_actual_shell_failure_kills_before_detach(self):
        app, local_string = self.fixture()
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            with self.assertRaisesRegex(bridge.StorageError, "exit 7"):
                app.shell("printf bad >&2\nexit 7", timeout=20)
        self.assertTrue(app.closed)
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())
        self.assertFalse(list((self.home / "cache").glob(".ghvrq-jdwp-*")))

    def test_actual_jdwp_exit_when_adb_cleanup_transport_is_unavailable(self):
        app, local_string = self.fixture()
        app._process_guard = mock.Mock(side_effect=bridge.StorageError("ADB disconnected"))
        app.close()
        self.process.wait(timeout=10)
        self.assertTrue(app.closed)
        self.assertFalse((self.home / "game").exists())
        self.assertIn('"appOriginStorageExitFallback": true', (self.home / "adb.log").read_text())
