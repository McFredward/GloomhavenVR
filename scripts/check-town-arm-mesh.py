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
            report = {'file': path.name, 'vertices': vertex_count, 'faces': {str(k): len(v) for k, v in faces.items()},
                      'frames': frame_count, 'intersecting_frames': 0, 'maximum_pairs': 0, 'worst': None}
            for frame in range(frame_count):
                clock, attention = struct.unpack('<ff', file.read(8))
                positions = np.frombuffer(file.read(vertex_count * 12), dtype='<f4').reshape((-1, 3))
                torso = BVHTree.FromPolygons(positions, faces[3], all_triangles=True, epsilon=0)
                pairs = []
                for side in (1, 2):
                    arm = BVHTree.FromPolygons(positions, faces[side], all_triangles=True, epsilon=0)
                    for a, b in arm.overlap(torso):
                        pairs.append((side, int(a), int(b)))
                if pairs:
                    report['intersecting_frames'] += 1
                    if len(pairs) > report['maximum_pairs']:
                        report['maximum_pairs'] = len(pairs)
                        report['worst'] = {'frame': frame, 'clock': clock, 'attention': attention, 'pairs': pairs[:8],
                            'arm_centres': [positions[faces[s][a]].mean(axis=0).tolist() for s,a,b in pairs[:8]]}
            assert not file.read(1), 'Unexpected trailing geometry data'
            reports.append(report)
            print('TOWN_ARM_MESH', json.dumps(report), flush=True)
    assert len(reports) == 3, 'Every resident must be sampled'
    args.report.write_text(json.dumps(reports, indent=2) + '\n')
    if not args.report_only and any(row['intersecting_frames'] for row in reports):
        raise SystemExit('Actual skinned arm intersects actual torso surface')


if __name__ == '__main__':
    main()
