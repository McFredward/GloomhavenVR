#!/usr/bin/env python3
"""Exercise production desktop ownership with actual Unity 2021.3.5 cameras and callbacks.

Provider mode dispatch is source/API-bound because this editor has no attached OpenXR
headset. This proof does not claim final compositor pixels or headset acceptance.
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

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers
from town_run_retention import cleanup_completed_run


def execute(command, log, timeout):
    with log.open('w') as output:
        child = subprocess.Popen(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                try: os.killpg(child.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                child.wait()


def method(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    end = source.index('\n    }\n', start) + len('\n    }\n')
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/desktop-render-runtime')
    parser.add_argument('--case', action='append')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    args = parser.parse_args()
    args.source_root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    status = 1
    try:
        run_suite(args, run)
        status = 0
    finally:
        compilers = cleanup_project_compilers(run / 'unity', editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Private project compilers remain; retaining caches: ' + str(run))
        receipt = cleanup_completed_run('desktop-render-runtime', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


def run_suite(args, run):
    flat = args.source_root / 'src/GloomhavenVR/WorldUI/FlatScreen'
    desktop = (flat/'FlatScreen.3.Desktop.cs').read_text()
    core = (flat/'FlatScreen.1.Core.cs').read_text()
    lifecycle = (flat/'FlatScreen.4.Lifecycle.cs').read_text()
    stack = (flat/'FlatScreen.2.CameraStack.cs').read_text()
    fields = core[core.index('    private bool DesktopMirrorLeftEye'):core.index('    // ---- ITEM 1: hands')]
    end = method(lifecycle, '    public void OnEndOfFrame()\n')
    show = method(lifecycle, '    private void Show()\n')
    assert show.index('ReleaseDesktopScrub("flat screen capture begins");') < show.index('CaptureStack();'), 'Show must hand scrubbed cameras to capture in the same frame'
    assert 'SetCaptureRenderGuard(true);' in method(stack, '    private void CaptureStack()\n'), 'Visible native capture must own its final render guard'
    assert 'SetCaptureRenderGuard(false);' in method(stack, '    private void ReleaseStack()\n'), 'Native capture release must restore render guard ownership'
    assert 'Core.VRCameraPolicy.ExcludeStereo(cam, "screen capture");' in stack, 'Late native capture must not wait for the periodic stereo sweep'
    assert 'Graphics.Blit(_rt' not in end, 'Native menu must never composite to the spectator'
    assert 'UnityEngine.XR.XRMirrorViewBlitMode.None' in desktop and 'display.SetPreferredMirrorBlitMode(desired)' in desktop, 'Off must address the shipped OpenXR provider API'
    assert 'RenderPipelineManager.beginCameraRendering += OnScrubBeginCameraRendering' in desktop and 'RenderPipelineManager.endCameraRendering -= OnScrubEndCameraRendering' in desktop, 'SRP callback ownership must be paired and reversible'
    sources = {
        'Budget.cs': (args.source_root/'src/GloomhavenVR/Core/NativeCameraRenderBudget.cs').read_text(),
        'Cadence.cs': (args.source_root/'src/GloomhavenVR/WorldUI/Conversion/PanelMaintenanceCadence.cs').read_text(),
        'Desktop.cs': desktop,
        'Policy.cs': (flat/'FrameDesktopPolicy.cs').read_text(),
        'StereoPolicy.cs': (args.source_root/'src/GloomhavenVR/Core/VRCameraPolicy.cs').read_text(),
        'CaptureRender.cs': (flat/'FlatScreen.7.CaptureRender.cs').read_text(),
        'CullBoundary.cs': (args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentBudget.CameraBoundary.cs').read_text(),
        'Lifecycle.cs': 'using GloomhavenVR.Core;\nusing UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n'+fields+end+'}\n',
    }
    # Execute real discovery, classification, base selection, clear policy and
    # release. Only the heavyweight split/stereo construction is a named fixture
    # boundary; a hand-written capture stub cannot prove a first native render.
    captured_fields = core[core.index('    private sealed class CapturedCamera'):core.index('    // Placement anchors:')]
    stack_probe = 'using GloomhavenVR.Core;\nusing UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n' + captured_fields
    for signature in ('    private void CaptureStack()\n', '    private void SelectBases()\n',
                      '    private void TickStackClears()\n', '    private void ReleaseStack()\n'):
        stack_probe += method(stack, signature)
    start = stack.index('    private static bool IsUiCamera(')
    stack_probe += stack[start:stack.index(';', start)+1] + '\n'
    start = stack.index('    private RenderTexture? TargetFor(')
    stack_probe += stack[start:stack.index(';', start)+1] + '\n'
    sources['CaptureStack.cs'] = stack_probe + '}\n'
    fit = (args.source_root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs').read_text()
    probe = """using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace GloomhavenVR.WorldUI;
internal sealed class ConvertedPanel { internal RectTransform Target = null!, HostRect = null!; internal Transform? OriginalParent; internal GameObject? HostGo; internal Canvas? NativeMenuCanvas; internal RenderMode OriginalNativeMenuCanvasMode; }
internal static partial class CanvasConversion
{
private const float FitMinAlpha = .05f;
private enum MeasureReject { None, Culled, Faint, Empty, ClippedOut }
private static MeasureReject s_lastReject;
internal static int Reject => (int)s_lastReject;
private static readonly Vector3[] CornerScratch = new Vector3[4];
private static readonly Dictionary<Transform, RectTransform?> ClipperMemo = new(64);
private static readonly List<Transform> ClipperAncestorScratch = new(16);
private static readonly Dictionary<RectTransform, Rect> ClipperRectMemo = new(8);
private static readonly Dictionary<Transform, Vector2> AuthoredOffsetMemo = new(64);
internal static RectTransform? Find(ConvertedPanel panel, RectTransform graphic) => FindEnclosingClipper(panel,graphic);
internal static bool Measure(ConvertedPanel panel, Graphic graphic, out Vector2 min, out Vector2 max) => TryGetVisibleHostRect(panel,graphic,out min,out max,out _,out _);
"""
    for signature in ('    private static RectTransform? FindEnclosingClipper(', '    private static Vector2 AuthoredOffset(', '    private static void AuthoredHostRect(', '    internal static void BeginContentQuery()', '    private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g,\n        out Vector2 gMin, out Vector2 gMax, out Vector2 aMin, out Vector2 aMax,'):
        probe += method(fit, signature) + '\n'
    adopt = (args.source_root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.2.Adopt.cs').read_text()
    probe += method(adopt, '    private static Camera? FindGameUiCamera(Camera head)')
    probe += 'internal static Camera? OriginalUi(Camera head) => FindGameUiCamera(head);\n'
    sources['Fit.cs'] = probe + '}\n'
    conversion = args.source_root/'src/GloomhavenVR/WorldUI/Conversion'
    convert = (conversion/'CanvasConversion.1.Core.cs').read_text()
    release = (conversion/'CanvasConversion.4.Lifecycle.cs').read_text()
    start = convert.index('        Canvas? menuCanvas = capHeightToCanvas ?')
    end = convert.index('\n        AdoptNestedCanvases(panel);', start)
    handoff = convert[start:end]
    start = release.index('        if (panel.NativeMenuCanvas != null && NativeConversionHomeRestored(panel))')
    end = release.index('\n        RetainNativeMenuHome(panel);', start)
    mode_restore = release[start:end]
    assert convert.index('using var transaction = new ConversionTransaction(panel);') < convert.index(handoff)
    assert release.index('bool cameraWasWrong = RestoreAdoptedCameras("release");') < release.index(mode_restore)
    sources['NativeMenuCanvas.cs'] = 'using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class CanvasConversion {\n' + \
        'internal static void MenuFrame(ConvertedPanel panel, RectTransform target, RectTransform hostRect, Vector2 size, bool capHeightToCanvas) {\n' + handoff + '\n}\n' + \
        'internal static void RestoreMenuMode(ConvertedPanel panel) {\n' + mode_restore + '\n}\n' + \
        method(release, '    private static bool NativeConversionHomeRestored(ConvertedPanel panel)\n') + '\n}\n'
    movie = (args.source_root/'src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoWindow.cs').read_text()
    start = movie.index('    internal static bool OwnsRenderCamera(')
    owner = movie[start:movie.index(';', start)+1]
    sources['MovieOwner.cs'] = 'using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static class NativeVideoWindow {\n' + owner + '\n}\n'
    rig = (args.source_root/'src/GloomhavenVR/Rig/VRRigDriver.HeadCamera.cs').read_text()
    start = rig.index('    private Camera? ResolveScenarioCamera()')
    end = rig.index('    /// <summary>', start)
    resolver = rig[start:end].replace('private Camera? ResolveScenarioCamera()', 'internal Camera? ResolveScenarioCamera()', 1)
    sources['RigScenario.cs'] = ('using UnityEngine; using GloomhavenVR.Core; namespace GloomhavenVR.Rig; '
        + 'internal sealed class ScenarioCameraResolver { private Camera? _scenarioCam; private int _scenarioCamProbe; '
        + 'private const int SweepIntervalFrames = 16;\n' + resolver + '\n}\n')
    variants = [
        ('production', '', '', '', ''),
        ('native-menu-camera-mode-unpinned', 'NativeMenuCanvas.cs', '            menuCanvas.renderMode = RenderMode.WorldSpace;', '            /* injected: retain native head-driven camera adapter */', 'first menu head pixels already use the final native window frame'),
        ('native-menu-family-gate-lost', 'NativeMenuCanvas.cs', 'capHeightToCanvas ? target.GetComponent<Canvas>() : null', 'target.GetComponent<Canvas>()', 'ordinary converted windows retain their original canvas-mode policy'),
        ('native-menu-root-gate-lost', 'NativeMenuCanvas.cs', 'menuCanvas != null && menuCanvas.isRootCanvas', 'menuCanvas != null', 'nested native menu canvases never rewrite their original ancestor mode'),
        ('native-menu-mode-receipt-lost', 'NativeMenuCanvas.cs', 'panel.OriginalNativeMenuCanvasMode = menuCanvas.renderMode;', 'panel.OriginalNativeMenuCanvasMode = RenderMode.WorldSpace;', 'menu release restores the original camera adapter after native camera ownership'),
        ('native-menu-release-mode-lost', 'NativeMenuCanvas.cs', 'panel.NativeMenuCanvas.renderMode = panel.OriginalNativeMenuCanvasMode;', '/* injected: native camera mode never restored */', 'menu release restores the original camera adapter after native camera ownership'),
        ('native-menu-refused-home-lost', 'NativeMenuCanvas.cs', 'panel.NativeMenuCanvas != null && NativeConversionHomeRestored(panel)', 'panel.NativeMenuCanvas != null', 'refused native home retains the original menu canvas-mode receipt'),
        ('late-menu-discovery-lost', 'CaptureRender.cs', '            CaptureStack();', '            /* injected: first render bypasses native capture */', 'late native camera is captured before its first actual render'),
        ('late-menu-base-clear-lost', 'CaptureStack.cs', '                if (cam.backgroundColor != OpaqueBlack)\n                    cam.backgroundColor = OpaqueBlack;', '                /* injected: new base retains undefined native background */', 'first native render applies current opaque stack base clear'),
        ('late-menu-release-lost', 'CaptureStack.cs', '                cam.targetTexture = null;', '                { /* injected: first-render camera stays captured after release */ }', 'first-render capture releases exact camera and native clear state'),
        ('late-menu-head-census', 'CaptureRender.cs', 'camera == Rig.VRRigDriver.HeadCamera)', 'false)', 'head foreign preview and manual-disabled cameras never trigger first-render census'),
        ('late-menu-foreign-census', 'CaptureRender.cs', '|| camera.targetTexture != null)', '|| false)', 'head foreign preview and manual-disabled cameras never trigger first-render census'),
        ('late-menu-disabled-census', 'CaptureRender.cs', '|| !camera.isActiveAndEnabled ||', '|| false ||', 'head foreign preview and manual-disabled cameras never trigger first-render census'),
        ('late-menu-ui-classification-lost', 'CaptureStack.cs', 'IsUi = IsUiCamera(cam),', 'IsUi = false,', 'first late UI render joins transparent glass in the same render'),
        ('late-menu-stereo-sync-lost', 'CaptureStack.cs', '                        _stereo.SyncCamera(c.Camera);', '                        { /* injected: late camera omitted from stereo stack */ }', 'first late background camera synchronizes current stereo stack'),
        ('captured-menu-feedback', 'CaptureRender.cs', 'camera.cullingMask = mask & ~VRLayers.ModLayerMask;', 'camera.cullingMask = mask;', 'captured native menu cannot redraw the floating screen'),
        ('captured-target-write-leak', 'CaptureRender.cs', 'camera.targetTexture = target;', '{ /* injected: late native target reset survives */ }', 'render-time capture routing repairs a late native target reset'),
        ('captured-stereo-write-leak', 'CaptureRender.cs', 'VRCameraPolicy.ExcludeStereo(camera, "screen capture render");', '/* injected: late native stereo reset survives */', 'render-time capture excludes native stereo without waiting for a periodic sweep'),
        ('captured-mask-left-filtered', 'CaptureRender.cs', '_captureMaskedCamera.cullingMask = _captureMask;', '_captureMaskedCamera.cullingMask = 0;', 'native current mask is restored after each captured render'),
        ('captured-final-seam-lost', 'CaptureRender.cs', 'ScenarioCameraCullBoundary.Subscribe(OnCapturePreCull);', 'Camera.onPreCull += OnCapturePreCull;', 'render-time capture routing repairs a late native target reset'),
        ('captured-stereo-log-flood', 'StereoPolicy.cs', '        if (first)\n        {\n            Camera? head = AllowedHead;', '        if (true)\n        {\n            Camera? head = AllowedHead;', 'repeated native stereo corrections keep normal logging bounded'),
        ('camera-not-disabled', 'Budget.cs', 'if (camera.enabled) camera.enabled = false;', 'if (camera.enabled) camera.enabled = true;', 'unused native and UI cameras leave Unity'),
        ('native-projection-lost', 'Budget.cs', 'return _main;', 'return Camera.main;', 'projection resolver preserves exact original camera identity'),
        ('scenario-mask-lookup-lost', 'RigScenario.cs', 'int count = NativeCameraRenderBudget.GetProjectionCamerasNonAlloc(out Camera[] all);', 'Camera[] all = Camera.allCameras; int count = all.Length;', 'scenario mask lookup retains the suspended original camera'),
        ('projection-enumeration-lost', 'Budget.cs', 'return count;\n    }\n    private static Camera[] ProjectionCameras', 'return active;\n    }\n    private static Camera[] ProjectionCameras', 'original UI camera recovery retains suspended native identities'),
        ('movie-camera-suspended', 'Budget.cs', 'camera == Rig.VRRigDriver.HeadCamera || WorldUI.NativeVideoWindow.OwnsRenderCamera(camera) ||', 'camera == Rig.VRRigDriver.HeadCamera ||', 'native movie decoder and continuation camera remain enabled'),
        ('cadence-keeps-polling', 'Cadence.cs', 'bool due = interval <= 0f || immediate || changed || now >= state.Next;', 'bool due = true;', 'static panel avoids redundant fit polling'),
        ('cadence-misses-immediate', 'Cadence.cs', 'interval <= 0f || immediate || changed', 'interval <= 0f || changed', 'reveal or manual motion bypasses static cadence immediately'),
        ('cadence-misses-root-change', 'Cadence.cs', 'interval <= 0f || immediate || changed', 'interval <= 0f || immediate', 'native root geometry change bypasses cadence in the same frame'),
        ('mirror-off-draw-leak', 'Policy.cs', 'vrRunning && !flatScreenVisible;', 'mirrorLeftEye && vrRunning && !flatScreenVisible;', 'mirror off still redirects discarded native rendering'),
        ('native-skybox-draw-leak', 'Desktop.cs', 'if (_maskedSkybox)\n            cam.clearFlags = CameraClearFlags.SolidColor;', 'if (_maskedSkybox && false)\n            cam.clearFlags = CameraClearFlags.SolidColor;', 'unused native skybox draw is suppressed within the same real render callback pair'),
        ('native-mask-left-zero', 'Desktop.cs', '_maskedCam.cullingMask = _maskedValue;', '_maskedCam.cullingMask = 0;', 'actual built-in Camera.Render skips draw and restores within its own callback pair'),
        ('head-camera-scrubbed', 'Desktop.cs', 'Camera? head = Rig.VRRigDriver.HeadCamera;', 'Camera? head = null;', 'head eyes and native preview capture stay outside the scrub'),
        ('foreign-target-scrubbed', 'Desktop.cs', 'if (cam.targetTexture != null)\n                continue;', 'if (cam.targetTexture != null && false)\n                continue;', 'head eyes and native preview capture stay outside the scrub'),
        ('menu-draw-skipped', 'Policy.cs', 'vrRunning && !flatScreenVisible;', 'vrRunning;', 'visible headset menu restores scrub ownership before native capture'),
        ('missing-pre-render-hook', 'Desktop.cs', 'Camera.onPreCull += OnScrubPreCull;', '/* injected: no native pre-cull registration */', 'actual built-in Camera.Render skips draw and restores within its own callback pair'),
        ('clipper-memo-not-cleared', 'Fit.cs', '        ClipperMemo.Clear();', '        /* injected: stale ancestor memo */', 'next pass observes live disabled nearest mask'),
        ('clipper-bounds-not-cleared', 'Fit.cs', '        ClipperRectMemo.Clear();', '        /* injected: stale clip bounds */', 'next pass observes animated viewport growth'),
        ('host-projection-identity', 'Fit.cs', 'Matrix4x4 toHost = panel.HostRect.worldToLocalMatrix;', 'Matrix4x4 toHost = Matrix4x4.identity;', 'transformed host produces the same local graphic corners'),
        ('black-target-not-restored', 'Lifecycle.cs', 'RenderTexture.active = previous;', 'RenderTexture.active = null;', 'black desktop clear restores the previous in-headset capture target'),
    ]
    if args.case:
        selected = set(args.case)
        if selected - {v[0] for v in variants}: parser.error('Unknown variant')
        variants = [v for v in variants if v[0] in selected]
    fixture = ROOT/'scripts/desktop-render-runtime'
    source_files = [flat/name for name in ('FlatScreen.1.Core.cs', 'FlatScreen.2.CameraStack.cs',
        'FlatScreen.3.Desktop.cs', 'FlatScreen.4.Lifecycle.cs', 'FlatScreen.7.CaptureRender.cs',
        'FrameDesktopPolicy.cs', 'NativeVideoWindow.cs')]
    source_files += [args.source_root/'src/GloomhavenVR'/name for name in (
        'Core/NativeCameraRenderBudget.cs', 'Core/VRCameraPolicy.cs',
        'Core/Perf/ScenarioEnvironmentBudget.CameraBoundary.cs', 'Rig/VRRigDriver.HeadCamera.cs',
        'WorldUI/Conversion/PanelMaintenanceCadence.cs', 'WorldUI/Conversion/CanvasConversion.3.Fit.cs',
        'WorldUI/Conversion/CanvasConversion.2.Adopt.cs', 'WorldUI/Conversion/CanvasConversion.1.Core.cs',
        'WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs', 'WorldUI/Conversion/ConvertedPanel.cs')]
    input_files = source_files + [Path(__file__).resolve(), ROOT/'scripts/menu-depth-character/project_compilers.py'] + sorted(fixture.rglob('*.cs')) + [fixture/'Desktop.csproj']
    input_hashes = {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in input_files}
    for path in input_files:
        # Freeze actual source/fixture inputs before any caches can be removed.
        kind = 'production' if path in source_files else 'fixture'
        base = args.source_root if path in source_files else ROOT
        target = run/'source'/kind/path.relative_to(base)
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(path, target)
    manifest = {'result': str(run/'results.txt'), 'managed': str((args.source_root/'ressources/GH_Data/Managed').resolve()), 'cases': []}
    (run/'source-hashes.json').write_text(json.dumps({'root':str(args.source_root.resolve()),'sha256':{key:hashlib.sha256(value.encode()).hexdigest() for key,value in sources.items()},'inputs': input_hashes,'limits':['No OpenXR device is attached; provider presentation remains hardware-open.','Actual Camera.Render callbacks, ownership and current RenderTexture are executed.','Split/stereo construction is an explicit fixture boundary; same-pass sync calls and native glass pixels execute, not headset stereo presentation.','This ordinary-menu fixture has no menu-depth sources or borrowed eye camera; the separate menu-depth-desktop suite executes the spatial capture path.']},indent=2)+'\n')
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    assemblies = run/'assemblies'; assemblies.mkdir()
    for name, filename, before, after, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        for path, value in sources.items():
            if path == filename:
                assert value.count(before) == 1, 'mutation binding drift: '+name
                value = value.replace(before, after, 1)
            (production/path).write_text(value)
        project = build/'Desktop.csproj'; shutil.copyfile(fixture/'Desktop.csproj',project)
        assembly = 'DesktopRender_'+name.replace('-','_')
        command = [dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet',
            '--disable-build-servers','-nr:false','-p:UseSharedCompilation=false',
            '-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),
            '-p:UnityManaged='+str(args.unity.parent/'Data/Managed'),
            '-p:UnityUi='+str(args.source_root/'ressources/GH_Data/Managed/UnityEngine.UI.dll')]
        (build/'command.json').write_text(json.dumps(command, indent=2) + '\n')
        if execute(command, build/'build.log', 180):
            raise SystemExit((build/'build.log').read_text()+'\nCompilation failure is not a passing negative control')
        retained = assemblies/(assembly+'.dll')
        shutil.copyfile(build/'bin/Release/netstandard2.1'/retained.name, retained)
        manifest['cases'].append({'name':name,'dll':str(retained),'expected':expected})
    dependencies = run/'dependencies'; dependencies.mkdir()
    for dependency in (run/variants[0][0]/'bin/Release/netstandard2.1').glob('*.dll'):
        if not dependency.name.startswith('DesktopRender_'): shutil.copyfile(dependency, dependencies/dependency.name)
    manifest['dependencies'] = str(dependencies)
    manifest_path=run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2))
    project=run/'unity'; (project/'Assets/Editor').mkdir(parents=True); (project/'Packages').mkdir(); (project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/InteractionRunner.cs', project/'Assets/Editor/InteractionRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    # Camera.Render must use a real graphics device; a -nographics run never sends the
    # rendering callbacks and therefore cannot establish the ownership contract.
    command = [str(args.unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','InteractionRunner.Start','-interactionManifest',str(manifest_path),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run','-a'] + command
    (run/'unity-command.json').write_text(json.dumps(command, indent=2) + '\n')
    unity_code = execute(command, run/'launch.log', 240)
    report=Path(manifest['result'])
    if report.is_file(): print(report.read_text(),end='')
    (run/'unity-exit-code.txt').write_text(str(unity_code)+'\n')
    if unity_code or not report.is_file(): raise SystemExit('FAIL: Unity run; see '+str(run/'unity.log'))
    stable = all(path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == input_hashes[str(path.resolve())]
                 for path in input_files)
    (run/'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(input_files)}, indent=2)+'\n')
    if not stable: raise SystemExit('FAIL: source or fixture changed during run; frozen evidence required')
    print('PASS: '+str(len(variants))+' complete production/negative variants; evidence: '+str(run))

if __name__ == '__main__': main()
