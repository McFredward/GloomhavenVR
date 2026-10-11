"""Recover one actual Android signing conflict without changing any signing key.

The transaction is deliberately independent of the host OS and of Unity. All
required save data has durable, verified host backups before package removal.
The default saves policy excludes regenerable game banks and APK rollback copies.
Banks are retained with a real renameat syscall, never a hidden mv copy fallback.
An interrupted transaction resumes restoration before any launch. Legacy full
transactions retain their existing rollback promise after destructive work.
"""
from __future__ import annotations

import hashlib
import importlib.util
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
SAVE_ROOTS = ("GloomSaves", "GloomSavesOpenBeta", "GloomSavesClosedBeta", "GloomSavesDev", "GloomSavesLocalDev", "GloomSavesInEditor")


class RecoveryError(RuntimeError):
    """The existing data could not be proved safe to replace or restore."""


class DataPolicyError(RecoveryError):
    def __init__(self, message, *, save_data):
        super().__init__(message)
        self.reason = "save-data-inaccessible" if save_data else "non-save-inaccessible"


class CaptureRepairError(RecoveryError):
    def __init__(self, message, *, root, reason, attempts, total):
        super().__init__(message)
        self.action = {"schema": 1, "kind": "signing-repair-wait", "reason": reason, "saveDataProtected": True,
                       "attempts": attempts, "totalAttempts": total,
                       "root": root if root in ("private", "device-protected", "external") else "external"}


class CaptureFault(RecoveryError):
    """Carry bounded diagnostics about a rejected binary chunk, never trust it."""
    def __init__(self, message, *, byte_count, observed):
        super().__init__(message)
        self.byte_count, self.observed = byte_count, observed


class ContentReinstallChoiceError(RecoveryError):
    def __init__(self, *, estimated_bytes=0, transaction, device_serial):
        super().__init__("Android cannot retain the game directories with a direct rename. The installed app and saved games are retained. Confirm content reinstallation to continue; no game-bank backup or copy was started.")
        self.action = {"schema": 1, "kind": "signing-content-reinstall", "reason": "native-rename-unavailable",
                       "saveDataProtected": True, "estimatedBytes": estimated_bytes,
                       "transaction": transaction, "deviceSerial": device_serial}


def save_member(name):
    """Protect complete native release roots and native save-transfer history.

    RootSaveData places all globals, parties, checkpoints and old/corrupted saves
    below six release-channel roots. Quest save imports place retained backups
    and an interrupted transaction beside them. Extensions are never consulted:
    even a crash-like filename is protected when located inside a save root.
    Android Unity's persistent parent is files; root-level anchors also cover
    retained private/legacy layouts. Similar names elsewhere are not native roots.
    """
    parts = name.split("/")
    if parts[0] == "files": parts = parts[1:]
    if not parts: return True  # The persistent parent must remain accessible.
    anchor = parts[0]
    return (anchor in SAVE_ROOTS or anchor == "QuestSaveBackups"
            or anchor in (".quest-save-transfer.json", ".quest-save-snapshot.json")
            or anchor.startswith(".quest-save-import-"))


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
    # tarfile accepts an early end marker and can stop after a damaged header.
    # That is insufficient for save preservation. Android toybox writes complete
    # 512-byte headers/payload padding and at least two zero trailer blocks.
    # In capture141941 Windows adb shell produced nonaligned 6679/3076-byte
    # outputs while the old exec-out APK transfer remained intact. Reject the
    # entire malformed stream, including pollution after its first end marker.
    size = Path(path).stat().st_size
    if size < 1024 or size % 512: raise RecoveryError("Recovery archive has invalid 512-byte framing.")
    with Path(path).open("rb") as stream:
        offset = 0
        while offset < size:
            stream.seek(offset); header = stream.read(512)
            if header == b"\0" * 512:
                if size - offset < 1024: raise RecoveryError("Recovery archive has an incomplete end trailer.")
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    if block.strip(b"\0"): raise RecoveryError("Recovery archive has data after its end trailer.")
                break
            try: member = tarfile.TarInfo.frombuf(header, "utf-8", "surrogateescape")
            except tarfile.HeaderError as error: raise RecoveryError("Recovery archive has an invalid header checksum.") from error
            if member.size < 0: raise RecoveryError("Recovery archive has a negative member size.")
            offset += 512 + ((member.size + 511) // 512) * 512
            if offset > size - 1024: raise RecoveryError("Recovery archive payload or end trailer is truncated.")
        else: raise RecoveryError("Recovery archive has no complete end trailer.")
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
    def __init__(self, adb, address, hardware, source, directory, *, install_timeout=1800, decode_receipt=None, data_policy="saves", allow_content_reinstall=None):
        if not re.fullmatch(r"[A-Za-z0-9_-]+", hardware): raise RecoveryError("Invalid headset identity.")
        if data_policy not in ("all", "saves"): raise RecoveryError("Invalid signing recovery data policy.")
        if allow_content_reinstall is not None and not re.fullmatch(r"[0-9a-f]{32}", str(allow_content_reinstall)):
            raise RecoveryError("Invalid content reinstallation choice.")
        self.adb, self.address, self.hardware, self.source = adb, address, hardware, source
        self.data_policy = data_policy
        self.home = ordinary(Path(directory) / "signing-recovery" / hardware)
        self.home.mkdir(parents=True, exist_ok=True)
        self.path = ordinary(self.home / "active.json")
        self.journal = None; self.install_timeout = install_timeout
        self.decode_receipt = decode_receipt
        self._direct_storage_proved = False
        self._storage_process = None
        self._source_fingerprint = self._fingerprint(source.apk)
        self._rename_helper = None
        self._apk_regenerated_files = None
        self.allow_content_reinstall = allow_content_reinstall

    def _require_content_reinstall_choice(self):
        if self.allow_content_reinstall == self.journal["transaction"]: return
        total = sum(max(0, getattr(bank, "size", 0)) for bank in getattr(self.source, "content", ()))
        raise ContentReinstallChoiceError(estimated_bytes=total, transaction=self.journal["transaction"], device_serial=self.hardware)

    @staticmethod
    def _fingerprint(path):
        row = Path(path).stat()
        return (row.st_dev, row.st_ino, row.st_size, row.st_mtime_ns, row.st_ctime_ns)

    def _source_guard(self, *, phase="recovery preparation"):
        # Source selection already qualified this APK's SHA. Changes during this
        # transaction invalidate its cheap stat witness; do not reread 2.9 GB at
        # every preparation boundary merely to rediscover the same identity.
        retained = tuple(self.journal.get("selectedSourceStat", self._source_fingerprint))
        if self._fingerprint(self.source.apk) != self._source_fingerprint or retained != self._source_fingerprint or not replacement_is_debuggable(self.source.apk):
            raise RecoveryError("The selected APK changed during " + phase + "; the installed app was retained.")

    def _lightweight(self):
        return bool(self.journal and self.journal.get("smallDataOnly") is True)

    def _regenerated_apk_files(self):
        """Select Unity extraction copies from APK metadata without payload reads.

        The retained device census contains 35 MB of IL2CPP metadata/resources
        also present in the actual APK. The replacement extracts its own copies.
        Match exact packaged filenames; unknown siblings remain ordinary app data.
        """
        if self._apk_regenerated_files is None:
            prefix = "assets/bin/Data/Managed/"
            with zipfile.ZipFile(self.source.apk) as archive:
                names = {item.filename[len(prefix):] for item in archive.infolist()
                         if not item.is_dir() and item.filename.startswith(prefix)}
            self._apk_regenerated_files = tuple("files/il2cpp/" + name for name in sorted(names)
                if name == "Metadata/global-metadata.dat"
                or re.fullmatch(r"Resources/[^/\r\n\x00]+\.dll-resources\.dat", name))
        return self._apk_regenerated_files

    def _prepare_rename(self):
        if self._rename_helper: return
        spec = importlib.util.spec_from_file_location("quest_native_rename", Path(__file__).with_name("native_rename.py"))
        helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
        local = ordinary(self.backup / "rename-arm64")
        if local.exists() and (not local.is_file() or local.stat().st_nlink != 1): raise RecoveryError("Unsafe local native-rename helper path.")
        local.write_bytes(helper.executable())
        remote = "/data/local/tmp/.ghvr-rename-" + self.journal["transaction"]
        self._device_guard()
        self.shell("test ! -L " + shlex.quote(remote) + "\nif test -e " + shlex.quote(remote) + "; then test -f " + shlex.quote(remote) + "; fi")
        self.adb.run("-s", self.address, "push", local, remote, timeout=30, check_text=False)
        self.shell("chmod 700 " + shlex.quote(remote))
        self._rename_helper = remote

    def _native_rename(self, source, target):
        self._prepare_rename(); self._device_guard()
        # Capture errno explicitly: exec transports cannot be trusted to forward
        # a remote program's exit status. Shell arguments are quoted once.
        reply = self.shell("status=0\n" + shlex.quote(self._rename_helper) + " " + shlex.quote(source) + " " + shlex.quote(target)
                           + " || status=$?\nprintf 'GHVRQ_RENAME %s\\n' \"$status\"")
        results = re.findall(r"^GHVRQ_RENAME ([0-9]{1,3})$", reply, re.M)
        if len(results) != 1 or len(reply) > 4096: raise RecoveryError("The native directory rename did not return a bounded result.")
        code = int(results[0])
        with self.adb.log.open("a", encoding="utf-8") as log:
            log.write(json.dumps({"nativeRename": {"source": source, "target": target, "errno": code, "copiesPayload": False}}) + "\n")
        return code == 0

    def _installed_stat(self, path):
        value = self.shell("stat -c '%d:%i:%s:%Y:%Z' " + shlex.quote(path))
        if not re.fullmatch(r"[0-9]+:[0-9]+:[0-9]+:-?[0-9]+:-?[0-9]+", value):
            raise RecoveryError("The installed APK's metadata identity could not be verified.")
        return value

    def _legacy_upgrade_guard(self, installed):
        """Qualify a pre-removal legacy frontier with bounded APK witnesses.

        Older journals lack remote stat witnesses. Their retained original APK
        supplies small beginning/end comparisons and its original version stamp.
        This is a bounded migration identity check, not a new full-byte hash.
        No legacy bank is moved or discarded until these checks pass.
        """
        paths = installed.splitlines()
        if len(paths) != 1 or not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", paths[0]):
            raise RecoveryError("The installed package changed before the legacy recovery could be upgraded.")
        retained = self.journal.get("originalApk", {})
        remote = paths[0][8:]
        if (not retained and self.journal["state"] == "created" and not self.journal.get("banks")
                and not self.journal.get("movingBank") and self.journal.get("parkingMode") is None):
            return  # Interrupted before original identity or any bank action.
        before = self._installed_stat(remote)
        if type(retained.get("size")) is not int or int(before.split(":")[2]) != retained["size"]:
            raise RecoveryError("The installed APK changed before the legacy recovery could be upgraded; the current app was retained.")
        original = ordinary(self.backup / "original.apk")
        if not original.is_file():
            if self.journal["state"] != "created": raise RecoveryError("The legacy recovery lacks its original APK identity witness; the current app was retained.")
            return  # No bank movement has begun in this incomplete preflight.
        if original.stat().st_size != retained["size"] or original.stat().st_nlink != 1:
            raise RecoveryError("The original APK metadata witness is damaged; the current app was retained.")
        amount = min(65536, retained["size"])
        with original.open("rb") as stream:
            for tool, offset in (("head", 0), ("tail", retained["size"] - amount)):
                stream.seek(offset); expected = stream.read(amount)
                candidate = ordinary(self.backup / (".legacy-identity-" + uuid.uuid4().hex))
                try:
                    self._transfer(["exec-out", tool, "-c", str(amount), remote], candidate, total=amount)
                    if candidate.read_bytes() != expected:
                        raise RecoveryError("The installed APK changed before the legacy recovery could be upgraded; the current app was retained.")
                finally: candidate.unlink(missing_ok=True)
        # The builder's version name is a literal pool entry in Android's binary
        # manifest. Reading that one small ZIP member avoids another APK scan.
        try:
            with zipfile.ZipFile(original) as archive:
                item = archive.getinfo("AndroidManifest.xml")
                if item.file_size > 1024 * 1024: raise ValueError("oversized manifest")
                data = archive.read(item)
            offset = struct.unpack_from("<H", data, 2)[0]; stamps = []
            while offset < len(data):
                kind, header, size = struct.unpack_from("<HHI", data, offset)
                if size < header or size < 8 or offset + size > len(data): raise ValueError("invalid manifest chunk")
                if kind == 1: stamps.extend(value for value in _string_pool(data, offset) if re.fullmatch(r"0\.1\.0\.B[0-9]+\.[0-9a-f]{12}", value))
                offset += size
            if stamps:
                record = self.adb.run("-s", self.address, "shell", "dumpsys", "package", PACKAGE, timeout=30, check_text=False)
                if len(stamps) != 1 or re.findall(r"\bversionName=([^\s]+)", record) != stamps:
                    raise RecoveryError("The installed APK version changed before legacy recovery; the current app was retained.")
        except (OSError, KeyError, zipfile.BadZipFile, ValueError, struct.error) as error:
            raise RecoveryError("The legacy original APK manifest witness is damaged; the current app was retained.") from error
        if self._installed_stat(remote) != before: raise RecoveryError("The installed APK changed during its bounded legacy identity check.")

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
                raw = self.adb.run("-s", self.address, "shell", "pidof", PACKAGE, check_text=False, empty_exit_codes=(1,)).strip()
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
        self._source_guard()
        self.backup = ordinary(self.home / value["transaction"])
        self.park = "/sdcard/Download/.ghvr-quest-recovery/" + self.hardware + "/" + value["transaction"]
        previous_policy = value.get("dataPolicy", "all")
        if previous_policy not in ("all", "saves"): raise RecoveryError("Invalid retained signing recovery data policy.")
        if self.data_policy == "saves" and not self._lightweight() and value["state"] in ("created", "backed-up", "parking", "parked", "uninstalling"):
            installed = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False)
            if installed.startswith("package:"):
                self._legacy_upgrade_guard(installed)
                # Capture150551 stops inside the old FUSE copy fallback, while
                # the original app and saves are still present. Retain healthy
                # small archives, stop that copy frontier, and move any genuinely
                # parked tree back with renameat. Never finish its 11-GB copy.
                for name in value.get("banks", []):
                    if name not in BANKS: raise RecoveryError("Invalid parked-bank identity.")
                    original, parked = EXTERNAL + "/" + name, self.park + "/" + name.split("/")[-1]
                    state = self.shell("if test -d " + shlex.quote(original) + "; then printf present; elif test -d "
                                       + shlex.quote(parked) + "; then printf parked; else printf absent; fi")
                    if state == "parked":
                        self.shell("mkdir -p " + shlex.quote(EXTERNAL + "/files"))
                        if not self._native_rename(parked, original):
                            raise RecoveryError("The still-installed app's parked bank could not be returned without copying; its saves and bank remain retained.")
                    elif state == "present" and value.get("parkingMode") == "copy":
                        # This is the old transaction's partial duplicate, not
                        # the installed bank. Its intact original still exists.
                        # Remove generated staging without reading any payload.
                        self.shell("test -d " + shlex.quote(original) + "\ntest ! -L " + shlex.quote(parked)
                                   + "\nif test -d " + shlex.quote(parked) + "; then rm -rf " + shlex.quote(parked) + "; fi")
                self.commit("created", dataPolicy="saves", smallDataOnly=True, parkingMode=None, movingBank=None)
                previous_policy = "saves"
        if previous_policy != self.data_policy:
            if value["state"] not in ("created", "backed-up"):
                # Once replacement starts, use its original durable promise.
                # An omitted old file cannot be recovered after uninstall.
                self.data_policy = previous_policy
            else:
                # Retain all valid root archives, original APK and bank receipts.
                # Recensus only changes each root's required positive list; a
                # matching completed archive remains reusable in _backup.
                self.commit("created", dataPolicy=self.data_policy)
        return True

    def _record_omissions(self, item, omissions):
        if self.journal["state"] not in ("created", "backed-up", "parking", "parked", "uninstalling"):
            # The restored tree cannot contain intentionally uncaptured files.
            # Its verification must not erase the durable original loss receipt.
            return
        item["omittedEntries"] = omissions
        retained = self.journal.get("policyOmissions", [])
        indexed = {(row["root"], row["path"]): row for row in retained if row["root"] != item["name"]}
        for row in omissions: indexed[(item["name"], row["path"])] = {"root": item["name"], **row}
        if omissions or retained:
            self.commit(policyOmissions=list(indexed.values()))
        if omissions:
            progress("access", len(omissions), len(omissions), "Save protection retained; " + str(len(omissions))
                     + " inaccessible non-save entries will be omitted", unit="files", local=True)

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

    def _restore_tar_args(self, root, *, internal=False, access_mode=None):
        actual_root = self._access_root(root, access_mode)
        # Windows adb's RemoteShell path leaves CRT stdin/stdout in text mode.
        # -T only disables the remote PTY. exec-in/out selects binary CRT mode;
        # using Python binary files alone does not prevent adb translating LF.
        command = ["exec-in"] + (["run-as", PACKAGE] if internal or access_mode in ("app", "app-direct", "app-process") else [])
        # Android toybox tar otherwise attempts to restore the old app UID.
        # -o keeps ownership with the new run-as UID after package replacement.
        command += ["tar", "-C", actual_root, "-xof", "-"]
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
                for path in ([] if self._lightweight() else self.journal["bankWitnesses"][bank]):
                    ignored.add(prefix + path)
                    directories.update(prefix + str(parent) for parent in PurePosixPath(path).parents if str(parent) != ".")
                ignored.update(prefix + ".quest-installation.receipt" + suffix for suffix in ("", ".previous", ".pending"))
        errors = []; observed_failure = False; complete_save_census = False
        for mode in modes:
            try:
                actual_root = self._access_root(root, mode)
                if not item["internal"]: self._check_external_root(actual_root, mode)
                # Android Toybox treats -mindepth as an expression test. Without
                # parentheses it guards only the first branch of a prune/-o
                # chain: capture155522 printed the root itself and stopped the
                # save-only retry before any uninstall. Group the entire action
                # and use NUL framing, so Windows shell/log line endings never
                # create path entries. A foreign or malformed path still fails.
                pruned = [actual_root + "/" + name for name in ("cache", "code_cache")] if item["internal"] else []
                if self._lightweight() and item["internal"]:
                    # bridge.c owns these regenerated Wine/Proton directories;
                    # native game saves live under the protected GloomSaves
                    # roots, never inside this engine prefix. Keep its small
                    # diagnostics and any unknown sibling settings.
                    # QuestGameProcedural gets Android Context.filesDir. The
                    # top-level spelling is retained only for old layouts.
                    pruned += [actual_root + "/" + prefix + "/" + name
                               for prefix in ("files/quest-procedural-state", "quest-procedural-state")
                               for name in ("proton-prefix", "wine-prefix", "cache", "tmp", "home")]
                if self._lightweight() and not item["internal"]:
                    # Regenerable asset banks never become app data when an old
                    # receipt is absent or unreadable. Prune their payload trees
                    # before find visits them. Unknown bank-root settings remain
                    # in the typed positive census and are retained normally.
                    pruned = [actual_root + "/" + ("" if parked_bank else bank + "/") + "StreamingAssets"
                              for bank in ([parked_bank] if parked_bank else BANKS)]
                    if not parked_bank:
                        # Android's external cache and exact APK extraction
                        # copies are regenerated, not savegames/settings. Do not
                        # prune unknown IL2CPP siblings or other files directories.
                        pruned += [actual_root + "/cache"]
                        pruned += [actual_root + "/" + name for name in self._regenerated_apk_files()]
                # -path applies fnmatch even to a shell-quoted argument. Escape
                # its metacharacters too, so a packaged resource filename never
                # excludes a differently named, unknown user file.
                prune = " ".join("-path " + shlex.quote(re.sub(r"([\\*?\[\]])", r"\\\1", path)) + " -prune -o" for path in pruned)
                raw = self._context_shell("find " + shlex.quote(actual_root) + r" -mindepth 1 \( " + prune + r" -print0 \)", mode)
                if raw and not raw.endswith("\x00"):
                    raise RecoveryError("The app-data census has incomplete NUL framing; its original data was retained.")
                if len(raw) > 16 * 1024 * 1024:
                    raise RecoveryError("The app-data census exceeds its bounded size.")
                observed = raw[:-1].split("\x00") if raw else []
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
                rows = {}; failed = []; omissions = []; protected_failure = False
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
                        if parts[2] in ("U", "S"):
                            observed_failure = True
                            protected = save_member(name)
                            protected_failure = protected_failure or protected
                            if self.data_policy == "saves" and not protected:
                                omissions.append({"path": name, "type": "file" if parts[2] == "U" else "special",
                                                  "size": int(parts[1].split(":")[1]), "reason": "unreadable-non-save"})
                            else: failed.append(name)
                        else: rows[name] = {"type": {"F": "file", "D": "directory", "L": "symlink"}[parts[2]],
                                            "size": int(parts[1].split(":")[1]), "mode": int(parts[1].split(":")[0], 16) & 0o7777}
                    progress("access", offset + len(batch), len(names), "Checking " + item["name"] + " app data using " + mode + " access", unit="files", local=True)
                complete_save_census = complete_save_census or not protected_failure
                if failed: raise RecoveryError("Cannot safely read " + str(len(failed)) + " " + item["name"] + " app-data entries using " + mode + " access; first: " + failed[0])
                # Typed positive lists protect names containing wildcard or
                # option syntax. Binary capture sends bounded raw argv chunks;
                # ADB itself escapes those names exactly once for remote exec.
                result = {"accessMode": mode, "capturePaths": ["."] + ["./" + name for name in sorted(rows)], "captureEntries": rows,
                          "captureFiles": len(rows) + 1, "captureBytes": sum(value["size"] for value in rows.values() if value["type"] == "file")}
                item.update(result)
                self._record_omissions(item, omissions)
                return rows
            except RuntimeError as error:
                errors.append(mode + ": " + str(error)[:1200])
                self._device_guard()
        raise DataPolicyError("Automatic app-data access repair exhausted shell and app identities: " + "; ".join(errors)
                              + "; the installed app was retained.", save_data=not complete_save_census or not observed_failure)

    def _capture_diagnostic(self, item, local, observed, *, method, attempt, total, reason, fault=None):
        if isinstance(fault, CaptureFault): observed = fault.observed
        expected = item["captureEntries"]; actual = observed or {}
        missing = set(expected) - set(actual); extra = set(actual) - set(expected)
        types = {name for name in set(expected) & set(actual) if expected[name]["type"] != actual[name]["type"]}
        sizes = {name for name in set(expected) & set(actual) if expected[name]["type"] == actual[name]["type"] == "file"
                 and expected[name]["size"] != actual[name]["size"]}
        byte_count = fault.byte_count if isinstance(fault, CaptureFault) else local.stat().st_size if local.is_file() else 0
        value = {"root": item["name"] if item["name"] in ("private", "device-protected", "external") else "external",
                 "method": method, "attempts": attempt, "totalAttempts": total, "reason": reason,
                 "expectedCount": len(expected), "observedCount": len(actual), "missingCount": len(missing),
                 "unexpectedCount": len(extra), "typeMismatchCount": len(types), "sizeMismatchCount": len(sizes),
                 "archiveBytes": byte_count, "archiveAligned": byte_count >= 1024 and byte_count % 512 == 0}
        self.commit(captureRepair=value)
        with self.adb.log.open("a", encoding="utf-8") as log:
            log.write(json.dumps({"captureRepair": value, "missingSample": sorted(missing)[:6], "unexpectedSample": sorted(extra)[:6],
                                  "typeSample": sorted(types)[:6], "sizeSample": sorted(sizes)[:6]}, ensure_ascii=False) + "\n")
        return not (missing or extra or types or sizes)

    def _rejected_archive_observation(self, path):
        # Diagnostic only: Python can observe a prefix of a damaged tar. None of
        # these members are eligible for reuse/restoration until strict proof.
        result = {}
        try:
            with tarfile.open(path, "r:") as archive:
                for member in archive:
                    name = member.name
                    while name.startswith("./"): name = name[2:]
                    if name in ("", "."): continue
                    if len(result) >= 100000: break
                    value = {"type": "file" if member.isfile() else "directory" if member.isdir() else "symlink" if member.issym() else "special"}
                    if member.isfile(): value["size"] = member.size
                    result[name] = value
        except (tarfile.TarError, OSError, ValueError): pass
        return result

    def _binary_args(self, item, command):
        return ["exec-out"] + (["run-as", PACKAGE] if item["internal"] or item.get("accessMode") in ("app", "app-direct", "app-process") else []) + command

    def _exec_tar_capture(self, item, local, *, work=None):
        actual_root = self._access_root(item["root"], item.get("accessMode"))
        # exec-out does not forward stdin. ADB escape_arg already quotes each
        # argument after the executable: pass raw names, never literal shell
        # quotes. Bounded positive argv preserves names and Windows limits.
        prefix = self._binary_args(item, ["tar", "-C", actual_root, "-cf", "-", "--no-recursion"])
        overhead = sum(len(shlex.quote(value).encode("utf-8")) + 1 for value in prefix)
        chunks = []; chunk = []; length = overhead
        for name in item["capturePaths"]:
            quoted = shlex.quote(name); needed = len(quoted.encode("utf-8")) + 1
            if chunk and length + needed > 8192: chunks.append(chunk); chunk = []; length = overhead
            chunk.append(name); length += needed
        if chunk: chunks.append(chunk)
        offset = 0
        with tarfile.open(local, "w:", format=tarfile.PAX_FORMAT) as output:
            for number, paths in enumerate(chunks):
                scratch = self.backup / (".capture-" + uuid.uuid4().hex + ".tar")
                try:
                    args = [*prefix, *paths]
                    chunk_work = (work[0] + min(offset, work[1]), min(len(paths), max(0, work[1] - offset)), work[2], work[3]) if work else None
                    self._transfer(args, scratch, work=chunk_work)
                    try: observed = archive_inventory(scratch)
                    except (RecoveryError, tarfile.TarError) as error:
                        raise CaptureFault(str(error), byte_count=scratch.stat().st_size, observed=self._rejected_archive_observation(scratch)) from error
                    names = {name[2:] for name in paths if name != "."}
                    if set(observed) != names:
                        raise CaptureFault("Binary tar chunk differs from its positive file census.", byte_count=scratch.stat().st_size, observed=observed)
                    for name, row in observed.items():
                        expected = item["captureEntries"][name]
                        if expected["type"] != row["type"] or row["type"] == "file" and expected["size"] != row["size"]:
                            raise CaptureFault("Binary tar chunk differs from its typed member sizes.", byte_count=scratch.stat().st_size, observed=observed)
                    with tarfile.open(scratch, "r:") as source:
                        for member in source: output.addfile(member, source.extractfile(member) if member.isfile() else None)
                    offset += len(paths)
                    if work: progress("backup-work", work[0] + min(offset, work[1]), work[2], work[3], local=True)
                finally: scratch.unlink(missing_ok=True)

    def _member_identity(self, item, name, *, digest=False):
        path = self._access_root(item["root"], item.get("accessMode")) + "/" + name
        script = "stat -c '%f:%s' " + shlex.quote(path)
        if digest: script += "\nsha256sum " + shlex.quote(path)
        values = self._context_shell(script, "app" if item["internal"] else item.get("accessMode", "shell")).splitlines()
        row = item["captureEntries"][name]
        expected_kind = {"file": 0x8000, "directory": 0x4000, "symlink": 0xa000}[row["type"]]
        if (len(values) != (2 if digest else 1) or not re.fullmatch(r"[0-9a-fA-F]+:[0-9]+", values[0])
                or int(values[0].split(":")[0], 16) & 0xf000 != expected_kind
                or int(values[0].split(":")[1]) != row["size"]):
            raise RecoveryError("A streamed member changed its type or size during capture.")
        if digest and not re.match(r"[0-9a-f]{64}  ", values[1]): raise RecoveryError("The streamed member hash could not be verified.")
        return path, values[1][:64] if digest else None

    def _host_pax_capture(self, item, local, *, work=None):
        # This fallback does not trust device tar/list handling. Every regular
        # file crosses the binary raw service with exact length and pre/post
        # native hashes; directories and terminal symlinks retain their types.
        with tarfile.open(local, "w:", format=tarfile.PAX_FORMAT) as output:
            for index, (name, row) in enumerate(sorted(item["captureEntries"].items())):
                path, digest = self._member_identity(item, name, digest=row["type"] == "file")
                member = tarfile.TarInfo(name); member.mode = row["mode"]
                scratch = self.backup / (".capture-member-" + uuid.uuid4().hex)
                try:
                    if row["type"] == "directory": member.type = tarfile.DIRTYPE
                    elif row["type"] == "symlink":
                        if row["size"] > 16384: raise RecoveryError("The streamed symbolic link exceeds its byte bound.")
                        self._transfer(self._binary_args(item, ["readlink", path]), scratch)
                        raw = scratch.read_bytes()
                        if len(raw) != row["size"] + 1 or not raw.endswith(b"\n") or b"\0" in raw: raise RecoveryError("The streamed symbolic link changed or included unexpected output.")
                        member.type = tarfile.SYMTYPE; member.linkname = raw[:-1].decode("utf-8")
                        if len(member.linkname) > 4096: raise RecoveryError("The streamed symbolic link exceeds the supported bound.")
                    else:
                        self._transfer(self._binary_args(item, ["cat", path]), scratch, total=row["size"])
                        if scratch.stat().st_size != row["size"] or file_hash(scratch) != digest:
                            raise RecoveryError("The streamed member bytes were incomplete, polluted or changed.")
                        member.size = row["size"]
                        with scratch.open("rb") as stream: output.addfile(member, stream)
                    if row["type"] != "file": output.addfile(member)
                    if self._member_identity(item, name, digest=row["type"] == "file") != (path, digest):
                        raise RecoveryError("The streamed member changed after capture.")
                    progress("repair", index + 1, len(item["captureEntries"]), "[repair-root:" + (item["name"] if item["name"] in ("private", "device-protected", "external") else "external")
                             + "] Saving verified members using binary file transfer", unit="files", local=True)
                    if work: progress("backup-work", work[0] + min(index + 2, work[1]), work[2], work[3], local=True)
                finally: scratch.unlink(missing_ok=True)

    def _capture_root(self, item, local, *, work=None, fresh=False, parked_bank=None):
        if fresh or "capturePaths" not in item or "captureEntries" not in item: self._capture_census(item, parked_bank=parked_bank)
        methods = (["host-pax"] if item.get("captureTransport") == "host-pax" else ["exec-out-tar", "host-pax"]) + ["host-pax"]
        last = None; reason = "backup-census-mismatch"
        for attempt, method in enumerate(methods, 1):
            if attempt > 1:
                self._device_guard(); self._capture_census(item, parked_bank=parked_bank)
                progress("repair", attempt, len(methods), "[repair-root:" + (item["name"] if item["name"] in ("private", "device-protected", "external") else "external")
                         + "] Repairing only this app-data backup using " + method, unit="attempts", local=True)
            candidate = ordinary(self.backup / (".capture-candidate-" + uuid.uuid4().hex + ".tar")); inventory = None
            try:
                self._device_guard()
                if method == "exec-out-tar": self._exec_tar_capture(item, candidate, work=work)
                else: self._host_pax_capture(item, candidate, work=work)
                if item.get("accessMode") == "app-process": self._process_storage_root()
                inventory = archive_inventory(candidate)
                if not self._capture_diagnostic(item, candidate, inventory, method=method, attempt=attempt, total=len(methods), reason="verified"):
                    raise RecoveryError("The repaired backup differs from its typed positive census.")
                ordinary(local)
                if local.exists() and (not local.is_file() or local.stat().st_nlink != 1): raise RecoveryError("Unsafe capture destination.")
                os.replace(candidate, local)
                item["captureTransport"] = method
                return inventory
            except (RuntimeError, OSError, subprocess.SubprocessError, tarfile.TarError, UnicodeError) as error:
                last = error
                reason = "backup-transfer-unavailable" if isinstance(error, (OSError, subprocess.SubprocessError)) or "transfer" in str(error).lower() or "ADB failed" in str(error) else "backup-census-mismatch"
                self._capture_diagnostic(item, candidate, inventory, method=method, attempt=attempt, total=len(methods), reason=reason, fault=error)
                with self.adb.log.open("a", encoding="utf-8") as log: log.write("Root capture repair: " + str(error)[:1200] + "\n")
            finally: candidate.unlink(missing_ok=True)
        raise CaptureRepairError("App-data backup repair exhausted " + str(len(methods)) + " reversible attempts; verified backups and the installed app remain retained: " + str(last),
                                 root=item["name"], reason=reason, attempts=len(methods), total=len(methods))

    def _capture_stable_root(self, item, local, *, work=None):
        for attempt in range(1, 4):
            inventory = self._capture_root(item, local, fresh=attempt > 1, work=work)
            if work: progress("backup-work", work[0] + work[1], work[2], "Saved " + item["name"] + " app-data members", local=True)
            verify = self.backup / (item["name"] + ".verify.tar")
            verify_work = (work[0] + work[1], work[1], work[2], "Verifying " + item["name"] + " app data") if work else None
            try: observed = self._capture_root(item, verify, fresh=True, work=verify_work)
            finally: verify.unlink(missing_ok=True)
            if observed == inventory: return inventory
            self._capture_diagnostic(item, local, inventory, method=item["captureTransport"], attempt=attempt, total=3, reason="backup-census-mismatch")
            changed = sum(inventory.get(name) != observed.get(name) for name in set(inventory) | set(observed))
            with self.adb.log.open("a", encoding="utf-8") as log: log.write(json.dumps({"mutableCapture": {"root": item["name"], "changedCount": changed, "attempts": attempt, "totalAttempts": 3}}) + "\n")
            self._close_storage_process(); self._device_guard()
            self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
            progress("repair", attempt, 3, "[repair-root:" + item["name"] + "] App data changed; preserving this root again", unit="attempts", local=True)
        raise CaptureRepairError("App data changed during its backup after three scoped retries; existing saves and verified backups were retained.",
                                 root=item["name"], reason="backup-census-mismatch", attempts=3, total=3)

    def _repair_retained_archives(self):
        if self.journal["state"] not in ("backed-up", "parking", "parked", "uninstalling"): return
        # Validate only identities before reading old streams. Fresh source data
        # can repair a damaged archive only while the exact old APK still exists.
        self._verify_backups(original=False, validate_only=True)
        original = ordinary(self.backup / "original.apk")
        try:
            apk_damaged = not self._lightweight() and (original.stat().st_size != self.journal["originalApk"]["size"] or file_hash(original) != self.journal["originalApk"]["sha256"])
        except OSError: apk_damaged = True
        damaged = []
        for item in self.journal["roots"]:
            local = ordinary(self.backup / item["archive"])
            try:
                if file_hash(local) != item["sha256"] or archive_inventory(local) != item["inventory"]: damaged.append(item)
            except (RecoveryError, OSError, tarfile.TarError, UnicodeError): damaged.append(item)
        if not damaged and not apk_damaged: return
        self._installed_apk_guard(self.journal["originalApk"]["sha256"])
        self._close_storage_process()
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
        if self.journal["state"] != "backed-up": self._restore_banks()
        for item in damaged:
            progress("repair", 1, 3, "[repair-root:" + item["name"] + "] Repairing a damaged retained archive from the unchanged installed app", unit="attempts", local=True)
        if apk_damaged: progress("backup", 0, 7, "Repairing only the retained original APK from the unchanged installed package")
        # _backup retains and reuses healthy archives/APK/witnesses. Only failed
        # roots get fresh unique candidates; their old files are never deleted.
        self.commit("created")
        self._backup()

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
        self._source_guard()
        self._check_users()
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
        installed = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False).splitlines()
        if not installed: return False
        if len(installed) != 1 or not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", installed[0]): raise RecoveryError("Installed package changed since its recovery backup.")
        self._installed_apk_guard(self.journal["originalApk"]["sha256"])
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
        self._source_guard(phase="content parking")
        self._check_users(); self._device_guard()

    def _device_guard(self):
        if self.adb.identity(self.address) != self.hardware: raise RecoveryError("The ADB address now belongs to a different headset; no foreign app data will be changed.")

    def _installed_apk_guard(self, expected):
        self._device_guard()
        paths = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False).splitlines()
        if len(paths) != 1 or not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", paths[0]): raise RecoveryError("The package selected for restoration cannot be identified.")
        if expected is None and self._lightweight():
            original = self.journal["originalApk"]
            if paths[0][8:] != original["remotePath"] or self._installed_stat(paths[0][8:]) != original["stat"]:
                raise RecoveryError("The installed APK changed before data restoration; the unexpected app was retained.")
        elif self._lightweight() and expected == self.source.sha256:
            metadata = self._installed_stat(paths[0][8:])
            if int(metadata.split(":")[2]) != self._source_fingerprint[2]:
                raise RecoveryError("The installed replacement APK has an unexpected size; original saves remain retained.")
            build = getattr(self.source, "mod_build", None)
            if build:
                record = self.adb.run("-s", self.address, "shell", "dumpsys", "package", PACKAGE, check_text=False, timeout=30)
                if (re.findall(r"\bversionCode=(\d+)\b", record) != [str(build)]
                        or re.findall(r"\bversionName=([^\s]+)", record) != ["0.1.0.B" + str(build) + "." + self.source.input_key[:12]]):
                    raise RecoveryError("The installed replacement APK has an unexpected build identity; original saves remain retained.")
            retained = self.journal.get("replacementStat")
            if retained is not None and retained != metadata:
                raise RecoveryError("The installed replacement APK changed before data restoration; original saves remain retained.")
            if retained is None: self.commit(replacementStat=metadata)
        elif not self.shell("sha256sum " + shlex.quote(paths[0][8:])).startswith(expected + "  "):
            raise RecoveryError("The installed APK changed before data restoration; the unexpected app was retained.")

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
        original_stat = None
        if self._lightweight():
            original_stat = self._installed_stat(installed_apk)
            original_hash, original_size = None, int(original_stat.split(":")[2])
        else:
            raw = self.shell("sha256sum " + shlex.quote(installed_apk) + "\nstat -c %s " + shlex.quote(installed_apk))
            lines = raw.splitlines()
            if len(lines) != 2 or not re.fullmatch(r"[0-9a-f]{64}  .*", lines[0]) or not lines[1].isdigit():
                raise RecoveryError("The installed APK's recovery identity could not be verified.")
            original_hash, original_size = lines[0][:64], int(lines[1])
        retained_apk = self.journal.get("originalApk", {})
        if retained_apk and (retained_apk.get("stat") is not None if self._lightweight() else True) and (retained_apk.get("sha256") != original_hash or retained_apk.get("size") != original_size
                or self._lightweight() and retained_apk.get("stat") != original_stat):
            raise RecoveryError("The installed APK changed since partial recovery preparation; its existing app data was retained.")
        previous = {item.get("name"): dict(item) for item in self.journal.get("roots", []) if isinstance(item, dict)}
        # Readable mutable members are never narrowed to known save filenames.
        # The explicit saves policy omits only inaccessible members outside the
        # complete native roots, checkpoint/history and import rollback paths.
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
                if self._lightweight() or self._bank_is_immutable(remote): banks.append(name)
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
                item.update({key: retained[key] for key in ("archive", "sha256", "inventory", "captureTransport") if key in retained})
        # Persist prior verified references before a new access check can fail.
        original_identity = {"path": "original.apk", "sha256": original_hash, "size": original_size}
        if self._lightweight(): original_identity.update(remotePath=installed_apk, stat=original_stat)
        self.commit(originalApk=original_identity, roots=roots, banks=banks, excludePrivateLib=generated_lib)
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
        original_retained = self._lightweight() or original.is_file() and original.stat().st_size == original_size and file_hash(original) == original_hash
        required = (0 if original_retained else original_size) + sum(mutable_sizes) + max(mutable_sizes) * 2 + 64 * 1024 * 1024
        if shutil.disk_usage(self.backup).free < required:
            raise RecoveryError("Insufficient PC storage for a verified rollback backup (required " + str(required) + " bytes); the installed app was retained.")
        work_total = (0 if self._lightweight() else 2) + sum(item["captureFiles"] * 2 + 1 for item in roots); work_done = 0
        self.commit(backupWork={"done": 0, "total": work_total})
        original = self.backup / "original.apk"
        if not original_retained:
            candidate = ordinary(self.backup / (".original-candidate-" + uuid.uuid4().hex + ".apk"))
            try:
                self._transfer(["exec-out", "cat", installed_apk], candidate, total=original_size)
                if candidate.stat().st_size != original_size or file_hash(candidate) != original_hash:
                    raise RecoveryError("The original APK backup failed verification; the installed app was retained.")
                os.replace(candidate, original)
            finally: candidate.unlink(missing_ok=True)
        if not self._lightweight():
            progress("backup-work", 1, work_total, "Original APK rollback copy retained", local=True)
            if original.stat().st_size != original_size or file_hash(original) != original_hash:
                raise RecoveryError("The original APK backup failed verification; the installed app was retained.")
            work_done = 2; progress("backup-work", work_done, work_total, "Original APK rollback copy verified", local=True)
        for index, item in enumerate(roots):
            old = item.get("archive")
            reusable = False
            if isinstance(old, str) and re.fullmatch(re.escape(item["name"]) + r"-[0-9a-f]{32}\.tar", old):
                try:
                    # A narrowed save-only census already disproves reuse of a
                    # legacy asset archive. Compare its bounded recorded members
                    # before hashing/reading a known-obsolete large stream.
                    reusable = (set(item["inventory"]) == {path[2:] for path in item["capturePaths"] if path != "."}
                                and all(value["type"] == item["captureEntries"][name]["type"]
                                        and (value["type"] != "file" or value["size"] == item["captureEntries"][name]["size"])
                                        for name, value in item["inventory"].items())
                                and ordinary(self.backup / old).is_file() and file_hash(self.backup / old) == item.get("sha256")
                                and archive_inventory(self.backup / old) == item.get("inventory"))
                except (RecoveryError, OSError, tarfile.TarError):
                    # Keep the unique prior file and its reference until a fresh
                    # verified candidate replaces only this damaged root.
                    self._capture_diagnostic(item, self.backup / old, self._rejected_archive_observation(self.backup / old),
                                             method="exec-out-tar", attempt=1, total=3, reason="backup-census-mismatch")
            if reusable:
                work_done += item["captureFiles"] * 2 + 1
                self.commit(backupWork={"done": work_done, "total": work_total})
                progress("backup-work", work_done, work_total, "Reusing verified " + item["name"] + " app-data backup", local=True)
                continue
            local = self.backup / (item["name"] + "-" + uuid.uuid4().hex + ".tar")
            inventory = self._capture_stable_root(item, local, work=(work_done, item["captureFiles"], work_total, "Saving " + item["name"] + " app data"))
            item.update(archive=local.name, sha256=file_hash(local), inventory=inventory)
            work_done += item["captureFiles"] * 2 + 1
            self.commit(roots=roots, backupWork={"done": work_done, "total": work_total})
            progress("backup-work", work_done, work_total, "Verified " + item["name"] + " app-data backup committed", local=True)
            progress("backup", index + 1, len(roots), "Verified retained " + item["name"] + " app data", unit="files", local=True)
        self.commit("backed-up")
        progress("backup", 2, 7, "Saves and settings are safely retained" if self._lightweight() else "Original APK, saves and settings are safely retained")

    def _verify_backups(self, *, original=True, validate_only=False):
        policy = self.journal.get("dataPolicy", "all")
        if policy not in ("all", "saves") or policy != self.data_policy: raise RecoveryError("Invalid retained signing data policy.")
        apk = self.journal["originalApk"]
        if apk.get("path") != "original.apk": raise RecoveryError("Invalid original APK backup identity.")
        if self._lightweight():
            if (apk.get("sha256") is not None or not re.fullmatch(r"/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", str(apk.get("remotePath")))
                    or not re.fullmatch(r"[0-9]+:[0-9]+:[0-9]+:-?[0-9]+:-?[0-9]+", str(apk.get("stat")))
                    or type(apk.get("size")) is not int or int(apk["stat"].split(":")[2]) != apk["size"]):
                raise RecoveryError("Invalid installed APK metadata identity.")
        elif not re.fullmatch(r"[0-9a-f]{64}", str(apk.get("sha256"))): raise RecoveryError("Invalid original APK backup identity.")
        local = ordinary(self.backup / "original.apk")
        if not self._lightweight() and original and not validate_only and (local.stat().st_size != apk["size"] or file_hash(local) != apk["sha256"]): raise RecoveryError("The retained original APK backup is damaged.")
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
                entries = item.get("captureEntries")
                if entries is not None and (not isinstance(entries, dict) or set(entries) != {path[2:] for path in paths[1:]}
                        or any(not isinstance(row, dict) or row.get("type") not in ("file", "directory", "symlink")
                               or type(row.get("size")) is not int or row["size"] < 0
                               or type(row.get("mode")) is not int or not 0 <= row["mode"] <= 0o7777 for row in entries.values())):
                    raise RecoveryError("Invalid retained typed app-data census.")
            if "captureTransport" in item and item["captureTransport"] not in ("exec-out-tar", "host-pax"):
                raise RecoveryError("Invalid retained binary capture transport.")
            omitted = item.get("omittedEntries", [])
            if not isinstance(omitted, list) or len(omitted) > 100000: raise RecoveryError("Invalid retained app-data omission census.")
            seen_omitted = set()
            for row in omitted:
                name = row.get("path") if isinstance(row, dict) else None
                if (policy != "saves" or not isinstance(name, str) or len(name) > 4096 or name.startswith("/")
                        or any(char in name for char in "\x00\r\n") or any(part in ("", ".", "..") for part in name.split("/"))
                        or name in seen_omitted or save_member(name) or name in item["inventory"]
                        or row.get("type") not in ("file", "special") or type(row.get("size")) is not int or row["size"] < 0
                        or row.get("reason") != "unreadable-non-save"):
                    raise RecoveryError("Invalid or save-affecting retained app-data omission.")
                seen_omitted.add(name)
            if not validate_only and (file_hash(local) != item["sha256"] or archive_inventory(local) != item["inventory"]): raise RecoveryError("A retained app-data backup is damaged.")
        if any(name not in BANKS for name in self.journal.get("banks", [])): raise RecoveryError("Invalid parked-bank identity.")
        banks = self.journal.get("banks", [])
        if len(banks) != len(set(banks)): raise RecoveryError("Duplicate parked-bank identity.")
        witnesses = self.journal.get("bankWitnesses", {})
        if not isinstance(witnesses, dict) or not self._lightweight() and not set(banks).issubset(witnesses): raise RecoveryError("Invalid parked-bank witness.")
        for name in banks:
            if self._lightweight(): continue
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
        if self._lightweight():
            self._park_small_data_banks(); return
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

    def _park_small_data_banks(self):
        """Keep game banks with bounded metadata and real renameat, or redeploy."""
        self.shell("mkdir -p " + shlex.quote(self.park) + "\ntest ! -L " + shlex.quote(self.park))
        probe = EXTERNAL + "/files/.quest-signing-probe-" + self.journal["transaction"]
        destination = self.park + "/probe"
        token = self.journal["transaction"]
        for prior in (probe, destination):
            # A killed host can leave the tiny probe on either side. Its exact
            # transaction marker permits targeted cleanup before the next test;
            # rmdir refuses any unexpected additional member.
            self.shell("if test -e " + shlex.quote(prior) + "; then test -d " + shlex.quote(prior)
                       + "; test ! -L " + shlex.quote(prior) + "; test -f " + shlex.quote(prior + "/marker")
                       + "; test ! -L " + shlex.quote(prior + "/marker") + "; test \"$(cat " + shlex.quote(prior + "/marker")
                       + ")\" = " + shlex.quote(token) + "; rm " + shlex.quote(prior + "/marker") + "; rmdir " + shlex.quote(prior) + "; fi")
        self.shell("test ! -e " + shlex.quote(probe) + "\ntest ! -e " + shlex.quote(destination)
                   + "\nmkdir " + shlex.quote(probe) + "\nprintf %s " + shlex.quote(token) + " > " + shlex.quote(probe + "/marker"))
        moved = False
        try:
            try: moved = self._native_rename(probe, destination)
            except RuntimeError as error:
                # Architecture/exec-policy/tool transfer failures may preclude
                # the primitive. A live, still-installed headset is required to
                # select targeted content redeployment instead of any copying.
                self._device_guard()
                with self.adb.log.open("a", encoding="utf-8") as log:
                    log.write(json.dumps({"nativeRenameUnavailable": str(error)[:1024], "copiesPayload": False}) + "\n")
                moved = False
            if moved:
                self.shell("test ! -e " + shlex.quote(probe) + "\ntest \"$(cat " + shlex.quote(destination + "/marker") + ")\" = " + shlex.quote(token))
                if not self._native_rename(destination, probe):
                    raise RecoveryError("The native two-way content rename probe cannot be returned; the installed app was retained.")
                moved = False
                self.shell("test ! -e " + shlex.quote(destination) + "\ntest \"$(cat " + shlex.quote(probe + "/marker") + ")\" = " + shlex.quote(token))
            else:
                self._require_content_reinstall_choice()
                self.commit("parked", parkingMode="reinstall")
                progress("replace", 3, 7, "Saves retained; game assets require normal content installation because Android cannot rename their directory")
                return
        finally:
            # Only this unique empty probe is removed; never a game directory.
            for root in ((destination,) if moved else (probe,)):
                self.shell("if test -f " + shlex.quote(root + "/marker") + "; then test \"$(cat " + shlex.quote(root + "/marker")
                           + ")\" = " + shlex.quote(token) + "; rm " + shlex.quote(root + "/marker") + "; rmdir " + shlex.quote(root) + "; fi")
        self.commit(parkingMode="atomic")
        for name in self.journal["banks"]:
            source, target = EXTERNAL + "/" + name, self.park + "/" + name.split("/")[-1]
            self.commit(movingBank=name)
            existence = self.shell("test ! -L " + shlex.quote(source) + "\ntest ! -L " + shlex.quote(target)
                                   + "\nif test -d " + shlex.quote(target) + "; then test ! -e " + shlex.quote(source)
                                   + "; printf parked; else test -d " + shlex.quote(source) + "; printf source; fi")
            if existence == "source" and not self._native_rename(source, target):
                # Android may allow the small probe but refuse a protected bank.
                # Return earlier successful renames before selecting redeploy.
                self._restore_banks()
                self._require_content_reinstall_choice()
                self.commit("parked", parkingMode="reinstall", movingBank=None)
                progress("replace", 3, 7, "Saves retained; Android requires normal installation of the game assets")
                return
            self.shell("test -d " + shlex.quote(target) + "\ntest ! -e " + shlex.quote(source))
        self.commit("parked", movingBank=None)
        progress("replace", 3, 7, "Game directories retained on the Quest without copying or hashing assets")

    def _restore_banks(self):
        self._device_guard()
        if self.journal.get("parkingMode") in ("none", "reinstall"): return
        for name in self.journal["banks"]:
            self._device_guard()
            target, source = EXTERNAL + "/" + name, self.park + "/" + name.split("/")[-1]
            self.commit(restoringBank=name)
            if self._lightweight():
                self.shell("mkdir -p " + shlex.quote(EXTERNAL + "/files") + "\ntest ! -L " + shlex.quote(source)
                           + "\ntest ! -L " + shlex.quote(target))
                exists = self.shell("if test -d " + shlex.quote(source) + "; then printf parked; elif test -d " + shlex.quote(target) + "; then printf restored; else printf absent; fi")
                if exists == "parked":
                    self.shell("test ! -e " + shlex.quote(target))
                    if not self._native_rename(source, target):
                        raise RecoveryError("The retained game directory cannot be returned without copying; retained saves and game data remain available for retry.")
                elif exists == "absent":
                    # A failed second park may precede moving this bank.
                    raise RecoveryError("A retained game directory is missing; saves and remaining bank data were retained.")
                continue
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
                self._transfer(self._restore_tar_args(item["root"], internal=item["internal"], access_mode=item.get("accessMode")), restore, restore=True, phase="restore", total=restore.stat().st_size)
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
                                "apkSha256": self.source.sha256, "inputKey": self.source.input_key, "state": "created",
                                "dataPolicy": self.data_policy, "smallDataOnly": self.data_policy == "saves",
                                "selectedSourceStat": list(self._source_fingerprint)}
                self.commit()
            print("Retained signing migration backup: " + str(self.backup), flush=True)
            if self.journal["state"] == "created": self._backup()
            else: self._repair_retained_archives()
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
                        if self._lightweight():
                            # The finished replacement APK remains local. Retry
                            # its installation with retained small save archives;
                            # no multi-GB original APK rollback copy is required.
                            raise CaptureRepairError("The replacement APK could not yet be installed; saves, settings and retained game directories are safe. Retry installation without rebuilding.",
                                                     root="external", reason="replacement-install", attempts=1, total=1)
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
            if isinstance(error, DataPolicyError):
                print("GHVRQ_ACTION " + json.dumps({"schema": 1, "kind": "signing-data-policy", "reason": error.reason,
                                                  "policy": self.data_policy, "saveDataProtected": True}), flush=True)
            elif isinstance(error, CaptureRepairError):
                print("GHVRQ_ACTION " + json.dumps(error.action), flush=True)
            elif isinstance(error, ContentReinstallChoiceError):
                print("GHVRQ_ACTION " + json.dumps(error.action), flush=True)
            directory = str(getattr(self, "backup", self.home))
            raise RecoveryError("Signing migration could not safely continue: " + str(error)
                                + ". Original recovery data is retained at " + directory + ". Retry with the same APK and headset.") from error
        finally:
            failed = sys.exc_info()[0] is not None
            try: self._close_storage_process()
            except RuntimeError as cleanup:
                if not failed: raise
                print("Signing recovery storage cleanup could not be confirmed: " + str(cleanup)[:1024], flush=True)
            if self._rename_helper:
                try:
                    self._device_guard()
                    self.shell("test ! -L " + shlex.quote(self._rename_helper) + "\nrm -f " + shlex.quote(self._rename_helper))
                    self._rename_helper = None
                except RuntimeError:
                    print("Signing recovery retained its tiny transaction-owned rename helper for later cleanup.", flush=True)

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
