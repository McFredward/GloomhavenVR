"""Replacement-UID restoration and bounded external-storage view controls."""
import importlib.util
import contextlib
import io
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_restore_app_access", ROOT / "tools/quest-installer/signing_recovery.py")
recovery = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(recovery)
LEGACY_SPEC = importlib.util.spec_from_file_location("quest_restore_access_filesystem_fixture", Path(__file__).with_name("test_signing_recovery.py"))
fixture = importlib.util.module_from_spec(LEGACY_SPEC)
LEGACY_SPEC.loader.exec_module(fixture)


class RestoreAccessTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.log = self.root / "adb.log"
        self.adb = types.SimpleNamespace(log=self.log, identity=mock.Mock(return_value="QUEST-LOCAL"), run=mock.Mock())
        self.view = recovery.AppStorageView(self.adb, "usb-local", "QUEST-LOCAL")

    def engine(self):
        value = object.__new__(recovery.Recovery)
        value.adb = self.adb
        value.address = "usb-local"
        value.hardware = "QUEST-LOCAL"
        value._direct_storage_proved = False
        value._storage_process = None
        value.park = "/sdcard/Download/.ghvr-quest-recovery/QUEST-LOCAL/" + "a" * 32
        return value

    def test_shared_view_has_no_signing_journal_or_payload_access(self):
        self.assertFalse(hasattr(self.view, "source"))
        self.assertFalse(hasattr(self.view, "journal"))
        self.assertFalse(hasattr(self.view, "backup"))
        self.adb.identity.assert_not_called()
        self.adb.run.assert_not_called()

    def test_shared_view_rejects_foreign_and_sibling_paths_before_any_command(self):
        for value in ("/sdcard/Android/data/another.app", recovery.EXTERNAL + "-foreign", "/sdcard/Download/retained",
                      recovery.EXTERNAL + "/../foreign", recovery.EXTERNAL + "/files/./log", recovery.EXTERNAL + "//files"):
            with self.subTest(value=value), self.assertRaisesRegex(recovery.RecoveryError, "outside"):
                self.view._access_root(value, "app-direct")
        self.adb.run.assert_not_called()

    def test_shared_view_preserves_original_exception_after_cleanup_failure(self):
        with mock.patch.object(self.view, "_close_storage_process", side_effect=recovery.RecoveryError("cleanup denied")):
            with mock.patch("sys.stdout", new=io.StringIO()) as output:
                with self.assertRaisesRegex(ValueError, "original fault"):
                    with self.view: raise ValueError("original fault")
                self.assertIn("cleanup denied", output.getvalue())

    def test_shared_view_requires_cleanup_success_before_successful_exit(self):
        with mock.patch.object(self.view, "_close_storage_process", side_effect=recovery.RecoveryError("cleanup denied")):
            with self.assertRaisesRegex(recovery.RecoveryError, "cleanup denied"):
                with self.view: pass

    def test_external_tar_never_uses_shell_capture_identity(self):
        engine = self.engine()
        for mode in (None, "shell", "foreign"):
            with self.subTest(mode=mode), self.assertRaisesRegex(recovery.RecoveryError, "replacement app UID"):
                engine._restore_tar_args(recovery.EXTERNAL, access_mode=mode)
        self.adb.run.assert_not_called()

    def test_external_tar_keeps_current_app_uid_even_for_old_600_file_modes(self):
        with mock.patch.object(self.view, "_access_root", return_value="/storage/emulated/0/Android/data/" + recovery.PACKAGE):
            args = self.view._restore_tar_args(recovery.EXTERNAL, access_mode="app-direct")
        self.assertEqual(args[:3], ["exec-in", "run-as", recovery.PACKAGE])
        self.assertEqual(args[-2:], ["-xof", "-"])

    def test_private_tar_retains_current_app_uid(self):
        args = self.engine()._restore_tar_args("files", internal=True)
        self.assertEqual(args, ["exec-in", "run-as", recovery.PACKAGE, "tar", "-C", "files", "-xof", "-"])

    def test_direct_app_probe_is_fresh_and_leaves_capture_mode_unchanged(self):
        engine = self.engine()
        item = {"root": recovery.EXTERNAL, "internal": False, "accessMode": "shell", "restoreAccessMode": "app-process"}
        scripts = []
        def command(script, mode):
            if script.endswith("printf GHVRQ_APP_ROOT_READY"): return "GHVRQ_APP_ROOT_READY"
            scripts.append((script, mode))
            return script.rsplit("printf %s ", 1)[1]
        with mock.patch.object(engine, "_access_root", return_value="/storage/emulated/0/Android/data/" + recovery.PACKAGE), \
                mock.patch.object(engine, "_check_external_root") as check, \
                mock.patch.object(engine, "_context_shell", side_effect=command), \
                mock.patch.object(engine, "_process_storage_root") as process:
            self.assertEqual(engine._restore_access_mode(item), "app-direct")
            self.assertEqual(engine._restore_access_mode(item), "app-direct")
        self.assertEqual(item["accessMode"], "shell")
        self.assertNotEqual(scripts[0][0], scripts[1][0])
        self.assertTrue(all(mode == "app-direct" for _, mode in scripts))
        self.assertTrue(all("umask 077" in script and "trap" in script for script, _ in scripts))
        process.assert_not_called()
        self.assertEqual(check.call_count, 2)

    def test_denied_direct_view_uses_only_guarded_app_process_view(self):
        engine = self.engine()
        modes = []
        def command(script, mode):
            if script.endswith("printf GHVRQ_APP_ROOT_READY"):
                if mode == "app-direct": raise recovery.RecoveryError("app namespace denied")
                return "GHVRQ_APP_ROOT_READY"
            modes.append(mode)
            if mode == "app-direct": raise recovery.RecoveryError("app namespace denied")
            return script.rsplit("printf %s ", 1)[1]
        with mock.patch.object(engine, "_access_root", return_value="/storage/emulated/0/Android/data/" + recovery.PACKAGE), \
                mock.patch.object(engine, "_check_external_root"), \
                mock.patch.object(engine, "_context_shell", side_effect=command):
            self.assertEqual(engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False}), "app-process")
        self.assertEqual(modes, ["app-process"])

    def test_invalid_acknowledgement_never_selects_shell_restore(self):
        engine = self.engine()
        with mock.patch.object(engine, "_access_root", return_value="/storage/emulated/0/Android/data/" + recovery.PACKAGE), \
                mock.patch.object(engine, "_check_external_root"), \
                mock.patch.object(engine, "_context_shell", return_value="incomplete"):
            with self.assertRaisesRegex(recovery.RecoveryError, "scoped retry"):
                engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False})
        self.assertIn('"shellRestoreAllowed": false', self.log.read_text())

    def test_missing_external_root_is_created_only_through_its_replacement_uid(self):
        (self.root / "storage/emulated/0/Android/data").mkdir(parents=True)
        actual = str(self.root / "storage/emulated/0/Android/data" / recovery.PACKAGE)
        scripts = []
        def context(script, mode):
            scripts.append((script, mode))
            ran = subprocess.run(["sh", "-c", "set -eu\n" + script], capture_output=True, text=True)
            if ran.returncode: raise recovery.RecoveryError(ran.stderr)
            return ran.stdout
        engine = self.engine()
        with mock.patch.object(engine, "_access_root", return_value=actual), \
                mock.patch.object(engine, "_context_shell", side_effect=context):
            engine._prepare_external_restore_root(actual, "app-direct")
            engine._prepare_external_restore_root(actual, "app-direct")
        self.assertTrue(Path(actual).is_dir())
        self.assertEqual(Path(actual).stat().st_mode & 0o777, 0o700)
        self.assertTrue(all(mode == "app-direct" for _, mode in scripts))
        self.assertFalse(any("chmod" in script or "chown" in script for script, _ in scripts))
        self.adb.run.assert_not_called()

    def test_external_root_creation_rejects_target_and_ancestor_symlinks(self):
        container = self.root / "storage/emulated/0/Android/data"
        container.mkdir(parents=True)
        target = container / recovery.PACKAGE
        foreign = self.root / "foreign"
        foreign.mkdir()
        def context(script, mode):
            ran = subprocess.run(["sh", "-c", "set -eu\n" + script], capture_output=True, text=True)
            if ran.returncode: raise recovery.RecoveryError("guarded creation refused")
            return ran.stdout
        engine = self.engine()
        with mock.patch.object(engine, "_access_root", return_value=str(target)), \
                mock.patch.object(engine, "_context_shell", side_effect=context):
            target.symlink_to(foreign)
            with self.assertRaisesRegex(recovery.RecoveryError, "guarded creation refused"):
                engine._prepare_external_restore_root(str(target), "app-direct")
            target.unlink()
            container.rmdir(); container.symlink_to(foreign)
            with self.assertRaisesRegex(recovery.RecoveryError, "guarded creation refused"):
                engine._prepare_external_restore_root(str(target), "app-direct")
        self.assertEqual(list(foreign.iterdir()), [])

    def test_root_preparation_rejects_foreign_root_and_shell_identity(self):
        engine = self.engine()
        with mock.patch.object(engine, "_access_root", return_value="/storage/emulated/0/Android/data/" + recovery.PACKAGE):
            for actual, mode in (("/storage/emulated/0/Android/data/foreign", "app-direct"), (recovery.EXTERNAL, "shell")):
                with self.subTest(actual=actual, mode=mode), self.assertRaisesRegex(recovery.RecoveryError, "replacement app view"):
                    engine._prepare_external_restore_root(actual, mode)
        self.adb.run.assert_not_called()

    def test_launch_pending_action_survives_restore_access_selection(self):
        engine = self.engine()
        pending = recovery.CaptureRepairError("wake and unlock", root="external", reason="restore-app-access", attempts=2, total=2)
        def actual(root, mode):
            if mode == "app-process": raise pending
            raise recovery.RecoveryError("direct app view is inaccessible")
        with mock.patch.object(engine, "_access_root", side_effect=actual):
            with self.assertRaises(recovery.CaptureRepairError) as error:
                engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False})
        self.assertIs(error.exception, pending)
        self.assertEqual(error.exception.action["reason"], "restore-app-access")

    def test_only_observed_sleeping_headset_receives_one_idempotent_wake_request(self):
        for wakefulness, wake_denied, expected in (("Asleep", False, 1), ("Dozing", False, 1), ("Awake", False, 0), ("", False, 0), ("Asleep", True, 1)):
            with self.subTest(wakefulness=wakefulness, wake_denied=wake_denied):
                engine = self.engine()
                calls = []
                def command(*args, **options):
                    calls.append(args)
                    if "resolve-activity" in args: return recovery.PACKAGE + "/com.unity3d.player.UnityPlayerActivity"
                    if "KEYCODE_WAKEUP" in args and wake_denied: raise recovery.RecoveryError("input is unavailable on this Android build")
                    return ""
                def shell(script, **options):
                    if script == "date +%s": return "1000"
                    if script.startswith("dumpsys activity processes"): return ""
                    if script.startswith("printf 'GHVRQ_STORAGE_POWER"):
                        return "GHVRQ_STORAGE_POWER\n" + ("  mWakefulness=" + wakefulness + "\n" if wakefulness else "")
                    raise AssertionError(script)
                with mock.patch.object(engine, "shell", side_effect=shell), \
                        mock.patch.object(self.adb, "run", side_effect=command), mock.patch.object(recovery.time, "sleep"), \
                        contextlib.redirect_stdout(io.StringIO()) as output:
                    with self.assertRaises(recovery.CaptureRepairError) as error:
                        engine._process_storage_root()
                self.assertEqual(error.exception.action["reason"], "restore-app-launch-unavailable" if wakefulness == "Awake" else "restore-app-access")
                self.assertEqual(sum("KEYCODE_WAKEUP" in call for call in calls), expected)
                self.assertEqual(sum("start" in call for call in calls), 2)
                self.assertEqual(sum("pidof" in call for call in calls), 40)
                self.assertFalse(any("clear-debug-app" in call or "set-debug-app" in call for call in calls))
                updates = [json.loads(line[len("GHVRQ_PROGRESS "):]) for line in output.getvalue().splitlines() if line.startswith("GHVRQ_PROGRESS ")]
                self.assertTrue(updates)
                self.assertTrue(all(row["done"] < row["total"] for row in updates))
                if wake_denied: self.assertIn("storageWakeRequestUnavailable", self.log.read_text())

    def test_restore_connection_failure_is_preserved_without_probing_another_mode(self):
        engine = self.engine()
        ConnectionRequired = type("ConnectionRequired", (RuntimeError,), {})
        interrupted = ConnectionRequired("Reconnect the Quest")
        with mock.patch.object(engine, "_access_root", side_effect=interrupted) as actual:
            with self.assertRaises(ConnectionRequired) as error:
                engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False})
        self.assertIs(error.exception, interrupted)
        self.assertEqual(actual.call_count, 1)

    def test_actual_tar_restores_fresh_missing_external_root_without_launching_game(self):
        class MissingRootDevice(fixture.Device):
            def __call__(self, command, **options):
                result = super().__call__(command, **options)
                action = command[3:] if command[1:2] == ["-s"] else command[1:]
                if action[:1] == ["install"] and result.returncode == 0:
                    shutil.rmtree(self.path(recovery.EXTERNAL))
                return result
        device = MissingRootDevice(self.root / "device")
        adb = fixture.installer.Adb("adb", self.log, device)
        source = types.SimpleNamespace(apk=device.new_apk, sha256=recovery.file_hash(device.new_apk), input_key="a" * 64)
        engine = recovery.Recovery(adb, "usb-local", "QUEST-LOCAL", source, self.root / "workspace", data_policy="saves")
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual(device.device_save_path().read_bytes(), b"campaign\0original")
        self.assertFalse(any(row[:3] == ("shell", "am", "start") for row in device.calls))
        writes = [row for row in device.calls if row[:2] == ("shell", "-T")]
        self.assertTrue(any(row[2:4] == ("run-as", recovery.PACKAGE) for row in writes))

    def test_foreign_device_cannot_reach_app_restore_probe(self):
        engine = self.engine()
        self.adb.identity.return_value = "OTHER-QUEST"
        with mock.patch.object(engine, "_access_root") as root:
            with self.assertRaisesRegex(recovery.RecoveryError, "different headset"):
                engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False})
        root.assert_not_called()

    def test_foreign_external_restore_root_is_rejected(self):
        with self.assertRaisesRegex(recovery.RecoveryError, "exact recovery root"):
            self.engine()._restore_access_mode({"root": recovery.EXTERNAL + "/../foreign", "internal": False})
        self.adb.run.assert_not_called()

    def transaction(self, *, process=False):
        device = fixture.Device(self.root / "device")
        if process:
            device.app_sdcard_denied = device.app_direct_denied = True
            device.process_enabled = True
        adb = fixture.installer.Adb("adb", self.log, device)
        source = types.SimpleNamespace(apk=device.new_apk, sha256=recovery.file_hash(device.new_apk), input_key="a" * 64)
        engine = recovery.Recovery(adb, "usb-local", "QUEST-LOCAL", source, self.root / "workspace", data_policy="saves")
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertTrue(engine.recover(detected=True)["verified"])
        return engine, device

    def test_actual_tar_transaction_changes_only_restore_identity(self):
        engine, device = self.transaction()
        external = next(row for row in engine.journal["roots"] if row["name"] == "external")
        self.assertEqual(external["accessMode"], "shell")
        self.assertEqual(external["restoreAccessMode"], "app-direct")
        self.assertTrue(all(mode == "app" for mode, args in device.tar_modes if "-xof" in args))
        self.assertEqual(device.path(recovery.EXTERNAL + "/files/GloomSaves/Campaign/party_76561198000000001.save").read_bytes(), b"campaign\0original")
        self.assertFalse(any(call[:3] == ("shell", "am", "start") for call in device.calls))

    def test_actual_tar_transaction_uses_waiting_process_only_when_direct_alias_denied(self):
        engine, device = self.transaction(process=True)
        external = next(row for row in engine.journal["roots"] if row["name"] == "external")
        self.assertEqual(external["accessMode"], "shell")
        self.assertEqual(external["restoreAccessMode"], "app-process")
        self.assertTrue(all(mode == "app" for mode, args in device.tar_modes if "-xof" in args))
        self.assertTrue(any(call[:3] == ("shell", "am", "start") and "-D" in call for call in device.calls))
        self.assertFalse(device.process_started)
        self.assertIsNone(engine._storage_process)


class MutableAdb:
    """Portable shell/byte runner; denied-owner policy is injected explicitly."""
    def __init__(self, root):
        self.root = root
        self.log = root / "adb.log"
        self.hardware = "QUEST-LOCAL"
        self.denied = set()
        self.calls = []
        self.views = []
        self.change_after_pull = False
        self.lose_acknowledgement = False
        (root / "external/files").mkdir(parents=True)

    def identity(self, address): return self.hardware
    def translate(self, text):
        return text.replace(recovery.EXTERNAL, str(self.root / "external")).replace(
            "/storage/emulated/0/Android/data/" + recovery.PACKAGE, str(self.root / "external"))

    def run(self, *args, **options):
        self.calls.append(args)
        if args[2] != "pull": raise AssertionError("Unexpected command " + repr(args))
        origin, target = Path(self.translate(args[3])), Path(args[4])
        shutil.copyfile(origin, target)
        if self.change_after_pull: origin.write_bytes(b"newest current state")
        return "one bounded file pulled"

    def binary_stream(self, address, local, script, timeout):
        command = shlex.split(script)
        self.calls.append(("binary", script, Path(local).stat().st_size))
        if command[:5] != ["exec", "run-as", recovery.PACKAGE, "sh", "-c"]:
            raise AssertionError("Binary write did not select the actual app UID")
        result = subprocess.run(["sh", "-c", self.translate(command[5])], input=Path(local).read_bytes(),
                                capture_output=True, check=False)
        if result.returncode == 0: self.denied.clear()
        return types.SimpleNamespace(returncode=result.returncode, stdout="" if self.lose_acknowledgement else result.stdout.decode(),
                                     stderr=result.stderr.decode())


class MutableView:
    def __init__(self, adb, address, hardware):
        self.adb, self.hardware = adb, hardware
        self.closed = False
        adb.views.append(self)

    def __enter__(self): return self
    def __exit__(self, *_): self.closed = True
    def _device_guard(self):
        if self.adb.hardware != self.hardware: raise recovery.RecoveryError("foreign headset")
    def _access_root(self, root, mode):
        return root.replace("/sdcard", "/storage/emulated/0")
    def _check_external_root(self, root, mode): pass
    def _process_storage_root(self): raise AssertionError("Unnecessary paused-process fallback")
    def shell(self, script, timeout=60):
        self.adb.calls.append(("shell", script))
        result = subprocess.run(["sh", "-c", "set -eu\n" + self.adb.translate(script)], capture_output=True, text=True)
        if result.returncode: raise recovery.RecoveryError(result.stderr)
        return result.stdout.strip().replace(str(self.adb.root / "external"), recovery.EXTERNAL)
    def _context_shell(self, script, mode):
        self.adb.calls.append(("app", mode, script))
        for remote in self.adb.denied:
            if remote.replace("/sdcard", "/storage/emulated/0") in script and "head -c 1" in script:
                raise recovery.RecoveryError("old-owner access denied")
        return self.shell(script).replace(recovery.EXTERNAL, "/storage/emulated/0/Android/data/" + recovery.PACKAGE)


class CurrentMutableAccessTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.adb = MutableAdb(self.root)
        self.source = types.SimpleNamespace(sha256="b" * 64, input_key="a" * 64)
        self.log = self.root / "external/files/quest-startup.log"
        self.log.write_bytes(b"current source log, not a prior backup")

    def repair(self, directory=None):
        return recovery.repairs_current_external_access(self.adb, "usb-local", "QUEST-LOCAL", self.source,
                                                       directory, view_factory=MutableView)

    def denied(self, relative="files/quest-startup.log"):
        self.adb.denied.add(recovery.EXTERNAL + "/" + relative)

    def journal(self, inventory, **changes):
        directory = self.root / "config"
        path = directory / "signing-recovery/QUEST-LOCAL/active.json"
        path.parent.mkdir(parents=True)
        value = {"schema": 1, "package": recovery.PACKAGE, "deviceSerial": "QUEST-LOCAL", "state": "complete",
                 "apkSha256": self.source.sha256, "inputKey": self.source.input_key,
                 "roots": [{"name": "external", "root": recovery.EXTERNAL, "internal": False, "inventory": inventory}]}
        value.update(changes)
        path.write_text(json.dumps(value))
        return directory

    def test_current_bad_log_bytes_preserved_and_successor_is_private(self):
        self.denied()
        expected = self.log.read_bytes()
        inode = self.log.stat().st_ino
        result = self.repair()
        self.assertEqual(result["mutableAccessRepairedFiles"], 1)
        self.assertEqual(self.log.read_bytes(), expected)
        self.assertNotEqual(self.log.stat().st_ino, inode)
        self.assertEqual(self.log.stat().st_mode & 0o777, 0o600)
        self.assertTrue(all(view.closed for view in self.adb.views))
        self.assertFalse(any("quest-owned-game" in str(call) or "find " in str(call) for call in self.adb.calls))

    def test_healthy_current_files_need_no_download_or_replacement(self):
        inode = self.log.stat().st_ino
        result = self.repair()
        self.assertEqual(result["mutableAccessRepairedFiles"], 0)
        self.assertEqual(result["mutableAccessVerifiedFiles"], 1)
        self.assertEqual(self.log.stat().st_ino, inode)
        self.assertFalse(any(call[0] == "binary" or len(call) > 2 and call[2] == "pull" for call in self.adb.calls))

    def test_matching_inventory_repairs_only_current_named_mutable_files(self):
        settings = self.root / "external/files/future-settings.json"
        settings.write_bytes(b"newest user setting")
        self.denied("files/future-settings.json")
        directory = self.journal({"files/future-settings.json": {"type": "file", "size": 1, "sha256": "old archive sha"},
                                  "files/quest-owned-game/StreamingAssets/big.bundle": {"type": "file", "size": 11000000000}})
        result = self.repair(directory)
        self.assertTrue(result["mutableAccessJournalQualified"])
        self.assertEqual(result["mutableAccessRepairedFiles"], 1)
        self.assertEqual(settings.read_bytes(), b"newest user setting")
        self.assertFalse(any("big.bundle" in str(call) for call in self.adb.calls))

    def test_mismatched_or_other_hardware_journal_cannot_select_unknown_settings(self):
        directory = self.journal({"files/do-not-touch.json": {"type": "file"}}, deviceSerial="OTHER-QUEST")
        result = self.repair(directory)
        self.assertFalse(result["mutableAccessJournalQualified"])
        self.assertFalse(any("do-not-touch" in str(call) for call in self.adb.calls))

    def test_linked_diagnostic_never_mutates_its_target(self):
        self.log.unlink()
        outside = self.root / "outside.settings"
        outside.write_bytes(b"untouched")
        self.log.symlink_to(outside)
        with self.assertRaises(recovery.RecoveryError): self.repair()
        self.assertEqual(outside.read_bytes(), b"untouched")
        self.assertFalse(any(call[0] == "binary" for call in self.adb.calls))
        self.assertTrue(all(view.closed for view in self.adb.views))

    def test_known_current_oversized_file_pauses_before_transfer(self):
        with self.log.open("wb") as stream: stream.truncate(2 * 1024 * 1024 + 1)
        self.denied()
        with self.assertRaises(recovery.CaptureRepairError) as caught: self.repair()
        self.assertEqual(caught.exception.action["reason"], "current-app-access-bound")
        self.assertFalse(any(call[0] == "binary" or len(call) > 2 and call[2] == "pull" for call in self.adb.calls))

    def test_changed_current_bytes_are_retained_without_old_backup_replay(self):
        self.denied()
        self.adb.change_after_pull = True
        with self.assertRaisesRegex(recovery.RecoveryError, "metadata changed"): self.repair()
        self.assertEqual(self.log.read_bytes(), b"newest current state")
        self.assertFalse(any(call[0] == "binary" for call in self.adb.calls))

    def test_lost_ack_keeps_new_private_current_successor_and_retry_is_read_only(self):
        self.denied()
        self.adb.lose_acknowledgement = True
        expected = self.log.read_bytes()
        with self.assertRaises(recovery.CaptureRepairError): self.repair()
        self.assertEqual(self.log.read_bytes(), expected)
        self.assertEqual(self.log.stat().st_mode & 0o777, 0o600)
        self.adb.lose_acknowledgement = False
        calls = len(self.adb.calls)
        self.assertEqual(self.repair()["mutableAccessRepairedFiles"], 0)
        self.assertFalse(any(call[0] == "binary" for call in self.adb.calls[calls:]))

    def test_foreign_hardware_cannot_reach_permission_or_byte_mutations(self):
        self.adb.hardware = "OTHER-QUEST"
        with self.assertRaisesRegex(recovery.RecoveryError, "foreign headset"): self.repair()
        self.assertFalse(self.adb.calls)


if __name__ == "__main__": unittest.main()
