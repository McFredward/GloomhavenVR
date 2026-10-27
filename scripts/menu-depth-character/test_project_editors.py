#!/usr/bin/env python3
"""Real escaped-session process control for exact native editor teardown."""
import hashlib
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
from project_compilers import cleanup_project_editors
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def main():
    category = ROOT / '.planning/debug/menu-depth-character724'; category.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=category)); print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    checks = []; processes = []; descriptor = None; status = 1
    def check(value, message):
        if not value: raise AssertionError(message)
        checks.append(message)
    try:
        with tempfile.TemporaryDirectory(prefix='private-unity-control-') as temporary:
            base = Path(temporary); project = base / 'owned'; other = base / 'other'; project.mkdir(); other.mkdir()
            editor = base / 'Unity'; harmless = base / 'different-program'
            shutil.copyfile(sys.executable, editor); editor.chmod(0o755)
            shutil.copyfile(sys.executable, harmless); harmless.chmod(0o755)
            script = 'import signal,subprocess,time;signal.signal(signal.SIGTERM,signal.SIG_IGN);p=subprocess.Popen(["/bin/sleep","60"],start_new_session=True);print(p.pid,flush=True);time.sleep(60)'
            def spawn(executable, target, code):
                process = subprocess.Popen([str(executable), '-c', code, '-projectPath', str(target)], stdout=subprocess.PIPE, start_new_session=True)
                processes.append(process); return process
            try:
                owned = spawn(editor, project, script); child = int(owned.stdout.readline()); descriptor = os.pidfd_open(child)
                foreign = spawn(editor, other, 'import time;time.sleep(60)')
                ordinary = spawn(harmless, project, 'import time;time.sleep(60)')
                check(os.getpgid(owned.pid) != os.getpgrp() and os.getpgid(child) != os.getpgid(owned.pid),
                    'native editor and child truly escape the launcher process group')
                try: cleanup_project_editors(project, editor, launcher_reaped=False)
                except ValueError: check(True, 'teardown refuses before launcher reaping')
                else: raise AssertionError('unreaped launcher accepted')
                receipt = cleanup_project_editors(project, editor, launcher_reaped=True)
                check(set(receipt['matched']) == {owned.pid}, 'only exact executable and project arguments select a native editor')
                check(set(receipt['descendants']) == {child}, 'observed descendants remain owned after leaving the editor process group')
                check(not receipt['remaining'], 'editor and descendant stop within the bounded wait')
                check(owned.wait(timeout=2) == -signal.SIGKILL, 'blocked native editor takes the forced-exit fallback')
                check(bool(select.select([descriptor], [], [], 1)[0]), 'escaped descendant pidfd confirms actual exit')
                check(foreign.poll() is None and ordinary.poll() is None, 'other project and other executable remain running')
                check(not cleanup_project_editors(project, editor, launcher_reaped=True)['matched'], 'native editor cleanup is idempotent')
                (run / 'editor-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
                status = 0
            finally:
                if descriptor is not None:
                    try: signal.pidfd_send_signal(descriptor, signal.SIGKILL)
                    except ProcessLookupError: pass
                    select.select([descriptor], [], [], 1); os.close(descriptor)
                for process in processes:
                    if process.poll() is None: process.kill()
                    process.wait()
    finally:
        receipt = cleanup_completed_run('menu-depth-character-editor-cleanup', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    pins = {}
    for source in (Path(__file__), Path(__file__).with_name('project_compilers.py')):
        shutil.copyfile(source, run / source.name); pins[source.name] = hashlib.sha256(source.read_bytes()).hexdigest()
    (run / 'results.json').write_text(json.dumps({'status': 'passed', 'assertions': len(checks), 'checks': checks,
        'all_direct_children_reaped': all(process.returncode is not None for process in processes), 'sha256': pins}, indent=2) + '\n')
    print('PASS: ' + str(len(checks)) + ' real escaped-editor process assertions; every direct child reaped')


if __name__ == '__main__': main()
