#!/usr/bin/env python3
"""Compile unchanged wall-performance policy and real Unity native presentation primitives.

The collector, original controllers and broad actor/floor classifiers are explicit model
boundaries. Source guards bind their integration calls; this is not the full original game.
"""
import argparse,hashlib,json,os,shutil,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def method(text,signature):
    start=text.index(signature);body=text.index('{',start);end=body+1;depth=1
    while depth:depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start:end]
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/wall-performance-runtime')
    parser.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--baseline-ref',default='2f6819eee')
    parser.add_argument('--production-only',action='store_true')
    parser.add_argument('--compile-only',action='store_true')
    parser.add_argument('--case',action='append')
    args=parser.parse_args();root=args.source_root.resolve();fixture=ROOT/'tests/wall-performance-runtime'
    directory=root/'src/GloomhavenVR/Core/WallFade'
    names=['WallSegmentFade.Performance.cs','WallSegmentFade.ArchMounted.cs','WallSegmentFade.cs','WallSegmentFade.Net.cs','WallSegmentFade.DrawTrace.cs','WallSegmentFade.Water.cs']
    paths=[directory/name for name in names];read={p.name:p.read_text() for p in paths}
    scenery=root/'src/GloomhavenVR/Core/Perf/ScenarioSceneryBudget.cs';paths.append(scenery)
    scenery_method=method(scenery.read_text(),'private static void SetHidden(Record record, bool hide)')
    methods=[]
    for filename,signatures in [('WallSegmentFade.cs',['private void LateUpdate()','private void ClearAllBlocks(string reason)']),
        ('WallSegmentFade.Net.cs',['internal int SampleFadedKeys(uint[] dest)','private void ComputeWireKeys()','private static uint Fnv1a32Key(string s)']),
        ('WallSegmentFade.DrawTrace.cs',['private void NoteWallDrawWrite(Segment seg, MeshRenderer renderer, MaterialPropertyBlock? block)']),
        ('WallSegmentFade.Water.cs',['private bool IsWaterProtected(Bounds b)'])]:
        for signature in signatures:
            extracted=method(read[filename],signature)
            if signature=='private void ComputeWireKeys()':extracted=extracted.replace('{','{ FixtureWireRebuilds++;',1)
            methods.append(extracted)
    source={'WallSegmentFade.Performance.cs':read[names[0]],'WallSegmentFade.ArchMounted.cs':read[names[1]],
        'SceneryPrimitive.cs':(fixture/'SceneryFixture.cs').read_text().replace('// @PRODUCTION_SET_HIDDEN@',scenery_method),
        'ProductionPrimitives.cs':'using System; using UnityEngine; namespace GloomhavenVR.Core {internal static partial class WallSegmentFade {private sealed partial class FadeDriver {\n'+'\n'.join(methods)+'\n}}}'}
    baseline_sha=subprocess.check_output(['git','-C',str(ROOT),'rev-parse',args.baseline_ref],text=True).strip()
    old_wall=subprocess.check_output(['git','-C',str(ROOT),'show',baseline_sha+':src/GloomhavenVR/Core/WallFade/WallSegmentFade.cs'],text=True)
    old_entry=method(old_wall,'private void LateUpdate()')
    baseline=dict(source);baseline['ProductionPrimitives.cs']=baseline['ProductionPrimitives.cs'].replace(method(read['WallSegmentFade.cs'],'private void LateUpdate()'),old_entry)
    assert baseline!=source,'entry baseline drift'
    # These are deliberate causal controls, never silently transformed production inputs.
    changes=[
        ('entry-not-paused','ProductionPrimitives.cs','if (TickPerformanceVisibility())','if (TickPerformanceVisibility() && bool.Parse("false"))','settled hidden entry performs no old Tick collector or renderer writes'),
        ('mask-write-lost','WallSegmentFade.Performance.cs','renderer.forceRenderingOff = true;','renderer.forceRenderingOff = false;','hide all masks wall and original attached shelf/column'),
        ('shared-protection-lost','WallSegmentFade.Performance.cs','_performanceProtected.Contains(renderer) || _performanceMasks.ContainsKey(renderer)','false || _performanceMasks.ContainsKey(renderer)','floor actor gate and shared protected attachment remain visible'),
        ('floor-guard-lost','WallSegmentFade.Performance.cs','|| FloorNeverFades(renderer) || HeldNeverFades(renderer)','|| false || HeldNeverFades(renderer)','floor actor gate and shared protected attachment remain visible'),
        ('clock-cap-lost','WallSegmentFade.Performance.cs','Math.Min(delta, .25f)','delta','one long paused frame cannot trigger Auto'),
        ('network-scan-not-paused','ProductionPrimitives.cs','if (PerformanceWallsHidden) return 0;','if (PerformanceWallsHidden && bool.Parse("false")) return 0;','hidden network sample emits no faded wall keys'),
        ('diagnostics-not-paused','ProductionPrimitives.cs','if (PerformanceWallsHidden || !VRLog.Wants','if (false || !VRLog.Wants','hidden sender and diagnostic leave existing buffers untouched'),
        ('cosmetic-prewarm-veto-regression','WallSegmentFade.Performance.cs','&& !ScenarioRoomLoading.HasPendingReveal','&& !ScenarioInteractionPreparation.IsPreparing && !ScenarioRoomLoading.HasPendingReveal','cosmetic prewarm does not veto Auto in native settled gameplay'),
        ('options-veto-regression','WallSegmentFade.Performance.cs','loaded && VRSession.InputFocus != false','loaded && VRSession.InputFocus != false && !GloomhavenVR.WorldUI.VROptionsTab.IsOpen','open VR Options do not veto sustained low-FPS Auto'),
        ('desktop-focus-veto-regression','WallSegmentFade.Performance.cs','loaded && VRSession.InputFocus != false','loaded && VRSession.InputFocus != false && Application.isFocused','desktop window focus is irrelevant to headset Auto'),
        ('unknown-xr-focus-veto-regression','WallSegmentFade.Performance.cs','VRSession.InputFocus != false','VRSession.InputFocus == true','unknown XR focus does not permanently veto loaded Auto'),
        ('xr-focus-loss-ignored','WallSegmentFade.Performance.cs','VRSession.InputFocus != false','true','native loading reveal or explicit XR focus loss does not trigger Auto'),
        ('native-loading-ignored','WallSegmentFade.Performance.cs','!controller.IsLoading && !controller.ScenarioIsLoading','true','native loading reveal or explicit XR focus loss does not trigger Auto'),
        ('native-reveal-ignored','WallSegmentFade.Performance.cs','!ScenarioRoomLoading.HasPendingReveal','true','native loading reveal or explicit XR focus loss does not trigger Auto'),
        ('auto-recovery-grace-lost','WallSegmentFade.Performance.cs','eligible && now - _performanceReadySince >= .5f','eligible','native loading reveal or XR focus recovery starts a fresh grace and full window'),
        ('cosmetic-hidden-inventory-veto-regression','WallSegmentFade.Performance.cs','TickPerformanceInventory(gen, now, loaded);','TickPerformanceInventory(gen, now, loaded && !ScenarioInteractionPreparation.IsPreparing);','same-count native generation collects fresh new wall membership'),
        ('collector-remask-lost','WallSegmentFade.Performance.cs','SetPerformanceCollectionMasks(true);','SetPerformanceCollectionMasks(false);','same-count native generation collects fresh new wall membership'),
        ('valid-negative-scene-recovery-lost','WallSegmentFade.Performance.cs','if (!VRSession.IsRunning\n                || Rig.VRRigDriver.HeadCamera == null','if (!VRSession.IsRunning || _performanceScene < 0\n                || Rig.VRRigDriver.HeadCamera == null','Hidden to Regular rebuilds actual native wire keys once before sender resumes'),
        ('wire-recovery-omitted','WallSegmentFade.Performance.cs','if (recoverWireKeys) ComputeWireKeys();','/* injected: no key recovery */','Hidden to Regular rebuilds actual native wire keys once before sender resumes'),
        ('wire-recovery-unconditional','WallSegmentFade.Performance.cs','if (recoverWireKeys) ComputeWireKeys();','if (recoverWireKeys || true) ComputeWireKeys();','Regular to Hide all performs no wire recovery'),
        ('scenery-release-clears-wall','SceneryPrimitive.cs','&& _retainPerformanceWallMask?.Invoke(renderer) != true','&& true','actual scenery release retains wall-owned flag and relinquishes its own claim'),
        ('foreign-release-prior-stale','WallSegmentFade.Performance.cs','_performanceMasks[renderer] = false;','_performanceMasks[renderer] = true;','Regular preserves current native MPB and relinquished foreign flag'),
    ]
    variants=[('production',source,'')]
    if not args.production_only:
        for name,filename,before,after,expected in changes:
            assert before in source[filename],'control binding drift: '+name
            copied=dict(source);copied[filename]=copied[filename].replace(before,after)
            variants.append((name,copied,expected))
    if args.case:variants=[v for v in variants if v[0]=='production' or v[0] in args.case]
    variants.append(('baseline-entry',baseline,''))
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    inputs=paths+[Path(__file__).resolve()]+sorted(p for p in fixture.rglob('*') if p.is_file())
    hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    (run/'source-hashes.json').write_text(json.dumps({'sha256':hashes,'sourceRoot':str(root),'baselineEntryCommit':baseline_sha,'baselineWallSha256':hashlib.sha256(old_wall.encode()).hexdigest(),'limits':[
        'Complete Performance partial and actual LateUpdate/ClearAllBlocks/SampleFadedKeys/draw-write guard/Water protection/arch-mounted hierarchy execute.',
        'Native controller, scene/config/loading/focus/clock, held registry, staged collector, attachment restorers and broad floor/actor/mod classifiers are explicit model boundaries.',
        'Native Unity renderer/mesh/MPB/scene/bounds/hierarchy/camera pixels execute. Foreign native command draws are measured as an explicit boundary.',
        'Timing measures added entry overhead against the identical entry with guard removed and the same cheap Tick boundary; not saved whole-wall CPU or Frame FPS.'
    ],'transformations':['using aliases for Unity Time and Application to deterministic named clock/focus boundaries','Invocation count inserted only at actual ComputeWireKeys entry; no key algorithm replacement; no calls inside warmed timing region'],'variants':[v[0] for v in variants]},indent=2)+'\n')
    manifest={'result':str(run/'results.txt'),'cases':[]};dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    harmony=Path.home()/'.nuget/packages/harmonyx/2.7.0/lib/net45/0Harmony.dll'
    for name,texts,expected in variants:
        case=run/name;production=case/'production';production.mkdir(parents=True)
        for filename,text in texts.items():
            text='using Time = GloomhavenVR.Core.WallFixtureClock; using Application = GloomhavenVR.Core.WallFixtureFocus;\n'+text
            (production/filename).write_text(text)
        project=case/'WallPerformance.csproj';shutil.copyfile(fixture/'WallPerformance.csproj',project)
        assembly='WallPerformance_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,
            '-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed'),'-p:HarmonyPath='+str(harmony)],capture_output=True,text=True)
        (case/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr+'\nCompilation failure cannot pass a control: '+str(run))
        manifest['cases'].append({'name':name,'dll':str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    (run/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    if args.compile_only:print('COMPILE PASS '+str(run));return
    unity=run/'unity';(unity/'Assets/Editor').mkdir(parents=True);(unity/'Packages').mkdir();(unity/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/WallPerformanceRunner.cs',unity/'Assets/Editor/WallPerformanceRunner.cs');shutil.copyfile(fixture/'Native.shader',unity/'Assets/Native.shader')
    (unity/'Packages/manifest.json').write_text('{"dependencies":{}}\n');(unity/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(unity),'-executeMethod','WallPerformanceRunner.Start','-wallManifest',str(run/'manifest.json'),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
    try:result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=300)
    finally:
        for cache in ('Library','Temp'):shutil.rmtree(unity/cache,ignore_errors=True)
    changed={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs if hashlib.sha256(p.read_bytes()).hexdigest()!=hashes[str(p)]}
    (run/'source-stability.json').write_text(json.dumps({'unchanged':not changed,'changed':changed},indent=2)+'\n')
    (run/'unity-exit-code.txt').write_text(str(result.returncode)+'\n')
    if (run/'results.txt').is_file():print((run/'results.txt').read_text(),end='')
    if changed or result.returncode or not (run/'results.txt').is_file():raise SystemExit('Runtime failure '+str(run))
    print('PASS '+str(run))
if __name__=='__main__':main()
