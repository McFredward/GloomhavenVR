#!/usr/bin/env python3
"""Restore unresolved exported pointers from exact original native fields.

Each replacement joins the owning exported local object to its original native
CAB/pathID, reads that object's original serialized type tree, and follows the
same field/array path to an original PPtr. Its actual dependency CAB and pathID
then identify the recovered target. Display names and fallback class IDs never
select a target. Only the pointer text changes; all other bytes remain intact.
"""
import argparse
import collections
import hashlib
import gc
import json
from pathlib import Path
import re
import sys
import struct
import shutil

from export_identity import object_index
from recover import RecoveryError, sha256, write_json, build_progress

MISSING_GUID = "0000000deadbeef15deadf00d0000000"
# Unity's Windows exports retain CRLF; a header match must never consume its
# line ending, or YAML node offsets no longer point into the original bytes.
DOCUMENT = re.compile(r"^--- !u!(\d+) &(-?\d+)(?:[ \t]+stripped)?[ \t]*(?=\r?$)", re.M)


def _read_unity_yaml(text, reader):
    """Remove Unity directives/tags without moving any original text offset."""
    import yaml
    clean = re.sub(r"^%[^\r\n]*", lambda match: " " * len(match[0]), text, flags=re.M)
    clean = DOCUMENT.sub(lambda match: "---" + " " * (len(match[0]) - 3), clean)
    loader = getattr(yaml, "CSafeLoader", yaml.SafeLoader)
    try:
        return reader(clean, Loader=loader)
    except yaml.scanner.ScannerError as error:
        # Original null managed-reference registries contain a compact empty
        # type map accepted by SafeLoader but rejected by libyaml. Admit only
        # that witnessed syntax; malformed unrelated documents still fail.
        empty_types = list(re.finditer(r"(?m)^[ \t]+type:[ \t]*(\{class:, ns:, asm:\})[ \t]*(?=\r?$)", clean))
        index = error.problem_mark.index if error.problem_mark is not None else -1
        if (loader is yaml.SafeLoader or error.problem != "found unexpected ':'" or
                not any(match.start(1) <= index < match.end(1) for match in empty_types)):
            raise
        return reader(clean, Loader=yaml.SafeLoader)


def compose_unity_yaml(text):
    import yaml
    return _read_unity_yaml(text, yaml.compose)


def native_pointers(value, path=()):
    """Retain exact native field/array paths, including serialized pair lists."""
    if isinstance(value, dict):
        if set(value) in ({"m_FileID", "m_PathID"}, {"m_FileID", "m_PathID", "m_TargetClassID"}):
            yield path, (int(value["m_FileID"]), int(value["m_PathID"]))
        else:
            for key, child in value.items():
                yield from native_pointers(child, path + (key,))
    elif isinstance(value, (list, tuple)):
        pair_keys = [child[0] for child in value if isinstance(child, tuple) and len(child) == 2 and isinstance(child[0], str)]
        dictionary = len(pair_keys) == len(value) and len(set(pair_keys)) == len(pair_keys)
        for index, child in enumerate(value):
            if isinstance(child, tuple) and len(child) == 2 and isinstance(child[0], str):
                yield from native_pointers(child[1], path + (index, child[0]))
                # Unity's serialized map can be exported either as pair arrays
                # or as a YAML property dictionary. The original unique key is
                # part of the native field path, never an asset-name search.
                if dictionary:
                    yield from native_pointers(child[1], path + (child[0],))
            else:
                yield from native_pointers(child, path + (index,))


def yaml_missing(text):
    """Return exact owner/localID/field coordinates without reserializing YAML."""
    from yaml.nodes import MappingNode, ScalarNode, SequenceNode
    headers = list(DOCUMENT.finditer(text))
    for index, header in enumerate(headers):
        end = headers[index + 1].start() if index + 1 < len(headers) else len(text)
        block = text[header.start():end]
        if MISSING_GUID not in block:
            continue
        node = compose_unity_yaml(block)
        if not isinstance(node, MappingNode) or len(node.value) != 1:
            raise RecoveryError("Original Unity YAML document lost its native class mapping.")
        def walk(current, path=()):
            if isinstance(current, MappingNode):
                values = {key.value: value for key, value in current.value if isinstance(key, ScalarNode)}
                if "guid" in values and values["guid"].value == MISSING_GUID:
                    if set(values) != {"guid", "fileID", "type"} or not current.flow_style:
                        raise RecoveryError("Unknown unresolved pointer serialization.")
                    yield {"classId": int(header[1]), "localFileId": int(header[2]), "field": path,
                           "start": header.start() + current.start_mark.index,
                           "end": header.start() + current.end_mark.index,
                           "fallbackFileId": int(values["fileID"].value), "fallbackType": int(values["type"].value)}
                else:
                    for key, child in current.value:
                        if not isinstance(key, ScalarNode):
                            raise RecoveryError("Unknown original YAML field key.")
                        yield from walk(child, path + (key.value,))
            elif isinstance(current, SequenceNode):
                for offset, child in enumerate(current.value):
                    yield from walk(child, path + (offset,))
        yield from walk(node.value[0][1])


def native_target(collection, pointer):
    file_id, path_id = pointer
    if path_id == 0:
        raise RecoveryError("Exported non-null missing pointer maps to an original null field.")
    if file_id == 0:
        return collection.name.casefold(), path_id
    if not 1 <= file_id <= len(collection.externals):
        raise RecoveryError("Original native PPtr dependency index is invalid.")
    name = collection.externals[file_id - 1].path.replace("\\", "/").rsplit("/", 1)[-1]
    return name.casefold(), path_id


def exported_type(project, target):
    text = (project / (target["path"] + ".meta")).read_text()
    importer = re.findall(r"^([A-Za-z]\w*Importer):", text, re.M)
    if len(importer) != 1:
        raise RecoveryError("Recovered target has no unique actual Unity importer.")
    return 2 if importer[0] in ("NativeFormatImporter", "DefaultImporter") and Path(target["path"]).suffix != ".prefab" else 3


def load_native(unitypy, path):
    """Close the source descriptor immediately; retain the serialized filename."""
    environment = unitypy.Environment()
    environment.load_file(Path(path).read_bytes(), name=Path(path).name)
    return environment


def native_recipe_fields(recipe, original):
    """Read pinned original-assembly field evidence for a stripped core tree."""
    import yaml
    path = Path(recipe["yamlPath"])
    if recipe["classId"] != original.type.value or sha256(path) != recipe["yamlSha256"]:
        raise RecoveryError("Original native managed field recipe is not its captured type/hash.")
    text = path.read_bytes().decode("utf-8")
    headers = list(DOCUMENT.finditer(text))
    if len(headers) != 1 or int(headers[0][1]) != original.type.value:
        raise RecoveryError("Native field recipe lacks its unique original object type.")
    fields = _read_unity_yaml(text, yaml.load)
    if not isinstance(fields, dict) or len(fields) != 1:
        raise RecoveryError("Native managed field recipe lost its native root.")
    pointers = dict(native_pointers(next(iter(fields.values()))))
    raw = original.get_raw_data()
    if original.type.value != 114 or len(raw) < 32:
        raise RecoveryError("Unsupported stripped native managed field owner.")
    endian = original.reader.endian
    for name, offset in (("m_GameObject", 0), ("m_Script", 16)):
        if pointers.get((name,)) != struct.unpack_from(endian + "iq", raw, offset):
            raise RecoveryError("Original managed field recipe disagrees with native owner/script header.")
    return pointers


def prepare(project, game_data, identities, cab_bundles, output, *, unitypy=None, limit=None,
            redirects=None, atlas_recipes=None, native_recipes=None, only_paths=None):
    """Create a private pointer-only overlay and exhaustive native proof report."""
    if unitypy is None:
        import UnityPy as unitypy
    project, game_data, output = [Path(path).resolve() for path in (project, game_data, output)]
    if output.exists() or any(root == output or root in output.parents or output in root.parents for root in (project, game_data)):
        raise RecoveryError("Native pointer repair requires fresh private output outside original/staged inputs.")
    rows = json.loads(Path(identities).read_text()) if isinstance(identities, (str, Path)) else identities
    rows = rows.get("identities", rows) if isinstance(rows, dict) else rows
    objects = object_index(rows)
    by_local = {(obj["path"], int(obj["fileId"])): obj for obj in objects.values()}
    owners = json.loads(Path(cab_bundles).read_text()) if isinstance(cab_bundles, (str, Path)) else cab_bundles
    owners = {key.casefold(): value for key, value in owners.items()}
    from native_targets import engine_redirects, script_target, atlas_target
    if redirects is not None:
        for key, target in engine_redirects(redirects).items():
            if key in objects:
                # A physical export of the same native texture/sprite remains
                # authoritative. Engine redirects fill absent targets only.
                if objects[key]["classId"] != target["classId"]:
                    raise RecoveryError("Native engine redirect conflicts with original object type.")
            else:
                objects[key] = target
    recipes = {} if atlas_recipes is None else {
        (row["collection"].casefold(), int(row["pathId"])): row for row in atlas_recipes}
    field_recipes = {} if native_recipes is None else {
        (row["collection"].casefold(), int(row["pathId"])): row for row in native_recipes}
    managed_types = json.loads((project / "QuestRecovery/managed-types.json").read_text())
    target_environments, additional_targets = {}, []
    from recover import audit_asset_references
    bad_paths = audit_asset_references(project)["missing"].get(MISSING_GUID, [])
    if only_paths is not None:
        requested = set(only_paths)
        if not requested <= set(bad_paths):
            raise RecoveryError("Requested native pointer subset has no source missing-reference evidence.")
        bad_paths = [path for path in bad_paths if path in requested]
    groups = collections.defaultdict(lambda: collections.defaultdict(list))
    texts, source_hashes, expected = {}, {}, 0
    for relative in sorted(bad_paths):
        # The Windows 075121 failure was a real-byte hash checked against
        # newline-normalized read_text(). Decode exact UTF-8 bytes and splice
        # only PPtr tokens; locale/newline translation must not alter originals.
        before_bytes = (project / relative).read_bytes()
        text = before_bytes.decode("utf-8")
        pointers = list(yaml_missing(text))
        if len(pointers) != text.count(MISSING_GUID):
            raise RecoveryError("Original missing-pointer field parser lost a reference: " + relative)
        for pointer in pointers:
            obj = by_local.get((relative, pointer["localFileId"]))
            if obj is None or obj["classId"] != pointer["classId"]:
                raise RecoveryError("Unresolved pointer owner has no exact captured native identity: " + relative)
            container = owners.get(obj["collection"], obj["collection"])
            groups[container][(obj["collection"], obj["pathId"])].append({**pointer, "path": relative})
        texts[relative] = text
        source_hashes[relative] = hashlib.sha256(before_bytes).hexdigest()
        expected += len(pointers)
    output.mkdir(parents=True)
    replacements, proofs, errors = collections.defaultdict(list), [], []
    selected = sorted(groups)
    if limit is not None:
        selected = selected[:limit]
    counter = build_progress.Counter("recovery-pointer-containers", len(selected), "containers", "Original native pointer sources")
    for container_index, relative_container in enumerate(selected):
        wanted = groups[relative_container]
        source = game_data / relative_container
        environment = load_native(unitypy, source)
        container_sha = sha256(source)
        found = set()
        for original in environment.objects:
            key = (original.assets_file.name.casefold(), int(original.path_id))
            if key not in wanted:
                continue
            found.add(key)
            try:
                try:
                    fields = dict(native_pointers(original.read_typetree()))
                    field_proof = "original-serialized-native-type-tree"
                except (ValueError, KeyError) as error:
                    if key not in field_recipes:
                        raise error
                    fields = native_recipe_fields(field_recipes[key], original)
                    field_proof = "pinned-exporter-original-assembly-parsed-native-field-recipe"
                raw_hash = hashlib.sha256(original.get_raw_data()).hexdigest()
                for pointer in wanted[key]:
                    field = tuple(pointer["field"])
                    if field not in fields:
                        raise RecoveryError("Native original field does not match exported pointer path: " + repr(field))
                    target_key = native_target(original.assets_file, fields[field])
                    target = objects.get(target_key)
                    fallback_id = pointer["fallbackFileId"]
                    fallback_class = fallback_id // 100000 if fallback_id % 100000 == 0 else fallback_id
                    if target is None and (fallback_class == 115 or target_key in recipes):
                        target_source = owners.get(target_key[0], target_key[0])
                        if target_source == relative_container:
                            target_environment = environment
                        else:
                            if target_source not in target_environments:
                                target_environments[target_source] = load_native(unitypy, game_data / target_source)
                            target_environment = target_environments[target_source]
                        candidates = [item for item in target_environment.objects
                                      if (item.assets_file.name.casefold(), int(item.path_id)) == target_key]
                        if len(candidates) != 1:
                            raise RecoveryError("Missing target has no unique actual original native object: " + repr(target_key))
                        target_original = candidates[0]
                        if target_original.type.value == 115:
                            target = script_target(project, game_data, target_original, managed_types)
                        elif target_key in recipes:
                            recipe = recipes[target_key]
                            yaml_path = Path(recipe["yamlPath"])
                            if sha256(yaml_path) != recipe["yamlSha256"]:
                                raise RecoveryError("Pinned exporter native atlas YAML changed.")
                            target = atlas_target(project, target_original, yaml_path.read_bytes().decode("utf-8"), objects, output)
                        if target is not None:
                            objects[target_key] = target
                            additional_targets.append(target)
                    if target is None:
                        raise RecoveryError("Actual original pointer target was not recovered: " + repr(target_key))
                    # Unity keeps high native class IDs verbatim. The exporter
                    # multiplies only conventional small class IDs by 100000.
                    fallback_class = fallback_id // 100000 if fallback_id % 100000 == 0 else fallback_id
                    # The original field can be a native base type (Texture,
                    # Object, Transform, RuntimeAnimatorController). Its lossy
                    # fallback class cannot reject an actual derived target.
                    # Source CAB/pathID and captured target class are the proof.
                    target_type = target.get("nativePointerType")
                    if target_type is None:
                        target_type = exported_type(project, target)
                    restored = "{fileID: " + str(target["fileId"]) + ", guid: " + target["guid"] + ", type: " + str(target_type) + "}"
                    replacements[pointer["path"]].append((pointer["start"], pointer["end"], restored))
                    proofs.append({"assetPath": pointer["path"], "ownerCollection": key[0], "ownerPathId": key[1],
                                   "ownerLocalFileId": pointer["localFileId"], "field": list(field),
                                   "originalFieldProof": field_proof,
                                   "originalOwnerBytesSha256": raw_hash, "originalPointer": list(fields[field]),
                                   "originalSourceContainer": relative_container, "originalSourceContainerSha256": container_sha,
                                   "targetCollection": target_key[0], "targetPathId": target_key[1],
                                   "targetGuid": target["guid"], "targetFileId": target["fileId"],
                                   "targetClassId": target["classId"], "targetAssetPath": target["path"], "targetType": target_type})
            except (RecoveryError, ValueError, KeyError, TypeError, AttributeError) as error:
                errors.append({"ownerCollection": key[0], "ownerPathId": key[1], "assetPath": wanted[key][0]["path"],
                               "pointerCount": len(wanted[key]), "error": str(error)})
        if found != wanted.keys():
            raise RecoveryError("Native pointer owners missing from actual source CAB: " + repr(wanted.keys() - found))
        write_json(output / "progress.json", {"schema": 1, "containersComplete": container_index + 1,
                   "containerCount": len(selected), "restoredPointerCount": len(proofs), "blockedOwnerCount": len(errors),
                   "blockedReasons": dict(collections.Counter(item["error"] for item in errors))})
        counter.add(1, relative_container)
        del environment
        if container_index % 20 == 0:
            gc.collect()
    counter.finish()
    files = []
    for relative, changes in sorted(replacements.items()):
        before = texts[relative]
        previous_start = len(before) + 1
        after = before
        for start, end, replacement in sorted(changes, reverse=True):
            if end > previous_start or MISSING_GUID not in before[start:end]:
                raise RecoveryError("Native pointer-only overlay contains overlapping/non-missing edits.")
            after = after[:start] + replacement + after[end:]
            previous_start = start
        target = output / "Overlay" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(after.encode("utf-8"))
        files.append({"path": relative, "beforeSha256": source_hashes[relative],
                      "sha256": sha256(target), "pointerCount": len(changes), "remainingMissingPointers": after.count(MISSING_GUID)})
    receipt = {"schema": 1, "proof": "exact-original-native-owner-CAB-pathID-field-and-target-CAB-pathID",
               "scope": "all-original-missing-native-pointers" if only_paths is None else "selected-original-native-pointer-assets",
               "sourceProject": str(project), "nativePointerCount": expected, "restoredPointerCount": len(proofs),
               "blockedOwnerCount": len(errors), "remainingMissingPointerCount": expected - len(proofs),
               "complete": limit is None and not errors and expected == len(proofs), "files": files,
               "pointers": proofs, "errors": errors, "additionalNativeTargets": additional_targets,
               "nativeEngineRedirects": [value for value in objects.values() if value.get("nativePointerType") == 0],
               "unityImportVerified": False}
    write_json(output / "native-pointer-repair.json", receipt)
    return receipt


def apply(project, overlay, receipt, *, allow_partial=False):
    """Apply a source-verified overlay to a fresh private project only."""
    project, overlay = Path(project).resolve(), Path(overlay).resolve()
    if not receipt.get("complete") and not allow_partial:
        raise RecoveryError("Incomplete native pointer recovery cannot be applied as complete content.")
    for row in receipt["files"]:
        original = project / row["path"]
        source = overlay / "Overlay" / row["path"]
        if sha256(original) != row["beforeSha256"] or sha256(source) != row["sha256"]:
            raise RecoveryError("Native pointer overlay's exact input/output bytes changed: " + row["path"])
    for path in sorted((overlay / "Overlay").rglob("*")):
        if path.is_file():
            relative = path.relative_to(overlay / "Overlay")
            target = project / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("project", "game-data", "identities", "cab-bundles", "output"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--limit", type=int, help="Diagnostic container bound; never produces a complete receipt.")
    parser.add_argument("--redirects", help="Captured actual pinned-exporter native engine redirect JSONL.")
    parser.add_argument("--atlas-recipes", help="Captured native atlas YAML descriptors JSON.")
    parser.add_argument("--native-recipes", help="Captured original managed field YAML descriptors JSON.")
    args = parser.parse_args(argv)
    try:
        atlas_recipes = json.loads(Path(args.atlas_recipes).read_text()) if args.atlas_recipes else None
        native_recipes = json.loads(Path(args.native_recipes).read_text()) if args.native_recipes else None
        receipt = prepare(args.project, args.game_data, args.identities, args.cab_bundles, args.output, limit=args.limit,
                          redirects=args.redirects, atlas_recipes=atlas_recipes, native_recipes=native_recipes)
        print(json.dumps({key: receipt[key] for key in ("schema", "nativePointerCount", "restoredPointerCount",
              "blockedOwnerCount", "remainingMissingPointerCount", "complete", "unityImportVerified")}))
        return 0 if receipt["complete"] or args.limit is not None else 1
    except (RecoveryError, OSError, ValueError) as error:
        print("[Quest native pointer recovery] FAILED:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
