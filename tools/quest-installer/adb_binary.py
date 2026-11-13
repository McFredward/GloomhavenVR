"""Bounded binary stdin over the local ADB server's shell-v2 protocol.

Android's raw exec-in path uses a PTY and closes it at local EOF, sending
SIGHUP before a command can reliably finish its EOF follow-up. The Windows
shell CLI instead reads redirected stdin through the CRT's text mode. Speaking
shell-v2 to the already-running local server avoids both boundaries and adds
no base64 expansion or whole-file temporary copy.
"""
from pathlib import Path
import ipaddress
import math
import os
import re
import socket
import struct
import subprocess
import threading
import time
from types import SimpleNamespace

CHUNK = 64 * 1024
MAX_FRAME = 1024 * 1024
MAX_STDERR = 16 * 1024
MAX_STDOUT = 16 * 1024


def _server_endpoint(environ=None):
    env = os.environ if environ is None else environ
    spec = env.get("ADB_SERVER_SOCKET")
    if spec:
        if spec.startswith("localfilesystem:"):
            path = spec[len("localfilesystem:"):]
            if not path or "\x00" in path or not hasattr(socket, "AF_UNIX"):
                raise RuntimeError("The configured local ADB socket is unavailable.")
            return "unix", path
        if not spec.startswith("tcp:"):
            raise RuntimeError("Binary upload requires a local TCP or filesystem ADB server socket.")
        value = spec[4:]
        if ":" in value:
            host, port = value.rsplit(":", 1)
            host = host.strip("[]")
        else:
            host, port = "127.0.0.1", value
    else:
        host = env.get("ANDROID_ADB_SERVER_ADDRESS") or "127.0.0.1"
        port = env.get("ANDROID_ADB_SERVER_PORT") or "5037"
    if host != "localhost":
        try:
            if not ipaddress.ip_address(host).is_loopback:
                raise ValueError()
        except ValueError as error:
            raise RuntimeError("Binary upload requires the ADB server on this computer.") from error
    if not re.fullmatch(r"[0-9]{1,5}", str(port)) or not 0 < int(port) < 65536:
        raise RuntimeError("The configured ADB server port is invalid.")
    return "tcp", (host, int(port))


def stream(adb, serial, local, script, timeout, report=None):
    """Return remote returncode and bounded output after exact binary stdin EOF.

    report receives (transmitted_bytes, total_bytes); intermediate reports stop
    below total; the caller owns exact remote verification and publication.
    Each output stream retains at most its last 16 KiB, including the caller's
    small publication acknowledgement. No whole payload is retained in memory.
    Every read and write shares a finite operation deadline; writer shutdown
    has a separate two-second bound after the socket is closed.
    """
    local = Path(local)
    if local.is_symlink() or not local.is_file():
        raise RuntimeError("Binary upload requires an ordinary local file.")
    if not isinstance(serial, str) or not serial or len(serial) > 256 or any(ord(c) < 32 for c in serial):
        raise RuntimeError("Invalid ADB transport serial.")
    if not isinstance(script, str) or "\x00" in script:
        raise RuntimeError("Invalid binary upload command.")
    if not isinstance(timeout, (float, int)) or not math.isfinite(timeout) or timeout <= 0:
        raise RuntimeError("Binary upload requires a finite positive timeout.")
    deadline = time.monotonic() + timeout
    command = [str(getattr(adb, "executable", "adb")), "binary-shell-v2", serial]
    endpoint = _server_endpoint()
    stopped = threading.Event()
    writer_done = threading.Event()
    writer_errors = []
    writer = None
    channel = None
    stderr = bytearray()
    stdout = bytearray()
    state = {"bytes": 0, "closed": False}
    initial = local.stat()

    def remaining():
        left = deadline - time.monotonic()
        if left <= 0:
            raise subprocess.TimeoutExpired(command, timeout)
        return left

    def send(data):
        view = memoryview(data)
        while view:
            remaining()
            if stopped.is_set():
                raise RuntimeError("Binary upload was stopped.")
            try:
                count = channel.send(view)
            except socket.timeout:
                continue
            if not count:
                raise RuntimeError("The ADB server closed binary upload input.")
            view = view[count:]

    def receive(size):
        data = bytearray()
        while len(data) < size:
            remaining()
            if writer_errors:
                raise writer_errors[0]
            try:
                block = channel.recv(size - len(data))
            except socket.timeout:
                continue
            if not block:
                raise RuntimeError("The ADB server closed without a complete shell-v2 response.")
            data.extend(block)
        return bytes(data)

    def request(service):
        encoded = service.encode("utf-8")
        if len(encoded) > 65535:
            raise RuntimeError("The binary upload service command is too long.")
        send(format(len(encoded), "04x").encode("ascii") + encoded)
        status = receive(4)
        if status == b"FAIL":
            try:
                size = int(receive(4), 16)
            except ValueError as error:
                raise RuntimeError("Malformed ADB server failure length.") from error
            message = receive(size)[-MAX_STDERR:].decode("utf-8", errors="replace")
            raise RuntimeError("The ADB server refused binary upload: " + message)
        if status != b"OKAY":
            raise RuntimeError("Malformed ADB server response.")

    def write_input():
        last_report = 0.0
        try:
            with local.open("rb", buffering=0) as source:
                while block := source.read(CHUNK):
                    send(struct.pack("<BI", 0, len(block)))
                    send(block)
                    state["bytes"] += len(block)
                    now = time.monotonic()
                    if report and now - last_report >= 1:
                        report(min(state["bytes"], max(0, initial.st_size - 1)), initial.st_size)
                        last_report = now
                current = local.stat()
                if ((initial.st_size, initial.st_mtime_ns, initial.st_ctime_ns)
                        != (current.st_size, current.st_mtime_ns, current.st_ctime_ns)):
                    raise RuntimeError("Local content changed during binary upload.")
                send(struct.pack("<BI", 4, 0))
                state["closed"] = True
        except Exception as error:
            writer_errors.append(error)
        finally:
            writer_done.set()

    try:
        if endpoint[0] == "unix":
            channel = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            channel.settimeout(min(1, remaining()))
            channel.connect(endpoint[1])
        else:
            channel = socket.create_connection(endpoint[1], timeout=min(1, remaining()))
        channel.settimeout(min(1, remaining()))
        request("host:transport:" + serial)
        request("shell,v2,raw:" + script)
        writer = threading.Thread(target=write_input, name="quest-adb-binary-writer", daemon=True)
        writer.start()
        while True:
            frame_id, size = struct.unpack("<BI", receive(5))
            if frame_id not in (1, 2, 3) or size > MAX_FRAME or (frame_id == 3 and size != 1):
                raise RuntimeError("Malformed shell-v2 output frame.")
            if frame_id == 3:
                code = receive(1)[0]
                if not writer_done.wait(min(1, remaining())):
                    raise RuntimeError("The remote command completed before binary input finished.")
                if code == 0:
                    if writer_errors:
                        raise writer_errors[0]
                    if not state["closed"] or state["bytes"] != initial.st_size:
                        raise RuntimeError("The remote command completed without exact binary input EOF.")
                return SimpleNamespace(returncode=code, stderr=stderr.decode("utf-8", errors="replace"),
                                       stdout=stdout.decode("utf-8", errors="replace"))
            while size:
                block = receive(min(CHUNK, size)); size -= len(block)
                retained, bound = (stderr, MAX_STDERR) if frame_id == 2 else (stdout, MAX_STDOUT)
                retained.extend(block)
                if len(retained) > bound:
                    del retained[:-bound]
    finally:
        stopped.set()
        if channel is not None:
            try:
                channel.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            channel.close()
        if writer is not None:
            writer.join(timeout=2)
            if writer.is_alive():
                raise RuntimeError("The binary upload writer did not stop after its socket closed.")
