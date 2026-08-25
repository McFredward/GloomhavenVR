#!/usr/bin/env python3
"""ROUND 3's pictures: the cap in the user's own well, and the cap beside a board BACK.

Everything geometric here -- the field quad in his screenshot, the crop box, the mip model,
the accent colour, the field-statistics box -- is IMPORTED from `plates.py`, which is round 2's
station and was validated against the screenshot to within 0.007 per channel. Re-deriving any
of it would mean two instruments that can disagree about the same quantity.

WHAT THE TWO SHEETS ANSWER
--------------------------
`caps_on_his_bronze_well.png`  the user's own frame, four panels, and the second one is a
    CONTROL: the ROUND-2 SHIPPED atlas redrawn through the same chain, which must be invisible
    against his untouched pixels. A compositor that cannot reproduce the picture it is about to
    change has not earned the right to change it.

`back_vs_cap.png`  the comparison the brief says decides this round. A shipped board BACK --
    the one thing from this generator the user volunteered praise for -- beside a round-3 cap,
    both resampled to the SAME on-screen size, with `plate_forensics` numbers under each. It
    is the only sheet here that can say whether the caps now do what the backs do.

WHAT NEITHER SHEET CAN SEE, stated because round 2's write-up had to add this afterwards.
The composite redraws the recessed FIELD only; the bevel, the recess wall and the board are
his own pixels, so it says nothing about the new BEZEL, which is half of what round 3 changed.
For the bezel the evidence is the Unity render station, and that runs the project's own
compiled BoardLit rather than the bundle's D3D11 variant. And the back-vs-cap sheet compares
ALBEDO ART at matched scale: it is a statement about what was drawn, not about what the two
surfaces do under their (different) shading.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

import cap_atlas as C            # noqa: E402
import cap_deltae as D          # noqa: E402
import cap_object as O          # noqa: E402
import plate_forensics as F     # noqa: E402
import plates as P              # noqa: E402

REPO = os.path.dirname(os.path.dirname(PREP))
MAIN = P._planning_root(REPO)
OUT = os.path.join(PREP, "out")
CACHE = os.path.join(HERE, "out")
DEBUG = os.path.join(MAIN, ".planning", "debug", "keycaps3")
BUNDLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")
STYLES = ("oak", "steel", "bronze")
IDLE = {"oak": np.array([0.550, 0.514, 0.564]),      # Cards/PlayTray.6.Build.cs BoardIdleColor
        "steel": np.array([0.407, 0.541, 0.607]),
        "bronze": np.array([0.753, 0.471, 0.224])}


# =========================================================================================
# SHEET 1 -- his own well
# =========================================================================================
def composite_bronze(r2_dir, dst=None, cell_index=1, scale=3):
    dst = dst or os.path.join(DEBUG, "caps_on_his_bronze_well.png")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shot = Image.open(P.SHOT).convert("RGB")
    base = np.asarray(shot, dtype=np.float64) / 255.0
    W, H = shot.size

    r2a = os.path.join(r2_dir, "KeycapBronze_albedo.png")
    r2n = os.path.join(r2_dir, "KeycapBronze_normal.png")
    r3a = os.path.join(BUNDLE, "KeycapBronze_albedo.png")
    r3n = os.path.join(BUNDLE, "KeycapBronze_normal.png")

    panels = [("1  ORIGINAL   the user's screenshot, untouched", None, None, None),
              ("2  CONTROL   the SHIPPED ModBuild 286 atlas x the accent colour the "
               "screenshot is in (0.350, 0.460, 0.280). Must be invisible against panel 1.",
               r2a, r2n, P.ACCENT_CONFIRM),
              ("3  ROUND 3, SAME COLOUR   the registered object cell x that same accent "
               "colour -- the texture change on its own", r3a, r3n, P.ACCENT_CONFIRM),
              ("4  ROUND 3, IDLE   the registered object cell x the ModBuild 286 bronze idle "
               "(0.753, 0.471, 0.224) -- an AVAILABLE key on this board",
               r3a, r3n, IDLE["bronze"])]

    drawn_panels = []
    for label, atlas, normal, state in panels:
        img = base.copy()
        if atlas is not None and os.path.isfile(atlas):
            cell = P._face_cell(atlas, cell_index, state, normal_path=normal)
            cell = P._to_mip(cell, P.FIELD_QUAD)
            d_rgb, inside = P._bilinear_quad(cell, P.FIELD_QUAD, (W, H))
            lum = base @ D.REC709
            keep = lum > P.CAPTION_LUM        # the game's own caption stays his pixels
            img = np.where((inside & ~keep)[..., None], d_rgb, img)
        drawn_panels.append((label, img))

    cw = P.CROP_BOX[2] - P.CROP_BOX[0]
    ch = P.CROP_BOX[3] - P.CROP_BOX[1]
    tw, th = cw * scale, ch * scale
    n = len(drawn_panels)
    head, foot, pad = 46, 48, 8
    sheet = Image.new("RGB", (n * tw + (n + 1) * pad, th + head + foot + 2 * pad), (18, 18, 20))
    dr = ImageDraw.Draw(sheet)
    dr.text((pad, 10), "ROUND 3 -- THE BRONZE CAP IN ITS OWN WELL   "
                       ".planning/debug/abgeschnitter_text.jpg, "
                       f"crop {P.CROP_BOX}, {scale}x nearest.", fill=(235, 235, 220))
    dr.text((pad, 24), "Only the RECESSED FIELD is redrawn. The bevel, the recess wall, the "
                       "board and the game's caption are the screenshot's own pixels -- so "
                       "this sheet cannot show the new BEZEL, which is half of round 3.",
            fill=(150, 150, 160))
    for i, (label, img) in enumerate(drawn_panels):
        crop = Image.fromarray((np.clip(img, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB") \
            .crop(P.CROP_BOX).resize((tw, th), Image.Resampling.NEAREST)
        x0 = pad + i * (tw + pad)
        sheet.paste(crop, (x0, head + pad))
        dr.rectangle([x0, head + pad, x0 + tw - 1, head + pad + th - 1], outline=(70, 70, 78))
        for k, line in enumerate(_wrap(label, 58)):
            dr.text((x0 + 4, head + pad + th + 6 + 11 * k), line, fill=(240, 230, 160))
        m, rc = P._field_stats(img)
        dr.text((x0 + 4, head + pad + th + 34),
                f"field mean {np.round(m, 3).tolist()}   contrast {rc:5.1f} %"
                + ("   <- what every panel is checked against" if i == 0 else ""),
                fill=(170, 170, 180))
    sheet.save(dst)
    stats = [P._field_stats(img) for _, img in drawn_panels]
    return dst, stats


def _wrap(text, n):
    out, line = [], ""
    for w in text.split():
        if len(line) + len(w) + 1 > n:
            out.append(line)
            line = w
        else:
            line = (line + " " + w).strip()
    if line:
        out.append(line)
    return out[:3]


# =========================================================================================
# SHEET 2 -- the cap beside a board BACK, at matched on-screen scale
# =========================================================================================
# The board back is 1536 x 1024 at 1 px = 0.3125 mm (`gen_board.py`), i.e. a 480 x 320 mm
# panel. A cap is 53.2 x 43.9 mm (bronze) to 63.0 x 62.1 (steel). At the same distance and the
# same pixels-per-degree the back is therefore ~8x the cap across -- so "matched on-screen
# scale" means matched MILLIMETRES PER PIXEL, and a fair sheet shows a CROP of the back the
# size of a cap beside the whole cap. Both are then drawn at the same size on the page.
BACK_MM_PER_PX = 0.3125
CAP_MM = O.CAP_MM


def back_vs_cap(r2_dir, dst=None, tile=300, cell_index=1):
    dst = dst or os.path.join(DEBUG, "back_vs_cap.png")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    pad, head, foot = 10, 56, 96
    cols, rows = 3, 3
    sheet = Image.new("RGB", (cols * tile + (cols + 1) * pad,
                              rows * (tile + foot) + head + pad), (18, 18, 20))
    dr = ImageDraw.Draw(sheet)
    dr.text((pad, 10), "WHAT THE ACCEPTED BOARD BACKS DO, AND WHETHER THE CAPS NOW DO IT   "
                       "-- all six crops are the SAME number of millimetres across "
                       "(the cap's own width) and are drawn at the same size.",
            fill=(235, 235, 220))
    dr.text((pad, 26), "row 1: a crop of the shipped board BACK (ModBuild 276, 'Die Rueckseite "
                       "der boards gefaellt mir sehr gut').   row 2: the SHIPPED ModBuild 286 "
                       "cap cell.   row 3: round 3's registered cell.",
            fill=(150, 150, 160))
    dr.text((pad, 40), "REG is mean luminance against distance-to-border, peak-to-trough, per "
                       "cent of the mean -- the statistic a swatch cannot have. A stationary "
                       "noise swatch reads 17.4 +- 3.9.", fill=(150, 150, 160))

    for col, style in enumerate(STYLES):
        capw = CAP_MM[style][0]
        px = int(round(capw / BACK_MM_PER_PX))
        back = np.asarray(Image.open(os.path.join(OUT, f"board_back_{style}.png"))
                          .convert("RGB"), dtype=np.float64) / 255.0
        bh, bw = back.shape[:2]
        y0, x0 = (bh - px) // 2, (bw - px) // 2
        crops = [(f"board BACK, {capw:.0f} mm crop of {bw * BACK_MM_PER_PX:.0f} mm",
                  back[y0:y0 + px, x0:x0 + px]),
                 ("SHIPPED cap cell (ModBuild 286)",
                  F.cell_of(os.path.join(r2_dir, f"Keycap{style.capitalize()}_albedo.png"),
                            cell_index)),
                 ("ROUND 3 cap cell",
                  F.cell_of(os.path.join(BUNDLE, f"Keycap{style.capitalize()}_albedo.png"),
                            cell_index))]
        for row, (label, arr) in enumerate(crops):
            im = Image.fromarray((np.clip(arr, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB") \
                .resize((tile, tile), Image.Resampling.LANCZOS)
            x = pad + col * (tile + pad)
            y = head + row * (tile + foot)
            sheet.paste(im, (x, y))
            dr.rectangle([x, y, x + tile - 1, y + tile - 1], outline=(70, 70, 78))
            dr.text((x + 2, y + tile + 4), f"{style.upper()}  {label}", fill=(240, 230, 160))
            m = F.measure(F._lum(F.normalise(arr)))
            dr.text((x + 2, y + tile + 18),
                    f"REG {m['reg']:6.1f}   total sigma {m['total']:5.1f} %", fill=(200, 200, 210))
            dr.text((x + 2, y + tile + 32),
                    f"STORY {m['story']:5.1f}   GRAIN {m['grain']:5.1f}", fill=(170, 170, 180))
            dr.text((x + 2, y + tile + 46),
                    f"NSI {m['nsi']:.2f}   COH {m['coh']:.2f}   GINI {m['gini']:.2f}",
                    fill=(170, 170, 180))
    sheet.save(dst)
    return dst



# =========================================================================================
# SHEET 3 -- the reference column, through the real shader
# =========================================================================================
CONTROLS = ("Confirm", "Undo", "Skip", "ItemUse", "ShortRest", "LongRest", "Fixed")


def renders_r2_vs_r3(dst=None, tile=210):
    """ModBuild 286 beside round 3, every role on every board, through `BoardLit`.

    Both columns come from the SAME render station, run twice with only the six atlas PNGs
    changed between them, so nothing in the picture differs for any other reason. The
    station's `before` column is the pre-atlas shared KeycapGrain and answers a different
    question; this one answers "did this round improve on the last one".
    """
    dst = dst or os.path.join(DEBUG, "renders_r2_vs_r3.png")
    r2 = os.path.join(DEBUG, "r2ref")
    head, cap, pad = 46, 18, 6
    ncol = len(CONTROLS) * 2
    nrow = 3
    sheet = Image.new("RGB", (ncol * tile + (ncol + 1) * pad,
                              nrow * (tile + cap) + head + pad), (18, 18, 20))
    dr = ImageDraw.Draw(sheet)
    dr.text((pad, 10), "SHIPPED ModBuild 286 (left of each pair) vs ROUND 3 (right), every "
                       "role on every board, through the real BoardLit at the idle colour.",
            fill=(235, 235, 220))
    dr.text((pad, 26), "One render station, run twice, with only the six atlas PNGs changed "
                       "between the runs. The station compiles the PROJECT's BoardLit for "
                       "OpenGL; only the rig proves the bundle's D3D11 variant.",
            fill=(150, 150, 160))
    for row, style in enumerate(("Oak", "Steel", "Bronze")):
        for i, ctrl in enumerate(CONTROLS):
            for k, base in enumerate((r2, DEBUG)):
                p = os.path.join(base, f"{style}_{ctrl}_after_front.png")
                x = pad + (2 * i + k) * (tile + pad)
                y = head + row * (tile + cap)
                if os.path.isfile(p):
                    sheet.paste(Image.open(p).convert("RGB").resize(
                        (tile, tile), Image.Resampling.LANCZOS), (x, y))
                dr.rectangle([x, y, x + tile - 1, y + tile - 1],
                             outline=(150, 130, 60) if k else (70, 70, 78))
                dr.text((x + 2, y + tile + 3),
                        f"{style} {ctrl} {'ROUND 3' if k else '286'}",
                        fill=(240, 230, 160) if k else (150, 150, 156))
    sheet.save(dst)
    return dst


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--r2", required=True, help="directory holding the ModBuild 286 atlases")
    ap.add_argument("--bronze", action="store_true")
    ap.add_argument("--backs", action="store_true")
    ap.add_argument("--renders", action="store_true")
    a = ap.parse_args()
    if a.bronze:
        p, st = composite_bronze(a.r2)
        print("BRONZE WELL ", p)
        for i, (m, rc) in enumerate(st):
            print(f"   panel {i + 1}: field mean {np.round(m, 4).tolist()}  contrast {rc:5.2f} %")
    if a.backs:
        print("BACK vs CAP ", back_vs_cap(a.r2))
    if a.renders:
        print("RENDERS     ", renders_r2_vs_r3())
