"""APK-format evidence against real zipalign; no game or headset claim."""
import io
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import MagicMock, Mock, patch
import zipfile
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import apk_update as update
from storage import BuildError


def manual_zip(path, members, *, start=0, library_alignment=16384, comment=b""):
    """Independent ZIP32 fixture, including optional 12/16-byte descriptors."""
    records = []
    with path.open("wb") as stream:
        stream.seek(start)
        for row in members:
            name, body = row["name"], row["body"]
            method = row.get("method", zipfile.ZIP_STORED)
            flags = 0x800 if not name.isascii() else 0
            descriptor = row.get("descriptor")
            if descriptor is not None: flags |= 8
            filename = name.encode("utf-8" if flags & 0x800 else "ascii")
            compressed = body
            if method == zipfile.ZIP_DEFLATED:
                compressor = zlib.compressobj(wbits=-15)
                compressed = compressor.compress(body) + compressor.flush()
            crc = zlib.crc32(body)
            offset = stream.tell(); extra = row.get("local_extra", b"")
            if method == zipfile.ZIP_STORED:
                alignment = library_alignment if name.endswith(".so") else 4
                extra += bytes(-(offset + 30 + len(filename) + len(extra)) % alignment)
            stream.write(struct.pack("<4s5H3I2H", b"PK\x03\x04", 20, flags, method, 0, 33,
                0 if descriptor is not None else crc,
                0 if descriptor is not None else len(compressed),
                0 if descriptor is not None else len(body), len(filename), len(extra)))
            stream.write(filename); stream.write(extra); stream.write(compressed)
            if descriptor is not None:
                if descriptor: stream.write(b"PK\x07\x08")
                stream.write(struct.pack("<III", crc, len(compressed), len(body)))
            records.append((row, filename, flags, crc, len(compressed), offset))
        directory = stream.tell()
        for row, filename, flags, crc, size, offset in records:
            extra = row.get("central_extra", b""); file_comment = row.get("comment", b"")
            stream.write(struct.pack("<4s6H3I5H2I", b"PK\x01\x02", 20, 20, flags,
                row.get("method", zipfile.ZIP_STORED), 0, 33, crc, size, len(row["body"]),
                len(filename), len(extra), len(file_comment), 0, 0, 0, offset))
            stream.write(filename); stream.write(extra); stream.write(file_comment)
        directory_size = stream.tell() - directory
        stream.write(struct.pack("<4s4H2IH", b"PK\x05\x06", 0, 0, len(records),
                                 len(records), directory_size, directory, len(comment)))
        stream.write(comment)


def body_and_descriptor(path, name):
    with zipfile.ZipFile(path) as archive, path.open("rb") as source:
        info = archive.getinfo(name); source.seek(info.header_offset)
        fields = struct.unpack("<4s5H3I2H", source.read(30))
        source.seek(fields[-2] + fields[-1], 1)
        start = source.tell(); compressed = source.read(info.compress_size)
        descriptor = source.read(16) if info.flag_bits & 8 else b""
        if descriptor[:4] != b"PK\x07\x08": descriptor = descriptor[:12]
        return start, compressed, descriptor, info


def payload_with_crc(target):
    """Solve the 32-bit CRC transform for one actual four-byte payload."""
    zero = zlib.crc32(bytes(4)); basis = {}
    for bit in range(32):
        vector = zlib.crc32((1 << bit).to_bytes(4, "little")) ^ zero
        mask = 1 << bit
        while vector:
            pivot = vector.bit_length() - 1
            if pivot not in basis:
                basis[pivot] = vector, mask; break
            previous, previous_mask = basis[pivot]
            vector ^= previous; mask ^= previous_mask
    vector, mask = target ^ zero, 0
    while vector:
        previous, previous_mask = basis[vector.bit_length() - 1]
        vector ^= previous; mask ^= previous_mask
    body = mask.to_bytes(4, "little")
    assert zlib.crc32(body) == target
    return body


class ApkAlignmentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.base, self.output = self.root / "base.apk", self.root / "output.apk"
        library = bytearray(64); library[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", library, 18, 183)
        manifest = {"schema": 1, "target": "game", "inputKey": "a" * 64, "mod": {"modBuild": 668}}
        self.values = {update.INPUT: json.dumps(manifest).encode(),
                       update.INSTALLATION: b'{"schema":1,"inputKey":"' + b"a" * 64 + b'"}',
                       update.LIBRARY: bytes(library), update.METADATA: struct.pack("<II", 0xFAB11BAF, 29),
                       "assets/owned-payload": b"unchanged compressed content" * 20}
        self.manifest = {"schema": 1, "gameInputKey": "a" * 64, "updateKey": "b" * 64, "modBuild": 668}

    def tearDown(self): self.temp.cleanup()

    def source(self, *, extra_members=(), comment=b"", library_alignment=4096):
        rows = [{"name": name, "body": value,
                 "method": zipfile.ZIP_STORED if name == update.LIBRARY else zipfile.ZIP_DEFLATED}
                for name, value in self.values.items()]
        manual_zip(self.base, rows + list(extra_members), comment=comment, library_alignment=library_alignment)

    def publish(self, *, signer=None, verifier=None):
        return update.repack(self.base, self.output, {update.PROFILE: b'{"displayName":"DUMMY"}'},
            update_manifest=self.manifest, signer=signer or (lambda _: None), verifier=verifier or (lambda _: None))

    def native_check(self, path):
        tool = shutil.which("zipalign")
        pinned = Path("/home/claw/unity-2021.3.5/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/build-tools/30.0.2/zipalign")
        if pinned.is_file(): tool = str(pinned)
        if not tool: self.skipTest("Native Android zipalign is not available in this environment.")
        result = subprocess.run([tool, "-c", "-p", "4", str(path)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_repack_aligns_stored_payloads_and_so_in_the_existing_copy_pass(self):
        extra = struct.pack("<HH3s", 0xCAFE, 3, b"raw")
        comment = b"original archive comment retained byte-for-byte"
        members = [{"name": "assets/uncompressed", "body": b"stored game bytes", "central_extra": extra,
                    "local_extra": extra, "comment": b"original member comment"},
                   {"name": "assets/owned-native.so", "body": b"retained standalone library"},
                   {"name": "assets/unicode-\u00f6", "body": b"unicode path", "method": zipfile.ZIP_DEFLATED}]
        self.source(extra_members=members, comment=comment)
        original = self.base.read_bytes(); sign_checks = []
        self.publish(signer=lambda path: sign_checks.append(update.check_alignment(path)))
        self.assertEqual(sign_checks[0]["sharedLibraries"], 2)
        report = update.check_alignment(self.output)
        self.assertEqual(report["sharedLibraryAlignment"], 16384)
        self.assertEqual(report["size"], self.output.stat().st_size)
        self.native_check(self.output)
        with zipfile.ZipFile(self.base) as before, zipfile.ZipFile(self.output) as after:
            self.assertEqual(after.comment, before.comment)
            self.assertIsNone(after.testzip())
            for info in before.infolist():
                current = after.getinfo(info.filename)
                self.assertEqual((current.CRC, current.file_size, current.compress_size, current.compress_type,
                                  current.extra, current.comment, current.flag_bits, current.date_time),
                                 (info.CRC, info.file_size, info.compress_size, info.compress_type,
                                  info.extra, info.comment, info.flag_bits, info.date_time))
                self.assertEqual(body_and_descriptor(self.base, info.filename)[1],
                                 body_and_descriptor(self.output, info.filename)[1])
        self.assertEqual(self.base.read_bytes(), original)

    def test_signed_and_unsigned_descriptor_bytes_survive_raw_repack(self):
        for signature in (False, True):
            with self.subTest(signature=signature):
                self.source(extra_members=[{"name": "assets/streamed", "body": b"streamed content" * 200,
                                            "method": zipfile.ZIP_DEFLATED, "descriptor": signature}])
                self.publish(); self.native_check(self.output)
                before = body_and_descriptor(self.base, "assets/streamed")
                after = body_and_descriptor(self.output, "assets/streamed")
                self.assertEqual(before[1:3], after[1:3])
                with zipfile.ZipFile(self.output) as archive: self.assertIsNone(archive.testzip())

    def test_unsigned_descriptor_crc_equal_to_optional_signature_is_preserved(self):
        body = payload_with_crc(0x08074b50)
        self.source(extra_members=[{"name": "assets/crc-collision", "body": body, "descriptor": False}])
        self.publish()
        with zipfile.ZipFile(self.output) as archive:
            self.assertEqual(archive.read("assets/crc-collision"), body)
            self.assertIsNone(archive.testzip())
        before = body_and_descriptor(self.base, "assets/crc-collision")
        after = body_and_descriptor(self.output, "assets/crc-collision")
        self.assertEqual(before[1], after[1])
        # Do not infer descriptor width from a prefix that is the real CRC.
        with self.output.open("rb") as source:
            source.seek(after[0] + after[3].compress_size)
            self.assertEqual(source.read(12), struct.pack("<III", 0x08074b50, 4, 4))

    def test_old_four_kib_signed_library_can_be_checked_without_rewriting(self):
        manual_zip(self.base, [{"name": "lib/arm64-v8a/example.so", "body": b"library"}], library_alignment=4096)
        before = self.base.read_bytes()
        result = update.check_alignment(self.base, shared_library_alignment=4096)
        self.assertEqual(result["sharedLibraryAlignment"], 4096)
        with self.assertRaisesRegex(BuildError, "16384"):
            update.check_alignment(self.base)
        self.native_check(self.base)
        self.assertEqual(self.base.read_bytes(), before)

    def test_sparse_zip32_member_above_two_gib_is_checked_without_reading_payload(self):
        offset = (1 << 31) + 8192
        manual_zip(self.base, [{"name": "assets/sparse", "body": b"payload"}], start=offset)
        with self.base.open("rb") as source:
            monitored = MagicMock(wraps=source); monitored.__enter__.return_value = monitored
            with patch.object(Path, "open", return_value=monitored):
                result = update.check_alignment(self.base)
            reads = [call.args[0] for call in monitored.read.call_args_list]
            self.assertTrue(all(0 <= size <= 22 + 0xffff for size in reads))
            self.assertLess(sum(reads), 80 * 1024)
        self.assertEqual(result["entries"], 1)
        self.assertGreater(result["size"], 1 << 31)
        with zipfile.ZipFile(self.base) as archive:
            self.assertEqual(archive.getinfo("assets/sparse").header_offset, offset)
            self.assertEqual(archive.read("assets/sparse"), b"payload")

    def test_end_signature_inside_a_zip_comment_does_not_replace_the_real_end_record(self):
        manual_zip(self.base, [{"name": "assets/payload", "body": b"payload"}],
                   comment=b"comment PK\x05\x06 followed by bytes that are not an end record")
        self.assertEqual(update.check_alignment(self.base)["entries"], 1)

    def test_zip32_descriptor_width_uses_uint32_fields_above_python_conservative_limit(self):
        # This is a header-only parser contract, not a payload CRC/deflate claim.
        # A ZIP32 declared uncompressed size above 2 GiB still has a 12-byte DD.
        name = b"assets/declared-size"; size = zipfile.ZIP64_LIMIT + 1
        info = zipfile.ZipInfo(name.decode()); info.flag_bits = 8; info.compress_type = zipfile.ZIP_DEFLATED
        info.CRC, info.compress_size, info.file_size, info.header_offset = 1, 2, size, 0
        descriptor = struct.pack("<III", 1, 2, size)
        source = io.BytesIO(struct.pack("<4s5H3I2H", b"PK\x03\x04", 20, 8, 8, 0, 33,
                                       0, 0, 0, len(name), 0) + name + b"xx" + descriptor)
        target = io.BytesIO(); update._copy_raw_record(source, target, info)
        self.assertEqual(target.getvalue()[-14:], b"xx" + descriptor)

    def test_local_central_fields_and_truncated_descriptor_are_rejected_atomically(self):
        self.source(extra_members=[{"name": "assets/streamed", "body": b"streamed", "descriptor": True}])
        original = self.base.read_bytes(); self.output.write_bytes(b"previous signed result")
        with zipfile.ZipFile(self.base) as archive: offset = archive.getinfo("assets/streamed").header_offset
        mutations = {"method": (offset + 8, "<H", 8), "flags": (offset + 6, "<H", 0),
                     "crc": (offset + 14, "<I", 123), "size": (offset + 18, "<I", 123),
                     "zip64": (offset + 18, "<I", 0xffffffff)}
        for label, (position, format, value) in mutations.items():
            with self.subTest(label=label):
                body = bytearray(original); struct.pack_into(format, body, position, value); self.base.write_bytes(body)
                signer, verifier = Mock(), Mock()
                with self.assertRaises(BuildError): self.publish(signer=signer, verifier=verifier)
                signer.assert_not_called(); verifier.assert_not_called()
                self.assertEqual(self.output.read_bytes(), b"previous signed result")
                self.assertEqual(self.base.read_bytes(), body)
                self.assertEqual(list(self.root.glob("output.apk.*.apk")), [])
        self.base.write_bytes(original)
        start, _, _, info = body_and_descriptor(self.base, "assets/streamed")
        broken = bytearray(original); struct.pack_into("<I", broken, start + info.compress_size + 4, 123)
        self.base.write_bytes(broken)
        with self.assertRaisesRegex(BuildError, "descriptor is invalid"): self.publish()

    def test_alignment_checker_rejects_local_path_and_stored_size_disagreement(self):
        manual_zip(self.base, [{"name": "assets/payload", "body": b"payload"}])
        original = self.base.read_bytes()
        with zipfile.ZipFile(self.base) as archive: offset = archive.getinfo("assets/payload").header_offset
        body = bytearray(original); body[offset + 30] = ord("x"); self.base.write_bytes(body)
        with self.assertRaisesRegex(BuildError, "member paths disagree"): update.check_alignment(self.base)
        body = bytearray(original); struct.pack_into("<I", body, offset + 22, 6); self.base.write_bytes(body)
        with self.assertRaisesRegex(BuildError, "CRC/sizes disagree"): update.check_alignment(self.base)

    def test_unsupported_end_directory_shapes_and_counts_are_rejected(self):
        manual_zip(self.base, [{"name": "assets/payload", "body": b"payload"}])
        original = self.base.read_bytes(); end = len(original) - 22
        mutations = {"split": (end + 4, "<H", 1), "count-sentinel": (end + 10, "<H", 0xffff),
                     "count-mismatch": (end + 8, "<H", 0), "directory-size": (end + 12, "<I", 0xffffffff),
                     "directory-offset": (end + 16, "<I", 0xffffffff), "bad-directory-bounds": (end + 12, "<I", 1)}
        for label, (position, format, value) in mutations.items():
            with self.subTest(label=label):
                body = bytearray(original); struct.pack_into(format, body, position, value); self.base.write_bytes(body)
                with self.assertRaises(BuildError): update.check_alignment(self.base)
        self.base.write_bytes(original[:-1])
        with self.assertRaisesRegex(BuildError, "end record"): update.check_alignment(self.base)

    def test_zip64_locator_and_overlapping_local_records_are_rejected(self):
        manual_zip(self.base, [{"name": "assets/payload", "body": b"payload"}])
        original = self.base.read_bytes(); end = len(original) - 22
        locator = struct.pack("<4sIQI", b"PK\x06\x07", 0, 0, 1)
        body = bytearray(original[:end] + locator + original[end:])
        directory_size = struct.unpack_from("<I", body, end + 20 + 12)[0]
        struct.pack_into("<I", body, end + 20 + 12, directory_size + 20)
        self.base.write_bytes(body)
        with self.assertRaisesRegex(BuildError, "ZIP64 directory"): update.check_alignment(self.base)

        inner = b"assets/inner"; crc = zlib.crc32(b"x")
        record = struct.pack("<4s5H3I2H", b"PK\x03\x04", 20, 0, 0, 0, 33,
                             crc, 1, 1, len(inner), 2) + inner + bytes(2) + b"x"
        # The second local header is embedded in the first stored body; both
        # separate headers agree with their directory entries but overlap.
        manual_zip(self.base, [{"name": "assets/outer", "body": bytes(4) + record}])
        original = self.base.read_bytes(); end = len(original) - 22
        central = struct.pack("<4s6H3I5H2I", b"PK\x01\x02", 20, 20, 0, 0, 0, 33,
                              crc, 1, 1, len(inner), 0, 0, 0, 0, 0, 48) + inner
        body = bytearray(original[:end] + central + original[end:])
        struct.pack_into("<HH", body, end + len(central) + 8, 2, 2)
        directory_size = struct.unpack_from("<I", body, end + len(central) + 12)[0]
        struct.pack_into("<I", body, end + len(central) + 12, directory_size + len(central))
        self.base.write_bytes(body)
        with self.assertRaisesRegex(BuildError, "records overlap"): update.check_alignment(self.base)

    def test_zip64_central_extras_and_unsupported_flags_or_methods_are_rejected(self):
        cases = [{"central_extra": struct.pack("<HHQ", 1, 8, 7)},
                 {"central_extra": b"\x02"}]
        for values in cases:
            with self.subTest(values=values):
                manual_zip(self.base, [{"name": "assets/payload", "body": b"payload", **values}])
                with self.assertRaises(BuildError): update.check_alignment(self.base)
        manual_zip(self.base, [{"name": "assets/payload", "body": b"payload"}])
        original = self.base.read_bytes(); directory = original.index(b"PK\x01\x02")
        for flags in (1, 0x40, 0x2000):
            with self.subTest(flags=flags):
                body = bytearray(original); struct.pack_into("<H", body, 6, flags)
                struct.pack_into("<H", body, directory + 8, flags); self.base.write_bytes(body)
                with self.assertRaisesRegex(BuildError, "flags or version"): update.check_alignment(self.base)
        body = bytearray(original); struct.pack_into("<H", body, 8, 99)
        struct.pack_into("<H", body, directory + 10, 99); self.base.write_bytes(body)
        with self.assertRaisesRegex(BuildError, "method"): update.check_alignment(self.base)

    def test_unaligned_stored_member_is_rejected_without_payload_read_or_hash(self):
        with zipfile.ZipFile(self.base, "w") as archive: archive.writestr("assets/x", b"payload")
        with patch.object(update, "digest", side_effect=AssertionError("Bounded alignment must not hash the APK")), \
                patch.object(zipfile.ZipFile, "open", side_effect=AssertionError("Bounded alignment must not inflate payloads")):
            with self.assertRaisesRegex(BuildError, "not aligned"): update.check_alignment(self.base)

    def test_invalid_alignment_requirement_is_rejected(self):
        with self.assertRaisesRegex(BuildError, "alignment requirement"):
            update.check_alignment(self.base, shared_library_alignment=1)


if __name__ == "__main__": unittest.main()
