"""Resume only audited Editor upgrades; preserve all completed owners and Library.

The journal/witness implementation is real. Small synthetic shader pairs replace
the private copyrighted source fingerprints in this portable protocol fixture.
Output ownership comes from the production manifest resolver: post-effects owns
four outputs, case-paths reowns sources through its Campaign manifest, and
script-orders reowns serialized assets through assetIdentityEvidence. Shader
metas deliberately have no invented journal owner.
"""
import copy
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import preparation_identity
import prepare_resume as resume
import shaders
import storage

SCHEDULE = [
    ("base-project", "project-files"),
    *[(name, "startup-content") for name in (
        "post-effects", "loading-resources", "startup-movies", "native-sprites", "loading-sprite",
        "startup-audio", "ui-recipes", "startup-ui", "startup-blur", "dlc-selection", "file-extras")],
    ("native-runtime", "native-runtime"), ("bundled-audio", "audio"),
    *[(name, "textures") for name in ("native-cubemaps", "ordinary-texture-audit", "native-texture2d")],
    *[(name, "graphics") for name in ("campaign-compute", "campaign-shaders")],
    *[(name, "mod-banks") for name in (
        "startup-archive", "archive-cleanup", "package-settings", "mod-resource-banks")],
    *[(name, "preparation-contracts") for name in (
        "compiler-contracts", "case-paths", "script-orders", "final-settings")],
]


class PostEffectImportResumeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="quest-import-upgrade-")
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name)
        self.project = self.output / "projects" / ("b" * 64)
        self.original, self.upgraded = {}, {}
        pins = copy.deepcopy(shaders.SHADERS)
        for name, spec in pins.items():
            self.original[name] = ("original official " + name + "\n").encode()
            self.upgraded[name] = ("exact audited Unity upgrade " + name + "\n").encode()
            spec.update(sourceBytes=len(self.original[name]),
                        sourceSha256=hashlib.sha256(self.original[name]).hexdigest(),
                        importUpgradeSha256=hashlib.sha256(self.upgraded[name]).hexdigest())
        self.pin_patch = patch.object(shaders, "SHADERS", pins)
        self.pin_patch.start(); self.addCleanup(self.pin_patch.stop)
        self.seed()

    def put(self, relative, raw):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return path

    @staticmethod
    def shader_path(name): return "Assets/Shader/Hidden_" + name + ".shader"

    def seed(self):
        first = self.open()
        entries = []
        for name, spec in shaders.SHADERS.items():
            relative = self.shader_path(name)
            self.put(relative, self.original[name])
            metadata = ("fileFormatVersion: 2\nguid: " + spec["guid"] + "\nShaderImporter:\n  userData:\n").encode()
            self.put(relative + ".meta", metadata)
            entries.append(shaders._entry(name, spec, metadata))
        receipt = {"schema": 1, "target": "startup", "changeset": shaders.CHANGESET,
                   "installerSha256": shaders.SOURCE_SHA256,
                   "effectsPackageSha256": shaders.PACKAGE_SHA256, "shaders": entries}
        self.put(shaders.RECEIPT, (json.dumps(receipt) + "\n").encode())
        campaign = "Assets/QuestOriginalCampaign/campaign-shaders.json"
        self.put(campaign, (json.dumps({"schema": 1, "shaders": [{"assetPath": entry["assetPath"]} for entry in entries]}) + "\n").encode())
        self.case_receipt = "QuestStartupEvidence/case-path-migration.json"
        self.put(self.case_receipt, (json.dumps({"schema": 1, "files": [], "manifestSha256": {
            campaign: storage.record_file(self.project / campaign, campaign)["sha256"]}}) + "\n").encode())
        self.value = first.value
        for name, operation in SCHEDULE:
            sentinel = "QuestFixture/closed-" + name
            self.put(sentinel, ("owned " + name).encode())
            paths = [sentinel]
            if name == "post-effects": paths = resume.manifest_contracts(self.project, [shaders.RECEIPT])
            if name == "case-paths": paths = resume.manifest_contracts(self.project, [self.case_receipt, campaign])
            self.value["steps"].append({"name": name, "operation": operation,
                                       "outputs": [first._observe(path) for path in paths]})
        storage.write_json(first.journal, self.value)
        self.journal = first.journal
        first.close()
        self.library = self.put("Library/imported-artifact", b"retain expensive real imports")
        self.unchanged = {path: (path.read_bytes(), path.stat().st_mtime_ns)
                          for path in [self.library, self.project / shaders.RECEIPT,
                              *[self.project / (self.shader_path(name) + ".meta") for name in shaders.SHADERS]]}
        self.assertTrue(preparation_identity._completed_game_preparation(self.value, "game"))

    def open(self, **kwargs):
        return resume.Preparation(self.output, self.project,
                                  input_key=kwargs.pop("input_key", getattr(self, "input_key", "a" * 64)),
                                  target="game", recipe=1, **kwargs)

    def import_sources(self):
        for name in shaders.SHADERS: self.put(self.shader_path(name), self.upgraded[name])

    def assert_retained(self):
        current = json.loads(self.journal.read_text())
        self.assertEqual([(step["name"], step["operation"]) for step in current["steps"]], SCHEDULE)
        self.assertEqual(current["pending"], None)
        for name in shaders.SHADERS:
            path = self.shader_path(name)
            latest = [row for step in current["steps"] for row in step["outputs"] if row["path"] == path][-1]
            self.assertEqual(latest, storage.record_file(self.project / path, path))
        for path, expected in self.unchanged.items():
            self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), expected)

    def test_real_producer_contracts_have_no_invented_shader_meta_owner(self):
        post = self.value["steps"][1]["outputs"]
        self.assertEqual(len(post), 4)
        expected = {shaders.RECEIPT, *[self.shader_path(name) for name in shaders.SHADERS]}
        self.assertEqual({row["path"] for row in post}, expected)
        all_paths = {row["path"] for step in self.value["steps"] for row in step["outputs"]}
        for name in shaders.SHADERS:
            self.assertNotIn(self.shader_path(name) + ".meta", all_paths)
            owners = [step["name"] for step in self.value["steps"] if any(
                row["path"] == self.shader_path(name) for row in step["outputs"])]
            self.assertEqual(owners, ["post-effects", "case-paths"])

    def seed_script_orders(self):
        """Use the real receipt resolver to expose script-orders' later owner."""
        import script_remap_resume as mapping
        old, new = "a" * 32, "b" * 32
        self.script_asset = "Assets/GameObject/AboutTab.prefab"
        self.script_original = ("%YAML 1.1\n  m_Script: {fileID: -619905303, guid: " + old +
                                ", type: 3}\n  m_OnClick: originalCallback\n").encode()
        self.script_imported = self.script_original.replace(
            ("fileID: -619905303, guid: " + old).encode(), ("fileID: 11500000, guid: " + new).encode())
        binding = {"assemblyName": "UnityEngine.UI", "fullName": "UnityEngine.UI.Button", "oldGuid": old,
                   "oldFileId": -619905303}
        self.put(self.script_asset, self.script_original)
        self.put(mapping.MANIFESTS[0], json.dumps({"schema": 1, "assetPaths": [self.script_asset],
            "bindings": [binding], "disabledPluginGuids": [old]}).encode())
        self.put("Packages/manifest.json", json.dumps({"dependencies": {"com.unity.ugui": "1.0.0"}}).encode())
        self.put("Packages/com.unity.ugui/Runtime/UI/Core/Button.cs", b"namespace UnityEngine.UI { public class Button {} }")
        self.put("Packages/com.unity.ugui/Runtime/UI/Core/Button.cs.meta", ("guid: " + new + "\nMonoImporter:\n").encode())
        self.put(mapping.RECEIPT, json.dumps({"schema": 1, "callbacksAndOtherSerializedBytesPreserved": True,
            "replacements": [{**binding, "newGuid": new, "newFileId": 11500000}], "assets": []}).encode())
        script_receipt = "QuestStartupEvidence/script-orders-source.json"
        script_manifest = "Assets/QuestOriginalStartup/script-orders.json"
        self.put(script_manifest, json.dumps({"schema": 1, "entries": [], "excluded": []}).encode())
        self.put(script_receipt, json.dumps({"schema": 1,
            "bindingManifest": storage.record_file(self.project / mapping.MANIFESTS[0], mapping.MANIFESTS[0]),
            "manifest": storage.record_file(self.project / script_manifest, script_manifest),
            "assetIdentityEvidence": [storage.record_file(self.project / self.script_asset, self.script_asset)],
            "plugins": []}).encode())
        # A Campaign material/scene manifest already records this asset under
        # case-paths. The later script-order identity census supersedes it.
        first = self.open()
        first.value["steps"][24]["outputs"].append(first._observe(self.script_asset))
        first.value["steps"][21]["outputs"].append(first._observe("Packages/manifest.json"))
        first.value["steps"][25]["outputs"] = [first._observe(path) for path in
            resume.manifest_contracts(self.project, [script_receipt, script_manifest])]
        storage.write_json(self.journal, first.value)
        self.value = copy.deepcopy(first.value)
        first.close()
        return mapping

    def test_script_orders_identity_census_owns_legitimate_package_pointer_changes(self):
        self.seed_script_orders()
        owners = [step["name"] for step in self.value["steps"] if any(
            row["path"] == self.script_asset for row in step["outputs"])]
        self.assertEqual(owners, ["case-paths", "script-orders"])
        self.import_sources()
        self.put(self.script_asset, self.script_imported)
        second = self.open(); second.close()
        self.assert_retained()
        owned = {row["path"]: row for row in json.loads(self.journal.read_text())["steps"][25]["outputs"]}
        self.assertEqual(owned[self.script_asset], storage.record_file(self.project / self.script_asset, self.script_asset))
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(
                resume, "digest", side_effect=AssertionError("Warm original imports need no payload hash")), patch.object(
                __import__("script_remap_resume").ScriptRemap, "_load", side_effect=AssertionError("Warm import must not scan package metas")):
            third = self.open(); third.close()

    def test_real_current_input_migration_retains_all_three_import_mutation_classes(self):
        import import_workspace
        import preparation_metadata
        before = {"schema": 1, "mod": {"modBuild": 660, "files": []},
                  "game": {"key": "d" * 64, "unityVersion": "2021.3.5f1"}, "profile": {"provider": "steam"}}
        self.input_key = storage.value_hash(before)
        before["inputKey"] = self.input_key
        self.project = self.output / "projects" / ("c" * 64)
        self.seed()
        self.seed_script_orders()
        storage.write_json(self.output / "manifests" / (self.input_key + ".json"), before)
        first = self.open()
        for path, (owner, archive) in preparation_metadata.MANIFESTS.items():
            archive_path = "Assets/StreamingAssets/" + archive
            self.put(archive_path, ("retained original " + archive).encode())
            row = first._observe(archive_path)
            stage = next(step for step in first.value["steps"] if step["name"] == owner)
            stage["outputs"].append(row)
            storage.write_json(self.project / path, {"schema": 1, "inputKey": self.input_key,
                "archive": archive, "archiveSha256": row["sha256"], "files": []})
            stage["outputs"].append(first._observe(path))
        storage.write_json(self.project / preparation_metadata.INPUTS, before)
        storage.write_json(self.project / preparation_metadata.SETTINGS, {"schema": 1, "target": "game",
            "inputKey": self.input_key, "profileSha256": "e" * 64, "package": "dev.gloomhavenvr.quest", "modBuild": 660})
        settings_path = "ProjectSettings/ProjectSettings.asset"
        self.put(settings_path, b"%YAML 1.1\n--- !u!129 &1\nPlayerSettings:\n  m_ActiveColorSpace: 0\n"
                               b"  companyName: Original PC\n  m_BuildTargetGraphicsAPIs: []\n  activeInputHandler: 0\n")
        import_workspace.stage(self.project, "game")
        final = first.value["steps"][-1]
        final["outputs"] += [first._observe(path) for path in resume.manifest_contracts(self.project,
            [import_workspace.RECEIPT], extra=(preparation_metadata.INPUTS, preparation_metadata.SETTINGS))]
        storage.write_json(self.journal, first.value)
        first.close()
        self.import_sources()
        self.put(self.script_asset, self.script_imported)
        settings = (self.project / settings_path).read_bytes().replace(b"Original PC", b"GloomhavenVR")
        self.put(settings_path, settings)
        previous_key = self.input_key
        after = copy.deepcopy(before); after.pop("inputKey"); after["mod"]["modBuild"] = 661
        self.input_key = storage.value_hash(after); after["inputKey"] = self.input_key
        second = self.open(compatible_input_key=previous_key, current_inputs=after)
        second.close()
        current = json.loads(self.journal.read_text())
        self.assertEqual(current["inputKey"], self.input_key)
        self.assertEqual(len(current["steps"]), 27)
        self.assertIsNone(current["pending"])
        self.assertEqual(json.loads((self.project / preparation_metadata.INPUTS).read_text()), after)
        self.assertEqual(current["editorSettings"]["accepted"], storage.record_file(self.project / settings_path, settings_path))
        self.assertEqual(self.library.read_bytes(), self.unchanged[self.library][0])
        # Simulate the next isolated process: no invocation hash can conceal a
        # missing transfer from the prior input's database into the new one.
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(
                resume, "digest", side_effect=AssertionError("Migrated import witnesses must be durable")):
            third = self.open(); third.close()
        self.assert_retained()

    def test_script_orders_asset_non_pointer_changes_are_rejected(self):
        self.seed_script_orders()
        self.import_sources()
        self.put(self.script_asset, self.script_imported.replace(b"originalCallback", b"foreignCallback"))
        with self.assertRaisesRegex(storage.BuildError, "changed in script-orders: " + self.script_asset):
            self.open()
        self.assertEqual(json.loads(self.journal.read_text())["steps"], self.value["steps"])
        self.assertEqual(self.library.read_bytes(), self.unchanged[self.library][0])

    def test_changed_unowned_meta_reports_the_failed_frozen_receipt_gate(self):
        self.import_sources()
        path = self.project / (self.shader_path("BlendForBloom") + ".meta")
        path.write_bytes(path.read_bytes() + b"  userData: changed\n")
        with self.assertRaisesRegex(storage.BuildError, "complete meta hash or official source receipt differs"):
            self.open()

    def test_unowned_shader_meta_mutation_during_proof_cannot_be_adopted(self):
        self.import_sources()
        path = self.project / (self.shader_path("BlendForBloom") + ".meta")
        actual = shaders._entry
        def mutate_after_read(name, spec, metadata):
            entry = actual(name, spec, metadata)
            if name == "BlendForBloom": path.write_bytes(metadata + b"  userData: concurrent change\n")
            return entry
        with patch.object(shaders, "_entry", mutate_after_read):
            with self.assertRaisesRegex(storage.BuildError, "provenance changed during its bounded read"):
                self.open()
        self.assertEqual(json.loads(self.journal.read_text())["steps"], self.value["steps"])

    def test_recorded_meta_contract_is_still_checked_under_its_real_latest_owner(self):
        path = self.shader_path("BlendForBloom") + ".meta"
        value = copy.deepcopy(self.value)
        value["steps"][25]["outputs"].append(storage.record_file(self.project / path, path))
        storage.write_json(self.journal, value)
        self.import_sources()
        second = self.open(); second.close(); self.assert_retained()

    def test_three_exact_upgrades_retain_all_owners_and_warm_restart_reads_no_payload(self):
        self.import_sources()
        second = self.open(); second.close(); self.assert_retained()
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(
                resume, "digest", side_effect=AssertionError("Warm upgraded files must retain their byte witnesses")):
            third = self.open(); third.close()
        self.assert_retained()

    def test_unchanged_originals_do_not_enter_upgrade_reader(self):
        with patch.object(resume.Preparation, "_accept_post_effect_import", side_effect=AssertionError("No changed row")):
            second = self.open(); second.close()
        self.assert_retained()

    def test_partial_native_import_adopts_only_the_completed_upgrade(self):
        name = "BlendForBloom"
        self.put(self.shader_path(name), self.upgraded[name])
        second = self.open(); second.close(); self.assert_retained()
        for other in ("BrightPassFilter2", "BlurAndFlares"):
            self.assertEqual((self.project / self.shader_path(other)).read_bytes(), self.original[other])

    def test_unknown_shader_edit_is_rejected_without_rewriting_source(self):
        self.import_sources()
        path = self.put(self.shader_path("BlendForBloom"), b"unreviewed arbitrary source")
        with self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed in case-paths"):
            self.open()
        self.assertEqual(path.read_bytes(), b"unreviewed arbitrary source")
        self.assertEqual(json.loads(self.journal.read_text())["steps"], self.value["steps"])

    def test_another_shader_cannot_use_the_post_effect_upgrade_exception(self):
        relative = "Assets/Shader/Unrelated.shader"
        path = self.put(relative, b"owned other shader")
        value = json.loads(self.journal.read_text())
        value["steps"][24]["outputs"].append(storage.record_file(path, relative))
        storage.write_json(self.journal, value); path.write_bytes(self.upgraded["BlendForBloom"])
        with self.assertRaisesRegex(storage.BuildError, "Unrelated.shader"):
            self.open()

    def test_changed_meta_guid_or_source_receipt_is_rejected(self):
        for kind in ("meta", "receipt"):
            with self.subTest(kind=kind):
                self.import_sources()
                relative = self.shader_path("BlendForBloom") + ".meta" if kind == "meta" else shaders.RECEIPT
                path = self.project / relative; before = path.read_bytes()
                path.write_bytes(before + b"changed provenance")
                with self.assertRaises(storage.BuildError): self.open()
                path.write_bytes(before)

    def test_wrong_latest_owner_or_incomplete_schedule_cannot_adopt(self):
        self.import_sources()
        for kind in ("owner", "incomplete"):
            with self.subTest(kind=kind):
                value = copy.deepcopy(self.value)
                if kind == "owner": value["steps"][24]["name"] = "unknown-owner"
                else: value["steps"].pop()
                storage.write_json(self.journal, value)
                with self.assertRaises(storage.BuildError): self.open()

    def test_original_row_hash_cannot_be_self_declared(self):
        self.import_sources()
        value = copy.deepcopy(self.value)
        for row in value["steps"][24]["outputs"]:
            if row["path"] == self.shader_path("BlendForBloom"): row["sha256"] = "f" * 64
        storage.write_json(self.journal, value)
        with self.assertRaisesRegex(storage.BuildError, "Hidden_BlendForBloom.shader"):
            self.open()

    def test_duplicate_receipt_target_and_wrong_guid_are_rejected_even_with_new_receipt_hash(self):
        self.import_sources()
        receipt_path = self.project / shaders.RECEIPT
        original = json.loads(receipt_path.read_text())
        for kind in ("duplicate", "guid"):
            with self.subTest(kind=kind):
                receipt = copy.deepcopy(original)
                if kind == "duplicate": receipt["shaders"].append(copy.deepcopy(receipt["shaders"][0]))
                else: receipt["shaders"][0]["guid"] = "f" * 32
                receipt_path.write_text(json.dumps(receipt))
                value = copy.deepcopy(self.value)
                for row in value["steps"][1]["outputs"]:
                    if row["path"] == shaders.RECEIPT:
                        row.update(storage.record_file(receipt_path, shaders.RECEIPT))
                storage.write_json(self.journal, value)
                with self.assertRaisesRegex(storage.BuildError, "Hidden_BlendForBloom.shader"):
                    self.open()

    def test_linked_shader_does_not_acquire_editor_upgrade_ownership(self):
        self.import_sources()
        path = self.project / self.shader_path("BlendForBloom")
        linked = self.output / "external-shader-owner"
        try: os.link(path, linked)
        except OSError: self.skipTest("Filesystem cannot create a hard link")
        with self.assertRaisesRegex(storage.BuildError, "Hidden_BlendForBloom.shader"):
            self.open()
        self.assertEqual(linked.read_bytes(), self.upgraded["BlendForBloom"])

    def test_cut_before_or_after_adoption_publication_retains_work(self):
        self.seed_script_orders()
        self.import_sources()
        self.put(self.script_asset, self.script_imported)
        actual = resume.write_json
        for after in (False, True):
            with self.subTest(after=after):
                storage.write_json(self.journal, copy.deepcopy(self.value))
                def killed(path, value):
                    adopted = any(row.get("sha256") == shaders.SHADERS["BlendForBloom"]["importUpgradeSha256"]
                                  for step in value.get("steps", []) for row in step["outputs"])
                    if Path(path) == self.journal and adopted:
                        if after: actual(path, value)
                        raise RuntimeError("fixture adoption cut")
                    return actual(path, value)
                with patch.object(resume, "write_json", killed):
                    with self.assertRaisesRegex(RuntimeError, "fixture adoption cut"): self.open()
                with patch.dict(storage._invocation_file_proofs, clear=True):
                    if after:
                        with patch.object(resume, "digest", side_effect=AssertionError("Published adoption must retain byte witnesses")):
                            resumed = self.open(); resumed.close()
                    else:
                        resumed = self.open(); resumed.close()
                self.assert_retained()


if __name__ == "__main__": unittest.main()
