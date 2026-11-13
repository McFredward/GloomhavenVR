"""Separate installer CLI processes prove digest/CRC retry-cache boundaries.

Payload reads and CRC calls wrap the real operations rather than substituting
successful qualification. Ordinary CLI attempts stop at an explicitly empty
ADB device list; no hardware installation outcome is asserted.
"""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile

REPO = Path(__file__).resolve().parents[2]
INSTALLER = Path(os.environ.get("QUEST_INSTALLER_UNDER_TEST", REPO / "tools/quest-installer/installer.py"))


def probe(installer_source, operation, selected, cache_path):
    selected = Path(selected).absolute(); cache_path = Path(cache_path)
    spec = importlib.util.spec_from_file_location("quest_installer_probe", installer_source)
    installer = importlib.util.module_from_spec(spec); sys.modules[spec.name] = installer
    spec.loader.exec_module(installer)
    trace = {"payloadReadBytes": 0, "payloadOpens": 0, "crcCalls": 0, "crcResults": [], "adbCalls": []}
    original_open, original_crc = Path.open, zipfile.ZipFile.testzip

    class CountingReader:
        def __init__(self, stream): self.stream = stream
        def __enter__(self): self.stream.__enter__(); return self
        def __exit__(self, *args): return self.stream.__exit__(*args)
        def __getattr__(self, name): return getattr(self.stream, name)
        def read(self, *args):
            block = self.stream.read(*args); trace["payloadReadBytes"] += len(block); return block

    def count_open(path, *args, **kwargs):
        stream = original_open(path, *args, **kwargs)
        mode = args[0] if args else kwargs.get("mode", "r")
        if path.absolute() == selected and mode == "rb":
            trace["payloadOpens"] += 1
            return CountingReader(stream)
        return stream

    def count_crc(archive):
        trace["crcCalls"] += 1
        result = original_crc(archive)
        trace["crcResults"].append(result)
        return result

    def no_device(command, **kwargs):
        trace["adbCalls"].append(command[1:])
        if command[1:] != ["devices", "-l"]:
            raise AssertionError("Unexpected device mutation in qualification control: " + repr(command))
        return subprocess.CompletedProcess(command, 0, "List of devices attached\n", "")

    Path.open, zipfile.ZipFile.testzip = count_open, count_crc
    output, error = io.StringIO(), io.StringIO()
    try:
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(error):
            if operation == "digest":
                installer._INPUT_CACHE_PATH = cache_path
                trace["digest"] = installer.digest(selected)
                trace["returncode"] = 0
            else:
                fixture_adb = selected.parent / "qualification-only-adb"
                args = ["--config", str(cache_path.parent / "wireless-install.json"), "--adb", str(fixture_adb)]
                if operation.startswith("handoff"):
                    args += ["--handoff", str(selected.with_suffix(".handoff.json"))]
                else:
                    args += ["--apk", str(selected)]
                if operation.endswith("dry-run"):
                    args.append("--dry-run")
                trace["returncode"] = installer.main(args, runner=no_device)
    finally:
        Path.open, zipfile.ZipFile.testzip = original_open, original_crc
    trace["stdout"], trace["stderr"] = output.getvalue(), error.getvalue()
    print(json.dumps(trace))


class InputCacheIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="quest-cache-cli-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.apk = self.root / "selected completed.apk"
        self.cache = self.root / "state" / "qualified-inputs.json"
        self.make_apk()
        (self.root / "qualification-only-adb").write_bytes(b"explicit no-device subprocess boundary")

    def make_apk(self, payload=b"a" * (1024 * 1024)):
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr("AndroidManifest.xml", b"local fixture manifest")
            archive.writestr("assets/data.bin", payload)

    def corrupt_payload(self):
        with zipfile.ZipFile(self.apk) as archive:
            offset = archive.getinfo("assets/data.bin").header_offset
        with self.apk.open("r+b") as stream:
            stream.seek(offset)
            header = stream.read(30)
            name_size, extra_size = struct.unpack_from("<HH", header, 26)
            stream.seek(offset + 30 + name_size + extra_size)
            stream.write(b"!")

    def invoke(self, operation="manual-cli"):
        result = subprocess.run([sys.executable, "-I", "-B", str(Path(__file__).resolve()), "--probe", str(INSTALLER), operation, str(self.apk), str(self.cache)], capture_output=True, text=True, timeout=12)
        self.assertEqual(result.returncode, 0, result.stderr)
        try: return json.loads(result.stdout)
        except ValueError: self.fail("Probe did not produce its bounded JSON result: " + result.stdout[-2000:])

    def sha(self):
        return hashlib.sha256(self.apk.read_bytes()).hexdigest()

    def assert_waiting_for_quest(self, row):
        self.assertEqual(row["returncode"], 1)
        self.assertIn("Quest connection required:", row["stderr"])
        self.assertIn(["devices", "-l"], row["adbCalls"])

    def state_snapshot(self):
        folder = self.cache.parent
        return {str(path.relative_to(folder)): path.read_bytes() for path in folder.rglob("*") if path.is_file()} if folder.exists() else {}

    def test_unchanged_input_is_reused_between_real_processes_without_payload_read(self):
        first, second = self.invoke("digest"), self.invoke("digest")
        self.assertEqual(first["digest"], self.sha())
        self.assertEqual(first["payloadReadBytes"], self.apk.stat().st_size)
        self.assertEqual(second["digest"], self.sha())
        self.assertEqual(second["payloadReadBytes"], 0)
        self.assertEqual(second["payloadOpens"], 0)

    def test_same_size_replacement_preserving_mtime_requires_new_digest(self):
        first = self.invoke("digest"); old = self.apk.stat()
        replacement = self.root / "replacement.apk"
        with zipfile.ZipFile(replacement, "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr("AndroidManifest.xml", b"local fixture manifest")
            archive.writestr("assets/data.bin", b"b" * (1024 * 1024))
        self.assertEqual(replacement.stat().st_size, old.st_size)
        os.utime(replacement, ns=(old.st_atime_ns, old.st_mtime_ns))
        os.replace(replacement, self.apk)
        second = self.invoke("digest")
        self.assertNotEqual(first["digest"], second["digest"])
        self.assertEqual(second["digest"], self.sha())
        self.assertEqual(second["payloadReadBytes"], self.apk.stat().st_size)

    def test_corrupted_cache_requalifies_exactly_one_local_input(self):
        self.invoke("digest")
        self.cache.write_bytes(b"interrupted-cache-json")
        resumed = self.invoke("digest")
        self.assertEqual(resumed["digest"], self.sha())
        self.assertEqual(resumed["payloadOpens"], 1)
        self.assertEqual(resumed["payloadReadBytes"], self.apk.stat().st_size)
        self.assertEqual(self.invoke("digest")["payloadReadBytes"], 0)

    def test_digest_receipt_does_not_qualify_manual_apk_crc(self):
        self.invoke("digest")
        self.assertFalse(self.cache.with_name("qualified-apks.json").exists())
        selected = self.invoke()
        self.assert_waiting_for_quest(selected)
        self.assertEqual(selected["payloadReadBytes"], 0)
        self.assertEqual(selected["crcCalls"], 1)
        self.assertEqual(selected["crcResults"], [None])
        self.assertTrue(self.cache.with_name("qualified-apks.json").is_file())

    def test_manual_crc_success_is_reused_by_independent_cli_retry(self):
        first, second = self.invoke(), self.invoke()
        self.assert_waiting_for_quest(first); self.assert_waiting_for_quest(second)
        self.assertEqual(first["crcCalls"], 1)
        self.assertEqual(first["crcResults"], [None])
        self.assertEqual(second["crcCalls"], 0)
        self.assertEqual(second["payloadReadBytes"], 0)

    def test_manual_crc_failure_never_qualifies_later_cli_attempt(self):
        self.corrupt_payload()
        first, second = self.invoke(), self.invoke()
        for row in (first, second):
            self.assertEqual(row["returncode"], 1)
            self.assertIn("incomplete or corrupt", row["stderr"])
            self.assertEqual(row["crcCalls"], 1)
            self.assertEqual(row["crcResults"], ["assets/data.bin"])
            self.assertEqual(row["adbCalls"], [])
        self.assertEqual(first["payloadReadBytes"], self.apk.stat().st_size)
        self.assertEqual(second["payloadReadBytes"], 0)
        self.assertFalse(self.cache.with_name("qualified-apks.json").exists())

    def test_changed_apk_invalidates_previous_manual_crc_success(self):
        first = self.invoke(); self.assert_waiting_for_quest(first)
        self.corrupt_payload()
        second = self.invoke()
        self.assertEqual(second["returncode"], 1)
        self.assertIn("incomplete or corrupt", second["stderr"])
        self.assertEqual(second["crcCalls"], 1)
        self.assertEqual(second["crcResults"], ["assets/data.bin"])
        self.assertEqual(second["payloadReadBytes"], self.apk.stat().st_size)
        self.assertEqual(second["adbCalls"], [])

    def test_manual_dry_run_creates_no_settings_or_qualification_cache(self):
        row = self.invoke("manual-dry-run")
        self.assertEqual(row["returncode"], 0, row["stderr"])
        self.assertEqual(row["crcCalls"], 1)
        self.assertEqual(row["crcResults"], [None])
        self.assertEqual(row["adbCalls"], [])
        self.assertFalse(self.cache.parent.exists(), "Dry run created persistent state")

    def test_dry_run_does_not_modify_existing_settings_or_cache(self):
        self.assert_waiting_for_quest(self.invoke())
        config = self.cache.parent / "wireless-install.json"
        config.write_text(json.dumps({"schema": 1, "endpoint": "192.168.1.42:5555", "deviceSerial": "saved-headset"}))
        before = self.state_snapshot()
        row = self.invoke("manual-dry-run")
        self.assertEqual(row["returncode"], 0, row["stderr"])
        self.assertEqual(row["adbCalls"], [])
        self.assertEqual(self.state_snapshot(), before)

    def test_failed_source_dry_run_does_not_write_digest_cache(self):
        self.corrupt_payload()
        row = self.invoke("manual-dry-run")
        self.assertEqual(row["returncode"], 1)
        self.assertIn("incomplete or corrupt", row["stderr"])
        self.assertEqual(row["crcCalls"], 1)
        self.assertFalse(self.cache.parent.exists(), "Failed dry run created persistent state")

    def test_retained_handoff_still_requires_expected_digest_on_cached_retry(self):
        handoff = self.apk.with_suffix(".handoff.json")
        manifest = {"schema": 1, "apk": self.apk.name, "package": "dev.gloomhavenvr.quest", "apkSha256": self.sha(), "buildReport": {"buildResult": "Succeeded"}}
        handoff.write_text(json.dumps(manifest))
        first = self.invoke("handoff-cli"); self.assert_waiting_for_quest(first)
        second = self.invoke("handoff-cli"); self.assert_waiting_for_quest(second)
        self.assertEqual(second["payloadReadBytes"], 0)
        manifest["apkSha256"] = "0" * 64; handoff.write_text(json.dumps(manifest))
        rejected = self.invoke("handoff-cli")
        self.assertEqual(rejected["returncode"], 1)
        self.assertIn("APK SHA-256 differs", rejected["stderr"])
        self.assertEqual(rejected["payloadReadBytes"], 0)
        self.assertEqual(rejected["adbCalls"], [])


if __name__ == "__main__":
    if len(sys.argv) == 6 and sys.argv[1] == "--probe":
        probe(*sys.argv[2:])
    else:
        unittest.main()
