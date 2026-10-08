"""Desktop path selection shared by the Windows and Linux Wizard hosts."""
from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import sys

from state import WizardError


def editor_path(selected, *, windows=None):
    """Accept an Editor executable, its folder or a Hub version folder."""
    path = Path(selected).expanduser()
    if path.is_file():
        return str(path.resolve())
    name = "Unity.exe" if (os.name == "nt" if windows is None else windows) else "Unity"
    return str(next((candidate for candidate in (path / name, path / "Editor" / name)
                     if candidate.is_file()), path / name).resolve())


def linux_picker():
    if not sys.platform.startswith("linux") or not (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
        return None
    return next(((name, executable) for name in ("zenity", "kdialog")
                 if (executable := shutil.which(name))), None)


def browse_available():
    return os.name == "nt" or linux_picker() is not None


def browse_linux(kind):
    """Use the desktop's picker without shell interpolation or uploading files."""
    if kind not in ("game", "unity", "apk"):
        raise WizardError("browse_unavailable", "Unsupported path selection.", "Diese Dateiauswahl wird nicht unterstützt.")
    picker = linux_picker()
    if picker is None:
        raise WizardError("browse_unavailable",
                          "A desktop file picker (Zenity or KDialog) is unavailable. Enter the path in the field instead.",
                          "Eine Desktop-Dateiauswahl (Zenity oder KDialog) ist nicht verfügbar. Den Pfad stattdessen im Feld eingeben.")
    name, executable = picker
    title = {"game": "Select your Gloomhaven installation", "unity": "Select the Unity 2021.3.5f1 Editor folder",
             "apk": "Select the existing Quest APK"}[kind]
    if name == "zenity":
        command = [executable, "--file-selection", "--title=" + title]
        command += ["--file-filter=Quest APK | *.apk"] if kind == "apk" else ["--directory"]
    else:
        command = [executable, "--title", title]
        command += ["--getopenfilename", str(Path.home()), "Quest APK (*.apk)"] if kind == "apk" else ["--getexistingdirectory", str(Path.home())]
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=600)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise WizardError("browse_failed", "The desktop file picker could not finish. Enter the path or try again.",
                          "Die Desktop-Dateiauswahl konnte nicht abgeschlossen werden. Den Pfad eingeben oder erneut versuchen.") from error
    if result.returncode == 1:
        return None
    if result.returncode != 0:
        raise WizardError("browse_failed", "The desktop file picker failed. Enter the path or try again.",
                          "Die Desktop-Dateiauswahl ist fehlgeschlagen. Den Pfad eingeben oder erneut versuchen.")
    path = result.stdout.rstrip("\r\n")
    if not path:
        return None
    if len(path) > 32768 or "\0" in path or "\n" in path or "\r" in path or not Path(path).is_absolute():
        raise WizardError("browse_failed", "The picker did not return one absolute local path.",
                          "Die Dateiauswahl hat keinen einzelnen absoluten lokalen Pfad geliefert.")
    return editor_path(path) if kind == "unity" else path
