"""Build-input integrity and artifact staging for the real native codec seam."""
import hashlib
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

MODULE_PATH = Path(__file__).resolve().parents[2] / "tools/quest-network/native.py"
SPEC = importlib.util.spec_from_file_location("quest_network_native", MODULE_PATH)
native = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(native)


class NativeInputTests(unittest.TestCase):
    def archive(self, root, member="opus-1.5.2/verified.c", link=False):
        output = root / "opus-1.5.2.tar.gz"
        with tarfile.open(output, "w:gz") as package:
            item = tarfile.TarInfo(member)
            if link:
                item.type = tarfile.SYMTYPE
                item.linkname = "/tmp/quest-native-untrusted"
                package.addfile(item)
            else:
                content = b"verified source from pinned archive\n"
                item.size = len(content)
                package.addfile(item, io.BytesIO(content))
        return hashlib.sha256(output.read_bytes()).hexdigest()

    def test_corrupt_cached_archive_is_rejected_without_download(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "opus-1.5.2.tar.gz").write_bytes(b"corrupt")
            with patch.object(native.urllib.request, "urlopen", side_effect=AssertionError("network unexpected")):
                with self.assertRaisesRegex(RuntimeError, "pinned checksum"):
                    native.fetch_source(root)

    def test_extracted_edits_do_not_acquire_pinned_source_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            checksum = self.archive(root)
            source = root / "opus-1.5.2"
            source.mkdir()
            (source / "verified.c").write_bytes(b"edited unverified input")
            (source / "injected.c").write_bytes(b"extra")
            with patch.object(native, "OPUS_SHA256", checksum):
                verified = native.fetch_source(root)
            self.assertEqual((verified / "verified.c").read_bytes(), b"verified source from pinned archive\n")
            self.assertFalse((verified / "injected.c").exists())

    def test_nonlocal_or_link_archive_member_is_rejected(self):
        for member, link in [("../outside.c", False), ("/tmp/outside.c", False),
                             ("other-root/input.c", False), ("opus-1.5.2/link.c", True)]:
            with self.subTest(member=member), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                checksum = self.archive(root, member, link)
                with patch.object(native, "OPUS_SHA256", checksum):
                    with self.assertRaisesRegex(RuntimeError, "nonlocal source member"):
                        native.fetch_source(root)

    def test_stage_includes_exact_codec_and_license(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "cache/libopus_egpv.so"
            source.parent.mkdir()
            source.write_bytes(b"private native staging fixture")
            source.with_name("OPUS-LICENSE.txt").write_text("fixture notice\n", encoding="utf-8")
            with patch.object(native, "build_network_native", return_value=source):
                receipt = native.stage(root / "cache", root / "ndk", root / "plugins", root / "notices")
            self.assertEqual(Path(receipt["library"]).read_bytes(), source.read_bytes())
            self.assertEqual(receipt["librarySha256"], native.digest(source))
            self.assertEqual(Path(receipt["license"]).read_text(encoding="utf-8"), "fixture notice\n")
            self.assertEqual(receipt["licenseSha256"], native.digest(source.with_name("OPUS-LICENSE.txt")))

    def test_stage_refuses_missing_redistribution_notice(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "libopus_egpv.so"
            source.write_bytes(b"fixture")
            with patch.object(native, "build_network_native", return_value=source):
                with self.assertRaisesRegex(RuntimeError, "license notice"):
                    native.stage(root, root / "ndk", root / "plugins", root / "notices")
            self.assertFalse((root / "plugins").exists())


if __name__ == "__main__":
    unittest.main()
