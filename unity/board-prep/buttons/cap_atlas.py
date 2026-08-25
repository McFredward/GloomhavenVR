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

import tex_common as T          # noqa: E402
import tex_symbols as S         # noqa: E402

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
# `Uv(p) = (p.x/width + 0.5, p.y/height + 0.5)`, applied to top, bevel ring AND walls). So
# the flat TOP PLATEAU -- the only part a symbol may live on -- is NOT the whole cell: it is
# inset by the 7 mm bevel on every side. On the tuned 63 x 65 mm cap that is UV
# [0.111, 0.889]; on the BRONZE board, whose recess forces the fit down to about 59 x 48 mm,
# it is [0.146, 0.854]. The tighter one is the one that decides, exactly as `_seatMinHalf`
# decides the cap size, so the usable band is taken as [0.16, 0.84].
#
# PIL row 0 is the TOP of the image and the cap's v = 1 is the TOP of the cap (v grows with
# +Y), so image-top IS cap-top and no flip is needed anywhere in this file.
PLATEAU_LO, PLATEAU_HI = 0.16, 0.84

# "solo" -- the symbol is the whole face.
SOLO_SIZE = 0.56                # fraction of the cell side
SOLO_CY = 0.50                  # centre, fraction of the cell side from the TOP

# "text" -- the cap keeps its LIVE GAME TEXT (`CardsGameApi.PickDialogOptionLabel` reads it
# off the option button a 2D player would click, so it changes every pick and with the
# language) and the symbol sits above it. These two numbers and the label box in
# `Cards/CapSymbols.cs` are one layout: the symbol occupies the upper band and the TMP label
# is moved down into the lower one.
TEXT_SIZE = 0.32
TEXT_CY = 0.30                  # from the TOP of the cell
# The label box `Cards/CapSymbols.cs` gives the TMP on a "text" cap, as fractions of the cap
# footprint: it is the band BELOW the symbol, v 0.13 .. 0.50, i.e. centred 0.185 of the cap
# height below the middle. The two must be read together -- a change here that is not made
# there puts a caption through a carved symbol.
TEXT_LABEL_CENTRE_DY = -0.185   # of the cap HEIGHT, from the cap centre
TEXT_LABEL_BOX = (0.92, 0.36)   # of the cap width / height

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


def carve(mat, cov, cell_px):
    """Cut `cov` into `mat`. Returns (albedo RGB in [0,1], height field)."""
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
    lum = mat.mean(axis=2)
    micro = (lum - lum.mean()) * GRAIN_RELIEF

    # AMBIENT OCCLUSION, at three radii, exactly as the board's own compositor does it --
    # and this is the term that makes the symbol readable at all, because the specular on a
    # flat face is essentially zero (see the header).
    ao = T.cavity_multi(height, radii=(2, 6, 18), gain=AO_GAIN)
    ao = np.clip(1.0 - np.clip(ao, 0.0, 1.0), 0.0, 1.0)
    shade = ao ** AO_GAMMA * (1.0 - FLOOR_DARKEN * floor * cov)

    alb = np.clip(mat * shade[..., None], 0.0, 1.0)
    return alb, height + micro


def normalise_plate(plate, target=GRAIN_TARGET_LUM):
    """Re-base a plate's mean luminance to `target`, uniformly in RGB, with a soft top end.

    Returns (normalised, gain, achieved_mean). The gain is SOLVED rather than computed in one
    step because the knee is non-linear: applying `target / mean` once and stopping would
    undershoot and leave the caps darker than intended, which is the very thing this function
    exists to prevent.
    """
    base = float(plate.mean())
    if base <= 1e-6:
        return plate, 1.0, base
    gain = target / base
    out = plate
    for _ in range(NORM_ITERS):
        x = plate * gain
        # Linear below the knee; above it, an exponential approach to 1.0 that never clips.
        out = np.where(x <= KNEE, x,
                       KNEE + (1.0 - KNEE) * (1.0 - np.exp(-(x - KNEE) / (1.0 - KNEE))))
        got = float(out.mean())
        if abs(got - target) < 1e-4:
            break
        gain *= target / max(got, 1e-6)
    return np.clip(out, 0.0, 1.0), gain, float(out.mean())


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


def build_style(style, cell_px, sheet_masks, motif_root, plates_dir, out_dir, report=True):
    cols, rows = GRID
    atlas_px = (cols * cell_px, rows * cell_px)
    plate_path = os.path.join(plates_dir, f"keycap_plate_{style}.png")
    plate_img = Image.open(plate_path).convert("RGB")
    raw = np.asarray(plate_img, dtype=np.float64) / 255.0
    plate, gain, achieved = normalise_plate(raw)
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
            a, h = carve(mat, cov, cell_px)
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
                                args.out))
        write_meta(args.out, STYLE_FILES[style], BUNDLE)
    print(f"\nmip guard: {MARGIN_NOTE}")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(dict(grid=GRID, cell=args.cell, cells=CELLS, styles=recs), fh,
                      indent=2, default=str)


if __name__ == "__main__":
    main()
