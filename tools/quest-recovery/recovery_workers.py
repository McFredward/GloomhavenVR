"""Bounded isolated exporters; canonical assets are published by one parent.

The shared input is a private, immutable COPY of PC serialized data. Hard links
are allowed only between that private copy and short-lived read-only exporter
inputs; mutable Unity assets, original game files and .meta outputs never share
an inode with another project. Exporters read inputs and write separate trees.
"""
import importlib
import os
from pathlib import Path
import shutil
import stat
import sys
import threading
from types import SimpleNamespace

from recover import ordinary_path, RecoveryError

_modules = None
_lock = threading.Lock()


def modules():
    global _modules
    with _lock:
        if _modules is None:
            directory = str(Path(__file__).resolve().parents[1] / "quest-builder")
            sys.path.insert(0, directory)
            try:
                _modules = importlib.import_module("asset_jobs"), importlib.import_module("host_resources")
            finally:
                sys.path.remove(directory)
        return _modules


def policy(workspace):
    _, resources = modules()
    selected = resources.phase_budget("asset-recovery", workspace)
    # .NET's runtime-supported process-local override bounds nested exporter
    # parallelism instead of starting N runtimes each claiming every core.
    # https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/environment-processorcount-on-windows
    cpus = selected["effectiveCpus"]
    cpu_budget = min(cpus, selected["requestedJobs"]) if selected["requestedJobs"] else max(1, cpus - (1 if cpus > 4 else 0))
    selected["workerProcessorCount"] = max(1, cpu_budget // selected["jobs"])
    return selected


def oom_kill_count(path=Path("/sys/fs/cgroup/memory.events")):
    """Optional Linux kernel witness; SIGKILL alone never means low memory."""
    try:
        with path.open("rb") as stream: data = stream.read(4097)
        if len(data) > 4096: return None
        for line in data.decode("ascii").splitlines():
            key, value = line.split()
            if key == "oom_kill": return int(value) if value.isdecimal() else None
    except (OSError, ValueError, UnicodeError): pass
    return None


def package(index, group, directory, completed=None):
    # Reserve compressed expansion plus the shared core reader's estimated
    # resident graph. This is advisory; witnessed pressure triggers serial
    # retry with siblings' accepted closed outputs retained.
    size = sum(row.get("bytes", 0) for row in group["bundles"])
    return SimpleNamespace(index=index, group=group, directory=directory,
                           completed=completed, memory_bytes=2 * 1024 ** 3 + size * 12)


def isolate_input(shared, stage):
    shared, stage = ordinary_path(shared), ordinary_path(stage)
    stage.mkdir(parents=True, exist_ok=True)
    for folder, directories, filenames in os.walk(shared):
        relative = Path(folder).relative_to(shared)
        if relative == Path(".") and "SelectedBundles" in directories:
            directories.remove("SelectedBundles")  # Legacy sequential input's last batch.
        destination = stage / relative
        destination.mkdir(parents=True, exist_ok=True)
        for name in directories:
            ordinary_path(Path(folder) / name)
        for name in filenames:
            original = ordinary_path(Path(folder) / name)
            target = ordinary_path(destination / name)
            if not original.is_file():
                raise RecoveryError("Private recovery input is not a regular file.")
            try:
                os.link(original, target)
            except OSError:
                # Cross-volume/non-hardlink filesystems are supported; this is
                # a bounded current batch copy, never a permanent second cache.
                shutil.copy2(original, target)


def discard_tree(path):
    """Delete only an owned temporary tree, including copied Win32 readonly files."""
    path = ordinary_path(path)
    if not path.exists(): return
    def retry(operation, filename, error):
        target = ordinary_path(filename)
        if not isinstance(error, PermissionError): raise error
        before = target.lstat()
        # Never chmod a link or an outside caller's source file. Windows copies
        # can inherit read-only attributes even though the directory is ours.
        if not stat.S_ISREG(before.st_mode) or before.st_mode & stat.S_IWRITE: raise error
        target.chmod(before.st_mode | stat.S_IWRITE)
        operation(filename)
    shutil.rmtree(path, onexc=retry)


def ordered(items, prepare, execute, publish, selected_policy):
    jobs, _ = modules()
    return jobs.ordered_pipeline(items, prepare, execute, publish,
        jobs=selected_policy["jobs"], byte_budget=selected_policy["memoryBudgetBytes"])
