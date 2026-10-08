"""Fixture contracts for compressed payload reuse; no APK execution claim."""
import copy
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import apk_update as update
import storage


class ApkUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.base, self.output = self.root / "base.apk", self.root / "updated.apk"
        self.game = self.root / "game"
        rows = []
        for name in ("GH.Runtime.dll", "GH.Shared.dll"):
            data = ("owned-original:" + name).encode(); path = self.game / "Managed" / name
            path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
            rows.append({"path": "Managed/" + name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})
        self.input = {"schema": 1, "target": "game", "inputKey": "a" * 64, "game": {"files": rows}, "mod": {"modBuild": 643}}
        self.row = {"assembly": "GH.Runtime.dll", "namespace": "", "class": "OriginalWidget", "propertiesHash": "1" * 32, "executionOrder": 0}
        self.capsule = {"schema": 1, "scope": "quest-code-update-v1", "inputKey": "a" * 64, "unityVersion": "2021.3.5f1",
                        "packageAbi": "b" * 64, "metadataVersion": 29, "managedKey": storage.value_hash(update._managed(self.input)),
                        "scriptTypes": [self.row], "modSchema": 1, "dynamicProfile": True}
        self.library = bytearray(64); self.library[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", self.library, 18, 183)
        self.metadata = struct.pack("<II", 0xFAB11BAF, 29) + b"fixture metadata"
        self.values = {update.INPUT: json.dumps(self.input).encode(), update.INSTALLATION: json.dumps({"schema": 1, "inputKey": "a" * 64}).encode(),
                       update.CAPSULE: json.dumps(self.capsule).encode(), update.LIBRARY: bytes(self.library), update.METADATA: self.metadata,
                       update.INITIALIZERS: b'{"root":[]}', update.ASSEMBLIES: b'{"names":["GH.Runtime.dll"],"types":[16]}',
                       "assets/bin/Data/level0": b"unchanged serialized scene" * 1000,
                       "assets/game-textures.resource": b"original texture bytes" * 10000,
                       "META-INF/MANIFEST.MF": b"old signature", "META-INF/CERT.RSA": b"old signature certificate", "META-INF/NOTICE": b"unrelated metadata"}
        self.write_zip(self.base, self.values)
        self.provenance = {"schema": 1, "gameInputKey": "a" * 64, "updateKey": "c" * 64, "modBuild": 644}

    def tearDown(self): self.temp.cleanup()

    @staticmethod
    def write_zip(path, values):
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as apk:
            for name, data in values.items(): apk.writestr(name, data)

    @staticmethod
    def compressed(path, name):
        with zipfile.ZipFile(path) as apk, path.open("rb") as raw:
            info = apk.getinfo(name); raw.seek(info.header_offset); header = raw.read(30)
            fields = struct.unpack("<4s5H3I2H", header); raw.seek(fields[-2] + fields[-1], 1)
            return raw.read(info.compress_size)

    def publish(self, replacements, **kwargs):
        return update.repack(self.base, self.output, replacements, update_manifest=self.provenance,
                             signer=lambda _: None, verifier=lambda _: None, **kwargs)

    def test_compressed_game_payloads_and_unrelated_metadata_are_retained(self):
        seen = []
        result = self.publish({update.PROFILE: b'{"profile":"replacement"}'}, progress=lambda *row: seen.append(row))
        with zipfile.ZipFile(self.output) as apk:
            self.assertIsNone(apk.testzip()); self.assertNotIn("META-INF/CERT.RSA", apk.namelist())
            self.assertEqual(apk.read("META-INF/NOTICE"), b"unrelated metadata")
            self.assertEqual(json.loads(apk.read(update.UPDATE)), self.provenance)
        for name in ("assets/bin/Data/level0", "assets/game-textures.resource"):
            self.assertEqual(self.compressed(self.base, name), self.compressed(self.output, name))
        self.assertGreater(result["preservedCompressedBytes"], 0); self.assertEqual(seen[-1][0], seen[-1][1])

    def test_signature_failure_keeps_previous_output_and_input(self):
        self.output.write_bytes(b"previous published result"); before = self.base.read_bytes()
        def fail(_): raise storage.BuildError("controlled signer failed")
        with self.assertRaisesRegex(storage.BuildError, "signer failed"):
            update.repack(self.base, self.output, {}, update_manifest=self.provenance, signer=fail, verifier=lambda _: None)
        self.assertEqual(self.output.read_bytes(), b"previous published result"); self.assertEqual(self.base.read_bytes(), before)
        self.assertEqual(list(self.root.glob("updated.apk.*.apk")), [])

    def test_il2cpp_pair_or_unqualified_code_cannot_be_published(self):
        with self.assertRaisesRegex(storage.BuildError, "together"): self.publish({update.LIBRARY: bytes(self.library)})
        with self.assertRaisesRegex(storage.BuildError, "qualification"): self.publish({update.LIBRARY: bytes(self.library), update.METADATA: self.metadata})
        self.assertFalse(self.output.exists())

    def test_unrelated_payload_replacement_and_wrong_game_key_are_rejected(self):
        with self.assertRaisesRegex(storage.BuildError, "scope"): self.publish({"assets/bin/Data/level0": b"changed scene"})
        self.provenance["gameInputKey"] = "d" * 64
        with self.assertRaisesRegex(storage.BuildError, "identity"): self.publish({})

    def test_unqualified_guid_named_resource_bytes_cannot_be_published(self):
        with self.assertRaisesRegex(storage.BuildError, "native-registry/object qualification"):
            self.publish({"assets/bin/Data/" + "f" * 32: b"unverified serialized object bytes"})

    def test_profile_rejects_credentials_and_cross_account_dlc_flags(self):
        profile = {"schema": 1, "provider": "steam", "steamId": "76561197960265729", "accountId": 1, "displayName": "Owner"}
        with self.assertRaisesRegex(storage.BuildError, "credentials"): update.profile_update(self.base, {**profile, "token": "private"})
        ownership = {"schema": 1, "provider": "steam", "steamId": "76561197960265730", "appId": 780290, "ownedMask": 1, "installedAppIds": [1809490]}
        with self.assertRaisesRegex(storage.BuildError, "selected account"): update.profile_update(self.base, {**profile, "dlcOwnership": ownership})
        ownership["steamId"] = profile["steamId"]
        self.assertEqual(json.loads(update.profile_update(self.base, {**profile, "dlcOwnership": ownership})[update.PROFILE])["dlcOwnership"], ownership)
        ownership["installedAppIds"] = [1809490, 1809490]
        with self.assertRaisesRegex(storage.BuildError, "selected account"): update.profile_update(self.base, {**profile, "dlcOwnership": ownership})

    def test_owned_assembly_inputs_required_on_every_update(self):
        self.assertEqual(update.preflight_owned_game(self.base, self.game)["managedKey"], self.capsule["managedKey"])
        (self.game / "Managed/GH.Runtime.dll").write_bytes(b"wrong owned game")
        with self.assertRaisesRegex(storage.BuildError, "does not match"): update.preflight_owned_game(self.base, self.game)

    def test_duplicate_members_and_metadata_capsule_mismatch_are_rejected(self):
        with zipfile.ZipFile(self.base, "a") as apk: apk.writestr(update.INPUT.upper(), b"ambiguous")
        with self.assertRaisesRegex(storage.BuildError, "duplicate"): update.inspect_base(self.base)
        self.capsule["metadataVersion"] = 28; self.values[update.CAPSULE] = json.dumps(self.capsule).encode(); self.write_zip(self.base, self.values)
        with self.assertRaisesRegex(storage.BuildError, "metadata format"): update.inspect_base(self.base)

    def test_legacy_profile_requires_one_code_migration_and_future_profile_needs_no_compiler(self):
        del self.values[update.CAPSULE]; self.write_zip(self.base, self.values)
        profile = {"schema": 1, "provider": "steam", "steamId": "76561197960265729", "accountId": 1, "displayName": "Owner"}
        with self.assertRaisesRegex(storage.BuildError, "compiled profile constants"): update.profile_update(self.base, profile)
        self.values[update.CAPSULE] = json.dumps(self.capsule).encode(); self.write_zip(self.base, self.values)
        with patch.object(update, "stage_code_project", side_effect=AssertionError("Profile-only update must not invoke Unity")):
            replacements = update.profile_update(self.base, profile); self.publish(replacements)
        self.assertEqual(update.inspect_base(self.output)["modBuild"], 644)

    def test_serialized_class_changes_are_actionable_full_build_errors(self):
        update._required_types([self.row], [dict(self.row), {**self.row, "class": "NewModWidget"}])
        for changed in ([{**self.row, "propertiesHash": "2" * 32}], [], [{**self.row, "executionOrder": 1}]):
            with self.assertRaisesRegex(storage.BuildError, "full build"): update._required_types([self.row], changed)

    def test_real_compiler_shaped_receipt_closes_pair_sidecars_and_profile_boundary(self):
        compiled = self.root / "code.apk"
        values = dict(self.values)
        callback = {"assemblyName": "QuestGame.Campaign", "nameSpace": "GloomhavenVR.Quest", "className": "QuestOfflineProfile", "methodName": "Initialize", "loadTypes": 1, "isUnityClass": False}
        values[update.INITIALIZERS] = json.dumps({"root": [callback]}).encode(); self.write_zip(compiled, values)
        receipt = {"schema": 1, "scope": "quest-code-update-v1", "abi": "arm64-v8a", "buildResult": "Succeeded", "originalAssetsImported": False,
                   "unityVersion": "2021.3.5f1", "packageAbi": "b" * 64,
                   "files": [{"path": n, "size": len(values[n]), "sha256": hashlib.sha256(values[n]).hexdigest()} for n in (update.LIBRARY, update.METADATA)]}
        with patch.object(update, "script_roster", return_value={"unityVersion": "2021.3.5f1", "scriptTypes": [self.row]}):
            qualified = update.collect_code_update(self.base, compiled, receipt, package_abi="b" * 64)
            self.publish(qualified["replacements"], code_evidence=qualified)
            broken = copy.deepcopy(receipt); broken["files"][0]["sha256"] = "0" * 64
            with self.assertRaisesRegex(storage.BuildError, "closed compiler receipt"): update.collect_code_update(self.base, compiled, broken, package_abi="b" * 64)
        qualified["replacements"][update.METADATA] += b"late changed bytes"
        with self.assertRaisesRegex(storage.BuildError, "changed before"): self.publish(qualified["replacements"], code_evidence=qualified)

    def test_code_project_retains_owned_library_and_closed_staging_between_retries(self):
        repo, project = self.root / "source", self.root / "code-project"
        template = repo / "unity/GloomhavenVR.Quest"
        for name, body in {"ProjectSettings/ProjectSettings.asset": "private settings", "Packages/manifest.json": '{"dependencies":{}}',
                           "Assets/Quest/Runtime/QuestOfflineProfile.cs": "runtime", "Assets/Quest/Editor/QuestCodeUpdateBuild.cs": "helper"}.items():
            path = template / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(body)
        text = repo / "src/GloomhavenVR/Core/Loc/QuestText.cs"; text.parent.mkdir(parents=True); text.write_text("namespace GloomhavenVR.Core {}")
        for name in ("UnityEngine.SpatialTracking.dll", "Unity.TextMeshPro.dll", "System.Runtime.CompilerServices.Unsafe.dll", "System.Core.dll", "UnityEngine.CoreModule.dll"):
            (self.game / "Managed" / name).write_bytes(b"owned plugin classification fixture")
        self.values[update.ASSEMBLIES] = b'{"names":["GH.Runtime.dll","UnityEngine.SpatialTracking.dll","System.Runtime.CompilerServices.Unsafe.dll","System.Core.dll"],"types":[16,16,16,2]}'
        self.write_zip(self.base, self.values)
        arguments = dict(profile={"displayName": "Owner"}, version_code=644, version_name="B644", package_abi="b" * 64, code_key="c" * 64)
        first = update.stage_code_project(repo, project, self.base, self.game / "Managed", **arguments)
        plugin_names = {p.name for p in (project / "Assets/Plugins/QuestGame").glob("*.dll")}
        self.assertTrue({"UnityEngine.SpatialTracking.dll", "Unity.TextMeshPro.dll", "System.Runtime.CompilerServices.Unsafe.dll"}.issubset(plugin_names))
        self.assertFalse({"System.Core.dll", "UnityEngine.CoreModule.dll"} & plugin_names)
        library = project / "Library/imported"; library.parent.mkdir(); library.write_bytes(b"expensive retained compile state")
        plugin = project / "Assets/Plugins/QuestGame/GH.Runtime.dll"; plugin.write_bytes(b"already woven replacement")
        self.assertIn("isExplicitlyReferenced: 1", plugin.with_suffix(".dll.meta").read_text())
        self.assertEqual((project / "Assets/Quest/Runtime/QuestText.cs").read_text(), "namespace GloomhavenVR.Core {}")
        second = update.stage_code_project(repo, project, self.base, self.game / "Managed", **arguments)
        self.assertFalse(first["reused"]); self.assertTrue(second["reused"])
        self.assertEqual(library.read_bytes(), b"expensive retained compile state"); self.assertEqual(plugin.read_bytes(), b"already woven replacement")
        with self.assertRaisesRegex(storage.BuildError, "different update"):
            update.stage_code_project(repo, project, self.base, self.game / "Managed", **{**arguments, "code_key": "d" * 64})

    def test_local_central_name_mismatch_is_rejected_before_signing(self):
        raw = bytearray(self.base.read_bytes())
        with zipfile.ZipFile(self.base) as apk: offset = apk.getinfo("assets/bin/Data/level0").header_offset + 30
        raw[offset] = ord("x"); self.base.write_bytes(raw)
        with self.assertRaisesRegex(storage.BuildError, "member paths disagree"): self.publish({})


if __name__ == "__main__": unittest.main()
