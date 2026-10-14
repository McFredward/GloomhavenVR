"""Execute storage work in the app's real mount context before Application starts.

Android's run-as inherits adbd's mount namespace. The supplied capture rejects
both its ordinary external path and the paused process's proc/root bridge; it
does not identify the precise FUSE or SELinux rejection. This bridge instead uses
the already installed, debuggable app's Java Runtime to start a storage-only
shell. It never invokes game code, creates Application, or resumes gameplay.

The caller first proves a fresh debugger wait and stops every native app task.
An initial VM suspend only inspects the wait stack: invocation requires a real
one-shot step EVENT with Suspend_ALL on that exact main thread. Cleanup kills
the same PID/UID/starttime before disconnecting; JDWP Dispose would resume it.
"""
from __future__ import annotations

from collections import deque
import json
import re
import shlex
import socket
import struct
import subprocess
import tempfile
import time
import uuid

PACKAGE = "dev.gloomhavenvr.quest"
MAX_OUTPUT = 16 * 1024 * 1024
MAX_PACKET = 8 * 1024 * 1024
MAX_SCRIPT = 512 * 1024
HANDSHAKE = b"JDWP-Handshake"
# A live event-suspended connection is deliberately retained if both kill
# transports fail. Dropping its last socket reference would resume Application.
_PENDING_CLOSE = []


class StorageError(RuntimeError):
    """A storage command did not retain its proved process boundary."""


class _ProcessChanged(StorageError):
    """A positively observed PID/UID/starttime mismatch; never kill that PID."""


def _u32(value):
    return struct.pack(">I", value)


def _string(value):
    encoded = value.encode("utf-8")
    if len(encoded) > MAX_SCRIPT:
        raise StorageError("The storage debugger string exceeds its bounded size.")
    return _u32(len(encoded)) + encoded


class _Reader:
    def __init__(self, data):
        self.data, self.at = data, 0

    def take(self, length):
        if length < 0 or length > len(self.data) - self.at:
            raise StorageError("The storage debugger returned a truncated packet.")
        result = self.data[self.at:self.at + length]
        self.at += length
        return result

    def number(self, width=4):
        return int.from_bytes(self.take(width), "big")

    def string(self):
        try:
            return self.take(self.number()).decode("utf-8")
        except UnicodeDecodeError as error:
            raise StorageError("The storage debugger returned an invalid string.") from error

    def done(self):
        if self.at != len(self.data):
            raise StorageError("The storage debugger returned unexpected packet fields.")


class _Protocol:
    """Strict, bounded JDWP subset; no VM disposal or generic method evaluation."""

    def __init__(self, stream):
        self.stream, self.sequence = stream, 1
        self.events = deque()
        self.sizes = None
        self.types, self.methods_cache = {}, {}

    def _read(self, length, deadline):
        data = bytearray()
        while len(data) < length:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise StorageError("The app storage debugger did not respond in time.")
            self.stream.settimeout(remaining)
            try:
                block = self.stream.recv(length - len(data))
            except (socket.timeout, OSError) as error:
                raise StorageError("The app storage debugger connection was interrupted.") from error
            if not block:
                raise StorageError("The app storage debugger disconnected unexpectedly.")
            data.extend(block)
        return bytes(data)

    def send(self, command_set, command, payload=b""):
        self.sequence += 1
        identity = self.sequence
        packet = struct.pack(">IIBBB", len(payload) + 11, identity, 0, command_set, command) + payload
        try:
            self.stream.sendall(packet)
        except OSError as error:
            raise StorageError("The app storage debugger connection was interrupted.") from error
        return identity

    def _packet(self, deadline):
        header = self._read(11, deadline)
        length, identity, flags = struct.unpack(">IIB", header[:9])
        if not 11 <= length <= MAX_PACKET or flags not in (0, 0x80):
            raise StorageError("The storage debugger returned an invalid packet header.")
        data = self._read(length - 11, deadline)
        if flags:
            return identity, struct.unpack(">H", header[9:])[0], data
        command_set, command = header[9:]
        if (command_set, command) == (64, 100):
            if len(self.events) >= 64:
                raise StorageError("The storage debugger exceeded its event budget.")
            self.events.append(data)
        elif (command_set, command) == (199, 1):
            # Android's DDMS WAIT notification is not a debugger suspension.
            # Acknowledge only this bounded extension, never treat it as a step.
            self.stream.sendall(struct.pack(">IIBH", 11, identity, 0x80, 99))
        else:
            raise StorageError("The storage debugger returned an unsupported command.")
        return None

    def reply(self, identity, timeout=15):
        deadline = time.monotonic() + timeout
        for _ in range(128):
            value = self._packet(deadline)
            if value is None:
                continue
            reply_id, error, data = value
            if reply_id != identity:
                raise StorageError("The storage debugger returned a foreign reply identity.")
            if error:
                raise StorageError("The storage debugger rejected command (JDWP " + str(error) + ").")
            return data
        raise StorageError("The storage debugger exceeded its packet budget.")

    def command(self, command_set, command, payload=b"", timeout=15):
        return self.reply(self.send(command_set, command, payload), timeout)

    def id(self, value, kind="object"):
        if self.sizes is None or kind not in self.sizes:
            raise StorageError("The storage debugger identifier widths were not established.")
        try:
            return int(value).to_bytes(self.sizes[kind], "big")
        except (ValueError, OverflowError) as error:
            raise StorageError("The storage debugger identifier is invalid.") from error

    def widths(self):
        reader = _Reader(self.command(1, 7))
        names = ("field", "method", "object", "type", "frame")
        sizes = {name: reader.number() for name in names}
        reader.done()
        if any(not 1 <= size <= 8 for size in sizes.values()):
            raise StorageError("The storage debugger returned unsupported identifier widths.")
        self.sizes = sizes

    def signature(self, type_id):
        if type_id not in self.types:
            reader = _Reader(self.command(2, 1, self.id(type_id, "type")))
            self.types[type_id] = reader.string()
            reader.done()
        return self.types[type_id]

    def methods(self, type_id):
        if type_id not in self.methods_cache:
            reader = _Reader(self.command(2, 5, self.id(type_id, "type")))
            count = reader.number()
            if count > 16384:
                raise StorageError("The storage debugger returned too many methods.")
            methods = {}
            for _ in range(count):
                identity = reader.number(self.sizes["method"])
                value = (reader.string(), reader.string(), reader.number())
                if identity in methods:
                    raise StorageError("The storage debugger returned duplicate methods.")
                methods[identity] = value
            reader.done()
            self.methods_cache[type_id] = methods
        return self.methods_cache[type_id]

    def loaded(self, signature):
        reader = _Reader(self.command(1, 2, _string(signature)))
        if reader.number() != 1:
            raise StorageError("The required storage-only Java class is not uniquely loaded.")
        tag = reader.number(1)
        identity = reader.number(self.sizes["type"])
        status = reader.number()
        reader.done()
        if tag != 1 or not status & 2:
            raise StorageError("The required storage-only Java class is not prepared.")
        return identity

    def method(self, type_id, name, signature, *, static):
        matches = [identity for identity, value in self.methods(type_id).items()
                   if value[:2] == (name, signature) and bool(value[2] & 8) == static]
        if len(matches) != 1:
            raise StorageError("The required storage-only Java method is not unique.")
        return matches[0]

    def frames(self, thread):
        reader = _Reader(self.command(11, 6, self.id(thread) + _u32(0) + _u32(0xffffffff)))
        count = reader.number()
        if not 1 <= count <= 128:
            raise StorageError("The storage main thread did not return a bounded stack.")
        frames = []
        for _ in range(count):
            reader.take(self.sizes["frame"])
            tag = reader.number(1)
            type_id = reader.number(self.sizes["type"])
            method_id = reader.number(self.sizes["method"])
            reader.take(8)
            if tag != 1 or method_id not in self.methods(type_id):
                raise StorageError("The storage main thread returned an unknown frame.")
            frames.append((self.signature(type_id), self.methods(type_id)[method_id][0]))
        reader.done()
        return frames

    def main_thread(self):
        reader = _Reader(self.command(1, 4))
        count = reader.number()
        if not 1 <= count <= 4096:
            raise StorageError("The storage debugger returned an invalid thread inventory.")
        threads = [reader.number(self.sizes["object"]) for _ in range(count)]
        reader.done()
        main = []
        for thread in threads:
            name = _Reader(self.command(11, 1, self.id(thread)))
            if name.string() == "main":
                main.append(thread)
            name.done()
        if len(main) != 1:
            raise StorageError("The storage debugger did not identify one main thread.")
        return main[0]

    def step_event(self, thread):
        # Count applies to the already thread-filtered step, not a random event.
        modifiers = (b"\x0a" + self.id(thread) + _u32(0) + _u32(0)
                     + b"\x01" + _u32(1))
        reader = _Reader(self.command(15, 1, b"\x01\x02" + _u32(2) + modifiers))
        request = reader.number()
        reader.done()
        self.command(1, 9)
        deadline = time.monotonic() + 15
        for _ in range(128):
            if not self.events:
                if self._packet(deadline) is not None:
                    raise StorageError("The storage debugger returned an unexpected event reply.")
                continue
            event = _Reader(self.events.popleft())
            policy, count = event.number(1), event.number()
            if not 1 <= count <= 64:
                raise StorageError("The storage debugger returned an invalid event count.")
            matched = False
            for _ in range(count):
                kind, event_request = event.number(1), event.number()
                if kind == 90:  # VMStart may precede IDSizes after an attach.
                    event.take(self.sizes["object"])
                    if policy != 0:
                        raise StorageError("The storage debugger returned an unexpected VM-start suspension.")
                elif kind == 1:
                    event_thread = event.number(self.sizes["object"])
                    event.take(1 + self.sizes["type"] + self.sizes["method"] + 8)
                    if (event_request, event_thread, policy) != (request, thread, 2):
                        raise StorageError("The storage debugger returned a foreign step boundary.")
                    matched = True
                else:
                    raise StorageError("The storage debugger returned an unexpected event kind.")
            event.done()
            if matched:
                return request
        raise StorageError("The storage debugger did not establish its invocation event.")

    def value(self, reader):
        tag = reader.number(1)
        if tag in (ord("["), ord("L"), ord("s"), ord("t"), ord("g"), ord("l"), ord("c")):
            return tag, reader.number(self.sizes["object"])
        widths = {ord("B"): 1, ord("Z"): 1, ord("C"): 2, ord("S"): 2,
                  ord("I"): 4, ord("F"): 4, ord("J"): 8, ord("D"): 8, ord("V"): 0}
        if tag not in widths:
            raise StorageError("The storage debugger returned an invalid value tag.")
        return tag, reader.number(widths[tag])

    def invoke(self, thread, type_id, method, arguments=b"", *, target=None, timeout=15):
        if target is None:
            payload = self.id(type_id, "type") + self.id(thread) + self.id(method, "method")
            command_set, command = 3, 3
        else:
            payload = self.id(target) + self.id(thread) + self.id(type_id, "type") + self.id(method, "method")
            command_set, command = 9, 6
        # Exactly one string argument or none; no arbitrary game evaluation.
        payload += _u32(bool(arguments)) + arguments + _u32(1)
        reader = _Reader(self.command(command_set, command, payload, timeout=timeout))
        result, exception = self.value(reader), self.value(reader)
        reader.done()
        if exception[1]:
            raise StorageError("The storage-only Java invocation raised an exception.")
        return result


class AppCommand:
    def __init__(self, adb, address, hardware, process_identity_tuple):
        identity = tuple(map(str, process_identity_tuple))
        if (len(identity) != 3 or not re.fullmatch(r"[1-9][0-9]{0,9}", identity[0])
                or not identity[1].isdigit() or int(identity[1]) < 10000
                or not identity[2].isdigit() or not re.fullmatch(r"[A-Za-z0-9_-]+", hardware)):
            raise StorageError("The storage debugger received an invalid process identity.")
        self.adb, self.address, self.hardware, self.identity = adb, address, hardware, identity
        self.stream, self.forward, self.protocol, self.thread = None, None, None, None
        self.event_owned, self.closed, self.socket_owned = False, False, False
        self.scratches = set()
        try:
            self._process_guard(stopped=True)
            port = self.adb.run("-s", self.address, "forward", "tcp:0", "jdwp:" + identity[0], check_text=False).strip()
            if not re.fullmatch(r"[1-9][0-9]{0,4}", port) or int(port) > 65535:
                raise StorageError("ADB did not return a bounded owned debugger port.")
            self.forward = "tcp:" + port
            self.stream = socket.create_connection(("127.0.0.1", int(port)), timeout=15)
            self.protocol = _Protocol(self.stream)
            # Queue the suspend while native SIGSTOP still proves the wait.
            # No request round-trip is needed before the initial suspend packet.
            self.stream.sendall(HANDSHAKE)
            initial_suspend = self.protocol.send(1, 8)
            self._process_guard(stopped=True)
            self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "kill", "-CONT", identity[0], check_text=False)
            if self.protocol._read(len(HANDSHAKE), time.monotonic() + 15) != HANDSHAKE:
                raise StorageError("The app storage debugger handshake is invalid.")
            self.protocol.reply(initial_suspend)
            self.protocol.widths()
            self.thread = self.protocol.main_thread()
            self._wait_boundary()
            self._process_guard()
            self.socket_owned = True
            self.protocol.step_event(self.thread)
            self.event_owned = True
            self.guard()
            self.runtime_type = self.protocol.loaded("Ljava/lang/Runtime;")
            method = self.protocol.method(self.runtime_type, "getRuntime", "()Ljava/lang/Runtime;", static=True)
            value = self.protocol.invoke(self.thread, self.runtime_type, method)
            if value[0] != ord("L") or not value[1]:
                raise StorageError("The app storage Runtime was not established.")
            self.runtime = value[1]
            self.exec_method = self.protocol.method(self.runtime_type, "exec", "(Ljava/lang/String;)Ljava/lang/Process;", static=False)
            self._log({"appOriginStorageEstablished": True, "mainThreadEventSuspended": True,
                       "gameplayResumed": False, "pid": identity[0]})
        except BaseException as error:
            self.close(suppress=True)
            if isinstance(error, OSError):
                raise StorageError("The app storage debugger socket could not be opened.") from error
            raise

    def _log(self, value):
        with self.adb.log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(value) + "\n")

    def _run_as(self, script, timeout=20):
        return self.adb.run("-s", self.address, "shell", "-T", "run-as", PACKAGE, "sh", "-s",
                            input_text="set -eu\n" + script + "\n", timeout=timeout, check_text=False)

    def _process_guard(self, *, stopped=False):
        if self.adb.identity(self.address) != self.hardware:
            raise StorageError("The debugger address belongs to a different Quest.")
        pid = self.identity[0]
        prefix = "/proc/" + pid
        script = ("cat " + prefix + "/stat\ntr '\\000' '\\n' < " + prefix + "/cmdline | sed '/^$/d'\n"
                  "stat -c %u " + prefix + "\nid -u")
        if stopped:
            script += ("\nfor task in " + prefix + "/task/*; do state=$(sed -n 's/^State:[[:space:]]*\\([A-Za-z]\\).*/\\1/p' \"$task/status\"); "
                       "case \"$state\" in T|t) ;; *) exit 53;; esac; done")
        rows = self._run_as(script).splitlines()
        if (len(rows) != 4 or not rows[0].startswith(pid + " (") or ") " not in rows[0]
                or not rows[2].isdigit() or not rows[3].isdigit()):
            raise StorageError("The app storage process identity is incomplete.")
        fields = rows[0].rsplit(") ", 1)[1].split()
        if len(fields) < 20 or not fields[19].isdigit():
            raise StorageError("The app storage process start identity is incomplete.")
        if fields[19] != self.identity[2] or rows[2] != self.identity[1]:
            raise _ProcessChanged("The app storage process native identity changed.")
        if rows[1] != PACKAGE or rows[3] != self.identity[1]:
            raise StorageError("The app storage command no longer has the original package UID.")
        if stopped and fields[0] not in ("T", "t"):
            raise StorageError("The app storage process stopped boundary changed.")

    def _wait_boundary(self):
        frames = self.protocol.frames(self.thread)
        allowed = ("Ljava/", "Ljavax/", "Landroid/", "Ldalvik/", "Llibcore/", "Lsun/", "Ljdk/")
        if (any(not signature.startswith(allowed) for signature, _ in frames)
                or ("Landroid/os/Debug;", "waitForDebugger") not in frames
                or ("Landroid/app/ActivityThread;", "handleBindApplication") not in frames):
            raise StorageError("The app main thread did not retain its pre-Application debugger wait.")

    def guard(self):
        if self.closed or not self.event_owned:
            raise StorageError("The app storage invocation event is unavailable.")
        self._process_guard()
        reader = _Reader(self.protocol.command(11, 4, self.protocol.id(self.thread)))
        reader.number()  # Java thread status, independent from its suspend bit.
        suspended = reader.number()
        reader.done()
        if suspended != 1:
            raise StorageError("The app storage main thread is no longer event-suspended.")
        self._wait_boundary()

    def _binary(self, relative, *, maximum):
        # exec-out is binary on Windows as well. File-backed subprocess capture
        # bounds host RAM; remote head bounds output even if a scratch is large.
        script = "test ! -L " + shlex.quote(relative) + " && head -c " + str(maximum + 1) + " " + shlex.quote(relative)
        command = [self.adb.executable, "-s", self.adb.transport(self.address), "exec-out", "run-as", PACKAGE,
                   "sh", "-c", script]
        with tempfile.TemporaryFile() as output, tempfile.TemporaryFile() as error:
            try:
                result = self.adb.runner(command, stdout=output, stderr=error, stdin=subprocess.DEVNULL,
                                         timeout=30, shell=False)
            except subprocess.TimeoutExpired as failure:
                self._connection_failure("ADB timed out while reading app storage.", timed_out=True)
                raise StorageError("The app storage scratch read timed out.") from failure
            output.seek(0, 2)
            size = output.tell()
            error.seek(0)
            diagnostic = error.read(4096).decode("utf-8", errors="replace")
            if result.returncode or size > maximum:
                if result.returncode:
                    output.seek(0)
                    diagnostic = output.read(4096).decode("utf-8", errors="replace") + "\n" + diagnostic
                    self._connection_failure(diagnostic)
                raise StorageError("The app storage scratch output is inaccessible or exceeds its bounded size. " + diagnostic[:1024])
            output.seek(0)
            return output.read(maximum)

    def _connection_failure(self, diagnostic, *, timed_out=False):
        # Preserve this Adb instance's own exception class and reconnect contract
        # without importing a second installer module under isolated -I loaders.
        method = getattr(self.adb.run, "__func__", None)
        namespace = getattr(method, "__globals__", {})
        error = namespace.get("ConnectionRequired") if isinstance(namespace, dict) else None
        detector = namespace.get("connection_unavailable") if isinstance(namespace, dict) else None
        if (isinstance(error, type) and issubclass(error, RuntimeError)
                and (timed_out or (callable(detector) and detector(diagnostic)))):
            raise error("The Quest connection was interrupted during app storage work. Reconnect and unlock the same Quest; retained saves and installation work remain available.",
                        "offline-or-unauthorized")

    def shell(self, script, *, timeout=120):
        if not isinstance(script, str) or "\0" in script or len(script.encode("utf-8")) > MAX_SCRIPT:
            raise StorageError("The app storage command exceeds its bounded script size.")
        if not isinstance(timeout, (int, float)) or not 0 < timeout <= 1800:
            raise StorageError("The app storage command timeout is invalid.")
        self.guard()
        token = uuid.uuid4().hex
        relative = "cache/.ghvrq-jdwp-" + token
        absolute = "/data/user/0/" + PACKAGE + "/" + relative
        marker = "GHVRQ_SCRIPT_" + token
        content = ("#!/system/bin/sh\numask 077\n(\nset -eu\n" + script + "\n) > " + shlex.quote(absolute + "/stdout")
                   + " 2> " + shlex.quote(absolute + "/stderr") + "\nstatus=$?\nprintf '%s' \"$status\" > "
                   + shlex.quote(absolute + "/status.part") + "\nmv " + shlex.quote(absolute + "/status.part") + " "
                   + shlex.quote(absolute + "/status") + "\n")
        upload = ("umask 077\ntest ! -L cache\nmkdir -p cache\nmkdir " + shlex.quote(relative)
                  + "\ncat > " + shlex.quote(relative + "/job.sh") + " <<'" + marker + "'\n" + content + marker)
        completed = False
        try:
            self.scratches.add(relative)
            self._run_as(upload)
            self.guard()
            reader = _Reader(self.protocol.command(1, 11, _string("/system/bin/sh " + absolute + "/job.sh")))
            command_string = reader.number(self.protocol.sizes["object"])
            reader.done()
            self.protocol.command(9, 7, self.protocol.id(command_string))
            value = self.protocol.invoke(self.thread, self.runtime_type, self.exec_method,
                                         b"s" + self.protocol.id(command_string), target=self.runtime, timeout=min(timeout, 30))
            self.protocol.command(9, 8, self.protocol.id(command_string))
            if value[0] != ord("L") or not value[1]:
                raise StorageError("The app-origin storage shell did not start.")
            # Do not waitFor on the suspended main thread: process reapers can
            # also be suspended. Child completion is an atomic private marker.
            deadline = time.monotonic() + timeout
            status = None
            while time.monotonic() < deadline:
                self.guard()
                observed = self._run_as("if test -f " + shlex.quote(relative + "/status") + "; then test ! -L "
                                        + shlex.quote(relative + "/status") + "; cat " + shlex.quote(relative + "/status") + "; fi")
                if observed:
                    if not re.fullmatch(r"[0-9]{1,3}", observed) or int(observed) > 255:
                        raise StorageError("The app storage shell returned an invalid completion marker.")
                    status = int(observed)
                    break
                time.sleep(0.1)
            if status is None:
                raise StorageError("The app storage shell did not complete in time.")
            output = self._binary(relative + "/stdout", maximum=MAX_OUTPUT)
            if status:
                diagnostic = self._binary(relative + "/stderr", maximum=16384).decode("utf-8", errors="replace")
                raise StorageError("The app-origin storage shell failed (exit " + str(status) + "): " + diagnostic[:1024])
            self.guard()
            completed = True
            return output.decode("utf-8", errors="surrogateescape")
        except BaseException:
            # A timed-out child must not outlive storage recovery and mutate
            # saves while the caller retries. Closing kills its package first.
            self.close(suppress=True)
            raise
        finally:
            if completed and not self.closed:
                self._process_guard()
                self._run_as("test ! -L " + shlex.quote(relative) + "\nrm -rf " + shlex.quote(relative))
                self.scratches.discard(relative)

    def _cleanup_scratches(self):
        if not getattr(self, "scratches", None):
            return
        try:
            if self.adb.identity(self.address) != self.hardware or self._run_as("id -u") != self.identity[1]:
                return
            for relative in tuple(self.scratches):
                if not re.fullmatch(r"cache/\.ghvrq-jdwp-[0-9a-f]{32}", relative):
                    raise StorageError("The app storage scratch ownership is invalid.")
                self._run_as("test ! -L cache\ntest ! -L " + shlex.quote(relative) + "\nrm -rf " + shlex.quote(relative))
                self.scratches.discard(relative)
        except (RuntimeError, OSError) as error:
            self._log({"appOriginStorageScratchCleanupDeferred": str(error)[:1024], "saveTreesUntouched": True})

    def close(self, *, suppress=False):
        if self.closed:
            return
        failure, terminated = None, False
        try:
            self._process_guard()
            # Android stops the process group, including storage-shell children.
            # Never kill the PID first and then stop a newly restarted package.
            self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE, check_text=False)
            pid = self.identity[0]
            marker = self._run_as("if test ! -e /proc/" + pid + "/stat; then printf GHVRQ_APP_EXITED; "
                                  "else cat /proc/" + pid + "/stat; fi")
            if marker == "GHVRQ_APP_EXITED":
                terminated = True
            elif marker.startswith(pid + " (") and ") " in marker:
                fields = marker.rsplit(") ", 1)[1].split()
                terminated = len(fields) >= 20 and (fields[19] != self.identity[2] or fields[0] in ("Z", "X"))
            if not terminated:
                raise StorageError("Android did not confirm the owned storage process termination.")
        except _ProcessChanged as error:
            # The original native identity no longer exists. Its old socket can
            # be closed without force-stopping a process that reused the PID.
            terminated = True
            self._log({"appOriginStorageOriginalProcessChanged": True, "foreignProcessUntouched": True})
        except (RuntimeError, OSError) as error:
            failure = error
            if getattr(self, "socket_owned", False) and self.protocol is not None:
                try:
                    # This socket addresses the previously proved VM itself;
                    # unlike a new ADB address, it cannot select a foreign PID.
                    # Exit terminates without Application/finally callbacks.
                    self.protocol.command(1, 10, _u32(0), timeout=5)
                    terminated, failure = True, None
                    self._log({"appOriginStorageExitFallback": True, "gameplayResumed": False})
                except (RuntimeError, OSError):
                    pass
            if not terminated:
                if self not in _PENDING_CLOSE:
                    _PENDING_CLOSE.append(self)
                self._log({"appOriginStorageCleanupUnconfirmed": str(error)[:1024],
                           "debuggerRetainedSuspended": True, "foreignProcessUntouched": True})
        if terminated:
            self.closed = True
            if self in _PENDING_CLOSE:
                _PENDING_CLOSE.remove(self)
            if self.stream is not None:
                self.stream.close()
                self.stream = None
            if self.forward is not None:
                try:
                    self.adb.run("-s", self.address, "forward", "--remove", self.forward, check_text=False)
                except (RuntimeError, OSError) as error:
                    if failure is None:
                        failure = error
                self.forward = None
            self._cleanup_scratches()
        if failure is not None and not suppress:
            raise StorageError("The owned app storage process cleanup could not be confirmed.") from failure
