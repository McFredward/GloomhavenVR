"""Portable PC-side transport for one signed APK and its complete content bank.

The ZIP is a transport container, not an Android APK. Its small manifest is a
hint; the selected APK's embedded delivery contract owns the bank identity.
Selection reads bounded metadata only. Export hashes while writing, import
copies only the APK, and nested bank readers seek directly in a STORED member.
Callers retain the normal APK signature, owned-game and signing-key checks.
"""
from __future__ import annotations

from contextlib import contextmanager
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import stat
import struct
import uuid
import zipfile
import zlib

import storage

APK = "GloomhavenVR-Quest.apk"
CONTENT = "GloomhavenVR-Quest-content.zip"
MANIFEST = "quest-install-package.json"
ARCHIVE = "quest-startup-content.zip"
PACKAGE = "dev.gloomhavenvr.quest"
FORMAT = "gloomhavenvr-quest-install-package"
CHUNK = 8 * 1048576
CHECKPOINT = 64 * 1048576
MAX_MANIFEST = 65536
MAX_INPUT_MANIFEST = 8 * 1048576
MAX_CONTENT = 64 * 1024 ** 3
MAX_APK = 4 * 1024 ** 3 - 1
MAX_METADATA = 2 * 1048576
SHA = re.compile(r"[0-9a-f]{64}\Z")
_SOURCE_OWNER = {"schema": 1, "owner": "Quest portable actual byte proofs"}


class Cancelled(storage.BuildError):
    """A requested stop preserves the last completed and resumable results."""


def _file(path):
    path = storage._ordinary_owned(Path(path).expanduser())
    value = path.lstat()
    if not stat.S_ISREG(value.st_mode) or getattr(value, "st_file_attributes", 0) & 0x400:
        raise storage.BuildError("Portable package input must be an ordinary file: " + path.name)
    return path


def _stamp(path):
    value = path.lstat()
    reliable = storage._file_witness_stamp(path, value) if value.st_nlink == 1 else None
    return {"identity": [value.st_dev, value.st_ino, value.st_size,
                          value.st_mtime_ns, value.st_ctime_ns],
            "reliable": list(reliable) if reliable is not None else None}


def _json(stream, limit, label):
    raw = stream.read(limit + 1)
    if len(raw) > limit: raise storage.BuildError(label + " exceeds its metadata bound.")
    try: value = json.loads(raw)
    except (ValueError, UnicodeError) as exc: raise storage.BuildError(label + " is invalid JSON.") from exc
    if not isinstance(value, dict): raise storage.BuildError(label + " must be a JSON object.")
    return value


def _json_path(path, limit=MAX_METADATA):
    with _file(path).open("rb") as stream: return _json(stream, limit, path.name)


def _cancel(cancel):
    if cancel and cancel(): raise Cancelled("Portable package operation cancelled; retained results remain available.")


def _progress(callback, phase, completed, total, detail, *, complete=False):
    if callback:
        percent = 100.0 if complete else min(99.9, completed * 100.0 / max(1, total))
        callback({"phase": phase, "completed": completed, "total": total,
                  "percent": percent, "detail": detail})


def _space(folder, required):
    folder.mkdir(parents=True, exist_ok=True)
    reserve = 64 * 1048576
    available = shutil.disk_usage(folder).free
    if available < required + reserve:
        raise storage.BuildError("Not enough free disk space for the portable package operation: "
                                 + str(required + reserve) + " bytes required, " + str(available) + " available.")


def _directory(stream, *, expected=None, max_entries=100000, max_size=32 * 1048576):
    """Bound ZIP's central directory before ZipFile allocates its members.

    Outer ZIP64 offsets are 64-bit. The locator is accepted only at its exact
    position immediately before EOCD; multi-disk and appended data are refused.
    """
    stream.seek(0, io.SEEK_END); length = stream.tell()
    stream.seek(max(0, length - 65557)); tail = stream.read(65557)
    position = tail.rfind(b"PK\x05\x06")
    if position < 0 or position + 22 > len(tail): raise storage.BuildError("Portable ZIP has no complete directory.")
    values = struct.unpack_from("<4s4H2IH", tail, position)
    _, disk, start_disk, count_disk, count, size, offset, comment = values
    eocd = length - len(tail) + position
    if position + 22 + comment != len(tail) or disk or start_disk or count_disk != count:
        raise storage.BuildError("Portable ZIP has unsupported appended or multi-disk data.")
    central_end = eocd
    if eocd >= 20:
        stream.seek(eocd - 20); locator = stream.read(20)
        if locator[:4] == b"PK\x06\x07":
            _, location_disk, record_offset, disks = struct.unpack("<4sIQI", locator)
            if location_disk or disks != 1 or record_offset + 56 != eocd - 20:
                raise storage.BuildError("Portable ZIP64 directory location is invalid.")
            stream.seek(record_offset); record = stream.read(56)
            if len(record) != 56: raise storage.BuildError("Portable ZIP64 directory is incomplete.")
            fields = struct.unpack("<4sQ2H2I4Q", record)
            if fields[0] != b"PK\x06\x06" or fields[1] != 44 or fields[4] or fields[5] or fields[6] != fields[7]:
                raise storage.BuildError("Portable ZIP64 directory is unsupported.")
            count, size, offset = fields[7:10]; central_end = record_offset
    if count > max_entries or size > max_size or offset + size != central_end:
        raise storage.BuildError("Portable ZIP central directory exceeds its bound or has invalid offsets.")
    if expected is not None and count != expected:
        raise storage.BuildError("Portable package must contain exactly its APK, content bank and manifest.")
    return offset


def _member_offset(stream, info, central):
    stream.seek(info.header_offset); header = stream.read(30)
    if len(header) != 30 or header[:4] != b"PK\x03\x04":
        raise storage.BuildError("Portable member has an invalid local header.")
    fields = struct.unpack("<4s5H3I2H", header)
    if fields[2] != info.flag_bits or fields[3] != zipfile.ZIP_STORED:
        raise storage.BuildError("Portable member flags or compression differ from its directory.")
    name = stream.read(fields[-2]); stream.read(fields[-1])
    expected = info.filename.encode("utf-8" if info.flag_bits & 0x800 else "cp437")
    offset = info.header_offset + 30 + fields[-2] + fields[-1]
    if name != expected or offset + info.file_size > central:
        raise storage.BuildError("Portable member name or extent differs from its directory.")
    return offset


class _BoundedFile(io.RawIOBase):
    """Own an independent file handle and map nested seeks to one member."""
    def __init__(self, path, offset, size):
        super().__init__()
        self.file = _file(path).open("rb")
        self.offset, self.size, self.position = offset, size, 0

    def readable(self): return True
    def seekable(self): return True
    def tell(self): self._checkClosed(); return self.position

    def seek(self, offset, whence=io.SEEK_SET):
        self._checkClosed()
        position = offset + (self.position if whence == io.SEEK_CUR else self.size if whence == io.SEEK_END else 0)
        if whence not in (io.SEEK_SET, io.SEEK_CUR, io.SEEK_END) or position < 0:
            raise ValueError("Invalid bounded content seek.")
        self.position = position
        return position

    def read(self, count=-1):
        self._checkClosed()
        count = min(max(0, self.size - self.position), count if count >= 0 else self.size)
        self.file.seek(self.offset + self.position)
        result = self.file.read(count); self.position += len(result)
        return result

    def readinto(self, buffer):
        result = self.read(len(buffer)); buffer[:len(result)] = result; return len(result)

    def close(self):
        if not self.closed: self.file.close()
        super().close()


@contextmanager
def _outer(path):
    path = _file(path)
    before = _stamp(path)
    try:
        with path.open("rb") as stream:
            central = _directory(stream, expected=3, max_entries=3, max_size=MAX_MANIFEST)
            with zipfile.ZipFile(stream) as archive:
                rows = archive.infolist()
                if {row.filename for row in rows} != {APK, CONTENT, MANIFEST} or len(rows) != 3:
                    raise storage.BuildError("Portable package contains duplicate or unsupported members.")
                intervals = []
                for row in rows:
                    mode = row.external_attr >> 16
                    bound = MAX_APK if row.filename == APK else MAX_CONTENT if row.filename == CONTENT else MAX_MANIFEST
                    if (row.flag_bits & ~0x808 or row.compress_type != zipfile.ZIP_STORED
                            or row.compress_size != row.file_size or not 0 < row.file_size <= bound
                            or stat.S_IFMT(mode) not in (0, stat.S_IFREG) or row.is_dir()):
                        raise storage.BuildError("Portable package contains linked, encrypted, compressed or oversized members.")
                    offset = _member_offset(stream, row, central)
                    intervals.append((row.header_offset, offset + row.file_size, row.filename, offset))
                ordered = sorted(intervals)
                if any(left[1] > right[0] for left, right in zip(ordered, ordered[1:])):
                    raise storage.BuildError("Portable package members overlap.")
                yield archive, {row[2]: row[3] for row in intervals}, before
        if _stamp(path) != before: raise storage.BuildError("Portable package changed while being read.")
    except (zipfile.BadZipFile, EOFError, struct.error, OSError) as exc:
        raise storage.BuildError("Portable package is incomplete or corrupt: " + path.name) from exc


def _delivery(stream):
    try: return _read_delivery(stream)
    except storage.BuildError: raise
    except (zipfile.BadZipFile, EOFError, RuntimeError, struct.error, OSError) as exc:
        raise storage.BuildError("APK signed delivery metadata is incomplete or corrupt.") from exc


def _read_delivery(stream):
    _directory(stream)
    with zipfile.ZipFile(stream) as archive:
        def read(name, bound=MAX_MANIFEST):
            rows = [row for row in archive.infolist() if row.filename == name]
            if len(rows) != 1 or rows[0].file_size > bound or rows[0].flag_bits & 1:
                raise storage.BuildError("APK lacks one bounded signed Quest delivery manifest: " + name)
            with archive.open(rows[0]) as member: return _json(member, bound, name)
        value = read("assets/Quest/content-delivery.json")
        identity = read("assets/Quest/input-manifest.json", MAX_INPUT_MANIFEST)
    rows = value.get("files")
    if (value.get("schema") != 1 or value.get("package") != PACKAGE
            or not SHA.fullmatch(str(value.get("inputKey", "")))
            or identity.get("schema") != 1 or identity.get("target") != "game"
            or identity.get("inputKey") != value["inputKey"]
            or not isinstance(rows, list) or len(rows) != 1 or not isinstance(rows[0], dict)):
        raise storage.BuildError("APK content delivery differs from its original game identity.")
    row = rows[0]
    if (row.get("file") != CONTENT or row.get("archive") != ARCHIVE
            or not SHA.fullmatch(str(row.get("sha256", "")))
            or type(row.get("size")) is not int or not 22 <= row["size"] <= MAX_CONTENT):
        raise storage.BuildError("APK content delivery has an invalid complete-game bank.")
    return dict(row)


def inspect(path):
    """Read bounded transport and nested APK metadata; do not hash the bank."""
    path = _file(path)
    with _outer(path) as (archive, offsets, _):
        with archive.open(MANIFEST) as stream: value = _json(stream, MAX_MANIFEST, MANIFEST)
        if (set(value) != {"schema", "format", "package", "apk", "content"}
                or value.get("schema") != 1 or value.get("format") != FORMAT or value.get("package") != PACKAGE):
            raise storage.BuildError("Portable package manifest has an unsupported format.")
        for name, field in ((APK, "apk"), (CONTENT, "content")):
            row = value.get(field)
            keys = {"file", "size", "sha256"} | ({"archive"} if field == "content" else set())
            if (not isinstance(row, dict) or set(row) != keys or row.get("file") != name
                    or type(row.get("size")) is not int or row["size"] != archive.getinfo(name).file_size
                    or not SHA.fullmatch(str(row.get("sha256", "")))):
                raise storage.BuildError("Portable member differs from its bounded manifest: " + name)
        with _BoundedFile(path, offsets[APK], value["apk"]["size"]) as stream: delivery = _delivery(stream)
        if value["content"] != delivery:
            raise storage.BuildError("Portable content manifest differs from the APK's signed delivery contract.")
        return value


@contextmanager
def _cache(output):
    """Cache availability is optional; no corrupt/link cache can stop a copy."""
    database = Path(output) / "cache/portable-witnesses.sqlite3"
    connection = None
    try:
        database = storage._ordinary_owned(database)
        database.parent.mkdir(parents=True, exist_ok=True)
        for suffix in ("", "-wal", "-shm"):
            path = storage._ordinary_owned(Path(str(database) + suffix))
            if path.exists() and (not path.is_file() or path.stat().st_size > 16 * 1048576 or path.stat().st_nlink != 1):
                raise OSError("Unsafe portable observation cache.")
        for attempt in range(2):
            try:
                connection = sqlite3.connect(database, timeout=30)
                connection.execute("CREATE TABLE IF NOT EXISTS portable_sources (sha TEXT PRIMARY KEY, value TEXT NOT NULL)")
                connection.execute("CREATE TABLE IF NOT EXISTS portable_members (identity TEXT PRIMARY KEY, value TEXT NOT NULL)")
                connection.execute("CREATE TABLE IF NOT EXISTS portable_exports (destination TEXT PRIMARY KEY, value TEXT NOT NULL)")
                break
            except sqlite3.DatabaseError:
                if connection: connection.close(); connection = None
                if attempt: raise
                suffix = ".damaged-" + uuid.uuid4().hex
                for extension in ("", "-wal", "-shm"):
                    old = Path(str(database) + extension)
                    if old.exists(): old.replace(Path(str(old) + suffix))
    except (OSError, sqlite3.DatabaseError, storage.BuildError):
        if connection is not None: connection.close()
        connection = sqlite3.connect(":memory:")
        connection.execute("CREATE TABLE portable_sources (sha TEXT PRIMARY KEY, value TEXT NOT NULL)")
        connection.execute("CREATE TABLE portable_members (identity TEXT PRIMARY KEY, value TEXT NOT NULL)")
        connection.execute("CREATE TABLE portable_exports (destination TEXT PRIMARY KEY, value TEXT NOT NULL)")
    try:
        yield connection
        for table in ("portable_sources", "portable_members", "portable_exports"):
            connection.execute("DELETE FROM " + table + " WHERE rowid NOT IN (SELECT rowid FROM " + table + " ORDER BY rowid DESC LIMIT 128)")
        connection.commit()
    finally: connection.close()


def _remember_source(output, source):
    row = {**source, "path": str(source["path"])}
    with _cache(output) as connection:
        connection.execute("INSERT OR REPLACE INTO portable_sources VALUES (?,?)", (source["sha256"], json.dumps(row)))


def _known_source(output, sha):
    with _cache(output) as connection:
        value = connection.execute("SELECT value FROM portable_sources WHERE sha=?", (sha,)).fetchone()
    if value:
        try:
            row = json.loads(value[0])
            return row if isinstance(row, dict) else None
        except ValueError: pass
    return None


def _source_from_package(path, expected):
    path = _file(path)
    value = inspect(path)
    if value["content"] != expected: raise storage.BuildError("Selected package contains another game's content bank.")
    return {"path": path, "member": CONTENT, "size": expected["size"],
            "sha256": expected["sha256"], "archive": expected["archive"]}


def _physical(path, expected):
    path = _file(path)
    if path.stat().st_size != expected["size"]:
        raise storage.BuildError("Complete-game content bank size differs from the signed APK contract.")
    return {"path": path, "member": None, "size": expected["size"],
            "sha256": expected["sha256"], "archive": expected["archive"]}


def _receipt_paths(output, expected):
    """Inspect bounded known artifact receipts, never walk game/import trees."""
    output = storage._ordinary_owned(Path(output))
    files = [output / "latest-build.json"]
    directory = storage._ordinary_owned(output / "receipts/build")
    if directory.is_dir():
        # Keep metadata work bounded even if old results have accumulated.
        candidates = []
        with os.scandir(directory) as entries:
            for index, entry in enumerate(entries):
                if index >= 4096: break
                if entry.name.endswith(".json") and entry.is_file(follow_symlinks=False):
                    candidates.append((entry.stat(follow_symlinks=False).st_mtime_ns, Path(entry.path)))
        files.extend(path for _, path in sorted(candidates, reverse=True)[:128])
    for file in files:
        try:
            value = _json_path(file)
            rows = value.get("details", {}).get("contentFiles", [])
            if not isinstance(rows, list) or len(rows) > 8: continue
            for row in rows:
                if (isinstance(row, dict) and row.get("sha256") == expected["sha256"]
                        and row.get("size") == expected["size"] and isinstance(row.get("path"), str)):
                    relative = row["path"]
                    if (not relative or relative.startswith("/") or "\\" in relative or ":" in relative
                            or any(part in ("", ".", "..") for part in relative.split("/"))): continue
                    path = storage._ordinary_owned(output / row["path"])
                    if output not in path.parents: continue
                    try: yield _physical(path, expected)
                    except (OSError, storage.BuildError): continue
        except (OSError, ValueError, AttributeError, storage.BuildError): continue


def _closed_source(apk, output, expected):
    """Follow at most eight exact closed update owners after cache cleanup.

    A closed receipt is a locator hint, never a new byte/signature proof. The
    selected APK's delivery identity stays authoritative; every hinted source
    must declare exactly that same bank before any reader is returned. This
    avoids scanning imports or rebuilding a bank just because its optional
    SQLite lookup cache was removed after a successful update.
    """
    output = storage._ordinary_owned(Path(output)); selected = Path(apk)
    seen = set()
    for _ in range(8):
        try:
            selected = _file(selected)
            if selected.suffix.lower() != ".apk" or selected in seen: return None
            seen.add(selected)
            with selected.open("rb") as stream:
                if _delivery(stream) != expected: return None
            bank = selected.parent / CONTENT
            if bank.is_file():
                try: return _physical(bank, expected)
                except (OSError, storage.BuildError): pass
            descriptor = selected.parent / "portable-source.json"
            if descriptor.exists():
                try:
                    value = _json_path(descriptor)
                    if value.get("schema") == 1 and value.get("content") == expected:
                        return _source_from_package(value["sourcePackage"], expected)
                except (OSError, KeyError, TypeError, ValueError, storage.BuildError): pass
            relative = selected.relative_to(output)
            if (len(relative.parts) != 3 or relative.parts[0] not in ("builds", "updates")
                    or not SHA.fullmatch(relative.parts[1])): return None
            value = _json_path(output / "receipts/build" / (relative.parts[1] + ".json"))
            details = value.get("details"); rows = value.get("outputs")
            if (value.get("schema") != 1 or value.get("stage") != "build"
                    or value.get("key") != relative.parts[1] or not isinstance(details, dict)
                    or details.get("package") != PACKAGE or details.get("isDiagnostic") is not False
                    or not isinstance(rows, list) or not 0 < len(rows) <= 64): return None
            matches = [row for row in rows if isinstance(row, dict) and row.get("path") == relative.as_posix()]
            if (len(matches) != 1 or matches[0].get("size") != selected.stat().st_size
                    or matches[0].get("sha256") != details.get("apkSha256")
                    or not SHA.fullmatch(str(details.get("apkSha256", "")))): return None
            package = details.get("sourcePackage")
            if isinstance(package, str):
                try: return _source_from_package(package, expected)
                except (OSError, TypeError, storage.BuildError): pass
            base = details.get("baseApk")
            if not isinstance(base, str) or not base: return None
            selected = Path(base)
        except (OSError, ValueError, AttributeError, storage.BuildError): return None
    return None


def content_source(apk, output, source_package=None):
    """Find the exact signed bank without hashing or materializing its bytes."""
    apk = _file(apk)
    with apk.open("rb") as stream: expected = _delivery(stream)
    if source_package is not None: return _source_from_package(source_package, expected)
    sibling = apk.parent / CONTENT
    if sibling.exists():
        result = _physical(sibling, expected); _remember_source(output, result); return result
    descriptor = apk.parent / "portable-source.json"
    if descriptor.exists():
        try:
            row = _json_path(descriptor)
            if row.get("schema") == 1 and row.get("content") == expected:
                result = _source_from_package(row["sourcePackage"], expected)
                _remember_source(output, result); return result
        except (OSError, KeyError, TypeError, ValueError, storage.BuildError): pass
    row = _known_source(output, expected["sha256"])
    if row:
        try:
            if row.get("member") == CONTENT: return _source_from_package(row["path"], expected)
            if row.get("member") is None: return _physical(row["path"], expected)
        except (OSError, KeyError, TypeError, storage.BuildError): pass
    result = _closed_source(apk, output, expected)
    if result is not None:
        _remember_source(output, result); return result
    for result in _receipt_paths(output, expected):
        _remember_source(output, result); return result
    raise storage.BuildError("The complete-game content bank is missing. Select its original portable ZIP or retain the matching .ghvrq build output.")


@contextmanager
def open_content(source):
    """Yield a seekable reader. Opening a nested bank reads no preceding data."""
    path = _file(source["path"])
    before = _stamp(path)
    if source.get("member") is None:
        if path.stat().st_size != source["size"]: raise storage.BuildError("Content bank size changed.")
        with path.open("rb") as stream: yield stream
    elif source.get("member") == CONTENT:
        with _outer(path) as (archive, offsets, _):
            row = archive.getinfo(CONTENT)
            if row.file_size != source["size"]: raise storage.BuildError("Portable content bank size changed.")
            with _BoundedFile(path, offsets[CONTENT], row.file_size) as stream: yield stream
    else: raise storage.BuildError("Unsupported portable content member.")
    if _stamp(path) != before: raise storage.BuildError("Content source changed during use.")


def _member_proof(source):
    path = _file(source["path"]); stamp = _stamp(path)
    value = {"path": str(path), "member": source.get("member"), "size": source["size"],
             "sha256": source["sha256"], "stamp": stamp["reliable"]}
    return storage.value_hash({"path": str(path), "member": source.get("member")}), value


def _save_proof(connection, source, *, stamp=None):
    key, value = _member_proof(source)
    if stamp is not None and value["stamp"] != stamp: return
    if value["stamp"] is not None:
        value["check"] = storage.value_hash(value)
        connection.execute("INSERT OR REPLACE INTO portable_members VALUES (?,?)", (key, json.dumps(value)))


def qualify_content(source, output, progress=None, cancel=None):
    """Optional actual whole-bank proof, only for callers needing archive SHA."""
    key, expected = _member_proof(source)
    with _cache(output) as connection:
        row = connection.execute("SELECT value FROM portable_members WHERE identity=?", (key,)).fetchone()
        if row and expected["stamp"] is not None:
            try:
                value = json.loads(row[0]); check = value.pop("check", None)
                if value == expected and check == storage.value_hash(value): return source["sha256"]
            except ValueError: pass
        hasher = hashlib.sha256(); done = 0
        with open_content(source) as stream:
            while chunk := stream.read(CHUNK):
                _cancel(cancel); hasher.update(chunk); done += len(chunk)
                _progress(progress, "portable-content-verify", done, source["size"], CONTENT)
        if done != source["size"] or hasher.hexdigest() != source["sha256"]:
            raise storage.BuildError("Complete-game bank differs from the signed APK SHA-256.")
        _save_proof(connection, source, stamp=expected["stamp"])
    _progress(progress, "portable-content-verify", done, done, CONTENT, complete=True)
    return hasher.hexdigest()


def import_apk(path, output, progress=None, cancel=None):
    """Copy only the selected APK; retain completed copies and partial resumes."""
    path = _file(path); manifest = inspect(path); source_stamp = _stamp(path)
    key = storage.value_hash({"schema": 1, "apk": manifest["apk"]})
    folder = storage._ordinary_owned(Path(output) / "imports/portable" / key)
    folder.mkdir(parents=True, exist_ok=True)
    target, partial, checkpoint = [storage._ordinary_owned(folder / name) for name in (APK, APK + ".part", "copy-progress.json")]
    expected = manifest["apk"]
    with _cache(output) as connection:
        witnesses = storage.ValidatedFileWitnesses(connection, folder, _SOURCE_OWNER)
        reusable = target.is_file() and witnesses.qualify(target, expected["sha256"], expected["size"])
    if reusable:
        _remember_source(output, _source_from_package(path, manifest["content"]))
        storage.write_json(folder / "portable-source.json", {"schema": 1, "sourcePackage": str(path),
            "sourceStamp": source_stamp, "apk": expected, "content": manifest["content"]})
        _progress(progress, "portable-apk-import", expected["size"], expected["size"], "Retained imported APK", complete=True)
        return target
    with _cache(output) as connection:
        witnesses = storage.ValidatedFileWitnesses(connection, folder, _SOURCE_OWNER)
        hasher = hashlib.sha256(); copied = 0
        if partial.exists() and checkpoint.exists():
            try:
                retained = _json_path(checkpoint)
                if (retained.get("schema") == 1 and retained.get("sourceStamp") == source_stamp
                        and retained.get("apk") == expected and type(retained.get("copied")) is int
                        and 0 < retained["copied"] <= expected["size"]
                        and _file(partial).stat().st_size >= retained["copied"]):
                    with partial.open("r+b") as stream:
                        stream.truncate(retained["copied"])
                        while chunk := stream.read(CHUNK):
                            _cancel(cancel); hasher.update(chunk); copied += len(chunk)
                    if hasher.hexdigest() != retained.get("prefixSha256"):
                        copied = 0; hasher = hashlib.sha256()
            except Cancelled: raise
            except (OSError, ValueError, TypeError, storage.BuildError):
                copied = 0; hasher = hashlib.sha256()
        _space(folder, expected["size"] - copied)
        def save():
            storage.write_json(checkpoint, {"schema": 1, "sourceStamp": source_stamp, "apk": expected,
                               "copied": copied, "prefixSha256": hasher.hexdigest()})
        with _outer(path) as (archive, offsets, _):
            info = archive.getinfo(APK); crc = 0; initial_copied = copied
            # Prefix SHA proves retained local bytes; source CRC is independently
            # checked by inspect/whole SHA and the final fresh copy below.
            with _BoundedFile(path, offsets[APK], info.file_size) as source, partial.open("r+b" if copied else "wb") as destination:
                source.seek(copied); destination.seek(copied); last_checkpoint = copied
                _progress(progress, "portable-apk-import", copied, expected["size"], APK)
                try:
                    while chunk := source.read(CHUNK):
                        _cancel(cancel); destination.write(chunk); hasher.update(chunk); copied += len(chunk)
                        crc = zlib.crc32(chunk, crc)
                        _progress(progress, "portable-apk-import", copied, expected["size"], APK)
                        if copied - last_checkpoint >= CHECKPOINT:
                            destination.flush(); os.fsync(destination.fileno()); save(); last_checkpoint = copied
                    destination.flush(); os.fsync(destination.fileno()); save()
                except BaseException:
                    destination.flush(); os.fsync(destination.fileno()); save(); raise
            if copied != expected["size"] or hasher.hexdigest() != expected["sha256"]:
                partial.unlink(missing_ok=True); checkpoint.unlink(missing_ok=True)
                raise storage.BuildError("Portable APK differs from its SHA-256 manifest; select an intact export.")
            # A resumed copy is proven by the full cryptographic digest; the CRC
            # is a second structural check only when the complete entry was read.
            if initial_copied == 0 and crc != info.CRC:
                partial.unlink(missing_ok=True); checkpoint.unlink(missing_ok=True)
                raise storage.BuildError("Portable APK has an invalid ZIP CRC.")
        if _stamp(path) != source_stamp: raise storage.BuildError("Portable source changed while importing its APK.")
        partial.replace(target); witnesses.remember(target, hasher.hexdigest())
        checkpoint.unlink(missing_ok=True)
    source = _source_from_package(path, manifest["content"]); _remember_source(output, source)
    storage.write_json(folder / "portable-source.json", {"schema": 1, "sourcePackage": str(path),
        "sourceStamp": source_stamp, "apk": expected, "content": manifest["content"]})
    _progress(progress, "portable-apk-import", copied, copied, APK, complete=True)
    return target


def imported_apk_digest(apk, output):
    """Return a retained actual import proof, or None without a payload read.

    A descriptor supplies only the requested identity. The SQLite witness must
    independently match the current exact imported file stamp and byte proof;
    renamed, edited, unknown or damaged inputs use the caller's ordinary hash.
    """
    class UnknownProof(Exception): pass
    def unknown(path): raise UnknownProof()
    try:
        apk = _file(apk); output = storage._ordinary_owned(Path(output))
        relative = apk.relative_to(output)
        if len(relative.parts) != 4 or relative.parts[:2] != ("imports", "portable") or relative.parts[-1] != APK:
            return None
        descriptor = _json_path(apk.parent / "portable-source.json")
        expected = descriptor.get("apk")
        if (descriptor.get("schema") != 1 or not isinstance(expected, dict)
                or set(expected) != {"file", "size", "sha256"} or expected.get("file") != APK
                or not SHA.fullmatch(str(expected.get("sha256", ""))) or type(expected.get("size")) is not int
                or storage.value_hash({"schema": 1, "apk": expected}) != relative.parts[2]):
            return None
        with _cache(output) as connection:
            witnesses = storage.ValidatedFileWitnesses(connection, apk.parent, _SOURCE_OWNER)
            if witnesses.qualify(apk, expected["sha256"], expected["size"], hasher=unknown): return expected["sha256"]
    except (OSError, ValueError, TypeError, storage.BuildError, UnknownProof): pass
    return None


class _HashingWriter:
    """Force forward-only ZIP data descriptors and hash each final byte once."""
    def __init__(self, file): self.file = file; self.sha = hashlib.sha256(); self.position = 0
    def tell(self): return self.position
    def seek(self, *args): raise io.UnsupportedOperation("Forward-only portable ZIP")
    def flush(self): self.file.flush()
    def write(self, data):
        count = self.file.write(data)
        if count != len(data): raise OSError("Portable ZIP writer made a short write.")
        self.sha.update(data); self.position += count; return count


def _info(name):
    value = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
    value.compress_type = zipfile.ZIP_STORED; value.external_attr = (stat.S_IFREG | 0o644) << 16
    return value


def _begin_export(output, destination, temporary):
    """Retire only the exact cache-owned failed attempt, before reserving disk.

    Callers serialize jobs with the workspace lock. A killed process can leave
    its one temporary ZIP behind; no glob or unrelated export is ever removed.
    Without trustworthy small metadata, ordinary disk qualification still runs.
    """
    with _cache(output) as connection:
        row = connection.execute("SELECT value FROM portable_exports WHERE destination=?", (str(destination),)).fetchone()
        if row:
            try:
                value = json.loads(row[0]); path = storage._ordinary_owned(Path(value["temporary"]))
                prefix = destination.name + ".partial-"
                if (value.get("schema") == 1 and value.get("destination") == str(destination)
                        and path.parent == destination.parent and path.name.startswith(prefix)
                        and re.fullmatch(r"[0-9a-f]{32}", path.name[len(prefix):])
                        and path != temporary and path.exists() and _file(path).stat().st_nlink == 1):
                    path.unlink()
            except (ValueError, KeyError, TypeError, OSError, storage.BuildError): pass
        connection.execute("INSERT OR REPLACE INTO portable_exports VALUES (?,?)", (str(destination), json.dumps(
            {"schema": 1, "destination": str(destination), "temporary": str(temporary)})))


def _end_export(output, destination, temporary):
    with _cache(output) as connection:
        row = connection.execute("SELECT value FROM portable_exports WHERE destination=?", (str(destination),)).fetchone()
        if row:
            try:
                if json.loads(row[0]).get("temporary") == str(temporary):
                    connection.execute("DELETE FROM portable_exports WHERE destination=?", (str(destination),))
            except (ValueError, AttributeError): pass


def export_package(apk, destination, output, source_package=None, progress=None, cancel=None):
    """Write one atomic ZIP64 transport with actual measured payload proofs."""
    apk = _file(apk); destination = storage._ordinary_owned(Path(destination).expanduser())
    source = content_source(apk, output, source_package)
    if destination.suffix.lower() != ".zip": raise storage.BuildError("Portable export destination must be a ZIP file.")
    if destination in (apk, source["path"]) or (destination.exists() and
            (os.path.samefile(destination, apk) or os.path.samefile(destination, source["path"]))):
        raise storage.BuildError("Portable export cannot replace one of its input files.")
    if destination.exists(): _file(destination)
    apk_size = apk.stat().st_size
    if not 0 < apk_size <= MAX_APK: raise storage.BuildError("Portable export requires an ordinary ZIP32 Android APK.")
    before = _stamp(apk)
    source_before = _stamp(source["path"])
    # Placeholder SHA has the exact same length as the eventual measured APK
    # hash, so progress has one known total for the complete export.
    manifest = {"schema": 1, "format": FORMAT, "package": PACKAGE,
                "apk": {"file": APK, "size": apk_size, "sha256": "0" * 64},
                "content": {"file": CONTENT, "size": source["size"], "sha256": source["sha256"], "archive": ARCHIVE}}
    total = apk_size + source["size"] + len(storage.canonical(manifest)); done = 0
    temporary = storage._ordinary_owned(destination.with_name(destination.name + ".partial-" + uuid.uuid4().hex))
    _begin_export(output, destination, temporary)
    try:
        _space(destination.parent, total + 65536); _cancel(cancel)
        with temporary.open("xb") as raw:
            sink = _HashingWriter(raw)
            with zipfile.ZipFile(sink, "w", compression=zipfile.ZIP_STORED, allowZip64=True) as archive:
                def copy(stream, name, expected_size, expected_sha=None):
                    nonlocal done
                    sha = hashlib.sha256(); size = 0
                    with archive.open(_info(name), "w", force_zip64=True) as writer:
                        while chunk := stream.read(CHUNK):
                            _cancel(cancel); writer.write(chunk); sha.update(chunk); size += len(chunk); done += len(chunk)
                            _progress(progress, "portable-package-export", done, total, name)
                    if size != expected_size or (expected_sha is not None and sha.hexdigest() != expected_sha):
                        raise storage.BuildError("Portable export input differs from its signed bank identity: " + name)
                    return sha.hexdigest()
                _progress(progress, "portable-package-export", 0, total, APK)
                with apk.open("rb") as stream: manifest["apk"]["sha256"] = copy(stream, APK, apk_size)
                if _stamp(apk) != before: raise storage.BuildError("APK changed during portable export.")
                with open_content(source) as stream: copy(stream, CONTENT, source["size"], source["sha256"])
                payload = storage.canonical(manifest)
                copy(io.BytesIO(payload), MANIFEST, len(payload))
            raw.flush(); os.fsync(raw.fileno())
            receipt = {"schema": 1, "package": PACKAGE, "path": str(destination), "size": sink.position,
                       "sha256": sink.sha.hexdigest(), "apk": manifest["apk"], "content": manifest["content"]}
        _cancel(cancel)
        temporary.replace(destination)
        with _cache(output) as connection:
            _save_proof(connection, source, stamp=source_before["reliable"])
            exported = {"path": destination, "member": CONTENT, "size": source["size"],
                        "sha256": source["sha256"], "archive": ARCHIVE}
            _save_proof(connection, exported)
        _remember_source(output, exported)
    finally:
        temporary.unlink(missing_ok=True)
        _end_export(output, destination, temporary)
    _progress(progress, "portable-package-export", total, total, "Portable package exported", complete=True)
    return receipt
