"""Stop only compiler daemons retained by one completed private Unity project."""
import os
from pathlib import Path
import select
import signal
import time


_COMPILERS = {b'VBCSCompiler.exe', b'VBCSCompiler.dll'}
_RUNTIMES = {b'dotnet', b'mono', b'mono-sgen'}


def _compiler(proc, project):
    try:
        if proc.stat().st_uid != os.geteuid(): return None
        arguments = (proc / 'cmdline').read_bytes().split(b'\0')
        executable = arguments[0].rsplit(b'/', 1)[-1]
        if executable in _COMPILERS:
            compiler = executable
        elif executable in _RUNTIMES:
            compiler = next((argument.rsplit(b'/', 1)[-1] for argument in arguments[1:]
                             if argument.rsplit(b'/', 1)[-1] in _COMPILERS), None)
        else:
            return None
        if compiler is None: return None
        # Read only a candidate's project identity. Never retain or print its
        # environment, which can contain unrelated credentials.
        wanted = b'PWD=' + os.fsencode(project)
        if wanted not in (proc / 'environ').read_bytes().split(b'\0'): return None
        return compiler.decode('ascii')
    except (OSError, ValueError):
        return None # The process can exit between /proc reads.


def cleanup_project_compilers(project, *, editor_reaped=False):
    """TERM/KILL exact project-owned daemons after the Unity editor was reaped.

    Linux pidfds bind every signal to the inspected process even if a PID is
    reused. All candidates share bounded waits; no global build-server shutdown
    or signal to an unrelated project's process is permitted.
    """
    if editor_reaped is not True:
        raise ValueError('The private Unity editor must be reaped before compiler cleanup.')
    if not hasattr(os, 'pidfd_open') or not hasattr(signal, 'pidfd_send_signal'):
        raise RuntimeError('Project compiler cleanup requires Linux pidfd process identity.')
    project = str(Path(project).resolve())
    result = {'project': project, 'editor_reaped': True, 'matched': [], 'term_sent': [],
              'kill_sent': [], 'remaining': []}
    handles = {}
    try:
        for proc in Path('/proc').iterdir():
            if not proc.name.isdecimal() or int(proc.name) == os.getpid(): continue
            compiler = _compiler(proc, project)
            if compiler is None: continue
            try: descriptor = os.pidfd_open(int(proc.name), 0)
            except ProcessLookupError: continue
            # Recheck after opening the stable handle; /proc itself is PID based.
            if _compiler(proc, project) != compiler:
                os.close(descriptor); continue
            handles[descriptor] = int(proc.name)
            result['matched'].append({'pid': int(proc.name), 'compiler': compiler})

        def send(descriptor, value):
            try: signal.pidfd_send_signal(descriptor, value)
            except ProcessLookupError: return
            result['term_sent' if value == signal.SIGTERM else 'kill_sent'].append(handles[descriptor])

        def wait(pending, seconds):
            deadline = time.monotonic() + seconds
            while pending:
                ready, _, _ = select.select(pending, [], [], max(0, deadline - time.monotonic()))
                pending = [descriptor for descriptor in pending if descriptor not in ready]
                if time.monotonic() >= deadline: break
            return pending

        for descriptor in handles: send(descriptor, signal.SIGTERM)
        pending = wait(list(handles), .3)
        for descriptor in pending: send(descriptor, signal.SIGKILL)
        result['remaining'] = [handles[descriptor] for descriptor in wait(pending, .7)]
        return result
    finally:
        for descriptor in handles: os.close(descriptor)
