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
    "build": ("game-inputs", "mod-inputs", "profile-inputs", "source-snapshot", "game-snapshot", "snapshot-check", "recovery", "project-files", "startup-content", "native-runtime", "audio", "textures", "graphics", "mod-banks", "preparation-contracts", "weave", "unity-import", "package-api", "unity-validation", "content-bank", "player", "delivery", "output-verify"),
    "install": ("connect", "apk", "content", "launch", "output-verify"),
}

# Reserved work spans, not time estimates. The October 6 Windows witness spent
# minutes exporting each of sixteen data packages, while seven short input
# operations had already consumed 28.6% of the equally weighted old bar. Give
# conversion/import their own useful spans and count each scheduled package
# inside conversion. Only actual counters and successful boundaries advance it.
BUILD_SHARES = dict(zip(PLANS["build"], (1, 1, 1, 1, 2, 1, 40, 1, 1, 2, 2, 5, 2, 2, 1, 2, 18, 1, 1, 4, 8, 2, 1)))
BUILD_GROUPS = {"inputs": PLANS["build"][:6], "recovery": ("recovery",),
                "project": PLANS["build"][7:15], "code": ("weave",),
                "import": ("unity-import", "package-api", "unity-validation"),
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
MEMORY_OBSERVATIONS = frozenset(("native-compiler-profile", "native-memory-retry", "native-memory-wait",
                               "asset-memory-retry", "asset-memory-wait"))
UNITY_IMPORT_OBSERVATIONS = frozenset(("unity-work-plan", "unity-asset-import", "unity-import-activity"))
# One finite schedule belongs to one actual Editor invocation. Asset coverage
# is a scoped census, and method parts are scheduled work shares rather than
# time estimates. Native graph/shader/task counters fill only their own part;
# a local 100% never closes another graph or the Editor process.
UNITY_WORK_OWNERS = frozenset(("mod-banks", "unity-import", "package-api", "unity-validation",
    "content-bank", "player", "update-code", "update-art"))
UNITY_METHOD_PARTS = {
    "mod-banks": {"build": 1.},
    "unity-import": {"sdk": 1.},
    "package-api": {"sdk": 1.},
    "unity-validation": {"configuration": .1, "checks": .9},
    "content-bank": {"catalog": .1, "shaders": .1, "build": .65, "audit": .05, "postprocess": .1},
    "player": {"configuration": .05, "scenes": .05, "il2cpp": .2, "native": .5, "packaging": .2},
    "update-code": {"configuration": .05, "scenes": .05, "il2cpp": .2, "native": .5, "packaging": .2},
    "update-art": {"build": 1.},
}
UNITY_PART_COUNTERS = {
    "configuration": {"unity-configuration": 1.}, "checks": {"unity-validation-tasks": 1.},
    "sdk": {"unity-sdk-tasks": 1.}, "scenes": {"unity-player-scenes": 1.},
    "catalog": {"unity-addressables-keys": .3, "unity-addressables-entries": .7},
    "shaders": {"unity-addressables-shader-roots": .3, "unity-addressables-shaders": .7},
    "audit": {"unity-native-shader-audit-read": .3, "unity-native-shader-audit-objects": .6,
              "unity-native-shader-audit": .1},
    "postprocess": {"unity-content-post-tasks": 1.},
    "packaging": {"unity-gradle-tasks": 1.}, "il2cpp": {"unity-il2cpp": 1.},
}
# A native graph or compiler pass can finish before serialization or validation
# fails. Only these actual caller boundaries close their complete logical part.
UNITY_PART_TERMINALS = {"content-bank": {"build": "unity-addressables-build", "audit": "unity-native-shader-audit"}}
CONTENT_PACK_SHARES = {"native-content-source-hash": .15, "native-content-native-hash": .15,
                       "native-content-write": .55, "native-content-final-hash": .15}
PACKAGE_API_SHARES = {"package-api-bind": .65, "package-api-output": .20, "package-api-publish": .15}
UNITY_VALIDATION_SHARES = {"unity-configuration": .1, "unity-validation-tasks": .9}
# These counters describe real child work, not the entire import/bank/Player.
# Only the complete known validation schedule contributes a parent fraction.
UNITY_CHILD_COUNTERS = frozenset(("unity-sdk-tasks", "unity-original-scenes", "unity-validation-sprites",
    "unity-validation-campaign-sprites", "unity-addressables-keys", "unity-addressables-entries",
    "unity-addressables-shaders", "unity-addressables-shader-roots", "unity-content-post-tasks",
    "unity-content-delivery", "unity-player-scenes", "unity-player-native-result", "unity-gradle-tasks",
    "unity-script-bindings", "unity-script-assets", "unity-script-write", "unity-campaign-objects", "unity-campaign-scenes", "unity-campaign-audio", "unity-campaign-atlases", "unity-campaign-packed-sprites",
    "unity-native-shader-audit", "unity-native-shader-audit-read", "unity-native-shader-audit-objects",
    "unity-scene-objects", "unity-scene-dependencies", "unity-texture-cubes", "unity-texture-targets",
    "unity-texture-owners", "unity-texture-floating", "unity-texture-platform", "unity-compute-identities", "unity-compute-kernels",
    "unity-shader-materials", "unity-shader-identities", "unity-shader-retention", "unity-shader-programs"))
# Actual known loop plans inside one TaskSequence call. These are work shares,
# not elapsed-time estimates. Unlisted counters remain useful local detail.
UNITY_VALIDATION_TASK_PARTS = {
    "Load the required original scene assets": {"unity-original-scenes": 1.},
    "Remap and validate original script bindings": {"unity-script-bindings": .05, "unity-script-assets": .9, "unity-script-write": .05},
    "Validate startup Sprite geometry": {"unity-validation-sprites": 1.},
    "Validate original Campaign Sprite geometry": {"unity-validation-campaign-sprites": 1.},
    "Validate original Campaign assets": {"unity-campaign-objects": .6, "unity-campaign-atlases": .05,
        "unity-campaign-packed-sprites": .05, "unity-campaign-audio": .1, "unity-campaign-scenes": .2},
    "Validate original Campaign textures": {"unity-texture-cubes": .05, "unity-texture-platform": .05,
        "unity-texture-floating": .4, "unity-texture-targets": .05, "unity-texture-owners": .45},
    "Validate original Campaign compute sources": {"unity-compute-identities": 1.},
    "Retain original Campaign Shader identities": {"unity-shader-programs": .2, "unity-shader-retention": .4,
        "unity-shader-identities": .2, "unity-shader-materials": .2},
}
PREPARATION_ITEMS = {"bundled-audio": "audio", "native-cubemaps": "textures",
                     "ordinary-texture-audit": "textures", "native-texture2d": "textures",
                     "native-sprites": "startup-content", "environment-bundles": "mod-banks",
                     "environment-meshes": "mod-banks"}
# These actual producer passes prepare geometry for one resource-bank
# checkpoint. Geometry is only half its work: Unity still has to compile the
# bank and return a qualified result. Its final mesh cannot close the scope.
ENVIRONMENT_ITEM_SHARES = {"environment-bundles": (0., .25), "environment-meshes": (.25, .25)}
# Reservations inside one real preparation checkpoint, never elapsed-time
# estimates. Each measured pass adds only its own share. Container loads and
# Sprite writes interleave, so an item cannot imply every container is loaded.
STARTUP_ITEM_SCHEDULES = {
    "startup-movies": {"startup-movies-backup": .05, "startup-movies-clips": .04,
                       "startup-movies-scenes": .06, "startup-movies-assets": .75,
                       "startup-movies-media": .10},
    "native-sprites": {"native-sprites-identities": .05, "native-sprites-packed": .10,
                       "native-sprites-targets": .05, "native-sprites-containers": .10,
                       "native-sprites": .70},
    "startup-audio": {"startup-audio-metadata": .05, "startup-audio-preflight": .65,
                      "startup-audio-write": .30},
}
STARTUP_ITEM_CHECKPOINTS = {name: checkpoint for checkpoint, parts in STARTUP_ITEM_SCHEDULES.items() for name in parts}
STARTUP_ITEM_CHECKPOINTS["startup-audio-decode"] = "startup-audio"
PREPARATION_ITEMS.update({name: "startup-content" for name in STARTUP_ITEM_CHECKPOINTS})

# Count each actual pass inside its one open checkpoint. The codec's final
# decoded file cannot consume the shares reserved for references and publication.
for checkpoint in ("native-cubemaps", "native-texture2d"):
    STARTUP_ITEM_SCHEDULES[checkpoint] = {
        checkpoint + "-identities": .02, checkpoint + "-pointer-owners": .08,
        checkpoint + "-backup": .10, checkpoint: .55,
        checkpoint + "-manifests": .10, checkpoint + "-reference-discovery": .01,
        checkpoint + "-reference-scan": .10, checkpoint + "-reference-receipts": .02,
        checkpoint + "-contracts": .02,
    }
STARTUP_ITEM_SCHEDULES["campaign-compute"] = {
    "campaign-compute-identities": .025, "campaign-compute-pointer-owners": .075,
    "campaign-compute-backup": .10, "campaign-compute-discovery": .005,
    "campaign-compute-inventory": .01, "campaign-compute-inputs": .01,
    "campaign-compute-kernels": .34, "campaign-compute-shaders": .01,
    "campaign-compute-original-check": .01, "campaign-compute-verify": .025,
    "campaign-compute-apply": .05, "campaign-compute-manifests": .10,
    "campaign-compute-reference-identities": .01, "campaign-compute-reference-index": .01,
    "campaign-compute-reference-targets": .01, "campaign-compute-reference-discovery": .01,
    "campaign-compute-reference-scan": .14, "campaign-compute-reference-write": .015,
    "campaign-compute-reference-identity-verify": .02,
    "campaign-compute-publish": .005, "campaign-compute-contracts": .02,
}
STARTUP_ITEM_SCHEDULES["campaign-shaders"] = {
    "campaign-shaders-backup": .03, "campaign-shaders-identities": .015,
    "campaign-shaders-bundles": .005, "campaign-shaders-containers": .005,
    "campaign-shaders-inventory": .53, "campaign-shaders-materials": .02,
    "campaign-shaders-binary-materials": .005, "campaign-shaders-material-containers": .005,
    "campaign-shaders-programs": .20, "campaign-shaders-sources": .12,
    "campaign-shaders-copy": .03, "campaign-shaders-publish": .02,
    "campaign-shaders-contracts": .015,
}
CHECKPOINT_OWNERS = {
    "base-project": "project-files", "native-runtime": "native-runtime", "bundled-audio": "audio",
    **{name: "startup-content" for name in ("post-effects", "loading-resources", "startup-movies",
       "native-sprites", "loading-sprite", "startup-audio", "ui-recipes", "startup-ui", "startup-blur",
       "dlc-selection", "file-extras")},
    **{name: "textures" for name in ("native-cubemaps", "ordinary-texture-audit", "native-texture2d")},
    **{name: "graphics" for name in ("campaign-compute", "campaign-shaders")},
    **{name: "mod-banks" for name in ("startup-archive", "archive-cleanup", "package-settings", "mod-resource-banks")},
    **{name: "preparation-contracts" for name in ("compiler-contracts", "case-paths", "startup-compute", "script-orders", "final-settings")},
}
for checkpoint, owner in CHECKPOINT_OWNERS.items():
    for suffix in ("backup", "contracts"):
        name = checkpoint + "-" + suffix
        STARTUP_ITEM_CHECKPOINTS[name] = checkpoint
        PREPARATION_ITEMS[name] = owner
for checkpoint in ("native-cubemaps", "native-texture2d", "campaign-compute", "campaign-shaders"):
    for name in STARTUP_ITEM_SCHEDULES[checkpoint]:
        STARTUP_ITEM_CHECKPOINTS[name] = checkpoint
        PREPARATION_ITEMS[name] = CHECKPOINT_OWNERS[checkpoint]
for name in ("campaign-shaders-extract", "campaign-shaders-variants"):
    STARTUP_ITEM_CHECKPOINTS[name] = "campaign-shaders"
    PREPARATION_ITEMS[name] = "graphics"

# These are the producer's actual checkpoint plans. A denominator qualifies a
# plan; an unfamiliar target is displayed from observed boundaries instead of
# inventing unchecked children. Completed checkpoint counts and the current
# checkpoint's ordinal are intentionally different numbers.
PREPARATION_CHECKPOINTS = {
    "project-files": ("base-project",),
    "startup-content": ("post-effects", "loading-resources", "startup-movies", "native-sprites",
                        "loading-sprite", "startup-audio", "ui-recipes", "startup-ui", "startup-blur",
                        "dlc-selection", "file-extras"),
    "native-runtime": ("native-runtime",), "audio": ("bundled-audio",),
    "textures": ("native-cubemaps", "ordinary-texture-audit", "native-texture2d"),
    "graphics": ("campaign-compute", "campaign-shaders"),
    "mod-banks": ("startup-archive", "archive-cleanup", "package-settings", "mod-resource-banks"),
    "preparation-contracts": ("compiler-contracts", "case-paths", "startup-compute", "script-orders", "final-settings"),
}
CONDITIONAL_PREPARATION_PASSES = {
    "campaign-shaders": ("campaign-shaders-binary-materials", "campaign-shaders-material-containers"),
}


def _checkpoint_names(operation, total):
    names = PREPARATION_CHECKPOINTS.get(operation, ())
    if operation == "startup-content" and total == 8:
        names = tuple(name for name in names if name not in ("native-sprites", "ui-recipes", "file-extras"))
    if operation == "preparation-contracts" and total == 4:
        names = tuple(name for name in names if name != "startup-compute")
    return names if len(names) == total else ()


def _item_checkpoint(name):
    return STARTUP_ITEM_CHECKPOINTS.get(name, "mod-resource-banks" if name in ENVIRONMENT_ITEM_SHARES else name)


def workflow(row):
    return row.get("workMode") if row.get("workMode") in UPDATE_PLANS else "build"


def planned_operations(row):
    if workflow(row) == "update-profile":
        profile_plans = {
            "tools": ("qualify", "download-apkJdk", "extract-apkJdk", "verify-apkJdk", "download-apkBuildTools", "extract-apkBuildTools", "verify-apkBuildTools", "output-verify"),
            "source": ("source-verify", "output-verify"),
            "unity": ("prerequisites", "output-verify"),
        }
        if row["id"] in profile_plans: return profile_plans[row["id"]]
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
        # A native Shader pass has its own variant denominator, not the total
        # import/bank/player workload. Only the Editor's public task counter is
        # a measured task fraction; native passes remain visible child detail.
        if phase == "unity-shader-task": return None, True
        if phase == "unity-shader-compile": return None, False
    if stage == "install" and phase == "install-content": return "content", True
    return None, False


def initialize(row):
    plan = row.get("progressPlan")
    operations = planned_operations(row)
    mode = workflow(row)
    if not isinstance(plan, dict) or plan.get("version") not in (1, 2) or plan.get("workflow", "build") != mode:
        plan = row["progressPlan"] = {"version": 2, "workRevision": WORK_REVISION, "workflow": mode, "current": None, "completed": [], "fractions": {}, "percent": 0.0}
    plan["workflow"] = mode
    if row["id"] == "build" and mode == "build" and plan.get("unityOrderRevision") != 1:
        # Older launchers labelled the first full import as package-api. A
        # saved start is no proof that this newly earlier import returned.
        # Translate that historical live owner without changing durable work
        # or the saved bar. The next runner boundary removes this display seam.
        plan["unityOrderRevision"] = 1
        if plan.get("current") == "package-api" and "package-api" not in plan.get("completed", []) \
                and "unity-import" not in plan.get("completed", []):
            plan["legacyPackageApiImport"] = True
            plan["current"] = "unity-import"
            if plan.get("liveOperation") == "package-api": plan["liveOperation"] = "unity-import"
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
    plan.pop("legacyPackageApiImport", None)
    plan["operationProofs"] = {name: "retained" for name in plan["completed"]}
    plan["liveOperation"] = None
    plan.pop("liveStatus", None)
    plan["liveFractions"] = {}
    plan["preparationScopes"] = {}
    # A retained checkpoint is durable; the previous run's open pass/variant
    # counter is not. Preserve closed evidence and remove stale live detail.
    for checkpoints in plan.get("preparationDetails", {}).values():
        for checkpoint in checkpoints.values():
            if checkpoint.get("closed"):
                checkpoint["status"] = "retained"
                for part in checkpoint.get("passes", {}).values():
                    if part.get("closed"): part["status"] = "retained"
            else:
                checkpoint.update(status="pending", passes={})
            checkpoint.pop("active", None)
    plan["unityCompiler"] = {}
    # Counts describe one real Editor invocation. Unity's Library is retained
    # independently; a new invocation must not claim the last invocation's
    # completed import operations as fresh work. Closed owner evidence stays.
    plan["unityImports"] = {}
    plan["contentPack"] = {}
    plan["packageApi"] = {}
    plan["unityValidation"] = {}
    plan["unityTasks"] = {}
    plan["unityWork"] = {}
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


def _observe_preparation(plan, operation, value):
    """Keep named checkpoint/pass evidence apart from weighted percentages.

    Capture123047 showed a valid 1/2 closed checkpoint count for hundreds of
    Shader bindings. The current second checkpoint and its distinct measured
    passes were missing. Never convert a local alias 100% into completion of
    the inventory pass, checkpoint, or operation.
    """
    phase, status = value["phase"], value.get("operationStatus", "progress")
    if operation not in PREPARATION_CHECKPOINTS: return
    records = plan.setdefault("preparationDetails", {}).setdefault(operation, {})
    if phase.startswith("prepare-substage:"):
        name = phase.split(":", 1)[1]
        if CHECKPOINT_OWNERS.get(name) != operation: return
        record = records.setdefault(name, {"passes": {}})
        record.update(closed=status in ("complete", "reuse"), status=status)
        if status == "start": record["active"] = None
        return
    scope = plan.get("preparationScopes", {}).get(operation, {})
    if not scope.get("open") or plan.get("liveOperation") != operation: return
    name = scope.get("name")
    if CHECKPOINT_OWNERS.get(name) != operation: return
    if not phase.startswith("prepare-items:"): return
    part = phase.split(":", 1)[1]
    if STARTUP_ITEM_CHECKPOINTS.get(part, _item_checkpoint(part)) != name: return
    ratio = _ratio(value)
    if ratio is None: return
    record = records.setdefault(name, {"passes": {}})
    record.update(closed=False, status="start")
    if part in ("campaign-shaders-extract", "campaign-shaders-variants"):
        # These repeat for every Shader and belong to the inventory's current
        # item. Display their own counts, without adding another planned pass.
        record["nestedCounter"] = {key: value.get(key) for key in ("phase", "done", "total", "unit", "detail")}
        return
    record.pop("nestedCounter", None)
    previous = record["passes"].get(part, {})
    closed = previous.get("closed", False) or status in ("complete", "reuse")
    record["passes"][part] = {"closed": closed, "status": status, "percent": round(100 * ratio, 6),
                               "done": value.get("done"), "total": value.get("total"), "unit": value.get("unit")}
    record["active"] = part


def _preparation_overview(plan, operation, failed=False):
    scope = plan.get("preparationScopes", {}).get(operation, plan.get("preparationCompleted", {}).get(operation, {}))
    total, done = scope.get("total"), scope.get("done")
    if type(total) is not int or type(done) is not int or not 0 <= done <= total: return None
    names = _checkpoint_names(operation, total)
    if not names: return None
    records = plan.get("preparationDetails", {}).get(operation, {})
    active = scope.get("name") if scope.get("open") and plan.get("liveOperation") == operation else None
    rows = []
    for index, name in enumerate(names):
        record = records.get(name, {})
        closed = record.get("closed", False) or index < done or operation in plan["completed"]
        status = ("failed" if failed else "checking" if closed else "running") if name == active else (
            "reused" if record.get("status") == "reuse" else "complete" if record.get("status") == "complete"
            else "retained") if closed else "pending"
        parts = record.get("passes", {})
        schedule = STARTUP_ITEM_SCHEDULES.get(name, {})
        part_names = tuple(schedule) if name == active else tuple(parts)
        conditional = CONDITIONAL_PREPARATION_PASSES.get(name, ())
        branch_observed = any(part in parts for part in conditional)
        branch_finished = any(part in parts for part in ("campaign-shaders-programs", "campaign-shaders-sources",
                             "campaign-shaders-copy", "campaign-shaders-publish", "campaign-shaders-contracts"))
        # Native binary materials are a conditional producer branch. Before
        # witnessing that branch or the following producer, its possible plan
        # is visible but cannot establish an exact remaining-task denominator.
        if conditional and branch_finished and not branch_observed:
            part_names = tuple(part for part in part_names if part not in conditional)
        plan_known = bool(schedule) and (not conditional or branch_observed or branch_finished)
        # A complete receipt may omit pass details on warm reuse. The aggregate
        # proof is sufficient; do not expose freshly pending children below it.
        aggregate = closed and (not parts or any(not part.get("closed") for part in parts.values())
                                or bool(schedule) and not set(schedule).issubset(parts))
        if aggregate: part_names = ()
        child_rows = _overview_rows(part_names, [part for part, data in parts.items() if data.get("closed")],
                                    {part: data.get("percent", 0.) / 100 for part, data in parts.items()},
                                    record.get("active") if name == active else None,
                                    {part: data.get("status") for part, data in parts.items()}, failed,
                                    parts.get(record.get("active"), {}).get("status"))
        for item in child_rows:
            data = parts.get(item["id"], {})
            if data: item["counter"] = {key: data.get(key) for key in ("done", "total", "unit")}
            if item["id"] in conditional and not branch_observed: item["conditional"] = True
        child_done = sum(item["closed"] for item in child_rows)
        current_part = record.get("active")
        rows.append({"id": name, "closed": closed, "status": status,
                     "percent": 100. if closed else round(100 * scope.get("itemFraction", 0.), 6) if name == active else 0.,
                     "passes": child_rows, "passDone": child_done, "passTotal": len(child_rows),
                     "passPlanKnown": plan_known,
                     "passActive": current_part if name == active else None, "detail": "aggregate" if aggregate else "observed",
                     "nestedCounter": record.get("nestedCounter") if name == active else None})
    return {"done": sum(item["closed"] for item in rows), "total": total, "remaining": sum(not item["closed"] for item in rows),
            "active": active, "index": names.index(active) + 1 if active in names else None, "checkpoints": rows}


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
    scope = plan.get("preparationScopes", {}).get(operation, {})
    nested_movie_bytes = phase == "file-hash" and scope.get("name") == "startup-movies"
    nested_audio_blocks = phase == "prepare-items:startup-audio-decode" and scope.get("name") == "startup-audio"
    nested_shader_aliases = phase in ("prepare-items:campaign-shaders-extract", "prepare-items:campaign-shaders-variants") and scope.get("name") == "campaign-shaders"
    if nested_movie_bytes or nested_audio_blocks or nested_shader_aliases:
        parent = scope.get("itemCounter", {})
        allowed = (("campaign-shaders-inventory",) if nested_shader_aliases else
                   ("startup-audio-preflight", "startup-audio-write") if nested_audio_blocks else ("startup-movies-media",))
        if parent.get("name") not in allowed or not parent.get("total"): return None
        name = parent["name"]
    elif phase.startswith("prepare-items:"):
        name = phase.split(":", 1)[1]
    else: return None
    if PREPARATION_ITEMS.get(name) != operation: return None
    if not scope.get("open") or scope.get("name") != _item_checkpoint(name) or not scope.get("total"): return None
    ratio = _ratio(value)
    if ratio is None: return None
    if nested_movie_bytes or nested_audio_blocks or nested_shader_aliases:
        # Repeated source/derived hashes belong to at most the currently open
        # media file. They never count another delivered movie or close it.
        if nested_shader_aliases:
            parts = scope.setdefault("shaderParts", {})
            part = phase.split(":", 1)[1]
            parts[part] = max(parts.get(part, 0.), ratio)
            ratio = .10 * parts.get("campaign-shaders-extract", 0.) + .90 * parts.get("campaign-shaders-variants", 0.)
        ratio = min(1., (parent["done"] + min(.99, ratio)) / parent["total"])
    else:
        if name == "campaign-shaders-inventory":
            parent = scope.get("itemCounter", {})
            if parent.get("name") != name or parent.get("done") != value["done"]:
                scope.pop("shaderParts", None)
        scope["itemCounter"] = {"name": name, "done": value["done"], "total": value["total"]}
    if name in ENVIRONMENT_ITEM_SHARES:
        offset, share = ENVIRONMENT_ITEM_SHARES[name]
        ratio = offset + share * ratio
    schedule = STARTUP_ITEM_SCHEDULES.get(scope.get("name"))
    if schedule and (name != "native-sprites" or "itemParts" in scope):
        parts = scope.setdefault("itemParts", {})
        parts[name] = max(parts.get(name, 0.), ratio)
        ratio = sum(share * parts.get(part, 0.) for part, share in schedule.items())
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


def _unity_part(plan, owner, value):
    phase = value["phase"]
    parts = UNITY_METHOD_PARTS.get(owner, {})
    if owner == "unity-validation" and phase in UNITY_CHILD_COUNTERS:
        return "configuration" if phase == "unity-configuration" else "checks"
    for name in parts:
        if phase in UNITY_PART_COUNTERS.get(name, {}): return name
    if owner == "content-bank" and phase in ("unity-native-shader-audit", "unity-native-shader-audit-read", "unity-native-shader-audit-objects"):
        return "audit"
    if phase.startswith("bee-actions:") or phase in ("unity-bee-activity", "unity-native-build"):
        return "native" if "native" in parts else "build" if "build" in parts else None
    if phase in ("unity-addressables-build", "unity-shader-compile", "unity-shader-task", "unity-shaders") \
            or phase.startswith("unity-shader-task:"):
        return "build" if "build" in parts else "native" if "native" in parts else None
    if phase in ("unity-gradle",): return "packaging" if "packaging" in parts else None
    if phase in ("unity-progress", "unity-player-memory") or phase.startswith("unity-progress:"):
        work = plan.get("unityWork", {}).get(owner, {})
        if work.get("active") == "method":
            return work.get("part") or ("build" if "build" in parts else "native" if "native" in parts else None)
    return None


def _unity_work_fraction(work):
    names = ("assets", "method", "result") if work.get("parts") else ("assets", "result")
    shares = {"assets": .15, "method": .80, "result": .05} if work.get("parts") else {"assets": .95, "result": .05}
    return sum(shares[name] * (1. if name in work.get("completed", []) else work.get("fractions", {}).get(name, 0.)) for name in names)


def _observe_unity_work(plan, owner, value, status):
    """Join one launch's counted scopes without confusing local and global work."""
    phase = value["phase"]
    works = plan.setdefault("unityWork", {})
    if phase == "unity-work-invocation" and status == "start":
        previous = works.get(owner, {})
        if previous.get("startedAt") != value.get("updatedAt"):
            works[owner] = {"startedAt": value.get("updatedAt"), "active": "assets", "completed": [],
                "fractions": {}, "parts": dict(UNITY_METHOD_PARTS.get(owner, {})), "partFractions": {}, "counters": {}}
            for key in ("unityImports", "unityCompiler", "unityTasks"):
                plan.get(key, {}).pop(owner, None)
    elif phase == "unity-work-plan" and owner not in works:
        works[owner] = {"startedAt": value.get("updatedAt"), "active": "assets", "completed": [],
            "fractions": {}, "parts": dict(UNITY_METHOD_PARTS.get(owner, {})), "partFractions": {}, "counters": {}}
    work = works.get(owner)
    if not work: return False
    if phase == "unity-work-invocation":
        work["invocation"] = {key: value.get(key) for key in ("phase", "done", "total", "unit", "detail")}
        if status in ("complete", "reuse"):
            work.update(active="result", finished=True)
            work["completed"] = ["assets", *(("method",) if work.get("parts") else ()), "result"]
        elif status == "failed": work["failed"] = True
    elif phase in UNITY_IMPORT_OBSERVATIONS:
        # An importer may switch from known unique asset coverage to Unity's
        # native current-batch total. The counter remains visibly scoped, and
        # an activity heartbeat cannot erase the known census denominator.
        work["counters"]["assets"] = {key: value.get(key) for key in ("phase", "done", "total", "unit", "detail")}
        ratio = _ratio(value)
        if ratio is not None:
            work["fractions"]["assets"] = max(work["fractions"].get("assets", 0.), min(.99, ratio))
    else:
        if owner in ("player", "update-code") and phase == "unity-player-native-result" and status in ("complete", "reuse") and _ratio(value) == 1.:
            # BuildReport.Succeeded proves these native passes returned. The
            # caller still owns original final evidence and publication, so
            # this does not close its method, invocation or Player operation.
            work["active"] = "method"
            if "assets" not in work["completed"]: work["completed"].append("assets")
            for completed_part in ("configuration", "scenes", "il2cpp", "native", "packaging"):
                if completed_part in work.get("parts", {}): _close_unity_part(work, completed_part, status)
            work["fractions"]["method"] = sum(weight * work["partFractions"].get(name, 0.) for name, weight in work["parts"].items())
        method_boundary = phase == "unity-work-stage:method"
        part = _unity_part(plan, owner, value)
        method_entry = phase in ("unity-sdk-tasks", "unity-configuration", "unity-player-scenes", "unity-validation-tasks")
        if method_boundary or method_entry or part and work.get("active") != "assets":
            work["active"] = "method"
            if "assets" not in work["completed"]: work["completed"].append("assets")
        if method_boundary:
            work["counters"]["method"] = {key: value.get(key) for key in ("phase", "done", "total", "unit", "detail")}
            if status in ("complete", "reuse"):
                work["completed"] = list(dict.fromkeys([*work["completed"], "method"]))
                work["active"] = "result"
        if part and work.get("active") == "method" and "method" not in work["completed"]:
            work["part"] = part
            work["counters"][part] = {key: value.get(key) for key in ("phase", "done", "total", "unit", "detail")}
            ratio = _ratio(value)
            if owner == "unity-validation" and part == "checks":
                ratio = plan.get("unityValidation", {}).get("fractions", {}).get("unity-validation-tasks", ratio)
            if ratio is not None:
                schedule = UNITY_PART_COUNTERS.get(part)
                if schedule and phase in schedule:
                    seen = work.setdefault("partCounters", {}).setdefault(part, {})
                    # Reading every bundle is not the authoritative receipt
                    # gate. Its last 10% belongs to the successful C# return.
                    if owner == "content-bank" and phase == "unity-native-shader-audit" and status not in ("complete", "reuse"):
                        ratio = 0.
                    seen[phase] = max(seen.get(phase, 0.), min(.99, ratio))
                    terminal = status in ("complete", "reuse") and _ratio(value) == 1.
                    if terminal:
                        seen[phase] = 1.
                        work.setdefault("partCounterProofs", {}).setdefault(part, {})[phase] = status
                    ratio = sum(weight * seen.get(name, 0.) for name, weight in schedule.items())
                    proofs = work.get("partCounterProofs", {}).get(part, {})
                    if all(name in proofs for name in schedule):
                        _close_unity_part(work, part, "reuse" if all(proof == "reuse" for proof in proofs.values()) else "complete")
                if phase == UNITY_PART_TERMINALS.get(owner, {}).get(part) and status in ("complete", "reuse") and _ratio(value) == 1.:
                    _close_unity_part(work, part, status)
                # A repeated Shader pass or Bee DAG cannot restart this part's
                # contribution. It also cannot prove that all future DAGs end.
                work["partFractions"][part] = max(work["partFractions"].get(part, 0.), min(.99, ratio))
                work["fractions"]["method"] = sum(weight * work["partFractions"].get(name, 0.) for name, weight in work["parts"].items())
    fraction = min(.99, _unity_work_fraction(work))
    if owner == "content-bank": fraction *= .5
    if owner == "mod-banks" and plan.get("preparationScopes", {}).get(owner, {}).get("open"):
        scope = plan["preparationScopes"][owner]
        fraction = (scope["done"] + .5 + .5 * fraction) / scope["total"]
    plan["fractions"][owner] = max(plan["fractions"].get(owner, 0.), fraction)
    plan.setdefault("liveFractions", {})[owner] = max(plan.get("liveFractions", {}).get(owner, 0.), fraction)
    return True


def _close_unity_part(work, part, proof):
    """Keep a successful child green when its sibling later fails.

    Capture215313 contained complete 688/688 Shader and 6531/6531 entry
    counters but their logical parts remained pending at99%. A local final
    count alone still proves neither its caller nor the whole Editor process.
    """
    work["partFractions"][part] = 1.
    work.setdefault("partProofs", {})[part] = proof
    work["partCompleted"] = list(dict.fromkeys([*work.get("partCompleted", []), part]))


def _unity_work_overview(plan, owner, failed=False):
    work = plan.get("unityWork", {}).get(owner)
    if not work: return None
    names = ("assets", "method", "result") if work.get("parts") else ("assets", "result")
    complete = owner in plan["completed"]
    completed = names if complete else work.get("completed", [])
    proofs = dict.fromkeys(completed, "complete")
    if work.get("sharedAssets") and "assets" in proofs: proofs["assets"] = "reuse"
    rows = _overview_rows(names, completed, work.get("fractions", {}), None if complete else work.get("active"),
                          proofs, failed or work.get("failed", False))
    for row in rows:
        row["counter"] = work.get("counters", {}).get(row["id"],
            {"done": int(row["closed"]), "total": 1, "unit": "tasks", "phase": "unity-work-stage:" + row["id"]})
        if row["id"] == "method":
            part_names = tuple(UNITY_METHOD_PARTS.get(owner, work["parts"]))
            closed_parts = part_names if row["closed"] else work.get("partCompleted", ())
            proofs = dict.fromkeys(part_names, "complete") if row["closed"] else work.get("partProofs", {})
            active_part = None if row["closed"] or work.get("part") in closed_parts else work.get("part")
            row["parts"] = _overview_rows(part_names, closed_parts,
                work.get("partFractions", {}), active_part, proofs, failed=failed or work.get("failed", False))
            for part in row["parts"]:
                part["counter"] = work.get("counters", {}).get(part["id"],
                    {"done": int(part["closed"]), "total": 1, "unit": "tasks", "phase": "unity-work-stage:" + part["id"]})
    return {"phases": rows, "done": sum(row["closed"] for row in rows), "total": len(rows),
            "active": None if complete else work.get("active"), "percent": 100. if complete else round(100 * _unity_work_fraction(work), 6)}


def _active_work(plan, current):
    if current == "recovery":
        done, total, fraction = _recovery_work(plan.get("recovery", {}))
    elif current in plan.get("preparationScopes", {}):
        scope = plan["preparationScopes"][current]
        done, total = scope["done"], scope["total"]
        fraction = 1. if current in plan["completed"] else plan.get("liveFractions", plan["fractions"]).get(current, 0.)
    elif current == "content-bank" and plan.get("contentPack"):
        work = plan["contentPack"]
        done, total = len(work.get("completed", [])), len(CONTENT_PACK_SHARES)
        fraction = _observed_pass_ratio(work, CONTENT_PACK_SHARES)
    elif current == "package-api" and plan.get("packageApi"):
        work = plan["packageApi"]
        done, total = len(work.get("completed", [])), len(PACKAGE_API_SHARES)
        fraction = _observed_pass_ratio(work, PACKAGE_API_SHARES)
    elif current == "unity-validation" and plan.get("unityValidation"):
        work = plan["unityValidation"]
        counter = work.get("counters", {}).get(work.get("current"), {})
        done, total = counter.get("done", 0), counter.get("total", 1)
        fraction = _observed_pass_ratio(work, UNITY_VALIDATION_SHARES)
    else:
        done, total = (int(current in plan["completed"]), 1)
        fraction = 1. if done else plan.get("liveFractions", plan["fractions"]).get(current, 0.)
    value = {"operation": current, "done": done, "total": total, "unit": "steps",
             "percent": None if fraction is None else round(100 * fraction, 6)}
    unity_work = _unity_work_overview(plan, current)
    if unity_work:
        value["unity"] = {key: unity_work[key] for key in ("done", "total", "active", "percent")}
    preparation = _preparation_overview(plan, current)
    if preparation:
        value.update(done=preparation["done"], total=preparation["total"], remaining=preparation["remaining"],
                     index=preparation["index"], checkpoint=preparation["active"])
        active = next((item for item in preparation["checkpoints"] if item["id"] == preparation["active"]), None)
        if active:
            value["passes"] = {"done": active["passDone"], "total": active["passTotal"],
                               "remaining": active["passTotal"] - active["passDone"], "active": active["passActive"],
                               "known": active["passPlanKnown"]}
    return value


def _build_percent(plan):
    shares = build_shares(plan)
    weighted = sum(weight * (1. if name in plan["completed"] else plan["fractions"].get(name, 0.))
                   for name, weight in shares.items())
    return 100 * weighted / sum(shares.values())


def _observed_pass_ratio(work, shares):
    return sum(share * (1. if name in work.get("completed", []) else work.get("fractions", {}).get(name, 0.))
               for name, share in shares.items())


def _observed_pass_overview(plan, key, shares, status):
    work = plan.get(key)
    if not work: return None
    rows = _overview_rows(tuple(shares), work.get("completed", []), work.get("fractions", {}),
                          work.get("current"), work.get("proofs"), status == "failed", work.get("liveStatus"))
    for row in rows:
        counter = work.get("counters", {}).get(row["id"])
        if counter: row["counter"] = dict(counter)
    return {"passes": rows, "done": sum(row["closed"] for row in rows), "total": len(rows),
            "active": work.get("current"), "percent": round(100 * _observed_pass_ratio(work, shares), 6)}


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
    if row["status"] in ("blocked", "failed", "cancelled", "interrupted") and plan.get("liveStatus") in ("complete", "reuse"):
        # A host preflight or launcher can reject the next job after an actual
        # completed operation. Do not paint that previous success as failed.
        active = None
    operations = _overview_rows(planned_operations(row), plan["completed"], plan.get("liveFractions", plan["fractions"]), active,
                               plan.get("operationProofs"), row["status"] == "failed", plan.get("liveStatus"))
    for item in operations:
        detail = _preparation_overview(plan, item["id"], row["status"] == "failed" and item["id"] == active)
        if detail: item["preparation"] = detail
        compiler = plan.get("unityCompiler", {}).get(item["id"])
        if compiler:
            compiled = item["id"] == "content-bank" and "build" in plan.get("unityWork", {}).get(item["id"], {}).get("partCompleted", ())
            item["compiler"] = dict(compiler, status="complete" if item["closed"] or compiled else "failed"
                                     if row["status"] == "failed" and item["id"] == active else "running" if item["id"] == active else "pending")
        imported = plan.get("unityImports", {}).get(item["id"])
        if imported:
            item["import"] = dict(imported, status="failed" if row["status"] == "failed" and item["id"] == active
                                  else "complete" if item["closed"] or item["id"] == "content-bank" and plan.get("contentPack")
                                  else "running" if item["id"] == active else "pending")
        if item["id"] == "content-bank":
            packed = _observed_pass_overview(plan, "contentPack", CONTENT_PACK_SHARES,
                                             "failed" if row["status"] == "failed" and item["id"] == active else item["status"])
            if packed: item["pack"] = packed
        if item["id"] == "package-api":
            api = _observed_pass_overview(plan, "packageApi", PACKAGE_API_SHARES,
                                          "failed" if row["status"] == "failed" and item["id"] == active else item["status"])
            if api: item["api"] = api
        if item["id"] == "unity-validation":
            checks = _observed_pass_overview(plan, "unityValidation", UNITY_VALIDATION_SHARES,
                "failed" if row["status"] == "failed" and item["id"] == active else item["status"])
            if checks: item["validation"] = checks
        task = plan.get("unityTasks", {}).get(item["id"])
        if task: item["unityTask"] = dict(task, status="complete" if task.get("closed") else "failed" if row["status"] == "failed" and item["id"] == active
                                        else "complete" if item["closed"] else "running" if item["id"] == active else "pending")
        unity_work = _unity_work_overview(plan, item["id"], row["status"] == "failed" and item["id"] == active)
        if unity_work: item["unityWork"] = unity_work
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
    # A reused recovery receipt closes the whole derived project without
    # replaying its fourteen inner boundaries. Do not turn that absent detail
    # into a new 0/14 plan, or invent fourteen individual completion proofs.
    staging_live = any(item["status"] in ("running", "checking", "failed") for item in steps)
    staging_observed = any(item["closed"] or item["percent"] > 0 for item in steps) or staging_live
    if "staging" in work.get("completed", []) and not all(item["closed"] for item in steps) and not staging_live:
        staging_detail = "aggregate"
    else:
        staging_detail = "observed" if staging_observed else "unobserved"
    if staging_detail != "observed": steps = []
    batch = recovery.get("batch", {})
    total = recovery.get("plannedBatches", batch.get("total"))
    done = total if "batches" in work.get("completed", []) else batch.get("done", 0)
    if type(total) is not int or total < 0: total, done = None, None
    return {"schema": 1, "done": sum(item["closed"] for item in operations), "total": len(operations),
            "active": active, "groups": groups, "recovery": {"sections": sections,
            "batches": {"done": done, "total": total}, "staging": steps, "stagingDetail": staging_detail,
            "section": section, "stagingCounter": staging.get("counter") if section == "staging" else None}}


def advance(row, value, operation=None, status=None):
    for key in ("recoverySection", "recoveryBatchIndex", "recoveryBatchTotal", "recoveryNativeIndex", "recoveryNativeTotal", "activeWork", "buildOverview"):
        value.pop(key, None)
    plan = initialize(row); operations = planned_operations(row)
    if (row["id"] == "build" and value["phase"] == "native-memory-check"
            and (status or value.get("operationStatus")) == "failed"):
        # Legacy refusal is a prerequisite, never an asset producer. Keep
        # the high-water/completed frontier, but detach the old live owner.
        # A successful observation inside Player must keep its actual owner.
        plan["liveOperation"] = None
        plan["liveStatus"] = "progress"
        value.pop("reportedOperation", None)
        operation = None
    if value["phase"] in MEMORY_OBSERVATIONS:
        # A compiler can lower concurrency or wait inside its current build.
        # That observation neither reopens a producer nor completes its owner.
        # Keep the real owner and durable high-water through pressure/retries.
        value.update(done=None, total=None, unit=None, percent=None)
        value.pop("reportedOperation", None)
        operation = None
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
    if plan.get("legacyPackageApiImport") and operation == "package-api":
        if status in ("complete", "reuse") and value["phase"] == "operation:package-api":
            # The historical SDK method returned only after its import; that
            # actual successful boundary qualifies both owners.
            plan.pop("legacyPackageApiImport", None)
        else:
            operation = "unity-import"
            if value["phase"] == "operation:package-api": value["phase"] = "operation:unity-import"
    if status is not None: value["operationStatus"] = status
    if value["phase"] == "operation:" + str(operation) and status == "start" and operation in UNITY_METHOD_PARTS:
        previous_owner = plan.get("liveOperation")
        previous_work = plan.get("unityWork", {}).get(previous_owner)
        if (previous_owner, operation) in (("unity-validation", "content-bank"), ("content-bank", "player")) \
                and previous_work and not previous_work.get("failed"):
            # QuestBuild.Build keeps one Editor process while actual operation
            # boundaries move from validation to the content bank to Player.
            # Reuse that process's asset readiness; do not rescan the project or
            # fabricate a new Editor launch for each owner's finite schedule.
            plan.setdefault("unityWork", {})[operation] = {"startedAt": value.get("updatedAt"),
                "active": "method", "completed": ["assets"], "fractions": {},
                "parts": dict(UNITY_METHOD_PARTS[operation]), "partFractions": {},
                "counters": {"assets": dict(previous_work.get("counters", {}).get("assets",
                    {"phase": "unity-work-stage:assets", "done": 1, "total": 1, "unit": "tasks"}))}, "sharedAssets": True}
    unity_work_boundary = value["phase"] == "unity-work-invocation" or value["phase"].startswith("unity-work-stage:")
    if unity_work_boundary or value["phase"] == "unity-work-plan":
        live_owner = plan.get("liveOperation")
        if live_owner not in UNITY_WORK_OWNERS or operation not in (None, live_owner):
            if value is row.get("progress"):
                value.update(phase="operation:" + live_owner if live_owner in operations else "starting",
                             done=None, total=None, percent=None, unit=None, detail=None)
                value.pop("reportedOperation", None)
                return advance(row, value, live_owner, plan.get("liveStatus", "progress"))
            previous = dict(row.get("progress", {})); value.clear(); value.update(previous)
            return value
        operation, measured = live_owner, False
        _observe_unity_work(plan, live_owner, value, status)
    pack_counter = value["phase"] in CONTENT_PACK_SHARES
    api_counter = value["phase"] in PACKAGE_API_SHARES
    validation_counter = value["phase"] in UNITY_VALIDATION_SHARES
    unity_child = value["phase"] in UNITY_CHILD_COUNTERS
    if pack_counter or api_counter or validation_counter:
        live_owner = plan.get("liveOperation")
        owner = "content-bank" if pack_counter else "package-api" if api_counter else "unity-validation"
        if live_owner != owner or operation not in (None, live_owner):
            if value is row.get("progress"):
                value.update(phase="operation:" + live_owner if live_owner in operations else "starting",
                             done=None, total=None, percent=None, unit=None, detail=None)
                value.pop("reportedOperation", None)
                return advance(row, value, live_owner, plan.get("liveStatus", "progress"))
            previous = dict(row.get("progress", {}))
            value.clear(); value.update(previous)
            return value
        operation, measured = live_owner, False
        work = plan.setdefault("contentPack" if pack_counter else "packageApi" if api_counter else "unityValidation", {})
        shares = CONTENT_PACK_SHARES if pack_counter else PACKAGE_API_SHARES if api_counter else UNITY_VALIDATION_SHARES
        # The Unity content-bank task owns the first half, its packer's passes
        # the remaining half. API binding has its own complete three-pass plan.
        # An already measured partial fraction anchors the remaining work
        # rather than moving an existing stage bar backwards.
        work.setdefault("origin", max(.5 if pack_counter else 0., plan["fractions"].get(live_owner, 0.)))
        name = value["phase"]
        work.update(current=name, liveStatus=status or "progress")
        if validation_counter and name == "unity-validation-tasks":
            task = work.get("task", {})
            if task.get("done") != value.get("done") or task.get("detail") != value.get("detail"):
                task = work["task"] = {"done": value.get("done"), "total": value.get("total"),
                    "detail": value.get("detail"), "fractions": {}}
        ratio = _ratio(value)
        if ratio is not None: work.setdefault("fractions", {})[name] = max(work.get("fractions", {}).get(name, 0.), ratio)
        if status in ("complete", "reuse"):
            work["completed"] = list(dict.fromkeys([*work.get("completed", []), name]))
            work.setdefault("proofs", {})[name] = status
        work.setdefault("counters", {})[name] = {key: value.get(key) for key in ("phase", "done", "total", "unit")}
        fraction = min(.99, work["origin"] + (1. - work["origin"]) * _observed_pass_ratio(work, shares))
        if live_owner not in plan.get("unityWork", {}) or pack_counter or api_counter:
            plan.setdefault("liveFractions", {})[live_owner] = fraction
            plan["fractions"][live_owner] = max(plan["fractions"].get(live_owner, 0.), fraction)
    if unity_child or validation_counter:
        live_owner = plan.get("liveOperation")
        if live_owner not in ("mod-banks", "unity-import", "package-api", "unity-validation", "content-bank", "player", "update-code", "update-art") or operation not in (None, live_owner):
            if value is row.get("progress"):
                value.update(phase="operation:" + live_owner if live_owner in operations else "starting",
                             done=None, total=None, percent=None, unit=None, detail=None)
                value.pop("reportedOperation", None)
                return advance(row, value, live_owner, plan.get("liveStatus", "progress"))
            previous = dict(row.get("progress", {})); value.clear(); value.update(previous)
            return value
        operation, measured = live_owner, False
        task = plan.setdefault("unityTasks", {}).setdefault(live_owner, {})
        if task.get("phase") != value["phase"] or status == "start": task["startedAt"] = value.get("updatedAt")
        task.update({key: value.get(key) for key in ("phase", "done", "total", "unit", "detail", "updatedAt")})
        task["closed"] = status in ("complete", "reuse") and _ratio(value) == 1.
        value["unityTask"] = dict(task)
        if unity_child and live_owner == "unity-validation":
            work = plan.get("unityValidation", {})
            current_task = work.get("task", {})
            parts = UNITY_VALIDATION_TASK_PARTS.get(current_task.get("detail"), {})
            ratio = _ratio(value)
            done, total = current_task.get("done"), current_task.get("total")
            if value["phase"] in parts and ratio is not None and type(done) is int and type(total) is int and 0 <= done < total:
                current_task.setdefault("fractions", {})[value["phase"]] = max(current_task.get("fractions", {}).get(value["phase"], 0.), ratio)
                child_fraction = sum(share * current_task["fractions"].get(name, 0.) for name, share in parts.items())
                # A last loop item proves that loop, not its caller's final
                # checks/receipt. The real TaskSequence return owns the boundary.
                step_fraction = (done + min(.99, child_fraction)) / total
                work.setdefault("fractions", {})["unity-validation-tasks"] = max(work["fractions"].get("unity-validation-tasks", 0.), step_fraction)
                fraction = min(.99, work.get("origin", 0.) + (1. - work.get("origin", 0.)) * _observed_pass_ratio(work, UNITY_VALIDATION_SHARES))
                if live_owner not in plan.get("unityWork", {}):
                    plan.setdefault("liveFractions", {})[live_owner] = fraction
                    plan["fractions"][live_owner] = max(plan["fractions"].get(live_owner, 0.), fraction)
    import_counter = value["phase"] in UNITY_IMPORT_OBSERVATIONS
    if import_counter:
        # A child log may still be read after the next SDK/player boundary.
        # Only the actually open Editor owner accepts its observations; no
        # historical import can move the live heading or close an operation.
        live_owner = plan.get("liveOperation")
        allowed = live_owner in ("mod-banks", "unity-import", "package-api", "unity-validation",
                                 "content-bank", "player", "update-code", "update-art")
        if not allowed or operation not in (None, live_owner):
            if value is row.get("progress"):
                # A status read may project an older snapshot whose last
                # child observation already outlived its owner. Recover the
                # current owner's neutral display instead of clearing the
                # same dictionary used as the historical source.
                value.update(phase="operation:" + live_owner if live_owner in operations else "starting",
                             done=None, total=None, percent=None, unit=None, detail=None)
                value.pop("unityImport", None)
                value.pop("reportedOperation", None)
                return advance(row, value, live_owner, plan.get("liveStatus", "progress"))
            previous = dict(row.get("progress", {}))
            value.clear()
            value.update(previous)
            return value
        operation = live_owner
        observed = plan.setdefault("unityImports", {}).setdefault(live_owner, {})
        done, total = value.get("done"), value.get("total")
        if type(done) is int and 0 <= done <= 2 ** 53 - 1 and value.get("unit") == "assets":
            detail = value.get("detail") or ""
            scope = "coverage" if detail.startswith("[asset-coverage]") else "native" if detail.startswith("[unity-native-total]") else None
            if scope is not None and observed.get("scope") != scope:
                observed.update(done=0, total=None, scope=scope)
            done = done if scope == "native" else max(done, observed.get("done", 0))
            observed["done"] = done
            value["done"] = done
            if type(total) is int and done <= total <= 2 ** 53 - 1 and total > 0:
                observed["total"] = total
            elif total is not None or observed.get("scope") is None:
                # Native artifact lines count operations, not unique assets.
                # Unknown totals never borrow a different task's denominator.
                observed["total"] = None
            value["total"] = observed.get("total")
            value["percent"] = None if value["total"] is None else round(100 * done / value["total"], 1)
        observed.update(phase=value["phase"], detail=value.get("detail"), updatedAt=value.get("updatedAt"))
        observed.setdefault("startedAt", value.get("updatedAt"))
        value["unityImport"] = dict(observed)
    compiler_counter = value["phase"] in ("unity-shader-compile", "unity-shader-task") or value["phase"].startswith("unity-shader-task:")
    if compiler_counter:
        # Child log readers can outlive their Unity job. An explicitly foreign
        # bank/compiler cannot move the import or player frontier backwards.
        live_owner = plan.get("liveOperation")
        allowed = live_owner in ("mod-banks", "unity-import", "content-bank", "player", "update-code", "update-art")
        if not allowed or operation not in (None, live_owner):
            if operation is not None: value["reportedOperation"] = operation
            operation, measured = None, False
        else:
            operation = live_owner
            plan.setdefault("unityCompiler", {})[live_owner] = {
                "phase": value["phase"], "done": value.get("done"), "total": value.get("total"),
                "unit": value.get("unit"), "percent": value.get("percent"), "detail": value.get("detail"),
                "scope": "pass" if value["phase"] == "unity-shader-compile" else "task"}
            scope = plan.get("preparationScopes", {}).get(live_owner, {})
            if value["phase"] == "unity-shader-task" and scope.get("open") and scope.get("name") == "mod-resource-banks":
                measured = False
                ratio = _ratio(value)
                if ratio is not None and scope.get("total"):
                    # Geometry owns the first half of this one checkpoint;
                    # the Shader task gets a bounded compiler share, leaving
                    # room for the bank's write/acceptance/publication work.
                    scope["itemFraction"] = max(scope.get("itemFraction", 0.), .5 + .25 * ratio)
                    fraction = (scope["done"] + min(.99, scope["itemFraction"])) / scope["total"]
                    plan.setdefault("liveFractions", {})[live_owner] = min(.99, fraction)
                    plan["fractions"][live_owner] = max(plan["fractions"].get(live_owner, 0.), min(.99, fraction))
    if operation == "unity-validation" and plan.get("unityValidation"):
        # A public Editor task may finish inside one validation call. Its
        # local 100% is not completion of our known remaining task schedule.
        measured = False
    if row["id"] == "build" and value["phase"].startswith("prepare-items:"):
        # Counter producers need no duplicated parent argument. The actual
        # opened checkpoint, not a historical frontier, establishes ownership.
        name = value["phase"].split(":", 1)[1]
        owner = PREPARATION_ITEMS.get(name)
        scope = plan.get("preparationScopes", {}).get(owner, {})
        active_scope = scope.get("open") and scope.get("name") == _item_checkpoint(name)
        if name in ENVIRONMENT_ITEM_SHARES or name in STARTUP_ITEM_CHECKPOINTS:
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
    movie_file_counter = False
    if row["id"] == "build" and value["phase"] == "file-hash":
        scope = plan.get("preparationScopes", {}).get("startup-content", {})
        movie_file_counter = (scope.get("open") and scope.get("name") == "startup-movies"
                              and scope.get("itemCounter", {}).get("name") == "startup-movies-media"
                              and plan.get("liveOperation") == "startup-content" and operation in (None, "startup-content"))
        if movie_file_counter: operation = "startup-content"
    child_boundary = (value["phase"].startswith(("recovery-section:", "staging-section:", "prepare-substage:", "prepare-items:"))
                      or value["phase"] in ("recovery-batches", "recovery-raw-reuse", "recovery-plan", "prepare-project-copy")
                      or movie_file_counter
                      or compiler_counter
                      or import_counter
                      or pack_counter
                      or api_counter
                      or validation_counter
                      or unity_child
                      or unity_work_boundary
                      or value["phase"] == "unity-work-plan"
                      or operation in plan.get("unityWork", {}) and value["phase"].startswith(("unity-", "bee-actions:"))
                      or workflow(row) in UPDATE_PLANS and value.get("childOperation") is not None)
    parent_status = "progress" if child_boundary else status
    preparation_counter = row["id"] == "build" and (value["phase"].startswith(("prepare-substage:", "prepare-items:")) or movie_file_counter)
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
        _observe_preparation(plan, operation, value)
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
    unity_owner = plan.get("liveOperation")
    unity_observation = (value["phase"].startswith(("unity-", "bee-actions:"))
        and unity_owner in plan.get("unityWork", {}) and operation in (None, unity_owner))
    if unity_observation:
        _observe_unity_work(plan, unity_owner, value, status)
        # A logical indivisible task is one pending scheduled operation, never
        # a guessed count of imports or a time-generated percentage.
        if value.get("done") is None and value.get("total") is None:
            value.update(done=0, total=1, unit="tasks", percent=0.)
        if compiler_counter and unity_owner in plan.get("unityCompiler", {}):
            plan["unityCompiler"][unity_owner].update({key: value.get(key) for key in ("done", "total", "unit", "percent")})
        measured = False
    if current in operations and measured and (operation is None or operation == current) and value["percent"] is not None:
        fraction = min(.99, _ratio(value))
        if current == "content-bank" and value["phase"].startswith(("unity-", "bee-actions:")): fraction *= .5
        plan["fractions"][current] = max(plan["fractions"].get(current, 0.), fraction)
    live = plan.get("liveOperation", current)
    if live in operations and measured and (operation is None or operation == live) and value["percent"] is not None:
        fraction = min(.99, _ratio(value))
        if live == "content-bank" and value["phase"].startswith(("unity-", "bee-actions:")): fraction *= .5
        plan.setdefault("liveFractions", {})[live] = fraction
    if row["status"] == "complete":
        plan["completed"] = list(operations)
        plan["current"] = operations[-1]
        current = operations[-1]
        for name in operations:
            if plan.setdefault("operationProofs", {}).get(name) != "reuse": plan["operationProofs"][name] = "complete"
    done = sum(1. if name in plan["completed"] else plan["fractions"].get(name, 0.) for name in operations)
    # Keep small measured byte/file contributions for the bar, even when its
    # concise percentage label rounds them to a few decimal places.
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
