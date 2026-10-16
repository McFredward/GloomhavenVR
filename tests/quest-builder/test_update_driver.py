"""Update orchestration contracts, using real APK/owned-input/storage boundaries.

Signing and binary Resources/AXML writers are injected here; their actual
formats are covered separately. This fixture does not run a Unity player.
"""
import copy
import hashlib
import importlib.util
import io
import json
import shutil
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import apk_update
import apk_input_cache
import storage
import portable_package as portable
import preparation_identity
import release
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
        self.builder = SimpleNamespace(apk_updates=apk_update, apk_input_hashes=apk_input_cache, BuildError=storage.BuildError,
            game_data=lambda root: Path(root), selected_profile=lambda args: (dict(self.profile), b"fixture logo"),
            dlcs=SimpleNamespace(capture=lambda *args: copy.deepcopy(self.ownership)),
            digest=storage.digest, value_hash=storage.value_hash, canonical=storage.canonical,
            _ordinary_owned=storage._ordinary_owned, preparation_identity=preparation_identity,
            write_json=storage.write_json, Stages=storage.Stages, PACKAGE="dev.gloomhavenvr.quest",
            ORIGINAL_UGUI_SHA256="3" * 64, UGUI_LAYOUT_SOURCE_SHA256="4" * 64,
            build_progress=SimpleNamespace(operation=Mock(), event=Mock()))
        signing = patch.object(driver, "select_signing", return_value={"schema": 1, "requestedMode": "auto",
            "mode": "original", "sourceCertificateSha256": "a" * 64, "certificateSha256": "a" * 64})
        signing.start(); self.addCleanup(signing.stop)
        self.args = SimpleNamespace(command="update-profile", update_kind="update-profile", game_root=str(self.game),
                                    base_apk=str(self.apk), signing_root=None)

    def write_apk(self, *, extra=None):
        lib = bytearray(64); lib[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", lib, 18, 183)
        values = {apk_update.INPUT: storage.canonical(self.manifest),
                  apk_update.INSTALLATION: storage.canonical({"schema": 1, "inputKey": "a" * 64}),
                  apk_update.UPDATE: storage.canonical({"schema": 1, "gameInputKey": "a" * 64,
                    "updateKey": "2" * 64, "modBuild": self.current_mod["modBuild"], "mod": self.current_mod}),
                  apk_update.CAPSULE: storage.canonical(self.capsule), apk_update.LIBRARY: bytes(lib),
                  apk_update.METADATA: struct.pack("<II", 0xFAB11BAF, 29), "assets/fixture.resource": b"unchanged payload" * 10000}
        values.update(extra or {})
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in values.items(): archive.writestr(name, value)

    def test_profile_edit_keeps_latest_mod_instead_of_original_conversion(self):
        plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(plan["inputs"]["mod"], self.current_mod)
        self.assertEqual(plan["gameInputKey"], self.manifest["inputKey"])
        self.assertEqual(apk_update.inspect_base(self.apk)["manifest"]["mod"]["modBuild"], 625)
        self.assertEqual(json.loads((self.output / "latest-update-input.json").read_text())["plan"],
                         "updates/" + plan["updateKey"] + "/plan.json")

    def test_repeated_inspection_reuses_unchanged_apk_proof_but_still_requires_owned_game(self):
        first = driver.inspect(self.builder, self.args, ROOT, self.output)
        actual = storage.digest
        def refuse_apk_read(path, *args, **kwargs):
            if Path(path) == self.apk:
                raise AssertionError("The unchanged selected APK already has an actual byte witness")
            return actual(path, *args, **kwargs)
        with patch.object(storage, "digest", side_effect=refuse_apk_read):
            again = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(again["baseApkSha256"], first["baseApkSha256"])
        (self.game / "Managed/GH.Runtime.dll").unlink()
        with patch.object(apk_input_cache, "digest_apk", side_effect=AssertionError("Ownership qualification must precede cache reuse")):
            with self.assertRaisesRegex(storage.BuildError, "does not match this APK's original assembly"):
                driver.inspect(self.builder, self.args, ROOT, self.output)

    def test_imported_apk_uses_its_actual_closed_byte_proof_without_a_second_read(self):
        actual = storage.digest(self.apk)
        self.builder.portable_packages = SimpleNamespace(imported_apk_digest=Mock(return_value=actual))
        with patch.object(apk_input_cache, "digest_apk", side_effect=AssertionError("Portable importer already qualified these exact APK bytes")):
            plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertEqual(plan["baseApkSha256"], actual)
        self.builder.portable_packages.imported_apk_digest.assert_called_once_with(self.apk, self.output)

    def external_bank(self):
        bank = self.apk.parent / portable.CONTENT
        with zipfile.ZipFile(bank, "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr("StreamingAssets/owned.bundle", b"actual owned nested content" * 8192)
        contract = {"schema": 1, "package": portable.PACKAGE, "inputKey": self.manifest["inputKey"],
                    "files": [{"file": portable.CONTENT, "size": bank.stat().st_size,
                               "sha256": storage.digest(bank), "archive": portable.ARCHIVE}]}
        self.write_apk(extra={"assets/Quest/content-delivery.json": storage.canonical(contract)})
        return bank

    def test_repeated_real_zip_updates_preserve_direct_locator_after_import_cleanup(self):
        bank = self.external_bank(); original = bank.read_bytes()
        package = self.root / "shared-install-package.zip"
        portable.export_package(self.apk, package, self.output)
        imported = portable.import_apk(package, self.output)
        self.builder.portable_packages = portable
        self.args.base_apk = str(package)
        outputs = []
        with patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"):
            for index in range(10):
                self.profile["displayName"] = "Fixture Owner " + str(index)
                updated = driver.run(self.builder, self.args, ROOT, self.output)
                details = json.loads(Path(str(updated) + ".build.json").read_text())
                self.assertEqual(details["sourcePackage"], str(package))
                closed = json.loads((self.output / "receipts/build" / (details["updateKey"] + ".json")).read_text())
                self.assertEqual(closed["details"]["sourcePackage"], str(package))
                self.assertEqual(json.loads((self.output / "latest-build.json").read_text())["details"]["sourcePackage"], str(package))
                outputs.append(updated)
                self.args.base_apk = str(updated)
                # Retain evidence, but remove both the optional lookup DB and
                # prior materialized APKs. The current closed owner must stand
                # alone; ten updates exceed the helper's eight-hop bound.
                for cache in (self.output / "cache").glob("portable-witnesses.sqlite3*"):
                    cache.unlink()
                if index == 1: shutil.rmtree(imported.parent)
                if index:
                    outputs[index - 1].unlink()
                storage._invocation_file_proofs.clear()
        source = portable.content_source(outputs[-1], self.output)
        self.assertEqual(source["path"], package); self.assertEqual(source["member"], portable.CONTENT)
        with portable.open_content(source) as stream:
            self.assertEqual(stream.read(), original)
        self.assertFalse(imported.exists())

    def test_physical_bank_is_not_mislabelled_as_a_portable_package(self):
        self.external_bank(); self.builder.portable_packages = portable
        with patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"):
            updated = driver.run(self.builder, self.args, ROOT, self.output)
        self.assertIsNone(json.loads(Path(str(updated) + ".build.json").read_text())["sourcePackage"])
        self.assertEqual(portable.content_source(self.apk, self.output)["member"], None)

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

    def producer_fixture(self):
        source = self.root / "producer-source"
        for name, body in (("builder.py", b"def prepare():\n    return 1\n"),
                           ("preparation_identity.py", (ROOT / "tools/quest-builder/preparation_identity.py").read_bytes())):
            path = source / "tools/quest-builder" / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(body)
        rows = [storage.record_file(source / name, name) for name in
                ("tools/quest-builder/builder.py", "tools/quest-builder/preparation_identity.py")]
        for name in ("update_driver.py", "apk_update.py", "apk_manifest.py", "storage.py", "profile.py", "dlcs.py", "proton_helper_update.py"):
            (source / "tools/quest-builder" / name).write_bytes((ROOT / "tools/quest-builder" / name).read_bytes())
        self.manifest["mod"]["files"] = copy.deepcopy(rows)
        self.current_mod["files"] = copy.deepcopy(rows)
        self.capsule["payloadProducer"] = driver.payload_producer(self.builder, source, rows)
        self.write_apk()
        self.args.command = "update-mod"; self.builder.mod_build = lambda repo: 671
        return source, rows

    def test_signed_actual_source_normalization_allows_only_equivalent_builder_edits(self):
        source, old_rows = self.producer_fixture()
        path = source / "tools/quest-builder/builder.py"
        path.write_bytes(b"# host source formatting changed\n\ndef prepare():\n    return 1\n")
        rows = [storage.record_file(source / row["path"], row["path"]) for row in old_rows]
        self.builder.source_inventory = lambda repo: (rows, "8" * 40, False)
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
            compatible = driver.inspect(self.builder, self.args, source, self.output)
        self.assertEqual(compatible["updateStrategy"], "compact-code")
        path.write_bytes(b"def prepare():\n    consume_new_original_asset()\n    return 1\n")
        rows = [storage.record_file(source / row["path"], row["path"]) for row in old_rows]
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
            incompatible = driver.inspect(self.builder, self.args, source, self.output)
        self.assertEqual(incompatible["updateStrategy"], "cached-full-build")
        self.assertIn("builder.py", incompatible["rebuildReasons"][0])

    def test_digest_tamper_or_absent_proof_never_ignores_changed_builder_rows(self):
        source, old_rows = self.producer_fixture()
        path = source / "tools/quest-builder/builder.py"; path.write_bytes(b"# equivalent formatting\ndef prepare():\n    return 1\n")
        rows = [storage.record_file(source / row["path"], row["path"]) for row in old_rows]
        self.builder.source_inventory = lambda repo: (rows, "8" * 40, False)
        for producer in (None, {**self.capsule["payloadProducer"], "sha256": "0" * 64}):
            if producer is None: self.capsule.pop("payloadProducer")
            else: self.capsule["payloadProducer"] = producer
            self.write_apk()
            with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
                plan = driver.inspect(self.builder, self.args, source, self.output)
            self.assertEqual(plan["updateStrategy"], "cached-full-build")

    def test_changed_shader_is_not_hidden_by_an_equal_signed_builder_digest(self):
        source, rows = self.producer_fixture()
        shader = {"path": "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestWorldScreen.shader", "size": 4, "sha256": "7" * 64}
        self.builder.source_inventory = lambda repo: ([*rows, shader], "8" * 40, False)
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
            plan = driver.inspect(self.builder, self.args, source, self.output)
        self.assertEqual(plan["updateStrategy"], "cached-full-build")
        self.assertIn("QuestWorldScreen.shader", plan["rebuildReasons"][0])

    def test_actual_legacy_b665_producers_without_new_capsule_field_retain_game_payloads(self):
        # These are the actual immutable pre-update Git source blobs, rather
        # than invented matching digests. The historical capsule lacked the
        # newly introduced producer field; its signed inventory was sufficient.
        paths = ("tools/quest-builder/builder.py", "tools/quest-builder/preparation_identity.py",
                 "tools/quest-builder/campaign_compute.py", "tools/quest-builder/campaign_native_shaders.py")
        rows = [storage.record_file(ROOT / name, name) for name in paths]
        self.builder.source_inventory = lambda repo: (rows, "8" * 40, False)
        self.args.command = "update-mod"; self.builder.mod_build = lambda repo: 671
        for revision in ("8063c8fc8", "cb6f84fb2", "f23bea592", "5cfcfa1c0", "4b4b40b1d"):
            old = []
            for name in paths:
                body = subprocess.check_output(["git", "-C", str(ROOT), "show", revision + ":" + name])
                old.append({"path": name, "size": len(body), "sha256": hashlib.sha256(body).hexdigest()})
            self.current_mod.update(files=old, modBuild=665)
            self.capsule.pop("payloadProducer", None); self.write_apk()
            with self.subTest(revision=revision), patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
                plan = driver.inspect(self.builder, self.args, ROOT, self.output)
            self.assertEqual(plan["updateStrategy"], "compact-code")
            self.assertEqual(plan["rebuildReasons"], [])
            self.assertTrue(plan["codeChanged"]); self.assertTrue(plan["requiresUnity"])
            # Changing even one byte identity loses the finite old-source proof.
            self.current_mod["files"][0]["sha256"] = "0" * 64; self.write_apk()
            with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]):
                unknown = driver.inspect(self.builder, self.args, ROOT, self.output)
            self.assertEqual(unknown["updateStrategy"], "cached-full-build")
            self.assertIn("builder.py", unknown["rebuildReasons"][0])

    def test_known_audit_alias_never_accepts_new_compute_producer_or_removed_audit(self):
        paths = ("tools/quest-builder/campaign_compute.py", "tools/quest-builder/campaign_native_shaders.py")
        current = [storage.record_file(ROOT / name, name) for name in paths]
        old = []
        for name in paths:
            body = subprocess.check_output(["git", "-C", str(ROOT), "show", "f23bea592:" + name])
            old.append({"path": name, "size": len(body), "sha256": hashlib.sha256(body).hexdigest()})
        self.assertEqual(driver.rebuild_reasons(current, old), [])
        changed = copy.deepcopy(current)
        changed[0]["sha256"] = hashlib.sha256((ROOT / paths[0]).read_bytes().replace(
            b"converter.ensure(cache", b"different_converter.ensure(cache")).hexdigest()
        self.assertIn("campaign_compute.py", driver.rebuild_reasons(changed, old)[0])
        changed = copy.deepcopy(current); changed[1]["sha256"] = "0" * 64
        self.assertIn("campaign_native_shaders.py", driver.rebuild_reasons(changed, old)[0])
        self.assertIn("campaign_native_shaders.py", driver.rebuild_reasons(current[:1], old)[0])

    def test_host_only_update_reuses_signed_native_pair_without_snapshot_or_unity(self):
        self.args.command = "update-mod"
        code = {"path": "src/GloomhavenVR/Core/Plugin.cs", "size": 4, "sha256": "7" * 64}
        self.current_mod.update(files=[code], modBuild=671); self.write_apk()
        self.builder.source_inventory = lambda repo: ([code, {"path": "tools/quest-wizard/server.py", "size": 4,
                                                              "sha256": "8" * 64}], "8" * 40, False)
        self.builder.mod_build = lambda repo: 671
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_game_subset", side_effect=AssertionError("No owned-game snapshot for host-only edits")), \
             patch.object(driver, "_compile_code", side_effect=AssertionError("The signed native pair is already current")), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"):
            updated = driver.run(self.builder, self.args, ROOT, self.output)
        with zipfile.ZipFile(self.apk) as before, zipfile.ZipFile(updated) as after:
            for name in (apk_update.LIBRARY, apk_update.METADATA, "assets/fixture.resource"):
                self.assertEqual(before.read(name), after.read(name))
            update = json.loads(after.read(apk_update.UPDATE))
            self.assertEqual(update["mod"]["files"][1]["path"], "tools/quest-wizard/server.py")
        plan = json.loads((self.output / "updates" / update["updateKey"] / "plan.json").read_text())
        self.assertFalse(plan["requiresUnity"]); self.assertFalse(plan["codeChanged"]); self.assertFalse(plan["artChanged"])
        self.assertFalse((self.output / "inputs/mod").exists())
        self.assertFalse((self.output / "updates/game-inputs").exists())

    def test_changed_runtime_weaver_or_compiler_source_always_requires_native_pair(self):
        for name in ("src/GloomhavenVR/Core/Plugin.cs", "tools/QuestWeaver/StartupPatches.cs",
                     "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestGameBootstrap.cs",
                     "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs",
                     "tools/quest-builder/apk_update.py", "Directory.Build.props", "global.json"):
            before = [{"path": name, "size": 1, "sha256": "1" * 64}]
            after = [{"path": name, "size": 1, "sha256": "2" * 64}]
            with self.subTest(path=name): self.assertNotEqual(driver._code_rows(before), driver._code_rows(after))

    def test_actual_verified_release_metadata_change_skips_native_compiler_and_updates_source_identity(self):
        source = self.root / "released-source"; source.mkdir()
        code = source / "src/GloomhavenVR/Core/Plugin.cs"; code.parent.mkdir(parents=True)
        code.write_bytes(b"unchanged actual runtime source")
        code_row = storage.record_file(code, code.relative_to(source).as_posix())
        manifest = source / release.MANIFEST
        def publish(commit):
            storage.write_json(manifest, {"schema": 1, "kind": release.RELEASE_KIND, "sourceCommit": commit,
                "modBuild": 671, "files": [code_row], "localDependencyRoots": list(release.LOCAL_DEPENDENCIES)})
        # Real release byte verification supplies the manifest row. Small fixture
        # package requirements avoid shipping unrelated pinned native archives.
        with patch.object(release, "PUBLIC_PACKAGES", {}), patch.object(release, "REQUIRED", {code_row["path"]}), \
             patch.object(release, "LINUX_REQUIRED", set()):
            publish("1" * 40); previous, _, _ = release.verified_source_inventory(source)
            publish("2" * 40); current, commit, dirty = release.verified_source_inventory(source)
        self.assertEqual(previous[:-1], current[:-1]); self.assertNotEqual(previous[-1], current[-1])
        self.current_mod.update(files=previous, modBuild=671); self.write_apk()
        self.args.command = "update-mod"; self.builder.mod_build = lambda repo: 671
        self.builder.source_inventory = lambda repo: (current, commit, dirty)
        for name in ("update_driver.py", "apk_update.py", "apk_manifest.py", "builder.py", "storage.py", "profile.py", "dlcs.py", "proton_helper_update.py"):
            path = source / "tools/quest-builder" / name; path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / "tools/quest-builder" / name, path)
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_compile_code", side_effect=AssertionError("Release metadata is not native code")), \
             patch.object(driver, "_game_subset", side_effect=AssertionError("No game snapshot for release metadata")), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"):
            updated = driver.run(self.builder, self.args, source, self.output)
        with zipfile.ZipFile(self.apk) as before, zipfile.ZipFile(updated) as after:
            for name in (apk_update.LIBRARY, apk_update.METADATA, "assets/fixture.resource"):
                self.assertEqual(before.read(name), after.read(name))
            update = json.loads(after.read(apk_update.UPDATE))
        self.assertEqual(update["mod"]["files"], current)
        self.assertEqual(update["mod"]["key"], storage.value_hash({"files": current}))
        self.assertNotEqual(update["mod"]["key"], storage.value_hash({"files": previous}))
        plan = json.loads((self.output / "updates" / update["updateKey"] / "plan.json").read_text())
        self.assertFalse(plan["requiresUnity"]); self.assertFalse(plan["codeChanged"])
        self.assertFalse((self.output / "updates/source-inputs").exists())

    def test_actual_code_source_snapshot_excludes_unused_large_banks_without_claiming_complete_inputs(self):
        repo = self.root / "selected-source"
        values = {"src/GloomhavenVR/Core/Plugin.cs": b"changed runtime code",
                  "tools/QuestWeaver/StartupPatches.cs": b"current static original-game adapter",
                  "unity/GloomhavenVR.Quest/Packages/manifest.json": b'{"dependencies": {}}',
                  "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs": b"compiler source",
                  "libs/RuntimeDeps/Unity.XR.OpenXR.dll": b"owned compiler reference",
                  "Directory.Build.props": b"current build settings",
                  "quest-builder-release.json": b'{"completeReleaseInventory": "must not claim a partial source copy"}',
                  "prebuilt/quest-proton-runtime.tar.zst": b"unchanged native package" * 8192,
                  "unity/GloomhavenVR.Assets/Assets/unchanged.asset": b"unchanged authored asset" * 4096}
        for name, body in values.items():
            path = repo / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(body)
        for name in ("update_driver.py", "apk_update.py", "apk_manifest.py", "builder.py", "storage.py", "profile.py", "dlcs.py", "proton_helper_update.py"):
            path = repo / "tools/quest-builder" / name; path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / "tools/quest-builder" / name, path)
        rows = [storage.record_file(repo / name, name) for name in values]
        previous = copy.deepcopy(rows); previous[0]["sha256"] = "0" * 64
        self.current_mod.update(files=previous, modBuild=671); self.write_apk()
        self.args.command = "update-mod"; self.builder.mod_build = lambda repo: 671
        self.builder.source_inventory = lambda repo: (rows, "8" * 40, False)
        self.builder.snapshot = storage.snapshot
        observed = []
        def compile_code(builder, args, inputs, output, source, game, *_):
            for name, body in values.items():
                if name.startswith(("prebuilt/", "unity/GloomhavenVR.Assets/")) or name == "quest-builder-release.json":
                    self.assertFalse((source / name).exists())
                else: self.assertEqual((source / name).read_bytes(), body)
            self.assertTrue((game / "Managed/GH.Runtime.dll").is_file())
            self.assertTrue(source.is_relative_to(output / "updates/source-inputs"))
            self.assertFalse((output / "inputs/mod").exists())
            self.assertEqual(inputs["mod"]["files"], rows)  # Full immutable identity is retained.
            observed.append(source); return {"replacements": {}}
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_compile_code", side_effect=compile_code), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"):
            self.assertTrue(driver.run(self.builder, self.args, repo, self.output).is_file())
        self.assertEqual(len(observed), 1)

    def test_normalizer_reads_exact_current_captured_source_and_rejects_drift(self):
        source, rows = self.producer_fixture()
        (source / "tools/quest-builder/builder.py").write_bytes(b"changed after inventory capture")
        with self.assertRaisesRegex(storage.BuildError, "changed or lack source provenance"):
            driver.payload_producer(self.builder, source, rows)
        with self.assertRaisesRegex(storage.BuildError, "inventory is incomplete"):
            driver.payload_producer(self.builder, source, rows[:1])
        actual = [storage.record_file(ROOT / name, name) for name in
                  ("tools/quest-builder/builder.py", "tools/quest-builder/preparation_identity.py")]
        proof = driver.payload_producer(self.builder, ROOT, actual)
        self.assertEqual(proof["source"], actual[0])
        self.assertEqual(proof["normalizer"], actual[1])
        self.assertEqual(proof["sha256"], preparation_identity.builder_producer_digest((ROOT / actual[0]["path"]).read_bytes()))

    def test_known_incompatible_payload_requires_a_decision_without_conversion(self):
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
            with self.assertRaisesRegex(storage.BuildError, "Compact update needs a rebuild decision:"):
                driver.run(self.builder, self.args, ROOT, self.output)
        self.builder.rebuild_update.assert_not_called()
        selected = json.loads((self.output / "latest-update-input.json").read_text())
        plan = json.loads((self.output / selected["plan"]).read_text())
        self.assertEqual(plan["inputs"]["mod"]["files"], files)
        self.assertEqual(plan["updateStrategy"], "cached-full-build")
        self.assertTrue(plan["rebuildDecisionRequired"])
        self.assertEqual(self.apk.read_bytes(), original)
        self.assertFalse((self.output / "latest-build.json").exists())
        # Feed the event into the production schema too: a mock accepting an
        # invented 'running' status would conceal a real fallback abort.
        spec = importlib.util.spec_from_file_location("update_driver_actual_progress", ROOT / "tools/quest-builder/progress.py")
        progress = importlib.util.module_from_spec(spec); spec.loader.exec_module(progress)
        emitted = self.builder.build_progress.event.call_args
        value = progress.event(*emitted.args, **emitted.kwargs, stream=io.StringIO())
        self.assertEqual(value["status"], "progress")

    def test_typed_registry_incompatibility_preserves_results_and_never_converts(self):
        self.args.command = "update-mod"
        self.builder.source_inventory = lambda repo: ([], "8" * 40, False)
        self.builder.mod_build = lambda repo: 671
        self.builder.snapshot = Mock()
        self.builder.rebuild_update = Mock(return_value=self.output / "recovered.apk")
        retained = {}
        for name in ("latest-build.json", "cache/full-original-recovery/old/completed.asset",
                     "projects/old/Library/native-object", "receipts/update-code/old.json"):
            path = self.output / name; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(("retained " + name).encode()); retained[path] = path.read_bytes()
        original = self.apk.read_bytes()
        with patch.object(driver, "package_fingerprint", return_value=self.capsule["packageAbi"]), \
             patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(driver, "_game_subset"), \
             patch.object(driver, "_compile_code", side_effect=apk_update.RebuildRequired("Serialized script layout changed")):
            for _ in range(2):
                with self.assertRaisesRegex(storage.BuildError, "Compact update needs a rebuild decision:"):
                    driver.run(self.builder, self.args, ROOT, self.output)
        self.builder.rebuild_update.assert_not_called()
        selected = json.loads((self.output / "latest-update-input.json").read_text())
        plan = json.loads((self.output / selected["plan"]).read_text())
        self.assertIn("Serialized script layout changed", plan["rebuildReasons"])
        self.assertEqual(plan["updateStrategy"], "compact-code")
        self.assertTrue(plan["rebuildDecisionRequired"])
        self.assertEqual(self.apk.read_bytes(), original)
        for path, contents in retained.items(): self.assertEqual(path.read_bytes(), contents)
        self.assertFalse((self.output / "inputs/game").exists())
        self.assertFalse((self.output / "builds").exists())
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

    def test_fresh_missing_catalog_entry_requires_compiler_repair_not_asset_migration(self):
        log = self.output / "logs/update-code-unity.log"
        identity = "Unity.InputSystem.ForUI.dll|UnityEngine.InputSystem.Plugins.InputForUI|InputSystemProvider"
        def missing_catalog(*args, **kwargs):
            log.parent.mkdir(parents=True, exist_ok=True)
            log.write_text("InvalidDataException: Retained original serialized script is missing: " + identity + "; a full build is required.\n")
            raise storage.BuildError("Actual Unity player failed")
        self.builder.command = missing_catalog
        original = self.apk.read_bytes()
        retained = self.output / "projects/retained/Library/native-object"
        retained.parent.mkdir(parents=True); retained.write_bytes(b"completed compiler object")
        self.builder.rebuild_update = Mock()
        with self.assertRaisesRegex(storage.BuildError, "Compact update repair required:") as missing:
            driver._compact_player_command(self.builder, ["Unity", "fixture"], self.output / "launch.log", log, {})
        self.assertNotIsInstance(missing.exception, apk_update.RebuildRequired)
        self.assertIn(identity, str(missing.exception))
        self.assertNotIn("a full build is required", str(missing.exception))
        self.assertEqual(self.apk.read_bytes(), original)
        self.assertEqual(retained.read_bytes(), b"completed compiler object")
        self.builder.rebuild_update.assert_not_called()

    def test_stale_catalog_errors_and_unrelated_current_failures_are_not_migration_proof(self):
        log = self.output / "logs/update-code-unity.log"
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text("InvalidDataException: Retained original serialized script is missing: GH.Runtime.dll|Fixture|Original; a full build is required.\n")
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

    def test_fresh_missing_script_with_failed_plugin_import_is_not_asset_incompatibility(self):
        log = self.output / "logs/update-code-unity.log"
        for import_error in ("Unable to resolve reference 'System.Private.CoreLib'",
                             "GH.Runtime references itself.",
                             "Reference has errors 'GloomhavenVR'"):
            def failed_import(*args, **kwargs):
                log.parent.mkdir(parents=True, exist_ok=True)
                log.write_text(import_error + "\nInvalidDataException: Retained original serialized script is missing: Apparance.Unity.dll||ApparanceAbout; a full build is required.\n")
                raise storage.BuildError("Actual Unity player failed")
            self.builder.command = failed_import
            with self.subTest(import_error=import_error), self.assertRaisesRegex(storage.BuildError, "Compact update plugin import failed:") as failure:
                driver._compact_player_command(self.builder, ["Unity", "fixture"], self.output / "launch.log", log, {})
            self.assertNotIsInstance(failure.exception, apk_update.RebuildRequired)
            self.assertIn("not evidence that game assets need conversion", str(failure.exception))

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

    def test_verified_repack_hash_closes_the_update_without_repeated_apk_reads(self):
        driver.inspect(self.builder, self.args, ROOT, self.output)
        actual = storage.digest
        def refuse_apk_read(path, *args, **kwargs):
            if Path(path).suffix.lower() == ".apk":
                raise AssertionError("Repack already proved the signed APK bytes")
            return actual(path, *args, **kwargs)
        with patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"), \
             patch.object(apk_update, "digest", wraps=actual) as repack_hash, \
             patch.object(storage, "digest", side_effect=refuse_apk_read), \
             patch.object(self.builder, "digest", side_effect=refuse_apk_read):
            built = driver.run(self.builder, self.args, ROOT, self.output)
            self.assertEqual(sum(Path(call.args[0]).suffix.lower() == ".apk" for call in repack_hash.call_args_list), 1)
            with patch.object(apk_update, "repack", side_effect=AssertionError("Closed output must be reused")):
                self.assertEqual(driver.run(self.builder, self.args, ROOT, self.output), built)
        report = json.loads(Path(str(built) + ".build.json").read_text())
        self.assertEqual(report["apkSha256"], actual(built))
        key = json.loads((self.output / "latest-build.json").read_text())["details"]["updateKey"]
        receipt = storage.Stages(self.output).valid("build", key)
        row = next(item for item in receipt["outputs"] if item["path"] == built.relative_to(self.output).as_posix())
        self.assertEqual(row["sha256"], report["apkSha256"])

    def test_mutation_after_repack_prevents_known_hash_witness_or_success_receipt(self):
        actual = apk_update.repack
        def late_mutation(*args, **kwargs):
            result = actual(*args, **kwargs)
            with Path(result["output"]).open("ab") as stream: stream.write(b"unqualified later edit")
            return result
        original = self.apk.read_bytes()
        with patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: None)), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"), \
             patch.object(apk_update, "repack", side_effect=late_mutation):
            with self.assertRaisesRegex(storage.BuildError, "changed before recording"):
                driver.run(self.builder, self.args, ROOT, self.output)
        self.assertEqual(self.apk.read_bytes(), original)
        self.assertFalse((self.output / "latest-build.json").exists())
        self.assertEqual(list((self.output / "receipts/build").glob("*.json")), [])
        self.assertFalse((self.output / "receipts/.file-witnesses.sqlite3").exists())

    def test_verified_certificate_closes_the_next_selected_update_without_another_hash(self):
        certificate = "1" * 64
        with patch.object(driver, "signing_tools", return_value={}), \
             patch.object(driver, "_signers", return_value=(lambda _: None, lambda _: {"certificateSha256": certificate})), \
             patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML tested separately"):
            built = driver.run(self.builder, self.args, ROOT, self.output)
        report = json.loads(Path(str(built) + ".build.json").read_text())
        self.assertEqual(report["certificateSha256"], certificate)
        # A new selected APK can use its actual closed build witness directly,
        # without first creating another generic hash observation namespace.
        with patch.object(storage, "digest", side_effect=AssertionError("Closed update bytes already have an actual witness")):
            self.assertEqual(apk_input_cache._closed_build(built, self.output), report["apkSha256"])

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
