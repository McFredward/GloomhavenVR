#!/usr/bin/env python3
"""Actual spatial menu sources, native Unity raycasters and production laser/pointer dispatch.

Tracked input, controller readiness and unrelated town/bar/settings services are
explicit fixture inputs. No geometry query, source camera, alpha receipt, native
raycast or pointer callback body is replaced. This is not live OpenXR/game boot.
"""
from pathlib import Path
import argparse, hashlib, json, os, shutil, signal, subprocess, sys, tempfile, time
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0,str(ROOT/'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers,cleanup_project_editors

def block(source,signature):
    assert source.count(signature)==1,signature
    start=source.index(signature);end=source.index('{',start)+1;depth=1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]

def execute(argv,log,timeout=240):
    with log.open('w')as stream:
        child=subprocess.Popen(argv,cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,start_new_session=True)
        try:return child.wait(timeout=timeout)
        finally:
            if child.poll()is None:os.killpg(child.pid,signal.SIGKILL)
            child.wait()

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--case',action='append');args=parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):parser.error('Use a private ADB_SERVER_SOCKET.')
    fixture=Path(__file__).parent;out=ROOT/'.planning/debug/menu-depth-laser';out.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=out));print('Evidence:',run,flush=True);started=time.monotonic()
    completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n')
    code=ROOT/'src/GloomhavenVR/WorldUI/MenuDepth'
    names=['IMenuDepthSource.cs','MenuDepthEye.cs','MenuDepthSources.cs','MenuDepthProjection.cs','MenuDepthScreenProjection.cs',
        'MenuDepthDisplay.cs','MenuDepthHeadRaster.cs','MenuDepthHeadCarrier.cs','MenuDepthNativeUiScope.cs',
        'MenuDepthHeightfield.cs','MenuDepthGraphic.cs','MenuDepthCardPlane.cs','MenuDepthCharacterSource.cs','MenuDepthCharacterProjection.cs']
    paths=[code/name for name in names]+[ROOT/'src/GloomhavenVR/Cards/Art/CardFaceMipBake.cs',
        ROOT/'src/GloomhavenVR/Hands/Interact/UguiPointer.cs',ROOT/'src/GloomhavenVR/Hands/Interact/VisibleUiSurface.cs',
        ROOT/'src/GloomhavenVR/Hands/HoverHaptics.cs',ROOT/'src/GloomhavenVR/Hands/Interact/IPokeable.cs']
    sources={p.name:p.read_text()for p in paths}
    hand_path=ROOT/'src/GloomhavenVR/Hands/VRHand.cs';hand=hand_path.read_text()
    sources['ActualHoverSender.cs']='namespace GloomhavenVR.Hands { internal sealed partial class VRHand {\n'+block(hand,'internal void SendHoverHaptic(')+'\n'+block(hand,'public void SendHaptic(')+'\n}}'
    ray_path=ROOT/'src/GloomhavenVR/Hands/Interact/RayUguiDriver.cs';ray=ray_path.read_text()
    signatures=['internal void Tick()','private void TickPressed(in PickPose pick, float scale)',
        'private bool TrySurface(Canvas canvas, Vector3 origin, Vector3 direction, float maxDistance,',
        'private static bool TryIntersect(Canvas canvas, Vector3 origin, Vector3 direction,',
        'private static void HitCorners(RectTransform rect, in Rect local, Vector3[] into)',
        'private static Vector2 ToScreen(Canvas canvas, Vector3 worldPoint)','internal void Cancel()']
    hit=ray[ray.index('    private static Rect HitRectOf('):ray.index(';',ray.index('    private static Rect HitRectOf('))+1]
    sources['ActualRayUgui.cs']='using GloomhavenVR.Cards; using UnityEngine; using UnityEngine.UI; using UnityEngine.EventSystems; namespace GloomhavenVR.Hands.Interact; internal sealed partial class RayUguiDriver {\n'+'\n'.join(block(ray,s)for s in signatures)+'\n'+hit+'\n}'
    grab_path=ROOT/'src/GloomhavenVR/Hands/Interact/RayGrabDriver.cs';grab=grab_path.read_text()
    sources['ActualBarGeometry.cs']='using UnityEngine;using GloomhavenVR.WorldUI;namespace GloomhavenVR.Hands.Interact;internal sealed partial class RayGrabDriver {\n'+block(grab,'private static bool TryPickBarGeometry(Ray ray, float limit, bool requireGrabbable,')+'\n}'
    cases=[('production','','','',''),
        ('retired-plane-story','','','',''),
        ('retired-plane-old-ready','MenuDepthCardPlane.cs','this != null && _configured && isActiveAndEnabled',
            '_configured && isActiveAndEnabled','retired card plane cannot abort scenario story laser or following grab-bar phase'),
        ('more-cards-hover','','','',''),
        ('more-cards-old-root','MenuDepthCardPlane.cs','nativeRoot: NativeInputRoot()',
            'nativeRoot: _card', 'raised More Cards centre reaches the original row MouseEnter callback'),
        ('more-cards-old-canvas','UguiPointer.cs','Canvas? ownerCanvas = nativeRoot != null',
            'Canvas? ownerCanvas = nativeRoot == null', 'adopted nested mini Canvas still reaches its original outer More Cards row raycaster'),
        ('more-cards-owner-tie-order','UguiPointer.cs','pass == 0 && ownerCanvas != null && ownerCanvas != logical ? ownerCanvas : logical',
            'pass == 0 ? logical : ownerCanvas', 'raised native query preserves the original flat top hit for equal-order nested children'),
        ('more-cards-old-haptics','HoverHaptics.cs','selectable.IsInteractable() || FurtherAbilityPreview(selectable)',
            'selectable.IsInteractable()', 'cold More Cards preview reaches the real hand sender with the ordinary HoverTick preset'),
        ('more-cards-preview-blocker','HoverHaptics.cs','navigation.StateMachine.CurrentState\n                != navigation.StateMachine.GetState(CampaignMapStateTag.ShowingCardLevelUp)',
            'true', 'native ShowingCardLevelUp preview refusal offers no new controller hover encounter'),
        ('more-cards-hover-permission','HoverHaptics.cs',' || !button.scaleNonInteractable','',
            'scaleNonInteractable false denies both native preview feedback and the controller hover offer'),
        ('more-cards-disabled-tooltip','HoverHaptics.cs','selectable.IsInteractable() || FurtherAbilityPreview(selectable)',
            'selectable.IsInteractable() || FurtherAbilityPreview(selectable) || (selectable is ExtendedButton e && e.scaleNonInteractable && e.GetComponent<UILevelUpCard>() == null)',
            'an unrelated disabled ExtendedButton remains silent despite its native scaleNonInteractable flag'),
        ('more-cards-native-audio-lost','','','','cold More Cards preview plays its exact original native hover sound once'),
        ('source-draw-throws','MenuDepthCharacterSource.cs','_camera.Render();',
            'if (_rayRenderedIdentity != null) throw new InvalidOperationException("fixture source draw failed"); _camera.Render();',''),
        ('card-flat','MenuDepthCardPlane.cs','float depth = -Mathf.Abs(root.rect.width) * plane._depthWidthRatio * xScale / zScale;',
            'float depth = 0f;', 'laser point matches independent physical card-width relief'),
        ('native-coordinate','MenuDepthCardPlane.cs','original.MultiplyPoint3x4(new Vector3(local.x, local.y, 0f)), distance, preferGraphic:',
            '(origin + direction * distance), distance, preferGraphic:', 'physical card hit maps back to the original native event coordinate'),
        ('alpha-opaque','MenuDepthGraphic.cs','private float RayAlpha(Vector2 uv)\n    {',
            'private float RayAlpha(Vector2 uv)\n    { if (uv.x >= 0f) return 1f;', 'transparent source texels leave real holes'),
        ('source-camera','MenuDepthCharacterSource.cs','owner.Source._rayCollecting && owner.Source._camera == current',
            'owner.Source._rayCollecting', 'actual source-camera receipts select exactly the native visible Unity LOD member'),
        ('source-lease','MenuDepthCharacterSource.cs','owner.Source._rayCollecting && owner.Source._camera == current',
            'owner.Source._camera == current', 'unleased original-camera draws never enter'),
        ('pooled-witness','MenuDepthCharacterSource.cs','_rayWitnessCandidates[w] != null && !_rayWitnessCandidates[w].Retired',
            '_rayWitnessCandidates[w] != null', 'same-frame pooled original replacement retains a live witness'),
        ('stale-source','MenuDepthCharacterSource.cs','_rayRenderedFrame < Time.frameCount - 1',
            '_rayRenderedFrame < -1000', 'source geometry expires when its actual capture stops drawing'),
        ('permission-fallthrough','UguiPointer.cs','if (!preferSurface && !any) any = TryRaycast(canvas!, screenPos, out topHit);',
            'if (!any) any = TryRaycast(canvas!, screenPos, out topHit);', 'visible read-only card retains physical occlusion'),
        ('skin-per-hand','MenuDepthCharacterSource.cs','if (_frame != Time.frameCount)',
            'if (_frame != -1234)', 'both hands share at most one potentially-hit skin bake'),
        ('native-raised-flat','ActualRayUgui.cs','if (WorldUI.CanvasConversion.TryGetHitRect(canvas,out _))',
            'if (false && WorldUI.CanvasConversion.TryGetHitRect(canvas,out _))', 'actual original 32px confirmation members supply their physical world plane')]
    if args.case:
        selected=set(args.case);assert selected<={c[0]for c in cases};cases=[c for c in cases if c[0]in selected]
    unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');managed=unity.parent/'Data/Managed'
    game=ROOT/'ressources/GH_Data/Managed';bundle=ROOT/'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle'
    native=ROOT/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/hero_brute_assets_all.bundle'
    cards=ROOT/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle'
    field=ROOT/'src/GloomhavenVR/Assets/MenuDepth/Story/af21d2cb8296eb19eac3f458e3773d1f.gmd'
    os.environ['GHVR_LASER_NATIVE_BRUTE']=str(native);os.environ['GHVR_LASER_FIELD']=str(field)
    os.environ['GHVR_LASER_NATIVE_ROW']=str(run/'native-row.json')
    unitypy=Path('/home/claw/unitypy-venv')
    toolchain=[*managed.glob('UnityEngine/*.dll'),*Path('/home/claw/.dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'),
        unitypy/'bin/python',Path(sys.executable).resolve(),
        *[p for p in (unitypy/'lib').rglob('*') if p.suffix in {'.py','.so'} and '__pycache__' not in p.parts],
        Path('/home/claw/.nuget/packages/lib.harmony/2.4.2/lib/net472/0Harmony.dll')]
    toolchain += [p for package in ['bepinex.assemblypublicizer.msbuild/0.4.3','bepinex.assemblypublicizer/0.4.3']
        for p in (Path('/home/claw/.nuget/packages')/package).rglob('*') if p.is_file()]
    pins={str(p):hashlib.sha256(p.read_bytes()).hexdigest()for p in [*paths,ray_path,grab_path,hand_path,*fixture.glob('*'),fixture/'Editor/Runner.cs',bundle,*toolchain,
        unity,native,cards,field,Path('/home/claw/.dotnet/dotnet'),*game.glob('*.dll'),ROOT/'ressources/GH_Data/level9',ROOT/'ressources/GH_Data/sharedassets2.assets',
        ROOT/'scripts/menu726-native-profile.py',ROOT/'scripts/menu-depth-character/project_compilers.py']if p.is_file()}
    (run/'source-pins.json').write_text(json.dumps({'sha256':pins,'scope':__doc__,'subset':args.case},indent=2)+'\n')
    project=run/'unity';status=1;stable=None
    try:
        retained=run/'Sources';retained.mkdir();shutil.copytree(fixture,retained/'fixture')
        for name,source in sources.items():(retained/name).write_text(source)
        (retained/'RayUguiDriver.full-source.txt').write_text(ray)
        (retained/'VRHand.full-source.txt').write_text(hand)
        if execute(['/home/claw/unitypy-venv/bin/python',str(fixture/'audit-native.py'),'--output',str(run/'native-row.json')],run/'native-audit.log'):
            raise RuntimeError('Original More Cards row audit failed')
        assemblies=run/'assemblies';assemblies.mkdir();manifest={'result':str(run/'results.txt'),'evidence':str(run),
            'gameManaged':str(game.resolve()),'harmonyRuntime':'/home/claw/.nuget/packages/lib.harmony/2.4.2/lib/net472','cases':[]}
        for name,filename,before,after,expected in cases:
            case=run/name;production=case/'production';production.mkdir(parents=True)
            for source_name,source in sources.items():
                if source_name==filename:
                    assert source.count(before)==1,(name,before);source=source.replace(before,after,1)
                (production/source_name).write_text(source)
            shutil.copy2(fixture/'Laser.csproj',case/'Laser.csproj');assembly='MenuDepthLaser_'+name.replace('-','_')
            command=['/home/claw/.dotnet/dotnet','build',str(case/'Laser.csproj'),'-c','Release','-v','quiet','--nologo','--disable-build-servers','-nr:false',
                '-p:UseSharedCompilation=false','-p:CaseName='+assembly,'-p:FixtureDir='+str(retained/'fixture'),'-p:ProductionDir='+str(production),
                '-p:UnityManaged='+str(managed),'-p:GameManaged='+str(game),'-p:DepthBundle='+str(bundle)]
            if execute(command,case/'build.log'):raise RuntimeError('Compilation failed: '+name)
            dll=assemblies/(assembly+'.dll');shutil.copy2(case/'bin/Release/net472'/dll.name,dll)
            shutil.copy2(case/'obj/project.assets.json',case/'resolved-build-assets.json')
            manifest['cases'].append({'name':name,'dll':str(dll),'expected':expected,
                'method':'RunRetiredPlaneStory' if name.startswith('retired-plane-') else 'RunMoreCards' if name.startswith('more-cards-') else 'Run'})
        for directory in ['Assets/Editor','Packages','ProjectSettings']:(project/directory).mkdir(parents=True)
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
        shutil.copy2(fixture/'Editor/Runner.cs',project/'Assets/Editor/Runner.cs')
        (run/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
        (run/'assembly-pins.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest()for p in assemblies.glob('*.dll')},indent=2)+'\n')
        status=execute(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MenuDepthLaserRunner.Start',
            '-menuDepthLaserManifest',str(run/'manifest.json'),'-logFile',str(run/'unity.log')],run/'launch.log',360)
        stable=all(Path(p).is_file()and hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in pins.items())
        (run/'source-stability.json').write_text(json.dumps({'unchanged':stable,'inputs':len(pins)})+'\n')
        if not stable:status=1
        if(run/'results.txt').is_file():print((run/'results.txt').read_text(),flush=True)
    finally:
        editors=cleanup_project_editors(project,unity,launcher_reaped=True);(run/'editor-cleanup.json').write_text(json.dumps(editors,indent=2)+'\n');assert not editors['remaining']
        compilers=cleanup_project_compilers(project,editor_reaped=True);(run/'compiler-cleanup.json').write_text(json.dumps(compilers,indent=2)+'\n');assert not compilers['remaining']
        receipt=cleanup_completed_run('menu-depth-laser',status,completion,ROOT,process_reaped=True);(run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
        (run/'result.json').write_text(json.dumps({'exit_code':status,'source_stable':stable,'elapsed_seconds':time.monotonic()-started,'variants':len(cases)},indent=2)+'\n')
    return status
if __name__=='__main__':
    def interrupted(signum,frame):raise KeyboardInterrupt('signal '+str(signum))
    signal.signal(signal.SIGTERM,interrupted)
    raise SystemExit(main())
