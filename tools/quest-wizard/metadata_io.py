"""Bounded recovery when Windows refuses an atomic Builder metadata replacement.

Never unlink the previous document, elevate access, edit ACLs or change a folder's
permissions. Only a single-link ordinary JSON leaf beneath a verified Wizard
owner may have its Windows read-only attribute cleared after an actual refusal.
"""
from __future__ import annotations

import errno
import json
import os
from pathlib import Path
import stat
import sys
import time

RETRY_DELAYS = (.05, .10, .20, .40, .80)
READONLY = getattr(stat, "FILE_ATTRIBUTE_READONLY", 1)
REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)


def _windows_host():
    return sys.platform == "win32"


def access_error(error):
    return (isinstance(error, PermissionError) or getattr(error, "errno", None) in (errno.EACCES, errno.EPERM)
            or getattr(error, "winerror", None) in (5, 32, 33))


def _stamp(info):
    return (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns)


def owned_root(path, ordinary):
    for parent in Path(path).parents:
        marker = parent / "wizard-owner.json"
        try:
            ordinary(marker)
            info = marker.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size > 1024:
                continue
            if json.loads(marker.read_bytes()) == {"schema": 1, "owner": "GloomhavenVR.QuestWizard"}:
                return parent
        except (OSError, ValueError, RuntimeError):
            continue
    return None


def busy_error(path, error, error_type, *, attempts=1, repaired=False):
    return error_type("workspace_metadata_busy",
        "Windows or the operating system still prevents saving Builder metadata. Close other Builder windows or programs using the file shown below, then retry. Completed build work and the existing APK are retained; do not delete the workspace.",
        "Windows oder das Betriebssystem verhindert weiterhin das Speichern einer Builder-Datei. Andere Builder-Fenster oder Programme schließen, die die unten genannte Datei verwenden, und erneut versuchen. Fertige Build-Ergebnisse und die vorhandene APK bleiben erhalten; den Arbeitsordner nicht löschen.",
        path=str(path), cause=str(error)[:2048], errorType=type(error).__name__,
        winerror=getattr(error, "winerror", None), retryAttempts=attempts,
        readOnlyRepaired=repaired, retryable=True, completedWorkRetained=True)


def record(path, ordinary, event, **fields):
    """Keep small repair facts without recording the JSON's account/secret data."""
    try:
        root = owned_root(path, ordinary)
        if root is None: return
        log = ordinary(root / "logs/metadata-repair.jsonl")
        log.parent.mkdir(parents=True, exist_ok=True)
        if log.exists():
            info = log.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size >= 131072:
                return
        row = dict(time=time.time(), event=event, path=Path(path).relative_to(root).as_posix(), **fields)
        with log.open("ab") as stream:
            stream.write((json.dumps(row, sort_keys=True) + "\n").encode())
    except (OSError, ValueError, RuntimeError):
        pass  # An optional repair log must not replace the original outcome.


def replace(temp, path, ordinary, error_type):
    attempts, retries, repaired = 0, 0, False
    while True:
        attempts += 1
        try:
            ordinary(temp); ordinary(path)
            os.replace(temp, path)
            if retries or repaired:
                record(path, ordinary, "metadata-replaced", attempts=attempts, readOnlyRepaired=repaired)
            return {"attempts": attempts, "readOnlyRepaired": repaired}
        except OSError as error:
            if not access_error(error): raise
            # Access denied may also mean a temporary sharing conflict on
            # Windows. Observe the actual attribute before changing anything.
            if _windows_host() and not repaired and path.suffix == ".json" and owned_root(path, ordinary):
                try:
                    info = path.lstat()
                    if (stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                            and not getattr(info, "st_file_attributes", 0) & REPARSE
                            and getattr(info, "st_file_attributes", 0) & READONLY):
                        ordinary(path)
                        if _stamp(path.lstat()) == _stamp(info):
                            os.chmod(path, stat.S_IMODE(info.st_mode) | stat.S_IWRITE, follow_symlinks=False)
                            repaired = True
                            record(path, ordinary, "metadata-readonly-cleared", attempts=attempts)
                            continue
                except OSError:
                    pass  # Keep the original refusal and use bounded retries.
            if (_windows_host() or getattr(error, "winerror", None) in (5, 32, 33)) and retries < len(RETRY_DELAYS):
                delay = RETRY_DELAYS[retries]; retries += 1
                time.sleep(delay)
                continue
            record(path, ordinary, "metadata-replacement-blocked", attempts=attempts,
                   readOnlyRepaired=repaired, winerror=getattr(error, "winerror", None))
            raise busy_error(path, error, error_type, attempts=attempts, repaired=repaired) from error
