#!/usr/bin/env python3
"""Complete production scoped generation override in Unity 2021.3.5, with negative controls."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/scenario-generation-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    args = parser.parse_args()
    if not args.unity.is_file(): parser.error('Real Unity 2021.3.5 is required')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/scenario-generation-runtime'
    source = (args.source_root / 'src/GloomhavenVR/Core/Perf/ScenarioGenerationDetail.cs').read_text()
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(args.source_root.resolve()), 'sha256': hashlib.sha256(source.encode()).hexdigest()}, indent=2) + '\n')
    variants = [('production', source, '')]
    for name, before, after, expected in (
        ('mutate-native-asset', 'copy = UnityEngine.Object.Instantiate(result);', 'copy = result;', 'original native asset is never changed'),
        ('ignore-getter-scope', 'if (_scopeDepth == 0 || !VRSession.IsRunning || result == null', 'if (!_installed || !VRSession.IsRunning || result == null', 'unrelated nested map writer cannot inherit override'),
        ('lose-underground', 'copy._qualityLevel = 0;', 'copy._qualityLevel = 0; copy._showUnderground = false;', 'copy preserves underground'),
        ('change-quality-midturn', 'if (profile.Reduced)', 'if (PerfConfig.ReducedScenarioGenerationOn)', 'live setting does not change current room reveal quality'),
        ('nested-map-inherits', '_scopeDepth = 0; // an unrelated', '// _scopeDepth = 0; // an unrelated', 'unrelated nested map writer cannot inherit override'),
        ('swallow-native-error', 'ScenarioGenerationDetail.Leave(__state); return __exception;', 'ScenarioGenerationDetail.Leave(__state); return null;', 'native exception propagates and scope closes'),
    ):
        count = source.count(before)
        if count != (2 if name == 'swallow-native-error' else 1): raise SystemExit('Mutation binding drift: ' + name)
        variants.append((name, source.replace(before, after), expected))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, text, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        (production / 'Generation.cs').write_text(text)
        project = build / 'Generation.csproj'
        shutil.copyfile(ROOT / 'scripts/scenario-scenery-runtime/Scenery.csproj', project)
        assembly = 'ScenarioGeneration_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2))
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(ROOT / 'scripts/scenario-scenery-runtime/Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run([str(args.unity), '-batchmode', '-nographics', '-projectPath', str(project),
        '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(manifest_path),
        '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see ' + str(run / 'unity.log'))
    print('PASS: complete production generation scope and six runtime negative controls; evidence: ' + str(run))

if __name__ == '__main__': main()
