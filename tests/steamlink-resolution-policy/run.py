#!/usr/bin/env python3
"""Execute the actual integer policy and compiled negative controls; no VR writes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / '.planning/debug/steamlink-resolution-policy')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    completion = run / 'completion.log'
    completion.write_text('Actual-carrier integer policy and compiled negative controls.\nevidence: ' + str(run) + '\n')
    source = ROOT / 'src/GloomhavenVR/Core/SteamLink/SteamLinkResolutionPolicy.cs'
    inputs = [source, *sorted(path for path in HERE.iterdir() if path.is_file())]
    pins = {str(path.relative_to(ROOT)): digest(path) for path in inputs}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    snapshots = run / 'sources'
    snapshots.mkdir()
    for path in inputs:
        shutil.copyfile(path, snapshots / path.name)
    children = []
    results = []
    code = 1
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    framework = Path.home() / '.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'

    def execute(name, project_template, source_file, expect_success):
        project_dir = run / name
        project_dir.mkdir()
        project = project_dir / (name + '.csproj')
        shutil.copyfile(HERE / project_template, project)
        command = [dotnet, 'build' if project_template == 'Typecheck.csproj' else 'run', '--project', str(project)]
        if project_template == 'Typecheck.csproj':
            command = [dotnet, 'build', str(project)]
        command += ['-c', 'Release', '-p:UseSharedCompilation=false',
                    '-p:SourceFile=' + str(source_file), '-p:FixtureDir=' + str(HERE),
                    '-p:FrameworkReferenceDir=' + str(framework)]
        with (run / (name + '.log')).open('w') as out:
            child = subprocess.Popen(command, stdout=out, stderr=subprocess.STDOUT, start_new_session=True)
            children.append(child)
            try:
                status = child.wait(timeout=90)
            except BaseException:
                if child.poll() is None:
                    os.killpg(child.pid, signal.SIGKILL)
                child.wait()
                raise
        results.append({'name': name, 'exit_code': status, 'expected_success': expect_success})
        assert (status == 0) == expect_success, name + ' has the wrong result; see ' + str(run)
        if not expect_success:
            log = (run / (name + '.log')).read_text(errors='replace')
            assert 'System.InvalidOperationException:' in log, name + ' failed before executing the policy fixture'

    try:
        execute('Typecheck', 'Typecheck.csproj', source, True)
        execute('Production', 'Fixture.csproj', source, True)
        original = source.read_text()
        assignment = 'targetWidth = (longest + EncoderAlignment - 1) / EncoderAlignment * EncoderAlignment;'
        mutations = {
            'Fixed3072': (assignment, 'targetWidth = 3072;'),
            'Ceiling4096': (assignment, 'targetWidth = Math.Min(4096, (longest + EncoderAlignment - 1) / EncoderAlignment * EncoderAlignment);'),
            'RoundDown': (assignment, 'targetWidth = longest / EncoderAlignment * EncoderAlignment;'),
            'ShorterAxis': ('int longest = Math.Max(carrierWidth, carrierHeight);', 'int longest = Math.Min(carrierWidth, carrierHeight);'),
        }
        for name, (old, new) in mutations.items():
            assert original.count(old) == 1, name + ' mutation target changed'
            mutant = snapshots / (name + '.cs')
            mutant.write_text(original.replace(old, new))
            execute(name, 'Fixture.csproj', mutant, False)
        assert pins == {str(path.relative_to(ROOT)): digest(path) for path in inputs}, 'Source changed during execution'
        code = 0
    finally:
        reaped = all(child.poll() is not None for child in children)
        receipt = cleanup_completed_run('steamlink-resolution-policy', code, completion, ROOT, process_reaped=reaped)
        if reaped:
            for item in results:
                for cache in ['bin', 'obj']:
                    path = run / item['name'] / cache
                    if path.is_dir() and not path.is_symlink():
                        shutil.rmtree(path)
        (run / 'cleanup.json').write_text(json.dumps({'retention_api': receipt, 'all_children_reaped': reaped}, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': code, 'results': results,
            'scope': 'PURE_INTEGER_POLICY_ONLY_NO_UNITY_OR_OPENVR_EXECUTED'}, indent=2) + '\n')
        print(json.dumps({'run': str(run), 'exit_code': code}))
    return code


if __name__ == '__main__':
    raise SystemExit(main())
