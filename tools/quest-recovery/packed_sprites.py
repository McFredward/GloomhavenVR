"""Bake exact native packed-Sprite drawing state into portable native assets."""
import hashlib
import json
from pathlib import Path
import re
import struct

from recover import RecoveryError, sha256, write_json


def guid(value):
    # Unity's native GUID uses four little-endian, nibble-ordered words.
    return "".join(f"{value['data[' + str(index) + ']']:08x}"[::-1] for index in range(4))


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def packed_uv(position, transform, width, height):
    return (f32(f32(f32(position[0] * transform["x"]) + transform["y"]) / width),
            f32(f32(f32(position[1] * transform["z"]) + transform["w"]) / height))


def vertex_bytes(vertex, row, width, height):
    """Retain native positions/indices and update only the original UV channel."""
    channels = vertex["m_Channels"]
    if len(channels) < 5 or channels[0]["format"] != 0 or channels[0]["dimension"] != 3 or channels[4]["format"] != 0 or channels[4]["dimension"] != 2:
        raise RecoveryError("Unsupported original packed Sprite vertex format.")
    if (row["settingsRaw"] >> 2) & 15:
        raise RecoveryError("Original rotated Sprite needs a witnessed native packing rule.")
    count = vertex["m_VertexCount"]
    data = bytearray(vertex["m_DataSize"])
    strides = {}
    for channel in channels:
        if channel["dimension"]:
            if channel["format"] != 0:
                raise RecoveryError("Unsupported original packed Sprite channel scalar type.")
            strides[channel["stream"]] = max(strides.get(channel["stream"], 0), channel["offset"] + channel["dimension"] * 4)
    offsets, position = {}, 0
    for stream in sorted(strides):
        offsets[stream] = position
        position = (position + count * strides[stream] + 15) & ~15
    if not len(data) <= position < len(data) + 16:
        raise RecoveryError("Native packed Sprite stream sizes disagree.")
    vertices, uv = [], []
    for index in range(count):
        pos = struct.unpack_from("<3f", data, offsets[channels[0]["stream"]] + index * strides[channels[0]["stream"]] + channels[0]["offset"])
        coords = packed_uv(pos, row["uvTransform"], width, height)
        struct.pack_into("<2f", data, offsets[channels[4]["stream"]] + index * strides[channels[4]["stream"]] + channels[4]["offset"], *coords)
        vertices.append({"x": pos[0], "y": pos[1]})
        uv.append({"x": coords[0], "y": coords[1]})
    return bytes(data), vertices, uv


def field_edits(text, values):
    import yaml
    from yaml.nodes import MappingNode
    clean = re.sub(r"^%[^\n]*\n", lambda match: " " * (len(match[0]) - 1) + "\n", text, flags=re.M)
    clean = re.sub(r"^--- !u!\d+ &-?\d+", lambda match: "---" + " " * (len(match[0]) - 3), clean, flags=re.M)
    node = yaml.compose(clean, Loader=getattr(yaml, "CSafeLoader", yaml.SafeLoader))
    if not isinstance(node, MappingNode) or len(node.value) != 1:
        raise RecoveryError("Packed Sprite lost its unique original YAML object.")
    nodes = {}
    def walk(current, path=()):
        nodes[path] = current
        if isinstance(current, MappingNode):
            for key, child in current.value:
                walk(child, path + (key.value,))
    walk(node.value[0][1])
    edits = []
    for path, value in values.items():
        if path not in nodes:
            raise RecoveryError("Original packed Sprite field absent: " + repr(path))
        item = nodes[path]
        if isinstance(item, MappingNode) and not item.flow_style:
            value += "\n" + " " * item.end_mark.column
        edits.append((item.start_mark.index, item.end_mark.index, value))
    previous = len(text) + 1
    for start, end, value in sorted(edits, reverse=True):
        if end > previous:
            raise RecoveryError("Packed Sprite restoration contains overlapping fields.")
        text = text[:start] + value + text[end:]
        previous = start
    return text


def scalar(value):
    return format(float(value), ".9g")


def vector(value):
    return "{" + ", ".join(key + ": " + scalar(item) for key, item in value.items()) + "}"


def rectangle(value, indentation):
    # Unity's native Rect YAML reader requires its versioned block layout.
    return "serializedVersion: 2\n" + "\n".join(" " * indentation + key + ": " + scalar(item) for key, item in value.items())


def restore(project, game_data, atlas, atlas_identity, objects, owners, output, *, unitypy=None, oracle=None):
    """Restore every actual packed member, including private cross-bundle sprites."""
    if unitypy is None:
        import UnityPy as unitypy
    from pointer_recovery import native_target, load_native
    project, game_data, output = map(Path, (project, game_data, output))
    fields = atlas.read_typetree()
    maps = {(guid(key[0]), int(key[1])): value for key, value in fields["m_RenderDataMap"]}
    if len(maps) != len(fields["m_RenderDataMap"]):
        raise RecoveryError("Original packed atlas has ambiguous render keys.")
    environments = {}
    def source_object(key):
        container = owners.get(key[0], key[0])
        if container not in environments:
            environments[container] = load_native(unitypy, game_data / container)
        matches = [obj for obj in environments[container].objects if (obj.assets_file.name.casefold(), int(obj.path_id)) == key]
        if len(matches) != 1:
            raise RecoveryError("Original packed sprite/texture has no unique source native identity.")
        return matches[0]
    entries = []
    for member_index, pointer in enumerate(fields["m_PackedSprites"]):
        key = native_target(atlas.assets_file, (pointer["m_FileID"], pointer["m_PathID"]))
        if key not in objects or Path(objects[key]["path"]).suffix != ".asset":
            raise RecoveryError("Original packed Sprite has no portable native Sprite asset.")
        target, native = objects[key], source_object(key)
        sprite = native.read_typetree()
        render_key = guid(sprite["m_RenderDataKey"][0]), int(sprite["m_RenderDataKey"][1])
        if render_key not in maps:
            raise RecoveryError("Native packed Sprite key is absent from its original atlas.")
        row = maps[render_key]
        texture_key = native_target(atlas.assets_file, (row["texture"]["m_FileID"], row["texture"]["m_PathID"]))
        texture, texture_fields = objects[texture_key], source_object(texture_key).read_typetree()
        data, vertices, uv = vertex_bytes(sprite["m_RD"]["m_VertexData"], row, texture_fields["m_Width"], texture_fields["m_Height"])
        changes = {(name,): vector(sprite[name]) for name in ("m_Offset", "m_Border", "m_Pivot")}
        changes[("m_Rect",)] = rectangle(sprite["m_Rect"], 4)
        changes[("m_PixelsToUnits",)] = scalar(sprite["m_PixelsToUnits"])
        changes[("m_RenderDataKey",)] = "{" + render_key[0] + ": " + str(render_key[1]) + "}"
        changes[("m_SpriteAtlas",)] = "{fileID: 687078895, guid: " + atlas_identity["guid"] + ", type: 2}"
        changes[("m_RD", "texture")] = "{fileID: " + str(texture["fileId"]) + ", guid: " + texture["guid"] + ", type: 3}"
        for name in ("textureRect", "textureRectOffset", "atlasRectOffset", "uvTransform"):
            changes[("m_RD", name)] = rectangle(row[name], 6) if name == "textureRect" else vector(row[name])
        for name in ("settingsRaw", "downscaleMultiplier"):
            changes[("m_RD", name)] = str(row[name])
        changes[("m_RD", "m_VertexData", "_typelessdata")] = data.hex()
        before = (project / target["path"]).read_text()
        after = field_edits(before, changes)
        path = output / "Overlay" / target["path"]
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(after)
        entry = {"collection": key[0], "pathId": key[1], "memberIndex": member_index,
                 "assetPath": target["path"], "guid": target["guid"], "fileId": target["fileId"],
                 "nativeRenderKey": list(render_key), "originalObjectBytesSha256": hashlib.sha256(native.get_raw_data()).hexdigest(),
                 "beforeSha256": hashlib.sha256(before.encode()).hexdigest(), "sha256": sha256(path),
                 "rect": sprite["m_Rect"], "pivot": sprite["m_Pivot"], "pixelsPerUnit": sprite["m_PixelsToUnits"],
                 "vertices": vertices, "uv": uv, "textureGuid": texture["guid"],
                 "textureWidth": texture_fields["m_Width"], "textureHeight": texture_fields["m_Height"],
                 "uvRule": "float32-native-position-original-atlas-uvTransform-and-texture-size"}
        if oracle is not None:
            witnessed = oracle["packedSprites"][member_index]
            if witnessed["name"] != sprite["m_Name"] + "(Clone)" or witnessed["rect"]["width"] != sprite["m_Rect"]["width"] or len(witnessed["uv"]) != len(uv):
                raise RecoveryError("Original native Sprite oracle order/shape disagrees.")
            maximum = max(abs(actual[axis] - expected[axis]) for actual, expected in zip(uv, witnessed["uv"]) for axis in ("x", "y"))
            if maximum > 1e-7 or vertices != witnessed["vertices"]:
                raise RecoveryError("Native packed Sprite rule disagrees with actual original player geometry/UV.")
            entry["originalPlayerUvMaximumAbsoluteError"] = maximum
        entries.append(entry)
    receipt = {"schema": 1, "originalAtlasCollection": atlas_identity["collection"], "originalAtlasPathId": atlas_identity["pathId"],
               "originalMemberCount": len(fields["m_PackedSprites"]), "restoredMembers": entries,
               "originalWindowsPlayerOracleCompared": oracle is not None, "unityImportVerified": False, "headsetPictureVerified": False}
    write_json(output / ("packed-sprites-" + atlas_identity["guid"] + ".json"), receipt)
    return receipt
