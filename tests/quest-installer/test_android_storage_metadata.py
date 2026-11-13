"""Host protocol uses real local pipes/files; Android Binder has separate tests."""
import contextlib
import hashlib
import importlib.util
import io
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("android_metadata_control", ROOT / "tools/quest-installer/android_storage.py")
metadata = importlib.util.module_from_spec(spec); spec.loader.exec_module(metadata)


class Device:
    def __init__(self, root):
        self.root, self.hardware = root, "fixture-Quest"
        self.log = root / "adb.log"
        self.external, self.scratch = root / "files", root / "shell"
        self.external.mkdir(); self.scratch.mkdir()
        self.calls, self.pushes = [], []
        self.corrupt = self.bad_reply = False
        self.process = root / "platform.py"
        self.process.write_text('''import os,sys
from pathlib import Path
lines=sys.stdin.read().splitlines()
assert lines[0]=='GHVRQ_STORAGE_FIXUP_V1' and lines[-1]=='END'
paths=lines[1:-1]
prefix='/storage/emulated/0/Android/data/dev.gloomhavenvr.quest/files/'
for path in paths:
 assert path.startswith(prefix)
 target=Path(sys.argv[1])/path[len(prefix):]
 assert target.is_dir() and not target.is_symlink()
 os.chmod(target,0o2770)
print('GHVRQ_STORAGE_BEGIN\\t'+str(len(paths)))
for i,path in enumerate(paths,1):print('GHVRQ_STORAGE_DONE\\t'+str(i)+'\\t'+path)
print('GHVRQ_STORAGE_COMPLETE\\t'+str(len(paths)))
''')

    def identity(self, address): return self.hardware

    def run(self, *args, **options):
        assert args[:2] == ("-s", "usb")
        action = args[2:]; self.calls.append(action)
        if action[0] == "push":
            self.pushes.append(str(action[1]))
            target = Path(str(action[2]).replace("/data/local/tmp", str(self.scratch)))
            shutil.copyfile(action[1], target)
            if self.corrupt: target.write_bytes(b"wrong")
            return "1 file pushed"
        assert action == ("shell", "-T", "sh", "-s"), action
        script = options["input_text"].replace("/data/local/tmp", str(self.scratch))
        # Leave the protocol's canonical paths intact; only shell-side external
        # accesses map into this fixture. The stub models only the Binder call.
        script = script.replace(metadata.SHELL_BASE, str(self.external) + "/")
        script = script.replace("app_process /system/bin dev.gloomhavenvr.tools.StorageAccess",
                                "python3 " + str(self.process) + " " + str(self.external))
        result = subprocess.run(["sh", "-s"], input=script, text=True, capture_output=True, timeout=10)
        if result.returncode: raise RuntimeError(result.stderr)
        text = result.stdout.strip().replace(str(self.scratch), "/data/local/tmp")
        return text.replace("GHVRQ_STORAGE_COMPLETE", "INCOMPLETE") if self.bad_reply else text


class MetadataTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.device = Device(Path(self.temp.name))
        self.root = metadata.SHELL_BASE + "quest-mod-resources"
        self.bank = self.device.external / "quest-mod-resources"
        self.parent = self.bank / "StreamingAssets"
        self.parent.mkdir(parents=True)
        self.file = self.parent / "fixture.bundle"; self.file.write_bytes(b"unchanged owned payload")
        self.manifest = {"archive": "quest-mod-content.zip", "files": [
            {"path": "StreamingAssets/fixture.bundle", "size": self.file.stat().st_size,
             "sha256": hashlib.sha256(self.file.read_bytes()).hexdigest()}]}
        self.access = metadata.StorageMetadata(self.device, "usb", self.device.hardware, 10191)
        self.addCleanup(self.access.close)

    def qualify(self, owner=None, manifest=None):
        with contextlib.redirect_stdout(io.StringIO()):
            return (owner or self.access).qualify(self.root, manifest or self.manifest, ".quest-installation.receipt")

    def test_metadata_repair_retains_bytes_and_warm_cache_avoids_repair_and_upload(self):
        self.bank.chmod(0o700); self.parent.chmod(0o700)
        before = self.file.read_bytes()
        result = self.qualify()
        self.assertEqual(result["storageDirectoriesRequested"], 1)
        self.assertEqual(self.parent.stat().st_mode & 0o7777, 0o2770)
        self.assertEqual(self.file.read_bytes(), before)
        self.assertEqual(self.device.pushes, [str(metadata.ASSET)])
        self.access.close(); self.assertEqual(list(self.device.scratch.iterdir()), [])
        previous = self.file.stat()
        self.device.calls.clear(); self.device.pushes.clear()
        self.assertEqual(self.qualify()["platformStorageMetadata"], "retained")
        self.assertEqual(self.device.pushes, [])
        self.assertFalse(any("app_process" in str(call) for call in self.device.calls))
        self.assertEqual(self.file.stat().st_mtime_ns, previous.st_mtime_ns)

    def test_partial_platform_reply_cannot_publish_reuse_marker(self):
        self.device.bad_reply = True
        with self.assertRaisesRegex(RuntimeError, "did not acknowledge"): self.qualify()
        self.assertFalse((self.bank / metadata.MARKER).exists())

    def test_corrupt_helper_cannot_invoke_platform_or_publish_marker(self):
        self.device.corrupt = True
        with self.assertRaisesRegex(RuntimeError, "changed in transfer"): self.qualify()
        self.assertFalse(any("app_process" in str(call) for call in self.device.calls))
        self.assertFalse((self.bank / metadata.MARKER).exists())

    def test_foreign_headset_or_unmanifested_escape_never_requests_repair(self):
        self.device.hardware = "another-Quest"
        with self.assertRaisesRegex(RuntimeError, "Quest changed"): self.qualify()
        self.assertEqual(self.device.calls, [])
        invalid = {"files": [{"path": "StreamingAssets/../save"}]}
        with self.assertRaisesRegex(RuntimeError, "noncanonical"): self.qualify(manifest=invalid)
        self.assertEqual(self.device.calls, [])

    def test_new_installed_uid_reconciles_again_without_game_copy(self):
        self.qualify(); self.access.close(); self.device.pushes.clear()
        changed = metadata.StorageMetadata(self.device, "usb", self.device.hardware, 10192)
        self.addCleanup(changed.close)
        self.assertEqual(self.qualify(changed)["platformStorageMetadata"], "requested")
        self.assertEqual(self.device.pushes, [str(metadata.ASSET)])


if __name__ == "__main__": unittest.main()
