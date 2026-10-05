"""Real standard-library ZIP bytes, exact reuse, ZIP64 and interrupted publish."""
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import native_content_pack as packer
import builder


class NativeContentPack(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.native = self.root / "native"; self.native.mkdir()
        (self.native / "original.bundle").write_bytes(b"UnityFS\0native byte fixture" * 120)
        (self.native / "settings.json").write_bytes(b'{"nativeCatalog":true}')
        (self.native / "ignored.meta").write_bytes(b"not game content")
        self.archive = self.root / "Assets/StreamingAssets/quest-startup-content.zip"; self.archive.parent.mkdir(parents=True)
        original = b"retained original movie bytes" * 100
        with zipfile.ZipFile(self.archive, "w", compression=zipfile.ZIP_DEFLATED) as zip:
            zip.writestr("StreamingAssets/Movies/original.mp4", original)
        self.manifest = self.root / packer.MANIFEST; self.manifest.parent.mkdir(parents=True)
        self.write_manifest({"schema": 1, "inputKey": "a" * 64, "archive": self.archive.name,
                             "archiveSha256": packer.digest(self.archive), "externalDelivery": True,
                             "files": [{"path": "StreamingAssets/Movies/original.mp4", "size": len(original), "sha256": packer.stream_hash(io.BytesIO(original))}]})
        self.request = {"schema": 1, "projectRoot": str(self.root), "nativeRoot": str(self.native)}

    def tearDown(self): self.temp.cleanup()
    def write_manifest(self, value): self.manifest.write_text(json.dumps(value))

    def test_actual_stored_policy_and_unchanged_archive_manifest_are_reused(self):
        first = packer.pack(self.request)
        self.assertFalse(first["reused"]); self.assertEqual(first["nativeBundleCompression"], "ZIP_STORED")
        with zipfile.ZipFile(self.archive) as zip:
            native = zip.getinfo("StreamingAssets/aa/original.bundle")
            self.assertEqual(native.compress_type, zipfile.ZIP_STORED); self.assertEqual(native.compress_size, native.file_size)
            self.assertEqual(zip.read(native), (self.native / "original.bundle").read_bytes())
            self.assertEqual(zip.getinfo("StreamingAssets/Movies/original.mp4").compress_type, zipfile.ZIP_DEFLATED)
            self.assertNotIn("StreamingAssets/aa/ignored.meta", zip.namelist())
        before = self.archive.read_bytes(), self.manifest.read_bytes()
        os.utime(self.archive, (1000000, 1000000)); os.utime(self.manifest, (1000000, 1000000))
        reused = packer.pack(self.request)
        self.assertTrue(reused["reused"]); self.assertEqual(reused["nativeBundleCompression"], "preserved-existing")
        self.assertEqual(before, (self.archive.read_bytes(), self.manifest.read_bytes()))
        self.assertEqual(self.archive.stat().st_mtime, 1000000); self.assertEqual(self.manifest.stat().st_mtime, 1000000)

    def test_same_size_changed_native_bytes_require_repack(self):
        packer.pack(self.request); before = packer.digest(self.archive)
        path = self.native / "original.bundle"; data = path.read_bytes(); path.write_bytes(data[:-1] + b"X")
        result = packer.pack(self.request); self.assertFalse(result["reused"]); self.assertNotEqual(before, result["archiveSha256"])

    def test_exact_entry_hash_rejects_forged_metadata_even_with_actual_archive_hash(self):
        packer.pack(self.request); manifest = json.loads(self.manifest.read_text()); manifest["files"][0]["sha256"] = "0" * 64; self.write_manifest(manifest)
        before = self.archive.read_bytes(), self.manifest.read_bytes()
        with self.assertRaisesRegex(ValueError, "file hash"): packer.pack(self.request)
        self.assertEqual(before, (self.archive.read_bytes(), self.manifest.read_bytes()))

    def test_extra_and_duplicate_archive_entries_are_never_silently_removed(self):
        original_archive, original_manifest = self.archive.read_bytes(), self.manifest.read_bytes()
        for name in ("unexpected", "StreamingAssets/Movies/original.mp4"):
            with self.subTest(name=name):
                self.archive.write_bytes(original_archive); self.manifest.write_bytes(original_manifest)
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    with zipfile.ZipFile(self.archive, "a") as zip: zip.writestr(name, b"extra")
                manifest = json.loads(self.manifest.read_text()); manifest["archiveSha256"] = packer.digest(self.archive); self.write_manifest(manifest)
                with self.assertRaisesRegex(ValueError, "entry set"): packer.pack(self.request)

    def test_copy_checks_bytes_after_inventory(self):
        value = b"before inventory"; row = {"path": "StreamingAssets/aa/bank.bundle", "size": len(value), "sha256": packer.stream_hash(io.BytesIO(value))}
        with zipfile.ZipFile(self.root / "race.zip", "w") as zip:
            with self.assertRaisesRegex(ValueError, "changed during"): packer.write_entry(zip, row, io.BytesIO(b"after  inventory"))

    def test_real_zip64_records_with_bounded_native_bytes(self):
        # Lower only stdlib's format threshold in this fixture, avoiding4GiB I/O.
        with patch.object(zipfile, "ZIP64_LIMIT", 1024): result = packer.pack(self.request)
        data = self.archive.read_bytes(); self.assertIn(b"PK\x06\x06", data); self.assertIn(b"PK\x06\x07", data)
        with zipfile.ZipFile(self.archive) as zip: self.assertIsNone(zip.testzip())
        self.assertTrue(result["allEntryBytesVerified"])

    def test_interruption_leaves_no_partial_zip_and_does_not_modify_original_pair(self):
        before = self.archive.read_bytes(), self.manifest.read_bytes()
        with patch.object(packer, "write_entry", side_effect=KeyboardInterrupt), self.assertRaises(KeyboardInterrupt): packer.pack(self.request)
        self.assertEqual(before, (self.archive.read_bytes(), self.manifest.read_bytes()))
        self.assertEqual(list(self.archive.parent.glob("*.repack-*")), [])

    def test_invalid_scope_and_relative_paths_fail_before_publishing(self):
        for changed in ({**self.request, "schema": 2}, {**self.request, "projectRoot": None}, {**self.request, "nativeRoot": str(self.root.parent)}):
            with self.subTest(changed=changed), self.assertRaises(ValueError): packer.pack(changed)
        for value in (None, "", "../outside", "C:/file", "/absolute", "a\\file", "a//file"):
            with self.subTest(value=value), self.assertRaises(ValueError): packer.relative(value)

    @unittest.skipIf(os.name == "nt", "Requires local symlink fixture permission")
    def test_symlinked_native_members_are_rejected(self):
        (self.native / "outside.bundle").symlink_to(self.archive)
        with self.assertRaisesRegex(ValueError, "links"): packer.pack(self.request)

    def test_builder_overwrites_inherited_packer_paths_without_mutating_parent_env(self):
        inherited = {"GHVR_QUEST_CONTENT_PACK_PYTHON": "untrusted executable", "GHVR_QUEST_CONTENT_PACK_HELPER": "untrusted helper", "OTHER": "kept"}
        actual = builder.content_pack_environment(inherited)
        self.assertEqual(actual["GHVR_QUEST_CONTENT_PACK_PYTHON"], str(Path(sys.executable).resolve()))
        self.assertEqual(actual["GHVR_QUEST_CONTENT_PACK_HELPER"], str(Path(packer.__file__).resolve()))
        self.assertEqual(inherited["GHVR_QUEST_CONTENT_PACK_HELPER"], "untrusted helper"); self.assertEqual(actual["OTHER"], "kept")


if __name__ == "__main__": unittest.main()
