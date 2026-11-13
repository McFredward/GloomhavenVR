"""Actual shell open/chmod controls for the bounded app-access seam.

The mapped namespace runs ordinary local processes, not Android FUSE. Device
identity and mount discovery have separate production Recovery controls.
"""
import hashlib
import importlib.util
from pathlib import Path, PurePosixPath as PurePath
import shlex
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_content_access_controls", ROOT / "tools/quest-installer/content_access.py")
access = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(access)


class ConnectionRequired(RuntimeError):
    pass


class ShellView:
    def __init__(self, device, address, hardware):
        self.device = device
        self.closed = False

    def _device_guard(self):
        if self.device.foreign: raise RuntimeError("foreign headset")

    def _access_root(self, root, mode):
        self._device_guard()
        return root

    def _context_shell(self, script, mode):
        if self.device.disconnect: raise ConnectionRequired("device offline")
        if self.device.direct_open_denied and mode == "app-direct" and "head -c 1" in script:
            raise RuntimeError("direct namespace open denied")
        if self.device.process_open_denied and mode == "app-process" and "head -c 1" in script:
            raise RuntimeError("proc pathname namespace open denied")
        return self.device.execute(script, app=True)

    def shell(self, script, timeout):
        return self.device.execute(script, app=False)

    def _close_storage_process(self):
        self.closed = True

    def __exit__(self, kind, error, traceback):
        self._close_storage_process()

    def _check_external_root(self, root, mode):
        parts = PurePath(root)
        self.device.execute("\n".join("test ! -L " + shlex.quote(str(path))
                            for path in (parts, *parts.parents) if str(path) not in ("/sdcard", "/")), app=mode != "shell")


class Device:
    def __init__(self, local):
        self.local = local
        self.scripts = []
        self.foreign = self.disconnect = self.deny_after_repair = False
        self.direct_open_denied = False
        self.process_open_denied = False
        self.waits = 0

    def identity(self, address):
        return "fixture-headset"

    def wait_for_connection(self, address):
        self.waits += 1
        self.disconnect = False

    def execute(self, script, *, app):
        self.scripts.append((app, script))
        if app and self.deny_after_repair and "head -c 1" in script: raise RuntimeError("app denied")
        translated = script.replace(access.BASE.rstrip("/"), str(self.local))
        result = subprocess.run(["sh", "-s"], input="set -eu\n" + translated,
                                capture_output=True, text=True, timeout=20)
        if result.returncode: raise RuntimeError("shell check failed: " + result.stderr[:1000])
        return result.stdout.strip()


class ContentAccessTests(unittest.TestCase):
    def test_runtime_deferred_check_retains_exact_root_receipt_and_headset_guards(self):
        class RuntimeDevice:
            hardware = "fixture-headset"
            def identity(self, address): return self.hardware
        device = RuntimeDevice()
        def forbidden_view(*args, **kwargs):
            raise AssertionError("normal installation must not start an app-context probe")
        with access.RuntimeBankAccess(device, "quest", forbidden_view) as owner:
            result = owner.qualify(access.BASE + "quest-owned-game", {"files": [{}]}, ".quest-installation.receipt")
            self.assertIs(result["appAccessVerified"], False)
            for root, receipt in ((access.BASE + "quest-saves", ".quest-installation.receipt"),
                                  (access.BASE + "quest-owned-game", "../save")):
                with self.assertRaisesRegex(RuntimeError, "unexpected delivery boundary"):
                    owner.qualify(root, {"files": [{}]}, receipt)
            device.hardware = "another-headset"
            with self.assertRaisesRegex(RuntimeError, "headset changed"):
                owner.qualify(access.BASE + "quest-owned-game", {"files": [{}]}, ".quest-installation.receipt")

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.local = Path(self.temporary.name)
        self.bank = self.local / "quest-mod-resources"
        self.assets = self.bank / "StreamingAssets"
        self.assets.mkdir(parents=True)
        self.remote = access.BASE + "quest-mod-resources"
        self.rows = []
        for index in range(7):
            path = self.assets / (str(index) + ".bundle")
            path.write_bytes(bytes([index]) * 8192)
            self.rows.append({"path": "StreamingAssets/" + path.name, "size": path.stat().st_size,
                              "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        (self.bank / ".quest-installation.receipt").write_bytes(b"compact completed receipt")
        self.device = Device(self.local)

    def qualify(self):
        with access.BankAccess(self.device, "quest", ShellView) as owner:
            result = owner.qualify(self.remote, {"files": self.rows}, ".quest-installation.receipt")
        self.assertTrue(owner.view.closed)
        return result

    def test_warm_uses_only_receipt_and_three_one_byte_opens(self):
        # A sparse 1-GB payload still receives one byte-open, never a full read.
        with (self.assets / "0.bundle").open("r+b") as stream: stream.truncate(1024 ** 3)
        self.assertEqual(self.qualify()["appAccess"], "verified")
        scripts = "\n".join(script for _, script in self.device.scripts)
        self.assertEqual(scripts.count("head -c 1 "), 4)
        self.assertFalse(any(not app for app, _ in self.device.scripts))
        for forbidden in ("sha256sum", "find ", "cp ", "tar ", "cat ", "du "):
            self.assertNotIn(forbidden, scripts)

    def test_restrictive_existing_directory_repairs_modes_without_changing_bytes(self):
        before = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in self.assets.iterdir()}
        unknown = self.local / "quest-saves"
        unknown.mkdir(); save = unknown / "party.save"; save.write_bytes(b"private save"); save.chmod(0o600)
        self.assets.chmod(0)
        try:
            result = self.qualify()
        finally:
            self.assets.chmod(0o755)
        self.assertEqual(result, {"appAccess": "repaired", "accessRepairFiles": 7})
        self.assertEqual(before, {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in self.assets.iterdir()})
        self.assertEqual(save.read_bytes(), b"private save")
        self.assertEqual(save.stat().st_mode & 0o777, 0o600)
        self.assertFalse(any("quest-saves" in script or "chmod -R" in script for _, script in self.device.scripts))

    def test_denied_sample_file_repairs_exact_files_and_leaves_unknown_sibling(self):
        target = self.assets / "3.bundle"; target.chmod(0)
        unknown = self.assets / "unknown.txt"; unknown.write_bytes(b"unknown"); unknown.chmod(0o600)
        try: self.assertEqual(self.qualify()["appAccess"], "repaired")
        finally: target.chmod(0o644)
        self.assertEqual(unknown.stat().st_mode & 0o777, 0o600)

    def test_unexpected_root_never_starts_a_remote_operation(self):
        with access.BankAccess(self.device, "quest", ShellView) as owner:
            with self.assertRaisesRegex(RuntimeError, "unexpected bank"):
                owner.qualify(access.BASE + "quest-saves", {"files": self.rows}, ".quest-installation.receipt")
        self.assertEqual(self.device.scripts, [])

    def test_symlink_bank_refuses_repair_without_touching_target(self):
        outside = self.local / "outside"; outside.mkdir(); outside.chmod(0o700)
        self.assets.rename(self.bank / "old-assets")
        self.assets.symlink_to(outside, target_is_directory=True)
        with self.assertRaises(RuntimeError): self.qualify()
        self.assertEqual(outside.stat().st_mode & 0o777, 0o700)

    def test_foreign_headset_never_receives_probe_or_mutation(self):
        self.device.foreign = True
        with self.assertRaisesRegex(RuntimeError, "foreign headset"): self.qualify()
        self.assertEqual(self.device.scripts, [])

    def test_disconnect_is_not_reinterpreted_as_a_permission_failure(self):
        self.device.disconnect = True
        with self.assertRaises(ConnectionRequired): self.qualify()
        self.assertFalse(any(not app for app, _ in self.device.scripts))

    def test_disconnect_waits_then_resumes_same_app_access_without_chmod(self):
        self.device.disconnect = self.device.wait_enabled = True
        self.assertEqual(self.qualify()["appAccess"], "verified")
        self.assertEqual(self.device.waits, 1)
        self.assertFalse(any(not app for app, _ in self.device.scripts))

    def test_process_namespace_open_can_succeed_without_permission_mutation(self):
        self.device.direct_open_denied = True
        self.assertEqual(self.qualify()["appAccess"], "verified")
        self.assertFalse(any(not app for app, _ in self.device.scripts))

    def test_actual_app_origin_open_can_succeed_after_both_aliases_fail(self):
        self.device.direct_open_denied = self.device.process_open_denied = True
        self.assertEqual(self.qualify()["appAccess"], "verified")
        self.assertFalse(any(not app for app, _ in self.device.scripts))

    def test_unavailable_alias_roots_select_app_origin_without_chmod(self):
        class OriginOnlyView(ShellView):
            def _access_root(self, root, mode):
                if mode in ("app-direct", "app-process"): raise RuntimeError("scoped alias denied")
                if mode != "app-jdwp": raise AssertionError("unexpected storage view")
                return super()._access_root(root, mode)
        with access.BankAccess(self.device, "quest", OriginOnlyView) as owner:
            result = owner.qualify(self.remote, {"files": self.rows}, ".quest-installation.receipt")
            self.assertEqual(owner.mode, "app-jdwp")
        self.assertEqual(result["appAccess"], "verified")
        self.assertFalse(any(not app for app, _ in self.device.scripts))

    def test_process_namespace_is_rechecked_after_permission_repair(self):
        self.device.direct_open_denied = True
        target = self.assets / "3.bundle"; target.chmod(0)
        try: self.assertEqual(self.qualify()["appAccess"], "repaired")
        finally: target.chmod(0o644)
        self.assertTrue(any("chmod " in script for _, script in self.device.scripts))

    def test_linked_files_parent_never_changes_outside_modes(self):
        alias = self.local / "linked-files"
        alias.symlink_to(self.local, target_is_directory=True)
        self.device.local = alias
        before = self.assets.stat().st_mode
        with self.assertRaises(RuntimeError): self.qualify()
        self.assertEqual(self.assets.stat().st_mode, before)
        self.assertFalse(any("chmod " in script for _, script in self.device.scripts))

    def test_unfixed_access_is_reported_without_large_copy_fallback(self):
        self.device.deny_after_repair = True
        with self.assertRaisesRegex(RuntimeError, "still denies.*no full content copy"):
            self.qualify()
        self.assertFalse(any("cp " in script or "sha256sum" in script for _, script in self.device.scripts))


if __name__ == "__main__": unittest.main()
