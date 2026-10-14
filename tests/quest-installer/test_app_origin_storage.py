"""App-origin restore integration, with real local shell/tar/hash processes.

These fixtures model the separately denied run-as aliases explicitly. They
exercise transfer/framing and exact recovery scope, not Android FUSE behavior
or the JDWP protocol, which has its own transport/control tests.
"""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import types
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_app_origin_restore", ROOT / "tools/quest-installer/signing_recovery.py")
recovery = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(recovery)


class AppOriginStorageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.external = self.root / "external"
        self.external.mkdir()
        self.private_remote = "/data/user/0/" + recovery.PACKAGE
        self.remote_stage = self.private_remote + "/cache/.quest-restore-stage-" + "a" * 32
        self.stage = self.root / "private/cache" / (".quest-restore-stage-" + "a" * 32)
        self.stage.parent.mkdir(parents=True)
        self.calls = []
        self.adb = types.SimpleNamespace(log=self.root / "adb.log", identity=mock.Mock(return_value="LOCAL-QUEST"),
                                         run=mock.Mock(), runner=mock.Mock(), executable="adb")
        self.engine = object.__new__(recovery.Recovery)
        self.engine.adb, self.engine.address, self.engine.hardware = self.adb, "usb", "LOCAL-QUEST"
        self.engine._storage_process = None
        self.engine._storage_command = None
        self.engine._direct_storage_proved = False
        self.engine.park = "/sdcard/Download/.ghvr-recovery/retained"
        self.engine.data_policy = "saves"
        self.engine.journal = {"smallDataOnly": True, "banks": [], "state": "restoring"}
        self.engine.path = self.root / "active.json"
        self.engine._regenerated_apk_files = lambda: ()

    def shell(self, script, mode, **options):
        self.calls.append((mode, script))
        translated = script.replace(self.private_remote, str(self.root / "private"))
        result = subprocess.run(["sh", "-s"], input="set -eu\n" + translated,
                                capture_output=True, text=True, timeout=20)
        if result.returncode: raise recovery.RecoveryError(result.stderr or "fixture denied")
        return result.stdout

    def transfer(self, args, local, **options):
        self.assertEqual(args[:5], ["exec-in", "run-as", recovery.PACKAGE, "sh", "-c"])
        self.assertTrue(options["restore"])
        self.assertIn("umask 077", args[-1])
        self.calls.append(("private-binary", args[-1]))
        with Path(local).open("rb") as source:
            result = subprocess.run(["sh", "-c", args[-1].replace(self.private_remote, str(self.root / "private"))], stdin=source,
                                    capture_output=True, timeout=20)
        if result.returncode: raise recovery.RecoveryError("fixture private transfer denied")

    def restore_fixture(self):
        source = self.root / "saved"
        (source / "files/GloomSaves/Campaign").mkdir(parents=True)
        party = source / "files/GloomSaves/Campaign/party.save"
        party.write_bytes(b"original\x00campaign\r\n" * 1000)
        party.chmod(0o600)
        archive = self.root / "retained.restore.tar"
        with tarfile.open(archive, "w:") as output:
            for path in sorted(source.rglob("*")):
                output.add(path, arcname=str(path.relative_to(source)), recursive=False)
        self.item = {"name": "external", "root": recovery.EXTERNAL, "internal": False,
                     "inventory": recovery.archive_inventory(archive), "restoreAccessMode": "app-jdwp"}
        return archive, party.read_bytes()

    def patches(self):
        stack = contextlib.ExitStack()
        stack.enter_context(mock.patch.object(self.engine, "_access_root", return_value=str(self.external)))
        stack.enter_context(mock.patch.object(self.engine, "_check_external_root"))
        stack.enter_context(mock.patch.object(self.engine, "_context_shell", side_effect=self.shell))
        stack.enter_context(mock.patch.object(self.engine, "_private_storage_stage", return_value=self.remote_stage))
        stack.enter_context(mock.patch.object(self.engine, "_transfer", side_effect=self.transfer))
        return stack

    def test_external_restore_uses_real_tar_in_app_origin_and_deletes_only_private_stage(self):
        archive, expected = self.restore_fixture()
        kept = self.stage.parent / "other-owner"
        kept.write_bytes(b"keep")
        with self.patches(), contextlib.redirect_stdout(io.StringIO()):
            self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
            self.engine._verify_app_context_restore(self.item)
        actual = self.external / "files/GloomSaves/Campaign/party.save"
        self.assertEqual(actual.read_bytes(), expected)
        self.assertEqual(actual.stat().st_mode & 0o777, 0o600)
        self.assertFalse(self.stage.exists())
        self.assertEqual(kept.read_bytes(), b"keep")
        self.assertTrue(archive.exists())
        self.assertTrue(any(mode == "app-jdwp" and "tar -C " in script for mode, script in self.calls))
        self.assertFalse(any(mode == "app" and "tar -C " in script for mode, script in self.calls))
        self.assertTrue(all(mode == "app-jdwp" for mode, script in self.calls if "find " in script))
        self.adb.runner.assert_not_called()

    def test_interrupted_binary_transfer_never_extracts_and_retains_host_original(self):
        archive, _ = self.restore_fixture()
        def broken(args, local, **options):
            self.stage.write_bytes(b"partial")
            raise recovery.RecoveryError("connection interrupted")
        with self.patches(), mock.patch.object(self.engine, "_transfer", side_effect=broken), \
                contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(recovery.RecoveryError, "connection interrupted"):
                self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
        self.assertFalse(self.stage.exists())
        self.assertTrue(archive.exists())
        self.assertEqual(list(self.external.iterdir()), [])
        self.assertFalse(any("tar -C " in script for _, script in self.calls))

    def test_corrupt_private_stage_never_extracts(self):
        archive, _ = self.restore_fixture()
        def corrupted(args, local, **options):
            self.stage.write_bytes(Path(local).read_bytes())
            with self.stage.open("r+b") as stream: stream.write(b"corrupt")
        with self.patches(), mock.patch.object(self.engine, "_transfer", side_effect=corrupted), \
                contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(recovery.RecoveryError):
                self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
        self.assertFalse(self.stage.exists())
        self.assertTrue(archive.exists())
        self.assertFalse(any("tar -C " in script for _, script in self.calls))

    def test_disconnected_cleanup_is_durable_and_next_retry_removes_only_known_stage(self):
        archive, _ = self.restore_fixture()
        ConnectionRequired = type("ConnectionRequired", (RuntimeError,), {})
        def interrupted(args, local, **options):
            self.stage.write_bytes(b"partial")
            raise ConnectionRequired("USB disconnected")
        def disconnected(script, mode, **options):
            if "GHVRQ_PRIVATE_STAGE_REMOVED" in script: raise ConnectionRequired("still disconnected")
            return self.shell(script, mode, **options)
        with self.patches(), mock.patch.object(self.engine, "_transfer", side_effect=interrupted), \
                mock.patch.object(self.engine, "_context_shell", side_effect=disconnected), \
                contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(ConnectionRequired, "USB disconnected"):
                self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
        self.assertTrue(self.stage.exists())
        retained = json.loads(self.engine.path.read_text())
        self.assertEqual(retained["appContextStage"], self.remote_stage)
        retry = copy.copy(self.engine)
        retry.journal = retained
        sibling = self.stage.parent / "other-owner"
        sibling.write_bytes(b"retained")
        with mock.patch.object(retry, "_context_shell", side_effect=self.shell): retry._cleanup_app_context_stage()
        self.assertFalse(self.stage.exists())
        self.assertIsNone(json.loads(retry.path.read_text())["appContextStage"])
        self.assertEqual(sibling.read_bytes(), b"retained")
        self.assertTrue(archive.exists())

    def test_foreign_stage_receipt_cannot_delete_another_app_or_cache(self):
        for remote in ("/data/user/0/other.app/cache/.quest-restore-stage-" + "a" * 32,
                       self.private_remote + "/cache/other-owner", self.private_remote + "/cache/../files/settings"):
            with self.subTest(remote=remote):
                self.engine.journal["appContextStage"] = remote
                with mock.patch.object(self.engine, "_context_shell") as shell:
                    with self.assertRaisesRegex(recovery.RecoveryError, "exact owned cache scope"):
                        self.engine._cleanup_app_context_stage()
                shell.assert_not_called()

    def test_journalless_view_retains_and_clears_device_bound_stage_receipt(self):
        view = recovery.AppStorageView(self.adb, "usb", "LOCAL-QUEST")
        self.stage.write_bytes(b"owned partial")
        view._remember_app_context_stage(self.remote_stage)
        path = self.root / "app-storage-stage-LOCAL-QUEST.json"
        self.assertTrue(path.exists())
        with mock.patch.object(view, "_context_shell", side_effect=self.shell): view._cleanup_app_context_stage()
        self.assertFalse(path.exists())
        self.assertFalse(self.stage.exists())

    def test_empty_directories_and_terminal_link_targets_are_verified_exactly(self):
        archive, _ = self.restore_fixture()
        source = self.root / "saved"
        (source / "empty.settings").mkdir()
        (source / "terminal.link").symlink_to("missing-target\n")
        with tarfile.open(archive, "w:") as output:
            for path in sorted(source.rglob("*")):
                output.add(path, arcname=str(path.relative_to(source)), recursive=False)
        self.item["inventory"] = recovery.archive_inventory(archive)
        with self.patches(), contextlib.redirect_stdout(io.StringIO()):
            self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
            self.engine._verify_app_context_restore(self.item)
        self.assertTrue((self.external / "empty.settings").is_dir())
        self.assertEqual((self.external / "terminal.link").readlink(), Path("missing-target\n"))

    def test_actual_app_checksum_rejects_changed_restored_file(self):
        archive, _ = self.restore_fixture()
        with self.patches(), contextlib.redirect_stdout(io.StringIO()):
            self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
            target = self.external / "files/GloomSaves/Campaign/party.save"
            changed = bytearray(target.read_bytes()); changed[-1] ^= 1; target.write_bytes(changed)
            with self.assertRaisesRegex(recovery.RecoveryError, "size/hash"):
                self.engine._verify_app_context_restore(self.item)
        self.assertTrue(archive.exists())

    def test_true_app_denial_cannot_fall_back_to_readable_shell_verification(self):
        archive, _ = self.restore_fixture()
        with self.patches(), contextlib.redirect_stdout(io.StringIO()):
            self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
            def denied(script, mode, **options):
                if "find " in script and mode == "app-jdwp": raise recovery.RecoveryError("true app denied")
                return self.shell(script, mode, **options)
            with mock.patch.object(self.engine, "_context_shell", side_effect=denied):
                with self.assertRaisesRegex(recovery.DataPolicyError, "true app denied"):
                    self.engine._verify_app_context_restore(self.item)
        self.assertFalse(any(mode == "shell" for mode, _ in self.calls))

    def test_unknown_user_member_is_not_accepted_as_original(self):
        archive, _ = self.restore_fixture()
        with self.patches(), contextlib.redirect_stdout(io.StringIO()):
            self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
            (self.external / "unexpected.settings").write_bytes(b"new")
            with self.assertRaisesRegex(recovery.RecoveryError, "member census"):
                self.engine._verify_app_context_restore(self.item)

    def test_verification_prunes_owned_game_payload_without_hashing_or_copying_it(self):
        archive, _ = self.restore_fixture()
        with self.patches(), contextlib.redirect_stdout(io.StringIO()):
            self.engine._restore_app_context_archive(archive, recovery.EXTERNAL)
            bank = self.external / "files/quest-owned-game"
            (bank / "StreamingAssets").mkdir(parents=True)
            with (bank / "StreamingAssets/large.bundle").open("wb") as stream: stream.truncate(512 * 1024 * 1024)
            self.item["inventory"]["files/quest-owned-game"] = {"type": "directory"}
            self.engine._verify_app_context_restore(self.item)
        scripts = "\n".join(script for _, script in self.calls)
        self.assertNotIn("large.bundle", scripts)
        self.assertNotIn("cp ", scripts)
        self.assertNotIn("tar -cf", scripts)
        self.assertEqual(sum(mode == "private-binary" for mode, _ in self.calls), 1)

    def test_alias_denials_select_app_origin_without_shell_restore(self):
        modes = []
        def context(script, mode, **options):
            modes.append(mode)
            if mode != "app-jdwp": raise recovery.RecoveryError("FUSE alias denied")
            if script.endswith("printf GHVRQ_APP_ROOT_READY"): return "GHVRQ_APP_ROOT_READY"
            return script.rsplit("printf %s ", 1)[1]
        with mock.patch.object(self.engine, "_access_root", return_value=str(self.external)), \
                mock.patch.object(self.engine, "_check_external_root"), \
                mock.patch.object(self.engine, "_context_shell", side_effect=context):
            selected = self.engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False})
        self.assertEqual(selected, "app-jdwp")
        self.assertEqual(modes[:2], ["app-direct", "app-process"])
        self.assertNotIn("shell", modes)

    def test_existing_app_command_rechecks_guard_without_sigstop_snapshot(self):
        command = mock.Mock()
        self.engine._storage_command = command
        with mock.patch.object(self.engine, "_process_storage_root") as old_root, \
                mock.patch.object(self.engine, "_process_snapshot") as snapshot:
            self.assertIs(self.engine._app_context_command(), command)
        command.guard.assert_called_once()
        old_root.assert_not_called(); snapshot.assert_not_called()

    def test_app_command_factory_receives_only_exact_paused_uid_pid_starttime(self):
        owned = ("10415", "10190", "941516")
        command = mock.Mock()
        factory = mock.Mock(return_value=command)
        def started():
            self.engine._storage_process = owned
            return "/proc/10415/root/storage/emulated/0"
        loader = types.SimpleNamespace(exec_module=lambda module: setattr(module, "AppCommand", factory))
        with mock.patch.object(self.engine, "_process_storage_root", side_effect=started), \
                mock.patch.object(self.engine, "_process_snapshot", return_value=owned) as snapshot, \
                mock.patch.object(recovery.importlib.util, "spec_from_file_location", return_value=types.SimpleNamespace(loader=loader)), \
                mock.patch.object(recovery.importlib.util, "module_from_spec", return_value=types.SimpleNamespace()):
            self.assertIs(self.engine._app_context_command(), command)
        snapshot.assert_called_once_with("10415", stopped=True)
        factory.assert_called_once_with(self.adb, "usb", "LOCAL-QUEST", owned)

    def test_changed_pause_identity_never_attaches_debugger(self):
        self.engine._storage_process = ("10415", "10190", "941516")
        with mock.patch.object(self.engine, "_process_storage_root"), \
                mock.patch.object(self.engine, "_process_snapshot", return_value=("10415", "10190", "941517")), \
                mock.patch.object(recovery.importlib.util, "spec_from_file_location") as load:
            with self.assertRaisesRegex(recovery.RecoveryError, "proved pre-game"):
                self.engine._app_context_command()
        load.assert_not_called()

    def test_private_stage_parent_is_not_followed_through_a_link(self):
        self.adb.run.return_value = "/data/user/0/" + recovery.PACKAGE
        seen = []
        def deny(script, mode, **options):
            seen.append(script)
            raise recovery.RecoveryError("linked cache refused")
        with mock.patch.object(self.engine, "_context_shell", side_effect=deny):
            with self.assertRaisesRegex(recovery.RecoveryError, "linked cache refused"):
                self.engine._private_storage_stage()
        self.assertIn("test ! -L /data/user/0/" + recovery.PACKAGE + "/cache", seen[0])
        self.assertNotIn("mkdir -p", seen[0])

    def test_exhausted_awake_storage_denials_are_not_reported_as_a_sleeping_quest(self):
        with mock.patch.object(self.engine, "_access_root", side_effect=recovery.RecoveryError("namespace denied")):
            with self.assertRaises(recovery.CaptureRepairError) as error:
                self.engine._restore_access_mode({"root": recovery.EXTERNAL, "internal": False})
        self.assertEqual(error.exception.action["reason"], "restore-app-storage-unavailable")
        self.assertEqual(error.exception.action["totalAttempts"], 3)
        self.assertNotIn("unlock", str(error.exception))


if __name__ == "__main__": unittest.main()
