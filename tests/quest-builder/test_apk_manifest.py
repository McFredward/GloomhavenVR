"""Bounded compiled-XML mutations retain namespaces and unrelated attributes."""
from pathlib import Path
import struct
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import apk_manifest
from storage import BuildError


def manifest(utf8=True, package="dev.gloomhavenvr.quest"):
    strings = ["manifest", "package", "versionCode", "versionName", apk_manifest.ANDROID, package, "B643", "unrelated", "keep"]
    data = b""; offsets = []
    for text in strings:
        offsets.append(len(data)); raw = text.encode("utf-8" if utf8 else "utf-16le")
        data += (bytes([len(text), len(raw)]) + raw + b"\0") if utf8 else struct.pack("<H", len(text)) + raw + b"\0\0"
    data += b"\0" * (-len(data) % 4); start = 28 + 4 * len(strings)
    pool = struct.pack("<HH6I", 1, 28, start + len(data), len(strings), 0, 0x100 if utf8 else 0, start, 0) + struct.pack("<" + "I" * len(strings), *offsets) + data
    def attribute(ns, name, raw, kind, value): return struct.pack("<IIIHBBI", ns, name, raw, 8, 0, kind, value)
    attrs = attribute(0xffffffff, 1, 5, 3, 5) + attribute(4, 2, 0xffffffff, 0x10, 643) + attribute(4, 3, 6, 3, 6) + attribute(0xffffffff, 7, 8, 3, 8)
    node = struct.pack("<HHIII", 0x102, 16, 36 + len(attrs), 1, 0xffffffff) + struct.pack("<II6H", 0xffffffff, 0, 20, 20, 4, 0, 0, 0) + attrs
    return struct.pack("<HHI", 3, 8, 8 + len(pool) + len(node)) + pool + node


class AndroidManifestTests(unittest.TestCase):
    def test_utf8_and_utf16_version_fields_only(self):
        for utf8 in (True, False):
            old = manifest(utf8); new = apk_manifest.update_versions(old, 644, "0.1.0.B644.123456789abc")
            size = struct.unpack_from("<I", new, 12)[0]
            _, strings, index = apk_manifest._pool(new[8:8 + size], "unused")
            self.assertEqual(strings[-1], "0.1.0.B644.123456789abc"); self.assertEqual(index, 10)
            element = new[8 + size:]
            self.assertEqual(struct.unpack_from("<I", element, 72)[0], 644)
            self.assertEqual(struct.unpack_from("<I", element, 92)[0], 9)
            self.assertEqual(element[-20:], old[-20:])

    def test_wrong_package_and_invalid_version_fail(self):
        with self.assertRaisesRegex(BuildError, "package does not match"): apk_manifest.update_versions(manifest(package="different.app"), 644, "B644")
        for code in (0, -1, True, 2**32):
            with self.assertRaisesRegex(BuildError, "version is invalid"): apk_manifest.update_versions(manifest(), code, "B644")

    def test_unicode_and_long_utf8_name_remain_decodable(self):
        name = "é" * 128
        new = apk_manifest.update_versions(manifest(), 644, name)
        size = struct.unpack_from("<I", new, 12)[0]
        _, strings, _ = apk_manifest._pool(new[8:8 + size], "unused")
        self.assertEqual(strings[-1], name)

    def test_bad_chunk_boundaries_reject_before_output(self):
        raw = bytearray(manifest()); struct.pack_into("<I", raw, 12, len(raw) + 1)
        with self.assertRaisesRegex(BuildError, "boundary"): apk_manifest.update_versions(raw, 644, "B644")


if __name__ == "__main__": unittest.main()
