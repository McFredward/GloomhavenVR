#!/usr/bin/env python3
"""Exercise production native-decoration loading against deterministic Addressables boundaries in Unity."""
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-decor')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    base = args.source_root / 'src/GloomhavenVR/WorldUI/TownServices'
    sources = {name: (base / name).read_text().replace('Time.unscaledTime', 'DecorClock.Now') for name in (
        'TownServiceDecor.cs', 'TownServiceDecorMaterial.cs', 'TownServiceArcaneEffect.cs', 'TownServiceWorkspacePractical.cs')}
    # The work prop starts at the same production contact seat as its activity rig.
    motion = (base / 'TownServiceActivityMotion.cs').read_text()
    start = motion.index('    internal static Vector3 CoinSeat(')
    end = motion.index(';', start) + 1
    sources['CoinSeat.cs'] = ('using UnityEngine; namespace GloomhavenVR.WorldUI { '
        'internal static class TownServiceActivityMotion { ' + motion[start:end] + ' } }')
    variants = [('production', None, '', '', ''),
        ('stale-coin-guid', 'TownServiceDecor.cs', '? NativeCoinMaterialAddress : key;', '? key : key;', 'merchant original work coin survives stale native material GUID'),
        ('coin-alias-scope', 'TownServiceDecor.cs', 'piece.Entry == "Treasure.Clutter.Shelf.Individual#1"', 'piece.Entry.Length > 0', 'coin catalog alias cannot rewrite another native prop'),
        ('wrong-coin-identity', 'TownServiceDecor.cs', 'load.Handle.Result.name != "GoldCoinMat"', 'false', 'unexpected coin subasset is never rendered as native coin art'),
        ('global-material-gate', 'TownServiceDecor.cs', 'foreach (MaterialLoad load in piece.Materials)', 'foreach (MaterialLoad load in _loads.Values)', 'unrelated book builds while lantern material fails'),
        ('no-retry', 'TownServiceDecor.cs', 'Attempts >= 3', 'Attempts >= 1', 'independent material retry restores both practicals'),
        ('wrong-atlas-quadrant', 'TownServiceDecorMaterial.cs', 'Vector2.one * tiling', 'Vector2.one * .5f', 'native atlas UVs do not sample stale Standard quadrant'),
        ('template-leak', 'TownServiceDecor.cs', 'CoinTemplate = null;', 'CoinTemplate = CoinTemplate;', 'coin template cannot outlive owner materials'),
        ('workspace-light-in-template', 'TownServiceWorkspacePractical.cs', 'if (owner.isActiveAndEnabled)', 'if (true)', 'inactive frozen template never creates a practical'),
        ('workspace-topology-change', 'TownServiceWorkspacePractical.cs', 'obj.transform.position = transform.TransformPoint(_point);', 'obj.transform.SetParent(transform,false); obj.transform.position = transform.TransformPoint(_point);', 'lighting cannot alter mirrored prop topology'),
        ('workspace-ownership-leak', 'TownServiceWorkspacePractical.cs', 'TownServiceLighting.ForgetPractical(_light!);', '', 'disable immediately releases and darkens standalone light')]
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    fixture = ROOT / 'scripts/town-decor-runtime'
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
        project = build / 'Decor.csproj'
        shutil.copyfile(fixture / 'Decor.csproj', project)
        assembly = 'TownDecor_' + label.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed')], capture_output=True, text=True)
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
    for shader in ('TownNpc.shader', 'TownFlame.shader'):
        shutil.copyfile(args.source_root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders' / shader, project / 'Assets' / shader)
    (project / 'Assets/NativeFixture.shader').write_text('Shader "Amp_TownDecorFixture" { Properties { _MainTex("Atlas",2D)="white"{} _UVTiling("UV",Float)=1 _Tint("Tint",Color)=(1,1,1,0) } SubShader { Pass {} } }')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0"}}')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=240, stdout=subprocess.DEVNULL)
    evidence = Path(manifest['result'])
    if evidence.exists(): print(evidence.read_text())
    if result.returncode or not evidence.exists(): raise SystemExit('FAIL: ' + str(run / 'unity.log'))
    print('PASS: native decor loading/material tests and ten compiled negative controls; evidence: ' + str(run))

if __name__ == '__main__': main()
