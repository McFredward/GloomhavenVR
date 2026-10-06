"""Durable scheduled-work progress; percentages describe work, never remaining time.

Each unit is an actual operation in the selected workflow. Observed counters can
advance that unit, but only a successful operation boundary closes it. Nested
file hashes/commands do not masquerade as completion of their parent operation.
The final unit is the wizard's independent output verification and publication.
"""
from __future__ import annotations
import math

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
        # Only the complete batch schedule describes recovery as a whole.
        # One finished collection/recipe/hash remains a resetting substep.
        if stage == "build" and phase == "recovery-batches": return "recovery", True
        # Bee starts a new DAG when native compilation/linking changes. Its
        # reset affects only the secondary counter, never the stage high-water.
        if phase.startswith("bee-actions:"): return None, True
        if phase in ("unity-progress", "unity-asset-import"): return None, True
    if stage == "install" and phase == "install-content": return "content", True
    return None, False


def initialize(row):
    plan = row.get("progressPlan")
    operations = PLANS[row["id"]]
    if not isinstance(plan, dict) or plan.get("version") != 1:
        plan = row["progressPlan"] = {"version": 1, "current": None, "completed": [], "fractions": {}, "percent": 0.0}
    plan["completed"] = [name for name in plan.get("completed", []) if name in operations]
    plan["fractions"] = {name: min(0.99, max(0., number)) for name, number in plan.get("fractions", {}).items()
                         if name in operations and type(number) in (int, float) and math.isfinite(number)}
    number = plan.get("percent", 0.)
    plan["percent"] = min(99.9, max(0., number)) if type(number) in (int, float) and math.isfinite(number) else 0.
    return plan


def advance(row, value, operation=None, status=None):
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
    # Ignore nested one-file hashes and incidental version commands. Only a
    # known aggregate counter or Unity's own task counter contributes a fraction.
    if current in operations and measured and (operation is None or operation == current) and value["percent"] is not None:
        fraction = min(.99, value["percent"] / 100)
        plan["fractions"][current] = max(plan["fractions"].get(current, 0.), fraction)
    if row["status"] == "complete":
        plan["completed"] = list(operations)
        plan["current"] = operations[-1]
        current = operations[-1]
    done = sum(1. if name in plan["completed"] else plan["fractions"].get(name, 0.) for name in operations)
    number = min(99.9, round(100 * done / len(operations), 1))
    plan["percent"] = max(plan["percent"], number)
    value.update(stagePercent=100. if row["status"] == "complete" else plan["percent"],
                 stageOperation=current, stageDone=round(done, 3), stageTotal=len(operations))
    return value
