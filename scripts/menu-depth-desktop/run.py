#!/usr/bin/env python3
"""Actual native ScreenSpaceCamera menu-depth capture and original glass GPU proof."""
import argparse
import hashlib
import importlib.util
import json
import os
import re
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
import native_tmp
_cleanup_spec = importlib.util.spec_from_file_location('menu_depth_compiler_cleanup', ROOT / 'scripts/check-environment-menu-scope.py')
_cleanup_module = importlib.util.module_from_spec(_cleanup_spec)
_cleanup_spec.loader.exec_module(_cleanup_module)
cleanup_run_compilers = _cleanup_module.cleanup_run_compilers


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


def execute(command, log, timeout=180, env=None):
    with log.open('w') as output:
        child = subprocess.Popen(command, cwd=ROOT, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None: os.killpg(child.pid, signal.SIGKILL); child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--case', action='append')
    parser.add_argument('--graphics-api',choices=('glcore','vulkan'),default='glcore')
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'): parser.error('Use the task-private ADB server.')
    def terminated(signum, frame): raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, terminated)
    output = ROOT / '.planning/debug/menu-depth-desktop'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); print('Evidence: ' + str(run), flush=True)
    compiler_temp = Path(tempfile.mkdtemp(prefix='ghmd-', dir='/tmp'))
    env = dict(os.environ, TMPDIR=str(compiler_temp), UseSharedCompilation='false')
    (run / 'compiler-temp.json').write_text(json.dumps({'TMPDIR': str(compiler_temp)}) + '\n')
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
             'MenuDepthCharacters.cs', 'MenuDepthCharacterSource.cs', 'MenuDepthCharacterProjection.cs', 'MenuDepthCardPlane.cs', 'MenuDepthNativeUiScope.cs', 'MenuDepthDesktopCarrier.cs', 'MenuDepthDesktopResources.cs')
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
    sources['OwnedMembers.cs'] = (source_root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.7.OwnedMembers.cs').read_text()
    ownership = (source_root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.1.Core.cs').read_text()
    sources['PanelOwnership.cs'] = 'namespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n{\n' + ''.join(
        member(ownership, signature) for signature in ('    internal static bool OwnsPanelLayers(', '    internal static void NoticeRelease(')) + '}\n'
    # Execute the exact outer admission loop, stopping before its unchanged cap
    # diagnostic. Eligibility, failure ledgers and allocation are explicit ports;
    # the owned-member branch must prevent even reaching that allocation port.
    consider = member(ownership, '    private static void Consider()')
    tail = '        if (Entries.Count >= cap && !_capLogged)'
    assert consider.count(tail) == 1
    sources['PanelConsider.cs'] = 'using System.Collections.Generic;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n{\n' + consider[:consider.index(tail)] + '    }\n}\n'
    capture = (source_root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs').read_text()
    dispatch = '            if (cam == Rig.VRRigDriver.HeadCamera)\n                CaptureMenuDepth(cam);'
    assert capture.count(dispatch) == 1
    # The independent generic panel hook survives an idle depth registry. Its
    # pre-existing pool/entry admission is the same already-engaged fixture port.
    sources['PanelHeadPreCull745.cs'] = ('using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n'
        + '{\n    private static void ConvertedHeadPreCull745(Camera cam)\n    {\n' + dispatch + '\n    }\n}\n')
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
    sources['MenuDepthDisplay.cs'] = sources['MenuDepthDisplay.cs'].replace('SystemInfo.graphicsUVStartsAtTop', 'global::HeadPitchBackend736.TopOrigin')
    # Supply the actual stored XR views even when the editor has no provider.
    # Projection remains the explicitly configured real mono lens for both eyes.
    sources['MenuDepthEye.cs'] = replace_once(sources['MenuDepthEye.cs'],
        'head.stereoEnabled ? head.GetStereoViewMatrix(side)',
        'global::DesktopEyeInput.Available ? head.GetStereoViewMatrix(side)')
    sources['DesktopCapture.cs'] = sources['DesktopCapture.cs'].replace(
        'camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right',
        'global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right')
    # Exact ready-toggle frame walk, growth hysteresis and band-limit arithmetic. Only
    # measurement scheduling/diagnostic sinks and unrelated offered-card roots are ports.
    content = (source_root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.4.Content.cs').read_text()
    frame_methods = ('private readonly struct ClipFrame', 'private static void MeasureFrame(',
        'private static void MeasureFramePadding(', 'private static void ApplyFrameHysteresis(',
        'private static Rect ClampFrameToBandLimit(', 'private static float AllowanceFor(',
        'private static void SplitAllowance(', 'private static bool ClipsChildren(',
        'private static bool Draws(', 'private static bool TryHostLocalBounds(', 'private static bool Intersect(')
    frame_body = ''.join(member(content, signature) for signature in frame_methods)
    for signature in ('private static float QuantiseUp(', 'private static Rect Union('):
        start = content.index(signature); frame_body += content[start:content.index(';', start)+1]+'\n'
    for name in ('FrameQuantumPx','MaxContentExpansion','MinScaledAreaFraction','ScaledContentThreshold',
                 'FrameOutlierGrowQuanta','FrameShrinkDeadBandQuanta','BandLimitFrameBudgetPx',
                 'FrameOutlierShrinkRunMeasurements','FrameShrinkRunMeasurements','MaxRtDimension','BandLimitedTexelsPerPixel'):
        frame_body += re.search(r'private const (?:int|float) '+name+r' = [^;]+;', ownership)[0]+'\n'
    sources['ReadyFrame746.cs'] = ('using System.Collections.Generic; using GloomhavenVR.Core; using UnityEngine; using UnityEngine.UI; '
        'namespace GloomhavenVR.WorldUI; internal static partial class PanelSupersample {\n' + frame_body + '\n}')
    sources['ReadyBadge746.cs'] = (source_root / 'src/GloomhavenVR/WorldUI/Modal/SharedWindowBadgeLayout.cs').read_text()
    fixtures = ROOT / 'tests/GloomhavenVR.MenuDepthDesktopTests'
    inputs = [flat / name for name in ('FlatScreen.1.Core.cs', 'FlatScreen.2.CameraStack.cs', 'FlatScreen.7.CaptureRender.cs', 'FlatScreen.8.MenuDepth.cs')]
    inputs += [depth / name for name in names]
    inputs += [source_root / 'src/GloomhavenVR/WorldUI/Sharpness' / name for name in
               ('PanelSupersample.1.Core.cs', 'PanelSupersample.2.Capture.cs', 'PanelSupersample.4.Content.cs', 'PanelSupersample.6.MenuDepth.cs', 'PanelSupersample.7.OwnedMembers.cs')]
    inputs += [source_root / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle', ROOT / 'scripts/menu-depth-desktop/Anchors724.cs', Path(__file__), *fixtures.glob('*.cs'), *fixtures.glob('*.shader'), fixtures / 'MenuDepthDesktop.csproj', ROOT / 'scripts/menu-depth-desktop/Editor/Runner.cs']
    inputs += [ROOT / 'scripts/menu-depth-character/project_compilers.py', ROOT / 'scripts/check-environment-menu-scope.py']
    inputs += [ROOT / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll', ROOT / 'scripts/menu-depth-desktop/native_tmp.py']
    wants_native_tmp = not args.case or any(name.startswith(('head-native-tmp','head-confirmation')) for name in args.case)
    if wants_native_tmp:
        inputs += native_tmp.inputs(ROOT)
    inputs += [source_root / 'src/GloomhavenVR/WorldUI/Modal/SharedWindowBadgeLayout.cs']
    pins = [{'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for path in dict.fromkeys(inputs)]
    variants = [
        ('head-creation752', '', '', '', ''),
        ('head-creation-old752', 'MenuDepthCharacterProjection.cs',
         '        CreationLayout(rectangle, out Rect fit, out float scale, out float ground);',
         '        Rect fit = rectangle.rect; float scale = 1f, ground = float.NegativeInfinity;',
         'the entire short and tall creation figure clears every native keyboard key'),
        ('head-ready746', '', '', '', ''),
        ('head-ready-cap-old746', 'ReadyFrame746.cs',
         'if (target == null || target.GetComponent<UIReadyToggle>() == null) return;',
         'if (target == null || target.GetComponent<UIReadyToggle>() == null || target.GetInstanceID() != 0) return;',
         'full native vote symbols survive the actual measured capture and final head pixels'),
        ('head-ready-badge-old746', 'ReadyBadge746.cs',
         'seat = new Vector2(right + gap + size * 0.5f, top - size * 0.5f);',
         'seat = new Vector2(20, 0);',
         'compact multiplayer badge leaves the entire native button and vote ink unobscured'),
        ('head-confirmation745', '', '', '', ''),
        ('head-confirmation-membership-lost745', 'MenuDepthSources.cs',
         '=> source is IMenuDepthOwnedWindowSource member ? ReferenceEquals(member.CaptureOwner, root)',
         '=> source is IMenuDepthOwnedWindowSource member ? source.Graphic.transform.IsChildOf(root)',
         'owned native confirmation joins the exact producer head route at an already exhausted mono budget'),
        ('head-confirmation-ownership-lost745', 'PanelOwnership.cs',
         'if (CapturedOwnedMember(panel)) return true;', '/* no native member layer/admission ownership */',
         'the actual generic conversion and mono admission seam respects its captured native member'),
        ('head-confirmation-admission-lost745', 'PanelConsider.cs',
         '!Eligible(panel) || OwnsPanelLayers(panel)', '!Eligible(panel)',
         'actual generic admission never requests a second confirmation allocation at the exhausted mono budget'),
        ('head-confirmation-visible-anchor-lost745', 'OwnedMembers.cs',
         'if (MenuDepthSources.IsVisible(graphic) && OwnedTree(member, graphic.transform))', 'if (graphic != null && graphic.isActiveAndEnabled)',
         'owned native confirmation joins the exact producer head route at an already exhausted mono budget'),
        ('head-confirmation-layer-lost745', 'OwnedMembers.cs',
         'node.gameObject.layer = entry.Layer;', 'node.gameObject.layer = observed;',
         'both final head eyes retain original native confirmation button and font pixels at every movement pose'),
        ('head-confirmation-aa-lost745', 'MenuDepthHeadRaster.cs', 'SamplesPerAxis = 2;', 'SamplesPerAxis = 1;',
         'actual final head native TMP glyphs retain independent original-font pixel detail'),
        ('head-confirmation-restore-lost745', 'OwnedMembers.cs',
         'record.Transform.gameObject.layer = record.OriginalLayer;', 'record.Transform.gameObject.layer = layer;',
         'owner render permission loss immediately restores native member layers and retains its input'),
        ('head-confirmation-pooled-restore-lost745', 'OwnedMembers.cs',
         'record.Transform != null && OwnedTree(member, record.Transform)', 'record.Transform != null',
         'member release restores only its own native root and preserves a pooled foreign capture handoff'),
        ('head-confirmation-permission-lost745', 'OwnedMembers.cs',
         '&& !member.Owner.RenderHidden && !member.Owner.OwnerRenderHidden', '&& true',
         'owner render permission loss immediately restores native member layers and retains its input'),
        ('head-confirmation-generation-lost745', 'OwnedMembers.cs',
         'if (!SameBinding(member) || !LiveConversion(member.Owner)', 'if (!LiveConversion(member.Owner)',
         'a pooled native identity change revokes and forgets the old exact capture binding'),
        ('head-confirmation-generic-hook-lost745', 'PanelHeadPreCull745.cs',
         '                CaptureMenuDepth(cam);', '                { /* existing generic dispatch lost */ }',
         'restored producer permission readmits its own native confirmation at unchanged quality'),
        ('head-native-tmp742', '', '', '', ''),
        ('head-native-tmp-native-metric-lost742', 'MenuDepthNativeUiScope.cs',
         'float scale = Mathf.Pow(sx * sy * sz, 1f / 3f);', 'float scale = Mathf.Pow(sx * sy * sz, 0f);',
         'actual final head native TMP glyphs retain independent original-font pixel detail'),
        ('head-native-tmp-native-view-lost742', 'MenuDepthNativeUiScope.cs',
         'Matrix4x4 projection = eye.RasterProjection;', 'Matrix4x4 projection = head.projectionMatrix;',
         'actual final head native TMP glyphs retain independent original-font pixel detail'),
        ('head-native-tmp-converted-view-lost742', 'MenuDepthDisplay.cs',
         '        Matrix4x4 projection = eye.RasterProjection;\n        capture.targetTexture',
         '        Matrix4x4 projection = eye.HeadCamera.projectionMatrix;\n        capture.targetTexture',
         'actual final head native TMP glyphs retain independent original-font pixel detail'),
        ('head-model-churn-lost742', 'MenuDepthCharacterSource.cs',
         'if (!_headTargets || width != _width || height != _height || bytes != _pairBytes',
         'if (true || !_headTargets || width != _width || height != _height || bytes != _pairBytes',
         'a native model scratch reallocates only with the observed UI capacity transitions'),
        ('head-crop-metric742', '', '', '', ''),
        ('head-crop-metric-view-lost742', 'MenuDepthCharacterProjection.cs',
         'Matrix4x4 projection = eye.RasterProjection;',
         'Matrix4x4 projection = head.projectionMatrix;',
         'storage-only crop preserves original native view normals and physical clip metric'),
        ('head-crop-metric-ui-lost742', 'MenuDepthNativeUiScope.cs',
         'Matrix4x4 projection = eye.RasterProjection;',
         'Matrix4x4 projection = head.projectionMatrix;',
         'native UI view and target pixel density remain invariant under a storage-only crop'),
        ('head-crop-metric-copy-lost742', 'MenuDepthSources.cs',
         'eye.HasWorldToHeadClip ? eye.WorldToHeadClip : null, eye.RasterProjection);',
         'eye.HasWorldToHeadClip ? eye.WorldToHeadClip : null);',
         'actual sampled-source dispatch preserves cropped raster projection and native view metric'),
        ('head-backing742', '', '', '', ''),
        ('head-late-renderer742', '', '', '', ''),
        ('head-late-renderer-census-lost742', 'MenuDepthCharacterSource.cs',
         '_holder.GetComponentsInChildren(true, _headBoundsRenderers);',
         '_headBoundsRenderers.Clear(); _headBoundsRenderers.AddRange(_calibration.SamplingRenderers(identity));',
         'actual final head figure retains direct native-world XY surface detail during movement'),
        ('head-crop-origin-lost742', 'MenuDepthHeadRaster.cs',
         '(_head.x - 2f * _region.x - width) / width', '(_head.x - 2f * (_region.x + .5f) - width) / width',
         'actual final head text and XY print retain direct native-world pixel detail during movement'),
        ('head-capacity-small-growth-lost742', 'MenuDepthHeadRaster.cs',
         'int width = FilterCapacity(region.width, head.x);',
         'int width = Mathf.Min(head.x, Mathf.CeilToInt(region.width / 64f) * 64);',
         'continuous motion uses bounded GPU capacity growth instead of repeated small reallocations'),
        ('head-capacity-retirement-lost742', 'MenuDepthHeadRaster.cs',
         'Time.unscaledTime - _filterShrinkSince < 1.5f', 'Time.unscaledTime - _filterShrinkSince < float.PositiveInfinity',
         'retiring oversized near-view storage preserves final head UI and figure sampling at the fixed two-times grid'),
        ('head-backing-old-single742', '', '', '', 'actual final head retains original window and figure ink for both eyes'),
        ('head-filter-lifecycle742', '', '', '', ''),
        ('head-filter-xr-viewport-lost742', 'MenuDepthHeadRaster.cs',
         'current != null && current.targetTexture != null ? current.pixelRect : new Rect(0, 0, head.x, head.y)',
         'current != null ? current.pixelRect : new Rect(0, 0, head.x, head.y)',
         'a null-target XR observation restores the eye grid rather than the desktop pixel rectangle'),
        ('head-filter-first-failure-lost742', 'DesktopCapture.cs',
         '        MenuDepthHeadRaster.ReleaseFilter();', '        /* first failed capture keeps its shared filter */',
         'failed first capture releases AA storage even before a display material exists'),
        ('head-filter-pending-release-lost742', 'MenuDepthHeadRaster.cs',
         'if (_filterBorrowed) { _filterReleasePending = true; return; }', 'if (_filterBorrowed) { return; }',
         'shutdown requested inside the actual capture drains its AA lease after the native draw'),
        ('head-filter-idle-release-lost742', 'MenuDepthSources.cs',
         'if (Sources.Count == 0) MenuDepthHeadRaster.ReleaseFilter();', '/* idle registry retains its head AA surface */',
         'the last native graphic releases AA storage while discovery remains installed'),
        ('head-aliasing742', '', '', '', ''),
        ('head-aliasing-lost742', 'MenuDepthHeadRaster.cs',
         'SamplesPerAxis = 2;', 'SamplesPerAxis = 1;',
         'actual final head UI edges reduce alias error against independent supersampled native pixels'),
        ('head-preflight736', '', '', '', ''),
        ('head-preflight-lost736', 'DesktopCapture.cs', '            ValidateMenuDepthMappings(left, right);',
         '            /* native UI root preflight omitted */',
         'an unmapped native UI stack cannot admit head targets or prepare a partial spatial frame'),
        ('head-pitch736', '', '', '', ''),
        ('head-pitch-rt-y736', '', '', '', ''),
        ('head-pitch-rt-y-lost736', 'MenuDepthDisplay.cs',
         'if (global::HeadPitchBackend736.TopOrigin) gpu.SetRow(1, -gpu.GetRow(1));',
         '/* sampled UV still uses the inverted native GPU row */',
         'actual final head retains original window and figure ink for both eyes'),
        ('head-motion730', '', '', '', ''),
        ('head-raster4040', '', '', '', ''),
        ('head-resources730', '', '', '', ''),
        ('head-order730', '', '', '', ''),
        ('head-card-edge732', '', '', '', ''),
        ('head-native-card-edge732', '', '', '', ''),
        ('head-figure-card732', '', '', '', ''),
        ('head-native-card-ztest-lost732', 'MenuDepthGraphic.cs',
         '_depthMaterial.EnableKeyword("UNITY_UI_ALPHACLIP");',
         '_depthMaterial.EnableKeyword("UNITY_UI_ALPHACLIP"); _depthMaterial.SetInt("unity_GUIZTestMode",8);',
         'actual final head figure retains direct native-world XY surface detail during movement'),
        ('head-annotation-glyph-plane732', 'DesktopCapture.cs',
         '(RectTransform)canvas.transform, _quad.transform.position,', 'annotation.Graphic.rectTransform, _quad.transform.position,',
         'actual final head text and XY print retain direct native-world pixel detail during movement'),
        ('head-native-card-alpha-lost732', 'MenuDepthGraphic.cs',
         '_depthMaterial.EnableKeyword("UNITY_UI_ALPHACLIP");', '/* native empty sprite fragments write depth */',
         'transparent native card corners do not grant ordinary off-window UI permission'),
        ('head-card-plane-depth-lost732', 'MenuDepthGraphic.cs',
         'bool plane = MenuDepthHeadRaster.Active && HasPlaneOwner(true);', 'bool plane = false;',
         'the real head compositor preserves the accepted native card sprite outside its window frame'),
        ('head-card-plane-unscoped732', 'MenuDepthCardPlane.cs',
         'MenuDepthHeadRaster.Active && _headCapture && isActiveAndEnabled', 'MenuDepthHeadRaster.Active && isActiveAndEnabled',
         'an unrelated head capture cannot adopt an already primed direct card material'),
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
        ('head-coplanar-lost730','DesktopCapture.cs',
         'eye.WorldPosition, out Vector3 nativeEye, out Matrix4x4 nativeToPhysical, allowCoplanar: true)',
         'eye.WorldPosition, out Vector3 nativeEye, out Matrix4x4 nativeToPhysical, allowCoplanar: false)',
         'fixed head coordinator retains stereo capture at the exact physical glass-plane crossing'),
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
        ('outer-converted-sample-clip742', '', '', '', ''),
        ('outer-converted-sample-clip-lost742', 'MenuDepthHeadRaster.cs',
         'worldToFrame, frame, annotations, nativeToPhysical);',
         'worldToFrame, frame, annotations, nativeToPhysical); _material.SetFloat("_GvrHeadClipActive", 0f);',
         'head antialias samples never blend unrelated native UI into off-window figure edges'),
        ('outer-sample-clip-lost742', 'MenuDepthHeadRaster.cs',
         'worldToFrame, frame, annotations, nativeToPhysical);',
         'worldToFrame, frame, annotations, nativeToPhysical); _material.SetFloat("_GvrHeadClipActive", 0f);',
         'head antialias samples never blend unrelated native UI into off-window figure edges'),
        ('outer-bounds-lost728', 'MenuDepthCharacterSource.cs',
         'view = MenuDepthCharacterProjection.Head(view, _image.rectTransform, eye);',
         'view = view.Expanded(new Rect(-1f,-1f,2f,2f));',
         'the complete original 3D figure matches its independent physical geometry instead of outer-frame clipping'),
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
        ('window-anchors-native-unit-pixels-725', 'MenuDepthCharacterSource.cs',
         'int width = pixels.x, height = pixels.y;',
         'int width = pixels.x / 2, height = pixels.y / 2;',
         'the real original character eye targets retain full fixed head detail from far to near'),
        ('window-anchors-holder-fallback-725', 'MenuDepthCharacterSource.cs',
         'return ReadOriginal == null ? _holder : ReadOriginal(_manager);', 'return _holder;',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-rear-rejected726', 'MenuDepthScreenProjection.cs',
         '(allowCoplanar || Mathf.Abs(Vector3.Dot(center - nativeEye, forward)) > 1e-5f)',
         '(Vector3.Dot(center - nativeEye, forward) > 1e-5f)',
         'the calibrated original renders both sampled eye targets from a mapped physical pixel demand'),
        ('window-anchors-native-image-effect', '', '', '', ''),
        ('window-anchors-ui-image-effect', '', '', '', ''),
        ('window-anchors-frozen-eye', 'MenuDepthScreenProjection.cs',
         'Matrix4x4.TRS(physicalCenter, screen.rotation,', 'Matrix4x4.TRS(physicalCenter + screen.right * .15f, screen.rotation,',
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
         'MenuDepthDisplay.SetHeadCapture(_menuDepthHeadMaterial, _menuDepthCarrierLeft!, _menuDepthRight!,',
         'MenuDepthDisplay.SetHeadCapture(_menuDepthHeadMaterial, _menuDepthRight!, _menuDepthCarrierLeft!,',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('no-native-window-mapping', 'MenuDepthScreenProjection.cs',
         'Matrix4x4.TRS(physicalCenter, screen.rotation,',
         'Matrix4x4.TRS(physicalCenter + screen.right * .15f, screen.rotation,',
         'both original native UI cameras populate the borrowed eye target without ordinary-route overwrite'),
        ('right-route-lost', 'CaptureGuard.cs',
         '        if (camera != null && ReferenceEquals(camera, _menuDepthBorrowedCamera)) return;',
         '        /* injected: ordinary capture rewrites a borrowed right target */',
         'both original native UI cameras populate the borrowed eye target without ordinary-route overwrite'),
        ('head-right-selection-lost', 'MenuDepthDisplay.cs',
         'ApplyEye(global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right ? 1 : 0);',
         'ApplyEye(0);',
         'actual head rendering selects the correct original glass eye before draw'),
        ('native-depth-projection-lost', 'MenuDepthGraphic.cs',
         'UIVertex vertex = _stream[i]; vertex.position.z += depth;',
         'UIVertex vertex = _stream[i]; /* graphic depth is lost before the real head projection */',
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
        plugins = project / 'Assets/Plugins'; plugins.mkdir()
        shutil.copyfile(ROOT / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll', plugins / 'Unity.TextMeshPro.dll')
        if wants_native_tmp:
            native_tmp.prepare(ROOT, run, project)
            pinned = {pin['path'] for pin in pins}
            for pin in json.loads((run / 'native-tmp-inputs/decoder-pins.json').read_text())['files']:
                if pin['path'] not in pinned:
                    pins.append(pin); pinned.add(pin['path'])
        shutil.copyfile(ROOT / 'scripts/menu-depth-desktop/Editor/Runner.cs', editor / 'Runner.cs')
        for shader in fixtures.glob('*.shader'):
            shutil.copyfile(shader, project / 'Assets' / shader.name)
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'depthBundle': str(source_root / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle'), 'cases': []}
        (run / 'source-pins.json').write_text(json.dumps({'inputs': pins,
            'boundaries': 'Already-visible screen resources and native scene/manager geometry; actual screen-space Canvas, uGUI, native Camera.Render, original capture route/clears/guard/desktop mapper/registry/character and graphic production run. XR availability/active eye are supplied, real Unity stored stereo matrices remain.',
            'limits': 'No real XR/provider/headset, native 3D prefab art, game session/network or Windows D3D11 execution. Native TMP cases use original font/atlas/material/DLL and audited canonical SDF source.'}, indent=2) + '\n')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        for name, filename, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, code in sources.items():
                if source_name=='MenuDepthDisplay.cs' and name.startswith('head-pitch-rt-y') and args.graphics_api=='glcore':
                    code=replace_once(code,'GL.GetGPUProjectionMatrix(projection, true)',
                        'global::HeadPitchBackend736.TopOriginGpuProjection(projection, true)')
                if source_name == filename:
                    if name == 'outer-frozen-basis-lost728':
                        # Both native raster scopes must lose the same layout
                        # freeze; the active head route remains the pixel witness.
                        assert code.count(before) == 2, (name, code.count(before))
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
            if execute(command, case / 'build.log', env=env):
                raise RuntimeError('Compilation failure is not a causal-control pass: ' + name)
            dll = assemblies / (assembly + '.dll'); shutil.copyfile(case / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected,
                                      'method': ('HeadCreation752' if name.startswith('head-creation') else 'HeadReady746' if name.startswith('head-ready') else 'HeadConfirmation745' if name.startswith('head-confirmation') else 'HeadNativeTmp742' if name.startswith('head-native-tmp') else 'HeadLateRenderer742' if name == 'head-model-churn-lost742' else 'HeadCropMetric742' if name.startswith('head-crop-metric') else 'HeadMotion730' if name.startswith('head-capacity') else 'HeadLateRenderer742' if name.startswith('head-late-renderer') else 'HeadMotion730' if name.startswith('head-crop-origin') else 'HeadBackingOld742' if name == 'head-backing-old-single742' else 'HeadBacking742' if name.startswith('head-backing') else 'HeadFilterLifecycle742' if name.startswith('head-filter') else 'HeadAliasing742' if name.startswith('head-aliasing') else 'HeadPreflight736' if name.startswith('head-preflight') else 'HeadPitchRt736' if name.startswith('head-pitch-rt-y') else 'HeadPitch736' if name.startswith('head-pitch') else 'HeadFigureCard732' if name in ('head-figure-card732','head-native-card-ztest-lost732') else 'HeadNativeCardEdge732' if name.startswith('head-native-card-') else 'HeadCardEdge732' if name == 'head-card-edge732' or name.startswith('head-card-plane-') else 'HeadOrder730' if name in ('head-order730','head-order-stale730') else 'HeadAnnotations730' if name in ('head-annotations730','head-annotation-character-gated730','head-annotation-glyph-plane732') else 'HeadResources730' if name in ('head-resources730','head-window-slots-lost730','head-scene-slots-lost730','head-normalization-lost730') else 'HeadRaster4040' if name == 'head-raster4040' else 'HeadMotion730' if name.startswith('head-motion') or name.startswith('head-coplanar-') else 'HeadFrustum729' if name.startswith('head-frustum-') or name == 'head-frustum729' else
                                          'DesktopResources728' if name.startswith('desktop-') else 'OuterCarrierConverted742' if name.startswith('outer-converted') else 'OuterCarrier728' if name.startswith('outer-') else 'NativeDepthProbe728' if name.startswith('native-depth-') and name.endswith('728') else
                                          'AnchorsModelEffect724' if name == 'window-anchors-native-image-effect' else
                                          'AnchorsUiEffect724' if name == 'window-anchors-ui-image-effect' else
                                          'Anchors724' if name.startswith('window-anchors-') else 'Run')})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'command-environment.json').write_text(json.dumps({name: os.environ.get(name) for name in
            ('ADB_SERVER_SOCKET', 'ADB_MDNS_AUTO_CONNECT')}, indent=2) + '\n')
        # The complete83-case scope now includes repeated real two-route head
        # motion, independent supersampled references and allocation controls.
        # The old420s editor deadline stopped after39 cases on llvmpipe. Keep
        # every render/assertion and bound this expanded fixture at900s instead.
        status = execute(['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-'+args.graphics_api,
            '-projectPath', str(project), '-executeMethod', 'MenuDepthDesktopRunner.Start', '-menuDepthDesktopManifest', str(run / 'manifest.json'),
            '-logFile', str(run / 'unity.log')], run / 'launch.log', 900, env=env)
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
        borrowed = cleanup_run_compilers(run, editor_reaped=True)
        (run / 'compiler-fd-cleanup.json').write_text(json.dumps(borrowed, indent=2) + '\n')
        if borrowed['remaining'] or borrowed['refused']:
            raise RuntimeError('Private compiler borrowers remain; retaining project caches.')
        if compilers['remaining']:
            raise RuntimeError('Private project compiler processes remain; retaining project caches: ' + str(run))
        receipt = cleanup_completed_run('menu-depth-desktop', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        shutil.rmtree(compiler_temp)
    return status


if __name__ == '__main__': raise SystemExit(main())
