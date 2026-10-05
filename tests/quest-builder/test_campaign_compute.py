"""Exercise delivery closure across actual ZIP entries and repeated compiled objects."""
import json
import copy
import hashlib
import struct
from pathlib import Path
import sys
import tempfile
import types
import unittest
import zipfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import campaign_compute
from storage import BuildError


class DeliveredComputeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.apk, self.bank = self.root / "player.apk", self.root / "content.zip"
        self.a = {"m_Name": "EyeHistogram", "variants": ["native Vulkan bytes A"]}
        self.b = {"m_Name": "Waveform", "variants": ["native Vulkan bytes B"]}
        self.manifest = {"shaders": [{"name": row["m_Name"]} for row in (self.a, self.b)]}

    def tearDown(self):
        self.temp.cleanup()

    def write(self, bank):
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("assets/bin/Data/resources.assets", self.serialized([self.a]))
            archive.writestr("assets/unrelated.json", "not a serialized player asset")
        with zipfile.ZipFile(self.bank, "w") as archive:
            archive.writestr("StreamingAssets/aa/Android/native.bundle", json.dumps(bank))
            archive.writestr("StreamingAssets/Rulebase/unused.yml", "not a native bank")

    def collect(self):
        return campaign_compute.collect_delivered(self.apk, [self.bank], self.manifest, self.decode)

    @staticmethod
    def serialized(objects, version=22, endian=0):
        # The payload decoder is a fixture. The real header/size/offset contract
        # is exercised independently of JSON's synthetic object representation.
        payload = json.dumps(objects).encode()
        if version == 22:
            return struct.pack(">IIII", 0, 0, version, 0) + bytes([endian, 0, 0, 0]) + struct.pack(
                ">IQQQ", 16, 64 + len(payload), 64, 0) + b"M" * 16 + payload
        return struct.pack(">IIII", 44, 64 + len(payload), version, 64) + bytes([
            endian, 0, 0, 0]) + b"M" * 44 + payload

    @staticmethod
    def decode(data):
        if data.startswith(b"["):
            return json.loads(data)
        return json.loads(data[64:])

    def test_reads_both_delivered_sources_and_deduplicates_exact_objects(self):
        self.write([self.a, self.b])
        objects, proof = self.collect()
        self.assertEqual({row["m_Name"] for row in objects}, {"EyeHistogram", "Waveform"})
        self.assertEqual(proof["actualSerializedPayloadCount"], 2)
        self.assertEqual(len(proof["actualObjectOrigins"]), 3)

    def test_missing_object_cannot_pass_a_valid_apk_and_bank(self):
        self.write([self.a])
        with self.assertRaisesRegex(BuildError, "omit original.*Waveform"):
            self.collect()

    def test_conflicting_cooked_duplicates_are_rejected(self):
        self.write([{**self.a, "variants": ["different native bytes"]}, self.b])
        with self.assertRaisesRegex(BuildError, "different compiled programs"):
            self.collect()

    def test_unowned_object_is_not_ignored(self):
        self.write([self.b, {"m_Name": "InventedReplacement"}])
        with self.assertRaisesRegex(BuildError, "unowned compute"):
            self.collect()

    def test_hashed_game_resource_contains_the_missing_original_compute(self):
        self.write([self.b])
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("assets/bin/Data/14b830dd8a5381e4399cfe161a01662f", self.serialized([self.a]))
            archive.writestr("assets/bin/Data/unity default resources", self.serialized([
                {"m_Name": "Internal-Skinning"}]))
            archive.writestr("assets/bin/Data/not-a-game-resource.json", "{}");
            archive.writestr("assets/bin/Data/Managed/Metadata/global-metadata.dat", "raw IL2CPP bytes")
        objects, proof = self.collect()
        self.assertEqual({obj["m_Name"] for obj in objects}, {"EyeHistogram", "Waveform"})
        self.assertEqual(proof["actualPlayerMetadataPayloadCount"], 1)
        self.assertEqual(proof["actualDecodedPayloadCount"], 2)
        self.assertEqual(proof["actualObjectOrigins"][0]["entry"], "assets/bin/Data/14b830dd8a5381e4399cfe161a01662f")

    def test_unknown_and_conflicting_compute_in_hashed_files_are_rejected(self):
        self.write([self.b])
        for objects, message in (([{"m_Name": "InventedReplacement"}], "unowned compute"),
                ([self.a, {**self.a, "variants": ["changed native program"]}], "different compiled programs")):
            with self.subTest(defect=message):
                with zipfile.ZipFile(self.apk, "w") as archive:
                    archive.writestr("assets/bin/Data/" + "1" * 32, self.serialized(objects))
                with self.assertRaisesRegex(BuildError, message): self.collect()

    def test_player_native_metadata_can_skip_unrelated_payload_bodies(self):
        self.write([self.b])
        calls = []
        def metadata_only(prefix):
            calls.append(len(prefix))
            self.assertEqual(len(prefix), 64)
            return True
        objects, proof = campaign_compute.collect_delivered(self.apk, [self.bank], self.manifest,
            self.decode, metadata_only)
        self.assertEqual(calls, [64])
        self.assertEqual(len(objects), 2)
        self.assertEqual(proof["actualPlayerMetadataPayloadCount"], 1)
        with self.assertRaisesRegex(BuildError, "omit original.*EyeHistogram"):
            campaign_compute.collect_delivered(self.apk, [self.bank], self.manifest,
                self.decode, lambda _: False)

    def test_malformed_native_headers_are_rejected_before_the_decoder(self):
        self.write([self.b])
        valid = self.serialized([self.a])
        controls = (
            b"short", b"plain JSON content", valid[:16],
            valid[:8] + struct.pack(">I", 99) + valid[12:],
            valid[:16] + b"\x02" + valid[17:],
            valid[:17] + b"bad" + valid[20:],
            valid[:24] + struct.pack(">Q", len(valid) + 1) + valid[32:],
            valid[:32] + struct.pack(">Q", 16 * 1024 * 1024 + 1) + valid[40:],
            valid[:20] + struct.pack(">I", 64) + valid[24:],
        )
        for index, raw in enumerate(controls):
            with self.subTest(defect=index):
                with zipfile.ZipFile(self.apk, "w") as archive:
                    archive.writestr("assets/bin/Data/" + "1" * 32, raw)
                def unexpected(_): self.fail("Malformed native header reached object decoding")
                with self.assertRaises(BuildError):
                    campaign_compute.collect_delivered(self.apk, [self.bank], self.manifest, unexpected)

    def test_known_scene_asset_names_and_legacy_headers_are_supported(self):
        self.write([self.b])
        for name in ("globalgamemanagers", "globalgamemanagers.assets", "level0"):
            with self.subTest(name=name):
                with zipfile.ZipFile(self.apk, "w") as archive:
                    archive.writestr("assets/bin/Data/" + name, self.serialized([self.a], version=17, endian=1))
                self.assertEqual(len(self.collect()[0]), 2)

    def test_duplicate_archive_entries_are_rejected_before_metadata_selection(self):
        self.write([self.b])
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("assets/bin/Data/" + "1" * 32, self.serialized([self.a]))
            archive.writestr("assets/bin/Data/" + "1" * 32, self.serialized([self.a]))
        with self.assertRaisesRegex(BuildError, "duplicate entry identities"):
            self.collect()


class RuntimeComputeBankTests(unittest.TestCase):
    def setUp(self):
        self.vulkan = {"targetRenderer": 21, "targetLevel": 0, "kernels": []}
        self.desktop = {"targetRenderer": 17, "targetLevel": 11, "kernels": [
            {"name": "KEyeHistogram", "variantMap": [["", {"threadGroupSize": [16, 16, 1],
                "code": list(b"#version 430\nvoid main() {}\n")}]]}]}
        self.original = {"m_Name": "EyeHistogram", "variants": [self.desktop, self.vulkan]}
        self.manifest = {"graphicsApi": "Vulkan", "shaders": [{"name": "EyeHistogram",
            "guid": "14b830dd8a5381e4399cfe161a01662f", "assetPath": "Assets/Resources/shaders/EyeHistogram.compute",
            "kernels": [{"name": "KEyeHistogram", "threadGroups": [16, 16, 1],
                "originalDxbcSha256": "26239d6030173bc084c84cd719c28e261b31987e1423ebc98cbeded5bd164533"}]}]}
        # No proprietary native shader bytes are stored in this fixture. The
        # actual signed bank hash is verified separately on the private APK.
        self.desktop_hash = hashlib.sha256(json.dumps(self.desktop, sort_keys=True,
            separators=(",", ":")).encode()).hexdigest()

    def select(self, objects=None, manifest=None):
        with patch.object(campaign_compute, "_RETAINED_EYE_OPENGL_BANK_SHA256", self.desktop_hash):
            return campaign_compute.select_vulkan_runtime_banks(
                manifest or self.manifest, objects if objects is not None else [self.original])

    def test_exact_extra_bank_is_disclosed_and_raw_native_object_remains_unchanged(self):
        untouched = copy.deepcopy(self.original)
        objects, proof = self.select()
        self.assertEqual(self.original, untouched)
        self.assertEqual(objects[0]["variants"], [self.vulkan])
        self.assertEqual(len(self.original["variants"]), 2)
        extra = proof["additionalNativeBanks"][0]
        self.assertEqual(extra["actualBankSha256"], self.desktop_hash)
        self.assertEqual(extra["backend"], "desktop-OpenGL")
        self.assertEqual(extra["threadGroups"], [16, 16, 1])
        self.assertEqual(extra["kernelName"], "KEyeHistogram")
        self.assertEqual(extra["actualProgramSha256"], hashlib.sha256(
            bytes(self.desktop["kernels"][0]["variantMap"][0][1]["code"])).hexdigest())
        self.assertFalse(extra["selectedForAndroidRuntimeAudit"])

    def test_ordinary_vulkan_only_bank_and_legacy_gles_path_remain_unchanged(self):
        actual = {"m_Name": "EyeHistogram", "variants": [self.vulkan]}
        selected, proof = self.select([actual])
        self.assertEqual(selected, [actual]); self.assertEqual(proof, {"additionalNativeBanks": []})
        legacy = {**self.manifest, "graphicsApi": "OpenGLES3"}
        selected, proof = self.select(manifest=legacy)
        self.assertIs(selected[0], self.original)
        self.assertEqual(proof, {"additionalNativeBanks": []})

    def test_missing_duplicate_wrong_level_and_unrecognized_native_banks_fail_closed(self):
        controls = (
            lambda x: x.update(variants=[self.desktop]),
            lambda x: x.update(variants=[self.vulkan, copy.deepcopy(self.vulkan)]),
            lambda x: x.update(variants=[{**self.vulkan, "targetLevel": 1}]),
            lambda x: x.update(variants=[self.vulkan, {"targetRenderer": 99, "targetLevel": 0}]),
            lambda x: x.update(variants=[self.vulkan, self.desktop, copy.deepcopy(self.desktop)]),
            lambda x: x["variants"][0]["kernels"][0]["variantMap"][0][1]["code"].append(1),
            lambda x: x.update(m_Name="InventedReplacement"),
        )
        for index, mutate in enumerate(controls):
            with self.subTest(defect=index):
                actual = copy.deepcopy(self.original); mutate(actual)
                with self.assertRaises(BuildError): self.select([actual])

    def test_extra_bank_requires_exact_original_owner_guid_and_native_dxbc(self):
        controls = (
            lambda x: x["shaders"][0].update(guid="0" * 32),
            lambda x: x["shaders"][0]["kernels"][0].update(originalDxbcSha256="0" * 64),
            lambda x: x["shaders"][0]["kernels"].append(copy.deepcopy(x["shaders"][0]["kernels"][0])),
        )
        for index, mutate in enumerate(controls):
            with self.subTest(defect=index):
                manifest = copy.deepcopy(self.manifest); mutate(manifest)
                with self.assertRaises(BuildError): self.select(manifest=manifest)

    def test_delivery_caller_keeps_frozen_validator_and_records_complete_raw_object(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = root / "frozen-source"; project = root / "project"
            manifest_path = project / "Assets/QuestOriginalCampaign/campaign-computes.json"
            manifest_path.parent.mkdir(parents=True); manifest_path.write_text(json.dumps(self.manifest))
            apk = root / "player.apk"; receipt = root / "validation.json"
            with zipfile.ZipFile(apk, "w") as archive:
                archive.writestr("assets/bin/Data/14b830dd8a5381e4399cfe161a01662f",
                    DeliveredComputeTests.serialized([self.original]))
            raw = copy.deepcopy(self.original); calls = []
            compute_type = types.SimpleNamespace(name="ComputeShader")
            actual_object = types.SimpleNamespace(type=compute_type, read_typetree=lambda: raw)
            unity = types.SimpleNamespace(load=lambda _: types.SimpleNamespace(objects=[actual_object]))
            native = types.SimpleNamespace(SerializedFile=lambda _: types.SimpleNamespace(objects={1: actual_object}))
            streams = types.SimpleNamespace(EndianBinaryReader=lambda data: data)
            def validate(manifest, objects):
                calls.append(objects)
                self.assertEqual(objects[0]["variants"], [self.vulkan])
                self.assertEqual(manifest, self.manifest)
                return {"frozenValidatorUsed": True}
            validator = types.SimpleNamespace(validate_objects=validate)
            with patch.dict(sys.modules, {"UnityPy": unity, "UnityPy.files": native, "UnityPy.streams": streams}), \
                    patch.object(campaign_compute.campaign_shaders, "load", return_value=validator) as loader, \
                    patch.object(campaign_compute, "_RETAINED_EYE_OPENGL_BANK_SHA256", self.desktop_hash):
                result = campaign_compute.validate_delivered(source, project, apk, [], receipt)
            loader.assert_called_once_with(source, "tools/quest-compute/compiled.py", "tools/quest-compute")
            self.assertEqual(raw, self.original)
            self.assertEqual(len(calls), 1)
            self.assertTrue(result["frozenValidatorUsed"])
            self.assertEqual(result["actualObjectOrigins"][0]["objectSha256"], hashlib.sha256(
                json.dumps(raw, sort_keys=True, separators=(",", ":")).encode()).hexdigest())
            self.assertEqual(len(result["additionalNativeBanks"]), 1)
            self.assertEqual(json.loads(receipt.read_text()), result)


if __name__ == "__main__":
    unittest.main()
