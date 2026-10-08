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
RECOVERY_SECTIONS = ("source", "core", "batches", "references", "source-recheck", "checkpoint", "cab-index", "staging")
STAGING_STEPS = ("catalog", "canonical", "copy", "runtime", "guid", "layout", "native", "catalog-final", "index", "tmp", "bindings", "audit", "scenes", "report")
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
    "recovery-asset-reference-file": ("recovery-asset-references",),
    "staging-copy-file": ("staging-copy",),
    "staging-runtime-file": ("staging-runtime-copy",),
    "staging-report-file": ("staging-report-files",),
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
    "build": ("game-inputs", "mod-inputs", "profile-inputs", "source-snapshot", "game-snapshot", "snapshot-check", "recovery", "project-files", "startup-content", "native-runtime", "audio", "textures", "graphics", "mod-banks", "preparation-contracts", "weave", "package-api", "unity-import", "unity-validation", "content-bank", "player", "delivery", "output-verify"),
    "install": ("connect", "apk", "content", "launch", "output-verify"),
}

# Reserved work spans, not time estimates. The October 6 Windows witness spent
# minutes exporting each of sixteen data packages, while seven short input
# operations had already consumed 28.6% of the equally weighted old bar. Give
# conversion/import their own useful spans and count each scheduled package
# inside conversion. Only actual counters and successful boundaries advance it.
BUILD_SHARES = dict(zip(PLANS["build"], (1, 1, 1, 1, 2, 1, 40, 1, 1, 2, 2, 5, 2, 2, 1, 2, 1, 18, 1, 4, 8, 2, 1)))
BUILD_GROUPS = {"inputs": PLANS["build"][:6], "recovery": ("recovery",),
                "project": PLANS["build"][7:15], "code": ("weave", "package-api"),
                "import": ("unity-import", "unity-validation"),
                "export": ("content-bank", "player", "delivery", "output-verify")}
UPDATE_PLANS = {
    "update-mod": ("update-owned-game", "update-mod-source", "update-code", "update-art", "update-repack", "update-verify"),
    "update-profile": ("update-owned-game", "update-profile", "update-repack", "update-verify"),
}
UPDATE_SHARES = {"update-mod": dict(zip(UPDATE_PLANS["update-mod"], (5, 5, 40, 15, 25, 10))),
                 "update-profile": dict(zip(UPDATE_PLANS["update-profile"], (20, 20, 40, 20)))}
UPDATE_GROUPS = {"update-mod": {"inputs": UPDATE_PLANS["update-mod"][:2], "code": UPDATE_PLANS["update-mod"][2:4], "export": UPDATE_PLANS["update-mod"][4:]},
                 "update-profile": {"inputs": UPDATE_PLANS["update-profile"][:2], "export": UPDATE_PLANS["update-profile"][2:]}}
WORK_REVISION = 6
PREPARATION_ITEMS = {"bundled-audio": "audio", "native-cubemaps": "textures",
                     "ordinary-texture-audit": "textures", "native-texture2d": "textures",
                     "native-sprites": "startup-content", "environment-bundles": "mod-banks",
                     "environment-meshes": "mod-banks"}
# These actual producer passes prepare geometry for one resource-bank
# checkpoint. Geometry is only half its work: Unity still has to compile the
# bank and return a qualified result. Its final mesh cannot close the scope.
ENVIRONMENT_ITEM_SHARES = {"environment-bundles": (0., .25), "environment-meshes": (.25, .25)}


def _item_checkpoint(name):
    return "mod-resource-banks" if name in ENVIRONMENT_ITEM_SHARES else name


def workflow(row):
    return row.get("workMode") if row.get("workMode") in UPDATE_PLANS else "build"


def planned_operations(row):
    return UPDATE_PLANS.get(workflow(row), PLANS["build"]) if row["id"] == "build" else PLANS[row["id"]]


def build_shares(plan):
    return UPDATE_SHARES.get(plan.get("workflow"), BUILD_SHARES)


def update_alias(row, name):
    """Map only known reused helper counters to their current update owner."""
    if row["id"] != "build" or workflow(row) != "update-mod": return None
    aliases = {"source-snapshot": "update-mod-source", "game-snapshot": "update-mod-source", "snapshot-check": "update-mod-source",
               "weave": "update-code", "package-api": "update-code", "unity-import": "update-code",
               "unity-validation": "update-code", "unity-build": "update-code",
               "mod-banks": "update-art", "mod-resource-banks": "update-art", "delivery": "update-repack"}
    parent = aliases.get(name)
    if parent == "update-code" and row.get("progressPlan", {}).get("liveOperation") == "update-art": return "update-art"
    return parent


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
        if stage == "build" and phase == "prepare-project-copy": return "project-files", True
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
    operations = planned_operations(row)
    mode = workflow(row)
    if not isinstance(plan, dict) or plan.get("version") not in (1, 2) or plan.get("workflow", "build") != mode:
        plan = row["progressPlan"] = {"version": 2, "workRevision": WORK_REVISION, "workflow": mode, "current": None, "completed": [], "fractions": {}, "percent": 0.0}
    plan["workflow"] = mode
    # A replaced Builder preserves its saved operation/high-water evidence.
    # Older plans lack child scopes, so those are learned from the live producer
    # without interpreting a previously reset substep 100% as global completion.
    plan["version"] = 2
    plan["completed"] = [name for name in plan.get("completed", []) if name in operations]
    plan["fractions"] = {name: min(.999999 if name == "recovery" and row["id"] == "build" else .99, max(0., number)) for name, number in plan.get("fractions", {}).items()
                         if name in operations and type(number) in (int, float) and math.isfinite(number)}
    number = plan.get("percent", 0.)
    plan["percent"] = min(99.9, max(0., number)) if type(number) in (int, float) and math.isfinite(number) else 0.
    if plan.get("workRevision") != WORK_REVISION:
        # A new work distribution must not move an existing owner's bar back,
        # or freeze it until the new curve catches up. Anchor the remaining
        # measured work to the previous high-water once, then keep that mapping.
        plan["workRevision"] = WORK_REVISION
        if row["id"] == "build":
            # Native pointer preparation also audits references. Older models
            # assigned that counter to the later final audit and inferred that
            # every intervening transform was complete. Retain explicit phase
            # receipts only; the global bar keeps its existing high-water.
            staging = plan.get("recovery", {}).get("sections", {}).get("staging")
            if isinstance(staging, dict):
                proofs = staging.get("proofs", {})
                staging["completed"] = [name for name in staging.get("completed", [])
                                        if proofs.get(name) in ("complete", "reuse")]
                staging["fractions"] = {}
                for key in ("current", "live", "liveStatus", "counter"):
                    staging.pop(key, None)
                # Drop the matching parent fraction too. Its former max()
                # high-water included the falsely closed later phases and
                # would otherwise freeze newly measured native work again.
                plan["recovery"].get("work", {}).get("fractions", {}).pop("staging", None)
            plan["fractions"].pop("recovery", None)
            if plan.get("current") == "recovery" and isinstance(row.get("progress"), dict):
                nested = _recovery_fraction(plan, row["progress"])
                if nested is not None: plan["fractions"]["recovery"] = min(.999999, nested)
            # Base the mapping on the saved event, before accepting a new
            # producer counter. Status reads do not publish state; otherwise
            # that first new counter could become the anchor and be lost.
            origin = _build_percent(plan)
            if plan["percent"] > origin:
                plan["progressScale"] = {"origin": origin, "floor": plan["percent"]}
    return plan


def begin_attempt(row, boundary):
    """Separate retained work evidence from the new runner's live position.

    Capture 215452 exposed two independent retry leaks: the previous failed
    build row stayed red while earlier stage receipts were being checked, and
    recovery inherited its last live staging step/counter when it reopened.
    Fractions and closed operations are the compatible owner's high-water;
    pointers, counters and open codec scopes describe only one attempt. Clear
    the latter at an actual runner boundary, never while polling status.
    """
    plan = initialize(row)
    if row["id"] == "build": _retain_preparation(plan)
    plan["attemptBoundary"] = boundary
    plan["operationProofs"] = {name: "retained" for name in plan["completed"]}
    plan["liveOperation"] = None
    plan.pop("liveStatus", None)
    plan["liveFractions"] = {}
    plan["preparationScopes"] = {}
    recovery = plan.get("recovery", {})
    recovery.pop("liveSection", None)

    def retained_work(work):
        if not isinstance(work, dict): return
        work["proofs"] = {name: "retained" for name in work.get("completed", [])}
        for key in ("current", "live", "liveStatus", "counter"):
            work.pop(key, None)
        # Closed steps remain retained; an unfinished counter is merely the
        # previous attempt's observation. Overall high-water stays in plan.
        work["fractions"] = {}
        # A previous native reader's index belongs to that package invocation;
        # accepting an earlier package on retry must open its own native scope.
        work.pop("native", None)

    retained_work(recovery.get("work"))
    for work in recovery.get("sections", {}).values(): retained_work(work)
    retained_work(recovery.get("batch", {}).get("work"))
    return plan


def _retain_preparation(plan):
    """Keep closed checkpoint counts apart from an invocation's live items."""
    def valid(scope):
        return isinstance(scope, dict) and type(scope.get("done")) is int and type(scope.get("total")) is int \
            and 0 <= scope["done"] <= scope["total"] and scope["total"] > 0
    saved = plan.get("preparationCompleted", {})
    retained = plan["preparationCompleted"] = {
        name: {"done": scope["done"], "total": scope["total"]}
        for name, scope in (saved.items() if isinstance(saved, dict) else ())
        if name in PLANS["build"] and valid(scope)}
    scopes = plan.get("preparationScopes", {})
    for name, scope in (scopes.items() if isinstance(scopes, dict) else ()):
        if name not in PLANS["build"] or not valid(scope): continue
        done, total = scope.get("done"), scope.get("total")
        previous = retained.get(name, {})
        if previous.get("total") == total: done = max(done, previous.get("done", 0))
        retained[name] = {"done": done, "total": total}
    return retained


def retain_preparation_history(row, events):
    """Recover old closed counters before the new attempt clears live state.

    Older planners discarded the scope on the terminal operation failure. The
    bounded session history still has its preceding closed-checkpoint count.
    A changed choice boundary excludes counters from a previous selection;
    begin_stage later rejects any incompatible qualified work scope.
    """
    if row["id"] != "build" or workflow(row) != "build" or not isinstance(events, list): return
    plan = initialize(row)
    if "project-files" not in plan["completed"]: return
    retained = _retain_preparation(plan)
    recent = events[-128:]
    boundary = max((index for index, item in enumerate(recent)
                    if isinstance(item, dict) and item.get("code") == "choices_updated"), default=-1)
    for event in recent[boundary + 1:]:
        if not isinstance(event, dict) or event.get("stage") != "build" or event.get("code") != "stage_progress": continue
        value = event.get("parameters", {})
        if not isinstance(value, dict) or not isinstance(value.get("phase"), str) or not value["phase"].startswith("prepare-substage:"): continue
        name, done, total = value.get("reportedOperation"), value.get("done"), value.get("total")
        if name not in PLANS["build"] or type(done) is not int or type(total) is not int or not 0 <= done <= total or total < 1: continue
        previous = retained.get(name, {})
        if previous.get("total") == total: done = max(done, previous.get("done", 0))
        retained[name] = {"done": done, "total": total}


def retain_committed(row):
    """Requalify a changed Builder without discarding closed game work.

    Observed item/byte fractions from an unfinished transform have no commit
    witness. Only closed operations, packages and checkpoint counts survive
    the new artifact identity. Their UI status is retained, never fresh reuse.
    This function does not qualify files or authorize a builder cache.
    """
    plan = begin_attempt(row, None)
    # A new source archive can invalidate code, import and the Android player.
    # Keeping those old outputs at 99.9% would conceal the new build's work.
    retained_names = (PLANS["build"][:PLANS["build"].index("weave")] if workflow(row) == "build" else
                      ("update-owned-game", "update-profile") if workflow(row) == "update-profile" else ("update-owned-game",))
    plan["completed"] = [name for name in plan["completed"] if name in retained_names]
    plan["operationProofs"] = {name: "retained" for name in plan["completed"]}
    plan["current"] = None
    plan["fractions"] = {}
    plan.pop("progressScale", None)
    if workflow(row) != "build":
        plan["percent"] = round(_build_percent(plan), 6)
        return plan
    retained = plan.get("preparationCompleted", {})
    for name, scope in retained.items():
        if name not in retained_names or not isinstance(scope, dict): continue
        done, total = scope.get("done"), scope.get("total")
        if type(done) is int and type(total) is int and 0 <= done <= total and total > 0:
            plan["fractions"][name] = min(.99, done / total)
    recovery = plan.get("recovery", {})
    work = recovery.get("work", {})
    sections = recovery.get("sections", {})
    for section, names in (("core", CORE_STEPS), ("staging", STAGING_STEPS)):
        child = sections.get(section)
        if isinstance(child, dict): work.setdefault("fractions", {})[section] = _work_fraction(child, names)
    batch = recovery.get("batch", {})
    # A package is durable only at its completed aggregate boundary. The open
    # package's export/merge counters belong to the previous invocation.
    batch["work"] = _work()
    done, total = batch.get("done"), recovery.get("plannedBatches", batch.get("total"))
    if type(done) is int and type(total) is int and 0 <= done <= total and total > 0:
        work.setdefault("fractions", {})["batches"] = done / total
    _, _, fraction = _recovery_work(recovery)
    if fraction is not None: plan["fractions"]["recovery"] = min(.999999, fraction)
    plan["percent"] = min(99.9, round(_build_percent(plan), 6))
    return plan


def _ratio(value):
    done, total = value.get("done"), value.get("total")
    if done is None or total is None: return None
    return 1. if total == 0 else min(1., done / total)


def _preparation_fraction(plan, operation, value):
    """Only observed items inside the currently opened checkpoint add credit.

    A checkpoint's completed/total counter owns the parent denominator. Its
    item counter contributes at most the next checkpoint, never another work
    unit or a nested platform-image conversion inside that same item.
    """
    phase = value["phase"]
    if phase.startswith("prepare-substage:"):
        ratio = _ratio(value)
        if ratio is None or operation not in PLANS["build"]: return None
        plan.setdefault("preparationScopes", {})[operation] = {
            "name": phase.split(":", 1)[1], "done": value["done"], "total": value["total"],
            "open": value.get("operationStatus") == "start", "itemFraction": 0.}
        _retain_preparation(plan)
        return ratio
    if not phase.startswith("prepare-items:"): return None
    name = phase.split(":", 1)[1]
    if PREPARATION_ITEMS.get(name) != operation: return None
    scope = plan.get("preparationScopes", {}).get(operation, {})
    if not scope.get("open") or scope.get("name") != _item_checkpoint(name) or not scope.get("total"): return None
    ratio = _ratio(value)
    if ratio is None: return None
    if name in ENVIRONMENT_ITEM_SHARES:
        offset, share = ENVIRONMENT_ITEM_SHARES[name]
        ratio = offset + share * ratio
    scope["itemFraction"] = ratio
    scope["items"] = {"done": value["done"], "total": value["total"]}
    return min(1., (scope["done"] + min(.99, ratio)) / scope["total"])


def _work():
    return {"current": None, "fractions": {}, "completed": []}


def _advance_work(work, steps, step, ratio=None, status=None):
    """Only a later actual boundary can close previous scheduled work."""
    if step not in steps: return
    work["live"] = step
    work["liveStatus"] = status or "progress"
    if status in ("complete", "reuse"):
        work["completed"] = list(dict.fromkeys([*work.get("completed", []), step]))
        work.setdefault("proofs", {})[step] = status
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
        work["counter"] = {"phase": phase, "done": value["done"], "total": value["total"], "unit": value.get("unit")}
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
    if phase == "recovery-plan" and type(value.get("total")) is int:
        recovery["plannedBatches"] = value["total"]
    if phase.startswith("recovery-section:"):
        section = phase.split(":", 1)[1]
        _advance_work(recovery["work"], RECOVERY_SECTIONS, section, status=value.get("operationStatus"))
        recovery["liveSection"] = section
    section = recovery["work"].get("current")
    # Compatibility for pre-scope producers: an aggregate schedule gives a
    # real batch denominator, but a lone collection cannot invent one.
    if phase == "recovery-batches":
        _advance_work(recovery["work"], RECOVERY_SECTIONS, "batches")
        section = "batches"
        recovery["liveSection"] = section
    if section not in RECOVERY_SECTIONS: return None
    # Retained high-water remains monotone, while a retry's earlier section is
    # named separately. Its verified reuse must not pretend to start staging.
    live = recovery.get("liveSection", section)
    if live in RECOVERY_SECTIONS: section = live
    work = recovery["sections"].setdefault(section, _work())
    ratio = None
    if section == "batches":
        batch = recovery["batch"]
        if phase in ("recovery-batches", "recovery-batch"):
            done, total = value.get("done"), value.get("total")
            if type(done) is int and type(total) is int and 0 <= done <= total:
                recovery["plannedBatches"] = total
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
    elif section == "staging":
        if phase.startswith("staging-section:"):
            step = phase.split(":", 1)[1]
            if step in STAGING_STEPS:
                if work.get("live") != step: work.pop("counter", None)
                _advance_work(work, STAGING_STEPS, step, _ratio(value), value.get("operationStatus"))
                if value.get("operationStatus") in ("complete", "reuse") or value.get("done") == value.get("total") == 1:
                    work["completed"] = list(dict.fromkeys([*work["completed"], step]))
                    work.setdefault("proofs", {})[step] = value.get("operationStatus") or "complete"
        else:
            effective, measured = _counter_fraction(work, value)
            step = {"staging-copy": "copy", "staging-managed-assemblies": "runtime", "staging-runtime-copy": "runtime",
                    "staging-report-files": "report", "staging-report-hash": "report"}.get(effective)
            if work.get("live") == "native":
                # The preparatory reference pass and original container reads
                # share this open native phase. Neither closes the final audit
                # or the native overlay/Sprite application checkpoint.
                if effective == "recovery-asset-references":
                    step = "native"
                    measured = .5 * measured if measured is not None else None
                elif effective == "recovery-pointer-containers":
                    step = "native"
                    measured = .5 + .49 * measured if measured is not None else None
            elif effective == "recovery-asset-references" and work.get("live") == "audit":
                step = "audit"
            _advance_work(work, STAGING_STEPS, step, measured)
        ratio = _work_fraction(work, STAGING_STEPS)
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
    _advance_work(recovery["work"], RECOVERY_SECTIONS, section, ratio,
                  value.get("operationStatus") if phase.startswith("recovery-section:") else None)
    _, _, fraction = _recovery_work(recovery)
    return fraction


def _recovery_work(recovery):
    """Count one actual task per section or package, without inventing a plan."""
    count = recovery.get("plannedBatches", recovery.get("batch", {}).get("total"))
    if type(count) is not int or count < 0: return None, None, None
    work = recovery.get("work", {})
    completed, fractions = work.get("completed", []), work.get("fractions", {})
    done = sum(1 for section in RECOVERY_SECTIONS if section != "batches" and section in completed)
    fraction = sum(1. if section in completed else fractions.get(section, 0.)
                   for section in RECOVERY_SECTIONS if section != "batches")
    if "batches" in completed:
        done += count; fraction += count
    else:
        done += recovery.get("batch", {}).get("done", 0)
        fraction += count * fractions.get("batches", 0.)
    total = len(RECOVERY_SECTIONS) - 1 + count
    return done, total, min(1., fraction / total)


def _active_work(plan, current):
    if current == "recovery":
        done, total, fraction = _recovery_work(plan.get("recovery", {}))
    elif current in plan.get("preparationScopes", {}):
        scope = plan["preparationScopes"][current]
        done, total = scope["done"], scope["total"]
        fraction = 1. if current in plan["completed"] else plan.get("liveFractions", plan["fractions"]).get(current, 0.)
    else:
        done, total = (int(current in plan["completed"]), 1)
        fraction = 1. if done else plan.get("liveFractions", plan["fractions"]).get(current, 0.)
    return {"operation": current, "done": done, "total": total, "unit": "steps",
            "percent": None if fraction is None else round(100 * fraction, 6)}


def _build_percent(plan):
    shares = build_shares(plan)
    weighted = sum(weight * (1. if name in plan["completed"] else plan["fractions"].get(name, 0.))
                   for name, weight in shares.items())
    return 100 * weighted / sum(shares.values())


def _overview_rows(steps, completed, fractions, active, proofs=None, failed=False, active_status=None):
    proofs = proofs or {}
    rows = []
    for name in steps:
        closed = name in completed
        proof = proofs.get(name, "retained" if closed else None)
        if name == active:
            status = ("failed" if failed else "reused" if active_status == "reuse" else "complete"
                      if active_status == "complete" else "checking" if closed else "running")
        elif closed:
            status = "reused" if proof == "reuse" else "complete" if proof == "complete" else "retained"
        else:
            status = "pending"
        rows.append({"id": name, "status": status, "closed": closed,
                     "percent": 100. if closed else round(100 * fractions.get(name, 0.), 6)})
    return rows


def _build_overview(row, plan):
    """Project durable evidence without running checks or inventing new work.

    The completion frontier is historical high-water. Live retry operations can
    precede it; keeping them separate explains a stable bar during revalidation.
    Retained completion is not labelled freshly verified in the new attempt.
    """
    active = plan.get("liveOperation", plan.get("current"))
    if row["status"] == "complete": active = None
    operations = _overview_rows(planned_operations(row), plan["completed"], plan.get("liveFractions", plan["fractions"]), active,
                               plan.get("operationProofs"), row["status"] == "failed", plan.get("liveStatus"))
    groups = []
    shares = build_shares(plan)
    for name, names in UPDATE_GROUPS.get(workflow(row), BUILD_GROUPS).items():
        members = [item for item in operations if item["id"] in names]
        done = sum(item["closed"] for item in members)
        groups.append({"id": name, "done": done, "total": len(members), "active": active in names,
                       "percent": round(sum(shares[item["id"]] * item["percent"] for item in members)
                                        / sum(shares[item["id"]] for item in members), 6), "operations": members})
    if workflow(row) in UPDATE_PLANS:
        return {"schema": 1, "workflow": workflow(row), "done": sum(item["closed"] for item in operations), "total": len(operations),
                "active": active, "groups": groups, "recovery": {"sections": [], "staging": [], "batches": None}}
    recovery = plan.get("recovery", {})
    work = recovery.get("work", {})
    section = recovery.get("liveSection", work.get("current")) if active == "recovery" else None
    sections = _overview_rows(RECOVERY_SECTIONS, work.get("completed", []), work.get("fractions", {}), section,
                             work.get("proofs"), row["status"] == "failed", work.get("liveStatus"))
    staging = recovery.get("sections", {}).get("staging", {})
    steps = _overview_rows(STAGING_STEPS, staging.get("completed", []), staging.get("fractions", {}),
                          staging.get("live", staging.get("current")) if section == "staging" else None,
                          staging.get("proofs"), row["status"] == "failed", staging.get("liveStatus"))
    batch = recovery.get("batch", {})
    total = recovery.get("plannedBatches", batch.get("total"))
    done = total if "batches" in work.get("completed", []) else batch.get("done", 0)
    if type(total) is not int or total < 0: total, done = None, None
    return {"schema": 1, "done": sum(item["closed"] for item in operations), "total": len(operations),
            "active": active, "groups": groups, "recovery": {"sections": sections,
            "batches": {"done": done, "total": total}, "staging": steps,
            "section": section, "stagingCounter": staging.get("counter") if section == "staging" else None}}


def advance(row, value, operation=None, status=None):
    for key in ("recoverySection", "recoveryBatchIndex", "recoveryBatchTotal", "recoveryNativeIndex", "recoveryNativeTotal", "activeWork", "buildOverview"):
        value.pop(key, None)
    plan = initialize(row); operations = planned_operations(row)
    # One observed attempt boundary, never a status read, marks old completion
    # as retained. Later explicit completion/reuse restores current evidence.
    if value["phase"] == "starting" and value.get("updatedAt") != plan.get("attemptBoundary"):
        begin_attempt(row, value.get("updatedAt"))
    inferred, measured = phase_operation(row["id"], value["phase"], value)
    if row["id"] == "build" and workflow(row) in UPDATE_PLANS:
        inferred = {"game-inputs": "update-owned-game", "mod-inputs": "update-mod-source",
                    "output-verify": "update-verify"}.get(inferred, inferred)
        if inferred is not None and inferred not in operations:
            mapped = update_alias(row, inferred)
            if mapped is not None: value["childOperation"], inferred = inferred, mapped
            else: inferred, measured = None, False
    if row["id"] == "unity" and value["phase"] == "unity-prerequisites" and plan.get("current") != "prerequisites":
        measured = False
    operation = operation or inferred or value.get("reportedOperation")
    status = status or value.get("operationStatus")
    if status is not None: value["operationStatus"] = status
    if row["id"] == "build" and value["phase"].startswith("prepare-items:"):
        # Counter producers need no duplicated parent argument. The actual
        # opened checkpoint, not a historical frontier, establishes ownership.
        name = value["phase"].split(":", 1)[1]
        owner = PREPARATION_ITEMS.get(name)
        scope = plan.get("preparationScopes", {}).get(owner, {})
        active_scope = scope.get("open") and scope.get("name") == _item_checkpoint(name)
        if name in ENVIRONMENT_ITEM_SHARES:
            # A stale or explicitly foreign counter cannot reopen this bank
            # after its actual owner moved on or the runner was restarted.
            active_scope = active_scope and plan.get("liveOperation") == owner and operation in (None, owner)
            if not active_scope:
                # Preserve an explicitly foreign producer owner so replaying
                # this saved observation cannot reauthorize it during a poll.
                if operation is not None: value["reportedOperation"] = operation
                operation = None
        if active_scope:
            operation = owner
    child_boundary = (value["phase"].startswith(("recovery-section:", "staging-section:", "prepare-substage:", "prepare-items:"))
                      or value["phase"] in ("recovery-batches", "recovery-raw-reuse", "recovery-plan", "prepare-project-copy")
                      or workflow(row) in UPDATE_PLANS and value.get("childOperation") is not None)
    parent_status = "progress" if child_boundary else status
    preparation_counter = row["id"] == "build" and value["phase"].startswith(("prepare-substage:", "prepare-items:"))
    if operation in operations:
        value["reportedOperation"] = operation
        plan["liveOperation"] = operation
        plan["liveStatus"] = parent_status or "progress"
        if parent_status == "start": plan.setdefault("liveFractions", {})[operation] = 0.
        if parent_status in ("complete", "reuse"):
            plan.setdefault("operationProofs", {})[operation] = parent_status
            plan["completed"] = list(dict.fromkeys([*plan["completed"], operation]))
        index = operations.index(operation)
        current = plan.get("current")
        if current not in operations or index >= operations.index(current):
            # Reaching the next scheduled boundary proves previous operations
            # returned successfully or were explicitly verified/reused/skipped.
            plan["completed"] = list(dict.fromkeys([*plan["completed"], *operations[:index]]))
            plan["current"] = operation
            if parent_status in ("complete", "reuse"):
                plan["completed"] = list(dict.fromkeys([*plan["completed"], operation]))
    current = plan.get("current")
    if preparation_counter:
        fraction = _preparation_fraction(plan, operation, value)
        if fraction is not None:
            plan.setdefault("liveFractions", {})[operation] = min(.99, fraction)
            plan["fractions"][operation] = max(plan["fractions"].get(operation, 0.), min(.99, fraction))
    elif row["id"] == "build" and value["phase"].startswith("operation:") and operation in operations:
        plan.get("preparationScopes", {}).pop(operation, None)
    if row["id"] == "build" and plan.get("liveOperation", current) == "recovery":
        nested = _recovery_fraction(plan, value)
        if nested is not None:
            # A retry's live recovery may precede a later historical frontier.
            # Its counters own only recovery, never unfinished Player/import work.
            plan["fractions"]["recovery"] = max(plan["fractions"].get("recovery", 0.), min(.999999, nested))
            plan.setdefault("liveFractions", {})["recovery"] = min(.999999, nested)
        recovery = plan.get("recovery", {})
        section = recovery.get("work", {}).get("current")
        if section in RECOVERY_SECTIONS:
            value["recoverySection"] = section
        batch = recovery.get("batch", {})
        done, total = batch.get("done"), batch.get("total")
        if (section == "batches" and value["phase"] != "recovery-batches"
                and type(done) is int and type(total) is int and 0 <= done < total):
            value.update(recoveryBatchIndex=done + 1, recoveryBatchTotal=total)
            native = batch.get("work", {}).get("native", {})
            index, native_total = native.get("index"), native.get("total")
            if type(index) is int and type(native_total) is int and 0 <= index < native_total:
                value.update(recoveryNativeIndex=index + 1, recoveryNativeTotal=native_total)
    # Ignore nested one-file hashes and incidental version commands. Only a
    # known aggregate counter or Unity's own task counter contributes a fraction.
    if current in operations and measured and (operation is None or operation == current) and value["percent"] is not None:
        fraction = min(.99, _ratio(value))
        plan["fractions"][current] = max(plan["fractions"].get(current, 0.), fraction)
    live = plan.get("liveOperation", current)
    if live in operations and measured and (operation is None or operation == live) and value["percent"] is not None:
        plan.setdefault("liveFractions", {})[live] = min(.99, _ratio(value))
    if row["status"] == "complete":
        plan["completed"] = list(operations)
        plan["current"] = operations[-1]
        current = operations[-1]
        for name in operations:
            if plan.setdefault("operationProofs", {}).get(name) != "reuse": plan["operationProofs"][name] = "complete"
    done = sum(1. if name in plan["completed"] else plan["fractions"].get(name, 0.) for name in operations)
    # Keep small measured byte/file contributions for the bar, even when its
    # concise percentage label rounds them to two decimal places.
    if row["id"] == "build":
        number = _build_percent(plan)
        scale = plan.get("progressScale")
        if scale and number >= scale["origin"] and scale["origin"] < 100:
            number = scale["floor"] + (100 - scale["floor"]) * (number - scale["origin"]) / (100 - scale["origin"])
    else:
        number = 100 * done / len(operations)
    number = min(99.9, round(number, 6))
    plan["percent"] = max(plan["percent"], number)
    value.update(stagePercent=100. if row["status"] == "complete" else plan["percent"],
                 stageOperation=plan.get("liveOperation", current), stageDone=round(done, 3), stageTotal=len(operations))
    live = plan.get("liveOperation", current)
    if live in operations: value["activeWork"] = _active_work(plan, live)
    if row["id"] == "build": value["buildOverview"] = _build_overview(row, plan)
    return value
