"""Focused controls for truthful mixed-source build provenance, without Unity."""

import copy
import hashlib
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

    def shader_order_receipt(self):
        """Small identity ledger, all 90 witnessed input programs; no Unity claim."""
        scope = "native-fragment-stereo-input-order"
        shader_guid = "1baec85ef43ddac49b802c75062235f0"
        shader = "Assets/Original/NativeFoliage.shader"
        material = "Assets/Original/NativeFoliage.mat"
        for name, guid in ((shader, shader_guid), (material, "8" * 32)):
            self.write(self.project / name, b"// immutable original identity fixture\n")
            self.write(self.project / (name + ".meta"), ("guid: " + guid + "\n").encode())
        produce = self.driver.parent / "quest-shaders/produce.py"
        helper = self.driver.parent / "quest-shaders/stereo_repair.py"
        self.write(produce, b"# current actual produce.py fixture\n")
        self.write(helper, b"# new exact repair helper fixture\n")
        self.write(self.source / "tools/quest-shaders/produce.py", b"# frozen prior produce.py fixture\n")
        rows, native, variants = [], [], []
        for i in range(90):
            name = "Assets/Original/Programs/Native" + str(i) + ".hlsl"
            before = b"struct SPIRV_Cross_Input\n{\n    float2 uv : TEXCOORD0;\n    bool face : SV_IsFrontFace;\n    UNITY_VERTEX_OUTPUT_STEREO\n};\n"
            after = b"struct SPIRV_Cross_Input\n{\n    float2 uv : TEXCOORD0;\n    UNITY_VERTEX_OUTPUT_STEREO\n    bool face : SV_IsFrontFace;\n};\n"
            self.write(self.project / name, after)
            self.write(self.project / (name + ".meta"), ("guid: " + format(i + 100, "032x") + "\n").encode())
            dxbc = format(i + 1, "064x"); interface = format(i + 500, "064x")
            row = {"assetPath": name, "originalDxbcSha256": dxbc, "originalInterfaceSha256": interface,
                   "beforeSha256": hashlib.sha256(before).hexdigest(), "afterSha256": digest(self.project / name),
                   "metaSha256": digest(self.project / (name + ".meta")),
                   "restBytesSha256": hashlib.sha256(after.replace(b"    UNITY_VERTEX_OUTPUT_STEREO\n", b"")).hexdigest(),
                   "originalDeclarationsAndMathPreserved": True}
            rows.append(row)
            native.append({"assetPath": name, "sourceSha256": row["afterSha256"],
                "originalDxbcSha256": dxbc, "originalInterfaceSha256": interface,
                "originalInputSignature": [{"systemValue": 9, "semantic": "SV_IsFrontFace", "semanticIndex": 0}]})
            variants.append({"subshader": 0, "pass": 0, "hardwareTier": 0, "passType": "Normal",
                "keywords": ["NATIVE_" + str(i)], "fragmentOriginalDxbcSha256": dxbc})
        untouched = "Assets/Original/Programs/Unchanged.hlsl"
        self.write(self.project / untouched, b"// exact unaffected vertex program\n")
        self.write(self.project / (untouched + ".meta"), b"guid: 99999999999999999999999999999999\n")
        native.append({"assetPath": untouched, "sourceSha256": digest(self.project / untouched),
            "originalDxbcSha256": "9" * 64, "originalInterfaceSha256": "a" * 64,
            "originalInputSignature": [{"systemValue": 1, "semantic": "SV_Position", "semanticIndex": 0}]})
        manifest = {"schema": 1, "scope": "campaign-compiler", "graphicsApi": "Vulkan",
            "requiredShaderCount": 1, "requiredMaterialCount": 1,
            "requiredOriginalNativeAliasCount": 90, "requiredSyntheticAliasCount": 0,
            "shaders": [{"assetPath": shader, "guid": shader_guid, "sourceSha256": digest(self.project / shader),
                "originalName": "PRIVATE SHADER CONTENT", "variants": variants}],
            "materials": [{"assetPath": material, "guid": "8" * 32}], "programs": native,
            "fragmentStereoInputOrderRepair": {"path": "QuestCampaignEvidence/fragment-stereo-input-order.json",
                "scope": scope, "programCount": 90}}
        self.store_shader_manifest(manifest)
        def identity(name, guid=None):
            value = {"assetPath": name, "sha256": digest(self.project / name), "metaSha256": digest(self.project / (name + ".meta"))}
            if guid is not None: value["guid"] = guid
            return value
        ledger = {"schema": 1, "scope": scope + "-identities", "originalAliasCount": 90,
            "shaders": [identity(shader, shader_guid)], "materials": [identity(material, "8" * 32)],
            "programs": [identity(untouched)]}
        ledger_path = "QuestCampaignEvidence/fragment-stereo-input-order-identities.json"
        input_path = "QuestCampaignEvidence/fragment-stereo-input-order-input.json"
        witness_path = "QuestCampaignEvidence/fragment-stereo-input-order-witness.json"
        driver_path = "QuestCampaignEvidence/fragment-stereo-input-order-driver.json"
        self.write(self.project / ledger_path, json.dumps(ledger).encode())
        self.write(self.project / input_path, json.dumps({"failures": list(range(26)), "nativeControls": list(range(7)),
            "rewrites": copy.deepcopy(rows), "privateEnvironment": "PRIVATE COMPILER INPUT"}).encode())
        witness = {"schema": 1, "unityVersion": "2021.3.5f1", "inputSha256": digest(self.project / input_path),
            "baselineRejectedCount": 26, "positiveCompilerCount": 33, "monoExactStageCount": 7,
            "actualNativeBundleBuilt": True, "originalDeclarationsAndMathPreserved": True,
            "originalShaderAndMetaPreserved": True, "headsetPictureVerified": False, "originalPixelParityVerified": False}
        self.write(self.project / witness_path, json.dumps(witness).encode())
        driver = {"schema": 1, "graphicsApi": "Vulkan", "sourceWitnessSha256": digest(self.project / witness_path),
            "actualNativePipelineAliasCount": 33, "actualDistinctNativePipelineCount": 33,
            "missingNativeEntryRejected": True, "headsetPictureVerified": False, "originalWindowsPixelParityVerified": False}
        self.write(self.project / driver_path, json.dumps(driver).encode())
        receipt = {"schema": 1, "scope": scope, "graphicsApi": "Vulkan", "applied": True,
            "generator": {"path": "tools/quest-shaders/produce.py", "sha256": digest(produce)},
            "repairHelper": {"path": "tools/quest-shaders/stereo_repair.py", "sha256": digest(helper)},
            "manifest": {"path": "Assets/QuestOriginalCampaign/campaign-shaders.json", "beforeSha256": "b" * 64,
                "afterSha256": digest(self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json")},
            "programCount": 90, "changedProgramCount": 90, "programs": rows,
            "identities": {"path": ledger_path, "sha256": digest(self.project / ledger_path), "shaderCount": 1,
                "materialCount": 1, "originalAliasCount": 90},
            "nativeCompilerInput": {"path": input_path, "sha256": digest(self.project / input_path)},
            "nativeCompilerWitness": {"path": witness_path, "sha256": digest(self.project / witness_path),
                **{k: witness[k] for k in ("baselineRejectedCount", "positiveCompilerCount", "monoExactStageCount", "actualNativeBundleBuilt")}},
            "nativeDriverWitness": {"path": driver_path, "sha256": digest(self.project / driver_path),
                **{k: driver[k] for k in ("actualNativePipelineAliasCount", "actualDistinctNativePipelineCount", "missingNativeEntryRejected")}},
            "headsetPictureVerified": False, "originalPixelParityVerified": False}
        self.store_shader_order_receipt(receipt)
        return receipt, manifest, ledger

    def store_shader_order_receipt(self, receipt):
        self.write(self.project / "QuestCampaignEvidence/fragment-stereo-input-order.json", json.dumps(receipt).encode())

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

    def test_native_shader_order_repair_binds_actual_generator_snapshot_and_all_unchanged_identities(self):
        receipt, _, _ = self.shader_order_receipt()
        result = self.capture()
        repair = result["campaignFragmentStereoInputOrderRepair"]
        self.assertEqual(repair["programCount"], 90)
        self.assertEqual(repair["changedProgramCount"], 90)
        self.assertEqual(repair["generator"]["sha256"], receipt["generator"]["sha256"])
        self.assertEqual(repair["generator"]["snapshotSha256"], digest(self.source / "tools/quest-shaders/produce.py"))
        self.assertTrue(repair["generator"]["changedFromSnapshot"])
        self.assertIsNone(repair["repairHelper"]["snapshotSha256"])
        self.assertTrue(repair["repairHelper"]["changedFromSnapshot"])
        self.assertEqual(repair["manifest"]["sha256"], result["campaignShaderManifest"]["sha256"])
        self.assertEqual(repair["identities"]["unchangedProgramCount"], 1)
        self.assertEqual(repair["nativeCompilerWitness"]["positiveCompilerCount"], 33)
        self.assertFalse(repair["headsetPictureVerified"])
        self.assertFalse(repair["originalPixelParityVerified"])
        self.assertEqual(result, self.capture())
        output = json.dumps(result)
        self.assertNotIn("PRIVATE", output)
        self.assertNotIn("SPIRV_Cross_Input", output)
        self.assertNotIn("originalInputSignature", output)
        self.assertNotIn(str(self.root), output)

    def test_shader_order_repair_rejects_missing_receipt_marker_and_partial_transaction(self):
        receipt, manifest, _ = self.shader_order_receipt()
        path = self.project / "QuestCampaignEvidence/fragment-stereo-input-order.json"
        path.unlink()
        with self.assertRaisesRegex(BuildError, "receipt is missing"): self.capture()
        self.store_shader_order_receipt(receipt)
        marker = manifest.pop("fragmentStereoInputOrderRepair")
        self.store_shader_manifest(manifest)
        with self.assertRaisesRegex(BuildError, "manifest marker"): self.capture()
        manifest["fragmentStereoInputOrderRepair"] = marker
        self.store_shader_manifest(manifest)
        self.write(self.project / "QuestCampaignEvidence/fragment-stereo-input-order.transaction", b"interrupted")
        with self.assertRaisesRegex(BuildError, "transaction is incomplete"): self.capture()

    def test_shader_order_repair_invalid_scope_counts_readiness_and_identity_fail_closed(self):
        receipt, _, _ = self.shader_order_receipt()
        controls = (
            lambda x: x.update(schema=True), lambda x: x.update(scope="unrelated repair"),
            lambda x: x.update(graphicsApi="OpenGLES3"), lambda x: x.update(applied=False),
            lambda x: x.update(programCount=89), lambda x: x.update(changedProgramCount=True),
            lambda x: x.update(headsetPictureVerified=True), lambda x: x.update(originalPixelParityVerified=True),
            lambda x: x["manifest"].update(afterSha256="0" * 64),
            lambda x: x["generator"].update(sha256="0" * 64),
            lambda x: x["repairHelper"].update(path="tools/quest-shaders/unknown.py"),
            lambda x: x["programs"][0].update(originalDxbcSha256="0" * 64),
            lambda x: x["programs"][0].update(originalInterfaceSha256="0" * 64),
            lambda x: x["programs"][0].update(afterSha256="0" * 64),
            lambda x: x["programs"][0].update(metaSha256="0" * 64),
            lambda x: x["programs"][0].update(restBytesSha256="0" * 64),
            lambda x: x["programs"][0].update(originalDeclarationsAndMathPreserved=False),
            lambda x: x["programs"][1].update(assetPath=x["programs"][0]["assetPath"]),
            lambda x: x["nativeCompilerWitness"].update(positiveCompilerCount=34),
            lambda x: x["nativeDriverWitness"].update(missingNativeEntryRejected=False),
        )
        for i, mutate in enumerate(controls):
            with self.subTest(defect=i):
                bad = copy.deepcopy(receipt); mutate(bad); self.store_shader_order_receipt(bad)
                with self.assertRaises(BuildError): self.capture()

    def test_shader_order_repair_only_recognizes_native_foliage_front_face_programs(self):
        receipt, manifest, _ = self.shader_order_receipt()
        for mutate in (
                lambda x: x["shaders"][0].update(guid="6" * 32),
                lambda x: x["programs"][0]["originalInputSignature"][0].update(systemValue=1),
                lambda x: x["programs"][0]["originalInputSignature"][0].update(semantic="TEXCOORD")):
            bad = copy.deepcopy(manifest); mutate(bad); self.store_shader_manifest(bad)
            changed = copy.deepcopy(receipt)
            changed["manifest"]["afterSha256"] = digest(self.project / "Assets/QuestOriginalCampaign/campaign-shaders.json")
            self.store_shader_order_receipt(changed)
            with self.assertRaises(BuildError): self.capture()

    def test_shader_order_repair_missing_changed_linked_include_meta_and_witness_fail_closed(self):
        receipt, _, _ = self.shader_order_receipt()
        paths = [self.project / receipt["programs"][0]["assetPath"],
            self.project / (receipt["programs"][0]["assetPath"] + ".meta"),
            self.project / "Assets/Original/NativeFoliage.mat",
            self.project / receipt["nativeCompilerInput"]["path"],
            self.project / receipt["nativeCompilerWitness"]["path"],
            self.driver.parent / "quest-shaders/produce.py",
            self.driver.parent / "quest-shaders/stereo_repair.py"]
        for path in paths:
            with self.subTest(path=path.name):
                retained = path.with_name(path.name + ".retained"); path.rename(retained)
                try:
                    with self.assertRaises(BuildError): self.capture()
                    path.write_bytes(b"changed source bytes")
                    with self.assertRaises(BuildError): self.capture()
                    path.unlink(); path.symlink_to(retained)
                    with self.assertRaises(BuildError): self.capture()
                finally:
                    path.unlink(missing_ok=True); retained.rename(path)

    def test_shader_order_repair_native_meta_guid_and_unchanged_ledger_census_are_verified(self):
        receipt, _, ledger = self.shader_order_receipt()
        ledger_path = self.project / receipt["identities"]["path"]
        for mutate in (
                lambda x: x["shaders"][0].update(guid="0" * 32),
                lambda x: x["materials"][0].update(metaSha256="0" * 64),
                lambda x: x["programs"][0].update(sha256="0" * 64),
                lambda x: x["materials"].append(copy.deepcopy(x["materials"][0]))):
            bad = copy.deepcopy(ledger); mutate(bad); ledger_path.write_text(json.dumps(bad))
            changed = copy.deepcopy(receipt); changed["identities"]["sha256"] = digest(ledger_path)
            self.store_shader_order_receipt(changed)
            with self.assertRaises(BuildError): self.capture()

    def test_shader_order_repair_rejects_changed_native_guid_even_with_refreshed_meta_hash(self):
        receipt, _, ledger = self.shader_order_receipt()
        meta = self.project / (ledger["materials"][0]["assetPath"] + ".meta")
        meta.write_text("guid: " + "0" * 32 + "\n")
        ledger["materials"][0]["metaSha256"] = digest(meta)
        ledger_path = self.project / receipt["identities"]["path"]
        ledger_path.write_text(json.dumps(ledger))
        receipt["identities"]["sha256"] = digest(ledger_path)
        self.store_shader_order_receipt(receipt)
        with self.assertRaisesRegex(BuildError, "metadata GUID"): self.capture()

    def test_shader_order_repair_source_race_and_removed_evidence_are_rejected(self):
        receipt, _, _ = self.shader_order_receipt()
        target = self.project / receipt["programs"][0]["assetPath"]
        changed = False
        def concurrent_change(path, relative):
            nonlocal changed
            row = record_file(path, relative)
            if path == target and not changed:
                changed = True; path.write_bytes(path.read_bytes() + b"// raced math\n")
            return row
        with patch.object(build_provenance, "record_file", concurrent_change):
            with self.assertRaises(BuildError): self.capture()

    def test_shader_order_repair_unchanged_material_race_and_disappearing_receipt_fail_closed(self):
        receipt, _, ledger = self.shader_order_receipt()
        for target, delete in ((self.project / ledger["materials"][0]["assetPath"], False),
                               (self.project / "QuestCampaignEvidence/fragment-stereo-input-order.json", True)):
            with self.subTest(path=target.name):
                payload = target.read_bytes(); changed = False
                def concurrent_change(path, relative):
                    nonlocal changed
                    row = record_file(path, relative)
                    if path == target and not changed:
                        changed = True
                        if delete: path.unlink()
                        else: path.write_bytes(payload + b"// concurrent mutation\n")
                    return row
                try:
                    with patch.object(build_provenance, "record_file", concurrent_change):
                        with self.assertRaises(BuildError): self.capture()
                finally: target.write_bytes(payload)

    def test_shader_order_repair_recapture_detects_edits_without_rewriting_frozen_runtime(self):
        receipt, _, _ = self.shader_order_receipt()
        before = self.capture()
        self.write(self.source / "tools/quest-shaders/produce.py", b"# changed snapshot generator\n")
        after = self.capture()
        self.assertEqual(before["runtime"], after["runtime"])
        self.assertNotEqual(value_hash(before), value_hash(after))
        self.write(self.driver.parent / "quest-shaders/produce.py", b"# changed actual generator\n")
        with self.assertRaisesRegex(BuildError, "generator differs"): self.capture()


if __name__ == "__main__":
    unittest.main()
