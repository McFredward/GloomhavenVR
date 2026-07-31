# palm_repaint.py — Blender headless: repaint the Plate gauntlet's PALM in the shared albedo,
# in place, without moving one UV and without touching one texel any other part of the hand
# samples.
#
# WHY
#   The "star" of flat wedges on the palm is PIGMENT, not polygons. An emission-only render (no
#   lights, every pixel literally a texel) shows it in full; a clay render shows nothing. The
#   albedo came off the Hunyuan model, which baked its shading onto the coarse pre-decimation
#   mesh, so the decimator's big flat fans are painted into the image. Subdivision cannot remove
#   pigment, and neither can any amount of relaxing.
#
# WHY NOT THE PREVIOUS TWO ATTEMPTS
#   * Filtering IN ATLAS SPACE cannot work: two wedges that touch on the hand can sit anywhere in
#     the image, so no image neighbourhood contains both. Measured here: the palm is shredded
#     into 370 UV islands.
#   * RE-UNWRAPPING the palm into a fresh 4096 atlas did remove the star, but doubled the texture
#     and moved every other UV by a factor of 0.5, which is a lot of risk for the rest of the
#     hand. It is not needed: this palm is not texel-starved. Measured on the shipped asset, the
#     palm runs 3.0 texels/mm against 3.1 for the hand as a whole, and holds 14.1 % of the atlas
#     area for 18.0 % of the surface. The resolution was never the problem — the pigment was.
#
# WHAT THIS DOES
#   Everything in SURFACE space, which is the only space where two wedges that touch are
#   neighbours:
#     1. Rasterises both hands' UVs and records, per texel, its 3-D point on the hand and its
#        palm weight (palm_region.py — the same weight palm_smooth.py uses for the shading, so
#        the two stop in the same place). L and R share this atlas, so both contribute coverage
#        and a texel any NON-palm face uses is struck out and never written; the 3-D points come
#        from the LAST hand alone, since the two are mirror images and one texel must mean one
#        point in space.
#     2. Projects the palm texels into the palm plate's own tangent frame, builds a 1 mm grid of
#        the existing colour there and blurs it wide. Wide is the point: the wedges are 20-40 mm
#        across, so anything narrower leaves a soft band exactly where the hard edge was.
#     3. Puts the surface's own character back as 3-D VALUE NOISE evaluated at each texel's
#        world position — three octaves, so it reads as metal grain rather than as blur. Being a
#        function of the 3-D point and not of UV, it crosses all 370 island boundaries without a
#        seam, which is precisely what a UV-space grain could not do.
#     4. Blends by the palm weight, so texels at the edge of the region fade back into the
#        original pigment instead of ending at a line.
#     5. GUTTER FILL: the atlas is packed wall to wall with BLACK between the islands. At mip 1
#        and beyond that black bleeds into every island edge as a dark hairline — the "cracks"
#        that get reported as holes. Every texel no face samples is filled with its nearest
#        used colour. This cannot change what any triangle samples at mip 0 and it improves
#        every island on the hand, not just the palm.
#
# PROOF OBLIGATIONS, asserted here
#   * every texel used by a face outside the palm is BIT-IDENTICAL afterwards
#   * the PNG on disk really changed (byte length and pixel diff are printed)
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/palm_repaint.py -- \
#       <L.fbx> <R.fbx> <albedo.png> <out.png> [--blur 14] [--grain 0.55] [--pad 6]
import bpy, bmesh, sys, os, math
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palm_region import palm_weight   # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
OPTS = ("--blur", "--grain", "--pad", "--seed")
pos, skip = [], False
for a in argv:
    if skip:
        skip = False
        continue
    if a in OPTS:
        skip = True
        continue
    if a.startswith("--"):
        continue
    pos.append(a)
FBXS = pos[:-2]
SRC_PNG, DST_PNG = pos[-2], pos[-1]


def getopt(n, d):
    return float(argv[argv.index(n) + 1]) if n in argv else d


BLUR_MM = getopt("--blur", 14.0)     # sigma of the surface-space blur, in mm
GRAIN = getopt("--grain", 0.5)       # 0 = mirror-smooth, 1 = as grainy as the original palm
PAD = int(getopt("--pad", 6))        # texels of gutter fill around every island
SEED = int(getopt("--seed", 7))


def log(*a):
    print("[palm_repaint]", *a)


# ------------------------------------------------------------------ read the atlas exactly
img = bpy.data.images.load(os.path.abspath(SRC_PNG))
img.colorspace_settings.name = 'Non-Color'   # raw 8-bit values, no transfer function applied
W, H = img.size
buf = np.empty(W * H * 4, np.float32)
img.pixels.foreach_get(buf)
A0 = buf.reshape(H, W, 4).copy()             # row 0 is the BOTTOM row, as UV v=0 is
log(f"atlas {SRC_PNG}: {W}x{H}, {A0.shape[2]} channels")

# ------------------------------------------------------------------ rasterise both hands
tw = np.zeros((H, W), np.float32)            # palm weight per texel
tp = np.zeros((H, W, 3), np.float32)         # 3-D point per texel, mm
used = np.zeros((H, W), bool)                # any face at all samples this texel
blocked = np.zeros((H, W), bool)             # a NON-palm face samples this texel
got = np.zeros((H, W), bool)                 # the reference hand gave this texel a 3-D point

# The last FBX is the GEOMETRY REFERENCE: its world positions define the surface the blur and
# the grain live on. L and R share this atlas and this UV layout but are mirror images, so
# letting both write positions would make one texel mean two different points in space and drag
# the plate frame across both hands. Both still contribute coverage and palm weight, so a texel
# either hand uses outside the palm is still protected.
for path in FBXS:
    is_ref = path == FBXS[-1]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    w, P, _, _ = palm_weight(ob, bm, bpy.context.evaluated_depsgraph_get(), log=log)
    bm.free()
    me.calc_loop_triangles()
    uvd = me.uv_layers.active.data
    npal = 0
    for t in me.loop_triangles:
        li = t.loops
        vi = t.vertices
        uv = np.array([[uvd[l].uv[0], uvd[l].uv[1]] for l in li])
        wv = np.array([w[v] for v in vi])
        pv = np.array([P[v] for v in vi])
        px = uv * np.array([W, H])
        x0 = max(int(math.floor(px[:, 0].min())) - 1, 0)
        x1 = min(int(math.ceil(px[:, 0].max())) + 1, W)
        y0 = max(int(math.floor(px[:, 1].min())) - 1, 0)
        y1 = min(int(math.ceil(px[:, 1].max())) + 1, H)
        if x1 <= x0 or y1 <= y0:
            continue
        xs = np.arange(x0, x1) + 0.5
        ys = np.arange(y0, y1) + 0.5
        gx, gy = np.meshgrid(xs, ys)
        a, b, c = px[0], px[1], px[2]
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        l0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / d
        l1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / d
        l2 = 1.0 - l0 - l1
        # a texel counts as covered when its centre is in the triangle, plus a small skirt so
        # neighbouring triangles of one island never leave an unpainted line of old pigment
        inside = (l0 > -0.02) & (l1 > -0.02) & (l2 > -0.02)
        if not inside.any():
            continue
        sub = np.s_[y0:y1, x0:x1]
        used[sub] |= inside
        fw3 = l0 * wv[0] + l1 * wv[1] + l2 * wv[2]
        fw3 = np.clip(fw3, 0.0, 1.0)
        if wv.max() <= 0.0:
            blocked[sub] |= inside
            continue
        npal += 1
        better = inside & (fw3 > tw[sub])
        cur = tw[sub]
        cur[better] = fw3[better]
        tw[sub] = cur
        if is_ref:
            got[sub] |= inside
            for k in range(3):
                comp = l0 * pv[0][k] + l1 * pv[1][k] + l2 * pv[2][k]
                layer = tp[sub][..., k]
                layer[better] = comp[better]
                tp[sub][..., k] = layer
    log(f"{os.path.basename(path)}: {npal} palm triangles rasterised")

tw[blocked] = 0.0
tw[~got] = 0.0
palm = tw > 0.0
log(f"texels: {int(used.sum())} used by the mesh, {int(palm.sum())} in the palm "
    f"({int((tw > 0.99).sum())} at full weight), {int((~used).sum())} gutter")
if palm.sum() < 10000:
    raise SystemExit("palm_repaint: the palm barely covers the atlas — the rasteriser is wrong")

# ------------------------------------------------------------------ smooth in SURFACE space
# A 3-D grid, not a 2-D projection into the plate's tangent frame. The projection was tried
# first and is wrong here: the palm region wraps around the thenar and the sides of the hand, so
# two patches that are 60 mm apart on the surface land in the same cell and the blur mixes them
# — the render came out blotched in exactly the triangles where that happened. In 3-D, only
# points that really are near each other mix. Nothing but palm texels ever enters the grid, so
# the back of the hand cannot bleed in.
idx = np.nonzero(palm)
pts = tp[idx]                                     # mm
col = A0[idx][:, :3].astype(np.float64)
CELL = 2.0                                        # mm
lo3 = pts.min(0) - 3 * BLUR_MM
hi3 = pts.max(0) + 3 * BLUR_MM
dim = np.maximum(((hi3 - lo3) / CELL).astype(int) + 2, 3)
gi = np.clip(((pts - lo3) / CELL).astype(int), 0, dim - 1)
flat = (gi[:, 0] * dim[1] + gi[:, 1]) * dim[2] + gi[:, 2]
n = int(dim[0] * dim[1] * dim[2])
acc = np.zeros((n, 3))
cnt = np.zeros(n)
for k in range(3):
    acc[:, k] = np.bincount(flat, col[:, k], minlength=n)
cnt = np.bincount(flat, minlength=n).astype(float)
acc = acc.reshape(dim[0], dim[1], dim[2], 3)
cnt = cnt.reshape(dim[0], dim[1], dim[2])
log(f"palm colour grid: {dim.tolist()} cells of {CELL:.0f} mm over {len(pts)} texels, "
    f"{int((cnt > 0).sum())} occupied")


def gauss1d(sig):
    r = max(1, int(math.ceil(sig * 3)))
    x = np.arange(-r, r + 1)
    k = np.exp(-(x ** 2) / (2 * sig * sig))
    return k / k.sum()


def blur_nd(a, sig, axes):
    k = gauss1d(sig)
    out = a
    for ax in axes:
        out = np.apply_along_axis(lambda m: np.convolve(m, k, mode='same'), ax, out)
    return out


sig = BLUR_MM / CELL
num = np.stack([blur_nd(acc[..., k], sig, (0, 1, 2)) for k in range(3)], -1)
den = blur_nd(cnt, sig, (0, 1, 2))
# NORMALISED convolution: divide the blurred colour by the blurred occupancy, so the region's
# own edge does not pull the result toward black the way a plain blur over empty cells would.
smooth3 = num / np.maximum(den, 1e-12)[..., None]
new = smooth3[gi[:, 0], gi[:, 1], gi[:, 2]]
log(f"surface blur sigma {BLUR_MM:.0f} mm: palm colour spread "
    f"sd {col.std(0).mean():.4f} -> {new.std(0).mean():.4f}")

# ------------------------------------------------------------------ put the grain back, in 3-D
rng = np.random.default_rng(SEED)
lo = pts.min(0) - 4.0
span = pts.max(0) - pts.min(0) + 8.0


def value_noise(p, cell):
    n = np.maximum((span / cell).astype(int) + 2, 2)
    lat = rng.random((n[0], n[1], n[2])).astype(np.float32) - 0.5
    f = (p - lo) / cell
    i = np.floor(f).astype(int)
    t = f - i
    t = t * t * (3 - 2 * t)
    i = np.clip(i, 0, n - 2)
    out = np.zeros(len(p))
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                wgt = ((t[:, 0] if dx else 1 - t[:, 0]) *
                       (t[:, 1] if dy else 1 - t[:, 1]) *
                       (t[:, 2] if dz else 1 - t[:, 2]))
                out += wgt * lat[i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz]
    return out


grain = (value_noise(pts, 1.8) * 1.0 +
         value_noise(pts, 5.0) * 0.5 +
         value_noise(pts, 13.0) * 0.25)
grain /= max(np.abs(grain).std(), 1e-9)
# scale the grain to a fraction of the character the original palm had at that scale
resid = col - new
amp = GRAIN * float(np.percentile(np.abs(resid), 35))
new = new + grain[:, None] * amp
log(f"grain: 3 octaves of 3-D value noise, amplitude {amp:.4f} "
    f"({GRAIN:.2f} of the original palm's own p35 residual)")

# ------------------------------------------------------------------ write it back
A1 = A0.copy()
blend = tw[idx][:, None]
A1[idx[0], idx[1], :3] = np.clip(A0[idx][:, :3] * (1 - blend) + new * blend, 0.0, 1.0)

# ------------------------------------------------------------------ gutter fill
gut = ~used
if PAD > 0 and gut.any():
    src = np.zeros((H, W, 2), np.int32)
    yy, xx = np.mgrid[0:H, 0:W]
    src[..., 0] = yy
    src[..., 1] = xx
    known = used.copy()
    for _ in range(PAD):
        nxt = known.copy()
        cand = (~known)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            nb = np.roll(known, (dy, dx), (0, 1))
            take = cand & nb
            if not take.any():
                continue
            rs = np.roll(src, (dy, dx, 0), (0, 1, 2))
            src[take] = rs[take]
            nxt |= take
            cand &= ~take
        if not (nxt ^ known).any():
            break
        known = nxt
    grown = known & gut
    A1[grown] = A1[src[grown][:, 0], src[grown][:, 1]]
    log(f"gutter fill: {int(grown.sum())} unused texels filled from their nearest island "
        f"({PAD} texel skirt)")

# ------------------------------------------------------------------ proof
q0 = np.round(A0 * 255).astype(np.int16)
q1 = np.round(A1 * 255).astype(np.int16)
diff = (np.abs(q0 - q1).max(2) > 0)
outside_changed = int((diff & used & (tw <= 0.0)).sum())
log(f"texels changed: {int(diff.sum())} total, {int((diff & palm).sum())} in the palm, "
    f"{int((diff & gut).sum())} in the gutter, {outside_changed} used-by-something-else")
if outside_changed:
    raise SystemExit(f"palm_repaint: {outside_changed} texels OUTSIDE the palm changed — aborting")

out = bpy.data.images.new("out", W, H, alpha=(A0.shape[2] == 4))
out.colorspace_settings.name = 'Non-Color'
out.pixels.foreach_set(A1.reshape(-1).astype(np.float32))
out.file_format = 'PNG'
out.filepath_raw = os.path.abspath(DST_PNG)
out.save()
log(f"wrote {DST_PNG} ({os.path.getsize(DST_PNG)} bytes, source was "
    f"{os.path.getsize(SRC_PNG)})")
