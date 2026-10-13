"""Pinned ADB provisioning controls; no network, Windows executable or ADB call."""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import stat
import ssl
import tempfile
import unittest
from unittest import mock
import zipfile
import urllib.error

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
        opener = mock.Mock()
        opener.open.return_value = response
        with mock.patch.object(adb.urllib.request, "build_opener", return_value=opener) as factory:
            REAL_DOWNLOAD(target)
        self.assertEqual(target.read_bytes(), content)
        arguments, keywords = opener.open.call_args
        self.assertEqual(arguments[0].full_url, adb.URL)
        self.assertEqual(keywords["timeout"], 15)
        handler, redirects = factory.call_args.args
        self.assertTrue(handler._context.check_hostname)
        self.assertEqual(handler._context.verify_mode, ssl.CERT_REQUIRED)
        self.assertIsInstance(redirects, adb._HttpsRedirects)

    def test_pinned_official_constants(self):
        self.assertEqual(adb.VERSION, "37.0.1")
        self.assertEqual(adb.URL, "https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip")
        self.assertEqual(OFFICIAL_SHA, "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99")
        self.assertEqual(OFFICIAL_BYTES, 8044989)
        self.assertEqual(adb.URLS, (adb.URL,
            "https://dl-ssl.google.com/android/repository/platform-tools_r37.0.1-win.zip",
            "https://redirector.gvt1.com/edgedl/android/repository/platform-tools_r37.0.1-win.zip"))
        self.assertEqual(adb.LINUX["urls"], (adb.LINUX["url"],
            "https://dl-ssl.google.com/android/repository/platform-tools_r37.0.1-linux.zip",
            "https://redirector.gvt1.com/edgedl/android/repository/platform-tools_r37.0.1-linux.zip"))

    def test_timeout_uses_next_official_host_and_publishes_only_verified_bytes(self):
        attempted = []
        def request(value, **kwargs):
            attempted.append(value.full_url)
            if len(attempted) == 1:
                raise urllib.error.URLError(TimeoutError("fixture read timeout"))
            return contextlib.closing(io.BytesIO(self.archive.read_bytes()))
        opener = mock.Mock(); opener.open.side_effect = request
        with mock.patch.object(adb, "_download", wraps=REAL_DOWNLOAD), \
             mock.patch.object(adb.urllib.request, "build_opener", return_value=opener):
            executable = self.provision()
        self.assertTrue(executable.is_file())
        self.assertEqual(attempted, list(adb.URLS[:2]))
        self.assertEqual((self.scripts / ".quest-adb/platform-tools_r37.0.1-win.zip").read_bytes(), self.archive.read_bytes())
        self.assertFalse(list((self.scripts / ".quest-adb").glob("*.partial")))

    def test_third_host_is_tried_after_timeout_and_http_failure(self):
        content = self.archive.read_bytes()
        opener = mock.Mock()
        opener.open.side_effect = [TimeoutError("timeout"),
            urllib.error.HTTPError(adb.URLS[1], 403, "blocked", {}, None),
            contextlib.closing(io.BytesIO(content))]
        target = self.root / "download.zip"
        with mock.patch.object(adb.urllib.request, "build_opener", return_value=opener):
            REAL_DOWNLOAD(target)
        self.assertEqual(target.read_bytes(), content)
        self.assertEqual([row.args[0].full_url for row in opener.open.call_args_list], list(adb.URLS))

    def test_same_size_untrusted_mirror_bytes_fall_back_before_extraction(self):
        content = self.archive.read_bytes()
        tampered = bytearray(content); tampered[-1] ^= 1
        opener = mock.Mock()
        opener.open.side_effect = [contextlib.closing(io.BytesIO(tampered)),
                                  contextlib.closing(io.BytesIO(content))]
        with mock.patch.object(adb, "_download", wraps=REAL_DOWNLOAD), \
             mock.patch.object(adb.urllib.request, "build_opener", return_value=opener):
            self.assertTrue(self.provision().is_file())
        self.assertEqual(opener.open.call_count, 2)
        self.assertTrue(json.loads((self.tools / adb.MARKER).read_text())["complete"])

    def test_all_hosts_failed_leave_no_partial_or_ready_payload_and_redact_proxy_credentials(self):
        opener = mock.Mock()
        opener.open.side_effect = urllib.error.URLError("proxy https://secret-user:secret-password@private-host refused")
        output = io.StringIO()
        with mock.patch.object(adb, "_download", wraps=REAL_DOWNLOAD), \
             mock.patch.object(adb.urllib.request, "build_opener", return_value=opener), \
             contextlib.redirect_stdout(output):
            with self.assertRaises(adb.AdbBootstrapError) as caught:
                adb.ensure_windows_adb(self.scripts)
        self.assertEqual(opener.open.call_count, 3)
        self.assertIn("any official endpoint", str(caught.exception))
        for secret in ("secret-user", "secret-password", "private-host"):
            self.assertNotIn(secret, str(caught.exception) + output.getvalue())
        for url in adb.URLS:
            self.assertIn(url, str(caught.exception))
        self.assertFalse(self.tools.exists())
        self.assertFalse(list((self.scripts / ".quest-adb").glob("*.partial")))

    def test_tls_validation_error_can_try_another_tls_verified_host(self):
        opener = mock.Mock()
        opener.open.side_effect = [urllib.error.URLError(ssl.SSLCertVerificationError("fixture certificate mismatch")),
            contextlib.closing(io.BytesIO(self.archive.read_bytes()))]
        with mock.patch.object(adb.urllib.request, "build_opener", return_value=opener) as factory:
            REAL_DOWNLOAD(self.root / "download.zip")
        self.assertEqual(opener.open.call_count, 2)
        for call in factory.call_args_list:
            self.assertTrue(call.args[0]._context.check_hostname)
            self.assertEqual(call.args[0]._context.verify_mode, ssl.CERT_REQUIRED)

    def test_unsafe_redirects_are_rejected_and_official_cdn_https_is_accepted(self):
        handler = adb._HttpsRedirects()
        request = adb.urllib.request.Request(adb.URL)
        for url in ("http://dl-ssl.google.com/file.zip", "https://dl.google.com.evil.invalid/file.zip",
                    "https://user:secret@dl.google.com/file.zip", "https://dl.google.com:8443/file.zip"):
            with self.subTest(url=url), self.assertRaisesRegex(adb.AdbBootstrapError, "unexpected redirect"):
                handler.redirect_request(request, None, 302, "Found", {}, url)
        self.assertEqual(handler.redirect_request(request, None, 302, "Found", {},
            "https://rr5---sn-fixture.gvt1.com/edgedl/android/repository/file.zip").full_url,
            "https://rr5---sn-fixture.gvt1.com/edgedl/android/repository/file.zip")

    def test_oversized_first_host_falls_back_without_reusing_partial_bytes(self):
        content = self.archive.read_bytes()
        opener = mock.Mock()
        opener.open.side_effect = [contextlib.closing(io.BytesIO(content + b"oversize")),
                                  contextlib.closing(io.BytesIO(content))]
        target = self.root / "download.zip"
        with mock.patch.object(adb.urllib.request, "build_opener", return_value=opener):
            REAL_DOWNLOAD(target)
        self.assertEqual(target.read_bytes(), content)
        self.assertEqual(opener.open.call_count, 2)

    def test_interrupted_read_discards_partial_bytes_before_next_host(self):
        content = self.archive.read_bytes()
        class Interrupted(io.BytesIO):
            def read1(self, size):
                if self.tell():
                    raise TimeoutError("fixture read timeout after first block")
                return super().read1(32)
        opener = mock.Mock()
        opener.open.side_effect = [contextlib.closing(Interrupted(content)),
                                  contextlib.closing(io.BytesIO(content))]
        target = self.root / "download.zip"
        with mock.patch.object(adb.urllib.request, "build_opener", return_value=opener):
            REAL_DOWNLOAD(target)
        self.assertEqual(target.read_bytes(), content)
        self.assertEqual(opener.open.call_count, 2)

    def test_elapsed_download_limit_moves_to_next_endpoint(self):
        content = self.archive.read_bytes()
        opener = mock.Mock()
        opener.open.side_effect = [contextlib.closing(io.BytesIO(content)),
                                  contextlib.closing(io.BytesIO(content))]
        target = self.root / "download.zip"
        with mock.patch.object(adb.urllib.request, "build_opener", return_value=opener), \
             mock.patch.object(adb.time, "monotonic", side_effect=[0, 121, 0, 0, 0, 0]):
            REAL_DOWNLOAD(target)
        self.assertEqual(target.read_bytes(), content)
        self.assertEqual(opener.open.call_count, 2)

    def test_download_staging_permission_error_does_not_retry_other_hosts(self):
        target = self.root / "download.zip"
        with mock.patch.object(adb, "_download_once", side_effect=PermissionError("fixture locked cache")) as request:
            with self.assertRaises(PermissionError):
                REAL_DOWNLOAD(target)
        self.assertEqual(request.call_count, 1)

    def test_existing_staging_file_is_preserved_without_network(self):
        target = self.root / "download.zip"; target.write_bytes(b"unowned existing data")
        with mock.patch.object(adb.urllib.request, "build_opener", side_effect=AssertionError("No download")):
            with self.assertRaisesRegex(adb.AdbBootstrapError, "already exists"):
                REAL_DOWNLOAD(target)
        self.assertEqual(target.read_bytes(), b"unowned existing data")

    def test_linux_fallback_uses_same_linux_pin_and_never_a_windows_archive(self):
        content = self.archive.read_bytes()
        spec = dict(adb.LINUX, sha256=hashlib.sha256(content).hexdigest(), bytes=len(content))
        opener = mock.Mock()
        opener.open.side_effect = [TimeoutError("timeout"), contextlib.closing(io.BytesIO(content))]
        with mock.patch.object(adb.urllib.request, "build_opener", return_value=opener):
            REAL_DOWNLOAD(self.root / "download-linux.zip", spec)
        self.assertEqual([row.args[0].full_url for row in opener.open.call_args_list], list(adb.LINUX["urls"][:2]))


if __name__ == "__main__":
    unittest.main()
