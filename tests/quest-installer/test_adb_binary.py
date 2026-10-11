"""Real local ADB-server socket controls for binary EOF, framing and bounds."""
import contextlib
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import socket
import struct
import subprocess
import sys
import tempfile
import time
import tracemalloc
from types import SimpleNamespace
import unittest
from unittest import mock

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_adb_binary", REPO / "tools/quest-installer/adb_binary.py")
binary = importlib.util.module_from_spec(spec); spec.loader.exec_module(binary)


def exact(sock, count):
    data = bytearray()
    while len(data) < count:
        block = sock.recv(count - len(data))
        if not block: raise EOFError("early socket EOF")
        data.extend(block)
    return bytes(data)


def server(directory, mode):
    root = Path(directory); report = {"mode": mode, "services": [], "frames": [], "bytes": 0}
    listener = socket.socket(); listener.bind(("127.0.0.1", 0)); listener.listen(1)
    listener.settimeout(8); (root / "port").write_text(str(listener.getsockname()[1]))
    connection = None
    try:
        connection, _ = listener.accept(); connection.settimeout(5)
        for number in range(2):
            count = int(exact(connection, 4), 16)
            service = exact(connection, count).decode(); report["services"].append(service)
            if mode == "denied" and number == 0:
                message = b"device unauthorized: accept USB debugging"
                connection.sendall(b"FAIL" + format(len(message), "04x").encode() + message)
                return
            if mode == "bad-status": connection.sendall(b"WHAT"); return
            connection.sendall(b"OKAY")
        if mode == "silent": time.sleep(2); return
        if mode == "truncated": connection.sendall(b"\x01\x03"); return
        if mode == "wrong-id": connection.sendall(struct.pack("<BI", 4, 0)); return
        if mode == "huge-frame": connection.sendall(struct.pack("<BI", 1, 0x7fffffff)); return
        if mode == "bad-exit": connection.sendall(struct.pack("<BI", 3, 4) + b"\0" * 4); return
        if mode == "early-exit": connection.sendall(struct.pack("<BI", 3, 1) + b"\0"); time.sleep(.2); return
        digest = hashlib.sha256()
        while True:
            ident, count = struct.unpack("<BI", exact(connection, 5))
            report["frames"].append({"id": ident, "size": count})
            if ident == 4:
                assert count == 0; report["closeStdin"] = True; break
            assert ident == 0 and 0 < count <= binary.CHUNK
            block = exact(connection, count); digest.update(block); report["bytes"] += len(block)
        report["sha256"] = digest.hexdigest()
        if mode == "no-exit": return
        if mode == "stderr":
            text = b"discard-" * 8192 + b"retained-last-error"
            connection.sendall(struct.pack("<BI", 2, len(text)) + text)
            connection.sendall(struct.pack("<BI", 3, 1) + b"\x07")
        else:
            # stdout payload may contain byte patterns that resemble headers.
            connection.sendall(struct.pack("<BI", 1, 7) + b"\0\xffhello")
            connection.sendall(struct.pack("<BI", 3, 1) + b"\0")
    except Exception as error:
        report["serverError"] = str(error)
    finally:
        if connection: connection.close()
        listener.close()
        (root / "report.json").write_text(json.dumps(report))


@contextlib.contextmanager
def fake_server(root, mode="ok"):
    folder = root / ("server-" + mode); folder.mkdir()
    process = subprocess.Popen([sys.executable, "-I", "-B", str(Path(__file__).resolve()),
                                "--server", str(folder), mode],
                               stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    try:
        deadline = time.monotonic() + 5
        while not (folder / "port").exists():
            if process.poll() is not None or time.monotonic() >= deadline:
                raise AssertionError("The local ADB-server fixture did not start")
            time.sleep(.01)
        port = (folder / "port").read_text()
        with mock.patch.dict(os.environ, {"ADB_SERVER_SOCKET": "tcp:127.0.0.1:" + port}):
            yield folder
    finally:
        try:
            process.wait(timeout=4)
        except subprocess.TimeoutExpired:
            process.kill(); process.wait(timeout=3)
        if process.stderr: process.stderr.close()


class BinaryAdbTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); self.local = self.root / "payload"
        self.payload = bytes(range(256)) * 513 + b"\r\n\0\x1aend"
        self.local.write_bytes(self.payload); self.adb = SimpleNamespace(executable="adb.exe")

    def call(self, **kwargs):
        return binary.stream(self.adb, "QUEST-123", self.local, "cat > '/app/short-stage'", **kwargs)

    def test_exact_binary_frames_eof_and_remote_exit(self):
        values = []
        with fake_server(self.root) as folder:
            result = self.call(timeout=5, report=lambda done, total: values.append((done, total)))
        report = json.loads((folder / "report.json").read_text())
        self.assertEqual(0, result.returncode); self.assertEqual("", result.stderr)
        self.assertEqual(["host:transport:QUEST-123", "shell,v2,raw:cat > '/app/short-stage'"], report["services"])
        self.assertEqual(len(self.payload), report["bytes"])
        self.assertEqual(hashlib.sha256(self.payload).hexdigest(), report["sha256"])
        self.assertTrue(report["closeStdin"])
        self.assertTrue(values)
        self.assertTrue(all(done < total for done, total in values))

    def test_remote_failure_stderr_is_observed_and_bounded(self):
        with fake_server(self.root, "stderr"):
            result = self.call(timeout=5)
        self.assertEqual(7, result.returncode)
        self.assertEqual(binary.MAX_STDERR, len(result.stderr))
        self.assertTrue(result.stderr.endswith("retained-last-error"))

    def test_smart_socket_denied_transport_is_actionable(self):
        with fake_server(self.root, "denied"):
            with self.assertRaisesRegex(RuntimeError, "unauthorized.*USB debugging"):
                self.call(timeout=5)

    def test_truncated_response_is_not_a_success(self):
        with fake_server(self.root, "truncated"):
            with self.assertRaises((RuntimeError, OSError)):
                self.call(timeout=5)

    def test_wrong_frame_id_is_not_a_success(self):
        with fake_server(self.root, "wrong-id"):
            with self.assertRaises((RuntimeError, OSError)):
                self.call(timeout=5)

    def test_oversized_frame_does_not_allocate_its_declared_size(self):
        with fake_server(self.root, "huge-frame"):
            with self.assertRaises((RuntimeError, OSError)):
                self.call(timeout=5)

    def test_malformed_exit_is_not_a_success(self):
        with fake_server(self.root, "bad-exit"):
            with self.assertRaises((RuntimeError, OSError)):
                self.call(timeout=5)

    def test_bad_smart_socket_status_is_not_a_success(self):
        with fake_server(self.root, "bad-status"):
            with self.assertRaisesRegex(RuntimeError, "Malformed ADB server"):
                self.call(timeout=5)

    def test_eof_without_remote_exit_is_not_a_success(self):
        with fake_server(self.root, "no-exit"):
            with self.assertRaisesRegex(RuntimeError, "complete shell-v2"):
                self.call(timeout=5)

    def test_silent_command_has_a_bounded_deadline(self):
        with fake_server(self.root, "silent"):
            began = time.monotonic()
            with self.assertRaises(subprocess.TimeoutExpired): self.call(timeout=.2)
            self.assertLess(time.monotonic() - began, 1.5)

    def test_early_zero_exit_does_not_acknowledge_large_unread_input(self):
        with self.local.open("wb") as target:
            for _ in range(256): target.write(bytes(range(256)) * 256)
        with fake_server(self.root, "early-exit"):
            with self.assertRaises((RuntimeError, OSError)):
                self.call(timeout=5)

    def test_local_mutation_disproves_success(self):
        def mutate(done, total):
            self.local.write_bytes(b"changed")
        with fake_server(self.root):
            with self.assertRaisesRegex(RuntimeError, "Local content changed"):
                self.call(timeout=5, report=mutate)

    def test_empty_input_still_closes_stdin_and_waits_for_remote_exit(self):
        self.local.write_bytes(b"")
        with fake_server(self.root) as folder:
            result = self.call(timeout=5)
        report = json.loads((folder / "report.json").read_text())
        self.assertEqual(0, result.returncode)
        self.assertEqual([{"id": 4, "size": 0}], report["frames"])

    def test_16_mib_payload_is_file_backed_and_bounded(self):
        with self.local.open("wb") as target:
            for _ in range(256): target.write(bytes(range(256)) * 256)
        with fake_server(self.root) as folder:
            tracemalloc.start()
            try:
                result = self.call(timeout=10); _, peak = tracemalloc.get_traced_memory()
            finally:
                tracemalloc.stop()
        self.assertEqual(0, result.returncode)
        self.assertLess(peak, 2 * 1024 * 1024)
        report = json.loads((folder / "report.json").read_text())
        self.assertEqual(16 * 1024 * 1024, report["bytes"])

    def test_server_env_precedence_and_loopback_bounds(self):
        endpoint = binary._server_endpoint({"ADB_SERVER_SOCKET": "tcp:localhost:5555", "ANDROID_ADB_SERVER_PORT": "6666"})
        self.assertEqual(("tcp", ("localhost", 5555)), endpoint)
        self.assertEqual(("tcp", ("127.0.0.1", 6666)), binary._server_endpoint({"ANDROID_ADB_SERVER_PORT": "6666"}))
        self.assertEqual(("tcp", ("::1", 5037)), binary._server_endpoint({"ADB_SERVER_SOCKET": "tcp:[::1]:5037"}))
        for env in ({"ADB_SERVER_SOCKET": "tcp:192.168.0.5:5037"},
                    {"ANDROID_ADB_SERVER_ADDRESS": "203.0.113.1"},
                    {"ADB_SERVER_SOCKET": "tcp:127.0.0.1:0"},
                    {"ADB_SERVER_SOCKET": "tcp:70000"}):
            with self.subTest(env=env):
                with self.assertRaises(RuntimeError): binary._server_endpoint(env)

    def test_bad_input_has_no_network_side_effect(self):
        with mock.patch.object(binary.socket, "create_connection") as connect:
            for value in (0, -1, float("inf"), float("nan")):
                with self.subTest(timeout=value):
                    with self.assertRaises(RuntimeError): self.call(timeout=value)
            with self.assertRaises(RuntimeError):
                binary.stream(self.adb, "bad\nserial", self.local, "cat", 5)
            connect.assert_not_called()


if __name__ == "__main__":
    if sys.argv[1:2] == ["--server"]:
        server(sys.argv[2], sys.argv[3])
    else:
        unittest.main()
