#!/usr/bin/env python3
"""Validate town-cloth integration and run real Unity 2021.3 positive/null controls."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def source_contract(source):
    checks = {
        'both local hands': ('if (VRHands.Left?.HasPose == true' in source
                             and 'if (VRHands.Right?.HasPose == true' in source),
        'six remote hands': 'MaximumPeerHands = 6' in source and 'TryGetTownClothHandProbes' in source,
        'local head mask': 'VRRigDriver.HeadCamera' in source and 'PlaceHead(_heads[headAt++]' in source,
        'three remote heads': 'MaximumHeads = 4' in source and 'TryGetTownFaceHead' in source,
        'particle-aligned table support': ('const int samples = DriverColumns' in source
                                           and 'TableSupport(runner, i, "Front"' in source
                                           and 'TableSupport(runner, i, "Rear"' in source),
        'all native colliders attached': 'runner.SupportPairs.Count + _hands.Length + _heads.Length' in source,
        'no ray interception': source.count('layer = IgnoreRaycastLayer') >= 5,
        'single vertex snapshot': source.count('runner.Cloth.vertices;') == 1,
        'single-layer physical driver': 'DriverRows = 25' in source and 'DriverColumns = 13' in source,
        'FBX reorder independent': ('AuthoredPoint(row, column, minX, maxX)' in source
                                    and 'shipping FBX importer reorders and splits' in source),
        'nested FBX scale converted': ('stationUnitInDriver = driver.InverseTransformVector' in source
                                      and 'Physics.gravity * runner.StationUnitInDriver' in source
                                      and '* MaximumFreedomRealMeters * stationUnitInDriver' in source
                                      and '.004f * stationUnitInDriver' in source),
        'bounded smooth visible deformation': ('MaximumFreedomRealMeters = .11f' in source
                                               and 'VisibleDriverMap' in source
                                               and 'DriverDelta(in runner.VisibleDriverMap[n]' in source
                                               and 'RestoreAfterContact(runner, dt)' in source
                                               and 'runner.EpisodeOrigin' in source
                                               and 'SetFreedom(runner, 0f)' in source
                                               and 'runner.CaptureOrigin = true' in source
                                               and 'Array.Copy(simulated, runner.EpisodeOrigin' in source
                                               and 'AnyProbeWithin(runner, .16f, out' in source
                                               and 'AnyProbeWithin(runner, .018f, out' in source
                                               and 'ProbeWithin(runner, probe' in source
                                               and 'DistanceSquaredToSegment(surface[i], localA, localB)' in source
                                               and 'runner.DeformationWeight = contact ? 1f' in source
                                               and 'if (runner.CaptureOrigin && runner.ContactSurface.Length == runner.EpisodeOrigin.Length)' in source
                                               and 'Array.Copy(runner.ContactSurface, runner.EpisodeOrigin' in source
                                               and source.count('AddComponent<Cloth>()') == 1),
        'solver initializes before first touch': ('IdleFreedomRealMeters = .0005f' in source
                                                  and 'runner.DriverFreedom[n]\n                * IdleFreedomRealMeters * stationUnitInDriver' in source
                                                  and 'runner.DriverFreedom[n] * IdleFreedomRealMeters * runner.StationUnitInDriver' in source),
        'real local fingertips': ('VRHands.Left.Rig.IndexTip.position' in source
                                  and 'VRHands.Right.Rig.IndexTip.position' in source
                                  and 'VRHands.Left.WorldScale' in source
                                  and 'VRHands.Right.WorldScale' in source),
        'contact follows physical surface': ('runner.ContactSurface = simulated;' in source
                                             and 'Vector3[] surface = runner.ContactSurface' in source
                                             and 'DistanceSquaredToSegment(surface[i], localA, localB)' in source),
        'merchant side banner absent': ('service == 1 ? 0 : service == 2 ? 2 : 1' in source
                                        and 'if (filters.Count == 0) return;' in source),
    }
    missing = [name for name, present in checks.items() if not present]
    if missing:
        raise AssertionError(', '.join(missing))


def validate_source():
    source = (ROOT / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceCloth.cs').read_text()
    source_contract(source)
    figure_cloth = (ROOT / 'src/GloomhavenVR/Board/FigureGrab/FigureCloth.cs').read_text()
    assert 'next[v].maxDistance = m >= float.MaxValue ? m : m * factor;' in figure_cloth
    mutations = {
        'local-hand': ('if (VRHands.Left?.HasPose == true', 'if (VRHands.Left == null'),
        'remote-hand': ('TryGetTownClothHandProbes', 'DisabledRemoteHandProbe'),
        'head-mask': ('TryGetTownFaceHead', 'DisabledRemoteHeadProbe'),
        'table': ('const int samples = DriverColumns', 'const int samples = 0'),
        'raycast': ('layer = IgnoreRaycastLayer', 'layer = 0'),
        'extra-frame-snapshot': ('Vector3[] simulated = runner.Cloth.vertices;',
                                 'Vector3[] simulated = runner.Cloth.vertices; var duplicate = runner.Cloth.vertices;'),
        'nested-scale': ('stationUnitInDriver = driver.InverseTransformVector',
                         'missingScale = driver.InverseTransformVector'),
        'scale-correct-gravity': ('Physics.gravity * runner.StationUnitInDriver',
                                  'Physics.gravity'),
        'bounded-envelope': ('MaximumFreedomRealMeters = .11f',
                             'MaximumFreedomRealMeters = .34f'),
        'smooth-map': ('DriverDelta(in runner.VisibleDriverMap[n]',
                       'simulated[runner.VisibleDriverVertex[n]] - runner.DriverRest[runner.VisibleDriverVertex[n]]'),
        'episode-zero': ('Array.Copy(simulated, runner.EpisodeOrigin, simulated.Length)',
                         'Array.Copy(runner.DriverRest, runner.EpisodeOrigin, simulated.Length)'),
        'idle-pin': ('SetFreedom(runner, 0f)', 'SetFreedom(runner, 1f)'),
        'component-rebuild': ('SetFreedom(runner, 0f);',
                              'SetFreedom(runner, 0f); runner.Cloth = runner.DriverRoot.AddComponent<Cloth>();'),
        'proximity-is-contact': ('AnyProbeWithin(runner, .018f, out',
                                 'AnyProbeWithin(runner, .16f, out'),
        'point-aabb-contact': ('DistanceSquaredToSegment(surface[i], localA, localB)',
                               'broadphase.SqrDistance(a)'),
        'invisible-contact-physics': ('runner.DeformationWeight = contact ? 1f',
                                      'runner.DeformationWeight = false ? 1f'),
        'attenuated-contact-physics': ('runner.DeformationWeight = contact ? 1f',
            'runner.DeformationWeight = Mathf.MoveTowards(runner.DeformationWeight, contact ? 1f'),
        'short-touch-origin': ('if (runner.CaptureOrigin && runner.ContactSurface.Length == runner.EpisodeOrigin.Length)',
                               'if (runner.DeformationWeight <= 0f) runner.CaptureOrigin = true;'),
        'fingertip': ('VRHands.Left.Rig.IndexTip.position', 'VRHands.Left.Rig.PalmCenter.forward'),
        'rest-surface-gate': ('Vector3[] surface = runner.ContactSurface',
                              'Vector3[] surface = runner.DriverRest'),
        'merchant-banner-reintroduced': ('service == 1 ? 0 : service == 2 ? 2 : 1',
                                        'service == 1 ? 1 : service == 2 ? 2 : 1'),
        'all-particles-fixed-at-start': ('IdleFreedomRealMeters = .0005f',
                                         'IdleFreedomRealMeters = 0f'),
    }
    for name, (before, after) in mutations.items():
        changed = source.replace(before, after) if name == 'raycast' else source.replace(before, after, 1)
        try:
            source_contract(changed)
        except AssertionError:
            continue
        raise AssertionError('negative control escaped: ' + name)
    print('source_contract=PASS negative_controls=' + str(len(mutations)))


def main():
    validate_source()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path,
                        default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--output-dir', type=Path,
                        default=ROOT / '.planning/debug/town-cloth-native')
    parser.add_argument('--bundle', type=Path, default=ROOT / 'prebuilt/ghvr-town.bundle')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    project = run / 'project'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    fixture = ROOT / 'scripts/town-cloth-runtime'
    shutil.copyfile(fixture / 'Builder.cs', project / 'Assets/Editor/Builder.cs')
    shutil.copyfile(fixture / 'TownClothProbe.cs', project / 'Assets/TownClothProbe.cs')
    shutil.copyfile(fixture / 'ProductionBoundaries.cs', project / 'Assets/ProductionBoundaries.cs')
    production = (ROOT / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceCloth.cs').read_text()
    production = production.replace('namespace GloomhavenVR.WorldUI;\n',
                                    'namespace GloomhavenVR.WorldUI\n{\n', 1) + '\n}\n'
    (project / 'Assets/TownServiceCloth.cs').write_text(production)
    dead_visible = production.replace('TownServiceCloth', 'TownServiceClothDead')
    dead_visible = dead_visible.replace('runner.DeformationWeight = contact ? 1f',
                                        'runner.DeformationWeight = false ? 1f', 1)
    (project / 'Assets/TownServiceClothDead.cs').write_text(dead_visible)
    contact_reset = production.replace('TownServiceCloth', 'TownServiceClothContactReset')
    contact_reset = contact_reset.replace(
        'if (runner.CaptureOrigin && runner.ContactSurface.Length == runner.EpisodeOrigin.Length)\n'
        '            {\n'
        '                Array.Copy(runner.ContactSurface, runner.EpisodeOrigin, runner.EpisodeOrigin.Length);\n'
        '                runner.CaptureOrigin = false;\n'
        '            }',
        'if (runner.DeformationWeight <= 0f) runner.CaptureOrigin = true;', 1)
    (project / 'Assets/TownServiceClothContactReset.cs').write_text(contact_reset)
    rest_gate = production.replace('TownServiceCloth', 'TownServiceClothRestGate')
    rest_gate = rest_gate.replace(
        'Vector3[] surface = runner.ContactSurface.Length == runner.DriverRest.Length\n'
        '            ? runner.ContactSurface : runner.DriverRest;',
        'Vector3[] surface = runner.DriverRest;', 1)
    (project / 'Assets/TownServiceClothRestGate.cs').write_text(rest_gate)
    (project / 'Packages/manifest.json').write_text(
        '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.cloth":"1.0.0","com.unity.modules.physics":"1.0.0"}}')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    editor_log = run / 'editor.log'
    built = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-nographics',
        '-projectPath', str(project), '-executeMethod', 'Builder.BuildLinux', '-logFile', str(editor_log)],
        timeout=300)
    if built.returncode:
        raise SystemExit('Unity cloth probe build failed: ' + str(editor_log))
    result = run / 'result.txt'
    player_log = run / 'player.log'
    played = subprocess.run(['xvfb-run', '-a', str(project / 'Build/towncloth'), '-batchmode',
        '--result=' + str(result), '--bundle=' + str(args.bundle.resolve()),
        '-logFile', str(player_log)], timeout=120)
    player_text = player_log.read_text(errors='replace') if player_log.exists() else ''
    # Build 573 looked green in the simulated contact sweep while Unity refused
    # to initialize the actual game driver at startup. This exact engine warning
    # preceded every rigid headset cloth in the supplied Player.log. Do not
    # allow another green metric with a dead native solver.
    initialized = 'All cloth particles are fixed so the Cloth component is not initialized' not in player_text
    if played.returncode or not result.exists() or 'PASS' not in result.read_text() or not initialized:
        raise SystemExit('Unity cloth probe failed: ' + str(player_log))
    print('native_solver_initialization=PASS (no all-particles-fixed warning)')
    print(result.read_text().strip())
    print('evidence:', run)


if __name__ == '__main__':
    main()
