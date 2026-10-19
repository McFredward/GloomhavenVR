#!/usr/bin/env python3
"""Execute production near-figure arbitration with explicit portable boundaries."""
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


def block(source, signature, indent='    '):
    start = source.index(indent + signature)
    end = source.index('\n' + indent + '}', start) + len('\n' + indent + '}')
    return source[start:end]


def generated_sources(root, producer_root=None):
    base = root / 'src/GloomhavenVR'
    paths = {
        'grabber': base / 'Hands/Interact/ProximityGrabber.cs',
        'ray': base / 'Hands/Interact/RayInteractor.cs',
        'interfaces': base / 'Hands/Interact/IGrabbable.cs',
        'registry': base / 'Hands/Interact/VRInteractables.cs',
        'figure': (producer_root or root) / 'src/GloomhavenVR/Board/FigureGrab/FigureGrabbable.cs',
        'ugui': base / 'Hands/Interact/RayUguiDriver.cs',
        'figure_driver': (producer_root or root) / 'src/GloomhavenVR/Board/FigureGrab/FigureGrabDriver.cs',
        'flat_pointer': (producer_root or root) / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.6.Pointer.cs',
        'flat_core': (producer_root or root) / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.1.Core.cs',
        'board_click': (producer_root or root) / 'src/GloomhavenVR/Board/BoardClickDriver.cs',
    }
    raw = {name: path.read_text() for name, path in paths.items()}
    # Compile the complete real interactor, including every early return, election,
    # hysteresis, callback, ForceGrab, held self-heal, and log branch.
    sources = {'ProximityGrabber.cs': raw['grabber'], 'IGrabbable.cs': raw['interfaces']}
    registry = raw['registry']
    start = registry.index('internal static class VRInteractables')
    end = registry.index('\n}', start) + 2
    narrator = block(registry, 'internal interface IGrabRefusalNarrator', indent='')
    sources['VRInteractables.cs'] = 'using System; using System.Collections.Generic; using GloomhavenVR.Core; using UnityEngine; namespace GloomhavenVR.Hands.Interact;\n' + registry[start:end] + '\n' + narrator
    ray = raw['ray']
    # This contiguous region includes actual field initializers and conservative
    # compatibility setters; no replacement claim predicate appears in the fixture.
    start = ray.index('    public Vector3? UiHitOverride')
    end = ray.index('    // ---- card-contact stand-down', start)
    has_fresh = re.search(r'    public bool HasFreshUiHit\s*=>[^;]+;', ray)[0]
    sources['RayClaims.cs'] = 'using UnityEngine; namespace GloomhavenVR.Hands.Interact; internal sealed partial class RayInteractor {\n' + ray[start:end] + '\n' + has_fresh + '\n}'
    figure = raw['figure']
    methods = '\n'.join(block(figure, signature) for signature in ('public bool CanGrab', 'public bool AllowsHand(', 'internal void SetProximitySuppressed('))
    methods = methods.replace('public bool CanGrab', 'public override bool CanGrab', 1).replace('public bool AllowsHand(', 'public override bool AllowsHand(', 1)
    constructor = block(figure, 'internal FigureGrabbable(').replace('internal FigureGrabbable(', 'internal Figure(', 1)
    pick_volume = re.search(r'    internal void SetPickVolume\(Collider volume\)\s*=>[^;]+;', figure)[0]
    sources['FigureEligibility.cs'] = 'using System; using UnityEngine; using GloomhavenVR.Hands; using CActor = GloomhavenVR.Hands.Interact.NativeCharacter; using ActorBehaviour = GloomhavenVR.Hands.Interact.NativeActor; namespace GloomhavenVR.Hands.Interact; internal sealed partial class Figure {\n' + constructor + '\n' + pick_volume + '\n' + methods + '\n}'
    # Compile the actual trigger-down block after the picking boundary. The real
    # near-offer guard, native-card exception, ownership stamp and pointer callback
    # remain contiguous; the fixture does not copy their priority policy.
    entry = block(raw['ugui'], 'if (_hand.TriggerDown && hit && _hand.Grabber.Held == null)', indent='        ')
    sources['UguiEntry.cs'] = 'using UnityEngine; using GloomhavenVR.Cards; namespace GloomhavenVR.Hands.Interact; internal sealed partial class RayUguiDriver { internal void StartPress(bool hit=true, bool nativeAreaHit=false, VRCard? selectedCard=null) { Vector2 screenPos = new();\n' + entry + '\n}}'
    driver = raw['figure_driver']
    constants = '\n'.join(re.search(r'^    (?:private|internal) const (?:float|int) ' + name + r' = [^;]+;', driver, flags=re.MULTILINE)[0] for name in ('ReachMeters', 'PickExitFactor', 'PickDwellFrames'))
    methods = '\n'.join(block(driver, signature) for signature in ('private void TryLaserGrab(', 'private void SelectByOffsetAnchor(', 'private void ApplySuppression('))
    methods = methods.replace('private void TryLaserGrab(', 'internal void TryLaserGrab(', 1).replace('private void SelectByOffsetAnchor(', 'internal void SelectByOffsetAnchor(', 1)
    # The actual net472 Unity/BCL signatures are oblivious to nullable annotations;
    # net8 Dictionary.TryGetValue adds MaybeNullWhen(false) absent from that target.
    sources['FigureDriver.cs'] = '#pragma warning disable CS8600\nusing System; using UnityEngine; using GloomhavenVR.Hands; using FigureGrabbable = GloomhavenVR.Hands.Interact.Figure; using CInteractableActor = GloomhavenVR.Hands.Interact.NativeActor; namespace GloomhavenVR.Hands.Interact; internal sealed partial class FigureGrabDriver {\n' + constants + '\n' + methods + '\n}'
    flat = raw['flat_pointer']
    tick = block(flat, 'private void TickPointer()')
    # Preserve the actual entire preamble: TickPoke runs BEFORE the ordinary
    # laser's figure guard. The rest of screen laser picking is an explicit sink.
    preamble = tick[tick.index('{') + 1:tick.index('        IPickProvider? pick =')]
    methods = '\n'.join(block(flat, signature) for signature in ('private void TickPoke()', 'private void TryBeginPoke(', 'private void EndPoke(', 'private void YieldScreenPointer()'))
    allowed = re.search(r'    private static bool PokePressAllowed\(VRHand hand\)\s*=>[^;]+;', flat)[0]
    constants = '\n'.join(re.search(r'^    private const float ' + name + r' = [^;]+;', raw['flat_core'], flags=re.MULTILINE)[0] for name in ('PokeContactMeters', 'PokeReleaseMeters', 'PokeThroughMeters', 'PokeDragUnlockMeters'))
    sources['FlatScreenPoke.cs'] = 'using UnityEngine; using GloomhavenVR.Core; using GloomhavenVR.Hands; namespace GloomhavenVR.WorldUI; internal sealed partial class FlatScreen {\n' + constants + '\n' + allowed + '\n' + methods + '\ninternal void TickPointerEntry() {\n' + preamble + '\nPointerContinuations++;\n}\n}'
    board = raw['board_click']
    methods = '\n'.join(block(board, signature) for signature in ('private static void TickFar()', 'private static void RequestClick(', 'public static bool ConsumePendingClick()'))
    methods = methods.replace('private static void TickFar()', 'internal static void TickFar()', 1)
    sources['BoardClick.cs'] = 'using GloomhavenVR.Core; using GloomhavenVR.Core.Events; using GloomhavenVR.Hands; namespace GloomhavenVR.Board; internal static partial class BoardClickDriver {\n' + methods + '\n}'
    return paths, sources


def execute(command, log, timeout=120):
    with log.open('w') as stream:
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--producer-root', type=Path, help='Explicit split-source worker development only')
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/near-figure-grab-runtime')
    parser.add_argument('--case', action='append', help='Partial development run only')
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = ROOT / 'scripts/near-figure-grab-runtime'
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    completion = run / 'completion.log'
    completion.write_text('evidence: ' + str(run) + '\n')
    print('evidence: ' + str(run), flush=True)
    status = 1
    result = {'scope': 'complete production ProximityGrabber; real Ray claim and Figure eligibility kernels',
              'boundaries': ['clock', 'input/tracking', 'AABB ClosestPoint', 'native eligibility inputs', 'haptic/audio/log sinks'],
              'unity_physics': False, 'native_srl_execution': False, 'network_session': False, 'headset': False, 'cases': []}
    try:
        paths, sources = generated_sources(root, args.producer_root.resolve() if args.producer_root else None)
        inputs = [*paths.values(), Path(__file__).resolve(), *fixture.glob('*.*')]
        hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}
        (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
        snapshot = run / 'Sources' / 'fixture'
        snapshot.mkdir(parents=True)
        for path in fixture.glob('*.*'):
            shutil.copyfile(path, snapshot / path.name)
        shutil.copyfile(Path(__file__).resolve(), run / 'Sources' / Path(__file__).name)
        for name, path in paths.items():
            destination = run / 'Sources' / 'production-inputs' / (name + '.cs')
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, destination)
        variants = [('production', {}, '')]
        # Reintroduce the actual previous beam-priority defect without altering the
        # election or the test oracle. Compilation failure never passes a control.
        old_beam = '    private bool BeamOwnsTrigger(IGrabbable target)\n    { return _hand.Ray.HasFreshUiHit && !_hand.Ray.FreshUiHitIsPanelHoverOnly; }'
        variants.append(('old-beam-priority', {'ProximityGrabber.cs': (block(sources['ProximityGrabber.cs'], 'private bool BeamOwnsTrigger('), old_beam)}, 'highlighted figure survives visual world clamp'))
        tail = block(block(sources['ProximityGrabber.cs'], 'private void UpdateHighlight()'), 'if (IsFigure(Highlighted))', indent='            ')
        variants.append(('misleading-figure-glow-tail', {'ProximityGrabber.cs': (tail, '')}, 'figure eligibility loss clears glow without a misleading tail'))
        mutations = [
            ('original-native-veto-lost', 'FigureEligibility.cs', 'if (_hasGameCollider && !VRInteractables.IsUsablePickShape(_gameCollider))', 'if (false && _hasGameCollider && !VRInteractables.IsUsablePickShape(_gameCollider))', 'real CanGrab retains original native collider veto under usable extension'),
            ('original-native-constructor-forgets-veto', 'FigureEligibility.cs', '_hasGameCollider = !ReferenceEquals(collider, null);', '_hasGameCollider = false;', 'real CanGrab retains original native collider veto under usable extension'),
            ('ui-live-offer-ignored', 'UguiEntry.cs', '_hand.Grabber.FigureGrabOffered || ', '', 'invalid live figure offer does not swallow actual UI press'),
            ('far-live-offer-ignored', 'FigureDriver.cs', 'hand.Grabber.FigureGrabOffered || ', '', 'invalid near glow cannot block a deliberate far pickup'),
            ('grip-keeps-false-figure-glow', 'ProximityGrabber.cs', 'clearHighlight: IsFigure(Highlighted)', 'clearHighlight: false', 'grip-bar acquisition cannot leave an unusable figure glowing'),
            ('flat-poke-steals-near-offer', 'FlatScreenPoke.cs', ' || hand.Grabber.FigureGrabOffered)', ')', 'actual early flat-screen poke cannot steal a lit figure in either update order'),
            ('flat-poke-start-unclaimed', 'FlatScreenPoke.cs', 'hand.Ray.ClaimTrigger("flat screen fingertip press");', '', 'actual initial fingertip press commits ownership'),
            ('flat-poke-active-unclaimed', 'FlatScreenPoke.cs', 'hand.Ray.ClaimTrigger("flat screen active fingertip press");', '', 'active fingertip renews committed claim'),
            ('world-hover-erases-active-claim', 'RayClaims.cs', '_uiHitLabel = label;\n    }\n\n    /// <summary>Hover protection', '_uiHitLabel = label;\n        _triggerClaimFrame = -10;\n    }\n\n    /// <summary>Hover protection', 'later hover cannot erase committed claim provenance'),
            ('board-spends-lit-figure-trigger', 'BoardClick.cs', '&& !hand.Grabber.FigureGrabOffered', '&& true', 'actual board trigger cannot duplicate highlighted near pickup in either update order'),
        ]
        for name, filename, before, after, expected in mutations:
            variants.append((name, {filename: (before, after)}, expected))
        dwell = ('if (winner != null && _dwellBySide[side] < PickDwellFrames && previousEligible)\n'
                 '                winner = held;\n'
                 '            else if (winner != null && _dwellBySide[side] < PickDwellFrames)\n'
                 '                winner = null;')
        old_dwell = ('if (winner != null && _dwellBySide[side] < PickDwellFrames)\n'
                     '                winner = held;\n            _ = previousEligible;')
        variants.append(('old-ineligible-winner-dwell', {'FigureDriver.cs': (dwell, old_dwell)}, 'switching dwell never reallows an ineligible old winner'))
        if args.case:
            selected = set(args.case); variants = [entry for entry in variants if entry[0] in selected]
            if len(variants) != len(selected):
                raise AssertionError('Unknown selected variant')
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        for name, mutations, expected in variants:
            case = run / name; generated = case / 'Sources'; generated.mkdir(parents=True)
            for filename, source in sources.items():
                if filename in mutations:
                    before, after = mutations[filename]
                    if source.count(before) != 1:
                        raise AssertionError('Causal source binding drift: ' + name)
                    source = source.replace(before, after, 1)
                (generated / filename).write_text(source)
            project = case / 'NearFigure.csproj'; shutil.copyfile(fixture / project.name, project)
            built = execute([dotnet, 'build', '--disable-build-servers', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:FixtureDir=' + str(snapshot), '-p:GeneratedDir=' + str(generated), '-p:UseSharedCompilation=false'], case / 'build.log')
            if built:
                raise RuntimeError('Compilation is not a passing causal control: ' + str(case / 'build.log'))
            ran = execute([dotnet, str(case / 'bin/Release/net8.0/NearFigure.dll')], case / 'runtime.log', timeout=30)
            output = (case / 'runtime.log').read_text()
            passed = (ran == 0 and 'PASS near figure grab:' in output) if not expected else (ran == 1 and 'ASSERT: ' + expected in output)
            result['cases'].append({'name': name, 'passed': passed, 'exit_code': ran, 'expected_failure': expected, 'output': output})
            print(('PASS ' if passed else 'FAIL ') + name + ': ' + output.strip().splitlines()[0], flush=True)
            if not passed:
                raise RuntimeError('Production/control failure: ' + str(case))
        changed = [str(path) for path in inputs if hashlib.sha256(path.read_bytes()).hexdigest() != hashes[str(path)]]
        result['changed_inputs'] = changed
        if changed:
            raise RuntimeError('Inputs changed during fixture run: ' + repr(changed))
        status = 0
        return 0
    finally:
        result['exit_code'] = status
        (run / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
        receipt = cleanup_completed_run('near-figure-grab-runtime', status, completion, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())
