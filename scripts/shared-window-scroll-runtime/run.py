#!/usr/bin/env python3
"""Run the exact shared-window scroll coordinator, codec, proxy and pointer delivery in Unity.

Three independent coordinator instances represent separate processes. Native
session/subject/source registries, unrelated raised-model picks and coordinate-
transpiler scope are explicit boundary ports; original modular Results Build,
Prune/RootPose, binding read/validate/apply and clone neutralization execute.
Original laser TrySurface/TryIntersect/VisibleUiSurface precede registered nested
ray dispatch and both generic and Results-only stick input. Original Unity UI,
scroll callbacks, camera projections, masks and groups execute. Tick's explicit clock samples an actual intermediate renderer
frame without depending on a software renderer's wall-clock frame rate. The
fixture does not claim Photon, a loaded Victory game controller or HMD evidence.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers, cleanup_project_editors


def block(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature); end = source.index('{', start) + 1; depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end]


def execute(argv, log, timeout=180):
    with log.open('w') as stream:
        process = subprocess.Popen(argv, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--presentation-source-root', type=Path,
                        help='Exact disjoint Results producer checkout; defaults to --source-root.')
    parser.add_argument('--case', action='append')
    args = parser.parse_args(); source_root = args.source_root.resolve()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Use a task-private ADB_SERVER_SOCKET.')
    fixture = Path(__file__).parent
    output = ROOT / '.planning/debug/shared-window-scroll-runtime'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); print('Evidence:', run, flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    started = time.monotonic(); status = 1; stable = False
    pointer_path = source_root / 'src/GloomhavenVR/Hands/Interact/UguiPointer.cs'
    pointer = pointer_path.read_text()
    actual = ('using System.Collections.Generic;using UnityEngine;using UnityEngine.EventSystems;using UnityEngine.UI;'
        'namespace GloomhavenVR.Hands.Interact {internal sealed class UguiPointer {'
        'private readonly List<Component> _scrollHandlers=new(4);'
        'private GameObject? _hovered,_pressed,_pressedClickHandler,_dragTarget;private Component? _sharedScrollDrag;'
        'private bool _sharedScrollbarPress,_dragging;private Vector2 _lastDragPos;private PointerEventData? _pointerData;'
        'private readonly List<RaycastResult> _hits=new();private bool _farRay=true;private int _side;'
        'private int _lastPokeHitCount,_lastPokePadRank;private GameObject? _lastPokeRunnerUp;'
        'private bool TryAimRay(out Vector3 origin,out Vector3 direction){origin=direction=default;return false;}'
        'private readonly int _pointerId=-101;private string _sourceTag="fixture laser";'
        'private void FlushPendingHoverExit(){}'
        'private static bool ShouldWithholdUnstartedWindowClick(GameObject target,string source)=>false;'
        'private void LogPokePick(GameObject handler,GameObject hit){}'
        'private GameObject? Pressing{get=>_pressed;set=>_pressed=value;}'
        'internal GameObject? Hovered=>_hovered;'
        'internal bool TryRaycast(Canvas canvas,Vector2 screen,out RaycastResult hit)=>TryRaycast(canvas,screen,out hit,true);'
        'internal void SetFixtureHover(GameObject target,RaycastResult hit=default){_hovered=target;GetData().pointerCurrentRaycast=hit;}'
        + '\n'.join(block(pointer,sig) for sig in ['internal void Scroll(Vector2 scrollDelta)',
           'internal static GameObject? FindScrollTarget(GameObject start, List<Component> handlers)',
           'internal void Press(Vector2 screenPos)','internal void Drag(Vector2 screenPos)',
           'internal void Release(Vector2 screenPos)','private PointerEventData GetData()',
           'private bool TryRaycast(Canvas canvas, Vector2 screenPos, out RaycastResult topHit, bool worldPlane)',
           'private bool TryRaycastTop(GraphicRaycaster raycaster, PointerEventData data, out RaycastResult top, Canvas? admittedHost = null)',
           'private static bool Beats(in RaycastResult challenger, in RaycastResult incumbent)',
           'private static int StableOrder(in RaycastResult hit)'])
        + 'internal bool FixturePick(Canvas canvas,Vector2 position,out RaycastResult hit)=>TryRaycast(canvas,position,out hit,true);}}')
    actual += 'namespace GloomhavenVR.WorldUI {internal static class UiSoundEar{internal static void BeforeUiEvent(){}}}'
    actual += 'namespace GloomhavenVR {internal static class Plugin {internal sealed class Setting{internal bool Value=true;}internal static Setting ScrollWithStickOnly=new();}}'
    stick_path = source_root / 'src/GloomhavenVR/Hands/Interact/RayUguiDriver.cs'
    stick = stick_path.read_text()
    stick_constants = '\n'.join(re.search(r'private const float ' + name + r' = [^;]+;', stick).group(0)
                                for name in ['ScrollDeadzone', 'ScrollNotchesPerSecond'])
    actual_stick = ('using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;namespace GloomhavenVR.Hands.Interact {'
        'internal sealed class RayUguiDriver {private readonly UguiPointer _pointer;private readonly VRHand _hand;'
        'private static readonly Vector3[] Corners=new Vector3[4];'
        'internal RayUguiDriver(UguiPointer pointer,VRHand hand){_pointer=pointer;_hand=hand;hand.RayUgui=this;}'
        'internal GameObject? Hovered=>_pointer.Hovered;'
        'internal void FixtureTick()=>TickStickScroll();'
        'internal bool FixtureAim(Vector3 origin,Vector3 direction,out RaycastResult hit){'
        'Canvas? best=null;float budget=10000;Vector3 input=default;hit=default;'
        'foreach(Canvas canvas in UguiPokeSurfaces.Surfaces)'
        'if(canvas!=null&&canvas.isActiveAndEnabled&&TrySurface(canvas,origin,direction,budget,out float distance,out _,out Vector3 point,out _))'
        '{best=canvas;budget=distance;input=point;}'
        'bool found=best!=null&&_pointer.FixturePick(best,ToScreen(best,input),out hit);'
        '_pointer.SetFixtureHover(found?hit.gameObject:null!,hit);return found;}'
        + stick_constants + re.search(r'private static Rect HitRectOf\([^;]+;', stick).group(0)
        + '\n'.join(block(stick, signature) for signature in [
            'private void TickStickScroll()',
            'private bool TrySurface(Canvas canvas, Vector3 origin, Vector3 direction, float maxDistance,',
            'private static void HitCorners(RectTransform rect, in Rect local, Vector3[] into)',
            'private static bool TryIntersect(Canvas canvas, Vector3 origin, Vector3 direction,',
            'private static Vector2 ToScreen(Canvas canvas, Vector3 worldPoint)']) + '}}')
    tracked = [pointer_path, source_root/'src/GloomhavenVR/Net/NetProtocol.cs',
        source_root/'src/GloomhavenVR/Net/SharedWindowScrollCodec.cs',source_root/'src/GloomhavenVR/Net/SharedWindowScrollSync.cs',
        source_root/'src/GloomhavenVR/WorldUI/Modal/SharedWindowScrollBridge.cs',
        source_root/'src/GloomhavenVR/WorldUI/Modal/ModalFallback.5.ResultsScroll.cs',
        source_root/'src/GloomhavenVR/Hands/Interact/UguiPokeSurfaces.cs',
        source_root/'src/GloomhavenVR/Hands/Interact/VisibleUiSurface.cs']
    sources = {path.name:path.read_text() for path in tracked[2:]};sources['ActualPointer.cs']=actual
    sources['ActualStick.cs'] = actual_stick
    town = source_root / 'src/GloomhavenVR/Net/TownServices'
    for name in ['TownServiceBinding.cs', 'TownServiceMaterial.cs', 'TownServiceFlameClock.cs',
                 'TownServiceNeutralize.cs', 'TownServiceAssets.cs', 'TownServiceAssets.PackedSprites.cs']:
        path = town / name; tracked.append(path); sources[name] = path.read_text()
    frame_path = town / 'TownServiceFrame.cs'; tracked.append(frame_path); frame = frame_path.read_text()
    sources['Frame.cs'] = ('#pragma warning disable CS0649\nusing System;using System.Collections.Generic;'
        'namespace GloomhavenVR.Net.TownServices {' + '\n'.join(block(frame, signature) for signature in [
            'internal sealed class TownServiceFrame', 'internal sealed class TownServiceNode',
            'internal sealed class TownServiceValue\n', 'internal sealed class TownServiceValueComparer',
            'internal static class TownServiceProperty']) + '}')
    presentation_path = (args.presentation_source_root or source_root).resolve() / 'src/GloomhavenVR/Net/ScenarioResultsPresentation.cs'
    tracked.append(presentation_path); presentation = presentation_path.read_text()
    sources['ActualResultsBuild.cs'] = (
        'using System;using System.IO;using System.Collections.Generic;using UnityEngine;'
        'using GloomhavenVR.WorldUI;using GloomhavenVR.Net.TownServices;using Object=UnityEngine.Object;'
        'namespace GloomhavenVR.Net {internal sealed partial class FixtureResultsBuilder {'
        + '\n'.join(block(presentation, signature) for signature in [
            'private void Build(ScenarioResultsState state, ConvertedPanel panel)',
            'private void Prune(Transform source, Transform copy, Dictionary<Transform, ushort> roots)',
            'private static void SetLayer(Transform root, int layer)',
            'private static void RootPose(Transform root, TownServiceNode node)'])
        + re.search(r'private static bool Excluded\(Transform child\)[^;]+;', presentation).group(0)
        + 'private static TownServiceFrame Frame(ScenarioResultsPart part)=>new(){Structure=part.Structure,Nodes=part.Nodes};}}')
    tracked.append(stick_path)
    protocol=tracked[1].read_text()
    constants='\n'.join(line.strip() for line in protocol.splitlines() if any(term in line for term in ['public const uint Magic =','public const byte Version =','public const ushort ModBuild =']))
    sources['Protocol.cs']='namespace GloomhavenVR.Net {internal static class NetProtocol {'+constants+block(protocol,'public static uint HashMapKey(string? id)')+'}}'
    cases = [
        ('production','','','',''),
        ('missing-visible-small-state','SharedWindowScrollSync.cs',
         'if (original != null)\n                target.Picture.Apply',
         'if (original != null && state.Kind == 0)\n                target.Picture.Apply',
         'accepted small author state moves visible common content without a new Results graph'),
        ('stale-graph-overwrites-scroll','SharedWindowScrollSync.cs',
         'ApplyResultsPicture(pictureRoot, Time.unscaledTime);', '',
         'unchanged-descriptor post-Paint projection defeats a stale graph content and handle overwrite'),
        ('linked-handle-split-state','SharedWindowScrollSync.cs',
         'if (original is Scrollbar bar)\n                foreach (Target linked',
         'if (original is Scrollbar bar && state.Kind == 0)\n                foreach (Target linked',
         'visible common scrollbar handle follows accepted author position and size without callbacks'),
        ('modular-original-names','ActualResultsBuild.cs','clone.name = original.name;','',
         'modular original Results paths resolve the exact viewport and scrollbar before reveal'),
        ('inert-header-hover','SharedWindowScrollBridge.cs','if (header != null) lease.AdmitHeader(host, header, enabled);','',
         'the real laser picker resolves the visible Results header outside the common viewport'),
        ('invisible-header-hover','SharedWindowScrollBridge.cs','original.color.a * original.canvasRenderer.GetAlpha()',
         '1f * original.canvasRenderer.GetAlpha()',
         'a transparent original header never leaves an invisible laser target'),
        ('private-composed-host','SharedWindowScrollBridge.cs','Singleton<MapStoryController>.Instance.dialogBox?.transform','window.transform',
         'private loadout sibling outside the exact public story subtree stays local'),
        ('early-client-mutation','SharedWindowScrollSync.cs','return IsPublicInput(component);','return false;',
         'first public wheel is consumed before descriptor without mutating private native content'),
        ('duplicate-wheel','SharedWindowScrollSync.cs','LastRequest.TryGetValue(sender, out uint sequence) && !SharedWindowScrollCodec.Newer(message.Sequence, sequence)','false',
         'duplicate nonce cannot execute the native wheel twice'),
        ('foreign-source-state','SharedWindowScrollSync.cs','if (IsAuthor(message.Kind) || sender != Authors[message.Kind]) return true;',
         'if (IsAuthor(message.Kind)) return true;',
         "foreign peer cannot impersonate the common author's canonical viewport state"),
        ('missing-control-generation','SharedWindowScrollSync.cs','&& source.State.Generation == message.Generation','&& true',
         'old component generation cannot mutate its same-path pooled replacement'),
        ('ancestor-scrollbar-steals-button','ActualPointer.cs','nativeDownOwner != null ? nativeDownOwner.GetComponent<Scrollbar>() : null',
         '_hovered.GetComponentInParent<Scrollbar>()',
         'a nearer original pointer-down handler retains priority over an ancestor shared scrollbar'),
        ('retired-kind-misses-close','SharedWindowScrollSync.cs',
         'if (IsAuthor(value) && (Windows[value].Subject == window || Windows[value].NativeSubject == window))',
         'if (IsAuthor(value) && value == 0)',
         'captured exact native subject survives shared-kind retirement before Hide notification'),
        ('observer-snap','SharedWindowScrollSync.cs','target.Duration = 1f / 15f;','target.Duration = 0;',
         'observer native viewport has an intermediate smooth frame rather than15Hz snapping'),
        ('inert-group-blocks-own-proxy','SharedWindowScrollSync.cs','group.ignoreParentGroups = true;','group.ignoreParentGroups = false;',
         'real original GraphicRaycaster reaches the common viewport through inert observer groups'),
        ('missing-nested-input-canvas','SharedWindowScrollBridge.cs','Hands.Interact.UguiPokeSurfaces.RegisterNested(host, canvas);','',
         'only the exact common picture input canvas joins the existing nested-pointer census'),
        ('inactive-first-picture','SharedWindowScrollSync.cs|SharedWindowScrollBridge.cs','GetComponentInParent<Canvas>(true)',
         'GetComponentInParent<Canvas>()',
         'only the exact common picture input canvas joins the existing nested-pointer census'),
        ('former-drag-owner','SharedWindowScrollSync.cs','target.PointerOwner != request.Requester || ', '',
         "ended earlier player's drag cannot move a newer explicit scrollbar gesture"),
        ('common-header-fallback','ModalFallback.5.ResultsScroll.cs',
         'SharedWindowScrollProxy? common = SharedWindowScrollBridge.ResultsInput?.Invoke(wp.Panel.HostTransform);',
         'SharedWindowScrollProxy? common = null;',
         'Results header joystick uses the common viewport with no private native scroll target'),
        ('native-permission-lost','SharedWindowScrollSync.cs','|| !InputAllowed(original)','|| false',
         'native canvas-group permission is retained at execution time'),
        ('map-off-host-retained','SharedWindowScrollSync.cs','if (common > 0 && Participant(common)) return common;','if (common > 0) return common;',
         'map-off host yields only public map scroll authority to the smallest participating VR peer'),
    ]
    if args.case:
        selected=set(args.case);assert selected <= {case[0] for case in cases};cases=[case for case in cases if case[0] in selected]
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity'); managed = unity.parent / 'Data/Managed'
    game = ROOT / 'ressources/GH_Data/Managed'
    paths = [*tracked, *fixture.rglob('*'), unity, game/'UnityEngine.UI.dll', ROOT/'scripts/town_run_retention.py',ROOT/'scripts/menu-depth-character/project_compilers.py']
    pins = {str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in paths if path.is_file()}
    (run/'source-pins.json').write_text(json.dumps({'sha256':pins,'scope':__doc__},indent=2)+'\n')
    project = run / 'unity'
    try:
        retained = run / 'Sources'; retained.mkdir(); shutil.copytree(fixture, retained / 'fixture')
        for name, source in sources.items(): (retained / name).write_text(source)
        (retained / 'UguiPointer.full-source.txt').write_text(pointer)
        (retained / 'RayUguiDriver.full-source.txt').write_text(stick)
        (retained / 'ScenarioResultsPresentation.full-source.txt').write_text(presentation)
        assemblies = run / 'assemblies'; assemblies.mkdir()
        manifest = {'result': str(run / 'results.txt'), 'cases': []}
        for name, filename, before, after, expected in cases:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, source in sources.items():
                if before and source_name in filename.split('|'):
                    assert source.count(before) >= 1, (name, before); source = source.replace(before, after)
                (production / source_name).write_text(source)
            shutil.copy2(fixture / 'Scroll.csproj', case / 'Scroll.csproj')
            assembly = 'SharedScroll762_' + name.replace('-', '_')
            command = ['/home/claw/.dotnet/dotnet', 'build', str(case / 'Scroll.csproj'), '-c', 'Release', '-v', 'quiet', '--nologo',
                       '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                       '-p:FixtureDir=' + str(retained / 'fixture'), '-p:ProductionDir=' + str(production),
                       '-p:UnityManaged=' + str(managed), '-p:GameManaged=' + str(game)]
            if execute(command, case / 'build.log'):
                raise RuntimeError('Compilation failed: ' + name)
            dll = assemblies / (assembly + '.dll'); shutil.copy2(case / 'bin/Release/net472' / dll.name, dll)
            shutil.copy2(case / 'obj/project.assets.json', case / 'resolved-build-assets.json')
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
        for directory in ['Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings']: (project / directory).mkdir(parents=True)
        shutil.copy2(game / 'Unity.TextMeshPro.dll', project / 'Assets/Plugins/Unity.TextMeshPro.dll')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        shutil.copy2(fixture / 'Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'assembly-pins.json').write_text(json.dumps({str(path): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in assemblies.glob('*.dll')}, indent=2) + '\n')
        status = execute(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                          '-executeMethod', 'SharedScroll762Runner.Start', '-sharedScroll762', str(run / 'manifest.json'),
                          '-logFile', str(run / 'unity.log')], run / 'launch.log', 240)
        stable = all(Path(path).is_file() and hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in pins.items())
        if not stable: status = 1
        if (run / 'results.txt').is_file(): print((run / 'results.txt').read_text(), flush=True)
    finally:
        editors = cleanup_project_editors(project, unity, launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n'); assert not editors['remaining']
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n'); assert not compilers['remaining']
        receipt = cleanup_completed_run('shared-window-scroll-runtime', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': status, 'source_stable': stable,
            'elapsed_seconds': time.monotonic() - started, 'cases': len(cases)}, indent=2) + '\n')
    return status


if __name__ == '__main__':
    def interrupted(signum, frame): raise KeyboardInterrupt('signal ' + str(signum))
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
