"""Host-only scheduling and bounded support evidence; never a content-cache input.

The B625 native retry measured one frontend with VmHWM24,172,988 KiB plus
VmSwap3,093,152 KiB (approximately 26 GiB combined). Native IL2CPP admission
therefore reserves 32 GiB for the first translation unit and 28 GiB for each
additional worker. Reported available memory already excludes current OS and
parent allocations; only future Unity growth and bounded transient headroom are
reserved again. Windows admission uses commit, not a requirement for free RAM. It must not
reuse the much smaller CMake codec budget. Overrides are upper limits, not an
instruction to exceed measured host capacity. On Windows, a physically constrained
host may admit exactly one native job only with known sufficient commit headroom.
Linux paging admission additionally requires reported, cgroup-bounded free swap.
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
            "opus": (2 * GIB, GIB, 0, 32),
            # Managed image codecs are separate processes. Keep native bundle
            # readers and the single parent YAML publisher outside their pool.
            # Admission also checks each job's actual estimated payload memory.
            "asset-codec": (2 * GIB, GIB, 6 * GIB, 16)}


class ResourceError(ValueError):
    pass


class MemoryStatus(ctypes.Structure):
    _fields_ = [("length", ctypes.c_uint32), ("load", ctypes.c_uint32)] + [
        (name, ctypes.c_uint64) for name in ("totalPhys", "availPhys", "totalPageFile",
                                            "availPageFile", "totalVirtual", "availVirtual", "availExtendedVirtual")]



class PerformanceInformation(ctypes.Structure):
    _fields_ = [("cb", ctypes.c_uint32)] + [(name, ctypes.c_size_t) for name in
        ("CommitTotal", "CommitLimit", "CommitPeak", "PhysicalTotal", "PhysicalAvailable",
         "SystemCache", "KernelTotal", "KernelPaged", "KernelNonpaged", "PageSize")] + [
        (name, ctypes.c_uint32) for name in ("HandleCount", "ProcessCount", "ThreadCount")]


class ProcessMemoryCounters(ctypes.Structure):
    _fields_ = [("cb", ctypes.c_uint32), ("PageFaultCount", ctypes.c_uint32)] + [
        (name, ctypes.c_size_t) for name in ("PeakWorkingSetSize", "WorkingSetSize",
        "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage",
        "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage", "PrivateUsage")]

def _windows_snapshot(kernel=None):
    """Real Win32 memory/commit and current-process affinity, without PowerShell."""
    kernel = kernel or ctypes.WinDLL("kernel32", use_last_error=True)
    state = MemoryStatus(length=ctypes.sizeof(MemoryStatus))
    memory_function = kernel.GlobalMemoryStatusEx
    memory_function.argtypes = [ctypes.POINTER(MemoryStatus)]
    memory_function.restype = ctypes.c_int
    if not memory_function(ctypes.byref(state)):
        raise OSError("GlobalMemoryStatusEx failed")
    # HANDLE and masks are pointer-sized; default ctypes int truncates x64 handles.
    process_function = kernel.GetCurrentProcess
    process_function.restype = ctypes.c_void_p
    process_function.argtypes = []
    affinity_function = kernel.GetProcessAffinityMask
    affinity_function.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_size_t), ctypes.POINTER(ctypes.c_size_t)]
    affinity_function.restype = ctypes.c_int
    process_mask, system_mask = ctypes.c_size_t(), ctypes.c_size_t()
    affinity = None
    if affinity_function(process_function(), ctypes.byref(process_mask), ctypes.byref(system_mask)):
        affinity = process_mask.value.bit_count() or None
    result = {"totalMemoryBytes": state.totalPhys, "availableMemoryBytes": state.availPhys,
              "commitHeadroomBytes": state.availPageFile, "affinityCpus": affinity,
              "processCommitHeadroomBytes": state.availPageFile,
              "currentCommitLimitBytes": state.totalPageFile}
    # ullAvailPageFile is allocatable commit for THIS process, not pagefile size
    # or necessarily system-wide headroom. Preserve both and do not assume that
    # an administrator-configured automatic pagefile has already grown.
    try:
        performance = PerformanceInformation(cb=ctypes.sizeof(PerformanceInformation))
        function = kernel.K32GetPerformanceInfo
        function.argtypes = [ctypes.POINTER(PerformanceInformation), ctypes.c_uint32]
        function.restype = ctypes.c_int
        if function(ctypes.byref(performance), performance.cb) and performance.PageSize:
            limit = performance.CommitLimit * performance.PageSize
            used = performance.CommitTotal * performance.PageSize
            result.update(systemCommitLimitBytes=limit, systemCommittedBytes=used,
                          systemCommitHeadroomBytes=max(0, limit - used))
            result["commitHeadroomBytes"] = min(state.availPageFile, max(0, limit - used))
    except (AttributeError, OSError):
        pass
    try:
        counters = ProcessMemoryCounters(cb=ctypes.sizeof(ProcessMemoryCounters))
        function = kernel.K32GetProcessMemoryInfo
        function.argtypes = [ctypes.c_void_p, ctypes.POINTER(ProcessMemoryCounters), ctypes.c_uint32]
        function.restype = ctypes.c_int
        if function(process_function(), ctypes.byref(counters), counters.cb):
            result.update(processWorkingSetBytes=counters.WorkingSetSize,
                          processPrivateCommitBytes=counters.PrivateUsage)
    except (AttributeError, OSError):
        pass
    return result


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
              "commitHeadroomBytes": None, "availableSwapBytes": values.get("SwapFree")}
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
            swap_limit = (root / "memory.swap.max").read_text().strip()
            swap_used = int((root / "memory.swap.current").read_text())
            if swap_limit.isdigit():
                free_swap = max(0, int(swap_limit) - swap_used)
                result["availableSwapBytes"] = min(result.get("availableSwapBytes") or 0, free_swap)
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
    physical = host.get("availableMemoryBytes")
    commit = host.get("commitHeadroomBytes")
    total = host.get("totalMemoryBytes")
    if phase == "il2cpp":
        # Existing OS and Python allocations are already absent from both live
        # headroom values. Reserving another 10% of installed RAM penalized large
        # machines and double counted their current allocations. The 6 GiB parent
        # reserve is FUTURE Unity growth, since admission runs before its launch.
        os_reserve = 2 * GIB
        available = commit if host.get("platform") == "win32" and commit is not None else physical
        if host.get("platform") == "linux" and physical is not None:
            available = physical + (host.get("availableSwapBytes") or 0)
    else:
        os_reserve = max(4 * GIB, int((total or 0) * 0.10))
        available = physical
        if commit is not None and available is not None:
            available = min(available, commit)
    budget = max(0, available - os_reserve - parent) if available is not None else None
    memory_limit = 1 if budget is None else max(1, 1 + (budget - first) // extra)
    if phase == "il2cpp" and physical is not None:
        physical_budget = max(0, physical - os_reserve - parent)
        physical_limit = max(1, 1 + (physical_budget - first) // extra)
        # Use commit/swap only to admit ONE memory-heavy compiler on smaller
        # machines. More workers require actual physical headroom as well.
        memory_limit = min(memory_limit, physical_limit)
    jobs = min(cpu_limit, memory_limit, requested or cpu_limit)
    insufficient = budget is not None and budget < first
    required_commit = first + parent + os_reserve
    physical_shortfall = max(0, required_commit - physical) if physical is not None else None
    commit_shortfall = max(0, required_commit - commit) if commit is not None else None
    memory_known = budget is not None and physical is not None
    launch_allowed = phase != "il2cpp" or (memory_known and not insufficient
                      and (host.get("platform") != "win32" or commit is not None))
    paging = (phase == "il2cpp" and launch_allowed and bool(physical_shortfall)
              and (host.get("platform") == "win32" or bool(host.get("availableSwapBytes"))))
    # Paging is the smaller-machine fallback, never permission to parallelize
    # several giant native frontends against a nominal swap/commit capacity.
    if paging:
        jobs = 1
    failure = ("commit-unknown" if host.get("platform") == "win32" and commit is None else
               "physical-memory-unknown" if physical is None else "memory-headroom-insufficient") if not launch_allowed else None
    policy = {"schema": 1, "phase": phase, "requestedJobs": requested, "jobs": jobs,
            "cpuLimit": cpu_limit, "memoryLimit": memory_limit, "memoryBudgetBytes": budget,
            "osReserveBytes": os_reserve, "parentReserveBytes": parent,
            "largestWorkerReserveBytes": first, "additionalWorkerReserveBytes": extra,
            "memoryKnown": memory_known, "memoryInsufficient": insufficient or paging,
            "nativeCapacityInsufficient": insufficient,
            "nativeLaunchAllowed": launch_allowed, "pagingRequired": paging,
            "requiredCommitHeadroomBytes": required_commit, "pagingExecutionVerified": False,
            "windowsExecutionVerified": False, "physicalShortfallBytes": physical_shortfall,
            "commitShortfallBytes": commit_shortfall, "admissionFailure": failure}
    if phase == "il2cpp":
        policy.update(transientReserveBytes=os_reserve, availablePhysicalMemoryBytes=physical,
                      availableCommitHeadroomBytes=commit,
                      peakEvidenceScope="B625-Linux-Release-single-translation-unit",
                      hostMemoryEvidence={name: host.get(name) for name in (
                          "platform", "totalMemoryBytes", "availableMemoryBytes", "commitHeadroomBytes",
                          "processCommitHeadroomBytes", "currentCommitLimitBytes", "systemCommitLimitBytes",
                          "systemCommittedBytes", "systemCommitHeadroomBytes", "processWorkingSetBytes",
                          "processPrivateCommitBytes", "availableSwapBytes")},
                      admissionMessage=native_admission_error(host, policy))
    return policy


def native_admission_error(host, policy):
    """Actionable native preflight text; no guesses about future pagefile growth."""
    if policy["nativeLaunchAllowed"]:
        return None
    required = policy["requiredCommitHeadroomBytes"] / GIB
    physical = host.get("availableMemoryBytes")
    commit = host.get("commitHeadroomBytes")
    physical_text = "unknown" if physical is None else f"{physical / GIB:.1f} GiB"
    commit_text = "unknown" if commit is None else f"{commit / GIB:.1f} GiB"
    if host.get("platform") == "win32":
        shortfall = policy["commitShortfallBytes"]
        missing = "" if shortfall is None else f" ({shortfall / GIB:.1f} GiB short)"
        return (f"Native compiler needs {required:.1f} GiB of additional available commit for one job; "
                f"Windows reports {commit_text}{missing}, with {physical_text} of available physical RAM. "
                "Close memory-heavy applications or increase the Windows paging-file capacity, then retry. "
                "Installed RAM is not the same as available commit. Automatic paging-file growth is not assumed; "
                "the Builder does not change system settings. Completed work is retained.")
    swap = host.get("availableSwapBytes")
    swap_text = "unknown" if swap is None else f"{swap / GIB:.1f} GiB"
    return (f"Native compiler needs {required:.1f} GiB of available RAM plus known free swap for one job; "
            f"reported RAM is {physical_text}, free swap is {swap_text}. Close memory-heavy applications "
            "or provide more swap within any cgroup limit, then retry. Completed work is retained.")


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
          "requiredCommitHeadroomBytes": policy["requiredCommitHeadroomBytes"],
          "hostMemoryEvidence": policy.get("hostMemoryEvidence"),
          "admissionFailure": policy["admissionFailure"],
          "admissionMessage": policy.get("admissionMessage")}), flush=True)
    if policy["pagingRequired"]:
        print("resources: native compilation requires paging; admitting exactly one job within the reported memory capacity. "
              "This may be significantly slower; paging execution/performance is not yet qualified.", flush=True)
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
