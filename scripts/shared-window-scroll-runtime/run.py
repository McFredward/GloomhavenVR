#!/usr/bin/env python3
"""Run the exact shared-window scroll coordinator, codec, proxy and pointer delivery in Unity.

Three independent coordinator instances represent separate processes. Native
session/subject registries are explicit boundary ports; original Unity UI,
scroll callbacks, camera projections, raycasts, masks, groups and nested-input
registry execute. Tick's explicit clock samples an actual intermediate renderer
frame without depending on a software renderer's wall-clock frame rate. The
fixture does not claim Photon, a loaded Victory game controller or HMD evidence.
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
        'private readonly int _pointerId=-101;private string _sourceTag="fixture laser";'
        'private void FlushPendingHoverExit(){}'
        'private GameObject? Pressing{get=>_pressed;set=>_pressed=value;}'
        'internal void SetFixtureHover(GameObject target,RaycastResult hit=default){_hovered=target;GetData().pointerCurrentRaycast=hit;}'
        + '\n'.join(block(pointer,sig) for sig in ['internal void Scroll(Vector2 scrollDelta)',
           'internal static GameObject? FindScrollTarget(GameObject start, List<Component> handlers)',
           'internal void Press(Vector2 screenPos)','internal void Drag(Vector2 screenPos)','private PointerEventData GetData()']) + '}}')
    actual += 'namespace GloomhavenVR.WorldUI {internal static class UiSoundEar{internal static void BeforeUiEvent(){}}}'
    actual += 'namespace GloomhavenVR {internal static class Plugin {internal sealed class Setting{internal bool Value=true;}internal static Setting ScrollWithStickOnly=new();}}'
    tracked = [pointer_path, source_root/'src/GloomhavenVR/Net/NetProtocol.cs',
        source_root/'src/GloomhavenVR/Net/SharedWindowScrollCodec.cs',source_root/'src/GloomhavenVR/Net/SharedWindowScrollSync.cs',
        source_root/'src/GloomhavenVR/WorldUI/Modal/SharedWindowScrollBridge.cs',
        source_root/'src/GloomhavenVR/Hands/Interact/UguiPokeSurfaces.cs']
    sources = {path.name:path.read_text() for path in tracked[2:]};sources['ActualPointer.cs']=actual
    protocol=tracked[1].read_text()
    constants='\n'.join(line.strip() for line in protocol.splitlines() if any(term in line for term in ['public const uint Magic =','public const byte Version =','public const ushort ModBuild =']))
    sources['Protocol.cs']='namespace GloomhavenVR.Net {internal static class NetProtocol {'+constants+block(protocol,'public static uint HashMapKey(string? id)')+'}}'
    cases = [
        ('production','','','',''),
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
        assemblies = run / 'assemblies'; assemblies.mkdir()
        manifest = {'result': str(run / 'results.txt'), 'cases': []}
        for name, filename, before, after, expected in cases:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, source in sources.items():
                if before and source_name == filename:
                    assert source.count(before) == 1, (name, before); source = source.replace(before, after, 1)
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
        for directory in ['Assets/Editor', 'Packages', 'ProjectSettings']: (project / directory).mkdir(parents=True)
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
