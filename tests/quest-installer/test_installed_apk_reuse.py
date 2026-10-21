"""Installed APK reuse keeps actual byte qualification and physical witnesses."""
import contextlib
import hashlib
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest

spec = importlib.util.spec_from_file_location("apk_reuse_fixture", Path(__file__).with_name("test_file_content.py"))
fixture = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixture)
installer = fixture.installer


class Device:
    remote = "/data/app/owned-package/base.apk"
    def __init__(self, path): self.path, self.calls = path, []
    def identity(self, address): return "fixture-Quest"
    def run(self, *args, **options):
        assert args[:2] == ("-s", "usb")
        call = args[2:]; self.calls.append(call)
        if call == ("shell", "pm", "path", "--user", "0", installer.PACKAGE): return "package:" + self.remote
        if call == ("shell", "stat", "-c", "%d:%i:%s:%Y:%Z", self.remote):
            return subprocess.check_output(["stat", "-c", "%d:%i:%s:%Y:%Z", self.path], text=True).strip()
        if call == ("shell", "sha256sum", self.remote):
            return hashlib.sha256(self.path.read_bytes()).hexdigest() + "  " + self.remote
        raise AssertionError(call)


class InstalledReuseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name); self.apk = root / "owned.apk"; self.apk.write_bytes(b"owned APK fixture")
        self.remote = root / "installed.apk"; self.remote.write_bytes(self.apk.read_bytes())
        self.device = Device(self.remote)
        self.source = SimpleNamespace(apk=self.apk, sha256=hashlib.sha256(self.apk.read_bytes()).hexdigest(), input_key="a" * 64)
        self.previous = {"deviceSerial": "fixture-Quest", "package": installer.PACKAGE,
                         "apkSha256": self.source.sha256, "inputKey": self.source.input_key}

    def retain(self):
        with contextlib.redirect_stdout(io.StringIO()):
            return installer.retain_installed_apk(self.device, "usb", "fixture-Quest", self.source, self.previous)

    def test_old_receipt_qualifies_once_then_retains_without_rehash_or_install(self):
        proof = self.retain(); self.assertIsNotNone(proof)
        self.assertEqual(sum("sha256sum" in call for call in self.device.calls), 1)
        self.previous["installedApkWitness"] = proof; self.device.calls.clear()
        self.assertEqual(self.retain(), proof)
        self.assertFalse(any("sha256sum" in call or "install" in call or "push" in call for call in self.device.calls))

    def test_wrong_installed_bytes_are_never_retained(self):
        self.remote.write_bytes(b"X" * self.apk.stat().st_size)
        self.assertIsNone(self.retain())

    def test_different_headset_or_source_receipt_has_no_device_work(self):
        self.previous["deviceSerial"] = "another-Quest"
        self.assertIsNone(self.retain()); self.assertEqual(self.device.calls, [])
        self.previous["deviceSerial"] = "fixture-Quest"; self.previous["apkSha256"] = "b" * 64
        self.assertIsNone(self.retain()); self.assertEqual(self.device.calls, [])

    def test_changed_physical_witness_requires_fresh_byte_qualification(self):
        self.previous["installedApkWitness"] = self.retain(); self.device.calls.clear()
        stat = self.remote.stat(); self.remote.write_bytes(b"X" * self.apk.stat().st_size)
        os.utime(self.remote, (stat.st_atime, stat.st_mtime + 10))
        self.assertIsNone(self.retain())
        self.assertTrue(any("sha256sum" in call for call in self.device.calls))


if __name__ == "__main__": unittest.main()
