#!/usr/bin/env python3
"""Does THE SAME BOARD POINT still get THE SAME COLOUR after an atlas repack?

    python3 tex_pointcheck.py --style oak \
        --geo-ref  <geobufdir of the shipped mesh>  --albedo-ref <shipped atlas> \
        --geo-new  <geobufdir of the new mesh>      --albedo-new <new atlas> \
        [--groups front,interior] [--res 2048] [--shift N] [--report x.json]

WHY THE OBVIOUS CHECK IS THE WRONG ONE HERE
-------------------------------------------
ModBuild 279 proved its front and rim untouched by rasterising every NON-BACK UV triangle
of the shipped and the new mesh into a 2048^2 map and diffing: 0 differing texels on all
three boards, against a one-texel control moving ~20 500.  That was the right gate then,
and it was right precisely BECAUSE nothing repacked -- it asks whether the islands sit in
the same place.

ModBuild 280 gives the rim a profile, so its strip island changes size and the `sides`
category repacks around it.  "The islands sit in the same place" is then false by design,
and a check that can only report that is a check that cannot pass.  The question that
still matters is the one the player can see: **the board point 40 mm in from the left end
of the frame band -- does it still come out the same colour?**  A repack that moved a
region would answer no; a repack confined to the region being rewritten answers yes.

HOW IT IS MEASURED
------------------
Each side is turned into a BOARD-SPACE image independently, through its OWN mesh: every
atlas texel in the selected groups carries the object-space position of the surface point
that samples it (`gen_geobuf`), so scattering that texel's atlas colour to its own (long,
short) coordinates asks the atlas "what colour is this board point?" without either side
ever seeing the other's UVs.  The two images are then compared where both are covered.

That is deliberately NOT a UV comparison.  It goes through the UVs of each mesh and comes
out the other side in a shared coordinate system, so it is blind to how the atlas is laid
out and sensitive only to what the surface ends up looking like -- which is the property
being claimed.

CONTROLS, because a difference metric that reports 0.00 has to be shown capable of
reporting anything else.  `--shift N` rolls the NEW atlas by N texels before sampling,
which is exactly the failure a bad repack would produce: the same island, one lattice step
sideways.  Run it once at 0 and once at 1; if 1 does not fire, the instrument is measuring
nothing and its 0 means nothing.

WHAT THE GROUPS MEAN.  `front` and `interior` together are the five regions this round
claims not to touch -- face, frame, slot_floor, rest_pads and button_seats all live in one
of those two `gen_geobuf` classes.  `back` should also come out unchanged, for a different
reason: `tex_backfill` places the back art at each texel's own board coordinates, so it
reproduces wherever the island lands.  `rim` is the thing being rewritten and is expected
to differ; its number is reported as the size of the intended change, not as a failure.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

GRP = {"front": 1, "back": 2, "rim": 3, "interior": 4}


def frame_of(geo):
    """The board frame (origin + extents + axis roles) taken off ONE mesh.

    BOTH SIDES MUST SHARE ONE FRAME, and the first version of this file did not: it
    derived the frame from each mesh's own bounding box, and on steel the new rim's
    extreme vertex quantised 3.73 MICROMETRES differently in float32.  That is 0.012 of a
    board pixel -- and it flipped 19 468 texels into the neighbouring bucket, which the
    checker then reported as 1.46 % of the front face changing colour.  A shared
    coordinate system that is computed twice is not shared."""
    pos = np.load(os.path.join(geo, "pos.npy")).astype(np.float64)
    grp = np.load(os.path.join(geo, "grp.npy"))
    flat = pos[grp > 0]
    lo, hi = flat.min(0), flat.max(0)
    ext = hi - lo
    thin = int(np.argmin(ext))
    rest = [i for i in range(3) if i != thin]
    lng, srt = (rest[0], rest[1]) if ext[rest[0]] >= ext[rest[1]] else (rest[1], rest[0])
    return lo, ext, lng, srt, thin


def board_image(geo, albedo, groups, res, shift, frame):
    pos = np.load(os.path.join(geo, "pos.npy")).astype(np.float64)
    grp = np.load(os.path.join(geo, "grp.npy"))
    alb = np.asarray(Image.open(albedo).convert("RGB"), np.float64)
    if shift:
        alb = np.roll(np.roll(alb, shift, 0), shift, 1)

    lo, ext, lng, srt, thin = frame

    sel = np.zeros(grp.shape, bool)
    for g in groups:
        sel |= grp == GRP[g]

    w, h = res, res // 2
    U = (pos[..., lng] - lo[lng]) / max(ext[lng], 1e-9)
    V = 1.0 - (pos[..., srt] - lo[srt]) / max(ext[srt], 1e-9)
    xi = np.clip(np.rint(U[sel] * (w - 1)).astype(np.int64), 0, w - 1)
    yi = np.clip(np.rint(V[sel] * (h - 1)).astype(np.int64), 0, h - 1)
    idx = yi * w + xi
    acc = np.zeros((w * h, 3), np.float64)
    cnt = np.zeros(w * h, np.float64)
    np.add.at(acc, idx, alb[sel])
    np.add.at(cnt, idx, 1.0)
    seen = cnt > 0
    acc[seen] /= cnt[seen][:, None]
    return acc.reshape(h, w, 3), seen.reshape(h, w)


def run(a):
    groups = [g.strip() for g in a.groups.split(",") if g.strip()]
    for g in groups:
        assert g in GRP, "unknown group %r" % g
    frame = frame_of(a.geo_ref)
    ref, mref = board_image(a.geo_ref, a.albedo_ref, groups, a.res, 0, frame)
    new, mnew = board_image(a.geo_new, a.albedo_new, groups, a.res, a.shift, frame)
    both = mref & mnew
    n = int(both.sum())
    assert n > 0, "the two board images do not overlap anywhere -- nothing was compared"
    d = np.abs(ref[both] - new[both])
    stats = {
        "style": a.style, "groups": groups, "res": [a.res, a.res // 2], "shift": a.shift,
        "px_ref": int(mref.sum()), "px_new": int(mnew.sum()), "px_compared": n,
        "coverage_ref": round(float(mref.mean()), 5),
        "coverage_new": round(float(mnew.mean()), 5),
        "mean_abs_err_255": round(float(d.mean()), 4),
        "p99_abs_err_255": round(float(np.percentile(d, 99.0)), 3),
        "max_abs_err_255": round(float(d.max()), 3),
        "px_differing": int((d.max(1) > 0.5).sum()),
        "px_differing_frac": round(float((d.max(1) > 0.5).mean()), 6),
    }
    print(json.dumps(stats, indent=1))
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(stats, fh, indent=1)
    return stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--style", required=True)
    ap.add_argument("--geo-ref", required=True)
    ap.add_argument("--albedo-ref", required=True)
    ap.add_argument("--geo-new", required=True)
    ap.add_argument("--albedo-new", required=True)
    ap.add_argument("--groups", default="front,interior")
    ap.add_argument("--res", type=int, default=2048)
    ap.add_argument("--shift", type=int, default=0,
                    help="POSITIVE CONTROL: roll the new atlas by N texels first")
    ap.add_argument("--report", default=None)
    run(ap.parse_args())


if __name__ == "__main__":
    main()
