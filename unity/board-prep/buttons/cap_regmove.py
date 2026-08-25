#!/usr/bin/env python3
"""WHERE DID THE INSIDE-OUTSIDE ORDER GO? — REG measured on the RENDERED CAP, not on the texture.

    python3 unity/board-prep/buttons/cap_regmove.py

THE PROBLEM THIS EXISTS TO STOP SOMEBODY WALKING INTO
------------------------------------------------------
`plate_forensics.py` measures REG — mean luminance against distance-to-border, peak-to-trough, as
a per cent of the mean — on the ATLAS CELL. Round 3 raised it from 3.7/9.9/3.8 to 28.3/35.3/23.7
by painting a registered bezel into the cell, and was rejected anyway.

Round 4 takes the bezel OUT of the cell and builds it as geometry, so the cell is deliberately a
flat material sample and its REG **falls**, to 13.0/10.5/2.2. Read as a verdict that is a
catastrophic regression. Read correctly it is the instrument pointed one stage too early: the cell
is no longer the thing that carries the order, and a good instrument on the wrong stage looks
exactly like proof.

So the same function is applied to the thing a player actually sees — the cap RENDERED, square-on,
alone, filling the frame — for both constructions, with the SAME atlas underneath both. That
isolates the question to: does the cap still have an inside-outside order, and does it have MORE
of one than the plain slab it replaces?

The controls are `plate_forensics.py`'s own and are quoted rather than re-derived:
    a stationary noise swatch (the null)   REG ~17-20
    the accepted board backs               REG 57-107
"""
import os
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cap_options as CO                                              # noqa: E402
import plate_forensics as PF                                          # noqa: E402

BLENDER = os.environ.get("BLENDER", "/home/claw/blender-4.2/blender")
SHOTS = os.path.join(CO.DEBUG, "capshots")
MESHES = os.path.join(CO.DEBUG, "meshes")


def shoot(style, which):
    dst = os.path.join(SHOTS, f"{style}_{which}.png")
    subprocess.run([BLENDER, "--background", "--python",
                    os.path.join(HERE, "cap_onboard.py"), "--",
                    style, which, MESHES, dst, "--capshot", "--res", "512"],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return dst


def reg_of(path):
    """REG over the cap's OWN FOOTPRINT, cropped by its alpha silhouette.

    THE FIRST VERSION MEASURED THE FRAME AND NOT THE CAP, and it produced numbers that looked
    authoritative and meant nothing: 133 / 124 / 196, barely moving between the two
    constructions. `registration` defines distance-to-border against the FRAME, on the premise
    that the frame is the object — true of an atlas cell by construction, and false of a render
    with any background in it. With black around the cap, the d~0 bins were pure background and
    every score was really "cap against void", which of course does not care what the bezel is
    doing.

    Cropping to the alpha bbox restores the premise: the frame becomes the cap's footprint,
    exactly as the cell is.
    """
    with Image.open(path) as im:
        im = im.convert("RGBA")
        bbox = im.getchannel("A").point(lambda v: 255 if v > 8 else 0).getbbox()
        if bbox is None:
            raise SystemExit(f"{path}: nothing rendered")
        im = im.crop(bbox)
        a = np.asarray(im.convert("RGB"), dtype=np.float64) / 255.0
    # Rec.709 luma, the same conversion plate_forensics uses.
    lum = a @ np.array([0.2126, 0.7152, 0.0722])
    return PF.registration(lum)[0], im.size


def main():
    os.makedirs(SHOTS, exist_ok=True)
    print("REG on the RENDERED cap (square-on, alone, same atlas under both)")
    print("  controls, from plate_forensics.py: null noise swatch ~17-20, "
          "accepted board backs 57-107\n")
    print(f"  {'board':8s} {'BEFORE (289 slab)':>18s} {'AFTER (round 4)':>17s} {'change':>10s}"
          f"   crop px")
    for s in CO.STYLES:
        b, bs = reg_of(shoot(s, "before"))
        a, asz = reg_of(shoot(s, "after"))
        print(f"  {s:8s} {b:18.1f} {a:17.1f} {a - b:+9.1f}   {bs[0]}x{bs[1]} / {asz[0]}x{asz[1]}")


if __name__ == "__main__":
    main()
