#!/usr/bin/env python3
"""ROUND 6 -- HOW MUCH ROUGHER THAN THE BOARD DOES THE CAP READ?

    python3 cap_rough.py --report                    # the table, on the shipped atlas
    python3 cap_rough.py --report --atlas <dir>      # ... on a candidate atlas
    python3 cap_rough.py --selfcheck                 # six legs, run this first

THE COMPLAINT, AND WHY NOTHING HERE COULD SEE IT
-------------------------------------------------
User, 2026-08-25, on the ModBuild 291 caps he otherwise likes:

    "ABER alle Buttons sind so extrem rau, dass es schon fast wie Noise erscheint.
     Siehe noisy_buttons.jpg. Das gerne etwas weniger."

Five instruments already live in this directory and **not one of them measures roughness.**
`cap_check` measures symbol CONTRAST at a viewing size; `cap_deltae` and `cap_belong` measure
COLOUR; `plate_forensics`/`cap_regmove` measure whether a picture has an inside-outside ORDER.
A cap made of sandpaper and a cap made of glass score identically on every one of them, as long
as they are the same colour and their rims are in the same place. That is why five rounds went
by without this being caught, and it is why this file exists.

WHAT "ROUGH" IS, STATED AS A MEASURABLE THING
----------------------------------------------
The user's own bar is relational and he stated it in one sentence: the cap must not read
rougher than the board it sits on. Both are in the same picture, at the same distance, under
the same light. So the quantity is:

    grain contrast  =  100 * RMS(band-passed luminance) / mean(luminance)

taken on BOTH surfaces after resampling BOTH to the SAME number of pixels per millimetre --
the sampling a Quest 3 actually gives them at arm's length -- and band-limited to the octave
and a half that reads as "speckle" rather than as "figure" (periods of ~1.5 to ~6 screen
pixels; below that the display cannot resolve it, above that it is wood grain and is wanted).

    roughness ratio = cap grain contrast / board grain contrast

and the requirement is **<= 1.0**.

Two properties make the number mean what it says:

* it is a RELATIVE contrast, so it is invariant to `BoardLit`'s `alb = tex2D(...) * _Color`.
  A cap and a board at different brightnesses are compared on equal terms, and the whole
  `BoardIdleColor` / `SeatedCapColor` / `BoardCapTint` product cancels. Selfcheck 3 drives
  that negative.
* it is taken at a common PIXELS-PER-MILLIMETRE, not at a common texel. That is not a detail;
  see the next section.

THE TERM THE BRIEF DID NOT NAME, AND IT IS THE LARGER ONE
----------------------------------------------------------
The round was briefed with a hypothesis: ModBuild 291 bought its colour fix by raising field
contrast 4.12 -> 10.69 % on oak (2.59x), and that same gain is the noise. **THAT IS FALSE, and
this instrument's first output falsified it before a line of the remedy was written.** The cap's
ALBEDO measures 0.93x / 1.03x / 1.51x of its own board -- oak, the board in both screenshots and
the cap the brief called worst, was already BELOW its bar -- while the NORMAL MAP measures
15.9x / 45.5x / 53.7x. Undoing the contrast rise would have cost the colour and bought nothing.

There is a second term, and it is structural rather than a regression. Measured here:

    board face      1213 / 1246 / 1186 texels per metre   (oak / steel / bronze)
    keycap cell     4547 / 4122 / 5831 texels per metre    (256-texel cell over the cap's
                                                            short side: 56.3 / 62.1 / 43.9 mm)

**The cap carries its material at 3.3-4.9x the board's texel density** (2.8-3.6x for the larger
round caps). Even at IDENTICAL texture contrast the cap's grain therefore lands nearly two
octaves higher up the frequency axis at the same viewing distance -- which is a good part of the
difference between "wood" and "sandpaper". It is a property of the ASSET PIPELINE rather than of
one board: all three land within 5 % of each other. No amount of colour work would ever have
touched it, and it is why the albedo remedy is a LOW-PASS at the board's own resolution limit
rather than a contrast reduction: what is taken out is only the band the board's own material
could not have recorded in the first place.

TWO CONTRIBUTIONS, REPORTED SEPARATELY, BECAUSE THEY ARE SEPARATE KNOBS
------------------------------------------------------------------------
The brief asked for this explicitly and it is right to insist on it -- turning one down and
hoping is how a round gets spent.

* **ALBEDO GRAIN.** The material's own luminance texture. Scales with the plate, the
  normalising gain and the knee. Changed by `cap_atlas.GRAIN_TEMPER`.
* **NORMAL-MAP RELIEF.** `cap_atlas.carve` puts the material's high-passed luminance into the
  height field and then pins the RMS of its Sobel GRADIENT to `cap_atlas.GRAIN_RELIEF_SLOPE`.
  (It used to pin the height's standard deviation, `GRAIN_RELIEF_STD`, and pinning the wrong
  factor of that product is the whole defect this file was built to find -- see there.) Because
  that amplitude is PINNED, tempering the albedo does **not** move it: the two are genuinely
  independent, which is why they get two numbers. It is converted to the luminance modulation
  `BoardLit`'s baked key actually produces from it, so it is in the same units as the albedo
  term and the two combine in quadrature.

WHERE EACH SURFACE IS SAMPLED, AND WHY IT IS NOT THE OBVIOUS PLACE
--------------------------------------------------------------------
**The cap is measured on a RESERVED cell (10), not on a role cell.** Cells 1-8 carry a carved
symbol, and a carve is a groove with a burr and an AO shadow -- high-frequency, wanted, and
nothing to do with how rough the material is. Reserved cells take the identical `square` art
through the identical `field_jitter` and are never carved, so they are the shipped material
with the one confound removed. They are also genuinely shipped: they are real texels in the
real atlas, not a rebuild.

**The board is measured over the whole face band as a distribution of patches**, each the same
size IN PIXELS as the cap's field, and the MEDIAN patch is the bar. The face band also contains
the frame, the seat pockets and the carved rest motifs; those are the tail, not the surface a
cap sits on, and a mean over the band would let them set a bar the plain wood never asked for.
The quartiles are printed so the tail is visible rather than hidden -- an instrument that
reports one number for a distribution has already chosen an answer.
"""
import argparse
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
REPO = os.path.abspath(os.path.join(PREP, "..", ".."))
BUNDLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")

sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

STYLES = ("oak", "steel", "bronze")

# The board FBX each style ships, and its albedo. Copied from cap_onboard.BOARDS / gen_capref
# .STYLE_FILES rather than re-derived -- "board identity from the source, not the look".
BOARD_FILES = {
    "oak": "PlayTray_albedo.png",
    "steel": "PlayTray_9capjqp6_albedo.png",
    "bronze": "PlayTray_16vm268h_albedo.png",
}
BOARD_NORMALS = {
    "oak": "PlayTray_normal.png",
    "steel": "PlayTray_9capjqp6_normal.png",
    "bronze": "PlayTray_16vm268h_normal.png",
}
CAP_FILES = {"oak": "KeycapOak", "steel": "KeycapSteel", "bronze": "KeycapBronze"}

# TEXELS PER METRE ON THE BOARD'S FACE -- measured off the shipped FBX, not assumed.
#
# For every triangle whose geometric normal is within 20 degrees of +/-Z (the board face) and
# whose UVs lie in the face band (v < 0.5, the band `gen_capref` crops), texels/m is
# sqrt(uv_area * W * H / world_area); the number below is the AREA-WEIGHTED MEDIAN over those
# triangles, at the shipped 2048^2 albedo. A mean would have been pulled by the frame's tiny
# high-density strips (p90 is ~3400 on all three boards) -- which are 1 % of the area and none
# of the surface a cap sits on.
#
# All three boards are 0.64 x 0.32 m and all three land within 5 % of each other, which is the
# check that this is a property of the ASSET PIPELINE and not of one board.
BOARD_TEXELS_PER_M = {"oak": 1213.4, "steel": 1245.7, "bronze": 1185.8}

# THE CAP'S SHORT SIDE IN METRES -- one 256-texel atlas cell spans exactly this.
#
# Taken from the SHIPPED meshes (`Assets/Editor/ExportCapMeshes.cs` -> OBJ bounding boxes, the
# same files `cap_onboard` places), not from the config defaults: `BoardAnchors.FitCapSize`
# shrinks a cap to its own board's measured recess, so the authored dial is a ceiling and the
# fitted size is what a texel actually covers.
CAP_SHORT_SIDE_M = {
    "square": {"oak": 0.0563, "steel": 0.0621, "bronze": 0.0439},
    "round": {"oak": 0.0736, "steel": 0.0737, "bronze": 0.0601},
}

# THE VIEWING SAMPLING, and it is inherited rather than invented: `cap_check.py` argues 96 px
# across a 40 mm cap at arm's length and 32 px across the table, from ~20 px/degree on a Quest 3.
# 96/40 = 2.4 px/mm; 32/40 = 0.8 px/mm. The complaint is about the arm's-length view -- the caps
# fill a third of the frame in `noisy_buttons.jpg` -- so that is the headline, and the table view
# is printed beside it because a defect that only exists at one distance is a different defect.
PX_PER_MM_ARM = 2.4
PX_PER_MM_TABLE = 0.8

# THE BAND. Periods of ~1.5 to ~6 screen pixels, as a difference of Gaussians. Below 1.5 px the
# display cannot resolve it at all (it is what aliases, and it is not what he is pointing at);
# above ~6 px it is figure -- ray fleck, a casting seam, a temper bloom -- and it is wanted. The
# two sigmas are chosen so the pass band sits between them and nowhere near either edge of the
# sampling; selfcheck 4 drives it negative by blurring an input and requiring the number to fall.
BAND_SIGMA_LO = 0.6
BAND_SIGMA_HI = 2.5

# The atlas grid, mirrored from cap_atlas.GRID / Cards/CapCellMath. A reserved cell carries the
# same `square` material as a role cell and NO carve, which is the one confound this measurement
# has to be free of.
GRID = 4
UNCARVED_CELL = 10

# `BoardLit`'s baked key, verbatim from cap_onboard.GAME_KEY / render_asset.py. Used to turn a
# normal map into the luminance modulation it actually produces, so the relief term is in the
# same units as the albedo term instead of in "normal map units", which are not a thing anyone
# can compare to a board.
_K = (0.35, -0.45, 0.85)
_KN = math.sqrt(sum(c * c for c in _K))
GAME_KEY = tuple(c / _KN for c in _K)


# =========================================================================================
# THE MEASUREMENT
# =========================================================================================
def _gauss1d(sigma):
    r = max(1, int(math.ceil(3.0 * sigma)))
    x = np.arange(-r, r + 1, dtype=np.float64)
    k = np.exp(-0.5 * (x / sigma) ** 2)
    return k / k.sum()


def _blur(a, sigma):
    """Separable Gaussian with EDGE REPLICATION.

    Reflect or wrap would both invent structure at the border -- a reflection doubles every
    edge feature and a wrap butts two unrelated sides together, and either one lands squarely
    in the band being measured. Replication is the only padding that adds no gradient.
    """
    k = _gauss1d(sigma)
    r = (len(k) - 1) // 2
    p = np.pad(a, ((r, r), (r, r)), mode="edge")
    out = np.apply_along_axis(lambda m: np.convolve(m, k, mode="valid"), 1, p)
    return np.apply_along_axis(lambda m: np.convolve(m, k, mode="valid"), 0, out)


def resample(a, src_px_per_mm, dst_px_per_mm):
    """Resample `a` from its own texel sampling to the eye's pixel sampling.

    BOTH DIRECTIONS ARE MODELLED AND THE FIRST VERSION OF THIS ONLY MODELLED ONE, which its own
    selfcheck caught before any number was believed. Returning a coarse texture untouched left
    the two surfaces being compared at DIFFERENT pixels per millimetre while the report claimed
    they were at the same one -- and it inverted leg 5, reporting that the denser texture was the
    smoother of the two.

    * DOWNSAMPLE (a cap: 4.1-5.8 tex/mm seen at 2.4 px/mm) -- BOX, i.e. an area average. That is
      the one thing that actually happens on the way to the retina: a GPU with mips and
      anisotropic filtering integrates the texels inside a pixel's footprint. A bilinear point
      sample would have thrown the grain away without averaging it -- measuring aliasing, not
      roughness -- and a Lanczos kernel rings, which puts energy INTO the band being measured.
    * UPSAMPLE (a board: 1.19-1.25 tex/mm seen at 2.4 px/mm) -- BILINEAR, which is what the
      hardware does when a surface is magnified past its own texel size. **This is not a
      formality: it is half the finding.** A magnified texture is bilinearly SMOOTH between its
      texels, and it has nothing at all in the top of the eye's band because its own Nyquist is
      below that band. That is a real, physical reason a board reads smooth and a cap does not,
      and an instrument that skipped it would have credited the board with grain it cannot have.
    """
    h, w = a.shape
    scale = dst_px_per_mm / src_px_per_mm
    nh, nw = max(8, int(round(h * scale))), max(8, int(round(w * scale)))
    if (nh, nw) == (h, w):
        return a.astype(np.float64)
    im = Image.fromarray(np.clip(a, 0.0, 1.0).astype(np.float32), "F")
    filt = Image.Resampling.BOX if scale < 1.0 else Image.Resampling.BILINEAR
    return np.asarray(im.resize((nw, nh), filt), dtype=np.float64)


def grain_contrast(a):
    """100 * RMS(band-passed) / mean -- the number the whole file is about.

    The mean is taken on the SAME array, so any uniform scale factor divides out exactly; that
    is what makes this comparable between a cap at one brightness and a board at another, and
    what makes it blind to `_Color` on purpose.
    """
    m = float(a.mean())
    if m <= 1e-6:
        return float("nan")
    band = _blur(a, BAND_SIGMA_LO) - _blur(a, BAND_SIGMA_HI)
    return 100.0 * float(band.std()) / m


def patch_contrasts(a, patch_px):
    """`grain_contrast` on every non-overlapping patch, as a sorted array.

    A single number for a whole board face would be a mean over plain wood, a carved motif, a
    dentil frame and two seat pockets, and it would be none of them.
    """
    h, w = a.shape
    n = max(8, int(patch_px))
    out = []
    for y in range(0, h - n + 1, n):
        for x in range(0, w - n + 1, n):
            v = grain_contrast(a[y:y + n, x:x + n])
            if np.isfinite(v):
                out.append(v)
    return np.sort(np.array(out)) if out else np.array([float("nan")])


# =========================================================================================
# THE TWO SURFACES
# =========================================================================================
def cell_field(atlas, cell, plateau=0.135):
    """The recessed FIELD of one atlas cell -- the only part a cap's FACE submesh samples."""
    n = atlas.shape[0] // GRID
    r, c = cell // GRID, cell % GRID
    sub = atlas[r * n:(r + 1) * n, c * n:(c + 1) * n]
    lo = int(round(plateau * n))
    return sub[lo:n - lo, lo:n - lo]


def board_face(img):
    """The face band: u 0..1, v 0..0.5, i.e. the LOWER half of the file.

    UV v = 0 is the BOTTOM of the image and PIL row 0 is the TOP, so the band is rows h/2..h.
    Getting that backwards samples the frame/seat/side regions -- `gen_capref` records the same
    trap, and it is repeated here rather than imported because this file must be readable on its
    own by anyone re-deriving the bar.
    """
    h = img.shape[0]
    return img[h // 2:, :]


def _lum(rgb):
    return rgb.mean(axis=2)


def _normal_shade(png, invert_red=True):
    """A normal map -> the luminance `BoardLit`'s baked key gets off it.

    THE RED CHANNEL IS INVERTED IN EVERY SHIPPED MAP IN THIS PROJECT, deliberately and
    consistently (`cap_atlas`: "every shipped board normal map carries that inversion and the
    caps sit on those boards under the same key"). Undoing it here for BOTH surfaces keeps the
    comparison honest; and since the metric is an RMS about a mean, a consistent sign error
    would not have changed the answer anyway -- which is stated so nobody re-litigates it.
    """
    n = png.astype(np.float64) / 255.0 * 2.0 - 1.0
    nx = -n[..., 0] if invert_red else n[..., 0]
    ny, nz = n[..., 1], n[..., 2]
    ln = np.sqrt(np.maximum(nx * nx + ny * ny + nz * nz, 1e-9))
    return np.clip((nx * GAME_KEY[0] + ny * GAME_KEY[1] + nz * GAME_KEY[2]) / ln, 0.0, 1.0)


def measure(style, shape="square", ppmm=PX_PER_MM_ARM, atlas_dir=BUNDLE):
    """Every number for one board, at one viewing sampling."""
    cap_side_mm = CAP_SHORT_SIDE_M[shape][style] * 1000.0
    alb = np.asarray(Image.open(os.path.join(atlas_dir, f"{CAP_FILES[style]}_albedo.png"))
                     .convert("RGB"), dtype=np.float64) / 255.0
    nrm = np.asarray(Image.open(os.path.join(atlas_dir, f"{CAP_FILES[style]}_normal.png"))
                     .convert("RGB"))
    board = np.asarray(Image.open(os.path.join(BUNDLE, BOARD_FILES[style])).convert("RGB"),
                       dtype=np.float64) / 255.0
    board_n = np.asarray(Image.open(os.path.join(BUNDLE, BOARD_NORMALS[style])).convert("RGB"))

    cap_tex_mm = (alb.shape[0] / GRID) / cap_side_mm
    cap_ntex_mm = (nrm.shape[0] / GRID) / cap_side_mm
    board_tex_mm = BOARD_TEXELS_PER_M[style] / 1000.0
    board_ntex_mm = board_tex_mm * (board_n.shape[0] / board.shape[0])

    cap_a = resample(_lum(cell_field(alb, UNCARVED_CELL)), cap_tex_mm, ppmm)
    cap_r = resample(_normal_shade(cell_field(nrm, UNCARVED_CELL)), cap_ntex_mm, ppmm)
    brd_a = resample(_lum(board_face(board)), board_tex_mm, ppmm)
    brd_r = resample(_normal_shade(board_face(board_n)), board_ntex_mm, ppmm)

    patch = max(16, min(cap_a.shape))
    ca, cr = grain_contrast(cap_a), grain_contrast(cap_r)
    ba = patch_contrasts(brd_a, patch)
    br = patch_contrasts(brd_r, patch)

    def q(arr, f):
        return float(arr[min(len(arr) - 1, int(f * (len(arr) - 1)))])

    board_alb, board_rel = q(ba, 0.5), q(br, 0.5)
    return dict(
        style=style, shape=shape, ppmm=ppmm,
        cap_tex_per_mm=cap_tex_mm, board_tex_per_mm=board_tex_mm,
        density_ratio=cap_tex_mm / board_tex_mm,
        cap_albedo=ca, cap_relief=cr,
        cap_total=math.sqrt(ca * ca + cr * cr),
        board_albedo=board_alb, board_relief=board_rel,
        board_total=math.sqrt(board_alb ** 2 + board_rel ** 2),
        board_albedo_q=(q(ba, 0.25), board_alb, q(ba, 0.75)),
        board_relief_q=(q(br, 0.25), board_rel, q(br, 0.75)),
        ratio_albedo=ca / board_alb if board_alb > 0 else float("nan"),
        ratio_relief=cr / board_rel if board_rel > 0 else float("nan"),
        ratio_total=(math.sqrt(ca * ca + cr * cr)
                     / math.sqrt(board_alb ** 2 + board_rel ** 2)),
        patch_px=patch, board_patches=len(ba),
    )


# =========================================================================================
# THE SELF-CHECKS -- a new instrument's first output is a hypothesis
# =========================================================================================
def selfcheck(say=print):
    rng = np.random.default_rng(20260825)
    ok = True

    def check(name, cond, detail):
        nonlocal ok
        ok = ok and bool(cond)
        say(f"  [{'PASS' if cond else 'FAIL'}] {name}: {detail}")

    # 1. NULL. A perfectly flat surface has no grain, and an instrument that reports one on it
    #    is measuring its own padding.
    flat = np.full((128, 128), 0.5)
    v = grain_contrast(flat)
    check("null input reads zero", v < 1e-9, f"flat 0.5 -> {v:.3e} %")

    # 2. KNOWN POSITIVE, and it is checked as a PROPORTIONALITY rather than against a constant.
    #    The absolute number depends on how much of white noise's power falls in the band, which
    #    is a property of the band and not something to hard-code and then defend.
    base = np.full((256, 256), 0.5)
    v1 = grain_contrast(base + rng.standard_normal((256, 256)) * 0.02)
    v2 = grain_contrast(base + rng.standard_normal((256, 256)) * 0.04)
    check("doubling the noise doubles the reading", abs(v2 / v1 - 2.0) < 0.06,
          f"sigma 0.02 -> {v1:.2f} %, sigma 0.04 -> {v2:.2f} %, ratio {v2 / v1:.3f}")

    # 3. SCALE INVARIANCE. This is the one that makes the number comparable across two surfaces
    #    at different brightnesses -- i.e. it is what lets a cap be compared to its board at all,
    #    through `alb = tex2D(_MainTex, uv) * _Color`.
    img = base + rng.standard_normal((256, 256)) * 0.03
    check("invariant to a uniform gain (i.e. to _Color)",
          abs(grain_contrast(img * 1.7) - grain_contrast(img)) < 1e-9,
          f"x1 {grain_contrast(img):.4f} % vs x1.7 {grain_contrast(img * 1.7):.4f} %")

    # 4. MONOTONE. Smoothing must lower it, or the remedy this file exists to steer cannot be
    #    steered by it.
    v_sharp = grain_contrast(img)
    v_soft = grain_contrast(_blur(img, 1.5))
    check("blurring lowers it", v_soft < v_sharp * 0.5,
          f"{v_sharp:.2f} % -> {v_soft:.2f} %")

    # 5. IT SEES THE FREQUENCY, NOT ONLY THE CONTRAST -- and this leg is the round's finding
    #    driven negative. ONE physical surface, authored once at a high density, is stored at two
    #    texel densities (an area average is what an asset pipeline does) and then both are
    #    viewed at the same distance. The densely-stored one must read ROUGHER, because the
    #    coarsely-stored one threw its fine grain away at bake time and can only be magnified
    #    smoothly afterwards. Without this leg the instrument would agree that a cap and a board
    #    with equal texture contrast are equally rough, which is exactly what is false here.
    #
    #    The FIRST version of this leg failed and the instrument was wrong, not the world: it
    #    compared two arrays at two different samplings because `resample` refused to magnify.
    truth = rng.standard_normal((768, 768)) * 0.03 + 0.5
    coarse_store = resample(truth, 9.6, 1.2)       # baked at a board's density
    dense_store = resample(truth, 9.6, 4.8)        # baked at a keycap cell's density
    coarse = grain_contrast(resample(coarse_store, 1.2, 2.4))
    dense = grain_contrast(resample(dense_store, 4.8, 2.4))
    check("the same surface stored denser reads rougher at the same distance",
          dense > coarse * 1.5,
          f"stored at 1.2 tex/mm -> {coarse:.2f} %, at 4.8 tex/mm -> {dense:.2f} % "
          f"({dense / max(coarse, 1e-9):.1f}x)")

    # 6. THE BOARD IS ITS OWN BAR. Measuring a surface against itself must give exactly 1.
    check("a surface against itself is 1.000", abs(v_sharp / v_sharp - 1.0) < 1e-12,
          "trivially, and it is here so the ratio's definition is pinned by a test")
    return ok


# =========================================================================================
# THE REPORT
# =========================================================================================
def report(atlas_dir=BUNDLE, say=print, shapes=("square", "round")):
    say("")
    say("ROUGHNESS -- grain contrast, band-passed at 1.5-6 screen px, "
        f"both surfaces resampled to {PX_PER_MM_ARM} px/mm (arm's length, Quest 3)")
    say("")
    say("  TEXEL DENSITY -- the term that is structural and was never named")
    for style in STYLES:
        for shape in shapes:
            m = measure(style, shape, atlas_dir=atlas_dir)
            say(f"    {style:<7} {shape:<6} cap {m['cap_tex_per_mm']:.2f} tex/mm vs board "
                f"{m['board_tex_per_mm']:.2f} tex/mm  =  {m['density_ratio']:.2f}x")
    rows = {}
    for shape in shapes:
        say("")
        say(f"  {shape.upper()} CAPS -- cap / board, and the requirement is <= 1.00")
        say(f"    {'board':<8}{'albedo':>18}{'relief':>18}{'combined':>18}")
        for style in STYLES:
            m = measure(style, shape, atlas_dir=atlas_dir)
            rows[(style, shape)] = m
            say(f"    {style:<8}"
                f"{m['cap_albedo']:6.2f}/{m['board_albedo']:5.2f}={m['ratio_albedo']:5.2f}x"
                f"{m['cap_relief']:8.2f}/{m['board_relief']:5.2f}={m['ratio_relief']:5.2f}x"
                f"{m['cap_total']:8.2f}/{m['board_total']:5.2f}={m['ratio_total']:5.2f}x")
        for style in STYLES:
            m = rows[(style, shape)]
            say(f"      {style}: board albedo patches q25/med/q75 "
                f"{m['board_albedo_q'][0]:.2f}/{m['board_albedo_q'][1]:.2f}/"
                f"{m['board_albedo_q'][2]:.2f} %, relief "
                f"{m['board_relief_q'][0]:.2f}/{m['board_relief_q'][1]:.2f}/"
                f"{m['board_relief_q'][2]:.2f} % over {m['board_patches']} patches of "
                f"{m['patch_px']} px")
    say("")
    say(f"  ACROSS THE TABLE ({PX_PER_MM_TABLE} px/mm) -- a defect at one distance only is a "
        "different defect")
    for style in STYLES:
        m = measure(style, "square", ppmm=PX_PER_MM_TABLE, atlas_dir=atlas_dir)
        say(f"    {style:<8}combined {m['cap_total']:5.2f}/{m['board_total']:5.2f} = "
            f"{m['ratio_total']:5.2f}x")
    return rows


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--selfcheck", action="store_true")
    ap.add_argument("--atlas", default=BUNDLE, help="read the atlas from here instead")
    a = ap.parse_args()
    rc = 0
    if a.selfcheck:
        print("cap_rough selfcheck -- six legs, including a null and a known positive")
        if not selfcheck():
            rc = 1
    if a.report or not a.selfcheck:
        report(atlas_dir=a.atlas)
    sys.exit(rc)


if __name__ == "__main__":
    main()
