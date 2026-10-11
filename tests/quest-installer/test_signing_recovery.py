"""Real tar/filesystem transaction controls; no real device or package removal."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import struct
import subprocess
import sys
import tarfile
import tempfile
import types
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_signing_recovery_tests", ROOT / "tools/quest-installer/signing_recovery.py")
recovery = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(recovery)
INSTALLER = importlib.util.spec_from_file_location("quest_recovery_installer_tests", ROOT / "tools/quest-installer/installer.py")
installer = importlib.util.module_from_spec(INSTALLER); sys.modules[INSTALLER.name] = installer; INSTALLER.loader.exec_module(installer)


def manifest(*, debug=True, package=recovery.PACKAGE, long=False, utf8=True, resource=0x0101000f):
    values = ["manifest", "application", "package", "debuggable", package, "http://schemas.android.com/apk/res/android"]
    if long: values.append("x" * 300)
    offsets = []; pool = bytearray()
    def length(value):
        if utf8: return bytes([value]) if value < 128 else bytes([(value >> 8) | 128, value & 255])
        return struct.pack("<H", value)
    for text in values:
        offsets.append(len(pool)); raw = text.encode("utf-8" if utf8 else "utf-16le")
        pool += length(len(text)) + (length(len(raw)) if utf8 else b"") + raw + (b"\0" if utf8 else b"\0\0")
    pool += b"\0" * (-len(pool) % 4)
    strings = struct.pack("<HH6I", 1, 28, 28 + len(values) * 4 + len(pool), len(values), 0, 256 if utf8 else 0, 28 + len(values) * 4, 0)
    strings += struct.pack("<" + "I" * len(values), *offsets) + pool
    def tag(name, attributes):
        return struct.pack("<HHIII", 0x102, 16, 36 + len(attributes) * 20, 1, 0xffffffff) + struct.pack("<II6H", 0xffffffff, name, 20, 20, len(attributes), 0, 0, 0) + b"".join(attributes)
    package_attr = struct.pack("<IIIHBBI", 0xffffffff, 2, 4, 8, 0, 3, 4)
    debug_attr = struct.pack("<IIIHBBI", 5, 3, 0xffffffff, 8, 0, 0x12, 0xffffffff if debug else 0)
    resource_map = struct.pack("<HHI4I", 0x180, 8, 24, 0, 0, 0, resource)
    body = strings + resource_map + tag(0, [package_attr]) + tag(1, [debug_attr])
    return struct.pack("<HHI", 3, 8, len(body) + 8) + body


def apk(path, *, debug=True, content=b"new"):
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("AndroidManifest.xml", manifest(debug=debug))
        archive.writestr("assets/test", content)


class Device:
    """Run real shell/tar commands inside a temporary virtual Android tree."""
    def __init__(self, root):
        self.root = root; self.calls = []; self.installed = True
        self.user = "0"; self.others = False; self.deny_run_as = False
        self.hardware = "QUEST-LOCAL"
        self.shell_unreadable = set(); self.app_unreadable = set()
        self.tar_modes = []; self.fail_external_backup = False
        self.fail_new_install = False; self.fail_restore = False; self.copied_probe = False
        self.interrupt = None; self.apk = "/data/app/~~local/quest-test/base.apk"
        self.private = "/data/user/0/" + recovery.PACKAGE
        for value in (self.private, recovery.EXTERNAL, "/data/user_de/0", "/sdcard/Download", "/data/app/~~local/quest-test"):
            self.path(value).mkdir(parents=True, exist_ok=True)
        apk(self.path(self.apk), content=b"old")
        (self.path(self.private) / "no_backup").mkdir()
        (self.path(self.private) / "no_backup" / "native.bin").write_bytes(b"internal\x00save")
        (self.path(self.private) / "wine").mkdir()
        (self.path(self.private) / "wine" / "z:").symlink_to("/")
        (self.path(recovery.EXTERNAL) / "files/GloomSaves/Campaign").mkdir(parents=True)
        (self.path(recovery.EXTERNAL) / "files/GloomSaves/Campaign" / "party_76561198000000001.save").write_bytes(b"campaign\0original")
        (self.path(recovery.EXTERNAL) / "lib").mkdir()
        (self.path(recovery.EXTERNAL) / "lib" / "future-setting.json").write_bytes(b"unknown mutable")
        self.new_apk = root.parent / "new.apk"; apk(self.new_apk)
        self.old_bytes = self.path(self.apk).read_bytes()

    def path(self, value): return self.root / value.lstrip("/")

    def translate(self, text):
        return re.sub(r"(?<![A-Za-z0-9_./-])/(sdcard|data)(?=/|\s|[\"':]|$)", lambda match: str(self.root / match[1]), text)

    def __call__(self, command, **options):
        action = command[3:] if command[1:2] == ["-s"] else command[1:]
        self.calls.append(tuple(action))
        def result(text="", code=0): return subprocess.CompletedProcess(command, code, text, "")
        if action[:2] == ["shell", "getprop"]:
            return result(self.hardware if action[2] == "ro.serialno" else "Quest 3")
        if action == ["shell", "am", "get-current-user"]: return result(self.user)
        if action == ["shell", "pm", "list", "users"]: return result("Users:\n UserInfo{0:Primary:13}" + ("\n UserInfo{10:Other:10}" if self.others else ""))
        if action[:4] == ["shell", "pm", "list", "packages"]: return result("package:" + recovery.PACKAGE if self.others else "")
        if action[:3] == ["shell", "am", "force-stop"]: return result()
        if action[:3] == ["shell", "pm", "path"]: return result("package:" + self.apk if self.installed else "")
        app_context = False
        if action[:2] == ["shell", "run-as"]:
            app_context = True
            if self.deny_run_as: return result("run-as: package not debuggable", 1)
            if action[3:] == ["pwd"]: return result(self.private)
            action = action[3:]
        elif action[:2] == ["shell", "-T"]:
            action = action[2:]
            if action[:1] == ["run-as"]:
                app_context = True
                if self.deny_run_as: return result("run-as: package not debuggable", 1)
                action = action[2:]
        elif action[:1] == ["exec-out"]: action = action[1:]
        elif action[:1] == ["pull"]:
            source, destination = self.path(action[1]), Path(action[2])
            if not source.is_file(): return result("missing", 1)
            shutil.copyfile(source, destination); return result("1 file pulled")
        elif action[:1] == ["uninstall"]:
            if action != ["uninstall", recovery.PACKAGE]: raise AssertionError("global or foreign uninstall")
            for root in (self.private, recovery.EXTERNAL, "/data/user_de/0/" + recovery.PACKAGE):
                shutil.rmtree(self.path(root), ignore_errors=True)
            self.installed = False; return result("Success")
        elif action[:1] == ["install"]:
            origin = Path(action[-1])
            if self.fail_new_install and origin == self.new_apk: return result("Failure [INSTALL_FAILED_INVALID_APK]", 1)
            self.path(self.apk).write_bytes(origin.read_bytes()); self.installed = True
            self.path(self.private).mkdir(parents=True, exist_ok=True)
            self.path(recovery.EXTERNAL).mkdir(parents=True, exist_ok=True)
            return result("Success")
        else: raise AssertionError("Unexpected ADB command " + repr(action))
        if self.fail_restore and action[:1] == ["tar"] and "-xof" in action:
            options["stderr"].write(b"restore denied"); return result("", 1)
        if action[:1] == ["tar"]:
            self.tar_modes.append(("app" if app_context else "shell", tuple(action)))
            if self.fail_external_backup and recovery.EXTERNAL in action and "-cf" in action:
                options["stdout"].write(b"partial failed archive"); options["stderr"].write(b"tar: permission denied"); return result("", 1)
        translated = [self.translate(value) for value in action]
        options = dict(options)
        if "input" in options:
            script = options["input"].decode("utf-8")
            for path in (self.app_unreadable if app_context else self.shell_unreadable):
                script = script.replace("head -c 1 " + shlex.quote(path) + " >/dev/null 2>&1", "false")
            script = self.translate(script)
            if self.copied_probe and "printf atomic" in script: script = script.replace("then printf atomic", "then printf copied")
            options["input"] = script.encode("utf-8")
        ran = subprocess.run(translated, **options)
        if isinstance(ran.stdout, str):
            ran.stdout = ran.stdout.replace(str(self.root / "sdcard"), "/sdcard").replace(str(self.root / "data"), "/data")
        elif isinstance(ran.stdout, bytes):
            ran.stdout = ran.stdout.replace(str(self.root / "sdcard").encode(), b"/sdcard").replace(str(self.root / "data").encode(), b"/data")
        return ran


class TransactionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.device = Device(self.root / "device")
        self.log = self.root / "adb.log"; self.log.touch()
        self.adb = installer.Adb("adb", self.log, self.device)
        self.source = types.SimpleNamespace(apk=self.device.new_apk, sha256=recovery.file_hash(self.device.new_apk), input_key="a" * 64)
        self.output = io.StringIO(); self.output_context = contextlib.redirect_stdout(self.output); self.output_context.__enter__(); self.addCleanup(self.output_context.__exit__, None, None, None)

    def engine(self): return recovery.Recovery(self.adb, "192.0.2.10:5555", "QUEST-LOCAL", self.source, self.root / "workspace", decode_receipt=installer.decode_content_receipt)

    def verify_saves(self):
        self.assertEqual((self.device.path(self.device.private) / "no_backup/native.bin").read_bytes(), b"internal\x00save")
        self.assertEqual((self.device.path(recovery.EXTERNAL) / "files/GloomSaves/Campaign/party_76561198000000001.save").read_bytes(), b"campaign\0original")
        self.assertEqual((self.device.path(recovery.EXTERNAL) / "lib/future-setting.json").read_bytes(), b"unknown mutable")
        self.assertEqual(os.readlink(self.device.path(self.device.private) / "wine/z:"), "/")

    def test_actual_private_external_saves_links_and_unknown_files_restored(self):
        engine = self.engine(); value = engine.recover(detected=True)
        self.assertTrue(value["verified"]); self.assertTrue(engine.path.is_file())
        self.verify_saves(); engine.complete()
        state = json.loads(engine.path.read_text()); self.assertEqual(state["state"], "complete")
        self.assertEqual((engine.backup / "original.apk").read_bytes(), self.device.old_bytes)
        self.assertEqual(sum(call[0] == "uninstall" for call in self.device.calls), 1)
        self.assertNotIn("kill-server", str(self.device.calls)); self.assertNotIn("chmod", str(self.device.calls))

    def test_non_debuggable_replacement_never_removes_existing_app(self):
        apk(self.device.new_apk, debug=False); self.source.sha256 = recovery.file_hash(self.device.new_apk)
        with self.assertRaisesRegex(recovery.RecoveryError, "restoration support"): self.engine().recover(detected=True)
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_inaccessible_run_as_never_removes_existing_app(self):
        self.device.deny_run_as = True
        with self.assertRaisesRegex(recovery.RecoveryError, "run-as"): self.engine().recover(detected=True)
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_secondary_and_other_installed_android_profiles_retained(self):
        for attr, value in (("user", "10"), ("others", True)):
            with self.subTest(attr=attr):
                setattr(self.device, attr, value)
                with self.assertRaisesRegex(recovery.RecoveryError, "profile|primary Quest"): self.engine().recover(detected=True)
                self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))
                setattr(self.device, attr, "0" if attr == "user" else False)

    def test_disk_space_refusal_precedes_package_removal(self):
        with mock.patch.object(recovery.shutil, "disk_usage", return_value=types.SimpleNamespace(free=0)):
            with self.assertRaisesRegex(recovery.RecoveryError, "Insufficient PC storage"): self.engine().recover(detected=True)
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_interruption_after_removal_resumes_restoration_without_old_data_loss(self):
        engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "removed": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        self.assertFalse(self.device.installed)
        resumed = self.engine(); self.assertTrue(resumed.recover()["verified"]); self.verify_saves()
        self.assertEqual(sum(call[0] == "uninstall" for call in self.device.calls), 1)

    def test_changed_save_after_cancel_is_refreshed_before_removal(self):
        engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "uninstalling": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        save = self.device.path(recovery.EXTERNAL) / "files/GloomSaves/Campaign/party_76561198000000001.save"
        save.write_bytes(b"new progress after cancelled backup")
        self.assertTrue(self.engine().recover()["verified"])
        self.assertEqual(save.read_bytes(), b"new progress after cancelled backup")

    def test_changed_installed_apk_after_cancel_is_not_uninstalled(self):
        engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "uninstalling": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        apk(self.device.path(self.device.apk), content=b"another manually installed APK")
        with self.assertRaisesRegex(recovery.RecoveryError, "APK changed"): self.engine().recover()
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_reassigned_wifi_address_never_uninstalls_another_headset(self):
        engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "uninstalling": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        self.device.hardware = "OTHER-QUEST"
        with self.assertRaisesRegex(recovery.RecoveryError, "different headset"): self.engine().recover()
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_selected_local_apk_change_after_backup_preserves_old_app(self):
        engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "uninstalling": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        apk(self.device.new_apk, content=b"new file replaced during a long backup")
        with self.assertRaisesRegex(recovery.RecoveryError, "selected APK changed"): self.engine().recover()
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_reassigned_wifi_address_before_parking_does_not_move_foreign_banks(self):
        bank, payload = self.bank(); engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "backed-up": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        self.device.hardware = "OTHER-QUEST"
        with self.assertRaisesRegex(recovery.RecoveryError, "different headset"): self.engine().recover()
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_pending_restore_refuses_manual_unrelated_apk(self):
        self.device.fail_restore = True
        with self.assertRaises(recovery.RecoveryError): self.engine().recover(detected=True)
        self.device.fail_restore = False
        apk(self.device.path(self.device.apk), content=b"manual unrelated replacement")
        with self.assertRaisesRegex(recovery.RecoveryError, "changed before data restoration"): self.engine().recover()

    def test_persisted_bank_witness_cannot_escape_the_recovery_tree(self):
        self.bank(); engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "backed-up": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        state = json.loads(engine.path.read_text()); name = state["banks"][0]
        state["bankWitnesses"][name] = {"StreamingAssets/../../foreign": {"path": "StreamingAssets/../../foreign", "size": 1, "sha256": "a" * 64}}
        recovery.atomic_json(engine.path, state)
        with self.assertRaisesRegex(recovery.RecoveryError, "Invalid parked-bank file identity"): self.engine().recover()
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_failed_new_install_restores_exact_old_apk_and_data(self):
        self.device.fail_new_install = True
        with self.assertRaisesRegex(recovery.RecoveryError, "INVALID_APK"): self.engine().recover(detected=True)
        self.verify_saves(); self.assertTrue(self.device.installed)
        self.assertEqual(self.device.path(self.device.apk).read_bytes(), self.device.old_bytes)
        self.assertEqual(self.engine().recover()["verified"] if not self.device.fail_new_install else json.loads(self.engine().path.read_text())["state"], "backed-up")

    def test_restore_failure_retains_backup_and_resumes_without_fresh_uninstall(self):
        self.device.fail_restore = True; engine = self.engine()
        with self.assertRaisesRegex(recovery.RecoveryError, "restore denied"): engine.recover(detected=True)
        self.assertTrue((engine.backup / "original.apk").is_file())
        self.assertEqual(json.loads(engine.path.read_text())["state"], "restoring")
        self.device.fail_restore = False; self.assertTrue(self.engine().recover()["verified"]); self.verify_saves()
        self.assertEqual(sum(call[0] == "uninstall" for call in self.device.calls), 1)

    def test_backup_corruption_before_uninstall_preserves_current_data(self):
        engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "backed-up": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        item = engine.journal["roots"][0]
        (engine.backup / item["archive"]).write_bytes(b"damaged retained archive")
        with self.assertRaisesRegex(recovery.RecoveryError, "backup is damaged"): self.engine().recover()
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_original_apk_backup_corruption_before_uninstall_preserves_app(self):
        engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "backed-up": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        (engine.backup / "original.apk").write_bytes(b"damaged APK")
        with self.assertRaisesRegex(recovery.RecoveryError, "APK backup is damaged"): self.engine().recover()
        self.verify_saves(); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_partial_restore_existing_terminal_link_can_be_replayed_safely(self):
        engine = self.engine(); real_restore = engine._restore
        def interrupted():
            real_restore(); raise KeyboardInterrupt()
        engine._restore = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        self.verify_saves()
        self.assertTrue(self.engine().recover()["verified"]); self.verify_saves()
        self.assertEqual(sum(call[0] == "uninstall" for call in self.device.calls), 1)

    def test_changed_symlink_or_file_at_original_link_path_never_overwrites_foreign_content(self):
        engine = self.engine(); real_restore = engine._restore
        def interrupted():
            real_restore(); raise KeyboardInterrupt()
        engine._restore = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        link = self.device.path(self.device.private) / "wine/z:"
        link.unlink(); link.write_bytes(b"new data in unexpected regular file")
        with self.assertRaisesRegex(recovery.RecoveryError, "ADB failed"): self.engine().recover()
        self.assertEqual(link.read_bytes(), b"new data in unexpected regular file")

    def test_new_save_changes_before_verify_do_not_produce_success(self):
        engine = self.engine(); real_transfer = engine._transfer
        def changed(args, local, **options):
            if str(local).endswith("external.restored.tar"):
                save = self.device.path(recovery.EXTERNAL) / "files/GloomSaves/Campaign/party_76561198000000001.save"
                save.write_bytes(b"wrong restored save")
            return real_transfer(args, local, **options)
        engine._transfer = changed
        with self.assertRaisesRegex(recovery.RecoveryError, "failed verification"): engine.recover(detected=True)
        self.assertEqual(engine.journal["state"], "restoring")
        self.assertTrue((engine.backup / "original.apk").is_file())

    def test_bounded_binary_transfers_never_use_text_mode_or_in_memory_input(self):
        real_runner = self.device
        observations = []
        def checked(command, **options):
            if "capture_output" not in options:
                observations.append(options)
                self.assertFalse(options.get("text", False)); self.assertNotIn("input", options)
                self.assertFalse(options["shell"])
                self.assertTrue(hasattr(options["stderr"], "write"))
            if options.get("input") is not None:
                self.assertIsInstance(options["input"], bytes); self.assertNotIn(b"\r\n", options["input"])
            return real_runner(command, **options)
        self.adb.runner = checked
        self.assertTrue(self.engine().recover(detected=True)["verified"])
        self.assertGreaterEqual(len(observations), 6)

    def test_completed_recovery_retry_does_not_rehash_old_backups(self):
        engine = self.engine(); engine.recover(detected=True)
        with mock.patch.object(recovery, "file_hash", side_effect=AssertionError("completed proof must be retained")):
            self.assertTrue(self.engine().recover()["verified"])

    def test_hard_link_backup_target_does_not_overwrite_foreign_file(self):
        engine = self.engine(); engine.journal = {"banks": []}
        foreign = self.root / "foreign.bin"; foreign.write_bytes(b"must be retained")
        alias = self.root / "alias.tar"; os.link(foreign, alias)
        with self.assertRaisesRegex(recovery.RecoveryError, "independent file"):
            engine._transfer(["exec-out", "cat", self.device.apk], alias)
        self.assertEqual(foreign.read_bytes(), b"must be retained")

    def test_pending_recovery_requires_exact_selected_apk_and_headset(self):
        engine = self.engine(); engine._backup = mock.Mock(side_effect=RuntimeError("interrupted"))
        with self.assertRaises(recovery.RecoveryError): engine.recover(detected=True)
        self.source.sha256 = "b" * 64
        with self.assertRaisesRegex(recovery.RecoveryError, "original selected APK"): self.engine().recover()
        self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def bank(self):
        name = recovery.BANKS[0]; root = self.device.path(recovery.EXTERNAL + "/" + name); root.mkdir(parents=True)
        payload = b"owned large content represented without large test allocation"
        (root / "StreamingAssets").mkdir()
        (root / "StreamingAssets/game.bundle").write_bytes(payload)
        rows = [{"path": "StreamingAssets/game.bundle", "size": len(payload), "sha256": hashlib.sha256(payload).hexdigest()}]
        manifest = {"archive": "quest-startup-content.zip", "files": rows}
        (root / ".quest-installation.receipt").write_bytes(installer.encode_content_receipt(manifest, {row["path"]: row for row in rows}))
        return root, payload

    def test_atomic_bank_roundtrip_avoids_host_bank_copy(self):
        bank, payload = self.bank(); engine = self.engine()
        self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.assertEqual(engine.journal["parkingMode"], "atomic")
        external = next(row for row in engine.journal["roots"] if row["name"] == "external")
        self.assertNotIn("files/quest-owned-game/StreamingAssets/game.bundle", external["inventory"])
        self.verify_saves()

    def test_fuse_copy_fallback_preserves_exact_old_bank_for_rollback(self):
        bank, payload = self.bank(); self.device.copied_probe = True; engine = self.engine()
        self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual(engine.journal["parkingMode"], "copy")
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.assertEqual(self.device.path(engine.park + "/quest-owned-game/StreamingAssets/game.bundle").read_bytes(), payload)
        engine.complete()
        self.assertFalse(self.device.path(engine.park).exists())
        self.assertTrue((engine.backup / "original.apk").is_file())

    def test_receipt_file_symlink_makes_whole_bank_mutable(self):
        bank, payload = self.bank()
        foreign = self.device.path(recovery.EXTERNAL) / "foreign-game.bundle"; foreign.write_bytes(payload)
        bundle = bank / "StreamingAssets/game.bundle"; bundle.unlink(); bundle.symlink_to("../../foreign-game.bundle")
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual(engine.journal["banks"], [])
        self.assertEqual(os.readlink(bundle), "../../foreign-game.bundle")

    def test_receipt_file_beneath_directory_link_makes_whole_bank_mutable(self):
        bank, payload = self.bank(); directory = bank / "StreamingAssets"
        directory.rename(bank / "original-files"); directory.symlink_to("original-files")
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual(engine.journal["banks"], []); self.assertEqual(os.readlink(directory), "original-files")

    def test_cancelled_atomic_park_resumes_without_double_move_or_data_loss(self):
        bank, payload = self.bank(); engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "parked": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        self.assertFalse(bank.exists()); self.assertTrue(self.engine().recover()["verified"])
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload); self.verify_saves()

    def test_cancelled_copy_park_resumes_preserving_old_source_and_bank(self):
        bank, payload = self.bank(); self.device.copied_probe = True; engine = self.engine(); original = engine.commit
        def interrupted(state=None, **fields):
            original(state, **fields)
            if state == "parked": raise KeyboardInterrupt()
        engine.commit = interrupted
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.assertTrue(self.engine().recover()["verified"]); self.verify_saves()

    def test_unknown_bank_entries_are_preserved_as_mutable_data(self):
        bank, payload = self.bank(); (bank / "unknown-player-data.dat").write_bytes(b"unknown save")
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual(engine.journal["banks"], [recovery.BANKS[0]])
        external = next(row for row in engine.journal["roots"] if row["name"] == "external")
        self.assertEqual(external["inventory"]["files/quest-owned-game/unknown-player-data.dat"]["sha256"], hashlib.sha256(b"unknown save").hexdigest())
        self.assertNotIn("files/quest-owned-game/StreamingAssets/game.bundle", external["inventory"])
        self.assertEqual((bank / "unknown-player-data.dat").read_bytes(), b"unknown save")

    def test_external_read_denials_automatically_use_app_identity_without_dropping_tombstones(self):
        self.bank()
        for number in range(3):
            path = recovery.EXTERNAL + "/files/tombstone_0" + str(number)
            self.device.path(path).write_bytes(b"native crash data " + bytes([number]))
            self.device.shell_unreadable.add(path)
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        external = next(row for row in engine.journal["roots"] if row["name"] == "external")
        self.assertEqual(external["accessMode"], "app")
        for number in range(3):
            name = "files/tombstone_0" + str(number)
            self.assertIn(name, external["inventory"])
            self.assertEqual((self.device.path(recovery.EXTERNAL) / name).read_bytes(), b"native crash data " + bytes([number]))
        external_transfers = [(mode, args) for mode, args in self.device.tar_modes if recovery.EXTERNAL in args]
        self.assertTrue(external_transfers); self.assertTrue(all(mode == "app" for mode, _ in external_transfers))
        self.assertNotIn("files/quest-owned-game/StreamingAssets/game.bundle", external["inventory"])
        self.verify_saves()

    def test_unreadable_arbitrary_save_uses_app_identity_and_never_a_filename_exemption(self):
        path = recovery.EXTERNAL + "/files/GloomSaves/Campaign/party_76561198000000001.save"
        self.device.shell_unreadable.add(path)
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual(next(row for row in engine.journal["roots"] if row["name"] == "external")["accessMode"], "app")
        self.verify_saves()

    def test_both_identities_denied_refuses_before_payload_backup_or_uninstall(self):
        path = recovery.EXTERNAL + "/files/tombstone_00"; self.device.path(path).write_bytes(b"must preserve")
        self.device.shell_unreadable.add(path); self.device.app_unreadable.add(path)
        with self.assertRaisesRegex(recovery.RecoveryError, "access repair exhausted"): self.engine().recover(detected=True)
        self.assertFalse(self.device.tar_modes)
        self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls)); self.verify_saves()
        self.assertEqual(self.device.path(path).read_bytes(), b"must preserve")

    def partial_external_failure(self):
        protected = self.device.path("/data/user_de/0/" + recovery.PACKAGE)
        protected.mkdir(); (protected / "device-settings.bin").write_bytes(b"protected settings")
        self.device.fail_external_backup = True; engine = self.engine()
        with self.assertRaisesRegex(recovery.RecoveryError, "permission denied"): engine.recover(detected=True)
        self.device.fail_external_backup = False
        self.assertEqual(engine.journal["state"], "created")
        prior = {item["name"]: item["archive"] for item in engine.journal["roots"] if "archive" in item}
        self.assertEqual(set(prior), {"private", "device-protected"})
        return engine, prior

    def test_partial_resume_reuses_original_apk_and_verified_ce_de_without_reexport(self):
        engine, prior = self.partial_external_failure()
        self.assertFalse(list(engine.backup.glob("external-*.tar")))
        resumed = self.engine(); transfer = resumed._transfer; copied = []
        def observed(args, local, **kwargs):
            copied.append(tuple(args)); return transfer(args, local, **kwargs)
        resumed._transfer = observed; commit = resumed.commit
        def stop_at_prepared(state=None, **fields):
            commit(state, **fields)
            if state == "backed-up": raise KeyboardInterrupt()
        resumed.commit = stop_at_prepared
        with self.assertRaises(KeyboardInterrupt): resumed.recover()
        retained = {item["name"]: item["archive"] for item in resumed.journal["roots"] if "archive" in item}
        self.assertTrue(all(retained[name] == archive for name, archive in prior.items()))
        self.assertFalse(any(args[:1] == ("exec-out",) for args in copied))
        self.assertFalse(any(self.device.private in args or "/data/user_de/0/" + recovery.PACKAGE in args for args in copied))
        self.assertTrue(self.engine().recover()["verified"]); self.verify_saves()

    def test_failed_new_preflight_keeps_all_prior_verified_archive_references(self):
        engine, prior = self.partial_external_failure()
        path = recovery.EXTERNAL + "/files/lib-unreadable.bin"; self.device.path(path).write_bytes(b"opaque app data")
        self.device.shell_unreadable.add(path); self.device.app_unreadable.add(path)
        with self.assertRaisesRegex(recovery.RecoveryError, "access repair exhausted"): self.engine().recover()
        journal = json.loads(engine.path.read_text())
        retained = {item["name"]: item.get("archive") for item in journal["roots"]}
        self.assertTrue(all(retained[name] == archive for name, archive in prior.items()))
        self.assertTrue(all((engine.backup / archive).is_file() for archive in prior.values()))
        self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_failed_unreferenced_legacy_archive_is_pruned_before_free_space_gate(self):
        engine, prior = self.partial_external_failure()
        legacy = engine.backup / ("external-" + "b" * 32 + ".tar"); legacy.write_bytes(b"failed legacy large archive")
        unrelated = engine.backup / "user-notes.txt"; unrelated.write_text("keep")
        old_snapshot = engine.backup / ("private-" + "c" * 32 + ".tar"); old_snapshot.write_bytes(b"possibly unique previous snapshot")
        foreign = self.root / "foreign.tar"; foreign.write_bytes(b"foreign")
        linked = engine.backup / ("external-" + "d" * 32 + ".tar"); linked.symlink_to(foreign)
        def space(_): return types.SimpleNamespace(free=0 if legacy.exists() else 10**12)
        with mock.patch.object(recovery.shutil, "disk_usage", side_effect=space):
            self.assertTrue(self.engine().recover()["verified"])
        self.assertFalse(legacy.exists()); self.assertTrue(old_snapshot.is_file()); self.assertTrue(linked.is_symlink())
        self.assertEqual(foreign.read_bytes(), b"foreign"); self.assertEqual(unrelated.read_text(), "keep")
        self.assertTrue(all((engine.backup / archive).is_file() for archive in prior.values()))

    def test_receipt_owned_bank_with_fourteen_unknown_native_extras_is_small_mutable_subset(self):
        for copied in (False, True):
            with self.subTest(copied=copied):
                # Separate complete transactions and device trees for each mode.
                self.device.copied_probe = copied
                bank, payload = self.bank()
                extras = bank / "StreamingAssets/aa/Android"; extras.mkdir(parents=True)
                for number in range(14): (extras / ("old-" + str(number) + "_startup.bundle")).write_bytes(b"old native asset " + bytes([number]))
                engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
                external = next(row for row in engine.journal["roots"] if row["name"] == "external")
                captured = [name for name in external["inventory"] if "_startup.bundle" in name]
                self.assertEqual(len(captured), 14); self.assertNotIn("files/quest-owned-game/StreamingAssets/game.bundle", external["inventory"])
                self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
                self.assertTrue(all((extras / ("old-" + str(number) + "_startup.bundle")).read_bytes() == b"old native asset " + bytes([number]) for number in range(14)))
                engine.complete(); shutil.rmtree(bank)

    def test_cancelled_atomic_bank_with_unknown_extras_refreshes_changes_without_losing_entries(self):
        bank, _ = self.bank(); (bank / "unknown-save.bin").write_bytes(b"before cancel")
        engine = self.engine(); commit = engine.commit
        def cancelled(state=None, **fields):
            commit(state, **fields)
            if state == "uninstalling": raise KeyboardInterrupt()
        engine.commit = cancelled
        with self.assertRaises(KeyboardInterrupt): engine.recover(detected=True)
        parked = self.device.path(engine.park + "/quest-owned-game")
        (parked / "unknown-save.bin").write_bytes(b"changed after cancelled preparation")
        (parked / "new-player-save.bin").write_bytes(b"new save")
        resumed = self.engine(); self.assertTrue(resumed.recover()["verified"])
        self.assertEqual((bank / "unknown-save.bin").read_bytes(), b"changed after cancelled preparation")
        self.assertEqual((bank / "new-player-save.bin").read_bytes(), b"new save")
        external = next(row for row in resumed.journal["roots"] if row["name"] == "external")
        self.assertIn("files/quest-owned-game/new-player-save.bin", external["inventory"])

    def test_added_member_between_snapshot_and_verification_cannot_be_silently_omitted(self):
        engine = self.engine(); transfer = engine._transfer; added = False
        def changing(args, local, **kwargs):
            nonlocal added
            value = transfer(args, local, **kwargs)
            if not added and self.device.private in args and "-cf" in args:
                (self.device.path(self.device.private) / "new-unsaved-data.bin").write_bytes(b"must not lose")
                added = True
            return value
        engine._transfer = changing
        with self.assertRaisesRegex(recovery.RecoveryError, "App data changed during its backup"): engine.recover(detected=True)
        self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))
        self.assertEqual((self.device.path(self.device.private) / "new-unsaved-data.bin").read_bytes(), b"must not lose")

    def test_explicit_lf_tar_lists_preserve_spaces_quotes_wildcards_unicode_and_option_names(self):
        names = [" leading and trailing ", "quotes'and\"double", "[unknown]*?.save", "--files-from", "Spielstand-Ä.save"]
        for name in names: (self.device.path(recovery.EXTERNAL) / name).write_bytes(name.encode())
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        for name in names: self.assertEqual((self.device.path(recovery.EXTERNAL) / name).read_bytes(), name.encode())
        self.assertTrue(all("--no-recursion" in args and "-T" in args for _, args in self.device.tar_modes if "-cf" in args))

    def test_newline_filename_is_refused_before_any_binary_transfer(self):
        (self.device.path(recovery.EXTERNAL) / "unsafe\nname.save").write_bytes(b"preserve")
        with self.assertRaisesRegex(recovery.RecoveryError, "unrepresentable|unsafe filename"): self.engine().recover(detected=True)
        self.assertFalse(self.device.tar_modes); self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_backup_work_has_one_finite_monotonic_total_and_actual_member_progress(self):
        self.assertTrue(self.engine().recover(detected=True)["verified"])
        events = [json.loads(line[len("GHVRQ_PROGRESS "):]) for line in self.output.getvalue().splitlines() if line.startswith("GHVRQ_PROGRESS ")]
        work = [row for row in events if row["phase"] == "install-recovery-backup-work"]
        self.assertGreater(len(work), 4); self.assertEqual(len({row["total"] for row in work}), 1)
        self.assertEqual([row["done"] for row in work], sorted(row["done"] for row in work))
        self.assertEqual(work[-1]["done"], work[-1]["total"])
        self.assertTrue(any(row["phase"] == "install-recovery-access" and row["total"] > 0 for row in events))

    def test_private_alias_excludes_only_proven_android_generated_library_link(self):
        original = self.device.private; alias = "/data/data/" + recovery.PACKAGE
        self.device.path(alias).parent.mkdir(parents=True)
        self.device.path(original).rename(self.device.path(alias)); self.device.private = alias
        native = str(Path(self.device.apk).parent) + "/lib/arm64"
        (self.device.path(alias) / "lib").symlink_to(native)
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        private = next(row for row in engine.journal["roots"] if row["name"] == "private")
        self.assertNotIn("lib", private["inventory"]); self.verify_saves()

    def test_new_generated_private_directories_are_allowed_but_other_restore_additions_refused(self):
        engine = self.engine(); transfer = engine._transfer
        def changing(args, local, **kwargs):
            value = transfer(args, local, **kwargs)
            if kwargs.get("restore") and self.device.private in args:
                (self.device.path(self.device.private) / "files").mkdir(exist_ok=True)
                (self.device.path(self.device.private) / "unexpected-player-data.bin").write_bytes(b"retain for inspection")
            return value
        engine._transfer = changing
        with self.assertRaisesRegex(recovery.RecoveryError, "unexpected new member"): engine.recover(detected=True)
        self.assertEqual(engine.journal["state"], "restoring")
        self.assertEqual((self.device.path(self.device.private) / "unexpected-player-data.bin").read_bytes(), b"retain for inspection")
        self.assertTrue((engine.backup / "original.apk").is_file())

    def test_partial_journal_with_changed_original_apk_never_adopts_or_prunes_it(self):
        engine, prior = self.partial_external_failure()
        leftover = engine.backup / ("external-" + "e" * 32 + ".tar"); leftover.write_bytes(b"retain uncertain partial")
        apk(self.device.path(self.device.apk), content=b"manual replacement after partial backup")
        with self.assertRaisesRegex(recovery.RecoveryError, "installed APK changed since partial"): self.engine().recover()
        self.assertTrue(leftover.is_file()); self.assertTrue(all((engine.backup / name).is_file() for name in prior.values()))
        self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls))

    def test_typed_census_rejects_false_row_index_or_stat_type_before_any_transfer(self):
        runner = self.device
        def corrupt(command, **options):
            result = runner(command, **options)
            if options.get("input") and b"metadata=$(stat" in options["input"] and isinstance(result.stdout, bytes):
                result.stdout = result.stdout.replace(b"0\t", b"999\t", 1)
            return result
        self.adb.runner = corrupt
        with self.assertRaisesRegex(recovery.RecoveryError, "Invalid app-data access metadata"): self.engine().recover(detected=True)
        self.assertFalse(self.device.tar_modes)

    def test_postparking_mutation_is_preserved_and_banks_restored_before_retry(self):
        bank, _ = self.bank(); engine = self.engine(); park = engine._park_banks
        save = self.device.path(recovery.EXTERNAL) / "files/GloomSaves/Campaign/party_76561198000000001.save"
        def changing():
            park(); save.write_bytes(b"new save while content parking completed")
        engine._park_banks = changing
        with self.assertRaisesRegex(recovery.RecoveryError, "App data changed during content parking"): engine.recover(detected=True)
        self.assertTrue(bank.is_dir()); self.assertTrue(self.device.installed)
        self.assertEqual(save.read_bytes(), b"new save while content parking completed")
        self.assertEqual(engine.journal["state"], "backed-up")
        self.assertTrue(self.engine().recover()["verified"])
        self.assertEqual(save.read_bytes(), b"new save while content parking completed")

    def test_selected_source_changed_during_fresh_parking_retains_app_and_banks(self):
        bank, payload = self.bank(); self.device.copied_probe = True
        engine = self.engine(); park = engine._park_banks
        def changing():
            park(); apk(self.device.new_apk, content=b"source changed while content was parked")
        engine._park_banks = changing
        with self.assertRaisesRegex(recovery.RecoveryError, "selected APK changed during content parking"): engine.recover(detected=True)
        self.assertTrue(self.device.installed); self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.assertFalse(any(call[0] == "uninstall" for call in self.device.calls)); self.verify_saves()

    def test_completed_copy_cleanup_never_touches_reassigned_headset(self):
        self.bank(); self.device.copied_probe = True; engine = self.engine()
        self.assertTrue(engine.recover(detected=True)["verified"])
        retained = self.device.path(engine.park + "/quest-owned-game")
        self.device.hardware = "OTHER-QUEST"; engine.complete()
        self.assertTrue(retained.is_dir()); self.assertEqual(engine.journal["state"], "complete")


class ArchiveAndManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup); self.root = Path(self.temp.name)

    def test_compiled_utf8_utf16_and_long_string_pool_prove_debug_mode(self):
        for utf8 in (True, False):
            for long in (True, False):
                path = self.root / "player.apk"
                with zipfile.ZipFile(path, "w") as archive: archive.writestr("AndroidManifest.xml", manifest(utf8=utf8, long=long))
                self.assertTrue(recovery.replacement_is_debuggable(path))

    def test_non_debug_foreign_package_and_corrupt_manifest_refused(self):
        path = self.root / "player.apk"
        for raw in (manifest(debug=False), manifest(package="foreign.package"), manifest(resource=0x01010010), b"not a compiled manifest", manifest()[:-1]):
            with zipfile.ZipFile(path, "w") as archive: archive.writestr("AndroidManifest.xml", raw)
            self.assertFalse(recovery.replacement_is_debuggable(path))

    def test_archive_traversal_specials_and_members_under_link_refused(self):
        controls = [("../escape", tarfile.REGTYPE, b"bad", ""), ("/absolute", tarfile.REGTYPE, b"bad", ""),
                    ("fifo", tarfile.FIFOTYPE, b"", ""), ("hard", tarfile.LNKTYPE, b"", "unknown")]
        for name, kind, payload, link in controls:
            path = self.root / "unsafe.tar"
            with tarfile.open(path, "w") as archive:
                info = tarfile.TarInfo(name); info.type = kind; info.linkname = link; info.size = len(payload); archive.addfile(info, io.BytesIO(payload) if payload else None)
            with self.assertRaises(recovery.RecoveryError): recovery.archive_inventory(path)
        path = self.root / "unsafe.tar"
        with tarfile.open(path, "w") as archive:
            link = tarfile.TarInfo("link"); link.type = tarfile.SYMTYPE; link.linkname = "/"; archive.addfile(link)
            info = tarfile.TarInfo("link/escape"); info.size = 4; archive.addfile(info, io.BytesIO(b"evil"))
        with self.assertRaisesRegex(recovery.RecoveryError, "beneath a symbolic link"): recovery.archive_inventory(path)


if __name__ == "__main__": unittest.main()
