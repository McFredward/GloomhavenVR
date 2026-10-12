"""Actual portable ZIP bytes, bounded nested seeks and retained-copy controls."""
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import portable_package as portable
import storage


class PortablePackageTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="quest-portable-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.output = self.root / "output"; self.output.mkdir()
        self.bank = self.root / portable.CONTENT
        with zipfile.ZipFile(self.bank, "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr("StreamingAssets/catalog.json", b'{"catalog":"original"}')
            archive.writestr("StreamingAssets/bundles/owned.bundle", b"actual owned bytes\n" * 16384)
        self.ap = self.root / "owned-player.apk"
        self.delivery = {"file": portable.CONTENT, "size": self.bank.stat().st_size,
                         "sha256": storage.digest(self.bank), "archive": portable.ARCHIVE}
        self.write_apk(self.ap)
        self.destination = self.root / "portable-export.zip"
        storage._invocation_file_proofs.clear()
        self.addCleanup(storage._invocation_file_proofs.clear)

    def write_apk(self, path, *, payload=b"actual Android player\n" * 1024, delivery=None):
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr("assets/Quest/input-manifest.json", storage.canonical(
                {"schema": 1, "target": "game", "inputKey": "a" * 64, "mod": {"modBuild": 671}}))
            archive.writestr("assets/Quest/content-delivery.json", storage.canonical(
                {"schema": 1, "package": portable.PACKAGE, "inputKey": "a" * 64, "files": [delivery or self.delivery]}))
            archive.writestr("lib/arm64-v8a/libil2cpp.so", payload)

    def export(self, **kwargs):
        return portable.export_package(self.ap, self.destination, self.output, **kwargs)

    def mutate_export(self, operation):
        with zipfile.ZipFile(self.destination) as archive:
            rows = [(info.filename, archive.read(info)) for info in archive.infolist()]
        with zipfile.ZipFile(self.destination, "w", compression=zipfile.ZIP_STORED) as archive:
            operation(archive, rows)

    def test_export_is_one_stored_file_and_hashes_final_written_bytes(self):
        progress = []; receipt = self.export(progress=progress.append)
        self.assertEqual(receipt["sha256"], storage.digest(self.destination))
        self.assertEqual(receipt["size"], self.destination.stat().st_size)
        with zipfile.ZipFile(self.destination) as archive:
            self.assertEqual(set(archive.namelist()), {portable.APK, portable.CONTENT, portable.MANIFEST})
            self.assertIsNone(archive.testzip())
            self.assertTrue(all(row.compress_type == zipfile.ZIP_STORED for row in archive.infolist()))
            self.assertEqual(archive.read(portable.APK), self.ap.read_bytes())
            self.assertEqual(archive.read(portable.CONTENT), self.bank.read_bytes())
        manifest = portable.inspect(self.destination)
        self.assertEqual(manifest["apk"]["sha256"], storage.digest(self.ap))
        self.assertEqual(manifest["content"], self.delivery)
        self.assertEqual({row["total"] for row in progress}, {progress[-1]["total"]})
        self.assertEqual(progress[-1]["percent"], 100)
        self.assertTrue(all(row["percent"] < 100 for row in progress[:-1]))
        self.assertEqual([row["completed"] for row in progress], sorted(row["completed"] for row in progress))

    def test_import_materializes_only_apk_and_reuses_real_byte_witness(self):
        self.export(); events = []
        imported = portable.import_apk(self.destination, self.output, progress=events.append)
        self.assertEqual(imported.read_bytes(), self.ap.read_bytes())
        self.assertFalse((imported.parent / portable.CONTENT).exists())
        descriptor = json.loads((imported.parent / "portable-source.json").read_text())
        self.assertEqual(descriptor["content"], self.delivery)
        stamp = imported.stat()
        storage._invocation_file_proofs.clear()
        repeated = portable.import_apk(self.destination, self.output)
        self.assertEqual(repeated, imported)
        self.assertEqual((imported.stat().st_ino, imported.stat().st_mtime_ns), (stamp.st_ino, stamp.st_mtime_ns))
        source = portable.content_source(imported, self.output)
        self.assertEqual(source["member"], portable.CONTENT)
        self.assertEqual(source["path"], self.destination)
        with portable.open_content(source) as stream, zipfile.ZipFile(stream) as archive:
            self.assertEqual(archive.read("StreamingAssets/catalog.json"), b'{"catalog":"original"}')

    def test_fresh_process_reuses_import_without_reading_apk_payload(self):
        self.export(); imported = portable.import_apk(self.destination, self.output)
        script = """
import json,sys
from pathlib import Path
sys.path.insert(0,sys.argv[1])
import portable_package as p,storage
original=storage.ValidatedFileWitnesses._observe
reads=[]
def observe(self,path,hasher=None):
 def fail(path): reads.append(str(path)); raise RuntimeError('unexpected APK payload read')
 return original(self,path,hasher=fail)
storage.ValidatedFileWitnesses._observe=observe
print(json.dumps({'path':str(p.import_apk(Path(sys.argv[2]),Path(sys.argv[3]))),'reads':reads}))
"""
        result = subprocess.run([sys.executable, "-I", "-B", "-c", script, str(ROOT / "tools/quest-builder"),
                                 str(self.destination), str(self.output)], capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), {"path": str(imported), "reads": []})

    def test_imported_digest_reuses_proof_and_never_hashes_changed_or_unknown_file(self):
        self.export(); imported = portable.import_apk(self.destination, self.output)
        storage._invocation_file_proofs.clear()
        self.assertEqual(portable.imported_apk_digest(imported, self.output), storage.digest(self.ap))
        self.assertIsNone(portable.imported_apk_digest(self.ap, self.output))
        value = imported.read_bytes(); imported.write_bytes(b"Y" + value[1:])
        self.assertIsNone(portable.imported_apk_digest(imported, self.output))
        descriptor = imported.parent / "portable-source.json"
        metadata = json.loads(descriptor.read_text()); metadata["apk"]["sha256"] = storage.digest(imported)
        descriptor.write_text(json.dumps(metadata))
        self.assertIsNone(portable.imported_apk_digest(imported, self.output))

    def test_corrupt_import_repairs_only_apk_not_retained_neighbour(self):
        self.export(); imported = portable.import_apk(self.destination, self.output)
        evidence = imported.parent / "retained.log"; evidence.write_text("keep evidence")
        imported.write_bytes(b"damaged")
        repaired = portable.import_apk(self.destination, self.output)
        self.assertEqual(repaired.read_bytes(), self.ap.read_bytes())
        self.assertEqual(evidence.read_text(), "keep evidence")
        self.assertFalse((repaired.parent / portable.CONTENT).exists())

    def test_cancelled_import_continues_exact_checkpoint(self):
        self.write_apk(self.ap, payload=b"fixture-data" * 4096)
        self.export(); events = []
        with mock.patch.object(portable, "CHUNK", 4096), mock.patch.object(portable, "CHECKPOINT", 8192):
            with self.assertRaisesRegex(storage.BuildError, "cancelled"):
                portable.import_apk(self.destination, self.output, progress=events.append,
                    cancel=lambda: bool(events and events[-1]["completed"] >= 12288))
            partial = list((self.output / "imports/portable").glob("*/" + portable.APK + ".part"))[0]
            retained = partial.stat().st_size
            self.assertGreater(retained, 0)
            continued = []
            imported = portable.import_apk(self.destination, self.output, progress=continued.append)
        self.assertEqual(continued[0]["completed"], retained)
        self.assertEqual(imported.read_bytes(), self.ap.read_bytes())
        self.assertFalse(partial.exists())

    def test_corrupt_partial_is_repaired_and_final_hash_remains_authoritative(self):
        self.export(); events = []
        with mock.patch.object(portable, "CHUNK", 4096):
            with self.assertRaises(storage.BuildError):
                portable.import_apk(self.destination, self.output, progress=events.append,
                    cancel=lambda: bool(events and events[-1]["completed"] >= 4096))
        partial = list((self.output / "imports/portable").glob("*/" + portable.APK + ".part"))[0]
        partial.write_bytes(b"X" * partial.stat().st_size)
        events = []; imported = portable.import_apk(self.destination, self.output, progress=events.append)
        self.assertEqual(events[0]["completed"], 0)
        self.assertEqual(imported.read_bytes(), self.ap.read_bytes())

    def test_cancelled_export_preserves_previous_destination_and_no_partial(self):
        self.destination.write_bytes(b"previous deliverable")
        events = []
        with mock.patch.object(portable, "CHUNK", 4096):
            with self.assertRaisesRegex(storage.BuildError, "cancelled"):
                self.export(progress=events.append, cancel=lambda: bool(events and events[-1]["completed"] >= 4096))
        self.assertEqual(self.destination.read_bytes(), b"previous deliverable")
        self.assertEqual(list(self.root.glob("*.partial-*")), [])

    def test_disk_space_preflight_preserves_previous_result(self):
        self.destination.write_bytes(b"previous deliverable")
        with mock.patch.object(shutil, "disk_usage", return_value=shutil._ntuple_diskusage(100, 99, 1)):
            with self.assertRaisesRegex(storage.BuildError, "free disk space"): self.export()
        self.assertEqual(self.destination.read_bytes(), b"previous deliverable")
        self.assertEqual(list(self.root.glob("*.partial-*")), [])

    def test_export_refuses_changed_bank_and_preserves_previous_result(self):
        self.destination.write_bytes(b"previous deliverable")
        value = self.bank.read_bytes(); self.bank.write_bytes(b"X" + value[1:])
        with self.assertRaisesRegex(storage.BuildError, "signed bank identity"): self.export()
        self.assertEqual(self.destination.read_bytes(), b"previous deliverable")

    def test_selected_package_never_overwrites_itself(self):
        self.export(); imported = portable.import_apk(self.destination, self.output)
        before = self.destination.read_bytes()
        with self.assertRaisesRegex(storage.BuildError, "input files"):
            portable.export_package(imported, self.destination, self.output, source_package=self.destination)
        self.assertEqual(self.destination.read_bytes(), before)

    def test_wrong_outer_bank_manifest_rejected_by_signed_apk_contract(self):
        self.export()
        def rewrite(archive, rows):
            for name, data in rows:
                if name == portable.MANIFEST:
                    value = json.loads(data); value["content"]["sha256"] = "0" * 64; data = storage.canonical(value)
                archive.writestr(name, data)
        self.mutate_export(rewrite)
        with self.assertRaisesRegex(storage.BuildError, "signed delivery contract"): portable.inspect(self.destination)

    def test_unknown_duplicate_encrypted_link_and_compressed_members_refused(self):
        for kind in ("unknown", "duplicate", "link", "compressed", "encrypted"):
            with self.subTest(kind=kind):
                self.export()
                def rewrite(archive, rows):
                    for name, data in rows:
                        row = portable._info(name)
                        if name == portable.CONTENT:
                            if kind == "unknown": row.filename = "../unsafe.zip"
                            if kind == "link": row.external_attr = (0o120777 << 16)
                            if kind == "compressed": row.compress_type = zipfile.ZIP_DEFLATED
                        archive.writestr(row, data)
                    if kind == "duplicate": archive.writestr(portable.APK, rows[0][1])
                self.mutate_export(rewrite)
                if kind == "encrypted":
                    with self.destination.open("rb") as stream: outer_cd = portable._directory(stream)
                    with self.destination.open("r+b") as stream:
                        data = stream.read(); value = bytearray(data); value[outer_cd + 8] |= 1
                        stream.seek(0); stream.write(value)
                with self.assertRaises(storage.BuildError): portable.inspect(self.destination)

    def test_corrupt_apk_bytes_cannot_receive_completed_import_receipt(self):
        self.export()
        with portable._outer(self.destination) as (_, offsets, _): offset = offsets[portable.APK]
        with self.destination.open("r+b") as stream:
            stream.seek(offset + 40); value = stream.read(1); stream.seek(offset + 40); stream.write(bytes([value[0] ^ 1]))
        with self.assertRaises(storage.BuildError): portable.import_apk(self.destination, self.output)
        self.assertEqual(list((self.output / "imports").glob("**/" + portable.APK)), [])

    def test_reexport_nested_package_uses_no_materialized_bank(self):
        self.export(); imported = portable.import_apk(self.destination, self.output)
        other = self.root / "recipient-package.zip"
        receipt = portable.export_package(imported, other, self.output)
        self.assertEqual(receipt["content"], self.delivery)
        self.assertFalse((imported.parent / portable.CONTENT).exists())
        with zipfile.ZipFile(other) as archive:
            self.assertEqual(archive.read(portable.CONTENT), self.bank.read_bytes())

    def test_updated_apk_resolves_original_workspace_receipt_without_tree_scan(self):
        build = self.output / "builds" / ("b" * 64); build.mkdir(parents=True)
        bank = build / portable.CONTENT; shutil.copyfile(self.bank, bank)
        update = self.output / "updates" / ("c" * 64) / portable.APK
        update.parent.mkdir(parents=True); shutil.copyfile(self.ap, update)
        storage.write_json(self.output / "receipts/build" / ("b" * 64 + ".json"),
            {"schema": 1, "details": {"contentFiles": [{"path": bank.relative_to(self.output).as_posix(),
            "sha256": self.delivery["sha256"], "size": self.delivery["size"]}]}})
        source = portable.content_source(update, self.output)
        self.assertEqual(source["path"], bank)
        with mock.patch.object(portable, "_receipt_paths", side_effect=AssertionError("unnecessary receipt scan")):
            self.assertEqual(portable.content_source(update, self.output)["path"], bank)

    def test_updated_apk_resolves_previous_import_without_reextracting_bank(self):
        self.export(); imported = portable.import_apk(self.destination, self.output)
        update = self.output / "updates/current" / portable.APK
        update.parent.mkdir(parents=True); shutil.copyfile(imported, update)
        source = portable.content_source(update, self.output)
        self.assertEqual(source["path"], self.destination)
        self.assertEqual(source["member"], portable.CONTENT)

    def test_optional_content_digest_reuses_only_real_unchanged_byte_proof(self):
        self.export(); source = portable.content_source(self.ap, self.output)
        with mock.patch.object(portable, "open_content", side_effect=AssertionError("unexpected repeated bank read")):
            self.assertEqual(portable.qualify_content(source, self.output), self.delivery["sha256"])
        changed = self.bank.read_bytes(); self.bank.write_bytes(b"Y" + changed[1:])
        with self.assertRaisesRegex(storage.BuildError, "signed APK SHA-256"):
            portable.qualify_content(source, self.output)

    @unittest.skipUnless(os.name == "posix", "Actual sparse file accounting requires POSIX")
    def test_sparse_zip64_nested_seek_above_four_gibibytes(self):
        # The bank member itself is sparse and >4 GiB; its nested ZIP directory
        # is at the end. No zero-filled payload is hashed or allocated/read.
        size = 4 * 1024 ** 3 + 4096
        package = self.root / "sparse-offsets.zip"
        apk = self.ap.read_bytes(); catalog = b'{"sparse":"original"}'
        manifest = {"schema": 1, "format": portable.FORMAT, "package": portable.PACKAGE,
                    "apk": {"file": portable.APK, "size": len(apk), "sha256": hashlib.sha256(apk).hexdigest()},
                    "content": {"file": portable.CONTENT, "size": size, "sha256": "1" * 64, "archive": portable.ARCHIVE}}
        nested = io.BytesIO()
        with zipfile.ZipFile(nested, "w") as archive: archive.writestr("catalog.json", catalog)
        nested_bytes = nested.getvalue()
        with package.open("w+b") as stream:
            rows = []
            def member(name, data, length=None):
                length = len(data) if length is None else length; offset = stream.tell(); raw = name.encode()
                large = length > 0xffffffff; extra = struct.pack("<HHQQ", 1, 16, length, length) if large else b""
                stream.write(struct.pack("<4s5H3I2H", b"PK\x03\x04", 45 if large else 20, 0, 0, 0, 0,
                                        zlib.crc32(data), 0xffffffff if large else length, 0xffffffff if large else length,
                                        len(raw), len(extra)) + raw + extra)
                payload = stream.tell()
                if large: stream.seek(payload + length - len(data))
                stream.write(data); rows.append((name, offset, length, zlib.crc32(data)))
                return payload
            member(portable.APK, apk)
            payload_offset = member(portable.CONTENT, nested_bytes, size)
            member(portable.MANIFEST, storage.canonical(manifest))
            start = stream.tell()
            for name, offset, length, crc in rows:
                raw = name.encode(); large_size = length > 0xffffffff; large_offset = offset > 0xffffffff
                extended = (struct.pack("<QQ", length, length) if large_size else b"") + (struct.pack("<Q", offset) if large_offset else b"")
                extra = struct.pack("<HH", 1, len(extended)) + extended if extended else b""
                stream.write(struct.pack("<4s6H3I5H2I", b"PK\x01\x02", 45, 45, 0, 0, 0, 0, crc,
                    0xffffffff if large_size else length, 0xffffffff if large_size else length, len(raw), len(extra), 0,
                    0, 0, 0, 0xffffffff if large_offset else offset) + raw + extra)
            central_size = stream.tell() - start; record = stream.tell()
            stream.write(struct.pack("<4sQ2H2I4Q", b"PK\x06\x06", 44, 45, 45, 0, 0, 3, 3, central_size, start))
            stream.write(struct.pack("<4sIQI", b"PK\x06\x07", 0, record, 1))
            stream.write(struct.pack("<4s4H2IH", b"PK\x05\x06", 0, 0, 3, 3, central_size, 0xffffffff, 0))
        allocated = package.stat().st_blocks * 512
        self.assertLess(allocated, 1048576)
        source = {"path": package, "member": portable.CONTENT, "size": size, "sha256": "1" * 64, "archive": portable.ARCHIVE}
        with portable.open_content(source) as stream:
            self.assertGreater(stream.offset + stream.size, 4 * 1024 ** 3)
            stream.seek(-len(nested_bytes), io.SEEK_END)
            self.assertEqual(stream.read(), nested_bytes)
            # ZipFile handles the zero-filled concatenation prefix using its
            # central-directory correction and reads only its tiny directory.
            with zipfile.ZipFile(stream) as archive:
                self.assertEqual(archive.read("catalog.json"), catalog)
        self.assertEqual(package.stat().st_blocks * 512, allocated)


if __name__ == "__main__": unittest.main()
