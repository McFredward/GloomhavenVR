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

from catalog import bundle_closure, decode_catalog
from export_identity import (build_tool, identity_remaps, object_index, read_identities,
                             remap_yaml, POINTER)
from recover import (RecoveryError, YAML_EXTENSIONS, audit_asset_references,
                     audit_export_log, repair_managed_plugins, run_export,
                     sha256, stage_input, write_json)


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
    rows, files, output_paths = [], [], {}
    for row in incoming_rows:
        if row.get("skippedCore"):
            if any((obj["collection"], int(obj["pathId"])) not in canonical_objects for obj in row["objects"]):
                raise RecoveryError("Skipped core contains an unknown original object; re-export core coherently.")
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
    for row in rows:
        original = next(item for item in incoming_rows if item["guid"] == row["guid"] and not item.get("skippedCore"))
        source, destination = incoming_project / original["path"], project / row["path"]
        if destination.exists() or destination.with_name(destination.name + ".meta").exists():
            raise RecoveryError("New recovered GUID overlaps an existing generated asset: " + row["guid"])
        destination.parent.mkdir(parents=True, exist_ok=True)
        if source.suffix in YAML_EXTENSIONS:
            text = remap_yaml(source.read_text(encoding="utf-8"), pointers)
            destination.write_text(text, encoding="utf-8")
        else:
            shutil.copy2(source, destination)
        metadata = source.with_name(source.name + ".meta")
        target_meta = destination.with_name(destination.name + ".meta")
        # Compound texture importers can contain external Sprite/Atlas pointers.
        target_meta.write_text(remap_yaml(metadata.read_text(encoding="utf-8"), pointers), encoding="utf-8")
        for path in (destination, target_meta):
            files.append({"path": path.relative_to(project).as_posix(), "sha256": sha256(path), "bytes": path.stat().st_size})
        for obj in row["objects"]:
            canonical_objects[(obj["collection"], int(obj["pathId"]))] = {**obj, "guid": row["guid"], "path": row["path"]}
    return {"exportedCollectionCount": len(rows), "newOriginalObjectCount": sum(len(row["objects"]) for row in rows),
            "remappedOriginalPointerCount": len(pointers), "files": files, "identities": rows}


def run_recovery(game_data, core_project, core_identities, output, workspace, tool_command, selected_groups=None):
    source, core, output, workspace = map(lambda value: Path(value).resolve(), (game_data, core_project, output, workspace))
    if output == source or source in output.parents or output in source.parents:
        raise RecoveryError("Full recovery output must stay outside original game data.")
    plan = catalog_bundle_plan(source)
    workspace.mkdir(parents=True, exist_ok=True)
    write_json(workspace / "bundle-plan.json", plan)
    checkpoint = output / "quest-full-recovery-progress.json"
    if checkpoint.exists():
        progress = json.loads(checkpoint.read_text())
        if progress["catalogSha256"] != plan["catalogSha256"] or progress["coreIdentitySha256"] != sha256(core_identities):
            raise RecoveryError("Full recovery resume inputs changed.")
        for row in progress["files"]:
            path = output / row["path"]
            if not path.is_file() or sha256(path) != row["sha256"]:
                raise RecoveryError("Full recovered checkpoint file changed: " + row["path"])
        identities = progress["identities"]
    else:
        if output.exists():
            raise RecoveryError("Full recovery requires a fresh output or a verified checkpoint.")
        shutil.copytree(core, output)
        repair_managed_plugins(output, source)
        identities = read_identities(core_identities, core)
        files = [{"path": path.relative_to(output).as_posix(), "bytes": path.stat().st_size, "sha256": sha256(path)}
                 for path in sorted(output.rglob("*")) if path.is_file()]
        progress = {"schema": 1, "catalogSha256": plan["catalogSha256"], "coreIdentitySha256": sha256(core_identities),
                    "completedGroups": [], "files": files, "identities": identities, "groups": [], "assetsRecovered": False}
        write_json(checkpoint, progress)
    canonical = object_index(identities)
    stage = workspace / "Input/GH_Data"
    if not stage.exists():
        stage.parent.mkdir(parents=True, exist_ok=True)
        stage_input(source, stage, [])
    settings = json.loads((Path(__file__).parent / "tool-lock.json").read_text())["settings"]
    groups = range(len(plan["groups"])) if selected_groups is None else selected_groups
    for index in groups:
        if index in progress["completedGroups"]:
            continue
        group = plan["groups"][index]
        directory = workspace / ("batch-" + str(index).zfill(3))
        if directory.exists():
            raise RecoveryError("Interrupted batch evidence exists; retain it and choose a new workspace: " + str(directory))
        directory.mkdir()
        selected = stage / "SelectedBundles"
        if selected.exists():
            shutil.rmtree(selected)
        selected.mkdir()
        for row in group["bundles"]:
            path = source / row["path"]
            if sha256(path) != row["sha256"]:
                raise RecoveryError("Original bundle changed during bounded recovery: " + row["path"])
            destination = selected / Path(row["path"]).name
            if destination.exists():
                raise RecoveryError("Distinct original bundles have the same staged filename.")
            shutil.copy2(path, destination)
        previous = {key: os.environ.get(key) for key in ("QUEST_EXPORT_IDENTITIES", "QUEST_EXPORT_BUNDLE_ONLY")}
        identity_path = directory / "identities.jsonl"
        os.environ["QUEST_EXPORT_IDENTITIES"] = str(identity_path)
        os.environ["QUEST_EXPORT_BUNDLE_ONLY"] = "1"
        try:
            project, shaders, scripts = run_export(tool_command, stage, directory / "Export", directory / "export.log",
                                                   directory / "Evidence", settings, require_scene_settings=False)
        finally:
            for key, value in previous.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
        incoming = read_identities(identity_path, project)
        merged = merge_export(output, project, incoming, canonical)
        progress["files"].extend(merged["files"])
        progress["identities"].extend(merged["identities"])
        progress["completedGroups"].append(index)
        progress["groups"].append({"index": index, "input": group, "exportLog": audit_export_log(directory / "export.log"),
                                   "identitySha256": sha256(identity_path), "shaderRecipes": shaders,
                                   "exportedCollectionCount": merged["exportedCollectionCount"],
                                   "newOriginalObjectCount": merged["newOriginalObjectCount"],
                                   "remappedOriginalPointerCount": merged["remappedOriginalPointerCount"]})
        progress["assetsRecovered"] = len(progress["completedGroups"]) == len(plan["groups"])
        write_json(checkpoint, progress)
        print("[Quest full recovery] Batch", index + 1, "of", len(plan["groups"]), "merged:",
              merged["exportedCollectionCount"], "collections; total original objects", len(canonical), flush=True)
    progress["assetReferences"] = audit_asset_references(output)
    write_json(checkpoint, progress)
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
