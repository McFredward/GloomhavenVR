#!/usr/bin/env python3
"""Focused real managed observer/source routes; no Steam or device process is contacted."""
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


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / '.planning/debug/steamlink-coverage-bridge')
    parser.add_argument('--suite', choices=['all', 'wire', 'lifecycle', 'callback', 'typecheck'], default='all')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    log = run / 'completion.log'; log.write_text('evidence: ' + str(run) + '\n')
    source = ROOT / 'src/GloomhavenVR/Core/SteamLink'
    startup = ROOT / 'tests/steamlink-scene-startup'
    paths = [source / name for name in ['SteamLinkCoverageBridge.cs', 'SteamLinkSceneRuntime.cs', 'SceneEyeOutput.cs', 'SteamLinkStreamQuality.cs']]
    paths += [p for p in HERE.iterdir() if p.is_file()]
    paths += [startup / name for name in ['Boundary.cs', 'Program.cs', 'Fixture.csproj']]
    pins = {str(p.relative_to(ROOT)): sha(p) for p in paths}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    saved = run / 'sources'; saved.mkdir()
    for index, p in enumerate(paths): shutil.copyfile(p, saved / (str(index) + '-' + p.name))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    children, results = [], []
    code = 1
    def execute(name, command, expected=None):
        target = run / (name + '.log')
        with target.open('w') as output:
            child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            children.append(child)
            try: status = child.wait(timeout=120)
            except BaseException:
                if child.poll() is None: os.killpg(child.pid, signal.SIGKILL)
                child.wait(); raise
        text = target.read_text(errors='replace')
        okay = status == 0 if expected is None else status != 0 and expected in text and 'error CS' not in text
        results.append({'name': name, 'exit_code': status, 'expected_result': okay, 'expected_failure': expected})
        assert okay, name + ':\n' + text[-4096:]
        return text
    def managed(name, contents, project, fixture_dir, options=(), expected=None):
        owned = run / ('owned-' + name); owned.mkdir()
        shutil.copyfile(project, owned / 'Fixture.csproj')
        candidate = owned / 'Production.cs'; candidate.write_text(contents)
        return execute(name, [dotnet, 'run', '--project', str(owned / 'Fixture.csproj'), '-c', 'Release',
            '-p:SourceFile=' + str(candidate), '-p:FixtureDir=' + str(fixture_dir),
            '-p:UseSharedCompilation=false', '--', *options], expected)
    try:
        bridge = (source / 'SteamLinkCoverageBridge.cs').read_text()
        runtime = (source / 'SteamLinkSceneRuntime.cs').read_text()
        if args.suite in ('all', 'wire'):
            managed('wire', bridge, HERE / 'Fixture.csproj', HERE, ['--wire', '--paths'])
            old = '|| !System.Linq.Enumerable.SequenceEqual(packet.Skip(24).Take(16), nonce)'
            assert bridge.count(old) == 1
            managed('no-nonce', bridge.replace(old, ''), HERE / 'Fixture.csproj', HERE, ['--wire'], 'bad field rejected at 24')
            managed('false-operational', bridge.replace('CoverageOperational = accept && operational;', 'CoverageOperational = accept;'),
                    HERE / 'Fixture.csproj', HERE, ['--wire'], 'negotiated does not mean coverage operational')
        if args.suite in ('all', 'lifecycle'):
            managed('lifecycle', bridge, HERE / 'Fixture.csproj', HERE, ['--lifecycle'])
            old = 'if (_wanted && epoch == _epoch && !token.IsCancellationRequested) _current = value;'
            assert bridge.count(old) == 1
            managed('stale-observer', bridge.replace(old, 'if (_wanted) _current = value;'),
                    HERE / 'Fixture.csproj', HERE, ['--lifecycle'], 'late old observer cannot revive permission')
        if args.suite in ('all', 'callback'):
            managed('callback', runtime, startup / 'Fixture.csproj', startup, ['--ordinary-only'])
            old = '? _pack!.TryRenderOrdinary(source, destination, eye)'
            assert runtime.count(old) == 1
            managed('force-atlas', runtime.replace(old, '? _pack!.TryRender(source, destination, eye)'),
                    startup / 'Fixture.csproj', startup, ['--ordinary-only'], 'negotiated source uses ordinary RGBA without an atlas')
            old = 'if (_output.OrdinaryRgba != SteamLinkCoverageBridge.CanAcceptOrdinaryRgba)'
            assert runtime.count(old) == 1
            managed('mid-pair-route', runtime.replace(old, old[:-1] + ' && _epoch == ulong.MaxValue)'),
                    startup / 'Fixture.csproj', startup, ['--ordinary-only'], 'cold route transition retires the old stereo epoch')
            old = 'SteamLinkCoverageBridge.CanAcceptOrdinaryRgba && SteamLinkStreamQuality.LegacyReleased'
            assert runtime.count(old) == 1
            managed('skip-legacy-restore', runtime.replace(old, 'SteamLinkCoverageBridge.CanAcceptOrdinaryRgba'),
                    startup / 'Fixture.csproj', startup, ['--ordinary-only'], 'full RGBA waits for actual legacy settings restoration')
        if args.suite in ('all', 'typecheck'):
            owned = run / 'owned-typecheck'; owned.mkdir()
            shutil.copyfile(HERE / 'Typecheck.csproj', owned / 'Fixture.csproj')
            framework = Path.home() / '.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
            execute('net472-typecheck', [dotnet, 'build', str(owned / 'Fixture.csproj'), '-c', 'Release',
                '-p:SourceFile=' + str(source / 'SteamLinkCoverageBridge.cs'), '-p:FixtureDir=' + str(HERE),
                '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed', '-p:FrameworkReferenceDir=' + str(framework),
                '-p:UseSharedCompilation=false'])
        assert pins == {str(p.relative_to(ROOT)): sha(p) for p in paths}, 'source changed while child ran'
        code = 0
    finally:
        reaped = all(child.poll() is not None for child in children)
        cleanup = cleanup_completed_run('steamlink-coverage-bridge', code, log, ROOT, process_reaped=reaped)
        removed = []
        if reaped:
            for owned in run.glob('owned-*'):
                for name in ('bin', 'obj'):
                    cache = owned / name
                    if cache.is_dir() and not cache.is_symlink():
                        shutil.rmtree(cache); removed.append(str(cache.relative_to(run)))
        (run / 'cleanup.json').write_text(json.dumps({'retention_api': cleanup, 'all_children_reaped': reaped, 'owned_caches_removed': removed}, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': code, 'suite': args.suite, 'cases': results,
            'scope': 'ACTUAL_MANAGED_SOURCE_AND_CALLBACK_WITH_EXPLICIT_OS_GPU_BOUNDARIES',
            'windows_pipe_executed': False, 'native_frame_mapping_proven': False, 'headset_output_proven': False}, indent=2) + '\n')
        print(json.dumps({'run': str(run), 'exit_code': code}))
    return code


if __name__ == '__main__':
    raise SystemExit(main())
