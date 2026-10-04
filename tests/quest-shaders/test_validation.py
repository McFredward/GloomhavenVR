"""Contract and planted-defect tests for the production shader evidence gate."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-shaders"))
import manifest
import run
import gles


class EvidenceContract(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.shader = self.root / "Assets/Shader/Original.shader"
        self.shader.parent.mkdir(parents=True)
        self.shader.write_text('Shader "Original" {}\n')
        self.shader.with_suffix(".shader.meta").write_text("guid: " + "a" * 32 + "\n")
        self.data = {"schema": 1, "scope": "campaign", "requiredMaterialCount": 1,
            "requiredFeatures": ["geometry", "lighting", "texture", "alpha", "uv", "animation", "stereo"],
            "shaders": [{"guid": "a" * 32, "assetPath": "Assets/Shader/Original.shader", "originalName": "Original",
                "originalSerializedFile": "CAB-original", "originalPathId": -128, "sourceSha256": manifest.sha256(self.shader),
                "variants": [{"subshader": 0, "pass": 0, "keywords": [], "stereo": "mono",
                    "vertexOriginalDxbcSha256": "1" * 64, "fragmentOriginalDxbcSha256": "2" * 64}]}],
            "materials": [{"guid": "b" * 32, "assetPath": "Assets/Material/Original.mat", "shaderGuid": "a" * 32}],
            "renderCases": [{"id": "original", "shaderGuid": "a" * 32, "materialGuid": "b" * 32, "meshGuid": "c" * 32,
                "features": ["geometry", "lighting", "texture", "alpha", "uv", "animation", "stereo"],
                "originalMaterial": {"assetName": "Assets/Original.prefab", "bundlePaths": [{"path": "original.bundle", "sha256": "3" * 64}]},
                "originalMesh": {"assetName": "Assets/Original.prefab", "bundlePaths": [{"path": "original.bundle", "sha256": "3" * 64}]}}]}
        self.path = self.root / "campaign-shaders.json"

    def tearDown(self):
        self.temp.cleanup()

    def load(self, data=None):
        self.path.write_text(json.dumps(self.data if data is None else data))
        return manifest.load(self.path, self.root)

    def test_exact_source_identity_contract(self):
        self.assertEqual(self.load()["shaders"][0]["originalPathId"], -128)

    def test_original_source_and_meta_mutations_rejected(self):
        self.shader.write_text('Shader "Replacement" {}\n')
        with self.assertRaisesRegex(manifest.ValidationError, "bytes differ"):
            self.load()
        self.data["shaders"][0]["sourceSha256"] = manifest.sha256(self.shader)
        self.shader.with_suffix(".shader.meta").write_text("guid: " + "d" * 32 + "\n")
        with self.assertRaisesRegex(manifest.ValidationError, "GUID differs"):
            self.load()

    def test_planted_identity_coverage_defects(self):
        mutations = [
            ("scope", lambda d: d.update(scope="startup")),
            ("count", lambda d: d.update(requiredMaterialCount=2)),
            ("duplicate-shader", lambda d: d["shaders"].append(copy.deepcopy(d["shaders"][0]))),
            ("duplicate-material", lambda d: d["materials"].append(copy.deepcopy(d["materials"][0]))),
            ("wrong-material-shader", lambda d: d["materials"][0].update(shaderGuid="d" * 32)),
            ("error-shader", lambda d: d["shaders"][0].update(originalName="Hidden/InternalErrorShader")),
            ("missing-cab", lambda d: d["shaders"][0].update(originalSerializedFile="")),
            ("traversal", lambda d: d["shaders"][0].update(assetPath="Assets/../Original.shader")),
            ("duplicate-variant", lambda d: d["shaders"][0]["variants"].append(copy.deepcopy(d["shaders"][0]["variants"][0]))),
            ("fragment-provenance", lambda d: d["shaders"][0]["variants"][0].update(fragmentOriginalDxbcSha256="")),
            ("multiview-bank", lambda d: d["shaders"][0]["variants"][0].update(stereo="multiview")),
            ("duplicate-keyword", lambda d: d["shaders"][0]["variants"][0].update(keywords=["FOG_LINEAR", "FOG_LINEAR"])),
            ("wrong-pass", lambda d: d["shaders"][0]["variants"][0].update({"pass": -1})),
            ("unknown-depth-output", lambda d: d["shaders"][0]["variants"][0].update(fragmentOutput="invented")),
            ("case-association", lambda d: d["renderCases"][0].update(shaderGuid="d" * 32)),
            ("no-original-bundle", lambda d: d["renderCases"][0]["originalMaterial"].update(bundlePaths=[])),
            ("unhashed-original-bundle", lambda d: d["renderCases"][0]["originalMesh"]["bundlePaths"][0].update(sha256="")),
            ("unknown-feature", lambda d: d["renderCases"][0].update(features=["state-only"])),
            ("missing-feature", lambda d: d.update(requiredFeatures=["geometry"])),
        ]
        for label, mutation in mutations:
            with self.subTest(label=label):
                data = copy.deepcopy(self.data)
                mutation(data)
                with self.assertRaises(manifest.ValidationError):
                    self.load(data)

    def compiler_receipt(self):
        return {"schema": 1, "sourceManifestSha256": "4" * 64, "unityVersion": "2021.3.5f1", "compilerPlatform": "GLES3x",
                "materialCount": 1, "originalPixelParityVerified": False, "headsetPictureVerified": False,
                "programs": [{"guid": "a" * 32, "subshader": 0, "pass": 0, "keywords": [], "stereo": "mono",
                              "vertexCompiled": True, "fragmentCompiled": True, "glesSha256": "5" * 64}]}

    def test_actual_receipt_requires_exact_bank_coverage(self):
        manifest.validate_receipt(self.data, "4" * 64, self.compiler_receipt())
        for change in (
            lambda r: r.update(sourceManifestSha256="9" * 64),
            lambda r: r.update(unityVersion="2021.3.6f1"),
            lambda r: r.update(programs=[]),
            lambda r: r["programs"].append(copy.deepcopy(r["programs"][0])),
            lambda r: r["programs"][0].update(fragmentCompiled=False),
            lambda r: r.update(originalPixelParityVerified=True),
            lambda r: r.update(headsetPictureVerified=True),
        ):
            receipt = self.compiler_receipt()
            change(receipt)
            with self.assertRaises(manifest.ValidationError):
                manifest.validate_receipt(self.data, "4" * 64, receipt)

    def test_actual_multiview_receipt_requires_observed_eye_routing(self):
        data = copy.deepcopy(self.data)
        data["shaders"][0]["variants"][0].update(keywords=["STEREO_MULTIVIEW_ON"], stereo="multiview")
        receipt = self.compiler_receipt()
        receipt["programs"][0].update(keywords=["STEREO_MULTIVIEW_ON"], stereo="multiview", vertexEyeRoutingObserved=False)
        with self.assertRaisesRegex(manifest.ValidationError, "eye routing"):
            manifest.validate_receipt(data, "4" * 64, receipt)

    def test_native_pixel_receipt_requires_all_pictures_and_controls(self):
        pictures = []
        for feature in ["baseline", *self.data["renderCases"][0]["features"]]:
            filename = feature + ".rgba"
            (self.root / filename).write_bytes(bytes([16, 32, 48, 255]) * 16)
            pictures.append({"caseId": "original", "feature": feature, "passed": True, "differingPixels": 0,
                "referenceForegroundPixels": 32 if feature != "alpha" else 0, "observedInputChangePixels": 8,
                "referenceFile": filename, "candidateFile": filename})
        receipt = {"schema": 1, "sourceManifestSha256": "4" * 64, "unityVersion": "2021.3.5f1", "graphicsDeviceType": "Direct3D11",
            "originalWindowsDxbcPixelsCompared": True, "allCasesPassed": True, "errors": [], "androidMultiviewPixelsVerified": False,
            "headsetPictureVerified": False, "pictures": pictures, "negativeControls": [
                {"caseId": "original", "defect": "native-error-shader", "rejected": True},
                {"caseId": "original", "defect": "one-sided-texture-binding", "rejected": True}]}
        run.validate_pixels(self.data, "4" * 64, receipt, self.root)
        for change in (
            lambda r: r["pictures"].pop(),
            lambda r: r["pictures"][0].update(referenceForegroundPixels=0),
            lambda r: r["pictures"][1].update(observedInputChangePixels=0),
            lambda r: r["negativeControls"].pop(),
            lambda r: r.update(androidMultiviewPixelsVerified=True),
            lambda r: r.update(errors=["native shader compile failed"]),
        ):
            broken = copy.deepcopy(receipt)
            change(broken)
            with self.assertRaises(manifest.ValidationError):
                run.validate_pixels(self.data, "4" * 64, broken, self.root)


class GlesDriverContract(unittest.TestCase):
    def test_missing_native_sections_rejected(self):
        for source in ("", "#ifdef VERTEX\n#version 300 es\n#endif\n", "#ifdef FRAGMENT\n#version 300 es\n#endif\n"):
            with self.assertRaises(gles.GlesError):
                gles.sections(source)

    def test_real_gles_compiler_negative_control(self):
        device = gles.Device(64, 64)
        shader = device.stage(0x8B31, "#version 300 es\nvoid main(){gl_Position=vec4(0,0,0,1);}\n")
        self.assertGreater(shader, 0)
        device.call["glDeleteShader"](shader)
        with self.assertRaisesRegex(gles.GlesError, "compiler rejected"):
            device.stage(0x8B31, "#version 300 es\nvoid main(){gl_Position=MissingOriginalPosition;}\n")


if __name__ == "__main__":
    unittest.main()
