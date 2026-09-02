#!/usr/bin/env python3
"""Keycap symbol stencils DRAWN as geometry, for cells that need no generator at all.

    from cap_stencils import stencil, NAMES
    mask = stencil("stake")          # bool ndarray, True = ink

WHY A DRAWN STENCIL IS NOT A DOWNGRADE FROM A GENERATED ONE
-----------------------------------------------------------
`cap_atlas.py` states the rule it inherited from the board pipeline: **a generated image is a
BINARY STENCIL, nothing else.** It is thresholded through `tex_symbols._cell_binary`, the bevel
is rebuilt from an exact distance transform and the motif is carved into a procedural material
-- "that is the whole reason the result stops reading as AI". Every grey, every halo and every
brush texture the model produced is thrown away before the cell is built.

So the only thing a generated cell ever contributed to a cap was an OUTLINE. Drawing that
outline as polygons and arcs gives the pipeline exactly the same input, and gives it three
things a generated one cannot:

  * it is REPRODUCIBLE from this file -- no `../out/` image, which is gitignored and therefore
    absent from every diff and every fresh clone, has to exist for the atlas to rebuild;
  * the LIMB WIDTHS are authored rather than discovered. `cap_atlas.min_feature_px` is the
    number that decides whether a symbol survives being carved at 143 cell texels and then
    seen at ~75 screen px on a Quest 3; here it is a constant in the drawing instead of a
    property of whatever came back;
  * it can be REVISED. A generated cell can only be re-rolled.

`tex_symbols` already carries the same idea for the board's own ornament (`proc_centre_rose`,
`proc_rest_short`, ...), described there as "the final path for anything that must tile or must
be geometrically exact". This is that path, for the cap symbols.

THE NORMAL FORM, and why it is worth the four lines
---------------------------------------------------
`cap_atlas.place` treats every stencil the same way -- `tex_symbols._resize_mask` reconstructs
the edge from the mask's signed distance field and supersamples it down into the cell. For that
to be true, a drawn stencil has to arrive in the same shape a sheet cell arrives in: TRIMMED to
its ink and PADDED BACK TO SQUARE, which is what `tex_symbols._cell_binary` ends with and what
`_squarify` is. If a drawing were handed over untrimmed, its own empty margin would be scaled
into the cell as if it were part of the motif, and the symbol would come out smaller than
`SOLO_SIZE` says -- silently, and differently for every drawing. So `stencil()` ends with the
same trim + `_squarify` a generated cell ends with, and the two kinds are interchangeable from
`place()` onward.

EVERYTHING IS IN UNIT COORDINATES, NOT TEXELS
---------------------------------------------
Every number in a `_draw_*` function is a fraction of the drawing's side, y growing DOWN
(PIL's convention, and `cap_atlas` documents that image-top is cap-top so no flip happens
anywhere). That makes `side` a pure sampling knob: doubling it must change the smoothness of
the edge and nothing about the shape. It also makes the limb widths readable as what they are
-- a limb of 0.09 is 0.09 * 143 = 12.9 cell texels at `SOLO_SIZE`, which is the only scale the
legibility question is asked at.

THE MOTIFS, AND WHAT EACH ONE HAS TO PAIR WITH
----------------------------------------------
`FixedPinned` and `FixedFollow` are the two states of ONE toggle -- the board either stays where
the player put it or follows him. A player has to tell them apart in one glance at a symbol
about 75 screen px across, so the requirement is not "both look good": it is that the two
SILHOUETTES are different in kind. `FixedFollow` is two bare footprints -- two soft vertical
blobs with toes. So every candidate here is built around a HEAVY HORIZONTAL GROUND LINE or a
single closed body, because that is the axis the footprints do not use.

    stake     an iron ground stake driven through a ground line, hammered head mushroomed
              flat at the top. Depicts the act the toggle performs. Pairs by opposition:
              feet that walk against iron driven into the earth.
    padlock   a medieval barrel padlock, chunky body and a thick shackle. The most legible
              silhouette of the four at small sizes because it is nearly all body.
    boot      one boot in side profile planted on a heavy ground bar -- the literal opposite
              of two moving footprints, in the same visual vocabulary.
    picket    a picket pin: a heavy ring at the top, a tapering spike, a ground line. The
              stake's sibling; the ring gives it a distinct top at the cost of a hole.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, PREP)

import tex_symbols as S        # noqa: E402  (SS, and the trim/squarify normal form)

# The drawing resolution. It is NOT a design parameter -- see EVERYTHING IS IN UNIT COORDINATES
# above -- but it is not free either: `place()` resizes the stencil down to 0.56 * 256 = 143
# cell texels through an SDF, and an SDF resampled from a coarse mask carries that mask's own
# quantisation into the sub-texel edge it reconstructs. 1024 with `tex_symbols.SS` = 4 means
# every outline is laid down at 4096 and area-averaged twice before it reaches the cell, which
# is well past the point where the drawing's sampling could be the limiting term.
DEFAULT_SIDE = 1024


# ---------------------------------------------------------------------------
# drawing primitives -- unit coordinates in, supersampled ink out
# ---------------------------------------------------------------------------
def _canvas(side):
    """A supersampled scratch bitmap and the scale that maps unit coordinates onto it."""
    s = side * S.SS
    img = Image.new("L", (s, s), 0)
    return img, ImageDraw.Draw(img), float(s)


def _reduce(img, side):
    """Area-average the supersample down and threshold. A stencil is BINARY by contract, so
    the antialiasing is thrown away here on purpose -- `place()` regenerates it from the
    signed distance field, which is sub-texel accurate where a grey edge is not."""
    small = np.asarray(img.resize((side, side), Image.Resampling.BOX), dtype=np.float64) / 255.0
    return small >= 0.5


def _poly(dr, pts, s, fill=255):
    dr.polygon([(x * s, y * s) for x, y in pts], fill=fill)


def _rect(dr, x0, y0, x1, y1, s, fill=255, r=0.0):
    box = [x0 * s, y0 * s, x1 * s, y1 * s]
    if r > 0.0:
        dr.rounded_rectangle(box, radius=r * s, fill=fill)
    else:
        dr.rectangle(box, fill=fill)


def _disc(dr, cx, cy, r, s, fill=255):
    dr.ellipse([(cx - r) * s, (cy - r) * s, (cx + r) * s, (cy + r) * s], fill=fill)


def _limb(dr, p0, w0, p1, w1, s, fill=255, caps=True):
    """A tapered limb: a quad from `p0` at width `w0` to `p1` at width `w1`, with round caps.

    Round caps rather than mitres because limbs here are joined end-to-end (a shaft into a
    head, a shackle leg into a body) and a mitred join leaves a notch that the carve's chamfer
    then eats from both sides -- the one failure mode `min_feature_px` exists to catch.
    """
    (x0, y0), (x1, y1) = p0, p1
    dx, dy = x1 - x0, y1 - y0
    ln = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / ln, dx / ln
    h0, h1 = w0 * 0.5, w1 * 0.5
    _poly(dr, [(x0 + nx * h0, y0 + ny * h0), (x1 + nx * h1, y1 + ny * h1),
               (x1 - nx * h1, y1 - ny * h1), (x0 - nx * h0, y0 - ny * h0)], s, fill)
    if caps:
        _disc(dr, x0, y0, h0, s, fill)
        _disc(dr, x1, y1, h1, s, fill)


def _arc_band(dr, cx, cy, r, w, a0, a1, s, fill=255, steps=96):
    """A constant-width arc, drawn as a closed strip rather than with `dr.arc`.

    `dr.arc` strokes in DEVICE pixels and its width is an integer, so an arc's limb width
    would stop being a fraction of the side the moment `side` changed -- exactly the coupling
    the unit-coordinate rule exists to prevent. Angles are degrees, measured the way PIL
    measures them (0 = +x, growing CLOCKWISE on screen because y points down).
    """
    outer, inner = [], []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        ca, sa = math.cos(a), math.sin(a)
        outer.append((cx + (r + w * 0.5) * ca, cy + (r + w * 0.5) * sa))
        inner.append((cx + (r - w * 0.5) * ca, cy + (r - w * 0.5) * sa))
    _poly(dr, outer + inner[::-1], s, fill)


# ---------------------------------------------------------------------------
# the motifs
# ---------------------------------------------------------------------------
# A NOTE ON EVERY WIDTH BELOW. The cell is 256 texels, `SOLO_SIZE` is 0.56, so a unit width w
# lands at w * 143 cell texels; the shipped anchor measures 6.3 there and the footprints 10.2.
# Nothing here is thinner than 0.075 (= 10.7 texels), and the load-bearing masses are 0.10 and
# up. That is the whole reason these read as chunky iron rather than as line art: the carve's
# chamfer (`RIM_FRAC` = 0.014 of the cell, i.e. 3.6 texels) eats a limb from BOTH sides, so a
# limb under ~8 texels never reaches the groove floor and never gets its ambient occlusion.

def _draw_stake(dr, s):
    """An iron ground stake driven through the earth: hammered head, tapering shaft, ground
    line. The head is MUSHROOMED -- wider at the top than at the neck -- because that is what
    a struck iron head looks like and because it is what stops the silhouette reading as a
    cross or a dagger, which is the failure a plain bar-and-shaft has."""
    # ground line: a hewn beam with chamfered ends, heavy enough to survive on its own
    _poly(dr, [(0.030, 0.808), (0.075, 0.762), (0.925, 0.762), (0.970, 0.808),
               (0.925, 0.872), (0.075, 0.872)], s)
    # the shaft, driven through it and tapering to a blunt point below
    _limb(dr, (0.500, 0.235), 0.240, (0.500, 0.935), 0.135, s, caps=False)
    _poly(dr, [(0.4325, 0.930), (0.5675, 0.930), (0.500, 1.000)], s)
    # the mushroomed head: a crown wider than the neck, with the neck flaring out to meet it
    _rect(dr, 0.195, 0.048, 0.805, 0.168, s, r=0.048)
    _poly(dr, [(0.230, 0.158), (0.770, 0.158), (0.648, 0.252), (0.352, 0.252)], s)


def _draw_padlock(dr, s):
    """A medieval barrel padlock: one closed body and a thick shackle over it. The most
    body-heavy of the four, which is why it is the one that survives furthest down in size --
    almost none of its area is limb."""
    # the shackle first, so the body draws over its legs and the join has no seam
    _arc_band(dr, 0.500, 0.452, 0.222, 0.125, 180.0, 360.0, s)
    _limb(dr, (0.278, 0.452), 0.125, (0.278, 0.560), 0.125, s, caps=False)
    _limb(dr, (0.722, 0.452), 0.125, (0.722, 0.560), 0.125, s, caps=False)
    # the body: a banded barrel, slightly wider at the foot the way a struck lock case is
    _rect(dr, 0.105, 0.492, 0.895, 0.930, s, r=0.110)
    _poly(dr, [(0.105, 0.730), (0.895, 0.730), (0.930, 0.930), (0.070, 0.930)], s)
    # the keyhole, cut back out. It fills in below ~60 screen px and that is accepted: what
    # carries the symbol is the closed body under a shackle, not the hole in it.
    _disc(dr, 0.500, 0.686, 0.100, s, fill=0)
    _poly(dr, [(0.425, 0.706), (0.575, 0.706), (0.610, 0.868), (0.390, 0.868)], s, fill=0)


def _draw_boot(dr, s):
    """One boot in side profile, planted on a heavy ground bar -- the literal opposite of two
    footprints, said in the footprints' own vocabulary. Side profile rather than a single sole
    print ON PURPOSE: a single top-down sole is a VARIATION on two top-down soles, and the one
    thing the pair may not be is variations of each other."""
    # ground bar
    _poly(dr, [(0.015, 0.930), (0.055, 0.888), (0.945, 0.888), (0.985, 0.930),
               (0.945, 0.988), (0.055, 0.988)], s)
    # THE BOOT IS AN L, and that is the whole of what makes it read. A boot seen from the side
    # is one horizontal mass (the foot) meeting one vertical mass (the leg); the first version
    # here sloped the vamp between them, which merged the two into a single trapezoid and came
    # out as a bucket. The two masses are drawn separately and left to overlap at the ankle.
    _rect(dr, 0.435, 0.185, 0.805, 0.815, s)                 # the leg
    _rect(dr, 0.090, 0.625, 0.805, 0.848, s, r=0.092)        # the foot, toe rounded at the left
    # the sole: a heavy slab under the whole length, standing ON the bar
    _poly(dr, [(0.068, 0.800), (0.818, 0.800), (0.836, 0.890), (0.050, 0.890)], s)
    # the cuff, a turned-over band across the top of the shaft
    _rect(dr, 0.392, 0.120, 0.848, 0.268, s, r=0.045)


def _draw_picket(dr, s):
    """A picket pin: a ring to tether to, a tapering spike, a ground line. The stake's sibling.
    The ring is the only enclosed hole in this file, so it is drawn deliberately fat -- an
    annulus whose HOLE is 0.170 of the side, i.e. 24 cell texels, because a hole narrower than
    the carve's chamfer closes up and the ring becomes a lollipop."""
    _arc_band(dr, 0.500, 0.198, 0.150, 0.116, 0.0, 360.0, s)
    _limb(dr, (0.500, 0.290), 0.190, (0.500, 0.930), 0.120, s, caps=False)
    _poly(dr, [(0.030, 0.790), (0.072, 0.746), (0.928, 0.746), (0.970, 0.790),
               (0.928, 0.852), (0.072, 0.852)], s)
    _poly(dr, [(0.440, 0.925), (0.560, 0.925), (0.500, 1.000)], s)


_DRAW = {
    "stake": _draw_stake,
    "padlock": _draw_padlock,
    "boot": _draw_boot,
    "picket": _draw_picket,
}

# One line each, printed by `--report` and by `cap_atlas`'s cell table so a reader of either
# never has to open this file to find out what a cell depicts.
WHAT = {
    "stake": "fixed_pinned (iron ground stake driven through a ground line)",
    "padlock": "fixed_pinned ALTERNATE (medieval barrel padlock)",
    "boot": "fixed_pinned ALTERNATE (one boot planted on a ground bar)",
    "picket": "fixed_pinned ALTERNATE (picket pin: ring, spike, ground line)",
}

NAMES = list(_DRAW)


def stencil(name, side=DEFAULT_SIDE):
    """The named motif as a binary stencil: bool ndarray, True = ink, trimmed and square.

    Deterministic -- there is no seed and no randomness anywhere in this file. Two calls with
    the same `side` return bit-identical arrays, which is what lets `cap_atlas` be rebuilt from
    its own sources with no `../out/` image present.
    """
    if name not in _DRAW:
        raise KeyError(f"no drawn stencil named {name!r}; have {NAMES}")
    img, dr, s = _canvas(side)
    _DRAW[name](dr, s)
    m = _reduce(img, side)
    if not m.any():
        return m
    # The normal form a sheet cell arrives in. See THE NORMAL FORM in the module docstring.
    ys, xs = np.where(m)
    return S._squarify(m[ys.min():ys.max() + 1, xs.min():xs.max() + 1])


def main():
    """`python3 cap_stencils.py [--out DIR]` -- write every motif out and print its numbers.

    The numbers are the ones that decide, not decoration: `min limb` is
    `cap_atlas.min_feature_px`, in stencil texels and again at the 143-texel cell scale
    `SOLO_SIZE` gives a solo symbol, which is the scale the legibility question is asked at.
    """
    import argparse
    import cap_atlas as A

    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--side", type=int, default=DEFAULT_SIDE)
    ap.add_argument("--cell", type=int, default=256, help="atlas cell side, for the cell-scale "
                                                          "limb figure")
    ap.add_argument("--out", default=None, help="write <name>_stencil.png here")
    args = ap.parse_args()

    for name in NAMES:
        m = stencil(name, args.side)
        cov = A.place(m, args.cell, A.SOLO_SIZE, A.SOLO_CY)
        print(f"  {name:<10} {WHAT[name]:<52} {m.shape[0]:4d}px stencil, "
              f"ink {m.mean() * 100:5.1f}%, min limb {A.min_feature_px(m):5.1f}px src "
              f"-> {A.min_feature_px(cov >= 0.5):4.1f}px cell")
        if args.out:
            os.makedirs(args.out, exist_ok=True)
            Image.fromarray((m * 255).astype(np.uint8), "L").save(
                os.path.join(args.out, f"{name}_stencil.png"))


if __name__ == "__main__":
    main()
