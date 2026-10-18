#!/usr/bin/env python3
"""Exercise production foreground-depth viewport commands against actual Unity pixels.

The desktop Editor has no XR provider. One explicitly qualified provider-query
boundary calls the exact production normalized-to-pixel converter with distinct
left/right render-pass descriptors. Production depth capture/restoration executes
unchanged. This does not establish a Quest headset image or the main-menu cause.
"""
import argparse
import atexit
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/actor-bar-viewport-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--graphics-api', choices=('glcore', 'vulkan'), default='glcore')
    args = parser.parse_args()
    source = args.source_root / 'src/GloomhavenVR/WorldUI/HealthbarForegroundDepth.cs'
    production = source.read_text()
    before = 'Rect viewport = Viewport(camera, ref width, ref height);'
    assert production.count(before) == 1, 'Production provider-query boundary drift'
    assert production.count('return XrViewportPixels(parameter.viewport, pass.renderTargetDesc, ref width, ref height);') == 1, 'Actual XR provider conversion/descriptor binding drift'
    injected = production.replace(before, 'Rect viewport = ViewportFixture.ProviderViewport(ref width, ref height);')
    mutations = [('production', injected, '')]
    old = 'return new Rect(normalized.x * width, normalized.y * height,\n            normalized.width * width, normalized.height * height);'
    assert injected.count(old) == 1, 'Normalized viewport conversion binding drift'
    mutations.append(('normalized-as-pixels', injected.replace(old, 'return normalized;'),
                      'production depth capture occupies the expected provider pixel subrect'))
    marker = 'capture.SetViewport(viewport);'
    assert injected.count(marker) == 2, 'Capture and restoration viewport binding drift'
    mutations.append(('capture-full-target', injected.replace(marker, 'capture.SetViewport(new Rect(0, 0, width, height));', 1),
                      'production depth capture keeps the uncovered half outside the foreground silhouette'))
    prefix, suffix = injected.rsplit(marker, 1)
    mutations.append(('restore-full-target', prefix + 'capture.SetViewport(new Rect(0, 0, width, height));' + suffix,
                      'production restoration confines following colour draws to the provider pixel subrect'))

    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    state = {'exit_code': 1, 'reaped': True}

    def cleanup():
        result = cleanup_completed_run('actor-bar-viewport-runtime', state['exit_code'], completion,
                                       args.source_root, process_reaped=state['reaped'])
        (run / 'cache-cleanup.json').write_text(json.dumps(result, indent=2) + '\n')
    atexit.register(cleanup)

    fixture = args.source_root / 'scripts/actor-bar-viewport-runtime'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    inputs = [source, Path(__file__), *(fixture.rglob('*.cs')), *(fixture.glob('*.shader')), fixture / 'Viewport.csproj']
    (run / 'source-hashes.json').write_text(json.dumps({
        'schema': 1, 'sha256': {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs},
        'boundary': 'Only the native provider query at Prepare is replaced. Its exact production XrViewportPixels method receives normalized viewport and per-pass renderTargetDesc. Actual camera capture/restore/temporary depth/global texture/DrawRenderer code executes unchanged.',
        'limits': 'Small desktop-Editor GPU analogue; no native XR session or headset appearance is claimed. Non-XR/targetTexture branches are also invoked unchanged.'
    }, indent=2) + '\n')
    for name, value, expected in mutations:
        build = run / name
        generated = build / 'production'
        generated.mkdir(parents=True)
        (generated / source.name).write_text(value)
        project = build / 'Viewport.csproj'
        shutil.copyfile(fixture / 'Viewport.csproj', project)
        assembly = 'ActorBarViewport_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                                 '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
                                 '-p:ProductionDir=' + str(generated),
                                 '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + '\nCompile errors are not successful negative controls.')
        binaries = run / 'assemblies'
        binaries.mkdir(exist_ok=True)
        dll = binaries / (assembly + '.dll')
        shutil.copyfile(build / 'bin/Release/netstandard2.1' / (assembly + '.dll'), dll)
        manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})

    project = run / 'unity'
    for name in ('Assets/Editor', 'Assets/Resources', 'Packages', 'ProjectSettings'):
        (project / name).mkdir(parents=True)
    shutil.copyfile(fixture / 'Editor/ViewportRunner.cs', project / 'Assets/Editor/ViewportRunner.cs')
    shutil.copyfile(fixture / 'Witness.shader', project / 'Assets/Resources/ViewportWitness.shader')
    dependencies = {name: '1.0.0' for name in ('com.unity.modules.xr', 'com.unity.modules.physics',
                    'com.unity.modules.jsonserialize', 'com.unity.modules.imageconversion')}
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}) + '\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'
    path.write_text(json.dumps(manifest, indent=2) + '\n')
    command = [str(args.unity), '-batchmode', '-force-' + args.graphics_api, '-projectPath', str(project),
               '-executeMethod', 'ViewportRunner.Start', '-viewportManifest', str(path), '-logFile', str(run / 'unity.log')]
    if not os.environ.get('DISPLAY'):
        command = ['xvfb-run', '-a'] + command
    state['reaped'] = False
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
    try:
        returncode = process.wait(timeout=180)
    finally:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait()
        state['reaped'] = True
    report = Path(manifest['result'])
    if report.is_file():
        print(report.read_text(), end='')
    if returncode or not report.is_file():
        raise SystemExit('FAIL Unity: ' + str(run / 'unity.log'))
    state['exit_code'] = 0
    print('PASS: production and three negative viewport controls; evidence: ' + str(run))


if __name__ == '__main__':
    main()
