#!/usr/bin/env python3
"""Build docs/img/env-styles-en.png and env-styles-de.png — the ENVIRONMENT picker, one tile per room.

WHY THIS IS A SCRIPT AND NOT A HAND-ASSEMBLED IMAGE
---------------------------------------------------
The same reason build-board-diagram.py and build-controls-diagram.py give, and it applies here word
for word: the picture has to exist in two languages, and it has to stay true when the option list
moves.

  env-tile-*.jpg   the ARTWORK ONLY — five in-headset captures, cropped so the MAP sits in the
                   middle of each, no words anywhere in them.
  this script      every WORD, drawn as real Inter text at build time, per language.

THE SOURCE CAPTURES AND WHY THEY NEEDED CROPPING. The maintainer shot all five from the same seat
and said so himself: *"Ich habe versucht immer aus dem gleichen Winkel ein Screenshot zu machen -
das ist mir nicht ganz gelungen."* They are hand-held VR captures, so the board lands in a different
place and at a slightly different apparent size in each. The crop is therefore not a framing
preference, it is the thing that makes the five COMPARABLE: each window is centred on that shot's
board and sized as a fixed multiple of that shot's board width, so the board occupies the same
fraction of every tile and the eye is left comparing the ROOM, which is the only variable the
picture is about. The board centres and widths are read off a coordinate-grid overlay of each
capture and recorded in CROPS below; re-shooting a room means re-reading those four numbers for it
and nothing else.

THE NAMES COME FROM Loc.cs, VERBATIM, AND THAT IS THE POINT. A gallery whose captions drift from the
menu rows is a lookup, not a glance — the same argument the control-board callouts are written
under. `sky_default` / `sky_cellar` / `sky_swamp` / `sky_off` are the four SkyStyle rows and
`vr_o_mrenabled` is the mixed-reality tile, in the order VariantTilesTable.EnvironmentTiles builds
them. If a row is renamed there, rename it here in the same commit.

THE TWO-ROW SPLIT IS MEANINGFUL, not a way to fill a grid. The top row is the three ROOMS the mod
can put around the table; the bottom row is the two ways of having NO room, and those two get the
wider tiles because what they show is the absence of one.
"""

import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONT_DIR = "/usr/share/fonts/opentype/inter"


def font(name, size):
    return ImageFont.truetype(os.path.join(FONT_DIR, name), size)


# ---- layout ---------------------------------------------------------------------------------
# 1440 wide so the README can render it at 720 on a 2x display without softening.
W = 1440
GAP = 12
TOP_W = (W - 2 * GAP) // 3          # 472
TOP_H = round(TOP_W * 531 / 944)    # 266, the tile artwork's own aspect
BOT_W = (W - GAP) // 2              # 714
BOT_H = round(BOT_W * 797 / 1416)   # 402
H = TOP_H + GAP + BOT_H

BG = (14, 13, 12)
INK = (238, 233, 224)
SUB = (176, 168, 154)

# key, English name, German name, English gloss, German gloss
TILES = [
    ("default", "Default", "Standard",
     "the game's own scenario sky", "der Szenario-Himmel des Spiels"),
    ("cellar", "Cellar", "Keller",
     "a candle-lit stone room", "ein kerzenbeleuchteter Steinraum"),
    ("forest", "Night forest", "Nachtwald",
     "a moonlit swamp under a real star catalogue", "Mondsumpf unter echtem Sternkatalog"),
    ("off", "Off (black)", "Aus (schwarz)",
     "nothing around the table at all", "gar nichts rund um den Tisch"),
    ("mr", "Mixed Reality", "Mixed Reality an",
     "a green key for your streaming app",
     "ein Greenscreen für die Streaming-App"),
]


def caption(img, box, name, gloss, big):
    """A bottom-anchored caption over a darkened strip, inside the tile itself.

    Over the tile rather than beneath it because the five tiles are two different sizes: a caption
    band outside them would make the two rows different heights for a reason the reader cannot see.
    """
    x, y, w, h = box
    band_h = 92 if big else 64
    strip = Image.new("RGBA", (w, band_h), (0, 0, 0, 0))
    sd = ImageDraw.Draw(strip)
    for i in range(band_h):                      # a soft ramp, so no hard edge crosses the art
        a = int(238 * (i / (band_h - 1)) ** 1.25)
        sd.line([(0, i), (w, i)], fill=(8, 7, 6, a))
    img.paste(strip, (x, y + h - band_h), strip)

    d = ImageDraw.Draw(img)
    f_name = font("Inter-SemiBold.otf", 27 if big else 22)
    f_gloss = font("Inter-Regular.otf", 19 if big else 16)
    pad = 20 if big else 16
    ty = y + h - band_h + (26 if big else 14)
    d.text((x + pad, ty), name, font=f_name, fill=INK)
    d.text((x + pad, ty + (32 if big else 26)), gloss, font=f_gloss, fill=SUB)


def build(lang):
    sheet = Image.new("RGB", (W, H), BG)
    for i, (key, en, de, gen, gde) in enumerate(TILES):
        art = Image.open(os.path.join(HERE, f"env-tile-{key}.jpg")).convert("RGB")
        big = i >= 3
        w, h = (BOT_W, BOT_H) if big else (TOP_W, TOP_H)
        x = (i - 3) * (BOT_W + GAP) if big else i * (TOP_W + GAP)
        y = TOP_H + GAP if big else 0
        sheet.paste(art.resize((w, h), Image.LANCZOS), (x, y))
        caption(sheet, (x, y, w, h), en if lang == "en" else de,
                gen if lang == "en" else gde, big)
    out = os.path.join(HERE, f"env-styles-{lang}.png")
    sheet.save(out, optimize=True)
    print(f"{out}  {W}x{H}  {os.path.getsize(out):,} bytes")


if __name__ == "__main__":
    build("en")
    build("de")
