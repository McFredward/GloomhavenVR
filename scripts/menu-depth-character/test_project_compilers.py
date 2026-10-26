#!/usr/bin/env python3
"""Real disposable-process control for exact project compiler cleanup ownership."""
import hashlib
import json
import os
from pathlib import Path
from contextlib import contextmanager
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
from project_compilers import cleanup_project_compilers

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


@contextmanager
def completed_run(run, completion):
    status = 1
    try:
        yield
        status = 0
    finally:
        # The nested temporary process fixture reaps all of its children first,
        # including when an assertion or process launch fails.
        cache = cleanup_completed_run('menu-depth-character-cleanup', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cache, indent=2) + '\n')


def main():
    category = ROOT / '.planning/debug/menu-depth-character-cleanup'; category.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=category))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    children = []; checks = []
    def check(value, message):
        if not value: raise AssertionError(message)
        checks.append(message)
    with completed_run(run, completion), tempfile.TemporaryDirectory(prefix='menu-depth-process-') as temporary:
        source = Path(temporary); project = source / 'owned'; foreign = source / 'foreign'
        project.mkdir(); foreign.mkdir()
        compiler = source / 'VBCSCompiler.exe'; ordinary = source / 'ordinary-worker'
        shutil.copyfile('/bin/sleep', compiler); shutil.copyfile('/bin/sleep', ordinary)
        compiler.chmod(0o755); ordinary.chmod(0o755)
        def spawn(binary, cwd, pwd, ignore_term=False):
            environment = os.environ.copy(); environment['PWD'] = str(pwd)
            child = subprocess.Popen([str(binary), '60'], cwd=cwd, env=environment,
                preexec_fn=(lambda: signal.signal(signal.SIGTERM, signal.SIG_IGN)) if ignore_term else None)
            children.append(child); return child
        try:
            owned = spawn(compiler, project, project)
            stubborn = spawn(compiler, project, project, True)
            other = spawn(compiler, foreign, foreign)
            wrong_pwd = spawn(compiler, project, foreign)
            noncompiler = spawn(ordinary, project, project)
            try: cleanup_project_compilers(project, editor_reaped=False)
            except ValueError: check(True, 'cleanup refuses before the editor is reaped')
            else: raise AssertionError('unreaped editor was accepted')
            check(all(child.poll() is None for child in children), 'refused cleanup leaves every process running')
            receipt = cleanup_project_compilers(project, editor_reaped=True)
            check({item['pid'] for item in receipt['matched']} == {owned.pid, stubborn.pid},
                  'only exact compiler names and exact private PWD are selected')
            check(not receipt['remaining'], 'every selected compiler exits within the bounded wait')
            check(owned.wait(timeout=2) == -signal.SIGTERM, 'ordinary owned compiler exits on TERM')
            check(stubborn.wait(timeout=2) == -signal.SIGKILL and stubborn.pid in receipt['kill_sent'],
                  'TERM-ignoring owned compiler takes the bounded KILL fallback')
            check(other.poll() is None, 'another project compiler remains alive')
            check(wrong_pwd.poll() is None, 'same cwd with another PWD remains alive')
            check(noncompiler.poll() is None, 'a noncompiler in the same project remains alive')
            again = cleanup_project_compilers(project, editor_reaped=True)
            check(not again['matched'] and not again['remaining'], 'completed cleanup is idempotent')
            check(all(child.poll() is None for child in (other, wrong_pwd, noncompiler)),
                  'repeated cleanup preserves every ownership negative control')
            (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
            (run / 'cleanup-repeat.json').write_text(json.dumps(again, indent=2) + '\n')
        finally:
            for child in children:
                if child.poll() is None: child.kill()
                child.wait()
    pins = {}
    for name in ('project_compilers.py', 'test_project_compilers.py', 'run.py'):
        source = Path(__file__).with_name(name); shutil.copyfile(source, run / name)
        pins[name] = hashlib.sha256(source.read_bytes()).hexdigest()
    result = {'status': 'passed', 'assertions': len(checks), 'checks': checks,
              'all_children_reaped': all(child.returncode is not None for child in children), 'source_sha256': pins}
    (run / 'results.json').write_text(json.dumps(result, indent=2) + '\n')
    print('PASS: ' + str(len(checks)) + ' actual process ownership assertions; all children reaped')


if __name__ == '__main__': main()
