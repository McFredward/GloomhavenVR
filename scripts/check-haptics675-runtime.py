#!/usr/bin/env python3
"""Run unchanged semantic hover code against original native UI components in Unity.

Clock and controller delivery are boundaries. The source-bound fan, sender, raw UI
handoff, geometric-half haptic block and board toggle path execute; native visual
hover callbacks/gameplay, a physical controller and HMD are outside this receipt.
"""
import argparse
import atexit
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
from town_run_retention import cleanup_completed_run

ROOT=Path(__file__).resolve().parents[1]

def method(source,signature):
    assert source.count(signature)==1,'Source binding drift: '+signature
    start=source.index(signature);end=source.index('{',start)+1;depth=1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]

def bind(root):
    relative=['Hands/HoverHaptics.cs','Hands/VRHand.cs','Hands/Interact/IPokeable.cs',
        'Hands/Interact/RayUguiDriver.cs','Hands/Interact/PokeInteractor.cs',
        'Hands/Interact/ProximityGrabber.cs','Hands/Interact/RayGrabDriver.cs','Cards/CardFan.cs',
        'Cards/VRCard.cs','Cards/HalfSelection.cs','Cards/Driver/CardsDriver.3.Laser.cs',
        'Cards/Piles/ItemsPile.cs','Board/TargetingUx.cs','Hands/VRHaptics.cs']
    inputs={name:(root/'src/GloomhavenVR'/name).read_text() for name in relative}
    hand=inputs['Hands/VRHand.cs'];ray=inputs['Hands/Interact/RayUguiDriver.cs'];half=inputs['Cards/HalfSelection.cs']
    refresh='''        if (Highlighted != null)
            _hand.SendHoverHaptic(Highlighted);'''
    assert inputs['Hands/Interact/ProximityGrabber.cs'].count(refresh)==1
    ui='''        _pointer.SetHovered(hit ? top.gameObject : null);
        _hand.SendHoverHaptic(HoverHapticTargets.Ui(_pointer.Hovered));'''
    assert ray.count(ui)==1
    haptic=half[half.index('        object? uiRegion = HoverHapticTargets.Ui(dom?.RayUgui.Hovered);'):half.index('        bool unchanged = ReferenceEquals(hitCard, _laserCard)')]
    # Prevent an untested remaining card driver from restoring the old duplicate edge.
    for name,source in inputs.items():
        if name in ['Hands/Interact/RayUguiDriver.cs','Hands/Interact/ProximityGrabber.cs','Cards/CardFan.cs','Cards/VRCard.cs','Cards/HalfSelection.cs']:
            assert 'SendHaptic(HapticPreset.HoverTick)' not in source,'Raw card hover pulse remains: '+name
    assert inputs['Hands/Interact/PokeInteractor.cs'].count('_hand.SendHoverHaptic(HoverHapticTargets.Pokeable(_hovered));')==1
    assert inputs['Cards/Piles/ItemsPile.cs'].count('SendHoverHaptic(_handWinner')==2
    assert inputs['Cards/Driver/CardsDriver.3.Laser.cs'].count('dom.SendHoverHaptic(')==6
    arm="        _hand.SendHoverHaptic(HoverHapticTargets.Ui(_pointer.Hovered)); // same region, no second hover tick"
    assert inputs['Hands/Interact/PokeInteractor.cs'].count(arm)==1
    board=inputs['Board/TargetingUx.cs']
    bound={'HoverHaptics.cs':inputs['Hands/HoverHaptics.cs'],'IPokeable.cs':inputs['Hands/Interact/IPokeable.cs'],
        'VRHand.cs':'namespace GloomhavenVR.Hands { internal sealed partial class VRHand {\n'+method(hand,'internal void SendHoverHaptic(')+'\n'+method(hand,'public void SendHaptic(')+'\ninternal void RefreshGrabber() { var _hand=this; object? Highlighted=Grabber.Highlighted;\n'+refresh+'\n}\n}}',
        'Fan.cs':'using UnityEngine; using GloomhavenVR.Hands;\nnamespace GloomhavenVR.Cards { internal sealed partial class CardFan {\n'+method(inputs['Cards/CardFan.cs'],'private void UpdateFingertipHover(')+'\n} internal sealed partial class VRCard {\n'+method(inputs['Cards/VRCard.cs'],'public void OnPokeEnter(')+'\n}}',
        'Drivers.cs':'''using GloomhavenVR.Hands; using UnityEngine;
internal static class DriverFrames {
    private sealed class Pointer { internal GameObject? Hovered; internal void SetHovered(GameObject? target) => Hovered=target; }
    internal static void Ray(VRHand _hand, GameObject? target) {
        var _pointer=new Pointer(); bool hit=target!=null; var top=new UnityEngine.EventSystems.RaycastResult { gameObject=target };
'''+ui+'''
        _hand.RayUgui.Hovered=_pointer.Hovered;
    }
    internal static void Arm(VRHand _hand, GameObject target) { var _pointer=new Pointer(); _pointer.SetHovered(target);
'''+arm+'''
    }
    internal static void Grabber(VRHand hand) => hand.RefreshGrabber();
    internal static void Half(VRHand? dom,FullAbilityCard? hitFull,bool hitTop) { object? hitCard=hitFull;
'''+haptic+'''
    }
    internal static int BoardCases(VRHand hand) {
        int before=hand.Pulses.Count; var go=new GameObject("board target");
        try { var target=go.AddComponent<CInteractable>(); GloomhavenVR.Board.BoardPick.HitCollider=go.AddComponent<BoxCollider>();GloomhavenVR.Board.BoardPick.HasHit=true;GloomhavenVR.Board.BoardPick.SourceHand=hand;
            GloomhavenVR.Board.BoardConfig.HoverHaptics.Value=true;
            VRHand.Now=9;GloomhavenVR.Board.BoardPick.Source=GloomhavenVR.Board.BoardPick.PickSource.Far;GloomhavenVR.Board.TargetingUx.Tick();
            if(hand.Pulses.Count!=before)throw new System.InvalidOperationException("passive far board tooltip hover is silent");
            VRHand.Now=9.1f;GloomhavenVR.Board.BoardPick.Source=GloomhavenVR.Board.BoardPick.PickSource.Near;GloomhavenVR.Board.TargetingUx.Tick();
            if(hand.Pulses.Count!=before+1)throw new System.InvalidOperationException("physical near board hover keeps its existing feedback");
            GloomhavenVR.Board.TargetingUx.Tick(); if(hand.Pulses.Count!=before+1)throw new System.InvalidOperationException("same near board target remains one edge");
            GloomhavenVR.Board.BoardConfig.HoverHaptics.Value=false;GloomhavenVR.Board.TargetingUx.Tick();
            VRHand.Now=9.3f;GloomhavenVR.Board.TargetingUx.Tick();if(hand.Pulses.Count!=before+1)throw new System.InvalidOperationException("saved board hover toggle still disables physical feedback");
            return 4;
        } finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
''',
        'Board.cs':'using GloomhavenVR.Hands;\nnamespace GloomhavenVR.Board { internal static class TargetingUx { private static CInteractable? _lastHover; private static void SuppressHoverActorInfo() {}\n'+method(board,'public static void Tick(')+'\n'+method(board,'private static bool IsValidTarget(')+'''\n}
internal static class BoardConfig { internal sealed class Toggle { internal bool Value; } internal static Toggle HoverHaptics=new(); }
internal static class BoardPick { internal enum PickSource { None, Far, Near } internal static PickSource Source; internal static bool HasHit; internal static UnityEngine.Collider? HitCollider; internal static VRHand? SourceHand; }
}'''}
    return bound,{name:hashlib.sha256(text.encode()).hexdigest() for name,text in inputs.items()}

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source-root',type=Path,default=ROOT);parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/haptics675-runtime');args=parser.parse_args();root=args.source_root.resolve()
    bound,hashes=bind(root);args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    print('evidence: '+str(run),flush=True)
    completion=run/'completion.log';completion.write_text('evidence: '+str(run)+'\n');outcome={'exit_code':1,'reaped':True}
    def cleanup():
        receipt=cleanup_completed_run('haptics675-runtime',outcome['exit_code'],completion,root,process_reaped=outcome['reaped']);(run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
    atexit.register(cleanup)
    unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'));game=root/'ressources/GH_Data/Managed';fixture=root/'scripts/haptics675-runtime'
    retained=run/'Sources';retained.mkdir();shutil.copytree(fixture,retained/'fixture');shutil.copyfile(Path(__file__),retained/'check-haptics675-runtime.py')
    fixture_hashes={str(file.relative_to(retained)):hashlib.sha256(file.read_bytes()).hexdigest() for file in retained.rglob('*') if file.is_file()}
    fixture=retained/'fixture'
    native_hashes={name:hashlib.sha256((game/name).read_bytes()).hexdigest() for name in ['GH.Runtime.dll','GH.Runtime.FirstPass.dll','UnityEngine.UI.dll']}
    pingpong=bound['HoverHaptics.cs'].replace('    internal bool Enter(', '    private object? _lastRegion;\n    internal bool Enter(',1).replace('        int oldest = 0;', '        bool regionChanged = !ReferenceEquals(_lastRegion, target);\n        _lastRegion = target;\n        int oldest = 0;',1).replace('bool entered = now - entry.SeenAt > ExitGraceSeconds || now < entry.SeenAt;', 'bool entered = regionChanged || now - entry.SeenAt > ExitGraceSeconds || now < entry.SeenAt;',1)
    variants=[('production',None,None,None,''),
        ('per-driver-duplicates','HoverHaptics.cs','bool entered = now - entry.SeenAt > ExitGraceSeconds || now < entry.SeenAt;','bool entered = true;','simultaneous distinct action producers tick each region once without ping-pong'),
        ('raw-tooltip-targets','HoverHaptics.cs','return action;','return target;','native icon children resolve one original top action'),
        ('fingertip-refresh-only-at-edge','Fan.cs','        if (target != null && target.CanGrab && target.AllowsHand(dom!))','        if (!ReferenceEquals(target, _pokeHoverCard) && target != null && target.CanGrab && target.AllowsHand(dom!))','one physical pulse per card despite staggered fingertip'),
        ('default-to-half-hidden-refresh','Drivers.cs','&& !standardAction','', 'standard to top ticks after genuine surrounding-half absence'),
        ('global-action-region-pingpong','HoverHaptics.cs',bound['HoverHaptics.cs'],pingpong, 'simultaneous distinct action producers tick each region once without ping-pong'),
        ('distinct-card-consumed-by-global-frame','VRHand.cs','\n                && _lastSemanticHoverHapticTime != Time.unscaledTime','', 'simultaneous distinct action producers tick each region once without ping-pong'),
        ('duplicate-card-arming','Drivers.cs','_hand.SendHoverHaptic(HoverHapticTargets.Ui(_pointer.Hovered)); // same region, no second hover tick','_hand.SendHaptic(HapticPreset.HoverTick);','arming same card action retains its single hover encounter'),
        ('passive-board-buzz','Board.cs',' || BoardPick.Source != BoardPick.PickSource.Near','', 'passive far board tooltip hover is silent')]
    manifest={'result':str(run/'results.txt'),'cases':[]}
    (run/'source-hashes.json').write_text(json.dumps({'production':hashes,'fixture':fixture_hashes,'publisher':native_hashes,'limits':__doc__},indent=2)+'\n')
    for name,file,before,after,expected in variants:
        case=run/name;production=case/'production';production.mkdir(parents=True)
        for filename,source in bound.items():
            if filename==file:
                assert source.count(before)==1,'Mutation seam drift: '+name
                source=source.replace(before,after,1)
            (production/filename).write_text(source)
        csproj=case/'Haptics.csproj';shutil.copyfile(fixture/'Haptics.csproj',csproj);assembly='Haptics675_'+name.replace('-','_')
        build=subprocess.run([str(Path.home()/'.dotnet/dotnet'),'build',str(csproj),'-c','Release','--nologo','-v','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(game)],capture_output=True,text=True)
        (case/'build.log').write_text(build.stdout+build.stderr)
        if build.returncode:raise SystemExit(build.stdout+build.stderr)
        manifest['cases'].append({'name':name,'dll':str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    project=run/'unity'
    for name in ['Assets/Editor','Assets/Plugins','Packages','ProjectSettings']:(project/name).mkdir(parents=True,exist_ok=True)
    shutil.copyfile(game/'UnityEngine.UI.dll',project/'Assets/Plugins/UnityEngine.UI.dll')
    shutil.copyfile(fixture/'Editor/InteractionRunner.cs',project/'Assets/Editor/InteractionRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.ui":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');manifest_path=run/'manifest.json';manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    command=[str(unity),'-batchmode','-nographics','-disable-assembly-updater','-projectPath',str(project),'-executeMethod','InteractionRunner.Start','-interactionManifest',str(manifest_path),'-nativeGameManaged',str(game),'-logFile',str(run/'unity.log')]
    outcome['reaped']=False
    process=subprocess.Popen(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,start_new_session=True)
    try:
        returncode=process.wait(timeout=300)
    finally:
        # Own the complete Unity/import process group and reap it before cache cleanup,
        # including failed and timed-out runs. Never sweep another agent's Unity process.
        try:os.killpg(process.pid,signal.SIGKILL)
        except ProcessLookupError:pass
        process.wait()
        outcome['reaped']=True
    receipt=Path(manifest['result'])
    if receipt.is_file():print(receipt.read_text(),end='')
    if returncode or not receipt.is_file():raise SystemExit('FAIL haptics runtime; see '+str(run/'unity.log'))
    outcome['exit_code']=0
    print('PASS source-bound semantic haptics and '+str(len(variants)-1)+' causal controls; evidence: '+str(run))

if __name__=='__main__':main()
