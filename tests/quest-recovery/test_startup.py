"""Original startup closure, provenance and source shader safety controls."""
import base64
import copy
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import recover
import startup
import tmp_shaders


def catalog_fixture(path, key, class_name):
    keys = [(label, [0]) for label in startup.LABELS] + [(key, [0]), (path, [0])]
    key_bytes = bytearray(struct.pack("<i", len(keys)))
    bucket_bytes = bytearray(struct.pack("<i", len(keys)))
    for key, entries in keys:
        offset = len(key_bytes)
        raw = key.encode()
        key_bytes.extend(b"\0" + struct.pack("<i", len(raw)) + raw)
        bucket_bytes.extend(struct.pack("<ii", offset, len(entries)))
        bucket_bytes.extend(struct.pack("<" + "i" * len(entries), *entries))
    entry_bytes = struct.pack("<i7i", 1, 0, 0, -1, 0, -1, 3, 0)
    return {"m_KeyDataString": base64.b64encode(key_bytes).decode(),
            "m_BucketDataString": base64.b64encode(bucket_bytes).decode(),
            "m_EntryDataString": base64.b64encode(entry_bytes).decode(),
            "m_InternalIds": [path], "m_ProviderIds": ["BundledAssetProvider"],
            "m_resourceTypes": [{"m_AssemblyName": "UnityEngine.CoreModule", "m_ClassName": class_name}]}


class StartupContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.project = self.root / "project"
        self.project.mkdir()

    def tearDown(self):
        self.temp.cleanup()

    def asset(self, relative, content, guid):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content)
        path.with_name(path.name + ".meta").write_text("guid: " + guid + "\n")
        return path

    def test_closure_retains_callbacks_and_reports_real_missing_reference(self):
        scene = "Assets/Original.unity"
        text = "m_Script: {fileID: 123, guid: " + "1" * 32 + ", type: 3}\n  m_MethodName: ContinueOriginal\n  m_Target: {fileID: 17}\n"
        self.asset(scene, text, "2" * 32)
        self.asset("Assets/Plugins/Game.dll", "immutable original", "1" * 32)
        index, _ = startup.asset_index(self.project)
        selected, missing = startup.closure(self.project, [scene], index)
        self.assertEqual(selected, {scene, "Assets/Plugins/Game.dll"})
        self.assertEqual(missing, {})
        self.assertEqual((self.project / scene).read_text(), text)
        (self.project / "Assets/Plugins/Game.dll").unlink()
        index, _ = startup.asset_index(self.project)
        _, missing = startup.closure(self.project, [scene], index)
        self.assertEqual(missing, {"1" * 32: [scene]})

    def test_identity_collision_and_escaping_or_symlinked_source_fail(self):
        self.asset("Assets/A.asset", "", "1" * 32)
        self.asset("Assets/B.asset", "", "1" * 32)
        with self.assertRaisesRegex(recover.RecoveryError, "Ambiguous"):
            startup.asset_index(self.project)
        for path in ("../outside", "/etc/passwd", "Assets/../../outside", "Assets\\wrong"):
            with self.assertRaises(recover.RecoveryError):
                startup.safe_path(self.project, path)
        external = self.root / "external.asset"
        external.write_text("keep")
        (self.project / "link.asset").symlink_to(external)
        with self.assertRaisesRegex(recover.RecoveryError, "escapes"):
            startup.safe_path(self.project, "link.asset")

    def test_builder_hash_matches_external_canonical_input_format(self):
        rows = [{"path": "z", "bytes": 9, "sha256": "a" * 64}, {"path": "a", "bytes": 1, "sha256": "b" * 64}]
        expected = hashlib.sha256(b'{"files":[{"path":"a","sha256":"' + b'b' * 64 + b'","size":1},{"path":"z","sha256":"' + b'a' * 64 + b'","size":9}]}').hexdigest()
        self.assertEqual(startup.builder_fingerprint(rows), expected)
        rows[0]["sha256"] = "c" * 64
        self.assertNotEqual(startup.builder_fingerprint(rows), expected)

    def test_stale_asset_copy_never_passes_receipt_hash_check(self):
        source = self.root / "owned"
        source.write_bytes(b"same-size-original")
        expected = recover.sha256(source)
        target = self.root / "staged/asset"
        startup.verify_copy(source, target, expected)
        self.assertEqual(source.read_bytes(), target.read_bytes())
        source.write_bytes(b"same-size-modified")
        with self.assertRaisesRegex(recover.RecoveryError, "differs"):
            startup.verify_copy(source, target, expected)
        self.assertEqual(target.read_bytes(), b"same-size-original")

    def test_sprite_mapping_requires_original_render_key_not_name_similarity(self):
        original = "Assets/GUI/Portrait.png"
        sprite = "Assets/GUI/Portrait.asset"
        original_guid = "a" * 32
        path = self.asset(sprite, "--- !u!213 &21300000\nSprite:\n  m_RenderDataKey:\n    " + original_guid + ": 21300000\n", "b" * 32)
        catalog = self.root / "catalog.json"
        catalog.write_text(json.dumps(catalog_fixture(original, original_guid, "UnityEngine.Sprite")))
        index, paths = startup.asset_index(self.project)
        manifest, missing = startup.startup_associations(catalog, self.project, paths, index, {})
        self.assertEqual(missing, [])
        row = manifest["entries"][0]
        self.assertEqual(row["assetPath"], sprite)
        self.assertEqual(row["recoveredFileId"], 21300000)
        self.assertEqual(row["labels"], sorted(startup.LABELS))
        self.assertIn(original_guid, row["keys"])
        path.write_text(path.read_text().replace(original_guid, "c" * 32))
        _, missing = startup.startup_associations(catalog, self.project, paths, index, {})
        self.assertEqual(len(missing), 1)
        self.assertIsNone(missing[0]["assetPath"])

    def test_original_windows_path_case_mapping_requires_unique_full_path(self):
        self.asset("Assets/VFX/FireBlend_Shd.shader", 'Shader "Original/Fire" {}', "b" * 32)
        catalog = self.root / "catalog.json"
        catalog.write_text(json.dumps(catalog_fixture("Assets/VFX/FireBlend_shd.shader", "a" * 32, "UnityEngine.Shader")))
        index, paths = startup.asset_index(self.project)
        manifest, missing = startup.startup_associations(catalog, self.project, paths, index, {})
        self.assertEqual(missing, [])
        self.assertEqual(manifest["entries"][0]["assetPath"], "Assets/VFX/FireBlend_Shd.shader")
        self.asset("Assets/VFX/FIREBLEND_SHD.shader", 'Shader "Different/Fire" {}', "c" * 32)
        index, paths = startup.asset_index(self.project)
        _, missing = startup.startup_associations(catalog, self.project, paths, index, {})
        self.assertEqual(len(missing), 1)

    def test_disabled_sdk_unknown_script_is_not_deleted_or_guessed(self):
        relative = "Assets/Original.unity"
        self.asset(relative, "m_Script: {fileID: 99, guid: " + "1" * 32 + ", type: 3}\n", "2" * 32)
        with self.assertRaisesRegex(recover.RecoveryError, "unsupported"):
            startup.binding_manifest(self.project, {relative}, {}, {"UnityEngine.UI.dll": "1" * 32})
        self.assertIn("fileID: 99", (self.project / relative).read_text())


class RealOfficialTmpControls(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.archive = Path("/home/claw/quest3-tools/textmeshpro-3.0.6.tgz")
        cls.recipes = Path("/home/claw/quest3-local/recovery/startup-source-project/QuestRecovery/ShaderRecipes")
        if not cls.archive.is_file() or not cls.recipes.is_dir():
            raise unittest.SkipTest("Private owned recovery/official pinned SDK evidence is not installed.")
        cls.sources = tmp_shaders.official_sources(cls.archive)

    def test_genuine_owned_tmp_all_variants_verified_and_mutation_controls_fail(self):
        count = 0
        for path in self.recipes.glob("TextMeshPro*.json"):
            recipe = json.loads(path.read_text())
            name = recipe["parsedForm"]["m_Name"]
            if name not in tmp_shaders.SHADERS:
                continue
            source = self.sources[tmp_shaders.SHADERS[name]]
            proof = tmp_shaders.validate_recipe(recipe, source)
            self.assertFalse(proof["pixelParityVerified"])
            bad = copy.deepcopy(recipe)
            bad["parsedForm"]["m_SubShaders"][0]["m_Passes"][0]["m_State"]["m_RtBlend0"]["m_DestinationBlend"]["m_Value"] = 1
            with self.assertRaisesRegex(recover.RecoveryError, "incompatible"):
                tmp_shaders.validate_recipe(bad, source)
            property_name = recipe["parsedForm"]["m_PropInfo"]["m_Props"][0]["m_Name"]
            with self.assertRaisesRegex(recover.RecoveryError, "lacks original property"):
                tmp_shaders.validate_recipe(recipe, source.replace(property_name.encode(), b"_ChangedProperty"))
            count += 1
        self.assertEqual(count, 6)

    def test_pinned_archive_bytes_cannot_be_substituted(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "untrusted.tgz"
            archive.write_bytes(b"untrusted")
            with self.assertRaisesRegex(recover.RecoveryError, "pinned SHA256"):
                tmp_shaders.official_sources(archive)


if __name__ == "__main__":
    unittest.main()
