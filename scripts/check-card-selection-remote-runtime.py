#!/usr/bin/env python3
"""Production owner publication, unchanged wire codecs, receive and glow application.

The default portable run uses an explicit hierarchy/activation boundary. It proves
the state-to-activation path, not Unity rendering, a network session, or HMD pixels.
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
import sys
import tempfile
import xml.etree.ElementTree as ET

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


def uncomment(source):
    # Do not rewrite strings; only standalone documentation/comment lines.
    return re.sub(r'^\s*//[^\n]*\n', '', source, flags=re.MULTILINE)


def source_inputs(root):
    # Reuse the central wire project's real production input list, not substitute
    # serializers. Vector/test files are excluded; their separate golden gate stays intact.
    project = root / 'tests/GloomhavenVR.WireTests/GloomhavenVR.WireTests.csproj'
    files = []
    for entry in ET.parse(project).iter('Compile'):
        path = entry.get('Include', '').replace('\\', '/')
        if path.startswith('$(RepoRoot)'):
            path = root / path.replace('$(RepoRoot)', '')
            if not path.is_file():
                raise AssertionError('Central production wire input missing: ' + str(path))
            files.append(path)
    return project, files


def generated_sources(root):
    paths = {
        'driver': root / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs',
        'tray': root / 'src/GloomhavenVR/Cards/Tray/PlayTray.4.Slots.cs',
        'remote': root / 'src/GloomhavenVR/Net/Remote/RemoteAvatar.cs',
        'furniture': root / 'src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs',
        'slots': root / 'src/GloomhavenVR/Net/Board/LocalBoardSlots.cs',
        'board': root / 'src/GloomhavenVR/Net/Remote/RemoteControlBoard.cs',
        'reveal': root / 'src/GloomhavenVR/Net/RevealGate.cs',
    }
    driver, tray, remote, furniture, slots, board, reveal = (paths[name].read_text() for name in paths)
    sampler = block(driver, 'private static int SampleBoardUi(').replace('private static', 'internal static', 1)
    sender = block(driver, 'if (boardUiNow >= 0)')
    receiver = remote[remote.index('        HasBoardUi = p.HasBoardUi;'):remote.index('        // FOLLOW/PIN: absent record')]
    fan_receiver = remote[remote.index('        HandCardCount = p.HandCardCount;'):remote.index('        // SECOND HELD FIGURE')]
    order_receiver = remote[remote.index('        SlotOrderKnown = p.HasSlotOrder;'):remote.index('        // TRACK HOVER')]
    slot_methods = '\n'.join(block(slots, signature) for signature in (
        'private static VRCard? CardInSlot(', 'private static FullAbilityCard? WidgetInSlot(',
        'internal static bool TrySampleSlotOrder(PlayTray? tray, CardsHandUI? hand, out bool swapped,',
        'private static string SlotCardName('))
    order_sample = driver[driver.index('        bool slotOrder = LocalBoardSlots.TrySampleSlotOrder('):driver.index('        int slotOrderNow =')]
    assert order_sample.count('trayNow, CardsGameApi.ActiveHand()') == 1
    order_sample = order_sample.replace('trayNow, CardsGameApi.ActiveHand()', 'tray, hand', 1)
    order_sender = block(driver, 'if (slotOrder)')
    order_method = block(board, 'private void OrderRoundCards(').replace('private void', 'internal void', 1)
    peer_face = re.search(r'public static bool ShowPeerCardFronts\(CPlayerActor\? actor\)\s*=>[^;]+;', reveal)[0]
    glow = furniture[furniture.index('        int wantedMask;'):furniture.index('        SetSnap(snapMask);') + len('        SetSnap(snapMask);')]
    wanted_property = re.search(r'internal int WantedSlotMask => [^;]+;', tray)[0]
    tray_methods = '\n'.join((block(tray, 'internal void SetWantedSlots('), wanted_property,
                              block(tray, 'internal int OccupiedSlotMask'), block(tray, 'private static bool SlotHoldsCard(')))
    furniture_methods = '\n'.join((block(furniture, 'private void SetWanted('), block(furniture, 'private void SetSnap(')))
    prefix = 'using System; using GloomhavenVR.Cards; using GloomhavenVR.Core; using UnityEngine; using GameObject = FixtureObject; using Transform = FixtureTransform; using Time = FixtureClock;\n'
    sources = {
        'Owner.cs': prefix + 'namespace GloomhavenVR.Net { internal static class OwnerPublication {\n' + sampler + '\ninternal static PresenceState Publish(PlayTray? tray, CardsHandUI? hand = null) { var extras = new PresenceState(); int boardUiNow = SampleBoardUi(tray);\n' + sender + '\n' + order_sample + '\n' + order_sender + '\nreturn extras; } }}',
        'Tray.cs': prefix + 'namespace GloomhavenVR.Cards { internal sealed partial class PlayTray {\n' + tray_methods + '\n}}',
        'Receiver.cs': prefix + 'namespace GloomhavenVR.Net { internal sealed partial class RemoteAvatar { internal void Apply(in PresenceState p) {\n' + receiver + '\n' + fan_receiver + '\n' + order_receiver + '\n}}}',
        'Furniture.cs': prefix + 'namespace GloomhavenVR.Net { internal sealed partial class RemoteBoardFurniture { internal void TickWire(RemoteAvatar owner, int slotMask) { bool synced = owner.HasBoardUi; bool slot0 = (slotMask & 1) != 0; bool slot1 = (slotMask & 2) != 0;\n' + glow + '\n}\n' + furniture_methods + '\n}}',
        'LocalBoardSlots.cs': prefix + 'namespace GloomhavenVR.Net { internal static class LocalBoardSlots {\n' + slot_methods + '\n}}',
        'RemoteControlBoard.cs': prefix + 'namespace GloomhavenVR.Net { internal sealed partial class RemoteControlBoard {\n' + order_method + '\n}}',
        'PeerFace.cs': prefix + 'namespace GloomhavenVR.Net { internal static partial class RevealGate {\n' + peer_face + '\n}}',
    }
    # Scope bindings for the unchanged full methods outside the compiled state kernel.
    bindings = {
        'board_state_changes_preempt_ordinary_extras_cadence': '&& !boardUiChanged && !boardSnapDue' in driver,
        'full_snapshot_record_four_written_each_ordinary_packet': 'if (boardUiNow >= 0)' in driver,
        'occupancy_read_physical_slot_hierarchy': 'SlotHoldsCard(_slots[i])' in tray_methods,
        'furniture_owner_mask_authoritative': 'wantedMask = owner.WantedGlowMask;' in glow,
        'slot_order_changes_preempt_ordinary_cadence': '&& !slotOrderChanged' in driver,
        'physical_order_recomputed_by_board_each_frame': 'OrderRoundCards(actor);' in block(board, 'public void Tick('),
        'empty_recess_wire_overrules_lagged_model': 'int wire = exhausted ? 0 : _owner.SlotOccupancyKnown ? _owner.BoardSlotMask : -1;' in board and '_cards[i].Set(null, showFronts, actor); // empty recess' in board,
    }
    if not all(bindings.values()):
        raise AssertionError('Publication/application source binding drift: ' + repr(bindings))
    return paths, {name: uncomment(code) for name, code in sources.items()}, bindings


def execute(command, log, timeout=180):
    with log.open('w') as stream:
        child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/card-selection-remote-runtime')
    parser.add_argument('--case', action='append', help='Partial development run only')
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = ROOT / 'scripts/card-selection-remote-runtime'
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    print('evidence: ' + str(run), flush=True)
    status = 1
    result = {'scope': 'portable production publication/codec/application kernel; hierarchy/activation boundary',
              'unity_rasterization': False, 'native_network_session': False, 'headset': False, 'cases': []}
    try:
        paths, sources, bindings = generated_sources(root)
        wire_project, wire = source_inputs(root)
        core = root / 'ressources/GH_Data/Managed/UnityEngine.CoreModule.dll'
        if not core.is_file():
            raise RuntimeError('Real UnityEngine.CoreModule reference is required')
        inputs = [*paths.values(), wire_project, *wire, core, Path(__file__).resolve(), *fixture.glob('*.*')]
        hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}
        (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
        (run / 'source-bindings.json').write_text(json.dumps(bindings, indent=2) + '\n')
        variants = [('production', '', '', '', '')]
        mutations = [
            ('owner-wanted-stale', 'Owner.cs', 'trayNow.WantedSlotMask & NetProtocol.BoardUiWantedMask', '0', 'all wanted masks reach the remote overlay'),
            ('owner-occupancy-stale', 'Owner.cs', '(trayNow.OccupiedSlotMask << NetProtocol.BoardUiSlotShift)', '(3 << NetProtocol.BoardUiSlotShift)', 'physical withdrawal clears remote occupancy'),
            ('sender-mask-lost', 'Owner.cs', 'extras.BoardOverlayMask = (byte)((boardUiNow >> 8) & 0xFF);', 'extras.BoardOverlayMask = 0;', 'physical withdrawal clears remote occupancy'),
            ('receiver-mask-stale', 'Receiver.cs', 'WantedGlowMask = p.HasBoardUi ? p.BoardOverlayMask & NetProtocol.BoardUiWantedMask : 0;', 'WantedGlowMask = 0;', 'all wanted masks reach the remote overlay'),
            ('remote-rederives-from-occupancy', 'Furniture.cs', 'wantedMask = owner.WantedGlowMask;', 'wantedMask = (~slotMask) & 3;', 'all wanted masks reach the remote overlay'),
            ('remote-overlay-never-reopens', 'Furniture.cs', '_wanted[i]!.SetActive(on);', '_wanted[i]!.SetActive(false);', 'all wanted masks reach the remote overlay'),
            ('held-card-still-occupies-slot', 'Tray.cs', 'card != null && !card.IsHeld', 'card != null', 'held card no longer occupies its former recess'),
            ('withdrawn-card-restores-stale-slot', 'Receiver.cs', 'BoardSlotMask = slotMask;', 'BoardSlotMask |= slotMask;', 'physical withdrawal clears remote occupancy'),
            ('physical-swap-not-published', 'Owner.cs', 'extras.SlotOrderSwapped = slotOrderSwapped;', 'extras.SlotOrderSwapped = false;', 'remote model order follows the two physical recesses'),
            ('physical-swap-not-applied', 'RemoteControlBoard.cs', 'if (_owner.SlotOrderKnown && _owner.SlotOrderSwapped', 'if (false && _owner.SlotOrderKnown && _owner.SlotOrderSwapped', 'remote model order follows the two physical recesses'),
            ('peer-local-ownership-leaks-selection-front', 'PeerFace.cs', 'locallyControlled: false', 'locallyControlled: true', 'remote selection cards remain covered even for locally controlled actors'),
        ]
        for name, file, before, after, expected in mutations:
            assert sources[file].count(before) == 1, 'Mutation binding drift: ' + name
            variants.append((name, file, before, after, expected))
        if args.case:
            names = set(args.case)
            variants = [entry for entry in variants if entry[0] in names]
            if len(variants) != len(names):
                raise AssertionError('Unknown selected case')
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        for name, file, before, after, expected in variants:
            case = run / name
            generated = case / 'Sources'
            generated.mkdir(parents=True)
            for source_name, source in sources.items():
                if source_name == file:
                    source = source.replace(before, after, 1)
                (generated / source_name).write_text(source)
            for index, path in enumerate(wire):
                shutil.copyfile(path, generated / ('Wire' + str(index) + '-' + path.name))
            project = case / 'RemoteSelection.csproj'
            shutil.copyfile(fixture / project.name, project)
            command = [dotnet, 'build', '--disable-build-servers', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                       '-p:FixtureDir=' + str(fixture), '-p:GeneratedDir=' + str(generated), '-p:UnityCore=' + str(core), '-p:UseSharedCompilation=false']
            built = execute(command, case / 'build.log')
            if built:
                raise RuntimeError('Compilation is not a passing causal control: ' + str(case / 'build.log'))
            command = [dotnet, str(case / 'bin/Release/net8.0/RemoteSelection.dll')]
            ran = execute(command, case / 'runtime.log', timeout=45)
            output = (case / 'runtime.log').read_text()
            passed = ran == 0 if not expected else ran != 0 and expected in output
            result['cases'].append({'name': name, 'passed': passed, 'exit_code': ran, 'expected': expected, 'output': output})
            print(('PASS ' if passed else 'FAIL ') + name + ': ' + output.strip(), flush=True)
            if not passed:
                raise RuntimeError('Production/control failure: ' + str(case))
        changed = [str(path) for path in inputs if hashlib.sha256(path.read_bytes()).hexdigest() != hashes[str(path)]]
        if changed:
            raise RuntimeError('Inputs changed during test: ' + repr(changed))
        status = 0
        return 0
    finally:
        result['exit_code'] = status
        (run / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
        receipt = cleanup_completed_run('card-selection-remote-runtime', status, completion, root, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())
