#!/usr/bin/env python3
"""The ROUND 4 contact sheet: every generated button option, beside the board it was drawn for.

WHY THE BOARD IS ON THE SHEET, and it is not decoration
--------------------------------------------------------
The question this round has to answer is not "is this a nice button". It is the user's own,
which is RELATIONAL:

    "Sie passt ueberhaupt nicht zu dem jeweiligen board."

A sheet of buttons on grey cannot be judged against that. A sheet of buttons WITH THE BOARD
BESIDE THEM can be, in one glance and without reading a filename -- which is the requirement
this layout exists to satisfy. Each row is one board: its own render on the left at the same
size in every row, its six options on the right.

    python3 unity/board-prep/buttons/cap_sheet_options.py [-o path.png]
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import cap_options as CO                                              # noqa: E402

OUT = CO.OUT
BOARDS = CO.BOARDS
DEBUG = CO.DEBUG

# The six cells of every sheet, in the order the ask lays them out: top row round, bottom row
# square, left to right. The ask is in `cap_options.PROMPTS` and it is what makes this indexing
# safe rather than assumed -- and `--verify` re-derives it from the image instead of trusting it.
CELLS = ["R1", "R2", "R3", "S1", "S2", "S3"]

# WHAT EACH OPTION ACTUALLY IS. Written after LOOKING at the sheets, not predicted from the
# prompt -- the prompt asked for six constructions and getting six is a result, not a given.
# These strings are the labels on the sheet and they are also the record of what was on offer.
NOTES = {
    "oak": {
        "R1": "rolled rim, incised ring, flat face",
        "R2": "stepped double terrace",
        "R3": "rolled-over lip, dished face",
        "S1": "clipped joinery corners, incised border",
        "S2": "clipped corners + four square pegs",
        "S3": "DENTIL RING - the board's own border, at cap scale",
    },
    "steel": {
        "R1": "plain seated plug, single thin chamfer",
        "R2": "flanged base, slotted screws, stepped crown",
        "R3": "rolled lip on a wider flange plate",
        "S1": "rounded square, four dome rivet heads",
        "S2": "keyed retaining ring on a square plate",
        "S3": "rounded square, four slotted corner screws",
    },
    "bronze": {
        "R1": "rolled rim, single incised ring",
        "R2": "stepped triple terrace, cast",
        "R3": "domed crown over a collar",
        "S1": "rounded square, four corner dome bosses",
        "S2": "raised inner plateau + four dome bosses",
        "S3": "rolled lip with keyed retaining notches",
    },
}

# The picks, and every one of them is a claim about the BOARD rather than about the button.
# Recorded here so the sheet SAYS which two were taken and the reader can disagree with a
# reason. `cap_options.CHOSEN` carries the same pair machine-readably.
PICKS = {
    "oak":    ("R2", "S3"),
    "steel":  ("R2", "S1"),
    "bronze": ("R2", "S2"),
}

BG = (26, 26, 28)
FG = (238, 238, 232)
DIM = (150, 150, 146)
PICK = (232, 196, 96)


def _font(size, bold=False):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf"
              % ("-Bold" if bold else ""),
              "/usr/share/fonts/truetype/liberation/LiberationSans%s.ttf"
              % ("-Bold" if bold else "-Regular")):
        if os.path.isfile(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def _wrap(d, text, font, width):
    """Greedy word wrap MEASURED against the font, never a character count.

    Returns every word; there is no path here that drops one. If a single word is itself wider
    than the box it gets its own line and overflows visibly rather than being cut -- the same
    rule `CapFaceLayout` applies to a caption that genuinely cannot fit.
    """
    words, lines, cur = text.split(), [], ""
    for w in words:
        trial = (cur + " " + w).strip()
        if cur and d.textlength(trial, font=font) > width:
            lines.append(cur)
            cur = w
        else:
            cur = trial
    if cur:
        lines.append(cur)
    return lines


def build(dst, sheet_w=1290, thumb=560, pad=34):
    rows = []
    for style in CO.STYLES:
        sp = os.path.join(OUT, CO.GENERATED[style])
        bp = os.path.join(BOARDS, f"board_{style}.png")
        for p in (sp, bp):
            if not os.path.isfile(p):
                raise SystemExit(f"missing {p}")
        rows.append((style, sp, bp))

    sheet_h = int(sheet_w * 1024 / 1536)
    cell_w, cell_h = sheet_w // 3, sheet_h // 2
    head = 78
    label = 46
    row_h = head + max(sheet_h + label, thumb) + pad

    W = pad * 2 + thumb + pad + sheet_w
    H = 96 + row_h * 3
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)

    f_title = _font(40, True)
    f_style = _font(38, True)
    f_cell = _font(23, True)
    f_note = _font(20)
    f_small = _font(19)

    d.text((pad, 26), "Board buttons, round 4 — options generated against each board",
           font=f_title, fill=FG)
    d.text((pad, 70), "gpt-image-2, shown the board itself. Top row of each sheet: ROUND. "
                      "Bottom row: SQUARE.  ★ = the two taken forward.",
           font=f_small, fill=DIM)

    y = 108
    for style, sp, bp in rows:
        d.text((pad, y + 6), style.upper(), font=f_style, fill=FG)
        d.text((pad + 200, y + 18), {
            "oak":    "square dentil border · sharp corners · carved joinery · no metal",
            "steel":  "riveted plate · dentils with rivet heads · plate on plate",
            "bronze": "rounded everything · bead border + dome bosses · rope interlace",
        }[style], font=f_small, fill=DIM)
        yy = y + head

        # CROP THE BOARD TO ITS OWN CONTENT FIRST. `render_asset.py` writes the board small in a
        # square frame on black, so pasting it as-is spends most of a 560 px tile on background
        # and the board -- the whole reason this column exists -- ends up smaller than the
        # buttons it has to be compared against.
        with Image.open(bp) as b:
            b = b.convert("RGB")
            bbox = b.point(lambda v: 255 if v > 10 else 0).convert("L").getbbox()
            if bbox:
                b = b.crop(bbox)
            b.thumbnail((thumb, thumb), Image.LANCZOS)
            tile = Image.new("RGB", (thumb, thumb), BG)
            tile.paste(b, ((thumb - b.size[0]) // 2, (thumb - b.size[1]) // 2))
        im.paste(tile, (pad, yy))
        d.text((pad, yy + thumb + 6), f"the {style} board", font=f_small, fill=DIM)

        sx = pad + thumb + pad
        with Image.open(sp) as s:
            s = s.convert("RGB").resize((sheet_w, sheet_h), Image.LANCZOS)
        im.paste(s, (sx, yy))
        d.rectangle([sx - 1, yy - 1, sx + sheet_w, yy + sheet_h], outline=(70, 70, 72))

        for i, cid in enumerate(CELLS):
            cx = sx + (i % 3) * cell_w
            cy = yy + (i // 3) * cell_h
            chosen = cid in PICKS[style]
            tag = ("★ " if chosen else "") + f"{style[:2].upper()}-{cid}"
            col = PICK if chosen else FG
            # A dark plate behind the tag: the backdrop the model drew is mid-grey and a light
            # label on it is exactly as unreadable as a dark one.
            tw = d.textlength(tag, font=f_cell)
            d.rectangle([cx + 8, cy + 8, cx + 18 + tw, cy + 38], fill=(20, 20, 22))
            d.text((cx + 13, cy + 12), tag, font=f_cell, fill=col)
            if chosen:
                d.rectangle([cx + 2, cy + 2, cx + cell_w - 3, cy + cell_h - 3],
                            outline=PICK, width=3)

            # THE NOTE GOES INSIDE ITS OWN CELL, WRAPPED TO THE CELL'S WIDTH.
            # The first version of this sheet ran the six notes as two long lines under the
            # grid and the canvas cut three of them mid-word -- "the board's own border,",
            # "four slotted corner", "keyed retaining". A sheet that truncates its own labels,
            # in a mod whose source lint BANS TextOverflowModes.Truncate because a cap once
            # shipped reading "AUSWAHL BEEN", is not a sheet anyone should be shown. Measured
            # against the cell and wrapped, so it cannot happen at any string length.
            lines = _wrap(d, NOTES[style][cid], f_note, cell_w - 28)
            bh = 8 + 22 * len(lines)
            by = cy + cell_h - 10 - bh
            d.rectangle([cx + 8, by, cx + cell_w - 10, by + bh], fill=(20, 20, 22))
            for li, ln in enumerate(lines):
                d.text((cx + 14, by + 3 + li * 22), ln, font=f_note,
                       fill=PICK if chosen else FG)

        y += row_h

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    im.save(dst)
    print(f"[sheet] {dst}  {im.size[0]}x{im.size[1]}")
    return dst


def verify():
    """Check the cells really are 3 wide by 2 tall with a plain backdrop between them.

    The cell indexing above is taken from the ASK, and an ask is a hypothesis. This measures
    the delivered images: the four gutters between cells must be markedly flatter than the
    cell centres, or the grid is not where `CELLS` says it is.
    """
    import numpy as np
    ok = True
    for style in CO.STYLES:
        p = os.path.join(OUT, CO.GENERATED[style])
        with Image.open(p) as im:
            a = np.asarray(im.convert("L"), dtype=np.float32)
        h, w = a.shape
        cw, ch = w // 3, h // 2
        gut = [a[:, int(cw * k) - 6:int(cw * k) + 6].std() for k in (1, 2)]
        gut += [a[int(ch) - 6:int(ch) + 6, :].std()]
        cen = [a[int(ch * (r + .5)) - 40:int(ch * (r + .5)) + 40,
                 int(cw * (c + .5)) - 40:int(cw * (c + .5)) + 40].std()
               for r in (0, 1) for c in (0, 1, 2)]
        good = max(gut) < min(cen)
        ok &= good
        print(f"  {style:6s} {w}x{h}  gutter sigma {max(gut):5.1f}   "
              f"weakest cell sigma {min(cen):5.1f}   {'grid confirmed' if good else 'GRID NOT AS ASSUMED'}")
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-o", "--out", default=os.path.join(DEBUG, "options_contact_sheet.png"))
    ap.add_argument("--verify", action="store_true")
    a = ap.parse_args()
    if a.verify:
        raise SystemExit(0 if verify() else 1)
    verify()
    build(a.out)


if __name__ == "__main__":
    main()
