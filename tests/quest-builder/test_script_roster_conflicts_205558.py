"""Real serialized Unity registry fixtures; no game data, Editor or APK build."""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import apk_update as update
from storage import BuildError

UNITY_AVAILABLE = importlib.util.find_spec("UnityPy") is not None
MANAGER = "assets/bin/Data/globalgamemanagers"
SCRIPTS = "assets/bin/Data/globalgamemanagers.assets"
DEFAULTS = "assets/bin/Data/unity default resources"


def serialized(objects, externals=(), version="2021.3.5f1"):
    """Create a small real format22 file using only Unity's public type schema."""
    from UnityPy.enums import BuildTarget
    from UnityPy.files.ObjectReader import ObjectReader
    from UnityPy.files.SerializedFile import FileIdentifier, SerializedFile, SerializedFileHeader, SerializedType
    from UnityPy.helpers.Tpk import get_typetree_node
    from UnityPy.streams import EndianBinaryReader

    file = SerializedFile.__new__(SerializedFile)
    header = SerializedFileHeader.__new__(SerializedFileHeader)
    for name, value in dict(version=22, metadata_size=0, file_size=0, data_offset=0, endian="<", reserved=b"\0" * 3).items():
        setattr(header, name, value)
    file.parent = None
    file.header = header
    file.reader = EndianBinaryReader(b"", endian="<")
    file.set_version(version)
    file.target_platform = BuildTarget.Android
    file._m_target_platform = int(BuildTarget.Android)
    file._enable_type_tree = False  # UnityPy resolves known classes from its pinned TPK.
    file.types, file.script_types, file.externals, file.ref_types = [], [], [], []
    file.objects = {}
    file.big_id_enabled = file.unknown = 0
    file.userInformation = "Nonproprietary script roster fixture"
    for path in externals:
        external = FileIdentifier.__new__(FileIdentifier)
        external.path, external.temp_empty, external.guid, external.type = path, "", b"\0" * 16, 0
        file.externals.append(external)
    for path_id, kind, tree in objects:
        typ = SerializedType.__new__(SerializedType)
        values = dict(class_id=int(kind), is_stripped_type=False, script_type_index=-1, script_id=None,
                      old_type_hash=b"\0" * 16, node=get_typetree_node(kind, file.version), type_dependencies=[],
                      m_ClassName=None, m_NameSpace=None, m_AssemblyName=None)
        for name, value in values.items():
            setattr(typ, name, value)
        file.types.append(typ)
        obj = ObjectReader(file, file.reader, path_id, len(file.types) - 1, typ, int(kind), kind, 0, 0, None, None)
        obj.save_typetree(tree)
        file.objects[path_id] = obj
    return file.save()


def script(row):
    return {"m_Name": row["class"], "m_AssemblyName": row["assembly"], "m_Namespace": row["namespace"],
            "m_ClassName": row["class"], "m_ExecutionOrder": row["executionOrder"],
            "m_PropertiesHash": {"bytes[" + str(i) + "]": value for i, value in enumerate(bytes.fromhex(row["propertiesHash"]))}}


def registry_members(candidates, *, refs=None, default_version="2021.3.2f1"):
    from UnityPy.enums import ClassIDType
    locations = [SCRIPTS, DEFAULTS]
    rows = {name: [] for name in locations}
    pointers = []
    for name, path_id, row in candidates:
        rows[name].append((path_id, ClassIDType.MonoScript, script(row)))
        pointers.append({"m_FileID": locations.index(name) + 1, "m_PathID": path_id})
    manager = {"m_ScriptHashes": [], "m_RuntimeClassHashes": [], "m_Scripts": pointers if refs is None else refs}
    result = {MANAGER: serialized([(1, ClassIDType.MonoManager, manager)],
                                 ["globalgamemanagers.assets", "Library/unity default resources"])}
    for name, values in rows.items():
        if values:
            result[name] = serialized(values, version=default_version if name == DEFAULTS else "2021.3.5f1")
    return result


@unittest.skipUnless(UNITY_AVAILABLE, "The builder's pinned UnityPy is required for serialized fixtures")
class ScriptRosterConflictTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.apk = self.root / "registry.apk"
        self.original = {"assembly": "Fixture.dll", "namespace": "Fixtures", "class": "OriginalWidget",
                         "propertiesHash": "1" * 32, "executionOrder": -37}

    def tearDown(self):
        self.temp.cleanup()

    def write(self, candidates, **kwargs):
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_DEFLATED) as apk:
            for name, data in registry_members(candidates, **kwargs).items():
                apk.writestr(name, data)

    def duplicate(self, **changes):
        return [(SCRIPTS, 11, self.original), (DEFAULTS, 22, {**self.original, **changes})]

    def test_real_identical_duplicate_objects_collapse_without_an_authority(self):
        self.write(self.duplicate())
        actual = update.script_roster(self.apk)
        self.assertEqual(actual["scriptTypes"], [self.original])
        self.assertEqual(actual["retainedOrderDuplicateTypes"], 0)

    def test_generic_roster_remains_strict_and_reports_both_locations_and_values(self):
        self.write(self.duplicate(executionOrder=0))
        with self.assertRaises(BuildError) as caught:
            update.script_roster(self.apk)
        detail = json.loads(str(caught.exception).split(": ", 1)[1])
        self.assertEqual(detail["identity"], ["Fixture.dll", "Fixtures", "OriginalWidget"])
        self.assertEqual(detail["fields"], ["executionOrder"])
        self.assertEqual([(row["member"], row["fileIDs"], row["pathID"], row["executionOrder"])
                          for row in detail["candidates"]], [(SCRIPTS, [1], 11, -37), (DEFAULTS, [2], 22, 0)])
        self.assertLess(len(str(caught.exception)), 2000)

    def test_only_order_conflict_uses_exact_retained_original_row(self):
        self.write(self.duplicate(executionOrder=0))
        with contextlib.redirect_stdout(io.StringIO()) as output:
            actual = update.script_roster(self.apk, expected_types=[self.original])
        self.assertEqual(actual["scriptTypes"], [self.original])
        self.assertEqual(actual["retainedOrderDuplicateTypes"], 1)
        self.assertIn("every physical serialized property hash matches", output.getvalue())

    def test_conflicting_hash_rejects_even_when_one_candidate_matches_original(self):
        self.write(self.duplicate(propertiesHash="2" * 32))
        with self.assertRaisesRegex(BuildError, '"fields":\\["propertiesHash"\\]'):
            update.script_roster(self.apk, expected_types=[self.original])

    def test_empty_hash_does_not_authorize_an_incompatible_layout(self):
        self.write(self.duplicate(propertiesHash="0" * 32, executionOrder=0))
        with self.assertRaisesRegex(BuildError, "conflicting MonoScript identities"):
            update.script_roster(self.apk, expected_types=[self.original])

    def test_order_resolution_requires_an_exact_original_candidate(self):
        candidates = self.duplicate(executionOrder=0)
        candidates[0] = (SCRIPTS, 11, {**self.original, "executionOrder": 3})
        self.write(candidates)
        with self.assertRaisesRegex(BuildError, "conflicting MonoScript identities"):
            update.script_roster(self.apk, expected_types=[self.original])

    def test_identical_but_changed_candidate_hashes_do_not_gain_an_order_exception(self):
        candidates = [(name, path_id, {**row, "propertiesHash": "2" * 32})
                      for name, path_id, row in self.duplicate(executionOrder=0)]
        self.write(candidates)
        with self.assertRaisesRegex(BuildError, "conflicting MonoScript identities"):
            update.script_roster(self.apk, expected_types=[self.original])

    def test_new_ambiguous_types_have_no_original_authority(self):
        self.write(self.duplicate(executionOrder=0))
        with self.assertRaisesRegex(BuildError, "conflicting MonoScript identities"):
            update.script_roster(self.apk, expected_types=[{**self.original, "class": "OtherWidget"}])

    def test_single_changed_serialized_type_still_requires_asset_migration(self):
        changed = {**self.original, "propertiesHash": "2" * 32}
        self.write([(SCRIPTS, 11, changed)])
        registry = update.script_roster(self.apk, expected_types=[self.original])
        self.assertEqual(registry["scriptTypes"], [changed])
        with self.assertRaisesRegex(update.RebuildRequired, "changes or removes a serialized script"):
            update._required_types([self.original], registry["scriptTypes"])

    def test_order_resolution_is_independent_of_object_and_pointer_order(self):
        candidates = self.duplicate(executionOrder=0)
        candidates += [(SCRIPTS, 33, {**self.original, "executionOrder": 5})]
        for physical in (candidates, list(reversed(candidates))):
            self.write(physical)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(update.script_roster(self.apk, expected_types=[self.original])["scriptTypes"], [self.original])

    def test_all_hashes_are_checked_not_just_the_first_order_only_conflict(self):
        candidates = self.duplicate(executionOrder=0)
        candidates += [(SCRIPTS, 33, {**self.original, "propertiesHash": "2" * 32})]
        self.write(candidates)
        with self.assertRaises(BuildError) as caught:
            update.script_roster(self.apk, expected_types=[self.original])
        detail = json.loads(str(caught.exception).split(": ", 1)[1])
        self.assertIn("propertiesHash", detail["fields"])
        self.assertEqual({row["propertiesHash"] for row in detail["candidates"]}, {"1" * 32, "2" * 32})

    def test_objects_outside_mono_manager_registration_do_not_join_the_roster(self):
        self.write(self.duplicate(propertiesHash="2" * 32), refs=[{"m_FileID": 1, "m_PathID": 11}])
        self.assertEqual(update.script_roster(self.apk)["scriptTypes"], [self.original])

    def test_missing_script_pointer_still_rejects(self):
        self.write(self.duplicate(), refs=[{"m_FileID": 1, "m_PathID": 999}])
        with self.assertRaisesRegex(BuildError, "does not resolve to a MonoScript"):
            update.script_roster(self.apk, expected_types=[self.original])


@unittest.skipUnless(UNITY_AVAILABLE, "The builder's pinned UnityPy is required for serialized fixtures")
class QualifiedNativePairRosterTests(unittest.TestCase):
    def setUp(self):
        # Reuse only the nonproprietary signed-contract fixture, not its tests.
        path = Path(__file__).with_name("test_apk_update.py")
        spec = importlib.util.spec_from_file_location("_roster_apk_fixture", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.fixture = module.ApkUpdateTests(methodName="runTest")
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.compiled = self.fixture.root / "compiled.apk"
        row = self.fixture.row
        self.members = registry_members([(SCRIPTS, 11, row), (DEFAULTS, 22, {**row, "executionOrder": 7})])
        callback = {"assemblyName": "QuestGame.Campaign", "nameSpace": "GloomhavenVR.Quest", "className": "QuestOfflineProfile",
                    "methodName": "Initialize", "loadTypes": 1, "isUnityClass": False}
        self.members.update({name: self.fixture.values[name] for name in (update.LIBRARY, update.METADATA, update.ASSEMBLIES)})
        self.members[update.INITIALIZERS] = json.dumps({"root": [callback]}).encode()
        self.fixture.write_zip(self.compiled, self.members)
        self.receipt = {"schema": 1, "scope": "quest-code-update-v1", "buildResult": "Succeeded", "originalAssetsImported": False,
                        "unityVersion": "2021.3.5f1", "packageAbi": "b" * 64, "abi": "arm64-v8a",
                        "files": [{"path": name, "size": len(self.members[name]), "sha256": hashlib.sha256(self.members[name]).hexdigest()}
                                  for name in (update.LIBRARY, update.METADATA)]}

    def test_qualified_pair_keeps_original_registry_and_capsule_orders(self):
        with contextlib.redirect_stdout(io.StringIO()):
            actual = update.collect_code_update(self.fixture.base, self.compiled, self.receipt, package_abi="b" * 64)
        self.assertTrue(actual["nativePairQualified"])
        self.assertEqual(actual["serializedOriginalTypesQualified"], 1)
        self.assertEqual(actual["capsule"]["scriptTypes"], [self.fixture.row])
        self.assertTrue({update.LIBRARY, update.METADATA}.issubset(actual["replacements"]))
        self.assertFalse({MANAGER, SCRIPTS, DEFAULTS}.intersection(actual["replacements"]))

    def test_native_pair_receipt_failure_never_reaches_order_admission(self):
        receipt = copy.deepcopy(self.receipt)
        receipt["files"][0]["sha256"] = "0" * 64
        with patch.object(update, "script_roster", side_effect=AssertionError("Unqualified pair must stop first")):
            with self.assertRaisesRegex(BuildError, "differs from its closed compiler receipt"):
                update.collect_code_update(self.fixture.base, self.compiled, receipt, package_abi="b" * 64)

    def test_qualified_native_pair_cannot_hide_a_conflicting_property_hash(self):
        self.members.update(registry_members([(SCRIPTS, 11, self.fixture.row),
            (DEFAULTS, 22, {**self.fixture.row, "propertiesHash": "2" * 32})]))
        self.fixture.write_zip(self.compiled, self.members)
        with self.assertRaisesRegex(BuildError, "conflicting MonoScript identities"):
            update.collect_code_update(self.fixture.base, self.compiled, self.receipt, package_abi="b" * 64)


if __name__ == "__main__":
    unittest.main()
