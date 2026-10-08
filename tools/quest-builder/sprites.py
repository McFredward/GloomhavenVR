"""Restore owned loading-sprite drawing geometry lost by the recovered export.

Only a generated Quest project is changed. Texture pixels, atlas crops, vertex
data, UVs, GUIDs and the player's original serialized bank remain untouched.
The pinned Unity v22 Sprite prefix reader uses the Python standard library.
"""
import math
import json
from pathlib import Path
import re
import struct

from script_order import _Reader
from storage import BuildError, digest, write_json

RECEIPT = "QuestStartupEvidence/loading-sprite-geometry.json"
NAMES = {"LoadingBase", "LoadingOverlay"}


def read_sprite_headers(path: Path) -> list[dict]:
    """Read original Sprite drawing fields, without decoding textures or game code."""
    if path.is_symlink() or not path.is_file() or not 48 <= path.stat().st_size <= 128 * 1024 * 1024:
        raise BuildError("Owned sprite geometry bank is missing, linked or unexpectedly large.")
    data = path.read_bytes()
    header = _Reader(data, endian=">")
    header.take(8)
    version = header.number("I")
    header.take(4)
    little = header.number("B")
    if version != 22 or little not in (0, 1) or header.take(3) != b"\0\0\0":
        raise BuildError("Unsupported original sprite serialized format; audit the new game version.")
    metadata_size, file_size, data_offset = header.number("I"), header.number("q"), header.number("q")
    header.take(8)
    if file_size != len(data) or not 48 < 48 + metadata_size <= data_offset <= file_size:
        raise BuildError("Original sprite serialized header sizes are invalid.")
    reader = _Reader(data, 48, 48 + metadata_size, "<" if little == 0 else ">")
    unity = bytearray()
    while len(unity) <= 64:
        byte = reader.take(1)
        if byte == b"\0":
            break
        unity.extend(byte)
    if bytes(unity) != b"2021.3.5f1":
        raise BuildError("Original sprite Unity version changed; audit before rebuilding.")
    reader.take(4)
    type_tree = reader.number("B")
    if type_tree not in (0, 1):
        raise BuildError("Invalid original sprite type-tree flag.")
    types = []
    for _ in range(reader.count(4096)):
        class_id = reader.number("i")
        reader.take(3)
        if class_id == 114:
            reader.take(16)
        reader.take(16)
        if type_tree:
            nodes, strings = reader.count(262144), reader.count(16 * 1024 * 1024)
            reader.take(nodes * 32 + strings)
            reader.take(reader.count(262144) * 4)
        types.append(class_id)
    objects, identities, intervals = [], set(), []
    for _ in range(reader.count()):
        reader.align()
        path_id, start, size, type_index = reader.number("q"), reader.number("q"), reader.number("I"), reader.number("i")
        start += data_offset
        if (path_id in identities or not 0 <= type_index < len(types)
                or not data_offset <= start <= start + size <= file_size):
            raise BuildError("Original sprite object identity or bounds are invalid.")
        identities.add(path_id)
        intervals.append((start, start + size))
        if types[type_index] == 213:
            objects.append((path_id, start, size))
    previous = data_offset
    for start, end in sorted(intervals):
        if start < previous:
            raise BuildError("Original sprite serialized objects overlap.")
        previous = end
    records = []
    for path_id, start, size in objects:
        item = _Reader(data, start, start + size, reader.endian)
        name = item.string()
        if name not in NAMES:
            continue
        rectangle = dict(zip(("x", "y", "width", "height"), struct.unpack(item.endian + "4f", item.take(16))))
        offset = dict(zip(("x", "y"), struct.unpack(item.endian + "2f", item.take(8))))
        border = dict(zip(("x", "y", "z", "w"), struct.unpack(item.endian + "4f", item.take(16))))
        ppu = item.number("f")
        pivot = dict(zip(("x", "y"), struct.unpack(item.endian + "2f", item.take(8))))
        values = [*rectangle.values(), *offset.values(), *border.values(), ppu, *pivot.values()]
        if (not all(math.isfinite(v) for v in values) or not 0 < rectangle["width"] <= 16384
                or not 0 < rectangle["height"] <= 16384 or not 0 < ppu <= 1000000):
            raise BuildError("Original loading sprite geometry is invalid.")
        records.append({"name": name, "rect": rectangle, "offset": offset, "border": border,
                        "pixelsPerUnit": ppu, "pivot": pivot, "sourcePathId": path_id})
    if {record["name"] for record in records} != NAMES:
        raise BuildError("Owned sprite bank lacks both original loading layers.")
    return records


def _numbers(text: str, field: str, keys: tuple[str, ...], *, indentation: int = 2) -> dict:
    pattern = re.compile(r"^" + " " * indentation + re.escape(field) + r": \{([^\r\n]+)\}\r?$", re.M)
    matches = list(pattern.finditer(text))
    if len(matches) != 1:
        raise BuildError("Recovered sprite lacks a unique " + field + ".")
    pairs = dict(re.findall(r"([a-zA-Z]+):\s*([^,}]+)", matches[0].group(1)))
    try:
        values = {key: float(pairs[key]) for key in keys}
    except (ValueError, KeyError) as error:
        raise BuildError("Recovered sprite has invalid " + field + ".") from error
    if set(pairs) != set(keys) or not all(math.isfinite(v) for v in values.values()):
        raise BuildError("Recovered sprite has unsupported " + field + ".")
    return values


def _rect(text: str, field: str, indentation: int = 2) -> dict:
    pattern = re.compile(r"^" + " " * indentation + re.escape(field) + r":\r?\n"
                         + r"(?:^" + " " * (indentation + 2) + r"[^\r\n]+\r?\n)+", re.M)
    matches = list(pattern.finditer(text))
    if len(matches) != 1:
        raise BuildError("Recovered sprite lacks a unique " + field + ".")
    values = dict(re.findall(r"^\s+(x|y|width|height):\s*([^\r\n]+)", matches[0].group(), re.M))
    try:
        result = {key: float(values[key]) for key in ("x", "y", "width", "height")}
    except (ValueError, KeyError) as error:
        raise BuildError("Recovered sprite has invalid " + field + ".") from error
    if not all(math.isfinite(v) for v in result.values()):
        raise BuildError("Recovered sprite has nonfinite " + field + ".")
    return result


def _close(left: dict, right: dict, tolerance: float = 0.001) -> bool:
    return left.keys() == right.keys() and all(abs(left[key] - right[key]) <= tolerance for key in left)


def _vector(field: str, values: dict, indentation: int = 2) -> str:
    return " " * indentation + field + ": {" + ", ".join(key + ": " + format(value, ".9g") for key, value in values.items()) + "}"


def _restore(text: str, originals: list[dict]) -> tuple[str, dict]:
    names = re.findall(r"^  m_Name:\s*(\S+)\r?$", text, re.M)
    if len(names) != 1 or names[0] not in NAMES or not text.startswith("%YAML 1.1") or "--- !u!213 " not in text:
        raise BuildError("Recovered loading sprite identity is unsupported.")
    # Only the m_RD section carries the sampled atlas crop; the empty m_AtlasRD
    # section also contains textureRectOffset and must remain untouched.
    if text.count("  m_RD:\n") != 1 or text.count("  m_AtlasRD:\n") != 1:
        raise BuildError("Recovered loading sprite render-data sections changed.")
    start, end = text.index("  m_RD:\n"), text.index("  m_AtlasRD:\n")
    render = text[start:end]
    crop = _rect(render, "textureRect", 4)
    if crop["width"] <= 0 or crop["height"] <= 0:
        raise BuildError("Recovered loading sprite crop is empty or inverted.")
    old_rect, old_pivot = _rect(text, "m_Rect"), _numbers(text, "m_Pivot", ("x", "y"))
    uv = _numbers(render, "uvTransform", ("x", "y", "z", "w"), indentation=4)
    candidates = []
    for original in originals:
        if original["name"] != names[0] or abs(uv["x"] - original["pixelsPerUnit"]) > 0.001 or abs(uv["z"] - original["pixelsPerUnit"]) > 0.001:
            continue
        width, height = original["rect"]["width"], original["rect"]["height"]
        pivot = original["pivot"]
        rectangle = {"x": uv["y"] - pivot["x"] * width, "y": uv["w"] - pivot["y"] * height,
                     "width": width, "height": height}
        offset = {"x": crop["x"] - rectangle["x"], "y": crop["y"] - rectangle["y"]}
        if (min(offset.values()) < -0.001 or offset["x"] + crop["width"] > width + 0.001
                or offset["y"] + crop["height"] > height + 0.001):
            continue
        trimmed_pivot = {"x": (pivot["x"] * width - offset["x"]) / crop["width"],
                         "y": (pivot["y"] * height - offset["y"]) / crop["height"]}
        if ((_close(old_rect, crop) and _close(old_pivot, trimmed_pivot))
                or (_close(old_rect, rectangle) and _close(old_pivot, pivot))):
            candidates.append((original, rectangle, offset))
    if len(candidates) != 1:
        raise BuildError("Recovered loading sprite cannot map uniquely to its owned drawing geometry: " + names[0])
    original, rectangle, offset = candidates[0]
    patched = text
    for field, values in (("m_Offset", original["offset"]), ("m_Pivot", original["pivot"])):
        _numbers(patched, field, tuple(values))
        patched = re.sub(r"^  " + field + r": \{[^\r\n]+\}\r?$", _vector(field, values), patched, flags=re.M)
    block = "  m_Rect:\n    serializedVersion: 2\n" + "".join("    " + key + ": " + format(value, ".9g") + "\n" for key, value in rectangle.items())
    patched = re.sub(r"^  m_Rect:\n(?:^    [^\r\n]+\n)+", block, patched, flags=re.M)
    _numbers(render, "textureRectOffset", ("x", "y"), indentation=4)
    fixed_render = re.sub(r"^    textureRectOffset: \{[^\r\n]+\}\r?$", _vector("textureRectOffset", offset, 4), render, flags=re.M)
    patched = patched.replace(render, fixed_render, 1)
    return patched, {"name": original["name"], "sourcePathId": original["sourcePathId"],
                     "originalRect": original["rect"], "restoredAtlasRect": rectangle,
                     "textureCrop": crop, "trimOffset": offset, "pivot": original["pivot"],
                     "offset": original["offset"]}


def restore_loading_sprite_geometry(project: Path, game: Path) -> dict:
    """Restore both authored spinner layers before Unity imports a generated project."""
    if any(path.is_symlink() for path in (project, *project.parents)):
        raise BuildError("Generated sprite geometry project is symlinked.")
    bank = game / "resources.assets"
    originals = read_sprite_headers(bank)
    bank_hash = digest(bank)
    packed_manifest = project / "Assets/QuestOriginalCampaign/packed-sprites.json"
    packed = {}
    if packed_manifest.is_file():
        manifest = json.loads(packed_manifest.read_text(encoding="utf-8"))
        for row in manifest["sprites"]:
            if row["assetPath"] in packed:
                raise BuildError("Native packed-Sprite drawing identity is ambiguous.")
            packed[row["assetPath"]] = {**row, "geometryManifest": packed_manifest.relative_to(project).as_posix(),
                                        "nativePacked": True}
    sprite_manifest = project / "Assets/QuestOriginalCampaign/native-sprites.json"
    if sprite_manifest.is_file():
        manifest = json.loads(sprite_manifest.read_text(encoding="utf-8"))
        native_drawings = {}
        for row in manifest["assets"]:
            if row["assetPath"] in native_drawings:
                raise BuildError("Native Sprite drawing identity is ambiguous.")
            native_drawings[row["assetPath"]] = {**row, "collection": row["originalCollection"],
                                                "pathId": row["originalPathId"], "nativePacked": False,
                                                "geometryManifest": sprite_manifest.relative_to(project).as_posix()}
        if packed.keys() & native_drawings.keys():
            raise BuildError("A native loading-Sprite is claimed as both packed and nonpacked.")
        packed.update(native_drawings)
    plans = []
    for path in sorted((project / "Assets/Sprite").glob("Loading*.asset")):
        if path.is_symlink() or any(parent.is_symlink() for parent in path.parents):
            raise BuildError("Recovered loading sprite is symlinked.")
        text = path.read_text(encoding="utf-8")
        name = re.findall(r"^  m_Name:\s*(\S+)\r?$", text, re.M)
        if name not in (["LoadingBase"], ["LoadingOverlay"]):
            continue
        meta = Path(str(path) + ".meta")
        if not meta.is_file() or meta.is_symlink():
            raise BuildError("Recovered loading sprite has no owned GUID metadata.")
        guids = re.findall(r"^guid:\s*([a-f0-9]{32})\s*$", meta.read_text(encoding="utf-8"), re.M)
        if len(guids) != 1:
            raise BuildError("Recovered loading sprite GUID is ambiguous.")
        native = packed.get(path.relative_to(project).as_posix())
        if native is not None:
            # Full recovery already restores the original Sprite object and its
            # separately packed atlas geometry. Applying the historical export
            # crop repair again would replace native drawing coordinates with
            # atlas coordinates and recreate the doubled-spinner defect.
            matches = [row for row in originals if row["sourcePathId"] == native["pathId"]]
            if (native["collection"] != "resources.assets" or native["guid"] != guids[0]
                    or native["sha256"] != digest(path) or len(matches) != 1):
                raise BuildError("Native packed loading-Sprite proof does not match its original identity.")
            original = matches[0]
            if (original["name"] != name[0] or not _close(_rect(text, "m_Rect"), original["rect"])
                    or not _close(_numbers(text, "m_Pivot", ("x", "y")), original["pivot"])
                    or not _close(_numbers(text, "m_Offset", ("x", "y")), original["offset"])):
                raise BuildError("Native packed loading-Sprite drawing geometry differs from the original.")
            patched = text
            render_start = text.index("  m_RD:\n")
            render_end = text.index("  m_AtlasRD:\n")
            render = text[render_start:render_end]
            record = {"name": original["name"], "sourcePathId": original["sourcePathId"],
                      "originalRect": original["rect"], "pivot": original["pivot"], "offset": original["offset"],
                      "restoredAtlasRect": original["rect"], "textureCrop": _rect(render, "textureRect", 4),
                      "trimOffset": _numbers(render, "textureRectOffset", ("x", "y"), indentation=4),
                      "preservedNativePackedGeometry": native["nativePacked"], "preservedNativeDrawingGeometry": True,
                      "geometryManifest": native["geometryManifest"],
                      "geometryManifestSha256": digest(project / native["geometryManifest"])}
        else:
            patched, record = _restore(text, originals)
        plans.append((path, patched, {**record, "asset": path.relative_to(project).as_posix(),
                                     "guid": guids[0], "metaSha256": digest(meta)}))
    if {record["name"] for _, _, record in plans} != NAMES:
        raise BuildError("Generated project lacks both recovered loading layers.")
    records = []
    # Validate every plan before changing any generated asset.
    for path, patched, record in plans:
        path.write_text(patched, encoding="utf-8", newline="\n")
        records.append({**record, "restoredSha256": digest(path)})
    receipt = {"schema": 1, "source": "owned-resources.assets-sprite-drawing-prefix",
               "sourceSha256": bank_hash, "assets": records,
               "preserved": ["texture pixels", "atlas crop", "UV transform", "vertex data", "GUID metadata", "animation layers"]}
    write_json(project / RECEIPT, receipt)
    return receipt
