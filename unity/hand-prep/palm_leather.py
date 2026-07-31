# palm_leather.py — Blender headless: author the Plate gauntlet's palm as a LEATHER PALM
# riveted into the steel, in the shared albedo, in surface space, without moving one UV.
#
# WHY THIS EXISTS
#   palm_repaint.py took the baked facet star out of the palm and left it a clean, featureless
#   grey plate. Clean was the point of that pass and it worked, but the palm then had no
#   character at all next to the back of the hand, which is segmented plates, panel lines, edge
#   highlights and gold studs. "The problem is gone, but so is every bit of texture" was the
#   verdict, and it was right.
#
# THE DESIGN, and why this one
#   A real armoured gauntlet does NOT have a plate palm. It has LEATHER there — you cannot grip
#   a weapon with steel, and the palm is the one place a fifteenth-century harness gives up
#   protection for function. So: a dark brown leather palm, let into the steel, with
#     * a rolled steel LIP catching light all round it and a contact shadow where the leather
#       drops into the recess — the fake depth the material cannot give us (BoardLit is albedo
#       only, no normal map, exactly like the back's plates which are also painted depth),
#     * a fine tan STITCH line following the edge,
#     * a handful of GOLD RIVETS through the leather into the plate beneath, at a deliberate
#       spacing — the same gold the back of the hand studs its plates with, so the palm reads as
#       part of the same object,
#     * leather grain as a two-scale crease network, and burnished WEAR where a hand actually
#       presses: the finger-root pads, the thenar mound, the heel.
#   Below the wrist the leather stops and the steel continues as the wrist plate, with brushed
#   streaks along the arm.
#
# AND IT CLEANS THE FINGER-ROOT TRANSITION
#   palm_repaint blended by the palm weight itself, so where the weight ramps out at the MCP
#   creases it left 30-100 % of the ORIGINAL pigment — the dark mottled stipple that reads as
#   grime in the screenshot. This pass blends with a firm curve instead (full paint wherever the
#   weight is above ~0.18, which is ~1.5 mm inside the region edge), and the thing it paints
#   there is the smoothed steel, not the leather. So the stipple goes and the transition is a
#   plain steel surface running into the finger plates, with the leather edge — lip, stitching,
#   rivets — sitting a deliberate distance inside it. The leather boundary is NOT the region
#   boundary: the region boundary is the rig's MCP crease and it zigzags by +-3 mm from vertex
#   to vertex, which no design edge may follow. The leather edge is a SMOOTHED, wrist-cut
#   version of it, and the steel band in between absorbs the zigzag invisibly because it is the
#   same colour on both sides of it.
#
# EVERYTHING IN SURFACE SPACE
#   The palm is shredded into ~370 UV islands, so any pattern authored in UV space seams at
#   every one of them. Every field here — the leather shape, the distance to its edge, the arc
#   length along it, the grain, the wear — is a function of the texel's 3-D POINT ON THE HAND,
#   taken from palm_atlas.rasterise(). The layout grid is the (x, z) plane of the rig's contract
#   frame, which is exactly the palm plane: x runs across the hand, z toward the fingers, and
#   -y is the palm normal.
#
# PROOF OBLIGATIONS, asserted here
#   * every texel used by a face outside the palm is BIT-IDENTICAL afterwards
#   * the atlas gutter is re-filled from the new island colours (palm_repaint's fill is stale
#     the moment the palm changes)
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/palm_leather.py -- \
#       <L.fbx> <R.fbx> <albedo.png> <out.png> [--cache scratch.npz] [--pad 6] [--seed 11]
import bpy, sys, os, math
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palm_atlas import (load_atlas, save_atlas, rasterise, gutter_fill,   # noqa: E402
                        assert_outside_untouched, bone_heads)

# ----------------------------------------------------------------------------- arguments
argv = sys.argv[sys.argv.index("--") + 1:]
OPTS = ("--cache", "--pad", "--seed", "--debug")
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
    return argv[argv.index(n) + 1] if n in argv else d


CACHE = getopt("--cache", "")
PAD = int(getopt("--pad", 6))
SEED = int(getopt("--seed", 11))
DEBUG = getopt("--debug", "")

# ----------------------------------------------------------------------------- the design
# All lengths in mm on the hand. The gauntlet is ~130 mm across the palm and ~95 mm from the
# wrist to the MCP creases, so these read as real leatherwork rather than as pattern.
CELL = 0.5            # mm, the (x, z) layout grid everything is laid out and sampled on.
                      # 1.0 was too coarse: the atlas runs 3 texels/mm, so a distance field
                      # quantised to 1 mm put a visible 1 mm SAWTOOTH along the whole leather
                      # edge in the render. Halving it costs a few seconds and nothing else.
FACE_N0, FACE_N1 = 0.28, 0.60   # -n.y ramp: below 0.28 the surface has turned away, steel only
NSMOOTH_MM = 4.0      # mm, how wide the surface normal is averaged before it is trusted

# THE PANEL. A gauntlet's leather palm is not a patch stuck on the middle of the hand — it is
# the WHOLE palm face, from the wrist to the knuckles and out to where the plates begin, one
# piece of hide laced into the steel. So the shape is the palm region itself, smoothed and inset
# by a constant margin, cut off above the wrist where the wrist plate takes over.
#   * smoothed, because the region's edge is the rig's per-vertex MCP crease and zigzags +-3 mm;
#   * inset, so the leather never reaches that edge and the steel band between them can absorb
#     the zigzag invisibly (it is the same colour on both sides of it);
#   * face-only, because the region also wraps onto the flanks of the hand, and leather there
#     would run round a corner the design does not have.
# An earlier attempt built the panel out of discs at the rig's landmarks instead. It was smooth,
# but it read as a blob dropped on the palm rather than as the palm's own surface, and it left
# broad steel margins that nothing explained. The landmarks are still used — for the WEAR.
CLIP_MARGIN = 3.5     # mm the leather keeps clear of the palm region's own edge
REG_SMOOTH = 3.5      # mm, how hard the region outline is smoothed before it becomes a design
WRIST_Z = -4.0        # mm, where the leather gives way to the wrist plate (wrist joint at z=6)
WRIST_BOW = 0.0016    # the wrist edge rises at the sides by BOW * x^2, so the panel sits in a bowl
CORNER_K = 6.0        # mm, how roundly the wrist cut meets the side edges
FIELD_SMOOTH = 1.2    # mm, final smoothing of the distance field: kills the 1 mm grid's fringe

# the landmarks the WEAR is laid out on: pads behind the knuckles, thenar mound, heel, hollow
PAD_R = 19.5
PAD_PULL = 0.12
THENAR_R = 24.0
THENAR_PULL = 0.38
HOLLOW_R = 31.0
HEEL_R = 23.0
HEEL_UP = 15.0

LIP_MM = 2.2          # width of the steel lip that catches light outside the leather
LIP_GAIN = 0.40       # how much brighter the crown of the lip is
LIP_FOOT = 4.2        # mm out, where the roll's own shadow sits
SHADOW_MM = 2.8       # width of the contact shadow inside the leather edge
SHADOW_GAIN = 0.45    # how much darker

STITCH_D = 4.0        # mm inside the leather edge
STITCH_W = 0.80       # mm half-width of the thread
STITCH_PERIOD = 5.0   # mm, one stitch + one gap
STITCH_DUTY = 0.55

CREASE_W = 1.9        # mm, half-width of a flex crease in the hide
CREASE_DARK = 0.30    # how much the fold darkens
CREASE_LIFT = 0.14    # and how much its near lip catches light

RIVET_PERIOD = 30.0   # mm along the leather edge
RIVET_D = 8.5         # mm inside the leather edge
RIVET_R = 2.3         # mm, head radius

LEATHER = np.array([0.196, 0.150, 0.113])   # the atlas's own dark strap brown, a shade lifted
STITCH = np.array([0.400, 0.336, 0.244])
GOLD = np.array([0.640, 0.512, 0.330])      # the atlas's own stud gold
GOLD_HI = np.array([0.800, 0.690, 0.500])

BLUR_MM = 12.0        # surface blur that rebuilds the steel base (and erases the MCP stipple)
W_ON, W_FULL = 0.02, 0.18   # the firm blend curve: full paint from 0.18 of the palm weight up


def log(*a):
    print("[palm_leather]", *a)


def smoothstep(x):
    c = np.clip(x, 0.0, 1.0)
    return c * c * (3.0 - 2.0 * c)


def gauss1d(sig):
    r = max(1, int(math.ceil(sig * 3)))
    x = np.arange(-r, r + 1)
    k = np.exp(-(x ** 2) / (2 * sig * sig))
    return k / k.sum()


def blur_axes(a, sig, axes):
    k = gauss1d(sig)
    out = a
    for ax in axes:
        out = np.apply_along_axis(lambda m: np.convolve(m, k, mode='same'), ax, out)
    return out


def surface_blur(vals, pts, sigma_mm, cell=2.0):
    """Average a per-texel quantity over a sphere on the HAND, not over the atlas.

    A normalised convolution on a 3-D occupancy grid: divide the blurred sum by the blurred
    count, so the region's own edge does not pull the result toward zero. This is the only
    neighbourhood operator that is legal here — the palm is ~370 UV islands, and two texels that
    are neighbours on the hand can be anywhere in the image.
    """
    vals = np.atleast_2d(vals.T).T if vals.ndim > 1 else vals[:, None]
    lo = pts.min(0) - 3 * sigma_mm
    hi = pts.max(0) + 3 * sigma_mm
    dim = np.maximum(((hi - lo) / cell).astype(int) + 2, 3)
    gi = np.clip(((pts - lo) / cell).astype(int), 0, dim - 1)
    flat = (gi[:, 0] * dim[1] + gi[:, 1]) * dim[2] + gi[:, 2]
    n = int(dim[0] * dim[1] * dim[2])
    sig = sigma_mm / cell
    num = np.stack([blur_axes(np.bincount(flat, vals[:, k], minlength=n)
                              .reshape(dim[0], dim[1], dim[2]), sig, (0, 1, 2))
                    for k in range(vals.shape[1])], -1)
    den = blur_axes(np.bincount(flat, minlength=n).astype(float)
                    .reshape(dim[0], dim[1], dim[2]), sig, (0, 1, 2))
    out = (num / np.maximum(den, 1e-12)[..., None])[gi[:, 0], gi[:, 1], gi[:, 2]]
    return out[:, 0] if out.shape[1] == 1 else out


def bilinear(grid, gu_f, gv_f):
    """Sample a (nu, nv) layout grid at fractional cell coordinates. Sub-cell smooth on purpose:
    reading the grid with a nearest lookup quantises every band edge to 1 mm, and at 3 texels/mm
    that shows in the render as a hairy fringe along the leather."""
    nu, nv = grid.shape
    a = np.clip(gu_f, 0, nu - 1.001)
    b = np.clip(gv_f, 0, nv - 1.001)
    i, j = a.astype(int), b.astype(int)
    fa, fb = a - i, b - j
    return (grid[i, j] * (1 - fa) * (1 - fb) + grid[i + 1, j] * fa * (1 - fb) +
            grid[i, j + 1] * (1 - fa) * fb + grid[i + 1, j + 1] * fa * fb)


def edt_signed(inside, uu, vv, log=print):
    """Exact signed Euclidean distance to the boundary of a grid set, plus, for every cell, the
    ARC LENGTH of the nearest boundary point along the boundary loop. The stitching and the
    rivets are spaced by that arc length, so the two must come from the same walk."""
    er = inside.copy()
    for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        er &= np.roll(np.roll(inside, da, 0), db, 1)
    bi, bj = np.nonzero(inside & ~er)
    bu, bv = uu[bi], vv[bj]
    cu, cv = bu.mean(), bv.mean()
    # ARC LENGTH BY THE POLAR INTEGRAL, not by chaining the cells. Two chainings were tried and
    # both lie about the perimeter: sorting the cells by angle scrambles wherever the boundary
    # runs diagonally and puts two or three cells at nearly the same angle (it reported 1379 mm
    # for a 280 mm seam, and scattered 46 rivets round it), and a greedy nearest-unvisited walk
    # still zig-zags across a boundary that is locally two cells thick (526 mm). The patch is
    # star-shaped about its own centroid — it is a palm — so integrating r dtheta over the
    # boundary radius is exact enough, monotone by construction, and cannot scramble.
    th = np.arctan2(bv - cv, bu - cu)
    rad = np.hypot(bu - cu, bv - cv)
    NB = 720
    bi_ = np.clip(((th + math.pi) / (2 * math.pi) * NB).astype(int), 0, NB - 1)
    rsum = np.bincount(bi_, rad, minlength=NB)
    rcnt = np.bincount(bi_, minlength=NB)
    rbin = np.where(rcnt > 0, rsum / np.maximum(rcnt, 1), np.nan)
    ok = ~np.isnan(rbin)
    rbin = np.interp(np.arange(NB), np.nonzero(ok)[0], rbin[ok], period=NB)
    dth = 2 * math.pi / NB
    cum = np.concatenate([[0.0], np.cumsum(rbin * dth)])
    perim = float(cum[-1])
    U, V = np.meshgrid(uu, vv, indexing='ij')
    fu, fv = U.ravel(), V.ravel()
    dist = np.empty(len(fu))
    CH = 8000
    for a in range(0, len(fu), CH):
        b = min(a + CH, len(fu))
        dist[a:b] = np.sqrt(np.min((fu[a:b, None] - bu[None, :]) ** 2 +
                                   (fv[a:b, None] - bv[None, :]) ** 2, axis=1))
    dist = dist.reshape(U.shape) * np.where(inside, 1.0, -1.0)
    log(f"seam: {len(bu)} boundary cells, perimeter {perim:.0f} mm")
    return dist, perim, (cu, cv), cum, rbin


# ----------------------------------------------------------------- atlas + rasterisation
A0, W, H = load_atlas(SRC_PNG, log=log)
if CACHE and os.path.exists(CACHE):
    z = np.load(CACHE)
    tw, tp, tn, tfp, used = z["tw"], z["tp"], z["tn"], z["tfp"], z["used"]
    log(f"rasterisation read from cache {CACHE}")
else:
    tw, tp, tn, tfp, used, _ = rasterise(FBXS, W, H, log=log)
    if CACHE:
        np.savez_compressed(CACHE, tw=tw, tp=tp, tn=tn, tfp=tfp, used=used)
        log(f"rasterisation cached to {CACHE}")
palm = tw > 0.0
idx = np.nonzero(palm)
pts = tp[idx].astype(np.float64)          # mm, the 3-D point each palm texel sits on
nrm = tn[idx].astype(np.float64)
wv = tw[idx].astype(np.float64)
fp_raw = np.maximum(tfp[idx].astype(np.float64), 0.05)   # mm of hand per texel, per triangle
log(f"palm texels {len(pts)}, {int((wv > 0.99).sum())} at full weight")

# ----------------------------------------------------------------- the palm's own plane
# The rig contract frame IS the palm frame: x across the hand, z toward the fingers, -y out of
# the palm. No fitting needed, and no fitting wanted — a fitted frame would drift between the
# two hands and the pattern would not land in the same place on both.
u, v, yy = pts[:, 0], pts[:, 2], pts[:, 1]

# WHICH TEXELS ARE THE PALM FACE, and which have already turned round the side of the hand? The
# region wraps onto the thenar flank and the pinky edge, and those project onto the same (x, z)
# as the face itself, so the layout grid alone cannot tell them apart. Only the face gets
# leather.
#
# The first attempt measured "how far behind the frontmost surface in this column" — a depth map
# along -y, the same trick palm_region uses to decide the region. It is WRONG at this scale. A
# depth map's cell is 2 mm wide and the palm's own slope inside one cell is of the same order, so
# the measure reports 2-6 mm of false depth everywhere the palm is not flat-on: the rendered
# field came out as a black band across the middle of the palm and a checkerboard of the grid
# cells, and it tore the leather mask apart. The palm's own curvature was being read as
# occlusion. palm_region can afford the trick because it allows 7 mm before it reacts; a design
# edge cannot.
#
# Use the SURFACE NORMAL instead, spatially averaged first. Raw normals cannot be trusted on this
# shell — it is non-manifold with overlapping fragments, and scattered vertices on the BACK carry
# an area-weighted normal that points palmward — but averaging them over a 4 mm ball on the hand
# drowns any single bad fragment, which is exactly what palm_region does for its own normal gate.
ns = surface_blur(nrm, pts, NSMOOTH_MM)
ns /= np.maximum(np.linalg.norm(ns, axis=1, keepdims=True), 1e-20)
F = smoothstep((-ns[:, 1] - FACE_N0) / (FACE_N1 - FACE_N0))   # 1 = palm face, 0 = the sides
log(f"palm face: {int((F > 0.5).sum())} of {len(F)} texels face the palm direction "
    f"(-n.y > {(FACE_N0 + FACE_N1) / 2:.2f}) after a {NSMOOTH_MM:.0f} mm normal average")

# ----------------------------------------------------------------- the leather's panel
# The palm region itself, smoothed, inset, face-only, and cut off above the wrist. See the
# design note at the head of this block of constants for why it is not an authored blob.
u0, v0 = u.min() - 25.0, v.min() - 25.0
nu = int((u.max() + 25.0 - u0) / CELL) + 1
nv = int((v.max() + 25.0 - v0) / CELL) + 1
UU = u0 + np.arange(nu) * CELL
VV = v0 + np.arange(nv) * CELL
guf = (u - u0) / CELL                      # fractional cell coordinates, sampled bilinearly
gvf = (v - v0) / CELL
GU, GV = np.meshgrid(UU, VV, indexing='ij')

B = bone_heads(FBXS[-1], log=log)
wr = np.array([B["Anchor_Wrist"][0], B["Anchor_Wrist"][2]])


def xz(name):
    return np.array([B[name][0], B[name][2]])


def blur2(a, sig):
    return blur_axes(a, sig, (0, 1))


# The region, face-only, closed (the rasteriser leaves single-cell pinholes) then smoothed hard.
# Seeded on a 1 mm grid even though the field is 0.5 mm: the atlas runs 3 texels/mm, so at 0.5 mm
# a cell holds less than one texel on average and the seed mask comes out full of holes that the
# closing then has to guess at — the panel lost 1200 mm^2 the first time this was tried. The
# smoothed 1 mm field is upsampled bilinearly instead, which puts its 0.5 contour between cells.
MC = 1.0
nuc, nvc = int(nu * CELL / MC) + 2, int(nv * CELL / MC) + 2
seed_m = (wv > 0.50) & (F > 0.30)
mask = np.zeros((nuc, nvc), np.float32)
np.maximum.at(mask, (np.clip(((u[seed_m] - u0) / MC).astype(int), 0, nuc - 1),
                     np.clip(((v[seed_m] - v0) / MC).astype(int), 0, nvc - 1)), 1.0)
mregc = (blur2(mask, 1.5 / MC) > 0.28).astype(np.float32)
mregc = blur2(mregc, REG_SMOOTH / MC)
FI, FJ = np.meshgrid(np.arange(nu) * CELL / MC, np.arange(nv) * CELL / MC, indexing='ij')
mreg = bilinear(mregc, FI, FJ) > 0.5
sd_reg = edt_signed(mreg, UU, VV, log=log)[0]

# the wrist cut, as a signed distance of its own, so the two corners where it meets the side
# edges come out rounded rather than mitred
sd_wrist = GV - (WRIST_Z + WRIST_BOW * GU ** 2)
D = -CORNER_K * np.log(np.exp(-(sd_reg - CLIP_MARGIN) / CORNER_K) +
                       np.exp(-sd_wrist / CORNER_K))
D = blur2(D, FIELD_SMOOTH / CELL)
area = float((D > 0).sum()) * CELL * CELL
log(f"leather panel: {area:.0f} mm^2 of hand, inset {CLIP_MARGIN:.1f} mm from the region edge")
if area < 4000:
    raise SystemExit("palm_leather: the leather patch came out tiny — the layout is wrong")

# ----------------------------------------------------------------- distance to the edge, and
# ----------------------------------------------------------------- arc length along it
dist, PERIM, (cu, cv), CUM, RBIN = edt_signed(D > 0, UU, VV, log=log)
dist = blur2(dist, FIELD_SMOOTH / CELL)
d = bilinear(dist, guf, gvf)
# the position along the seam comes from the texel's OWN polar angle, not from the arc length of
# whichever boundary cell happens to be nearest. The seam is star-shaped about its centroid, so
# the two agree, but only this one is continuous: reading it off the nearest cell made the stitch
# dashes come out different lengths all the way round.
NBH = len(RBIN)
sarc = np.interp((np.arctan2(v - cv, u - cu) + math.pi) / (2 * math.pi) * NBH,
                 np.arange(NBH + 1), CUM)
log(f"edge distance: {int((d > 0).sum())} texels inside the leather, "
    f"max inset {d.max():.0f} mm")

# ----------------------------------------------------------------- the steel base
# A 3-D normalised convolution of the palm's current colour. Two jobs: it is the wrist plate and
# the band between the leather and the finger plates, and it is what erases the MCP stipple that
# the previous pass's weight-proportional blend left standing.
col = A0[idx][:, :3].astype(np.float64)
steel = surface_blur(col, pts, BLUR_MM)
log(f"steel base: 3-D blur sigma {BLUR_MM:.0f} mm, spread sd "
    f"{col.std(0).mean():.4f} -> {steel.std(0).mean():.4f}")

# ----------------------------------------------------------------- 3-D noise, island-blind
rng = np.random.default_rng(SEED)
nlo = pts.min(0) - 4.0
nspan = pts.max(0) - pts.min(0) + 8.0


def value_noise(p, cell):
    nn = np.maximum((nspan / cell).astype(int) + 2, 2)
    lat = rng.random((nn[0], nn[1], nn[2])).astype(np.float32) - 0.5
    f = (p - nlo) / cell
    i = np.floor(f).astype(int)
    t = f - i
    t = t * t * (3 - 2 * t)
    i = np.clip(i, 0, nn - 2)
    out = np.zeros(len(p))
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                wgt = ((t[:, 0] if dx else 1 - t[:, 0]) *
                       (t[:, 1] if dy else 1 - t[:, 1]) *
                       (t[:, 2] if dz else 1 - t[:, 2]))
                out += wgt * lat[i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz]
    return out / 0.25      # roughly unit-ish


# a 4th-power mean, not an average: the filter must follow the WORST texel in the neighbourhood,
# and a plain mean let a starved triangle borrow resolution from the fine ones next to it (it
# reported 1.6 mm where the raw measure said 41, and the comb survived).
fp = surface_blur(fp_raw ** 4, pts, 2.5, cell=1.5) ** 0.25
log(f"texel footprint, 2.5 mm quartic mean: median {np.median(fp):.2f} mm, p90 {np.percentile(fp, 90):.2f}, "
    f"p99 {np.percentile(fp, 99):.2f}, max {fp.max():.2f}")


def resolvable(cell):
    """How much of a feature of this size the atlas can actually hold, per texel.

    The palm's texel footprint runs from 0.2 mm round the rim to over 4 mm across the big
    decimator triangles in the middle. Painting a 1 mm grain into a 2 mm texel does not make
    fine leather, it makes a comb: the first pass came out striped across exactly those
    triangles. So every octave is faded out where it has fewer than ~4 samples per cell —
    proper texture filtering, done at author time because there is no mip chain that can undo
    it later. The coarse octaves survive everywhere, so the middle of the palm keeps its tone
    and loses only detail that was never going to survive the resampling.

    The footprint used here is the RMS of the raw per-triangle one over a 3 mm ball on the hand.
    Unsmoothed it steps at every triangle edge, the noise amplitude steps with it, and the
    render grows hard triangular seams — which is the artefact this filter exists to remove.
    """
    return smoothstep((cell / fp - 2.6) / 2.2)


def res_band(width_mm):
    """The same filter for a deliberate BAND — a stitch, the lip, a rivet head. A 2 mm line
    drawn across a triangle that samples every 5 mm is not a line, it is a row of teeth, and
    the residual comb in the middle of the palm turned out to be exactly the two flex creases
    after the noise had already been filtered.

    Gentler than the filter on NOISE, and deliberately so. An under-sampled noise field is a
    comb and has to go; an under-sampled line is a line of uneven dashes, which on a stitch seam
    is indistinguishable from hand sewing. The strict curve deleted the stitching and the gold
    off the rivet heads round the bottom of the panel and left their sockets standing, which
    looked far worse than the aliasing it was preventing.
    """
    return smoothstep((width_mm * 2.4 / fp - 1.0) / 1.2)


def band_noise(cell):
    """One octave of 3-D value noise, faded where the atlas cannot resolve it."""
    nz = value_noise(pts, cell)
    return nz / max(np.abs(nz).std(), 1e-9) * resolvable(cell)


def crease(p, cell, width):
    """A network of thin dark lines along a noise field's zero set — leather grain, not blur."""
    nz = value_noise(p, cell)
    nz = nz / max(np.abs(nz).std(), 1e-9)
    return (1.0 - smoothstep(np.abs(nz) / width)) * resolvable(cell)


# ----------------------------------------------------------------- the steel, dressed
# brushed streaks along the arm: noise that is long in z and short in x, so it reads as the
# direction the plate was polished in rather than as dirt
brush = value_noise(np.stack([pts[:, 0], pts[:, 1], pts[:, 2] * 0.10], 1), 0.9)
brush = brush / max(np.abs(brush).std(), 1e-9) * resolvable(0.9)
grain = (band_noise(1.8) + 0.5 * band_noise(5.0) + 0.25 * band_noise(13.0))
metal = steel * (1.0 + 0.022 * brush[:, None] + 0.040 * grain[:, None])

# ----------------------------------------------------------------- the leather, authored
lg = (0.50 * crease(pts, 2.6, 0.30) + 0.35 * crease(pts, 1.3, 0.34)
      + 0.42 * crease(pts, 5.5, 0.26) + 0.30 * crease(pts, 11.0, 0.22))   # 1 = in a grain crease
pebble = 0.6 * band_noise(3.4) + 0.7 * band_noise(8.0) + 0.6 * band_noise(16.0)

# wear: burnished where a hand actually presses — the four pads behind the knuckles, the thenar
# mound at the base of the thumb, the heel at the wrist, and the hollow between them that never
# touches anything. Laid out on the RIG's landmarks, not on literal coordinates, so it lands on
# the anatomy rather than near it.
discs = []
for f in ("Index", "Middle", "Ring", "Pinky"):
    c = xz(f"Anchor_{f}_Root")
    discs.append((c + (wr - c) * PAD_PULL, PAD_R))
th = xz("Anchor_Thumb_Root")
discs.append((th + (wr - th) * THENAR_PULL, THENAR_R))
mid = np.mean([xz(f"Anchor_{f}_Root") for f in ("Index", "Middle", "Ring", "Pinky")], 0)
discs.append((mid * 0.42 + wr * 0.58, HOLLOW_R))
discs.append((wr + np.array([0.0, HEEL_UP]), HEEL_R))
log("wear lobes (x, z, r) mm: " + ", ".join(f"({c[0]:.0f},{c[1]:.0f},{r:.0f})" for c, r in discs))


def lobe(c, r):
    return np.exp(-((u - c[0]) ** 2 + (v - c[1]) ** 2) / (r * r))


wear = np.zeros(len(u))
for c, r in discs[:4]:                       # the finger pads
    wear = np.maximum(wear, 0.90 * lobe(c, r * 0.95))
wear = np.maximum(wear, 0.80 * lobe(discs[4][0], discs[4][1] * 0.85))    # thenar
wear = np.maximum(wear, 0.62 * lobe(discs[6][0], discs[6][1] * 0.80))    # heel
hollow = np.clip(lobe(discs[5][0], discs[5][1] * 0.80) - wear, 0.0, 1.0)  # the cup, darker

# a broad dirt/patina drift so the hide is not one flat colour over 130 mm
patina = band_noise(22.0)

# THE FLEX CREASES. Two of them, where a hand folds: the transverse crease under the knuckles
# and the thenar crease round the base of the thumb. This is the one mark that says HIDE rather
# than "a brown area" — leather takes a permanent fold where it is bent every day, and the
# shader is pure Lambert with no normal map, so if it is not painted it does not exist. Each is
# a dark valley with a lit lip on the wrist side of it: painted depth, the same trick the back
# of the hand uses on every plate boundary.
def spline(ctrl, n=200):
    ctrl = np.asarray(ctrl, float)
    t = np.linspace(0, 1, n)[:, None]
    if len(ctrl) == 3:                      # quadratic Bezier
        return ((1 - t) ** 2 * ctrl[0] + 2 * (1 - t) * t * ctrl[1] + t ** 2 * ctrl[2])
    return ((1 - t) ** 3 * ctrl[0] + 3 * (1 - t) ** 2 * t * ctrl[1] +
            3 * (1 - t) * t ** 2 * ctrl[2] + t ** 3 * ctrl[3])


def toward(a, b, f):
    return a + (b - a) * f


creases = [
    # under the knuckles, pinky side to the index/middle web
    spline([toward(xz("Anchor_Pinky_Root"), wr, 0.34),
            toward(xz("Anchor_Ring_Root"), wr, 0.30),
            toward(xz("Anchor_Middle_Root"), wr, 0.26),
            toward(xz("Anchor_Index_Root"), wr, 0.14)]),
    # round the thenar mound, from the thumb web down to the wrist
    spline([toward(xz("Anchor_Index_Root"), xz("Anchor_Thumb_Root"), 0.55),
            toward(discs[4][0], discs[5][0], 0.30),
            toward(wr, discs[4][0], 0.30)]),
]
cr = np.zeros(len(u))
crlip = np.zeros(len(u))
for cpts in creases:
    gd = np.sqrt(np.min((GU.ravel()[:, None] - cpts[None, :, 0]) ** 2 +
                        (GV.ravel()[:, None] - cpts[None, :, 1]) ** 2, axis=1)).reshape(GU.shape)
    e = np.hypot(GU - cpts[0, 0], GV - cpts[0, 1])
    f = np.hypot(GU - cpts[-1, 0], GV - cpts[-1, 1])
    fade = smoothstep(np.minimum(e, f) / 7.0)          # the fold dies out at its own ends
    dd = bilinear(gd, guf, gvf)
    ff = bilinear(fade, guf, gvf)
    rb = resolvable(CREASE_W * 2.2)      # a crease IS what combed: filter it strictly
    cr = np.maximum(cr, np.exp(-(dd / CREASE_W) ** 2) * ff * rb)
    crlip = np.maximum(crlip, np.exp(-((dd - CREASE_W * 1.9) / (CREASE_W * 1.1)) ** 2) * ff * rb)
log(f"flex creases: {len(creases)}, {int((cr > 0.4).sum())} texels in a fold")

leather = (LEATHER[None, :]
           * (1.0 + 0.34 * wear[:, None] - 0.16 * hollow[:, None] + 0.10 * patina[:, None])
           * (1.0 - 0.30 * lg[:, None] + 0.060 * pebble[:, None])
           * (1.0 - CREASE_DARK * cr[:, None] + CREASE_LIFT * crlip[:, None]))
# the leather darkens toward its own edge — it is let into a recess
leather *= (0.78 + 0.22 * smoothstep(d / 9.0))[:, None]

# ----------------------------------------------------------------- composite
new = metal.copy()

# 1. the leather patch, with an antialiased edge that the region weight never gets to soften
a_leather = smoothstep((d + 0.7) / 1.8) * smoothstep((F - 0.25) / 0.35)
new = new * (1 - a_leather[:, None]) + leather * a_leather[:, None]

# 2. the rolled steel lip OUTSIDE the leather: brightest a third of the way out, dying at the
#    leather edge and again at its own crown. Painted depth — the back of the hand does the same
#    thing on every plate boundary and it is the only depth this material can have.
lip = np.exp(-((d + LIP_MM * 0.5) / (LIP_MM * 0.5)) ** 2) * (d < 0.4) * res_band(LIP_MM)
foot = np.exp(-((d + LIP_FOOT) / 1.7) ** 2) * res_band(1.7)
new = new * (1.0 - 0.22 * foot)[:, None]
new = new + (LIP_GAIN * lip)[:, None] * np.maximum(metal, 0.16) * np.array([1.0, 0.99, 0.96])

# 3. the contact shadow INSIDE the leather edge
sh = np.exp(-((d - 0.4) / SHADOW_MM) ** 2) * (d > -0.4) * res_band(SHADOW_MM)
new = new * (1.0 - SHADOW_GAIN * sh)[:, None]

# 4. the stitch line: dashes of waxed thread following the seam, each with its own little
#    shadow on the far side so it does not read as a painted stripe
ph = (sarc / STITCH_PERIOD) % 1.0
dash = smoothstep((STITCH_DUTY - np.abs(ph - 0.5) * 2.0) / 0.22)
band = np.exp(-((d - STITCH_D) / STITCH_W) ** 2)
bandsh = np.exp(-((d - STITCH_D + STITCH_W * 1.5) / (STITCH_W * 1.1)) ** 2)
a_st = dash * band * a_leather * res_band(STITCH_W * 2)
new = new * (1.0 - 0.35 * (dash * bandsh * a_leather * res_band(STITCH_W * 2)))[:, None]
new = new * (1 - a_st[:, None]) + STITCH[None, :] * a_st[:, None]

# 5. gold rivets through the leather into the plate beneath, evenly along the seam
nriv = max(3, int(round(PERIM / RIVET_PERIOD)))
step = PERIM / nriv
# invert the polar arc-length integral to get the seam point at each target arc length, then
# pull it inward along its own radius — the seam is star-shaped about the centroid, so the
# radius IS the inward normal to within a couple of degrees, and it carries none of the noise a
# finite-difference gradient would pick up off a 1 mm mask
NB = len(RBIN)
tbin = np.interp((np.arange(nriv) + 0.5) * step, CUM, np.arange(NB + 1))
rth = tbin / NB * 2 * math.pi - math.pi
rr = np.interp(tbin, np.arange(NB + 1), np.append(RBIN, RBIN[0])) - RIVET_D
ru = cu + rr * np.cos(rth)
rv = cv + rr * np.sin(rth)
rd = np.min(np.hypot(u[:, None] - ru[None, :], v[:, None] - rv[None, :]), 1)
socket = np.exp(-((rd - RIVET_R * 1.35) / (RIVET_R * 0.5)) ** 2) * 0.45 * res_band(RIVET_R)
head = smoothstep((RIVET_R - rd) / 0.7)
dome = np.clip(1.0 - (rd / RIVET_R) ** 2, 0.0, 1.0) ** 0.5
gold = GOLD[None, :] * (0.72 + 0.28 * dome[:, None]) + (GOLD_HI - GOLD)[None, :] * \
    (smoothstep((0.55 * RIVET_R - rd) / (0.6 * RIVET_R)) ** 2)[:, None]
a_riv = head * smoothstep((F - 0.25) / 0.35) * res_band(RIVET_R)
new = new * (1.0 - (socket * smoothstep((F - 0.25) / 0.35))[:, None])
new = new * (1 - a_riv[:, None]) + gold * a_riv[:, None]
log(f"rivets: {nriv} at {step:.1f} mm along the seam, {RIVET_D:.0f} mm inside it")

# ----------------------------------------------------------------- write it back
# A FIRM blend curve, not the palm weight itself. The weight ramp is ~7 mm wide at the MCP
# creases and blending by it is what left the original stipple showing through at 30-100 % —
# the grime in the screenshot. Full paint from 0.18 up puts the handover ~1.5 mm inside the
# region edge, where what is painted is the smoothed steel and the join is invisible.
blend = smoothstep((wv - W_ON) / (W_FULL - W_ON))
A1 = A0.copy()
A1[idx[0], idx[1], :3] = np.clip(col * (1 - blend[:, None]) + new * blend[:, None], 0.0, 1.0)
log(f"blend: {int((blend > 0.999).sum())} texels fully painted, "
    f"{int(((blend > 0.001) & (blend < 0.999)).sum())} in the {W_ON}-{W_FULL} handover")

if DEBUG:
    dbg = np.zeros_like(A1)
    dbg[..., 3] = 1.0
    dbg[used, :3] = 0.02
    fld = {"d": np.clip(d / 30.0 + 0.5, 0, 1), "arc": (sarc % 20.0) / 20.0,
           "F": F, "wear": wear, "blend": blend, "a_leather": a_leather,
           "fp": np.clip(fp / 3.0, 0, 1), "fpraw": np.clip(fp_raw / 3.0, 0, 1),
           "res1": resolvable(1.3), "res3": resolvable(3.4), "res8": resolvable(8.0),
           "grain": np.clip(lg, 0, 1), "pebble": np.clip(pebble * 0.5 + 0.5, 0, 1)}[DEBUG]
    dbg[idx[0], idx[1], :3] = np.stack([fld, fld, fld], -1)
    save_atlas(dbg, W, H, DST_PNG.replace(".png", f"_dbg_{DEBUG}.png"), log=log)

gutter_fill(A1, used, PAD, log=log)
assert_outside_untouched(A0, A1, used, palm, log=log, tag="palm_leather")
save_atlas(A1, W, H, DST_PNG, alpha=(A0.shape[2] == 4), log=log)
