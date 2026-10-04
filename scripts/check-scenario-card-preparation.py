#!/usr/bin/env python3
"""Run full production scenario preparation/pin/mip/factory paths in real Unity frames."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FILES = [
    'src/GloomhavenVR/Cards/Art/ScenarioCardPreparation.cs',
    'src/GloomhavenVR/Cards/Art/CardArtPin.cs',
    'src/GloomhavenVR/Cards/Art/CardFaceMipBake.cs',
    'src/GloomhavenVR/Cards/VRCardFactory.cs',
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/scenario-card-preparation')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--case', action='append', help='Run named variants only; repeat for a focused follow-up')
    args = parser.parse_args()
    ui = args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'
    if not args.unity.is_file() or not ui.is_file(): parser.error('Actual Unity 2021.3.5 and native UI assembly required; no silent skip')
    fixture = ROOT / 'scripts/scenario-card-preparation-runtime'
    sources = {Path(path).name: (args.source_root / path).read_text() for path in FILES}
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    (run / 'source-hashes.json').write_text(json.dumps({path: hashlib.sha256((args.source_root/path).read_bytes()).hexdigest() for path in FILES}, indent=2) + '\n')
    fixture_files = list(fixture.glob('*')) + [Path(__file__).resolve(), ROOT/'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs']
    (run / 'fixture-hashes.json').write_text(json.dumps({str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in fixture_files if path.is_file()}, indent=2) + '\n')
    variants = [
        ('production', '', '', '', ''),
        ('first-character-only', 'ScenarioCardPreparation.cs', 'foreach (CPlayerActor player in players)', 'foreach (CPlayerActor player in players.GetRange(0, 1))', 'all scenario classes and transferred ability skins are prepared without focus'),
        ('skip-cold-cache', 'ScenarioCardPreparation.cs', 'CardFaceMipBake.WarmSprite(sprite);', '// omitted shared mip preparation', 'ordinary local and remote fronts have no cold sprite miss after preparation'),
        ('skip-element-icons', 'ScenarioCardPreparation.cs', 'else if (value is ElementConfigUI element)', 'else if (value is ElementConfigUI element && Time.frameCount < 0)', 'ordinary local and remote fronts have no cold sprite miss after preparation'),
        ('modify-native-images', 'ScenarioCardPreparation.cs', 'CardFaceMipBake.WarmSprite(sprite);', 'CardFaceMipBake.WarmSprite(sprite); foreach (var img in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>()) CardFaceMipBake.Rescan(img);', 'preparation never selects a character activates native widgets or assigns original artwork'),
        ('live-reservations', 'VRCardFactory.cs', '_prepared.Add(BuildBlank());', 'VRCard added = BuildBlank(); _prepared.Add(added); _all.Add(added);', 'reserved wrappers remain absent from live hand driver inventories'),
        ('skip-backing-reuse', 'VRCardFactory.cs', 'while (_prepared.Count > 0)', 'while (_prepared.Count > 0 && Time.frameCount < 0)', 'first ordinary card consumes a prebuilt backing without rebuilding or fabricating native content'),
        ('reset-live-faces', 'ScenarioCardPreparation.cs', 's_factory?.ClearPreparedBlanks();', 's_factory?.Clear();', 'reset releases only unused backings retaining native and remote active art plus shared pins'),
        ('release-shared-pins', 'ScenarioCardPreparation.cs', 's_factory?.ClearPreparedBlanks();', 's_factory?.ClearPreparedBlanks(); CardArtPin.ReleaseAll("incorrect preparation reset");', 'reset releases only unused backings retaining native and remote active art plus shared pins'),
        ('no-pending-bound', 'ScenarioCardPreparation.cs', 'pending && Time.realtimeSinceStartup < s_deadline', 'pending', 'bounded preparation reaches readiness'),
        ('same-frame-repeat', 'ScenarioCardPreparation.cs', 'if (s_tickFrame == Time.frameCount) return;', 'if (s_tickFrame == Time.frameCount && Time.frameCount < 0) return;', 'repeated same-frame loader offers cannot multiply preparation work'),
    ]
    if args.case:
        requested = set(args.case)
        unknown = requested - {variant[0] for variant in variants}
        if unknown: parser.error('Unknown variants: ' + ', '.join(sorted(unknown)))
        variants = [variant for variant in variants if variant[0] in requested]
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, filename, before, after, expected in variants:
        build = run / name
        generated = build / 'production'
        generated.mkdir(parents=True)
        for path, code in sources.items():
            if path == filename:
                if code.count(before) != 1: raise SystemExit('Production negative-control binding drift: ' + name)
                code = code.replace(before, after, 1)
            (generated / path).write_text(code)
        project = build / 'Preparation.csproj'
        shutil.copyfile(fixture / 'Preparation.csproj', project)
        assembly = 'ScenarioCards_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(generated), '-p:UnityManaged=' + str(args.unity.parent/'Data/Managed'), '-p:UnityUi=' + str(ui)], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name': name, 'dll': str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')), 'expected': expected})
    project = run / 'unity'
    for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'): (project/directory).mkdir(parents=True, exist_ok=True)
    for entry in manifest['cases']:
        destination = project/'Assets/Plugins'/Path(entry['dll']).name
        shutil.copyfile(entry['dll'], destination); entry['dll'] = str(destination)
    manifest_path = run / 'manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    # The existing runner owns actual Unity frame progression, with nested preparation
    # enumerators supported here just as Unity's real coroutine pump supports them.
    runner = (ROOT/'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs').read_text()
    runner = runner.replace('private static IEnumerator steps;', 'private static IEnumerator steps; private static Stack<IEnumerator> nested = new Stack<IEnumerator>();')
    runner = runner.replace('if (steps.MoveNext()) return;', '''if (nested.Count == 0) nested.Push(steps);
            while (nested.Count > 0)
            {
                IEnumerator top = nested.Peek();
                if (!top.MoveNext()) { nested.Pop(); continue; }
                if (top.Current is IEnumerator child) { nested.Push(child); continue; }
                return;
            }''')
    runner = runner.replace('output.Flush(); steps = null;', 'output.Flush(); nested.Clear(); steps = null;')
    (project/'Assets/Editor/DiagnosticRunner.cs').write_text(runner)
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project), '-executeMethod', 'DiagnosticRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run/'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=360)
    for cache in ('Library', 'Temp'): shutil.rmtree(project/cache, ignore_errors=True)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see ' + str(run/'unity.log'))
    print(f'PASS: {len(variants)} production/causal variants; retained evidence: {run}')


if __name__ == '__main__': main()
