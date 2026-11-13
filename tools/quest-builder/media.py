"""Losslessly adapt ancillary QuickTime timecode tracks for Android MP4 delivery.

No encoder, executable download or third-party package is involved. Only an
unfragmented MP4 containing a tmcd metadata track is rewritten. All authored
audio/video sample payloads, codec descriptions and timelines are retained.
"""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
from pathlib import Path
import struct
import uuid

from storage import BuildError, digest, record_file

RECIPE = "android-mp4-tmcd-remux-v1"
MAX_METADATA = 16 * 1024 * 1024


@dataclass(frozen=True)
class Atom:
    kind: bytes
    start: int
    size: int
    header: int


def _atoms(data: bytes, start: int = 0, end: int | None = None) -> list[Atom]:
    end = len(data) if end is None else end
    atoms = []
    while start < end:
        if end - start < 8:
            raise BuildError("MP4 metadata contains a truncated atom.")
        size, kind = struct.unpack_from(">I4s", data, start)
        header = 8
        if size == 1:
            if end - start < 16:
                raise BuildError("MP4 metadata contains a truncated extended atom.")
            size, header = struct.unpack_from(">Q", data, start + 8)[0], 16
        if size == 0:
            size = end - start
        if size < header or size > end - start or len(atoms) >= 10000:
            raise BuildError("MP4 metadata contains an invalid atom length/count.")
        atoms.append(Atom(kind, start, size, header))
        start += size
    return atoms


def _children(data: bytes, atom: Atom) -> list[Atom]:
    return _atoms(data, atom.start + atom.header, atom.start + atom.size)


def _one(atoms: list[Atom], kind: bytes) -> Atom:
    selected = [a for a in atoms if a.kind == kind]
    if len(selected) != 1:
        raise BuildError("MP4 metadata needs one " + kind.decode("ascii") + " atom.")
    return selected[0]


def _box(kind: bytes, payload: bytes) -> bytes:
    if len(payload) + 8 >= 2**32:
        raise BuildError("Adapted MP4 metadata exceeds its bounded size.")
    return struct.pack(">I4s", len(payload) + 8, kind) + payload


def _top(source: Path) -> tuple[list[Atom], bytes | None]:
    atoms, movie = [], None
    with source.open("rb") as stream:
        if stream.read(8)[4:] != b"ftyp":
            return [], None  # Other container formats are delivered unchanged.
        total, offset = source.stat().st_size, 0
        while offset < total:
            stream.seek(offset)
            prefix = stream.read(16)
            if len(prefix) < 8:
                raise BuildError("MP4 contains a truncated top-level atom.")
            size, kind = struct.unpack_from(">I4s", prefix)
            header = 8
            if size == 1:
                if len(prefix) < 16:
                    raise BuildError("MP4 contains a truncated extended atom.")
                size, header = struct.unpack_from(">Q", prefix, 8)[0], 16
            if size == 0:
                size = total - offset
            if size < header or size > total - offset or len(atoms) >= 10000:
                raise BuildError("MP4 contains an invalid top-level atom.")
            atoms.append(Atom(kind, offset, size, header))
            if kind == b"moov":
                if movie is not None or size > MAX_METADATA:
                    raise BuildError("MP4 movie metadata is duplicated or exceeds its bound.")
                stream.seek(offset); movie = stream.read(size)
                if len(movie) != size:
                    raise BuildError("MP4 movie metadata is truncated.")
            offset += size
    return atoms, movie


def _track(data: bytes, atom: Atom) -> tuple[int, bytes]:
    children = _children(data, atom)
    header = _one(children, b"tkhd")
    payload = data[header.start + header.header:header.start + header.size]
    if not payload or payload[0] not in (0, 1):
        raise BuildError("MP4 track header version is unsupported.")
    at = 12 if payload[0] == 0 else 20
    if len(payload) < at + 4:
        raise BuildError("MP4 track header is truncated.")
    identity = struct.unpack_from(">I", payload, at)[0]
    media = _one(children, b"mdia")
    handler = _one(_children(data, media), b"hdlr")
    payload = data[handler.start + handler.header:handler.start + handler.size]
    if len(payload) < 12:
        raise BuildError("MP4 track handler is truncated.")
    return identity, payload[8:12]


def _metadata(data: bytes, atom: Atom) -> None:
    # Item locations, external data and encrypted auxiliary offsets need their
    # own relocation proof. Recognize only inert metadata used by owned sources.
    start = atom.start + atom.header
    if atom.kind == b"meta" and data[start:start + 4] == b"\0\0\0\0":
        start += 4  # ISO FullBox, versus QuickTime's headerless meta variant.
    children = _atoms(data, start, atom.start + atom.size)
    allowed = {b"meta"} if atom.kind == b"udta" else {b"hdlr", b"keys", b"ilst"}
    if any(a.kind not in allowed for a in children):
        raise BuildError("MP4 timecode adaptation found metadata with unknown offset safety.")
    for child in children:
        if child.kind == b"meta":
            _metadata(data, child)


def _rewrite(data: bytes, atom: Atom, removed: set[int], delta: int, extent: tuple[int, int]) -> bytes:
    payload = data[atom.start + atom.header:atom.start + atom.size]
    if atom.kind == b"tref":
        parts = []
        for child in _children(data, atom):
            links = data[child.start + child.header:child.start + child.size]
            if len(links) % 4:
                raise BuildError("MP4 track reference is malformed.")
            ids = list(struct.unpack(">" + "I" * (len(links) // 4), links))
            if any(value in removed for value in ids) and child.kind != b"tmcd":
                raise BuildError("An authored non-timecode track references removed timecode metadata.")
            ids = [value for value in ids if value not in removed]
            if ids:
                parts.append(_box(child.kind, struct.pack(">" + "I" * len(ids), *ids)))
        return _box(atom.kind, b"".join(parts)) if parts else b""
    if atom.kind in (b"stco", b"co64"):
        width, format = (4, ">I") if atom.kind == b"stco" else (8, ">Q")
        if len(payload) < 8 or payload[:4] != b"\0\0\0\0":
            raise BuildError("MP4 chunk offset table is malformed.")
        count = struct.unpack_from(">I", payload, 4)[0]
        if len(payload) != 8 + count * width:
            raise BuildError("MP4 chunk offset table is truncated.")
        changed = bytearray(payload)
        for index in range(count):
            offset = struct.unpack_from(format, payload, 8 + index * width)[0]
            target = offset + delta
            if not extent[0] <= offset < extent[1] or not 0 <= target < 2**(width * 8):
                raise BuildError("MP4 sample offsets escape the retained media payload.")
            struct.pack_into(format, changed, 8 + index * width, target)
        return _box(atom.kind, changed)
    if atom.kind in (b"trak", b"mdia", b"minf", b"stbl"):
        allowed = {
            b"trak": {b"tkhd", b"tref", b"edts", b"mdia"},
            b"mdia": {b"mdhd", b"hdlr", b"minf"},
            b"minf": {b"vmhd", b"smhd", b"dinf", b"stbl"},
            b"stbl": {b"stsd", b"stts", b"stss", b"ctts", b"stsc", b"stsz", b"stz2", b"stco", b"co64", b"sgpd", b"sbgp"},
        }
        if any(child.kind not in allowed[atom.kind] for child in _children(data, atom)):
            raise BuildError("MP4 timecode adaptation found unsupported AV metadata; offset safety is unknown.")
        return _box(atom.kind, b"".join(_rewrite(data, child, removed, delta, extent) for child in _children(data, atom)))
    if atom.kind == b"dinf":
        reference = _one(_children(data, atom), b"dref")
        payload = data[reference.start + reference.header:reference.start + reference.size]
        if len(payload) < 8 or payload[:4] != b"\0\0\0\0":
            raise BuildError("MP4 data references are malformed.")
        references = _atoms(payload, 8)
        if struct.unpack_from(">I", payload, 4)[0] != len(references) or not references:
            raise BuildError("MP4 data reference count is invalid.")
        if any(a.kind != b"url " or payload[a.start + a.header:a.start + a.size] != b"\0\0\0\1" for a in references):
            raise BuildError("MP4 timecode adaptation requires self-contained media data.")
    return data[atom.start:atom.start + atom.size]


def _plan(source: Path) -> tuple[list[bytes | Atom] | None, dict]:
    top, movie = _top(source)
    if movie is None:
        return None, {}
    root = _one(_atoms(movie), b"moov")
    children = _children(movie, root)
    tracks = [(a, *_track(movie, a)) for a in children if a.kind == b"trak"]
    removed = {identity for atom, identity, handler in tracks if handler == b"tmcd"}
    if not removed:
        return None, {}
    if any(a.kind not in (b"mvhd", b"trak", b"udta", b"meta") for a in children):
        raise BuildError("MP4 timecode adaptation found unsupported movie metadata.")
    for child in children:
        if child.kind in (b"udta", b"meta"):
            _metadata(movie, child)
    if len({identity for atom, identity, handler in tracks}) != len(tracks) or any(identity == 0 for atom, identity, handler in tracks):
        raise BuildError("MP4 track identities are invalid or duplicated.")
    if any(handler not in (b"vide", b"soun", b"tmcd") for atom, identity, handler in tracks):
        raise BuildError("MP4 timecode adaptation found another authored track; preserve it explicitly before building.")
    retained = [(a, identity, handler) for a, identity, handler in tracks if handler != b"tmcd"]
    if not any(handler == b"vide" for atom, identity, handler in retained):
        raise BuildError("MP4 timecode adaptation requires retained video.")
    if any(a.kind not in (b"ftyp", b"free", b"wide", b"skip", b"moov", b"mdat") for a in top):
        raise BuildError("MP4 timecode adaptation does not support this top-level container shape.")
    media = _one(top, b"mdat")
    extent = (media.start + media.header, media.start + media.size)
    def build(delta):
        return _box(b"moov", b"".join(_rewrite(movie, a, removed, delta, extent)
            if a.kind == b"trak" else movie[a.start:a.start + a.size]
            for a in children if a.kind != b"trak" or _track(movie, a)[0] not in removed))
    provisional = build(0)
    before = sum(a.size for a in top if a.start < media.start and a.kind != b"moov")
    delta = before + len(provisional) - media.start
    adapted = build(delta)
    parts, inserted = [], False
    for atom in top:
        if atom.kind == b"moov":
            continue
        if atom.kind == b"mdat":
            parts.append(adapted); inserted = True
        parts.append(atom)
    if not inserted:
        raise BuildError("Adapted MP4 has no media payload.")
    # Reversing only the relocated offsets must yield exact original AV metadata
    # after removing its ancillary timecode link. Timelines/codec/sample tables
    # are otherwise byte-identical, and the complete mdat is copied unchanged.
    new_tracks = [a for a in _children(adapted, _one(_atoms(adapted), b"moov")) if a.kind == b"trak"]
    if len(new_tracks) != len(retained):
        raise BuildError("Adapted MP4 lost an authored AV track.")
    fingerprints = []
    for (old, identity, handler), new in zip(retained, new_tracks):
        unchanged = _rewrite(movie, old, removed, 0, extent)
        normalized = _rewrite(adapted, new, set(), -delta, (extent[0] + delta, extent[1] + delta))
        if unchanged != normalized:
            raise BuildError("Adapted MP4 changed authored AV metadata or timeline.")
        fingerprints.append({"trackId": identity, "handler": handler.decode("ascii"), "metadataSha256": hashlib.sha256(unchanged).hexdigest()})
    return parts, {"removedTimecodeTrackIds": sorted(removed), "chunkOffsetDelta": delta,
                   "moovBeforeMdat": True, "avMetadata": fingerprints, "mediaPayloadUnchanged": True}


def _chunks(source, parts):
    for part in parts:
        if isinstance(part, bytes):
            yield part
            continue
        source.seek(part.start); remaining = part.size
        while remaining:
            data = source.read(min(remaining, 1024 * 1024))
            if not data:
                raise BuildError("Original media changed/truncated while staging.")
            remaining -= len(data)
            yield data


def stage_media(source: Path, target: Path, relative: str) -> dict:
    """Atomically deliver original bytes or an explicitly derived AV-identical MP4."""
    if source.is_symlink() or not source.is_file() or any(parent.is_symlink() for parent in source.parents):
        raise BuildError("Original movie source is missing or linked: " + relative)
    if any(parent.is_symlink() for parent in (target, *target.parents)):
        raise BuildError("Generated movie destinations must not be symbolic links: " + relative)
    if source.resolve() == target.resolve() or (target.exists() and not target.is_file()):
        raise BuildError("Movie delivery needs a separate generated file destination: " + relative)
    original = record_file(source, relative)
    parts, proof = _plan(source)
    recipe = RECIPE if parts is not None else "original"
    if parts is None:
        parts = [Atom(b"file", 0, original["size"], 0)]
    expected, size = original["sha256"], original["size"]
    if recipe != "original":
        expected, size = hashlib.sha256(), 0
        with source.open("rb") as stream:
            for data in _chunks(stream, parts):
                expected.update(data); size += len(data)
        expected = expected.hexdigest()
    if not target.is_file() or target.stat().st_size != size or digest(target) != expected:
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = target.with_name(target.name + ".tmp-" + uuid.uuid4().hex)
        try:
            with source.open("rb") as stream, temporary.open("xb") as output:
                for data in _chunks(stream, parts):
                    output.write(data)
            if temporary.stat().st_size != size or digest(temporary) != expected or digest(source) != original["sha256"]:
                raise BuildError("Original/derived movie changed during delivery.")
            temporary.replace(target)
        finally:
            temporary.unlink(missing_ok=True)
    elif digest(source) != original["sha256"]:
        raise BuildError("Original movie changed during delivery.")
    return {"path": relative, "sha256": expected, "size": size, "delivery": recipe,
            "originalSha256": original["sha256"], "originalSize": original["size"], "mediaProof": proof}
