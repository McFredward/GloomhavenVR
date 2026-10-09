"""Owned, incremental full-staging receipts and bounded write-ahead rollback.

Completed phases keep JSON context and the latest byte proofs, not snapshots of
the project. Persisted change stamps qualify unchanged owned files; legacy or
changed files still require byte reads. During a
transform, one scoped CPython audit dispatcher records preimages before Python
filesystem writes, moves and removals. Only the unfinished phase is rolled back.
Current GUID/layout/native/TMP writers use these operations; subprocess writers
must declare another transaction seam before they can be added here.
"""
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import stat
import sys
import threading
import uuid

from storage import BuildError, ValidatedFileWitnesses, build_progress

SCHEMA = 1
BATCH = 128
_DISPATCH_NAME = "_quest_staging_audit_dispatch"


def _dispatcher():
    # addaudithook cannot be removed. Install exactly one dispatcher even when
    # the builder module is imported again; idle hooks retain no project state.
    state = getattr(sys, _DISPATCH_NAME, None)
    if state is None:
        state = threading.local()
        def dispatch(event, args):
            owner = getattr(state, "owner", None)
            if owner is not None and not getattr(state, "busy", False):
                state.busy = True
                try: owner.observe(event, args)
                finally: state.busy = False
        setattr(sys, _DISPATCH_NAME, state)
        sys.addaudithook(dispatch)
    return state


def _json(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


def _regular(path):
    value = path.lstat()
    if not stat.S_ISREG(value.st_mode) or value.st_nlink != 1:
        raise BuildError("Staging receipt path is not a regular file: " + str(path))
    return value


class Journal:
    def __init__(self, output, identity, proofs, *, resume_owner=None):
        self.output = Path(output).absolute()
        self.root = self.output.with_name(self.output.name + ".staging-resume")
        self.proofs, self.pending, self.phase = proofs, [], None
        self.read_only = [Path(identity[key]).absolute() for key in ("source", "game", "canonicalProject", "canonicalStartup") if identity.get(key)]
        self.state = _dispatcher()
        self.claimed = set()
        expected = {"schema": SCHEMA, "owner": "Quest derived full staging", "identity": identity}
        if self.root.exists():
            if self.root.is_symlink() or getattr(self.root, "is_junction", lambda: False)() or not self.root.is_dir():
                raise BuildError("Staging resume journal is not an owned directory.")
            try:
                _regular(self.root / "owner.json")
                actual = json.loads((self.root / "owner.json").read_text(encoding="utf-8"))
            except (OSError, ValueError) as error:
                raise BuildError("Staging resume owner is missing or corrupt; retained files were preserved.") from error
            if actual != expected:
                raise BuildError("Staging resume inputs/owner differ; retained files were preserved.")
        else:
            if self.output.exists() and any(self.output.iterdir()):
                marker = self.output.parent / "stage-owner.json"
                try: owner = json.loads(marker.read_text(encoding="utf-8"))
                except (OSError, ValueError): owner = None
                if resume_owner is None or owner != resume_owner:
                    raise BuildError("Existing staging files lack an exact parent owner; files were preserved.")
                # Only a previous interrupted *copy* can be adopted. Restored
                # trees need their original phase contexts and latest proofs.
                markers = ("QuestRecovery/canonical-guid-restoration.json", "QuestRecovery/serialized-layout-restoration.json",
                           "QuestRecovery/full-native-pointer-repair.json", "Assets/QuestOriginalCampaign",
                           "quest-campaign-report.json", "quest-startup-report.json")
                if any((self.output / name).exists() for name in markers):
                    raise BuildError("Existing transformed staging files lack their resume journal; files were preserved.")
            self.root.mkdir(parents=True)
            (self.root / "owner.json").write_text(_json(expected), encoding="utf-8")
        database = self.root / "journal.sqlite3"
        if database.is_symlink():
            raise BuildError("Staging journal database is a symlink.")
        self.db = sqlite3.connect(database, timeout=30)
        self.db.execute("PRAGMA journal_mode=WAL")
        # NORMAL retains committed transactions across process termination,
        # without a disk flush per small asset. This is not a power-loss promise.
        self.db.execute("PRAGMA synchronous=NORMAL")
        self.db.executescript("""
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS phases (name TEXT PRIMARY KEY, position INTEGER UNIQUE, payload TEXT, sha TEXT);
            CREATE TABLE IF NOT EXISTS files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, sha TEXT NOT NULL, phase TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS undo (path TEXT PRIMARY KEY, kind TEXT NOT NULL, backup TEXT, size INTEGER,
                                            sha TEXT, mode INTEGER, atime INTEGER, mtime INTEGER);
            CREATE TABLE IF NOT EXISTS auxiliary (path TEXT PRIMARY KEY);
        """)
        self.db.commit()
        self.output.mkdir(parents=True, exist_ok=True)
        self.witnesses = ValidatedFileWitnesses(self.db, self.output, expected)
        self.proofs.witnesses = self.witnesses
        try:
            self.rollback()
            self._qualify()
        except BaseException:
            self.db.close()
            raise

    def close(self):
        self.flush()
        self.db.close()

    def __enter__(self): return self

    def __exit__(self, *args): self.close()

    def _path(self, value, dir_fd=None):
        if not isinstance(value, (str, bytes, os.PathLike)):
            return None
        path = Path(os.fsdecode(value))
        if dir_fd not in (None, -1):
            # Linux shutil.rmtree supplies relative dir_fd paths. Resolve the
            # descriptor explicitly; never treat cwd-relative names as owned.
            try: path = Path(os.readlink("/proc/self/fd/" + str(dir_fd))) / path
            except OSError as error:
                raise BuildError("Unsupported descriptor-relative staging mutation.") from error
        path = Path(os.path.abspath(path))
        if path != self.output and self.output not in path.parents:
            if any(path == root or root in path.parents for root in self.read_only):
                raise BuildError("Staging transform attempted to mutate an original input: " + str(path))
            return None
        for parent in (path, *path.parents):
            if parent == self.output.parent: break
            if parent.is_symlink() or getattr(parent, "is_junction", lambda: False)():
                raise BuildError("Staging mutation crosses a symlink: " + str(path))
        return path

    def _capture(self, path, *, recursive=False):
        if path is None: return
        relative = path.relative_to(self.output).as_posix()
        if recursive and path.is_dir():
            for child in path.iterdir(): self._capture(child, recursive=True)
        if relative in self.claimed: return
        self.witnesses.invalidate(path)
        try: value = path.lstat()
        except FileNotFoundError: value = None
        backup, size, digest, mode, atime, mtime = None, None, None, None, None, None
        if value is None: kind = "absent"
        elif stat.S_ISDIR(value.st_mode): kind = "directory"
        elif stat.S_ISREG(value.st_mode):
            if value.st_nlink != 1:
                raise BuildError("Staging mutation target has multiple hard links: " + str(path))
            kind = "file"
            backup = "backup-" + uuid.uuid4().hex
            saved = self.root / backup
            before = self.proofs.current(path)
            prior = self.proofs.files.get(path.absolute())
            # Hash the bytes streamed into the rollback writer once. A second
            # full backup read for every rewritten YAML asset adds no useful
            # qualification to this invocation's exact writer proof.
            copied = hashlib.sha256(); written = 0
            with path.open("rb") as source, saved.open("xb") as destination:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    if destination.write(chunk) != len(chunk):
                        raise BuildError("Staging rollback backup write was incomplete: " + str(path))
                    copied.update(chunk); written += len(chunk)
            digest = copied.hexdigest()
            if self.proofs.current(path) != before:
                saved.unlink()
                raise BuildError("Staged file changed while recording its rollback preimage: " + str(path))
            if written != value.st_size or _regular(saved).st_size != written:
                raise BuildError("Staging rollback backup size differs: " + str(path))
            if prior is not None and prior[0] == before and prior[1] != digest:
                raise BuildError("Staging rollback backup differs from its current byte proof: " + str(path))
            self.proofs.remember(saved, digest, self.proofs.current(saved))
            size, mode, atime, mtime = value.st_size, stat.S_IMODE(value.st_mode), value.st_atime_ns, value.st_mtime_ns
        else: raise BuildError("Unsupported staging mutation target: " + str(path))
        with self.db:
            self.db.execute("INSERT INTO undo VALUES (?,?,?,?,?,?,?,?)", (relative, kind, backup, size, digest, mode, atime, mtime))
        self.claimed.add(relative)

    def observe(self, event, args):
        if event == "open":
            path, mode, flags = args
            if flags & (os.O_WRONLY | os.O_RDWR | os.O_CREAT | os.O_TRUNC | os.O_APPEND):
                self._capture(self._path(path))
        elif event == "os.rename":
            source, target, source_fd, target_fd = args
            original, destination = self._path(source, source_fd), self._path(target, target_fd)
            # A move across the owned boundary would modify original inputs or
            # publish outside the stage. Current writers only move inside it.
            if (original is None) != (destination is None):
                raise BuildError("Staging transform attempted a move across its owned boundary.")
            if original is not None and original.is_dir() and destination is not None:
                # Destination descendants do not exist yet, but become real
                # paths after a directory rename. Record their absent preimages.
                for child in original.rglob("*"):
                    self._capture(destination / child.relative_to(original))
            self._capture(original, recursive=True); self._capture(destination, recursive=True)
        elif event in ("os.remove", "os.rmdir"):
            self._capture(self._path(args[0], args[1]), recursive=event == "os.rmdir")
        elif event == "os.mkdir":
            path = self._path(args[0], args[2])
            if path is not None and not path.exists(): self._capture(path)
        elif event == "shutil.rmtree":
            self._capture(self._path(args[0], args[1]), recursive=True)
        elif event in ("os.chmod", "os.utime"):
            self._capture(self._path(args[0], args[-1]))
        elif event in ("os.symlink", "os.link"):
            if self._path(args[1]) is not None:
                raise BuildError("Staging transforms cannot create linked output paths.")
        elif event == "subprocess.Popen":
            raise BuildError("External staging writer requires an explicit declared transaction seam.")

    @contextmanager
    def _active(self):
        if getattr(self.state, "owner", None) is not None:
            raise BuildError("Nested staging writer transactions are unsupported.")
        self.state.owner = self
        try: yield
        finally: self.state.owner = None

    def _qualify(self):
        count, size = self.db.execute("SELECT COUNT(*),COALESCE(SUM(size),0) FROM files").fetchone()
        if not count: return
        counter = build_progress.Counter("staging-resume-verify", size, "bytes")
        files = build_progress.Counter("staging-resume-verify-files", count, "files")
        try:
            records = [{"path": relative, "size": expected_size, "sha256": expected, "phase": phase}
                       for relative, expected_size, expected, phase in self.db.execute("SELECT path,size,sha,phase FROM files ORDER BY path")]
            for row, valid in self.witnesses.qualify_many(records):
                relative, expected_size, expected, phase = row["path"], row["size"], row["sha256"], row["phase"]
                try:
                    path = self.output / relative
                    observed = _regular(path)  # retain staging's stricter one-link writer rule
                    valid = valid and observed.st_size == expected_size
                    if valid:
                        self.proofs.files[path.absolute()] = (observed.st_dev, observed.st_ino, observed.st_size,
                                                             observed.st_mtime_ns, observed.st_ctime_ns), expected
                except (OSError, BuildError) as error:
                    raise BuildError("Retained staged bytes could not be qualified (phase " + phase + "): " + relative) from error
                if not valid:
                    raise BuildError("Retained staged bytes differ from the last completed write (phase " + phase + "): " + relative)
                counter.add(expected_size, relative); files.add(1, relative)
            counter.finish(); files.finish()
            self.db.commit()
            build_progress.event("staging-resume-verify", size, size, "bytes", self.witnesses.summary(), status="complete")
        except BaseException as error:
            counter.fail(error); files.fail(error); raise

    def record(self, path):
        path = Path(path)
        hashed = self.proofs.published(path)
        self.witnesses.remember(path, hashed)
        self.pending.append((path.relative_to(self.output).as_posix(), _regular(path).st_size, hashed, self.phase or "copy-incomplete"))
        if len(self.pending) >= BATCH: self.flush()

    def flush(self):
        if self.pending:
            with self.db: self.db.executemany("INSERT OR REPLACE INTO files VALUES (?,?,?,?)", self.pending)
            self.pending.clear()

    def _publish(self):
        rows = list(self.db.execute("SELECT path,backup FROM undo"))
        for relative, _ in rows:
            path = self.output / relative
            if path.is_file():
                self.db.execute("INSERT OR REPLACE INTO files VALUES (?,?,?,?)", (relative, _regular(path).st_size, self.proofs.digest(path), self.phase or "unknown"))
            else: self.db.execute("DELETE FROM files WHERE path=?", (relative,))
        self.db.execute("DELETE FROM undo")
        self.claimed.clear()
        return rows

    def accept(self):
        """Accept one completed runtime task without replaying earlier copies."""
        with self.db: rows = self._publish()
        self._clean_backups(rows)

    def _clean_backups(self, rows):
        for _, backup in rows:
            if backup:
                path = self.root / backup
                path.unlink(missing_ok=True)
                self.proofs.files.pop(path.absolute(), None)

    def rollback(self):
        """Restore exactly the preimages of the unfinished phase, including moves."""
        rows = list(self.db.execute("SELECT path,kind,backup,size,sha,mode,atime,mtime FROM undo"))
        # Validate every preimage before touching the stage. A damaged journal
        # cannot silently discard original bytes or reset the whole project.
        for relative, kind, backup, size, digest, *_ in rows:
            path = self._path(self.output / relative)
            if path is None: raise BuildError("Staging rollback path escapes its owned project.")
            if kind not in ("file", "directory", "absent"):
                raise BuildError("Staging rollback receipt has an unknown preimage kind: " + relative)
            if kind == "file":
                if not isinstance(backup, str) or not re.fullmatch(r"backup-[0-9a-f]{32}", backup):
                    raise BuildError("Staging rollback backup escapes its owned journal: " + relative)
                saved = self.root / backup
                if _regular(saved).st_size != size or self.proofs.digest(saved) != digest:
                    raise BuildError("Staging rollback preimage is corrupt; files were preserved: " + relative)
        for relative, kind, *_ in sorted(rows, key=lambda row: len(Path(row[0]).parts), reverse=True):
            path = self.output / relative
            self.witnesses.invalidate(path)
            if path.is_file(): path.unlink()
            elif path.exists() and kind != "directory":
                # All children of a moved/removed directory have their own undo
                # row. Never rmtree an unexpected surviving descendant.
                try: path.rmdir()
                except OSError as error: raise BuildError("Staging rollback found an unrecorded directory member: " + relative) from error
        for relative, kind, backup, size, digest, mode, atime, mtime in sorted(rows, key=lambda row: len(Path(row[0]).parts)):
            path = self.output / relative
            if kind == "directory": path.mkdir(parents=True, exist_ok=True)
            elif kind == "file":
                path.parent.mkdir(parents=True, exist_ok=True)
                # Use the same exact streamed writer contract as normal
                # staging; a rollback must not publish an unchecked short copy.
                from full_assets import _copy_recovered
                _copy_recovered(self.root / backup, path, digest, expected_size=size,
                                proofs=self.proofs, phase="staging-rollback-file")
                os.chmod(path, mode); os.utime(path, ns=(atime, mtime))
                self.proofs.remember(path, digest, self.proofs.current(path))
        for (relative,) in list(self.db.execute("SELECT path FROM auxiliary")):
            auxiliary = Path(relative)
            if auxiliary != self.output.with_name(self.output.name + "-native-restoration") or auxiliary.is_symlink():
                raise BuildError("Staging auxiliary rollback owner differs; files were preserved.")
            if auxiliary.exists(): shutil.rmtree(auxiliary)
        with self.db:
            self.db.execute("DELETE FROM undo")
            self.db.execute("DELETE FROM auxiliary")
            self.db.execute("DELETE FROM meta WHERE key='active'")
        self.claimed.clear()
        self._clean_backups([(row[0], row[2]) for row in rows])
        # A process can die between writing a backup and publishing its undo
        # row. Such unreferenced private backups are safe to remove.
        for path in self.root.glob("backup-*"):
            if re.fullmatch(r"backup-[0-9a-f]{32}", path.name) and path.is_file() and not path.is_symlink():
                path.unlink()
                self.proofs.files.pop(path.absolute(), None)

    def run(self, name, position, action, *, copy=False, auxiliary=None):
        previous = self.db.execute("SELECT payload,sha,position FROM phases WHERE name=?", (name,)).fetchone()
        if previous is not None:
            payload, expected, recorded_position = previous
            if recorded_position != position or hashlib.sha256(payload.encode()).hexdigest() != expected:
                raise BuildError("Staging phase receipt is corrupt: " + name)
            build_progress.event("staging-section:" + name, 1, 1, "steps", status="reuse")
            return json.loads(payload)
        if self.db.execute("SELECT COUNT(*) FROM phases").fetchone()[0] != position:
            raise BuildError("Staging phase receipts are not a completed prefix: " + name)
        build_progress.event("staging-section:" + name, 0, 1, "steps", status="start")
        try:
            self.phase = name
            with self.db:
                self.db.execute("INSERT OR REPLACE INTO meta VALUES ('active',?)", (name,))
                if auxiliary is not None:
                    auxiliary = Path(auxiliary).absolute()
                    if auxiliary.exists(): raise BuildError("Native staging workspace lacks a fresh journal owner: " + str(auxiliary))
                    self.db.execute("INSERT INTO auxiliary VALUES (?)", (str(auxiliary),))
            if copy: result = action()
            else:
                with self._active(): result = action()
            self.flush()
            payload = _json(result)
            with self.db:
                rows = self._publish()
                self.db.execute("INSERT INTO phases VALUES (?,?,?,?)", (name, position, payload, hashlib.sha256(payload.encode()).hexdigest()))
                self.db.execute("DELETE FROM meta WHERE key='active'")
                self.db.execute("DELETE FROM auxiliary")
            self._clean_backups(rows)
            build_progress.event("staging-section:" + name, 1, 1, "steps", status="complete")
            return result
        except BaseException as error:
            self.flush()
            self.rollback()
            build_progress.event("staging-section:" + name, detail="Failed: " + type(error).__name__, status="failed")
            raise
        finally:
            self.phase = None
