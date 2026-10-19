#!/usr/bin/env python3
"""Complete production source/callback state with explicit Unity/XR/GPU boundaries."""
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
sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / '.planning/debug/steamlink-scene-startup')
    parser.add_argument('--typecheck-only', action='store_true',
                        help='Compile the real Unity/net472 sources without repeating state/mutant cases.')
    parser.add_argument('--metadata', choices=('auto', 'unity', 'nuget'), default='auto',
                        help='Use installed Unity metadata, the pinned NuGet modules, or automatic fallback.')
    parser.add_argument('--unity-managed', type=Path,
                        default=Path('/home/claw/unity-2021.3.5/Editor/Data/Managed'))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    log = run / 'completion.log'; log.write_text('evidence: ' + str(run) + '\n')
    source = ROOT / 'src/GloomhavenVR/Core/SteamLink/SteamLinkSceneRuntime.cs'
    text = source.read_text()
    old_mono = '''                Close();
                Graphics.Blit(source, destination);
                return;'''
    assert text.count('            || !StereoReady(head)') == 1
    assert text.count(old_mono) == 1
    late_guard = '''            if (!SteamLinkSceneRuntime.StereoReady(Head)
                || !SteamLinkSceneRuntime.TransportPrepared()
                || (eye != Camera.MonoOrStereoscopicEye.Left
                    && eye != Camera.MonoOrStereoscopicEye.Right))'''
    assert text.count(late_guard) == 1
    cohort_guard = '            if (!StereoCohortReady(eye, Time.frameCount))'
    assert text.count(cohort_guard) == 1
    late_owner_close = '''        if (Ready && !SteamLinkSceneRuntime.Owns(this, Head, _epoch))
            Close();'''
    assert text.count(late_owner_close) == 1
    variants = {
        'production': text,
        'unready-admission': text.replace('            || !StereoReady(head)', ''),
        'latched-mono': text.replace(old_mono, '''                if (eye == Camera.MonoOrStereoscopicEye.Mono) Failure = "UnknownEye";
                Close();
                if (eye == Camera.MonoOrStereoscopicEye.Mono) SteamLinkSceneRuntime.Failed(Head, Failure);
                Graphics.Blit(source, destination);
                return;'''),
        'late-attachment': text.replace(late_guard, '''            if (eye != Camera.MonoOrStereoscopicEye.Left
                && eye != Camera.MonoOrStereoscopicEye.Right
                || !SteamLinkSceneRuntime.TransportPrepared())'''),
        'no-cohort': text.replace(cohort_guard, '            if (!StereoCohortReady(eye, Time.frameCount) && Time.frameCount == int.MinValue)'),
        'same-frame-activation': text.replace('        return _cohortReady;', '        return _cohortReady || _cohortEyes == 3;'),
        'cross-frame-cohort': text.replace('            _cohortEyes = 0;', '            // Mutant retains eye labels across unrelated frames.'),
        'pending-host-admission': text.replace(' || !TransportPrepared())', ')'),
        'stale-host-owner': text.replace('        && TransportPrepared();', '        ;'),
    }
    expected = {
        'unready-admission': 'startup non-stereo head must not arm',
        'latched-mono': 'temporary Mono metadata must close without a permanent failure',
        'late-attachment': 'XR attachment loss after pre-cull closes normally',
        # Host-admission cases now prime a complete cohort before the dedicated
        # stereo sequence below. These defects must already fail that earlier
        # positive assertion; never require them to reach a later test first.
        'no-cohort': 'first complete startup cohort must stay ordinary in both eyes',
        'same-frame-activation': 'first complete startup cohort must stay ordinary in both eyes',
        'cross-frame-cohort': 'duplicate Left then Right on another frame never forms a complete cohort',
        'pending-host-admission': 'Pending paired capability must not prepare a source',
        'stale-host-owner': 'Capability loss retires the current stereo epoch',
    }
    production = source.parent
    names = ['SteamLinkSceneRuntime.cs', 'SceneEyeCoverage.cs', 'SceneEyeOutput.cs',
             'SceneTransparentMaterials.cs']
    gpu_fixture = ROOT / 'tests/steamlink-loading-source'
    inputs = [production/name for name in names] + [p for p in HERE.iterdir() if p.is_file()]
    inputs += [gpu_fixture/name for name in ['Fixture.csproj', 'Boundary.cs', 'Fixture.cs']]
    pins = {str(p.relative_to(ROOT)): sha(p) for p in inputs}
    (run/'source-pins.json').write_text(json.dumps(pins, indent=2)+'\n')
    saved = run/'sources'; saved.mkdir()
    for i,p in enumerate(inputs): shutil.copyfile(p, saved/(str(i)+'-'+p.name))
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    children = []; results = []; code = 1; assertions = None
    def execute(command, output):
        with output.open('w') as stream:
            child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            children.append(child)
            try: return child.wait(timeout=120)
            except BaseException:
                if child.poll() is None: os.killpg(child.pid, signal.SIGKILL)
                child.wait(); raise
    try:
        fixture = run/'owned-portable-project'
        if not args.typecheck_only:
            fixture.mkdir()
            shutil.copyfile(HERE/'Fixture.csproj', fixture/'Fixture.csproj')
        for name,content in ({} if args.typecheck_only else variants).items():
            candidate = saved/(name+'.cs'); candidate.write_text(content)
            command = [dotnet, 'run', '--project', str(fixture/'Fixture.csproj'), '-c', 'Release',
                              '-p:SourceFile='+str(candidate), '-p:FixtureDir='+str(HERE),
                              '-p:UseSharedCompilation=false']
            status = execute(command, run/(name+'.log'))
            output = (run/(name+'.log')).read_text()
            if name == 'production':
                marker = ' startup/callback assertions passed'
                assert status == 0 and marker in output, output
                assertions = int(output.split(marker)[0].splitlines()[-1])
            else:
                assert status != 0 and expected[name] in output and 'error CS' not in output, name + ': ' + output
            results.append({'variant': name, 'status': status, 'expected_semantic_result': True})
        # Compile the complete unmodified production owner, ordinary output, coverage and
        # materials against actual Unity2021/net472 metadata with strict warnings.
        frozen = run/'typecheck-sources'; frozen.mkdir()
        for name in names: shutil.copyfile(production/name, frozen/name)
        typecheck = run/'owned-typecheck-project'; typecheck.mkdir()
        shutil.copyfile(gpu_fixture/'Fixture.csproj', typecheck/'Fixture.csproj')
        packages = Path(os.environ.get('NUGET_PACKAGES', str(Path.home()/'.nuget/packages')))
        framework = packages/'microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
        modules = ['CoreModule', 'XRModule', 'VRModule', 'AssetBundleModule', 'JSONSerializeModule']
        unity_files = [args.unity_managed/'UnityEngine'/('UnityEngine.'+name+'.dll') for name in modules]
        use_unity = args.metadata != 'nuget' and all(p.is_file() for p in unity_files)
        if args.metadata == 'unity':
            assert use_unity, 'Requested installed Unity metadata is incomplete: '+str(args.unity_managed)
        if use_unity:
            managed = args.unity_managed
            metadata_files = unity_files
        else:
            # Hosted CI restores this exact module package alongside the net472
            # framework. Supply the Editor-shaped directory privately; never
            # create or alter a shared reference directory or its symlinks.
            nuget = packages/'unityengine.modules/2021.3.5/lib/net35'
            metadata_files = [nuget/('UnityEngine.'+name+'.dll') for name in modules]
            assert all(p.is_file() for p in metadata_files), 'Pinned Unity2021.3.5 NuGet metadata is incomplete: '+str(nuget)
            managed = run/'owned-typecheck-metadata'
            (managed/'UnityEngine').mkdir(parents=True)
            for p in metadata_files: shutil.copyfile(p, managed/'UnityEngine'/p.name)
        metadata_pins = {str(p): sha(p) for p in metadata_files}
        (run/'metadata-pins.json').write_text(json.dumps({'provider': 'unity' if use_unity else 'nuget',
            'unity_version': '2021.3.5', 'files': metadata_pins}, indent=2)+'\n')
        status = execute([dotnet, 'build', str(typecheck/'Fixture.csproj'), '-c', 'Release',
                          '-p:FrozenSource='+str(frozen), '-p:FixtureDir='+str(gpu_fixture),
                          '-p:UnityManaged='+str(managed), '-p:FrameworkReferenceDir='+str(framework),
                          '-p:UseSharedCompilation=false'], run/'net472-typecheck.log')
        assert status == 0, 'Actual Unity net472 compile failed: '+str(run)
        assert metadata_pins == {str(p): sha(p) for p in metadata_files}, 'Borrowed metadata changed through child reap'
        assert pins == {str(p.relative_to(ROOT)): sha(p) for p in inputs}, 'Sources changed through child reap'
        code = 0
    finally:
        reaped = all(c.poll() is not None for c in children)
        receipt = cleanup_completed_run('steamlink-scene-startup', code, log, ROOT, process_reaped=reaped)
        if reaped and receipt is not None:
            metadata_copy = run/'owned-typecheck-metadata'
            if metadata_copy.is_dir() and not metadata_copy.is_symlink(): shutil.rmtree(metadata_copy)
        (run/'cleanup.json').write_text(json.dumps({'retention_api': receipt, 'all_children_reaped': reaped}, indent=2)+'\n')
        (run/'result.json').write_text(json.dumps({'exit_code': code, 'cases': results,
            'assertions': assertions if code == 0 else None,
            'actual_runtime_and_callback_bodies': True, 'state_callbacks_executed': not args.typecheck_only,
            'xr_and_gpu_boundaries_supplied': True,
            'native_receiver_executed': False, 'headset_picture_proven': False}, indent=2)+'\n')
        print(json.dumps({'run': str(run), 'exit_code': code}))
    return code


if __name__ == '__main__': raise SystemExit(main())
