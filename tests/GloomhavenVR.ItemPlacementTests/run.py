#!/usr/bin/env python3
"""Exercise original item drop/close lifetimes with explicit native API boundaries."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run

METHODS = [
    'internal void Close(', 'private void CloseAfterPlacement()',
    'private static void ClaimUseBerthForPlacement()',
    'internal void OnChipReleased(', 'private void HandleBonusDrop(',
    'private void HandleDemandDrop(', 'private void HandleTakeDamageDrop(',
    'private void AfterRefusedDrop(', 'private void TickPlacedWhileClosed(',
    'private void CancelPlacedCard(', 'private void ReturnPlacedChipHome(',
    'private void RetireChipToPile(', 'private ItemChip? HeldDemandCandidate()',
    'private ItemChip? HeldTakeDamageCandidate()',
    'private void EndDemand(', 'private void EndTakeDamagePick(',
    'internal int ClippedChipIndex', 'internal bool OwnsUseBerth',
    'private bool UseAnimationPending', 'internal void RetirePlacedCardIfAny(',
    'internal void Tick(', 'private bool AnyHeld()',
    'private void ConfirmDemandPick()', 'private void BeginUsedChipPresentation(',
    'private void CompleteUsedChipPresentation(', 'internal void TickDemandPick(',
]


def extract(source, signature):
    start = source.index(signature)
    if signature in ('private void CloseAfterPlacement()', 'internal bool OwnsUseBerth', 'private bool UseAnimationPending'):
        return source[start:source.index(';', start) + 1]
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def execute(command, log):
    with log.open('w') as stream:
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                   start_new_session=True)
        try:
            return process.wait(timeout=90)
        finally:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()


def main():
    destination = ROOT / '.planning/debug/item-placement-runtime'
    destination.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=destination))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    print('evidence: ' + str(run), flush=True)
    outcome = 1
    try:
        fixture = run / 'project'
        fixture.mkdir(parents=True)
        for path in Path(__file__).resolve().parent.glob('*'):
            if path.is_file():
                shutil.copyfile(path, fixture / path.name)
        original = ROOT / 'src/GloomhavenVR/Cards/Piles/ItemsPile.cs'
        source = original.read_text()
        (run / 'Sources').mkdir()
        (run / 'Sources/ItemsPile.cs.txt').write_text(source)
        hashes = {str(original.relative_to(ROOT)): hashlib.sha256(original.read_bytes()).hexdigest()}
        for path in fixture.glob('*'):
            hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
        (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
        body = '\n'.join(extract(source, method) for method in METHODS)
        production = ('using UnityEngine; using ScenarioRuleLibrary; using GloomhavenVR.Hands;\n'
                      'namespace GloomhavenVR.Cards; internal sealed partial class ItemsPile {\n' + body + '\n}\n')
        path = fixture / 'Production.cs'
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        mutations = [
            ('production', '', '', ''),
            ('missing-close', 'CloseAfterPlacement();', '_ = chip;', 'Accepted item placement must close the fan'),
            ('missing-berth-yield', 'CardsDriver.YieldActiveDismissBerthToItem();', '_ = PlayTray.Current;', 'Accepted item must release active dismissal before installing its cap'),
            ('missing-item-slot-restore', 'CardsDriver.YieldActiveDismissBerthToItem();\n        PlayTray.Current?.SetItemUseSlotVisible(true);', 'CardsDriver.YieldActiveDismissBerthToItem();', 'Placed item overlay must stay visible: demand'),
            ('missing-demand-survivor', '_pendingUseChip ?? _demandChip ?? _tdChip', '_pendingUseChip ?? _tdChip', 'Placed card must remain in its slot: demand'),
            ('missing-td-survivor', '_pendingUseChip ?? _demandChip ?? _tdChip', '_pendingUseChip ?? _demandChip', 'Placed card must remain in its slot: damage'),
            ('missing-closed-pump-owner', 'if (ReferenceEquals(chip, _demandChip) || ReferenceEquals(chip, _tdChip))', 'if (bool.Parse("false"))', 'Demand decision must remain owned while closed'),
            ('refused-drop-no-retirement', 'CancelPlacedCard(chip, "taken out of the recess and released away from it — the CANCEL");', '_ = chip;', 'Refused closed-fan return must retire the card'),
            ('accepted-result-retired-off-turn', 'if (UseAnimationPending)\n            return;\n        if (_keptClip != null)', 'if (_keptClip != null)', "Off-turn retirement must not preempt an accepted demand's native result"),
            ('closed-picker-hides-native-result', 'SetItemUseSlotVisible(UseAnimationPending);', 'SetItemUseSlotVisible(false);', "Closing demand must keep the native result's parent active"),
            ('completion-hides-following-demand', 'SetItemUseSlotVisible(_demandActive);', 'SetItemUseSlotVisible(false);', "Old completion must retain a successor's live berth: demand"),
            ('callback-reclassifies-forfeit', 'bool consumed = loseReward', 'bool consumed = _demandLoseReward', "Native callback reentry must preserve the accepted demand's burn/tap classification: reward"),
            ('callback-reclassifies-refresh', '|| (!refreshing', '|| (!_demandRefreshing', "Native callback reentry must preserve the accepted demand's burn/tap classification: consume"),
        ]
        cases = []
        for name, old, new, expected in mutations:
            assert not old or old in production, name
            path.write_text(production.replace(old, new) if old else production)
            code = execute([dotnet, 'run', '--project', str(fixture / 'GloomhavenVR.ItemPlacementTests.csproj'),
                            '--configuration', 'Release'], run / (name + '.log'))
            log = (run / (name + '.log')).read_text()
            if name == 'production':
                if code:
                    raise RuntimeError(log)
                print(log.rstrip(), flush=True)
            elif not code or expected not in log:
                raise RuntimeError(name + ' did not fail for its causal assertion:\n' + log)
            else:
                print('Item placement negative control: ' + name + ' failed as expected.', flush=True)
            cases.append({'case': name, 'exit_code': code, 'expected': expected})
        path.write_text(production)
        (run / 'results.json').write_text(json.dumps({'cases': cases, 'scope': 'Exact production drop/close/closed-survivor/home, demand-confirm/pump, result-ownership/completion and Tick methods. Native callbacks, visual hierarchy and animation completion are explicit boundaries: this fixture does not execute Unity ItemChip.Update, native FX, the game, headset rendering or network transport. The separate item-burn fixture executes original result-playback kernels.'}, indent=2) + '\n')
        outcome = 0
        return 0
    finally:
        result = cleanup_completed_run('item-placement', outcome, completion, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())
