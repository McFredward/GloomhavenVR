"""Real tiny APK collection keeps identical assembly registrations and order.

Fixtures use real Unity2021 serialized registries and matched native receipts.
No game assets, Editor build or Android execution is involved.
"""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import apk_update as update
import storage

import test_script_roster_conflicts_205558 as serialized_fixture
import test_native_update_resume as resume_fixture

UNITY_AVAILABLE = importlib.util.find_spec("UnityPy") is not None


@unittest.skipUnless(UNITY_AVAILABLE, "The builder's pinned UnityPy is required for serialized fixtures")
class AssemblyRegistryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.base, self.compiled, self.output = (self.root / name for name in ("base.apk", "compiled.apk", "updated.apk"))
        self.row = {"assembly": "GH.Runtime.dll", "namespace": "Fixtures", "class": "OriginalWidget",
                    "propertiesHash": "1" * 32, "executionOrder": 0}
        managed = [{"path": "Managed/" + name, "size": 1, "sha256": "7" * 64}
                   for name in ("GH.Runtime.dll", "GH.Shared.dll")]
        capsule = {"schema": 1, "scope": "quest-code-update-v1", "inputKey": "a" * 64,
                   "unityVersion": "2021.3.5f1", "packageAbi": "b" * 64, "metadataVersion": 29,
                   "managedKey": storage.value_hash(managed), "scriptTypes": [self.row],
                   "modSchema": 1, "dynamicProfile": True}
        library = bytearray(64); library[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", library, 18, 183)
        self.old_callback = {"assemblyName": "GH.Runtime", "nameSpace": "Fixtures", "className": "OriginalWidget",
                             "methodName": "Initialize", "loadTypes": 1, "isUnityClass": False}
        self.offline_callback = {"assemblyName": "QuestGame.Campaign", "nameSpace": "GloomhavenVR.Quest",
                                 "className": "QuestOfflineProfile", "methodName": "Initialize", "loadTypes": 1,
                                 "isUnityClass": False}
        self.registry = {"names": ["UnityEngine.SpatialTracking.dll", "GH.Runtime.dll",
                                   "UnityEngine.XR.LegacyInputHelpers.dll", "UnityEngine.SpatialTracking.dll",
                                   "UnityEngine.XR.LegacyInputHelpers.dll"], "types": [16, 16, 16, 16, 16]}
        self.values = {update.INPUT: storage.canonical({"schema": 1, "target": "game", "inputKey": "a" * 64,
                        "game": {"files": managed}, "mod": {"modBuild": 672}}),
                       update.INSTALLATION: storage.canonical({"schema": 1, "inputKey": "a" * 64}),
                       update.CAPSULE: storage.canonical(capsule), update.LIBRARY: bytes(library),
                       update.METADATA: struct.pack("<II", 0xFAB11BAF, 29) + b"paired fixture metadata",
                       update.ASSEMBLIES: storage.canonical(self.registry),
                       update.INITIALIZERS: storage.canonical({"root": [self.old_callback]}),
                       update.MANAGED_RESOURCES + "GH.Runtime.dll-resources.dat": b"original managed resource",
                       "assets/bin/Data/level0": b"original serialized scene"}
        self.values.update(serialized_fixture.registry_members([(serialized_fixture.SCRIPTS, 11, self.row)]))
        self.new_values = dict(self.values)
        self.new_values[update.INITIALIZERS] = storage.canonical({"root": [self.old_callback, self.offline_callback]})
        self.receipt = {"schema": 1, "scope": "quest-code-update-v1", "buildResult": "Succeeded",
                        "originalAssetsImported": False, "unityVersion": "2021.3.5f1", "packageAbi": "b" * 64,
                        "abi": "arm64-v8a", "files": [{"path": name, "size": len(self.new_values[name]),
                            "sha256": hashlib.sha256(self.new_values[name]).hexdigest()}
                            for name in (update.LIBRARY, update.METADATA)]}

    @staticmethod
    def write_zip(path, values):
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as apk:
            for name, value in values.items():
                apk.writestr(name, value)

    def collect(self):
        self.write_zip(self.base, self.values); self.write_zip(self.compiled, self.new_values)
        return update.collect_code_update(self.base, self.compiled, self.receipt, package_abi="b" * 64)

    def test_identical_duplicates_in_both_players_preserve_compiled_order_and_bytes(self):
        self.new_values[update.ASSEMBLIES] = b'{ "names": ["GH.Runtime.dll","UnityEngine.XR.LegacyInputHelpers.dll","UnityEngine.SpatialTracking.dll","GH.Runtime.dll","UnityEngine.SpatialTracking.dll"], "types": [16,16,16,16,16] }\n'
        qualified = self.collect()
        self.assertTrue(qualified["nativePairQualified"])
        self.assertEqual(qualified["serializedOriginalTypesQualified"], 1)
        self.assertEqual(qualified["replacements"][update.ASSEMBLIES], self.new_values[update.ASSEMBLIES])
        before = self.base.read_bytes(), self.compiled.read_bytes()
        update.repack(self.base, self.output, qualified["replacements"], code_evidence=qualified,
                      update_manifest={"schema": 1, "gameInputKey": "a" * 64, "updateKey": "c" * 64, "modBuild": 673},
                      signer=lambda _: None, verifier=lambda _: None)
        with zipfile.ZipFile(self.output) as apk:
            self.assertEqual(apk.read(update.ASSEMBLIES), self.new_values[update.ASSEMBLIES])
            self.assertEqual(apk.read("assets/bin/Data/level0"), self.values["assets/bin/Data/level0"])
            self.assertEqual(apk.read(update.MANAGED_RESOURCES + "GH.Runtime.dll-resources.dat"),
                             self.values[update.MANAGED_RESOURCES + "GH.Runtime.dll-resources.dat"])
        self.assertEqual(before, (self.base.read_bytes(), self.compiled.read_bytes()))

    def test_original_duplicates_do_not_require_duplicates_in_compiled_registry(self):
        self.new_values[update.ASSEMBLIES] = storage.canonical({"names": self.registry["names"][:3], "types": [16] * 3})
        self.assertTrue(self.collect()["nativePairQualified"])

    def test_compiled_duplicates_and_new_assembly_do_not_require_original_duplicates(self):
        self.values[update.ASSEMBLIES] = storage.canonical({"names": self.registry["names"][:3], "types": [16] * 3})
        self.new_values[update.ASSEMBLIES] = storage.canonical({"names": self.registry["names"] + ["New.Mod.dll"],
                                                              "types": self.registry["types"] + [16]})
        self.assertTrue(self.collect()["nativePairQualified"])

    def test_conflicting_classification_reports_both_indices_and_source_without_rebuild(self):
        for source in ("original", "compiled"):
            with self.subTest(source=source):
                original, compiled = self.values[update.ASSEMBLIES], self.new_values[update.ASSEMBLIES]
                value = copy.deepcopy(self.registry); value["types"][3] = 2
                target = self.values if source == "original" else self.new_values
                target[update.ASSEMBLIES] = storage.canonical(value)
                with self.assertRaises(storage.BuildError) as caught:
                    self.collect()
                self.assertNotIsInstance(caught.exception, update.RebuildRequired)
                detail = json.loads(str(caught.exception).split(": ", 1)[1])
                self.assertEqual(detail, {"source": source, "member": update.ASSEMBLIES,
                    "reason": "conflicting-classifications", "name": "UnityEngine.SpatialTracking.dll",
                    "indices": [0, 3], "types": [16, 2]})
                self.assertFalse(self.output.exists())
                self.values[update.ASSEMBLIES], self.new_values[update.ASSEMBLIES] = original, compiled

    def test_malformed_names_and_classifications_fail_with_bounded_source_context(self):
        malformed = [({"names": {}, "types": []}, "names-and-types-must-be-arrays"),
                     ({"names": [], "types": []}, "array-lengths"),
                     ({"names": ["GH.Runtime.dll"], "types": []}, "array-lengths")]
        malformed += [({"names": [value], "types": [16]}, "assembly-name")
                      for value in (None, [], "", ".dll", "GH.Runtime", "../GH.dll", "a\\GH.dll", "a:GH.dll",
                                    " GH.dll", "GH\n.dll", "GH\0.dll", "GH\x7f.dll", "a" * 1025 + ".dll", "\ud800.dll")]
        malformed += [({"names": ["GH.Runtime.dll"], "types": [value]}, "classification")
                      for value in (None, True, False, "16", 16.0, -1, 0x80000000, [], {})]
        for source in ("original", "compiled"):
            for value, reason in malformed:
                with self.subTest(source=source, value=value):
                    original, compiled = self.values[update.ASSEMBLIES], self.new_values[update.ASSEMBLIES]
                    target = self.values if source == "original" else self.new_values
                    target[update.ASSEMBLIES] = json.dumps(value).encode()
                    with self.assertRaises(storage.BuildError) as caught:
                        self.collect()
                    self.assertNotIsInstance(caught.exception, update.RebuildRequired)
                    detail = json.loads(str(caught.exception).split(": ", 1)[1])
                    self.assertEqual((detail["source"], detail["member"], detail["reason"]), (source, update.ASSEMBLIES, reason))
                    self.assertLess(len(str(caught.exception)), 2200)
                    self.values[update.ASSEMBLIES], self.new_values[update.ASSEMBLIES] = original, compiled

    def test_original_subset_still_requires_every_name_and_exact_classification(self):
        for registry in ({"names": self.registry["names"][1:3], "types": [16, 16]},
                         {"names": self.registry["names"][:3], "types": [2, 16, 16]}):
            with self.subTest(registry=registry):
                self.new_values[update.ASSEMBLIES] = storage.canonical(registry)
                with self.assertRaisesRegex(update.RebuildRequired, "removed or reclassified an original player assembly"):
                    self.collect()

    def test_duplicate_admission_keeps_callback_and_managed_resource_gates(self):
        self.new_values[update.INITIALIZERS] = storage.canonical({"root": [self.offline_callback]})
        with self.assertRaisesRegex(storage.BuildError, "lost an original game/package startup callback"):
            self.collect()
        self.new_values[update.INITIALIZERS] = storage.canonical({"root": [self.old_callback]})
        with self.assertRaisesRegex(storage.BuildError, "offline-profile startup boundary"):
            self.collect()
        self.new_values[update.INITIALIZERS] = storage.canonical({"root": [self.old_callback, self.offline_callback]})
        del self.new_values[update.MANAGED_RESOURCES + "GH.Runtime.dll-resources.dat"]
        with self.assertRaisesRegex(update.RebuildRequired, "removed original managed resource sidecars"):
            self.collect()

    def test_corrupt_native_pair_still_stops_before_assembly_admission(self):
        self.receipt["files"][0]["sha256"] = "9" * 64
        self.new_values[update.ASSEMBLIES] = b'{"names": [], "types": []}'
        with self.assertRaisesRegex(storage.BuildError, "differs from its closed compiler receipt"):
            self.collect()


class AssemblyNativeResumeTests(unittest.TestCase):
    def test_completed_native_player_with_identical_duplicate_rows_is_adopted_without_unity(self):
        fixture = resume_fixture.NativeUpdateResumeTests(methodName="runTest")
        self.addCleanup(fixture.doCleanups)
        original_write = fixture.write_zip
        registry = storage.canonical({"names": ["GH.Runtime.dll", "GH.Runtime.dll"], "types": [16, 16]})
        def duplicate_write(path, values):
            return original_write(path, {**values, update.ASSEMBLIES: registry})
        with patch.object(fixture, "write_zip", side_effect=duplicate_write):
            fixture.setUp()
        before = fixture.compiled.read_bytes(), fixture.receipt.read_bytes()
        with patch.object(resume_fixture.driver, "_build_code_player", side_effect=AssertionError("Completed native pair must be reused")) as build:
            qualified = fixture.compile()
        build.assert_not_called()
        self.assertTrue(qualified["nativePairQualified"])
        self.assertEqual(qualified["replacements"][update.ASSEMBLIES], registry)
        self.assertEqual(before, (fixture.compiled.read_bytes(), fixture.receipt.read_bytes()))
        self.assertTrue(storage.Stages(fixture.output).valid("update-code", fixture.root.name))


if __name__ == "__main__":
    unittest.main()
