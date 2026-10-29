"""Real native Unity Resources contracts; no original game or mocked reader."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import zipfile

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import apk_update as update
from storage import BuildError
from test_script_roster_conflicts_205558 import serialized

UNITY_AVAILABLE = importlib.util.find_spec("UnityPy") is not None
MANAGER = "assets/bin/Data/globalgamemanagers"
RESOURCE = "assets/bin/Data/resources.assets"
SECOND = "assets/bin/Data/" + "a" * 32
CAMPAIGN = "assets/quest-campaign-content.zip"
CONTENT, BUNDLES = "quest-mod-content", "quest-mod-bundles"


@unittest.skipUnless(UNITY_AVAILABLE, "Pinned UnityPy is required for real serialized Resources fixtures")
class MenuResourceJsonTests(unittest.TestCase):
    def setUp(self):
        from UnityPy.enums import ClassIDType
        self.kinds = ClassIDType
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.apk = self.root / "old-player.apk"
        self.values = {CONTENT: {"schema": 1, "archive": "quest-mod-content.zip", "files": [], "label": "Original Ä"},
                       BUNDLES: {"target": "Android", "banks": [{"bundleName": "ghvr-town.bundle"}]}}
        self.rows = [(CONTENT, {"m_FileID": 1, "m_PathID": 7}),
                     (BUNDLES, {"m_FileID": 1, "m_PathID": 8})]
        self.assets = {RESOURCE: self.text_file([(7, CONTENT, self.values[CONTENT]), (8, BUNDLES, self.values[BUNDLES])])}
        self.write()

    def text_file(self, rows):
        return serialized([(path_id, self.kinds.TextAsset,
                            {"m_Name": name, "m_Script": json.dumps(value)}) for path_id, name, value in rows])

    def manager(self, rows=None, externals=("resources.assets",), count=1):
        tree = {"m_Container": self.rows if rows is None else rows, "m_DependentAssets": []}
        return serialized([(1 + i, self.kinds.ResourceManager, tree) for i in range(count)], externals)

    def write(self, *, manager=None, extras=()):
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_STORED) as apk:
            apk.writestr(MANAGER, self.manager() if manager is None else manager)
            for name, body in self.assets.items(): apk.writestr(name, body)
            apk.writestr(CAMPAIGN, b"Campaign bank must stay unopened")
            for name, body in extras: apk.writestr(name, body)

    @staticmethod
    def object_table_position(raw, path_id):
        """Locate a real fixture metadata row independently of the production reader."""
        import UnityPy
        obj = next(obj for obj in UnityPy.load(raw).objects if obj.path_id == path_id)
        data_offset = struct.unpack_from(">q", raw, 32)[0]
        row = struct.pack("<qqI", path_id, obj.byte_start - data_offset, obj.byte_size)
        position = raw.find(row, 48, data_offset)
        if position < 0 or raw.find(row, position + 1, data_offset) >= 0:
            raise AssertionError("Fixture must have one independently identifiable metadata row")
        return position

    def test_reads_both_actual_resources_without_changing_apk_or_minting_proofs(self):
        before = hashlib.sha256(self.apk.read_bytes()).hexdigest()
        proofs, values = set(update._resource_proofs), copy.deepcopy(update._resource_value_proofs)
        self.assertEqual(update.read_resource_json(self.apk, (CONTENT, BUNDLES)), self.values)
        self.assertEqual(hashlib.sha256(self.apk.read_bytes()).hexdigest(), before)
        self.assertEqual(update._resource_proofs, proofs)
        self.assertEqual(update._resource_value_proofs, values)

    def test_does_not_open_the_actual_campaign_zip_member(self):
        with zipfile.ZipFile(self.apk) as apk:
            info = apk.getinfo(CAMPAIGN)
        raw = bytearray(self.apk.read_bytes())
        names, extra = struct.unpack_from("<HH", raw, info.header_offset + 26)
        raw[info.header_offset + 30 + names + extra] ^= 1
        self.apk.write_bytes(raw)
        with zipfile.ZipFile(self.apk) as apk:
            with self.assertRaises(zipfile.BadZipFile): apk.read(CAMPAIGN)
        self.assertEqual(update.read_resource_json(self.apk, (CONTENT, BUNDLES)), self.values)

    def test_follows_two_real_external_player_files(self):
        self.assets = {RESOURCE: self.text_file([(7, CONTENT, self.values[CONTENT])]),
                       SECOND: self.text_file([(8, BUNDLES, self.values[BUNDLES])])}
        rows = [(CONTENT, {"m_FileID": 1, "m_PathID": 7}), (BUNDLES, {"m_FileID": 2, "m_PathID": 8})]
        self.write(manager=self.manager(rows, ("resources.assets", "a" * 32)))
        self.assertEqual(update.read_resource_json(self.apk, (CONTENT, BUNDLES)), self.values)

    def test_reads_only_requested_supported_resource(self):
        self.write(manager=self.manager(self.rows[:1]))
        self.assertEqual(update.read_resource_json(self.apk, CONTENT), {CONTENT: self.values[CONTENT]})

    def test_rejects_unsupported_empty_or_duplicate_requested_names(self):
        for names in [None, (), ("quest-profile",), (CONTENT, CONTENT), (CONTENT, "../quest-mod-bundles")]:
            with self.subTest(names=names), self.assertRaisesRegex(BuildError, "read scope"):
                update.read_resource_json(self.apk, names)

    def test_rejects_missing_or_duplicate_native_resource_manager(self):
        for count in (0, 2):
            with self.subTest(count=count):
                self.write(manager=self.manager(count=count))
                with self.assertRaisesRegex(BuildError, "one native ResourceManager"):
                    update.read_resource_json(self.apk, (CONTENT, BUNDLES))

    def test_rejects_missing_and_duplicate_resource_registry_keys(self):
        for rows, message in [(self.rows[:1], "missing from"), (self.rows + self.rows[:1], "ambiguous")]:
            with self.subTest(message=message):
                self.write(manager=self.manager(rows))
                with self.assertRaisesRegex(BuildError, message): update.read_resource_json(self.apk, (CONTENT, BUNDLES))

    def test_rejects_invalid_external_indices_and_empty_pointers(self):
        for index, path_id in [(0, 7), (-1, 7), (2, 7), (1, 0)]:
            with self.subTest(index=index, path_id=path_id):
                rows = [(CONTENT, {"m_FileID": index, "m_PathID": path_id}), self.rows[1]]
                self.write(manager=self.manager(rows))
                with self.assertRaisesRegex(BuildError, "invalid external pointer"):
                    update.read_resource_json(self.apk, (CONTENT, BUNDLES))

    def test_rejects_missing_wrong_kind_and_wrong_name_target_objects(self):
        for asset, message in [
                (self.text_file([(8, BUNDLES, self.values[BUNDLES])]), "resolve to a TextAsset"),
                (serialized([(7, self.kinds.ResourceManager, {"m_Container": [], "m_DependentAssets": []})]), "resolve to a TextAsset"),
                (self.text_file([(7, "different-native-resource", self.values[CONTENT])]), "wrong object")]:
            with self.subTest(message=message):
                self.assets = {RESOURCE: asset}; self.write(manager=self.manager(self.rows[:1]))
                with self.assertRaisesRegex(BuildError, message): update.read_resource_json(self.apk, CONTENT)

    def test_rejects_unsafe_or_non_player_external_members(self):
        for external in ("../resources.assets", "/resources.assets", "folder\\resources.assets", "globalgamemanagers.assets", "../../quest-campaign-content.zip"):
            with self.subTest(external=external):
                self.write(manager=self.manager(externals=(external,)))
                with self.assertRaises(BuildError): update.read_resource_json(self.apk, CONTENT)

    def test_rejects_missing_referenced_player_member(self):
        self.write(manager=self.manager(externals=("b" * 32,)))
        with self.assertRaisesRegex(BuildError, "missing player file"): update.read_resource_json(self.apk, CONTENT)

    def test_rejects_case_colliding_apk_members(self):
        self.write(extras=[(RESOURCE.upper(), self.assets[RESOURCE])])
        with self.assertRaisesRegex(BuildError, "duplicate, encrypted or excessive"):
            update.read_resource_json(self.apk, CONTENT)

    def test_rejects_invalid_json_or_non_object_contract(self):
        for value in ([], None, "not-a-contract"):
            with self.subTest(value=value):
                self.assets = {RESOURCE: self.text_file([(7, CONTENT, value)])}; self.write(manager=self.manager(self.rows[:1]))
                with self.assertRaisesRegex(BuildError, "must be an object"): update.read_resource_json(self.apk, CONTENT)
        raw = self.text_file([(7, CONTENT, {"x": 1})]).replace(b'{"x": 1}', b'{"x": x}')
        self.assets = {RESOURCE: raw}; self.write(manager=self.manager(self.rows[:1]))
        with self.assertRaisesRegex(BuildError, "could not be read safely"): update.read_resource_json(self.apk, CONTENT)

    def test_rejects_excessive_native_json(self):
        self.assets = {RESOURCE: self.text_file([(7, CONTENT, {"large": "x" * update.MAX_JSON})])}
        self.write(manager=self.manager(self.rows[:1]))
        with self.assertRaisesRegex(BuildError, "extent|bounded contract"): update.read_resource_json(self.apk, CONTENT)

    def test_rejects_header_object_and_overlapping_extents(self):
        original = self.assets[RESOURCE]
        for change, message in [("header", "header extents"), ("object", "identity or extents"), ("overlap", "overlap")]:
            with self.subTest(change=change):
                raw = bytearray(original)
                if change == "header": struct.pack_into(">q", raw, 24, len(raw) + 1)
                elif change == "object": struct.pack_into("<q", raw, self.object_table_position(original, 7) + 8, len(raw))
                else:
                    first = self.object_table_position(original, 7)
                    struct.pack_into("<q", raw, self.object_table_position(original, 8) + 8, struct.unpack_from("<q", raw, first + 8)[0])
                self.assets = {RESOURCE: bytes(raw)}; self.write()
                with self.assertRaisesRegex(BuildError, message): update.read_resource_json(self.apk, (CONTENT, BUNDLES))

    def test_rejects_duplicate_native_object_table_id_before_unitypy_can_collapse_it(self):
        original = self.assets[RESOURCE]; raw = bytearray(original)
        struct.pack_into("<q", raw, self.object_table_position(original, 8), 7)
        self.assets = {RESOURCE: bytes(raw)}; self.write()
        with self.assertRaisesRegex(BuildError, "object identity"): update.read_resource_json(self.apk, (CONTENT, BUNDLES))

    def test_existing_patch_still_proves_composed_values_and_retains_unrelated_object_bytes(self):
        import UnityPy
        self.assets[RESOURCE] = self.text_file([(7, CONTENT, self.values[CONTENT]), (8, BUNDLES, self.values[BUNDLES]),
                                              (9, "unrelated-native-note", {"original": True})])
        self.write()
        before = {obj.path_id: obj.get_raw_data() for obj in UnityPy.load(self.assets[RESOURCE]).objects}
        proofs = set(update._resource_proofs)
        try:
            patched = update.patch_resource_json(self.apk, {CONTENT: {"new": "content"}})
            patched = update.patch_resource_json(self.apk, {BUNDLES: {"new": "bank"}}, replacements=patched)
            after = {obj.path_id: obj for obj in UnityPy.load(patched[RESOURCE]).objects}
            self.assertEqual(after[9].get_raw_data(), before[9])
            self.assertEqual(json.loads(after[7].read_typetree()["m_Script"]), {"new": "content"})
            self.assertEqual(json.loads(after[8].read_typetree()["m_Script"]), {"new": "bank"})
            proof = update._base_stamp(self.apk), RESOURCE, hashlib.sha256(patched[RESOURCE]).hexdigest()
            self.assertEqual(set(update._resource_value_proofs[proof]), {CONTENT, BUNDLES})
            self.assertEqual(update.read_resource_json(self.apk, (CONTENT, BUNDLES)), self.values)
        finally:
            for proof in update._resource_proofs - proofs:
                update._resource_proofs.discard(proof); update._resource_value_proofs.pop(proof, None)


if __name__ == "__main__": unittest.main()
