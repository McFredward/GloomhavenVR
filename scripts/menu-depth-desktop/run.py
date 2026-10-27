#!/usr/bin/env python3
"""Actual native ScreenSpaceCamera menu-depth capture and original glass GPU proof."""
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


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature); opening = source.index('{', start)
    level = 1; end = opening + 1
    while level:
        level += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end] + '\n'


def replace_once(source, before, after):
    assert source.count(before) == 1, before
    return source.replace(before, after, 1)


def execute(command, log, timeout=180):
    with log.open('w') as output:
        child = subprocess.Popen(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None: os.killpg(child.pid, signal.SIGKILL); child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'): parser.error('Use the task-private ADB server.')
    def terminated(signum, frame): raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, terminated)
    output = ROOT / '.planning/debug/menu-depth-desktop'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    source_root = args.source_root.resolve()
    flat = source_root / 'src/GloomhavenVR/WorldUI/FlatScreen'
    depth = source_root / 'src/GloomhavenVR/WorldUI/MenuDepth'
    core = (flat / 'FlatScreen.1.Core.cs').read_text(); stack = (flat / 'FlatScreen.2.CameraStack.cs').read_text()
    captured = core[core.index('    private sealed class CapturedCamera'):core.index('    // Placement anchors:')]
    body = 'using GloomhavenVR.Core;\nusing UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n' + captured
    for signature in ('    private void CaptureStack()\n', '    private void SelectBases()\n',
                      '    private void TickStackClears()\n', '    private void ReleaseStack()\n',
                      '    private void SetSplitRouting(', '    private void ClearUiRt()\n'):
        body += member(stack, signature)
    for signature in ('    private static bool IsUiCamera(', '    private RenderTexture? TargetFor('):
        start = stack.index(signature); body += stack[start:stack.index(';', start) + 1] + '\n'
    names = ('IMenuDepthSource.cs', 'MenuDepthHeadRaster.cs', 'MenuDepthHeadCarrier.cs', 'MenuDepthEye.cs', 'MenuDepthProjection.cs', 'MenuDepthScreenProjection.cs',
             'MenuDepthSources.cs', 'MenuDepthDisplay.cs', 'MenuDepthGraphic.cs', 'MenuDepthHeightfield.cs',
             'MenuDepthCharacters.cs', 'MenuDepthCharacterSource.cs', 'MenuDepthCharacterProjection.cs', 'MenuDepthNativeUiScope.cs', 'MenuDepthDesktopCarrier.cs', 'MenuDepthDesktopResources.cs')
    sources = {name: (depth / name).read_text() for name in names}
    # Rotation dispatch is proved by the dedicated real-input integration suite.
    # Rendering fixtures supply no pointer lifecycle; this explicit port keeps
    # discovery enrollment intact without importing gameplay-owned UI ancestry.
    sources['RotationBoundary730.cs'] = """using System; using UnityEngine.UI;
namespace GloomhavenVR.WorldUI.MenuDepth;
internal static class MenuDepthCharacterRotation
{
    internal static float GetYaw(RawImage image, object identity) => 0f;
    internal static void Bind(RawImage image, Character3DDisplayManager manager, Func<object?> readyIdentity) { }
    internal static void Unbind(RawImage image) { }
    internal static void Shutdown() { }
}
"""
    sources['CaptureStack.cs'] = body + '}\n'
    sources['PanelDepth.cs'] = (source_root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.6.MenuDepth.cs').read_text()
    sources['CaptureGuard.cs'] = (flat / 'FlatScreen.7.CaptureRender.cs').read_text()
    sources['DesktopCapture.cs'] = (flat / 'FlatScreen.8.MenuDepth.cs').read_text()
    budget_at = sources['DesktopCapture.cs'].index('MenuDepthHeadRaster.WindowBudget(')
    assert budget_at < sources['DesktopCapture.cs'].index('_menuDepthRight = MenuDepthTarget('), 'Budget must admit before either extra allocation'
    sources['Anchors724.cs'] = (ROOT / 'scripts/menu-depth-desktop/Anchors724.cs').read_text()
    # Explicit XR observation ports only. The original native camera's stored eye
    # matrices are real Unity inputs; the editor cannot report an active XR eye.
    sources['MenuDepthDisplay.cs'] = replace_once(sources['MenuDepthDisplay.cs'],
        'head == null || !head.stereoEnabled', 'head == null || !global::DesktopEyeInput.Available')
    sources['MenuDepthDisplay.cs'] = sources['MenuDepthDisplay.cs'].replace('camera.stereoActiveEye', 'global::DesktopEyeInput.Read(camera)')
    sources['DesktopCapture.cs'] = sources['DesktopCapture.cs'].replace(
        'camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right',
        'global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right')
    fixtures = ROOT / 'tests/GloomhavenVR.MenuDepthDesktopTests'
    inputs = [flat / name for name in ('FlatScreen.1.Core.cs', 'FlatScreen.2.CameraStack.cs', 'FlatScreen.7.CaptureRender.cs', 'FlatScreen.8.MenuDepth.cs')]
    inputs += [depth / name for name in names]
    inputs += [source_root / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle', ROOT / 'scripts/menu-depth-desktop/Anchors724.cs', Path(__file__), *fixtures.glob('*.cs'), *fixtures.glob('*.shader'), fixtures / 'MenuDepthDesktop.csproj', ROOT / 'scripts/menu-depth-desktop/Editor/Runner.cs']
    pins = [{'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for path in inputs]
    variants = [
        ('head-motion730', '', '', '', ''),
        ('head-raster4040', '', '', '', ''),
        ('head-resources730', '', '', '', ''),
        ('head-order730', '', '', '', ''),
        ('head-order-stale730','MenuDepthHeadCarrier.cs','        RefreshOrder();\n        float scale',
         '        /* stale constructor-time sorting */\n        float scale',
         'actual final head pixels follow native window focus sorting changes without reopening'),
        ('head-annotations730', '', '', '', ''),
        ('head-annotation-character-gated730','MenuDepthSources.cs',
         '&& graphic.color.a > 0f && !graphic.canvasRenderer.cull',
         '&& global::GloomhavenVR.WorldUI.WorldUIConfig.CharacterPreviewDepth.Value && graphic.color.a > 0f && !graphic.canvasRenderer.cull',
         'original outboard annotations admit both real head routes when character depth is off or not ready'),
        ('head-window-slots-lost730','MenuDepthHeadRaster.cs','WindowSlots = 8','WindowSlots = 16','the ninth real window is refused before allocating a ninth head pair'),
        ('head-scene-slots-lost730','MenuDepthHeadRaster.cs','SceneSlots = 4','SceneSlots = 8','the fifth real native preview cannot allocate a fifth head scratch'),
        ('head-normalization-lost730','MenuDepthHeadRaster.cs','original.msaaSamples = 1;','/* inherited MSAA survives */','head raster normalization removes inherited MSAA mips and dynamic scaling'),
        ('head-coplanar-side-noise730','MenuDepthCharacterSource.cs',
         'if (headRaster && Mathf.Abs(local.z) <= epsilon) local.z = 0f;',
         '/* injected: round-trip native units choose an arbitrary plane side */',
         'actual final head figure retains direct native-world XY surface detail during movement'),
        ('head-coplanar-lost730','MenuDepthScreenProjection.cs',
         '(allowCoplanar || Mathf.Abs(Vector3.Dot(center - nativeEye, forward)) > 1e-5f)',
         '(Mathf.Abs(Vector3.Dot(center - nativeEye, forward)) > 1e-5f)',
         'fixed head mapping admits the exact physical glass-plane crossing'),
        ('head-motion-downsample-lost730','MenuDepthHeadRaster.cs',
         'Mathf.CeilToInt(head.targetTexture.width * head.rect.width)',
         'Mathf.CeilToInt(head.targetTexture.width * head.rect.width * .25f)',
         'actual final head text and XY print retain direct native-world pixel detail during movement'),
        ('head-motion-second-eye-lost730','PanelDepth.cs',
         'e.MenuDepthDisplay?.PrepareHead(head);', '/* cached pair was not readmitted before second-eye culling */',
         'actual final head retains original window and figure ink for both eyes'),
        ('head-motion-source-detail-lost730','MenuDepthCharacterSource.cs',
         'int width = pixels.x, height = pixels.y;', 'int width = pixels.x / 4, height = pixels.y / 4;',
         'actual final head figure retains direct native-world XY surface detail during movement'),
        ('head-frustum729', '', '', '', ''),
        ('head-frustum-native-mapping-lost729', 'MenuDepthScreenProjection.cs',
         'new Vector3(1f / xScale, 1f / yScale, 1f / Mathf.Sqrt(xScale * yScale))) * nativeFrame;',
         'Vector3.one);', 'native frustum mapping recovers the actual physical viewer through anisotropic UI scale'),
        ('head-frustum-sampling-copy-lost729', 'MenuDepthSources.cs',
         'eye.HasWorldToHeadClip ? eye.WorldToHeadClip : null',
         'null', 'sampling-demand dispatch preserves the mapped physical frustum'),
        ('desktop-resources728', '', '', '', ''),
        ('desktop-budget-cap-lost728', 'MenuDepthDesktopResources.cs', '160L * 1024 * 1024', 'long.MaxValue', 'extreme native eye allocations are rejected before creating either additional target'),
        ('desktop-small-shrink-churn728', 'MenuDepthDesktopResources.cs', 'wanted.width <= current.width * .75f || wanted.height <= current.height * .75f',
         'wanted.width < current.width || wanted.height < current.height', 'small silhouette jitter never reallocates even after the old two-second timeout'),
        ('outer-carrier728', '', '', '', ''),
        ('outer-bounds-lost728', 'DesktopCapture.cs', 'MenuDepthSources.CollectReadyForegroundGraphics(_menuDepthBoundsGraphics);',
         '_menuDepthBoundsGraphics.Clear();', 'actual original character foreground expands beyond the entire old FlatScreen carrier'),
        ('outer-frozen-basis-lost728', 'MenuDepthNativeUiScope.cs', 'state.Freeze();', '/* native Canvas continues relayout under the enlarged camera */',
         'expanded GPU rendering retains original native root dimensions and edge-anchored controls during the capture itself'),
        ('native-depth-near-domain-lost728', 'MenuDepthNativeUiScope.cs',
         'camera.nearClipPlane = distance - halfSpan;\n            camera.farClipPlane = distance + halfSpan;',
         '/* native near/far domain unchanged */',
         'common UI depth retains foreground geometry in front of the original capture camera near plane'),
        ('native-depth-stack-clear-lost728', 'MenuDepthNativeUiScope.cs',
         'camera.clearFlags = CameraClearFlags.Nothing;', '/* native second camera clears prior depth */',
         'later UI cameras share foreground depth instead of clearing or reinterpreting the first native camera'),
        ('native-depth-rear-reflection-lost728', 'MenuDepthNativeUiScope.cs',
         'camera.worldToCameraMatrix = reflected;', '/* rear reflection omitted */',
         'rear depth places the original front witness behind native glass'),
        ('native-depth-restore-lost728', 'MenuDepthNativeUiScope.cs',
         '_camera.nearClipPlane = _near; _camera.farClipPlane = _far;', '/* clip restoration omitted */',
         'native depth scope restores automatic projection and original camera clears'),
        ('native-depth-probe728', '', '', '', ''),
        ('production', '', '', '', ''),
        ('window-anchors-724', '', '', '', ''),
        ('window-anchors-native-unit-pixels-725', 'MenuDepthScreenProjection.cs',
         'MenuDepthProjection.HeadPixels(eye, OnGlass(screen, original, bl), OnGlass(screen, original, tl),\n            OnGlass(screen, original, tr), OnGlass(screen, original, br))',
         'MenuDepthProjection.HeadPixels(eye, bl, tl, tr, br)',
         'the real original character eye targets grow from far to near displayed pixels within their bounded tier'),
        ('window-anchors-holder-fallback-725', 'MenuDepthCharacterSource.cs',
         'ReadOriginal?.Invoke(_manager) ?? _holder', '_holder',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-rear-rejected726', 'MenuDepthScreenProjection.cs',
         'Mathf.Abs(Vector3.Dot(center - nativeEye, forward)) > 1e-5f',
         'Vector3.Dot(center - nativeEye, forward) > 1e-5f',
         'the calibrated original renders both sampled eye targets from a mapped physical pixel demand'),
        ('window-anchors-native-image-effect', '', '', '', ''),
        ('window-anchors-ui-image-effect', '', '', '', ''),
        ('window-anchors-frozen-eye', 'MenuDepthScreenProjection.cs',
         'Vector3 offset = physicalEye - physicalCenter;', 'Vector3 offset = -screen.forward * 2f;',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-forced-full-viewport', 'MenuDepthScreenProjection.cs',
         'nativeEye = Vector3.zero;\n        nativeToPhysical = Matrix4x4.identity;\n        Rect viewport = original.rect;',
         'nativeEye = Vector3.zero;\n        nativeToPhysical = Matrix4x4.identity;\n        Rect viewport = new Rect(0f, 0f, 1f, 1f);',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-moving-native-scene', 'MenuDepthCharacterSource.cs',
         '            _camera.targetTexture = target;',
         '            _camera.targetTexture = target;\n            _camera.transform.SetPositionAndRotation(view.Position, view.Rotation);',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-wrong-eye', 'DesktopCapture.cs',
         '_glassMaterial.mainTexture = _menuDepthFrame == Time.frameCount\n            && global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right ? _menuDepthRight : _menuDepthCarrierLeft ?? _uiRt;',
         '_glassMaterial.mainTexture = _uiRt;',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('no-native-window-mapping', 'MenuDepthScreenProjection.cs',
         'nativeEye = center + right * (Vector3.Dot(offset, screen.right) * xScale)',
         'nativeEye = center + right * 0f',
         'original orthographic UI stack carries physical-eye-dependent graphic relief'),
        ('right-route-lost', 'CaptureGuard.cs',
         '        if (camera != null && ReferenceEquals(camera, _menuDepthBorrowedCamera)) return;',
         '        /* injected: ordinary capture rewrites a borrowed right target */',
         'both original native UI cameras populate the borrowed eye target without ordinary-route overwrite'),
        ('head-right-selection-lost', 'DesktopCapture.cs',
         '_glassMaterial.mainTexture = _menuDepthFrame == Time.frameCount\n            && global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right ? _menuDepthRight : _menuDepthCarrierLeft ?? _uiRt;',
         '_glassMaterial.mainTexture = _uiRt;',
         'actual head rendering selects the correct original glass eye before draw'),
        ('native-depth-projection-lost', 'MenuDepthGraphic.cs',
         'return MenuDepthProjection.ProjectToPlane(point, _eye);',
         'return point; /* injected: native orthographic UI never receives depth projection */',
         'original orthographic UI stack carries physical-eye-dependent graphic relief'),
    ]
    if args.case:
        wanted = set(args.case); assert wanted <= {case[0] for case in variants}
        variants = [case for case in variants if case[0] in wanted]
    status = 1
    try:
        project = run / 'unity'; editor = project / 'Assets/Editor'; editor.mkdir(parents=True)
        (project / 'ProjectSettings').mkdir(); (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages').mkdir(); (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        shutil.copyfile(ROOT / 'scripts/menu-depth-desktop/Editor/Runner.cs', editor / 'Runner.cs')
        for shader in fixtures.glob('*.shader'):
            shutil.copyfile(shader, project / 'Assets' / shader.name)
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'depthBundle': str(source_root / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle'), 'cases': []}
        (run / 'source-pins.json').write_text(json.dumps({'inputs': pins,
            'boundaries': 'Already-visible screen resources and native scene/manager geometry; actual screen-space Canvas, uGUI, native Camera.Render, original capture route/clears/guard/desktop mapper/registry/character and graphic production run. XR availability/active eye are supplied, real Unity stored stereo matrices remain.',
            'limits': 'No real XR/provider/headset, actual prefab art, native network or final HMD comfort proof.'}, indent=2) + '\n')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        for name, filename, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, code in sources.items():
                if source_name == filename:
                    if name == 'window-anchors-holder-fallback-725':
                        # Prime, actual capture and foreground bounds must all
                        # abandon the same original identity in this control.
                        assert code.count(before) == 3, (name, code.count(before))
                        code = code.replace(before, after)
                    else:
                        code = replace_once(code, before, after)
                (production / source_name).write_text(code)
            private_fixture = case / 'fixture'; private_fixture.mkdir()
            for path in fixtures.glob('*.cs'):
                code = path.read_text()
                if path.name == 'Boundary.cs':
                    code = code.replace('internal static void Warn(string scope, string text)',
                        'internal static void Debug(string scope, string text) => Messages.Add(text);\n        internal static void Warn(string scope, string text)')
                if path.name == 'Program.cs':
                    code = code.replace('public static class MenuDepthDesktopProgram', 'public static partial class MenuDepthDesktopProgram')
                (private_fixture / path.name).write_text(code)
            csproj = case / 'MenuDepthDesktop.csproj'; shutil.copyfile(fixtures / csproj.name, csproj)
            assembly = 'MenuDepthDesktop_' + name.replace('-', '_')
            command = ['/home/claw/.dotnet/dotnet', 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo',
                '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(private_fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed',
                '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed')]
            if execute(command, case / 'build.log'):
                raise RuntimeError('Compilation failure is not a causal-control pass: ' + name)
            dll = assemblies / (assembly + '.dll'); shutil.copyfile(case / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected,
                                      'method': ('HeadOrder730' if name in ('head-order730','head-order-stale730') else 'HeadAnnotations730' if name in ('head-annotations730','head-annotation-character-gated730') else 'HeadResources730' if name in ('head-resources730','head-window-slots-lost730','head-scene-slots-lost730','head-normalization-lost730') else 'HeadRaster4040' if name == 'head-raster4040' else 'HeadMotion730' if name.startswith('head-motion') or name.startswith('head-coplanar-') else 'HeadFrustum729' if name.startswith('head-frustum-') or name == 'head-frustum729' else
                                          'DesktopResources728' if name.startswith('desktop-') else 'OuterCarrier728' if name.startswith('outer-') else 'NativeDepthProbe728' if name.startswith('native-depth-') and name.endswith('728') else
                                          'AnchorsModelEffect724' if name == 'window-anchors-native-image-effect' else
                                          'AnchorsUiEffect724' if name == 'window-anchors-ui-image-effect' else
                                          'Anchors724' if name.startswith('window-anchors-') else 'Run')})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'command-environment.json').write_text(json.dumps({name: os.environ.get(name) for name in
            ('ADB_SERVER_SOCKET', 'ADB_MDNS_AUTO_CONNECT')}, indent=2) + '\n')
        status = execute(['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
            '-projectPath', str(project), '-executeMethod', 'MenuDepthDesktopRunner.Start', '-menuDepthDesktopManifest', str(run / 'manifest.json'),
            '-logFile', str(run / 'unity.log')], run / 'launch.log', 420)
        stable = all(hashlib.sha256(Path(pin['path']).read_bytes()).hexdigest() == pin['sha256'] for pin in pins)
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(pins)}) + '\n')
        if not stable: status = 1
        (run / 'unity-exit-code.txt').write_text(str(status) + '\n')
        if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
        if status: raise RuntimeError('Desktop capture proof failed: ' + str(run))
    finally:
        editor_dll = project / 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'
        if editor_dll.exists(): shutil.copyfile(editor_dll, run / 'assemblies/Assembly-CSharp-Editor.dll')
        (run / 'executed-pins.json').write_text(json.dumps({p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (run / 'assemblies').glob('*.dll')}, indent=2) + '\n')
        editors = cleanup_project_editors(run / 'unity', Path('/home/claw/unity-2021.3.5/Editor/Unity'), launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']: raise RuntimeError('Owned editor remains; retaining its project caches.')
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Private project compiler processes remain; retaining project caches: ' + str(run))
        receipt = cleanup_completed_run('menu-depth-desktop', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return status


if __name__ == '__main__': raise SystemExit(main())
