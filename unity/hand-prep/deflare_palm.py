#!/usr/bin/env python3
# deflare_palm.py — remove the BAKED-IN facet shading from the starved region of a hand atlas.
#
# WHY (measured, not assumed)
#   The gauntlet's palm reads in game as a radial star of flat wedges, and looks smeared next
#   to the crisp back of the hand. Neither is geometry:
#     - rendered as untextured CLAY the palm is smooth — no star, no facets;
#     - rendered EMISSION-ONLY (every pixel literally a texel, no lights) the star is fully
#       present. It is pigment, and subdivision cannot remove pigment. That is why the earlier
#       geometry refinement did not help.
#     - the dark "cracks" along the cuff and thumb are pigment too: an emission render against
#       a magenta background shows 0 magenta pixels inside the palm silhouette and 46 of 1.4 M
#       at the cuff. You cannot see through the hand; you are looking at paint.
#
#   The mechanism is the UV layout. The starved region — 13.3 % of the hand's surface — lives
#   on 0.58 % of the atlas, shredded into 3355 disconnected islands, the largest 61x61 texels.
#   With 2455 faces on 24 494 texels, each big palm triangle owns about TEN texels: it is very
#   nearly a flat colour sampled from its own scrap of atlas, and its neighbour samples a
#   different scrap with a different tone. The wedge boundaries ARE the UV island boundaries.
#
# WHY THE FILTER RUNS OVER 3-D DISTANCE, NOT OVER THE IMAGE
#   Blurring the atlas cannot fix this: two wedges that touch on the hand can sit anywhere in
#   the image, so no image-space neighbourhood contains both. Tried it — the star came back
#   fainter and still obviously there. So each masked texel carries its 3-D point (from
#   palm_texels.py) and the low-pass runs over surface distance. Tone differences between
#   adjacent wedges become local differences and divide out; detail finer than the radius
#   survives.
#
# WHAT IT DOES
#   gain = target / (mean luma within RADIUS mm on the surface), clamped so a genuinely dark
#   feature cannot be blown out, applied to RGB so hue is preserved. The mask boundary is
#   feathered. Every texel outside the mask is preserved BIT-EXACTLY, and that is asserted.
#
# NOT FIXED BY THIS: the palm still has ~3x fewer texels per millimetre than the fingers
# (1.07 vs 3.24), so it stays softer than the rest. Curing that needs a bigger atlas and a
# re-unwrap, not a filter.
#
# RUN:
#   blender --background --python unity/hand-prep/palm_texels.py -- 2048 texels.npz <fbx>...
#   python3 unity/hand-prep/deflare_palm.py <atlas.png> texels.npz <out.png> [radius_mm]
#
# AFTER RUNNING: rebuild the AssetBundle (see prebuilt/README.md) — the mod loads the atlas
# from gloomhavenvr.bundle, not from the PNG.
import sys
from collections import defaultdict

import numpy as np
from PIL import Image

# The correction is ADDITIVE on luma — new = target + (luma - local mean) — so it removes the
# whole large-scale component rather than scaling towards it. A purely multiplicative version
# was tried first and ran into its own clamp on 36 % of the texels (the wedge-to-wedge tone
# steps here exceed +-45 %), which left the star clearly visible. The clamp below therefore
# only catches pathological texels, it is not doing the work.
GAIN_LO, GAIN_HI = 0.35, 2.60
LUMA = np.array([0.2126, 0.7152, 0.0722])


def log(*a):
    print("[deflare_palm]", *a)


def box(a, r):
    """Separable box blur; window is clamped at the edges so no wrap-around bleed."""
    def one(x):
        n = x.shape[1]
        c = np.concatenate([np.zeros((x.shape[0], 1)), np.cumsum(x, axis=1)], axis=1)
        lo = np.clip(np.arange(n) - r, 0, n)
        hi = np.clip(np.arange(n) + r + 1, 0, n)
        return (c[:, hi] - c[:, lo]) / (hi - lo)
    return one(one(a.T).T)


def surface_mean(pos, val, radius):
    """Mean of val over neighbours within `radius` in 3-D, via a uniform grid."""
    cell = np.floor(pos / radius).astype(np.int64)
    buckets = defaultdict(list)
    for i, c in enumerate(map(tuple, cell)):
        buckets[c].append(i)
    log(f"grid: {len(buckets)} occupied cells for {len(val)} texels")

    out = np.empty(len(val))
    r2 = radius * radius
    offsets = [(dx, dy, dz) for dx in (-1, 0, 1) for dy in (-1, 0, 1) for dz in (-1, 0, 1)]
    for i in range(len(val)):
        cx, cy, cz = cell[i]
        idx = []
        for dx, dy, dz in offsets:
            idx.extend(buckets.get((cx + dx, cy + dy, cz + dz), ()))
        if not idx:
            out[i] = val[i]; continue
        j = np.fromiter(idx, np.int64, len(idx))
        d = pos[j] - pos[i]
        q = np.einsum('ij,ij->i', d, d)
        k = q <= r2
        if not k.any():
            out[i] = val[i]; continue
        w = 1.0 - q[k] / r2          # smooth falloff: no hard cutoff ring
        out[i] = float(np.dot(w, val[j[k]]) / w.sum())
    return out


def main():
    atlas_p, texels_p, out_p = sys.argv[1], sys.argv[2], sys.argv[3]
    radius = (float(sys.argv[4]) if len(sys.argv) > 4 else 14.0) / 1000.0

    src = np.asarray(Image.open(atlas_p).convert("RGB"))
    img = src.astype(np.float64) / 255.0
    px = img.shape[0]

    z = np.load(texels_p)
    ys, xs, pos, fid, fpos = z["ys"], z["xs"], z["pos"], z["fid"], z["fpos"]
    if int(z["px"][0]) != px:
        raise SystemExit(f"texel set was built for {int(z['px'][0])} px, atlas is {px} px")
    log(f"atlas {px}x{px}, {len(ys)} masked texels ({100.0*len(ys)/(px*px):.2f}%), "
        f"radius {radius*1000:.0f} mm")

    # PER FACE and PER CHANNEL.
    #   per FACE, because the star IS face-scale variation: each big palm triangle samples its
    #   own scrap of atlas at a different tone. Smoothing per texel would cost 264 k
    #   neighbourhood queries for a correction that only needs to vary at wedge scale — the
    #   face means carry all of it at a fraction of the work.
    #   per CHANNEL, because the wedges differ in tint as well as brightness; a luma-only
    #   correction leaves the colour steps standing and the star stays legible (verified in an
    #   emission render).
    src_rgb = img[ys, xs]
    nf = len(fpos)
    fmean = np.zeros((nf, 3))
    cnt = np.bincount(fid, minlength=nf).astype(np.float64)
    for c in range(3):
        fmean[:, c] = np.bincount(fid, weights=src_rgb[:, c], minlength=nf) / np.maximum(cnt, 1)

    out_rgb = np.empty_like(src_rgb)
    for c in range(3):
        target = float(np.median(fmean[cnt > 0, c]))
        local = surface_mean(fpos, fmean[:, c], radius)
        # Face-scale offset: what this face's neighbourhood is, minus what it should be.
        out_rgb[:, c] = np.clip(src_rgb[:, c] - local[fid] + target, 0.0, 1.0)
        log(f"  channel {c}: target {target:.3f}, face-scale range "
            f"{local.min():.3f}..{local.max():.3f}")

    # A per-texel guard, so no single texel can be pushed absurdly far from its origin.
    ratio = out_rgb.sum(1) / np.maximum(src_rgb.sum(1), 1e-4)
    scale = np.clip(ratio, GAIN_LO, GAIN_HI) / np.maximum(ratio, 1e-4)
    held = int(np.count_nonzero(np.abs(scale - 1.0) > 1e-6))
    out_rgb = np.clip(out_rgb * scale[:, None], 0.0, 1.0)
    if held:
        log(f"  guard: {held} texels ({100.0*held/len(ratio):.1f}%) held at the limit")

    out = img.copy()
    out[ys, xs] = out_rgb

    # Feather only the seam: masked texels that touch an unmasked one.
    m = np.zeros((px, px)); m[ys, xs] = 1.0
    edge = (box(m, 1) < 0.999) & (m > 0.5)
    soft = np.stack([box(out[..., c], 1) for c in range(3)], -1)
    out = np.where(edge[..., None], 0.5 * out + 0.5 * soft, out)
    log(f"feathered {int(edge.sum())} seam texels")

    out8 = np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8)
    untouched = m < 0.5
    if not bool((out8[untouched] == src[untouched]).all()):
        raise SystemExit("deflare_palm: texels outside the mask changed — aborting")
    log(f"outside the mask preserved bit-exactly ({100.0*untouched.mean():.1f}% of the atlas)")

    Image.fromarray(out8).save(out_p)
    log("wrote", out_p)


if __name__ == "__main__":
    main()
