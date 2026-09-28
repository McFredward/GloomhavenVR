"""Retain one completed local test run per town suite without touching hardware logs.

Only run-test-suites.py may mark a run successful, after observing exit code zero.
Unmarked runs (including failed, interrupted and legacy runs) are never removed.
The explicit suite-to-output mapping keeps this away from user-supplied debug logs.
Place a .keep file in a generated run to retain it for manual inspection.
"""

from __future__ import annotations

import fcntl
import json
from pathlib import Path
import re
import shutil


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
}
MARKER = '.suite-success.json'
RUN_NAME = re.compile(r'run-[a-z0-9_]{8}\Z')


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
        if marker.is_symlink() or not marker.is_file() or (run / '.keep').exists():
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
    with (output_root / '.retention.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        marker = run / MARKER
        if not marker.exists():
            with marker.open('x') as stream:
                json.dump({'schema_version': 1, 'suite': suite_id}, stream)
                stream.write('\n')
        removed = 0
        # This suite's process has just exited successfully, so its run is the
        # newest complete one even on filesystems with coarse mtime precision.
        for old in _marked_runs(output_root, suite_id):
            if old == run:
                continue
            # A reviewer may pin a run while waiting for the category lock.
            if (old / '.keep').exists():
                continue
            shutil.rmtree(old)
            removed += 1
        return run, removed
