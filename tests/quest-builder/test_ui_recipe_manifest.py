"""Qualify native UI content independent of export-session addresses/JSON text.

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
            native_hash = hashlib.sha256(json.dumps({"parsedForm": form, "compiledPlatforms": [4]}, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            form_hash = hashlib.sha256(json.dumps(state, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            if name in self.specs: self.specs[name].update(recipeSha256=recipe_hash, formSha256=form_hash, nativeRecipeSha256=native_hash)
            else: self.blur_hash, self.blur_form, self.blur_native = recipe_hash, form_hash, native_hash
            path = self.directory / (re.sub(r"[^A-Za-z0-9._-]+", "_", name) + "-fixture.json")
            path.write_bytes(raw); self.inputs[path] = raw
        self.patches = [patch.object(ui_assets, "SHADERS", self.specs), patch.object(ui_blur, "RECIPE_SHA256", self.blur_hash), patch.object(ui_blur, "FORM_SHA256", self.blur_form), patch.object(ui_blur, "NATIVE_RECIPE_SHA256", self.blur_native)]
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

    def test_missing_modified_banks_properties_passes_or_platform_never_replace_previous_manifest(self):
        previous = ui_assets.stage_campaign_recipe_manifest(self.project)
        manifest = self.project / ui_assets.RECIPES; old = manifest.read_bytes()
        path, raw = next(iter(self.inputs.items()))
        for mutation in ("missing", "program", "property", "pass", "name", "platform", "invalid-json", "duplicate-json-key"):
            with self.subTest(mutation=mutation):
                if mutation == "missing": path.unlink()
                elif mutation in ("program", "property", "pass", "name", "platform"):
                    value = json.loads(raw)
                    if mutation == "program": value["parsedForm"]["m_SubShaders"][0]["m_Passes"][0]["program"] += " changed operation"
                    elif mutation == "property": value["parsedForm"]["m_PropInfo"]["m_Props"][0]["m_DefaultValue"][0] += 1
                    elif mutation == "pass": value["parsedForm"]["m_SubShaders"][0]["m_Passes"][0]["m_State"]["blend"] = "changed"
                    elif mutation == "name": value["parsedForm"]["m_Name"] = "unqualified same-family shader"
                    else: value["compiledPlatforms"] = [9]
                    path.write_bytes((json.dumps(value, indent=2, sort_keys=True) + "\n").encode())
                elif mutation == "invalid-json": path.write_bytes(b"not JSON")
                else: path.write_bytes(raw.replace(b'{\n', b'{\n "sourcePath": {},\n', 1))
                with self.assertRaisesRegex(storage.BuildError, "lacks the exact original"):
                    ui_assets.stage_campaign_recipe_manifest(self.project)
                self.assertEqual(manifest.read_bytes(), old)
                path.write_bytes(raw)
        self.assertEqual(json.loads(old), previous)

    def test_session_local_collection_indices_and_json_format_do_not_change_native_contract(self):
        original = ui_assets.stage_campaign_recipe_manifest(self.project)
        for path, raw in self.inputs.items():
            value = json.loads(raw)
            value["sourcePath"] = {"C": {"B": {"P": [7]}, "I": 97}, "D": -8456927755942251258}
            # Different importer load orders and valid JSON transports retain
            # exactly the same original program/property/pass/platform fields.
            path.write_bytes(b"\xef\xbb\xbf" + json.dumps(value, sort_keys=False, indent=4).encode())
        changed = ui_assets.stage_campaign_recipe_manifest(self.project)
        for before, after in zip(original["recipes"], changed["recipes"]):
            self.assertEqual(before["recipeSha256"], after["recipeSha256"])
            self.assertEqual(before["nativeRecipeSha256"], after["nativeRecipeSha256"])
            self.assertEqual(before["parsedForm"], after["parsedForm"])
            self.assertNotEqual(before["sourceRecipeFileSha256"], after["sourceRecipeFileSha256"])
            self.assertEqual(after["sourcePath"]["C"]["I"], 97)

    def test_equivalent_physical_objects_preserve_all_provenance_and_select_one_program(self):
        path, raw = next(iter(self.inputs.items()))
        value = json.loads(raw); value["sourcePath"]["C"]["I"] += 100
        path.with_name(path.stem + "-duplicate.json").write_bytes(json.dumps(value, indent=4).encode())
        manifest = ui_assets.stage_campaign_recipe_manifest(self.project)
        self.assertEqual(len(manifest["recipes"]), 3)
        row = next(row for row in manifest["recipes"] if row["parsedForm"]["m_Name"] == value["parsedForm"]["m_Name"])
        self.assertEqual(len(row["equivalentSources"]), 2)
        self.assertNotEqual(row["equivalentSources"][0]["sourceRecipeFileSha256"], row["equivalentSources"][1]["sourceRecipeFileSha256"])
        self.assertEqual(ui_assets.stage_campaign_recipe_manifest(self.project), manifest)

    def test_failure_names_bounded_rejected_candidates_without_program_data(self):
        path, raw = next(iter(self.inputs.items()))
        for slot in range(8):
            value = json.loads(raw); value["compiledPlatforms"] = [9]
            path.with_name(path.stem + "-" + str(slot) + ".json").write_bytes(json.dumps(value).encode())
        path.unlink()
        with self.assertRaises(storage.BuildError) as error:
            ui_assets.stage_campaign_recipe_manifest(self.project)
        message = str(error.exception)
        self.assertIn("Candidate diagnostics", message)
        self.assertIn("fingerprint differs", message)
        self.assertIn("nativeRecipeSha256", message)
        self.assertNotIn("fixture compiled DXBC", message)
        details = json.loads(message.split("Candidate diagnostics: ", 1)[1])
        self.assertEqual(len(next(iter(details.values()))["candidates"]), 3)

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
