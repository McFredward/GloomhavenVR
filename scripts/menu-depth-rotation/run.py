#!/usr/bin/env python3
"""Run the original portrait turntable and real uGUI press/drag dispatch in Unity.

Native preview readiness, model meshes and tracked screen points are explicit
inputs. No gameplay controller, native camera or model transform is changed.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from town_run_retention import cleanup_completed_run
from project_compilers import cleanup_project_compilers, cleanup_project_editors


def execute(command, log, timeout=180):
    with log.open('w') as stream:
        process = subprocess.Popen(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()


def member(source, signature):
    start = source.index(signature)
    brace = source.index('{', start)
    depth, end = 1, brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end] + '\n'


def pointer_source(source):
    # Native input eligibility/hover are explicit inputs. The production press,
    # drag, release and cancellation dispatch methods execute without edits.
    result = '''using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace GloomhavenVR.Hands.Interact;
internal sealed class UguiPointer {
private readonly int _pointerId;
private readonly bool _farRay=true;
private readonly string _sourceTag="rotation-fixture-laser";
private PointerEventData? _data;
private GameObject? _hovered, _pressed, _pressedClickHandler, _dragTarget;
private bool _dragging;
private Vector2 _lastDragPos;
private GameObject? Pressing {get=>_pressed;set=>_pressed=value;}
internal UguiPointer(int id){_pointerId=id;}
private PointerEventData GetData()=>_data??=new PointerEventData(EventSystem.current){pointerId=_pointerId};
internal void Hover(GameObject target,RaycastResult hit){_hovered=target;GetData().pointerCurrentRaycast=hit;}
private void FlushPendingHoverExit(){}
private void SetHovered(GameObject? target,bool force){_hovered=target;}
private void LogPokePick(GameObject handler,GameObject hit){}
private static bool ShouldWithholdUnstartedWindowClick(GameObject target,string tag)=>false;
'''
    for signature in ['    internal void Press(Vector2 screenPos)', '    internal void Drag(Vector2 screenPos)',
                      '    internal void Release(Vector2 screenPos)', '    internal void Cancel()']:
        result += member(source, signature)
    return result + '}\n'


def upstream_source(ray, flat):
    start = ray.index('    internal void Tick()'); brace = ray.index('{', start)
    guard = ray[brace + 1:ray.index('        PickPose pick =', brace)]
    result = '''using UnityEngine;
using GloomhavenVR.Hands;
using GloomhavenVR.Core;
namespace GloomhavenVR.Hands.Interact {
internal sealed class RotationLaserLifetime {
private readonly RotationHandInput _hand;
private readonly UguiPointer _pointer;
private Canvas? _canvas;
private bool _pressing;
internal bool HasHit;
internal float HitDistance;
internal RotationLaserLifetime(UguiPointer pointer,RotationHandInput hand,Canvas canvas){
 _pointer=pointer;_hand=hand;_canvas=canvas;_pressing=true;HasHit=true;HitDistance=1;
}
internal void TickTrackingGate(){
''' + guard + '}\n' + member(ray, '    internal void Cancel()') + '}\n}\n'
    result += '''namespace GloomhavenVR.WorldUI {
internal sealed class RotationFlatDispatch {
private const int ScreenDragPointerId=-120;
private const float ScreenDragLogSeconds=.5f;
private float _lastScreenDragLog=float.NegativeInfinity;
private bool _screenDragActive,_screenDragBegun,_pressing,_latched,_mapPanGesture;
private UnityEngine.EventSystems.PointerEventData? _screenDragData;
private GameObject? _screenDragPress,_screenDragTarget;
private Vector2 _screenDragLastPos;
private readonly RotationMapInput _stereo=new();
private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> s_raycastResults=new();
internal void Begin(Vector2 point){_pressing=true;BeginScreenDrag(point);}
internal void Move(Vector2 point)=>UpdateScreenDrag(point);
internal void PoseLost()=>HideReticle();
}
}
'''
    # Insert the production native-screen methods before its closing braces.
    methods = ''.join(member(flat, signature) for signature in [
        '    private void BeginScreenDrag(Vector2 pixel)', '    private void UpdateScreenDrag(Vector2 pixel)',
        '    private void EndScreenDrag()', '    private void HideReticle()'])
    return result.replace('internal void PoseLost()=>HideReticle();\n}', 'internal void PoseLost()=>HideReticle();\n' + methods + '}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Use the task-private ADB server.')
    signal.signal(signal.SIGTERM, lambda signum, frame: (_ for _ in ()).throw(SystemExit(128 + signum)))
    output = ROOT / '.planning/debug/menu-depth-rotation'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    fixture = ROOT / 'tests/GloomhavenVR.MenuDepthRotationTests'
    production_path = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthCharacterRotation.cs'
    pointer_path = ROOT / 'src/GloomhavenVR/Hands/Interact/UguiPointer.cs'
    ray_path = ROOT / 'src/GloomhavenVR/Hands/Interact/RayUguiDriver.cs'
    flat_path = ROOT / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.6.Pointer.cs'
    sources = {'MenuDepthCharacterRotation.cs': production_path.read_text(), 'Pointer.cs': pointer_source(pointer_path.read_text()),
        'Upstream.cs': upstream_source(ray_path.read_text(), flat_path.read_text())}
    variants = [
        ('production', '', '', '', ''),
        ('lost-portrait-raycast', 'MenuDepthCharacterRotation.cs', 'if (!_image.raycastTarget) _image.raycastTarget = true;',
         'if (!_image.raycastTarget) _image.raycastTarget = false;', 'original native portrait becomes the exact enabled uGUI hit'),
        ('lost-raycast-restoration', 'MenuDepthCharacterRotation.cs', 'if (_image != null) _image.raycastTarget = _nativeRaycast;',
         '/* original raycast state is not restored */', 'closing the native portrait restores its original hit behavior immediately'),
        ('lost-first-drag-delta', 'MenuDepthCharacterRotation.cs',
         'public void OnBeginDrag(PointerEventData eventData) => eventData.useDragThreshold = false;',
         'public void OnBeginDrag(PointerEventData eventData) { eventData.useDragThreshold = false; TryLocalX(eventData, out _lastX); }',
         'the first actual laser drag turns the portrait by its physical width'),
        ('lost-pointer-ownership', 'MenuDepthCharacterRotation.cs', '_pointer.Value != eventData.pointerId',
         'false', 'a second held pointer cannot change the established figure turn'),
        ('lost-identity-reset', 'MenuDepthCharacterRotation.cs', 'Cancel(); _identity = identity; _yaw = 0f;',
         '_identity = identity;', 'a replacement native character starts at its own original facing'),
        ('lost-native-ground', 'MenuDepthCharacterRotation.cs', 'Matrix4x4.Translate(nativeMainGround)',
         'Matrix4x4.identity', 'the actual native main ground remains fixed throughout a full turn'),
        ('lost-view-turn', 'MenuDepthCharacterRotation.cs', 'Quaternion.AngleAxis(yaw, nativeUp)',
         'Quaternion.AngleAxis(0f, nativeUp)', 'actual viewport reveals the original back marker after half a turn'),
        ('lost-native-focus', 'MenuDepthCharacterRotation.cs', 'if (_nativeWindow != null) _nativeWindow.OnPointerDown(eventData);',
         '/* native window focus is not forwarded */', 'portrait input preserves the original native window pointer-focus callback once'),
        ('lost-private-scope', 'MenuDepthCharacterRotation.cs', 'if (owner == null) { Unbind(image); return; }',
         'if (owner == null) owner = image.canvas;', 'automatic unlock and retirement portraits keep their original noninteractive presentation'),
        ('lost-tracking-cancellation', 'Upstream.cs', '_pointer.Cancel();',
         '/* upstream lost ray no longer cancels its pointer */', 'production laser tracking loss releases native portrait capture'),
        ('lost-flat-cancellation', 'Upstream.cs', '            EndScreenDrag();',
         '            /* native screen lost pose no longer ends the drag */', 'native screen pose loss ends the same original turn capture'),
    ]
    if args.case:
        names = set(args.case); assert names <= {row[0] for row in variants}
        variants = [row for row in variants if row[0] in names]
    paths = [production_path, pointer_path, ray_path, flat_path, Path(__file__), ROOT / 'scripts/menu-depth-rotation/Editor/Runner.cs',
             *fixture.glob('*.cs'), fixture / 'MenuDepthRotation.csproj']
    pins = [{'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for path in paths]
    project = run / 'unity'; status = 1
    try:
        for folder in ['Assets/Editor', 'ProjectSettings', 'Packages']:
            (project / folder).mkdir(parents=True)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        shutil.copy2(ROOT / 'scripts/menu-depth-rotation/Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        (run / 'source-pins.json').write_text(json.dumps({'inputs': pins, 'boundaries': __doc__}, indent=2) + '\n')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
        for name, filename, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, source in sources.items():
                code = source
                if source_name == filename:
                    assert code.count(before) == 1, before
                    code = code.replace(before, after, 1)
                (production / source_name).write_text(code)
            private_fixture = case / 'fixture'; shutil.copytree(fixture, private_fixture,
                ignore=shutil.ignore_patterns('bin', 'obj'))
            descriptor = case / 'MenuDepthRotation.csproj'; shutil.copy2(fixture / descriptor.name, descriptor)
            assembly = 'MenuDepthRotation_' + name.replace('-', '_')
            command = ['/home/claw/.dotnet/dotnet', 'build', str(descriptor), '-c', 'Release', '-v', 'quiet', '--nologo',
                '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(private_fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed', '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed')]
            if execute(command, case / 'build.log'):
                raise RuntimeError('Compilation failure is never a causal-control pass: ' + name)
            dll = assemblies / (assembly + '.dll'); shutil.copy2(case / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        status = execute(['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
            '-projectPath', str(project), '-executeMethod', 'MenuDepthRotationRunner.Start', '-menuDepthRotationManifest',
            str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')], run / 'launch.log', 240)
        stable = all(hashlib.sha256(Path(pin['path']).read_bytes()).hexdigest() == pin['sha256'] for pin in pins)
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(pins)}) + '\n')
        if not stable: status = 1
        if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
        if status: raise RuntimeError('Character rotation proof failed: ' + str(run))
    finally:
        (run / 'exit-code.txt').write_text(str(status) + '\n')
        editors = cleanup_project_editors(project, Path('/home/claw/unity-2021.3.5/Editor/Unity'), launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']: raise RuntimeError('Owned editor remains; retaining caches.')
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']: raise RuntimeError('Owned compiler remains; retaining caches.')
        cleanup = cleanup_completed_run('menu-depth-rotation', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    return status


if __name__ == '__main__':
    raise SystemExit(main())
