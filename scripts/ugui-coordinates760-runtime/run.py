#!/usr/bin/env python3
"""Original Unity GraphicRaycaster IL with the exact production synthetic-coordinate seam.

Unity's unmodified window-coordinate algorithm executes against explicit desktop/OS
display inputs (3440x1440 system,1920x1080 window), because Xvfb cannot certify the
Windows RelativeMouseAt backend. Original camera/graphic/filter/blocker geometry,
the production plane and pointer lookup, Harmony-transformed native raycaster,
scope nesting and exception paths all execute in Unity2021.3.5. This is not HMD or
Windows/window-manager acceptance and does not change any render-camera projection.
"""
import argparse
import hashlib
import importlib.util
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
spec = importlib.util.spec_from_file_location('scroll_runner', ROOT / 'scripts/ugui-scroll760-runtime/run.py')
shared = importlib.util.module_from_spec(spec); spec.loader.exec_module(shared)
block = shared.block; execute = shared.execute


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT); parser.add_argument('--case', action='append')
    parser.add_argument('--common', action='store_true', help='Qualify native full-screen callers and processed virtual-mouse provenance.')
    parser.add_argument('--capture-source-root', type=Path, help='Explicit producer API source while workers have not yet integrated.')
    args = parser.parse_args(); source_root = args.source_root.resolve()
    if not os.environ.get('ADB_SERVER_SOCKET'): parser.error('Use a task-private ADB_SERVER_SOCKET.')
    fixture = Path(__file__).parent
    suite = 'native-ui-pointer760-runtime' if args.common else 'ugui-coordinates760-runtime'
    output = ROOT / '.planning/debug' / suite
    output.mkdir(parents=True, exist_ok=True); run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    print('Evidence:', run, flush=True); completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    started = time.monotonic(); status = 1; stable = False; project = run / 'unity'
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity'); managed = unity.parent / 'Data/Managed'
    game = ROOT / 'ressources/GH_Data/Managed'; code = source_root / 'src/GloomhavenVR/Hands/Interact'
    helper_path = code / 'UguiRaycastCoordinates.cs'; pointer_path = code / 'UguiPointer.cs'; ray_path = code / 'RayUguiDriver.cs'
    fit_path = source_root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs'
    pointer = pointer_path.read_text(); ray = ray_path.read_text()
    fields = ('private readonly bool _farRay=true;private readonly List<RaycastResult> _hits=new(16);'
              'private int _lastPokeHitCount,_lastPokePadRank;private GameObject? _lastPokeRunnerUp;')
    actual_pointer = ('using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;'
        'namespace GloomhavenVR.Hands.Interact {internal sealed class UguiPointer {' + fields
        + 'internal bool FixturePick(GraphicRaycaster raycaster,PointerEventData data,out RaycastResult hit,Canvas? admittedHost=null)=>TryRaycastTop(raycaster,data,out hit,admittedHost);'
        + block(pointer, 'private bool TryRaycastTop(GraphicRaycaster raycaster, PointerEventData data, out RaycastResult top, Canvas? admittedHost = null)') + '}}')
    kernels = [block(ray, signature) for signature in ['private static bool TryIntersect(', 'private static void HitCorners(', 'private static Vector2 ToScreen(']]
    start = ray.index('private static Rect HitRectOf('); kernels.append(ray[start:ray.index(';', start) + 1])
    plane = ('using UnityEngine;namespace GloomhavenVR.Hands.Interact {internal static class InputPlane760 {'
        'private static readonly Vector3[] Corners=new Vector3[4];'
        'internal static bool Test(Canvas canvas,Vector3 origin,Vector3 direction,float maxDist,out float distance,out Vector3 hit)=>TryIntersect(canvas,origin,direction,maxDist,out distance,out hit);'
        'internal static Vector2 Screen(Canvas canvas,Vector3 world)=>ToScreen(canvas,world);' + '\n'.join(kernels) + '}}')
    boundary = ('using System.Collections.Generic;using UnityEngine;namespace GloomhavenVR.Core {'
        'internal static class VRLog {internal static readonly List<string> Warnings=new(),DebugLines=new();internal static bool DebugEnabled;internal static bool WantsDebug=>DebugEnabled;'
        'internal static void Debug(string scope,string message)=>DebugLines.Add(message);internal static void Warn(string scope,string message)=>Warnings.Add(message);}}'
        'namespace GloomhavenVR.WorldUI {internal static class FlatScreen {internal static bool OwnsNativeUiPixels(Canvas? canvas,Camera? camera)=>false;}'
        'internal static class CanvasConversion {internal static bool TryGetHitRect(Canvas canvas,out Rect rect){rect=((RectTransform)canvas.transform).rect;return true;}}}'
        'namespace GloomhavenVR.Hands.Interact {internal static class UguiPokeSurfaces {internal static readonly Dictionary<Canvas,List<Canvas>> Nested=new();'
        'internal static List<Canvas>? NestedOf(Canvas canvas)=>Nested.TryGetValue(canvas,out var children)?children:null;}}'
        'internal sealed class NativeMouseMetrics {internal int renderingWidth,renderingHeight,systemWidth,systemHeight;}'
        'internal static class NativeMouseDisplay {internal static readonly NativeMouseMetrics main=new();'
        'internal static UnityEngine.Vector3 RelativeMouseAt(UnityEngine.Vector2 point)=>new(point.x,point.y,0);}'
        'internal static class NativeMouseScreen {internal static bool fullScreen;}')
    fit_lookup = block(fit_path.read_text(), 'internal static bool TryGetHitRect(Canvas? canvas, out Rect hit)')
    before = 'internal static class CanvasConversion {internal static bool TryGetHitRect(Canvas canvas,out Rect rect){rect=((RectTransform)canvas.transform).rect;return true;}}'
    fit_adapter = ('internal static class CanvasConversion {private sealed class HitRectEntry {internal Canvas? Canvas;internal Rect Hit;}'
        'private static readonly Dictionary<int,HitRectEntry> HitRects=new();'
        'internal static void SetFixtureHitRect(Canvas canvas,Rect bounds){HitRects[canvas.GetInstanceID()]=new HitRectEntry{Canvas=canvas,Hit=bounds};}'
        + fit_lookup + '}')
    assert boundary.count(before) == 1; boundary = boundary.replace(before, fit_adapter)
    sources = {'UguiRaycastCoordinates.cs': helper_path.read_text(), 'ActualPointer.cs': actual_pointer,
        'ActualPlane.cs': plane, 'Boundary.cs': boundary, 'VisibleUiSurface.cs': (code / 'VisibleUiSurface.cs').read_text(),
        'PokeOnlyTarget.cs': (code / 'PokeOnlyTarget.cs').read_text()}
    cases = [
        ('production', '', '', ''),
        ('old-window-coordinate-route', 'if (_expected.HasValue && ReferenceEquals', 'if (_expected.HasValue && data == null && ReferenceEquals',
         'scoped actual production pointer reaches the original button in windowed mode'),
        ('inherit-native-pointer', '&& ReferenceEquals(_expected.Value.Data, data)', '&& data != null',
         'nested native pointer data never inherits the synthetic coordinate exception'),
        ('inherit-other-raycaster', '&& (ReferenceEquals(_expected.Value.Raycaster, raycaster)', '&& (ReferenceEquals(raycaster, raycaster)',
         'nested unrelated native raycaster never inherits the synthetic coordinate exception'),
        ('leak-coordinate-scope', 'public void Dispose() => _expected = _previous;', 'public void Dispose() => _expected = _expected ?? _previous;',
         'native caller after the pointer lease retains original windowed conversion'),
        ('replace-native-fallback', 'return Native!(position);', 'return new Vector3(position.x, position.y, 0);',
         'native caller after the pointer lease retains original windowed conversion'),
        ('ignore-exception-boundary', '|| unsupportedBoundary)', '|| false && unsupportedBoundary)',
         'unsupported exception boundary falls back to untouched original instructions'),
        ('lose-world-physical-frame', 'if (host == null || host.renderMode', 'if (host == null || data != null || host.renderMode',
         'admitted world pane uses logical physical bounds independent of desktop mono aspect'),
        ('infer-unadmitted-frame', 'Canvas? host = _expected.Value.AdmittedHost;', 'Canvas? host = _expected.Value.AdmittedHost ?? raycaster.GetComponent<Canvas>();',
         'unadmitted or native-remapped query retains its original mono viewport classification'),
        ('ignore-committed-overspill', 'Rect bounds = WorldUI.CanvasConversion.TryGetHitRect(host, out Rect committed) ? committed : rectangle.rect;',
         'Rect bounds = rectangle.rect;', 'committed interaction overspill remains reachable beyond authored host bounds'),
        ('constant-inside-viewport', 'return new Vector3((float)((x - bounds.xMin) / bounds.width), (float)((y - bounds.yMin) / bounds.height), original.z);',
         'return new Vector3(.5f,.5f,original.z);', 'logical viewport derives actual physical coordinates instead of a constant inside value'),
    ]
    cases = [(name, 'UguiRaycastCoordinates.cs', before, after, expected) for name, before, after, expected in cases]
    common_paths = []
    if args.common:
        common_spec = importlib.util.spec_from_file_location('common_inputs', fixture / 'common_sources.py')
        common = importlib.util.module_from_spec(common_spec); common_spec.loader.exec_module(common)
        common_sources, cases, common_paths = common.generated(source_root, (args.capture_source_root or source_root).resolve(), block)
        sources = {'UguiRaycastCoordinates.cs': helper_path.read_text(), **common_sources}
        cases.append(('retain-scroll-subfixture', 'fixture/Program.cs', 'Object.DestroyImmediate(list.gameObject);',
            'retainedScrollFixture = true;', 'fullscreen captured native click retains ordinary behavior'))
    if args.case:
        selected = set(args.case); assert selected <= {case[0] for case in cases}
        cases = [case for case in cases if case[0] in selected]
    fixture_inputs = [fixture / 'run.py', fixture / 'Coordinates.csproj', fixture / 'Editor/Runner.cs',
                      fixture / ('Common.cs' if args.common else 'Program.cs')]
    if args.common: fixture_inputs.append(fixture / 'common_sources.py')
    paths = [helper_path, pointer_path, ray_path, fit_path, code / 'VisibleUiSurface.cs', code / 'PokeOnlyTarget.cs',
        ROOT / 'scripts/ugui-scroll760-runtime/run.py', ROOT / 'scripts/town_run_retention.py',
        ROOT / 'scripts/menu-depth-character/project_compilers.py', *fixture_inputs, *common_paths, unity,
        game / 'UnityEngine.UI.dll', game / 'Unity.TextMeshPro.dll', Path('/home/claw/.dotnet/tools/ilspycmd')]
    if args.common: paths += [game / name for name in ['InControl.dll', 'Utilities.dll', 'Unity.InputSystem.dll']]
    pins = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths if path.is_file()}
    (run / 'source-pins.json').write_text(json.dumps({'sha256': pins, 'scope': __doc__, 'subset': args.case}, indent=2) + '\n')
    env = dict(os.environ, DOTNET_ROOT='/home/claw/.dotnet'); os.environ['GHVR_COORDINATES760_EVIDENCE'] = str(run)
    try:
        retained = run / 'Sources'; retained.mkdir(); shutil.copytree(fixture, retained / 'fixture')
        if args.common: shutil.copy2(fixture / 'Common.cs', retained / 'fixture/Program.cs')
        native = subprocess.run(['/home/claw/.dotnet/tools/ilspycmd', '-t', 'UnityEngine.UI.MultipleDisplayUtilities', str(game / 'UnityEngine.UI.dll')],
                                env=env, text=True, capture_output=True, check=True).stdout
        (run / 'MultipleDisplayUtilities.native.cs').write_text(native)
        graphic = subprocess.run(['/home/claw/.dotnet/tools/ilspycmd', '-t', 'UnityEngine.UI.GraphicRaycaster', str(game / 'UnityEngine.UI.dll')],
                                 env=env, text=True, capture_output=True, check=True).stdout
        (run / 'GraphicRaycaster.native.cs').write_text(graphic)
        if args.common:
            native_pointer = subprocess.run(['/home/claw/.dotnet/tools/ilspycmd', '-t', 'InControl.PointerInputModuleExtended', str(game / 'InControl.dll')],
                env=env, text=True, capture_output=True, check=True).stdout
            (run / 'PointerInputModuleExtended.native.cs').write_text(native_pointer)
        method = block(native, 'public static Vector3 RelativeMouseAtScaled(Vector2 position)')
        port = method.replace('Display.', 'NativeMouseDisplay.').replace('Screen.', 'NativeMouseScreen.')
        sources['NativeWindowedDisplayPort.cs'] = 'using UnityEngine;internal static class NativeWindowedDisplayPort {' + port + '}'
        for name, source in sources.items(): (retained / name).write_text(source)
        (retained / 'UguiPointer.full-source.txt').write_text(pointer); (retained / 'RayUguiDriver.full-source.txt').write_text(ray)
        assemblies = run / 'assemblies'; assemblies.mkdir()
        harmony_runtime = Path('/home/claw/.nuget/packages/lib.harmony/2.4.2/lib/net472/0Harmony.dll')
        manifest = {'result': str(run / 'results.txt'), 'harmony': str(harmony_runtime), 'gameManaged': str(game.resolve()), 'cases': []}
        for name, filename, before, after, expected in cases:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            case_fixture = retained / 'fixture'
            if filename == 'fixture/Program.cs':
                source = (case_fixture / 'Program.cs').read_text()
                assert before and source.count(before) == 1, (name, before)
                case_fixture = case / 'fixture'; case_fixture.mkdir()
                (case_fixture / 'Program.cs').write_text(source.replace(before, after, 1))
            for source_name, source in sources.items():
                if before and source_name == filename:
                    assert source.count(before) == 1, (name, before); source = source.replace(before, after, 1)
                (production / source_name).write_text(source)
            shutil.copy2(fixture / 'Coordinates.csproj', case / 'Coordinates.csproj'); assembly = 'UguiCoordinates760_' + name.replace('-', '_')
            command = ['/home/claw/.dotnet/dotnet', 'build', str(case / 'Coordinates.csproj'), '-c', 'Release', '-v', 'quiet', '--nologo',
                '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(case_fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(managed), '-p:GameManaged=' + str(game)]
            if args.common: command.append('-p:CommonInput=true')
            if execute(command, case / 'build.log'): raise RuntimeError('Compilation failed: ' + name)
            dll = assemblies / (assembly + '.dll'); shutil.copy2(case / 'bin/Release/net472' / dll.name, dll)
            shutil.copy2(case / 'obj/project.assets.json', case / 'resolved-build-assets.json')
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected,
                'fixture': str(case_fixture / 'Program.cs'),
                'fixture_sha256': hashlib.sha256((case_fixture / 'Program.cs').read_bytes()).hexdigest()})
        for directory in ['Assets/Editor', 'Packages', 'ProjectSettings']: (project / directory).mkdir(parents=True)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        dependencies = {'com.unity.ugui': '1.0.0', 'com.unity.textmeshpro': '3.0.6'}
        if args.common: dependencies['com.unity.inputsystem'] = '1.3.0'
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}) + '\n')
        if args.common:
            (project / 'ProjectSettings/TagManager.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!78 &1\nTagManager:\n  serializedVersion: 2\n  tags:\n  - UICamera\n  layers:\n  - Default\n  - TransparentFX\n  - Ignore Raycast\n  - \n  - Water\n  - UI\n')
        shutil.copy2(fixture / 'Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'assembly-pins.json').write_text(json.dumps({str(path): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in assemblies.glob('*.dll')}, indent=2) + '\n')
        status = execute(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod',
            'UguiCoordinates760Runner.Start', '-uguiCoordinates760', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')], run / 'launch.log', 300)
        actual_runtime = run / 'executed-runtime/UnityEngine.UI.dll'
        if actual_runtime.is_file():
            actual_native = subprocess.run(['/home/claw/.dotnet/tools/ilspycmd', '-t', 'UnityEngine.UI.MultipleDisplayUtilities', str(actual_runtime)],
                env=env, text=True, capture_output=True, check=True).stdout
            (run / 'MultipleDisplayUtilities.executed.cs').write_text(actual_native)
            equal = block(actual_native, 'public static Vector3 RelativeMouseAtScaled(Vector2 position)') == method
            (run / 'native-algorithm-binding.json').write_text(json.dumps({'editor_algorithm_equals_game': equal,
                'executed_ui_sha256': hashlib.sha256(actual_runtime.read_bytes()).hexdigest(),
                'original_ui_sha256': hashlib.sha256((game / 'UnityEngine.UI.dll').read_bytes()).hexdigest(),
                'port_changes': ['Display→explicit OS/display inputs', 'Screen→explicit fullscreen/windowed input'],
                'limit': 'Editor UI has a simpler display utility and no Physics blocking delegates; exact game utility body is independently replayed through its original call seam.'}, indent=2) + '\n')
        stable = all(Path(path).is_file() and hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in pins.items())
        if not stable: status = 1
        if (run / 'results.txt').is_file(): print((run / 'results.txt').read_text(), flush=True)
    finally:
        editors = cleanup_project_editors(project, unity, launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n'); assert not editors['remaining']
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n'); assert not compilers['remaining']
        receipt = cleanup_completed_run(suite, status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': status, 'source_stable': stable,
            'elapsed_seconds': time.monotonic() - started, 'cases': len(cases)}, indent=2) + '\n')
    return status


if __name__ == '__main__':
    def interrupted(signum, frame): raise KeyboardInterrupt('signal ' + str(signum))
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
