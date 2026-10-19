#!/usr/bin/env python3
"""Focused synthetic Steam configuration checks for the Frame setup helper."""

import importlib.util
import contextlib
import hashlib
import os
import re
import shlex
import shutil
import signal
import socket
import time
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
HELPER = ROOT / "scripts/steam-frame-config.py"
SPEC = importlib.util.spec_from_file_location("steam_frame_config", HELPER)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = module
SPEC.loader.exec_module(module)


def localconfig(options: str | None = None) -> bytes:
    value = f'"LaunchOptions" {json.dumps(options)}' if options is not None else ""
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

    def test_launch_check_accepts_equivalent_whitespace_and_override_quotes(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            config = base / "Steam/userdata/123/config"
            config.mkdir(parents=True)
            local = config / "localconfig.vdf"
            game = base / "game"
            game.mkdir()
            (game / "GH.exe").touch()
            args = [sys.executable, str(HELPER), "--steam-root", str(base / "Steam"),
                    "--game-path", str(game), "--check-launch-options"]
            valid = [
                '  WINEDLLOVERRIDES="winhttp=n,b" %command%  ',
                "WINEDLLOVERRIDES='winhttp=n,b' %command%",
                'WINEDLLOVERRIDES=winhttp=n,b %command%',
                "PROTON_LOG=1  WINEDLLOVERRIDES='d3d11=n;winhttp=n,b'  %command% -windowed",
            ]
            for options in valid:
                with self.subTest(options=options):
                    original = localconfig(options)
                    local.write_bytes(original)
                    checked = subprocess.run(args, text=True, capture_output=True)
                    self.assertEqual(checked.returncode, 0, checked.stderr)
                    self.assertEqual(local.read_bytes(), original)

    def test_launch_check_refuses_missing_wrong_or_misplaced_environment(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            config = base / "Steam/userdata/123/config"
            config.mkdir(parents=True)
            local = config / "localconfig.vdf"
            game = base / "game"
            game.mkdir()
            (game / "GH.exe").touch()
            args = [sys.executable, str(HELPER), "--steam-root", str(base / "Steam"),
                    "--game-path", str(game)]
            invalid = [
                "%command% -windowed",
                'WINEDLLOVERRIDES="winhttp=b" %command%',
                'WINEDLLOVERRIDES="winhttp=n,b" -windowed',
                '%command% WINEDLLOVERRIDES="winhttp=n,b" -windowed',
                'WINEDLLOVERRIDES="winhttp=n,b" "%command%"',
                'WINEDLLOVERRIDES="winhttp=n,b" %command% %command%',
                'WINEDLLOVERRIDES="winhttp=n,b %command%',
                'WINEDLLOVERRIDES="winhttp=n,b" %command% ; echo unsafe-wrapper',
                'gamescope WINEDLLOVERRIDES="winhttp=n,b" %command%',
                'WINEDLLOVERRIDES="winhttp=n,b" # %command%',
                'WINEDLLOVERRIDES="winhttp=n,b" %command% `unterminated',
                'WINEDLLOVERRIDES="winhttp=n,b" %command% "--gloomhavenvr"',
            ]
            for options in invalid:
                with self.subTest(options=options):
                    original = localconfig(options)
                    local.write_bytes(original)
                    checked = subprocess.run(args + ["--check-launch-options"], text=True, capture_output=True)
                    self.assertNotEqual(checked.returncode, 0)
                    self.assertIn("repair the original game's launch options automatically", checked.stderr)
                    self.assertEqual(local.read_bytes(), original)
            local.write_bytes(localconfig('%command% WINEDLLOVERRIDES="winhttp=n,b" -windowed'))
            suggested = subprocess.run(args + ["--required-launch-options"], text=True, capture_output=True)
            self.assertEqual(suggested.returncode, 0, suggested.stderr)
            self.assertEqual(suggested.stdout.strip(), 'WINEDLLOVERRIDES="winhttp=n,b" %command%  -windowed')
            self.assertTrue(module.valid_launch_options(suggested.stdout, suggested.stdout))

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
            (proc / "stat").write_text("222 (steamwebhelper) S " + "0 " * 18 + "100")
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
            self.assertIn("automatic Frame setup", prepared.stderr)
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
            self.assertIn("automatic managed setup", before.stderr)
            self.assertEqual(local.read_bytes(), ready)
            local.write_bytes(stale)  # Steam flushes its OLD in-memory state after setup dies.
            start(200)  # Even PID reuse is a new Steam client lifetime.
            flushed = subprocess.run(check, text=True, capture_output=True)
            self.assertEqual(flushed.returncode, 3)
            self.assertIn("missing or stale", flushed.stderr)
            self.assertEqual(local.read_bytes(), stale)
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
                self.assertIn("Rerun GloomhavenVR Steam Frame setup", checked.stderr)

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
            self.assertIn("Steam Frame Setup im Desktop-Modus erneut ausführen", checked.stderr)
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


class ManagedRequestTest(unittest.TestCase):
    """Generation/account/readback contract without a graphical Steam session."""

    def setUp(self):
        evidence=ROOT/'.planning/debug/frame-managed-request';evidence.mkdir(parents=True,exist_ok=True)
        self.base=Path(tempfile.mkdtemp(prefix='run-',dir=evidence));self.calls=[]
        self.addCleanup(self._cleanup)
        self.steam=self.base/'Steam';self.config=self.steam/'userdata/123/config'
        self.config.mkdir(parents=True);self.local=self.config/'localconfig.vdf'
        self.local.write_bytes(localconfig('PROTON_LOG=1 %command% -windowed'))
        self.game=self.base/'game';self.game.mkdir();(self.game/'GH.exe').touch()
        self.proc=self.base/'proc';self.proc.mkdir()
        boot=self.proc/'sys/kernel/random/boot_id';boot.parent.mkdir(parents=True)
        boot.write_text('00000000-0000-0000-0000-000000000001')
        self._client('222',100)
        self.job=self.base/'managed.json'
        self.args=[sys.executable,str(HELPER),'--steam-root',str(self.steam),'--game-path',str(self.game),
                   '--account-id','123','--proc-root',str(self.proc)]
        for name in ('launcher','icon','logo','portrait','header','hero'):
            path=self.base/(name+'.input');path.write_bytes(b'artwork')
            self.args+=['--'+name,str(path)]

    def _client(self,pid,ticks):
        path=self.proc/pid;path.mkdir(exist_ok=True)
        (path/'comm').write_text('steam\n')
        (path/'stat').write_text(pid+' (steam) S '+'0 '*18+str(ticks))

    def _run(self,args,label):
        result=subprocess.run(args,text=True,capture_output=True,timeout=10,env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1'))
        (self.base/(label+'.stdout')).write_text(result.stdout);(self.base/(label+'.stderr')).write_text(result.stderr)
        self.calls.append({'label':label,'args':args,'returncode':result.returncode})
        (self.base/'calls.json').write_text(json.dumps(self.calls,indent=2)+'\n')
        return result

    def _queue(self,label='queue'):
        result=self._run(self.args+['--queue-managed-setup',str(self.job)],label)
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        job=json.loads(self.job.read_text());self.assertEqual(job['schema'],2);self.assertEqual(job['phase'],'pending')
        return job

    def _mode(self,mode,label):
        return self._run([sys.executable,str(HELPER),'--'+mode+'-managed-setup',str(self.job),'--proc-root',str(self.proc)],label)

    def _offline_apply(self):
        shutil.rmtree(self.proc/'222')
        result=self._mode('apply','apply')
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        job=json.loads(self.job.read_text());self.assertEqual(job['phase'],'applied')
        self._client('333',200)
        return job

    def _cleanup(self):
        sys.path.insert(0,str(ROOT/'scripts'))
        from town_run_retention import cleanup_completed_run
        log=self.base/'completion.log';log.write_text('evidence: '+str(self.base)+'\ncompleted '+self.id()+'\n')
        result=self._outcome.result;failures=result.failures+result.errors if result is not None else []
        code=int(any(test is self for test,_ in failures))
        (self.base/'cleanup.json').write_text(json.dumps(cleanup_completed_run('frame-managed-request',code,log,ROOT,process_reaped=True),indent=2)+'\n')

    def test_queue_apply_fresh_cache_and_exact_generation(self):
        before=self.local.read_bytes();queued=self._queue()
        self.assertEqual(self.local.read_bytes(),before)
        pending=self._mode('verify','pending-verify');self.assertNotEqual(pending.returncode,0)
        applied=self._offline_apply();self.assertEqual(applied['generation'],queued['generation'])
        verified=self._mode('verify','fresh-verify');self.assertEqual(verified.returncode,0,verified.stderr)
        wrong=self._run([sys.executable,str(HELPER),'--verify-managed-setup',str(self.job),'--proc-root',str(self.proc),'--expected-generation','f'*32],'wrong-foreground-generation')
        self.assertNotEqual(wrong.returncode,0)
        self.assertIn(b'PROTON_LOG=1',self.local.read_bytes())
        self.assertIn(b'"999" { "LaunchOptions" "-unrelated" }',self.local.read_bytes())
        # A forged/old result may never certify a different queued generation.
        applied['result']['generation']='0'*32;self.job.write_text(json.dumps(applied))
        stale=self._mode('verify','stale-result');self.assertNotEqual(stale.returncode,0)
        self.assertNotIn('complete',stale.stdout.lower())

    def test_live_unknown_census_and_missing_inputs_fail_without_false_success(self):
        before=self.local.read_bytes();self._queue()
        busy=self._mode('apply','still-running');self.assertNotEqual(busy.returncode,0)
        self.assertEqual(self.local.read_bytes(),before)
        self._queue('requeue-after-busy');(self.proc/'222/stat').unlink()
        unknown=self._mode('apply','unknown-lifetime');self.assertNotEqual(unknown.returncode,0)
        self.assertEqual(self.local.read_bytes(),before)
        # Queue again with identifiable old client, then remove the game after
        # shutdown. The durable failure must remain recoverable by a new queue.
        self._client('222',100);failed=self._queue('requeue-missing-game')
        shutil.rmtree(self.proc/'222');(self.game/'GH.exe').unlink()
        missing=self._mode('apply','missing-game');self.assertNotEqual(missing.returncode,0)
        self.assertEqual(json.loads(self.job.read_text())['phase'],'failed')
        self.assertEqual(self.local.read_bytes(),before)
        (self.game/'GH.exe').touch();self._client('222',101)
        retry=self._queue('requeue-restored');self.assertNotEqual(retry['generation'],failed['generation'])
        self._offline_apply();self.assertEqual(self._mode('verify','restored-verify').returncode,0)

    def test_applied_semantics_reject_removed_options_and_account_switch(self):
        self._queue();self._offline_apply()
        self.assertEqual(self._mode('verify','ready').returncode,0)
        ready=self.local.read_bytes();self.local.write_bytes(localconfig('%command%'))
        missing=self._mode('verify','removed-options');self.assertNotEqual(missing.returncode,0)
        self.local.write_bytes(ready)
        other=self.steam/'userdata/456/config';other.mkdir(parents=True)
        (other/'localconfig.vdf').write_bytes(module.patch_localconfig(localconfig()))
        logins=self.steam/'config';logins.mkdir()
        (logins/'loginusers.vdf').write_text(f'"users" {{ "{module.STEAM_ID64_BASE+456}" {{ "MostRecent" "1" }} }}')
        account=self._mode('verify','wrong-current-account');self.assertNotEqual(account.returncode,0)

    def test_nonregular_or_dangling_inputs_fail_without_hanging_or_writes(self):
        before=self.local.read_bytes();self._queue();shutil.rmtree(self.proc/'222')
        icon=self.base/'icon.input';icon.unlink();os.mkfifo(icon)
        failed=self._mode('apply','fifo-artwork')
        self.assertNotEqual(failed.returncode,0)
        self.assertEqual(self.local.read_bytes(),before)
        self.assertEqual(json.loads(self.job.read_text())['phase'],'failed')
        self.job.unlink();self.job.symlink_to(self.base/'absent-request.json')
        dangling=self._mode('apply','dangling-request')
        self.assertNotEqual(dangling.returncode,0)
        # The unguarded options are already correct: only a real pending-guard
        # failure can block this launch, rather than unrelated missing options.
        ready=module.patch_localconfig(before);self.local.write_bytes(ready)
        args=[sys.executable,str(HELPER),'--steam-root',str(self.steam),'--game-path',str(self.game),
              '--check-launch-options','--pending-setup',str(self.job),'--proc-root',str(self.proc)]
        launch=self._run(args,'dangling-launch-guard')
        self.assertNotEqual(launch.returncode,0)
        self.job.unlink()
        unguarded=self._run(args,'ready-without-pending-guard')
        self.assertEqual(unguarded.returncode,0,unguarded.stderr)
        self.assertEqual(self.local.read_bytes(),ready)

    def test_missing_job_noop_and_request_mutation_cannot_write(self):
        missing=self._mode('apply','no-job');self.assertEqual(missing.returncode,0,missing.stderr)
        before=self.local.read_bytes();job=self._queue()
        job['request']['account_id']='456';self.job.write_text(json.dumps(job))
        shutil.rmtree(self.proc/'222')
        changed=self._mode('apply','tampered-request');self.assertNotEqual(changed.returncode,0)
        self.assertEqual(self.local.read_bytes(),before)


# This client owns an independent cached VDF image. It never calls the production
# parser/patcher: shutdown flushes the cached bytes, and only a new process reloads
# disk. The manager executes the persisted production ExecStartPre after reaping
# the old client. A successful CLI invocation alone changes neither cache nor disk.
FAKE_MANAGER = r"""
import ctypes,json,os,re,shlex,signal,socket,subprocess,sys,time
from pathlib import Path

base=Path(sys.argv[1]); mode=sys.argv[2]
local=Path(os.environ['GHVR_TEST_LOCALCONFIG'])
state_path=base/'manager-state.json'
def save(data):
    temporary=state_path.with_suffix('.new');temporary.write_text(json.dumps(data));temporary.replace(state_path)
def event(value):
    with (base/'manager-events.log').open('a') as f:f.write(value+'\n')
if mode=='client':
    ctypes.CDLL(None).prctl(15,b'steam',0,0,0)
    cached=local.read_bytes()
    (base/'cached-client.vdf').write_bytes(cached)
    (base/'client-ready').write_text(str(os.getpid()))
    def stop(*_):
        # A user's most recent edit belongs to Steam's cache until shutdown.
        edit=base/'cache-on-stop.vdf'
        local.write_bytes(edit.read_bytes() if edit.exists() else cached)
        edit.unlink(missing_ok=True)
        event('old-cache-flushed');raise SystemExit(0)
    signal.signal(signal.SIGTERM,stop)
    while True:signal.pause()

client=None
running=True
restart_requested=False

def start_client():
    global client
    (base/'client-ready').unlink(missing_ok=True)
    client=subprocess.Popen([sys.executable,__file__,str(base),'client'])
    for _ in range(200):
        if (base/'client-ready').exists():break
        if client.poll() is not None:raise RuntimeError('fake client died')
        time.sleep(.01)
    else:raise RuntimeError('fake client readiness timed out')
    save({'pid':client.pid,'phase':'active'})
    event('new-cache-loaded')

def stop_client():
    global client
    if client is not None:
        client.terminate();client.wait(timeout=5);client=None
        event('old-client-reaped')

def startup_hook():
    dropin=Path(os.environ['XDG_CONFIG_HOME'])/'systemd/user/steam.service.d/90-gloomhavenvr-setup.conf'
    if not dropin.exists():return
    commands=[line.split('=',1)[1].strip() for line in dropin.read_text().splitlines() if line.startswith('ExecStartPre=')]
    for line in commands:
        if not line:raise RuntimeError('owned hook reset existing ExecStartPre')
        # Only the documented nonfatal command prefix is accepted here. Decode
        # systemd's literal specifier/environment escaping after argument split.
        if not line.startswith('-'):raise RuntimeError('hook can block Steam')
        args=shlex.split(line[1:])
        args=[re.sub(r'\\x([0-9a-fA-F]{2})',lambda m:chr(int(m[1],16)),a).replace('%%','%').replace('$$','$') for a in args]
        event('startup-hook-begin')
        if (base/'omit-hook').exists():event('control-hook-omitted');return
        with (base/'startup-hook.log').open('a') as f:
            result=subprocess.run(args,stdout=f,stderr=subprocess.STDOUT,timeout=20)
        event('startup-hook-end:'+str(result.returncode))

start_client()
server=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
server.bind('\0ghvr-fixture-'+base.name+'-'+str(os.getpid()));server.listen(8);server.settimeout(.1)
(base/'manager-address').write_text('ghvr-fixture-'+base.name+'-'+str(os.getpid()))
(base/'manager-ready').touch()
try:
    while running:
        try:connection,_=server.accept()
        except socket.timeout:continue
        with connection:
            request=json.loads(connection.recv(65536))
            op=request['op'];answer={'code':0,'stdout':''}
            if op=='show':
                pid=client.pid
                cgroups=Path('/proc/'+str(pid)+'/cgroup').read_text().splitlines()
                group=cgroups[0].split(':',2)[2]
                if os.environ.get('GHVR_TEST_CGROUP_ADAPTER')=='1' and group=='/':group='/fixture/steam.service'
                if (base/'foreign-cgroup').exists():group='/different-user-service'
                if (base/'root-cgroup').exists():group='/'
                answer['stdout']='LoadState=loaded\nActiveState=active\nMainPID='+str(pid)+'\nControlGroup='+group+'\nTimeoutStartUSec=90s\nKillMode=control-group\n'
                if (base/'unsafe-kill-mode').exists():answer['stdout']=answer['stdout'].replace('KillMode=control-group','KillMode=process')
                if (base/'short-start-budget').exists():answer['stdout']=answer['stdout'].replace('TimeoutStartUSec=90s','TimeoutStartUSec=10s')
                if (base/'bus-unavailable').exists():answer={'code':1,'stdout':'user manager unavailable'}
            elif op=='daemon-reload':event('daemon-reload')
            elif op=='is-active':answer['stdout']='active\n'
            elif op=='restart':restart_requested=True;event('service-restart-requested')
            elif op=='stop':running=False
            else:answer={'code':1,'stdout':'unsupported mock operation: '+op}
            connection.sendall(json.dumps(answer).encode())
        if restart_requested:
            restart_requested=False
            if (base/'kill-installer').exists():
                os.kill(request['caller'],signal.SIGTERM)
                event('installer-terminal-lost')
            # Delay gives the foreground polling path an actual pending window.
            time.sleep(.15)
            stop_client()
            if (base/'remove-game-before-hook').exists():
                Path(os.environ['GHVR_TEST_GAME'],'GH.exe').unlink(missing_ok=True)
            startup_hook()
            start_client()
finally:
    stop_client();server.close()
"""

FAKE_SYSTEMCTL = r"""#!/usr/bin/env python3
import json,os,socket,sys
from pathlib import Path
base=Path(os.environ['GHVR_TEST_MANAGER'])
args=sys.argv[1:]
ops=[a for a in args if a in ('show','daemon-reload','is-active','restart')]
if len(ops)!=1:raise SystemExit('unsupported systemctl args: '+repr(args))
with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as c:
    c.connect('\0'+(base/'manager-address').read_text())
    c.sendall(json.dumps({'op':ops[0],'caller':os.getppid()}).encode())
    data=b''
    while True:
        block=c.recv(65536)
        if not block:break
        data+=block
answer=json.loads(data);print(answer['stdout'],end='');raise SystemExit(answer['code'])
"""


class ManagedSteamSetupTest(unittest.TestCase):
    """Production installer, actual process census, persisted hook and cached client."""

    def setUp(self):
        evidence=ROOT/'.planning/debug/frame-managed-installer'
        evidence.mkdir(parents=True,exist_ok=True)
        self.base=Path(tempfile.mkdtemp(prefix='run-',dir=evidence))
        self.manager=None
        self.manager_log=None
        self.addCleanup(self._cleanup)
        home_segment='home % $HOME ${HOME} back\\slash \"quote\"' if self._testMethodName=='test_generated_hook_preserves_literal_paths' else 'home with spaces'
        self.home=self.base/home_segment
        self.data=self.home/'.local/share'
        self.config_home=self.home/'.config'
        self.steam=self.data/'Steam'
        self.game=self.steam/'steamapps/common/Gloomhaven'
        self.config=self.steam/'userdata/123/config'
        self.setup=self.game/'BepInEx/plugins/GloomhavenVR/FrameSetup'
        self.local=self.config/'localconfig.vdf'
        self.bin=self.base/'bin'
        for directory in (self.config,self.setup,self.bin,self.game/'GH_Data',self.game/'BepInEx/patchers/GloomhavenVR'):
            directory.mkdir(parents=True,exist_ok=True)
        (self.game/'GH.exe').touch()
        (self.game/'GH_Data/boot.config').write_text('wait-for-native-debugger=0\n')
        (self.game/'BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll').touch()
        self.local.write_bytes(localconfig('MANGOHUD=1 %command% -windowed'))
        self.vendor_dropin=self.config_home/'systemd/user/steam.service.d/20-existing.conf'
        self.vendor_dropin.parent.mkdir(parents=True,exist_ok=True)
        self.vendor_bytes=b'[Service]\nEnvironment=KEEP=1\nExecStartPre=/usr/bin/true\n'
        self.vendor_dropin.write_bytes(self.vendor_bytes)
        for name in ('install-steam-frame.sh','steam-frame-config.py','frame-boot-config.py'):
            shutil.copy2(ROOT/'scripts'/name,self.setup/name)
        artwork=self.setup/'SteamArtwork';artwork.mkdir()
        for name in ('library_600x900.png','library_header.png','library_hero.png','logo.png','icon.png'):
            (artwork/name).write_bytes(b'fixture artwork '+name.encode())
        (self.bin/'steam').write_text('#!/usr/bin/env python3\nimport json,os,sys\nfrom pathlib import Path\nPath(os.environ["GHVR_TEST_MANAGER"],"steam-cli.json").write_text(json.dumps(sys.argv[1:]))\n')
        (self.bin/'systemctl').write_text(FAKE_SYSTEMCTL)
        for name in ('steam','systemctl'):(self.bin/name).chmod(0o755)
        self.env=dict(os.environ,HOME=str(self.home),XDG_DATA_HOME=str(self.data),XDG_CONFIG_HOME=str(self.config_home),
                      GHVR_TEST_MANAGER=str(self.base),GHVR_TEST_LOCALCONFIG=str(self.local),GHVR_TEST_GAME=str(self.game),
                      DBUS_SESSION_BUS_ADDRESS='unix:path='+str(self.base/'test-user-bus'),PYTHONDONTWRITEBYTECODE='1',
                      PATH=str(self.bin)+os.pathsep+os.environ['PATH'])
        self.env.pop('DISPLAY',None);self.env.pop('WAYLAND_DISPLAY',None)
        self.calls=[]
        # Some CI containers place every test process in cgroup '/'. A real
        # user steam.service cannot own that root. Adapt only this read boundary
        # in a private fixture copy; PID, UID, lifetimes and census stay actual.
        original=(self.setup/'install-steam-frame.sh').read_text()
        (self.setup/'install-steam-frame.production.sh').write_text(original)
        host_rows=Path('/proc/self/cgroup').read_text().splitlines()
        root_group=any(row.split(':',2)[2]=='/' for row in host_rows if row.split(':',2)[1] in {'','name=systemd'})
        if root_group:
            needle='        rows = (Path("/proc") / pid / "cgroup").read_text().splitlines()'
            self.assertEqual(original.count(needle),1)
            replacement=needle+'\n        rows = [row[:-1] + "/fixture/steam.service" if row.endswith(":/") else row for row in rows]'
            (self.setup/'install-steam-frame.sh').write_text(original.replace(needle,replacement))
            self.env['GHVR_TEST_CGROUP_ADAPTER']='1'
        inputs={'cgroup_boundary_adapted':root_group,'production_installer_sha256':hashlib.sha256(original.encode()).hexdigest(),
                'fixture_installer_sha256':hashlib.sha256((self.setup/'install-steam-frame.sh').read_bytes()).hexdigest(),
                'helper_sha256':hashlib.sha256((self.setup/'steam-frame-config.py').read_bytes()).hexdigest(),
                'test_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
        (self.base/'source-inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
        self._run_installer('offline-prepare',require_success=True)
        self.launcher=self.data/'GloomhavenVR/launch-steam-frame.sh'
        self.job=self.data/'GloomhavenVR/SteamSetup/managed-setup.json'
        # Match the report exactly: the shortcut is already correct, but the
        # original app's override has subsequently been removed in Steam.
        self.local.write_bytes(localconfig('MANGOHUD=1 %command% -windowed'))
        self.shortcut_before=(self.config/'shortcuts.vdf').read_bytes()
        self._start_manager()

    def _cleanup(self):
        if self.manager is not None:
            with contextlib.suppress(OSError,ValueError):self._manager_command('stop')
            try:self.manager.wait(timeout=8)
            except subprocess.TimeoutExpired:
                # The manager owns the child; preserve logs after terminating it.
                state=self._state();pid=state.get('pid')
                if type(pid) is int and pid>1:
                    with contextlib.suppress(OSError,ValueError,IndexError):
                        process=Path('/proc')/str(pid)
                        parent=int((process/'stat').read_text().rsplit(')',1)[1].split()[1])
                        if (parent==self.manager.pid and process.stat().st_uid==os.getuid()
                                and (process/'comm').read_text().strip()=='steam'):
                            os.kill(pid,signal.SIGTERM)
                self.manager.terminate();self.manager.wait(timeout=5)
        if self.manager_log is not None:self.manager_log.close()
        sys.path.insert(0,str(ROOT/'scripts'))
        from town_run_retention import cleanup_completed_run
        log=self.base/'completion.log'
        log.write_text('evidence: '+str(self.base)+'\ncompleted '+self.id()+'\n')
        result=self._outcome.result
        failures=result.failures+result.errors if result is not None else []
        status=int(any(test is self for test,_ in failures))
        receipt=cleanup_completed_run('frame-managed-installer',status,log,ROOT,process_reaped=True)
        (self.base/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')

    def _state(self):
        try:return json.loads((self.base/'manager-state.json').read_text())
        except (OSError,ValueError):return {}

    def _manager_command(self,op):
        with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as connection:
            connection.settimeout(3);connection.connect('\0'+(self.base/'manager-address').read_text())
            connection.sendall(json.dumps({'op':op,'caller':os.getpid()}).encode())
            return json.loads(connection.recv(65536))

    def _start_manager(self):
        script=self.base/'fake-manager.py';script.write_text(FAKE_MANAGER)
        self.manager_log=(self.base/'manager.log').open('w')
        self.manager=subprocess.Popen([sys.executable,str(script),str(self.base),'manager'],env=self.env,
                                      stdout=self.manager_log,stderr=subprocess.STDOUT)
        self._wait(lambda:(self.base/'manager-ready').exists(),'manager readiness')

    def _wait(self,predicate,message):
        for _ in range(400):
            if predicate():return
            if self.manager is not None and self.manager.poll() is not None:
                self.fail('manager exited: '+(self.base/'manager.log').read_text())
            time.sleep(.025)
        self.fail(message+' timed out; evidence '+str(self.base))

    def _run(self,args,label,**kwargs):
        result=subprocess.run(args,env=self.env,text=True,capture_output=True,timeout=40,**kwargs)
        (self.base/(label+'.stdout')).write_text(result.stdout)
        (self.base/(label+'.stderr')).write_text(result.stderr)
        self.calls.append({'label':label,'args':[str(a) for a in args],'returncode':result.returncode})
        (self.base/'calls.json').write_text(json.dumps(self.calls,indent=2)+'\n')
        return result

    def _run_installer(self,label,require_success=False):
        script='install-steam-frame.production.sh' if label=='root-cgroup' else 'install-steam-frame.sh'
        result=self._run(['bash',str(self.setup/script)],label)
        if require_success:self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        return result

    def _cached_options(self):
        raw=(self.base/'cached-client.vdf').read_text()
        match=re.search(r'"780290"\s*\{\s*"LaunchOptions"\s*("(?:\\.|[^"\\])*")',raw)
        return json.loads(match[1]) if match else ''

    def _assert_ready(self):
        options=self._cached_options()
        self.assertIn('WINEDLLOVERRIDES="winhttp=n,b"',options)
        self.assertEqual(options.count('%command%'),1)
        self.assertIn('MANGOHUD=1',options)
        self.assertIn('-windowed',options)
        self.assertNotIn('--gloomhavenvr',options)
        self.assertIn(b'"999" { "LaunchOptions" "-unrelated" }',self.local.read_bytes())
        self.assertEqual((self.config/'shortcuts.vdf').read_bytes(),self.shortcut_before)
        self.assertEqual(self.vendor_dropin.read_bytes(),self.vendor_bytes)
        job=json.loads(self.job.read_text())
        self.assertEqual(job['schema'],2);self.assertEqual(job['phase'],'applied')
        verify=self._run([sys.executable,str(self.setup/'steam-frame-config.py'),'--verify-managed-setup',str(self.job)],'verify')
        self.assertEqual(verify.returncode,0,verify.stdout+verify.stderr)
        launch=self._run([str(self.launcher)],'launch')
        self.assertEqual(launch.returncode,0,launch.stdout+launch.stderr)
        self.assertEqual(json.loads((self.base/'steam-cli.json').read_text()),['-applaunch','780290','--gloomhavenvr'])
        return job

    def test_managed_restart_updates_current_shortcut_missing_original_options(self):
        original_pid=self._state()['pid']
        # A cache edit made after the foreground queued the job must survive. It
        # becomes visible only when the old process flushes on managed shutdown.
        (self.base/'cache-on-stop.vdf').write_bytes(localconfig('MANGOHUD=1 %command% -windowed --latest-cache-edit'))
        result=self._run_installer('automatic-repair',require_success=True)
        self.assertIn('WINEDLLOVERRIDES=',self._cached_options(),'the new Steam client must cache the repaired ORIGINAL app options')
        self.assertNotEqual(self._state()['pid'],original_pid)
        self._assert_ready()
        self.assertIn('--latest-cache-edit',self._cached_options())
        self.assertIn('Setup state: complete (Steam service applied and verified the launch options).',result.stdout)
        events=(self.base/'manager-events.log').read_text().splitlines()
        self.assertLess(events.index('old-client-reaped'),events.index('startup-hook-begin'))
        self.assertLess(events.index('startup-hook-end:0'),len(events)-1)
        self.assertEqual(events[-1],'new-cache-loaded')
        # Complete jobs remain no-ops on later unrelated Steam starts.
        first=self.job.read_bytes();backups=list(self.config.glob('localconfig.vdf.gloomhavenvr-backup-*'))
        self._manager_command('restart')
        self._wait(lambda:(self.base/'manager-events.log').read_text().count('startup-hook-end:0')>=2,'second managed restart')
        self.assertEqual(self.job.read_bytes(),first)
        self.assertEqual(list(self.config.glob('localconfig.vdf.gloomhavenvr-backup-*')),backups)

    def test_terminal_loss_does_not_lose_persisted_startup_repair(self):
        (self.base/'kill-installer').touch()
        result=self._run_installer('terminal-loss')
        self.assertNotEqual(result.returncode,0)
        self._wait(lambda:self.job.exists() and json.loads(self.job.read_text()).get('phase')=='applied','persisted job application')
        self._wait(lambda:'WINEDLLOVERRIDES=' in self._cached_options(),'new cached options')
        self._assert_ready()
        self.assertIn('installer-terminal-lost',(self.base/'manager-events.log').read_text())

    def test_foreign_service_cgroup_fails_before_any_live_write(self):
        (self.base/'foreign-cgroup').touch()
        before=self.local.read_bytes()
        result=self._run_installer('foreign-service')
        self.assertNotEqual(result.returncode,0)
        self.assertEqual(self.local.read_bytes(),before)
        self.assertNotIn('WINEDLLOVERRIDES=',self._cached_options())
        self.assertNotIn('Setup state: complete',result.stdout)
        self.assertNotIn('service-restart-requested',(self.base/'manager-events.log').read_text())

    def test_generated_hook_preserves_literal_paths(self):
        self._run_installer('literal-service-paths',require_success=True)
        self._assert_ready()
        dropin=self.config_home/'systemd/user/steam.service.d/90-gloomhavenvr-setup.conf'
        source=dropin.read_text()
        self.assertIn('%%',source)
        self.assertIn('$$HOME',source)
        self.assertIn('$${HOME}',source)
        self.assertIn('ExecStartPre=-',source)
        self.assertNotIn('ExecStartPre=\n',source)
        if shutil.which('systemd-analyze'):
            unit=self.base/'gloomhavenvr-fixture.service'
            unit.write_text('[Service]\nType=oneshot\nExecStart=/usr/bin/true\n'+source)
            checked=self._run(['systemd-analyze','verify',str(unit)],'systemd-unit-parse')
            self.assertEqual(checked.returncode,0,checked.stdout+checked.stderr)

    def test_unsafe_service_restart_configuration_is_rejected(self):
        before=self.local.read_bytes()
        for marker in ('unsafe-kill-mode','short-start-budget','root-cgroup','bus-unavailable'):
            with self.subTest(marker=marker):
                flag=self.base/marker;flag.touch()
                result=self._run_installer(marker)
                flag.unlink()
                self.assertNotEqual(result.returncode,0)
                self.assertNotIn('Setup state: complete',result.stdout)
                self.assertEqual(self.local.read_bytes(),before)
        self.assertNotIn('service-restart-requested',(self.base/'manager-events.log').read_text())

    def test_control_old_disk_readback_and_forwarded_restart_cannot_repair_cache(self):
        # Replay the reported failure with the actual old write/readback path.
        module.backup_and_replace(self.local,module.patch_localconfig(self.local.read_bytes()))
        self.assertTrue(module.valid_launch_options(module.launch_options(self.local.read_bytes()),module.update_launch_options(module.launch_options(self.local.read_bytes()))))
        forwarded=self._run(['steam','-shutdown'],'forwarded-old-client-command')
        self.assertEqual(forwarded.returncode,0)
        self.assertNotIn('WINEDLLOVERRIDES=',self._cached_options())
        original_pid=self._state()['pid']
        self._manager_command('restart')
        self._wait(lambda:self._state().get('pid')!=original_pid,'control restart')
        self.assertNotIn(b'WINEDLLOVERRIDES=',self.local.read_bytes())
        self.assertNotIn('WINEDLLOVERRIDES=',self._cached_options())

    def test_failed_hook_is_nonfatal_to_steam_and_blocks_vr_until_retry(self):
        # Model an SD card removal after validation/queueing, before ExecStartPre.
        (self.base/'remove-game-before-hook').touch()
        old_pid=self._state()['pid']
        result=self._run_installer('game-lost-during-restart')
        self.assertNotEqual(result.returncode,0)
        self._wait(lambda:self._state().get('pid')!=old_pid,'Steam after failed hook')
        self.assertTrue(self.manager.poll() is None)
        self.assertNotIn('Setup state: complete',result.stdout)
        job=json.loads(self.job.read_text())
        self.assertEqual(job['phase'],'failed')
        launch=self._run([str(self.launcher)],'failed-job-launch')
        self.assertNotEqual(launch.returncode,0)
        self.assertFalse((self.base/'steam-cli.json').exists())
        (self.base/'remove-game-before-hook').unlink()
        (self.game/'GH.exe').touch()
        self._run_installer('retry-after-game-restored',require_success=True)
        repaired=self._assert_ready()
        self.assertNotEqual(repaired['generation'],job['generation'])




if __name__ == "__main__":
    unittest.main()
