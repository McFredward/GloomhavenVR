"""Provision pinned platform-tools in the installer's own script folder.

The installer calls this only after normal Windows/Linux ADB discovery fails. This
module never runs ADB, changes PATH, installs drivers or needs administrator rights.
"""
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import re
import shutil
import ssl
import stat
import sys
import tempfile
import time
import urllib.request
import uuid
import zipfile

URL = "https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip"
VERSION = "37.0.1"
SHA256 = "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99"
BYTES = 8044989
OWNER = "GloomhavenVR.QuestInstaller.ADB"
MARKER = ".quest-owner.json"
REQUIRED = ("adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "source.properties", "NOTICE.txt")
LINUX = {"url": "https://dl.google.com/android/repository/platform-tools_r37.0.1-linux.zip",
         "sha256": "d230f13842f60f782a8645f9c813f8f845bf36089ea7289f28c48f17979313f1",
         "bytes": 9054187, "required": ("adb", "source.properties", "NOTICE.txt", "lib64/libc++.so"),
         "executable": "adb", "host": "linux-x64"}


def _spec(value=None):
    # Keep the historical Windows constants and entry point stable for older
    # installers and receipts. Host selection never falls back to a foreign ABI.
    return value or {"url": URL, "sha256": SHA256, "bytes": BYTES,
                     "required": REQUIRED, "executable": "adb.exe", "host": "win-x64"}


class AdbBootstrapError(RuntimeError):
    """Automatic ADB setup could not safely complete."""


def _plain(path):
    try:
        metadata = path.lstat()
    except FileNotFoundError:
        return
    if stat.S_ISLNK(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & 0x400:
        raise AdbBootstrapError("Refusing a linked ADB cache path: " + str(path))


def _sha(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def _json(path, value):
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _owned(directory, kind):
    _plain(directory)
    if not directory.exists():
        return None
    marker = directory / MARKER
    _plain(marker)
    try:
        value = json.loads(marker.read_text(encoding="utf-8"))
        if directory.is_dir() and value.get("schema") == 1 and value.get("owner") == OWNER and value.get("kind") == kind:
            return value
    except (OSError, ValueError, AttributeError):
        pass
    raise AdbBootstrapError("Refusing to change an unowned ADB folder: " + str(directory)
                            + ". Move it aside yourself or use another installer folder.")


def _marker(kind, **values):
    return {"schema": 1, "owner": OWNER, "kind": kind, **values}


def _files(directory):
    _plain(directory)
    for base, directories, names in os.walk(directory, followlinks=False):
        for name in directories:
            _plain(Path(base) / name)
        for name in names:
            file = Path(base) / name
            _plain(file)
            if file != directory / MARKER:
                yield file


@contextmanager
def _lock(path, attempts=40):
    _plain(path)
    with path.open("a+b") as handle:
        if handle.tell() == 0:
            handle.write(b"0")
            handle.flush()
        for attempt in range(attempts):
            try:
                handle.seek(0)
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if attempt + 1 == attempts:
                    raise AdbBootstrapError("Another ADB setup is using this installer folder. Wait for it to finish, then retry.")
                time.sleep(0.25)
        try:
            yield
        finally:
            handle.seek(0)
            if os.name == "nt":
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


def _download(destination, spec=None):
    spec = _spec(spec)
    started = time.monotonic()
    request = urllib.request.Request(spec["url"], headers={"User-Agent": "GloomhavenVR-QuestInstaller"})
    with urllib.request.urlopen(request, timeout=30, context=ssl.create_default_context()) as response:
        with destination.open("xb") as stream:
            size = 0
            while True:
                block = response.read(1024 * 1024)
                if not block:
                    break
                size += len(block)
                if size > spec["bytes"] or time.monotonic() - started > 120:
                    raise AdbBootstrapError("ADB download exceeded its expected size or time limit. Retry on a reliable connection.")
                stream.write(block)


def _archive_valid(path, spec=None):
    spec = _spec(spec)
    _plain(path)
    return path.is_file() and path.stat().st_size == spec["bytes"] and _sha(path) == spec["sha256"]


def _extract(archive, stage, spec=None):
    spec = _spec(spec)
    with zipfile.ZipFile(archive) as zipped:
        seen = set()
        for entry in zipped.infolist():
            name = entry.filename.replace("\\", "/")
            parts = PurePosixPath(name).parts
            mode = (entry.external_attr >> 16) & 0o170000
            if (not parts or parts[0] != "platform-tools" or ".." in parts or ":" in name
                    or MARKER in parts or mode not in (0, stat.S_IFREG, stat.S_IFDIR)
                    or entry.external_attr & 0x400 or name.casefold() in seen):
                raise AdbBootstrapError("The platform-tools ZIP contains an unsafe path: " + name)
            seen.add(name.casefold())
            target = (stage / name).resolve()
            if stage not in target.parents:
                raise AdbBootstrapError("The platform-tools ZIP escapes its staging folder.")
        zipped.extractall(stage)
        if spec["host"].startswith("linux"):
            # ZipFile does not restore Unix permission bits. Only this pinned,
            # path-checked archive may supply executable modes; ignore setid bits.
            for entry in zipped.infolist():
                target = stage / entry.filename
                if not entry.is_dir():
                    target.chmod(0o755 if (entry.external_attr >> 16) & 0o111 else 0o644)
    tools = stage / "platform-tools"
    for name in spec["required"]:
        if not (tools / name).is_file() or (tools / name).stat().st_size == 0:
            raise AdbBootstrapError("The platform-tools ZIP is missing required file: " + name)
    if not re.search(r"^Pkg\.Revision\s*=\s*" + re.escape(VERSION) + r"\s*$",
                     (tools / "source.properties").read_text(encoding="utf-8"), re.M):
        raise AdbBootstrapError("The platform-tools ZIP has an unexpected version.")
    records = [{"path": file.relative_to(tools).as_posix(), "sha256": _sha(file)} for file in _files(tools)]
    _json(tools / MARKER, _marker("tools", complete=True, version=VERSION,
                                archiveSha256=spec["sha256"], host=spec["host"], files=records))
    return tools


def _valid(tools, state, spec=None):
    spec = _spec(spec)
    if (not state or not state.get("complete") or state.get("archiveSha256") != spec["sha256"]
            or state.get("version") != VERSION or state.get("host", "win-x64") != spec["host"]):
        return False
    try:
        records = state["files"]
        actual = {file.relative_to(tools).as_posix(): _sha(file) for file in _files(tools)}
        expected = {item["path"]: item["sha256"] for item in records}
        return (actual == expected and all(name in actual for name in spec["required"])
                and (not spec["host"].startswith("linux") or os.access(tools / spec["executable"], os.X_OK)))
    except (OSError, ValueError, KeyError, TypeError):
        return False


def ensure_windows_adb(script_directory: Path) -> Path:
    """Return a verified private Windows adb.exe; provisioning never executes it."""
    return _ensure_adb(script_directory)


def ensure_linux_adb(script_directory: Path) -> Path:
    """Return a verified executable Linux x86-64 ADB without running it."""
    if platform.machine().lower() not in ("x86_64", "amd64"):
        raise AdbBootstrapError("The pinned Linux platform-tools require an x86-64 computer.")
    return _ensure_adb(script_directory, LINUX)


def ensure_adb(script_directory: Path) -> Path:
    if sys.platform == "win32": return ensure_windows_adb(script_directory)
    if sys.platform == "linux": return ensure_linux_adb(script_directory)
    raise AdbBootstrapError("Automatic ADB setup supports Windows and Linux x86-64.")


def _ensure_adb(script_directory: Path, selected=None) -> Path:
    spec = _spec(selected)
    try:
        directory = Path(script_directory).expanduser().resolve()
        if not directory.is_dir():
            raise AdbBootstrapError("ADB setup needs an existing writable installer script folder.")
        with _lock(directory / ".quest-adb.lock"):
            cache = directory / ".quest-adb"
            if _owned(cache, "cache") is None:
                cache.mkdir()
                _json(cache / MARKER, _marker("cache"))
            tools = cache / "platform-tools"
            state = _owned(tools, "tools")
            if _valid(tools, state, spec):
                return tools / spec["executable"]
            archive = cache / PurePosixPath(spec["url"]).name
            if not _archive_valid(archive, spec):
                temporary = cache / ("download-" + uuid.uuid4().hex + ".partial")
                try:
                    print("Downloading Android platform-tools " + VERSION + " for this installer...", flush=True)
                    if selected is None: _download(temporary)
                    else: _download(temporary, spec)
                    if not _archive_valid(temporary, spec):
                        raise AdbBootstrapError("ADB download SHA-256/size mismatch. No downloaded program was executed; retry.")
                    os.replace(temporary, archive)
                finally:
                    temporary.unlink(missing_ok=True)
            with tempfile.TemporaryDirectory(prefix="stage-", dir=cache) as temporary:
                replacement = _extract(archive, Path(temporary).resolve(), spec)
                if state is not None:
                    # Validate ownership/links again before removing only our old tools.
                    _owned(tools, "tools")
                    list(_files(tools))
                    shutil.rmtree(tools)
                os.replace(replacement, tools)
            print("Local Android platform-tools are ready.", flush=True)
            return tools / spec["executable"]
    except AdbBootstrapError:
        raise
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile, RuntimeError) as error:
        raise AdbBootstrapError("Automatic ADB setup failed: " + str(error)
                                + ". Check the network and writable script folder, then retry.") from error
