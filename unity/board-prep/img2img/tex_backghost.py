#!/usr/bin/env python3
"""Is the BACK's paint registered to the BACK's new geometry, or does it ghost beside it?

    python3 tex_backghost.py --style <s> --geo <geobufdir> --albedo <atlas albedo> \
        [--group back|front] [--report <json>]

WHAT THIS MEASURES, AND WHY THE ROUND-2 NUMBER IS NOT THE RIGHT ONE HERE
------------------------------------------------------------------------
Round 2's falsifier was the GHOST RATIO: the density of albedo edges within a texel or two
of a geometric edge, over the density out on the open plate.  It caught a real defect --
gpt-image-2 redrew the FRONT's feature outlines 3 to 9 mm off the mesh's real ones, so the
board shipped with a painted edge beside every carved one.  Its own positive control (the
raw generated art) fired at 1.67 / 3.14 / 4.12 for oak / steel / bronze and the composited
art came back at 1.10 / 1.30 / 0.62.

That harness was never committed and its scale cannot be reproduced from the record, so
this file does not pretend to continue its numbers.  It also would not answer the question
if it did.  On the BACK, a HIGH ghost ratio is the GOOD outcome, not the bad one: the
geometry was built at the art's own measured feature positions, so paint and mesh are
supposed to have their edges in the same place, and "albedo edges cluster at geometric
edges" is a statement that they do.  A single ratio cannot tell "on top of" from "beside".

So the product here is the PROFILE -- albedo-edge density against distance from the nearest
geometric wall, in texels -- and the ratio is reported from it for continuity.  A registered
pair peaks at distance 0 and decays.  A ghost peaks at the misregistration distance, and
that is a shape a scalar cannot show.

THE CONTROLS, and both of them fire
-----------------------------------
* POSITIVE: the same albedo shifted by --shift texels (8 texels is 6.6 mm at the back's
  1.21 tex/mm, inside the 3-9 mm the front was actually wrong by).  Its peak MUST move by
  the shift.  If it does not, the profile is measuring something other than registration.
* NULL: the same albedo shifted by --null-shift texels (43 is 35 mm, further than any
  feature on any of the three backs is wide).  Its profile MUST come out flat and its ratio
  near 1.0.  Without this, "the peak is at zero" could just be an artefact of edges and
  walls both being commoner in some part of the island.

  An earlier version used a RANDOM mask of the same texel count instead, and it was a bad
  null: 5656 points scattered over 296 000 texels leave a mean spacing of 7 texels, so no
  texel is ever far from one, the far buckets emptied out and the null "fired" at 1.325
  with a peak at 13.  A null control has to preserve the geometry of the thing it nulls.

`--group front` runs the identical instrument over the FRONT face, whose geometry and UVs
this round does not touch, as a regression guard: that number must not move.
"""

import argparse
import json
import os

import numpy as np

GRP = {"front": 1, "back": 2}


def blur(a, sigma):
    r = max(1, int(round(sigma * 3)))
    x = np.arange(-r, r + 1, dtype=np.float64)
    k = np.exp(-x * x / (2.0 * sigma * sigma))
    k /= k.sum()
    p = len(k) // 2
    b = np.pad(a.astype(np.float64), ((0, 0), (p, p)), mode="edge")
    b = np.apply_along_axis(lambda m: np.convolve(m, k, "valid"), 1, b)
    b = np.pad(b, ((p, p), (0, 0)), mode="edge")
    return np.apply_along_axis(lambda m: np.convolve(m, k, "valid"), 0, b)


def chebyshev_distance(mask, maxd):
    """Distance in texels to the nearest True, capped at maxd.  A plain BFS over a boolean
    field -- no scipy in this project's python, and the cap keeps it to maxd passes."""
    d = np.full(mask.shape, maxd + 1, np.int32)
    d[mask] = 0
    cur = mask.copy()
    for k in range(1, maxd + 1):
        nxt = np.zeros_like(cur)
        nxt[1:, :] |= cur[:-1, :]
        nxt[:-1, :] |= cur[1:, :]
        nxt[:, 1:] |= cur[:, :-1]
        nxt[:, :-1] |= cur[:, 1:]
        nxt &= d > maxd
        d[nxt] = k
        cur = nxt
        if not cur.any():
            break
    return d


def profile(edge, dist, sel, maxd):
    """Albedo-edge density per distance bucket, over the selected region only."""
    out = []
    for k in range(maxd + 1):
        m = sel & (dist == k)
        n = int(m.sum())
        out.append(round(float(edge[m].mean()), 4) if n else None)
    return out


def run(a):
    grp = np.load(os.path.join(a.geo, "grp.npy"))
    nrm = np.load(os.path.join(a.geo, "nrm.npy")).astype(np.float64)
    pos = np.load(os.path.join(a.geo, "pos.npy")).astype(np.float64)
    gid = GRP[a.group]
    sel = grp == gid

    mapped = grp > 0
    ext = pos[mapped].max(0) - pos[mapped].min(0)
    thin = int(np.argmin(ext))

    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    alb = np.asarray(Image.open(a.albedo).convert("L"), np.float64) / 255.0

    # albedo EDGES: the high-pass magnitude, thresholded at the region's own p90 so the
    # number does not depend on how contrasty a given style's material happens to be
    hp = alb - blur(alb, a.edge_sigma)
    gu = (np.roll(hp, -1, 1) - np.roll(hp, 1, 1)) * 0.5
    gv = (np.roll(hp, -1, 0) - np.roll(hp, 1, 0)) * 0.5
    mag = np.hypot(gu, gv)
    thr = float(np.percentile(mag[sel], 90.0))
    edge = (mag > thr).astype(np.float64)

    # geometric EDGES: where the mesh's own normal tilts off the face's normal
    n = nrm / np.maximum(np.linalg.norm(nrm, axis=2, keepdims=True), 1e-12)
    tilt = 1.0 - np.abs(n[..., thin])
    wall = sel & (tilt > a.wall_tilt)

    res = {"style": a.style, "group": a.group, "region_texels": int(sel.sum()),
           "wall_texels": int(wall.sum()), "albedo_edge_threshold": round(thr, 5)}

    # ---- THE DECISIVE MEASUREMENT: where does the paint best fit the MESH's own height? --
    #
    # The geobuf gives every texel the object-space POSITION of its surface point, so its
    # thickness component IS a height map of the back, in atlas space, at atlas resolution.
    # High-pass both that and the albedo and slide one over the other: the offset that
    # maximises the correlation is the registration offset, in texels, with no threshold and
    # no edge model in between.  An edge-DENSITY ratio cannot do this -- a feature region
    # carries more texture than an open plate whether it is registered or not, which is why
    # the null control of that statistic sits at 1.21 rather than at 1.0.
    hgt = pos[..., thin].copy()
    hgt[~sel] = 0.0
    hh = hgt - blur(hgt, a.edge_sigma)
    aa = hp.copy()
    hh[~sel] = 0.0
    aa[~sel] = 0.0
    hh = hh - hh[sel].mean()
    aa = aa - aa[sel].mean()
    hh[~sel] = 0.0
    aa[~sel] = 0.0

    def best_offset(field, span):
        best, grid = None, {}
        for dv in range(-span, span + 1):
            for du in range(-span, span + 1):
                f = np.roll(np.roll(field, dv, 0), du, 1)
                c = float((hh[sel] * f[sel]).sum() /
                          max(np.sqrt((hh[sel] ** 2).sum() * (f[sel] ** 2).sum()), 1e-12))
                grid[(du, dv)] = c
                if best is None or c > best[2]:
                    best = (du, dv, c)
        return best, grid

    bu, bv, bc = best_offset(aa, a.corr_range)[0]
    grid0 = best_offset(aa, 0)[1]
    su_, sv_, sc = best_offset(np.roll(aa, a.shift, 1), a.corr_range)[0]
    res["registration"] = {
        "best_offset_texels": [bu, bv],
        "best_correlation": round(bc, 4),
        "correlation_at_zero": round(grid0[(0, 0)], 4),
        "control_planted_shift_u": a.shift,
        "control_recovered_offset_texels": [su_, sv_],
        "control_correlation": round(sc, 4),
    }

    dist = chebyshev_distance(wall, a.maxd)
    for name, e in (("measured", edge),
                    ("control_shift_%d" % a.shift, np.roll(edge, a.shift, 1)),
                    ("control_null_shift_%d" % a.null_shift, np.roll(edge, a.null_shift, 1))):
        prof = profile(e, dist, sel, a.maxd)
        near = [p for p in prof[:3] if p is not None]
        far = [p for p in prof[a.maxd - 8:a.maxd + 1] if p is not None]
        ratio = (sum(near) / len(near)) / max(sum(far) / max(len(far), 1), 1e-9)
        peak = int(np.argmax([p if p is not None else -1 for p in prof]))
        res[name] = {"ratio_0_2_over_far": round(ratio, 3), "peak_texel": peak,
                     "profile": prof}
    print(json.dumps(res, indent=1))
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(res, fh, indent=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--style", required=True)
    ap.add_argument("--geo", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--group", default="back", choices=sorted(GRP))
    ap.add_argument("--edge-sigma", type=float, default=3.0)
    ap.add_argument("--wall-tilt", type=float, default=0.08)
    ap.add_argument("--maxd", type=int, default=20)
    ap.add_argument("--shift", type=int, default=8,
                    help="POSITIVE control: 8 texels is 6.6 mm at the back's 1.21 tex/mm, "
                         "inside the 3-9 mm the FRONT was actually misregistered by")
    ap.add_argument("--null-shift", type=int, default=43,
                    help="NULL control: 35 mm, further than any feature's own size, so the "
                         "mask and the picture cannot correspond at all")
    ap.add_argument("--corr-range", type=int, default=12)
    ap.add_argument("--report", default=None)
    run(ap.parse_args())


if __name__ == "__main__":
    main()
