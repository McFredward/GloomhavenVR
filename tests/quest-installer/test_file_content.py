"""Real local shell/file operations exercise the PC-owned installation boundary.

The injected device maps only the exact application directory into a temporary
folder. Actual extraction, SHA, stat, receipt parsing and atomic shell renames run;
this does not establish Quest flash/Wi-Fi throughput.
"""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("quest_file_installer", ROOT / "tools/quest-installer/installer.py")
installer = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = installer
SPEC.loader.exec_module(installer)


class LocalDevice:
    prefix = "/sdcard/Android/data/" + installer.PACKAGE + "/files/"

    def __init__(self, root):
        self.root = root
        self.calls = []
        self.pushed_files = []
        self.fail_after_directory = None
        self.directory_pushes = 0
        self.corrupt_push = False

    def local(self, path):
        assert str(path).startswith(self.prefix), path
        return self.root / str(path)[len(self.prefix):]

    def run(self, *args, **kwargs):
        assert args[:2] == ("-s", "quest")
        action = args[2:]
        self.calls.append(action)
        if action == ("shell", "am", "force-stop", installer.PACKAGE):
            return ""
        if action[:3] == ("shell", "sh", "-c"):
            script = shlex.split(action[3])[0]
            assert self.prefix in script
            # Any other absolute provider/save location is forbidden by this fake.
            assert "/data/user" not in script and "quest-saves" not in script
            translated = script.replace(self.prefix, str(self.root) + "/")
            result = subprocess.run(["sh", "-c", translated], text=True, capture_output=True, timeout=30)
            if result.returncode:
                raise installer.InstallError("Device shell failed: " + result.stderr)
            return result.stdout.strip().replace(str(self.root) + "/", self.prefix)
        if action[0] == "pull":
            origin = self.local(action[1])
            if not origin.is_file():
                raise installer.InstallError("No such file")
            shutil.copy2(origin, action[2])
            return "1 file pulled"
        if action[0] == "push":
            origin, target = Path(action[1]), self.local(action[2])
            if origin.is_dir():
                self.directory_pushes += 1
                for source in origin.rglob("*"):
                    if source.is_file():
                        destination = target / origin.name / source.relative_to(origin)
                        destination.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copyfile(source, destination)
                        self.pushed_files.append(str(destination.relative_to(self.root)))
                        if self.corrupt_push:
                            destination.write_bytes(b"X" * destination.stat().st_size)
                if self.fail_after_directory == self.directory_pushes:
                    raise installer.InstallError("Injected interrupted ADB transfer")
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(origin, target)
            return "files pushed"
        raise AssertionError("Unexpected ADB command: " + repr(action))


class FileContentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.device = LocalDevice(self.root / "device")
        self.game = {"StreamingAssets/Rulebase/Campaign.ruleset": b"campaign fixture",
                     "StreamingAssets/Movies/intro.mp4": b"video fixture",
                     "StreamingAssets/aa/Android/scene.bundle": b"scene fixture"}
        self.mod = {"StreamingAssets/" + name: name.encode() for name in
                    ("gloomhavenvr.bundle", "ghvr-town.bundle", "ghvr-town-voices.bundle")}
        self.make_source()

    def make_source(self, key="c" * 64, game=None, mod=None):
        self.game = game if game is not None else self.game
        self.mod = mod if mod is not None else self.mod
        bank, mod_zip = self.root / "GloomhavenVR-Quest-content.zip", self.root / "mod.zip"
        manifests = {}
        for kind, path, archive, payload in (("game", bank, "quest-startup-content.zip", self.game),
                                           ("mod", mod_zip, "quest-mod-content.zip", self.mod)):
            with zipfile.ZipFile(path, "w") as destination:
                for name, data in payload.items():
                    destination.writestr(name, data)
            manifests[kind] = {"schema": 1, "inputKey": key, "archive": archive,
                               "archiveSha256": installer.digest(path), "externalDelivery": kind == "game",
                               "files": [{"path": name, "sha256": hashlib.sha256(data).hexdigest(), "size": len(data)}
                                         for name, data in payload.items()]}
        self.manifest = {"schema": 1, "inputKey": key, **manifests}
        apk = self.root / "game.apk"
        with zipfile.ZipFile(apk, "w") as destination:
            destination.write(mod_zip, "assets/quest-mod-content.zip")
            destination.writestr("assets/Quest/installation-manifest.json", json.dumps(self.manifest))
        self.source = installer.Source("apk", apk, apk, installer.digest(apk), "fixture", False, True, 625, key,
                                      (installer.ContentFile(bank, "quest-startup-content.zip", installer.digest(bank), bank.stat().st_size),))

    def install(self, repair=False):
        with contextlib.redirect_stdout(io.StringIO()):
            return installer.install_file_content(self.device, "quest", self.source, repair)

    def receipt(self, kind="game"):
        directory = "quest-owned-game" if kind == "game" else "quest-mod-resources"
        path = self.device.root / directory / installer._RECEIPT
        return installer.decode_content_receipt(path.read_bytes()) if path.exists() else None

    def test_first_install_prepares_all_assets_before_launch_and_never_uploads_bank(self):
        records = self.install()
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 6)
        self.assertEqual(self.receipt()["key"], installer.content_key(self.manifest["game"]))
        for name, content in self.game.items():
            self.assertEqual((self.device.root / "quest-owned-game" / name).read_bytes(), content)
        self.assertFalse(any("quest-install-input" in str(call) or "start" in call for call in self.device.calls))
        self.assertFalse(list(self.device.root.rglob("*.zip")))

    def test_warm_install_and_source_only_update_have_zero_tree_scans_and_pushes(self):
        self.install()
        pushes = len(self.device.pushed_files)
        self.make_source(key="d" * 64)
        self.device.calls.clear()
        records = self.install()
        self.assertEqual(sum(row["uploadedFiles"] for row in records), 0)
        self.assertEqual(len(self.device.pushed_files), pushes)
        self.assertFalse(any("sha256sum" in str(call) or "stat -c" in str(call) for call in self.device.calls))
        self.assertEqual(sum(call[0] == "pull" for call in self.device.calls), 2)

    def test_changed_bank_updates_only_changed_file_and_preserves_saves(self):
        self.install()
        save = self.device.root / "quest-saves/campaign.save"
        save.parent.mkdir(); save.write_bytes(b"private original save")
        new = dict(self.game); new["StreamingAssets/aa/Android/scene.bundle"] = b"changed scene fixture"
        self.make_source(key="e" * 64, game=new)
        start = len(self.device.pushed_files)
        records = self.install()
        self.assertEqual(self.device.pushed_files[start:], ["quest-owned-game/StreamingAssets/aa/Android/scene.bundle"])
        self.assertEqual(records[1]["reusedFiles"], 2)
        self.assertEqual(save.read_bytes(), b"private original save")

    def test_old_menu_inventory_adopts_known_subset_without_rehashing_unmodified_files(self):
        old = dict(self.game)
        self.make_source(game={next(iter(old)): next(iter(old.values()))})
        self.install()
        self.make_source(key="e" * 64, game=old)
        self.device.calls.clear()
        records = self.install()
        self.assertEqual(records[1]["reusedFiles"], 1)
        self.assertEqual(records[1]["uploadedFiles"], 2)
        self.assertFalse(any("sha256sum" in str(call) and "Campaign.ruleset" in str(call) for call in self.device.calls))

    def test_retired_owned_files_are_removed_after_new_tree_commits_unknown_files_survive(self):
        self.install()
        unknown = self.device.root / "quest-owned-game/StreamingAssets/player-owned.txt"
        unknown.write_bytes(b"unknown preserved bytes")
        new = dict(self.game)
        del new["StreamingAssets/aa/Android/scene.bundle"]
        new["StreamingAssets/aa/Android/new-scene.bundle"] = b"new original bank"
        self.make_source(key="f" * 64, game=new)
        self.install()
        self.assertFalse((self.device.root / "quest-owned-game/StreamingAssets/aa/Android/scene.bundle").exists())
        self.assertEqual(unknown.read_bytes(), b"unknown preserved bytes")
        self.assertIsNotNone(self.receipt())

    def test_missing_file_is_repaired_by_explicit_repair_not_by_warm_launch_scanning(self):
        self.install()
        missing = self.device.root / "quest-owned-game/StreamingAssets/Movies/intro.mp4"
        missing.unlink()
        start = len(self.device.pushed_files)
        records = self.install(repair=True)
        self.assertEqual(records[1]["uploadedFiles"], 1)
        self.assertEqual(self.device.pushed_files[start:], ["quest-owned-game/StreamingAssets/Movies/intro.mp4"])

    def test_same_size_corruption_repair_changes_only_corrupt_file(self):
        self.install()
        target = self.device.root / "quest-owned-game/StreamingAssets/Movies/intro.mp4"
        target.write_bytes(b"X" * target.stat().st_size)
        start = len(self.device.pushed_files)
        records = self.install(repair=True)
        self.assertEqual(records[1]["uploadedFiles"], 1)
        self.assertEqual(len(self.device.pushed_files) - start, 1)
        self.assertEqual(target.read_bytes(), self.game["StreamingAssets/Movies/intro.mp4"])

    def test_interrupted_batches_resume_verified_files_without_publishing_completion(self):
        # Force small batches without creating huge fixture assets.
        with mock.patch.object(installer, "_INSTALL_BATCH_BYTES", 18):
            self.device.fail_after_directory = 5  # mod three; game first committed, second interrupted
            with self.assertRaisesRegex(installer.InstallError, "interrupted"):
                self.install()
            self.assertIsNone(self.receipt())
            pending = self.device.root / "quest-owned-game" / (installer._RECEIPT + ".pending")
            self.assertEqual(len(installer.decode_content_receipt(pending.read_bytes())["files"]), 1)
            start = len(self.device.pushed_files)
            self.device.fail_after_directory = None
            records = self.install()
        self.assertEqual(records[1]["reusedFiles"], 1)
        self.assertEqual(records[1]["uploadedFiles"], 2)
        self.assertEqual(len(self.device.pushed_files) - start, 2)
        self.assertIsNotNone(self.receipt())

    def test_bad_uploaded_bytes_never_publish_completion_and_can_be_retried(self):
        self.device.corrupt_push = True
        with self.assertRaisesRegex(installer.InstallError, "verification failed"):
            self.install()
        self.assertIsNone(self.receipt("mod"))
        self.device.corrupt_push = False
        self.install()
        self.assertIsNotNone(self.receipt("mod"))

    def test_manifest_scope_mismatch_and_traversal_fail_before_adb(self):
        for field, value in (("inputKey", "0" * 64), ("schema", 2)):
            with self.subTest(field=field):
                self.make_source(); self.manifest[field] = value
                self.replace_manifest()
                with self.assertRaises(installer.InstallError):
                    self.install()
                self.assertEqual(self.device.calls, [])
        self.make_source(); self.manifest["game"]["files"][0]["path"] = "StreamingAssets/../../quest-saves/campaign.save"
        self.replace_manifest()
        with self.assertRaises(installer.InstallError): self.install()
        self.assertEqual(self.device.calls, [])

    def replace_manifest(self):
        with zipfile.ZipFile(self.source.apk, "w") as destination:
            destination.write(self.root / "mod.zip", "assets/quest-mod-content.zip")
            destination.writestr("assets/Quest/installation-manifest.json", json.dumps(self.manifest))

    def test_extra_bank_member_is_rejected_before_game_writes(self):
        with zipfile.ZipFile(self.source.content[0].path, "a") as archive:
            archive.writestr("StreamingAssets/unmanifested.dat", b"not accepted")
        with self.assertRaisesRegex(installer.InstallError, "exact file inventory"):
            self.install()
        self.assertIsNone(self.receipt())
        self.assertFalse((self.device.root / "quest-owned-game/StreamingAssets").exists())

    def test_symlink_parent_cannot_write_outside_application_content(self):
        self.install()
        directory = self.device.root / "quest-owned-game/StreamingAssets/Movies"
        shutil.rmtree(directory)
        outside = self.root / "outside"; outside.mkdir()
        directory.symlink_to(outside, target_is_directory=True)
        with self.assertRaises(installer.InstallError): self.install(repair=True)
        self.assertFalse(list(outside.iterdir()))

    def test_receipt_roundtrip_and_checksum_corruption(self):
        manifest = self.manifest["game"]
        encoded = installer.encode_content_receipt(manifest, {row["path"]: row for row in manifest["files"]})
        self.assertEqual(installer.decode_content_receipt(encoded)["key"], installer.content_key(manifest))
        for changed in (encoded[:40], encoded[:-1], encoded[:-1] + bytes([encoded[-1] ^ 1])):
            self.assertIsNone(installer.decode_content_receipt(changed))


if __name__ == "__main__":
    unittest.main()
