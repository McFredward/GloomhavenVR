"""Exercise UI recovery provenance, no unrelated edits, cache and failure boundaries.

Fixtures are invented shader/recipe exports. The helper's immutable production
fingerprints are tested independently against private owned-game recovery, so
these tests never redistribute original game asset data.
"""
import copy
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


class OriginalUiRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.project = Path(self.temp.name) / "generated"
        (self.project / "Assets/Quest").mkdir(parents=True)
        specs = copy.deepcopy(ui_assets.SHADERS)
        rows = []
        for name, spec in specs.items():
            dummy = ('Shader "' + name + '" {\n Properties { _Invented ("Fixture", Float) = 0.25 }\n'
                     '\t//DummyShaderTextExporter\n SubShader { Pass {} }\n}\n').encode()
            form = {"m_Name": name, "fixtureProperties": ["_Invented"], "fixtureState": "source-only"}
            spec["dummySha256"] = hashlib.sha256(dummy).hexdigest()
            spec["sourceSha256"] = hashlib.sha256((dummy.decode().partition("\t//DummyShaderTextExporter")[0] + spec["program"]).encode()).hexdigest()
            spec["formSha256"] = hashlib.sha256(json.dumps(form, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            target = self.project / spec["asset"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(dummy)
            Path(str(target) + ".meta").write_text("fileFormatVersion: 2\nguid: " + spec["guid"] + "\nShaderImporter:\n  userData: fixture retained\n")
            rows.append({"compiledPlatforms": [4], "parsedForm": form, "recipeSha256": spec["recipeSha256"]})
        self.specs = specs
        self.patcher = patch.object(ui_assets, "SHADERS", specs)
        self.patcher.start()
        storage.write_json(self.project / ui_assets.RECIPES, {"schema": 1, "recipes": rows})
        self.other = self.project / "Assets/Material/owned-material.mat"
        self.other.parent.mkdir()
        self.other.write_bytes(b"original texture refs, colors, keyword, native scene binding")
        self.before = {path: path.read_bytes() for path in self.project.rglob("*") if path.is_file()}

    def tearDown(self):
        self.patcher.stop()
        self.temp.cleanup()

    def untouched(self):
        for path, content in self.before.items():
            self.assertEqual(path.read_bytes(), content, str(path))
        self.assertFalse((self.project / ui_assets.RECEIPT).exists())

    def test_restore_exact_programs_retains_props_meta_material_scene_and_recipe(self):
        result = ui_assets.stage_startup_ui(self.project)
        self.assertEqual(result["recipe"], "original-ui-dxbc-port-v1")
        self.assertEqual(len(result["shaders"]), 2)
        self.assertFalse(result["androidShaderCompiled"])
        self.assertFalse(result["originalPixelParityVerified"])
        self.assertEqual(json.loads((self.project / ui_assets.RECEIPT).read_text()), result)
        changed = {self.project / spec["asset"] for spec in self.specs.values()}
        for path, original in self.before.items():
            if path in changed:
                self.assertTrue(path.read_text().startswith(original.decode().partition("\t//DummyShaderTextExporter")[0]))
                self.assertNotIn("DummyShaderTextExporter", path.read_text())
            else:
                self.assertEqual(path.read_bytes(), original)
        for row in result["shaders"]:
            self.assertEqual(storage.digest(self.project / row["assetPath"]), row["sourceSha256"])
            self.assertEqual(storage.digest(Path(str(self.project / row["assetPath"]) + ".meta")), row["metaSha256"])

    def test_cached_stage_is_exact_and_does_not_rewrite(self):
        first = ui_assets.stage_startup_ui(self.project)
        timestamps = {path: path.stat().st_mtime_ns for path in self.project.rglob("*") if path.is_file()}
        self.assertEqual(ui_assets.stage_startup_ui(self.project), first)
        self.assertEqual({path: path.stat().st_mtime_ns for path in timestamps}, timestamps)

    def test_shader_recipe_property_state_or_bank_changes_fail_before_mutation(self):
        path = self.project / ui_assets.RECIPES
        baseline = path.read_bytes()
        for mutation in ("form", "hash", "platform", "duplicate", "absent"):
            with self.subTest(mutation=mutation):
                source = json.loads(baseline)
                if mutation == "form": source["recipes"][1]["parsedForm"]["fixtureState"] = "invented transparent"
                elif mutation == "hash": source["recipes"][1]["recipeSha256"] = "0" * 64
                elif mutation == "platform": source["recipes"][1]["compiledPlatforms"] = [9]
                elif mutation == "duplicate": source["recipes"].append(source["recipes"][1])
                else: source["recipes"].pop()
                storage.write_json(path, source)
                with self.assertRaisesRegex(storage.BuildError, "recipe differs"):
                    ui_assets.stage_startup_ui(self.project)
                path.write_bytes(baseline)
                self.untouched()

    def test_later_wrong_shader_meta_or_dummy_never_partially_restores_first(self):
        spec = self.specs["UI/Dissolve mask"]
        for relative, replacement in ((spec["asset"], b"unexpected export"), (spec["asset"] + ".meta", b"guid: " + b"0" * 32 + b"\nShaderImporter:")):
            target = self.project / relative
            original = target.read_bytes()
            target.write_bytes(replacement)
            with self.assertRaises(storage.BuildError):
                ui_assets.stage_startup_ui(self.project)
            target.write_bytes(original)
            self.untouched()

    def test_changed_cached_source_or_receipt_is_rejected(self):
        ui_assets.stage_startup_ui(self.project)
        target = self.project / self.specs["UI/Dissolve mask"]["asset"]
        original = target.read_bytes()
        target.write_bytes(original + b"// arbitrary edit\n")
        with self.assertRaisesRegex(storage.BuildError, "Previous restored"):
            ui_assets.stage_startup_ui(self.project)
        target.write_bytes(original)
        receipt_path = self.project / ui_assets.RECEIPT
        receipt = json.loads(receipt_path.read_text())
        receipt["originalPixelParityVerified"] = True
        storage.write_json(receipt_path, receipt)
        with self.assertRaisesRegex(storage.BuildError, "receipt differs"):
            ui_assets.stage_startup_ui(self.project)

    def test_receipt_failure_rolls_back_both_assets_and_removes_temporary_files(self):
        with patch.object(ui_assets, "write_json", side_effect=OSError("fixture write failure")):
            with self.assertRaisesRegex(OSError, "fixture write failure"):
                ui_assets.stage_startup_ui(self.project)
        self.untouched()
        self.assertFalse(list(self.project.rglob("*.restore")))

    def test_linked_shader_or_receipt_source_is_rejected(self):
        if sys.platform == "win32": self.skipTest("Symbolic links need developer privileges.")
        target = self.project / self.specs["UI/Dissolve mask"]["asset"]
        outside = Path(self.temp.name) / "private-owned.shader"
        outside.write_bytes(target.read_bytes())
        target.unlink(); target.symlink_to(outside)
        with self.assertRaisesRegex(storage.BuildError, "Linked"):
            ui_assets.stage_startup_ui(self.project)
        self.assertEqual(outside.read_bytes(), self.before[target])
        self.assertFalse((self.project / ui_assets.RECEIPT).exists())


if __name__ == "__main__":
    unittest.main()
