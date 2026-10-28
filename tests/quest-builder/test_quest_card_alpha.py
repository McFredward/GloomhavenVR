"""CPU alpha wire contract, bounded extraction and exact-source cache recovery."""
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import zlib

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_card_alpha_tested", ROOT / "tools/quest-builder/quest_card_alpha.py")
alpha = importlib.util.module_from_spec(spec)
spec.loader.exec_module(alpha)


def unpack(data):
    assert data[:5] == b"GHVA1"
    count = struct.unpack_from("<i", data, 5)[0]
    at, result = 9, {}
    for _ in range(count):
        size = struct.unpack_from("<i", data, at)[0]
        at += 4
        name = data[at:at + size].decode()
        at += size
        width, height, size = struct.unpack_from("<iii", data, at)
        at += 12
        result[name] = width, height, zlib.decompress(data[at:at + size], -15)
        at += size
    assert at == len(data)
    return result


class CardAlphaTests(unittest.TestCase):
    def test_exact_bottom_up_wire_and_deterministic_order(self):
        data, records, ambiguous = alpha.encode([("B", 2, 2, b"\x00\x7f\xff\x42"), ("A", 1, 1, b"\x05")])
        self.assertEqual(list(unpack(data)), ["A", "B"])
        self.assertEqual(unpack(data)["B"], (2, 2, b"\x00\x7f\xff\x42"))
        self.assertEqual([row["name"] for row in records], ["A", "B"])
        self.assertEqual(ambiguous, [])
        self.assertEqual(data, alpha.encode([("A", 1, 1, b"\x05"), ("B", 2, 2, b"\x00\x7f\xff\x42")])[0])

    def test_duplicate_identity_reuses_but_ambiguous_original_is_rejected(self):
        data, _, rejected = alpha.encode([("X", 1, 1, b"a"), ("X", 1, 1, b"b"), ("Y", 1, 1, b"c"), ("Y", 1, 1, b"c")])
        self.assertEqual(set(unpack(data)), {"Y"})
        self.assertEqual(rejected, ["X"])

    def test_empty_ambiguous_and_invalid_entries_fail_closed(self):
        for entries in ([], [("X", 0, 1, b"")], [("X", 2, 2, b"123")], [("\0", 1, 1, b"a")],
                        [("X", 1, 1, b"a"), ("X", 1, 1, b"b")]):
            with self.subTest(entries=entries), self.assertRaises(alpha.AlphaError):
                alpha.encode(entries)

    def test_decoded_compressed_and_inventory_budgets(self):
        with patch.object(alpha, "MAX_TOTAL", 3), self.assertRaises(alpha.AlphaError):
            alpha.encode([("A", 2, 2, b"1234")])
        with patch.object(alpha, "MAX_FILE", 12), self.assertRaises(alpha.AlphaError):
            alpha.encode([("A", 1, 1, b"1")])
        with patch.object(alpha, "MAX_ENTRIES", 1), self.assertRaises(alpha.AlphaError):
            alpha.encode([("A", 1, 1, b"1"), ("B", 1, 1, b"2")])

    def test_only_named_ability_item_and_common_gui_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            game = Path(temporary)
            directory = game / "StreamingAssets/aa/StandaloneWindows64"
            directory.mkdir(parents=True)
            names = ("character_gui_assets_ac_brute_background.bundle", "misc_gui_assets_all.bundle",
                     "dlc_dlc1_misc_gui_assets_all.bundle", "itemconfigs_backgrounds_assets_boots_of_striding.bundle")
            for name in (*names, "character_model_brute.bundle", "character_gui_assets_ac_brute_top.bundle"):
                (directory / name).write_bytes(b"owned")
            self.assertEqual({p.name for p in alpha.source_files(game)}, set(names))

    def test_completed_cache_is_reused_without_decoding_originals(self):
        with tempfile.TemporaryDirectory() as temporary:
            game, cache = Path(temporary) / "game", Path(temporary) / "cache"
            directory = game / "StreamingAssets/aa/StandaloneWindows64"
            directory.mkdir(parents=True)
            source = directory / "character_gui_assets_ac_brute_background.bundle"
            source.write_bytes(b"owned")
            calls = []
            def decoder(path):
                calls.append(path)
                yield "AC_Brute_Background", 2, 2, b"\0\xff\xff\0"
            first = alpha.build(game, cache, "owned-game", decoder=decoder)
            second = alpha.build(game, cache, "owned-game", decoder=decoder)
            self.assertFalse(first["reused"])
            self.assertTrue(second["reused"])
            self.assertEqual(len(calls), 1)
            # Repair only the small broken derived file; originals are untouched.
            Path(first["path"]).write_bytes(b"broken")
            third = alpha.build(game, cache, "owned-game", decoder=decoder)
            self.assertFalse(third["reused"])
            self.assertEqual(source.read_bytes(), b"owned")
            self.assertEqual(len(calls), 2)
            source.write_bytes(b"new owned")
            self.assertFalse(alpha.build(game, cache, "owned-game", decoder=decoder)["reused"])

    def test_source_changes_never_publish_partial_result(self):
        with tempfile.TemporaryDirectory() as temporary:
            game, cache = Path(temporary) / "game", Path(temporary) / "cache"
            directory = game / "StreamingAssets/aa/StandaloneWindows64"
            directory.mkdir(parents=True)
            source = directory / "character_gui_assets_ac_brute_background.bundle"
            source.write_bytes(b"owned")
            def decoder(path):
                yield "AC_Brute_Background", 1, 1, b"\xff"
                path.write_bytes(b"changed during decode")
            with self.assertRaisesRegex(alpha.AlphaError, "changed during extraction"):
                alpha.build(game, cache, "owned-game", decoder=decoder)
            self.assertFalse(list(cache.rglob("*.bin")))


if __name__ == "__main__":
    unittest.main()
