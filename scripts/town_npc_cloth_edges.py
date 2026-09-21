"""Trim the actual original shirt edges; never cover them with a second plate.

The priestess keeps her original blouse and atlas. Its low-resolution former
neck is cut back to the existing curved opening, with interpolated original UVs.
The merchant repair targets the two atlas islands identified by the pixel-to-
triangle probe, behind and above his untouched front clasp.
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


def _merchant_fragment(bm, uv):
    removed = []
    for face in bm.faces:
        x, y, z = face.calc_center_median()
        if not (abs(x) < .022 and -.082 < y < -.030 and 1.407 < z < 1.455):
            continue
        co = sum((loop[uv].uv for loop in face.loops), Vector((0., 0.))) / len(face.loops)
        # Original542 UV provenance: review15 camera rays hit the grey shirt
        # triangles at (.831,.373) and their split island at (.610,.868).
        if (.823 < co.x < .840 and .365 < co.y < .386) or (.602 < co.x < .618 and .857 < co.y < .882):
            removed.append(face)
    bmesh.ops.delete(bm, geom=removed, context='FACES_ONLY')
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
        return _merchant_fragment(bm, uv)
    if npc == 'enchantress':
        return {'removedOriginalFaces': 0, 'clippedOriginalFaces': 0, 'newBoundaryVertices': 0}
    raise ValueError('Unknown town costume: ' + npc)
