# palm_region.py — the ONE definition of "this is the palm", shared by palm_smooth.py (which
# rebuilds the shading there) and palm_repaint.py (which rebuilds the pigment there). They have
# to agree exactly, or one stops where the other does not and the seam shows.
#
# The region is a SMOOTH per-vertex weight in [0, 1], not a face selection. A hard patch needs a
# rim, and this mesh cannot give one: it carries 1080 non-manifold edges, so 619 of the palm's
# 716 vertices also belong to some face outside any face selection and "rim" swallows the patch.
#
# The weight is a product of four gates, in order of how much they are trusted:
#
#   S  IS IT ON THE PALM SIDE — how close the vertex sits to the frontmost surface in its own
#      column, measured against a depth map of the hand taken along world -Y (the palm direction
#      of the rig's contract frame). This is the only gate that does not depend on a normal, and
#      normals are the untrustworthy part of this asset: the shell is non-manifold with
#      overlapping fragments, so scattered vertices ON THE BACK carry an area-weighted normal
#      that points palmward. Selecting on the normal alone speckled the back of the hand with
#      2117 changed pixels in a render diff.
#      (A ray cast was tried first. It is a hard yes/no, and everything the thumb shadows came
#      out unpainted — stippled triangles of leftover pigment in the render. A depth map has no
#      shadows and is a smooth ramp.)
#   A  does it face the palm at all, from a SPATIALLY smoothed normal — a gentle ramp, so the
#      region may wrap round the sides of the hand but stops before the back.
#   B  how far above the cuff rim it sits.
#   C  how little it is skinned to a finger bone — the rig's own weights draw the MCP creases far
#      better than any geometric guess.
#
# The product is finally smoothed over a small spatial neighbourhood, and the cuff and finger
# ramps are re-applied afterwards so those two edges stay exactly where the rig puts them.
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

ZLOW = -78.0        # mm, world: the cuff rim
ZRAMP = 16.0        # mm over which the palm fades in above the cuff
NY0, NY1 = -0.25, 0.20    # palm-facing ramp on -Y; negative start = the sides still count
FWHI, FWRAMP = 0.72, 0.42
SMOOTH_MM = 6.0
SMOOTH_PASSES = 2
DEPTH_CELL = 3.0    # mm, the (x,z) grid of the palm-side depth map
DEPTH_IN = 7.0      # mm behind the frontmost surface that still counts fully as palm
DEPTH_OUT = 9.0     # mm over which it then fades out


def smoothstep(x):
    c = np.clip(x, 0.0, 1.0)
    return c * c * (3.0 - 2.0 * c)


def palm_weight(ob, bm, depsgraph, log=print, **over):
    """Per-vertex palm weight in [0,1] plus world geometry.

    Returns (w, P_mm, NW, vn): weight, world positions in mm, world-space vertex normals and the
    same normals in object space.
    """
    zlow = over.get("zlow", ZLOW)
    zramp = over.get("zramp", ZRAMP)
    ny0, ny1 = over.get("ny0", NY0), over.get("ny1", NY1)
    fwhi, fwramp = over.get("fw", FWHI), over.get("fwramp", FWRAMP)

    me = ob.data
    MW = ob.matrix_world
    NRM = MW.to_3x3().inverted().transposed()
    finger = [g.index for g in ob.vertex_groups
              if any(k in g.name for k in ("Index", "Middle", "Ring", "Pinky", "Thumb"))]
    fw = np.zeros(len(me.vertices))
    for i, v in enumerate(me.vertices):
        fw[i] = sum(g.weight for g in v.groups if g.group in finger)

    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    vn = np.zeros((len(bm.verts), 3))
    for f in bm.faces:
        fn = np.array(f.normal) * f.calc_area()
        for v in f.verts:
            vn[v.index] += fn
    vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-20)

    P = np.array([list(MW @ v.co) for v in bm.verts]) * 1000.0
    NW = np.array([list((NRM @ Vector(n)).normalized()) for n in vn])

    kd = KDTree(len(bm.verts))
    for i, p in enumerate(P):
        kd.insert(Vector(p), i)
    kd.balance()
    nbrs = [[j for _, j, _ in kd.find_range(Vector(P[i]), SMOOTH_MM)]
            for i in range(len(bm.verts))]

    # a spatially smoothed normal: one bad fragment cannot decide a vertex on its own
    NS = NW.copy()
    for _ in range(2):
        NS = np.array([NS[nb].mean(0) if nb else NS[i] for i, nb in enumerate(nbrs)])
        NS /= np.maximum(np.linalg.norm(NS, axis=1, keepdims=True), 1e-20)

    A = smoothstep((-NS[:, 1] - ny0) / max(ny1 - ny0, 1e-6))
    B = smoothstep((P[:, 2] - zlow) / zramp)
    C = smoothstep((fwhi - fw) / fwramp)
    gate = A * B * C

    # depth map along -Y: for every (x, z) cell, how far forward the hand reaches
    gx = ((P[:, 0] - P[:, 0].min()) / DEPTH_CELL).astype(int)
    gz = ((P[:, 2] - P[:, 2].min()) / DEPTH_CELL).astype(int)
    nx, nz = gx.max() + 1, gz.max() + 1
    front = np.full((nx, nz), np.inf)
    np.minimum.at(front, (gx, gz), P[:, 1])
    f2 = front.copy()                       # 3x3 min, so one stray vertex cannot mask a cell
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            f2 = np.minimum(f2, np.roll(np.roll(front, dx, 0), dz, 1))
    depth = P[:, 1] - f2[gx, gz]            # mm behind the frontmost surface
    S = 1.0 - smoothstep((depth - DEPTH_IN) / DEPTH_OUT)
    log(f"[palm_region] palm-side depth map {nx}x{nz} cells of {DEPTH_CELL:.0f} mm: "
        f"{int((S > 0.5).sum())} vertices within {DEPTH_IN:.0f} mm of the front")

    w = gate * S
    for _ in range(SMOOTH_PASSES):
        w = np.array([float(np.mean(w[nb])) if nb else w[i] for i, nb in enumerate(nbrs)])
        w = np.clip(w, 0.0, 1.0) * B * C
    log(f"[palm_region] palm weight: {int((w > 0.9).sum())} vertices near full weight, "
        f"{int((w > 0.01).sum())} touched at all, of {len(w)}")
    return w, P, NW, vn
