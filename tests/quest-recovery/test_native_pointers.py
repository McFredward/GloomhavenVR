import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-recovery"))
from recover import RecoveryError, audit_asset_references, serialized_pointer_tokens
from pointer_recovery import MISSING_GUID, native_pointers, native_target, yaml_missing, native_recipe_fields
from packed_sprites import field_edits, vertex_bytes, packed_uv, rectangle
from native_targets import engine_redirects
import pointer_recovery
import packed_sprites


def native_fixture(collection, path_id, class_id, fields):
    return types.SimpleNamespace(assets_file=collection, path_id=path_id,
                                 type=types.SimpleNamespace(value=class_id),
                                 read_typetree=lambda: fields,
                                 get_raw_data=lambda: b"original native object " + str(path_id).encode())


def fixture_loader(objects, expected):
    class Environment:
        def __init__(self): self.objects = objects
        def load_file(self, data, *, name):
            if data != expected: raise AssertionError("Native source bytes changed")
    return types.SimpleNamespace(Environment=Environment)


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

    def test_missing_pointer_spans_retain_crlf_unicode_signed_and_stripped_headers(self):
        pointer = "{fileID: 2700000, guid: " + MISSING_GUID + ", type: 3}"
        for newline in ("\n", "\r\n"):
            with self.subTest(newline=repr(newline)):
                text = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &-2100000 stripped\n"
                        "Material:\n  m_Name: Wächter 原始 ⚔\n  texture: " + pointer + "\n"
                        "--- !u!21 &2100001\nMaterial:\n  texture: " + pointer + "\n").replace("\n", newline)
                rows = list(yaml_missing(text))
                self.assertEqual([row["localFileId"] for row in rows], [-2100000, 2100001])
                self.assertEqual([text[row["start"]:row["end"]] for row in rows], [pointer, pointer])

    def test_null_managed_registry_keeps_later_pointer_offset(self):
        pointer = "{fileID: 2700000, guid: " + MISSING_GUID + ", type: 3}"
        text = ("--- !u!114 &-1 stripped\r\nMonoBehaviour:\r\n  references:\r\n"
                "    RefIds:\r\n    - rid: -2\r\n      type: {class:, ns:, asm:}\r\n"
                "      data:\r\n  texture: " + pointer + "\r\n")
        row, = yaml_missing(text)
        self.assertEqual(row["field"], ("texture",))
        self.assertEqual(text[row["start"]:row["end"]], pointer)

    def test_unrelated_malformed_yaml_is_not_accepted_as_null_registry(self):
        import yaml
        text = "--- !u!21 &1\r\nMaterial:\r\n  invalid: [}\r\n  texture: {fileID: 1, guid: " + MISSING_GUID + ", type: 3}\r\n"
        with self.assertRaises(yaml.YAMLError): list(yaml_missing(text))

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

    def test_block_edits_keep_crlf_and_nonascii_bytes_outside_changed_field(self):
        text = ("%YAML 1.1\r\n%TAG !u! tag:unity3d.com,2011:\r\n--- !u!213 &-21300000 stripped\r\n"
                "Sprite:\r\n  m_Name: Wächter 原始 ⚔\r\n  m_Rect:\r\n    serializedVersion: 2\r\n"
                "    x: 9\r\n    y: 8\r\n    width: 1\r\n    height: 2\r\n  m_Pivot: {x: 0.5, y: 0.5}\n")
        actual = field_edits(text, {("m_Rect",): rectangle({"x": 0, "y": 0, "width": 128, "height": 128}, 4)})
        expected = text.replace("    x: 9\r\n    y: 8\r\n    width: 1\r\n    height: 2", "    x: 0\r\n    y: 0\r\n    width: 128\r\n    height: 128")
        self.assertEqual(actual.encode("utf-8"), expected.encode("utf-8"))


class NativeOverlayBytes(unittest.TestCase):
    """Execute producer/application joins with exact source CAB/field fixtures."""
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project, self.game, self.overlay = [self.root / name for name in ("project", "game", "overlay")]
        (self.project / "QuestRecovery").mkdir(parents=True); self.game.mkdir()
        (self.project / "QuestRecovery/managed-types.json").write_bytes(b"{}")
        self.source_bytes = b"original native collection fixture"
        (self.game / "original.bundle").write_bytes(self.source_bytes)
        self.collection = types.SimpleNamespace(name="cab-original", externals=[])

    def pointer_overlay(self, newline="\n", mixed=False):
        original = "{fileID: 2700000, guid: " + MISSING_GUID + ", type: 3}"
        self.replacement = "{fileID: 2800000, guid: " + "b" * 32 + ", type: 3}"
        rows, objects, self.originals = [], [], {}
        for index in range(2):
            relative = "Assets/Material" + str(index) + ".mat"
            text = ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &-2100000 stripped\n"
                    "Material:\n  m_Name: Wächter 原始 ⚔\n  texture: " + original + "\n"
                    "  m_Keep: 'unchanged'\n").replace("\n", newline)
            if mixed: text = text.replace("  m_Name: Wächter 原始 ⚔" + newline, "  m_Name: Wächter 原始 ⚔\n", 1)
            path = self.project / relative; path.parent.mkdir(exist_ok=True); path.write_bytes(text.encode("utf-8"))
            self.originals[relative] = path.read_bytes()
            rows.append({"guid": str(index + 1) * 32, "path": relative,
                         "objects": [{"collection": "cab-original", "pathId": index + 1, "fileId": -2100000, "classId": 21}]})
            objects.append(native_fixture(self.collection, index + 1, 21, {"texture": {"m_FileID": 0, "m_PathID": 99}}))
        texture = self.project / "Assets/Texture.png"; texture.write_bytes(b"original texture fixture")
        texture.with_name(texture.name + ".meta").write_bytes(("guid: " + "b" * 32 + "\r\nTextureImporter:\r\n").encode())
        rows.append({"guid": "b" * 32, "path": "Assets/Texture.png",
                     "objects": [{"collection": "cab-original", "pathId": 99, "fileId": 2800000, "classId": 28}]})
        # The producer must use byte IO even when the host's text defaults would
        # normalize CRLF or choose an incompatible Windows locale.
        with patch.object(Path, "write_text", side_effect=AssertionError("Overlay bytes require explicit UTF-8")):
            receipt = pointer_recovery.prepare(self.project, self.game, rows, {"cab-original": "original.bundle"},
                                              self.overlay, unitypy=fixture_loader(objects, self.source_bytes))
        self.assertTrue(receipt["complete"])
        self.assertEqual(receipt["restoredPointerCount"], 2)
        self.expected = {path: data.replace(original.encode(), self.replacement.encode()) for path, data in self.originals.items()}
        return receipt

    def test_actual_prepare_apply_preserves_lf_crlf_mixed_and_utf8(self):
        for newline, mixed in (("\n", False), ("\r\n", False), ("\r\n", True)):
            with self.subTest(newline=repr(newline), mixed=mixed):
                # Each producer requires its own fresh overlay/workspace.
                if self.overlay.exists():
                    import shutil
                    shutil.rmtree(self.overlay)
                receipt = self.pointer_overlay(newline, mixed)
                for row in receipt["files"]:
                    self.assertEqual(row["beforeSha256"], hashlib.sha256(self.originals[row["path"]]).hexdigest())
                    self.assertEqual((self.overlay / "Overlay" / row["path"]).read_bytes(), self.expected[row["path"]])
                pointer_recovery.apply(self.project, self.overlay, receipt)
                for path, expected in self.expected.items(): self.assertEqual((self.project / path).read_bytes(), expected)

    def test_modified_later_input_is_rejected_before_any_overlay_file_is_applied(self):
        receipt = self.pointer_overlay("\r\n")
        second = receipt["files"][1]["path"]
        (self.project / second).write_bytes(self.originals[second] + b"changed")
        with self.assertRaisesRegex(RecoveryError, "exact input/output bytes changed"):
            pointer_recovery.apply(self.project, self.overlay, receipt)
        first = receipt["files"][0]["path"]
        self.assertEqual((self.project / first).read_bytes(), self.originals[first])

    def test_modified_later_output_is_rejected_before_any_overlay_file_is_applied(self):
        receipt = self.pointer_overlay("\r\n")
        second = self.overlay / "Overlay" / receipt["files"][1]["path"]
        second.write_bytes(second.read_bytes() + b"changed")
        with self.assertRaisesRegex(RecoveryError, "exact input/output bytes changed"):
            pointer_recovery.apply(self.project, self.overlay, receipt)
        for path, expected in self.originals.items(): self.assertEqual((self.project / path).read_bytes(), expected)

    def test_incomplete_receipt_is_rejected_before_mutation(self):
        receipt = self.pointer_overlay("\r\n"); receipt["complete"] = False
        with self.assertRaisesRegex(RecoveryError, "Incomplete native pointer recovery"):
            pointer_recovery.apply(self.project, self.overlay, receipt)
        for path, expected in self.originals.items(): self.assertEqual((self.project / path).read_bytes(), expected)

    def test_pointer_container_progress_counts_the_existing_source_schedule(self):
        stream = io.StringIO()
        with patch.dict(os.environ, {pointer_recovery.build_progress.ENV: "1"}), contextlib.redirect_stdout(stream):
            self.pointer_overlay("\r\n")
        prefix = pointer_recovery.build_progress.PREFIX
        events = [json.loads(line.removeprefix(prefix)) for line in stream.getvalue().splitlines() if line.startswith(prefix)]
        containers = [event for event in events if event["phase"] == "recovery-pointer-containers"]
        self.assertEqual([(event["status"], event["done"], event["total"], event["unit"]) for event in containers],
                         [("start", 0, 1, "containers"), ("complete", 1, 1, "containers")])
        self.assertEqual(containers[-1]["detail"], "original.bundle")

    def test_native_managed_recipe_retains_crlf_unicode_and_null_registry(self):
        before = ("%YAML 1.1\r\n%TAG !u! tag:unity3d.com,2011:\r\n--- !u!114 &-1 stripped\r\nMonoBehaviour:\r\n"
                  "  m_Name: Wächter 原始 ⚔\r\n  m_GameObject: {m_FileID: 0, m_PathID: 42}\r\n"
                  "  m_Script: {m_FileID: 1, m_PathID: 43}\r\n  references:\r\n    RefIds:\r\n"
                  "    - rid: -2\r\n      type: {class:, ns:, asm:}\r\n      data:\r\n").encode("utf-8")
        path = self.root / "recipe.yaml"; path.write_bytes(before)
        original = types.SimpleNamespace(type=types.SimpleNamespace(value=114), reader=types.SimpleNamespace(endian="<"),
                                         get_raw_data=lambda: struct.pack("<iqiiq", 0, 42, 1, 1, 43) + b"\0" * 4)
        recipe = {"classId": 114, "yamlPath": str(path), "yamlSha256": hashlib.sha256(before).hexdigest()}
        self.assertEqual(native_recipe_fields(recipe, original), {("m_GameObject",): (0, 42), ("m_Script",): (1, 43)})
        self.assertEqual(path.read_bytes(), before)

    def test_applied_receipt_cannot_be_reused_on_changed_input(self):
        receipt = self.pointer_overlay("\r\n")
        pointer_recovery.apply(self.project, self.overlay, receipt)
        before = {path: (self.project / path).read_bytes() for path in self.originals}
        with self.assertRaisesRegex(RecoveryError, "exact input/output bytes changed"):
            pointer_recovery.apply(self.project, self.overlay, receipt)
        self.assertEqual({path: (self.project / path).read_bytes() for path in self.originals}, before)

    def test_actual_packed_sprite_restore_hashes_and_splices_exact_crlf_utf8_bytes(self):
        sprite_path, texture_path = "Assets/OriginalSprite.asset", "Assets/Texture.png"
        sprite = {"m_Name": "Wächter 原始 ⚔", "m_Offset": {"x": 0, "y": 0}, "m_Border": {"x": 0, "y": 0, "z": 0, "w": 0},
                  "m_Pivot": {"x": 0.5, "y": 0.5}, "m_Rect": {"x": 0, "y": 0, "width": 1, "height": 1},
                  "m_PixelsToUnits": 100, "m_RenderDataKey": ({"data[" + str(i) + "]": 0 for i in range(4)}, 1),
                  "m_SpriteAtlas": {"fileID": 0}}
        channels = [{"stream": 0, "offset": 0, "format": 0, "dimension": 3}]
        channels.extend({"stream": 0, "offset": 0, "format": 0, "dimension": 0} for _ in range(3))
        channels.append({"stream": 1, "offset": 0, "format": 0, "dimension": 2})
        sprite["m_RD"] = {"texture": {"fileID": 0}, "textureRect": {"serializedVersion": 2, "x": 9, "y": 9, "width": 9, "height": 9},
                          "textureRectOffset": {"x": 0, "y": 0}, "atlasRectOffset": {"x": 0, "y": 0},
                          "uvTransform": {"x": 1, "y": 0, "z": 1, "w": 0}, "settingsRaw": 0, "downscaleMultiplier": 1,
                          "m_VertexData": {"m_Channels": channels, "m_VertexCount": 1,
                                           "m_DataSize": list(struct.pack("<3f", 0.5, 0.5, 0) + b"\0" * 12), "_typelessdata": "00" * 24}}
        atlas_fields = {"m_PackedSprites": [{"m_FileID": 0, "m_PathID": 1}],
                        "m_RenderDataMap": [(sprite["m_RenderDataKey"], {"texture": {"m_FileID": 0, "m_PathID": 2},
                            "textureRect": {"x": 0, "y": 0, "width": 1, "height": 1},
                            "textureRectOffset": {"x": 0, "y": 0}, "atlasRectOffset": {"x": 0, "y": 0},
                            "uvTransform": {"x": 1, "y": 0, "z": 1, "w": 0}, "settingsRaw": 0, "downscaleMultiplier": 1})]}
        import yaml
        # The parsed Sprite exporter fixture is deliberately Windows CRLF.
        header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!213 &21300000\n"
        before = (header + yaml.safe_dump({"Sprite": sprite}, allow_unicode=True, sort_keys=False)).replace("\n", "\r\n").encode("utf-8")
        path = self.project / sprite_path; path.parent.mkdir(exist_ok=True); path.write_bytes(before)
        atlas = native_fixture(self.collection, 3, 687078895, atlas_fields)
        objects = {("cab-original", 1): {"path": sprite_path, "fileId": 21300000, "guid": "a" * 32},
                   ("cab-original", 2): {"path": texture_path, "fileId": 2800000, "guid": "b" * 32}}
        originals = [native_fixture(self.collection, 1, 213, sprite),
                     native_fixture(self.collection, 2, 28, {"m_Width": 16, "m_Height": 16})]
        with patch.object(Path, "write_text", side_effect=AssertionError("Overlay bytes require explicit UTF-8")):
            receipt = packed_sprites.restore(self.project, self.game, atlas, {"guid": "c" * 32, "collection": "cab-original", "pathId": 3},
                                             objects, {"cab-original": "original.bundle"}, self.overlay,
                                             unitypy=fixture_loader(originals, self.source_bytes))
        row, = receipt["restoredMembers"]
        after = (self.overlay / "Overlay" / sprite_path).read_bytes()
        self.assertEqual(row["beforeSha256"], hashlib.sha256(before).hexdigest())
        self.assertEqual(row["sha256"], hashlib.sha256(after).hexdigest())
        self.assertEqual(path.read_bytes(), before)
        self.assertIn("Wächter 原始 ⚔".encode("utf-8"), after)
        self.assertNotIn(b"\n", after.replace(b"\r\n", b""))
        self.assertTrue(after.startswith(header.replace("\n", "\r\n").encode()))
        self.assertEqual(row["uv"], [{"x": 0.03125, "y": 0.03125}])


if __name__ == "__main__":
    unittest.main()
