#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_root="${GHVR_TEST_SOURCE_ROOT:-$repo_root}"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
python3 - "$repo_root" "$source_root" <<'PY'
import hashlib, importlib.util, json
from pathlib import Path
import shutil, subprocess, sys, tempfile, time

root, source_root = map(Path, sys.argv[1:])
fixture = root / 'tests/GloomhavenVR.ModalCloseTests'
out = root / '.planning/debug/modal-close'
out.mkdir(parents=True, exist_ok=True)
run = Path(tempfile.mkdtemp(prefix='run-', dir=out))
log = run / 'console.log'
log.write_text('Evidence: ' + str(run) + '\n')
print('Evidence: ' + str(run), flush=True)
status = 1
started = time.monotonic()
modal = source_root / 'src/GloomhavenVR/WorldUI/Modal'
paths = {
    'DecisionSource': modal / 'MandatoryDecision.cs',
    'TermsSource': modal / 'MandatoryDecisionTerm.cs',
    'StorySource': modal / 'NativeStoryWindow.cs',
    'CloseScopeSource': source_root / 'src/GloomhavenVR/WorldUI/MapRoom/TownWindowCloseScope.cs',
    'ProbeSource': modal / 'WindowProbeCadence.cs',
}
close_source = (modal / 'ModalFallback.7.Close.cs').read_text()
start = close_source.index('    internal static void CloseFloatedWindow(UIWindow? window)')
end = close_source.index('\n    /// <summary>', start)
close = ('using System;using GloomhavenVR.Core;using UnityEngine.UI;'
    'namespace GloomhavenVR.WorldUI;internal static partial class ModalFallback {\n'
    + close_source[start:end] + '\n}\n')
spawn = (modal / 'ModalFallback.9.Spawn.cs').read_text()
def method(signature):
    assert spawn.count(signature) == 1, 'Production method binding drift: ' + signature
    start = spawn.index(signature)
    end = spawn.index('{', start) + 1
    depth = 1
    while depth:
        depth += (spawn[end] == '{') - (spawn[end] == '}')
        end += 1
    return spawn[start:end]
recovery = ('using System.Collections.Generic;using GloomhavenVR.Core;using UnityEngine;using UnityEngine.UI;'
    'namespace GloomhavenVR.WorldUI;internal static partial class ModalFallback {\n'
    'private static readonly HashSet<UIWindow> EmptyRefused=new(),EmptyHold=new();'
    'private static readonly WindowProbeCadence EmptyRefusedProbes=new(),EmptyHoldProbes=new();'
    'internal static int RecoveryProbeCountForTest;'
    'private static bool DrawsAnythingLoose(Transform root){RecoveryProbeCountForTest++;return root.Drawn;}'
    'internal static void HoldForRecoveryTest(UIWindow window,bool refused){(refused?EmptyRefused:EmptyHold).Add(window);}'
    'internal static bool EmptyRecoveryForTest(UIWindow window,bool refused)=>refused?EmptyRefusedNow(window):EmptyHeldNow(window);'
    'internal static void ResetEmptyRecoveryForTest(){EmptyRefused.Clear();EmptyHold.Clear();EmptyRefusedProbes.Reset();EmptyHoldProbes.Reset();RecoveryProbeCountForTest=0;}'
    + method('private static bool EmptyRefusedNow(') + '\n' + method('private static bool EmptyHeldNow(') + '\n}')
cases = [('production', close, recovery, None)]
guard_start = close.index('        if (IsMandatoryDecision(window, out string mandatoryReason))')
guard_end = close.index('\n\n', guard_start)
guard = close[guard_start:guard_end]
for name, before, after, expected in [
    ('missing-guard', guard, '', 'stale close plate cannot hide a newly mandatory pooled dialog'),
    ('late-guard', guard, 'FindPanel(window)!.UserClosing = true;\n' + guard, 'pooled policy change is rechecked before conversion release'),
    ('no-rescue', 'RescueForMandatoryDecision(window, mandatoryReason, 0f, "a mod close request");', '{}', 'pooled policy change is rechecked before conversion release'),
    ('hide-before-rescue', 'RescueForMandatoryDecision(window, mandatoryReason, 0f, "a mod close request");', 'window.Hide(); RescueForMandatoryDecision(window, mandatoryReason, 0f, "a mod close request");', 'stale close plate cannot hide a newly mandatory pooled dialog'),
    ('shared-quest-fallthrough', '        if (MapRoom.MapLocationInteractor.HandleSharedQuestClose(window))\n            return;', '', 'shared quest semantic close must stop local-only modal release'),
    ('shared-city-fallthrough', '        if (ReferenceEquals(MapRoom.MapCityEventReadyUp.Window, window))\n            return;', '', 'shared city readiness cannot be privately hidden or marked for local release'),
    ('missing-navigation-intent', '        ConfirmationOnOwner.BeforeUserWindowClose(window);', '', 'admitted user close dispatches its decision decline intent exactly once'),
    ('navigation-before-permission', '        if (IsMapRoomPermanent(window))', '        ConfirmationOnOwner.BeforeUserWindowClose(window);\n        if (IsMapRoomPermanent(window))', 'mandatory close never becomes a decision decline intent'),
]:
    assert close.count(before) == 1, 'Close mutation binding drift: ' + name
    cases.append((name, close.replace(before, after), recovery, expected))
for name, probe, expected in [
    ('shared-refused-probe', 'EmptyRefusedProbes', 'later populated refused window cannot be starved by an earlier empty sibling'),
    ('shared-dormant-probe', 'EmptyHoldProbes', 'later populated dormant window cannot be starved by an earlier empty sibling'),
]:
    before = 'if (!' + probe + '.Due(window, now))\n            return true;'
    after = 'if(now < _legacySharedProbe) return true; _legacySharedProbe = now + 1f / 6f;'
    assert recovery.count(before) == 1, 'Recovery mutation binding drift: ' + name
    mutated = recovery.replace(before, after).replace('internal static int RecoveryProbeCountForTest;',
        'private static float _legacySharedProbe;internal static int RecoveryProbeCountForTest;')
    cases.append((name, close, mutated, expected))
bindings = [*paths.values(), modal / 'ModalFallback.7.Close.cs', modal / 'ModalFallback.9.Spawn.cs',
    modal / 'ModalFallback.10.CatchAll.cs', *fixture.glob('*.cs'), *fixture.glob('*.csproj'), root / 'scripts/modal-close-tests.sh', root / 'scripts/town_run_retention.py']
(run / 'source-bindings.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in bindings}, indent=2) + '\n')
try:
    for name, source, empty, expected in cases:
        case = run / name
        project = case / 'project'
        project.mkdir(parents=True)
        for original in fixture.iterdir():
            if original.suffix in ('.cs', '.csproj'): shutil.copy2(original, project / original.name)
        close_path, empty_path = case / 'Close.fixture', case / 'EmptyRecovery.fixture'
        close_path.write_text(source); empty_path.write_text(empty)
        command = ['dotnet', 'run', '--project', str(project / 'GloomhavenVR.ModalCloseTests.csproj'), '--configuration', 'Release',
            '--property:CloseSource=' + str(close_path), '--property:EmptyRecoverySource=' + str(empty_path),
            *['--property:' + key + '=' + str(value) for key,value in paths.items()]]
        result = subprocess.run(command, capture_output=True, text=True)
        transcript = result.stdout + result.stderr
        (case / 'result.log').write_text(transcript)
        executed = case / 'executed-assemblies'; executed.mkdir()
        for artifact in (project / 'bin/Release/net8.0').glob('*'):
            if artifact.is_file(): shutil.copy2(artifact, executed / artifact.name)
        (case / 'executed-pins.json').write_text(json.dumps({p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in executed.iterdir()}, indent=2) + '\n')
        with log.open('a') as stream: stream.write(name + '\n' + transcript + '\n')
        if expected is None:
            print(result.stdout, end='', flush=True)
            if result.returncode: raise RuntimeError('Production modal close fixture failed: ' + str(case / 'result.log'))
        elif result.returncode == 0 or expected not in transcript:
            raise RuntimeError('Defect control escaped or failed at an unrelated boundary: ' + str(case / 'result.log'))
        else: print('Modal close negative control: ' + name + ' rejected.', flush=True)
    status = 0
finally:
    (run / 'validation.json').write_text(json.dumps({'status':status,'variants':len(cases),'elapsed_seconds':time.monotonic()-started}, indent=2) + '\n')
    spec = importlib.util.spec_from_file_location('retention', root / 'scripts/town_run_retention.py')
    retention = importlib.util.module_from_spec(spec); spec.loader.exec_module(retention)
    receipt = retention.cleanup_completed_run('modal-close', status, log, root, process_reaped=True)
    (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
PY
