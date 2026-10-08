"""Bounded independent codecs with deterministic parent-only publication.

No UnityPy object, native reader or project writer crosses a worker boundary.
Threads supervise external managed codecs; they never rely on Windows fork or
Python multiprocessing import state. Only semantically accepted closed outputs
gain reusable cache receipts. Cancellation joins and reaps all supervised tools.
"""
from collections import deque
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
import json
import os
from pathlib import Path
import signal
import sqlite3
import stat
import subprocess
import tempfile
import threading

from storage import BuildError, ValidatedFileWitnesses, _ordinary_owned, value_hash, write_json

GIB = 1024 ** 3


class Cancelled(BuildError):
    pass


class Cancellation:
    def __init__(self):
        self.event = threading.Event()
        self.lock = threading.Lock()
        self.failure = None

    def check(self):
        if self.event.is_set(): raise Cancelled("Independent asset work was cancelled.")

    def fail(self, error):
        with self.lock:
            if self.failure is None and not isinstance(error, Cancelled): self.failure = error
            self.event.set()


def _terminate(process):
    if process.poll() is not None: return
    if os.name == "nt":
        # taskkill is provided by Windows. /T includes children of the managed
        # codec; literal argv prevents shell expansion of any player path.
        # https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/taskkill
        taskkill = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/taskkill.exe"
        try:
            subprocess.run([str(taskkill), "/PID", str(process.pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
        except (OSError, subprocess.TimeoutExpired): pass
    else:
        try: os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError: pass
    if process.poll() is None: process.kill()
    process.wait()


def run_command(command, arguments, cancellation):
    """Supervise one argv-only process; diagnostics are disk-backed and bounded."""
    cancellation.check()
    with tempfile.TemporaryFile() as log:
        settings = {"start_new_session": True} if os.name != "nt" else {
            "creationflags": subprocess.CREATE_NEW_PROCESS_GROUP}
        process = subprocess.Popen([*map(str, command), *map(str, arguments)],
            stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, **settings)
        try:
            while True:
                cancellation.check()
                try: code = process.wait(timeout=0.1); break
                except subprocess.TimeoutExpired: pass
            log.seek(0, os.SEEK_END); size = log.tell(); log.seek(max(0, size - 2000))
            text = log.read(2000).decode("utf-8", errors="replace")
            if code: raise BuildError("Pinned native codec failed: " + text)
            return text
        finally:
            if process.poll() is None: _terminate(process)


def ordered_pipeline(items, prepare, execute, publish, *, jobs, byte_budget=None, cancellation=None):
    """Prepare lazily in the parent, run bounded work, publish original order.

    A prepared package exposes memory_bytes. Reserve it until publication, so
    completed out-of-order results cannot form an unbounded memory queue.
    Failure anywhere cancels other tools before the caller's rollback begins.
    """
    if not isinstance(jobs, int) or isinstance(jobs, bool) or jobs < 1 or jobs > 1024:
        raise BuildError("Independent asset workers require a bounded positive job count.")
    cancellation = cancellation or Cancellation()
    pending, reserved, held = deque(), 0, None
    iterator = iter(items); exhausted = False
    pool = ThreadPoolExecutor(max_workers=jobs, thread_name_prefix="quest-asset-codec")
    def work(package):
        try: cancellation.check(); return execute(package, cancellation)
        except BaseException as error: cancellation.fail(error); raise
    try:
        while pending or held is not None or not exhausted:
            cancellation.check()
            # Accept a ready prefix before loading another native container.
            if pending and (pending[0][1].done() or len(pending) >= jobs or exhausted or held is not None):
                package, future = pending.popleft()
                result = future.result()
                cancellation.check(); publish(package, result)
                reserved -= package.memory_bytes
                continue
            if held is None and not exhausted:
                try: item = next(iterator)
                except StopIteration: exhausted = True; continue
                held = prepare(item)
                if not isinstance(held.memory_bytes,int) or isinstance(held.memory_bytes,bool) or held.memory_bytes < 0:
                    raise BuildError("Independent asset work has an invalid memory estimate.")
                if byte_budget is not None and held.memory_bytes > byte_budget:
                    raise BuildError("An independent codec job exceeds the detected host memory budget.")
            if held is not None:
                if byte_budget is not None and reserved + held.memory_bytes > byte_budget:
                    continue
                pending.append((held, pool.submit(work, held))); reserved += held.memory_bytes; held = None
    except BaseException as error:
        cancellation.fail(error)
        if cancellation.failure is not None and isinstance(error, Cancelled): raise cancellation.failure
        raise
    finally:
        if pending: cancellation.event.set()
        for _, future in pending: future.cancel()
        pool.shutdown(wait=True, cancel_futures=True)


def codec_fingerprint(command):
    """Use the pinned build receipt already byte-qualified by portable_decoder."""
    if len(command) < 2: return None
    receipt = Path(command[1]).parent / "codec-build.json"
    if not receipt.is_file(): return None
    value = json.loads(receipt.read_text())
    if not value.get("sourceFingerprint") or not value.get("files"): return None
    return value_hash({"source": value["sourceFingerprint"], "files": value["files"]})


@dataclass
class CodecWork:
    path: Path
    arguments: tuple
    owner: dict
    memory_bytes: int
    cached: bool = False


class CodecCache:
    """Parent-owned receipts for exact independent decoder input/parameters."""
    def __init__(self, tool_cache, fingerprint):
        self.root = _ordinary_owned(Path(tool_cache) / "independent-codecs-v1")
        self.root.mkdir(parents=True, exist_ok=True)
        self.fingerprint = fingerprint
        database = _ordinary_owned(self.root / ".witnesses.sqlite3")
        self._regular(database)
        self.db = sqlite3.connect(database)
        self.witnesses = ValidatedFileWitnesses(self.db, self.root, {"schema": 1, "codec": fingerprint})
        self.stats = {"decoded": 0, "reused": 0}

    def __enter__(self): return self

    def __exit__(self, *args):
        try: self.db.commit()
        finally: self.db.close()

    @staticmethod
    def _regular(path):
        if path.exists():
            value = path.lstat()
            if not stat.S_ISREG(value.st_mode) or value.st_nlink != 1:
                raise BuildError("Independent codec cache member is not a regular owned file.")

    def prepare(self, identity, raw, parameters, expected_bytes):
        import hashlib
        raw_sha = hashlib.sha256(raw).hexdigest()
        owner = {"schema": 1, "owner": "Quest independent codec", "identity": identity,
                 "codec": self.fingerprint, "rawSha256": raw_sha, "rawBytes": len(raw),
                 "parameters": list(parameters), "outputBytes": expected_bytes}
        directory = _ordinary_owned(self.root / value_hash(owner)); directory.mkdir(exist_ok=True)
        marker = _ordinary_owned(directory / "owner.json")
        self._regular(marker)
        if marker.exists():
            if json.loads(marker.read_text()) != owner: raise BuildError("Independent codec cache has a different owner.")
        else: write_json(marker, owner)
        source, target = _ordinary_owned(directory / "input.bin"), _ordinary_owned(directory / "output.bin")
        self._regular(source); self._regular(target)
        if not source.is_file() or not self.witnesses.qualify(source, raw_sha, len(raw)):
            self.witnesses.invalidate(source)
            if source.write_bytes(raw) != len(raw): raise BuildError("Independent codec input write was incomplete.")
            self.witnesses.remember(source, raw_sha)
        receipt = _ordinary_owned(directory / "accepted.json")
        self._regular(receipt)
        cached = False
        if self.fingerprint is not None and receipt.is_file():
            try:
                prior = json.loads(receipt.read_text())
                cached = (prior.get("owner") == owner and prior.get("size") == expected_bytes and
                          target.is_file() and self.witnesses.qualify(target, prior["sha256"], expected_bytes))
            except (OSError, ValueError, KeyError): cached = False
        arguments = (parameters[0], source, target, *parameters[1:])
        # Includes managed runtime, decoded allocation and parent-retained
        # metadata. Admission separately bounds both count and payload reserve.
        memory = max(GIB, 128 * 1048576 + 4 * len(raw) + 4 * expected_bytes)
        return CodecWork(target, arguments, owner, memory, cached)

    def execute(self, package, command, cancellation):
        if not package.cached:
            package.path.unlink(missing_ok=True)
            run_command(command, package.arguments, cancellation)
        return package.path

    def accept(self, package, sha256, stamp):
        """Called only after the parent proves dimensions/format/pixel semantics."""
        self.witnesses.remember(package.path, sha256, stamp=stamp)
        write_json(package.path.parent / "accepted.json", {"owner": package.owner,
                   "sha256": sha256, "size": package.owner["outputBytes"]})
        self.db.commit()
        self.stats["reused" if package.cached else "decoded"] += 1
