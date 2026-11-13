#!/usr/bin/env python3
"""Restore four observed original MonoBehaviour layouts from complete bytes.

The original UIFollowMapLocation target is a SerializeReference object with its
registry at the end of the payload. AssetRipper's metadata-only fallback loses
non-null registries. DimmingColorModel has no serializable fields, so its list
contains zero-byte values. Neither defect permits deleting the component or
substituting guessed/default references. This reader consumes every source byte
and rejects unknown layouts, managed types and unresolved object identities.
"""
import hashlib
import json
from pathlib import Path
import re
import struct

from recover import RecoveryError, sha256, write_json

CLASSES = {"DimmerUIElements", "UIFollowMapLocationInsideArea", "UILocationMapMarker", "UIQuestMapMarker"}


class Reader:
    def __init__(self, data, position=0):
        self.data, self.position = data, position

    def value(self, format_string):
        size = struct.calcsize(format_string)
        if self.position + size > len(self.data):
            raise RecoveryError("Truncated original serialized behaviour payload.")
        result = struct.unpack_from(format_string, self.data, self.position)
        self.position += size
        return result[0] if len(result) == 1 else result

    def text(self):
        length = self.value("<i")
        if length < 0 or self.position + length > len(self.data):
            raise RecoveryError("Invalid original serialized string length.")
        result = self.data[self.position:self.position + length].decode("utf-8")
        self.position = (self.position + length + 3) & ~3
        return result

    def pointer(self):
        return self.value("<i"), self.value("<q")


def parse_fields(class_name, raw):
    if class_name not in CLASSES:
        raise RecoveryError("Unaudited original layout repair: " + class_name)
    reader = Reader(raw)
    reader.pointer()  # Native MonoBehaviour header: owning GameObject.
    reader.value("<i")  # m_Enabled and alignment.
    reader.pointer()  # Original MonoScript pointer.
    reader.text()  # Original m_Name and alignment.
    fields, references = {}, None
    if class_name == "DimmerUIElements":
        count = reader.value("<i")
        if not 0 <= count <= 100_000:
            raise RecoveryError("Invalid original ignored graphic list.")
        fields["_elementsToIgnore"] = [reader.pointer() for _ in range(count)]
        count = reader.value("<i")
        if not 0 <= count <= 100_000:
            raise RecoveryError("Invalid original zero-field dimming model list.")
        fields["_dimmingColorPairsContainer"] = [{} for _ in range(count)]
    else:
        fields["offset"] = reader.value("<3f")
        fields["target"] = {"rid": reader.value("<q")}
        if class_name == "UIFollowMapLocationInsideArea":
            fields.update(area=reader.pointer(), xDisplacementToReposition=reader.value("<f"), pointer=reader.pointer())
        elif class_name == "UILocationMapMarker":
            for name in ("icon", "shield", "nameText", "mask", "labelContainer", "canvasGroup"):
                fields[name] = reader.pointer()
            for name in ("fadeFrom", "fadeTo", "scaleFrom", "scaleTo", "fadeTime", "slideLabelTime"):
                fields[name] = reader.value("<f")
        elif class_name == "UIQuestMapMarker":
            for name in ("questType", "canvasGroup", "incompleteMask"):
                fields[name] = reader.pointer()
            fields["lockedOpacity"] = reader.value("<f")
            fields["incompletedOpacity"] = reader.value("<f")
            fields["showAnimator"] = reader.pointer()
        version, count = reader.value("<i"), reader.value("<i")
        if version != 2 or not 0 <= count <= 100:
            raise RecoveryError("Unsupported original managed reference registry layout.")
        references = []
        for _ in range(count):
            rid = reader.value("<q")
            kind, namespace, assembly = reader.text(), reader.text(), reader.text()
            reference = {"rid": rid, "class": kind, "ns": namespace, "asm": assembly, "data": {}}
            if rid in (-2, -1) and not any((kind, namespace, assembly)):
                pass
            elif namespace or assembly != "GH.Runtime":
                raise RecoveryError("Unknown original map target managed assembly/namespace.")
            elif kind == "UIFollowMapLocation/FollowTransform":
                reference["data"]["targetTransform"] = reader.pointer()
            elif kind == "UIFollowMapLocation/FollowWorldPosition":
                reference["data"]["worldPosition"] = reader.value("<3f")
            else:
                raise RecoveryError("Unknown original map target managed type: " + kind)
            references.append(reference)
        ids = [item["rid"] for item in references]
        if len(ids) != len(set(ids)) or fields["target"]["rid"] not in ids:
            raise RecoveryError("Original managed reference registry does not resolve target exactly.")
    if reader.position != len(raw):
        raise RecoveryError("Original behaviour layout has unconsumed bytes: " + class_name)
    return fields, references


def yaml_fields(fields, references, pointer):
    def scalar(value):
        if isinstance(value, tuple) and len(value) == 2:
            return pointer(value)
        if isinstance(value, tuple) and len(value) == 3:
            return "{x: " + str(value[0]) + ", y: " + str(value[1]) + ", z: " + str(value[2]) + "}"
        return str(value)
    rows = []
    for name, value in fields.items():
        if isinstance(value, dict):
            rows.extend(["  " + name + ":", "    rid: " + str(value["rid"])])
        elif isinstance(value, list):
            rows.append("  " + name + (": []" if not value else ":"))
            rows.extend("  - " + ("{}" if item == {} else scalar(item)) for item in value)
        else:
            rows.append("  " + name + ": " + scalar(value))
    if references is not None:
        rows.extend(["  references:", "    version: 2", "    RefIds:"])
        for item in references:
            rows.extend(["    - rid: " + str(item["rid"]),
                         "      type: {class: " + item["class"] + ", ns: " + item["ns"] + ", asm: " + item["asm"] + "}"])
            if not item["data"]:
                rows.append("      data: {}")
            else:
                rows.append("      data:")
                rows.extend("        " + name + ": " + scalar(value) for name, value in item["data"].items())
    return "\n".join(rows) + "\n"


def restore(game_data, project, objects, unitypy=None):
    if unitypy is None:
        try:
            import UnityPy as unitypy
        except ImportError as error:
            raise RecoveryError("UnityPy is required to read original serialized-layout repairs.") from error
    source, project = Path(game_data).resolve(), Path(project).resolve()
    paths = [str(path) for path in source.iterdir() if path.is_file()
             and (re.fullmatch(r"level\d+", path.name) or path.suffix == ".assets")]
    environment = unitypy.load(*paths)
    keys = {(name.casefold(), path_id): value for (name, path_id), value in objects.items()}
    repairs, contents = [], {}
    for obj in environment.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        base = obj.read(check_read=False)
        try:
            script = base.m_Script.read()
        except (KeyError, FileNotFoundError):
            continue
        name = script.m_ClassName
        if name not in CLASSES:
            continue
        raw = obj.get_raw_data()
        fields, references = parse_fields(name, raw)
        key = (obj.assets_file.name.casefold(), obj.path_id)
        target = keys.get(key)
        if target is None:
            continue  # This original object is outside the selected export.
        path = project / target["path"]
        if path.suffix not in (".unity", ".prefab", ".asset"):
            raise RecoveryError("Original behaviour maps to a non-YAML asset.")
        text = contents.get(path, path.read_text(encoding="utf-8"))
        pattern = re.compile(r"(^--- !u!114 &" + str(target["fileId"]) + r"\n.*?^  m_EditorClassIdentifier:[^\n]*\n)(.*?)(?=^--- !u!|\Z)", re.M | re.S)
        matches = list(pattern.finditer(text))
        if len(matches) != 1:
            raise RecoveryError("Original behaviour identity does not resolve exactly one exported YAML object.")
        def pointer(value):
            file_id, path_id = value
            if path_id == 0:
                return "{fileID: 0}"
            collection = obj.assets_file.name
            if file_id:
                if not 1 <= file_id <= len(obj.assets_file.externals):
                    raise RecoveryError("Original serialized pointer has an invalid external file index.")
                collection = obj.assets_file.externals[file_id - 1].path.replace("\\", "/").split("/")[-1]
            item = keys.get((collection.casefold(), path_id))
            if item is None:
                raise RecoveryError("Original repaired behaviour references an unrecovered object: " + repr((collection, path_id)))
            if item["guid"] == target["guid"]:
                return "{fileID: " + str(item["fileId"]) + "}"
            return "{fileID: " + str(item["fileId"]) + ", guid: " + item["guid"] + ", type: 2}"
        restored = yaml_fields(fields, references, pointer)
        match = matches[0]
        before = match[2]
        contents[path] = text[:match.start(2)] + restored + text[match.end(2):]
        repairs.append({"collection": obj.assets_file.name, "pathId": obj.path_id, "class": name,
                        "originalBytes": len(raw), "originalSha256": hashlib.sha256(raw).hexdigest(),
                        "assetPath": target["path"], "fileId": target["fileId"],
                        "previousFieldsSha256": hashlib.sha256(before.encode()).hexdigest(),
                        "restoredFieldsSha256": hashlib.sha256(restored.encode()).hexdigest(),
                        "managedReferenceCount": len(references or []), "allOriginalBytesConsumed": True})
    for path, text in contents.items():
        path.write_text(text, encoding="utf-8")
    receipt = {"schema": 1, "ownedRuntimeSha256": sha256(source / "Managed/GH.Runtime.dll"), "repairs": repairs,
               "repairedObjectCount": len(repairs), "unityImportVerified": False,
               "unknownPayloadsOrPointersReplacedWithDefaults": False}
    destination = project / "QuestRecovery/serialized-layout-restoration.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    write_json(destination, receipt)
    return receipt
