"""Resolve native redirects and packed atlases without naming heuristics."""
import hashlib
import json
from pathlib import Path
import re

from recover import RecoveryError, GUID_PATTERN, script_file_id, sha256, write_json

ENGINE_GUIDS = {"0000000000000000e000000000000000", "0000000000000000f000000000000000"}
RAW_POINTER = re.compile(r"\{m_FileID: (-?\d+), m_PathID: (-?\d+), m_TargetClassID: (-?\d+)\}")


def engine_redirects(path):
    """Only actual pinned-exporter engine redirects are portable without assets."""
    result = {}
    for line in Path(path).read_text().splitlines():
        row = json.loads(line)
        if row["guid"] not in ENGINE_GUIDS:
            continue
        if row["type"] != 0 or row["exportCollection"] not in (
                "AssetRipper.Export.UnityProjects.SingleRedirectExportCollection",
                "AssetRipper.Export.UnityProjects.RedirectExportCollection"):
            raise RecoveryError("Unknown native engine redirect implementation.")
        row = {**row, "collection": row["collection"].casefold(), "path": "", "nativePointerType": 0,
               "proof": "pinned-exporter-original-native-engine-redirect"}
        key = row["collection"], int(row["pathId"])
        if key in result and result[key] != row:
            raise RecoveryError("Original native engine object has conflicting redirects.")
        result[key] = row
    return result


def script_target(project, game_data, original, managed_types):
    """Join actual serialized MonoScript metadata to the same original DLL type."""
    fields = original.read_typetree()
    name, namespace, assembly = fields["m_ClassName"], fields["m_Namespace"], fields["m_AssemblyName"]
    dll = Path(project) / "Assets/Plugins" / assembly
    source = Path(game_data) / "Managed" / assembly
    if assembly not in managed_types or not dll.is_file() or not source.is_file() or sha256(dll) != sha256(source):
        raise RecoveryError("Native MonoScript has no matching exact original managed assembly.")
    matches = [row for row in managed_types[assembly]["types"]
               if not row["nested"] and row["name"] == name and row["namespace"] == namespace]
    if len(matches) != 1:
        raise RecoveryError("Native MonoScript has no unique original assembly metadata type.")
    match = GUID_PATTERN.search(dll.with_name(dll.name + ".meta").read_text())
    if match is None:
        raise RecoveryError("Original managed assembly has no imported GUID.")
    return {"collection": original.assets_file.name.casefold(), "pathId": int(original.path_id),
            "classId": 115, "guid": match[1], "fileId": script_file_id(namespace, name),
            "path": dll.relative_to(project).as_posix(), "nativePointerType": 3,
            "proof": "original-native-MonoScript-and-exact-original-DLL-metadata",
            "assemblySha256": sha256(source), "assembly": assembly,
            "nativeNamespace": namespace, "nativeClassName": name}


def atlas_target(project, original, editor_yaml, objects, output):
    """Preserve the native packed atlas graph and all original drawing maps."""
    from pointer_recovery import native_target
    fields = original.read_typetree()
    if original.type.value != 687078895 or not fields["m_PackedSprites"] or fields["m_IsVariant"]:
        raise RecoveryError("Unsupported or empty original packed SpriteAtlas.")
    replacements = []
    def replace(match):
        pointer = int(match[1]), int(match[2])
        if pointer[1] == 0:
            return "{fileID: 0}"
        key = native_target(original.assets_file, pointer)
        if key not in objects:
            raise RecoveryError("Original packed atlas target was not recovered: " + repr(key))
        target = objects[key]
        replacements.append({"collection": key[0], "pathId": key[1], "guid": target["guid"], "fileId": target["fileId"]})
        # Original atlas members are native Sprite and Texture2D objects, which
        # the recovered project imports as native assets and TextureImporter.
        pointer_type = 3 if Path(target["path"]).suffix.lower() in (".png", ".jpg", ".jpeg", ".tga") else 2
        return "{fileID: " + str(target["fileId"]) + ", guid: " + target["guid"] + ", type: " + str(pointer_type) + "}"
    text = RAW_POINTER.sub(replace, editor_yaml)
    if "m_FileID:" in text or "m_PathID:" in text:
        raise RecoveryError("Packed atlas YAML contains an unparsed native pointer.")
    key = original.assets_file.name.casefold(), int(original.path_id)
    guid = hashlib.sha256(("quest-original-packed-atlas\0" + key[0] + "\0" + str(key[1])).encode()).hexdigest()[:32]
    # A NativeFormatImporter asset retains the original packed runtime map.
    # SpriteAtlasImporter would attempt to repack already packed native meshes.
    relative = "Assets/QuestOriginalCampaign/NativeAtlases/" + guid + ".asset"
    target = Path(output) / "Overlay" / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text)
    target.with_name(target.name + ".meta").write_text("fileFormatVersion: 2\nguid: " + guid + "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 687078895\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    return {"collection": key[0], "pathId": key[1], "classId": 687078895, "guid": guid,
            "fileId": 687078895, "path": relative, "nativePointerType": 2,
            "proof": "original-native-packed-SpriteAtlas-and-pinned-editor-YAML",
            "originalObjectBytesSha256": hashlib.sha256(original.get_raw_data()).hexdigest(),
            "originalPackedSpriteCount": len(fields["m_PackedSprites"]),
            "originalRenderDataMapCount": len(fields["m_RenderDataMap"]),
            "originalTag": fields["m_Tag"], "nativeRemappedPointers": replacements,
            "editorYamlSha256": hashlib.sha256(editor_yaml.encode()).hexdigest(), "sha256": sha256(target),
            "unityImportVerified": False, "androidPackedAtlasVerified": False}
