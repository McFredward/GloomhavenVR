#!/usr/bin/env python3
"""Exercise actual recorded-eye raster and live native UI camera leases in Unity.

Only decoder readiness/pixels and the XR eye observation are supplied. Production
raster, render textures, UI draw, VideoPlayer alpha binding and restoration are real.
No native decoder, headset, audio, multiplayer timing or comfort claim is made.
"""
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
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait()


def replace_once(source, before, after):
    assert source.count(before) == 1, before
    return source.replace(before, after, 1)


def main():
    output = ROOT / '.planning/debug/stereo-movie-render'
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    print('Evidence: ' + str(run), flush=True)
    status = 1
    def interrupted(signum, frame):
        raise KeyboardInterrupt
    previous_handlers = {kind: signal.signal(kind, interrupted) for kind in (signal.SIGTERM, signal.SIGINT)}
    try:
        unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
        project = run / 'unity'
        for directory in ('Assets/Editor', 'Packages', 'ProjectSettings'):
            (project / directory).mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / 'scripts/stereo-movie-render/Editor/StereoVideoRunner.cs', project / 'Assets/Editor/StereoVideoRunner.cs')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0',
            'com.unity.modules.assetbundle': '1.0.0', 'com.unity.modules.video': '1.0.0'}}) + '\n')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        source_root = ROOT / 'src/GloomhavenVR/WorldUI/FlatScreen'
        movie = (source_root / 'FlatScreenStereo.5.Movie.cs').read_text()
        movie = replace_once(movie, 'head.stereoActiveEye', 'global::StereoEyeFixture.ActiveEye')
        movie = replace_once(movie, 'Texture? texture = player.texture;', 'Texture? texture = global::MovieDecoderFixture.Texture;')
        movie = replace_once(movie, '''bool valid = player.isActiveAndEnabled && player.isPrepared && (player.isPlaying || player.isPaused)
                && player.frame >= 0 && texture != null && texture.width == 3840 && texture.height == 1080;''',
            'bool valid = global::MovieDecoderFixture.Prepared && texture != null && texture.width == 3840 && texture.height == 1080;')
        lease = (source_root / 'NativeMovieCameraPlane.cs').read_text()
        surface = (source_root / 'StereoVideoSurface.cs').read_text()
        cases = [('production', movie, lease, ''),
            ('duplicate-eye', replace_once(movie, 'DrawMovieEye(valid ? texture : null, _rtRight, 1, alpha, background);', 'DrawMovieEye(valid ? texture : null, _rtRight, 0, alpha, background);'), lease,
                'actual right eye uses its recorded right raster'),
            ('packed-aspect', replace_once(movie, 'StereoVideoSurface.PerEyeAspect(texture.width, texture.height)', 'texture.width / (float)texture.height'), lease,
                'per-eye movie frame retains round source geometry across desktop aspect ratios'),
            ('brightness', replace_once(movie, '0, 0, 0, 0, new Color(.5f, .5f, .5f, .5f * alpha)', '0, 0, 0, 0, Color.white'), lease,
                'decoded eye raster retains original unsaturated RGB without tint or brightness change'),
            ('vertical-flip', replace_once(movie, '.5f / texture.height,\n            .5f - 1f / texture.width, 1f - 1f / texture.height', '1f - .5f / texture.height,\n            .5f - 1f / texture.width, -1f + 1f / texture.height'), lease,
                'asymmetric top and bottom marks keep original vertical orientation'),
            ('alpha-ignored', replace_once(movie, 'Mathf.Clamp01(NativeMovieCameraPlane.PresentationAlpha(player))', '1f'), lease,
                'borrowed native alpha affects replacement RGB over retained background'),
            ('decoder-target', movie, replace_once(lease, 'player.targetCameraAlpha = 0f;', 'player.renderMode = VideoRenderMode.APIOnly;'),
                'camera-plane lease hides packed plane without decoder mode or target mutation'),
            ('alpha-restore', movie, replace_once(lease, 'Leases.Pop().Restore();\n    }\n    internal static void RestoreAll()', 'Leases.Pop();\n    }\n    internal static void RestoreAll()'),
                'post-render restores exact original native plane alpha'),
            ('nested-restore', movie, replace_once(lease, 'internal static void AfterRender(Camera camera)\n    {', 'internal static void AfterRender(Camera camera)\n    {\n        RestoreAll();'),
                'nested foreign camera cannot restore enclosing packed plane early')]
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
        fixture = ROOT / 'tests/GloomhavenVR.StereoMovieRenderTests'
        dotnet = str(Path.home() / '.dotnet/dotnet')
        for name, movie_code, lease_code, expected in cases:
            case = run / name
            production = case / 'production'
            production.mkdir(parents=True)
            (production / 'FlatScreenStereo.5.Movie.cs').write_text(movie_code)
            (production / 'NativeMovieCameraPlane.cs').write_text(lease_code)
            (production / 'StereoVideoSurface.cs').write_text(surface)
            csproj = case / 'StereoMovieRender.csproj'
            shutil.copyfile(fixture / 'StereoMovieRender.csproj', csproj)
            assembly = 'StereoMovie_' + name.replace('-', '_')
            if execute([dotnet, 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo', '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false',
                    '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                    '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
                    '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed'),
                    '-p:StereoBundle=' + str(ROOT / 'src/GloomhavenVR/Assets/StereoVideo/ui-sbs.bundle')], case / 'build.log', 120):
                raise RuntimeError('Fixture compilation failure is not a negative-control pass: ' + str(case))
            retained = run / 'assemblies' / (assembly + '.dll')
            retained.parent.mkdir(exist_ok=True)
            shutil.copyfile(case / 'bin/Release/net472' / retained.name, retained)
            manifest['cases'].append({'name': name, 'dll': str(retained), 'expected': expected})
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in [*(fixture.glob('*.cs')), fixture / 'StereoMovieRender.csproj', source_root / 'FlatScreenStereo.5.Movie.cs', source_root / 'NativeMovieCameraPlane.cs', source_root / 'StereoVideoSurface.cs']},
            'boundaries': 'Only decoder readiness/pixels and the headset eye observation; actual GPU raster, native UI, VideoPlayer alpha and render-target restoration.',
            'limits': 'No native media decode, headset/XR provider, D3D11, comfort, audio, multiplayer or timing evidence.'}, indent=2) + '\n')
        manifest_path = run / 'manifest.json'
        manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        result = execute(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
            '-executeMethod', 'StereoVideoRunner.Start', '-stereoManifest', str(manifest_path), '-logFile', str(run / 'unity.log')], run / 'launch.log')
        if (run / 'results.txt').exists():
            print((run / 'results.txt').read_text(), end='', flush=True)
        if result or not (run / 'results.txt').exists():
            raise RuntimeError('Actual movie raster fixture failed: ' + str(run))
        status = 0
    finally:
        receipt = cleanup_completed_run('stereo-movie-render', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        for kind, previous in previous_handlers.items():
            signal.signal(kind, previous)


if __name__ == '__main__':
    main()
