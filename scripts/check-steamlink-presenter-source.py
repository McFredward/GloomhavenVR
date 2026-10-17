#!/usr/bin/env python3
"""Focused presenter-v2 source proof; optional real Unity typing and GPU pool proof."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def execute(command, log, *, cwd=None, timeout=180, environment=None):
    with log.open('w') as output:
        child = subprocess.Popen(command, cwd=cwd, env=environment, stdout=output,
                                 stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        except BaseException:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()
            raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--compile-runtime', action='store_true')
    parser.add_argument('--gpu-only', action='store_true', help='Run only new-profile real Unity upload/blit/fence/off controls')
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--output-root', type=Path, default=ROOT / '.planning/debug/steamlink-presenter-source')
    parser.add_argument('--retention-checkout', type=Path, default=ROOT)
    args = parser.parse_args()
    args.output_root = args.output_root.absolute(); args.output_root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_root))
    completion = run / 'completion.log'; completion.write_text('evidence: ' + str(run) + '\n')
    fixture = ROOT / 'tests/steamlink-presenter-source'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    source = ROOT / 'src/GloomhavenVR/Core/SteamLink'
    production = ['ProbePolicy.cs', 'ProbeManifest.cs', 'ProbeTextures.cs', 'PayloadOutput.cs', 'RegistrationPairPolicy.cs',
                  'RegistrationSource.cs', 'PresenterSource.cs', 'RegistrationTextures.cs', 'SteamLinkMaskRuntime.cs']
    pins = {'production': {name: digest(source / name) for name in production},
            'fixture': {str(p.relative_to(fixture)): digest(p) for p in fixture.rglob('*') if p.is_file()}}
    (run / 'source-hashes.json').write_text(json.dumps(pins, indent=2) + '\n')
    result = {'scope': 'focused synthetic presenter-v2 source only', 'gpu_executed': False, 'xr_callbacks_executed': False,
              'native_carriage_proven': False, 'passthrough_authorized': False, 'stages': []}
    code = 1
    def checked(name, command, **options):
        nonlocal code
        code = execute(command, run / (name + '.log'), **options)
        result['stages'].append({'name': name, 'exit_code': code})
        if code:
            raise RuntimeError(name + ' failed; see ' + str(run / (name + '.log')))
    try:
        if not args.gpu_only:
            project_dir = run / 'controls'; project_dir.mkdir()
            project = project_dir / 'PresenterSourceControls.csproj'; shutil.copy2(fixture / project.name, project)
            checked('source-build', [dotnet, 'build', '--disable-build-servers', str(project), '--configfile', str(fixture / 'NuGet.Config'),
                    '-c', 'Release', '-p:SourceRoot=' + str(ROOT), '-p:UseSharedCompilation=false'])
            checked('source-controls', [dotnet, str(project_dir / 'bin/Release/net8.0/PresenterSourceControls.dll'), str(fixture / 'profile.json'), str(run)])
            result['controls'] = json.loads((run / 'source-controls.log').read_text())
        if args.compile_runtime:
            compile_dir = run / 'typing'; compile_dir.mkdir()
            original = ROOT / 'tests/steamlink-mask-runtime'
            project = compile_dir / 'RuntimeCompile.csproj'; shutil.copy2(original / project.name, project)
            framework = Path.home() / '.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
            checked('unity-reference-typecheck', [dotnet, 'build', '--disable-build-servers', str(project), '--configfile', str(original / 'NuGet.Config'),
                    '-c', 'Release', '-p:SourceRoot=' + str(ROOT), '-p:FrameworkReferenceDir=' + str(framework), '-p:UseSharedCompilation=false'])
        if args.gpu_only:
            assert args.unity.is_file() and shutil.which('xvfb-run'), 'Real Unity2021.3.5 and xvfb-run required'
            copied = run / 'production'; copied.mkdir()
            for name in production:
                if name != 'SteamLinkMaskRuntime.cs': shutil.copy2(source / name, copied / name)
            project = run / 'PresenterPoolGpu.csproj'; shutil.copy2(fixture / project.name, project)
            checked('gpu-build', [dotnet, 'build', '--disable-build-servers', str(project), '-c', 'Release', '--configfile', str(fixture / 'NuGet.Config'),
                    '-p:ProductionDir=' + str(copied), '-p:FixtureDir=' + str(fixture),
                    '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'), '-p:UseSharedCompilation=false'])
            assembly = run / 'SteamLinkPresenterPoolGpu.dll'
            shutil.copy2(run / 'bin/Release/netstandard2.1/SteamLinkPresenterPoolGpu.dll', assembly)
            unity = run / 'unity'; (unity / 'Assets/Editor').mkdir(parents=True)
            shutil.copy2(ROOT / 'tests/steamlink-registration-gpu/Editor/RegistrationRunner.cs', unity / 'Assets/Editor/RegistrationRunner.cs')
            (unity / 'ProjectSettings').mkdir()
            (unity / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
            (unity / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 1\n')
            environment = dict(os.environ); environment['LIBGL_ALWAYS_SOFTWARE'] = '0'
            checked('gpu-editor', ['xvfb-run', '-a', str(args.unity), '-batchmode', '-force-vulkan', '-projectPath', str(unity),
                    '-executeMethod', 'RegistrationRunner.Start', '-registrationEvidence', str(run), '-registrationAssembly', str(assembly),
                    '-logFile', str(run / 'unity.log')], timeout=240, environment=environment)
            actual = json.loads((run / 'editor-result.json').read_text()); assert actual['passed'] is True
            rows = [json.loads(x) for x in (run / 'runtime.jsonl').read_text().splitlines()]
            assert rows[-1]['name'] == 'complete' and rows[-1]['new_profile_only'] is True
            result['gpu_executed'] = True; result['gpu_controls'] = rows[-1]
        # Refuse certification if an integrator changed a source while this run compiled it.
        assert pins['production'] == {name: digest(source / name) for name in production}, 'Production source changed during focused run'
        code = 0
        return 0
    except BaseException:
        if code == 0: code = 1
        raise
    finally:
        result['exit_code'] = code
        (run / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
        cleanup = cleanup_completed_run('steamlink-presenter-source', code, completion, args.retention_checkout, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
        print('evidence: ' + str(run)); print(json.dumps(result))


if __name__ == '__main__':
    raise SystemExit(main())
