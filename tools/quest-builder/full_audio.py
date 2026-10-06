"""Recover original bundled audio with actual FSB channel-extension evidence."""
import hashlib
import json
from pathlib import Path
import struct
import sys

from storage import BuildError, write_json, ImmutableFileHashes


def ogg_packets(data):
    position, partial, packets = 0, bytearray(), []
    while position < len(data):
        if position + 27 > len(data) or data[position:position + 4] != b"OggS" or data[position + 4] != 0:
            raise BuildError("Native Vorbis reconstruction is not a complete Ogg stream.")
        count = data[position + 26]
        sizes = data[position + 27:position + 27 + count]
        if len(sizes) != count or position + 27 + count + sum(sizes) > len(data):
            raise BuildError("Native Vorbis Ogg page is truncated.")
        payload = position + 27 + count
        for size in sizes:
            partial.extend(data[payload:payload + size]); payload += size
            if size < 255:
                packets.append(bytes(partial)); partial.clear()
        position = payload
    if partial or position != len(data):
        raise BuildError("Native Vorbis Ogg packets are truncated.")
    return packets


def fsb_vorbis(raw, expected):
    if raw[:4] != b"FSB5" or len(raw) < 68:
        raise BuildError("Bundled original audio is not a complete FSB5 bank.")
    version, count, headers, names, size, mode = struct.unpack_from("<6I", raw, 4)
    if version != 1 or count != 1 or mode != 15 or names != 0 or 60 + headers + size != len(raw):
        raise BuildError("Bundled audio needs a witnessed single-sample native Vorbis bank.")
    sample = struct.unpack_from("<Q", raw, 60)[0]
    rates = (4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000)
    if ((sample >> 1) & 15) >= len(rates) or (sample >> 7) & 0x7ffffff:
        raise BuildError("Bundled FSB frequency/sample offset changed.")
    rate = rates[(sample >> 1) & 15]
    channels, frames = 2 if sample & 32 else 1, sample >> 34
    cursor, more, extension_seen = 68, bool(sample & 1), False
    while more:
        if cursor + 4 > 60 + headers:
            raise BuildError("Bundled FSB extension header is truncated.")
        value = struct.unpack_from("<I", raw, cursor)[0]; cursor += 4
        more, length, kind = bool(value & 1), (value >> 1) & 0xffffff, value >> 25
        if cursor + length > 60 + headers:
            raise BuildError("Bundled FSB channel/setup extension is truncated.")
        if kind == 1:
            if length != 1 or extension_seen:
                raise BuildError("Bundled FSB has an unknown repeated channel extension.")
            channels, extension_seen = raw[cursor], True
        elif kind != 11:
            raise BuildError("Bundled Vorbis FSB has an unwitnessed sample extension.")
        cursor += length
    if cursor > 60 + headers or channels != expected["m_Channels"] or rate != expected["m_Frequency"] or abs(frames - rate * expected["m_Length"]) > 2:
        raise BuildError("Bundled FSB disagrees with its exact native AudioClip fields.")
    payload, packets, position = raw[60 + headers:], [], 0
    while position + 2 <= len(payload):
        length = struct.unpack_from("<H", payload, position)[0]; position += 2
        if length == 0:
            break
        if position + length > len(payload):
            raise BuildError("Original FSB Vorbis packet is truncated.")
        packets.append(payload[position:position + length]); position += length
    if any(payload[position:]):
        raise BuildError("Original FSB has nonzero unconsumed Vorbis packet bytes.")
    return channels, rate, frames, packets


def stage(project, game_data, *, dotnet, tool_cache, cab_bundles):
    """Return actual native bundled-audio receipts; change the generated copy."""
    recovery = Path(__file__).resolve().parents[1] / "quest-recovery"
    if str(recovery) not in sys.path: sys.path.append(str(recovery))
    from export_identity import object_index
    from pointer_recovery import load_native
    from recover import sha256
    import portable_decoder
    import UnityPy
    project, game_data = Path(project), Path(game_data)
    rows = json.loads((project / "QuestRecovery/original-asset-identities.json").read_text())["identities"]
    objects = object_index(rows)
    owners = json.loads(Path(cab_bundles).read_text()) if isinstance(cab_bundles, (str, Path)) else cab_bundles
    owners = {key.casefold(): value for key, value in owners.items()}
    wanted = [obj for obj in objects.values() if obj["classId"] == 83 and obj["collection"].startswith("cab-")]
    command = portable_decoder.build(tool_cache, dotnet)
    assets, environments = [], {}
    source_hashes = ImmutableFileHashes()
    temporary = Path(tool_cache) / "original-audio-payloads"
    temporary.mkdir(exist_ok=True)
    for target in wanted:
        container = owners[target["collection"]]
        if container not in environments: environments[container] = load_native(UnityPy, game_data / container)
        env = environments[container]
        native = [obj for obj in env.objects if (obj.assets_file.name.casefold(), int(obj.path_id)) == (target["collection"], target["pathId"])]
        if len(native) != 1: raise BuildError("Bundled audio lost its exact original native identity.")
        fields = native[0].read_typetree(); resource = fields["m_Resource"]
        if fields["m_CompressionFormat"] != 1:
            raise BuildError("Bundled AudioClip compression changed; audit before reconstruction.")
        bundle = next(iter(env.files.values()))
        resource_name = resource["m_Source"].replace("\\", "/").rsplit("/", 1)[-1]
        reader = bundle.files[resource_name]
        reader.Position = resource["m_Offset"]
        raw = reader.read_bytes(resource["m_Size"])
        channels, rate, frames, original_packets = fsb_vorbis(raw, fields)
        input_path = temporary / (target["guid"] + ".fsb")
        output_path = temporary / (target["guid"] + ".ogg")
        input_path.write_bytes(raw)
        portable_decoder.execute(command, ("fsb", input_path, output_path, channels))
        corrected = output_path.read_bytes(); packets = ogg_packets(corrected)
        if not packets or packets[0][:7] != b"\x01vorbis" or packets[0][11] != channels or struct.unpack_from("<I", packets[0], 12)[0] != rate:
            raise BuildError("Rebuilt native Vorbis header lost actual channels/frequency.")
        if packets[3:] != original_packets:
            raise BuildError("Native Vorbis repair changed original compressed audio packets.")
        path = project / target["path"]
        before = sha256(path)
        path.write_bytes(corrected)
        assets.append({"assetPath": target["path"], "guid": target["guid"], "fileId": target["fileId"],
              "originalCollection": target["collection"], "originalPathId": target["pathId"],
              "sourceContainer": container, "sourceContainerSha256": source_hashes.digest(game_data / container),
              "originalFsbSha256": hashlib.sha256(raw).hexdigest(), "beforeSha256": before, "sha256": sha256(path),
              "channels": channels, "frequency": rate, "samples": frames,
              "compressedAudioPacketsPreserved": True, "originalCompressedPacketCount": len(original_packets),
              "source": "original-native-FSB-channel-extension-and-unaltered-Vorbis-packets"})
        input_path.unlink(); output_path.unlink()
    receipt = {"schema": 1, "bundledAudioClipCount": len(assets), "assets": assets,
               "unityImportVerified": False, "headsetAudioVerified": False}
    write_json(project / "Assets/QuestOriginalCampaign/bundled-audio.json", receipt)
    return receipt
