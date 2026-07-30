# palm_texels.py — Blender headless: which atlas texels belong to the TEXEL-STARVED faces,
# and where does each of them sit in 3-D? Feeds deflare_palm.py, which does the filtering.
#
# Split in two on purpose: Blender's bundled Python has numpy but no PIL, so mesh work and
# image work live in separate processes. This half never touches the PNG.
#
# "Starved" is measured, never hand-picked: a face's texel density is
# sqrt(uvArea * px^2 / worldArea), and starved means under half the mesh's median. On
# VRHandPlate that selects the palm plate and the cuff band — 13.3 % of the surface living on
# 0.58 % of the atlas at 1.07 texels/mm against a median of 3.24.
#
# RUN:
#   blender --background --python palm_texels.py -- <atlas_px> <out.npz> <grow> <fbx> [<fbx> ...]
#
# <grow> is how many face rings to add around the measured selection. It matters: a baked
# wedge does not stop where the density does, so a selection that ends mid-wedge leaves the
# step it was supposed to remove. Two rings covers it on this mesh.
import bpy, bmesh, sys, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
PX, OUT, GROW, FBXS = int(argv[0]), argv[1], int(argv[2]), argv[3:]
DENSITY_FRACTION = 0.5


def log(*a):
    print("[palm_texels]", *a)


def face_texels(f, uvl, px):
    """Texels covered by one face's UV triangles, each with its barycentric 3-D position."""
    uv = [(l[uvl].uv.x * px, (1.0 - l[uvl].uv.y) * px) for l in f.loops]
    co = [l.vert.co for l in f.loops]
    out = []
    for i in range(1, len(uv) - 1):
        a, b, c = uv[0], uv[i], uv[i + 1]
        pa, pb, pc = co[0], co[i], co[i + 1]
        x0 = max(0, int(min(a[0], b[0], c[0])) - 1); x1 = min(px, int(max(a[0], b[0], c[0])) + 2)
        y0 = max(0, int(min(a[1], b[1], c[1])) - 1); y1 = min(px, int(max(a[1], b[1], c[1])) + 2)
        if x1 <= x0 or y1 <= y0:
            continue
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        yy, xx = np.mgrid[y0:y1, x0:x1]
        cx = xx + 0.5; cy = yy + 0.5
        l1 = ((b[1] - c[1]) * (cx - c[0]) + (c[0] - b[0]) * (cy - c[1])) / d
        l2 = ((c[1] - a[1]) * (cx - c[0]) + (a[0] - c[0]) * (cy - c[1])) / d
        l3 = 1.0 - l1 - l2
        # A small negative tolerance keeps the texels straddling a triangle edge, which is
        # where bilinear filtering will reach anyway.
        m = (l1 >= -0.02) & (l2 >= -0.02) & (l3 >= -0.02)
        if not m.any():
            continue
        pos = np.outer(l1[m], pa) + np.outer(l2[m], pb) + np.outer(l3[m], pc)
        out.append((yy[m], xx[m], pos))
    return out


ys, xs, ps, fids, fpos = [], [], [], [], []
for fbx in FBXS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    o = [x for x in bpy.data.objects if x.type == 'MESH'][0]
    bm = bmesh.new(); bm.from_mesh(o.data)
    uvl = bm.loops.layers.uv.active
    bm.faces.ensure_lookup_table()

    dens = np.zeros(len(bm.faces))
    for f in bm.faces:
        a = f.calc_area()
        if a <= 0:
            continue
        uv = [l[uvl].uv for l in f.loops]
        ua = sum(abs((uv[i] - uv[0]).cross(uv[i + 1] - uv[0])) / 2 for i in range(1, len(uv) - 1))
        dens[f.index] = math.sqrt((ua * PX * PX) / (a * 1e6))
    med = float(np.median(dens[dens > 0]))
    thresh = med * DENSITY_FRACTION
    # A fixed threshold picks only the WORST faces, and a baked wedge does not stop where the
    # threshold does: rendering the mask onto the hand showed the star's bright wedges lying
    # outside it, which is why filtering the selection barely moved the star. So: seed on the
    # faces under half the median, then flood through neighbours that are still below the
    # median. That follows the coarse patch to its real edge and stops at the fingers, which
    # sit well above the median. GROW then adds a few rings of pure margin on top.
    core = set(f for f in bm.faces if 0 < dens[f.index] < thresh)
    sel = set(core)
    stack = list(core)
    while stack:
        f = stack.pop()
        for e in f.edges:
            for g in e.link_faces:
                if g not in sel and 0 < dens[g.index] < med:
                    sel.add(g); stack.append(g)
    flooded = len(sel)
    for _ in range(GROW):
        ring = set()
        for f in sel:
            for e in f.edges:
                ring.update(g for g in e.link_faces if g not in sel)
        sel |= ring
    starved = list(sel)
    log(f"{fbx}: median {med:.2f} texels/mm, core {len(core)} under {thresh:.2f}, "
        f"flooded to {flooded}, {len(starved)}/{len(bm.faces)} after {GROW} margin ring(s)")

    for f in starved:
        tex = face_texels(f, uvl, PX)
        if not tex:
            continue
        fid = len(fpos)
        fpos.append(np.array(f.calc_center_median()))
        for (fy, fx, pos) in tex:
            ys.append(fy); xs.append(fx); ps.append(pos)
            fids.append(np.full(len(fy), fid, np.int64))
    bm.free()

ys = np.concatenate(ys); xs = np.concatenate(xs); ps = np.concatenate(ps)
fids = np.concatenate(fids); fpos = np.array(fpos)
# Both hands share one atlas: keep each texel once, with the first face that claimed it.
_, first = np.unique(ys.astype(np.int64) * PX + xs, return_index=True)
ys, xs, ps, fids = ys[first], xs[first], ps[first], fids[first]
log(f"{len(ys)} distinct texels ({100.0*len(ys)/(PX*PX):.2f}% of the atlas) "
    f"on {len(fpos)} faces")
np.savez_compressed(OUT, ys=ys, xs=xs, pos=ps, fid=fids, fpos=fpos, px=np.array([PX]))
log("wrote", OUT)
