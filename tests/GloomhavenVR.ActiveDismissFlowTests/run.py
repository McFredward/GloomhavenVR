#!/usr/bin/env python3
"""Run the actual physical dismissal partial and shared berth ownership methods."""
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


def execute(command, log):
    with log.open('w') as stream:
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=90)
        finally:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()


def main():
    parent = ROOT / '.planning/debug/active-dismiss-flow'
    parent.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=parent))
    log = run / 'completion.log'
    log.write_text('evidence: ' + str(run) + '\n')
    print('evidence: ' + str(run), flush=True)
    result = 1
    try:
        project = run / 'project'
        project.mkdir()
        for source in Path(__file__).resolve().parent.glob('*'):
            if source.is_file():
                shutil.copyfile(source, project / source.name)
        original = ROOT / 'src/GloomhavenVR/Cards/Driver/CardsDriver.7.ActiveDismiss.cs'
        flow = original.read_text()
        tray = (ROOT / 'src/GloomhavenVR/Cards/Tray/PlayTray.4.Slots.cs').read_text()
        gates = 'namespace GloomhavenVR.Cards; internal sealed partial class PlayTray {\n' + '\n'.join(
            method(tray, signature) for signature in (
                'internal void SetActiveDismissPresentation(', 'internal void SetItemUseAreaLabel(',
                'internal void SetItemUseConfirmVisible(', 'internal void SetItemUseSlotVisible(')) + '\n}\n'
        bind_start = tray.index('internal static System.Action BindItemUseConfirmation(')
        bind_end = tray.index(';', bind_start) + 1
        gates = gates.rsplit('}', 1)[0] + tray[bind_start:bind_end] + '\n}\n'
        (project / 'Tray.cs').write_text(gates)
        path = project / 'Flow.cs'
        sources = run / 'Sources'
        sources.mkdir()
        (sources / 'Flow.cs').write_text(flow)
        (sources / 'Tray.cs').write_text(tray)
        bindings = {
            'release-before-read-return': 'if (TryStageActiveDismissCard(card, hand))',
            'grab-cancels-reservation': 'TakeBackActiveDismissCard(card);',
        }
        interactions = (ROOT / 'src/GloomhavenVR/Cards/Driver/CardsDriver.5.Interactions.cs').read_text()
        assert all(token in interactions for token in bindings.values())
        assert interactions.index(bindings['release-before-read-return']) < interactions.index('_active.Add(card);')
        update = (ROOT / 'src/GloomhavenVR/Cards/Driver/CardsDriver.2.Update.cs').read_text()
        assert 'TickActiveDismiss(_fakeActive ? null : presented);' in update
        assert 'driver.ResetActiveDismiss();' in method(update, 'internal static void ReleaseCardsBeforeSceneLoad(')
        rebuild = (ROOT / 'src/GloomhavenVR/Cards/Driver/CardsDriver.4.Rebuild.cs').read_text()
        departures = ('using GloomhavenVR.Core;\nusing ScenarioRuleLibrary;\nusing UnityEngine;\n'
                      'namespace GloomhavenVR.Cards; internal sealed partial class CardsDriver {\n')
        for signature in ('private enum RoundCardExit', 'private bool TryStartFlyToPile(',
                          'private static RoundCardExit RoundCardExitOf(',
                          'private Net.CardFlightSource CapturedActiveSource(',
                          'private Net.CardFlightSource? FlightSourceOf(',
                          'private static void ReportCardFx('):
            if signature.startswith('private Net.CardFlightSource Captured'):
                start = rebuild.index(signature)
                end = rebuild.index(';', start) + 1
                departures += rebuild[start:end] + '\n'
            else:
                departures += method(rebuild, signature) + '\n'
        departures += '\n}\n'
        zone_start = rebuild.index('        // Configure cards per zone; everything else parks invisibly.')
        zone_end = rebuild.index('            // Same rule, one more owner', zone_start)
        zone = rebuild[zone_start:zone_end]
        departures = departures.rsplit('}', 1)[0] + '\ninternal void NativeHandoffZoneSweep() {\n' + zone + '\nif (_active.Contains(card)) continue;\n_factory.Park(card);\n}\n}\n}\n'
        (project / 'Departures.cs').write_text(departures)
        (sources / 'Rebuild.cs').write_text(rebuild)
        assert rebuild.index('AdvanceCommittedActiveDismiss(hand);') < rebuild.index('UpdateActive(hand);')
        board_rebuild = method(rebuild, 'private void RebuildBoard(')
        assert board_rebuild.index('ResetActiveDismiss();') < board_rebuild.index('_factory.Park(card);')
        build = (ROOT / 'src/GloomhavenVR/Cards/Tray/PlayTray.6.Build.cs').read_text()
        assert 'BindItemUseConfirmation(_itemUseConfirmAction)' in build
        pose = (ROOT / 'src/GloomhavenVR/Cards/Tray/PlayTray.3.Pose.cs').read_text()
        assert '_itemUseConfirm.SetClickAction(BindItemUseConfirmation(_itemUseConfirmAction));' in pose
        active = (ROOT / 'src/GloomhavenVR/Cards/Piles/ActivePileViewer.cs').read_text()
        layout = method(active, 'private void Relayout(')
        assert layout.index('ReferenceEquals(card, StagedCard)') < layout.index('card.SetHome(')
        hashes = {str(original.relative_to(ROOT)): hashlib.sha256(original.read_bytes()).hexdigest(),
                  'tray-gates': hashlib.sha256(gates.encode()).hexdigest()}
        (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        cases = []
        for name, old, new, expected in [
            ('production', '', '', ''),
            ('release-authority', '!(hand.TriggerUp || hand.GripUp)', 'false', 'Synthetic release cannot stage dismissal'),
            ('stage-without-consent', 'hand.SendHaptic(HapticPreset.HoverTick);', 'decision.Confirm(); hand.SendHaptic(HapticPreset.HoverTick);', 'Placement must leave the original effect active'),
            ('lost-cancel', 'TakeBackActiveDismissCard(VRCard card)\n    {', 'TakeBackActiveDismissCard(VRCard card)\n    { return;\n#pragma warning disable CS0162', 'Taking back cancels without ending the effect'),
            ('unscaled-radius', 'ActiveDismissDropRadius * Mathf.Abs(berth.lossyScale.x)', 'ActiveDismissDropRadius', 'Drop capture must scale with the board'),
            ('missing-item-reservation', '|| presented == null || ItemOwnsDismissBerth()', '|| presented == null', 'A pending item owns the shared berth'),
            ('stale-cap-session', 'if (!ReferenceEquals(decision, expected))', 'if (bool.Parse("false"))', 'Old actual cap callback must not confirm a replacement card'),
            ('hidden-berth-flight', 'private void PreserveActiveDismissFlightPose(VRCard card)\n    {',
             'private void PreserveActiveDismissFlightPose(VRCard card)\n    { return;\n#pragma warning disable CS0162',
             'Confirmed discard flies the same staged original from its actual mid-glide pose'),
            ('native-pile-handoff', 'if (exit is not (RoundCardExit.Discarded or RoundCardExit.Lost or RoundCardExit.PermanentlyLost))',
             'if (bool.Parse("false"))', 'Accepted card stays in its berth without a second confirm until native model retirement'),
            ('native-zone-handoff', 'if (_dismissCommitted && ReferenceEquals(card, _dismissCard))',
             'if (bool.Parse("false"))', 'Actual rebuild zone sweep keeps the accepted original visible across a native list handoff gap'),
            ('original-counted-source', 'new Net.CardFlightSource(_dismissActorId, _dismissSeat, _dismissCount)',
             'new Net.CardFlightSource(_dismissActorId, 0, 0)', 'Flight retains complete counted active seat and immutable original pool provenance before removal'),
            ('actual-launch-origin', 'Position = berth.InverseTransformPoint(position)',
             'Position = Vector3.zero', 'Remote origin records the actual launch pose relative to the berth'),
            ('native-burn-order', '&& !TryTakeBurnFlightSlot(card.GameCard, card)',
             '&& bool.Parse("false")', 'Native burn artwork holds the same detached original after berth and active-grid removal'),
        ]:
            assert not old or old in flow or old in departures, name
            path.write_text(flow.replace(old, new) if old and old in flow else flow)
            (project / 'Departures.cs').write_text(departures.replace(old, new) if old and old in departures else departures)
            code = execute([dotnet, 'run', '--project', str(project / 'GloomhavenVR.ActiveDismissFlowTests.csproj'),
                            '-c', 'Release'], run / (name + '.log'))
            text = (run / (name + '.log')).read_text()
            if name == 'production':
                if code:
                    raise RuntimeError(text)
                print(text.rstrip(), flush=True)
            elif code == 0 or expected not in text or 'error CS' in text:
                raise RuntimeError(name + ' failed without its expected causal assertion:\n' + text)
            else:
                print('Active dismissal flow negative control: ' + name + ' failed as expected.', flush=True)
            cases.append({'case': name, 'exit_code': code, 'expected': expected})
        path.write_text(flow)
        (project / 'Departures.cs').write_text(departures)
        (run / 'results.json').write_text(json.dumps({'cases': cases,
            'scope': 'Actual physical-flow partial, native-membership classifier, pile launcher, counted source/metadata handoff and PlayTray ownership methods. Explicit Unity interpolation, native callback adapter and active-grid renderer boundaries; no rendered pixels/headset/network acceptance.'}, indent=2) + '\n')
        result = 0
        return 0
    finally:
        report = cleanup_completed_run('active-dismiss-flow', result, log, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())
