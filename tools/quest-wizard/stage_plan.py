"""Durable scheduled-work progress; percentages describe work, never remaining time.

Each unit is an actual operation in the selected workflow. Observed counters can
advance that unit, but only a successful operation boundary closes it. Nested
file hashes/commands do not masquerade as completion of their parent operation.
The final unit is the wizard's independent output verification and publication.
"""
from __future__ import annotations
import math

# Fractions are shares of the actual workflow, not estimates of elapsed time.
# Repeated counters (one batch, index or large file) occupy only their owner's
# bounded share. Explicit recovery-section boundaries distinguish the same
# source/checkpoint counter when it runs before and after bundle conversion.
RECOVERY_SECTIONS = ("source", "core", "batches", "references", "source-recheck", "checkpoint", "cab-index")
CORE_STEPS = ("input-hash", "input-copy", "asset-load", "asset-export", "shader-recipes", "core-hash", "core-verify", "core-copy", "checkpoint")
BATCH_STEPS = ("retained-export", "bundle-copy", "asset-load", "asset-export", "shader-recipes", "export-receipt", "checkpoint-verify", "collections", "native", "checkpoint", "commit-verify")
NATIVE_STEPS = ("read", "recipes", "write")
RECOVERY_PHASES = {
    "recovery-stage-input-hash": "input-hash", "recovery-stage-input-copy": "input-copy",
    "recovery-asset-load": "asset-load", "recovery-asset-export": "asset-export",
    "recovery-shader-recipes": "shader-recipes", "recovery-core-receipt-verify": "core-hash",
    "recovery-core-output-hash": "core-hash", "recovery-core-copy-hash": "core-copy",
    "recovery-resume-verify": "core-copy", "recovery-rollback-verify": "core-copy", "recovery-rollback-index-verify": "core-copy",
    "recovery-batch-bundle-copy": "bundle-copy", "recovery-export-receipt-hash": "export-receipt",
    "recovery-batch-export-verify": "retained-export",
    "recovery-checkpoint-verify": "checkpoint-verify", "recovery-original-collection-merge": "collections",
    "recovery-checkpoint-write": "checkpoint",
}
FILE_CHILDREN = {
    "recovery-source-file-hash": ("recovery-source-hash",),
    "recovery-core-file-hash": ("recovery-core-receipt-verify", "recovery-core-output-hash"),
    "recovery-export-file-hash": ("recovery-export-receipt-hash",),
    "recovery-checkpoint-file-hash": ("recovery-batch-export-verify", "recovery-checkpoint-verify", "recovery-resume-verify", "recovery-rollback-verify", "recovery-rollback-index-verify", "recovery-core-copy-hash", "recovery-original-collection-merge"),
    "recovery-native-recipe-hash": ("recovery-native-recipe-merge",),
    "recovery-native-recipe-copy": ("recovery-native-recipe-merge",),
}

PLANS = {
    "tools": ("qualify", "download-git", "extract-git", "verify-git", "download-dotnet8", "extract-dotnet8", "verify-dotnet8", "download-dotnet10", "extract-dotnet10", "verify-dotnet10", "output-verify"),
    "source": ("source-verify", "source-copy", "xr-sources", "xr-build", "source-inventory", "output-verify"),
    "unity": ("hub", "editor", "prerequisites", "output-verify"),
    "profile": ("logo", "identity", "output-verify"),
    "inspect": ("game-inputs", "mod-inputs", "profile-inputs", "manifest", "output-verify"),
    "build": ("game-inputs", "mod-inputs", "profile-inputs", "source-snapshot", "game-snapshot", "snapshot-check", "recovery", "project-files", "native-runtime", "audio", "textures", "graphics", "mod-banks", "weave", "package-api", "unity-import", "unity-validation", "content-bank", "player", "delivery", "output-verify"),
    "install": ("connect", "apk", "content", "launch", "output-verify"),
}


def phase_operation(stage, phase, value):
    """Recognize only counters with a known parent; arbitrary nested work stays local."""
    if phase == "output-verify": return "output-verify", True
    if phase == "receipt-verify": return None, False
    if stage == "tools":
        for prefix, operation in (("tool-download-", "download-"), ("tool-extract-", "extract-")):
            if phase.startswith(prefix): return operation + phase[len(prefix):], True
    if stage == "source" and phase == "source-copy": return "source-copy", True
    if stage == "profile" and phase == "steam-logo-download": return "logo", True
    if stage == "unity":
        if phase == "unity-hub-download": return "hub", False
        if phase.startswith("unity-hub-") and phase != "unity-hub-help": return "hub", True
        if phase.startswith("unity-editor-"): return "editor", False
        # The setup engine opens this operation only after witnessing actual
        # Editor/Android module files; an incidental check counter cannot skip
        # pending Hub/login/installation work by itself.
        if phase == "unity-prerequisites": return None, True
    if stage in ("inspect", "build"):
        mapping = {"game-hash": "game-inputs", "source-hash": "mod-inputs", "source-snapshot": "source-snapshot",
                   "source-snapshot-verify": "source-snapshot", "game-snapshot": "game-snapshot",
                   "game-snapshot-verify": "game-snapshot", "game-snapshot-source-verify": "snapshot-check"}
        if phase in mapping: return mapping[phase], True
        if stage == "build" and (phase == "recovery-batches" or phase in {"recovery-section:" + name for name in RECOVERY_SECTIONS}):
            return "recovery", False
        # Bee starts a new DAG when native compilation/linking changes. Its
        # reset affects only the secondary counter, never the stage high-water.
        if phase.startswith("bee-actions:"): return None, True
        if phase in ("unity-progress", "unity-asset-import"): return None, True
    if stage == "install" and phase == "install-content": return "content", True
    return None, False


def initialize(row):
    plan = row.get("progressPlan")
    operations = PLANS[row["id"]]
    if not isinstance(plan, dict) or plan.get("version") not in (1, 2):
        plan = row["progressPlan"] = {"version": 2, "current": None, "completed": [], "fractions": {}, "percent": 0.0}
    # A replaced Builder preserves its saved operation/high-water evidence.
    # Older plans lack child scopes, so those are learned from the live producer
    # without interpreting a previously reset substep 100% as global completion.
    plan["version"] = 2
    plan["completed"] = [name for name in plan.get("completed", []) if name in operations]
    plan["fractions"] = {name: min(0.99, max(0., number)) for name, number in plan.get("fractions", {}).items()
                         if name in operations and type(number) in (int, float) and math.isfinite(number)}
    number = plan.get("percent", 0.)
    plan["percent"] = min(99.9, max(0., number)) if type(number) in (int, float) and math.isfinite(number) else 0.
    return plan


def _ratio(value):
    done, total = value.get("done"), value.get("total")
    if done is None or total is None: return None
    return 1. if total == 0 else min(1., done / total)


def _work():
    return {"current": None, "fractions": {}, "completed": []}


def _advance_work(work, steps, step, ratio=None):
    """Only a later actual boundary can close previous scheduled work."""
    if step not in steps: return
    current = work.get("current")
    if current in steps and steps.index(step) < steps.index(current): return
    work["completed"] = list(dict.fromkeys([*work.get("completed", []), *steps[:steps.index(step)]]))
    work["current"] = step
    if ratio is not None:
        old = work.setdefault("fractions", {}).get(step, 0.)
        work["fractions"][step] = max(old, min(.99, ratio))


def _work_fraction(work, steps):
    completed, fractions = work.get("completed", []), work.get("fractions", {})
    return sum(1. if name in completed else fractions.get(name, 0.) for name in steps) / len(steps)


def _counter_fraction(work, value):
    """A large-file byte counter supplements at most one observed parent item."""
    phase, ratio = value["phase"], _ratio(value)
    parents = FILE_CHILDREN.get(phase)
    if parents:
        if ratio is None: return None, None
        parent = work.get("counter")
        if not isinstance(parent, dict) or parent.get("phase") not in parents or not parent.get("total"):
            return None, None
        # The parent counter may be throttled: use only witnessed completed
        # files, not an assumed number of unreported iterations.
        return parent["phase"], min(1., (parent["done"] + min(.99, ratio)) / parent["total"])
    if ratio is not None:
        work["counter"] = {"phase": phase, "done": value["done"], "total": value["total"]}
    return phase, ratio


def _native_fraction(batch, value):
    phase = value["phase"]
    # The same index reader is also used while preparing the merge journal,
    # before collections have been copied. Only the actual merger's explicit
    # index-set scope owns these counters in the scheduled native span.
    if phase != "recovery-native-index-set" and "native" not in batch: return None
    native = batch.setdefault("native", {"index": 0, "total": 2, "work": _work()})
    if phase == "recovery-native-index-set":
        index, total = value.get("done"), value.get("total")
        if type(index) is not int or total != 2 or not 0 <= index <= total: return None
        if index < native["index"]: return None
        if index != native["index"]: native.update(index=index, work=_work())
    else:
        phase, ratio = _counter_fraction(native["work"], value)
        step = {"recovery-native-index-read": "read", "recovery-native-recipe-merge": "recipes",
                "recovery-native-index-write": "write"}.get(phase)
        if step is None: return None
        _advance_work(native["work"], NATIVE_STEPS, step, ratio)
    return min(.99, (native["index"] + _work_fraction(native["work"], NATIVE_STEPS)) / native["total"])


def _recovery_fraction(plan, value):
    """Measured child work contributes continuously inside its bounded owner."""
    recovery = plan.setdefault("recovery", {"work": _work(), "sections": {}, "batch": {}})
    phase = value["phase"]
    if phase.startswith("recovery-section:"):
        section = phase.split(":", 1)[1]
        _advance_work(recovery["work"], RECOVERY_SECTIONS, section)
    section = recovery["work"].get("current")
    # Compatibility for pre-scope producers: an aggregate schedule gives a
    # real batch denominator, but a lone collection cannot invent one.
    if phase == "recovery-batches":
        _advance_work(recovery["work"], RECOVERY_SECTIONS, "batches")
        section = "batches"
    if section not in RECOVERY_SECTIONS: return None
    work = recovery["sections"].setdefault(section, _work())
    ratio = None
    if section == "batches":
        batch = recovery["batch"]
        if phase in ("recovery-batches", "recovery-batch"):
            done, total = value.get("done"), value.get("total")
            if type(done) is int and type(total) is int and 0 <= done <= total:
                if batch.get("done") != done or batch.get("total") != total:
                    batch.update(done=done, total=total, work=_work())
                ratio = 1. if total == 0 else done / total
        elif batch.get("total") and batch.get("done", 0) < batch["total"]:
            child = batch["work"]
            if phase.startswith("recovery-native-"):
                nested = _native_fraction(child, value)
                if nested is not None: _advance_work(child, BATCH_STEPS, "native", nested)
            else:
                effective, measured = _counter_fraction(child, value)
                step = RECOVERY_PHASES.get(effective)
                # Verification after the write is the independent commit gate;
                # its earlier occurrence qualifies the retained previous tree.
                if step == "checkpoint-verify" and child.get("current") in ("checkpoint", "commit-verify"):
                    step = "commit-verify"
                _advance_work(child, BATCH_STEPS, step, measured)
            ratio = (batch["done"] + _work_fraction(child, BATCH_STEPS)) / batch["total"]
    elif section == "core":
        effective, measured = _counter_fraction(work, value)
        step = "core-verify" if effective == "recovery-checkpoint-verify" else RECOVERY_PHASES.get(effective)
        _advance_work(work, CORE_STEPS, step, measured)
        ratio = _work_fraction(work, CORE_STEPS)
    else:
        effective, measured = _counter_fraction(work, value)
        expected = {"source": "recovery-source-hash", "references": "recovery-asset-references",
                    "source-recheck": "recovery-source-hash", "checkpoint": "recovery-checkpoint-write",
                    "cab-index": "recovery-cab-bundle-index"}[section]
        if effective == expected: ratio = measured
    _advance_work(recovery["work"], RECOVERY_SECTIONS, section, ratio)
    return _work_fraction(recovery["work"], RECOVERY_SECTIONS)


def advance(row, value, operation=None, status=None):
    for key in ("recoverySection", "recoveryBatchIndex", "recoveryBatchTotal"):
        value.pop(key, None)
    plan = initialize(row); operations = PLANS[row["id"]]
    inferred, measured = phase_operation(row["id"], value["phase"], value)
    if row["id"] == "unity" and value["phase"] == "unity-prerequisites" and plan.get("current") != "prerequisites":
        measured = False
    operation = operation or inferred
    if operation in operations:
        index = operations.index(operation)
        current = plan.get("current")
        if current not in operations or index >= operations.index(current):
            # Reaching the next scheduled boundary proves previous operations
            # returned successfully or were explicitly verified/reused/skipped.
            plan["completed"] = list(dict.fromkeys([*plan["completed"], *operations[:index]]))
            plan["current"] = operation
            if status in ("complete", "reuse"):
                plan["completed"] = list(dict.fromkeys([*plan["completed"], operation]))
    current = plan.get("current")
    if row["id"] == "build" and current == "recovery":
        nested = _recovery_fraction(plan, value)
        if nested is not None:
            plan["fractions"][current] = max(plan["fractions"].get(current, 0.), min(.99, nested))
        recovery = plan.get("recovery", {})
        section = recovery.get("work", {}).get("current")
        if section in RECOVERY_SECTIONS:
            value["recoverySection"] = section
        batch = recovery.get("batch", {})
        done, total = batch.get("done"), batch.get("total")
        if (section == "batches" and value["phase"] != "recovery-batches"
                and type(done) is int and type(total) is int and 0 <= done < total):
            value.update(recoveryBatchIndex=done + 1, recoveryBatchTotal=total)
    # Ignore nested one-file hashes and incidental version commands. Only a
    # known aggregate counter or Unity's own task counter contributes a fraction.
    if current in operations and measured and (operation is None or operation == current) and value["percent"] is not None:
        fraction = min(.99, _ratio(value))
        plan["fractions"][current] = max(plan["fractions"].get(current, 0.), fraction)
    if row["status"] == "complete":
        plan["completed"] = list(operations)
        plan["current"] = operations[-1]
        current = operations[-1]
    done = sum(1. if name in plan["completed"] else plan["fractions"].get(name, 0.) for name in operations)
    # Keep small measured byte/file contributions for the bar, even when its
    # concise percentage label rounds them to two decimal places.
    number = min(99.9, round(100 * done / len(operations), 6))
    plan["percent"] = max(plan["percent"], number)
    value.update(stagePercent=100. if row["status"] == "complete" else plan["percent"],
                 stageOperation=current, stageDone=round(done, 3), stageTotal=len(operations))
    return value
