#!/usr/bin/env python3
"""Current native return receiver under legacy107 independent-part transport.

The exact Build656 motion budget is the declared historical sender boundary.
Build658 current113 atomic publication is separately covered by npc658-card-returns.
"""
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc-return655')
    parser.add_argument('--case', choices=['production', 'merchant', 'split', 'split-merchant', 'publisher'])
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--only-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('return655_sources', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    # Preserve the original independent-part/delayed-receipt challenge. The current
    # sender uses atomic113 and cannot honestly simulate face-before-body delivery.
    legacy_budget_commit = 'bf3444cb502e1eff52bcf0e5194a255109879d36'
    legacy_budget = subprocess.check_output(['git', 'show', legacy_budget_commit + ':src/GloomhavenVR/Net/TownServices/TownServiceMotionBudget.cs'], cwd=root, text=True)
    bound['TownServiceMotionBudget.cs'] = legacy_budget
    hashes['TownServiceMotionBudget.cs (legacy107 Build656 boundary)'] = hashlib.sha256(legacy_budget.encode()).hexdigest()
    old_motion = subprocess.check_output(['git', 'show', 'deb989570:src/GloomhavenVR/Net/TownServices/TownServiceMirror.Motion.cs'], cwd=root, text=True)
    old_return = loader.method(old_motion, 'private static void ApplyCardReturnMotion(')
    old_clock = '        TownServiceMotionEntry entry = sample.Entry;\n        float age = entry.Numbers[0] + Mathf.Max(0f, now - sample.ReceivedAt);'
    if old_return.count(old_clock) != 1: raise RuntimeError('Historical receive-clock source drift')
    hashes['ApplyCardReturnMotion (historical receive-clock source)'] = hashlib.sha256(old_return.encode()).hexdigest()
    (run / 'historical-receive-clock.cs.txt').write_text(old_return)
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(Path(__file__).with_name('Return655.cs'), fixture / 'Return655.cs')
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
    for name in ['TownServiceMirror.cs', 'TownServiceMirror.Motion.cs', 'TownServiceMotion.cs', 'TownServiceMirror.CardReturnCohorts.cs']:
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
    text = text.replace('var flight = Return655(variant.Contains("merchant"), variant.Contains("split") || variant == "per-partition-clock");', 'var flight = variant.Contains("publisher") ? PublisherReturn655() : Return655(variant.Contains("merchant"), variant.Contains("split") || variant == "per-partition-clock");')
    (fixture / 'Program.cs').write_text(text)
    publisher = (fixture / 'Publisher.cs').read_text()
    anchor = '{revision=629;values=ReturnNumbers==null?Array.Empty<float>():(float[])ReturnNumbers.Clone();'
    if publisher.count(anchor) != 1: raise RuntimeError('Native ItemChip sampler adapter binding drift')
    publisher = publisher.replace(anchor, '{if(GetComponent<NativeMerchant655>() is NativeMerchant655 native)return native.TryTownReturnMotion(source,shared,hand,out revision,out values);revision=629;values=ReturnNumbers==null?Array.Empty<float>():(float[])ReturnNumbers.Clone();')
    (fixture / 'Publisher.cs').write_text(publisher)
    cases = [('production', None), ('merchant', None), ('split', None), ('split-merchant', None), ('publisher', None)]
    if args.case: cases = [case for case in cases if case[0] == args.case]
    if not args.no_negative_controls:
        controls = [('old-receive-clock', 'old'), ('old-merchant-clock', 'old'), ('per-partition-clock', 'partition'), ('old-publisher-affinity', 'affinity')]
        scope = {'production': 'old-receive-clock', 'merchant': 'old-merchant-clock', 'split': 'per-partition-clock', 'split-merchant': 'per-partition-clock', 'publisher': 'old-publisher-affinity'}
        cases.extend([case for case in controls if not args.case or case[0] == scope[args.case]])
    if args.only_negative_controls: cases = [case for case in cases if case[1]]
    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    manifest = {'suite': 'return655', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    receipts = {}
    for name, mutation in cases:
        files = dict(bound)
        if mutation == 'old':
            current = loader.method(files['TownServiceMirror.Motion.cs'], 'private static TownServiceMotionEntry CardReturnSample(')
            if files['TownServiceMirror.Motion.cs'].count(current) != 1: raise RuntimeError('Current receive-clock helper binding drift')
            historical = '    private static TownServiceMotionEntry CardReturnSample(MotionSlot sample, float now, out float age)\n    {\n' + old_clock.replace('float age =', 'age =', 1) + '\n        return entry;\n    }'
            files['TownServiceMirror.Motion.cs'] = files['TownServiceMirror.Motion.cs'].replace(current, historical, 1)
        if mutation == 'partition':
            anchor = 'ReturnOffset(state, packet.SampleTime, receivedAt)'
            if files['TownServiceMirror.Motion.cs'].count(anchor) != 1: raise RuntimeError('Return offset control binding drift')
            files['TownServiceMirror.Motion.cs'] = files['TownServiceMirror.Motion.cs'].replace(anchor, 'receivedAt - packet.SampleTime')
        if mutation == 'affinity':
            anchor = 'if (hand == null && cardFlight != null && cardFlight.Hand != 0)'
            if files['TownServiceMirror.Motion.cs'].count(anchor) != 1: raise RuntimeError('Publisher affinity control binding drift')
            files['TownServiceMirror.Motion.cs'] = files['TownServiceMirror.Motion.cs'].replace(anchor, 'if (hand == null && cardFlight != null && cardFlight.Hand > 9)')
        production = run / name / 'production'; production.mkdir(parents=True)
        for file, content in files.items(): (production / file).write_text(content)
        project = run / name / 'Mirror.csproj'; shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'NativeReturn655' + name.replace('-', '')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release',
            '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
            '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (run / name / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': name, 'dll': str(run / name / 'bin/Release/netstandard2.1' / (assembly + '.dll')),
            'expected': ('actual StockSync publishes matching root and native clock before the first return render' if mutation == 'affinity' else 'native return keeps the source curve under variable packet delay') if mutation else ''})
        receipts[name] = {file: hashlib.sha256(content.encode()).hexdigest() for file, content in files.items()}
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    # Program's shared boundary setup imports the original purse even in a card-only suite.
    subprocess.run([os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python')),
        str(root / 'scripts/town-purse-runtime/export-native.py'), str(root), str(run / 'native-purse.json')], check=True)
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
    (run / 'source-hashes.json').write_text(json.dumps({'boundaries': {'sender': 'exact Build656 legacy107 motion budget', 'sender_commit': legacy_budget_commit, 'receiver': 'current source', 'clock_control': 'historical deb989570 receive-age fragment only'}, 'production': hashes, 'cases': receipts,
        'fixture': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.glob('*.cs')}}, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=240,
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
    if result.returncode or not (run / 'results.txt').exists(): raise SystemExit('FAIL native return655: ' + str(run))
    print('PASS native return655: ' + str(run))


if __name__ == '__main__': main()
