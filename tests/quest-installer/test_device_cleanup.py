"""Real-shell controls for bounded Quest installer scratch cleanup."""
import importlib.util
import json
import os
import re
from pathlib import Path
import shlex
import subprocess
import tempfile
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_device_cleanup_tests", ROOT / "tools/quest-installer/device_cleanup.py")
cleanup = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cleanup)

HW = "QUEST-TEST"
ADDRESS = "quest-usb"
TXN = "a" * 32
SHA = "b" * 64
KEY = "c" * 64
TOKEN = "d" * 32
BASE = cleanup.EXTERNAL + "/files/quest-owned-game/StreamingAssets"
STAGE = BASE + "/.quest-install-upload-" + TOKEN
PARK = "/sdcard/Download/.ghvr-quest-recovery/" + HW + "/" + TXN


class ShellDevice:
    def __init__(self, root):
        self.root = root
        self.hardware = HW
        self.running = ""
        self.scripts = []
        self.calls = []
        self.denied = None

    def path(self, remote):
        return self.root / remote.lstrip("/")

    def identity(self, address):
        return self.hardware

    def run(self, *args, **keywords):
        self.calls.append((args, keywords))
        if args[-3:] == ("shell", "pidof", cleanup.PACKAGE):
            return self.running
        script = keywords.get("input_text")
        if script is None:
            raise AssertionError("Unexpected command: " + repr(args))
        self.scripts.append(script)
        if self.denied and self.denied in script:
            raise RuntimeError("Android storage view is unavailable")
        translated = re.sub(r"(?<![A-Za-z0-9_/])/(sdcard|data)/", lambda m: str(self.root / m.group(1)) + "/", script)
        result = subprocess.run(["/bin/sh", "-s"], input=translated, text=True, capture_output=True, timeout=10)
        if result.returncode:
            raise RuntimeError("shell status " + str(result.returncode) + ": " + result.stderr)
        return result.stdout.replace(str(self.root) + "/", "/").strip()


class CleanupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.directory = self.root / "host/device"
        self.directory.mkdir(parents=True)
        self.adb = ShellDevice(self.root / "android")
        self.value = cleanup.DeviceCleanup(self.adb, ADDRESS, HW, self.directory)
        self.progress = []

    def put(self, remote, value=b"temporary data"):
        path = self.adb.path(remote)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)
        return path

    def journal(self, state="complete", **fields):
        path = self.directory / "signing-recovery" / HW / "active.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        value = {"schema": 1, "package": cleanup.PACKAGE, "deviceSerial": HW,
                 "transaction": TXN, "apkSha256": SHA, "inputKey": KEY, "state": state}
        value.update(fields)
        path.write_text(json.dumps(value))
        return path

    def upload_log(self, hardware=HW, address=ADDRESS, source=SHA):
        script = "test ! -e " + shlex.quote(STAGE) + "\ncat > " + shlex.quote(STAGE)
        script += "\nprintf %s " + TOKEN + " > " + shlex.quote(STAGE + ".ready")
        rows = [{"apkSha256": source}, ["adb", "-s", address, "shell", "getprop", "ro.serialno"],
                {"exitCode": 0}, hardware, ["adb", "-s", address, "shell", "-T", "sh", "-s"],
                {"stdinScript": script}]
        path = self.directory / "wireless-install-20261014T200000Z-12345678.log"
        path.write_text("\n".join(row if isinstance(row, str) else json.dumps(row) for row in rows))
        return path

    def preview(self):
        return self.value.preview(progress=self.progress.append)

    def test_inactive_exact_upload_and_marker_removed_without_touching_game(self):
        stage = self.put(STAGE, b"broken transfer")
        marker = self.put(STAGE + ".ready", TOKEN.encode())
        current = self.put(BASE + "/current.asset", b"current game data")
        pending = self.put(str(Path(BASE).parent) + "/.quest-installation.accepted.jsonl", b"useful resume row")
        log = self.upload_log()
        before_log = log.read_bytes()
        plan = self.preview()
        self.assertEqual({STAGE, STAGE + ".ready"}, {row["path"] for row in plan["candidates"]})
        self.assertEqual(15 + 32, plan["bytes"])
        result = self.value.clean(plan, progress=self.progress.append)
        self.assertEqual(47, result["freedBytes"])
        self.assertEqual(2, result["deletedFiles"])
        self.assertFalse(stage.exists())
        self.assertFalse(marker.exists())
        self.assertEqual(b"current game data", current.read_bytes())
        self.assertEqual(b"useful resume row", pending.read_bytes())
        self.assertEqual(before_log, log.read_bytes())
        self.assertEqual(self.progress[-1]["done"], self.progress[-1]["total"])
        self.assertFalse(any("sha256sum" in s or "rm -rf" in s for s in self.adb.scripts))

    def test_orphan_reserved_upload_can_be_cleaned_after_old_host_logs_deleted(self):
        stage = self.put(STAGE)
        plan = self.preview()
        self.assertEqual([STAGE], [row["path"] for row in plan["candidates"]])
        result = self.value.clean(plan)
        self.assertEqual(1, result["deletedFiles"])
        self.assertFalse(stage.exists())

    def test_unrelated_upload_and_unknown_reserved_name_are_retained(self):
        data = self.put(BASE + "/important.asset.upload")
        bad = self.put(BASE + "/.quest-install-upload-other")
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertEqual("unknown-members", plan["unknown"][0]["reason"])
        self.value.clean(plan)
        self.assertTrue(data.exists())
        self.assertTrue(bad.exists())

    def test_canonical_legacy_archive_and_receipts_are_never_cleanup_targets(self):
        archive = self.put(cleanup.EXTERNAL + "/files/quest-install-input/" + KEY + "/quest-startup-content.zip.upload")
        pending = self.put(str(Path(BASE).parent) + "/.quest-installation.receipt.pending")
        final = self.put(str(Path(BASE).parent) + "/.quest-installation.receipt")
        plan = self.preview()
        self.value.clean(plan)
        self.assertTrue(archive.exists())
        self.assertTrue(pending.exists())
        self.assertTrue(final.exists())

    def test_legacy_parked_bank_is_protected_even_without_host_journal(self):
        bank = self.put(PARK + "/quest-owned-game/unique.asset")
        plan = self.preview()
        self.assertIn(PARK, [row["path"] for row in plan["protected"]])
        self.assertIsNone(next(row["bytes"] for row in plan["protected"] if row["path"] == PARK))
        self.value.clean(plan)
        self.assertTrue(bank.exists())
        self.assertFalse(any("find " + shlex.quote(PARK + "/") in script for script in self.adb.scripts))

    def test_terminal_transaction_helper_is_removed_host_backups_are_retained(self):
        journal = self.journal()
        helper = self.put("/data/local/tmp/.ghvr-rename-" + TXN)
        backup = journal.parent / TXN / "external.tar"
        backup.parent.mkdir()
        backup.write_bytes(b"original player saves")
        plan = self.preview()
        self.assertIn("rename-helper", [row["kind"] for row in plan["candidates"]])
        self.value.clean(plan)
        self.assertFalse(helper.exists())
        self.assertEqual(b"original player saves", backup.read_bytes())

    def test_pending_recovery_helper_and_private_stage_remain_protected(self):
        private = "/data/user/0/" + cleanup.PACKAGE + "/cache/.quest-restore-stage-" + TOKEN
        self.journal("restoring", appContextStage=private)
        helper = self.put("/data/local/tmp/.ghvr-rename-" + TXN)
        stage = self.put(private)
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertEqual(2, sum(row["reason"] == "pending-recovery" for row in plan["protected"]))
        self.value.clean(plan)
        self.assertTrue(helper.exists())
        self.assertTrue(stage.exists())

    def test_terminal_exact_private_restore_stage_is_removed(self):
        remote = "/data/user/0/" + cleanup.PACKAGE + "/cache/.quest-restore-stage-" + TOKEN
        self.journal("reset-complete", appContextStage=remote)
        stage = self.put(remote)
        plan = self.preview()
        self.assertEqual([remote], [r["path"] for r in plan["candidates"]])
        self.value.clean(plan)
        self.assertFalse(stage.exists())
        self.assertTrue(any("run-as" in args for args, kw in self.adb.calls))

    def test_changed_journal_invalidates_preview_before_any_deletion(self):
        journal = self.journal()
        stage = self.put(STAGE)
        plan = self.preview()
        journal.write_text(journal.read_text() + "\n")
        with self.assertRaisesRegex(cleanup.CleanupError, "ownership changed"):
            self.value.clean(plan)
        self.assertTrue(stage.exists())

    def test_source_bound_log_changed_after_preview_cancels_cleanup(self):
        self.put(STAGE)
        log = self.upload_log()
        plan = self.preview()
        log.write_text(log.read_text() + "\nnew operation")
        with self.assertRaisesRegex(cleanup.CleanupError, "ownership changed"):
            self.value.clean(plan)
        self.assertTrue(self.adb.path(STAGE).exists())

    def test_changed_stage_between_preview_and_clean_is_retained(self):
        stage = self.put(STAGE)
        plan = self.preview()
        stage.write_bytes(b"new active transfer owns a different state")
        result = self.value.clean(plan)
        self.assertEqual(0, result["deletedFiles"])
        self.assertEqual("metadata-changed", result["retained"][0]["reason"])
        self.assertTrue(stage.exists())

    def test_replaced_stage_inode_same_size_is_retained(self):
        stage = self.put(STAGE, b"same")
        plan = self.preview()
        replacement = stage.with_name("replacement")
        replacement.write_bytes(b"same")
        replacement.replace(stage)
        result = self.value.clean(plan)
        self.assertEqual("metadata-changed", result["retained"][0]["reason"])
        self.assertTrue(stage.exists())

    def test_changed_headset_cancels_before_deletion(self):
        stage = self.put(STAGE)
        plan = self.preview()
        self.adb.hardware = "ANOTHER-QUEST"
        with self.assertRaisesRegex(cleanup.CleanupError, "headset changed"):
            self.value.clean(plan)
        self.assertTrue(stage.exists())

    def test_app_started_after_preview_cancels_without_force_stop(self):
        stage = self.put(STAGE)
        plan = self.preview()
        self.adb.running = "1234"
        with self.assertRaisesRegex(cleanup.CleanupError, "Close GloomhavenVR"):
            self.value.clean(plan)
        self.assertTrue(stage.exists())
        self.assertFalse(any("force-stop" in args for args, kw in self.adb.calls))

    def test_active_app_makes_all_candidates_protected(self):
        stage = self.put(STAGE)
        self.adb.running = "1234 4321"
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertIn("app-running", [r["reason"] for r in plan["protected"]])
        self.assertTrue(stage.exists())

    def test_linked_upload_does_not_delete_target(self):
        target = self.put(BASE + "/keep.asset")
        self.adb.path(STAGE).symlink_to(target)
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertIn("link", [r["reason"] for r in plan["protected"]])
        self.value.clean(plan)
        self.assertTrue(target.exists())

    def test_linked_bank_ancestor_is_not_followed(self):
        real = self.root / "foreign"
        real.mkdir()
        (real / (".quest-install-upload-" + TOKEN)).write_bytes(b"foreign")
        location = self.adb.path(BASE)
        location.parent.mkdir(parents=True)
        location.symlink_to(real, target_is_directory=True)
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertIn("linked-path", [r["reason"] for r in plan["unknown"]])
        self.value.clean(plan)
        self.assertTrue((real / (".quest-install-upload-" + TOKEN)).exists())

    def test_hardlinked_upload_is_not_deleted(self):
        stage = self.put(STAGE)
        os.link(stage, stage.with_name("other-link"))
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertIn("unsafe-metadata", [r["reason"] for r in plan["protected"]])

    def test_denied_metadata_is_unknown_not_zero_verified_usage(self):
        self.put(STAGE)
        self.adb.denied = "StreamingAssets"
        plan = self.preview()
        self.assertFalse(plan["complete"])
        self.assertIsNone(plan["unknown"][0]["bytes"])
        self.assertEqual("metadata-unavailable", plan["unknown"][0]["reason"])

    def test_forged_or_reused_plan_is_rejected(self):
        self.put(STAGE)
        plan = self.preview()
        altered = dict(plan, bytes=plan["bytes"] + 1)
        with self.assertRaisesRegex(cleanup.CleanupError, "Refresh"):
            self.value.clean(altered)
        self.value.clean(plan)
        with self.assertRaisesRegex(cleanup.CleanupError, "Refresh"):
            self.value.clean(plan)

    def test_reconnect_keeps_preview_but_refuses_foreign_headset(self):
        self.put(STAGE)
        plan = self.preview()
        other = ShellDevice(self.adb.root)
        other.hardware = "OTHER"
        with self.assertRaisesRegex(cleanup.CleanupError, "same Quest"):
            self.value.reconnect(other, "wifi-other")
        other.hardware = HW
        self.value.reconnect(other, "wifi-same")
        result = self.value.clean(plan)
        self.assertEqual(1, result["deletedFiles"])

    def test_cancellation_during_preview_has_no_mutation(self):
        stage = self.put(STAGE)
        with self.assertRaisesRegex(RuntimeError, "cancelled"):
            self.value.preview(cancel=mock.Mock(side_effect=RuntimeError("cancelled")))
        self.assertTrue(stage.exists())
        self.assertFalse(any("rm " in script for script in self.adb.scripts))

    def test_host_evidence_symlink_and_wrong_journal_identity_are_rejected(self):
        journal = self.journal(deviceSerial="OTHER")
        with self.assertRaisesRegex(cleanup.CleanupError, "identity"):
            self.preview()
        journal.unlink()
        target = self.root / "unrelated.json"
        target.write_text("{}")
        journal.symlink_to(target)
        with self.assertRaisesRegex(cleanup.CleanupError, "linked local"):
            self.preview()

    def jdwp_log(self):
        relative = "cache/.ghvrq-jdwp-" + TOKEN
        path = self.upload_log()
        with path.open("a") as stream:
            stream.write("\n" + json.dumps(["adb", "-s", ADDRESS, "shell", "-T", "run-as", cleanup.PACKAGE, "sh", "-s"]))
            stream.write("\n" + json.dumps({"stdinScript": "umask 077\nmkdir " + relative + "\ncat > " + relative + "/job.sh"}))
        return "/data/user/0/" + cleanup.PACKAGE + "/" + relative

    def test_completed_jdwp_scratch_removes_only_known_job_files(self):
        self.journal()
        root = self.jdwp_log()
        for name in ("job.sh", "stdout", "stderr", "status"):
            self.put(root + "/" + name, b"data")
        plan = self.preview()
        self.assertEqual(["debugger-scratch"], [row["kind"] for row in plan["candidates"]])
        self.assertEqual(4, plan["files"])
        result = self.value.clean(plan)
        self.assertEqual(4, result["deletedFiles"])
        self.assertFalse(self.adb.path(root).exists())
        self.assertFalse(any("rm -rf" in script for script in self.adb.scripts))

    def test_jdwp_unknown_child_retains_entire_tree(self):
        self.journal()
        root = self.jdwp_log()
        known = self.put(root + "/stdout")
        unique = self.put(root + "/unknown-save")
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertIn("unknown-members", [r["reason"] for r in plan["protected"]])
        self.value.clean(plan)
        self.assertTrue(known.exists())
        self.assertTrue(unique.exists())

    def test_jdwp_pending_journal_keeps_completed_child_for_recovery(self):
        self.journal("restoring")
        root = self.jdwp_log()
        status = self.put(root + "/status", b"0")
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.assertIn("pending-or-unproved-recovery", [r["reason"] for r in plan["protected"]])
        self.assertTrue(status.exists())

    def test_unverified_or_foreign_log_cannot_own_private_jdwp_scratch(self):
        self.journal()
        root = self.jdwp_log()
        path = next(self.directory.glob("wireless-install-*.log"))
        path.write_text(path.read_text().replace(HW, "FOREIGN-QUEST"))
        file = self.put(root + "/stdout")
        plan = self.preview()
        self.assertFalse(plan["candidates"])
        self.value.clean(plan)
        self.assertTrue(file.exists())

    def test_automatic_cleanup_is_tiny_and_does_not_scan_log_or_device_roots(self):
        self.journal()
        helper = self.put("/data/local/tmp/.ghvr-rename-" + TXN)
        upload = self.put(STAGE)
        bank = self.put(PARK + "/quest-owned-game/unique.asset")
        result = cleanup.cleanup_after_install(self.adb, ADDRESS, HW, self.directory,
                    types.SimpleNamespace(sha256=SHA, input_key=KEY))
        self.assertEqual(1, result["deletedFiles"])
        self.assertFalse(helper.exists())
        self.assertTrue(upload.exists())
        self.assertTrue(bank.exists())
        self.assertFalse(any("find " in s for s in self.adb.scripts))

    def test_automatic_cleanup_preserves_pending_or_different_source_transaction(self):
        helper = self.put("/data/local/tmp/.ghvr-rename-" + TXN)
        for state, source in (("restoring", SHA), ("complete", "f" * 64)):
            self.journal(state)
            result = cleanup.cleanup_after_install(self.adb, ADDRESS, HW, self.directory,
                    types.SimpleNamespace(sha256=source, input_key=KEY))
            self.assertEqual("pending-or-different-recovery", result["deferred"])
            self.assertTrue(helper.exists())
        self.assertFalse(self.adb.scripts)

    def test_automatic_cleanup_denial_does_not_fail_successful_installation(self):
        self.journal()
        self.put("/data/local/tmp/.ghvr-rename-" + TXN)
        self.adb.denied = ".ghvr-rename-"
        result = cleanup.cleanup_after_install(self.adb, ADDRESS, HW, self.directory,
                    types.SimpleNamespace(sha256=SHA, input_key=KEY))
        self.assertFalse(result["complete"])
        self.assertEqual(0, result["deletedFiles"])


if __name__ == "__main__":
    unittest.main()
