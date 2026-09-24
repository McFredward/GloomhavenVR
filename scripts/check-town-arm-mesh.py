#!/usr/bin/env python3
"""Check the actual Unity-skinned arm/torso triangles exported by ArmGeometry.

Run using Blender 4.2 --background --python this.py -- --input DIR --report JSON.
The input contains full selected surface triangles, not contact target proxies.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import numpy as np
from mathutils.bvhtree import BVHTree


def intersections(positions, faces):
    # Exact triangle tests follow conservative bounding-box rejection. No surface
    # samples or body capsules replace the imported geometry.
    torso_triangles = positions[faces[3]]
    low = torso_triangles.min(axis=1)
    high = torso_triangles.max(axis=1)
    pairs = []
    for side in (1, 2):
        arm_indices = np.unique(faces[side])
        arm_low = positions[arm_indices].min(axis=0)
        arm_high = positions[arm_indices].max(axis=0)
        candidates = np.flatnonzero(np.all(high >= arm_low, axis=1) & np.all(low <= arm_high, axis=1))
        if not len(candidates):
            continue
        torso_faces = faces[3][candidates]
        torso_indices, torso_inverse = np.unique(torso_faces, return_inverse=True)
        arm_indices, arm_inverse = np.unique(faces[side], return_inverse=True)
        torso = BVHTree.FromPolygons(positions[torso_indices], torso_inverse.reshape((-1, 3)), all_triangles=True, epsilon=0)
        arm = BVHTree.FromPolygons(positions[arm_indices], arm_inverse.reshape((-1, 3)), all_triangles=True, epsilon=0)
        pairs.extend((side, int(a), 3, int(candidates[b])) for a, b in arm.overlap(torso))
    left = BVHTree.FromPolygons(positions, faces[1], all_triangles=True, epsilon=0)
    right = BVHTree.FromPolygons(positions, faces[2], all_triangles=True, epsilon=0)
    pairs.extend((1, int(a), 2, int(b)) for a, b in left.overlap(right))
    return pairs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--report-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    reports = []
    for path in sorted(args.input.glob('service*-skin.bin')):
        with path.open('rb') as file:
            vertex_count, face_count, frame_count = struct.unpack('<iii', file.read(12))
            records = np.frombuffer(file.read(face_count * 13), dtype=[('label', 'u1'), ('face', '<i4', (3,))])
            faces = {i: records['face'][records['label'] == i] for i in (1, 2, 3)}
            seams = np.loadtxt(path.with_name(path.stem.replace('-skin', '-seams') + '.csv'), delimiter=',', ndmin=2)
            assert len(seams) >= 20, 'Actual cuff/skin seam coverage missing'
            seam_cloth, seam_skin = seams[:, 0].astype(int), seams[:, 1].astype(int)
            report = {'file': path.name, 'vertices': vertex_count, 'faces': {str(k): len(v) for k, v in faces.items()},
                      'frames': frame_count, 'intersecting_frames': 0, 'maximum_pairs': 0, 'worst': None, 'seam_pairs': len(seams), 'maximum_seam_growth': 0.}
            for frame in range(frame_count):
                clock, attention = struct.unpack('<ff', file.read(8))
                positions = np.frombuffer(file.read(vertex_count * 12), dtype='<f4').reshape((-1, 3))
                growth = np.linalg.norm(positions[seam_cloth] - positions[seam_skin], axis=1) - seams[:, 2]
                report['maximum_seam_growth'] = max(report['maximum_seam_growth'], float(growth.max()))
                pairs = intersections(positions, faces)
                if frame == 0:
                    # Deliberately drive the real left forearm into its own torso.
                    # A marker-only/disabled detector must not pass this control.
                    injected = False
                    left = np.unique(faces[1])
                    for shift in (-.12, -.24, -.36):
                        corrupted = positions.copy()
                        corrupted[left, 0] += shift
                        if any(pair[2] == 3 for pair in intersections(corrupted, faces)):
                            injected = True
                            break
                    assert injected, ('Injected actual-skin penetration escaped detection', path)
                    report['negative_control_detected'] = True
                    separated = positions.copy()
                    separated[seam_skin[0], 1] += .10
                    injected_growth = np.linalg.norm(separated[seam_cloth] - separated[seam_skin], axis=1) - seams[:, 2]
                    assert injected_growth.max() > .012, 'Injected detached wrist escaped detection'
                    report['seam_negative_control_detected'] = True
                if pairs:
                    report['intersecting_frames'] += 1
                    if len(pairs) > report['maximum_pairs']:
                        report['maximum_pairs'] = len(pairs)
                        report['worst'] = {'frame': frame, 'clock': clock, 'attention': attention, 'pairs': pairs[:8],
                            'arm_centres': [positions[faces[s][a]].mean(axis=0).tolist() for s,a,other,b in pairs[:8]]}
            assert not file.read(1), 'Unexpected trailing geometry data'
            reports.append(report)
            print('TOWN_ARM_MESH', json.dumps(report), flush=True)
    assert len(reports) == 3, 'Every resident must be sampled'
    args.report.write_text(json.dumps(reports, indent=2) + '\n')
    if not args.report_only and any(row['intersecting_frames'] or row['maximum_seam_growth'] > .012 for row in reports):
        raise SystemExit('Actual skin has an arm/torso or opposite-arm intersection, or separated wrist seam')


if __name__ == '__main__':
    main()
