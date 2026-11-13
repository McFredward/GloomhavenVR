"""Extract exact owned card alpha without importing Unity or rebuilding game banks.

Only original card-background and common GUI bundles are opened, one at a time.
The seekable GHVA1 file stores bottom-up mip-zero alpha as raw DEFLATE entries;
the runtime never derives physical card geometry from an XR GPU readback.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import struct
import time
import uuid
import zlib

MAGIC = b"GHVA1"
RECIPE = "owned-native-card-alpha-v1"
MEMBER = "assets/quest-card-alpha.bin"
MANIFEST = "assets/Quest/card-alpha.json"
RUNTIME_SOURCE = "src/GloomhavenVR/Cards/Art/QuestCardAlpha.cs"
MAX_FILE = 16 * 1024 * 1024
MAX_PIXELS = 32 * 1024 * 1024
MAX_TOTAL = 128 * 1024 * 1024
MAX_ENTRIES = 256
MAX_SOURCE = 128 * 1024 * 1024
_BACKGROUND = re.compile(r"AC_[A-Za-z0-9]+_Background\Z", re.IGNORECASE)
_BUNDLE = re.compile(r"character_gui_assets_ac_[a-z0-9]+_background[.]bundle\Z", re.IGNORECASE)
_COMMON = {"misc_gui_assets_all.bundle", "dlc_dlc1_misc_gui_assets_all.bundle"}
_ITEM = {"ItemCardBase", "Item_frame"}
_ITEM_BUNDLE = re.compile(r"itemconfigs_backgrounds_assets_[a-z0-9_]+[.]bundle\Z", re.IGNORECASE)


class AlphaError(ValueError):
    pass


def _publish(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.write_bytes(data)
        for attempt in range(10):
            try:
                os.replace(temporary, path)
                return
            except PermissionError:
                if attempt == 9:
                    raise
                time.sleep(.05 * (attempt + 1))
    finally:
        temporary.unlink(missing_ok=True)


def source_files(game):
    """Bounded named inputs, not a recursive game scan or all item artwork."""
    root = Path(game) / "StreamingAssets/aa"
    result = []
    if root.is_dir():
        for folder in sorted(root.iterdir()):
            if folder.is_dir() and folder.name.startswith("Standalone"):
                for path in sorted(folder.iterdir()):
                    if path.is_file() and (_BUNDLE.fullmatch(path.name) or _ITEM_BUNDLE.fullmatch(path.name)
                                           or path.name.lower() in _COMMON):
                        if not 0 < path.stat().st_size <= MAX_SOURCE:
                            raise AlphaError("Owned card source exceeds the bounded bundle size: " + path.name)
                        result.append(path)
    if not result or len(result) > MAX_ENTRIES:
        raise AlphaError("The owned installation has no supported card-outline bundles.")
    return result


def decode_bundle(path):
    import UnityPy
    environment = UnityPy.load(str(path))
    try:
        for obj in environment.objects:
            if obj.type.name != "Texture2D":
                continue
            name = obj.peek_name()
            if not (_BACKGROUND.fullmatch(name or "") or name in _ITEM or _ITEM_BUNDLE.fullmatch(Path(path).name)):
                continue
            texture = obj.read()
            width, height = int(texture.m_Width), int(texture.m_Height)
            if not 1 <= width <= 8192 or not 1 <= height <= 8192 or width * height > MAX_PIXELS:
                raise AlphaError("Owned card texture exceeds alpha bounds: " + name)
            # UnityPy presents PIL images top-down. GetPixels32 and the native
            # sprite textureRect use bottom-up mip-zero coordinates.
            image = texture.image.convert("RGBA")
            if image.size != (width, height):
                raise AlphaError("Owned card decoder dimensions disagree: " + name)
            from PIL import Image
            alpha = image.getchannel("A").transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
            yield name, width, height, alpha
    finally:
        # Readers retain only this one original package, never the complete game.
        del environment


def encode(entries):
    """Reject ambiguous texture names rather than choosing a guessed contour."""
    unique = {}
    ambiguous = set()
    for name, width, height, alpha in entries:
        if (not isinstance(name, str) or "\0" in name or not 0 < len(name.encode("utf-8")) <= 1024
                or not 1 <= width <= 8192 or not 1 <= height <= 8192
                or len(alpha) != width * height or len(alpha) > MAX_PIXELS):
            raise AlphaError("Malformed owned card alpha entry.")
        value = width, height, bytes(alpha)
        if name in unique and unique[name] != value:
            ambiguous.add(name)
        unique[name] = value
    for name in ambiguous:
        unique.pop(name, None)
    if not unique or len(unique) > MAX_ENTRIES or sum(w * h for w, h, _ in unique.values()) > MAX_TOTAL:
        raise AlphaError("Owned card alpha entry inventory exceeds bounds or is empty.")
    result = bytearray(MAGIC + struct.pack("<i", len(unique)))
    records = []
    for name, (width, height, alpha) in sorted(unique.items()):
        compressed = zlib.compressobj(6, zlib.DEFLATED, -15)
        payload = compressed.compress(alpha) + compressed.flush()
        label = name.encode("utf-8")
        result += struct.pack("<i", len(label)) + label + struct.pack("<iii", width, height, len(payload)) + payload
        if len(result) > MAX_FILE:
            raise AlphaError("Owned card alpha file exceeds its compressed byte bound.")
        records.append({"name": name, "width": width, "height": height,
                        "alphaSha256": hashlib.sha256(alpha).hexdigest()})
    return bytes(result), records, sorted(ambiguous)


def build(game, cache, game_key, *, progress=None, decoder=None):
    game, cache = Path(game), Path(cache)
    sources = source_files(game)
    stamps = [{"path": p.relative_to(game).as_posix(), "size": p.stat().st_size,
               "mtimeNs": p.stat().st_mtime_ns} for p in sources]
    recipe = RECIPE + ":" + hashlib.sha256(Path(__file__).read_bytes()).hexdigest()[:16]
    owner = hashlib.sha256((recipe + ":" + str(game_key)).encode()).hexdigest()
    root = cache / "card-alpha" / owner
    target, receipt = root / "quest-card-alpha.bin", root / "receipt.json"
    try:
        previous = json.loads(receipt.read_text())
        metadata = previous["manifest"]
        if (previous["sources"] == stamps and metadata["recipe"] == recipe
                and target.stat().st_size == metadata["bytes"] <= MAX_FILE
                and hashlib.sha256(target.read_bytes()).hexdigest() == metadata["sha256"]):
            return {"path": str(target), "manifest": metadata, "reused": True}
    except (OSError, ValueError, KeyError, TypeError):
        pass
    def entries():
        for index, path in enumerate(sources):
            yield from (decoder or decode_bundle)(path)
            if progress:
                progress(index + 1, len(sources), path.name)
    data, records, ambiguous = encode(entries())
    after = [{"path": p.relative_to(game).as_posix(), "size": p.stat().st_size,
              "mtimeNs": p.stat().st_mtime_ns} for p in sources]
    if after != stamps:
        raise AlphaError("Owned card sources changed during extraction; retry with the game closed.")
    metadata = {"schema": 1, "recipe": recipe, "gameKey": str(game_key), "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(), "entries": len(records)}
    _publish(target, data)
    _publish(receipt, (json.dumps({"sources": stamps, "manifest": metadata, "textures": records,
                                 "ambiguousNamesRejected": ambiguous}, sort_keys=True, indent=2) + "\n").encode())
    return {"path": str(target), "manifest": metadata, "reused": False}
