#!/usr/bin/env python3
"""Execute Results host requests, actual receive prefix and module mailbox lifecycle in portable CLR.

Production request/sender/current-native-receipt sources run unchanged. The whole actual
FfsNetTransport.ReceivePrefix and NetModule.Init/Shutdown bodies run as exact projections.
Native entities/roster storage, native Results pointer permission/latch, socket/install,
unrelated existing request handlers, gameobjects and logger are declared fixture ports.
The Results lane separately executes the original pointer callback and ready lifecycle.
No Photon, Unity/head pixels, serializer/golden or HMD acceptance is claimed here.
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


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def method(source, signature):
    start = source.index(signature)
    at = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[at] == '{') - (source[at] == '}')
        at += 1
    return source[start:at]


def command(args, log):
    with log.open('w') as out:
        child = subprocess.Popen(args, stdout=out, stderr=subprocess.STDOUT, cwd=ROOT, start_new_session=True)
        try:
            return child.wait(timeout=90)
        finally:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--production-only', action='store_true', help='Partial development invocation')
    args = parser.parse_args()
    category = ROOT / '.planning/debug/scenario-results-control-runtime'
    category.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=category))
    print('Evidence: ' + str(run), flush=True)
    completion = run / 'completion.log'
    completion.write_text(__doc__ + '\nEvidence: ' + str(run) + '\n')
    started = time.monotonic()
    code = 1
    results = []
    try:
        base = ROOT / 'src/GloomhavenVR'
        fixture = ROOT / 'scripts/scenario-results-control-runtime'
        paths = [base / path for path in [
            'Net/ScenarioResultsControlRequests.cs', 'Net/SideActionRequest.cs',
            'WorldUI/Options/NativeCheatQueue.cs']]
        paths += sorted(fixture.glob('*.cs'))
        sources = {path.name: path.read_text() for path in paths}
        transport = base / 'Net/FfsNetTransport.cs'
        module = base / 'Net/NetModule.cs'
        protocol = base / 'Net/NetProtocol.cs'
        transport_text, module_text, protocol_text = [p.read_text() for p in [transport, module, protocol]]
        receive = method(transport_text, '    private static bool ReceivePrefix(')
        installed = re.search(r'^    internal bool ReceiveHookInstalled =>[^;]+;', transport_text, re.M).group(0)
        sources['ActualReceivePrefix.cs'] = 'using System;using GloomhavenVR.Core;namespace GloomhavenVR.Net { internal sealed partial class FfsNetTransport {\n' + installed + '\n' + receive + '\n}}'
        init = method(module_text, '    public void Init()')
        shutdown = method(module_text, '    public void Shutdown()')
        mailbox = re.search(r'^    private readonly System.Func<byte, uint, bool> _resultsContinuation[^;]+;', module_text, re.M).group(0)
        sources['ActualModuleLifecycle.cs'] = 'using UnityEngine;using GloomhavenVR.Core;using Object=UnityEngine.Object;namespace GloomhavenVR.Net { internal sealed partial class NetModule {\n' + mailbox + '\n' + init + '\n' + shutdown + '\n}}'
        constants = ['ModBuild', 'SentinelActionTypeId', 'SentinelTargetPlayerId',
                     'SideRequestResultsContinue', 'SideRequestResultsReturn', 'SideRequestResultsRetry']
        values = '\n'.join(re.search(r'^    (?:public|internal) const [^;\n]+\b' + name + r'\s*=[^;]+;', protocol_text, re.M).group(0) for name in constants)
        sources['ActualConstants.cs'] = 'namespace GloomhavenVR.Net { internal static class NetProtocol {\n' + values + '\n}}'
        paths += [transport, module, protocol, Path(__file__).resolve()]
        pins = {str(p.relative_to(ROOT)): sha(p) for p in paths}
        (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
        originals = run / 'originals'
        originals.mkdir()
        for p in [transport, module, protocol]:
            shutil.copy2(p, originals / p.name)
        (run / 'projection-bindings.json').write_text(json.dumps({
            'source_pins': pins, 'limits': __doc__, 'exact_projection_hashes': {
                name: hashlib.sha256(text.encode()).hexdigest() for name, text in {
                    'ReceivePrefix': receive, 'ReceiveHookInstalled': installed, 'Init': init,
                    'Shutdown': shutdown, 'mailbox': mailbox, 'constants': values}.items()}}, indent=2) + '\n')
        controls = [
            ('dispatch', 'ActualReceivePrefix.cs', 'if (ScenarioResultsControlRequests.TryHandleSideAction(action))', 'if (false)', 'current host reaches original continuation boundary once'),
            ('exact-opening', 'ScenarioResultsControlRequests.cs', 'opening == ScenarioResultsPose.Key', 'true', 'request refuses stale opening'),
            ('common-author', 'ScenarioResultsControlRequests.cs', 'ScenarioResultsPose.Author == PlayerRegistry.HostPlayerID', 'true', 'request refuses wrong common author'),
            ('native-visible', 'ScenarioResultsControlRequests.cs', '&& ScenarioResultsPose.NativeVisible;', ';', 'request refuses closed native opening'),
            ('current-native-receipt', 'ScenarioResultsControlRequests.cs', 'NativeCheatQueue.HasCurrentVersion(request.PlayerID)', 'true', 'request refuses missing current receipt'),
            ('actual-host', 'ScenarioResultsControlRequests.cs', 'FFSNetwork.IsHost && !FFSNetwork.IsClient', 'true', 'request refuses native nonhost'),
            ('flat-mode', 'ScenarioResultsControlRequests.cs', '!NetSession.FlatNetMode && ', '', 'request refuses flat mode'),
            ('native-local-host', 'ScenarioResultsControlRequests.cs', 'local == PlayerRegistry.HostPlayerID', 'true', 'request refuses wrong native local host'),
            ('complete-marker', 'ScenarioResultsControlRequests.cs', '            || request.TargetPlayerID != NetProtocol.SentinelTargetPlayerId\n', '', 'marker preserves wrong target'),
            ('lossless-key', 'ScenarioResultsControlRequests.cs', 'unchecked((int)opening)', 'unchecked((int)(opening & 0x7FFFFFFF))', 'entire unsigned opening survives native int field'),
            ('unmodded-fallback', 'ScenarioResultsControlRequests.cs', '!NativeCheatQueue.HasCurrentVersion(PlayerRegistry.HostPlayerID)', 'false', 'send refuses unmodded host'),
            ('installed-mailbox', 'ActualModuleLifecycle.cs', 'if (nativeTransport.ReceiveHookInstalled)', 'if (true)', 'failed receive installation does not bind mailbox'),
            ('owned-mailbox-clear', 'ActualModuleLifecycle.cs', 'if (ReferenceEquals(WorldUI.ScenarioResultsNative.SubmitContinuation, _resultsContinuation))', 'if (true)', 'shutdown preserves a replaced mailbox'),
            ('native-entity-reuse', 'NativeCheatQueue.cs', '&& ReferenceEquals(seen, CurrentPlayer(sender));', '&& CurrentPlayer(sender) != null;', 'request refuses reused native ID'),
        ]
        cases = [('production', None, None, None, None)] + ([] if args.production_only else controls)
        for name, filename, old, new, expected in cases:
            case = run / name
            case.mkdir()
            for filename_source, source in sources.items():
                if filename_source == filename:
                    assert source.count(old) == 1, (name, source.count(old))
                    source = source.replace(old, new, 1)
                (case / filename_source).write_text(source)
            project = case / 'ResultsControl.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation><NoWarn>$(NoWarn);CS0649;CS0414;CS0162</NoWarn></PropertyGroup></Project>')
            log = case / 'run.log'
            status = command([str(Path.home() / '.dotnet/dotnet'), 'run', '--disable-build-servers', '--project', str(project), '-c', 'Release'], log)
            output = log.read_text()
            binary = case / 'bin/Release/net8.0/ResultsControl.dll'
            assert binary.is_file(), 'Expected executable must compile: ' + output
            shutil.copy2(binary, case / 'executed.dll')
            assertion_count = re.search(r'(\d+) assertions passed\.', output)
            passed = status == 0 and assertion_count if name == 'production' else status != 0 and expected in output and 'error CS' not in output
            results.append({'name': name, 'passed': bool(passed), 'exit_code': status,
                            'assertions': int(assertion_count.group(1)) if assertion_count else None,
                            'expected': expected, 'executed_sha256': sha(binary), 'log_sha256': sha(log)})
            print(('PASS ' if passed else 'FAIL ') + name, flush=True)
            assert passed, output
        assert all(sha(ROOT / p) == h for p, h in pins.items()), 'Inputs changed during the owned run'
        code = 0
    finally:
        (run / 'result.json').write_text(json.dumps({'exit_code': code, 'partial': args.production_only,
            'cases': results, 'seconds': time.monotonic() - started, 'limits': __doc__}, indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('scenario-results-control-runtime', code,
            completion, ROOT, process_reaped=True), indent=2) + '\n')
    return code


if __name__ == '__main__':
    raise SystemExit(main())
