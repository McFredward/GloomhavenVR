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
    parser.add_argument('--negative-control', action='append', choices=['inert-artwork', 'owner-text', 'complete-picture'])
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('mirror_source_binding', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(Path(__file__).resolve().parent / 'town-native-state623-runtime/NativeState623.cs', fixture / 'NativeState623.cs')
    program = fixture / 'Program.cs'
    anchor = '            if (variant == "production") PublisherNoCloth();'
    branch = '''            if (suite == "native-state623") {
                var state = NativeState623(); while (state.MoveNext()) yield return state.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
'''
    text = program.read_text()
    if text.count(anchor) != 1: raise RuntimeError('Fixture entry binding drift')
    program.write_text(text.replace(anchor, anchor + '\n' + branch, 1))
    variants = [('production', None, None, None, '')]
    if not args.no_negative_controls:
        variants += [
            ('inert-artwork', 'TownServiceMirror.NativeTemplateState.cs', 'binding.Read(Assets, includeInactiveGraphics: true)', 'binding.Read(Assets)', 'actual original prefab produces compact native metadata without a prior network baseline'),
            ('owner-text', 'TownServiceMirror.NativeTemplateState.cs', 'index == 0 || NativeTextProperty(key)', 'index == 0', 'localized observer defaults never replace exact owner text or font'),
            ('complete-picture', 'TownServiceMirror.NativeTemplateState.cs', 'if (peer <= 0 || session.Service != 3 || session.Modules.Length == 0) return true;', 'return true;\n#pragma warning disable CS0162', 'a missing offered card prevents a partial ring or options picture'),
        ]
    if args.negative_control:
        variants = [case for case in variants if case[0] == 'production' or case[0] in args.negative_control]
    manifest = {'suite': 'native-state623', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
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
