"""Adapt native work to measured capacity without a fixed RAM admission gate.

Capture165405 stopped before its first compiler despite successful 32 GiB Linux
builds. Full C++ local-variable DWARF caused the historic giant dispatcher's
~26 GiB peak. The exact pinned -Os action with line tables uses <0.5 GiB instead.
Keep runtime optimization and source-line symbols; schedule against that actual
workload, and recover a genuine allocation failure using the same Bee outputs.
"""
from __future__ import annotations

import os
import errno
from pathlib import Path
import re
import time

from storage import BuildError, recover_player_exclusions

PHASE = "native-memory-check"
PROFILE = "release-line-tables"
# Repeat the existing depth flag because Unity's options parser can replace a
# scalar compiler-flags value. Existing PlayerSettings retains the LLD selection.
# IL2CPPUtils.GetAdditionalArguments appends this process-local environment field
# after PlayerSettings in the pinned Editor. No template/import cache is changed.
EXTRA_ARGS = '--compiler-flags="-fbracket-depth=1024 -gline-tables-only"'
GIB = 1024 ** 3
MAX_LOG_BYTES = 2 * 1024 * 1024
MEMORY_ERROR = re.compile(
    r"LLVM ERROR:\s*out of memory|std::bad_alloc|System\.OutOfMemoryException|"
    r"(?:fatal error|error|failed)[^\r\n]{0,120}(?:out of memory|cannot allocate memory)|"
    r"(?:out of memory|not enough (?:memory|storage)|paging file is too small)[^\r\n]{0,120}(?:error|allocat|process)|"
    r"virtual memory exhausted|Could not allocate memory:\s*System out of memory|unable to allocate[^\r\n]{0,80}memory|"
    r"insufficient memory|STATUS_NO_MEMORY|0xC0000017|0xC000012D", re.IGNORECASE)


class NativeMemoryUnavailable(BuildError):
    """Legacy typed failure retained for reading older diagnostic contracts."""
    code = "native_memory_unavailable"
    failureStage = PHASE

    def __init__(self, policy):
        super().__init__(policy.get("admissionMessage") or "Native memory is temporarily unavailable.")
        self.resources = {"host": dict(policy["hostMemoryEvidence"]), "policy": dict(policy)}


def _metrics(host):
    return {name: host[name] for name in ("availableMemoryBytes", "commitHeadroomBytes",
        "processWorkingSetBytes", "processPrivateCommitBytes") if type(host.get(name)) is int and host[name] >= 0}


def _event(progress, phase, host, jobs, attempt, reason, detail, *, status="progress"):
    progress.event(phase, detail=detail, status=status, nativeMemory={"compilerProfile": PROFILE,
        "jobs": jobs, "attempt": attempt, "reason": reason, "resources": _metrics(host)})


def require_capacity(resources, progress, output, *, target):
    if target not in ("startup", "game"):
        return resources.phase_budget("unity", output)
    policy = resources.phase_budget("il2cpp", output)
    _event(progress, "native-compiler-profile", policy["hostMemoryEvidence"], policy["jobs"], 1,
        "capacity", "Release optimization with bounded debug metadata; " + str(policy["jobs"]) + " native workers",
        status="complete")
    return policy


def player_environment(base, *, target):
    env = dict(base)
    if target in ("startup", "game"):
        # This invocation owns compiler flags. Do not inherit unrelated host
        # flags that could restore the huge DWARF representation or change ABI.
        env["IL2CPP_ADDITIONAL_ARGS"] = EXTRA_ARGS
    return env


def memory_failure(*logs, error=None):
    """Recognize real allocation diagnostics; ordinary compiler errors still fail."""
    if error is not None and re.search(r"exited with (?:-1073741801|3221225495|-1073741523|3221225773)(?:;|\b)", str(error)):
        return True  # STATUS_NO_MEMORY / STATUS_COMMITMENT_LIMIT, not arbitrary kills.
    cause = getattr(error, "__cause__", None)
    if isinstance(cause, OSError) and (cause.errno == errno.ENOMEM or getattr(cause, "winerror", None) in (8, 14, 1455)):
        return True
    for name in logs:
        try:
            with Path(name).open("rb") as stream:
                stream.seek(0, os.SEEK_END)
                stream.seek(max(0, stream.tell() - MAX_LOG_BYTES))
                text = stream.read(MAX_LOG_BYTES).decode("utf-8", errors="replace")
        except OSError:
            continue
        if MEMORY_ERROR.search(text):
            return True
    return False


def _capacity(host):
    values = [host.get("availableMemoryBytes"), host.get("commitHeadroomBytes")]
    known = [v for v in values if type(v) is int and v >= 0]
    return min(known) if known else None


def _retain_logs(logs, attempt):
    # Rename rather than copying growing compiler logs. Keep each memory retry
    # available to support export, before command() opens its fresh launch log.
    for name in logs:
        path = Path(name)
        archived = path.with_name(path.stem + ".memory-attempt-" + str(attempt) + path.suffix)
        try:
            if path.is_file():
                path.replace(archived)
        except OSError:
            # Windows live log observers may share read/write but deny delete.
            # Keep a bounded diagnostic tail if rename is briefly unavailable;
            # optional archival must never abandon a recoverable memory retry.
            try:
                with path.open("rb") as stream:
                    stream.seek(0, os.SEEK_END)
                    stream.seek(max(0, stream.tell() - MAX_LOG_BYTES))
                    raw = stream.read(MAX_LOG_BYTES)
                archived.write_bytes(raw)
            except OSError:
                print("native memory recovery: diagnostic log archival temporarily unavailable", flush=True)
        if attempt > 8:
            try:
                path.with_name(path.stem + ".memory-attempt-" + str(attempt - 8) + path.suffix).unlink(missing_ok=True)
            except OSError:
                pass  # An optional retained diagnostic cannot block the build.


def run_player(resources, progress, output, policy, execute, argv, log, *, env, compiler_log,
               project=None, recover_delivery=None, sleep=time.sleep):
    """Retry only genuine memory failures, keeping the same incremental project.

    Reduce concurrent work first. A failed single worker uses a visible,
    cancellable backoff and an attainable live headroom threshold, then retries
    its retained outputs with Unity's background job queue also limited to one.
    All codegen/compiler flags and output paths stay unchanged across retries.
    """
    jobs, attempt = policy["jobs"], 1
    while True:
        if attempt > 1:
            # A fatal child cannot run C# Dispose. Restore only its journaled
            # parked payloads, retaining the still-open outer content transaction
            # and original backups. Re-entering preparation would redo assets.
            if project is not None:
                recover_player_exclusions(Path(project))
            if recover_delivery is not None:
                recover_delivery()
        before = resources.detect_host(output)
        current = dict(env)
        current["GHVRQ_BEE_THREADS"] = str(jobs)
        arguments = list(argv)
        if attempt > 1 and jobs == 1:
            if "-job-worker-count" in arguments:
                arguments[arguments.index("-job-worker-count") + 1] = "1"
            else:
                arguments += ["-job-worker-count", "1"]
        try:
            return execute(arguments, log, env=current)
        except BuildError as error:
            if not memory_failure(log, compiler_log, error=error):
                raise
            _retain_logs((log, compiler_log), attempt)
            _event(progress, "native-memory-retry", before, jobs, attempt, "compiler-memory-failed",
                "Native allocation failed; retaining completed compiler outputs and adapting concurrency", status="start")
            resources.emit_resource_evidence(output, {"event": "native-memory-retry", "attempt": attempt,
                "jobs": jobs, "compilerProfile": PROFILE, "host": before, "retained": True})
            latest = resources.phase_budget("il2cpp", output)
            if jobs > 1:
                jobs = max(1, min(jobs // 2, latest["jobs"]))
            else:
                # Never demand more than this host can physically provide (the
                # previous free-before-launch +25% threshold could exceed RAM).
                # Closed native actions can lower the next attempt's demand,
                # even when the initial free-memory observation is unchanged.
                total = before.get("totalMemoryBytes")
                needed = min(2 * GIB, max(512 * 1024 ** 2, total // 4)) if type(total) is int and total > 0 else 2 * GIB
                wait_seconds = min(30, 5 * 2 ** min(attempt - 1, 3))
                _event(progress, "native-memory-wait", latest.get("hostMemoryEvidence", before), 1, attempt, "pressure",
                    "Reducing background work and waiting briefly before resuming retained native outputs", status="start")
                sleep(wait_seconds)
                while True:
                    host = resources.detect_host(output)
                    now = _capacity(host)
                    if now is None or now >= needed:
                        break
                    _event(progress, "native-memory-wait", host, 1, attempt, "pressure",
                        "Waiting for additional memory; completed work is retained and resumes automatically", status="progress")
                    sleep(5)
                _event(progress, "native-memory-wait", host, 1, attempt, "retry-ready",
                    "Memory backoff complete; resuming the pending native work", status="complete")
            attempt += 1
            _event(progress, "native-memory-retry", resources.detect_host(output), jobs, attempt, "retry-ready",
                "Resuming retained native work with " + str(jobs) + " workers", status="complete")


def persist_failure(error, output, writer):
    """Retain decoding compatibility for legacy typed memory failures."""
    if not isinstance(error, NativeMemoryUnavailable):
        return
    writer(output / "last-failure.json", {"schema": 1, "stage": PHASE, "failureStage": PHASE,
        "code": error.code, "error": type(error).__name__, "message": str(error),
        "resources": error.resources, "retained": True})
