"""Retain local test evidence while removing explicitly owned generated caches.

Only run-test-suites.py may mark a run successful, after observing exit code zero.
Unmarked runs (including failed, interrupted and legacy runs) are never pruned.
Completion cleanup can remove their project caches while preserving all evidence.
The success-only suite mapping and explicit completion paths protect hardware logs.
Place a .keep file in a generated run to retain it for manual inspection.
"""

from __future__ import annotations

import errno
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import tempfile
import time

try:
    import fcntl as _fcntl
except ImportError:  # Windows
    _fcntl = None
try:
    import msvcrt as _msvcrt
except ImportError:  # POSIX
    _msvcrt = None


SUITE_ROOTS = {
    'town-activity': 'town-activity',
    'town-activity-portable': 'town-activity',
    'town-cloth': 'town-cloth-native',
    'town-voice': 'town-voice',
    'town-native-audio': 'town-native-audio',
    'town-face': 'town-face',
    'town-ritual-transactions': 'town-ritual-transaction',
    'town-service-catalog': 'town-service-catalog',
    'town-service-interaction': 'town-service-interaction',
    'town-service-mirror': 'town-service-mirror',
    'town-service-workspace': 'town-service-workspace',
    'town-service-decor': 'town-decor',
    'town-flame': 'town-flame',
    'town-ritual-layout': 'town-ritual-layout',
    'town-enhancement-handoff': 'town-enhancement-handoff',
    'town-service-clearance': 'town-service-clearance',
    'town-merchant-handoff': 'town-merchant-handoff',
    'town-card-slots': 'town-card-slots',
    'town-public-item-fronts': 'town-public-item-fronts',
    'town-visitor-motion': 'town-visitor-motion',
    'town-depth-order': 'town-depth-order',
    'town-visitor-stock': 'town-visitor-stock',
    'town-merchant-badge': 'town-merchant-badge',
}
MARKER = '.suite-success.json'
RUN_NAME = re.compile(r'run-[a-z0-9_]{8}\Z')

# Completion cleanup is deliberately independent of the success-only mapping
# above. New suites can name their own generated run without granting permission
# to sweep a category or to remove failed evidence. Bounds fail closed.
MAX_LOG_BYTES = 32 * 1024 * 1024
MAX_PROJECT_DIRECTORIES = 512
MAX_PROJECT_DEPTH = 5
MAX_PROJECT_ENTRIES = 50000
MAX_CACHE_ENTRIES = 250000
MAX_RETAINED_REPORT_BYTES = 64 * 1024 * 1024
REPORT_DIRECTORY = '.retained-cache-reports'
_PROTECTED_COMPONENTS = {'remote', 'hardware', 'steamframe'}
_PROJECT_SCAN_SKIP = {'bin', 'obj', 'Library', 'Temp', 'Obj', 'Logs', 'Assets',
                      'Packages', 'ProjectSettings', 'Sources', '.git', REPORT_DIRECTORY}
_PRESERVED_TREES = {'sources', 'source', 'src', 'captures', 'manifests'}
_REPORT_SUFFIXES = {'.log', '.json', '.xml', '.trx', '.junit', '.html', '.htm',
                    '.txt', '.md', '.csv', '.yaml', '.yml', '.sha256', '.sha512',
                    '.sha1', '.md5', '.manifest', '.dmp', '.dump', '.crash',
                    '.png', '.jpg', '.jpeg', '.gif', '.bmp', '.svg', '.tif',
                    '.tiff', '.mp4', '.webm', '.mov', '.cs', '.py', '.sh',
                    '.ps1', '.csproj', '.sln', '.props', '.targets'}


def _pinned(path: Path) -> bool:
    marker = path / '.keep'
    return marker.exists() or marker.is_symlink()


def _protected_name(name: str) -> bool:
    return re.sub(r'[-_]', '', name.lower()) in _PROTECTED_COMPONENTS


def _physical_path(path: Path, base: Path) -> bool:
    """Accept only a lexical descendant with no borrowed/symlink component."""
    try:
        relative = path.relative_to(base)
    except ValueError:
        return False
    if '..' in relative.parts:
        return False
    current = base
    for component in (None, *relative.parts):
        if component is not None:
            current /= component
        if current.is_symlink():
            return False
    return True


def _generated_run(path: Path, checkout: Path) -> Path | None:
    debug = checkout / '.planning/debug'
    if not path.is_absolute() or not _physical_path(path, checkout):
        return None
    try:
        relative = path.relative_to(debug)
    except ValueError:
        return None
    if any(_protected_name(part) for part in relative.parts):
        return None
    runs = [debug.joinpath(*relative.parts[:index + 1])
            for index, part in enumerate(relative.parts) if RUN_NAME.fullmatch(part)]
    # Nested run names make ownership ambiguous; no ancestor inference is allowed.
    if len(runs) != 1 or not runs[0].is_dir():
        return None
    return runs[0]


def _completion_run(log_path: Path, checkout: Path) -> Path | None:
    """Read explicit evidence, or an exact failed-build project path, never glob."""
    if log_path.is_symlink() or not log_path.is_file() or log_path.stat().st_size > MAX_LOG_BYTES:
        return None
    evidence, projects = [], []
    with log_path.open(errors='replace') as log:
        for line in log:
            match = re.search(r'evidence:\s*(.*?)\s*$', line, re.IGNORECASE)
            if match:
                evidence.append(Path(match[1]))
            projects.extend(Path(value) for value in re.findall(r'\[([^\[\]\r\n]+\.csproj)\]', line))
            match = re.search(r'-projectPath\s+(?:"([^"]+)"|\'([^\']+)\'|(\S+))', line)
            if match:
                projects.append(Path(next(value for value in match.groups() if value is not None)))
    # If evidence is present it must be wholly unambiguous. A rejected hardware
    # reference cannot be replaced by a conveniently local compiler error.
    references = evidence or projects
    if not references:
        return None
    candidates = [_generated_run(path, checkout) for path in references]
    if any(run is None for run in candidates) or len(set(candidates)) != 1:
        return None
    return candidates[0]


def _project_caches(run: Path) -> list[Path]:
    """Bounded discovery inside the one evidenced run, with physical markers."""
    pending = [(run, 0)]
    visited = 0
    entries = 0
    caches = set()
    while pending:
        project, depth = pending.pop()
        visited += 1
        if visited > MAX_PROJECT_DIRECTORIES:
            raise ValueError('Generated project discovery exceeds its directory bound')
        if _pinned(project) or _protected_name(project.name) or not _physical_path(project, run):
            continue
        children = []
        for child in project.iterdir():
            entries += 1
            if entries > MAX_PROJECT_ENTRIES:
                raise ValueError('Generated project discovery exceeds its entry bound')
            children.append(child)
        managed = any(child.suffix == '.csproj' and child.is_file() and not child.is_symlink()
                      for child in children)
        version = project / 'ProjectSettings/ProjectVersion.txt'
        unity = (project.joinpath('Assets').is_dir() and _physical_path(project / 'Assets', run)
                 and _physical_path(version, run) and version.is_file()
                 and version.stat().st_size < 4096
                 and re.search(r'^m_EditorVersion:\s*\d+\.\d+\.\S+', version.read_text(), re.MULTILINE))
        names = ({'bin', 'obj'} if managed else set()) | ({'Library', 'Temp', 'Obj'} if unity else set())
        # Unity Logs is evidence, not a cache target. Real source/project/evidence
        # trees likewise stay in place; only these original sibling names qualify.
        caches.update(project / name for name in names if (project / name).exists() or (project / name).is_symlink())
        if depth < MAX_PROJECT_DEPTH:
            pending.extend((child, depth + 1) for child in children
                           if child.is_dir() and not child.is_symlink() and child.name not in _PROJECT_SCAN_SKIP
                           and child.name.lower() not in _PRESERVED_TREES
                           and not _protected_name(child.name))
    return sorted(caches)


def _cache_inventory(cache: Path, run: Path) -> tuple[list[tuple[Path, os.stat_result]], tuple]:
    if not _physical_path(cache, run) or not cache.is_dir():
        raise ValueError('Cache is not a physical generated directory')
    if any(_pinned(ancestor) for ancestor in (cache, *cache.parents) if ancestor == run or run in ancestor.parents):
        raise ValueError('Cache or generated project is pinned')
    files, fingerprint, pending = [], [], [cache]
    while pending:
        current = pending.pop()
        for child in current.iterdir():
            info = child.lstat()
            if child.name == '.keep' or _protected_name(child.name) or stat.S_ISLNK(info.st_mode):
                raise ValueError('Cache contains a pin or borrowed input')
            if stat.S_ISDIR(info.st_mode) and child.name.lower() in _PRESERVED_TREES:
                raise ValueError('Cache contains a source, manifest or capture tree')
            if len(fingerprint) >= MAX_CACHE_ENTRIES:
                raise ValueError('Cache exceeds its entry bound')
            fingerprint.append((str(child.relative_to(cache)), info.st_mode, info.st_dev,
                                info.st_ino, info.st_size, info.st_mtime_ns))
            if stat.S_ISDIR(info.st_mode):
                pending.append(child)
            elif stat.S_ISREG(info.st_mode):
                files.append((child, info))
            else:
                raise ValueError('Cache contains a non-regular input')
    return files, tuple(sorted(fingerprint))


def _is_report(path: Path, cache: Path) -> bool:
    parts = [part.lower() for part in path.relative_to(cache).parts]
    return (path.suffix.lower() in _REPORT_SUFFIXES
            or any(re.search(r'result|report|crash|capture|receipt|manifest|sha256|sha512', part) for part in parts))


def _digest(path: Path) -> str:
    if path.stat().st_size > MAX_RETAINED_REPORT_BYTES:
        raise ValueError('Retained evidence exceeds its byte bound')
    value = hashlib.sha256()
    count = 0
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            count += len(chunk)
            if count > MAX_RETAINED_REPORT_BYTES:
                raise ValueError('Retained evidence grew beyond its byte bound')
            value.update(chunk)
    return value.hexdigest()


def _copy_report(reader, writer, expected_size: int) -> None:
    count = 0
    for chunk in iter(lambda: reader.read(1024 * 1024), b''):
        count += len(chunk)
        if count > expected_size:
            raise ValueError('Retained evidence grew during its copy')
        writer.write(chunk)
    if count != expected_size:
        raise ValueError('Retained evidence shrank during its copy')


def _retain_reports(cache: Path, run: Path, files: list[tuple[Path, os.stat_result]]) -> int:
    reports = [(path, info) for path, info in files if _is_report(path, cache)]
    if sum(info.st_size for _, info in reports) > MAX_RETAINED_REPORT_BYTES:
        raise ValueError('Cache evidence exceeds its retention byte bound')
    copied = 0
    for source, info in reports:
        destination = run / REPORT_DIRECTORY / source.relative_to(run)
        if not _physical_path(destination, run):
            raise ValueError('Retained evidence destination contains a symlink')
        if any(_pinned(ancestor) for ancestor in destination.parents if ancestor == run or run in ancestor.parents):
            raise ValueError('Retained evidence destination is pinned')
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists():
            if not destination.is_file() or _digest(source) != _digest(destination):
                raise ValueError('Retained evidence already exists with different bytes')
        else:
            # Publish a complete, verified receipt exclusively. A failed copy
            # leaves no truncated destination to block a later safe retry.
            temporary = None
            try:
                with source.open('rb') as reader, tempfile.NamedTemporaryFile(
                        mode='wb', prefix='.retaining-', dir=destination.parent, delete=False) as writer:
                    temporary = Path(writer.name)
                    _copy_report(reader, writer, info.st_size)
                if _digest(source) != _digest(temporary):
                    raise ValueError('Retained evidence copy did not preserve exact bytes')
                os.link(temporary, destination)
            finally:
                if temporary is not None:
                    temporary.unlink(missing_ok=True)
            copied += info.st_size
    return copied


def cleanup_completed_run(suite_id: str, exit_code: int, log_path: Path, checkout: Path,
                          *, process_reaped: bool = False) -> dict:
    """Remove only reproducible project caches after a terminated/reaped suite.

    Callers must first kill the suite process group and reap its parent. Failed
    and cancelled runs qualify for cache cleanup, never success marking. This
    separate API also lets a monitor reload cleanup for an already-running old
    runner. No known-root mapping, global sweep or hardware-log discovery occurs.
    Missing/ambiguous evidence, pins, borrowed inputs or unreadable/oversized
    receipts fail closed. Logs stays intact; cache receipts are copied byte for
    byte below .retained-cache-reports before the respective cache is deleted.
    """
    result = {'run': None, 'removed_paths': [], 'removed_bytes': 0, 'skipped_paths': []}
    if process_reaped is not True or type(exit_code) is not int or not re.fullmatch(r'[a-z0-9][a-z0-9-]*', suite_id):
        return result
    checkout = Path(checkout).absolute()
    run = _completion_run(Path(log_path), checkout)
    if run is None or _pinned(run):
        return result
    result['run'] = str(run)
    lock_path = run / '.cache-cleanup.lock'
    if lock_path.is_symlink():
        result['skipped_paths'].append(str(lock_path))
        return result
    with lock_path.open('a+b') as lock:
        try:
            acquire_file_lock(lock, blocking=False)
        except BlockingIOError:
            result['skipped_paths'].append(str(run))
            return result
        try:
            if _pinned(run):
                return result
            caches = _project_caches(run)
            for cache in caches:
                try:
                    files, fingerprint = _cache_inventory(cache, run)
                    copied = _retain_reports(cache, run, files)
                    if _cache_inventory(cache, run)[1] != fingerprint:
                        raise ValueError('Cache changed while retaining evidence')
                    shutil.rmtree(cache)
                    result['removed_paths'].append(str(cache))
                    result['removed_bytes'] += max(0, sum(info.st_size for _, info in files) - copied)
                except (OSError, ValueError):
                    result['skipped_paths'].append(str(cache))
            return result
        finally:
            release_file_lock(lock)


def acquire_file_lock(stream, *, blocking: bool = True) -> None:
    """Lock the first byte of a binary file on POSIX or Windows."""
    if _fcntl is not None:
        flags = _fcntl.LOCK_EX | (0 if blocking else _fcntl.LOCK_NB)
        _fcntl.flock(stream, flags)
        return
    if _msvcrt is None:
        raise OSError('No file locking implementation is available')
    # msvcrt.locking() locks from the current file offset. All callers use
    # a+b, and this reserved byte gives Windows an identical lock range.
    stream.seek(0, 2)
    if stream.tell() == 0:
        stream.write(b'\0')
        stream.flush()
    stream.seek(0)
    while True:
        try:
            _msvcrt.locking(stream.fileno(), _msvcrt.LK_NBLCK, 1)
            return
        except OSError as exc:
            busy = exc.errno in (errno.EACCES, errno.EAGAIN, errno.EDEADLK) or getattr(exc, 'winerror', None) in (33, 36)
            if not busy:
                raise
            if not blocking:
                raise BlockingIOError(errno.EAGAIN, 'File lock is held') from exc
            time.sleep(.05)


def release_file_lock(stream) -> None:
    if _fcntl is not None:
        _fcntl.flock(stream, _fcntl.LOCK_UN)
    else:
        stream.seek(0)
        _msvcrt.locking(stream.fileno(), _msvcrt.LK_UNLCK, 1)


def _evidence_path(log_path: Path, expected_root: Path) -> Path | None:
    """Accept one exact, direct run child named by the successful suite's log."""
    candidates = set()
    with log_path.open(errors='replace') as log:
        for line in log:
            match = re.search(r'evidence:\s*(.*?)\s*$', line, re.IGNORECASE)
            if match is None:
                continue
            candidate = Path(match[1])
            if not candidate.is_absolute() or candidate.is_symlink() or not RUN_NAME.fullmatch(candidate.name):
                continue
            if candidate.parent.resolve() != expected_root or not candidate.is_dir():
                continue
            candidates.add(candidate.resolve())
    return next(iter(candidates)) if len(candidates) == 1 else None


def _marked_runs(output_root: Path, suite_id: str) -> list[Path]:
    marked = []
    for run in output_root.iterdir():
        if run.is_symlink() or not run.is_dir() or not RUN_NAME.fullmatch(run.name):
            continue
        marker = run / MARKER
        if marker.is_symlink() or not marker.is_file() or _pinned(run):
            continue
        try:
            value = json.loads(marker.read_text())
            if value != {'schema_version': 1, 'suite': suite_id}:
                continue
            marked.append(run)
        except (OSError, ValueError):
            continue
    return marked


def record_success(suite_id: str, exit_code: int, log_path: Path, checkout: Path) -> tuple[Path | None, int]:
    """Mark a successful generated run, then remove older marked runs of that suite.

    The runner calls this only after the child process is reaped. The exit_code
    guard is repeated here so a future caller cannot mark a failed run by mistake.
    """
    if exit_code != 0 or suite_id not in SUITE_ROOTS:
        return None, 0
    checkout = checkout.resolve()
    planning = checkout / '.planning'
    debug = planning / 'debug'
    output_root = debug / SUITE_ROOTS[suite_id]
    # Worktrees may borrow generated inputs via symlinks. Never prune a shared
    # checkout's evidence through one of those links.
    if any(path.is_symlink() for path in (planning, debug, output_root)) or not output_root.is_dir():
        return None, 0
    output_root = output_root.resolve()
    run = _evidence_path(log_path, output_root)
    if run is None:
        return None, 0

    # Activity's Unity and portable variants share one output root. A root lock
    # serializes their retention passes without serializing the actual suites.
    with (output_root / '.retention.lock').open('a+b') as lock:
        acquire_file_lock(lock)
        try:
            marker = run / MARKER
            if not marker.exists():
                with marker.open('x') as stream:
                    json.dump({'schema_version': 1, 'suite': suite_id}, stream)
                    stream.write('\n')
            removed = 0
            # This suite's process has just exited successfully, so its run is
            # newest even on filesystems with coarse mtime precision.
            for old in _marked_runs(output_root, suite_id):
                if old == run:
                    continue
                # A reviewer may pin a run while waiting for the category lock.
                if _pinned(old):
                    continue
                shutil.rmtree(old)
                removed += 1
            return run, removed
        finally:
            release_file_lock(lock)
