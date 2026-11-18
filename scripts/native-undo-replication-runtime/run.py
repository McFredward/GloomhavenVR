#!/usr/bin/env python3
"""Exercise publisher item confirmation and targeting Undo callbacks on original Unity IL."""
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
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def execute(command, log, env, timeout=240):
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT,
                                 start_new_session=True, env=env)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def game_plugin(path):
    return path.name not in ('UnityEngine.dll', 'mscorlib.dll', 'netstandard.dll') and (
        not path.name.startswith(('UnityEngine.', 'System.')) or path.name in (
            'UnityEngine.UI.dll', 'System.Runtime.CompilerServices.Unsafe.dll'))


def pin_inputs(paths):
    pins = {}
    for path in paths:
        with path.open('rb') as contents:
            pins[str(path)] = hashlib.file_digest(contents, 'sha256').hexdigest()
    return pins


def complete(run, project, unity, status, completion, started):
    editors = load_module('project_compilers', ROOT / 'scripts/menu-depth-character/project_compilers.py')
    compilers = load_module('environment_menu_scope', ROOT / 'scripts/check-environment-menu-scope.py')
    try:
        editor_cleanup = editors.cleanup_project_editors(project, unity, launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editor_cleanup, indent=2) + '\n')
        if editor_cleanup['remaining']:
            raise RuntimeError('Private Unity editor exit unresolved: ' + str(run))
        compiler_cleanup = compilers.cleanup_run_compilers(run, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compiler_cleanup, indent=2) + '\n')
        if compiler_cleanup['remaining'] or compiler_cleanup['refused']:
            raise RuntimeError('Private Unity compiler ownership or exit unresolved: ' + str(run))
    except BaseException:
        (run / '.keep').write_text('Private editor/compiler cleanup requires review; preserve active project caches.\n')
        raise
    cleanup = cleanup_completed_run('native-undo-replication-runtime', status, completion, ROOT, process_reaped=True)
    (run / 'cleanup.json').write_text(json.dumps({
        'project': cleanup, 'seconds': time.monotonic() - started, 'exit_code': status,
    }, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/native-undo-replication-runtime')
    args = parser.parse_args()
    game = args.source_root.resolve() / 'ressources/GH_Data/Managed'
    native = ('GH.Runtime.dll', 'GH.Runtime.FirstPass.dll', 'ScenarioRuleLibrary.dll')
    for required in (args.unity, *(game / dll for dll in native)):
        if not required.is_file():
            parser.error('Missing original runtime dependency: ' + str(required))
    out = args.output_dir.resolve()
    out.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    started = time.monotonic()
    status = 1
    project = run / 'unity'
    home = Path.home()
    env = dict(os.environ)
    own_adb = not env.get('ADB_SERVER_SOCKET')
    if own_adb:
        env['ADB_SERVER_SOCKET'] = 'localfilesystem:/tmp/gvr-undo-' + run.name + '.sock'
        env['ADB_MDNS_AUTO_CONNECT'] = ''
    deps = [
        ('harmonyx', '2.7.0', 'net45', '0Harmony'),
        ('monomod.runtimedetour', '21.12.13.1', 'net452', 'MonoMod.RuntimeDetour'),
        ('monomod.utils', '21.12.13.1', 'net452', 'MonoMod.Utils'),
        ('mono.cecil', '0.11.4', 'net40', 'Mono.Cecil'),
    ]
    dependency_files = [home / '.nuget/packages' / package / version / 'lib' / framework / (name + '.dll')
                        for package, version, framework, name in deps]
    common = ROOT / 'scripts/native-bugfix-cards-runtime'
    fixture = Path(__file__).resolve().parent
    production_file = args.source_root.resolve() / 'src/GloomhavenVR/Board/Patches/NativeUndoReplication.cs'
    try:
        original = production_file.read_text()
        runner = common / 'Editor/CardsRunner.cs'
        csproj = common / 'CardsRestore.csproj'
        plugins = sorted(path for path in game.glob('*.dll') if game_plugin(path))
        unity_managed = args.unity.parent / 'Data/Managed'
        unity_assemblies = sorted((unity_managed / 'UnityEngine').glob('*.dll'))
        inputs = [production_file, runner, csproj, fixture / 'Program.cs', Path(__file__),
                  args.unity, *plugins, *unity_assemblies, *dependency_files]
        pins = pin_inputs(inputs)
        (run / 'source-hashes.json').write_text(json.dumps(pins, indent=2) + '\n')
        manifest = {'result': str(run / 'results.txt'), 'cases': []}
        before = '__instance.SkipNextNetworkAction = false;'
        if original.count(before) != 1:
            raise RuntimeError('The causal mutation seam changed')
        cases = [
            ('production', original, ''),
            ('orphan-preserved', original.replace(before, '_ = __instance.SkipNextNetworkAction;'),
             'genuine targeting undo sends exactly one original UndoAction packet'),
        ]
        for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'):
            (project / directory).mkdir(parents=True, exist_ok=True)
        for name, code, expected in cases:
            build = run / name
            production = build / 'production'
            production.mkdir(parents=True)
            (production / production_file.name).write_text('#nullable enable\n' + code)
            shutil.copyfile(csproj, build / csproj.name)
            assembly = 'NativeUndoReplication_' + name.replace('-', '_')
            log = build / 'build.log'
            command = [str(home / '.dotnet/dotnet'), 'build', str(build / csproj.name), '-c', 'Release',
                       '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false',
                       '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                       '-p:UnityManaged=' + str(unity_managed),
                       '-p:GameManaged=' + str(game), '-p:HarmonyPath=' + str(dependency_files[0])]
            if execute(command, log, env):
                raise RuntimeError(log.read_text())
            dll = build / 'bin/Release/netstandard2.1' / (assembly + '.dll')
            shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
        for dll in plugins:
            shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
        for dll in dependency_files:
            shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
        shutil.copyfile(runner, project / 'Assets/Editor' / runner.name)
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.' + name: '1.0.0'
            for name in ('ui', 'imgui', 'animation', 'assetbundle', 'physics', 'audio', 'particlesystem')}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        manifest_path = run / 'manifest.json'
        manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                   '-executeMethod', 'CardsRunner.Start', '-interactionManifest', str(manifest_path),
                   '-nativeGameManaged', str(game), '-nativeHarmony', str(project / 'Assets/Plugins/0Harmony.dll'),
                   '-logFile', str(run / 'unity.log')]
        if not env.get('DISPLAY'):
            command = ['xvfb-run', '-a', *command]
        result = execute(command, run / 'launcher.log', env)
        receipt = Path(manifest['result'])
        output = receipt.read_text() if receipt.is_file() else ''
        print(output, end='')
        if result or 'PASS production:' not in output or 'PASS negative control orphan-preserved:' not in output:
            raise RuntimeError('Actual Unity proof failed; see ' + str(run / 'unity.log'))
        if pin_inputs(inputs) != pins:
            raise RuntimeError('A pinned source or runtime dependency changed during proof')
        status = 0
    finally:
        if own_adb and shutil.which('adb'):
            adb = subprocess.run(['adb', 'kill-server'], env=env, capture_output=True, text=True, timeout=20)
            (run / 'adb-cleanup.log').write_text(adb.stdout + adb.stderr)
        complete(run, project, args.unity, status, completion, started)
        print('Evidence: ' + str(run))


if __name__ == '__main__':
    main()
