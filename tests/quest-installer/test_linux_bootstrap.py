"""Exercise the Linux bootstrap against actual private venvs, not mocked shells."""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
BOOTSTRAP = ROOT / "tools/quest-installer/bootstrap.sh"
HELPER = ROOT / "tools/quest-installer/linux_bootstrap.py"
SPEC = importlib.util.spec_from_file_location("quest_linux_bootstrap", HELPER)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


@unittest.skipUnless(sys.platform.startswith("linux"), "Linux shell/venv runtime tests")
class LinuxBootstrap(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest Linux bootstrap spaces ")
        self.root = Path(self.temp.name)
        self.scripts = self.root / "script folder"
        self.scripts.mkdir()
        self.requirements = self.root / "requirements with spaces.txt"
        self.requirements.write_text("# standard library only\n", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def shell(self, prefix="", *, ok=True):
        command = (f"source {shlex.quote(str(BOOTSTRAP))}\n" + prefix + "\n"
                   f"get_quest_installer_python {shlex.quote(str(self.scripts))} {shlex.quote(str(self.requirements))}")
        result = subprocess.run(["bash", "-c", command], capture_output=True, text=True, timeout=30)
        if ok:
            self.assertEqual(result.returncode, 0, result.stderr)
        return result

    def state(self):
        return json.loads((self.scripts / ".quest-venv/.quest-owner.json").read_text())

    def test_actual_script_local_venv_and_isolation(self):
        result = self.shell()
        python = Path(result.stdout.strip())
        self.assertEqual(python, self.scripts / ".quest-venv/bin/python")
        info = MODULE.python_info(python)
        self.assertEqual(info["prefix"], str(self.scripts / ".quest-venv"))
        self.assertNotEqual(info["prefix"], info["basePrefix"])
        self.assertTrue(self.state()["complete"])
        self.assertEqual(self.state()["requirementsSha256"], hashlib.sha256(self.requirements.read_bytes()).hexdigest())
        self.assertFalse((self.scripts / ".quest-python-linux").exists())

    def test_second_launch_reuses_actual_environment(self):
        self.shell()
        witness = self.scripts / ".quest-venv/retained.txt"
        witness.write_text("kept")
        config = self.scripts / ".quest-venv/pyvenv.cfg"
        before = config.stat().st_mtime_ns
        result = self.shell()
        self.assertEqual(witness.read_text(), "kept")
        self.assertEqual(config.stat().st_mtime_ns, before)
        self.assertNotIn("Creating isolated", result.stderr)

    def test_changed_comment_manifest_preserves_environment(self):
        self.shell()
        witness = self.scripts / ".quest-venv/retained.txt"
        witness.write_text("kept")
        self.requirements.write_text("# updated comment only\n")
        self.shell()
        self.assertEqual(witness.read_text(), "kept")
        self.assertEqual(self.state()["requirementsSha256"], hashlib.sha256(self.requirements.read_bytes()).hexdigest())

    def test_interrupted_owned_venv_recreated(self):
        environment = self.scripts / ".quest-venv"
        environment.mkdir()
        MODULE.atomic_json(environment / ".quest-owner.json", {"schema": 1, "owner": MODULE.OWNER, "kind": "venv", "complete": False})
        (environment / "unfinished.txt").write_text("incomplete")
        self.shell()
        self.assertFalse((environment / "unfinished.txt").exists())
        self.assertTrue(self.state()["complete"])

    def test_unowned_directory_not_deleted(self):
        environment = self.scripts / ".quest-venv"
        environment.mkdir()
        witness = environment / "user.txt"
        witness.write_text("user data")
        result = self.shell(ok=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unowned", result.stderr)
        self.assertEqual(witness.read_text(), "user data")

    def test_unowned_symlink_not_followed(self):
        original = self.root / "original data"
        original.mkdir()
        witness = original / "user.txt"
        witness.write_text("user data")
        (self.scripts / ".quest-venv").symlink_to(original, target_is_directory=True)
        result = self.shell(ok=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(witness.read_text(), "user data")

    def test_invalid_owned_receipt_not_deleted(self):
        environment = self.scripts / ".quest-venv"
        environment.mkdir()
        (environment / ".quest-owner.json").write_text('{"owner":"another app"}')
        witness = environment / "user.txt"
        witness.write_text("user data")
        result = self.shell(ok=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(witness.read_text(), "user data")

    def test_broken_owned_python_recreated_without_touching_external_symlink(self):
        self.shell()
        original = self.root / "original data"
        original.mkdir()
        witness = original / "user.txt"
        witness.write_text("user data")
        environment = self.scripts / ".quest-venv"
        (environment / "foreign directory").symlink_to(original, target_is_directory=True)
        (environment / "bin/python").unlink()
        (environment / "bin/python").symlink_to(self.root / "missing Python")
        self.shell()
        self.assertEqual(witness.read_text(), "user data")
        self.assertFalse((environment / "foreign directory").exists())

    def test_lock_blocks_another_bootstrap(self):
        import fcntl
        with (self.scripts / ".quest-bootstrap-linux.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            result = self.shell("flock() { command flock -n 9; }", ok=False)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Another Quest Python bootstrap", result.stderr)
        self.assertFalse((self.scripts / ".quest-venv").exists())

    def test_linked_lock_not_opened(self):
        original = self.root / "original.txt"
        original.write_text("untouched")
        (self.scripts / ".quest-bootstrap-linux.lock").symlink_to(original)
        result = self.shell(ok=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(original.read_text(), "untouched")

    def test_wrong_download_rejected_before_extract_or_execute(self):
        cache = self.scripts / ".quest-python-linux"
        cache.mkdir()
        (cache / ".quest-linux-owner").write_text("GloomhavenVR.QuestInstaller.Linux schema=1 kind=cache\n")
        original = self.root / "original.txt"
        original.write_text("not an archive")
        archive = cache / "python-3.13.16-20261003.tar.gz"
        archive.symlink_to(original)
        result = self.shell("quest_linux_usable_python() { return 1; }", ok=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("linked Python download", result.stderr)
        self.assertEqual(original.read_text(), "not an archive")
        self.assertFalse((self.scripts / ".quest-venv").exists())

    def test_linked_cache_not_modified(self):
        original = self.root / "original cache"
        original.mkdir()
        witness = original / "user.txt"
        witness.write_text("kept")
        (self.scripts / ".quest-python-linux").symlink_to(original, target_is_directory=True)
        result = self.shell("quest_linux_usable_python() { return 1; }", ok=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(witness.read_text(), "kept")

    def test_archive_hash_check_rejects_same_sized_changed_data(self):
        archive = self.root / "archive.tar.gz"
        archive.write_bytes(b"correct")
        code = (f"source {shlex.quote(str(BOOTSTRAP))}; quest_linux_python_bytes=7; "
                f"quest_linux_python_sha={hashlib.sha256(b'correct').hexdigest()}; quest_linux_package_valid {shlex.quote(str(archive))}")
        self.assertEqual(subprocess.run(["bash", "-c", code]).returncode, 0)
        archive.write_bytes(b"changed")
        self.assertNotEqual(subprocess.run(["bash", "-c", code]).returncode, 0)

    def test_venv_without_ensurepip_or_ssl_is_not_accepted_as_system_python(self):
        # Run the real probe through an executable Python fixture whose import
        # hook reproduces a distribution omitting exactly one stdlib module.
        # The old venv-only probe succeeds, proving this fixture distinguishes
        # the Debian failure instead of merely returning failure for all code.
        for missing in ("ensurepip", "ssl"):
            with self.subTest(missing=missing):
                fixture = self.root / f"python missing {missing}"
                fixture.write_text("#!" + sys.executable + "\n"
                    "import builtins, sys\n"
                    "original = builtins.__import__\n"
                    "def restricted(name, *args, **kwargs):\n"
                    f"    if name == {missing!r}: raise ModuleNotFoundError(name)\n"
                    "    return original(name, *args, **kwargs)\n"
                    "builtins.__import__ = restricted\n"
                    "exec(sys.argv[sys.argv.index('-c') + 1])\n")
                fixture.chmod(0o755)
                old = subprocess.run([str(fixture), "-I", "-B", "-c", "import sys,venv; raise SystemExit(0 if sys.version_info >= (3,11) else 1)"], capture_output=True)
                self.assertEqual(old.returncode, 0, old.stderr)
                code = f"source {shlex.quote(str(BOOTSTRAP))}; quest_linux_usable_python {shlex.quote(str(fixture))}"
                self.assertNotEqual(subprocess.run(["bash", "-c", code]).returncode, 0)

    @unittest.skipUnless(os.environ.get("GHVR_QUEST_PBS_FIXTURE"), "Optional actual pinned standalone archive fixture")
    def test_actual_pinned_fallback_and_cached_restart(self):
        cache = self.scripts / ".quest-python-linux"
        cache.mkdir()
        (cache / ".quest-linux-owner").write_text("GloomhavenVR.QuestInstaller.Linux schema=1 kind=cache\n")
        shutil.copyfile(os.environ["GHVR_QUEST_PBS_FIXTURE"], cache / "python-3.13.16-20261003.tar.gz")
        first = self.shell("quest_linux_usable_python() { return 1; }")
        info = MODULE.python_info(first.stdout.strip())
        self.assertEqual(info["version"], "3.13.16")
        self.assertIn(".quest-python-linux", info["basePrefix"])
        witness = self.scripts / ".quest-venv/retained.txt"
        witness.write_text("kept")
        second = self.shell("quest_linux_usable_python() { return 1; }")
        self.assertEqual(witness.read_text(), "kept")
        self.assertNotIn("Downloading", first.stderr + second.stderr)
        self.assertNotIn("Creating isolated", second.stderr)


if __name__ == "__main__":
    unittest.main()
