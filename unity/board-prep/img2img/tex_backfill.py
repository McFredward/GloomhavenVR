#!/usr/bin/env python3
"""Author the BACK PLATE and the RIM BEVEL of a control board into its atlas.

    python3 tex_backfill.py --style <oak|steel|bronze> --geo <geobufdir> \
        --gen <board_back_<style>.png> --front-board <front board-space albedo> \
        --albedo <atlas albedo in> --normal <atlas normal in> --mrs <atlas mrs in> \
        --out <dir> [--report <json>]

THE DEFECT THIS FIXES
---------------------
"Die Seiten und die Rückseite die Textur ist kaputt ... Auch dort soll eine entsprechende
Textur sein."  The ModBuild 274 pass authored the FRONT face only: it rendered the board
flat-on, generated art at that view, and scattered it back through a UV pass taken from
the same view.  Everything a front camera cannot see was then filled by
`tex_composite.pushpull_fill`.

Push-pull is right for a recess wall -- one or two texels from its own floor, and made of
the same metal.  It is wrong for the back plate, which is hundreds of texels from the
nearest authored texel, so the fill converges to a flat average.  Measured in the shipped
274 atlas, over each island:

    board  island   |slope| mean vs FRONT     albedo high-pass rms vs FRONT
    oak    BACK           26.0%                        34.2%
    steel  BACK            6.7%                        45.0%
    bronze BACK           11.7%                        36.5%

6.7% of the front's relief is not "a bit soft", it is nothing -- which is exactly the flat
grey slab in the screenshot.  The rim's numbers are not low, but its albedo is dilation
smear stretched along the fill direction, which is the streaking he saw along the edge.

HOW EACH REGION IS ADDRESSED
----------------------------
Not by a camera.  `gen_geobuf.py` rasterises the mesh into atlas space, so every texel
carries the OBJECT-SPACE POSITION and NORMAL of the surface point that samples it.  Art is
placed by that position, which is why it cannot drift: a texel gets the colour of the board
point it actually is, not the colour a picture thinks belongs there.

    BACK  -- the generated plate, sampled at the texel's own (long, short) board
             coordinates.  The back is geometrically FLAT, so unlike the front there is no
             mesh feature for the model's painted structure to be misregistered against,
             and the art can be used whole.  That is also why the back's relief may be
             derived from its own art: the plank seams and rivets ARE the relief, there is
             no competing mesh-registered version of them to double.

    RIM   -- a depth blend from the front material to the back material across the board's
             own thickness.  The rim is literally the surface that runs from one face to
             the other, so at t=0 it is the front's material and at t=1 it is the back's,
             both sampled at the rim texel's own (long, short) position.  Nothing is
             invented, both edges match their neighbour by construction, and there is no
             fill direction to smear along.  The rim's RELIEF is left alone: the studded
             border is real mesh geometry and is already registered in the shipped map.

    INTERIOR -- untouched.  Recess floors and walls measure 90-127% of the front's
             high-pass energy already, because there push-pull travels one or two texels
             and does the physically correct thing.

WHY THE GENERATED PLATE IS DE-SHADED FIRST
------------------------------------------
The art comes back with its own soft key: nail heads and rivets carry a bright top and a
dark shadow.  Left in the albedo it would be lit a SECOND time by BoardLit, and the same
structure is also about to become the normal map, so the shading would be applied twice.
A heavy low-pass is divided out and the plate's own median restored, which removes the
gradient and the vignette while leaving every feature edge intact.

THE 3:2 TRAP, AND WHAT IT COST HERE
-----------------------------------
The tool's aspect enum has no 2:1.  The oak and bronze plates came back inside their padded
frames at 1.839:1 and 1.813:1 and are resampled to 2:1, a 1.09x stretch.  STEEL IGNORED THE
PAD and filled the whole 3:2 frame, so its resample is 1.333x in the long direction: its
~8 mm dome rivets ship as ~11 x 8 mm ellipses.  That is a real, measured distortion and it
is accepted rather than regenerated -- it is a back face, and a blind regeneration on a
nudged prompt is not how this pipeline spends images.
"""

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageFilter

Image.MAX_IMAGE_PIXELS = None

PLATE_W, PLATE_H = 2048, 1024
GRP_FRONT, GRP_BACK, GRP_RIM, GRP_INT = 1, 2, 3, 4


def srgb2lin(x):
    x = np.asarray(x, np.float64) / 255.0
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def lin2srgb(x):
    x = np.clip(np.asarray(x, np.float64), 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055) * 255.0


def gauss(a, sigma):
    if sigma <= 0:
        return np.asarray(a, np.float64).copy()
    if a.ndim == 3:
        return np.stack([gauss(a[..., i], sigma) for i in range(a.shape[2])], -1)
    im = Image.fromarray(np.clip(a * 255.0, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(sigma)), np.float64) / 255.0


def plate_rect(img, thr=26, frac=0.30):
    """The generated plate inside its padded frame.  Located, not assumed: the model
    rescales the frame it is given, and on steel it overran the pad entirely."""
    a = np.asarray(img.convert("RGB"), np.float64).mean(2)
    xs = np.nonzero((a > thr).mean(0) > frac)[0]
    ys = np.nonzero((a > thr).mean(1) > frac)[0]
    if len(xs) == 0 or len(ys) == 0:
        return (0, 0, img.width, img.height)
    return (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def deshade(lin, sigma=90.0):
    """Divide out the generated plate's own soft key, keep its median level."""
    lo = gauss(lin, sigma)
    med = np.median(lo.reshape(-1, lo.shape[-1]), axis=0)
    return np.clip(lin / np.maximum(lo, 1e-4) * med, 0.0, 4.0)


def bilinear(img, u, v):
    """img (H,W,C) sampled at fractional (u,v) in [0,1], clamped at the border."""
    h, w = img.shape[:2]
    x = np.clip(u * (w - 1), 0, w - 1)
    y = np.clip(v * (h - 1), 0, h - 1)
    x0 = np.floor(x).astype(int); x1 = np.minimum(x0 + 1, w - 1)
    y0 = np.floor(y).astype(int); y1 = np.minimum(y0 + 1, h - 1)
    fx = (x - x0)[..., None]; fy = (y - y0)[..., None]
    return ((img[y0, x0] * (1 - fx) + img[y0, x1] * fx) * (1 - fy) +
            (img[y1, x0] * (1 - fx) + img[y1, x1] * fx) * fy)


def board_axes(pos, nrm):
    """(long axis index, short axis index, thickness axis index) plus the bbox."""
    flat = pos.reshape(-1, 3)
    lo, hi = flat.min(0), flat.max(0)
    ext = hi - lo
    thin = int(np.argmin(ext))
    rest = [i for i in range(3) if i != thin]
    lng, srt = (rest[0], rest[1]) if ext[rest[0]] >= ext[rest[1]] else (rest[1], rest[0])
    return lng, srt, thin, lo, hi


def run(a):
    os.makedirs(a.out, exist_ok=True)
    pos = np.load(os.path.join(a.geo, "pos.npy")).astype(np.float64)
    grp = np.load(os.path.join(a.geo, "grp.npy"))
    N = grp.shape[0]

    # object-space extents come from the buffer's own mapped texels
    mapped = grp > 0
    flat = pos[mapped]
    lo, hi = flat.min(0), flat.max(0)
    ext = hi - lo
    thin = int(np.argmin(ext))
    rest = [i for i in range(3) if i != thin]
    lng, srt = (rest[0], rest[1]) if ext[rest[0]] >= ext[rest[1]] else (rest[1], rest[0])

    # ---- the generated back plate, located, de-shaded and put at the board's own 2:1 ----
    gen = Image.open(a.gen).convert("RGB")
    x0, y0, x1, y1 = plate_rect(gen)
    src_aspect = (x1 - x0) / max(y1 - y0, 1)
    plate = gen.crop((x0, y0, x1, y1)).resize((PLATE_W, PLATE_H), Image.LANCZOS)
    back_lin = deshade(srgb2lin(np.asarray(plate, np.float64)))

    front = Image.open(a.front_board).convert("RGB").resize((PLATE_W, PLATE_H), Image.LANCZOS)
    front_lin = srgb2lin(np.asarray(front, np.float64))

    # ---- atlas albedo in ------------------------------------------------------------
    # THE BYTES OUTSIDE THE AUTHORED REGIONS MUST NOT MOVE. An earlier version converted the
    # whole atlas to linear and back, and the sRGB round trip alone shifted 1029 FRONT, 570
    # INTERIOR and 12680 unmapped texels by +-1 -- invisible, and still a lie in any diff
    # that claims the front face is untouched. The uint8 array is therefore kept whole and
    # only the BACK and RIM texels are ever assigned into it.
    alb_u8 = np.asarray(Image.open(a.albedo).convert("RGB")).copy()
    alb = srgb2lin(alb_u8.astype(np.float64))

    # normalised board coordinates for every mapped texel
    U = (pos[..., lng] - lo[lng]) / max(ext[lng], 1e-9)
    V = 1.0 - (pos[..., srt] - lo[srt]) / max(ext[srt], 1e-9)
    # depth through the thickness: 0 at the front face, 1 at the back face
    Tt = (pos[..., thin] - lo[thin]) / max(ext[thin], 1e-9)
    front_at_hi = np.mean(pos[grp == GRP_FRONT][:, thin]) > np.mean(pos[grp == GRP_BACK][:, thin])
    depth = (1.0 - Tt) if front_at_hi else Tt

    mb = grp == GRP_BACK
    mr = grp == GRP_RIM
    stats = {}

    if mb.any():
        v = bilinear(back_lin, U[mb], V[mb])
        alb[mb] = v
        alb_u8[mb] = np.clip(np.rint(lin2srgb(v)), 0, 255).astype(np.uint8)
        stats["back_texels"] = int(mb.sum())

    if mr.any():
        f = bilinear(front_lin, U[mr], V[mr])
        b = bilinear(back_lin, U[mr], V[mr])
        t = np.clip(depth[mr], 0.0, 1.0)[..., None]
        t = t * t * (3.0 - 2.0 * t)                      # smoothstep, so neither face steps
        v = f * (1.0 - t) + b * t
        alb[mr] = v
        alb_u8[mr] = np.clip(np.rint(lin2srgb(v)), 0, 255).astype(np.uint8)
        stats["rim_texels"] = int(mr.sum())

    Image.fromarray(alb_u8).save(os.path.join(a.out, os.path.basename(a.albedo)))

    # ---- relief and material for the BACK, derived from its own art ------------------
    nrm = np.asarray(Image.open(a.normal).convert("RGB"), np.float64) / 255.0
    mrs = np.asarray(Image.open(a.mrs).convert("RGB"), np.float64) / 255.0

    if mb.any():
        lum = (0.2126 * alb[..., 0] + 0.7152 * alb[..., 1] + 0.0722 * alb[..., 2])
        hp = lum - gauss(lum, a.relief_sigma)
        sd = float(hp[mb].std())
        if sd > 0:
            hp = np.tanh(hp / (2.5 * sd)) * (2.5 * sd)
        gu = (np.roll(hp, -1, 1) - np.roll(hp, 1, 1)) * 0.5
        gv = (np.roll(hp, -1, 0) - np.roll(hp, 1, 0)) * 0.5
        # matches tex_common/tex_maps: this pack writes nx = +dh/du (see tex_maps.py)
        mag = np.hypot(gu, gv)
        p99 = float(np.percentile(mag[mb], 99.0))
        k = (np.tan(np.radians(a.back_tilt_deg)) / p99) if p99 > 0 else 0.0
        su, sv = gu * k, gv * k
        nz = 1.0 / np.sqrt(su * su + sv * sv + 1.0)
        newn = np.stack([su * nz, sv * nz, nz], -1) * 0.5 + 0.5
        nrm[mb] = newn[mb]
        stats["back_relief_p99_deg"] = a.back_tilt_deg
        stats["back_slope_mean"] = float(np.hypot(su[mb], sv[mb]).mean())

        # material: the back is the same substance as the front, so its metallic follows
        # the front's policy; only roughness moves, because an unfinished reverse is
        # rougher than a handled face.
        fm = mrs[grp == GRP_FRONT]
        met = float(np.median(fm[..., 0]))
        rgh = float(np.median(fm[..., 1]))
        micro = np.sqrt(np.maximum(gauss((lum - gauss(lum, 3.0)) ** 2, 6.0), 0.0))
        mref = max(float(np.percentile(micro[mb], 99.0)), 1e-6)
        rough = np.clip(rgh + a.back_rough_bias + 0.10 * np.clip(micro / mref, 0, 1), 0.42, 0.95)
        mrs[mb, 0] = met
        mrs[mb, 1] = rough[mb]
        mrs[mb, 2] = 0.0
        stats["back_metallic"] = met
        stats["back_roughness_median"] = float(np.median(rough[mb]))

    if mr.any():
        fm = mrs[grp == GRP_FRONT]
        mrs[mr, 0] = float(np.median(fm[..., 0]))
        mrs[mr, 1] = np.clip(float(np.median(fm[..., 1])) + 0.03, 0.42, 0.95)
        mrs[mr, 2] = 0.0

    Image.fromarray(np.clip(np.rint(nrm * 255), 0, 255).astype(np.uint8)).save(
        os.path.join(a.out, os.path.basename(a.normal)))
    Image.fromarray(np.clip(np.rint(mrs * 255), 0, 255).astype(np.uint8)).save(
        os.path.join(a.out, os.path.basename(a.mrs)))

    stats.update(style=a.style, atlas=N, gen_plate_rect=[x0, y0, x1, y1],
                 gen_plate_aspect=round(src_aspect, 4),
                 resample_stretch=round(2.0 / src_aspect, 4),
                 long_axis="xyz"[lng], short_axis="xyz"[srt], thickness_axis="xyz"[thin])
    print(json.dumps(stats, indent=1))
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(stats, fh, indent=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--style", required=True)
    ap.add_argument("--geo", required=True)
    ap.add_argument("--gen", required=True)
    ap.add_argument("--front-board", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--normal", required=True)
    ap.add_argument("--mrs", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default=None)
    ap.add_argument("--relief-sigma", type=float, default=4.0)
    ap.add_argument("--back-tilt-deg", type=float, default=18.0,
                    help="99th-percentile tilt of the back's derived relief")
    ap.add_argument("--back-rough-bias", type=float, default=0.06,
                    help="an unfinished reverse is rougher than a handled face")
    run(ap.parse_args())


if __name__ == "__main__":
    main()
