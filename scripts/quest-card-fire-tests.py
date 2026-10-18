#!/usr/bin/env python3
"""Qualify passive burn snapshots against real Unity UI material bindings."""
import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime-source', type=Path, default=ROOT / 'src/GloomhavenVR/Cards/Art/QuestCardFireEvidence.cs')
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    runtime = args.runtime_source.read_text()
    boundary = (ROOT / 'src/GloomhavenVR/Core/QuestStandalonePlatform.cs').read_text()
    assert 'public static bool DebugLogging => Enabled && VRLog.Wants(VRLogLevel.Debug);' in boundary
    variants = [
        ('production', '', '', ''),
        ('debug-gate-lost', '!QuestStandalonePlatform.DebugLogging', 'false', 'Quest/Debug gates precede native reads and formatting'),
        ('idle-missing-property-admitted', '!(Read(source, "_FXAnim") > 0f)', 'Read(source, "_FXAnim") <= 0f', 'idle cards and missing fire metadata do not exhaust the observation lane'),
        ('draw-binding-hidden', 'Material? drawn = renderer != null && renderer.materialCount > 0 ? renderer.GetMaterial() : null;', 'Material? drawn = source;', 'actual base and stale CanvasRenderer material/texture/animation bindings remain distinct'),
        ('same-frame-throttle-lost', '&& now < next)', '&& now < -1f)', 'same-frame samples are throttled before expensive native work'),
        ('row-bound-lost', 'row.Length > RowLimit ? row.Substring(0, RowLimit) : row', 'row', 'one native fire record is bounded to 900 characters'),
        ('card-bound-lost', 'Capacity = 4,', 'Capacity = 5,', 'native fire snapshots retain at most four cards'),
        ('observation-bound-lost', 'ObservationLimit = 128,', 'ObservationLimit = 129,', 'native fire observation lane stops at 128 admitted observations'),
        ('native-input-write', 'Rows[key] = row.Length > RowLimit ? row.Substring(0, RowLimit) : row;', 'Rows[key] = row.Length > RowLimit ? row.Substring(0, RowLimit) : row; source!.SetFloat("_FXAnim", 0f);', 'passive observation neither writes nor materializes native material/color/texture/active state'),
    ]
    if args.case:
        variants = [v for v in variants if v[0] in args.case]
        if len(variants) != len(set(args.case)): parser.error('Unknown or repeated case')
    base = ROOT / '.planning/debug/quest-card-fire'; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base)); log = run / 'runner.log'
    log.write_text('Quest card fire evidence: ' + str(run) + '\n'); print(log.read_text(), end='', flush=True)
    fixture = ROOT / 'tests/QuestCardFire.Tests'
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    code = 1; process = None
    if sys.platform.startswith('linux'):
        if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0: raise OSError('Could not own fixture children')
    try:
        for name, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            source = runtime
            if before:
                assert source.count(before) == 1, 'Causal mutation boundary drift: ' + name
                source = source.replace(before, after, 1)
            (production / 'QuestCardFireEvidence.cs').write_text(source)
            project = case / 'Runtime.csproj'; shutil.copyfile(fixture / 'Runtime.csproj', project)
            assembly = 'QuestCardFire_' + name.replace('-', '_')
            build = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '--disable-build-servers', '-p:UseSharedCompilation=false',
                '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ROOT / 'ressources/GH_Data/Managed/UnityEngine.UI.dll')], capture_output=True, text=True)
            (case / 'build.log').write_text(build.stdout + build.stderr)
            if build.returncode: raise RuntimeError('Actual fire fixture compilation failed: ' + str(case / 'build.log'))
            manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        project = run / 'unity'
        for folder in ('Assets/Plugins', 'Assets/Resources', 'Assets/Editor', 'Packages', 'ProjectSettings'): (project / folder).mkdir(parents=True, exist_ok=True)
        for case in manifest['cases']:
            target = project / 'Assets/Plugins' / Path(case['dll']).name; shutil.copyfile(case['dll'], target); case['dll'] = str(target)
        shutil.copyfile(ROOT / 'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs', project / 'Assets/Editor/DiagnosticRunner.cs')
        shutil.copyfile(fixture / 'Binding.shader', project / 'Assets/Resources/Binding.shader')
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'source-hashes.json').write_text(json.dumps({'runtimeSource': str(args.runtime_source), 'runtimeSha256': hashlib.sha256(runtime.encode()).hexdigest(),
            'gateSha256': hashlib.sha256(boundary.encode()).hexdigest(), 'fixtureFiles': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.iterdir() if p.is_file()},
            'headsetVerified': False, 'nativeFlamePixelTestRepeated': False}, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-force-vulkan', '-projectPath', str(project), '-executeMethod', 'DiagnosticRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
        code = process.wait(timeout=180)
        result = Path(manifest['result'])
        if result.is_file(): print(result.read_text()[-8192:], end='', flush=True)
        if code or not result.is_file(): raise RuntimeError('Actual fire snapshot fixture failed: ' + str(run / 'unity.log'))
        if args.runtime_source.read_text() != runtime: raise RuntimeError('Runtime source changed during qualification')
        print('PASS Quest native fire snapshots: ' + str(len(variants)) + ' production/causal cases; passive evidence only', flush=True)
    finally:
        if process is not None and process.poll() is None: process.kill(); process.wait()
        spec = importlib.util.spec_from_file_location('fire_fixture_cleanup', ROOT / 'scripts/quest-artwork-tests.py')
        helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
        children = helper.reap_fixture_group(process.pid if process else None)
        (run / 'process-cleanup.json').write_text(json.dumps(children, indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-card-fire', code, log, ROOT, process_reaped=True), indent=2) + '\n')


if __name__ == '__main__': main()
