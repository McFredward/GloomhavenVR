import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import canonical_guids
from recover import RecoveryError
from full_shaders import ShaderRecoveryError, field_components, parameter_delta, restore_uniforms


class CampaignContracts(unittest.TestCase):
    def asset(self, root, relative, guid, body):
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(body)
        path.with_name(path.name + ".meta").write_text("guid: " + guid + "\n")

    def projects(self, directory):
        old, new = Path(directory) / "old", Path(directory) / "new"
        for root in (old, new):
            (root / "ProjectSettings").mkdir(parents=True)
            (root / "ProjectSettings/EditorBuildSettings.asset").write_text("m_Scenes:\n  - enabled: 1\n    path: Assets/Scenes/Only.unity\n")
        old_scene, new_scene, old_asset, new_asset = (str(i) * 32 for i in (1, 2, 3, 4))
        self.asset(old, "Assets/Scenes/Only.unity", old_scene,
                   "%YAML 1.1\n--- !u!1 &91\nGameObject:\n  target: {fileID: 2100000, guid: " + old_asset + ", type: 2}\n")
        self.asset(new, "Assets/Scenes/Only.unity", new_scene,
                   "%YAML 1.1\n--- !u!1 &91\nGameObject:\n  target: {fileID: 2100000, guid: " + new_asset + ", type: 2}\n")
        material = "%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Name: Witnessed\n"
        self.asset(old, "Assets/OldPath.mat", old_asset, material)
        self.asset(new, "Assets/OtherPath.mat", new_asset, material)
        rows = [{"guid": new_scene, "path": "Assets/Scenes/Only.unity", "objects": [
                    {"collection": "level0", "pathId": 91, "fileId": 91, "classId": 1}]},
                {"guid": new_asset, "path": "Assets/OtherPath.mat", "objects": [
                    {"collection": "cab-original", "pathId": -91, "fileId": 2100000, "classId": 21}]}]
        return old, new, rows

    def test_guid_join_follows_exact_original_graph_despite_different_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            old, new, rows = self.projects(directory)
            receipt = canonical_guids.witness(old, new, rows)
            self.assertEqual(receipt["mappings"]["4" * 32], "3" * 32)
            self.assertEqual(receipt["mappedOriginalObjectCount"], 2)
            self.assertEqual(receipt["proofs"][1]["edge"], "cab-original:-91")

    def test_changed_serialized_field_cannot_witness_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            old, new, rows = self.projects(directory)
            (new / "Assets/OtherPath.mat").write_text("%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Name: Changed\n")
            receipt = canonical_guids.witness(old, new, rows)
            self.assertNotIn("4" * 32, receipt["mappings"])
            self.assertEqual(receipt["rejected"][0]["reason"], "serialized-body-differs")

    def test_canonical_guid_application_preserves_observed_path_and_pointer(self):
        with tempfile.TemporaryDirectory() as directory:
            old, new, rows = self.projects(directory)
            receipt = canonical_guids.witness(old, new, rows)
            changed = canonical_guids.apply(new, rows, receipt)
            self.assertEqual(changed[1]["path"], "Assets/OldPath.mat")
            self.assertFalse((new / "Assets/OtherPath.mat").exists())
            self.assertIn("guid: " + "3" * 32, (new / "Assets/Scenes/Only.unity").read_text())
            self.assertEqual((new / "Assets/OldPath.mat.meta").read_text(), "guid: " + "3" * 32 + "\n")

    def field(self, **changes):
        return {"name": "_Amount", "type": 0, "rows": 1, "columns": 1, "matrix": False,
                "arraySize": 0, "byteOffset": 12, **changes}

    def interface(self, fields):
        return {"buffers": [{"name": "Original", "bytes": 16, "fields": fields}],
                "bindings": [{"name": "Original", "kind": "cbuffer", "slot": 0},
                             {"name": "_Mask", "kind": "texture", "slot": 3, "samplerSlot": 2, "dimension": 4}]}

    def test_translated_register_remap_uses_original_numeric_resource_identity(self):
        hlsl = """cbuffer cbX : register(b9) { float4 cb0_0_m0[1] : packoffset(c0); };
Texture2D<float4> t3 : register(t17);
SamplerState s2 : register(s12);
void vert_main() { float amount = cb0_0_m0[0u].w; float4 value=t3.Sample(s2,float2(0,0))*amount; }
"""
        actual, proof = restore_uniforms(hlsl, self.interface([self.field()]))
        self.assertIn("Texture2D<float4> _Mask;", actual)
        self.assertIn("SamplerState sampler_Mask;", actual)
        self.assertIn("_Mask.Sample(sampler_Mask", actual)
        self.assertIn("float4(0.0, 0.0, 0.0, _Amount)", actual)
        self.assertTrue(proof[0]["allUsedScalarsBound"])

    def test_used_unknown_uniform_fails_instead_of_becoming_zero(self):
        hlsl = "cbuffer cbX : register(b0) { float4 cb0_0_m0[1] : packoffset(c0); };\nvoid vert_main() { float amount=cb0_0_m0[0u].x; }"
        with self.assertRaisesRegex(ShaderRecoveryError, "cannot explain"):
            restore_uniforms(hlsl, self.interface([self.field()]))

    def test_matrix_native_column_packing_and_integer_bits_are_preserved(self):
        matrix = field_components(self.field(name="unity_MatrixVP", rows=4, columns=4, matrix=True, byteOffset=0))
        self.assertEqual(matrix[6], "transpose(UNITY_MATRIX_VP)[1][2]")
        integer = field_components(self.field(type=1))
        self.assertEqual(integer[3], "asfloat(_Amount)")

    def test_input_semantics_restore_position_instead_of_generic_texcoord(self):
        hlsl = "struct Input { float4 v0 : TEXCOORD0; }; void vert_main() {}"
        actual, _ = restore_uniforms(hlsl, {"buffers": [], "bindings": []}, [{"systemValue": 0, "register": 0, "semantic": "POSITION", "semanticIndex": 0}])
        self.assertIn("v0 : POSITION0", actual)

    def test_unknown_interface_trailing_payload_is_rejected(self):
        # SourceMap, zero channels, zero buffers, zero bindings.
        raw = struct.pack("<4i", 0, 0, 0, 0)
        self.assertTrue(parameter_delta(raw, 0)["allOriginalInterfaceBytesConsumed"])
        with self.assertRaisesRegex(ShaderRecoveryError, "trailing"):
            parameter_delta(raw + b"\0\0\0\0", 0)


if __name__ == "__main__":
    unittest.main()
