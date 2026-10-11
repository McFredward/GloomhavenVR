"""Recover one actual Android signing conflict without changing any signing key.

The transaction is deliberately independent of the host OS and of Unity. All
mutable data and the old installed APK have durable, verified host backups before
package removal. Large immutable banks remain on the headset through proven
renames or verified copies. An interrupted transaction resumes restoration before
any launch.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shlex
import shutil
import struct
import subprocess
import sys
import tarfile
import tempfile
import threading
import time
import uuid
import zipfile

PACKAGE = "dev.gloomhavenvr.quest"
EXTERNAL = "/sdcard/Android/data/" + PACKAGE
BANKS = ("files/quest-owned-game", "files/quest-mod-resources")
PHASES = ("detect", "backup", "replace", "restore", "verify")


class RecoveryError(RuntimeError):
    """The existing data could not be proved safe to replace or restore."""


def progress(phase, done, total, detail, *, unit="tasks", local=False):
    value = {"schema": 1, "phase": "install-recovery-" + phase if local else "install-signature-" + phase,
             "operation": "apk", "done": done, "total": total, "unit": unit,
             "status": "progress", "detail": detail[:1024]}
    print("GHVRQ_PROGRESS " + json.dumps(value, ensure_ascii=False), flush=True)


def file_hash(path):
    value = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""): value.update(block)
    return value.hexdigest()


def ordinary(path):
    path = Path(path)
    for part in (path, *path.parents):
        if part.is_symlink() or (part.exists() and getattr(part.lstat(), "st_file_attributes", 0) & 0x400):
            raise RecoveryError("The persistent recovery directory contains a link or reparse point.")
    return path


def atomic_json(path, value):
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False, sort_keys=True)
            stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _string_pool(data, offset):
    _, header, size = struct.unpack_from("<HHI", data, offset)
    if header < 28 or size < header or offset + size > len(data): raise ValueError("Invalid manifest string pool")
    count, _, flags, strings, _ = struct.unpack_from("<IIIII", data, offset + 8)
    if count > 32768 or header + count * 4 > size: raise ValueError("Invalid manifest string count")
    result = []
    def length(at, utf8):
        first = data[at] if utf8 else struct.unpack_from("<H", data, at)[0]
        step, mask = (1, 128) if utf8 else (2, 32768)
        if not first & mask: return first, at + step
        second = data[at + 1] if utf8 else struct.unpack_from("<H", data, at + 2)[0]
        return ((first & (mask - 1)) << (8 if utf8 else 16)) | second, at + 2 * step
    for index in range(count):
        at = offset + strings + struct.unpack_from("<I", data, offset + header + 4 * index)[0]
        if flags & 0x100:
            _, at = length(at, True); size_value, at = length(at, True)
            raw = data[at:at + size_value]; text = raw.decode("utf-8")
        else:
            size_value, at = length(at, False)
            raw = data[at:at + size_value * 2]; text = raw.decode("utf-16le")
        if at + len(raw) >= offset + size: raise ValueError("Truncated manifest string")
        result.append(text)
    return result


def replacement_is_debuggable(apk):
    """Read the real compiled Android manifest; never infer from a filename."""
    try:
        with zipfile.ZipFile(apk) as archive:
            item = archive.getinfo("AndroidManifest.xml")
            if item.file_size > 1024 * 1024: return False
            data = archive.read(item)
        kind, header, size = struct.unpack_from("<HHI", data)
        if kind != 3 or size != len(data) or header < 8: return False
        strings, package, debug, resources = None, None, False, None
        counts = {"manifest": 0, "application": 0, "debug": 0}
        offset = header
        while offset < size:
            kind, chunk_header, chunk_size = struct.unpack_from("<HHI", data, offset)
            if chunk_size < chunk_header or chunk_size < 8 or offset + chunk_size > size: return False
            if kind == 1:
                if strings is not None: return False
                strings = _string_pool(data, offset)
            if kind == 0x180:
                if resources is not None or chunk_header != 8 or (chunk_size - 8) % 4: return False
                resources = struct.unpack_from("<" + "I" * ((chunk_size - 8) // 4), data, offset + 8)
            if kind == 0x102:
                if strings is None or chunk_header != 16 or chunk_size < 36: return False
                _, name, start, attr_size, count = struct.unpack_from("<IIHHH", data, offset + 16)
                if attr_size != 20 or start < 20 or offset + 16 + start + count * attr_size > offset + chunk_size: return False
                element = strings[name]
                if element in ("manifest", "application"): counts[element] += 1
                for number in range(count):
                    at = offset + 16 + start + number * attr_size
                    namespace, attribute, raw, typed_size, _, typed_kind, value = struct.unpack_from("<IIIHBBI", data, at)
                    attr_name = strings[attribute]
                    if element == "manifest" and attr_name == "package" and namespace == 0xffffffff:
                        package = strings[raw] if raw != 0xffffffff else strings[value] if typed_kind == 3 else None
                    if (element == "application" and attr_name == "debuggable" and namespace != 0xffffffff
                            and strings[namespace] == "http://schemas.android.com/apk/res/android"
                            and typed_size == 8 and typed_kind == 0x12):
                        if resources is None or attribute >= len(resources) or resources[attribute] != 0x0101000f: return False
                        counts["debug"] += 1; debug = value != 0
            offset += chunk_size
        return package == PACKAGE and debug and counts == {"manifest": 1, "application": 1, "debug": 1}
    except (OSError, ValueError, IndexError, KeyError, struct.error, zipfile.BadZipFile): return False


def archive_inventory(path):
    """Validate every saved member before allowing tar to touch the new app UID.

    Wine prefixes contain terminal symlinks such as dosdevices/z: -> /. Preserve
    those literal links, but forbid all archive members beneath any link. Tar
    therefore never writes through them. Special files and hard links are refused.
    """
    result = {}; links = set()
    with tarfile.open(path, "r:") as archive:
        for member in archive:
            name = member.name
            while name.startswith("./"): name = name[2:]
            if name in ("", "."):
                if not member.isdir(): raise RecoveryError("Invalid backup root member.")
                continue
            if (len(name) > 4096 or name.startswith("/") or "\x00" in name or
                    any(part in ("", ".", "..") for part in name.split("/")) or name in result):
                raise RecoveryError("Unsafe or duplicate backup member.")
            if len(result) >= 100000: raise RecoveryError("Recovery backup has too many members.")
            if member.isdir(): value = {"type": "directory"}
            elif member.issym():
                if "\x00" in member.linkname or len(member.linkname) > 4096: raise RecoveryError("Invalid backup link.")
                value = {"type": "symlink", "target": member.linkname}; links.add(name)
            elif member.isfile():
                digest = hashlib.sha256()
                stream = archive.extractfile(member)
                for block in iter(lambda: stream.read(1024 * 1024), b""): digest.update(block)
                value = {"type": "file", "size": member.size, "sha256": digest.hexdigest()}
            else: raise RecoveryError("Recovery refuses a backup containing a special file or hard link.")
            result[name] = value
    for name in result:
        if any(str(parent) in links for parent in PurePosixPath(name).parents if str(parent) != "."):
            raise RecoveryError("Recovery refuses a backup member beneath a symbolic link.")
    return result


class Recovery:
    def __init__(self, adb, address, hardware, source, directory, *, install_timeout=1800, decode_receipt=None):
        if not re.fullmatch(r"[A-Za-z0-9_-]+", hardware): raise RecoveryError("Invalid headset identity.")
        self.adb, self.address, self.hardware, self.source = adb, address, hardware, source
        self.home = ordinary(Path(directory) / "signing-recovery" / hardware)
        self.home.mkdir(parents=True, exist_ok=True)
        self.path = ordinary(self.home / "active.json")
        self.journal = None; self.install_timeout = install_timeout
        self.decode_receipt = decode_receipt
        self._direct_storage_proved = False
        self._storage_process = None

    def _process_snapshot(self, pid, *, stopped=False):
        # starttime distinguishes recycled PIDs; UID/cmdline prevent following
        # another app's mount namespace. All tasks must be stopped before data
        # access, including Java's debugger-wait thread.
        prefix = "/proc/" + str(pid)
        script = "cat " + prefix + "/stat\ntr '\\000' '\\n' < " + prefix + "/cmdline | sed '/^$/d'\nstat -c %u " + prefix + "\nid -u"
        if stopped:
            script += "\nfor task in " + prefix + "/task/*; do state=$(sed -n 's/^State:[[:space:]]*\\([A-Za-z]\\).*/\\1/p' \"$task/status\"); case \"$state\" in T|t) ;; *) exit 53;; esac; done"
        rows = self._context_shell(script, "app").splitlines()
        if (len(rows) != 4 or rows[1] != PACKAGE or not rows[2].isdigit() or int(rows[2]) < 10000
                or rows[2] != rows[3] or not rows[0].startswith(str(pid) + " (") or ") " not in rows[0]):
            raise RecoveryError("The paused app storage process has a foreign or incomplete identity.")
        fields = rows[0].rsplit(") ", 1)[1].split()
        if len(fields) < 20 or not fields[19].isdigit() or (stopped and fields[0] not in ("T", "t")):
            raise RecoveryError("The app storage process is not completely paused.")
        return (str(pid), rows[2], fields[19])

    def _process_storage_root(self):
        if self._storage_process:
            self._device_guard()
            if self._process_snapshot(self._storage_process[0], stopped=True) != self._storage_process:
                raise RecoveryError("The paused storage process changed during recovery.")
            return "/proc/" + self._storage_process[0] + "/root/storage/emulated/0"
        self._device_guard()
        resolved = self.adb.run("-s", self.address, "shell", "cmd", "package", "resolve-activity", "--brief", "--user", "0",
                                "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", PACKAGE, check_text=False).strip().splitlines()
        if not resolved or not re.fullmatch(re.escape(PACKAGE) + r"/[A-Za-z_][A-Za-z0-9_.$]*|" + re.escape(PACKAGE) + r"/\.[A-Za-z_][A-Za-z0-9_.$]*", resolved[-1]):
            raise RecoveryError("The installed app's launcher cannot open its own storage view.")
        started_at = self.shell("date +%s").strip()
        if not started_at.isdigit(): raise RecoveryError("The app storage wait boundary could not be identified.")
        debug_rows = self.shell("dumpsys activity processes | sed -n '/mDebugApp=/p'").splitlines()
        debug_args = ["-D"]
        if debug_rows:
            debug = re.fullmatch(r"\s*mDebugApp=([A-Za-z0-9_.:]+|null)/orig=([A-Za-z0-9_.:]+|null) mDebugTransient=(true|false) mOrigWaitForDebugger=(true|false)\s*", debug_rows[0])
            if len(debug_rows) != 1 or len(debug_rows[0]) > 4096 or not debug:
                raise RecoveryError("The existing Android debug selection cannot be safely preserved.")
            if debug.group(1) == PACKAGE and debug.group(3) == "true":
                # A previous start can fail before attachApplication restores
                # mOrigDebugApp. Repeating -D would overwrite that saved choice.
                # Reuse its pending selection, then prove a fresh wait marker.
                debug_args = []
        # START_FLAG_DEBUG is transient: ActivityManager restores previous debug
        # settings when binding this process. ActivityThread waits before making
        # Application or installing providers. Observe that boundary before
        # pausing; subsequent complete snapshots still verify mutable app data.
        # Do not clear-debug-app: it would overwrite another developer setting.
        owned = None
        try:
            self.adb.run("-s", self.address, "shell", "am", "start", *debug_args, "--user", "0", "-n", shlex.quote(resolved[-1]), timeout=30)
            for attempt in range(40):
                raw = self.adb.run("-s", self.address, "shell", "pidof", PACKAGE, check_text=False).strip()
                if re.fullmatch(r"[1-9][0-9]{0,9}", raw):
                    owned = self._process_snapshot(raw)
                    logs = self.adb.run("-s", self.address, "shell", "logcat", "-d", "-t", "128", "--pid", raw,
                                        "-v", "epoch", "ActivityThread:I", "*:S", check_text=False)
                    waiting = any(re.match(r"\s*([0-9]+)\.[0-9]+\s", line) and
                                  int(re.match(r"\s*([0-9]+)\.[0-9]+\s", line).group(1)) >= int(started_at) and
                                  "Application " + PACKAGE + " is waiting for the debugger" in line for line in logs.splitlines())
                    if waiting:
                        if self._process_snapshot(raw) != owned: raise RecoveryError("The waiting app process changed before pausing.")
                        self._device_guard()
                        self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "kill", "-STOP", raw, check_text=False)
                        for pause_attempt in range(20):
                            try: paused = self._process_snapshot(raw, stopped=True)
                            except RuntimeError:
                                time.sleep(0.05); continue
                            if paused != owned: raise RecoveryError("The waiting app process changed while pausing.")
                            self._storage_process = paused
                            progress("access", 1, 1, "Using the paused app's own Android storage namespace", local=True)
                            return "/proc/" + raw + "/root/storage/emulated/0"
                        raise RecoveryError("The app storage process did not completely pause.")
                progress("access", attempt + 1, 40, "Opening the app's own storage view before gameplay starts", local=True)
                time.sleep(0.25)
            raise RecoveryError("The app did not prove its debugger-wait storage boundary in time.")
        except BaseException:
            try:
                self._device_guard()
                if owned:
                    if self._process_snapshot(owned[0]) == owned:
                        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE, check_text=False)
                else:
                    # The start request can reach Android even when ADB times
                    # out before returning a PID. The target was force-stopped
                    # before this scoped request; terminate only that package.
                    self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE, check_text=False)
            except RuntimeError as cleanup:
                print("Signing recovery storage cleanup could not be confirmed: " + str(cleanup)[:1024], flush=True)
            raise

    def _close_storage_process(self):
        owned = self._storage_process
        if not owned: return
        self._storage_process = None
        self._device_guard()
        # Stop only the process we started. A changed/reused PID is not a reason
        # to force-stop a newly launched app or any foreign process.
        try: current = self._process_snapshot(owned[0])
        except RuntimeError: return
        if current != owned: raise RecoveryError("The paused storage process changed before scoped cleanup.")
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE, check_text=False)

    def shell(self, script, *, timeout=60):
        return self.adb.run("-s", self.address, "shell", "-T", "sh", "-s",
                            input_text="set -eu\n" + script + "\n", timeout=timeout, check_text=False)

    def commit(self, state=None, **fields):
        if state: self.journal["state"] = state
        self.journal.update(fields)
        atomic_json(self.path, self.journal)

    def load(self):
        if not self.path.exists(): return False
        if self.path.stat().st_size > 32 * 1024 * 1024: raise RecoveryError("Recovery journal exceeds its bounded size.")
        value = json.loads(self.path.read_text(encoding="utf-8"))
        if (value.get("schema") != 1 or value.get("package") != PACKAGE or value.get("deviceSerial") != self.hardware
                or not re.fullmatch(r"[0-9a-f]{32}", str(value.get("transaction", "")))
                or value.get("state") not in ("created", "backed-up", "parking", "parked", "uninstalling", "removed", "installing", "restoring", "rolling-back", "rollback-restoring", "verified", "complete")):
            raise RecoveryError("Recovery journal has an invalid identity or state.")
        self.journal = value
        if value["state"] == "complete": return False
        if value.get("apkSha256") != self.source.sha256 or value.get("inputKey") != self.source.input_key:
            raise RecoveryError("Finish the pending recovery with its original selected APK before selecting another build.")
        self.backup = ordinary(self.home / value["transaction"])
        self.park = "/sdcard/Download/.ghvr-quest-recovery/" + self.hardware + "/" + value["transaction"]
        return True

    def _transfer(self, args, local, *, restore=False, phase="backup", total=None, paths=None, work=None):
        """Binary ADB transfer with bounded memory and observed byte counters."""
        command = [self.adb.executable, "-s", self.address, *map(str, args)]
        ordinary(local)
        if local.exists() and (not local.is_file() or local.stat().st_nlink != 1):
            raise RecoveryError("A recovery archive is not an ordinary independent file.")
        with self.adb.log.open("a", encoding="utf-8") as log:
            log.write(json.dumps({"binaryCommand": command, "backup": str(local), "restore": restore}) + "\n")
        stop = threading.Event(); transfer_error = None
        with local.open("rb" if restore else "wb") as stream, tempfile.TemporaryFile() as stderr, tempfile.TemporaryFile() as listing:
            if paths is not None:
                listing.write("".join(name + "\n" for name in paths).encode("utf-8")); listing.seek(0)
            def report():
                offset = members = 0
                while not stop.wait(1):
                    try: observed = stream.tell() if restore else local.stat().st_size
                    except OSError: continue
                    progress(phase, observed, total, "Restoring retained app data" if restore else "Saving retained app data", unit="bytes", local=True)
                    if work:
                        # Count only fully written real tar members; extension
                        # records are metadata, never additional planned work.
                        try:
                            with local.open("rb") as tail:
                                while observed >= offset + 512:
                                    tail.seek(offset); header = tail.read(512)
                                    if len(header) != 512 or header == b"\0" * 512: break
                                    try: length = tarfile.nti(header[124:136])
                                    except (ValueError, tarfile.InvalidHeaderError): break
                                    if length < 0: break
                                    end = offset + 512 + ((length + 511) // 512) * 512
                                    if end > observed: break
                                    offset = end
                                    if header[156:157] not in (b"x", b"g", b"L", b"K"): members += 1
                        except OSError:
                            # Windows antivirus/sharing checks can briefly deny
                            # the optional census reader. Byte progress remains
                            # valid; retry the same header on the next sample.
                            continue
                        progress("backup-work", work[0] + min(members, work[1]), work[2], work[3], local=True)
            monitor = threading.Thread(target=report, daemon=True); monitor.start()
            try:
                options = {"stderr": stderr, "timeout": 1800, "shell": False}
                if restore: options.update(stdin=stream, stdout=subprocess.DEVNULL)
                else: options.update(stdout=stream, stdin=listing if paths is not None else subprocess.DEVNULL)
                result = self.adb.runner(command, **options)
            except Exception as error:
                transfer_error = error
            finally:
                stop.set(); monitor.join()
            stderr.seek(0, 2); size = stderr.tell(); stderr.seek(max(0, size - 16384))
            message = stderr.read(16384).decode("utf-8", errors="replace")
        with self.adb.log.open("a", encoding="utf-8") as log: log.write(message + "\n")
        if transfer_error:
            if not restore: local.unlink(missing_ok=True)
            raise transfer_error
        if result.returncode:
            if not restore: local.unlink(missing_ok=True)
            raise RecoveryError("ADB data transfer failed: " + message[:1200])
        if not restore and re.search(r"\b(ignored|socket|changed|cannot|denied)\b", message, re.I):
            local.unlink(missing_ok=True)
            raise RecoveryError("Android did not capture every app-data entry: " + message[:1200])
        progress(phase, local.stat().st_size, total, "Retained app-data transfer finished", unit="bytes", local=True)

    def _tar_args(self, root, *, internal=False, restore=False, access_mode=None, explicit=False):
        actual_root = self._access_root(root, access_mode)
        command = ["shell", "-T"] + (["run-as", PACKAGE] if internal or access_mode in ("app", "app-direct", "app-process") else [])
        # Android toybox tar otherwise attempts to restore the old app UID.
        # -o keeps ownership with the new run-as UID after package replacement.
        command += ["tar", "-C", actual_root, "-xof" if restore else "-cf", "-"]
        if explicit and not restore: return command + ["--no-recursion", "-T", "-"]
        if not restore:
            if internal:
                command += ["--exclude=./cache", "--exclude=./code_cache"]
                if self.journal.get("excludePrivateLib") and root in ("/data/user/0/" + PACKAGE, "/data/data/" + PACKAGE): command += ["--exclude=./lib"]
            else: command += ["--exclude=./" + name for name in self.journal.get("banks", [])]
            command += ["."]
        return command

    def _check_users(self):
        current = self.adb.run("-s", self.address, "shell", "am", "get-current-user", check_text=False).strip()
        if current != "0": raise RecoveryError("Automatic signing recovery currently requires the primary Quest user; other profiles are retained.")
        users = self.adb.run("-s", self.address, "shell", "pm", "list", "users", check_text=False)
        ids = re.findall(r"UserInfo\{(\d+):", users)
        if not ids or "0" not in ids: raise RecoveryError("Android user ownership could not be verified.")
        for identifier in ids:
            if identifier == "0": continue
            owned = self.adb.run("-s", self.address, "shell", "pm", "list", "packages", "-u", "--user", identifier, PACKAGE, check_text=False)
            if "package:" + PACKAGE in owned: raise RecoveryError("The app also belongs to another Android profile; signing recovery will not remove that profile's app.")

    def _access_root(self, root, mode):
        if mode not in ("app-direct", "app-process"): return root
        if not (root == EXTERNAL or root.startswith(EXTERNAL + "/") or root == self.park or root.startswith(self.park + "/")):
            raise RecoveryError("The direct storage view is outside this app's recovery roots.")
        if mode == "app-process": return self._process_storage_root() + root[len("/sdcard"):]
        if not self._direct_storage_proved:
            self._device_guard()
            # /sdcard can traverse /mnt/user/0, whose shell-owned parent is not
            # accessible after run-as changes UID. The direct emulated view
            # avoids that alias without changing any permissions. Prove both
            # views designate the same directory before using it for app data.
            identities = self.shell("stat -L -c '%d:%i' /sdcard /storage/emulated/0").splitlines()
            if len(identities) != 2 or identities[0] != identities[1] or not re.fullmatch(r"[0-9]+:[0-9]+", identities[0]):
                raise RecoveryError("The direct external storage view does not match the primary Quest storage.")
            self._direct_storage_proved = True
        return "/storage/emulated/0" + root[len("/sdcard"):]

    def _context_shell(self, script, mode):
        if mode in ("app", "app-direct", "app-process"):
            if mode == "app-process": self._process_storage_root()
            return self.adb.run("-s", self.address, "shell", "-T", "run-as", PACKAGE, "sh", "-s",
                                input_text="set -eu\n" + script + "\n", check_text=False, timeout=120)
        return self.shell(script, timeout=120)

    def _check_external_root(self, root, mode):
        # The proc root is intentionally the kernel namespace bridge; every
        # storage component below it must be an ordinary directory. Absolute
        # symlinks would otherwise escape back into run-as's original namespace.
        boundary = "/sdcard" if root.startswith("/sdcard/") else "/storage"
        if root.startswith("/proc/"): boundary = root.split("/root/", 1)[0] + "/root"
        checked = []
        for parent in (PurePosixPath(root), *PurePosixPath(root).parents):
            value = str(parent)
            if value == boundary: break
            checked.append("test ! -L " + shlex.quote(value))
        self._context_shell("test -d " + shlex.quote(root) + "\n" + "\n".join(checked), mode)

    def _capture_census(self, item, *, parked_bank=None):
        """Prove readable typed members before streaming any payload bytes."""
        root = item["root"]; modes = ("app",) if item["internal"] else ("shell", "app", "app-direct", "app-process")
        ignored = set(); directories = set()
        if not item["internal"]:
            for bank in ([parked_bank] if parked_bank else self.journal.get("banks", [])):
                prefix = "" if parked_bank else bank + "/"
                if not parked_bank: directories.add(bank)
                for path in self.journal["bankWitnesses"][bank]:
                    ignored.add(prefix + path)
                    directories.update(prefix + str(parent) for parent in PurePosixPath(path).parents if str(parent) != ".")
                ignored.update(prefix + ".quest-installation.receipt" + suffix for suffix in ("", ".previous", ".pending"))
        errors = []
        for mode in modes:
            try:
                actual_root = self._access_root(root, mode)
                if not item["internal"]: self._check_external_root(actual_root, mode)
                observed = self._context_shell("find " + shlex.quote(actual_root) + " -mindepth 1 -print", mode).splitlines()
                names = []
                for path in observed:
                    if not path.startswith(actual_root + "/"): raise RecoveryError("The app-data census contains an unrepresentable or foreign path.")
                    name = path[len(actual_root) + 1:]
                    if (len(name) > 4096 or any(char in name for char in "\x00\r\n") or any(part in ("", ".", "..") for part in name.split("/"))):
                        raise RecoveryError("The app-data census contains an unsafe filename.")
                    if item["internal"] and (name.split("/", 1)[0] in ("cache", "code_cache") or
                            name.split("/", 1)[0] == "lib" and self.journal.get("excludePrivateLib")
                            and root in ("/data/user/0/" + PACKAGE, "/data/data/" + PACKAGE)): continue
                    if name not in ignored and name not in directories: names.append(name)
                if len(names) > 100000 or sum(len(name) + 3 for name in names) > 16 * 1024 * 1024: raise RecoveryError("The app-data census exceeds its bounded size.")
                rows = {}; failed = []
                for offset in range(0, len(names), 64):
                    batch = names[offset:offset + 64]; lines = []
                    for index, name in enumerate(batch):
                        path = shlex.quote(actual_root + "/" + name)
                        lines.append("metadata=$(stat -c '%f:%s' " + path + "); "
                                     + "if test -L " + path + "; then kind=L; elif test -d " + path + "; then kind=D; "
                                     + "elif test -f " + path + "; then if head -c 1 " + path + " >/dev/null 2>&1; then kind=F; else kind=U; fi; else kind=S; fi; "
                                     + "printf '%s\\t%s\\t%s\\n' '" + str(index) + "' \"$metadata\" \"$kind\"")
                    values = self._context_shell("\n".join(lines), mode).splitlines()
                    if len(values) != len(batch): raise RecoveryError("The app-data access census was incomplete.")
                    for index, (line, name) in enumerate(zip(values, batch)):
                        parts = line.split("\t")
                        if len(parts) != 3 or parts[0] != str(index) or not re.fullmatch(r"[0-9a-fA-F]+:[0-9]+", parts[1]): raise RecoveryError("Invalid app-data access metadata.")
                        kind = int(parts[1].split(":")[0], 16) & 0xf000
                        if parts[2] not in ("F", "D", "L", "U", "S") or (parts[2] != "S" and kind != {"F": 0x8000, "U": 0x8000, "D": 0x4000, "L": 0xa000}[parts[2]]):
                            raise RecoveryError("The app-data member type changed during its access census.")
                        if parts[2] in ("U", "S"): failed.append(name)
                        else: rows[name] = {"type": {"F": "file", "D": "directory", "L": "symlink"}[parts[2]], "size": int(parts[1].split(":")[1])}
                    progress("access", offset + len(batch), len(names), "Checking " + item["name"] + " app data using " + mode + " access", unit="files", local=True)
                if failed: raise RecoveryError("Cannot safely read " + str(len(failed)) + " " + item["name"] + " app-data entries using " + mode + " access; first: " + failed[0])
                # Explicit positive lists protect names containing wildcard or
                # option syntax. LF stdin avoids Windows command-length limits.
                result = {"accessMode": mode, "capturePaths": ["."] + ["./" + name for name in sorted(rows)],
                          "captureFiles": len(rows) + 1, "captureBytes": sum(value["size"] for value in rows.values() if value["type"] == "file")}
                item.update(result); return rows
            except RuntimeError as error:
                errors.append(mode + ": " + str(error)[:1200])
                self._device_guard()
        raise RecoveryError("Automatic app-data access repair exhausted shell and app identities: " + "; ".join(errors) + "; the installed app was retained.")

    def _capture_root(self, item, local, *, work=None, fresh=False, parked_bank=None):
        if fresh or "capturePaths" not in item: self._capture_census(item, parked_bank=parked_bank)
        self._transfer(self._tar_args(item["root"], internal=item["internal"], access_mode=item.get("accessMode"), explicit=True),
                       local, paths=item["capturePaths"], work=work)
        if item.get("accessMode") == "app-process": self._process_storage_root()
        inventory = archive_inventory(local)
        expected = {name[2:] for name in item["capturePaths"] if name != "."}
        if set(inventory) != expected: raise RecoveryError("The captured archive does not match its complete mutable app-data census.")
        return inventory

    def _bank_is_immutable(self, remote):
        if self.decode_receipt is None: return False
        local = self.backup / (remote.rsplit("/", 1)[1] + ".receipt")
        try:
            self.adb.run("-s", self.address, "pull", remote + "/.quest-installation.receipt", local, timeout=60, check_text=False)
            if not local.is_file() or local.stat().st_size > 16 * 1024 * 1024: return False
            receipt = self.decode_receipt(local.read_bytes())
            if not receipt or not isinstance(receipt.get("files"), dict): return False
            for name in receipt["files"]:
                if name.startswith("/") or any(part in ("", ".", "..") for part in name.split("/")): return False
            # Old test installs can leave additional native bundles or unknown
            # mutable files. Capture those separately; they must not force the
            # complete multi-GB receipt-owned bank into the host backup.
            # A path covered by a receipt is not immutable when it is a link to
            # arbitrary mutable data. Require regular files and ordinary dirs.
            checks = []
            parents = set()
            for name in receipt["files"]:
                checks += ["test -f " + shlex.quote(remote + "/" + name), "test ! -L " + shlex.quote(remote + "/" + name)]
                parents.update(str(parent) for parent in PurePosixPath(name).parents if str(parent) != ".")
            for parent in sorted(parents): checks += ["test -d " + shlex.quote(remote + "/" + parent), "test ! -L " + shlex.quote(remote + "/" + parent)]
            for suffix in ("", ".previous", ".pending"):
                receipt_path = shlex.quote(remote + "/.quest-installation.receipt" + suffix)
                checks.append("test ! -L " + receipt_path)
                checks.append("if test -e " + receipt_path + "; then test -f " + receipt_path + "; fi")
            for offset in range(0, len(checks), 128): self.shell("\n".join(checks[offset:offset + 128]))
            self.journal.setdefault("bankWitnesses", {})[remote[len(EXTERNAL) + 1:]] = receipt["files"]
            return True
        except (OSError, ValueError, RuntimeError): return False

    def _safe_restore_archive(self, origin, destination, item):
        ordinary(destination)
        actual_root = self._access_root(item["root"], item.get("accessMode"))
        if not item["internal"]: self._check_external_root(actual_root, item.get("accessMode", "shell"))
        if destination.exists() and (not destination.is_file() or destination.stat().st_nlink != 1): raise RecoveryError("Unsafe restoration archive.")
        # Previously restored terminal links remain legal, but no data write may
        # traverse any existing link. Validate each original non-link path and
        # its ancestors in bounded scripts; quote every original filename.
        checked = set()
        for name, value in item["inventory"].items():
            parents = [str(parent) for parent in PurePosixPath(name).parents if str(parent) != "."]
            if value["type"] != "symlink": parents.append(name)
            checked.update(actual_root + "/" + parent for parent in parents)
        paths = sorted(checked)
        for offset in range(0, len(paths), 128):
            script = "set -eu\n" + "\n".join("test ! -L " + shlex.quote(path) for path in paths[offset:offset + 128]) + "\n"
            self._context_shell(script, "app" if item["internal"] else item.get("accessMode", "shell"))
        # Android toybox tar refuses existing links instead of overwriting them.
        # Remove only original terminal links after verifying their exact target.
        links_to_remove = [(actual_root + "/" + name, value["target"]) for name, value in item["inventory"].items() if value["type"] == "symlink"]
        for offset in range(0, len(links_to_remove), 64):
            lines = []
            for path, target in links_to_remove[offset:offset + 64]:
                quoted = shlex.quote(path)
                lines.append("if test -L " + quoted + "; then test \"$(readlink " + quoted + ")\" = " + shlex.quote(target)
                             + "; rm " + quoted + "; else test ! -e " + quoted + "; fi")
            self._context_shell("\n".join(lines), "app" if item["internal"] else item.get("accessMode", "shell"))
        links = []
        with tarfile.open(origin, "r:") as source, tarfile.open(destination, "w:") as output:
            for member in source:
                if member.issym(): links.append(member)
                else: output.addfile(member, source.extractfile(member) if member.isfile() else None)
            for member in links: output.addfile(member)
        if archive_inventory(destination) != item["inventory"]: raise RecoveryError("Restoration archive differs from its verified original.")

    def _verify_bank_copy(self, name, root):
        rows = self.journal.get("bankWitnesses", {}).get(name)
        if not isinstance(rows, dict) or not rows: raise RecoveryError("Retained immutable-bank witness is missing.")
        entries = list(rows.items())
        for offset in range(0, len(entries), 32):
            batch = entries[offset:offset + 32]
            script = "\n".join("sha256sum " + shlex.quote(root + "/" + path) for path, _ in batch)
            raw = self.shell(script, timeout=600).splitlines()
            if len(raw) != len(batch): raise RecoveryError("Parked content verification did not return every required file.")
            for line, (path, value) in zip(raw, batch):
                if line != value["sha256"] + "  " + root + "/" + path: raise RecoveryError("A parked content bank failed byte verification.")
            progress("backup", offset + len(batch), len(entries), "Verifying retained " + name.split("/")[-1] + " content", unit="files", local=True)

    def _copy_bank(self, name, source, target, *, phase="backup"):
        rows = self.journal["bankWitnesses"][name]
        total = sum(row["size"] for row in rows.values()); done = 0
        for index, (path, row) in enumerate(rows.items()):
            if index % 32 == 0: self._device_guard()
            destination = target + "/" + path
            temporary = destination + ".quest-signing-copy-" + self.journal["transaction"]
            quoted = shlex.quote(destination)
            observed = self.shell("if test -f " + quoted + " && ! test -L " + quoted + "; then sha256sum " + quoted + "; fi", timeout=600)
            if observed != row["sha256"] + "  " + destination:
                self.shell("mkdir -p " + shlex.quote(str(PurePosixPath(destination).parent)) + "\ntest ! -L " + quoted
                           + "\ntest ! -L " + shlex.quote(temporary))
                stopped = threading.Event()
                def report():
                    while not stopped.wait(1):
                        try:
                            value = self.shell("if test -f " + shlex.quote(temporary) + "; then stat -c %s " + shlex.quote(temporary) + "; fi", timeout=20)
                            if value.isdigit(): progress(phase, done + min(int(value), row["size"]), total, "Copying retained " + name.split("/")[-1] + " · " + path, unit="bytes", local=True)
                        except RuntimeError: pass
                monitor = threading.Thread(target=report, daemon=True); monitor.start()
                try:
                    self.shell("cp -p " + shlex.quote(source + "/" + path) + " " + shlex.quote(temporary), timeout=1800)
                finally: stopped.set(); monitor.join()
                actual = self.shell("sha256sum " + shlex.quote(temporary), timeout=600)
                if actual != row["sha256"] + "  " + temporary: raise RecoveryError("A retained immutable-content copy failed byte verification.")
                self.shell("mv -f " + shlex.quote(temporary) + " " + quoted)
            done += row["size"]
            progress(phase, done, total, "Retained " + name.split("/")[-1] + " · " + path, unit="bytes", local=True)
        # Small completion/checkpoint receipts are part of the old native loader
        # contract and must survive rollback together with the exact old banks.
        for suffix in ("", ".previous", ".pending"):
            origin, destination = source + "/.quest-installation.receipt" + suffix, target + "/.quest-installation.receipt" + suffix
            self.shell("if test -f " + shlex.quote(origin) + " && ! test -L " + shlex.quote(origin) + "; then cp -p " + shlex.quote(origin) + " " + shlex.quote(destination) + "; fi")

    def _refresh_before_uninstall(self):
        self._close_storage_process()
        self._device_guard()
        # Backing up a large installed APK can take minutes. The selected local
        # replacement must still be the verified file before removing the old app.
        if file_hash(self.source.apk) != self.source.sha256 or not replacement_is_debuggable(self.source.apk):
            raise RecoveryError("The selected APK changed during recovery preparation; the installed app was retained.")
        self._check_users()
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
        installed = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False).splitlines()
        if not installed: return False
        if len(installed) != 1 or not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", installed[0]): raise RecoveryError("Installed package changed since its recovery backup.")
        raw = self.shell("sha256sum " + shlex.quote(installed[0][8:]))
        if not raw.startswith(self.journal["originalApk"]["sha256"] + "  "): raise RecoveryError("The installed APK changed since its recovery backup; it was retained.")
        for index, item in enumerate(self.journal["roots"]):
            self._capture_census(item)
            candidate = self.backup / (item["name"] + "-" + uuid.uuid4().hex + ".tar")
            inventory = self._capture_root(item, candidate)
            if inventory == item["inventory"]:
                candidate.unlink(); continue
            verify = self.backup / (item["name"] + ".refresh-verify.tar")
            try:
                if self._capture_root(item, verify, fresh=True) != inventory: raise RecoveryError("App data continues changing after force-stop; the installed app was retained.")
            finally: verify.unlink(missing_ok=True)
            item.update(archive=candidate.name, inventory=inventory, sha256=file_hash(candidate))
            self.commit(roots=self.journal["roots"])
            progress("backup", index + 1, len(self.journal["roots"]), "Updated backup retains changes made since the previous attempt", unit="files", local=True)
        return True

    def _verify_prepared_snapshot(self):
        """Verify mutable data at its actual location after content parking."""
        self._device_guard()
        self._close_storage_process()
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
        self._installed_apk_guard(self.journal["originalApk"]["sha256"])
        for item in self.journal["roots"]:
            candidate = dict(item)
            temporary = self.backup / (item["name"] + ".boundary-verify.tar")
            try:
                inventory = self._capture_root(candidate, temporary, fresh=True)
            finally: temporary.unlink(missing_ok=True)
            if item["name"] == "external" and self.journal.get("parkingMode") == "atomic":
                # Unknown extras moved with their old bank. Capture only those
                # extras, then compare the combined census with the snapshot.
                for bank in self.journal["banks"]:
                    parked = {"name": "parked " + bank.split("/")[-1], "root": self.park + "/" + bank.split("/")[-1], "internal": False}
                    temporary = self.backup / (bank.split("/")[-1] + ".boundary-verify.tar")
                    try:
                        extra = self._capture_root(parked, temporary, fresh=True, parked_bank=bank)
                        for name, value in extra.items():
                            full = bank + "/" + name
                            if full in inventory: raise RecoveryError("Parked app-data roots overlap; the installed app was retained.")
                            inventory[full] = value
                    finally: temporary.unlink(missing_ok=True)
            if inventory != item["inventory"]:
                raise RecoveryError("App data changed during content parking; the installed app and original snapshots were retained for a targeted retry.")
        # Copy fallback can take minutes. Recheck the replacement and profile
        # boundaries after that work, immediately before permitting uninstall.
        if file_hash(self.source.apk) != self.source.sha256 or not replacement_is_debuggable(self.source.apk):
            raise RecoveryError("The selected APK changed during content parking; the installed app was retained.")
        self._check_users(); self._device_guard()

    def _device_guard(self):
        if self.adb.identity(self.address) != self.hardware: raise RecoveryError("The ADB address now belongs to a different headset; no foreign app data will be changed.")

    def _installed_apk_guard(self, expected):
        self._device_guard()
        paths = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False).splitlines()
        if len(paths) != 1 or not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", paths[0]): raise RecoveryError("The package selected for restoration cannot be identified.")
        if not self.shell("sha256sum " + shlex.quote(paths[0][8:])).startswith(expected + "  "): raise RecoveryError("The installed APK changed before data restoration; the unexpected app was retained.")

    def _backup(self):
        self._device_guard()
        progress("backup", 0, 7, "Checking whether existing saves and settings can be retained")
        self._check_users()
        if not replacement_is_debuggable(self.source.apk): raise RecoveryError("The selected APK does not prove run-as restoration support; the installed app was retained.")
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
        private = self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "pwd", check_text=False).strip()
        if private not in ("/data/user/0/" + PACKAGE, "/data/data/" + PACKAGE):
            raise RecoveryError("Existing app data is not accessible through Android run-as; the installed app was retained.")
        paths = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False).splitlines()
        if len(paths) != 1 or not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", paths[0]):
            raise RecoveryError("The original installed APK cannot be retained as one verified rollback package.")
        installed_apk = paths[0][8:]
        raw = self.shell("sha256sum " + shlex.quote(installed_apk) + "\nstat -c %s " + shlex.quote(installed_apk))
        lines = raw.splitlines()
        if len(lines) != 2 or not re.fullmatch(r"[0-9a-f]{64}  .*", lines[0]) or not lines[1].isdigit():
            raise RecoveryError("The installed APK's recovery identity could not be verified.")
        original_hash, original_size = lines[0][:64], int(lines[1])
        retained_apk = self.journal.get("originalApk", {})
        if retained_apk and (retained_apk.get("sha256") != original_hash or retained_apk.get("size") != original_size):
            raise RecoveryError("The installed APK changed since partial recovery preparation; its existing app data was retained.")
        previous = {item.get("name"): dict(item) for item in self.journal.get("roots", []) if isinstance(item, dict)}
        # Backup the entire mutable app-specific external root, not an allowlist
        # of known save names. Unknown future save/config files remain protected.
        external_state = self.shell("if test -d " + shlex.quote(EXTERNAL) + "; then printf present; else printf absent; fi\n"
                           + "if test -d /sdcard/Android/obb/" + PACKAGE + "; then if test -n \"$(ls -A /sdcard/Android/obb/" + PACKAGE + ")\"; "
                           + "then printf '%s\\n' 'Additional app OBB data is not yet supported by automatic signing recovery; the installed app was retained.' >&2; exit 52; fi; fi")
        if external_state not in ("present", "absent"): raise RecoveryError("The external app-data root could not be identified.")
        external_exists = external_state == "present"
        # The typed census supplies exact mutable sizes without repeated du scans.
        banks = []
        for name in BANKS:
            remote = EXTERNAL + "/" + name
            exists = self.shell("if test -d " + shlex.quote(remote) + " && ! test -L " + shlex.quote(remote) + "; then printf present; fi")
            if exists:
                if exists != "present": raise RecoveryError("A retained content-bank root could not be verified.")
                # Receipt-owned regular files are parked on the headset.
                # Unknown descendants still belong to the mutable host backup.
                if self._bank_is_immutable(remote): banks.append(name)
        roots = [{"name": "private", "root": private, "internal": True}]
        protected = "/data/user_de/0/" + PACKAGE
        device = self.adb.run("-s", self.address, "shell", "-T", "run-as", PACKAGE, "sh", "-s",
                              input_text="set -eu\nif test -d " + shlex.quote(protected) + "; then test -r " + shlex.quote(protected)
                              + " && test -x " + shlex.quote(protected) + " && printf present; "
                              + "elif ls -ld /data/user_de/0 >/dev/null 2>&1; then printf absent; "
                              + "else printf '%s\\n' 'Device-protected app-data access was denied; the installed app was retained.' >&2; exit 51; fi\n", check_text=False)
        if device not in ("present", "absent"): raise RecoveryError("Device-protected app-data access could not be verified.")
        if device == "present": roots.append({"name": "device-protected", "root": protected, "internal": True})
        if external_exists: roots.append({"name": "external", "root": EXTERNAL, "internal": False})
        lib = self.adb.run("-s", self.address, "shell", "-T", "run-as", PACKAGE, "sh", "-s",
                           input_text="set -eu\nif test -L " + shlex.quote(private + "/lib") + "; then readlink " + shlex.quote(private + "/lib") + "; fi\n", check_text=False)
        generated_lib = lib.startswith(str(PurePosixPath(installed_apk).parent) + "/lib/")
        for item in roots:
            retained = previous.get(item["name"], {})
            if retained.get("root") == item["root"] and retained.get("internal") == item["internal"]:
                item.update({key: retained[key] for key in ("archive", "sha256", "inventory") if key in retained})
        # Persist prior verified references before a new access check can fail.
        self.commit(originalApk={"path": "original.apk", "sha256": original_hash, "size": original_size}, roots=roots, banks=banks, excludePrivateLib=generated_lib)
        for item in roots: self._capture_census(item)
        self.commit(roots=roots)
        # Delete only failed/unreferenced archives owned by this still-installed
        # original APK transaction. Successful CE/DE/private snapshots stay valid.
        referenced = {item.get("archive") for item in roots}
        incomplete = {item["name"] for item in roots if not item.get("archive")}
        for local in self.backup.iterdir():
            if (local.name not in referenced and any(re.fullmatch(re.escape(name) + r"-[0-9a-f]{32}\.tar", local.name) for name in incomplete)
                    and not local.is_symlink() and local.is_file() and local.stat().st_nlink == 1):
                ordinary(local).unlink()
        mutable_sizes = [item["captureBytes"] + item["captureFiles"] * 4096 for item in roots]
        original = self.backup / "original.apk"
        original_retained = original.is_file() and original.stat().st_size == original_size and file_hash(original) == original_hash
        required = (0 if original_retained else original_size) + sum(mutable_sizes) + max(mutable_sizes) * 2 + 64 * 1024 * 1024
        if shutil.disk_usage(self.backup).free < required:
            raise RecoveryError("Insufficient PC storage for a verified rollback backup (required " + str(required) + " bytes); the installed app was retained.")
        work_total = 2 + sum(item["captureFiles"] * 2 + 1 for item in roots); work_done = 0
        self.commit(backupWork={"done": 0, "total": work_total})
        original = self.backup / "original.apk"
        if not original_retained:
            self._transfer(["exec-out", "cat", installed_apk], original, total=original_size)
        progress("backup-work", 1, work_total, "Original APK rollback copy retained", local=True)
        if original.stat().st_size != original_size or file_hash(original) != original_hash:
            raise RecoveryError("The original APK backup failed verification; the installed app was retained.")
        work_done = 2; progress("backup-work", work_done, work_total, "Original APK rollback copy verified", local=True)
        for index, item in enumerate(roots):
            old = item.get("archive")
            if (isinstance(old, str) and re.fullmatch(re.escape(item["name"]) + r"-[0-9a-f]{32}\.tar", old)
                    and ordinary(self.backup / old).is_file() and file_hash(self.backup / old) == item.get("sha256")
                    and archive_inventory(self.backup / old) == item.get("inventory")
                    and set(item["inventory"]) == {path[2:] for path in item["capturePaths"] if path != "."}):
                work_done += item["captureFiles"] * 2 + 1
                self.commit(backupWork={"done": work_done, "total": work_total})
                progress("backup-work", work_done, work_total, "Reusing verified " + item["name"] + " app-data backup", local=True)
                continue
            local = self.backup / (item["name"] + "-" + uuid.uuid4().hex + ".tar")
            inventory = self._capture_root(item, local, work=(work_done, item["captureFiles"], work_total, "Saving " + item["name"] + " app data"))
            work_done += item["captureFiles"]
            progress("backup-work", work_done, work_total, "Saved " + item["name"] + " app-data members", local=True)
            verify = self.backup / (item["name"] + ".verify.tar")
            try:
                if self._capture_root(item, verify, fresh=True, work=(work_done, item["captureFiles"], work_total, "Verifying " + item["name"] + " app data")) != inventory: raise RecoveryError("App data changed during its backup; the installed app was retained.")
            finally: verify.unlink(missing_ok=True)
            item.update(archive=local.name, sha256=file_hash(local), inventory=inventory)
            work_done += item["captureFiles"] + 1
            self.commit(roots=roots, backupWork={"done": work_done, "total": work_total})
            progress("backup-work", work_done, work_total, "Verified " + item["name"] + " app-data backup committed", local=True)
            progress("backup", index + 1, len(roots), "Verified retained " + item["name"] + " app data", unit="files", local=True)
        self.commit("backed-up")
        progress("backup", 2, 7, "Original APK, saves and settings are safely retained")

    def _verify_backups(self, *, original=True, validate_only=False):
        apk = self.journal["originalApk"]
        if apk.get("path") != "original.apk" or not re.fullmatch(r"[0-9a-f]{64}", str(apk.get("sha256"))): raise RecoveryError("Invalid original APK backup identity.")
        local = ordinary(self.backup / "original.apk")
        if original and not validate_only and (local.stat().st_size != apk["size"] or file_hash(local) != apk["sha256"]): raise RecoveryError("The retained original APK backup is damaged.")
        roots = self.journal.get("roots")
        if not isinstance(roots, list) or not 1 <= len(roots) <= 3: raise RecoveryError("Invalid app-data recovery roots.")
        allowed = {"private": (self.journal.get("roots", [{}])[0].get("root"), True), "device-protected": ("/data/user_de/0/" + PACKAGE, True), "external": (EXTERNAL, False)}
        if roots[0].get("name") != "private" or roots[0].get("root") not in ("/data/user/0/" + PACKAGE, "/data/data/" + PACKAGE): raise RecoveryError("Invalid private recovery root.")
        seen = set()
        for item in roots:
            name = item.get("name")
            if (name not in allowed or name in seen or (item.get("root"), item.get("internal")) != allowed[name]
                    or not re.fullmatch(re.escape(name) + r"-[0-9a-f]{32}\.tar", str(item.get("archive")))
                    or not re.fullmatch(r"[0-9a-f]{64}", str(item.get("sha256")))):
                raise RecoveryError("Invalid retained app-data archive identity.")
            seen.add(name); local = ordinary(self.backup / item["archive"])
            if "accessMode" in item and item["accessMode"] not in ("app", "app-direct", "app-process", "shell"): raise RecoveryError("Invalid retained app-data access identity.")
            if item.get("accessMode") in ("app-direct", "app-process") and item["internal"]: raise RecoveryError("Private app data cannot use the external storage view.")
            if item.get("accessMode") == "shell" and item["internal"]: raise RecoveryError("Private app data cannot be restored through shell ownership.")
            if "capturePaths" in item:
                paths = item["capturePaths"]
                if (not isinstance(paths, list) or not 1 <= len(paths) <= 100001 or paths[0] != "." or any(not isinstance(path, str) for path in paths) or len(set(paths)) != len(paths)
                        or type(item.get("captureFiles")) is not int or item["captureFiles"] != len(paths)
                        or type(item.get("captureBytes")) is not int or item["captureBytes"] < 0
                        or any(not isinstance(path, str) or len(path) > 4098 or not path.startswith("./") or any(char in path for char in "\x00\r\n")
                               or any(part in ("", ".", "..") for part in path[2:].split("/")) for path in paths[1:])):
                    raise RecoveryError("Invalid retained app-data positive census.")
            if not validate_only and (file_hash(local) != item["sha256"] or archive_inventory(local) != item["inventory"]): raise RecoveryError("A retained app-data backup is damaged.")
        if any(name not in BANKS for name in self.journal.get("banks", [])): raise RecoveryError("Invalid parked-bank identity.")
        banks = self.journal.get("banks", [])
        if len(banks) != len(set(banks)): raise RecoveryError("Duplicate parked-bank identity.")
        witnesses = self.journal.get("bankWitnesses", {})
        if not isinstance(witnesses, dict) or not set(banks).issubset(witnesses): raise RecoveryError("Invalid parked-bank witness.")
        for name in banks:
            rows = witnesses[name]
            if not isinstance(rows, dict) or not 0 < len(rows) <= 32768: raise RecoveryError("Invalid parked-bank file census.")
            for path, row in rows.items():
                if (not isinstance(path, str) or not path.startswith("StreamingAssets/") or len(path) > 8192
                        or any(char in path for char in "\\:\x00\r\n\t") or any(part in ("", ".", "..") for part in path.split("/"))
                        or not isinstance(row, dict) or row.get("path") != path or type(row.get("size")) is not int or row["size"] < 0
                        or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))):
                    raise RecoveryError("Invalid parked-bank file identity.")

    def _park_banks(self):
        self._device_guard()
        self.commit("parking")
        if not self.journal["banks"]:
            self.commit("parked", parkingMode="none"); return
        self.shell("mkdir -p " + shlex.quote(self.park) + "\ntest ! -L " + shlex.quote(self.park))
        # Probe the actual two-way move and permissions before touching game data.
        probe = EXTERNAL + "/files/.quest-signing-probe-" + self.journal["transaction"]
        result = self.shell("mkdir -p " + shlex.quote(probe) + "\nprintf probe > " + shlex.quote(probe + "/marker")
                   + "\nbefore=$(stat -c %d:%i " + shlex.quote(probe) + ")\nmv " + shlex.quote(probe) + " " + shlex.quote(self.park + "/probe")
                   + "\nafter=$(stat -c %d:%i " + shlex.quote(self.park + "/probe") + ")\nmv " + shlex.quote(self.park + "/probe") + " " + shlex.quote(probe)
                   + "\nback=$(stat -c %d:%i " + shlex.quote(probe) + ")\nrm " + shlex.quote(probe + "/marker")
                   + "\nrmdir " + shlex.quote(probe) + "\nif test \"$before\" = \"$after\" && test \"$before\" = \"$back\"; then printf atomic; else printf copied; fi")
        if result not in ("atomic", "copied"): raise RecoveryError("Android content-bank parking capability could not be verified.")
        mode = "atomic" if result == "atomic" else "copy"
        self.commit(parkingMode=mode)
        if mode == "copy":
            total = sum(value["size"] for rows in self.journal["bankWitnesses"].values() for value in rows.values())
            free = self.shell("df -k " + shlex.quote(self.park))
            lines = free.splitlines(); fields = lines[-1].split() if lines else []
            if len(fields) < 4 or not fields[3].isdigit() or int(fields[3]) * 1024 < total + 64 * 1024 * 1024:
                raise RecoveryError("Android requires a temporary content copy, but the Quest has insufficient free storage; the installed app was retained.")
        for name in self.journal["banks"]:
            self._device_guard()
            source, target = EXTERNAL + "/" + name, self.park + "/" + name.split("/")[-1]
            self.commit(movingBank=name)
            if mode == "atomic":
                self.shell("test ! -L " + shlex.quote(source) + "\ntest ! -L " + shlex.quote(target) + "\n"
                           + "if test -d " + shlex.quote(target) + "; then test ! -e " + shlex.quote(source) + "; else "
                           + "test -d " + shlex.quote(source) + "; before=$(stat -c %i " + shlex.quote(source) + "); mv " + shlex.quote(source) + " " + shlex.quote(target)
                           + "; test \"$before\" = \"$(stat -c %i " + shlex.quote(target) + ")\"; fi\n"
                           + "test -d " + shlex.quote(target) + "\ntest ! -e " + shlex.quote(source))
            else:
                self.shell("test ! -L " + shlex.quote(source) + "\ntest ! -L " + shlex.quote(target) + "\nmkdir -p " + shlex.quote(target))
                self._copy_bank(name, source, target)
                self._verify_bank_copy(name, target)
        self.commit("parked", movingBank=None)
        progress("replace", 3, 7, "Large game banks retained on the headset")

    def _restore_banks(self):
        self._device_guard()
        if self.journal.get("parkingMode") == "none": return
        for name in self.journal["banks"]:
            self._device_guard()
            target, source = EXTERNAL + "/" + name, self.park + "/" + name.split("/")[-1]
            self.commit(restoringBank=name)
            if self.journal.get("parkingMode") == "copy":
                # Copy remains as durable device recovery material until a later
                # explicit cleanup; interruption can safely replay this restore.
                self.shell("mkdir -p " + shlex.quote(target) + "\ntest ! -L " + shlex.quote(source) + "\ntest ! -L " + shlex.quote(target))
                self._copy_bank(name, source, target, phase="restore")
                self._verify_bank_copy(name, target)
                continue
            self.shell("mkdir -p " + shlex.quote(EXTERNAL + "/files") + "\ntest ! -L " + shlex.quote(source)
                       + "\ntest ! -L " + shlex.quote(target) + "\n"
                       + "if test -d " + shlex.quote(source) + "; then test ! -e " + shlex.quote(target) + "; "
                       + "mv " + shlex.quote(source) + " " + shlex.quote(target) + "; else test -d " + shlex.quote(target) + "; fi")
        self.commit(restoringBank=None)

    def _restore(self, *, expected_apk=None):
        self._installed_apk_guard(expected_apk or self.source.sha256)
        self._close_storage_process()
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
        private = self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "pwd", check_text=False).strip()
        if private not in ("/data/user/0/" + PACKAGE, "/data/data/" + PACKAGE): raise RecoveryError("The installed replacement does not permit run-as restoration; all original backups remain retained.")
        # Unknown extras captured inside a native bank must overlay its retained
        # original tree, rather than precreating a conflicting bank directory.
        self._restore_banks()
        for index, item in enumerate(self.journal["roots"]):
            local = self.backup / item["archive"]
            if item["internal"]:
                self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "mkdir", "-p", item["root"], check_text=False)
            else: self._context_shell("mkdir -p " + shlex.quote(self._access_root(item["root"], item.get("accessMode"))), item.get("accessMode", "shell"))
            self.commit(restoringArchive=item["archive"])
            restore = self.backup / (item["name"] + ".restore.tar")
            self._safe_restore_archive(local, restore, item)
            try:
                self._transfer(self._tar_args(item["root"], internal=item["internal"], restore=True, access_mode=item.get("accessMode")), restore, restore=True, phase="restore", total=restore.stat().st_size)
                if item.get("accessMode") == "app-process": self._process_storage_root()
            finally: restore.unlink(missing_ok=True)
            progress("restore", index + 1, len(self.journal["roots"]), "Restored " + item["name"] + " app data", unit="files", local=True)
        self.commit(restoringArchive=None)
        progress("restore", 5, 7, "Retained saves, settings and content restored")
        for item in self.journal["roots"]:
            verify = self.backup / (item["name"] + ".restored.tar")
            try:
                restored = self._capture_root(item, verify, fresh=True)
                original = item["inventory"]
                if any(restored.get(name) != value for name, value in original.items()): raise RecoveryError("Restored app data failed verification; the app was not launched and original backups remain retained.")
                for name in set(restored) - set(original):
                    value = restored[name]
                    if item["internal"] and name in ("files", "no_backup", "shared_prefs", "databases") and value == {"type": "directory"}: continue
                    if item["internal"] and name == "lib" and value.get("type") == "symlink" and value.get("target", "").startswith("/data/app/") and "/lib/" in value["target"]: continue
                    raise RecoveryError("Restored app data contains an unexpected new member; original backups remain retained.")
            finally: verify.unlink(missing_ok=True)
        progress("verify", 6, 7, "Restored saves and settings match their original backups")

    def recover(self, *, detected=False):
        try:
            pending = self.load()
            if not pending and not detected: return None
            if not pending:
                identifier = uuid.uuid4().hex
                self.backup = ordinary(self.home / identifier); self.backup.mkdir(mode=0o700)
                self.park = "/sdcard/Download/.ghvr-quest-recovery/" + self.hardware + "/" + identifier
                self.journal = {"schema": 1, "package": PACKAGE, "deviceSerial": self.hardware, "transaction": identifier,
                                "apkSha256": self.source.sha256, "inputKey": self.source.input_key, "state": "created"}
                self.commit()
            print("Retained signing migration backup: " + str(self.backup), flush=True)
            if self.journal["state"] == "created": self._backup()
            self._verify_backups(original=self.journal["state"] != "restoring", validate_only=self.journal["state"] == "verified")
            prepared = False
            if self.journal["state"] in ("backed-up", "parking"):
                try:
                    if self.journal["state"] == "parking":
                        self._installed_apk_guard(self.journal["originalApk"]["sha256"])
                        self._restore_banks()
                    self._refresh_before_uninstall()
                    self._park_banks(); prepared = True
                except Exception:
                    # A denied second bank move must not leave the still-installed
                    # old app without its first bank. Original data remains durable.
                    self._restore_banks(); self.commit("backed-up"); raise
            if self.journal["state"] == "parked":
                self.commit("uninstalling")
            if self.journal["state"] == "uninstalling":
                installed = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False)
                if installed.startswith("package:"):
                    self._installed_apk_guard(self.journal["originalApk"]["sha256"])
                    try:
                        # A resumed preparation may contain newly written saves.
                        # Put atomic banks back for the refreshed complete census.
                        if not prepared:
                            if self.journal.get("parkingMode") == "atomic": self._restore_banks()
                            self._refresh_before_uninstall()
                            self._park_banks(); self.commit("uninstalling")
                        self._verify_prepared_snapshot()
                    except Exception:
                        self._restore_banks(); self.commit("backed-up"); raise
                    scratch = max((self.backup / item["archive"]).stat().st_size for item in self.journal["roots"]) + 64 * 1024 * 1024
                    if shutil.disk_usage(self.backup).free < scratch:
                        raise RecoveryError("Insufficient PC storage for restoration scratch; the installed app was retained.")
                    self._close_storage_process()
                    self.adb.run("-s", self.address, "uninstall", PACKAGE, timeout=120, positive=r"^Success\s*$")
                self.commit("removed")
            if self.journal["state"] in ("removed", "installing"):
                self._device_guard()
                self.commit("installing")
                try:
                    self.adb.run("-s", self.address, "install", "-r", self.source.apk, timeout=self.install_timeout, positive=r"^Success\s*$")
                except Exception:
                    # Roll back to the exact original signed APK when Android has
                    # definitely left no replacement installed. Ambiguous states
                    # remain pending; no launch or success receipt is permitted.
                    installed = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False)
                    if not installed.startswith("package:"):
                        self.commit("rolling-back")
                        self.adb.run("-s", self.address, "install", self.backup / "original.apk", timeout=self.install_timeout, positive=r"^Success\s*$")
                        self.commit("rollback-restoring")
                        self._restore(expected_apk=self.journal["originalApk"]["sha256"])
                        self._close_storage_process(); self.commit("backed-up", rolledBack=True)
                    raise
                self.commit("restoring")
                progress("replace", 4, 7, "Replacement APK installed; restoring retained data before launch")
            if self.journal["state"] == "restoring":
                self._restore(); self._close_storage_process(); self.commit("verified")
            elif pending and self.journal["state"] == "verified":
                # A host terminated after durable verification may have left
                # a paused Android process. A verified retry proves the exact
                # replacement before stopping only this package for launch.
                self._installed_apk_guard(self.source.sha256)
                self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE, check_text=False)
            if self.journal["state"] in ("rolling-back", "rollback-restoring"):
                self._device_guard()
                if self.journal["state"] == "rolling-back":
                    self.adb.run("-s", self.address, "install", "-r", self.backup / "original.apk", timeout=self.install_timeout, positive=r"^Success\s*$")
                    self.commit("rollback-restoring")
                self._restore(expected_apk=self.journal["originalApk"]["sha256"])
                self._close_storage_process(); self.commit("backed-up", rolledBack=True)
                raise RecoveryError("Interrupted rollback has restored the original app and data; retry the selected new APK.")
            return {"schema": 1, "transaction": self.journal["transaction"], "backupPath": str(self.backup),
                    "verified": self.journal["state"] in ("verified", "complete")}
        except Exception as error:
            directory = str(getattr(self, "backup", self.home))
            raise RecoveryError("Signing migration could not safely continue: " + str(error)
                                + ". Original recovery data is retained at " + directory + ". Retry with the same APK and headset.") from error
        finally:
            failed = sys.exc_info()[0] is not None
            try: self._close_storage_process()
            except RuntimeError as cleanup:
                if not failed: raise
                print("Signing recovery storage cleanup could not be confirmed: " + str(cleanup)[:1024], flush=True)

    def complete(self):
        if self.journal and self.journal["state"] == "verified":
            self.commit("complete")
            progress("verify", 7, 7, "Signing migration and original app-data restoration verified")
            if self.journal.get("parkingMode") == "copy":
                # Success is already committed. Device staging is reproducible
                # and no longer needed; canonical host backups stay untouched.
                try:
                    for name in self.journal["banks"]:
                        self._device_guard()
                        path = self.park + "/" + name.split("/")[-1]
                        self.shell("test ! -L " + shlex.quote(path) + "\nrm -rf " + shlex.quote(path), timeout=120)
                    self._device_guard()
                    self.shell("rmdir " + shlex.quote(self.park), timeout=30)
                except RuntimeError:
                    print("Signing migration completed; retained device staging awaits cleanup: " + self.park, flush=True)
