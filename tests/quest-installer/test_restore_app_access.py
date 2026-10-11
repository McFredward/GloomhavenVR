"""Replacement-UID restoration and bounded external-storage view controls."""
import importlib.util
import contextlib
import io
from pathlib import Path
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
            modes.append(mode)
            if mode == "app-direct": raise recovery.RecoveryError("app namespace denied")
            return script.rsplit("printf %s ", 1)[1]
        with mock.patch.object(engine, "_access_root", return_value="/storage/emulated/0/Android/data/" + recovery.PACKAGE), \
                mock.patch.object(engine, "_check_external_root"), \
                mock.patch.object(engine, "_context_shell", side_effect=command):
            self.assertEqual(engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False}), "app-process")
        self.assertEqual(modes, ["app-direct", "app-process"])

    def test_invalid_acknowledgement_never_selects_shell_restore(self):
        engine = self.engine()
        with mock.patch.object(engine, "_access_root", return_value="/storage/emulated/0/Android/data/" + recovery.PACKAGE), \
                mock.patch.object(engine, "_check_external_root"), \
                mock.patch.object(engine, "_context_shell", return_value="incomplete"):
            with self.assertRaisesRegex(recovery.RecoveryError, "scoped retry"):
                engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False})
        self.assertIn('"shellRestoreAllowed": false', self.log.read_text())

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


if __name__ == "__main__": unittest.main()
