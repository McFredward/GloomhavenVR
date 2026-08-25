#!/usr/bin/env python3
"""Take the FAKE relief out of a board's SIDE STRIP normal map.

    python3 tex_siderelief.py --style <s> --geo <geobufdir> --normal <atlas normal in> \
        --out <dir> [--feature-sigma 6.0] [--report <json>]

THE DEFECT, and it is the user's complaint in its purest form
--------------------------------------------------------------
"Auf den Texturen sind Schrauben und Halzplatten etc zu sehen, also eigentlich
3-dimensionale Objekte.  Sie werden aber flach nur auf der Textur dargestellt."

ModBuild 275 repainted the rim's ALBEDO.  Nothing has ever touched the rim's NORMAL map,
so it is still what ModBuild 274's `tex_composite.pushpull_fill` left there: dilation
smear from the front face, dragged down a band no front camera can see.  Cropped out of
the atlas it is unmistakable -- an embossed row of RECTANGULAR PLATES running the whole
length of every side, with saturated edges, repeated once per wrapped strip row.  It is
the frame band's studded border, smeared sideways.

Measured, |slope| = hypot(nx, ny)/|nz| over the `sides` region's rim texels against that
same board's FRONT island:

    board    side strip        FRONT island     ratio      after
    oak      0.8650            0.3395           2.55x      0.3192
    steel    0.9013            0.3041           2.96x      0.3078
    bronze   0.5561            0.2486           2.24x      0.2507

A band that is geometrically a plain wall carries two to three times the normal-map slope
of the decorated face.  That is not relief, it is noise with a picture of plates in it --
and now that ModBuild 280 cuts a REAL profile into the side, it is noise that argues with
geometry the shader is already drawing.  This is the same sign conflict `tex_backrelief`
was written for on the back, arrived at from the other direction: there paint had to yield
to new geometry, here paint has to yield because it never described this surface at all.

WHY THE WHOLE FEATURE BAND GOES, AND NOT A MASKED PART OF IT
------------------------------------------------------------
On the back, `tex_backrelief` removes relief only inside a mask taken from the mesh's own
normals, because the back's art is REGISTERED -- its straps and nails are drawn where the
mesh has them, and outside the mask the map is correct.  The side strip has no registered
content of any kind: every feature the side actually has -- the rails, the rebate, oak's
cross battens -- is mesh geometry as of this build, and everything the map draws there
came from a different face.  So the correct feature content of this island is NONE, and
the edit is a band limit rather than a mask.

WHAT IS KEPT: the micro band, which is the material.  The slope field is split at
`--feature-sigma` texels and only the fast part survives, soft-clipped at a small multiple
of the FRONT's grain -- not at the strip's own sigma, because the plate outlines ARE most
of that sigma -- and then rescaled so the MEAN of its micro band matches the MEAN of the
front island's micro band.  Both the statistic and the target are measured off the same
atlas rather than chosen: the side is the same substance as the face, so its grain should
be the face's grain.  Every wrong version of that target is recorded at the call site, and
each of them passed every assert while making the map worse.

WHAT THIS DOES NOT TOUCH.  The `sides` contract region and the RIM group are not the same
set -- `gen_geobuf` calls a triangle RIM on its normal and its position, which also catches
the frame band's outer walls and the ornament walls near the board's edge, and those live
in the `frame` island with correct, registered relief.  The population here is the
INTERSECTION: rim texels inside the sides region, which after `gen_backuv` moves the back
plate out is exactly the side strip and nothing else.  Every other texel of the map is
asserted byte-identical afterwards, not hoped to be.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageFilter

Image.MAX_IMAGE_PIXELS = None

GRP_FRONT, GRP_BACK, GRP_RIM, GRP_INT = 1, 2, 3, 4
# the contract's `sides` rectangle, u 0.5..1.0 / v 0.75..1.0, in atlas rows and columns
SIDES_UV = (0.5, 0.75, 1.0, 1.0)


def _kernel(sigma):
    r = max(1, int(round(3.0 * sigma)))
    x = np.arange(-r, r + 1, dtype=np.float64)
    k = np.exp(-0.5 * (x / sigma) ** 2)
    return k / k.sum()


def gauss(a, sigma):
    """Separable Gaussian in plain numpy, on a SIGNED float field.

    Not through PIL: the slope field here is signed and unbounded, and PIL's blur takes
    8-bit or float images only -- an integer round trip would quantise away the material
    grain this file exists to preserve, which is the opposite of the intended edit."""
    if sigma <= 0:
        return np.asarray(a, np.float64).copy()
    k = _kernel(sigma)
    r = len(k) // 2
    out = np.zeros_like(a, dtype=np.float64)
    p = np.pad(a, [(r, r), (r, r)] + [(0, 0)] * (a.ndim - 2), mode="edge")
    for i, w in enumerate(k):
        out += w * p[i:i + a.shape[0], r:r + a.shape[1]]
    q = np.pad(out, [(0, 0), (r, r)] + [(0, 0)] * (a.ndim - 2), mode="edge")
    out = np.zeros_like(out)
    for i, w in enumerate(k):
        out += w * q[:, i:i + a.shape[1]]
    return out


def gauss_masked(a, m, sigma):
    """Normalised convolution: the low-pass of `a` using ONLY the texels under `m`.

    A plain blur at sigma = 6 reaches ~18 texels, which is past the atlas's 8-texel
    gutter, so near the strip's border it would mix in whatever island sits next door and
    the micro band would inherit that neighbour's structure as a false signal.  Dividing
    two blurs keeps the filter inside the island by construction."""
    w = m.astype(np.float64)
    num = gauss(a * w[..., None] if a.ndim == 3 else a * w, sigma)
    den = gauss(w, sigma)
    den = np.maximum(den, 1e-6)
    return num / (den[..., None] if a.ndim == 3 else den)


def run(a):
    os.makedirs(a.out, exist_ok=True)
    grp = np.load(os.path.join(a.geo, "grp.npy"))
    n8 = np.asarray(Image.open(a.normal).convert("RGB")).copy()
    N = grp.shape[0]

    u0, v0, u1, v1 = SIDES_UV
    reg = np.zeros(grp.shape, bool)
    # row 0 is v = 1, matching the project's rasteriser
    reg[int(round((1.0 - v1) * N)):int(round((1.0 - v0) * N)),
        int(round(u0 * N)):int(round(u1 * N))] = True
    m = (grp == GRP_RIM) & reg
    assert m.any(), "no rim texels inside the sides region -- wrong geobuf?"

    nrm = n8.astype(np.float64) / 255.0 * 2.0 - 1.0
    nz = np.where(np.abs(nrm[..., 2]) < 1e-6, 1e-6, nrm[..., 2])
    slope = np.stack([nrm[..., 0] / nz, nrm[..., 1] / nz], -1)

    before = float(np.hypot(slope[..., 0], slope[..., 1])[m].mean())
    fm = grp == GRP_FRONT
    front_mean = float(np.hypot(slope[..., 0], slope[..., 1])[fm].mean())

    # THE TARGET IS THE FRONT'S GRAIN, MEASURED IN THE SAME BAND WITH THE SAME STATISTIC,
    # and getting there took two wrong tries that are worth recording because both of them
    # PASSED EVERY ASSERT while making the map worse:
    #
    #   1. matched against the front island's RAW p99 slope.  That is 2.75 -- a 70 degree
    #      tilt, i.e. the front's ORNAMENT WALLS, not its grain.  Gain came out 1.33 and
    #      the side got STEEPER, 0.8985 -> 1.0015, the exact opposite of the intent.
    #   2. matched against the front's own MICRO band at p99: 2.785, barely different,
    #      because a hard painted edge is broadband and lands in the micro band too.  A
    #      p99 of anything on this atlas is an edge statistic.
    #
    # The MEAN of the micro band is the grain: it is dominated by the 99 % of texels that
    # are open surface rather than the 1 % that are edges.  A target has to be the same
    # statistic of the same band as the quantity it is a target for.
    fmicro = slope - gauss_masked(slope, fm, a.feature_sigma)
    fmag = np.hypot(fmicro[..., 0], fmicro[..., 1])[fm]
    target = float(fmag.mean())
    front_grain_p99 = float(np.percentile(fmag, 99.0))

    # the band limit, taken with a normalised convolution over the strip's own texels so
    # the filter never reaches across the gutter into a neighbouring island
    micro = slope - gauss_masked(slope, m, a.feature_sigma)
    # THE CLIP IS AGAINST THE TARGET, NOT AGAINST THE STRIP'S OWN SPREAD.  A soft clip at
    # 2.5 sigma of the strip's own micro band does almost nothing to the plate outlines,
    # because those outlines ARE most of that sigma -- the picture after it still showed
    # every rectangle, flattened inside and fully saturated at its edge.  Clipping at a
    # small multiple of the FRONT'S grain says the thing that is actually meant: nothing on
    # this surface is steeper than a few times the material, because nothing on this
    # surface is anything but material.
    lim = a.clip_x_grain * target
    micro = np.tanh(micro / lim) * lim
    smag_mean = float(np.hypot(micro[..., 0], micro[..., 1])[m].mean())
    gain = (target / smag_mean) if smag_mean > 0 else 0.0
    micro = micro * gain

    su, sv = micro[..., 0], micro[..., 1]
    inv = 1.0 / np.sqrt(su * su + sv * sv + 1.0)
    new = np.stack([su * inv, sv * inv, inv], -1) * 0.5 + 0.5
    out = n8.copy()
    out[m] = np.clip(np.rint(new[m] * 255.0), 0, 255).astype(np.uint8)

    outside = int(((out != n8).any(2) & ~m).sum())
    assert outside == 0, "texels outside the side strip moved: %d" % outside
    Image.fromarray(out).save(os.path.join(a.out, os.path.basename(a.normal)))

    chk = out.astype(np.float64) / 255.0 * 2.0 - 1.0
    cz = np.where(np.abs(chk[..., 2]) < 1e-6, 1e-6, chk[..., 2])
    after = float(np.hypot(chk[..., 0] / cz, chk[..., 1] / cz)[m].mean())
    stats = {
        "style": a.style, "side_strip_texels": int(m.sum()),
        "feature_sigma": a.feature_sigma,
        "slope_mean_before": round(before, 4),
        "slope_mean_after": round(after, 4),
        "front_slope_mean": round(front_mean, 4),
        "front_grain_mean_target": round(target, 4),
        "front_grain_p99": round(front_grain_p99, 4),
        "side_micro_mean_before_gain": round(smag_mean, 4),
        "gain_applied": round(gain, 4),
        "texels_changed_outside_side_strip": outside,
        "texels_changed": int((out != n8).any(2).sum()),
    }
    print(json.dumps(stats, indent=1))
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(stats, fh, indent=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--style", required=True)
    ap.add_argument("--geo", required=True)
    ap.add_argument("--normal", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default=None)
    ap.add_argument("--clip-x-grain", type=float, default=2.0,
                    help="soft-clip the micro band at this multiple of the FRONT island's "
                         "grain, before the gain that matches their means")
    ap.add_argument("--feature-sigma", type=float, default=6.0,
                    help="texels; the split between the plates (removed) and the material "
                         "grain (kept).  6 texels is ~4 mm at the strip's 1.5 tex/mm")
    run(ap.parse_args())


if __name__ == "__main__":
    main()
