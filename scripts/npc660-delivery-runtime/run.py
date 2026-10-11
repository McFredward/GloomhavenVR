#!/usr/bin/env python3
"""Exercise actual null-prefab VRCard originals through native publication and delivery."""
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


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc660-delivery/proof')
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    loader = module('delivery660_mirror', root / 'scripts/check-town-service-mirror.py')
    delivery = module('delivery660_transport', root / 'scripts/check-town-native-state623.py')
    bound, _ = loader.sources(root)
    delivery.bind_delivery_transport(root, bound, loader)
    protocol = (root / 'src/GloomhavenVR/Net/NetProtocol.cs').read_text()
    bound['ReceiptConstant660.cs'] = 'public static partial class MirrorProgram {\n' \
        + loader.expression(protocol, 'public const byte MsgTownOriginalReceipt').replace('public const', 'private const', 1) + '\n}\n'
    fixture = run / 'fixture'
    shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(Path(__file__).with_name('AbilityBody660.cs'), fixture / 'AbilityBody660.cs')
    program = fixture / 'Program.cs'
    text = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    if text.count(anchor) != 1:
        raise RuntimeError('Actual mirror entry drift')
    text = text.replace(anchor, '''            if (suite == "ability-body660") {
                var actual = AbilityBody660(); while (actual.MoveNext()) yield return actual.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
''' + anchor, 1)
    program.write_text(text)
    cases = [('production', None, None, None, '')]
    if not args.no_negative_controls:
        cases += [
            ('missing-original-body', 'LazyNativeTemplates.cs',
             '|| key.StartsWith("map.cardbody.", StringComparison.Ordinal))',
             '|| false)', 'Missing original town widget: map.cardbody.'),
            ('observer-sized-body', 'TownServiceAbilityBody.cs',
             'new FloatBits { Value = size.x }',
             'new FloatBits { Value = size.x * 2f }',
             'canonical body uses exact shared owner mesh at original dimensions'),
            ('lost-contour-consumer', 'TownServiceAbilityBody.cs',
             'CardMesh.AttachBody(filter, CardBodyKind.Ability, width, height);',
             '// omit exact late contour consumer registration',
             'observer body follows the real later original contour completion'),
            ('first-size-pattern-alias', 'TownServiceTemplateAssets.cs',
             'if (CardMesh.IsOriginalBackTexture(texture))',
             'if (texture == null)',
             'independently frozen observer expands exact compact body after inverse owner-size borrow order'),
        ]
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'suite': 'ability-body660', 'evidence': str(run), 'result': str(run / 'results.txt'), 'cases': []}
    raw_sources = [root / 'src/GloomhavenVR/Cards/VRCard.cs', root / 'src/GloomhavenVR/Cards/Art/CardMesh.cs',
                   root / 'src/GloomhavenVR/Cards/Art/CardContour.cs', root / 'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.cs',
                   root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceAbilityBody.cs',
                   root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceTemplateAssets.cs',
                   root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.cs',
                   root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.Stock.cs',
                   root / 'src/GloomhavenVR/Net/ExtrasSendQueue.cs']
    (run / 'source-hashes.json').write_text(json.dumps({
        'raw': {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in raw_sources},
        'bound': {name: hashlib.sha256(value.encode()).hexdigest() for name, value in bound.items()},
        'fixture': {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in fixture.glob('*.cs')},
        'runner': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    }, indent=2) + '\n')
    for name, target, before, after, expected in cases:
        build = run / name
        production = build / 'production'
        production.mkdir(parents=True)
        for filename, value in bound.items():
            if filename == target:
                if value.count(before) != 1:
                    raise RuntimeError('Exact source control drift: ' + name)
                value = value.replace(before, after, 1)
            (production / filename).write_text(value)
        project = build / 'Mirror.csproj'
        shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'Delivery660_' + name.replace('-', '_')
        command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                   '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                   '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
                   '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
                   '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')]
        done = subprocess.run(command, capture_output=True, text=True)
        (build / 'build.log').write_text(done.stdout + done.stderr)
        if done.returncode:
            raise SystemExit(done.stdout + done.stderr)
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        print('Compiled ' + name, flush=True)
    editor = run / 'unity'
    (editor / 'Assets/Editor').mkdir(parents=True)
    (editor / 'Packages').mkdir()
    (editor / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', editor / 'Assets/Editor/MirrorRunner.cs')
    (editor / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (editor / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'
    path.write_text(json.dumps(manifest, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    done = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(editor),
                           '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=240)
    result = Path(manifest['result'])
    if result.exists():
        print(result.read_text(), end='')
    if done.returncode or not result.exists():
        raise SystemExit('FAIL660: ' + str(run / 'unity.log'))
    print('PASS660 actual optional-prefab body, real shared geometry and original delivery; ' + str(run))


if __name__ == '__main__':
    main()
