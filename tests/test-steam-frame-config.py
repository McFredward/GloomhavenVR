#!/usr/bin/env python3
"""Focused synthetic Steam configuration checks for the Frame setup helper."""

import importlib.util
import json
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
            portrait = base / "data/portrait.png"
            header = base / "data/header.png"
            hero = base / "data/hero.png"
            proc = base / "empty-proc"
            proc.mkdir()
            args = [sys.executable, str(HELPER), "--steam-root", str(root),
                    "--game-path", str(game), "--launcher", str(launcher),
                    "--icon", str(icon), "--logo", str(logo),
                    "--portrait", str(portrait), "--header", str(header),
                    "--hero", str(hero), "--proc-root", str(proc)]
            dry = subprocess.run(args + ["--dry-run"], text=True, capture_output=True)
            self.assertEqual(dry.returncode, 0, dry.stderr)
            self.assertIn("Steam restart required: yes", dry.stdout)
            self.assertFalse((config / "shortcuts.vdf").exists())
            for path in (launcher, icon, logo, portrait, header, hero):
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
            self.assertEqual((config / "grid" / f"{appid}p.png").read_bytes(), b"art")
            self.assertEqual((config / "grid" / f"{appid}.png").read_bytes(), b"art")
            self.assertEqual((config / "grid" / f"{appid}_hero.png").read_bytes(), b"art")
            second = subprocess.run(args, text=True, capture_output=True)
            self.assertEqual(second.returncode, 0, second.stderr)
            self.assertIn("Steam restart required: no", second.stdout)
            need = subprocess.run(args + ["--needs-update"], text=True, capture_output=True)
            self.assertEqual(need.returncode, 0, need.stderr)
            self.assertEqual(need.stdout, "no\n")
            self.assertEqual(len(list(config.glob("localconfig.vdf.gloomhavenvr-backup-*"))), 1)
            # A replaced image must update the same shortcut, with no duplicate
            # shortcut or unrelated Steam configuration changes.
            hero.write_bytes(b"new hero")
            need = subprocess.run(args + ["--needs-update"], text=True, capture_output=True)
            self.assertEqual(need.stdout, "yes\n")
            again = subprocess.run(args, text=True, capture_output=True)
            self.assertEqual(again.returncode, 0, again.stderr)
            self.assertEqual((config / "grid" / f"{appid}_hero.png").read_bytes(), b"new hero")
            self.assertEqual(module.patch_shortcuts((config / "shortcuts.vdf").read_bytes(), launcher, icon)[1], appid)
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
            portrait = base / "portrait.png"
            header = base / "header.png"
            hero = base / "hero.png"
            for path in (launcher, icon, logo, portrait, header, hero):
                path.write_bytes(b"x")
            proc = base / "proc/222"
            proc.mkdir(parents=True)
            (proc / "comm").write_text("steamwebhelper\n")
            args = [sys.executable, str(HELPER), "--steam-root", str(root),
                    "--game-path", str(game), "--launcher", str(launcher),
                    "--icon", str(icon), "--logo", str(logo),
                    "--portrait", str(portrait), "--header", str(header),
                    "--hero", str(hero),
                    "--proc-root", str(proc.parent)]
            result = subprocess.run(args, text=True, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Steam is still running", result.stderr)
            self.assertEqual((config / "localconfig.vdf").read_bytes(), source)
            self.assertFalse((config / "shortcuts.vdf").exists())
            prepared = subprocess.run(args + ["--allow-running"], text=True, capture_output=True)
            self.assertNotEqual(prepared.returncode, 0)
            self.assertIn("Properties UI", prepared.stderr)
            self.assertEqual((config / "localconfig.vdf").read_bytes(), source)
            self.assertFalse((config / "shortcuts.vdf").exists())
            prepared = subprocess.run(args + ["--allow-running", "--shortcut-only"], text=True, capture_output=True)
            self.assertEqual(prepared.returncode, 0, prepared.stderr)
            self.assertTrue((config / "shortcuts.vdf").is_file())
            self.assertEqual((config / "localconfig.vdf").read_bytes(), source)
            self.assertFalse(list(config.glob("localconfig.vdf.gloomhavenvr-backup-*")))
            self.assertEqual(subprocess.run(args + ["--needs-update"], text=True,
                                            capture_output=True).stdout, "yes\n")
            self.assertEqual(subprocess.run(args + ["--needs-update", "--shortcut-only"], text=True,
                                            capture_output=True).stdout, "no\n")

    def test_pending_live_session_rejects_stale_disk_success_and_cached_flush(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            config = base / "Steam/userdata/123/config"
            config.mkdir(parents=True)
            local = config / "localconfig.vdf"
            stale = localconfig("PROTON_LOG=1 %command% -windowed")
            ready = module.patch_localconfig(stale)
            local.write_bytes(ready)  # Old live setup's apparently green disk readback.
            game = base / "game"
            game.mkdir()
            (game / "GH.exe").touch()
            proc = base / "proc/222"
            proc.mkdir(parents=True)
            (proc / "comm").write_text("steam\n")
            boot_id = proc.parent / "sys/kernel/random/boot_id"
            boot_id.parent.mkdir(parents=True)
            boot_id.write_text("00000000-0000-0000-0000-000000000001")

            def start(ticks):
                (proc / "stat").write_text("222 (steam) S " + "0 " * 18 + str(ticks))

            start(100)
            guard = base / "pending.json"
            args = [sys.executable, str(HELPER), "--steam-root", str(base / "Steam"),
                    "--game-path", str(game), "--proc-root", str(proc.parent)]
            record = subprocess.run(args + ["--record-running-setup", str(guard)], text=True, capture_output=True)
            self.assertEqual(record.returncode, 0, record.stderr)
            self.assertEqual(json.loads(guard.read_text())["processes"], {"222": "100"})
            check = args + ["--check-launch-options", "--pending-setup", str(guard)]
            before = subprocess.run(check, text=True, capture_output=True)
            self.assertEqual(before.returncode, 3)
            self.assertIn("native Steam save", before.stderr)
            self.assertEqual(local.read_bytes(), ready)
            local.write_bytes(stale)  # Steam flushes its OLD in-memory state after setup dies.
            start(200)  # Even PID reuse is a new Steam client lifetime.
            flushed = subprocess.run(check, text=True, capture_output=True)
            self.assertEqual(flushed.returncode, 3)
            self.assertIn("missing or stale", flushed.stderr)
            self.assertIn('WINEDLLOVERRIDES="winhttp=n,b" PROTON_LOG=1 %command% -windowed', flushed.stderr)
            local.write_bytes(ready)  # Native Steam UI save survived the restart.
            complete = subprocess.run(check, text=True, capture_output=True)
            self.assertEqual(complete.returncode, 0, complete.stderr)
            self.assertEqual(local.read_bytes(), ready)
            self.assertFalse((config / "shortcuts.vdf").exists())
            # A full Frame power restart can reuse the same PID/start ticks.
            start(100)
            again = subprocess.run(check, text=True, capture_output=True)
            self.assertEqual(again.returncode, 3)
            boot_id.write_text("00000000-0000-0000-0000-000000000002")
            rebooted = subprocess.run(check, text=True, capture_output=True)
            self.assertEqual(rebooted.returncode, 0, rebooted.stderr)
            boot_id.write_text("00000000-0000-0000-0000-000000000001")
            (proc / "stat").unlink()
            incomplete = subprocess.run(args + ["--record-running-setup", str(guard)], text=True, capture_output=True)
            self.assertEqual(incomplete.returncode, 0, incomplete.stderr)
            self.assertEqual(json.loads(guard.read_text())["processes"], {"222": "unknown"})
            start(100)
            boot_id.write_text("00000000-0000-0000-0000-000000000002")
            for interrupted in ({"222": "unknown"}, {}):
                receipt = json.loads(guard.read_text())
                receipt["processes"] = interrupted
                guard.write_text(json.dumps(receipt))
                # A boot change is independent proof even with lost proc data.
                recovered = subprocess.run(check, text=True, capture_output=True)
                self.assertEqual(recovered.returncode, 0, recovered.stderr)

    def test_missing_or_corrupt_session_receipts_fail_closed(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            config = base / "Steam/userdata/123/config"
            config.mkdir(parents=True)
            local = config / "localconfig.vdf"
            local.write_bytes(module.patch_localconfig(localconfig()))
            game = base / "game"
            game.mkdir()
            (game / "GH.exe").touch()
            proc = base / "empty-proc"
            proc.mkdir()
            guard = base / "pending.json"
            args = [sys.executable, str(HELPER), "--steam-root", str(base / "Steam"),
                    "--game-path", str(game), "--proc-root", str(proc)]
            record = subprocess.run(args + ["--record-running-setup", str(guard)], text=True, capture_output=True)
            self.assertEqual(record.returncode, 0, record.stderr)
            for receipt in (guard.read_bytes(), b"{broken", b'{"schema":1,"processes":{"222":false}}'):
                guard.write_bytes(receipt)
                checked = subprocess.run(args + ["--check-launch-options", "--pending-setup", str(guard)],
                                         text=True, capture_output=True)
                self.assertEqual(checked.returncode, 3)
                self.assertIn("Rerun setup, save the native options", checked.stderr)

    def test_read_only_launch_check_and_required_line_do_not_need_shortcut_inputs(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            config = base / "Steam/userdata/123/config"
            config.mkdir(parents=True)
            local = config / "localconfig.vdf"
            original = localconfig("MANGOHUD=1 %command% --gloomhavenvr -windowed")
            local.write_bytes(original)
            game = base / "game"
            game.mkdir()
            (game / "GH.exe").touch()
            args = [sys.executable, str(HELPER), "--steam-root", str(base / "Steam"), "--game-path", str(game)]
            required = subprocess.run(args + ["--required-launch-options"], text=True, capture_output=True)
            self.assertEqual(required.returncode, 0, required.stderr)
            self.assertIn('WINEDLLOVERRIDES="winhttp=n,b"', required.stdout)
            self.assertEqual(required.stdout.count("%command%"), 1)
            self.assertNotIn("--gloomhavenvr", required.stdout)
            checked = subprocess.run(args + ["--check-launch-options"], text=True, capture_output=True)
            self.assertEqual(checked.returncode, 3)
            self.assertIn("Eigenschaften > Allgemein > Startoptionen", checked.stderr)
            self.assertEqual(local.read_bytes(), original)

    def test_most_recent_account_id_uses_steam_id64_conversion(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for account in ("123", "456"):
                config = root / "userdata" / account / "config"
                config.mkdir(parents=True)
                (config / "localconfig.vdf").write_bytes(localconfig(f"ACCOUNT_{account}=1 %command%"))
            steamid = module.STEAM_ID64_BASE + 456
            (root / "config").mkdir()
            (root / "config/loginusers.vdf").write_text(
                f'"users" {{ "{steamid}" {{ "MostRecent" "1" }} }}')
            self.assertEqual(module.select_account(root, None).name, "456")
            game = root / "game"
            game.mkdir()
            (game / "GH.exe").touch()
            args = [sys.executable, str(HELPER), "--steam-root", str(root), "--game-path", str(game),
                    "--required-launch-options"]
            current = subprocess.run(args, text=True, capture_output=True)
            self.assertEqual(current.returncode, 0, current.stderr)
            self.assertIn("ACCOUNT_456=1", current.stdout)
            self.assertNotIn("ACCOUNT_123=1", current.stdout)
            (root / "config/loginusers.vdf").write_text(
                f'"users" {{ "{module.STEAM_ID64_BASE + 123}" {{ "MostRecent" "1" }} }}')
            switched = subprocess.run(args, text=True, capture_output=True)
            self.assertEqual(switched.returncode, 0, switched.stderr)
            self.assertIn("ACCOUNT_123=1", switched.stdout)
            for account in ("123", "456"):
                self.assertEqual((root / "userdata" / account / "config/localconfig.vdf").read_bytes(),
                                 localconfig(f"ACCOUNT_{account}=1 %command%"))


if __name__ == "__main__":
    unittest.main()
