#!/usr/bin/env python3
"""Exercise original native figure/audio producers and real Unity audio sources.

The original GH.Runtime.dll executes unchanged with exact production observer/follower
sources and Harmony registration. Native actor association/hierarchy are used read-only;
its unrelated gameplay Update is disabled. Native audio uses synthetic probe waveforms
and actual AudioController/AudioObject/AudioSources, loop, delay, stop and pool reuse.
This does not qualify a physical HMD/audio device or network choreography. Original
native serialized summon/item/source evidence is recorded separately by the asset audit.
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

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers, cleanup_project_editors


def execute(command, log, timeout=180):
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()



def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/figure-audio-runtime')
    parser.add_argument('--case', action='append', help='Partial development run; repeat to select cases')
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Set a private ADB_SERVER_SOCKET before starting Unity.')
    root = args.source_root.resolve()
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    game = root / 'ressources/GH_Data'
    fixture = root / 'scripts/figure-audio-runtime'
    sources = {
        'NativeFigureAudio.cs': root / 'src/GloomhavenVR/Core/Sound/NativeFigureAudio.cs',
        'NativeFigureAudioPatches.cs': root / 'src/GloomhavenVR/Core/Patches/NativeFigureAudioPatches.cs',
    }
    original = {name: path.read_text() for name, path in sources.items()}
    variants = [
        ('production', '', '', '', ''),
        ('original-detached-source', 'NativeFigureAudio.cs', 'Source.transform.position = target;',
            '/* original detached position remains stationary */', 'persistent original source follows moving/animated figure emitter'),
        ('pooled-generation-reused', 'NativeFigureAudio.cs', 'if (follower != null) follower.Reused();',
            '/* injected: unscoped reuse retains old figure ownership */', 'same-frame same-item reuse before outer finalizer rejects stale generation'),
        ('stale-actor-owner', 'NativeFigureAudio.cs', '&& ReferenceEquals(owner.Figure.Actor, owner.Actor)',
            '', 'actor pooling/reassignment revokes old emitter receipt'),
        ('vr-off-producer-captured', 'NativeFigureAudio.cs', 'VRSession.IsRunning',
            'true', 'VR-off original figure audio remains untouched'),
    ]
    if args.case:
        unknown = set(args.case) - {v[0] for v in variants}
        if unknown:
            parser.error('Unknown cases: ' + str(unknown))
        variants = [v for v in variants if v[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    project = run / 'unity'
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    print('evidence: ' + str(run), flush=True)
    status = 1
    pins = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in [
        *sources.values(), Path(__file__), *fixture.rglob('*.cs'), *fixture.glob('*.csproj'),
        game / 'Managed/GH.Runtime.dll', game / 'Managed/GH.Runtime.FirstPass.dll',
        root / 'decompiled/GH.Runtime/SFXOnEnable.cs', root / 'decompiled/GH.Runtime/AudioEffectPlayer.cs',
        root / 'decompiled/GH.Runtime/FootstepSound.cs', root / 'decompiled/GH.Runtime/ClockStone/PlayAudio.cs',
        root / 'decompiled/GH.Runtime/AudioController.cs', root / 'decompiled/GH.Runtime/ClockStone/AudioObject.cs',
        root / 'decompiled/GH.Runtime/ClockStone/AudioItem.cs', root / 'decompiled/GH.Runtime/ActorBehaviour.cs',
    ]}
    try:
        retained = run / 'Sources'; retained.mkdir()
        shutil.copytree(fixture, retained / 'fixture')
        shutil.copyfile(Path(__file__), retained / Path(__file__).name)
        for path in sources.values():
            shutil.copyfile(path, retained / path.name)
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': pins, 'scope': __doc__,
            'subset': args.case, 'ports': ['native ActorBehaviour gameplay Update disabled', 'read-only native Actor association', 'synthetic probe waveform', 'audio bootstrap only']}, indent=2) + '\n')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        manifest = {'evidence': str(run), 'result': str(run / 'results.txt'),
            'gameManaged': str((game / 'Managed').resolve()), 'dependencies': str(assemblies), 'cases': []}
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        for name, filename, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for target, code in original.items():
                if target == filename:
                    assert code.count(before) == (2 if name == 'vr-off-producer-captured' else 1), 'Mutation binding drift: ' + name
                    code = code.replace(before, after)
                (production / target).write_text(code)
            csproj = case / 'FigureAudio.csproj'; shutil.copyfile(retained / 'fixture/FigureAudio.csproj', csproj)
            assembly = 'FigureAudio_' + name.replace('-', '_')
            command = [dotnet, 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo',
                '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(retained / 'fixture'), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:GameManaged=' + str((game / 'Managed').resolve())]
            if execute(command, case / 'build.log'):
                raise RuntimeError('Fixture compilation failure is not a successful negative control: ' + name)
            binary = case / 'bin/Release/netstandard2.1' / (assembly + '.dll')
            shutil.copyfile(binary, assemblies / binary.name)
            for dependency in binary.parent.glob('*.dll'):
                if dependency.name != binary.name:
                    shutil.copyfile(dependency, assemblies / dependency.name)
            manifest['cases'].append({'name': name, 'dll': str(assemblies / binary.name), 'expected': expected})
        for directory in ['Assets/Editor', 'Packages', 'ProjectSettings']:
            (project / directory).mkdir(parents=True)
        shutil.copyfile(retained / 'fixture/Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {
            'com.unity.modules.audio': '1.0.0', 'com.unity.modules.animation': '1.0.0',
            'com.unity.modules.physics': '1.0.0', 'com.unity.modules.ui': '1.0.0',
            'com.unity.modules.imgui': '1.0.0', 'com.unity.modules.assetbundle': '1.0.0',
            'com.unity.modules.particlesystem': '1.0.0'}}) + '\n')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        status = execute([str(unity), '-batchmode', '-nographics', '-forceAudio', '-disable-assembly-updater',
            '-projectPath', str(project), '-executeMethod', 'FigureAudioRunner.Start',
            '-figureAudioManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')], run / 'launch.log', 360)
        stable = all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in pins.items())
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(pins)}) + '\n')
        if not stable:
            status = 1
        if (run / 'results.txt').exists():
            print((run / 'results.txt').read_text(), end='')
        if status or not (run / 'results.txt').exists():
            status = 1
            raise RuntimeError('Actual native figure audio proof failed: ' + str(run))
        print('PASS selected native figure/audio cases: ' + str(len(variants)) + ' (' + str(sum(v[0] != 'production' for v in variants)) + ' causal controls)')
    finally:
        editors = cleanup_project_editors(project, unity, launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']:
            raise RuntimeError('Owned editor remains; caches retained')
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Owned project compiler remains; caches retained')
        cleanup = cleanup_completed_run('figure-audio-runtime', status, completion, root, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    return status


if __name__ == '__main__':
    raise SystemExit(main())
