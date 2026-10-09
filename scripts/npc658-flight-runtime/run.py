#!/usr/bin/env python3
"""Exact native card returns under variable packet delay and artwork refresh."""
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc658-flights')
    parser.add_argument('--case', choices=['coherent', 'prepared'])
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--only-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('return658_sources', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(root / 'scripts/npc-return655-runtime/Return655.cs', fixture / 'Return655.cs')
    shutil.copyfile(Path(__file__).with_name('Flight658.cs'), fixture / 'Flight658.cs')
    card_source = (root / 'src/GloomhavenVR/Cards/VRCard.cs').read_text()
    sampler = loader.method(card_source, 'internal bool TryTownReturnMotion(')
    step = card_source[card_source.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'):
                       card_source.index('            if (ft >= 1f)', card_source.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'))]
    bound['NativeReturnSource655.cs'] = '''using System;
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
    internal void BeginNative655(Vector3 target, float seconds) {
        _flying=true; _flyIntro=true; _flyElapsed=0; _flyDuration=seconds; _townReturnRevision=12;
        _flyFromPos=transform.position; _flyToPos=target; _flyRot=transform.rotation;
        _flyFromScale=transform.localScale; _flyToScale=transform.localScale*.7f;
        _flyArcUp=Vector3.up; _flyArcHeight=.08f;
    }
''' + sampler.replace('TryTownReturnMotion(', 'CaptureNativeTownReturn655(', 1) + '\n    internal void StepNative655() {\n' + step.replace('Time.unscaledDeltaTime', 'global::FlightTime655.Delta') + '\n    }\n}\n'
    chip_source = (root / 'src/GloomhavenVR/Cards/Piles/ItemsPile.cs').read_text()
    chip_start = chip_source.index('        internal bool TryTownReturnMotion(')
    chip_end = chip_source.index('\n        }', chip_start) + 10
    chip_sampler = chip_source[chip_start:chip_end].replace('CardsConfig.', 'NativeReturnConfig655.').replace('Defaults.FanSelectedPopForward', '0f')
    step_start = chip_source.index('                _releaseGlide -= udt;')
    step_end = chip_source.index('\n            }', step_start)
    chip_step = chip_source[step_start:step_end].replace('CardsConfig.', 'NativeReturnConfig655.')
    bound['NativeMerchant655.cs'] = '''using System;
using GloomhavenVR.Hands;
using UnityEngine;
namespace GloomhavenVR.Cards;
internal static class NativeReturnConfig655 {
    internal sealed class Dial { internal float Value; internal Dial(float v) { Value=v; } }
    internal static readonly Dial CardLerpSpeed=new(18f), FanSelectedPopForward=new(0f), ItemFanCloseDuration=new(.35f);
}
internal sealed class NativeMerchant655 : MonoBehaviour {
    private bool IsTownInspection=true, TownOffering, PendingUse, _emerging, _inspectionArtPending, _collapsing;
    private VRHand? Holder;
    private uint _townReturnRevision=12;
    private float _releaseGlide=.55f, _collapseTime, _collapseDelay, _collapseFromScale, _pop, _homeScale=.7f;
    private const float PopUp=0f, PopScale=1f;
    private Vector3 _collapseFrom, _collapseWorld, _homePos=new(-.5f,1.1f,.2f);
    private Quaternion _collapseFromRot, _collapseSpin, _homeRot=Quaternion.Euler(15f,-21f,8f);
    private float Overshoot() => 1.7f;
    private float SeedScale() => .01f;
''' + chip_sampler + '\n    internal void StepNative655() {\n        float udt=FlightTime655.Delta; Vector3 posTarget=_homePos; float scaleTarget=_homeScale;\n' + chip_step + '\n    }\n}\n'
    hashes['ItemsPile.cs (complete native source)'] = hashlib.sha256(chip_source.encode()).hexdigest()
    hashes['NativeMerchant655.cs'] = hashlib.sha256(bound['NativeMerchant655.cs'].encode()).hexdigest()
    hashes['VRCard.cs (complete native source)'] = hashlib.sha256(card_source.encode()).hexdigest()
    hashes['NativeReturnSource655.cs'] = hashlib.sha256(bound['NativeReturnSource655.cs'].encode()).hexdigest()
    # Deterministic transport/render time only; actual source curve and codec stay
    # intact. A real-clock focused suite remains separate inherited evidence.
    for name in ['TownServiceMirror.cs', 'TownServiceMirror.Motion.cs', 'TownServiceMotion.cs']:
        bound[name] = bound[name].replace('Time.unscaledTime', 'global::FlightTime655.Now')
    text = (fixture / 'Program.cs').read_text()
    text = text.replace('            DelayedCensusRace();', '            if (suite != "return655") DelayedCensusRace();', 1)
    anchor = '            if (variant == "production") PublisherNoCloth();'
    assert text.count(anchor) == 1
    text = text.replace(anchor, anchor + '''
            if (suite == "return655") {
                var flight = Return655(variant.Contains("merchant"), variant.Contains("split") || variant == "per-partition-clock");
                while (flight.MoveNext()) yield return flight.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
''', 1)
    text = text.replace('var flight = Return655(variant.Contains("merchant"), variant.Contains("split") || variant == "per-partition-clock");', 'var flight = Flight658(variant.Contains("prepared"));')
    (fixture / 'Program.cs').write_text(text)
    publisher = (fixture / 'Publisher.cs').read_text()
    anchor = '{revision=629;values=ReturnNumbers==null?Array.Empty<float>():(float[])ReturnNumbers.Clone();'
    if publisher.count(anchor) != 1: raise RuntimeError('Native ItemChip sampler adapter binding drift')
    publisher = publisher.replace(anchor, '{if(GetComponent<NativeMerchant655>() is NativeMerchant655 native)return native.TryTownReturnMotion(source,shared,hand,out revision,out values);revision=629;values=ReturnNumbers==null?Array.Empty<float>():(float[])ReturnNumbers.Clone();')
    (fixture / 'Publisher.cs').write_text(publisher)
    cases = [('coherent', None), ('prepared', None)]
    if args.case: cases = [case for case in cases if case[0] == args.case]
    if not args.no_negative_controls:
        controls = [('old-part-admission', 'parts'), ('old-prepared-visibility', 'visibility')]
        scope = {'coherent': 'old-part-admission', 'prepared': 'old-prepared-visibility'}
        cases.extend([case for case in controls if not args.case or case[0] == scope[args.case]])
    if args.only_negative_controls: cases = [case for case in cases if case[1]]
    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    manifest = {'suite': 'return655', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    receipts = {}
    for name, mutation in cases:
        files = dict(bound)
        if mutation == 'parts':
            files['TownServiceMotionBudget.cs'] = subprocess.check_output(['git', 'show', 'bf3444cb502e1eff52bcf0e5194a255109879d36:src/GloomhavenVR/Net/TownServices/TownServiceMotionBudget.cs'], cwd=root, text=True)
        if mutation == 'visibility':
            files['TownServiceMirror.Motion.cs'] = subprocess.check_output(['git', 'show', 'bf3444cb502e1eff52bcf0e5194a255109879d36:src/GloomhavenVR/Net/TownServices/TownServiceMirror.Motion.cs'], cwd=root, text=True).replace('Time.unscaledTime','global::FlightTime655.Now')
        production = run / name / 'production'; production.mkdir(parents=True)
        for file, content in files.items(): (production / file).write_text(content)
        project = run / name / 'Mirror.csproj'; shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'NativeReturn658' + name.replace('-', '')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release',
            '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
            '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (run / name / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': name, 'dll': str(run / name / 'bin/Release/netstandard2.1' / (assembly + '.dll')),
            'expected': ('each moving original retains its printed front on the same first render' if mutation == 'parts' else 'prepared original opens on the first exact native flight') if mutation else ''})
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
    if result.returncode or not (run / 'results.txt').exists(): raise SystemExit('FAIL native flight658: ' + str(run))
    print('PASS native flight658: ' + str(run))


if __name__ == '__main__': main()
