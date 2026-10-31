#!/usr/bin/env python3
"""Render actual menu depth code, shader, UI and ownership under Unity2021.3.5f1."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers, cleanup_project_editors
_cleanup_spec = importlib.util.spec_from_file_location('menu_depth_compiler_cleanup', ROOT / 'scripts/check-environment-menu-scope.py')
_cleanup_module = importlib.util.module_from_spec(_cleanup_spec)
_cleanup_spec.loader.exec_module(_cleanup_module)
cleanup_run_compilers = _cleanup_module.cleanup_run_compilers


def execute(argv, log, timeout=420, env=None):
    with log.open('w') as stream:
        process = subprocess.Popen(argv, env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()


def member(source, signature):
    start = source.index(signature); opening = source.index('{', start); depth = 1; end = opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end] + '\n'


def replace(source, before, after):
    assert source.count(before) == 1, (before, source.count(before))
    return source.replace(before, after, 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    output = ROOT / '.planning/debug/menu-depth-capture'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n'); print('Evidence: ' + str(run), flush=True)
    compiler_temp = Path(tempfile.mkdtemp(prefix='ghmc-', dir='/tmp'))
    (run / 'compiler-temp.json').write_text(json.dumps({'TMPDIR': str(compiler_temp)}) + '\n')
    status = 1
    try:
        fixture = ROOT / 'tests/GloomhavenVR.MenuDepthCaptureTests'
        source = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth'
        names = ('IMenuDepthSource.cs', 'MenuDepthHeadRaster.cs', 'MenuDepthHeadCarrier.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs',
                 'MenuDepthProjection.cs', 'MenuDepthDisplay.cs')
        originals = {name: (source / name).read_text() for name in names}
        quest_copy = ROOT / 'src/GloomhavenVR/Core/QuestTextureCopy.cs'
        quest_boundary = Path(__file__).parent / 'QuestDesktopBoundary.cs'
        originals['QuestTextureCopy.cs'] = quest_copy.read_text()
        originals['QuestDesktopBoundary.cs'] = quest_boundary.read_text()
        originals['PanelDepth.cs'] = (ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.6.MenuDepth.cs').read_text()
        perf_root = ROOT / 'src/GloomhavenVR/Core/Perf'
        monitor = (perf_root / 'PerfMonitor.cs').read_text()
        split = (perf_root / 'PerfFrameSplit.cs').read_text()
        # Exercise the actual allocation-free scopes and camera callback bodies.
        # Only the host/window/profiler lifecycle is a declared boundary here.
        originals['PerfScopes746.cs'] = ('using System;\nusing System.Collections.Generic;\nusing System.Diagnostics;\n'
            'namespace GloomhavenVR.Core;\ninternal static partial class PerfMonitor\n{\n'
            'internal static bool StepsActive = true; private static int _depth; private static double _frameModSeconds;\n'
            'private static readonly Dictionary<string, Step> Steps = new(); private static readonly List<Step> StepOrder = new();\n'
            + member(monitor, '    private sealed class Step')
            + member(monitor, '    internal static long BeginStep()')
            + member(monitor, '    internal static void EndStep(')
            + member(monitor, '    internal readonly struct Measure')
            + 'internal static Measure Scope(string name) => new(name);\n}\n')
        assert 'internal static Measure Scope(string name) => new(name);' in monitor
        originals['PerfCameraCallbacks746.cs'] = ('using System.Collections.Generic;\nusing System.Diagnostics;\nusing UnityEngine;\n'
            'namespace GloomhavenVR.Core;\ninternal static partial class PerfFrameSplit\n{\n'
            'private static readonly Dictionary<int, CamRec> Cameras = new(); private static readonly List<CamRec> CameraOrder = new();\n'
            + member(split, '    private sealed class CamRec')
            + member(split, '    private static void OnPreCull(')
            + member(split, '    private static void OnPreRender(')
            + member(split, '    private static void OnPostRender(') + '}\n')
        for name in ('CameraPasses', 'Spike'):
            originals['PerfFrameSplit.' + name + '.cs'] = (perf_root / ('PerfFrameSplit.' + name + '.cs')).read_text()
        roll = member(split, '    internal static void RollFrame(')
        assert roll.index('CaptureCompletedFrame(') < roll.index('_logicStart = now;')
        assert 'ResetCameraPasses(Time.frameCount);' in roll
        assert 'PerfFrameSplit.AppendSpike(sb);' in member(monitor, '    private static void LogSpike(')
        isolation_path = ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.5.Isolation.cs'
        isolation = isolation_path.read_text()
        originals['LayerReservation724.cs'] = ('using System.Collections.Generic;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n{\n'
            'private static readonly List<int> FreeLayers = new(); private static readonly List<int> LayerPool = new(); private static int _poolMask;\n'
            'private static void ResolvePool() {} // Explicit already-resolved native layer pool input.\n'
            'internal static void ResetLayerProof() { FreeLayers.Clear(); LayerPool.Clear(); FreeLayers.AddRange(new[]{8,9}); LayerPool.AddRange(new[]{8,9}); _poolMask=0; }\n'
            'internal static int TakeLayerProof()=>AcquireLayer(); internal static void ReturnLayerProof(int layer)=>ReleaseLayer(layer);\n'
            + member(isolation, '    private static int AcquireLayer()') + member(isolation, '    private static void ReleaseLayer(int layer)') + '}\n')
        originals['Routing724.cs'] = (Path(__file__).parent / 'Routing724.cs').read_text()
        originals['Workload725.cs'] = (Path(__file__).parent / 'Workload725.cs').read_text()
        originals['Frame728.cs'] = (Path(__file__).parent / 'Frame728.cs').read_text()
        legacy_path = ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs'
        legacy = legacy_path.read_text()
        copied = 'using GloomhavenVR.Core;\nusing UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n{\n'
        for signature in ('    private static long VramBytesFor(', '    private static long CaptureVramBytesFor(',
                          '    private static long MipVramBytesFor(', '    private static RenderTexture DisplayTexture(',
                          '    private static void ResolveAndMip(', '    private static float ApplyMipLodOffset('):
            start = legacy.index(signature)
            if '{' in legacy[start:legacy.index(';', start)]: copied += member(legacy, signature)
            else: copied += legacy[start:legacy.index(';', start) + 1] + '\n'
        for signature in ('    private static void SyncProjection(', '    private static void SyncDisplayPose(',
                          '    private static void SyncVisibility('): copied += member(legacy, signature)
        for signature in ('    private static bool SameComponents(Vector2', '    private static bool SameComponents(Vector3',
                          '    private static bool SameComponents(Quaternion'):
            start = legacy.index(signature); copied += legacy[start:legacy.index(';', start) + 1] + '\n'
        copied += member(legacy, '    private static void ResolveRate(')
        core = (ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.1.Core.cs').read_text()
        for name in ('MaxRtDimension', 'MinStepDownFactor', 'RateQuantum', 'MinContentScaleFloor'):
            declarations = re.findall(r'    private const (?:int|float) ' + name + r' = [^;]+;', core)
            assert len(declarations) == 1, name
            copied += declarations[0] + '\n'
        copied += re.search(r'    private const float MipLodOffsetEpsilon = [^;]+;', legacy).group(0) + '\n'
        copied += '    private static bool _mipBiasWriteWarned;\n'
        originals['LegacyHelpers.cs'] = copied + '}\n'
        core_path = ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.1.Core.cs'
        limits = re.findall(r'    private const long (?:MaxPanelVramBytes|MaxTotalVramBytes) = [^;]+;', core_path.read_text())
        assert len(limits) == 2
        limits += re.findall(r'    private const int CaptureIntervalFrames = [^;]+;', core_path.read_text())
        originals['ActualLimits.cs'] = 'namespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n{\n' + '\n'.join(limits) + '\n}\n'
        # XR observations are explicit fixture inputs, not fake rendered output.
        production = dict(originals)
        for name in ('MenuDepthSources.cs', 'MenuDepthDisplay.cs'):
            production[name] = production[name].replace('camera.stereoActiveEye', 'global::MenuDepthFixture.CurrentEye')
        production['MenuDepthDisplay.cs'] = production['MenuDepthDisplay.cs'].replace('!head.stereoEnabled', '!global::MenuDepthFixture.StereoEnabled')
        production['MenuDepthDisplay.cs'] = production['MenuDepthDisplay.cs'].replace(
            'head.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyPoint3x4(Vector3.zero)', 'global::MenuDepthFixture.Left').replace(
            'head.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.MultiplyPoint3x4(Vector3.zero)', 'global::MenuDepthFixture.Right')
        cases = [
            ('production', '', '', '', ''),
            ('camera-parent-lost746', 'PerfFrameSplit.CameraPasses.cs',
             'CameraPassStack[_cameraPassDepth++] = new CameraPass',
             '_cameraPassDepth = 0; CameraPassStack[_cameraPassDepth++] = new CameraPass',
             'real nested Camera.Render retains both parent and child callback intervals'),
            ('camera-frame-mismatch746', 'PerfFrameSplit.CameraPasses.cs',
             'if (_cameraPassFrame == frame) return;',
             'if (_cameraPassFrame >= -1) return;',
             'a stale callback cannot turn a cross-frame interval into camera work'),
            ('camera-orphan-unreported746', 'PerfFrameSplit.CameraPasses.cs',
             '_cameraPassFaults += _cameraPassDepth - index - 1;', '',
             'a missing child post-render is reported while the actual parent still completes'),
            ('spike-frame-mismatch746', 'PerfFrameSplit.Spike.cs',
             '&& _logicFrame == reportingFrame - 1;', ';',
             'a skipped reporting frame cannot relabel old seams as a completed frame'),
            ('nested-scope-double-count746', 'PerfScopes746.cs',
             'if (_depth == 0)\n            _frameModSeconds += seconds;',
             'if (_depth >= 0)\n            _frameModSeconds += seconds;',
             'nested native/render intervals contribute once to the outer measured total'),
            ('head-scope-lost746', 'MenuDepthSources.cs',
             'using var measurement = VRLog.WantsDebug ? PerfMonitor.Scope("MenuDepth.HeadPreCull") : default;', '',
             'actual head pre-cull and coordinator capture are attributed before native render callbacks finish'),
            ('head-coordinator-scope-lost746', 'PanelDepth.cs',
             'using var measurement = VRLog.WantsDebug ? PerfMonitor.Scope("MenuDepth.HeadCoordinator") : default;', '',
             'actual head pre-cull and coordinator capture are attributed before native render callbacks finish'),
            ('head-scope-normal746', 'MenuDepthSources.cs',
             'VRLog.WantsDebug ? PerfMonitor.Scope("MenuDepth.HeadPreCull") : default',
             'PerfMonitor.Scope("MenuDepth.HeadPreCull")',
             'normal logging leaves the new render measurements completely inactive'),
            ('member-sync-lost745', 'PanelDepth.cs',
             'SyncOwnedMembers(e); // live permission/lifetime before either actual head eye',
             '/* omitted membership boundary */',
             'membership sync boundary precedes actual native source preparation for the exact entry'),
            ('member-sync-wrong-entry745', 'PanelDepth.cs',
             'SyncOwnedMembers(e); // live permission/lifetime before either actual head eye',
             'SyncOwnedMembers(new Entry());',
             'membership sync boundary precedes actual native source preparation for the exact entry'),
            ('member-sync-after-cache745', 'PanelDepth.cs',
             'SyncOwnedMembers(e); // live permission/lifetime before either actual head eye\n            if (e.MenuDepthFrame == Time.frameCount) { e.MenuDepthDisplay?.PrepareHead(head); continue; }',
             'if (e.MenuDepthFrame == Time.frameCount) { e.MenuDepthDisplay?.PrepareHead(head); continue; }\n            SyncOwnedMembers(e);',
             'cached head dispatch observes membership before its cache shortcut without another native draw'),
            ('member-sync-only-visible745', 'PanelDepth.cs',
             'SyncOwnedMembers(e); // live permission/lifetime before either actual head eye',
             'if (e.DisplayImage.isActiveAndEnabled) SyncOwnedMembers(e);',
             'hidden head dispatch still observes membership before visibility retirement'),
            ('initial-empty-pump-725', 'MenuDepthSources.cs', 'if (pump != null) EnsureHook(); else UnhookIfIdle();',
             'if (pump == null) UnhookIfIdle();', 'an initially empty registry flushes late native readiness before its first actual head draw without recursive pumping'),
            ('late-query-pump-725', 'MenuDepthSources.cs', 'internal static bool HasContent(Transform root)\n    {\n        FlushPendingContent();',
             'internal static bool HasContent(Transform root)\n    {', 'a late content query flushes ready native identities before returning an empty answer'),
            ('redundant-mono-725', 'LegacyHelpers.cs', 'visible && due && e.MenuDepthDisplay == null',
             'visible && due', 'a successful spatial window has exactly two UI camera draws per tick and no automatic third draw'),
            ('hidden-stereo-725', 'PanelDepth.cs', '                || !e.DisplayImage.isActiveAndEnabled)',
             '                || !e.DisplayImage.enabled)', 'a hidden physical display performs no UI or native-content capture'),
            ('no-empty-mono-725', 'PanelDepth.cs', 'if (!MenuDepthSources.HasContent(e.Panel.HostRect))\n            {\n                bool skippedMono = e.MenuDepthDisplay != null && !e.Cam.enabled;',
             'if (!MenuDepthSources.HasContent(e.Panel.HostRect))\n            {\n                bool skippedMono = false;', 'leaving spatial content refills a suppressed automatic mono capture immediately'),
            ('sample-ceiling-725', 'MenuDepthProjection.cs', 'Mathf.Min(final.x, intermediate.x), Mathf.Min(final.y, intermediate.y)',
             'final.x, final.y', 'legacy photographed sampling demand stops at the actual intermediate UI raster'),
            ('near-native-fallback-725', 'MenuDepthProjection.cs', 'if (front < 4) return new Vector2(width, height);',
             'if (front < 4) return Vector2.zero;', 'a visibly near-clipped angled pane receives bounded visible pixel demand instead of native-size fallback'),
            ('masked-source-725', 'MenuDepthSources.cs', '!graphic.canvasRenderer.cull',
             'true', 'native RectMask2D-clipped sampled content performs no eye-resource work'),
            ('stale-private-exclusion', 'LayerReservation724.cs',
             '_poolMask |= 1 << layer;\n        MenuDepth.MenuDepthSources.ExcludedDirectLayers = _poolMask;',
             '_poolMask |= 1 << layer;', 'the second actual private-layer reservation updates direct-capture exclusion ownership immediately'),
            ('screen-ui-direct', 'MenuDepthSources.cs',
             'source.Graphic.canvas.rootCanvas.renderMode == RenderMode.WorldSpace',
             'source.Graphic.canvas.rootCanvas != null', 'screen-space consumers with nested canvases are never mistaken for direct world-space previews'),
            ('hidden-source-direct', 'MenuDepthSources.cs', 'graphic.canvasRenderer.GetInheritedAlpha() > 0f',
             'graphic.canvasRenderer.GetInheritedAlpha() >= 0f', 'fully hidden pooled native consumers never borrow the model scene or opt in their window'),
            ('fixture-hdr-camera-mismatch', 'Program.cs',
             'capture.allowHDR=false;', 'capture.allowHDR=true;',
             'retired spatial presentation returns original mono photograph to final head pixels'),
            ('mono-resolve-lost', 'LegacyHelpers.cs',
             'QuestTextureCopy.Copy(e.Rt, e.MipRt);', '/* injected: native mono display retains stale artwork */',
             'retired spatial presentation returns original mono photograph to final head pixels'),
            ('photo-canvas-restore-lost', 'MenuDepthHeadCarrier.cs',
             'if (_photoCanvas != null && !_photoCanvas.enabled) _photoCanvas.enabled = _canvasEnabled;',
             '/* injected: original photo Canvas remains retired */',
             'retired spatial presentation returns original mono photograph to final head pixels'),
            ('photo-canvas-foreign-enable-lost', 'MenuDepthHeadCarrier.cs',
             'if (_photoCanvas != null && !_photoCanvas.enabled) _photoCanvas.enabled = _canvasEnabled;',
             'if (_photoCanvas != null) _photoCanvas.enabled = _canvasEnabled;',
             'a foreign later photo Canvas enable survives head presentation disposal'),
            ('head-sorting-frozen', 'MenuDepthHeadCarrier.cs',
             'if (_renderer.sortingOrder != order) _renderer.sortingOrder = order;',
             '/* injected: presentation retains its first native order */',
             'head carrier follows the original live photo order above an independent native surface'),
            ('original-photo-ghost', 'MenuDepthHeadCarrier.cs',
             'if (_photoCanvas != null) _photoCanvas.enabled = false;', '/* injected: original photograph remains visible to a rebuilt Canvas batch */',
             'private mono photograph cannot add ink outside the current head raster'),
            ('cached-head-carrier-lost', 'PanelDepth.cs',
             'e.MenuDepthDisplay?.PrepareHead(head);', '/* injected: cached head carrier stays absent during culling */',
             'the final head raster actually displays the physical sampled aperture at its predicted pixel width'),
            ('old-mono-path', 'PanelDepth.cs',
             'e.MenuDepthDisplay.RenderHead(e.Cam, e.Panel.HostRect, e.Frame, e.Rt, leftEye, rightEye);',
             'e.Cam.Render();', 'a successful spatial window has exactly two UI camera draws per tick and no automatic third draw'),
            ('wrong-right-eye', 'MenuDepthDisplay.cs',
             'CaptureHeadEye(capture, plane, filter.Value.Target, sampledRight, filter.Value.Pixels);',
             'CaptureHeadEye(capture, plane, filter.Value.Target, sampledLeft, filter.Value.Pixels);',
             'a right preparation failure draws the completed left and fresh mono fallback while restoring all begun leases'),
            ('missing-off-axis', 'MenuDepthProjection.cs',
             'projection = Matrix4x4.Frustum(left, rightEdge, bottom, top, near, far);',
             'projection = Matrix4x4.Perspective(40f, frame.width / frame.height, near, far);',
             'window corners remain exactly pinned in the off-axis capture'),
            ('missing-native-restore', 'MenuDepthDisplay.cs', 'state.Restore(capture);',
             '/* injected: borrowed camera state survives */',
             'the mono fallback actually contains newly changed native artwork instead of the previous spatial eye'),
            ('missing-source-restore', 'MenuDepthSources.cs', 'try { Prepared[i].AfterCapture(); }',
             'try { /* injected: native content lease survives */ }',
             'every begun direct source lease restores before the head callback completes'),
            ('nested-eye-lost', 'MenuDepthDisplay.cs',
             'ApplyEye(PreviousEyes.Count > 0 ? PreviousEyes.Pop() : 0);',
             'ApplyEye(0);',
             'nested mono render restores the enclosing right eye shader selection'),
            ('missing-lifecycle-release', 'MenuDepthDisplay.cs', 'texture.Release(); UnityEngine.Object.Destroy(texture);',
             '/* injected: owned render surface remains created */',
             'failed second-eye preparation actually releases its right render surface'),
            ('missing-depth-write', 'MenuDepthDisplay.cs', '_depthShader = _bundle.LoadAsset<Shader>(DepthShaderPath);',
             '_depthShader = Shader.Find("UI/Default");',
             'opaque nearer native sprite pixels occlude later farther UI triangles'),
            ('missing-mask-projection', 'MenuDepthSources.cs',
             'Shader.SetGlobalFloat(ProjectMaskId, eye.ProjectToGraphicPlane ? 0f : 1f);',
             'Shader.SetGlobalFloat(ProjectMaskId, 0f);',
             'direct RectMask2D aperture stays on its original plane while art is deeper'),
            ('mixed-scope-order', 'MenuDepthSources.cs', 'BeforeHeadCapture?.Invoke(camera);',
             '/* injected: coordinator is delayed until direct sources are already borrowed */',
             'mixed direct and supersampled windows finish offscreen captures before direct source leases'),
            ('owner-loss-draw', 'MenuDepthDisplay.cs', 'private void ThrowIfHeadOwnerClosed(RenderTexture target)\n    {\n        if (_disposeRequested || !target.IsCreated())',
             'private void ThrowIfHeadOwnerClosed(RenderTexture target)\n    {\n        if (target.width < 0)',
             'closed native owner cannot submit another eye draw after its source lease ends'),
        ]
        if args.case:
            wanted = set(args.case); assert wanted <= {case[0] for case in cases}
            cases = [case for case in cases if case[0] in wanted]
        manifest = {'evidence': str(run), 'result': str(run / 'results.txt'), 'cases': []}
        env = dict(os.environ, DOTNET_ROOT='/home/claw/.dotnet', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0',
                   TMPDIR=str(compiler_temp), UseSharedCompilation='false')
        for name, filename, before, after, expected in cases:
            case = run / name; bound = case / 'production'; bound.mkdir(parents=True)
            for filename_actual, code in production.items():
                if filename_actual == filename: code = replace(code, before, after)
                if name == 'mixed-scope-order' and filename_actual == filename:
                    code = replace(code, 'else Prepared[i].BeforeCapture(sampled);\n            }\n        }\n        catch (Exception error)',
                        'else Prepared[i].BeforeCapture(sampled);\n            }\n            BeforeHeadCapture?.Invoke(camera);\n        }\n        catch (Exception error)')
                (bound / filename_actual).write_text(code)
            bound_fixture = case / 'fixture'; bound_fixture.mkdir()
            for path in fixture.glob('*.cs'):
                code = path.read_text()
                if path.name == 'Boundary.cs':
                    code = code.replace('internal static readonly List<string> Messages',
                        'internal static void Debug(string scope, string message) => Messages.Add(message);\n        internal static readonly List<string> Messages')
                if path.name == 'Program.cs':
                    code = code.replace('        Projection();', '        _checks += Routing724.Run(output);\n        Workload725(output);\n        Frame728(output);\n        Projection();')
                    code = code.replace('new GameObject("source scope", typeof(RectTransform))', 'new GameObject("source scope", typeof(RectTransform), typeof(Canvas))')
                    if name == 'mixed-scope-order':
                        # Exercise the intended mixed lease overlap before the
                        # separate no-direct-source callback pixel witness.
                        code = replace(code, '        Workload725(output);',
                            '        MixedCaptureOrder();\n        Workload725(output);')
                if path.name == filename: code = replace(code, before, after)
                (bound_fixture / path.name).write_text(code)
            csproj = case / 'MenuDepthCapture.csproj'; shutil.copyfile(fixture / csproj.name, csproj)
            assembly = 'MenuDepth_' + name.replace('-', '_')
            argv = ['/home/claw/.dotnet/dotnet', 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo',
                    '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                    '-p:FixtureDir=' + str(bound_fixture), '-p:ProductionDir=' + str(bound),
                    '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed',
                    '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed'),
                    '-p:DepthBundle=' + str(ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle')]
            with (case / 'build.log').open('w') as stream:
                result = subprocess.run(argv, env=env, stdout=stream, stderr=subprocess.STDOUT)
            if result.returncode:
                raise RuntimeError('Fixture compilation failure is not a control pass: ' + str(case))
            retained = run / 'assemblies' / (assembly + '.dll'); retained.parent.mkdir(exist_ok=True)
            shutil.copyfile(case / 'bin/Release/net472' / retained.name, retained)
            manifest['cases'].append({'name': name, 'dll': str(retained), 'expected': expected})
        project = run / 'unity'
        for name in ('Assets/Editor', 'Packages', 'ProjectSettings'): (project / name).mkdir(parents=True)
        # A versioned project keeps Unity from replacing the explicit package
        # manifest with its new-project template (including unrelated packages).
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        shutil.copyfile(Path(__file__).parent / 'Editor/MenuDepthRunner.cs', project / 'Assets/Editor/MenuDepthRunner.cs')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0',
            'com.unity.modules.assetbundle': '1.0.0', 'com.unity.modules.ui': '1.0.0'}}) + '\n')
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        pins = [*(source / name for name in names), *(perf_root / name for name in
                ('PerfMonitor.cs', 'PerfFrameSplit.cs', 'PerfFrameSplit.CameraPasses.cs', 'PerfFrameSplit.Spike.cs')),
                quest_copy, quest_boundary, legacy_path, core_path, isolation_path, Path(__file__).parent / 'Routing724.cs',
                Path(__file__).parent / 'Workload725.cs', Path(__file__).parent / 'Frame728.cs', ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.3.Report.cs', ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.6.MenuDepth.cs',
                *fixture.glob('*'), Path(__file__), Path(__file__).parent / 'Editor/MenuDepthRunner.cs',
                ROOT / 'scripts/menu-depth-character/project_compilers.py', ROOT / 'scripts/check-environment-menu-scope.py',
                ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle']
        hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in pins if p.is_file()}
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': hashes,
            'boundaries': 'XR eye observations/matrices, original panel entry/legacy allocation and an explicitly empty owned-member group with observed sync dispatch are ports. Actual coordinator, projections, UI geometry, source leases, camera rendering, mips and shipping shaders execute. Real owned-member grouping/layers/lifetime are covered by the head and native producer containers.',
            'limits': 'No physical XR, D3D11, native model/asset loading, gameplay, multiplayer or comfort claim.'}, indent=2) + '\n')
        argv = ['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
                '-projectPath', str(project), '-executeMethod', 'MenuDepthRunner.Start',
                '-menuDepthManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
        (run / 'command.json').write_text(json.dumps(argv, indent=2) + '\n')
        (run / 'command-environment.json').write_text(json.dumps({name: os.environ.get(name) for name in
            ('ADB_SERVER_SOCKET', 'ADB_MDNS_AUTO_CONNECT')}, indent=2) + '\n')
        status = execute(argv, run / 'launch.log', env=env)
        print((run / 'results.txt').read_text() if (run / 'results.txt').exists() else 'No Unity results', flush=True)
        for name, sha in hashes.items(): assert hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == sha, name
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': True, 'inputs': len(hashes)}) + '\n')
        return status
    finally:
        editors = cleanup_project_editors(run / 'unity', Path('/home/claw/unity-2021.3.5/Editor/Unity'), launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']: raise RuntimeError('Owned editor remains; retaining its project caches.')
        compilers = cleanup_project_compilers(run / 'unity', editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Project compiler survived reaping; its generated caches are retained.')
        borrowed = cleanup_run_compilers(run, editor_reaped=True)
        (run / 'compiler-fd-cleanup.json').write_text(json.dumps(borrowed, indent=2) + '\n')
        if borrowed['remaining'] or borrowed['refused']:
            raise RuntimeError('Private compiler borrowers remain; retaining project caches.')
        cleanup = cleanup_completed_run('menu-depth-capture', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
        shutil.rmtree(compiler_temp)


if __name__ == '__main__': raise SystemExit(main())
