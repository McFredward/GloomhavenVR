"""Cheap, finite known-asset coverage before Unity can load Editor observers.

This inventory is deliberately named coverage: Unity's dependency scheduler may
reimport a covered asset. Native task totals supersede it whenever available.
No asset payload is read and optional telemetry can never fail a build.
"""
from __future__ import annotations
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import sqlite3
import time
import uuid

SCHEMA = 1
MAX_CONTROL_BYTES = 16384
MAX_ASSETS = 1000000
MAX_PACKAGE_CONTROL_BYTES = 4 * 1048576
DB_NAME = ".quest-unity-work.sqlite3"
IDENTITY_NAME = ".quest-unity-work.identity"
CONTEXT_SUFFIXES = frozenset((".cs", ".dll", ".asmdef", ".asmref", ".shader", ".compute", ".hlsl", ".cginc", ".glslinc"))


def _json(path, limit):
    if path.is_symlink() or not path.is_file() or path.stat().st_size > limit: return None
    with path.open("rb") as stream: raw = stream.read(limit + 1)
    if len(raw) > limit: return None
    value = json.loads(raw)
    return value if isinstance(value, dict) else None


def _atomic(path, value):
    temporary = path.with_name(path.name + ".part")
    with temporary.open("w", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, separators=(",", ":"))
        stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, path)


def sidecar(log): return Path(str(log) + ".work.json")


def _stat(path):
    if path.is_symlink(): return None
    try:
        value = path.stat()
        return [value.st_size, value.st_mtime_ns, value.st_ctime_ns, value.st_dev, value.st_ino]
    except (FileNotFoundError, NotADirectoryError): return None


def _argument(argv, name, cwd):
    if name not in argv: return None
    index = argv.index(name)
    if index + 1 == len(argv): return None
    path = Path(argv[index + 1])
    return (Path(cwd or os.getcwd()) / path).absolute() if not path.is_absolute() else path.absolute()


def _package_roots(project):
    """Only currently resolved/embedded packages, never every cached version."""
    roots = {}
    packages = project / "Packages"
    if packages.is_dir() and not packages.is_symlink():
        for child in packages.iterdir():
            if child.is_dir() and not child.is_symlink() and (child / "package.json").is_file():
                roots[child.name] = child
    lock = _json(packages / "packages-lock.json", MAX_PACKAGE_CONTROL_BYTES) or {}
    dependencies = lock.get("dependencies", {})
    cache = project / "Library/PackageCache"
    if isinstance(dependencies, dict) and cache.is_dir() and not cache.is_symlink():
        for name, value in dependencies.items():
            if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9_.-]+", name) or name in roots or not isinstance(value, dict): continue
            version = value.get("version")
            if not isinstance(version, str) or not re.fullmatch(r"[A-Za-z0-9_.+-]+", version): continue
            child = cache / (name + "@" + version)
            if child.is_dir() and not child.is_symlink(): roots[name] = child
    return [("Packages/" + name, root) for name, root in sorted(roots.items())]


def _walk(root, prefix):
    if not root.is_dir() or root.is_symlink(): return
    pending = [(root, prefix)]
    while pending:
        folder, logical = pending.pop()
        with os.scandir(folder) as entries:
            children = list(entries)
        for entry in sorted(children, key=lambda row: row.name):
            if entry.is_symlink() or entry.name.startswith((".", "~")) or entry.name.endswith("~"): continue
            path = Path(entry.path); relative = logical + "/" + entry.name
            if entry.is_dir(follow_symlinks=False):
                pending.append((path, relative))
            elif entry.is_file(follow_symlinks=False) and not entry.name.endswith(".meta"):
                yield relative, path, [_stat(path), _stat(path.with_name(path.name + ".meta"))]


def _context(project, argv, editor, rows):
    """Global import dependencies make prior coverage conservative on changes."""
    digest = hashlib.sha256()
    def add(value): digest.update(json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode())
    add([str(project), str(editor), _stat(editor), _stat(project / "Library" / IDENTITY_NAME)])
    for flag in ("-buildTarget", "-force-d3d11", "-force-vulkan", "-force-glcore", "-forceGLES", "-forceGLES30"):
        add([flag, argv[argv.index(flag) + 1] if flag in argv and argv.index(flag) + 1 < len(argv) else None])
    for relative, _, witness in rows:
        if PurePosixPath(relative).suffix.lower() in CONTEXT_SUFFIXES: add([relative, witness])
    settings = project / "ProjectSettings"
    if settings.is_dir() and not settings.is_symlink():
        for child in sorted(settings.iterdir()):
            if child.is_file() and not child.is_symlink(): add([child.name, _stat(child)])
    for name in ("manifest.json", "packages-lock.json"): add([name, _stat(project / "Packages" / name)])
    return digest.hexdigest()


def publish_plan(argv, log, progress, *, cwd=None):
    """Write a fresh finite invocation plan. Return None on unavailable telemetry."""
    try:
        argv = list(map(str, argv)); log = Path(log).absolute()
        if callable(getattr(progress, "enabled", None)) and not progress.enabled(): return None
        if "-version" in argv: return None
        project = _argument(argv, "-projectPath", cwd)
        if project is None or not project.is_dir() or project.is_symlink(): return None
        library = project / "Library"
        if library.is_symlink(): return None
        library.mkdir(exist_ok=True)
        identity = library / IDENTITY_NAME
        if identity.is_symlink(): return None
        if not identity.exists():
            # A separate immutable marker's creation metadata survives normal
            # Library/journal writes but rejects copied/replaced caches, even
            # when the filesystem recycles the directory inode immediately.
            with identity.open("xb") as stream: stream.write(uuid.uuid4().hex.encode("ascii"))
        editor = next((Path(value) for value in argv if Path(value).name.casefold() in ("unity", "unity.exe")), Path(argv[0]))
        roots = [("Assets", project / "Assets")] + _package_roots(project)
        rows = []
        for prefix, root in roots:
            for row in _walk(root, prefix):
                rows.append(row)
                if len(rows) > MAX_ASSETS: return None
        rows.sort(key=lambda row: row[0])
        context = _context(project, argv, editor, rows)
        database = library / DB_NAME
        if database.is_symlink(): return None
        invocation = uuid.uuid4().hex
        with closing(sqlite3.connect(database, timeout=1)) as connection, connection:
            connection.execute("CREATE TABLE IF NOT EXISTS scope (id INTEGER PRIMARY KEY, context TEXT, project TEXT, invocation TEXT)")
            connection.execute("CREATE TABLE IF NOT EXISTS assets (path TEXT PRIMARY KEY, witness TEXT NOT NULL, completed INTEGER NOT NULL, invocation TEXT NOT NULL)")
            previous = connection.execute("SELECT context, project FROM scope WHERE id=1").fetchone()
            if previous != (context, str(project)): connection.execute("DELETE FROM assets")
            connection.execute("INSERT OR REPLACE INTO scope VALUES (1,?,?,?)", (context, str(project), invocation))
            connection.executemany("INSERT INTO assets VALUES (?, ?, 0, ?) ON CONFLICT(path) DO UPDATE SET "
                "completed=CASE WHEN assets.witness=excluded.witness THEN assets.completed ELSE 0 END, "
                "witness=excluded.witness, invocation=excluded.invocation",
                ((relative, json.dumps(witness, separators=(",", ":")), invocation) for relative, _, witness in rows))
            connection.execute("DELETE FROM assets WHERE invocation<>?", (invocation,))
            done = connection.execute("SELECT count(*) FROM assets WHERE completed=1").fetchone()[0]
        logs = list(dict.fromkeys([log, _argument(argv, "-logFile", cwd) or log]))
        plan = {"schema": SCHEMA, "invocationId": invocation, "createdAt": time.time(),
                "project": str(project), "database": str(database), "databaseIdentity": _stat(database)[3:],
                "context": context, "done": done, "total": len(rows),
                "scopes": [prefix for prefix, _ in roots],
                "logs": [{"path": str(path), "before": _stat(path)} for path in logs]}
        for path in logs:
            path.parent.mkdir(parents=True, exist_ok=True)
            _atomic(sidecar(path), plan)
        progress.event("unity-work-invocation", 0, 1, "invocations", "Unity process started", status="start")
        progress.event("unity-work-plan", done, len(rows), "assets",
                       "[asset-coverage] Known project/package assets: " + str(done) + " / " + str(len(rows)), status="start")
        return plan
    except (OSError, ValueError, TypeError, sqlite3.Error, OverflowError):
        # Telemetry is never a reason to rerun a conversion or refuse a build.
        return None


def finish_plan(plan, *, success, progress):
    """Only the actual process result closes its finite invocation envelope."""
    if not plan: return
    try:
        progress.event("unity-work-invocation", 1 if success else 0, 1, "invocations",
                       "Unity process completed" if success else "Unity process failed",
                       status="complete" if success else "failed")
    except (OSError, ValueError, TypeError):
        pass


class Coverage:
    """Live unique-path observations with crash-safe bounded batch publication."""
    def __init__(self, plan):
        self.plan = plan
        self.connection = sqlite3.connect(plan["database"], timeout=.05)
        try:
            scope = self.connection.execute("SELECT context, project, invocation FROM scope WHERE id=1").fetchone()
            if scope != (plan["context"], plan["project"], plan["invocationId"]): raise ValueError("Stale import invocation")
            count = self.connection.execute("SELECT count(*) FROM assets WHERE invocation=?", (plan["invocationId"],)).fetchone()[0]
            if count != plan["total"] or count > MAX_ASSETS: raise ValueError("Invalid asset inventory")
            rows = self.connection.execute("SELECT path, completed FROM assets WHERE invocation=?", (plan["invocationId"],)).fetchall()
        except (sqlite3.Error, ValueError, KeyError, TypeError):
            self.connection.close(); raise
        self.known = {row[0] for row in rows}; self.completed = {row[0] for row in rows if row[1] == 1}
        self.pending = set(); self.last_commit = time.monotonic()

    @property
    def done(self): return len(self.completed)

    def observe(self, logical):
        logical = logical.replace("\\", "/")
        if logical in self.known and logical not in self.completed:
            self.completed.add(logical); self.pending.add(logical)
        if len(self.pending) >= 250 or time.monotonic() - self.last_commit >= .5: self.flush()
        return self.done, self.plan["total"]

    def flush(self):
        if not self.pending: return
        self.connection.executemany("UPDATE assets SET completed=1 WHERE path=? AND invocation=?",
                                    ((path, self.plan["invocationId"]) for path in self.pending))
        self.connection.commit(); self.pending.clear(); self.last_commit = time.monotonic()

    def close(self):
        try: self.flush()
        finally: self.connection.close()


def load_plan(log, *, started=0):
    """Only a fresh owned invocation can supply a tail's fixed denominator."""
    try:
        log = Path(log).absolute(); control = sidecar(log)
        value = _json(control, MAX_CONTROL_BYTES)
        if not value or value.get("schema") != SCHEMA or not re.fullmatch(r"[0-9a-f]{32}", str(value.get("invocationId"))): return None
        if type(value.get("createdAt")) not in (int, float) or value["createdAt"] < started: return None
        if type(value.get("total")) is not int or not 0 <= value["total"] <= MAX_ASSETS: return None
        project = Path(value["project"]); database = Path(value["database"])
        if not project.is_absolute() or project.is_symlink() or database != project / "Library" / DB_NAME or database.is_symlink(): return None
        if _stat(database)[3:] != value.get("databaseIdentity"): return None
        entry = next((row for row in value["logs"] if row["path"] == str(log)), None)
        stat = _stat(log)
        if not entry or stat is None or stat == entry.get("before") or stat[1] < int(value["createdAt"] * 1e9): return None
        if not isinstance(value.get("context"), str) or not re.fullmatch(r"[0-9a-f]{64}", value["context"]): return None
        return value
    except (OSError, ValueError, KeyError, TypeError, StopIteration, OverflowError): return None
