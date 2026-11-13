"""Actual input witnesses avoid duplicate reads without persistent trust."""
import hashlib
import importlib.util
import contextlib
import io
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location("quest_digest_reuse_tests", Path(__file__).resolve().parents[2] / "tools/quest-installer/installer.py")
installer = importlib.util.module_from_spec(SPEC)
import sys
sys.modules[SPEC.name] = installer; SPEC.loader.exec_module(installer)


class DigestReuseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); self.path = self.root / "large-input.bin"
        self.path.write_bytes(b"qualified original bytes")
        installer._DIGEST_CACHE.clear()

    def test_same_witness_hashes_payload_once(self):
        expected = hashlib.sha256(self.path.read_bytes()).hexdigest()
        with mock.patch.object(installer.hashlib, "sha256", wraps=hashlib.sha256) as hashes:
            self.assertEqual(installer.digest(self.path), expected)
            self.assertEqual(installer.digest(self.path), expected)
            self.assertEqual(hashes.call_count, 1)

    def test_same_size_mutation_invalidates_the_witness(self):
        first = installer.digest(self.path); old = self.path.stat()
        self.path.write_bytes(b"modified different bytes")
        os.utime(self.path, ns=(old.st_atime_ns, old.st_mtime_ns + 1000000))
        self.assertNotEqual(installer.digest(self.path), first)

    def test_same_size_replacement_with_restored_mtime_invalidates_inode(self):
        first = installer.digest(self.path); old = self.path.stat()
        other = self.root / "replacement.bin"; other.write_bytes(b"modified different bytes")
        os.utime(other, ns=(old.st_atime_ns, old.st_mtime_ns)); os.replace(other, self.path)
        self.assertNotEqual(installer.digest(self.path), first)

    def test_changed_during_hash_never_enters_cache(self):
        before = installer._digest_witness(self.path)
        after = (*before[:-2], before[-2] + 1000000, before[-1])
        with mock.patch.object(installer, "_digest_witness", side_effect=(before, after)):
            with self.assertRaisesRegex(installer.InstallError, "changed while its checksum"):
                installer.digest(self.path)
        self.assertEqual(installer._DIGEST_CACHE, {})

    def test_cache_is_bounded_and_cli_start_drops_old_invocation_witnesses(self):
        for index in range(40):
            path = self.root / (str(index) + ".bin"); path.write_bytes(str(index).encode())
            installer.digest(path)
        self.assertEqual(len(installer._DIGEST_CACHE), 32)
        with contextlib.redirect_stdout(io.StringIO()), self.assertRaises(SystemExit): installer.main(["--help"])
        self.assertEqual(installer._DIGEST_CACHE, {})
