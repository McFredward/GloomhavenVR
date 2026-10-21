"""Read an installed Quest player as the input of a compact PC-side update.

Only Package Manager's exact base.apk is transferred. External Campaign banks,
save data and private application storage are neither traversed nor copied. An
installed Android player is not a Unity/Mono source project: the update driver
must separately qualify its bounded original-assembly seed before compiling.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tempfile

PACKAGE = "dev.gloomhavenvr.quest"
REPO = Path(__file__).resolve().parents[2]
MAX_APK_BYTES = 8 * 1024 ** 3
MAX_RECORD_BYTES = 128 * 1024
CHUNK_BYTES = 8 * 1024 * 1024
BLOCK_BYTES = 1024 * 1024
DISK_MARGIN_BYTES = 64 * 1024 * 1024


class InstalledSourceError(RuntimeError):
    """An actionable installed-source qualification/transfer failure."""
    def __init__(self, message, reason="installed-source-unavailable"):
        super().__init__(message)
        self.reason = reason


class ImportCancelled(InstalledSourceError):
    def __init__(self):
        super().__init__("Quest APK import was paused; completed transfer chunks are retained.", "cancelled")


def _installer():
    name = "_ghvr_quest_installed_source_installer"
    if name not in sys.modules:
        spec = importlib.util.spec_from_file_location(name, REPO / "tools/quest-installer/installer.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        try: spec.loader.exec_module(module)
        except BaseException:
            sys.modules.pop(name, None)
            raise
    return sys.modules[name]


def _cancel(cancel):
    if cancel is not None and (cancel() if callable(cancel) else cancel.is_set()):
        raise ImportCancelled()


def _report(progress, phase, done, total, detail):
    if progress:
        progress({"phase": phase, "done": done, "total": total, "unit": "bytes", "detail": detail})


def _ordinary(path):
    """Reject links before mkdir/open; never resolve away an unsafe parent."""
    path = Path(os.path.abspath(path))
    for item in (path, *path.parents):
        try: info = item.lstat()
        except FileNotFoundError: continue
        if stat.S_ISLNK(info.st_mode) or getattr(item, "is_junction", lambda: False)():
            raise InstalledSourceError("Quest import paths cannot be links or junctions.", "unsafe-local-path")
        if item != path and not stat.S_ISDIR(info.st_mode):
            raise InstalledSourceError("Quest import parent is not a directory.", "unsafe-local-path")
        if item == path and stat.S_ISREG(info.st_mode) and info.st_nlink != 1:
            raise InstalledSourceError("Quest import files cannot have multiple hard links.", "unsafe-local-path")
    return path


def _stat(path):
    info = _ordinary(path).stat()
    if not stat.S_ISREG(info.st_mode):
        raise InstalledSourceError("Quest import requires an ordinary file.", "unsafe-local-path")
    return [info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns]


def _read(path):
    path = _ordinary(path)
    try:
        if path.stat().st_size > MAX_RECORD_BYTES: return None
        value = json.loads(path.read_bytes())
        return value if isinstance(value, dict) and value.get("schema") == 1 else None
    except (FileNotFoundError, ValueError, UnicodeError):
        return None


def _write(path, value):
    path = _ordinary(path)
    payload = (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")
    if len(payload) > MAX_RECORD_BYTES:
        raise InstalledSourceError("Installed-source receipt exceeds its bounded size.", "receipt-too-large")
    descriptor, name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload); stream.flush(); os.fsync(stream.fileno())
        _ordinary(path)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _remote_path(raw):
    if len(raw) > 4096:
        raise InstalledSourceError("Package Manager returned an excessive APK path.", "unsafe-package-path")
    lines = raw.strip().splitlines()
    if not lines: return None
    if len(lines) != 1 or not lines[0].startswith("package:"):
        raise InstalledSourceError("The installed Quest player has split APKs or an ambiguous package path; select a complete local Quest package instead.", "split-package")
    path = lines[0][8:]
    parts = PurePosixPath(path).parts
    if (not re.fullmatch(r"/data/app/[A-Za-z0-9_./=+~\-]+/base\.apk", path)
            or any(part in (".", "..") for part in path.split("/"))
            or "//" in path or len(parts) not in (5, 6)
            or not parts[-2].startswith(PACKAGE + "-")):
        raise InstalledSourceError("Package Manager returned an unsafe Quest APK path; no device files were read.", "unsafe-package-path")
    return path


def inspect_installed(adb, address, hardware=None):
    """Return cheap, read-only installed-player facts; do not read APK contents.

    The caller selects USB/Wi-Fi through installer.choose_device and owns the
    ADB server lifetime. Every observation qualifies the stable hardware serial
    again, so a remembered IP address alone is never an installed-source proof.
    """
    actual = adb.identity(address)
    if hardware is not None and actual != hardware:
        raise InstalledSourceError("The connected Quest differs from the selected headset. Reconnect the same Quest or select another headset explicitly.", "different-headset")
    raw = adb.run("-s", address, "shell", "pm", "path", "--user", "0", PACKAGE,
                  timeout=30, check_text=False)
    path = _remote_path(raw)
    result = {"source": "quest", "available": path is not None, "package": PACKAGE,
              "address": address, "deviceSerial": actual}
    if path is None:
        result.update(reason="package-missing", action="Install a complete GloomhavenVR Quest player on this headset, or select a local Quest APK/package.")
        return result
    record = adb.run("-s", address, "shell", "dumpsys", "package", PACKAGE,
                     timeout=30, check_text=False)
    codes = re.findall(r"\bversionCode=(\d+)\b", record)
    names = re.findall(r"\bversionName=([^\s]+)", record)
    if (len(record) > MAX_RECORD_BYTES or len(codes) != 1 or len(names) != 1 or len(names[0]) > 256
            or not 0 < int(codes[0]) <= 2147483647):
        raise InstalledSourceError("Android did not return one unambiguous installed Quest version.", "ambiguous-package-version")
    remote_stat = adb.run("-s", address, "shell", "stat", "-c", "%s:%Y:%i:%f", path,
                          timeout=30, check_text=False)
    match = re.fullmatch(r"([0-9]{1,19}):([0-9]{1,19}):([0-9]{1,19}):([0-9a-fA-F]{1,8})", remote_stat.strip())
    if not match:
        raise InstalledSourceError("The installed APK size could not be read. Unlock and reconnect the Quest, then retry.", "package-stat-unavailable")
    size, modified, inode = map(int, match.groups()[:3])
    mode = int(match[4], 16)
    if not stat.S_ISREG(mode) or not 0 < size <= MAX_APK_BYTES or inode <= 0:
        raise InstalledSourceError("The installed Quest APK is not an ordinary bounded base file.", "invalid-package-file")
    witness = {"package": PACKAGE, "deviceSerial": actual, "remotePath": path,
               "versionCode": int(codes[0]), "versionName": names[0],
               "remoteStat": {"size": size, "modified": modified, "inode": inode, "mode": mode}}
    key = hashlib.sha256(json.dumps(witness, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    result.update(witness, size=size, witness=witness, witnessKey=key)
    stamp = re.fullmatch(r"0\.1\.0\.B([0-9]+)\.([0-9a-f]{12})", names[0])
    if stamp: result.update(modBuild=int(stamp[1]), inputKeyPrefix=stamp[2])
    return result


def _chunk(adb, address, remote, offset, count):
    """Use the installer's runner/server, retaining at most one 8-MiB chunk."""
    adb.ensure_owned_server()
    # exec-out's raw Android service merges stdout/stderr and does not return
    # the remote process status. Suppress dd's records footer on the device,
    # before it can corrupt APK bytes. All interpolated operands are either the
    # exact allowlisted Package Manager path or our integers. The ADB client
    # escapes the single sh -c script operand; no host shell is involved.
    script = ("exec dd if=" + remote + " bs=" + str(BLOCK_BYTES)
              + " skip=" + str(offset // BLOCK_BYTES)
              + " count=" + str((count + BLOCK_BYTES - 1) // BLOCK_BYTES) + " 2>/dev/null")
    command = [adb.executable, "-s", adb.transport(address), "exec-out", "sh", "-c", script]
    try:
        result = adb.runner(command, capture_output=True, text=False, timeout=120, shell=False)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise InstalledSourceError("Quest APK transfer was interrupted. Unlock/reconnect the same Quest and retry; completed chunks are retained.", "transfer-interrupted") from error
    output, stderr = result.stdout or b"", result.stderr or b""
    if not isinstance(output, bytes) or not isinstance(stderr, bytes):
        raise InstalledSourceError("Quest APK transfer did not preserve binary bytes.", "transfer-invalid")
    with adb.log.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps({"command": command, "exitCode": result.returncode,
                                 "receivedBytes": len(output), "expectedBytes": count,
                                 "stderr": stderr[-4096:].decode("utf-8", errors="replace")}) + "\n")
    if result.returncode or len(output) != count:
        raise InstalledSourceError("Quest APK transfer was incomplete. Reconnect the same Quest and retry; completed chunks are retained.", "transfer-interrupted")
    return output


def _qualified(adb, address, selected, apk, receipt, *, reused, resumed_bytes):
    local_before = _stat(apk)
    builder = _installer().builder_module()
    try: base = builder.apk_updates.inspect_base(apk)
    except (OSError, RuntimeError, ValueError) as error:
        raise InstalledSourceError("The installed APK lacks readable Quest update contracts. Select a complete Quest package or prepare a one-time update from the PC game installation. " + str(error), "invalid-update-source") from error
    identity = {key: base.get(key) for key in ("inputKey", "modBuild", "currentProfile", "currentMod")}
    if (type(identity["modBuild"]) is not int or identity["modBuild"] != selected["versionCode"]
            or selected.get("modBuild", identity["modBuild"]) != identity["modBuild"]
            or (selected.get("inputKeyPrefix") and not identity["inputKey"].startswith(selected["inputKeyPrefix"]))):
        raise InstalledSourceError("The transferred APK build does not match Android's installed package. Retry the selected Quest import.", "package-identity-mismatch")
    current = inspect_installed(adb, address, selected["deviceSerial"])
    if current.get("witnessKey") != selected["witnessKey"]:
        raise InstalledSourceError("The installed package changed during import. Retry to import its current version; the older transfer was not published.", "package-changed")
    # Older installed players remain discoverable. Missing compact original-DLL
    # input is an explicit one-time migration outcome, never a reason to infer or
    # reconstruct original Mono code from the native Android player.
    try: seed = builder._local_helper("update_seed").inspect_seed(apk, base)
    except (FileNotFoundError, ImportError, AttributeError):
        seed = {"available": False, "reason": "seed-reader-unavailable"}
    if _stat(apk) != local_before:
        raise InstalledSourceError("The local Quest APK changed while its update metadata was being inspected. Retry to reacquire that input from the headset.", "local-source-changed")
    return {**selected, "apk": str(apk), "path": str(apk), "identity": identity, "base": base,
            "seed": seed, "receipt": str(receipt), "reused": reused, "resumedBytes": resumed_bytes}


def acquire_installed(adb, address, state_root, *, hardware=None, progress=None, cancel=None):
    """Import only the exact base APK, with byte progress and retained chunks.

    Repeating discovery never hashes the APK. A closed import can be reused when
    both the current Android package witness and the local file stat witness are
    unchanged. Metadata is re-read with the production bounded APK validator.
    """
    _cancel(cancel)
    selected = inspect_installed(adb, address, hardware)
    if not selected["available"]:
        raise InstalledSourceError(selected["action"], selected["reason"])
    directory = _ordinary(Path(state_root) / "build/device-inputs" / selected["witnessKey"])
    directory.mkdir(parents=True, exist_ok=True)
    apk, partial = (_ordinary(directory / name) for name in ("base.apk", "base.apk.partial"))
    receipt, journal = (_ordinary(directory / name) for name in ("source.json", "transfer.json"))
    # Reuse the installer's reviewed short cross-platform file lock. The UI owns
    # normal job exclusion; this also protects a second CLI/window's same import.
    try:
        with _installer().input_cache_module()._lock(directory / "import.lock"):
            old = _read(receipt)
            if (old and old.get("witness") == selected["witness"] and apk.is_file()
                    and old.get("localStat") == _stat(apk) and apk.stat().st_size == selected["size"]):
                _cancel(cancel)
                result = _qualified(adb, address, selected, apk, receipt, reused=True, resumed_bytes=selected["size"])
                _report(progress, "quest-source-import", selected["size"], selected["size"], "Using the unchanged installed Quest APK already imported on this PC.")
                return result
            state = _read(journal)
            offset = 0
            if (state and state.get("witness") == selected["witness"] and partial.is_file()
                    and state.get("localStat") == _stat(partial)
                    and type(state.get("bytes")) is int and state["bytes"] == partial.stat().st_size
                    and 0 <= state["bytes"] <= selected["size"]
                    and (state["bytes"] % CHUNK_BYTES == 0 or state["bytes"] == selected["size"])):
                offset = state["bytes"]
            resumed = offset
            required = selected["size"] - offset + DISK_MARGIN_BYTES
            reclaimable = partial.stat().st_size if not offset and partial.is_file() else 0
            if shutil.disk_usage(directory).free + reclaimable < required:
                raise InstalledSourceError("Not enough PC disk space to import the installed APK. Free at least " + str(required) + " bytes in the Builder work drive, then retry; existing results are retained.", "insufficient-space")
            if not offset:
                with partial.open("wb"): pass
            _write(journal, {"schema": 1, "witness": selected["witness"], "bytes": offset, "localStat": _stat(partial)})
            _report(progress, "quest-source-import", offset, selected["size"], "Reading the installed base APK; game content banks and saves stay on the Quest.")
            with partial.open("ab") as stream:
                while offset < selected["size"]:
                    _cancel(cancel)
                    count = min(CHUNK_BYTES, selected["size"] - offset)
                    body = _chunk(adb, address, selected["remotePath"], offset, count)
                    _cancel(cancel)
                    stream.write(body); stream.flush(); os.fsync(stream.fileno())
                    offset += count
                    _write(journal, {"schema": 1, "witness": selected["witness"], "bytes": offset, "localStat": _stat(partial)})
                    _report(progress, "quest-source-import", min(offset, selected["size"] - 1), selected["size"], "Reading the installed base APK; completed chunks are saved for retry.")
            _cancel(cancel)
            result = _qualified(adb, address, selected, partial, receipt, reused=False, resumed_bytes=resumed)
            _cancel(cancel)
            _ordinary(apk)
            os.replace(partial, apk)
            _write(receipt, {"schema": 1, "source": "quest", "witness": selected["witness"], "localStat": _stat(apk), "identity": result["identity"], "seed": result["seed"]})
            journal.unlink(missing_ok=True)
            result.update(apk=str(apk), path=str(apk))
            result["base"]["resolvedApk"] = str(apk)
            _report(progress, "quest-source-import", selected["size"], selected["size"], "Installed Quest APK imported and update metadata qualified.")
            return result
    except OSError as error:
        raise InstalledSourceError("The local Quest APK import could not complete. Close another Builder using this source and retry; completed inputs are retained. " + str(error), "local-import-unavailable") from error
