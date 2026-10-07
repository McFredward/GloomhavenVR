"""Restore owned audio at the export boundary, without changing playback.

AssetRipper's PCM16 FSB exporter ignores the extended channel-count field. In
the owned player this turns 793 quad clips into the first quarter of the original
interleaved samples labelled mono. Ten decoded ADPCM WAVs also have zero RIFF
and data lengths. B618 hardware reports pitched SFX and absent music. Recover
the exact PCM16 sample bytes, or repair only the two zero length words; never
resample, downmix, normalize, alter a mixer or replace native audio callbacks.
"""
from __future__ import annotations

from array import array
import hashlib
import math
from pathlib import Path
import re
import struct
import sys
import uuid

from storage import BuildError, digest, write_json

REPORT = "quest-startup-audio.json"
RESOURCE = "Assets/Resources/QuestOriginalAudio.json"
MAX_METADATA = 64 * 1024 * 1024
MAX_CLIP = 256 * 1024 * 1024
AUDIO_TYPE_HASH = bytes.fromhex("57a6370a001d5449abd37c2184ec5138")
FSB_RATES = (4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000)
IMA_STEPS = (7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55,
             60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
             337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411,
             1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871,
             5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289,
             16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767)
IMA_INDEX = (-1, -1, -1, -1, 2, 4, 6, 8)


def _ima_transitions():
    result = []
    for index, step in enumerate(IMA_STEPS):
        row = []
        for code in range(16):
            delta = (step >> 3) + ((step >> 2) if code & 1 else 0) + ((step >> 1) if code & 2 else 0) + (step if code & 4 else 0)
            row.append((-delta if code & 8 else delta, max(0, min(88, index + IMA_INDEX[code & 7]))))
        result.append(row)
    return result


IMA_TRANSITIONS = _ima_transitions()


class _Reader:
    def __init__(self, data: bytes):
        self.data, self.position = data, 0

    def take(self, size: int) -> bytes:
        if size < 0 or self.position + size > len(self.data):
            raise BuildError("Original audio metadata is truncated or exceeds its bound.")
        value = self.data[self.position:self.position + size]
        self.position += size
        return value

    def unpack(self, fmt: str):
        return struct.unpack("<" + fmt, self.take(struct.calcsize("<" + fmt)))

    def align(self):
        self.take((-self.position) % 4)

    def count(self, limit: int) -> int:
        value, = self.unpack("i")
        if not 0 <= value <= limit:
            raise BuildError("Original audio metadata count exceeds its bound.")
        return value

    def text(self) -> str:
        raw = self.take(self.count(4096))
        self.align()
        try:
            return raw.decode("utf-8", errors="strict")
        except UnicodeError as error:
            raise BuildError("Original audio metadata contains an invalid string.") from error


def original_clips(source: Path) -> list[dict]:
    """Read only the pinned player's AudioClip objects; no third-party decoder.

    This deliberately accepts the original Unity2021.3.5 little-endian v22
    stripped type-tree layout and AudioClip type hash, rather than guessing a
    different player schema. Other game revisions need an explicit new contract.
    """
    if source.is_symlink() or not source.is_file():
        raise BuildError("Original resources.assets is missing or linked.")
    with source.open("rb") as stream:
        header = stream.read(48)
        if len(header) != 48:
            raise BuildError("Original audio asset header is truncated.")
        version, = struct.unpack_from(">I", header, 8)
        metadata_size, file_size, data_offset = struct.unpack_from(">IQQ", header, 20)
        if (version != 22 or header[16:20] != b"\0\0\0\0" or
                not 0 < metadata_size <= MAX_METADATA or file_size != source.stat().st_size or
                not 48 + metadata_size <= data_offset <= file_size):
            raise BuildError("Original audio assets need the pinned little-endian Unity v22 layout.")
        reader = _Reader(stream.read(metadata_size))
        marker = reader.data.find(b"\0", 0, 64)
        if marker < 0 or reader.take(marker + 1) != b"2021.3.5f1\0":
            raise BuildError("Original audio assets do not identify Unity2021.3.5f1.")
        platform, = reader.unpack("i")
        if platform != 19 or reader.take(1) != b"\0":
            raise BuildError("Original audio assets need the owned Windows stripped type-tree layout.")
        classes = []
        for _ in range(reader.count(4096)):
            class_id, = reader.unpack("i")
            stripped = reader.take(1)
            reader.take(2)  # Script type index remains original, irrelevant for AudioClip.
            if class_id == 114:
                reader.take(16)
            type_hash = reader.take(16)
            if class_id == 83 and (stripped != b"\0" or type_hash != AUDIO_TYPE_HASH):
                raise BuildError("Original AudioClip serialized type changed; refusing a guessed layout.")
            classes.append(class_id)
        clips, identities = [], set()
        for _ in range(reader.count(1_000_000)):
            reader.align()
            identity, offset, size, type_id = reader.unpack("qQIi")
            if (identity in identities or not 0 <= type_id < len(classes) or
                    offset + size > file_size - data_offset):
                raise BuildError("Original audio object table has an invalid identity or range.")
            identities.add(identity)
            if classes[type_id] != 83:
                continue
            if not 0 < size <= 16384 or len(clips) >= 65536:
                raise BuildError("Original AudioClip object exceeds its bound.")
            stream.seek(data_offset + offset)
            obj = _Reader(stream.read(size))
            name = obj.text()
            if not name or name in (".", "..") or any(c in name for c in "/\\\0"):
                raise BuildError("Original AudioClip name is unsafe.")
            load_type, channels, frequency, bits, duration = obj.unpack("iiiif")
            tracker, ambisonic = obj.unpack("BB")
            obj.align()
            subsound, = obj.unpack("i")
            preload, background, legacy = obj.unpack("BBB")
            obj.align()
            resource = obj.text()
            offset, size, compression = obj.unpack("QQi")
            if (obj.position != len(obj.data) or not 1 <= channels <= 8 or
                    not 8000 <= frequency <= 192000 or bits != 16 or
                    not math.isfinite(duration) or duration < 0 or
                    load_type not in (0, 1, 2) or compression not in (0, 1, 2) or
                    tracker or ambisonic or subsound or preload not in (0, 1) or
                    background not in (0, 1) or legacy not in (0, 1) or
                    resource != "resources.resource" or not 0 < size <= MAX_CLIP):
                raise BuildError("Original AudioClip metadata has an unsupported shape: " + name)
            clips.append({"name": name, "pathId": identity, "channels": channels, "frequency": frequency,
                          "duration": duration, "loadType": load_type, "compression": compression,
                          "resource": resource, "offset": offset, "size": size})
    if not clips or len({item["name"] for item in clips}) != len(clips):
        raise BuildError("Original audio clips are absent or have ambiguous names.")
    return clips


def _fsb(raw: bytes, clip: dict, encoding: int) -> tuple[int, int, int]:
    if len(raw) < 68 or raw[:4] != b"FSB5":
        raise BuildError("Original PCM audio is not an FSB5 bank: " + clip["name"])
    version, count, headers, names, size, mode = struct.unpack_from("<6I", raw, 4)
    data_start = 60 + headers + names
    if (version != 1 or count != 1 or mode != encoding or names != 0 or
            not 8 <= headers <= 4096 or data_start + size != len(raw)):
        raise BuildError("Original PCM FSB5 bank has an unsupported shape: " + clip["name"])
    sample, = struct.unpack_from("<Q", raw, 60)
    rate_code = (sample >> 1) & 15
    channels = 2 if sample & 32 else 1
    if rate_code >= len(FSB_RATES) or ((sample >> 6) & 0x0FFFFFFF) != 0:
        raise BuildError("Original PCM FSB5 rate or data offset is unsupported.")
    more, cursor, channel_override = bool(sample & 1), 68, False
    while more:
        if cursor + 4 > 60 + headers:
            raise BuildError("Original PCM FSB5 extended header is truncated.")
        extension, = struct.unpack_from("<I", raw, cursor)
        cursor += 4
        more, length, kind = bool(extension & 1), (extension >> 1) & 0xFFFFFF, extension >> 25
        if cursor + length > 60 + headers or kind != 1 or length != 1 or channel_override:
            raise BuildError("Original PCM FSB5 has an unsupported extended sample header.")
        channels, channel_override = raw[cursor], True
        cursor += length
    frames = sample >> 34
    if (channels != clip["channels"] or FSB_RATES[rate_code] != clip["frequency"] or
            abs(frames - clip["duration"] * clip["frequency"]) > 2 or
            not 0 < frames * channels * 2 <= MAX_CLIP or any(raw[cursor:60 + headers])):
        raise BuildError("Original PCM FSB5 samples disagree with serialized AudioClip metadata.")
    return data_start, size, frames


def pcm16_fsb(raw: bytes, clip: dict) -> bytes:
    """Extract complete original interleaved samples from a single-sample bank."""
    data_start, size, frames = _fsb(raw, clip, 2)
    pcm_size = frames * clip["channels"] * 2
    if not 0 < pcm_size <= size or size - pcm_size >= 256 or any(raw[data_start + pcm_size:]):
        raise BuildError("Original PCM FSB5 samples disagree with serialized AudioClip metadata.")
    return raw[data_start:data_start + pcm_size]


def ima_fsb(raw: bytes, clip: dict) -> bytes:
    """Decode the original FMOD IMA blocks, retaining every authored channel.

    Mono/stereo use Xbox IMA; multichannel FSB IMA separates predictor and index
    tables and uses two-byte channel groups. The predictor plus 63 deltas
    produces 64 frames; the final nibble is unused. This is the original codec's
    integer decoding, not a resample or a new lossy encoding. Native FMOD output
    serves as an independent byte-for-byte fixture outside public game assets.
    Layout reference: https://github.com/vgmstream/vgmstream/blob/master/src/coding/ima_decoder.c
    """
    data_start, size, frames = _fsb(raw, clip, 7)
    channels = clip["channels"]
    encoded = frames // 64 * channels * 36
    if frames % 64 or not 0 < encoded <= size or size - encoded >= 256 or any(raw[data_start + encoded:]):
        raise BuildError("Original IMA FSB5 frame blocks disagree with the declared timeline.")
    output = array("h", [0]) * (frames * channels)
    for block in range(frames // 64):
        start = data_start + block * channels * 36
        base = block * 64 * channels
        for channel in range(channels):
            if channels > 2:
                predictor, = struct.unpack_from("<h", raw, start + channel * 2)
                index, reserved = struct.unpack_from("<BB", raw, start + channels * 2 + channel * 2)
            else:
                predictor, index, reserved = struct.unpack_from("<hBB", raw, start + channel * 4)
            if index > 88 or reserved:
                raise BuildError("Original IMA FSB5 block predictor has an invalid index.")
            output[base + channel] = predictor
            for frame in range(1, 64):
                nibble = frame - 1
                group = 2 if channels > 2 else 4
                byte = raw[start + channels * 4 + (nibble // (group * 2)) * channels * group
                           + channel * group + (nibble % (group * 2)) // 2]
                code = (byte >> (4 * (nibble % 2))) & 15
                delta, index = IMA_TRANSITIONS[index][code]
                predictor += delta
                if predictor > 32767: predictor = 32767
                elif predictor < -32768: predictor = -32768
                output[base + frame * channels + channel] = predictor
    if sys.byteorder != "little":
        output.byteswap()
    return output.tobytes()


def wave(pcm: bytes, channels: int, frequency: int) -> bytes:
    align = channels * 2
    fmt = struct.pack("<HHIIHH", 1, channels, frequency, frequency * align, align, 16)
    return b"RIFF" + struct.pack("<I", len(pcm) + 36) + b"WAVEfmt " + struct.pack("<I", 16) + fmt + b"data" + struct.pack("<I", len(pcm)) + pcm


def restore_wave(exported: bytes, clip: dict, original_pcm: bytes | None) -> tuple[bytes, str]:
    if (len(exported) < 44 or len(exported) > MAX_CLIP or exported[:4] != b"RIFF" or
            exported[8:16] != b"WAVEfmt " or exported[36:40] != b"data"):
        raise BuildError("Recovered WAV has an unsupported PCM container: " + clip["name"])
    riff_size, fmt_size = struct.unpack_from("<I", exported, 4)[0], struct.unpack_from("<I", exported, 16)[0]
    encoding, channels, rate, byte_rate, align, bits = struct.unpack_from("<HHIIHH", exported, 20)
    data_size, = struct.unpack_from("<I", exported, 40)
    payload = exported[44:]
    if (fmt_size != 16 or encoding != 1 or bits != 16 or rate != clip["frequency"] or
            channels not in (1, 2, 4, 6, 8) or align != channels * 2 or
            byte_rate != rate * align or len(payload) % align):
        raise BuildError("Recovered WAV sample format is inconsistent: " + clip["name"])
    if original_pcm is not None:
        corrected = wave(original_pcm, clip["channels"], rate)
        if exported == corrected:
            return exported, "original-pcm16" if clip["compression"] == 0 else "decoded-original-ima-adpcm"
        # Recover only the demonstrated channel-extension defect. PCM exports
        # copied an exact prefix without deinterleaving. Quad ADPCM exports also
        # decoded the four-channel block layout as mono; recover those complete
        # samples from the original encoded bank instead of trusting that decode.
        lengths_valid = riff_size == len(exported) - 8 and data_size == len(payload)
        lengths_zero = clip["compression"] == 2 and riff_size == 0 and data_size == 0
        if (clip["channels"] != 4 or channels != 1 or not (lengths_valid or lengths_zero) or len(original_pcm) != len(payload) * 4 or
                (clip["compression"] == 0 and not original_pcm.startswith(payload)) or
                (clip["compression"] == 2 and not lengths_zero)):
            raise BuildError("Recovered PCM WAV is not the proven extended-channel export defect: " + clip["name"])
        return corrected, "restore-original-quad-pcm16" if clip["compression"] == 0 else "restore-original-quad-ima-adpcm"
    if channels != clip["channels"] or abs(len(payload) / align - clip["duration"] * rate) > 2:
        raise BuildError("Recovered decoded WAV disagrees with the original AudioClip timeline.")
    if riff_size == len(exported) - 8 and data_size == len(payload):
        return exported, "decoded-pcm16"
    if riff_size != 0 or data_size != 0:
        raise BuildError("Recovered decoded WAV has an unrecognized length defect.")
    corrected = bytearray(exported)
    struct.pack_into("<I", corrected, 4, len(exported) - 8)
    struct.pack_into("<I", corrected, 40, len(payload))
    return bytes(corrected), "repair-zero-wave-lengths"


def stage_startup_audio(project: Path, game: Path) -> dict:
    project, game = Path(project).absolute(), Path(game).absolute()
    for root in (project, game):
        if any(parent.is_symlink() for parent in (root, *root.parents)):
            raise BuildError("Audio staging roots must not contain symbolic links.")
    project, game = project.resolve(), game.resolve()
    if project == game or game in project.parents or project in game.parents:
        raise BuildError("Audio staging needs a generated project separate from the owned game.")
    source = game / "resources.assets"
    clips = original_clips(source)
    bank = game / "resources.resource"
    if not bank.is_file() or bank.is_symlink():
        raise BuildError("Original audio resource bank is missing or linked.")
    assets, plans, counts = [], [], {}
    before = (bank.stat().st_size, bank.stat().st_mtime_ns, source.stat().st_size, source.stat().st_mtime_ns)
    with bank.open("rb") as stream:
        for clip in clips:
            if clip["compression"] == 1:
                continue  # Existing original Ogg exports are outside these two PCM defects.
            relative = "Assets/AudioClip/" + clip["name"] + ".wav"
            path = project / relative
            meta = Path(str(path) + ".meta")
            if (not path.is_file() or path.is_symlink() or not meta.is_file() or meta.is_symlink() or
                    any(parent.is_symlink() for parent in path.parents)):
                raise BuildError("A recovered audio asset is absent or linked: " + relative)
            metadata = meta.read_bytes()
            guids = re.findall(rb"^guid: ([0-9a-f]{32})$", metadata, re.MULTILINE)
            if len(guids) != 1 or b"AudioImporter:\n" not in metadata:
                raise BuildError("A recovered audio asset lost its original importer GUID.")
            exported = path.read_bytes()
            pcm, raw_hash = None, None
            if clip["compression"] in (0, 2):
                if clip["offset"] + clip["size"] > bank.stat().st_size:
                    raise BuildError("Original PCM audio resource span exceeds the bank.")
                stream.seek(clip["offset"])
                raw = stream.read(clip["size"])
                if clip["compression"] == 0:
                    pcm = pcm16_fsb(raw, clip)
                elif clip["channels"] == 4:
                    pcm = ima_fsb(raw, clip)
                else:
                    _fsb(raw, clip, 7)
                raw_hash = hashlib.sha256(raw).hexdigest()
            corrected, recipe = restore_wave(exported, clip, pcm)
            frames = (len(corrected) - 44) // (clip["channels"] * 2)
            item = {"assetPath": relative, "name": clip["name"], "originalPathId": clip["pathId"],
                    "guid": guids[0].decode("ascii"), "channels": clip["channels"], "frequency": clip["frequency"],
                    "samples": frames, "loadType": clip["loadType"], "delivery": recipe,
                    "exportedSha256": hashlib.sha256(exported).hexdigest(),
                    "sha256": hashlib.sha256(corrected).hexdigest(), "size": len(corrected),
                    "pcmSha256": hashlib.sha256(corrected[44:]).hexdigest(),
                    "metaSha256": hashlib.sha256(metadata).hexdigest(),
                    "originalResourceOffset": clip["offset"], "originalResourceSize": clip["size"],
                    "originalResourceSha256": raw_hash}
            assets.append(item)
            counts[recipe] = counts.get(recipe, 0) + 1
            if corrected != exported:
                plans.append((path, clip, item))
    after = (bank.stat().st_size, bank.stat().st_mtime_ns, source.stat().st_size, source.stat().st_mtime_ns)
    if before != after:
        raise BuildError("Owned audio changed during staging; retry from a stable installation.")
    report = {"schema": 1, "recipe": "original-audio-pcm-boundary-v1", "sourceAssetsSha256": digest(source),
              "originalClipCount": len(clips), "waveClipCount": len(assets), "deliveryCounts": counts, "clips": assets,
              "playbackModified": False, "resampled": False, "downmixed": False}
    # All original/exported associations preflight before changing generated data.
    # Reconstruct one clip at a time instead of retaining hundreds of complete
    # WAVs in RAM. The read-only preflight above already checked every binding.
    with bank.open("rb") as stream:
        for path, clip, evidence in plans:
            exported = path.read_bytes()
            if hashlib.sha256(exported).hexdigest() != evidence["exportedSha256"]:
                raise BuildError("Recovered audio changed after staging preflight.")
            pcm = None
            if clip["compression"] == 0 or clip["channels"] == 4:
                stream.seek(clip["offset"])
                raw = stream.read(clip["size"])
                if hashlib.sha256(raw).hexdigest() != evidence["originalResourceSha256"]:
                    raise BuildError("Owned original audio changed after staging preflight.")
                pcm = pcm16_fsb(raw, clip) if clip["compression"] == 0 else ima_fsb(raw, clip)
            corrected, _ = restore_wave(exported, clip, pcm)
            if hashlib.sha256(corrected).hexdigest() != evidence["sha256"]:
                raise BuildError("Reconstructed original audio changed after staging preflight.")
            temp = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
            try:
                temp.write_bytes(corrected)
                temp.replace(path)
            finally:
                temp.unlink(missing_ok=True)
    write_json(project / REPORT, report)
    write_json(project / RESOURCE, {"schema": 1, "clips": [{k: row[k] for k in
               ("assetPath", "name", "channels", "frequency", "samples", "loadType")} for row in assets]})
    return report
