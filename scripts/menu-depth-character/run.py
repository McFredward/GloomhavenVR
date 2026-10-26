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
from project_compilers import cleanup_project_compilers


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
    parser.add_argument('--api-root', type=Path, default=ROOT,
        help='Read the unchanged shared capture contract from the integration checkout while testing private character sources.')
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    args = parser.parse_args()
    output = ROOT / '.planning/debug/menu-depth-character'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    code_root = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth'
    names = ('IMenuDepthSource.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs', 'MenuDepthProjection.cs',
             'MenuDepthCharacters.cs', 'MenuDepthCharacterSource.cs', 'MenuDepthCharacterProjection.cs',
             'MenuDepthCharacterDisplayObserver.cs')
    api_names = ('IMenuDepthSource.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs', 'MenuDepthProjection.cs')
    inputs = {name: (args.api_root / 'src/GloomhavenVR/WorldUI/MenuDepth' / name)
              if name in api_names else code_root / name for name in names}
    sources = {name: path.read_text() for name, path in inputs.items()}
    fixture = ROOT / 'tests/GloomhavenVR.MenuDepthCharacterTests'
    pins = {str(path): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in [*inputs.values(), *fixture.glob('*'), Path(__file__),
                         ROOT / 'scripts/menu-depth-character/Editor/Runner.cs'] if path.is_file()}
    variants = [
        ('production', '', '', '', 'Run', ''),
        ('native-lifecycle', '', '', '', 'Lifecycle', ''),
        ('native-close-during-render', '', '', '', 'NativeCloseDuringRender', ''),
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
         'native RawImage UV cropping is preserved without double-cropping the portal'),
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
         '&& !_readHidden(_manager);',
         ';', 'Run',
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
         'DisplayRequests[key] = target;\n        RetireStale();\n        Discover();',
         'DisplayRequests[key] = target;\n        RetireStale(); /* injected: new request misses pre-render discovery */', 'Lifecycle',
         'the actual native Display postfix binds a new window before its first visible render'),
        ('render-disposal-not-deferred', 'MenuDepthCharacterSource.cs',
         'if (!_rendering) ReleaseTargets();', 'ReleaseTargets();', 'NativeCloseDuringRender',
         'shutdown during the actual native image effect defers owned-target destruction until the original draw returns'),
    ]
    if args.case:
        selected = set(args.case)
        assert selected <= {case[0] for case in variants}, selected
        variants = [case for case in variants if case[0] in selected]
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'harmony': str(run / 'assemblies/0Harmony.dll'), 'cases': []}
    exit_code = 1
    try:
        project = run / 'unity'; editor = project / 'Assets/Editor'; editor.mkdir(parents=True)
        (project / 'ProjectSettings').mkdir()
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages').mkdir()
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0'}}))
        shutil.copyfile(ROOT / 'scripts/menu-depth-character/Editor/Runner.cs', editor / 'Runner.cs')
        (run / 'source-pins.json').write_text(json.dumps({'sha256': pins,
            'boundaries': 'Native model/content/manager state and supplied XR eye positions; original Unity camera/image-effect/UI raster and production discovery/projection/leases run.',
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
                '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed')]
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
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Private project compiler processes remain; retaining project caches: ' + str(run))
        receipt = cleanup_completed_run('menu-depth-character', exit_code, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return exit_code


if __name__ == '__main__':
    raise SystemExit(main())
