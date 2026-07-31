# palm_atlas.py — the atlas machinery every palm paint pass needs, lifted out of palm_repaint.py
# so the leather pass and the original repaint cannot drift apart. Nothing here decides what the
# palm should LOOK like; it only answers
#
#   rasterise()   for every texel of the shared 2048 atlas: which 3-D point on the hand does it
#                 sit on, which way does the surface face there, how much hand does one texel
#                 cover, what is its palm weight (palm_region.py), and is it claimed by a face
#                 that is NOT palm (in which case it is off limits — L and R share this atlas)
#   gutter_fill() every texel no face samples gets its nearest island's colour, because the
#                 packer left BLACK there and that black bleeds into every seam from mip 1 on
#   bone_heads()  the rig's own landmarks, so a design can be laid out against the anatomy
#   assert_outside_untouched()  the standing proof obligation, in one call
#
# The rasteriser and the gutter fill were proven in commit 0416342 ("the fold was in the normals,
# the star was in the paint"); the refactor was checked by running the pre-refactor script and
# this one on the same inputs and comparing md5 (identical). Two later fixes are documented at
# rasterise(): the missing 3-D positions, and the anisotropic texel footprint.
import bpy, bmesh, os, math
import numpy as np

from palm_region import palm_weight


def load_atlas(path, log=print):
    """The atlas as float RGBA, row 0 = BOTTOM row (as UV v=0 is). Raw values, no transfer."""
    img = bpy.data.images.load(os.path.abspath(path))
    img.colorspace_settings.name = 'Non-Color'
    W, H = img.size
    buf = np.empty(W * H * 4, np.float32)
    img.pixels.foreach_get(buf)
    A = buf.reshape(H, W, 4).copy()
    log(f"atlas {path}: {W}x{H}, {A.shape[2]} channels")
    return A, W, H


def save_atlas(A, W, H, path, alpha=True, log=print):
    out = bpy.data.images.new("out", W, H, alpha=alpha)
    out.colorspace_settings.name = 'Non-Color'
    out.pixels.foreach_set(A.reshape(-1).astype(np.float32))
    out.file_format = 'PNG'
    out.filepath_raw = os.path.abspath(path)
    out.save()
    log(f"wrote {path} ({os.path.getsize(path)} bytes)")


def bone_heads(fbx, log=print):
    """{bone name: world head position in mm} for one hand — the rig's own landmarks.

    A design laid out against these instead of against literal coordinates says what it means
    ("the leather covers the palm, from the wrist to the knuckles") and survives the hand being
    re-rigged. The names are the frozen 19-bone contract, Anchor_*.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    mw = arm.matrix_world
    out = {b.name: np.array(mw @ b.head_local) * 1000.0 for b in arm.data.bones}
    log(f"{os.path.basename(fbx)}: {len(out)} rig landmarks")
    return out


def rasterise(fbxs, W, H, log=print):
    """Rasterise every hand's UVs into per-texel palm weight, 3-D point and coverage.

    The LAST FBX is the GEOMETRY REFERENCE: its world positions define the surface that the
    surface-space work lives on. L and R share this atlas and this UV layout but are mirror
    images, so letting both write positions would make one texel mean two different points in
    space. Both still contribute coverage and palm weight, so a texel either hand uses outside
    the palm is still protected.

    THE POSITION BUG, fixed here 2026-07 — the reference hand's positions used to be written
    only where the triangle IMPROVED the accumulated palm weight. But the accumulator is shared
    with the other hand, which is rasterised first and has already put 1.0 into every interior
    texel, so "improve" was false for all of them and 63 % of the palm (247 944 texels, exactly
    the full-weight middle) kept the initialised position (0, 0, 0). Every surface-space pass
    then found the whole palm interior in ONE grid cell at the origin: the 3-D blur averaged it
    to a single colour and the 3-D noise evaluated to a single value. That is precisely why the
    repainted palm came out a featureless grey plate with no tonal variation anywhere. The
    reference hand now keeps its OWN coverage accumulator, so it writes a position for every
    texel it touches.

    Returns (tw, tp, tn, tf, used, palm):
        tw    (H,W)    palm weight per texel, 0 outside the palm
        tp    (H,W,3)  3-D point on the reference hand, mm
        tn    (H,W,3)  world-space surface normal there, area-weighted per vertex
        tf    (H,W)    TEXEL FOOTPRINT in mm: the LARGEST surface distance one texel step
                       spans here (the top singular value of the texel->surface Jacobian)
        used  (H,W)    any face at all samples this texel
        palm  (H,W)    tw > 0 — the only texels a paint pass may write

    The footprint is what stops a paint pass from authoring detail the atlas cannot hold. This
    palm is NOT uniformly 3 texels/mm: the decimator left its middle as a handful of very large
    flat triangles whose islands are both small AND badly stretched, and there one texel step
    can span 10 mm of hand along one axis. Anything finer than that combs — the first leather
    grain came out as a fan of vertical stripes across exactly those triangles. Measured per
    triangle, so it carries none of the island-edge contamination a texel-neighbour estimate
    has, and measured as the largest singular value rather than the area, because the area
    measure averages the starved axis away and reported 1.7 mm where the truth was 10.
    """
    tw = np.zeros((H, W), np.float32)
    tp = np.zeros((H, W, 3), np.float32)
    tn = np.zeros((H, W, 3), np.float32)
    tf = np.zeros((H, W), np.float32)
    used = np.zeros((H, W), bool)
    blocked = np.zeros((H, W), bool)      # a NON-palm face samples this texel
    got = np.zeros((H, W), bool)          # the reference hand gave this texel a 3-D point
    twr = np.zeros((H, W), np.float32)    # the REFERENCE hand's own weight accumulator

    for path in fbxs:
        is_ref = path == fbxs[-1]
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=path)
        ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
        me = ob.data
        bm = bmesh.new()
        bm.from_mesh(me)
        w, P, NW, _ = palm_weight(ob, bm, bpy.context.evaluated_depsgraph_get(), log=log)
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
            nv_ = np.array([NW[v] for v in vi])
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
            a, bp, cp = px[0], px[1], px[2]
            d = (bp[1] - cp[1]) * (a[0] - cp[0]) + (cp[0] - bp[0]) * (a[1] - cp[1])
            if abs(d) < 1e-12:
                continue
            l0 = ((bp[1] - cp[1]) * (gx - cp[0]) + (cp[0] - bp[0]) * (gy - cp[1])) / d
            l1 = ((cp[1] - a[1]) * (gx - cp[0]) + (a[0] - cp[0]) * (gy - cp[1])) / d
            l2 = 1.0 - l0 - l1
            # a texel counts as covered when its centre is in the triangle, plus a small skirt
            # so neighbouring triangles of one island never leave an unpainted line of old paint
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
                # against the reference hand's OWN accumulator, never the shared one
                bref = inside & ((fw3 > twr[sub]) | ~got[sub])
                cr = twr[sub]
                cr[bref] = fw3[bref]
                twr[sub] = cr
                got[sub] |= inside
                for k in range(3):
                    comp = l0 * pv[0][k] + l1 * pv[1][k] + l2 * pv[2][k]
                    layer = tp[sub][..., k]
                    layer[bref] = comp[bref]
                    tp[sub][..., k] = layer
                    cn = l0 * nv_[0][k] + l1 * nv_[1][k] + l2 * nv_[2][k]
                    lay = tn[sub][..., k]
                    lay[bref] = cn[bref]
                    tn[sub][..., k] = lay
                # The footprint is the LARGEST stretch of the texel->surface map, not the
                # square root of the area ratio. Area misses anisotropy, and these islands are
                # wildly anisotropic: a palm triangle can be 100 texels wide and 3 tall in the
                # atlas while covering 30 x 30 mm of hand, which the area measure calls 1.7 mm
                # per texel and which is really 0.3 mm one way and 10 mm the other. Grain
                # authored against the area measure came out as a fan of vertical stripes
                # across exactly those triangles — aliasing along the starved axis.
                # J maps texel steps to millimetres; its largest singular value is the answer.
                E = np.array([[bp[0] - a[0], cp[0] - a[0]],
                              [bp[1] - a[1], cp[1] - a[1]]], float)
                dete = E[0, 0] * E[1, 1] - E[0, 1] * E[1, 0]
                if abs(dete) < 1e-9:
                    continue
                Einv = np.array([[E[1, 1], -E[0, 1]], [-E[1, 0], E[0, 0]]]) / dete
                J = np.stack([pv[1] - pv[0], pv[2] - pv[0]], 1) @ Einv     # (3, 2), mm/texel
                M = J.T @ J
                tr, dt = M[0, 0] + M[1, 1], M[0, 0] * M[1, 1] - M[0, 1] * M[1, 0]
                smax = math.sqrt(max(tr / 2 + math.sqrt(max(tr * tr / 4 - dt, 0.0)), 0.0))
                lf = tf[sub]
                lf[bref] = smax
                tf[sub] = lf
        log(f"{os.path.basename(path)}: {npal} palm triangles rasterised")

    tw[blocked] = 0.0
    tw[~got] = 0.0
    palm = tw > 0.0
    log(f"texels: {int(used.sum())} used by the mesh, {int(palm.sum())} in the palm "
        f"({int((tw > 0.99).sum())} at full weight), {int((~used).sum())} gutter")
    if palm.sum() < 10000:
        raise SystemExit("palm_atlas: the palm barely covers the atlas — the rasteriser is wrong")
    # the bug above was silent for two rounds because nothing checked it. It cannot be again.
    orphan = int((palm & (np.abs(tp).max(2) == 0)).sum())
    log(f"palm texels without a 3-D position: {orphan} "
        f"({100.0 * orphan / max(int(palm.sum()), 1):.2f} %)")
    if orphan > palm.sum() * 0.01:
        raise SystemExit(f"palm_atlas: {orphan} palm texels carry no 3-D point — every "
                         f"surface-space pass would collapse them into one grid cell")
    tn /= np.maximum(np.linalg.norm(tn, axis=2, keepdims=True), 1e-20)
    fp = tf[palm]
    log(f"texel footprint on the palm: median {np.median(fp):.2f} mm, p90 {np.percentile(fp, 90):.2f}, "
        f"p99 {np.percentile(fp, 99):.2f}, max {fp.max():.2f} — anything finer than twice this "
        f"cannot be painted here")
    return tw, tp, tn, tf, used, palm


def gutter_fill(A, used, pad, log=print):
    """Flood every unused texel with its nearest island's colour. In place.

    No triangle samples these texels at mip 0, so nothing visible can change there; every mip
    above it stops averaging island colour with the packer's black.
    """
    gut = ~used
    if pad <= 0 or not gut.any():
        return A
    H, W = used.shape
    src = np.zeros((H, W, 2), np.int32)
    yy, xx = np.mgrid[0:H, 0:W]
    src[..., 0] = yy
    src[..., 1] = xx
    known = used.copy()
    for _ in range(pad):
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
    A[grown] = A[src[grown][:, 0], src[grown][:, 1]]
    log(f"gutter fill: {int(grown.sum())} unused texels filled from their nearest island "
        f"({pad} texel skirt)")
    return A


def assert_outside_untouched(A0, A1, used, palm, log=print, tag="palm_atlas"):
    """Every texel a NON-palm face samples must be bit-identical at 8 bit. Aborts if not."""
    q0 = np.round(A0 * 255).astype(np.int16)
    q1 = np.round(A1 * 255).astype(np.int16)
    diff = (np.abs(q0 - q1).max(2) > 0)
    outside = int((diff & used & ~palm).sum())
    log(f"texels changed: {int(diff.sum())} total, {int((diff & palm).sum())} in the palm, "
        f"{int((diff & ~used).sum())} in the gutter, {outside} used-by-something-else")
    if outside:
        raise SystemExit(f"{tag}: {outside} texels OUTSIDE the palm changed — aborting")
    return diff
