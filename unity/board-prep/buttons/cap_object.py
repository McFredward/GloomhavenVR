#!/usr/bin/env python3
"""ROUND 3 -- the keycap as a MADE OBJECT rather than a material swatch.

WHY ROUND 3 EXISTS, and what rounds 1 and 2 got right
-----------------------------------------------------
Round 1 asked gpt-image-2 for MATERIALS and got micro-mottle; rejected as "einheitlich".
Round 2 found a real arithmetic lever -- a feature `f` texels wide in a 1024^2 plate arrives at
`f * 0.151` screen pixels, so the ask must name a feature SIZE -- and raised cap-scale STORY
contrast by +60 / +71 / +147 %. **Rejected with the same word.** The lever was real and it was
not the whole thing.

`plate_forensics.py` measures what the difference actually is, against the one thing from the
same generator the user volunteered praise for ("Die Rueckseite der boards gefaellt mir sehr
gut!"), and the answer is not contrast at any scale:

    REG (mean luminance vs distance-to-border, peak-to-trough, % of mean)
        accepted board backs         57.3 / 89.8 / 106.6      mean 84.6
        round-2 cap plates           14.6 / 18.0 /  30.9      mean 21.2
        a STATIONARY NOISE SWATCH                             mean 17.4
        the SHIPPED atlas cells       3.7 /  9.9 /   3.8      mean  5.8

The shipped cap face has LESS border structure than random noise. The board backs have four
times as much. And on total contrast the round-2 plates sit at 19.6 against the noise swatch's
20.0 -- round 2 bought exactly as much contrast as a random field and exactly as much layout.

THE TWO CAUSES, and the second one is in this repository rather than in the prompt
---------------------------------------------------------------------------------
1. **The ask was for a SWATCH.** A material sample is stationary by construction; that is what
   a sample IS. Raising its contrast makes a louder uniform field.
2. **`cap_atlas.material_cell` takes a RANDOM CROP at a RANDOM OFFSET.** Even the little
   registration round 2's plates had does not survive it: REG 18.0 -> 3.7 (oak), 30.9 -> 3.8
   (bronze). An unregistered crop cannot carry a rim, because a rim is a statement about WHERE.

THE STRUCTURAL FACT THAT MAKES THE FIX POSSIBLE
-----------------------------------------------
**One atlas cell IS the whole button, edge to edge.** The keycap meshes UV planar over their
own footprint (`CardMesh`: `Uv(p) = (p.x/width + 0.5, p.y/height + 0.5)` on EVERY vertex,
walls included), so cell UV (0,0)..(1,1) is exactly the cap's outline. The signet profile's
bands therefore have exact, known positions in the cell:

    outer chamfer   d in [0.000, 0.060)      d = distance to the cap's outline, in units of
    rim land        d in [0.060, 0.105)          the cap's SHORT side
    inner chamfer   d in [0.105, 0.135)
    recessed field  d in [0.135, 0.500]

(`CapFaceLayout.BezelChamfer/BezelRim/BezelStep`, and the vertical field band is exactly
[0.135, 0.865] on all three boards.) The cap is NOT square, so the same absolute band is a
different fraction of the cell's u: 0.1206 (oak), 0.1331 (steel), 0.1114 (bronze) against
0.135 in v. That anisotropy is why the art is generated ONCE per board on a square template
and REGISTERED per board here, rather than trusted to land right out of the model.

WHAT THIS MODULE DOES
---------------------
    init_frames()   the geometry the model is shown: a square signet plate and a round one,
                    on a flat backdrop, with the four bands at their exact fractions and the
                    band shading that `BoardLit`'s baked key implies.
    PROMPTS         the asks, VERBATIM. Round 2 lost the wording for two shipped plates and
                    had to record "substance, not prompts". This file is the record.
    segment()       find each plate's silhouette against the backdrop -- the plates are drawn
                    as objects ON a backdrop precisely so the crop is MEASURED, not assumed.
    register()      warp the crop so its bands land on the mesh's, per board and per shape.
    zone_report()   where the model actually put its rim, against where it belongs.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

REPO = os.path.dirname(os.path.dirname(PREP))
OUT = os.path.join(HERE, "out")
GEN = os.path.join(PREP, "out")

STYLES = ("oak", "steel", "bronze")

# ---------------------------------------------------------------------------------------
# THE PROFILE. Copied from Cards/CapFaceLayout.cs -- and `cap_check.py --lint` reads BOTH
# files as text so they can never drift apart silently (the lint that would have caught
# ModBuild 281).
# ---------------------------------------------------------------------------------------
BEZEL_CHAMFER = 0.060
BEZEL_RIM = 0.045
BEZEL_STEP = 0.030
BEZEL_TOTAL = BEZEL_CHAMFER + BEZEL_RIM + BEZEL_STEP        # 0.135
BANDS = (BEZEL_CHAMFER,
         BEZEL_CHAMFER + BEZEL_RIM,
         BEZEL_TOTAL)                                        # 0.060, 0.105, 0.135

# `BoardAnchors.FitCapSize`, mm, per board (width x height). The SHORT side is the height on
# all three, which is why the vertical band fraction is the plain 0.135 everywhere.
CAP_MM = {"oak": (63.0, 56.3), "steel": (63.0, 62.1), "bronze": (53.2, 43.9)}


def aspect(style):
    """(u-scale, v-scale) that turn a band fraction of the SHORT side into a fraction of the
    cell's u and v. v is always 1.0; u shrinks because the cap is wider than it is tall."""
    w, h = CAP_MM[style]
    return min(w, h) / w, min(w, h) / h


# ---------------------------------------------------------------------------------------
# THE INIT FRAME -- the geometry the model is shown
# ---------------------------------------------------------------------------------------
BACKDROP = (108, 108, 112)
INIT_SIZE = (1536, 1024)        # 3:2 -- a NATIVE aspect for this tool, so the 16:9-into-3:2
                                # rescale trap recorded in ../img2img/README.md cannot fire.
PLATE_FRAC = 0.80               # the plate's side as a fraction of its 1024-square panel


# `BoardLit`'s baked key, verbatim from the shader: above, toward the viewer, slightly right.
# The template's band values are a DOT PRODUCT against it, not a hand-picked ramp -- the first
# version of this file hand-signed them and got both chamfers backwards, which would have
# taught the model to paint its relief lit from BELOW and put the painted shading in direct
# opposition to the geometry the mesh actually has. The failure mode the brief names as a
# "doubled edge" is that, with a sign error on top.
KEY = np.array([0.35, 0.85, -0.45])
KEY = KEY / np.linalg.norm(KEY)
AMBIENT = 0.34
FIELD_AO = 0.90                 # the recessed field sits in its own frame's shadow


def _shade(nx, ny, nz):
    """Lambert against the shader's baked key, plus a flat ambient. Normals are in CAP space:
    +x right, +y UP the cap, -z toward the viewer."""
    n = np.stack([nx, ny, nz], axis=-1)
    n = n / np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
    return AMBIENT + (1.0 - AMBIENT) * np.clip(n @ KEY, 0.0, 1.0)


def _bands_from(d, ex, ey):
    """Band values for a distance-to-outline field `d` and an OUTWARD unit direction (ex, ey).

    outer chamfer  falls away outward       normal = ( ex,  ey, -1) / sqrt2
    rim land       frontmost, flat          normal = (  0,   0, -1)
    inner chamfer  steps DOWN inward        normal = (-ex, -ey, -1) / sqrt2
    recessed field flat, one step back      normal = (  0,   0, -1), times FIELD_AO
    """
    s = 1.0 / np.sqrt(2.0)
    flat = _shade(np.zeros_like(d), np.zeros_like(d), -np.ones_like(d))
    out = _shade(ex * s, ey * s, -np.ones_like(d) * s)
    inn = _shade(-ex * s, -ey * s, -np.ones_like(d) * s)
    v = np.where(d < BANDS[0], out,
                 np.where(d < BANDS[1], flat,
                          np.where(d < BANDS[2], inn, flat * FIELD_AO)))
    return v / float(flat.max())


def _square_template(n):
    """A square signet plate, bands at their exact fractions of the side."""
    ys = (np.arange(n) + 0.5) / n
    xs = (np.arange(n) + 0.5) / n
    dy = np.broadcast_to(np.minimum(ys, 1.0 - ys)[:, None], (n, n))
    dx = np.broadcast_to(np.minimum(xs, 1.0 - xs)[None, :], (n, n))
    d = np.minimum(dy, dx)
    # Outward direction = toward the NEAREST edge. PIL row 0 is the image's top and the cap's
    # v = 1 is the cap's top, so a texel in the upper rows is near the cap's TOP edge and its
    # outward direction is +y in cap space.
    vert = dy <= dx
    ex = np.where(vert, 0.0, np.where(np.broadcast_to(xs[None, :], (n, n)) < 0.5, -1.0, 1.0))
    ey = np.where(vert, np.where(np.broadcast_to(ys[:, None], (n, n)) < 0.5, 1.0, -1.0), 0.0)
    return _bands_from(d, ex, ey), d


def _round_template(n):
    """A round signet plate. Same bands, measured radially -- the rest pads are round
    (`CardMesh.BuildRoundKeycap`), and their UV is planar over a CIRCULAR footprint, so their
    bands are annuli and a rectangular registration would be wrong for them."""
    ys = (np.arange(n) + 0.5) / n - 0.5
    xs = (np.arange(n) + 0.5) / n - 0.5
    Y = np.broadcast_to(ys[:, None], (n, n))
    X = np.broadcast_to(xs[None, :], (n, n))
    r = np.maximum(np.sqrt(Y * Y + X * X), 1e-9)
    d = 0.5 - r
    ex, ey = X / r, -Y / r                 # -Y: image rows run down, cap v runs up
    out = _bands_from(np.clip(d, 0.0, None), ex, ey)
    return np.where(d < 0.0, np.nan, out), d


def init_frames(dst_dir=OUT):
    """Write one 1536x1024 init frame per board: square plate left, round plate right, both on
    a flat backdrop with the four bands at their exact fractions.

    THE PLATES SIT ON A BACKDROP WITH MARGIN ON PURPOSE. It is the one thing that makes the
    crop MEASURABLE afterwards -- a plate that fills the frame edge to edge gives the
    registration step nothing to find, and a registration that has to be assumed is exactly how
    a doubled edge ships.
    """
    os.makedirs(dst_dir, exist_ok=True)
    W, H = INIT_SIZE
    panel = W // 2                  # two 768x1024 panels in the 3:2 frame
    n = int(panel * PLATE_FRAC)
    sq, _ = _square_template(n)
    rd, _ = _round_template(n)
    written = []
    for style in STYLES:
        ref = os.path.join(dst_dir, f"ref_face_{style}.png")
        tint = np.array([0.62, 0.55, 0.44])
        if os.path.isfile(ref):
            a = np.asarray(Image.open(ref).convert("RGB"), dtype=np.float64) / 255.0
            tint = a.reshape(-1, 3).mean(axis=0)
            tint = tint / max(tint.max(), 1e-6) * 0.72
        img = np.zeros((H, W, 3), dtype=np.float64)
        img[:, :] = np.array(BACKDROP) / 255.0
        for k, tpl in enumerate((sq, rd)):
            y0 = (H - n) // 2
            x0 = k * panel + (panel - n) // 2
            body = tpl[..., None] * tint[None, None, :]
            m = ~np.isnan(tpl)
            dst = img[y0:y0 + n, x0:x0 + n]
            dst[m] = np.clip(body, 0, 1)[m]
        # NO CAPTION IS DRAWN ON THE INIT FRAME. The ask forbids letters anywhere on the
        # plates, and a label burnt into the reference is the most reliable way to get the
        # model to paint one back.
        out = Image.fromarray((np.clip(img, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB")
        p = os.path.join(dst_dir, f"capinit_{style}.png")
        out.save(p)
        written.append(p)
        print(f"  {style}: {p}  {out.size}  plate {n}px in a {panel}px panel  "
              f"bands {BANDS} of the plate's side")
    return written


# =========================================================================================
# THE PROMPTS, VERBATIM.
#
# Round 2's record says, of its own two shipped plates: "the revised WORDING was not captured
# before the generating session ended -- only its substance". These strings are the exact ones
# passed to `create_asset`, and `cap_check.py --lint` asserts that every generated round-3 file
# in ../out is named by CHOSEN or DISCARDED below, so the record cannot go stale silently.
# =========================================================================================
_COMMON = """Two objects on one flat neutral grey studio backdrop, photographed from directly
overhead, orthographic, no perspective, no cast shadow, no vignette, no depth of field: LEFT a
square signet button plate, RIGHT a round signet button plate of the same set. Both are the
same size as the grey template image supplied, in the same positions, with the same margin of
backdrop around them, and both are axis-aligned.

The supplied grey image is the exact GEOMETRY of these two plates and must be followed band for
band. Reading inward from the outside edge of each plate there are exactly four concentric
bands and no others:
  1. an OUTER CHAMFER, 6 % of the plate's width, cut at 45 degrees, falling away outward;
  2. a flat RIM LAND, 4.5 % of the plate's width, at the very front of the plate, the brightest
     and most polished surface on it;
  3. an INNER CHAMFER, 3 % of the plate's width, cut at 45 degrees, stepping DOWN inward;
  4. a large flat RECESSED FIELD filling the whole middle, sunk below the rim.
The band edges must be crisp, unbroken and exactly where the template puts them.

THE RECESSED FIELD IS COMPLETELY EMPTY. No letter, no digit, no word, no rune, no icon, no
symbol, no engraving, no logo and no ornament anywhere in the field or anywhere else on either
plate. The field carries nothing but its own bare material.

This is a used object, not a material sample. It must carry the marks of having been made and
handled, and each of them must be BIG -- an eighth to a sixth of the plate across, so it
survives being seen small:
{story}

Light it as if from above and slightly toward the viewer, one soft key, no coloured rim light.
Fill the frame; the two plates and the flat backdrop are the entire image."""

PROMPTS = {
    "oak": _COMMON.format(story="""  - the plate is carved from one piece of quarter-sawn oak; the
    grain runs top to bottom and the RIM LAND is cut across it, so the rim shows short end-grain
    ticks where the field shows long straight figure;
  - a band of silver ray fleck about a sixth of the plate wide crosses the field off-centre;
  - the bottom half of the rim land is rubbed pale and glassy where a thumb has pressed it for
    years, and that polish fades out over about a sixth of the plate; the top of the rim is
    still dark and waxy;
  - dark wax and dirt have collected in the inner chamfer and in the corners, darkest at the
    bottom corners;
  - one small shallow ding on the outer chamfer at the lower left, and a fine split in the wood
    running a third of the way in from one edge."""),
    "steel": _COMMON.format(story="""  - the plate is forged and then draw-filed from one piece of
    dark blued steel; the file has left straight parallel tool marks across the field, each
    about a twentieth of the plate apart, all running the same way;
  - two or three soft temper-bloom clouds, each about a sixth of the plate across, drift across
    the field in straw-gold and faint violet;
  - the rim land is worn back to bright bare metal along its bottom edge and stays dark blue at
    the top, and the transition between the two is ragged rather than straight;
  - red-brown oxide has crept out of the inner chamfer into the field for about a tenth of the
    plate, worst at the bottom corners;
  - one shallow peening dent about an eighth of the plate across on the field, off-centre."""),
    "bronze": _COMMON.format(story="""  - the plate is sand-cast in bronze; a faint casting seam
    runs across the outer chamfer and over the rim land on one side, and the cast surface of the
    field is subtly dimpled where the sand was;
  - the rim land is burnished to bright warm gold along its lower edge where it is handled, and
    the rest of the rim keeps a duller brown skin;
  - green-blue verdigris has pooled in the inner chamfer all the way round and creeps a tenth of
    the plate into the field at the two bottom corners; the high rim land is almost free of it;
  - two or three broad shallow planishing dishes, each about a sixth of the plate across, catch
    the light on the field;
  - one small round foundry punch mark, no letters or digits in it, sits near a lower corner of
    the field."""),
}


# =========================================================================================
# SEGMENTATION -- find each plate against the backdrop, MEASURED rather than assumed
# =========================================================================================
def _largest_run(mask):
    """(start, stop) of the longest True run in a 1-D boolean array, or None."""
    best = None
    i = 0
    n = len(mask)
    while i < n:
        if mask[i]:
            j = i
            while j < n and mask[j]:
                j += 1
            if best is None or (j - i) > (best[1] - best[0]):
                best = (i, j)
            i = j
        else:
            i += 1
    return best


def segment(path, panels=2, tol=0.10, cover=0.55):
    """Crop each panel's plate out of a generated frame, by finding where it is NOT backdrop.

    The backdrop colour is taken from the frame's own four corners rather than from `BACKDROP`,
    because the model repaints the backdrop in its own grey and a hard-coded reference would
    quietly segment the wrong thing. Returns a list of (x0, y0, x1, y1) boxes, one per panel.
    """
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
    h, w = a.shape[:2]
    k = max(4, min(h, w) // 64)
    corners = np.concatenate([a[:k, :k].reshape(-1, 3), a[:k, -k:].reshape(-1, 3),
                              a[-k:, :k].reshape(-1, 3), a[-k:, -k:].reshape(-1, 3)])
    bg = np.median(corners, axis=0)
    fg = np.linalg.norm(a - bg[None, None, :], axis=2) > tol
    boxes = []
    pw = w // panels
    for p in range(panels):
        sub = fg[:, p * pw:(p + 1) * pw]
        colrun = _largest_run(sub.mean(axis=0) > cover * sub.mean(axis=0).max())
        rowrun = _largest_run(sub.mean(axis=1) > cover * sub.mean(axis=1).max())
        if colrun is None or rowrun is None:
            boxes.append(None)
            continue
        boxes.append((p * pw + colrun[0], rowrun[0], p * pw + colrun[1], rowrun[1]))
    return boxes, bg


# =========================================================================================
# REGISTRATION -- put the model's bands where the MESH's bands are
# =========================================================================================
def _piecewise(src_knots, dst_knots, n):
    """A 1-D sampling map: for each of `n` output texels, which input coordinate to read.

    Both knot lists are in [0, 1] and are the band boundaries measured inward from BOTH edges,
    so the map is symmetric by construction and the plate's centre stays its centre.
    """
    o = (np.arange(n) + 0.5) / n
    s = np.array([0.0] + list(dst_knots) + [0.5])
    t = np.array([0.0] + list(src_knots) + [0.5])
    half = np.interp(np.minimum(o, 1.0 - o), s, t)
    return np.where(o <= 0.5, half, 1.0 - half)


def _resize(crop, n):
    return np.asarray(Image.fromarray(
        (np.clip(crop, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB")
        .resize((n, n), Image.Resampling.LANCZOS), dtype=np.float64) / 255.0


# THE INWARD CONTINUATION OF THE PLAIN CELL, and the structural fact that forces it.
#
# One atlas cell is read by THREE submeshes and the split is not the obvious one
# (`PlayTray.7.Nested.cs`): submesh [0], the recessed FIELD, takes the ROLE cell; submesh [1],
# the bezel ring, and submesh [2], the walls, both take `CapRole.Plain` -- cell 0 -- on EVERY
# cap. So:
#
#   * a ROLE cell (1..8) is only ever sampled at d > 0.135. Its bezel band is never drawn.
#   * the PLAIN cell is only ever sampled at d < 0.135 ... by a SQUARE cap. A ROUND cap's bezel
#     is an ANNULUS, and an annulus in a square-registered cell runs out into the middle of the
#     cell at the diagonals: at 45 degrees the round bezel sits at square-distance 0.146..0.242.
#   * the PLAIN cell's own field is never a cap FACE. The only two buttons built with the
#     default `CapRole.Plain` are `CombatLogSurface`'s pin and close, and they pass
#     `capStyle: null`, which `NewKeycapMaterial` routes to the shared KeycapGrain with NO cell
#     transform at all.
#
# ONE CELL CANNOT CARRY BOTH SHAPES' BANDS -- that was checked rather than assumed. Registering
# the plain cell on `min(d_square, d_round)` puts chamfer material over ~74 % of the SQUARE
# cap's rim land; registering on `max` puts field material over ~38 % of the ROUND cap's bezel.
# So the plain cell is registered EXACTLY for the square cap out to d = 0.135, and everything
# further in -- which no square cap ever samples -- is filled with RIM-LAND material. A round
# cap's bezel then reads bezel-family material at every angle: exact on the four axes, drifting
# to plain rim material at the four diagonals. That drift is this round's stated cost, and it
# is a wear difference rather than a wrong band.
FIELD_TO_RIM_LO = BANDS[1]      # 0.105 -- what d = 0.135 maps to
FIELD_TO_RIM_HI = BANDS[0]      # 0.060 -- what d = 0.500 maps to


def _band_coord(n, su, sv):
    """(b, vertical) for every texel of an n x n cell: b is the distance to the cap's outline in
    units of the cap's SHORT side, and `vertical` says whether the nearest edge is a horizontal
    one (top/bottom) or a vertical one (left/right)."""
    ys = (np.arange(n) + 0.5) / n
    xs = (np.arange(n) + 0.5) / n
    dv = np.broadcast_to(np.minimum(ys, 1.0 - ys)[:, None], (n, n)) / sv
    du = np.broadcast_to(np.minimum(xs, 1.0 - xs)[None, :], (n, n)) / su
    return np.minimum(dv, du), dv <= du


def register_square(crop, style, n, continue_rim=False):
    """Resample a segmented SQUARE plate so its bands land exactly on the mesh's.

    The model is shown an ISOTROPIC template (bands at 0.060 / 0.105 / 0.135 of the plate's
    side). The cap is not square, so in the atlas cell's u those same absolute bands are
    0.1206 (oak) / 0.1331 (steel) / 0.1114 (bronze). This is the step that turns "the model
    roughly drew a rim" into "the rim is ON the rim land", and it is why the doubled edge the
    brief warns about cannot happen: the band positions are not inferred from the picture, they
    are imposed on it.

    The gather is BAND-PRESERVING: a texel's distance-to-outline decides its source distance,
    and its position ALONG the nearest edge is carried through unchanged. Nothing is sampled
    across a band boundary, so a rim never bleeds into a field.
    """
    su, sv = aspect(style)
    src = _resize(crop, n)
    b, vertical = _band_coord(n, su, sv)
    ys = np.broadcast_to(((np.arange(n) + 0.5) / n)[:, None], (n, n))
    xs = np.broadcast_to(((np.arange(n) + 0.5) / n)[None, :], (n, n))

    # THE BAND GATHER: distance-to-outline decides the source distance, position along the
    # nearest edge is carried through. Exact inside the bezel, which is all the square cap ever
    # reads of the plain cell and all any cap reads of a role cell's rim.
    sy = np.where(vertical, np.where(ys < 0.5, b, 1.0 - b), ys)
    sx = np.where(vertical, xs, np.where(xs < 0.5, b, 1.0 - b))
    if not continue_rim:
        iy = np.clip((sy * n).astype(int), 0, n - 1)
        ix = np.clip((sx * n).astype(int), 0, n - 1)
        return src[iy, ix]

    # THE INWARD CONTINUATION IS AN ANGULAR SWEEP, NOT A NEAREST-EDGE PUSH -- and the first
    # version was the push. Pushing every interior texel out to the rim along its NEAREST edge
    # makes the whole upper interior sample one source ROW, smeared downward, and the nearest
    # edge changes at the diagonals: the result is four triangular sectors with hard seams
    # running along the diagonals (`capcell_steel_bezel.png` at the time showed it as a
    # pinwheel). The diagonals are precisely where a ROUND cap's bevel samples this cell, so the
    # artefact would have landed exactly on the one reader this fill exists for.
    #
    # Sweeping by ANGLE instead is continuous all the way round: each interior texel reads the
    # rim band along its own direction from the centre, so the fill is a smooth annular
    # stretch of the rim with no seam anywhere.
    t = np.clip((b - BANDS[2]) / (0.5 - BANDS[2]), 0.0, 1.0)
    bs = FIELD_TO_RIM_LO + (FIELD_TO_RIM_HI - FIELD_TO_RIM_LO) * t
    ang = np.arctan2(ys - 0.5, xs - 0.5)
    c, s = np.cos(ang), np.sin(ang)
    m = np.maximum(np.maximum(np.abs(c), np.abs(s)), 1e-9)
    px = 0.5 + (0.5 - bs) * c / m
    py = 0.5 + (0.5 - bs) * s / m
    # Cross-fade so the two gathers meet without a step. The blend band is inside the field, so
    # no square cap ever sees it, and a round cap crosses it smoothly.
    w = np.clip((b - BANDS[2]) / 0.020, 0.0, 1.0)[..., None]
    iy0 = np.clip((sy * n).astype(int), 0, n - 1)
    ix0 = np.clip((sx * n).astype(int), 0, n - 1)
    iy1 = np.clip((py * n).astype(int), 0, n - 1)
    ix1 = np.clip((px * n).astype(int), 0, n - 1)
    out = src[iy0, ix0] * (1.0 - w) + src[iy1, ix1] * w
    # AND THE DEEP INTERIOR IS WASHED OUT, because an angular sweep still STRETCHES: carried all
    # the way to the centre it draws a tunnel of radial streaks converging on a vanishing point.
    # The deepest any reader goes into this cell is a round cap's bevel at the diagonals, which
    # reaches square-distance 0.242 -- so the sweep keeps its grain out to 0.26 and fades to the
    # rim band's own mean colour beyond it, where nothing samples and only the MIP average (a
    # mean, not a pattern) can still see it.
    rimband = (b >= BANDS[0]) & (b < BANDS[1])
    wash = out[rimband].reshape(-1, 3).mean(axis=0) if rimband.any() else out.reshape(-1, 3).mean(0)
    k = np.clip((b - 0.26) / 0.16, 0.0, 1.0)[..., None]
    return out * (1.0 - k) + wash[None, None, :] * k


def register_round(crop, n):
    """A segmented ROUND plate into a round cap's cell.

    `CardMesh.BuildRoundKeycap` takes ONE diameter, so the round cap's footprint is a true
    circle inscribed in a square cell and its UV is isotropic -- there is no per-board
    anisotropy to undo here, unlike the square caps. The generated disc's own bands are
    measured and reported by `zone_report`; this is the identity resample, and if the model's
    radii ever drift far enough to matter the correction belongs here and nowhere else.
    """
    return _resize(crop, n)


# =========================================================================================
# INGEST -- generated frame -> two cached square crops per board
# =========================================================================================
GENERATED = {s: f"keycap3_object_{s}.png" for s in STYLES}

# THE WHOLE ROUND-3 IMAGE BUDGET, and the record round 2 could not keep.
#
# FOUR images, every one 3:2, every one delivered EXACTLY 1536x1024 and read back with PIL
# rather than assumed (the 16:9-into-3:2 rescale trap of ../img2img/README.md cannot fire on a
# native 3:2 ask). Three kept, one discarded. That is four against round 2's seven for the same
# three boards, and the reason is in `PROMPTS`: an ask for an OBJECT with named, sized, PLACED
# marks lands first time where an ask for a MATERIAL had to be rolled four times for bronze
# alone. Each image also carries BOTH shapes, so four images cover six plates.
#
# THE DISCARD IS THE INTERESTING ONE, because it is round 2's own mistake offered again.
# `caps_on_his_bronze_well.png` redraws only the recessed FIELD, and on that panel round 3's
# bronze is CALMER than the shipped cap (field contrast 13.4 % -> 9.1 %). So a fourth bronze was
# asked for with the field's planishing dishes made much larger and the verdigris flooded
# further in, and it did exactly what it was asked to do:
#
#     roll                       REG    cell sigma   rendered sigma WHOLE   FIELD    clipped
#     keycap3_object_bronze      23.7      18.7            17.3 %           7.0 %     2.4 %
#     keycap3_object_bronze_b    18.5      17.3            15.3 %           9.2 %     7.1 %
#
# It buys 31 % more contrast in the field and gives back 22 % of the REGISTRATION and 12 % of
# the contrast of the WHOLE cap, which is what a player actually looks at. Taking it would have
# been round 2's error in a new place: optimising the one term an instrument happened to be
# pointed at. Discarded on that measurement, not on taste.
CHOSEN = dict(GENERATED)
DISCARDED = {
    "keycap3_object_bronze_b.png":
        "field contrast 7.0 -> 9.2 %, but REG 23.7 -> 18.5 and whole-cap rendered contrast "
        "17.3 -> 15.3 % with clipping 2.4 -> 7.1 %; the field is one term of the cap",
}


def _check_manifest(out_dir=GEN):
    """RAISE if a generated round-3 plate is unaccounted for, or is listed both ways.

    Round 2 shipped a `plates.py` whose narrative described a different set of images from the
    one its own `CHOSEN` named, and nobody noticed because nothing checked. This runs at import.
    Driven negative before it was believed: removing an entry from `DISCARDED` makes it raise by
    name.
    """
    if not os.path.isdir(out_dir):
        return
    named = set(CHOSEN.values()) | set(DISCARDED)
    both = set(CHOSEN.values()) & set(DISCARDED)
    if both:
        raise RuntimeError(f"cap_object: plate(s) both kept and discarded: {sorted(both)}")
    found = {f for f in os.listdir(out_dir)
             if f.startswith("keycap3_object_") and f.endswith(".png")}
    missing = found - named
    if missing:
        raise RuntimeError(
            "cap_object: round-3 plate(s) in ../out that the record does not account for: "
            f"{sorted(missing)} -- add each to CHOSEN or to DISCARDED with its reason")
    absent = named - found
    if absent:
        raise RuntimeError(
            f"cap_object: the record names plate(s) that are not in ../out: {sorted(absent)}")


_check_manifest()


def crops(out_dir=GEN, cache=OUT, report=True):
    """Cut every generated frame into its square plate and its round plate.

    THE CROP IS THE SILHOUETTE, RESAMPLED PER AXIS -- and the two cleverer things that were
    tried first are recorded because both failed, and both failed the same way.

    (1) *Force the crop square about the silhouette's centre.* The model photographs each plate
        with a trace of perspective, so a sliver of the plate's own side wall and a soft contact
        shadow fall inside the silhouette: oak's square plate measures 643 x 698 for an object
        that is square. Taking `min` of the two then cut 28 px off the bottom chamfer -- visible
        in `capcrop_oak_square.png` at the time.
    (2) *Fit the inner-chamfer contour and scale out through the known 0.73 field span.* The
        strongest gradient ridge in the frame is the plate-to-backdrop edge, so the fit locked
        onto that and returned an 805 px plate inside a 643 px silhouette. Windowing the search
        rescued it partly and still disagreed with itself by 5-13 % between the two axes.
    (3) *Cross-correlate the ridge profile against the init template the model was shown.*
        Correlation 0.89-0.98, and per-axis scales that disagreed by up to 13.5 % -- the same
        plate-to-backdrop ridge dominating the fit again.

    So the crop is the silhouette box, resampled to a square cell per axis, which absorbs the
    aspect error into an 8 % vertical stretch of a wood grain nobody can measure by eye. The
    residual is then MEASURED, after registration, by `zone_report` on the finished cell: the
    number that matters is not where the model put its rim but where the rim ends up.
    """
    os.makedirs(cache, exist_ok=True)
    out = {}
    for style in STYLES:
        p = os.path.join(out_dir, GENERATED[style])
        if not os.path.isfile(p):
            if report:
                print(f"  {style}: MISSING {p}")
            continue
        boxes, bg = segment(p)
        a = np.asarray(Image.open(p).convert("RGB"), dtype=np.float64) / 255.0
        got = {}
        for shape, box in zip(("square", "round"), boxes):
            if box is None:
                if report:
                    print(f"  {style}/{shape}: no silhouette found")
                continue
            x0, y0, x1, y1 = box
            crop = a[y0:y1, x0:x1]
            got[shape] = crop
            dst = os.path.join(cache, f"capcrop_{style}_{shape}.png")
            Image.fromarray((np.clip(crop, 0, 1) * 255.0 + 0.5).astype(np.uint8),
                            "RGB").save(dst)
            if report:
                print(f"  {style}/{shape}: silhouette {x1 - x0}x{y1 - y0} at ({x0},{y0}), "
                      f"aspect {(x1 - x0) / max(y1 - y0, 1):.3f}   "
                      f"backdrop {np.round(bg, 3).tolist()}")
        out[style] = got
    return out


_CACHE = {}


def cell_art(style, kind, n, out_dir=GEN, cache=OUT):
    """One registered cell of material. `kind` is "square", "round" or "bezel"."""
    key = (style, kind, n)
    if key in _CACHE:
        return _CACHE[key]
    src = os.path.join(cache, f"capcrop_{style}_{'round' if kind == 'round' else 'square'}.png")
    if not os.path.isfile(src):
        crops(out_dir, cache, report=False)
    if not os.path.isfile(src):
        raise FileNotFoundError(f"no registered crop for {style}/{kind}: {src} -- run "
                                f"cap_object.py --ingest after generating the plates")
    crop = np.asarray(Image.open(src).convert("RGB"), dtype=np.float64) / 255.0
    if kind == "round":
        art = register_round(crop, n)
    else:
        art = register_square(crop, style, n, continue_rim=(kind == "bezel"))
    _CACHE[key] = art
    return art


def normalised_cells(style, n, out_dir=GEN, cache=OUT, target=None):
    """The three registered cells of one board, re-based to the shipped grain's level.

    THE GAIN IS SOLVED ON THE FIELD, NOT ON THE WHOLE CELL, and that is the difference between
    preserving the shipped cap-to-well ratio and drifting off it. `BoardLit` computes
    `alb = tex2D(_MainTex, uv) * _Color`, so the texture is a MODULATOR and its level belongs to
    the state palette (round 1's finding, and the "invisible button" defect that produced it).
    The level that decides whether a cap reads proud of its own recess is the level of the FACE
    -- and the face is submesh [0], which samples only the recessed FIELD. Solving on the whole
    cell would let a bright rim pull the gain down and take the face with it.

    ONE gain for all three kinds. Re-basing them independently would re-level the bezel against
    the field and destroy the one thing this round is for: the relationship between the parts of
    one object.
    """
    import cap_atlas as C                                     # noqa: E402  (imported, not copied)
    target = C.GRAIN_TARGET_LUM if target is None else target
    raw = {k: cell_art(style, k, n, out_dir=out_dir, cache=cache)
           for k in ("square", "bezel", "round")}
    su, sv = aspect(style)
    b, _ = _band_coord(n, su, sv)
    field = b >= BANDS[2]

    # The gain is SOLVED on the square cell's field by `cap_atlas.normalise_plate` -- imported,
    # not re-implemented, so the knee, the iteration count and the chroma-safe compression are
    # the one definition -- and then that ONE gain is replayed on the other two kinds.
    sq, gain, got, clipped = C.normalise_plate(raw["square"], target, mask=field)
    base = float(raw["square"].mean(axis=2)[field].mean())
    out = {"square": sq}
    for k in ("bezel", "round"):
        out[k] = np.clip(C._knee(raw[k] * gain), 0.0, 1.0)
    return out, float(gain), float(base), float(got), float(clipped)


def field_jitter(n, seed, style, kind, amount=0.055):
    """A per-CELL wear difference, confined to the recessed field.

    Seven caps cut from one plate would be seven clones. Real siblings differ in what has
    happened TO them, so the difference is a low-frequency stain/burnish field and nothing
    else -- and it is faded to nothing before it reaches the bezel, because the bezel of every
    cap is drawn from the PLAIN cell while the field comes from the ROLE cell, and a jitter
    that reached the boundary would put a visible step exactly on the seam between two
    submeshes' textures.
    """
    rng = np.random.default_rng(seed)
    k = 8
    lo = rng.standard_normal((k, k))
    blob = np.asarray(Image.fromarray(
        ((lo - lo.min()) / max(float(lo.max() - lo.min()), 1e-9) * 255.0).astype(np.uint8), "L")
        .resize((n, n), Image.Resampling.BICUBIC), dtype=np.float64) / 255.0
    blob = (blob - blob.mean()) * 2.0
    if kind == "round":
        ys = (np.arange(n) + 0.5) / n - 0.5
        xs = (np.arange(n) + 0.5) / n - 0.5
        b = 0.5 - np.sqrt(ys[:, None] ** 2 + xs[None, :] ** 2)
    else:
        su, sv = aspect(style)
        b, _ = _band_coord(n, su, sv)
    fade = np.clip((b - BANDS[2]) / 0.05, 0.0, 1.0)
    return 1.0 + amount * blob * fade


# =========================================================================================
# THE REPORT -- where the model actually put its rim
# =========================================================================================
def zone_profile(lum, shape="square", bins=64, su=1.0, sv=1.0):
    """Mean luminance against distance-to-outline, in units of the plate's short side.

    `su`/`sv` are the cell's anisotropy (`aspect`). A REGISTERED cell's bands are narrower in u
    than in v by exactly that ratio, so measuring it with an isotropic distance would report a
    smeared rim on a cell whose rim is exactly where it should be -- an instrument disagreeing
    with the thing it is checking because it models a different geometry.
    """
    h, w = lum.shape
    if shape == "square":
        ys = (np.arange(h) + 0.5) / h
        xs = (np.arange(w) + 0.5) / w
        d = np.minimum(np.minimum(ys, 1 - ys)[:, None] / sv,
                       np.minimum(xs, 1 - xs)[None, :] / su)
        d = np.broadcast_to(d, (h, w))
    else:
        ys = (np.arange(h) + 0.5) / h - 0.5
        xs = (np.arange(w) + 0.5) / w - 0.5
        d = 0.5 - np.sqrt(ys[:, None] ** 2 + xs[None, :] ** 2)
    idx = np.clip((np.clip(d, 0, 0.5) / 0.5 * bins).astype(int), 0, bins - 1)
    prof = np.array([lum[idx == b].mean() if (idx == b).any() else np.nan for b in range(bins)])
    return (np.arange(bins) + 0.5) / bins * 0.5, prof


def zone_report(lum, shape="square", say=print, su=1.0, sv=1.0):
    d, prof = zone_profile(lum, shape, su=su, sv=sv)
    m = np.nanmean(prof)
    peak = int(np.nanargmax(np.where(d < 0.20, prof, -np.inf)))
    trough = int(np.nanargmin(np.where((d > BANDS[1]) & (d < 0.20), prof, np.inf)))

    def band(lo, hi):
        s = (d >= lo) & (d < hi)
        return float(np.nanmean(prof[s])) / m if s.any() else float("nan")

    say(f"      band means / plate mean:  outer chamfer {band(0, BANDS[0]):.3f}   "
        f"rim land {band(BANDS[0], BANDS[1]):.3f}   inner chamfer "
        f"{band(BANDS[1], BANDS[2]):.3f}   field {band(BANDS[2], 0.5):.3f}")
    say(f"      brightest ring at d = {d[peak]:.3f} (rim land is 0.060..0.105); "
        f"darkest inner ring at d = {d[trough]:.3f} (inner chamfer is 0.105..0.135)")
    return dict(peak=float(d[peak]), trough=float(d[trough]),
                chamfer=band(0, BANDS[0]), rim=band(BANDS[0], BANDS[1]),
                step=band(BANDS[1], BANDS[2]), field=band(BANDS[2], 0.5))


def report_cells(n=256, say=print, cache=OUT, out_dir=GEN):
    """Every registered cell, with the bands MEASURED where they finally landed.

    This is the acceptance check for the registration, and it is deliberately taken on the
    OUTPUT rather than on the generated plate: the question the pipeline has to answer is not
    "where did the model draw its rim" but "is the rim on the rim land". The mesh's own bands
    are 0.060 / 0.105 / 0.135 of the cap's SHORT side, and the u-band is narrower than the
    v-band by the cap's aspect, which is exactly what `register_square` imposes.
    """
    say("")
    say("REGISTERED CELLS -- band means as a fraction of the cell mean, and where the "
        "extrema landed")
    say(f"  mesh bands: outer chamfer 0..{BANDS[0]:.3f}, rim land {BANDS[0]:.3f}..{BANDS[1]:.3f}, "
        f"inner chamfer {BANDS[1]:.3f}..{BANDS[2]:.3f}, field {BANDS[2]:.3f}..0.5")
    rows = {}
    for style in STYLES:
        su, sv = aspect(style)
        say("")
        say(f"  {style}  (u-band {BANDS[2] * su:.4f}, v-band {BANDS[2] * sv:.4f} of the cell)")
        for kind, shape in (("square", "square"), ("bezel", "square"), ("round", "round")):
            art = cell_art(style, kind, n, out_dir=out_dir, cache=cache)
            say(f"    {kind:<7}")
            au, av = (su, sv) if shape == "square" else (1.0, 1.0)
            rows[(style, kind)] = zone_report(art.mean(axis=2), shape,
                                              say=lambda t: say("    " + t), su=au, sv=av)
            dst = os.path.join(cache, f"capcell_{style}_{kind}.png")
            Image.fromarray((np.clip(art, 0, 1) * 255.0 + 0.5).astype(np.uint8),
                            "RGB").save(dst)
    return rows


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--init", action="store_true", help="write the three init frames")
    ap.add_argument("--ingest", action="store_true", help="segment the generated frames")
    ap.add_argument("--cells", action="store_true",
                    help="build every registered cell and measure where its bands landed")
    ap.add_argument("--prompts", action="store_true", help="print the asks, verbatim")
    a = ap.parse_args()
    if a.init:
        init_frames()
    if a.ingest:
        crops()
    if a.cells:
        report_cells()
    if a.prompts:
        for k, v in PROMPTS.items():
            print("=" * 96)
            print(k)
            print("=" * 96)
            print(v)
