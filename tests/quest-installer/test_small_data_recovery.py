"""Save-only controls: no game-bank or old-APK byte reads/copies are allowed."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
from unittest import mock

import test_signing_recovery as legacy
Device, recovery = legacy.Device, legacy.recovery

SPEC = importlib.util.spec_from_file_location("quest_native_rename_controls", Path(recovery.__file__).with_name("native_rename.py"))
native = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(native)


class NativeDevice(Device):
    def __init__(self, root):
        super().__init__(root)
        self.rename_errno = 0
        self.forbid_bulk = True

    def __call__(self, command, **options):
        action = command[3:] if command[1:2] == ["-s"] else command[1:]
        if action[:1] == ["push"]:
            self.calls.append(tuple(action))
            local, remote = Path(action[1]), str(action[2])
            assert local.read_bytes() == native.executable()
            assert remote.startswith("/data/local/tmp/.ghvr-rename-")
            target = self.path(remote); target.parent.mkdir(parents=True, exist_ok=True)
            # The exact generated ARM64 program is independently run under QEMU.
            # This portable fixture exercises the same host OS rename syscall on
            # real trees, without trying to execute ARM64 on the x64 test host.
            target.write_text("#!/usr/bin/env python3\nimport os,sys\n"
                              + ("sys.exit(" + str(self.rename_errno) + ")\n" if self.rename_errno else
                                 "try:\n os.rename(sys.argv[1],sys.argv[2])\nexcept OSError as e:\n sys.exit(e.errno)\n"))
            return subprocess.CompletedProcess(command, 0, "1 file pushed", "")
        script = options.get("input", b"").decode("utf-8", errors="replace")
        if self.forbid_bulk:
            assert not (action[:2] == ["exec-out", "cat"] and self.apk in action), "Original APK must not be transferred"
            assert not ("sha256sum " + self.apk in script), "Original APK must not be hashed"
            assert not ("StreamingAssets/" in script and ("sha256sum" in script or "cp -p" in script)), "Game payload must not be copied or hashed"
        return super().__call__(command, **options)


class SmallDataTests(legacy.TransactionTests):
    # Reuse the real filesystem fixture, not its legacy full-policy test methods.
    __unittest_skip__ = False

    def setUp(self):
        super().setUp()
        shutil.rmtree(self.device.root)
        self.device = NativeDevice(self.root / "device")
        self.adb.runner = self.device
        self.source.apk = self.device.new_apk
        self.source.sha256 = recovery.file_hash(self.device.new_apk)

    def engine(self, *, data_policy="saves", allow_content_reinstall=None):
        value = super().engine(data_policy=data_policy)
        value.allow_content_reinstall = allow_content_reinstall
        return value

    def test_small_data_preserves_every_save_and_setting_without_old_apk_copy(self):
        bank, payload = self.bank()
        (bank / "local-options.json").write_bytes(b"small setting")
        engine = self.engine()
        with mock.patch.object(recovery, "file_hash", wraps=recovery.file_hash) as hashing:
            self.assertTrue(engine.recover(detected=True)["verified"])
            self.assertNotIn(self.source.apk, [call.args[0] for call in hashing.call_args_list])
        self.verify_saves()
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.assertEqual((bank / "local-options.json").read_bytes(), b"small setting")
        self.assertEqual(engine.journal["parkingMode"], "atomic")
        self.assertFalse((engine.backup / "original.apk").exists())
        self.assertLess(sum(path.stat().st_size for path in engine.backup.glob("*.tar")), 128 * 1024)
        self.assertFalse(list(self.device.path("/data/local/tmp").glob(".ghvr-rename-*")))

    def test_small_data_ignores_fuse_inode_equality_and_never_uses_copy_fallback(self):
        bank, payload = self.bank(); self.device.copied_probe = True
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        self.assertEqual(engine.journal["parkingMode"], "atomic")
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.assertNotIn("cp -p", self.log.read_text())
        self.verify_saves()

    def test_small_data_missing_or_broken_bank_receipt_never_captures_payload(self):
        for invalid in (None, b"broken receipt"):
            with self.subTest(receipt=invalid):
                self.setUp()
                bank, payload = self.bank()
                receipt = bank / ".quest-installation.receipt"
                if invalid is None: receipt.unlink()
                else: receipt.write_bytes(invalid)
                engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
                external = next(row for row in engine.journal["roots"] if row["name"] == "external")
                self.assertFalse(any("StreamingAssets" in name for name in external["inventory"]))
                self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
                self.verify_saves()

    def test_small_data_native_exdev_never_copies_bank_and_defers_required_content_install(self):
        bank, _ = self.bank(); self.device.rename_errno = 18
        engine = self.engine()
        with self.assertRaisesRegex(recovery.RecoveryError, "Confirm content reinstallation"): engine.recover(detected=True)
        engine.allow_content_reinstall = engine.journal["transaction"]
        self.assertTrue(engine.recover()["verified"])
        self.assertEqual(engine.journal["parkingMode"], "reinstall")
        self.assertFalse(bank.exists()); self.verify_saves()
        self.assertNotIn("cp -p", self.log.read_text())
        self.assertIn("normal content installation", self.output.getvalue())

    def test_small_data_unexecutable_helper_uses_content_install_without_hidden_copy(self):
        self.bank(); self.device.rename_errno = 126
        engine = self.engine()
        with self.assertRaisesRegex(recovery.RecoveryError, "Confirm content reinstallation"): engine.recover(detected=True)
        engine.allow_content_reinstall = engine.journal["transaction"]
        self.assertTrue(engine.recover()["verified"])
        self.assertEqual(engine.journal["parkingMode"], "reinstall")
        self.verify_saves()

    def test_small_data_unavailable_rename_pauses_before_uninstall_without_consent(self):
        bank, payload = self.bank(); self.device.rename_errno = 18
        engine = self.engine()
        with self.assertRaisesRegex(recovery.RecoveryError, "Confirm content reinstallation"):
            engine.recover(detected=True)
        self.assertEqual(engine.journal["state"], "backed-up")
        self.assertTrue(self.device.installed)
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.verify_saves()
        self.assertIn('"kind": "signing-content-reinstall"', self.output.getvalue())
        self.assertTrue(self.engine(allow_content_reinstall=engine.journal["transaction"]).recover()["verified"])
        self.verify_saves()

    def test_small_data_failed_replacement_keeps_saves_and_retries_same_apk(self):
        bank, payload = self.bank(); self.device.fail_new_install = True
        engine = self.engine()
        with self.assertRaisesRegex(recovery.RecoveryError, "Retry installation without rebuilding"):
            engine.recover(detected=True)
        self.assertEqual(engine.journal["state"], "installing")
        self.assertFalse((engine.backup / "original.apk").exists())
        self.assertFalse(self.device.installed)
        self.assertTrue(all((engine.backup / row["archive"]).is_file() for row in engine.journal["roots"]))
        self.device.fail_new_install = False
        self.assertTrue(self.engine().recover()["verified"])
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.verify_saves()

    def test_small_data_resume_old_partial_fuse_copy_does_not_finish_copy_or_hash_apk(self):
        bank, payload = self.bank(); self.device.forbid_bulk = False
        legacy = super().engine(data_policy="all"); legacy._backup = lambda: None
        with self.assertRaises(recovery.RecoveryError): legacy.recover(detected=True)
        legacy._backup = recovery.Recovery._backup.__get__(legacy)
        legacy._backup()
        legacy.commit("parking", parkingMode="copy", dataPolicy="saves")
        parked = self.device.path(legacy.park + "/quest-owned-game"); parked.mkdir(parents=True)
        (parked / "partial-copy.bin").write_bytes(b"partial generated bytes")
        original_bytes = (legacy.backup / "original.apk").read_bytes()
        self.device.forbid_bulk = True
        resumed = self.engine(); self.assertTrue(resumed.recover()["verified"])
        self.assertEqual(resumed.journal["transaction"], legacy.journal["transaction"])
        self.assertTrue(resumed.journal["smallDataOnly"])
        self.assertEqual((legacy.backup / "original.apk").read_bytes(), original_bytes)
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        self.verify_saves()

    def test_small_data_reinstall_consent_is_bound_to_exact_transaction(self):
        bank, payload = self.bank(); self.device.rename_errno = 18
        engine = self.engine(allow_content_reinstall="f" * 32)
        with self.assertRaisesRegex(recovery.RecoveryError, "Confirm content reinstallation"):
            engine.recover(detected=True)
        self.assertTrue(self.device.installed)
        self.assertEqual((bank / "StreamingAssets/game.bundle").read_bytes(), payload)
        actions = [json.loads(line.split(" ", 1)[1]) for line in self.output.getvalue().splitlines() if line.startswith("GHVRQ_ACTION ")]
        self.assertEqual(actions[-1]["transaction"], engine.journal["transaction"])
        self.assertEqual(actions[-1]["deviceSerial"], "QUEST-LOCAL")

    def test_small_data_prunes_only_generated_runtime_prefixes_and_retains_other_private_settings(self):
        directory = self.device.path(self.device.private) / "quest-procedural-state"
        for name in ("proton-prefix", "wine-prefix", "cache", "tmp", "home"):
            (directory / name).mkdir(parents=True, exist_ok=True)
            (directory / name / "generated.bin").write_bytes(b"regenerable")
        (directory / "procedural-worker.log").write_bytes(b"small diagnostic")
        (directory / "user-options.json").write_bytes(b"unknown settings")
        engine = self.engine(); self.assertTrue(engine.recover(detected=True)["verified"])
        private = engine.journal["roots"][0]
        self.assertNotIn("quest-procedural-state/proton-prefix/generated.bin", private["inventory"])
        self.assertFalse((directory / "proton-prefix").exists())
        self.assertEqual((directory / "user-options.json").read_bytes(), b"unknown settings")
        self.assertEqual((directory / "procedural-worker.log").read_bytes(), b"small diagnostic")
        self.verify_saves()

    def test_small_data_legacy_upgrade_rejects_changed_original_before_touching_partial_bank(self):
        bank, _ = self.bank(); self.device.forbid_bulk = False
        legacy_engine = super().engine(data_policy="all")
        original = legacy_engine.commit
        def stop(state=None, **fields):
            original(state, **fields)
            if state == "backed-up": raise KeyboardInterrupt()
        legacy_engine.commit = stop
        with self.assertRaises(KeyboardInterrupt): legacy_engine.recover(detected=True)
        legacy_engine.commit = original
        legacy_engine.commit("parking", parkingMode="copy", dataPolicy="saves")
        parked = self.device.path(legacy_engine.park + "/quest-owned-game"); parked.mkdir(parents=True)
        (parked / "partial.bin").write_bytes(b"retained partial")
        current = self.device.path(self.device.apk).read_bytes()
        self.device.path(self.device.apk).write_bytes(current[:-1] + bytes([current[-1] ^ 1]))
        self.device.forbid_bulk = True
        with self.assertRaisesRegex(recovery.RecoveryError, "installed APK changed"):
            self.engine().recover()
        self.assertTrue(self.device.installed)
        self.assertEqual((parked / "partial.bin").read_bytes(), b"retained partial")
        self.assertTrue(bank.is_dir())

    def test_small_data_legacy_preflight_before_original_identity_can_resume_without_resetting_source(self):
        self.device.forbid_bulk = False
        engine = super().engine(data_policy="all")
        self.device.deny_run_as = True
        with self.assertRaisesRegex(recovery.RecoveryError, "not debuggable"):
            engine.recover(detected=True)
        self.assertNotIn("originalApk", engine.journal)
        self.device.deny_run_as = False; self.device.forbid_bulk = True
        resumed = self.engine(); self.assertTrue(resumed.recover()["verified"])
        self.assertEqual(resumed.journal["transaction"], engine.journal["transaction"])
        self.verify_saves()


# Avoid inheriting legacy tests: this class deliberately changes the requested
# promise. The existing TransactionTests continue to certify explicit full mode.
for _name in tuple(name for name in dir(legacy.TransactionTests) if name.startswith("test_")):
    if _name not in SmallDataTests.__dict__: setattr(SmallDataTests, _name, None)


class NativeImageTests(unittest.TestCase):
    def test_owned_native_image_is_small_static_arm64_without_imports(self):
        import struct
        value = native.executable()
        self.assertEqual(len(value), 176)
        self.assertEqual(value[:8], b"\x7fELF\x02\x01\x01\x00")
        self.assertEqual(struct.unpack_from("<HH", value, 16), (2, 183))
        self.assertEqual(struct.unpack_from("<I", value, 64), (1,))
        self.assertEqual(struct.unpack_from("<Q", value, 112), (16384,))
