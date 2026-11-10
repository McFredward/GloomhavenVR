#!/usr/bin/env python3
"""Prove native scenario-start catchup and queued victory dispatch across offline hosting/late join.

Original native story/action/win method bodies and production lifecycle/83 ledger execute.
Scene/model identities, first-page animation, scheduling, sockets and result drawing are
declared ports. Existing Results Unity presentation separately owns original final pixels.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tempfile
import time

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    start = source.index(signature)
    at = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[at] == '{') - (source[at] == '}')
        at += 1
    return source[start:at]


def run_command(command, log):
    with log.open('w') as out:
        child = subprocess.Popen(command, stdout=out, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=120)
        finally:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append', help='Partial development run only')
    args = parser.parse_args()
    category = ROOT / '.planning/debug/scenario-start-visitor-runtime'
    category.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=category))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'
    completion.write_text(__doc__ + '\nEvidence: ' + str(run) + '\n')
    code = 1
    started = time.monotonic()
    results = []
    try:
        fixture = ROOT / 'scripts/scenario-start-visitor-runtime'
        base = ROOT / 'src/GloomhavenVR'
        paths = [base / name for name in [
            'Net/Remote/MapStoryLifecycle.cs', 'Net/Remote/MapStoryLifecycle.ScenarioStart.cs',
            'Net/MapStoryOpeningLedger.cs', 'Net/MapStoryLifecycleState.cs', 'Net/SharedMapRunIdentity.cs',
            'WorldUI/Options/NativeCheatQueue.cs']]
        paths += list(fixture.glob('*.cs'))
        common = ROOT / 'scripts/multiplayer-cheats-runtime'
        paths += [common / name for name in ['NativeMethods.cs', 'NativeActionType.cs', 'NativePhase.cs', 'NativeProcessorStateType.cs']]
        sources = {p.name: p.read_text() for p in paths}
        protocol = base / 'Net/NetProtocol.cs'
        build = re.search(r'public const ushort ModBuild = (\d+);', protocol.read_text()).group(1)
        sources['Boundary.cs'] = sources['Boundary.cs'].replace('internal const ushort ModBuild=763;',
                                                                'internal const ushort ModBuild=' + build + ';')
        native = ROOT / 'decompiled/GH.Runtime'
        original_story = (native / 'StoryController.cs').read_text()
        original_choreo = (native / 'Choreographer.cs').read_text()
        original_trigger = (ROOT / 'decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary.CustomLevels/CLevelTrigger.cs').read_text()
        original_remote = (base / 'Net/Remote/RemoteStorySync.cs').read_text()
        authority = base / 'Net/SharedWindowSpawnAuthority.cs'
        authority_source = authority.read_text()
        initial_start = authority_source.index('    internal static bool IsInitialAuthor()')
        initial_end = authority_source.index(';', initial_start) + 1
        sources['OriginalAuthority.cs'] = 'using System;using System.Collections.Generic;using FFSNet;namespace GloomhavenVR.Net {internal static class SharedWindowSpawnAuthority {private static readonly List<int> Peers=new();private static Func<bool> _transportOnline=()=>true;\n' + method(authority_source, 'internal static int AuthorId') + '\n' + authority_source[initial_start:initial_end] + '\n}}'
        bridge = base / 'WorldUI/Modal/SharedWindowSpawnBridge.cs'
        sources[bridge.name] = bridge.read_text()
        sources['OriginalStory.cs'] = '#nullable disable\nusing System;using FFSNet;using ScenarioRuleLibrary;internal sealed partial class StoryController {\n' + '\n'.join(method(original_story, s) for s in [
            'private void ShowImmediately(', 'private void OnFinishShow()', 'private void ShowNext()',
            'private void TryBlockedUpdate()', 'private void TryUnblockedUpdate()']) + '\n}'
        sources['OriginalWin.cs'] = '#nullable disable\nusing System;using System.Collections;using UnityEngine.Events;using ScenarioRuleLibrary;internal sealed partial class Choreographer {\n' + '\n'.join(method(original_choreo, s) for s in [
            'public void ClientDebugWinScenario()', 'public void WinScenario(bool forceUI', 'private IEnumerator WinScenarioCoroutine(']) + '\n}'
        sources['OriginalTrigger.cs'] = '#nullable disable\ninternal sealed partial class CLevelTrigger {\n' + method(original_trigger, 'public CLevelTrigger(ELevelMessagePredefinedDisplayTrigger') + '\n}'
        sources['OriginalContentIdentity.cs'] = 'using System.Collections.Generic; namespace GloomhavenVR.Net {internal static partial class RemoteStorySync {\n' + '\n'.join(method(original_remote, s) for s in [
            'internal static uint HashDialog(', 'private static uint HashString(']) + '\n}}'
        paths += [native / 'StoryController.cs', native / 'Choreographer.cs',
                  ROOT / 'decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary.CustomLevels/CLevelTrigger.cs',
                  base / 'Net/Remote/RemoteStorySync.cs', authority, bridge, protocol, Path(__file__)]
        original_pins = json.loads((common / 'native-excerpt-manifest.json').read_text())
        for name, expected in original_pins['fixture_sha256'].items():
            assert hashlib.sha256((common / name).read_bytes()).hexdigest() == expected
        assert hashlib.sha256((ROOT / 'ressources/GH_Data/Managed/GH.Runtime.dll').read_bytes()).hexdigest() == original_pins['native_binaries']['GH.Runtime.dll']
        paths.append(common / 'native-excerpt-manifest.json')
        for name in ['GH.Runtime.dll', 'ScenarioRuleLibrary.dll']:
            paths.append(ROOT / 'ressources/GH_Data/Managed' / name)
        (run / 'source-pins.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}, indent=2) + '\n')
        (run / 'source-content-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name, text in sources.items()}, indent=2) + '\n')
        cases = [
            ('production', None, None, None, None),
            ('no-offline-start', 'MapStoryLifecycle.cs', '        RememberScenarioStart(message, semantic == 0 ? 1u : semantic, content, (byte)count);', '', 'offline successful native start enrolls only its matching live visitor'),
            ('no-late-visitor', 'MapStoryLifecycle.cs', '        EnrollScenarioStartVisitor(sender, presence.MapStoryLifecycleEntries);', '', 'offline successful native start enrolls only its matching live visitor'),
            ('no-native-host', 'MapStoryLifecycle.ScenarioStart.cs', ' || !FFSNetwork.IsHost', '', 'late start refuses non-host'),
            ('no-native-receipt', 'MapStoryLifecycle.ScenarioStart.cs', '|| !NativeCheatQueue.HasCurrentVersion(sender)', '|| false', 'late start refuses missing current native version receipt'),
            ('no-episode-identity', 'MapStoryLifecycle.ScenarioStart.cs', '!ReferenceEquals(_scenarioStartEpisode?.Target, episode)', 'false', 'late start refuses changed native scenario episode'),
            ('ordinary-message-grant', 'MapStoryLifecycle.ScenarioStart.cs',
             'if (trigger == null || trigger.IsUIEventTypeTrigger || trigger.IsTriggeredByDismiss\n            || trigger.EventTriggerTypeInt != 2 || trigger.EventTriggerSubTypeInt != 1\n            || trigger.EventTriggerContextTypeInt != 1 || trigger.EventTriggerRound != 1) return;',
             'if (trigger == null) return;', 'ordinary/end/room story never acquires retroactive start permission'),
            ('no-original-completion', 'MapStoryLifecycle.ScenarioStart.cs', '|| !start.Finished', '|| false', 'unfinished native start is never a completed receipt'),
            ('no-reciprocal-request', 'MapStoryOpeningLedger.cs', '|| !MapStoryLifecycleCodec.Valid(request) || !Has(request.Participants, localId)', '|| !MapStoryLifecycleCodec.Valid(request)', 'late start refuses nonreciprocal request'),
            ('unconditional-native-reads', 'MapStoryLifecycle.ScenarioStart.cs', 'if (!CurrentScenarioStarts() || requests.Length == 0) return;', '_ = NativeCheatQueue.HasCurrentVersion(sender);\n        if (!CurrentScenarioStarts() || requests.Length == 0) return;', 'ordinary83 traffic performs no native roster or player-reflection reads'),
            ('weak-completed-proof', 'MapStoryLifecycle.ScenarioStart.cs', 'if (start.LedgerToken == 0)', 'if (start.Subject.Target is not StoryController.DialogInfo) return;\n        if (start.LedgerToken == 0)', 'bounded completed start proof survives native subject GC'),
            ('no-flat-host-surrogate', 'OriginalAuthority.cs', 'int author = local;', 'int author = 0;', 'unmodded flat host retains one usable native VR intro'),
            ('no-session-retirement', 'MapStoryLifecycle.cs', 'if (_sessionEpoch == epoch) return;', 'if (_sessionEpoch == epoch || _sessionEpoch != epoch) return;', 'new native session drops ordinary story ledger and move bindings'),
            ('no-proof-token-retirement', 'MapStoryLifecycle.ScenarioStart.cs', '            pair.Value.LedgerToken = 0;', '', 'new session retains only current native episode completed proof'),
            ('drop-pending-start-provenance', 'MapStoryLifecycle.ScenarioStart.cs', '!pair.Value.Finished && !pair.Value.Subject.IsAlive', '!pair.Value.Finished', 'query-before-finish retains exact native start success'),
            ('wrong-page-direction', 'MapStoryLifecycle.ScenarioStart.cs', 'request.Finished || request.Bidirectional || ', 'request.Finished || ', 'refused late start creates no ordinary ledger occurrence: bidirectional request'),
            ('stale-local-session-id', 'MapStoryLifecycle.cs', '_ledger = new MapStoryOpeningLedger();\n        _localPlayerId = 0;\n        Peers.Clear();', '_ledger = new MapStoryOpeningLedger();\n        Peers.Clear();', 'new session drops prior native local player id before current packet admission'),
        ]
        if args.case:
            assert set(args.case) <= {c[0] for c in cases}
            cases = [c for c in cases if c[0] in args.case]
        for name, file, before, after, expected in cases:
            case = run / name
            case.mkdir()
            for filename, source in sources.items():
                if filename == file:
                    assert source.count(before) == 1, (name, source.count(before))
                    source = source.replace(before, after, 1)
                (case / filename).write_text(source)
            project = case / 'Visitor.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup></Project>')
            log = case / 'run.log'
            status = run_command([str(Path.home() / '.dotnet/dotnet'), 'run', '--project', str(project), '-c', 'Release'], log)
            output = log.read_text()
            binary = case / 'bin/Release/net8.0/Visitor.dll'
            assert binary.is_file(), 'The expected runtime control must compile: ' + output
            shutil.copy2(binary, case / 'executed.dll')
            binary_hash = hashlib.sha256(binary.read_bytes()).hexdigest()
            if name == 'production':
                assert status == 0 and 'assertions passed.' in output, output
            else:
                assert status != 0 and expected in output and 'error CS' not in output, output
            results.append({'name': name, 'exit_code': status, 'negative_control': name != 'production',
                            'expected': expected, 'executed_sha256': binary_hash,
                            'log_sha256': hashlib.sha256(log.read_bytes()).hexdigest()})
            print('PASS ' + name, flush=True)
        code = 0
    finally:
        (run / 'result.json').write_text(json.dumps(dict(exit_code=code, partial=bool(args.case), cases=results,
            seconds=time.monotonic()-started, limits=__doc__), indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('scenario-start-visitor-runtime', code,
            completion, ROOT, process_reaped=True), indent=2) + '\n')
    return code


if __name__ == '__main__':
    raise SystemExit(main())
