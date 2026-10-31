#!/usr/bin/env python3
"""Prove production Characters enrollment -> native uGUI drag -> Source render yaw.

The surrounding registry/shader ports do not supply enrollment,
readiness, a view, a yaw callback, a render or replacement rotation mathematics.
Native manager state and asymmetric original model meshes are explicit inputs.
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
import sys
import tempfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('rotation_input_proof', Path(__file__).with_name('run.py'))
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)


def mutate_capture(source, before, after):
    # The original capture and the spatial ray query both consume presentation
    # yaw. This render control must mutate only the original capture method.
    signature = 'private void Capture(MenuDepthEye eye, bool direct)'
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    capture = source[start:end]
    assert capture.count(before) == 1, before
    return source[:start] + capture.replace(before, after, 1) + source[end:]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append')
    parser.add_argument('--api-root', type=Path, default=ROOT)
    parser.add_argument('--api-commit', help='Read a frozen integration commit without changing either checkout.')
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Use the task-private ADB server.')
    signal.signal(signal.SIGTERM, lambda signum, frame: (_ for _ in ()).throw(SystemExit(128 + signum)))
    base = 'src/GloomhavenVR/WorldUI/MenuDepth/'
    names = ['MenuDepthCharacterRotation.cs', 'MenuDepthCharacters.cs', 'MenuDepthCharacterSource.cs',
             'MenuDepthCharacterProjection.cs', 'MenuDepthEye.cs', 'IMenuDepthSource.cs', 'MenuDepthProjection.cs',
             'MenuDepthHeadRaster.cs']
    relative = [base + name for name in names] + ['src/GloomhavenVR/Hands/Interact/UguiPointer.cs']

    def read(path):
        if args.api_commit:
            return subprocess.check_output(['git', '-C', str(args.api_root), 'show', args.api_commit + ':' + path])
        return (args.api_root / path).read_bytes()

    inputs = {path: read(path) for path in relative}
    sources = {Path(path).name: content.decode() for path, content in inputs.items() if path.startswith(base)}
    sources['Pointer.cs'] = shared.pointer_source(inputs[relative[-1]].decode())
    cases = [
        ('production', '', '', '', ''),
        ('lost-enrollment', 'MenuDepthCharacters.cs',
         'MenuDepthCharacterRotation.Bind(image, manager.Original, () => source.ReadyIdentity);',
         '/* enrollment no longer binds the original portrait */',
         'real Characters enrollment attaches the original portrait rotation receiver'),
        ('lost-yaw-provider', 'MenuDepthCharacters.cs',
         'MenuDepthCharacterSource.YawProvider = MenuDepthCharacterRotation.GetYaw;',
         'MenuDepthCharacterSource.YawProvider = null;',
         'real Characters pointer yawprovider and Source render reveal the original back marker'),
        ('lost-capture-yaw', 'MenuDepthCharacterSource.cs',
         'float yaw = YawProvider?.Invoke(_image, identity) ?? 0f;',
         'float yaw = 0f;',
         'real Characters pointer yawprovider and Source render reveal the original back marker'),
        ('lost-source-ground', 'MenuDepthCharacterSource.cs',
         'Vector3 ground = _camera.transform.TransformPoint(origin);',
         'Vector3 ground = _camera.transform.TransformPoint(origin) + _camera.transform.right * .1f;',
         'integrated Source rotation keeps the actual main ground fixed in the measured native camera view'),
        ('android-aot', '', '', '', ''),
        ('android-aot-hidden-emit-lost', 'MenuDepthCharacters.cs',
         'if (Application.platform == RuntimePlatform.Android)',
         'if (Application.platform != RuntimePlatform.Android)',
         'real Characters enrollment registers exactly its native camera target consumer'),
        ('android-aot-original-emit-lost', 'MenuDepthCharacterSource.cs',
         'if (Application.platform == RuntimePlatform.Android)',
         'if (Application.platform != RuntimePlatform.Android)',
         'Reflection.Emit is forbidden in the selected Android AOT character path'),
        ('android-aot-hidden-value-lost', 'MenuDepthCharacters.cs',
         'return manager => (bool)field.GetValue(manager)!;',
         'return manager => false;',
         'native hide revokes actual integrated Source readiness'),
        ('android-aot-original-value-lost', 'MenuDepthCharacterSource.cs',
         'return manager => field.GetValue(manager);',
         'return manager => manager;',
         'real Characters enrollment uses actual Source readiness and original native identity'),
    ]
    if args.case:
        selected = set(args.case); assert selected <= {row[0] for row in cases}
        cases = [row for row in cases if row[0] in selected]
    output = ROOT / '.planning/debug/menu-depth-rotation'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    project = run / 'unity'; status = 1
    fixture = ROOT / 'tests/GloomhavenVR.MenuDepthRotationTests/integration'
    local_paths = [*fixture.glob('*'), Path(__file__), Path(__file__).with_name('run.py'),
                   ROOT / 'scripts/menu-depth-rotation/Editor/Runner.cs']
    local_pins = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in local_paths if path.is_file()}
    pins = {'integration_root': str(args.api_root), 'integration_commit': args.api_commit,
            'production_sha256': {path: hashlib.sha256(content).hexdigest() for path, content in inputs.items()},
            'fixture_sha256': local_pins, 'boundaries': __doc__}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    (run / 'launch-environment.json').write_text(json.dumps({key: os.environ.get(key)
        for key in ('ADB_SERVER_SOCKET', 'ADB_MDNS_AUTO_CONNECT')}, indent=2) + '\n')
    try:
        for folder in ['Assets/Editor', 'ProjectSettings', 'Packages']:
            (project / folder).mkdir(parents=True)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        shutil.copy2(ROOT / 'scripts/menu-depth-rotation/Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
        for name, filename, before, after, expected in cases:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for source_name, original in sources.items():
                code = original
                if source_name == filename:
                    if name in ('lost-capture-yaw', 'lost-source-ground'):
                        code = mutate_capture(code, before, after)
                    else:
                        assert code.count(before) == 1, before
                        code = code.replace(before, after, 1)
                if name.startswith('android-aot') and source_name in ('MenuDepthCharacters.cs', 'MenuDepthCharacterSource.cs'):
                    # Supply the platform observation only. The complete actual
                    # enrollment/readiness/render path must work while every
                    # attempted runtime-codegen constructor is rejected, as on
                    # IL2CPP. No field value or identity is supplied by this port.
                    assert code.count('Application.platform') == 1, source_name
                    assert code.count('new DynamicMethod(') == 1, source_name
                    code = code.replace('Application.platform', 'global::AndroidAotInput.Platform')
                    code = code.replace('new DynamicMethod(', 'global::AndroidAotInput.RejectEmit(')
                (production / source_name).write_text(code)
            private_fixture = case / 'fixture'; shutil.copytree(fixture, private_fixture)
            descriptor = case / 'Integrated.csproj'; shutil.copy2(fixture / descriptor.name, descriptor)
            assembly = 'MenuDepthRotationIntegrated_' + name.replace('-', '_')
            command = ['/home/claw/.dotnet/dotnet', 'build', str(descriptor), '-c', 'Release', '-v', 'quiet', '--nologo',
                '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(private_fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed', '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed')]
            if shared.execute(command, case / 'build.log'):
                raise RuntimeError('Compilation failure is never a causal-control pass: ' + name)
            dll = assemblies / (assembly + '.dll'); shutil.copy2(case / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        status = shared.execute(['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
            '-projectPath', str(project), '-executeMethod', 'MenuDepthRotationRunner.Start', '-menuDepthRotationManifest',
            str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')], run / 'launch.log', 240)
        stable = all(read(path) == content for path, content in inputs.items()) and all(
            hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in local_pins.items())
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(inputs) + len(local_pins)}) + '\n')
        if not stable: status = 1
        if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
        if status: raise RuntimeError('Integrated character rotation proof failed: ' + str(run))
    finally:
        (run / 'exit-code.txt').write_text(str(status) + '\n')
        editors = shared.cleanup_project_editors(project, Path('/home/claw/unity-2021.3.5/Editor/Unity'), launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']: raise RuntimeError('Owned editor remains; retaining caches.')
        compilers = shared.cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']: raise RuntimeError('Owned compiler remains; retaining caches.')
        retained_editor = run / 'assemblies/editor'; retained_editor.mkdir(parents=True, exist_ok=True)
        editor_hashes = {}
        for compiled in (project / 'Library/ScriptAssemblies').glob('*'):
            if compiled.is_file() and compiled.suffix in ('.dll', '.pdb', '.mdb'):
                shutil.copy2(compiled, retained_editor / compiled.name)
                editor_hashes[compiled.name] = hashlib.sha256(compiled.read_bytes()).hexdigest()
        (run / 'editor-assemblies.json').write_text(json.dumps(editor_hashes, indent=2) + '\n')
        cleanup = shared.cleanup_completed_run('menu-depth-rotation', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    return status


if __name__ == '__main__':
    raise SystemExit(main())
