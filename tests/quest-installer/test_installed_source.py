"""Installed-source protocol tests against the real installer/APK metadata code.

The runner models Android's read-only responses and bounded binary dd payloads.
It is not a headset, Android signature or Unity compiler acceptance test.
"""
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import struct
import subprocess
import sys
import tempfile
import threading
import types
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_installed_source_tests_subject", ROOT / "tools/quest-installer/installed_source.py")
subject = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(subject)
installer = subject._installer()
PACKAGE = installer.PACKAGE
REMOTE = "/data/app/~~generation/" + PACKAGE + "-random/base.apk"
INPUT = "a" * 64


def player(build=717, key=INPUT, malformed=False):
    if malformed: return b"not an Android ZIP"
    elf = bytearray(64)
    elf[:6] = b"\x7fELF\x02\x01"
    struct.pack_into("<H", elf, 18, 183)
    manifest = {"schema": 1, "target": "game", "inputKey": key,
                "mod": {"modBuild": build}, "profile": {"displayName": "Fixture Player"}}
    target = io.BytesIO()
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_STORED) as archive:
        archive.writestr("lib/arm64-v8a/libil2cpp.so", elf)
        archive.writestr("assets/bin/Data/Managed/Metadata/global-metadata.dat", struct.pack("<II", 0xFAB11BAF, 27))
        archive.writestr("assets/Quest/input-manifest.json", json.dumps(manifest))
        archive.writestr("assets/Quest/installation-manifest.json", json.dumps({"schema": 1, "inputKey": key}))
        archive.writestr("assets/bin/Data/opaque-bank", b"x" * 24576)
    return target.getvalue()


class Android:
    """Strict command fixture; no launch/debugger/uninstall/storage census allowed."""
    def __init__(self, body=None):
        self.body = player() if body is None else body
        self.calls, self.transfers = [], []
        self.hardware = "QUEST-HARDWARE-1"
        self.path = REMOTE
        self.version = 717
        self.key = INPUT
        self.modified, self.inode, self.mode = 100, 200, stat.S_IFREG | 0o644
        self.paths = None
        self.fail_transfer = None
        self.after_transfer = None
        self.unauthorized = False
        self.stat_override = None

    def __call__(self, command, **options):
        assert options["shell"] is False
        assert options["timeout"] <= 120
        self.calls.append(command)
        assert command[:2] == ["fixture-adb", "-s"], command
        assert command[2] in ("USB-QUEST", "192.168.1.42:5555"), command
        args = command[3:]
        raw, error, code = "", "", 0
        if self.unauthorized:
            return subprocess.CompletedProcess(command, 1, "", "error: device unauthorized")
        if args[:2] == ["shell", "getprop"]:
            raw = {"ro.product.model": "Quest 3", "ro.product.name": "eureka", "ro.serialno": self.hardware}[args[2]]
        elif args == ["shell", "pm", "path", "--user", "0", PACKAGE]:
            raw = self.paths if self.paths is not None else "package:" + self.path
        elif args == ["shell", "dumpsys", "package", PACKAGE]:
            raw = "versionCode=" + str(self.version) + " minSdk=29\nversionName=0.1.0.B" + str(self.version) + "." + self.key[:12]
        elif args == ["shell", "stat", "-c", "%s:%Y:%i:%f", self.path]:
            raw = self.stat_override or str(len(self.body)) + ":" + str(self.modified) + ":" + str(self.inode) + ":" + format(self.mode, "x")
        elif args[:3] == ["exec-out", "sh", "-c"]:
            self.assert_binary(command, options)
            words = args[3].split()
            assert words[:2] == ["exec", "dd"] and words[-1] == "2>/dev/null"
            values = dict(value.split("=", 1) for value in words[2:-1])
            # Android may keep an old installed file readable until the final
            # reader closes it; the closing Package Manager witness must still
            # reject publication of that departed generation.
            assert values["if"] in (REMOTE, self.path)
            block, skip, count = (int(values[name]) for name in ("bs", "skip", "count"))
            assert 0 < count * block <= subject.CHUNK_BYTES
            start = skip * block
            self.transfers.append((start, min(count * block, len(self.body) - start)))
            if self.fail_transfer:
                failure = self.fail_transfer(len(self.transfers))
                if isinstance(failure, BaseException): raise failure
                if failure == "short":
                    return subprocess.CompletedProcess(command, 0, self.body[start:start + count * block - 1], b"")
                if failure == "exit":
                    return subprocess.CompletedProcess(command, 1, b"", b"dd: read failed")
            raw = self.body[start:start + count * block]
            if self.after_transfer: self.after_transfer(len(self.transfers))
            return subprocess.CompletedProcess(command, 0, raw, b"1+0 records in\n1+0 records out\n")
        else:
            raise AssertionError("Unexpected device access: " + repr(args))
        return subprocess.CompletedProcess(command, code, raw, error)

    @staticmethod
    def assert_binary(command, options):
        assert options["capture_output"] is True
        assert options["text"] is False
        assert len(command) == 7


class InstalledSourceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="quest-installed-source-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.android = Android()
        self.adb = installer.Adb("fixture-adb", self.root / "adb.log", self.android)
        self.addCleanup(mock.patch.stopall)
        mock.patch.dict(os.environ, {"GHVRQ_ADB_LIFETIME": ""}).start()
        mock.patch.object(subject, "CHUNK_BYTES", 4096).start()
        mock.patch.object(subject, "BLOCK_BYTES", 1024).start()

    def acquire(self, **options):
        return subject.acquire_installed(self.adb, "USB-QUEST", self.root / "state", **options)

    def expect_reason(self, reason, action):
        with self.assertRaises(subject.InstalledSourceError) as caught: action()
        self.assertEqual(reason, caught.exception.reason)

    def test_discovery_reads_only_identity_package_and_stat(self):
        result = subject.inspect_installed(self.adb, "USB-QUEST")
        self.assertTrue(result["available"])
        self.assertEqual("quest", result["source"])
        self.assertEqual(717, result["modBuild"])
        self.assertEqual(INPUT[:12], result["inputKeyPrefix"])
        self.assertEqual(64, len(result["witnessKey"]))
        self.assertEqual([], self.android.transfers)
        self.assertNotIn("apk", result)
        self.assertNotIn("seed", result)

    def test_acquire_uses_actual_metadata_and_no_external_banks(self):
        events = []
        result = self.acquire(progress=events.append)
        self.assertEqual(self.android.body, Path(result["apk"]).read_bytes())
        self.assertEqual(INPUT, result["identity"]["inputKey"])
        self.assertEqual(717, result["base"]["modBuild"])
        self.assertEqual(str(Path(result["apk"]).absolute()), result["base"]["resolvedApk"])
        self.assertFalse(result["reused"])
        self.assertFalse(result["seed"]["available"])
        self.assertEqual(len(self.android.body), events[-1]["done"])
        self.assertEqual(events[-1]["done"], events[-1]["total"])
        self.assertTrue(all(row["done"] < row["total"] for row in events[:-1]))
        self.assertTrue(all("/sdcard" not in " ".join(call) and "run-as" not in call for call in self.android.calls))

    def test_closed_import_reuses_exact_local_and_device_witness(self):
        first = self.acquire()
        transferred = len(self.android.transfers)
        with mock.patch.object(subject.hashlib, "sha256", wraps=subject.hashlib.sha256) as hashed:
            second = self.acquire()
        self.assertTrue(second["reused"])
        self.assertEqual(first["apk"], second["apk"])
        self.assertEqual(transferred, len(self.android.transfers))
        # Hash arguments are tiny witness JSON, never the APK or game bank.
        self.assertTrue(all(len(call.args[0]) < 4096 for call in hashed.call_args_list))

    def test_cancel_retains_committed_chunks_and_resumes_from_offset(self):
        cancel = threading.Event()
        def progress(row):
            if row["done"] >= subject.CHUNK_BYTES: cancel.set()
        self.expect_reason("cancelled", lambda: self.acquire(cancel=cancel, progress=progress))
        self.assertEqual([(0, subject.CHUNK_BYTES)], self.android.transfers)
        cancel.clear()
        result = self.acquire(cancel=cancel)
        self.assertEqual(subject.CHUNK_BYTES, result["resumedBytes"])
        self.assertEqual(subject.CHUNK_BYTES, self.android.transfers[1][0])
        self.assertEqual(self.android.body, Path(result["apk"]).read_bytes())

    def test_cancel_before_operation_does_not_probe_device(self):
        event = threading.Event(); event.set()
        self.expect_reason("cancelled", lambda: self.acquire(cancel=event))
        self.assertEqual([], self.android.calls)

    def test_short_chunk_and_timeout_resume_only_unfinished_bytes(self):
        for failure in ("short", subprocess.TimeoutExpired("fixture-adb", 120), "exit"):
            with self.subTest(failure=failure):
                self.android.transfers.clear()
                self.android.fail_transfer = lambda index: failure if index == 2 else None
                root = self.root / ("case" + str(len(list(self.root.glob("case*")))))
                self.expect_reason("transfer-interrupted", lambda: subject.acquire_installed(self.adb, "USB-QUEST", root))
                self.android.fail_transfer = None
                result = subject.acquire_installed(self.adb, "USB-QUEST", root)
                self.assertEqual(subject.CHUNK_BYTES, result["resumedBytes"])
                self.assertEqual(subject.CHUNK_BYTES, self.android.transfers[2][0])

    def test_swapped_headset_is_rejected_before_any_transfer(self):
        self.expect_reason("different-headset", lambda: self.acquire(hardware="ANOTHER-QUEST"))
        self.assertEqual([], self.android.transfers)

    def test_swapped_headset_during_transfer_never_publishes(self):
        self.android.after_transfer = lambda _: setattr(self.android, "hardware", "ANOTHER-QUEST")
        self.expect_reason("different-headset", self.acquire)
        self.assertEqual([], list((self.root / "state").rglob("base.apk")))
        self.assertEqual([], list((self.root / "state").rglob("source.json")))

    def test_changed_package_stat_during_transfer_never_publishes(self):
        self.android.after_transfer = lambda _: setattr(self.android, "modified", 101)
        self.expect_reason("package-changed", self.acquire)
        self.assertEqual([], list((self.root / "state").rglob("base.apk")))

    def test_changed_package_path_during_transfer_never_publishes(self):
        self.android.after_transfer = lambda _: setattr(self.android, "path", REMOTE.replace("random", "replacement"))
        self.expect_reason("package-changed", self.acquire)
        self.assertEqual([], list((self.root / "state").rglob("base.apk")))

    def test_version_metadata_mismatch_cannot_be_used(self):
        self.android.body = player(build=716)
        self.expect_reason("package-identity-mismatch", self.acquire)
        self.assertEqual([], list((self.root / "state").rglob("source.json")))

    def test_input_key_prefix_mismatch_cannot_be_used(self):
        self.android.body = player(key="b" * 64)
        self.expect_reason("package-identity-mismatch", self.acquire)

    def test_unreadable_native_apk_is_actionable_not_published(self):
        self.android.body = player(malformed=True)
        self.expect_reason("invalid-update-source", self.acquire)
        self.assertEqual([], list((self.root / "state").rglob("source.json")))

    def test_split_package_is_rejected_without_reading_any_apk(self):
        self.android.paths = "package:" + REMOTE + "\npackage:" + REMOTE.replace("base.apk", "split_config.apk")
        self.expect_reason("split-package", self.acquire)
        self.assertEqual([], self.android.transfers)

    def test_unsafe_paths_are_rejected_without_shell_expansion(self):
        for path in (REMOTE.replace("random", "../random"), REMOTE + ";touch /sdcard/owned",
                     REMOTE.replace("base.apk", "base.apk\n"), REMOTE.replace(PACKAGE, "other.package"),
                     REMOTE.replace("/data/app/", "/data/app//")):
            with self.subTest(path=path):
                self.android.paths = "package:" + path
                if path.endswith("\n"): continue  # pm's trailing newline is ordinary framing.
                self.expect_reason("unsafe-package-path", self.acquire)
        self.assertEqual([], self.android.transfers)

    def test_symlink_or_excessive_remote_file_is_rejected(self):
        self.android.mode = stat.S_IFLNK | 0o777
        self.expect_reason("invalid-package-file", self.acquire)
        self.android.mode = stat.S_IFREG | 0o644
        self.android.stat_override = str(subject.MAX_APK_BYTES + 1) + ":100:200:81a4"
        self.expect_reason("invalid-package-file", self.acquire)
        self.assertEqual([], self.android.transfers)

    def test_missing_package_returns_available_false_and_action(self):
        self.android.paths = ""
        result = subject.inspect_installed(self.adb, "USB-QUEST")
        self.assertFalse(result["available"])
        self.assertEqual("package-missing", result["reason"])
        self.expect_reason("package-missing", self.acquire)
        self.assertEqual([], self.android.transfers)

    def test_unauthorized_device_uses_real_installer_action(self):
        self.android.unauthorized = True
        with self.assertRaises(installer.ConnectionRequired): self.acquire()
        self.assertEqual([], self.android.transfers)

    def test_disk_space_is_checked_before_transfer(self):
        with mock.patch.object(subject.shutil, "disk_usage", return_value=types.SimpleNamespace(free=0)):
            self.expect_reason("insufficient-space", self.acquire)
        self.assertEqual([], self.android.transfers)

    def test_changed_local_import_is_replaced_not_reused(self):
        first = self.acquire()
        path = Path(first["apk"])
        previous = len(self.android.transfers)
        path.write_bytes(b"z" * path.stat().st_size)
        result = self.acquire()
        self.assertFalse(result["reused"])
        self.assertGreater(len(self.android.transfers), previous)
        self.assertEqual(self.android.body, path.read_bytes())

    def test_local_mutation_during_metadata_inspection_is_not_published(self):
        builder = installer.builder_module()
        original = builder.apk_updates.inspect_base
        def mutate(path):
            result = original(path)
            with Path(path).open("ab") as stream: stream.write(b"changed-during-inspection")
            return result
        with mock.patch.object(installer, "builder_module", return_value=builder), mock.patch.object(builder.apk_updates, "inspect_base", side_effect=mutate):
            self.expect_reason("local-source-changed", self.acquire)
        self.assertEqual([], list((self.root / "state").rglob("source.json")))

    @unittest.skipIf(sys.platform == "win32", "Requires a symlink-capable test account")
    def test_linked_cache_cannot_overwrite_another_directory(self):
        target = self.root / "other"; target.mkdir()
        state = self.root / "state"; state.mkdir()
        (state / "build").symlink_to(target, target_is_directory=True)
        self.expect_reason("unsafe-local-path", self.acquire)
        self.assertEqual([], list(target.iterdir()))
        self.assertEqual([], self.android.transfers)

    def test_transport_aliases_remain_owned_by_installer(self):
        self.adb._transport_aliases["USB-QUEST"] = "192.168.1.42:5555"
        result = self.acquire()
        self.assertEqual(self.android.hardware, result["deviceSerial"])
        self.assertTrue(all(call[2] == "192.168.1.42:5555" for call in self.android.calls))

    def test_actual_eight_megabyte_transfer_bound(self):
        with mock.patch.object(subject, "CHUNK_BYTES", 8 * 1024 * 1024), mock.patch.object(subject, "BLOCK_BYTES", 1024 * 1024):
            target = io.BytesIO()
            with zipfile.ZipFile(io.BytesIO(player())) as original, zipfile.ZipFile(target, "w", compression=zipfile.ZIP_STORED) as out:
                for name in original.namelist(): out.writestr(name, original.read(name))
                out.writestr("assets/bin/Data/opaque-extra", b"x" * (9 * 1024 * 1024))
            self.android.body = target.getvalue()
            result = self.acquire()
            self.assertEqual(2, len(self.android.transfers))
            self.assertEqual(8 * 1024 * 1024, self.android.transfers[0][1])
            self.assertEqual(self.android.body, Path(result["apk"]).read_bytes())

    @unittest.skipIf(sys.platform == "win32", "Requires the host POSIX shell for this raw-channel witness")
    def test_raw_exec_channel_suppresses_dd_footer_before_binary_transfer(self):
        local = self.root / "native-input.apk"
        local.write_bytes(self.android.body)
        commands = []
        def raw_exec(command, **options):
            commands.append(command)
            script = command[-1].replace(REMOTE, str(local))
            # Android's raw exec channel merges process stderr into stdout.
            # Model that boundary with an actual host dd and merged pipes.
            return subprocess.run(["sh", "-c", script], capture_output=False, text=False,
                                  stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  timeout=options["timeout"], shell=False)
        self.adb.runner = raw_exec
        actual = subject._chunk(self.adb, "USB-QUEST", REMOTE, 0, subject.CHUNK_BYTES)
        self.assertEqual(self.android.body[:subject.CHUNK_BYTES], actual)
        self.assertTrue(commands[0][-1].endswith(" 2>/dev/null"))

    def test_remote_update_changes_closed_import_key(self):
        first = self.acquire()
        self.android.version = 718
        self.android.body = player(build=718)
        self.android.modified += 1
        second = self.acquire()
        self.assertNotEqual(first["apk"], second["apk"])
        self.assertEqual(718, second["identity"]["modBuild"])


if __name__ == "__main__": unittest.main()
