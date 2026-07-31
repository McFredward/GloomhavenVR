# palm_paint.py — plain python (needs PIL + numpy; Blender's bundled python has no PIL,
# which is why this half lives outside Blender). Grows VRHandPlate_albedo.png from 2048 to
# 4096 and paints the palm islands that palm_reunwrap.py carved out.
#
# THE ATLAS GROWS, NOTHING ELSE MOVES
#   The old 2048 image is pasted 1:1 into the bottom-left quadrant and every old uv was
#   multiplied by 0.5, so every untouched island samples the SAME texels at the SAME mip
#   level (uv derivative in texels is unchanged: 0.5*du * 4096 == du * 2048). No resampling,
#   no recompression drift — the block-compressed 4x4 tiles line up too, because 2048 is a
#   multiple of 4. The script asserts the quadrant is bit-identical before it saves.
#
# WHAT GOES INTO THE PALM ISLAND
#   base   the OLD palm colour, blurred with a wide kernel IN ISLAND SPACE. This is the step
#          every earlier attempt could not take: the old palm was shredded into 3355 UV
#          islands, so blurring in atlas space could never bring two neighbouring wedges into
#          one kernel. The new island is a single flattening of the palm, so a blur there is
#          a blur ON THE HAND. At sigma ~30 mm the baked wedge steps dissolve completely and
#          what survives is the palm's genuine large-scale shading (darker in the hollow and
#          along the creases), which is what makes the patch still sit in the hand.
#   detail a luminance-only high-pass lifted from the HEALTHY plate areas of the same atlas
#          and quilted over the island with random flips and offsets. Luminance-only on
#          purpose: a per-channel ratio would replay the gold rivets as coloured blobs.
#   seam   within FEATHER texels of the island border the result cross-fades back to the
#          exact old colour, so the boundary with the untouched shell is continuous. The
#          border runs along the MCP creases and the cuff rim, where a texture change is
#          invisible anyway.
#
# RUN:
#   python3 unity/hand-prep/palm_paint.py <old_2048.png> <out_4096.png> <a.npz> [<b.npz> ...]
#     [--sigma-mm 30] [--density 4.0] [--gain 0.85] [--feather 14] [--seed 7]
import sys
import numpy as np
from PIL import Image

args = []
skip = False
for i, a in enumerate(sys.argv[1:]):
    if skip:
        skip = False
        continue
    if a.startswith("--"):
        skip = True
        continue
    args.append(a)
opts = sys.argv[1:]


def getopt(name, default, cast=float):
    return cast(opts[opts.index(name) + 1]) if name in opts else default


OLD, OUT, NPZS = args[0], args[1], args[2:]
SIGMA_MM = getopt("--sigma-mm", 30.0)
DENSITY = getopt("--density", 4.0)
GAIN = getopt("--gain", 0.85)
FEATHER = getopt("--feather", 14.0)
SEED = getopt("--seed", 7, int)
# The old atlas is only 37 % real surface — the rest is the baker's dilation halo — so a
# source tile has to be small to fit wholly inside an island. 64 px is the largest size that
# still yields thousands of candidates.
TILE = getopt("--tile", 64, int)
HP_SIGMA = getopt("--hp", 11.0)   # high-pass cutoff, texels (~2.7 mm at 4 texels/mm)
RCLIP = (getopt("--rmin", 0.78), getopt("--rmax", 1.30))   # no holes, no blown blobs
ZOOM = getopt("--zoom", 4, int)   # magnification of the second detail octave
COARSE = getopt("--coarse", 0.8)  # weight of that octave
DUMP = opts[opts.index("--dump-tiles") + 1] if "--dump-tiles" in opts else None
PAD = 8                      # texels of edge dilation around the island (mip/bilinear guard)


def log(*a):
    print("[palm_paint]", *a)


rng = np.random.default_rng(SEED)
old = np.asarray(Image.open(OLD).convert("RGB"), np.uint8)
OP = old.shape[0]
assert old.shape[1] == OP, "old atlas must be square"
NP_ = OP * 2
oldf = old.astype(np.float32) / 255.0
log(f"old atlas {OP}x{OP} -> new {NP_}x{NP_}")


# ------------------------------------------------------------------ small image helpers
def box(a, r):
    """Separable box blur, radius r, edge-clamped. a is 2-D or 3-D float."""
    if r < 1:
        return a.copy()
    def one(x):
        n = x.shape[0]
        pad = np.concatenate([np.repeat(x[:1], r, 0), x, np.repeat(x[-1:], r, 0)], 0)
        c = np.cumsum(pad, 0, dtype=np.float64)
        c = np.concatenate([np.zeros((1,) + c.shape[1:], np.float64), c], 0)
        return ((c[2 * r + 1:] - c[:-(2 * r + 1)]) / (2 * r + 1)).astype(np.float32)
    return one(one(a).swapaxes(0, 1)).swapaxes(0, 1)


def gauss(a, sigma, passes=3):
    """Gaussian approximated by repeated box blurs (van Vliet): r ~ sigma*sqrt(12/n)/2."""
    if sigma <= 0:
        return a.copy()
    r = max(1, int(round(sigma * (12.0 / passes) ** 0.5 / 2.0)))
    for _ in range(passes):
        a = box(a, r)
    return a


def bilinear(img, u, v):
    """Sample img (H,W,3 float) at uv in [0,1]^2, v measured from the BOTTOM."""
    h, w = img.shape[:2]
    x = np.clip(u * w - 0.5, 0, w - 1)
    y = np.clip((1.0 - v) * h - 0.5, 0, h - 1)
    x0 = np.floor(x).astype(np.int32); y0 = np.floor(y).astype(np.int32)
    x1 = np.minimum(x0 + 1, w - 1); y1 = np.minimum(y0 + 1, h - 1)
    fx = (x - x0)[:, None]; fy = (y - y0)[:, None]
    return ((img[y0, x0] * (1 - fx) + img[y0, x1] * fx) * (1 - fy) +
            (img[y1, x0] * (1 - fx) + img[y1, x1] * fx) * fy)


def rim_distance(mask, maxd):
    """Distance in texels from each mask pixel to the nearest non-mask pixel, saturating at
    maxd. Iterated 4-neighbour erosion — only the first `maxd` rings matter for the feather,
    so an exact EDT would be wasted work."""
    d = np.zeros(mask.shape, np.float32)
    cur = mask.copy()
    for i in range(int(maxd)):
        e = cur.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            e &= np.roll(np.roll(cur, dy, 0), dx, 1)
        e[0] = False; e[-1] = False; e[:, 0] = False; e[:, -1] = False
        d[e] = i + 1
        cur = e
    return d


# --------------------------------------------------------- pick healthy plate source tiles
zs = [np.load(p) for p in NPZS]
cov = np.zeros((OP, OP), bool)
for z in zs:
    cov |= np.unpackbits(z["cov"])[:OP * OP].reshape(OP, OP).astype(bool)
log(f"old-atlas UV coverage (both hands): {100.0 * cov.mean():.1f} %")

lum_old = oldf @ np.array([0.299, 0.587, 0.114], np.float32)
hp_ref = gauss(lum_old, HP_SIGMA)
detail_e = np.abs(lum_old - hp_ref)
big = gauss(lum_old, 40.0)                # scale of the baker's inter-island halo blobs
mx = oldf.max(2); mn = oldf.min(2)
sat = (mx - mn) / np.maximum(mx, 1e-4)

step = max(8, TILE // 4)
cands = []
for y in range(0, OP - TILE, step):
    for x in range(0, OP - TILE, step):
        if not cov[y:y + TILE, x:x + TILE].all():
            continue                      # any halo texel drags a dark blob into the palm
        L = lum_old[y:y + TILE, x:x + TILE]
        S = sat[y:y + TILE, x:x + TILE]
        E = detail_e[y:y + TILE, x:x + TILE]
        if L.mean() < 0.18 or L.mean() > 0.55:
            continue                      # too dark (leather) or blown out
        if S.mean() > 0.20 or (S > 0.42).mean() > 0.001:
            continue                      # gold rivets / brass buckles
        if (np.abs(L - hp_ref[y:y + TILE, x:x + TILE]) > 0.20).mean() > 0.004:
            continue                      # rivet heads, holes, hard island edges
        cands.append((float(E.mean()), x, y))
if len(cands) < 8:
    raise SystemExit(f"only {len(cands)} usable plate tiles — loosen the tile filter")
# Rank by high-pass energy but take a BAND, not the top: the very strongest tiles are plate
# BORDERS, and quilting those repeats a hard edge across the palm. The band below them is
# plate surface with real grain.
cands.sort(reverse=True)
lo = int(0.05 * len(cands))
cands = cands[lo:lo + 160]
log(f"detail source: {len(cands)} fully-covered plate tiles of {TILE}px, "
    f"high-pass energy {cands[-1][0]:.4f}..{cands[0][0]:.4f}")
if DUMP:
    sheet = np.zeros(((len(cands) + 15) // 16 * TILE, 16 * TILE, 3), np.uint8)
    for i, (e, x, y) in enumerate(cands):
        sheet[(i // 16) * TILE:(i // 16 + 1) * TILE,
              (i % 16) * TILE:(i % 16 + 1) * TILE] = old[y:y + TILE, x:x + TILE]
    Image.fromarray(sheet).save(DUMP)
    log("wrote tile sheet", DUMP)

# ratio field per tile: lum / blur(lum), clipped
tiles = []
for e, x, y in cands:
    L = lum_old[y:y + TILE, x:x + TILE]
    r = L / np.maximum(gauss(L, HP_SIGMA), 1e-3)
    tiles.append(np.clip(r, RCLIP[0], RCLIP[1]).astype(np.float32))
tiles = np.array(tiles)


def quilt(h, w, zoom=1):
    """Cover an h x w field with the source ratio tiles: random tile, random flip/rot,
    random offset, cross-faded on a 25 % overlap so there is no seam and no obvious repeat.
    zoom > 1 magnifies the tiles first, which turns the same grain into plate-scale mottling."""
    T = TILE * zoom
    ov = T // 4
    stepq = T - ov
    acc = np.zeros((h + T, w + T), np.float32)
    wsum = np.zeros((h + T, w + T), np.float32)
    ramp = np.minimum(np.arange(T) + 1, T - np.arange(T))
    ramp = np.clip(ramp / float(ov), 0, 1).astype(np.float32)
    win = np.outer(ramp, ramp)
    for yy in range(0, h + ov, stepq):
        for xx in range(0, w + ov, stepq):
            t = tiles[rng.integers(len(tiles))]
            t = np.rot90(t, rng.integers(4))
            if rng.integers(2):
                t = t[::-1]
            if rng.integers(2):
                t = t[:, ::-1]
            if zoom > 1:
                t = np.repeat(np.repeat(t, zoom, 0), zoom, 1)
                t = box(t, zoom)           # kill the nearest-neighbour blockiness
            acc[yy:yy + T, xx:xx + T] += t * win
            wsum[yy:yy + T, xx:xx + T] += win
    out = acc[:h, :w] / np.maximum(wsum[:h, :w], 1e-6)
    # overlap-averaging flattens contrast; put it back so the quilt keeps the source's bite
    m = out.mean()
    src_std = float(np.std(tiles))
    out = m + (out - m) * min(2.2, src_std / max(float(out.std()), 1e-6))
    return np.clip(out, RCLIP[0], RCLIP[1])


# ------------------------------------------------------------------------ compose the atlas
# The three freed quadrants are filled with the atlas's own median plate colour rather than
# black: deep mip levels average across island boundaries, and a black neighbour would darken
# the palm's rim at distance. A neutral neighbour cannot be seen doing it.
fill = np.median(old[cov], axis=0).astype(np.uint8)
new = np.tile(fill, (NP_, NP_, 1)).astype(np.uint8)
new[OP:, :OP] = old                       # uv [0,.5]^2 == bottom-left quadrant
log(f"unused atlas filled with the median plate colour {tuple(int(c) for c in fill)}")

covered = np.zeros((NP_, NP_), bool)
for npz, z in zip(NPZS, zs):
    ys, xs = z["ys"].astype(np.int64), z["xs"].astype(np.int64)
    olduv, pos = z["olduv"], z["pos"]
    assert int(z["newpx"][0]) == NP_ and int(z["oldpx"][0]) == OP

    y0, y1 = ys.min(), ys.max() + 1
    x0, x1 = xs.min(), xs.max() + 1
    h, w = y1 - y0, x1 - x0
    log(f"{npz}: {len(ys)} texels, island bbox {w}x{h} at ({x0},{y0})")

    mask = np.zeros((h, w), bool)
    mask[ys - y0, xs - x0] = True
    oldc = np.zeros((h, w, 3), np.float32)
    oldc[ys - y0, xs - x0] = bilinear(oldf, olduv[:, 0], olduv[:, 1])

    # base: wide blur of the old colour, IN ISLAND SPACE, normalised by the coverage mask
    mf = mask.astype(np.float32)
    sig = SIGMA_MM * DENSITY
    num = gauss(oldc * mf[:, :, None], sig)
    den = gauss(mf, sig)[:, :, None]
    base = num / np.maximum(den, 1e-5)

    # Two octaves: grain at 1:1 and the same grain magnified into plate-scale mottling, so
    # the palm reads as beaten steel rather than as a flat plastic shell.
    r1 = quilt(h, w, 1)
    r2 = quilt(h, w, ZOOM)
    d = GAIN * ((r1 - 1.0) + COARSE * (r2 - 1.0))
    out = base * (1.0 + d[:, :, None])
    log(f"  detail: fine std {r1.std():.3f}, coarse(x{ZOOM}) std {r2.std():.3f}, "
        f"combined +-{np.percentile(np.abs(d), 99) * 100:.1f} % at p99")

    # feather back to the exact old colour at the island border (seam continuity)
    dist = rim_distance(mask, FEATHER + 1)
    f = np.clip(dist / FEATHER, 0, 1)[:, :, None]
    f = f * f * (3 - 2 * f)
    out = f * out + (1 - f) * oldc

    out = np.clip(out, 0, 1)
    px = (out * 255.0 + 0.5).astype(np.uint8)

    # dilate the island a few texels so bilinear/mip filtering never reaches outside content
    filled = mask.copy()
    cur = px.copy()
    for _ in range(PAD):
        nxt = cur.copy(); nf = filled.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            src = np.roll(np.roll(cur, dy, 0), dx, 1)
            sf = np.roll(np.roll(filled, dy, 0), dx, 1)
            take = sf & ~nf
            nxt[take] = src[take]; nf |= take
        cur, filled = nxt, nf

    sub = new[max(y0 - PAD, 0):y1 + PAD, max(x0 - PAD, 0):x1 + PAD]
    oy, ox = y0 - max(y0 - PAD, 0), x0 - max(x0 - PAD, 0)
    reg = np.zeros(sub.shape[:2], bool)
    reg[oy:oy + h, ox:ox + w] = filled
    tmp = np.zeros_like(sub)
    tmp[oy:oy + h, ox:ox + w] = cur
    if covered[max(y0 - PAD, 0):y1 + PAD, max(x0 - PAD, 0):x1 + PAD][reg].any():
        raise SystemExit("island overlaps a previously painted island")
    sub[reg] = tmp[reg]
    covered[max(y0 - PAD, 0):y1 + PAD, max(x0 - PAD, 0):x1 + PAD] |= reg
    log(f"  painted {int(filled.sum())} texels (island + {PAD} px of padding); "
        f"base tone {base[mask].mean():.3f}")

if np.any(covered[OP:, :OP]):
    raise SystemExit("a palm island landed inside the preserved old-atlas quadrant")
if not np.array_equal(new[OP:, :OP], old):
    raise SystemExit("the preserved quadrant is not bit-identical to the old atlas")
log(f"preserved quadrant verified bit-identical ({OP}x{OP} = {OP*OP} texels)")

Image.fromarray(new).save(OUT, optimize=True)
log("wrote", OUT)
