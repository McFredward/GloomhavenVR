"""Loose Story publication preserves real compressed bank records and reuse.

Tiny exact ZIPs and original-art manifest shapes exercise actual production
archive/installation consumers. No Unity, depth model or game bank is invoked.
"""
import copy
import json
from pathlib import Path
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import installation_manifest as contract
import menu_depth_content as content
import menu_depth_resources as resources
import storage


class MenuDepthContentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source, self.output = self.root / "source", self.root / "output"
        self.archive = self.root / "quest-mod-content.zip"
        self.builder = SimpleNamespace(BuildError=storage.BuildError, digest=storage.digest,
            _ordinary_owned=storage._ordinary_owned, Stages=storage.Stages, value_hash=storage.value_hash,
            write_json=storage.write_json, menu_depth_resources=resources)
        self.key = "a" * 64
        self.bank_bytes = {name: ("unchanged native bank " + name).encode() * 100 for name in contract.MOD_PATHS}
        with zipfile.ZipFile(self.archive, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, body in self.bank_bytes.items(): archive.writestr(name, body)
        self.records = [{"path": name, "size": len(body), "sha256": self.sha(body)}
                        for name, body in self.bank_bytes.items()]
        self.manifest = {"schema": 1, "inputKey": self.key, "archive": self.archive.name,
                         "archiveSha256": storage.digest(self.archive), "files": self.records}
        banks = [{**row, "path": Path(row["path"]).name} for row in self.records]
        self.bundles = {"schema": 1, "bundle": banks[0], "bundles": banks,
                        "sourceFiles": [{"path": "Assets/native-bank-fixture", "size": 1, "sha256": "b" * 64}]}
        self.write_source()

    @staticmethod
    def sha(body):
        import hashlib
        return hashlib.sha256(body).hexdigest()

    def write_source(self, body=b"supplied original-art numeric relief"):
        folder = self.source / resources.STORY
        folder.mkdir(parents=True, exist_ok=True)
        name = "1" * 32 + ".gmd"
        (folder / name).write_bytes(body)
        storage.write_json(folder / "manifest.json", {"schema": 1, "status": "complete", "format": "GMD1",
            "images": [{"source_id": "1" * 32, "depth_file": name, "depth_bytes": len(body), "depth_sha256": self.sha(body)}]})
        self.source_rows = [storage.record_file(path, path.relative_to(self.source).as_posix())
                            for path in folder.iterdir()]

    @staticmethod
    def compressed(path, name):
        with zipfile.ZipFile(path) as archive, path.open("rb") as raw:
            entry = archive.getinfo(name)
            raw.seek(entry.header_offset)
            header = raw.read(30)
            name_bytes, extra_bytes = struct.unpack("<4s5H3I2H", header)[-2:]
            raw.seek(name_bytes + extra_bytes, 1)
            return raw.read(entry.compress_size)

    def augment(self, **kwargs):
        return content.augment(self.builder, self.output, self.source, self.archive,
            self.manifest, self.bundles, source_files=self.source_rows, **kwargs)

    def test_actual_loose_story_delivery_preserves_compressed_banks_and_original_manifests(self):
        before = self.archive.read_bytes(), copy.deepcopy(self.manifest), copy.deepcopy(self.bundles)
        result, manifest, bundles = self.augment()
        expected = resources.story_records(self.source, self.source_rows)
        self.assertEqual(manifest["files"], [*self.records, *expected])
        self.assertEqual(manifest["archiveSha256"], storage.digest(result))
        self.assertEqual(bundles["bundles"], self.bundles["bundles"])
        self.assertEqual(bundles["sourceFiles"], self.bundles["sourceFiles"])
        self.assertEqual(bundles["menuDepthStory"]["files"], expected)
        for row in self.records:
            self.assertEqual(self.compressed(self.archive, row["path"]), self.compressed(result, row["path"]))
        with zipfile.ZipFile(result) as archive:
            self.assertEqual(archive.namelist(), [row["path"] for row in manifest["files"]])
            for row in expected:
                self.assertEqual(self.sha(archive.read(row["path"])), row["sha256"])
        self.assertEqual(before, (self.archive.read_bytes(), self.manifest, self.bundles))
        # Exercise the real signed-installation consumer with this exact output,
        # rather than certifying only the producer's own returned dictionaries.
        game = self.root / "retained-game.zip"
        body = b"unmodified original Rulebase fixture"
        name = "StreamingAssets/Rulebase/Campaign.ruleset"
        with zipfile.ZipFile(game, "w") as archive: archive.writestr(name, body)
        game_inventory = {"schema": 1, "inputKey": self.key, "archive": "quest-startup-content.zip",
            "archiveSha256": storage.digest(game), "externalDelivery": True,
            "files": [{"path": name, "size": len(body), "sha256": self.sha(body)}]}
        player = self.root / "signed-fixture.apk"
        with zipfile.ZipFile(player, "w") as archive:
            archive.writestr(contract.MANIFEST, json.dumps({"schema": 1, "inputKey": self.key,
                "game": game_inventory, "mod": manifest}))
            archive.write(result, "assets/quest-mod-content.zip")
        with zipfile.ZipFile(player) as archive:
            verified = contract.validate(archive, self.key, game,
                {"archive": "quest-startup-content.zip", "sha256": game_inventory["archiveSha256"]})
        self.assertTrue(verified["modEntryBytesVerified"])
        self.assertEqual(verified["modFileCount"], len(self.records) + len(expected))

    def test_repeated_update_reuses_closed_archive_without_bank_decompression_or_story_rewrite(self):
        first = self.augment()
        stamp = first[0].stat().st_mtime_ns
        with patch.object(resources, "write_story", side_effect=AssertionError("closed Story delivery")), \
             patch.object(zipfile.ZipFile, "open", side_effect=AssertionError("no retained bank decompression")):
            second = self.augment()
        self.assertEqual(first, second)
        self.assertEqual(first[0].stat().st_mtime_ns, stamp)
        self.archive, self.manifest, self.bundles = first
        with patch.object(resources, "write_story", side_effect=AssertionError("current archive already has exact Story")):
            self.assertEqual(self.augment(), first)

    def test_only_changed_numeric_story_is_replaced_without_touching_current_native_banks(self):
        first = self.augment()
        original_bytes = first[0].read_bytes()
        self.archive, self.manifest, self.bundles = first
        self.write_source(b"new supplied numeric relief field")
        second = self.augment()
        self.assertNotEqual(first[0], second[0])
        self.assertEqual(first[0].read_bytes(), original_bytes)
        for row in self.records:
            self.assertEqual(self.compressed(first[0], row["path"]), self.compressed(second[0], row["path"]))
        self.assertEqual(len(second[1]["files"]), len(self.records) + 2)

    def test_bad_retained_archive_bank_receipt_and_changed_source_are_rejected(self):
        with patch.dict(self.manifest, archiveSha256="f" * 64), self.assertRaisesRegex(storage.BuildError, "archive SHA"):
            self.augment()
        with patch.dict(self.bundles, bundles=self.bundles["bundles"][:-1]), self.assertRaisesRegex(storage.BuildError, "bank receipt"):
            self.augment()
        self.source_rows[0]["sha256"] = "f" * 64
        with self.assertRaisesRegex(storage.BuildError, "source snapshot"):
            self.augment()

    def test_failed_new_story_transfer_preserves_input_and_previous_delivery(self):
        first = self.augment()
        previous = first[0].read_bytes()
        original = self.archive.read_bytes()
        self.write_source(b"changed supplied relief")
        with patch.object(resources, "write_story", side_effect=RuntimeError("interrupted loose content write")), \
             self.assertRaisesRegex(RuntimeError, "interrupted loose"):
            self.augment()
        self.assertEqual(first[0].read_bytes(), previous)
        self.assertEqual(self.archive.read_bytes(), original)
        self.assertFalse(list(self.output.rglob("*.partial")))

    def test_damaged_closed_story_output_repairs_only_its_owned_archive(self):
        first = self.augment()
        original = self.archive.read_bytes()
        first[0].write_bytes(b"damaged regenerable delivery cache")
        with patch.object(resources, "write_story", wraps=resources.write_story) as writer:
            repaired = self.augment()
        writer.assert_called_once()
        self.assertEqual(first[0], repaired[0])
        self.assertEqual(self.archive.read_bytes(), original)
        with zipfile.ZipFile(repaired[0]) as archive:
            self.assertEqual(archive.namelist(), [row["path"] for row in repaired[1]["files"]])

    def test_native_archive_witness_reads_once_then_reuses_across_fresh_invocations(self):
        storage._invocation_file_proofs.clear()
        actual = storage.digest
        reads = []
        def measured(path):
            if Path(path) == self.archive: reads.append(path)
            return actual(path)
        with patch.object(self.builder, "digest", side_effect=measured):
            first = self.augment()
        self.assertEqual(reads, [self.archive])
        storage._invocation_file_proofs.clear()
        def no_input_read(path):
            if Path(path) == self.archive: raise AssertionError("no repeated retained bank hash")
            return actual(path)
        with patch.object(self.builder, "digest", side_effect=no_input_read):
            self.assertEqual(self.augment(), first)

    def test_native_archive_witness_rejects_same_size_mutation_and_false_expected_hash(self):
        self.augment()
        storage._invocation_file_proofs.clear()
        with patch.dict(self.manifest, archiveSha256="f" * 64), self.assertRaisesRegex(storage.BuildError, "archive SHA"):
            self.augment()
        before = self.archive.read_bytes()
        self.archive.write_bytes(b"X" + before[1:])
        with self.assertRaisesRegex(storage.BuildError, "archive SHA"):
            self.augment()

    def test_unknown_filesystem_stamps_cannot_reuse_native_archive_hashes(self):
        self.augment()
        storage._invocation_file_proofs.clear()
        actual = storage.digest
        def no_input_read(path):
            if Path(path) == self.archive: raise AssertionError("unknown filesystem needs actual bytes")
            return actual(path)
        with patch.object(storage, "_file_witness_stamp", return_value=None), \
             patch.object(self.builder, "digest", side_effect=no_input_read), \
             self.assertRaisesRegex(AssertionError, "unknown filesystem"):
            self.augment()

    def test_archive_witness_uses_captured_storage_after_global_alias_changes(self):
        import types
        with patch.dict(sys.modules, storage=types.ModuleType("unrelated_application_storage")):
            first = self.augment()
            self.assertEqual(self.augment(), first)


if __name__ == "__main__":
    unittest.main()
