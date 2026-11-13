#!/usr/bin/env python3
"""Join the complete original catalog to captured CAB/pathID export identities."""
import collections
import importlib.util
import json
from pathlib import Path
import re

from catalog import bundle_closure, decode_catalog
from export_identity import object_index, POINTER
from recover import RecoveryError, sha256, write_json

# Builder and recovery both have a historical startup.py. Resolve this helper
# explicitly so a builder import can never bind to the unrelated cached module.
_spec = importlib.util.spec_from_file_location("quest_recovery_startup", Path(__file__).with_name("startup.py"))
_startup = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_startup)
native_asset_type, script_index = _startup.native_asset_type, _startup.script_index

PRELOAD_LABELS = {"always_loaded_base", "always_loaded_standalone", "always_loaded_base_high"}
NATIVE_CLASSES = {1: "UnityEngine.GameObject", 21: "UnityEngine.Material", 28: "UnityEngine.Texture2D",
                  43: "UnityEngine.Mesh", 48: "UnityEngine.Shader", 49: "UnityEngine.TextAsset",
                  74: "UnityEngine.AnimationClip", 83: "UnityEngine.AudioClip", 90: "UnityEngine.Avatar",
                  91: "UnityEngine.RuntimeAnimatorController", 213: "UnityEngine.Sprite", 319: "UnityEngine.SpriteAtlas"}


def prefab_root(project, candidates):
    """Identify the captured native root through its actual local Transform."""
    roots = {}
    for obj in candidates:
        text = (project / obj["path"]).read_text()
        for document in re.split(r"(?=^--- !u!)", text, flags=re.M):
            if not document.startswith(("--- !u!4 ", "--- !u!224 ")):
                continue
            if not re.search(r"^  m_Father: \{fileID: 0\}$", document, re.M):
                continue
            go = re.search(r"^  m_GameObject: \{fileID: (-?\d+)\}$", document, re.M)
            if go and int(go[1]) == int(obj["fileId"]):
                roots[(obj["collection"], obj["pathId"])] = obj
    return list(roots.values())


def associate(catalog_path, project, identities, cab_bundles, managed_types):
    project, catalog_path = Path(project).resolve(), Path(catalog_path).resolve()
    cab_bundles = {key.casefold(): value for key, value in cab_bundles.items()}
    decoded = decode_catalog(json.loads(catalog_path.read_text(encoding="utf-8-sig")))
    scripts, _ = script_index(project, managed_types)
    native_cache = {}
    def native(obj):
        if obj["classId"] != 114:
            return NATIVE_CLASSES.get(obj["classId"])
        if obj["path"] not in native_cache:
            native_cache[obj["path"]] = native_asset_type(project, obj["path"], scripts)
        return native_cache[obj["path"]]
    objects = object_index(identities)
    by_path, by_pointer = collections.defaultdict(list), {}
    for obj in objects.values():
        by_pointer[(obj["guid"], int(obj["fileId"]))] = obj
        if obj.get("originalPath") and obj["collection"] in cab_bundles:
            original = obj["originalPath"].replace("\\", "/").casefold()
            by_path[original].append(obj)
            # Pinned OriginalPathProcessor.EnsureStartsWithAssets adds Assets/
            # to a native Packages/ container. Reverse only that exact prefix;
            # the catalog path, CAB owner and native pathID still all agree.
            if original.startswith("assets/packages/"):
                by_path[original[len("assets/"):]].append(obj)
    aliases = collections.defaultdict(set)
    for key, buckets in zip(decoded["keys"], decoded["buckets"]):
        for location in buckets:
            if isinstance(key, str):
                aliases[location].add(key)
    entries = []
    for location in decoded["locations"]:
        original_path = location["internalId"].replace("\\", "/")
        if not original_path.startswith(("Assets/", "Packages/")):
            continue
        entry_index = location["index"]
        required_bundles = bundle_closure(decoded, [entry_index])
        owners = set(required_bundles)
        candidates = [obj for obj in by_path[original_path.casefold()]
                      if cab_bundles[obj["collection"]] in owners]
        requested_type = location["resourceType"]["m_ClassName"]
        exact = [obj for obj in candidates if native(obj) == requested_type]
        proof = "original-catalog-container-CAB-pathID-and-native-object-type"
        if len(exact) > 1 and requested_type == "UnityEngine.GameObject":
            exact = prefab_root(project, exact)
            proof += "-native-transform-root"
        if not exact and requested_type == "UnityEngine.Material":
            discovered = {}
            for root in candidates:
                if native(root) not in ("TMPro.TMP_SpriteAsset", "TMPro.TMP_FontAsset"):
                    continue
                text = (project / root["path"]).read_text()
                for match in re.finditer(r"^  material: (\{fileID:[^}]+\})$", text, re.M):
                    pointer = POINTER.fullmatch(match[1])
                    target = by_pointer.get((pointer[2], int(pointer[1]))) if pointer else None
                    if target is not None and native(target) == requested_type:
                        discovered[(target["collection"], target["pathId"])] = target
            exact = list(discovered.values())
            proof += "-native-TMP-material-field"
        if not exact and requested_type in ("UnityEngine.Mesh", "UnityEngine.Avatar"):
            # FBX main containers export as native prefab plus separate native
            # mesh/avatar assets. Follow only the witnessed original prefab's
            # actual serialized field, keeping the owning original CAB fixed.
            field = "m_Mesh" if requested_type == "UnityEngine.Mesh" else "m_Avatar"
            discovered = {}
            for root in candidates:
                if native(root) != "UnityEngine.GameObject":
                    continue
                text = (project / root["path"]).read_text()
                for match in re.finditer(re.escape(field) + r":\s*(\{fileID:[^}]+\})", text):
                    pointer = POINTER.fullmatch(match[1])
                    if pointer is not None:
                        target = by_pointer.get((pointer[2], int(pointer[1])))
                        if target is not None and native(target) == requested_type and target["collection"] == root["collection"]:
                            discovered[(target["collection"], target["pathId"])] = target
            exact = list(discovered.values())
            proof = "original-catalog-container-CAB-prefab-native-field-pathID-and-object-type"
        unique = {(obj["collection"], obj["pathId"]): obj for obj in exact}
        # The original provider exposes all typed subassets from PNG atlases
        # and controller containers. Keep every actual original pathID and its
        # subobject name instead of arbitrarily choosing one sprite/clip.
        multiple = len(unique) > 1 and requested_type in ("UnityEngine.Sprite", "UnityEngine.AnimationClip",
                                                          "UnityEngine.Mesh", "UnityEngine.Material")
        targets = list(unique.values()) if len(unique) == 1 or multiple else []
        target = targets[0] if len(targets) == 1 else None
        keys = sorted(aliases[entry_index])
        labels = [key for key in keys if not key.startswith(("Assets/", "Packages/")) and not re.fullmatch(r"[0-9a-f]{32}", key)]
        # The original catalog also emits serializable fields/value types as
        # typed locations for the same native container. They are not separate
        # UnityEngine.Object assets and must not become fabricated objects.
        value_type = not targets and bool(candidates) and not requested_type.startswith("UnityEngine.")
        value_type |= not targets and bool(candidates) and requested_type in {
            "UnityEngine.AddressableAssets.AssetReference", "UnityEngine.AddressableAssets.AssetReferenceSprite",
            "UnityEngine.RectOffset", "UnityEngine.Events.PersistentCallGroup", "UnityEngine.Events.UnityEvent",
            "UnityEngine.Events.PersistentListenerMode", "UnityEngine.Events.ArgumentCache", "UnityEngine.TextCore.FaceInfo",
            "UnityEngine.TextCore.GlyphMetrics", "UnityEngine.TextCore.GlyphRect"}
        value_type |= not targets and bool(candidates) and requested_type.startswith((
            "UnityEngine.UI.", "UnityEngine.EventSystems.", "UnityEngine.AddressableAssets.AssetReferenceT`"))
        value_type |= not targets and bool(candidates) and "+" in requested_type
        row = {"originalLocationIndex": entry_index, "entryIndex": entry_index,
               "originalAssetPath": original_path, "resourceTypeName": requested_type + ", " + location["resourceType"]["m_AssemblyName"],
               "keys": [key for key in keys if key not in labels], "labels": labels,
               "provider": location["provider"], "requiredOriginalBundles": required_bundles,
               "status": "associated" if targets else "ambiguous-original-object" if len(unique) > 1 else
                         "serialized-value-location-excluded" if value_type else "unresolved-original-object",
               "initialObjectLoadEligible": bool(target and PRELOAD_LABELS.intersection(labels)),
               "assetPath": target["path"] if target else None, "recoveredGuid": target["guid"] if target else None,
               "nativeFileId": int(target["fileId"]) if target else 0,
               "recoveredFileId": int(target["fileId"]) if target else 0,
               "sourceBundle": cab_bundles[target["collection"]] if target else None,
               "originalCollection": target["collection"] if target else None,
               "originalPathId": int(target["pathId"]) if target else 0,
               "associationProof": proof if target else None,
               "candidateObjects": [{"collection": obj["collection"], "pathId": obj["pathId"], "className": native(obj)} for obj in candidates] if not targets else []}
        if multiple:
            for obj in targets:
                text = (project / obj["path"]).read_text()
                name = re.search(r"^  m_Name: (.+)$", text, re.M)
                if name is None:
                    raise RecoveryError("Original typed subasset lost its native m_Name: " + obj["path"])
                subobject = name[1]
                native_keys = [obj["guid"]]
                subkeys = [key + "[" + subobject + "]" for key in row["keys"]]
                entries.append({**row, "assetPath": obj["path"], "recoveredGuid": obj["guid"],
                                "nativeFileId": int(obj["fileId"]), "recoveredFileId": int(obj["fileId"]),
                                "sourceBundle": cab_bundles[obj["collection"]], "originalCollection": obj["collection"],
                                "originalPathId": int(obj["pathId"]), "subObjectName": subobject,
                                "nativeKeys": native_keys, "originalSubObjectKeys": subkeys,
                                "associationProof": proof + "-typed-subobject-set",
                                "initialObjectLoadEligible": bool(PRELOAD_LABELS.intersection(labels))})
        else:
            if target:
                row["nativeKeys"] = [target["guid"]]
            entries.append(row)
    unresolved = [row for row in entries if row["status"] not in ("associated", "serialized-value-location-excluded")]
    return {"schema": 1, "catalogSha256": sha256(catalog_path), "entries": entries,
            "originalKeyCount": len(decoded["keys"]), "originalLocationCount": len(decoded["locations"]),
            "associatedEntryCount": sum(row["status"] == "associated" for row in entries),
            "serializedValueLocationCount": sum(row["status"] == "serialized-value-location-excluded" for row in entries),
            "unresolvedEntryCount": len(unresolved), "androidCatalogBuilt": False,
            "association": "captured-original-UnityFS-CAB-and-serialized-pathID"}
