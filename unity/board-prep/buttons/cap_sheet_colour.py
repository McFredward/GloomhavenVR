#!/usr/bin/env python3
"""ROUND 5's SHEETS: the caps on their own boards, and the colour table beside them.

    python3 unity/board-prep/buttons/cap_sheet_colour.py --render     (needs blender)
    python3 unity/board-prep/buttons/cap_sheet_colour.py --sheets

WHAT THE BEFORE COLUMN IS, AND WHY IT IS NOT A STORED PICTURE
--------------------------------------------------------------
Both columns are rendered by `cap_onboard.py` in the same run: same meshes, same seats, same
two baked studio directions, same camera, same projection. Exactly two things differ, and they
are the two halves of ONE decision:

    the ATLAS          --atlas .planning/debug/round5/atlas_before   (ModBuild 290's, from git)
    the STATE COLOUR   --idle 0.550,0.514,0.564  (ModBuild 286's BoardIdleColor)

`BoardLit` computes `alb = tex2D(_MainTex, uv) * _Color`, so the cap's colour is the PRODUCT of
those two. A before/after that swapped only the atlas would credit this round with half a
change and blame it for the other half; round 4's own sheet says the same thing about its
construction column and it is the reason that sheet could be trusted.

THE GEOMETRY IS IDENTICAL IN BOTH COLUMNS and the sheet says so on its face. ModBuild 290's
rebuilt cap geometry is what both wear; this round did not touch a vertex. A reader who is not
told that will read the whole difference as "the buttons were rebuilt again".
"""
import argparse
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
DEBUG = os.path.join(REPO, ".planning", "debug", "round5")
ONBOARD = os.path.join(DEBUG, "onboard")
MESHES = os.path.join(DEBUG, "meshes")
BEFORE_ATLAS = os.path.join(DEBUG, "atlas_before")
BLENDER = "/home/claw/blender-4.2/blender"
STYLES = ("oak", "steel", "bronze")

IDLE_BEFORE = {"oak": "0.550,0.514,0.564", "steel": "0.407,0.541,0.607",
               "bronze": "0.753,0.471,0.224"}

BG, FG, DIM, ACC = (26, 26, 28), (238, 238, 232), (150, 150, 146), (232, 196, 96)


def font(sz, bold=False):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf"
              % ("-Bold" if bold else ""),):
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()


def render(extra_args=()):
    os.makedirs(ONBOARD, exist_ok=True)
    for style in STYLES:
        for pas in ("before", "now"):
            out = os.path.join(ONBOARD, f"{style}_{pas}.png")
            cmd = [BLENDER, "--background", "--python",
                   os.path.join(HERE, "cap_onboard.py"), "--",
                   style, "after", MESHES, out, *extra_args]
            if pas == "before":
                cmd += ["--atlas", BEFORE_ATLAS, "--idle", IDLE_BEFORE[style]]
            print("  " + " ".join(cmd[3:]), flush=True)
            r = subprocess.run(cmd, capture_output=True, text=True)
            if r.returncode != 0 or not os.path.exists(out):
                sys.stderr.write(r.stdout[-4000:] + r.stderr[-4000:])
                raise SystemExit(f"{style}/{pas}: render failed")
            for line in r.stdout.splitlines():
                if "winding" in line or "IdleColor" in line:
                    print("    " + line.strip(), flush=True)


def sheet(out, width=2400, pad=26):
    """Three rows, two columns: each board's caps before and after, on that board."""
    cw = (width - pad * 3) // 2
    tiles = {}
    for style in STYLES:
        for pas in ("before", "now"):
            p = os.path.join(ONBOARD, f"{style}_{pas}.png")
            im = Image.open(p).convert("RGB")
            im = im.resize((cw, int(im.height * cw / im.width)), Image.Resampling.LANCZOS)
            tiles[(style, pas)] = im
    rh = max(t.height for t in tiles.values())
    head, cap = 96, 34
    img = Image.new("RGB", (width, head + 3 * (rh + cap + 54) + pad), BG)
    d = ImageDraw.Draw(img)
    d.text((pad, 20), "Board buttons, round 5 — the CAP'S COLOUR, on its own board",
           FG, font(34, True))
    d.text((pad, 62), "Same meshes, same seats, same light, same camera. What differs is the "
                      "atlas AND the state colour it modulates — the two halves of one "
                      "decision. The GEOMETRY is ModBuild 290's in both columns.",
           DIM, font(17))
    y = head
    for style in STYLES:
        d.text((pad, y), style.upper(), ACC, font(24, True))
        d.text((pad + 130, y + 6), NOTE[style], DIM, font(16))
        y += 38
        for i, (pas, label) in enumerate((("before", "BEFORE — ModBuild 290"),
                                          ("now", "AFTER — round 5"))):
            x = pad + i * (cw + pad)
            img.paste(tiles[(style, pas)], (x, y))
            if pas == "now":
                d.rectangle([x - 1, y - 1, x + cw, y + rh], outline=ACC)
            d.text((x, y + rh + 8), label, ACC if pas == "now" else DIM, font(18, True))
        y += rh + cap + 20
    img.save(out)
    print(f"wrote {out}  {img.size[0]}x{img.size[1]}")


NOTE = {
    "oak": "hue error against its own board  +27.5° → −1.3°   ·   field contrast 4.12 % → 10.69 %",
    "steel": "hue error against its own board  +164.0° → +7.5°   ·   field contrast 6.64 % → 10.36 %",
    "bronze": "hue error against its own board  −26.2° → +3.4°   ·   field contrast 4.21 % → 9.05 %",
}


def table(out, width=1900):
    """The dE table as a picture, straight out of `cap_belong.report`."""
    import io
    import contextlib
    import cap_belong as B
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        B.report(os.path.join(DEBUG, "boards"), before=BEFORE_ATLAS)
    lines = buf.getvalue().splitlines()
    f = font(19)
    lh = 26
    img = Image.new("RGB", (width, 112 + lh * len(lines)), BG)
    d = ImageDraw.Draw(img)
    d.text((26, 20), "Round 5 — the colour table (CIEDE2000)", FG, font(32, True))
    d.text((26, 62), "Both columns read off a PNG. `cap_belong.py --report`.", DIM, font(17))
    for i, ln in enumerate(lines):
        col = FG
        s = ln.strip()
        if s and not s[0].isdigit() and s == s.upper() and len(s) > 12:
            col = ACC
        elif ln.startswith("  ") and not ln.startswith("   "):
            col = DIM
        d.text((26, 106 + i * lh), ln.replace("\t", "    "), col, f)
    img.save(out)
    print(f"wrote {out}  {img.size[0]}x{img.size[1]}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--render", action="store_true")
    ap.add_argument("--sheets", action="store_true")
    ap.add_argument("--onboard-out", default=os.path.join(DEBUG, "caps_on_boards.png"))
    ap.add_argument("--table-out", default=os.path.join(DEBUG, "colour_table.png"))
    a = ap.parse_args()
    if a.render:
        render()
    if a.sheets:
        sheet(a.onboard_out)
        table(a.table_out)


if __name__ == "__main__":
    main()
