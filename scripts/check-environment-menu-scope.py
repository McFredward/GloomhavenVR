#!/usr/bin/env python3
"""Execute the whole custom-environment owner in Unity across menu/map/scenario rigs.

Native menu readiness/config/audio/gameplay boundaries are explicit surrogates.
Actual shipping RoomGeo/PlaySpace meshes prove spawn clearance, with a declared
GL material witness; synthetic rooms retain map/scenario placement controls.
Original shader appearance, Default sky, HMD timing and peer audio remain separate.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]


def cleanup_run_compilers(run, *, editor_reaped=False):
    """Retire only VBCSCompiler processes still borrowing this completed run.

    Unity Bee can launch Roslyn outside the editor process group with a global
    working directory and PWD. An open private Bee descriptor is the ownership
    proof in that case. A compiler with another private run open is left alone.
    pidfds bind signals to the rechecked process; no global daemon shutdown runs.
    """
    if editor_reaped is not True:
        raise ValueError('Reap the private editor before its compiler cleanup.')
    run = str(Path(run).absolute())
    if not hasattr(os, 'pidfd_open') or not hasattr(signal, 'pidfd_send_signal'):
        raise RuntimeError('Private compiler cleanup requires Linux pidfds.')
    result = {'run': run, 'editor_reaped': True, 'matched': [], 'refused': [],
              'term_sent': [], 'kill_sent': [], 'remaining': []}
    handles = {}

    def inside(path):
        return path == run or path.startswith(run + '/')

    def ownership(proc):
        try:
            if proc.stat().st_uid != os.geteuid():
                return None
            args = [a for a in (proc / 'cmdline').read_bytes().split(b'\0') if a]
            compilers = {b'VBCSCompiler.dll', b'VBCSCompiler.exe'}
            if not args or args[0].rsplit(b'/', 1)[-1] not in {b'dotnet', b'mono', b'mono-sgen', *compilers}:
                return None
            if not any(a.rsplit(b'/', 1)[-1] in compilers for a in args):
                return None
            cwd = os.readlink(proc / 'cwd').removesuffix(' (deleted)')
            files = []
            for fd in (proc / 'fd').iterdir():
                try:
                    files.append(os.readlink(fd).removesuffix(' (deleted)'))
                except FileNotFoundError:
                    continue
            owned = [path for path in [cwd, *files] if inside(path)]
            if not owned:
                return None
            other = [path for path in [cwd, *files]
                     if '/.planning/debug/' in path and not inside(path)]
            if other:
                return {'private_paths': owned, 'conflicting_private_paths': sorted(set(other))}
            return {'private_paths': sorted(set(owned)), 'conflicting_private_paths': []}
        except (OSError, ValueError):
            return None  # Exit/race/unknown ownership must never authorize a signal.

    def wait(pending, seconds):
        deadline = time.monotonic() + seconds
        while pending:
            ready, _, _ = select.select(pending, [], [], max(0, deadline - time.monotonic()))
            pending = [fd for fd in pending if fd not in ready]
            if time.monotonic() >= deadline:
                break
        return pending

    try:
        for proc in Path('/proc').iterdir():
            if not proc.name.isdecimal() or int(proc.name) == os.getpid():
                continue
            proof = ownership(proc)
            if proof is None:
                continue
            if proof['conflicting_private_paths']:
                result['refused'].append({'pid': int(proc.name), **proof})
                continue
            try:
                fd = os.pidfd_open(int(proc.name), 0)
            except ProcessLookupError:
                continue
            checked = ownership(proc)
            if checked is None or checked['conflicting_private_paths']:
                os.close(fd)
                continue
            handles[fd] = int(proc.name)
            result['matched'].append({'pid': int(proc.name), **checked})
        for fd, pid in handles.items():
            try:
                signal.pidfd_send_signal(fd, signal.SIGTERM)
                result['term_sent'].append(pid)
            except ProcessLookupError:
                pass
        pending = wait(list(handles), .4)
        for fd in pending:
            try:
                signal.pidfd_send_signal(fd, signal.SIGKILL)
                result['kill_sent'].append(handles[fd])
            except ProcessLookupError:
                pass
        result['remaining'] = [handles[fd] for fd in wait(pending, 1)]
        return result
    finally:
        for fd in handles:
            os.close(fd)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/environment-menu-scope')
    parser.add_argument('--production-only', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = root / 'scripts/environment-menu-scope'
    source_path = root / 'src/GloomhavenVR/Core/Environment/SkyAlternative.cs'
    environment_bank = root / 'prebuilt/gloomhavenvr.bundle'
    source = source_path.read_text()
    mutations = [
        ('intro-gate-lost', 'if (!Events.MenuWorldContext.AllowsSurroundings)',
         'if (bool.Parse("false"))', 'studio intro never reveals custom surroundings before the real menu is ready'),
        ('old-menu-gate', '        // OFF (BLACK) — the whole style, in full',
         '        if (!Events.VRModeStateMachine.TableInFrontOfPlayer) { Deactivate(); return false; }\n        // OFF (BLACK) — the whole style, in full',
         'selected custom room is visible in the main menu without a native board'),
        ('menu-subject-lost', 'if (Events.VRModeStateMachine.TableInFrontOfPlayer || !Events.MenuWorldContext.AllowsSurroundings\n            || rig == null || head == null)',
         'if (rig == null || head == null || !Events.VRModeStateMachine.TableInFrontOfPlayer)',
         'cold main-menu room completes its placement before reveal'),
        ('menu-floor-buried', 'undersideY = floor.y + FloatGapToBoardRatio * extent;',
         'undersideY = floor.y;', 'main-menu room floor matches tracking floor'),
        ('rig-identity-lost', 'if ((_skyGo != null || _roomGo != null) && !ReferenceEquals(_environmentRig, anchor))',
         'if (bool.Parse("false"))', 'a replacement map rig rebuilds the menu room at the map'),
        ('room-follows-rig', '            HealScaleDrift(_skyGo.transform, anchor);',
         '            _roomGo.transform.position = anchor.position;\n            HealScaleDrift(_skyGo.transform, anchor);',
         'menu room remains world-fixed under head and rig motion'),
        ('menu-overrides-native-table', 'if (Events.VRModeStateMachine.TableInFrontOfPlayer || !Events.MenuWorldContext.AllowsSurroundings\n            || rig == null || head == null)',
         'if (rig == null || head == null)', 'native map width keeps the existing room proportions'),
        ('closed-room-not-centered', '&& TryMenuClearance(room, out float authoredFloor, out float authoredCeiling)',
         '&& bool.Parse("false") && TryMenuClearance(room, out float authoredFloor, out float authoredCeiling)',
         'packaged cellar eye is vertically centered between its actual floor and ceiling'),
    ]
    cases = [('production', source, '')]
    for name, before, after, expected in mutations:
        if source.count(before) != 1:
            raise RuntimeError('Mutation source binding drift: ' + name)
        if not args.production_only:
            cases.append((name, source.replace(before, after), expected))
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
    paths = [source_path, environment_bank, Path(__file__), *fixture.rglob('*.cs'), fixture / 'EnvironmentScope.csproj']
    pins = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    code = 1; process = None; reaped = True
    # Unity Bee's Roslyn pipe name is SDK-derived. Give this suite a short private
    # Unix socket directory so a parallel editor cannot borrow this run's compiler.
    compiler_temp = Path(tempfile.mkdtemp(prefix='ghvr-envscope-', dir='/tmp'))
    (run / 'compiler-temp.json').write_text(json.dumps({'TMPDIR': str(compiler_temp)}) + '\n')
    env = dict(os.environ, DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
               UseSharedCompilation='false', TMPDIR=str(compiler_temp),
               GHVR_SCOPE_ENVIRONMENT_BUNDLE=str(environment_bank))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    try:
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
        assemblies = run / 'executed-assemblies'; assemblies.mkdir()
        for name, current, expected in cases:
            build = run / name; production = build / 'production'; production.mkdir(parents=True)
            (production / 'SkyAlternative.cs').write_text(current)
            project = build / 'EnvironmentScope.csproj'; shutil.copyfile(fixture / project.name, project)
            assembly = 'EnvironmentScope_' + name.replace('-', '_')
            command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                       '-p:UseSharedCompilation=false', '-nodeReuse:false', '-p:CaseName=' + assembly,
                       '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                       '-p:UnityManaged=' + str(unity.parent / 'Data/Managed')]
            result = subprocess.run(command, capture_output=True, text=True, env=env)
            (build / 'build.log').write_text(result.stdout + result.stderr)
            if result.returncode:
                raise RuntimeError('Compilation failure is not a successful defect control: ' + str(build / 'build.log'))
            dll = assemblies / (assembly + '.dll'); shutil.copyfile(build / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'expected': expected})
        project = run / 'unity'
        for path in ['Assets/Editor', 'Packages', 'ProjectSettings']:
            (project / path).mkdir(parents=True)
        shutil.copyfile(fixture / 'Editor/EnvironmentScopeRunner.cs', project / 'Assets/Editor/EnvironmentScopeRunner.cs')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {name: '1.0.0' for name in
            ['com.unity.modules.physics', 'com.unity.modules.particlesystem', 'com.unity.modules.imageconversion', 'com.unity.modules.jsonserialize', 'com.unity.modules.assetbundle']}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod',
                   'EnvironmentScopeRunner.Start', '-environmentScopeManifest', str(path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'):
            command = ['xvfb-run', '-a', *command]
        process = subprocess.Popen(command, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
        reaped = False
        try:
            status = process.wait(timeout=300)
        finally:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait(); reaped = True
        results = (run / 'results.txt').read_text() if (run / 'results.txt').exists() else 'Missing runtime results'
        print(results, end='', flush=True)
        if status or 'FAIL' in results or results.count('PASS') != len(cases):
            raise RuntimeError('Environment runtime failed: ' + str(run / 'unity.log'))
        assert all(p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest() == pins[str(p)] for p in paths), 'Execution input changed during run'
        code = 0
        print('PASS: ' + str(len(cases)) + ' actual environment lifecycle/pixel variants; ' + str(run), flush=True)
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL); process.wait(); reaped = True
        compilers = cleanup_run_compilers(run, editor_reaped=reaped)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining'] or compilers['refused']:
            # The central runner also performs completion cleanup. Pin this run
            # rather than let that outer cleanup delete another live borrower's data.
            (run / '.keep').write_text('Compiler ownership/exit unresolved; inspect compiler-cleanup.json.\n')
            raise RuntimeError('Private compiler ownership/exit unresolved; caches retained: ' + str(run))
        spec = importlib.util.spec_from_file_location('retention', ROOT / 'scripts/town_run_retention.py')
        retention = importlib.util.module_from_spec(spec); spec.loader.exec_module(retention)
        result = retention.cleanup_completed_run('environment-menu-scope', code, completion, ROOT, process_reaped=reaped)
        (run / 'cache-cleanup.json').write_text(json.dumps(result, indent=2) + '\n')
        shutil.rmtree(compiler_temp)
    return code


if __name__ == '__main__':
    raise SystemExit(main())
