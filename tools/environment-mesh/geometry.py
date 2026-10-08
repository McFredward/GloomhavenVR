"""Geometry certificates for private, reversible environment derivatives."""
import collections
import math
import struct


def parse(data):
    if data[:5] != b'GHEM1':
        raise ValueError('Unsupported environment geometry stream')
    offset = 5
    length = struct.unpack_from('<i', data, offset)[0]
    offset += 4 + length + 24
    count, width = struct.unpack_from('<ii', data, offset)
    offset += 8
    position = offset
    vertices = [struct.unpack_from('<fff', data, offset + 12 * index) for index in range(count)]
    offset += count * width * 4
    for _ in range(11):
        count, width = struct.unpack_from('<ii', data, offset)
        offset += 8 + count * width * 4
    prefix = offset
    count = struct.unpack_from('<i', data, offset)[0]
    offset += 4
    submeshes = []
    for _ in range(count):
        count = struct.unpack_from('<i', data, offset)[0]
        offset += 4
        indices = struct.unpack_from('<' + 'i' * count, data, offset)
        offset += 4 * count
        submeshes.append(list(zip(indices[::3], indices[1::3], indices[2::3])))
    if offset != len(data):
        raise ValueError('Unexpected geometry trailing bytes')
    return vertices, submeshes, position, prefix


def boundaries(vertices, submeshes):
    edges = collections.Counter()
    for triangles in submeshes:
        for a, b, c in triangles:
            for i, j in ((a, b), (b, c), (c, a)):
                first, second = vertices[i], vertices[j]
                if first != second:
                    edges[tuple(sorted((first, second)))] += 1
    return {point for edge, count in edges.items() if count == 1 for point in edge}


def footprint(vertices, submeshes, samples=18, extent=None):
    """Highest original surface on a fixed XZ lattice, including interior holes.

    This is a bounded offline certificate, not a replacement collision surface.
    Samples also retain the bottom/top height range of each covered cell.
    """
    if extent is None:
        extent = [(min(point[axis] for point in vertices), max(point[axis] for point in vertices))
                  for axis in (0, 2)]
    points = [(extent[0][0] + (x + .5) / samples * (extent[0][1] - extent[0][0]),
               extent[1][0] + (z + .5) / samples * (extent[1][1] - extent[1][0]))
              for z in range(samples) for x in range(samples)]
    heights = [[math.inf, -math.inf] for _ in points]
    # Most source floors contain hundreds of triangles, not hundreds of thousands;
    # this bounded lattice keeps the preparation independent of third-party libs.
    for triangles in submeshes:
        for a, b, c in triangles:
            first, second, third = vertices[a], vertices[b], vertices[c]
            denominator = ((second[2] - third[2]) * (first[0] - third[0])
                           + (third[0] - second[0]) * (first[2] - third[2]))
            if abs(denominator) < 1e-12:
                continue
            low_x, high_x = min(first[0], second[0], third[0]), max(first[0], second[0], third[0])
            low_z, high_z = min(first[2], second[2], third[2]), max(first[2], second[2], third[2])
            for index, (x, z) in enumerate(points):
                if x < low_x - 1e-8 or x > high_x + 1e-8 or z < low_z - 1e-8 or z > high_z + 1e-8:
                    continue
                u = ((second[2] - third[2]) * (x - third[0])
                     + (third[0] - second[0]) * (z - third[2])) / denominator
                v = ((third[2] - first[2]) * (x - third[0])
                     + (first[0] - third[0]) * (z - third[2])) / denominator
                w = 1 - u - v
                if min(u, v, w) >= -1e-7:
                    height = u * first[1] + v * second[1] + w * third[1]
                    heights[index][0] = min(heights[index][0], height)
                    heights[index][1] = max(heights[index][1], height)
    return heights, extent


def floor_certificate(source, reduced, source_submeshes, reduced_submeshes):
    original, extent = footprint(source, source_submeshes)
    current, _ = footprint(reduced, reduced_submeshes, extent=extent)
    covered = [index for index, height in enumerate(original) if math.isfinite(height[1])]
    if not covered:
        return False, {'sourceFootprintSamples': 0}
    retained = sum(math.isfinite(current[index][1]) for index in covered)
    introduced = sum(math.isfinite(new[1]) and not math.isfinite(old[1])
                     for old, new in zip(original, current))
    height_extent = max(point[1] for point in source) - min(point[1] for point in source)
    maximum_shift = max((abs(current[index][1] - original[index][1])
                         for index in covered if math.isfinite(current[index][1])), default=math.inf)
    facts = {'sourceFootprintSamples': len(covered), 'retainedFootprintSamples': retained,
             'introducedFootprintSamples': introduced, 'maximumTopHeightShift': maximum_shift,
             'sourceHeightExtent': height_extent, 'footprintGrid': 18}
    # No sampled walkable footprint may disappear and no original hole may fill.
    # Relief can become coarse, but surfaces must remain close to native heights.
    return retained == len(covered) and introduced == 0 and maximum_shift <= max(.015, height_extent * .2), facts


def simplify(data, tier, role='none'):
    vertices, submeshes, position, prefix = parse(data)
    fixed = boundaries(vertices, submeshes)
    low = [min(point[axis] for point in vertices) for axis in range(3)]
    high = [max(point[axis] for point in vertices) for axis in range(3)]
    # Keep the actual silhouette bounds, not merely the serialized Bounds value.
    # The open seam positions are fixed in all three dimensions.
    for axis in range(3):
        fixed.add(min(vertices, key=lambda point: (point[axis], point)))
        fixed.add(max(vertices, key=lambda point: (point[axis], point)))
    if role == 'floor':
        # Closed native floor meshes have no topological boundary. Their outer
        # XZ rim is still a room seam; pin every original position on that rim.
        fixed.update(point for point in vertices
                     if any(point[axis] == low[axis] or point[axis] == high[axis] for axis in (0, 2)))
    for divisions in ((6, 8, 12) if tier == 50 else (2, 3, 4, 6, 8)):
        groups = collections.defaultdict(set)
        def cell(point):
            return tuple(min(divisions - 1, max(0, int((point[axis] - low[axis])
                         / (high[axis] - low[axis]) * divisions))) if high[axis] > low[axis] else 0
                         for axis in range(3))
        for point in vertices:
            if point not in fixed: groups[cell(point)].add(point)
        centers = {key: tuple(sum(point[axis] for point in group) / len(group) for axis in range(3))
                   for key, group in groups.items()}
        moved = [point if point in fixed else centers[cell(point)] for point in vertices]
        # Certificates inspect the exact float32 values the decoder will receive.
        moved = [struct.unpack('<fff', struct.pack('<fff', *point)) for point in moved]
        reduced = []
        for triangles in submeshes:
            retained = []
            for a, b, c in triangles:
                first, second, third = moved[a], moved[b], moved[c]
                ab = [second[axis] - first[axis] for axis in range(3)]
                ac = [third[axis] - first[axis] for axis in range(3)]
                cross = (ab[1] * ac[2] - ab[2] * ac[1], ab[2] * ac[0] - ab[0] * ac[2],
                         ab[0] * ac[1] - ab[1] * ac[0])
                if sum(value * value for value in cross) > 1e-18: retained.append((a, b, c))
            reduced.append(retained)
        before, after = sum(map(len, submeshes)), sum(map(len, reduced))
        if after == 0 or after >= before * .98:
            continue
        floor_facts = {}
        if role == 'floor':
            valid, floor_facts = floor_certificate(vertices, moved, submeshes, reduced)
            if not valid: continue
        output = bytearray(data[:prefix])
        for index, point in enumerate(moved): struct.pack_into('<fff', output, position + 12 * index, *point)
        output.extend(struct.pack('<i', len(reduced)))
        for triangles in reduced:
            indices = [index for triangle in triangles for index in triangle]
            output.extend(struct.pack('<i', len(indices)))
            output.extend(struct.pack('<' + 'i' * len(indices), *indices))
        return bytes(output), dict(tier=tier, sourceTriangles=before, triangles=after,
                                  sourceVertices=len(vertices), vertices=len(moved),
                                  fixedBoundaryPositions=len(boundaries(vertices, submeshes)),
                                  fixedSeamAndBoundsPositions=len(fixed), gridDivisions=divisions,
                                  sameIndexMorph=True, **floor_facts)
    return None, None
