"""Pinned acquisition survives upstream removal without changing runtime bytes."""
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch
import urllib.error

ROOT = Path(__file__).resolve().parents[2] / "tools/quest-procedural-runtime"
sys.path.insert(0, str(ROOT))
import runtime


class PinnedDownloadTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.cache = Path(self.temp.name) / "downloads"
        self.lock = json.loads((ROOT / "proton.lock.json").read_text())["fex"]
        self.url = self.lock["url"]
        self.body = b"Same pinned artifact fixture, never a runtime binary."
        self.sha = hashlib.sha256(self.body).hexdigest()
        self.name = "fex-fixture.deb"

    def fetch(self):
        return runtime.fetch(self.cache, self.name, self.url, self.sha)

    def test_official_build_archive_is_attempted_after_unavailable_librarian(self):
        missing = urllib.error.HTTPError(self.url, 404, "Not Found", {}, io.BytesIO(b"not found"))
        with patch.object(runtime.urllib.request, "urlopen", side_effect=[missing, io.BytesIO(self.body)]) as opener:
            path = self.fetch()
        self.assertEqual(path.read_bytes(), self.body)
        self.assertTrue(missing.fp.closed)
        self.assertEqual([call.args[0] for call in opener.call_args_list], list(runtime.DOWNLOAD_MIRRORS[self.url]))
        self.assertFalse(list(self.cache.glob("*.partial")))

    def test_wrong_mirror_bytes_are_discarded_before_exact_fallback(self):
        with patch.object(runtime.urllib.request, "urlopen", side_effect=[io.BytesIO(b"different build"), io.BytesIO(self.body)]) as opener:
            self.assertEqual(self.fetch().read_bytes(), self.body)
        self.assertEqual(opener.call_count, 2)
        self.assertFalse(list(self.cache.glob("*.partial")))

    def test_existing_correct_archive_is_reused_without_network(self):
        self.cache.mkdir(); (self.cache / self.name).write_bytes(self.body)
        with patch.object(runtime.urllib.request, "urlopen", side_effect=AssertionError("No acquisition is needed")):
            self.assertEqual(self.fetch().read_bytes(), self.body)

    def test_corrupt_cached_archive_is_repaired_atomically(self):
        self.cache.mkdir(); previous = self.cache / self.name; previous.write_bytes(b"interrupted earlier archive")
        def download(url, **kwargs):
            self.assertEqual(previous.read_bytes(), b"interrupted earlier archive")
            return io.BytesIO(self.body)
        with patch.object(runtime.urllib.request, "urlopen", side_effect=download):
            self.assertEqual(self.fetch().read_bytes(), self.body)
        self.assertFalse(list(self.cache.glob("*.partial")))

    def test_no_wrong_bytes_or_partial_are_published_after_all_sources_fail(self):
        self.cache.mkdir(); previous = self.cache / self.name; previous.write_bytes(b"keep old cache until replacement qualifies")
        with patch.object(runtime.urllib.request, "urlopen", side_effect=lambda *_a, **_k: io.BytesIO(b"unqualified")) as opener:
            with self.assertRaisesRegex(RuntimeError, "acquisition failed.*fex-fixture.deb.*checksum mismatch"):
                self.fetch()
        self.assertEqual(opener.call_count, len(runtime.DOWNLOAD_MIRRORS[self.url]) + 1)
        self.assertEqual(previous.read_bytes(), b"keep old cache until replacement qualifies")
        self.assertFalse(list(self.cache.glob("*.partial")))

    def test_unknown_source_gets_no_guessed_mirror_or_version(self):
        with patch.object(runtime.urllib.request, "urlopen", return_value=io.BytesIO(self.body)) as opener:
            path = runtime.fetch(self.cache, self.name, "https://source.invalid/new-version.deb", self.sha)
        self.assertEqual(path.read_bytes(), self.body)
        opener.assert_called_once_with("https://source.invalid/new-version.deb", timeout=60)

    def test_interrupted_read_is_removed_before_retry(self):
        response = io.BytesIO(self.body); response.read = Mock(side_effect=OSError("fixture interrupted transfer"))
        with patch.object(runtime.urllib.request, "urlopen", side_effect=[response, io.BytesIO(self.body)]):
            self.assertEqual(self.fetch().read_bytes(), self.body)
        self.assertTrue(response.closed)
        self.assertFalse(list(self.cache.glob("*.partial")))


if __name__ == "__main__":
    unittest.main()
