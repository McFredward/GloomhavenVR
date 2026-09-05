#!/usr/bin/env python3
"""Build docs/img/controls-en.png and controls-de.png — the controller map in the playing guide.

WHY THIS IS A SCRIPT AND NOT A HAND-DRAWN IMAGE
-----------------------------------------------
The button map is the one thing a new player needs first, and prose is the worst possible carrier
for it. It also has to exist in two languages, and it has to stay true when a binding moves.

So the picture is split in two:

  controllers-artwork.png   the ARTWORK ONLY — a transparent render of a controller pair with
                            completely blank buttons. Generated once (see docs/img/README.md).
  this script               every WORD and every marker, drawn as real text at build time.

That split is the whole point. A diagram whose labels are baked into the generated bitmap would
have to be regenerated per language — by a model that cannot spell — and could never be corrected
without redrawing the controllers. Here the German page is one more pass over the same artwork, and
a binding that moves is a one-line edit in LABELS below.

THE BINDINGS BELOW COME FROM THE SOURCE, NOT FROM THE DOCS. See the citations on each entry; if you
change one, change the citation with it.

Usage:  python3 docs/img/build-controls-diagram.py
"""

import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, "controllers-artwork.png")

FONT_DIR = "/usr/share/fonts/opentype/inter"
def font(name, size):
    return ImageFont.truetype(os.path.join(FONT_DIR, name), size)

INK       = (26, 22, 19)
INK_SOFT  = (92, 86, 80)
RULE      = (219, 213, 205)
PAPER     = (255, 255, 255)

# One colour per INPUT, used for the ring on the artwork and the swatch in the legend. The ring is
# what ties a legend line to a physical button, so the two must never drift apart.
BLUE   = (37, 99, 235)     # thumbstick — BOTH its entries, because it is one physical stick and a
                           # second colour on it read as a second button rather than a second gesture
ORANGE = (217, 94, 20)     # trigger
CYAN   = (10, 133, 160)    # grip
GREEN  = (5, 130, 90)      # X
RED    = (200, 45, 45)     # A
PURPLE = (124, 58, 237)    # Y + B  (the mod's own accent, as on the README version badge)

# Feature centres in controllers-artwork.png pixel space, read off the render and verified by
# overlaying probe dots. If the artwork is ever regenerated these all move — re-probe, do not guess.
L = {"stick": (126, 76), "Y": (112, 162), "X": (172, 194), "trigger": (133, 324), "grip": (242, 421)}
R = {"stick": (715, 72), "B": (671, 148), "A": (731, 189), "trigger": (718, 324), "grip": (601, 421)}

# Canvas. Rendered at 1720 px and shown at 860, so every size here is twice its on-page size.
W, H     = 1720, 1600   # H is an upper bound; the canvas is cropped to what the legend uses
ART_TOP  = 44
SCALE    = 0.74        # the artwork is drawn at its own resolution; this is how big it lands
GAP_ADD  = 40          # extra air pushed between the two controllers so the rings do not collide
SPLIT_X  = 421         # column in the artwork that falls in the gap between the two controllers

LABELS = {
    "en": {
        "left": "LEFT", "right": "RIGHT",
        "cells": [
            (BLUE, "Thumbstick — push", [
                "Left stick: fly through the room.",
                "Right stick, left or right: turn.",
                "Right stick, pull back: reel a window in.",
            ]),
            (BLUE, "Thumbstick — click in", [
                "Hold it in and move your hand: pull yourself along.",
                "Both sticks at once: rotate and zoom the table.",
            ]),
            (ORANGE, "Trigger — either hand", [
                "Take a card, pick a figure up.",
                "With the laser: point and click across the table.",
            ]),
            (CYAN, "Grip — hold, either hand", [
                "Touch a hex with your fingertip.",
                "Grab a bar to move a window or board.",
            ]),
            (GREEN, "X — short tap", [
                "Opens and closes the options menu.",
            ]),
            (RED, "A", [
                "Ping a hex so everyone sees it.",
            ]),
            (PURPLE, "Y + B — hold one second", [
                "Recentre yourself where you stand.",
            ]),
        ],
        "note": "A Quest 3 is shown; every supported controller has the same keys in the same "
                "places. Handedness, turning and every binding above are changeable in VR Options "
                "— and the tutorial in the game teaches all of it without you reading anything.",
    },
    "de": {
        "left": "LINKS", "right": "RECHTS",
        "cells": [
            (BLUE, "Thumbstick — drücken", [
                "Linker Stick: durch den Raum fliegen.",
                "Rechter Stick, links oder rechts: drehen.",
                "Rechter Stick, zurückziehen: Fenster heranholen.",
            ]),
            (BLUE, "Thumbstick — eindrücken", [
                "Gedrückt halten und die Hand bewegen: dich heranziehen.",
                "Beide Sticks zugleich: Tisch drehen und zoomen.",
            ]),
            (ORANGE, "Trigger — beide Hände", [
                "Karte aus dem Fächer nehmen, Figur hochheben.",
                "Mit dem Laser: quer über den Tisch zeigen und klicken.",
            ]),
            (CYAN, "Grip — halten, beide Hände", [
                "Ein Feld mit der Fingerspitze berühren.",
                "An der Stange Fenster oder Brett bewegen.",
            ]),
            (GREEN, "X — kurz antippen", [
                "Öffnet und schließt das Optionen-Menü.",
            ]),
            (RED, "A", [
                "Ein Feld markieren, sodass alle es sehen.",
            ]),
            (PURPLE, "Y + B — eine Sekunde halten", [
                "Dich neu zentrieren, wo du gerade stehst.",
            ]),
        ],
        "note": "Abgebildet ist eine Quest 3; jeder unterstützte Controller hat dieselben Tasten "
                "an denselben Stellen. Händigkeit, Drehen und jede Belegung oben lassen sich in den "
                "VR-Optionen ändern — und das Tutorial im Spiel bringt dir alles davon bei.",
    },
}


def ring(d, xy, colour, r=19, w=7):
    """A RING, never a filled disc: the reader has to be able to see the button underneath it."""
    x, y = xy
    d.ellipse([x - r - w, y - r - w, x + r + w, y + r + w], outline=PAPER, width=4)
    d.ellipse([x - r, y - r, x + r, y + r], outline=colour, width=w)


def build(lang):
    art = Image.open(ART).convert("RGBA")
    art = art.resize((round(art.width * SCALE), round(art.height * SCALE)), Image.LANCZOS)
    split = round(SPLIT_X * SCALE)
    left_sprite = art.crop((0, 0, split, art.height))
    right_sprite = art.crop((split, 0, art.width, art.height))

    block_w = art.width + GAP_ADD
    lx = (W - block_w) // 2
    rx = lx + split + GAP_ADD
    ty = ART_TOP

    im = Image.new("RGB", (W, H), PAPER)
    im.paste(left_sprite, (lx, ty), left_sprite)
    im.paste(right_sprite, (rx, ty), right_sprite)
    d = ImageDraw.Draw(im)

    def lp(k):   # a left-controller feature, in canvas space
        return (round(L[k][0] * SCALE) + lx, round(L[k][1] * SCALE) + ty)

    def rp(k):   # a right-controller feature, in canvas space
        return (round(R[k][0] * SCALE) - split + rx, round(R[k][1] * SCALE) + ty)

    # --- the rings on the artwork ---------------------------------------------------------
    for p in (lp("stick"), rp("stick")):
        ring(d, p, BLUE, r=19, w=7)
    for p in (lp("trigger"), rp("trigger")):
        ring(d, p, ORANGE)
    for p in (lp("grip"), rp("grip")):
        ring(d, p, CYAN)
    ring(d, lp("X"), GREEN, r=16, w=7)
    ring(d, rp("A"), RED, r=16, w=7)
    ring(d, lp("Y"), PURPLE, r=16, w=7)
    ring(d, rp("B"), PURPLE, r=16, w=7)

    txt = LABELS[lang]

    # --- LEFT / RIGHT under each controller -----------------------------------------------
    f_hand = font("Inter-SemiBold.otf", 30)
    hand_y = ty + art.height + 6
    for label, cx in ((txt["left"], lx + split // 2 + 20), (txt["right"], rx + (art.width - split) // 2)):
        spaced = " ".join(label)
        w = d.textlength(spaced, font=f_hand)
        d.text((cx - w / 2, hand_y), spaced, font=f_hand, fill=INK_SOFT)

    # --- the legend -----------------------------------------------------------------------
    f_title = font("Inter-SemiBold.otf", 29)
    f_body = font("Inter-Regular.otf", 25)
    f_note = font("Inter-Regular.otf", 24)

    rule_y = hand_y + 62
    d.line([(56, rule_y), (W - 56, rule_y)], fill=RULE, width=2)

    col_x = [56, 616, 1176]
    col_w = 490

    # EVERY string here is wrapped against the width it is actually given. German is 15-30 % longer
    # than the English it is set beside, and a legend that silently overruns its column is how a
    # translated diagram starts printing one cell's words on top of the next one's.
    def wrap(text, f, width):
        out, cur = [], ""
        for word in text.split():
            trial = (cur + " " + word).strip()
            if cur and d.textlength(trial, font=f) > width:
                out.append(cur)
                cur = word
            else:
                cur = trial
        out.append(cur)
        return out

    def cell(x, y, colour, title, lines, measure=False):
        yy = y
        for i, ln in enumerate(wrap(title, f_title, col_w - 40)):
            if not measure:
                if i == 0:
                    d.ellipse([x, yy + 6, x + 20, yy + 26], outline=colour, width=7)
                d.text((x + 40, yy), ln, font=f_title, fill=INK)
            yy += 38
        yy += 4
        for line in lines:
            for ln in wrap(line, f_body, col_w - 40):
                if not measure:
                    d.text((x + 40, yy), ln, font=f_body, fill=INK_SOFT)
                yy += 32
        return yy

    # Row pitch follows the TALLEST cell in the row, in the language being drawn.
    rows = [txt["cells"][0:3], txt["cells"][3:6], txt["cells"][6:7]]
    y = rule_y + 40
    for row in rows:
        bottom = y
        for c, (colour, title, lines) in enumerate(row):
            bottom = max(bottom, cell(col_x[c], y, colour, title, lines))
        if len(row) == 1:                       # the last row is one cell wide; the note fills the rest
            ny = y + 6
            for ln in wrap(txt["note"], f_note, W - 56 - col_x[1]):
                d.text((col_x[1], ny), ln, font=f_note, fill=INK_SOFT)
                ny += 33
            bottom = max(bottom, ny)
        y = bottom + 34

    out = os.path.join(HERE, "controls-%s.png" % lang)
    im = im.crop((0, 0, W, min(H, y + 12)))
    im.quantize(colors=200, method=Image.FASTOCTREE).save(out, optimize=True)
    print("wrote %s  (%d x %d, %d kB)" % (out, im.width, im.height, os.path.getsize(out) // 1024))


if __name__ == "__main__":
    for lang in ("en", "de"):
        build(lang)
