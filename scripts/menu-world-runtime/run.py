#!/usr/bin/env python3
"""Execute ready-menu admission, original world-screen coordinator and real grabs.

One Unity2021.3.5 GL session runs the complete containing scope and causal controls.
The original native UI/input/art and callback boundaries are declared; no port
replaces menu readiness, world-window geometry, near election, carry or scale.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run
spec=importlib.util.spec_from_file_location('menu_world_compiler_cleanup',ROOT/'scripts/check-environment-menu-scope.py')
compiler_cleanup=importlib.util.module_from_spec(spec);spec.loader.exec_module(compiler_cleanup)

WHOLE=(
 'Core/Events/MenuWorldContext.cs',
 'Net/SharedWindowMoveRegister.cs','Net/SharedWindowMoveOrder.cs','WorldUI/Modal/SharedWindowInteractionBridge.cs',
 'WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs','WorldUI/FlatScreen/FlatScreen.5.Placement.cs',
 'WorldUI/Surfaces/SurfaceGrabBar.cs','WorldUI/Grab/PanelGrab.cs',
 'WorldUI/Grab/GrabBarLayout.cs','WorldUI/Grab/GrabBarTween.cs','WorldUI/Grab/BarSizeSettle.cs',
 'WorldUI/Grab/WindowReFacePolicy.cs','WorldUI/Grab/WindowReFaceTween.cs',
 'WorldUI/HeadFacing.cs','WorldUI/Conversion/PanelPlacement.cs',
 'Core/GrabBar.cs','Core/GrabBarVisual.cs','Core/GrabBarTexture.cs',
 'Hands/Interact/IGrabbable.cs','Hands/Interact/VRInteractables.cs','Hands/Interact/ProximityGrabber.cs',
 'Hands/Interact/RayGrabDriver.cs','Hands/Interact/LaserPointerPolicy.cs',
 'Rig/Flight.cs','Rig/WorldGrab.cs','Rig/SnapTurn.cs','Rig/RigTarget.cs',
 'Rig/LocalTurnControl.cs','Rig/ScrollTurnGate.cs','Rig/LiftWedge.cs',
)
KERNELS=(
 'WorldUI/Grab/GrabbableModal.cs',
 'Core/Events/VRModeStateMachine.cs','Hands/HandsDriver.cs','Hands/VRHand.cs','Hands/Interact/RayInteractor.cs','Hands/Interact/RayUguiDriver.cs','Board/BoardDriver.cs','WorldUI/MapRoom/MapLocationInteractor.cs',
 'Rig/VRRigDriver.Recenter.cs','WorldUI/FlatScreen/FlatScreen.6.Pointer.cs',
 'WorldUI/Conversion/CanvasConversion.8.Order.cs','WorldUI/Conversion/CanvasConversion.6.Hide.cs',
 'WorldUI/WorldUIConfig.cs',
 'Rig/ComfortSettings.cs','Rig/Comfort.cs',
 'Rig/VRRigDriver.cs',
 'WorldUI/FlatScreen/FlatScreen.1.Core.cs',
)

def block(source,signature,indent='    ',suffix=''):
    marker=indent+signature
    if source.count(marker)!=1:raise RuntimeError('Ambiguous production extraction: '+signature)
    start=source.index(marker);end=source.index('\n'+indent+'}',start)+len('\n'+indent+'}')
    result=source[start:end]+suffix
    if not result.strip().startswith(signature):raise RuntimeError('Extraction prefix drift')
    return result


def generated(raw):
    result={Path(name).name:raw[name] for name in WHOLE}
    modal=raw['WorldUI/Grab/GrabbableModal.cs']
    author=modal[modal.index('    bool IPanelGrabAuthority.IsSharedGrab'):modal.index('    /// <summary>\n    /// USER-OWNED POSE',modal.index('    bool IPanelGrabAuthority.IsSharedGrab'))]
    wants=modal[modal.index('    private bool WantsReFaceOnRelease()'):modal.index(';',modal.index('    private bool WantsReFaceOnRelease()'))+1]
    result['ActualSharedOwner746.cs']='using System; using UnityEngine; using GloomhavenVR.Core; namespace GloomhavenVR.WorldUI; internal sealed partial class GrabbableModal {\n'+author+block(modal,'void IPanelGrabOwner.OnGrabFinished()')+'\n'+wants+'\n}'
    mode=raw['Core/Events/VRModeStateMachine.cs']
    enums='\n'.join(block(mode,'internal enum '+name,indent='') for name in ('VRMode','Interactors','HandRole'))
    data='\n'.join(block(mode,'private static readonly Dictionary'+signature,suffix=';') for signature in
                  ('<VRMode, Interactors> InteractorPolicy = new()','<(VRMode, HandRole), Interactors> HandPolicy = new()'))
    result['ActualModePolicy.cs']='using System; using System.Collections.Generic; namespace GloomhavenVR.Core.Events;\n'+enums+'\ninternal static class VRModeStateMachine { internal static bool TableInFrontOfPlayer=false; internal static VRMode CurrentMode=VRMode.Menu2D;\n'+data+'\n'+block(mode,'public static Interactors InteractorsFor(VRMode mode, HandRole role)')+'\n}\n'
    hands=raw['Hands/HandsDriver.cs']
    result['ActualHandsPolicy.cs']='using GloomhavenVR.Core; using GloomhavenVR.Core.Events; namespace GloomhavenVR.Hands; internal sealed partial class HandsDriver {\n'+block(hands,'private void Update()')+'\n'+block(hands,'private void ApplyMode(VRMode mode)')+'\n}'
    ray=raw['Hands/Interact/RayInteractor.cs']
    active=re.search(r'    public bool Active =>[^;]+;',ray)[0]
    native_mask=re.search(r'    public LayerMask Mask =[^;]+;',ray)[0]
    native_limit=re.search(r'    private const float MaxDistanceMeters =[^;]+;',ray)[0]
    query=re.search(r'if \((Physics\.Raycast\(origin, direction, out RaycastHit hit, maxDistance, Mask\))\)',ray)
    if query is None:raise RuntimeError('Native physics raycast extraction drift')
    capture='internal void CaptureNativePhysics(Vector3 origin,Vector3 direction){float maxDistance=MaxDistanceMeters*_hand.WorldScale;bool hasHit='+query[1]+';Pick=new PickPose(origin,direction,hasHit,hit.distance,hit.collider);}'
    result['ActualRayState.cs']='using UnityEngine; namespace GloomhavenVR.Hands.Interact; internal sealed partial class RayInteractor {\n'+active+'\n'+native_mask+'\n'+native_limit+'\n'+capture+'\n}\n'
    ui=raw['Hands/Interact/RayUguiDriver.cs']
    ui_methods='\n'.join(block(ui,sig) for sig in ('internal void Tick()', 'private void TickPressed(in PickPose pick, float scale)', 'private static bool TryIntersect(Canvas canvas, Vector3 origin, Vector3 direction,', 'private static void HitCorners(RectTransform rect, in Rect local, Vector3[] into)', 'private static Vector2 ToScreen(Canvas canvas, Vector3 worldPoint)', 'internal void Cancel()'))
    ui_rect=ui[ui.index('    private static Rect HitRectOf('):ui.index(';',ui.index('    private static Rect HitRectOf('))+1]
    result['ActualRayUgui.cs']='using GloomhavenVR.Cards; using UnityEngine; using UnityEngine.EventSystems; namespace GloomhavenVR.Hands.Interact; internal sealed partial class RayUguiDriver {\n'+ui_methods+'\n'+ui_rect+'\n}\n'
    masks='using UnityEngine; using GloomhavenVR.Core; using GloomhavenVR.Hands; namespace GloomhavenVR.Board { internal sealed partial class BoardDriver {\n'+block(raw['Board/BoardDriver.cs'],'private static void SyncRayMask()')+'\n}}\n'
    map=raw['WorldUI/MapRoom/MapLocationInteractor.cs']
    release=block(map,'if (_maskTaken)',indent='        ')
    masks+='namespace GloomhavenVR.WorldUI.MapRoom { internal sealed partial class MapLocationInteractor {\n'+block(map,'private void ApplyMask()')+'\ninternal void ReleaseMaskOnly(string reason){int hadPokes=0;\n'+release+'\n}\n}}\n'
    result['ActualNativeMaskOwners.cs']=masks
    result['ActualHandMask.cs']='namespace GloomhavenVR.Hands; internal sealed partial class VRHand {\n'+block(raw['Hands/VRHand.cs'],'internal void SetInteractorMask(Core.Events.Interactors mask)')+'\n}'
    result['ActualMenuRecenter.cs']='using GloomhavenVR.Core; using UnityEngine; namespace GloomhavenVR.Rig; internal sealed partial class VRRigDriver {\n'+block(raw['Rig/VRRigDriver.Recenter.cs'],'private void RecenterMenu()')+'\n}'
    rig=raw['Rig/VRRigDriver.cs'];arrival_start=rig.index('        if (_pendingRecenter && _camera != null && _camera.transform.localPosition.sqrMagnitude > 1e-6f)')
    arrival_end=rig.index('MenuWorldContext.Tick(_kind == RigKind.Menu, !_pendingRecenter);',arrival_start)+len('MenuWorldContext.Tick(_kind == RigKind.Menu, !_pendingRecenter);')
    tracking=re.search(r'    internal static bool TrackingSettled =>[^;]+;',rig)[0]
    dispatch=block(raw['Rig/VRRigDriver.Recenter.cs'],'internal void Recenter()')
    dispatch=dispatch[:dispatch.index('        if (_kind == RigKind.Map)')]+ '        throw new System.InvalidOperationException("Non-menu recenter is outside this fixture");\n    }'
    result['ActualFirstPoseCoordinator.cs']='using System; using GloomhavenVR.Core; using GloomhavenVR.Core.Events; using UnityEngine; namespace GloomhavenVR.Rig; internal sealed partial class VRRigDriver {\n'+block(rig,'private enum RigKind')+'\n'+tracking+'\n'+dispatch+'\ninternal void TickArrival() {\n'+rig[arrival_start:arrival_end]+'\n}}\n'
    result['ActualNativePointer.cs']='using GloomhavenVR.Core; using GloomhavenVR.Hands; using GloomhavenVR.Hands.Interact; using UnityEngine; namespace GloomhavenVR.WorldUI; internal sealed partial class FlatScreen {\n'+'\n'.join(block(raw['WorldUI/FlatScreen/FlatScreen.6.Pointer.cs'],sig) for sig in ('private void TickPointer()','private void YieldScreenPointer()','private void HideReticle()'))+'\n}'
    order=raw['WorldUI/Conversion/CanvasConversion.8.Order.cs']
    result['ActualCanvasRegistration.cs']='using UnityEngine; namespace GloomhavenVR.WorldUI; internal static partial class CanvasConversion {\n'+'\n'.join((block(order,'internal static void RegisterOrderFollower(ConvertedPanel panel, Renderer renderer, int offset,'),block(order,'private static void ApplyPanelOrder(ConvertedPanel panel, int order)'),block(raw['WorldUI/Conversion/CanvasConversion.6.Hide.cs'],'internal static void AddRenderRoot(ConvertedPanel? panel, Transform? root)')))+'\n}'
    result['ActualFacingEnum.cs']='namespace GloomhavenVR.WorldUI;\n'+block(raw['WorldUI/WorldUIConfig.cs'],'internal enum WindowFaceMode',indent='')+'\n'
    comfort=raw['Rig/ComfortSettings.cs']
    result['ActualMotionEnums.cs']='namespace GloomhavenVR.Rig;\n'+'\n'.join(block(comfort,'internal enum '+name,indent='') for name in ('TurnMode','TurnHandChoice','FlightDirectionSource'))+'\n'
    result['ActualRigClamp.cs']='using UnityEngine; namespace GloomhavenVR.Rig;\n'+block(raw['Rig/Comfort.cs'],'internal static class RigClamp',indent='')+'\n'
    core=raw['WorldUI/FlatScreen/FlatScreen.1.Core.cs']
    result['ActualCaptureLifetime.cs']='using GloomhavenVR.Core; namespace GloomhavenVR.WorldUI; internal sealed partial class FlatScreen { internal void TickCaptureLifetime() {\n'+block(core,'if (_visible && _quad == null)',indent='        ')+'\n}}\n'
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/menu-world-runtime')
    parser.add_argument('--production-only',action='store_true')
    args=parser.parse_args();root=args.source_root.resolve()
    paths={name:root/'src/GloomhavenVR'/name for name in (*WHOLE,*KERNELS)}
    raw={name:p.read_text() for name,p in paths.items()}
    mutations=[
        ('shared-reface-veto-restored746','WorldUI/Grab/WindowReFacePolicy.cs',
         'WindowFaceMode mode = WorldUIConfig.WindowFacing != null',
         'if (shared) return false; WindowFaceMode mode = WorldUIConfig.WindowFacing != null',
         'accepted shared laser release starts the original modal release tween'),
        ('shared-grip-revocation-lost746','WorldUI/Grab/PanelGrab.cs',
         '&& !authority.MayContinueGrab)', '&& bool.Parse("false"))',
         'losing actual shared handle force-drops both grabbers without a release or facing tween'),
        ('shared-cancel-is-drop746','WorldUI/Grab/PanelGrab.cs',
         'if (_owner is IPanelGrabAuthority authority && authority.IsSharedGrab)',
         'if (_owner is IPanelGrabAuthority authority && authority.IsSharedGrab && bool.Parse("false"))',
         'external cancellation closes the actual shared grip without release-facing'),
        ('unshown-menu-admitted','Core/Events/MenuWorldContext.cs','if (window == null || !window.IsVisible)','if (window == null)',
         'EULA or sign-in initialization does not stand in for the actual visible main menu'),
        ('untracked-menu-admitted','Core/Events/MenuWorldContext.cs','if (!menuRig || !trackingSettled || menu == null','if (!menuRig || menu == null',
         'shown menu before tracked recenter cannot admit a provisional world pose'),
        ('first-pose-recenter-omitted','Rig/VRRigDriver.cs','Recenter();\n                if (_kind == RigKind.Scenario && !_scenarioStart.HasPose)',
         '/* first-pose recenter omitted */\n                if (_kind == RigKind.Scenario && !_scenarioStart.HasPose)',
         'actual menu recenter resolves the final ready eye before room/window spawn'),
        ('head-follow-restored','WorldUI/FlatScreen/FlatScreen.5.Placement.cs','if (TickWorldMenu())\n            return;',
         'if (TickWorldMenu() && (Rig.VRRigDriver.RigRoot == null || (Rig.VRRigDriver.RigRoot.position - _placedRigPos).sqrMagnitude <= 1e-4f))\n            return;',
         "actual stick flight retains the independent menu's world pose"),
        ('paused-carry-frozen','WorldUI/Grab/PanelGrab.cs','float k = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);','float k = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);',
         'paused menu follows changed laser aim with stationary palm after fresh-open'),
        ('readiness-mask-edge-lost','Hands/HandsDriver.cs','_menuWorldReady = menuReady;\n            ApplyMode(VRModeStateMachine.CurrentMode);','_menuWorldReady = menuReady;',
         'actual HandsDriver readiness edge enables both real near-grabbers without a mode transition'),
        ('late-background-detached','WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs','        if (_backQuad != null)\n        {\n            Transform back = _backQuad.transform;',
         '        if (_backQuad != null && bool.Parse("false"))\n        {\n            Transform back = _backQuad.transform;',
         'late split background joins the same physical world-menu frame'),
        ('reverse-surface-creation-lost','WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs','        EnsureWorldMenuRear();','        /* reverse surface omitted */',
         'actual menu coordinator creates one owned rear mesh under the original capture'),
        ('reverse-surface-winding-lost','WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs','for (int i = 0; i + 2 < triangles.Length; i += 3)','for (int i = triangles.Length; i + 2 < triangles.Length; i += 3)',
         'rear mesh reverses native face winding while keeping native UVs'),
        ('reverse-mesh-retirement-lost','WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs','        if (_worldMenuRearMesh != null) Object.Destroy(_worldMenuRearMesh);','        /* owned reverse mesh retained */',
         'closing menu presentation destroys its owned reverse mesh and surface'),
        ('external-capture-retirement-lost','WorldUI/FlatScreen/FlatScreen.1.Core.cs','            ReleaseWorldMenu();\n            _visible = false;',
         '            /* original capture loss retained the wrapper */\n            _visible = false;',
         'external original capture loss retires its wrapper and owned reverse mesh'),
        ('absolute-reopen-size-lost','WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs','                _worldMenuUnit = _savedMenuUnit;','                _worldMenuUnit = width / 1920f;',
         'same-opening movie round trip retains absolute window size after independent world zoom'),
        ('old-flight-menu-gate','Rig/Flight.cs','mode == VRMode.Menu2D && !MenuWorldContext.IsReady && !RigTarget.IsDevProxy','mode == VRMode.Menu2D && !RigTarget.IsDevProxy',
         'actual ready-menu stick flight moves the player while native time is paused'),
        ('paused-flight-clock-lost','Rig/Flight.cs','float meters = response * ComfortSettings.FlightMaxSpeed.Value * Time.unscaledDeltaTime;',
         'float meters = response * ComfortSettings.FlightMaxSpeed.Value * Time.deltaTime;',
         'actual ready-menu stick flight moves the player while native time is paused'),
        ('old-turn-menu-gate','Rig/SnapTurn.cs','vrMode == VRMode.Menu2D && !MenuWorldContext.IsReady && !RigTarget.IsDevProxy','vrMode == VRMode.Menu2D && !RigTarget.IsDevProxy',
         'actual ready-menu snap turn rotates the live rig by its configured step'),
        ('old-world-grab-menu-gate','Rig/WorldGrab.cs','mode == VRMode.Menu2D && !MenuWorldContext.IsReady && !RigTarget.IsDevProxy','mode == VRMode.Menu2D && !RigTarget.IsDevProxy',
         'actual ready-menu stick-click world drag moves the live rig'),
        ('old-table-clamp-in-menu','Rig/Comfort.cs','if (Core.Events.MenuWorldContext.IsReady)','if (bool.Parse("false"))',
         'ready world menu ignores a retained gameplay-table clamp even with saved free movement disabled'),
        ('null-camera-target-admitted','WorldUI/FlatScreen/FlatScreen.9.WorldMenu.cs','!((owner._uiRt != null && camera.targetTexture == owner._uiRt)\n                || (owner._rt != null && camera.targetTexture == owner._rt))',
         '!(camera.targetTexture == owner._uiRt || camera.targetTexture == owner._rt)',
         'captured camera with null target is not proof of menu ownership'),
    ]
    cases=[('production',raw,'')]
    mutations.extend([
        ('map-missing-hands-broad-mask','WorldUI/MapRoom/MapLocationInteractor.cs',
         '_maskWasLeft = left != null ? left.Ray.Mask.value : Physics.DefaultRaycastLayers & ~VRLayers.ModLayerMask;',
         '_maskWasLeft = left != null ? left.Ray.Mask.value : Physics.DefaultRaycastLayers;',
         'missing-hand map fallback restores the same native-only default on return to menu'),
        ('old-native-menu-physics-mask','Hands/Interact/RayInteractor.cs',
         'public LayerMask Mask = Physics.DefaultRaycastLayers & ~Core.VRLayers.ModLayerMask;',
         'public LayerMask Mask = Physics.DefaultRaycastLayers;',
         'ready main-menu laser resolves the visible production rod before native pointer delivery'),
        ('menu-laser-disconnected','Hands/Interact/RayGrabDriver.cs',
         'if (!_hand.Ray.Active || VRHands.Primary != _hand || !_hand.Grabber.Enabled)\n        {\n            ClearHover();',
         'if (_hand.HasPose || !_hand.Ray.Active || VRHands.Primary != _hand || !_hand.Grabber.Enabled)\n        {\n            ClearHover();',
         'normal laser route starts actual menu carry through ForceGrab'),
        ('menu-ui-before-rod','Hands/Interact/RayUguiDriver.cs','barDistance <= bestDist','barDistance > bestDist',
         'normal laser route starts actual menu carry through ForceGrab'),
        ('menu-rod-steals-native-widget','Hands/Interact/RayGrabDriver.cs',
         '|| _hand.RayUgui.IsPressing\n            || (_hand.RayUgui.HasHit && _hand.RayUgui.HitDistance < bestDist)',
         '|| (_hand.RayUgui.IsPressing && !_hand.HasPose)',
         'nearer native widget receives its press and blocks menu laser carry'),
        ('menu-carry-screen-clickthrough','WorldUI/FlatScreen/FlatScreen.6.Pointer.cs',
         'if (hand != null && hand.RayGrab.OwnsPointerFrame)',
         'if (hand != null && hand.RayGrab.OwnsPointerFrame && !hand.HasPose)',
         'active menu laser carry parks native screen pointer without click-through'),
        ('menu-laser-reach-shortened','Hands/Interact/RayGrabDriver.cs','internal const float MaxDistanceMeters = 20f;',
         'internal const float MaxDistanceMeters = 5f;',
         'ordinary twenty-metre laser reach still admits a seven-metre menu handle'),
        ('menu-native-widget-release-lost','Hands/Interact/RayUguiDriver.cs',
         'if (_pressing && _canvas != null)','if (_pressing && _canvas != null && !_hand.HasPose)',
         'latched native widget keeps original trigger-up before later menu grab'),
        ('paused-menu-laser-reel-frozen','WorldUI/Grab/PanelGrab.cs',
         'Rig.ComfortSettings.LaserCarryReelSpeed.Value * Time.unscaledDeltaTime;',
         'Rig.ComfortSettings.LaserCarryReelSpeed.Value * Time.deltaTime;',
         'paused native menu stick reel actually pushes the carried window farther after fresh-open'),
    ])
    if not args.production_only:
        for name,file,before,after,expected in mutations:
            if raw[file].count(before)!=1:raise RuntimeError('Control binding drift: '+name)
            changed=dict(raw);changed[file]=raw[file].replace(before,after);cases.append((name,changed,expected))
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()));print('Evidence: '+str(run),flush=True)
    completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n')
    unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');dotnet=str(Path.home()/'.dotnet/dotnet')
    inputs=[*paths.values(),*HERE.rglob('*.cs'),HERE/'MenuWorld.csproj',Path(__file__),root/'scripts/check-environment-menu-scope.py',unity,
            root/'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader',
            *sorted((unity.parent/'Data/Managed/UnityEngine').glob('UnityEngine*.dll')),
            Path.home()/'.dotnet/sdk/8.0.423/Roslyn/bincore/csc.dll']
    pins={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    compiler_temp=Path(tempfile.mkdtemp(prefix='ghvr-menuworld-',dir='/tmp'))
    env=dict(os.environ,TMPDIR=str(compiler_temp),UseSharedCompilation='false',DOTNET_CLI_USE_MSBUILD_SERVER='0',MSBUILDDISABLENODEREUSE='1',ADB_SERVER_SOCKET='localfilesystem:/tmp/ghvr-menu-world-'+run.name+'.sock',ADB_MDNS_AUTO_CONNECT='')
    (run/'compiler-temp.json').write_text(json.dumps({'TMPDIR':str(compiler_temp),'ADB_SERVER_SOCKET':env['ADB_SERVER_SOCKET']})+'\n')
    process=None;status=1;reaped=True
    try:
        manifest={'result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
        (run/'toolchain.json').write_text(json.dumps({'unity':'2021.3.5f1','api':'OpenGLCore','dotnet':subprocess.check_output([dotnet,'--version'],env=env,text=True).strip()},indent=2)+'\n')
        for filename,value in raw.items():
            original=run/'original-production'/filename;original.parent.mkdir(parents=True,exist_ok=True);original.write_text(value)
        assemblies=run/'executed-assemblies';assemblies.mkdir()
        for name,current,expected in cases:
            case=run/name;production=case/'production';production.mkdir(parents=True)
            for filename,value in generated(current).items():(production/filename).write_text(value)
            project=case/'MenuWorld.csproj';shutil.copyfile(HERE/project.name,project)
            assembly='MenuWorld_'+name.replace('-','_')
            cmd=[dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:UseSharedCompilation=false','-nodeReuse:false','-p:CaseName='+assembly,'-p:FixtureDir='+str(HERE),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(unity.parent/'Data/Managed')]
            (case/'build-command.json').write_text(json.dumps(cmd,indent=2)+'\n')
            result=subprocess.run(cmd,capture_output=True,text=True,env=env);(case/'compile.log').write_text(result.stdout+result.stderr)
            if result.returncode:raise RuntimeError('Compilation failures are not valid controls: '+str(case/'compile.log'))
            dll=assemblies/(assembly+'.dll');shutil.copyfile(case/'bin/Release/net472'/dll.name,dll)
            manifest['cases'].append({'name':name,'dll':str(dll),'sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),'expected':expected})
        project=run/'unity'
        for path in ('Assets/Editor','Packages','ProjectSettings'):(project/path).mkdir(parents=True)
        shutil.copyfile(HERE/'Editor/MenuWorldRunner.cs',project/'Assets/Editor/MenuWorldRunner.cs')
        shutil.copyfile(root/'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader',project/'Assets/OriginalOverlay.shader')
        (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{name:'1.0.0' for name in ('com.unity.modules.physics','com.unity.modules.imageconversion','com.unity.modules.jsonserialize')}}))
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
        cmd=[str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MenuWorldRunner.Start','-menuWorldManifest',str(path),'-logFile',str(run/'unity.log')]
        if not os.environ.get('DISPLAY'):cmd=['xvfb-run','-a',*cmd]
        (run/'editor-command.json').write_text(json.dumps(cmd,indent=2)+'\n')
        process=subprocess.Popen(cmd,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,start_new_session=True);reaped=False
        try:code=process.wait(timeout=300)
        finally:
            try:os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError:pass
            process.wait();reaped=True
        results=(run/'results.txt').read_text() if (run/'results.txt').exists() else 'Missing runtime results'
        print(results,end='',flush=True)
        if code or 'FAIL' in results or results.count('PASS')!=len(cases):raise RuntimeError('World-menu runtime failed: '+str(run/'unity.log'))
        if not all(p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest()==pins[str(p)] for p in inputs):raise RuntimeError('Execution inputs changed during suite')
        status=0;print(('PARTIAL PASS' if args.production_only else 'PASS')+': '+str(len(cases))+' actual world-menu variants; '+str(run),flush=True)
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid,signal.SIGKILL);process.wait();reaped=True
        compilers=compiler_cleanup.cleanup_run_compilers(run,editor_reaped=reaped);(run/'compiler-cleanup.json').write_text(json.dumps(compilers,indent=2)+'\n')
        if compilers['remaining'] or compilers['refused']:
            (run/'.keep').write_text('Private compiler ownership unresolved; inspect compiler-cleanup.json.\n');raise RuntimeError('Private compiler ownership/exit unresolved')
        subprocess.run(['adb','kill-server'],env=env,capture_output=True,timeout=20)
        receipt=cleanup_completed_run('menu-world-runtime',status,completion,ROOT,process_reaped=reaped);(run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
        shutil.rmtree(compiler_temp)
    return status

if __name__=='__main__':raise SystemExit(main())
