"""The compact native pair carries Unity's own Android extraction identity.

Tiny real ZIPs contain real Unity2021 serialized script registries. The cache
model follows the pinned Player's observed GUID comparison/copy predicate; it
does not claim Android/IL2CPP execution or a verified headset outcome.
"""
import copy
import hashlib
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import apk_update as update
import storage
import test_assembly_registry_211749 as fixtures

GUID = "assets/bin/Data/unity_app_guid"
OLD_GUID = b"11111111-1111-4111-8111-111111111111"
NEW_GUID = b"22222222-2222-4222-8222-222222222222"


class ExtractedManagedCache:
    """Model UnityInitApplication: FilesDir/il2cpp/unity.ver equals APK GUID."""
    def __init__(self):
        self.version = None
        self.members = {}
        self.extractions = 0

    def launch(self, path):
        with zipfile.ZipFile(path) as apk:
            version = apk.read(GUID)
            if not version or self.version != version:
                self.members = {row.filename: apk.read(row) for row in apk.infolist()
                                if row.filename.startswith("assets/bin/Data/Managed/")}
                self.version = version
                self.extractions += 1
            return apk.read(update.LIBRARY), self.members[update.METADATA]


@unittest.skipUnless(importlib.util.find_spec("UnityPy") is not None, "Pinned builder UnityPy is required")
class Il2CppCacheIdentityTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.AssemblyRegistryTests(methodName="runTest")
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.fixture.values[GUID] = OLD_GUID
        self.fixture.new_values[GUID] = NEW_GUID
        self.fixture.new_values[update.LIBRARY] += b"compiled code generation 2"
        self.fixture.new_values[update.METADATA] += b"compiled metadata generation 2"
        self.close_pair()

    def close_pair(self):
        self.fixture.receipt["files"] = [{"path": name, "size": len(self.fixture.new_values[name]),
            "sha256": hashlib.sha256(self.fixture.new_values[name]).hexdigest()}
            for name in (update.LIBRARY, update.METADATA)]

    def publish(self, qualified):
        return update.repack(self.fixture.base, self.fixture.output, qualified["replacements"],
            update_manifest={"schema": 1, "gameInputKey": "a" * 64, "updateKey": "c" * 64, "modBuild": 673},
            signer=lambda _: None, verifier=lambda _: None, code_evidence=qualified)

    def test_real_zip_update_refreshes_preexisting_metadata_once_and_keeps_scene(self):
        qualified = self.fixture.collect()
        self.assertEqual(qualified["replacements"][GUID], NEW_GUID)
        self.assertEqual(qualified["replacementSha256"][GUID], hashlib.sha256(NEW_GUID).hexdigest())
        original = self.fixture.base.read_bytes(), self.fixture.compiled.read_bytes()
        cache = ExtractedManagedCache()
        old_pair = cache.launch(self.fixture.base)
        self.publish(qualified)
        actual_pair = cache.launch(self.fixture.output)
        self.assertNotEqual(old_pair, actual_pair)
        self.assertEqual(actual_pair, tuple(self.fixture.new_values[name] for name in (update.LIBRARY, update.METADATA)))
        self.assertEqual(cache.extractions, 2)
        self.assertEqual(cache.launch(self.fixture.output), actual_pair)
        self.assertEqual(cache.extractions, 2)
        with zipfile.ZipFile(self.fixture.output) as apk:
            self.assertEqual(apk.read(GUID), NEW_GUID)
            self.assertEqual(apk.read("assets/bin/Data/level0"), self.fixture.values["assets/bin/Data/level0"])
        self.assertEqual(original, (self.fixture.base.read_bytes(), self.fixture.compiled.read_bytes()))

    def test_negative_control_old_collector_retains_guid_and_uses_old_cached_metadata(self):
        qualified = self.fixture.collect()
        cache = ExtractedManagedCache(); cache.launch(self.fixture.base)
        broken = {**self.fixture.values, **qualified["replacements"], GUID: OLD_GUID}
        self.fixture.write_zip(self.fixture.output, broken)
        actual = cache.launch(self.fixture.output)
        self.assertEqual(actual[0], self.fixture.new_values[update.LIBRARY])
        self.assertEqual(actual[1], self.fixture.values[update.METADATA])
        self.assertNotEqual(actual[1], self.fixture.new_values[update.METADATA])
        self.assertEqual(cache.extractions, 1)

    def test_fresh_guid_avoids_reading_old_native_or_managed_payloads(self):
        open_member = zipfile.ZipFile.open
        seen = []
        def watch(apk, name, *args, **kwargs):
            member = name.filename if isinstance(name, zipfile.ZipInfo) else name
            if apk.mode == "r" and Path(apk.filename) == self.fixture.base and member in (update.LIBRARY, update.METADATA):
                seen.append(member)
            return open_member(apk, name, *args, **kwargs)
        # inspect_base/_native reads only native headers. A second read of either
        # base payload would be an unnecessary full-pair verification scan.
        with patch.object(zipfile.ZipFile, "open", watch): qualified = self.fixture.collect()
        self.assertEqual(seen.count(update.LIBRARY), 1)
        self.assertEqual(seen.count(update.METADATA), 1)
        self.assertEqual(qualified["replacements"][GUID], NEW_GUID)

    def test_unqualified_native_pair_fails_before_guid_or_registry_admission(self):
        self.fixture.new_values[GUID] = b"invalid"
        self.fixture.receipt["files"][0]["sha256"] = "0" * 64
        with patch.object(update, "script_roster", side_effect=AssertionError("Native pair admission must come first")):
            with self.assertRaisesRegex(storage.BuildError, "closed compiler receipt"):
                self.fixture.collect()

    def test_missing_guid_and_malformed_or_zero_uuid_are_rejected_without_publication(self):
        for value in (None, b"", NEW_GUID + b"\n", b"0" * 36,
                      b"00000000-0000-0000-0000-000000000000", b"z" * 36,
                      b"../../../../../../../../../../../../", b"\xff" * 36):
            with self.subTest(value=value):
                self.fixture.new_values.pop(GUID, None)
                if value is not None: self.fixture.new_values[GUID] = value
                with self.assertRaisesRegex(storage.BuildError, "Unity IL2CPP cache identity"):
                    self.fixture.collect()
                self.assertFalse(self.fixture.output.exists())

    def test_same_guid_and_exact_same_pair_is_idempotent(self):
        self.fixture.values[GUID] = NEW_GUID
        for name in (update.LIBRARY, update.METADATA):
            self.fixture.new_values[name] = self.fixture.values[name]
        self.close_pair()
        self.assertEqual(self.fixture.collect()["replacements"][GUID], NEW_GUID)

    def test_same_guid_with_changed_library_or_metadata_is_rejected(self):
        self.fixture.new_values[GUID] = OLD_GUID
        for name in (update.LIBRARY, update.METADATA):
            with self.subTest(name=name):
                self.fixture.new_values[name] = self.fixture.values[name]
                other = update.METADATA if name == update.LIBRARY else update.LIBRARY
                self.fixture.new_values[other] = self.fixture.values[other] + b"changed"
                self.close_pair()
                with self.assertRaisesRegex(storage.BuildError, "without changing the Unity cache identity"):
                    self.fixture.collect()

    def test_same_guid_with_changed_or_new_cached_managed_resources_is_rejected(self):
        self.fixture.new_values[GUID] = OLD_GUID
        for name in (update.LIBRARY, update.METADATA): self.fixture.new_values[name] = self.fixture.values[name]
        self.close_pair()
        for name in (update.MANAGED_RESOURCES + "GH.Runtime.dll-resources.dat",
                     update.MANAGED_RESOURCES + "New.Mod.dll-resources.dat"):
            with self.subTest(name=name):
                self.fixture.new_values[name] = b"changed cached resource"
                with self.assertRaisesRegex(storage.BuildError, "without changing the Unity cache identity"):
                    self.fixture.collect()
                if name in self.fixture.values: self.fixture.new_values[name] = self.fixture.values[name]
                else: self.fixture.new_values.pop(name)

    def test_guid_cannot_be_removed_or_mutated_after_qualification(self):
        qualified = self.fixture.collect()
        for mode in ("removed", "mutated", "evidence-omitted"):
            with self.subTest(mode=mode):
                broken = copy.deepcopy(qualified)
                if mode == "removed": broken["replacements"].pop(GUID)
                elif mode == "mutated": broken["replacements"][GUID] = OLD_GUID
                else: broken["replacementSha256"].pop(GUID)
                with self.assertRaisesRegex(storage.BuildError, "changed before|matching player sidecars"):
                    self.publish(broken)
                self.assertFalse(self.fixture.output.exists())

    def test_guid_only_replacement_cannot_bypass_native_pair_gate(self):
        self.fixture.collect()
        with self.assertRaisesRegex(storage.BuildError, "requires the qualified matching native pair"):
            self.publish({"replacements": {GUID: NEW_GUID}})


if __name__ == "__main__": unittest.main()
