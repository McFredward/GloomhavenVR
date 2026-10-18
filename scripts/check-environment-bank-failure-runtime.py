#!/usr/bin/env python3
"""Native bank-name/index proof and bounded environment failure retention.

Loads the retained desktop bank unchanged and builds one tiny invalid-index test
bank. No original game bank or Android player is rebuilt. A specific B691 Android
index failure remains unproved because its payload was not captured.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def worker(run):
    fixture = ROOT / 'tests/environment-bank-failure-runtime'
    bank = ROOT / 'prebuilt/ghvr-environment.bundle'
    paths = [ROOT / 'src/GloomhavenVR/Core/Perf' / name for name in
             ('ScenarioEnvironmentMeshBank.cs', 'ScenarioEnvironmentMeshStream.cs')]
    sources = {path.name: path.read_text() for path in paths}
    mutations = [
        ('index-reason-lost', 'RetainFailure("index", error);', '/* omitted index reason */',
         'retained state identifies the missing owned-receipt stage and exact cause'),
        ('loader-reason-lost', 'RetainFailure("asset-loader", error);', '/* omitted loader reason */',
         'retained state includes bounded outer and inner loader causes with no control characters'),
        ('inner-reason-lost', 'depth < 3 && error != null', 'depth < 1 && error != null',
         'retained state includes bounded outer and inner loader causes with no control characters'),
        ('detail-unbounded', 'if (detail.Length > 384) detail = detail.Substring(0, 384);', '/* missing bound */',
         'retained exception obeys its strict 384-character bound'),
        ('control-character-leak', 'if (char.IsControl(text[i]))', 'if (false && char.IsControl(text[i]))',
         'retained state includes bounded outer and inner loader causes with no control characters'),
    ]
    variants = [('production', sources['ScenarioEnvironmentMeshBank.cs'], '')]
    for name, before, after, expected in mutations:
        assert sources['ScenarioEnvironmentMeshBank.cs'].count(before) == 1, name
        variants.append((name, sources['ScenarioEnvironmentMeshBank.cs'].replace(before, after), expected))
    inputs = paths + [Path(__file__)] + sorted(path for path in fixture.rglob('*') if path.is_file())
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}
    (run / 'source-hashes.json').write_text(json.dumps({'sha256': hashes, 'desktopBankSha256': hashlib.sha256(bank.read_bytes()).hexdigest(),
        'transformations': [], 'limits': [
            'Complete production environment MeshBank and MeshStream compile unchanged, except explicit negative-control variants.',
            'Actual native retained desktop bank/index, mixed/lower-case Contains and LoadAsset execute in Unity2021.3.5f1.',
            'Only a tiny deliberately invalid index bank is compiled; original game data and full Android environment bank are never rebuilt.',
            'Platform selection and unused owned-provenance admission are explicit boundary surrogates; no actual Android failing payload was available.',
            'No source-mesh matching, Android native parser outcome, terrain/pillar submission or headset FPS claim.'
        ]}, indent=2) + '\n')
    manifest = {'result': str(run / 'results.txt'), 'bank': str(bank), 'cases': []}
    editor = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    for name, text, expected in variants:
        case = run / name; production = case / 'production'; production.mkdir(parents=True)
        (production / paths[0].name).write_text(text)
        (production / paths[1].name).write_text(sources[paths[1].name])
        project = case / 'EnvironmentBankFailure.csproj'; shutil.copyfile(fixture / project.name, project)
        assembly = 'EnvironmentBankFailure_' + name.replace('-', '_')
        command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                   '-p:AssemblyName=' + assembly, '-p:FixtureDir=' + str(fixture),
                   '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(editor.parent / 'Data/Managed')]
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
        (case / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise RuntimeError('Strict compilation failed: ' + str(case / 'build.log'))
        manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    unity = run / 'unity'; (unity / 'Assets/Editor').mkdir(parents=True)
    (unity / 'Packages').mkdir(); (unity / 'ProjectSettings').mkdir()
    # A deliberately tiny project still needs its native AssetBundle container.
    # The retained desktop bank loaded independently before this fixture build;
    # enabling the fixture compiler module makes no claim about the Android bank.
    (unity / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}\n')
    (unity / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    shutil.copyfile(fixture / 'Editor/Runner.cs', unity / 'Assets/Editor/Runner.cs')
    command = [str(editor), '-batchmode', '-nographics', '-projectPath', str(unity),
               '-executeMethod', 'EnvironmentBankFailureRunner.Run', '-failureManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    changed = [str(path) for path in inputs if hashlib.sha256(path.read_bytes()).hexdigest() != hashes[str(path)]]
    (run / 'source-stability.json').write_text(json.dumps({'unchanged': not changed, 'changed': changed}, indent=2) + '\n')
    if (run / 'results.txt').is_file(): print((run / 'results.txt').read_text(), end='')
    if result.returncode or changed or not (run / 'results.txt').is_file():
        raise RuntimeError('Native bank failure fixture failed: ' + str(run))


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--worker', type=Path)
    args = parser.parse_args()
    if args.worker: worker(args.worker); return
    ctypes.CDLL(None).prctl(36, 1, 0, 0, 0)
    base = ROOT / '.planning/debug/environment-bank-failure'; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base)); log = run / 'runner.log'
    print('Environment bank failure evidence: ' + str(run), flush=True)
    process = None; code = 1
    try:
        with log.open('w') as output:
            output.write('Environment bank failure evidence: ' + str(run) + '\n'); output.flush()
            process = subprocess.Popen([sys.executable, __file__, '--worker', str(run)], stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            code = process.wait(timeout=420)
    finally:
        helper = __import__('quest-card-alpha-content-tests')
        receipt = helper.reap_owned(process.pid if process else None)
        (run / 'process-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        cleanup = cleanup_completed_run('environment-bank-failure', code, log, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    print(log.read_text(errors='replace'), end='')
    raise SystemExit(code)


if __name__ == '__main__': main()
