"""Controls for the actual Unity import defect: case-colliding recovered assets."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import case_paths


class CasePathContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.project = Path(self.temp.name) / "generated"
        (self.project / "Assets/Quest").mkdir(parents=True)
        (self.project / "Assets/QuestOriginalStartup").mkdir(parents=True)
        self.entries = []
        self.binding_paths = []
        self.manifests()

    def tearDown(self):
        self.temp.cleanup()

    def asset(self, path, guid, body=None, bundled=False):
        p = self.project / path
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(body if body is not None else "original " + guid + "\n")
        p.with_name(p.name + ".meta").write_text("guid: " + guid + "\n")
        if bundled:
            self.entries.append({"assetPath": path, "originalAssetPath": path, "recoveredGuid": guid,
                                 "provider": "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider",
                                 "keys": [path, "original-key-" + guid], "labels": ["always_loaded_base", "misc_gui"]})
        return p

    def manifests(self):
        (self.project / case_paths.ADDRESSABLES).write_text(json.dumps({"schema": 1, "entries": self.entries}))
        (self.project / case_paths.BINDINGS).write_text(json.dumps({"schema": 1, "assetPaths": self.binding_paths,
                                                                 "bindings": [{"oldGuid": "a" * 32}]}))

    def file_bytes(self):
        return {p.relative_to(self.project).as_posix(): p.read_bytes()
                for p in self.project.rglob("*") if p.is_file()}

    def test_actual_folder_defect_retains_core_resource_and_catalog_variant(self):
        core = "Assets/Resources/generatecompendium/UICompendiumButton.prefab"
        bundle = "Assets/Resources/GenerateCompendium/UICompendiumButton.prefab"
        callback = "m_Script: {fileID: 123, guid: " + "4" * 32 + "}\n  m_MethodName: ContinueOriginal\n  m_Target: {fileID: 17}\n"
        self.asset(core, "1" * 32, callback + "core dependencies\n")
        self.asset(bundle, "2" * 32, callback + "bundle dependencies\n", bundled=True)
        self.binding_paths = [core, bundle]
        self.manifests()
        original = self.file_bytes()
        original_rows = json.loads((self.project / case_paths.ADDRESSABLES).read_text())["entries"]
        report = case_paths.migrate(self.project)
        self.assertEqual(len(report["moves"]), 1)
        self.assertEqual((self.project / core).read_bytes(), original[core])
        new_bundle = case_paths.mapped(bundle, report["pathMappings"])
        self.assertFalse(case_paths.resource_path(new_bundle))
        self.assertNotEqual(new_bundle, bundle)
        self.assertEqual((self.project / new_bundle).read_bytes(), original[bundle])
        self.assertEqual((self.project / (new_bundle + ".meta")).read_bytes(), original[bundle + ".meta"])
        rows = json.loads((self.project / case_paths.ADDRESSABLES).read_text())["entries"]
        self.assertEqual(rows[0]["assetPath"], new_bundle)
        rows[0]["assetPath"] = bundle
        self.assertEqual(rows, original_rows)
        bindings = json.loads((self.project / case_paths.BINDINGS).read_text())
        self.assertEqual(bindings["assetPaths"], [core, new_bundle])
        self.assertEqual(bindings["bindings"], [{"oldGuid": "a" * 32}])
        self.assertFalse(report["fullGameReady"])
        self.assertFalse(report["serializedReferencesChanged"])
        self.assertFalse(case_paths.plan(case_paths.nodes(self.project), set())[0])

    def test_sprite_and_meta_collision_preserves_distinct_original_objects(self):
        self.asset("Assets/Sprite/Brute.asset", "1" * 32, "big atlas sprite\n")
        self.asset("Assets/Sprite/brute.asset", "2" * 32, "different atlas and UVs\n", bundled=True)
        self.manifests()
        before = self.file_bytes()
        report = case_paths.migrate(self.project)
        self.assertEqual(len(report["files"]), 2)
        for row in report["files"]:
            self.assertEqual((self.project / row["assetPath"]).read_bytes(), before[row["originalPath"]])
        self.assertEqual((self.project / "Assets/Sprite/Brute.asset").read_bytes(), before["Assets/Sprite/Brute.asset"])
        self.assertEqual(report["files"][0]["guid"], "2" * 32)
        self.assertFalse(case_paths.plan(case_paths.nodes(self.project), set())[0])

    def test_full_campaign_only_bundle_moves_all_import_contracts_keeps_original_keys(self):
        core, variant = "Assets/Resources/Hex.prefab", "Assets/Resources/hex.prefab"
        self.asset(core, "1" * 32)
        self.asset(variant, "2" * 32, "native PPtr bytes\n")
        root = self.project / "Assets/QuestOriginalCampaign"
        root.mkdir()
        original = {"assetPath": variant, "originalAssetPath": variant, "recoveredGuid": "2" * 32,
                    "provider": "BundledAssetProvider", "keys": [variant, "campaign-key"], "nativeKeys": [variant],
                    "labels": ["DLC2", "high_detail"], "nativeFileId": 123}
        docs = {
            "campaign-addressables.json": {"schema": 1, "entries": [original]},
            "campaign-scenes.json": {"schema": 1, "scenes": [{"path": variant, "guid": "2" * 32}]},
            "script-bindings.json": {"schema": 1, "assetPaths": [variant], "bindings": [{"oldGuid": "2" * 32}]},
            "campaign-shaders.json": {"schema": 1, "shaders": [{"assetPath": variant, "sourceSha256": "3" * 64}],
                                       "materials": [{"assetPath": variant}], "programs": []},
            "campaign-computes.json": {"schema": 1, "shaders": [{"assetPath": variant, "guid": "2" * 32, "kernelCount": 3}]},
            "bundled-audio.json": {"schema": 1, "assets": [{"assetPath": variant, "guid": "2" * 32, "channels": 4}]},
            "native-texture2d.json": {"schema": 1, "assets": [{"assetPath": variant, "guid": "2" * 32,
                                                               "nativePixelSha256": "4" * 64}]},
            "ordinary-texture2d-audit.json": {"schema": 1, "assets": [{"assetPath": variant,
                                                                       "originalTextureFormat": "RGBAHalf"}]},
            "native-texture-references.json": {"schema": 1, "targets": [{"assetPath": variant, "guid": "2" * 32}],
                                                "owners": [{"assetPath": variant, "sha256": "5" * 64}]},
            "packed-sprites.json": {"schema": 1, "atlases": [{"assetPath": variant}], "sprites": [{"assetPath": variant}]}}
        for name, document in docs.items(): (root / name).write_text(json.dumps(document))
        report = case_paths.migrate(self.project)
        destination = case_paths.mapped(variant, report["pathMappings"])
        self.assertNotEqual(destination, variant)
        self.assertFalse(case_paths.resource_path(destination))
        self.assertEqual((self.project / destination).read_text(), "native PPtr bytes\n")
        rows = json.loads((root / "campaign-addressables.json").read_text())["entries"]
        self.assertEqual(rows, [{**original, "assetPath": destination}])
        self.assertEqual(json.loads((root / "script-bindings.json").read_text())["assetPaths"], [destination])
        self.assertEqual(json.loads((root / "campaign-scenes.json").read_text())["scenes"][0]["path"], destination)
        self.assertEqual(json.loads((root / "campaign-shaders.json").read_text())["shaders"][0],
                         {"assetPath": destination, "sourceSha256": "3" * 64})
        self.assertEqual(json.loads((root / "packed-sprites.json").read_text())["sprites"][0]["assetPath"], destination)
        self.assertEqual(json.loads((root / "campaign-computes.json").read_text())["shaders"][0],
                         {"assetPath": destination, "guid": "2" * 32, "kernelCount": 3})
        self.assertEqual(json.loads((root / "bundled-audio.json").read_text())["assets"][0],
                         {"assetPath": destination, "guid": "2" * 32, "channels": 4})
        self.assertEqual(json.loads((root / "native-texture2d.json").read_text())["assets"][0],
                         {"assetPath": destination, "guid": "2" * 32, "nativePixelSha256": "4" * 64})
        # The ordinary audit describes the pre-replacement PNG export, not a final physical asset.
        self.assertEqual(json.loads((root / "ordinary-texture2d-audit.json").read_text()),
                         docs["ordinary-texture2d-audit.json"])
        self.assertEqual(json.loads((root / "native-texture-references.json").read_text()),
                         {"schema": 1, "targets": [{"assetPath": destination, "guid": "2" * 32}],
                          "owners": [{"assetPath": destination, "sha256": "5" * 64}]})
        self.assertEqual(len([name for name in report["manifestSha256"] if name.startswith("Assets/QuestOriginalCampaign/")]), 9)
        self.assertEqual(case_paths.migrate(self.project), report)

    def campaign_only_manifests(self, asset, guid):
        (self.project / case_paths.ADDRESSABLES).unlink()
        (self.project / case_paths.BINDINGS).unlink()
        root = self.project / "Assets/QuestOriginalCampaign"
        root.mkdir()
        entry = {"assetPath": asset, "originalAssetPath": asset, "recoveredGuid": guid,
                 "provider": "BundledAssetProvider", "keys": ["native-campaign-key"], "nativeFileId": 17}
        (self.project / case_paths.CAMPAIGN_ADDRESSABLES).write_text(json.dumps({"schema": 1, "entries": [entry]}))
        (self.project / case_paths.CAMPAIGN_BINDINGS).write_text(json.dumps({"schema": 1, "assetPaths": [asset]}))

    def test_complete_campaign_without_diagnostic_startup_manifests(self):
        core, bundled = "Assets/Resources/Hex.prefab", "Assets/Resources/hex.prefab"
        self.asset(core, "1" * 32)
        self.asset(bundled, "2" * 32, "native Campaign callback and dependency bytes\n")
        self.campaign_only_manifests(bundled, "2" * 32)
        original = self.file_bytes()
        report = case_paths.migrate(self.project)
        destination = case_paths.mapped(bundled, report["pathMappings"])
        self.assertEqual(report["target"], "game")
        self.assertNotEqual(destination, bundled)
        self.assertFalse(case_paths.resource_path(destination))
        self.assertEqual((self.project / destination).read_bytes(), original[bundled])
        self.assertEqual((self.project / (destination + ".meta")).read_bytes(), original[bundled + ".meta"])
        self.assertFalse((self.project / case_paths.ADDRESSABLES).exists())
        self.assertFalse((self.project / case_paths.BINDINGS).exists())
        catalog = json.loads((self.project / case_paths.CAMPAIGN_ADDRESSABLES).read_text())
        self.assertEqual(catalog["entries"][0]["assetPath"], destination)
        self.assertEqual(catalog["entries"][0]["originalAssetPath"], bundled)
        self.assertEqual(catalog["entries"][0]["keys"], ["native-campaign-key"])
        self.assertEqual(case_paths.migrate(self.project), report)

    def test_campaign_only_incomplete_pair_and_wrong_guid_reject_without_mutation(self):
        asset = "Assets/Native.prefab"
        self.asset(asset, "1" * 32)
        self.campaign_only_manifests(asset, "2" * 32)
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "GUID mismatch"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)
        (self.project / case_paths.CAMPAIGN_BINDINGS).unlink()
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "Incomplete Campaign"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)

    def test_folder_metadata_and_nested_references_move_with_owner(self):
        self.asset("Assets/A/Child.asset", "1" * 32)
        self.asset("Assets/a/Child.asset", "2" * 32, "m_Target: {fileID: 71}\n")
        (self.project / "Assets/A.meta").write_text("guid: " + "3" * 32 + "\nfolderAsset: yes\n")
        (self.project / "Assets/a.meta").write_text("guid: " + "4" * 32 + "\nfolderAsset: yes\n")
        before = self.file_bytes()
        report = case_paths.migrate(self.project)
        self.assertEqual(len(report["files"]), 3)
        for row in report["files"]:
            self.assertEqual((self.project / row["assetPath"]).read_bytes(), before[row["originalPath"]])
        self.assertEqual(case_paths.asset_guid(self.project, report["pathMappings"]["Assets/a"]), "4" * 32)

    def test_resource_collision_without_catalog_proof_rejects_before_mutation(self):
        self.asset("Assets/Resources/A.prefab", "1" * 32)
        self.asset("Assets/Resources/a.prefab", "2" * 32)
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "Ambiguous original Resources"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)

    def test_mixed_resource_directory_cannot_silently_choose_a_different_key(self):
        self.asset("Assets/Resources/Core/A.asset", "1" * 32)
        self.asset("Assets/Resources/core/A.asset", "2" * 32, bundled=True)
        self.asset("Assets/Resources/core/Unknown.asset", "3" * 32)
        self.manifests()
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "Ambiguous original Resources"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)

    def test_missing_meta_and_orphan_meta_fail_closed(self):
        self.asset("Assets/A.asset", "1" * 32)
        p = self.asset("Assets/a.asset", "2" * 32)
        p.with_name(p.name + ".meta").unlink()
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "Missing original asset metadata"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)
        (self.project / "Assets/orphan.asset.meta").write_text("guid: " + "3" * 32 + "\n")
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "Orphan metadata"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)

    def test_wrong_addressable_identity_and_escaping_binding_reject_unchanged(self):
        self.asset("Assets/A.asset", "1" * 32, bundled=True)
        self.entries[0]["recoveredGuid"] = "2" * 32
        self.manifests()
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "GUID mismatch"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)
        self.entries[0]["recoveredGuid"] = "1" * 32
        self.binding_paths = ["Assets/../../original.prefab"]
        self.manifests()
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "escapes Assets"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)

    def test_retry_is_idempotent_and_changed_migrated_asset_rejects(self):
        self.asset("Assets/A.asset", "1" * 32)
        self.asset("Assets/a.asset", "2" * 32)
        first = case_paths.migrate(self.project)
        before = self.file_bytes()
        self.assertEqual(case_paths.migrate(self.project), first)
        self.assertEqual(self.file_bytes(), before)
        (self.project / first["files"][0]["assetPath"]).write_text("changed\n")
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "Previously migrated asset changed"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)

    def test_raw_export_and_symlinked_asset_are_rejected(self):
        (self.project / "Assets/Quest").rmdir()
        with self.assertRaisesRegex(case_paths.CasePathError, "Refusing raw"):
            case_paths.migrate(self.project)
        (self.project / "Assets/Quest").mkdir()
        outside = Path(self.temp.name) / "untouched.asset"
        outside.write_text("read-only reference\n")
        (self.project / "Assets/linked.asset").symlink_to(outside)
        with self.assertRaisesRegex(case_paths.CasePathError, "Symlink"):
            case_paths.migrate(self.project)
        self.assertEqual(outside.read_text(), "read-only reference\n")

    def test_deterministic_mapping_and_occupied_destination_failure(self):
        self.asset("Assets/Z.asset", "1" * 32)
        self.asset("Assets/z.asset", "2" * 32)
        inventory = case_paths.nodes(self.project)
        first = case_paths.plan(inventory, set())
        second = case_paths.plan(dict(reversed(list(inventory.items()))), set())
        self.assertEqual(first, second)
        destination = first[0]["Assets/z.asset"]
        self.asset(destination, "3" * 32)
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "occupied"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)

    def test_existing_destination_parent_case_and_evidence_symlink_fail(self):
        self.asset("Assets/A.asset", "1" * 32)
        self.asset("Assets/a.asset", "2" * 32)
        (self.project / "Assets/QuestOriginalStartup/casevariants").mkdir()
        before = self.file_bytes()
        with self.assertRaisesRegex(case_paths.CasePathError, "parent has conflicting casing"):
            case_paths.migrate(self.project)
        self.assertEqual(self.file_bytes(), before)
        (self.project / "Assets/QuestOriginalStartup/casevariants").rmdir()
        outside = Path(self.temp.name) / "evidence"
        outside.mkdir()
        (self.project / "QuestStartupEvidence").symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(case_paths.CasePathError, "evidence must not be symlinked"):
            case_paths.migrate(self.project)
        self.assertFalse(list(outside.iterdir()))


if __name__ == "__main__":
    unittest.main()
