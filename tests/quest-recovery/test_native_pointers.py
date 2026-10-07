import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-recovery"))
from recover import RecoveryError, audit_asset_references, serialized_pointer_tokens
from pointer_recovery import MISSING_GUID, native_pointers, native_target, yaml_missing, native_recipe_fields
from packed_sprites import field_edits, vertex_bytes, packed_uv, rectangle
from native_targets import engine_redirects


class NativePointers(unittest.TestCase):
    def test_reference_audit_excludes_literal_guid_in_original_object_name(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            (project / "Assets").mkdir()
            missing = "b162c21018d6e5a4a8f81bece580e557"
            native = project / "Assets/original.prefab"
            native.write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1 &1\nGameObject:\n  m_Name: 'Original (Missing Prefab with guid: " + missing + ")'\n")
            self.assertEqual(audit_asset_references(project)["missingGuidCount"], 0)
            native.write_text(native.read_text() + "  m_Mesh: {fileID: 4300000, guid: " + missing + ", type: 2}\n")
            result = audit_asset_references(project)
            self.assertEqual(result["missingGuidCount"], 1)
            self.assertEqual(result["referenceCount"], 1)

    def test_complete_pointer_text_inside_scalar_is_not_an_object_reference(self):
        guid = "b162c21018d6e5a4a8f81bece580e557"
        literal = "{fileID: 1, guid: " + guid + ", type: 2}"
        text = "--- !u!1 &1\nGameObject:\n  m_Name: 'Example " + literal + "'\n  m_Help: |\n    " + literal + "\n"
        self.assertEqual(list(serialized_pointer_tokens(text)), [])

    def test_reference_audit_includes_unknown_native_suffix_and_importer_metadata(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            (project / "Assets").mkdir()
            guid = "b162c21018d6e5a4a8f81bece580e557"
            (project / "Assets/original.unknown").write_text("--- !u!114 &1\nMonoBehaviour:\n  target: {fileID: 1, guid: " + guid + ", type: 2}\n")
            (project / "Assets/import.png.meta").write_text("fileFormatVersion: 2\nTextureImporter:\n  external: {fileID: 1, guid: " + guid + ", type: 2}\n")
            result = audit_asset_references(project)
            self.assertEqual(result["referenceCount"], 2)
            self.assertEqual(len(result["missing"][guid]), 2)

    def test_native_map_supports_exact_exported_dictionary_and_pair_paths(self):
        native = {"m_TexEnvs": [("_Mask", {"m_Texture": {"m_FileID": 2, "m_PathID": 53}})]}
        pointers = dict(native_pointers(native))
        self.assertEqual(pointers[("m_TexEnvs", "_Mask", "m_Texture")], (2, 53))
        self.assertEqual(pointers[("m_TexEnvs", 0, "_Mask", "m_Texture")], (2, 53))

    def test_duplicate_map_keys_do_not_invent_dictionary_aliases(self):
        native = [("_Mask", {"m_FileID": 1, "m_PathID": 1}), ("_Mask", {"m_FileID": 2, "m_PathID": 2})]
        self.assertNotIn(("_Mask",), dict(native_pointers(native)))

    def test_exact_yaml_coordinates_preserve_nonpointer_bytes(self):
        text = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  m_Name: Original\n  texture: {fileID: 2700000, guid: " + MISSING_GUID + ", type: 3}\n"
        row, = yaml_missing(text)
        self.assertEqual(row["field"], ("texture",))
        self.assertEqual(text[row["start"]:row["end"]], "{fileID: 2700000, guid: " + MISSING_GUID + ", type: 3}")
        self.assertEqual(row["classId"], 21)

    def test_unknown_missing_pointer_mapping_fails(self):
        with self.assertRaises(RecoveryError):
            list(yaml_missing("--- !u!21 &1\nMaterial:\n  texture: {fileID: 1, guid: " + MISSING_GUID + "}\n"))

    def test_original_external_index_selects_actual_dependency_cab(self):
        class External:
            path = "archive:/CAB-A/CAB-Original"
        class Collection:
            name = "CAB-Owner"
            externals = [External()]
        self.assertEqual(native_target(Collection(), (1, -9)), ("cab-original", -9))
        self.assertEqual(native_target(Collection(), (0, 9)), ("cab-owner", 9))
        with self.assertRaises(RecoveryError):
            native_target(Collection(), (2, 9))
        with self.assertRaises(RecoveryError):
            native_target(Collection(), (1, 0))

    def test_engine_redirect_accepts_actual_source_implementation_only(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "redirects.jsonl"
            row = {"collection": "CAB-Source", "pathId": 19, "classId": 28,
                   "guid": "0000000000000000f000000000000000", "fileId": 10300, "type": 0,
                   "exportCollection": "AssetRipper.Export.UnityProjects.RedirectExportCollection"}
            path.write_text(json.dumps(row) + "\n")
            self.assertEqual(engine_redirects(path)[("cab-source", 19)]["fileId"], 10300)
            row["exportCollection"] = "NameGuess"
            path.write_text(json.dumps(row) + "\n")
            with self.assertRaises(RecoveryError):
                engine_redirects(path)


class PackedNativeSprites(unittest.TestCase):
    def test_native_float32_uv_matches_original_player_aa_immune_witness(self):
        uv = packed_uv((-0.15000000596046448, 0.44999998807907104),
                       {"x": 100.0, "y": 3133.0, "z": 100.0, "w": 1337.0}, 4096, 4096)
        self.assertEqual(uv, (0.76123046875, 0.33740234375))

    def test_uv_stream_replacement_retains_original_position_bytes(self):
        channels = [{"stream": 0, "offset": 0, "format": 0, "dimension": 3}]
        channels.extend({"stream": 0, "offset": 0, "format": 0, "dimension": 0} for _ in range(3))
        channels.append({"stream": 1, "offset": 0, "format": 0, "dimension": 2})
        original = struct.pack("<3f", -.15, .45, 0) + b"\0" * 4 + b"\0" * 8
        actual, positions, uv = vertex_bytes({"m_Channels": channels, "m_VertexCount": 1, "m_DataSize": list(original)},
                   {"settingsRaw": 67, "uvTransform": {"x": 100, "y": 3133, "z": 100, "w": 1337}}, 4096, 4096)
        self.assertEqual(actual[:16], original[:16])
        self.assertEqual(struct.unpack("<2f", actual[16:]), (.76123046875, .33740234375))

    def test_unknown_packing_rotation_is_rejected(self):
        with self.assertRaises(RecoveryError):
            vertex_bytes({"m_Channels": [], "m_VertexCount": 0, "m_DataSize": []}, {}, 1, 1)

    def test_rect_replacement_preserves_unity_native_versioned_block(self):
        text = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!213 &21300000\nSprite:\n  m_Rect:\n    serializedVersion: 2\n    x: 9\n    y: 8\n    width: 1\n    height: 2\n  m_Pivot: {x: 0.5, y: 0.5}\n"
        value = rectangle({"x": 0, "y": 0, "width": 128, "height": 128}, 4)
        actual = field_edits(text, {("m_Rect",): value})
        self.assertIn("  m_Rect:\n    serializedVersion: 2\n    x: 0\n", actual)
        self.assertIn("    height: 128\n  m_Pivot:", actual)


if __name__ == "__main__":
    unittest.main()
