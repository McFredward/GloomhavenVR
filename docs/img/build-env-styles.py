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
preference, it is the thing that makes the five COMPARABLE: every window is sized as the SAME
multiple of that shot's own board width (K = 2.6), so the board occupies the same fraction of every
tile and the eye is left comparing the ROOM, which is the only variable the picture is about.

AND THE WINDOW HANGS UPWARD FROM THE BOARD RATHER THAN CENTRING ON IT. Maintainer, second pass:
*"Schneide die Perspektive so, dass das Spielfeld am unteren Rand ist bei allen Bildern so dass man
bei unseren maps mehr sieht nach oben hinweg."* The board's BOTTOM edge is placed just above the
caption band - the lowest it can sit and still leave the words readable - and everything the window
gains, it gains above the board, which is where the cellar's brazier and wall and the forest's
trunks and light shafts live. A first attempt at this moved the window the wrong way: in the
centred crop the board's bottom already sat at ~0.79 of the frame, so pinning it at 0.67 raised it
and cut the brazier off the top. Seeing more upward needs a WIDER window, not a shifted one - hence
K going 2.0 -> 2.6 in the same change.

THE CROP NUMBERS, so a re-shoot is four numbers and not a re-derivation. Per capture: the board's
centre X, its BOTTOM edge Y and its width, all in 3840x2160 source pixels and all read off a
coordinate-grid overlay; plus the fraction of the tile height the board's bottom should land at,
which differs by row because the two caption bands are different heights against different tile
heights (122/402 wide, 64/266 narrow) and one fraction would put the board through the words on one
row and leave a gap on the other.

    cellar   cx 2022  bottom 1470  width 1160   f 0.667   -> lands 0.667
    forest   cx 1990  bottom 1750  width 1250   f 0.667   -> lands 0.776 (clamped: only 410 px of
                                                            source lie below that board)
    default  cx 2048  bottom 1560  width 1290   f 0.729   -> lands 0.729
    off      cx 1974  bottom 1650  width 1330   f 0.729   -> lands 0.738
    mr       cx 2103  bottom 1660  width 1340   f 0.729   -> lands 0.745

THE CAPTIONS SAY WHAT AN OPTION IS, NOT WHAT IT FEELS LIKE. The first version read "a moonlit
swamp under a real star catalogue", and the maintainer put it in the same category as the page
headings he had already had rewritten: a caption on a picker tile is a label, and a label that has
to be decoded is not doing its job. The star catalogue is a real technical fact and it stays; it
just stops being scenery.

THE NAMES COME FROM Loc.cs, VERBATIM, AND THAT IS THE POINT. A gallery whose captions drift from the
menu rows is a lookup, not a glance — the same argument the control-board callouts are written
under. `sky_default` / `sky_cellar` / `sky_swamp` / `sky_off` are the four SkyStyle rows and
`vr_o_mrenabled` is the mixed-reality tile, in the order VariantTilesTable.EnvironmentTiles builds
them. If a row is renamed there, rename it here in the same commit.

THE TWO-ROW SPLIT IS MEANINGFUL, not a way to fill a grid, AND THE MAINTAINER SET ITS DIRECTION:
"In der Matrix der Umgebungsbilder will ich das die zwei eigenen Umgebungen (wald und Keller)
hervorgehoben sind und groesser sind als die anderen nicht Off und mixed-reality." So the top row is
the two rooms BUILT FOR THIS MOD, at nearly half the sheet each and wearing an accent rule and an
eyebrow; the bottom row is the three that are not rooms at all - the game's own sky, no sky, and the
green key. Size on this sheet is a claim about what the project MADE, and it should not be spent on
the absence of a room. The first version had it the other way round and was wrong.
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
TOP_W = (W - GAP) // 2              # 714 - the two rooms this project built
TOP_H = round(TOP_W * 797 / 1416)   # 402
BOT_W = (W - 2 * GAP) // 3          # 472 - the three that are not rooms
BOT_H = round(BOT_W * 531 / 944)    # 266
H = TOP_H + GAP + BOT_H

BG = (14, 13, 12)
INK = (238, 233, 224)
SUB = (176, 168, 154)
# The warm accent the mod's own board furniture uses. It marks the two tiles, nothing else.
ACCENT = (226, 168, 74)

# key, English name, German name, English gloss, German gloss. The first TWO are the mod's own
# rooms and take the wide top row; the order inside each row follows VariantTilesTable's picker.
TILES = [
    ("cellar", "Cellar", "Keller",
     "Indoor room, firelight and ambient sound",
     "Innenraum, Feuerschein und Umgebungsklang"),
    ("forest", "Night forest", "Nachtwald",
     "Outdoors at night, star field from a real catalogue",
     "Außen bei Nacht, Sternfeld aus einem echten Katalog"),
    ("default", "Default", "Standard",
     "The game's own scenario sky", "Der Szenario-Himmel des Spiels"),
    ("off", "Off (black)", "Aus (schwarz)",
     "No environment at all", "Gar keine Umgebung"),
    ("mr", "Mixed Reality", "Mixed Reality an",
     "Green key for passthrough or streaming",
     "Greenscreen für Passthrough oder Streaming"),
]

# Only the two rooms wear it. It says what the size is already saying, in words.
EYEBROW = {"en": "BUILT FOR THIS MOD", "de": "FÜR DIESEN MOD GEBAUT"}


def caption(img, box, name, gloss, big, eyebrow=None):
    """A bottom-anchored caption over a darkened strip, inside the tile itself.

    Over the tile rather than beneath it because the five tiles are two different sizes: a caption
    band outside them would make the two rows different heights for a reason the reader cannot see.
    """
    x, y, w, h = box
    band_h = 122 if big else 64
    strip = Image.new("RGBA", (w, band_h), (0, 0, 0, 0))
    sd = ImageDraw.Draw(strip)
    for i in range(band_h):                      # a soft ramp, so no hard edge crosses the art
        a = int(238 * (i / (band_h - 1)) ** 1.25)
        sd.line([(0, i), (w, i)], fill=(8, 7, 6, a))
    img.paste(strip, (x, y + h - band_h), strip)

    d = ImageDraw.Draw(img)
    f_name = font("Inter-SemiBold.otf", 31 if big else 21)
    f_gloss = font("Inter-Regular.otf", 20 if big else 15)
    pad = 22 if big else 16
    ty = y + h - band_h + (20 if big else 14)
    if eyebrow:
        d.text((x + pad, ty), eyebrow, font=font("Inter-SemiBold.otf", 15), fill=ACCENT)
        ty += 27
    # A CAPTION THAT DOES NOT FIT IS A BUILD FAILURE, NOT A CLIPPED LINE. German runs longer than
    # English almost everywhere in this project and the first version of this sheet shipped
    # "seltene Ereigni" with the rest of the word off the tile. Nothing here wraps or shrinks to
    # fit on purpose: a caption that needs either is a caption that is too long, and the fix is the
    # wording in TILES, not the layout.
    for line, f in ((name, f_name), (gloss, f_gloss)):
        over = pad + d.textlength(line, font=f) - (w - pad)
        if over > 0:
            raise SystemExit(
                f"caption overflows its tile by {over:.0f}px: {line!r} — shorten it in TILES")
    d.text((x + pad, ty), name, font=f_name, fill=INK)
    d.text((x + pad, ty + (39 if big else 25)), gloss, font=f_gloss, fill=SUB)

    # THE HIGHLIGHT ITSELF: a hairline of the accent along the tile's top edge. A border all the way
    # round would read as a SELECTION state - the mod's own variant picker uses one for the live
    # style - and this sheet is not a picker. A single rule reads as emphasis and nothing else.
    if eyebrow:
        d.rectangle([x, y, x + w - 1, y + 2], fill=ACCENT)


def build(lang):
    sheet = Image.new("RGB", (W, H), BG)
    for i, (key, en, de, gen, gde) in enumerate(TILES):
        art = Image.open(os.path.join(HERE, f"env-tile-{key}.jpg")).convert("RGB")
        big = i < 2
        w, h = (TOP_W, TOP_H) if big else (BOT_W, BOT_H)
        x = i * (TOP_W + GAP) if big else (i - 2) * (BOT_W + GAP)
        y = 0 if big else TOP_H + GAP
        sheet.paste(art.resize((w, h), Image.LANCZOS), (x, y))
        caption(sheet, (x, y, w, h), en if lang == "en" else de,
                gen if lang == "en" else gde, big,
                eyebrow=EYEBROW[lang] if big else None)
    out = os.path.join(HERE, f"env-styles-{lang}.png")
    sheet.save(out, optimize=True)
    print(f"{out}  {W}x{H}  {os.path.getsize(out):,} bytes")


if __name__ == "__main__":
    build("en")
    build("de")
