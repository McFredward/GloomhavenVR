#!/usr/bin/env python3
"""Build the three PER-STYLE KEYCAP ATLASES the mod's board buttons wear.

    unity/GloomhavenVR.Assets/Assets/Bundle/Table/KeycapOak_albedo.png    (+ _normal)
    unity/GloomhavenVR.Assets/Assets/Bundle/Table/KeycapSteel_albedo.png  (+ _normal)
    unity/GloomhavenVR.Assets/Assets/Bundle/Table/KeycapBronze_albedo.png (+ _normal)

WHAT PROBLEM THIS SOLVES
------------------------
All three boards' keycaps shared ONE material -- `KeycapGrain_albedo.png` bound in
`Cards/PlayTray.NewKeycapMaterial` -- which is exactly why an oak board, a steel board and
a bronze board all wore identical keys (user, 2026-08-25: "Pro Board soll es auch ein
anderes passendes Aussehen der buttons sein, das zu dem board und seinem Aussehen passt").
And the caps carried no symbol at all, only text.

Both are fixed by ONE texture per style rather than by one texture per style PER ROLE. The
atlas is a grid of cells; a cap's material picks its cell with `mainTextureScale/Offset`,
which BoardLit already honours (`o.uv = TRANSFORM_TEX(v.uv, _MainTex)`, and _BumpMap /
_MRSMap sample that same `i.uv`). So a role change is two floats on a material instance,
not a second texture, and the OWNER and the PEER MIRROR get it from the same call
(`PlayTray.NewKeycapMaterial`) rather than from two agreeing copies of a rule.

WHY THE SYMBOL IS CARVED IN AND NOT RAISED -- measured, not stylistic
--------------------------------------------------------------------
The board-texture round established that on this shader "the specular is a BEVEL term, not
a surface term": with BoardLit's baked key and a viewer in front of the board the
half-vector sits ~32 degrees off a flat face, so the open plate gets essentially no
highlight (`_SpecStrength` 0 vs 0.85 moves the flat-on mean by 0.001). A RAISED symbol
reads by its highlight, and the highlight is the one thing this surface cannot deliver. A
RECESSED symbol reads by AMBIENT OCCLUSION, which is baked into the albedo and is
view-independent, so it reads at every angle and every distance. It also echoes the board:
the rest-pad motifs and the frame ornament are carved, not embossed.

WHY THE GENERATED IMAGE NEVER BECOMES THE SYMBOL'S SHADING
----------------------------------------------------------
Inherited verbatim from the board pipeline (see ../README.md and ../tex_symbols.py): a
generated image is a BINARY STENCIL, the bevel is rebuilt from an exact distance transform,
and the motif is carved into a procedural material. That is the whole reason the result
stops reading as AI. This file reuses `tex_symbols._cell_binary` / `_resize_mask` /
`signed_distance` rather than re-implementing them, so the thresholding, the morphology and
the sub-texel SDF resize are the same code the board's own motifs went through -- including
the two bugs already found and fixed in it (the transposed cell map and the Otsu units).

THE TWO REST SYMBOLS ARE NOT GENERATED AT ALL
---------------------------------------------
`rest_short` (crescent-and-embers) and `rest_long` (spoked wheel) already exist as binary
masks in `../out/motifs_a/` and `../out/motifs_b/` -- they are the motifs CARVED INTO THE
BOARD'S OWN REST PADS. The design asks the cap symbol to "echo the pad it sits beside", and
reusing the pad's own stencil is not an echo, it is the same shape. It also costs no image.
The sheet each style takes them from is the sheet that style's BOARD took them from
(`tex_atlas.STYLE_MOTIFS`: oak and steel from sheet_a, bronze from sheet_b), so a bronze
cap carries the plaited crescent its bronze pad carries and an oak cap carries the guild
one.

THE GENERATED INPUTS -- four images, all accepted first try, none re-rolled
--------------------------------------------------------------------------
    ../out/keycap_symbols_sheet.png   1024^2, 3x3, the five ROLE symbols + spares
    ../out/keycap_plate_oak.png       1024^2, a flat shadowless material swatch
    ../out/keycap_plate_steel.png     1024^2
    ../out/keycap_plate_bronze.png    1024^2

The three plates were generated with that style's OWN board albedo face band passed as a
reference image (`gen_capref.py`), so the cap material is anchored to the board's material
rather than to a second verbal description of it. `../out/` is gitignored, so none of them
are in any diff; the prompts are in the commit that added this file.

The 3:2 trap does not apply here and that is checked rather than assumed: every request was
1:1 and every image came back 1024x1024 exactly (`--report` prints the delivered sizes).
"""
import argparse
import json
import os
import sys
import zlib

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, PREP)
sys.path.insert(0, HERE)

import tex_common as T          # noqa: E402
import tex_symbols as S         # noqa: E402
import cap_object as O          # noqa: E402  (round 3: the registered object cells)

REPO = os.path.dirname(os.path.dirname(PREP))
BUNDLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")

# ---------------------------------------------------------------------------
# THE CELL MAP -- MIRRORED IN C# and load-bearing on both sides of the wire
# ---------------------------------------------------------------------------
# `Cards/CapSymbols.cs` carries the same table as `CapRole` + `CapSymbols.Cell`, and its
# doc comment names this file. If one moves and the other does not, every cap wears the
# wrong symbol -- on the owner's board AND on every peer's mirror of it, because both sides
# resolve the cell through the same `PlayTray.NewKeycapMaterial`.
#
# 4 x 4 so the grid is power-of-two in both axes at every cell size: Unity's NPOT handling
# is fine on Win64 but a POT atlas keeps every compression format and every mip level
# available with no special case, and seven spare cells cost nothing (they are plain
# material, so they compress to almost nothing).
GRID = (4, 4)                   # cols, rows

# index -> (role name, symbol source, layout)
#   symbol source: ("sheet", cell)      one cell of ../out/keycap_symbols_sheet.png
#                  ("motif", name)      ../out/motifs_<sheet>/<name>_mask.png, per style
#                  None                 no symbol; plain material
#   layout:        "text"    the cap ALSO carries live game text -> symbol in the upper band
#                  "solo"    the symbol is the whole face -> centred and large
#                  "plain"   no symbol
CELLS = [
    dict(idx=0, role="Plain",       src=None,              layout="plain"),
    dict(idx=1, role="Confirm",     src=("sheet", "A1"),   layout="text"),
    dict(idx=2, role="Undo",        src=("sheet", "A2"),   layout="text"),
    dict(idx=3, role="Skip",        src=("sheet", "A3"),   layout="text"),
    dict(idx=4, role="ItemUse",     src=("sheet", "C3"),   layout="text"),
    dict(idx=5, role="ShortRest",   src=("motif", "rest_short"), layout="solo"),
    dict(idx=6, role="LongRest",    src=("motif", "rest_long"),  layout="solo"),
    dict(idx=7, role="FixedPinned", src=("sheet", "B2"),   layout="solo"),
    dict(idx=8, role="FixedFollow", src=("sheet", "B3"),   layout="solo"),
]

# The sheet's own cell map, in the same "letter is the ROW" convention `tex_symbols.cell_box`
# documents (A1 top-left, A3 top-RIGHT, C1 bottom-left). It is written out here because the
# prompt is the only thing the generator saw and the prompt enumerates the nine motifs as
# three rows read left to right.
SHEET_CELLS = {
    "A1": "confirm (bold check mark)",
    "A2": "undo (counter-clockwise return arrow)",
    "A3": "skip (two triangles against an upright bar)",
    "B1": "item_use ALTERNATE (apothecary phial with rays)",
    "B2": "fixed_pinned (anchor)",
    "B3": "fixed_follow (two footprints)",
    "C1": "confirm ALTERNATE (check mark in a ring)",
    "C2": "undo ALTERNATE (plain arc arrow)",
    "C3": "item_use (open hand with rays)",
}

# Which delivered board sheet each style's REST motifs come from -- copied, not invented,
# from tex_atlas.STYLE_MOTIFS. Oak and steel took the medieval guild woodcut (sheet_a);
# bronze took the Norse/Celtic interlace (sheet_b) because interlace is a metalwork idiom.
STYLE_MOTIF_SHEET = {"oak": "a", "steel": "a", "bronze": "b"}

STYLE_FILES = {"oak": "KeycapOak", "steel": "KeycapSteel", "bronze": "KeycapBronze"}

# ---------------------------------------------------------------------------
# THE FACE BUDGET -- why the symbol boxes are where they are
# ---------------------------------------------------------------------------
# The cap's UVs are PLANAR over its whole footprint (`CardMesh.BuildBeveledKeycap`:
# `Uv(p) = (p.x/width + 0.5, p.y/height + 0.5)`, applied to EVERY face). So the flat
# RECESSED FIELD -- the only part a symbol may live on -- is not the whole cell: it is
# inset by the cap's whole bezel on every side.
#
# ROUND 2 (2026-08-25) RE-CUT THE PROFILE, and these numbers moved with it. The cap is no
# longer "flat plateau + one 7 mm chamfer": it is a SIGNET PLATE -- outer chamfer, flat rim
# land, inner chamfer stepping DOWN, recessed field (`CardMesh.BuildBeveledKeycap`, and the
# fractions live on `CardMesh.CapBezel*`). The whole bezel is 0.135 of the cap's SHORT side:
#     outer chamfer 0.060 + rim land 0.045 + inner step 0.030 = 0.135
# The short side is the HEIGHT on all three fitted caps (Oak 63.0x56.3, Steel 63.0x62.1,
# Bronze 53.2x43.9 mm), so the vertical field band is EXACTLY [0.135, 0.865] on all three and
# needs no "tightest board" argument at all. Horizontally the bezel is the same absolute
# width, so as a fraction of the WIDTH it is 0.111 (Bronze), 0.121 (Oak), 0.133 (Steel) --
# and the tightest one decides, exactly as `_seatMinHalf` decides the cap size. 0.135 covers
# all six numbers, so ONE band is correct on both axes of every board.
#
# WHY THE OLD 7 mm CHAMFER HAD TO GO: it is 0.159 of Bronze's short side by itself, and the
# field it left could not hold TWO LINES of caption at the readability floor -- which is the
# whole of the truncation defect the user reported ("AUSWAHL BEEN").
#
# PIL row 0 is the TOP of the image and the cap's v = 1 is the TOP of the cap (v grows with
# +Y), so image-top IS cap-top and no flip is needed anywhere in this file.
PLATEAU_LO, PLATEAU_HI = 0.135, 0.865

# "solo" -- the symbol is the whole face.
SOLO_SIZE = 0.56                # fraction of the cell side
SOLO_CY = 0.50                  # centre, fraction of the cell side from the TOP

# "text" -- the cap keeps its LIVE GAME TEXT (`CardsGameApi.PickDialogOptionLabel` reads it
# off the option button a 2D player would click, so it changes every pick and with the
# language) and the symbol sits above it. These two numbers and the label box in
# `Cards/CapSymbols.cs` are one layout: the symbol occupies the upper band and the TMP label
# is moved down into the lower one.
#
# THE SPLIT OF THE FIELD (v measured up from the cap's bottom edge; field = [0.135, 0.865]):
#     caption band  0.135 .. 0.585   (0.45 of the cap height -- 0.62 of the field)
#     gap           0.585 .. 0.615
#     symbol band   0.615 .. 0.865   (0.25 of the cap height)
# The caption band GREW from 0.36 to 0.45 of the cap height and the symbol SHRANK from 0.32
# to 0.235 of the cell, because a caption cut in half is a defect and a symbol 12 % smaller is
# a trade. On Bronze -- the smallest cap, and the one in the user's screenshot -- 0.45 x 43.9
# mm = 19.8 mm of caption band, which holds two lines at font size 0.083 (1.2 em line advance)
# where the old 15.8 mm band could not hold two at the 0.080 floor. That single line-count is
# the whole difference between "Auswahl beenden" and "AUSWAHL BEEN".
TEXT_SIZE = 0.235
TEXT_CY = 0.26                  # from the TOP of the cell
# The label box `Cards/CapSymbols.cs` gives the TMP on a "text" cap, as fractions of the cap
# footprint. The two must be read together -- a change here that is not made there puts a
# caption through a carved symbol. The wire suite asserts the pair against these very numbers.
TEXT_LABEL_CENTRE_DY = -0.14    # of the cap HEIGHT, from the cap centre
TEXT_LABEL_BOX = (0.73, 0.45)   # of the cap width / height

# ---------------------------------------------------------------------------
# THE CARVE
# ---------------------------------------------------------------------------
# All in fractions of the CELL SIDE so the recipe is resolution-independent: doubling
# --cell must change the sampling, never the look.
RIM_FRAC = 0.014                # chamfer width of the cut wall
DEPTH = 1.0                     # groove depth in height units (normalised, see NORMAL_STRENGTH)
LIP_FRAC = 0.008                # the raised burr the chisel throws up outside the cut
LIP_H = 0.22
AO_GAIN = 1.15                  # how hard the cavity darkens the groove
AO_GAMMA = 1.0
FLOOR_DARKEN = 0.30             # extra darkening at the very bottom of the cut (dirt/shadow)
NORMAL_STRENGTH = 26.0          # height units -> normal slope; tuned against the cell side

# The micro-relief of the MATERIAL itself, derived from the plate's own luminance, in the
# same height units as the groove (DEPTH = 1). Small on purpose: the plate already carries
# its grain in the albedo, and this only has to keep the surface from reading as glass.
GRAIN_RELIEF = 0.35
# The micro-relief's HEIGHT STANDARD DEVIATION, in the same units as DEPTH = 1. This is the
# amplitude the ModBuild 286 chain actually produced, measured rather than chosen: 24 cells
# (three boards x eight crops) through round 2's own `normalise_plate` -> `material_cell` ->
# `(lum - lum.mean()) * GRAIN_RELIEF`, mean 0.02376 (oak 0.0158, steel 0.0264, bronze 0.0290).
# Round 3 pins it instead of re-deriving it from a gain, because the registered art carries
# roughly twice the luminance contrast of a swatch and the same GAIN would have doubled the
# bump for a reason that has nothing to do with how rough the material is.
GRAIN_RELIEF_STD = 0.02376

# ---------------------------------------------------------------------------
# THE LEVEL BELONGS TO THE STATE PALETTE, NOT TO THE MATERIAL
# ---------------------------------------------------------------------------
# THE REGRESSION THIS EXISTS TO UNDO, measured rather than noticed:
#
#   BoardLit computes `alb = tex2D(_MainTex, uv) * _Color`, so a keycap's texture is a
#   MODULATOR of the state colour, not a colour in its own right. The texture it replaces --
#   `KeycapGrain_albedo.png` -- is a near-white GREYSCALE grain with mean 0.837, i.e. a
#   modulator that passes the state colour through almost untouched. These generated plates
#   are photographs of materials and their means are 0.27-0.42. Dropped in unchanged, the
#   rendered cap face goes from 1.75x the luminance of the mod-drawn WELL it sits in to
#   0.95x (oak), 0.85x (bronze) and 0.57x (STEEL) -- from clearly proud of its own recess to
#   level with it or darker.
#
#   That is precisely the "invisible button, only the text still visible" shape the seat
#   floor (`WorldUI.ButtonTuning.SeatedCapColor`) was written for after a hardware report --
#   AND THE SEAT FLOOR CANNOT SEE IT. It floors the material's `_Color`, and `_Color` has not
#   moved: the darkening arrived in the TEXTURE, a term that did not exist when that guard
#   was written. The render sheet showed it as "the steel caps look nearly black"; the number
#   is what turned that into a cause.
#
# THE FIX IS THE RIGHT DECOMPOSITION, not a brightness tweak: what a BOARD contributes is its
# material's COLOUR CAST and its STRUCTURE, and what the state palette plus the seat floor
# contribute is the LEVEL. So each plate is re-based to the shipped grain's mean before
# anything is carved into it. The gain is UNIFORM across R, G and B, so the hue the model
# delivered is preserved exactly; only the top end is compressed, so a bright grain line does
# not clip flat and take the wood structure with it.
GRAIN_TARGET_LUM = 0.837        # measured on the shipped KeycapGrain_albedo.png
KNEE = 0.80                     # below this the gain is linear; above it, a soft roll-off
NORM_ITERS = 6                  # the knee moves the mean, so the gain is SOLVED, not guessed

# Mip bleed guard. Neighbouring cells are the same material, so material bleed is harmless
# and is deliberately not fought; only a SYMBOL crossing into a neighbour would be a defect,
# and the layout above keeps every symbol at least 0.16 of a cell from its border.
MARGIN_NOTE = ("symbols are kept >= 0.16 cell from every cell border, so the first four mip "
               "levels cannot carry a symbol into a neighbouring cell")


def load_sheet_masks(sheet_path):
    """Every cell of the generated symbol sheet, as a trimmed square binary stencil.

    Straight through `tex_symbols._cell_binary`, which is the board pipeline's own reader:
    crop -> guarded auto-level -> Otsu -> invert test -> area-based speckle/pinhole clean ->
    trim -> squarify. Nothing here re-implements any of it.
    """
    src = np.asarray(Image.open(sheet_path).convert("L"), dtype=np.float64) / 255.0
    out, notes = {}, {}
    for cell in SHEET_CELLS:
        mask, note = S._cell_binary(src, cell)
        out[cell] = mask
        notes[cell] = note
    return out, notes


def load_motif_mask(name, sheet_letter, motif_root):
    path = os.path.join(motif_root, f"motifs_{sheet_letter}", f"{name}{S.MASK_SUFFIX}")
    if not os.path.isfile(path):
        return None
    return np.asarray(Image.open(path).convert("L"), dtype=np.uint8) > 127


def place(mask, cell_px, size_frac, cy_frac):
    """Antialiased coverage of `mask` placed in a cell-sized square.

    The resize goes through `tex_symbols._resize_mask`, which reconstructs the edge from the
    stencil's SIGNED DISTANCE FIELD and supersamples it down -- so a motif composited LARGER
    than its source cell does not inherit the source's pixel staircase. That fix was made
    for the board's rosettes and it matters more here, not less: a 224 px `rest_short`
    stencil goes to a 143 px cap symbol and back up again at close zoom.
    """
    side = max(2, int(round(size_frac * cell_px)))
    cov_small = S._resize_mask(mask, side)
    cov = np.zeros((cell_px, cell_px), dtype=np.float64)
    cy = int(round(cy_frac * cell_px))
    y0 = cy - side // 2
    x0 = (cell_px - side) // 2
    y0 = max(0, min(cell_px - side, y0))
    x0 = max(0, min(cell_px - side, x0))
    cov[y0:y0 + side, x0:x0 + side] = cov_small
    return cov


def min_feature_px(mask):
    """The narrowest limb of a stencil, in source texels: twice the largest inscribed
    radius is the WIDEST, so the useful number is the median inward distance over the ink,
    doubled. A symbol whose median limb is thinner than a couple of texels at cap scale is
    one that will disappear into the material, and this is the number that decides between
    two candidate cells instead of an opinion about which looks nicer."""
    m = np.asarray(mask, dtype=bool)
    if not m.any():
        return 0.0
    d = T.edt(~m)
    return float(np.median(d[m]) * 2.0)


def carve(mat, cov, cell_px, band=None):
    """Cut `cov` into `mat`. Returns (albedo RGB in [0,1], height field).

    `band` is the per-texel distance-to-outline of a REGISTERED cell (`cap_object._band_coord`)
    and is what lets the micro-relief tell a painted band from a real one -- see below.
    """
    solid = cov >= 0.5
    rim = max(1.0, RIM_FRAC * cell_px)
    lip_w = max(1.0, LIP_FRAC * cell_px)

    if solid.any():
        d_in = T.edt(~solid)                 # 0 at the boundary, growing inward
        d_out = T.edt(solid)                 # 0 at the boundary, growing outward
        floor = T.smoothstep(0.0, rim, d_in)
        height = -DEPTH * floor * cov
        # The burr: a narrow raised bead just outside the cut, the way a chisel lifts the
        # material it displaces. It is what stops the groove reading as a printed shadow.
        height += LIP_H * np.exp(-(d_out / lip_w) ** 2) * (1.0 - cov)
    else:
        height = np.zeros((cell_px, cell_px), dtype=np.float64)
        floor = np.zeros_like(height)

    # The MATERIAL's own micro-relief, from its luminance. Added to the height so the
    # normal map carries grain as well as the cut.
    #
    # IT IS THE HIGH-PASS, AND ROUND 3 IS WHY. This used to be `lum - lum.mean()`, i.e. the
    # WHOLE luminance deviation. On a swatch that is the same thing -- a swatch has almost no
    # low-frequency content, which is exactly what "einheitlich" meant. On a REGISTERED plate it
    # is not: the rim land is a broad bright band, and feeding that straight into the height
    # field builds a second, painted bevel in the normal map sitting on top of the real 45 deg
    # geometry the mesh already has. That is the doubled edge, authored by the pipeline rather
    # than by the model. A low-frequency albedo band is a difference in MATERIAL, not in HEIGHT;
    # only the grain is relief.
    #
    # AND A HIGH-PASS IS NOT ENOUGH, WHICH IS THE HALF THAT HAD TO BE MEASURED TWICE. A band
    # EDGE is a step, and a step has energy at every frequency -- so high-passing at cell/16
    # and then again at cell/48 left the rim's and the chamfer's edges in the height field
    # both times, and the rendered normal map showed exactly the doubled bevel this was
    # supposed to prevent (`_scratch/oak_n1`: four bright/dark ridge pairs tracking the four
    # band boundaries). The fix is to remove the thing by NAME rather than by frequency:
    # subtract the cell's own RADIAL BAND PROFILE, which is by construction everything that is
    # a pure function of distance-to-outline, and take the relief from what is left. Whatever
    # the registration painted cannot survive that subtraction; whatever the MATERIAL did is
    # untouched by it.
    #
    # AND THE PROFILE IS TAKEN PER SIDE, WHICH IS THE THIRD ATTEMPT AND THE ONE THAT WORKS. A
    # single RADIAL profile came out flat to +-0.001 against a height sigma of 0.024 -- and the
    # rendered normal map still showed four ridge pairs tracking the four band boundaries. Both
    # were true: the gather tilts the surface OUTWARD at every edge, so the artefact is +y at
    # the top and -y at the bottom and a radial mean cancels it exactly while leaving every
    # ridge in place. An instrument that averages over the axis the defect lives on agrees with
    # every broken build. Per side, the profile sees it.
    lum = mat.mean(axis=2)
    if band is not None:
        b, side = band
        nb, ns = 96, int(side.max()) + 1
        idx = (np.clip((np.clip(b, 0.0, 0.5) / 0.5 * nb).astype(int), 0, nb - 1)
               + nb * side.astype(int))
        n = nb * ns
        cnt = np.bincount(idx.ravel(), minlength=n).astype(np.float64)
        tot = np.bincount(idx.ravel(), weights=lum.ravel(), minlength=n)
        prof = np.where(cnt > 0, tot / np.maximum(cnt, 1.0), float(lum.mean()))
        flat = lum - prof[idx]
    else:
        flat = lum - lum.mean()
    micro = flat - T.box_blur(flat, max(1, cell_px // 48))
    # THE RELIEF AMOUNT IS PINNED, NOT INHERITED. `GRAIN_RELIEF` was a GAIN on the plate's own
    # luminance deviation, so a plate with more contrast automatically got more bump -- and
    # round 3's registered art has roughly twice round 2's. Pinning the standard deviation
    # instead means the surface's relief is the SHIPPED relief and the only thing that changed
    # this round is the albedo, which is the one variable under test.
    s = float(micro.std())
    micro = micro * (GRAIN_RELIEF_STD / s) if s > 1e-9 else micro

    # AMBIENT OCCLUSION, at three radii, exactly as the board's own compositor does it --
    # and this is the term that makes the symbol readable at all, because the specular on a
    # flat face is essentially zero (see the header).
    ao = T.cavity_multi(height, radii=(2, 6, 18), gain=AO_GAIN)
    ao = np.clip(1.0 - np.clip(ao, 0.0, 1.0), 0.0, 1.0)
    shade = ao ** AO_GAMMA * (1.0 - FLOOR_DARKEN * floor * cov)

    alb = np.clip(mat * shade[..., None], 0.0, 1.0)
    return alb, height + micro


def _knee(x):
    """Linear below KNEE; above it an exponential approach to 1.0 that never reaches it."""
    return np.where(x <= KNEE, x,
                    KNEE + (1.0 - KNEE) * (1.0 - np.exp(-(x - KNEE) / (1.0 - KNEE))))


def normalise_plate(plate, target=GRAIN_TARGET_LUM, mask=None):
    """Re-base a plate's mean luminance to `target` with a soft top end and NO clipping.

    Returns (normalised, gain, achieved_mean). The gain is SOLVED rather than computed in one
    step because the knee is non-linear: applying `target / mean` once and stopping would
    undershoot and leave the caps darker than intended, which is the very thing this function
    exists to prevent. `mask` restricts the mean the solve targets (round 3 aims it at the
    recessed FIELD, because the FACE is submesh [0] and samples only the field).

    THE COMPRESSION STAYS PER CHANNEL, AND ROUND 3 TRIED TO CHANGE THAT AND MEASURED THAT IT
    SHOULD NOT. The knee and the final clip do distort hue -- badly:

        texels at >= 0.996 in ANY channel, SHIPPED ModBuild 286 atlases, Confirm cell
            oak 97.4 %      steel 0.35 %      bronze 36.3 %
        per-channel contrast of oak's field, same cell
            R 4.70 %        G 8.63 %          B 22.30 %

    So oak's modulator is very nearly a CONSTANT in its own brightest channel, and what
    structure it has lives in the channel the warm state colour attenuates most. The obvious
    correction is to compress each texel's BRIGHTEST channel and scale the other two with it:
    hue ratios then exact, nothing clipped at all. It was built, and it is worse, because a
    modulator's mean and its saturation trade off directly and there is no way round it -- a
    warm plate whose channels sit near (1.00, 0.75, 0.45) of its own maximum cannot have a mean
    luminance above 0.73 with its brightest channel under 1. Measured through the whole shipped
    chain (`_scratch/spend_ab`, rendered face = plate x idle x BoardCapTint x shade):

        chroma spent   rendered sigma oak/steel/bronze     min pairwise dE
        shipped         8.04 / 11.04 /  9.82                    17.0
        0 % (exact)     7.48 /  8.35 /  3.69                    13.7   (oak 19 % darker)
        50 %            4.26 /  9.46 /  5.48                    15.8
        100 % (grey)   13.79 / 10.77 / 10.99                     6.8

    A greyscale modulator wins the contrast handsomely and collapses the per-board separation
    ModBuild 286 solved for -- oak's rendered chroma falls from C* 15.3 to 3.5 and oak-steel dE
    from 21.6 to 6.8 -- because with a neutral plate the boards differ only by their idle
    colour, and those were solved WITH the plates' casts in the product. The clipping is
    therefore load-bearing: it is what lets a warm plate reach 0.837 while keeping a cast.

    It is left exactly as it shipped, and the numbers above are recorded so the next round does
    not rediscover the trade. The lever that would actually free this is to lower the
    modulator's target and raise `[ButtonColors] BoardCapTint` by the reciprocal -- the product
    is unchanged, so nothing renders differently, and the texture gets its headroom back. That
    is four tint families x three channels in `Defaults`, the wire defaults that mirror them,
    and `KeycapGrain` itself for the caps that do not use a board atlas; it is a config round,
    not a texture round, and it is out of this lane.
    """
    m = np.ones(plate.shape[:2], dtype=bool) if mask is None else mask
    base = float(plate.mean(axis=2)[m].mean())
    if base <= 1e-6:
        return plate, 1.0, base, 0.0
    gain = target / base
    out = plate
    got = base
    for _ in range(2 * NORM_ITERS):
        out = _knee(plate * gain)
        got = float(out.mean(axis=2)[m].mean())
        if abs(got - target) < 1e-4:
            break
        gain *= target / max(got, 1e-6)
    out = np.clip(out, 0.0, 1.0)
    q = (out * 255.0 + 0.5).astype(np.uint8)
    clipped = float((q >= 255).any(axis=2)[m].mean())
    return out, gain, got, clipped


def material_cell(plate, cell_px, rng):
    """One cell's worth of material: a random crop of the generated plate, resampled.

    A random crop PER CELL rather than a tile: cells never touch on the mesh (each cap face
    samples exactly one cell), so there is no seam to close and no repetition to hide -- the
    single hardest constraint in the board texture job simply does not exist here. Which is
    also why nothing in this file asks a generated image to tile, the one thing the record
    says never to ask it for.
    """
    ph, pw = plate.shape[:2]
    side = min(ph, pw, max(cell_px, int(0.62 * min(ph, pw))))
    y = int(rng.integers(0, ph - side + 1))
    x = int(rng.integers(0, pw - side + 1))
    crop = plate[y:y + side, x:x + side]
    if side != cell_px:
        crop = np.asarray(
            Image.fromarray((np.clip(crop, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB")
            .resize((cell_px, cell_px), Image.Resampling.LANCZOS),
            dtype=np.float64) / 255.0
    return crop


# Which cells wear the ROUND signet plate. `RestControls` builds the two rest pads with
# `round: round`, and every other cap in the mod is boxy (`PlayTray.1.Core` builds the
# follow/pin toggle with `boxy: true`), so these two are the whole list. If a third round
# control ever appears it belongs here and in nothing else.
ROUND_CELLS = {5, 6}


def cell_band(style, cellkind, cell_px):
    """`(b, side)` for a registered cell, or None for an unregistered one.

    `b` is the distance to the cap's outline in units of the cap's SHORT side; `side` names
    WHICH edge is nearest (0 top, 1 bottom, 2 left, 3 right; one class for a round cell, whose
    bands really are annuli). `carve` subtracts the per-side profile of the cell's own
    luminance before it takes any relief, so nothing the registration painted can become a
    second bevel on top of the real one.
    """
    if cellkind is None:
        return None
    ys = np.broadcast_to(((np.arange(cell_px) + 0.5) / cell_px)[:, None], (cell_px, cell_px))
    xs = np.broadcast_to(((np.arange(cell_px) + 0.5) / cell_px)[None, :], (cell_px, cell_px))
    if cellkind == "round":
        b = 0.5 - np.sqrt((ys - 0.5) ** 2 + (xs - 0.5) ** 2)
        return b, np.zeros((cell_px, cell_px), dtype=np.int64)
    su, sv = O.aspect(style)
    b, vertical = O._band_coord(cell_px, su, sv)
    side = np.where(vertical, np.where(ys < 0.5, 0, 1), np.where(xs < 0.5, 2, 3))
    return b, side.astype(np.int64)


def build_style(style, cell_px, sheet_masks, motif_root, plates_dir, out_dir, report=True,
                objects=True):
    cols, rows = GRID
    atlas_px = (cols * cell_px, rows * cell_px)

    art = None
    if objects:
        # ROUND 3: the cell is not a crop of a material any more, it is a REGISTERED PICTURE OF
        # THE BUTTON. See cap_object.py for the measurement that forced the change -- the
        # shipped cells carried LESS border structure than a random noise field, and half of
        # that was `material_cell` cropping the registration out of whatever the plate had.
        art, obj_gain, obj_base, obj_got, obj_spend = O.normalised_cells(style, cell_px)

    plate_path = os.path.join(plates_dir, f"keycap_plate_{style}.png")
    plate_img = Image.open(plate_path).convert("RGB")
    raw = np.asarray(plate_img, dtype=np.float64) / 255.0
    plate, gain, achieved, _plate_clip = normalise_plate(raw)
    # A STABLE SEED, and the first version of this line was not one. It read
    # `abs(hash(style)) % 2**31`, and Python randomises str hashing per process (PYTHONHASHSEED),
    # so every re-run drew DIFFERENT material crops -- the atlas was not reproducible from its own
    # inputs, and two runs of cap_check.py disagreed by a percentage point on cells that had not
    # changed. That is the shape of defect this pipeline exists to avoid: a number that moves for
    # no reason teaches the reader to ignore numbers that move.
    rng = np.random.default_rng(zlib.crc32(style.encode("utf-8")))

    alb = np.zeros((atlas_px[1], atlas_px[0], 3), dtype=np.float64)
    hgt = np.zeros((atlas_px[1], atlas_px[0]), dtype=np.float64)
    rows_report = []

    by_idx = {c["idx"]: c for c in CELLS}
    for r in range(rows):
        for c in range(cols):
            idx = r * cols + c
            spec = by_idx.get(idx, dict(idx=idx, role=f"reserved{idx}", src=None, layout="plain"))
            if art is not None:
                # Cell 0 is the one EVERY cap's bevel ring and side walls sample
                # (`PlayTray.7.Nested.cs` gives submeshes [1] and [2] `CapRole.Plain`), so it
                # gets the bezel build; cells 5 and 6 are the round rest pads; everything else
                # is a square cap's face. `rng` is still drawn from once per cell so the seed
                # stream -- and therefore every OTHER cell -- is unchanged by this branch.
                cellkind = "bezel" if idx == 0 else ("round" if idx in ROUND_CELLS
                                                    else "square")
                seed = int(rng.integers(0, 2 ** 31))
                mat = np.clip(art[cellkind]
                              * O.field_jitter(cell_px, seed, style, cellkind)[..., None],
                              0.0, 1.0)
            else:
                cellkind = None
                mat = material_cell(plate, cell_px, rng)
            cov = np.zeros((cell_px, cell_px), dtype=np.float64)
            src_note = "plain"
            if spec["src"] is not None:
                kind, key = spec["src"]
                mask = (sheet_masks.get(key) if kind == "sheet"
                        else load_motif_mask(key, STYLE_MOTIF_SHEET[style], motif_root))
                if mask is None:
                    src_note = f"{kind}:{key} MISSING -> plain"
                else:
                    size = SOLO_SIZE if spec["layout"] == "solo" else TEXT_SIZE
                    cy = SOLO_CY if spec["layout"] == "solo" else TEXT_CY
                    cov = place(mask, cell_px, size, cy)
                    src_note = (f"{kind}:{key} minlimb {min_feature_px(mask):.1f}px src "
                                f"-> {min_feature_px(cov >= 0.5):.1f}px cell")
            a, h = carve(mat, cov, cell_px, band=cell_band(style, cellkind, cell_px))
            y0, x0 = r * cell_px, c * cell_px
            alb[y0:y0 + cell_px, x0:x0 + cell_px] = a
            hgt[y0:y0 + cell_px, x0:x0 + cell_px] = h
            rows_report.append(dict(idx=idx, role=spec["role"], layout=spec["layout"],
                                    src=src_note, ink=float((cov >= 0.5).mean())))

    # THE NORMAL MAP SHIPS AT HALF THE ALBEDO'S RESOLUTION, and that is a measurement rather
    # than a saving. Written at full resolution it came out 2.5 MB per style -- BIGGER than
    # the albedo -- because the material's own micro-grain is high-entropy noise and PNG
    # cannot compress it. What that grain buys is nearly nothing: `BoardLit`'s baked key is
    # `normalize(0.35, 0.85, -0.45)` and the CARVE is a smooth, low-frequency field, so
    # halving the sampling costs the groove walls nothing a player can see while the
    # per-texel noise -- which was the whole cost -- is averaged away by the box decimation
    # first. The gradient is taken AFTER the decimation and the strength doubled with it, so
    # the slope of a groove wall in world terms is unchanged.
    half = T.box_blur(hgt, 1)[::2, ::2]
    nrm = T.normal_from_height(half * NORMAL_STRENGTH * 2.0, strength=1.0)
    # THE RED CHANNEL IS INVERTED RELATIVE TO `UnpackNormal`, DELIBERATELY. `tex_common
    # .normal_from_height` writes nx = +dh/du where the convention wants -dh/du (green is
    # correct). Every shipped board normal map carries that same inversion, and the caps sit
    # ON those boards under the same baked key -- mixing conventions would light the cap's
    # carving from the opposite side to the board's own. See ../img2img/README.md, "Normal
    # and MRS": `--x-convention opengl` exists there for when this is fixed at source, and
    # this file has to be fixed in the same commit if it ever is.

    os.makedirs(out_dir, exist_ok=True)
    base = STYLE_FILES[style]
    a_path = os.path.join(out_dir, f"{base}_albedo.png")
    n_path = os.path.join(out_dir, f"{base}_normal.png")
    Image.fromarray((np.clip(alb, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB").save(
        a_path, optimize=True)
    Image.fromarray((np.clip(nrm, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB").save(
        n_path, optimize=True)

    if report:
        print(f"\n{style}: plate {plate_img.size} -> atlas {atlas_px[0]}x{atlas_px[1]} "
              f"({cols}x{rows} cells of {cell_px})")
        if art is not None:
            print(f"    ROUND 3 OBJECT CELLS: registered from "
                  f"{O.GENERATED[style]} -- cell 0 bezel (every cap's bevel + walls), "
                  f"cells {sorted(ROUND_CELLS)} round, the rest square")
            print(f"    level re-based ON THE FIELD: field mean {obj_base:.3f} x gain "
                  f"{obj_gain:.2f} -> {obj_got:.3f} (target {GRAIN_TARGET_LUM:.3f}); the FACE "
                  f"is submesh [0] and samples only the field, so the field is what the "
                  f"cap-to-well ratio turns on")
            print(f"    field texels clipped in at least one channel: {obj_spend * 100:.1f} % "
                  f"-- the price of a warm modulator at mean {GRAIN_TARGET_LUM:.3f}; see "
                  f"normalise_plate for the measured trade and the lever that would free it")
        print(f"    level re-based: plate mean {raw.mean():.3f} x gain {gain:.2f} -> "
              f"{achieved:.3f} (target {GRAIN_TARGET_LUM:.3f}, the shipped KeycapGrain's) — "
              "a keycap texture MODULATES the state colour, so the LEVEL is not the board's to "
              "set; its colour cast and its structure are")
        for rec in rows_report:
            if rec["src"] == "plain" and rec["role"].startswith("reserved"):
                continue
            print(f"    [{rec['idx']:2d}] {rec['role']:<12} {rec['layout']:<5} "
                  f"ink {rec['ink'] * 100:5.2f}%  {rec['src']}")
        print(f"    albedo {os.path.getsize(a_path) / 1e6:.2f} MB   "
              f"normal {os.path.getsize(n_path) / 1e6:.2f} MB")
    return dict(style=style, atlas=atlas_px, cell=cell_px, cells=rows_report,
                albedo_bytes=os.path.getsize(a_path), normal_bytes=os.path.getsize(n_path))


def write_meta(out_dir, base, template_dir):
    """A .meta beside each PNG, cloned from the KeycapGrain pair with a fresh GUID.

    Cloned rather than authored so the import settings (sRGB on the albedo, textureType
    NormalMap on the normal, mipmaps on, maxTextureSize 2048) are the SAME settings the
    shipped keycap textures already import under -- a hand-written meta is one more thing
    that can silently differ from the board's.
    """
    import hashlib
    for suffix, tmpl in (("_albedo", "KeycapGrain_albedo"), ("_normal", "KeycapGrain_normal")):
        src = os.path.join(template_dir, f"{tmpl}.png.meta")
        dst = os.path.join(out_dir, f"{base}{suffix}.png.meta")
        if not os.path.isfile(src):
            continue
        text = open(src, "r", encoding="utf-8").read()
        guid = hashlib.md5(f"GloomhavenVR/{base}{suffix}".encode()).hexdigest()
        out = []
        for line in text.splitlines(True):
            out.append(f"guid: {guid}\n" if line.startswith("guid:") else line)
        with open(dst, "w", encoding="utf-8") as fh:
            fh.write("".join(out))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cell", type=int, default=256, help="cell side in texels")
    ap.add_argument("--sheet", default=os.path.join(PREP, "out", "keycap_symbols_sheet.png"))
    ap.add_argument("--plates", default=os.path.join(PREP, "out"))
    ap.add_argument("--motifs", default=os.path.join(PREP, "out"))
    ap.add_argument("--out", default=BUNDLE)
    ap.add_argument("--styles", nargs="*", default=list(STYLE_FILES))
    ap.add_argument("--json", default=None, help="write a machine-readable build record here")
    ap.add_argument("--no-objects", action="store_true",
                    help="build from the round-2 material plates instead of the round-3 "
                         "registered object cells (the A/B control for every sheet)")
    args = ap.parse_args()

    print(f"symbol sheet: {args.sheet}")
    sheet_masks, notes = load_sheet_masks(args.sheet)
    for cell, what in SHEET_CELLS.items():
        m = sheet_masks[cell]
        n = notes[cell]
        if m is None:
            print(f"  {cell}: EMPTY after cleanup -- {what}")
            continue
        print(f"  {cell}: {what:<50} {m.shape[0]:4d}px stencil, ink "
              f"{m.mean() * 100:5.1f}%, threshold {n['thr']:.3f}, "
              f"min limb {min_feature_px(m):.1f}px")

    recs = []
    for style in args.styles:
        recs.append(build_style(style, args.cell, sheet_masks, args.motifs, args.plates,
                                args.out, objects=not args.no_objects))
        write_meta(args.out, STYLE_FILES[style], BUNDLE)
    print(f"\nmip guard: {MARGIN_NOTE}")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(dict(grid=GRID, cell=args.cell, cells=CELLS, styles=recs), fh,
                      indent=2, default=str)


if __name__ == "__main__":
    main()
