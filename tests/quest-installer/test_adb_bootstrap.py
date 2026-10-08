"""Pinned ADB provisioning controls; no network, Windows executable or ADB call."""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import stat
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_adb_bootstrap", ROOT / "tools/quest-installer/adb_bootstrap.py")
adb = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(adb)
REAL_DOWNLOAD = adb._download
OFFICIAL_SHA = adb.SHA256
OFFICIAL_BYTES = adb.BYTES


class AdbBootstrapTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest adb fixture ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.scripts = self.root / "script folder's space"
        self.scripts.mkdir()
        self.archive = self.root / "fixture.zip"
        self.make_archive()
        self.downloads = []
        self.pin()
        self.download = mock.patch.object(adb, "_download", side_effect=self.receive)
        self.download.start()
        self.addCleanup(self.download.stop)

    def make_archive(self, omit=None, extra=None, version="37.0.1"):
        with zipfile.ZipFile(self.archive, "w") as zipped:
            for name in adb.REQUIRED:
                if name != omit:
                    data = ("Pkg.Revision=" + version + "\n") if name == "source.properties" else "pinned fixture " + name
                    zipped.writestr("platform-tools/" + name, data)
            if extra:
                for name, content in extra.items():
                    zipped.writestr(name, content)

    def pin(self):
        if hasattr(self, "hash_patch"):
            self.hash_patch.stop()
            self.size_patch.stop()
        self.hash_patch = mock.patch.object(adb, "SHA256", hashlib.sha256(self.archive.read_bytes()).hexdigest())
        self.size_patch = mock.patch.object(adb, "BYTES", self.archive.stat().st_size)
        self.hash_patch.start()
        self.size_patch.start()
        self.addCleanup(self.hash_patch.stop)
        self.addCleanup(self.size_patch.stop)

    def receive(self, destination):
        self.downloads.append(destination)
        shutil.copyfile(self.archive, destination)

    def provision(self):
        with contextlib.redirect_stdout(io.StringIO()):
            return adb.ensure_windows_adb(self.scripts)

    @property
    def tools(self):
        return self.scripts / ".quest-adb/platform-tools"

    def test_first_setup_is_private_and_retains_required_binaries_and_license(self):
        executable = self.provision()
        self.assertEqual(executable, self.tools / "adb.exe")
        self.assertEqual(len(self.downloads), 1)
        self.assertTrue(all((self.tools / name).is_file() for name in adb.REQUIRED))
        self.assertTrue((self.scripts / ".quest-adb/platform-tools_r37.0.1-win.zip").is_file())
        state = json.loads((self.tools / adb.MARKER).read_text())
        self.assertTrue(state["complete"])
        self.assertEqual(state["archiveSha256"], adb.SHA256)

    def test_valid_cache_reuse_has_no_download(self):
        first = self.provision()
        with mock.patch.object(adb, "_download", side_effect=AssertionError("Unexpected network request")):
            self.assertEqual(self.provision(), first)

    def test_previous_windows_receipt_without_host_reuses_existing_tools(self):
        first = self.provision()
        marker = first.parent / adb.MARKER
        state = json.loads(marker.read_text()); state.pop("host")
        adb._json(marker, state)
        with mock.patch.object(adb, "_download", side_effect=AssertionError("Existing Windows receipt remains valid")), \
             mock.patch.object(adb, "_extract", side_effect=AssertionError("No reinstall for receipt-only addition")):
            self.assertEqual(self.provision(), first)

    def test_corrupted_binary_is_repaired_from_cached_archive_offline(self):
        executable = self.provision()
        original = executable.read_bytes()
        executable.write_bytes(b"corrupted binary")
        with mock.patch.object(adb, "_download", side_effect=AssertionError("Unexpected network request")):
            self.assertEqual(self.provision(), executable)
        self.assertEqual(executable.read_bytes(), original)

    def test_missing_dll_is_repaired_offline(self):
        self.provision()
        dll = self.tools / "AdbWinUsbApi.dll"
        dll.unlink()
        self.provision()
        self.assertTrue(dll.is_file())
        self.assertEqual(len(self.downloads), 1)

    def test_incomplete_owned_tools_are_recreated(self):
        self.provision()
        adb._json(self.tools / adb.MARKER, adb._marker("tools", complete=False))
        self.provision()
        self.assertTrue(json.loads((self.tools / adb.MARKER).read_text())["complete"])

    def test_corrupt_cached_zip_is_redownloaded_before_repair(self):
        self.provision()
        (self.tools / "adb.exe").unlink()
        (self.scripts / ".quest-adb/platform-tools_r37.0.1-win.zip").write_bytes(b"corrupt archive")
        self.provision()
        self.assertEqual(len(self.downloads), 2)
        self.assertTrue((self.tools / "adb.exe").is_file())

    def test_sha_mismatch_prevents_extraction_and_cleans_partial(self):
        def tampered(destination):
            destination.write_bytes(b"tampered download")
        with mock.patch.object(adb, "_download", side_effect=tampered), mock.patch.object(adb, "_extract", side_effect=AssertionError("Unsafe extraction")):
            with self.assertRaisesRegex(adb.AdbBootstrapError, "SHA-256/size mismatch"):
                self.provision()
        self.assertFalse(self.tools.exists())
        self.assertFalse(list((self.scripts / ".quest-adb").glob("*.partial")))

    def test_partial_network_failure_can_retry_without_false_ready_marker(self):
        def interrupted(destination):
            destination.write_bytes(b"partial")
            raise OSError("fixture network failure")
        with mock.patch.object(adb, "_download", side_effect=interrupted):
            with self.assertRaisesRegex(adb.AdbBootstrapError, "fixture network failure"):
                self.provision()
        self.assertFalse(self.tools.exists())
        self.assertFalse(list((self.scripts / ".quest-adb").glob("*.partial")))
        self.assertTrue(self.provision().is_file())

    def test_unowned_cache_preserves_foreign_data_and_does_not_download(self):
        cache = self.scripts / ".quest-adb"
        cache.mkdir()
        valuable = cache / "user-data"
        valuable.write_text("retain")
        with self.assertRaisesRegex(adb.AdbBootstrapError, "unowned ADB folder"):
            self.provision()
        self.assertEqual(valuable.read_text(), "retain")
        self.assertFalse(self.downloads)

    def test_unowned_tools_inside_owned_cache_are_not_removed(self):
        self.tools.mkdir(parents=True)
        adb._json(self.scripts / ".quest-adb" / adb.MARKER, adb._marker("cache"))
        valuable = self.tools / "user-data"
        valuable.write_text("retain")
        with self.assertRaisesRegex(adb.AdbBootstrapError, "unowned ADB folder"):
            self.provision()
        self.assertEqual(valuable.read_text(), "retain")
        self.assertFalse(self.downloads)

    def test_zip_escape_and_windows_stream_paths_are_rejected(self):
        for name in ("../outside", "platform-tools/../../outside", "platform-tools\\..\\outside", "C:/outside", "platform-tools/adb.exe:stream"):
            self.make_archive(extra={name: "must not escape"})
            self.pin()
            with self.assertRaisesRegex(adb.AdbBootstrapError, "unsafe path"):
                self.provision()
            self.assertFalse(self.tools.exists())
            self.assertFalse((self.scripts / "outside").exists())
            self.assertFalse(list((self.scripts / ".quest-adb").glob("stage-*")))

    def test_zip_symlink_is_rejected(self):
        with zipfile.ZipFile(self.archive, "a") as zipped:
            entry = zipfile.ZipInfo("platform-tools/linked")
            entry.external_attr = (stat.S_IFLNK | 0o777) << 16
            zipped.writestr(entry, "../../outside")
        self.pin()
        with self.assertRaisesRegex(adb.AdbBootstrapError, "unsafe path"):
            self.provision()
        self.assertFalse(self.tools.exists())

    def test_existing_cache_symlink_does_not_modify_outside_directory(self):
        outside = self.root / "outside"
        outside.mkdir()
        valuable = outside / "valuable"
        valuable.write_text("retain")
        try:
            (self.scripts / ".quest-adb").symlink_to(outside, target_is_directory=True)
        except OSError:
            self.skipTest("Creating fixture links requires developer mode on this Windows host")
        with self.assertRaisesRegex(adb.AdbBootstrapError, "linked ADB cache path"):
            self.provision()
        self.assertEqual(valuable.read_text(), "retain")
        self.assertFalse(self.downloads)

    def test_link_inside_owned_tools_prevents_removal_or_outside_write(self):
        self.provision()
        outside = self.root / "outside"
        outside.mkdir()
        valuable = outside / "valuable"
        valuable.write_text("retain")
        try:
            (self.tools / "outside-link").symlink_to(outside, target_is_directory=True)
        except OSError:
            self.skipTest("Creating fixture links requires developer mode on this Windows host")
        with self.assertRaisesRegex(adb.AdbBootstrapError, "linked ADB cache path"):
            self.provision()
        self.assertEqual(valuable.read_text(), "retain")

    def test_windows_junction_attributes_are_rejected(self):
        path = self.scripts / "junction"
        metadata = type("Metadata", (), {"st_mode": stat.S_IFDIR, "st_file_attributes": 0x400})()
        with mock.patch.object(Path, "lstat", return_value=metadata):
            with self.assertRaisesRegex(adb.AdbBootstrapError, "linked"):
                adb._plain(path)

    def test_missing_required_file_or_wrong_version_fails_without_ready_payload(self):
        for omission, version in (("AdbWinApi.dll", "37.0.1"), ("NOTICE.txt", "37.0.1"), (None, "36.0.0")):
            self.make_archive(omit=omission, version=version)
            self.pin()
            with self.assertRaises(adb.AdbBootstrapError):
                self.provision()
            self.assertFalse(self.tools.exists())

    def test_invalid_replacement_keeps_old_owned_payload_until_verified(self):
        self.provision()
        (self.tools / "adb.exe").write_bytes(b"old damaged but retained")
        self.make_archive(omit="AdbWinApi.dll")
        self.pin()
        with self.assertRaisesRegex(adb.AdbBootstrapError, "missing required"):
            self.provision()
        self.assertEqual((self.tools / "adb.exe").read_bytes(), b"old damaged but retained")

    def test_bounded_exclusive_lock_and_release(self):
        lock = self.scripts / ".quest-adb.lock"
        with adb._lock(lock):
            with self.assertRaisesRegex(adb.AdbBootstrapError, "Another ADB setup"):
                with adb._lock(lock, attempts=1):
                    self.fail("Second lock acquired")
        with adb._lock(lock, attempts=1):
            pass

    def test_download_uses_verified_tls_bounded_timeout_and_pinned_url(self):
        content = self.archive.read_bytes()
        response = contextlib.closing(io.BytesIO(content))
        target = self.root / "download.zip"
        with mock.patch.object(adb.urllib.request, "urlopen", return_value=response) as request:
            REAL_DOWNLOAD(target)
        self.assertEqual(target.read_bytes(), content)
        arguments, keywords = request.call_args
        self.assertEqual(arguments[0].full_url, adb.URL)
        self.assertEqual(keywords["timeout"], 30)
        self.assertTrue(keywords["context"].check_hostname)

    def test_pinned_official_constants(self):
        self.assertEqual(adb.VERSION, "37.0.1")
        self.assertEqual(adb.URL, "https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip")
        self.assertEqual(OFFICIAL_SHA, "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99")
        self.assertEqual(OFFICIAL_BYTES, 8044989)


if __name__ == "__main__":
    unittest.main()
