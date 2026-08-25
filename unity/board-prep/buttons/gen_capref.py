#!/usr/bin/env python3
"""Crop each board style's OWN albedo face band into a 1024x1024 palette reference.

WHY THIS EXISTS. The keycaps have to "zum board und seinem Aussehen passen" (user,
2026-08-25). The three boards' materials were authored last round through gpt-image-2 and
they ARE the ground truth for what "oak", "steel" and "bronze" mean on this board set --
so the cap-material generation is anchored to them by passing a crop of the board's own
face band as a reference image, rather than by describing the material in words and hoping
the second description lands in the same place as the first one did.

WHAT IT IS NOT. This crop is a PALETTE AND GRAIN reference for a generation prompt. It is
never composited, never registered against anything, and nothing downstream reads it: the
cap atlas is built from the GENERATED plate. So a later rewrite of the board atlas (the
board-texture lane is editing the back and the rim this round) cannot silently move a cap
texel -- it can only mean the reference was taken from a slightly older interpretation of
the same material, which is a stylistic remark and not a defect.

The face band is `regions.face` of the style's UV layout (u 0..1, v 0..0.5). UV v = 0 is
the BOTTOM of the image and PIL's y = 0 is the TOP, so the band is the lower half of the
file, not the upper -- getting that backwards would sample the frame/seat/side regions and
hand the model a patchwork instead of a plate.
"""
import argparse
import json
import os
import sys

from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
BUNDLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")

# The Identity table of BOARD-CONTRACT.md -- these base names are load-bearing (the mod
# resolves boards by them) and are copied, not invented, from tex_atlas.STYLE_FILES.
STYLE_FILES = {
    "oak": "PlayTray",
    "steel": "PlayTray_9capjqp6",
    "bronze": "PlayTray_16vm268h",
}

DEFAULT_FACE = {"u0": 0.0, "v0": 0.0, "u1": 1.0, "v1": 0.5}


def face_region(style, uv_dir):
    """The style's own `face` region, or the contract default announced loudly."""
    path = os.path.join(uv_dir, f"{style}_uv.json")
    if not os.path.isfile(path):
        print(f"  {style}: no {style}_uv.json -- using the BOARD-CONTRACT default face band "
              f"{DEFAULT_FACE}. A crop against a guessed layout is a guess.")
        return DEFAULT_FACE
    with open(path, "r", encoding="utf-8") as fh:
        data = json.load(fh)
    return data.get("regions", {}).get("face", DEFAULT_FACE)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--uv-dir", default=os.path.join(REPO, "unity", "board-prep", "out"),
                    help="where <style>_uv.json live")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "out"))
    ap.add_argument("--size", type=int, default=1024)
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    for style, base in STYLE_FILES.items():
        src = os.path.join(BUNDLE, f"{base}_albedo.png")
        if not os.path.isfile(src):
            print(f"  {style}: {src} missing -- skipped", file=sys.stderr)
            continue
        im = Image.open(src).convert("RGB")
        w, h = im.size
        reg = face_region(style, args.uv_dir)
        # UV -> pixel. v is measured from the BOTTOM, so the band's pixel rows run
        # h*(1-v1) .. h*(1-v0).
        x0, x1 = int(round(reg["u0"] * w)), int(round(reg["u1"] * w))
        y0, y1 = int(round((1.0 - reg["v1"]) * h)), int(round((1.0 - reg["v0"]) * h))
        band = im.crop((x0, y0, x1, y1))
        bw, bh = band.size
        side = min(bw, bh)
        cx, cy = (bw - side) // 2, (bh - side) // 2
        crop = band.crop((cx, cy, cx + side, cy + side))
        if crop.size[0] != args.size:
            crop = crop.resize((args.size, args.size), Image.LANCZOS)
        dst = os.path.join(args.out, f"ref_face_{style}.png")
        crop.save(dst)
        print(f"  {style}: {os.path.basename(src)} {im.size} face band {band.size} "
              f"-> {os.path.basename(dst)} {crop.size}")


if __name__ == "__main__":
    main()
