#!/usr/bin/env python3
"""Rebuild the environment picker's DEFAULT tile:
src/GloomhavenVR/WorldUI/Options/VariantTiles/tile_env_default.png

WHY IT WAS REBUILT
------------------
The other four environment tiles are composed two ways: Keller and Nachtwald are crops of a real
room (`EnvironmentsPreview` renders), and **Mixed Reality and Off (black) are the SAME play-tray
render, centred, over a chosen backdrop**. Default was neither — it was an ffmpeg frame grab of
`docs/img/control-board.mp4` cropped to whatever the capture happened to show: an off-centre
scenario shot with ruins, a party and an element token, and no board in the middle. Beside MR and
Off it did not read as the same kind of picture, which is what the author reported.

This script composes Default the way MR and Off are composed: **the surround behind, the board in
the middle.**

WHERE EACH LAYER COMES FROM — both from files this repository already tracks:

  the surround   a crop of the game's own scenario sky (`GH_SkySphere`, shader `AMP_SkyShader`)
                 taken from `docs/img/control-board.mp4`. There is no asset to composite instead:
                 `SkyAlternative.cs:568-575` gives Default a NULL bundle path because the mod owns
                 no sky for it — Default IS the base game's sky, and the only way to picture it is
                 to photograph the game rendering it. So the frame grab stays; what changes is that
                 a region of PURE SURROUND is used, instead of a window onto the diorama.

  the board      keyed straight out of `tile_env_offblack.png`, whose backdrop is pure black. That
                 is deliberate over re-rendering the tray in Blender: keying the shipped tile
                 guarantees the board sits at the SAME pose, the SAME size and the SAME place as on
                 the MR and Off tiles, which is the whole point of the change. Re-rendering would
                 re-derive all three and could drift from them by a pixel.

THE ONE LIFT, AND WHY IT IS NOT "RE-GRADING A ROOM"
--------------------------------------------------
The raw surround is near-black (mean ~12/255). Left raw, the Default tile would be a black
rectangle with a board on it — which is the OFF (BLACK) tile, and `.planning/variant-tiles.md:44-46`
already records why a tile must never look like that ("exactly what a tile whose art failed to load
looks like"). The lift below is what makes Default distinguishable FROM Off at 320x240 in a
headset. It is a UI thumbnail, not an environment preview: the standing "never re-light a room to
make a screenshot behave" ruling is about `EnvironmentsPreview` renders that are judged as the
product, and this is not one.

Usage:  python3 unity/asset-preview/build_variant_tile_default.py
Needs:  ffmpeg on PATH, Pillow.
"""

import os
import subprocess
import sys
import tempfile

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CLIP = os.path.join(ROOT, "docs", "img", "control-board.mp4")
TILES = os.path.join(ROOT, "src", "GloomhavenVR", "WorldUI", "Options", "VariantTiles")
OUT = os.path.join(TILES, "tile_env_default.png")

# The frame, and the window on it that is ENTIRELY surround. The clip is 960x540; the diorama's
# top edge and the first tooltip both start below/right of this box — checked frame by frame, not
# assumed. 4:3 so nothing is ever stretched.
FRAME_AT = "6.0"
CROP = (240, 0, 453, 160)

WORK = (640, 480)      # composed at 2x and downsampled, so the tray edge stays clean
TILE = (320, 240)


def surround():
    with tempfile.TemporaryDirectory() as tmp:
        png = os.path.join(tmp, "frame.png")
        subprocess.run(["ffmpeg", "-v", "error", "-ss", FRAME_AT, "-i", CLIP,
                        "-frames:v", "1", png], check=True)
        im = Image.open(png).convert("RGB").crop(CROP)

    im = im.resize(WORK, Image.LANCZOS).filter(ImageFilter.GaussianBlur(2.2))
    im = ImageEnhance.Brightness(im).enhance(2.9)
    im = ImageEnhance.Contrast(im).enhance(1.12)

    # A soft centre glow, so the eye lands on the board rather than on a tree trunk. Same job the
    # MR tile's lighter middle does; drawn rather than lit, because there is nothing here to light.
    glow = Image.new("L", WORK, 0)
    gx, gy, r = WORK[0] // 2, int(WORK[1] * 0.56), int(WORK[0] * 0.46)
    Image.Image.paste(glow, Image.new("L", (2 * r, 2 * r), 255),
                      (gx - r, gy - r), _radial(r))
    glow = glow.filter(ImageFilter.GaussianBlur(70))
    return Image.composite(ImageEnhance.Brightness(im).enhance(1.5), im, glow)


def _radial(r):
    m = Image.new("L", (2 * r, 2 * r), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(m).ellipse([0, 0, 2 * r, 2 * r], fill=255)
    return m


def board():
    """The tray, keyed off the pure-black backdrop of the Off tile."""
    src = Image.open(os.path.join(TILES, "tile_env_offblack.png")).convert("RGB")
    src = src.resize(WORK, Image.LANCZOS)
    lo, hi = 10, 34
    alpha = src.convert("L").point(lambda v: 0 if v <= lo else (255 if v >= hi else
                                                               int(255 * (v - lo) / (hi - lo))))
    alpha = alpha.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
    out = src.convert("RGBA")
    out.putalpha(alpha)
    return out


def main():
    if not os.path.isfile(CLIP):
        sys.exit("missing %s" % CLIP)
    bg = surround()

    # the contact shadow first, then the tray on top of it
    tray = board()
    shadow = Image.new("L", WORK, 0)
    shadow.paste(tray.split()[3], (0, 26))
    shadow = shadow.filter(ImageFilter.GaussianBlur(22)).point(lambda v: int(v * 0.55))
    bg = Image.composite(Image.new("RGB", WORK, (0, 0, 0)), bg, shadow)

    # Composite by hand. `tile_env_offblack.png` is the tray ALREADY over black, i.e. its RGB is
    # premultiplied by the alpha we just keyed out of it; PIL's alpha_composite expects STRAIGHT
    # alpha and would dim the tray against a non-black backdrop. bg*(1-a) + premultiplied is the
    # arithmetic that actually holds here, and it is why the board looks identical on all three
    # tiles instead of a little darker on this one.
    a = np.asarray(tray.split()[3], dtype=np.float32)[:, :, None] / 255.0
    comp = np.asarray(bg, dtype=np.float32) * (1.0 - a) \
        + np.asarray(tray.convert("RGB"), dtype=np.float32)
    out = Image.fromarray(np.clip(comp, 0, 255).astype("uint8"), "RGB").resize(TILE, Image.LANCZOS)
    out.save(OUT, optimize=True)
    print("wrote %s (%d x %d, %d kB)" % (OUT, out.width, out.height, os.path.getsize(OUT) // 1024))


if __name__ == "__main__":
    main()
