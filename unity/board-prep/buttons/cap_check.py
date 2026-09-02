#!/usr/bin/env python3
"""The acceptance instrument for the keycap atlases and the engraving palette.

TWO JOBS, and they are separate because they fail separately.

1. `--symbols` -- DOES THE CARVE READ? A symbol is legible on this surface if the groove is
   darker than the material around it by enough to survive the shader, and if its narrowest
   limb is wider than the chamfer that eats into it from both sides. Both are measured per
   cell, per style, against the cell's OWN material rather than against a global mean --
   the bronze plate is mottled and a global mean would let a symbol sitting on a bright
   patch pass on the strength of a dark patch somewhere else.

   THE NULL INPUT AND THE POSITIVE CONTROL, because a new instrument's first output is a
   hypothesis: `--selfcheck` runs the same measurement over the PLAIN cell (no symbol at
   all -- the null: contrast must come out at the material's own noise floor) and over a
   synthetic disc carved by the same `cap_atlas.carve` (the positive: it must fire).

2. `--palette` -- WHAT COLOUR IS A CUT IN THIS BOARD? Prints the per-style groove-floor,
   lit-lip and keyline colours the engraved board text is styled with
   (`src/GloomhavenVR/Cards/BoardEngraving.cs`). Those three are AUTHORED CONSTANTS in C#
   because a bundled texture is imported non-readable and the plugin cannot sample it at
   runtime; this is where the numbers come from, so they can be re-derived rather than
   re-guessed if the plates are ever re-generated.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))

import cap_atlas as A          # noqa: E402
import tex_common as T         # noqa: E402
import tex_symbols as S        # noqa: E402

# The band a cut is expected to land in, as a fraction of the surrounding material's
# luminance. Below FLOOR the groove is not separable from the material's own variation;
# above CEIL it stops reading as a cut in this material and starts reading as a black
# sticker on it.
CONTRAST_FLOOR = 0.12
CONTRAST_CEIL = 0.75

# THE GATE IS THE OUTCOME AND NOT THE MECHANISM, and the first version of this file got
# that wrong in a way worth writing down. It gated on LIMB WIDTH against twice the chamfer
# -- "a limb thinner than the chamfer eating it from both sides never reaches the groove
# floor" -- and failed 19 of 24 cells. Two things were wrong with it. The chamfer is a
# SMOOTHSTEP, not a ramp, so a limb at 79 % of that width still reaches 90 % of full depth;
# and, much more to the point, depth is a property of the mechanism while what a player
# needs is CONTRAST ON SCREEN. Every one of those 19 "failures" was measuring 35-46 %
# contrast at the same time it failed. The limb and its achieved depth are still printed,
# as diagnostics that explain a low contrast when there is one -- they just do not decide.
#
# What DOES decide is the drop measured after resampling the cell to the size the cap
# actually occupies in the headset, because that is the term an atlas-space measurement
# cannot see: a limb 2.8 texels wide in a 256 cell is 1.1 pixels at arm's length and part
# of it is simply gone.
#
# THE TWO SIZES. The fitted cap is 63 mm at most and the board's world scale runs 0.42-0.88
# (BOARD-REBUILD-HANDOVER's footprint table), so the cap is 26-55 mm in world. A Quest 3
# over Virtual Desktop renders about 20 px/degree. At 0.5 m -- the board pulled in to read
# -- a 40 mm cap subtends 4.6 degrees, i.e. ~96 px. At 1.5 m -- the board across the table
# -- 1.5 degrees, i.e. ~32 px. Both are reported; the gate is the NEAR one, because a
# symbol that has faded back into its own surface at 1.5 m is behaving like a real carving
# and the caption is what carries the meaning at that range anyway.
SCREEN_NEAR_PX = 96
SCREEN_FAR_PX = 32

# Weber-ish floor: the smallest luminance ratio still separable on a mid-toned, textured
# surface at speed. Deliberately not a JND (which would be ~1 %) -- this has to be readable
# at a glance while the player is doing something else, not detectable under study.
SCREEN_DROP_FLOOR = 0.08


def rim_px(cell_px):
    """The chamfer width `cap_atlas.carve` cuts with, in texels."""
    return max(1.0, A.RIM_FRAC * cell_px)


def achieved_depth(limb, cell_px):
    """How much of the full groove depth a limb of this width actually reaches -- the
    mechanism number the old gate got wrong by treating the chamfer as linear."""
    t = min(1.0, (limb * 0.5) / rim_px(cell_px))
    return t * t * (3.0 - 2.0 * t)


def downsample(rgb, cov, px):
    """The cell and its coverage as the eye gets them at `px` across."""
    a = np.asarray(
        Image.fromarray((np.clip(rgb, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB")
        .resize((px, px), Image.Resampling.BOX), dtype=np.float64) / 255.0
    c = np.asarray(
        Image.fromarray((np.clip(cov, 0, 1) * 255.0 + 0.5).astype(np.uint8), "L")
        .resize((px, px), Image.Resampling.BOX), dtype=np.float64) / 255.0
    return a, c


def cell_slice(img, idx, cell_px, cols):
    r, c = idx // cols, idx % cols
    return img[r * cell_px:(r + 1) * cell_px, c * cell_px:(c + 1) * cell_px]


def measure(alb_cell, cov):
    """Contrast of the cut against the material immediately around it.

    The reference is a RING around the symbol, not the whole cell: a mottled plate has
    bright and dark patches, and comparing a groove to the cell mean lets a symbol sitting
    on a bright patch borrow darkness from somewhere it is not.
    """
    solid = cov >= 0.5
    if not solid.any():
        return None
    lum = alb_cell.mean(axis=2)
    d_out = T.edt(solid)
    ring = (d_out > 2) & (d_out < 14)
    if not ring.any():
        ring = ~solid
    inside = float(lum[solid].mean())
    around = float(lum[ring].mean())
    if around <= 1e-6:
        return None
    return dict(inside=inside, around=around, drop=(around - inside) / around,
                limb=float(np.median(T.edt(~solid)[solid]) * 2.0))


def symbols(args):
    cols, rows = A.GRID
    bad = 0
    sheet_masks, _ = A.load_sheet_masks(args.sheet)
    for style in args.styles:
        base = A.STYLE_FILES[style]
        path = os.path.join(args.atlas_dir, f"{base}_albedo.png")
        if not os.path.isfile(path):
            print(f"{style}: {path} MISSING")
            bad += 1
            continue
        alb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
        cell_px = alb.shape[1] // cols
        print(f"\n{style}: {alb.shape[1]}x{alb.shape[0]}, cell {cell_px}, "
              f"chamfer {rim_px(cell_px):.1f}px  (drop = how much darker the cut is than the "
              f"material around it; the gate is the @{SCREEN_NEAR_PX}px column)")
        for spec in A.CELLS:
            if spec["src"] is None:
                continue
            # Through cap_atlas's OWN resolver, not a copy of it. This line used to be a
            # two-way branch over ("sheet", ...) / ("motif", ...) and it silently reported every
            # DRAWN cell as SOURCE MISSING the moment that kind was added -- the instrument
            # failing the atlas rather than the other way round.
            mask = A.resolve_mask(spec["src"], style, sheet_masks, args.motifs)
            if mask is None:
                print(f"    [{spec['idx']}] {spec['role']:<12} SOURCE MISSING")
                bad += 1
                continue
            size = A.SOLO_SIZE if spec["layout"] == "solo" else A.TEXT_SIZE
            cy = A.SOLO_CY if spec["layout"] == "solo" else A.TEXT_CY
            cov = A.place(mask, cell_px, size, cy)
            cell = cell_slice(alb, spec["idx"], cell_px, cols)
            m = measure(cell, cov)
            if m is None:
                print(f"    [{spec['idx']}] {spec['role']:<12} EMPTY")
                bad += 1
                continue
            near = measure(*downsample(cell, cov, SCREEN_NEAR_PX))
            far = measure(*downsample(cell, cov, SCREEN_FAR_PX))
            n_drop = near["drop"] if near else 0.0
            f_drop = far["drop"] if far else 0.0
            ok = (CONTRAST_FLOOR <= m["drop"] <= CONTRAST_CEIL
                  and n_drop >= SCREEN_DROP_FLOOR)
            bad += 0 if ok else 1
            why = "" if ok else (" <- atlas contrast" if not (CONTRAST_FLOOR <= m["drop"] <= CONTRAST_CEIL)
                                 else " <- unreadable at arm's length")
            print(f"    [{spec['idx']}] {spec['role']:<12} atlas {m['drop'] * 100:5.1f}%  "
                  f"@{SCREEN_NEAR_PX}px {n_drop * 100:5.1f}%  @{SCREEN_FAR_PX}px {f_drop * 100:5.1f}%  "
                  f"| limb {m['limb']:4.1f}px reaching {achieved_depth(m['limb'], cell_px) * 100:3.0f}% depth  "
                  f"{'ok' if ok else 'FAIL'}{why}")
    return bad


def selfcheck(args):
    """NULL INPUT and POSITIVE CONTROL for `measure`, before any of its numbers are believed."""
    cell = 256
    plate = np.asarray(
        Image.open(os.path.join(args.plates, "keycap_plate_oak.png")).convert("RGB"),
        dtype=np.float64)[:cell, :cell] / 255.0

    # NULL: no symbol. `measure` must refuse to report at all (nothing is solid).
    null = A.carve(plate, np.zeros((cell, cell)), cell)[0]
    got = measure(null, np.zeros((cell, cell)))
    print(f"  NULL   (plain cell, no symbol): {'refused to measure -- correct' if got is None else f'REPORTED {got} -- WRONG'}")

    # NULL 2: a symbol-shaped mask measured against a plate it was never carved into. The
    # drop must land at the material's own noise floor, i.e. well under CONTRAST_FLOOR.
    yy, xx = np.mgrid[0:cell, 0:cell]
    disc = ((yy - cell / 2) ** 2 + (xx - cell / 2) ** 2) < (0.22 * cell) ** 2
    flat = measure(plate, disc.astype(float))
    print(f"  NULL2  (uncarved plate, disc mask): drop {flat['drop'] * 100:5.2f}% "
          f"-- {'below the floor, correct' if abs(flat['drop']) < CONTRAST_FLOOR else 'ABOVE THE FLOOR -- the instrument fires on nothing'}")

    # POSITIVE: the same disc carved by the same recipe. It must fire.
    carved = A.carve(plate, disc.astype(float), cell)[0]
    hit = measure(carved, disc.astype(float))
    print(f"  POSITIVE (same disc, carved):     drop {hit['drop'] * 100:5.2f}% "
          f"limb {hit['limb']:.1f}px -- {'fires, correct' if hit['drop'] >= CONTRAST_FLOOR else 'DID NOT FIRE -- the instrument is blind'}")
    return 0


def palette(args):
    """The three engraving colours per style, and the arithmetic that produced them.

    THE REFERENCE IS THE BOARD'S OWN FACE BAND (`gen_capref.py`'s ref_face_<style>.png), not the
    keycap plate. The first version used the cap plate, on the reasoning that a board and its keys
    are the same material family -- true to within 4-6 % for oak and bronze, and wrong by a factor
    of 0.68 for STEEL, whose cap is dark blued iron while its board face is bright brushed silver.
    An engraving is cut INTO the board, so the board is the only candidate; the cap-derived numbers
    would have put a 70 % drop where 55 % was meant and made the "lit lip" DARKER than the surface
    it is supposed to be catching light against, inverting the one cue that says the mark is a cut.
    """
    # THE MULTIPLIERS MOVED, 2026-09-03 (user request 5: "Gewaehrleiste, dass der Text lesbar
    # ist auf den Boards ... Es soll immersiv sein weiterhin und gut aussehen, aber lesbar
    # sein"). Measured off his text-board.jpg BEFORE the change: the three engraved captions ran
    # at 1.64-1.96:1 groove-vs-stone with a carve internal step of only 2.46-3.59:1, every one of
    # them under the 3:1 floor. Glyph-vs-stone is CAPPED at ~3.2:1 by how dark the board itself
    # renders, so the only lever left is the carve's OWN dark-to-light step: a deeper groove and
    # a brighter chamfer. The derivation lives on BoardEngraving.GrooveFloor. If these three
    # numbers and that file's literals ever disagree, the file is what ships and this print is
    # the thing that is wrong.
    GROOVE, LIP, KEY = 0.13, 1.60, 0.07
    print(f"style      board face mean RGB    groove x{GROOVE:<12.2f} lit lip x{LIP:<12.2f} keyline x{KEY:.2f}")
    for style in args.styles:
        p = os.path.join(args.refs, f"ref_face_{style}.png")
        a = np.asarray(Image.open(p).convert("RGB"), dtype=np.float64) / 255.0
        mean = a.reshape(-1, 3).mean(axis=0)

        def fmt(v):
            return "(" + ", ".join(f"{x:.3f}" for x in np.clip(v, 0, 1)) + ")"

        print(f"{style:<10} {fmt(mean)}  {fmt(mean * GROOVE)}  {fmt(mean * LIP)}  {fmt(mean * KEY)}")
    print("\nThese are the literals in src/GloomhavenVR/Cards/BoardEngraving.cs. They are AUTHORED")
    print("there, not sampled at runtime: the board atlases import with isReadable = 0, so the")
    print("plugin cannot call GetPixel on one, and making a 4 MB texture readable to recover three")
    print("colours is not a trade worth making. Re-run this whenever the BOARD atlases change --")
    print("the board-texture lane owns those, and this palette follows them, not the caps.")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--atlas-dir", default=A.BUNDLE)
    ap.add_argument("--plates", default=os.path.join(os.path.dirname(HERE), "out"))
    ap.add_argument("--refs", default=os.path.join(HERE, "out"),
                    help="where gen_capref.py wrote ref_face_<style>.png — the BOARD's own "
                         "face band, which is what the engraving palette is derived from")
    ap.add_argument("--motifs", default=os.path.join(os.path.dirname(HERE), "out"))
    ap.add_argument("--sheet", default=os.path.join(os.path.dirname(HERE), "out",
                                                    "keycap_symbols_sheet.png"))
    ap.add_argument("--styles", nargs="*", default=list(A.STYLE_FILES))
    ap.add_argument("--symbols", action="store_true")
    ap.add_argument("--selfcheck", action="store_true")
    ap.add_argument("--palette", action="store_true")
    args = ap.parse_args()
    if not (args.symbols or args.selfcheck or args.palette):
        args.symbols = args.selfcheck = args.palette = True

    rc = 0
    if args.selfcheck:
        print("INSTRUMENT CHECK -- null input and known-positive control")
        rc |= selfcheck(args)
    if args.palette:
        print("\nENGRAVING PALETTE")
        rc |= palette(args)
    if args.symbols:
        print("\nSYMBOL LEGIBILITY (groove vs the material immediately around it)")
        n = symbols(args)
        if n:
            print(f"\n{n} cell(s) outside the accepted band")
        rc |= 1 if n else 0
    return rc


if __name__ == "__main__":
    sys.exit(main())
