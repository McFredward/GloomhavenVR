#!/usr/bin/env python3
"""Focused checks for the Steam Frame first-launch graphics configuration."""

from __future__ import annotations

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "frame-boot-config.py"
SPEC = importlib.util.spec_from_file_location("frame_boot_config", SCRIPT)
assert SPEC and SPEC.loader
frame_boot_config = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(frame_boot_config)


class FrameBootConfigTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.game = Path(self.temp.name) / "Gloomhaven"
        self.boot = self.game / "GH_Data" / "boot.config"
        self.boot.parent.mkdir(parents=True)
        self.backup = self.boot.with_name("boot.config.gloomhavenvr-backup")

    def test_fresh_install_backs_up_and_preserves_unrelated_lines(self) -> None:
        original = b"wait-for-native-debugger=0\r\ngfx-enable-gfx-jobs=0\r\ncustom-setting=hello\r\n"
        self.boot.write_bytes(original)

        self.assertTrue(frame_boot_config.prepare(self.game))
        self.assertEqual(self.backup.read_bytes(), original)
        self.assertEqual(
            self.boot.read_bytes(),
            b"wait-for-native-debugger=0\r\ngfx-enable-gfx-jobs=1\r\n"
            b"custom-setting=hello\r\ngfx-enable-native-gfx-jobs=1\r\n",
        )

    def test_second_run_is_a_true_no_op(self) -> None:
        self.boot.write_bytes(b"gfx-enable-gfx-jobs=1\ngfx-enable-native-gfx-jobs=1\n")
        inode = self.boot.stat().st_ino

        self.assertFalse(frame_boot_config.prepare(self.game))
        self.assertEqual(self.boot.stat().st_ino, inode)
        self.assertFalse(self.backup.exists())

    def test_partial_install_fills_missing_key_without_overwriting_backup(self) -> None:
        self.boot.write_bytes(b"gfx-enable-gfx-jobs=1\nother=ok")
        self.backup.write_bytes(b"untouched original")

        self.assertTrue(frame_boot_config.prepare(self.game))
        self.assertEqual(self.boot.read_bytes(), b"gfx-enable-gfx-jobs=1\nother=ok\ngfx-enable-native-gfx-jobs=1\n")
        self.assertEqual(self.backup.read_bytes(), b"untouched original")

    def test_false_config_disables_both_keys_like_the_preloader(self) -> None:
        self.boot.write_bytes(b"gfx-enable-gfx-jobs=1\ngfx-enable-native-gfx-jobs=1\n")
        config = self.game / "BepInEx" / "config" / "dev.gloomhavenvr.cfg"
        config.parent.mkdir(parents=True)
        config.write_text("[Other]\nEnableGraphicsJobs=true\n[Core]\nEnableGraphicsJobs = false\n")

        self.assertTrue(frame_boot_config.prepare(self.game))
        self.assertEqual(self.boot.read_bytes(), b"gfx-enable-gfx-jobs=0\ngfx-enable-native-gfx-jobs=0\n")

    def test_dry_run_does_not_write_or_create_backup(self) -> None:
        original = b"gfx-enable-gfx-jobs=0\n"
        self.boot.write_bytes(original)

        self.assertTrue(frame_boot_config.prepare(self.game, dry_run=True))
        self.assertEqual(self.boot.read_bytes(), original)
        self.assertFalse(self.backup.exists())

    def test_missing_boot_config_exits_nonzero(self) -> None:
        self.boot.unlink(missing_ok=True)

        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--game-path", str(self.game)],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("boot.config", result.stderr)


if __name__ == "__main__":
    unittest.main()
