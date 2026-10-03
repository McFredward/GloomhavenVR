"""Original startup provenance controls; diagnostic evidence cannot unlock the game."""

import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import startup
import storage
import builder


class StartupExportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.scenes = ["Assets/Scenes/" + name + ".unity" for name in startup.SCENE_NAMES]
        for name in self.scenes:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("original scene " + name)
            Path(str(path) + ".meta").write_text("original GUID " + name)
        plugin = self.root / "Assets/Plugins/GH.Runtime.dll"
        plugin.parent.mkdir(parents=True)
        plugin.write_bytes(b"original owned DLL fixture")
        Path(str(plugin) + ".meta").write_text("original script GUID fixture")
        self.game = [{"path": "Managed/GH.Runtime.dll", "sha256": storage.digest(plugin), "size": plugin.stat().st_size}]
        self.key = storage.value_hash({"files": self.game})
        self.report = {"schema": 1, "target": "startup", "fullGameReady": False,
                       "sourceBuilderFingerprint": self.key, "recoveryReceiptSha256": "a" * 64,
                       "selectedScenes": self.scenes, "files": storage.inventory(self.root),
                       "shaders": {"unresolved": ["original custom shader"]},
                       "readiness": {"closureReady": False}}
        self.write_report()

    def tearDown(self):
        self.temp.cleanup()

    def write_report(self):
        storage.write_json(self.root / startup.REPORT, self.report)

    def inspect(self):
        return startup.inspect_project(self.root, self.key, self.game)

    def test_original_closure_preserves_limits_and_fingerprints(self):
        result = self.inspect()
        self.assertFalse(result["fullGameReady"])
        self.assertEqual(result["scenes"], self.scenes)
        self.assertEqual(result["reportSha256"], storage.digest(self.root / startup.REPORT))
        self.assertEqual(result["files"], storage.inventory(self.root))

    def test_changed_owned_game_is_rejected(self):
        with self.assertRaisesRegex(storage.BuildError, "different original installation"):
            startup.inspect_project(self.root, "b" * 64, self.game)

    def test_false_full_game_claim_is_rejected(self):
        self.report["fullGameReady"] = True
        self.write_report()
        with self.assertRaisesRegex(storage.BuildError, "diagnostic scope"):
            self.inspect()

    def test_missing_intro_and_scene_reordering_are_rejected(self):
        for scenes in (self.scenes[::2], list(reversed(self.scenes))):
            self.report["selectedScenes"] = scenes
            self.write_report()
            with self.subTest(scenes=scenes), self.assertRaisesRegex(storage.BuildError, "in that order"):
                self.inspect()

    def test_changed_asset_and_undeclared_file_are_rejected(self):
        path = self.root / self.scenes[0]
        path.write_text("changed scene")
        with self.assertRaisesRegex(storage.BuildError, "changed or include undeclared"):
            self.inspect()
        path.write_text("original scene " + self.scenes[0])
        (self.root / "Assets/hidden.asset").write_text("unreported")
        with self.assertRaisesRegex(storage.BuildError, "changed or include undeclared"):
            self.inspect()

    def test_modified_dll_cannot_be_laundered_with_new_manifest(self):
        path = self.root / "Assets/Plugins/GH.Runtime.dll"
        path.write_bytes(b"rewritten unknown code")
        self.report["files"] = storage.inventory(self.root, [row["path"] for row in self.report["files"]])
        self.write_report()
        with self.assertRaisesRegex(storage.BuildError, "owned original DLL"):
            self.inspect()

    def test_new_executable_or_secret_input_is_rejected(self):
        for suffix in (".cs", ".exe", ".so", ".env", ".keystore"):
            path = self.root / ("Assets/injected" + suffix)
            path.write_text("untrusted fixture")
            self.report["files"].append(storage.record_file(path, path.relative_to(self.root).as_posix()))
            self.write_report()
            with self.subTest(suffix=suffix), self.assertRaisesRegex(storage.BuildError, "cannot inject"):
                self.inspect()
            self.report["files"].pop()
            path.unlink()

    def test_escaping_and_duplicate_inventory_rows_are_rejected(self):
        row = self.report["files"][0]
        self.report["files"].append(dict(row, path="../escape"))
        self.write_report()
        with self.assertRaisesRegex(storage.BuildError, "unsafe asset path"):
            self.inspect()
        self.report["files"][-1] = dict(row)
        self.write_report()
        with self.assertRaisesRegex(storage.BuildError, "duplicate"):
            self.inspect()

    def test_asset_meta_identity_is_required(self):
        self.report["files"] = [row for row in self.report["files"] if row["path"] != "Assets/Plugins/GH.Runtime.dll.meta"]
        self.write_report()
        with self.assertRaisesRegex(storage.BuildError, "lost its recovered script GUID"):
            self.inspect()

    def test_unsafe_scene_path_is_rejected(self):
        self.report["selectedScenes"][0] = "../Bootstrap.unity"
        self.write_report()
        with self.assertRaisesRegex(storage.BuildError, "unsafe scene path"):
            self.inspect()

    def test_missing_recovery_receipt_is_rejected(self):
        del self.report["recoveryReceiptSha256"]
        self.write_report()
        with self.assertRaisesRegex(storage.BuildError, "receipt fingerprint"):
            self.inspect()

    def test_invalid_json_is_rejected(self):
        (self.root / startup.REPORT).write_text("not json")
        with self.assertRaisesRegex(storage.BuildError, "valid quest-startup-report"):
            self.inspect()

    def test_snapshot_receipt_does_not_become_an_original_asset(self):
        destination = self.root.parent / (self.root.name + "-snapshot")
        try:
            expected = self.inspect()
            storage.snapshot(self.root, expected["files"], destination)
            actual = startup.inspect_project(destination, self.key, self.game, snapshot_receipt=True)
            self.assertEqual(actual, expected)
        finally:
            import shutil
            shutil.rmtree(destination, ignore_errors=True)


class StartupPackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "Assets/StreamingAssets/Rulebase").mkdir(parents=True)

    def tearDown(self):
        self.temp.cleanup()

    def test_original_rule_bytes_are_manifested_once_and_roundtrip(self):
        source = self.root / "Assets/StreamingAssets/Rulebase/Campaign.ruleset"
        original = b"original binary rulebase fixture\x00\xff"
        source.write_bytes(original)
        Path(str(source) + ".meta").write_text("guid: original")
        manifest = builder.package_startup_content(self.root, "b" * 64)
        archive = self.root / "Assets/StreamingAssets/quest-startup-content.zip"
        self.assertEqual(manifest["archiveSha256"], storage.digest(archive))
        self.assertEqual(manifest["files"][0]["path"], "StreamingAssets/Rulebase/Campaign.ruleset")
        with zipfile.ZipFile(archive) as zip_file:
            self.assertEqual(zip_file.namelist(), [manifest["files"][0]["path"]])
            self.assertEqual(zip_file.read(manifest["files"][0]["path"]), original)
        self.assertFalse(source.exists())
        self.assertFalse(Path(str(source) + ".meta").exists())

    def test_empty_rulebase_is_not_a_successful_startup(self):
        with self.assertRaisesRegex(storage.BuildError, "real Rulebase files"):
            builder.package_startup_content(self.root, "b" * 64)

    def test_input_provenance_is_not_exposed_as_original_rule_data(self):
        (self.root / "Assets/StreamingAssets/Rulebase/Campaign.ruleset").write_bytes(b"real fixture")
        private = self.root / "Assets/StreamingAssets/Quest/input-manifest.json"
        private.parent.mkdir()
        private.write_text("source provenance fixture")
        manifest = builder.package_startup_content(self.root, "b" * 64)
        self.assertEqual(len(manifest["files"]), 1)
        self.assertTrue(private.exists())

    def test_duplicate_package_plugin_is_disabled_with_guid_retained(self):
        plugin = self.root / "Assets/Plugins/Unity.InputSystem.dll.meta"
        plugin.parent.mkdir()
        original = "fileFormatVersion: 2\nguid: original-script-id\nPluginImporter:\n  platformData:\n  - second:\n      enabled: 1\n      settings: {}\n  - second:\n      enabled: 1\n      settings: {}\n"
        plugin.write_text(original)
        builder.exclude_recovered_package_plugins(self.root)
        self.assertEqual(plugin.read_text(), original.replace("enabled: 1", "enabled: 0"))


class RestoredBepInExTests(unittest.TestCase):
    def test_global_debug_wrapper_is_hidden_from_package_compilers_only(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            plugin = root / "Assets/Plugins/GH.Runtime.FirstPass.dll"
            plugin.parent.mkdir(parents=True)
            plugin.write_bytes(b"original wrapper fixture")
            meta = Path(str(plugin) + ".meta")
            original = "guid: original-script-id\nPluginImporter:\n  isExplicitlyReferenced: 0\n  platformData:\n    enabled: 1\n"
            meta.write_text(original)
            builder.isolate_original_compiler_namespace(root)
            self.assertEqual(meta.read_text(), original.replace("isExplicitlyReferenced: 0", "isExplicitlyReferenced: 1"))
            self.assertEqual(plugin.read_bytes(), b"original wrapper fixture")

    def test_only_real_restored_runtime_is_selected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package = root / "packages/bepinex.baselib/5.4.20/lib/netstandard2.0/BepInEx.dll"
            package.parent.mkdir(parents=True)
            package.write_bytes(b"real restored runtime fixture")
            assets = root / "project.assets.json"
            storage.write_json(assets, {"libraries": {"BepInEx.BaseLib/5.4.20": {
                "type": "package", "path": "bepinex.baselib/5.4.20"}},
                "packageFolders": {str(root / "packages"): {}}})
            self.assertEqual(builder.resolved_bepinex_runtime(assets), package.resolve())
            package.unlink()
            reference = root / "libs/RefAsm/BepInEx.dll"
            reference.parent.mkdir(parents=True)
            reference.write_bytes(b"metadata-only reference fixture")
            with self.assertRaisesRegex(storage.BuildError, "one restored"):
                builder.resolved_bepinex_runtime(assets)

    def test_restored_runtime_path_cannot_escape_package_folder(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets = Path(temporary) / "project.assets.json"
            storage.write_json(assets, {"libraries": {"BepInEx.BaseLib/5.4.20": {
                "type": "package", "path": "../../outside"}}, "packageFolders": {temporary: {}}})
            with self.assertRaisesRegex(storage.BuildError, "package path is invalid"):
                builder.resolved_bepinex_runtime(assets)


if __name__ == "__main__":
    unittest.main()
