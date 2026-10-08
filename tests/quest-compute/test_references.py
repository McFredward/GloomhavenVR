"""Fail-closed native ComputeShader importer reference boundary tests."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "tools/quest-compute"
spec = importlib.util.spec_from_file_location("quest_compute_references_test", PACKAGE / "__init__.py", submodule_search_locations=[str(PACKAGE)])
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
from quest_compute_references_test.native import ComputeRecoveryError
from quest_compute_references_test.references import IDENTITIES, RECEIPT, repair

GUID = "1" * 32
OTHER_GUID = "2" * 32
HEADER = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n"


def checksum(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ReferenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.project = Path(self.directory.name)
        self.original = "Assets/Native/Kernel.asset"
        self.path = "Assets/Native/Kernel.compute"
        self.source = self.project / self.path
        self.source.parent.mkdir(parents=True)
        self.source.write_text("#pragma kernel OriginalKernel\n[numthreads(1,1,1)] void OriginalKernel() {}\n")
        self.meta = Path(str(self.source) + ".meta")
        self.meta.write_text("fileFormatVersion: 2\nguid: " + GUID + "\nComputeShaderImporter:\n  externalObjects: {}\n")
        self.manifest = {"shaders": [{"guid": GUID, "classId": 72, "localFileId": 7200000,
            "originalPath": self.original, "assetPath": self.path,
            "sourceSha256": checksum(self.source), "metaSha256": checksum(self.meta)}],
            "pathMap": {self.original: self.path}}
        self.identities = {"schema": 1, "identities": [{"guid": GUID, "path": self.original,
            "objects": [{"classId": 72, "fileId": 7200000, "collection": "cab-original", "pathId": -123}]}]}
        (self.project / IDENTITIES).parent.mkdir(parents=True)
        self.save_identities()
        self.owner = self.project / "Assets/Owner.asset"
        self.owner.write_text(HEADER + "  field: {fileID: 7200000, guid: " + GUID + ", type: 2}\n")
        Path(str(self.owner) + ".meta").write_text("fileFormatVersion: 2\nguid: " + OTHER_GUID + "\nNativeFormatImporter:\n  mainObjectFileID: 11400000\n")
        self.identities["identities"].append({"guid": OTHER_GUID, "path": "Assets/Owner.asset",
            "objects": [{"classId": 114, "fileId": 11400000, "collection": "cab-original", "pathId": 456}]})
        self.save_identities()

    def save_identities(self):
        (self.project / IDENTITIES).write_text(json.dumps(self.identities))

    def run_repair(self, **kwargs):
        return repair(self.project, self.manifest, expected_count=1, **kwargs)

    def test_only_actual_target_pointer_type_changes_and_native_identity_is_recorded(self):
        scalar = "  title: 'guid: " + GUID + "'\n"
        unrelated = "  other: {fileID: 7200000, guid: " + OTHER_GUID + ", type: 2}\n"
        self.owner.write_text(self.owner.read_text() + scalar + unrelated)
        before = self.owner.read_bytes()
        source, meta = self.source.read_bytes(), self.meta.read_bytes()
        result = self.run_repair()
        self.assertEqual(result["changedReferenceCount"], 1)
        self.assertEqual(result["ownerCount"], 1)
        self.assertEqual(result["targets"][0]["originalCollection"], "cab-original")
        self.assertEqual(result["targets"][0]["originalPathId"], -123)
        self.assertEqual(result["owners"][0]["originalGuid"], OTHER_GUID)
        self.assertEqual(result["owners"][0]["originalObjects"][0]["pathId"], 456)
        expected = before.replace(("guid: " + GUID + ", type: 2}").encode(), ("guid: " + GUID + ", type: 3}").encode())
        self.assertEqual(self.owner.read_bytes(), expected)
        self.assertEqual(self.source.read_bytes(), source)
        self.assertEqual(self.meta.read_bytes(), meta)
        self.assertEqual(result["owners"][0]["beforeSha256"], hashlib.sha256(before).hexdigest())
        self.assertEqual(result["owners"][0]["sha256"], checksum(self.owner))
        self.assertTrue((self.project / RECEIPT).is_file())

    def test_dry_run_does_not_mutate_owner_or_write_receipt(self):
        before = self.owner.read_bytes()
        result = self.run_repair(apply=False)
        self.assertEqual(result["changedReferenceCount"], 1)
        self.assertFalse(result["applied"])
        self.assertEqual(self.owner.read_bytes(), before)
        self.assertFalse((self.project / RECEIPT).exists())

    def test_repeat_application_is_idempotent(self):
        self.run_repair()
        before = self.owner.read_bytes()
        result = self.run_repair()
        self.assertEqual(result["changedReferenceCount"], 0)
        self.assertEqual(self.owner.read_bytes(), before)

    def test_wrong_local_id_fails_before_any_owner_mutation(self):
        invalid = self.project / "Assets/ZInvalid.asset"
        invalid.write_text(HEADER + "  field: {fileID: 7200001, guid: " + GUID + ", type: 2}\n")
        before = self.owner.read_bytes()
        with self.assertRaisesRegex(ComputeRecoveryError, "localID"):
            self.run_repair()
        self.assertEqual(self.owner.read_bytes(), before)
        self.assertFalse((self.project / RECEIPT).exists())

    def test_block_pointer_representation_fails_explicitly(self):
        self.owner.write_text(HEADER + "  field:\n    fileID: 7200000\n    guid: " + GUID + "\n    type: 2\n")
        before = self.owner.read_bytes()
        with self.assertRaisesRegex(ComputeRecoveryError, "representation"):
            self.run_repair()
        self.assertEqual(self.owner.read_bytes(), before)

    def test_wrong_reference_type_fails(self):
        self.owner.write_text(self.owner.read_text().replace("type: 2", "type: 1"))
        with self.assertRaisesRegex(ComputeRecoveryError, "reference type"):
            self.run_repair()

    def test_wrong_native_class_id_cannot_authorize_pointer_repair(self):
        self.identities["identities"][0]["objects"][0]["classId"] = 48
        self.save_identities()
        with self.assertRaisesRegex(ComputeRecoveryError, "CAB/pathID/type/localID"):
            self.run_repair()

    def test_changed_compute_bytes_are_rejected(self):
        self.source.write_text(self.source.read_text() + "// unauthorized edit\n")
        with self.assertRaisesRegex(ComputeRecoveryError, "source/meta changed"):
            self.run_repair()

    def test_native_importer_is_not_accepted_for_restored_compute_source(self):
        self.meta.write_text(self.meta.read_text().replace("ComputeShaderImporter:", "NativeFormatImporter:"))
        self.manifest["shaders"][0]["metaSha256"] = checksum(self.meta)
        with self.assertRaisesRegex(ComputeRecoveryError, "ComputeShaderImporter"):
            self.run_repair()

    def test_default_census_requires_all_thirteen_compute_targets(self):
        with self.assertRaisesRegex(ComputeRecoveryError, "census"):
            repair(self.project, self.manifest)

    def test_reassigned_guid_without_native_identity_fails(self):
        self.identities["identities"][0]["guid"] = OTHER_GUID
        self.save_identities()
        with self.assertRaisesRegex(ComputeRecoveryError, "unique captured"):
            self.run_repair()

    def test_recorded_type_token_offset_counts_utf8_bytes(self):
        self.owner.write_text(self.owner.read_text().replace("  field:", "  title: 'Größe'\n  field:"), encoding="utf-8")
        before = self.owner.read_bytes()
        result = self.run_repair()
        offset = result["owners"][0]["references"][0]["typeTokenOffset"]
        self.assertEqual(before[offset:offset + 1], b"2")
        self.assertEqual(self.owner.read_bytes()[offset:offset + 1], b"3")

    def test_owner_without_exact_original_identity_is_rejected(self):
        self.identities["identities"][1]["path"] = "Assets/DifferentOwner.asset"
        self.save_identities()
        before = self.owner.read_bytes()
        with self.assertRaisesRegex(ComputeRecoveryError, "owner has no unique"):
            self.run_repair()
        self.assertEqual(self.owner.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
