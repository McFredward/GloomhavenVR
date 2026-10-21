"""Wait for the owner's Unity Editor before repairing generated project files.

The presence of UnityLockfile alone is not evidence of a running Editor. Never
remove that file or terminate an Editor. Windows observes the live sharing lock;
Linux observes the actual Unity command line for this exact project instead.
"""
from __future__ import annotations

import os
from pathlib import Path
import time

from storage import BuildError, _ordinary_owned


def _windows_busy(path):
    import ctypes
    from ctypes import wintypes
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    create = kernel.CreateFileW
    create.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                       ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    create.restype = wintypes.HANDLE
    close = kernel.CloseHandle
    close.argtypes = [wintypes.HANDLE]; close.restype = wintypes.BOOL
    handle = create(str(path), 0, 0, None, 3, 0x80, None)
    if handle == ctypes.c_void_p(-1).value:
        error = ctypes.get_last_error()
        if error in (32, 33): return True  # Live sharing/byte-range lock.
        if error in (2, 3): return False
        raise BuildError("The generated Unity project lock cannot be inspected (Windows error " + str(error) + ").")
    close(handle)
    return False


def _unity_project(arguments, project, *, cwd=None):
    if not arguments or Path(arguments[0]).name.lower() not in ("unity", "unity.exe"):
        return False
    for index, argument in enumerate(arguments[:-1]):
        if argument.lower() == "-projectpath":
            candidate = Path(arguments[index + 1])
            if not candidate.is_absolute():
                if cwd is None: return False
                candidate = Path(cwd) / candidate
            return os.path.normcase(os.path.abspath(candidate)) == os.path.normcase(str(project))
    return False


def _linux_busy(project, processes=Path("/proc")):
    for entry in processes.iterdir():
        if not entry.name.isdigit(): continue
        try:
            with (entry / "cmdline").open("rb") as stream: raw = stream.read(65537)
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
        if len(raw) > 65536: continue
        arguments = [os.fsdecode(part) for part in raw.rstrip(b"\0").split(b"\0")]
        cwd = None
        if arguments and Path(arguments[0]).name.lower() in ("unity", "unity.exe"):
            try: cwd = os.readlink(entry / "cwd")
            except (FileNotFoundError, PermissionError, ProcessLookupError, OSError): pass
        if _unity_project(arguments, project, cwd=cwd): return True
    return False


def editor_busy(project):
    project = _ordinary_owned(Path(project).absolute())
    lock = _ordinary_owned(project / "Temp/UnityLockfile")
    if os.name == "nt":
        return _windows_busy(lock)
    return _linux_busy(project)


def wait_for_editor(project, progress=None):
    """Keep the build alive and cancellable while its project is open in Unity."""
    waiting, started, last_report = False, time.monotonic(), -float("inf")
    while editor_busy(project):
        now = time.monotonic()
        if progress and now - last_report >= 15:
            progress.event("prepare-editor-wait", detail="Close the Unity Editor for this project; the build will continue automatically.",
                           status="start" if not waiting else "progress")
            last_report = now
        waiting = True
        time.sleep(1)
    if waiting and progress:
        progress.event("prepare-editor-wait", 1, 1, "projects", "Unity project is available; continuing the saved build.", status="complete")
    return time.monotonic() - started if waiting else 0.
