"""SYNC mkdir metadata denial is distinct from denied shell/file writes.

The actual Adb command boundary runs mapped POSIX shells, extraction, checksums
and receipts. Its SYNC stand-in rejects missing parents instead of creating
them. Tiny payloads deliberately replace the hardware's reported 11-GB bank;
these controls establish recovery/ownership, not Android FUSE or throughput.
"""
import contextlib
import io
from pathlib import Path
import shutil
import subprocess
import unittest

import test_file_content as fixtures
from test_raw_content_upload import RawDevice

installer = fixtures.installer


class DirectoryMetadataDevice(fixtures.LocalDevice):
    binary_stream = RawDevice.binary_stream

    def __init__(self, root, log):
        super().__init__(root)
        self.log, self.executable = log, "fixture-adb.exe"
        self.raw_calls, self.raw_fault, self.streamed = [], None, []
        self.forced_metadata_failure = False
        self.corrupt_partial = False
        self.interrupt_stream = None
        self.sync_failures = 0
        self.failure_text = "remote secure_mkdirs failed: Operation not permitted"

    def run(self, *args, **options):
        action = args[2:]
        if action[:2] == ("shell", "getprop"):
            return {"ro.product.model": "Quest 3", "ro.product.name": "eureka",
                    "ro.serialno": "fixture-headset"}[action[2]]
        if action[0] == "push" and Path(action[1]).is_dir():
            origin, root = Path(action[1]), self.local(action[2])
            self.calls.append(action)
            files = sorted(path for path in origin.rglob("*") if path.is_file())
            for index, source in enumerate(files):
                destination = root / origin.name / source.relative_to(origin)
                if not destination.parent.is_dir():
                    self.sync_failures += 1
                    raise installer.InstallError(self.failure_text)
                shutil.copyfile(source, destination)
                self.pushed_files.append(str(destination.relative_to(self.root)))
                if self.forced_metadata_failure and index == 0:
                    if self.corrupt_partial:
                        destination.write_bytes(b"X" * destination.stat().st_size)
                    self.forced_metadata_failure = False
                    self.sync_failures += 1
                    raise installer.InstallError(self.failure_text)
            return "files pushed"
        return super().run(*args, **options)

    def stream(self, address, local, script, timeout, report=None):
        if self.interrupt_stream == len(self.streamed):
            self.interrupt_stream = None
            raise installer.ConnectionRequired("device offline", "offline-or-unauthorized")
        self.streamed.append(Path(local).name)
        return self.binary_stream(address, local, script, timeout, report)

    def runner(self, command, **options):
        self.assert_short_command(command)
        try:
            output = self.run(*command[1:], input_text=options.get("input", b"").decode())
            return subprocess.CompletedProcess(command, 0, stdout=output, stderr="")
        except installer.InstallError as error:
            return subprocess.CompletedProcess(command, 1, stdout="", stderr=str(error))

    @staticmethod
    def assert_short_command(command):
        assert len(subprocess.list2cmdline(command).encode("utf-16-le")) // 2 < 4096


class SyncDirectoryMetadataTests(unittest.TestCase):
    make_source = fixtures.FileContentTests.make_source
    receipt = fixtures.FileContentTests.receipt

    def setUp(self):
        fixtures.FileContentTests.setUp(self)
        self.device = DirectoryMetadataDevice(self.root / "device", self.root / "adb.log")
        self.adb = installer.Adb("fixture-adb.exe", self.device.log, runner=self.device.runner)
        self.adb.binary_stream = self.device.stream

    def install(self, repair=False):
        with contextlib.redirect_stdout(io.StringIO()):
            return installer.install_file_content(self.adb, "quest", self.source, repair)

    def test_actual_adb_fresh_bank_prepares_parents_before_sync_metadata(self):
        # This same fresh-tree negative control fails before any bytes can be
        # accepted. The repaired install then succeeds with ordinary fast SYNC.
        probe = self.root / "StreamingAssets"; probe.mkdir()
        (probe / "probe.bundle").write_bytes(b"source")
        remote = self.device.prefix + "quest-mod-resources/"
        with self.assertRaisesRegex(installer.InstallError, "secure_mkdirs"):
            self.adb.run("-s", "quest", "push", probe, remote)
        self.source = installer.Source(**{**self.source.__dict__, "content": (
            installer.ContentFile(self.source.content[0].path, "quest-startup-content.zip",
                                  self.source.content[0].sha256, 11025189977),)})
        records = self.install()
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 7)
        self.assertEqual(records[1]["size"], 11025189977)
        self.assertEqual(self.device.sync_failures, 1)  # Negative control only.
        self.assertEqual(self.device.raw_calls, [])
        self.assertEqual(self.receipt()["key"], installer.content_key(self.manifest["game"]))
        self.assertFalse(any(call[0] in ("install", "uninstall") for call in self.device.calls))

    def test_partial_sync_is_qualified_and_not_sent_twice_by_binary_fallback(self):
        self.device.forced_metadata_failure = True
        records = self.install()
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 7)
        self.assertEqual(self.device.sync_failures, 1)
        self.assertNotIn("ghvr-environment.bundle", self.device.streamed)
        self.assertIn("gloomhavenvr.bundle", self.device.streamed)
        self.assertEqual(self.receipt()["files"], {row["path"]: row for row in self.manifest["game"]["files"]})
        self.assertFalse(list(self.device.root.rglob(".quest-install-upload-*")))

    def test_interrupted_fallback_keeps_per_file_claims_for_new_adb_attempt(self):
        self.device.forced_metadata_failure = True
        self.device.interrupt_stream = 1
        with self.assertRaises(installer.ConnectionRequired):
            self.install()
        bank = self.device.root / "quest-mod-resources"
        journal = bank / installer._FILE_JOURNAL
        self.assertTrue(journal.is_file())
        self.assertEqual(self.device.streamed, ["gloomhavenvr.bundle"])
        previous = list(self.device.streamed)
        self.device.calls.clear()
        self.adb = installer.Adb("fixture-adb.exe", self.device.log, runner=self.device.runner)
        self.adb.binary_stream = self.device.stream
        result = self.install()
        self.assertEqual(result[0]["reusedFiles"], 2)
        self.assertEqual(self.device.streamed, previous)
        resent = self.device.pushed_files[-5:]
        self.assertNotIn("quest-mod-resources/StreamingAssets/ghvr-environment.bundle", resent)
        self.assertNotIn("quest-mod-resources/StreamingAssets/gloomhavenvr.bundle", resent)
        # Resuming used compact receipts and metadata for accepted files, never
        # a new full-bank hash, reinstall or recursive device enumeration.
        scripts = "\n".join(str(call) for call in self.device.calls)
        self.assertNotIn("find ", scripts)
        self.assertFalse(any(call[0] in ("install", "uninstall") for call in self.device.calls))
        self.assertNotIn("sha256sum " + self.device.prefix + "quest-mod-resources/StreamingAssets/ghvr-environment.bundle", scripts)

    def test_same_size_partial_sync_corruption_is_replaced_before_receipt(self):
        self.device.forced_metadata_failure = self.device.corrupt_partial = True
        result = self.install()
        self.assertEqual(result[0]["files"], 4)
        self.assertIn("ghvr-environment.bundle", self.device.streamed)
        for relative, data in self.mod.items():
            self.assertEqual((self.device.root / "quest-mod-resources" / relative).read_bytes(), data)
        self.assertEqual(self.receipt("mod")["files"], {row["path"]: row for row in self.manifest["mod"]["files"]})

    def test_manifested_symlink_parent_is_refused_before_sync(self):
        bank = self.device.root / "quest-mod-resources"
        bank.mkdir(parents=True)
        outside = self.root / "outside"; outside.mkdir()
        (bank / "StreamingAssets").symlink_to(outside, target_is_directory=True)
        with self.assertRaises(installer.InstallError): self.install()
        self.assertEqual(list(outside.iterdir()), [])
        self.assertEqual(self.device.raw_calls, [])
        self.assertFalse(any(call[0] == "push" and Path(call[1]).is_dir() for call in self.device.calls))

    def test_unknown_permission_failure_never_reinterpreted_as_sync_metadata(self):
        self.device.forced_metadata_failure = True
        self.device.failure_text = "remote write failed: Permission denied"
        with self.assertRaisesRegex(installer.InstallError, "Permission denied"):
            self.install()
        self.assertEqual(self.device.raw_calls, [])

    def test_preparation_is_manifest_only_and_keeps_windows_argv_short(self):
        rows = [{"path": "StreamingAssets/" + ("x" * 180) + str(index) + "/file.asset"}
                for index in range(256)]
        root = self.device.prefix + "quest-owned-game"
        installer._prepare_content_parents(self.adb, "quest", root, rows)
        self.assertEqual(len(list((self.device.root / "quest-owned-game/StreamingAssets").iterdir())), 256)
        self.assertGreater(self.device.log.stat().st_size, 32767)
        self.assertFalse(any("find " in str(call) for call in self.device.calls))


if __name__ == "__main__": unittest.main()
