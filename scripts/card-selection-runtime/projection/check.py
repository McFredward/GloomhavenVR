#!/usr/bin/env python3
"""Execute the exact tray/fan projection between explicitly supplied native acknowledgements.

The native callback suite proves the real acknowledgements separately. This portable
section runs actual SyncFromGameState/FillHandFan, membership, token and occupancy
methods, with named Unity hierarchy/SetHome boundaries. It is not complete scene,
GPU or headset execution.
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
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def method(text, signature):
    assert text.count(signature) == 1, 'Projection source drift: ' + signature
    start = text.index(signature); brace = text.index('{', start); semi = text.find(';', start)
    if 0 <= semi < brace:
        return text[start:semi + 1]
    end = brace + 1; depth = 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}'); end += 1
    return text[start:end]


def bind(root):
    cards = root / 'src/GloomhavenVR/Cards'
    tray = (cards / 'Tray/PlayTray.4.Slots.cs').read_text()
    driver = (cards / 'Driver/CardsDriver.4.Rebuild.cs').read_text()
    update = (cards / 'Driver/CardsDriver.2.Update.cs').read_text()
    release = (cards / 'Driver/CardsDriver.5.Interactions.cs').read_text()
    core = (cards / 'Tray/PlayTray.1.Core.cs').read_text()
    # The real pump can acknowledge an unselect and rebuild before the next select.
    assert update.index('CardActionQueue.Pump();') < update.index('Rebuild(anchor);')
    assert '_tray.IsSelectionPending(already, _boundHand)' in method(driver, 'private void FillHandFan(')
    assert release.count('_tray.BeginPendingSelection(card)') == 2
    assert release.count('_tray.SelectionWasWithdrawn(pendingSelection)') == 4
    assert release.count('ReconcileInitiative(handRef, leftChoice);') == 2
    assert release.count('_tray.CompletePendingSelection(card, pendingSelection)') == 2
    assert release.count('_tray.WithdrawPendingSelection(') == 2
    scene_release = method(update, 'internal static void ReleaseCardsBeforeSceneLoad()')
    assert scene_release.index('driver._tray.CancelPendingSelections();') < scene_release.index('CardActionQueue.Clear();')
    assert 'CancelPendingSelections();' in method(core, 'internal void Destroy()')
    assert 'FillHandFan();\n                _tray.SyncFromGameState(hand, _factory);' in driver
    methods = ['internal bool SyncFromGameState(', 'private bool PlaceRoundCardIfMissing(',
        'private static AbilityCardUI? FindWidget(', 'internal void PlaceCard(', 'internal void RemoveCard(',
        'internal int SlotOf(', 'internal void ClearSlots(', 'private sealed class PendingRoundSelection',
        'internal object? BeginPendingSelection(', 'internal bool CompletePendingSelection(',
        'internal void CancelPendingSelection(', 'internal void CancelPendingSelections(',
        'internal void WithdrawPendingSelection(', 'internal bool SelectionWasWithdrawn(',
        'internal bool IsSelectionPending(', 'internal int OccupiedSlotMask', 'private static bool SlotHoldsCard(']
    source = 'using System; using System.Collections.Generic; using ScenarioRuleLibrary; using UnityEngine;\npublic partial class PlayTray {\n' \
        + '\n'.join(method(tray, signature) for signature in methods) + '\n}\npublic partial class CardsDriver {\n' \
        + method(driver, 'private void FillHandFan(') + '\n}\n'
    inputs = {'Tray/PlayTray.4.Slots.cs': tray, 'Driver/CardsDriver.4.Rebuild.cs': driver,
              'Driver/CardsDriver.2.Update.cs': update, 'Driver/CardsDriver.5.Interactions.cs': release,
              'Tray/PlayTray.1.Core.cs': core}
    return source, inputs, methods + ['FillHandFan']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/card-selection-runtime/projection')
    parser.add_argument('--production-only', action='store_true')
    args = parser.parse_args(); source, inputs, methods = bind(args.source_root.resolve())
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('projection evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'; completion.write_text('evidence: ' + str(run) + '\n')
    outcome = {'exit_code': 1, 'reaped': True}
    def cleanup():
        receipt = cleanup_completed_run('card-selection-projection', outcome['exit_code'], completion, ROOT, process_reaped=outcome['reaped'])
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    atexit.register(cleanup)
    retained = run / 'Sources'; retained.mkdir()
    fixture = retained / 'fixture'; shutil.copytree(Path(__file__).parent, fixture)
    for name, text in inputs.items():
        target = retained / 'production-inputs' / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_text(text)
    variants = [('production', '', '', '')]
    if not args.production_only:
        variants += [
            ('pending-sync-evicts', '\n                && !IsSelectionPending(occ, hand)', '',
             'pending replacement remains the physical occupant between native callbacks'),
            ('pending-card-enters-fan', '\n                                    || _tray.IsSelectionPending(already, _boundHand)', '',
             'pending replacement cannot also enter fan'),
            ('stale-token-completes-new-drop', '\n                && ReferenceEquals(_pendingRoundSelections[i], token)', '',
             'stale same-card native ack cannot clear a newer placement token'),
            ('explicit-withdraw-ignored', 'pending.ExplicitlyWithdrawn = true;', 'pending.ExplicitlyWithdrawn = false;',
             'only explicit user withdrawal supersedes captured select'),
        ]
    manifest = {'methods': methods, 'source_hashes': {name: hashlib.sha256(text.encode()).hexdigest() for name, text in inputs.items()},
                'limits': __doc__, 'cases': []}
    dotnet = str(Path.home() / '.dotnet/dotnet')
    for name, before, after, expected in variants:
        project = run / name; project.mkdir()
        for file in fixture.glob('*'):
            if file.suffix in ['.cs', '.csproj']: shutil.copyfile(file, project / file.name)
        code = source
        if before:
            assert code.count(before) == 1, 'Projection mutation drift: ' + name
            code = code.replace(before, after, 1)
        (project / 'Production.cs').write_text(code)
        build = subprocess.run([dotnet, 'build', str(project / 'Projection.csproj'), '-c', 'Release', '--nologo', '-v', 'quiet'], capture_output=True, text=True)
        (project / 'build.log').write_text(build.stdout + build.stderr)
        if build.returncode: raise SystemExit('Projection compilation is not a valid control:\n' + build.stdout + build.stderr)
        outcome['reaped'] = False
        log = project / 'run.log'
        with log.open('w') as output:
            process = subprocess.Popen([dotnet, str(project / 'bin/Release/net8.0/Projection.dll')], stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                returncode = process.wait(timeout=30)
            finally:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait(); outcome['reaped'] = True
        text = log.read_text()
        passed = returncode == 0 if not expected else returncode != 0 and expected in text
        manifest['cases'].append({'name': name, 'exit_code': returncode, 'passed': passed, 'expected': expected, 'log': str(log)})
        (run / 'results.json').write_text(json.dumps(manifest, indent=2) + '\n')
        if not passed: raise SystemExit('FAIL projection ' + name + ':\n' + text)
        print(text.strip() if not expected else 'PASS projection negative control ' + name + ': ' + expected)
    outcome['exit_code'] = 0
    print('PASS exact projection and ' + str(len(variants) - 1) + ' causal controls; evidence: ' + str(run))


if __name__ == '__main__': main()
