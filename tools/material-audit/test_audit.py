"""Focused negative controls for identity-safe catalog construction, not pixels."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("native_material_audit", Path(__file__).with_name("audit.py"))
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


class ReferenceTests(unittest.TestCase):
    def setUp(self):
        self.files = {
            "root::resources.assets": {"normalizedName": "resources.assets", "externals": [{"path": "archive:/CAB-a/CAB-a"}]},
            "a.bundle::CAB-a": {"normalizedName": "cab-a", "externals": []},
            "b.bundle::CAB-b": {"normalizedName": "cab-b", "externals": []}}
        self.material = {"serializedFile": "root::resources.assets", "shaderPPtr": {"m_FileID": 1, "m_PathID": 7}}
        self.shaders = {
            ("a.bundle::CAB-a", 7): [{"id": "a7", "rawSha256": "bytes-a", "name": "Amp_Basic_N_MRAO"}],
            ("b.bundle::CAB-b", 7): [{"id": "b7", "rawSha256": "bytes-b", "name": "UI/Default"}]}

    def test_external_pathid_collision_uses_file(self):
        r = AUDIT.resolve_shader(self.material, self.files, self.shaders)
        self.assertEqual(r["targets"], ["a7"])
        self.assertEqual(r["name"], "Amp_Basic_N_MRAO")

    def test_local_pathid_collision_cannot_resolve_global(self):
        self.material["shaderPPtr"]["m_FileID"] = 0
        self.assertEqual(AUDIT.resolve_shader(self.material, self.files, self.shaders)["status"], "unresolved")

    def test_missing_external_not_guessed_from_pathid(self):
        self.files["root::resources.assets"]["externals"][0]["path"] = "archive:/CAB-missing/CAB-missing"
        self.assertEqual(AUDIT.resolve_shader(self.material, self.files, self.shaders)["status"], "unresolved")

    def test_invalid_external_file_index(self):
        self.material["shaderPPtr"]["m_FileID"] = 2
        self.assertEqual(AUDIT.resolve_shader(self.material, self.files, self.shaders)["status"], "invalid-file-id")

    def test_duplicate_cab_conflicting_payload_rejected(self):
        self.files["other.bundle::CAB-a"] = {"normalizedName": "cab-a", "externals": []}
        self.shaders[("other.bundle::CAB-a", 7)] = [{"id": "other7", "rawSha256": "changed", "name": "Amp_Basic_N_MRAO"}]
        self.assertEqual(AUDIT.resolve_shader(self.material, self.files, self.shaders)["status"], "ambiguous")

    def test_duplicate_cab_identical_payload_retains_provenance(self):
        self.files["other.bundle::CAB-a"] = {"normalizedName": "cab-a", "externals": []}
        self.shaders[("other.bundle::CAB-a", 7)] = [{"id": "other7", "rawSha256": "bytes-a", "name": "Amp_Basic_N_MRAO"}]
        r = AUDIT.resolve_shader(self.material, self.files, self.shaders)
        self.assertEqual(r["status"], "resolved")
        self.assertEqual(set(r["targets"]), {"a7", "other7"})

    def test_root_and_builtin_source_inclusion(self):
        from tempfile import TemporaryDirectory
        with TemporaryDirectory() as folder:
            root = Path(folder)
            for name in ["resources.assets", "sharedassets11.assets", "globalgamemanagers", "level10",
                         "Resources/unity_builtin_extra", "Resources/unity default resources",
                         "StreamingAssets/aa/DLC/x.bundle", "resources.resource", "level10.resS", "Managed/game.dll"]:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"fixture")
            paths = {str(p.relative_to(root)) for p in AUDIT.source_paths(root)}
            self.assertEqual(paths, {"resources.assets", "sharedassets11.assets", "globalgamemanagers", "level10",
                                     "Resources/unity_builtin_extra", "Resources/unity default resources",
                                     "StreamingAssets/aa/DLC/x.bundle"})

    def test_null_shader_explicit(self):
        self.material["shaderPPtr"]["m_PathID"] = 0
        self.assertEqual(AUDIT.resolve_shader(self.material, self.files, self.shaders)["status"], "null-shader")


class MaterialGateTests(unittest.TestCase):
    def setUp(self):
        self.material = {"customRenderQueue": -1, "shaderResolution": {"status": "resolved"},
                         "properties": {"m_Floats": []}, "disabledShaderPasses": []}
        self.shader = {"name": "Amp_Basic_N_MRAO", "subShaders": [{"tags": {"QUEUE": "AlphaTest+0"}}]}

    def test_native_default_queue(self):
        self.assertEqual(AUDIT.queue_value(self.material, self.shader), 2450)
        self.assertEqual(AUDIT.exclusion_reasons(self.material, self.shader), [])

    def test_custom_transparent_queue(self):
        self.material["customRenderQueue"] = 3000
        self.assertIn("transparent-or-overlay-queue", AUDIT.exclusion_reasons(self.material, self.shader))

    def test_unknown_queue_stays_unknown(self):
        self.shader["subShaders"][0]["tags"]["QUEUE"] = "unrecognized"
        self.assertIn("unknown-render-queue", AUDIT.exclusion_reasons(self.material, self.shader))

    def test_low_emission_and_animation_veto(self):
        self.shader["name"] = "Amp_Low/Amp_Basic_N_MRAO_Low"
        self.material["properties"]["m_Floats"] = [["_EmissionMap", 1], ["_UseTextureEmission", 1], ["_AddVertexAnim", 1]]
        reasons = AUDIT.exclusion_reasons(self.material, self.shader)
        self.assertEqual(set(reasons), {"active-_EmissionMap", "active-_UseTextureEmission", "active-_AddVertexAnim"})

    def test_unreviewed_family_stays_excluded(self):
        self.shader["name"] = "Amp_Basic_Prop_Shader"
        self.assertEqual(AUDIT.exclusion_reasons(self.material, self.shader), ["unreviewed-native-family"])

    def test_basic_dynamic_cutout_veto(self):
        self.shader["name"] = "Amp_Basic"
        self.material["properties"]["m_Floats"] = [["_ToggleDissolve", 1], ["_Cutout_VertexPos_Influence", .1]]
        self.assertEqual(set(AUDIT.exclusion_reasons(self.material, self.shader)),
                         {"active-_ToggleDissolve", "active-_Cutout_VertexPos_Influence"})

    def test_standard_fade_emission_details_stay_native(self):
        self.shader["name"] = "Standard"
        self.material["properties"]["m_Floats"] = [["_Mode", 2], ["_SrcBlend", 5], ["_ZWrite", 0]]
        self.material["properties"]["m_Colors"] = [["_EmissionColor", {"r": 1, "g": 0, "b": 0, "a": 1}]]
        self.material["validKeywords"] = ["_DETAIL_MULX2"]
        self.assertEqual(set(AUDIT.exclusion_reasons(self.material, self.shader)),
                         {"unsupported-standard-_Mode", "unsupported-standard-_SrcBlend", "unsupported-standard-_ZWrite",
                          "active-keyword-_DETAIL_MULX2", "nonzero-emission-color"})


if __name__ == "__main__":
    unittest.main()
