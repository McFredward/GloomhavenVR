"""Real APK publication boundary for the exact native helper repair.

ELF headers are inert fixtures. Actual NDK/symbol and Wine path execution proofs
live in test_proton_helper_update/test_proton_nls; no Android result is inferred.
"""
import copy
import hashlib
import json
from pathlib import Path
import struct
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import apk_update as core
import proton_helper_update as helper
import storage
import update_driver as driver
import test_apk_update as fixtures


class ProtonHelperPublicationTests(unittest.TestCase):
    def setUp(self):
        self.f = fixtures.ApkUpdateTests()
        self.f.setUp()
        self.addCleanup(self.f.tearDown)
        previous = {row["path"]: row for row in helper.PREVIOUS_SOURCE_ROWS}
        self.old_sources = [copy.deepcopy(previous.get(helper.PREFIX + name) or
                            storage.record_file(ROOT / helper.PREFIX / name, helper.PREFIX + name))
                            for name in helper.SOURCE_NAMES]
        now = {row["path"]: row for row in helper.CURRENT_SOURCE_ROWS}
        self.current = [copy.deepcopy(now.get(row["path"], row)) for row in self.old_sources]
        self.f.input["mod"]["files"] = self.old_sources
        self.f.input["proceduralBackend"] = helper.BACKEND
        self.f.values[core.INPUT] = storage.canonical(self.f.input)
        self.old_rows = []
        self.new_rows = []
        self.replacements = {}
        for index, name in enumerate(helper.NATIVE_NAMES):
            def body(marker):
                data = bytearray(64)
                data[:7] = b"\x7fELF\x02\x01\x01"
                struct.pack_into("<HHI", data, 16, 3, 183, 1)
                return bytes(data) + marker
            member = "lib/arm64-v8a/" + name
            old, new = body(b"old" + bytes([index])), body(b"new" + bytes([index]))
            record = lambda value: {"path": "Assets/Quest/Plugins/Android/arm64-v8a/" + name,
                                    "size": len(value), "sha256": hashlib.sha256(value).hexdigest()}
            self.old_rows.append(record(old)); self.new_rows.append(record(new))
            self.f.values[member] = old; self.replacements[member] = new
        self.contract = {"schema": 1, "backend": helper.BACKEND, "files": self.old_rows}
        self.f.values[core.PROTON_CONTRACT] = storage.canonical(self.contract)
        self.f.write_zip(self.f.base, self.f.values)
        self.evidence = {"schema": 1, "scope": "proton-nls-helper-update-v1", "backend": helper.BACKEND,
            "sourceFiles": self.current, "nativeFiles": self.new_rows, "previousNativeFiles": self.old_rows,
            "androidDeviceExecutionVerified": False, "wineBinaryChanged": False, "winePayloadChanged": False,
            "originalGameAssetsRebuilt": False}
        self.provenance = {**self.f.provenance, "mod": {"files": self.current}, "nativeHelpers": self.evidence}
        new_contract = {**self.contract, "files": self.new_rows, "helperUpdate": self.evidence}
        self.replacements[core.PROTON_CONTRACT] = storage.canonical(new_contract)
        # Stand in only for the independent Unity object serializer's witnessed
        # output. The real ZIP writer and all native publication checks run.
        resource = "assets/bin/Data/resources.assets"
        self.replacements[resource] = b"inert serialized Resources witness"
        proof = (core._base_stamp(self.f.base), resource, hashlib.sha256(self.replacements[resource]).hexdigest())
        core._resource_proofs.add(proof)
        core._resource_value_proofs[proof] = {"quest-procedural-native": hashlib.sha256(core._json_bytes(new_contract)).hexdigest()}

    def publish(self, *, evidence=True):
        return core.repack(self.f.base, self.f.output, self.replacements,
            update_manifest=self.provenance, signer=self.sign, verifier=self.verify,
            native_helper_evidence=self.evidence if evidence else None)

    def test_actual_repack_keeps_game_compressed_bytes_and_replaces_only_proven_helpers(self):
        self.sign, self.verify = Mock(), Mock(return_value=None)
        original = self.f.base.read_bytes()
        result = self.publish()
        self.sign.assert_called_once(); self.verify.assert_called_once()
        self.assertEqual(self.f.base.read_bytes(), original)
        self.assertEqual(set(result["replacedMembers"]), {core.UPDATE, *self.replacements})
        for name in ("assets/bin/Data/level0", "assets/game-textures.resource"):
            self.assertEqual(self.f.compressed(self.f.base, name), self.f.compressed(self.f.output, name))
        with zipfile.ZipFile(self.f.output) as archive:
            for name, value in self.replacements.items(): self.assertEqual(archive.read(name), value)

    def test_partial_or_unqualified_helpers_never_reach_signing_or_overwrite_output(self):
        mutations = [lambda: self.replacements.pop(next(iter(core.PROTON_HELPERS))),
            lambda: self.replacements.pop(core.PROTON_CONTRACT),
            lambda: self.replacements.pop("assets/bin/Data/resources.assets"),
            lambda: self.evidence.update(scope="unknown"),
            lambda: self.evidence.update(androidDeviceExecutionVerified=True),
            lambda: self.evidence["nativeFiles"][0].update(sha256="f" * 64),
            lambda: self.evidence["previousNativeFiles"][0].update(sha256="f" * 64),
            lambda: self.current[0].update(sha256="f" * 64),
            lambda: self.evidence.update(sourceFiles=[]),
            lambda: self.replacements.update({"lib/arm64-v8a/unrelated.so": b"unexpected"}),
            lambda: self.replacements.update({core.PROTON_CONTRACT: b"{}"})]
        for change in mutations:
            with self.subTest(mutation=change):
                # Each transaction starts from the same independently owned tiny APK.
                self.setUp()
                self.sign, self.verify = Mock(), Mock()
                self.f.output.write_bytes(b"previous completed APK")
                before = self.f.base.read_bytes()
                change()
                with self.assertRaises(storage.BuildError): self.publish()
                self.sign.assert_not_called(); self.verify.assert_not_called()
                self.assertEqual(self.f.base.read_bytes(), before)
                self.assertEqual(self.f.output.read_bytes(), b"previous completed APK")

    def test_absent_evidence_cannot_publish_native_members(self):
        self.sign, self.verify = Mock(), Mock()
        with self.assertRaisesRegex(storage.BuildError, "qualified static"): self.publish(evidence=False)
        self.sign.assert_not_called()

    def test_actual_resource_patch_composition_keeps_native_value_proof(self):
        # Adapt only Unity's binary object serialization; exercise the actual
        # resource registry, target/readback checks and proof composition.
        names = {"1": "quest-procedural-native", "2": "quest-build", "3": "unrelated"}
        bodies = {key: {"m_Name": name, "m_Script": "{}"} for key, name in names.items()}
        class File:
            def __init__(self, values): self.values = values; self.externals = [SimpleNamespace(path="resources.assets")]
            def save(self): return storage.canonical(self.values)
        class Obj:
            def __init__(self, owner, key): self.owner = owner; self.path_id = int(key); self.key = key; self.type = SimpleNamespace(name="TextAsset")
            def read_typetree(self): return copy.deepcopy(self.owner.values[self.key])
            def save_typetree(self, fields): self.owner.values[self.key] = copy.deepcopy(fields)
            def get_raw_data(self): return storage.canonical(self.owner.values[self.key])
        def load(raw):
            file = File({} if raw == b"inert registry" else json.loads(raw))
            if raw == b"inert registry":
                manager = SimpleNamespace(type=SimpleNamespace(name="ResourceManager"),
                    read_typetree=lambda: {"m_Container": [(name, {"m_FileID": 1, "m_PathID": int(key)}) for key, name in names.items()]})
                return SimpleNamespace(files={"file": file}, objects=[manager])
            return SimpleNamespace(files={"file": file}, objects=[Obj(file, key) for key in file.values])
        self.f.values["assets/bin/Data/globalgamemanagers"] = b"inert registry"
        self.f.values["assets/bin/Data/resources.assets"] = storage.canonical(bodies)
        self.f.write_zip(self.f.base, self.f.values)
        contract = json.loads(self.replacements[core.PROTON_CONTRACT])
        with patch.dict(sys.modules, {"UnityPy": SimpleNamespace(load=load)}):
            result = core.patch_resource_json(self.f.base, {"quest-procedural-native": contract})
            result = core.patch_resource_json(self.f.base, {"quest-build": {"modBuild": 673}}, replacements=result)
            self.replacements.update(result)
            self.sign, self.verify = Mock(), Mock(return_value=None)
            self.publish()
            self.sign.assert_called_once()
            # A subsequent qualified generic TextAsset edit cannot conceal loss
            # of the actual native contract, even in the same serialized file.
            self.replacements.update(core.patch_resource_json(self.f.base, {"quest-procedural-native": {}}, replacements=result))
            with self.assertRaisesRegex(storage.BuildError, "composed TextAsset proof"): self.publish()

    def test_only_the_exact_reviewed_native_transition_is_compact(self):
        self.assertEqual(driver.rebuild_reasons(self.current, self.old_sources, procedural_backend=helper.BACKEND), [])
        changed = copy.deepcopy(self.current); changed[0]["sha256"] = "f" * 64
        self.assertTrue(driver.rebuild_reasons(changed, self.old_sources, procedural_backend=helper.BACKEND))
        for backend in (None, "box64-wine9"):
            self.assertTrue(driver.rebuild_reasons(self.current, self.old_sources, procedural_backend=backend))
        for path in ("protocol.h", "worker.c", "proton.lock.json"):
            changed = copy.deepcopy(self.current)
            next(row for row in changed if row["path"] == helper.PREFIX + path)["sha256"] = "f" * 64
            self.assertTrue(driver.rebuild_reasons(changed, self.old_sources, procedural_backend=helper.BACKEND))
        selected = driver._update_source_rows(self.current + [storage.record_file(ROOT / helper.PREFIX / name, helper.PREFIX + name)
                 for name in ("runtime.py", "upstream.lock.json")], art_changed=False, native_changed=True)
        self.assertEqual({row["path"] for row in selected}, {helper.PREFIX + name for name in (*helper.SOURCE_NAMES, "runtime.py", "upstream.lock.json")})


if __name__ == "__main__": unittest.main()
