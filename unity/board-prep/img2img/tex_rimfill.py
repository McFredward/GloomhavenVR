#!/usr/bin/env python3
"""Stop the RIM painting the BACK's screws onto a wall that has none.

    python3 tex_rimfill.py --style <s> --geo <geobufdir> --gen <board_back_<s>.png> \
        --albedo <atlas albedo in> --out <dir> [--walk 0.28] [--sweep] [--report <json>]

THE DEFECT, and it is one this round created the instrument to see
------------------------------------------------------------------
"Auf den Texturen sind Schrauben und Halzplatten etc zu sehen ... Ich moechte, dass du das
Mesh fuer die SEITEN und Rueckseite an die Textur anpasst."

The back's half of that is geometry (ModBuild 278 builds the straps, nails, rivets and cast
fields).  The sides' half turned out to be a different defect with a different fix, and the
measurement is what separated them.

`tex_backfill` builds a rim texel at depth t from

    lerp( front(p - n*t*W),  back(p - n*(1-t)*W),  smoothstep(t) )

with W the board's OWN THICKNESS.  The walk is what gives the rim two dimensions of
variation instead of one -- without it the band draws from a single row of art and streaks,
which is exactly how the first version of the rim shipped wrong.  But W = 35.6 mm on steel
reaches a long way in, and the steel back's rivet row sits 8.9 mm from the plate edge with
its recessed fields at 14.5 mm.  So at t = 0.75, where the back term already carries 84 % of
the blend, the rim samples the rivet row and paints it down the side of the board.

Measured with the mesh's own wall mask carried into board space, the share of rim texels
whose back-sample lands on a back FEATURE:

    oak     4.1 %   (2.5 % weighted by the blend)
    steel  33.6 %   (17.4 % weighted)
    bronze  9.7 %   (2.0 % weighted)

A THIRD of the steel rim is a picture of screws that are on the other face.

THE VERDICT: THIS FILE IS AN INSTRUMENT, AND ModBuild 278 DID NOT APPLY IT
--------------------------------------------------------------------------
It works.  At k = 0.20 (a 7.1 mm walk) the steel rim's weighted ghost fraction goes
15.5 % -> 4.2 %, 109 518 of 121 816 rim texels are rewritten, and nothing outside the rim
moves by one byte.  AND THE PICTURE BARELY CHANGES -- see
`.planning/debug/board278/rim_steel_compare.png`, the same edge shot before and after.

So the statistic does not predict what the eye sees here, and this project has lost builds
to exactly that before.  Two things came out of chasing it, and both are worth keeping:

* The prominent three-dimensional-looking objects on the steel side are REAL.  The band
  across the top of the rim shot is the FRONT frame's studded border seen edge-on -- mesh
  geometry, already correct.  What the walk actually ghosts onto the rim is much fainter
  than the ghost fraction implies.
* Splitting the statistic by TERM killed the obvious follow-up hypothesis.  "The visible
  structure must be the FRONT term then" is wrong on the board where it matters: weighted
  by the blend, steel is FRONT 10.1 % against BACK 15.5 %, so the term this file already
  corrects is the dominant one.  (oak FRONT 4.1 / BACK 2.5; bronze FRONT 23.5 / BACK 2.0.)

Changing an accepted texture to move a number the player cannot see is not an improvement.
The file ships so the measurement is repeatable and the option is one command away; use it
if the user reports the side, and read the compare picture first.

WHY THE FIX WOULD NOT BE GEOMETRY EITHER, and this is a judgement call worth stating plainly
--------------------------------------------------------------------------------------------
Read literally, the user asked for the mesh to be adapted to those screws.  Doing that would
mean drilling a rivet row into the EDGE of a plate that has none -- adapting the object to a
sampling artefact rather than the texture to the object.  The screws he is looking at are
real and they are on the back; this round makes them real THERE, and stops the side pretending
to have its own set.  If he wants a genuinely riveted edge band that is a different feature
and a different round: the rim is a wrapped STRIP island, so relief on it needs new UV
islands and therefore an atlas repack, which this round deliberately avoids.

WHY ONE PARAMETER, AND WHY THE FRONT TERM IS NEVER TOUCHED
-----------------------------------------------------------
The endpoints must not move: at t=0 the rim has to equal its FRONT neighbour and at t=1 its
BACK neighbour, or a seam opens.  Both survive any scaling of the walk, because the back
term's offset is (1-t)*W*k and goes to zero at t=1 whatever k is.  So the whole correction
is k, and this file solves for it against the measured ghost fraction with the streak guard
(the rim's own cross-band variation) watched at the same time -- the two pull opposite ways
and `--sweep` prints both.

The edit is written as a DIFFERENCE for the same reason tex_backrelief's is:

    new = old  -  smoothstep(t) * back(old offset)  +  smoothstep(t) * back(new offset)

The FRONT term cancels algebraically, so this file never needs the composited front
board-space albedo (which is an intermediate that was not kept), and at k = 1 it reproduces
the input EXACTLY.  That null is checked, not assumed.
"""

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tex_backfill as tb                                            # noqa: E402

Image.MAX_IMAGE_PIXELS = None
GRP_FRONT, GRP_BACK, GRP_RIM = 1, 2, 3
PW, PH = 2048, 1024


def board_feature_mask(pos, wall, lo, ext, lng, srt, dilate_px, edge_keepout_m):
    """The BACK's wall mask, carried into board space at plate resolution.

    THE FIRST VERSION OF THIS MASK MADE THE SWEEP READ BACKWARDS, and the shape of the
    error is the useful part: the ghost fraction went UP as the walk was shortened, which
    is the opposite of what shortening a walk can do.  Cause: gen_geobuf classifies a
    triangle as BACK on its normal, and steel's 45-degree rim CHAMFER passes that test --
    the round-3 README records the same 204 triangles being swept in.  Those sit AT the
    plate outline, so a short walk lands on them and the mask called that a feature.  A
    keep-out band round the plate's own perimeter removes them; the real features start
    8.9 mm in (the steel rivet row), so the band costs nothing that matters.
    """
    m = np.zeros((PH, PW), bool)
    u = (pos[..., lng] - lo[lng]) / ext[lng]
    v = 1.0 - (pos[..., srt] - lo[srt]) / ext[srt]
    inset = np.minimum(np.minimum(u, 1.0 - u) * ext[lng],
                       np.minimum(v, 1.0 - v) * ext[srt])
    wall = wall & (inset > edge_keepout_m)
    m[np.clip((v[wall] * (PH - 1)).astype(int), 0, PH - 1),
      np.clip((u[wall] * (PW - 1)).astype(int), 0, PW - 1)] = True
    for _ in range(dilate_px):
        e = np.zeros_like(m)
        e[1:] |= m[:-1]
        e[:-1] |= m[1:]
        e[:, 1:] |= m[:, :-1]
        e[:, :-1] |= m[:, 1:]
        m |= e
    return m


def run(a):
    os.makedirs(a.out, exist_ok=True)
    pos = np.load(os.path.join(a.geo, "pos.npy")).astype(np.float64)
    nrm = np.load(os.path.join(a.geo, "nrm.npy")).astype(np.float64)
    grp = np.load(os.path.join(a.geo, "grp.npy"))

    mapped = grp > 0
    lo, hi = pos[mapped].min(0), pos[mapped].max(0)
    ext = hi - lo
    thin = int(np.argmin(ext))
    rest = [i for i in range(3) if i != thin]
    lng, srt = (rest[0], rest[1]) if ext[rest[0]] >= ext[rest[1]] else (rest[1], rest[0])

    mr = grp == GRP_RIM
    mb = grp == GRP_BACK
    if not mr.any():
        raise SystemExit("no RIM texels in this geobuf")

    # the same back plate tex_backfill used, through its own crop and de-shade
    gen = Image.open(a.gen).convert("RGB")
    x0, y0, x1, y1 = tb.plate_rect(gen)
    plate = gen.crop((x0, y0, x1, y1)).resize((tb.PLATE_W, tb.PLATE_H), Image.LANCZOS)
    back_lin = tb.deshade(tb.srgb2lin(np.asarray(plate, np.float64)))

    n = nrm / np.maximum(np.linalg.norm(nrm, axis=2, keepdims=True), 1e-12)
    wall = mb & ((1.0 - np.abs(n[..., thin])) > a.wall_tilt)
    feat = board_feature_mask(pos, wall, lo, ext, lng, srt, a.dilate_px,
                              a.edge_keepout_mm / 1000.0)

    U = (pos[..., lng] - lo[lng]) / ext[lng]
    V = 1.0 - (pos[..., srt] - lo[srt]) / ext[srt]
    Tt = (pos[..., thin] - lo[thin]) / ext[thin]
    front_at_hi = np.mean(pos[grp == GRP_FRONT][:, thin]) > np.mean(pos[mb][:, thin])
    depth = (1.0 - Tt) if front_at_hi else Tt

    nl, ns = n[..., lng][mr], n[..., srt][mr]
    nn = np.hypot(nl, ns)
    ok = nn > 1e-6
    nl = np.where(ok, nl / np.maximum(nn, 1e-9), 0.0)
    ns = np.where(ok, ns / np.maximum(nn, 1e-9), 0.0)
    t = np.clip(depth[mr], 0.0, 1.0)
    ts = (t * t * (3.0 - 2.0 * t))[..., None]
    wl = ext[thin] / ext[lng]
    ws = ext[thin] / ext[srt]

    def sample(k):
        ub = np.clip(U[mr] - nl * (1.0 - t) * wl * k, 0.0, 1.0)
        vb = np.clip(V[mr] + ns * (1.0 - t) * ws * k, 0.0, 1.0)
        return ub, vb, tb.bilinear(back_lin, ub, vb)

    def ghost(k):
        ub, vb, _ = sample(k)
        hit = feat[np.clip((vb * (PH - 1)).astype(int), 0, PH - 1),
                   np.clip((ub * (PW - 1)).astype(int), 0, PW - 1)]
        return float(hit.mean()), float((hit * ts[..., 0]).mean())

    alb_u8 = np.asarray(Image.open(a.albedo).convert("RGB")).copy()
    alb = tb.srgb2lin(alb_u8.astype(np.float64))

    def cross_band(k):
        """THE STREAK GUARD, and the first version of it was the wrong statistic.

        It reported rms(v - v.mean()), which is the TOTAL variation of the back term, and
        that stays high at k = 0 because the sample still moves along the rim's LENGTH.
        What streaking destroys is variation ACROSS the band: at k = 0 every texel of a
        column draws the same colour.  So the guard is the mean |difference| between a
        texel's own sample and the sample the SAME texel would get at t = 1, which is
        exactly the colour its back neighbour has -- zero when the band is a smear of one
        row, and larger the further the walk carries it."""
        _, _, v = sample(k)
        ub = np.clip(U[mr], 0.0, 1.0)
        vb = np.clip(V[mr], 0.0, 1.0)
        e = tb.bilinear(back_lin, ub, vb)
        return float(np.abs(v - e).mean())

    if a.sweep:
        # THE STREAK GUARD.  The rim's cross-band variation is what the walk buys; a walk of
        # zero is the streak defect ModBuild 276 shipped and had to fix.  Reported beside the
        # ghost so the trade is visible instead of assumed.
        print("%-7s  k     ghost%%  weighted%%  cross-band CHANGE of the back term" % a.style)
        for k in (1.00, 0.70, 0.50, 0.40, 0.30, 0.25, 0.20, 0.10, 0.0):
            g, gw = ghost(k)
            print("         %.2f   %5.1f    %5.1f      %.5f  (walk %5.1f mm)"
                  % (k, 100 * g, 100 * gw, cross_band(k),
                     1000.0 * k * float(ext[thin])))
        return

    _, _, old = sample(1.0)
    _, _, new = sample(a.walk)
    out = alb.copy()
    out[mr] = np.clip(alb[mr] - ts * old + ts * new, 0.0, 4.0)
    u8 = alb_u8.copy()
    u8[mr] = np.clip(np.rint(tb.lin2srgb(out[mr])), 0, 255).astype(np.uint8)

    outside = int(((u8 != alb_u8).any(2) & ~mr).sum())
    assert outside == 0, "texels outside the rim moved: %d" % outside
    Image.fromarray(u8).save(os.path.join(a.out, os.path.basename(a.albedo)))

    g1, gw1 = ghost(1.0)
    g2, gw2 = ghost(a.walk)
    _, _, v1 = sample(1.0)
    _, _, v2 = sample(a.walk)
    stats = {
        "style": a.style, "walk": a.walk, "rim_texels": int(mr.sum()),
        "ghost_before_pct": round(100 * g1, 2), "ghost_after_pct": round(100 * g2, 2),
        "ghost_weighted_before_pct": round(100 * gw1, 2),
        "ghost_weighted_after_pct": round(100 * gw2, 2),
        "band_variation_before": round(float(np.sqrt(((v1 - v1.mean(0)) ** 2).mean())), 5),
        "band_variation_after": round(float(np.sqrt(((v2 - v2.mean(0)) ** 2).mean())), 5),
        "rim_texels_changed": int((u8 != alb_u8).any(2).sum()),
        "texels_changed_outside_rim": outside,
        "unroll_mm": round(1000.0 * float(ext[thin]), 2),
    }
    print(json.dumps(stats, indent=1))
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(stats, fh, indent=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--style", required=True)
    ap.add_argument("--geo", required=True)
    ap.add_argument("--gen", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default=None)
    ap.add_argument("--walk", type=float, default=0.28,
                    help="scale on the BACK term's inward walk; 1.0 reproduces the input "
                         "exactly and is the null control")
    ap.add_argument("--sweep", action="store_true",
                    help="print the ghost fraction and the streak guard against k, write nothing")
    ap.add_argument("--wall-tilt", type=float, default=0.08)
    ap.add_argument("--edge-keepout-mm", type=float, default=4.0,
                    help="ignore wall texels this close to the plate's own outline -- \n                         that band is the rim CHAMFER, which gen_geobuf calls BACK")
    ap.add_argument("--dilate-px", type=int, default=6,
                    help="plate pixels of dilation on the back feature mask -- a feature's "
                         "painted shadow lies beside its edge, not on it")
    run(ap.parse_args())


if __name__ == "__main__":
    main()
