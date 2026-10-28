"""Run the literal Unity helper in isolated Python, without a Builder import path."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import native_content_pack as packer


def fixture(root):
    native = root / "Library/com.unity.addressables/aa/Android"
    native.mkdir(parents=True)
    (native / "original.bundle").write_bytes(b"UnityFS\0controlled cooked Android bundle" * 50)
    (native / "settings.json").write_text(json.dumps({"m_CatalogLocations": [
        {"m_InternalId": "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/catalog.json"}]}))
    (native / "catalog.json").write_text(json.dumps({"m_InternalIds": [
        "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/original.bundle", "original-guid"]}))
    archive = root / "Assets/StreamingAssets/quest-startup-content.zip"
    archive.parent.mkdir(parents=True)
    original = b"retained original movie fixture" * 100
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as stream:
        stream.writestr("StreamingAssets/Movies/original.mp4", original)
    manifest = root / packer.MANIFEST
    manifest.parent.mkdir(parents=True)
    manifest.write_text(json.dumps({"schema": 1, "inputKey": "a" * 64,
        "archive": archive.name, "archiveSha256": packer.digest(archive),
        "files": [{"path": "StreamingAssets/Movies/original.mp4", "size": len(original),
                   "sha256": hashlib.sha256(original).hexdigest()}]}))
    return native, archive, manifest, {"schema": 1, "projectRoot": str(root), "nativeRoot": str(native)}


class Kernel:
    """Documented Win32 structures; creation/write clocks deliberately constant."""
    def __init__(self):
        self.change, self.links, self.attributes = 101, 1, 0
        self.filesystem, self.fail, self.closed = "NTFS", None, 0

    def CreateFileW(self, name, access, sharing, security, disposition, flags, template):
        self.opened = (name, access, sharing, disposition, flags)
        return 123

    def GetFileInformationByHandleEx(self, handle, kind, pointer, size):
        if kind == self.fail: return 0
        value = pointer._obj
        if kind == 0:
            value.CreationTime, value.LastWriteTime, value.ChangeTime = 77, 88, self.change
            value.FileAttributes = self.attributes
        elif kind == 1:
            value.EndOfFile, value.NumberOfLinks = 999, self.links
        elif kind == 18:
            value.VolumeSerialNumber, value.FileId[0] = 42, 11
        return 1

    def GetVolumeInformationByHandleW(self, handle, label, label_size, serial, maximum, flags, filesystem, bound):
        filesystem.value = self.filesystem
        return 1

    def CloseHandle(self, handle):
        self.closed += 1
        return 1


class StandalonePackerRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-isolated-content-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.native, self.archive, self.manifest, self.request = fixture(self.root)
        self.helper = self.root / "QuestStartupEvidence/native_content_pack.py"
        self.helper.parent.mkdir()
        shutil.copyfile(packer.__file__, self.helper)
        self.request_path = self.helper.with_name("request.json")
        self.request_path.write_text(json.dumps(self.request))
        self.env = {**os.environ, "PYTHONPATH": "invalid-sibling-path", "GHVR_QUEST_VERIFICATION": "normal",
                    "GHVR_QUEST_VALIDATE_IMPORTED_ASSETS": "0", "GHVR_QUEST_NATIVE_BANK_KEY": "b" * 64,
                    "GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT": "0"}

    def isolated(self, mode=None):
        command = [sys.executable, "-I", "-B", "-X", "utf8", str(self.helper), "--request", str(self.request_path)]
        if mode: command += ["--native-checkpoint", mode]
        return subprocess.run(command, cwd=self.root, env=self.env, capture_output=True, text=True, timeout=30)

    def test_literal_copied_helper_packs_and_reuses_with_no_storage_module(self):
        self.assertFalse(self.helper.with_name("storage.py").exists())
        first = self.isolated()
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        self.assertFalse(json.loads(first.stdout)["reused"])
        with zipfile.ZipFile(self.archive) as stream:
            self.assertIsNone(stream.testzip())
            self.assertEqual(stream.read("StreamingAssets/aa/original.bundle"), (self.native / "original.bundle").read_bytes())
        second = self.isolated()
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        self.assertTrue(json.loads(second.stdout)["reused"])

    def test_completed_bank_is_committed_before_packing_and_reused_by_isolated_child(self):
        saved = self.isolated("record")
        self.assertEqual(saved.returncode, 0, saved.stdout + saved.stderr)
        checkpoint = json.loads((self.root / packer.NATIVE_CHECKPOINT).read_text())
        self.assertTrue(checkpoint["compilerCompleted"])
        self.assertEqual(checkpoint["owner"]["compilerKey"], "b" * 64)
        checked = self.isolated("check")
        self.assertEqual(checked.returncode, 0, checked.stdout + checked.stderr)
        self.assertTrue(json.loads(checked.stdout)["reused"])

    def test_windows_observation_uses_true_change_time_for_single_and_hardlinked_sources(self):
        kernel = Kernel(); observer = packer.WindowsFileMetadata(kernel)
        self.assertEqual(ctypes.sizeof(observer.Basic), 40)
        self.assertEqual(ctypes.sizeof(observer.Standard), 24)
        self.assertEqual(ctypes.sizeof(observer.Identity), 24)
        for links in (1, 2):
            kernel.links = links
            current_change = kernel.change
            stamp = observer.stamp(self.archive)
            self.assertEqual(stamp, ("win32-v1", 42, "0b" + "00" * 15, 999, 88, current_change))
            kernel.change += 1
            self.assertNotEqual(observer.stamp(self.archive), stamp)
        self.assertEqual(kernel.closed, 4)
        self.assertEqual(kernel.opened[1:], (0x80, 7, 3, 0x00200000))

    def test_unsupported_windows_metadata_never_claims_a_reliable_stamp(self):
        for change, filesystem, fail in ((0, "NTFS", None), (101, "FAT32", None), (101, "NTFS", 18)):
            kernel = Kernel(); kernel.change, kernel.filesystem, kernel.fail = change, filesystem, fail
            with self.subTest(change=change, filesystem=filesystem, fail=fail):
                self.assertIsNone(packer.WindowsFileMetadata(kernel).stamp(self.archive))
                self.assertEqual(kernel.closed, 1)
        kernel = Kernel(); kernel.attributes = 0x400
        with self.assertRaisesRegex(ValueError, "linked"):
            packer.WindowsFileMetadata(kernel).stamp(self.archive)
        self.assertEqual(kernel.closed, 1)

    def test_unknown_windows_stamps_fall_back_to_source_bytes_and_never_abort_valid_pack(self):
        observer = type("UnknownMetadata", (), {"stamp": lambda self, path: None})()
        with patch.object(packer.sys, "platform", "win32"), patch.object(packer, "_windows_metadata", observer), \
                patch.object(packer, "digest", wraps=packer.digest) as reads, patch.dict(os.environ, self.env):
            result = packer.pack(self.request)
        self.assertFalse(result["reused"])
        self.assertGreaterEqual(sum(call.args[0] == self.archive for call in reads.call_args_list), 3)

    def test_source_change_time_race_prevents_publishing_the_new_archive(self):
        class Observer:
            revision = 101
            def stamp(self, path):
                value = path.stat()
                return ("win32-v1", value.st_dev, str(value.st_ino), value.st_size, value.st_mtime_ns, self.revision)
        observer = Observer(); digest = packer.digest
        original = self.archive.read_bytes(), self.manifest.read_bytes()
        def mutate(path, progress=None):
            result = digest(path, progress)
            if path == self.native / "original.bundle": observer.revision += 1
            return result
        with patch.object(packer.sys, "platform", "win32"), patch.object(packer, "_windows_metadata", observer), \
                patch.object(packer, "digest", side_effect=mutate), patch.dict(os.environ, self.env):
            with self.assertRaisesRegex(ValueError, "changed during"):
                packer.pack(self.request)
        self.assertEqual(original, (self.archive.read_bytes(), self.manifest.read_bytes()))
        self.assertFalse(list(self.archive.parent.glob("*.repack-*")))


if __name__ == "__main__":
    unittest.main()
