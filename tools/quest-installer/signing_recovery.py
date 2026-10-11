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

    def _transfer(self, args, local, *, restore=False, phase="backup", total=None):
        """Binary ADB transfer with bounded memory and observed byte counters."""
        command = [self.adb.executable, "-s", self.address, *map(str, args)]
        ordinary(local)
        if local.exists() and (not local.is_file() or local.stat().st_nlink != 1):
            raise RecoveryError("A recovery archive is not an ordinary independent file.")
        with self.adb.log.open("a", encoding="utf-8") as log:
            log.write(json.dumps({"binaryCommand": command, "backup": str(local), "restore": restore}) + "\n")
        stop = threading.Event()
        with local.open("rb" if restore else "wb") as stream, tempfile.TemporaryFile() as stderr:
            def report():
                while not stop.wait(1):
                    observed = stream.tell() if restore else local.stat().st_size
                    progress(phase, observed, total, "Restoring retained app data" if restore else "Saving retained app data", unit="bytes", local=True)
            monitor = threading.Thread(target=report, daemon=True); monitor.start()
            try:
                options = {"stderr": stderr, "timeout": 1800, "shell": False}
                if restore: options.update(stdin=stream, stdout=subprocess.DEVNULL)
                else: options.update(stdout=stream, stdin=subprocess.DEVNULL)
                result = self.adb.runner(command, **options)
            finally:
                stop.set(); monitor.join()
            stderr.seek(0, 2); size = stderr.tell(); stderr.seek(max(0, size - 16384))
            message = stderr.read(16384).decode("utf-8", errors="replace")
        with self.adb.log.open("a", encoding="utf-8") as log: log.write(message + "\n")
        if result.returncode: raise RecoveryError("ADB data transfer failed: " + message[:1200])
        if not restore and re.search(r"\b(ignored|socket|changed|cannot|denied)\b", message, re.I):
            raise RecoveryError("Android did not capture every app-data entry: " + message[:1200])
        progress(phase, local.stat().st_size, total, "Retained app-data transfer finished", unit="bytes", local=True)

    def _tar_args(self, root, *, internal=False, restore=False):
        command = ["shell", "-T"] + (["run-as", PACKAGE] if internal else [])
        # Android toybox tar otherwise attempts to restore the old app UID.
        # -o keeps ownership with the new run-as UID after package replacement.
        command += ["tar", "-C", root, "-xof" if restore else "-cf", "-"]
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

    def _bank_is_immutable(self, remote):
        if self.decode_receipt is None: return False
        local = self.backup / (remote.rsplit("/", 1)[1] + ".receipt")
        try:
            self.adb.run("-s", self.address, "pull", remote + "/.quest-installation.receipt", local, timeout=60, check_text=False)
            if not local.is_file() or local.stat().st_size > 16 * 1024 * 1024: return False
            receipt = self.decode_receipt(local.read_bytes())
            if not receipt or not isinstance(receipt.get("files"), dict): return False
            allowed = {remote + "/.quest-installation.receipt", remote + "/.quest-installation.receipt.previous", remote + "/.quest-installation.receipt.pending"}
            for name in receipt["files"]:
                if name.startswith("/") or any(part in ("", ".", "..") for part in name.split("/")): return False
                allowed.add(remote + "/" + name)
                allowed.update(remote + "/" + str(parent) for parent in PurePosixPath(name).parents if str(parent) != ".")
            found = self.shell("find " + shlex.quote(remote) + " -mindepth 1 -print").splitlines()
            if not set(found).issubset(allowed): return False
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
        if destination.exists() and (not destination.is_file() or destination.stat().st_nlink != 1): raise RecoveryError("Unsafe restoration archive.")
        # Previously restored terminal links remain legal, but no data write may
        # traverse any existing link. Validate each original non-link path and
        # its ancestors in bounded scripts; quote every original filename.
        checked = set()
        for name, value in item["inventory"].items():
            parents = [str(parent) for parent in PurePosixPath(name).parents if str(parent) != "."]
            if value["type"] != "symlink": parents.append(name)
            checked.update(item["root"] + "/" + parent for parent in parents)
        paths = sorted(checked)
        for offset in range(0, len(paths), 128):
            script = "set -eu\n" + "\n".join("test ! -L " + shlex.quote(path) for path in paths[offset:offset + 128]) + "\n"
            args = ["-s", self.address, "shell", "-T"] + (["run-as", PACKAGE] if item["internal"] else []) + ["sh", "-s"]
            self.adb.run(*args, input_text=script, check_text=False)
        # Android toybox tar refuses existing links instead of overwriting them.
        # Remove only original terminal links after verifying their exact target.
        links_to_remove = [(item["root"] + "/" + name, value["target"]) for name, value in item["inventory"].items() if value["type"] == "symlink"]
        for offset in range(0, len(links_to_remove), 64):
            lines = []
            for path, target in links_to_remove[offset:offset + 64]:
                quoted = shlex.quote(path)
                lines.append("if test -L " + quoted + "; then test \"$(readlink " + quoted + ")\" = " + shlex.quote(target)
                             + "; rm " + quoted + "; else test ! -e " + quoted + "; fi")
            args = ["-s", self.address, "shell", "-T"] + (["run-as", PACKAGE] if item["internal"] else []) + ["sh", "-s"]
            self.adb.run(*args, input_text="set -eu\n" + "\n".join(lines) + "\n", check_text=False)
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
            candidate = self.backup / (item["name"] + "-" + uuid.uuid4().hex + ".tar")
            self._transfer(self._tar_args(item["root"], internal=item["internal"]), candidate)
            inventory = archive_inventory(candidate)
            if inventory == item["inventory"]:
                candidate.unlink(); continue
            verify = self.backup / (item["name"] + ".refresh-verify.tar")
            try:
                self._transfer(self._tar_args(item["root"], internal=item["internal"]), verify)
                if archive_inventory(verify) != inventory: raise RecoveryError("App data continues changing after force-stop; the installed app was retained.")
            finally: verify.unlink(missing_ok=True)
            item.update(archive=candidate.name, inventory=inventory, sha256=file_hash(candidate))
            self.commit(roots=self.journal["roots"])
            progress("backup", index + 1, len(self.journal["roots"]), "Updated backup retains changes made since the previous attempt", unit="files", local=True)
        return True

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
        # Backup the entire mutable app-specific external root, not an allowlist
        # of known save names. Unknown future save/config files remain protected.
        sizes = self.shell("if test -d " + shlex.quote(EXTERNAL) + "; then du -sk " + shlex.quote(EXTERNAL) + "; else printf '0\\tmissing\\n'; fi\n"
                           + "if test -d /sdcard/Android/obb/" + PACKAGE + "; then if test -n \"$(ls -A /sdcard/Android/obb/" + PACKAGE + ")\"; "
                           + "then printf '%s\\n' 'Additional app OBB data is not yet supported by automatic signing recovery; the installed app was retained.' >&2; exit 52; fi; fi")
        root_size = re.match(r"^(\d+)\s", sizes)
        if not root_size: raise RecoveryError("Available recovery storage could not be estimated.")
        external_exists = not sizes.strip().endswith("missing")
        # Exact immutable bank sizes are removed from the host-space requirement.
        banks = []
        for name in BANKS:
            remote = EXTERNAL + "/" + name
            exists = self.shell("if test -d " + shlex.quote(remote) + " && ! test -L " + shlex.quote(remote) + "; then du -sk " + shlex.quote(remote) + "; fi")
            if exists:
                match = re.match(r"^(\d+)\s", exists)
                if not match: raise RecoveryError("A retained content-bank size could not be verified.")
                # Only receipt-owned native banks with no unknown descendants
                # may be excluded from the complete mutable-data backup.
                if self._bank_is_immutable(remote): banks.append(name)
        private_size = self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "du", "-sk", private, check_text=False)
        match = re.match(r"^(\d+)\s", private_size)
        if not match: raise RecoveryError("The private-data backup size could not be estimated.")
        external_bytes = int(root_size[1]) * 1024
        for name in banks:
            bank_size = self.shell("du -sk " + shlex.quote(EXTERNAL + "/" + name))
            external_bytes -= int(re.match(r"^(\d+)\s", bank_size)[1]) * 1024
        mutable_sizes = [max(0, external_bytes), int(match[1]) * 1024]
        roots = [{"name": "private", "root": private, "internal": True}]
        protected = "/data/user_de/0/" + PACKAGE
        device = self.adb.run("-s", self.address, "shell", "-T", "run-as", PACKAGE, "sh", "-s",
                              input_text="set -eu\nif test -d " + shlex.quote(protected) + "; then test -r " + shlex.quote(protected)
                              + " && test -x " + shlex.quote(protected) + " && printf present; "
                              + "elif ls -ld /data/user_de/0 >/dev/null 2>&1; then printf absent; "
                              + "else printf '%s\\n' 'Device-protected app-data access was denied; the installed app was retained.' >&2; exit 51; fi\n", check_text=False)
        if device not in ("present", "absent"): raise RecoveryError("Device-protected app-data access could not be verified.")
        if device == "present": roots.append({"name": "device-protected", "root": protected, "internal": True})
        if device == "present":
            size = self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "du", "-sk", protected, check_text=False)
            parsed = re.match(r"^(\d+)\s", size)
            if not parsed: raise RecoveryError("Device-protected app-data size could not be estimated.")
            mutable_sizes.append(int(parsed[1]) * 1024)
        if external_exists: roots.append({"name": "external", "root": EXTERNAL, "internal": False})
        required = original_size + sum(mutable_sizes) + max(mutable_sizes) * 2 + 64 * 1024 * 1024
        if shutil.disk_usage(self.backup).free < required:
            raise RecoveryError("Insufficient PC storage for a verified rollback backup (required " + str(required) + " bytes); the installed app was retained.")
        lib = self.adb.run("-s", self.address, "shell", "-T", "run-as", PACKAGE, "sh", "-s",
                           input_text="set -eu\nif test -L " + shlex.quote(private + "/lib") + "; then readlink " + shlex.quote(private + "/lib") + "; fi\n", check_text=False)
        generated_lib = lib.startswith(str(PurePosixPath(installed_apk).parent) + "/lib/")
        self.commit(originalApk={"path": "original.apk", "sha256": original_hash, "size": original_size}, roots=roots, banks=banks, excludePrivateLib=generated_lib)
        original = self.backup / "original.apk"
        if not original.is_file() or original.stat().st_size != original_size or file_hash(original) != original_hash:
            self._transfer(["exec-out", "cat", installed_apk], original, total=original_size)
        if original.stat().st_size != original_size or file_hash(original) != original_hash:
            raise RecoveryError("The original APK backup failed verification; the installed app was retained.")
        for index, item in enumerate(roots):
            local = self.backup / (item["name"] + "-" + uuid.uuid4().hex + ".tar")
            self._transfer(self._tar_args(item["root"], internal=item["internal"]), local)
            inventory = archive_inventory(local)
            verify = self.backup / (item["name"] + ".verify.tar")
            try:
                self._transfer(self._tar_args(item["root"], internal=item["internal"]), verify)
                if archive_inventory(verify) != inventory: raise RecoveryError("App data changed during its backup; the installed app was retained.")
            finally: verify.unlink(missing_ok=True)
            item.update(archive=local.name, sha256=file_hash(local), inventory=inventory)
            self.commit(roots=roots)
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
        self.adb.run("-s", self.address, "shell", "am", "force-stop", PACKAGE)
        private = self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "pwd", check_text=False).strip()
        if private not in ("/data/user/0/" + PACKAGE, "/data/data/" + PACKAGE): raise RecoveryError("The installed replacement does not permit run-as restoration; all original backups remain retained.")
        for index, item in enumerate(self.journal["roots"]):
            local = self.backup / item["archive"]
            if item["internal"]:
                self.adb.run("-s", self.address, "shell", "run-as", PACKAGE, "mkdir", "-p", item["root"], check_text=False)
            else: self.shell("mkdir -p " + shlex.quote(item["root"]))
            self.commit(restoringArchive=item["archive"])
            restore = self.backup / (item["name"] + ".restore.tar")
            self._safe_restore_archive(local, restore, item)
            try:
                self._transfer(self._tar_args(item["root"], internal=item["internal"], restore=True), restore, restore=True, phase="restore", total=restore.stat().st_size)
            finally: restore.unlink(missing_ok=True)
            progress("restore", index + 1, len(self.journal["roots"]), "Restored " + item["name"] + " app data", unit="files", local=True)
        self._restore_banks()
        self.commit(restoringArchive=None)
        progress("restore", 5, 7, "Retained saves, settings and content restored")
        for item in self.journal["roots"]:
            verify = self.backup / (item["name"] + ".restored.tar")
            try:
                self._transfer(self._tar_args(item["root"], internal=item["internal"]), verify, phase="restore")
                restored = archive_inventory(verify)
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
            if self.journal["state"] in ("backed-up", "parking"):
                try: self._park_banks()
                except Exception:
                    # A denied second bank move must not leave the still-installed
                    # old app without its first bank. Original data remains durable.
                    self._restore_banks(); self.commit("backed-up"); raise
            if self.journal["state"] == "parked":
                self.commit("uninstalling")
            if self.journal["state"] == "uninstalling":
                installed = self.adb.run("-s", self.address, "shell", "pm", "path", "--user", "0", PACKAGE, check_text=False)
                if installed.startswith("package:"):
                    self._refresh_before_uninstall()
                    scratch = max((self.backup / item["archive"]).stat().st_size for item in self.journal["roots"]) + 64 * 1024 * 1024
                    if shutil.disk_usage(self.backup).free < scratch:
                        raise RecoveryError("Insufficient PC storage for restoration scratch; the installed app was retained.")
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
                        self._restore(expected_apk=self.journal["originalApk"]["sha256"]); self.commit("backed-up", rolledBack=True)
                    raise
                self.commit("restoring")
                progress("replace", 4, 7, "Replacement APK installed; restoring retained data before launch")
            if self.journal["state"] == "restoring":
                self._restore(); self.commit("verified")
            if self.journal["state"] in ("rolling-back", "rollback-restoring"):
                self._device_guard()
                if self.journal["state"] == "rolling-back":
                    self.adb.run("-s", self.address, "install", "-r", self.backup / "original.apk", timeout=self.install_timeout, positive=r"^Success\s*$")
                    self.commit("rollback-restoring")
                self._restore(expected_apk=self.journal["originalApk"]["sha256"]); self.commit("backed-up", rolledBack=True)
                raise RecoveryError("Interrupted rollback has restored the original app and data; retry the selected new APK.")
            return {"schema": 1, "transaction": self.journal["transaction"], "backupPath": str(self.backup),
                    "verified": self.journal["state"] in ("verified", "complete")}
        except Exception as error:
            directory = str(getattr(self, "backup", self.home))
            raise RecoveryError("Signing migration could not safely continue: " + str(error)
                                + ". Original recovery data is retained at " + directory + ". Retry with the same APK and headset.") from error

    def complete(self):
        if self.journal and self.journal["state"] == "verified":
            self.commit("complete")
            progress("verify", 7, 7, "Signing migration and original app-data restoration verified")
            if self.journal.get("parkingMode") == "copy":
                # Success is already committed. Device staging is reproducible
                # and no longer needed; canonical host backups stay untouched.
                try:
                    for name in self.journal["banks"]:
                        path = self.park + "/" + name.split("/")[-1]
                        self.shell("test ! -L " + shlex.quote(path) + "\nrm -rf " + shlex.quote(path), timeout=120)
                    self.shell("rmdir " + shlex.quote(self.park), timeout=30)
                except RuntimeError:
                    print("Signing migration completed; retained device staging awaits cleanup: " + self.park, flush=True)
