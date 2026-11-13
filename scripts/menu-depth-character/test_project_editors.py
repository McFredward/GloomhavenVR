#!/usr/bin/env python3
"""Real escaped-session process control for exact native editor teardown."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from unittest.mock import patch

sys.dont_write_bytecode = True
from project_compilers import cleanup_project_editors
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def caller_controls(run, check, processes):
    """Exercise the real runner functions, with live escaped editors at their edge."""
    modules = []
    for name in ('results-presentation', 'environment-budget'):
        spec = importlib.util.spec_from_file_location(name, ROOT/'scripts'/('check-'+name+'-runtime.py'))
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        modules.append((name, module))
    original_popen = subprocess.Popen
    with tempfile.TemporaryDirectory(prefix='editor-caller-controls-') as temporary:
        base = Path(temporary); editor = base/'Unity'
        shutil.copyfile(sys.executable, editor); editor.chmod(0o755)
        compiler_binary = base/'dotnet'; shutil.copyfile(sys.executable, compiler_binary); compiler_binary.chmod(0o755)
        for name, module in modules:
            for mode in ('success', 'failure', 'timeout', 'remaining', 'causal',
                         'compiler', 'compiler-borrowed', 'compiler-causal'):
                category = run/(name+'-'+mode); category.mkdir()
                case = Path(tempfile.mkdtemp(prefix='run-', dir=category))
                project = case/'unity'
                for part in ('Assets', 'ProjectSettings', 'Library'): (project/part).mkdir(parents=True)
                (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
                (project/'Library/cache.bin').write_bytes(b'regenerable cache')
                evidence = case/'results.txt'; evidence.write_text('Retained original evidence\n')
                completion = case/'completion.log'; completion.write_text('Evidence: '+str(case)+'\n')
                pid_file = case/'escaped.json'; launchers = []; handles = []; events = []
                expected_code = 7 if mode == 'failure' else 1 if mode in ('timeout', 'remaining', 'causal') else 0
                real_editor_cleanup = module.editor_tools.cleanup_project_editors
                real_compiler_cleanup = module.run_tools.cleanup_run_compilers
                real_retention = module.cleanup_completed_run
                def spawn(*args, **kwargs):
                    process = original_popen(*args, **kwargs); processes.append(process); launchers.append(process)
                    return process
                def editor_cleanup(*args, **kwargs):
                    check(bool(launchers) and all(p.returncode is not None for p in launchers), name+' '+mode+': launcher reaped before editor teardown')
                    check((project/'Library/cache.bin').is_file(), name+' '+mode+': cache survives until exact editor teardown')
                    events.append('editor')
                    if pid_file.exists():
                        pids = json.loads(pid_file.read_text())
                        handles.extend(os.pidfd_open(pid) for pid in pids)
                    if mode in ('remaining', 'causal'):
                        # Causal control: an omitted exact teardown used to claim
                        # success while the escaped editor and its child lived.
                        return {'matched': pids, 'remaining': pids if mode == 'remaining' else []}
                    receipt = real_editor_cleanup(*args, **kwargs)
                    if mode.startswith('compiler'):
                        check(not receipt['matched'], name+' '+mode+': normal editor exit leaves no live editor root for orphan discovery')
                    return receipt
                def compiler_cleanup(*args, **kwargs):
                    check(events == ['editor'] and kwargs.get('editor_reaped') is True,
                          name+' '+mode+': compiler ownership check follows exact editor exit')
                    events.append('compiler')
                    if mode == 'compiler-causal': return {'matched': [], 'refused': [], 'remaining': []}
                    return real_compiler_cleanup(*args, **kwargs)
                def retention(suite, status, log, checkout, **kwargs):
                    check(events == ['editor', 'compiler'], name+' '+mode+': editor and compiler teardown precede retention')
                    check(all(select.select([fd], [], [], 0)[0] for fd in handles), name+' '+mode+': live private process before retention')
                    check(kwargs.get('process_reaped') is True and status == expected_code,
                          name+' '+mode+': retention receives actual reaping and unchanged exit status')
                    events.append('retention')
                    return real_retention(suite, status, log, checkout, **kwargs)
                if mode.startswith('compiler'):
                    bee = project/'Library/Bee'; bee.mkdir(); private_file = bee/'fixture.dag'; private_file.write_text('owned')
                    foreign = run/'foreign-compiler-input'; foreign.write_text('borrowed independently')
                    foreign_arg = str(foreign) if mode == 'compiler-borrowed' else None
                    release = case/'release-foreign'; closed = case/'foreign-closed'
                    compiler_code = ('import json,os,time;from pathlib import Path;'
                        'owned=open('+repr(str(private_file))+');'
                        'foreign=open('+repr(foreign_arg)+') if '+repr(foreign_arg)+' else None;'
                        'Path('+repr(str(pid_file))+').write_text(json.dumps([os.getpid()]))\n'
                        'while not Path('+repr(str(release))+').exists(): time.sleep(.01)\n'
                        'if foreign: foreign.close()\n'
                        'Path('+repr(str(closed))+').write_text("closed");time.sleep(60)')
                    compiler_args = [str(compiler_binary), '-c', compiler_code, 'VBCSCompiler.dll']
                    editor_code = ('import subprocess,time;from pathlib import Path;'
                        'subprocess.Popen('+repr(compiler_args)+',cwd='+repr(str(base))+',start_new_session=True)\n'
                        'while not Path('+repr(str(pid_file))+').exists(): time.sleep(.01)')
                    command = [str(editor), '-c', editor_code, '-projectPath', str(project)]
                elif mode in ('timeout', 'remaining', 'causal'):
                    child_code = ('import json,os,signal,subprocess,time;from pathlib import Path;'
                                  'signal.signal(signal.SIGTERM,signal.SIG_IGN);'
                                  'p=subprocess.Popen(["/bin/sleep","60"],start_new_session=True);'
                                  'Path('+repr(str(pid_file))+').write_text(json.dumps([os.getpid(),p.pid]));time.sleep(60)')
                    editor_args = [str(editor), '-c', child_code, '-projectPath', str(project)]
                    launcher_code = 'import subprocess;p=subprocess.Popen('+repr(editor_args)+',start_new_session=True);p.wait()'
                    command = [sys.executable, '-c', launcher_code]
                else:
                    command = [str(editor), '-c', 'import sys;sys.exit('+str(expected_code)+')', '-projectPath', str(project)]
                caught = None
                try:
                    with patch.object(module.subprocess, 'Popen', spawn), \
                         patch.object(module.editor_tools, 'cleanup_project_editors', editor_cleanup), \
                         patch.object(module.run_tools, 'cleanup_run_compilers', compiler_cleanup), \
                         patch.object(module, 'cleanup_completed_run', retention):
                        try:
                            if name == 'results-presentation':
                                status = 1
                                try: status = module.command(command, case/'launcher.log', timeout=2)
                                finally: module.complete_editor_run(case, project, editor, status, completion)
                                result_code = status
                            else:
                                result_code = module.editor_command(command, project, editor, case, os.environ.copy(), timeout=2).returncode
                        except (subprocess.TimeoutExpired, RuntimeError, AssertionError) as error:
                            caught = error
                    if mode in ('success', 'failure', 'compiler'):
                        check(caught is None and result_code == expected_code, name+' '+mode+': native return code preserved')
                        check(not (project/'Library').exists(), name+' '+mode+': completed cache removed')
                    elif mode == 'timeout':
                        check(isinstance(caught, subprocess.TimeoutExpired), name+': timeout remains a failure after exact cleanup')
                        check(not (project/'Library').exists(), name+': timeout cache removed after escaped editor and child exit')
                    elif mode == 'remaining':
                        check(isinstance(caught, RuntimeError) and 'failed to stop' in str(caught), name+': remaining editor refuses completion')
                        check(events == ['editor'] and (project/'Library/cache.bin').exists() and (case/'.keep').exists(),
                              name+': remaining editor preserves caches and pins outer-suite cleanup')
                    elif mode == 'compiler-borrowed':
                        check(isinstance(caught, RuntimeError) and 'compiler ownership/exit unresolved' in str(caught),
                              name+': borrowed compiler refuses completion after normal editor exit')
                        check(events == ['editor', 'compiler'] and (project/'Library/cache.bin').exists() and (case/'.keep').exists()
                              and all(not select.select([fd], [], [], 0)[0] for fd in handles),
                              name+': foreign compiler input and live owned cache survive refusal')
                    else:
                        check(isinstance(caught, AssertionError) and 'live private process before retention' in str(caught),
                              name+': omitted exact teardown is rejected by the live-process control')
                        check((project/'Library/cache.bin').exists(), name+': causal control stops before deleting live caches')
                    check(evidence.read_text() == 'Retained original evidence\n', name+' '+mode+': original evidence retained')
                    (case/'control-result.json').write_text(json.dumps({'mode': mode, 'events': events,
                        'expected_exit_code': expected_code, 'caught': type(caught).__name__ if caught else None}, indent=2)+'\n')
                finally:
                    (case/'.keep').write_text('Control process cleanup in progress.\n')
                    editors = real_editor_cleanup(project, editor, launcher_reaped=True)
                    (case/'control-editor-cleanup.json').write_text(json.dumps(editors, indent=2)+'\n')
                    if editors['remaining']: raise RuntimeError('Control editor cleanup incomplete')
                    if mode.startswith('compiler') and handles and any(not select.select([fd], [], [], 0)[0] for fd in handles):
                        release.write_text('release foreign input')
                        deadline = time.monotonic()+2
                        while not closed.exists() and time.monotonic()<deadline: time.sleep(.01)
                        if not closed.exists(): raise RuntimeError('Control compiler did not release its foreign input')
                    compilers = real_compiler_cleanup(case, editor_reaped=True)
                    (case/'control-compiler-cleanup.json').write_text(json.dumps(compilers, indent=2)+'\n')
                    if compilers['remaining'] or compilers['refused']: raise RuntimeError('Control compiler cleanup incomplete')
                    for fd in handles:
                        if not select.select([fd], [], [], 1)[0]: raise RuntimeError('Control descendant still running')
                        os.close(fd)
                    (case/'.keep').unlink(missing_ok=True)
                    receipt = real_retention('editor-caller-controls', 1 if caught else expected_code, completion, ROOT, process_reaped=True)
                    (case/'control-cache-cleanup.json').write_text(json.dumps(receipt, indent=2)+'\n')
        results = modules[0][1]
        category = run/'results-followups'; category.mkdir()
        followup = Path(tempfile.mkdtemp(prefix='run-', dir=category))
        completion = followup/'completion.log'; completion.write_text('Evidence: '+str(followup)+'\n')
        (followup/'result.json').write_text('{"exit_code":0}')
        try:
            results.required_followup(followup, 'native_transport', [sys.executable, '-c', ''], time.monotonic())
            try: results.required_followup(followup, 'head', [sys.executable, '-c', 'import sys;sys.exit(7)'], time.monotonic())
            except AssertionError: pass
            else: raise AssertionError('Failed required followup accepted')
            report = json.loads((followup/'result.json').read_text())
            check(report['exit_code'] == report['required_head_exit_code'] == 7 and report['required_native_transport_exit_code'] == 0,
                  'Results required followup preserves nonzero status and preceding successful receipt')
            real_command = results.command
            with patch.object(results, 'command', lambda command, log, timeout: real_command(command, log, timeout=.1)):
                try: results.required_followup(followup, 'head', [sys.executable, '-c', 'import time;time.sleep(60)'], time.monotonic())
                except subprocess.TimeoutExpired: pass
                else: raise AssertionError('Timed-out required followup accepted')
            report = json.loads((followup/'result.json').read_text())
            check(report['exit_code'] == report['required_head_exit_code'] == -1 and report['required_native_transport_exit_code'] == 0,
                  'Results timed-out required followup retains failure and preceding successful receipt')
        finally:
            receipt = cleanup_completed_run('editor-caller-followups', 1, completion, ROOT, process_reaped=True)
            (followup/'cleanup.json').write_text(json.dumps(receipt, indent=2)+'\n')


def main():
    started = time.monotonic()
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
            finally:
                if descriptor is not None:
                    try: signal.pidfd_send_signal(descriptor, signal.SIGKILL)
                    except ProcessLookupError: pass
                    select.select([descriptor], [], [], 1); os.close(descriptor)
                for process in processes:
                    if process.poll() is None: process.kill()
                    process.wait()
        caller_controls(run, check, processes)
        status = 0
    finally:
        receipt = cleanup_completed_run('menu-depth-character-editor-cleanup', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    pins = {}
    for source in (Path(__file__), Path(__file__).with_name('project_compilers.py'),
                   ROOT/'scripts/check-results-presentation-runtime.py', ROOT/'scripts/check-environment-budget-runtime.py',
                   ROOT/'scripts/town_run_retention.py', ROOT/'scripts/check-environment-menu-scope.py'):
        shutil.copyfile(source, run / source.name); pins[source.name] = hashlib.sha256(source.read_bytes()).hexdigest()
    (run / 'results.json').write_text(json.dumps({'status': 'passed', 'assertions': len(checks), 'checks': checks,
        'all_direct_children_reaped': all(process.returncode is not None for process in processes),
        'elapsed_seconds': time.monotonic()-started, 'sha256': pins}, indent=2) + '\n')
    print('PASS: ' + str(len(checks)) + ' real escaped-editor process assertions; every direct child reaped')


if __name__ == '__main__': main()
