#!/usr/bin/env python3
"""Render actual menu depth code, shader, UI and ownership under Unity2021.3.5f1."""
from pathlib import Path
import argparse
import hashlib
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


def execute(argv, log, timeout=420):
    with log.open('w') as stream:
        process = subprocess.Popen(argv, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
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
    status = 1
    try:
        fixture = ROOT / 'tests/GloomhavenVR.MenuDepthCaptureTests'
        source = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth'
        names = ('IMenuDepthSource.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs',
                 'MenuDepthProjection.cs', 'MenuDepthDisplay.cs')
        originals = {name: (source / name).read_text() for name in names}
        originals['PanelDepth.cs'] = (ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.6.MenuDepth.cs').read_text()
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
        copied = 'using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class PanelSupersample\n{\n'
        for signature in ('    private static long VramBytesFor(', '    private static long CaptureVramBytesFor(',
                          '    private static long MipVramBytesFor(', '    private static RenderTexture DisplayTexture('):
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
             'final.x, final.y', 'source sampling demand stops at the actual intermediate UI raster'),
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
            ('old-mono-path', 'PanelDepth.cs',
             'e.MenuDepthDisplay.Render(e.Cam, e.Panel.HostRect, e.Frame, e.Rt, DisplayTexture(e),\n                    leftEye, rightEye, captureFrame);',
             'e.Cam.Render();', 'a successful spatial window has exactly two UI camera draws per tick and no automatic third draw'),
            ('wrong-right-eye', 'MenuDepthDisplay.cs',
             'CaptureEye(capture, plane, frame, sampledFrame, _rightCapture, _rightMip ?? _rightCapture, rightEye, out float rightDepth, out float rightScale);',
             'CaptureEye(capture, plane, frame, sampledFrame, _rightCapture, _rightMip ?? _rightCapture, leftEye, out float rightDepth, out float rightScale);',
             'a right preparation failure draws the completed left and fresh mono fallback while restoring all begun leases'),
            ('missing-off-axis', 'MenuDepthProjection.cs',
             'projection = Matrix4x4.Frustum(left, rightEdge, bottom, top, near, far);',
             'projection = Matrix4x4.Perspective(40f, frame.width / frame.height, near, far);',
             'window corners remain exactly pinned in the off-axis capture'),
            ('missing-native-restore', 'MenuDepthDisplay.cs', 'original.Restore(capture);',
             '/* injected: borrowed camera state survives */',
             'the mono fallback actually contains newly changed native artwork instead of the previous spatial eye'),
            ('missing-source-restore', 'MenuDepthSources.cs', 'try { Prepared[i].AfterCapture(); }',
             'try { /* injected: native content lease survives */ }',
             'every begun direct source lease restores before the head callback completes'),
            ('nested-eye-lost', 'MenuDepthDisplay.cs',
             'private static void AfterCamera(Camera camera) => ApplyEye(PreviousEyes.Count > 0 ? PreviousEyes.Pop() : 0);',
             'private static void AfterCamera(Camera camera) => ApplyEye(0);',
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
            ('owner-loss-draw', 'MenuDepthDisplay.cs', 'if (_disposeRequested || !target.IsCreated())',
             'if (target.width < 0)',
             'closed native owner cannot submit another eye draw after its source lease ends'),
        ]
        if args.case:
            wanted = set(args.case); assert wanted <= {case[0] for case in cases}
            cases = [case for case in cases if case[0] in wanted]
        manifest = {'evidence': str(run), 'result': str(run / 'results.txt'), 'cases': []}
        env = dict(os.environ, DOTNET_ROOT='/home/claw/.dotnet', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
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
                        'internal static bool WantsDebug => true; internal static void Debug(string scope, string message) => Messages.Add(message);\n        internal static readonly List<string> Messages')
                if path.name == 'Program.cs':
                    code = code.replace('        Projection();', '        _checks += Routing724.Run(output);\n        Workload725(output);\n        Frame728(output);\n        Projection();')
                    code = code.replace('new GameObject("source scope", typeof(RectTransform))', 'new GameObject("source scope", typeof(RectTransform), typeof(Canvas))')
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
        pins = [*(source / name for name in names), legacy_path, core_path, isolation_path, Path(__file__).parent / 'Routing724.cs',
                Path(__file__).parent / 'Workload725.cs', Path(__file__).parent / 'Frame728.cs', ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.3.Report.cs', ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.6.MenuDepth.cs',
                *fixture.glob('*'), Path(__file__), Path(__file__).parent / 'Editor/MenuDepthRunner.cs',
                ROOT / 'scripts/menu-depth-character/project_compilers.py',
                ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle']
        hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in pins if p.is_file()}
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': hashes,
            'boundaries': 'XR eye observations/matrices and original panel entry/legacy allocation are explicit ports. Actual new coordinator, projections, UI geometry, source leases, camera rendering, mips and shipping shaders execute.',
            'limits': 'No physical XR, D3D11, native model/asset loading, gameplay, multiplayer or comfort claim.'}, indent=2) + '\n')
        argv = ['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
                '-projectPath', str(project), '-executeMethod', 'MenuDepthRunner.Start',
                '-menuDepthManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
        (run / 'command.json').write_text(json.dumps(argv, indent=2) + '\n')
        (run / 'command-environment.json').write_text(json.dumps({name: os.environ.get(name) for name in
            ('ADB_SERVER_SOCKET', 'ADB_MDNS_AUTO_CONNECT')}, indent=2) + '\n')
        status = execute(argv, run / 'launch.log')
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
        cleanup = cleanup_completed_run('menu-depth-capture', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': raise SystemExit(main())
