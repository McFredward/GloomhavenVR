"""Exact RGB/alpha sidecar wire, bounded streaming and independent resume cache."""
import hashlib
import importlib.util
import json
import random
from pathlib import Path
import struct
import tempfile
import types
import unittest
from unittest.mock import patch
import zlib

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_card_pixels_tested", ROOT / "tools/quest-builder/quest_card_pixels.py")
pixels = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pixels)


def unpack(path):
    data = Path(path).read_bytes()
    assert data[:5] == b"GHVP1"
    count = struct.unpack_from("<i", data, 5)[0]
    at, result = 9, {}
    for _ in range(count):
        size = struct.unpack_from("<i", data, at)[0]; at += 4
        name = data[at:at + size].decode(); at += size
        width, height, srgb, size = struct.unpack_from("<iiii", data, at); at += 16
        digest = data[at:at + 32]; at += 32
        rgba = zlib.decompress(data[at:at + size], -15); at += size
        assert hashlib.sha256(rgba).digest() == digest
        result[name] = width, height, srgb, rgba
    assert at == len(data)
    return result


class CardPixelTests(unittest.TestCase):
    def test_normal_lossless_search_fits_artwork_that_fast_search_cannot_fit(self):
        # Card-like repeated motifs at varied offsets exercise DEFLATE's search
        # distance, rather than a zero-filled texture that every preset handles.
        rng = random.Random(177)
        motifs = [rng.randbytes(64) for _ in range(100)]
        rgba = b"".join(rng.choice(motifs) for _ in range(4096))
        def length(level):
            c = zlib.compressobj(level, zlib.DEFLATED, -15)
            return len(c.compress(rgba) + c.flush())
        self.assertLess(length(6), length(1))
        bound = length(6) + 9 + 4 + len("Ability") + 16 + 32
        self.assertGreater(length(1), bound)
        with tempfile.TemporaryDirectory() as directory, patch.object(pixels, "MAX_FILE", bound):
            path = Path(directory) / "pixels.bin"
            pixels.write_entries([("Ability", 256, 256, 1, rgba)], path)
            self.assertEqual(path.stat().st_size, bound)
            self.assertEqual(unpack(path)["Ability"][3], rgba)
            with patch.object(pixels, "COMPRESSION_LEVEL", 1), self.assertRaises(pixels.PixelError):
                pixels.write_entries([("Ability", 256, 256, 1, rgba)], path)

    def test_ambiguous_dead_spool_is_reclaimed_without_redecoding_or_losing_later_payload(self):
        rng = random.Random(984)
        dead, body, tail = rng.randbytes(256), rng.randbytes(2 * 1024 * 1024), rng.randbytes(16)
        def length(data):
            c = zlib.compressobj(6, zlib.DEFLATED, -15)
            return len(c.compress(data) + c.flush())
        bound = length(dead) + length(body)
        self.assertGreater(bound, length(body) + length(tail) + 121)
        entries = [("ambiguous", 8, 8, 1, dead), ("body", 512, 1024, 1, body),
                   ("ambiguous", 8, 8, 1, bytes(reversed(dead))), ("tail", 2, 2, 1, tail),
                   ("ambiguous", 8, 8, 1, dead)]
        with tempfile.TemporaryDirectory() as directory, patch.object(pixels, "MAX_FILE", bound):
            path = Path(directory) / "pixels.bin"
            records, rejected = pixels.write_entries(iter(entries), path)
            actual = unpack(path)
            self.assertEqual(set(actual), {"body", "tail"})
            self.assertEqual(actual["body"][3], body)
            self.assertEqual(actual["tail"][3], tail)
            self.assertEqual(rejected, ["ambiguous"])
            self.assertEqual(len(records), 2)

    def test_unrelated_empty_and_oversized_native_font_textures_are_not_decoded(self):
        class Font:
            m_Name = "Font Texture"
            m_MipCount = 1
            def __init__(self, width): self.m_Width = width; self.m_Height = width
            @property
            def image(self): raise AssertionError("unrelated font pixels were decoded")
        objects = [types.SimpleNamespace(type=types.SimpleNamespace(name="Texture2D"), read=lambda width=width: Font(width))
                   for width in (0, 8192)]
        loader = types.SimpleNamespace(load=lambda path: types.SimpleNamespace(objects=objects))
        with patch.dict("sys.modules", {"UnityPy": loader}):
            self.assertEqual(list(pixels.decode_bundle(Path("misc_gui_assets_all.bundle"))), [])

    def test_exact_bottom_up_rgba_colour_encoding_and_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "pixels.bin"
            rgba = bytes(range(16))
            records, rejected = pixels.write_entries([("Z", 2, 2, 1, rgba), ("A", 1, 1, 0, b"abcd")], path)
            self.assertEqual(list(unpack(path)), ["A", "Z"])
            self.assertEqual(unpack(path)["Z"], (2, 2, 1, rgba))
            self.assertEqual(records[1]["rgbaSha256"], hashlib.sha256(rgba).hexdigest())
            self.assertEqual(rejected, [])

    def test_exact_importer_alias_and_conflicting_native_identity_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "pixels.bin"
            records, rejected = pixels.write_entries([
                ("sactx-DXT5|BC3", 1, 1, 1, b"abcd"), ("sactx-DXT5_BC3", 1, 1, 1, b"abcd"),
                ("conflict|page", 1, 1, 1, b"abcd"), ("conflict_page", 1, 1, 1, b"efgh")], path)
            self.assertEqual(set(unpack(path)), {"sactx-DXT5_BC3"})
            self.assertEqual(rejected, ["conflict_page"])
            self.assertEqual(len(records), 1)

    def test_invalid_bounds_and_colour_encoding_fail_closed(self):
        for entries in ([], [("X", 0, 1, 1, b"")], [("X", 1, 1, 1, b"a")],
                        [("X", 1, 1, 2, b"abcd")], [("\0", 1, 1, 1, b"abcd")]):
            with tempfile.TemporaryDirectory() as directory, self.subTest(entries=entries), self.assertRaises(pixels.PixelError):
                pixels.write_entries(entries, Path(directory) / "pixels.bin")

    def test_decoded_compressed_and_inventory_budgets(self):
        for setting, bound in (("MAX_FILE", 12), ("MAX_TOTAL", 3), ("MAX_ENTRIES", 1)):
            with tempfile.TemporaryDirectory() as directory, self.subTest(setting=setting), patch.object(pixels, setting, bound), self.assertRaises(pixels.PixelError):
                pixels.write_entries([("A", 2, 2, 1, b"1234" * 4), ("B", 1, 1, 1, b"abcd")], Path(directory) / "pixels.bin")

    def test_source_selection_covers_base_dlc_ability_states_items_and_portraits_only(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); aa = root / "StreamingAssets/aa/StandaloneWindows64"; aa.mkdir(parents=True)
            selected = {"character_gui_assets_ac_brute_background.bundle", "character_gui_assets_ac_brute_top_highlighted.bundle",
                        "itemconfigs_backgrounds_assets_boots_of_striding.bundle", "actor_portrats_assets_portrait_brute.bundle",
                        "misc_gui_assets_all.bundle", "misc_characterportraits_assets_all.bundle", "dlc_dlc2_misc_gui_assets_all.bundle"}
            for name in selected | {"character_model_brute.bundle", "pcg_materials_assets_wall.bundle"}:
                (aa / name).write_bytes(b"owned")
            self.assertEqual({path.name for path in pixels.source_files(root)}, selected)

    def test_resume_reuses_small_receipt_without_decoding_or_hash_walk(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); game, cache = root / "game", root / "cache"
            aa = game / "StreamingAssets/aa/StandaloneWindows64"; aa.mkdir(parents=True)
            source = aa / "character_gui_assets_ac_brute_background.bundle"; source.write_bytes(b"owned")
            calls = []
            def decoder(path):
                calls.append(path); yield "AC_Brute_Background", 2, 2, 1, bytes(range(16))
            first = pixels.build(game, cache, "owner", decoder=decoder)
            with patch.object(pixels, "file_hash", side_effect=AssertionError("warm path rehashed payload")):
                second = pixels.build(game, cache, "owner", decoder=decoder)
            self.assertFalse(first["reused"]); self.assertTrue(second["reused"]); self.assertEqual(len(calls), 1)
            target = Path(first["path"]); previous = target.read_bytes(); target.write_bytes(b"x" * len(previous))
            third = pixels.build(game, cache, "owner", decoder=decoder)
            self.assertFalse(third["reused"]); self.assertEqual(target.read_bytes(), previous)
            self.assertEqual(source.read_bytes(), b"owned"); self.assertEqual(len(calls), 2)
            self.assertNotEqual(pixels.build(game, cache, "different-owner", decoder=decoder)["path"], first["path"])

    def test_source_changes_never_replace_completed_cache_or_leave_spool(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); game, cache = root / "game", root / "cache"
            aa = game / "StreamingAssets/aa/StandaloneWindows64"; aa.mkdir(parents=True)
            source = aa / "misc_gui_assets_all.bundle"; source.write_bytes(b"owned")
            def decoder(path):
                source.write_bytes(b"changed"); yield "A", 1, 1, 1, b"abcd"
            with self.assertRaises(pixels.PixelError): pixels.build(game, cache, "owner", decoder=decoder)
            self.assertFalse(list(cache.rglob("*.tmp")))
            self.assertFalse(list(cache.rglob("quest-card-pixels.bin")))


if __name__ == "__main__":
    unittest.main()
