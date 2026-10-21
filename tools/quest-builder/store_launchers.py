"""Open an installed local store client without owning its interactive lifetime.

Launching a client is not evidence of an account, license or DLC entitlement.
Callers must repeat their original read-only ownership check afterwards. This
module deliberately imports only the standard library: the isolated Wizard and
Builder loaders both use the exact packaged file without global module aliases.
"""
from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import sys
import threading

PROVIDERS = {"steam": "Steam", "epic": "Epic Games Launcher", "gog": "GOG GALAXY"}


class LauncherError(RuntimeError):
    """A local desktop action with bilingual, credential-free recovery text."""
    def __init__(self, provider, reason, en, de, detail=""):
        super().__init__(en)
        self.provider, self.reason = provider, reason
        self.messages, self.detail = {"en": en, "de": de}, str(detail)[:2000]


def _provider(provider):
    if not isinstance(provider, str) or provider not in PROVIDERS:
        raise LauncherError("", "unsupported-provider", "Choose Steam, Epic or GOG.",
                            "Steam, Epic oder GOG auswählen.")
    return provider


def _file(path, names):
    """Registry fields are paths, never shell commands or caller-supplied argv."""
    if not isinstance(path, (str, Path)) or not str(path).strip():
        return None
    value = os.path.expandvars(str(path).strip().strip('"'))
    candidate = Path(value).expanduser()
    try:
        if candidate.name.lower() in names and candidate.is_file():
            return str(candidate.absolute())
    except OSError:
        pass
    return None


def _registry_values(key, names):
    # Both installer views matter on 64-bit Windows. No recursive registry walk.
    try:
        import winreg
    except ImportError:
        return []
    values = []
    for hive in (winreg.HKEY_CURRENT_USER, winreg.HKEY_LOCAL_MACHINE):
        for view in (getattr(winreg, "KEY_WOW64_64KEY", 0), getattr(winreg, "KEY_WOW64_32KEY", 0)):
            try:
                with winreg.OpenKey(hive, key, 0, winreg.KEY_READ | view) as handle:
                    for name in names:
                        try:
                            value, kind = winreg.QueryValueEx(handle, name)
                            if isinstance(value, str):
                                values.append(value)
                        except OSError:
                            pass
            except OSError:
                pass
    return values


def _windows(provider):
    executable = {"steam": "steam.exe", "epic": "EpicGamesLauncher.exe", "gog": "GalaxyClient.exe"}[provider]
    candidates = list(_registry_values("Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\" + executable, ("",)))
    if provider == "steam":
        candidates += _registry_values(r"Software\Valve\Steam", ("SteamExe",))
        roots = _registry_values(r"Software\Valve\Steam", ("SteamPath", "InstallPath"))
        relatives = ("Steam/steam.exe",)
    elif provider == "epic":
        roots = _registry_values(r"Software\Epic Games\EpicGamesLauncher", ("InstallLocation", "InstalledDirectory"))
        # Windows distributions have used both binary directories.
        relatives = ("Epic Games/Launcher/Portal/Binaries/Win64/EpicGamesLauncher.exe",
                     "Epic Games/Launcher/Portal/Binaries/Win32/EpicGamesLauncher.exe")
    else:
        roots = _registry_values(r"Software\GOG.com\GalaxyClient", ("path", "InstallLocation"))
        relatives = ("GOG Galaxy/GalaxyClient.exe", "GOG.com/GalaxyClient/GalaxyClient.exe")
    for root in roots:
        root = Path(os.path.expandvars(root.strip().strip('"')))
        candidates.append(str(root / executable))
        if provider == "epic":
            candidates.extend(str(root / "Portal/Binaries" / arch / executable) for arch in ("Win64", "Win32"))
    for key in ("ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA"):
        if os.environ.get(key):
            candidates.extend(str(Path(os.environ[key]) / relative) for relative in relatives)
    for candidate in candidates:
        found = _file(candidate, {executable.lower()})
        if found:
            return found, [], PROVIDERS[provider]
    return None


def _linux(provider):
    names = ("steam",) if provider == "steam" else ("heroic", "Heroic")
    for name in names:
        found = shutil.which(name)
        if found and Path(found).is_file():
            return str(Path(found).absolute()), [], "Steam" if provider == "steam" else "Heroic Games Launcher"
    if provider == "steam":
        for path in (Path.home() / ".local/share/Steam/steam.sh", Path.home() / ".steam/steam/steam.sh"):
            if path.is_file() and os.access(path, os.X_OK):
                return str(path.absolute()), [], "Steam"
    flatpak = shutil.which("flatpak")
    app_id = "com.valvesoftware.Steam" if provider == "steam" else "com.heroicgameslauncher.hgl"
    if flatpak:
        try:
            check = subprocess.run([flatpak, "info", "--show-location", app_id],
                                   stdin=subprocess.DEVNULL, capture_output=True, text=True,
                                   timeout=3, shell=False)
            if check.returncode == 0 and check.stdout.strip():
                return str(Path(flatpak).absolute()), ["run", app_id], "Steam" if provider == "steam" else "Heroic Games Launcher"
        except (OSError, subprocess.TimeoutExpired):
            pass
    return None


def discover(provider):
    """Bounded local installation discovery; never downloads or starts a client."""
    provider = _provider(provider)
    found = _windows(provider) if sys.platform == "win32" else _linux(provider) if sys.platform.startswith("linux") else None
    if found:
        executable, arguments, name = found
        return {"provider": provider, "available": True, "launcher": name,
                "executable": executable, "arguments": arguments,
                "message": {"en": name + " is installed.", "de": name + " ist installiert."}}
    name = PROVIDERS[provider]
    linux = sys.platform.startswith("linux") and provider != "steam"
    return {"provider": provider, "available": False, "launcher": name, "reason": "not-installed",
            "message": {"en": ("Install or open Heroic Games Launcher for this " + provider + " copy, then retry."
                               if linux else "Open or install " + name + " for this PC copy, then retry."),
                        "de": ("Heroic Games Launcher für diese " + provider + "-Version installieren oder öffnen und erneut versuchen."
                               if linux else name + " für diese PC-Version öffnen oder installieren und erneut versuchen.")}}


def _open_windows(executable):
    # ShellExecute starts a shared interactive application, outside the Builder's
    # process job. Closing the Wizard must not close a pre-existing store client.
    os.startfile(executable)


def _reap(child):
    child.wait()


def _open_linux(executable, arguments):
    child = subprocess.Popen([executable, *arguments], stdin=subprocess.DEVNULL,
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                             start_new_session=True, close_fds=True, shell=False)
    # Direct executable invocation has no temporary shell/poll loop. Reap a
    # launcher wrapper if it exits while its interactive application continues.
    threading.Thread(target=_reap, args=(child,), name="quest-store-launcher-reaper", daemon=True).start()


def launch(provider):
    """Open the fixed installed client; acceptance never means authentication."""
    result = discover(provider)
    if not result["available"]:
        raise LauncherError(provider, result["reason"], result["message"]["en"], result["message"]["de"])
    if sys.platform.startswith("linux") and not os.environ.get("DISPLAY") and not os.environ.get("WAYLAND_DISPLAY"):
        raise LauncherError(provider, "desktop-required", "Open the Builder in your Linux desktop session, then retry.",
                            "Den Builder in deiner Linux-Desktop-Sitzung öffnen und erneut versuchen.")
    try:
        if sys.platform == "win32":
            _open_windows(result["executable"])
        else:
            _open_linux(result["executable"], result["arguments"])
    except (OSError, RuntimeError) as error:
        raise LauncherError(provider, "launch-failed", "Could not open " + result["launcher"] + "; open it manually, sign in and retry.",
                            result["launcher"] + " konnte nicht geöffnet werden; manuell öffnen, anmelden und erneut versuchen.",
                            str(error)) from error
    return {**result, "launched": True, "message": {
        "en": result["launcher"] + " was requested. Sign in if prompted; the original account and ownership checks still apply.",
        "de": result["launcher"] + " wurde angefordert. Falls nötig anmelden; Konto und Besitz werden anschließend weiterhin geprüft."}}
