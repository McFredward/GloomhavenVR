"""Actual nested ZIP and signed file-inventory controls for PC preinstallation."""
import copy
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock
import warnings
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import installation_manifest as contract


class InstallationManifestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.key = "c" * 64
        self.bank, self.apk = self.root / "owned-bank.zip", self.root / "player.apk"
        self.game = {"StreamingAssets/Rulebase/Campaign.ruleset": b"campaign original fixture",
                     "StreamingAssets/Rulebase/Guildmaster.ruleset": b"guildmaster original fixture"}
        self.mod = {name: ("native fixture " + name).encode() for name in contract.MOD_PATHS}
        self.write_bank()
        self.mod_bytes = self.zip_bytes(self.mod)
        self.manifest = {"schema": 1, "inputKey": self.key,
            "game": self.inventory(self.game, "quest-startup-content.zip", self.sha(self.bank.read_bytes()), True),
            "mod": self.inventory(self.mod, "quest-mod-content.zip", self.sha(self.mod_bytes), False)}
        self.delivery = {"archive": "quest-startup-content.zip", "sha256": self.manifest["game"]["archiveSha256"]}
        self.write_apk()

    @staticmethod
    def sha(data):
        return hashlib.sha256(data).hexdigest()

    @staticmethod
    def zip_bytes(files):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, data in files.items(): archive.writestr(name, data)
        return stream.getvalue()

    def inventory(self, files, archive, sha, external):
        return {"schema": 1, "inputKey": self.key, "archive": archive, "archiveSha256": sha, "externalDelivery": external,
                "files": [{"path": name, "size": len(data), "sha256": self.sha(data)} for name, data in files.items()]}

    def write_bank(self):
        self.bank.write_bytes(self.zip_bytes(self.game))

    def write_apk(self, include_manifest=True, include_mod=True, duplicate_manifest=False):
        with warnings.catch_warnings(), zipfile.ZipFile(self.apk, "w") as archive:
            warnings.simplefilter("ignore", UserWarning)
            if include_manifest:
                archive.writestr(contract.MANIFEST, json.dumps(self.manifest))
                if duplicate_manifest: archive.writestr(contract.MANIFEST, json.dumps(self.manifest))
            if include_mod: archive.writestr("assets/quest-mod-content.zip", self.mod_bytes)

    def validate(self):
        with zipfile.ZipFile(self.apk) as archive:
            return contract.validate(archive, self.key, self.bank, self.delivery)

    def reject_changed_manifest(self, change, expected=None):
        saved = copy.deepcopy(self.manifest)
        try:
            change(self.manifest); self.write_apk()
            if expected:
                with self.assertRaisesRegex(ValueError, expected): self.validate()
            else:
                with self.assertRaises(ValueError): self.validate()
        finally:
            self.manifest = saved
            self.write_apk()

    def test_exact_signed_contract_and_both_game_modes_are_retained(self):
        result = self.validate()
        self.assertTrue(result["gameZipMetadataVerified"] and result["modEntryBytesVerified"])
        self.assertEqual(result["gameFileCount"], 2)
        self.assertEqual(result["modFileCount"], 3)
        self.assertEqual(result["inputKey"], self.key)
        self.assertEqual(result["manifestSha256"], self.sha(json.dumps(self.manifest).encode()))

    def test_external_game_member_bytes_are_never_reread_by_metadata_gate(self):
        real_open = zipfile.ZipFile.open

        def observed(archive, name, *args, **kwargs):
            if archive.filename == str(self.bank):
                raise AssertionError("Another Campaign payload-byte pass was attempted")
            return real_open(archive, name, *args, **kwargs)

        with mock.patch.object(zipfile.ZipFile, "open", observed): self.validate()

    def test_missing_or_duplicate_signed_manifest_is_rejected(self):
        self.write_apk(include_manifest=False)
        with self.assertRaisesRegex(ValueError, "bounded signed"): self.validate()
        self.write_apk(duplicate_manifest=True)
        with self.assertRaisesRegex(ValueError, "bounded signed"): self.validate()

    def test_manifest_bound_is_checked_before_reading(self):
        with zipfile.ZipFile(self.apk) as archive:
            info = zipfile.ZipInfo(contract.MANIFEST); info.file_size = contract.MAXIMUM_MANIFEST_BYTES + 1
            with mock.patch.object(archive, "infolist", return_value=[info]), mock.patch.object(archive, "read", side_effect=AssertionError("Excessive JSON read")):
                with self.assertRaisesRegex(ValueError, "bounded signed"): contract.validate(archive, self.key, self.bank, self.delivery)

    def test_scope_and_build_identity_malformed_controls(self):
        changes = [lambda m: m.update(schema=2), lambda m: m.update(schema=True), lambda m: m.update(inputKey="d" * 64),
                   lambda m: m.update(unexpected=True), lambda m: m["game"].update(inputKey="e" * 64),
                   lambda m: m["mod"].update(inputKey="f" * 64), lambda m: m["game"].update(externalDelivery=False),
                   lambda m: m["mod"].update(externalDelivery=True), lambda m: m["game"].update(archive="other.zip"),
                   lambda m: m["mod"].update(archive="quest-startup-content.zip"), lambda m: m.update(game=[])]
        for index, change in enumerate(changes):
            with self.subTest(control=index): self.reject_changed_manifest(change)

    def test_inventory_paths_sizes_hashes_and_count_controls(self):
        changes = [lambda m: m["game"]["files"][0].update(path="../quest-saves/campaign.save"),
                   lambda m: m["game"]["files"][0].update(path="StreamingAssets/../quest-saves/campaign.save"),
                   lambda m: m["game"]["files"][0].update(path="StreamingAssets/a\0b"),
                   lambda m: m["game"]["files"][0].update(path="StreamingAssets/a\nb"),
                   lambda m: m["game"]["files"][0].update(path="StreamingAssets/" + "a" * 8192),
                   lambda m: m["game"]["files"][0].update(size=-1), lambda m: m["game"]["files"][0].update(size=True),
                   lambda m: m["game"]["files"][0].update(sha256="0"), lambda m: m["game"].update(files=[]),
                   lambda m: m["game"].update(files=m["game"]["files"] * (contract.MAXIMUM_FILES + 1)),
                   lambda m: m["game"]["files"].append(dict(m["game"]["files"][0])),
                   lambda m: m["game"]["files"].append({**m["game"]["files"][0], "path": m["game"]["files"][0]["path"].upper()}),
                   lambda m: m["game"]["files"][0].update(additional="not a file record")]
        for index, change in enumerate(changes):
            with self.subTest(control=index): self.reject_changed_manifest(change)

    def test_game_delivery_and_actual_zip_shape_must_match_signed_inventory(self):
        self.reject_changed_manifest(lambda m: m["game"].update(archiveSha256="0" * 64), "signed archive delivery")
        for altered in ({"StreamingAssets/Rulebase/Campaign.ruleset": b"wrong size"},
                        {**self.game, "StreamingAssets/unmanifested.bin": b"extra file"}):
            with self.subTest(files=tuple(altered)):
                self.bank.write_bytes(self.zip_bytes(altered))
                with self.assertRaisesRegex(ValueError, "ZIP entry"): self.validate()
        self.write_bank()
        with zipfile.ZipFile(self.bank, "a") as archive, warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            archive.writestr(next(iter(self.game)), next(iter(self.game.values())))
        with self.assertRaisesRegex(ValueError, "ZIP entry"): self.validate()

    def test_mod_archive_and_individual_entry_hashes_are_proven_from_bytes(self):
        self.reject_changed_manifest(lambda m: m["mod"].update(archiveSha256="0" * 64), "archive SHA")
        self.reject_changed_manifest(lambda m: m["mod"]["files"][0].update(sha256="0" * 64), "entry SHA")
        changed = dict(self.mod); changed[contract.MOD_PATHS[0]] = b"X" * len(changed[contract.MOD_PATHS[0]])
        self.mod_bytes = self.zip_bytes(changed)
        self.write_apk()
        with self.assertRaisesRegex(ValueError, "archive SHA"): self.validate()
        self.manifest["mod"]["archiveSha256"] = self.sha(self.mod_bytes)
        self.write_apk()
        with self.assertRaisesRegex(ValueError, "entry SHA"): self.validate()

    def test_mod_native_bank_order_and_exact_member_inventory_are_required(self):
        self.reject_changed_manifest(lambda m: m["mod"]["files"].reverse(), "paths/order")
        self.write_apk(include_mod=False)
        with self.assertRaisesRegex(ValueError, "mod archive"): self.validate()
        self.mod_bytes = self.zip_bytes({**self.mod, "StreamingAssets/unknown.bundle": b"extra"})
        self.manifest["mod"]["archiveSha256"] = self.sha(self.mod_bytes)
        self.write_apk()
        with self.assertRaisesRegex(ValueError, "ZIP entry"): self.validate()

    def test_zip_link_members_are_rejected(self):
        info = zipfile.ZipInfo(next(iter(self.game))); info.external_attr = 0o120777 << 16
        with zipfile.ZipFile(self.bank, "w") as archive:
            archive.writestr(info, next(iter(self.game.values())))
            archive.writestr(list(self.game)[1], list(self.game.values())[1])
        with self.assertRaisesRegex(ValueError, "size/type"): self.validate()


if __name__ == "__main__": unittest.main()
