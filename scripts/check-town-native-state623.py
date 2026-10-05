#!/usr/bin/env python3
"""Prove sparse native metadata, exact original reconstruction and atomic mage exposure in Unity."""
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
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-native-state623')
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--bank-split-only', action='store_true')
    parser.add_argument('--lifecycle-only', action='store_true', help='Run only cold-template disconnect and existing-bank reset preparation proofs')
    parser.add_argument('--negative-control', action='append', choices=['inert-artwork', 'owner-text', 'complete-picture', 'split-headers', 'owner-state', 'overridden-basis', 'cold-original', 'retained-canvas', 'eager-bank', 'departed-cold-peer', 'reset-preparation'])
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('mirror_source_binding', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(Path(__file__).resolve().parent / 'town-native-state623-runtime/NativeState623.cs', fixture / 'NativeState623.cs')
    if args.lifecycle_only:
        if args.bank_split_only: parser.error('--lifecycle-only and --bank-split-only are mutually exclusive')
        preparation = root / 'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.EnhancementPreparation.cs'
        bound[preparation.name] = preparation.read_text()
        shutil.copyfile(Path(__file__).resolve().parent / 'town-native-state623-runtime/NativeTemplateDelivery625.cs', fixture / 'NativeTemplateDelivery625.cs')
        publisher = fixture / 'Publisher.cs'
        text = publisher.read_text()
        for before, after in [('internal sealed class Part {', 'internal sealed partial class Part {'),
                              ('internal static class TownServiceNativeAssets {', 'internal static partial class TownServiceNativeAssets {')]:
            if text.count(before) != 1: raise RuntimeError('Lifecycle adapter binding drift: ' + before)
            text = text.replace(before, after, 1)
        publisher.write_text(text)
    program = fixture / 'Program.cs'
    anchor = '            if (variant == "production") PublisherNoCloth();'
    branch = '''            if (suite == "native-state623") {
                var state = NativeState623(); while (state.MoveNext()) yield return state.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
            if (suite == "native-bank-split623") {
                var bank = NativeBankSplit623(); while (bank.MoveNext()) yield return bank.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
'''
    text = program.read_text()
    if text.count(anchor) != 1: raise RuntimeError('Fixture entry binding drift')
    program.write_text(text.replace(anchor, anchor + '\n' + branch, 1))
    if args.lifecycle_only:
        anchor = '            DelayedCensusRace();'
        text = program.read_text()
        if text.count(anchor) != 1: raise RuntimeError('Lifecycle-only entry binding drift')
        branch = '''            if (suite == "native-template-lifecycle625") {
                var state = NativeTemplateLifecycle625(); while (state.MoveNext()) yield return state.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
'''
        program.write_text(text.replace(anchor, branch + anchor, 1))
    variants = [('production', None, None, None, '')]
    if args.lifecycle_only and not args.no_negative_controls:
        variants += [
            ('departed-cold-peer', 'TownServiceMirror.cs', 'ForgetUnpreparedNativePeer(peer);', '// omit departed deferred admission lifetime', 'departed cold metadata cannot recreate pending or baseline state'),
            ('reset-preparation', 'NativeTemplates.EnhancementPreparation.cs', '|| _enhancementBasisRevision != TownServiceMirror.NativeTemplatePreparationRevision)', '|| false)', 'network reset rewarms existing frozen originals without changing asset identities'),
        ]
    if not args.no_negative_controls and not args.lifecycle_only:
        variants += [
            ('eager-bank', 'TownServiceMirror.CatalogBank.cs',
             'if (frame.CatalogBank.Prepared)', 'if (false && frame.CatalogBank.Prepared)',
             'prepared page clock excludes synchronous full-bank compression'),
            ('retained-canvas', 'TownCatalogBank.cs',
             'original.CanvasSortingLayer != current.CanvasSortingLayer || original.CanvasSortingOrder != current.CanvasSortingOrder',
             'false', 'cached original key cannot authorize a changed canonical canvas header'),
            ('split-headers', 'TownServiceMirror.CatalogBank.cs',
             'TownServiceFrame reference = TownCatalogClock.Create(frame, NoCatalogPatchBases);',
             'TownServiceFrame reference = TownServiceDelta.Retain(frame); reference.CatalogBank = new TownCatalogBank { Prepared = frame.CatalogBank.Prepared, Members = frame.CatalogBank.Members };',
             'split reference retains every current original header for atomic dependency admission'),
        ]
    if not args.no_negative_controls and not args.bank_split_only and not args.lifecycle_only:
        variants += [
            ('inert-artwork', 'TownServiceMirror.NativeTemplateState.cs', 'binding.Read(Assets, includeInactiveGraphics: true)', 'binding.Read(Assets)', 'actual original prefab produces compact native metadata without a prior network baseline'),
            ('owner-text', 'TownServiceMirror.NativeTemplateState.cs', 'index == 0 || NativeTextProperty(key)\n        || !TownServiceFastNumbers.IsMaterial(key);', 'index == 0 || (!NativeTextProperty(key) && !TownServiceFastNumbers.IsMaterial(key));', 'localized observer defaults never replace exact owner text or font'),
            ('owner-state', 'TownServiceMirror.NativeTemplateState.cs', '|| !TownServiceFastNumbers.IsMaterial(key);', '|| key == TownServiceProperty.Sibling;', 'localized observer defaults never replace exact owner text or font'),
            ('overridden-basis', 'TownServiceMirror.NativeTemplateState.cs', 'if (OwnerProperty(index, key) || basis.Coverage.Length != 0\n                        && (basis.Coverage[index] & (1u << key)) != 0) continue;', 'if (OwnerProperty(index, key)) continue;', 'a fully transmitted owner material replaces a different observer native default immediately'),
            ('cold-original', 'TownServiceMirror.NativeTemplateState.cs', 'bool ready = attempt && TryExpandNativeTemplateState(received, out _);', 'bool ready = false;', 'a retained first sparse offer replays immediately after its original becomes available'),
            ('complete-picture', 'TownServiceMirror.NativeTemplateState.cs', 'if (peer <= 0 || session.Service != 3 || session.Modules.Length == 0) return true;', 'return true;\n#pragma warning disable CS0162', 'a missing offered card prevents a partial ring or options picture'),
        ]
    if args.negative_control:
        variants = [case for case in variants if case[0] == 'production' or case[0] in args.negative_control]
    suite = 'native-template-lifecycle625' if args.lifecycle_only else 'native-bank-split623' if args.bank_split_only else 'native-state623'
    manifest = {'suite': suite, 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if text.count(before) != 1: raise RuntimeError('Mutation binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Mirror.csproj'; shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'TownNativeState623_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        print('Compiled ' + name, flush=True)
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}, indent=2) + '\n')
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True)
    if not args.lifecycle_only:
        shutil.copytree(root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices', project / 'Assets/Resources/TownServices')
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2))
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=300)
    report = Path(manifest['result'])
    if report.exists(): print(report.read_text(), end='')
    if result.returncode or not report.exists(): raise SystemExit('FAIL native metadata proof; see ' + str(run / 'unity.log'))
    print('PASS native metadata source/runtime; evidence: ' + str(run))


if __name__ == '__main__': main()
