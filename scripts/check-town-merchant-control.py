#!/usr/bin/env python3
"""Exercise actual public cabinet input, host dedup and original drawer clocks in Unity.

Native card/row presenters and transport are declared fixture ports. The production
Select/RequestTurn bridge, new54-byte wire, host receiver and crank release are bound
verbatim. This does not claim hardware render or internet latency parity.
"""
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


def method(source, signature):
    at = source.index(signature)
    start = source.index('{', at)
    depth, end = 1, start + 1
    while depth:
        if source[end] == '{': depth += 1
        if source[end] == '}': depth -= 1
        end += 1
    return source[at:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-merchant-control')
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('catalog_binding', root / 'scripts/check-town-service-catalog.py')
    binder = importlib.util.module_from_spec(spec); spec.loader.exec_module(binder)
    bound, hashes = binder.sources(root)
    public = (root / 'src/GloomhavenVR/WorldUI/TownServices/TownServicePublicMerchant.cs').read_text()
    bridge = []
    for signature in ('internal static bool TrySelectCategory', 'internal static bool TryTurnPage',
                      'internal static bool ApplyOriginalControl', 'internal static TownRackState? ControlClock',
                      'internal static void ApplySharedControlClock'):
        bridge.append(method(public, signature))
    bound['PublicControl.cs'] = ('using GloomhavenVR.Net.TownServices; using GloomhavenVR.WorldUI.MapRoom; '
                                'namespace GloomhavenVR.WorldUI; internal static partial class TownServicePublicMerchant { '
                                'internal static uint Session => 1;\n' + '\n'.join(bridge) + '\n}')
    for name in ('TownMerchantControlCodec.cs', 'TownMerchantControlSync.cs'):
        bound[name] = (root / 'src/GloomhavenVR/Net/Avatar' / name).read_text()
        hashes[name] = hashlib.sha256(bound[name].encode()).hexdigest()
    interface = (root / 'src/GloomhavenVR/Net/INetTransport.cs').read_text()
    bound['INetTransport.cs'] = interface[:interface.index('/// <summary>No-op transport')]
    protocol = (root / 'src/GloomhavenVR/Net/NetProtocol.cs').read_text()
    constants = '\n'.join(line.strip() for line in protocol.splitlines()
                          if line.strip().startswith(('public const uint Magic =', 'public const byte Version =')))
    bound['ProtocolConstants.cs'] = 'namespace GloomhavenVR.Net; internal static class NetProtocol { ' + constants + ' }'
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-catalog-runtime', fixture)
    shutil.copyfile(root / 'scripts/town-merchant-control-runtime/Control.cs', fixture / 'Control.cs')
    boundary = (fixture / 'Boundaries.cs').read_text()
    for signature in ('internal static bool TrySelectCategory', 'internal static bool TryTurnPage'):
        boundary = boundary.replace(method(boundary, signature), '')
    boundary = boundary.replace('public static NetworkPlayer? MyPlayer;', 'public static NetworkPlayer? MyPlayer; public static int HostPlayerID;')
    boundary = boundary.replace('internal static readonly HashSet<int> ForeignStock = new();',
        'internal static int AuthorityClaims; internal static void ClaimPublicCatalog(){AuthorityClaims++;} '
        'internal static bool IsPublicAuthor => ControlFixture.LocalPeer == FFSNet.PlayerRegistry.HostPlayerID; '
        'internal static void ApplyPublicControlClock(int author,uint session,TownRackState clock){} '
        'internal static readonly HashSet<int> ForeignStock = new();')
    (fixture / 'Boundaries.cs').write_text(boundary)
    program = (fixture / 'Program.cs').read_text().replace('        NativeBankPreparationProof(inventory, anchor.transform);',
        '        NativeBankPreparationProof(inventory, anchor.transform);\n        MerchantControlProof(inventory, anchor.transform);')
    (fixture / 'Program.cs').write_text(program)
    variants = [('production', None, None, None, '')]
    if not args.no_negative_controls:
        variants += [
            ('wire-counter-swap', 'TownMerchantControlCodec.cs',
             'writer.Write(message.Session); writer.Write(message.Sequence);',
             'writer.Write(message.Sequence); writer.Write(message.Session);',
             'public request matches independent byte-exact golden layout'),
            ('crank-local-bypass', 'TownServiceMerchantDrawer.cs', 'TownServicePublicMerchant.TryTurnPage(this, 1);', 'RequestTurn();',
             'physical crank release follows the same shared intent'),
            ('duplicate-page-replay', 'TownMerchantControlSync.cs', '!TownMerchantControlCodec.Newer(message.Sequence, previous)', 'false',
             'duplicate reliable request cannot replay the page turn'),
            ('bank-authority-reset', 'PublicControl.cs', 'TownServiceMerchantDrawer rack = _catalog.Drawers[0];',
             'TownServiceMirror.ClaimPublicCatalog(); TownServiceMerchantDrawer rack = _catalog.Drawers[0];',
             'public input preserves the complete prepared original bank'),
        ]
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    ui = root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    hashes['PublicControl.cs'] = hashlib.sha256(bound['PublicControl.cs'].encode()).hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(root), 'sha256': hashes}, indent=2) + '\n')
    for name, filename, before, after, expected in variants:
        case = run / name; production = case / 'production'; production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if before not in text: raise RuntimeError('Mutation source drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        shutil.copyfile(fixture / 'Interaction.csproj', case / 'Interaction.csproj')
        assembly = 'MerchantControl_' + name.replace('-', '_')
        command = [shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet'), 'build', str(case / 'Interaction.csproj'), '--configuration', 'Release', '--nologo',
                   '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture),
                   '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ui.resolve())]
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (case / 'build.log').write_text(result.stdout)
        if result.returncode: print(result.stdout); raise SystemExit('FAIL compilation: ' + name)
        manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        print('Compiled ' + name, flush=True)
    (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run([str(unity), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'InteractionRunner.Start',
                             '-interactionManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')],
                            stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    evidence = Path(manifest['result'])
    if evidence.exists(): print(evidence.read_text(), end='')
    if result.returncode or not evidence.exists(): raise SystemExit('FAIL runtime: ' + str(run))
    print('PASS merchant control: ' + str(len(variants)) + ' variants; evidence: ' + str(run))


if __name__ == '__main__': main()
