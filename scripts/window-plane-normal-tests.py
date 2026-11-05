#!/usr/bin/env python3
"""Execute physical-window normalization in the installed real Unity engine.

This compiles the complete current projection file, then runs real Transform,
Vector3.Normalize and the production normal helper without GPU/XR/native UI ports.
Other projection entry points only compile; the render suite owns consumer pixels.
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
import tempfile
import time

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    assert source.count(signature) == 1, 'Production method binding changed: ' + signature
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=Path(os.environ.get('GHVR_TEST_SOURCE_ROOT', ROOT)))
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Use a private task ADB_SERVER_SOCKET; do not borrow the default shared ADB server.')
    source_root = args.source_root.resolve()
    fixture = ROOT / 'tests/GloomhavenVR.WindowPlaneNormalTests'
    production = source_root / 'src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthProjection.cs'
    original = production.read_text()
    plane = method(original, '    internal static Vector3 PlaneNormal(')
    direction = method(original, '    private static bool TryNormalizeDirection(')
    cases = [('production', original, '')]
    historical = ('    internal static Vector3 PlaneNormal(Transform plane)\n    {\n'
        '        if (plane == null) return Vector3.zero;\n'
        '        return Vector3.Cross(plane.TransformVector(Vector3.right), plane.TransformVector(Vector3.up)).normalized;\n    }')
    cases.append(('historical-raw-axis-cross', original.replace(plane, historical), 'valid physical window retains a finite unit plane normal'))
    per_axis = ('    private static bool TryNormalizeDirection(Vector3 value, out Vector3 direction)\n    {\n'
        '        direction = value.normalized; return Finite(direction) && direction.sqrMagnitude > 0f;\n    }')
    cases.append(('unity-axis-cutoff', original.replace(direction, per_axis), 'valid physical window retains a finite unit plane normal'))
    for name, before, after, expected in [
        ('missing-safe-rescale', 'Vector3 scaled = value / largest;', 'Vector3 scaled = value;', 'normalization accepts only finite nonzero directions'),
        ('rotation-only-winding', 'return normal;', 'return plane.forward;', 'physical plane winding matches its independent transformed geometric reference'),
        ('invalid-plane-sentinel-lost', '            return Vector3.zero;\n        return normal;', '            return Vector3.one;\n        return normal;', 'degenerate physical planes keep the zero sentinel'),
        ('nonfinite-input-accepted', 'if (!Finite(value)) return false;', 'if (!Finite(value)) { direction = Vector3.one; return true; }', 'normalization accepts only finite nonzero directions'),
    ]:
        assert original.count(before) == 1, 'Mutation binding changed: ' + name
        cases.append((name, original.replace(before, after), expected))
    output = ROOT / '.planning/debug/window-plane-normal'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    project = run / 'unity'
    for folder in ('Assets/Editor', 'ProjectSettings', 'Packages'): (project / folder).mkdir(parents=True)
    log = run / 'console.log'; log.write_text('Evidence: ' + str(run) + '\n')
    print('Evidence: ' + str(run), flush=True)
    inputs = [production, source_root / 'src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthDisplay.cs',
        source_root / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.8.MenuDepth.cs',
        Path(__file__), ROOT / 'scripts/window-plane-normal-tests.sh', ROOT / 'scripts/town_run_retention.py',
        *fixture.rglob('*.cs'), fixture / 'WindowPlaneNormal.csproj']
    (run / 'source-bindings.json').write_text(json.dumps({str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}, indent=2) + '\n')
    assemblies = run / 'executed-assemblies'; assemblies.mkdir()
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    started, status, process = time.monotonic(), 1, None
    reaped = True
    try:
        # The actual consumers must all use one computed normal, not an unused tested helper.
        display = inputs[1].read_text(); flat = inputs[2].read_text()
        head = method(display, '    internal void RenderHead(')
        assert head.count('MenuDepthProjection.PlaneNormal(plane)') == 1
        assert head.count('\n                normal,') == 2 and head.count('\n                normal);') == 1
        assert 'Vector3.Cross' not in head
        assert flat.count('MenuDepthProjection.PlaneNormal(_quad.transform)') == 1 and 'Vector3.Cross' not in flat
        for name, source, expected in cases:
            case = run / name; case.mkdir()
            snapshot = case / 'MenuDepthProjection.cs'; snapshot.write_text(source)
            shutil.copyfile(fixture / 'WindowPlaneNormal.csproj', case / 'WindowPlaneNormal.csproj')
            assembly = 'GvrPlaneNormal760_' + name.replace('-', '_')
            command = [str(Path(os.environ.get('DOTNET_ROOT', Path.home() / '.dotnet')) / 'dotnet'), 'build',
                str(case / 'WindowPlaneNormal.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                '-p:UseSharedCompilation=false', '-nodeReuse:false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(fixture), '-p:ProjectionSource=' + str(snapshot),
                '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed')]
            result = subprocess.run(command, capture_output=True, text=True)
            transcript = result.stdout + result.stderr; (case / 'compile.log').write_text(transcript)
            with log.open('a') as stream: stream.write(transcript)
            if result.returncode: raise RuntimeError('Strict fixture compile failed: ' + str(case / 'compile.log'))
            dll = assemblies / (assembly + '.dll')
            shutil.copyfile(case / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
            print('Strict compile passed: ' + name, flush=True)
        shutil.copyfile(fixture / 'Editor/WindowPlaneNormalRunner.cs', project / 'Assets/Editor/WindowPlaneNormalRunner.cs')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.jsonserialize': '1.0.0'}}) + '\n')
        manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(args.unity), '-batchmode', '-nographics', '-projectPath', str(project),
            '-executeMethod', 'WindowPlaneNormalRunner.Run', '-planeManifest', str(manifest_path),
            '-logFile', str(run / 'unity.log')]
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
        reaped = False
        try: result = process.wait(timeout=180)
        finally:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait(); reaped = True
        editor_dll = project / 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'
        if editor_dll.exists(): shutil.copyfile(editor_dll, assemblies / editor_dll.name)
        (run / 'executed-pins.json').write_text(json.dumps({p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in assemblies.iterdir()}, indent=2) + '\n')
        report = Path(manifest['result'])
        if report.exists():
            print(report.read_text(), end='', flush=True)
            with log.open('a') as stream: stream.write(report.read_text())
        if result or not report.exists(): raise RuntimeError('Actual Unity math fixture failed: ' + str(run / 'unity.log'))
        status = 0
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL); process.wait(); reaped = True
        spec = importlib.util.spec_from_file_location('project_compilers', ROOT / 'scripts/menu-depth-character/project_compilers.py')
        editor_cleanup = importlib.util.module_from_spec(spec); spec.loader.exec_module(editor_cleanup)
        editors = editor_cleanup.cleanup_project_editors(project, args.unity, launcher_reaped=reaped)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        spec = importlib.util.spec_from_file_location('run_compilers', ROOT / 'scripts/check-environment-menu-scope.py')
        compilers = importlib.util.module_from_spec(spec); spec.loader.exec_module(compilers)
        compiler_result = compilers.cleanup_run_compilers(run, editor_reaped=reaped)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compiler_result, indent=2) + '\n')
        if editors['remaining'] or compiler_result['remaining'] or compiler_result['refused']:
            (run / '.keep').write_text('Exact owned process cleanup needs review.\n')
            raise RuntimeError('Owned processes could not be reaped safely')
        (run / 'validation.json').write_text(json.dumps({'exit_code': status, 'cases': len(cases),
            'negative_controls': len(cases) - 1, 'elapsed_seconds': time.monotonic() - started,
            'gpu_or_headset_claim': False}, indent=2) + '\n')
        receipt = cleanup_completed_run('window-plane-normal', status, log, ROOT, process_reaped=reaped)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    signal.signal(signal.SIGTERM, lambda signum, frame: (_ for _ in ()).throw(SystemExit(128 + signum)))
    main()
