"""Bounded byte-exact file batches with parent-only resumable publication.

The 2026-10-13 Windows report spends minutes copying about 100,000 already
recovered files serially. These independent copies use a small I/O pool instead
of the multi-GiB native-converter budget. No SQLite connection, byte-proof map,
transformed file or original game writer crosses a worker boundary. Workers
create only exclusive fresh final targets; closed files surviving cancellation
are independently qualified on resume rather than copied again. There is no
second full-tree scratch copy or per-file rollback backup during this phase.
"""
from dataclasses import dataclass
import json
import os
from pathlib import Path
import stat

import asset_jobs
import host_resources
import storage
from storage import BuildError, build_progress

BATCH_FILES = 32
MAX_WORKERS = 16
WORKER_BYTES = 64 * 1024 * 1024


def policy(output, total):
    host = host_resources.detect_host(output)
    requested = host_resources.parse_jobs(os.environ.get(host_resources.JOBS_ENV))
    cpus = max(1, host["effectiveCpus"])
    physical, commit = host.get("availableMemoryBytes"), host.get("commitHeadroomBytes")
    available = min(physical, commit) if physical is not None and commit is not None else physical
    memory_jobs = max(1, (available - min(1024 ** 3, available // 4)) // WORKER_BYTES) if available is not None else 1
    jobs = min(MAX_WORKERS, cpus, requested or cpus, memory_jobs, max(1, (total + BATCH_FILES - 1) // BATCH_FILES))
    value = {"event": "staging-copy-policy", "phase": "file-staging", "jobs": jobs,
             "batchFiles": BATCH_FILES, "totalFiles": total, "effectiveCpus": cpus,
             "workerReserveBytes": WORKER_BYTES, "additionalScratchBytes": 0,
             "availableMemoryBytes": physical, "commitHeadroomBytes": commit}
    print("resources: " + json.dumps(value, sort_keys=True), flush=True)
    if root := os.environ.get(host_resources.EVIDENCE_ENV):
        host_resources.emit_resource_evidence(root, value)
    return jobs


@dataclass(frozen=True)
class Batch:
    members: tuple
    memory_bytes: int = WORKER_BYTES


def _ancestors(path, root, identity):
    """Recheck actual owned directories without resolving outside ancestors.

    Startup's exact safe_path qualification remains in the parent. At worker
    dispatch its already-canonical paths need directory lstat guards, not a
    second full resolve(root)+resolve(file) walk. Reject internal links too;
    owned raw/staged files have no reason to cross a junction or symlink.
    """
    for parent in path.parents:
        value = parent.lstat()
        if not stat.S_ISDIR(value.st_mode) or getattr(value, "st_file_attributes", 0) & 0x400:
            raise BuildError("Parallel staged copy crosses a linked directory: " + str(path))
        if parent == root:
            if (value.st_dev, value.st_ino) != identity:
                raise BuildError("Parallel staged copy owner root changed: " + str(root))
            return
    raise BuildError("Parallel staged copy escapes its owned root: " + str(path))


def copy_rows(source, output, rows, proofs, journal, safe_path, *, jobs=None):
    """Use exact checkpoint order while independently copying bounded batches."""
    from full_assets import _copy_recovered, _qualify_copy, _stamp, WINDOWS_FILE_TIMES
    source, output = Path(source), Path(output)
    source_identity = (source.stat().st_dev, source.stat().st_ino)
    output_identity = (output.stat().st_dev, output.stat().st_ino)
    # Repeated target paths would otherwise race between workers. Reject the
    # malformed checkpoint before scheduling any write rather than overwriting.
    paths = set()
    for row in rows:
        if row["path"] in paths:
            raise BuildError("Recovered copy schedule contains a repeated target: " + row["path"])
        paths.add(row["path"])
    selected = policy(output, len(rows)) if jobs is None else jobs
    counter = build_progress.Counter("staging-copy", len(rows), "files")
    copied = []

    def prepare(batch):
        members = []
        for row in batch:
            original, target = safe_path(source, row["path"]), safe_path(output, row["path"])
            reused = _qualify_copy(original, target, row, proofs, journal)
            # Creating known parents here leaves workers with exact canonical
            # directory identities and avoids missing ancestor ambiguities.
            if not reused: target.parent.mkdir(parents=True, exist_ok=True)
            members.append((row, original, target, reused))
        return Batch(tuple(members))

    def execute(batch, cancellation):
        completed = []
        for row, original, target, reused in batch.members:
            cancellation.check()
            if reused:
                completed.append(None)
                continue
            # Qualify ancestors again at actual dispatch, since other files in
            # the bounded batch may have been queued while its predecessor ran.
            _ancestors(original, source, source_identity)
            _ancestors(target, output, output_identity)
            completed.append(_copy_recovered(original, target, row["sha256"],
                expected_size=row.get("bytes", row.get("size")), proofs=None,
                cancellation=cancellation, receipt=True))
        return completed

    def publish(batch, completed):
        for (row, original, target, reused), result in zip(batch.members, completed):
            if not reused:
                _ancestors(original, source, source_identity)
                actual, original_stamp, target_stamp, native_source, native_target = result
                if (_stamp(original.lstat()) != original_stamp or
                        proofs.current(target) != target_stamp):
                    raise BuildError("Parallel staged copy changed before publication: " + row["path"])
                if WINDOWS_FILE_TIMES:
                    if (native_source is not None and storage._file_witness_stamp(original, original.lstat()) != native_source or
                            native_target is not None and storage._file_witness_stamp(target, target.lstat()) != native_target):
                        raise BuildError("Parallel staged copy native change stamp changed before publication: " + row["path"])
                    if native_target is None and proofs.digest(target) != actual:
                        raise BuildError("Parallel staged copy differs from its worker bytes: " + row["path"])
                proofs.remember(target, actual, target_stamp, witness_stamp=native_target)
                proofs.remember(original, actual, original_stamp)
                journal.record(target)
            else:
                # The parent qualified these bytes before dispatch. Preserve
                # the original serial publication guard if another writer
                # changes the staged target while a sibling is copying.
                if proofs.published(target) != row["sha256"]:
                    raise BuildError("Retained staged copy changed before publication: " + row["path"])
            copied.append({"path": row["path"], "sha256": row["sha256"]})
            counter.add(1, row["path"])

    try:
        batches = (rows[index:index + BATCH_FILES] for index in range(0, len(rows), BATCH_FILES))
        asset_jobs.ordered_pipeline(batches, prepare, execute, publish, jobs=selected,
            byte_budget=selected * WORKER_BYTES)
        counter.finish()
        return copied
    except BaseException as error:
        counter.fail(error)
        raise
