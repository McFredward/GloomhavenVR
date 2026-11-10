#!/usr/bin/env python3
"""Source-bound public forced-hand-pick producer/consumer/flight test.

Executes exact sampler, pool provenance, codec/inbox, remote resolution and existing
flight-cache methods. Native actor/widget/phase, ownership and renderer admission are
explicit ports; this is not a native callback, Photon or headset picture qualification.
The separate wire suite owns full presence/legacy39 goldens. No gameplay callbacks run.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def member(text, signature):
    at = text.index(signature)
    opened = text.index('{', at)
    depth = 0
    for i in range(opened, len(text)):
        if text[i] == '{': depth += 1
        if text[i] == '}':
            depth -= 1
            if depth == 0: return text[at:i + 1]
    raise ValueError('Unclosed source member: ' + signature)


def expression(text, signature):
    at = text.index(signature)
    return text[at:text.index(';', at) + 1]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-root', type=Path, default=ROOT)
    p.add_argument('--output-dir', type=Path)
    p.add_argument('--case', action='append')
    args = p.parse_args(); root = args.source_root.resolve()
    output = (args.output_dir or root / '.planning/debug/public-hand-pick-runtime').resolve()
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n'); print('evidence: ' + str(run), flush=True)
    source = {}
    paths = [
        'src/GloomhavenVR/Net/PublicHandPickState.cs', 'src/GloomhavenVR/Net/PublicHandPickSampler.cs',
        'src/GloomhavenVR/Net/CardAppearanceProvenance.cs', 'src/GloomhavenVR/Net/CardAppearanceBinding.cs',
        'src/GloomhavenVR/Net/CardPresentationPolicy.cs', 'src/GloomhavenVR/Net/RevealGate.cs',
        'src/GloomhavenVR/Net/Remote/RemoteControlBoard.cs', 'src/GloomhavenVR/Net/Avatar/AvatarSerializer.cs',
        'decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/CPhase.cs', 'decompiled/GH.Runtime/CardPileType.cs',
        'scripts/public-hand-pick-runtime/Program.cs', 'scripts/check-public-hand-pick-runtime.py']
    sources = run / 'sources'; sources.mkdir()
    pins = {}
    for name in paths:
        path = root / name; target = sources / name; target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target); source[Path(name).name] = path.read_text(); pins[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    generated = {name: source[name] for name in ['PublicHandPickState.cs', 'PublicHandPickSampler.cs',
        'CardAppearanceProvenance.cs', 'CardAppearanceBinding.cs', 'CardPresentationPolicy.cs', 'Program.cs']}
    generated['NativePhaseEnum.cs'] = 'namespace ScenarioRuleLibrary; internal static class CPhase {\n' + member(source['CPhase.cs'], 'public enum PhaseType') + '\n}\n'
    generated['NativePileEnum.cs'] = member(source['CardPileType.cs'], 'public enum CardPileType') + '\n'
    board = source['RemoteControlBoard.cs']
    assert board.count('int wire = exhausted ? 0 : PublicPickOccupancy(actor,') == 1, 'Production occupancy helper is not adopted before the original slot branches'
    assert board.count('PreservePublicPickDeparture(i);') == 1, 'Production hand-to-pile departure handover is not adopted'
    generated['RemoteMethods.cs'] = 'using ScenarioRuleLibrary;\nnamespace GloomhavenVR.Net;\ninternal sealed partial class RemoteBoard {\n' + '\n'.join(member(board, key) for key in [
        'private bool TryResolvePublicHandPick(', 'internal enum DepartedFaceVerdict',
        'internal bool TryTakeDepartedFace(', 'private bool InDestinationPile(', 'private void NoteRecessDeparture(']) + '\n}\n'
    start = board.index('    private static bool PublicPickContextMatches(')
    end = board.index('    private bool TryResolveSacrifice(', start)
    generated['RemoteMethods.cs'] = generated['RemoteMethods.cs'][:-2] + board[start:end] + '\n}\n'
    gate = source['RevealGate.cs']
    generated['RevealMethods.cs'] = 'using ScenarioRuleLibrary;\nnamespace GloomhavenVR.Net;\ninternal static partial class RevealGate {\n' + '\n'.join(expression(gate, key) for key in [
        'public static bool IsSecretSelectionPhase', 'public static bool PeersSeeOurCardFronts',
        'public static bool PeersMayNameOurCard(', 'public static bool ShowRoundCardFronts(']) + '\n}\n'
    serializer = source['AvatarSerializer.cs']
    generated['CodecMethods.cs'] = 'namespace GloomhavenVR.Net;\ninternal static class AvatarSerializer {\n' + '\n'.join(member(serializer, key) for key in ['internal static void WriteU32(', 'internal static uint ReadU32(']) + '\n}\n'
    variants = [
        ('production', '', '', '', ''),
        ('original-anonymous-pick', 'PublicHandPickSampler.cs', 'named |= (byte)(1 << slot);', '/* original hand-source omission */', 'forced native action discard publishes BOTH exact hand originals'),
        ('private-provenance-leak', 'PublicHandPickSampler.cs', 'if (phase == 0 || RevealGate.IsSecretSelectionPhase\n                    || !RevealGate.PeersMayNameOurCard(actor, widget.CardInstanceID)\n                    || !RevealGate.PeersSeeOurCardFronts) continue;', 'if (phase == 0) continue;', 'private selection never emits source provenance'),
        ('foreign-actor-seats', 'RemoteMethods.cs', '&& actor != null && state.ActorId == NetFigures.StableActorId(actor)', '&& actor != null', 'foreign displayed actor cannot reuse owner seats'),
        ('stale-round-seats', 'RemoteMethods.cs', '&& state.Round == (ScenarioManager.CurrentScenarioState?.RoundNumber ?? -1)', '', 'changed round cannot reuse public seats'),
        ('wrong-native-namespace', 'RemoteMethods.cs', 'state.MatchesScenario(ScenarioManager.CurrentScenarioState?.Seed ?? 0,\n            PublicHandPickState.ScenarioFingerprint(ScenarioManager.CurrentScenarioState?.ID))', 'true', 'different native seed invalidates consumer'),
        ('overrides-legacy-pile', 'PublicHandPickSampler.cs', 'widget?.CardType != CardPileType.Hand', 'false', 'native hand-to-discard commit releases hand identity'),
        ('stale-legacy-occupancy', 'RemoteMethods.cs', 'PublicPickContextMatches(_owner.PublicHandPick, actor) ? _owner.PublicHandPick!.BoardSlots : original;', 'original;', 'ordered hand fill overrides an older empty legacy occupancy'),
        ('stale-public-context-flight', 'RemoteMethods.cs', 'ValidatePublicPickFaces();', '/* injected: old namespace retained */', 'private phase clears public departure before next board pass'),
        ('invalid-public-departed-face', 'RemoteMethods.cs', '_departedFace[i] = null; _departedPublicPick[i] = false;', '_departedPublicPick[i] = false;', 'unusable record immediately revokes only public live/departed faces'),
        ('original-no-public-flight-face', 'RemoteMethods.cs', '_latchedFaces[slot] ?? _publicPickFaces[slot]', '_latchedFaces[slot]', 'flight before empty edge takes approved public LEFT'),
    ]
    if args.case:
        assert set(args.case) <= {v[0] for v in variants}, 'Unknown case'
        variants = [v for v in variants if v[0] in args.case]
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    results = []; child = None; status = 1
    try:
        for name, filename, before, after, expected in variants:
            project = run / name; project.mkdir()
            for path, original in generated.items():
                code = original
                if path == filename:
                    count = code.count(before); assert count == (2 if name == 'original-no-public-flight-face' else 1), 'Mutation binding drift: ' + name
                    code = code.replace(before, after)
                (project / path).write_text(code)
            (project / 'Run.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup></Project>\n')
            with (project / 'build.log').open('w') as log:
                child = subprocess.Popen([dotnet, 'build', str(project / 'Run.csproj'), '-v', 'quiet', '--disable-build-servers', '-nr:false'], stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
                if child.wait(timeout=120): raise RuntimeError('Compilation is not an accepted causal-control failure: ' + name)
            dll = project / 'bin/Debug/net8.0/Run.dll'
            retained = run / 'assemblies' / (name + '.dll'); retained.parent.mkdir(exist_ok=True); shutil.copyfile(dll, retained)
            with (project / 'execution.log').open('w') as log:
                child = subprocess.Popen([dotnet, str(dll)], stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
                code = child.wait(timeout=30)
            text = (project / 'execution.log').read_text()
            passed = code == 0 and 'PASS ' in text if name == 'production' else code != 0 and ('FAIL ' + expected) in text
            results.append({'case': name, 'passed': passed, 'exit_code': code, 'expected_failure': expected, 'output': text.strip()})
            print(name + ': ' + text.strip(), flush=True)
            if not passed: raise RuntimeError('Case did not establish the required boundary: ' + name)
        status = 0
    finally:
        if child is not None:
            if child.poll() is None: os.killpg(child.pid, signal.SIGKILL)
            child.wait()
        (run / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
        stable = all(hashlib.sha256((root / name).read_bytes()).hexdigest() == digest for name, digest in pins.items())
        (run / 'source-hashes.json').write_text(json.dumps({'sha256': pins, 'unchanged': stable, 'scope': __doc__, 'selected_cases': args.case}, indent=2) + '\n')
        if not stable: status = 1
        cleanup = cleanup_completed_run('public-hand-pick-runtime', status, completion, root, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    return status


if __name__ == '__main__':
    sys.exit(main())
