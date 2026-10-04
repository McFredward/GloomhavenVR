"""Independent serialized Sprite fixtures and bounded loading-geometry recovery."""
import hashlib
from pathlib import Path
import struct
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import sprites
from storage import BuildError


def bank(*, endian="<", tree=False, version=22, unity="2021.3.5f1", duplicate=False):
    def pack(form, *values):
        return struct.pack(endian + form, *values)
    rows = [("LoadingBase", (0.5, 0.5)), ("LoadingOverlay", (0.5, 0.5))]
    if duplicate:
        rows.append(rows[0])
    payloads = []
    for name, pivot in rows:
        encoded = name.encode()
        value = pack("i", len(encoded)) + encoded
        value += bytes((-len(value)) % 4)
        value += pack("4f", 0, 0, 128, 128) + pack("2f", 0, 0) + pack("4f", 0, 0, 0, 0)
        value += pack("f2f", 100, *pivot) + bytes(32)
        payloads.append(value)
    metadata = unity.encode() + b"\0" + pack("i", 19) + bytes([int(tree)]) + pack("i", 1)
    metadata += pack("iBh", 213, 0, -1) + bytes(16)
    if tree:
        metadata += pack("ii", 1, 4) + bytes(32) + b"x\0y\0" + pack("ii", 1, 213)
    metadata += pack("i", len(rows))
    data = bytearray()
    for index, payload in enumerate(payloads):
        metadata += bytes((-(48 + len(metadata))) % 4)
        metadata += pack("qqIi", index + 1, len(data), len(payload), 0)
        data += payload + bytes((-len(payload)) % 8)
    metadata += pack("iii", 0, 0, 0) + b"\0"
    offset = (48 + len(metadata) + 15) // 16 * 16
    header = struct.pack(">IIIIB3sIqqq", 0, 0, version, 0, 0 if endian == "<" else 1,
                         bytes(3), len(metadata), offset + len(data), offset, 0)
    return header + metadata + bytes(offset - 48 - len(metadata)) + data


def asset(name, *, trimmed=True):
    base = name == "LoadingBase" and trimmed
    return ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!213 &21300000\nSprite:\n"
            "  m_Name: " + name + "\n  m_Rect:\n    serializedVersion: 2\n"
            + ("    x: 108\n    y: 208\n    width: 112\n    height: 112\n" if base else
               "    x: 100\n    y: 200\n    width: 128\n    height: 128\n")
            + "  m_Offset: {x: 0, y: 0}\n  m_Border: {x: 0, y: 0, z: 0, w: 0}\n"
            "  m_PixelsToUnits: 100\n  m_Pivot: {x: 0.5, y: 0.5}\n  m_RD:\n"
            "    texture: {fileID: 2800000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n"
            "    m_VertexData:\n      _typelessdata: 001122aabbcc\n    textureRect:\n    "
            "  serializedVersion: 2\n"
            + ("      x: 108\n      y: 208\n      width: 112\n      height: 112\n" if base else
               "      x: 100\n      y: 200\n      width: 128\n      height: 128\n")
            + "    textureRectOffset: {x: 0, y: 0}\n    uvTransform: {x: 100, y: 164, z: 100, w: 264}\n"
            "  m_AtlasRD:\n    textureRectOffset: {x: 0, y: 0}\n")


class SpriteGeometryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-sprite-")
        self.root = Path(self.temp.name)
        self.game, self.project = self.root / "game", self.root / "project"
        self.game.mkdir()
        self.folder = self.project / "Assets/Sprite"
        self.folder.mkdir(parents=True)
        self.bank = self.game / "resources.assets"
        self.bank.write_bytes(bank())
        for index, name in enumerate(sorted(sprites.NAMES)):
            path = self.folder / (name + ".asset")
            path.write_text(asset(name))
            Path(str(path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + str(index + 1) * 32 + "\n")

    def tearDown(self):
        self.temp.cleanup()

    def test_independent_little_big_endian_and_type_tree_headers(self):
        for endian in ("<", ">"):
            for tree in (False, True):
                with self.subTest(endian=endian, tree=tree):
                    self.bank.write_bytes(bank(endian=endian, tree=tree))
                    rows = sprites.read_sprite_headers(self.bank)
                    self.assertEqual([r["name"] for r in rows], ["LoadingBase", "LoadingOverlay"])
                    self.assertEqual(rows[0]["rect"]["width"], 128)
                    self.assertEqual(rows[0]["pivot"], {"x": 0.5, "y": 0.5})

    def test_restores_trimmed_geometry_retains_atlas_uv_vertex_and_guid(self):
        original = self.bank.read_bytes()
        path = self.folder / "LoadingBase.asset"
        original_asset = path.read_text()
        meta = Path(str(path) + ".meta").read_bytes()
        receipt = sprites.restore_loading_sprite_geometry(self.project, self.game)
        text = path.read_text()
        self.assertEqual(sprites._rect(text, "m_Rect"), {"x": 100, "y": 200, "width": 128, "height": 128})
        self.assertIn("    textureRectOffset: {x: 8, y: 8}", text)
        self.assertIn("  m_AtlasRD:\n    textureRectOffset: {x: 0, y: 0}", text)
        for preserved in ("_typelessdata: 001122aabbcc", "uvTransform: {x: 100, y: 164, z: 100, w: 264}",
                          "texture: {fileID: 2800000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}"):
            self.assertIn(preserved, original_asset)
            self.assertIn(preserved, text)
        self.assertEqual(self.bank.read_bytes(), original)
        self.assertEqual(Path(str(path) + ".meta").read_bytes(), meta)
        self.assertEqual(receipt["sourceSha256"], hashlib.sha256(original).hexdigest())
        self.assertEqual(receipt, sprites.restore_loading_sprite_geometry(self.project, self.game))

    def test_full_overlay_geometry_remains_identical(self):
        path = self.folder / "LoadingOverlay.asset"
        before = path.read_bytes()
        sprites.restore_loading_sprite_geometry(self.project, self.game)
        self.assertEqual(path.read_bytes(), before)

    def test_bad_original_version_or_unity_fails_before_changes(self):
        for replacement in (bank(version=21), bank(unity="2021.3.6f1"), bank()[:-1]):
            before = {p: p.read_bytes() for p in self.folder.iterdir()}
            self.bank.write_bytes(replacement)
            with self.assertRaises(BuildError):
                sprites.restore_loading_sprite_geometry(self.project, self.game)
            self.assertEqual(before, {p: p.read_bytes() for p in self.folder.iterdir()})

    def test_ambiguous_owned_identity_is_rejected(self):
        self.bank.write_bytes(bank(duplicate=True))
        with self.assertRaisesRegex(BuildError, "uniquely"):
            sprites.restore_loading_sprite_geometry(self.project, self.game)

    def test_missing_overlay_is_rejected_before_base_write(self):
        (self.folder / "LoadingOverlay.asset").unlink()
        path = self.folder / "LoadingBase.asset"
        before = path.read_bytes()
        with self.assertRaisesRegex(BuildError, "both"):
            sprites.restore_loading_sprite_geometry(self.project, self.game)
        self.assertEqual(path.read_bytes(), before)

    def test_unrecognized_pivot_does_not_get_guessed(self):
        path = self.folder / "LoadingBase.asset"
        path.write_text(path.read_text().replace("m_Pivot: {x: 0.5, y: 0.5}", "m_Pivot: {x: 0.1, y: 0.8}"))
        before = path.read_bytes()
        with self.assertRaisesRegex(BuildError, "uniquely"):
            sprites.restore_loading_sprite_geometry(self.project, self.game)
        self.assertEqual(path.read_bytes(), before)

    def test_symlinked_generated_sprite_cannot_write_through(self):
        path = self.folder / "LoadingBase.asset"
        borrowed = self.root / "borrowed.asset"
        path.rename(borrowed)
        path.symlink_to(borrowed)
        before = borrowed.read_bytes()
        with self.assertRaisesRegex(BuildError, "symlink"):
            sprites.restore_loading_sprite_geometry(self.project, self.game)
        self.assertEqual(borrowed.read_bytes(), before)

    def test_nonfinite_or_invalid_crop_is_rejected(self):
        path = self.folder / "LoadingBase.asset"
        original = path.read_text()
        for before, after in (("      width: 112", "      width: nan"), ("      width: 112", "      width: 999"),
                              ("      width: 112", "      width: 0"), ("      width: 112", "      width: -1")):
            path.write_text(original.replace(before, after))
            with self.assertRaises(BuildError):
                sprites.restore_loading_sprite_geometry(self.project, self.game)


if __name__ == "__main__":
    unittest.main()
