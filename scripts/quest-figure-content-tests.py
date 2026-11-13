#!/usr/bin/env python3
"""Execute the exact alpha installer with real file/ZIP/hash operations; JSON is a portable stub."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time

from town_run_retention import cleanup_completed_run


def reap_owned(group):
    """Reap only this launcher's process group and adopted descendants."""
    reaped, adopted = [], set()
    for signum in (signal.SIGTERM, signal.SIGKILL):
        try:
            if group is not None: os.killpg(group, signum)
        except ProcessLookupError:
            pass
        until = time.monotonic() + 3
        while time.monotonic() < until:
            for entry in Path('/proc').iterdir():
                if not entry.name.isdigit(): continue
                try:
                    fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
                    if int(fields[1]) != os.getpid(): continue
                    adopted.add(int(entry.name)); os.kill(int(entry.name), signum)
                except (FileNotFoundError, ProcessLookupError, PermissionError):
                    pass
            try:
                child, status = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError:
                return {'ownedProcessGroup': group, 'adoptedChildren': sorted(adopted), 'reapedChildren': reaped, 'remainingChildren': 0}
            if child: reaped.append({'pid': child, 'status': status})
            else: time.sleep(.05)
    raise RuntimeError('Owned figure fixture children remain before cleanup')


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--kind', choices=('figure', 'audio'), default='figure')
    parser.add_argument('--runtime-source', type=Path)
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    stem = 'QuestAudioClipContent' if args.kind == 'audio' else 'QuestFigureMeshContent'
    suite = 'quest-' + args.kind + '-content'
    fixture_name = 'QuestAudioContent.Tests' if args.kind == 'audio' else 'QuestFigureContent.Tests'
    if args.runtime_source is None: args.runtime_source = root / 'unity/GloomhavenVR.Quest/Assets/Quest/Runtime' / (stem + '.cs')
    original = args.runtime_source.read_text()
    cases = [('production', '', '', '')]
    controls = [
        ('digest-ignored', 'if (BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant() != item.sha256)', 'if (item.size < 0)', 'ASSERT[bad-part-rejected]'),
        ('warm-rereads', '&& File.ReadAllText(receipt) == proof)', '&& item.size < 0)', 'Owned ' + args.kind + ' part missing'),
        ('temporary-leaked', 'finally { if (File.Exists(temporary)) File.Delete(temporary); }', 'finally { }', 'ASSERT[no-temp-leak]'),
        ('index-not-last', 'Array.Sort(manifest.files, (a, b) => a.path == Index ? 1 : b.path == Index ? -1 : string.CompareOrdinal(a.path, b.path));', '// no publication ordering', 'ASSERT[index-last]'),
    ]
    cases.extend(controls)
    if args.case:
        cases = [case for case in cases if case[0] in args.case]
        if len(cases) != len(set(args.case)): parser.error('Unknown or repeated case')
    base = root / '.planning/debug' / suite; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base))
    runner = run / 'runner.log'; runner.write_text('Quest ' + args.kind + ' content evidence: ' + str(run) + '\n')
    print(runner.read_text(), end='', flush=True)
    borrowed = run / 'sources'; borrowed.mkdir()
    (borrowed / (stem + '.cs')).write_text(original)
    evidence = {'sourcePath': str(args.runtime_source), 'sourceSha256': hashlib.sha256(args.runtime_source.read_bytes()).hexdigest(),
                'scope': 'Portable .NET8 exact runtime helper; actual File/ZipArchive/SHA256/File.Replace; Unity JsonUtility field boundary stubbed.', 'headsetVerified': False, 'cases': []}
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    fixture = root / 'tests' / fixture_name
    environment = dict(os.environ, MSBUILDDISABLENODEREUSE='1')
    process = None; exit_code = 1; children = []
    linux = sys.platform.startswith('linux')
    if linux and ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), 'Cannot own fixture descendants')
    try:
        for name, before, after, expected in cases:
            case = run / name; case.mkdir()
            project = case / 'fixture'; shutil.copytree(fixture, project, ignore=shutil.ignore_patterns('bin', 'obj'))
            source = case / 'sources'; source.mkdir()
            modified = original
            if before:
                if original.count(before) != 1: raise RuntimeError('Causal binding drift: ' + name)
                modified = original.replace(before, after, 1)
            (source / (stem + '.cs')).write_text(modified)
            command = [dotnet, 'build', str(project / (fixture_name + '.csproj')), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '--disable-build-servers', '-p:RuntimeSource=' + str(source)]
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env=environment, start_new_session=True)
            output, _ = process.communicate(timeout=120)
            (case / 'build.log').write_text(output)
            if process.returncode: raise RuntimeError('Fixture compilation failed: ' + str(case / 'build.log'))
            if linux: children.append(reap_owned(process.pid))
            command = [dotnet, str(project / 'bin/Release/net8.0' / (fixture_name + '.dll')), str(case / 'files')]
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env=environment, start_new_session=True)
            output, _ = process.communicate(timeout=30)
            (case / 'console.log').write_text(output)
            passed = process.returncode != 0 and expected in output if expected else process.returncode == 0 and 'PASS ' in output
            evidence['cases'].append({'name': name, 'exitCode': process.returncode, 'expected': expected, 'passed': passed})
            (run / 'results.json').write_text(json.dumps(evidence, indent=2) + '\n')
            if linux: children.append(reap_owned(process.pid))
            if not passed: raise RuntimeError('FAIL ' + name + ': ' + str(case / 'console.log'))
            print('PASS causal ' + name + ': ' + expected if expected else output.strip(), flush=True)
        exit_code = 0
    finally:
        if process is not None and process.poll() is None:
            if linux: os.killpg(process.pid, signal.SIGKILL)
            else: process.kill()
            process.wait()
        if linux: children.append(reap_owned(process.pid if process is not None else None))
        (run / 'process-cleanup.json').write_text(json.dumps(children, indent=2) + '\n')
        cleanup = cleanup_completed_run(suite, exit_code, runner, root, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': main()
