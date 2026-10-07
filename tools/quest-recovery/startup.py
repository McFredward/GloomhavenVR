#!/usr/bin/env python3
"""Stage an audited original Bootstrap/Intro/main-menu closure, never a full port.

Consumes a genuine owned-game recovery receipt. Output is private build input;
the report records unresolved references, shader approximations and native calls.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys

from catalog import bundle_closure, decode_catalog
from recover import (GUID_PATTERN, RecoveryError, SCRIPT_POINTER, YAML_EXTENSIONS,
                     audit_asset_references, audit_script_bindings, script_file_id, sha256, write_json)
import tmp_shaders

SCENES = ["Assets/Scenes/Bootstrap.unity", "Assets/Scenes/Intro.unity",
          "Assets/Scenes/Release/Gloomhaven_unified.unity", "Assets/Scenes/Release/MainMenu.unity"]
LABELS = ["always_loaded_base", "always_loaded_standalone", "always_loaded_base_high"]
SDK_ASSEMBLIES = ["UnityEngine.UI.dll", "Unity.InputSystem.dll"]
DISABLED_SDK_ASSEMBLIES = SDK_ASSEMBLIES + ["Unity.Addressables.dll", "Unity.ResourceManager.dll", "Unity.ScriptableBuildPipeline.dll"]
REFERENCE = re.compile(r"guid:\s*([0-9a-f]{32})")
SPRITE_KEY = re.compile(r"m_RenderDataKey:\s*\n\s*([0-9a-f]{32}):\s*(-?\d+)")


def safe_path(root, relative):
    if not isinstance(relative, str) or "\\" in relative or Path(relative).is_absolute():
        raise RecoveryError("Non-relative recovery path: " + str(relative))
    path = root / relative
    if ".." in Path(relative).parts or path.is_symlink() or root.resolve() not in path.resolve().parents:
        raise RecoveryError("Recovery path escapes its root: " + relative)
    return path


def builder_fingerprint(rows):
    converted = [{"path": x["path"], "size": x["bytes"], "sha256": x["sha256"]} for x in rows]
    converted.sort(key=lambda row: row["path"])
    return hashlib.sha256(json.dumps({"files": converted}, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()


def asset_index(project):
    index, paths = {}, {}
    for metadata in sorted((project / "Assets").rglob("*.meta")):
        asset = metadata.with_name(metadata.name[:-5])
        if not asset.is_file():
            continue
        relative = asset.relative_to(project).as_posix()
        safe_path(project, relative)
        match = GUID_PATTERN.search(metadata.read_text(encoding="utf-8", errors="strict"))
        if not match:
            raise RecoveryError("Missing original asset GUID: " + relative)
        if match[1] in index:
            raise RecoveryError("Ambiguous recovered asset GUID: " + match[1])
        index[match[1]] = relative
        paths[relative] = match[1]
    return index, paths


def script_index(project, types):
    mappings, plugin_guids = {}, {}
    for name, assembly in types.items():
        path = project / "Assets/Plugins" / name
        if not path.exists():
            continue
        match = GUID_PATTERN.search(path.with_name(name + ".meta").read_text())
        if not match:
            raise RecoveryError("Missing original DLL metadata: " + name)
        plugin_guids[name] = match[1]
        for info in assembly["types"]:
            if info["nested"]:
                continue
            full_name = (info["namespace"] + "." if info["namespace"] else "") + info["name"]
            mappings[(match[1], str(script_file_id(info["namespace"], info["name"])))] = (name, full_name)
    return mappings, plugin_guids


def native_asset_type(project, relative, scripts):
    suffix = Path(relative).suffix.lower()
    types = {".prefab": "UnityEngine.GameObject", ".mat": "UnityEngine.Material", ".shader": "UnityEngine.Shader",
             ".png": "UnityEngine.Texture2D", ".jpg": "UnityEngine.Texture2D", ".anim": "UnityEngine.AnimationClip",
             ".controller": "UnityEngine.RuntimeAnimatorController"}
    if suffix in types:
        return types[suffix]
    if suffix != ".asset":
        return None
    text = (project / relative).read_text(errors="replace")
    header = re.search(r"^--- !u!(\d+)", text, re.M)
    if not header:
        return None
    class_id = int(header[1])
    classes = {21: "UnityEngine.Material", 43: "UnityEngine.Mesh", 48: "UnityEngine.Shader", 213: "UnityEngine.Sprite",
               28: "UnityEngine.Texture2D", 74: "UnityEngine.AnimationClip", 319: "UnityEngine.SpriteAtlas"}
    if class_id in classes:
        return classes[class_id]
    if class_id == 114:
        pointer = SCRIPT_POINTER.search(text)
        if pointer and (pointer[2], pointer[1]) in scripts:
            return scripts[(pointer[2], pointer[1])][1]
    return None


def startup_associations(catalog_path, project, paths, index, scripts):
    decoded = decode_catalog(json.loads(catalog_path.read_text(encoding="utf-8-sig")))
    aliases = collections.defaultdict(list)
    selected = set()
    for key, entries in zip(decoded["keys"], decoded["buckets"]):
        if not isinstance(key, str):
            continue
        for entry in entries:
            aliases[entry].append(key)
        if key in LABELS:
            selected.update(entries)
    present_labels = {key for key in decoded["keys"] if key in LABELS}
    if present_labels != set(LABELS):
        raise RecoveryError("Original startup Addressables labels are incomplete.")
    rows, missing = [], []
    case_paths = collections.defaultdict(list)
    for path in paths:
        case_paths[path.casefold()].append(path)
    for entry in sorted(selected):
        location = decoded["locations"][entry]
        original = location["internalId"].replace("\\", "/")
        if not original.startswith("Assets/"):
            raise RecoveryError("Startup asset is not an original asset container: " + original)
        safe_path(project, original)
        class_name = location["resourceType"]["m_ClassName"]
        keys = sorted(set(aliases[entry]))
        # Source catalog buckets keep the original group labels (misc_gui,
        # etc.) as well as the three initial preload labels. Preserve all of
        # these aliases; dynamic original loaders also request group labels.
        labels = sorted(key for key in keys if not key.startswith("Assets/") and not re.fullmatch(r"[0-9a-f]{32}", key))
        key_guids = [key for key in keys if re.fullmatch(r"[0-9a-f]{32}", key)]
        target, file_id, proof = None, None, None
        # Atlas sprites become standalone Sprite assets. The original render key
        # proves identity even when the original container extension is PNG/JPG.
        sprite_path = str(Path(original).with_suffix(".asset"))
        if class_name == "UnityEngine.Sprite" and sprite_path in paths:
            text = (project / sprite_path).read_text()
            key = SPRITE_KEY.search(text)
            if key and key[1] in key_guids and native_asset_type(project, sprite_path, scripts) == class_name:
                target, file_id, proof = sprite_path, int(key[2]), "original-sprite-render-data-key"
        if target is None:
            candidates = [original] if original in paths else case_paths.get(original.casefold(), [])
            if len(candidates) == 1 and native_asset_type(project, candidates[0], scripts) == class_name:
                target = candidates[0]
                proof = ("exact-original-container-path-and-object-type" if target == original else
                         "unique-case-insensitive-original-Windows-container-path-and-object-type")
        # An FBX container becomes a prefab plus a referenced native Mesh. Only
        # the exact original container stem is permitted, never a name search.
        prefab = str(Path(original).with_suffix(".prefab"))
        if target is None and original.lower().endswith(".fbx") and prefab in paths:
            text = (project / prefab).read_text()
            if class_name == "UnityEngine.GameObject":
                target, proof = prefab, "exact-original-container-exported-as-prefab"
            elif class_name == "UnityEngine.Mesh":
                meshes = set(re.findall(r"m_Mesh:\s*\{fileID:\s*4300000,\s*guid:\s*([0-9a-f]{32})", text))
                if len(meshes) == 1 and next(iter(meshes)) in index:
                    candidate = index[next(iter(meshes))]
                    if native_asset_type(project, candidate, scripts) == class_name:
                        target, file_id, proof = candidate, 4300000, "unique-mesh-pointer-in-exact-exported-container-prefab"
        # Converted JPG textures use PNG without changing container directory.
        png_path = str(Path(original).with_suffix(".png"))
        if target is None and class_name == "UnityEngine.Texture2D" and original.lower().endswith(".jpg") and png_path in paths:
            sprite = project / sprite_path
            if sprite.is_file():
                key = SPRITE_KEY.search(sprite.read_text())
                texture = re.search(r"texture:\s*\{fileID:\s*2800000,\s*guid:\s*([0-9a-f]{32})", sprite.read_text())
                if key and key[1] in key_guids and texture and index.get(texture[1]) == png_path:
                    target, file_id, proof = png_path, 2800000, "original-render-key-sprite-texture-pointer"
        object_type = native_asset_type(project, original, scripts) if original in paths else None
        value_type = target is None and object_type is not None and object_type != class_name
        row = {"entryIndex": entry, "originalAssetPath": original, "assetPath": target,
               "recoveredGuid": paths[target] if target else None, "recoveredFileId": file_id,
               "keys": [key for key in keys if key not in labels], "labels": labels,
               "resourceTypeName": class_name + ", " + location["resourceType"]["m_AssemblyName"],
               "provider": location["provider"], "associationProof": proof,
               "requiredOriginalBundles": bundle_closure(decoded, [entry]),
               "status": "associated" if target else "serialized-value-location-excluded" if value_type else "unresolved",
               "initialObjectLoadEligible": target is not None}
        rows.append(row)
        if not target and not value_type:
            missing.append(row)
    return {"schema": 1, "labels": LABELS, "catalogSha256": sha256(catalog_path), "entries": rows,
            "requiredOriginalBundles": bundle_closure(decoded, selected), "androidCatalogBuilt": False,
            "unresolvedEntryCount": len(missing)}, missing


def closure(project, roots, index):
    pending, selected = list(roots), set()
    missing = collections.defaultdict(set)
    while pending:
        relative = pending.pop()
        if relative in selected:
            continue
        path = safe_path(project, relative)
        if not path.is_file():
            raise RecoveryError("Required original asset is not recovered: " + relative)
        selected.add(relative)
        if path.suffix not in YAML_EXTENSIONS:
            continue
        for guid in REFERENCE.findall(path.read_text(encoding="utf-8", errors="replace")):
            if guid.startswith("0000000000000000"):
                continue
            if guid not in index:
                missing[guid].add(relative)
            else:
                pending.append(index[guid])
    return selected, {guid: sorted(paths) for guid, paths in sorted(missing.items())}


def extend_assembly_closure(project, selected, types):
    pending = [Path(path).name for path in selected if path.endswith(".dll")]
    seen = set()
    while pending:
        name = pending.pop()
        if name in seen:
            continue
        seen.add(name)
        for reference in types[name].get("references", []):
            filename = reference + ".dll"
            path = project / "Assets/Plugins" / filename
            if path.is_file() and filename not in seen:
                selected.add(path.relative_to(project).as_posix())
                pending.append(filename)
    return selected


def binding_manifest(project, selected, scripts, plugin_guids):
    bindings, asset_paths = {}, []
    disabled = {plugin_guids[name] for name in DISABLED_SDK_ASSEMBLIES if name in plugin_guids}
    for relative in sorted(selected):
        if Path(relative).suffix not in YAML_EXTENSIONS:
            continue
        text = (project / relative).read_text(errors="replace")
        asset_paths.append(relative)
        for file_id, guid in SCRIPT_POINTER.findall(text):
            if guid not in disabled:
                continue
            identity = scripts.get((guid, file_id))
            if not identity or identity[0] not in SDK_ASSEMBLIES:
                raise RecoveryError("Disabled SDK has an unsupported original serialized script: " + relative)
            assembly, full_name = identity
            bindings[(guid, file_id)] = {"assemblyName": assembly[:-4], "fullName": full_name,
                                         "oldGuid": guid, "oldFileId": int(file_id)}
    return {"schema": 1, "bindings": sorted(bindings.values(), key=lambda x: x["fullName"]),
            "assetPaths": asset_paths, "disabledPluginGuids": sorted(disabled)}


def verify_copy(source, destination, expected=None):
    if source.is_symlink() or not source.is_file():
        raise RecoveryError("Immutable source is not a regular file: " + str(source))
    actual = sha256(source)
    if expected is not None and actual != expected:
        raise RecoveryError("Recovered output differs from recovery receipt: " + str(source))
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    if sha256(destination) != actual:
        raise RecoveryError("Staged copy verification failed: " + str(destination))


def stage(source, game_data, output, tmp_archive):
    source, game_data, output = Path(source).resolve(), Path(game_data).resolve(), Path(output).resolve()
    if output == source or output in source.parents or source in output.parents or output.exists():
        raise RecoveryError("Startup staging requires a fresh output outside the recovered source project.")
    if output == game_data or output in game_data.parents or game_data in output.parents:
        raise RecoveryError("Startup staging must remain outside the read-only owned game.")
    receipt_path = source / "quest-recovery-report.json"
    receipt = json.loads(receipt_path.read_text())
    if receipt.get("schema") != 1:
        raise RecoveryError("Unsupported original recovery receipt.")
    encoded_source = json.dumps(receipt["sourceInventory"], sort_keys=True, separators=(",", ":")).encode()
    if hashlib.sha256(encoded_source).hexdigest() != receipt["sourceHash"]:
        raise RecoveryError("Recovery source inventory fingerprint is inconsistent.")
    source_inventory = {row["path"]: row for row in receipt["sourceInventory"]}
    output_inventory = {row["path"]: row for row in receipt["outputInventory"]}
    if len(source_inventory) != len(receipt["sourceInventory"]) or len(output_inventory) != len(receipt["outputInventory"]):
        raise RecoveryError("Recovery receipt contains duplicate inventory paths.")
    for relative in output_inventory:
        safe_path(source, relative)
    for relative in source_inventory:
        safe_path(game_data, relative)
    catalog_path = game_data / "StreamingAssets/aa/catalog.json"
    catalog_row = source_inventory.get("StreamingAssets/aa/catalog.json")
    if not catalog_row or sha256(catalog_path) != catalog_row["sha256"]:
        raise RecoveryError("Original catalog differs from coherent recovered game inputs.")
    for relative in ("QuestRecovery/managed-types.json", "QuestRecovery/original-script-identities.json"):
        if relative not in output_inventory or sha256(source / relative) != output_inventory[relative]["sha256"]:
            raise RecoveryError("Original managed metadata differs from recovery receipt: " + relative)
    types = json.loads((source / "QuestRecovery/managed-types.json").read_text())
    identities = json.loads((source / "QuestRecovery/original-script-identities.json").read_text())
    index, paths = asset_index(source)
    scripts, plugin_guids = script_index(source, types)
    associations, missing_addresses = startup_associations(catalog_path, source, paths, index, scripts)
    selected_bundle_paths = {row["path"] if isinstance(row, dict) else row for row in receipt["selectedBundles"]}
    missing_bundles = sorted(set(associations["requiredOriginalBundles"]) - selected_bundle_paths)
    if missing_bundles:
        raise RecoveryError("Coherent export omits required startup bundles: " + ", ".join(missing_bundles))
    resource_roots = [relative for relative in paths if relative.startswith("Assets/Resources/")
                      and not relative.startswith("Assets/Resources/srdebugger/")]
    roots = SCENES + resource_roots + [row["assetPath"] for row in associations["entries"] if row["assetPath"]]
    selected, missing_refs = closure(source, roots, index)
    extend_assembly_closure(source, selected, types)
    bindings = binding_manifest(source, selected, scripts, plugin_guids)
    output.mkdir(parents=True)
    originals = []
    for relative in sorted(selected):
        for item in (relative, relative + ".meta"):
            expected = output_inventory.get(item)
            if expected is None:
                raise RecoveryError("Selected recovered asset is absent from recovery receipt: " + item)
            verify_copy(safe_path(source, item), safe_path(output, item), expected["sha256"])
            originals.append({"path": item, "sha256": expected["sha256"]})
        if relative.endswith(".dll"):
            row = source_inventory.get("Managed/" + Path(relative).name)
            if not row or sha256(output / relative) != row["sha256"] or sha256(game_data / row["path"]) != row["sha256"]:
                raise RecoveryError("Original managed assembly bytes are not retained: " + relative)
    for relative in sorted(output_inventory):
        if relative.startswith("ProjectSettings/"):
            verify_copy(source / relative, output / relative, output_inventory[relative]["sha256"])
    # Filesystem rules are loaded by original code, not reachable through GUIDs.
    for relative, row in sorted(source_inventory.items()):
        if relative.startswith("StreamingAssets/Rulebase/") or relative in ("StreamingAssets/GloomData.dat",):
            verify_copy(safe_path(game_data, relative), output / "Assets" / relative, row["sha256"])
    restored = tmp_shaders.restore(output, source, selected, tmp_archive)
    restored_paths = {row["assetPath"] for row in restored}
    shader_rows = []
    shader_recipes = []
    for relative, row in sorted(output_inventory.items()):
        if relative.startswith("QuestRecovery/ShaderRecipes/"):
            if sha256(source / relative) != row["sha256"]:
                raise RecoveryError("Original shader recipe differs from recovery receipt: " + relative)
            recipe = json.loads((source / relative).read_text())
            parsed = recipe["parsedForm"]
            # Do not import desktop GPU programs into Unity as a giant TextAsset.
            # Retain source property/state recipes and their raw recipe hash.
            state_recipe = {"m_Name": parsed["m_Name"], "m_PropInfo": parsed["m_PropInfo"],
                            "m_SubShaders": [{"m_Passes": [{"m_State": p["m_State"]} for p in s["m_Passes"]]}
                                             for s in parsed["m_SubShaders"]]}
            shader_recipes.append({"recipePath": relative, "recipeSha256": row["sha256"],
                                   "compiledPlatforms": recipe["compiledPlatforms"], "parsedForm": state_recipe})
    for relative in sorted(selected):
        if not relative.endswith(".shader"):
            continue
        match = re.search(r'Shader\s+"([^"]+)"', (source / relative).read_text(encoding="utf-8-sig"))
        shader_rows.append({"path": relative, "name": match[1] if match else None,
                            "status": "official-compatible-TMP-source" if relative in restored_paths else "unresolved-original-dummy",
                            "originalShaderFidelity": False})
    script_audit = audit_script_bindings(output, types, identities)
    layout_names = []
    for message in receipt["audit"]["exportLog"]["errors"]:
        match = re.search(r"because script (.+?) layout", message)
        if match:
            layout_names.append(match[1])
    failed_scripts = {(guid, fid): name for (guid, fid), (_, name) in scripts.items() if name in layout_names}
    layout_in_closure = []
    for relative in sorted(selected):
        if Path(relative).suffix in YAML_EXTENSIONS:
            for fid, guid in SCRIPT_POINTER.findall((source / relative).read_text(errors="replace")):
                if (guid, fid) in failed_scripts:
                    layout_in_closure.append({"path": relative, "type": failed_scripts[(guid, fid)]})
    native = [{"assembly": Path(relative).name, "imports": types[Path(relative).name].get("nativeImports", [])}
              for relative in sorted(selected) if relative.endswith(".dll") and types[Path(relative).name].get("nativeImports")]
    (output / "Assets/QuestOriginalStartup").mkdir(parents=True)
    write_json(output / "Assets/QuestOriginalStartup/script-bindings.json", bindings)
    write_json(output / "Assets/QuestOriginalStartup/startup-addressables.json", associations)
    write_json(output / "Assets/QuestOriginalStartup/shader-recipes.json", {"schema": 1, "recipes": shader_recipes})
    original_scenes = receipt["audit"]["originalBuildScenes"]
    closure_ready = not missing_refs and not missing_addresses and not layout_in_closure and script_audit["unresolvedCount"] == 0
    report = {"schema": 1, "target": "startup", "sourceFingerprint": receipt["sourceHash"],
              "sourceBuilderFingerprint": builder_fingerprint(receipt["sourceInventory"]),
              "recoveryReceiptSha256": sha256(receipt_path), "selectedScenes": SCENES,
              "originalBuildScenes": original_scenes, "fullGameReady": False,
              "readiness": {"closureReady": closure_ready, "originalSceneClosureStaged": True,
                            "unityImportVerified": False, "androidPlayerBuilt": False,
                            "faithfulGraphicsVerified": False, "playableCampaignVerified": False},
              "missingReferences": missing_refs, "unresolvedAddressables": missing_addresses,
              "serializedFailuresInClosure": layout_in_closure, "originalExportLayoutFailureCount": receipt["audit"]["exportLog"]["monoBehaviourLayoutFailureCount"],
              "managedScriptBindings": script_audit, "nativePluginDependencies": native,
              "startupAddressablesManifest": "Assets/QuestOriginalStartup/startup-addressables.json",
              "scriptBindingsManifest": "Assets/QuestOriginalStartup/script-bindings.json",
              "shaders": shader_rows, "officialTmpRestorations": restored, "originalDerivedFiles": originals,
              "limits": ["Only Bootstrap, Intro, unified startup and MainMenu are selected; no playable campaign target.",
                         "Desktop Addressables URLs are not shipped; a genuine Android catalog must be built from the association manifest.",
                         "Unresolved custom shaders retain placeholders; material appearance is not established.",
                         "Native desktop calls require explicit startup adapters; no Windows native plugins are copied.",
                         "SRDebugger resource prefabs are excluded, including an original missing ScrollRectPatch script."]}
    report["files"] = [{"path": path.relative_to(output).as_posix(), "sha256": sha256(path), "size": path.stat().st_size}
                       for path in sorted(output.rglob("*")) if path.is_file()]
    write_json(output / "quest-startup-report.json", report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-project", required=True)
    parser.add_argument("--game-data", required=True)
    parser.add_argument("--output-project", required=True)
    parser.add_argument("--tmp-source-archive", required=True)
    args = parser.parse_args()
    try:
        report = stage(args.source_project, args.game_data, args.output_project, args.tmp_source_archive)
        print("[Quest startup] Staged", len(report["files"]), "immutable input files;")
        print("[Quest startup] Closure ready:", report["readiness"]["closureReady"],
              "; unresolved Addressables:", len(report["unresolvedAddressables"]),
              "; missing GUIDs:", len(report["missingReferences"]))
        # Closure readiness is separate from successful diagnostic staging. The
        # builder must read the report and must never promote this to full game.
        return 0
    except (RecoveryError, OSError, ValueError, KeyError) as error:
        print("[Quest startup] FAILED:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
