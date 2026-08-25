#!/usr/bin/env python3
"""Round 2 of the KEYCAP MATERIAL PLATES: the gpt-image-2 prompts, the ingest, and the two
pictures that answer the complaint.

THE COMPLAINT, verbatim
-----------------------
    "Die Textur die dort gewaehlt ist, ist einheitlich und passt sonst nicht wirklich zum
     Styl. Generiere eine echte unique Textur fuer die buttons mit gpt-image-2."
    -- user, 2026-08-25, against .planning/debug/abgeschnitter_text.jpg (the BRONZE board)

On that screenshot the cap face reads as a flat olive-khaki field with fine noise and no
structure, sitting on a verdigris-and-gold board. Round 1's bronze plate IS that: measured,
its 62 %-crop-to-256-texel cell -- which is what a cap face actually samples -- carries a
luminance standard deviation of **0.047**, i.e. 5.6 % contrast, and every feature in it is
below the resampling scale. Round 1 asked for materials; it did not ask for a FEATURE SIZE,
and everything it got was micro-mottle.

WHY THE FEATURE SIZE IS THE WHOLE PROBLEM, and it is arithmetic
--------------------------------------------------------------
`cap_atlas.material_cell` takes a random crop of 62 % of the plate (634 px of 1024) and
resamples it to one atlas cell (256 texels). The cap is ~96 px across at arm's length on a
Quest 3. So a feature `f` texels wide in the generated 1024 plate arrives on screen at

    f * (256 / 634) * (96 / 256)  =  f * 0.151  screen pixels.

Fine grain at f = 4 lands at 0.6 px -- it is averaged away before it is ever drawn. To read
as structure at 3 px on screen a feature must be **~20 px** in the plate, and to read as
STRUCTURE rather than noise it wants an order more: this round's prompts therefore ask for
named features sized as a FRACTION OF THE FRAME (a tenth, a sixth, a twelfth), which is the
one thing round 1's prompts never said.

THE PROMPTS, AND THE ONE THAT IS NOT HERE -- read this before trusting `PROMPTS`
--------------------------------------------------------------------------------
Round 1 recorded "the prompts are in the commit that added this file", and by the time this
round needed them they were not in that commit -- only the intent was. So `PROMPTS` below
holds the ROUND-A asks, verbatim, and `PROMPTS_SUPERSEDED` records what was wrong with what
each one produced.

**BUT TWO OF THE THREE SHIPPED PLATES DID NOT COME FROM THE WORDS IN THIS FILE.** `oak_b` and
`bronze_d` were re-rolled from revised asks, and the revised WORDING was not captured before
the generating session ended -- only its substance, which is recorded under
`SHIPPED_ASK_SUBSTANCE`. That is exactly the failure this section was written to prevent,
recorded plainly rather than papered over: a later round can reproduce the INTENT of those two
asks but not the exact string. The single change that made both of them work is stated in the
arithmetic above and is the thing worth carrying forward -- ask for a NAMED FEATURE AT A STATED
FRACTION OF THE FRAME, not for a material.

WHAT WAS GENERATED, AND WHAT WAS DISCARDED
------------------------------------------
SEVEN images, all gpt-image-2, all requested `1:1` and all delivered **1024x1024 exactly** --
the 3:2 rescale trap (../img2img/README.md) is real for 16:9 and 2:1 asks and did not fire
here, and that was READ BACK from the returned files rather than assumed. Four were discarded.

    keycap2_plate_oak.png       DROPPED  ~20 fine rings, FINER than round 1. Measured worse:
                                       cap-scale STORY x0.73 against the shipped plate
    keycap2_plate_oak_b.png     SHIPPED  6-7 wide value bands, ray fleck at a fifth of the
                                       frame, one aged quarter. STORY x1.60
    keycap2_plate_steel.png     SHIPPED  case-hardened steel: temper-bloom clouds at a sixth
                                       of the frame plus draw-file scratch patches. STORY x1.71
    keycap2_plate_bronze.png    DROPPED  the facets came back as a REGULAR HEXAGONAL TILING
                                       -- it reads as reptile scales, not as hammered metal,
                                       and a repeating lattice is the one thing a random
                                       per-cell crop cannot hide
    keycap2_plate_bronze_b.png  DROPPED  good look, but only STORY x1.09 -- it does not answer
                                       the complaint, which is about structure and not taste
    keycap2_plate_bronze_c.png  DROPPED  soft tonal drift only -- the closest of all four to
                                       the "einheitlich" the user rejected, so it loses on
                                       the complaint's own terms
    keycap2_plate_bronze_d.png  SHIPPED  broad planishing dishes at a sixth of the frame,
                                       bright gold crowns, verdigris only in the rims. STORY x2.47

THE LEVEL IS STILL RE-BASED BY `cap_atlas.normalise_plate`, imported and not re-implemented.
A keycap texture MODULATES the state colour (`BoardLit`: alb = tex2D(_MainTex, uv) * _Color),
so its mean belongs to the state palette and only its cast and its structure belong to the
board. That is round 1's finding and nothing here touches it.
"""
import argparse
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

import cap_atlas as C            # noqa: E402
import cap_deltae as D          # noqa: E402

REPO = os.path.dirname(os.path.dirname(PREP))


def _planning_root(repo):
    """`.planning/debug/` is GITIGNORED, so it exists in the main checkout and NOT in a git
    worktree cut from it. A worktree lives at <main>/.claude/worktrees/<name>, so the main
    checkout is two levels up from `worktrees` -- and if this is not a worktree the path is
    already right. Getting this wrong writes the round's pictures into a directory nobody
    looks at, which is a silent failure rather than a loud one."""
    parts = repo.split(os.sep)
    if len(parts) >= 3 and parts[-2] == "worktrees" and parts[-3] == ".claude":
        return os.sep.join(parts[:-3])
    return repo


MAIN = _planning_root(REPO)
OUT = os.path.join(PREP, "out")
DEBUG = os.path.join(MAIN, ".planning", "debug", "keycaps2")
SHOT = os.path.join(MAIN, ".planning", "debug", "abgeschnitter_text.jpg")

# The generated file each board ships, and the round-1 plate it replaces (kept beside it as
# `keycap_plate_<style>_r1.png` so the before/after sheet is drawn from files, not memory).
CHOSEN = {"oak": "keycap2_plate_oak_b.png",
          "steel": "keycap2_plate_steel.png",
          "bronze": "keycap2_plate_bronze_d.png"}
DISCARDED = {"keycap2_plate_oak.png": "~20 fine rings, finer than round 1 -- STORY x0.73",
             "keycap2_plate_bronze.png": "regular hexagonal tiling -- reads as scales",
             "keycap2_plate_bronze_b.png": "good look, but only STORY x1.09",
             "keycap2_plate_bronze_c.png": "soft drift only -- nearest to the rejected look"}

PROMPTS = {
    "oak": """A square swatch of a MATERIAL, photographed straight down flat-on under \
completely even, shadowless studio light. The frame is filled edge to edge with one \
continuous piece of the surface - nothing else is in the picture.

THE MATERIAL: quarter-sawn aged oak, waxed, the panel of a guild-hall lectern. Warm \
honey-brown. Because it is quarter-sawn the growth rings run as straight vertical lines - \
about eight to ten rings across the frame, wide and clearly separated, alternating pale \
sapwood-honey and darker latewood-umber. Across those rings lie the MEDULLARY RAY FLECKS: \
broad silvery-tan lens-shaped figures, each roughly one twelfth of the frame long, lying at \
a slight angle to the grain, catching the light with a pale sheen - the tiger-stripe fleck \
of true quarter-sawn oak. Open pores read as short dark fissures along the grain. A waxed \
sheen, a couple of small dark knots-in-passing, some age darkening in patches.

The structure must be LARGE and legible: rings and ray flecks that read across the whole \
plate, not fine sanded noise. Match the honey-brown wood tone of the reference image.

STRICTLY EXCLUDED: no border, no frame, no edge, no rim, no bevel, no chamfer, no plank \
seams, no butt joints, no nails, no vignette, no cast shadow, no directional highlight \
sweep across the plate, no perspective, no tilt, no depth of field, no lettering, no \
letters, no numbers, no symbols, no logos, no ornament, no carving, no objects, no \
background. Even illumination corner to corner.""",

    "steel": """A square swatch of a MATERIAL, photographed straight down flat-on under \
completely even, shadowless studio light. The frame is filled edge to edge with one \
continuous piece of the surface - nothing else is in the picture.

THE MATERIAL: case-hardened and cold-blued steel, the face plate of an armourer's \
instrument. A hard, cool blue-grey ground. Over it lies the TEMPER BLOOM of case hardening: \
large soft irregular clouds, each about one sixth of the frame across, drifting from \
straw-gold through pale rose to peacock blue-violet and back to cold grey - a mottled \
oil-on-water iridescence with clear big shapes, not a uniform tint. Crossing the bloom are \
groups of DRAW-FILE SCRATCHES: straight fine parallel tool marks running in two or three \
different directions in distinct patches, each patch a hand's width of the frame, catching \
light as thin bright lines. A few darker scale patches and pin-point pits.

The structure must be LARGE and legible: half a dozen temper clouds across the plate and \
clearly separated scratch patches, not fine grey noise. Match the steel tone of the \
reference image; keep it a hard cool grey with only faint iridescence, never a saturated \
rainbow.

STRICTLY EXCLUDED: no border, no frame, no edge, no rim, no bevel, no chamfer, no rivets, \
no vignette, no cast shadow, no directional highlight sweep across the plate, no \
perspective, no tilt, no depth of field, no lettering, no letters, no numbers, no symbols, \
no logos, no ornament, no engraving, no objects, no background. Even illumination corner to \
corner.""",

    "bronze": """A square swatch of a MATERIAL, photographed straight down flat-on under \
completely even, shadowless studio light. The frame is filled edge to edge with one \
continuous piece of the surface - nothing else is in the picture.

THE MATERIAL: an old cast-bronze plate that has been hammered flat by hand. The planishing \
marks are IRREGULAR AND OVERLAPPING - wandering, uneven, of clearly different sizes, some \
broad and shallow, some small and deep, some struck twice on top of each other, running in \
no fixed direction. NOT a regular honeycomb, NOT a repeating tiling, NOT scales, NOT a \
lattice. Between and across them the metal is burnished smooth in wide passages where a \
hand has polished it, so large areas of the plate are plain warm gold-brown with almost no \
hammering at all, and other areas are densely worked. Warm brown-gold bronze, bright and \
buttery where burnished, deeper amber-brown where dull.

Thin pale blue-green VERDIGRIS is trapped only in the deepest creases and in a few drift \
patches near the darker areas - sparse, broken, never a continuous outline around every \
mark, never a general green film. A few dark oxide freckles, one or two casting pits, a \
faint sweep of old scratches.

The variation must be LARGE-SCALE and uneven: the plate should read differently in its \
different quarters. Match the bronze hue and the verdigris green of the reference image.

STRICTLY EXCLUDED: no repeating pattern, no tiling, no honeycomb, no hexagons, no scales, \
no border, no frame, no edge, no rim, no bevel, no chamfer, no vignette, no cast shadow, no \
directional highlight sweep, no perspective, no tilt, no depth of field, no lettering, no \
letters, no numbers, no symbols, no logos, no ornament, no engraving, no rivets, no \
objects, no background. Even illumination corner to corner.""",
}

# THE SUBSTANCE of the two revised asks whose exact wording was not captured (see the module
# header). These are NOT prompts and must not be pasted as one -- they are the description of
# what was asked for, so a re-roll starts from the intent rather than from nothing.
SHIPPED_ASK_SUBSTANCE = {
    "keycap2_plate_oak_b.png":
        "quarter-sawn oak, but COARSER than the first ask: six to seven WIDE value bands "
        "across the frame instead of ~20 fine rings; medullary ray fleck sized at a FIFTH of "
        "the frame; one quarter of the plate visibly aged/darker so the plate is not the same "
        "everywhere.",
    "keycap2_plate_bronze_d.png":
        "hand-planished cast bronze: broad planishing DISHES at a SIXTH of the frame (not a "
        "lattice and not fine mottle), bright burnished gold on the crowns of the dishes, "
        "verdigris confined to the rims of the dishes rather than spread over the surface.",
}

# Every request carried that board's OWN albedo face band (`gen_capref.py`) as the single
# reference image, exactly as round 1 did -- the cap material is anchored to the board's
# material rather than to a second verbal description of it.
REFERENCE = "ref_face_{style}.png"


def _check_manifest(out_dir=OUT):
    """THE NARRATIVE ABOVE AND THE DICTS BELOW MUST NOT DISAGREE, and they already did once:
    the module header described five images and named `oak` and `bronze_b` as kept, while
    `CHOSEN` correctly named `oak_b` and `bronze_d`. A stale record reads perfectly and is
    believed, which is how ".planning" entries get written from something that is no longer
    true. So: every file named by CHOSEN, DISCARDED or SHIPPED_ASK_SUBSTANCE must exist, every
    generated plate must be accounted for by exactly one of CHOSEN/DISCARDED, and no file may
    be in both. Raises rather than warns -- a manifest nobody notices is worth nothing."""
    if not os.path.isdir(out_dir):
        return
    named = set(CHOSEN.values()) | set(DISCARDED)
    for f in sorted(named | set(SHIPPED_ASK_SUBSTANCE)):
        if not os.path.isfile(os.path.join(out_dir, f)):
            raise RuntimeError(f"plates.py manifest names {f}, which is not in {out_dir}")
    overlap = set(CHOSEN.values()) & set(DISCARDED)
    if overlap:
        raise RuntimeError(f"plates.py manifest has {sorted(overlap)} both kept and discarded")
    on_disk = {f for f in os.listdir(out_dir)
               if f.startswith("keycap2_plate_") and f.endswith(".png")}
    missing = on_disk - named
    if missing:
        raise RuntimeError(
            f"plates.py manifest does not account for {sorted(missing)} — every generated "
            "plate must be listed as kept or discarded, with the reason, or the record of "
            "this round is already stale")


_check_manifest()


# =============================================================================================
# INGEST
# =============================================================================================
def ingest(out_dir=OUT, report=True):
    """Put the chosen generated plates where `cap_atlas.py` reads them from.

    The round-1 plate is preserved as `keycap_plate_<style>_r1.png` first -- `out/` is
    gitignored, so if it is not kept beside the new one the before/after sheet has no
    "before" to draw and the comparison becomes a memory."""
    for style, src_name in CHOSEN.items():
        src = os.path.join(out_dir, src_name)
        dst = os.path.join(out_dir, f"keycap_plate_{style}.png")
        keep = os.path.join(out_dir, f"keycap_plate_{style}_r1.png")
        if not os.path.isfile(src):
            raise FileNotFoundError(src)
        if os.path.isfile(dst) and not os.path.isfile(keep):
            shutil.copyfile(dst, keep)
        im = Image.open(src)
        if im.size != (1024, 1024):
            raise ValueError(f"{src_name} is {im.size}, not 1024x1024 -- the 3:2 trap fired")
        shutil.copyfile(src, dst)
        if report:
            a = np.asarray(im.convert("RGB"), dtype=np.float64) / 255.0
            print(f"  {style:<7} {src_name:<28} {im.size}  mean {a.mean():.4f} "
                  f"rgb {a.reshape(-1,3).mean(0).round(4).tolist()}  -> keycap_plate_{style}.png")
    if report:
        for name, why in DISCARDED.items():
            print(f"  DISCARDED {name:<28} {why}")


def cell_contrast(plate, crop_frac=0.62, cell=256, y=100, x=100):
    """The luminance std of one fixed crop of the plate at cell resolution.

    KEPT, BUT IT IS NOT THE NUMBER THIS ROUND IS JUDGED ON, and the reason is instructive: it
    samples ONE FIXED OFFSET. On a plate whose whole point is large-scale variation, a fixed
    crop can land in the quiet quarter and report a fall where the built atlas reports a rise
    -- which is exactly what it did for round-2 oak (0.057 -> 0.044 here, while the cap-scale
    structure below went UP by 1.6x). A summary stat over one draw is not the distribution."""
    side = int(crop_frac * min(plate.shape[:2]))
    crop = plate[y:y + side, x:x + side]
    small = np.asarray(Image.fromarray((np.clip(crop, 0, 1) * 255.0 + 0.5).astype(np.uint8),
                                       "RGB").resize((cell, cell), Image.Resampling.LANCZOS),
                       dtype=np.float64) / 255.0
    return float(small.mean(axis=2).std())


# The split that answers "einheitlich". A single contrast number cannot: the round-1 bronze and
# the round-2 bronze can carry the SAME total contrast and still read completely differently,
# because one carries it as a fine even sponge and the other as large worked passages. So the
# field is low-passed at 4 px of its ~106 x 83 px on-screen size -- about a tenth of the cap --
# and reported as two numbers:
#     STORY  the low-pass's contrast: does this material CHANGE across a cap face
#     GRAIN  the residual: the micro-material
# Both relative to the field's own mean, so they are comparable between two different
# brightnesses. This is `tex_labstats`'s STORY/GRAIN idea from the board round, at cap scale.
STRUCTURE_SIGMA = 4.0


def cap_scale_stats(albedo_path, normal_path, state, cell_index=0):
    """(total, STORY, GRAIN) in per cent of the mean, for one style's PLAIN cap face -- the
    whole shipped chain: atlas cell -> recessed field -> state colour -> normal-map shade ->
    the mip the rig samples -> the size the cap occupies on screen."""
    cell = _face_cell(albedo_path, cell_index, state, normal_path=normal_path)
    cell = _to_mip(cell, FIELD_QUAD)
    lum = cell.mean(axis=2)
    lo = np.asarray(Image.fromarray((np.clip(lum, 0, 1) * 255.0 + 0.5).astype(np.uint8), "L")
                    .filter(ImageFilter.GaussianBlur(STRUCTURE_SIGMA)), dtype=np.float64) / 255.0
    m = max(float(lum.mean()), 1e-9)
    return (100.0 * float(lum.std()) / m,
            100.0 * float(lo.std()) / m,
            100.0 * float((lum - lo).std()) / m)


# =============================================================================================
# PICTURE 1 -- the six plates, same scale, labelled
# =============================================================================================
ATLAS_R1 = "keycap_atlas_{style}_r1.png"
NORMAL_R1 = "keycap_normal_{style}_r1.png"
ATLAS_NEW = "Keycap{Style}_albedo.png"
NORMAL_NEW = "Keycap{Style}_normal.png"


def before_after(out_dir=OUT, dst=None, tile=460, bundle=None):
    dst = dst or os.path.join(DEBUG, "plates_before_after.png")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    pad, head, foot = 8, 40, 66
    sheet = Image.new("RGB", (3 * tile + 4 * pad, 2 * (tile + foot) + head + 2 * pad),
                      (18, 18, 20))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 12), "KEYCAP MATERIAL PLATES  -  round 1 (shipped, ModBuild 281) above, "
                      "round 2 (gpt-image-2, this round) below.  Same scale, 1024^2 sources.",
           fill=(235, 235, 220))
    d.text((pad, 26), "STORY / GRAIN are the cap-scale numbers: the built atlas' plain cap "
                      "face, through its normal map and the mip the rig samples, split at a "
                      "tenth of the cap. STORY is what 'einheitlich' is about.",
           fill=(150, 150, 160))
    for row, suffix in enumerate(("_r1", "")):
        for col, style in enumerate(D.STYLES):
            path = os.path.join(out_dir, f"keycap_plate_{style}{suffix}.png")
            im = Image.open(path).convert("RGB")
            arr = np.asarray(im, dtype=np.float64) / 255.0
            x0 = pad + col * (tile + pad)
            y0 = head + pad + row * (tile + foot)
            sheet.paste(im.resize((tile, tile), Image.Resampling.LANCZOS), (x0, y0))
            tag = "ROUND 1  (shipped)" if suffix else "ROUND 2  (new)"
            d.rectangle([x0, y0, x0 + tile - 1, y0 + tile - 1], outline=(70, 70, 78))
            d.text((x0 + 4, y0 + tile + 4), f"{style.upper()}   {tag}", fill=(240, 230, 160))
            d.text((x0 + 4, y0 + tile + 18),
                   f"mean {arr.mean():.3f}   rgb {arr.reshape(-1,3).mean(0).round(3).tolist()}",
                   fill=(180, 180, 190))
            if bundle:
                if suffix:
                    a_p = os.path.join(out_dir, ATLAS_R1.format(style=style))
                    n_p = os.path.join(out_dir, NORMAL_R1.format(style=style))
                else:
                    a_p = os.path.join(bundle, ATLAS_NEW.format(Style=style.capitalize()))
                    n_p = os.path.join(bundle, NORMAL_NEW.format(Style=style.capitalize()))
                t, st, gr = cap_scale_stats(a_p, n_p, D.IDLE_SHIPPED)
                d.text((x0 + 4, y0 + tile + 32),
                       f"AT CAP SCALE:  total {t:5.2f} %   STORY {st:5.2f} %   GRAIN {gr:5.2f} %",
                       fill=(200, 220, 200) if not suffix else (180, 180, 190))
            else:
                d.text((x0 + 4, y0 + tile + 32),
                       f"cell std {cell_contrast(arr):.4f}", fill=(180, 180, 190))
            d.text((x0 + 4, y0 + tile + 46), os.path.basename(path), fill=(120, 120, 130))
    sheet.save(dst)
    return dst


# =============================================================================================
# PICTURE 2 -- the new bronze cap face dropped into the well in the user's own screenshot
# =============================================================================================
# THE CAP IN `abgeschnitter_text.jpg`, LOCATED BY MEASUREMENT rather than by eye -- and the
# first version of this block was wrong in a way worth recording, because it is this project's
# own recurring defect.
#
# The first quad was DERIVED: cap top face x 1962..2100, y 1038..1168 from luminance profiles,
# then inset by `cap_atlas.PLATEAU_LO` = 0.135 of the cap's short side. It came out 17 px too
# far right at the left edge and 14 px short at the right, because the derivation assumed the
# recess is seen symmetrically. IT IS NOT: the board recedes to the right, so the recess's
# LEFT inner wall is visible (a warm gold band at x 1964..1985) and the RIGHT one is hidden
# behind its own lip. An inset computed from the cap's silhouette cannot know that.
#
# What replaced it is a DETECTION, on the one signal that separates the two surfaces: the
# field is drawn through a GREEN state colour and every bevel and wall around it is warm, so
# `G - R > 0.004` is the field and nothing else is. Measured at four heights and three widths:
#     x 1986..2091 at y 1060-1070      y 1061..1142 at x 1992-2005
#     x 1989..2095 at y 1095-1112      y 1056..1138 at x 2075-2090
# which is the trapezoid below, and the 5 px of rise from left to right IS the perspective.
FIELD_QUAD = [(1987.0, 1060.0), (2094.0, 1056.0), (2095.0, 1139.0), (1990.0, 1143.0)]
CROP_BOX = (1900, 985, 2180, 1215)

# THE CAP IN THAT SCREENSHOT IS NOT IDLE, and finding that out is what turned the compositor
# from a picture into evidence. The first control panel drew the field through `IdleColor` and
# came back visibly warmer and brighter than the screenshot underneath it. Measured, the
# screenshot's field is (0.172, 0.188, 0.085) -- GREEN, G > R -- while idle predicts
# (0.248, 0.206, 0.097), warm, R > G. The confirm cap in that frame is in its ACCENT state: a
# card selection is pending, so `PlayTray.6.Build` line 427 passes
# `new Color(0.35f, 0.46f, 0.28f)  // T4: muted sage green` as this button's accent colour.
#
# Through the SAME arithmetic, with the accent colour and with the seat floor lifting its red
# channel from 0.175 to 0.2025 (`SeatedCapColor`, and it is not decorative here -- it is the
# only reason the red channel is where it is), the model gives
#     (0.167, 0.186, 0.078)   against the screenshot's measured   (0.172, 0.188, 0.085)
# i.e. every channel within 0.007 of a JPEG of a headset frame. That is a far stronger check
# on the chain than the dE record was, because it is against the rig rather than against
# another instrument -- and it is what licenses drawing a replacement face into this hole.
#
# AN EARLIER SAMPLE OF THE SAME PIXELS SAID THE OPPOSITE and it was wrong for a dull reason:
# it averaged x 1972..2000, which straddles that left gold wall, and the wall dragged the mean
# warm. One step too early on the boundary and the measurement agrees with the wrong model.
ACCENT_CONFIRM = np.array([0.35, 0.46, 0.28])   # Cards/PlayTray.6.Build.cs:427

# The cell's UVs are PLANAR OVER THE WHOLE CAP FOOTPRINT (`CardMesh.BuildBeveledKeycap`), so
# one atlas cell covers the cap's whole top face, bevel included -- NOT just the recessed
# field. Warping the whole cell into the field quad would put the carved symbol at the wrong
# size and the wrong height. The field is `cap_atlas.PLATEAU_LO..PLATEAU_HI` of the cell
# vertically, and the same ABSOLUTE bezel as a fraction of the WIDTH horizontally, which
# cap_atlas records as 0.111 on Bronze (the bezel is 0.135 of the SHORT side, and Bronze's
# short side is its height).
FIELD_V = (C.PLATEAU_LO, C.PLATEAU_HI)
FIELD_U_BRONZE = (0.111, 0.889)

CAPTION_LUM = 0.30      # inside the field only the caption reaches this


def _quad_size(quad):
    """The quad's own pixel extent -- the destination sampling rate."""
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = quad
    w = 0.5 * (np.hypot(x1 - x0, y1 - y0) + np.hypot(x2 - x3, y2 - y3))
    h = 0.5 * (np.hypot(x3 - x0, y3 - y0) + np.hypot(x2 - x1, y2 - y1))
    return int(round(w)), int(round(h))


def _to_mip(cell, quad):
    """Area-resample a cell down to the size it will occupy, i.e. pick its mip level.

    WITHOUT THIS THE PICTURE LIES IN THE DIRECTION THAT MATTERS MOST HERE. `_bilinear_quad`
    takes ONE sample per destination pixel, so warping a 186x200 cell into a ~108x83 hole is a
    2:1 minification with no filtering: it aliases, and aliasing PRESERVES high-frequency
    energy that the GPU's trilinear mip chain would have averaged away. Measured, the control
    panel came back at 26.0 % relative field contrast against the screenshot's own 10.8 % --
    an instrument that made every plate look 2.4x more textured than the rig draws it, which
    is precisely the wrong error for a round about whether a texture reads as uniform.
    """
    w, h = _quad_size(quad)
    ch, cw = cell.shape[:2]
    if w >= cw and h >= ch:
        return cell
    im = Image.fromarray((np.clip(cell, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB")
    return np.asarray(im.resize((max(1, w), max(1, h)), Image.Resampling.BOX),
                      dtype=np.float64) / 255.0


def _bilinear_quad(cell_rgb, quad, size):
    """Draw `cell_rgb` into `quad` on a `size` canvas, plus its coverage mask.

    An inverse map with bilinear sampling: for every destination pixel inside the quad, find
    its (u, v) by inverting the bilinear surface, and sample the cell there. Written out
    rather than pulled from a library because the only alternative available here is PIL's
    QUAD transform, which maps a quad INTO a rectangle and would need the inverse quad
    anyway."""
    w, h = size
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = quad
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float64)
    # Solve the bilinear map by fixed-point iteration; the quad is nearly a rectangle here so
    # it converges in a handful of steps and needs no quadratic-formula special cases.
    u = np.full((h, w), 0.5)
    v = np.full((h, w), 0.5)
    for _ in range(24):
        px = ((1 - u) * (1 - v) * x0 + u * (1 - v) * x1 + u * v * x2 + (1 - u) * v * x3)
        py = ((1 - u) * (1 - v) * y0 + u * (1 - v) * y1 + u * v * y2 + (1 - u) * v * y3)
        dxdu = (1 - v) * (x1 - x0) + v * (x2 - x3)
        dxdv = (1 - u) * (x3 - x0) + u * (x2 - x1)
        dydu = (1 - v) * (y1 - y0) + v * (y2 - y3)
        dydv = (1 - u) * (y3 - y0) + u * (y2 - y1)
        det = dxdu * dydv - dxdv * dydu
        det = np.where(np.abs(det) < 1e-9, 1e-9, det)
        rx, ry = xs - px, ys - py
        u = u + (dydv * rx - dxdv * ry) / det
        v = v + (-dydu * rx + dxdu * ry) / det
    inside = (u >= 0) & (u <= 1) & (v >= 0) & (v <= 1)
    ch, cw = cell_rgb.shape[:2]
    su = np.clip(u * (cw - 1), 0, cw - 1)
    sv = np.clip(v * (ch - 1), 0, ch - 1)
    i0, j0 = np.floor(sv).astype(int), np.floor(su).astype(int)
    i1 = np.minimum(i0 + 1, ch - 1)
    j1 = np.minimum(j0 + 1, cw - 1)
    fy, fx = (sv - i0)[..., None], (su - j0)[..., None]
    out = ((cell_rgb[i0, j0] * (1 - fx) + cell_rgb[i0, j1] * fx) * (1 - fy)
           + (cell_rgb[i1, j0] * (1 - fx) + cell_rgb[i1, j1] * fx) * fy)
    return out, inside


def _cell_of(atlas_path, cell_index, grid=C.GRID, field=True):
    """One atlas cell, cropped to the RECESSED FIELD. Works for the albedo and the (half
    resolution) normal map alike, because both are indexed by the same cell grid."""
    atlas = np.asarray(Image.open(atlas_path).convert("RGB"), dtype=np.float64) / 255.0
    cols, rows = grid
    side = atlas.shape[0] // rows
    r, c = divmod(cell_index, cols)
    cell = atlas[r * side:(r + 1) * side, c * side:(c + 1) * side]
    # `cap_atlas` writes cell 0 at the image's TOP-LEFT and PIL row 0 is the image top, so the
    # index arithmetic here is the same as build_style's and needs no flip.
    if field:
        y0, y1 = int(round(FIELD_V[0] * side)), int(round(FIELD_V[1] * side))
        x0, x1 = int(round(FIELD_U_BRONZE[0] * side)), int(round(FIELD_U_BRONZE[1] * side))
        cell = cell[y0:y1, x0:x1]
    return cell


# THE MIP THE RIG SAMPLES THE NORMAL MAP AT, and this is the one number here that is FITTED
# rather than derived -- so it is labelled, and it is checked against the thing it was fitted
# to. Drawn at mip 0 the control panel came back at 20.4 % relative field contrast where the
# screenshot's own field carries 10.8 %: the compositor was making the plate's micro-relief
# read twice as strongly as the rig draws it, in a round about whether a texture looks
# uniform, which is the worst possible direction for that error.
#
# The cap occupies ~107 x 84 px of the frame and its normal-map cell is 93 x 100 texels
# (`cap_atlas` ships the normal at HALF the albedo's resolution), so the sampler sits about
# one mip level below mip 0 -- and averaging normals across a mip flattens their slope, which
# is exactly what is missing. A Gaussian on the normal cell is the cheap stand-in for that
# averaging, and the sweep says sigma 1.25 reproduces the screenshot:
#     sigma  0.0  0.5  1.0  1.25  1.5  2.0  3.0     screenshot
#     shown 20.4 18.0 12.7  10.8   9.0  7.3  5.4       10.8 %
# 1.25 texels of Gaussian is very close to one 2x box, i.e. one mip -- so the fit lands where
# the arithmetic says it should, which is what makes it a calibration rather than a knob.
# THE SAME VALUE IS USED IN EVERY PANEL, so nothing about the comparison depends on it.
NORMAL_MIP_SIGMA = 1.25


def _shade_from_normal(nrm_cell, shape, strength=1.0, mip_sigma=NORMAL_MIP_SIGMA):
    """BoardLit's `shade` with the NORMAL MAP in it, which is where most of the cap's visible
    texture actually lives.

    THIS TERM WAS MISSING FROM THE FIRST VERSION OF THIS COMPOSITOR AND THE OMISSION WAS
    MEASURABLE. With a flat `shade` the redrawn field carried 4.3 % relative luminance
    contrast; the screenshot's own field carries 10.8-13.6 %. Two thirds of what a player sees
    on a cap face is not the albedo at all -- it is the key light raking across the plate's
    micro-relief, and `cap_atlas` writes exactly that relief into the normal map
    (`GRAIN_RELIEF`, derived from the plate's own luminance). A picture answering "is this
    texture uniform?" that models only the albedo is an instrument measuring one term of what
    the eye sees, which is this project's own recorded failure mode.

    The basis is EXACT rather than approximate, and it is the mesh's: `CardMesh.KeycapTangent`
    writes (1, 0, 0, -1) on every vertex, so T = +X and B = cross(N, T) * w = +Y, with the
    plateau's N at -Z. Unity imports these as NormalMap textures, so `UnpackNormal` takes x
    from the (DXT5nm) alpha -- which is the red channel of the authored PNG -- and y from
    green; z is reconstructed. `cap_atlas` inverts red relative to that convention on purpose,
    to match every shipped board map, and this reproduces the inversion rather than correcting
    it: the point is to draw what the rig draws.
    """
    h, w = shape
    src = Image.fromarray((np.clip(nrm_cell, 0, 1) * 255).astype(np.uint8), "RGB")
    if mip_sigma:
        src = src.filter(ImageFilter.GaussianBlur(mip_sigma))
    n = np.asarray(src.resize((w, h), Image.Resampling.LANCZOS), dtype=np.float64) / 255.0
    nx = (n[..., 0] * 2.0 - 1.0) * strength
    ny = (n[..., 1] * 2.0 - 1.0) * strength
    nz = np.sqrt(np.clip(1.0 - np.clip(nx * nx + ny * ny, 0.0, 1.0), 0.0, 1.0))
    N = np.stack([nx, ny, -nz], axis=-1)
    N /= np.maximum(np.linalg.norm(N, axis=-1, keepdims=True), 1e-9)
    key = np.array([0.35, 0.85, -0.45]); key /= np.linalg.norm(key)
    fill = np.array([-0.55, 0.35, 0.30]); fill /= np.linalg.norm(fill)
    return (D.AMBIENT + np.clip(N @ key, 0, None) * D.LIGHT_BOOST
            + np.clip(N @ fill, 0, None) * D.FILL_WEIGHT)


def _face_cell(atlas_path, cell_index, state, grid=C.GRID, field=True, normal_path=None):
    """One atlas cell's RECESSED FIELD, rendered as the game draws it:

        texel  x  SeatedCapColor(state x BoardCapTint)  x  shade(normal map)
    """
    cell = _cell_of(atlas_path, cell_index, grid, field)
    if normal_path and os.path.isfile(normal_path):
        shade = _shade_from_normal(_cell_of(normal_path, cell_index, grid, field),
                                   cell.shape[:2])[..., None]
    else:
        shade = D.SHADE
    return np.clip(cell * D.face_colour(state) * shade, 0.0, 1.0)


def on_bronze(bundle, idles, dst=None, cell_index=1, scale=3, out_dir=OUT):
    """Three panels of the user's own screenshot: untouched / the CONTROL / the NEW cap.

    THE CONTROL PANEL IS THE POINT OF THE PICTURE. It redraws the field with the CURRENT
    atlas and the CURRENT single idle colour -- i.e. it should be invisible against panel 1.
    A compositor and a shading model that cannot reproduce the picture they are about to
    change have no business changing it, and this project has shipped three instruments whose
    first output was trusted."""
    dst = dst or os.path.join(DEBUG, "plates_on_bronze.png")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shot = Image.open(SHOT).convert("RGB")
    base = np.asarray(shot, dtype=np.float64) / 255.0
    W, H = shot.size

    r1 = os.path.join(out_dir, "keycap_atlas_bronze_r1.png")   # kept in out/, not in the
    r1n = os.path.join(out_dir, "keycap_normal_bronze_r1.png")  # bundle Unity imports from
    new = os.path.join(bundle, "KeycapBronze_albedo.png")
    newn = os.path.join(bundle, "KeycapBronze_normal.png")
    normals = {r1: r1n, new: newn}
    panels = []
    for label, atlas, state in (
            ("1  ORIGINAL   the user's screenshot, untouched", None, None),
            ("2  CONTROL   round-1 plate x the ACCENT colour the screenshot is in "
             "(0.350, 0.460, 0.280).  This panel should be invisible against panel 1.",
             r1, ACCENT_CONFIRM),
            ("3  NEW MATERIAL, SAME COLOUR   round-2 plate x that same accent colour "
             "-- the texture change on its own", new, ACCENT_CONFIRM),
            ("4  NEW MATERIAL, NEW IDLE   round-2 plate x the solved bronze idle "
             f"({idles['bronze'][0]:.3f}, {idles['bronze'][1]:.3f}, {idles['bronze'][2]:.3f})"
             "  -- what an AVAILABLE key on this board becomes", new, idles["bronze"])):
        img = base.copy()
        if atlas is not None and os.path.isfile(atlas):
            cell = _face_cell(atlas, cell_index, state, normal_path=normals.get(atlas))
            cell = _to_mip(cell, FIELD_QUAD)
            drawn, inside = _bilinear_quad(cell, FIELD_QUAD, (W, H))
            lum = base @ D.REC709
            # Keep the game's own caption: inside the field nothing else is this bright.
            keep = (lum > CAPTION_LUM)
            m = (inside & ~keep)[..., None]
            img = np.where(m, drawn, img)
        panels.append((label, img))

    cw = CROP_BOX[2] - CROP_BOX[0]
    ch = CROP_BOX[3] - CROP_BOX[1]
    tw, th = cw * scale, ch * scale
    n = len(panels)
    head, foot, pad = 46, 34, 8
    sheet = Image.new("RGB", (n * tw + (n + 1) * pad, th + head + foot + 2 * pad), (18, 18, 20))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 10), "THE BRONZE CAP IN ITS OWN WELL  -  .planning/debug/abgeschnitter_text.jpg, "
                      f"crop {CROP_BOX}, {scale}x nearest.", fill=(235, 235, 220))
    d.text((pad, 24), "Only the RECESSED FIELD is redrawn (plate x SeatedCapColor(state x "
                      "BoardCapTint 0.5) x shade 0.8737). The bevel, the recess wall, the "
                      "board and the game's caption are the screenshot's own pixels.",
           fill=(150, 150, 160))
    for i, (label, img) in enumerate(panels):
        crop = Image.fromarray((np.clip(img, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB") \
            .crop(CROP_BOX).resize((tw, th), Image.Resampling.NEAREST)
        x0 = pad + i * (tw + pad)
        sheet.paste(crop, (x0, head + pad))
        d.rectangle([x0, head + pad, x0 + tw - 1, head + pad + th - 1], outline=(70, 70, 78))
        d.text((x0 + 4, head + pad + th + 6), label[:120], fill=(240, 230, 160))
        m, rc = _field_stats(img)
        d.text((x0 + 4, head + pad + th + 20),
               f"field mean {np.round(m, 3).tolist()}   relative contrast {rc:5.1f} %"
               + ("" if i else "   <- the number every other panel is checked against"),
               fill=(170, 170, 180))
    sheet.save(dst)
    return dst


def _field_stats(img, box=(1094, 1116, 1995, 2088)):
    """Mean and RELATIVE LUMINANCE CONTRAST of the field, sampled clear of the caption, the
    carved check and both recess walls -- the box is the band between the check's bottom and
    the caption's top, which is the only part of this cap that is nothing but material."""
    y0, y1, x0, x1 = box
    p = img[y0:y1, x0:x1]
    lum = p.mean(axis=2)
    return p.reshape(-1, 3).mean(axis=0), 100.0 * float(lum.std()) / max(float(lum.mean()), 1e-9)


def structure_report(out_dir=OUT, bundle=None, dst=None):
    """The plate-level and cap-level numbers, appended to the dE report so one file carries
    the whole round. Written by APPEND rather than by rewrite, because `cap_deltae.py` owns the
    first half of that file and duplicating its arithmetic here is how two instruments start
    disagreeing about the same quantity."""
    lines = []

    def say(t=""):
        print(t)
        lines.append(t)

    say("")
    say("=" * 96)
    say("THE PLATES -- round 1 vs round 2, and the structure a cap face actually receives")
    say("=" * 96)
    say("gpt-image-2, every request 1:1, every delivery read back with PIL:")
    for name, why in list(CHOSEN.items()):
        im = Image.open(os.path.join(out_dir, why))
        say(f"  {name:<7} KEPT      {why:<28} returned {im.size[0]}x{im.size[1]}")
    for name, why in DISCARDED.items():
        im = Image.open(os.path.join(out_dir, name))
        say(f"  {'':<7} DISCARDED {name:<28} returned {im.size[0]}x{im.size[1]}   {why}")
    say("  the 3:2 rescale trap did not fire on any of them, and that was read back, not assumed.")

    say("")
    say("  MEAN BEFORE AND AFTER THE LEVEL RE-BASE (cap_atlas.normalise_plate, imported):")
    for style in D.STYLES:
        for tag, suffix in (("round 1", "_r1"), ("round 2", "")):
            path = os.path.join(out_dir, f"keycap_plate_{style}{suffix}.png")
            arr = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
            norm, gain, achieved, _spend = C.normalise_plate(arr)
            say(f"  {style:<7} {tag}  mean {arr.mean():.4f} "
                f"rgb {arr.reshape(-1,3).mean(0).round(4).tolist()}  x gain {gain:.3f}  ->  "
                f"mean {achieved:.4f} rgb {norm.reshape(-1,3).mean(0).round(4).tolist()}")

    if bundle:
        say("")
        say("  AT CAP SCALE -- the built atlas' PLAIN face through its normal map, at the mip the")
        say("  rig samples and the size the cap occupies (106 x 83 px in the user's screenshot).")
        say("  STORY is the low-pass at a tenth of the cap; GRAIN is what it removed. Per cent of")
        say("  the field's own mean, so the two brightnesses are comparable.")
        say("")
        say("  board    round        total      STORY      GRAIN")
        for style in D.STYLES:
            a1 = os.path.join(out_dir, ATLAS_R1.format(style=style))
            n1 = os.path.join(out_dir, NORMAL_R1.format(style=style))
            a2 = os.path.join(bundle, ATLAS_NEW.format(Style=style.capitalize()))
            n2 = os.path.join(bundle, NORMAL_NEW.format(Style=style.capitalize()))
            t1, s1, g1 = cap_scale_stats(a1, n1, D.IDLE_SHIPPED)
            t2, s2, g2 = cap_scale_stats(a2, n2, D.IDLE_SHIPPED)
            say(f"  {style:<7}  round 1    {t1:6.2f} %   {s1:6.2f} %   {g1:6.2f} %")
            say(f"  {style:<7}  round 2    {t2:6.2f} %   {s2:6.2f} %   {g2:6.2f} %"
                f"     STORY x{s2/s1:.2f}, total x{t2/t1:.2f}")

    say("")
    say("  AND THE ONE THING THE COMPOSITOR STILL DOES NOT REPRODUCE, stated rather than buried:")
    say(f"  the control panel of plates_on_bronze.png lands within 0.5 points of the "
        f"screenshot's own field contrast, but only with NORMAL_MIP_SIGMA = {NORMAL_MIP_SIGMA} --")
    say("  a FITTED filtering term standing in for the mip level the rig samples the normal map")
    say("  at. It is the same in every panel, so no comparison depends on it, but the absolute")
    say("  texture level in that picture rests on a fit and not on a derivation.")

    if dst:
        with open(dst, "a", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
    return lines


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ingest", action="store_true", help="install the chosen generated plates")
    ap.add_argument("--sheet", action="store_true", help="write plates_before_after.png")
    ap.add_argument("--bronze", action="store_true", help="write plates_on_bronze.png")
    ap.add_argument("--stats", default=None,
                    help="append the plate/cap-scale structure report to this text file")
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--bundle", default=os.path.join(
        REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table"))
    args = ap.parse_args()

    if args.ingest:
        print("INGEST")
        ingest(args.out)
    if args.sheet:
        print("SHEET   ", before_after(args.out, bundle=args.bundle))
    if args.stats:
        structure_report(args.out, bundle=args.bundle, dst=args.stats)
    if args.bronze:
        best, de, lum_ref, _ = D.solve(
            {s: C.normalise_plate(np.asarray(Image.open(
                os.path.join(args.out, f"keycap_plate_{s}.png")).convert("RGB"),
                dtype=np.float64) / 255.0)[0].reshape(-1, 3).mean(axis=0) for s in D.STYLES},
            None)
        print(f"BRONZE  min pairwise dE {de:.2f}")
        print("BRONZE  ", on_bronze(args.bundle, best))


if __name__ == "__main__":
    main()
