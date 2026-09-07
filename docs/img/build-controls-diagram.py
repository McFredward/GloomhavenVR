#!/usr/bin/env python3
"""Build docs/img/controls-en.png and controls-de.png — the controller map in the playing guide.

WHY THIS IS A SCRIPT AND NOT A HAND-DRAWN IMAGE
-----------------------------------------------
The button map is the one thing a new player needs first, and prose is the worst possible carrier
for it. It also has to exist in two languages, and it has to stay true when a binding moves.

So the picture is split in two:

  controllers-artwork.png   the ARTWORK ONLY — a transparent render of a Meta Quest Touch Plus pair
                            with completely blank buttons. Generated once (see docs/img/README.md).
  this script               every WORD and every marker, drawn as real text at build time.

That split is the whole point. A diagram whose labels are baked into the generated bitmap would
have to be regenerated per language — by a model that cannot spell — and could never be corrected
without redrawing the controllers. Here the German page is one more pass over the same artwork, and
a binding that moves is a one-line edit in LABELS below.

THE CALLOUTS ON THE PICTURE ARE NOT A BREACH OF THAT RULE — read the rule precisely, exactly as
build-board-diagram.py sets it out. What is forbidden is a word BAKED INTO THE GENERATED BITMAP.
CALLOUTS is none of that: real Inter text drawn by this script, per language, anchored to the same
feature coordinates the rings are drawn from, and every one of them a one-line edit. The user asked
for it for this picture in the same words he used for the board — "Ich will es auch so gestalten das
eine Beschriftung im Bild schon vorhanden ist und man auf einem Blick schon das meiste sieht so wie
du es beim board auch gemacht hast" — because the colour was the ONLY bridge from a ring to a legend
cell, and a colour lookup is not a glance. The colour coding stays and every callout wears its
ring's colour, so the picture and the legend reinforce each other instead of being two halves of a
lookup. Do not "fix" the callouts back out.

WHERE EVERY BINDING IS NAMED, AND WHY IT IS NAMED ONLY ONCE
-----------------------------------------------------------
A controller pair is not a control board: every physical part exists TWICE, and the naive labelling
writes "Trigger" and "Grip" once per hand. That is noise — it says nothing except that the pair is
a pair — so this picture never writes the same word twice. Instead the PLACE of a name carries a
second piece of information:

  * OUTSIDE, beside one controller — a binding that is that hand's alone. The two thumbsticks do
    different things (left flies, right turns), and X and A are one hand's button each, so each of
    those is named beside the hand it belongs to.
  * IN THE MIDDLE, with a leader running to EACH hand — a binding that is identical on both. Two
    lines leaving one name is the picture saying "same control, both hands", which writing "either
    hand" twice is not. The stick CLICK, the Y+B chord and the grip are named this way.

The one exception is the TRIGGER, and it is geometric rather than editorial. Both ways of putting it
in the lane were built and both were REJECTED BY THE GUARD, not by taste:

  * level with the trigger, the lane is at its narrowest — the two plates bulge inward above the
    necks — and the words cannot be set there without being drawn ON a controller;
  * lower down, where the lane is wide enough for them, the leader that reaches back up to the
    trigger passes straight THROUGH the grip ring, which reads as pointing at the grip.

So the trigger is named once, beside the left hand, and its WORDS carry the "either hand" its lines
cannot. If the artwork ever changes so that both triggers are reachable from the middle, move it
there — the guard will tell you when they are.

THE THUMBSTICK IS ONE PART WITH TWO GESTURES, and the picture must not imply one action. It has one
colour — a second colour on one physical stick read as a second button rather than as a second
gesture; that was tried and it was worse — so instead it carries TWO callouts in that one colour:
the push, named per hand outside, and the click, named once above with a line into both sticks. That
is the same shape as the two blue legend cells, and it is why the stick rings have two leaders each.

THE BINDINGS BELOW COME FROM THE SOURCE, NOT FROM THE DOCS. See the citations on each entry; if you
change one, change the citation with it.

THE FEATURE COORDINATES ARE RE-PROBED PIXELS IN THE ARTWORK BITMAP, not measurements of a real
object — the artwork is a generated render and has no published frame the way the board's does.
Regenerating the artwork moves all ten of them, and it moves every callout and every leader with
them, because those are anchored in the same artwork pixel space (see AX/AY). Re-probe, do not
guess: check_geometry() samples the artwork under every anchor and refuses to build if a ring has
stopped landing on the part it names.

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

# One colour per INPUT, used for the ring on the artwork, the callout beside it and the swatch in
# the legend. The ring is what ties a legend line to a physical button, so the three must never
# drift apart.
BLUE   = (37, 99, 235)     # thumbstick — BOTH its entries, because it is one physical stick and a
                           # second colour on it read as a second button rather than a second gesture
ORANGE = (217, 94, 20)     # trigger
CYAN   = (10, 133, 160)    # grip
GREEN  = (5, 130, 90)      # X
RED    = (200, 45, 45)     # A
PURPLE = (124, 58, 237)    # Y + B  (the mod's own accent, as on the README version badge)

# ---------------------------------------------------------------------------------------------
# THE ARTWORK'S OWN FRAME. Everything below — rings, callouts, leaders — is expressed in these
# pixels, so a change to SCALE, to the gap between the hands or to the page width moves all of them
# together. Only a NEW ARTWORK moves them relative to each other, and check_geometry() is what
# catches that.
ART_W, ART_H = 980, 729
SPLIT_X = 490              # the column that falls between the two controllers — exactly half of the
                           # artwork, because each controller is packed centred in its own half.
                           # That is what lets LEFT / RIGHT below centre with no fudge.

# Feature centres in controllers-artwork.png pixel space, read off the render and verified by
# overlaying probe dots. The layout is the real Touch Plus one: the thumbstick sits at the OUTER top
# of the black plate, the two lettered buttons run down its INNER side on a diagonal, and the
# unbound button (menu on the left, Meta on the right) sits below the stick — it carries no binding
# and gets no ring. The two halves are exact mirrors about SPLIT_X.
L = {"stick": (144, 106), "Y": (285, 113), "X": (248, 181), "trigger": (224, 345), "grip": (296, 393)}
R = {"stick": (836, 106), "B": (695, 113), "A": (732, 181), "trigger": (756, 345), "grip": (684, 393)}

# Canvas. Rendered at 1720 px and shown at 860, so every size here is twice its on-page size.
W        = 1720
MARGIN   = 56
SCALE    = 0.76        # the artwork is drawn at its own resolution; this is how big it lands
GAP      = 180         # the lane between the two controllers. It is this wide because the shared
                       # bindings are named IN it: at 28 px, which is all the picture needed when
                       # every word lived in the legend, the gutter held a leader and nothing else.
TOP_BAND = 178         # room above the controllers for the two names that belong to both hands
LX       = round((W - (ART_W * SCALE + GAP)) / 2.0)

# Ring geometry, in canvas pixels. OUTER is the halo's outside edge — what a leader has to keep
# clear of, and what a callout block has to keep clear of.
RING_R    = {"stick": 19, "trigger": 19, "grip": 19, "X": 16, "Y": 16, "A": 16, "B": 16}
RING_W    = 7
RING_HALO = 4
def ring_outer(key):
    return RING_R[key] + RING_W + RING_HALO


def AX(x):
    """Artwork x -> canvas x. The gap is a ramp across the one-pixel seam at SPLIT_X, so the seam's
    own midpoint (SPLIT_X + 0.5) lands on the middle of the lane — which is where the callouts that
    belong to both hands are centred."""
    return LX + x * SCALE + GAP * min(max(x - SPLIT_X, 0.0), 1.0)


def AY(y):
    return TOP_BAND + y * SCALE


MID = SPLIT_X + 0.5        # artwork x of the lane's centre

# ---------------------------------------------------------------------------------------------
# THE CALLOUTS. Everything here is in ARTWORK pixels, from the same table the rings are drawn from.
#
#   at       where the text block starts: (x, y-of-its-TOP-line). x is the block's left edge for
#            align "l", its right edge for "r", its centre for "c".
#   maxw     the width the text is wrapped against, in CANVAS pixels. A hard limit: a wrapped line
#            that still overruns raises, because that is how the German version of a diagram starts
#            printing one label over the next one.
#   rows     how many lines the gap this label sits in can absorb, and the OTHER half of that guard.
#            A too-long German string usually does not overrun `maxw` — wrap() takes it — it grows
#            DOWNWARD into whatever is under it, which no width check can see.
#   leads    one polyline per hand this name reaches, in artwork pixels, each ENDING ON the anchor
#            it names. The start point is derived from the drawn text block, so a leader always
#            leaves the words at the right edge and never has to be re-aimed by hand.
#
# WHY THE VERTICAL POSITIONS ARE WHAT THEY ARE. A leader leaves a text block horizontally only if
# the block's own line of text is level with the target; that is why the stick names sit level with
# the sticks and the X / A names level with those buttons, 57 px lower. The two names in the top
# band drop into the plate from above — the wider blue staple over the narrower purple one, so the
# two never cross — and the grip's two short wings come up out of the lane. Nothing here is aimed by
# eye: check_geometry() re-derives every block and every segment and refuses to draw a line that
# passes through a ring it does not name.
CALLOUT_GEOM = {
    "stick_click": dict(colour=BLUE,   at=(MID, -205), align="c", maxw=520, rows=1,
                        leads=[[(144, -115), (144, 106)], [(836, -115), (836, 106)]]),
    "recentre":    dict(colour=PURPLE, at=(MID,  -87), align="c", maxw=430, rows=1,
                        leads=[[(285, -30), (285, 113)], [(695, -30), (695, 113)]]),
    "left_stick":  dict(colour=BLUE,   at=(-14,   60), align="r", maxw=326, rows=2,
                        leads=[[(144, 106)]]),
    "options":     dict(colour=GREEN,  at=(-14,  173), align="r", maxw=326, rows=2,
                        leads=[[(248, 181)]]),
    "trigger":     dict(colour=ORANGE, at=(-14,  323), align="r", maxw=326, rows=3,
                        leads=[[(224, 345)]]),
    "right_stick": dict(colour=BLUE,   at=(994,   60), align="l", maxw=326, rows=2,
                        leads=[[(836, 106)]]),
    "ping":        dict(colour=RED,    at=(994,  173), align="l", maxw=326, rows=2,
                        leads=[[(732, 181)]]),
    "grip":        dict(colour=CYAN,   at=(MID,  430), align="c", maxw=420, rows=2,
                        leads=[[(296, 393)], [(684, 393)]]),
}

# Which anchor each leader ENDS on, so check_geometry() can tell "this leader is allowed to touch
# this ring" from "this leader is cutting through somebody else's".
LEAD_TARGET = {
    "stick_click": [("L", "stick"), ("R", "stick")],
    "recentre":    [("L", "Y"), ("R", "B")],
    "left_stick":  [("L", "stick")],
    "options":     [("L", "X")],
    "trigger":     [("L", "trigger")],
    "right_stick": [("R", "stick")],
    "ping":        [("R", "A")],
    "grip":        [("L", "grip"), ("R", "grip")],
}

# THE LAYOUT GUARD. Each entry is a claim the PICTURE makes about the artwork, checked against the
# coordinates the rings are drawn from. A regenerated artwork that framed the pair differently, or
# mirrored it, fails here instead of shipping a diagram whose labels point at the wrong button.
LAYOUT = [
    ("the two halves are exact mirrors about SPLIT_X",
     lambda: all(L[k][0] + R[m][0] == 2 * SPLIT_X and L[k][1] == R[m][1]
                 for k, m in (("stick", "stick"), ("Y", "B"), ("X", "A"),
                              ("trigger", "trigger"), ("grip", "grip")))),
    ("the thumbstick is at the OUTER top of the plate, outboard of the lettered buttons",
     lambda: L["stick"][0] < L["X"][0] < L["Y"][0] and L["stick"][1] < L["X"][1]),
    ("the lettered buttons run down the plate's INNER side on a diagonal: Y above X, X outboard",
     lambda: L["Y"][1] < L["X"][1] and L["X"][0] < L["Y"][0]),
    ("the trigger is on the neck, above the grip",
     lambda: L["trigger"][1] < L["grip"][1]),
    ("the grip nub is INBOARD of the trigger, on the inner side of the handle",
     lambda: L["grip"][0] > L["trigger"][0]),
    ("every anchor is inside its own half of the artwork",
     lambda: all(0 < p[0] < SPLIT_X and 0 < p[1] < ART_H for p in L.values())
             and all(SPLIT_X < p[0] < ART_W and 0 < p[1] < ART_H for p in R.values())),
]

LABELS = {
    "en": {
        "left": "LEFT", "right": "RIGHT",
        # The callouts answer "what does this one do" in one breath. The legend below keeps the
        # detail; nothing here is written twice, and where a name serves both hands it says so
        # either with two leaders or, for the trigger, in the words.
        "callouts": {
            "stick_click": "Click a stick in: pull yourself along",
            "recentre":    "Hold Y + B: recentre yourself",
            "left_stick":  "Left stick: move through the room",
            "options":     "X: opens the pause menu",
            "trigger":     "Trigger, either hand: take a card, point and click",
            "right_stick": "Right stick: turn · reel a window in",
            "ping":        "A: ping a hex for everyone",
            "grip":        "Grip — hold to touch and grab",
        },
        "cells": [
            (BLUE, "Thumbstick — push", [
                "Left stick: move through the room.",
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
            # WorldUI/Options/OptionsToggle.cs:11-15 with WorldUI/Grab/NonDominantHold.cs:82-84:
            # a short tap of the NON-DOMINANT lower face button opens the game's own PAUSE screen,
            # and the mod's options are one button inside it. It is X only under the shipped
            # right-handed default, which is what the note below covers.
            (GREEN, "X — short tap", [
                "Opens and closes the game's pause menu.",
                "The VR options sit inside it.",
            ]),
            (RED, "A", [
                "Ping a hex so everyone sees it.",
            ]),
            (PURPLE, "Y + B — hold one second", [
                "Recentre yourself where you stand.",
            ]),
        ],
        "note": "A Quest 3 is shown; every supported controller has the same keys in the same "
                "places. Handedness, turning and every binding above are changeable in VR "
                "Options. The in-game tutorial covers all of it.",
    },
    "de": {
        "left": "LINKS", "right": "RECHTS",
        # German is 15-30 % longer than the English beside it, and these sit in gaps between drawn
        # parts rather than in a column that can grow. Where a faithful translation does not fit,
        # the WORDING is shortened — never the type, which is already at the floor for a picture
        # shown at half its rendered width.
        "callouts": {
            "stick_click": "Stick eindrücken: dich heranziehen",
            "recentre":    "Y + B halten: neu zentrieren",
            "left_stick":  "Linker Stick: durch den Raum bewegen",
            "options":     "X: öffnet das Pause-Menü",
            "trigger":     "Trigger, beide Hände: Karte nehmen, zeigen und klicken",
            "right_stick": "Rechter Stick: drehen, Fenster heranholen",
            "ping":        "A: ein Feld für alle markieren",
            "grip":        "Grip — halten: berühren und greifen",
        },
        "cells": [
            (BLUE, "Thumbstick — drücken", [
                "Linker Stick: durch den Raum bewegen.",
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
                "Öffnet und schließt das Pause-Menü des Spiels.",
                "Die VR-Optionen liegen darin.",
            ]),
            (RED, "A", [
                "Ein Feld markieren, sodass alle es sehen.",
            ]),
            (PURPLE, "Y + B — eine Sekunde halten", [
                "Dich neu zentrieren, wo du gerade stehst.",
            ]),
        ],
        "note": "Abgebildet ist eine Quest 3; jeder unterstützte Controller hat dieselben Tasten "
                "an denselben Stellen. Händigkeit, Drehen und jede Belegung oben lassen sich "
                "in den VR-Optionen ändern. Das Tutorial im Spiel erklärt alles davon.",
    },
}


CALL_SIZE = 29     # the callout type. 29 px is 14.5 px at the 860 the guides show this at — the
CALL_LH   = 38     # floor for a coloured semibold name that has to be read at a glance.


def ring(d, xy, colour, r=19, w=RING_W):
    """A RING, never a filled disc: the reader has to be able to see the button underneath it."""
    x, y = xy
    d.ellipse([x - r - w, y - r - w, x + r + w, y + r + w], outline=PAPER, width=RING_HALO)
    d.ellipse([x - r, y - r, x + r, y + r], outline=colour, width=w)


def leader(d, colour, pts):
    """A thin leader line ending in a dot on the button it names.

    Drawn UNDER a paper casing so it stays readable where it crosses the black plate or the white
    body, and thin enough (3 px against the rings' 7) that it never competes with them.
    """
    d.line(pts, fill=PAPER, width=9, joint="curve")
    d.line(pts, fill=colour, width=3, joint="curve")
    ex, ey = pts[-1]
    d.ellipse([ex - 7, ey - 7, ex + 7, ey + 7], fill=PAPER)
    d.ellipse([ex - 5, ey - 5, ex + 5, ey + 5], fill=colour)


def wrap(d, text, f, width):
    """Break a string against the width it is ACTUALLY given, not the width it would like.

    German is 15-30 % longer than the English it is set beside, and a legend cell — or a callout —
    that silently overruns is how a translated diagram starts printing one label on top of the next.
    """
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


def fit(d, lang, key, f, maxw, rows):
    """The wrapped lines of one callout, or a loud refusal. Two failure modes, both German's.

    A word WIDER than its gap survives wrap() and paints over its neighbour. A string that is
    merely long does not overrun at all — it grows DOWNWARD, into whatever sits under it, which no
    width check can see. Both raise, and both name the string so the fix is obvious.
    """
    lines = wrap(d, LABELS[lang]["callouts"][key], f, maxw)
    for ln in lines:
        w = d.textlength(ln, font=f)
        if w > maxw + 0.5:
            raise SystemExit(
                "CALLOUT '%s' does not fit its gap in '%s': %r is %.0f px against a %d px budget."
                "\nShorten the WORDING — do not shrink the type, which is already at the floor "
                "for a picture shown at half its rendered width." % (key, lang, ln, w, maxw))
    if rows and len(lines) > rows:
        raise SystemExit(
            "CALLOUT '%s' takes %d lines in '%s' and its gap holds %d: %r.\nA width check cannot "
            "see this one — the text did not overrun, it grew downward into whatever is under it. "
            "Shorten the WORDING." % (key, len(lines), lang, rows, lines))
    return lines


def block(d, lang, key, f):
    """One callout's wrapped lines, its box in canvas pixels, and its leaders' full polylines.

    Both the guard and the drawing go through here, so what is checked is exactly what is drawn.
    """
    g = CALLOUT_GEOM[key]
    lines = fit(d, lang, key, f, g["maxw"], g["rows"])
    widths = [d.textlength(ln, font=f) for ln in lines]
    wmax = max(widths)
    x_px, y_px = AX(g["at"][0]), AY(g["at"][1])
    if g["align"] == "c":
        bx0 = x_px - wmax / 2.0
    elif g["align"] == "r":
        bx0 = x_px - wmax
    else:
        bx0 = x_px
    box = (bx0, y_px, bx0 + wmax, y_px + CALL_LH * len(lines) - 6)

    polys = []
    for lead in g["leads"]:
        pts = [(AX(px), AY(py)) for px, py in lead]
        lx, ly = pts[0]
        if ly < box[1] - 2:                                  # the button is above the words
            polys.append([(min(max(lx, box[0]), box[2]), box[1] - 8)] + pts)
        elif ly > box[3] + 2:                                # below them
            polys.append([(min(max(lx, box[0]), box[2]), box[3] + 8)] + pts)
        elif lx > box[2]:                                    # level, off to the right
            polys.append([(box[2] + 8, ly)] + pts)
        else:                                                # level, off to the left
            polys.append([(box[0] - 8, ly)] + pts)
    return lines, widths, box, polys


def art_xy(cx, cy):
    """A canvas point back in artwork pixels, or None if it lands in the lane between the hands.

    AX is piecewise — the gap is inserted at the seam — so the inverse is too. It exists for the one
    claim that cannot be made any other way: THE WORDS NEVER SIT ON A CONTROLLER. A leader may cross
    the plate, a name may not, and a German string that grew one line sideways into the black plate
    would be invisible rather than merely ugly.
    """
    if cx <= AX(SPLIT_X):
        x = (cx - LX) / SCALE
    elif cx >= AX(SPLIT_X + 1):
        x = (cx - LX - GAP) / SCALE
    else:
        return None
    y = (cy - TOP_BAND) / SCALE
    if 0 <= x < ART_W and 0 <= y < ART_H:
        return (int(x), int(y))
    return None


def hand_boxes(d, lang, f):
    """The two LEFT / RIGHT captions, in canvas pixels — centred under their own controller.

    They are letter-spaced by hand, so their width is only knowable through the font. Both the guard
    and the drawing go through here, so a callout can never be proved clear of a caption that is
    then drawn somewhere else.
    """
    out = {}
    for key, cx in (("left", AX(SPLIT_X / 2.0)), ("right", AX(SPLIT_X * 1.5))):
        w = d.textlength(" ".join(LABELS[lang][key]), font=f)
        out[key] = (cx - w / 2.0, HAND_Y, cx + w / 2.0, HAND_Y + HAND_SIZE + 6)
    return out


def anchor_px(hand, key):
    """One anchor, in canvas pixels."""
    p = (L if hand == "L" else R)[key]
    return (AX(p[0]), AY(p[1]))


def seg_dist(p, a, b):
    (px, py), (ax, ay), (bx, by) = p, a, b
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return ((px - ax) ** 2 + (py - ay) ** 2) ** 0.5
    t = min(max(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0.0), 1.0)
    return ((px - ax - t * dx) ** 2 + (py - ay - t * dy) ** 2) ** 0.5


def check_geometry(rule_y):
    """Prove the claims the PICTURE makes, in every language, before a single file is written.

    Not folded into the draw: the languages are built one after another, so a failure discovered
    while drawing German would already have left a new English file on disk beside a stale German
    one. A preflight fails with both files untouched.

    Four families of claim, and every one of them is measured rather than asserted:
      1. the artwork is the one these pixel anchors were measured against, and each ring still
         lands on the PART it names — PROBED OUT OF THE BITMAP: the plate features have to be dark
         under the ring and the trigger and grip light;
      2. the pair is laid out the way the callouts assume (mirrored halves, stick outboard, grip
         inboard of the trigger);
      3. every callout fits its gap in both languages, in width AND in rows, and the block it
         actually occupies stays on the page, clear of the legend rule, of the LEFT / RIGHT
         captions, of every ring, of every other callout, and OFF THE CONTROLLER SILHOUETTE —
         a leader may cross the plate, a name may not;
      4. no leader passes through a ring it does not name.

    3 and 4 are not decoration: between them they are what rejected both attempts to name the
    trigger from the lane, and that rejection is why the trigger's callout sits where it does.
    """
    art = Image.open(ART).convert("RGBA")
    if art.size != (ART_W, ART_H):
        raise SystemExit(
            "controllers-artwork.png is %dx%d and the anchors were measured against %dx%d.\n"
            "Every ring, callout and leader in this script is a pixel position in that bitmap. "
            "Re-probe L and R against the new artwork before building." % (art.size + (ART_W, ART_H)))
    px = art.load()

    # 1. each ring lands on the part it names. The plate is matte charcoal and the body is white,
    # so "this anchor is on the plate" and "this anchor is on the body" are measurable, and a
    # regenerated artwork that moved a button fails here rather than shipping a ring on bare paper.
    # The probe disc is smaller than the ring on purpose: the grip is a small NUB on the silhouette's
    # inner edge, and its ring is deliberately drawn overhanging the outline. What is being claimed
    # is that the ring's CENTRE sits on the part it names, not that a 19 px disc of controller
    # surrounds it — which is false for the grip and would fail on a correct artwork.
    PLATE = ("stick", "X", "Y", "A", "B")
    PROBE_R = 10
    for hand, table in (("L", L), ("R", R)):
        for key, (x, y) in table.items():
            r = PROBE_R
            disc = [(px[x + dx, y + dy]) for dx in range(-r, r + 1) for dy in range(-r, r + 1)
                    if dx * dx + dy * dy <= r * r]
            lum = [0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2] for p in disc]
            if min(p[3] for p in disc) < 200:
                raise SystemExit("RING %s-%s is not on the controller at all — the artwork is "
                                 "transparent under it. Re-probe L and R." % (hand, key))
            if key in PLATE and max(lum) > 140:
                raise SystemExit(
                    "RING %s-%s should sit on the black plate and the artwork under it reaches "
                    "luminance %.0f. Either the anchor slid off the plate or the artwork was "
                    "regenerated — re-probe L and R." % (hand, key, max(lum)))
            if key not in PLATE and min(lum) < 150:
                raise SystemExit(
                    "RING %s-%s should sit on the white body and the artwork under it drops to "
                    "luminance %.0f. Either the anchor slid onto the plate or off the silhouette "
                    "— re-probe L and R." % (hand, key, min(lum)))

    # 2. the pair is laid out the way every callout position assumes
    for claim, holds in LAYOUT:
        if not holds():
            raise SystemExit(
                "L / R do not describe the controller pair this picture is drawn for.\n"
                "  FAILED: %s\n"
                "If the artwork was regenerated, re-probe every anchor before touching anything "
                "else — the callouts and the leaders are anchored to them." % claim)

    # 3 and 4, per language
    probe = ImageDraw.Draw(Image.new("RGB", (8, 8)))
    f = font("Inter-SemiBold.otf", CALL_SIZE)
    for lang in LABELS:
        named = set(LABELS[lang]["callouts"])
        if named != set(CALLOUT_GEOM):
            raise SystemExit("'%s' names %s and the layout knows %s — every callout needs both."
                             % (lang, sorted(named), sorted(CALLOUT_GEOM)))
        boxes = {}
        hands = hand_boxes(probe, lang, font("Inter-SemiBold.otf", HAND_SIZE))
        for key in CALLOUT_GEOM:
            _, _, box, polys = block(probe, lang, key, f)
            boxes[key] = box

            step = 4
            cx = box[0]
            while cx <= box[2]:
                cy = box[1]
                while cy <= box[3]:
                    ap = art_xy(cx, cy)
                    if ap is not None and px[ap][3] > 40:
                        raise SystemExit(
                            "CALLOUT '%s' is drawn ON the controller in '%s': %s reaches artwork "
                            "pixel %s, which is not transparent. A leader may cross the plate; a "
                            "name may not. Move it in CALLOUT_GEOM or shorten the wording."
                            % (key, lang, tuple(round(v) for v in box), ap))
                    cy += step
                cx += step

            for h_key, hb in hands.items():
                if (box[0] - 12 < hb[2] and hb[0] - 12 < box[2]
                        and box[1] - 8 < hb[3] and hb[1] - 8 < box[3]):
                    raise SystemExit(
                        "CALLOUT '%s' runs into the %s caption in '%s': %s against %s. That "
                        "caption is what says which controller is which — move the callout."
                        % (key, h_key.upper(), lang, tuple(round(v) for v in box),
                           tuple(round(v) for v in hb)))

            if box[0] < MARGIN or box[2] > W - MARGIN or box[1] < 8 or box[3] > rule_y - 12:
                raise SystemExit(
                    "CALLOUT '%s' falls off the picture in '%s': it occupies %s on a %d px page "
                    "whose legend rule is at %d. Move it in CALLOUT_GEOM or shorten the wording."
                    % (key, lang, tuple(round(v) for v in box), W, rule_y))

            for hand, table in (("L", L), ("R", R)):
                for a_key in table:
                    ax, ay = anchor_px(hand, a_key)
                    dx = max(box[0] - ax, 0.0, ax - box[2])
                    dy = max(box[1] - ay, 0.0, ay - box[3])
                    if (dx * dx + dy * dy) ** 0.5 < ring_outer(a_key):
                        raise SystemExit(
                            "CALLOUT '%s' overlaps the %s-%s ring in '%s'. The words would be "
                            "drawn over the button they name." % (key, hand, a_key, lang))

            targets = set(LEAD_TARGET[key])
            for poly in polys:
                for i in range(len(poly) - 1):
                    for hand, table in (("L", L), ("R", R)):
                        for a_key in table:
                            if (hand, a_key) in targets:
                                continue
                            clear = ring_outer(a_key) + 5
                            if seg_dist(anchor_px(hand, a_key), poly[i], poly[i + 1]) < clear:
                                raise SystemExit(
                                    "The '%s' leader passes through the %s-%s ring in '%s' — a "
                                    "line that crosses a ring it does not name reads as pointing "
                                    "at it. Re-aim the polyline in CALLOUT_GEOM, or move the "
                                    "callout to the other side." % (key, hand, a_key, lang))

        for a in boxes:
            for b in boxes:
                if a >= b:
                    continue
                ba, bb = boxes[a], boxes[b]
                if (ba[0] - 12 < bb[2] and bb[0] - 12 < ba[2]
                        and ba[1] - 10 < bb[3] and bb[1] - 10 < ba[3]):
                    raise SystemExit(
                        "CALLOUTS '%s' and '%s' collide in '%s': %s against %s. German usually "
                        "does this by growing DOWNWARD a line further than English does — cut a "
                        "row from one of them, or move it in CALLOUT_GEOM."
                        % (a, b, lang, tuple(round(v) for v in ba), tuple(round(v) for v in bb)))


def build(lang):
    art = Image.open(ART).convert("RGBA")
    art = art.resize((round(art.width * SCALE), round(art.height * SCALE)), Image.LANCZOS)
    split = round(SPLIT_X * SCALE)
    left_sprite = art.crop((0, 0, split, art.height))
    right_sprite = art.crop((split, 0, art.width, art.height))

    im = Image.new("RGB", (W, 2000), PAPER)
    im.paste(left_sprite, (LX, TOP_BAND), left_sprite)
    im.paste(right_sprite, (LX + split + GAP, TOP_BAND), right_sprite)
    d = ImageDraw.Draw(im)

    txt = LABELS[lang]

    f_call = font("Inter-SemiBold.otf", CALL_SIZE)
    f_hand = font("Inter-SemiBold.otf", HAND_SIZE)
    f_title = font("Inter-SemiBold.otf", 29)
    f_body = font("Inter-Regular.otf", 25)
    f_note = font("Inter-Regular.otf", 24)

    # --- the rings on the artwork ---------------------------------------------------------
    for hand, table in (("L", L), ("R", R)):
        for key in table:
            colour = {"stick": BLUE, "trigger": ORANGE, "grip": CYAN,
                      "X": GREEN, "A": RED, "Y": PURPLE, "B": PURPLE}[key]
            ring(d, anchor_px(hand, key), colour, r=RING_R[key])

    # --- the callouts ---------------------------------------------------------------------
    # Drawn over the rings, so a leader crossing the plate stays readable and a name never
    # disappears under a marker. check_geometry() has already proved every string here fits, every
    # block stays on the page and no leader cuts through a ring it does not name.
    for key, g in CALLOUT_GEOM.items():
        lines, widths, box, polys = block(d, lang, key, f_call)
        for poly in polys:
            leader(d, g["colour"], poly)
        for i, ln in enumerate(lines):
            if g["align"] == "c":
                lx0 = (box[0] + box[2]) / 2.0 - widths[i] / 2.0
            elif g["align"] == "r":
                lx0 = box[2] - widths[i]
            else:
                lx0 = box[0]
            d.text((lx0, box[1] + i * CALL_LH), ln, font=f_call, fill=g["colour"],
                   stroke_width=5, stroke_fill=PAPER)

    # --- LEFT / RIGHT under each controller -----------------------------------------------
    for key, box in hand_boxes(d, lang, f_hand).items():
        d.text((box[0], box[1]), " ".join(txt[key]), font=f_hand, fill=INK_SOFT)

    # --- the legend -----------------------------------------------------------------------
    rule_y = RULE_Y
    d.line([(MARGIN, rule_y), (W - MARGIN, rule_y)], fill=RULE, width=2)

    col_x = [56, 616, 1176]
    col_w = 490

    def cell(x, y, colour, title, lines):
        yy = y
        for i, ln in enumerate(wrap(d, title, f_title, col_w - 40)):
            if i == 0:
                d.ellipse([x, yy + 6, x + 20, yy + 26], outline=colour, width=7)
            d.text((x + 40, yy), ln, font=f_title, fill=INK)
            yy += 38
        yy += 4
        for line in lines:
            for ln in wrap(d, line, f_body, col_w - 40):
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
            for ln in wrap(d, txt["note"], f_note, W - 56 - col_x[1]):
                d.text((col_x[1], ny), ln, font=f_note, fill=INK_SOFT)
                ny += 33
            bottom = max(bottom, ny)
        y = bottom + 34

    out = os.path.join(HERE, "controls-%s.png" % lang)
    im = im.crop((0, 0, W, y + 12))
    im.quantize(colors=200, method=Image.FASTOCTREE).save(out, optimize=True)
    print("wrote %s  (%d x %d, %d kB)" % (out, im.width, im.height, os.path.getsize(out) // 1024))


# The LEFT / RIGHT captions and the legend rule sit below the artwork. Both are module constants
# because the preflight needs them: "this callout stays clear of the captions" and "…of the legend"
# are two of the claims it proves before either file is written.
HAND_Y   = TOP_BAND + round(ART_H * SCALE) + 6
HAND_SIZE = 30
RULE_Y   = HAND_Y + 62


if __name__ == "__main__":
    check_geometry(RULE_Y)
    for lang in ("en", "de"):
        build(lang)
