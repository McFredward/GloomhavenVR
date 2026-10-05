"""Focused controls for truthful mixed-source build provenance, without Unity."""

import copy
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import build_provenance
from storage import BuildError, digest, record_file, value_hash


class BuildProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest provenance & 100% ")
        self.root = Path(self.temp.name)
        self.project, self.source = self.root / "project", self.root / "frozen-source"
        self.driver = self.root / "live/tools/quest-builder"
        self.editor = self.project / "Assets/Quest/Editor"
        self.frozen_editor = self.source / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
        self.frozen_driver = self.source / "tools/quest-builder"
        for folder in (self.editor, self.frozen_editor, self.driver, self.frozen_driver):
            folder.mkdir(parents=True)
        for relative, payload in (("builder.py", b"# original build driver\n"),
                                  ("storage.py", b"# dependency\n")):
            self.write(self.driver / relative, payload)
            self.write(self.frozen_driver / relative, payload)
        for relative, payload in (("QuestBuild.cs", b"// original Editor entry\n"),
                                  ("Nested/QuestOriginalScriptOrders.cs", b"// original script order consumer\n")):
            self.write(self.editor / relative, payload)
            self.write(self.frozen_editor / relative, payload)
        self.inputs = {"inputKey": "a" * 64, "mod": {"commit": "b" * 40, "modBuild": 623},
                       "target": "game", "profile": {"persona": "PRIVATE ACCOUNT", "steamId": "PRIVATE ID"}}
        self.tools = {"unityVersion": "2021.3.5f1", "buildDriverSha256": digest(self.driver / "builder.py"),
                      "key": "c" * 64, "editorSha256": "d" * 64, "javaSha256": "e" * 64,
                      "apksignerSha256": "f" * 64, "ndkPropertiesSha256": "1" * 64,
                      "buildToolsVersion": "30.0.2", "editor": str(self.root / "private/Unity"),
                      "keystorePassword": "PRIVATE SIGNING SECRET", "JAVA_HOME": "PRIVATE ENV"}

    def tearDown(self):
        self.temp.cleanup()

    @staticmethod
    def write(path, payload):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)

    def capture(self, **changes):
        arguments = dict(inputs=self.inputs, project=self.project, source=self.source,
                         driver_dir=self.driver, toolchain=self.tools)
        arguments.update(changes)
        return build_provenance.capture(**arguments)

    def compute_receipt(self, *, changed=2):
        generator = self.driver.parent / "quest-compute/references.py"
        self.write(generator, b"# recognized exact class72 pointer generator\n")
        targets = []
        for i in range(13):
            name = "Assets/QuestOriginalCampaign/Compute/Native" + str(i) + ".compute"
            path = self.project / name
            guid = format(i + 1, "032x")
            self.write(path, b"// immutable native compute instructions\n")
            self.write(Path(str(path) + ".meta"), ("guid: " + guid + "\nComputeShaderImporter:\n").encode())
            targets.append({"assetPath": name, "guid": guid, "fileId": 7200000, "classId": 72, "type": 3,
                            "originalCollection": "CAB-fixture", "originalPathId": i + 1,
                            "sourceSha256": digest(path), "metaSha256": digest(Path(str(path) + ".meta"))})
        owner = self.project / "Assets/Resources/PostProcessResources.asset"
        self.write(owner, b"%YAML 1.1\n# controlled applied owner bytes\n")
        references = [{"guid": targets[i]["guid"], "fileId": 7200000,
                       "beforeType": 2 if i < changed else 3, "type": 3, "typeTokenOffset": 8 + i} for i in range(2)]
        receipt = {"schema": 1, "scope": "native-class72-ComputeShaderImporter-PPtr-types",
                   "targetCount": 13, "ownerCount": 1, "changedReferenceCount": changed,
                   "originalIdentityManifestSha256": "4" * 64, "generatorSha256": digest(generator),
                   "unchangedComputeSourcesAndMetas": True, "unchangedOtherOwnerBytes": True,
                   "applied": True, "unityImportVerified": False, "hardwareVerified": False,
                   "targets": targets, "owners": [{"assetPath": owner.relative_to(self.project).as_posix(),
                       "beforeSha256": "5" * 64 if changed else digest(owner), "sha256": digest(owner),
                       "changedReferenceCount": changed, "references": references, "unchangedOtherOwnerBytes": True}]}
        self.store_compute_receipt(receipt)
        return receipt

    def store_compute_receipt(self, receipt):
        path = self.project / "QuestCampaignEvidence/compute-reference-types.json"
        self.write(path, (json.dumps(receipt, sort_keys=True) + "\n").encode())

    def shader_manifest(self):
        manifest = {"schema": 1, "scope": "campaign-compiler", "requiredShaderCount": 1,
                    "requiredMaterialCount": 1, "requiredOriginalNativeAliasCount": 1, "requiredSyntheticAliasCount": 0,
                    "materials": [{"assetPath": "Assets/Original/Original.mat"}],
                    "shaders": [{"assetPath": "Assets/Original/Original.shader", "guid": "6" * 32,
                        "sourceSha256": "7" * 64, "originalName": "PRIVATE INTERNAL CONTENT",
                        "variants": [{"subshader": 0, "pass": 0, "hardwareTier": 0, "passType": "Normal", "keywords": []}]}]}
        self.store_shader_manifest(manifest)
        return manifest

    def store_shader_manifest(self, manifest):
        self.write(self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json",
                   (json.dumps(manifest, sort_keys=True) + "\n").encode())

    def test_unchanged_sources_are_exact_sorted_portable_records(self):
        result = self.capture()
        self.assertEqual(result["schema"], 1)
        self.assertEqual(result["runtime"], {"sourceCommit": "b" * 40, "modBuild": 623, "inputKey": "a" * 64})
        self.assertEqual([row["path"] for row in result["buildDriverModules"]],
                         ["tools/quest-builder/builder.py", "tools/quest-builder/storage.py"])
        self.assertEqual([row["path"] for row in result["stagedEditorSources"]],
                         ["Assets/Quest/Editor/Nested/QuestOriginalScriptOrders.cs", "Assets/Quest/Editor/QuestBuild.cs"])
        for row in result["buildDriverModules"] + result["stagedEditorSources"]:
            self.assertEqual(row["sha256"], row["snapshotSha256"])
            self.assertFalse(row["changedFromSnapshot"])
            self.assertIsInstance(row["size"], int)
            self.assertFalse(Path(row["path"]).is_absolute())
            self.assertTrue((self.source / row["snapshotPath"]).is_file())
        self.assertEqual(result, self.capture())

    def test_actual_editor_override_keeps_the_frozen_runtime_identity(self):
        before = self.capture()
        override = self.editor / "Nested/QuestOriginalScriptOrders.cs"
        self.write(override, b"// corrected full Campaign exclusions\n")
        after = self.capture()
        self.assertEqual(after["runtime"], before["runtime"])
        self.assertNotEqual(value_hash(before), value_hash(after))
        changed = [row for row in after["stagedEditorSources"] if row["changedFromSnapshot"]]
        self.assertEqual(len(changed), 1)
        self.assertEqual(changed[0]["path"], "Assets/Quest/Editor/Nested/QuestOriginalScriptOrders.cs")
        self.assertEqual(changed[0]["sha256"], digest(override))
        self.assertEqual(changed[0]["snapshotSha256"], digest(self.frozen_editor / "Nested/QuestOriginalScriptOrders.cs"))

    def test_actual_driver_dependency_change_invalidates_derivative_identity(self):
        before = self.capture()
        self.write(self.driver / "storage.py", b"# corrected dependency\n")
        after = self.capture()
        self.assertNotEqual(value_hash(before), value_hash(after))
        self.assertEqual(after["runtime"], before["runtime"])
        self.assertFalse(after["buildDriverModules"][0]["changedFromSnapshot"])
        self.assertTrue(after["buildDriverModules"][1]["changedFromSnapshot"])

    def test_new_and_removed_sources_change_identity_without_fabricated_baselines(self):
        before = self.capture()
        self.write(self.driver / "next_module.py", b"# future current builder module\n")
        self.write(self.editor / "New/QuestNextValidation.cs", b"// derived new importer validator\n")
        after = self.capture()
        self.assertNotEqual(value_hash(before), value_hash(after))
        for key in ("buildDriverModules", "stagedEditorSources"):
            new = [row for row in after[key] if row["snapshotSha256"] is None]
            self.assertEqual(len(new), 1)
            self.assertTrue(new[0]["changedFromSnapshot"])
        (self.driver / "storage.py").unlink()
        self.assertNotEqual(value_hash(after), value_hash(self.capture()))

    def test_different_runtime_identity_is_preserved_and_changes_derivative_key(self):
        before = self.capture()
        for field, replacement in (("sourceCommit", "2" * 40), ("modBuild", 624), ("inputKey", "3" * 64)):
            with self.subTest(field=field):
                inputs = copy.deepcopy(self.inputs)
                if field == "inputKey": inputs[field] = replacement
                else: inputs["mod"]["commit" if field == "sourceCommit" else field] = replacement
                after = self.capture(inputs=inputs)
                self.assertEqual(after["runtime"][field], replacement)
                self.assertNotEqual(value_hash(before), value_hash(after))

    def test_no_absolute_paths_profile_environment_or_signing_fields_leak(self):
        result = self.capture()
        serialized = json.dumps(result)
        self.assertNotIn(str(self.root), serialized)
        self.assertNotIn("PRIVATE", serialized)
        self.assertNotIn("profile", serialized)
        self.assertNotIn("JAVA_HOME", serialized)
        self.assertEqual(set(result["toolchain"]), set(build_provenance._TOOL_FIELDS))
        # An unrelated sensitive field cannot invalidate this build input.
        changed = copy.deepcopy(self.tools)
        changed["keystorePassword"] = "ANOTHER PRIVATE SIGNING SECRET"
        self.assertEqual(result, self.capture(toolchain=changed))

    def test_optional_public_tool_fingerprint_changes_derivative_identity(self):
        before = self.capture()
        for field, value in (("editorSha256", "0" * 64), ("javaSha256", "0" * 64),
                             ("apksignerSha256", "0" * 64), ("ndkPropertiesSha256", "0" * 64),
                             ("buildToolsVersion", "31.0.0")):
            with self.subTest(field=field):
                tools = copy.deepcopy(self.tools); tools[field] = value
                self.assertNotEqual(value_hash(before), value_hash(self.capture(toolchain=tools)))
        minimal = {key: self.tools[key] for key in ("unityVersion", "buildDriverSha256", "key")}
        self.assertEqual(set(self.capture(toolchain=minimal)["toolchain"]), set(minimal))

    def test_unknown_missing_or_empty_roots_fail(self):
        for field in ("project", "source", "driver_dir"):
            with self.subTest(root=field):
                with self.assertRaises(BuildError): self.capture(**{field: self.root / "missing"})
                not_directory = self.root / (field + ".txt"); not_directory.write_text("regular file")
                with self.assertRaises(BuildError): self.capture(**{field: not_directory})
        for folder in (self.editor, self.frozen_editor, self.frozen_driver):
            with self.subTest(required=folder.name):
                moved = folder.with_name(folder.name + "-retained"); folder.rename(moved)
                try:
                    with self.assertRaises(BuildError): self.capture()
                finally: moved.rename(folder)
        (self.driver / "builder.py").unlink()
        with self.assertRaises(BuildError): self.capture()

    def test_empty_editor_is_not_a_valid_build_source(self):
        shutil.rmtree(self.editor); self.editor.mkdir()
        with self.assertRaises(BuildError): self.capture()

    def test_root_and_nested_source_symlinks_are_rejected(self):
        for path in (self.project, self.source, self.driver, self.editor / "Nested",
                     self.driver / "storage.py", self.frozen_driver / "storage.py",
                     self.frozen_editor / "Nested"):
            with self.subTest(path=path.name):
                moved = path.with_name(path.name + "-retained"); path.rename(moved)
                path.symlink_to(moved, target_is_directory=moved.is_dir())
                try:
                    with self.assertRaises(BuildError): self.capture()
                finally: path.unlink(); moved.rename(path)
        broken = self.editor / "Broken"; broken.symlink_to(self.root / "absent", target_is_directory=True)
        with self.assertRaises(BuildError): self.capture()

    def test_wrong_or_mutated_driver_hash_stops_capture(self):
        bad = copy.deepcopy(self.tools); bad["buildDriverSha256"] = "0" * 64
        with self.assertRaisesRegex(BuildError, "recorded toolchain"): self.capture(toolchain=bad)
        self.write(self.driver / "builder.py", b"# changed actual live driver\n")
        with self.assertRaisesRegex(BuildError, "recorded toolchain"): self.capture()

    def test_invalid_runtime_and_tool_fingerprints_fail_without_echoing_secrets(self):
        for mutation in (lambda x: x.pop("inputKey"), lambda x: x.update(inputKey="PRIVATE PATH"),
                         lambda x: x["mod"].update(commit="current HEAD"), lambda x: x["mod"].update(modBuild=True),
                         lambda x: x["mod"].update(modBuild=0)):
            bad = copy.deepcopy(self.inputs); mutation(bad)
            with self.assertRaises(BuildError) as error: self.capture(inputs=bad)
            self.assertNotIn("PRIVATE", str(error.exception))
        for field in build_provenance._TOOL_FIELDS:
            bad = copy.deepcopy(self.tools); bad[field] = "PRIVATE SECRET OR PATH"
            with self.assertRaises(BuildError) as error: self.capture(toolchain=bad)
            self.assertNotIn("PRIVATE", str(error.exception))
        for field in ("unityVersion", "buildDriverSha256", "key"):
            bad = copy.deepcopy(self.tools); bad.pop(field)
            with self.assertRaises(BuildError): self.capture(toolchain=bad)

    def test_source_bytes_modified_after_first_hash_are_rejected(self):
        target = self.driver / "storage.py"
        changed = False
        def concurrent_change(path, relative):
            nonlocal changed
            row = record_file(path, relative)
            if path == target and not changed:
                changed = True
                self.write(target, b"# concurrent dependency edit\n")
            return row
        with patch.object(build_provenance, "record_file", concurrent_change):
            with self.assertRaisesRegex(BuildError, "bytes changed"): self.capture()

    def test_new_file_appearing_during_capture_is_rejected(self):
        original = record_file
        changed = False
        def concurrent_add(path, relative):
            nonlocal changed
            row = original(path, relative)
            if not changed:
                changed = True
                self.write(self.editor / "Unexpected.cs", b"// concurrent new source\n")
            return row
        with patch.object(build_provenance, "record_file", concurrent_add):
            with self.assertRaisesRegex(BuildError, "selection changed"): self.capture()

    def test_applied_compute_receipt_binds_exact_actual_owner_target_and_generator_bytes(self):
        before = self.capture()
        receipt = self.compute_receipt()
        after = self.capture()
        self.assertEqual(after["runtime"], before["runtime"])
        self.assertNotEqual(value_hash(before), value_hash(after))
        compute = after["campaignComputeReferenceRepair"]
        self.assertEqual(compute["path"], "QuestCampaignEvidence/compute-reference-types.json")
        self.assertEqual(compute["sha256"], digest(self.project / compute["path"]))
        self.assertEqual(compute["generator"]["sha256"], receipt["generatorSha256"])
        self.assertEqual(len(compute["targets"]), 13)
        self.assertEqual(compute["owners"][0]["sha256"], receipt["owners"][0]["sha256"])
        self.assertEqual(compute["owners"][0]["beforeSha256"], receipt["owners"][0]["beforeSha256"])
        self.assertEqual(compute["changedReferenceCount"], 2)
        self.assertTrue(compute["applied"])
        self.assertFalse(compute["unityImportVerified"])
        self.assertFalse(compute["hardwareVerified"])
        self.assertEqual(after, self.capture())

    def test_idempotent_applied_compute_receipt_keeps_zero_changes_honest(self):
        receipt = self.compute_receipt(changed=0)
        result = self.capture()["campaignComputeReferenceRepair"]
        self.assertEqual(result["changedReferenceCount"], 0)
        self.assertEqual(result["owners"][0]["sha256"], receipt["owners"][0]["beforeSha256"])
        self.assertTrue(result["applied"])

    def test_compute_receipt_unknown_scope_dry_run_or_claimed_readiness_is_rejected(self):
        receipt = self.compute_receipt()
        for field, value in (("schema", 2), ("scope", "generic arbitrary evidence"), ("applied", False),
                             ("targetCount", 12), ("ownerCount", 2), ("changedReferenceCount", 1),
                             ("unchangedComputeSourcesAndMetas", False), ("unchangedOtherOwnerBytes", False),
                             ("unityImportVerified", True), ("hardwareVerified", True),
                             ("generatorSha256", "0" * 64), ("originalIdentityManifestSha256", "PRIVATE PATH")):
            with self.subTest(field=field):
                bad = copy.deepcopy(receipt); bad[field] = value; self.store_compute_receipt(bad)
                with self.assertRaises(BuildError): self.capture()

    def test_compute_target_and_owner_contract_defect_controls(self):
        receipt = self.compute_receipt()
        controls = (
            lambda x: x["targets"].pop(),
            lambda x: x["targets"][0].update(assetPath="../../PRIVATE FILE"),
            lambda x: x["targets"][0].update(sourceSha256="0" * 64),
            lambda x: x["targets"][0].update(metaSha256="0" * 64),
            lambda x: x["targets"][0].update(classId=48),
            lambda x: x["targets"][0].update(fileId=7200001),
            lambda x: x["targets"][0].update(type=2),
            lambda x: x["targets"][1].update(guid=x["targets"][0]["guid"]),
            lambda x: x["targets"][1].update(assetPath=x["targets"][0]["assetPath"]),
            lambda x: x["owners"][0].update(assetPath="Assets/../PRIVATE FILE"),
            lambda x: x["owners"][0].update(sha256="0" * 64),
            lambda x: x["owners"][0].update(beforeSha256="PRIVATE PATH"),
            lambda x: x["owners"][0].update(changedReferenceCount=1),
            lambda x: x["owners"][0].update(unchangedOtherOwnerBytes=False),
            lambda x: x["owners"][0].update(references=[]),
            lambda x: x["owners"][0]["references"][0].update(guid="0" * 32),
            lambda x: x["owners"][0]["references"][0].update(fileId=7200001),
            lambda x: x["owners"][0]["references"][0].update(type=2),
            lambda x: x["owners"][0]["references"][0].update(beforeType=4),
            lambda x: x["owners"][0]["references"][0].update(typeTokenOffset=-1),
        )
        for index, mutate in enumerate(controls):
            with self.subTest(defect=index):
                bad = copy.deepcopy(receipt); mutate(bad); self.store_compute_receipt(bad)
                with self.assertRaises(BuildError): self.capture()

    def test_compute_missing_changed_or_linked_actual_files_are_rejected(self):
        receipt = self.compute_receipt()
        paths = [self.project / receipt["owners"][0]["assetPath"],
                 self.project / receipt["targets"][0]["assetPath"],
                 self.project / (receipt["targets"][0]["assetPath"] + ".meta"),
                 self.driver.parent / "quest-compute/references.py",
                 self.project / "QuestCampaignEvidence/compute-reference-types.json"]
        for path in paths:
            with self.subTest(path=path.name):
                moved = path.with_name(path.name + "-retained"); path.rename(moved)
                if path.name != "compute-reference-types.json":
                    with self.assertRaises(BuildError): self.capture()
                    self.write(path, b"changed receipt input")
                    with self.assertRaises(BuildError): self.capture()
                    path.unlink()
                path.symlink_to(moved)
                try:
                    with self.assertRaises(BuildError): self.capture()
                finally: path.unlink(); moved.rename(path)

    def test_compute_evidence_whitelist_cannot_export_extra_account_fields(self):
        receipt = self.compute_receipt()
        receipt["profile"] = "PRIVATE ACCOUNT"
        receipt["targets"][0]["originalCollection"] = "PRIVATE PATH"
        receipt["owners"][0]["secret"] = "PRIVATE SIGNING KEY"
        self.store_compute_receipt(receipt)
        output = json.dumps(self.capture())
        self.assertNotIn("PRIVATE", output)
        self.assertNotIn(str(self.root), output)

    def test_compute_owner_race_is_rejected_before_result_publication(self):
        receipt = self.compute_receipt()
        owner = self.project / receipt["owners"][0]["assetPath"]
        changed = False
        def concurrent_change(path, relative):
            nonlocal changed
            row = record_file(path, relative)
            if path == owner and not changed:
                changed = True
                self.write(owner, b"modified after owner receipt validation")
            return row
        with patch.object(build_provenance, "record_file", concurrent_change):
            with self.assertRaisesRegex(BuildError, "bytes changed"): self.capture()

    def test_campaign_shader_pass_metadata_changes_derivative_key_without_exporting_contents(self):
        self.assertNotIn("campaignShaderManifest", self.capture())
        manifest = self.shader_manifest()
        before = self.capture()
        self.assertEqual(set(before["campaignShaderManifest"]), {"path", "sha256", "size"})
        self.assertEqual(before["campaignShaderManifest"]["path"], "Assets/QuestOriginalCampaign/campaign-shaders.json")
        self.assertEqual(before["campaignShaderManifest"]["sha256"], digest(self.project / before["campaignShaderManifest"]["path"]))
        self.assertNotIn("PRIVATE INTERNAL CONTENT", json.dumps(before))
        self.assertNotIn("passType", json.dumps(before))
        self.assertNotIn(str(self.root), json.dumps(before))
        manifest["shaders"][0]["variants"][0]["passType"] = "Vertex"
        self.store_shader_manifest(manifest)
        after = self.capture()
        self.assertEqual(before["runtime"], after["runtime"])
        self.assertNotEqual(value_hash(before), value_hash(after))
        self.assertEqual(after, self.capture())

    def test_campaign_shader_manifest_scope_shape_and_counts_are_checked(self):
        manifest = self.shader_manifest()
        controls = (
            lambda x: x.update(schema=2), lambda x: x.update(schema=True),
            lambda x: x.update(scope="unrelated private dump"), lambda x: x.update(shaders=[]),
            lambda x: x.update(materials={}), lambda x: x.update(requiredShaderCount=2),
            lambda x: x.update(requiredMaterialCount=0), lambda x: x.update(requiredOriginalNativeAliasCount=2),
            lambda x: x.update(requiredSyntheticAliasCount=-1), lambda x: x.update(requiredShaderCount=True),
            lambda x: x["shaders"][0].update(assetPath="Assets/../Original.shader"),
            lambda x: x["shaders"][0].update(assetPath="PRIVATE/Original.shader"),
            lambda x: x["shaders"][0].update(guid="not an original GUID"),
            lambda x: x["shaders"][0].update(sourceSha256="not a source hash"),
            lambda x: x["shaders"][0].update(variants=[]),
            lambda x: x["shaders"][0]["variants"][0].update(passType=""),
            lambda x: x["shaders"][0]["variants"][0].update({"pass": -1}),
            lambda x: x["shaders"][0]["variants"][0].update(subshader=True),
            lambda x: x["shaders"][0]["variants"][0].update(hardwareTier="0"),
            lambda x: x["shaders"][0]["variants"][0].update(keywords="PRIVATE CONTENT"),
            lambda x: x["shaders"][0]["variants"][0].update(keywords=[None]),
        )
        for index, mutate in enumerate(controls):
            with self.subTest(defect=index):
                bad = copy.deepcopy(manifest); mutate(bad); self.store_shader_manifest(bad)
                with self.assertRaises(BuildError): self.capture()

    def test_campaign_shader_manifest_duplicate_native_identities_are_rejected(self):
        manifest = self.shader_manifest()
        manifest["requiredShaderCount"] = 2; manifest["requiredOriginalNativeAliasCount"] = 2
        manifest["shaders"].append(copy.deepcopy(manifest["shaders"][0]))
        for duplicate in ("guid", "assetPath"):
            with self.subTest(identity=duplicate):
                bad = copy.deepcopy(manifest)
                if duplicate == "guid": bad["shaders"][1]["assetPath"] = "Assets/Original/Second.shader"
                else: bad["shaders"][1]["guid"] = "8" * 32
                self.store_shader_manifest(bad)
                with self.assertRaises(BuildError): self.capture()

    def test_campaign_shader_manifest_file_and_parent_symlinks_are_rejected(self):
        self.shader_manifest()
        for path in (self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json",
                     self.project / "Assets/QuestOriginalCampaign"):
            with self.subTest(path=path.name):
                moved = path.with_name(path.name + "-retained"); path.rename(moved)
                path.symlink_to(moved, target_is_directory=moved.is_dir())
                try:
                    with self.assertRaises(BuildError): self.capture()
                finally: path.unlink(); moved.rename(path)

    def test_campaign_shader_manifest_changed_during_capture_is_rejected(self):
        manifest = self.shader_manifest()
        target = self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json"
        changed = False
        def concurrent_change(path, relative):
            nonlocal changed
            row = record_file(path, relative)
            if path == target and not changed:
                changed = True
                manifest["shaders"][0]["variants"][0]["passType"] = "Vertex"
                self.store_shader_manifest(manifest)
            return row
        with patch.object(build_provenance, "record_file", concurrent_change):
            with self.assertRaisesRegex(BuildError, "bytes changed"): self.capture()

    def test_campaign_shader_manifest_removed_after_first_hash_is_rejected(self):
        self.shader_manifest()
        target = self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json"
        def concurrent_remove(path, relative):
            row = record_file(path, relative)
            if path == target: target.unlink()
            return row
        with patch.object(build_provenance, "record_file", concurrent_remove):
            with self.assertRaises(BuildError): self.capture()


if __name__ == "__main__":
    unittest.main()
