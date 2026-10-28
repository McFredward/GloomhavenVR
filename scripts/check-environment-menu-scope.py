#!/usr/bin/env python3
"""Execute the whole custom-environment owner in Unity across menu/map/scenario rigs.

Native/config/audio/gameplay boundaries and tiny RoomGeo/PlaySpace prefabs are
explicit surrogates. This proves current lifecycle/placement and real GL head
pixels, not bundled artwork, original Default sky, HMD timing or peer audio.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/environment-menu-scope')
    parser.add_argument('--production-only', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = root / 'scripts/environment-menu-scope'
    source_path = root / 'src/GloomhavenVR/Core/Environment/SkyAlternative.cs'
    source = source_path.read_text()
    mutations = [
        ('old-menu-gate', '        // OFF (BLACK) — the whole style, in full',
         '        if (!Events.VRModeStateMachine.TableInFrontOfPlayer) { Deactivate(); return false; }\n        // OFF (BLACK) — the whole style, in full',
         'selected custom room is visible in the main menu without a native board'),
        ('menu-subject-lost', 'if (Events.VRModeStateMachine.TableInFrontOfPlayer || rig == null || head == null)',
         'if (rig == null || head == null || !Events.VRModeStateMachine.TableInFrontOfPlayer)',
         'cold main-menu room completes its placement before reveal'),
        ('menu-floor-buried', 'undersideY = floor.y + FloatGapToBoardRatio * extent;',
         'undersideY = floor.y;', 'main-menu room floor matches tracking floor'),
        ('rig-identity-lost', 'if ((_skyGo != null || _roomGo != null) && !ReferenceEquals(_environmentRig, anchor))',
         'if (bool.Parse("false"))', 'a replacement map rig rebuilds the menu room at the map'),
        ('room-follows-rig', '            HealScaleDrift(_skyGo.transform, anchor);',
         '            _roomGo.transform.position = anchor.position;\n            HealScaleDrift(_skyGo.transform, anchor);',
         'menu room remains world-fixed under head and rig motion'),
        ('menu-overrides-native-table', 'if (Events.VRModeStateMachine.TableInFrontOfPlayer || rig == null || head == null)',
         'if (rig == null || head == null)', 'native map width keeps the existing room proportions'),
    ]
    cases = [('production', source, '')]
    for name, before, after, expected in mutations:
        if source.count(before) != 1:
            raise RuntimeError('Mutation source binding drift: ' + name)
        if not args.production_only:
            cases.append((name, source.replace(before, after), expected))
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    paths = [source_path, Path(__file__), *fixture.rglob('*.cs'), fixture / 'EnvironmentScope.csproj']
    pins = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    code = 1; process = None; reaped = True
    env = dict(os.environ, DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1', UseSharedCompilation='false')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    try:
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
        assemblies = run / 'executed-assemblies'; assemblies.mkdir()
        for name, current, expected in cases:
            build = run / name; production = build / 'production'; production.mkdir(parents=True)
            (production / 'SkyAlternative.cs').write_text(current)
            project = build / 'EnvironmentScope.csproj'; shutil.copyfile(fixture / project.name, project)
            assembly = 'EnvironmentScope_' + name.replace('-', '_')
            command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                       '-p:UseSharedCompilation=false', '-nodeReuse:false', '-p:CaseName=' + assembly,
                       '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                       '-p:UnityManaged=' + str(unity.parent / 'Data/Managed')]
            result = subprocess.run(command, capture_output=True, text=True, env=env)
            (build / 'build.log').write_text(result.stdout + result.stderr)
            if result.returncode:
                raise RuntimeError('Compilation failure is not a successful defect control: ' + str(build / 'build.log'))
            dll = assemblies / (assembly + '.dll'); shutil.copyfile(build / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'expected': expected})
        project = run / 'unity'
        for path in ['Assets/Editor', 'Packages', 'ProjectSettings']:
            (project / path).mkdir(parents=True)
        shutil.copyfile(fixture / 'Editor/EnvironmentScopeRunner.cs', project / 'Assets/Editor/EnvironmentScopeRunner.cs')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {name: '1.0.0' for name in
            ['com.unity.modules.physics', 'com.unity.modules.particlesystem', 'com.unity.modules.imageconversion', 'com.unity.modules.jsonserialize']}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod',
                   'EnvironmentScopeRunner.Start', '-environmentScopeManifest', str(path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'):
            command = ['xvfb-run', '-a', *command]
        process = subprocess.Popen(command, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
        reaped = False
        try:
            status = process.wait(timeout=300)
        finally:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait(); reaped = True
        results = (run / 'results.txt').read_text() if (run / 'results.txt').exists() else 'Missing runtime results'
        print(results, end='', flush=True)
        if status or 'FAIL' in results or results.count('PASS') != len(cases):
            raise RuntimeError('Environment runtime failed: ' + str(run / 'unity.log'))
        assert all(p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest() == pins[str(p)] for p in paths), 'Execution input changed during run'
        code = 0
        print('PASS: ' + str(len(cases)) + ' actual environment lifecycle/pixel variants; ' + str(run), flush=True)
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL); process.wait(); reaped = True
        spec = importlib.util.spec_from_file_location('retention', ROOT / 'scripts/town_run_retention.py')
        retention = importlib.util.module_from_spec(spec); spec.loader.exec_module(retention)
        result = retention.cleanup_completed_run('environment-menu-scope', code, completion, ROOT, process_reaped=reaped)
        (run / 'cache-cleanup.json').write_text(json.dumps(result, indent=2) + '\n')
    return code


if __name__ == '__main__':
    raise SystemExit(main())
