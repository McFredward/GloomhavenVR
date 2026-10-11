#!/usr/bin/env python3
"""Prove native offered-ring continuity, geometry and immutable template provenance."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc-ring645')
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--case', choices=['production', 'partitioned', 'reversed', 'normal-clock'])
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    python = os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python'))
    native = run / 'native'
    subprocess.run([python, str(root / 'scripts/town-native-overlays635-runtime/export-native.py'),
                    str(root), str(native)], check=True)
    effect = run / 'native-effect.json'
    subprocess.run([python, str(root / 'scripts/town-enhancement-lifecycle635-runtime/export-native.py'),
                    str(root / 'ressources/GH_Data'), str(effect)], check=True)
    descriptor = json.loads(effect.read_text())
    assert descriptor['rotationTime'] == 20 and descriptor['rotationSpeed'] == 1
    assert descriptor['effects'][0]['FromUniqueValue'] == 1
    assert abs(descriptor['effects'][0]['ToUniqueValue'] - .6) < .000001

    spec = importlib.util.spec_from_file_location('mirror645', root / 'scripts/check-town-service-mirror.py')
    binder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(binder)
    bound, hashes = binder.sources(root)
    warm = binder.method((root / 'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.EnhancementPreparation.cs').read_text(),
                         'private static void WarmEnhancementBasis(string key)')
    bound['RingWarm645.cs'] = ('using System;\nusing UnityEngine;\nusing GloomhavenVR.Net.TownServices;\n'
                              'namespace GloomhavenVR.WorldUI;\ninternal static partial class LazyTemplateProbe {\n'
                              + warm + '\n}\n')
    hashes['RingWarm645.cs'] = hashlib.sha256(bound['RingWarm645.cs'].encode()).hexdigest()
    fixture = run / 'fixture'
    shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(Path(__file__).with_name('Shared') / 'NativeRingShared645.cs', fixture / 'NativeRingShared645.cs')
    reader_path = root / 'scripts/town-first-picture632-runtime/FirstPicture632.cs'
    reader = reader_path.read_text().split('    private static IEnumerator FirstPicture632()')[0] + '}\n'
    (fixture / 'OriginalRingReader645.cs').write_text(reader.replace('NativeFirstPicture632', 'NativeRing645'))
    for name in ['OfferedOrientation629.cs', 'NativeRingShared645.cs']:
        path = fixture / name
        path.write_text(path.read_text().replace('AddComponent<AbilityCardUI>()', 'AddComponent<GloomhavenVR.WorldUI.AbilityCardUI>()'))
    program = fixture / 'Program.cs'
    text = program.read_text()
    # Bind a private entry without changing the common fixture.
    import re
    match = re.search(r'            if \(variant == "production"\) PublisherNo\w+\(\);', text)
    if not match:
        raise RuntimeError('Native mirror suite entry drift')
    anchor = match.group(0)
    entry = '''            if (suite == "native-ring645")
            {
                var ring = NativeRingShared645(variant == "partitioned", variant == "reversed" ? -.75f : 1f, variant == "normal-clock");
                while (ring.MoveNext()) yield return ring.Current;
                File.WriteAllText(Path.Combine(_output, "assertions.txt"), _assertions + " assertions\\n");
                yield break;
            }
'''
    program.write_text(text.replace(anchor, entry + anchor, 1))
    managed = root / 'ressources/GH_Data/Managed'
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    purse = run / 'native-purse.json'
    subprocess.run([python, str(root / 'scripts/town-purse-runtime/export-native.py'), str(root), str(purse)], check=True)
    project_text = (fixture / 'Mirror.csproj').read_text().replace('<NoWarn>', '<NoWarn>CS0436;')
    project_text = project_text.replace('  </ItemGroup>',
        '    <Reference Include="GH.Runtime" HintPath="$(GameManaged)/GH.Runtime.dll" Private="false"/>\n'
        '    <Reference Include="GH.Runtime.FirstPass" HintPath="$(GameManaged)/GH.Runtime.FirstPass.dll" Private="false"/>\n  </ItemGroup>')
    cases = [('production', None, ''), ('partitioned', None, ''), ('reversed', None, ''), ('normal-clock', None, '')]
    failure = 'final visible ink advances at its exact authored 20-second native rate on each render'
    if not args.no_negative_controls:
        cases.extend([('prior-sampled-ring', 'old-clock', failure), ('lost-frozen-warm-rate', 'lost-warm', failure)])
    if args.case:
        cases = [case for case in cases if case[0] == args.case]
    manifest = {'suite': 'native-ring645', 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    receipts = {}
    for name, mutation, expected in cases:
        case = run / name
        production = case / 'production'
        production.mkdir(parents=True)
        files = dict(bound)
        if mutation == 'old-clock':
            source = files['TownServiceMirror.Offerings.cs']
            point = '        if (service != 3) return;'
            assert source.count(point) == 1
            source = source.replace('    private static readonly Dictionary<string, NativeRingInfo>',
                                    '    private static bool SkipNativeRing645 = true;\n    private static readonly Dictionary<string, NativeRingInfo>', 1)
            files['TownServiceMirror.Offerings.cs'] = source.replace(point, '        if (SkipNativeRing645 || service != 3) return;', 1)
        elif mutation == 'lost-warm':
            files['RingWarm645.cs'] = files['RingWarm645.cs'].replace(',\n                part.NativeRingRoot, part.NativeRingRate', '')
            assert files['RingWarm645.cs'] != bound['RingWarm645.cs']
        if name != 'normal-clock':
            # Engine-clock boundary only: source LeanTween's manual scaled time
            # and observer Time.timeAsDouble receive the same deterministic samples.
            # The normal-clock case compiles this production line UNCHANGED.
            point = 'motion.Ring?.Draw(holder, print, Time.timeAsDouble,'
            assert files['TownServiceMirror.Offerings.cs'].count(point) == 1
            files['TownServiceMirror.Offerings.cs'] = files['TownServiceMirror.Offerings.cs'].replace(
                point, 'motion.Ring?.Draw(holder, print, global::NativeRingTime645.Time,', 1)
        for file, source in files.items():
            (production / file).write_text(source)
        project = case / 'Mirror.csproj'
        project.write_text(project_text)
        command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=NativeRing645' + name.replace('-', ''), '-p:FixtureDir=' + str(fixture),
            '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(managed / 'UnityEngine.UI.dll'), '-p:UnityTmp=' + str(managed / 'Unity.TextMeshPro.dll'),
            '-p:GameManaged=' + str(managed)]
        result = subprocess.run(command, capture_output=True, text=True)
        (case / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr)
        dll = case / 'bin/Release/netstandard2.1' / ('NativeRing645' + name.replace('-', '') + '.dll')
        manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
        receipts[name] = {'clock': 'unaltered Unity Time.timeAsDouble + native normal-delta LeanTween' if name == 'normal-clock'
                         else 'recorded deterministic scaled Unity clock boundary + original manual LeanTween',
                         'sha256': {file: hashlib.sha256(source.encode()).hexdigest() for file, source in files.items()}}
    manifest_path = run / 'manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    editor = run / 'unity'
    (editor / 'Assets/Editor').mkdir(parents=True)
    (editor / 'Packages').mkdir()
    (editor / 'ProjectSettings').mkdir()
    shutil.copytree(native, editor / 'Assets/NativeRing645')
    runner = (fixture / 'Editor/MirrorRunner.cs').read_text()
    anchor = '        var args = Environment.GetCommandLineArgs();'
    runner = runner.replace(anchor,
        '        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => { string n = new AssemblyName(e.Name).Name + ".dll"; '
        'string f = Path.Combine(' + json.dumps(str(managed)) + ', n); '
        'return File.Exists(f) && !n.StartsWith("UnityEngine") && !n.StartsWith("System.") ? Assembly.LoadFrom(f) : null; };\n' + anchor, 1)
    (editor / 'Assets/Editor/MirrorRunner.cs').write_text(runner)
    (editor / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (editor / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (run / 'source-hashes.json').write_text(json.dumps({'production': hashes, 'cases': receipts,
        'fixture': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.glob('*.cs')},
        'native_dll': {name: hashlib.sha256((managed / name).read_bytes()).hexdigest()
                       for name in ['GH.Runtime.dll', 'GH.Runtime.FirstPass.dll']},
        'reader_sha256': hashlib.sha256(reader_path.read_bytes()).hexdigest()}, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(editor),
                             '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(manifest_path),
                             '-logFile', str(run / 'unity.log')], timeout=300, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    if (run / 'results.txt').exists():
        print((run / 'results.txt').read_text(), end='')
    if result.returncode or not (run / 'results.txt').exists():
        raise SystemExit('FAIL native ring final writer: ' + str(run))
    print('PASS native ring final writer: ' + str(run))


if __name__ == '__main__':
    main()
