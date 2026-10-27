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
    names = ('IMenuDepthSource.cs', 'MenuDepthEye.cs', 'MenuDepthProjection.cs', 'MenuDepthScreenProjection.cs',
             'MenuDepthSources.cs', 'MenuDepthDisplay.cs', 'MenuDepthGraphic.cs', 'MenuDepthHeightfield.cs',
             'MenuDepthCharacters.cs', 'MenuDepthCharacterSource.cs', 'MenuDepthCharacterProjection.cs')
    sources = {name: (depth / name).read_text() for name in names}
    sources['CaptureStack.cs'] = body + '}\n'
    sources['CaptureGuard.cs'] = (flat / 'FlatScreen.7.CaptureRender.cs').read_text()
    sources['DesktopCapture.cs'] = (flat / 'FlatScreen.8.MenuDepth.cs').read_text()
    sources['Anchors724.cs'] = (ROOT / 'scripts/menu-depth-desktop/Anchors724.cs').read_text()
    # Explicit XR observation ports only. The original native camera's stored eye
    # matrices are real Unity inputs; the editor cannot report an active XR eye.
    sources['MenuDepthDisplay.cs'] = replace_once(sources['MenuDepthDisplay.cs'],
        'head == null || !head.stereoEnabled', 'head == null || !global::DesktopEyeInput.Available')
    sources['DesktopCapture.cs'] = replace_once(sources['DesktopCapture.cs'],
        'camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right',
        'global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right')
    fixtures = ROOT / 'tests/GloomhavenVR.MenuDepthDesktopTests'
    inputs = [flat / name for name in ('FlatScreen.1.Core.cs', 'FlatScreen.2.CameraStack.cs', 'FlatScreen.7.CaptureRender.cs', 'FlatScreen.8.MenuDepth.cs')]
    inputs += [depth / name for name in names]
    inputs += [ROOT / 'scripts/menu-depth-desktop/Anchors724.cs']
    pins = [{'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for path in inputs]
    variants = [
        ('production', '', '', '', ''),
        ('window-anchors-724', '', '', '', ''),
        ('window-anchors-native-unit-pixels-725', 'MenuDepthScreenProjection.cs',
         'MenuDepthProjection.HeadPixels(eye, OnGlass(screen, original, bl), OnGlass(screen, original, tl),\n            OnGlass(screen, original, tr), OnGlass(screen, original, br))',
         'MenuDepthProjection.HeadPixels(eye, bl, tl, tr, br)',
         'the real original character eye targets grow from far to near displayed pixels within their bounded tier'),
        ('window-anchors-holder-fallback-725', 'MenuDepthCharacterSource.cs',
         'ReadOriginal?.Invoke(_manager) ?? _holder', '_holder',
         'native hierarchical landmark is visible through the actual final glass head draw'),
        ('window-anchors-native-image-effect', '', '', '', ''),
        ('window-anchors-ui-image-effect', '', '', '', ''),
        ('window-anchors-frozen-eye', 'MenuDepthScreenProjection.cs',
         'Vector3 offset = physicalEye - physicalCenter;', 'Vector3 offset = -screen.forward * 2f;',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-forced-full-viewport', 'MenuDepthScreenProjection.cs',
         'nativeEye = Vector3.zero;\n        Rect viewport = original.rect;',
         'nativeEye = Vector3.zero;\n        Rect viewport = new Rect(0f, 0f, 1f, 1f);',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-moving-native-scene', 'MenuDepthCharacterSource.cs',
         '            _camera.targetTexture = target;',
         '            _camera.targetTexture = target;\n            _camera.transform.SetPositionAndRotation(view.Position, view.Rotation);',
         'final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion'),
        ('window-anchors-wrong-eye', 'DesktopCapture.cs',
         '_glassMaterial.mainTexture = _menuDepthFrame == Time.frameCount\n            && global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right ? _menuDepthRight : _uiRt;',
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
         '_glassMaterial.mainTexture = _menuDepthFrame == Time.frameCount\n            && global::DesktopEyeInput.Read(camera) == Camera.MonoOrStereoscopicEye.Right ? _menuDepthRight : _uiRt;',
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
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
        (run / 'source-pins.json').write_text(json.dumps({'inputs': pins,
            'boundaries': 'Already-visible screen resources and native scene/manager geometry; actual screen-space Canvas, uGUI, native Camera.Render, original capture route/clears/guard/desktop mapper/registry/character and graphic production run. XR availability/active eye are supplied, real Unity stored stereo matrices remain.',
            'limits': 'No real XR/provider/headset, actual prefab art, native network or final HMD comfort proof.'}, indent=2) + '\n')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        for name, filename, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, code in sources.items():
                if source_name == filename: code = replace_once(code, before, after)
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
                                      'method': ('AnchorsModelEffect724' if name == 'window-anchors-native-image-effect' else
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
