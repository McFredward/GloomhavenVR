"""Build-input integrity and artifact staging for the real native codec seam."""
import contextlib
import hashlib
import http.server
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import ssl
import subprocess
import tarfile
import tempfile
import threading
import unittest
from unittest.mock import Mock, patch
import urllib.error

MODULE_PATH = Path(__file__).resolve().parents[2] / "tools/quest-network/native.py"
SPEC = importlib.util.spec_from_file_location("quest_network_native", MODULE_PATH)
native = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(native)


class NativeInputTests(unittest.TestCase):
    def test_windows_ndk_uses_executable_with_literal_path_and_android_target(self):
        with tempfile.TemporaryDirectory(prefix="Quest build & 100% ") as directory:
            ndk = Path(directory)
            tools = ndk / "toolchains/llvm/prebuilt/windows-x86_64/bin"
            tools.mkdir(parents=True)
            for name in ("clang.exe", "llvm-nm.exe"):
                (tools / name).write_bytes(b"tool path fixture")
            command, auditor = native.android_compiler(ndk, platform="win32")
            self.assertEqual(command[0], str(tools / "clang.exe"))
            self.assertEqual(command[1], "--target=aarch64-linux-android29")
            self.assertEqual(command[2], "--sysroot=" + str(tools.parent / "sysroot"))
            self.assertEqual(auditor, tools / "llvm-nm.exe")
            self.assertFalse(any(value.endswith(".cmd") for value in command))
            (tools / "clang.exe").unlink()
            (tools / "aarch64-linux-android29-clang.cmd").write_bytes(b"batch wrapper")
            with self.assertRaisesRegex(RuntimeError, "compiler/symbol auditor"):
                native.android_compiler(ndk, platform="win32")

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
            with patch.object(native.urllib.request, "build_opener", side_effect=AssertionError("network unexpected")):
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

    def response(self, content, url=None):
        response = io.BytesIO(content)
        response.geturl = lambda: url or native.OPUS_URL
        return response

    def test_verified_download_is_cached_and_second_fetch_never_contacts_network(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            checksum = self.archive(root)
            archive = root / "opus-1.5.2.tar.gz"
            content = archive.read_bytes()
            archive.unlink()
            opener = Mock()
            opener.open.return_value = self.response(content)
            output = io.StringIO()
            with patch.object(native, "OPUS_ARCHIVE_BYTES", len(content)), patch.object(native, "OPUS_SHA256", checksum), \
                    patch.object(native.urllib.request, "build_opener", return_value=opener), contextlib.redirect_stdout(output):
                source = native.fetch_source(root)
                self.assertEqual((source / "verified.c").read_bytes(), b"verified source from pinned archive\n")
                self.assertEqual(archive.read_bytes(), content)
                source.joinpath("verified.c").write_bytes(b"edited extracted source")
                native.fetch_source(root)
                self.assertEqual((source / "verified.c").read_bytes(), b"verified source from pinned archive\n")
            self.assertEqual(opener.open.call_count, 1)
            self.assertIn("verified Opus source", output.getvalue())
            self.assertFalse(archive.with_suffix(".partial").exists())

    def test_download_checksum_failure_is_not_retried_or_cached(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            opener = Mock()
            opener.open.return_value = self.response(b"wrong bytes")
            with patch.object(native, "OPUS_ARCHIVE_BYTES", len(b"wrong bytes")), \
                    patch.object(native.urllib.request, "build_opener", return_value=opener), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(RuntimeError, "pinned checksum"):
                    native.fetch_source(root)
            self.assertEqual(opener.open.call_count, 1)
            self.assertEqual(list(root.iterdir()), [])

    def test_failed_archive_publication_removes_partial_and_does_not_retry_download(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.object(native, "_download_source", return_value=b"verified download") as download, \
                    patch.object(native.os, "replace", side_effect=PermissionError("archive publication denied")):
                with self.assertRaisesRegex(PermissionError, "publication denied"):
                    native.fetch_source(root)
            self.assertEqual(download.call_count, 1)
            self.assertEqual(list(root.iterdir()), [])

    def test_oversized_source_is_not_retried_or_cached(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            opener = Mock()
            opener.open.return_value = self.response(b"oversized bytes")
            with patch.object(native, "OPUS_ARCHIVE_BYTES", 1), \
                    patch.object(native.urllib.request, "build_opener", return_value=opener), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(RuntimeError, "pinned size"):
                    native.fetch_source(root)
            self.assertEqual(opener.open.call_count, 1)
            self.assertEqual(list(root.iterdir()), [])

    def test_both_network_failures_are_bounded_and_actionable(self):
        opener = Mock()
        opener.open.side_effect = [urllib.error.URLError("connection refused"), TimeoutError("timed out")]
        with patch.object(native.urllib.request, "build_opener", return_value=opener), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, "Opus source download failed with verified HTTPS") as caught:
                native._download_source()
        self.assertEqual(opener.open.call_count, 2)
        self.assertEqual([call.args[0].full_url for call in opener.open.call_args_list], [native.OPUS_URL, native.OPUS_FALLBACK_URL])
        self.assertTrue(all(call.kwargs["timeout"] == 60 for call in opener.open.call_args_list))
        self.assertIn("github.com", str(caught.exception))
        self.assertIn("downloads.xiph.org", str(caught.exception))
        self.assertIn("Keep the workspace", str(caught.exception))
        self.assertIn("system CA certificates", str(caught.exception))

    def test_measured_download_progress_is_bounded_and_cdn_credentials_are_redacted(self):
        content = b"verified source bytes" * 90_000
        checksum = hashlib.sha256(content).hexdigest()
        final = "https://release-assets.githubusercontent.com/github-production/opus.tar.gz?token=PRIVATE-CREDENTIAL&signature=PRIVATE-SIGNATURE"
        opener = Mock()
        opener.open.return_value = self.response(content, final)
        output = io.StringIO()
        with patch.object(native, "OPUS_ARCHIVE_BYTES", len(content)), patch.object(native, "OPUS_SHA256", checksum), \
                patch.object(native.urllib.request, "build_opener", return_value=opener), \
                patch.dict(os.environ, {"GHVRQ_WIZARD_PROGRESS": "1"}), contextlib.redirect_stdout(output):
            self.assertEqual(native._download_source(), content)
        events = [json.loads(line.removeprefix("GHVRQ_PROGRESS ")) for line in output.getvalue().splitlines() if line.startswith("GHVRQ_PROGRESS ")]
        self.assertEqual(events[0]["done"], 0)
        self.assertEqual(events[-1]["done"], len(content))
        self.assertTrue(all(event["total"] == len(content) and event["operation"] == "native-runtime" and event["status"] == "progress" for event in events))
        self.assertEqual([event["done"] for event in events], sorted(event["done"] for event in events))
        self.assertLessEqual(len(events), 20)
        self.assertNotIn("PRIVATE-", output.getvalue())
        reason = native._download_reason(urllib.error.URLError("failed at " + final))
        self.assertNotIn("PRIVATE-", reason)
        self.assertIn("release-assets.githubusercontent.com", reason)

    def test_http_response_is_rejected_before_reading(self):
        response = self.response(b"untrusted", "http://example.invalid/opus.tar.gz")
        opener = Mock()
        opener.open.return_value = response
        with patch.object(native.urllib.request, "build_opener", return_value=opener), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, "outside verified HTTPS"):
                native._download_source()
        self.assertEqual(opener.open.call_count, 1)

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


@unittest.skipUnless(shutil.which("openssl"), "Actual TLS fixtures require the OpenSSL command-line tool")
class VerifiedDownloadTlsTests(unittest.TestCase):
    """Real certificate rejection and redirect controls, independent of the web."""
    @classmethod
    def setUpClass(cls):
        cls.fixture = tempfile.TemporaryDirectory(prefix="quest opus TLS ")
        cls.root = Path(cls.fixture.name)
        for name in ("trusted", "untrusted"):
            subprocess.run([shutil.which("openssl"), "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
                            "-subj", "/CN=Quest Opus fixture", "-addext", "subjectAltName=IP:127.0.0.1",
                            "-keyout", str(cls.root / (name + ".key")), "-out", str(cls.root / (name + ".pem"))],
                           check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    @classmethod
    def tearDownClass(cls):
        cls.fixture.cleanup()

    def server(self, name, *, body=b"verified fixture", redirect=None):
        requests = []
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                requests.append(self.path)
                self.send_response(302 if redirect else 200)
                if redirect: self.send_header("Location", redirect)
                else: self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                if not redirect: self.wfile.write(body)
            def log_message(self, *_): pass
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(self.root / (name + ".pem"), self.root / (name + ".key"))
        server.socket = context.wrap_socket(server.socket, server_side=True)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(server.server_close)
        self.addCleanup(thread.join, 2)
        self.addCleanup(server.shutdown)
        return "https://127.0.0.1:" + str(server.server_port) + "/opus.tar.gz", requests

    def context(self):
        result = ssl.create_default_context(cafile=str(self.root / "trusted.pem"))
        self.assertEqual(result.verify_mode, ssl.CERT_REQUIRED)
        self.assertTrue(result.check_hostname)
        return result

    def test_real_rejected_primary_certificate_uses_verified_fallback(self):
        content = b"TLS-verified source fixture"
        primary, primary_requests = self.server("untrusted")
        fallback, fallback_requests = self.server("trusted", body=content)
        context = self.context()
        output = io.StringIO()
        with patch.object(native, "OPUS_URL", primary), patch.object(native, "OPUS_FALLBACK_URL", fallback), \
                patch.object(native, "OPUS_ARCHIVE_BYTES", len(content)), patch.object(native, "OPUS_SHA256", hashlib.sha256(content).hexdigest()), \
                patch.object(native.ssl, "create_default_context", return_value=context), contextlib.redirect_stdout(output):
            self.assertEqual(native._download_source(), content)
        self.assertEqual(primary_requests, [])  # TLS rejects before an HTTP request.
        self.assertEqual(fallback_requests, ["/opus.tar.gz"])
        self.assertIn("TLS certificate validation failed", output.getvalue())
        self.assertIn("(2/2)", output.getvalue())
        self.assertEqual(context.verify_mode, ssl.CERT_REQUIRED)
        self.assertTrue(context.check_hostname)

    def test_real_https_to_http_redirect_is_rejected_without_fallback(self):
        primary, requests = self.server("trusted", redirect="http://127.0.0.1:1/forbidden.tar.gz")
        context = self.context()
        with patch.object(native, "OPUS_URL", primary), patch.object(native, "OPUS_FALLBACK_URL", "https://127.0.0.1:1/never-requested"), \
                patch.object(native.ssl, "create_default_context", return_value=context), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(RuntimeError, "redirect outside verified HTTPS"):
                native._download_source()
        self.assertEqual(requests, ["/opus.tar.gz"])


if __name__ == "__main__":
    unittest.main()
