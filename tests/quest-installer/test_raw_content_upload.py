"""Binary content repair uses real shell processes/files, not a Quest daemon.

Only the exact observed sync metadata error chooses raw stdin. Remote size/SHA
and an operation-specific EOF marker must qualify bytes before publication.
"""
import contextlib
import hashlib
import io
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest

import test_file_content as fixtures

LocalDevice, installer = fixtures.LocalDevice, fixtures.installer


class RawDevice(LocalDevice):
    def __init__(self, root, log):
        super().__init__(root)
        self.log, self.executable = log, "fixture-adb.exe"
        self.sync_error = "remote fchown failed: Operation not permitted"
        self.raw_calls = []
        self.raw_fault = None

    def run(self, *args, **options):
        if args[2] == "push":
            self.calls.append(args[2:])
            raise installer.InstallError(self.sync_error)
        return super().run(*args, **options)

    def binary_stream(self, serial, local, script, timeout, report=None):
        assert serial == "quest"
        command = [self.executable, "-s", serial, "binary-shell-v2", script]
        self.raw_calls.append(command)
        if self.raw_fault == "timeout":
            raise subprocess.TimeoutExpired(command, timeout)
        if self.raw_fault == "offline":
            return subprocess.CompletedProcess(command, 1, stderr="device offline")
        # Execute the actual generated publication script through a binary
        # file/pipe, retaining its real exit/output. Faults alter input or the
        # EOF marker before qualification; they cannot invent success.
        if self.raw_fault == "silent":
            return subprocess.CompletedProcess(command, 0, stdout="", stderr="")
        remote = script.replace(self.prefix, str(self.root) + "/")
        if self.raw_fault == "marker":
            remote = remote.replace("printf %s ", "printf %s another-operation-")
        with Path(local).open("rb") as source:
            if self.raw_fault == "corrupt":
                source_data = bytearray(source.read()); source_data[0] ^= 1
                result = subprocess.run(["sh", "-c", remote], input=bytes(source_data),
                                        capture_output=True, timeout=10)
            else:
                result = subprocess.run(["sh", "-c", remote], stdin=source,
                                        capture_output=True, timeout=10)
        if self.raw_fault == "lost-ack":
            self.raw_fault = None
            raise RuntimeError("The ADB server closed without a complete shell-v2 response.")
        if self.raw_fault == "partial":
            raise AssertionError("Use the interrupted-input fixture")
        return subprocess.CompletedProcess(command, result.returncode,
                                           stdout=result.stdout.decode(), stderr=result.stderr.decode())



class RawContentUploadTests(unittest.TestCase):
    make_source = fixtures.FileContentTests.make_source

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.device = RawDevice(self.root / "device", self.root / "adb.log")
        self.game = {"StreamingAssets/Campaign's rules.bin": b"a\x00\r\n\x1a\xff\n" * 4096}
        self.mod = {"StreamingAssets/" + name: name.encode() for name in
                    ("gloomhavenvr.bundle", "ghvr-town.bundle", "ghvr-town-voices.bundle", "ghvr-environment.bundle")}
        self.make_source()

    def upload(self, fault=None, remote=None):
        payload = b"binary\x00\n\x1a\xff\r\n" * 4096
        path = self.root / "payload.bin"; path.write_bytes(payload)
        row = {"size": len(payload), "sha256": hashlib.sha256(payload).hexdigest()}
        self.device.raw_fault = fault
        target = remote or self.device.prefix + "quest-owned-game/StreamingAssets/a ' file.bin"
        with contextlib.redirect_stdout(io.StringIO()):
            result = installer._push_content(self.device, "quest", path, target, timeout=30, rows=[row])
        return result, target, payload

    def test_binary_file_is_verified_and_published_with_short_argv(self):
        verified, target, data = self.upload()
        self.assertTrue(verified)
        self.assertEqual(self.device.local(target).read_bytes(), data)
        # This script travels over a socket service, not CreateProcess argv.
        self.assertLess(len(self.device.raw_calls[0][-1].encode()), 65535)
        self.assertFalse(any(call[0] == "shell" for call in self.device.calls))
        self.assertFalse(list(self.device.root.rglob(".quest-install-upload-*")))

    def test_both_banks_and_receipts_repair_then_warm_reuse_without_tree_scan(self):
        with contextlib.redirect_stdout(io.StringIO()):
            records = installer.install_file_content(self.device, "quest", self.source)
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 5)
        for name, data in self.game.items():
            self.assertEqual((self.device.root / "quest-owned-game" / name).read_bytes(), data)
        self.assertEqual(sum(call[0] == "push" for call in self.device.calls), 1)
        save = self.device.root / "GloomSave/campaign.save"
        save.parent.mkdir(); save.write_bytes(b"retained campaign")
        self.device.calls.clear(); before = len(self.device.raw_calls)
        with contextlib.redirect_stdout(io.StringIO()):
            records = installer.install_file_content(self.device, "quest", self.source)
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 0)
        self.assertEqual(len(self.device.raw_calls), before)
        self.assertFalse(any("sha256sum" in str(call) or "stat -c" in str(call) for call in self.device.calls))
        self.assertEqual(save.read_bytes(), b"retained campaign")

    def test_other_sync_errors_never_choose_fallback(self):
        self.device.sync_error = "device unauthorized"
        with self.assertRaisesRegex(installer.InstallError, "unauthorized"): self.upload()
        self.assertFalse(self.device.raw_calls)

    def test_foreign_destination_is_rejected_before_any_transport(self):
        with self.assertRaisesRegex(installer.InstallError, "outside"):
            self.upload(remote="/sdcard/Android/data/another.app/files/quest-owned-game/a")
        self.assertFalse(self.device.calls)

    def test_same_size_wrong_bytes_are_not_published(self):
        with self.assertRaisesRegex(installer.InstallError, "checksum"): self.upload("corrupt")
        self.assertFalse(list(self.device.root.rglob("*.bin")))

    def test_zero_client_exit_without_remote_bytes_does_not_publish(self):
        with self.assertRaises(installer.InstallError): self.upload("silent")
        self.assertFalse(list(self.device.root.rglob("*.bin")))

    def test_exact_operation_marker_is_required(self):
        with self.assertRaises(installer.InstallError): self.upload("marker")
        self.assertFalse(list(self.device.root.rglob("*.bin")))

    def test_timeout_is_recoverable_error_and_preserves_existing_file(self):
        remote = self.device.prefix + "quest-owned-game/StreamingAssets/a ' file.bin"
        previous = self.device.local(remote); previous.parent.mkdir(parents=True)
        previous.write_bytes(b"previous committed content")
        with self.assertRaisesRegex(installer.ConnectionRequired, "timed out.*USB"):
            self.upload("timeout")
        self.assertEqual(previous.read_bytes(), b"previous committed content")
        self.assertIn("binaryUploadTimeout", self.device.log.read_text())

    def test_offline_keeps_error_detail_and_no_publication(self):
        with self.assertRaisesRegex(installer.InstallError, "device offline"): self.upload("offline")
        self.assertFalse(list(self.device.root.rglob("*.bin")))

    def test_bank_symlink_is_rejected_without_writing_outside_bank(self):
        outside = self.root / "outside"; outside.mkdir()
        self.device.root.mkdir()
        (self.device.root / "quest-owned-game").symlink_to(outside, target_is_directory=True)
        with self.assertRaises(installer.InstallError): self.upload()
        self.assertEqual(len(self.device.raw_calls), 1)
        self.assertEqual(list(outside.iterdir()), [])
    def test_lost_exit_resumes_same_publication_without_resending_payload(self):
        self.device.wait_enabled = True
        waits = []
        self.device.wait_for_connection = lambda serial: waits.append(serial)
        self.upload("lost-ack")
        self.assertEqual(waits, ["quest"])
        self.assertEqual(len(self.device.raw_calls), 1)
        self.assertFalse(list(self.device.root.rglob(".quest-install-upload-*")))

    def test_one_file_journal_survives_an_unclosed_batch_and_torn_append(self):
        row = self.manifest["game"]["files"][0]
        payload = self.root / "single.bin"; payload.write_bytes(self.game[row["path"]])
        remote_root = self.device.prefix + "quest-owned-game"
        journal = self.device.local(remote_root + "/" + installer._FILE_JOURNAL)
        journal.parent.mkdir(parents=True); journal.write_bytes(b'{"torn":')
        with contextlib.redirect_stdout(io.StringIO()):
            installer._push_content(self.device, "quest", payload, remote_root + "/" + row["path"],
                                    timeout=30, rows=[row], checkpoint=installer.content_key(self.manifest["game"]))
        accepted = installer._pull_file_journal(self.device, "quest", remote_root, self.manifest["game"], self.root)
        self.assertEqual(accepted, {row["path"]: row})



if __name__ == "__main__": unittest.main()
