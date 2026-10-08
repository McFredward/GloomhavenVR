#!/usr/bin/env python3
"""Audit the full immutable PCG catalog and certified architecture derivatives.

This verifies preparation/coverage and exact geometry safety. It is not an FPS
benchmark or headset appearance acceptance. Native files are read-only.
"""
import argparse
import collections
import copy
import gzip
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools/environment-mesh'))
from geometry import boundaries, floor_certificate, parse
from roles import architectural_ornament, classify


def check_entry(entry, certificate):
    role, reasons = classify(entry['signature']['name'], certificate['uses'])
    assert entry['role'] == role and certificate['roleReasons'] == reasons, 'all original uses certify the same role'
    assert entry['ornament'] == architectural_ornament(entry['signature']['name'], certificate['uses']), 'closed separate ornament identity'
    evidence = {'role': role, 'reasons': reasons, 'uses': certificate['uses'], 'floor': certificate['floor']}
    digest = hashlib.sha256(json.dumps(evidence, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    assert digest == entry['roleEvidenceSha256'], 'complete original role evidence hash'
    assert certificate['signature'] == entry['signature'] and certificate['sources'] == entry['sources'], 'exact original source identity'
    assert certificate['variants'] == entry['variants'], 'exact certified derivative set'


def projected_outer_slots(vertices):
    """Independently derive supporting hull edges with gift wrapping.

    Preparation uses sorted monotone chains. This checker uses a different hull
    construction and checks every original vertex slot against its supporting
    edges, including collinear edge points and duplicated attribute slots.
    """
    points = sorted({(point[0], point[2]) for point in vertices})
    if len(points) < 3:
        return set(range(len(vertices)))
    hull = [points[0]]
    while True:
        first = hull[-1]
        second = next(point for point in points if point != first)
        for point in points:
            dx, dz = second[0] - first[0], second[1] - first[1]
            px, pz = point[0] - first[0], point[1] - first[1]
            cross = dx * pz - dz * px
            if cross < 0 or cross == 0 and px * px + pz * pz > dx * dx + dz * dz:
                second = point
        if second == hull[0]:
            break
        assert second not in hull, 'projected supporting hull closes without a cycle'
        hull.append(second)
    if len(hull) < 3:
        return set(range(len(vertices)))
    span = max(max(point[axis] for point in points) - min(point[axis] for point in points)
               for axis in (0, 1))
    tolerance = max(span * 1e-6, 1e-9)
    slots = set()
    for index, point in enumerate(vertices):
        for first, second in zip(hull, hull[1:] + hull[:1]):
            dx, dz = second[0] - first[0], second[1] - first[1]
            px, pz = point[0] - first[0], point[2] - first[1]
            length = math.hypot(dx, dz)
            distance = abs(dx * pz - dz * px) / length
            along = (px * dx + pz * dz) / length
            if distance <= tolerance and -tolerance <= along <= length + tolerance:
                slots.add(index)
                break
    return slots


def check_geometry(entry, original, reduced, morph=False):
    source, source_submeshes, _, _ = parse(original)
    positions, submeshes, _, _ = parse(reduced)
    assert len(source) == len(positions), 'same-index morph retains every original vertex slot'
    fixed = boundaries(source, source_submeshes)
    assert all(positions[index] == point for index, point in enumerate(source) if point in fixed), 'every native open seam remains fixed'
    for axis in range(3):
        assert min(point[axis] for point in source) == min(point[axis] for point in positions), 'actual lower 3D geometry bound'
        assert max(point[axis] for point in source) == max(point[axis] for point in positions), 'actual upper 3D geometry bound'
    if entry['role'] != 'floor':
        return
    assert all(positions[index] == source[index] for index in projected_outer_slots(source)), 'projected floor hull-edge slots stay fixed'
    valid, facts = floor_certificate(source, positions, source_submeshes, submeshes)
    assert valid, 'floor retains sampled footprint/holes/top height: ' + str(facts)
    if morph:
        for progress in (.25, .5, .75):
            intermediate = [tuple(a[axis] + (b[axis] - a[axis]) * progress for axis in range(3))
                            for a, b in zip(source, positions)]
            valid, facts = floor_certificate(source, intermediate, source_submeshes, source_submeshes)
            assert valid, 'intermediate floor morph retains sampled surface/holes/height: ' + str(facts)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--morph', action='store_true', help='Also certify every reduced floor at three intermediate positions')
    args = parser.parse_args()
    root = args.source_root.resolve()
    prepared = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    entries = json.loads((prepared / 'index.json').read_text())['entries']
    catalog = json.loads(gzip.decompress((root / 'tools/environment-mesh/catalog.json.gz').read_bytes()))
    assert catalog['completePcgCensus'] is True, 'partial theme extraction cannot claim all-game coverage'
    base = root / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'
    originals = set((base / 'pcg_databases_assets_assets/pcg').glob('*.bundle'))
    actual = set()
    for bundle in catalog['bundles']:
        path = base / bundle['path']
        assert path in originals and path not in actual, 'complete original theme/DLC source set'
        actual.add(path)
        assert hashlib.sha256(path.read_bytes()).hexdigest() == bundle['sha256'], 'read-only native theme source hash'
    assert actual == originals, 'no PCG theme or DLC bundle omitted'
    uses = collections.defaultdict(list)
    for row in catalog['meshes']:
        uses[row['key']].extend(row['uses'])
    certified = {row['key']: row for row in catalog['certified']}
    assert len(certified) == len(entries), 'unique complete certified source set'
    role_counts = collections.Counter(); reductions = collections.defaultdict(lambda: [0, 0, 0])
    floor_variants = 0; total_variants = 0
    for entry in entries:
        certificate = certified[entry['key']]
        assert certificate['uses'] == uses[entry['key']], 'every native family use is included, including unsafe reuse'
        check_entry(entry, certificate)
        role_counts[entry['role']] += 1
        original = (prepared / next(variant['file'] for variant in entry['variants'] if variant['tier'] == 100)).read_bytes()
        for variant in entry['variants']:
            total_variants += 1
            if variant['tier'] == 100:
                continue
            reduced = (prepared / variant['file']).read_bytes()
            check_geometry(entry, original, reduced, args.morph)
            if entry['role'] == 'floor': floor_variants += 1
            if variant['tier'] == 0:
                group = reductions[entry['role']]
                group[0] += sum(entry['signature']['indices']) // 3
                group[1] += sum(map(len, parse(reduced)[1]))
                group[2] += 1
    ornaments = json.loads((root / 'tools/environment-mesh/ornaments.json').read_text())['entries']
    assert {row['key'] for row in ornaments} == {entry['key'] for entry in entries if entry['ornament']}, 'complete closed ornament catalog'
    # Causal controls use actual captured originals. A role forged for a real
    # protected source, a hidden unsafe reuse or a disturbed room seam must fail.
    controls = 0
    protected = next(entry for entry in entries if entry['role'] == 'none')
    altered = copy.deepcopy(protected); altered['role'] = 'structure'
    try: check_entry(altered, certified[altered['key']])
    except AssertionError as error:
        assert 'all original uses' in str(error); controls += 1
    else: raise AssertionError('protected-original role-forgery control escaped')
    safe = next(entry for entry in entries if entry['role'] == 'floor')
    bad = copy.deepcopy(certified[safe['key']])
    bad['uses'][0]['scripts'].append('BreakableObject')
    try: check_entry(safe, bad)
    except AssertionError as error:
        assert 'all original uses' in str(error); controls += 1
    else: raise AssertionError('interactive reuse control escaped')
    seam_entry = next(entry for entry in entries if entry['role'] == 'floor'
                      and any(variant['tier'] == 0 for variant in entry['variants']))
    source_variant = next(variant for variant in seam_entry['variants'] if variant['tier'] == 100)
    coarse_variant = next(variant for variant in seam_entry['variants'] if variant['tier'] == 0)
    original = (prepared / source_variant['file']).read_bytes()
    reduced = bytearray((prepared / coarse_variant['file']).read_bytes())
    positions, submeshes, offset, _ = parse(original)
    fixed = boundaries(positions, submeshes)
    if not fixed:
        fixed = {min(positions)}
    index = next(index for index, point in enumerate(positions) if point in fixed)
    struct.pack_into('<f', reduced, offset + index * 12, positions[index][0] + .25)
    try: check_geometry(seam_entry, original, bytes(reduced))
    except AssertionError: controls += 1
    else: raise AssertionError('actual original seam displacement control escaped')
    # Catch the former closed-floor regression specifically: an actual diagonal
    # hull-edge slot missed by both open edges and XZ axis extrema must be pinned.
    rim_control = None
    for entry in entries:
        if entry['role'] != 'floor' or not any(variant['tier'] != 100 for variant in entry['variants']):
            continue
        source_file = next(variant['file'] for variant in entry['variants'] if variant['tier'] == 100)
        original = (prepared / source_file).read_bytes()
        positions, submeshes, offset, _ = parse(original)
        fixed = boundaries(positions, submeshes)
        low = [min(point[axis] for point in positions) for axis in range(3)]
        high = [max(point[axis] for point in positions) for axis in range(3)]
        missed = [index for index in projected_outer_slots(positions)
                  if positions[index] not in fixed
                  and all(positions[index][axis] not in (low[axis], high[axis]) for axis in (0, 2))]
        if missed:
            rim_control = (entry, original, positions, offset, missed[0])
            break
    assert rim_control is not None, 'actual closed diagonal floor rim control exists'
    entry, original, positions, offset, index = rim_control
    altered = bytearray(original)
    # The chosen XZ slot is strictly inside the axis bounds, so this small
    # displacement cannot trigger the unrelated bounds or open-seam guards.
    low_x, high_x = min(point[0] for point in positions), max(point[0] for point in positions)
    shift = min(positions[index][0] - low_x, high_x - positions[index][0]) * .01
    struct.pack_into('<f', altered, offset + index * 12, positions[index][0] + shift)
    try: check_geometry(entry, original, bytes(altered))
    except AssertionError as error:
        assert 'hull-edge slots' in str(error); controls += 1
    else: raise AssertionError('actual original diagonal hull-edge displacement escaped')
    facts = {'format': 1, 'sourceBundles': len(actual), 'nativeMeshOccurrences': len(catalog['meshes']),
             'certifiedOriginals': len(entries), 'roles': dict(role_counts), 'variants': total_variants,
             'floorVariantsCertified': floor_variants, 'strongTierTriangles': dict(reductions),
             'ornaments': len(ornaments), 'causalControls': controls, 'morph': args.morph,
             'floorOuterBoundaryProof': 'unchanged original slots along the projected convex hull, including collinear points',
             'floorInteriorProof': '18x18 coverage/holes/top-height samples; no exact arbitrary small-feature union claim',
             'sourceSha256': {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                              for path in (Path(__file__), root / 'tools/environment-mesh/geometry.py',
                                           root / 'tools/environment-mesh/roles.py', prepared / 'index.json',
                                           root / 'tools/environment-mesh/catalog.json.gz')}}
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(facts, indent=2) + '\n')
    print('PASS environment architecture catalog: ' + json.dumps({key: value for key, value in facts.items() if key != 'sourceSha256'}))


if __name__ == '__main__': main()
