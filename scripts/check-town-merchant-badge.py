#!/usr/bin/env python3
"""Capture original merchant shelf badges through the real native publisher and mirror pipeline."""
import argparse
import hashlib
import importlib.util
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-merchant-badge')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    if not args.unity.is_file():
        parser.error('Real Unity 2021.3.5 is required; this rendered proof cannot silently skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('town_mirror_source_binding', ROOT / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loader)
    bound, hashes = loader.sources(args.source_root)
    helper = args.source_root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceTemplateAssets.cs'
    bound[helper.name] = helper.read_text()
    fixture = run / 'fixture'
    shutil.copytree(ROOT / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(ROOT / 'scripts/town-merchant-badge-runtime/Program.cs', fixture / 'OriginalMerchantBadge.cs')
    publisher = fixture / 'Publisher.cs'
    text = publisher.read_text()
    boundary = 'internal static bool IsBoundary(Transform source) => BoundaryRoots.Contains(source);'
    if text.count(boundary) != 1:
        raise SystemExit('Native boundary seam drift')
    text = text.replace(boundary, 'internal static bool IsBoundary(Transform source) => BoundaryRoots.Contains(source) || source.GetComponent<ItemCardUI>() != null || source.parent != null && TownServiceCatalog.CardMounts.ContainsKey(source.parent);')
    text = text.replace('NativeTemplates.Originals[key] = source;', 'NativeTemplates.Originals[key] = key == "merchant.cardface" ? NativeTemplates.BadgeTemplate : source;')
    publisher.write_text(text)
    program = fixture / 'Program.cs'
    text = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    branch = '''            if (suite == "merchant-badge")
            {
                var fronts = OriginalMerchantBadge();
                while (fronts.MoveNext()) yield return fronts.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;
            }
'''
    if anchor not in text:
        raise SystemExit('Fixture entry binding drift')
    program.write_text(text.replace(anchor, branch + anchor, 1))
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('original-face-omitted', 'PublisherTick.cs', 'Publish("merchant.cardface", entry.FaceRoot, prewarm: true);', '',
             'original shelf face and band are captured alongside the native item'),
            ('badge-not-page-member', 'PublisherTick.cs', 'AddRackMembers(entry.FaceRoot, entry, rack, rackId);', '',
             'original stock band belongs to the same causal cassette page as its card'),
            ('stock-band-copied', 'TownServiceSync.Stock.cs', 'Publish("merchant.heldstock", entry.MountRoot, prewarm: true);',
             'Publish("merchant.heldstock", entry.MountRoot, prewarm: true); Publish("merchant.cardface", entry.FaceRoot, prewarm: true);',
             'held-stock publication never copies the shelf-only sold-out annotation'),
        ]
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}, indent=2) + '\n')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'suite': 'merchant-badge', 'cases': []}
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / 'production'
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if text.count(before) != 1:
                    raise SystemExit('Production mutation binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Mirror.csproj'
        shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'TownMerchantBadge_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                                 '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
                                 '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
                                 '-p:UnityUi=' + str(args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
                                 '-p:UnityTmp=' + str(args.source_root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
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
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
                             '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(manifest_path),
                             '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    report = Path(manifest['result'])
    if report.is_file():
        print(report.read_text(), end='')
    if result.returncode or not report.is_file():
        raise SystemExit('FAIL Unity run; see ' + str(run / 'unity.log'))
    for case in manifest['cases']:
        counts = run / (case['name'] + '-evidence/assertions.txt')
        if counts.is_file():
            print(case['name'] + ': ' + counts.read_text().strip())
    # Keep rendered PNGs, source snapshots/hashes and logs, not large Unity/compiler caches.
    shutil.rmtree(project)
    for name, *_ in variants:
        for output in ('bin', 'obj'):
            shutil.rmtree(run / name / output, ignore_errors=True)
    print(f'PASS: {len(variants)} complete production/negative variants; evidence: {run}')


if __name__ == '__main__':
    main()
