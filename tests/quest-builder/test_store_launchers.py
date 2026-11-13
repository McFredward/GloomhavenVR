"""Installed provider discovery and shared interactive launch boundaries."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import types
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import store_launchers as launchers


class LauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def file(self, relative):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("test fixture; never executed")
        return path

    def test_windows_installed_registry_paths_for_each_provider(self):
        for provider, name, key, value in (
                ("steam", "steam.exe", r"Software\Valve\Steam", "SteamPath"),
                ("epic", "EpicGamesLauncher.exe", r"Software\Epic Games\EpicGamesLauncher", "InstallLocation"),
                ("gog", "GalaxyClient.exe", r"Software\GOG.com\GalaxyClient", "path")):
            executable = self.file(provider + "/" + name)
            def registry(requested, fields):
                return [str(executable.parent)] if requested == key and value in fields else []
            with self.subTest(provider=provider), mock.patch.object(launchers.sys, "platform", "win32"), \
                 mock.patch.object(launchers, "_registry_values", side_effect=registry), \
                 mock.patch.dict(os.environ, {}, clear=True), mock.patch.object(launchers, "_open_windows") as opened:
                found = launchers.discover(provider)
                self.assertTrue(found["available"])
                self.assertEqual(found["executable"], str(executable))
                self.assertEqual(found["arguments"], [])
                self.assertFalse(opened.called)
                result = launchers.launch(provider)
                opened.assert_called_once_with(str(executable))
                self.assertTrue(result["launched"])
                self.assertIn("checks still apply", result["message"]["en"])

    def test_windows_common_installed_locations_for_all_providers(self):
        relative = {"steam": "Steam/steam.exe",
                    "epic": "Epic Games/Launcher/Portal/Binaries/Win64/EpicGamesLauncher.exe",
                    "gog": "GOG Galaxy/GalaxyClient.exe"}
        for provider, path in relative.items():
            executable = self.file("Program Files (x86)/" + path)
            with self.subTest(provider=provider), mock.patch.object(launchers.sys, "platform", "win32"), \
                 mock.patch.object(launchers, "_registry_values", return_value=[]), \
                 mock.patch.dict(os.environ, {"ProgramFiles(x86)": str(self.root / "Program Files (x86)")}, clear=True):
                self.assertEqual(launchers.discover(provider)["executable"], str(executable))

    def test_epic_custom_installed_directory_uses_native_launcher_binary(self):
        executable = self.file("custom/Epic Launcher/Portal/Binaries/Win32/EpicGamesLauncher.exe")
        def registry(key, fields):
            return [str(self.root / "custom/Epic Launcher")] if key == r"Software\Epic Games\EpicGamesLauncher" and "InstalledDirectory" in fields else []
        with mock.patch.object(launchers.sys, "platform", "win32"), \
             mock.patch.object(launchers, "_registry_values", side_effect=registry), mock.patch.dict(os.environ, {}, clear=True):
            self.assertEqual(launchers.discover("epic")["executable"], str(executable))

    def test_windows_app_paths_accept_only_exact_executable_not_command(self):
        executable = self.file("steam.exe")
        with mock.patch.object(launchers.sys, "platform", "win32"), \
             mock.patch.dict(os.environ, {}, clear=True), \
             mock.patch.object(launchers, "_registry_values", return_value=['"' + str(executable) + '" --arbitrary-command']):
            self.assertFalse(launchers.discover("steam")["available"])
        with mock.patch.object(launchers.sys, "platform", "win32"), \
             mock.patch.object(launchers, "_registry_values", side_effect=lambda key, fields: [str(executable)] if "App Paths" in key else []):
            self.assertEqual(launchers.discover("steam")["executable"], str(executable))

    def test_windows_registry_reads_both_views_and_scopes(self):
        opened = []
        class Handle:
            def __enter__(self): return self
            def __exit__(self, *args): pass
        def open_key(hive, key, reserved, flags):
            opened.append((hive, key, flags))
            return Handle()
        registry = types.SimpleNamespace(HKEY_CURRENT_USER=1, HKEY_LOCAL_MACHINE=2, KEY_READ=4,
                                         KEY_WOW64_64KEY=8, KEY_WOW64_32KEY=16,
                                         OpenKey=open_key, QueryValueEx=lambda handle, field: ("fixture", 1))
        with mock.patch.dict(sys.modules, {"winreg": registry}):
            self.assertEqual(launchers._registry_values("fixed-key", ("Path",)), ["fixture"] * 4)
        self.assertEqual({(hive, flags) for hive, key, flags in opened}, {(1, 12), (1, 20), (2, 12), (2, 20)})

    def test_windows_uses_shell_execute_not_owned_subprocess(self):
        with mock.patch.object(os, "startfile", create=True) as execute, mock.patch.object(subprocess, "Popen") as owned:
            launchers._open_windows("installed/steam.exe")
        execute.assert_called_once_with("installed/steam.exe")
        owned.assert_not_called()

    def test_linux_steam_and_heroic_for_epic_and_gog_are_discovered_locally(self):
        steam, heroic = self.file("bin/steam"), self.file("bin/heroic")
        for provider, executable in (("steam", steam), ("epic", heroic), ("gog", heroic)):
            with self.subTest(provider=provider), mock.patch.object(launchers.sys, "platform", "linux"), \
                 mock.patch.object(launchers.shutil, "which", side_effect=lambda name: str(steam) if name == "steam" else str(heroic) if name == "heroic" else None), \
                 mock.patch.object(launchers, "_open_linux") as opened, \
                 mock.patch.dict(os.environ, {"DISPLAY": ":fixture"}, clear=True):
                self.assertEqual(launchers.discover(provider)["executable"], str(executable))
                self.assertTrue(launchers.launch(provider)["launched"])
                opened.assert_called_once_with(str(executable), [])

    def test_linux_flatpak_launch_requires_actual_installed_id(self):
        for provider, app_id in (("steam", "com.valvesoftware.Steam"), ("epic", "com.heroicgameslauncher.hgl"), ("gog", "com.heroicgameslauncher.hgl")):
            with self.subTest(provider=provider), mock.patch.object(launchers.sys, "platform", "linux"), \
                 mock.patch.object(launchers.shutil, "which", side_effect=lambda name: "/fixture/flatpak" if name == "flatpak" else None), \
                 mock.patch.object(launchers.Path, "home", return_value=self.root), \
                 mock.patch.object(launchers.subprocess, "run", return_value=types.SimpleNamespace(returncode=0, stdout="/installed/application")) as query:
                value = launchers.discover(provider)
                self.assertEqual(value["arguments"], ["run", app_id])
                self.assertEqual(query.call_args.args[0], ["/fixture/flatpak", "info", "--show-location", app_id])
                self.assertEqual(query.call_args.kwargs["timeout"], 3)
                self.assertFalse(query.call_args.kwargs["shell"])
                query.return_value = types.SimpleNamespace(returncode=1, stdout="")
                self.assertFalse(launchers.discover(provider)["available"])

    def test_uninstalled_client_is_bilingual_action_not_external_uri(self):
        with mock.patch.object(launchers.sys, "platform", "linux"), \
             mock.patch.object(launchers.shutil, "which", return_value=None), \
             mock.patch.object(launchers.Path, "home", return_value=self.root), \
             mock.patch.object(launchers, "_open_linux") as opened:
            for provider in ("steam", "epic", "gog"):
                with self.subTest(provider=provider), self.assertRaises(launchers.LauncherError) as error:
                    launchers.launch(provider)
                self.assertEqual(error.exception.reason, "not-installed")
                self.assertTrue(error.exception.messages["en"])
                self.assertTrue(error.exception.messages["de"])
            opened.assert_not_called()

    def test_linux_no_desktop_does_not_try_launch(self):
        with mock.patch.object(launchers, "discover", return_value={"available": True}), \
             mock.patch.object(launchers.sys, "platform", "linux"), mock.patch.dict(os.environ, {}, clear=True), \
             mock.patch.object(launchers, "_open_linux") as opened, self.assertRaises(launchers.LauncherError) as error:
            launchers.launch("steam")
        self.assertEqual(error.exception.reason, "desktop-required")
        opened.assert_not_called()

    def test_launch_failure_contains_local_diagnostics_and_guidance(self):
        with mock.patch.object(launchers.sys, "platform", "win32"), \
             mock.patch.object(launchers, "discover", return_value={"provider": "gog", "available": True, "launcher": "GOG GALAXY", "executable": "fixture.exe", "arguments": []}), \
             mock.patch.object(launchers, "_open_windows", side_effect=PermissionError("fixture access denied")), \
             self.assertRaises(launchers.LauncherError) as error:
            launchers.launch("gog")
        self.assertEqual(error.exception.reason, "launch-failed")
        self.assertIn("fixture access denied", error.exception.detail)
        self.assertIn("manually", error.exception.messages["en"])

    @unittest.skipUnless(sys.platform.startswith("linux"), "Native POSIX process lifetime fixture")
    def test_linux_detached_helper_is_reaped_without_owning_store_lifetime(self):
        output = self.root / "pid.json"
        command = "import json,os,time;open(" + repr(str(output)) + ",'w').write(json.dumps([os.getpid(),os.getpgrp()]));time.sleep(.05)"
        real = subprocess.Popen
        children = []
        def start(*args, **kwargs):
            child = real(*args, **kwargs)
            children.append(child)
            self.assertTrue(kwargs["start_new_session"])
            self.assertFalse(kwargs["shell"])
            return child
        with mock.patch.object(launchers.subprocess, "Popen", side_effect=start):
            launchers._open_linux(sys.executable, ["-I", "-c", command])
        child = children[0]
        self.addCleanup(lambda: child.wait(timeout=5))
        deadline = time.monotonic() + 5
        while child.returncode is None and time.monotonic() < deadline:
            time.sleep(.01)
        self.assertEqual(child.returncode, 0)
        pid, group = json.loads(output.read_text())
        self.assertEqual(pid, group)
        self.assertNotEqual(group, os.getpgrp())
        with self.assertRaises(ChildProcessError): os.waitpid(pid, os.WNOHANG)

    def test_isolated_packaged_module_has_no_builder_import_requirement(self):
        isolated = self.root / "store_launchers.py"
        isolated.write_bytes(Path(launchers.__file__).read_bytes())
        code = "import importlib.util; s=importlib.util.spec_from_file_location('isolated'," + repr(str(isolated)) + ");m=importlib.util.module_from_spec(s);s.loader.exec_module(m);assert set(m.PROVIDERS)=={'steam','epic','gog'}"
        run = subprocess.run([sys.executable, "-I", "-B", "-c", code], cwd=self.root, capture_output=True, text=True, timeout=5)
        self.assertEqual(run.returncode, 0, run.stderr)

    def test_provider_input_cannot_be_a_command_or_path(self):
        for provider in ("", "Steam", "steam://run/780290", "../../steam.exe", "epic;unsafe", None):
            with self.subTest(provider=provider), self.assertRaises(launchers.LauncherError): launchers.launch(provider)


if __name__ == "__main__": unittest.main()
