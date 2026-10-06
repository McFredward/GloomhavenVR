"""Owned process trees, cancellation and durable PID creation identities."""
from __future__ import annotations
import ctypes
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import time

from state import Cancelled, STAGES, WizardError, atomic_json, ordinary, stage_progress


class ProgressParser:
    """Recognize measured counters, never infer a percentage from elapsed time."""
    def __init__(self):
        self.bee_runs = {}

    def parse(self, line, source="tool"):
        line = line.strip()
        if line.startswith("GHVRQ_PROGRESS "):
            try:
                value = json.loads(line[15:])
                if not isinstance(value, dict) or value.get("schema") != 1: return None
                fields = {name: value.get(name) for name in ("phase", "done", "total", "unit", "detail")}
                stage_progress(**fields)  # The same strict bounds apply at ingestion.
                return fields
            except (ValueError, TypeError, WizardError): return None
        match = re.fullmatch(r"\[\s*(\d+)/(\d+)\s+\d+(?:\.\d+)?s\]\s+(Csc|Clang|Compile|Link|CopyFiles|IL2CPP\w*|Generate\w*|Archive|Lump|Pch|MoveFiles|DeleteFiles)\b(.*)", line)
        if match:
            done, total = int(match[1]), int(match[2])
            if not total or done > total: return None
            previous, generation = self.bee_runs.get((source, total), (-1, 0))
            if done < previous: generation += 1
            self.bee_runs[(source, total)] = done, generation
            return {"phase": f"bee-actions:{source}:{total}:{generation}"[:160], "done": done, "total": total,
                    "unit": "actions", "detail": (match[3] + match[4])[:1024]}
        match = re.fullmatch(r"Installed content files: (\d+)/(\d+)\.", line)
        if match and 0 < int(match[2]) >= int(match[1]):
            return {"phase": "install-content", "done": int(match[1]), "total": int(match[2]), "unit": "files", "detail": line}
        match = re.search(r"(?:Receiving objects|Resolving deltas|Counting objects):\s*\d+%\s*\((\d+)/(\d+)\)", line)
        if match and 0 < int(match[2]) >= int(match[1]):
            return {"phase": "git-transfer:" + source[:100], "done": int(match[1]), "total": int(match[2]), "unit": "objects", "detail": line[:1024]}
        match = re.match(r"^(inspect|snapshot|prepare|graphics|compute|build|package|install):\s+(.+)", line)
        if match:
            return {"phase": match[1], "done": None, "total": None, "unit": None, "detail": match[2][:1024]}
        return None


class LogTail:
    """Incremental bounded reads preserve whole child logs without pipe backpressure."""
    def __init__(self, path):
        self.path, self.offset, self.pending, self.identity = ordinary(path), 0, b"", None

    def read(self, final=False):
        if not self.path.is_file(): return []
        stat = self.path.stat(); identity = (stat.st_dev, stat.st_ino)
        if self.identity != identity or stat.st_size < self.offset:
            self.offset, self.pending, self.identity = 0, b"", identity
        with self.path.open("rb") as stream:
            stream.seek(self.offset); block = stream.read(128 * 1024); self.offset += len(block)
        data = self.pending + block
        lines = re.split(b"[\r\n]", data)
        self.pending = lines.pop()[-65536:]
        if final and self.offset == stat.st_size and self.pending:
            lines.append(self.pending); self.pending = b""
        return [row[:65536].decode("utf-8", errors="replace") for row in lines]


class WindowsJob:
    """Attach suspended children before they can spawn outside their Job Object."""
    CREATE_SUSPENDED = 4
    CREATE_NEW_PROCESS_GROUP = 0x200

    def __init__(self):
        from ctypes import wintypes as w
        self.k = ctypes.WinDLL("kernel32", use_last_error=True)
        self.k.CreateJobObjectW.argtypes = [ctypes.c_void_p, w.LPCWSTR]
        self.k.CreateJobObjectW.restype = w.HANDLE
        self.k.CloseHandle.argtypes = [w.HANDLE]
        self.k.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
        self.k.SetInformationJobObject.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD]
        self.k.TerminateJobObject.argtypes = [w.HANDLE, w.UINT]
        class Basic(ctypes.Structure):
            _fields_ = [("user", ctypes.c_int64), ("job", ctypes.c_int64), ("flags", w.DWORD),
                        ("minimum", ctypes.c_size_t), ("maximum", ctypes.c_size_t), ("active", w.DWORD),
                        ("affinity", ctypes.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]
        class IO(ctypes.Structure):
            _fields_ = [(name, ctypes.c_uint64) for name in ("readOps", "writeOps", "otherOps", "readBytes", "writeBytes", "otherBytes")]
        class Limits(ctypes.Structure):
            _fields_ = [("basic", Basic), ("io", IO), ("processMemory", ctypes.c_size_t),
                        ("jobMemory", ctypes.c_size_t), ("peakProcess", ctypes.c_size_t), ("peakJob", ctypes.c_size_t)]
        self.handle = self.k.CreateJobObjectW(None, None)
        if not self.handle: self.fail("create_job")
        limits = Limits(); limits.basic.flags = 0x2000  # KILL_ON_JOB_CLOSE
        if not self.k.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            self.close(); self.fail("configure_job")

    def fail(self, code):
        raise WizardError(code, "Windows could not supervise the owned build process.", "Windows konnte den Build-Prozess nicht kontrollieren.", winError=ctypes.get_last_error())

    def attach_resume(self, process):
        from ctypes import wintypes as w
        if not self.k.AssignProcessToJobObject(self.handle, int(process._handle)):
            process.kill(); process.wait(); self.fail("assign_job")
        # Popen closes the primary thread HANDLE. Resume its exact suspended
        # thread through the documented Toolhelp/OpenThread APIs, never a PID-only kill.
        class Thread(ctypes.Structure):
            _fields_ = [("size", w.DWORD), ("usage", w.DWORD), ("id", w.DWORD), ("owner", w.DWORD),
                        ("basePriority", w.LONG), ("deltaPriority", w.LONG), ("flags", w.DWORD)]
        self.k.CreateToolhelp32Snapshot.argtypes = [w.DWORD, w.DWORD]
        self.k.CreateToolhelp32Snapshot.restype = w.HANDLE
        self.k.Thread32First.argtypes = [w.HANDLE, ctypes.POINTER(Thread)]
        self.k.Thread32Next.argtypes = [w.HANDLE, ctypes.POINTER(Thread)]
        self.k.OpenThread.argtypes = [w.DWORD, w.BOOL, w.DWORD]; self.k.OpenThread.restype = w.HANDLE
        self.k.ResumeThread.argtypes = [w.HANDLE]; self.k.ResumeThread.restype = w.DWORD
        snapshot = self.k.CreateToolhelp32Snapshot(4, 0)
        if snapshot == ctypes.c_void_p(-1).value: self.fail("thread_snapshot")
        item = Thread(); item.size = ctypes.sizeof(item)
        resumed = False
        try:
            present = self.k.Thread32First(snapshot, ctypes.byref(item))
            while present:
                if item.owner == process.pid:
                    thread = self.k.OpenThread(2, False, item.id)
                    if thread:
                        try: resumed = self.k.ResumeThread(thread) != 0xFFFFFFFF
                        finally: self.k.CloseHandle(thread)
                    break
                present = self.k.Thread32Next(snapshot, ctypes.byref(item))
        finally: self.k.CloseHandle(snapshot)
        if not resumed: self.kill(); process.wait(); self.fail("resume_child")

    def kill(self):
        if self.handle and not self.k.TerminateJobObject(self.handle, 130): self.fail("terminate_job")
        if self.handle:
            from ctypes import wintypes as w
            class Accounting(ctypes.Structure):
                _fields_ = [(name, ctypes.c_int64) for name in ("user", "kernel", "periodUser", "periodKernel")] + [
                    (name, w.DWORD) for name in ("faults", "total", "active", "terminated")]
            self.k.QueryInformationJobObject.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD, ctypes.c_void_p]
            deadline = time.monotonic() + 10
            while True:
                data = Accounting()
                if not self.k.QueryInformationJobObject(self.handle, 1, ctypes.byref(data), ctypes.sizeof(data), None): self.fail("query_job")
                if data.active == 0: break
                if time.monotonic() >= deadline: self.fail("job_shutdown_timeout")
                time.sleep(0.05)

    def close(self):
        if self.handle: self.k.CloseHandle(self.handle); self.handle = None


def process_identity(process):
    if os.name == "nt":
        from ctypes import wintypes as w
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.GetProcessTimes.argtypes = [w.HANDLE, *[ctypes.POINTER(w.FILETIME)] * 4]
        times = [w.FILETIME() for _ in range(4)]
        if not kernel.GetProcessTimes(int(process._handle), *[ctypes.byref(x) for x in times]):
            raise WizardError("process_identity", "Could not record the owned process creation time.")
        start = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
    else:
        # Linux's stable start ticks are evidence only, never authorization to
        # kill a later process. Cancellation uses the owned Popen/group HANDLE.
        stat = Path("/proc") / str(process.pid) / "stat"
        start = stat.read_text().rsplit(")", 1)[1].split()[19] if stat.is_file() else time.time_ns()
    return {"pid": process.pid, "creation": str(start)}


class Supervisor:
    def __init__(self, store, session, *, grace=5, poll=0.1, job_factory=WindowsJob):
        self.store, self.session, self.grace, self.poll = store, session, grace, poll
        self.job_factory = job_factory
        self.stage = None

    def set_stage(self, stage):
        if stage not in STAGES: raise WizardError("invalid_stage", "Invalid wizard stage.")
        self.stage = stage

    def _tail_progress(self, tails, parser, started, *, final=False):
        if self.stage in ("inspect", "build"):
            folder = ordinary(self.store.root / "build/logs")
            if folder.is_dir():
                # Only known logs generated by this attempt are eligible. Old
                # completed logs must not overwrite live stage counters.
                candidates = sorted((entry for entry in folder.iterdir()
                                     if re.fullmatch(r"(?:unity-(?:build|launch)|recovery|recover|dotnet|mod|weave)[A-Za-z0-9_.-]*\.log", entry.name)
                                     and entry.is_file() and not entry.is_symlink() and entry.stat().st_mtime >= started),
                                    key=lambda entry: entry.stat().st_mtime, reverse=True)[:15]
                for path in list(tails):
                    if path.parent == folder and path not in candidates: del tails[path]
                for entry in candidates: tails.setdefault(entry, LogTail(entry))
        for tail in list(tails.values())[:16]:
            for line in tail.read(final=final):
                fields = parser.parse(line, tail.path.name)
                if fields and self.stage:
                    self.store.progress(self.session, self.stage, **fields)

    def run(self, argv, log, *, cwd=None, env=None, timeout=None, on_started=None, on_poll=None, acceptable_codes=(0,)):
        self.store.check_cancel(self.session)
        if timeout is not None and (type(timeout) not in (int, float) or not math.isfinite(timeout) or timeout <= 0):
            raise WizardError("invalid_timeout", "Process timeout must be positive.")
        log = Path(log); log.parent.mkdir(parents=True, exist_ok=True)
        job = self.job_factory() if os.name == "nt" else None
        options = {"creationflags": WindowsJob.CREATE_SUSPENDED | WindowsJob.CREATE_NEW_PROCESS_GROUP} if job else {"start_new_session": True}
        process = None; started = time.monotonic(); started_wall = time.time()
        tails = {log: LogTail(log)}; parser = ProgressParser(); controlled_stop = False
        try:
            with log.open("wb") as stream:
                process = subprocess.Popen(list(map(str, argv)), cwd=cwd, env=env,
                                           stdout=stream, stderr=subprocess.STDOUT, **options)
                identity = process_identity(process)
                atomic_json(self.store.session_dir(self.session) / "child.json", {"schema": 1, **identity, "session": self.session})
                if job: job.attach_resume(process)
                self.store.record(self.session, "process_started", self.stage, executable=Path(argv[0]).name, log=log.name)
                if on_started: on_started(process)
                while process.poll() is None:
                    try: self.store.check_cancel(self.session)
                    except Cancelled:
                        self.stop(process, job)
                        raise
                    self._tail_progress(tails, parser, started_wall)
                    if timeout is not None and time.monotonic() - started >= timeout:
                        raise WizardError("child_timeout", "A required tool exceeded its bounded wait; retry the displayed action.",
                                          "Ein benötigtes Werkzeug hat die Wartezeit überschritten; die angezeigte Aktion erneut ausführen.",
                                          executable=Path(argv[0]).name, durationSeconds=round(time.monotonic() - started, 3), log=str(log))
                    if on_poll and on_poll(process) is False:
                        self.stop(process, job); controlled_stop = True; break
                    time.sleep(self.poll)
                code = process.wait()
                self._tail_progress(tails, parser, started_wall, final=True)
                if code not in acceptable_codes and not controlled_stop:
                    raise WizardError("child_failed", "A required tool failed; its local log was retained.",
                                      "Ein benötigtes Werkzeug ist fehlgeschlagen; sein lokales Log bleibt erhalten.",
                                      executable=Path(argv[0]).name, exitCode=code, log=str(log))
                return 0 if controlled_stop else code
        finally:
            if process is not None:
                if process.poll() is None: self.stop(process, job)
                elif job: job.kill()
            if job: job.close()
            (self.store.session_dir(self.session) / "child.json").unlink(missing_ok=True)
            if process is not None:
                self.store.record(self.session, "process_finished", self.stage, executable=Path(argv[0]).name,
                                  exitCode=process.returncode, durationSeconds=round(time.monotonic() - started, 3), controlledStop=controlled_stop, log=log.name)

    def stop(self, process, job):
        if process.poll() is None:
            try:
                if job: process.send_signal(signal.CTRL_BREAK_EVENT)
                else: os.killpg(process.pid, signal.SIGTERM)
            except (OSError, ProcessLookupError): pass
        deadline = time.monotonic() + self.grace
        while process.poll() is None and time.monotonic() < deadline: time.sleep(self.poll)
        # Kill the entire owned group even if its parent exited gracefully:
        # the parent's compiler descendants may still have inherited open files.
        if job:
            job.kill()
            # Covers a failure before AssignProcessToJobObject: only this exact
            # Popen HANDLE is terminated, never a PID loaded from persisted JSON.
            if process.poll() is None: process.kill()
        else:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
        process.wait()
