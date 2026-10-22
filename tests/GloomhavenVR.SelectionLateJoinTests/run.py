#!/usr/bin/env python3
"""Execute production hand admission, queued recovery and wanted-slot methods.

The native game/Unity boundary is explicit in Boundaries.cs. This portable fixture
does not run the game-owned ownership callback or render headset pixels. Hardware
logs and the retained original decompile establish that callback's foreign-hand
output; the fixture supplies it and executes the actual mod recovery end to end.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def method(source, signature):
    start = source.index(signature)
    at = source.index('{', start)
    depth, end = 1, at + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    parent = ROOT / '.planning/debug/selection-latejoin'
    parent.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=parent))
    project = run / 'project'
    project.mkdir()
    for path in Path(__file__).parent.glob('*.cs*'):
        shutil.copyfile(path, project / path.name)
    api_path = ROOT / 'src/GloomhavenVR/Cards/CardsGameApi.cs'
    flow_path = ROOT / 'src/GloomhavenVR/Cards/Driver/CardsDriver.6.Flows.cs'
    update_path = ROOT / 'src/GloomhavenVR/Cards/Driver/CardsDriver.2.Update.cs'
    api, flow, update = (p.read_text() for p in (api_path, flow_path, update_path))
    api_methods = '\n'.join(method(api, s) for s in ('internal static CardsHandUI? ActiveHand(', 'internal static CPlayerActor? SelectionHandDrift(', 'internal static bool SwitchHandTo('))
    driver_methods = '\n'.join(method(flow, s) for s in ('private void PumpSelectionHandSwitch(', 'private void UpdateWantedSlots(', 'private bool PlacementIsOffered(')) + '\n' + method(update, 'private CardsHandUI? CurrentHand(')
    assert 'PumpSelectionHandSwitch();' in method(update, 'private void TickInteractionsAndStatus(')
    (project / 'ProductionApi.cs').write_text('using System.Collections.Generic; using ScenarioRuleLibrary; namespace GloomhavenVR.Cards; internal static partial class CardsGameApi {\n' + api_methods + '\n}')
    (project / 'ProductionDriver.cs').write_text('using ScenarioRuleLibrary; using UnityEngine; using GloomhavenVR.Core; namespace GloomhavenVR.Cards; internal sealed partial class CardsDriver {\n' + driver_methods + '\n}')
    (run / 'source-manifest.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in (api_path, flow_path, update_path)}, indent=2) + '\n')
    command = [str(Path.home() / '.dotnet/dotnet'), 'run', '--project', str(project / 'SelectionLateJoin.csproj'), '-c', 'Release']

    def execute(label):
        log = run / (label + '.log')
        with log.open('w') as stream:
            stream.write(f'evidence: {run}\n')
            stream.flush()
            process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                result = process.wait(timeout=90)
            finally:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait()
                print(cleanup_completed_run('selection-latejoin', process.returncode, log, ROOT, process_reaped=True), flush=True)
        return result, log.read_text()

    status, output = execute('positive')
    print(output, end='')
    if status: return status
    controls = [
        ('foreign-selection-refusal', 'ProductionApi.cs', 'selected = null;', 'if (bool.Parse("true")) return null; selected = null;', 'Latejoin must resolve the newly owned hand'),
        ('candidate-ownership', 'ProductionApi.cs', 'actor != null && !actor.IsDead && ControlsActor(actor)', 'actor != null && !actor.IsDead', 'Latejoin must resolve the newly owned hand'),
        ('native-phase', 'ProductionApi.cs', 'if (PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)', 'if (bool.Parse("false"))', 'Native non-selection phases must prevent'),
        ('live-queue-revalidation', 'ProductionDriver.cs', 'CPlayerActor? fresh = CardsGameApi.SelectionHandDrift();', 'CPlayerActor? fresh = drift;', 'Queued recovery must revalidate'),
        ('modal-admission', 'ProductionDriver.cs', 'if (WorldUI.ModalFallback.BlockingWindowModalActive)', 'if (bool.Parse("false"))', 'A genuine blocking modal must defer'),
        ('native-decision', 'ProductionApi.cs', 'if (DecidingHand() != null)', 'if (bool.Parse("false"))', 'A genuine deciding flow must retain'),
    ]
    for label, name, old, new, expected in controls:
        path = project / name
        original = path.read_text()
        assert original.count(old) == 1, label
        path.write_text(original.replace(old, new))
        try:
            status, output = execute(label)
            if status == 0 or expected not in output or 'error CS' in output:
                print(output, end='')
                raise RuntimeError(f'{label} did not fail for its intended behavior')
            print(f'Selection latejoin causal control: {label} failed as expected.')
        finally:
            path.write_text(original)
    print(f'evidence: {run}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
