"""Owned process trees, cancellation and durable PID creation identities."""
from __future__ import annotations
import ctypes
import os
from pathlib import Path
import signal
import subprocess
import time

from state import Cancelled, WizardError, atomic_json


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

    def run(self, argv, log, *, cwd=None, env=None):
        self.store.check_cancel(self.session)
        log = Path(log); log.parent.mkdir(parents=True, exist_ok=True)
        job = self.job_factory() if os.name == "nt" else None
        options = {"creationflags": WindowsJob.CREATE_SUSPENDED | WindowsJob.CREATE_NEW_PROCESS_GROUP} if job else {"start_new_session": True}
        process = None
        try:
            with log.open("wb") as stream:
                process = subprocess.Popen(list(map(str, argv)), cwd=cwd, env=env,
                                           stdout=stream, stderr=subprocess.STDOUT, **options)
                identity = process_identity(process)
                atomic_json(self.store.session_dir(self.session) / "child.json", {"schema": 1, **identity, "session": self.session})
                if job: job.attach_resume(process)
                while process.poll() is None:
                    try: self.store.check_cancel(self.session)
                    except Cancelled:
                        self.stop(process, job)
                        raise
                    time.sleep(self.poll)
                code = process.wait()
                if code:
                    raise WizardError("child_failed", "A required tool failed; its local log was retained.",
                                      "Ein benötigtes Werkzeug ist fehlgeschlagen; sein lokales Log bleibt erhalten.",
                                      executable=Path(argv[0]).name, exitCode=code, log=str(log))
                return code
        finally:
            if process is not None:
                if process.poll() is None: self.stop(process, job)
                elif job: job.kill()
            if job: job.close()
            (self.store.session_dir(self.session) / "child.json").unlink(missing_ok=True)

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
