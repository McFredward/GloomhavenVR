#!/usr/bin/env python3
"""Focused synthetic Steam configuration checks for the Frame setup helper."""

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
HELPER = ROOT / "scripts/steam-frame-config.py"
SPEC = importlib.util.spec_from_file_location("steam_frame_config", HELPER)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = module
SPEC.loader.exec_module(module)


def localconfig(options: str | None = None) -> bytes:
    value = f'"LaunchOptions" "{options}"' if options is not None else ""
    return (f'"UserLocalConfigStore" {{ "Software" {{ "Valve" {{ "Steam" '
            f'{{ "Apps" {{ "999" {{ "LaunchOptions" "-unrelated" }} '
            f'"780290" {{ {value} }} }} }} }} }} }}').encode()


def shortcut_entry(index: str, name: str, exe: str, appid: int, extra: bytes = b"") -> bytes:
    return module.bin_object(index, b"".join((
        module.bin_int("appid", appid), module.bin_string("AppName", name),
        module.bin_string("Exe", exe), module.bin_int("OpenVR", 0), extra)))


class SteamFrameConfigTest(unittest.TestCase):
    def test_launch_options_preserve_other_options_and_flat_identity(self):
        original = (b'// keep this comment\n' + localconfig('--gloomhavenvr -windowed'))
        changed = module.patch_localconfig(original)
        self.assertIn(b'WINEDLLOVERRIDES=', changed)
        self.assertIn(b'%command%', changed)
        self.assertIn(b'-windowed', changed)
        self.assertNotIn(b'--gloomhavenvr', changed)
        self.assertIn(b'"999" { "LaunchOptions" "-unrelated" }', changed)
        self.assertTrue(changed.startswith(b'// keep this comment\n'))
        self.assertEqual(module.patch_localconfig(changed), changed)

    def test_existing_override_is_merged_and_repaired(self):
        options = 'PROTON_LOG=1 WINEDLLOVERRIDES="d3d11=n;winhttp=b" %command% --gloomhavenvr -foo %command%'
        changed = module.update_launch_options(options)
        self.assertEqual(changed.count("%command%"), 1)
        self.assertEqual(changed.count("WINEDLLOVERRIDES="), 1)
        self.assertIn("d3d11=n;winhttp=n,b", changed)
        self.assertIn("-foo", changed)
        self.assertNotIn("--gloomhavenvr", changed)
        self.assertEqual(module.update_launch_options(changed), changed)

    def test_missing_game_entry_is_added_once(self):
        raw = b'"UserLocalConfigStore" { "Software" { "Valve" { "Steam" { "Apps" { } } } } }'
        changed = module.patch_localconfig(raw)
        parsed = module.parse_text(changed.decode())
        game, depth = module.find_path(parsed, ["UserLocalConfigStore", "Software", "Valve", "Steam", "Apps", "780290"])
        self.assertEqual(depth, 6)
        self.assertIsNotNone(game)
        self.assertEqual(module.patch_localconfig(changed), changed)

    def test_binary_shortcut_preserves_unrelated_bytes_and_updates_in_place(self):
        unrelated = shortcut_entry("0", "Another Game", '"/games/other"', 0x80112233,
                                   module.bin_string("Unfamiliar", "keep me")
                                   + b"\x05WideNote\0" + "Other".encode("utf-16le") + b"\0\0")
        raw = module.bin_object("shortcuts", unrelated) + b"\x08"
        launcher = Path("/home/frame/VR Launcher/launch.sh")
        icon = Path("/home/frame/VR Launcher/icon.png")
        changed, appid = module.patch_shortcuts(raw, launcher, icon)
        self.assertIn(unrelated, changed)
        self.assertEqual(module.patch_shortcuts(changed, launcher, icon), (changed, appid))
        root = module.parse_binary(changed)[0]
        created = root.children[1]
        fields = created.children
        self.assertEqual(module.bin_field(fields, "AppName").value, "GloomhavenVR")
        self.assertEqual(module.bin_field(fields, "Exe").value, '"/home/frame/VR Launcher/launch.sh"')
        self.assertEqual(module.bin_field(fields, "OpenVR").value, 1)
        self.assertEqual(module.bin_field(fields, "appid").value & 0xFFFFFFFF, appid)

        # An existing Steam-created shortcut has its own random ID. Keep it.
        custom_id = 0xF0102030
        existing = shortcut_entry("1", "GloomhavenVR", '"/old/location"', custom_id,
                                  module.bin_string("Unfamiliar", "preserved"))
        raw = module.bin_object("shortcuts", unrelated + existing) + b"\x08"
        updated, appid = module.patch_shortcuts(raw, launcher, icon)
        self.assertEqual(appid, custom_id)
        self.assertIn(unrelated, updated)
        self.assertIn(module.bin_string("Unfamiliar", "preserved"), updated)
        self.assertEqual(module.patch_shortcuts(updated, launcher, icon), (updated, appid))

    def test_cli_dry_run_then_idempotent_write_with_backups_and_art(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            root = base / "Steam"
            config = root / "userdata/123/config"
            config.mkdir(parents=True)
            (config / "localconfig.vdf").write_bytes(localconfig())
            game = base / "Gloomhaven"
            game.mkdir()
            (game / "GH.exe").touch()
            launcher = base / "data/launch.sh"
            icon = base / "data/icon.png"
            logo = base / "data/logo.png"
            proc = base / "empty-proc"
            proc.mkdir()
            args = [sys.executable, str(HELPER), "--steam-root", str(root),
                    "--game-path", str(game), "--launcher", str(launcher),
                    "--icon", str(icon), "--logo", str(logo), "--proc-root", str(proc)]
            dry = subprocess.run(args + ["--dry-run"], text=True, capture_output=True)
            self.assertEqual(dry.returncode, 0, dry.stderr)
            self.assertIn("Steam restart required: yes", dry.stdout)
            self.assertFalse((config / "shortcuts.vdf").exists())
            for path in (launcher, icon, logo):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"art")
            need = subprocess.run(args + ["--needs-update"], text=True, capture_output=True)
            self.assertEqual(need.returncode, 0, need.stderr)
            self.assertEqual(need.stdout, "yes\n")
            self.assertFalse((config / "shortcuts.vdf").exists())
            first = subprocess.run(args, text=True, capture_output=True)
            self.assertEqual(first.returncode, 0, first.stderr)
            self.assertIn("Steam restart required: yes", first.stdout)
            self.assertEqual(len(list(config.glob("localconfig.vdf.gloomhavenvr-backup-*"))), 1)
            appid = module.patch_shortcuts((config / "shortcuts.vdf").read_bytes(), launcher, icon)[1]
            self.assertEqual((config / "grid" / f"{appid}_logo.png").read_bytes(), b"art")
            self.assertEqual((config / "grid" / f"{appid}_icon.png").read_bytes(), b"art")
            second = subprocess.run(args, text=True, capture_output=True)
            self.assertEqual(second.returncode, 0, second.stderr)
            self.assertIn("Steam restart required: no", second.stdout)
            need = subprocess.run(args + ["--needs-update"], text=True, capture_output=True)
            self.assertEqual(need.returncode, 0, need.stderr)
            self.assertEqual(need.stdout, "no\n")
            self.assertEqual(len(list(config.glob("localconfig.vdf.gloomhavenvr-backup-*"))), 1)

    def test_steam_running_refuses_write_without_touching_config(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            root = base / "Steam"
            config = root / "userdata/123/config"
            config.mkdir(parents=True)
            source = localconfig()
            (config / "localconfig.vdf").write_bytes(source)
            game = base / "game"
            game.mkdir()
            (game / "GH.exe").touch()
            launcher = base / "launch.sh"
            icon = base / "icon.png"
            logo = base / "logo.png"
            for path in (launcher, icon, logo):
                path.write_bytes(b"x")
            proc = base / "proc/222"
            proc.mkdir(parents=True)
            (proc / "comm").write_text("steamwebhelper\n")
            args = [sys.executable, str(HELPER), "--steam-root", str(root),
                    "--game-path", str(game), "--launcher", str(launcher),
                    "--icon", str(icon), "--logo", str(logo),
                    "--proc-root", str(proc.parent)]
            result = subprocess.run(args, text=True, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Steam is still running", result.stderr)
            self.assertEqual((config / "localconfig.vdf").read_bytes(), source)
            self.assertFalse((config / "shortcuts.vdf").exists())

    def test_most_recent_account_id_uses_steam_id64_conversion(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for account in ("123", "456"):
                config = root / "userdata" / account / "config"
                config.mkdir(parents=True)
                (config / "localconfig.vdf").write_bytes(localconfig())
            steamid = module.STEAM_ID64_BASE + 456
            (root / "config").mkdir()
            (root / "config/loginusers.vdf").write_text(
                f'"users" {{ "{steamid}" {{ "MostRecent" "1" }} }}')
            self.assertEqual(module.select_account(root, None).name, "456")


if __name__ == "__main__":
    unittest.main()
