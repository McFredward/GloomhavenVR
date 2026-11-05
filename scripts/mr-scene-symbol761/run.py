#!/usr/bin/env python3
"""Execute original inactive native actor types and the backing sweep's exact gates."""
import argparse
import hashlib
import importlib
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
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def condition(source, prefix):
    start = source.index(prefix) + len('if (')
    depth = 1
    for end in range(start, len(source)):
        depth += (source[end] == '(') - (source[end] == ')')
        if depth == 0:
            return source[start:end]
    raise RuntimeError('Unterminated backing gate')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('A private ADB_SERVER_SOCKET is required')
    root = args.source_root.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    log = run / 'completion.log'
    log.write_text('Native scene-symbol admission/retirement; no gameplay bootstrap or pixel claim.\n')
    source_path = root / 'src/GloomhavenVR/WorldUI/MrBacking.cs'
    policy_path = source_path.with_name('MrBackingSceneSymbols.cs')
    source = source_path.read_text()
    retire = condition(source, 'if (!alive || p.MrBackingSuppressed')
    admission = condition(source, 'if (host == null || panel.HostGo == null || panel.MrBackingSuppressed')
    scopes = ('|| MrBackingSceneSymbols.Contains(p.Target)', '|| MrBackingSceneSymbols.Contains(panel.Target)')
    assert scopes[0] in retire and scopes[1] in admission
    game = root / 'ressources/GH_Data/Managed'
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    unity_managed = unity.parent / 'Data/Managed'
    pins = {str(path): digest(path) for path in [source_path, policy_path, *HERE.glob('*.*'), game / 'GH.Runtime.dll'] if path.is_file()}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    (run / 'original-backing-sweep.cs').write_text(source)
    process = None
    reaped = False
    status = 1
    sys.path.insert(0, str(root / 'scripts'))
    cleanup = importlib.import_module('town_run_retention').cleanup_completed_run
    def module(name, path):
        spec = importlib.util.spec_from_file_location(name, path)
        value = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(value)
        return value
    editors = module('symbol_editors761', root / 'scripts/menu-depth-character/project_compilers.py')
    compilers = module('symbol_compilers761', root / 'scripts/check-environment-menu-scope.py')
    try:
        manifest = {'result': str(run / 'results.txt'), 'managed': str(game), 'cases': []}
        variants = [('production', retire, admission, ''),
                    ('old-retirement', retire.replace(scopes[0], ''), admission, 'scene symbol retires an existing readability plate'),
                    ('old-admission', retire, admission.replace(scopes[1], ''), 'scene symbol cannot acquire new readability geometry')]
        for name, gate1, gate2, expected in variants:
            build = run / name
            production = build / 'production'
            production.mkdir(parents=True)
            shutil.copyfile(policy_path, production / policy_path.name)
            (production / 'SweepGates.cs').write_text('''using UnityEngine;
namespace GloomhavenVR.WorldUI {
internal sealed class ConvertedPanel {
 internal RectTransform? Target; internal bool MrBackingSuppressed;
 internal RectTransform? HostRect => Target; internal GameObject? HostGo => Target != null ? Target.gameObject : null;
}
internal static class BackingGate761 {
 internal static bool Retire(bool alive, ConvertedPanel p) => ''' + gate1 + ''';
 internal static bool Refuse(RectTransform? host, ConvertedPanel panel) => ''' + gate2 + ''';
}}
''')
            project = build / 'Fixture.csproj'
            shutil.copyfile(HERE / 'Fixture.csproj', project)
            command = [str(Path.home() / '.dotnet/dotnet'), 'build', str(project), '-c', 'Release', '-v:q',
                       '-p:UseSharedCompilation=false', '-p:CaseName=SceneSymbol761_' + name.replace('-', '_'),
                       '-p:FixtureDir=' + str(HERE), '-p:ProductionDir=' + str(production),
                       '-p:UnityManaged=' + str(unity_managed), '-p:GameManaged=' + str(game)]
            with (build / 'build.log').open('w') as stream:
                subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, check=True, timeout=120)
            dll = build / 'bin/Release/netstandard2.1' / ('SceneSymbol761_' + name.replace('-', '_') + '.dll')
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
        project = run / 'owned-unity-project'
        (project / 'Assets/Editor').mkdir(parents=True)
        shutil.copyfile(HERE / 'Runner.cs', project / 'Assets/Editor/Runner.cs')
        (project / 'Packages').mkdir()
        (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
        (project / 'ProjectSettings').mkdir()
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        manifest_path = run / 'manifest.json'
        manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = ['xvfb-run', '-a', str(unity), '-batchmode', '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'SceneSymbolRunner761.Start', '-symbolManifest', str(manifest_path), '-logFile', str(run / 'unity.log')]
        with (run / 'launcher.log').open('w') as stream:
            process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = process.wait(timeout=180)
            finally:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
                reaped = True
        report = run / 'results.txt'
        assert all(digest(Path(path)) == pin for path, pin in pins.items()), 'Inputs changed during native run'
        text = report.read_text() if report.is_file() else ''
        print(text, end='')
        lines = [line for line in text.splitlines() if line.startswith('PASS ')]
        assert code == 0 and len(lines) == 3 and not any(line.startswith('FAIL ') for line in text.splitlines()), 'Native component fixture failed: ' + str(run)
        status = 0
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            reaped = True
        if process is not None:
            editor = editors.cleanup_project_editors(run / 'owned-unity-project', unity, launcher_reaped=reaped)
            (run / 'editor-cleanup.json').write_text(json.dumps(editor, indent=2) + '\n')
            if editor['remaining']: raise RuntimeError('Private editor cleanup incomplete')
        compiler = compilers.cleanup_run_compilers(run, editor_reaped=process is None or reaped)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compiler, indent=2) + '\n')
        if compiler['remaining'] or compiler['refused']: raise RuntimeError('Private compiler cleanup incomplete')
        receipt = cleanup('mr-scene-symbol761', status, log, root, process_reaped=process is None or reaped)
        project = run / 'owned-unity-project'
        if project.is_dir() and not project.is_symlink(): shutil.rmtree(project)
        (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        (run / 'results.json').write_text(json.dumps({'passed': status == 0, 'cases': 3,
            'scope': 'Original inactive GH.Runtime actor components and exact backing sweep gates; no game bootstrap, full sweep/layout or head-pixel claim.'}, indent=2) + '\n')
    print('Evidence: ' + str(run))
    return status


if __name__ == '__main__':
    raise SystemExit(main())
