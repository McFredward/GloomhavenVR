"""Exercise original sample reconstruction and refuse unproven export changes."""
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import audio
from storage import BuildError


def clip(name="UI", *, channels=4, frequency=48000, frames=16, compression=0):
    return {"name": name, "channels": channels, "frequency": frequency, "duration": frames / frequency,
            "compression": compression, "loadType": 0 if compression == 0 else 1,
            "resource": "resources.resource", "offset": 0, "size": 0, "pathId": 1}


def pcm(frames=16, channels=4):
    # Distinct channels and nonrepeating frames detect incorrect deinterleaving,
    # truncation, downmixing and normalized/reordered output samples.
    return struct.pack("<" + str(frames * channels) + "h", *[i * 73 - 1000 for i in range(frames * channels)])


def bank(payload, item, *, extension=True, encoding=2, frames=None):
    channels, frequency = item["channels"], item["frequency"]
    sample = ((frames if frames is not None else len(payload) // (channels * 2)) << 34) | (audio.FSB_RATES.index(frequency) << 1)
    if extension:
        sample |= 1
    elif channels == 2:
        sample |= 32
    ext = struct.pack("<I", (1 << 25) | (1 << 1)) + bytes([channels]) if extension else b""
    headers = struct.pack("<Q", sample) + ext
    headers += b"\0" * ((-len(headers)) % 4)
    padded = payload + b"\0" * ((-len(payload)) % 32)
    return b"FSB5" + struct.pack("<6I", 1, 1, len(headers), 0, len(padded), encoding) + b"\0" * 32 + headers + padded


def text(value):
    raw = value.encode()
    return struct.pack("<i", len(raw)) + raw + b"\0" * ((-len(raw)) % 4)


def serialized(items):
    data = bytearray()
    table = bytearray()
    for identity, item in enumerate(items, 1):
        obj = text(item["name"]) + struct.pack("<iiiif", item["loadType"], item["channels"], item["frequency"], 16, item["duration"])
        obj += b"\0" * 4 + struct.pack("<i", 0) + b"\0\1\1\0"
        obj += text(item["resource"]) + struct.pack("<QQi", item["offset"], item["size"], item["compression"])
        table += struct.pack("<qQIi", identity, len(data), len(obj), 0)
        data += obj
    metadata = b"2021.3.5f1\0" + struct.pack("<iBi", 19, 0, 1)
    metadata += struct.pack("<iBh", 83, 0, -1) + audio.AUDIO_TYPE_HASH
    metadata += struct.pack("<i", len(items))
    metadata += b"\0" * ((-len(metadata)) % 4) + table
    offset = (48 + len(metadata) + 15) & ~15
    header = struct.pack(">4IB3sIQQQ", len(metadata), 0, 22, 0, 0, b"\0" * 3, len(metadata), offset + len(data), offset, 0)
    return header + metadata + b"\0" * (offset - 48 - len(metadata)) + data


class OriginalAudioTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.game, self.project = self.root / "owned", self.root / "generated"
        self.game.mkdir(); (self.project / "Assets/AudioClip").mkdir(parents=True)

    def tearDown(self):
        self.temp.cleanup()

    def fixture(self, *, corrupt_second=False):
        original, music = clip(), clip("MenuMusic", channels=2, compression=2, frames=64)
        raw = bank(pcm(), original)
        original["size"] = len(raw)
        encoded_music = bank(b"\0" * 72, music, extension=False, encoding=7, frames=64)
        music["offset"], music["size"] = len(raw), len(encoded_music)
        (self.game / "resources.assets").write_bytes(serialized([original, music]))
        (self.game / "resources.resource").write_bytes(raw + encoded_music)
        broken = audio.wave(pcm()[:len(pcm()) // 4], 1, 48000)
        good_music = bytearray(audio.wave(b"\0" * 256, 2, 48000))
        good_music[4:8] = b"\0" * 4; good_music[40:44] = b"\0" * 4
        if corrupt_second: good_music[40] = 1
        for number, item, exported in [(1, original, broken), (2, music, good_music)]:
            path = self.project / ("Assets/AudioClip/" + item["name"] + ".wav")
            path.write_bytes(exported)
            Path(str(path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + str(number) * 32 + "\nAudioImporter:\n  originalSettings: retained\n")
        return original, music

    def test_complete_quad_pcm_and_music_lengths_preserve_original_samples(self):
        original, music = self.fixture()
        owned = {p.name: p.read_bytes() for p in self.game.iterdir()}
        metas = {p.name: p.read_bytes() for p in (self.project / "Assets/AudioClip").glob("*.meta")}
        report = audio.stage_startup_audio(self.project, self.game)
        self.assertEqual(report["deliveryCounts"], {"restore-original-quad-pcm16": 1, "repair-zero-wave-lengths": 1})
        self.assertFalse(report["playbackModified"]); self.assertFalse(report["resampled"]); self.assertFalse(report["downmixed"])
        for item in (original, music):
            output = (self.project / ("Assets/AudioClip/" + item["name"] + ".wav")).read_bytes()
            self.assertEqual(output, audio.wave(pcm() if item is original else b"\0" * 256, item["channels"], 48000))
        self.assertEqual(owned, {p.name: p.read_bytes() for p in self.game.iterdir()})
        self.assertEqual(metas, {p.name: p.read_bytes() for p in (self.project / "Assets/AudioClip").glob("*.meta")})
        runtime = json.loads((self.project / audio.RESOURCE).read_text())
        self.assertEqual(runtime["clips"][0]["samples"], 16)
        self.assertEqual(runtime["clips"][0]["channels"], 4)
        self.assertEqual(runtime["clips"][1]["loadType"], 1)
        self.assertEqual(report["sourceAssetsSha256"], hashlib.sha256(owned["resources.assets"]).hexdigest())
        self.assertEqual(json.loads((self.project / audio.REPORT).read_text()), report)
        # A repaired generated project remains sample-identical on a later call.
        before = {p.name: p.read_bytes() for p in (self.project / "Assets/AudioClip").iterdir()}
        audio.stage_startup_audio(self.project, self.game)
        self.assertEqual(before, {p.name: p.read_bytes() for p in (self.project / "Assets/AudioClip").iterdir()})

    def test_unknown_defect_preflights_all_assets_before_any_mutation(self):
        self.fixture(corrupt_second=True)
        before = {p.name: p.read_bytes() for p in (self.project / "Assets/AudioClip").iterdir()}
        with self.assertRaisesRegex(BuildError, "unrecognized length"):
            audio.stage_startup_audio(self.project, self.game)
        self.assertEqual(before, {p.name: p.read_bytes() for p in (self.project / "Assets/AudioClip").iterdir()})
        self.assertFalse((self.project / audio.REPORT).exists())

    def test_single_and_stereo_pcm_keep_authored_payload_and_rate(self):
        for channels in (1, 2, 4):
            for frequency in (44100, 48000):
                with self.subTest(channels=channels, frequency=frequency):
                    item = clip(channels=channels, frequency=frequency)
                    payload = pcm(channels=channels)
                    raw = bank(payload, item, extension=channels == 4)
                    self.assertEqual(audio.pcm16_fsb(raw, item), payload)
                    original = audio.wave(payload, channels, frequency)
                    self.assertEqual(audio.restore_wave(original, item, payload), (original, "original-pcm16"))

    def test_original_fsb_metadata_corruption_is_rejected(self):
        item = clip(); raw = bank(pcm(), item)
        controls = [(4, "<I", 2), (8, "<I", 2), (16, "<I", 1), (24, "<I", 7),
                    (60, "<Q", 0), (68, "<I", 3 << 25), (72, "<B", 2)]
        for offset, fmt, value in controls:
            with self.subTest(offset=offset, value=value):
                bad = bytearray(raw); struct.pack_into(fmt, bad, offset, value)
                with self.assertRaises(BuildError): audio.pcm16_fsb(bytes(bad), item)
        with self.assertRaises(BuildError): audio.pcm16_fsb(raw[:-1], item)
        bad = bytearray(raw); bad[-1] = 1
        # Add trailing nonzero padding to a bank whose payload naturally aligns.
        bad = raw + b"\1" * 32; bad = bytearray(bad); struct.pack_into("<I", bad, 20, len(pcm()) + 32)
        with self.assertRaisesRegex(BuildError, "disagree"): audio.pcm16_fsb(bytes(bad), item)

    def test_ima_quad_predictors_and_complete_timeline_retain_every_channel(self):
        item = clip(channels=4, compression=2, frames=64)
        values = (-3000, -1000, 1000, 3000)
        encoded = struct.pack("<4h", *values) + b"\0" * 8 + b"\0" * 128
        raw = bank(encoded, item, encoding=7, frames=64)
        expected = struct.pack("<256h", *(values * 64))
        self.assertEqual(audio.ima_fsb(raw, item), expected)
        truncated = bytearray(audio.wave(expected[:len(expected) // 4], 1, 48000))
        truncated[4:8] = b"\0" * 4; truncated[40:44] = b"\0" * 4
        restored, recipe = audio.restore_wave(bytes(truncated), item, expected)
        self.assertEqual(restored, audio.wave(expected, 4, 48000))
        self.assertEqual(recipe, "restore-original-quad-ima-adpcm")

    def test_ima_step_sign_clamp_and_block_boundaries(self):
        item = clip(channels=2, compression=2, frames=128)
        positive = struct.pack("<hBBhBB", 32760, 88, 0, -32760, 88, 0) + (b"\x77" * 4 + b"\xff" * 4) * 8
        negative = struct.pack("<hBBhBB", 7, 0, 0, -7, 0, 0) + (b"\x00" * 4 + b"\x88" * 4) * 8
        raw = bank(positive + negative, item, extension=False, encoding=7, frames=128)
        values = struct.unpack("<256h", audio.ima_fsb(raw, item))
        self.assertEqual(values[:6], (32760, -32760, 32767, -32768, 32767, -32768))
        self.assertEqual(values[128:], (7, -7) * 64)
        for offset, value in [(68 + 2, 89), (68 + 3, 1)]:
            bad = bytearray(raw); bad[offset] = value
            with self.assertRaisesRegex(BuildError, "predictor"):
                audio.ima_fsb(bytes(bad), item)
        short = clip(channels=2, compression=2, frames=63)
        with self.assertRaisesRegex(BuildError, "frame blocks"):
            audio.ima_fsb(bank(positive, short, extension=False, encoding=7, frames=63), short)

    def test_inexact_quad_truncation_does_not_get_accepted_as_recovery(self):
        item = clip(); payload = pcm(); recovered = bytearray(audio.wave(payload[:len(payload) // 4], 1, 48000))
        recovered[-1] ^= 1
        with self.assertRaisesRegex(BuildError, "proven extended-channel"):
            audio.restore_wave(bytes(recovered), item, payload)
        with self.assertRaises(BuildError): audio.restore_wave(audio.wave(payload[::4], 1, 48000), item, payload)
        with self.assertRaises(BuildError): audio.restore_wave(audio.wave(payload, 1, 48000), item, payload)

    def test_only_zero_size_words_can_change_in_decoded_music(self):
        item = clip(channels=2, compression=2)
        authored = audio.wave(pcm(channels=2), 2, 48000)
        broken = bytearray(authored); broken[4:8] = b"\0" * 4; broken[40:44] = b"\0" * 4
        restored, recipe = audio.restore_wave(bytes(broken), item, None)
        self.assertEqual(restored, authored); self.assertEqual(restored[44:], broken[44:])
        self.assertEqual(recipe, "repair-zero-wave-lengths")
        broken[4] = 1
        with self.assertRaisesRegex(BuildError, "unrecognized length"):
            audio.restore_wave(bytes(broken), item, None)

    def test_unknown_wave_formats_rates_channels_and_timeline_are_rejected(self):
        item = clip(channels=2, compression=2); authored = audio.wave(pcm(channels=2), 2, 48000)
        for offset, fmt, value in [(16, "<I", 18), (20, "<H", 3), (22, "<H", 1), (24, "<I", 44100), (32, "<H", 1), (34, "<H", 8)]:
            with self.subTest(offset=offset):
                bad = bytearray(authored); struct.pack_into(fmt, bad, offset, value)
                with self.assertRaises(BuildError): audio.restore_wave(bytes(bad), item, None)
        with self.assertRaises(BuildError): audio.restore_wave(authored[:-2], item, None)
        with self.assertRaises(BuildError): audio.restore_wave(b"not a WAVE", item, None)

    def test_original_serialized_reader_refuses_unpinned_schema_and_ranges(self):
        self.fixture(); path = self.game / "resources.assets"; original = path.read_bytes()
        for bad in [original[:20], original.replace(b"2021.3.5f1", b"2021.3.6f1"),
                    original.replace(audio.AUDIO_TYPE_HASH, b"x" * 16),
                    original.replace(b"resources.resource", b"resources/../filex")]:
            with self.subTest(size=len(bad)):
                path.write_bytes(bad)
                with self.assertRaises(BuildError): audio.original_clips(path)
        path.write_bytes(original)
        clips = audio.original_clips(path)
        self.assertEqual(len(clips), 2); self.assertEqual(clips[0]["channels"], 4)
        bad = bytearray(original); struct.pack_into(">I", bad, 8, 21); path.write_bytes(bad)
        with self.assertRaises(BuildError): audio.original_clips(path)

    def test_generated_paths_cannot_modify_source_or_follow_links(self):
        self.fixture()
        with self.assertRaisesRegex(BuildError, "separate"):
            audio.stage_startup_audio(self.game, self.game)
        linked = self.root / "linked"; linked.symlink_to(self.project, target_is_directory=True)
        with self.assertRaisesRegex(BuildError, "symbolic links"):
            audio.stage_startup_audio(linked, self.game)
        source = self.project / "Assets/AudioClip/UI.wav"
        source.unlink(); source.symlink_to(self.game / "resources.assets")
        with self.assertRaisesRegex(BuildError, "linked"):
            audio.stage_startup_audio(self.project, self.game)


if __name__ == "__main__":
    unittest.main()
