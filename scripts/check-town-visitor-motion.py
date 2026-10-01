#!/usr/bin/env python3
"""Run the complete production town visitor tween with real Unity transform/UI state."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-visitor-motion')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    if not args.unity.is_file():
        parser.error('Real Unity 2021.3.5 is required; this runtime proof cannot silently skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    source = args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceMotion.cs'
    original = source.read_text()
    (run / 'source-hashes.json').write_text(json.dumps({str(source): hashlib.sha256(original.encode()).hexdigest()}, indent=2) + '\n')
    variants = [('production', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('held-purse-stop', '_continuousVisitorMotion = address == "ritual.purse.held|"', '_continuousVisitorMotion = false',
             'held purse continues moving between slower owner samples'),
            ('always-continuous', '_continuousDecisionFacing = address.StartsWith', '_continuousDecisionFacing = true || address.StartsWith',
             'non-held discrete controls retain their rapid response'),
            ('restart-at-target', '_from[i] = Read(_nodes[i]);', '_from[i] = _hasTarget ? _to[i] : Read(_nodes[i]);',
             "binding's unchanged properties start from the complete previous owner target"),
        ]
    fixture = ROOT / 'scripts/town-visitor-motion-runtime'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, before, after, expected in variants:
        build = run / name
        production = build / 'production'
        production.mkdir(parents=True)
        text = original
        if before:
            if text.count(before) != 1:
                raise SystemExit('Production mutation binding drift: ' + name)
            text = text.replace(before, after, 1)
        (production / 'Motion.cs').write_text(text)
        project = build / 'Motion.csproj'
        shutil.copyfile(fixture / 'Motion.csproj', project)
        assembly = 'TownVisitor_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                                 '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
                                 '-p:ProductionDir=' + str(production),
                                 '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
                                 '-p:UnityUi=' + str(args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll')],
                                capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2))
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    # The existing small runner executes each isolated production assembly in real Play Mode
    # and treats an unrelated exception as failure, including for mutation controls.
    shutil.copyfile(ROOT / 'scripts/scenario-scenery-runtime/Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.physics":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run([str(args.unity), '-batchmode', '-nographics', '-projectPath', str(project),
                             '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(manifest_path),
                             '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    report = Path(manifest['result'])
    if report.is_file():
        print(report.read_text(), end='')
    if result.returncode or not report.is_file():
        raise SystemExit('FAIL Unity run; see ' + str(run / 'unity.log'))
    # Preserve the small evidence, not a fresh Unity Library and compiler caches after each run.
    shutil.rmtree(project)
    for name, *_ in variants:
        for output in ('bin', 'obj'):
            shutil.rmtree(run / name / output, ignore_errors=True)
    print(f'PASS: {len(variants)} complete production/negative variants; evidence: {run}')


if __name__ == '__main__':
    main()
