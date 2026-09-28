#!/usr/bin/env python3
"""Check the imported merchant's skinned visitor pose against his coat.

Run with Blender ``--python-exit-code 1`` after
``check-town-activity.py --anatomy-focus``. The input is
the real Unity-skinned LOD0 mesh at 90 Hz, including the authored arm pose and
costume shapes. Marker positions select the palmar skin patch; they are never
used as a substitute for surface-to-surface contact.
"""

import argparse
import csv
import json
import struct
import sys
from pathlib import Path

import numpy as np
from mathutils.bvhtree import BVHTree


def tree(vertices, triangles):
    indices, remap = np.unique(triangles, return_inverse=True)
    return BVHTree.FromPolygons(vertices[indices], remap.reshape((-1, 3)), all_triangles=True)


def patch(vertices, faces, hand_indices, palm, inward):
    triangles = vertices[faces]
    face_normals = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
    normals = np.zeros_like(vertices)
    for corner in range(3):
        np.add.at(normals, faces[:, corner], face_normals)
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-8)
    distance = np.linalg.norm(vertices[hand_indices] - palm, axis=1)
    facing = normals[hand_indices] @ inward
    return hand_indices[(distance < .035) & (facing > .5)]


def contact(vertices, coat, indices):
    signed = []
    for index in indices:
        point = vertices[index]
        location, normal, _, _ = coat.find_nearest(tuple(point))
        signed.append(float(np.dot(point - np.asarray(location), np.asarray(normal)) * 1000))
    signed = np.asarray(signed)
    exterior = (signed >= 0) & (signed <= 5)
    return float(np.median(signed)), float(np.mean(exterior)), signed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--anatomy', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    parser.add_argument('--report-only', action='store_true', help='Write diagnostics without enforcing release gates')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:])
    stem = args.anatomy / 'service1'
    with (stem.with_name('service1-segments.csv')).open(newline='') as stream:
        segments = list(csv.DictReader(stream))
    required_segments = {'visit_approach', 'visit_withdrawal', 'card_offering_and_return',
                         'coin_author_handover', 'early_visit_approach',
                         'early_visit_withdrawal', 'attentive_phase_sweep'}
    if {entry['state'] for entry in segments} != required_segments:
        raise ValueError('Merchant imported-skin sweep is missing a required transition')
    with (stem.with_name('service1-hand-markers.csv')).open(newline='') as stream:
        markers = {(int(row['frame']), row['side']): row for row in csv.DictReader(stream)}
    with (stem.with_name('service1-digit-markers.csv')).open(newline='') as stream:
        digits = {(int(row['frame']), row['side'], row['name']): row for row in csv.DictReader(stream)}
    facts_path = stem.with_name('service1-phase-facts.csv')
    if facts_path.exists():
        with facts_path.open(newline='') as stream:
            facts = {int(row['frame']): row for row in csv.DictReader(stream)}
    else:
        facts = {}
    hands = {'L': [], 'R': []}
    with (stem.with_name('service1-hands.csv')).open(newline='') as stream:
        for side, index in csv.reader(stream):
            hands['L' if side == '1' else 'R'].append(int(index))
    hands = {side: np.asarray(indices) for side, indices in hands.items()}
    with (stem.with_name('service1-skin.bin')).open('rb') as stream:
        vertices_count, triangle_count, frames = struct.unpack('<iii', stream.read(12))
        records = np.frombuffer(stream.read(triangle_count * 13),
                                dtype=[('label', 'u1'), ('tri', '<i4', (3,))])
        payload = np.frombuffer(stream.read(), dtype='u1')
    stride = 8 + vertices_count * 12
    if len(payload) != frames * stride:
        raise ValueError('Incomplete Unity skin export')
    triangles = {label: records['tri'][records['label'] == label] for label in (1, 2, 3)}
    if any(len(value) < 100 for value in triangles.values()):
        raise ValueError('Missing imported arm or coat triangles')
    if int(segments[-1]['end']) + 1 != frames:
        raise ValueError('Segment list does not cover the imported sequence')

    phase_segment = next((entry for entry in segments if entry['state'] == 'attentive_phase_sweep'), None)
    phase_start = int(phase_segment['start']) if phase_segment else frames
    phase_end = int(phase_segment['end']) if phase_segment else -1
    counts = np.zeros((frames, 3), dtype=np.int32)
    clocks = np.zeros(frames)
    contacts = {}
    phase_contacts = {'L': [], 'R': []}
    for frame in range(frames):
        clocks[frame] = struct.unpack_from('<f', payload, frame * stride)[0]
        offset = frame * stride + 8
        vertices = np.frombuffer(payload[offset:offset + vertices_count * 12],
                                 dtype='<f4').reshape((-1, 3))
        left = tree(vertices, triangles[1])
        right = tree(vertices, triangles[2])
        coat = tree(vertices, triangles[3])
        counts[frame] = [len(left.overlap(coat)), len(right.overlap(coat)), len(left.overlap(right))]
        if frame == int(segments[0]['end']):
            for side in ('L', 'R'):
                row = markers[(frame, side)]
                palm = np.array([float(row['palm' + axis]) for axis in 'XYZ'])
                inward = np.array([float(row['normal' + axis]) for axis in 'XYZ'])
                indices = patch(vertices, records['tri'], hands[side], palm, inward)
                if len(indices) < 50:
                    raise ValueError(f'Unable to resolve {side} palmar skin patch')
                median, coverage, signed = contact(vertices, coat, indices)
                # The historical marker-only fixture passed even when the hand
                # floated. Push the actual selected skin 15 mm away from the coat
                # while leaving markers untouched; the contact gate must fail.
                hovered = vertices.copy()
                hovered[indices] -= inward * .015
                hover_median, hover_coverage, _ = contact(hovered, coat, indices)
                if hover_coverage >= .60 or hover_median <= 5:
                    raise AssertionError(f'{side} hover negative control did not fail')
                contacts[side] = {'vertices': len(indices), 'median_gap_mm': median,
                                  'p05_gap_mm': float(np.percentile(signed, 5)),
                                  'p90_gap_mm': float(np.percentile(signed, 90)),
                                  'exterior_within_5mm': coverage,
                                  'inside_fraction': float(np.mean(signed < 0)),
                                  'inside_beyond_1mm': int(np.sum(signed < -1)),
                                  'hover_control': {'median_gap_mm': hover_median,
                                                    'exterior_within_5mm': hover_coverage}}
                pad_gaps = {}
                for digit in ('Index', 'Middle', 'Ring', 'Little', 'Thumb'):
                    row = digits[(frame, side, digit + 'Pad.' + side)]
                    point = np.array([float(row[axis]) for axis in 'xyz'])
                    location, normal, _, _ = coat.find_nearest(tuple(point))
                    pad_gaps[digit] = float(np.dot(point - np.asarray(location), np.asarray(normal)) * 1000)
                contacts[side]['pad_gaps_mm'] = pad_gaps
        if phase_start <= frame <= phase_end and frame in facts:
            fact = facts[frame]
            if fact['merchantCanAttend'] == '1' and float(fact['coinGrip']) < .001:
                for side in ('L', 'R'):
                    row = markers[(frame, side)]
                    palm = np.array([float(row['palm' + axis]) for axis in 'XYZ'])
                    inward = np.array([float(row['normal' + axis]) for axis in 'XYZ'])
                    indices = patch(vertices, records['tri'], hands[side], palm, inward)
                    if len(indices) < 50:
                        raise ValueError(f'Unable to resolve {side} palm at WorkClock {fact["workClock"]}')
                    median, coverage, signed = contact(vertices, coat, indices)
                    phase_contacts[side].append({
                        'frame': frame, 'work_clock': float(fact['workClock']),
                        'median_gap_mm': median, 'exterior_within_5mm': coverage,
                        'inside_fraction': float(np.mean(signed < 0)),
                        'inside_beyond_1mm': int(np.sum(signed < -1)),
                    })

    summary = {'frames': frames, 'endpoint': int(segments[0]['end']), 'contact': contacts,
               'segments': {}, 'maximum_pairs': counts.max(axis=0).tolist()}
    for segment in segments:
        selection = counts[int(segment['start']):int(segment['end']) + 1]
        worst = int(np.argmax(selection.sum(axis=1))) + int(segment['start'])
        summary['segments'][segment['state']] = {
            'frames': len(selection),
            'start_pairs': selection[0].tolist(),
            'end_pairs': selection[-1].tolist(),
            'intersection_frames': np.count_nonzero(selection, axis=0).tolist(),
            'maximum_pairs': selection.max(axis=0).tolist(),
            'worst': {'frame': worst, 'work_clock': float(clocks[worst]),
                      'pairs': counts[worst].tolist()},
        }
    summary['legal_phase_contact'] = {}
    if set(contacts) != {'L', 'R'}:
        raise AssertionError('Both imported palmar skin patches must be checked')
    for side, samples in phase_contacts.items():
        if samples:
            summary['legal_phase_contact'][side] = {
                'samples': len(samples),
                'minimum_exterior_within_5mm': min(samples, key=lambda row: row['exterior_within_5mm']),
                'maximum_median_gap': max(samples, key=lambda row: row['median_gap_mm']),
                'maximum_inside_fraction': max(samples, key=lambda row: row['inside_fraction']),
            }
    allowed = np.ones(frames, dtype=bool)
    if phase_segment and facts:
        for frame in range(phase_start, phase_end + 1):
            fact = facts[frame]
            allowed[frame] = fact['merchantCanAttend'] == '1' and float(fact['coinGrip']) < .001
        legal = counts[phase_start:phase_end + 1][allowed[phase_start:phase_end + 1]]
        summary['legal_phase_intersections'] = {
            'samples': len(legal),
            'intersection_frames': np.count_nonzero(legal, axis=0).tolist(),
            'maximum_pairs': legal.max(axis=0).tolist() if len(legal) else [0, 0, 0],
        }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(summary, indent=2) + '\n')
    print('TOWN_MERCHANT_CONTACT', json.dumps(summary))
    if args.report_only:
        return
    if np.any(counts[allowed]):
        raise AssertionError('Imported merchant arms intersect the coat or each other')
    for side, result in contacts.items():
        if (result['median_gap_mm'] > 5 or result['exterior_within_5mm'] < .60
                or result['inside_beyond_1mm'] > 0 or result['inside_fraction'] > .02):
            raise AssertionError(f'{side} palmar skin still floats above the coat')
    if not facts or any(len(samples) < 20 for samples in phase_contacts.values()):
        raise AssertionError('Legal merchant Idle phase coverage is missing')
    for side, samples in phase_contacts.items():
        for sample in samples:
            if (sample['median_gap_mm'] > 5 or sample['exterior_within_5mm'] < .60
                    or sample['inside_beyond_1mm'] > 0 or sample['inside_fraction'] > .02):
                raise AssertionError(f'{side} loses palmar contact at WorkClock {sample["work_clock"]}')


if __name__ == '__main__':
    main()
