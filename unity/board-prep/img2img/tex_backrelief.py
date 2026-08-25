#!/usr/bin/env python3
"""Take the BACK's painted relief OUT of the normal map, now that the mesh carries it.

    python3 tex_backrelief.py --style <oak|steel|bronze> --geo <geobufdir> \
        --albedo <atlas albedo> --normal <atlas normal in> --out <dir> [--report <json>]

THE DEFECT THIS FIXES
---------------------
"Auf den Texturen sind Schrauben und Halzplatten etc zu sehen, also eigentlich
3-dimensionale Objekte.  Sie werden aber flach nur auf der Textur dargstellt.  Ich moechte,
dass du das Mesh fuer die Seiten und Rueckseite an die Textur anpasst."

ModBuild 278 builds those straps, nails, rivets and cast fields as real geometry.  The
ModBuild 276 back normal map still embosses them, because it was DERIVED from the same art:
`tex_backfill.py` high-passes the back albedo at 4 texels and turns the result into slopes.
Leaving both in place is not "more relief".  The painted feature is a dark LINE at the
edge of a strap, so its high-pass is a GROOVE; the mesh at the same place is a chamfer
RAMPING UP.  Superposed they read as a bevel with a ditch cut into it -- the two disagree
in sign, at a separation of one or two texels, which is exactly the doubled-edge failure
this pipeline spent round 2 measuring.

WHAT IS AND IS NOT TOUCHED
--------------------------
* THE ALBEDO IS NOT TOUCHED, and that is a decision with a measurement behind it rather
  than an omission.  gen_backuv's unwrap is a plain top-down planar map from the board's
  own (long, short) axes, so an atlas texel in the back's rectangle answers to the same
  board point whether the surface there is plate, chamfer or cap.  Re-running tex_backfill's
  own BACK assignment against the NEW mesh's geobuf reproduces the shipped atlas exactly --
  mean |err| 0.00 of 255 on all three boards -- against controls at 15.6 to 21.7 that fire.
  So the paint is already registered to the new geometry to better than one texel, and at
  flat-on viewing it is the ONLY thing that draws the feature: the img2img README's standing
  limit is that with the baked key the half-vector sits ~32 deg off a flat face, so the open
  plate gets essentially no highlight.  Removing the painted edge would take the feature
  away at the one viewing angle where the geometry cannot show it.
* THE RIM IS NOT TOUCHED.  Its islands' UVs are bit-identical before and after this round
  (measured: 0 differing texels out of 930 519 to 1 102 983, against a one-texel control
  that moves 20 458 to 21 314), so what ModBuild 276 blended into it is still correct.
* Only the BACK's NORMAL texels are assigned, and only inside the mask.

HOW THE MASK IS BUILT -- from the MESH, not from the picture
------------------------------------------------------------
`gen_geobuf.py` gives every texel the object-space NORMAL of the surface point that samples
it.  Where the new relief has a wall, that normal tilts away from the back's own normal by
up to MAX_BACK_SLOPE_DEG; on the plate, on a cap and on a recess floor it does not tilt at
all.  So `1 - |n . n_back|` IS the feature mask, taken from the thing that now carries the
feature.  It is then dilated, because the art paints a feature's shadow and highlight
beside the edge rather than on it.

Nothing here asks the picture where its features are.  That matters: the whole reason this
round needed no registration step is that both the art and the geometry are addressed by
board position, and a mask taken from the art would put a fitting problem back in.

WHY THE EDIT IS WRITTEN AS A DIFFERENCE
---------------------------------------
The obvious implementation recomputes the suppressed normal from the albedo and writes it.
That would silently rewrite every back texel, including the ones the mask does not touch,
because this file cannot reproduce `tex_backfill`'s p99 normalisation bit-for-bit once the
geobuf's BACK set has changed shape.  A diff that claims "only the feature band moved"
while moving 296 000 texels by +-1 is the same lie the ModBuild 276 colour round trip told.

So the slope field is computed TWICE from the same source -- once as tex_backfill computed
it and once with the feature band suppressed -- and only their DIFFERENCE is added to the
shipped map.  Outside the mask that difference is identically zero, so those texels come out
byte-identical by construction and not by hope.  This file checks that afterwards anyway.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageFilter

Image.MAX_IMAGE_PIXELS = None

GRP_BACK = 2


def srgb2lin(x):
    x = np.asarray(x, np.float64) / 255.0
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def gauss(a, sigma):
    """The SAME 8-bit PIL blur tex_backfill uses.  Matching it is the point: this file
    subtracts one of its own outputs from another, so any bias cancels only if both terms
    come out of the identical filter."""
    if sigma <= 0:
        return np.asarray(a, np.float64).copy()
    im = Image.fromarray(np.clip(a * 255.0, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(sigma)), np.float64) / 255.0


def gauss_f(a, sigma):
    """A float separable Gaussian, for the MASK only.  The mask is a 0..1 field with a very
    small support (a chamfer is 1.5 to 3.2 texels wide), and an 8-bit round trip quantises
    it to 1/255 steps that show as banding in the suppression ramp."""
    if sigma <= 0:
        return a.astype(np.float64).copy()
    r = max(1, int(round(sigma * 3)))
    x = np.arange(-r, r + 1, dtype=np.float64)
    k = np.exp(-x * x / (2.0 * sigma * sigma))
    k /= k.sum()
    p = len(k) // 2
    b = np.pad(a.astype(np.float64), ((0, 0), (p, p)), mode="edge")
    b = np.apply_along_axis(lambda m: np.convolve(m, k, "valid"), 1, b)
    b = np.pad(b, ((p, p), (0, 0)), mode="edge")
    return np.apply_along_axis(lambda m: np.convolve(m, k, "valid"), 0, b)


def slope_field(lum, mb, sigma, tilt_deg, suppress=None):
    """tex_backfill's own back-relief derivation, optionally with the feature band damped.

    `suppress` damps the HEIGHT field, not the finished slope, so the ramp into and out of
    the mask stays smooth instead of stepping: damping a gradient leaves a discontinuity at
    the mask edge that reads as a second, softer ridge beside the first.

    The p99 normalisation is taken from the UNSUPPRESSED magnitude in both calls, so the
    material grain keeps the amplitude ModBuild 276 gave it.  Renormalising the suppressed
    field would silently amplify the grain to make up for the relief that was removed.
    """
    hp = lum - gauss(lum, sigma)
    sd = float(hp[mb].std())
    if sd > 0:
        hp = np.tanh(hp / (2.5 * sd)) * (2.5 * sd)
    ref = hp
    if suppress is not None:
        hp = hp * (1.0 - suppress)
    gu = (np.roll(hp, -1, 1) - np.roll(hp, 1, 1)) * 0.5
    gv = (np.roll(hp, -1, 0) - np.roll(hp, 1, 0)) * 0.5
    ru = (np.roll(ref, -1, 1) - np.roll(ref, 1, 1)) * 0.5
    rv = (np.roll(ref, -1, 0) - np.roll(ref, 1, 0)) * 0.5
    p99 = float(np.percentile(np.hypot(ru, rv)[mb], 99.0))
    k = (np.tan(np.radians(tilt_deg)) / p99) if p99 > 0 else 0.0
    return gu * k, gv * k


def run(a):
    os.makedirs(a.out, exist_ok=True)
    pos = np.load(os.path.join(a.geo, "pos.npy")).astype(np.float64)
    nrm = np.load(os.path.join(a.geo, "nrm.npy")).astype(np.float64)
    grp = np.load(os.path.join(a.geo, "grp.npy"))
    mb = grp == GRP_BACK

    mapped = grp > 0
    ext = pos[mapped].max(0) - pos[mapped].min(0)
    thin = int(np.argmin(ext))

    # ---- the feature mask, from the MESH's own normals -------------------------------
    n = nrm / np.maximum(np.linalg.norm(nrm, axis=2, keepdims=True), 1e-12)
    tilt = 1.0 - np.abs(n[..., thin])            # 0 on plate / cap / floor, ~0.22 on a wall
    tilt[~mb] = 0.0
    wall = np.clip(tilt / a.wall_tilt, 0.0, 1.0)
    mask = np.clip(gauss_f(wall, a.dilate) * a.dilate_gain, 0.0, 1.0)
    mask[~mb] = 0.0

    # ---- the two slope fields, from the same source ----------------------------------
    alb = srgb2lin(np.asarray(Image.open(a.albedo).convert("RGB"), np.float64))
    lum = 0.2126 * alb[..., 0] + 0.7152 * alb[..., 1] + 0.0722 * alb[..., 2]
    su_f, sv_f = slope_field(lum, mb, a.relief_sigma, a.back_tilt_deg)
    su_s, sv_s = slope_field(lum, mb, a.relief_sigma, a.back_tilt_deg, suppress=mask)

    # ---- apply the DIFFERENCE to the shipped map -------------------------------------
    nu8 = np.asarray(Image.open(a.normal).convert("RGB")).copy()
    v = nu8.astype(np.float64) / 255.0 * 2.0 - 1.0
    nz = np.maximum(np.abs(v[..., 2]), 1e-6)
    cur_u, cur_v = v[..., 0] / nz, v[..., 1] / nz

    hit = mask > 1.0 / 512.0
    new_u = cur_u + (su_s - su_f)
    new_v = cur_v + (sv_s - sv_f)
    inv = 1.0 / np.sqrt(new_u * new_u + new_v * new_v + 1.0)
    packed = np.stack([new_u * inv, new_v * inv, inv], -1) * 0.5 + 0.5
    packed = np.clip(np.rint(packed * 255.0), 0, 255).astype(np.uint8)

    out = nu8.copy()
    sel = hit & mb
    out[sel] = packed[sel]
    Image.fromarray(out).save(os.path.join(a.out, os.path.basename(a.normal)))

    # ---- what moved, and the proof that nothing else did -----------------------------
    untouched = int(((out != nu8).any(2) & ~sel).sum())
    unchanged_in_mask = int((sel & ~(out != nu8).any(2)).sum())
    band = mask > 0.5
    stats = {
        "style": a.style,
        "back_texels": int(mb.sum()),
        "wall_texels": int((wall > 0.5).sum()),
        "mask_texels": int(sel.sum()),
        "mask_fraction_of_back": round(float(sel.sum()) / max(int(mb.sum()), 1), 4),
        "texels_changed_outside_mask": untouched,
        "masked_texels_that_rounded_to_the_same_byte": unchanged_in_mask,
        "slope_in_band_before": round(float(np.hypot(cur_u, cur_v)[band].mean()), 4),
        "slope_in_band_after": round(float(np.hypot(new_u, new_v)[band].mean()), 4),
        "slope_outside_band_before": round(float(np.hypot(cur_u, cur_v)[mb & ~band].mean()), 4),
        "slope_outside_band_after": round(float(np.hypot(new_u, new_v)[mb & ~band].mean()), 4),
        "max_mesh_wall_deg": round(float(np.degrees(np.arccos(
            np.clip(np.abs(n[..., thin])[mb].min(), -1, 1)))), 2),
    }
    assert untouched == 0, "texels outside the mask moved: %d" % untouched
    print(json.dumps(stats, indent=1))
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(stats, fh, indent=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--style", required=True)
    ap.add_argument("--geo", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--normal", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default=None)
    # these three mirror tex_backfill's defaults exactly; changing them here would make the
    # two slope fields incomparable and the difference meaningless
    ap.add_argument("--relief-sigma", type=float, default=4.0)
    ap.add_argument("--back-tilt-deg", type=float, default=18.0)
    ap.add_argument("--wall-tilt", type=float, default=0.08,
                    help="mesh tilt (1 - |n.n_back|) that counts as a full wall; 0.08 is 23 deg")
    ap.add_argument("--dilate", type=float, default=3.5,
                    help="texels of blur on the wall mask -- the art paints a feature's "
                         "shadow BESIDE its edge, not on it")
    ap.add_argument("--dilate-gain", type=float, default=2.6,
                    help="restores the peak a blur of a 2-texel band takes away")
    run(ap.parse_args())


if __name__ == "__main__":
    main()
