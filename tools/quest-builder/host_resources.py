"""Host-only scheduling and bounded support evidence; never a content-cache input.

The B625 native retry measured one frontend with VmHWM24,172,988 KiB plus
VmSwap3,093,152 KiB (approximately 26 GiB combined). Native IL2CPP admission
therefore reserves 32 GiB for the first translation unit and 28 GiB for each
additional worker, separately from Unity and OS headroom. It must not
reuse the much smaller CMake codec budget. Overrides are upper limits, not an
instruction to exceed measured host capacity. On Windows, a physically constrained
host may admit exactly one native job only with known sufficient commit headroom.
That explicitly requires paging and may be substantially slower; Windows paging
performance and end-to-end execution remain unverified.
"""
from __future__ import annotations

from contextlib import contextmanager
import ctypes
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import uuid

GIB = 1024 ** 3
JOBS_ENV = "GHVRQ_BUILD_JOBS"
EVIDENCE_ENV = "GHVRQ_RESOURCE_EVIDENCE_ROOT"
INPUT_ENV = "GHVRQ_RESOURCE_INPUT_KEY"
RUN_ENV = "GHVRQ_RESOURCE_RUN_ID"
# (largest first worker, each additional worker, parent/Unity headroom, CPU cap)
PROFILES = {"il2cpp": (32 * GIB, 28 * GIB, 6 * GIB, 32),
            "unity": (3 * GIB, 2 * GIB, 4 * GIB, 16),
            "box64": (4 * GIB, 2 * GIB, 0, 32),
            "opus": (2 * GIB, GIB, 0, 32)}


class ResourceError(ValueError):
    pass


class MemoryStatus(ctypes.Structure):
    _fields_ = [("length", ctypes.c_uint32), ("load", ctypes.c_uint32)] + [
        (name, ctypes.c_uint64) for name in ("totalPhys", "availPhys", "totalPageFile",
                                            "availPageFile", "totalVirtual", "availVirtual", "availExtendedVirtual")]


def _windows_snapshot(kernel=None):
    """Real Win32 memory/commit and current-process affinity, without PowerShell."""
    kernel = kernel or ctypes.WinDLL("kernel32", use_last_error=True)
    state = MemoryStatus(length=ctypes.sizeof(MemoryStatus))
    if not kernel.GlobalMemoryStatusEx(ctypes.byref(state)):
        raise OSError("GlobalMemoryStatusEx failed")
    # HANDLE and masks are pointer-sized; default ctypes int truncates x64 handles.
    process_function = kernel.GetCurrentProcess
    process_function.restype = ctypes.c_void_p
    affinity_function = kernel.GetProcessAffinityMask
    affinity_function.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_size_t), ctypes.POINTER(ctypes.c_size_t)]
    process_mask, system_mask = ctypes.c_size_t(), ctypes.c_size_t()
    affinity = None
    if affinity_function(process_function(), ctypes.byref(process_mask), ctypes.byref(system_mask)):
        affinity = process_mask.value.bit_count() or None
    return {"totalMemoryBytes": state.totalPhys, "availableMemoryBytes": state.availPhys,
            "commitHeadroomBytes": state.availPageFile, "affinityCpus": affinity}


def _linux_snapshot(proc=Path("/proc"), cgroup=Path("/sys/fs/cgroup")):
    values = {}
    for line in (proc / "meminfo").read_text().splitlines():
        fields = line.replace(":", " ").split()
        if len(fields) >= 2 and fields[1].isdigit():
            values[fields[0]] = int(fields[1]) * 1024
    available = values.get("MemAvailable")
    if available is None:
        available = sum(values.get(name, 0) for name in ("MemFree", "Buffers", "Cached"))
    result = {"totalMemoryBytes": values.get("MemTotal"), "availableMemoryBytes": available,
              "commitHeadroomBytes": None}
    # Respect unified cgroup constraints when exposed, including ancestor limits.
    roots = [cgroup]
    try:
        relative = next(line[3:] for line in (proc / "self/cgroup").read_text().splitlines() if line.startswith("0::"))
        parts = Path(relative.lstrip("/")).parts
        if ".." not in parts:
            current = cgroup.joinpath(*parts)
            if current.exists():
                roots = [current, *[p for p in current.parents if p == cgroup or cgroup in p.parents]]
    except (OSError, StopIteration):
        pass
    for root in roots:
        try:
            limit = (root / "memory.max").read_text().strip()
            used = int((root / "memory.current").read_text())
            if limit.isdigit():
                limit = int(limit)
                result["totalMemoryBytes"] = min(result["totalMemoryBytes"] or limit, limit)
                result["availableMemoryBytes"] = min(result["availableMemoryBytes"], max(0, limit - used))
        except (OSError, ValueError):
            pass
        try:
            quota, period = (root / "cpu.max").read_text().split()
            if quota.isdigit() and int(period) > 0:
                cpus = max(1, math.ceil(int(quota) / int(period)))
                result["quotaCpus"] = min(result.get("quotaCpus", cpus), cpus)
        except (OSError, ValueError):
            pass
    return result


def _mac_snapshot():
    total = int(subprocess.check_output(["sysctl", "-n", "hw.memsize"], text=True))
    stats = subprocess.check_output(["vm_stat"], text=True)
    page = re.search(r"page size of (\d+) bytes", stats)
    if not page:
        raise OSError("vm_stat page size unavailable")
    available = sum(int(match[1]) for match in re.finditer(r"Pages (?:free|inactive|speculative):\s+(\d+)\.", stats))
    return {"totalMemoryBytes": total, "availableMemoryBytes": available * int(page[1]), "commitHeadroomBytes": None}


def detect_host(output_path):
    """Portable, nonidentifying host snapshot. Missing memory yields one job."""
    logical = max(1, os.cpu_count() or 1)
    result = {"schema": 1, "platform": sys.platform, "logicalCpus": logical, "effectiveCpus": logical,
              "totalMemoryBytes": None, "availableMemoryBytes": None, "commitHeadroomBytes": None,
              "diskFreeBytes": None, "diskTotalBytes": None, "warnings": []}
    try:
        memory = _windows_snapshot() if sys.platform == "win32" else _linux_snapshot() if sys.platform == "linux" else _mac_snapshot()
        result.update(memory)
    except (OSError, ValueError, AttributeError):
        result["warnings"].append("memory-detection-unavailable")
    try:
        result["effectiveCpus"] = min(result["effectiveCpus"], len(os.sched_getaffinity(0)))
    except (OSError, AttributeError):
        pass
    for field in ("affinityCpus", "quotaCpus"):
        if result.get(field):
            result["effectiveCpus"] = min(result["effectiveCpus"], result[field])
    result["effectiveCpus"] = max(1, result["effectiveCpus"])
    path = Path(output_path).absolute()
    while not path.exists() and path != path.parent:
        path = path.parent
    try:
        disk = shutil.disk_usage(path)
        result.update(diskFreeBytes=disk.free, diskTotalBytes=disk.total)
    except OSError:
        result["warnings"].append("disk-detection-unavailable")
    return result


def parse_jobs(value):
    if value is None or value == "":
        return None
    if isinstance(value, bool) or not re.fullmatch(r"[1-9][0-9]{0,3}", str(value)) or int(value) > 1024:
        raise ResourceError("--jobs / GHVRQ_BUILD_JOBS must be an integer from 1 to 1024.")
    return int(value)


def choose_jobs(host, phase, override=None):
    """Return a resource-bounded policy; requested jobs never bypass RAM limits."""
    if phase not in PROFILES:
        raise ResourceError("Unknown host build phase: " + phase)
    requested = parse_jobs(os.environ.get(JOBS_ENV) if override is None else override)
    first, extra, parent, phase_cap = PROFILES[phase]
    cpus = max(1, host["effectiveCpus"])
    cpu_limit = min(phase_cap, cpus if requested else max(1, cpus - (1 if cpus > 4 else 0)))
    available = host.get("availableMemoryBytes")
    commit = host.get("commitHeadroomBytes")
    if commit is not None:
        if available is not None: available = min(available, commit)
    total = host.get("totalMemoryBytes")
    os_reserve = max(4 * GIB, int((total or 0) * 0.10))
    budget = max(0, available - os_reserve - parent) if available is not None else None
    memory_limit = 1 if budget is None else max(1, 1 + (budget - first) // extra)
    jobs = min(cpu_limit, memory_limit, requested or cpu_limit)
    insufficient = budget is not None and budget < first
    required_commit = first + parent + os_reserve
    paging = (phase == "il2cpp" and host.get("platform") == "win32" and insufficient
              and host.get("availableMemoryBytes") is not None and commit is not None and commit >= required_commit)
    if paging:
        jobs = 1
    launch_allowed = (phase != "il2cpp" or ((budget is not None and not insufficient) or paging)
                      and (host.get("platform") != "win32" or commit is not None))
    return {"schema": 1, "phase": phase, "requestedJobs": requested, "jobs": jobs,
            "cpuLimit": cpu_limit, "memoryLimit": memory_limit, "memoryBudgetBytes": budget,
            "osReserveBytes": os_reserve, "parentReserveBytes": parent,
            "largestWorkerReserveBytes": first, "additionalWorkerReserveBytes": extra,
            "memoryKnown": budget is not None, "memoryInsufficient": insufficient,
            "nativeLaunchAllowed": launch_allowed, "pagingRequired": paging,
            "requiredCommitHeadroomBytes": required_commit, "pagingExecutionVerified": False,
            "windowsExecutionVerified": False}


def _atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True); stream.write("\n")
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def emit_resource_evidence(output_root, event):
    """Only accept bounded public metrics; never serialize argv/environment."""
    root = Path(output_root)
    event = {**event, "schema": 1, "utc": datetime.now(timezone.utc).isoformat()}
    run = os.environ.get(RUN_ENV)
    key = os.environ.get(INPUT_ENV)
    if run and re.fullmatch(r"[0-9a-f]{32}", run): event["runId"] = run
    if key and re.fullmatch(r"[0-9a-f]{64}", key): event["inputKey"] = key
    encoded = json.dumps(event, sort_keys=True)
    if len(encoded) > 16384:
        raise ResourceError("Resource evidence event exceeds its bounded size.")
    path = root / "evidence/resource-events.jsonl"
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as stream:
        stream.write(encoded + "\n")
    if event.get("event") == "policy":
        _atomic_json(root / "evidence/resource-policy.json", event)
    metrics_path = root / "build-metrics.json"
    try:
        metrics = json.loads(metrics_path.read_text()) if metrics_path.stat().st_size <= 262144 else {}
        if (not isinstance(metrics, dict) or metrics.get("schema") != 1
                or not isinstance(metrics.get("phases", []), list) or not isinstance(metrics.get("policies", {}), dict)):
            metrics = {}
    except (OSError, ValueError):
        metrics = {}
    metrics.setdefault("schema", 1)
    if "runId" in event: metrics["runId"] = event["runId"]
    if "inputKey" in event: metrics["inputKey"] = event["inputKey"]
    if event.get("event") == "policy":
        metrics["host"] = event["host"]
        metrics.setdefault("policies", {})[event["policy"]["phase"]] = event["policy"]
    else:
        metrics["phases"] = [*metrics.get("phases", [])[-255:], event]
    _atomic_json(metrics_path, metrics)


def phase_budget(phase, path):
    host = detect_host(path)
    policy = choose_jobs(host, phase)
    root = os.environ.get(EVIDENCE_ENV)
    if root:
        event = {"event": "policy", "host": host, "policy": policy}
        key = os.environ.get(INPUT_ENV)
        if key and re.fullmatch(r"[0-9a-f]{64}", key):
            event["inputKey"] = key
        emit_resource_evidence(root, event)
    print("resources: " + json.dumps({"phase": phase, "jobs": policy["jobs"], "requestedJobs": policy["requestedJobs"],
          "effectiveCpus": host["effectiveCpus"], "availableMemoryBytes": host["availableMemoryBytes"],
          "diskFreeBytes": host["diskFreeBytes"], "memoryInsufficient": policy["memoryInsufficient"],
          "pagingRequired": policy["pagingRequired"], "nativeLaunchAllowed": policy["nativeLaunchAllowed"],
          "requiredCommitHeadroomBytes": policy["requiredCommitHeadroomBytes"]}), flush=True)
    if policy["pagingRequired"]:
        print("resources: native compilation requires Windows paging; admitting exactly one job within the reported commit headroom. "
              "This may be significantly slower; Windows paging execution/performance is not yet qualified.", flush=True)
    return policy


@contextmanager
def timed_phase(phase, *, log=None):
    root = os.environ.get(EVIDENCE_ENV)
    started = time.monotonic()
    identifier = uuid.uuid4().hex
    event = {"event": "phase", "phase": phase, "id": identifier}
    if log:
        event["logName"] = Path(log).name
    if root:
        emit_resource_evidence(root, {**event, "state": "started"})
    outcome = "failed"
    try:
        yield
        outcome = "succeeded"
    except KeyboardInterrupt:
        outcome = "interrupted"
        raise
    finally:
        if root:
            emit_resource_evidence(root, {**event, "state": "finished", "outcome": outcome,
                                   "durationSeconds": round(time.monotonic() - started, 3)})


@contextmanager
def resource_context(output, jobs=None):
    """Orchestrator and direct native tools share the same inherited policy."""
    requested = parse_jobs(jobs if jobs is not None else os.environ.get(JOBS_ENV))
    changes = {EVIDENCE_ENV: str(Path(output).absolute()), INPUT_ENV: "", RUN_ENV: uuid.uuid4().hex}
    if requested is not None:
        changes[JOBS_ENV] = str(requested)
    before = {key: os.environ.get(key) for key in changes}
    os.environ.update(changes)
    try:
        yield
    finally:
        for key, value in before.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value


def prepare_bee_launcher(dotnet, output):
    """Build our small native apphost in a private cache, never installed Unity."""
    import hashlib
    dotnet = Path(dotnet).resolve()
    source = Path(__file__).with_name("host-launcher")
    names = ("QuestBuildHost.csproj", "Program.cs")
    fingerprints = {name: hashlib.sha256((source / name).read_bytes()).hexdigest() for name in names}
    identity = {"schema": 1, "platform": sys.platform, "sources": fingerprints,
                "dotnetSha256": hashlib.sha256(dotnet.read_bytes()).hexdigest()}
    key = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
    root = Path(output) / "tool-cache/host-launcher" / key
    destination = root / "bin"
    executable = destination / ("QuestBuildHost.exe" if sys.platform == "win32" else "QuestBuildHost")
    receipt = root / "host-launcher.json"
    artifact_names = {executable.name, "QuestBuildHost.dll", "QuestBuildHost.deps.json", "QuestBuildHost.runtimeconfig.json"}
    if receipt.is_file():
        try:
            saved = json.loads(receipt.read_text()) if receipt.stat().st_size <= 16384 else {}
            files = saved.get("files", [])
            valid = (set(saved) == {"schema", "identity", "files"} and saved["schema"] == 1 and saved["identity"] == identity
                     and isinstance(files, list) and len(files) == 4 and {item["path"] for item in files} == artifact_names)
            if valid:
                for item in files:
                    path = destination / item["path"]
                    if (set(item) != {"path", "sha256", "size"} or type(item["size"]) is not int
                            or not isinstance(item["sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", item["sha256"])
                            or any(part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()) for part in (path, *path.parents))
                            or not path.is_file() or path.stat().st_size != item["size"]
                            or hashlib.sha256(path.read_bytes()).hexdigest() != item["sha256"]):
                        valid = False; break
            if valid: return executable
        except (OSError, ValueError, KeyError, TypeError):
            pass
        raise ResourceError("Cached host scheduling launcher differs from its receipt; repair the host-launcher cache.")
    copied = root / "source"
    copied.mkdir(parents=True, exist_ok=True)
    for name in names:
        shutil.copyfile(source / name, copied / name)
    log_path = root / "host-launcher-build.log"
    with timed_phase("host-launcher", log=log_path), log_path.open("w", encoding="utf-8") as log:
        result = subprocess.run([str(dotnet), "publish", str(copied / names[0]), "--configuration", "Release",
                "--no-self-contained", "--output", str(destination), "-p:UseAppHost=true",
                "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false"],
                stdout=log, stderr=subprocess.STDOUT, check=False)
        if result.returncode or not executable.is_file():
            raise ResourceError("Cannot build the private Bee scheduling apphost; inspect host-launcher-build.log.")
    files = []
    for name in (executable.name, "QuestBuildHost.dll", "QuestBuildHost.deps.json", "QuestBuildHost.runtimeconfig.json"):
        path = destination / name
        if not path.is_file():
            raise ResourceError("Host scheduling apphost output is incomplete.")
        files.append({"path": name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "size": path.stat().st_size})
    _atomic_json(receipt, {"schema": 1, "identity": identity, "files": files})
    return executable


def unity_native_environment(editor, launcher, jobs, dotnet, base):
    """Verify the installed backend CLI; exact process-local adapter applies it."""
    editor = Path(editor)
    backend = editor.parent / "Data" / ("bee_backend.exe" if sys.platform == "win32" else "bee_backend")
    if not backend.is_file():
        raise ResourceError("Selected Unity lacks its native Bee backend.")
    try:
        help_text = subprocess.check_output([str(backend), "--help"], text=True, errors="replace", timeout=20,
                                            stderr=subprocess.STDOUT)
    except (OSError, subprocess.SubprocessError) as error:
        raise ResourceError("Cannot verify the installed native Bee scheduling options.") from error
    if "--threads=<integer>" not in help_text or "Specify number of build threads" not in help_text:
        raise ResourceError("Installed Bee backend does not expose the witnessed --threads option.")
    jobs = parse_jobs(jobs)
    if jobs is None:
        raise ResourceError("Native Bee needs an explicit bounded thread policy.")
    env = dict(base)
    env.update(GHVRQ_BEE_REAL_PATH=str(backend.resolve()), GHVRQ_BEE_LAUNCHER=str(Path(launcher).resolve()),
               GHVRQ_BEE_THREADS=str(jobs), DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    return env
