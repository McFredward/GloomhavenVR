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

    def test_waiting_argument_uses_visible_named_boolean_in_exact_frame(self):
        table = bridge._u32(3) + bridge._u32(1) + (0).to_bytes(8, "big")
        table += bridge._string("waiting") + bridge._string("Z") + bridge._u32(100) + bridge._u32(2)
        for waiting in (0, 1):
            stream = BufferSocket(reply(payload=table) + reply(identity=3, payload=bridge._u32(1) + b"Z" + bytes([waiting])))
            protocol = bridge._Protocol(stream)
            protocol.sizes = {"object": 8, "type": 8, "method": 8, "frame": 8}
            frame = {"type": 12, "method": 13, "frame": 14, "index": 20}
            self.assertEqual(protocol.waiting_argument(7, frame), waiting == 1)
            self.assertEqual(stream.sent[1][9:11], bytes([16, 1]))
            self.assertEqual(stream.sent[1][-5:], bridge._u32(2) + b"Z")

    def test_owned_step_is_one_min_into_event_with_suspend_all(self):
        stream = BufferSocket(reply(payload=bridge._u32(9)) + reply(identity=3))
        protocol = bridge._Protocol(stream)
        protocol.sizes = {"object": 8, "type": 8, "method": 8}
        event = b"\x02" + bridge._u32(1) + b"\x01" + bridge._u32(9)
        event += (4).to_bytes(8, "big") + b"\x01" + b"\0" * 24
        protocol.events.append(event)
        self.assertEqual(protocol.step_event(4), 9)
        expected = b"\x01\x02" + bridge._u32(2) + b"\x0a" + (4).to_bytes(8, "big")
        expected += bridge._u32(0) + bridge._u32(0) + b"\x01" + bridge._u32(1)
        self.assertEqual(stream.sent[0][9:11], b"\x0f\x01")
        self.assertEqual(stream.sent[0][11:], expected)
        self.assertEqual(stream.sent[1][9:11], b"\x01\x09")

    def test_waiting_argument_refuses_missing_out_of_scope_or_duplicate_slots(self):
        variable = (0).to_bytes(8, "big") + bridge._string("waiting") + bridge._string("Z") + bridge._u32(10) + bridge._u32(2)
        for arguments, count, index in ((2, 1, 0), (3, 0, 0), (3, 1, 10), (3, 2, 0), (3, 1025, 0)):
            protocol = bridge._Protocol(BufferSocket(reply(payload=bridge._u32(arguments) + bridge._u32(count) + variable * min(count, 2))))
            protocol.sizes = {"object": 8, "type": 8, "method": 8, "frame": 8}
            with self.subTest(arguments=arguments, count=count, index=index), self.assertRaises(bridge.StorageError):
                protocol.waiting_argument(7, {"type": 12, "method": 13, "frame": 14, "index": index})

    def test_boot_type_refuses_non_boot_loader_and_caches_exact_type(self):
        for loader in (0, 25):
            stream = BufferSocket(reply(payload=loader.to_bytes(8, "big")))
            protocol = bridge._Protocol(stream)
            protocol.sizes = {"object": 8, "type": 8}
            self.assertEqual(protocol.boot_type(12), loader == 0)
            self.assertEqual(protocol.boot_type(12), loader == 0)
            self.assertEqual(len(stream.sent), 1)


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
        safe = self.detailed_frames(("Ljava/lang/Thread;", "sleep"), ("Landroid/os/Debug;", "waitForDebugger"),
                                    ("Landroid/app/ActivityThread;", "handleBindApplication"))
        self.value.protocol.frames.return_value = safe
        self.value._wait_boundary()
        for frames in (safe[:-2], safe + self.detailed_frames(("Lcom/unity3d/player/UnityPlayer;", "start"))):
            self.value.protocol.frames.return_value = frames
            with self.subTest(frames=frames), self.assertRaises(bridge.StorageError):
                self.value._wait_boundary()

    def test_android_zygote_and_runtime_boot_frames_remain_allowed(self):
        self.value.protocol.frames.return_value = self.detailed_frames(
            ("Landroid/os/Debug;", "waitForDebugger"),
            ("Landroid/app/ActivityThread;", "handleBindApplication"),
            ("Landroid/os/Looper;", "loop"),
            ("Landroid/app/ActivityThread;", "main"),
            ("Lcom/android/internal/os/RuntimeInit$MethodAndArgsCaller;", "run"),
            ("Lcom/android/internal/os/ZygoteInit;", "main"))
        self.value._wait_boundary()

    @staticmethod
    def detailed_frames(*pairs):
        wrapper = ("Landroid/app/ActivityThread;", "waitForDebugger")
        descriptors = {("Landroid/app/IActivityManager$Stub$Proxy;", "showWaitingForDebugger"): "(Landroid/app/IApplicationThread;Z)V",
                       wrapper: "(Landroid/app/ActivityThread$AppBindData;)V"}
        return [{"signature": signature, "name": name,
                 "descriptor": descriptors.get((signature, name), "()V"),
                 "flags": 2 if (signature, name) == wrapper else 1,
                 "type": index + 10, "method": index + 20, "frame": index + 30, "index": 4}
                for index, (signature, name) in enumerate(pairs)]

    def test_wait_wrapper_accepts_only_exact_private_boot_owner_and_no_intermediate_helper(self):
        safe = self.detailed_frames(("Ljava/lang/Thread;", "sleep"), ("Landroid/os/Debug;", "waitForDebugger"),
                                    ("Landroid/app/ActivityThread;", "waitForDebugger"),
                                    ("Landroid/app/ActivityThread;", "handleBindApplication"),
                                    ("Landroid/app/ActivityThread;", "main"))
        self.value.protocol.frames.return_value = safe
        self.value.protocol.boot_type.return_value = True
        self.value._wait_boundary()
        cases = [safe[:2] + self.detailed_frames(("Landroid/os/Trace;", "traceBegin")) + safe[2:],
                 safe[:3] + [dict(safe[2])] + safe[3:],
                 safe[:2] + [dict(safe[2], descriptor="()V")] + safe[3:],
                 safe[:2] + [dict(safe[2], flags=8 | 2)] + safe[3:],
                 safe[:2] + [dict(safe[2], flags=256 | 2)] + safe[3:],
                 safe[:2] + [dict(safe[2], flags=1)] + safe[3:],
                 safe + self.detailed_frames(("Landroid/app/Application;", "onCreate"))]
        for frames in cases:
            self.value.protocol.frames.return_value = frames
            with self.subTest(frames=frames), self.assertRaises(bridge.StorageError):
                self.value._wait_boundary()
        self.value.protocol.frames.return_value = safe
        self.value.protocol.boot_type.return_value = False
        with self.assertRaisesRegex(bridge.StorageError, "boot"):
            self.value._wait_boundary()
        self.value.protocol.invoke.assert_not_called()

    def test_wait_wrapper_pre_wait_witness_keeps_wrapper_call_site_and_true_argument(self):
        frames = self.pre_wait_frames()
        wrapper = self.detailed_frames(("Landroid/app/ActivityThread;", "waitForDebugger"))[0]
        wrapper.update(type=100, method=200, frame=300)
        frames.insert(3, wrapper)
        self.value.protocol.boot_type.return_value = True
        self.value.protocol.waiting_argument.return_value = True
        witness = self.value._pre_wait_boundary(frames)
        self.assertEqual(self.value._pre_wait_boundary(frames[3:], witness), witness)
        self.assertEqual(witness[0][0], (100, 200))
        for changed in (dict(wrapper, method=201), dict(wrapper, index=3)):
            with self.subTest(changed=changed), self.assertRaisesRegex(bridge.StorageError, "call site"):
                self.value._pre_wait_boundary([changed] + frames[4:], witness)
        self.value.protocol.waiting_argument.return_value = False
        with self.assertRaisesRegex(bridge.StorageError, "waiting=true"):
            self.value._pre_wait_boundary(frames, witness)

    def test_wait_wrapper_requires_actual_owned_step_and_still_waiting_stack(self):
        frames = self.detailed_frames(("Landroid/os/Debug;", "waitForDebugger"),
                                      ("Landroid/app/ActivityThread;", "waitForDebugger"),
                                      ("Landroid/app/ActivityThread;", "handleBindApplication"))
        self.value.protocol.boot_type.return_value = True
        self.value.event_owned = False
        self.value.protocol.frames.return_value = frames
        self.value.protocol.step_event.side_effect = bridge.StorageError("foreign step boundary")
        with self.assertRaisesRegex(bridge.StorageError, "foreign step"):
            self.value._establish_wait_event()
        self.assertFalse(self.value.event_owned)
        self.value.protocol.invoke.assert_not_called()
        self.value.protocol.step_event.side_effect = None
        self.value.protocol.frames.side_effect = [frames, frames[1:]]
        with self.assertRaisesRegex(bridge.StorageError, "pre-Application"):
            self.value._establish_wait_event()
        self.value.protocol.invoke.assert_not_called()

    def pre_wait_frames(self):
        return self.detailed_frames(("Landroid/os/BinderProxy;", "transactNative"),
                                    ("Landroid/os/BinderProxy;", "transact"),
                                    ("Landroid/app/IActivityManager$Stub$Proxy;", "showWaitingForDebugger"),
                                    ("Landroid/app/ActivityThread;", "handleBindApplication"),
                                    ("Landroid/app/ActivityThread;", "main"))

    def test_pre_wait_requires_true_argument_boot_ownership_and_exact_proxy_ancestry(self):
        self.value.protocol.boot_type.return_value = True
        self.value.protocol.waiting_argument.return_value = True
        frames = self.pre_wait_frames()
        witness = self.value._pre_wait_boundary(frames)
        self.assertEqual(self.value._pre_wait_boundary(frames, witness), witness)
        cases = [self.detailed_frames(("Landroid/app/Application;", "onCreate"),
                                     ("Landroid/app/IActivityManager$Stub$Proxy;", "showWaitingForDebugger"),
                                     ("Landroid/app/ActivityThread;", "handleBindApplication")),
                 frames[:-1] + self.detailed_frames(("Landroid/app/LoadedApk;", "makeApplication")),
                 self.detailed_frames(("Landroid/os/Trace;", "traceBegin"),
                                      ("Landroid/app/ActivityThread;", "handleBindApplication")),
                 self.detailed_frames(("Landroid/os/BinderProxy;", "transactNative"),
                                      ("Landroid/app/ActivityThread;", "handleBindApplication"))]
        for value in cases:
            with self.subTest(frames=value), self.assertRaises(bridge.StorageError):
                self.value._pre_wait_boundary(value)
        self.value.protocol.waiting_argument.return_value = False
        with self.assertRaisesRegex(bridge.StorageError, "waiting=true"):
            self.value._pre_wait_boundary(frames)
        self.value.protocol.waiting_argument.return_value = True
        self.value.protocol.boot_type.return_value = False
        with self.assertRaisesRegex(bridge.StorageError, "boot"):
            self.value._pre_wait_boundary(frames)

    def test_post_wait_application_and_loaded_apk_never_reach_invocation(self):
        safe = self.detailed_frames(("Landroid/os/Debug;", "waitForDebugger"), ("Landroid/app/ActivityThread;", "handleBindApplication"))
        for pair in (("Landroid/app/Application;", "onCreate"), ("Landroid/app/LoadedApk;", "makeApplication")):
            self.value.protocol.frames.return_value = safe + self.detailed_frames(pair)
            with self.subTest(pair=pair), self.assertRaises(bridge.StorageError):
                self.value._wait_boundary()
        self.value.protocol.invoke.assert_not_called()

    def test_pre_wait_owned_step_budget_never_invokes_from_waiting_display(self):
        self.value.protocol.frames.return_value = self.pre_wait_frames()
        self.value.protocol.boot_type.return_value = True
        self.value.protocol.waiting_argument.return_value = True
        self.value._process_guard = mock.Mock()
        with mock.patch.object(bridge, "MAX_PRE_WAIT_STEPS", 2), self.assertRaisesRegex(bridge.StorageError, "bounded"):
            self.value._establish_wait_event()
        self.assertEqual(self.value.protocol.step_event.call_count, 2)
        self.value.protocol.invoke.assert_not_called()
        self.assertIn('"ownedSingleSteps": 2', self.adb.log.read_text())

    def test_pre_wait_foreign_process_stops_before_next_resume_or_invocation(self):
        self.value.protocol.frames.return_value = self.pre_wait_frames()
        self.value.protocol.boot_type.return_value = True
        self.value.protocol.waiting_argument.return_value = True
        self.value._process_guard = mock.Mock(side_effect=bridge._ProcessChanged("foreign PID"))
        with self.assertRaises(bridge._ProcessChanged):
            self.value._establish_wait_event()
        self.value.protocol.step_event.assert_not_called()
        self.value.protocol.invoke.assert_not_called()

    def test_pre_wait_missing_argument_metadata_keeps_refusal_and_diagnostics(self):
        self.value.protocol.frames.return_value = self.pre_wait_frames()
        self.value.protocol.boot_type.return_value = True
        self.value.protocol.waiting_argument.side_effect = bridge.StorageError("argument metadata unavailable")
        with self.assertRaisesRegex(bridge.StorageError, "metadata unavailable"):
            self.value._establish_wait_event()
        self.value.protocol.step_event.assert_not_called()
        self.value.protocol.invoke.assert_not_called()
        log = self.adb.log.read_text()
        self.assertIn('"appOriginStorageWaitEntryRefused"', log)
        self.assertIn('"ownedSingleSteps": 0', log)

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
        for script in ("a\0b", "x" * (bridge.MAX_SCRIPT + 1), "💡" * (bridge.MAX_SCRIPT // 4 + 1), None, "\ud800"):
            with self.subTest(script=type(script)), self.assertRaises(bridge.StorageError):
                self.value.shell(script)
        self.adb.identity.assert_not_called()

    @unittest.skipUnless(os.name == "posix", "Literal shell transport fixture needs POSIX")
    def test_job_upload_preserves_literal_bytes_without_implicit_temporary_file(self):
        self.value.guard = mock.Mock()
        self.value.close = mock.Mock()
        self.value.scratches = set()
        payload = "# quotes ' \" ` $(touch INJECTED) \\ %s %b\r\n# Grüße 日本語 💡\n"
        payload += "# " + "'" * (bridge.MAX_SCRIPT // 4)
        token = "a" * 32
        relative = "cache/.ghvrq-jdwp-" + token
        absolute = "/data/user/0/" + bridge.PACKAGE + "/" + relative

        class UploadObserved(Exception):
            pass

        def upload_then_stop(*args, **kwargs):
            self.assertEqual(args, ("-s", "USB", "shell", "-T", "run-as", bridge.PACKAGE, "sh", "-s"))
            self.assertNotIn("<<", kwargs["input_text"])
            # The old heredoc upload needed mksh's default /data/local. This
            # actual stdin path is independent of an unusable inherited TMPDIR.
            result = subprocess.run(["sh", "-s"], input=kwargs["input_text"].encode("utf-8"),
                                    cwd=self.home.name, capture_output=True, timeout=10,
                                    env=dict(os.environ, TMPDIR=str(Path(self.home.name) / "missing")))
            self.assertEqual(result.returncode, 0, result.stderr)
            uploaded = (Path(self.home.name) / relative / "job.sh").read_bytes()
            expected = ("#!/system/bin/sh\numask 077\nTMPDIR=" + absolute + "\nexport TMPDIR\n(\nset -eu\n" + payload
                        + "\n) > " + absolute + "/stdout 2> " + absolute + "/stderr\nstatus=$?\nprintf '%s' \"$status\" > "
                        + absolute + "/status.part\nmv " + absolute + "/status.part " + absolute + "/status\n")
            self.assertEqual(uploaded, expected.encode("utf-8"))
            self.assertFalse((Path(self.home.name) / "INJECTED").exists())
            self.assertGreater(len(uploaded), 32768)
            raise UploadObserved()

        self.adb.run.side_effect = upload_then_stop
        with mock.patch.object(bridge.uuid, "uuid4", return_value=types.SimpleNamespace(hex=token)):
            with self.assertRaises(UploadObserved):
                self.value.shell(payload)
        self.value.protocol.command.assert_not_called()
        self.value.close.assert_called_once_with(suppress=True)

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
        self.phase = {"test_actual_pre_wait_proxy_reaches_strict_wait_before_storage": "pre",
                      "test_actual_post_wait_false_proxy_is_rejected_without_storage": "post",
                      "test_actual_application_proxy_is_rejected_without_storage": "application",
                      "test_actual_private_wrapper_wait_keeps_owned_event_and_no_gameplay": "wrapper-wait",
                      "test_actual_private_wrapper_true_proxy_reaches_wait_before_storage": "wrapper-pre",
                      "test_actual_private_wrapper_false_proxy_never_resumes_or_invokes": "wrapper-post",
                      "test_actual_foreign_pid_cannot_attach_to_pre_wait_vm": "pre"}.get(self._testMethodName, "wait")
        debug = self.home / "android/os/Debug.java"
        activity = self.home / "android/app/ActivityThread.java"
        debug.parent.mkdir(parents=True)
        activity.parent.mkdir(parents=True)
        debug.write_text('''package android.os;
public class Debug {
  public static void waitForDebugger() throws Exception {
    new java.io.FileOutputStream(System.getProperty("fixture.wait")).close();
    if (System.getProperty("fixture.phase").endsWith("post")) return;
    while (true) { Thread.sleep(20); }
  }
}''')
        activity.write_text('''package android.app;
public class ActivityThread {
  static class AppBindData {}
  private void waitForDebugger(AppBindData data) throws Exception {
    String phase=System.getProperty("fixture.phase");
    android.app.IActivityManager.Stub.Proxy manager=new android.app.IActivityManager.Stub.Proxy();
    if ("wrapper-pre".equals(phase)) manager.showWaitingForDebugger(null,true);
    android.os.Debug.waitForDebugger();
    manager.showWaitingForDebugger(null,false);
  }
  public static void handleBindApplication() throws Exception {
    String phase=System.getProperty("fixture.phase");
    android.app.IActivityManager.Stub.Proxy manager=new android.app.IActivityManager.Stub.Proxy();
    if (phase.startsWith("wrapper-")) {
      new ActivityThread().waitForDebugger(new AppBindData());
    } else if ("pre".equals(phase)) {
      manager.showWaitingForDebugger(null,true);
      android.os.Debug.waitForDebugger();
      manager.showWaitingForDebugger(null,false);
    } else if ("post".equals(phase)) {
      android.os.Debug.waitForDebugger();
      manager.showWaitingForDebugger(null,false);
    } else if ("application".equals(phase)) {
      new android.app.Application().onCreate();
    } else {
      android.os.Debug.waitForDebugger();
    }
  }
  public static void main(String[] args) throws Exception {
    Runtime.getRuntime();
    handleBindApplication();
    new java.io.FileOutputStream(System.getProperty("fixture.game")).close();
  }
}''')
        sources = {"android/app/IApplicationThread.java": "package android.app; public interface IApplicationThread {}",
                   "android/app/IActivityManager.java": '''package android.app;
public interface IActivityManager {
  public static class Stub {
    public static class Proxy {
      public void showWaitingForDebugger(IApplicationThread who,boolean waiting) {
        android.os.Parcel data=new android.os.Parcel(),reply=new android.os.Parcel();
        try {
          new android.os.BinderProxy().transact(1,data,reply,0);
          reply.readException();
        } finally { reply.recycle();data.recycle(); }
      }
    }
  }
}''', "android/os/Parcel.java": '''package android.os;
public class Parcel {
  public void readException() {}
  public void recycle() {}
}''', "android/os/BinderProxy.java": '''package android.os;
public class BinderProxy {
  static { System.load(System.getProperty("fixture.library")); }
  public boolean transact(int code,Parcel data,Parcel reply,int flags) {
    return transactNative(code,data,reply,flags);
  }
  private native boolean transactNative(int code,Parcel data,Parcel reply,int flags);
}''', "android/app/Application.java": '''package android.app;
public class Application {
  public void onCreate() { new IActivityManager.Stub.Proxy().showWaitingForDebugger(null,true); }
}'''}
        paths = [debug, activity]
        for name, text in sources.items():
            path = self.home / name
            path.write_text(text)
            paths.append(path)
        subprocess.run(["javac", "-g", "-d", str(self.home), *map(str, paths)], check=True, capture_output=True, timeout=30)
        if not self.phase.endswith("wait"):
            if not shutil.which("cc"):
                self.skipTest("Delayed real Binder control requires a native C compiler")
            # An actual JNI call blocks outside Java, like BinderProxy's native
            # transact. Thread.sleep would introduce an artificial Java frame
            # and could not test the production's deliberately narrow stack.
            native = self.home / "fixture-binder.c"
            native.write_text('''#include <jni.h>
#include <fcntl.h>
#include <stdlib.h>
#include <time.h>
#include <unistd.h>
JNIEXPORT jboolean JNICALL Java_android_os_BinderProxy_transactNative(
  JNIEnv *env,jobject self,jint code,jobject data,jobject reply,jint flags) {
  int file=open(getenv("GHVR_JDWP_PROXY_MARKER"),O_WRONLY|O_CREAT,0600);
  if(file>=0) close(file);
  struct timespec delay={0,800000000};
  nanosleep(&delay,NULL);
  return JNI_TRUE;
}
''')
            jdk = Path(shutil.which("javac")).resolve().parents[1]
            subprocess.run(["cc", "-shared", "-fPIC", "-I" + str(jdk / "include"), "-I" + str(jdk / "include/linux"),
                            str(native), "-o", str(self.home / "libfixturebinder.so")], check=True, capture_output=True, timeout=30)
        output = (self.home / "java.log").open("w+b")
        self.addCleanup(output.close)
        self.process = subprocess.Popen(["java", "-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address=127.0.0.1:0",
                                         "-Dfixture.wait=" + str(self.home / "wait"), "-Dfixture.game=" + str(self.home / "game"),
                                         "-Dfixture.phase=" + self.phase, "-Dfixture.library=" + str(self.home / "libfixturebinder.so"),
                                         "-Xbootclasspath/a:" + str(self.home), "-cp", str(self.home), "android.app.ActivityThread"],
                                        stdout=output, stderr=output,
                                        env=dict(os.environ, GHVR_JDWP_PROXY_MARKER=str(self.home / "proxy")))
        self.addCleanup(self.reap)
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            text = (self.home / "java.log").read_text(errors="replace")
            match = re.search(r"Listening for transport dt_socket at address: (\d+)", text)
            if match and (self.home / ("wait" if self.phase.endswith("wait") else "proxy")).is_file():
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
            for name in ("java.log", "adb.log", "android/os/Debug.java", "android/app/ActivityThread.java",
                         "android/app/IActivityManager.java", "android/os/BinderProxy.java", "fixture-binder.c"):
                source = self.home / name
                if source.is_file():
                    shutil.copyfile(source, evidence / source.name)
            (evidence / "fixture-result.json").write_text(json.dumps({
                "processReaped": self.process.poll() is not None, "processExitCode": self.process.returncode,
                "gameplayMarkerExists": (self.home / "game").exists(), "cleanup": cleanup,
                "fixturePhase": self.phase, "debugWaitMarkerExists": (self.home / "wait").exists(),
                "androidUidAndPathAdapted": True, "realJdwpProtocolAndRuntimeExec": True,
                "nativeBinderDelayAdapted": not self.phase.endswith("wait")}, indent=2))

    def fixture(self, *, identity=None):
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
                if self.identity[:1] != (str(process.pid),) or self.identity[2] != start:
                    raise bridge._ProcessChanged("Fixture actual PID/starttime changed")
                if process.poll() is not None:
                    raise bridge.StorageError("Fixture process stopped")
                values = Path("/proc/" + str(process.pid) + "/stat").read_text().rsplit(") ", 1)[1].split()
                if values[19] != start or (stopped and values[0] != "T"):
                    raise bridge.StorageError("Fixture actual PID/starttime changed")
        original_string = bridge._string
        def local_string(value):
            return original_string(value.replace(absolute, str(home)).replace("/system/bin/sh", "/bin/sh"))
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            app = LocalAppCommand(Adb(), "LOCAL", "FIXTURE", identity or (str(process.pid), "10061", start))
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

    def test_actual_shell_quoted_unicode_and_temporary_context(self):
        app, local_string = self.fixture()
        prefix = "Grüße 日本語 💡 ' \" ` $(touch INJECTED) \\ %s %b"
        expected = prefix + "\r\nend\0"
        # The command itself stays valid shell text; CRLF and NUL in stdout
        # must survive the binary result path exactly.
        script = "printf '%s\\r\\nend\\000' " + bridge.shlex.quote(prefix)
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            self.assertEqual(app.shell(script, timeout=20), expected)
            temporary = app.shell("test -d \"$TMPDIR\"\nprintf '%s' \"$TMPDIR\"", timeout=20)
            self.assertRegex(temporary, re.escape(str(self.home / "cache")) + r"/\.ghvrq-jdwp-[0-9a-f]{32}$")
        self.assertFalse((self.home / "INJECTED").exists())
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

    def test_actual_pre_wait_proxy_reaches_strict_wait_before_storage(self):
        self.assertFalse((self.home / "wait").exists())
        app, local_string = self.fixture()
        app.guard()
        self.assertIn('"appOriginStorageWaitEntry": true', (self.home / "adb.log").read_text())
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            self.assertEqual(app.shell("printf 'pre-wait storage'", timeout=20), "pre-wait storage")
        self.assertFalse((self.home / "game").exists())
        app.close()
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())

    def test_actual_private_wrapper_wait_keeps_owned_event_and_no_gameplay(self):
        app, local_string = self.fixture()
        frames = app.protocol.frames(app.thread)
        self.assertIn(("Landroid/app/ActivityThread;", "waitForDebugger"), frames)
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            self.assertEqual(app.shell("printf 'wrapper storage'", timeout=20), "wrapper storage")
        app.guard()
        app.close()
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())

    def test_actual_private_wrapper_true_proxy_reaches_wait_before_storage(self):
        self.assertFalse((self.home / "wait").exists())
        app, local_string = self.fixture()
        log = (self.home / "adb.log").read_text()
        self.assertIn('"waitingProxyArgumentProved": true', log)
        self.assertIn('"appOriginStorageWaitEntry": true', log)
        with mock.patch.object(bridge, "_string", side_effect=local_string):
            self.assertEqual(app.shell("printf 'true wrapper storage'", timeout=20), "true wrapper storage")
        app.close()
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())

    def test_actual_private_wrapper_false_proxy_never_resumes_or_invokes(self):
        self.assertTrue((self.home / "wait").exists())
        with mock.patch.object(bridge._Protocol, "step_event", side_effect=AssertionError("false proxy resumed")) as step, \
                mock.patch.object(bridge._Protocol, "invoke", side_effect=AssertionError("false proxy invocation")) as invocation:
            with self.assertRaisesRegex(bridge.StorageError, "waiting=true"):
                self.fixture()
        step.assert_not_called()
        invocation.assert_not_called()
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())

    def test_actual_post_wait_false_proxy_is_rejected_without_storage(self):
        self.assertTrue((self.home / "wait").exists())
        with mock.patch.object(bridge._Protocol, "invoke", side_effect=AssertionError("post-wait invocation")) as invocation:
            with self.assertRaisesRegex(bridge.StorageError, "waiting=true"):
                self.fixture()
        invocation.assert_not_called()
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())

    def test_actual_application_proxy_is_rejected_without_storage(self):
        self.assertFalse((self.home / "wait").exists())
        with mock.patch.object(bridge._Protocol, "invoke", side_effect=AssertionError("Application invocation")) as invocation:
            with self.assertRaisesRegex(bridge.StorageError, "directly by handleBindApplication"):
                self.fixture()
        invocation.assert_not_called()
        self.process.wait(timeout=10)
        self.assertFalse((self.home / "game").exists())

    def test_actual_foreign_pid_cannot_attach_to_pre_wait_vm(self):
        with mock.patch.object(bridge._Protocol, "invoke", side_effect=AssertionError("foreign invocation")) as invocation:
            with self.assertRaises(bridge._ProcessChanged):
                self.fixture(identity=(str(self.process.pid + 1), "10061", self.start))
        invocation.assert_not_called()
        self.assertIsNone(self.process.poll())
        self.assertFalse((self.home / "game").exists())
