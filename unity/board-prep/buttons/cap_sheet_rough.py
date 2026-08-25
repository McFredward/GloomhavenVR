#!/usr/bin/env python3
"""ROUND 6's THREE SHEETS -- roughness, the round cap's own rim, and the whole boards.

    python3 cap_sheet_rough.py            # after rendering with render pass below

Both columns come out of `cap_onboard.py` in the same run, at the same camera, under the same
two baked studio directions, with the SAME meshes -- and, for the first time in this directory,
with the cap's `_BumpMap` BOUND on both sides (`--capnormal`). That last point is the whole
reason these sheets exist rather than a rerun of round 4's:

    Round 4 and round 5 rendered the caps from their geometric normals only. The material's
    micro-grain lives ENTIRELY in the normal map. So every picture those rounds argued over had
    the noisy term switched off, while the user was looking at exactly that term.

Exactly two things differ between the columns and both are named on the sheet:

    * the ATLAS -- round 5's (`.planning/debug/round6/atlas_round5/`) against round 6's;
    * WHICH PLAIN CELL a round cap's bevel and walls sample -- cell 0, the SQUARE-registered one
      every cap took until now, against cell 9, the round one.

The state colours are IDENTICAL in both columns (`cap_onboard.IDLE`, round 5's solved values, in
both passes), because round 6 changes no colour. A before/after that also moved the colour would
have credited a roughness fix with a colour change and vice versa.
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DEBUG = os.path.join(REPO, ".planning", "debug", "round6")
ONBOARD = os.path.join(DEBUG, "onboard")

BG, FG, DIM, ACC = (26, 26, 28), (238, 238, 232), (150, 150, 146), (232, 196, 96)
STYLES = ("oak", "steel", "bronze")


def _font(px, bold=False):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf"
              % ("-Bold" if bold else ""),
              "/usr/share/fonts/truetype/liberation/LiberationSans%s.ttf"
              % ("-Bold" if bold else "-Regular")):
        if os.path.exists(p):
            return ImageFont.truetype(p, px)
    return ImageFont.load_default()


def sheet(dst, kind, title, subtitle, rows, tile=620, pad=22):
    """One before/after grid: three boards down, two columns across."""
    head, lab = 96, 58
    W = pad * 3 + tile * 2
    H = head + (tile + lab + pad) * len(STYLES) + pad
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.text((pad, 20), title, font=_font(30, True), fill=FG)
    d.text((pad, 60), subtitle, font=_font(17), fill=DIM)
    for r, style in enumerate(STYLES):
        y = head + r * (tile + lab + pad)
        for c, col in enumerate(("before", "after")):
            x = pad + c * (tile + pad)
            p = os.path.join(ONBOARD, f"{style}_{kind}_{col}.png")
            if os.path.exists(p):
                src = Image.open(p).convert("RGB")
                s = min(tile / src.width, tile / src.height)
                src = src.resize((int(src.width * s), int(src.height * s)), Image.LANCZOS)
                im.paste(src, (x + (tile - src.width) // 2, y + (tile - src.height) // 2))
            else:
                d.text((x + 10, y + 10), f"MISSING {os.path.basename(p)}", font=_font(16),
                       fill=(200, 90, 90))
            d.text((x, y + tile + 6),
                   f"{style.upper()}  —  {'ModBuild 291 (shipped)' if col == 'before' else 'round 6'}",
                   font=_font(19, True), fill=FG if col == "after" else DIM)
        d.text((pad, y + tile + 32), rows[style], font=_font(16), fill=ACC)
    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    im.save(dst)
    print(f"wrote {dst}  {im.size[0]}x{im.size[1]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("-o", "--outdir", default=DEBUG)
    a = ap.parse_args()

    # The numbers are `cap_rough.py --report`'s, quoted so the picture and the measurement cannot
    # be read apart. cap/board combined grain contrast, at 2.4 px/mm.
    sheet(os.path.join(a.outdir, "round6_roughness.png"), "sq",
          "ROUND 6 · THE CAPS ARE NOT SANDPAPER ANY MORE",
          "cap _BumpMap BOUND in both columns — round 4 and 5 rendered without it, "
          "so their sheets had the noisy term switched off. Colour and meshes identical.",
          {"oak": "cap / board grain contrast, combined:  5.16x  ->  0.93x     "
                  "(albedo 0.93x unchanged · relief 15.9x -> 0.88x)",
           "steel": "cap / board grain contrast, combined:  7.61x  ->  1.00x     "
                    "(albedo 1.03x -> 1.00x · relief 45.5x -> 0.91x)",
           "bronze": "cap / board grain contrast, combined: 10.36x  ->  1.26x     "
                     "(albedo 1.51x -> 1.27x · relief 53.7x -> 1.01x)"})

    sheet(os.path.join(a.outdir, "round6_round_caps.png"), "rest",
          "ROUND 6 · THE ROUND CAP STOPS WEARING A SQUARE RIM",
          "the two rest discs — a control that appeared in NO on-board render of round 4 "
          "or 5: this renderer only ever placed the square cap.",
          {s: "bevel + walls:  cell 0, SQUARE-registered  ->  cell 9, ROUND-registered "
              "(CapRole.PlainRound)" for s in STYLES})

    sheet(os.path.join(a.outdir, "round6_boards.png"), "board",
          "ROUND 6 · BOTH SHAPES, ON ALL THREE BOARDS",
          "the whole tray: three square caps on the right, two round rest discs on the left.",
          {s: "" for s in STYLES}, tile=700)


if __name__ == "__main__":
    main()
