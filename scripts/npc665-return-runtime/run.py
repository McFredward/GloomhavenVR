#!/usr/bin/env python3
"""Same-object enchantress offered-to-native-return regression proof.

Binds the real inactive/active VRCard sampler, native revision bump and capped
flight update to actual Stock publication, capture, codec, templates, binding and
observer renderer. The native 86-node ability prefab and exact procedural body
are used; native model initialization/gameplay callbacks remain explicit ports.
Legacy --source-root is supported for the preserved unmodified Build664 RED.
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc665-return')
    parser.add_argument('--case', choices=['native'])
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--only-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('return658_sources', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    for name in ['TownServiceCodec.ReturnOrigin.cs','TownServiceMirror.ReturnOrigins.cs']:
        path=root/'src/GloomhavenVR/Net/TownServices'/name
        if path.exists():
            # The common binder may already port the actual protocol constants.
            # Preserve those declared fixture ports while retaining raw hashes.
            bound[name]=bound.get(name,path.read_text()).replace('Time.unscaledTime','global::FlightTime655.Now')
            hashes[name]=hashlib.sha256(path.read_bytes()).hexdigest()

    legacy='TownServiceMirror.ReturnOrigins.cs' not in bound
    held='ReturnOriginals.Values.SelectMany(v=>v.Values).Select(v=>v.Module)' if not legacy else 'System.Array.Empty<RemoteModule>()'
    probes = """using System.Linq; using System.Collections.Generic; using UnityEngine;
namespace GloomhavenVR.Net.TownServices; internal static partial class TownServiceMirror {
internal static object? Host665(int peer,ushort id)=>Remote.TryGetValue(peer,out var modules)&&modules.TryGetValue(id,out var m)?m.Host:null;
internal static int VisibleParts665(string address)=>Remote.Values.SelectMany(m=>m.Values).Concat(HELD).Distinct().Count(m=>m.Alive&&m.Address==address&&m.Host.activeInHierarchy);
""".replace('HELD',held)
    if legacy:
        probes += """internal static object? HeldHost665(int peer,ushort id)=>null;
internal static byte[] WithoutStockOrigin665(byte[] bytes)=>bytes;
internal static bool StockOriginPresent665(byte[] bytes)=>false;
internal static uint OriginRevision665(int peer,ushort id)=>0;
internal static float OriginFloor665(int peer,ushort id)=>0;
internal static int OriginCount665(int peer)=>0;
internal static bool? OfferedEpochGuard665(int peer,ushort id)=>null;
internal static bool TerminalGuards665(int peer)=>false;
"""
    else:
        probes += """internal static object? HeldHost665(int peer,ushort id)=>ReturnOriginals.TryGetValue(peer,out var originals)?originals.Values.FirstOrDefault(o=>o.Origin.Module==id)?.Module.Host:null;
internal static byte[] WithoutStockOrigin665(byte[] bytes){TownServiceCodec.TryRead(bytes,bytes.Length,out var frame);if(frame!.VisitorStock&&frame.Module!=TownServiceFrame.ManifestModule)frame.ReturnOrigin=null;return TownServiceCodec.Write(frame);}
internal static bool StockOriginPresent665(byte[] bytes){TownServiceCodec.TryRead(bytes,bytes.Length,out var frame);return frame!.ReturnOrigin!=null;}
internal static uint OriginRevision665(int peer,ushort id)=>Remote[peer][id].LastFrame!.ReturnOrigin!.PreparationRevision;
internal static float OriginFloor665(int peer,ushort id)=>Remote[peer][id].ReturnOriginChangedAt;
internal static int OriginCount665(int peer)=>ReturnOriginals.TryGetValue(peer,out var originals)?originals.Count:0;
internal static bool? OfferedEpochGuard665(int peer,ushort id) {
    var guard=typeof(TownServiceMirror).GetMethod("ContinuousOfferedRoot",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
    if(guard==null)return null; // The isolated return worker predates the parallel yaw helper.
    var module=Remote[peer][id];
    var current=MotionPeers[peer].Slots.Values.SingleOrDefault(s=>s.Entry.Kind==1&&s.Entry.Lane==0&&s.Entry.Module==id);
    if(current==null)throw new System.InvalidOperationException("Actual current offered root absent: "+string.Join(";",MotionPeers[peer].Slots.Values.Select(s=>s.Entry.Kind+"/"+s.Entry.Lane+"/"+s.Entry.Module)));
    var stale=new MotionSlot { Entry=current.Entry,ReceivedSequence=current.ReceivedSequence,
        ReceivedAt=current.ReceivedAt,SampleTime=module.ReturnOriginChangedAt-1f/90f };
    bool Allows(MotionSlot slot)=>(bool)guard.Invoke(null,new object[]{peer,module,slot,FlightTime655.Now})!;
    return !Allows(stale)&&Allows(current);
}
internal static bool TerminalGuards665(int peer) {
    var current=MotionPeers[peer].ReturnCohorts.Values.First(a=>!a.Activated&&CardReturnClock.IsTerminal(a.Header)&&CompleteReturnPayload(a));
    int index=System.Array.FindIndex(current.Roots,r=>r!.HasCanvasFrame); if(index<0)index=0;
    var source=current.Roots[index]!;
    TownServiceMotionEntry Clone(TownServiceMotionEntry e)=>(TownServiceMotionEntry)typeof(object).GetMethod("MemberwiseClone",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(e,null)!;
    if(!KeepPendingOriginTerminal(peer,current,source,index))return false;
    var changed=Clone(source);changed.Hand=1;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    changed=Clone(source);changed.Visible=!source.Visible;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    changed=Clone(source);changed.ParentAlpha=.5f;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    changed=Clone(source);changed.ParentModule++;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    changed=Clone(source);changed.Binding++;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    changed=Clone(source);changed.Pose=(float[])source.Pose.Clone();changed.Pose[0]+=.01f;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    changed=Clone(source);changed.HasCanvasUpdate=true;changed.CanvasSortingOrder++;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    changed=Clone(source);changed.HasCanvasUpdate=true;changed.HasCanvasFrame=!source.HasCanvasFrame;if(KeepPendingOriginTerminal(peer,current,changed,index))return false;
    var stale=new ReturnCohortAssembly {Header=Clone(current.Header),Clock=current.Clock,Parts=current.Parts,Roots=current.Roots,Sequences=current.Sequences};
    stale.Header.Revision++;if(KeepPendingOriginTerminal(peer,stale,source,index))return false;
    stale.Header=Clone(current.Header);stale.Header.Session++;if(KeepPendingOriginTerminal(peer,stale,source,index))return false;
    stale.Header=Clone(current.Header);stale.Header.Lane=0;if(KeepPendingOriginTerminal(peer,stale,source,index))return false;
    stale.Header=Clone(current.Header);stale.Activated=true;if(KeepPendingOriginTerminal(peer,stale,source,index))return false;
    return true;
}

"""
    bound['ReturnIdentity665.cs']=probes+'}'
    cohort = root / 'src/GloomhavenVR/Net/TownServices/TownServiceMirror.CardReturnCohorts.cs'
    bound[cohort.name] = cohort.read_text().replace('Time.unscaledTime','global::FlightTime655.Now')
    hashes[cohort.name] = hashlib.sha256(cohort.read_bytes()).hexdigest()
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    (fixture/'FixtureTime665.cs').write_text('internal static class FlightTime655 { internal static float Now=100f,Delta=1f/90f; }')
    shutil.copyfile(Path(__file__).with_name('Return665.cs'), fixture / 'Flight658.cs')
    reader = (root/'scripts/town-first-picture632-runtime/FirstPicture632.cs').read_text().split('    private static IEnumerator FirstPicture632()')[0]+'}\n'
    reader = reader.replace('NativeRow632(Transform parent,string localized)', 'NativeRow632(Transform parent,string localized,string panel="ability-card")').replace('"NativeFirstPicture632"','"NativeFlight658",panel')
    reader = reader.replace('new Material(TMP_Settings.defaultFontAsset.material)','new Material(Shader.Find("TextMeshPro/Distance Field"))').replace('GameMaterials632.Add(material.key,restored);','restored.SetTexture("_MainTex",TMP_Settings.defaultFontAsset.atlasTextures[0]); GameMaterials632.Add(material.key,restored);')
    (fixture/'NativeReader658.cs').write_text(reader)
    for relative in ['Cards/Art/CardMesh.cs','Cards/Art/CardContour.cs','Cards/Caps/CapFaceLayout.cs','WorldUI/TownServices/TownServiceCardBody.cs']:
        path = root/'src/GloomhavenVR'/relative
        bound[path.name] = path.read_text(); hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    lazy = fixture/'LazyTemplate.cs'
    lazy.write_text(lazy.read_text().replace('    internal static class TownServiceCardBody\n    { internal static void RebindClone(string key,GameObject clone) { } }\n',''))

    card_source = (root / 'src/GloomhavenVR/Cards/VRCard.cs').read_text()
    sampler = loader.method(card_source, 'internal bool TryTownReturnMotion(')
    step = card_source[card_source.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'):
                       card_source.index('            if (ft >= 1f)', card_source.index('            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);'))]
    bound['NativeReturnSource655.cs'] = '''using System;
using UnityEngine;
using GloomhavenVR.Hands;
namespace GloomhavenVR.Cards;
internal sealed partial class VRCard {
    private bool _flying, _flyIntro;
    private float _flyElapsed, _flyDuration, _flyArcHeight;
    private uint _townReturnRevision;
    private Vector3 _flyFromPos, _flyToPos, _flyFromScale, _flyToScale, _flyArcUp;
    private Quaternion _flyRot;
    internal void BeginNative655(Vector3 target, float seconds) {
        _flying=true; _flyIntro=true; _flyElapsed=0; _flyDuration=seconds; if (++_townReturnRevision==0) _townReturnRevision=1;
        _flyFromPos=transform.position; _flyToPos=target; _flyRot=transform.rotation;
        _flyFromScale=transform.localScale; _flyToScale=transform.localScale*.7f;
        _flyArcUp=Vector3.up; _flyArcHeight=.08f;
    }
    internal void Cancel660(VRHand hand) { Holder=hand; _flying=false; }
    internal void Prepare665() { Holder=null; _flying=false; _flyIntro=false; }
''' + sampler.replace('TryTownReturnMotion(', 'CaptureNativeTownReturn655(', 1) + '\n    internal void StepNative655() {\n' + step.replace('Time.unscaledDeltaTime', 'global::FlightTime655.Delta') + '\n        if (ft >= 1f) _flying=false; // Exact native terminal flag; gameplay callbacks are the boundary.\n    }\n}\n'
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
    text = text.replace('var flight = Return655(variant.Contains("merchant"), variant.Contains("split") || variant == "per-partition-clock");', 'var flight = NativeFlight658(variant);')
    (fixture / 'Program.cs').write_text(text)
    transfer=(fixture/'Transfer.cs').read_text()
    start=transfer.index('        internal bool TryTownReturnMotion(Transform source,Transform shared,Hands.VRHand? hand,out uint revision,out float[] values)')
    end=transfer.index('\n    }',start)
    transfer=transfer[:start]+'''        internal bool TryTownReturnMotion(Transform source,Transform shared,Hands.VRHand? hand,out uint revision,out float[] values)
        => CaptureNativeTownReturn655(source,shared,hand,out revision,out values);'''+transfer[end:]
    (fixture/'Transfer.cs').write_text(transfer)
    legacy = 'TownServiceMirror.ReturnOrigins.cs' not in bound
    controls=[('old-migration','migration'),('old-retirement','retirement'),
        ('old-private-suppression','suppression'),('old-terminal-preservation','terminal'),
        ('old-origin-lifetime','lifetime'),('old-withdraw-floor','floor'),('old-epoch-budget','epoch'),('old-superseded-epoch','superseded')]
    if 'TownServiceMirror.OfferedRoot.cs' in bound:
        controls.append(('old-yaw-origin-floor','yaw-floor'))
    cases=[] if args.only_negative_controls else [('baseline' if legacy else 'native',None)]
    if not legacy and not args.no_negative_controls and args.case is None: cases.extend(controls)
    expected={None:'same visible offered binding starts its independent stock native return' if legacy else '',
        'migration':'same visible offered binding starts its independent stock native return',
        'retirement':'same preannounced original survives source closure before stock receipt',
        'suppression':'consumed private pending original never rebuilds after stock adoption',
        'terminal':'exact pending native terminal rejects changed hand visibility pose canvas revision session and prior activation',
        'lifetime':'same visible offered binding starts its independent stock native return',
        'floor':'late consumed origin cannot rebuild between reoffer census and its new preparation header',
        'epoch':'reoffer without unregister evicts obsolete origins without disposing current originals',
        'superseded':'older exact native113 cannot expose a stock copy while a newer private preparation is already rendered',
        'yaw-floor':'new offered preparation rejects old yaw samples and accepts its actual current root'}
    def no_op_method(files, filename, signature, result):
        old=loader.method(files[filename],signature)
        files[filename]=files[filename].replace(old,old[:old.index('{')]+'{ return '+result+'; }',1)
    def mutate(files, mutation):
        name='TownServiceMirror.ReturnOrigins.cs'
        if mutation=='migration': no_op_method(files,name,'private static bool AdoptReturnOriginals(','true')
        elif mutation=='retirement': no_op_method(files,name,'private static bool RetainReturnOriginal(','false')
        elif mutation=='suppression': no_op_method(files,name,'private static bool SuppressTransferredOriginal(','false')
        elif mutation=='terminal': no_op_method(files,'TownServiceMirror.CardReturnCohorts.cs','private static bool KeepPendingOriginTerminal(','false')
        elif mutation=='lifetime':
            old='module.ReturnOrigin is TownCardReturnOrigin prior'
            assert files[name].count(old)==1;files[name]=files[name].replace(old,'module.Last?.ReturnOrigin is TownCardReturnOrigin prior')
        elif mutation=='floor':
            old='if (ReturnOriginFloors.TryGetValue(peer, out var floors)'
            assert files[name].count(old)==1;files[name]=files[name].replace(old,'if (false && ReturnOriginFloors.TryGetValue(peer, out var floors)',1)
        elif mutation=='superseded':
            old='if (!current.Transferred && current.Module.Alive && current.Module.LastFrame != null'
            assert files[name].count(old)==1;files[name]=files[name].replace(old,'if (false && !current.Transferred && current.Module.Alive && current.Module.LastFrame != null',1)
        elif mutation=='yaw-floor':
            key='TownServiceMirror.OfferedRoot.cs'
            old=' || sample.SampleTime < module.ReturnOriginChangedAt'
            assert files[key].count(old)==1;files[key]=files[key].replace(old,'',1)
        elif mutation=='epoch':
            files[name]=files[name].replace('foreach (TownCardReturnOrigin obsolete in DeadReturnOrigins) originals.Remove(obsolete);','DeadReturnOrigins.Clear();',1)
            files[name]=files[name].replace('bool obsolete = !original.Transferred && !original.Retired','bool obsolete = false && !original.Transferred && !original.Retired',1)

    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    manifest = {'suite': 'return655', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    receipts = {}
    for name, mutation in cases:
        files = dict(bound)
        mutate(files,mutation)
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
            'expected': expected[mutation]})
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
        str(root/'scripts/npc658-flight-runtime/export-native.py'),str(root),str(native)],check=True)
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
    (run / 'source-hashes.json').write_text(json.dumps({'production': hashes, 'cases': receipts,
        'harness': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),Path(__file__).with_name('Return665.cs'),root/'scripts/npc658-flight-runtime/export-native.py']},
        'fixture': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.glob('*.cs')}}, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=240,
        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
    if result.returncode or not (run / 'results.txt').exists(): raise SystemExit('FAIL native return665: ' + str(run))
    print('PASS native return665: ' + str(run))


if __name__ == '__main__': main()
