#!/usr/bin/env python3
"""Exercise the source-bound map recenter in real Unity, including tracked-origin heights.

The headset boundary is a camera's measured local transform, not a fabricated seat
formula. Actual RecenterMap and YawOnly method bodies and the complete MapRoomSeat
file are copied verbatim into each compiled case. Game discovery/log endpoints are
explicit fixture boundaries. This cannot certify a headset's comfort or runtime
tracking-origin policy; it verifies the resulting source-owned world geometry.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    if source.count(signature) != 1:
        raise RuntimeError('Source binding drift: ' + signature)
    start = source.index(signature)
    opening = source.index('{', start)
    depth, at = 1, opening + 1
    while depth:
        depth += (source[at] == '{') - (source[at] == '}')
        at += 1
    return source[start:at]


def bind(root):
    paths = ('Rig/VRRigDriver.MapRig.cs', 'Rig/VRRigDriver.WorldTilt.cs',
             'WorldUI/MapRoom/MapRoomSeat.cs', 'Rig/VRRigDriver.Recenter.cs', 'Rig/VRRigDriver.cs')
    raw = {path: (root / 'src/GloomhavenVR' / path).read_text() for path in paths}
    map_rig = raw[paths[0]]
    # Both normal entry/rebuild and the deliberate map recenter use this method;
    # no new frame-driven head-height owner is introduced by the repair.
    build = method(map_rig, '    private void BuildMapRig()')
    assert '_pendingRecenter = true;' in build
    assert '_mapSeat = seat;' in build
    assert 'RecenterMap();' in method(raw[paths[3]], '    internal void Recenter()')
    assert 'if (_pendingRecenter && _camera != null' in raw[paths[4]]
    assert map_rig.count('EyeClearanceLiftMeters(') == 1
    bound = {
        'MapRoomSeat.cs': raw[paths[2]],
        'RecenterMap.cs': ('using UnityEngine;using GloomhavenVR.Core;using GloomhavenVR.WorldUI.MapRoom;'
            'namespace GloomhavenVR.Rig {internal sealed partial class VRRigDriver {'
            + method(map_rig, '    private void RecenterMap()')
            + method(raw[paths[1]], '    internal static Quaternion YawOnly(') + '}}')}
    return bound, {path: hashlib.sha256(text.encode()).hexdigest() for path, text in raw.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc639/visit/map')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir))
    bound, hashes = bind(args.source_root)
    (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
    variants = (
        ('production', '', '', ''),
        ('raw-eye-origin', 'float liftMeters = MapRoomSeat.EyeClearanceLiftMeters(headLocal.y, tableAboveRigFloorMeters);',
         'float liftMeters = 0f;', 'low origin obtains the minimum map overview'),
        ('double-clearance', 'liftMeters * scale', 'liftMeters * scale * 2f',
         'lift only to the minimum without raising the view farther'),
        ('lateral-tracking-retained', 'yaw * (flat * scale)', 'yaw * (Vector3.zero * scale)',
         'entry absorbs measured tracking XZ without moving the table'))
    fixture = ROOT / 'scripts/npc639-visit-runtime'
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, before, after, expected in variants:
        case = run / name
        production = case / 'production'
        production.mkdir(parents=True)
        for filename, text in bound.items():
            if filename == 'RecenterMap.cs' and before:
                if text.count(before) != 1:
                    raise RuntimeError('Control binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / filename).write_text(text)
        shutil.copyfile(fixture / 'Visit.csproj', case / 'Visit.csproj')
        assembly = 'Visit_' + name.replace('-', '_')
        compiled = subprocess.run([dotnet, 'build', str(case / 'Visit.csproj'), '-c', 'Release',
            '--nologo', '-v', 'quiet', '-p:CaseName=' + assembly,
            '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(unity.parent / 'Data/Managed')], capture_output=True, text=True)
        (case / 'build.log').write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode:
            raise SystemExit('Compile error is not a control pass:\n' + compiled.stdout + compiled.stderr)
        manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    manifest_path = run / 'manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    result = subprocess.run([str(unity), '-batchmode', '-nographics', '-projectPath', str(project),
        '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(manifest_path),
        '-logFile', str(run / 'unity.log')], timeout=240)
    receipt = Path(manifest['result'])
    if receipt.is_file():
        print(receipt.read_text(), end='')
    if result.returncode or not receipt.is_file():
        raise SystemExit('FAIL Unity; see ' + str(run))
    # Preserve the bound source, compiled inputs and receipts, remove only the
    # disposable editor project. No hardware logs or shared checkout are deleted.
    shutil.rmtree(project)
    print('PASS: actual map recenter and three causal controls; ' + str(run))


if __name__ == '__main__':
    main()
