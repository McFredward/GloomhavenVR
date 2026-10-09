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
    parser.add_argument('--case', choices=['coherent', 'prepared', 'capacity', 'native', 'clock'])
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--only-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('return658_sources', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    cohort = root / 'src/GloomhavenVR/Net/TownServices/TownServiceMirror.CardReturnCohorts.cs'
    bound[cohort.name] = cohort.read_text().replace('Time.unscaledTime','global::FlightTime655.Now')
    hashes[cohort.name] = hashlib.sha256(cohort.read_bytes()).hexdigest()
    # Exact integration seams supplied to the parent: shared Mirror.cs is owned
    # by latency/integrator. Keep the source and explicit seam receipts separately.
    raw_mirror = bound['TownServiceMirror.cs']
    mirror = raw_mirror
    seams = [
        ('PreserveReturningCardHeader(module, frame);',
         '                    if (frame.VisitorStock && HidePreparedCardReturn(source)) frame.Visible = false;',
         '\n                    PreserveReturningCardHeader(module, frame);', False),
        ('bool heldReturn = module != null && HoldIncomingReturnPicture(entry.Key, module, frame);',
         '                    Transform mount = parent;',
         '                    bool heldReturn = module != null && HoldIncomingReturnPicture(entry.Key, module, frame);\n', True),
        ('RestoreIncomingReturnPicture(module, heldReturn);',
         '                    HideDormantCatalogOriginal(entry.Key, frame, module);',
         '\n                    RestoreIncomingReturnPicture(module, heldReturn);', False)]
    seam_receipt=[]
    for call,anchor,insertion,before in seams:
        count=mirror.count(call)
        if count>1: raise RuntimeError('Duplicate flight integration seam: '+call)
        if count==0:
            if mirror.count(anchor)!=1: raise RuntimeError('Flight integration seam drift: '+anchor)
            mirror=mirror.replace(anchor,insertion+anchor if before else anchor+insertion)
        seam_receipt.append({'call':call,'already_integrated':count==1})
    bound['TownServiceMirror.cs']=mirror
    hashes['TownServiceMirror.cs (raw production)']=hashlib.sha256(raw_mirror.encode()).hexdigest()
    hashes['TownServiceMirror.cs (explicit integration seams)']=hashlib.sha256(mirror.encode()).hexdigest()
    (run/'integration-seams.json').write_text(json.dumps(seam_receipt,indent=2)+'\n')
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(root / 'scripts/npc-return655-runtime/Return655.cs', fixture / 'Return655.cs')
    shutil.copyfile(Path(__file__).with_name('Flight658.cs'), fixture / 'Flight658.cs')
    reader = (root/'scripts/town-first-picture632-runtime/FirstPicture632.cs').read_text().split('    private static IEnumerator FirstPicture632()')[0]+'}\n'
    reader = reader.replace('NativeRow632(Transform parent,string localized)', 'NativeRow632(Transform parent,string localized,string panel="ability-card")').replace('"NativeFirstPicture632"','"NativeFlight658",panel')
    reader = reader.replace('new Material(TMP_Settings.defaultFontAsset.material)','new Material(Shader.Find("TextMeshPro/Distance Field"))').replace('GameMaterials632.Add(material.key,restored);','restored.SetTexture("_MainTex",TMP_Settings.defaultFontAsset.atlasTextures[0]); GameMaterials632.Add(material.key,restored);')
    (fixture/'NativeReader658.cs').write_text(reader)
    for relative in ['Cards/Art/CardMesh.cs','Cards/Art/CardContour.cs','Cards/Caps/CapFaceLayout.cs','WorldUI/TownServices/TownServiceCardBody.cs']:
        path = root/'src/GloomhavenVR'/relative
        bound[path.name] = path.read_text(); hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    enums=(root/'src/GloomhavenVR/Cards/CardsEnums.cs').read_text()
    bound['NativeControlBoard658.cs']='namespace GloomhavenVR.Cards; '+enums[enums.index('internal enum ControlBoard'):enums.index('\n}',enums.index('internal enum ControlBoard'))+2]
    bound['NativeCohortProbe658.cs']='using System.Linq; namespace GloomhavenVR.Net.TownServices; internal static partial class TownServiceMirror { internal static int Cohorts658(int owner)=>MotionPeers.TryGetValue(owner,out var peer)?peer.ReturnCohorts.Count:0; internal static int ReturnClocks658(int owner)=>MotionPeers.TryGetValue(owner,out var peer)?peer.Slots.Values.Count(slot=>slot.Entry.Kind==8):0; }'
    bound['NativeClockProbe658.cs']='namespace GloomhavenVR.Net.TownServices; internal static partial class TownServiceMirror { internal sealed class ClockProbe658 { private readonly CardReturnClock clock; internal ClockProbe658(TownServiceMotionEntry entry,float time)=>clock=new CardReturnClock(entry,time,0f); internal void Observe(TownServiceMotionEntry entry,float time)=>clock.Observe(entry,time,time); internal TownServiceMotionEntry Current(float time,out float age)=>clock.Current(time,out age); } }'
    bound['NativePrintPartitions658.cs']='using System.Collections.Generic; using UnityEngine; namespace GloomhavenVR.WorldUI; internal static partial class LazyTemplateProbe { internal static List<Part> NativePrintPartitions658(Transform root) { var parts=new List<Part>(); Partition(root,string.Empty,parts); return parts; } }'
    lazy = fixture/'LazyTemplate.cs'
    lazy.write_text(lazy.read_text().replace('    internal static class TownServiceCardBody\n    { internal static void RebindClone(string key,GameObject clone) { } }\n',''))
    (fixture/'NativeBodyCacheBoundary658.cs').write_text('namespace GloomhavenVR.Net { internal static class PeerBoardFade { internal static bool SetSubmeshMaterial(UnityEngine.MeshRenderer r,int i,UnityEngine.Material m) { var a=r.sharedMaterials;a[i]=m;r.sharedMaterials=a;return true; } } } namespace BepInEx { internal static class Paths { internal static string ConfigPath => System.IO.Path.Combine(UnityEngine.Application.dataPath,"IsolatedCardCache658"); } } namespace GloomhavenVR { internal static class MyPluginInfo { internal const string PLUGIN_GUID="flight658"; } }')

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
''' + sampler.replace('TryTownReturnMotion(', 'CaptureNativeTownReturn655(', 1) + '\n    internal void StepNative655() {\n' + step.replace('Time.unscaledDeltaTime', 'global::FlightTime655.Delta') + '\n        if (ft >= 1f) _flying=false; // Exact native terminal flag; gameplay callbacks are the boundary.\n    }\n}\n'
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
''' + chip_sampler + '\n    internal void StepNative655() {\n        float udt=Mathf.Min(FlightTime655.Delta,.05f); Vector3 posTarget=_homePos; float scaleTarget=_homeScale;\n' + chip_step + '\n    }\n}\n'
    collapse=chip_source[chip_source.index('                float cdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'):chip_source.index('                if (ct >= 1f)',chip_source.index('                float cdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'))]
    collapse=collapse.replace('Time.unscaledDeltaTime','FlightTime655.Delta').replace('CardsConfig.','NativeReturnConfig655.')
    native=bound['NativeMerchant655.cs'];last=native.rindex('}\n')
    native=native[:last]+'''
    internal void BeginCollapse658(Vector3 target) {
        _collapsing=true;_collapseTime=0;_collapseDelay=0;_collapseFrom=transform.position;_collapseWorld=target;
        _collapseFromRot=transform.rotation;_collapseSpin=Quaternion.Euler(20f,50f,10f);_collapseFromScale=transform.localScale.x;
    }
    private static float EaseInBack(float t,float s)=>t*t*((s+1f)*t-s);
    internal void StepCollapse658() {
'''+collapse+'\n    }\n'+native[last:]
    bound['NativeMerchant655.cs']=native
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
    text = text.replace('var flight = Return655(variant.Contains("merchant"), variant.Contains("split") || variant == "per-partition-clock");', 'var flight = variant.Contains("clock") ? SlowClock658() : variant == "capacity" || variant == "old-cohort-priority" ? Capacity658() : variant.Contains("native") ? NativeFlight658() : Flight658(variant.Contains("prepared"));')
    (fixture / 'Program.cs').write_text(text)
    publisher = (fixture / 'Publisher.cs').read_text()
    anchor = '{revision=629;values=ReturnNumbers==null?Array.Empty<float>():(float[])ReturnNumbers.Clone();'
    if publisher.count(anchor) != 1: raise RuntimeError('Native ItemChip sampler adapter binding drift')
    publisher = publisher.replace(anchor, '{if(GetComponent<NativeMerchant655>() is NativeMerchant655 native)return native.TryTownReturnMotion(source,shared,hand,out revision,out values);revision=629;values=ReturnNumbers==null?Array.Empty<float>():(float[])ReturnNumbers.Clone();')
    (fixture / 'Publisher.cs').write_text(publisher)
    cases = [('coherent', None), ('prepared', None), ('capacity',None), ('native',None), ('clock',None)]
    if args.case: cases = [case for case in cases if case[0] == args.case]
    if not args.no_negative_controls:
        controls = [('old-part-admission', 'parts'), ('old-prepared-visibility', 'visibility'), ('old-native-part-admission', 'native-parts'), ('old-native-header', 'native-header'), ('old-wall-clock', 'clock'), ('old-cohort-priority','priority')]
        scope = {'coherent': 'old-part-admission', 'prepared': 'old-prepared-visibility', 'clock': 'old-wall-clock', 'capacity':'old-cohort-priority'}
        cases.extend([case for case in controls if not args.case or case[0] == scope.get(args.case) or args.case=='native' and case[1].startswith('native')])
    if args.only_negative_controls: cases = [case for case in cases if case[1]]
    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    manifest = {'suite': 'return655', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    receipts = {}
    for name, mutation in cases:
        files = dict(bound)
        if mutation in ('parts','native-parts'):
            files['TownServiceMotionBudget.cs'] = subprocess.check_output(['git', 'show', 'bf3444cb502e1eff52bcf0e5194a255109879d36:src/GloomhavenVR/Net/TownServices/TownServiceMotionBudget.cs'], cwd=root, text=True)
        if mutation == 'priority':
            # Restore the prior exact priority seam: native groups came before
            # any ordinary turn. Keep the independently repaired snapshot lifetime.
            budget=files['TownServiceMotionBudget.cs'];begin=budget.index('        bool nativeReturn = live.Exists(');end=budget.index('        // One physical card can contain',begin)
            files['TownServiceMotionBudget.cs']=(budget[:begin]+budget[end:]).replace('    private static int _returnRecoveryTurn;\n','')
        if mutation == 'clock':
            old = subprocess.check_output(['git','show','bf3444cb502e1eff52bcf0e5194a255109879d36:src/GloomhavenVR/Net/TownServices/TownServiceMirror.Motion.cs'],cwd=root,text=True)
            begin=old.index('    private sealed class CardReturnClock');end=old.index('    private sealed class SourceMotion',begin)
            motion=files['TownServiceMirror.Motion.cs'];first=motion.index('    private sealed class CardReturnClock');last=motion.index('    private sealed class SourceMotion',first)
            files['TownServiceMirror.Motion.cs']=motion[:first]+old[begin:end]+motion[last:]
        if mutation == 'native-header':
            files['TownServiceMirror.cs'] = files['TownServiceMirror.cs'].replace('                    PreserveReturningCardHeader(module, frame);','')
        if mutation == 'visibility':
            # Restore the exact old cached root/header seam, retaining the new
            # receiver API so this control isolates prepared-first visibility.
            motion = files['TownServiceMirror.Motion.cs']
            start = motion.index('            TownServiceFrame rootFrame = frame;')
            stop = motion.index('            TownServiceMotionEntry root = MotionHeader', start)
            motion = motion[:start] + motion[stop:]
            end = motion.index('            VRHand? hand = MotionHand(', start)
            motion = motion[:start] + motion[start:end].replace('rootFrame.', 'frame.') + motion[end:]
            files['TownServiceMirror.Motion.cs'] = motion
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
            'expected': ('each moving original retains its printed front on the same first render' if mutation == 'parts' else 'each complete native front/body stays coherent between independent return receipts' if mutation == 'native-parts' else 'repair metadata before113 retains hidden prepared originals' if mutation == 'native-header' else 'clamped native age never rewinds at an exact receipt' if mutation == 'clock' else 'all continuously contending live/fan/ordinary originals progress' if mutation == 'priority' else 'prepared original opens on the first exact native flight') if mutation else ''})
        receipts[name] = {file: hashlib.sha256(content.encode()).hexdigest() for file, content in files.items()}
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    # Program's shared boundary setup imports the original purse even in a card-only suite.
    subprocess.run([os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python')),
        str(root / 'scripts/town-purse-runtime/export-native.py'), str(root), str(run / 'native-purse.json')], check=True)
    native = project/'Assets/NativeFlight658'
    subprocess.run([os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')),
        str(Path(__file__).with_name('export-native.py')),str(root),str(native)],check=True)
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
    (run / 'source-hashes.json').write_text(json.dumps({'production': hashes, 'cases': receipts,
        'harness': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),Path(__file__).with_name('export-native.py')]},
        'fixture': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.glob('*.cs')}}, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=240,
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
    if result.returncode or not (run / 'results.txt').exists(): raise SystemExit('FAIL native flight658: ' + str(run))
    print('PASS native flight658: ' + str(run))


if __name__ == '__main__': main()
