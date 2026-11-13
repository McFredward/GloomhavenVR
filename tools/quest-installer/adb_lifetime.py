"""Wizard-owned local ADB server; never stop another application's server.

ADB normally detaches a persistent server from each short-lived client. The
Wizard instead starts one foreground server on a private socket, only when ADB
is first needed. A small detached guard owns that process and watches a pipe
held solely by the Wizard. Closing its console (including abrupt Windows
console closure) closes that pipe and reaps the server. A Windows kill-on-close
job also protects against termination of the guard itself. Existing/configured
servers are borrowed unchanged, since competing servers can claim the same USB
interface. Neither cleanup path restarts headset adbd or forgets Wi-Fi settings.
"""
from __future__ import annotations
import ctypes
import hmac
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import threading
import time

CONTROL_ENV = "GHVRQ_ADB_LIFETIME"
SOCKET_ENV = "ADB_SERVER_SOCKET"
MAX_REQUEST = 16384


def _line(stream):
    raw = stream.readline(MAX_REQUEST + 1)
    if not raw.endswith(b"\n") or len(raw) > MAX_REQUEST:
        raise RuntimeError("The owned ADB server returned an invalid control message.")
    return json.loads(raw)


def _listening(port):
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=.15):
            return True
    except OSError:
        return False


def _request(descriptor, value):
    with socket.create_connection(("127.0.0.1", descriptor["port"]), timeout=20) as channel:
        channel.settimeout(20)
        channel.sendall(json.dumps(dict(value, token=descriptor["token"])).encode() + b"\n")
        with channel.makefile("rb") as stream:
            result = _line(stream)
    if result.get("status") != "ready":
        raise RuntimeError(result.get("error") or "The owned ADB server did not become ready.")
    return result


def ensure_server(executable):
    """Qualify the owned server before each CLI or binary operation."""
    raw = os.environ.get(CONTROL_ENV)
    if not raw:
        return
    descriptor = json.loads(raw)
    if os.environ.get(SOCKET_ENV) != descriptor.get("socket"):
        raise RuntimeError("The Wizard's owned ADB socket changed; restart the Wizard.")
    _request(descriptor, {"executable": str(Path(executable).resolve())})


class Session:
    def __init__(self, directory, *, environ=None, probe=_listening):
        self.directory = Path(directory)
        self.environ = os.environ if environ is None else environ
        self.probe = probe
        self.process = None
        self.saved = {}
        self.descriptor = None

    def start(self):
        # Honor explicitly selected external servers and keep a currently
        # running default server's USB ownership. Never issue kill-server.
        external = (self.environ.get(CONTROL_ENV) or self.environ.get(SOCKET_ENV)
                or self.environ.get("ANDROID_ADB_SERVER_ADDRESS")
                or self.environ.get("ANDROID_ADB_SERVER_PORT"))
        shared = False if external else self.probe(5037)
        self.directory.mkdir(parents=True, exist_ok=True)
        log = self.directory / ("adb-lifetime-" + secrets.token_hex(6) + ".log")
        if external or shared:
            log.write_text(json.dumps({"event": "external-server-borrowed", "cleanupOwned": False,
                                       "reason": "configured" if external else "already-listening"}) + "\n", encoding="utf-8")
            return self
        options = {"stdin": subprocess.PIPE, "stdout": subprocess.PIPE, "stderr": subprocess.DEVNULL,
                   "env": dict(self.environ), "close_fds": True}
        if os.name == "nt":
            options["creationflags"] = subprocess.CREATE_NO_WINDOW
        else:
            options["start_new_session"] = True
        self.process = subprocess.Popen([sys.executable, "-I", "-B", str(Path(__file__).resolve()), "guard"], **options)
        token = secrets.token_urlsafe(32)
        self.process.stdin.write(json.dumps({"token": token, "log": str(log)}).encode() + b"\n")
        self.process.stdin.flush()
        result, errors = [], []
        def read_ready():
            try: result.append(_line(self.process.stdout))
            except Exception as error: errors.append(error)
        ready = threading.Thread(target=read_ready, name="quest-adb-guard-ready", daemon=True)
        ready.start(); ready.join(timeout=10)
        if ready.is_alive() or errors or not result or result[0].get("status") != "ready":
            self.close()
            raise RuntimeError("The Wizard's local ADB lifetime guard could not start.")
        self.process.stdout.close()
        self.descriptor = dict(result[0], token=token)
        for key, value in ((CONTROL_ENV, json.dumps(self.descriptor)), (SOCKET_ENV, self.descriptor["socket"])):
            self.saved[key] = self.environ.get(key)
            self.environ[key] = value
        return self

    def close(self):
        for key, value in self.saved.items():
            if value is None: self.environ.pop(key, None)
            else: self.environ[key] = value
        self.saved.clear()
        process = self.process
        if process is None: return
        if process.stdin and not process.stdin.closed: process.stdin.close()
        try: process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            process.kill(); process.wait(timeout=5)
        if process.stdout and not process.stdout.closed: process.stdout.close()
        self.process = None


class _WindowsJob:
    """The guard's private job only contains its foreground server process."""
    def __init__(self, kernel=None):
        from ctypes import wintypes as w
        class Basic(ctypes.Structure):
            _fields_ = [("process_time", ctypes.c_int64), ("job_time", ctypes.c_int64),
                        ("flags", w.DWORD), ("minimum", ctypes.c_size_t), ("maximum", ctypes.c_size_t),
                        ("active", w.DWORD), ("affinity", ctypes.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]
        class Counters(ctypes.Structure):
            _fields_ = [(name, ctypes.c_uint64) for name in ("reads", "writes", "other", "read_bytes", "write_bytes", "other_bytes")]
        class Extended(ctypes.Structure):
            _fields_ = [("basic", Basic), ("io", Counters), ("process_memory", ctypes.c_size_t),
                        ("job_memory", ctypes.c_size_t), ("peak_process", ctypes.c_size_t), ("peak_job", ctypes.c_size_t)]
        self.kernel = kernel or ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, w.LPCWSTR]
        self.kernel.CreateJobObjectW.restype = w.HANDLE
        self.kernel.SetInformationJobObject.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD]
        self.kernel.SetInformationJobObject.restype = w.BOOL
        self.kernel.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
        self.kernel.AssignProcessToJobObject.restype = w.BOOL
        self.kernel.CloseHandle.argtypes = [w.HANDLE]
        self.kernel.CloseHandle.restype = w.BOOL
        self.handle = self.kernel.CreateJobObjectW(None, None)
        if not self.handle: raise OSError("Windows could not create the owned ADB process job.")
        limits = Extended(); limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.kernel.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            self.close()
            raise OSError("Windows could not configure owned ADB process cleanup.")

    def assign(self, process):
        if not self.kernel.AssignProcessToJobObject(self.handle, int(process._handle)):
            raise OSError("Windows could not attach the owned ADB server to its cleanup job.")

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None


def _stop(process):
    if process is None: return
    if process.poll() is None:
        process.terminate()
        try: process.wait(timeout=3)
        except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=3)
    else: process.wait()


def _guard():
    initial = _line(sys.stdin.buffer)
    stopped = threading.Event()
    def watch_owner():
        sys.stdin.buffer.read(1)
        stopped.set()
    threading.Thread(target=watch_owner, name="quest-adb-owner-pipe", daemon=True).start()
    child = None; job = None; selected = None
    listener = socket.socket(); reservation = socket.socket()
    log = Path(initial["log"])
    try:
        listener.bind(("127.0.0.1", 0)); listener.listen(8); listener.settimeout(.2)
        reservation.bind(("127.0.0.1", 0))
        port = reservation.getsockname()[1]
        # Unity's platform-tools30 rejects a hostname in the *listener* spec.
        # tcp:PORT is loopback-only unless -a is supplied (which we never do).
        # The CLI and binary adapter both resolve this form to localhost.
        spec = "tcp:" + str(port)
        print(json.dumps({"status": "ready", "port": listener.getsockname()[1], "socket": spec}), flush=True)
        with log.open("a", encoding="utf-8", buffering=1) as output:
            output.write(json.dumps({"event": "guard-started", "pid": os.getpid(), "socket": spec}) + "\n")
            while not stopped.is_set():
                try: channel, _ = listener.accept()
                except socket.timeout: continue
                with channel:
                    channel.settimeout(3)
                    try:
                        with channel.makefile("rb") as source: request = _line(source)
                        if not hmac.compare_digest(str(request.get("token", "")), initial["token"]):
                            channel.sendall(b'{"status":"error","error":"The owned ADB request is unauthorized."}\n')
                            continue
                        executable = Path(request["executable"])
                        if not executable.is_absolute() or not executable.is_file():
                            raise RuntimeError("The selected ADB executable is missing.")
                        stat = executable.stat()
                        signature = (str(executable), stat.st_size, stat.st_mtime_ns)
                        if selected != signature and child is not None:
                            # A client from a different platform-tools release
                            # otherwise replaces a mismatched server itself,
                            # escaping this guard. Reap only our foreground
                            # process before accepting the new client version.
                            _stop(child); child = None
                            output.write(json.dumps({"event": "server-version-changed"}) + "\n")
                        if child is None or child.poll() is not None:
                            if stopped.is_set(): break
                            reservation.close()
                            if os.name == "nt" and job is None: job = _WindowsJob()
                            options = {"stdin": subprocess.DEVNULL, "stdout": output, "stderr": subprocess.STDOUT,
                                       "close_fds": True}
                            if os.name == "nt": options["creationflags"] = subprocess.CREATE_NO_WINDOW
                            child = subprocess.Popen([str(executable), "-L", spec, "server", "nodaemon"], **options)
                            if job: job.assign(child)
                            selected = signature
                            output.write(json.dumps({"event": "server-started", "pid": child.pid}) + "\n")
                            deadline = time.monotonic() + 12
                            while not _listening(port):
                                if child.poll() is not None or stopped.is_set() or time.monotonic() >= deadline:
                                    raise RuntimeError("The owned local ADB server could not start; see " + str(log))
                                stopped.wait(.05)
                        channel.sendall(b'{"status":"ready"}\n')
                    except (OSError, ValueError, KeyError, RuntimeError) as error:
                        _stop(child); child = None
                        output.write(json.dumps({"event": "server-error", "message": str(error)}) + "\n")
                        try: channel.sendall(json.dumps({"status": "error", "error": str(error)}).encode() + b"\n")
                        except OSError: pass
            _stop(child); child = None
            output.write(json.dumps({"event": "guard-stopped"}) + "\n")
    finally:
        _stop(child)
        if job: job.close()
        listener.close(); reservation.close()


if __name__ == "__main__":
    _guard()
