#!/usr/bin/env python3
"""Exact native card return across newer artwork and moving approved rig holders."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc-return646')
    parser.add_argument('--case', choices=['production', 'moving-hand'])
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('return646_sources', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(Path(__file__).with_name('Return646.cs'), fixture / 'Return646.cs')
    card_source = (root / 'src/GloomhavenVR/Cards/VRCard.cs').read_text()
    sampler = loader.method(card_source, 'internal bool TryTownReturnMotion(')
    step = card_source[card_source.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'):
                       card_source.index('            if (ft >= 1f)', card_source.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'))]
    bound['NativeReturnSource646.cs'] = '''using System;
using UnityEngine;
using GloomhavenVR.Hands;
namespace GloomhavenVR.Cards;
internal sealed partial class VRCard {
    internal VRHand? Holder;
    private bool _flying, _flyIntro;
    private float _flyElapsed, _flyDuration, _flyArcHeight;
    private uint _townReturnRevision;
    private Vector3 _flyFromPos, _flyToPos, _flyFromScale, _flyToScale, _flyArcUp;
    private Quaternion _flyRot;
    internal void BeginNative646(Vector3 target, float seconds) {
        _flying=true; _flyIntro=true; _flyElapsed=0; _flyDuration=seconds; _townReturnRevision=12;
        _flyFromPos=transform.position; _flyToPos=target; _flyRot=transform.rotation;
        _flyFromScale=transform.localScale; _flyToScale=transform.localScale*.7f;
        _flyArcUp=Vector3.up; _flyArcHeight=.08f;
    }
''' + sampler.replace('TryTownReturnMotion(', 'CaptureNativeTownReturn646(', 1) + '\n    internal void StepNative646() {\n' + step.replace('Time.unscaledDeltaTime', 'global::FlightTime646.Delta') + '\n    }\n}\n'
    hashes['VRCard.cs (complete native source)'] = hashlib.sha256(card_source.encode()).hexdigest()
    hashes['NativeReturnSource646.cs'] = hashlib.sha256(bound['NativeReturnSource646.cs'].encode()).hexdigest()
    # Deterministic transport/render time only; actual source curve and codec stay
    # intact. A real-clock focused suite remains separate inherited evidence.
    for name in ['TownServiceMirror.cs', 'TownServiceMirror.Motion.cs', 'TownServiceMotion.cs']:
        bound[name] = bound[name].replace('Time.unscaledTime', 'global::FlightTime646.Now')
    text = (fixture / 'Program.cs').read_text()
    text = text.replace('            DelayedCensusRace();', '            if (suite != "return646") DelayedCensusRace();', 1)
    anchor = '            if (variant == "production") PublisherNoCloth();'
    assert text.count(anchor) == 1
    text = text.replace(anchor, anchor + '''
            if (suite == "return646") {
                var flight = Return646(variant == "moving-hand");
                while (flight.MoveNext()) yield return flight.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
''', 1)
    (fixture / 'Program.cs').write_text(text)
    cases = [('production', None), ('moving-hand', None)]
    if args.case: cases = [case for case in cases if case[0] == args.case]
    if not args.no_negative_controls:
        cases.append(('old-artwork-filter', 'old'))
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    manifest = {'suite': 'return646', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    receipts = {}
    for name, mutation in cases:
        files = dict(bound)
        if mutation == 'old':
            old = subprocess.check_output(['git', 'show', '2d77ffb65:src/GloomhavenVR/Net/TownServices/TownServiceMirror.Motion.cs'], cwd=root, text=True)
            files['TownServiceMirror.Motion.cs'] = old.replace('Time.unscaledTime', 'global::FlightTime646.Now')
        production = run / name / 'production'; production.mkdir(parents=True)
        for file, content in files.items(): (production / file).write_text(content)
        project = run / name / 'Mirror.csproj'; shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'NativeReturn646' + name.replace('-', '')
        result = subprocess.run([str(Path.home() / '.dotnet/dotnet'), 'build', str(project), '-c', 'Release',
            '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
            '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (run / name / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': name, 'dll': str(run / name / 'bin/Release/netstandard2.1' / (assembly + '.dll')),
            'expected': 'native return keeps its exact per-render curve through newer artwork' if mutation else ''})
        receipts[name] = {file: hashlib.sha256(content.encode()).hexdigest() for file, content in files.items()}
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    # Program's shared boundary setup imports the original purse even in a card-only suite.
    subprocess.run([os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python')),
        str(root / 'scripts/town-purse-runtime/export-native.py'), str(root), str(run / 'native-purse.json')], check=True)
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
    (run / 'source-hashes.json').write_text(json.dumps({'production': hashes, 'cases': receipts,
        'fixture': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.glob('*.cs')}}, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=240,
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
    if result.returncode or not (run / 'results.txt').exists(): raise SystemExit('FAIL native return646: ' + str(run))
    print('PASS native return646: ' + str(run))


if __name__ == '__main__': main()
