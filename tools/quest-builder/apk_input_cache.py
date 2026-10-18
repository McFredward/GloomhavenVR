"""Reuse actual APK byte proofs across local update inspections.

The caller holds the builder output lock and separately checks the selected
owned game and APK contracts/signature. Sidecar hashes alone never establish a
proof. An unknown or changed input is read once before its exact file identity
and reliable change stamp can be reused on a later invocation.
"""
from __future__ import annotations

import json
from pathlib import Path
import re
import sqlite3
import stat
import uuid

import storage

_SHA = re.compile(r"[0-9a-f]{64}\Z")
_MAX_METADATA = 2 * 1048576
_MAX_DATABASE = 8 * 1048576
_MAX_INPUTS = 128
_OWNER = {"schema": 1, "owner": "Quest APK update input"}


def _ordinary_file(path, *, limit=None):
    path = storage._ordinary_owned(Path(path))
    value = path.lstat()
    if (not stat.S_ISREG(value.st_mode) or getattr(value, "st_file_attributes", 0) & 0x400
            or value.st_nlink != 1 or (limit is not None and value.st_size > limit)):
        raise storage.BuildError("APK cache metadata is not an ordinary bounded file: " + path.name)
    return path


def _closed_build(path, output):
    """Read only this APK's receipt row; never qualify sibling game banks."""
    try:
        relative = path.relative_to(output)
        parts = relative.parts
        if len(parts) != 3 or parts[0] not in ("builds", "updates") or not _SHA.fullmatch(parts[1]):
            return None
        receipt = _ordinary_file(output / "receipts/build" / (parts[1] + ".json"), limit=_MAX_METADATA)
        with receipt.open("rb") as stream: raw = stream.read(_MAX_METADATA + 1)
        if len(raw) > _MAX_METADATA: return None
        value = json.loads(raw)
        details = value.get("details", {})
        if (value.get("schema") != 1 or value.get("stage") != "build" or value.get("key") != parts[1]
                or not isinstance(details, dict) or details.get("isDiagnostic") is not False
                or details.get("package") != "dev.gloomhavenvr.quest"
                or not _SHA.fullmatch(str(details.get("certificateSha256", "")))
                or not _SHA.fullmatch(str(details.get("apkSha256", "")))):
            return None
        rows = value.get("outputs")
        if not isinstance(rows, list): return None
        rows = [row for row in rows if isinstance(row, dict) and row.get("path") == relative.as_posix()]
        if len(rows) != 1: return None
        row = rows[0]
        if (type(row.get("size")) is not int or row["size"] != path.lstat().st_size
                or row.get("sha256") != details["apkSha256"]):
            return None
        database = output / "receipts/.file-witnesses.sqlite3"
        for item in (database, database.with_name(database.name + "-wal"), database.with_name(database.name + "-shm")):
            storage._ordinary_owned(item)
            if item.exists(): _ordinary_file(item)
    except (OSError, ValueError, AttributeError, storage.BuildError, sqlite3.DatabaseError):
        # A missing/damaged closed receipt cannot supply a digest. The generic
        # APK-only cache below still qualifies the current bytes independently.
        return None
    try:
        with storage.Stages(output)._witnesses("build", parts[1]) as witnesses:
            if witnesses.qualify(path, row["sha256"], row["size"], hasher=storage.digest):
                return row["sha256"]
    except (OSError, sqlite3.DatabaseError):
        pass
    return None


def _observe(path, connection):
    witness = storage.ValidatedFileWitnesses(connection, path.parent, _OWNER)
    result = witness.observe(path, hasher=storage.digest)
    connection.execute("DELETE FROM validated_file_witnesses WHERE rowid NOT IN "
                       "(SELECT rowid FROM validated_file_witnesses ORDER BY rowid DESC LIMIT ?)", (_MAX_INPUTS,))
    connection.commit()
    return result


def _database_files(database):
    files = [database, database.with_name(database.name + "-wal"), database.with_name(database.name + "-shm")]
    for path in files:
        storage._ordinary_owned(path)
        if path.exists(): _ordinary_file(path, limit=_MAX_DATABASE)
    return files


def digest_apk(path, output):
    """Return the current SHA-256, reusing only actual unchanged byte proofs."""
    path = storage._ordinary_owned(Path(path).expanduser())
    output = storage._ordinary_owned(Path(output))
    value = path.lstat()
    if (path.suffix.lower() != ".apk" or not stat.S_ISREG(value.st_mode)
            or getattr(value, "st_file_attributes", 0) & 0x400):
        raise storage.BuildError("Select an ordinary existing APK file for the update.")
    def stamp():
        observed = path.lstat()
        return (storage._file_witness_stamp(path, observed), observed.st_dev, observed.st_ino,
                observed.st_size, observed.st_mtime_ns, observed.st_ctime_ns)
    before = stamp()
    result = _digest_apk(path, output)
    if stamp() != before:
        raise storage.BuildError("The selected APK changed during update inspection; retry after the edit completes.")
    return result


def _digest_apk(path, output):
    selected = _closed_build(path, output)
    if selected is not None: return selected
    database = output / "cache/apk-input-witnesses.sqlite3"
    try:
        _database_files(database)
        database.parent.mkdir(parents=True, exist_ok=True)
    except (OSError, storage.BuildError):
        # Cache availability is optional. Unsafe/unwritable cache paths are
        # never followed; ordinary hashing still permits a valid update.
        with sqlite3.connect(":memory:") as connection:
            return _observe(path, connection)
    try:
        for attempt in range(2):
            connection = None
            try:
                connection = sqlite3.connect(database, timeout=30)
                connection.execute("PRAGMA journal_mode=WAL")
                connection.execute("PRAGMA synchronous=NORMAL")
                return _observe(path, connection)
            except sqlite3.DatabaseError:
                if attempt: break
                if connection is not None: connection.close(); connection = None
                # Repair only this regenerable observation database. Preserve
                # its small damaged metadata; no APK/build result is removed.
                files = _database_files(database)
                suffix = ".damaged-" + uuid.uuid4().hex
                for item in files:
                    if item.exists(): item.replace(item.with_name(item.name + suffix))
            finally:
                if connection is not None: connection.close()
    except OSError:
        pass
    with sqlite3.connect(":memory:") as connection:
        return _observe(path, connection)
