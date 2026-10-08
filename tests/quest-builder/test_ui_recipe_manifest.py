"""Qualify exact native UI recipe transport without weakening original programs.

Invented producer-shaped recipes keep game shader data out of the repository.
The independent private replay uses the unchanged production fingerprints.
"""
import copy
import hashlib
import json
from pathlib import Path
import re
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import storage
import ui_assets
import ui_blur


class UiRecipeManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.project = Path(self.temp.name) / "generated"
        self.directory = self.project / "QuestRecovery/ShaderRecipes"
        self.directory.mkdir(parents=True)
        self.specs = copy.deepcopy(ui_assets.SHADERS)
        self.inputs = {}
        for index, name in enumerate([*self.specs, ui_blur.NAME]):
            form = {"m_Name": name, "m_PropInfo": {"m_Props": [{"m_Name": "fixture", "m_DefaultValue": [1, 2, 3, 4]}]},
                    "m_SubShaders": [{"m_Passes": [{"m_State": {"blend": "unchanged"}, "program": "fixture compiled DXBC bank\\r\\n"}]}]}
            recipe = {"sourcePath": {"C": {"B": {"P": []}, "I": index}, "D": 100 + index}, "parsedForm": form, "compiledPlatforms": [4]}
            raw = (json.dumps(recipe, indent=2, sort_keys=True) + "\n").encode("utf-8")
            state = {"m_Name": name, "m_PropInfo": form["m_PropInfo"], "m_SubShaders": [{"m_Passes": [{"m_State": {"blend": "unchanged"}}]}]}
            recipe_hash = hashlib.sha256(raw).hexdigest()
            form_hash = hashlib.sha256(json.dumps(state, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            if name in self.specs: self.specs[name].update(recipeSha256=recipe_hash, formSha256=form_hash)
            else: self.blur_hash, self.blur_form = recipe_hash, form_hash
            path = self.directory / (re.sub(r"[^A-Za-z0-9._-]+", "_", name) + "-fixture.json")
            path.write_bytes(raw); self.inputs[path] = raw
        self.patches = [patch.object(ui_assets, "SHADERS", self.specs), patch.object(ui_blur, "RECIPE_SHA256", self.blur_hash), patch.object(ui_blur, "FORM_SHA256", self.blur_form)]
        for item in self.patches: item.start()

    def tearDown(self):
        for item in reversed(self.patches): item.stop()
        self.temp.cleanup()

    def test_lf_and_real_windows_text_writer_share_exact_recipe_contract(self):
        lf = ui_assets.stage_campaign_recipe_manifest(self.project)
        for path, raw in self.inputs.items():
            # This is the producer's actual Windows text-mode byte transport.
            with path.open("w", encoding="utf-8", newline="\r\n") as stream: stream.write(raw.decode())
        crlf = ui_assets.stage_campaign_recipe_manifest(self.project)
        self.assertEqual(len(lf["recipes"]), 3)
        for first, second in zip(lf["recipes"], crlf["recipes"]):
            self.assertEqual(first["recipeSha256"], second["recipeSha256"])
            self.assertEqual(first["parsedForm"], second["parsedForm"])
            self.assertEqual(second["recipeLineEndings"], "CRLF")
            path = self.project / second["recipePath"]
            self.assertEqual(second["sourceRecipeFileSha256"], hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertEqual(second["sourceRecipeFileBytes"], path.stat().st_size)
            self.assertNotEqual(first["sourceRecipeFileSha256"], second["sourceRecipeFileSha256"])
            self.assertEqual(path.read_bytes(), self.inputs[path].replace(b"\n", b"\r\n"))

    def test_missing_modified_banks_locators_or_transport_never_replace_previous_manifest(self):
        previous = ui_assets.stage_campaign_recipe_manifest(self.project)
        manifest = self.project / ui_assets.RECIPES; old = manifest.read_bytes()
        path, raw = next(iter(self.inputs.items()))
        for mutation in ("missing", "program", "locator", "platform", "whitespace", "mixed-newlines", "bare-cr", "bom"):
            with self.subTest(mutation=mutation):
                if mutation == "missing": path.unlink()
                elif mutation in ("program", "locator", "platform"):
                    value = json.loads(raw)
                    if mutation == "program": value["parsedForm"]["m_SubShaders"][0]["m_Passes"][0]["program"] += " changed operation"
                    elif mutation == "locator": value["sourcePath"]["D"] += 1
                    else: value["compiledPlatforms"] = [9]
                    path.write_bytes((json.dumps(value, indent=2, sort_keys=True) + "\n").encode())
                elif mutation == "whitespace": path.write_bytes(raw.replace(b"  ", b"   ", 1))
                elif mutation == "mixed-newlines": path.write_bytes(raw.replace(b"\n", b"\r\n", 1))
                elif mutation == "bare-cr": path.write_bytes(raw.replace(b"\n", b"\r", 1))
                else: path.write_bytes(b"\xef\xbb\xbf" + raw)
                with self.assertRaisesRegex(storage.BuildError, "lacks the exact original"):
                    ui_assets.stage_campaign_recipe_manifest(self.project)
                self.assertEqual(manifest.read_bytes(), old)
                path.write_bytes(raw)
        self.assertEqual(json.loads(old), previous)

    def test_duplicate_lf_and_crlf_physical_objects_remain_an_error(self):
        path, raw = next(iter(self.inputs.items()))
        path.with_name(path.stem + "-duplicate.json").write_bytes(raw.replace(b"\n", b"\r\n"))
        with self.assertRaisesRegex(storage.BuildError, "duplicates"):
            ui_assets.stage_campaign_recipe_manifest(self.project)
        self.assertFalse((self.project / ui_assets.RECIPES).exists())

    def test_unrelated_shader_banks_are_never_opened(self):
        (self.directory / "UnrelatedHugeBank-other.json").write_bytes(b"invalid unrelated JSON")
        original = ui_assets._read; calls = []
        def read(path, maximum):
            calls.append(path.name); return original(path, maximum)
        with patch.object(ui_assets, "_read", side_effect=read): ui_assets.stage_campaign_recipe_manifest(self.project)
        self.assertNotIn("UnrelatedHugeBank-other.json", calls)
        self.assertEqual(sum(name.endswith("-fixture.json") for name in calls), 3)

    def test_linked_audited_family_is_rejected(self):
        if sys.platform == "win32": self.skipTest("Symbolic links require developer privileges.")
        path, raw = next(iter(self.inputs.items())); outside = Path(self.temp.name) / "outside.json"; outside.write_bytes(raw)
        path.unlink(); path.symlink_to(outside)
        with self.assertRaisesRegex(storage.BuildError, "Linked"):
            ui_assets.stage_campaign_recipe_manifest(self.project)
        self.assertEqual(outside.read_bytes(), raw)
        self.assertFalse((self.project / ui_assets.RECIPES).exists())


if __name__ == "__main__": unittest.main()
