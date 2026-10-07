"""Separate blur provenance/rollback tests with invented owned-game exports."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import storage
import ui_assets
import ui_blur


class OriginalBlurRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.project = Path(self.temp.name) / "generated"
        (self.project / "Assets/Quest").mkdir(parents=True)
        self.shader = self.project / ui_blur.ASSET
        self.shader.parent.mkdir(parents=True)
        self.dummy = ('Shader "Custom/SimpleGrabPassBlur" {\n Properties {\n'
                      ' _Color ("Tint", Vector) = (1,1,1,1)\n }\n'
                      '\t//DummyShaderTextExporter\n SubShader { Pass {} }\n}\n').encode()
        self.shader.write_bytes(self.dummy)
        self.meta = Path(str(self.shader) + ".meta")
        self.meta.write_text("fileFormatVersion: 2\nguid: " + ui_blur.GUID + "\nShaderImporter:\n  userData: original\n")
        self.form = {"m_Name": ui_blur.NAME, "fixturePassStates": "opaque-source-owned"}
        self.row = {"compiledPlatforms": [4], "recipeSha256": ui_blur.RECIPE_SHA256, "parsedForm": self.form}
        storage.write_json(self.project / ui_blur.RECIPES, {"recipes": [self.row]})
        restored = self.dummy.decode().partition("\t//DummyShaderTextExporter")[0].replace(', Vector)', ', Color)') + ui_blur.PROGRAM
        self.patches = [patch.object(ui_blur, "DUMMY_SHA256", hashlib.sha256(self.dummy).hexdigest()),
                        patch.object(ui_blur, "SOURCE_SHA256", hashlib.sha256(restored.encode()).hexdigest()),
                        patch.object(ui_blur, "FORM_SHA256", hashlib.sha256(json.dumps(self.form, sort_keys=True, separators=(",", ":")).encode()).hexdigest())]
        for item in self.patches: item.start()
        self.other = self.project / "Assets/native-scene-material.bin"
        self.other.write_bytes(b"unchanged native image, Canvas alpha, CullTransparentMesh and material")
        self.before = {path: path.read_bytes() for path in self.project.rglob("*") if path.is_file()}

    def tearDown(self):
        for item in self.patches: item.stop()
        self.temp.cleanup()

    def unchanged(self):
        self.assertEqual(self.shader.read_bytes(), self.dummy)
        self.assertFalse((self.project / ui_blur.RECEIPT).exists())
        for path, content in self.before.items(): self.assertEqual(path.read_bytes(), content)

    def test_separate_restore_preserves_original_scene_material_meta_and_two_shader_receipt(self):
        legacy = self.project / ui_assets.RECEIPT
        legacy.parent.mkdir(parents=True)
        legacy.write_bytes(b"two shader receipt stays byte-identical")
        result = ui_assets.stage_startup_blur(self.project)
        self.assertEqual(result["drawPassCount"], 3)
        self.assertEqual(result["grabPassCount"], 3)
        self.assertFalse(result["androidShaderCompiled"])
        self.assertFalse(result["originalPixelParityVerified"])
        self.assertEqual(legacy.read_bytes(), b"two shader receipt stays byte-identical")
        for path, content in self.before.items():
            if path != self.shader: self.assertEqual(path.read_bytes(), content)
        self.assertEqual(self.shader.read_text().count("GrabPass { }"), 3)
        self.assertNotIn(" : COLOR", self.shader.read_text())
        self.assertIn("Blend One Zero", self.shader.read_text())
        timestamps = {path: path.stat().st_mtime_ns for path in self.project.rglob("*") if path.is_file()}
        self.assertEqual(ui_blur.stage_startup_blur(self.project), result)
        self.assertEqual({path: path.stat().st_mtime_ns for path in timestamps}, timestamps)

    def test_same_named_unused_original_shader_is_not_ambiguous(self):
        source = {"recipes": [self.row, dict(self.row, recipeSha256="unused-other-identity")]}
        storage.write_json(self.project / ui_blur.RECIPES, source)
        self.assertEqual(ui_blur.stage_startup_blur(self.project)["guid"], ui_blur.GUID)

    def test_changed_original_form_platform_recipe_and_duplicate_fail_before_mutation(self):
        source_path = self.project / ui_blur.RECIPES
        original = source_path.read_bytes()
        for mutation in ("form", "platform", "identity", "duplicate", "missing"):
            with self.subTest(mutation=mutation):
                source = json.loads(original)
                if mutation == "form": source["recipes"][0]["parsedForm"]["fixturePassStates"] = "invented-alpha"
                elif mutation == "platform": source["recipes"][0]["compiledPlatforms"] = [9]
                elif mutation == "identity": source["recipes"][0]["recipeSha256"] = "0" * 64
                elif mutation == "duplicate": source["recipes"].append(source["recipes"][0])
                else: source["recipes"] = []
                storage.write_json(source_path, source)
                with self.assertRaisesRegex(storage.BuildError, "source contract differs"):
                    ui_blur.stage_startup_blur(self.project)
                source_path.write_bytes(original)
                self.unchanged()

    def test_changed_dummy_and_guid_fail_before_mutation(self):
        for path, replacement in ((self.shader, b"not-a-native-export"), (self.meta, b"guid: " + b"0" * 32 + b"\nShaderImporter:")):
            original = path.read_bytes()
            path.write_bytes(replacement)
            with self.assertRaises(storage.BuildError): ui_blur.stage_startup_blur(self.project)
            path.write_bytes(original)
            self.unchanged()

    def test_cached_source_or_receipt_change_is_rejected(self):
        ui_blur.stage_startup_blur(self.project)
        original = self.shader.read_bytes()
        self.shader.write_bytes(original + b"// arbitrary edit")
        with self.assertRaisesRegex(storage.BuildError, "Previous restored"):
            ui_blur.stage_startup_blur(self.project)
        self.shader.write_bytes(original)
        path = self.project / ui_blur.RECEIPT
        source = json.loads(path.read_text()); source["originalPixelParityVerified"] = True
        storage.write_json(path, source)
        with self.assertRaisesRegex(storage.BuildError, "receipt differs"):
            ui_blur.stage_startup_blur(self.project)

    def test_receipt_write_failure_restores_source_and_removes_temporary_file(self):
        with patch.object(ui_blur, "write_json", side_effect=OSError("write control")):
            with self.assertRaisesRegex(OSError, "write control"):
                ui_blur.stage_startup_blur(self.project)
        self.unchanged()
        self.assertFalse(list(self.project.rglob("*.restore")))

    def test_linked_shader_never_mutates_external_game_file(self):
        if sys.platform == "win32": self.skipTest("Symbolic links require developer privileges")
        external = Path(self.temp.name) / "original.shader"
        external.write_bytes(self.dummy)
        self.shader.unlink(); self.shader.symlink_to(external)
        with self.assertRaisesRegex(storage.BuildError, "Linked"):
            ui_blur.stage_startup_blur(self.project)
        self.assertEqual(external.read_bytes(), self.dummy)
        self.assertFalse((self.project / ui_blur.RECEIPT).exists())


if __name__ == "__main__":
    unittest.main()
