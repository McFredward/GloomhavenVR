"""Targeted reconstruction uses complete output proof and immutable originals."""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import import_workspace
import preparation_repair as repair
import storage

LIGHTING = (b"%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1120 &112000000\n"
            b"LightingDataAsset:\n  m_Name: LightingData\n  m_Scene: {fileID: 0}\n")
SETTINGS = (b'%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n'
            b'  serializedVersion: 23\n  m_ActiveColorSpace: 0\n'
            b'  m_BuildTargetGraphicsAPIs: []\n  activeInputHandler: 0\n')


class PreparationRepairSourcesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-repair-sources-")
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name)
        self.project = self.output / "projects" / ("b" * 64)
        self.original = self.output / "cache/recovery/original/project"
        self.source = self.output / "inputs/source"
        for root in (self.project, self.original, self.source):
            root.mkdir(parents=True)
        self.inputs = {"inputKey": "a" * 64, "profileKey": "c" * 64,
                       "profile": {"dlcOwnership": {"schema": 1, "ownedMask": 3}}}
        self.original_files = {}
        self.provider = repair.Provider(self.project, self.original, self.source, self.output, self.inputs,
                                        original_files=self.original_files)
        self.stage = self.output / "cache/prepare-resume/repair-data/file.part"
        self.latest = {}
        self.provider.bind(self.latest)

    def put(self, root, name, raw):
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        if root == self.original:
            self.original_files[name] = self.record(name, raw)
            if Path(name).suffix in (".asset", ".prefab", ".unity", ".controller", ".shader"):
                metadata = b"fileFormatVersion: 2\nguid: " + b"f" * 32 + b"\nNativeFormatImporter:\n  userData:\n"
                sidecar = Path(str(path) + ".meta")
                sidecar.write_bytes(metadata)
                self.original_files[name + ".meta"] = self.record(name + ".meta", metadata)
        return path

    def record(self, relative, raw):
        return {"path": relative, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}

    def reconstruct(self, relative, raw, owner="script-orders"):
        return self.provider(relative, self.record(relative, raw), owner, self.stage)

    def test_unity_binary_lighting_restores_only_original_yaml(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        source = self.put(self.original, relative, LIGHTING)
        live = self.put(self.project, relative, b"Unity2021 imported binary lighting")
        library = self.put(self.project, "Library/Artifacts/existing", b"expensive import")
        before_source, before_live, before_library = source.stat(), live.stat(), library.stat()
        with patch.object(Path, "rglob", side_effect=AssertionError("No native tree enumeration")):
            result = self.reconstruct(relative, LIGHTING)
        self.assertEqual(result["recipe"], "retained-original-recovery")
        self.assertEqual(self.stage.read_bytes(), LIGHTING)
        self.assertEqual(source.stat(), before_source)
        self.assertEqual(live.stat(), before_live)
        self.assertEqual(library.stat(), before_library)
        self.assertNotIn(b"m_Script", LIGHTING)

    def test_missing_original_file_uses_selected_template(self):
        relative = "Assets/Quest/Runtime/QuestStandalonePlatform.cs"
        raw = b"public static class QuestStandalonePlatform {}"
        self.put(self.source, "unity/GloomhavenVR.Quest/" + relative, raw)
        result = self.reconstruct(relative, raw, "base-project")
        self.assertEqual(result["recipe"], "selected-template")
        self.assertEqual(self.stage.read_bytes(), raw)

    def test_migration_restores_old_template_before_current_overlay(self):
        relative = "Assets/Quest/Editor/QuestBuild.cs"
        old = b"previous reviewed Editor source"
        key = "d" * 64
        self.put(self.source, "unity/GloomhavenVR.Quest/" + relative, b"new current Editor source")
        previous = self.output / "inputs/mod" / key
        self.put(previous, "unity/GloomhavenVR.Quest/" + relative, old)
        control = "Assets/StreamingAssets/Quest/input-manifest.json"
        self.put(self.project, control, storage.canonical({"mod": {"key": key}}))
        self.latest[control] = (26, storage.record_file(self.project / control, control))
        result = self.reconstruct(relative, old, "base-project")
        self.assertEqual(result["source"], str(previous / "unity/GloomhavenVR.Quest" / relative))
        self.assertEqual(self.stage.read_bytes(), old)

    def test_accepted_sdk_pointer_variant_is_reconstructed_with_full_hash_proof(self):
        old, new = "a" * 32, "b" * 32
        relative = "Assets/GameObject/AboutTab.prefab"
        raw = ("%YAML 1.1\n  m_Script: {fileID: -619905303, guid: " + old +
               ", type: 3}\n  m_OnClick: originalCallback\n").encode()
        imported = raw.replace(("fileID: -619905303, guid: " + old).encode(),
                               ("fileID: 11500000, guid: " + new).encode())
        self.put(self.original, relative, raw)
        binding = {"assemblyName": "UnityEngine.UI", "fullName": "UnityEngine.UI.Button", "oldGuid": old,
                   "oldFileId": -619905303}
        controls = {
            "Assets/QuestOriginalCampaign/script-bindings.json": {"schema": 1, "assetPaths": [relative],
                "bindings": [binding], "disabledPluginGuids": [old]},
            "Packages/manifest.json": {"dependencies": {"com.unity.ugui": "1.0.0"}},
        }
        for name, value in controls.items():
            self.put(self.project, name, storage.canonical(value))
            self.latest[name] = (25, storage.record_file(self.project / name, name))
        self.put(self.project, "Packages/com.unity.ugui/Runtime/UI/Core/Button.cs", b"namespace UnityEngine.UI { public class Button {} }")
        self.put(self.project, "Packages/com.unity.ugui/Runtime/UI/Core/Button.cs.meta", ("guid: " + new + "\nMonoImporter:\n").encode())
        self.put(self.project, "QuestStartupEvidence/script-remap.json", storage.canonical({"schema": 1,
            "callbacksAndOtherSerializedBytesPreserved": True,
            "replacements": [{**binding, "newGuid": new, "newFileId": 11500000}], "assets": []}))
        result = self.reconstruct(relative, imported)
        self.assertEqual(result["recipe"], "frozen-package-script-remap")
        self.assertEqual(self.stage.read_bytes(), imported)
        self.assertIsNone(self.reconstruct(relative, imported.replace(b"originalCallback", b"foreignCallback")))

    def test_modified_sdk_package_cannot_supply_forward_variant(self):
        # Reuse the complete proof setup, then independently break the installed
        # target identity before reconstructing the same expected bytes.
        self.test_accepted_sdk_pointer_variant_is_reconstructed_with_full_hash_proof()
        self.provider.remap = None
        metadata = self.project / "Packages/com.unity.ugui/Runtime/UI/Core/Button.cs.meta"
        metadata.write_bytes(b"guid: " + b"c" * 32 + b"\nMonoImporter:\n")
        relative = "Assets/GameObject/AboutTab.prefab"
        original = (self.original / relative).read_bytes()
        imported = original.replace(b"fileID: -619905303, guid: " + b"a" * 32,
                                    b"fileID: 11500000, guid: " + b"b" * 32)
        self.assertIsNone(self.reconstruct(relative, imported))

    def test_wrong_source_hash_is_not_published(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        self.put(self.original, relative, LIGHTING[:-1] + b"!")
        self.assertIsNone(self.reconstruct(relative, LIGHTING))
        self.assertFalse(self.stage.exists())

    def test_one_cached_shader_output_reuses_converter_overlay(self):
        relative = "Assets/Shader/Native.shader"
        raw = b"owned native Shader instructions"
        self.inputs["game"] = {"key": "e" * 64}
        self.put(self.output, "tool-cache/cs/" + "e" * 64 + "/o/" + relative, raw)
        with patch.object(Path, "rglob", side_effect=AssertionError("No converter variant sweep")):
            result = self.reconstruct(relative, raw, "campaign-shaders")
        self.assertEqual(result["recipe"], "retained-shader-overlay")
        self.assertEqual(self.stage.read_bytes(), raw)

    def test_case_moved_original_metadata_uses_unchanged_frozen_receipt(self):
        original = "Assets/Texture2D/Foo.png.meta"
        moved = "Assets/QuestOriginalStartup/CaseVariants/Foo_case.png.meta"
        raw = b"fileFormatVersion: 2\nguid: " + b"a" * 32 + b"\nTextureImporter: {}\n"
        self.put(self.original, original, raw)
        receipt = {"schema": 1, "assetContentChanged": False, "serializedReferencesChanged": False,
                   "pathMappings": {original: moved}}
        self.put(self.project, repair.CASE_RECEIPT, storage.canonical(receipt) + b"\n")
        self.latest[repair.CASE_RECEIPT] = (23, storage.record_file(self.project / repair.CASE_RECEIPT, repair.CASE_RECEIPT))
        result = self.reconstruct(moved, raw, "case-paths")
        self.assertEqual(result["source"], str(self.original / original))
        self.assertEqual(self.stage.read_bytes(), raw)

    def test_modified_case_mapping_cannot_select_another_source(self):
        original = "Assets/Foo.meta"
        moved = "Assets/CaseVariants/Foo.meta"
        raw = b"guid: aabbccdd\n"
        self.put(self.original, original, raw)
        doc = {"assetContentChanged": False, "serializedReferencesChanged": False,
               "pathMappings": {original: moved}}
        self.put(self.project, repair.CASE_RECEIPT, storage.canonical(doc))
        row = storage.record_file(self.project / repair.CASE_RECEIPT, repair.CASE_RECEIPT)
        self.latest[repair.CASE_RECEIPT] = (23, row)
        self.put(self.project, repair.CASE_RECEIPT, storage.canonical(doc) + b" ")
        self.assertIsNone(self.reconstruct(moved, raw, "case-paths"))

    def test_identity_and_logo_use_selected_snapshots(self):
        for relative, origin, raw in (
            ("Assets/Quest/Resources/quest-profile.json", self.output / "identities" / self.inputs["profileKey"] / "quest-profile.json", b"{\"dummy\":true}"),
            ("Assets/Quest/Resources/quest-loading-logo.png", self.source / "src/GloomhavenVR/Assets/GloomhavenVR_logo.png", b"owned-mod-logo"),
            ("Assets/Quest/Runtime/QuestText.cs", self.source / "src/GloomhavenVR/Core/Loc/QuestText.cs", b"owned-mod-localization")):
            with self.subTest(relative=relative):
                origin.parent.mkdir(parents=True, exist_ok=True); origin.write_bytes(raw)
                self.assertIsNotNone(self.reconstruct(relative, raw, "base-project"))
                self.assertEqual(self.stage.read_bytes(), raw)

    def test_generated_small_recipes_reproduce_recorded_bytes(self):
        pairs = (
            ("Assets/csc.rsp", b"-define:GHVR_QUEST_STARTUP;GHVR_QUEST_GAME\n"),
            ("Assets/StreamingAssets/Quest/input-manifest.json", storage.canonical(self.inputs) + b"\n"),
            ("Assets/Quest/Resources/quest-dlc-ownership.json", storage.canonical(self.inputs["profile"]["dlcOwnership"]) + b"\n"),
        )
        for relative, raw in pairs:
            with self.subTest(relative=relative):
                self.assertIsNotNone(self.reconstruct(relative, raw))
                self.assertEqual(self.stage.read_bytes(), raw)

    def test_bootstrap_settings_restore_audited_android_fields(self):
        relative = "ProjectSettings/ProjectSettings.asset"
        self.put(self.original, relative, SETTINGS)
        prepared = import_workspace.patch_settings(SETTINGS, "game")
        self.assertIsNotNone(self.reconstruct(relative, prepared, "final-settings"))
        self.assertEqual(self.stage.read_bytes(), prepared)

    def test_settings_never_guesses_previously_accepted_other_editor_fields(self):
        relative = "ProjectSettings/ProjectSettings.asset"
        self.put(self.original, relative, SETTINGS)
        other = import_workspace.patch_settings(SETTINGS, "game").replace(b"activeInputHandler: 0", b"activeInputHandler: 2")
        self.assertIsNone(self.reconstruct(relative, other, "final-settings"))

    def test_archive_restores_one_selected_member_without_extraction_or_full_hash(self):
        relative = "Assets/QuestOriginalStartup/example.json"
        raw = b"owned original manifest"
        archive = self.project / "Assets/StreamingAssets/quest-startup-content.zip"
        archive.parent.mkdir(parents=True)
        with zipfile.ZipFile(archive, "w") as package:
            package.writestr(relative[len("Assets/"):], raw)
            package.writestr("unrelated-original.asset", b"do not read this")
        original_open = zipfile.ZipFile.open
        opened = []
        def one(package, name, *args, **kwargs):
            opened.append(name.filename if isinstance(name, zipfile.ZipInfo) else name)
            return original_open(package, name, *args, **kwargs)
        with patch.object(zipfile.ZipFile, "open", one):
            result = self.reconstruct(relative, raw)
        self.assertEqual(result["recipe"], "retained-single-archive-member")
        self.assertEqual(opened, [relative[len("Assets/"):]])
        self.assertEqual(self.stage.read_bytes(), raw)

    def test_wrong_archive_member_hash_is_not_a_repair_source(self):
        relative = "Assets/QuestOriginalStartup/example.json"
        raw = b"owned original manifest"
        archive = self.project / "Assets/StreamingAssets/quest-startup-content.zip"
        archive.parent.mkdir(parents=True)
        with zipfile.ZipFile(archive, "w") as package:
            package.writestr(relative[len("Assets/"):], b"foreign bad manifest!!")
        self.assertIsNone(self.reconstruct(relative, raw))
        self.assertFalse(self.stage.exists())

    def test_missing_small_generated_control_restores_witnessed_cache(self):
        relative = "QuestStartupEvidence/original-ui-assets.json"
        raw = b'{"schema":1,"shaders":[]}'
        live = self.put(self.project, relative, raw)
        row = self.record(relative, raw)
        self.assertTrue(self.provider.remember(relative, row))
        live.unlink()
        result = self.provider(relative, row, "startup-ui", self.stage)
        self.assertEqual(result["recipe"], "retained-generated-control")
        self.assertEqual(self.stage.read_bytes(), raw)

    def test_warm_remember_reads_no_control_payload(self):
        relative = "QuestStartupEvidence/original-ui-assets.json"
        raw = b'{"schema":1}'
        self.put(self.project, relative, raw)
        row = self.record(relative, raw)
        self.assertTrue(self.provider.remember(relative, row))
        with patch.object(Path, "open", side_effect=AssertionError("Warm controls need no payload read")):
            self.assertTrue(self.provider.remember(relative, row))

    def test_native_assets_are_never_duplicated_for_repair_cache(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        self.put(self.project, relative, LIGHTING)
        with patch.object(Path, "open", side_effect=AssertionError("Native assets cannot enter the bounded control cache")):
            self.assertFalse(self.provider.remember(relative, self.record(relative, LIGHTING)))
        self.assertFalse(self.provider.control_cache.exists())

    def test_control_cache_bound_and_corruption_do_not_break_preparation(self):
        relative = "QuestStartupEvidence/generated.json"
        raw = b"generated"
        self.put(self.project, relative, raw)
        with patch.object(repair, "CACHE_LIMIT", 1):
            self.assertFalse(self.provider.remember(relative, self.record(relative, raw)))
        self.provider.control_cache.mkdir(parents=True)
        (self.provider.control_cache / "index.json").write_bytes(b"[]")
        other = repair.Provider(self.project, self.original, self.source, self.output, self.inputs)
        self.assertTrue(other.remember(relative, self.record(relative, raw)))

    def test_damaged_index_retains_existing_qualified_controls(self):
        relative = "QuestStartupEvidence/generated.json"
        raw = b"generated"
        live = self.put(self.project, relative, raw)
        row = self.record(relative, raw)
        self.assertTrue(self.provider.remember(relative, row))
        (self.provider.control_cache / "index.json").write_bytes(b"damaged index")
        live.unlink()
        other = repair.Provider(self.project, self.original, self.source, self.output, self.inputs)
        result = other(relative, row, "startup-ui", self.stage)
        self.assertEqual(result["recipe"], "retained-generated-control")
        self.assertEqual(self.stage.read_bytes(), raw)

    def test_large_controls_are_not_cached(self):
        relative = "QuestRecovery/original-asset-identities.json"
        row = {"path": relative, "size": repair.CONTROL_LIMIT + 1, "sha256": "a" * 64}
        self.assertFalse(self.provider.remember(relative, row))
        self.assertFalse(self.provider.control_cache.exists())

    def test_unconfined_or_linked_paths_never_modify_originals(self):
        for relative in ("../outside", "/absolute", "Assets/../outside", "Assets\\outside", "C:/outside"):
            with self.subTest(relative=relative), self.assertRaises(storage.BuildError):
                self.reconstruct(relative, LIGHTING)
        linked = self.output / "outside-source"
        linked.write_bytes(LIGHTING)
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        source = self.original / relative
        source.parent.mkdir(parents=True); source.symlink_to(linked)
        with self.assertRaises(storage.BuildError):
            self.reconstruct(relative, LIGHTING)
        self.assertEqual(linked.read_bytes(), LIGHTING)

    def test_staging_cannot_be_live_project_or_source(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        for root in (self.project, self.original, self.source):
            with self.subTest(root=root), self.assertRaises(storage.BuildError):
                self.provider(relative, self.record(relative, LIGHTING), "script-orders", root / "file.part")

    def test_engine_named_project_sibling_staging_is_accepted(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        self.put(self.original, relative, LIGHTING)
        target = self.project / relative
        stage = target.with_name(".quest-repair-" + hashlib.sha256(relative.encode()).hexdigest()[:20] + ".part")
        result = self.provider(relative, self.record(relative, LIGHTING), "script-orders", stage)
        self.assertEqual(stage.read_bytes(), LIGHTING)
        self.assertEqual(result["companions"][0]["path"], relative + ".meta")

    def test_missing_original_metadata_stages_qualified_guid_companion(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        self.put(self.original, relative, LIGHTING)
        result = self.reconstruct(relative, LIGHTING)
        metadata = result["companions"][0]
        staged = Path(metadata["stagingPath"])
        expected = self.original_files[relative + ".meta"]
        self.assertEqual(metadata["sha256"], expected["sha256"])
        self.assertEqual(staged.read_bytes(), (self.original / (relative + ".meta")).read_bytes())
        self.assertFalse((self.project / (relative + ".meta")).exists())

    def test_existing_original_metadata_is_not_rewritten(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        self.put(self.original, relative, LIGHTING)
        raw = (self.original / (relative + ".meta")).read_bytes() + b"  laterUnityImporterField: 1\n"
        metadata = self.put(self.project, relative + ".meta", raw)
        before = metadata.stat()
        result = self.reconstruct(relative, LIGHTING)
        self.assertNotIn("companions", result)
        self.assertEqual(metadata.stat(), before)
        self.assertEqual(metadata.read_bytes(), raw)

    def test_unqualified_or_modified_original_metadata_cannot_assign_new_guid(self):
        relative = "Assets/Scenes/Bootstrap/LightingData.asset"
        self.put(self.original, relative, LIGHTING)
        metadata = self.original / (relative + ".meta")
        metadata.write_bytes(metadata.read_bytes().replace(b"f" * 32, b"e" * 32))
        self.assertIsNone(self.reconstruct(relative, LIGHTING))
        del self.original_files[relative + ".meta"]
        self.assertIsNone(self.reconstruct(relative, LIGHTING))

    def test_old_published_manifest_restores_missing_migration_control(self):
        old_key, old_mod = "d" * 64, "e" * 64
        old_inputs = {"inputKey": old_key, "mod": {"key": old_mod}}
        relative = "Assets/StreamingAssets/Quest/input-manifest.json"
        raw = storage.canonical(old_inputs) + b"\n"
        self.latest[relative] = (26, self.record(relative, raw))
        self.provider.bind_journal({"inputKey": old_key})
        self.put(self.output, "manifests/" + old_key + ".json", raw)
        result = self.reconstruct(relative, raw, "final-settings")
        self.assertEqual(result["recipe"], "retained-input-manifest")
        self.assertEqual(self.stage.read_bytes(), raw)
        self.assertEqual(self.provider.previous_source, self.output / "inputs/mod" / old_mod)


if __name__ == "__main__":
    unittest.main()
