#!/usr/bin/env python3
"""Exercise production remote flame clock, immutable material sharing and real shader frames in Unity."""
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
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-flame')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    base = args.source_root / 'src/GloomhavenVR/Net/TownServices'
    sources = {name: (base / name).read_text().replace('Time.unscaledTime', 'FlameTestClock.Now') for name in (
        'TownServiceAssets.cs', 'TownServiceFrame.cs', 'TownRackState.cs', 'TownServiceDelta.cs', 'TownServiceMaterial.cs', 'TownServiceBinding.cs', 'TownServiceFlameClock.cs')}
    variants = [('production', None, '', '', ''),
        ('clock-pooling', 'TownServiceMaterial.cs', 'canonical.Numbers[n + 1] = 0f;', 'canonical.Numbers[n + 1] = clock;', 'pooled flame clock is canonical and immutable'),
        ('no-intermediate-clock', 'TownServiceFlameClock.cs', 'now + _ownerOffset - _sample', '0f', 'flame advances between owner packets'),
        ('reordered-clock', 'TownServiceFlameClock.cs', 'sample <= _sample', 'false', 'older owner sample cannot replace current clock'),
        ('clock-rewind', 'TownServiceFlameClock.cs', 'Mathf.Max(_ownerOffset, sample - now)', 'sample - now', 'coalesced late sample preserves elapsed owner clock'),
        ('drop-slot-properties', 'TownServiceFlameClock.cs', '_renderer.GetPropertyBlock(_block, _slot);', '_block.Clear();', 'unrelated slot properties survive playback'),
        ('no-dispose-restore', 'TownServiceFlameClock.cs', '_original.isEmpty ? null : _original', '_block', 'shader replacement restores the original slot property block')]
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    fixture = ROOT / 'scripts/town-flame-runtime'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    for label, filename, before, after, expected in variants:
        build = run / label
        production = build / 'production'
        production.mkdir(parents=True)
        for name, source in sources.items():
            if name == filename:
                if source.count(before) != 1: raise SystemExit('Production binding drift: ' + label)
                source = source.replace(before, after)
            (production / name).write_text(source)
        project = build / 'Flame.csproj'
        shutil.copyfile(fixture / 'Flame.csproj', project)
        assembly = 'TownFlame_' + label.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(args.source_root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': label, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(source.encode()).hexdigest() for name, source in sources.items()}, indent=2))
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest))
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    shutil.copyfile(ROOT / 'scripts/town-activity-runtime/Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    for shader in ('TownFlame.shader',):
        shutil.copyfile(args.source_root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders' / shader, project / 'Assets' / shader)
    shader_text = (project / 'Assets/TownFlame.shader').read_text()
    batching_tag = '"DisableBatching"="True"'
    if shader_text.count(batching_tag) != 1: raise SystemExit('Native billboard batching guard drift')
    (project / 'Assets/TownFlameBatchingNegative.shader').write_text(shader_text.replace(batching_tag, '')
        .replace('Shader "GloomhavenVR/TownFlame"', 'Shader "GloomhavenVR/TownFlameBatchingNegative"'))
    (project / 'Assets/TownFlameTransparentNegative.shader').write_text(shader_text.replace('_FlameCore > .5h', 'false')
        .replace('Shader "GloomhavenVR/TownFlame"', 'Shader "GloomhavenVR/TownFlameTransparentNegative"'))
    (run / 'shader-sha256.txt').write_text(hashlib.sha256(shader_text.encode()).hexdigest() + '\n')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project), '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(path), '-flameEvidence', str(run), '-logFile', str(run / 'unity.log')], timeout=240, stdout=subprocess.DEVNULL)
    evidence = Path(manifest['result'])
    if evidence.exists(): print(evidence.read_text())
    if result.returncode or not evidence.exists(): raise SystemExit('FAIL: ' + str(run / 'unity.log'))
    print('PASS: production flame clock/material/render tests, six compiled clock negatives and original billboard batching negative; evidence: ' + str(run))

if __name__ == '__main__': main()
