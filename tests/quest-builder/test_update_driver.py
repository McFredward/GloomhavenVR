"""Update orchestration contracts, using real APK/owned-input/storage boundaries.

Signing and binary Resources/AXML writers are injected here; their actual
formats are covered separately. This fixture does not run a Unity player.
"""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import apk_update
import storage
import update_driver as driver


class UpdateDriverTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.output = self.root / "work"
        self.game = self.root / "PC"; self.apk = self.root / "base.apk"
        self.profile = {"schema": 1, "provider": "steam", "steamId": "76561197960265729",
                        "accountId": 1, "displayName": "Fixture Owner"}
        self.ownership = {"schema": 1, "provider": "steam", "appId": 780290,
                          "steamId": self.profile["steamId"], "ownedMask": 1, "installedAppIds": [1809490]}
        rows = []
        for name in ("GH.Runtime.dll", "GH.Shared.dll"):
            path = self.game / "Managed" / name; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(("owned input " + name).encode()); rows.append(storage.record_file(path, "Managed/" + name))
        self.manifest = {"schema": 1, "target": "game", "inputKey": "a" * 64,
                         "game": {"key": "b" * 64, "files": rows, "unityVersion": "2021.3.5f1"},
                         "mod": {"key": "c" * 64, "modBuild": 625, "files": []},
                         "profile": {**self.profile, "dlcOwnership": self.ownership}}
        self.current_mod = {"key": "d" * 64, "modBuild": 643,
                            "files": [{"path": "unity/GloomhavenVR.Assets/Assets/fixture.asset", "size": 4, "sha256": "e" * 64}]}
        self.capsule = {"schema": 1, "scope": "quest-code-update-v1", "inputKey": "a" * 64,
                        "unityVersion": "2021.3.5f1", "metadataVersion": 29, "packageAbi": "f" * 64,
                        "managedKey": storage.value_hash(apk_update._managed(self.manifest)),
                        "scriptTypes": [{"assembly": "GH.Runtime.dll", "namespace": "", "class": "Fixture",
                                         "propertiesHash": "1" * 32, "executionOrder": 0}],
                        "modSchema": 1, "dynamicProfile": True}
        self.write_apk()
        self.builder = SimpleNamespace(apk_updates=apk_update, BuildError=storage.BuildError,
            game_data=lambda root: Path(root), selected_profile=lambda args: (dict(self.profile), b"fixture logo"),
            dlcs=SimpleNamespace(capture=lambda *args: copy.deepcopy(self.ownership)),
            digest=storage.digest, value_hash=storage.value_hash, canonical=storage.canonical,
            write_json=storage.write_json, Stages=storage.Stages, PACKAGE="dev.gloomhavenvr.quest",
            ORIGINAL_UGUI_SHA256="3" * 64, UGUI_LAYOUT_SOURCE_SHA256="4" * 64,
            build_progress=SimpleNamespace(operation=Mock(), event=Mock()))
        self.args = SimpleNamespace(command="update-profile", update_kind="update-profile", game_root=str(self.game),
                                    base_apk=str(self.apk), signing_root=None)

    def write_apk(self):
        lib = bytearray(64); lib[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", lib, 18, 183)
        values = {apk_update.INPUT: storage.canonical(self.manifest),
                  apk_update.INSTALLATION: storage.canonical({"schema": 1, "inputKey": "a" * 64}),
                  apk_update.UPDATE: storage.canonical({"schema": 1, "gameInputKey": "a" * 64,
                    "updateKey": "2" * 64, "modBuild": 643, "mod": self.current_mod}),
                  apk_update.CAPSULE: storage.canonical(self.capsule), apk_update.LIBRARY: bytes(lib),
                  apk_update.METADATA: struct.pack("<II", 0xFAB11BAF, 29), "assets/fixture.resource": b"unchanged payload" * 10000}
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in values.items(): archive.writestr(name, value)

    def test_profile_edit_keeps_latest_mod_instead_of_original_conversion(self):
        plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(plan["inputs"]["mod"], self.current_mod)
        self.assertEqual(plan["gameInputKey"], self.manifest["inputKey"])
        self.assertEqual(apk_update.inspect_base(self.apk)["manifest"]["mod"]["modBuild"], 625)
        self.assertEqual(json.loads((self.output / "latest-update-input.json").read_text())["plan"],
                         "updates/" + plan["updateKey"] + "/plan.json")

    def test_dlc_cannot_expand_past_original_converted_content(self):
        self.ownership["installedAppIds"].append(1958560)
        with self.assertRaisesRegex(storage.BuildError, "additional game content"):
            driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertFalse((self.output / "latest-update-input.json").exists())

    def test_changed_non_il2cpp_native_sources_select_a_cached_full_build(self):
        self.args.command = "update-mod"
        self.builder.source_inventory = lambda repo: ([{"path": "tools/quest-native/passthrough.cpp", "size": 10, "sha256": "7" * 64}], "8" * 40, False)
        self.builder.mod_build = lambda repo: 644
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
            plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(plan["updateStrategy"], "cached-full-build")
        self.assertTrue(plan["requiresUnity"])
        self.assertIn("Native Quest programs", plan["rebuildReasons"][0])
        self.assertEqual(plan["inputs"]["mod"]["modBuild"], 644)
        self.assertTrue((self.output / "latest-update-input.json").exists())

    def test_current_branch_runtime_mod_and_static_patches_use_the_compact_compiler(self):
        self.args.command = "update-mod"
        files = [{"path": path, "size": 4, "sha256": "7" * 64} for path in (
            "src/GloomhavenVR/Core/Plugin.cs", "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestGameBootstrap.cs",
            "tools/QuestWeaver/StartupPatches.cs", "tools/quest-wizard/server.py")]
        self.builder.source_inventory = Mock(return_value=(files, "8" * 40, False))
        self.builder.mod_build = lambda repo: 671
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
            plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(plan["updateStrategy"], "compact-code")
        self.assertEqual(plan["rebuildReasons"], [])
        self.assertEqual(plan["inputs"]["mod"]["files"], files)
        self.assertEqual(plan["inputs"]["mod"]["modBuild"], 671)
        self.builder.source_inventory.assert_called_once_with(ROOT)

    def test_changed_quest_shader_and_package_template_select_current_payload_migration(self):
        self.args.command = "update-mod"
        files = [{"path": path, "size": 4, "sha256": "7" * 64} for path in (
            "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestWorldScreen.shader",
            "unity/GloomhavenVR.Quest/ProjectSettings/ProjectSettings.asset")]
        self.builder.source_inventory = lambda repo: (files, "8" * 40, False)
        self.builder.mod_build = lambda repo: 671
        with patch.object(driver, "package_fingerprint", return_value="0" * 64):
            plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(plan["updateStrategy"], "cached-full-build")
        self.assertIn("QuestWorldScreen.shader", plan["rebuildReasons"][0])
        self.assertIn("package/template ABI", plan["rebuildReasons"][1])

    def test_legacy_apk_without_known_package_abi_migrates_without_guessing(self):
        self.args.command = "update-mod"
        self.builder.source_inventory = lambda repo: ([], "8" * 40, False)
        self.builder.mod_build = lambda repo: 671
        with zipfile.ZipFile(self.apk) as original:
            values = {row.filename: original.read(row) for row in original.infolist() if row.filename != apk_update.CAPSULE}
        with zipfile.ZipFile(self.apk, "w") as archive:
            for name, value in values.items(): archive.writestr(name, value)
        with patch.object(driver, "package_fingerprint", side_effect=AssertionError("Unknown old ABI must not be inferred")):
            plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(plan["updateStrategy"], "cached-full-build")
        self.assertIn("no signed compact", plan["rebuildReasons"][0])

    def test_art_producer_changes_rebuild_authored_banks_and_graphics_changes_migrate_original_assets(self):
        paths = ("tools/environment-mesh/roles.py", "tools/environment-mesh/geometry.py", "tools/quest-builder/environment_bank.py",
                 "tools/quest-builder/mod_assets.py", "scripts/generate-environment-meshes.py")
        rows = [{"path": path, "size": 4, "sha256": "7" * 64} for path in paths]
        self.assertEqual({row["path"] for row in driver._art_rows(rows)}, set(paths))
        self.assertEqual(driver.rebuild_reasons(rows, []), [])
        for path in ("tools/quest-shaders/produce.py", "tools/quest-compute/recovery.py", "src/GloomhavenVR/Assets/GloomhavenVR_logo.png"):
            self.assertIn(path, driver.rebuild_reasons([{"path": path, "size": 4, "sha256": "7" * 64}], [])[0])

    def test_known_current_payload_rebuild_skips_compact_compiler_and_preserves_base(self):
        self.args.command = "update-mod"
        files = [{"path": "tools/quest-native/passthrough.cpp", "size": 4, "sha256": "7" * 64}]
        self.builder.source_inventory = lambda repo: (files, "8" * 40, False)
        self.builder.mod_build = lambda repo: 671
        migrated = self.output / "builds/current-player.apk"
        self.builder.rebuild_update = Mock(return_value=migrated)
        original = self.apk.read_bytes()
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_compile_code", side_effect=AssertionError("Known asset migration must not compile twice")), \
             patch.object(apk_update, "repack", side_effect=AssertionError("Old payload must not be stamped as current")):
            self.assertEqual(driver.run(self.builder, self.args, ROOT, self.output), migrated)
        call = self.builder.rebuild_update.call_args
        self.assertEqual(call.args[1]["inputs"]["mod"]["files"], files)
        self.assertEqual(call.args[1]["updateStrategy"], "cached-full-build")
        self.assertEqual(self.apk.read_bytes(), original)
        self.assertFalse((self.output / "latest-build.json").exists())
        # Feed the event into the production schema too: a mock accepting an
        # invented 'running' status would conceal a real fallback abort.
        spec = importlib.util.spec_from_file_location("update_driver_actual_progress", ROOT / "tools/quest-builder/progress.py")
        progress = importlib.util.module_from_spec(spec); spec.loader.exec_module(progress)
        emitted = self.builder.build_progress.event.call_args
        value = progress.event(*emitted.args, **emitted.kwargs, stream=io.StringIO())
        self.assertEqual(value["status"], "progress")

    def test_only_typed_valid_compatibility_failure_dispatches_rebuild(self):
        self.args.command = "update-mod"
        self.builder.source_inventory = lambda repo: ([], "8" * 40, False)
        self.builder.mod_build = lambda repo: 671
        self.builder.snapshot = Mock()
        self.builder.rebuild_update = Mock(return_value=self.output / "recovered.apk")
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_game_subset"), \
             patch.object(driver, "_compile_code", side_effect=apk_update.RebuildRequired("Serialized script layout changed")):
            self.assertEqual(driver.run(self.builder, self.args, ROOT, self.output), self.output / "recovered.apk")
        self.assertIn("Serialized script layout changed", self.builder.rebuild_update.call_args.args[1]["rebuildReasons"])
        self.assertFalse((self.output / "latest-build.json").exists())
        self.builder.rebuild_update.reset_mock()
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_game_subset"), \
             patch.object(driver, "_compile_code", side_effect=storage.BuildError("Compiler receipt corrupted")):
            with self.assertRaisesRegex(storage.BuildError, "receipt corrupted"):
                driver.run(self.builder, self.args, ROOT, self.output)
        self.builder.rebuild_update.assert_not_called()

    def test_missing_owned_inputs_and_wrong_signer_never_dispatch_rebuild(self):
        self.args.command = "update-mod"
        self.builder.source_inventory = lambda repo: ([{"path": "tools/quest-native/native.c", "size": 4, "sha256": "7" * 64}], "8" * 40, False)
        self.builder.mod_build = lambda repo: 671
        self.builder.rebuild_update = Mock()
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", side_effect=storage.BuildError("Signing key does not belong to APK")):
            with self.assertRaisesRegex(storage.BuildError, "does not belong"):
                driver.run(self.builder, self.args, ROOT, self.output)
        (self.game / "Managed/GH.Runtime.dll").unlink()
        with self.assertRaisesRegex(storage.BuildError, "does not match"):
            driver.run(self.builder, self.args, ROOT, self.output)
        self.builder.rebuild_update.assert_not_called()

    def test_fresh_editor_serialized_migration_is_typed_but_other_errors_are_not(self):
        log = self.output / "logs/update-code-unity.log"
        message = "InvalidDataException: Retained original serialized script is missing: GH.Runtime.dll:Fixture:Original; a full build is required.\n"
        def compiler_migration(*args, **kwargs):
            log.parent.mkdir(parents=True, exist_ok=True); log.write_text(message)
            raise storage.BuildError("Actual Unity player failed")
        self.builder.command = compiler_migration
        with self.assertRaisesRegex(apk_update.RebuildRequired, "serialized script is missing"):
            driver._compact_player_command(self.builder, ["Unity", "fixture"], self.output / "launch.log", log, {})
        self.builder.command = Mock(side_effect=storage.BuildError("Editor did not start"))
        with self.assertRaises(storage.BuildError) as stale:
            driver._compact_player_command(self.builder, ["Unity", "fixture"], self.output / "launch.log", log, {})
        self.assertNotIsInstance(stale.exception, apk_update.RebuildRequired)
        def compiler_failure(*args, **kwargs):
            log.write_text("InvalidOperationException: Native code pair is incomplete\n")
            raise storage.BuildError("Actual Unity compiler failed")
        self.builder.command = compiler_failure
        with self.assertRaises(storage.BuildError) as ordinary:
            driver._compact_player_command(self.builder, ["Unity", "fixture"], self.output / "launch.log", log, {})
        self.assertNotIsInstance(ordinary.exception, apk_update.RebuildRequired)

    def test_profile_publish_and_cached_retry_each_require_the_owned_install(self):
        with patch.object(driver, "signing_tools", return_value={}) as tools, \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"), \
             patch.object(driver, "_compile_code", side_effect=AssertionError("No Unity for profile updates")):
            built = driver.run(self.builder, self.args, ROOT, self.output)
            self.assertEqual(apk_update.inspect_base(built)["modBuild"], 643)
            self.assertEqual(driver.run(self.builder, self.args, ROOT, self.output), built)
            self.assertTrue(json.loads(Path(str(built) + ".build.json").read_text())["retainedGameContent"])
            with zipfile.ZipFile(built) as archive:
                self.assertEqual(json.loads(archive.read(apk_update.PROFILE))["displayName"], "Fixture Owner")
            calls = tools.call_count
            (self.game / "Managed/GH.Runtime.dll").unlink()
            with self.assertRaisesRegex(storage.BuildError, "does not match"):
                driver.run(self.builder, self.args, ROOT, self.output)
            self.assertEqual(tools.call_count, calls)
            self.assertTrue(built.is_file())

    def test_closed_native_pair_reused_before_project_or_compiler(self):
        source = self.root / "source"; manifest = source / "unity/GloomhavenVR.Quest/Packages/manifest.json"
        storage.write_json(manifest, {"dependencies": {}})
        self.builder.ORIGINAL_UGUI_SHA256 = "3" * 64; self.builder.UGUI_LAYOUT_SOURCE_SHA256 = "4" * 64
        stage_root = self.output / "updates" / ("5" * 64); stage_root.mkdir(parents=True)
        compiled = stage_root / "code-player.apk"; compiled.write_bytes(b"matched compiler fixture")
        receipt = Path(str(compiled) + ".code-build.json"); receipt.write_text("{}")
        storage.Stages(self.output).run("update-code", stage_root.name, lambda: ([compiled, receipt], {}))
        with patch.object(apk_update, "collect_code_update", return_value={"qualified": True}) as collect, \
             patch.object(apk_update, "stage_code_project", side_effect=AssertionError("Closed compilation must not run")):
            result = driver._compile_code(self.builder, self.args, self.manifest, self.output, source,
                                          self.game, stage_root / "project", self.apk, {}, stage_root)
        self.assertEqual(result, {"qualified": True}); self.assertEqual(collect.call_count, 1)

    def test_update_without_original_key_stops_before_generating_another(self):
        self.builder.signing = Mock(side_effect=AssertionError("Must not invent a replacement key"))
        with self.assertRaisesRegex(storage.BuildError, "original signing folder"):
            driver._signers(self.builder, {}, self.output, base_apk=self.apk)

    def test_wrong_key_stops_before_compile_without_password_in_arguments(self):
        key = self.output / "signing/quest.keystore"; key.parent.mkdir(parents=True); key.write_bytes(b"key fixture")
        (key.parent / "local-key.json").write_text("{}")
        self.builder.signing = Mock(return_value=(key, {"password": "fixture-private-password", "alias": "quest"}))
        self.builder.command = Mock(side_effect=["Signer #1 certificate SHA-256 digest: " + "a" * 64,
                                               "SHA256: " + ":".join(["BB"] * 32)])
        tools = {}
        for name in ("java", "keytool", "apksigner", "zipalign"):
            path = self.root / (name + ".tool"); path.write_bytes(b"selected tool fixture")
            tools[name] = str(path)
        with self.assertRaisesRegex(storage.BuildError, "does not belong"):
            driver._signers(self.builder, tools, self.output, base_apk=self.apk)
        self.assertNotIn("fixture-private-password", str([call.args for call in self.builder.command.call_args_list]))


if __name__ == "__main__": unittest.main()
