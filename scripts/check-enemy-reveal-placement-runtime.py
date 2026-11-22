#!/usr/bin/env python3
"""Execute the enemy reveal's production fit/pose and depth rendering in Unity."""
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

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / 'scripts/enemy-reveal-placement-runtime'


def block(source, signature):
    start = source.index(signature)
    at = source.index('{', start)
    end, depth = at + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def member(source, signature):
    start = source.index(signature)
    brace, semi = source.index('{', start), source.index(';', start)
    return source[start:semi + 1] if semi < brace else block(source, signature)


def centered_reveal_sources(source):
    surface = source['surface']
    fields = surface[surface.index('private Vector3 _position;'):surface.index('private bool _lastVisible;')]
    constants = '\n'.join(re.search(r'private (?:const|static readonly) [^;]*?\b' + name + r' = [^;]+;', surface)[0]
        for name in ('DensityScale', 'MaxWidthMeters', 'FitPinGrowthFraction', 'FollowDeadzoneDeg', 'FollowDwellSeconds',
                     'FollowSettledDeg', 'FollowEaseRate', 'RevealReadingDistance', 'RevealViewDrop',
                     'NominalHalfSize', 'FollowPositionTolerance'))
    methods = [block(surface, 'private ' + signature) for signature in (
        'void PlantPose(', 'void CaptureOpening(', 'Vector3 LayoutGoal(', 'void Place(')]
    methods.extend(member(surface, 'private ' + signature) for signature in ('static bool NearPosition(', 'void AddBoard('))
    surface_text = 'internal sealed partial class EnemyRevealSurface {\n' + constants + fields + '\n'.join(methods) + '}'

    board = source['board']
    board_consts = '\n'.join(re.search(r'(?:private|internal) const float ' + key + r' = [^;]+;', board)[0]
        for key in ('BoardW', 'BoardH', 'BoardTopLocalY', 'BoardHalfWidthLocal', 'BoardExtentSanityMargin'))
    board_text = 'internal sealed partial class PlayTray {\n' + board_consts
    board_text += re.search(r'internal const float TrayPixelsPerMeter = [^;]+;', source['board-core'])[0]
    board_text += 'private static readonly List<MeshRenderer> ExtentScratch = new(48);\n'
    board_text += block(board, 'internal static void MeasureBoardLocalExtents(')
    board_text += block(board, 'internal static Bounds MeasureBoardLocalBounds(') + '}'
    facing = 'internal static class PanelPlacement {\n' + block(source['facing'], 'internal static Quaternion Facing(')
    facing += re.search(r'private static Vector3 Flatten\(Vector3 v\)[^;]+;', source['facing'])[0] + '}'
    head_facing = 'internal static class HeadFacing {\n'
    head_facing += re.search(r'private const float DegenerateSqr = [^;]+;', source['head-facing'])[0]
    head_facing += member(source['head-facing'], 'internal static Vector3 Flatten(') + '}'
    placement = source['placement'][source['placement'].index('internal sealed class EnemyRevealPlacement'):]
    return '\n'.join([surface_text, board_text, facing, head_facing, placement])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output', type=Path, default=ROOT / '.planning/debug/enemy-reveal-placement')
    parser.add_argument('--unity', type=Path, default=Path(os.getenv('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    args = parser.parse_args()
    plugin = args.source_root / 'src/GloomhavenVR'
    paths = {
        'surface': 'WorldUI/Surfaces/EnemyRevealSurface.cs',
        'placement': 'WorldUI/Surfaces/EnemyRevealPlacement.cs',
        'board': 'Cards/Tray/PlayTray.6.Build.cs',
        'board-core': 'Cards/Tray/PlayTray.1.Core.cs',
        'facing': 'WorldUI/Conversion/PanelPlacement.cs',
        'head-facing': 'WorldUI/HeadFacing.cs',
    }
    production = {key: (plugin / path).read_text() for key, path in paths.items()}
    source = centered_reveal_sources(production)
    fixture = (FIXTURE / 'Cases.cs').read_text()
    runner_template = (FIXTURE / 'Runner.cs').read_text()
    mutations = {
        'rectangle-width': ('facing * Vector3.right * halfSize.x', 'facing * Vector3.right * 0.05f',
            'wide fit repositions to clear the peer board'),
        'peer-board': ('Net.PeerBoardFade.CollectShownBoardRoots(_boardRoots);', '_boardRoots.Clear();',
            'wide fit repositions to clear the peer board'),
        'board-independent': ('if (!_placed || poseVersion != _facedPoseVersion)',
            'if (_placed || poseVersion != _facedPoseVersion)', 'a moving board never steers a standing reveal'),
        'gaze-dwell': ('now - _offGazeSince >= FollowDwellSeconds', 'now - _offGazeSince >= 0f',
            'follow waits out the physical head dwell'),
        'asymmetric-eye': ('(1f - Mathf.Abs(offset)) / diagonal', '1f / diagonal',
            'stereo aperture uses the tighter asymmetric eye'),
    }
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    completion = run / 'completion.log'
    completion.write_text(f'Evidence: {run}\n')
    print(f'Evidence: {run}', flush=True)
    sources = run / 'sources'
    sources.mkdir()
    project = run / 'unity'
    for folder in ('Assets/Editor', 'ProjectSettings', 'Packages'):
        (project / folder).mkdir(parents=True)
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {
        'com.unity.modules.imageconversion': '1.0.0', 'com.unity.modules.physics': '1.0.0',
        'com.unity.modules.xr': '1.0.0'}}))
    cases = {'production': source}
    for name, (original, mutated, _) in mutations.items():
        assert source.count(original) == 1, original
        cases[name] = source.replace(original, mutated)
    run_calls = []
    for case, generated in cases.items():
        scope = 'Fixture' + case.title().replace('-', '')
        fixture_text = fixture.replace('namespace Fixture\n', f'namespace {scope}\n', 1)
        generated = ('using System.Collections.Generic; using UnityEngine; using UnityEngine.XR;\n'
            f'namespace {scope} {{ using Time = FixtureTime;\n{generated}\n}}')
        (sources / f'{case}.cs').write_text(generated)
        (project / f'Assets/Editor/{case}Production.cs').write_text(generated)
        (project / f'Assets/Editor/{case}Fixture.cs').write_text(fixture_text)
        expected = mutations[case][2] if case in mutations else ''
        run_calls.append(f'Check({json.dumps(case)}, {scope}.Cases.Run, {json.dumps(expected)});')
    (project / 'Assets/Editor/Runner.cs').write_text(runner_template.replace('    // CASES', '\n'.join(run_calls)))
    pins = {str(plugin / path): hashlib.sha256((plugin / path).read_bytes()).hexdigest() for path in paths.values()}
    for path in [Path(__file__).resolve(), *FIXTURE.glob('*.cs')]:
        pins[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    cmd = [str(args.unity), '-batchmode', '-quit', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'EnemyRevealPlacementRunner.Run', '-enemyRevealEvidence', str(run),
        '-logFile', str(run / 'unity.log')]
    if not os.environ.get('DISPLAY'):
        cmd[:0] = ['xvfb-run', '-a']
    code = 1
    try:
        child = subprocess.Popen(cmd, start_new_session=True)
        try:
            code = child.wait(timeout=180)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()
        result = run / 'result.txt'
        if result.is_file():
            print(result.read_text(), end='')
        if code or not result.is_file() or 'FAIL' in result.read_text():
            code = 1
            print(f'FAIL Unity placement fixture. Run log: {run / "unity.log"}')
        return code
    finally:
        receipt = cleanup_completed_run('enemy-reveal-placement', code, completion, ROOT, process_reaped=True)
        (run / 'retention.json').write_text(json.dumps(receipt, indent=2) + '\n')
        # Generated fixture sources and all logs/receipts/screenshots stay above.
        if project.is_dir() and not project.is_symlink():
            shutil.rmtree(project)


if __name__ == '__main__':
    raise SystemExit(main())
