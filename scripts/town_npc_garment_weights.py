"""Continuous costume support on the original town NPC surface (Blender only).

Atlas colour provides a soft regional prior, never an instruction to replace one
vertex's arm weights. A welded surface graph spreads that prior across connected
cloth, including UV seams. Hands and distal sleeves are fixed boundaries. This
avoids the rigid/arm-weight discontinuities that produced elbow ribbons in 544.
"""
import numpy as np


def _smooth(a, b, value):
    t = np.clip((value - a) / (b - a), 0., 1.)
    return t * t * (3. - 2. * t)


def garment_support(bm, body, npc, rgba, uv):
    """Blend cape/vest support continuously; return the changed vertex count.

    Coordinates and original UVs are unchanged. Coincident vertices share one
    solve and one final weight vector, so split atlas islands cannot open seams.
    The caller owns subsequent neckline geometry and export.
    """
    if npc not in ('merchant', 'priestess', 'enchantress'):
        raise ValueError('Unknown town costume: ' + npc)
    deform = bm.verts.layers.deform.active
    if deform is None or uv is None:
        raise ValueError('Costume support requires original weights and UVs')
    groups = {g.index: g.name for g in body.vertex_groups}
    axial = [body.vertex_groups[n].index for n in ('Hips', 'Spine', 'Chest')]
    chest = body.vertex_groups['Chest'].index
    names = list(groups)
    column = {index: i for i, index in enumerate(names)}
    welded, members, lookup = {}, [], {}
    for vertex in bm.verts:
        key = tuple(round(float(v), 5) for v in vertex.co)
        index = welded.get(key)
        if index is None:
            index = len(members); welded[key] = index; members.append([])
        members[index].append(vertex); lookup[vertex] = index
    count = len(members)
    if not count:
        return 0
    coords = np.array([list(vertices[0].co) for vertices in members])
    original = np.zeros((count, len(names)))
    colors = np.zeros((count, 3))
    for i, vertices in enumerate(members):
        samples = []
        for vertex in vertices:
            for group, weight in vertex[deform].items():
                original[i, column[group]] += weight / len(vertices)
            for loop in vertex.link_loops:
                u, v = loop[uv].uv
                samples.append(rgba[max(0, min(rgba.shape[0]-1, int(v*rgba.shape[0]))),
                                    max(0, min(rgba.shape[1]-1, int(u*rgba.shape[1]))), :3])
        if samples:
            colors[i] = np.median(samples, axis=0)
    r, g, b = colors.T
    x, y, z = coords.T
    if npc == 'priestess':
        # Warm cape versus pale linen. Include highlighted brown; a binary
        # saturation cutoff previously split adjacent triangles at the elbow.
        prior = _smooth(.10, .24, (r-b)/np.maximum(.001, r+b))
        prior *= _smooth(1.02, 1.20, r/np.maximum(.001, g))
    elif npc == 'merchant':
        purple = _smooth(1.25, 1.65, r/np.maximum(.001, g)) * _smooth(.95, 1.15, b/np.maximum(.001, g))
        green = _smooth(.80, 1., g/np.maximum(.001, r)) * _smooth(1.05, 1.35, g/np.maximum(.001, b))
        prior = np.maximum(purple, green)
    else:
        prior = _smooth(1., 1.20, g/np.maximum(.001, r)) * _smooth(1., 1.15, b/np.maximum(.001, r))
    region = _smooth(.80, .97, z) * (1.-_smooth(1.40, 1.49, z))
    region *= 1.-_smooth(.40, .48, np.abs(x))
    # The actual sleeves lie in front of the hanging cape, below the elbow.
    # Warm linen and highlighted brown share hues, so colour alone must not
    # anchor cuffs to the chest. Keep the posterior cape eligible; use a broad
    # shoulder transition rather than a hard elbow plane.
    sleeve = (1.-_smooth(.0, .08, y)) * _smooth(.16, .28, np.abs(x))
    sleeve *= 1.-_smooth(1.12, 1.27, z)
    region *= 1.-sleeve
    # Preserve skin, finger articulation and the distal cuff regardless of a
    # skin pixel's similar hue. Existing hand weights define anatomy, not colour.
    distal = original[:, [column[i] for i, name in groups.items()
                         if name.startswith(('Hand.', 'Thumb', 'Index', 'Middle', 'Ring', 'Little'))]].sum(axis=1)
    protect = distal > .04
    prior *= region
    prior[protect] = 0.
    edges = set()
    for edge in bm.edges:
        a, c = (lookup[v] for v in edge.verts)
        if a != c:
            edges.add((min(a, c), max(a, c)))
    pairs = np.array(sorted(edges), dtype=np.int32)
    if not len(pairs):
        return 0
    a, c = pairs.T
    length = np.linalg.norm(coords[a]-coords[c], axis=1)
    # Surface diffusion is symmetric and metre-aware; disconnected nearby
    # sleeves never receive weights merely because they touch the cape in rest.
    conductance = 1./np.maximum(.002, length)
    degree = np.bincount(a, conductance, minlength=count) + np.bincount(c, conductance, minlength=count)
    fidelity = degree * .008
    support = prior.copy()
    for _ in range(240):
        adjacent = np.bincount(a, conductance*support[c], minlength=count)
        adjacent += np.bincount(c, conductance*support[a], minlength=count)
        support = (adjacent + fidelity*prior)/np.maximum(1e-12, degree+fidelity)
        support[protect] = 0.
    support *= region
    support *= 1.-_smooth(0., .04, distal)
    support[protect] = 0.
    arm = original[:, [column[i] for i, name in groups.items()
                      if name.startswith(('UpperArm.', 'Forearm.', 'Clavicle.'))]].sum(axis=1)
    support[arm < 1e-7] = 0.
    target = np.zeros_like(original)
    axial_columns = [column[i] for i in axial]
    target[:, axial_columns] = original[:, axial_columns]
    total = target.sum(axis=1)
    target[total < 1e-7, column[chest]] = 1.
    target /= np.maximum(1e-12, target.sum(axis=1))[:, None]
    result = original*(1.-support[:, None]) + target*support[:, None]
    # Match the four-influence runtime contract. Do not create differing
    # platform truncation or silently discard the protected finger weights.
    order = np.argsort(result, axis=1)[:, :-4]
    np.put_along_axis(result, order, 0., axis=1)
    result /= np.maximum(1e-12, result.sum(axis=1))[:, None]
    if not np.all(np.isfinite(result)) or np.max(np.abs(result.sum(axis=1)-1.)) > 1e-6:
        raise ValueError('Invalid costume support weights')
    changed = 0
    for i, vertices in enumerate(members):
        if protect[i] or np.max(np.abs(result[i]-original[i])) < 1e-6:
            continue
        weights = {names[j]: float(result[i, j]) for j in np.flatnonzero(result[i] > 1e-8)}
        for vertex in vertices:
            vertex[deform].clear()
            for group, weight in weights.items():
                vertex[deform][group] = weight
            changed += 1
    return changed
