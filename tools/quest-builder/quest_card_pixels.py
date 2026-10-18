"""Cache exact owned card/UI mip-zero pixels without Unity import or XR readback.

Only named original UI/card/portrait bundles are opened, one at a time. A disk
spool retains compressed entries while duplicate names are checked; decoded RAM
is bounded by one texture rather than the whole game's artwork.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import struct
import tempfile
import time
import uuid
import zlib

MAGIC = b"GHVP1"
RECIPE = "owned-native-card-pixels-v1"
MEMBER = "assets/quest-card-pixels.bin"
MANIFEST = "assets/Quest/card-pixels.json"
RUNTIME_SOURCE = "src/GloomhavenVR/Cards/Art/QuestCardPixels.cs"
MAX_FILE = 512 * 1024 * 1024
MAX_PIXELS = 4096 * 4096
MAX_TOTAL = 384 * 1024 * 1024  # pixels on disk; never decoded together
MAX_ENTRIES = 4096
MAX_SOURCE = 128 * 1024 * 1024
_BUNDLE = re.compile(r"(?:character_gui_assets_ac_[a-z0-9_]+|itemconfigs_backgrounds_assets_[a-z0-9_-]+|actor_portrats_assets_[a-z0-9_]+)[.]bundle\Z", re.IGNORECASE)
_COMMON = re.compile(r"(?:dlc_dlc[0-9]+_)?misc_(?:gui|characterportraits)_assets_all[.]bundle\Z", re.IGNORECASE)
_UNSAFE_NAME = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


class PixelError(ValueError):
    pass


def canonical_name(name):
    # AssetRipper's filename -> TextureImporter product replaces Windows-invalid
    # characters. The original sactx DXT5|BC3 page is DXT5_BC3 on the Quest. Apply
    # that same explicit alias before collision detection, never a fuzzy match.
    return _UNSAFE_NAME.sub("_", name)


def source_files(game):
    result = []
    root = Path(game) / "StreamingAssets/aa"
    if root.is_dir():
        for folder in sorted(root.iterdir()):
            if folder.is_dir() and folder.name.startswith("Standalone"):
                for path in sorted(folder.iterdir()):
                    if path.is_file() and (_BUNDLE.fullmatch(path.name) or _COMMON.fullmatch(path.name)):
                        if not 0 < path.stat().st_size <= MAX_SOURCE:
                            raise PixelError("Owned UI source exceeds its bundle byte bound: " + path.name)
                        result.append(path)
    if not result or len(result) > MAX_ENTRIES:
        raise PixelError("Owned installation has no supported card/UI pixel sources.")
    return result


def decode_bundle(path):
    import UnityPy
    from PIL import Image
    environment = UnityPy.load(str(path))
    try:
        for obj in environment.objects:
            if obj.type.name != "Texture2D":
                continue
            texture = obj.read()
            width, height = int(texture.m_Width), int(texture.m_Height)
            if int(texture.m_MipCount) > 1:
                continue  # The original already supplies filtering; no duplicate.
            if not 1 <= width <= 4096 or not 1 <= height <= 4096 or width * height > MAX_PIXELS:
                raise PixelError("Owned UI texture exceeds its pixel bound: " + texture.m_Name)
            image = texture.image.convert("RGBA")
            if image.size != (width, height) or int(texture.m_ColorSpace) not in (0, 1):
                raise PixelError("Owned UI decoder dimensions or colour encoding disagree.")
            # UnityPy/PIL rows are top-down; native Sprite textureRect/GetPixels32
            # coordinates are bottom-up. Preserve raw RGB AND alpha encoding.
            pixels = image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
            yield texture.m_Name, width, height, int(texture.m_ColorSpace), pixels
    finally:
        del environment


def _publish(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.write_bytes(data)
        _replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _replace(source, target):
    for attempt in range(10):
        try:
            os.replace(source, target)
            return
        except PermissionError:
            if attempt == 9:
                raise
            time.sleep(.05 * (attempt + 1))


def file_hash(path):
    with Path(path).open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def write_entries(entries, target):
    """Stream compressed entries; reject ALL ambiguous normalized identities."""
    target = Path(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    records, ambiguous, seen = {}, set(), 0
    with tempfile.TemporaryFile(dir=target.parent) as spool:
        for name, width, height, srgb, pixels in entries:
            if (not isinstance(name, str) or "\0" in name or not 0 < len(name.encode("utf-8")) <= 1024
                    or not 1 <= width <= 4096 or not 1 <= height <= 4096
                    or width * height > MAX_PIXELS or srgb not in (0, 1) or len(pixels) != width * height * 4):
                raise PixelError("Malformed owned card pixel entry.")
            seen += 1
            if seen > MAX_ENTRIES * 4:
                raise PixelError("Owned UI source texture inventory exceeds its bound.")
            name = canonical_name(name)
            digest = hashlib.sha256(pixels).digest()
            identity = width, height, srgb, digest
            if name in records:
                if records[name]["identity"] != identity:
                    ambiguous.add(name)
                continue
            if len(records) >= MAX_ENTRIES:
                raise PixelError("Owned card pixel inventory exceeds its entry bound.")
            compressor = zlib.compressobj(1, zlib.DEFLATED, -15)
            payload = compressor.compress(pixels) + compressor.flush()
            if spool.tell() + len(payload) > MAX_FILE:
                raise PixelError("Owned card pixel spool exceeds its compressed byte bound.")
            records[name] = {"identity": identity, "offset": spool.tell(), "size": len(payload)}
            spool.write(payload)
        for name in ambiguous:
            records.pop(name, None)
        if not records or sum(r["identity"][0] * r["identity"][1] for r in records.values()) > MAX_TOTAL:
            raise PixelError("Owned card pixel inventory is empty or exceeds its decoded bound.")
        result = []
        with target.open("wb") as output:
            output.write(MAGIC + struct.pack("<i", len(records)))
            for name, row in sorted(records.items()):
                width, height, srgb, digest = row["identity"]
                label = name.encode("utf-8")
                output.write(struct.pack("<i", len(label)) + label
                             + struct.pack("<iiii", width, height, srgb, row["size"]) + digest)
                spool.seek(row["offset"])
                remaining = row["size"]
                while remaining:
                    block = spool.read(min(remaining, 1024 * 1024))
                    if not block:
                        raise PixelError("Owned card pixel spool is truncated.")
                    output.write(block)
                    remaining -= len(block)
                if output.tell() > MAX_FILE:
                    raise PixelError("Owned card pixel file exceeds its compressed byte bound.")
                result.append({"name": name, "width": width, "height": height, "srgb": bool(srgb),
                               "rgbaSha256": digest.hex()})
    return result, sorted(ambiguous)


def build(game, cache, game_key, *, progress=None, decoder=None):
    game, cache = Path(game), Path(cache)
    sources = source_files(game)
    def stamps():
        return [{"path": p.relative_to(game).as_posix(), "size": p.stat().st_size,
                 "mtimeNs": p.stat().st_mtime_ns} for p in sources]
    before = stamps()
    recipe = RECIPE + ":" + hashlib.sha256(Path(__file__).read_bytes()).hexdigest()[:16]
    owner = hashlib.sha256((recipe + ":" + str(game_key)).encode()).hexdigest()
    root = cache / "card-pixels" / owner
    target, receipt = root / "quest-card-pixels.bin", root / "receipt.json"
    try:
        previous = json.loads(receipt.read_text())
        metadata = previous["manifest"]
        if (previous["sources"] == before and metadata["recipe"] == recipe and metadata["gameKey"] == str(game_key)
                and target.stat().st_size == metadata["bytes"] <= MAX_FILE
                and target.stat().st_mtime_ns == previous["outputMtimeNs"]):
            # Private builder cache + exact original metadata stamps and producer
            # receipt. Avoid rereading hundreds of MB on every Mod Update.
            return {"path": str(target), "manifest": metadata, "reused": True}
    except (OSError, ValueError, KeyError, TypeError):
        pass
    root.mkdir(parents=True, exist_ok=True)
    temporary = root / ("pixels-" + uuid.uuid4().hex + ".tmp")
    try:
        def entries():
            for index, path in enumerate(sources):
                yield from (decoder or decode_bundle)(path)
                if progress:
                    progress(index + 1, len(sources), path.name)
        records, ambiguous = write_entries(entries(), temporary)
        if stamps() != before:
            raise PixelError("Owned card pixel sources changed during extraction; retry with the game closed.")
        metadata = {"schema": 1, "recipe": recipe, "gameKey": str(game_key), "bytes": temporary.stat().st_size,
                    "sha256": file_hash(temporary), "entries": len(records)}
        _replace(temporary, target)
        _publish(receipt, (json.dumps({"sources": before, "manifest": metadata, "textures": records,
                                     "outputMtimeNs": target.stat().st_mtime_ns,
                                     "ambiguousNamesRejected": ambiguous}, sort_keys=True, indent=2) + "\n").encode())
        return {"path": str(target), "manifest": metadata, "reused": False}
    finally:
        temporary.unlink(missing_ok=True)
