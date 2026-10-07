"""Focused immutable multi-bank controls; mocked UnityFS fixtures are not native proof."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import mod_assets
import storage


class AuthoredBankTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.authored, self.bundles = self.root / "project", self.root / "bundles"
        self.main = ["Assets/Bundle/Hands/Fixture.prefab", "Assets/Bundle/Table/Fixture.shader"]
        self.art = list(mod_assets.REQUIRED_TOWN)
        self.voices = [mod_assets.TOWN_ROOT + "Audio/merchant-greet.wav", mod_assets.TOWN_ROOT + "Audio/merchant-greet.json"]
        self.include = mod_assets.TOWN_ROOT + "Shaders/TownPracticalLighting.cginc"
        for path in self.main + self.art + self.voices + [self.include]:
            self.write(self.authored / path, ("immutable:" + path).encode())
            self.write(self.authored / (path + ".meta"), ("original-import:" + path).encode())
        self.files = storage.inventory(self.authored)
        self.full = self.receipt(True)
        self.original_files = storage.inventory(self.authored)

    def tearDown(self):
        self.temporary.cleanup()

    @staticmethod
    def write(path, payload):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)

    def receipt(self, full):
        records = []
        banks = []
        selections = [(mod_assets.MAIN, self.main)]
        if full:
            selections += [(mod_assets.TOWN, self.art), (mod_assets.VOICES, self.voices)]
        for name, assets in selections:
            path = self.bundles / name
            self.write(path, b"UnityFS\0controlled-" + name.encode())
            row = storage.record_file(path, name)
            records.append(row)
            banks.append({"bundleName": name, "assetNames": list(assets), "requiredAssetNames": list(assets),
                          "dependencies": [], "bundle": row})
        declared = self.files if full else [row for row in self.files if not row["path"].startswith(mod_assets.TOWN_ROOT)]
        result = {"schema": 1, "target": "Android", "unityVersion": "2021.3.5f1", "bundleName": mod_assets.MAIN,
                  "graphicsApi": "Vulkan" if full else "OpenGLES3", "colorSpace": "Linear", "stereoRenderingPath": "SinglePass",
                  "typeTreesEnabled": True, "chunkBasedCompression": True, "townBanksIncluded": full,
                  "assetNames": list(self.main), "requiredAssetNames": list(self.main),
                  "bundle": records[0], "bundles": records, "banks": banks, "sourceFiles": declared}
        self.store(result)
        return result

    def store(self, receipt):
        storage.write_json(self.bundles / "quest-mod-bundles.json", receipt)

    def validate(self, receipt=None, full=True):
        if receipt is not None:
            self.store(receipt)
        return mod_assets.validate_bundle_set(self.bundles, self.authored, self.files, full_game=full)

    def test_full_bank_set_exact_bytes_and_loader_order(self):
        self.assertEqual(self.validate(), self.full)
        self.assertEqual([row["path"] for row in mod_assets.bundle_records(self.full)],
                         [mod_assets.MAIN, mod_assets.TOWN, mod_assets.VOICES])
        self.assertEqual(storage.inventory(self.authored), self.original_files)

    def test_legacy_startup_receipt_stays_single_bank_compatible(self):
        receipt = self.receipt(False)
        receipt.pop("bundles"); receipt.pop("banks")
        self.assertEqual(self.validate(receipt, full=False), receipt)
        self.assertEqual(mod_assets.bundle_records(receipt), [receipt["bundle"]])
        with self.assertRaises(storage.BuildError):
            self.validate(receipt, full=True)

    def test_full_game_contract_selection_and_provenance_fail_closed(self):
        controls = (
            ("desktop", lambda receipt: receipt.update(target="StandaloneWindows64")),
            ("startup only", lambda receipt: receipt.update(townBanksIncluded=False)),
            ("no type trees", lambda receipt: receipt.update(typeTreesEnabled=False)),
            ("wrong GPU", lambda receipt: receipt.update(graphicsApi="OpenGLES3")),
            ("wrong stereo", lambda receipt: receipt.update(stereoRenderingPath="MultiPass")),
            ("missing voice file", lambda receipt: receipt["bundles"].pop()),
            ("missing voice bank", lambda receipt: receipt["banks"].pop()),
            ("bank order", lambda receipt: receipt["bundles"].reverse()),
            ("bank path escape", lambda receipt: receipt["bundles"][1].update(path="../ghvr-town.bundle")),
            ("duplicate bank", lambda receipt: receipt["bundles"].append(receipt["bundles"][1])),
            ("bank hash", lambda receipt: receipt["bundles"][1].update(sha256="1" * 64)),
            ("source hash", lambda receipt: receipt["sourceFiles"][0].update(sha256="2" * 64)),
            ("source escape", lambda receipt: receipt["sourceFiles"][0].update(path="../outside.fbx")),
            ("source duplicate", lambda receipt: receipt["sourceFiles"].append(receipt["sourceFiles"][0])),
            ("missing resident", lambda receipt: receipt["banks"][1]["assetNames"].remove(mod_assets.REQUIRED_TOWN[0])),
            ("missing facial requirement", lambda receipt: receipt["banks"][1]["requiredAssetNames"].pop()),
            ("missing audio metadata", lambda receipt: receipt["banks"][2]["assetNames"].pop()),
            ("foreign dependency", lambda receipt: receipt["banks"][1]["dependencies"].append("pc-library.bundle")),
            ("main town dependency", lambda receipt: receipt["banks"][0]["dependencies"].append(mod_assets.TOWN)),
            ("voice path escape", lambda receipt: receipt["banks"][2]["assetNames"].append("Assets/Bundle/TownServices/Audio/../secret.wav")),
            ("Windows path", lambda receipt: receipt["banks"][1]["assetNames"].append("Assets/Bundle/TownServices\\secret.prefab")),
            ("changed legacy main", lambda receipt: receipt["assetNames"].pop()),
        )
        for name, mutate in controls:
            with self.subTest(control=name):
                bad = copy.deepcopy(self.full)
                mutate(bad)
                with self.assertRaises(storage.BuildError):
                    self.validate(bad)

    def test_new_authored_town_voice_and_main_art_are_automatically_required(self):
        for path, index in ((mod_assets.TOWN_ROOT + "Prefabs/NextTownFurniture.prefab", 1),
                            (mod_assets.TOWN_ROOT + "Audio/NextResidentLine.wav", 2),
                            ("Assets/Bundle/Table/NextModArt.prefab", 0)):
            with self.subTest(asset=path):
                self.write(self.authored / path, b"ordinary future authored asset")
                self.write(self.authored / (path + ".meta"), b"ordinary source GUID")
                self.files = storage.inventory(self.authored)
                with self.assertRaises(storage.BuildError):
                    self.validate(self.full)
                updated = copy.deepcopy(self.full)
                updated["sourceFiles"] = self.files
                updated["banks"][index]["assetNames"].append(path)
                updated["banks"][index]["requiredAssetNames"].append(path)
                if index == 0:
                    updated["assetNames"].append(path); updated["requiredAssetNames"].append(path)
                self.assertEqual(self.validate(updated), updated)
                self.full = updated

    def test_shader_include_and_import_guid_inputs_cannot_disappear(self):
        for path in (self.include, self.art[0] + ".meta"):
            bad = copy.deepcopy(self.full)
            bad["sourceFiles"] = [row for row in bad["sourceFiles"] if row["path"] != path]
            with self.assertRaises(storage.BuildError):
                self.validate(bad)

    def test_modified_cached_source_or_bundle_is_not_accepted(self):
        self.write(self.authored / self.voices[0], b"modified cached compiler input")
        with self.assertRaisesRegex(storage.BuildError, "changed during Android"):
            self.validate(self.full)
        self.write(self.authored / self.voices[0], ("immutable:" + self.voices[0]).encode())
        self.write(self.bundles / mod_assets.TOWN, b"modified cached native output")
        with self.assertRaisesRegex(storage.BuildError, "bytes differ"):
            self.validate(self.full)

    def test_unityfs_header_and_bundle_symlink_are_checked(self):
        bank = self.bundles / mod_assets.VOICES
        self.write(bank, b"not native Android Unity output")
        receipt = copy.deepcopy(self.full)
        receipt["bundles"][2] = storage.record_file(bank, bank.name)
        receipt["banks"][2]["bundle"] = receipt["bundles"][2]
        with self.assertRaisesRegex(storage.BuildError, "not a real Unity"):
            self.validate(receipt)
        bank.unlink(); bank.symlink_to(self.bundles / mod_assets.MAIN)
        with self.assertRaises(storage.BuildError):
            self.validate(receipt)


if __name__ == "__main__":
    unittest.main()
