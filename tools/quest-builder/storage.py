"""Isolated immutable snapshots and hash-verified stage receipts."""

from __future__ import annotations

from contextlib import contextmanager
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3
import stat
import sys
import time
import uuid

# Installer/API loaders intentionally avoid global dependency aliases. Resolve
# this standard-library-only helper beside the exact selected storage source.
build_progress = None
if Path(__file__).with_name("progress.py").is_file():
    _progress_spec = importlib.util.spec_from_file_location("quest_builder_progress", Path(__file__).with_name("progress.py"))
    build_progress = importlib.util.module_from_spec(_progress_spec)
    _progress_spec.loader.exec_module(build_progress)


def _counter(phase, total, unit):
    return build_progress.Counter(phase, total, unit) if build_progress and build_progress.enabled() else None


class BuildError(RuntimeError):
    """An actionable conversion failure, safe to display without credentials."""


def digest(path: Path, progress=None) -> str:
    result = hashlib.sha256()
    counter = None
    if progress is None and build_progress and build_progress.enabled() and path.stat().st_size >= 8 * 1048576:
        counter = build_progress.Counter("file-hash", path.stat().st_size, "bytes", path.name)
        progress = counter.add
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
            if progress: progress(len(chunk))
    if counter: counter.finish()
    return result.hexdigest()


class ImmutableFileHashes:
    """Reuse actual reads only within this invocation of an immutable input."""
    def __init__(self):
        self.records = {}

    def digest(self, path):
        path = _ordinary_owned(Path(path))
        observed = path.stat()
        stamp = (observed.st_dev, observed.st_ino, observed.st_size, observed.st_mtime_ns, observed.st_ctime_ns)
        previous = self.records.get(path)
        if previous is not None:
            if stamp != previous[0]:
                raise BuildError("Original source changed during conversion: " + path.name)
            return previous[1]
        hashed = digest(path)
        after = path.stat()
        if stamp != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns):
            raise BuildError("Original source changed while read: " + path.name)
        self.records[path] = (stamp, hashed)
        return hashed


def canonical(value) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def value_hash(value) -> str:
    return hashlib.sha256(canonical(value)).hexdigest()


def write_json(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    try:
        with temp.open("wb") as stream:
            stream.write(canonical(value) + b"\n")
            stream.flush()
            os.fsync(stream.fileno())
        temp.replace(path)
    finally:
        temp.unlink(missing_ok=True)


def record_file(path: Path, relative: str, progress=None) -> dict:
    before = path.stat()
    hashed = digest(path, progress=progress) if progress else digest(path)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise BuildError("An input changed while being hashed: " + relative + "; retry after the edit completes.")
    return {"path": relative, "sha256": hashed, "size": after.st_size}


def inventory(root: Path, paths: list[str] | None = None, *, phase="input-hash") -> list[dict]:
    if paths is None:
        paths = sorted(str(p.relative_to(root).as_posix()) for p in root.rglob("*") if p.is_file())
    result = []; selected = []
    for relative in sorted(set(paths)):
        rel = Path(relative)
        if rel.is_absolute() or ".." in rel.parts:
            raise BuildError("Unsafe input path: " + relative)
        path = root / rel
        if not path.is_file():
            raise BuildError("A selected input is missing: " + relative)
        # Source DLL links created by worktree-setup are deliberate read-only dependencies.
        # Other symlinks are not followed into arbitrary private directories.
        contained_link = path.is_symlink() and root.resolve() in path.resolve().parents
        if path.is_symlink() and not contained_link and not relative.startswith(("libs/RuntimeDeps/", "libs/Natives/")):
            raise BuildError("A conversion input is an unsupported symlink: " + relative)
        selected.append((path, rel.as_posix()))
    counter = _counter(phase, sum(path.stat().st_size for path, _ in selected), "bytes")
    try:
        for path, relative in selected:
            result.append(record_file(path, relative, progress=lambda size: counter.add(size, Path(relative).name)) if counter else record_file(path, relative))
        if counter: counter.finish()
    except BaseException as error:
        if counter: counter.fail(error)
        if isinstance(error, ValueError) and counter:
            raise BuildError("An input changed while being hashed; retry after the edit completes.") from error
        raise
    return result


def verify_files(root: Path, records: list[dict], *, phase="file-verify") -> bool:
    counter = _counter(phase, sum(item["size"] for item in records), "bytes")
    for item in records:
        path = root / item["path"]
        if not path.is_file() or path.is_symlink() or path.stat().st_size != item["size"]:
            return False
        if (digest(path, progress=lambda size: counter.add(size, Path(item["path"]).name)) if counter else digest(path)) != item["sha256"]:
            return False
    if counter: counter.finish()
    return True


def snapshot(source: Path, records: list[dict], destination: Path, *, phase="snapshot") -> None:
    receipt = destination / ".snapshot.json"
    if receipt.is_file():
        prior = json.loads(receipt.read_text(encoding="utf-8"))
        if prior.get("files") == records:
            counter = _counter(phase + "-verify", sum(row["size"] for row in records), "bytes")
            with _snapshot_witnesses(destination, destination, records) as witnesses:
                valid = True
                for row in records:
                    path = destination / row["path"]
                    try:
                        hits = witnesses.counters["cache_hits"]
                        hasher = (lambda path: digest(path, progress=lambda size: counter.add(size, path.name))) if counter else None
                        if not witnesses.qualify(path, row["sha256"], row["size"], hasher=hasher):
                            valid = False; break
                        if counter and witnesses.counters["cache_hits"] != hits: counter.add(row["size"], path.name)
                    except (OSError, BuildError):
                        valid = False; break
                if valid:
                    if counter: counter.finish()
                    if build_progress: build_progress.event(phase, 1, 1, "snapshots", "Verified existing snapshot; " + witnesses.summary(), status="reuse")
                    return
        raise BuildError("Immutable snapshot is corrupt: " + str(destination) + "; remove this snapshot and retry.")
    if destination.exists():
        raise BuildError("An incomplete snapshot occupies " + str(destination) + "; remove it and retry.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    temp = _ordinary_owned(destination.with_name(destination.name + ".staging"))
    owner = _ordinary_owned(destination.with_name(destination.name + ".staging.json"))
    expected = {"schema": 1, "owner": "Quest input snapshot", "destination": destination.name, "files": records}
    if owner.exists():
        if json.loads(owner.read_text()) != expected: raise BuildError("Snapshot staging belongs to different inputs.")
    elif temp.exists(): raise BuildError("Snapshot staging has no matching ownership record.")
    else: write_json(owner, expected)
    # The owner is durable before any directory/member creation. A killed copy
    # resumes only this exact known file set; original inputs stay read-only.
    counter = _counter(phase, len(records), "files")
    temp.mkdir(parents=True, exist_ok=True)
    proofs = []
    with _snapshot_witnesses(destination, temp, records) as witnesses:
        for item in records:
            original = source / item["path"]
            copied = _ordinary_owned(temp / item["path"])
            copied.parent.mkdir(parents=True, exist_ok=True)
            if not copied.is_file() or not witnesses.qualify(copied, item["sha256"], item["size"]):
                witnesses.invalidate(copied)
                shutil.copyfile(original, copied)
                if not witnesses.qualify(copied, item["sha256"], item["size"]):
                    raise BuildError("An input changed while copied: " + item["path"] + "; retry after editing stops.")
            proofs.append((item, witnesses.current(copied)))
            if counter: counter.add(1, Path(item["path"]).name)
        write_json(temp / ".snapshot.json", {"schema": 1, "files": records})
        temp.replace(destination)
    # Directory publication preserves each file's identity. Transfer only the
    # actual checked byte proofs, requiring their stamps to remain exact.
    with _snapshot_witnesses(destination, destination, records) as witnesses:
        for item, stamp in proofs:
            witnesses.remember(destination / item["path"], item["sha256"], stamp=stamp)
    owner.unlink()
    if counter: counter.finish()


@contextmanager
def _snapshot_witnesses(destination, root, records):
    # Keep the observation database outside the immutable snapshot file set.
    # Dot-prefix its sidecars too: authored snapshots can live in Unity Assets,
    # whose importer must never see a SQLite database as game content.
    database = _ordinary_owned(destination.with_name("." + destination.name + ".snapshot-witnesses.sqlite3"))
    if database.exists() and (not stat.S_ISREG(database.lstat().st_mode) or database.lstat().st_nlink != 1):
        raise BuildError("Snapshot witness database is not a regular owned file.")
    connection = sqlite3.connect(database, timeout=30)
    try:
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA synchronous=NORMAL")
        witnesses = ValidatedFileWitnesses(connection, root, {"schema": 1, "owner": "Quest input snapshot", "filesHash": value_hash(records)})
        yield witnesses
        connection.commit()
    finally:
        connection.close()


def ensure_output(output: Path, repo: Path, game_data: Path | None = None) -> Path:
    raw = output.absolute()
    resolved = raw.resolve()
    if raw != resolved:
        raise BuildError("Output paths must not use symlinks; select a separate real directory.")
    protected = [repo.resolve()]
    if game_data:
        protected.append(game_data.resolve())
    for source in protected:
        if resolved == source or resolved in source.parents:
            raise BuildError("Output must not contain or replace a source checkout/game installation.")
    if game_data and (game_data.resolve() in resolved.parents):
        raise BuildError("Output must not be inside the original game installation.")
    if repo.resolve() in resolved.parents and not (
            resolved == repo.resolve() / ".planning/quest3-local" or
            repo.resolve() / ".planning/quest3-local" in resolved.parents):
        raise BuildError("In-repository output is allowed only under ignored .planning/quest3-local.")
    marker = resolved / ".quest-builder-output.json"
    if marker.exists():
        if marker.is_symlink():
            raise BuildError("Output marker must not be a symlink.")
        try:
            value = json.loads(marker.read_text(encoding="utf-8"))
        except (ValueError, OSError) as exc:
            raise BuildError("The output-directory marker is invalid.") from exc
        if value != {"schema": 1, "purpose": "GloomhavenVR local Quest conversion"}:
            raise BuildError("The directory belongs to a different build operation.")
    else:
        if resolved.exists() and any(resolved.iterdir()):
            raise BuildError("Output must be empty or an existing marked Quest-builder output directory.")
        resolved.mkdir(parents=True, exist_ok=True)
        write_json(marker, {"schema": 1, "purpose": "GloomhavenVR local Quest conversion"})
    return resolved


def _ordinary_owned(path: Path) -> Path:
    path = Path(path).absolute()
    for part in (path, *path.parents):
        if part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()):
            raise BuildError("Generated workspace paths cannot be links: " + str(path))
    return path


class _WindowsFileMetadata:
    """Read one handle's identity and ChangeTime, never Python's creation time."""
    def __init__(self, kernel=None):
        import ctypes
        self.ctypes = ctypes
        # Explicit Windows widths also make ABI regression tests portable.
        class Basic(ctypes.Structure):
            _fields_ = [(name, ctypes.c_int64) for name in
                        ("CreationTime", "LastAccessTime", "LastWriteTime", "ChangeTime")] + [("FileAttributes", ctypes.c_uint32)]
        class Standard(ctypes.Structure):
            _fields_ = [("AllocationSize", ctypes.c_int64), ("EndOfFile", ctypes.c_int64),
                        ("NumberOfLinks", ctypes.c_uint32), ("DeletePending", ctypes.c_ubyte), ("Directory", ctypes.c_ubyte)]
        class Identity(ctypes.Structure):
            _fields_ = [("VolumeSerialNumber", ctypes.c_uint64), ("FileId", ctypes.c_ubyte * 16)]
        self.Basic, self.Standard, self.Identity = Basic, Standard, Identity
        self.kernel = kernel or ctypes.WinDLL("kernel32", use_last_error=True)
        self.filesystems = {}
        if kernel is None:
            self.kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                               ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
            self.kernel.CreateFileW.restype = ctypes.c_void_p
            self.kernel.GetFileInformationByHandleEx.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32]
            self.kernel.GetFileInformationByHandleEx.restype = ctypes.c_int
            self.kernel.GetVolumeInformationByHandleW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_uint32,
                ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_uint32]
            self.kernel.GetVolumeInformationByHandleW.restype = ctypes.c_int
            self.kernel.CloseHandle.argtypes = [ctypes.c_void_p]
            self.kernel.CloseHandle.restype = ctypes.c_int

    def stamp(self, path):
        c = self.ctypes
        name = str(path)
        if not name.startswith("\\\\?\\"):
            name = "\\\\?\\UNC\\" + name[2:] if name.startswith("\\\\") else "\\\\?\\" + name
        # Read attributes, share reads/writes/deletes, OPEN_EXISTING, and open a
        # reparse point itself so it can be rejected. No file data is opened.
        # https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew
        handle = self.kernel.CreateFileW(name, 0x80, 7, None, 3, 0x00200000, None)
        if handle is None or handle == c.c_void_p(-1).value:
            return None
        try:
            basic, standard, identity = self.Basic(), self.Standard(), self.Identity()
            # FileBasicInfo=0, FileStandardInfo=1, FileIdInfo=18; the documented
            # structures keep ChangeTime distinct from CreationTime.
            # https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex
            # https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_basic_info
            for kind, value in ((0, basic), (1, standard), (18, identity)):
                if not self.kernel.GetFileInformationByHandleEx(handle, kind, c.byref(value), c.sizeof(value)):
                    return None
            if basic.FileAttributes & (0x400 | 0x10) or standard.Directory or standard.DeletePending:
                raise BuildError("Owned witness path is linked, pending deletion, or not a regular file: " + str(path))
            if standard.NumberOfLinks != 1: return None
            volume = identity.VolumeSerialNumber
            if volume not in self.filesystems:
                filesystem = c.create_unicode_buffer(261)
                # Query the opened file's volume, rather than guessing from its
                # drive letter. Unknown/FAT/network drivers use byte checks.
                # https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew
                ok = self.kernel.GetVolumeInformationByHandleW(handle, None, 0, None, None, None, filesystem, len(filesystem))
                self.filesystems[volume] = filesystem.value.upper() if ok else ""
            if self.filesystems[volume] not in ("NTFS", "REFS") or not basic.ChangeTime or not any(identity.FileId):
                return None
            return ("win32-v1", volume, bytes(identity.FileId).hex(), standard.EndOfFile,
                    basic.LastWriteTime, basic.ChangeTime)
        finally:
            self.kernel.CloseHandle(handle)


_windows_metadata = None
_invocation_file_proofs = {}


def _file_witness_stamp(path, value):
    global _windows_metadata
    if sys.platform == "win32":
        try:
            if _windows_metadata is None: _windows_metadata = _WindowsFileMetadata()
            return _windows_metadata.stamp(path)
        except (OSError, AttributeError):
            # Unsupported metadata APIs never become persistent stamp trust.
            return None
    return ("posix-v1", value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)


class ValidatedFileWitnesses:
    """Persist byte proofs under an exact owner and reliable file-change stamp.

    The caller holds the workspace lock and commits the supplied SQLite
    connection. Receipt hashes remain authoritative. Unknown metadata or an old
    database without witnesses requires a byte read; this is not a hash skip.
    """
    def __init__(self, connection, owned_root, owner_identity):
        self.db = connection
        self.root = _ordinary_owned(Path(owned_root))
        root_stat = self.root.lstat()
        if not stat.S_ISDIR(root_stat.st_mode): raise BuildError("File witness root is not a directory.")
        self.root_identity = (root_stat.st_dev, root_stat.st_ino)
        self.namespace = value_hash({"schema": 1, "root": str(self.root), "owner": owner_identity})
        self.counters = {"files_read": 0, "bytes_read": 0, "cache_hits": 0}
        self.db.execute("""CREATE TABLE IF NOT EXISTS validated_file_witnesses
            (owner TEXT NOT NULL, path TEXT NOT NULL, size INTEGER NOT NULL,
             sha TEXT NOT NULL, stamp TEXT NOT NULL, check_hash TEXT NOT NULL,
             PRIMARY KEY(owner,path))""")

    def _path(self, path):
        path = Path(os.path.abspath(path))
        if path == self.root or self.root not in path.parents:
            raise BuildError("File witness is outside its owned root: " + str(path))
        # The root and its external ancestors were qualified once. Inspect
        # each owned ancestor with one lstat, without resolve/is_junction walks.
        for parent in path.parents:
            value = parent.lstat()
            if not stat.S_ISDIR(value.st_mode) or getattr(value, "st_file_attributes", 0) & 0x400:
                raise BuildError("File witness crosses a linked directory: " + str(path))
            if parent == self.root:
                observed = (value.st_dev, value.st_ino)
                if self.root_identity is None: self.root_identity = observed
                if observed != self.root_identity:
                    raise BuildError("File witness owner root changed during conversion.")
                break
        return path

    def current(self, path):
        path = self._path(path)
        return self._current(path)

    def _current(self, path):
        value = path.lstat()
        if not stat.S_ISREG(value.st_mode) or getattr(value, "st_file_attributes", 0) & 0x400:
            raise BuildError("File witness is not a regular owned file: " + str(path))
        # Content publication deliberately hardlinks ZIP preimages. Preserve
        # that contract, but never reuse persistent/invocation stamp trust.
        if value.st_nlink != 1: return None
        return _file_witness_stamp(path, value)

    def _key(self, path):
        return Path(path).absolute().relative_to(self.root).as_posix()

    def _save(self, path, sha256, stamp):
        if stamp is None: return
        _invocation_file_proofs[Path(path).absolute()] = (stamp, sha256)
        relative = self._key(path)
        payload = [self.namespace, relative, stamp[3], sha256, list(stamp)]
        self.db.execute("INSERT OR REPLACE INTO validated_file_witnesses VALUES (?,?,?,?,?,?)",
                        (self.namespace, relative, stamp[3], sha256, canonical(list(stamp)).decode(), value_hash(payload)))

    def remember(self, path, sha256, *, stamp=None):
        """Attach a caller's exact closed-writer/read proof; never infer bytes."""
        if not isinstance(sha256, str) or len(sha256) != 64 or any(c not in "0123456789abcdef" for c in sha256):
            raise BuildError("File witness requires a SHA-256 byte proof.")
        actual = self.current(path)
        if stamp is not None and tuple(stamp) != actual:
            raise BuildError("File changed before its witness was recorded: " + str(path))
        self._save(path, sha256, actual)

    def invalidate(self, path):
        path = Path(path).absolute()
        if path == self.root:
            # An explicit owned reset may delete/recreate the root. Drop only
            # this owner namespace and bind its newly created directory again.
            _ordinary_owned(path)
            self.db.execute("DELETE FROM validated_file_witnesses WHERE owner=?", (self.namespace,))
            for known in list(_invocation_file_proofs):
                if path in known.parents: del _invocation_file_proofs[known]
            try: value = path.lstat()
            except FileNotFoundError: self.root_identity = None
            else:
                if not stat.S_ISDIR(value.st_mode): raise BuildError("File witness reset root is not a directory.")
                self.root_identity = (value.st_dev, value.st_ino)
            return
        relative = self._key(path)
        _invocation_file_proofs.pop(path, None)
        if path.is_dir():
            for known in list(_invocation_file_proofs):
                if path in known.parents: del _invocation_file_proofs[known]
        # Indexed prefix ranges avoid scanning every witness per YAML write.
        self.db.execute("DELETE FROM validated_file_witnesses WHERE owner=? AND path=?", (self.namespace, relative))
        self.db.execute("DELETE FROM validated_file_witnesses WHERE owner=? AND path>=? AND path<?",
                        (self.namespace, relative + "/", relative + "0"))

    def observe(self, path, *, hasher=None):
        path = self._path(path)
        return self._observe(path, hasher=hasher)

    def _observe(self, path, *, hasher=None):
        before = self._current(path)
        row = self.db.execute("SELECT size,sha,stamp,check_hash FROM validated_file_witnesses WHERE owner=? AND path=?",
                              (self.namespace, self._key(path))).fetchone()
        if row is not None and before is not None:
            size, hashed, serialized, check = row
            try:
                stamp = json.loads(serialized)
                good = (stamp == list(before) and size == before[3] and isinstance(hashed, str) and len(hashed) == 64 and
                        all(c in "0123456789abcdef" for c in hashed) and
                        check == value_hash([self.namespace, self._key(path), size, hashed, stamp]))
            except (ValueError, TypeError): good = False
            if good:
                self.counters["cache_hits"] += 1
                _invocation_file_proofs[path] = (before, hashed)
                return hashed
        # A prior producer in this process may already have streamed the exact
        # closed-writer bytes. This also avoids a second producer/stage hash.
        proved = _invocation_file_proofs.get(path)
        if before is not None and proved is not None and proved[0] == before:
            self._save(path, proved[1], before)
            self.counters["cache_hits"] += 1
            return proved[1]
        fallback_before = path.lstat()
        if hasher is None:
            result = hashlib.sha256(); read = 0
            with path.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1048576), b""):
                    result.update(chunk); read += len(chunk)
            hashed = result.hexdigest()
        else:
            # Custom hashers must consume the complete file, as digest does.
            hashed = hasher(path); read = fallback_before.st_size
        self.counters["files_read"] += 1; self.counters["bytes_read"] += read
        after = self._current(path)
        fallback_after = path.lstat()
        stamp = lambda v: (v.st_dev, v.st_ino, v.st_size, v.st_mtime_ns, v.st_ctime_ns)
        if before != after or stamp(fallback_before) != stamp(fallback_after) or read != fallback_after.st_size:
            raise BuildError("Owned file changed while its bytes were read: " + str(path))
        self._save(path, hashed, after)
        return hashed

    def qualify(self, path, expected_sha256, expected_size, *, hasher=None):
        path = self._path(path)
        if path.lstat().st_size != expected_size: return False
        return self._observe(path, hasher=hasher) == expected_sha256

    def summary(self):
        c = self.counters
        return (str(c["cache_hits"]) + " files reused by metadata; " + str(c["files_read"]) +
                " files / " + str(c["bytes_read"]) + " bytes rechecked")


@contextmanager
def output_lock(output: Path):
    """The persistent kernel guard is distinct from its transient status record.

    Never unlink a locked inode: a concurrent builder could otherwise lock a
    replacement file while this process still owns the first. Hard death frees
    the kernel guard; the retained status record cannot permanently block reuse.
    """
    guard = _ordinary_owned(output / ".builder.guard")
    lock = _ordinary_owned(output / ".builder.lock")
    stream = guard.open("a+b")
    if stream.seek(0, os.SEEK_END) == 0: stream.write(b"0"); stream.flush()
    stream.seek(0)
    try:
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError as exc:
        stream.close(); raise BuildError("This output is locked by another running builder.") from exc
    nonce = uuid.uuid4().hex
    try:
        if lock.is_file():
            raw = lock.read_text(encoding="utf-8").strip()
            # Compatibility with the earlier O_EXCL/PID lock: only a dead
            # legacy owner can be recovered. PID checks never authorize killing.
            if raw.isdecimal():
                try: os.kill(int(raw), 0)
                except ProcessLookupError: pass
                except (PermissionError, OSError) as exc: raise BuildError("A legacy builder still owns this output.") from exc
                else: raise BuildError("A legacy builder still owns this output.")
            else:
                try: previous = json.loads(raw)
                except ValueError as exc: raise BuildError("Unrecognized builder lock record; retain it for inspection.") from exc
                if not isinstance(previous, dict) or previous.get("schema") != 1 or previous.get("lock") != "kernel-guard":
                    raise BuildError("Unrecognized builder lock record; retain it for inspection.")
        write_json(lock, {"schema": 1, "lock": "kernel-guard", "pid": os.getpid(), "nonce": nonce})
        yield
    finally:
        if lock.is_file():
            try:
                retained = json.loads(lock.read_text())
                if isinstance(retained, dict) and retained.get("nonce") == nonce: lock.unlink()
            except (ValueError, OSError): pass
        try:
            stream.seek(0)
            if os.name == "nt": msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else: fcntl.flock(stream, fcntl.LOCK_UN)
        finally: stream.close()


def project_generation_paths(output: Path, project: Path, input_key: str):
    output, project = _ordinary_owned(output), _ordinary_owned(project)
    # The exact recipe is caller-owned; the path remains strictly confined to
    # one SHA256-named generated child, never an arbitrary user folder.
    if project.parent != output / "projects" or len(project.name) != 64 or any(c not in "0123456789abcdef" for c in project.name):
        raise BuildError("Unexpected generated project identity.")
    marker = _ordinary_owned(output / "cache/project-lifecycle" / project.name / "owner.json")
    backup = marker.parent / "Library"
    expected = {"schema": 2, "owner": "Quest generated project", "project": project.relative_to(output).as_posix(), "workspaceKey": project.name}
    if marker.exists():
        _ordinary_owned(marker)
        prior = json.loads(marker.read_text())
        legacy = {"schema": 1, "owner": "Quest generated project", "project": project.relative_to(output).as_posix(), "inputKey": input_key}
        if prior == legacy: write_json(marker, expected)
        elif prior != expected: raise BuildError("Generated project ownership changed.")
    else:
        if project.exists():
            settings = project / "QuestBuilderSettings.json"
            if not settings.is_file() or json.loads(settings.read_text()).get("inputKey") != input_key:
                raise BuildError("Existing incomplete project has no matching preparation owner.")
        write_json(marker, expected)
    return marker, _ordinary_owned(backup)


def restore_project_library(output: Path, project: Path, input_key: str):
    _, backup = project_generation_paths(output, project, input_key)
    if backup.exists():
        destination = _ordinary_owned(project / "Library")
        if destination.exists(): raise BuildError("Two retained Unity Libraries exist; neither was deleted.")
        project.mkdir(parents=True, exist_ok=True)
        os.replace(backup, destination)


@contextmanager
def regenerate_project(output: Path, project: Path, input_key: str):
    """Repair only this owned generated project while retaining its imported Library.

    The Library rename and ownership record survive hard process death. A later
    prepare restores it before checking/repairing the immutable stage contract.
    """
    restore_project_library(output, project, input_key)
    _, backup = project_generation_paths(output, project, input_key)
    library = _ordinary_owned(project / "Library")
    if library.exists(): os.replace(library, backup)
    try:
        if project.exists(): shutil.rmtree(project)
        yield
    finally:
        if backup.exists():
            project.mkdir(parents=True, exist_ok=True)
            if library.exists(): raise BuildError("Preparation created an unexpected second Unity Library; retained both.")
            os.replace(backup, library)


CONTENT_PATHS = ("Assets/StreamingAssets/quest-startup-content.zip", "Assets/Quest/Resources/quest-startup-content.json")


def recover_player_exclusions(project: Path):
    """Restore only the native Player's journaled ZIP/meta/Addressables moves."""
    journal = _ordinary_owned(project / "QuestCampaignEvidence/excluded-payload/journal.json")
    if not journal.exists(): return
    if not journal.is_file() or journal.stat().st_size > 65536:
        raise BuildError("Campaign exclusion journal is not an ordinary bounded file.")
    value = json.loads(journal.read_text())
    key = value.get("inputKey")
    if value.get("schema") != 1 or value.get("scope") != "quest-campaign-player-content-exclusion" or value.get("state") not in ("planned", "excluded", "restored") or not isinstance(key, str) or len(key) != 64 or any(c not in "0123456789abcdef" for c in key):
        raise BuildError("Campaign exclusion journal has an unknown scope/identity.")
    if value["state"] != "restored":
        manifest = json.loads(_ordinary_owned(project / CONTENT_PATHS[1]).read_text())
        if manifest.get("inputKey") != key:
            raise BuildError("Campaign exclusion journal differs from the current content input.")
    native = "Library/com.unity.addressables/aa/Android"
    allowed = {
        CONTENT_PATHS[0]: ("file", "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip"),
        CONTENT_PATHS[0] + ".meta": ("file", "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip.meta"),
        native: ("directory", "QuestCampaignEvidence/excluded-payload/native-addressables-Android")}
    link = value.get("nativeLink")
    if not isinstance(link, dict) or link.get("source") != native + "/AddressablesLink/link.xml" or link.get("projectPath") != "Assets/Quest/CampaignLink/link.xml" or not isinstance(link.get("sha256"), str) or len(link["sha256"]) != 64 or any(c not in "0123456789abcdef" for c in link["sha256"]):
        raise BuildError("Campaign exclusion journal lost the native linker identity.")
    previous_hash = link.get("previousSha256")
    if previous_hash not in (None, "") and (not isinstance(previous_hash, str) or len(previous_hash) != 64 or any(c not in "0123456789abcdef" for c in previous_hash)):
        raise BuildError("Campaign exclusion journal has an invalid prior linker identity.")
    moves = value.get("moves")
    if not isinstance(moves, list) or not 2 <= len(moves) <= 3 or any(not isinstance(row, dict) for row in moves) or [row.get("source") for row in moves] not in ([CONTENT_PATHS[0], native], [CONTENT_PATHS[0], CONTENT_PATHS[0] + ".meta", native]):
        raise BuildError("Campaign exclusion journal must contain the exact ordered native moves.")
    stable_link = _ordinary_owned(project / link["projectPath"])
    if stable_link.exists() and (not stable_link.is_file() or digest(stable_link) not in (link["sha256"], previous_hash)):
        raise BuildError("Campaign stable native linker bytes changed; no payload moved.")
    planned = []
    for row in moves:
        kind, temporary = allowed[row["source"]]
        if row.get("kind") != kind or row.get("temporary") != temporary or type(row.get("size")) is not int or row["size"] < 0 or not isinstance(row.get("sha256"), str) or len(row["sha256"]) != 64 or any(c not in "0123456789abcdef" for c in row["sha256"]) or (kind == "directory" and row["sha256"] != link["sha256"]):
            raise BuildError("Campaign exclusion journal names an unsupported move.")
        source = _ordinary_owned(project / row["source"])
        held = _ordinary_owned(project / temporary)
        if value["state"] == "restored":
            # This historical journal owns no pending payload. A later failed
            # native build can remove its AA directory/meta; the outer content
            # transaction also owns ZIP/manifest rollback. Never pin old hashes.
            if held.exists():
                raise BuildError("Restored Campaign journal still has an outstanding temporary.")
            if source.exists():
                if (kind == "file" and not source.is_file()) or (kind == "directory" and not source.is_dir()):
                    raise BuildError("Restored Campaign source has an unsupported type.")
                if kind == "directory":
                    for member in source.rglob("*"):
                        _ordinary_owned(member)
                        if not member.is_dir() and not member.is_file():
                            raise BuildError("Restored Campaign native directory contains a non-file member.")
            continue
        if source.exists() == held.exists():
            raise BuildError("Campaign exclusion source/temporary conflict or missing pair; no payload moved.")
        present = source if source.exists() else held
        if kind == "file":
            if not present.is_file() or present.stat().st_size != row["size"] or digest(present) != row["sha256"]:
                raise BuildError("Campaign exclusion file bytes changed; no payload moved.")
        else:
            if not present.is_dir() or row.get("size") != 0 or row.get("sha256") != link["sha256"]:
                raise BuildError("Campaign exclusion native directory ownership differs.")
            for member in present.rglob("*"):
                _ordinary_owned(member)
                if not member.is_dir() and not member.is_file():
                    raise BuildError("Campaign native directory contains a non-file member.")
            original_link = _ordinary_owned(present / "AddressablesLink/link.xml")
            if not original_link.is_file() or digest(original_link) != link["sha256"]:
                raise BuildError("Campaign excluded native linker bytes changed; no payload moved.")
        planned.append((source, held))
    pending = []
    for relative in ("QuestCampaignEvidence/excluded-payload/journal.json", CONTENT_PATHS[1], "Assets/StreamingAssets/Quest/content-delivery.json", link["projectPath"]):
        path = _ordinary_owned(project / (relative + ".quest-content-pending"))
        if path.exists() and not path.is_file():
            raise BuildError("Campaign metadata pending path is not an ordinary file.")
        pending.append(path)
    # Every move/link is checked before the first change. A hard interruption
    # midway remains recoverable: already restored source-only pairs are valid.
    for source, held in reversed(planned):
        if held.exists():
            if source.exists():
                raise BuildError("Campaign recovery source appeared after preflight; no destination overwritten.")
            source.parent.mkdir(parents=True, exist_ok=True)
            os.rename(held, source)
    for path in pending: path.unlink(missing_ok=True)
    if value["state"] != "restored":
        value["state"] = "restored"
        write_json(journal, value)


def recover_project_content(output: Path, project: Path, input_key: str):
    """Rollback an interrupted native repack/exclusion to its verified entry pair."""
    project_generation_paths(output, project, input_key)
    recover_player_exclusions(project)
    root = output / "cache/project-content-transactions" / project.name
    journal = _ordinary_owned(root / "pending.json")
    if not journal.exists(): return
    value = json.loads(journal.read_text())
    journal_key = value.get("inputKey")
    if value.get("schema") != 1 or not isinstance(journal_key, str) or len(journal_key) != 64 or any(c not in "0123456789abcdef" for c in journal_key) or value.get("project") != project.relative_to(output).as_posix():
        raise BuildError("Content recovery journal differs from its generated project.")
    if not isinstance(value.get("files"), list) or len(value["files"]) != 2 or {row.get("path") for row in value["files"]} != set(CONTENT_PATHS):
        raise BuildError("Content journal must preserve the exact archive/manifest pair.")
    for row in value["files"]:
        if row["path"] not in CONTENT_PATHS: raise BuildError("Content journal names an unsupported mutable output.")
        saved = _ordinary_owned(root / row["backup"])
        if saved.parent != root or not saved.is_file() or saved.stat().st_size != row["size"] or digest(saved) != row["sha256"]:
            raise BuildError("Retained content recovery bytes changed; no archive was overwritten.")
    manifest_row = next(row for row in value["files"] if row["path"] == CONTENT_PATHS[1])
    manifest = json.loads((root / manifest_row["backup"]).read_text())
    archive_row = next(row for row in value["files"] if row["path"] == CONTENT_PATHS[0])
    if manifest.get("inputKey") != journal_key or manifest.get("archive") != "quest-startup-content.zip" or manifest.get("archiveSha256") != archive_row["sha256"]:
        raise BuildError("Retained content backups do not prove their original transaction identity.")
    for row in value["files"]:
        target = _ordinary_owned(project / row["path"]); target.parent.mkdir(parents=True, exist_ok=True)
        # A copy fallback is used when the filesystem does not offer hardlinks.
        temp = target.with_name(target.name + ".restore-" + uuid.uuid4().hex)
        saved = root / row["backup"]
        try:
            try: os.link(saved, temp)
            except OSError: shutil.copyfile(saved, temp)
            os.replace(temp, target)
        finally: temp.unlink(missing_ok=True)
    excluded = _ordinary_owned(project / "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip")
    excluded.unlink(missing_ok=True)
    for path in (project / "Assets/StreamingAssets").glob("quest-startup-content.zip.repack-*"):
        suffix = path.name.rsplit("-", 1)[-1]
        if len(suffix) == 32 and all(c in "0123456789abcdef" for c in suffix): _ordinary_owned(path).unlink()
    journal.unlink()
    for row in value["files"]: (root / row["backup"]).unlink(missing_ok=True)


def content_records(project: Path, input_key: str):
    files = [record_file(_ordinary_owned(project / relative), relative) for relative in CONTENT_PATHS]
    manifest = json.loads((project / CONTENT_PATHS[1]).read_text())
    if manifest.get("inputKey") != input_key or manifest.get("archive") != "quest-startup-content.zip" or manifest.get("archiveSha256") != files[0]["sha256"]:
        raise BuildError("Content archive/manifest pair does not match this build.")
    return files


def project_content_valid(output: Path, project: Path, input_key: str):
    """A separate mutable ledger permits completed native repacks to stay warm."""
    ledger = output / "cache/project-content-transactions" / project.name / "complete.json"
    try:
        current = content_records(project, input_key)
        if ledger.exists():
            prior = json.loads(_ordinary_owned(ledger).read_text())
            return prior == {"schema": 1, "inputKey": input_key, "files": current}
        # Adopt a coherent older prepared project without invalidating Library.
        write_json(ledger, {"schema": 1, "inputKey": input_key, "files": current})
        return True
    except (OSError, ValueError, BuildError): return False


def publish_project_content(output: Path, project: Path, input_key: str):
    write_json(output / "cache/project-content-transactions" / project.name / "complete.json",
               {"schema": 1, "inputKey": input_key, "files": content_records(project, input_key)})


@contextmanager
def project_content_transaction(output: Path, project: Path, input_key: str):
    """Build-owned mutable outputs never invalidate immutable preparation receipts."""
    recover_project_content(output, project, input_key)
    root = output / "cache/project-content-transactions" / project.name
    root.mkdir(parents=True, exist_ok=True)
    files = []
    for index, row in enumerate(content_records(project, input_key)):
        relative = row["path"]
        target = _ordinary_owned(project / relative)
        saved = _ordinary_owned(root / ("content-" + str(index) + ".backup"))
        if saved.exists(): saved.unlink()  # previous death before journal publication, owned basename
        if target.suffix == ".zip":
            try:
                os.link(target, saved)
                if not os.path.samestat(target.stat(), saved.stat()): raise BuildError("Content hardlink identity changed.")
            except OSError:
                shutil.copyfile(target, saved)
                if digest(saved) != row["sha256"]: raise BuildError("Content changed while preserving recovery bytes.")
        else: shutil.copyfile(target, saved)
        if target.suffix != ".zip" and digest(saved) != row["sha256"]: raise BuildError("Content changed while preserving recovery bytes.")
        files.append({**row, "backup": saved.name})
    journal = root / "pending.json"
    write_json(journal, {"schema": 1, "project": project.relative_to(output).as_posix(), "inputKey": input_key, "files": files})
    try:
        yield
        # Commit only after the player and delivered content have passed their
        # original gates. A failed stage retains a restartable archive pair.
        publish_project_content(output, project, input_key)
        journal.unlink()
        for row in files: (root / row["backup"]).unlink(missing_ok=True)
    except BaseException:
        # Keep the journal on failed/cancelled builds. Rollback runs under the
        # next output lock, after the supervisor has stopped every owned child.
        raise


class Stages:
    def __init__(self, output: Path):
        self.output = _ordinary_owned(Path(output))

    def path(self, name: str, key: str) -> Path:
        return self.output / "receipts" / name / (key + ".json")

    @contextmanager
    def _witnesses(self, name, key):
        root = _ordinary_owned(self.output)
        directory = _ordinary_owned(root / "receipts")
        directory.mkdir(parents=True, exist_ok=True)
        path = _ordinary_owned(directory / ".file-witnesses.sqlite3")
        if path.exists() and (not stat.S_ISREG(path.lstat().st_mode) or path.lstat().st_nlink != 1):
            raise BuildError("Stage witness database is not a regular owned file.")
        connection = sqlite3.connect(path, timeout=30)
        try:
            connection.execute("PRAGMA journal_mode=WAL")
            connection.execute("PRAGMA synchronous=NORMAL")
            witnesses = ValidatedFileWitnesses(connection, root, {"schema": 1, "stage": name, "key": key})
            yield witnesses
            connection.commit()
        finally:
            connection.close()

    def valid(self, name: str, key: str) -> dict | None:
        path = self.path(name, key)
        if not path.is_file():
            return None
        try:
            value = json.loads(path.read_text(encoding="utf-8"))
            if value.get("schema") != 1 or value.get("stage") != name or value.get("key") != key:
                return None
            records = value["outputs"]
            if not records:
                return None
            phase = "stage-receipt-verify:" + name
            counter = _counter(phase, sum(item["size"] for item in records), "bytes")
            with self._witnesses(name, key) as witnesses:
                for item in records:
                    candidate = self.output / item["path"]
                    hits = witnesses.counters["cache_hits"]
                    hasher = (lambda path: digest(path, progress=lambda size: counter.add(size, path.name))) if counter else None
                    if not witnesses.qualify(candidate, item["sha256"], item["size"], hasher=hasher): return None
                    if counter and witnesses.counters["cache_hits"] != hits: counter.add(item["size"], candidate.name)
                if counter: counter.finish()
                if build_progress: build_progress.event(phase, detail=witnesses.summary(), status="complete")
            return value
        except (ValueError, OSError, KeyError, BuildError):
            return None

    def run(self, name: str, key: str, action) -> dict:
        prior = self.valid(name, key)
        if prior:
            print(name + ": reusing verified output", flush=True)
            if build_progress: build_progress.event("stage:" + name, 1, 1, "stages", "Verified output reused", status="reuse")
            return prior
        self.path(name, key).unlink(missing_ok=True)
        print(name + ": running", flush=True)
        started = time.monotonic()
        if build_progress: build_progress.event("stage:" + name, detail="Running builder stage", status="start")
        try:
            paths, details = action()
            paths = list(paths)
            counter = _counter("stage-output-verify:" + name, sum(path.stat().st_size for path in paths), "bytes")
            records = []
            with self._witnesses(name, key) as witnesses:
                for path in paths:
                    hits = witnesses.counters["cache_hits"]
                    hasher = (lambda path: digest(path, progress=lambda size: counter.add(size, path.name))) if counter else None
                    hashed = witnesses.observe(path, hasher=hasher)
                    records.append({"path": path.relative_to(self.output).as_posix(), "sha256": hashed, "size": path.lstat().st_size})
                    if counter and witnesses.counters["cache_hits"] != hits: counter.add(records[-1]["size"], path.name)
                if build_progress: build_progress.event("stage-output-verify:" + name, detail=witnesses.summary(), status="complete")
            if not records:
                raise BuildError(name + " produced no verifiable files.")
            value = {"schema": 1, "stage": name, "key": key, "outputs": records, "details": details}
            write_json(self.path(name, key), value)
            if counter: counter.finish()
            if build_progress: build_progress.event("stage:" + name, 1, 1, "stages", "Output verified; duration " + str(round(time.monotonic() - started, 3)) + " s", status="complete")
            return value
        except BaseException as exc:
            # Failed/cancelled stages never acquire a successful receipt.
            write_json(self.output / "last-failure.json", {"schema": 1, "stage": name, "key": key,
                       "error": type(exc).__name__, "message": str(exc)})
            if build_progress: build_progress.event("stage:" + name, detail="Failed: " + type(exc).__name__, status="failed")
            raise
