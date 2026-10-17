#!/usr/bin/env python3
"""Build the isolated SBS UI shader bank and verify actual Unity camera pixels.

The eye observation alone is substituted because this fixture has no XR provider.
Camera.Render, shared camera callbacks, UI tessellation/material masking, shader
programs, texture filtering and readback are real. This is not headset evidence.
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


def execute(command, log, timeout=420):
    with log.open('w') as stream:
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()


def project(path, fixture):
    for name in ('Assets/Editor', 'Packages', 'ProjectSettings'):
        (path / name).mkdir(parents=True, exist_ok=True)
    for source in (fixture / 'Editor').glob('*.cs'):
        shutil.copyfile(source, path / 'Assets/Editor' / source.name)
    (path / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}\n')
    (path / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')


def replace_once(source, before, after):
    assert source.count(before) == 1, 'Mutation binding drift: ' + before
    return source.replace(before, after, 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--build-bundle-only', action='store_true')
    parser.add_argument('--use-existing-bundle', action='store_true', help='Compatibility flag: tests always use the committed production bundle.')
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--graphics-api', choices=('glcore', 'vulkan'), default='glcore')
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = root / 'scripts/stereo-video-render'
    output = (args.output_dir or root / '.planning/debug/stereo-video-render').resolve()
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    print('Evidence: ' + str(run), flush=True)
    status = 1
    try:
        unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
        managed = root / 'ressources/GH_Data/Managed'
        source_path = root / 'src/GloomhavenVR/WorldUI/FlatScreen/StereoVideoSurface.cs'
        source = source_path.read_text()
        shader = (fixture / 'StereoVideoUI.shader').read_text()
        shader_cases = [('production', shader, '')]
        if not args.no_negative_controls and not args.build_bundle_only:
            shader_cases += [
                ('seam', replace_once(shader,
                    'uv = clamp(uv, float2(eye * 0.5 + halfTexel.x, halfTexel.y),\n                    float2((eye + 1.0) * 0.5 - halfTexel.x, 1.0 - halfTexel.y));',
                    '// seam clamp deliberately removed'), 'bilinear seam never samples opposite eye'),
                ('stencil', replace_once(shader, 'Comp [_StencilComp]', 'Comp Always'), 'native UI stencil hides only outside original mask'),
                ('clip', replace_once(shader, 'color.a *= mask.x * mask.y;', '// original UI clip deliberately removed'), 'native RectMask2D clips original UI extent'),
                ('softness', replace_once(shader,
                    '0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY)', '0.0 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY)'),
                    'native RectMask2D softness preserves feathered boundary'),
                ('alpha', replace_once(shader, 'color.rgb *= color.a;', '// alpha premultiplication deliberately removed'),
                    'texture tint and CanvasGroup alpha compose correctly'),
            ]
        editor = run / 'unity'
        project(editor, fixture)
        bundles = run / 'bundles'
        bundles.mkdir()
        build_cases = []
        for name, text, _ in shader_cases:
            if name == 'production' and not args.build_bundle_only:
                continue
            path = run / (name + '.shader')
            path.write_text(text)
            build_cases.append({'name': name, 'shader': str(path), 'output': str(bundles / name)})
        build_manifest = run / 'build-manifest.json'
        build_manifest.write_text(json.dumps({'result': str(run / 'bundle-build.txt'),
            'license': str(fixture / 'Unity-LICENSE.txt'), 'cases': build_cases}, indent=2) + '\n')
        asset = root / 'src/GloomhavenVR/Assets/StereoVideo/ui-sbs.bundle'
        if build_cases:
            code = execute(['xvfb-run', '-a', str(unity), '-batchmode', '-nographics', '-projectPath', str(editor),
                '-executeMethod', 'StereoVideoBuilder.Build', '-stereoBuildManifest', str(build_manifest),
                '-logFile', str(run / 'bundle-unity.log')], run / 'bundle-launch.log')
            if code or not (run / 'bundle-build.txt').exists():
                raise RuntimeError('Shader bundle build failed; inspect ' + str(run))
        if args.build_bundle_only:
            asset.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(bundles / 'production/production.bundle', asset)
            (asset.parent / 'provenance.json').write_text(json.dumps({
                'unity': '2021.3.5f1', 'target': 'StandaloneWindows64', 'graphics_apis': ['Direct3D11', 'OpenGLCore', 'Vulkan'],
                'shader_source': 'scripts/stereo-video-render/StereoVideoUI.shader',
                'shader_sha256': hashlib.sha256(shader.encode()).hexdigest(),
                'bundle_sha256': hashlib.sha256(asset.read_bytes()).hexdigest(),
                'build_options': 'None; TypeTrees enabled', 'resource': 'GloomhavenVR.StereoVideo.ui-sbs.bundle',
                'source_provenance': 'scripts/stereo-video-render/source-provenance.json',
                'embedded_notice': 'assets/stereovideo/unity-license.txt',
            }, indent=2) + '\n')
        verifier = [str(Path.home() / 'unitypy-venv/bin/python'), str(fixture / 'verify_bundle.py'),
            str(asset), str(fixture / 'Unity-LICENSE.txt'), str(run / 'bundle-metadata.json')]
        if execute(verifier, run / 'bundle-metadata.log', timeout=60):
            raise RuntimeError('Actual bundle metadata failed: ' + str(run))
        if args.build_bundle_only:
            print('PASS bundle build: ' + str(asset), flush=True)
            status = 0
            return
        (bundles / 'production').mkdir()
        shutil.copyfile(asset, bundles / 'production/production.bundle')
        provenance = json.loads((asset.parent / 'provenance.json').read_text())
        assert provenance['shader_sha256'] == hashlib.sha256(shader.encode()).hexdigest(), 'Committed bundle shader source is stale: rebuild explicitly.'
        assert provenance['bundle_sha256'] == hashlib.sha256(asset.read_bytes()).hexdigest(), 'Committed bundle hash disagrees with provenance.'
        bound = replace_once(source, 'camera.stereoActiveEye', 'global::StereoEyeFixture.ActiveEye')
        cases = [('production', bound, bundles / 'production/production.bundle', '')]
        if not args.no_negative_controls:
            cases += [
                ('swapped-eye', replace_once(bound, 'EyeIndex(global::StereoEyeFixture.ActiveEye)',
                    '1 - EyeIndex(global::StereoEyeFixture.ActiveEye)'), bundles / 'production/production.bundle',
                    'actual left pixels select left packed half'),
                ('duplicated-eye', replace_once(bound, 'EyeIndex(global::StereoEyeFixture.ActiveEye)', '0'),
                    bundles / 'production/production.bundle', 'actual right pixels select right packed half'),
                ('packed-aspect', replace_once(bound, 'packedWidth / (2f * height)', 'packedWidth / (float)height'),
                    bundles / 'production/production.bundle', 'per-eye aspect renders the source circle round'),
                ('callback-leak', replace_once(bound, 'Camera.onPreRender -= BeforeCamera;', '// detach deliberately omitted'),
                    bundles / 'production/production.bundle', 'disposed surface leaves no camera callback behind'),
                ('nested-eye-restore', replace_once(bound, 'Camera.onPostRender += AfterCamera;', '// nested eye restoration deliberately omitted'),
                    bundles / 'production/production.bundle', 'nested mono camera restores outer right-eye pixels before outer draw'),
            ]
            cases += [(name, bound, bundles / name / (name + '.bundle'), expected)
                      for name, _, expected in shader_cases if name != 'production']
        cases += [
            ('unsupported-mode', replace_once(bound, 'XRSettings.stereoRenderingMode',
                'XRSettings.StereoRenderingMode.SinglePassInstanced'), bundles / 'production/production.bundle', ''),
            ('missing-bundle', replace_once(bound, '"GloomhavenVR.StereoVideo.ui-sbs.bundle"',
                '"GloomhavenVR.StereoVideo.deliberately-missing.bundle"'), bundles / 'production/production.bundle', ''),
        ]
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        test_fixture = root / 'tests/GloomhavenVR.StereoVideoRenderTests'
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
        for name, code, bundle, expected in cases:
            case = run / name
            production = case / 'production'
            production.mkdir(parents=True)
            (production / 'StereoVideoSurface.cs').write_text(code)
            csproj = case / 'StereoRender.csproj'
            shutil.copyfile(test_fixture / 'StereoRender.csproj', csproj)
            assembly = 'StereoVideo_' + name.replace('-', '_')
            command = [dotnet, 'build', str(csproj), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(test_fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(managed),
                '-p:StereoBundle=' + str(bundle)]
            if execute(command, case / 'build.log', timeout=120):
                raise RuntimeError('Compilation failure is not a successful negative control: ' + str(case))
            assembly_path = run / 'assemblies' / (assembly + '.dll')
            assembly_path.parent.mkdir(exist_ok=True)
            shutil.copyfile(case / 'bin/Release/net472' / (assembly + '.dll'), assembly_path)
            manifest['cases'].append({'name': name, 'dll': str(assembly_path), 'expected': expected,
                'method': 'RunUnavailable' if name in ('unsupported-mode', 'missing-bundle') else 'Run'})
        manifest_path = run / 'manifest.json'
        manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        hashes = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
                  for p in [source_path, asset, *(fixture.rglob('*')), *(test_fixture.glob('*'))]
                  if p.is_file() and '__pycache__' not in str(p)}
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': hashes,
            'eye_boundary': 'Only camera.stereoActiveEye -> StereoEyeFixture.ActiveEye; actual Camera.Render calls production onPreRender handler.',
            'limits': 'Real UI/GPU pixels and embedded shader; no headset/XR provider, native movie decoder, multiplayer, audio, skip/continuation or performance proof.',
            'coverage': 'focused-production' if args.no_negative_controls else 'production-and-causal-controls'}, indent=2) + '\n')
        code = execute(['xvfb-run', '-a', str(unity), '-batchmode', '-force-' + args.graphics_api, '-projectPath', str(editor),
            '-executeMethod', 'StereoVideoRunner.Start', '-stereoManifest', str(manifest_path),
            '-logFile', str(run / 'gpu-unity.log')], run / 'gpu-launch.log')
        if (run / 'results.txt').exists():
            print((run / 'results.txt').read_text(), end='', flush=True)
        if code or not (run / 'results.txt').exists():
            raise RuntimeError('Stereo GPU fixture failed; inspect ' + str(run))
        status = 0
    finally:
        receipt = cleanup_completed_run('stereo-video-render', status, completion, root, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    main()
