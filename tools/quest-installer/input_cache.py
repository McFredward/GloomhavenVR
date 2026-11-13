"""Bounded local checksum memoization for unchanged, already-hashed outputs.

This is a retry optimization, not an authenticity or ownership receipt. Callers
must hash an input before remembering it, compare the result with the expected
output digest, and still validate game ownership independently. Exact path,
device, inode, size, mtime and ctime equality permit reuse; missing, changed,
linked or malformed inputs and cache records require ordinary hashing. Metadata
preserving corruption or deliberate local tampering cannot be detected by this
cache, so it must never qualify a previously unknown installer input.
"""

from contextlib import contextmanager
import errno
import json
import os
from pathlib import Path
import re
import stat
import tempfile
import time

try:
    import fcntl
except ImportError:  # Windows
    fcntl = None
try:
    import msvcrt
except ImportError:  # POSIX
    msvcrt = None

MAX_ENTRIES = 32
MAX_BYTES = 256 * 1024
MAX_PATH_CHARS = 4096
LOCK_SECONDS = .5
_SHA256 = re.compile(r"[0-9a-f]{64}\Z")


def _witness(value):
    return (isinstance(value, (tuple, list)) and len(value) == 5 and
            all(type(item) is int and 0 <= item < 2 ** 64 for item in value))


def _plain_path(path):
    """Inspect parents without resolving away an input/cache symlink."""
    path = Path(os.path.abspath(path))
    for component in (path, *path.parents):
        try:
            info = component.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(component, "is_junction", lambda: False)():
            raise ValueError("Linked cache/input path")
        if component != path and not stat.S_ISDIR(info.st_mode):
            raise ValueError("Non-directory cache/input parent")
    return path


def _input(path, witness):
    if not _witness(witness):
        raise ValueError("Invalid input stat witness")
    path = _plain_path(path)
    info = path.lstat()
    actual = (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns)
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or actual != tuple(witness):
        raise ValueError("Changed or linked installer input")
    key = os.path.normcase(str(path))
    if len(key) > MAX_PATH_CHARS:
        raise ValueError("Input path exceeds cache bounds")
    return key


def _pairs(items):
    value = {}
    for key, item in items:
        if key in value:
            raise ValueError("Duplicate cache JSON key")
        value[key] = item
    return value


def _read(path):
    path = _plain_path(path)
    try:
        before = path.lstat()
    except FileNotFoundError:
        return []
    if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > MAX_BYTES:
        raise ValueError("Invalid cache file")
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_BINARY", 0))
    with os.fdopen(descriptor, "rb") as stream:
        opened = os.fstat(stream.fileno())
        if (opened.st_dev, opened.st_ino) != (before.st_dev, before.st_ino) or opened.st_nlink != 1:
            raise ValueError("Cache file changed before opening")
        payload = stream.read(MAX_BYTES + 1)
    if len(payload) > MAX_BYTES:
        raise ValueError("Oversized cache")
    value = json.loads(payload, object_pairs_hook=_pairs)
    if not isinstance(value, dict) or set(value) != {"schema", "entries"} or type(value["schema"]) is not int or value["schema"] != 1:
        raise ValueError("Unsupported cache schema")
    entries = value["entries"]
    if not isinstance(entries, list) or len(entries) > MAX_ENTRIES:
        raise ValueError("Invalid cache entry count")
    seen = set()
    for row in entries:
        if not isinstance(row, dict) or set(row) != {"path", "witness", "sha256"}:
            raise ValueError("Malformed cache record")
        key = row["path"]
        if not isinstance(key, str) or not key or len(key) > MAX_PATH_CHARS or not os.path.isabs(key) or key in seen:
            raise ValueError("Invalid cache input path")
        if not _witness(row["witness"]) or not isinstance(row["sha256"], str) or _SHA256.fullmatch(row["sha256"]) is None:
            raise ValueError("Invalid cache input witness/digest")
        seen.add(key)
    return entries


def lookup(cache_path, file_path, witness):
    """Return a prior digest only for an unchanged ordinary local input."""
    try:
        key = _input(file_path, witness)
        for row in _read(cache_path):
            if row["path"] == key and tuple(row["witness"]) == tuple(witness):
                # Recheck after cache I/O so a replacement during lookup misses.
                if _input(file_path, witness) == key:
                    return row["sha256"]
    except (OSError, ValueError, TypeError, RecursionError):
        pass
    return None


@contextmanager
def _lock(path):
    """Serialize atomic replacements across CLI processes with a short bound."""
    path = _plain_path(path)
    descriptor = os.open(path, os.O_RDWR | os.O_CREAT | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_BINARY", 0), 0o600)
    with os.fdopen(descriptor, "r+b") as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > 1:
            raise ValueError("Invalid cache lock file")
        deadline = time.monotonic() + LOCK_SECONDS
        while True:
            try:
                if fcntl is not None:
                    fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
                elif msvcrt is not None:
                    stream.seek(0)
                    if info.st_size == 0:
                        stream.write(b"\0"); stream.flush()
                    stream.seek(0)
                    msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    raise ValueError("No cache locking support")
                break
            except OSError as error:
                busy = error.errno in (errno.EACCES, errno.EAGAIN, errno.EDEADLK) or getattr(error, "winerror", None) in (33, 36)
                if not busy or time.monotonic() >= deadline:
                    raise
                time.sleep(.01)
        try:
            yield
        finally:
            if fcntl is not None:
                fcntl.flock(stream, fcntl.LOCK_UN)
            else:
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)


def remember(cache_path, file_path, witness, digest):
    """Best-effort record of a caller-computed digest; never blocks installation."""
    temporary = None
    try:
        if not isinstance(digest, str) or _SHA256.fullmatch(digest) is None:
            return False
        key = _input(file_path, witness)
        cache_path = _plain_path(cache_path)
        if cache_path.exists() and (not cache_path.is_file() or cache_path.stat().st_nlink != 1):
            return False
        cache_path.parent.mkdir(parents=True, exist_ok=True)
        with _lock(cache_path.with_name(cache_path.name + ".lock")):
            try:
                entries = _read(cache_path)
            except (ValueError, RecursionError):
                entries = []
            entries = [row for row in entries if row["path"] != key]
            entries.append({"path": key, "witness": list(witness), "sha256": digest})
            payload = json.dumps({"schema": 1, "entries": entries[-MAX_ENTRIES:]}, ensure_ascii=True, separators=(",", ":")).encode("utf-8")
            if len(payload) > MAX_BYTES:
                return False
            descriptor, name = tempfile.mkstemp(prefix=cache_path.name + ".", suffix=".tmp", dir=cache_path.parent)
            temporary = Path(name)
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(payload); stream.flush(); os.fsync(stream.fileno())
            if _input(file_path, witness) != key:
                return False
            _plain_path(cache_path)
            if cache_path.exists() and (not cache_path.is_file() or cache_path.stat().st_nlink != 1):
                return False
            os.replace(temporary, cache_path)
            temporary = None
        return True
    except (OSError, ValueError, TypeError, RecursionError):
        return False
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass
