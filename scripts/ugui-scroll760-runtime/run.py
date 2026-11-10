#!/usr/bin/env python3
"""Replay actual production stick/scroll dispatch against original Unity EventTrigger and ScrollRect.

Tracked hand values and settled hover are explicit inputs. Production stick response,
wheel-owner lookup, delivery and per-hand scroll arbitration execute unchanged. Real
GraphicRaycaster selects the list/card descendants used for the critical old/fixed case.
Native asset metadata independently binds the pointer-only card adapters to this case.
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


# Keep this unrelated shared-input service explicitly unbound, exactly as a
# private window behaves with no multiplayer scroll publisher installed.
INACTIVE_SHARED_SCROLL = """// This private native-pointer fixture has no shared-window session or input publisher.
// An absent production SharedWindowScrollBridge.Submit returns false as declared here;
// all original native callback, scroll, geometry and control oracles still execute.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace GloomhavenVR.WorldUI {
internal enum SharedScrollInput : byte { Wheel = 2, Position = 3, PointerDown = 5, PointerDrag = 6, PointerUp = 7 }
internal sealed class SharedWindowScrollProxy : MonoBehaviour {
    internal bool CanScroll => false;
    internal byte Control => 0;
}
internal static class SharedWindowScrollBridge {
    internal static bool Wheel(Component control, Vector2 delta) => false;
    internal static bool Pointer(Component control, SharedScrollInput input, PointerEventData data) => false;
}
}
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--case', action='append')
    args = parser.parse_args(); source_root = args.source_root.resolve()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Use a task-private ADB_SERVER_SOCKET.')
    fixture = Path(__file__).parent
    output = ROOT / '.planning/debug/ugui-scroll760-runtime'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); print('Evidence:', run, flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    started = time.monotonic(); status = 1; stable = False
    source_dir = source_root / 'src/GloomhavenVR/Hands/Interact'
    pointer_path = source_dir / 'UguiPointer.cs'; pointer = pointer_path.read_text()
    ray_path = source_dir / 'RayUguiDriver.cs'; ray = ray_path.read_text()
    focus_path = source_dir / 'UiScrollFocus.cs'
    actual = ('using System.Collections.Generic;using UnityEngine;using UnityEngine.EventSystems;'
              'using UnityEngine.UI;namespace GloomhavenVR.Hands.Interact {internal sealed class UguiPointer {'
              'private readonly List<Component> _scrollHandlers = new(4);private GameObject? _hovered;'
              'private PointerEventData? _pointerData;private readonly int _pointerId=-760;'
              'internal GameObject? Hovered=>_hovered;internal void SetFixtureHover(GameObject? target)=>_hovered=target;'
              'internal Vector2 FixtureScrollDelta=>GetData().scrollDelta;'
              + '\n'.join(block(pointer, signature) for signature in
                  ['internal void Scroll(Vector2 scrollDelta)', 'internal static GameObject? FindScrollTarget(GameObject start, List<Component> handlers)', 'private PointerEventData GetData()'])
              + '}internal sealed class RayUguiDriver {'
              'private readonly GloomhavenVR.Hands.VRHand _hand;private readonly UguiPointer _pointer;'
              'internal RayUguiDriver(GloomhavenVR.Hands.VRHand hand,UguiPointer pointer){_hand=hand;_pointer=pointer;}'
              'internal void TickFixtureScroll()=>TickStickScroll();'
              + '\n'.join(line.strip() for line in ray.splitlines() if line.strip().startswith(
                  ('private const float ScrollDeadzone =', 'private const float ScrollNotchesPerSecond =')))
              + block(ray, 'private void TickStickScroll()') + '}}'
              'namespace GloomhavenVR.Hands {internal enum HandSide {Left,Right}'
              'internal sealed class VRHand {internal HandSide Side;internal UnityEngine.Vector2 Thumbstick;}}')
    sources = {'ActualScroll.cs': actual, 'UiScrollFocus.cs': focus_path.read_text()}
    sources['SharedScrollBoundary762.cs'] = INACTIVE_SHARED_SCROLL
    cases = [
        ('production', '', '', ''),
        ('old-empty-event-trigger', 'ExecuteEvents.Execute(target, data, ExecuteEvents.scrollHandler);',
         'ExecuteEvents.ExecuteHierarchy(_hovered, data, ExecuteEvents.scrollHandler);',
         'card descendants scroll past the native pointer-only EventTriggers'),
        ('ignore-real-event-trigger', 'if (!handlesScroll)', 'if (!handlesScroll || component != null)',
         'explicit EventTrigger Scroll entry retains nearest-owner priority'),
        ('ignore-derived-event-trigger', 'component.GetType() == typeof(EventTrigger)', 'component is EventTrigger',
         'derived EventTrigger override retains its unlisted native Scroll behavior'),
        ('ignore-disabled-handler', ' || component is Behaviour behaviour && !behaviour.isActiveAndEnabled', '',
         'disabled intermediate handlers yield to the active native list'),
        ('ignore-stick-deadzone', 'if (Mathf.Abs(y) < ScrollDeadzone)', 'if (Mathf.Abs(y) > float.MaxValue)',
         'resting stick deadzone never scrolls the native list'),
    ]
    if args.case:
        selected = set(args.case); assert selected <= {case[0] for case in cases}
        cases = [case for case in cases if case[0] in selected]
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity'); managed = unity.parent / 'Data/Managed'
    game = ROOT / 'ressources/GH_Data/Managed'
    paths = [pointer_path, ray_path, focus_path, *fixture.rglob('*'), unity,
             game / 'UnityEngine.UI.dll', ROOT / 'scripts/town_run_retention.py',
             ROOT / 'scripts/menu-depth-character/project_compilers.py']
    pins = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths if path.is_file()}
    (run / 'source-pins.json').write_text(json.dumps({'sha256': pins, 'scope': __doc__}, indent=2) + '\n')
    project = run / 'unity'
    try:
        retained = run / 'Sources'; retained.mkdir(); shutil.copytree(fixture, retained / 'fixture')
        for name, source in sources.items(): (retained / name).write_text(source)
        (retained / 'UguiPointer.full-source.txt').write_text(pointer)
        (retained / 'RayUguiDriver.full-source.txt').write_text(ray)
        if execute(['/home/claw/unitypy-venv/bin/python', str(fixture / 'audit-native.py'), '--output', str(run / 'native-card-adapters.json')], run / 'native-audit.log'):
            raise RuntimeError('Native card adapter audit failed')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        manifest = {'result': str(run / 'results.txt'), 'cases': []}
        for name, before, after, expected in cases:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, source in sources.items():
                if before and source_name == 'ActualScroll.cs':
                    assert source.count(before) == 1, (name, before); source = source.replace(before, after, 1)
                (production / source_name).write_text(source)
            shutil.copy2(fixture / 'Scroll.csproj', case / 'Scroll.csproj')
            assembly = 'UguiScroll760_' + name.replace('-', '_')
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
                          '-executeMethod', 'UguiScroll760Runner.Start', '-uguiScroll760', str(run / 'manifest.json'),
                          '-logFile', str(run / 'unity.log')], run / 'launch.log', 240)
        stable = all(Path(path).is_file() and hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in pins.items())
        if not stable: status = 1
        if (run / 'results.txt').is_file(): print((run / 'results.txt').read_text(), flush=True)
    finally:
        editors = cleanup_project_editors(project, unity, launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n'); assert not editors['remaining']
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n'); assert not compilers['remaining']
        receipt = cleanup_completed_run('ugui-scroll760-runtime', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': status, 'source_stable': stable,
            'elapsed_seconds': time.monotonic() - started, 'cases': len(cases)}, indent=2) + '\n')
    return status


if __name__ == '__main__':
    def interrupted(signum, frame): raise KeyboardInterrupt('signal ' + str(signum))
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
