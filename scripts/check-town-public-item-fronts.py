#!/usr/bin/env python3
"""Render layered public merchant item fronts through the actual native mirror pipeline."""
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-public-item-fronts')
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
    shutil.copyfile(ROOT / 'scripts/town-public-item-fronts-runtime/Program.cs', fixture / 'OriginalPublicItemFronts.cs')
    stock = args.source_root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.Stock.cs'
    if stock.is_file():
        shutil.copyfile(ROOT / 'scripts/town-public-item-fronts-runtime/StockPublisher.cs', fixture / 'StockPublisher.cs')
        if not any('private void PublishStockEntries(TownServiceCatalog catalog)' in text for text in bound.values()):
            bound['StockPublisher.cs'] = 'using System;\nusing System.Globalization;\nusing UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceSync {\n' + loader.method(stock.read_text(), 'private void PublishStockEntries(TownServiceCatalog catalog)') + '\n}\n'
    program = fixture / 'Program.cs'
    text = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    branch = '''            if (suite == "public-item-fronts")
            {
                var fronts = OriginalPublicItemFronts();
                while (fronts.MoveNext()) yield return fronts.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;
            }
'''
    if anchor not in text:
        raise SystemExit('Fixture entry binding drift')
    if stock.is_file():
        branch = branch.replace('                var fronts', '                StockPublicationRouting();\n                var fronts')
    program.write_text(text.replace(anchor, branch + anchor, 1))
    # Older common harness revisions did not need template traversal. Bind the unchanged
    # production Append implementation in its inert NativeTemplates seam, not a guessed path.
    publisher = fixture / 'Publisher.cs'
    if 'NativeTemplatePaths.cs' not in bound:
        text = publisher.read_text()
        append = loader.method((args.source_root / 'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.cs').read_text(),
                               'internal static string Append(string path, Transform child)')
        anchor = '        internal static bool IsBoundary(Transform source) => false;'
        if anchor not in text:
            raise SystemExit('Native-template fixture boundary binding drift')
        publisher.write_text(text.replace(anchor, anchor + '\n' + append, 1))
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('native-tmp-rejected', 'TownServiceNeutralize.cs', '|| type == typeof(TMP_SubMeshUI);',
             '|| false && type == typeof(TMP_SubMeshUI);',
             'Native custom graphic requires an explicit original mesh adapter: TMPro.TMP_SubMeshUI'),
            ('native-tmp-retired', 'TownServiceNeutralize.cs', '|| EngineGraphic(type)',
             '|| EngineGraphic(type) && type != typeof(TMP_SubMeshUI)',
             'original pooled item fallback text renderer survives inert public cloning'),
            ('borrow-order-alias', 'TownServiceTemplateAssets.cs', 'bool modelFace = template.StartsWith("item.", StringComparison.Ordinal)',
             'bool modelFace = false && template.StartsWith("item.", StringComparison.Ordinal)',
             'different borrow orders retain model-aware artwork identity'),
            ('original-coin-refused', 'TownServiceAssets.cs', '|| texture.name == "CoinIcon2_White"', '|| false && texture.name == "CoinIcon2_White"',
             'Ambiguous native town-service texture requires an explicit binding: CoinIcon2_White'),
            ('original-ring-refused', 'TownServiceAssets.cs', '|| texture.name == "T_disc_ring"', '|| false && texture.name == "T_disc_ring"',
             'different borrow orders retain the original native effect identity'),
            ('front-sprite-omitted', 'TownServiceBinding.cs', 'image.sprite = assets.Resolve<Sprite>(text[0]); image.overrideSprite = assets.Resolve<Sprite>(text[1]);',
             'image.sprite = null; image.overrideSprite = null;', 'public held item retains its complete original artwork sprite'),
        ]
    if not args.no_negative_controls and stock.is_file():
        variants += [
            ('stationary-stock-duplicated', next(name for name, text in bound.items() if 'private void PublishStockEntries(TownServiceCatalog catalog)' in text), '!entry.Current || !entry.Sample.IsMoving', '!entry.Current',
             'independent stock publisher contains only current moving original samples'),
            ('stock-public-mount', next(name for name, text in bound.items() if 'private void PublishStockEntries(TownServiceCatalog catalog)' in text), 'Publish("merchant.heldstock",', 'Publish("merchant.cardmount",',
             'held stock uses its explicit visitor-owned original mount'),
        ]
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}, indent=2) + '\n')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'suite': 'public-item-fronts', 'cases': []}
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
        assembly = 'TownPublicItems_' + name.replace('-', '_')
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
