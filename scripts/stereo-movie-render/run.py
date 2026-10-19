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
import re

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


def member(source, signature):
    """Retain an exact production member, including its original control flow."""
    start = source.index(signature)
    opening = source.index('{', start)
    level = 1
    end = opening + 1
    while level:
        level += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end] + '\n'


def startup_sources(source_root, movie, lease, surface):
    core = (source_root / 'FlatScreen.1.Core.cs').read_text()
    stack = (source_root / 'FlatScreen.2.CameraStack.cs').read_text()
    lifecycle = (source_root / 'FlatScreen.4.Lifecycle.cs').read_text()
    compositor = (source_root / 'FlatScreenStereo.2.Compositor.cs').read_text()
    per_eye = (source_root / 'FlatScreenStereo.4.PerEye.cs').read_text()
    common = 'using System;\nusing System.Collections.Generic;\nusing BepInEx.Configuration;\nusing GloomhavenVR.Core;\nusing GloomhavenVR.Hands.Interact;\nusing UnityEngine;\nusing UnityEngine.SceneManagement;\nusing UnityEngine.Video;\nusing Object = UnityEngine.Object;\nnamespace GloomhavenVR.WorldUI;\n'
    assert '        _coverageOwner = this;' in member(core, '    public FlatScreen()\n'), 'Fixture ownership port must match the real constructor assignment'
    flat = common + 'internal sealed partial class FlatScreen\n{\n'
    flat += core[core.index('    private sealed class CapturedCamera'):core.index('    // Placement anchors:')]
    for signature in ('    public void Tick()\n',):
        flat += member(core, signature)
    for signature in ('    private static bool IsPreMenuScene()\n', '    private bool WantVisible()\n', '    private void Show()\n'):
        flat += member(lifecycle, signature)
    for signature in ('    internal static bool TryPrepareStartupMovie(', '    internal static bool OwnsStartupMovie(', '    internal static bool OwnsCapturedMoviePlane(', '    internal static void PrepareQuestMovie(',
                      '    private void CaptureStack()\n', '    private void SelectBases()\n', '    private void TickSplitLifecycle()\n',
                      '    private void EnsureBackQuad()\n', '    private void TeardownSplit(', '    private void SetSplitRouting(',
                      '    private void ClearUiRt()\n', '    private void TickNoUiWatchdog()\n', '    private void TickStackClears()\n',
                      '    private void ReleaseStack()\n'):
        flat += member(stack, signature)
    for signature in ('    private static bool IsUiCamera(', '    private RenderTexture? TargetFor(', '    private bool SplitActive =>'):
        start = stack.index(signature)
        flat += stack[start:stack.index(';', start) + 1] + '\n'
    stereo = common + 'internal sealed partial class FlatScreenStereo\n{\n'
    for signature in ('    public FlatScreenStereo()\n', '    internal static void BindConfig()\n',
                      '    internal static RenderTexture CreateColorRt(', '    internal static string DescribeRt(',
                      '    internal void Tick(', '    private float ComputeVideoShiftUv()\n', '    internal void Deactivate(',
                      '    private void ReleaseRightRt()\n', '    private void ReleaseShiftRt()\n',
                      '    internal void BeginStackSync()\n', '    internal void EndStackSync()\n', '    private bool EnsureShiftRt()\n'):
        stereo += member(compositor, signature)
    for signature in ('    private static float DepthStrength =>', '    private static float ParallaxScale =>', '    private static bool WantActive('):
        start = compositor.index(signature)
        stereo += compositor[start:compositor.index(';', start) + 1] + '\n'
    # Keep the exact head admission and movie dispatch. Map drawing and legacy mono
    # branches are outside this startup fixture and must fail if they are requested.
    head = member(per_eye, '    private void OnPreRenderCamera(Camera cam)\n')
    head = head[head.index('        Camera? head = Rig.VRRigDriver.HeadCamera;'):]
    head = head[:head.index('        // Desktop retains its existing timing.')]
    stereo += '    private void OnPreRenderCamera(Camera cam)\n    {\n        if (!_active) return;\n' + head
    stereo += '        throw new Exception("Unexpected nonmovie head render in startup fixture");\n    }\n'
    movies = (source_root / 'NativeVideoMovies.cs').read_text()
    movies = movies[:movies.index('/// <summary>Change the existing assignment')]
    keys = (ROOT / 'src/GloomhavenVR/Net/VideoWindowState.cs').read_text()
    keys = keys[:keys.index('/// <summary>Presentation only.')]
    defaults = (ROOT / 'src/GloomhavenVR/Defaults/Defaults.WorldUI.cs').read_text()
    defaults = '\n'.join(line for line in defaults.splitlines() if re.search(r'internal const .* (StereoScreen|ScreenDepthStrength|VideoDepthLayer|VideoDepth|ScreenParallaxScale) =', line))
    result = {'QuestMaterial.cs': (source_root / 'FlatScreen.7.QuestMaterial.cs').read_text(),
        'QuestMesh.cs': (source_root / 'FlatScreen.7.QuestMesh.cs').read_text(),
        'QuestSampling.cs': (ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/QuestScreenSampling.cs').read_text(),
        'CaptureStack.cs': flat + '}\n', 'CaptureRender.cs': (source_root / 'FlatScreen.7.CaptureRender.cs').read_text(),
        'Compositor.cs': stereo + '}\n', 'Movie.cs': movie, 'NativeMovieCameraPlane.cs': lease, 'StereoVideoSurface.cs': surface,
        'Movies.cs': movies, 'Keys.cs': keys, 'StartupPatch.cs': (source_root / 'NativeEmbeddedMoviePatches.cs').read_text(),
        'Defaults.cs': 'namespace GloomhavenVR;\ninternal static class Defaults\n{\n' + defaults + '\n}\n'}
    for name in ('Movies.cs', 'Movie.cs', 'NativeMovieCameraPlane.cs'):
        code = result[name]
        code = re.sub(r'player\.url = ([^;]+);', r'global::MovieSourceFixture.SetUrl(player, \1);', code)
        result[name] = code.replace('player.url', 'global::MovieSourceFixture.GetUrl(player)')
    return result


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
        shutil.copyfile(ROOT / 'tools/quest-movies/QuestMovieEye.shader', project / 'Assets/QuestMovieEye.shader')
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
            ('decoder-target', movie, replace_once(lease, 'Leases.Push(new Lease(camera, player));\n        player.targetCameraAlpha = 0f;', 'Leases.Push(new Lease(camera, player));\n        player.renderMode = VideoRenderMode.APIOnly;'),
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
        startup = startup_sources(source_root, movie, lease, surface)
        startup_fixture = fixture / 'Startup'
        variants = [
            ('startup-production', '', '', '', 'Run', ''),
            ('startup-unprepared', '', '', '', 'Unprepared', ''),
            ('startup-late', '', '', '', 'Late', ''),
            *[('startup-' + name, '', '', '', method, '') for name, method in (
                ('error', 'Error'), ('destroy-player', 'DestroyPlayer'), ('destroy-camera', 'DestroyCamera'),
                ('source-change', 'SourceChanged'), ('camera-change', 'CameraChanged'), ('surface-loss', 'SurfaceLost'),
                ('vr-stop', 'VrStopped'), ('intro-disabled', 'IntroDisabled'), ('non-vr', 'NonVr'),
                ('foreign-target', 'ForeignTarget'), ('no-head', 'NoHead'), ('missing-original', 'MissingOriginal'))],
            ('startup-preplay-lost', 'CaptureStack.cs', '            NativeMovieCameraPlane.ReserveStartup(player);',
                '        /* injected: native Play receives its packed alpha */', 'Run',
                'studio packed plane is reserved before original Play rather than first hidden at late pre-cull'),
            ('startup-lazy-show-lost', 'CaptureStack.cs', '            if (!owner._visible) owner.Tick();',
                '            /* injected: startup waits for a later ordinary Tick */', 'Run',
                'automatic studio prefix prepares the actual capture and stereo lifecycle without options or a scenario board'),
            ('startup-late-census-lost', 'CaptureRender.cs', '        if (_stereo.NeedsMovieCaptureSync(camera)) CaptureStack();',
                '        /* injected: known startup camera retains the stale census */', 'Late',
                'known startup camera reconciles late source admission before its first actual render'),
            ('startup-exact-player-lost', 'Movie.cs', '_active && _stereoMovie == player', '_active && _stereoMovie != null', 'Run',
                'persistent reservation proves exact decoder identity, not only its reused camera'),
            ('startup-created-right-lost', 'Movie.cs', '_rtRight != null && _rtRight.IsCreated()', '_rtRight != null', 'SurfaceLost',
                'studio reservation restores current original alpha on native continuation or ownership loss: surface'),
            ('startup-original-alpha-lost', 'NativeMovieCameraPlane.cs', 'if (ReferenceEquals(player, _startupPlayer)) return _startupAlpha;',
                'if (ReferenceEquals(player, _startupPlayer)) return 0f;', 'Run',
                'automatic startup left head render shows only its recorded left eye'),
            ('startup-persistent-lease-lost', 'NativeMovieCameraPlane.cs', '    internal static void AfterRender(Camera camera)\n    {',
                '    internal static void AfterRender(Camera camera)\n    {\n        ReleaseStartup();', 'Run',
                'persistent startup plane stays hidden between renders while replacement retains original alpha'),
            ('startup-release-lost', 'NativeMovieCameraPlane.cs', '|| QuestStandalonePlatform.Enabled && FlatScreen.OwnsCapturedMoviePlane(player))) { ReleaseStartup(); return; }', '|| QuestStandalonePlatform.Enabled && FlatScreen.OwnsCapturedMoviePlane(player))) { return; }', 'Run',
                'studio reservation restores current original alpha on native continuation or ownership loss: deactivate'),
            ('startup-native-alpha-lost', 'NativeMovieCameraPlane.cs', '_startupAlpha = player.targetCameraAlpha; // Preserve a later native fade.',
                '/* injected: later native fade is discarded */', 'Run',
                'later native alpha write updates replacement appearance without reopening packed plane'),
            ('startup-error-release-lost', 'NativeMovieCameraPlane.cs', '_failedPlayer = player; _failedUrl = global::MovieSourceFixture.GetUrl(player);\n            ReleaseStartup();',
                '_failedPlayer = player; _failedUrl = global::MovieSourceFixture.GetUrl(player);\n            /* injected: failed native source retains the persistent reservation */', 'Error',
                'studio reservation restores current original alpha on native continuation or ownership loss: error'),
            ('startup-mono-fallback-lost', 'Movies.cs', '                    global::MovieSourceFixture.SetUrl(player, companion);',
                '                    global::MovieSourceFixture.SetUrl(player, stereo);', 'IntroDisabled',
                'unowned startup cannot hide a native plane or expose a packed source: intro'),
        ]
        variants += [
            ('startup-original-prefix', 'Movies.cs', 'if (IsStereoUrl(global::MovieSourceFixture.GetUrl(player)) && !FlatScreen.TryPrepareStartupMovie(player))',
                'if (player.frame == long.MinValue)', 'Run',
                'automatic studio prefix prepares the actual capture and stereo lifecycle without options or a scenario board'),
            ('startup-throwing-readiness', 'CaptureStack.cs', '            if (!owner._visible) owner.Tick();',
                '            if (!owner._visible) throw new System.InvalidOperationException(\"injected readiness failure\");', 'ThrowingReadiness', ''),
            ('startup-throw-escape', 'CaptureStack.cs', '            if (!owner._visible) owner.Tick();',
                '            if (!owner._visible) throw new System.InvalidOperationException(\"injected readiness failure\");', 'ThrowingReadiness',
                'throwing startup readiness cannot escape the prefix and skip native Play or continuation')]
        for name, filename, before, after, method, expected in variants:
            case = run / name
            production = case / 'production'; production.mkdir(parents=True)
            for filename_actual, code in startup.items():
                if filename_actual == filename:
                    code = replace_once(code, before, after)
                    if name == 'startup-throw-escape':
                        code = replace_once(code, 'catch (System.Exception error)', 'catch (System.NotSupportedException error)')
                (production / filename_actual).write_text(code)
            csproj = case / 'Startup.csproj'; shutil.copyfile(startup_fixture / 'Startup.csproj', csproj)
            assembly = 'StereoMovie_' + name.replace('-', '_')
            if execute([dotnet, 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo', '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false',
                    '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(startup_fixture), '-p:ProductionDir=' + str(production),
                    '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed'),
                    '-p:StereoBundle=' + str(ROOT / 'src/GloomhavenVR/Assets/StereoVideo/ui-sbs.bundle')], case / 'build.log', 120):
                raise RuntimeError('Startup fixture compilation failure is not a negative-control pass: ' + str(case))
            retained = run / 'assemblies' / (assembly + '.dll'); shutil.copyfile(case / 'bin/Release/net472' / retained.name, retained)
            dependencies = run / 'dependencies'; dependencies.mkdir(exist_ok=True)
            for dependency in (case / 'bin/Release/net472').glob('*.dll'):
                if dependency.name != retained.name: shutil.copyfile(dependency, dependencies / dependency.name)
            manifest['cases'].append({'name': name, 'dll': str(retained), 'expected': expected, 'method': method})
        manifest['dependencies'] = str(run / 'dependencies')
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in [*(fixture.rglob('*.cs')), fixture / 'StereoMovieRender.csproj', startup_fixture / 'Startup.csproj', Path(__file__),
                ROOT / 'scripts/stereo-movie-render/Editor/StereoVideoRunner.cs', ROOT / 'src/GloomhavenVR/Net/VideoWindowState.cs',
                ROOT / 'src/GloomhavenVR/Defaults/Defaults.WorldUI.cs',
                *[source_root / name for name in ('FlatScreen.1.Core.cs', 'FlatScreen.2.CameraStack.cs', 'FlatScreen.4.Lifecycle.cs',
                    'FlatScreen.7.CaptureRender.cs', 'FlatScreenStereo.2.Compositor.cs', 'FlatScreenStereo.4.PerEye.cs',
                    'FlatScreenStereo.5.Movie.cs', 'NativeMovieCameraPlane.cs', 'NativeVideoMovies.cs', 'NativeEmbeddedMoviePatches.cs', 'StereoVideoSurface.cs')]]},
            'boundaries': 'Decoder readiness/pixels and XR eye input. Startup additionally supplies decoder URL metadata after attempting the actual setter: the private text placeholder reads back empty, cause unknown. Production parsing/admission are unchanged. Startup adds actual stereo constructor/config binding, native source admission, Show/Tick/capture/split/stereo lifecycle. FlatScreen constructor is a minimal ownership port pinned to its actual owner assignment. Nonmovie scene services and physical quad pose are explicit fixture ports; final-cull Harmony seam is covered separately by desktop-render-runtime; actual built-in fallback Camera.Render executes here.',
            'limits': 'No native MOV decode or native URL transport/admission, headset/XR provider, D3D11, comfort, audio, multiplayer or native plane latch timing evidence. Production finite-key admission is exercised with supplied decoder metadata.'}, indent=2) + '\n')
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
