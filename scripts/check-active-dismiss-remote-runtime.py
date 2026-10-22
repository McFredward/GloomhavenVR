#!/usr/bin/env python3
"""Execute production remote-dismiss commit, retained-original and flight-handover kernels."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def block(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def execute(command, log):
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=120)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/active-dismiss-remote-runtime')
    parser.add_argument('--case', action='append', help='Run only these production/control cases (partial evidence).')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir)).resolve()
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    status, results = 1, []
    inputs = [root / 'src/GloomhavenVR/Net/Remote' / name for name in
              ('RemoteBoardCard.cs', 'RemoteAvatar.cs', 'RemoteActiveCards.cs', 'RemoteBoardFurniture.cs',
               'RemoteActiveDepartures.cs', 'RemoteCardFx.cs', 'RemoteBurnFx.cs')]
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}
    try:
        card, receiver, active, furniture, departures, flight, burn = (path.read_text() for path in inputs)
        methods = '\n'.join(block(card, signature) for signature in (
            'internal void SetPresentationParent(', 'internal void FreezePresentationParent(', 'public void Move(', 'internal void TickGlide('))
        alias = 'using System; using GloomhavenVR.Core; using Vector3 = FixtureVector; using Quaternion = FixtureRotation; using Transform = FixtureTransform; using Mathf = FixtureMath; using Time = FixtureClock;\n'
        fields = '''private readonly FixtureObject _root = new(); private Vector3 _glideTo; private float _glideScale = 1f; private bool _gliding, _returningPose; private float _glideSpeed = Defaults.CardLerpSpeed; private const float GlideEpsilonSq = 1e-8f;
internal Transform PresentationTransform => _root.transform;
internal bool BlankCalled; internal void Blank() { BlankCalled = true; _root.transform.Active = false; }
internal void Set(FixtureCard? card, bool fronts, CPlayerActor actor) { _root.transform.Active = card != null; }
internal void MaintainMips() {} internal void SetNativeCardAppearance(int player, CPlayerActor actor, FixtureCard card) {}
internal RemoteBoardCard(Transform parent, Vector3 position) { _root.transform.parent = parent; _root.transform.localPosition = position; }
'''
        begin = receiver.index('        ActiveDismissBerthActive = p.HasActiveDismissBerth;')
        end = receiver.index('\n\n', begin)
        apply = 'internal void Apply(in PresenceState p) {\n' + receiver[begin:end] + '\n}\n'
        seat = re.search(r'        int dismissSeat = _owner.ActiveDismissMatchesActor[\s\S]*?;', active)[0]
        seat = seat.replace('_owner.', '').replace('Count', 'count').replace('ActiveDismissListcount', 'ActiveDismissListCount')
        seat = 'internal int ResolveDismissSeat(CPlayerActor? actor, int count, Transform? dismissBerth) {\n' + seat + '\nreturn dismissSeat; }\n'
        matches = re.search(r'internal bool ActiveDismissMatchesActor\(CPlayerActor\? actor\)[\s\S]*?;', receiver)[0]
        allows = re.search(r'internal bool AllowsActiveDismissPresentation\(CPlayerActor\? actor\)[\s\S]*?;', receiver)[0]
        allow = re.search(r'        bool allowUseBerth = [^;]+;', furniture)[0]
        shown = re.search(r'        bool shown = allowUseBerth[^;]+;', furniture)[0]
        armed = re.search(r'        bool armed = allowUseBerth[\s\S]*?;', furniture)[0]
        caption = re.search(r'        string\? use = allowUseBerth[^;]+;', furniture)[0]
        berth = re.search(r'SetItemUseShown\((allowUseBerth[\s\S]*?), _settled\);', furniture)[1]
        if 'SetCapLabels(owner, allowUseBerth);' not in furniture or 'ApplyActiveDismissGhost(owner, allowUseBerth);' not in furniture:
            raise AssertionError('Live ghost/caption application must consume the same actor gate')
        picture = 'internal static (bool Berth, bool Ghost, bool Armed, string? Caption) Picture(RemoteAvatar owner, CPlayerActor? displayedActor, bool synced, int buttons, bool retainItemClip = false) {\n' + allow + '\nbool berth = ' + berth + ';\n' + shown + '\n' + armed + '\n' + caption + '\nreturn (berth, shown, armed, use);\n}'
        protocol_path = root / 'src/GloomhavenVR/Net/NetProtocol.cs'
        inputs.append(protocol_path)
        hashes[str(protocol_path)] = hashlib.sha256(protocol_path.read_bytes()).hexdigest()
        protocol = protocol_path.read_text()
        bits = '\n'.join(re.search(r'public const byte ' + name + r' = [^;]+;', protocol)[0] for name in ('BoardUiItemRecessBit', 'BoardUiItemUseCapBit'))
        sources = {'Card.cs': alias + 'namespace GloomhavenVR.Net { internal sealed class RemoteBoardCard {\n' + fields + methods + '\n}}',
                   'Receiver.cs': alias + 'namespace GloomhavenVR.Net { internal sealed partial class RemoteAvatar {\n' + apply + matches + '\n' + allows + '\n' + seat + '\n}}',
                   'Furniture.cs': alias + 'namespace GloomhavenVR.Net { internal static class RemoteFurniture {\n' + picture + '\n} internal static class NetProtocol {\n' + bits + '\n}}'}
        flight_alias = alias + 'using CAbilityCard = FixtureCard; using CCharacterClass = FixtureCharacter;\n'
        sources['Departure.cs'] = flight_alias + 'namespace GloomhavenVR.Net { internal static class RemoteActiveDepartures {\n' + '\n'.join(
            block(departures, signature) for signature in ('internal static bool HasCommittedOriginal(',
                                                           'internal static bool TryDismissPose(')) + '\n}}'
        sources['Capture.cs'] = flight_alias + '''namespace GloomhavenVR.Net { internal sealed class RemoteActiveCards {
private readonly float _cardW = .0635f; private RemoteBoardCard? _dismissPanel; private CAbilityCard? _dismissCard;
private readonly RemoteAvatar _owner; private readonly System.Collections.Generic.List<CAbilityCard> _buffer = new();
private readonly Transform _root;
private readonly int _playerId = 22;
private readonly System.Collections.Generic.Dictionary<CAbilityCard, RemoteBoardCard> _retainedDismiss = new();
private readonly System.Collections.Generic.List<CAbilityCard> _retiredDismiss = new();
private int Count => _buffer.Count;
internal RemoteActiveCards(RemoteAvatar owner) { _owner = owner; _root = new Transform { parent = owner.StableBoard }; }
internal void Stage(RemoteBoardCard? panel, CAbilityCard? card) { _dismissPanel = panel; _dismissCard = card; }
internal void SetPopulation(params CAbilityCard[] cards) { _buffer.Clear(); _buffer.AddRange(cards); }
internal int PendingSeat(CPlayerActor actor, Transform? berth) => DismissSeat(actor, berth);
internal void Retain() => RetainDismissPanels();
internal bool Retains(CAbilityCard card) => _retainedDismiss.ContainsKey(card);
internal bool RefreshRetained(CAbilityCard card, RemoteBoardCard panel, CPlayerActor actor) => KeepRetainedDismissCard(card, panel, true, actor);
''' + '\n'.join(block(active, signature) for signature in ('internal bool TryDismissPose(',
                'private int DismissSeat(', 'private void RetainDismissPanels(', 'internal void FreezeDismissOriginal(',
                'private bool KeepRetainedDismissCard(')) + '\n}}'
        # net8 annotates Dictionary.TryGetValue's false result as nullable; the production
        # net472 reference does not. This changes only the extracted local type annotation.
        sources['Capture.cs'] = sources['Capture.cs'].replace('out RemoteBoardCard retained)', 'out RemoteBoardCard? retained)')
        pending = block(flight, 'private readonly struct PendingFlight').replace('ScenarioRuleLibrary.CAbilityCard', 'CAbilityCard')
        tick = block(flight, 'public void Tick(')
        tick = tick[:tick.index('        for (int i = 0; i < _flights.Count; i++)')] + '\n}'
        wait = re.search(r'    private static bool ArtworkWaitExpired\([^;]+;', flight)[0]
        keep = block(flight, 'private static bool KeepDismissFlight(').replace('ScenarioRuleLibrary.CAbilityCard', 'CAbilityCard')
        sources['Flight.cs'] = flight_alias + 'namespace GloomhavenVR.Net { internal sealed partial class RemoteCardFx {\n' + '\n'.join((block(flight, 'private void TransferVisibleFlight('), pending, tick, keep, wait)) + '\n}}'
        release = re.search(r'    private static bool MayReleaseOwnerBurn\([\s\S]*?;', burn)[0]
        sources['Burn.cs'] = flight_alias + 'namespace GloomhavenVR.Net { internal sealed partial class RemoteBurnFx {\n' + '\n'.join(
            block(burn, signature) for signature in ('internal void PrepareDismissRelease(',
                'private void PruneDismissOrigins(', 'private void CaptureFlightPose(',
                'private void Handover(')) + '\n' + release + '\n}}'
        # The orchestration assertion is deliberately source-pinned: the portable boundary below
        # executes handover/pose/native-commit decisions, not the full avatar or native burn watcher.
        if 'p.BurnCompletions, flight.DismissOrigin)' not in receiver or 'p.BurnCompletions, p.FxDismissOrigin)' not in receiver:
            raise AssertionError('Recent and legacy flights must retain their qualified dismiss origins')
        if 'card, entry.DismissOrigin)' not in receiver or 'originalCard, dismissOrigin)' not in receiver:
            raise AssertionError('Durable burn and mirrored-board dispatch must retain exact original origin')
        if 'CaptureFlightPose(b);' not in burn or 'b.Go.transform.SetPositionAndRotation(b.From, b.Rotation)' not in burn:
            raise AssertionError('Burn hold and handover must retain the berth orientation')
        mirrored = block(receiver, 'internal void PlayMirroredCardFlight(')
        if mirrored.index('FreezeDismissOriginal(originalCard)') > mirrored.index('_burnFx.PreparePresentation()'):
            raise AssertionError('Accepted original must detach before native watcher/furniture visibility can hide its berth')
        refresh = block(active, 'public void Refresh(')
        guard = refresh.index('if (KeepRetainedDismissCard(_buffer[i], _cards[i], showFronts, actor)) continue;')
        if guard > refresh.index('_cards[i].SetPresentationParent(staged ? dismissBerth! : _root)') or guard > refresh.index('_cards[i].Move(staged ? Vector3.zero : cellAt'):
            raise AssertionError('Retained original must bypass both parent and glide writes during delayed native active membership')
        mutations = [
            ('production', '', '', '', ''),
            ('parent-teleports', 'Card.cs', '_root.transform.SetParent(parent, worldPositionStays: true);\n        _returningPose = true;', '_root.transform.SetParent(parent, worldPositionStays: false);\n        _returningPose = true;', 'reparent preserves the complete current world pose'),
            ('reparent-does-not-settle', 'Card.cs', '_returningPose = true;', '_returningPose = false;', 'zero-distance reparent still settles its changed frame scale'),
            ('animated-wrong-scale', 'Card.cs', 'Vector3 targetScale = Vector3.one * _glideScale;', 'Vector3 targetScale = Vector3.one;', 'zero-distance reparent still settles its changed frame scale'),
            ('seed-wrong-scale', 'Card.cs', '_root.transform.localScale = Vector3.one * _glideScale;', '_root.transform.localScale = Vector3.one;', 'freshly seeded stage uses the same fit'),
            ('receiver-latches-absence', 'Receiver.cs', 'ActiveDismissBerthActive = p.HasActiveDismissBerth;', 'ActiveDismissBerthActive |= p.HasActiveDismissBerth;', 'absent state clears the previous remote reservation'),
            ('wrong-public-list', 'Receiver.cs', 'ActiveDismissListCount == count', 'true', 'active-list count mismatch cannot stage a different card'),
            ('wrong-public-actor', 'Receiver.cs', 'NetFigures.StableActorId(actor) == ActiveDismissActorId', 'true', 'same-count different actor cannot stage a card'),
            ('stage-ignores-actor', 'Receiver.cs', 'int dismissSeat = ActiveDismissMatchesActor(actor)', 'int dismissSeat = ActiveDismissBerthActive', 'same-count different actor cannot stage a card'),
            ('stale-dismissal-controls', 'Furniture.cs', 'bool allowUseBerth = owner.AllowsActiveDismissPresentation(displayedActor);', 'bool allowUseBerth = true;', 'wrong actor hides its dismissal berth, preview, cap and caption'),
            ('stale-dismissal-caption', 'Furniture.cs', 'allowUseBerth ? owner.ItemUseCapLabel : null', 'owner.ItemUseCapLabel', 'wrong actor hides its dismissal berth, preview, cap and caption'),
            ('stale-dismissal-preview', 'Furniture.cs', 'bool shown = allowUseBerth &&', 'bool shown =', 'wrong actor cannot show the held-card landing preview'),
            ('departure-ignores-active', 'Departure.cs', '!cc.ActivatedCards.Contains(card)', 'true', 'active membership must end before a committed flight'),
            ('departure-wrong-actor', 'Departure.cs', 'source.Value.ActorId != NetFigures.StableActorId(actor)', 'false', 'foreign actor cannot authorize a committed dismissal'),
            ('departure-wrong-pile', 'Departure.cs', 'cc.DiscardedAbilityCards.Contains(card)', 'true', 'native destination membership cannot name a different original'),
            ('fallback-drops-source-position', 'Departure.cs', 'berth.TransformPoint(origin.BerthPose.Position)', 'berth.position', 'missing stage retains exact owner intermediate position'),
            ('fallback-drops-source-width', 'Departure.cs', 'origin.Width * Mathf.Abs(berth.lossyScale.x)', '0.0635f * Mathf.Abs(berth.lossyScale.x)', 'missing stage retains exact owner intermediate width'),
            ('capture-wrong-card', 'Capture.cs', 'RemoteBoardCard? panel = _dismissPanel != null && ReferenceEquals(_dismissCard, card)\n            ? _dismissPanel : _retainedDismiss.TryGetValue(card, out RemoteBoardCard? retained) ? retained : null;\n        if (panel == null) return false;', 'RemoteBoardCard? panel = _dismissPanel;\n        if (panel == null) return false;', 'staged capture cannot borrow another original card'),
            ('handover-recaptures', 'Flight.cs', '!f.SourceTransferred && f.FromDismissBerth', 'f.FromDismissBerth', 'handover never recaptures after the original renderer was transferred'),
            ('handover-skips-source-transfer', 'Flight.cs', '_owner.TransferCardToFlight(', '_owner.ObserveWithoutTransfer(', 'first drawable flight transfers the exact original once'),
            ('pending-stage-returns-grid', 'Capture.cs', '_owner.DismissFlightOwnsCard(_dismissCard.CardInstanceID)', 'false', 'accepted pending flight retains its original staged card after118 clears'),
            ('pending-stage-recycled', 'Capture.cs', '_retainedDismiss[card] = panel;', '{}', 'native retirement retains the actual staged renderer while flight artwork loads'),
            ('pending-stage-leaks', 'Capture.cs', '_retainedDismiss.Remove(card);', '_retainedDismiss.ContainsKey(card);', 'completed or expired handover releases its retained original panel'),
            ('retained-stays-under-hidden-berth', 'Capture.cs', 'panel.FreezePresentationParent(_root.parent);', '{}', 'accepted original survives hidden berth without changing its frozen world pose'),
            ('active-model-reattaches-retained-card', 'Capture.cs', 'if (!_retainedDismiss.TryGetValue(card, out RemoteBoardCard? retained)\n            || !ReferenceEquals(retained, panel)) return false;', 'if (bool.Parse("true")) return false;', 'native active refresh preserves the frozen original before ordinary parent and glide writes'),
            ('freeze-keeps-old-glide', 'Card.cs', '_gliding = false;\n        _returningPose = false;\n        _glideTo = _root.transform.localPosition;\n        _glideScale = _root.transform.localScale.x;', '_gliding = true;\n        _returningPose = true;\n        _glideTo = Vector3.zero;\n        _glideScale = 1f;', 'frozen original cannot resume its obsolete parent home glide'),
            ('pending-dismiss-times-out', 'Flight.cs', 'pending.DismissOrigin != null ? !KeepDismissFlight', 'pending.DismissOrigin != null ? Time.unscaledTime - pending.ReceivedAt > ResolveSeconds || !KeepDismissFlight', 'accepted dismissal survives native model delay longer than two seconds'),
            ('stale-dismiss-scenario', 'Flight.cs', '!ReferenceEquals(scenario, ScenarioRuleLibrary.ScenarioManager.Scenario)', 'false', 'scenario replacement retires the pending original'),
            ('stale-dismiss-recovery', 'Flight.cs', 'cc.HandAbilityCards.Contains(card)', 'false', 'real recovery retires the pending original'),
            ('dismiss-artwork-times-out', 'Flight.cs', '!f.FromDismissBerth && f.ArtworkWait >= ResolveSeconds', 'f.ArtworkWait >= ResolveSeconds', 'accepted dismissal keeps waiting for its exact public artwork'),
            ('burn-drops-origin', 'Burn.cs', 'burn.DismissOrigin = origin;', 'burn.DismissOrigin = null;', 'burn release upgrades the same original hold with its exact origin'),
            ('burn-keeps-stale-origin', 'Burn.cs', '_dismissOrigins.Remove(card);', '_dismissOrigins.ContainsKey(card);', 'recovery retires dismissal origin before a later ordinary burn'),
            ('burn-ignores-owner-release', 'Burn.cs', 'burn.OwnerReleased &&', 'true &&', 'native artwork alone cannot authorize a remote burn flight'),
            ('burn-ignores-native-presentation', 'Burn.cs', 'CardAppearanceMirror.HasPresentedThrough(burn.PresentationPlayer, actor, card, burn.CompletionTime)', 'true', 'owner release waits for the exact native completion artwork'),
            ('burn-repeats-handover', 'Burn.cs', 'if (b.HandoverLogged)', 'if (b.HandoverLogged && bool.Parse("false"))', 'repeated release cannot restart the same burn flight'),
        ]
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        if args.case:
            unknown = set(args.case) - {item[0] for item in mutations}
            if unknown:
                raise ValueError('Unknown fixture cases: ' + ', '.join(sorted(unknown)))
            mutations = [item for item in mutations if item[0] in args.case]
        for name, filename, before, after, expected in mutations:
            case = run / name
            case.mkdir()
            for source_name, text in sources.items():
                if filename == source_name:
                    if text.count(before) != 1:
                        raise AssertionError('Causal-control source binding drift: ' + name)
                    text = text.replace(before, after, 1)
                (case / source_name).write_text(text)
            for path in (ROOT / 'scripts/active-dismiss-remote-runtime').glob('*.cs'):
                shutil.copyfile(path, case / path.name)
            project = case / 'RemoteDismiss.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>\n')
            code = execute([dotnet, 'build', '--disable-build-servers', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false'], case / 'build.log')
            if code:
                raise RuntimeError('Compilation is not a passing control: ' + str(case / 'build.log'))
            code = execute([dotnet, str(case / 'bin/Release/net8.0/RemoteDismiss.dll')], case / 'runtime.log')
            output = (case / 'runtime.log').read_text()
            passed = code == 0 if not expected else code != 0 and expected in output
            results.append({'name': name, 'passed': passed, 'exit_code': code, 'expected': expected, 'output': output})
            print(('PASS ' if passed else 'FAIL ') + name + ': ' + output.strip(), flush=True)
            if not passed:
                raise RuntimeError('Production/control failure: ' + str(case))
        if any(hashlib.sha256(path.read_bytes()).hexdigest() != hashes[str(path)] for path in inputs):
            raise RuntimeError('Production sources changed during test')
        status = 0
        return 0
    finally:
        (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': status, 'cases': results}, indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('active-dismiss-remote-runtime', status, completion, root, process_reaped=True), indent=2) + '\n')
        print('Evidence: ' + str(run), flush=True)


if __name__ == '__main__':
    raise SystemExit(main())
