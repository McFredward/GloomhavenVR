#!/usr/bin/env python3
"""Preserve a previous recovery's GUIDs using witnessed original object graphs.

The previous export predates object instrumentation. It is therefore never
joined by filename. Original scene order supplies roots, and corresponding
serialized PPtrs in otherwise byte-identical documents witness every edge.
The instrumented endpoint supplies the original collection/pathID identity.
"""
import collections
import json
from pathlib import Path
import re

from export_identity import GUID, POINTER, object_index
from recover import RecoveryError, YAML_EXTENSIONS, sha256, write_json

GUID_VALUE = re.compile(r"(?<=guid: )[0-9a-f]{32}")
YAML_TYPES = YAML_EXTENSIONS | {".mixer", ".lighting"}


def metadata_index(project):
    result = {}
    for path in Path(project).glob("Assets/**/*.meta"):
        match = GUID.search(path.read_text(encoding="utf-8-sig"))
        if match:
            asset = path.with_name(path.name[:-5])
            if asset.is_file():
                if match[1] in result:
                    raise RecoveryError("Canonical export contains duplicate GUID: " + match[1])
                result[match[1]] = asset
    return result


def scene_roots(old_project, new_project, identities):
    """Scene roots are paired by original native build index, not asset names."""
    def paths(project):
        settings = Path(project) / "ProjectSettings/EditorBuildSettings.asset"
        return re.findall(r"^\s+path: (Assets/.+\.unity)\s*$", settings.read_text(), re.M)
    old_paths, new_paths = paths(old_project), paths(new_project)
    if len(old_paths) != len(new_paths) or not old_paths:
        raise RecoveryError("Original build scene order differs between recoveries.")
    by_path = {row["path"]: row for row in identities}
    roots = []
    for index, (old_path, new_path) in enumerate(zip(old_paths, new_paths)):
        row = by_path.get(new_path)
        if row is None or not row["objects"] or any(obj["collection"] != "level" + str(index) for obj in row["objects"]):
            raise RecoveryError("Scene export is not backed by its original native build index: " + str(index))
        old_meta = Path(old_project) / (old_path + ".meta")
        match = GUID.search(old_meta.read_text())
        if match is None:
            raise RecoveryError("Previous scene metadata lacks its actual GUID.")
        roots.append((row["guid"], match[1], "original-scene-index:" + str(index)))
    return roots


def resource_roots(old_project, identities, new_project=None):
    """Witness original Resources.Load keys and native type, never filenames.

    The captured originalPath on resources.assets objects comes from the native
    ResourceManager container. The old export preserves the corresponding
    Resources directory semantics. Matching the runtime key supplies a root;
    exact serialized/binary content still has to verify it in witness().
    """
    old_index = metadata_index(old_project)
    suffix_types = {".png": 28, ".jpg": 28, ".prefab": 1, ".shader": 48,
                    ".mat": 21, ".ttf": 128, ".otf": 128, ".wav": 83,
                    ".anim": 74, ".controller": 91, ".txt": 49, ".json": 49}
    by_resource = collections.defaultdict(list)
    for guid, path in old_index.items():
        relative = path.relative_to(old_project).as_posix()
        if not relative.startswith("Assets/Resources/"):
            continue
        key = relative[len("Assets/Resources/"):].rsplit(".", 1)[0].casefold()
        native = suffix_types.get(path.suffix)
        if path.suffix in YAML_TYPES:
            header = re.search(r"^--- !u!(\d+)", path.read_text(), re.M)
            if header:
                native = int(header[1])
        if native is not None:
            by_resource[(key, native)].append(guid)
    roots = []
    for row in identities:
        for obj in row["objects"]:
            original = obj.get("originalPath") or ""
            if obj["collection"] != "resources.assets" or not original.startswith("Assets/Resources/"):
                continue
            key = original[len("Assets/Resources/"):].casefold()
            candidates = by_resource[(key, obj["classId"])]
            if len(candidates) == 1:
                if obj["classId"] == 114 and new_project is not None:
                    # Native ResourceManager paths are inherited by child
                    # ScriptableObjects. Their actual MonoScript identity must
                    # agree with the old resource's main native object.
                    pattern = r"m_Script: (\{fileID:[^}]+\})"
                    newer = re.search(pattern, (Path(new_project) / row["path"]).read_text())
                    older = re.search(pattern, old_index[candidates[0]].read_text())
                    if newer is None or older is None or newer[1] != older[1]:
                        continue
                roots.append((row["guid"], candidates[0], "original-Resources.Load:" + key + ":" + str(obj["classId"])))
            elif len(candidates) > 1:
                raise RecoveryError("Previous Resources.Load key/type is ambiguous: " + key)
    # A runtime resource key shared by distinct original objects is not an
    # identity witness. Keep it unresolved rather than selecting the first.
    by_old = collections.defaultdict(set)
    for new_guid, old_guid, _ in roots:
        by_old[old_guid].add(new_guid)
    return [row for row in roots if len(by_old[row[1]]) == 1]


def sprite_roots(old_project, new_project, identities):
    """Original Sprite render-data keys witness dynamically loaded artwork."""
    pattern = re.compile(r"m_RenderDataKey:\s*\n\s*([0-9a-f]{32}):\s*(-?\d+)")
    previous = collections.defaultdict(list)
    for guid, path in metadata_index(old_project).items():
        if path.suffix != ".asset":
            continue
        text = path.read_text()
        if not re.search(r"^--- !u!213 ", text, re.M):
            continue
        match = pattern.search(text)
        if match:
            previous[(match[1], int(match[2]))].append(guid)
    roots = []
    for row in identities:
        if not any(obj["classId"] == 213 for obj in row["objects"]) or not row["path"].endswith(".asset"):
            continue
        text = (Path(new_project) / row["path"]).read_text()
        match = pattern.search(text)
        if match:
            candidates = previous[(match[1], int(match[2]))]
            if len(candidates) == 1:
                roots.append((row["guid"], candidates[0], "original-Sprite-render-key:" + match[1] + ":" + match[2]))
    by_old = collections.defaultdict(set)
    for new_guid, old_guid, _ in roots:
        by_old[old_guid].add(new_guid)
    return [row for row in roots if len(by_old[row[1]]) == 1]


def witness(old_project, new_project, identities, extra_roots=()):
    old_project, new_project = Path(old_project).resolve(), Path(new_project).resolve()
    old_index, new_index = metadata_index(old_project), metadata_index(new_project)
    original = object_index(identities)
    by_pointer = {(obj["guid"], int(obj["fileId"])): obj for obj in original.values()}
    by_guid = collections.defaultdict(list)
    for key, obj in original.items():
        by_guid[obj["guid"]].append({"collection": key[0], "pathId": key[1], "fileId": obj["fileId"]})
    pending = collections.deque(scene_roots(old_project, new_project, identities) +
                                resource_roots(old_project, identities, new_project) +
                                sprite_roots(old_project, new_project, identities) + list(extra_roots))
    mappings, reverse, proofs, rejected, seen = {}, {}, [], [], set()
    while pending:
        new_guid, old_guid, edge = pending.popleft()
        pair = (new_guid, old_guid)
        if pair in seen:
            continue
        seen.add(pair)
        if new_guid in mappings and mappings[new_guid] != old_guid:
            raise RecoveryError("Original object graph witnesses conflicting previous GUIDs: " + repr((new_guid, old_guid, mappings[new_guid], edge)))
        if old_guid in reverse and reverse[old_guid] != new_guid:
            raise RecoveryError("Previous GUID witnesses two different original object collections: " + repr((new_guid, old_guid, reverse[old_guid], edge)))
        if new_guid not in new_index or old_guid not in old_index:
            # Built-in GUIDs and unchanged compiled DLL identities are not
            # guessed or included in original serialized object provenance.
            if new_guid == old_guid:
                continue
            rejected.append({"newGuid": new_guid, "oldGuid": old_guid, "edge": edge, "reason": "missing-exported-endpoint"})
            continue
        new_path, old_path = new_index[new_guid], old_index[old_guid]
        if new_path.suffix in YAML_TYPES and old_path.suffix in YAML_TYPES:
            newer, older = new_path.read_text(), old_path.read_text()
            if GUID_VALUE.sub("0" * 32, newer) != GUID_VALUE.sub("0" * 32, older):
                rejected.append({"newGuid": new_guid, "oldGuid": old_guid, "edge": edge, "reason": "serialized-body-differs"})
                continue
            new_pointers, old_pointers = list(POINTER.finditer(newer)), list(POINTER.finditer(older))
            if len(new_pointers) != len(old_pointers):
                raise RecoveryError("Equal serialized documents yielded different pointer counts.")
            for new_pointer, old_pointer in zip(new_pointers, old_pointers):
                file_id = int(new_pointer[1])
                if file_id != int(old_pointer[1]) or new_pointer[3] != old_pointer[3]:
                    raise RecoveryError("Serialized graph changed an original object target fileID/type.")
                target = by_pointer.get((new_pointer[2], file_id))
                if target is not None:
                    pending.append((new_pointer[2], old_pointer[2],
                                    target["collection"] + ":" + str(target["pathId"])))
                elif new_pointer[2] != old_pointer[2]:
                    rejected.append({"newGuid": new_pointer[2], "oldGuid": old_pointer[2], "edge": edge,
                                     "fileId": file_id, "reason": "unproven-exporter-generated-pointer"})
        else:
            if sha256(new_path) != sha256(old_path):
                rejected.append({"newGuid": new_guid, "oldGuid": old_guid, "edge": edge, "reason": "binary-content-differs"})
                continue
        if new_guid not in by_guid:
            continue
        mappings[new_guid], reverse[old_guid] = old_guid, new_guid
        proofs.append({"newGuid": new_guid, "canonicalGuid": old_guid, "edge": edge,
                       "newPath": new_path.relative_to(new_project).as_posix(),
                       "canonicalPath": old_path.relative_to(old_project).as_posix(),
                       "newSha256": sha256(new_path), "canonicalSha256": sha256(old_path),
                       "originalObjects": by_guid[new_guid]})
    return {"schema": 1, "association": "original-native-scene-index-and-exact-serialized-graph",
            "mappings": mappings, "proofs": proofs, "rejected": rejected,
            "mappedOriginalObjectCount": sum(len(row["originalObjects"]) for row in proofs),
            "canonicalRecoveryProject": str(old_project), "instrumentedRecoveryProject": str(new_project)}


def apply(project, identities, receipt):
    """Apply a proved GUID renaming to a private, completed staging tree only."""
    project = Path(project).resolve()
    mapping = receipt["mappings"]
    all_guids = metadata_index(project)
    for new_guid, old_guid in mapping.items():
        if old_guid in all_guids and old_guid != new_guid and old_guid not in mapping:
            raise RecoveryError("Canonical GUID would overwrite a distinct original object.")
    count = 0
    def replace(match):
        return mapping.get(match[0], match[0])
    for path in project.rglob("*"):
        if path.is_file() and (path.suffix in YAML_TYPES or path.suffix == ".meta"):
            text = path.read_text(encoding="utf-8")
            changed = GUID_VALUE.sub(replace, text)
            if changed != text:
                path.write_text(changed, encoding="utf-8")
                count += 1
    canonical_paths = {row["newGuid"]: row["canonicalPath"] for row in receipt["proofs"]}
    moves = [(row, canonical_paths.get(row["guid"], row["path"])) for row in identities
             if canonical_paths.get(row["guid"], row["path"]) != row["path"]]
    moving_sources = {row["path"] for row, _ in moves}
    temporary = project / ".quest-canonical-guid-moves"
    if temporary.exists():
        raise RecoveryError("An interrupted canonical asset move requires review.")
    for row, destination in moves:
        target = project / destination
        if (target.exists() or target.with_name(target.name + ".meta").exists()) and destination not in moving_sources:
            raise RecoveryError("Proved canonical asset path would overwrite a distinct object: " + destination)
    for row, _ in moves:
        source = project / row["path"]
        interim = temporary / row["guid"] / source.name
        interim.parent.mkdir(parents=True)
        source.rename(interim)
        source.with_name(source.name + ".meta").rename(interim.with_name(interim.name + ".meta"))
    for row, destination in moves:
        interim = temporary / row["guid"] / Path(row["path"]).name
        target = project / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        interim.rename(target)
        interim.with_name(interim.name + ".meta").rename(target.with_name(target.name + ".meta"))
        interim.parent.rmdir()
    if temporary.exists():
        temporary.rmdir()
    rows = []
    for row in identities:
        target_path = canonical_paths.get(row["guid"], row["path"])
        rows.append({**row, "guid": mapping.get(row["guid"], row["guid"]), "path": target_path})
    (project / "QuestRecovery").mkdir(exist_ok=True)
    write_json(project / "QuestRecovery/canonical-guid-restoration.json", {**receipt, "rewrittenFiles": count})
    return rows
