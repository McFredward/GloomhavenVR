"""Portable transport reaches the actual per-file installer without bank copies.

The injected device executes real local shell, extraction, hashes and receipts.
It does not establish Android package-manager or physical Quest throughput.
"""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("portable_local_device_fixture", ROOT / "tests/quest-installer/test_file_content.py")
fixture = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixture)
installer = fixture.installer


class PortableInstallationTests(unittest.TestCase):
    def setUp(self):
        self.original = fixture.FileContentTests("test_first_install_prepares_all_assets_before_launch_and_never_uploads_bank")
        self.original.setUp(); self.addCleanup(self.original.tearDown); self.addCleanup(self.original.doCleanups)
        self.root = self.original.root
        self.output = self.root / "builder"; self.output.mkdir()
        self.api = installer.builder_module().portable_packages
        self.apk = self.original.source.apk
        row = {"file": "GloomhavenVR-Quest-content.zip", "archive": "quest-startup-content.zip",
               "size": self.original.source.content[0].size, "sha256": self.original.source.content[0].sha256}
        self.identity = {"schema": 1, "target": "game", "inputKey": self.original.source.input_key,
                         "mod": {"modBuild": 625}, "profile": {"isDummy": True}}
        with zipfile.ZipFile(self.apk, "a") as archive:
            archive.writestr("AndroidManifest.xml", b"fixture Android manifest; actual signing tested separately")
            archive.writestr("assets/Quest/input-manifest.json", json.dumps(self.identity))
            archive.writestr("assets/Quest/content-delivery.json", json.dumps({"schema": 1,
                "package": installer.PACKAGE, "inputKey": self.identity["inputKey"], "files": [row]}))
        self.package = self.root / "shared-game.zip"
        self.api.export_package(self.apk, self.package, self.output)
        self.old_cache = installer._INPUT_CACHE_PATH
        installer._INPUT_CACHE_PATH = self.output / "device/qualified-inputs.json"
        self.addCleanup(setattr, installer, "_INPUT_CACHE_PATH", self.old_cache)
        installer._DIGEST_CACHE.clear(); self.addCleanup(installer._DIGEST_CACHE.clear)
        self.patch = mock.patch.object(installer, "portable_packages", return_value=self.api)
        self.patch.start(); self.addCleanup(self.patch.stop)

    def source(self):
        with contextlib.redirect_stdout(io.StringIO()):
            return installer.resolve_source("package", self.package)

    def install(self, source):
        with contextlib.redirect_stdout(io.StringIO()):
            return installer.install_file_content(self.original.device, "quest", source)

    def test_selection_and_fresh_install_use_nested_bank_without_whole_bank_hash(self):
        # Selection receives an actual import byte witness. Neither it nor
        # nested per-file installation needs a full outer/container digest.
        with mock.patch.object(installer, "digest", side_effect=AssertionError("No repeated APK or complete package hash")):
            source = self.source()
        self.assertEqual(source.kind, "package")
        self.assertEqual(source.path, self.package)
        self.assertEqual(source.content[0].package, self.package)
        self.assertEqual(source.content[0].size, self.original.source.content[0].size)
        self.assertFalse((source.apk.parent / "GloomhavenVR-Quest-content.zip").exists())
        records = self.install(source)
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 7)
        self.assertEqual(records[1]["size"], source.content[0].size)
        self.assertEqual(self.original.receipt()["key"], installer.content_key(self.original.manifest["game"]))
        for name, body in self.original.game.items():
            self.assertEqual((self.original.device.root / "quest-owned-game" / name).read_bytes(), body)
        self.assertFalse(list(self.original.device.root.rglob("*.zip")))
        self.assertFalse(list(source.apk.parent.glob("*content.zip")))

    def test_warm_install_never_opens_game_bank_or_uploads_existing_files(self):
        source = self.source(); self.install(source)
        before = len(self.original.device.pushed_files)
        self.original.device.calls.clear()
        with mock.patch.object(self.api, "open_content", side_effect=AssertionError("Warm banks must not be read")):
            records = self.install(source)
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 0)
        self.assertEqual(len(self.original.device.pushed_files), before)
        self.assertFalse(any("sha256sum" in str(row) for row in self.original.device.calls))

    def test_physical_file_backed_bank_skips_duplicate_archive_scan(self):
        original_digest = installer.digest
        def qualified_apk_only(path):
            self.assertNotEqual(Path(path), self.original.source.content[0].path,
                                "Signed per-file delivery must not scan the complete bank")
            return original_digest(path)
        with mock.patch.object(installer, "digest", side_effect=qualified_apk_only):
            source = installer.resolve_source("apk", self.apk)
        self.assertIsNone(source.content[0].package)
        records = self.install(source)
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 7)
        for name, body in self.original.game.items():
            self.assertEqual((self.original.device.root / "quest-owned-game" / name).read_bytes(), body)

    def test_partial_receipt_repairs_only_missing_game_file_from_original_zip(self):
        source = self.source(); self.install(source)
        rows = self.original.manifest["game"]["files"]
        missing = rows[-1]; directory = self.original.device.root / "quest-owned-game"
        (directory / missing["path"]).unlink()
        (directory / installer._RECEIPT).write_bytes(installer.encode_content_receipt(
            self.original.manifest["game"], {row["path"]: row for row in rows[:-1]}))
        before = len(self.original.device.pushed_files)
        records = self.install(source)
        self.assertEqual(records[1]["uploadedFiles"], 1)
        self.assertEqual(self.original.device.pushed_files[before:], ["quest-owned-game/" + missing["path"]])
        self.assertFalse((source.apk.parent / "GloomhavenVR-Quest-content.zip").exists())

    def test_remembered_package_and_apk_option_select_the_same_qualified_source(self):
        args = installer.parser().parse_args(["--apk", str(self.package)])
        with contextlib.redirect_stdout(io.StringIO()):
            selected = installer.select_source(args, {})
            restored = installer.select_source(installer.parser().parse_args([]), {"source": selected.setting()})
        self.assertEqual(restored, selected)
        with self.api.open_content({"path": selected.content[0].package, "member": selected.content[0].member,
            "size": selected.content[0].size, "sha256": selected.content[0].sha256,
            "archive": selected.content[0].archive}) as body, zipfile.ZipFile(body) as bank:
            self.assertEqual(bank.read(next(iter(self.original.game))), next(iter(self.original.game.values())))


if __name__ == "__main__": unittest.main()
