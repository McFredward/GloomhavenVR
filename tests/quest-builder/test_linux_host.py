"""Bounded Linux host contracts: graphics selection and owned ADB provisioning."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
from storage import BuildError


def load(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


adb = load("linux_host_adb", "tools/quest-installer/adb_bootstrap.py")
installer = load("linux_host_installer", "tools/quest-installer/installer.py")


class LinuxGraphicsTests(unittest.TestCase):
    def test_desktop_does_not_require_or_launch_xvfb(self):
        with patch.dict(os.environ, {"DISPLAY": ":1"}, clear=True), patch.object(builder.sys, "platform", "linux"), \
             patch.object(builder.shutil, "which", side_effect=AssertionError("Desktop needs no Xvfb lookup")):
            self.assertEqual(builder.unity_launcher("/a path/Unity", graphics=True),
                             ["/a path/Unity", "-batchmode", "-force-glcore"])

    def test_missing_xauth_reports_actionable_headless_dependency(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(builder.sys, "platform", "linux"), \
             patch.object(builder.shutil, "which", side_effect=lambda name: "/xvfb-run" if name == "xvfb-run" else None):
            with self.assertRaisesRegex(BuildError, "xauth"):
                builder.unity_launcher("/Unity", graphics=True)


@unittest.skipUnless(os.name == "posix", "Unix permission and executable controls")
class LinuxAdbTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="quest Linux host's ")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.scripts = self.root / "scripts"
        self.scripts.mkdir()
        self.archive = self.root / "platform-tools.zip"
        with zipfile.ZipFile(self.archive, "w") as package:
            for name in adb.LINUX["required"]:
                entry = zipfile.ZipInfo("platform-tools/" + name)
                entry.create_system = 3
                entry.external_attr = (stat.S_IFREG | (0o755 if name == "adb" else 0o644)) << 16
                if name == "source.properties": payload = b"Pkg.Revision=37.0.1\n"
                elif name == "adb": payload = b"#!/bin/sh\nprintf 'Android Debug Bridge version 1.0.41\\nVersion 37.0.1-fixture\\n'\n"
                else: payload = b"owned fixture " + name.encode()
                package.writestr(entry, payload)
        self.pin = {**adb.LINUX, "bytes": self.archive.stat().st_size,
                    "sha256": hashlib.sha256(self.archive.read_bytes()).hexdigest()}
        self.downloads = []
        def download(destination, selected=None):
            self.assertEqual(selected["host"], "linux-x64")
            self.downloads.append(destination)
            shutil.copyfile(self.archive, destination)
        self.patches = [patch.object(adb, "LINUX", self.pin),
                        patch.object(adb.platform, "machine", return_value="x86_64"),
                        patch.object(adb, "_download", side_effect=download)]
        for control in self.patches:
            control.start()
            self.addCleanup(control.stop)

    def provision(self):
        with contextlib.redirect_stdout(io.StringIO()): return adb.ensure_linux_adb(self.scripts)

    def test_private_verified_executable_runs_and_reuses_without_network(self):
        executable = self.provision()
        self.assertEqual(executable.name, "adb")
        self.assertTrue(os.access(executable, os.X_OK))
        self.assertIn("37.0.1-fixture", subprocess.check_output([str(executable), "version"], text=True))
        state = json.loads((executable.parent / adb.MARKER).read_text())
        self.assertEqual(state["host"], "linux-x64")
        self.assertEqual(state["archiveSha256"], self.pin["sha256"])
        with patch.object(adb, "_download", side_effect=AssertionError("No repeated download")):
            self.assertEqual(self.provision(), executable)
        self.assertEqual(len(self.downloads), 1)

    def test_lost_execute_permission_is_repaired_from_retained_archive(self):
        executable = self.provision()
        executable.chmod(0o644)
        with patch.object(adb, "_download", side_effect=AssertionError("Repair uses verified local archive")):
            self.assertEqual(self.provision(), executable)
        self.assertTrue(os.access(executable, os.X_OK))
        self.assertEqual(len(self.downloads), 1)

    def test_foreign_cpu_refuses_without_download(self):
        with patch.object(adb.platform, "machine", return_value="aarch64"):
            with self.assertRaisesRegex(adb.AdbBootstrapError, "x86-64"):
                self.provision()
        self.assertEqual(self.downloads, [])

    def test_tampered_archive_never_publishes_executable(self):
        with patch.object(adb, "_download", side_effect=lambda target, selected: target.write_bytes(b"tampered")):
            with self.assertRaisesRegex(adb.AdbBootstrapError, "SHA-256/size mismatch"):
                self.provision()
        self.assertFalse((self.scripts / ".quest-adb/platform-tools").exists())

    def test_dispatch_uses_linux_entry_point(self):
        with patch.object(adb.sys, "platform", "linux"), patch.object(adb, "ensure_linux_adb", return_value="result") as selected:
            self.assertEqual(adb.ensure_adb(self.scripts), "result")
        selected.assert_called_once_with(self.scripts)

    def test_installer_fallback_and_managed_paths_use_verified_linux_cache(self):
        executable = self.provision()
        with patch.object(installer, "REPO", self.root), patch.object(installer.sys, "platform", "linux"), \
             patch.object(installer.shutil, "which", return_value=None), \
             patch.object(installer, "managed_linux_adb", return_value=executable) as managed:
            self.assertEqual(installer.adb_path(None, executable), executable)
            managed.assert_called_once_with()
            managed.reset_mock()
            with patch.dict(os.environ, {}, clear=True), patch.object(Path, "home", return_value=self.root):
                self.assertEqual(installer.adb_path(None), executable)
            managed.assert_called_once_with()
            managed.reset_mock()
            with self.assertRaises(installer.InstallError): installer.adb_path(self.root / "missing-adb")
            managed.assert_not_called()

    def test_bootstrap_rejects_linked_cache_and_preserves_original(self):
        other = self.root / "outside"; other.mkdir(); original = other / "keep"; original.write_bytes(b"original")
        (self.scripts / ".quest-adb").symlink_to(other, target_is_directory=True)
        with self.assertRaisesRegex(adb.AdbBootstrapError, "linked"):
            self.provision()
        self.assertEqual(original.read_bytes(), b"original")
        self.assertEqual(self.downloads, [])


if __name__ == "__main__": unittest.main()
