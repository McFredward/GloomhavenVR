"""tex_symbols.py -- the decorative motif layer.

THE CENTRAL DECISION IN THIS FILE
---------------------------------
A generated image never becomes ALBEDO. It becomes a MASK, from which we build a
bevelled height stencil ourselves. The compositor then carves that stencil into
the board, and the colour you see in the carving is the board's own material
under the board's own lighting.

That is the direct answer to "die Symbole darauf sehen nicht clean sondern
KI-generiert aus". What makes AI ornament read as AI is not its shape -- shapes
are the one thing diffusion is genuinely good at -- it is everything that comes
with the shape: baked lighting, soft edges, drifting palette, a background that
is subtly not the background. Keep the shape, throw all of that away, and the
motif inherits the material instead of fighting it.

Consequences, and they are the reason the prompts below look the way they do:
  * We ask for FLAT WHITE ON FLAT BLACK. No grey, no shading, no relief, no
    perspective. Shading from a diffusion model is the mush; our bevel comes
    from an exact Euclidean distance transform and is clean by construction.
  * One motif serves all three styles, because a stencil has no colour to
    restyle. No x3 multiplier on the generation bill.
  * A motif that must TILE is procedural, not generated. Diffusion cannot close
    a seam, and the border runs and dentil strips must.

Every symbol is stored as RGBA: RGB = the bevelled height in [0,1],
A = antialiased coverage. Placeholders and processed AI output are
indistinguishable to the compositor, so the whole pipeline runs today.
"""

import argparse
import math
import os

import numpy as np
from PIL import Image, ImageDraw

import tex_common as T

SS = 4  # supersample factor for all vector drawing


# ==========================================================================
# THE SPECIFICATION
# ==========================================================================
#
#   name        the compositor and the UV json refer to the motif by this
#   px          the pixel size the motif is composited at, at a 2048 atlas
#   source      "ai"   -> a cell of a generated sheet, post-processed here
#               "proc" -> generated procedurally, final, never AI
#   shared      True  -> one motif serves Oak, Steel and Bronze
#   uses        where it appears and how many times per board
#   why         why it is on this side of the ai/proc line
#
# `px` is the size the motif is STORED at. It is not the size it is composited
# at: the compositor asks for an exact pixel size per placement, measured
# against the room the mesh actually leaves (tex_atlas.fit_placements), and
# get_symbol resamples the stored binary through one SDF resize to get there.
# The stored size only has to be at least as large as the largest request, so
# that resample is never an upscale of an upscale.
#
# The numbers below were set from the REBUILT boards, not from the old default
# layout. Measured room on the three meshes, at a 2048 atlas: slot-floor
# rosette 220-272 px, rest pad glyph 196-212 px, button seat 92-104 px, the
# widest spot anywhere on the face web 92-98 px, the frame band 40-53 px.
SYMBOL_SPEC = [
    dict(name="centre_rose", px=320, source="ai", shared=True, cell="A1",
         uses="slot_floor, 2x, one on each card recess floor (the roomiest "
              "decorated surface the board has); face, 2x, on the wide spot at "
              "the middle of the top and bottom field margin",
         why="the one large hero ornament; hand-vectoring a 12-fold rosette with "
             "interior filigree is hours of work and is exactly what diffusion is "
             "good at once you only keep its silhouette"),
    dict(name="corner_bracket", px=224, source="ai", shared=True, cell="A2",
         uses="face, 4x, mirrored into the four corners of the field web",
         why="an L-shaped scrollwork corner; asymmetric organic curve, tedious by "
             "hand, and it is used mirrored so only one is needed"),
    dict(name="rest_short", px=224, source="ai", shared=True, cell="A3",
         uses="rest_pads, 1x, the ShortRestToken pad",
         why="must read instantly as 'short rest' at 25 mm; a crescent-and-ember "
             "device is iconographic work, not geometry"),
    dict(name="rest_long", px=224, source="ai", shared=True, cell="B1",
         uses="rest_pads, 1x, the LongRestToken pad",
         why="its pair; must be distinguishable from rest_short at a glance"),
    dict(name="maker_mark", px=224, source="ai", shared=True, cell="B2",
         uses="face, 1x, on the wide spot at the middle of the bottom field margin",
         why="a small heraldic cartouche that gives the board an author; pure "
             "ornament, no functional meaning, so a generated silhouette is safe"),
    dict(name="rose_alt", px=320, source="ai", shared=True, cell="B3",
         uses="slot_floor / face, alternate for centre_rose (denser variant)",
         why="a second rosette on the SAME sheet costs nothing and gives a choice "
             "without a second call"),
    dict(name="bracket_alt", px=224, source="ai", shared=True, cell="C1",
         uses="face, alternate for corner_bracket (plainer variant)",
         why="same argument; a plainer bracket suits Steel where the ornate one "
             "suits Oak, and both come out of one call"),
    dict(name="pip", px=128, source="ai", shared=True, cell="C3",
         uses="face, 1x on the centre bridge between the two card recesses, "
              "which is 12 mm wide and fits nothing larger",
         why="spare cell, free; a tiny lozenge used as punctuation"),

    dict(name="seat_bezel", px=224, source="proc", shared=True, cell=None,
         uses="button_seats, 3x, one ring per seat",
         why="a concentric annulus with evenly spaced rivets. Perfect circles and "
             "exact angular spacing; generating this would be strictly worse and "
             "would cost money"),
    dict(name="slot_corner", px=112, source="proc", shared=True, cell=None,
         uses="NOT PLACED on the rebuilt boards -- kept because it is free and "
              "the next layout may want it",
         why="a right-angle tick. Pure geometry. It was going to sit in the four "
             "corners of each card recess floor, and it is not there: a recess "
             "floor is under a card for most of the game, and what shows when it "
             "is empty should be one clean ornament rather than one ornament plus "
             "eight ticks fighting it at 24 mm"),
    dict(name="border_run", px=(1024, 128), source="proc", shared=True, cell=None,
         uses="SUPERSEDED by tex_atlas.frame_relief, which is the same ornament "
              "evaluated per texel from the board coordinate instead of stamped "
              "as a strip",
         why="MUST TILE. Diffusion cannot close a seam; this is the single "
             "hardest constraint in the whole texture job and it is trivial "
             "procedurally. Stamping a strip solved the seam along the run but "
             "not at the four corners, and it had to be told where the frame "
             "was; a function of board position closes both and cannot be "
             "aimed at the wrong island"),
    dict(name="edge_dentil", px=(1024, 96), source="proc", shared=True, cell=None,
         uses="SUPERSEDED by tex_atlas.side_relief, same argument",
         why="MUST TILE, same argument"),
]

# ---------------------------------------------------------------------------
# EDITORIAL BLOCK -- a cell that came back fine and is deliberately not used.
#
# `rest_alt` (cell C2) came back on BOTH sheets as a crescent enclosing a
# six-pointed star. Each of those two devices is a real-world religious symbol
# and the combination reads as a deliberate juxtaposition of two faiths. It was
# a spare cell asked for because it was free; the board does not need it,
# `rest_short` and `rest_long` are the two rest glyphs that matter and both came
# back good on both sheets. So the cell is extracted for verification -- it is
# part of what was paid for and the acceptance test still measures it -- and
# then dropped: it is not in SYMBOL_SPEC, no motif file is written for it, and
# there is no procedural stand-in for it either, so no placement can reach it by
# any path.
BLOCKED_CELLS = {
    "rest_alt": "crescent enclosing a six-pointed star -- reads as a combination "
                "of two real-world religious symbols; spare cell, not needed",
}

SYMBOLS = {s["name"]: s for s in SYMBOL_SPEC}
AI_NAMES = [s["name"] for s in SYMBOL_SPEC if s["source"] == "ai"]
PROC_NAMES = [s["name"] for s in SYMBOL_SPEC if s["source"] == "proc"]


# ==========================================================================
# THE GENERATION REQUEST -- exactly what to send, and how many times
# ==========================================================================

SHEET_SIZE = 1024
SHEET_GRID = (3, 3)   # cols, rows -> cells A1..C3, row-major, A1 top-left

_SHEET_RULES = """\
Strict output rules, all of them mandatory:
- Pure flat WHITE shapes on a pure flat BLACK background. Only #FFFFFF and #000000.
- No grey, no gradients, no shading, no relief, no embossing, no bevel, no glow,
  no drop shadow, no outline halo, no texture, no paper, no parchment, no noise.
- Completely flat and frontal. No perspective, no 3D, no thickness.
- No text, no letters, no numerals, no signature, no watermark, no frame border
  around the whole image, no grid lines between the cells.
- Every shape crisp-edged, as if cut from paper with a scalpel.
- Nine motifs in a 3x3 grid, each centred in its cell with clear black space
  around it; the cells are separated only by black space, never by a drawn line.
- Each motif fully inside its cell, symmetrical where described, and drawn with
  strokes thick enough to survive being reduced to 200 pixels wide."""

SHEETS = [
    dict(
        id="sheet_a",
        size=SHEET_SIZE,
        prompt=(
            "A 3x3 grid of nine flat stencil motifs, white on black, in the style of "
            "medieval European guild ornament and heraldic woodcut.\n\n"
            "Top row, left to right:\n"
            "1. A twelve-pointed compass rosette inside two concentric rings, with a "
            "small six-petal flower at its exact centre; strictly rotationally "
            "symmetrical.\n"
            "2. An L-shaped corner scrollwork bracket that fits into the top-left "
            "corner of a rectangle: two straight arms meeting at a right angle, with a "
            "spiral volute where they meet and a leaf curl on each arm.\n"
            "3. A waning crescent moon with three small flames rising inside its "
            "hollow; compact and circular overall.\n\n"
            "Middle row, left to right:\n"
            "4. A full circle divided into eight equal wedges around a small central "
            "disc, with a ring of eight dots just inside its rim.\n"
            "5. A small heraldic shield with a straight top edge and a pointed base, "
            "carrying a single upright hammer crossed by a single upright key.\n"
            "6. A sixteen-pointed star rosette inside one ring, denser and finer than "
            "motif 1, with a small eight-petal flower at its centre; strictly "
            "rotationally symmetrical.\n\n"
            "Bottom row, left to right:\n"
            "7. An L-shaped corner bracket like motif 2 but plain and severe: two "
            "straight tapered arms meeting at a right angle with a single square stud "
            "at the join and no curls.\n"
            "8. A crescent moon enclosing a single six-pointed star; compact and "
            "circular overall.\n"
            "9. A small pointed lozenge, wider than tall, with a dot at each of its "
            "four points.\n\n" + _SHEET_RULES
        ),
        cells={"A1": "centre_rose", "A2": "corner_bracket", "A3": "rest_short",
               "B1": "rest_long", "B2": "maker_mark", "B3": "rose_alt",
               "C1": "bracket_alt", "C2": "rest_alt", "C3": "pip"},
    ),
    dict(
        id="sheet_b",
        size=SHEET_SIZE,
        prompt=(
            "A 3x3 grid of nine flat stencil motifs, white on black, in the style of "
            "Norse and Celtic interlace carving. Same nine subjects as before but a "
            "different ornamental hand throughout, so the two sheets can be compared "
            "cell by cell.\n\n"
            "Top row, left to right:\n"
            "1. A twelve-pointed compass rosette inside two concentric rings, the "
            "rings woven as a simple over-under interlace, with a small knot at its "
            "exact centre; strictly rotationally symmetrical.\n"
            "2. An L-shaped corner bracket built from a single interlaced band that "
            "loops once at the right-angle join and terminates in a beast head on "
            "each arm.\n"
            "3. A waning crescent moon with three small flames rising inside its "
            "hollow, the crescent itself formed from a plaited band.\n\n"
            "Middle row, left to right:\n"
            "4. A full circle divided into eight equal wedges around a small central "
            "knot, with a ring of eight dots just inside its rim.\n"
            "5. A small heraldic shield with a straight top edge and a pointed base, "
            "carrying a single upright hammer crossed by a single upright key, the "
            "shield edge formed from a plaited band.\n"
            "6. A sixteen-pointed star rosette inside one interlaced ring, denser and "
            "finer than motif 1; strictly rotationally symmetrical.\n\n"
            "Bottom row, left to right:\n"
            "7. An L-shaped corner bracket, plain and severe: two straight tapered "
            "arms meeting at a right angle with a single square stud at the join.\n"
            "8. A crescent moon enclosing a single six-pointed star.\n"
            "9. A small pointed lozenge, wider than tall, with a dot at each of its "
            "four points.\n\n" + _SHEET_RULES
        ),
        cells={"A1": "centre_rose", "A2": "corner_bracket", "A3": "rest_short",
               "B1": "rest_long", "B2": "maker_mark", "B3": "rose_alt",
               "C1": "bracket_alt", "C2": "rest_alt", "C3": "pip"},
    ),
]

CALL_COUNT = len(SHEETS)

CALL_RATIONALE = """\
TOTAL CALLS REQUESTED: 2 (two 1024x1024 sheets, nine motifs each).

Why it cannot be fewer than 1:
  Every AI motif on the board is on ONE sheet. Nine motifs, one call. There is no
  arrangement that gets a generated ornament onto the board for zero calls.

Why 2 and not 1:
  1 is the floor only if the sheet comes back usable in all the cells we need. It
  will not: the reliable failure modes of a nine-cell grid are (a) two cells
  bleed into each other, (b) one cell comes back with grey shading, (c) one motif
  is asymmetric where the prompt said symmetrical. Any ONE of those forces a
  re-call in a 1-call plan, and a re-call is a whole sheet. So the honest cost of
  the 1-call plan is 1 + P(any cell fails) x 1, and with nine independent cells
  that probability is not small.
  Asking for 2 up front costs the same as the likely 1-plus-a-reroll, and buys
  something the reroll does not: the two sheets are deliberately in DIFFERENT
  ornamental hands (guild woodcut vs Norse interlace), so we choose per cell and
  can also give Oak, Steel and Bronze different ornament from the same two calls.

Why there is no x3 for the three styles:
  A motif is used as a height stencil. It has no colour to restyle, so Oak, Steel
  and Bronze carve the SAME nine shapes and each one picks up its own material.
  A per-style generation would have been 27 motifs and would have looked worse,
  because three separately generated rosettes do not match each other.

Why the tiling ornament is not on the sheet:
  border_run and edge_dentil must close a seam. Diffusion cannot. They are
  procedural and always will be."""


# ==========================================================================
# post-processing a generated sheet
# ==========================================================================

def _otsu(gray, detail=False):
    """Otsu threshold, numpy only. Used because the sheet's black may come back
    at 0.04 rather than 0.00 and a fixed 0.5 would then be a guess.

    THE BUG THIS REPLACES -- it shipped, and it destroyed both delivered sheets.
    ---------------------------------------------------------------------------
    The previous implementation computed the between-class variance as

        w  = np.cumsum(hist)                 # a cumulative COUNT
        m  = np.cumsum(hist * centres)       # count * intensity, UNNORMALISED
        mt = m[-1]
        between = (mt * w - m) ** 2 / (w * (total - w))

    Otsu's numerator is (mu_T * omega(t) - mu(t)) with omega and mu both
    NORMALISED by the pixel count. Here the first term carries an extra factor
    of `total` that the second does not, so with omega = w/total the expression
    reduces to

        total^2 * mu_T^2 * omega / (1 - omega)   + lower-order terms

    which is MONOTONICALLY INCREASING in omega. Its argmax is therefore the top
    of the occupied histogram no matter what the picture is; the function was
    not measuring anything. Measured on the two delivered sheets it returned
    0.99414 for all nine cells of BOTH sheets -- a constant wearing the clothes
    of a measurement -- and at that threshold the slice falls INSIDE the white
    lobe, cutting the anti-aliased rim off every stroke and shattering the
    motifs into dots and dashes.

    It survived --selftest because the synthetic sheet is near-binary: slicing a
    0/1 image at 0.994 still separates ink from ground perfectly. Only a sheet
    whose white sits at 250-255 rather than exactly 255 exposes it.

    The corrected form normalises both terms, and where the between-class
    variance has a flat plateau (a genuinely empty valley between two lobes) it
    returns the MIDDLE of the plateau rather than np.argmax's first index --
    otherwise a perfectly bimodal image gets a threshold pinned to the bottom of
    its valley, which is the same failure mirrored.

    With detail=True also returns (t, mu_below, mu_above, valley_fraction) so a
    caller can refuse a threshold that is not sitting in a valley.
    """
    hist, edges = np.histogram(gray, bins=256, range=(0.0, 1.0))
    total = float(hist.sum())
    if total == 0:
        return (0.5, 0.0, 1.0, 1.0) if detail else 0.5
    p = hist / total
    centres = (edges[:-1] + edges[1:]) * 0.5
    omega = np.cumsum(p)
    mu = np.cumsum(p * centres)
    mu_t = mu[-1]
    with np.errstate(invalid="ignore", divide="ignore"):
        between = (mu_t * omega - mu) ** 2 / (omega * (1.0 - omega))
    between = np.nan_to_num(between, nan=-1.0, posinf=-1.0, neginf=-1.0)
    top = between.max()
    plateau = np.flatnonzero(between >= top - 1e-12)
    k = int(round(float(plateau.mean())))
    t = float(centres[k])
    if not detail:
        return t
    below = omega[k]
    mu_lo = float(mu[k] / below) if below > 1e-12 else 0.0
    mu_hi = float((mu_t - mu[k]) / (1.0 - below)) if below < 1.0 - 1e-12 else 1.0
    g = np.asarray(gray)
    valley = float(np.mean(np.abs(g - t) < 0.06))
    return t, mu_lo, mu_hi, valley


def erode(mask, r):
    """Exact Euclidean erosion (radius in pixels)."""
    return T.edt(~np.asarray(mask, dtype=bool)) > r


def dilate(mask, r):
    """Exact Euclidean dilation (radius in pixels)."""
    return T.edt(np.asarray(mask, dtype=bool)) <= r


def morph_clean(mask, open_r=1.0, close_r=1.0):
    """Morphological open then close. KEPT, but no longer the default -- see
    clean_mask below for the measurement that demoted it."""
    m = np.asarray(mask, dtype=bool)
    if open_r > 0:
        m = dilate(erode(m, open_r), open_r)
    if close_r > 0:
        m = erode(dilate(m, close_r), close_r)
    return m


def clean_mask(mask, min_area=None, frac=0.012):
    """Remove speckle and fill pinholes WITHOUT moving a single boundary texel.

    Foreground components smaller than `min_area` are deleted; enclosed
    background components smaller than `min_area` are filled. Nothing else is
    touched, so every edge of every surviving stroke is exactly where the
    threshold put it.

    WHY THIS REPLACED THE MORPHOLOGICAL OPEN/CLOSE, measured on the two
    delivered sheets (18 cells) against the same cells thresholded at 0.5, plus
    a cell deliberately damaged with 2% salt-and-pepper:

      cleanup                worst IoU   worst hole area kept   2% s+p IoU
      area, min_area=17          0.9919              97.6%          0.9886
      area 17 then open r=1      0.9863              97.9%          0.9779
      morphological r=1.0        0.9348              92.3%          0.9782
      morphological r=1.71       0.9154              80.7%          0.9625   <- shipped

    The last column is the point: the area filter is not a trade of robustness
    for fidelity. It is BETTER at removing salt-and-pepper than the morphology
    that was there to remove it.

    The shipped radius was r = cell/200 = 1.71 texels, which is a stroke-fattening
    operation, not a despeckle: on the Norse interlace sheet it ate a fifth of
    the area of the plait's own openings, welding strands that the eye can still
    see are separate. And it was WORSE at the job it was there for -- deleting
    a component by its area removes speckle exactly, while eroding by a radius
    removes speckle and one texel of everything else.

    min_area defaults to (frac * min(side))^2, i.e. anything smaller than a
    ~4x4 blob at a 341 px cell. That is below the smallest deliberate feature on
    either sheet (the pip's corner dots are ~90 texels) and above the largest
    accidental one."""
    m = np.asarray(mask, dtype=bool)
    if min_area is None:
        min_area = max(4, int(round((frac * min(m.shape)) ** 2)))
    lab, n = _label(m, conn=8)
    if n:
        sz = np.bincount(lab.ravel(), minlength=n + 1)
        drop = sz < min_area
        drop[0] = False
        m = m & ~drop[lab]
    bg, nb = _label(~m, conn=4)
    if nb:
        sz = np.bincount(bg.ravel(), minlength=nb + 1)
        border = set(np.unique(np.concatenate(
            [bg[0], bg[-1], bg[:, 0], bg[:, -1]])).tolist())
        fill = (sz < min_area)
        fill[0] = False
        for b in border:
            if b:
                fill[b] = False
        m = m | fill[bg]
    return m


def bevel_from_mask(cov, bevel_px=6.0, plateau=0.85):
    """Turn a coverage map into a bevelled height stencil.

    The chamfer is the exact distance INTO the shape, remapped through one
    smoothstep. This is the whole reason we throw the AI's own shading away:
    this edge is identical everywhere, at a width we chose, and it does not
    imply a light direction."""
    solid = cov >= 0.5
    if not solid.any():
        return np.zeros_like(cov)
    d = T.edt(~solid)                       # distance inward, 0 at the boundary
    h = T.smoothstep(0.0, max(bevel_px, 1e-6), d)
    h = np.minimum(h, 1.0) * plateau + (1.0 - plateau) * T.smoothstep(0.0, bevel_px * 3.0, d)
    return h * cov


def signed_distance(mask):
    """Signed distance to the mask boundary, in source texels: positive inside,
    negative outside, zero on the boundary halfway between the last inside texel
    centre and the first outside one. Exact Euclidean, both directions."""
    m = np.asarray(mask, dtype=bool)
    return T.edt(~m) - T.edt(m) - 0.5


def _resize_mask(mask, size):
    """Resize a binary mask to `size` and return ANTIALIASED coverage in [0,1].

    The edge is reconstructed from the mask's SIGNED DISTANCE FIELD rather than
    from its pixels, then supersampled and area-averaged down. Why: the previous
    version upsampled the binary with NEAREST, so every motif that is composited
    LARGER than its source cell -- which is most of them, a 341 px cell going to
    a 560 px rosette -- inherited the source's pixel staircase along every curve
    and then hardened it. An SDF is smooth across the boundary, so bilinear
    interpolation of it recovers a sub-texel-accurate edge, and the boundary
    lands where the shape's boundary actually is instead of on the nearest
    source texel edge.

    This is still OUR edge, not the model's: the SDF is computed from the binary
    stencil after thresholding, so none of the generator's own greys, shading or
    halo survive into it. It is the same argument the bevel already makes.
    Downsampling is unaffected -- the supersample-then-area-average is what
    gives the antialiasing, and it is untouched."""
    w, h = (size, size) if isinstance(size, int) else size
    m = np.asarray(mask, dtype=bool)
    if not m.any():
        return np.zeros((h, w), dtype=np.float64)
    sh, sw = m.shape
    sdf = signed_distance(m)
    bw, bh = w * SS, h * SS
    big = np.asarray(
        Image.fromarray(sdf.astype(np.float32), "F").resize((bw, bh), Image.Resampling.BILINEAR),
        dtype=np.float64)
    big = big > 0.0
    small = np.asarray(
        Image.fromarray((big * 255).astype(np.uint8), "L").resize((w, h), Image.Resampling.BOX),
        dtype=np.float64) / 255.0
    return small


def cell_box(sheet_size, cell, margin=0.06):
    """Pixel box of a grid cell.

    A CELL LETTER IS A ROW AND A CELL NUMBER IS A COLUMN. A1 is top-left, A3 is
    top-RIGHT, C1 is bottom-left. That is dictated by the prompts, which are the
    only thing the image generator ever saw: they enumerate the nine motifs as
    "Top row, left to right: 1, 2, 3 / Middle row, left to right: 4, 5, 6 /
    Bottom row, left to right: 7, 8, 9", and SHEETS[*]["cells"] maps A1, A2, A3,
    B1 ... to motifs 1, 2, 3, 4 ... in that order. Row-major, letter = row.

    THE BUG THIS REPLACES: the previous version read the letter as a COLUMN and
    the number as a ROW, i.e. the transpose of what was asked for and delivered.
    Six of the nine cells then resolved to the wrong motif -- most visibly
    `corner_bracket`, which was written out containing sheet A's eight-spoke
    wheel. The docstring said "A=column" and the code agreed with the docstring;
    both disagreed with the prompt one screen above them in this same file.

    The transpose is confirmed by measurement, not by reading: per-cell ink
    coverage at a 0.5 threshold on sheet_a is

            col1    col2    col3
      row A 21.9%   14.0%   19.1%
      row B 16.5%   33.8%   20.0%
      row C  6.4%   18.3%   10.8%

    and the shipped `--process` reported 3.5% for `corner_bracket` (the wheel at
    B1=16.5%, most of it then eaten by the threshold bug) and 22.4% for
    `bracket_alt` (the wheel-and-dots at A3=19.1%). Under the corrected mapping
    corner_bracket is A2 = 14.0% and bracket_alt is C1 = 6.4%, which is what the
    eye sees: an L-shaped scroll and a thin plain L.
    """
    cols, rows = SHEET_GRID
    ri = "ABC".index(cell[0].upper())
    ci = int(cell[1]) - 1
    cw = sheet_size / cols
    ch = sheet_size / rows
    mx, my = cw * margin, ch * margin
    return (int(ci * cw + mx), int(ri * ch + my),
            int((ci + 1) * cw - mx), int((ri + 1) * ch - my))


# The trimmed square binary a motif is really made of, kept beside the RGBA the
# compositor reads. The compositor asks for an exact pixel size per placement,
# so it resamples; resampling the STENCIL once from its native grid is one
# resample, while resampling an already-resampled coverage map is two and the
# second one softens what the first one just made crisp.
MASK_SUFFIX = "_mask.png"


def _cell_binary(src, cell, level=True):
    """One cell of a sheet -> (binary stencil at native cell resolution, notes).

    Chain, in order, and every step is here for a named reason:
      crop        -- cell_box; the letter is the ROW (see cell_box)
      auto-level  -- the returned black is rarely 0 and the white rarely 255,
                     GUARDED: a cell whose ink is so sparse that p98 lands in
                     the black lobe would otherwise be divided by ~0 and come
                     back solid white. Measured on the two delivered sheets the
                     level is very nearly a no-op (lo = 0.0000, hi = 1.0000 on 7
                     of 9 cells of sheet_a) -- it was NOT the cause of the
                     shattered motifs, the Otsu units bug was.
      Otsu        -- a measured threshold, now actually measuring (see _otsu)
      invert test -- a cell that came back white-on-black
      clean       -- deletes speckle by AREA and fills pinholes by AREA, so no
                     boundary texel moves at all (see clean_mask)
      trim + square
    """
    n = src.shape[0]
    x0, y0, x1, y1 = cell_box(n, cell)
    g0 = src[y0:y1, x0:x1]
    lo, hi = float(np.percentile(g0, 2.0)), float(np.percentile(g0, 98.0))
    levelled = level and (hi - lo) >= 0.20
    g = np.clip((g0 - lo) / (hi - lo), 0.0, 1.0) if levelled else g0
    thr, mu_lo, mu_hi, valley = _otsu(g, detail=True)
    m = g > thr
    inverted = m.mean() > 0.6
    if inverted:
        m = ~m
    min_area = max(4, int(round((0.012 * min(g.shape)) ** 2)))
    m = clean_mask(m, min_area=min_area)
    note = dict(cell=cell, box=(x0, y0, x1, y1), lo=lo, hi=hi, levelled=levelled,
                thr=thr, mu_lo=mu_lo, mu_hi=mu_hi, valley=valley,
                inverted=inverted, min_area=min_area, raw=g0, full=m)
    if not m.any():
        return None, note
    ys, xs = np.nonzero(m)
    m = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    return _squarify(m), note


def _squarify(m):
    """Pad a trimmed mask back to square so the motif keeps its aspect ratio
    inside a square file."""
    hgt, wid = m.shape
    side = max(hgt, wid)
    sq = np.zeros((side, side), dtype=bool)
    sq[(side - hgt) // 2:(side - hgt) // 2 + hgt,
       (side - wid) // 2:(side - wid) // 2 + wid] = m
    return sq


def process_sheet(sheet_path, out_dir, sheet_id=None, bevel_px=6.0, report=True):
    """Slice one generated sheet into the motif files the compositor consumes.

    Returns a list of records, one per cell of the sheet, so a caller can verify
    what happened instead of re-deriving it. Blocked cells (BLOCKED_CELLS) are
    processed and reported but never written.
    """
    sheet = SHEETS[0] if sheet_id is None else next(s for s in SHEETS if s["id"] == sheet_id)
    src = np.asarray(Image.open(sheet_path).convert("L"), dtype=np.float64) / 255.0
    os.makedirs(out_dir, exist_ok=True)
    recs = []
    for cell, name in sorted(sheet["cells"].items()):
        mask, note = _cell_binary(src, cell)
        rec = dict(note)
        rec.update(name=name, sheet=sheet["id"], mask=mask, path=None,
                   blocked=name in BLOCKED_CELLS)
        rec["full"] = note.get("full")
        if mask is None:
            print(f"  {cell} -> {name}: EMPTY after cleanup, skipped")
            recs.append(rec)
            continue
        if rec["blocked"]:
            if report:
                print(f"  {cell} -> {name}: BLOCKED, not written "
                      f"({BLOCKED_CELLS[name]})")
            recs.append(rec)
            continue
        px = SYMBOLS[name]["px"]
        cov = _resize_mask(mask, px)
        h = bevel_from_mask(cov, bevel_px=bevel_px)
        rec["path"] = _write_symbol(out_dir, name, h, cov, native=mask)
        rec["cov"] = float(cov.mean())
        recs.append(rec)
        if report:
            print(f"  {cell} -> {name}: coverage {cov.mean() * 100:5.1f}%  "
                  f"threshold {rec['thr']:.3f} (class means {rec['mu_lo']:.3f}/"
                  f"{rec['mu_hi']:.3f}, {rec['valley'] * 100:.2f}% of texels "
                  f"within +/-0.06 of it)  {px}px  {rec['path']}")
    return recs


def _write_symbol(out_dir, name, height, cov, native=None):
    a = np.zeros(height.shape + (4,), dtype=np.float64)
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(height, 0.0, 1.0)
    a[..., 3] = np.clip(cov, 0.0, 1.0)
    p = os.path.join(out_dir, f"{name}.png")
    os.makedirs(out_dir, exist_ok=True)
    Image.fromarray((a * 255.0 + 0.5).astype(np.uint8), "RGBA").save(p, optimize=True)
    if native is not None:
        Image.fromarray((np.asarray(native, dtype=bool) * 255).astype(np.uint8), "L").save(
            os.path.join(out_dir, name + MASK_SUFFIX), optimize=True)
    return p


# ==========================================================================
# ACCEPTANCE -- against the REAL sheets, not against a sheet we drew ourselves
# ==========================================================================
#
# The old --selftest built its input out of the same procedural motifs the
# reader then recovered, on a perfectly aligned grid, with near-binary pixels.
# Neither of the two defects that shipped could appear in it: a transposed cell
# map still recovers *a* motif from *a* cell and the coverage still looks sane,
# and a threshold pinned to 0.994 still separates ink from ground when the ink
# is exactly 1.0. It passed 9/9 while writing an eight-spoke wheel into
# corner_bracket.png. A test that agrees with itself is not evidence.
#
# So the gate is now this, and it is pointed at the delivered PNGs:
#
#   REFERENCE  -- the same cell of the same source image, thresholded at 0.5,
#                 trimmed to its bounding box and padded to square, with NO
#                 levelling and NO morphology. 0.5 is the right reference
#                 threshold because these sheets are genuinely bimodal: measured
#                 over both whole sheets, 17.8% / 17.9% of texels are above 0.5
#                 and the corrected Otsu lands at 0.479-0.494 on every one of
#                 the eighteen cells, i.e. the valley is wide and empty and the
#                 answer does not depend on where in it you cut.
#
#   COVERAGE   -- the alpha of the written motif must match the reference's ink
#                 fraction. Same denominator on both sides (the squared bbox),
#                 so this compares like with like.
#
#   SHAPE      -- intersection-over-union of the two stencils. Both live on the
#                 SAME grid (the untrimmed cell), so there is no resampling and
#                 no alignment question in this number at all.
#
#   TOPOLOGY   -- of the reference's enclosed background AREA (its holes: the
#                 ring inside a rosette, the counter of a volute, the openings
#                 of a plait), how much is still background in the pipeline's
#                 stencil. This is the "loses its interior detail" check, and it
#                 is measured in AREA rather than in a count of holes on
#                 purpose: a COUNT of components or holes is worthless on this
#                 kind of ornament. Measured on sheet_b's rosette the reference
#                 has 55 components and 4 holes while the correctly processed
#                 stencil has 3 components and 41 holes -- the two images are
#                 visually identical and the difference is entirely hairline
#                 contacts between plait strands. An instrument whose number
#                 swings by an order of magnitude on a change no one can see is
#                 not measuring the thing.
#                 Fragmentation is checked separately, as a count of components
#                 above 25 texels, because that IS what a shattered stencil
#                 looks like and it is not a hairline effect.
#
#   END TO END -- the alpha actually written to disk, against the reference
#                 trimmed and squared the same way. This one does pass through
#                 the trim and the resize, so it is the check that the file the
#                 compositor opens is the motif and not something upstream of it.
#
# Tolerances. Every one is set from a measurement, and both sides of the margin
# are stated so a later reader can see it rather than take a number on trust.
# Over the eighteen real cells of the two delivered sheets:
#
#   metric                      corrected        SHIPPED (both defects)    gate
#   worst relative cov error        0.89%               95.7%              10%
#   worst IoU                       0.9911              0.0201             0.95
#   worst hole area kept           98.28%              81.6%               90%
#   worst fragmentation           62 -> 62 comps      2 -> 36 comps       +50%+5
#
# Read the SHIPPED column honestly: coverage and IoU are what catch the defect
# that actually shipped, and they catch it by two orders of magnitude. The hole
# term does NOT catch it -- when a stencil shatters, the reference's holes are
# trivially still background, so 81.6% is very nearly a pass. The hole term is
# there for the OPPOSITE failure, an over-aggressive cleanup welding a motif
# shut, and it earned its place: it is what caught the shipped morphological
# close eating a fifth of the plait openings on sheet_b (80.65% -> 98.28% once
# the cleanup was fixed). Two failure modes, two instruments, and neither one
# can see the other's.
# THE REFERENCE MUST NOT ASK THE CODE UNDER TEST WHERE TO LOOK.
#
# The first version of this gate located a motif's source cell by calling
# cell_box() -- the same function whose transposition IS defect 2. Falsified by
# reintroducing the transpose on its own: the gate returned PASS on 8 of 8
# cells, worst IoU 0.9951, because the reference moved to exactly the same wrong
# cell as the pipeline. That is the identical failure the old --selftest had,
# rebuilt one layer up, and it is this project's standing lesson that a claim
# must not measure itself.
#
# The independent ground truth is the PROMPT, because the prompt is the only
# thing the image generator ever saw. Both prompts enumerate the nine motifs as
# "Top row, left to right: 1, 2, 3 / Middle row ... 4, 5, 6 / Bottom row ...
# 7, 8, 9". PROMPT_ORDER is that enumeration, written out, and prompt_cell_box
# does its own arithmetic. Nothing in the acceptance path calls cell_box.
PROMPT_ORDER = ["centre_rose", "corner_bracket", "rest_short",
                "rest_long", "maker_mark", "rose_alt",
                "bracket_alt", "rest_alt", "pip"]


def prompt_cell_box(sheet_size, name, margin=0.06):
    """Where the PROMPT put this motif, row-major from the top-left. Deliberately
    duplicated arithmetic rather than a call into cell_box."""
    i = PROMPT_ORDER.index(name)
    row, col = divmod(i, 3)
    side = sheet_size / 3.0
    m = side * margin
    return (int(col * side + m), int(row * side + m),
            int((col + 1) * side - m), int((row + 1) * side - m))


def _check_prompt_order():
    """The two descriptions of the sheet must at least name the same nine
    motifs. If a sheet's cells[] gains or loses one, this says so instead of
    letting PROMPT_ORDER.index() raise somewhere less obvious."""
    want = set(PROMPT_ORDER)
    for sh in SHEETS:
        got = set(sh["cells"].values())
        if got != want:
            raise ValueError(f"{sh['id']}: cells{sorted(got)} does not name the same "
                             f"nine motifs as PROMPT_ORDER {sorted(want)}")


COVERAGE_TOL_REL = 0.10       # observed worst 0.89%; shipped build 95.7%
COVERAGE_TOL_ABS = 0.004
IOU_MIN = 0.95                # observed worst 0.9911; shipped build 0.0201
HOLE_AREA_MIN = 0.90          # observed worst 98.28%; shipped morph close 80.65%
FRAGMENT_SLACK_REL = 0.50     # components >= 25 texels may grow by 50% + 5
FRAGMENT_SLACK_ABS = 5        # shipped build: 2 -> 36 on sheet_a corner_bracket
MIN_HOLE_PX = 16
MIN_COMP_PX = 25

# FALSIFICATION RECORD. A gate that has never been observed to fail is not known
# to be a gate. Each defect was reintroduced ALONE, against both real sheets:
#
#   what was broken                     sheet_a          sheet_b
#   nothing (control)                   PASS 0/8         PASS 0/8
#   cell_box transposed (defect 2)      FAIL 5/8         FAIL 5/8
#                                       IoU 0.059        IoU 0.116
#   _otsu unnormalised (defect 1)       FAIL 8/8         FAIL 8/8
#                                       IoU 0.173        IoU 0.081
#   morphological cleanup at r=1.71     PASS 0/8         FAIL 5/8, holes 80.7%
#
# Two things to read off it. The transpose fails exactly FIVE of the eight used
# cells, not eight, and that is correct rather than a weakness: A1, B2 and C3 are
# on the diagonal of a 3x3 grid and a transpose does not move them. And the old
# morphological cleanup passes on sheet_a and only fails on sheet_b -- the guild
# hand has no plait for it to weld shut. One sheet was never going to be enough
# to see it.


def _label(mask, conn=8):
    """Run-length + union-find labelling, numpy only. conn is 4 or 8.
    Returns (labels, count)."""
    mask = np.asarray(mask, dtype=bool)
    h, w = mask.shape
    parent = [0]

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[max(ra, rb)] = min(ra, rb)

    lab = np.zeros((h, w), dtype=np.int32)
    prev = []
    for y in range(h):
        row = mask[y]
        if not row.any():
            prev = []
            continue
        d = np.diff(np.concatenate(([False], row, [False])).astype(np.int8))
        starts = np.nonzero(d == 1)[0]
        ends = np.nonzero(d == -1)[0]
        runs = []
        for st, en in zip(starts, ends):
            if conn == 8:
                hits = [pl for ps, pe, pl in prev if ps <= en and st <= pe]
            else:
                hits = [pl for ps, pe, pl in prev if ps < en and st < pe]
            if hits:
                l = min(find(x) for x in hits)
                for x in hits:
                    union(l, x)
            else:
                parent.append(len(parent))
                l = len(parent) - 1
            lab[y, st:en] = l
            runs.append((st, en, l))
        prev = runs

    if len(parent) <= 1:
        return lab, 0
    roots, remap = {}, np.zeros(len(parent), dtype=np.int32)
    for i in range(1, len(parent)):
        r = find(i)
        if r not in roots:
            roots[r] = len(roots) + 1
        remap[i] = roots[r]
    return remap[lab], len(roots)


def hole_map(mask, min_px=MIN_HOLE_PX):
    """(background labels, keep flags) for the ENCLOSED background of a shape:
    4-connected background components that do not touch the image border and are
    at least `min_px` texels. 8-connected foreground against 4-connected
    background on purpose -- using the same connectivity for both double-counts
    across a diagonal pinch."""
    m = np.asarray(mask, dtype=bool)
    bg, n = _label(~m, conn=4)
    keep = np.zeros(n + 1, dtype=bool)
    if n:
        sz = np.bincount(bg.ravel(), minlength=n + 1)
        keep[1:] = sz[1:] >= min_px
        for b in np.unique(np.concatenate([bg[0], bg[-1], bg[:, 0], bg[:, -1]])):
            keep[int(b)] = False
        keep[0] = False
    return bg, keep


def n_components(mask, min_px=MIN_COMP_PX):
    """How many pieces of real size the shape is in. The shattered-stencil
    signature, and immune to hairline welds because those change the count of
    TINY components, not of big ones."""
    lab, n = _label(np.asarray(mask, dtype=bool), conn=8)
    if not n:
        return 0
    return int((np.bincount(lab.ravel(), minlength=n + 1)[1:] >= min_px).sum())


def compare_stencils(ref, got):
    """Reference vs pipeline on the SAME grid. Returns
    (iou, hole_area_kept, n_holes, ref_comps, got_comps)."""
    ref = np.asarray(ref, dtype=bool)
    got = np.asarray(got, dtype=bool)
    if ref.shape != got.shape:
        # The two grids should be the same cell of the same image. If they are
        # not, something has moved the pipeline's cell relative to the prompt's
        # -- which is defect 2's signature -- so compare what overlaps rather
        # than raising, and let the IoU say how bad it is.
        h = min(ref.shape[0], got.shape[0])
        w = min(ref.shape[1], got.shape[1])
        ref, got = ref[:h, :w], got[:h, :w]
    inter = float((ref & got).sum())
    union = float((ref | got).sum())
    iou = inter / union if union else 1.0
    bg, keep = hole_map(ref)
    total = kept = 0.0
    nh = 0
    for i in np.flatnonzero(keep):
        a = bg == i
        nh += 1
        total += float(a.sum())
        kept += float((a & ~got).sum())
    return iou, (kept / total if total else 1.0), nh, n_components(ref), n_components(got)


def reference_cell(src, name, thr=0.5):
    """The reference stencil for one motif: find its cell from the PROMPT, then
    threshold at `thr`, trim, squarify. No levelling, no morphology, no
    resampling, and no call into cell_box -- deliberately the simplest thing
    that could be right, so it cannot share a bug with the pipeline."""
    n = src.shape[0]
    x0, y0, x1, y1 = prompt_cell_box(n, name)
    m = src[y0:y1, x0:x1] > thr
    if not m.any():
        return None
    ys, xs = np.nonzero(m)
    return _squarify(m[ys.min():ys.max() + 1, xs.min():xs.max() + 1])


def verify_sheet(sheet_path, out_dir, sheet_id, ref_thr=0.5, report=True):
    """Run --process on a real sheet and hold the result against the source.
    Returns (ok, rows)."""
    _check_prompt_order()
    sheet = next(x for x in SHEETS if x["id"] == sheet_id)
    src = np.asarray(Image.open(sheet_path).convert("L"), dtype=np.float64) / 255.0
    print(f"\n--- {sheet_id}: {sheet_path} ---")
    whole = _otsu(src, detail=True)
    print(f"  whole sheet: ink above {ref_thr:.2f} = {(src > ref_thr).mean() * 100:.2f}%, "
          f"corrected Otsu = {whole[0]:.4f} (class means {whole[1]:.3f}/{whole[2]:.3f})")
    recs = process_sheet(sheet_path, out_dir, sheet_id, report=report)

    n = src.shape[0]
    rows, ok = [], True
    for rec in recs:
        name, cell = rec["name"], rec["cell"]
        x0, y0, x1, y1 = prompt_cell_box(n, name)
        ref_full = src[y0:y1, x0:x1] > ref_thr
        got_full = rec.get("full")
        if got_full is None or not ref_full.any():
            rows.append(dict(cell=cell, name=name, verdict="EMPTY"))
            ok = False
            continue
        iou, hole_keep, nh, rcomp, gcomp = compare_stencils(ref_full, got_full)
        ref_cov = float(ref_full.mean())
        got_cov = float(got_full.mean())
        dcov = abs(got_cov - ref_cov)
        tol = max(COVERAGE_TOL_ABS, COVERAGE_TOL_REL * ref_cov)

        # end to end: the alpha actually on disk, against the reference trimmed
        # and squared the same way the pipeline trims and squares
        ref_sq = reference_cell(src, name, ref_thr)
        e2e_ref = float(ref_sq.mean()) if ref_sq is not None else 0.0
        e2e_got = float("nan")
        if rec["path"] and os.path.exists(rec["path"]):
            e2e_got = float(T.load_rgba(rec["path"])[..., 3].mean())
        e2e_ok = (not rec["path"]) or (
            abs(e2e_got - e2e_ref) <= max(COVERAGE_TOL_ABS, COVERAGE_TOL_REL * e2e_ref))

        cov_ok = dcov <= tol
        iou_ok = iou >= IOU_MIN
        hole_ok = hole_keep >= HOLE_AREA_MIN
        frag_ok = gcomp <= rcomp * (1.0 + FRAGMENT_SLACK_REL) + FRAGMENT_SLACK_ABS
        good = cov_ok and iou_ok and hole_ok and frag_ok and e2e_ok
        if not rec["blocked"]:
            ok &= good
        why = "".join(c for c, f in (("c", cov_ok), ("i", iou_ok), ("h", hole_ok),
                                     ("f", frag_ok), ("e", e2e_ok)) if not f)
        pr = PROMPT_ORDER.index(name)
        rows.append(dict(cell=cell, name=name, blocked=rec["blocked"],
                         prompt_cell="ABC"[pr // 3] + str(pr % 3 + 1),
                         ref_cov=ref_cov, got_cov=got_cov, rel=dcov / max(ref_cov, 1e-9),
                         tol=tol, iou=iou, hole=hole_keep, nh=nh,
                         rcomp=rcomp, gcomp=gcomp, thr=rec["thr"],
                         e2e_ref=e2e_ref, e2e_got=e2e_got,
                         verdict="pass" if good else "FAIL " + why))

    if report:
        print(f"  {'cell':5s} {'prompt':6s} {'motif':15s} {'ref cov':>8s} {'got cov':>8s} "
              f"{'relerr':>7s} {'IoU':>7s} {'holes kept':>12s} {'comps':>10s} "
              f"{'end2end':>16s}  verdict")
        for r in rows:
            if r["verdict"] == "EMPTY":
                print(f"  {r['cell']:5s} {r['name']:15s}  EMPTY")
                continue
            e2 = ("--" if r["e2e_got"] != r["e2e_got"]
                  else f"{r['e2e_ref'] * 100:5.2f}->{r['e2e_got'] * 100:5.2f}%")
            print(f"  {r['cell']:5s} {r['prompt_cell']:6s} {r['name']:15s} "
                  f"{r['ref_cov'] * 100:7.2f}% "
                  f"{r['got_cov'] * 100:7.2f}% {r['rel'] * 100:6.2f}% "
                  f"{r['iou']:7.4f} {r['hole'] * 100:9.2f}% /{r['nh']:2d} "
                  f"{r['rcomp']:4d}->{r['gcomp']:<4d} {e2:>16s}  "
                  f"{r['verdict']}{'  (blocked, not written)' if r['blocked'] else ''}")
        live = [r for r in rows if r["verdict"] != "EMPTY" and not r["blocked"]]
        if live:
            print(f"  worst over the {len(live)} used cells: relative coverage error "
                  f"{max(r['rel'] for r in live) * 100:.2f}% (gate {COVERAGE_TOL_REL * 100:.0f}%), "
                  f"IoU {min(r['iou'] for r in live):.4f} (gate {IOU_MIN:.2f}), "
                  f"hole area kept {min(r['hole'] for r in live) * 100:.2f}% "
                  f"(gate {HOLE_AREA_MIN * 100:.0f}%)")
    print(f"  SHEET ACCEPTANCE {sheet_id}: {'PASS' if ok else 'FAIL'}")
    return ok, rows


# ==========================================================================
# procedural motifs -- the stand-in path, and the final path for anything
# that must tile or must be geometrically exact
# ==========================================================================

def _canvas(w, h):
    img = Image.new("L", (w * SS, h * SS), 0)
    return img, ImageDraw.Draw(img)


def _reduce(img, w, h):
    return np.asarray(img.resize((w, h), Image.Resampling.BOX), dtype=np.float64) / 255.0


def _star(dr, cx, cy, r_out, r_in, points, rot=0.0, fill=255):
    pts = []
    for i in range(points * 2):
        a = rot + i * math.pi / points
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    dr.polygon(pts, fill=fill)


def _ring(dr, cx, cy, r, w, fill=255):
    dr.ellipse([cx - r, cy - r, cx + r, cy + r], outline=fill, width=int(max(1, w)))


def proc_centre_rose(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    c = s / 2.0
    _ring(dr, c, c, s * 0.470, s * 0.020)
    _ring(dr, c, c, s * 0.415, s * 0.012)
    _star(dr, c, c, s * 0.385, s * 0.150, 12, rot=-math.pi / 2)
    _ring(dr, c, c, s * 0.170, s * 0.018)
    for i in range(6):
        a = i * math.pi / 3
        px_ = c + s * 0.105 * math.cos(a)
        py_ = c + s * 0.105 * math.sin(a)
        dr.ellipse([px_ - s * 0.052, py_ - s * 0.052, px_ + s * 0.052, py_ + s * 0.052], fill=255)
    dr.ellipse([c - s * 0.048, c - s * 0.048, c + s * 0.048, c + s * 0.048], fill=0)
    return _reduce(img, px, px)


def proc_rose_alt(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    c = s / 2.0
    _ring(dr, c, c, s * 0.470, s * 0.016)
    _star(dr, c, c, s * 0.430, s * 0.230, 16, rot=-math.pi / 2)
    _ring(dr, c, c, s * 0.200, s * 0.014)
    _star(dr, c, c, s * 0.165, s * 0.070, 8, rot=0.0)
    return _reduce(img, px, px)


def proc_corner_bracket(px, seed=0):
    """An L bracket occupying the TOP-LEFT of its square; the compositor mirrors
    it into the other three corners."""
    img, dr = _canvas(px, px)
    s = px * SS
    t = s * 0.110
    # outer L
    dr.polygon([(0, 0), (s * 0.94, 0), (s * 0.94, t), (t, t), (t, s * 0.94), (0, s * 0.94)],
               fill=255)
    # inner L, offset, thinner -- a double-line bracket
    a, b = s * 0.185, s * 0.070
    dr.polygon([(a, a), (s * 0.74, a), (s * 0.74, a + b), (a + b, a + b),
                (a + b, s * 0.74), (a, s * 0.74)], fill=255)
    # quarter-round fillet in the corner between the two runs
    dr.pieslice([-s * 0.34, -s * 0.34, s * 0.34, s * 0.34], 0, 90, fill=255)
    dr.pieslice([-s * 0.20, -s * 0.20, s * 0.20, s * 0.20], 0, 90, fill=0)
    # terminal studs
    for cx, cy in ((s * 0.845, t * 0.5), (t * 0.5, s * 0.845)):
        dr.ellipse([cx - s * 0.052, cy - s * 0.052, cx + s * 0.052, cy + s * 0.052], fill=255)
    return _reduce(img, px, px)


def proc_bracket_alt(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    t = s * 0.105
    dr.polygon([(0, 0), (s * 0.90, 0), (s * 0.78, t), (t, t), (t, s * 0.78), (0, s * 0.90)], fill=255)
    dr.rectangle([s * 0.135, s * 0.135, s * 0.255, s * 0.255], fill=255)
    return _reduce(img, px, px)


def proc_rest_short(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    c = s / 2.0
    dr.ellipse([c - s * 0.44, c - s * 0.44, c + s * 0.44, c + s * 0.44], fill=255)
    dr.ellipse([c - s * 0.20, c - s * 0.40, c + s * 0.56, c + s * 0.36], fill=0)
    for i, (dx, dy, r) in enumerate(((-0.16, 0.02, 0.075), (-0.05, -0.16, 0.058),
                                     (-0.04, 0.19, 0.052))):
        dr.ellipse([c + s * dx - s * r, c + s * dy - s * r,
                    c + s * dx + s * r, c + s * dy + s * r], fill=255)
    return _reduce(img, px, px)


def proc_rest_long(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    c = s / 2.0
    _ring(dr, c, c, s * 0.440, s * 0.055)
    for i in range(8):
        a = i * math.pi / 4
        dr.polygon([(c + s * 0.36 * math.cos(a), c + s * 0.36 * math.sin(a)),
                    (c + s * 0.10 * math.cos(a + 0.30), c + s * 0.10 * math.sin(a + 0.30)),
                    (c + s * 0.10 * math.cos(a - 0.30), c + s * 0.10 * math.sin(a - 0.30))],
                   fill=255)
        b = a + math.pi / 8
        dr.ellipse([c + s * 0.305 * math.cos(b) - s * 0.030, c + s * 0.305 * math.sin(b) - s * 0.030,
                    c + s * 0.305 * math.cos(b) + s * 0.030, c + s * 0.305 * math.sin(b) + s * 0.030],
                   fill=255)
    dr.ellipse([c - s * 0.085, c - s * 0.085, c + s * 0.085, c + s * 0.085], fill=255)
    return _reduce(img, px, px)


def proc_maker_mark(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    dr.polygon([(s * 0.14, s * 0.10), (s * 0.86, s * 0.10), (s * 0.86, s * 0.58),
                (s * 0.50, s * 0.92), (s * 0.14, s * 0.58)], fill=255)
    dr.polygon([(s * 0.21, s * 0.17), (s * 0.79, s * 0.17), (s * 0.79, s * 0.55),
                (s * 0.50, s * 0.82), (s * 0.21, s * 0.55)], fill=0)
    dr.rectangle([s * 0.455, s * 0.26, s * 0.545, s * 0.70], fill=255)
    dr.rectangle([s * 0.33, s * 0.30, s * 0.67, s * 0.39], fill=255)
    dr.ellipse([s * 0.42, s * 0.62, s * 0.58, s * 0.78], fill=255)
    dr.ellipse([s * 0.465, s * 0.665, s * 0.535, s * 0.735], fill=0)
    return _reduce(img, px, px)


def proc_pip(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    c = s / 2.0
    dr.polygon([(c - s * 0.44, c), (c, c - s * 0.24), (c + s * 0.44, c), (c, c + s * 0.24)], fill=255)
    for dx, dy in ((-0.47, 0.0), (0.47, 0.0), (0.0, -0.27), (0.0, 0.27)):
        dr.ellipse([c + s * dx - s * 0.045, c + s * dy - s * 0.045,
                    c + s * dx + s * 0.045, c + s * dy + s * 0.045], fill=255)
    return _reduce(img, px, px)


def proc_seat_bezel(px, seed=0):
    """FINAL, never AI: exact concentric rings and exactly-spaced rivets."""
    img, dr = _canvas(px, px)
    s = px * SS
    c = s / 2.0
    _ring(dr, c, c, s * 0.470, s * 0.035)
    _ring(dr, c, c, s * 0.390, s * 0.020)
    for i in range(8):
        a = i * math.pi / 4 + math.pi / 8
        cx = c + s * 0.430 * math.cos(a)
        cy = c + s * 0.430 * math.sin(a)
        dr.ellipse([cx - s * 0.032, cy - s * 0.032, cx + s * 0.032, cy + s * 0.032], fill=255)
    return _reduce(img, px, px)


def proc_slot_corner(px, seed=0):
    """FINAL, never AI: a right-angle tick for the card recess corners."""
    img, dr = _canvas(px, px)
    s = px * SS
    t = s * 0.20
    dr.polygon([(0, 0), (s * 0.86, 0), (s * 0.86, t), (t, t), (t, s * 0.86), (0, s * 0.86)], fill=255)
    return _reduce(img, px, px)


def proc_border_run(px, seed=0):
    """FINAL, never AI: a border strip that MUST tile horizontally. Every element
    is placed on an exact integer division of the width, so the wrap is closed by
    construction rather than by retouching."""
    w, h = px
    img, dr = _canvas(w, h)
    W, H = w * SS, h * SS
    reps = 16
    step = W / reps
    dr.rectangle([0, H * 0.10, W, H * 0.20], fill=255)
    dr.rectangle([0, H * 0.80, W, H * 0.90], fill=255)
    for i in range(reps):
        x = i * step
        dr.polygon([(x + step * 0.10, H * 0.50), (x + step * 0.50, H * 0.24),
                    (x + step * 0.90, H * 0.50), (x + step * 0.50, H * 0.76)], fill=255)
        dr.ellipse([x + step * 0.47, H * 0.465, x + step * 0.53, H * 0.535], fill=0)
    return _reduce(img, w, h)


def proc_edge_dentil(px, seed=0):
    """FINAL, never AI: dentil strip for the board edges. Tiles by construction."""
    w, h = px
    img, dr = _canvas(w, h)
    W, H = w * SS, h * SS
    reps = 32
    step = W / reps
    dr.rectangle([0, H * 0.05, W, H * 0.22], fill=255)
    dr.rectangle([0, H * 0.78, W, H * 0.95], fill=255)
    for i in range(reps):
        x = i * step
        dr.rectangle([x + step * 0.22, H * 0.34, x + step * 0.78, H * 0.66], fill=255)
    return _reduce(img, w, h)


PROC = {
    "centre_rose": proc_centre_rose, "rose_alt": proc_rose_alt,
    "corner_bracket": proc_corner_bracket, "bracket_alt": proc_bracket_alt,
    "rest_short": proc_rest_short, "rest_long": proc_rest_long,
    "maker_mark": proc_maker_mark, "pip": proc_pip,
    "seat_bezel": proc_seat_bezel, "slot_corner": proc_slot_corner,
    "border_run": proc_border_run, "edge_dentil": proc_edge_dentil,
}


# ==========================================================================
# the accessor the compositor uses -- it cannot tell placeholder from generated
# ==========================================================================

_SYM_CACHE = {}


def get_symbol(name, symbols_dir=None, px=None, bevel_px=None, seed=0):
    """Return (height, coverage), both (h,w) float in [0,1].

    Resolution order:
      1. <symbols_dir>/<name>.png -- a processed motif (from an AI sheet, or a
         baked placeholder). Used as-is.
      2. the procedural stand-in.
    A motif whose spec says source='proc' NEVER looks in symbols_dir, because a
    generated version of it would be worse; that is the point of the flag."""
    spec = SYMBOLS.get(name)
    size = px if px is not None else (spec["px"] if spec else 256)
    key = (name, symbols_dir, str(size), bevel_px, seed)
    if key in _SYM_CACHE:
        return _SYM_CACHE[key]

    got = None
    if symbols_dir and spec and spec["source"] == "ai":
        # Prefer the NATIVE stencil: rebuild coverage and bevel at the exact
        # size asked for, from the binary, through one SDF resize. Resampling
        # the already-resized RGBA instead would resample twice and would also
        # rescale the bevel, so a motif composited at half its stored size would
        # come out with a chamfer twice as wide relative to its strokes.
        mp = os.path.join(symbols_dir, name + MASK_SUFFIX)
        p = os.path.join(symbols_dir, f"{name}.png")
        if os.path.exists(mp):
            native = T.load_gray(mp) > 0.5
            cov = _resize_mask(native, size)
            b = bevel_px if bevel_px is not None else max(2.0, _minside(size) * 0.030)
            got = (bevel_from_mask(cov, bevel_px=b), cov)
        elif os.path.exists(p):
            rgba = T.load_rgba(p)
            got = (_fit(rgba[..., 0], size), _fit(rgba[..., 3], size))

    if got is None:
        cov = PROC[name](size if not isinstance(size, tuple) else size, seed=seed)
        b = bevel_px if bevel_px is not None else max(2.0, _minside(size) * 0.030)
        got = (bevel_from_mask(cov, bevel_px=b), cov)

    _SYM_CACHE[key] = got
    return got


def _minside(size):
    return size if isinstance(size, int) else min(size)


def _fit(a, size):
    w, h = (size, size) if isinstance(size, int) else size
    if a.shape == (h, w):
        return a
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), "L")
    return np.asarray(img.resize((w, h), Image.Resampling.LANCZOS), dtype=np.float64) / 255.0


def bake_placeholders(out_dir, seed=0):
    """Write every motif -- AI ones as their procedural stand-in -- so the whole
    pipeline runs end to end before a single paid image exists."""
    os.makedirs(out_dir, exist_ok=True)
    written = []
    for spec in SYMBOL_SPEC:
        size = spec["px"]
        cov = PROC[spec["name"]](size, seed=seed)
        b = max(2.0, _minside(size) * 0.030)
        h = bevel_from_mask(cov, bevel_px=b)
        written.append(_write_symbol(out_dir, spec["name"], h, cov))
    return written


# ==========================================================================
# CLI
# ==========================================================================

def print_spec():
    print("# SYMBOL SPECIFICATION -- GloomhavenVR control boards\n")
    print(f"Atlas 2048^2. Sizes below are the composited size at that atlas.\n")
    print("| motif | px | source | shared | where |")
    print("|---|---|---|---|---|")
    for s in SYMBOL_SPEC:
        px = s["px"] if not isinstance(s["px"], tuple) else f"{s['px'][0]}x{s['px'][1]}"
        print(f"| `{s['name']}` | {px} | **{s['source']}** | "
              f"{'yes' if s['shared'] else 'no'} | {s['uses']} |")
    print("\n## Why each motif is on its side of the ai/proc line\n")
    for s in SYMBOL_SPEC:
        print(f"- **{s['name']}** ({s['source']}): {s['why']}")
    print("\n## Post-processing applied to every generated motif\n")
    print("auto-level (p2/p98, guarded) -> Otsu threshold (measured, not guessed) -> exact\n"
          "Euclidean open+close (despeckle, close pinholes; no blur, so the edge\n"
          "does not creep) -> trim to content bbox -> supersampled binary resize to\n"
          "target px, area-averaged for antialiasing -> OUR bevel from the exact\n"
          "distance transform. The model's own greys are discarded entirely.\n"
          "Output is RGBA: RGB = bevelled height, A = coverage. The motif is then\n"
          "CARVED into the material; it never contributes albedo, so it cannot\n"
          "introduce a colour outside the style palette and cannot carry baked\n"
          "lighting into the board.\n")
    print("## The generation request\n")
    print(CALL_RATIONALE)
    for sh in SHEETS:
        print(f"\n### {sh['id']} -- {sh['size']}x{sh['size']}, one call\n")
        print("Cell map (A=ROW, 1=COLUMN; A1 top-left, A3 top-RIGHT, C1 bottom-left):")
        for cell in sorted(sh["cells"]):
            print(f"  {cell} -> {sh['cells'][cell]}")
        print("\nPROMPT (send verbatim):\n")
        print("-" * 74)
        print(sh["prompt"])
        print("-" * 74)


def _contact_sheet(out_dir, seed=0):
    os.makedirs(out_dir, exist_ok=True)
    tiles = []
    for spec in SYMBOL_SPEC:
        h, cov = get_symbol(spec["name"], None, seed=seed)
        tiles.append((spec["name"], h, cov))
    cell = 260
    cols = 5
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.full((rows * cell, cols * cell, 3), 0.13, dtype=np.float64)
    for i, (nm, h, cov) in enumerate(tiles):
        r, c = divmod(i, cols)
        hh, ww = h.shape
        sc = min((cell - 24) / ww, (cell - 24) / hh)
        w2, h2 = max(1, int(ww * sc)), max(1, int(hh * sc))
        hs = np.asarray(Image.fromarray((np.clip(h, 0, 1) * 255).astype(np.uint8), "L")
                        .resize((w2, h2), Image.Resampling.BOX), dtype=np.float64) / 255.0
        cs = np.asarray(Image.fromarray((np.clip(cov, 0, 1) * 255).astype(np.uint8), "L")
                        .resize((w2, h2), Image.Resampling.BOX), dtype=np.float64) / 255.0
        y0 = r * cell + (cell - h2) // 2
        x0 = c * cell + (cell - w2) // 2
        dst = sheet[y0:y0 + h2, x0:x0 + w2]
        v = (0.20 + 0.75 * hs)[..., None]
        sheet[y0:y0 + h2, x0:x0 + w2] = dst * (1 - cs[..., None]) + v * cs[..., None]
        print(f"  {nm:16s} {h.shape[1]}x{h.shape[0]}  coverage {cov.mean() * 100:5.1f}%  "
              f"height max {h.max():.2f}")
    p = T.save_rgb(os.path.join(out_dir, "symbol_contact_sheet.png"), sheet)
    print("WROTE", p, os.path.getsize(p))


def selftest_sheet(work_dir, sheet_id="sheet_a"):
    """Damage-recovery test on a SYNTHETIC sheet. Not the acceptance gate.

    Builds a synthetic 1024^2 nine-cell sheet from the procedural motifs and
    deliberately damages it with the failure modes a real generation actually
    shows: a cell returned with grey shading instead of flat white, a cell
    returned inverted, a cell peppered with speckle, and a whole-sheet lift of
    the black point. Then runs process_sheet over it and checks every motif came
    back with sane coverage.

    WHAT THIS TEST CANNOT SEE, stated plainly because it passed 9/9 while the
    pipeline was writing an eight-spoke wheel into corner_bracket.png:
      * it draws its own input with the same code that reads it back, so a
        TRANSPOSED cell map still finds a motif in every cell and still gets a
        plausible coverage out of it. Nothing here knows which motif was
        supposed to be in which cell.
      * its cells are near-binary (0.07 and 0.95 after the deliberate lift), so
        a threshold pinned to the top of the histogram still separates ink from
        ground perfectly. Only a real sheet, whose white sits at 250-255 with an
        anti-aliased rim below that, exposes it.
    It is kept because the damage-recovery path is real and worth exercising.
    The GATE is verify_sheet(), which measures the delivered PNGs against
    themselves at a sane threshold. Run --verify before trusting any motif."""
    os.makedirs(work_dir, exist_ok=True)
    sheet = next(s for s in SHEETS if s["id"] == sheet_id)
    n = sheet["size"]
    canvas = np.zeros((n, n), dtype=np.float64)
    rg = np.random.default_rng(99)

    damaged = {}
    for cell, name in sheet["cells"].items():
        if name in BLOCKED_CELLS:
            continue
        x0, y0, x1, y1 = cell_box(n, cell, margin=0.02)
        side = min(x1 - x0, y1 - y0)
        cov = PROC[name](side if not isinstance(SYMBOLS[name]["px"], tuple) else (side, side))
        if isinstance(cov, tuple):
            cov = cov[0]
        ch, cw = cov.shape
        oy, ox = y0 + (y1 - y0 - ch) // 2, x0 + (x1 - x0 - cw) // 2
        tile = cov.copy()
        if cell == "A2":
            # returned with soft relief shading instead of flat white
            tile = tile * (0.45 + 0.55 * T.norm01(T.box_blur(tile, 9)))
            damaged[cell] = "grey relief shading"
        elif cell == "B2":
            # returned inverted
            tile = 1.0 - tile
            damaged[cell] = "inverted"
        elif cell == "C1":
            tile = np.clip(tile + (rg.random(tile.shape) < 0.02) * 0.9
                           - (rg.random(tile.shape) < 0.02) * 0.9, 0, 1)
            damaged[cell] = "speckled"
        canvas[oy:oy + ch, ox:ox + cw] = np.maximum(canvas[oy:oy + ch, ox:ox + cw], tile)

    canvas = canvas * 0.88 + 0.07          # lifted black point, dulled white
    sheet_png = os.path.join(work_dir, f"synthetic_{sheet_id}.png")
    T.save_gray(sheet_png, canvas)
    print(f"  synthetic sheet {sheet_png} ({n}x{n}), deliberate damage: {damaged}")
    print(f"  black point lifted to {canvas.min():.3f}, white pulled to {canvas.max():.3f}")

    out = os.path.join(work_dir, "processed")
    recs = process_sheet(sheet_png, out, sheet_id)
    written = [r["path"] for r in recs if r["path"]]

    ok = True
    for cell, name in sheet["cells"].items():
        if name in BLOCKED_CELLS:
            continue
        p = os.path.join(out, f"{name}.png")
        if not os.path.exists(p):
            print(f"  MISSING {name} -- process_sheet dropped it")
            ok = False
            continue
        rgba = T.load_rgba(p)
        cov = rgba[..., 3].mean()
        want = SYMBOLS[name]["px"]
        want = (want, want) if not isinstance(want, tuple) else want
        shape_ok = rgba.shape[:2] == (want[1], want[0]) or rgba.shape[0] == rgba.shape[1]
        good = 0.03 < cov < 0.75 and shape_ok
        ok &= good
        if not good:
            print(f"  {name}: coverage {cov * 100:.1f}%, shape {rgba.shape[:2]} -- SUSPECT")
    live = len(sheet["cells"]) - sum(1 for v in sheet["cells"].values() if v in BLOCKED_CELLS)
    print(f"  SHEET SELFTEST {'PASS' if ok else 'FAIL'} "
          f"({len(written)}/{live} motifs recovered, damage included)")
    print("  NOTE this test cannot detect a transposed cell map or a threshold "
          "pinned to the\n       top of the histogram. --verify is the gate.")
    return ok


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="board decorative motifs")
    ap.add_argument("--selftest", metavar="DIR", nargs="?", const="unity/board-prep/out/sheet_test",
                    help="prove the AI-sheet post-processing works on a synthetic sheet")
    ap.add_argument("--spec", action="store_true", help="print the symbol specification")
    ap.add_argument("--contact", metavar="DIR", help="render a contact sheet of every motif")
    ap.add_argument("--bake", metavar="DIR", help="bake placeholder motif files")
    ap.add_argument("--process", metavar="SHEET_PNG", help="post-process a generated sheet")
    ap.add_argument("--verify", metavar="SHEET_PNG", nargs="*",
                    help="process a REAL sheet and hold every cell against the "
                         "source; with no argument, verifies both delivered sheets")
    ap.add_argument("--sheet-id", default="sheet_a")
    ap.add_argument("--out", default="unity/board-prep/out/symbols")
    ap.add_argument("--sheet-dir", default="unity/board-prep/out",
                    help="where sheet_a.png / sheet_b.png live")
    a = ap.parse_args()
    if a.spec:
        print_spec()
    if a.contact:
        _contact_sheet(a.contact)
    if a.bake:
        for p in bake_placeholders(a.bake):
            print("WROTE", p, os.path.getsize(p))
    if a.process:
        process_sheet(a.process, a.out, a.sheet_id)
    if a.verify is not None:
        jobs = []
        if a.verify:
            jobs = [(a.sheet_id, p) for p in a.verify]
        else:
            jobs = [(sh["id"], os.path.join(a.sheet_dir, sh["id"] + ".png")) for sh in SHEETS]
        allok = True
        for sid, path in jobs:
            if not os.path.exists(path):
                print(f"  {path} does not exist -- nothing to verify")
                allok = False
                continue
            ok, _ = verify_sheet(path, os.path.join(a.out, sid.replace("sheet_", "motifs_")),
                                 sid)
            allok &= ok
        print(f"\nACCEPTANCE OVER ALL SHEETS: {'PASS' if allok else 'FAIL'}")
        raise SystemExit(0 if allok else 1)
    if a.selftest:
        raise SystemExit(0 if selftest_sheet(a.selftest, a.sheet_id) else 1)
    if not any((a.spec, a.contact, a.bake, a.process, a.selftest)):
        ap.print_help()
