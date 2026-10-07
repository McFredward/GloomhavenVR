"""Opt-in measured builder counters; no elapsed-time or guessed percentages."""
from __future__ import annotations
from contextlib import contextmanager
import json
import math
import os
import re
import sys
import time

ENV = "GHVRQ_WIZARD_PROGRESS"
PREFIX = "GHVRQ_PROGRESS "
_last_event_at = 0.


def enabled(): return os.environ.get(ENV) == "1"


def event(phase, done=None, total=None, unit=None, detail=None, *, status="progress", stream=None, operation=None):
    if not isinstance(phase, str) or not phase or len(phase) > 160:
        raise ValueError("Progress phase must be a bounded non-empty string.")
    for count in (done, total):
        if count is not None and (type(count) not in (int, float) or count < 0 or count > 9007199254740991 or not math.isfinite(count)):
            raise ValueError("Progress counts must be finite, non-negative safe numbers.")
    if done is not None and total is not None and done > total: raise ValueError("Progress exceeds its observed total.")
    if unit is not None and (not isinstance(unit, str) or len(unit) > 64): raise ValueError("Invalid progress unit.")
    if detail is not None and not isinstance(detail, str): raise ValueError("Invalid progress detail.")
    if status not in ("start", "progress", "complete", "reuse", "failed"): raise ValueError("Invalid progress event.")
    detail = None if detail is None else re.sub(r"[\x00-\x08\x0b-\x1f]", "", detail)[:1024]
    value = {"schema": 1, "phase": phase, "done": done, "total": total, "unit": unit, "detail": detail, "status": status}
    if operation is not None:
        if not isinstance(operation, str) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", operation):
            raise ValueError("Invalid planned operation.")
        value["operation"] = operation
    if enabled():
        global _last_event_at
        print(PREFIX + json.dumps(value, ensure_ascii=False, separators=(",", ":")), file=stream or sys.stdout, flush=True)
        _last_event_at = time.monotonic()
    return value


@contextmanager
def silence_diagnostics(*, quiet_seconds=120., limit=3, stream=None):
    """Record bounded stacks during quiet recovery work; never impose a timeout.

    Native exporter/file calls can block between measured events. A later
    support export needs the actual stack, not an inferred failed asset name.
    This observer is private to the opt-in wizard process and stops on exit.
    """
    if not enabled():
        yield
        return
    if type(quiet_seconds) not in (int, float) or not math.isfinite(quiet_seconds) or quiet_seconds <= 0 or type(limit) is not int or not 1 <= limit <= 10:
        raise ValueError("Invalid quiet recovery diagnostic bounds.")
    import faulthandler
    import threading
    stopped = threading.Event()
    started = time.monotonic()
    output = stream or sys.stderr

    def observe():
        reports, next_report = 0, started
        while not stopped.wait(min(5., max(.01, quiet_seconds / 4))):
            now = time.monotonic()
            quiet = now - max(started, _last_event_at)
            if quiet < quiet_seconds or now < next_report:
                continue
            try:
                print("GHVRQ_DIAGNOSTIC " + json.dumps({"schema": 1, "kind": "recovery-silence",
                      "quietSeconds": round(quiet, 1), "report": reports + 1,
                      "timeout": False}), file=output, flush=True)
                faulthandler.dump_traceback(file=output, all_threads=True)
            except (OSError, ValueError, RuntimeError):
                return  # An optional diagnostic cannot fail conversion.
            reports += 1
            if reports >= limit:
                return
            next_report = now + quiet_seconds

    watcher = threading.Thread(target=observe, name="quest-recovery-diagnostics", daemon=True)
    try:
        watcher.start()
    except (OSError, RuntimeError):
        yield  # Observation must remain optional when a host cannot create threads.
        return
    try:
        yield
    finally:
        stopped.set()
        watcher.join(timeout=1.)


def operation(name, *, complete=False, detail=None):
    """Publish an actual schedule boundary; nested counters retain their own units."""
    return event("operation:" + name, 1 if complete else None, 1 if complete else None,
                 "operations", detail, status="complete" if complete else "start", operation=name)


class Counter:
    """Count real bytes/files. Publish the last count only after verification succeeds."""
    def __init__(self, phase, total=None, unit="files", detail=None, *, stream=None, interval=.5):
        self.phase, self.total, self.unit, self.detail = phase, total, unit, detail
        self.stream, self.interval, self.done, self.last = stream, interval, 0, time.monotonic()
        # Empty operations become complete only when their caller accepts them.
        if total != 0: event(phase, 0 if total is not None else None, total, unit, detail, status="start", stream=stream)

    def update(self, done, detail=None, *, force=False):
        if type(done) is not int or done > 9007199254740991 or done < self.done or self.total is not None and done > self.total:
            raise ValueError("Progress counters must advance within their observed total.")
        self.done = done
        if detail is not None: self.detail = detail
        now = time.monotonic()
        if self.done != self.total and (force or now - self.last >= self.interval):
            event(self.phase, done, self.total, self.unit, self.detail, stream=self.stream); self.last = now

    def add(self, amount, detail=None):
        if type(amount) is not int or amount < 0: raise ValueError("Progress increments must be non-negative integers.")
        self.update(self.done + amount, detail)

    def finish(self, *, reused=False):
        if self.total is not None and self.done != self.total: raise ValueError("Unfinished progress cannot be completed.")
        return event(self.phase, self.done if self.total is not None else None, self.total, self.unit, self.detail,
                     status="reuse" if reused else "complete", stream=self.stream)

    def fail(self, error):
        return event(self.phase, None, None, self.unit, "Failed: " + type(error).__name__, status="failed", stream=self.stream)
