"""Delete one accepted owned leaf, repairing only Windows read-only attributes.

The caller retains the confirmed preview and workspace/build locks. Its stamp
callback must reject linked parents, reparse points, special files and hardlinks;
the helper never infers ownership from a filename or recursively changes access.
Metadata-only previews may omit the final native witness. Actual deletion still
acquires the callback's strong witness and retains it across every retry.
"""
from __future__ import annotations

import errno
import os
from pathlib import Path
import stat
import sys
import time

from state import Cancelled, WizardError

_RETRY_DELAYS = (.05, .10, .20, .40)
_READONLY = getattr(stat, "FILE_ATTRIBUTE_READONLY", 0x1)
_REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)


def _windows_host():
    return sys.platform == "win32"


def _check_cancel(cancel):
    if cancel is not None and cancel.is_set():
        raise Cancelled()


def _event(progress, phase, detail, done, total, **parameters):
    if progress is not None:
        progress({"phase": phase, "done": done, "total": total,
                  "detail": detail, **parameters})


def _changed(detail):
    return WizardError("cleanup_preview_changed",
        "Cleanup files changed. Refresh the deletion preview and retry; files already removed stay removed.",
        "Bereinigungsdateien wurden verändert. Die Löschvorschau aktualisieren und erneut versuchen; bereits entfernte Dateien bleiben entfernt.",
        path=detail, recovery="refresh-preview", retryable=True)


def _denied(detail, error):
    busy = getattr(error, "winerror", None) in (32, 33)
    if busy:
        return WizardError("cleanup_file_busy",
            "A Builder file is still in use. Close the Unity Editor and other programs using the working folder, then refresh the deletion preview and retry. Files already removed stay removed.",
            "Eine Builder-Datei wird noch verwendet. Den Unity-Editor und andere Programme schließen, die den Arbeitsordner verwenden; anschließend die Löschvorschau aktualisieren und erneut versuchen. Bereits entfernte Dateien bleiben entfernt.",
            path=detail, winerror=getattr(error, "winerror", None), recovery="refresh-preview", retryable=True)
    return WizardError("cleanup_file_denied",
        "The operating system still prevents deleting a Builder file. Close programs using the working folder, then refresh the deletion preview and retry. Files already removed stay removed.",
        "Das Betriebssystem verhindert weiterhin das Löschen einer Builder-Datei. Programme schließen, die den Arbeitsordner verwenden; anschließend die Löschvorschau aktualisieren und erneut versuchen. Bereits entfernte Dateien bleiben entfernt.",
        path=detail, winerror=getattr(error, "winerror", None), recovery="refresh-preview", retryable=True)


def _matches(current, expected, directory):
    if directory:
        return current[:3] == expected[:3]
    return current == expected or (len(expected) == 7 and expected[6] is None
                                  and current[:6] == expected[:6])


def _witness(path, stamp_fn, detail):
    try:
        return stamp_fn(path)
    except FileNotFoundError as error:
        raise _changed(detail) from error
    except OSError as error:
        if isinstance(error, PermissionError) or error.errno in (errno.EACCES, errno.EPERM) or getattr(error, "winerror", None) in (5, 32, 33):
            raise _denied(detail, error) from error
        raise


def _same_after_attribute_repair(before, after):
    """Only permissions/change time may differ after our exact chmod call."""
    if (before[:2] != after[:2] or stat.S_IFMT(before[2]) != stat.S_IFMT(after[2])
            or before[3:5] != after[3:5]):
        return False
    if len(before) > 6 and before[6] is not None:
        native_before, native_after = before[6], after[6]
        if (not isinstance(native_before, (tuple, list)) or len(native_before) != 6
                or native_before[0] != "win32-v1" or not isinstance(native_after, (tuple, list))
                or len(native_after) != 6
                or native_before[:5] != native_after[:5]):
            return False
    return True


def delete_owned(path, expected_stamp, stamp_fn, *, owned_root, cancel=None, progress=None):
    """Remove a previewed regular file or empty directory without broad repair.

    Ownership and parent-chain validation belong to the caller's retained tree
    and strong ``stamp_fn``. Lexical containment is checked again here without
    resolving/following links. An actual access failure is the only trigger for
    a read-only attribute repair; ordinary successful deletion does no chmod.
    Sharing violations retry briefly with cancellation, retaining the exact
    witness. Persistent access errors carry an actionable, localized recovery.
    """
    path = Path(path).absolute(); root = Path(owned_root).absolute()
    try:
        relative = path.relative_to(root)
    except ValueError as error:
        raise _changed(path.name) from error
    if not relative.parts or ".." in relative.parts or ".." in root.parts:
        raise _changed(path.name)
    detail = relative.as_posix()
    directory = stat.S_ISDIR(expected_stamp[2])
    if not (directory or stat.S_ISREG(expected_stamp[2])):
        raise _changed(detail)
    accepted = expected_stamp
    repaired = False; retries = 0
    while True:
        _check_cancel(cancel)
        current = _witness(path, stamp_fn, detail)
        if not _matches(current, accepted, directory):
            raise _changed(detail)
        # Promote a metadata-only preview to the actual strong deletion witness.
        accepted = current
        try:
            if directory:
                path.rmdir()
            else:
                path.unlink()
            return
        except OSError as error:
            _check_cancel(cancel)
            winerror = getattr(error, "winerror", None)
            if winerror == 5 and _windows_host() and not repaired:
                try:
                    info = path.lstat()
                except FileNotFoundError as missing:
                    raise _changed(detail) from missing
                except PermissionError as denied:
                    raise _denied(detail, denied) from denied
                if (getattr(info, "st_file_attributes", 0) & _REPARSE
                        or not (stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode))
                        or (stat.S_ISREG(info.st_mode) and info.st_nlink != 1)):
                    raise _changed(detail) from error
                if getattr(info, "st_file_attributes", 0) & _READONLY:
                    # Recheck the strong witness after failure and immediately
                    # before changing this leaf. Never change a replacement.
                    if not _matches(_witness(path, stamp_fn, detail), accepted, directory):
                        raise _changed(detail) from error
                    _event(progress, "cleanup-readonly-repair", detail, 0, 1,
                           action="clear-readonly", winerror=5)
                    _check_cancel(cancel)
                    try:
                        os.chmod(path, stat.S_IMODE(info.st_mode) | stat.S_IWRITE,
                                 follow_symlinks=False)
                    except OSError as repair_error:
                        raise _denied(detail, repair_error) from repair_error
                    _check_cancel(cancel)
                    after = _witness(path, stamp_fn, detail)
                    if not _same_after_attribute_repair(accepted, after):
                        raise _changed(detail) from error
                    accepted = after; repaired = True
                    _event(progress, "cleanup-readonly-repair", detail, 1, 1,
                           action="cleared-readonly", winerror=5)
                    continue
            if winerror in (32, 33) and retries < len(_RETRY_DELAYS):
                delay = _RETRY_DELAYS[retries]; retries += 1
                _event(progress, "cleanup-delete-retry", detail, retries, len(_RETRY_DELAYS),
                       winerror=winerror, retryDelaySeconds=delay)
                if cancel is not None:
                    if cancel.wait(delay):
                        raise Cancelled()
                else:
                    time.sleep(delay)
                continue
            if isinstance(error, PermissionError) or error.errno in (errno.EACCES, errno.EPERM) or winerror in (5, 32, 33):
                raise _denied(detail, error) from error
            raise
