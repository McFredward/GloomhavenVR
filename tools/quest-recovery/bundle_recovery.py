#!/usr/bin/env python3
"""Bounded, original-object-proven recovery of the complete local asset catalog."""
import argparse
import collections
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys
import time
import uuid

from catalog import bundle_closure, decode_catalog
from export_identity import (build_tool, identity_remaps, object_index, read_identities,
                             remap_yaml, POINTER)
import native_evidence
from recover import (RecoveryError, YAML_EXTENSIONS, audit_asset_references,
                     audit_export_log, repair_managed_plugins, run_export,
                     sha256, stage_input, write_json, is_unity_yaml, ordinary_path, own_attempt, build_progress)


MUTABLE_NATIVE_INDICES = ("QuestRecovery/native-redirect-identities.jsonl", "QuestRecovery/NativeRecipes/index.jsonl")
NONEXPORTABLE_REDIRECTS = ("AssetRipper.Export.UnityProjects.RedirectExportCollection",
                         "AssetRipper.Export.UnityProjects.SingleRedirectExportCollection")


def exportable_identities(rows, evidence):
    """Adopt old false skipped-core rows only with their exact native engine witness.

    Before the Exportable guard was added inside ShouldExport, the exporter
    called it first to capture redirects and then labelled non-exportable
    collections as skipped core assets. The normal core export has no asset
    entry for those pointers. Their GUID/fileID/class/source identity must
    instead agree with the independently captured engine redirect evidence;
    neither a class name nor an unknown object permits a generic exemption.
    """
    redirects = None; result = []; count = 0
    for row in rows:
        if not row.get("skippedCore") or row.get("exportCollection") not in NONEXPORTABLE_REDIRECTS:
            result.append(row); continue
        if not row.get("objects"):
            raise RecoveryError("Skipped non-exportable redirect has no original object evidence.")
        path = ordinary_path(Path(evidence) / "QuestRecovery/native-redirect-identities.jsonl")
        if redirects is None:
            from native_targets import engine_redirects
            redirects = engine_redirects(path) if path.is_file() else {}
        for obj in row["objects"]:
            key = obj["collection"].casefold(), int(obj["pathId"])
            witness = redirects.get(key)
            if (witness is None or witness["exportCollection"] != row["exportCollection"]
                    or witness["guid"] != row["guid"] or int(witness["fileId"]) != int(obj["fileId"])
                    or int(witness["classId"]) != int(obj["classId"])
                    or not isinstance(obj.get("className"), str) or witness.get("className") != obj["className"]):
                raise RecoveryError("Skipped non-exportable redirect has no exact native engine witness: "
                                    + repr((obj["collection"][:160], obj["pathId"], obj.get("className"))))
            count += 1
    if count:
        print("[Quest full recovery] Verified", count,
              "legacy non-exportable original engine redirects; native pointer evidence retained.", flush=True)
    return result


def _hash_file(path, phase="recovery-checkpoint-file-hash"):
    size = path.stat().st_size
    counter = build_progress.Counter(phase, size, "bytes", path.name) if size >= 8 * 1048576 else None
    digest = sha256(path, progress=counter.add if counter else None)
    if counter: counter.finish()
    return digest


def _inventory(root, phase, byte_phase):
    paths = [path for path in sorted(root.rglob("*")) if path.is_file()]
    counter = build_progress.Counter(phase, len(paths), "files")
    records = []
    for path in paths:
        records.append({"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size,
                        "sha256": _hash_file(path, byte_phase)})
        counter.add(1, path.name)
    counter.finish()
    return records


def write_checkpoint(path, value):
    """Stream real checkpoint records without a second whole-JSON string in RAM.

    Large file/identity arrays previously serialized silently after the native
    merge. Schema/data remain unchanged; only whitespace differs. As with
    write_json(), the old checkpoint survives any interrupted write, and the
    merge journal is removed only after published output hashes verify.
    """
    path = ordinary_path(path); path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    total = sum(len(item) if isinstance(item, list) else 1 for item in value.values())
    counter = build_progress.Counter("recovery-checkpoint-write", total, "records", path.name)
    started = time.monotonic()
    print("[Quest full recovery] Writing checkpoint:", path.name, ";", total, "records.", flush=True)
    try:
        with temporary.open("w", encoding="utf-8", newline="\n") as stream:
            stream.write("{")
            for key_index, key in enumerate(sorted(value)):
                if key_index: stream.write(",")
                stream.write(json.dumps(key) + ":")
                item = value[key]
                if isinstance(item, list):
                    stream.write("[")
                    for index, row in enumerate(item):
                        if index: stream.write(",")
                        stream.write(json.dumps(row, sort_keys=True, separators=(",", ":")))
                        counter.add(1, "Checkpoint " + key)
                    stream.write("]")
                else:
                    stream.write(json.dumps(item, sort_keys=True, separators=(",", ":")))
                    counter.add(1, "Checkpoint " + key)
            stream.write("}\n"); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
        counter.finish()
    finally:
        temporary.unlink(missing_ok=True)
    print("[Quest full recovery] Checkpoint published:", path.name, ";", path.stat().st_size,
          "bytes;", round(time.monotonic() - started, 3), "seconds.", flush=True)


def verified_records(root, records, *, excluded=()):
    started = time.monotonic()
    print("[Quest full recovery] Verifying checkpoint:", len(records), "files;",
          sum(row["bytes"] for row in records if row["path"] not in excluded), "bytes.", flush=True)
    counter = build_progress.Counter("recovery-checkpoint-verify", len(records), "files")
    for row in records:
        relative = Path(row["path"])
        if relative.is_absolute() or ".." in relative.parts or "\\" in row["path"]:
            raise RecoveryError("Recovery checkpoint has an unsafe relative path.")
        path = ordinary_path(root / relative)
        if row["path"] in excluded:
            counter.add(1, "Excluded mutable index: " + path.name); continue
        if not path.is_file() or path.stat().st_size != row["bytes"] or _hash_file(path) != row["sha256"]:
            raise RecoveryError("Full recovered checkpoint file changed: " + row["path"])
        counter.add(1, path.name)
    counter.finish()
    print("[Quest full recovery] Checkpoint verified:", len(records), "files;",
          round(time.monotonic() - started, 3), "seconds.", flush=True)


def recover_merge(output, workspace):
    """A write-ahead journal restores only the exact unfinished batch changes."""
    journal = ordinary_path(workspace / "merge-pending.json")
    if not journal.exists(): return
    value = json.loads(journal.read_text()); checkpoint = output / "quest-full-recovery-progress.json"
    if value.get("schema") != 1 or value.get("output") != str(output): raise RecoveryError("Merge journal belongs to another output.")
    progress = json.loads(checkpoint.read_text())
    if sha256(checkpoint) != value["checkpointSha256"]:
        if value["group"] not in progress["completedGroups"]: raise RecoveryError("Pending merge checkpoint changed unexpectedly.")
        verified_records(output, progress["files"])
    else:
        prior = {row["path"] for row in progress["files"]}
        verified_records(output, progress["files"], excluded=MUTABLE_NATIVE_INDICES)
        for row in value["backups"]:
            if row["path"] not in MUTABLE_NATIVE_INDICES: raise RecoveryError("Merge backup is not a known mutable index.")
            saved = ordinary_path(workspace / row["backup"])
            if saved.parent != workspace or not saved.is_file() or sha256(saved) != row["sha256"]:
                raise RecoveryError("Retained merge index backup changed.")
        for relative in value["addedPaths"]:
            path = Path(relative)
            if path.is_absolute() or ".." in path.parts or "\\" in relative or not relative.startswith(("Assets/QuestRecoveredBundles/", "QuestRecovery/")):
                raise RecoveryError("Merge journal names an unsupported output.")
        for relative in value["addedPaths"]:
            if relative not in prior: ordinary_path(output / relative).unlink(missing_ok=True)
        for row in value["backups"]:
            target = ordinary_path(output / row["path"])
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(workspace / row["backup"], target)
        verified_records(output, progress["files"])
    journal.unlink()
    for row in value["backups"]: ordinary_path(workspace / row["backup"]).unlink(missing_ok=True)


def begin_merge(output, workspace, index, incoming, evidence, canonical):
    _, duplicates, _ = identity_remaps(incoming, canonical)
    paths = []
    for row in incoming:
        if row.get("skippedCore") or row["path"] in duplicates: continue
        relative = "Assets/QuestRecoveredBundles/" + row["guid"] + "/" + Path(row["path"]).name
        paths += [relative, relative + ".meta"]
    for filename in MUTABLE_NATIVE_INDICES:
        source = evidence / filename
        if not source.is_file(): continue
        paths.append(filename)
        if filename.endswith("NativeRecipes/index.jsonl"):
            for row in native_evidence._read_index(source):
                yaml = row["yamlPath"]
                if (not isinstance(yaml, str) or Path(yaml).name != yaml or Path(yaml).suffix != ".yaml"
                        or any(character in yaml for character in ("/", "\\", ":"))):
                    raise RecoveryError("Pending native recipe has an unsafe filename.")
                paths.append("QuestRecovery/NativeRecipes/" + yaml)
    backups = []
    for slot, relative in enumerate(MUTABLE_NATIVE_INDICES):
        path = ordinary_path(output / relative)
        if path.is_file():
            saved = ordinary_path(workspace / ("merge-index-" + str(slot) + ".backup")); shutil.copyfile(path, saved)
            backups.append({"path": relative, "backup": saved.name, "sha256": sha256(saved)})
    write_json(workspace / "merge-pending.json", {"schema": 1, "output": str(output), "group": index,
               "checkpointSha256": sha256(output / "quest-full-recovery-progress.json"), "addedPaths": sorted(set(paths)), "backups": backups})


def catalog_bundle_plan(game_data, byte_limit=768 * 1024 * 1024):
    """Select actual catalog dependencies, never a guessed bundle filename set."""
    root = Path(game_data).resolve()
    catalog = root / "StreamingAssets/aa/catalog.json"
    decoded = decode_catalog(json.loads(catalog.read_text(encoding="utf-8-sig")))
    assets = [row["index"] for row in decoded["locations"]
              if row["internalId"].replace("\\", "/").startswith(("Assets/", "Packages/"))]
    bundles = bundle_closure(decoded, assets)
    rows = []
    for relative in bundles:
        path = (root / relative).resolve()
        if root not in path.parents or not path.is_file():
            raise RecoveryError("Required catalog bundle is absent or unsafe: " + relative)
        with path.open("rb") as stream:
            if not stream.read(8).startswith((b"UnityFS\0", b"UnityWeb", b"UnityRaw")):
                raise RecoveryError("Required catalog dependency is not a Unity bundle: " + relative)
        rows.append({"path": relative, "bytes": path.stat().st_size, "sha256": sha256(path)})
    # Largest-first packing bounds individual compressed inputs and avoids a
    # high-texture group accidentally concentrating all large bundles.
    groups = []
    for row in sorted(rows, key=lambda item: (-item["bytes"], item["path"])):
        group = next((group for group in groups if group["bytes"] + row["bytes"] <= byte_limit), None)
        if group is None:
            group = {"bytes": 0, "bundles": []}
            groups.append(group)
        group["bundles"].append(row)
        group["bytes"] += row["bytes"]
    for group in groups:
        group["bundles"].sort(key=lambda item: item["path"])
    return {"schema": 1, "catalogSha256": sha256(catalog), "assetLocationCount": len(assets),
            "requiredBundleCount": len(rows), "requiredBundleBytes": sum(row["bytes"] for row in rows),
            "compressedBatchByteLimit": byte_limit, "groups": groups,
            "readiness": {"assetsRecovered": False, "androidAssetsBuilt": False}}


def merge_export(project, incoming_project, incoming_rows, canonical_objects):
    """Merge actual object identities and rewrite every external YAML pointer.

    New original objects receive their observed exporter GUID. Path collisions
    cannot overwrite core content: bundle files occupy a private GUID directory.
    Known objects remain owned by their previous actual exported collection.
    """
    project, incoming_project = Path(project).resolve(), Path(incoming_project).resolve()
    pointers, duplicates, incoming = identity_remaps(incoming_rows, canonical_objects)
    rows, files, output_paths, originals = [], [], {}, {}
    for row in incoming_rows:
        if not row.get("skippedCore"): originals.setdefault(row["guid"], row)
        if row.get("skippedCore"):
            missing = [obj for obj in row["objects"] if (obj["collection"], int(obj["pathId"])) not in canonical_objects]
            if missing:
                sample = [(obj["collection"][:160], obj["pathId"], str(obj.get("className", ""))[:80]) for obj in missing[:3]]
                raise RecoveryError("Skipped core contains an unknown original object; re-export core coherently. "
                                    + "Missing count=" + str(len(missing)) + "; exportCollection="
                                    + str(row.get("exportCollection", "unknown"))[:240] + "; sample=" + repr(sample))
            continue
        if row["path"] in duplicates:
            # All original members must already be mapped. A partly known
            # compound export cannot be discarded without losing subobjects.
            unknown = [(obj["collection"], obj["pathId"]) for obj in row["objects"]
                       if (obj["collection"], int(obj["pathId"])) not in canonical_objects]
            if unknown:
                raise RecoveryError("Partly duplicated compound export requires explicit importer conversion: " + row["path"])
            continue
        relative = "Assets/QuestRecoveredBundles/" + row["guid"] + "/" + Path(row["path"]).name
        output_paths[row["path"]] = relative
        rows.append({**row, "path": relative})
    counter = build_progress.Counter("recovery-original-collection-merge", len(rows), "collections")
    for row in rows:
        original = originals[row["guid"]]
        source, destination = incoming_project / original["path"], project / row["path"]
        if destination.exists() or destination.with_name(destination.name + ".meta").exists():
            raise RecoveryError("New recovered GUID overlaps an existing generated asset: " + row["guid"])
        destination.parent.mkdir(parents=True, exist_ok=True)
        if is_unity_yaml(source):
            text = remap_yaml(source.read_text(encoding="utf-8"), pointers)
            destination.write_text(text, encoding="utf-8")
        else:
            shutil.copy2(source, destination)
        metadata = source.with_name(source.name + ".meta")
        target_meta = destination.with_name(destination.name + ".meta")
        # Compound texture importers can contain external Sprite/Atlas pointers.
        target_meta.write_text(remap_yaml(metadata.read_text(encoding="utf-8"), pointers), encoding="utf-8")
        for path in (destination, target_meta):
            files.append({"path": path.relative_to(project).as_posix(), "sha256": _hash_file(path), "bytes": path.stat().st_size})
        for obj in row["objects"]:
            canonical_objects[(obj["collection"], int(obj["pathId"]))] = {**obj, "guid": row["guid"], "path": row["path"]}
        counter.add(1, source.name)
    counter.finish()
    return {"exportedCollectionCount": len(rows), "newOriginalObjectCount": sum(len(row["objects"]) for row in rows),
            "remappedOriginalPointerCount": len(pointers), "files": files, "identities": rows}


def run_recovery(game_data, core_project, core_identities, output, workspace, tool_command, selected_groups=None):
    for path in (output, workspace): ordinary_path(path)
    source, core, output, workspace = map(lambda value: Path(value).resolve(), (game_data, core_project, output, workspace))
    if output == source or source in output.parents or output in source.parents or output == core or core in output.parents or output in core.parents:
        raise RecoveryError("Full recovery output must stay outside original game data.")
    plan = catalog_bundle_plan(source)
    workspace.mkdir(parents=True, exist_ok=True)
    write_json(workspace / "bundle-plan.json", plan)
    checkpoint = output / "quest-full-recovery-progress.json"
    owner = workspace / "recovery-owner.json"
    expected = {"schema": 1, "owner": "Quest bounded original recovery", "output": str(output),
                "catalogSha256": plan["catalogSha256"], "coreIdentitySha256": sha256(core_identities)}
    if not owner.exists() and checkpoint.is_file():
        previous = json.loads(checkpoint.read_text())
        if previous["catalogSha256"] != expected["catalogSha256"] or previous["coreIdentitySha256"] != expected["coreIdentitySha256"]:
            raise RecoveryError("Full recovery resume inputs changed.")
        verified_records(output, previous["files"])
        write_json(owner, expected)  # adopt only a complete verified legacy checkpoint
    own_attempt(owner, expected, output)
    recover_merge(output, workspace)
    if checkpoint.exists():
        progress = json.loads(checkpoint.read_text())
        if progress["catalogSha256"] != plan["catalogSha256"] or progress["coreIdentitySha256"] != sha256(core_identities):
            raise RecoveryError("Full recovery resume inputs changed.")
        verified_records(output, progress["files"])
        identities = progress["identities"]
    else:
        # An interrupted initial core copy has the durable matching owner above.
        if output.exists(): shutil.rmtree(output)
        shutil.copytree(core, output)
        repair_managed_plugins(output, source)
        identities = read_identities(core_identities, core)
        files = _inventory(output, "recovery-core-copy-hash", "recovery-checkpoint-file-hash")
        progress = {"schema": 1, "catalogSha256": plan["catalogSha256"], "coreIdentitySha256": sha256(core_identities),
                    "completedGroups": [], "files": files, "identities": identities, "groups": [], "assetsRecovered": False}
        write_checkpoint(checkpoint, progress)
    canonical = object_index(identities)
    stage = workspace / "Input/GH_Data"
    stage.parent.mkdir(parents=True, exist_ok=True)
    stage_input(source, stage, [])
    settings = json.loads((Path(__file__).parent / "tool-lock.json").read_text())["settings"]
    groups = list(range(len(plan["groups"])) if selected_groups is None else selected_groups)
    measured_groups = list(dict.fromkeys(groups))
    group_counter = build_progress.Counter("recovery-batches", len(measured_groups), "batches")
    group_counter.update(sum(index in progress["completedGroups"] for index in measured_groups), "Verified retained recovery batches", force=True)
    for index in groups:
        if index in progress["completedGroups"]:
            continue
        group = plan["groups"][index]
        build_progress.event("recovery-batch", detail="Preparing original batch " + str(index + 1) + " of " + str(len(plan["groups"])), status="start")
        directory = workspace / ("batch-" + str(index).zfill(3))
        batch_owner = workspace / (directory.name + ".owner.json")
        tool_files = [{"name": Path(argument).name, "sha256": sha256(argument)} for argument in tool_command if Path(argument).is_file()]
        own_attempt(batch_owner, {"schema": 1, "owner": "Quest original bundle batch", "group": group,
                    "coreIdentitySha256": expected["coreIdentitySha256"], "toolFiles": tool_files}, directory)
        export_receipt = directory / "export-complete.json"
        completed_export = json.loads(export_receipt.read_text()) if export_receipt.is_file() else None
        if completed_export:
            if completed_export.get("schema") != 1: raise RecoveryError("Batch export receipt is invalid.")
            verified_records(directory, completed_export["files"])
            project = ordinary_path(directory / completed_export["project"])
            if directory not in project.parents: raise RecoveryError("Batch export project escaped its owner.")
            shaders = completed_export["shaderRecipes"]
        else:
            # Only the currently unfinished bounded export is restarted. Earlier
            # groups and an already verified export waiting to merge are reused.
            if directory.exists(): shutil.rmtree(directory)
            directory.mkdir()
        selected = stage / "SelectedBundles"
        if selected.exists():
            shutil.rmtree(selected)
        selected.mkdir()
        bundle_counter = build_progress.Counter("recovery-batch-bundle-copy", len(group["bundles"]), "bundles")
        for row in group["bundles"]:
            path = source / row["path"]
            if sha256(path) != row["sha256"]:
                raise RecoveryError("Original bundle changed during bounded recovery: " + row["path"])
            destination = selected / Path(row["path"]).name
            if destination.exists():
                raise RecoveryError("Distinct original bundles have the same staged filename.")
            shutil.copy2(path, destination)
            bundle_counter.add(1, path.name)
        bundle_counter.finish()
        capture = native_evidence.environment(directory / "Evidence")
        previous = {key: os.environ.get(key) for key in ("QUEST_EXPORT_IDENTITIES", "QUEST_EXPORT_BUNDLE_ONLY", *capture)}
        identity_path = directory / "identities.jsonl"
        os.environ["QUEST_EXPORT_IDENTITIES"] = str(identity_path)
        os.environ["QUEST_EXPORT_BUNDLE_ONLY"] = "1"
        os.environ.update(capture)
        try:
            if not completed_export:
                project, shaders, scripts = run_export(tool_command, stage, directory / "Export", directory / "export.log",
                                                       directory / "Evidence", settings, require_scene_settings=False)
                write_json(export_receipt, {"schema": 1, "project": project.relative_to(directory).as_posix(), "shaderRecipes": shaders,
                           "files": _inventory(directory, "recovery-export-receipt-hash", "recovery-export-file-hash")})
        finally:
            for key, value in previous.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
        incoming = read_identities(identity_path, project)
        incoming = exportable_identities(incoming, directory / "Evidence")
        verified_records(output, progress["files"])
        begin_merge(output, workspace, index, incoming, directory / "Evidence", canonical)
        merged = merge_export(output, project, incoming, canonical)
        progress["files"].extend(merged["files"])
        # Native field recipes and engine redirects are stable original object
        # evidence, even when a batch drops the corresponding export asset.
        additions = native_evidence.merge(output, directory / "Evidence")
        existing_files = {row["path"]: row for row in progress["files"]}
        for row in additions:
            existing_files[row["path"]] = row
        progress["files"] = list(existing_files.values())
        progress["identities"].extend(merged["identities"])
        progress["completedGroups"].append(index)
        progress["groups"].append({"index": index, "input": group, "exportLog": audit_export_log(directory / "export.log"),
                                   "identitySha256": sha256(identity_path), "shaderRecipes": shaders,
                                   "exportedCollectionCount": merged["exportedCollectionCount"],
                                   "newOriginalObjectCount": merged["newOriginalObjectCount"],
                                   "remappedOriginalPointerCount": merged["remappedOriginalPointerCount"]})
        progress["assetsRecovered"] = len(progress["completedGroups"]) == len(plan["groups"])
        write_checkpoint(checkpoint, progress)
        recover_merge(output, workspace)  # committed checkpoint cleans journal/backups
        group_counter.add(1, "Original batch " + str(index + 1) + " merged and checkpoint committed")
        print("[Quest full recovery] Batch", index + 1, "of", len(plan["groups"]), "merged:",
              merged["exportedCollectionCount"], "collections; total original objects", len(canonical), flush=True)
    group_counter.finish()
    build_progress.event("recovery-asset-references", detail="Auditing recovered original asset references", status="start")
    progress["assetReferences"] = audit_asset_references(output)
    write_checkpoint(checkpoint, progress)
    build_progress.event("recovery-asset-references", 1, 1, "audits", "Recovered asset references audited", status="complete")
    return progress


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", required=True)
    parser.add_argument("--core-project", required=True)
    parser.add_argument("--core-identities", required=True)
    parser.add_argument("--output-project", required=True)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--tool-cache", required=True)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--group", action="append", type=int)
    args = parser.parse_args(argv)
    try:
        command, _ = build_tool(args.tool_cache, args.dotnet)
        run_recovery(args.game_data, args.core_project, args.core_identities, args.output_project,
                     args.workspace, command, args.group)
        return 0
    except (RecoveryError, OSError, ValueError) as error:
        print("[Quest full recovery] FAILED:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
