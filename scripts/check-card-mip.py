#!/usr/bin/env python3
"""Render production card mip/filter/cache paths with actual Unity uGUI minification."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ['src/GloomhavenVR/Cards/Art/CardFaceMipBake.cs', 'src/GloomhavenVR/Cards/Art/CardArtWatch.cs', 'src/GloomhavenVR/Net/TownServices/TownServiceAssets.cs', 'src/GloomhavenVR/Net/TownServices/TownServiceBinding.cs']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/card-mip')
    parser.add_argument('--native-art-dir', type=Path, help='Read-only extracted original item PNGs; optional extra hardware-art evidence')
    parser.add_argument('--case', action='append', help='Run named causal variants only, retaining prior independent passes')
    args = parser.parse_args()
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    ui = args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'
    if not unity.is_file() or not ui.is_file(): parser.error('Actual Unity 2021.3.5 and native uGUI are required')
    fixture = ROOT / 'scripts/card-mip-runtime'
    code = {Path(path).name: (args.source_root / path).read_text() for path in SOURCES}
    # Only the actual Image playback branch is bound here; generic native hierarchy,
    # controllers/materials/masks are covered by the existing full mirror fixture.
    # This focused proof links the complete original registry and filtering code.
    binding = code.pop('TownServiceBinding.cs')
    branch = binding[binding.index('                    case TownServiceProperty.Image:', binding.index('    internal void Apply(')):binding.index('                    case TownServiceProperty.RawImage:', binding.index('    internal void Apply('))]
    code['NativeImagePlayback.cs'] = 'using UnityEngine; using UnityEngine.UI; using GloomhavenVR.Net.TownServices; internal static class NativeImagePlayback { internal static void Apply(Transform node, TownServiceAssets assets, float[] n, string[] text) { switch (6) {\n' + branch.replace('TownServiceProperty.Image', '6') + '\n} } private static T Require<T>(Transform node) where T:Component => node.GetComponent<T>(); }'
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    (run / 'source-hashes.json').write_text(json.dumps({path: hashlib.sha256((args.source_root / path).read_bytes()).hexdigest() for path in SOURCES}, indent=2) + '\n')
    files = list(fixture.glob('*')) + [Path(__file__).resolve(), ROOT / 'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs']
    (run / 'fixture-hashes.json').write_text(json.dumps({str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in files if path.is_file()}, indent=2) + '\n')
    variants = [
        ('production', '', '', '', ''),
        ('whole-atlas-small-region', 'CardFaceMipBake.cs', '&& !PreferRegion(source, srcTex)', '', 'small region cannot charge or retain an entire 85 MB atlas'),
        ('skip-override-filter', 'CardFaceMipBake.cs', 'if (!ReferenceEquals(draw, filtered)) { image.overrideSprite = filtered; swapped++; }', 'if (false && !ReferenceEquals(draw, filtered)) { image.overrideSprite = filtered; swapped++; }', 'native base and independent override both use filtered sprites'),
        ('skip-override-arrival', 'CardArtWatch.cs', ' && ReferenceEquals(img.overrideSprite, overrides[i])', '', 'an override-only native art arrival is filtered before rendering'),
        ('observer-original-only', 'NativeImagePlayback.cs', 'image.sprite = GloomhavenVR.Cards.CardFaceMipBake.PresentationFor(assets.Resolve<Sprite>(text[0]));\n                        image.overrideSprite = GloomhavenVR.Cards.CardFaceMipBake.PresentationFor(assets.Resolve<Sprite>(text[1]));', 'image.sprite = assets.Resolve<Sprite>(text[0]);\n                        image.overrideSprite = assets.Resolve<Sprite>(text[1]);', 'observer uses the owning native card filtering policy immediately'),
        ('no-mip-chain', 'CardFaceMipBake.cs', 'regionTex.Apply(updateMipmaps: true, makeNoLongerReadable: true);', 'regionTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);', 'mipmapped native region reduces distance and oblique minification error against supersampled reference'),
    ]
    if args.case:
        names = set(args.case)
        if names - {row[0] for row in variants}: parser.error('Unknown causal variant')
        variants = [row for row in variants if row[0] in names]
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        for path, text in code.items():
            if path == filename:
                if text.count(before) != 1: raise RuntimeError('Causal source binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Mip.csproj'; shutil.copyfile(fixture / 'Mip.csproj', project)
        assembly = 'CardMip_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ui), '-p:UnityTmp=' + str(args.source_root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation is never a passing negative control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    project = run / 'unity'
    for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'): (project / directory).mkdir(parents=True, exist_ok=True)
    for entry in manifest['cases']:
        dest = project / 'Assets/Plugins' / Path(entry['dll']).name
        shutil.copyfile(entry['dll'], dest); entry['dll'] = str(dest)
    runner = (ROOT / 'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs').read_text()
    runner = runner.replace('steps = (IEnumerator)program.GetMethod("Run").Invoke(null, null);', 'program.GetField("OutputDirectory").SetValue(null, ' + json.dumps(str(run)) + '); program.GetField("NativeArtDirectory").SetValue(null, ' + json.dumps(str(args.native_art_dir.resolve()) if args.native_art_dir else '') + '); steps = (IEnumerator)program.GetMethod("Run").Invoke(null, null);')
    (project / 'Assets/Editor/DiagnosticRunner.cs').write_text(runner)
    if args.native_art_dir:
        (run / 'native-input-hashes.json').write_text(json.dumps({str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in args.native_art_dir.glob('*.png')}, indent=2) + '\n')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'DiagnosticRunner.Start', '-interactionManifest', str(path), '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=360)
    for cache in ('Library', 'Temp'): shutil.rmtree(project / cache, ignore_errors=True)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: actual Unity run; see ' + str(run / 'unity.log'))
    print(f'PASS: {len(variants)} actual production/causal variants; evidence: {run}')


if __name__ == '__main__': main()
