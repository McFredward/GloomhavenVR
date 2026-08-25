#!/usr/bin/env python3
# windowmaterialise_compose.py - turn the raw renders from windowmaterialise_room.py into the
# things a reviewer actually looks at: contact strips, mp4s at the real playback speed, and the two
# side-by-side depth pairs.
#
# WHY THE LABELS MATTER. The previous design of this effect was rejected as "eher ein 2D-Effekt",
# and a strip whose cells are unlabelled invites the reader to assume the timing rather than read
# it. Every cell here carries its own t, its eased k and the window's element progress, taken out
# of sim/meta.json - not recomputed, not guessed.
#
# RUN:
#   python3 unity/asset-preview/windowmaterialise_compose.py [outdir]
# deps: Pillow, and ffmpeg on PATH (/home/linuxbrew/.linuxbrew/bin/ffmpeg).
import json
import os
import shutil
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else "render/windowmaterialise")
SIM = os.path.join(OUT, "sim")
FRAMES = os.path.join(OUT, "frames")

BG = (18, 18, 22)
PANEL = (26, 26, 32)
FG = (232, 230, 224)
DIM = (150, 148, 142)
ACCENT = (214, 176, 106)

CELL_W = 340
COLS, ROWS = 6, 2
PAD = 10
LABEL_H = 44
TITLE_H = 48

FONT_DIRS = ["/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/dejavu",
             "/usr/share/fonts/TTF", "/usr/local/share/fonts"]


def font(name, size):
    for d in FONT_DIRS:
        p = os.path.join(d, name)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except OSError:
                pass
    # Graceful fallback: PIL's built-in bitmap face ignores `size`, so the layout gets ugly but the
    # numbers are still legible - which is the whole point of the labels.
    return ImageFont.load_default()


F_TITLE = font("DejaVuSans-Bold.ttf", 22)
F_LABEL = font("DejaVuSans.ttf", 14)
F_SMALL = font("DejaVuSans.ttf", 13)
F_EYE = font("DejaVuSans-Bold.ttf", 26)
F_CAP = font("DejaVuSans.ttf", 16)

written = []


def note(path):
    written.append(path)
    return path


def fail(msg):
    raise SystemExit("windowmaterialise_compose: " + msg)


def load_meta():
    p = os.path.join(SIM, "meta.json")
    if not os.path.exists(p):
        fail("missing %s - run windowmaterialise_preview.py first" % p)
    if not os.path.isdir(FRAMES):
        fail("missing %s - run windowmaterialise_room.py first" % FRAMES)
    return json.load(open(p))


def load_pairs():
    p = os.path.join(FRAMES, "pairs.json")
    if os.path.exists(p):
        return json.load(open(p))
    return None


def frame_path(view, direction, i):
    return os.path.join(FRAMES, "%s_%s_%04d.png" % (view, direction, i))


def text(draw, xy, s, f, fill=FG, anchor="la"):
    draw.text(xy, s, font=f, fill=fill, anchor=anchor)


def wrap(draw, s, f, max_w):
    """Word-wrap to a pixel width. The first cut of the parallax caption ran off the right edge of
    the sheet and the sentence that justified the whole render was unreadable."""
    words, lines, cur = s.split(), [], ""
    for w in words:
        trial = (cur + " " + w).strip()
        if cur and draw.textlength(trial, font=f) > max_w:
            lines.append(cur)
            cur = w
        else:
            cur = trial
    if cur:
        lines.append(cur)
    return lines


# --- contact strips ---------------------------------------------------------------------------
VIEW_TITLE = {
    "front": "FRONT VIEW  -  dead-on, occluders hidden (the animation itself)",
    "oblique": "OBLIQUE VIEW  -  orbited right, occluders visible (the depth proof)",
}


def strip(view, direction, meta):
    frames = meta["directions"][direction]["frames"]
    seconds = meta["directions"][direction]["seconds"]
    n = len(frames)
    want = COLS * ROWS
    idx = [round(i * (n - 1) / float(want - 1)) for i in range(want)]

    probe = frame_path(view, direction, frames[0]["f"])
    if not os.path.exists(probe):
        fail("missing %s - run windowmaterialise_room.py first" % probe)
    with Image.open(probe) as im:
        src_w, src_h = im.size
    cell_h = int(round(CELL_W * src_h / float(src_w)))

    W = COLS * CELL_W + (COLS + 1) * PAD
    H = TITLE_H + ROWS * (cell_h + LABEL_H) + (ROWS + 1) * PAD
    sheet = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(sheet)

    d.rectangle([0, 0, W, TITLE_H], fill=PANEL)
    text(d, (PAD + 2, TITLE_H // 2), "%s  -  %s  -  %.3f s, %d frames @ %d fps"
         % (VIEW_TITLE[view], direction.upper(), seconds, n, meta["fps"]),
         F_TITLE, FG, anchor="lm")
    text(d, (W - PAD - 2, TITLE_H // 2),
         "%d shards, %d columns across the full duration" % (meta["shards"], want),
         F_SMALL, DIM, anchor="rm")

    for c, j in enumerate(idx):
        fr = frames[j]
        p = frame_path(view, direction, fr["f"])
        if not os.path.exists(p):
            fail("missing %s" % p)
        col, row = c % COLS, c // COLS
        x = PAD + col * (CELL_W + PAD)
        y = TITLE_H + PAD + row * (cell_h + LABEL_H + PAD)
        with Image.open(p) as im:
            sheet.paste(im.convert("RGB").resize((CELL_W, cell_h), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + CELL_W - 1, y + cell_h - 1], outline=(64, 64, 72))
        text(d, (x + 2, y + cell_h + 6), "%.3f s   k=%.2f" % (fr["t"], fr["k"]), F_LABEL, FG)
        text(d, (x + 2, y + cell_h + 24),
             "elements %.2f   f%d" % (fr["element_progress"], fr["f"]), F_SMALL, ACCENT)

    p = os.path.join(OUT, "%s_%s_strip.png" % (view, direction))
    sheet.save(p)
    return note(p)


# --- mp4 --------------------------------------------------------------------------------------
def movie(view, direction, meta):
    ff = shutil.which("ffmpeg") or "/home/linuxbrew/.linuxbrew/bin/ffmpeg"
    if not os.path.exists(ff) and shutil.which("ffmpeg") is None:
        fail("no ffmpeg on PATH")
    n = len(meta["directions"][direction]["frames"])
    fps = meta["fps"]
    out = os.path.join(OUT, "%s_%s.mp4" % (view, direction))
    cmd = [ff, "-y", "-loglevel", "error",
           "-framerate", str(fps), "-start_number", "0",
           "-i", os.path.join(FRAMES, "%s_%s_%%04d.png" % (view, direction)),
           "-frames:v", str(n),
           "-c:v", "libx264", "-crf", "17", "-preset", "medium",
           "-pix_fmt", "yuv420p", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
           "-r", str(fps), out]
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        fail("ffmpeg failed for %s_%s:\n%s" % (view, direction, r.stderr.strip()))
    return note(out), n / float(fps)


# --- side-by-side pairs -----------------------------------------------------------------------
def pair_sheet(name, left_png, right_png, left_label, right_label, title, caption, sub=None):
    for p in (left_png, right_png):
        if not os.path.exists(p):
            fail("missing %s - run windowmaterialise_room.py first" % p)
    with Image.open(left_png) as im:
        src_w, src_h = im.size
    cw = 760
    ch = int(round(cw * src_h / float(src_w)))
    eye_h = 40
    W = 2 * cw + 3 * PAD
    measure = ImageDraw.Draw(Image.new("RGB", (8, 8)))
    cap_lines = wrap(measure, caption, F_CAP, W - 2 * PAD - 4)
    sub_lines = wrap(measure, sub, F_SMALL, W - 2 * PAD - 4) if sub else []
    cap_h = 12 + 22 * len(cap_lines) + 19 * len(sub_lines)
    H = TITLE_H + eye_h + ch + cap_h + 3 * PAD
    sheet = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(sheet)

    d.rectangle([0, 0, W, TITLE_H], fill=PANEL)
    text(d, (PAD + 2, TITLE_H // 2), title, F_TITLE, FG, anchor="lm")

    for i, (png, lab) in enumerate(((left_png, left_label), (right_png, right_label))):
        x = PAD + i * (cw + PAD)
        y = TITLE_H + PAD
        text(d, (x + cw // 2, y + eye_h // 2), lab, F_EYE, ACCENT, anchor="mm")
        with Image.open(png) as im:
            sheet.paste(im.convert("RGB").resize((cw, ch), Image.LANCZOS), (x, y + eye_h))
        d.rectangle([x, y + eye_h, x + cw - 1, y + eye_h + ch - 1], outline=(64, 64, 72))

    cy = TITLE_H + PAD + eye_h + ch + PAD + 4
    for line in cap_lines:
        text(d, (PAD + 2, cy), line, F_CAP, FG)
        cy += 22
    for line in sub_lines:
        text(d, (PAD + 2, cy), line, F_SMALL, DIM)
        cy += 19

    p = os.path.join(OUT, name)
    sheet.save(p)
    return note(p)


def main():
    meta = load_meta()
    pairs = load_pairs()

    print("=== windowmaterialise_compose ================================================")
    print("in   %s" % FRAMES)
    print("out  %s" % OUT)
    print("")

    for view in ("front", "oblique"):
        for direction in ("appear", "vanish"):
            p = strip(view, direction, meta)
            print("strip  %s" % p)
    print("")
    for view in ("front", "oblique"):
        for direction in ("appear", "vanish"):
            p, dur = movie(view, direction, meta)
            print("mp4    %s   (%d frames @ %d fps = %.3f s; sim duration %.3f s)"
                  % (p, len(meta["directions"][direction]["frames"]), meta["fps"], dur,
                     meta["directions"][direction]["seconds"]))
    print("")

    # --- the two depth pairs -------------------------------------------------------------------
    if pairs:
        inst = pairs["instant"]
        st, px = pairs["stereo"], pairs["parallax"]
        instant_s = "vanish f%d, t = %.3f s (%.2f x the %.2f s vanish), elements %.2f" % (
            inst["frame"], inst["t"], inst["fraction_of_vanish"], inst["vanish_s"],
            inst["element_progress"])
        ipd, half, lat = st["ipd_m"], px["half_offset_m"], px["lateral_separation_m"]
        dist_st, dist_px = st["dist_m"], px["dist_m"]
        elem = inst["element_progress"]
    else:
        instant_s = "one instant of the vanish"
        ipd, half, lat, dist_st, dist_px = 0.063, 0.28, 0.56, 0.95, 0.95
        elem = 0.0

    # DO NOT PROMISE WHAT THE PICTURE DOES NOT SHOW. The window's own dissolve is finished well
    # before the debris is, so at a late instant there is barely any window left to be the static
    # reference and the caption has to say so - the room takes that job instead. Four art rounds on
    # this effect were lost to a preview being explained away rather than read.
    if elem > 0.5:
        ref = ("The window is %d%% dissolved at this instant, so the STATIC reference here is the "
               "room - the column in front of the window plane, the crate behind it, the table "
               "edge. Watch how far the debris travels against them between A and B, and how the "
               "shards in front of the plane shift opposite to the ones behind it. A decal painted "
               "in the window's plane could not do that." % round(elem * 100))
    else:
        ref = ("The window pivots about the aim point, so its centre barely moves, while the "
               "debris slides hard across it - shards in front of the plane shift one way, shards "
               "behind it the other. A decal painted in the window's plane could not do that.")

    print("pair   %s" % pair_sheet(
        "stereo_pair.png",
        os.path.join(FRAMES, "stereo_L.png"), os.path.join(FRAMES, "stereo_R.png"),
        "LEFT EYE", "RIGHT EYE",
        "STEREO PAIR  -  %s" % instant_s,
        "Parallel pair, IPD %.3f m: the two cameras are offset +-%.4f m along the camera's own "
        "right axis at %.2f m, with NO toe-in." % (ipd, ipd / 2.0, dist_st),
        "Cross-eye or free-fuse them. Every shard sits at a different disparity from the window "
        "plane and from the column - which is only possible if the debris has real depth. Nothing "
        "in the effect faces the camera, so the two eyes see the same geometry from two places."))

    print("pair   %s" % pair_sheet(
        "parallax_pair.png",
        os.path.join(FRAMES, "parallax_A.png"), os.path.join(FRAMES, "parallax_B.png"),
        "HEAD LEANED LEFT  (-%.2f m)" % half, "HEAD LEANED RIGHT  (+%.2f m)" % half,
        "PARALLAX PAIR  -  %s" % instant_s,
        "Same instant, same geometry, two head positions %.2f m apart at %.2f m, both aimed at the "
        "window centre." % (lat, dist_px),
        ref))

    print("")
    print("%d files written." % len(written))
    print("==============================================================================")


main()
