#!/usr/bin/env python3
"""Build docs/img/install-tree-en.png and install-tree-de.png — the "did it land in the right
place?" picture in the install guide.

WHY A PICTURE AND NOT A CODE FENCE
----------------------------------
The install guide used to draw this tree in a fenced code block. A fence is monochrome, so the
reader has to *read* nine lines to find the two that matter. Here the two folders that decide
whether the install worked are the only coloured things on the page, and a reader who looks at
nothing else still checks the right two folders.

Everything is drawn as real text, in two languages, from the same table — same reason as
build-controls-diagram.py: a translated screenshot rots, a translated draw call does not.

Usage:  python3 docs/img/build-install-tree.py
"""

import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONT_DIR = "/usr/share/fonts/opentype/inter"


def font(name, size):
    return ImageFont.truetype(os.path.join(FONT_DIR, name), size)


INK      = (26, 22, 19)
INK_SOFT = (120, 113, 105)
PAPER    = (255, 255, 255)
GUIDE    = (216, 210, 202)
FOLDER   = (198, 141, 42)
FILE     = (162, 157, 150)
GO       = (10, 122, 79)
GO_BG    = (232, 246, 238)

W = 1420
PITCH = 54
PAD_X = 56
PAD_Y = 40
INDENT = 62

# (indent, kind, name, note-key, highlighted)
ROWS = [
    (0, "dir",  "Gloomhaven\\",                 "root",   False),
    (1, "file", "GH.exe",                       None,     False),
    (1, "file", "INSTALL.txt",                  "zip",    False),
    (1, "file", "INSTALL-DEUTSCH.txt",          None,     False),
    (1, "dir",  "BepInEx\\",                    None,     False),
    (2, "dir",  "plugins\\GloomhavenVR\\",      None,     True),
    (2, "dir",  "patchers\\GloomhavenVR\\",     None,     True),
    (2, "dir",  "config\\",                     "cfg",    False),
    (2, "file", "LogOutput.log",                "log",    False),
]

TEXT = {
    "en": {
        "notes": {
            "root": "the folder GH.exe is in",
            "zip":  "these two came out of the mod's zip",
            "cfg":  "every setting, also as plain text",
            "log":  "proof BepInEx is working",
        },
        "caption": "Both green folders must exist. If either one is missing, unpack the zip into "
                   "the Gloomhaven folder again — never move files there by hand.",
    },
    "de": {
        "notes": {
            "root": "der Ordner, in dem GH.exe liegt",
            "zip":  "die beiden kommen aus dem Zip der Mod",
            "cfg":  "jede Einstellung, auch als Textdatei",
            "log":  "der Beweis, dass BepInEx läuft",
        },
        "caption": "Beide grünen Ordner müssen da sein. Fehlt einer, entpack das Zip noch einmal in "
                   "den Gloomhaven-Ordner — verschieb dort niemals Dateien von Hand.",
    },
}


def folder_icon(d, x, y, colour):
    d.polygon([(x, y + 6), (x + 12, y + 6), (x + 16, y + 1), (x + 30, y + 1),
               (x + 30, y + 6), (x, y + 6)], fill=colour)
    d.rounded_rectangle([x, y + 5, x + 30, y + 25], radius=3, fill=colour)


def file_icon(d, x, y, colour):
    d.rounded_rectangle([x + 5, y, x + 25, y + 26], radius=3, fill=colour)
    d.polygon([(x + 25, y), (x + 25, y + 8), (x + 17, y)], fill=PAPER)


def build(lang):
    t = TEXT[lang]
    f_row = font("Inter-Regular.otf", 28)
    f_hit = font("Inter-SemiBold.otf", 28)
    f_note = font("Inter-Regular.otf", 23)
    f_cap = font("Inter-Regular.otf", 25)

    body_h = PAD_Y * 2 + PITCH * len(ROWS)
    im = Image.new("RGB", (W, body_h + 110), PAPER)
    d = ImageDraw.Draw(im)

    for i, (ind, kind, name, note, hit) in enumerate(ROWS):
        y = PAD_Y + i * PITCH
        x = PAD_X + ind * INDENT

        if hit:
            d.rounded_rectangle([x - 14, y - 8, W - PAD_X, y + 40], radius=10, fill=GO_BG)

        # the elbow into the parent row
        if ind:
            gx = PAD_X + (ind - 1) * INDENT + 15
            up = PAD_Y + (i - 1) * PITCH
            d.line([(gx, up + 30), (gx, y + 16)], fill=GUIDE, width=2)
            d.line([(gx, y + 16), (x - 6, y + 16)], fill=GUIDE, width=2)

        colour = GO if hit else (FOLDER if kind == "dir" else FILE)
        (folder_icon if kind == "dir" else file_icon)(d, x, y + 3, colour)
        d.text((x + 44, y), name, font=f_hit if hit else f_row, fill=GO if hit else INK)

        tx = x + 54 + d.textlength(name, font=f_hit if hit else f_row)
        if hit:
            # Drawn, not typed: Inter has no U+2714 and a missing glyph renders as a tofu box —
            # which on the one row that says "this is the thing to check" is the worst place for it.
            d.line([(tx + 4, y + 16), (tx + 12, y + 25), (tx + 28, y + 4)], fill=GO, width=5,
                   joint="curve")
        elif note:
            d.text((tx, y + 4), "←  " + t["notes"][note], font=f_note, fill=INK_SOFT)

    cap_y = body_h - 4
    words, cur = t["caption"].split(), ""
    for word in words:
        trial = (cur + " " + word).strip()
        if cur and d.textlength(trial, font=f_cap) > W - 2 * PAD_X:
            d.text((PAD_X, cap_y), cur, font=f_cap, fill=INK_SOFT)
            cap_y += 34
            cur = word
        else:
            cur = trial
    d.text((PAD_X, cap_y), cur, font=f_cap, fill=INK_SOFT)

    im = im.crop((0, 0, W, cap_y + 50))
    out = os.path.join(HERE, "install-tree-%s.png" % lang)
    im.quantize(colors=64, method=Image.FASTOCTREE).save(out, optimize=True)
    print("wrote %s  (%d x %d, %d kB)" % (out, im.width, im.height, os.path.getsize(out) // 1024))


if __name__ == "__main__":
    for lang in ("en", "de"):
        build(lang)
