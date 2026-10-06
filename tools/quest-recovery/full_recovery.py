#!/usr/bin/env python3
"""Recover a player's complete original game locally, without private caches."""
import argparse
import json
import os
from pathlib import Path
import shutil
import sys

from bundle_members import serialized_members
from bundle_recovery import catalog_bundle_plan, run_recovery, write_checkpoint, _hash_file
from export_identity import build_tool, read_identities
import native_evidence
from recover import (RecoveryError, managed_inventory, repair_managed_plugins, resolve_game_data,
                     run_export, sha256, source_inventory, stage_input, write_json, own_attempt, ordinary_path)
from recover import build_progress


def prepare(game_data, workspace, tool_cache, dotnet, output_project=None, *,
            core_project=None, core_identities=None, bundle_workspace=None, metadata_project=None,
            managed_dotnet=None):
    """Return reproducible full recovery inputs for full_assets.stage().

    Only original game files and the pinned open-source tool source are inputs.
    All exports, metadata and checkpoints live in the supplied private workspace.
    Existing completed core/bundle outputs must pass their hash receipts to reuse.
    The .NET 10 SDK runs the pinned exporter; managed_inventory additionally uses
    the builder's existing .NET 8 toolchain for metadata-only assembly inspection.
    """
    source = resolve_game_data(game_data)
    workspace, tool_cache = Path(workspace).resolve(), Path(tool_cache).resolve()
    output = Path(output_project).resolve() if output_project else workspace / "RecoveredProject"
    for target in (workspace, tool_cache, output):
        if target == source or source in target.parents or target in source.parents:
            raise RecoveryError("Full recovery tools/output must stay outside the read-only original game.")
    workspace.mkdir(parents=True, exist_ok=True)
    build_progress.event("recovery-section:source", detail="Qualifying original source inputs", status="start")
    files, fingerprint = source_inventory(source)
    source_receipt = workspace / "original-source.json"
    if source_receipt.exists():
        previous = json.loads(source_receipt.read_text())
        if previous.get("sourceFingerprint") != fingerprint:
            raise RecoveryError("Original game version changed; choose a new full recovery workspace.")
    else:
        write_json(source_receipt, {"schema": 1, "sourceFingerprint": fingerprint, "sourceInventory": files})
    # Announce the actual bounded schedule before the slow core export. The
    # wizard can keep one stable conversion denominator throughout all batches.
    plan = catalog_bundle_plan(source, source_inventory=files)
    build_progress.event("recovery-plan", 0, len(plan["groups"]), "batches", "Full original conversion schedule", status="start")
    build_progress.event("recovery-section:core", detail="Preparing and qualifying the original core export", status="start")
    tool, tool_proof = build_tool(tool_cache, dotnet)
    if (core_project is None) != (core_identities is None):
        raise RecoveryError("Existing core reuse requires both its actual project and native identity evidence.")
    core = Path(core_project).resolve() if core_project else workspace / "CoreExport/ExportedProject"
    core_identities = Path(core_identities).resolve() if core_identities else workspace / "core-identities.jsonl"
    core_receipt = workspace / "core-recovery.json"
    if core_project is not None:
        if not (output / "quest-full-recovery-progress.json").is_file():
            raise RecoveryError("Existing full recovery reuse requires its actual bounded checkpoint.")
        existing = json.loads((output / "quest-full-recovery-progress.json").read_text())
        if existing["coreIdentitySha256"] != sha256(core_identities):
            raise RecoveryError("Existing core does not match full recovery's native object identity receipt.")
    elif core_receipt.exists():
        receipt = json.loads(core_receipt.read_text())
        if receipt["sourceFingerprint"] != fingerprint or receipt["exporterSource"] != tool_proof["source"]:
            raise RecoveryError("Core recovery inputs/exporter changed.")
        if sha256(core_identities) != receipt["identitiesSha256"]:
            raise RecoveryError("Core native object identity evidence changed.")
        if ordinary_path(output / "quest-full-recovery-progress.json").is_file():
            # run_recovery uses the merged checkpoint's identities and actual
            # output, and independently qualifies those retained bytes once.
            # The old core export is unused on this path; rereading its entire
            # inventory only duplicated the later merged-output verification.
            build_progress.event("recovery-core-receipt-verify", 1, 1, "receipts",
                                 "Retaining core identity receipt; merged output qualification follows", status="reuse")
        else:
            counter = build_progress.Counter("recovery-core-receipt-verify", len(receipt["files"]), "files")
            for row in receipt["files"]:
                path = ordinary_path(core / row["path"])
                if not path.is_file() or _hash_file(path, "recovery-core-file-hash") != row["sha256"]:
                    raise RecoveryError("Core recovery output changed: " + row["path"])
                counter.add(1, path.name)
            counter.finish()
    else:
        owner = workspace / "core-attempt.json"
        expected = {"schema": 1, "owner": "Quest original core export", "sourceFingerprint": fingerprint,
                    "exporterSource": tool_proof["source"]}
        if not owner.exists() and (core_identities.exists() or (workspace / "CoreEvidence").exists()):
            raise RecoveryError("Unfinished core evidence has no matching ownership receipt.")
        own_attempt(owner, expected, workspace / "CoreExport")
        # Completed core-recovery.json is handled above. Only this marker-owned
        # unfinished export is retried, preserving all later completed batches.
        for unfinished in (workspace / "CoreExport", workspace / "CoreEvidence"):
            ordinary_path(unfinished)
            if unfinished.exists(): shutil.rmtree(unfinished)
        ordinary_path(core_identities).unlink(missing_ok=True)
        staged = workspace / "CoreInput/GH_Data"
        staged.parent.mkdir(parents=True, exist_ok=True)
        stage_input(source, staged, [])
        capture = native_evidence.environment(workspace / "CoreEvidence")
        previous_env = {name: os.environ.get(name) for name in ("QUEST_EXPORT_IDENTITIES", "QUEST_EXPORT_BUNDLE_ONLY", *capture)}
        os.environ["QUEST_EXPORT_IDENTITIES"] = str(core_identities)
        os.environ.pop("QUEST_EXPORT_BUNDLE_ONLY", None)
        os.environ.update(capture)
        try:
            settings = json.loads(Path(__file__).with_name("tool-lock.json").read_text())["settings"]
            core, _, _ = run_export(tool, staged, workspace / "CoreExport", workspace / "core-export.log",
                                    workspace / "CoreEvidence", settings)
        finally:
            for name, value in previous_env.items():
                if value is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = value
        repair_managed_plugins(core, source)
        read_identities(core_identities, core)
        shutil.copytree(workspace / "CoreEvidence/QuestRecovery", core / "QuestRecovery")
        managed_inventory(source, core / "QuestRecovery/managed-types.json", tool_cache, dotnet=managed_dotnet)
        core_files = [path for path in sorted(core.rglob("*")) if path.is_file()]
        counter = build_progress.Counter("recovery-core-output-hash", len(core_files), "files")
        records = []
        for path in core_files:
            records.append({"path": path.relative_to(core).as_posix(), "bytes": path.stat().st_size, "sha256": _hash_file(path, "recovery-core-file-hash")})
            counter.add(1, path.name)
        receipt = {"schema": 1, "sourceFingerprint": fingerprint, "exporterSource": tool_proof["source"],
                   "identitiesSha256": sha256(core_identities), "files": records}
        write_json(core_receipt, receipt)
        counter.finish()
    # The actual catalog plan hashes required original bundles once. Final CAB
    # ownership indexing uses this same plan instead of rereading every bundle
    # merely to recreate an unchanged schedule; the final source guard remains.
    progress = run_recovery(source, core, core_identities, output,
                            Path(bundle_workspace).resolve() if bundle_workspace else workspace / "BundleRecovery", tool,
                            bundle_plan=plan)
    if metadata_project is not None:
        metadata = Path(metadata_project).resolve() / "QuestRecovery"
        if not metadata.is_dir():
            raise RecoveryError("Existing native metadata project has no QuestRecovery inventory.")
        # Older coherent exports have the same actual owned DLL bytes. Preserve
        # their verified metadata without executing any original assemblies.
        managed = Path(metadata_project).resolve() / "Assets/Plugins/GH.Runtime.dll"
        if sha256(managed) != sha256(source / "Managed/GH.Runtime.dll"):
            raise RecoveryError("Existing managed metadata comes from a different original game.")
        known_files = {row["path"]: row for row in progress["files"]}
        for path in sorted(metadata.rglob("*")):
            if not path.is_file():
                continue
            relative = "QuestRecovery/" + path.relative_to(metadata).as_posix()
            digest = sha256(path)
            previous = known_files.get(relative)
            if previous is not None and previous["sha256"] != digest:
                raise RecoveryError("Existing metadata would overwrite different witnessed recovery evidence: " + relative)
            if previous is not None:
                continue  # Qualified output already owns these exact metadata bytes.
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
            if previous is None:
                progress["files"].append({"path": relative, "bytes": path.stat().st_size, "sha256": digest})
    build_progress.event("recovery-section:source-recheck", detail="Rechecking original inputs after conversion", status="start")
    _, after_fingerprint = source_inventory(source)
    if after_fingerprint != fingerprint:
        raise RecoveryError("Original game files changed during full recovery.")
    progress["sourceFingerprint"], progress["sourceInventory"] = fingerprint, files
    build_progress.event("recovery-section:checkpoint", detail="Publishing the qualified full recovery checkpoint", status="start")
    write_checkpoint(output / "quest-full-recovery-progress.json", progress)
    owners = {}
    total = sum(len(group["bundles"]) for group in plan["groups"])
    build_progress.event("recovery-section:cab-index", detail="Indexing original physical bundle ownership", status="start")
    counter = build_progress.Counter("recovery-cab-bundle-index", total, "bundles")
    for group in plan["groups"]:
        for bundle in group["bundles"]:
            for node in serialized_members(source / bundle["path"]):
                member = node["name"]
                if member in owners and owners[member] != bundle["path"]:
                    raise RecoveryError("Original CAB occurs in two distinct physical bundles: " + member)
                owners[member] = bundle["path"]
            counter.add(1, Path(bundle["path"]).name)
    owner_path = workspace / "original-cab-bundles.json"
    write_json(owner_path, owners)
    counter.finish()
    result = {"schema": 1, "recoveryProject": str(output), "coreProject": str(core),
              "managedTypes": str(output / "QuestRecovery/managed-types.json"), "cabBundles": str(owner_path),
              "sourceFingerprint": fingerprint, "originalInputFileCount": len(files),
              "originalInputBytes": sum(row["bytes"] for row in files),
              "fullOriginalCatalogRecovered": progress["assetsRecovered"],
              "privateB614CacheRequired": False, "androidPlayerBuilt": False,
              "faithfulGraphicsVerified": False, "playableCampaignVerified": False}
    write_json(workspace / "full-recovery.json", result)
    # The merged checkpoint is independently hashed and complete. Temporary
    # per-batch project exports are no longer required for reproducible resume;
    # retain native input hashes, identity rows, recipes and exporter logs.
    batches = Path(bundle_workspace).resolve() if bundle_workspace else workspace / "BundleRecovery"
    deleted = []
    for index in progress["completedGroups"]:
        exported = batches / ("batch-" + str(index).zfill(3)) / "Export"
        if exported.is_dir():
            size = sum(path.stat().st_size for path in exported.rglob("*") if path.is_file())
            shutil.rmtree(exported)
            deleted.append({"path": str(exported), "bytes": size})
    write_json(workspace / "obsolete-export-cleanup.json", {"schema": 1,
               "verifiedRecoveryReceipt": str(workspace / "full-recovery.json"), "removed": deleted,
               "originalInputsAndIdentityEvidenceRetained": True})
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", required=True)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--tool-cache", required=True)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--managed-dotnet", help="Separate .NET SDK/runtime for the managed metadata reader.")
    parser.add_argument("--output-project")
    parser.add_argument("--core-project", help="Optional existing instrumented core paired with the bounded checkpoint.")
    parser.add_argument("--core-identities")
    parser.add_argument("--bundle-workspace")
    parser.add_argument("--metadata-project", help="Optional previous owned-game managed/shader metadata project.")
    args = parser.parse_args(argv)
    try:
        with build_progress.silence_diagnostics():
            result = prepare(args.game_data, args.workspace, args.tool_cache, args.dotnet, args.output_project,
                             core_project=args.core_project, core_identities=args.core_identities,
                             bundle_workspace=args.bundle_workspace, metadata_project=args.metadata_project,
                             managed_dotnet=args.managed_dotnet)
        print(json.dumps(result, sort_keys=True))
        return 0
    except (RecoveryError, OSError, ValueError) as error:
        print("[Quest full recovery] FAILED:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
