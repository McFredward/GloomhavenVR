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
SYMBOL_SPEC = [
    dict(name="centre_rose", px=560, source="ai", shared=True, cell="A1",
         uses="face, 1x, at the symbols[] centre_rose entry",
         why="the one large hero ornament; hand-vectoring a 12-fold rosette with "
             "interior filigree is hours of work and is exactly what diffusion is "
             "good at once you only keep its silhouette"),
    dict(name="corner_bracket", px=340, source="ai", shared=True, cell="A2",
         uses="frame, 4x, mirrored into each corner",
         why="an L-shaped scrollwork corner; asymmetric organic curve, tedious by "
             "hand, and it is used mirrored so only one is needed"),
    dict(name="rest_short", px=210, source="ai", shared=True, cell="A3",
         uses="rest_pads, 1x, the ShortRestToken pad",
         why="must read instantly as 'short rest' at 25 mm; a crescent-and-ember "
             "device is iconographic work, not geometry"),
    dict(name="rest_long", px=210, source="ai", shared=True, cell="B1",
         uses="rest_pads, 1x, the LongRestToken pad",
         why="its pair; must be distinguishable from rest_short at a glance"),
    dict(name="maker_mark", px=240, source="ai", shared=True, cell="B2",
         uses="frame, 1x, centred on the lower frame run",
         why="a small heraldic cartouche that gives the board an author; pure "
             "ornament, no functional meaning, so a generated silhouette is safe"),
    dict(name="rose_alt", px=560, source="ai", shared=True, cell="B3",
         uses="face, alternate for centre_rose (denser variant)",
         why="a second rosette on the SAME sheet costs nothing and gives a choice "
             "without a second call"),
    dict(name="bracket_alt", px=340, source="ai", shared=True, cell="C1",
         uses="frame, alternate for corner_bracket (plainer variant)",
         why="same argument; a plainer bracket suits Steel where the ornate one "
             "suits Oak, and both come out of one call"),
    dict(name="rest_alt", px=210, source="ai", shared=True, cell="C2",
         uses="rest_pads, alternate",
         why="spare cell, free"),
    dict(name="pip", px=96, source="ai", shared=True, cell="C3",
         uses="face, small separators between zones",
         why="spare cell, free; a tiny lozenge used as punctuation"),

    dict(name="seat_bezel", px=300, source="proc", shared=True, cell=None,
         uses="button_seats, 3x, one ring per seat",
         why="a concentric annulus with evenly spaced rivets. Perfect circles and "
             "exact angular spacing; generating this would be strictly worse and "
             "would cost money"),
    dict(name="slot_corner", px=140, source="proc", shared=True, cell=None,
         uses="face, 8x, the four corners of each card recess",
         why="a right-angle tick. Pure geometry"),
    dict(name="border_run", px=(1024, 128), source="proc", shared=True, cell=None,
         uses="frame, tiled along all four runs",
         why="MUST TILE. Diffusion cannot close a seam; this is the single "
             "hardest constraint in the whole texture job and it is trivial "
             "procedurally"),
    dict(name="edge_dentil", px=(1024, 96), source="proc", shared=True, cell=None,
         uses="sides, tiled along the board edge",
         why="MUST TILE, same argument"),
]

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

def _otsu(gray):
    """Otsu threshold, numpy only. Used because the sheet's black may come back
    at 0.04 rather than 0.00 and a fixed 0.5 would then be a guess."""
    hist, edges = np.histogram(gray, bins=256, range=(0.0, 1.0))
    total = hist.sum()
    if total == 0:
        return 0.5
    w = np.cumsum(hist).astype(np.float64)
    centres = (edges[:-1] + edges[1:]) * 0.5
    m = np.cumsum(hist * centres).astype(np.float64)
    mt = m[-1]
    with np.errstate(invalid="ignore", divide="ignore"):
        between = (mt * w - m) ** 2 / (w * (total - w))
    between = np.nan_to_num(between, nan=-1.0, posinf=-1.0, neginf=-1.0)
    return float(centres[int(np.argmax(between))])


def erode(mask, r):
    """Exact Euclidean erosion (radius in pixels)."""
    return T.edt(~np.asarray(mask, dtype=bool)) > r


def dilate(mask, r):
    """Exact Euclidean dilation (radius in pixels)."""
    return T.edt(np.asarray(mask, dtype=bool)) <= r


def clean_mask(mask, open_r=2.0, close_r=2.0):
    """Open then close: removes diffusion speckle and closes pinholes without
    the edge-softening a blur would cost."""
    m = np.asarray(mask, dtype=bool)
    if open_r > 0:
        m = dilate(erode(m, open_r), open_r)
    if close_r > 0:
        m = erode(dilate(m, close_r), close_r)
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


def _resize_mask(mask, size):
    """Resize a binary mask to `size` and return ANTIALIASED coverage in [0,1].
    Done by area-averaging a supersampled binary, never by blurring, so the edge
    stays where it was instead of spreading."""
    w, h = (size, size) if isinstance(size, int) else size
    img = Image.fromarray((np.asarray(mask, dtype=bool) * 255).astype(np.uint8), "L")
    big = img.resize((w * SS, h * SS), Image.Resampling.NEAREST)
    big = np.asarray(big, dtype=np.float64) / 255.0
    big = (big >= 0.5)
    small = np.asarray(
        Image.fromarray((big * 255).astype(np.uint8), "L").resize((w, h), Image.Resampling.BOX),
        dtype=np.float64) / 255.0
    return small


def cell_box(sheet_size, cell, margin=0.06):
    """Pixel box of a grid cell. Cells are lettered by COLUMN (A,B,C) and
    numbered by ROW (1,2,3): A1 is top-left, C3 is bottom-right."""
    cols, rows = SHEET_GRID
    ci = "ABC".index(cell[0].upper())
    ri = int(cell[1]) - 1
    cw = sheet_size / cols
    ch = sheet_size / rows
    mx, my = cw * margin, ch * margin
    return (int(ci * cw + mx), int(ri * ch + my),
            int((ci + 1) * cw - mx), int((ri + 1) * ch - my))


def process_sheet(sheet_path, out_dir, sheet_id=None, bevel_px=6.0, report=True):
    """Slice one generated sheet into the motif files the compositor consumes.

    Chain, in order, and every step is here for a named reason:
      auto-level  -- the returned black is rarely 0 and the white rarely 255
      Otsu        -- a measured threshold, not a guessed one
      open/close  -- kills diffusion speckle and pinholes; exact Euclidean
                     morphology, so the edge does not creep
      trim        -- crop to the motif so `px` means the motif, not its margin
      resize      -- supersampled binary -> area average; crisp, not blurred
      bevel       -- our chamfer, not the model's shading
    """
    sheet = SHEETS[0] if sheet_id is None else next(s for s in SHEETS if s["id"] == sheet_id)
    src = np.asarray(Image.open(sheet_path).convert("L"), dtype=np.float64) / 255.0
    n = src.shape[0]
    os.makedirs(out_dir, exist_ok=True)
    written = []
    for cell, name in sheet["cells"].items():
        x0, y0, x1, y1 = cell_box(n, cell)
        g = src[y0:y1, x0:x1]
        lo, hi = np.percentile(g, 2.0), np.percentile(g, 98.0)
        g = np.clip((g - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
        thr = _otsu(g)
        m = g > thr
        if m.mean() > 0.6:            # the sheet came back inverted
            m = ~m
        rel = max(1.0, min(g.shape) / 200.0)
        m = clean_mask(m, open_r=rel, close_r=rel)
        if not m.any():
            print(f"  {cell} -> {name}: EMPTY after cleanup, skipped")
            continue
        ys, xs = np.nonzero(m)
        m = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        # pad back to square so the motif keeps its aspect inside a square file
        hgt, wid = m.shape
        side = max(hgt, wid)
        sq = np.zeros((side, side), dtype=bool)
        sq[(side - hgt) // 2:(side - hgt) // 2 + hgt,
           (side - wid) // 2:(side - wid) // 2 + wid] = m
        px = SYMBOLS[name]["px"]
        cov = _resize_mask(sq, px)
        h = bevel_from_mask(cov, bevel_px=bevel_px)
        p = _write_symbol(out_dir, name, h, cov)
        written.append(p)
        if report:
            print(f"  {cell} -> {name}: coverage {cov.mean() * 100:5.1f}%  "
                  f"threshold {thr:.3f}  {px}px  {p}")
    return written


def _write_symbol(out_dir, name, height, cov):
    a = np.zeros(height.shape + (4,), dtype=np.float64)
    a[..., 0] = a[..., 1] = a[..., 2] = np.clip(height, 0.0, 1.0)
    a[..., 3] = np.clip(cov, 0.0, 1.0)
    p = os.path.join(out_dir, f"{name}.png")
    os.makedirs(out_dir, exist_ok=True)
    Image.fromarray((a * 255.0 + 0.5).astype(np.uint8), "RGBA").save(p, optimize=True)
    return p


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


def proc_rest_alt(px, seed=0):
    img, dr = _canvas(px, px)
    s = px * SS
    c = s / 2.0
    dr.ellipse([c - s * 0.44, c - s * 0.44, c + s * 0.44, c + s * 0.44], fill=255)
    dr.ellipse([c - s * 0.24, c - s * 0.40, c + s * 0.52, c + s * 0.36], fill=0)
    _star(dr, c - s * 0.10, c, s * 0.20, s * 0.085, 6, rot=-math.pi / 2)
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
    "rest_alt": proc_rest_alt, "maker_mark": proc_maker_mark, "pip": proc_pip,
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
        p = os.path.join(symbols_dir, f"{name}.png")
        if os.path.exists(p):
            rgba = T.load_rgba(p)
            got = (rgba[..., 0], rgba[..., 3])
            got = (_fit(got[0], size), _fit(got[1], size))

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
    print("auto-level (p2/p98) -> Otsu threshold (measured, not guessed) -> exact\n"
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
        print("Cell map (A=column, 1=row; A1 top-left):")
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
    """Prove the AI-sheet post-processing works BEFORE anyone pays for a sheet.

    Builds a synthetic 1024^2 nine-cell sheet from the procedural motifs and
    deliberately damages it with the failure modes a real generation actually
    shows: a cell returned with grey shading instead of flat white, a cell
    returned inverted, a cell peppered with speckle, and a whole-sheet lift of
    the black point. Then runs process_sheet over it and checks every motif
    came back with sane coverage.

    A pipeline stage that has never executed is a stage that does not work, and
    this one is the stage that would otherwise first execute on paid input."""
    os.makedirs(work_dir, exist_ok=True)
    sheet = next(s for s in SHEETS if s["id"] == sheet_id)
    n = sheet["size"]
    canvas = np.zeros((n, n), dtype=np.float64)
    rg = np.random.default_rng(99)

    damaged = {}
    for cell, name in sheet["cells"].items():
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
    written = process_sheet(sheet_png, out, sheet_id)

    ok = True
    for cell, name in sheet["cells"].items():
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
    print(f"  SHEET SELFTEST {'PASS' if ok else 'FAIL'} "
          f"({len(written)}/{len(sheet['cells'])} motifs recovered, damage included)")
    return ok


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="board decorative motifs")
    ap.add_argument("--selftest", metavar="DIR", nargs="?", const="unity/board-prep/out/sheet_test",
                    help="prove the AI-sheet post-processing works on a synthetic sheet")
    ap.add_argument("--spec", action="store_true", help="print the symbol specification")
    ap.add_argument("--contact", metavar="DIR", help="render a contact sheet of every motif")
    ap.add_argument("--bake", metavar="DIR", help="bake placeholder motif files")
    ap.add_argument("--process", metavar="SHEET_PNG", help="post-process a generated sheet")
    ap.add_argument("--sheet-id", default="sheet_a")
    ap.add_argument("--out", default="unity/board-prep/out/symbols")
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
    if a.selftest:
        raise SystemExit(0 if selftest_sheet(a.selftest, a.sheet_id) else 1)
    if not any((a.spec, a.contact, a.bake, a.process, a.selftest)):
        ap.print_help()
