#!/usr/bin/env python3
"""Source-bound native character portal/lease proof on actual Unity GPU and UI.

Native scene content/manager state and XR eye positions are named fixture inputs.
Discovery, registration, projection, native Camera.Render/image-effect callbacks,
uGUI texture/mask/alpha sampling and camera/texture restoration run production.
This does not establish native game art, Beautify visual parity or XR/headset comfort.
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
from town_run_retention import cleanup_completed_run
from project_compilers import cleanup_project_compilers, cleanup_project_editors


def execute(argv, log, timeout=180):
    with log.open('w') as output:
        child = subprocess.Popen(argv, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
                child.wait()


def replace_once(code, before, after):
    assert code.count(before) == 1, before
    return code.replace(before, after, 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append')
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--editor-mode', action='store_true', default=False, help='Optional direct editor Camera.Render; supplied effect matches native ExecuteInEditMode.')
    mode.add_argument('--play-mode', dest='editor_mode', action='store_false', help='Default: original Playmode callback execution.')
    parser.add_argument('--api-root', type=Path, default=ROOT,
        help='Read the unchanged shared capture contract from the integration checkout while testing private character sources.')
    parser.add_argument('--discovery-root', type=Path, default=ROOT,
        help='Read frozen character discovery/observer production from a parallel integration checkout.')
    parser.add_argument('--character-root', type=Path, default=ROOT,
        help='Read frozen native character Source/Projection production without modifying the owning worker checkout.')
    parser.add_argument('--native-scope-root', type=Path, default=None, help='Read the native UI depth scope from its isolated owner checkout.')
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    args = parser.parse_args()
    output = ROOT / '.planning/debug/menu-depth-character'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    print('Evidence: ' + str(run), flush=True)
    (run / 'launch-environment.json').write_text(json.dumps({key: os.environ.get(key)
        for key in ('ADB_SERVER_SOCKET', 'ADB_MDNS_AUTO_CONNECT')}, indent=2) + '\n')
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    code_root = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth'
    names = ('IMenuDepthSource.cs', 'MenuDepthHeadRaster.cs', 'MenuDepthHeadCarrier.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs', 'MenuDepthProjection.cs', 'MenuDepthScreenProjection.cs', 'MenuDepthDisplay.cs',
             'MenuDepthCharacters.cs', 'MenuDepthCharacterSource.cs', 'MenuDepthCharacterProjection.cs',
             'MenuDepthCharacterDisplayObserver.cs')
    api_names = ('IMenuDepthSource.cs', 'MenuDepthHeadRaster.cs', 'MenuDepthHeadCarrier.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs', 'MenuDepthProjection.cs', 'MenuDepthScreenProjection.cs', 'MenuDepthDisplay.cs')
    discovery_names = ('MenuDepthCharacters.cs', 'MenuDepthCharacterDisplayObserver.cs')
    character_names = ('MenuDepthCharacterSource.cs', 'MenuDepthCharacterProjection.cs')
    inputs = {name: (args.api_root / 'src/GloomhavenVR/WorldUI/MenuDepth' / name) if name in api_names
              else (args.discovery_root / 'src/GloomhavenVR/WorldUI/MenuDepth' / name) if name in discovery_names
              else (args.character_root / 'src/GloomhavenVR/WorldUI/MenuDepth' / name) if name in character_names
              else code_root / name for name in names}
    scope_root = args.native_scope_root or args.api_root
    inputs['MenuDepthNativeUiScope.cs'] = scope_root / 'src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthNativeUiScope.cs'
    sources = {name: path.read_text() for name, path in inputs.items()}
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
    fixture = ROOT / 'tests/GloomhavenVR.MenuDepthCharacterTests'
    pins = {str(path): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in [*inputs.values(), *fixture.glob('*'), Path(__file__),
                         ROOT / 'scripts/menu-depth-character/Editor/Runner.cs', Path(__file__).with_name('project_compilers.py'),
                         ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle'] if path.is_file()}
    variants = [
        ('bounds-cost729', '', '', '', 'BoundsCost729', ''),
        ('bounds-fast-path-lost729', 'MenuDepthCharacterSource.cs',
         'if (cuttingPlanes == 0)', 'if (cuttingPlanes < 0)', 'BoundsCost729',
         'fully visible native bounds use the eight-corner fast path without polygon scratch'),
        ('motion729', '', '', '', 'Motion729', ''),
        ('far-accessory-budget729', '', '', '', 'FarAccessory729', ''),
        ('motion-offscreen-bounds729', 'MenuDepthCharacterSource.cs',
         'if (!visible) return;', 'if (visible) return;', 'Motion729',
         'actual motion rasters retain the independently authored spatial detail'),
        ('motion-crop-capacity729', 'MenuDepthCharacterSource.cs',
         '        if (!shrink && _width > 0)\n        { width = Math.Max(width, _width); height = Math.Max(height, _height); }',
         '        shrink = true; /* injected: crop fluctuations release warm capacity */', 'Motion729',
         'continuous ordinary head motion preserves the warmed native eye target identities'),

        ('native-softmask728', '', '', '', 'NativeSoftMask728', ''),
        ('native-softmask-occlusion728', '', '', '', 'NativeSoftMaskOcclusion728', ''),
        ('native-softmask-ignored728', 'MenuDepthCharacterSource.cs',
         '_material.SetFloat("_GvrNativeSoftMask", nativeSoftMask ? 1f : 0f);',
         '_material.SetFloat("_GvrNativeSoftMask", 0f);', 'NativeSoftMask728',
         'the exact native softmask RGBA blend survives transparent partial and opaque UI destinations'),
        ('native-softmask-alpha-linear728', 'MenuDepthCharacterSource.cs',
         '_material.SetFloat("_GvrCharacterSrcAlphaBlend", nativeSoftMask ? 5f : 1f);',
         '_material.SetFloat("_GvrCharacterSrcAlphaBlend", 1f);', 'NativeSoftMask728',
         'the exact native softmask RGBA blend survives transparent partial and opaque UI destinations'),
        ('depth-attachment728', '', '', '', 'DepthAttachment728', ''),
        ('depth-occlusion728', '', '', '', 'Occlusion728', ''),
        ('depth-overflow728', '', '', '', 'Overflow728', ''),
        ('depth-cross-panel728', '', '', '', 'CrossPanel728', ''),
        ('depth-colour-instead-of-depth728', 'MenuDepthCharacterSource.cs',
         '_material.SetTexture("_NativeDepth", _depth!, UnityEngine.Rendering.RenderTextureSubElement.Depth);',
         '_material.SetTexture("_NativeDepth", _depth!);', 'Occlusion728',
         'actual foreground model pixels survive later native UI and the old preview aperture'),
        ('depth-writes-lost728', 'MenuDepthCharacterSource.cs',
         '_material.SetFloat("_GvrCharacterDepthWrite", _capturing ? 1f : 0f);',
         '_material.SetFloat("_GvrCharacterDepthWrite", 0f);', 'Occlusion728',
         'actual foreground model pixels survive later native UI and the old preview aperture'),
        ('depth-whole-frame-crop-lost728', 'MenuDepthDisplay.cs',
         'Rect sampledFrame = captureFrame ?? ForegroundFrame(plane, frame, leftEye, rightEye);',
         'Rect sampledFrame = frame;', 'Overflow728',
         'actual foreground survives outside the entire original window capture and final carrier'),
        ('depth-head-transport-lost728', 'MenuDepthDisplay.cs',
         'material.SetFloat("_GvrCaptureDepthActive", 1f);',
         'material.SetFloat("_GvrCaptureDepthActive", 0f);', 'CrossPanel728',
         'a separately drawn native UI panel behind the real foreground cannot overwrite it after the final carrier'),
        ('depth-byte-budget-lost728', 'MenuDepthCharacterSource.cs',
         'long depth = 4L;', 'long depth = 0L;', 'Presentation',
         'simultaneous actual eye pairs stay inside the shared96MiB budget'),
        ('character-preference727', '', '', '', 'Preference', ''),
        ('character-preference-ignored727', 'MenuDepthCharacters.cs',
         '!VRSession.IsRunning || !WorldUIConfig.CharacterPreviewDepth.Value', '!VRSession.IsRunning', 'Preference',
         'disabled character preference keeps original live colour without scene discovery'),
        ('character-preference-retirement-lost727', 'MenuDepthCharacters.cs',
         'if (!VRSession.IsRunning || !WorldUIConfig.CharacterPreviewDepth.Value) { Shutdown(); return; }',
         'if (!WorldUIConfig.CharacterPreviewDepth.Value) return;\n        if (!VRSession.IsRunning) { Shutdown(); return; }', 'Preference',
         'disabling character preference retires an existing eye registration before its next capture'),
        ('automatic-head725', '', '', '', 'AutomaticHead', ''),
        ('head-coplanar-mask-noise730','MenuDepthCharacterSource.cs',
         'if (_headRaster) nativePlane.w += (_view.Reversed ? -1f : 1f) * .0001f;',
         '/* exact native plane depth is classified by floating point noise */','AutomaticHead',
         'automatic native stencil and rectangle masks clip the original preview'),
        ('automatic-head-failure725', 'MenuDepthCharacterSource.cs', '            _camera.Render();',
         '            throw new InvalidOperationException("injected automatic head failure");', 'AutomaticHeadFailure', ''),
        ('late-head-texture-loan725', 'MenuDepthSources.cs',
         'Canvas.preWillRenderCanvases += PrimeDirectSources;', '/* injected: Canvas batches retain the native mono sampler */', 'AutomaticHead',
         'automatic head final raster contains both native preview landmarks'),
        ('direct-native-alpha-lost725', 'MenuDepthCharacterSource.cs',
         '_image.texture = target;', '_image.color = Color.white; _image.texture = target;', 'AutomaticHead',
         'automatic native UI retains its original colour alpha and ancestor fade'),
        ('direct-native-masks-lost725', 'MenuDepthCharacterSource.cs',
         '_image.texture = target;', '_image.maskable = false; _image.texture = target;', 'AutomaticHead',
         'automatic native stencil and rectangle masks clip the original preview'),
        ('orphan-direct-prime725', 'MenuDepthSources.cs',
         'if (_cleaningDirect || (onlyIfStale && _directPrimeFrame == Time.frameCount)) return;',
         'if (_cleaningDirect || Time.frameCount >= 0) return;', 'AutomaticHead',
         'actual next Unity frame restores orphaned texture primes when no head draw occurs'),
        ('presentation725', '', '', '', 'Presentation', ''),
        ('presentation725-native-ldr-effect', '', '', '', 'NativeLdrEffect', ''),
        ('fixed-native-samples725', 'MenuDepthCharacterSource.cs',
         'SelectResolution(eye.PixelFootprint);', 'SelectResolution(Vector2.zero);', 'Presentation',
         'near mid and far actual head rasters preserve the independently authored spatial frequency'),
        ('old-deep-framing725', 'MenuDepthCharacterProjection.cs',
         'Vector3 croppedCenter = source.transform.TransformPoint(stableMainOrigin) + up * (CommonSpan * .5f);',
         'Vector3 croppedCenter = source.transform.TransformPoint(stableMainOrigin) + up * (CommonSpan * .5f) + forward;', 'Presentation',
         'actual rendered landmarks occupy the independently specified common native-unit cabinet'),
        ('camera-child-move725', 'MenuDepthCharacterSource.cs',
         '            _camera.orthographic = false;',
         '            _camera.transform.SetPositionAndRotation(view.Position, view.Rotation);\n            _camera.orthographic = false;', 'Presentation',
         'actual rendered landmarks occupy the independently specified common native-unit cabinet'),
        ('completed-model-membership725', 'MenuDepthCharacterSource.cs',
         'if (!_calibration.TryGet(identity, out Vector3 origin))',
         'if (!_calibration.TryGet(_holder, out Vector3 origin))', 'Presentation',
         'actual rendered landmarks occupy the independently specified common native-unit cabinet'),
        ('common-cabinet726', '', '', '', 'CommonCabinet726', ''),
        ('desktop-cabinet726', '', '', '', 'DesktopCabinet726', ''),
        ('rear-throwing-render726', 'MenuDepthCharacterSource.cs', '            _camera.Render();',
         '            throw new InvalidOperationException("injected rear native render failure");', 'RearThrow726', ''),
        ('per-character-scale726', 'MenuDepthCharacterProjection.cs',
         'float nativePerUnit = Mathf.Max(CommonSpan / (physicalWidth * .82f), CommonSpan / (physicalHeight * .75f));',
         'float nativePerUnit = Mathf.Max(CommonSpan / (physicalWidth * .82f), CommonSpan / (physicalHeight * .75f)) * (stableMainOrigin.y < -.8f ? 1.7f : 1f);', 'CommonCabinet726',
         'actual front and rear body pixels retain the fixed common main-actor axis'),
        ('companion-centered726', 'MenuDepthCharacterSource.cs',
         'main = actor != null ? actor.transform : null;\n                        break;',
         'main = actor != null ? actor.transform : null;\n                        /* injected: last companion replaces the main axis */', 'CommonCabinet726',
         'actual front and rear body pixels retain the fixed common main-actor axis'),
        ('depth-squash726', 'MenuDepthCharacterProjection.cs',
         'forward * Vector3.Dot(relativeEye, physicalForward)) * nativePerUnit;',
         'forward * Vector3.Dot(relativeEye, physicalForward) * 2f) * nativePerUnit;', 'CommonCabinet726',
         'actual front and rear body pixels retain the fixed common main-actor axis'),
        ('rear-rejected726', 'MenuDepthCharacterProjection.cs',
         'Mathf.Abs(signedDistance) <= 1e-5f', 'signedDistance <= 1e-5f', 'CommonCabinet726',
         'Original character preview has no valid window portal.'),
        ('rear-culling-lost726', 'MenuDepthCharacterSource.cs',
         'GL.invertCulling = originalCullingInverted ^ view.Reflected;',
         'GL.invertCulling = originalCullingInverted;', 'CommonCabinet726',
         'actual native front or rear body surface is visible'),
        ('rear-culling-restore-lost726', 'MenuDepthCharacterSource.cs',
         '            GL.invertCulling = originalCullingInverted;\n            if (_camera != null)',
         '            /* injected: reflected rear winding leaks into the final UI draw */\n            if (_camera != null)', 'CommonCabinet726',
         'actual native front or rear body surface is visible'),
        ('unbounded-eye-pool725', 'MenuDepthCharacterSource.cs',
         'MaxPoolBytes = 96L', 'MaxPoolBytes = 192L', 'Presentation',
         'simultaneous actual eye pairs stay inside the shared96MiB budget'),
        ('allocation-hysteresis-lost725', 'MenuDepthCharacterSource.cs',
         '        if (!shrink && _width > 0)\n        { width = Math.Max(width, _width); height = Math.Max(height, _height); }',
         '        shrink = true; /* injected: ordinary motion discards warm capacity */', 'Motion729',
         'continuous ordinary head motion preserves the warmed native eye target identities'),
        ('production', '', '', '', 'Run', ''),
        ('glass-cabinet', '', '', '', 'GlassCabinet', ''),
        ('native-ldr-viewports', '', '', '', 'TargetViewport', ''),
        ('camera-child-head-follow', 'MenuDepthCharacterSource.cs',
         '            _camera.orthographic = false;',
         '            _camera.transform.SetPositionAndRotation(view.Position, view.Rotation);\n            _camera.orthographic = false;', 'GlassCabinet',
         'actual eye raster projects fixed cabinet landmarks from independently authored window-relative 3D'),
        ('native-ldr-viewport-ignored', 'MenuDepthCharacterProjection.cs',
         'Rect viewport = source.rect;',
         'Rect viewport = new Rect(0f, 0f, 1f, 1f);', 'TargetViewport',
         'actual eye raster projects fixed cabinet landmarks from independently authored window-relative 3D'),
        ('native-lifecycle', '', '', '', 'Lifecycle', ''),
        ('native-close-during-render', '', '', '', 'NativeCloseDuringRender', ''),
        ('destroyed-surface736', '', '', '', 'DestroyedSurface736', ''),
        ('destroyed-retire-guard-lost736', 'MenuDepthCharacterSource.cs',
         '        if (this == null) return;', '        /* destroyed component enters native graphic getter */', 'DestroyedSurface736',
         'destroyed native preview effects retire without accessing their destroyed Unity component'),
        ('throwing-render', 'MenuDepthCharacterSource.cs', '            _camera.Render();',
         '            throw new InvalidOperationException("injected native render failure");', 'ThrowingRender', ''),
        ('fixed-mono-view', 'MenuDepthCharacterProjection.cs', 'Vector3 relativeEye = eye - physicalCenter;',
         'Vector3 relativeEye = -physicalForward * (.5f * Mathf.Sqrt(physicalWidth * physicalHeight * source.projectionMatrix.m00 * source.projectionMatrix.m11));', 'Run',
         'each recorded eye renders the native model from its own physical window-relative viewpoint'),
        ('world-axis-window', 'MenuDepthCharacterProjection.cs', 'physicalRight.Normalize(); physicalUp.Normalize();',
         'physicalRight = Vector3.right; physicalUp = Vector3.up;', 'Run',
         'moved rotated windows derive the same local view independently of their different world poses'),
        ('lost-native-crop', 'MenuDepthCharacterProjection.cs', 'Rect rect = rectangle.rect;',
         'uv = new Rect(0, 0, 1, 1); Rect rect = rectangle.rect;', 'Run',
         'native RawImage UV cropping preserves the fitted model and companion'),
        ('texture-restore-lost', 'MenuDepthCharacterSource.cs', '            _image.texture = _restoreTexture;',
         '            /* injected: original UI keeps the borrowed eye texture */', 'Run',
         'original RawImage texture is restored after private UI capture'),
        ('automatic-view-frozen', 'MenuDepthCharacterSource.cs',
         'if (generatedView) _camera.ResetWorldToCameraMatrix(); else _camera.worldToCameraMatrix = originalView;',
         '_camera.worldToCameraMatrix = originalView;', 'Run',
         'restoring an automatic camera keeps subsequent native pose changes automatic'),
        ('automatic-projection-frozen', 'MenuDepthCharacterSource.cs',
         'if (generatedProjection) _camera.ResetProjectionMatrix(); else _camera.projectionMatrix = originalProjection;',
         '_camera.projectionMatrix = originalProjection;', 'Run',
         'restoring an automatic camera keeps subsequent native FOV changes automatic'),
        ('native-hidden-ignored', 'MenuDepthCharacterSource.cs',
         '|| _readHidden(_manager)) return null;',
         ') return null;', 'Run',
         'original native hide permission suppresses depth without calling native continuation'),
        ('inactive-consumer-omitted', 'MenuDepthCharacters.cs',
         'Object.FindObjectsOfType<RawImage>(true)', 'Object.FindObjectsOfType<RawImage>()', 'Lifecycle',
         'pooled inactive original consumer and disabled native manager are bound before first visibility'),
        ('inactive-camera-omitted', 'MenuDepthCharacters.cs',
         'Object.FindObjectsOfType<Character3DDisplayManager>(true)', 'Object.FindObjectsOfType<Character3DDisplayManager>()', 'Lifecycle',
         'pooled inactive original consumer and disabled native manager are bound before first visibility'),
        ('pooled-binding-dropped', 'MenuDepthCharacterSource.cs',
         '&& _holder != null && _nativeTarget != null && (_rendering || ReferenceEquals(_camera.targetTexture, _nativeTarget))',
         '&& _holder != null && _nativeTarget != null && _image.isActiveAndEnabled && (_rendering || ReferenceEquals(_camera.targetTexture, _nativeTarget))', 'Lifecycle',
         'native HideAll and window reopening retain depth before the next bounded census'),
        ('released-eye-reused', 'MenuDepthCharacterSource.cs', '!target.IsCreated() || target.width', 'target.width', 'Lifecycle',
         'a released owned eye resource is replaced before the next native eye capture'),
        ('first-display-discovery-lost', 'MenuDepthCharacters.cs',
         'PendingRequests.Add(key);',
         '/* injected: native Display does not queue its new request subtree */', 'Lifecycle',
         'the actual native Display postfix binds a new window before its first visible render'),
        ('render-disposal-not-deferred', 'MenuDepthCharacterSource.cs',
         'if (!_rendering) ReleaseTargets();', 'ReleaseTargets();', 'NativeCloseDuringRender',
         'shutdown during the actual native image effect defers owned-target destruction until the original draw returns'),
    ]
    if args.case:
        selected = set(args.case)
        assert selected <= {case[0] for case in variants}, selected
        variants = [case for case in variants if case[0] in selected]
    if args.editor_mode and any(case[4].startswith('Automatic') for case in variants):
        raise ValueError('Automatic Canvas/head/next-frame cases require the default real Playmode execution.')
    # Detect an evolved production seam before compiling unrelated variants.
    for name, filename, before, after, method, expected in variants:
        if filename: replace_once(sources[filename], before, after)
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'harmony': str(run / 'assemblies/0Harmony.dll'), 'editorMode': args.editor_mode, 'cases': []}
    exit_code = 1
    try:
        project = run / 'unity'; editor = project / 'Assets/Editor'; editor.mkdir(parents=True)
        (project / 'ProjectSettings').mkdir()
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages').mkdir()
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0'}}))
        shutil.copyfile(ROOT / 'scripts/menu-depth-character/Editor/Runner.cs', editor / 'Runner.cs')
        original_fixture = fixture
        fixture = run / 'source/fixture'; fixture.mkdir(parents=True)
        for path in original_fixture.iterdir():
            if path.is_file(): shutil.copyfile(path, fixture / path.name)
        for shader in fixture.glob('*.shader'): shutil.copyfile(shader, project / 'Assets' / shader.name)
        boundary = (fixture / 'Boundary.cs').read_text()
        boundary = replace_once(boundary, 'internal static class VRLog\n    {',
            'internal static class VRLog\n    {\n        internal static bool WantsDebug => true;\n        internal static void Debug(string scope, string text) => Messages.Add(text);')
        boundary = replace_once(boundary, 'public sealed class CharacterNativeImageEffect',
            '[ExecuteInEditMode] // Original native Beautify also carries this attribute.\npublic sealed class CharacterNativeImageEffect')
        (fixture / 'Boundary.cs').write_text(boundary)
        (run / 'source-pins.json').write_text(json.dumps({'sha256': pins,
            'boundaries': 'Native model/content/manager state and supplied XR eye positions; original Unity camera/image-effect/UI raster and production discovery/projection/leases run.',
            'fixture_ports': 'Exact copied native state boundary with diagnostic logger members, native Beautify ExecuteInEditMode annotation on the supplied pass-through effect, and unused general-decorative Driver observer ports. Character activation supplies the real surrounding Driver registration SetDiscoveryPump(Characters.FlushForCapture); queued discovery/camera/callbacks/GPU remain production/actual Unity.',
            'execution': 'EditorApplication.update' if args.editor_mode else 'Playmode',
            'limits': 'No actual game prefab, Beautify parity, provider, multiplayer transport or headset/comfort claim.'}, indent=2) + '\n')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        dotnet = os.environ.get('DOTNET', '/home/claw/.dotnet/dotnet')
        for name, filename, before, after, method, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, code in sources.items():
                if source_name == filename: code = replace_once(code, before, after)
                (production / source_name).write_text(code)
            csproj = case / 'MenuDepthCharacter.csproj'; shutil.copyfile(fixture / csproj.name, csproj)
            assembly = 'MenuDepthCharacter_' + name.replace('-', '_')
            command = [dotnet, 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo',
                '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
                '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed'),
                '-p:DepthBundle=' + str(ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle')]
            if execute(command, case / 'build.log'):
                raise RuntimeError('Compilation failure is not a negative-control pass: ' + name)
            retained = assemblies / (assembly + '.dll'); shutil.copyfile(case / 'bin/Release/net472' / retained.name, retained)
            for dependency in (case / 'bin/Release/net472').glob('*.dll'):
                if dependency.name != retained.name: shutil.copyfile(dependency, assemblies / dependency.name)
            manifest['cases'].append({'name': name, 'dll': str(retained), 'method': method, 'expected': expected})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        exit_code = execute(['xvfb-run', '-a', str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
            '-executeMethod', 'MenuDepthCharacterRunner.Start', '-menuDepthCharacterManifest', str(run / 'manifest.json'),
            '-logFile', str(run / 'unity.log')], run / 'launch.log', 420)
        (run / 'unity-exit-code.txt').write_text(str(exit_code) + '\n')
        stable = all(hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == digest for path, digest in pins.items())
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(pins)}) + '\n')
        if not stable: exit_code = 1
        if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
        if exit_code: raise RuntimeError('Unity character portal proof failed; see ' + str(run))
    finally:
        editors = cleanup_project_editors(project, args.unity, launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']:
            raise RuntimeError('Private native editor processes remain; retaining project caches: ' + str(run))
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Private project compiler processes remain; retaining project caches: ' + str(run))
        # The editor runner and generated uGUI assemblies actually executed too.
        # Retain their exact bytes outside Library before completion cleanup.
        retained_editor = run / 'assemblies/editor'; retained_editor.mkdir(parents=True, exist_ok=True)
        editor_hashes = {}
        for compiled in sorted((project / 'Library/ScriptAssemblies').glob('*')):
            if compiled.is_file() and compiled.suffix in ('.dll', '.pdb', '.mdb'):
                shutil.copyfile(compiled, retained_editor / compiled.name)
                editor_hashes[compiled.name] = hashlib.sha256(compiled.read_bytes()).hexdigest()
        (run / 'editor-assemblies.json').write_text(json.dumps(editor_hashes, indent=2) + '\n')
        receipt = cleanup_completed_run('menu-depth-character', exit_code, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return exit_code


if __name__ == '__main__':
    raise SystemExit(main())
