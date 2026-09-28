#!/usr/bin/env python3
"""Validate town-cloth integration and run real Unity 2021.3 positive/null controls."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def source_contract(source):
    checks = {
        'both local hands': ('if (VRHands.Left?.HasPose == true' in source
                             and 'if (VRHands.Right?.HasPose == true' in source),
        'six remote hands': 'MaximumPeerHands = 6' in source and 'TryGetTownClothHandProbes' in source,
        'local head mask': 'VRRigDriver.HeadCamera' in source and 'PlaceHead(_heads[headAt++]' in source,
        'three remote heads': 'MaximumHeads = 4' in source and 'TryGetTownClothHead' in source,
        'particle-aligned table support': ('const int samples = DriverColumns' in source
                                           and 'TableSupport(runner, i, "Front"' in source
                                           and 'TableSupport(runner, i, "Rear"' in source),
        'all native colliders attached': 'runner.SupportPairs.Count + _hands.Length + _heads.Length' in source,
        'no ray interception': source.count('layer = IgnoreRaycastLayer') >= 5,
        'idle-gated vertex snapshots': (source.count('runner.Cloth.vertices;') == 2
            and 'runner.ContactSurface = runner.Cloth.vertices;' in source
            and '!runner.Interactive && runner.DeformationWeight <= 0f' in source),
        'single-layer physical driver': 'DriverRows = 25' in source and 'DriverColumns = 13' in source,
        'FBX reorder independent': ('AuthoredPoint(row, column, minX, maxX)' in source
                                    and 'shipping FBX importer reorders and splits' in source),
        'nested FBX scale converted': ('stationUnitInDriver = driver.InverseTransformVector' in source
                                      and 'Physics.gravity * runner.StationUnitInDriver' in source
                                      and '* maximumFreedom * stationUnitInDriver' in source
                                      and '.004f * stationUnitInDriver' in source),
        'bounded smooth visible deformation': ('MaximumFreedomRealMeters = .08f' in source
                                               and 'maximumFreedom = _service == 3 ? .065f : MaximumFreedomRealMeters' in source
                                               and 'ContactPresentationSeconds = .045f' in source
                                               and 'cloth.bendingStiffness = .94f' in source
                                               and 'cloth.damping = .55f' in source
                                               and 'VisibleDriverMap' in source
                                               and 'BlendedDelta(in runner.VisibleDriverMap[n], runner.VisualDelta)' in source
                                               and 'RestoreAfterContact(runner, dt)' in source
                                               and 'PrepareProjection(runner)' in source
                                               and 'bool updateClearance = runner.ProjectionCount != 0 || runner.ActiveClearance' in source
                                               and 'toStation.MultiplyPoint3x4(shown), runner.Freedom[n])' in source
                                               and 'runner.EpisodeOrigin' in source
                                               and source.count('SetFreedom(runner, 0f)') == 2
                                               and source.count('Array.Copy(runner.ContactSurface, runner.EpisodeOrigin') == 2
                                               and 'AnyProbeWithin(runner, .16f, out' in source
                                               and 'AnyProbeWithin(runner, .018f, out' in source
                                               and 'ProbeWithin(runner, probe' in source
                                               and 'DistanceSquaredToSegment(surface[i], localA, localB, out float t)' in source
                                               and 'DistanceSquaredSegmentTriangle(a, b, p, q, r, out float t)' in source
                                               and 'runner.DeformationWeight = Mathf.MoveTowards(runner.DeformationWeight, near ? 1f : 0f,' in source
                                               and source.count('AddComponent<Cloth>()') == 1),
        'solver initializes before first touch': ('IdleFreedomRealMeters = .0005f' in source
                                                  and 'runner.DriverFreedom[n]\n                * IdleFreedomRealMeters * stationUnitInDriver' in source
                                                  and 'runner.DriverFreedom[n] * IdleFreedomRealMeters * runner.StationUnitInDriver' in source),
        'real local fingertips': ('VRHands.Left.Rig.IndexTip.position' in source
                                  and 'VRHands.Right.Rig.IndexTip.position' in source
                                  and 'VRHands.Left.WorldScale' in source
                                  and 'VRHands.Right.WorldScale' in source),
        'wrist through fingertip': ('VRHands.Left.Rig.Wrist.position' in source
                                   and 'VRHands.Right.Rig.Wrist.position' in source
                                   and 'WristRadiusRealMeters = PalmRadiusRealMeters + .020f' in source
                                   and 'float backLength = Vector3.Distance(wrist, palm)' in source
                                   and 'PlaceBoundHand(at++, ((long)(uint)peer << 2) | 1L, left, leftTip, leftWrist, peerScale)' in source),
        'whole capsule broadphase': ('expanded.Intersects(probeBounds)' in source
                                     and 'Vector3 span = probe.Tip.position - probe.Palm.position' in source),
        'triangle interior contact': ('OverlapsProbeBox(topLeft, topRight, bottomLeft, bottomRight' in source
                                      and 'TriangleWithin(localA, localB, topLeft, bottomLeft, topRight' in source
                                      and 'TriangleWithin(localA, localB, topRight, bottomLeft, bottomRight' in source),
        'tapered contact radius': ('Mathf.Lerp(probe.PalmSphere.radius, probe.TipSphere.radius, t)' in source
                                   and 'Mathf.Lerp(backRadius, tipRadius, t)' in source
                                   and 'DistanceSquaredToSegment(surface[i], localA, localB, out float t)' in source),
        'contact follows physical surface': ('runner.ContactSurface = simulated;' in source
                                             and 'Vector3[] surface = runner.ContactSurface' in source
                                             and 'DistanceSquaredToSegment(surface[i], localA, localB, out float t)' in source),
        'continuous contact episode': ('ContactHoldSeconds = .09f' in source
                                       and 'runner.ContactHold = rawContact ? ContactHoldSeconds' in source
                                       and 'near && runner.Contacting && runner.ContactHold > 0f' in source),
        'enchantress root backstop': ('if (_service == 3) BuildEnchantressRootSupport(runner)' in source
                                      and 'new Vector3(-.62f, y, -.20f)' in source
                                      and 'sphere.radius = .075f * runner.StationUnitInDriver;' in source),
        'narrow drape filtered physical displacement': ('SmoothNarrowDrape(runner, simulated)' in source
            and 'BlendedDelta(in runner.VisibleDriverMap[n], runner.VisualDelta)' in source),
        'merchant side banner absent': ('service == 1 ? 0 : service == 2 ? 2 : 1' in source
                                        and 'if (filters.Count == 0) return;' in source),
    }
    missing = [name for name, present in checks.items() if not present]
    if missing:
        raise AssertionError(', '.join(missing))


def validate_source():
    source = (ROOT / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceCloth.cs').read_text()
    source_contract(source)
    avatar_source = (ROOT / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs').read_text()
    remote_source = (ROOT / 'src/GloomhavenVR/Net/Remote/RemoteAvatar.cs').read_text()
    remote_contract = ('avatar.WristAnchorFor(l)' in avatar_source
                       and 'avatar.IndexTipAnchorFor(l)' in avatar_source
                       and 'avatar.WristAnchorFor(r)' in avatar_source
                       and 'avatar.IndexTipAnchorFor(r)' in avatar_source
                       and 'peerScale = avatar.AppliedScale' in avatar_source
                       and 'scale = avatar.AppliedScale' in avatar_source
                       and 'rig != null ? rig.Wrist : null' in remote_source
                       and 'rig != null ? rig.IndexTip : null' in remote_source)
    assert remote_contract, 'peer cloth anchors must come from the mirrored owner rig'
    for before, after in (
        ('avatar.WristAnchorFor(l)', 'avatar.PalmAnchorFor(l)'),
        ('avatar.IndexTipAnchorFor(r)', 'avatar.PalmAnchorFor(r)'),
        ('peerScale = avatar.AppliedScale', 'peerScale = 1f'),
    ):
        changed = avatar_source.replace(before, after, 1)
        assert not ('avatar.WristAnchorFor(l)' in changed
                    and 'avatar.IndexTipAnchorFor(r)' in changed
                    and 'peerScale = avatar.AppliedScale' in changed), 'remote anchor negative control escaped'
    figure_cloth = (ROOT / 'src/GloomhavenVR/Board/FigureGrab/FigureCloth.cs').read_text()
    assert 'next[v].maxDistance = m >= float.MaxValue ? m : m * factor;' in figure_cloth
    mutations = {
        'local-hand': ('if (VRHands.Left?.HasPose == true', 'if (VRHands.Left == null'),
        'remote-hand': ('TryGetTownClothHandProbes', 'DisabledRemoteHandProbe'),
        'head-mask': ('TryGetTownClothHead', 'DisabledRemoteHeadProbe'),
        'table': ('const int samples = DriverColumns', 'const int samples = 0'),
        'raycast': ('layer = IgnoreRaycastLayer', 'layer = 0'),
        'extra-frame-snapshot': ('Vector3[] simulated = runner.Cloth.vertices;',
                                 'Vector3[] simulated = runner.Cloth.vertices; var duplicate = runner.Cloth.vertices;'),
        'nested-scale': ('stationUnitInDriver = driver.InverseTransformVector',
                         'missingScale = driver.InverseTransformVector'),
        'scale-correct-gravity': ('Physics.gravity * runner.StationUnitInDriver',
                                  'Physics.gravity'),
        'bounded-envelope': ('MaximumFreedomRealMeters = .08f',
                             'MaximumFreedomRealMeters = .34f'),
        'enchantress-envelope': ('maximumFreedom = _service == 3 ? .065f : MaximumFreedomRealMeters',
                                 'maximumFreedom = _service == 3 ? .34f : MaximumFreedomRealMeters'),
        'overloose-bending': ('cloth.bendingStiffness = .94f', 'cloth.bendingStiffness = .40f'),
        'smooth-map': ('BlendedDelta(in runner.VisibleDriverMap[n]',
                       'simulated[runner.VisibleDriverVertex[n]] - runner.DriverRest[runner.VisibleDriverVertex[n]]'),
        'episode-zero': ('Array.Copy(runner.ContactSurface, runner.EpisodeOrigin, runner.EpisodeOrigin.Length)',
                         'Array.Copy(runner.DriverRest, runner.EpisodeOrigin, runner.EpisodeOrigin.Length)'),
        'idle-pin': ('SetFreedom(runner, 0f)', 'SetFreedom(runner, 1f)'),
        'component-rebuild': ('SetFreedom(runner, 0f);',
                              'SetFreedom(runner, 0f); runner.Cloth = runner.DriverRoot.AddComponent<Cloth>();'),
        'proximity-is-contact': ('AnyProbeWithin(runner, .018f, out',
                                 'AnyProbeWithin(runner, .16f, out'),
        'point-aabb-contact': ('DistanceSquaredToSegment(surface[i], localA, localB, out float t)',
                               'broadphase.SqrDistance(a)'),
        'wrist-coverage': ('VRHands.Left.Rig.Wrist.position', 'VRHands.Left.Rig.PalmCenter.position'),
        'whole-capsule-broadphase': ('expanded.Intersects(probeBounds)',
                                     'expanded.SqrDistance(probe.Palm.position) > 0f'),
        'triangle-interior': ('TriangleWithin(localA, localB, topLeft, bottomLeft, topRight',
                              'NoSurfaceTest(localA, localB, topLeft, bottomLeft, topRight'),
        'tapered-radius': ('Mathf.Lerp(probe.PalmSphere.radius, probe.TipSphere.radius, t)',
                            'probe.PalmSphere.radius'),
        'invisible-contact-physics': ('runner.DeformationWeight = Mathf.MoveTowards(runner.DeformationWeight, near ? 1f : 0f,',
                                      'runner.DeformationWeight = Mathf.MoveTowards(runner.DeformationWeight, false ? 1f : 0f,'),
        'attenuated-contact-physics': ('ContactPresentationSeconds = .045f',
            'ContactPresentationSeconds = .15f'),
        'short-touch-origin': ('if (runner.ContactSurface.Length == runner.EpisodeOrigin.Length)\n                Array.Copy(runner.ContactSurface, runner.EpisodeOrigin, runner.EpisodeOrigin.Length);',
                               'runner.EpisodeOrigin = runner.DriverRest;'),
        'fingertip': ('VRHands.Left.Rig.IndexTip.position', 'VRHands.Left.Rig.PalmCenter.forward'),
        'rest-surface-gate': ('Vector3[] surface = runner.ContactSurface',
                              'Vector3[] surface = runner.DriverRest'),
        'contact-drop-on-solver-crossing': ('ContactHoldSeconds = .09f', 'ContactHoldSeconds = 0f'),
        'missing-root-backstop': ('if (_service == 3) BuildEnchantressRootSupport(runner)',
                                  'if (_service == 1) BuildEnchantressRootSupport(runner)'),
        'missing-narrow-drape-filter': ('SmoothNarrowDrape(runner, simulated)',
                                         'DisabledNarrowDrapeFilter(runner, simulated)'),
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
    print('source_contract=PASS negative_controls=' + str(len(mutations) + 3))
    subprocess.run([sys.executable, str(ROOT / 'scripts/town_cloth_geometry_runtime.py')],
                   check=True)


def main():
    validate_source()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-only', action='store_true',
                        help='Run source and negative controls without starting Unity')
    parser.add_argument('--unity', type=Path,
                        default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--output-dir', type=Path,
                        default=ROOT / '.planning/debug/town-cloth-native')
    parser.add_argument('--bundle', type=Path, default=ROOT / 'prebuilt/ghvr-town.bundle')
    args = parser.parse_args()
    if args.source_only:
        return
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
    (project / 'Assets/Resources').mkdir(parents=True)
    shutil.copyfile(fixture / 'TownClothEvidence.shader', project / 'Assets/Resources/TownClothEvidence.shader')
    shutil.copyfile(fixture / 'TownClothEvidenceHand.shader', project / 'Assets/Resources/TownClothEvidenceHand.shader')
    production = (ROOT / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceCloth.cs').read_text()
    production = production.replace('namespace GloomhavenVR.WorldUI;\n',
                                    'namespace GloomhavenVR.WorldUI\n{\n', 1) + '\n}\n'
    (project / 'Assets/TownServiceCloth.cs').write_text(production)
    projection_guard = 'if (freedom > .12f)'
    if projection_guard not in production:
        raise AssertionError('negative controls must disable the real visible projection')
    dead_visible = production.replace('TownServiceCloth', 'TownServiceClothDead')
    dead_visible = dead_visible.replace('runner.DeformationWeight = Mathf.MoveTowards(runner.DeformationWeight, near ? 1f : 0f,',
                                        'runner.DeformationWeight = Mathf.MoveTowards(runner.DeformationWeight, false ? 1f : 0f,', 1)
    dead_visible = dead_visible.replace(projection_guard, 'if (false)', 1)
    (project / 'Assets/TownServiceClothDead.cs').write_text(dead_visible)
    contact_reset = production.replace('TownServiceCloth', 'TownServiceClothContactReset')
    contact_reset = contact_reset.replace(
        'runner.ContactSurface = simulated;',
        'runner.ContactSurface = simulated;\n'
        '        Array.Copy(simulated, runner.EpisodeOrigin, simulated.Length);', 1)
    contact_reset = contact_reset.replace(projection_guard, 'if (false)', 1)
    (project / 'Assets/TownServiceClothContactReset.cs').write_text(contact_reset)
    rest_gate = production.replace('TownServiceCloth', 'TownServiceClothRestGate')
    rest_gate = rest_gate.replace(
        'Vector3[] surface = runner.ContactSurface.Length == runner.DriverRest.Length\n'
        '            ? runner.ContactSurface : runner.DriverRest;',
        'Vector3[] surface = runner.DriverRest;', 1)
    (project / 'Assets/TownServiceClothRestGate.cs').write_text(rest_gate)
    no_contact_gate = production.replace('TownServiceCloth', 'TownServiceClothNoContactGate')
    no_contact_gate = no_contact_gate.replace(
        'bool rawContact = near && AnyProbeWithin(runner, .018f, out contactDistance);',
        'bool rawContact = false;', 1)
    (project / 'Assets/TownServiceClothNoContactGate.cs').write_text(no_contact_gate)
    (project / 'Packages/manifest.json').write_text(
        '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.cloth":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}')
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
