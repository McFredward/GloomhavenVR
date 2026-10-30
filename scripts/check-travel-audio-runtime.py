#!/usr/bin/env python3
"""Exercise original native travel/event/audio producers and real Unity audio sources.

The original GH.Runtime.dll executes unchanged, with exact production observer/helper sources.
Only movement, camera, bootstrap and delayed arrival endpoints are harness ports. Native audio
uses synthetic probe waveforms and original loop/multiplicity settings, including actual DSP
scheduling, fades, sample cursors and AudioSource.isPlaying. This proves source lifetime and
ownership; it does not prove headset/device audio or full native gameplay/network choreography.
Probe items use MinTimeBetweenPlayCalls=0 to isolate native instance lifetime from throttles.
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


def native_items(game, output):
    """Decode only the source-ordered item prefix, not an inferred full controller tree.

    UnityPy's generated whole AudioController typetree cannot decode this native serialized
    table. Each item Name is length-prefixed; its fixed fields follow AudioItem declaration
    order with native four-byte alignment. stopEvents length is checked before any subitem
    data. The retained raw prefix and asset/object hashes make every reading reviewable.
    """
    code = r'''import UnityPy,json,struct,hashlib,sys
from pathlib import Path
game,out=map(Path,sys.argv[1:]); wanted=['PlaySound_UITravelCartLoop','PlaySound_UITravelCartStartDrum','PlaySound_UIMapEncounter','PlaySound_UITravelReachLocation']; records=[]
for name,pathid in [('resources.assets',11504),('level1',11461)]:
 asset=game/name; env=UnityPy.load(str(asset)); obj=next(o for o in env.objects if o.path_id==pathid); raw=obj.get_raw_data(); items=[]
 for item in wanted:
  value=item.encode(); start=raw.find(value); assert start>=4 and raw.find(value,start+1)<0, 'Original item identity is missing or ambiguous: '+item
  assert struct.unpack_from('<I',raw,start-4)[0]==len(value), 'Original length-prefix differs: '+item
  field=(start+len(value)+3)&~3
  def i(offset):return struct.unpack_from('<i',raw,field+offset)[0]
  def f(offset):return struct.unpack_from('<f',raw,field+offset)[0]
  record={'name':item,'byte_offset':start,'prefix_hex':raw[start-4:field+100].hex(),'HapticAudioEnabled':i(0),'HapticVibrationEnabled':i(4),'Loop':i(8),'DestroyOnLoad':i(32),'Volume':f(36),'SubItemPickMode':i(40),'MinTimeBetweenPlayCalls':f(44),'MaxInstanceCount':i(48),'InterrupOnExceed':i(52),'Delay':f(56),'spatialBlend':f(88),'stopEvents_count':i(92),'subItems_count':i(96)}
  assert record['MaxInstanceCount']==0 and record['stopEvents_count']==0 and record['subItems_count']==1, 'Native multiplicity/stop/subitem assumptions changed'
  assert record['Loop']==(1 if item==wanted[0] else 0), 'Native loop mode changed'
  items.append(record)
 records.append({'asset':str(asset),'asset_sha256':hashlib.sha256(asset.read_bytes()).hexdigest(),'object_path_id':pathid,'object_sha256':hashlib.sha256(raw).hexdigest(),'items':items})
out.write_text(json.dumps({'schema':'source-ordered AudioItem fixed prefix only','UnityPy':UnityPy.__version__,'records':records},indent=2)+'\n')
'''
    python = Path.home() / 'unitypy-venv/bin/python'
    result = subprocess.run([str(python), '-c', code, str(game), str(output)], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError('Native serialized item proof failed: ' + result.stdout + result.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/travel-audio-runtime')
    parser.add_argument('--case', action='append', help='Partial development run; repeat to select cases')
    args = parser.parse_args()
    root = args.source_root.resolve()
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    game = root / 'ressources/GH_Data'
    fixture = root / 'scripts/travel-audio-runtime'
    sources = {
        'NativeTravelLoop.cs': root / 'src/GloomhavenVR/Core/Sound/NativeTravelLoop.cs',
        'NativeMapTravelAudio.cs': root / 'src/GloomhavenVR/WorldUI/MapRoom/NativeMapTravelAudio.cs',
    }
    original = {name: path.read_text() for name, path in sources.items()}
    variants = [
        ('production', '', '', '', ''),
        ('original-duplicate', 'NativeTravelLoop.cs', 'receipt.Object.Stop(0f);',
            '/* original defect: the first request survives */', 'resumed native timed travel has exactly one cart source'),
        ('lost-native-capture', 'NativeTravelLoop.cs', 'Remember(source, generation);',
            '/* injected: no ownership receipt */', 'resumed native timed travel has exactly one cart source'),
        ('pending-not-canceled', 'NativeTravelLoop.cs', 'if (!receipt.StillOwned) continue;',
            'if (!receipt.StillOwned || receipt.Object.scheduledPlayingAtDspTime > UnityEngine.AudioSettings.dspTime) continue;',
            'pending old native loop never reappears after resume'),
        ('global-same-id-stop', 'NativeTravelLoop.cs', 'receipt.Object.Stop(0f);',
            'AudioController.Stop(receipt.Item, 0f);', 'replacement preserves independent producer using the identical audio item'),
        ('lost-native-layer', 'NativeTravelLoop.cs', '_owner.Objects.Add(new Receipt(source, Item, generation));',
            '_owner.Objects.Clear(); _owner.Objects.Add(new Receipt(source, Item, generation));',
            'replacement cancels every prior layer and retains every new native layer'),
        ('stale-playback-generation', 'NativeTravelLoop.cs', '&& _generation.Value == _version',
            '', 'per-object generation preserves same-frame same-item native replay'),
        ('unobserved-unscoped-reuse', 'NativeTravelLoop.cs',
            '&& Generations.TryGetValue(source, out Generation generation)) generation.Value++;',
            '&& Generations.TryGetValue(source, out Generation generation)) { /* injected: old receipt remains */ }',
            'per-object generation preserves same-frame same-item native replay'),
        ('lost-scope-finalizer', 'NativeMapTravelAudio.cs', 'if (state.Entered) NativeTravelLoop.End(state.Previous);',
            'if (state.Entered && MapRoomDriver.Active) NativeTravelLoop.End(state.Previous);', 'exception finalizer restores ownership scope after room activity changes'),
        ('lost-destroy-ownership', 'NativeTravelLoop.cs', 'StopOwned(owner);\n        Owners.Remove(producer);',
            '/* injected: established audio ownership is forgotten */\n        Owners.Remove(producer);',
            'destroyed flow cancels its owned paused loop after room stand-down'),
        ('flat-producer-changed', 'NativeMapTravelAudio.cs', 'if (!MapRoomDriver.Active) return;',
            '/* injected: flat calls are incorrectly captured */', 'flat native travel remains outside the VR ownership repair'),
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
        root / 'decompiled/GH.Runtime/MapTimedMovementFlow.cs', root / 'decompiled/GH.Runtime/MapPointsMovementFlow.cs',
        root / 'decompiled/GH.Runtime/AudioControllerUtils.cs', root / 'decompiled/GH.Runtime/AudioController.cs',
        root / 'decompiled/GH.Runtime/ClockStone/AudioObject.cs', root / 'decompiled/GH.Runtime/ClockStone/AudioItem.cs',
        root / 'decompiled/GH.Runtime/UIEventPanel.cs', root / 'decompiled/GH.Runtime/MapChoreographer.cs',
    ]}
    try:
        retained = run / 'Sources'; retained.mkdir()
        shutil.copytree(fixture, retained / 'fixture')
        shutil.copyfile(Path(__file__), retained / Path(__file__).name)
        for path in sources.values():
            shutil.copyfile(path, retained / path.name)
        native_items(game, run / 'native-serialized-items.json')
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': pins, 'scope': __doc__,
            'subset': args.case, 'ports': ['native movement completion', 'camera focus', 'scene bootstrap', 'delayed arrival clock']}, indent=2) + '\n')
        assemblies = run / 'assemblies'; assemblies.mkdir()
        manifest = {'evidence': str(run), 'result': str(run / 'results.txt'),
            'gameManaged': str((game / 'Managed').resolve()), 'dependencies': str(assemblies), 'cases': []}
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        for name, filename, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for target, code in original.items():
                if target == filename:
                    assert code.count(before) == 1, 'Mutation binding drift: ' + name
                    code = code.replace(before, after, 1)
                (production / target).write_text(code)
            csproj = case / 'TravelAudio.csproj'; shutil.copyfile(retained / 'fixture/TravelAudio.csproj', csproj)
            assembly = 'TravelAudio_' + name.replace('-', '_')
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
            '-projectPath', str(project), '-executeMethod', 'TravelAudioRunner.Start',
            '-travelAudioManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')], run / 'launch.log', 360)
        stable = all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in pins.items())
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(pins)}) + '\n')
        if not stable:
            status = 1
        if (run / 'results.txt').exists():
            print((run / 'results.txt').read_text(), end='')
        if status or not (run / 'results.txt').exists():
            status = 1
            raise RuntimeError('Actual native travel audio proof failed: ' + str(run))
        print('PASS original native travel/audio lifecycle and ' + str(len(variants) - 1) + ' selected causal controls')
    finally:
        editors = cleanup_project_editors(project, unity, launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']:
            raise RuntimeError('Owned editor remains; caches retained')
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']:
            raise RuntimeError('Owned project compiler remains; caches retained')
        cleanup = cleanup_completed_run('travel-audio-runtime', status, completion, root, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    return status


if __name__ == '__main__':
    raise SystemExit(main())
