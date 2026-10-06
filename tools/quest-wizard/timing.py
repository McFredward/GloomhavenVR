"""Observed active runtime and scoped estimates, independent of work percentages.

Only this process's owned run accrues monotonic time. Small accumulated totals
and completed fresh batch durations survive restart; clocks and rate samples do
not. Equal work shares, an unobserved Unity import and offline gaps never become
an estimate for the whole build.
"""
from __future__ import annotations
from collections import deque
import copy
import math
import statistics
import time

MAX_SECONDS = 366 * 24 * 3600
MIN_RATE_SECONDS = 10.0
MIN_BATCH_SECONDS = 30.0
MAX_BATCH_HISTORY = 16
FILE_PHASES = frozenset(("staging-copy-file", "recovery-asset-reference-file", "recovery-source-file-hash", "recovery-core-file-hash",
    "recovery-export-file-hash", "recovery-checkpoint-file-hash",
    "recovery-native-recipe-hash", "recovery-native-recipe-copy"))


def number(value, fallback=0.0):
    return float(value) if type(value) in (int, float) and math.isfinite(value) and 0 <= value <= MAX_SECONDS else fallback


def estimate(status="unknown", scope="phase", *, reason=None, **values):
    result = {"status": status, "scope": scope, "remainingSeconds": None,
              "lowerSeconds": None, "upperSeconds": None, "samples": 0}
    if reason: result["reason"] = reason
    result.update(values)
    return result


class Timeline:
    def __init__(self, clock=None):
        self.clock = clock or time.monotonic
        self.owners, self.paused, self.live = set(), set(), {}

    def activate(self, state):
        self.owners.add(state["session"])
        self.paused.discard(state["session"])

    def _saved(self, state, row):
        saved = row.get("timingState")
        key = row.get("progressKey")
        content = state.get("completed", {}).get("inspect", {}).get("details", {}).get("inputKey") if row["id"] == "build" else None
        if not isinstance(content, str) or len(content) > 160: content = None
        if (not isinstance(saved, dict) or saved.get("version") != 1 or saved.get("inputKey") != key
                or saved.get("contentKey") != content):
            # Historical wall timestamps include waiting/offline time. Do not
            # retroactively present them as observed active build duration.
            legacy = bool(row.get("attempts", 0) or row.get("startedAt"))
            saved = row["timingState"] = {"version": 1, "inputKey": key, "contentKey": content,
                "elapsedSeconds": 0.0, "elapsedBasis": "since-update" if legacy else "recorded-active", "batches": []}
        saved["elapsedSeconds"] = number(saved.get("elapsedSeconds"))
        saved["elapsedBasis"] = "since-update" if saved.get("elapsedBasis") == "since-update" else "recorded-active"
        batches = [item for item in saved.get("batches", [])
            if isinstance(item, dict) and type(item.get("index")) is int and item["index"] >= 0
            and type(item.get("total")) is int and item["total"] > item["index"]
            and number(item.get("seconds")) >= MIN_BATCH_SECONDS][-MAX_BATCH_HISTORY:]
        saved["batches"] = list({(item["total"], item["index"]): {"index": item["index"], "total": item["total"], "seconds": number(item["seconds"])}
                                  for item in batches}.values())
        return saved

    def _running(self, state, row):
        session = state["session"]
        return (session in self.owners and session not in self.paused and state.get("status") == "running"
                and row.get("status") == "running" and not row.get("waiting"))

    def sync(self, state):
        """Accumulate at existing publication boundaries; never write a file here."""
        now = self.clock()
        for row in state["stages"]:
            saved = self._saved(state, row)
            identity = (state["session"], row["id"])
            inputs = (saved["inputKey"], saved["contentKey"])
            live = self.live.get(identity)
            if live and live["inputs"] != inputs:
                self.live.pop(identity); live = None
            running = self._running(state, row)
            if live:
                if live["active"]:
                    saved["elapsedSeconds"] = min(MAX_SECONDS, saved["elapsedSeconds"] + max(0.0, now - live["clock"]))
                if live["active"] != running:
                    # A user wait or cancellation invalidates the current
                    # rate window, including a partly observed batch.
                    live["rates"].clear(); live["batch"] = None
                live.update(clock=now, active=running)
            elif running:
                self.live[identity] = {"inputs": inputs, "clock": now,
                    "active": True, "rates": {}, "currentRate": None, "batch": None, "nativeIndex": None}
            row["timing"] = self.row_view(state, row, now=now)
        state["timing"] = self.total_view(state)

    def close(self, state):
        self.sync(state)
        session = state["session"]
        self.owners.discard(session); self.paused.discard(session)
        self.live = {key: value for key, value in self.live.items() if key[0] != session}

    def pause(self, state):
        self.paused.add(state["session"])
        self.sync(state)

    def _elapsed(self, row, live, now):
        saved = row.get("timingState", {})
        elapsed = number(saved.get("elapsedSeconds"))
        if live and live["active"]:
            elapsed = min(MAX_SECONDS, elapsed + max(0.0, now - live["clock"]))
        return elapsed

    def observe(self, state, row, value, status=None):
        self.sync(state)
        live = self.live.get((state["session"], row["id"]))
        if not live or not live["active"]: return
        elapsed = row["timingState"]["elapsedSeconds"]
        phase, done, total = value["phase"], value.get("done"), value.get("total")
        if phase == "recovery-native-index-set":
            live["nativeIndex"] = done
        if phase.startswith("recovery-section:") and phase != "recovery-section:batches":
            live["batch"] = None
        if phase == "recovery-batch" and type(done) is int and type(total) is int and done < total:
            # A repeated boundary does not restart the already observed timer.
            if not live["batch"] or live["batch"]["index"] != done or live["batch"]["total"] != total:
                live["batch"] = {"index": done, "total": total, "started": elapsed,
                                 "fresh": False, "retained": False}
                live["rates"].clear(); live["currentRate"] = None; live["nativeIndex"] = None
        batch = live["batch"]
        if batch:
            if phase == "recovery-batch-export-verify": batch["retained"] = True
            if phase in ("recovery-asset-load", "recovery-asset-export", "recovery-batch-bundle-copy"):
                batch["fresh"] = True
            if phase == "recovery-batches" and type(done) is int and done > batch["index"]:
                duration = elapsed - batch["started"]
                if batch["fresh"] and not batch["retained"] and duration >= MIN_BATCH_SECONDS:
                    samples = row["timingState"]["batches"]
                    samples[:] = [item for item in samples if (item["index"], item["total"]) != (batch["index"], batch["total"])]
                    samples.append({"index": batch["index"], "total": batch["total"], "seconds": round(duration, 3)})
                    samples[:] = samples[-MAX_BATCH_HISTORY:]
                live["batch"] = None
        if phase not in FILE_PHASES:
            measured = (type(done) in (int, float) and type(total) in (int, float)
                        and math.isfinite(done) and math.isfinite(total) and total > 0 and 0 <= done <= total)
            if not measured:
                live["currentRate"] = None
            else:
                context = (phase, total, value.get("unit"), value.get("recoverySection"),
                           value.get("recoveryBatchIndex"), live["nativeIndex"])
                rates = live["rates"]
                if len(rates) >= 16 and context not in rates: rates.clear()
                samples = rates.setdefault(context, deque(maxlen=32))
                if samples and done < samples[-1][1]: samples.clear()
                if not samples or done > samples[-1][1]: samples.append((elapsed, done))
                live["currentRate"] = context
        row["timing"] = self.row_view(state, row)
        state["timing"] = self.total_view(state)

    def _phase_estimate(self, live, elapsed):
        key = live.get("currentRate")
        samples = live["rates"].get(key) if key else None
        if not samples:
            return estimate(reason="unmeasured-phase")
        phase, total = key[:2]
        if samples[-1][1] >= total:
            return estimate("unknown", reason="unmeasured-phase", basisPhase=phase)
        if len(samples) < 3 or samples[-1][0] - samples[0][0] < MIN_RATE_SECONDS:
            return estimate("learning", reason="insufficient-history", basisPhase=phase, samples=len(samples))
        intervals = [(b[0] - a[0]) for a, b in zip(samples, list(samples)[1:]) if b[0] > a[0]]
        stale_after = max(30.0, 3 * statistics.median(intervals)) if intervals else 30.0
        if elapsed - samples[-1][0] > stale_after:
            return estimate(reason="counter-stale", basisPhase=phase, samples=len(samples))
        rates = [(b[1] - a[1]) / (b[0] - a[0]) for a, b in zip(samples, list(samples)[1:])
                 if b[0] > a[0] and b[1] > a[1]]
        if not rates: return estimate("learning", reason="insufficient-history", basisPhase=phase)
        rate = (samples[-1][1] - samples[0][1]) / (samples[-1][0] - samples[0][0])
        remaining = (total - samples[-1][1]) / rate
        lower = (total - samples[-1][1]) / max(max(rates), rate * 1.2)
        upper = (total - samples[-1][1]) / min(min(rates), rate * .8)
        if upper > MAX_SECONDS: return estimate(reason="unmeasured-phase", basisPhase=phase)
        return estimate("estimated", remainingSeconds=round(remaining), lowerSeconds=round(lower),
                        upperSeconds=round(upper), samples=len(samples), basisPhase=phase)

    def _batch_estimate(self, row, live, elapsed):
        batch = live.get("batch")
        if not batch or not batch["fresh"] or batch["retained"]: return None
        samples = [item for item in row.get("timingState", {}).get("batches", []) if item["total"] == batch["total"]]
        if len(samples) < 2: return None
        durations = [item["seconds"] for item in samples]
        middle, lower, upper = statistics.median(durations), min(durations) * .8, max(durations) * 1.4
        current = max(0.0, elapsed - batch["started"])
        future = batch["total"] - batch["index"] - 1
        if current > upper * 2:
            return estimate(scope="conversion-batches", reason="counter-stale", samples=len(samples),
                            batchIndex=batch["index"] + 1, batchTotal=batch["total"])
        # A completed batch is a time observation, not a claim that the next
        # similarly bounded bundle has identical cost. Expose the actual range.
        remaining = future * middle + max(middle * .15, middle - current)
        low = future * lower + max(0.0, lower - current)
        high = future * upper + max(upper * .15, upper - current)
        if high > MAX_SECONDS: return None
        return estimate("estimated", "conversion-batches", remainingSeconds=round(remaining),
            lowerSeconds=round(low), upperSeconds=round(high), samples=len(samples),
            batchIndex=batch["index"] + 1, batchTotal=batch["total"])

    def row_view(self, state, row, *, now=None):
        now = self.clock() if now is None else now
        live = self.live.get((state["session"], row["id"]))
        elapsed = self._elapsed(row, live, now)
        active = bool(live and live["active"] and self._running(state, row))
        if row.get("status") == "complete": eta = estimate("complete")
        elif (state["session"] in self.paused or row.get("waiting")
              or row.get("status") in ("cancelled", "blocked", "failed", "interrupted")):
            eta = estimate("paused")
        elif active:
            eta = self._batch_estimate(row, live, elapsed) or self._phase_estimate(live, elapsed)
        else: eta = estimate(reason="unmeasured-phase")
        basis = row.get("timingState", {}).get("elapsedBasis")
        if basis not in ("recorded-active", "since-update"):
            basis = "since-update" if row.get("attempts", 0) or row.get("startedAt") else "recorded-active"
        return {"elapsedSeconds": round(elapsed, 3), "active": active,
                "elapsedBasis": basis, "estimate": eta}

    @staticmethod
    def total_view(state):
        rows = [row.get("timing", {}) for row in state["stages"]]
        return {"elapsedSeconds": round(sum(number(row.get("elapsedSeconds")) for row in rows), 3),
                "active": any(row.get("active") for row in rows),
                "elapsedBasis": "since-update" if any(row.get("elapsedBasis") == "since-update" for row in rows) else "recorded-active"}

    def snapshot(self, state, source=None):
        """A status poll projects the live clock without touching persisted state."""
        value = copy.deepcopy(state)
        source_rows = {row["id"]: row for row in (source or state)["stages"]}
        now = self.clock()
        for row in value["stages"]:
            row["timing"] = self.row_view(source or state, source_rows[row["id"]], now=now)
        value["timing"] = self.total_view(value)
        return value
