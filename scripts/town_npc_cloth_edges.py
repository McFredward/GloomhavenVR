"""Trim the actual original shirt edges; never cover them with a second plate.

The priestess keeps her original blouse and atlas. Its low-resolution former
neck is cut back to the existing curved opening, with interpolated original UVs.
The merchant repair targets the atlas islands identified by pixel-to-triangle
probes behind his untouched clasp, and the obsolete rear cape head-cut.
"""
import bmesh
from mathutils import Vector


def _blouse_height(x):
    return 1.371 + .018 * (x / .073) ** 2


def _clip_blouse(bm, body, rgba, uv):
    deform = bm.verts.layers.deform.active
    chest = body.vertex_groups['Chest'].index
    candidates = []
    for face in bm.faces:
        x, y, z = face.calc_center_median()
        if not (abs(x) < .083 and -.13 < y < -.028 and 1.345 < z < 1.47):
            continue
        co = sum((loop[uv].uv for loop in face.loops), Vector((0., 0.))) / len(face.loops)
        r, g, b = rgba[max(0, min(rgba.shape[0]-1, int(co.y*rgba.shape[0]))),
                       max(0, min(rgba.shape[1]-1, int(co.x*rgba.shape[1]))), :3]
        # Original linen, including its dark folded edge; gold fastenings and
        # their warm brown cords remain outside the cut even where they overlap.
        if r < g * 1.24 and b > g * .72:
            candidates.append(face)
    removed, clipped, boundary = [], 0, []
    edge_vertices = {}
    for face in candidates:
        original = list(face.loops)
        signs = [loop.vert.co.z - _blouse_height(loop.vert.co.x) for loop in original]
        if max(signs) <= 0:
            continue
        if min(signs) >= 0:
            removed.append(face)
            continue
        polygon = []
        for i, loop in enumerate(original):
            following = original[(i + 1) % len(original)]
            a, c = loop.vert, following.vert
            sa, sc = signs[i], signs[(i + 1) % len(original)]
            if sa <= 0:
                polygon.append((a, loop[uv].uv.copy()))
            if (sa > 0) == (sc > 0):
                continue
            # Solve against the curved opening rather than clipping by a
            # triangle-centre test, which leaves long serrated triangle tips.
            low, high = 0., 1.
            for _ in range(22):
                t = (low + high) * .5
                p = a.co.lerp(c.co, t)
                if (p.z - _blouse_height(p.x) > 0) == (sa > 0):
                    low = t
                else:
                    high = t
            t = (low + high) * .5
            key = frozenset((a, c))
            vertex = edge_vertices.get(key)
            if vertex is None:
                vertex = bm.verts.new(a.co.lerp(c.co, t))
                vertex[deform][chest] = 1.
                edge_vertices[key] = vertex
                boundary.append(vertex)
            polygon.append((vertex, loop[uv].uv.lerp(following[uv].uv, t)))
        if len(polygon) >= 3:
            replacement = bm.faces.new([v for v, _ in polygon])
            replacement.material_index = face.material_index
            replacement.smooth = face.smooth
            for loop, (_, co) in zip(replacement.loops, polygon):
                loop[uv].uv = co
            clipped += 1
        removed.append(face)
    bmesh.ops.delete(bm, geom=removed, context='FACES_ONLY')
    # UV-split copies of the same cut point must remain coincident. The later
    # original-garment lining pass supplies the thin sewn thickness.
    bmesh.ops.remove_doubles(bm, verts=boundary, dist=.00008)
    return {'removedOriginalFaces': len(removed), 'clippedOriginalFaces': clipped,
            'newBoundaryVertices': len(boundary)}


def _merchant_rear_cut(bm, body, uv):
    """Close the obsolete horizontal source head-cut inside the rear cape.

    This is a bounded hole in the existing cloth, behind the actual neck
    opening. Its original 542 boundary lies almost exactly at z=1.56 m.
    """
    candidates = {edge for edge in bm.edges if edge.is_boundary and
                  all(abs(v.co.x) < .07 and .128 < v.co.y < .20 and
                      1.552 < v.co.z < 1.566 for v in edge.verts)}
    graph = {}
    for edge in candidates:
        a, b = edge.verts
        graph.setdefault(a, set()).add(b)
        graph.setdefault(b, set()).add(a)
    # A low LOD can join the rear cut to the neckline at a single vertex.
    # Extract closed cycles instead of treating that whole component as a hole.
    remaining, cycles = set(graph), []
    while remaining:
        root = next(iter(remaining))
        stack, parent, used = [root], {root: root}, {root: set()}
        while stack:
            vertex = stack.pop()
            for other in graph[vertex]:
                if other not in used:
                    parent[other] = vertex
                    stack.append(other)
                    used[other] = {vertex}
                elif other not in used[vertex]:
                    cycle = [other, vertex]
                    previous = parent[vertex]
                    while previous not in used[other]:
                        cycle.append(previous)
                        previous = parent[previous]
                    cycle.append(previous)
                    cycles.append(cycle)
                    used[other].add(vertex)
        remaining.difference_update(parent)
    chest, filled = body.vertex_groups['Chest'].index, 0
    deform = bm.verts.layers.deform.active
    for vertices in cycles:
        area = abs(sum(a.co.x * b.co.y - b.co.x * a.co.y
                       for a, b in zip(vertices, vertices[1:] + vertices[:1]))) * .5
        if area < .0004 or sum(v.co.y for v in vertices) / len(vertices) < .15:
            continue
        edge = bm.edges.get((vertices[0], vertices[1]))
        original = edge.link_loops[0]
        if original.vert == vertices[0]:
            vertices.reverse()
        material = original.face.material_index
        patch = bm.faces.new(vertices)
        patch.material_index = material
        patch.smooth = True
        for loop in patch.loops:
            # Reuse the original adjacent purple cape island in its own atlas;
            # planar mapping avoids spanning unrelated source UV islands.
            loop[uv].uv = (.30 - loop.vert.co.x * .12,
                           .455 + (loop.vert.co.y - .15) * .25)
        bmesh.ops.triangulate(bm, faces=[patch])
        for vertex in vertices:
            vertex[deform].clear()
            vertex[deform][chest] = 1.
        filled += 1
    # The former colour-based head cut also leaves short doubled tips along
    # the inner rear rim. Reposition that existing cut to one smooth cloth edge;
    # keep the outer cape and the real neck opening, with no overlay strip.
    rim = {v for edge in bm.edges if edge.is_boundary for v in edge.verts
           if abs(v.co.x) < .10 and .065 < v.co.y < .14 and v.co.z > 1.50}
    for vertex in rim:
        x = vertex.co.x
        vertex.co.y = .131 - .052 * (x / .10) ** 2
        vertex.co.z = 1.550 - .041 * (x / .10) ** 2
    # Lower the immediately adjoining original fold tips with the cut, so an
    # interior vertex cannot remain as a lone triangular spike above the seam.
    for vertex in bm.verts:
        if abs(vertex.co.x) < .10 and .065 < vertex.co.y < .14 and vertex.co.z > 1.50:
            vertex.co.z = min(vertex.co.z, 1.550 - .041 * (vertex.co.x / .10) ** 2)
    bmesh.ops.remove_doubles(bm, verts=list(rim), dist=.0012)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=.00008)
    return filled


def _merchant_fragment(bm, uv):
    removed = []
    for face in bm.faces:
        x, y, z = face.calc_center_median()
        if not (abs(x) < .022 and -.082 < y < -.030 and 1.407 < z < 1.455):
            continue
        co = sum((loop[uv].uv for loop in face.loops), Vector((0., 0.))) / len(face.loops)
        # Original542 UV provenance: review15 camera rays hit the grey shirt
        # triangles at (.831,.373) and their split island at (.610,.868).
        # Two adjacent cut remnants were independently ray-matched to original
        # shirt islands (.430,.267)/(.663,.435); the authored collar is separate.
        if ((.823 < co.x < .840 and .365 < co.y < .386) or
                (.602 < co.x < .618 and .857 < co.y < .882) or
                (.424 < co.x < .436 and .262 < co.y < .274) or
                (.657 < co.x < .675 and .428 < co.y < .442)):
            removed.append(face)
    bmesh.ops.delete(bm, geom=removed, context='FACES_ONLY')
    # Adjacent source UV seams leave thin vertical slivers after the proven
    # islands are removed. Lower only this central original shirt cut behind
    # the clasp; its front/gold surfaces lie forward of y=-.082 and are excluded.
    for vertex in bm.verts:
        x, y, z = vertex.co
        if abs(x) < .023 and -.082 < y < -.030 and 1.417 < z < 1.455:
            vertex.co.z = 1.416
    return {'removedOriginalFaces': len(removed), 'clippedOriginalFaces': 0,
            'newBoundaryVertices': 0}


def repair_cloth_edges(bm, body, npc, rgba, uv):
    """Run after generic boundary smoothing/merchant Chest assignment.

    Insert immediately before shirt_band and solidify, on original costume only.

    No anatomical face vertex, atlas pixel, clasp, or new covering is authored.
    ``rgba`` identifies the retained original neutral linen beside gold clasps.
    """
    if npc == 'priestess':
        return _clip_blouse(bm, body, rgba, uv)
    if npc == 'merchant':
        result = _merchant_fragment(bm, uv)
        result['closedRearCapeCuts'] = _merchant_rear_cut(bm, body, uv)
        return result
    if npc == 'enchantress':
        return {'removedOriginalFaces': 0, 'clippedOriginalFaces': 0, 'newBoundaryVertices': 0}
    raise ValueError('Unknown town costume: ' + npc)
