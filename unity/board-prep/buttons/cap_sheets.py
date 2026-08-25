#!/usr/bin/env python3
"""Contact sheets for the keycap render station (Assets/Editor/PreviewKeycaps.cs).

The station writes one PNG per cap, per view, per condition. That is 231 files and nobody
judges 231 files -- what a reader needs is the BEFORE beside the AFTER, the three boards
beside each other, and the two viewing distances beside each other, each with a caption
saying what it is. This assembles exactly those.

    python3 cap_sheets.py [--dir /home/claw/gloomhaven_vr/.planning/debug/keycaps]

THE ONE RULE THAT MATTERS HERE. The two zoom extremes are rendered by the station AT their
true pixel size -- 96 px and 32 px across, at 20 px/degree, which is a Quest 3 over Virtual
Desktop looking at the board from 0.5 m and 1.5 m. This script upscales them with NEAREST
(point) filtering only. A smooth resample would invent detail the headset never delivers, and
the whole question those two panels exist to answer is whether the carved symbol survives to
32 px. It must be possible to count the pixels in the picture.

matplotlib is not installed in this environment, so the PressDepth01 curve is drawn with PIL.
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont

STYLES = ["Oak", "Steel", "Bronze"]
# The control set in the order a player meets it. "Fixed" is the follow/pin toggle in its
# PINNED state (CapRole.FixedPinned); its FOLLOW twin is rendered too and gets its own row in
# the all-boards sheet, because the toggle swaps between them live.
CONTROLS = ["Confirm", "Undo", "Skip", "ItemUse", "ShortRest", "LongRest", "Fixed"]
LANGS = ["de", "en"]

BG = (26, 26, 28)
CAP_BG = (16, 16, 18)
FG = (222, 222, 226)
DIM = (150, 150, 156)
RULE = (86, 86, 92)
PAD = 10
CAPH = 20          # caption strip height under a panel
HEADH = 26         # sheet title strip


def font(size=13):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
              "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except OSError:
                pass
    return ImageFont.load_default()


F = font(13)
FS = font(11)
FB = font(15)


def load(d, name):
    p = os.path.join(d, name)
    if not os.path.exists(p):
        return None
    return Image.open(p).convert("RGB")


def caption(img, text, w, small=False):
    """A panel = the image with a caption strip under it, both `w` wide."""
    h = img.height if img else 0
    out = Image.new("RGB", (w, h + CAPH), BG)
    if img:
        out.paste(img, ((w - img.width) // 2, 0))
    dr = ImageDraw.Draw(out)
    dr.text((2, h + 4), text, font=FS if small else F, fill=DIM if small else FG)
    return out


def titled(rows_img, title, sub=""):
    """Stack a title strip on top of an assembled body."""
    head = HEADH + (18 if sub else 0)
    out = Image.new("RGB", (rows_img.width, rows_img.height + head + PAD), BG)
    dr = ImageDraw.Draw(out)
    dr.text((PAD, 6), title, font=FB, fill=FG)
    if sub:
        dr.text((PAD, 6 + HEADH - 4), sub, font=FS, fill=DIM)
    out.paste(rows_img, (0, head + PAD // 2))
    return out


def grid(panels, cols, gap=PAD, dividers=()):
    """Lay panels (already captioned, uniform width) out in a grid. `dividers` is a set of
    column indices to draw a vertical rule BEFORE -- that is what separates the reference
    column from the new one."""
    if not panels:
        return Image.new("RGB", (10, 10), BG)
    cw = max(p.width for p in panels)
    ch = max(p.height for p in panels)
    rows = (len(panels) + cols - 1) // cols
    W = cols * cw + (cols + 1) * gap
    H = rows * ch + (rows + 1) * gap
    out = Image.new("RGB", (W, H), BG)
    for i, p in enumerate(panels):
        r, c = divmod(i, cols)
        out.paste(p, (gap + c * (cw + gap), gap + r * (ch + gap)))
    dr = ImageDraw.Draw(out)
    for c in dividers:
        x = gap + c * (cw + gap) - gap // 2
        dr.line([(x, 4), (x, H - 4)], fill=RULE, width=2)
    return out


# ---------------------------------------------------------------------------------------
# 1. BEFORE | AFTER, one sheet per style
# ---------------------------------------------------------------------------------------
def before_after(d, style):
    panels = []
    for ctl in CONTROLS:
        for tag, cond, view in (("BEFORE  front", "before", "front"),
                                ("BEFORE  rake", "before", "rake"),
                                ("AFTER  front", "after", "front"),
                                ("AFTER  rake", "after", "rake")):
            im = load(d, f"{style}_{ctl}_{cond}_{view}.png")
            panels.append(caption(im, f"{ctl}  -  {tag}", im.width if im else 420))
    body = grid(panels, 4, dividers=(2,))
    return titled(
        body,
        f"KEYCAPS  -  {style} board  -  reference (left of the rule) beside the new atlas (right)",
        "BEFORE = exactly what ships today: the same mesh and the same state colours, KeycapGrain_albedo/normal "
        "shared by all three boards, no texture transform, no symbol.   AFTER = this board's own keycap atlas "
        "with the role's cell on the top plateau and the PLAIN cell on the bevel ring and walls.   Both at the "
        "shipped [ButtonColors] BoardCapTint of 0.5 and the IDLE (available) state colour.   No scene light: "
        "BoardLit bakes its own two studio directions.   Rake = 35 deg above the cap's face normal.")


# ---------------------------------------------------------------------------------------
# 2. The three boards side by side, after only, rake
# ---------------------------------------------------------------------------------------
def all_boards(d):
    panels = []
    for ctl in CONTROLS + ["FixedFollow"]:
        for style in STYLES:
            im = load(d, f"{style}_{ctl}_after_rake.png")
            panels.append(caption(im, f"{style}  -  {ctl}", im.width if im else 420))
    body = grid(panels, 3)
    return titled(
        body,
        "KEYCAPS  -  the three boards side by side (new atlas, rake view, idle state, cap tint 0.5)",
        "One column per board style. The last row is the follow/pin toggle's FOLLOW face, which the same cap "
        "swaps to live (two floats on the material's texture transform, no rebuild).")


# ---------------------------------------------------------------------------------------
# 3. The two viewing distances -- POINT upscaled, never resampled smooth
# ---------------------------------------------------------------------------------------
def zoom(d, scale_near=3, scale_far=9):
    # One ROW per control, nine columns: each board's near shot, its far shot and the same far
    # shot with MSAA off. Both magnifications land on 432 px so the grid is uniform and the
    # whole sheet stays inside the 4000 px limit; the point filter means the magnification
    # cannot add anything the render did not contain.
    panels = []
    for ctl in CONTROLS:
        for style in STYLES:
            for tag, sfx, sc in (("NEAR 96px (0.5 m)", "near96", scale_near),
                                 ("FAR 32px (1.5 m)", "far32", scale_far),
                                 ("FAR 32px, MSAA off", "far32_aa1", scale_far)):
                im = load(d, f"{style}_{ctl}_zoom_{sfx}.png")
                if im is not None:
                    im = im.resize((im.width * sc, im.height * sc), Image.NEAREST)
                panels.append(caption(im, f"{style} {ctl} - {tag} (x{sc} point)",
                                      im.width if im else 432, small=True))
    body = grid(panels, 9, gap=6, dividers=(3, 6))
    return titled(
        body,
        "KEYCAPS  -  the two viewing distances, rendered AT that pixel size and point-upscaled",
        "The cap is rasterised into a 144 px image at 96 px across, and into a 48 px image at 32 px across, "
        "holding 20 px/degree (a Quest 3 over Virtual Desktop). Nothing here was rendered large and shrunk. "
        "Every pixel you can see is a pixel the headset would actually be given; the magnification is NEAREST, "
        "so you can count them. The third column is the same far shot with MSAA off, so the sample count is not "
        "what is hiding the aliasing.")


# ---------------------------------------------------------------------------------------
# 4. The press stroke: 9 frames, and the curve they were sampled from
# ---------------------------------------------------------------------------------------
ATTACK, HOLD, RELEASE = 0.035, 0.030, 0.120
REBOUND, SPLIT = 0.10, 0.62
STROKE = ATTACK + HOLD + RELEASE


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def press_depth01(s):
    """WorldUI/ButtonTuning.PressDepth01, ported so the plotted curve is the function itself
    and not a drawing of what it is believed to do."""
    if s <= 0.0:
        return 0.0
    if s < ATTACK:
        t = s / ATTACK
        return 1.0 - (1.0 - t) * (1.0 - t)
    held = s - ATTACK
    if held < HOLD:
        return 1.0
    rel = (held - HOLD) / RELEASE
    if rel >= 1.0:
        return 0.0
    if rel < SPLIT:
        return 1.0 - (1.0 + REBOUND) * smoothstep(rel / SPLIT)
    return -REBOUND * (1.0 - smoothstep((rel - SPLIT) / (1.0 - SPLIT)))


def press_curve(w, h):
    im = Image.new("RGB", (w, h), CAP_BG)
    dr = ImageDraw.Draw(im)
    m = 34
    x0, x1 = m, w - m // 2
    # THE Y AXIS IS DRAWN THE WAY THE CAP MOVES, not the way the number grows: depth +1 is the
    # cap at the BOTTOM of its travel, so +1 is at the bottom of the plot and the negative
    # rebound rises ABOVE the rest line. A plot with +1 at the top reads as the key coming UP
    # when it is going down, which is the opposite of the thing being judged.
    top, bot = m // 2, h - m
    lo, hi = -0.18, 1.05

    def px(s):
        return x0 + (x1 - x0) * (s / STROKE)

    def py(v):
        return top + (bot - top) * ((v - lo) / (hi - lo))

    dr.line([(x0, py(0.0)), (x1, py(0.0))], fill=(120, 120, 128))      # rest
    dr.line([(x0, py(1.0)), (x1, py(1.0))], fill=(70, 70, 76))         # bottom of travel
    dr.text((4, py(0.0) - 7), "rest", font=FS, fill=DIM)
    dr.text((4, py(1.0) - 7), "-4.0mm", font=FS, fill=DIM)
    dr.text((4, py(-REBOUND) - 7), "+0.4", font=FS, fill=DIM)
    # phase boundaries
    for s, lab in ((ATTACK, "attack"), (ATTACK + HOLD, "hold")):
        dr.line([(px(s), top), (px(s), bot)], fill=(60, 60, 66))
        dr.text((px(s) + 3, top), lab, font=FS, fill=DIM)
    pts = []
    steps = 600
    for i in range(steps + 1):
        s = STROKE * i / steps
        pts.append((px(s), py(press_depth01(s))))
    dr.line(pts, fill=(236, 176, 96), width=2)
    for i in range(9):
        s = STROKE * i / 8.0
        x, y = px(s), py(press_depth01(s))
        dr.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(255, 255, 255))
        dr.text((x - 3, bot + 3), str(i), font=FS, fill=FG)
    dr.text((x0, bot + 16), f"PressDepth01 over the {STROKE * 1000:.0f} ms stroke, plotted the way the cap MOVES (down = sunk) "
                            f"(attack {ATTACK * 1000:.0f} + hold {HOLD * 1000:.0f} + release "
                            f"{RELEASE * 1000:.0f} ms); the dip below rest is the "
                            f"{REBOUND:.2f}-of-travel rebound.", font=FS, fill=DIM)
    return im


def press_sheet(d):
    frames = [load(d, f"press_{i}.png") for i in range(9)]
    panels = []
    for i, im in enumerate(frames):
        s = STROKE * i / 8.0
        dep = press_depth01(s)
        panels.append(caption(im, f"{i}:  t {s * 1000:5.1f} ms   depth {dep:+.3f}",
                              im.width if im else 360, small=True))
    strip = grid(panels, 9)
    curve = press_curve(strip.width - 2 * PAD, 240)
    body = Image.new("RGB", (strip.width, strip.height + curve.height + PAD), BG)
    body.paste(strip, (0, 0))
    body.paste(curve, (PAD, strip.height))
    return titled(
        body,
        "PRESS STROKE  -  one Confirm cap, 9 evenly spaced phases across the 185 ms stroke",
        "The cap body sits at local z -4.0 mm and travels +Z into the board by 4.0 mm x PressDepth01(t), "
        "which is the line BoardButton runs. The dark plate behind it is a STAND-IN for the seat, tinted with "
        "ButtonTuning.CapWellColor over this board's own plate texture -- it is darker than the shipped well, "
        "so read the motion from it, not the contrast.")


# ---------------------------------------------------------------------------------------
# 5. The engraving stand-ins
# ---------------------------------------------------------------------------------------
def engraving(d):
    panels = []
    for style in STYLES:
        for lang in LANGS:
            im = load(d, f"standin_engraving_{style}_{lang}_rake.png")
            panels.append(caption(im, f"{style}  -  {'DEUTSCH' if lang == 'de' else 'ENGLISH'}"
                                      f"   (STAND-IN, not the shipped glyphs)",
                                  im.width if im else 900))
    body = grid(panels, 2, dividers=(1,))
    return titled(
        body,
        "BOARD ENGRAVING  -  S T A N D - I N  -  layout, colour and depth only",
        "THE GLYPH SHAPES AND THE ANTIALIASING BELOW ARE NOT THE SHIPPED ONES. The shipped board labels are "
        "TextMeshPro SDF text in the game's MarcellusSC face; TextMeshPro is not in this Unity project's package "
        "manifest, so these are Unity's legacy TextMesh with the built-in font. WHAT IS REAL: the three layers "
        "(groove floor / lit lip offset DOWN and LEFT / shaded keyline), their per-style colours from "
        "Cards/BoardEngraving.cs, the 0.8 mm proud seating and the caption fit box. WHAT IS NOT: every glyph "
        "outline, and the exact lip/keyline widths, which are converted out of TMP's SDF units through an "
        "assumed 0.10 em spread. Judge LAYOUT, COLOUR and DEPTH BEHAVIOUR here. Judge stroke crispness on the rig.")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default="/home/claw/gloomhaven_vr/.planning/debug/keycaps")
    args = ap.parse_args()
    d = args.dir

    made = []
    for style in STYLES:
        p = os.path.join(d, f"caps_before_after_{style.lower()}.png")
        before_after(d, style).save(p)
        made.append(p)
    for name, im in (("caps_all_boards.png", all_boards(d)),
                     ("caps_zoom_near_far.png", zoom(d)),
                     ("press_stroke.png", press_sheet(d)),
                     ("standin_engraving_de_en.png", engraving(d))):
        p = os.path.join(d, name)
        im.save(p)
        made.append(p)

    for p in made:
        w, h = Image.open(p).size
        flag = "  *** OVER 4000 px ***" if max(w, h) > 4000 else ""
        print(f"{p}  {w}x{h}{flag}")


if __name__ == "__main__":
    main()
