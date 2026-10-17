#!/usr/bin/env python3
"""Prove nullable-identity withdrawal using production release code and real native card callbacks.

The ordinary release branch, fan identity/order methods and memory execute unchanged.
GH.Runtime Select/Unselect/OnClick/ToggleSelect/OnCardSelected/OnCardDeselected execute
against the real ScenarioRuleLibrary queue consumer and MoveAbilityCard implementation.
The loaded-save lookup, offline transport and picture-only widget methods are explicit
boundaries. Physical rays, complete scene rebuilding, UI tutorial profiles, networking
and headset pictures are outside this focused receipt.
"""
import argparse
import atexit
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    assert source.count(signature) == 1, 'Source binding drift: ' + signature
    start = source.index(signature)
    brace = source.index('{', start)
    # Expression-bodied helpers are retained verbatim too.
    arrow = source.find('=>', start, brace)
    if arrow >= 0:
        return source[start:source.index(';', arrow) + 1]
    end = brace + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def bind(source_root):
    cards = source_root / 'src/GloomhavenVR/Cards'
    paths = ['Driver/CardsDriver.4.Rebuild.cs', 'Driver/CardsDriver.5.Interactions.cs', 'FanOrderMemory.cs', 'CardsGameApi.cs', 'Driver/CardsDriver.6.Flows.cs']
    texts = {path: (cards / path).read_text() for path in paths}
    release = method(texts[paths[1]], 'private void OnCardReleased(')
    assert release.index('if (!CardsGameApi.HandOwnsWidget(gameHand, card.GameCard))') < release.index('int highlightSlot =')
    block = release[release.index('        int highlightSlot ='):]
    methods = [method(texts[paths[0]], signature) for signature in [
        'private static int FanId(', 'private static bool TryFanKey(',
        'private static FanOrderMemory FanOrdersFor(', 'private void ReorderFanBuffer()',
        'private void ClearFanInsertion()', 'private void CommitFanInsertion(']]
    methods += [method(texts[paths[4]], signature) for signature in ['private void ReconcileInitiative(',
        'private static string CardName(', 'private static int CardInit(', 'private void OnSwapRequested()']]
    api = texts['CardsGameApi.cs']
    assert 'hand.SelectCard(card);' in method(api, 'internal static void SelectCard(')
    assert 'hand.UnselectCard(card);' in method(api, 'internal static void UnselectCard(')
    generated = '''using System.Collections.Generic; using GloomhavenVR.Core; using GloomhavenVR.Hands;
using ScenarioRuleLibrary; using UnityEngine;
namespace GloomhavenVR.Cards { internal sealed partial class CardsDriver {
''' + '\n'.join(methods) + '''
private void ReleaseSelection(VRCard card, VRHand hand, CardsHandUI gameHand, bool fanOrigin) {
if (card.GameCard == null) throw new System.InvalidOperationException("fixture requires original native card");
''' + block + '\n}}\n'
    bound = {'Driver.cs': generated, 'FanOrderMemory.cs': texts['FanOrderMemory.cs'],
        'Api.cs': 'using System.Collections.Generic; using ScenarioRuleLibrary;\nnamespace GloomhavenVR.Cards { internal static partial class CardsGameApi {\n'
        + method(api, 'internal static bool SwapInitiative(') + '\n' + method(api, 'internal static bool HandOwnsWidget(') + '\n}}\n'}
    return bound, {str((cards / path).relative_to(source_root)): hashlib.sha256(text.encode()).hexdigest() for path, text in texts.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/card-selection-runtime')
    parser.add_argument('--production-only', action='store_true')
    args = parser.parse_args(); source_root = args.source_root.resolve()
    bound, hashes = bind(source_root)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('evidence: ' + str(run) + '\n')
    outcome = {'exit_code': 1, 'reaped': True}
    def cleanup():
        receipt = cleanup_completed_run('card-selection-runtime', outcome['exit_code'], completion, ROOT, process_reaped=outcome['reaped'])
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    atexit.register(cleanup)
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    game = source_root / 'ressources/GH_Data/Managed'
    fixture = ROOT / 'scripts/card-selection-runtime'
    retained = run / 'Sources'; retained.mkdir(); shutil.copytree(fixture, retained / 'fixture')
    shutil.copyfile(Path(__file__), retained / 'check-card-selection-runtime.py'); fixture = retained / 'fixture'
    (run / 'source-hashes.json').write_text(json.dumps({'production': hashes,
        'fixture': {str(file.relative_to(retained)): hashlib.sha256(file.read_bytes()).hexdigest() for file in retained.rglob('*') if file.is_file()},
        'publisher': {name: hashlib.sha256((game / name).read_bytes()).hexdigest() for name in ['GH.Runtime.dll', 'ScenarioRuleLibrary.dll', 'MapRuleLibrary.dll']},
        'limits': __doc__}, indent=2) + '\n')
    # The old direct CharacterName lookup is inserted at its original causal seam:
    # after physical removal but before native deselection can enter the queue.
    variants = [('production', '', '', '', '')]
    if not args.production_only:
        variants += [
            ('old-null-name-abort', 'Driver.cs', '            List<int>? returnOrder = gap >= 0 && TryFanKey(card, out string character, out _)\n                ? FanOrdersFor(card).ForCharacter(character) : null;',
             '            List<int>? returnOrder = _fanOrders.ForCharacter(gameHand.PlayerActor.CharacterName);',
             'nullable presentation identity must never abort native deselection'),
            ('missing-native-withdrawal', 'Driver.cs', '() => CardsGameApi.UnselectCard(handRef, ability),', '() => {},',
             'focus change before withdrawal pump: original round pile follows intended selection'),
            ('missing-guid-order', 'Driver.cs', 'character = widget.PlayerActor.ActorGuid;', 'character = string.Empty;',
             'unnamed native actor can reorder its normal fan'),
            ('identity-domain-collision', 'Driver.cs', '? _unnamedFanOrders : _fanOrders;', '? _fanOrders : _fanOrders;',
             'named display equal to unnamed GUID cannot collide with its order'),
        ]
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    packages = Path.home() / '.nuget/packages'
    harmony_path = packages / 'harmonyx/2.7.0/lib/net45/0Harmony.dll'
    for name, file, before, after, expected in variants:
        case = run / name; production = case / 'production'; production.mkdir(parents=True)
        for filename, source in bound.items():
            if filename == file:
                assert source.count(before) == 1, 'Mutation seam drift: ' + name
                source = source.replace(before, after, 1)
            (production / filename).write_text(source)
        csproj = case / 'Selection.csproj'; shutil.copyfile(fixture / 'Selection.csproj', csproj)
        assembly = 'CardSelection680_' + name.replace('-', '_')
        build = subprocess.run([str(Path.home() / '.dotnet/dotnet'), 'build', str(csproj), '-c', 'Release', '--nologo', '-v', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(game), '-p:HarmonyPath=' + str(harmony_path)], capture_output=True, text=True)
        (case / 'build.log').write_text(build.stdout + build.stderr)
        if build.returncode: raise SystemExit(build.stdout + build.stderr)
        manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    deps = run / 'dependencies'; deps.mkdir()
    for package, version, framework, name in [('harmonyx', '2.7.0', 'net45', '0Harmony'),
        ('monomod.runtimedetour', '21.12.13.1', 'net452', 'MonoMod.RuntimeDetour'), ('monomod.utils', '21.12.13.1', 'net452', 'MonoMod.Utils'),
        ('mono.cecil', '0.11.4', 'net40', 'Mono.Cecil')]:
        shutil.copyfile(packages / package / version / 'lib' / framework / (name + '.dll'), deps / (name + '.dll'))
    project = run / 'unity'
    for name in ['Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings']: (project / name).mkdir(parents=True, exist_ok=True)
    shutil.copyfile(game / 'UnityEngine.UI.dll', project / 'Assets/Plugins/UnityEngine.UI.dll')
    shutil.copyfile(fixture / 'Editor/SelectionRunner.cs', project / 'Assets/Editor/SelectionRunner.cs')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.' + name: '1.0.0' for name in ['ui', 'physics', 'imgui', 'animation', 'audio', 'particlesystem', 'assetbundle']}}) + '\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    command = [str(unity), '-batchmode', '-nographics', '-disable-assembly-updater', '-projectPath', str(project),
        '-executeMethod', 'SelectionRunner.Start', '-interactionManifest', str(manifest_path), '-nativeGameManaged', str(game),
        '-nativeHarmony', str(deps), '-logFile', str(run / 'unity.log')]
    outcome['reaped'] = False
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
    try:
        returncode = process.wait(timeout=300)
    finally:
        try: os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError: pass
        process.wait(); outcome['reaped'] = True
    receipt = Path(manifest['result'])
    if receipt.is_file(): print(receipt.read_text(), end='')
    if returncode or not receipt.is_file(): raise SystemExit('FAIL card selection runtime; see ' + str(run / 'unity.log'))
    outcome['exit_code'] = 0
    print('PASS original native selection and ' + str(len(variants) - 1) + ' causal controls; evidence: ' + str(run))


if __name__ == '__main__': main()
