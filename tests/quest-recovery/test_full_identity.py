import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-recovery"))
from export_identity import identity_remaps, object_index, remap_yaml
from recover import RecoveryError
from serialized_repairs import parse_fields, yaml_fields


def row(guid, path, collection="cab-original", path_id=7, file_id=2100000):
    return {"guid": guid, "path": path, "objects": [{"collection": collection, "pathId": path_id,
                                                      "fileId": file_id, "className": "Material", "classId": 21}]}


class OriginalIdentityTests(unittest.TestCase):
    def test_random_guids_and_paths_are_joined_only_through_original_object(self):
        canonical = object_index([row("1" * 32, "Assets/original.mat")])
        incoming = [row("2" * 32, "Assets/a-different-export.mat", file_id=2100010)]
        pointers, duplicates, _ = identity_remaps(incoming, canonical)
        source = "m_Material: {fileID: 2100010, guid: " + "2" * 32 + ", type: 2}"
        self.assertEqual(remap_yaml(source, pointers), "m_Material: {fileID: 2100000, guid: " + "1" * 32 + ", type: 2}")
        self.assertEqual(duplicates, {"Assets/a-different-export.mat": "Assets/original.mat"})

    def test_equal_name_from_different_serialized_container_is_distinct(self):
        canonical = object_index([row("1" * 32, "Assets/same.mat")])
        pointers, duplicates, _ = identity_remaps([row("2" * 32, "Assets/same.mat", collection="cab-other")], canonical)
        self.assertEqual(pointers, {})
        self.assertEqual(duplicates, {})

    def test_two_export_identities_for_original_object_fail(self):
        with self.assertRaises(RecoveryError):
            object_index([row("1" * 32, "Assets/a.mat"), row("2" * 32, "Assets/b.mat")])

    def test_compound_collection_cannot_join_two_distinct_canonical_assets(self):
        first = row("1" * 32, "Assets/a.mat")
        second = row("2" * 32, "Assets/b.mat", path_id=8)
        combined = row("3" * 32, "Assets/compound.asset")
        combined["objects"].append(second["objects"][0])
        with self.assertRaises(RecoveryError):
            identity_remaps([combined], object_index([first, second]))

    def test_unobserved_file_id_is_not_guessed_from_guid(self):
        pointers, _, _ = identity_remaps([row("2" * 32, "Assets/b.mat")], object_index([row("1" * 32, "Assets/a.mat")]))
        text = "{fileID: 42, guid: " + "2" * 32 + ", type: 2}"
        self.assertEqual(remap_yaml(text, pointers), text)


class OriginalLayoutTests(unittest.TestCase):
    @staticmethod
    def header():
        return struct.pack("<iqiiqi", 0, 1, 1, 0, 4, 0)

    @staticmethod
    def string(value):
        content = value.encode()
        return struct.pack("<i", len(content)) + content + b"\0" * (-len(content) % 4)

    def inside_payload(self, managed_type="UIFollowMapLocation/FollowTransform", registry_version=2):
        return (self.header() + struct.pack("<3fqiqfiqiiq", 1, 2, 3, 0, 0, 7, 20, 0, 8,
                                           registry_version, 1, 0)
                + self.string(managed_type) + self.string("") + self.string("GH.Runtime")
                + struct.pack("<iq", 0, 9))

    def test_zero_field_model_list_retains_every_original_entry(self):
        raw = self.header() + struct.pack("<ii", 0, 82)
        fields, references = parse_fields("DimmerUIElements", raw)
        self.assertEqual(len(fields["_dimmingColorPairsContainer"]), 82)
        self.assertIsNone(references)
        self.assertEqual(yaml_fields(fields, references, str).count("  - {}"), 82)

    def test_original_managed_registry_and_object_pointers_retained(self):
        fields, references = parse_fields("UIFollowMapLocationInsideArea", self.inside_payload())
        self.assertEqual(fields["offset"], (1, 2, 3))
        self.assertEqual(fields["area"], (0, 7))
        self.assertEqual(fields["target"], {"rid": 0})
        self.assertEqual(references[0]["data"], {"targetTransform": (0, 9)})
        text = yaml_fields(fields, references, lambda value: "{fileID: " + str(value[1]) + "}")
        self.assertIn("targetTransform: {fileID: 9}", text)
        self.assertIn("type: {class: UIFollowMapLocation/FollowTransform, ns: , asm: GH.Runtime}", text)

    def test_unknown_original_type_is_not_replaced_with_default(self):
        with self.assertRaises(RecoveryError):
            parse_fields("UIFollowMapLocationInsideArea", self.inside_payload("UnknownTarget"))

    def test_unknown_registry_version_fails(self):
        with self.assertRaises(RecoveryError):
            parse_fields("UIFollowMapLocationInsideArea", self.inside_payload(registry_version=3))

    def test_truncation_and_new_serialized_fields_fail(self):
        for raw in (self.inside_payload()[:-1], self.inside_payload() + b"extra"):
            with self.assertRaises(RecoveryError):
                parse_fields("UIFollowMapLocationInsideArea", raw)


if __name__ == "__main__":
    unittest.main()
