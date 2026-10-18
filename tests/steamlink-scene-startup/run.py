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
                || eye == Camera.MonoOrStereoscopicEye.Mono)'''
    assert text.count(late_guard) == 1
    variants = {
        'production': text,
        'unready-admission': text.replace('            || !StereoReady(head)', ''),
        'latched-mono': text.replace(old_mono, '''                if (eye == Camera.MonoOrStereoscopicEye.Mono) Failure = "UnknownEye";
                Close();
                if (eye == Camera.MonoOrStereoscopicEye.Mono) SteamLinkSceneRuntime.Failed(Head, Failure);
                Graphics.Blit(source, destination);
                return;'''),
        'late-attachment': text.replace(late_guard, '            if (eye == Camera.MonoOrStereoscopicEye.Mono)'),
    }
    expected = {
        'unready-admission': 'startup non-stereo head must not arm',
        'latched-mono': 'temporary Mono metadata must close without a permanent failure',
        'late-attachment': 'XR attachment loss after pre-cull closes normally',
    }
    production = source.parent
    names = ['SteamLinkSceneRuntime.cs', 'SceneEyeCoverage.cs', 'SceneEyeOutput.cs',
             'SceneTransparentMaterials.cs', 'SceneAtlasProfile.cs']
    gpu_fixture = ROOT / 'tests/steamlink-loading-source'
    inputs = [production/name for name in names] + [p for p in HERE.iterdir() if p.is_file()]
    inputs += [gpu_fixture/name for name in ['Fixture.csproj', 'Boundary.cs', 'Fixture.cs']]
    pins = {str(p.relative_to(ROOT)): sha(p) for p in inputs}
    (run/'source-pins.json').write_text(json.dumps(pins, indent=2)+'\n')
    saved = run/'sources'; saved.mkdir()
    for i,p in enumerate(inputs): shutil.copyfile(p, saved/(str(i)+'-'+p.name))
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    children = []; results = []; code = 1
    def execute(command, output):
        with output.open('w') as stream:
            child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            children.append(child)
            try: return child.wait(timeout=120)
            except BaseException:
                if child.poll() is None: os.killpg(child.pid, signal.SIGKILL)
                child.wait(); raise
    try:
        fixture = run/'owned-portable-project'; fixture.mkdir()
        shutil.copyfile(HERE/'Fixture.csproj', fixture/'Fixture.csproj')
        for name,content in variants.items():
            candidate = saved/(name+'.cs'); candidate.write_text(content)
            status = execute([dotnet, 'run', '--project', str(fixture/'Fixture.csproj'), '-c', 'Release',
                              '-p:SourceFile='+str(candidate), '-p:FixtureDir='+str(HERE),
                              '-p:UseSharedCompilation=false'], run/(name+'.log'))
            output = (run/(name+'.log')).read_text()
            if name == 'production':
                assert status == 0 and 'startup/callback assertions passed' in output, output
                assertions = int(output.split(' startup/callback assertions passed')[0].splitlines()[-1])
            else:
                assert status != 0 and expected[name] in output and 'error CS' not in output, output
            results.append({'variant': name, 'status': status, 'expected_semantic_result': True})
        # Compile the complete unmodified production owner, packer, coverage and
        # materials against actual Unity2021/net472 metadata with strict warnings.
        frozen = run/'typecheck-sources'; frozen.mkdir()
        for name in names: shutil.copyfile(production/name, frozen/name)
        typecheck = run/'owned-typecheck-project'; typecheck.mkdir()
        shutil.copyfile(gpu_fixture/'Fixture.csproj', typecheck/'Fixture.csproj')
        framework = Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
        managed = Path('/home/claw/unity-2021.3.5/Editor/Data/Managed')
        status = execute([dotnet, 'build', str(typecheck/'Fixture.csproj'), '-c', 'Release',
                          '-p:FrozenSource='+str(frozen), '-p:FixtureDir='+str(gpu_fixture),
                          '-p:UnityManaged='+str(managed), '-p:FrameworkReferenceDir='+str(framework),
                          '-p:UseSharedCompilation=false'], run/'net472-typecheck.log')
        assert status == 0, 'Actual Unity net472 compile failed: '+str(run)
        assert pins == {str(p.relative_to(ROOT)): sha(p) for p in inputs}, 'Sources changed through child reap'
        code = 0
    finally:
        reaped = all(c.poll() is not None for c in children)
        receipt = cleanup_completed_run('steamlink-scene-startup', code, log, ROOT, process_reaped=reaped)
        if reaped:
            for name in ['owned-portable-project', 'owned-typecheck-project']:
                project = run/name
                if project.is_dir() and not project.is_symlink(): shutil.rmtree(project)
        (run/'cleanup.json').write_text(json.dumps({'retention_api': receipt, 'all_children_reaped': reaped}, indent=2)+'\n')
        (run/'result.json').write_text(json.dumps({'exit_code': code, 'cases': results,
            'assertions': assertions if code == 0 else None,
            'actual_runtime_and_callback_bodies': True, 'xr_and_gpu_boundaries_supplied': True,
            'native_receiver_executed': False, 'headset_picture_proven': False}, indent=2)+'\n')
        print(json.dumps({'run': str(run), 'exit_code': code}))
    return code


if __name__ == '__main__': raise SystemExit(main())
