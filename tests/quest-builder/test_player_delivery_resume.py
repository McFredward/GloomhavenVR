"""Completed Unity artifacts resume delivery without preparation or byte sweeps."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import player_delivery
import storage


class CompletedPlayerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="completed Player & spaces ")
        self.root = Path(self.temp.name)
        self.output = self.root / "output"
        self.data = self.root / "owned/Gloomhaven_Data"
        for relative in (*player_delivery._ORIGINALS, "resources.assets", "StreamingAssets/Rulebase/rules.json"):
            path = self.data / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"original bytes " + relative.encode())
        self.output.mkdir()
        # This is the real persistent inventory producer, not a fabricated
        # metadata proof. Selection must use its indexed records without a read.
        original_files = storage.persistent_inventory(self.data, self.output / "cache/input-inventories/game.sqlite3")
        game_key = storage.value_hash({"files": original_files})
        profile = {"schema": 1, "provider": "steam", "steamId": "76561198000000001",
                   "accountId": 39734273, "displayName": "Local Player", "source": "explicit-local-profile"}
        source_files = [{"path": "src/GloomhavenVR/Net/NetProtocol.cs", "size": 1, "sha256": "2" * 64}]
        self.inputs = {"schema": 1, "recipe": 1, "target": "game",
                       "game": {"key": game_key, "unityVersion": "2021.3.5f1", "files": original_files},
                       "mod": {"key": storage.value_hash({"files": source_files}), "commit": "b" * 40,
                               "dirty": False, "modBuild": 665, "files": source_files},
                       "profile": profile, "profileKey": storage.value_hash(profile), "probeAssets": None,
                       "startupProject": None, "campaignProject": None, "proceduralBackend": "proton-arm64ec-fex"}
        self.inputs["inputKey"] = storage.value_hash(self.inputs)
        self.source = self.output / "inputs/mod" / self.inputs["mod"]["key"]
        self.project = self.output / "projects" / builder.import_workspace.workspace_key(self.inputs, "game")
        self.source.mkdir(parents=True); self.project.mkdir(parents=True)
        self.tools = {"key": "c" * 64, "unityVersion": "2021.3.5f1", "buildDriverSha256": "d" * 64,
                      "java": str(self.root / "jdk/bin/java"), "apksigner": str(self.root / "sdk/build-tools/30.0.2/lib/apksigner.jar"),
                      "aapt": str(self.root / "sdk/build-tools/30.0.2/aapt"), "jdk": str(self.root / "jdk")}
        self.provenance = {"schema": 1, "runtime": {"sourceCommit": "b" * 40, "modBuild": 665, "inputKey": self.inputs["inputKey"]},
                           "toolchain": {name: self.tools[name] for name in ("key", "unityVersion", "buildDriverSha256")},
                           "buildDriverModules": [], "stagedEditorSources": []}
        self.key = storage.value_hash({"input": self.inputs["inputKey"], "toolchain": self.tools["key"], "recipe": 1,
                                      "buildProvenance": storage.value_hash(self.provenance), "validateCampaignShaders": False})
        self.folder = self.output / "builds" / self.key; self.folder.mkdir(parents=True)
        self.apk = self.folder / "GloomhavenVR-Quest.apk"
        self.report = self.folder / "GloomhavenVR-Quest.apk.build.json"
        self.bank = self.folder / "GloomhavenVR-Quest-content.zip"
        with zipfile.ZipFile(self.bank, "w") as archive:
            archive.writestr("StreamingAssets/a.bundle", b"original bank")
        self.bank_sha = storage.digest(self.bank)
        identity = self.output / "identities" / self.inputs["profileKey"] / "quest-profile.json"
        storage.write_json(identity, profile)
        self.metadata = {"schema": 1, "target": "game", "inputKey": self.inputs["inputKey"], "package": builder.PACKAGE,
                         "profileSha256": storage.digest(identity), "unityVersion": "2021.3.5f1", "buildResult": "Succeeded",
                         "graphicsApi": "Vulkan", "proceduralBackend": "proton-arm64ec-fex", "stagedProceduralNativeFiles": []}
        self.members = {"assets/Quest/input-manifest.json": storage.canonical(self.inputs),
                        "assets/Quest/installation-manifest.json": storage.canonical({"schema": 1, "inputKey": self.inputs["inputKey"],
                            "game": {"inputKey": self.inputs["inputKey"], "archiveSha256": self.bank_sha}, "mod": {}}),
                        "assets/Quest/content-delivery.json": storage.canonical({"schema": 1, "inputKey": self.inputs["inputKey"],
                            "package": builder.PACKAGE, "files": [{"file": self.bank.name, "archive": "quest-startup-content.zip",
                            "sha256": self.bank_sha, "size": self.bank.stat().st_size}]}),
                        "assets/Quest/procedural-native.json": storage.canonical({"schema": 1, "backend": "proton-arm64ec-fex", "files": []}),
                        "AndroidManifest.xml": b"actual binary manifest is final delivery's scope",
                        "assets/bin/Data/Managed/Metadata/global-metadata.dat": struct.pack("<II", 0xFAB11BAF, 27)}
        elf = bytearray(64); elf[:7] = b"\x7fELF\x02\x01\x01"; struct.pack_into("<HHI", elf, 16, 3, 183, 1)
        for name in ("libil2cpp.so", "libunity.so", "libghvr_quest_passthrough.so", "libUnityOpenXR.so", "libopenxr_loader.so"):
            self.members["lib/arm64-v8a/" + name] = bytes(elf)
        self.publish()

    def tearDown(self):
        self.temp.cleanup()

    def publish(self):
        storage.write_json(self.output / "manifests" / (self.inputs["inputKey"] + ".json"), self.inputs)
        storage.write_json(self.output / "latest-input.json", {"manifest": "manifests/" + self.inputs["inputKey"] + ".json",
                                                           "sourceGameRoot": str(self.data), "sourceRepo": str(self.root / "old-source")})
        storage.write_json(self.output / "toolchain.json", self.tools)
        storage.write_json(self.folder / "build-provenance.json", self.provenance)
        storage.write_json(self.report, self.metadata)
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in self.members.items(): archive.writestr(name, value)

    def select(self, **kwargs):
        return player_delivery.select(builder, self.output, self.data, **kwargs)

    def record(self):
        return player_delivery.record(builder, self.output, self.inputs, self.source, self.project, self.tools, self.key, self.provenance)

    def test_actual_legacy_completed_player_reuses_frozen_665_and_never_prepares(self):
        with patch.object(builder, "inspect_inputs", side_effect=AssertionError("inspect forbidden")), \
             patch.object(builder, "snapshot_inputs", side_effect=AssertionError("snapshot forbidden")), \
             patch.object(builder, "prepare", side_effect=AssertionError("prepare forbidden")), \
             patch.object(builder, "weave", side_effect=AssertionError("weave forbidden")), \
             patch.object(builder, "toolchain", side_effect=AssertionError("Unity version forbidden")), \
             patch.object(storage, "digest", side_effect=AssertionError("hash sweep forbidden")), \
             patch.object(zipfile.ZipFile, "testzip", side_effect=AssertionError("ZIP byte sweep forbidden")), \
             patch.object(Path, "rglob", side_effect=AssertionError("tree scan forbidden")):
            selected = self.select()
        self.assertEqual(selected["resumeKind"], "legacy-completed-player")
        self.assertEqual(selected["modBuild"], 665)
        self.assertEqual(selected["inputKey"], self.inputs["inputKey"])
        self.assertEqual(selected["source"], self.source)
        self.assertTrue(selected["proof"]["finalValidationRequired"])

    def test_checkpoint_survives_capability_and_report_publication(self):
        self.record()
        self.members["assets/Quest/offline-profile.json"] = storage.canonical(self.inputs["profile"])
        self.metadata["buildProvenance"] = self.provenance
        self.metadata["updateCapability"] = {"schema": 1, "dynamicProfile": True}
        self.publish()
        selected = self.select(input_key=self.inputs["inputKey"])
        self.assertEqual(selected["resumeKind"], "checkpoint")
        self.assertEqual(selected["key"], self.key)

    def test_actual_cold_wizard_loader_needs_no_dependency_aliases(self):
        source = """import pathlib,sys
repo=pathlib.Path(sys.argv[1]); sys.path.insert(0,str(repo/'tools/quest-wizard'))
import discovery
builder=discovery.builder(repo)
assert 'storage' not in sys.modules and 'import_workspace' not in sys.modules
selected=builder._local_helper('player_delivery').select(builder,pathlib.Path(sys.argv[2]),pathlib.Path(sys.argv[3]))
assert selected['modBuild']==665 and selected['inputKey']==sys.argv[4]
print('cold loader selected completed Player without dependency aliases')
"""
        result = subprocess.run([sys.executable, "-I", "-B", "-c", source, str(ROOT), str(self.output),
                                 str(self.data), self.inputs["inputKey"]], capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_zip64_completed_apk_has_bounded_real_central_directory(self):
        with patch.object(zipfile, "ZIP64_LIMIT", 0):
            self.publish()
        self.assertEqual(self.select()["modBuild"], 665)

    def test_bad_il2cpp_metadata_is_not_a_completed_player(self):
        self.members["assets/bin/Data/Managed/Metadata/global-metadata.dat"] = b"invalid!"; self.publish()
        with self.assertRaisesRegex(builder.BuildError, "invalid IL2CPP metadata"):
            self.select()

    def test_no_artifact_returns_none_before_requiring_prepared_source(self):
        self.apk.unlink(); shutil.rmtree(self.source); shutil.rmtree(self.project)
        self.assertIsNone(self.select())

    def test_failed_unity_report_cannot_be_adopted(self):
        self.metadata["buildResult"] = "Failed"; self.publish()
        self.assertIsNone(self.select())

    def test_successful_report_alone_cannot_authorize_incomplete_apk(self):
        self.apk.write_bytes(b"PK\x03\x04 interrupted")
        with self.assertRaisesRegex(builder.BuildError, "truncated"):
            self.select()
        self.assertTrue(self.apk.exists())

    def test_missing_actual_arm64_payload_refuses_without_delete(self):
        del self.members["lib/arm64-v8a/libil2cpp.so"]; self.publish()
        with self.assertRaisesRegex(builder.BuildError, "ARM64 IL2CPP"):
            self.select()
        self.assertTrue(self.apk.exists())

    def test_changed_embedded_inputs_refuse_frozen_report_and_provenance(self):
        changed = copy.deepcopy(self.inputs); changed["mod"]["modBuild"] = 667
        self.members["assets/Quest/input-manifest.json"] = storage.canonical(changed); self.publish()
        with self.assertRaisesRegex(builder.BuildError, "frozen input"):
            self.select()

    def test_wrong_requested_input_refuses(self):
        with self.assertRaisesRegex(builder.BuildError, "requested retained input"):
            self.select(input_key="f" * 64)

    def test_changed_provider_profile_refuses(self):
        path = self.root / "profile.json"
        changed = dict(self.inputs["profile"], displayName="Other Player"); storage.write_json(path, changed)
        with self.assertRaisesRegex(builder.BuildError, "Selected account"):
            self.select(profile_path=path)

    def test_unmodified_wizard_profile_without_dlc_capture_is_accepted(self):
        path = self.root / "profile.json"; storage.write_json(path, self.inputs["profile"])
        self.assertEqual(self.select(profile_path=path)["modBuild"], 665)

    def test_original_metadata_change_refuses_without_hash_read(self):
        path = self.data / "Managed/GH.Runtime.dll"; payload = path.read_bytes()
        path.write_bytes(bytes([payload[0] ^ 1]) + payload[1:])
        with patch.object(storage, "digest", side_effect=AssertionError("original hash forbidden")):
            with self.assertRaisesRegex(builder.BuildError, "installation changed"):
                self.select()

    def test_changed_owned_installation_directory_refuses_same_contents(self):
        other = self.root / "other-owned/Gloomhaven_Data"; shutil.copytree(self.data, other)
        with self.assertRaisesRegex(builder.BuildError, "same locally owned PC"):
            player_delivery.select(builder, self.output, other)

    def test_external_bank_size_change_refuses(self):
        with self.bank.open("ab") as stream: stream.write(b"changed")
        with self.assertRaisesRegex(builder.BuildError, "bank is missing or changed"):
            self.select()

    def test_frozen_build_toolchain_change_refuses(self):
        self.tools["unityVersion"] = "2021.3.6f1"; self.publish()
        with self.assertRaisesRegex(builder.BuildError, "toolchain receipt changed"):
            self.select()

    def test_changed_build_provenance_refuses_successful_report(self):
        self.provenance["runtime"]["modBuild"] = 667; self.publish()
        with self.assertRaisesRegex(builder.BuildError, "frozen runtime"):
            self.select()

    def test_linked_completed_artifact_cannot_be_selected(self):
        copy_path = self.root / "foreign.apk"; self.apk.replace(copy_path); self.apk.symlink_to(copy_path)
        with self.assertRaisesRegex(builder.BuildError, "links"):
            self.select()

    def test_pointer_path_escape_is_rejected(self):
        storage.write_json(self.output / "latest-input.json", {"manifest": "../foreign.json"})
        with self.assertRaisesRegex(builder.BuildError, "unsafe owned path"):
            self.select()

    def test_duplicate_zip_identity_is_rejected(self):
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(self.apk, "a") as archive:
                archive.writestr("assets/Quest/input-manifest.json", storage.canonical(self.inputs))
        with self.assertRaisesRegex(builder.BuildError, "duplicate members"):
            self.select()

    def test_incomplete_immutable_manifest_cannot_rebind_same_input_key(self):
        self.inputs["profile"]["displayName"] = "Another account"; self.publish()
        with self.assertRaisesRegex(builder.BuildError, "immutable input/profile"):
            self.select()


if __name__ == "__main__":
    unittest.main()
