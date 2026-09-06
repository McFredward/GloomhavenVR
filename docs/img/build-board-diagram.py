#!/usr/bin/env python3
"""Build docs/img/board-en.png and board-de.png — the CONTROL BOARD map in the playing guide.

WHY THIS IS A SCRIPT AND NOT A HAND-DRAWN IMAGE
-----------------------------------------------
Exactly the reasons build-controls-diagram.py gives for the controller map, and they apply here
word for word: the picture has to exist in two languages and it has to stay true when a part moves.

  board-artwork.png   the ARTWORK ONLY — the shipped Oak control board with its grab rod,
                      photographed dead straight-on, no words anywhere in it.
  this script         every WORD and every marker, drawn as real text at build time.

THE CALLOUTS ON THE PICTURE ARE NOT A BREACH OF THAT RULE — read the rule precisely. What is
forbidden is a word BAKED INTO THE GENERATED BITMAP, because a model cannot spell, because a
labelled bitmap would have to be regenerated per language, and because a label that moves could not
be corrected without redrawing the picture. CALLOUTS below is none of those things: it is real Inter
text this script draws at build time, per language, anchored in board-local metres, and every one of
them is a one-line edit. The user asked for exactly this — "Ich will das ein kurzer Text direkt auch
schon auf dem Bild zu sehen ist, damit man in einem Blick versteht welches Element was ist statt in
der Legende erst ein mapping machen zu müssen" — because the colour was the ONLY bridge from a
marker to a legend cell, and a colour lookup is not a glance. The colour coding stays and every
callout wears its marker's colour, so the picture and the legend now reinforce each other instead of
being two halves of a lookup. Do not "fix" the callouts back out.

WHERE THE ARTWORK COMES FROM, and why it is not a screenshot. docs/img/control-board-poster.jpg is
the obvious candidate and it is unusable: a dark in-game capture with a play glyph painted over the
middle and the player's own forearm across the board. The artwork here is a RENDER OF THE SHIPPED
PREFAB, through the shipped BoardLit material, by

    BOARD_ASSET_OUT=<dir> xvfb-run -a Unity -batchmode \
        -projectPath unity/GloomhavenVR.Assets -buildTarget Win64 \
        -executeMethod GloomhavenVR.BoardAssetShot.RenderDiagramArtwork -logFile x.log -quit

(unity/GloomhavenVR.Assets/Assets/Editor/PreviewBoardAsset.cs). It writes board_artwork_zneg.png at
2000x1150; the committed artwork is that file resized to 1200 px and quantised to 200 colours.
It is Unity rather than unity/asset-preview/render_asset.py because the GRAB ROD is a procedural
mesh built in C# at runtime — there is no FBX for Blender to be handed.

THE RING COORDINATES ARE A MEASUREMENT, NOT A READING. The controls script had to read its twelve
feature centres off its render and check them with probe dots, and its own comment says
"Regenerating the artwork moves all twelve — re-probe, do not guess." Here the camera that took the
picture printed every anchor's pixel position (the ANCHOR lines in that run's log), and ORIENTATION
below re-derives each one from the board-local mapping and refuses to draw if they disagree. A
re-render that is mirrored, rotated or framed differently fails loudly instead of shipping a
diagram whose labels point at the wrong side of the board.

WHAT IS DELIBERATELY NOT ON THE PICTURE. A crowded diagram is the failure this design was fighting,
so five parts of the board were left off when it was built: the decision drawer, the active-card
matrix, the item-use berth, the pin toggle and the pick-progress placard. The user asked for the
ACTIVE-CARD MATRIX by name on 2026-09-06 ("Beim Board in der README fehlt im Bild das Areal wo die
aktiven Karten angegeben sind"), so that one is now drawn — see ACTIVE_* below. The other four are
still off, still on purpose, and adding one is a decision, not a chore.

THE EXPLANATIONS BELOW COME FROM THE SOURCE, NOT FROM THE DOCS. See the citations on each entry; if
you change one, change the citation with it. Two of them contradict older prose in the repo and are
the source's word, not the doc's:
  * the class doc at Cards/Tray/PlayTray.1.Core.cs:59-60 still lists a SETTINGS GEAR in the right
    column. There is no gear — CreateDashboardButtons builds only the follow/pin toggle, there is
    no _gear field anywhere under Cards/Tray/, and Caps/CapCellMath.cs's CapRole has no entry for
    one. Nothing here labels a gear.
  * the native "short rest" widget is documented in places as docking on the board. It does not:
    WorldUI's TrayControlDockSurface hardcodes ShortRestDocked => false, so the mod's own left-hand
    pad is the only short-rest control (Cards/Caps/RestControls.cs:11-16).
  * PlayTray.1.Core.cs:1009-1014 works out where the active-card matrix lands and says it leaves
    "a clear gap past the pile column". Its three inputs are all stale: it puts the mount at
    0.34 + 0.17 = 0.51 (ActiveMountBase is BoardW/2 + 0.012 + 0.17 = 0.502), it uses
    ActivePileViewer.CardScale ~ 0.82 (Defaults.Cards.cs:534 ships ActiveCardScale_Oak = 1f) and it
    lays the cards "at mount-local x = 0" (ActivePileViewer.Columns has been 3 since 85bbb8ca). The
    real numbers are worked out at ACTIVE_TRUE_* below: a full three-wide row starts 1.2 mm past
    the pile slabs' right edge, not the ~76 mm the comment's arithmetic implies. The gap is real
    but it is a millimetre, and this diagram had to be laid out around that.

Usage:  python3 docs/img/build-board-diagram.py
"""

import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, "board-artwork.png")

FONT_DIR = "/usr/share/fonts/opentype/inter"
def font(name, size):
    return ImageFont.truetype(os.path.join(FONT_DIR, name), size)

INK       = (26, 22, 19)
INK_SOFT  = (92, 86, 80)
RULE      = (219, 213, 205)
PAPER     = (255, 255, 255)

# One colour per PART, used for the marker on the artwork and the swatch in the legend. The marker
# is what ties a legend line to a physical place on the board, so the two must never drift apart.
BLUE   = (37, 99, 235)     # the two card recesses
GREEN  = (5, 130, 90)      # the three key seats on the right
ORANGE = (217, 94, 20)     # the two rest pads on the left — ONE colour for both, because they are
                           # one zone of the board with two keys in it, the way the controls
                           # diagram gives one physical thumbstick one colour for two gestures
CYAN   = (10, 133, 160)    # the grab rod
PURPLE = (124, 58, 237)    # the top edge: initiative track + round readout
GOLD   = (161, 118, 10)    # left of the board: objectives panel + element chips
RED    = (200, 45, 45)     # right of the board: the discard / burnt / items stacks AND the
                           # active-card matrix further out — ONE colour for one EDGE, the way
                           # GOLD covers the objectives panel and the element chips on the left
                           # and PURPLE covers the initiative track and the round readout on
                           # top. Grouping here is by ZONE, not by function, and a ninth hue
                           # beside these eight would be told apart by nobody.
PINK   = (190, 24, 120)    # your hand of cards

# ---------------------------------------------------------------------------------------------
# THE ARTWORK'S OWN FRAME, in board-local metres.
#
# Printed by PreviewBoardAsset.RenderDiagramArtwork for the committed render:
#     board_artwork_zneg.png: 2000x1150 px, ortho 0.64960 x 0.37343 m, centre (0, -0.02395, 0.005)
# so 2000 / 0.64960 = 3078.8 px per board-local metre, and board-local (0, 0) — the centre of the
# 0.640 x 0.320 m plate — lands at 1150/2 - 0.02395 * 3078.8 = 501.3 px down the native render.
ART_NATIVE_W = 2000
ORIGIN_PX    = (1000.0, 501.3)
PPM_NATIVE   = 3078.8

# Board metrics, from Cards/Tray/PlayTray.6.Build.cs:17-18. +X is the player's RIGHT, +Y is up
# (PlayTray.6.Build.cs:22-25).
BOARD_W, BOARD_H = 0.640, 0.320

# ---------------------------------------------------------------------------------------------
# THE SEVEN RECESSES, in board-local metres — the anchor empties the shipped Oak FBX carries,
# read straight out of it.
#
# They agree with the procedural fallback constants in PlayTray.6.Build.cs to within a centimetre
# (rest column -0.227 against RestZoneX -0.245, key column +0.227 against ButtonZoneX +0.235, key
# pitch 0.0765 = Defaults.StackPitchFallback exactly, slots +/-0.084 against +/-SlotSpacing/2 =
# 0.0775), and that agreement is the cross-check saying the two are describing the same board.
#
# READ THEM FROM THE FBX, NOT FROM THE UNITY PREFAB. The prefab's anchor transforms come out of
# AssetDatabase carrying a ~1.30x scale the board MESH does not have: projecting anchor.position
# through the render camera puts the key column at board-local 0.296 — 93 % of the way to the
# board's own edge, and a full centimetre outboard of three recesses that are plainly visible in
# the picture. The first cut of this diagram drew its markers there and they missed everything.
ANCHOR = {
    "Slot1":          (-0.0840,  0.0000),   # LEFT recess  — slot 0, the initiative slot
    "Slot2":          ( 0.0840,  0.0000),   # RIGHT recess — slot 1
    "ShortRestToken": (-0.2270,  0.0574),   # UPPER left pad
    "LongRestToken":  (-0.2270, -0.0574),   # LOWER left pad
    "ButtonSeat1":    ( 0.2270,  0.0765),   # seat 0 — Confirm (the item Use cap shares this seat)
    "ButtonSeat2":    ( 0.2270,  0.0000),   # seat 1 — Undo
    "ButtonSeat3":    ( 0.2270, -0.0765),   # seat 2 — Skip
    "GrabRod":        ( 0.0000, -0.1900),   # -BoardH/2 - 0.030, PlayTray.1.Core.cs BuildHandle
}

# Marker sizes, in board-local metres. Each is the RECESS AS THE ARTWORK DRAWS IT, measured off the
# render against a board-local grid — not the recess FLOOR the mod fits a keycap into, which is
# smaller because it sits inside the moulding (Oak: keys 74.6 x 64.3 mm, rest pads 81.6 mm across;
# PlayTray.6.Build.cs prints both every build). A marker drawn to the floor sits inside the bevel
# and reads as a miss.
SLOT_HALF = (0.0765, 0.1125)
SEAT_HALF = (0.0390, 0.0345)
REST_R    = 0.0472

# THE ORIENTATION GUARD. Each entry is a claim the SOURCE makes about the board, checked against
# the anchors above — and the artwork is pasted against those same coordinates, so the two cannot
# drift apart silently. A mirrored render fails the x-sign claims; an upside-down one fails the
# y-order claims. The reason this exists is that the first render of this board WAS mirrored: the
# decorated face is reached from -Z, the FBX importer flips handedness, and the wrong one of the
# two looks entirely plausible right up until you notice CONFIRM sitting on the left.
ORIENTATION = [
    ("Slot1 is LEFT of centre: slot 0 is the initiative slot, at -0.5 * SlotSpacing "
     "(PlayTray.6.Build.cs:152, CardsDriver.6.Flows.cs ReconcileInitiative)",
     lambda a: a["Slot1"][0] < -0.02),
    ("Slot2 is RIGHT of centre (PlayTray.6.Build.cs:152)",
     lambda a: a["Slot2"][0] > 0.02),
    ("the rest pads are on the player's LEFT: RestZoneX = -0.245 (PlayTray.6.Build.cs:27)",
     lambda a: a["ShortRestToken"][0] < -0.15 and a["LongRestToken"][0] < -0.15),
    ("the key seats are on the player's RIGHT: ButtonZoneX = +0.235 (PlayTray.6.Build.cs:28)",
     lambda a: min(a["ButtonSeat%d" % i][0] for i in (1, 2, 3)) > 0.15),
    ("SHORT rest is the UPPER pad and long rest the lower (PlayTray.6.Build.cs:172-201, "
     "Caps/RestControls.cs:135-170)",
     lambda a: a["ShortRestToken"][1] > a["LongRestToken"][1]),
    ("seat 0 (Confirm) is the TOP recess and seat 2 (Skip) the bottom (Cards/BoardAnchors.cs's "
     "seat table, PlayTray.6.Build.cs:301-318)",
     lambda a: a["ButtonSeat1"][1] > a["ButtonSeat2"][1] > a["ButtonSeat3"][1]),
    ("the grab rod hangs BELOW the bottom edge, at -BoardH/2 - 0.030 (PlayTray.1.Core.cs "
     "BuildHandle)",
     lambda a: a["GrabRod"][1] < -BOARD_H * 0.5),
    ("every recess is ON the 0.640 x 0.320 m plate (PlayTray.6.Build.cs:17-18)",
     lambda a: all(abs(p[0]) < BOARD_W * 0.5 and abs(p[1]) < BOARD_H * 0.5
                   for k, p in a.items() if k != "GrabRod")),
]

# ---------------------------------------------------------------------------------------------
# THE WORLD WINDOW the picture covers, in board-local metres. It is wider and taller than the
# board because half of what a player has to be told about the control board is DOCKED AROUND it
# rather than carved into it: the game's own initiative track along the top edge, the objectives
# panel and the element chips off the left edge, the three card stacks off the right edge. Those
# are converted GAME panels, so no render of the mod's own asset can contain them — the script
# draws them, at the mounts the code gives them, and the legend says what each one is. They are
# drawn at a deliberately LIGHTER weight than the markers on the board itself, so the board stays
# the subject of the picture and the docks read as the places things arrive.
#
# THE WINDOW IS TALLER THAN THE CONTENT ON PURPOSE. The x range is set by the content — the
# objectives column at -0.592 and the active-card matrix out at +0.559 leave no horizontal gutter
# at all, and widening it further would shrink the board, which is the subject. So the CALLOUT
# ROOM is bought in y, where it is free: a band above the initiative dock and a band below the
# grab rod. Everything a reader has to be told sits in one of those two bands, or on a plate.
#
# X1 WAS 0.440 UNTIL THE ACTIVE-CARD MATRIX WAS DRAWN. That zone mounts at board-local 0.502 —
# 57 % of the board's own half-width past its right edge — so it does not fit the old window at
# any scale, and the window had to grow by 0.148 m. The cost is real and was paid deliberately:
# the board is 54 % of the picture's width where it used to be 62 %. What is NOT paid in TYPE is
# the canvas WIDTH, which stays 1720: the guides show this file at width=860, so every callout
# still lands at the same 14.5 device px it did before. Growing W instead would have shrunk the
# type at the reader, which is the one thing this diagram may not do.
WIN_X0, WIN_X1 = -0.600, 0.588
WIN_Y0, WIN_Y1 = -0.445, 0.410

# The docked panels, at their own mount bases. Each is (x0, y0, x1, y1) in board-local metres.
#   objectives  ObjectivesMountBase (-0.332, 0, -0.004), right-centre origin growing LEFT, width
#               ObjectivesMountWidth 0.26; the max height is 0.32 but the panel is grown to its own
#               content, so it is drawn at a typical 0.22   (PlayTray.3.Pose.cs:512,
#               PlayTray.1.Core.cs:903-906 and :455-458)
#   elements    ElementMountBase (-0.332, -0.232, -0.004), the same column, max height 0.12; the y
#               is -(BoardH/2 + 0.012 + 0.12/2)  (PlayTray.3.Pose.cs:515-518)
#   initiative  InitiativeMount along the TOP edge, bottom-centre origin, width budget
#               InitiativeMountWidth = BoardW, max height 0.14  (PlayTray.1.Core.cs:890-897)
#   piles       PileMountBase (+0.332, 0, -0.004), left-centre origin growing right; the three
#               stacks sit at mount-local x 0.05 and at y +s/2, -s/2, -1.5s for the Oak spacing
#               s = 0.116  (PlayTray.3.Pose.cs:503, Piles/PileViewer.cs:212-243)
DOCK_OBJECTIVES = (-0.592, -0.075, -0.332, 0.145)
DOCK_ELEMENTS   = (-0.542, -0.292, -0.332, -0.172)
DOCK_INITIATIVE = (-0.320, 0.168, 0.320, 0.268)
DOCK_PILES      = (0.332, -0.232, 0.432, 0.116)
PILE_Y          = (0.058, -0.058, -0.174)   # discard, burnt, items — PileViewer's own order
# The fan is not on the board at all — it opens over the palm of the hand that is holding it, in
# front of and below the board. Drawn where a player meets it, which is a hand-placed illustrative
# position and not a mount the code gives: it sits low enough to leave the strip under the grab rod
# free for the rod's own callout. (Cards/CardFan.cs, PalmGate.cs.)
FAN_CENTRE      = (0.050, -0.330)

# ---------------------------------------------------------------------------------------------
# THE ACTIVE-CARD MATRIX, one zone further out on the right edge again — the small grid holding the
# cards you have active RIGHT NOW (round-long and persistent). Cards/Piles/ActivePileViewer.cs; the
# peer mirror of the same block is Net/Remote/RemoteActiveCards.cs.
#
#   mount   ActiveMountBase = (BoardW/2 + 0.012 + ActiveMountOffsetX, 0, -0.004) = (0.502, 0, ...)
#           — PlayTray.3.Pose.cs:506 and PlayTray.1.Core.cs:441. Oak's per-board ActiveOffset is
#           (0, 0, 0), so on the shipped board the mount IS the base (Defaults.Cards.cs:531).
#   grid    up to ActivePileViewer.Columns = 3 cards per row, symmetric about the mount x and
#           centred in y (ActivePileViewer.Relayout). The steps are CardWidth x ActiveCardScale x
#           ActiveGridSpacing.x across and CardHeight x ActiveCardScale x ActiveGridSpacing.y down.
#   Oak     CardWidth 0.0635 (Defaults.Cards.cs:24), CardHeight = width x 88/63.5 = 0.0880
#           (CardsConfig.cs:1944), ActiveCardScale_Oak 1.0 (Defaults.Cards.cs:534),
#           ActiveGridSpacing_Oak (1.06, 0.70) (Defaults.Cards.cs:327).
ACTIVE_MOUNT = (0.5020, 0.0000)
ACTIVE_COLS, ACTIVE_ROWS = 3, 2                 # 3 is the code's own column count; 2 rows is what
                                                # the picture shows, and the column is uncapped
ACTIVE_TRUE_CARD = (0.0635, 0.0880)
ACTIVE_TRUE_STEP = (0.0635 * 1.06, 0.0880 * 0.70)   # = (0.067310, 0.061600)

# THE MATRIX IS DRAWN AT HALF THE REAL CARD, and that is the one place this picture SCALES a part
# instead of measuring it. Say so out loud rather than let a reader assume otherwise.
#
# A REAL three-wide row spans board-local x 0.40294 .. 0.60106 (mount 0.502, one step 0.06731 each
# way, half a card 0.03175 beyond that). The pile slabs' real right edge is 0.382 + CardWidth x
# PileStack.SlabFactor(0.62) / 2 = 0.40170 — so on the real board the two zones clear each other by
# 1.2 MM. This diagram, meanwhile, draws the pile stacks as a PLATE from 0.332 to 0.432, roughly
# 1.7x their real slab, because a 40 mm ghost would vanish next to the board. A to-scale matrix
# would therefore be drawn straddling that plate and would read as a mistake in the drawing.
#
# So the card SIZE is halved and NOTHING ELSE IS: the mount centre, the column count, the 1.06
# column gap and the 0.70 row overlap are all the shipped numbers, and DOCK_CLAIMS below asserts
# each of them. That is exactly the licence the pile slabs and the objectives plate already take.
ACTIVE_SCALE = 0.50
ACTIVE_CARD  = (ACTIVE_TRUE_CARD[0] * ACTIVE_SCALE, ACTIVE_TRUE_CARD[1] * ACTIVE_SCALE)
ACTIVE_STEP  = (ACTIVE_CARD[0] * 1.06, ACTIVE_CARD[1] * 0.70)
ACTIVE_PAD   = 0.0070                            # the plate's own breathing room around the grid


def _active_dock():
    """DOCK_ACTIVE, derived from the grid rather than typed — a plate that no longer wraps its
    own content is the failure mode a literal here would hide."""
    hx = (ACTIVE_COLS - 1) * 0.5 * ACTIVE_STEP[0] + ACTIVE_CARD[0] * 0.5 + ACTIVE_PAD
    hy = (ACTIVE_ROWS - 1) * 0.5 * ACTIVE_STEP[1] + ACTIVE_CARD[1] * 0.5 + ACTIVE_PAD
    return (ACTIVE_MOUNT[0] - hx, ACTIVE_MOUNT[1] - hy,
            ACTIVE_MOUNT[0] + hx, ACTIVE_MOUNT[1] + hy)


DOCK_ACTIVE = _active_dock()

# THE DOCK GUARD. ORIENTATION above asserts what the SOURCE says about the seven recesses; this is
# the same idea for the zone that was added last and has no anchor empty of its own to be checked
# against. Every entry is a claim some file makes, checked against the constants right above it, so
# a wrong retype fails the build instead of shipping a matrix drawn in the wrong place, at the wrong
# column count, or on top of the pile plate.
DOCK_CLAIMS = [
    ("the active matrix docks to the RIGHT of the pile stacks — ActiveMountBase.x = 0.502 against "
     "PileMountBase.x = 0.332 (PlayTray.3.Pose.cs:503 and :506, PlayTray.1.Core.cs:441)",
     lambda: ACTIVE_MOUNT[0] > DOCK_PILES[2]),
    ("the mount is BoardW/2 + 0.012 + ActiveMountOffsetX(0.17), and Oak's ActiveOffset is zero "
     "(PlayTray.3.Pose.cs:506, Defaults.Cards.cs:531)",
     lambda: abs(ACTIVE_MOUNT[0] - (BOARD_W * 0.5 + 0.012 + 0.170)) < 1e-9),
    ("three cards per row — ActivePileViewer.Columns = 3, and Net/Remote/RemoteActiveCards mirrors "
     "it (it carried its own 2 until 2026-09-05 and every teammate read a different block)",
     lambda: ACTIVE_COLS == 3),
    ("the grid is symmetric about the mount x and centred in y (ActivePileViewer.Relayout)",
     lambda: abs((DOCK_ACTIVE[0] + DOCK_ACTIVE[2]) * 0.5 - ACTIVE_MOUNT[0]) < 1e-9
             and abs((DOCK_ACTIVE[1] + DOCK_ACTIVE[3]) * 0.5 - ACTIVE_MOUNT[1]) < 1e-9),
    ("the ROWS OVERLAP: the row step is 0.70 of a card height, so a lower row covers the tops of "
     "the row above (ActivePileViewer's grid comment, ActiveGridSpacing_Oak.y = 0.70)",
     lambda: ACTIVE_STEP[1] < ACTIVE_CARD[1] and ACTIVE_TRUE_STEP[1] < ACTIVE_TRUE_CARD[1]),
    ("the COLUMNS DO NOT: the column step is 1.06 of a card width, so neighbours never overlap "
     "horizontally (ActiveGridSpacing_Oak.x = 1.06)",
     lambda: ACTIVE_STEP[0] > ACTIVE_CARD[0] and ACTIVE_TRUE_STEP[0] > ACTIVE_TRUE_CARD[0]),
    ("the zone is entirely OFF the 0.640 x 0.320 m plate — nothing about it is carved into the "
     "board (PlayTray.1.Core.cs:1006-1014)",
     lambda: DOCK_ACTIVE[0] > BOARD_W * 0.5),
    ("the plate this diagram draws for it clears the plate this diagram draws for the pile stacks "
     "by at least 8 mm, so the two zones read as two",
     lambda: DOCK_ACTIVE[0] - DOCK_PILES[2] >= 0.008),
    ("the whole zone, and the room its callout needs above it, are inside the picture's window",
     lambda: DOCK_ACTIVE[2] < WIN_X1 and DOCK_ACTIVE[3] < WIN_Y1),
]

# ---------------------------------------------------------------------------------------------
# THE CANVAS, and the mapping from board-local metres onto it. MODULE level, not build()-local,
# because check_layout() has to measure the very pixels build() will draw: a preflight working from
# its own copy of the frame is a preflight that can pass while the picture is wrong.
W       = 1720
MARGIN  = 56
TOP     = 40
BLOCK_W = W - 2 * MARGIN
PPM     = BLOCK_W / (WIN_X1 - WIN_X0)           # canvas pixels per board-local metre
BLOCK_H = round((WIN_Y1 - WIN_Y0) * PPM)


def X(x):
    return MARGIN + (x - WIN_X0) * PPM


def Y(y):
    return TOP + (WIN_Y1 - y) * PPM


def box(b):
    """A board-local (x0, y0, x1, y1) as a canvas (left, top, right, bottom)."""
    return (X(b[0]), Y(b[3]), X(b[2]), Y(b[1]))


# ---------------------------------------------------------------------------------------------
# THE CALLOUTS — a short name beside every marker, so the picture answers "what is that thing"
# without a trip to the legend. Everything here is in BOARD-LOCAL METRES, from the same anchors the
# markers are drawn from; nothing is a pixel read off the render, so a re-render at another
# resolution moves the callouts with their markers instead of stranding them.
#
#   at       where the text block starts, in metres: (x, y-of-its-TOP-line). x is the block's left
#            edge for align "l", its right edge for "r", its centre for "c".
#   maxw     the width the text is wrapped against, in canvas pixels. It is a HARD limit: a wrapped
#            line that still overruns it raises, because that is exactly how the German version of a
#            diagram starts printing one label over the next one.
#   rows     how many lines the gap this label sits in can absorb, and the OTHER half of that guard.
#            A too-long German string usually does not overrun `maxw` — wrap() takes it — it grows
#            DOWNWARD instead, into whatever is under it, which no width check can see. Two of these
#            are 1 because a second line lands somewhere: the rod's name would sit on the card fan,
#            and your hand's name would fall off the bottom of the picture block. The rest have real
#            slack and say so.
#   lead     the leader polyline, in metres, ending INSIDE the marker it names. The start point is
#            derived from the drawn text block, so it always leaves the right edge of the words.
#            None means the label sits against its marker and needs no line.
#
# WHY THE LANES ARE WHERE THEY ARE. Three markers live inside the board's silhouette, so their names
# have to live outside the wood. The two that are named from ABOVE (the recesses, the round readout)
# drop their leader through a GAP BETWEEN TWO TILES of the initiative dock rather than across one —
# board-local 0.000 is the dock's own centre seam and 0.20557 is the seam between its fifth and
# sixth tiles, and check_lanes() below asserts both, because that alignment is arithmetic on the
# dock's tile pitch and would break silently if the dock changed. The three keys are named from
# BELOW instead: the round readout sits directly over the top key and occupies the whole key column
# in x, so there is no lane from above that reaches a key without crossing it.
CALLOUT_GEOM = {
    # the two card recesses: named from the top band, down the dock's centre seam, landing between
    # the two recesses at their top corners so it points at both at once
    "recesses":   dict(colour=BLUE,   at=(0.00000, 0.33239), align="c", maxw=260, rows=2,
                       lead=[(0.00000, 0.11300)]),
    # the three keys: from the bottom band, straight up the key column into the bottom key
    "keys":       dict(colour=GREEN,  at=(0.18000, -0.24520), align="l", maxw=400, rows=3,
                       lead=[(0.22700, -0.07800)]),
    # the two rest pads: from the bottom band, up into the lower pad
    "rest":       dict(colour=ORANGE, at=(-0.32000, -0.17211), align="l", maxw=205, rows=3,
                       lead=[(-0.22700, -0.09800)]),
    # the grab rod: directly under the middle of the rod, a short tick up into it
    "rod":        dict(colour=CYAN,   at=(0.01000, -0.22062), align="c", maxw=300, rows=1,
                       lead=[(0.01000, -0.19800)]),
    # the initiative dock: beside its own left edge, a tick across the gap
    "initiative": dict(colour=PURPLE, at=(-0.34200, 0.23085), align="r", maxw=370, rows=2,
                       lead=[(-0.31200, 0.21800)]),
    # the engraved round readout: from the top band, down the dock's fifth seam
    "round":      dict(colour=PURPLE, at=(0.20557, 0.33239), align="c", maxw=240, rows=2,
                       lead=[(0.20557, 0.12600)]),
    # the three stacks: from the top band, straight down the right margin onto the pile plate
    "piles":      dict(colour=RED,    at=(0.44000, 0.39965), align="r", maxw=500, rows=2,
                       lead=[(0.38200, 0.11200)]),
    # your hand: directly under the fan, touching it
    "hand":       dict(colour=PINK,   at=(0.05000, -0.39846), align="c", maxw=320, rows=1, lead=None),
    # the active-card matrix: centred over its own grid, WHERE THE COLUMN'S REAL CAPTION SITS —
    # ActivePileViewer builds a TextMeshPro "ACTIVE" at column-local (0, +0.075) — with a tick down
    # onto the VISIBLE band of the top-middle card (the lower row is drawn OVER the upper one, so
    # that card's own centre is buried). It sits ABOVE y 0.116, the top of the pile plate, so the
    # label and that plate cannot touch however wide a translation of it gets; rows=1 because a
    # second line would grow down into the matrix it is naming.
    "active":     dict(colour=RED,    at=(0.50200, 0.15600), align="c", maxw=200, rows=1,
                       lead=[(0.50200, 0.02500)]),
}

# The objectives panel and the element strip carry their names INSIDE their own ghost plates, where
# a real panel carries its title. They are the only two that need neither room nor a line.
#
# THEIR BUDGET IS THE PLATE, NOT A NUMBER. It was a flat 366 px, which was 91 % of the objectives
# plate and 129 % OF THE ELEMENT PLATE at the window this diagram used to have — a German title that
# used its whole budget would have been checked against a gap it does not have, and the width guard
# would have passed it. Deriving it from the plate is also what keeps it honest through a window
# change: widening WIN_X1 for the active matrix moved every plate's pixel width.
PLATE_CALLOUT_INSET = 18


def plate_callout_maxw(dock):
    return (dock[2] - dock[0]) * PPM - 2 * PLATE_CALLOUT_INSET

LABELS = {
    "en": {
        # Two or three words each. The legend below keeps the detail; these answer "what is that".
        "callouts": {
            "recesses":   "Card recesses",
            "keys":       "Confirm · Undo · Skip",
            "rest":       "Short + long rest keys",
            "rod":        "Grab rod",
            "initiative": "Initiative track",
            "round":      "Round",
            "piles":      "Discard · burnt · items",
            "hand":       "Your hand of cards",
            "active":     "Active cards",
            "objectives": "Objectives",
            "elements":   "Elements",
        },
        "cells": [
            # Cards/Tray/PlayTray.4.Slots.cs (capture + PlaceCard), CardsDriver.6.Flows.cs
            # (ReconcileInitiative), PlayTray.4.Slots.cs:325-410 (the two glows).
            (BLUE, "The two card recesses", [
                "Drop your two chosen cards in. The left one is your initiative.",
                "Grab a card to take it back; drop it on the other one to swap the two.",
                "A slow green pulse marks the recess the game is waiting for.",
            ]),
            # Cards/BoardAnchors.cs (the seat table), PlayTray.6.Build.cs:430-523 (the caps),
            # PlayTray.7.Nested.cs:1975-2015 (the press gate).
            (GREEN, "The three keys — right", [
                "Top: CONFIRM, which locks your round in and unlocks it again.",
                "Middle: UNDO. Bottom: SKIP, raised only when a step can be skipped.",
                "Push a key in with your fingertip while holding grip, or point and click.",
            ]),
            # Cards/Caps/RestControls.cs:6-30 and :83-200.
            (ORANGE, "The two rest keys — left", [
                "Upper: short rest — your discards come back, at the cost of one card.",
                "Lower: long rest — declares the long rest for this round.",
                "Both grey out while the game is not offering that rest.",
            ]),
            # Cards/Tray/PlayTray.1.Core.cs:1158-1273 (BuildHandle), :1043-1075 (carry).
            (CYAN, "The grab rod", [
                "Grip it with one hand and the whole board comes with you —",
                "cards, keys, stacks and panels together.",
                "Grip it with both hands to make the board bigger or smaller.",
            ]),
            # PlayTray.1.Core.cs:890-897 (InitiativeMount), PlayTray.5.Status.cs:72-108 and
            # PlayTray.3.Pose.cs:571 (the round readout).
            (PURPLE, "Top edge — the round", [
                "The game's own initiative track docks here, portraits and all,",
                "including the '?' for anyone who has not locked in yet.",
                "The round number is engraved into the top-right corner.",
            ]),
            # PlayTray.1.Core.cs:903-921, PlayTray.3.Pose.cs:512-518,
            # Net/Remote/RemoteElementStrip.cs:362-372 (the six elements and their states).
            (GOLD, "Left — objectives and elements", [
                "The scenario's objectives panel, with the element chips below it.",
                "Fire, ice, air, earth, light and dark, in that order.",
                "An element you cannot see there is not infused.",
            ]),
            # Cards/Piles/PileViewer.cs:212-243 and :1005-1025, PileBrowser.cs:9-27,
            # Piles/ItemsPile.cs:14-55.
            # Cards/Piles/ActivePileViewer.cs (the matrix), PlayTray.3.Pose.cs:506 (its mount).
            (RED, "Right — the stacks and your active cards", [
                "Three stacks, top to bottom, each carrying its own live count.",
                "Tap one and it fans out above the board so you can read it.",
                "Items can be taken out of the fan; the other two are for reading.",
                "Further out: the cards you have active right now, three to a row.",
                "Pull one out to read it and it goes back on its own.",
            ]),
            # Cards/CardFan.cs, Hands/Interact/PalmGate.cs,
            # CardsDriver.2.Update.cs:1219-1300 (the gate), .3.Laser.cs:150-177 (the pluck).
            (PINK, "Your hand of cards", [
                "Turn your free hand palm-up toward your face and the fan opens.",
                "Take a card with your other hand, or point at it and pull the trigger.",
                "Hold a card back over the fan and a gap opens to put it away.",
            ]),
        ],
        "note": "The Oak board is shown; Steel and Bronze carry the same seven recesses in the "
                "same places. Where the board hangs, how far it tilts and how big it is are all "
                "yours to set in VR Options — and the grab rod moves it without opening anything.",
    },
    "de": {
        # German is 15-30 % longer than the English beside it, and these sit in gaps between drawn
        # parts rather than in a column that can grow. Where a faithful translation does not fit,
        # the WORDING is shortened — never the type, which is already at the floor for a picture
        # shown at half its rendered width.
        "callouts": {
            "recesses":   "Kartenmulden",
            "keys":       "Bestätigen · Rückgängig · Überspringen",
            "rest":       "Kurze + lange Rast-Tasten",
            "rod":        "Greifstange",
            "initiative": "Initiativleiste",
            "round":      "Runde",
            "piles":      "Ablage · Verbrannt · Gegenstände",
            "hand":       "Deine Handkarten",
            "active":     "Aktive Karten",
            "objectives": "Aufgaben",
            "elements":   "Elemente",
        },
        "cells": [
            (BLUE, "Die beiden Kartenmulden", [
                "Lege deine zwei gewählten Karten hinein. Links liegt deine Initiative.",
                "Nimm eine Karte wieder heraus; lege sie auf die andere, um zu tauschen.",
                "Ein langsames grünes Pulsieren zeigt, welches Fach das Spiel erwartet.",
            ]),
            (GREEN, "Die drei Tasten — rechts", [
                "Oben: BESTÄTIGEN — legt deine Runde fest und gibt sie wieder frei.",
                "Mitte: RÜCKGÄNGIG. Unten: ÜBERSPRINGEN, nur wenn ein Schritt das erlaubt.",
                "Taste mit der Fingerspitze eindrücken, dabei Grip halten — oder anklicken.",
            ]),
            (ORANGE, "Die beiden Rast-Tasten — links", [
                "Oben: kurze Rast — deine abgeworfenen Karten kommen zurück, eine bleibt weg.",
                "Unten: lange Rast — meldet die lange Rast für diese Runde an.",
                "Beide werden blass, solange das Spiel diese Rast nicht anbietet.",
            ]),
            (CYAN, "Die Greifstange", [
                "Greif sie mit einer Hand, und das ganze Brett kommt mit —",
                "Karten, Tasten, Stapel und Panels zusammen.",
                "Mit beiden Händen greifen macht das Brett größer oder kleiner.",
            ]),
            (PURPLE, "Obere Kante — die Runde", [
                "Hier dockt die Initiativleiste des Spiels selbst an, mit allen Porträts,",
                "samt dem '?' für alle, die sich noch nicht festgelegt haben.",
                "Die Rundennummer ist oben rechts ins Brett eingraviert.",
            ]),
            (GOLD, "Links — Aufgaben und Elemente", [
                "Das Aufgaben-Panel des Szenarios, darunter die Element-Plättchen.",
                "Feuer, Eis, Luft, Erde, Licht und Dunkel, in dieser Reihenfolge.",
                "Ein Element, das du dort nicht siehst, ist nicht infundiert.",
            ]),
            (RED, "Rechts — die Stapel und deine aktiven Karten", [
                "Drei Stapel von oben nach unten, jeder mit seiner eigenen Anzahl.",
                "Tippe einen an, und er fächert sich über dem Brett zum Lesen auf.",
                "Gegenstände kannst du herausnehmen; die anderen beiden sind zum Nachschauen.",
                "Weiter außen: deine gerade aktiven Karten, drei pro Reihe.",
                "Zieh eine heraus, um sie zu lesen — sie kehrt von selbst zurück.",
            ]),
            (PINK, "Deine Handkarten", [
                "Dreh deine freie Hand mit der Handfläche zu dir — der Fächer geht auf.",
                "Nimm eine Karte mit der anderen Hand, oder ziel drauf und drück den Trigger.",
                "Halte eine Karte über den Fächer, und eine Lücke öffnet sich zum Einsortieren.",
            ]),
        ],
        "note": "Gezeigt ist das Eichenbrett; Stahl und Bronze haben dieselben sieben Vertiefungen "
                "an denselben Stellen. Wo das Brett hängt, wie stark es geneigt ist und wie groß "
                "es ist, bestimmst du in den VR-Optionen — und die Greifstange verschiebt es, "
                "ohne dass du irgendetwas öffnen musst.",
    },
}


def rounded(d, box, colour, w=7, r=14, halo=True):
    """A rounded-rect OUTLINE, never a fill: the reader has to see the board underneath it.

    The paper-coloured halo is what keeps a marker legible where it crosses a dark carving; it is
    the same trick the controls diagram's rings use, for the same reason.
    """
    x0, y0, x1, y1 = box
    if halo:
        d.rounded_rectangle([x0 - 3, y0 - 3, x1 + 3, y1 + 3], radius=r + 3, outline=PAPER, width=3)
    d.rounded_rectangle([x0, y0, x1, y1], radius=r, outline=colour, width=w)


def ring(d, xy, colour, r, w=7):
    x, y = xy
    d.ellipse([x - r - 3, y - r - 3, x + r + 3, y + r + 3], outline=PAPER, width=3)
    d.ellipse([x - r, y - r, x + r, y + r], outline=colour, width=w)


def leader(d, colour, pts):
    """A thin leader line ending in a dot on the thing it names.

    Drawn UNDER a paper casing so it stays readable where it crosses a ghost plate or the board's
    own rim, and thin enough (3 px against the markers' 7) that it never competes with them.
    """
    d.line(pts, fill=PAPER, width=9, joint="curve")
    d.line(pts, fill=colour, width=3, joint="curve")
    ex, ey = pts[-1]
    d.ellipse([ex - 7, ey - 7, ex + 7, ey + 7], fill=PAPER)
    d.ellipse([ex - 5, ey - 5, ex + 5, ey + 5], fill=colour)


CALL_SIZE = 29     # the callout type. 29 px is 14.5 px at the 860 the guides show this at — the
CALL_LH   = 38     # floor for a coloured semibold name that has to be read at a glance.


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


def callout_frames():
    """Every callout's DRAWING FRAME in canvas pixels — the anchor, alignment and budget build()
    hands to callout(), for all twelve of them in one place.

    The two plate callouts are placed by the plate that carries them rather than by CALLOUT_GEOM,
    so their frames are derived from exactly the expressions the draw uses. If those two ever drift
    apart, the preflight below measures a box the picture does not have, which is worse than no
    preflight at all.
    """
    frames = dict((k, dict(x=X(g["at"][0]), y=Y(g["at"][1]), align=g["align"],
                           maxw=g["maxw"], rows=g["rows"], colour=g["colour"]))
                  for k, g in CALLOUT_GEOM.items())
    frames["objectives"] = dict(x=X(DOCK_OBJECTIVES[0]) + PLATE_CALLOUT_INSET,
                                y=Y(DOCK_OBJECTIVES[3]) + 14, align="l",
                                maxw=plate_callout_maxw(DOCK_OBJECTIVES), rows=1, colour=GOLD)
    frames["elements"] = dict(x=X(DOCK_ELEMENTS[0]) + PLATE_CALLOUT_INSET,
                              y=Y(DOCK_ELEMENTS[3]) + 12, align="l",
                              maxw=plate_callout_maxw(DOCK_ELEMENTS), rows=1, colour=GOLD)
    return frames


def text_box(probe, lang, key, f, frame):
    """The pixel rectangle one callout's TEXT occupies, by the same arithmetic callout() draws it
    with. Returned as (left, top, right, bottom)."""
    lines = fit(probe, lang, key, f, frame["maxw"], frame["rows"])
    wmax = max(probe.textlength(ln, font=f) for ln in lines)
    if frame["align"] == "c":
        bx0 = frame["x"] - wmax / 2.0
    elif frame["align"] == "r":
        bx0 = frame["x"] - wmax
    else:
        bx0 = frame["x"]
    return (bx0, frame["y"], bx0 + wmax, frame["y"] + CALL_LH * len(lines) - 6)


def _hits(a, b, slack=0.0):
    """Do two (l, t, r, b) rectangles overlap, allowing `slack` px of touching?"""
    return (a[0] < b[2] - slack and b[0] < a[2] - slack
            and a[1] < b[3] - slack and b[1] < a[3] - slack)


def check_callouts():
    """Measure EVERY callout in EVERY language before a single file is written.

    Not folded into the draw: the languages are built one after another, so a failure discovered
    while drawing German would already have left a new English file on disk beside a stale German
    one. A preflight fails with both files untouched.

    THREE THINGS ARE CHECKED, and the third is the one that was missing. fit() proves a label fits
    its gap in WIDTH and in ROWS. What no budget can see is where the resulting BLOCK lands: a
    label that fits its own gap perfectly can still be printed across the label beside it, off the
    bottom of the picture, or over the board it is pointing at — and German, 15-30 % longer than
    the English it is set beside, is what discovers that. So every block is measured, in both
    languages, against the picture's own frame, against the board silhouette, against every ghost
    plate that is not its own, and against every other block.
    """
    probe = ImageDraw.Draw(Image.new("RGB", (8, 8)))
    f = font("Inter-SemiBold.otf", CALL_SIZE)

    for claim, holds in DOCK_CLAIMS:
        if not holds():
            raise SystemExit(
                "THE DOCKS do not describe the board the source describes.\n"
                "  FAILED: %s\n"
                "The constants this checks are ACTIVE_* and DOCK_* — fix those, and the citation "
                "beside them, before touching the drawing." % claim)

    frames = callout_frames()
    for lang in LABELS:
        named = set(LABELS[lang]["callouts"])
        if named != set(frames):
            raise SystemExit("'%s' names %s and the layout knows %s — every callout needs both."
                             % (lang, sorted(named), sorted(frames)))

    # The picture's own block, the board's silhouette, and the ghost plates, all in canvas pixels.
    # The legend rule is drawn 34 px BELOW the block, so "inside the block" is also "clear of the
    # rule and of every legend cell under it".
    block = (MARGIN, TOP, W - MARGIN, TOP + BLOCK_H)
    silhouette = box((-BOARD_W * 0.5, -BOARD_H * 0.5, BOARD_W * 0.5, BOARD_H * 0.5))
    plates = {"objectives": DOCK_OBJECTIVES, "elements": DOCK_ELEMENTS,
              "initiative": DOCK_INITIATIVE, "piles": DOCK_PILES, "active": DOCK_ACTIVE}
    # Only these two are drawn ON their plate, where a real panel carries its title. The other
    # three plates are named from OUTSIDE, down a leader, so their name must stay off every plate
    # including their own — a red name lying on the red plate it names is unreadable, not clearer.
    plate_titles = ("objectives", "elements")

    for lang in LABELS:
        boxes = dict((k, text_box(probe, lang, k, f, g)) for k, g in frames.items())
        for key, b in sorted(boxes.items()):
            where = "'%s' in '%s' is drawn at (%.0f, %.0f)-(%.0f, %.0f)" % ((key, lang) + b)
            if not (block[0] <= b[0] and b[2] <= block[2]
                    and block[1] <= b[1] and b[3] <= block[3]):
                raise SystemExit(
                    "%s, outside the picture block (%.0f, %.0f)-(%.0f, %.0f).\nMove its `at` in "
                    "CALLOUT_GEOM, or shorten the WORDING — the type is already at the floor for a "
                    "picture shown at half its rendered width." % ((where,) + block))
            if _hits(b, silhouette):
                raise SystemExit(
                    "%s, ON THE BOARD ITSELF (%.0f, %.0f)-(%.0f, %.0f).\nEvery name lives outside "
                    "the wood and reaches its marker with a leader; that is what the two lanes "
                    "through the initiative dock are for." % ((where,) + silhouette))
            if key in plate_titles and not _hits(b, box(plates[key])):
                raise SystemExit(
                    "%s, OUTSIDE the '%s' plate it is supposed to be titling." % (where, key))
            for pname, dock in sorted(plates.items()):
                if pname == key and key in plate_titles:
                    continue
                if _hits(b, box(dock)):
                    raise SystemExit(
                        "%s, over the '%s' ghost plate.\nA name printed on a plate that is not "
                        "its own says the wrong thing in the one colour a reader trusts."
                        % (where, pname))
        keys = sorted(boxes)
        for i, ka in enumerate(keys):
            for kb in keys[i + 1:]:
                if _hits(boxes[ka], boxes[kb]):
                    raise SystemExit(
                        "'%s' and '%s' are printed over each other in '%s': %s vs %s.\nShorten "
                        "the WORDING or move one `at` — this is exactly how a translated diagram "
                        "starts stacking one label on the next."
                        % (ka, kb, lang, boxes[ka], boxes[kb]))

    # The leaders have to END on the thing they name; a callout that points at nothing is worse
    # than one with no line at all, and nothing else in this file checks it.
    for key, g in sorted(CALLOUT_GEOM.items()):
        if not g["lead"]:
            continue
        ex, ey = g["lead"][-1]
        target = {"recesses": (-0.084 - SLOT_HALF[0], -SLOT_HALF[1],
                               0.084 + SLOT_HALF[0], SLOT_HALF[1]),
                  "keys": (0.227 - SEAT_HALF[0], -0.0765 - SEAT_HALF[1],
                           0.227 + SEAT_HALF[0], 0.0765 + SEAT_HALF[1]),
                  "rest": (-0.227 - REST_R, -0.0574 - REST_R, -0.227 + REST_R, -0.0574 + REST_R),
                  "rod": (-BOARD_W * 0.275, -0.207, BOARD_W * 0.275, -0.173),
                  "initiative": DOCK_INITIATIVE,
                  "round": (0.183, 0.111, 0.277, 0.141),
                  "piles": DOCK_PILES,
                  "active": DOCK_ACTIVE}[key]
        # 3 mm of tolerance, because two of these END ON A RIM on purpose: the recesses lane lands
        # on the two slots' top corners so it points at both at once, half a millimetre proud of
        # the marker. The defect this is looking for is a lane that misses by centimetres.
        tol = 0.003
        if not (target[0] - tol <= ex <= target[2] + tol
                and target[1] - tol <= ey <= target[3] + tol):
            raise SystemExit(
                "The '%s' leader ends at board-local (%.5f, %.5f), which is not on the part it "
                "names (%.3f, %.3f)-(%.3f, %.3f).\nA marker that lands beside its part is the "
                "defect the first cut of this diagram shipped." % ((key, ex, ey) + target))


def build(lang):
    art = Image.open(ART).convert("RGBA")
    art_scale = art.width / ART_NATIVE_W        # the committed artwork is a resized native render

    # ---- geometry ------------------------------------------------------------------------
    # W, MARGIN, TOP, PPM, X(), Y() and box() are MODULE level, so check_callouts() measures the
    # very pixels this function draws rather than a copy of the frame that can drift from it.
    ppm, block_h = PPM, BLOCK_H

    for claim, holds in ORIENTATION:
        if not holds(ANCHOR):
            raise SystemExit(
                "ANCHOR does not describe the board the source describes.\n"
                "  FAILED: %s\n"
                "If the artwork was re-rendered, it may also have come from the OTHER FACE — the\n"
                "decorated one is board_artwork_zneg.png. Re-run\n"
                "BoardAssetShot.RenderDiagramArtwork, check its ANCHOR log lines put CONFIRM top\n"
                "RIGHT and the rest pads LEFT, and fix ANCHOR before touching anything else."
                % claim)

    def A(name):
        """An anchor, in canvas pixels."""
        lx, ly = ANCHOR[name]
        return (X(lx), Y(ly))

    im = Image.new("RGB", (W, TOP + block_h + 1400), PAPER)

    # The artwork sits at the board's own place in the window: its native (0,0) is board-local
    # (-1000/PPM, +501.3/PPM), scaled by however big the committed file is.
    art_w = round(art.width / art_scale / PPM_NATIVE * ppm)
    art_h = round(art.height / art_scale / PPM_NATIVE * ppm)
    art = art.resize((art_w, art_h), Image.LANCZOS)
    art_x = round(X(-ORIGIN_PX[0] / PPM_NATIVE))
    art_y = round(Y(ORIGIN_PX[1] / PPM_NATIVE))
    im.paste(art, (art_x, art_y), art)
    d = ImageDraw.Draw(im)

    txt = LABELS[lang]

    f_call = font("Inter-SemiBold.otf", CALL_SIZE)
    f_title = font("Inter-SemiBold.otf", 29)
    f_body = font("Inter-Regular.otf", 25)
    f_note = font("Inter-Regular.otf", 24)

    def callout(key, colour, x_px, y_px, align, maxw, lead=None, f=None, rows=None):
        """One short name, in its marker's colour, optionally joined to the marker by a leader.

        check_callouts() has already proved every string here fits; fit() is called again only
        because it is also what returns the wrapped lines.
        """
        f = f or f_call
        lines = fit(d, lang, key, f, maxw, rows)
        widths = [d.textlength(ln, font=f) for ln in lines]
        wmax = max(widths)
        if align == "c":
            bx0 = x_px - wmax / 2.0
        elif align == "r":
            bx0 = x_px - wmax
        else:
            bx0 = x_px
        bx1, by0 = bx0 + wmax, y_px
        by1 = y_px + CALL_LH * len(lines) - 6

        if lead:
            lx, ly = lead[0]
            if ly < by0 - 2:
                start = (lx, by0 - 8)
            elif ly > by1 + 2:
                start = (lx, by1 + 8)
            elif lx > bx1:
                start = (bx1 + 8, ly)
            else:
                start = (bx0 - 8, ly)
            leader(d, colour, [start] + list(lead))

        for i, ln in enumerate(lines):
            if align == "c":
                lx0 = x_px - widths[i] / 2.0
            elif align == "r":
                lx0 = x_px - widths[i]
            else:
                lx0 = x_px
            d.text((lx0, y_px + i * CALL_LH), ln, font=f, fill=colour,
                   stroke_width=5, stroke_fill=PAPER)

    # ---- the docked panels, drawn where the code mounts them -----------------------------
    # Each is a plate in a paper tone with a HINT of what it carries — ruled lines, six chips, a
    # row of portrait tiles, three slab packs — so the picture reads as the layout a player meets
    # rather than as four empty boxes. Nothing here is a screenshot: these are the game's own
    # converted panels, and no render of the mod's asset can contain them.
    GHOST = (247, 244, 238)     # the dock's own ground
    HINT = (204, 195, 180)      # the ruled hint inside it

    def plate(b):
        x0, y0, x1, y1 = box(b)
        d.rounded_rectangle([x0, y0, x1, y1], radius=12, fill=GHOST)
        return x0, y0, x1, y1

    # objectives: its own name where a real panel carries its title, then four ruled lines
    ox0, oy0, ox1, oy1 = plate(DOCK_OBJECTIVES)
    callout("objectives", GOLD, ox0 + PLATE_CALLOUT_INSET, oy0 + 14, "l",
            plate_callout_maxw(DOCK_OBJECTIVES), rows=1)
    for i in range(4):
        yy = oy0 + 82 + i * 30
        d.line([(ox0 + 18, yy), (ox1 - (52 if i % 2 else 18), yy)], fill=HINT, width=6)

    # elements: its own name, then six chips in the game's own order (RemoteElementStrip.cs:367-372)
    ex0, ey0, ex1, ey1 = plate(DOCK_ELEMENTS)
    callout("elements", GOLD, ex0 + PLATE_CALLOUT_INSET, ey0 + 12, "l",
            plate_callout_maxw(DOCK_ELEMENTS), rows=1)
    step = (ex1 - ex0 - 24) / 6.0
    cr = step * 0.36
    for i in range(6):
        cx, cy = ex0 + 12 + step * (i + 0.5), (ey0 + ey1) / 2 + 20
        d.ellipse([cx - cr, cy - cr, cx + cr, cy + cr], outline=HINT, width=6)

    # initiative track: a strip of portrait tiles, two of them still the vanilla '?'
    ix0, iy0, ix1, iy1 = plate(DOCK_INITIATIVE)
    tw = (ix1 - ix0 - 36) / 6.0
    for i in range(6):
        tx = ix0 + 18 + i * tw
        d.rounded_rectangle([tx + 8, iy0 + 16, tx + tw - 8, iy1 - 16], radius=8,
                            fill=(232, 226, 214) if i < 4 else None, outline=HINT, width=5)

    # THE TWO LEADER LANES THROUGH THE DOCK. Both cross the initiative strip, and both are meant to
    # go down a SEAM between two tiles rather than over one. That alignment is arithmetic on the
    # tile pitch above, not a constant anybody chose, so it is asserted rather than trusted: change
    # the tile count or the padding and this fails loudly instead of drawing a line across a face.
    seams = [ix0 + 18 + i * tw for i in range(1, 6)]
    for key in ("recesses", "round"):
        lane = X(CALLOUT_GEOM[key]["lead"][0][0])
        if not any(abs(lane - s) <= 4.0 for s in seams):     # the seam is 16 px; the line casing 9
            raise SystemExit(
                "The '%s' leader would cross an initiative TILE, not the seam between two.\n"
                "Lane at %.1f px; seams at %s. Re-aim the lane in CALLOUT_GEOM, or move the "
                "callout below the board the way the three keys are." %
                (key, lane, ", ".join("%.1f" % s for s in seams)))

    # the three stacks, in the order PileViewer lays them out: discard, burnt, items
    px0, _, px1, _ = plate(DOCK_PILES)
    for cy_m in PILE_Y:
        cx, cy = (px0 + px1) / 2 - 4, Y(cy_m)
        hw, hh = (px1 - px0) * 0.33, 0.042 * ppm
        for s in range(3):
            o = (2 - s) * 6
            d.rounded_rectangle([cx - hw + o, cy - hh + o, cx + hw + o, cy + hh + o],
                                radius=7, fill=GHOST, outline=HINT, width=5)

    # the active-card matrix, one zone further out: ACTIVE_ROWS x ACTIVE_COLS card ghosts on the
    # shipped grid, at half the real card size (see ACTIVE_SCALE). The LOWER row is drawn last
    # because that is the order the real column stacks them — each row sits nearer the viewer than
    # the one above and covers its tops, which is what the 0.70 row step is for.
    ax0, ay0, ax1, ay1 = plate(DOCK_ACTIVE)
    acw, ach = ACTIVE_CARD[0] * 0.5 * ppm, ACTIVE_CARD[1] * 0.5 * ppm
    for r in range(ACTIVE_ROWS):
        for c in range(ACTIVE_COLS):
            cx = X(ACTIVE_MOUNT[0] + (c - (ACTIVE_COLS - 1) * 0.5) * ACTIVE_STEP[0])
            cy = Y(ACTIVE_MOUNT[1] + ((ACTIVE_ROWS - 1) * 0.5 - r) * ACTIVE_STEP[1])
            d.rounded_rectangle([cx - acw, cy - ach, cx + acw, cy + ach], radius=7,
                                fill=GHOST, outline=HINT, width=5)

    # your hand: five cards fanned over the palm that holds them
    fx, fy = X(FAN_CENTRE[0]), Y(FAN_CENTRE[1])
    cw, ch = 0.040 * ppm, 0.058 * ppm
    for i in range(5):
        card = Image.new("RGBA", (round(cw) + 10, round(ch) + 10), (0, 0, 0, 0))
        ImageDraw.Draw(card).rounded_rectangle([5, 5, cw + 5, ch + 5], radius=7,
                                              fill=GHOST, outline=HINT, width=5)
        card = card.rotate((2 - i) * 14, Image.BICUBIC, expand=True)
        dx, dy = (i - 2) * cw * 0.66, abs(i - 2) * ch * 0.10
        im.paste(card, (round(fx + dx - card.width / 2), round(fy + dy - card.height / 2)), card)

    # ---- the markers -----------------------------------------------------------------------
    # The board's own parts get the full 7 px weight; the docks around it get 5 px and no halo, so
    # the eye lands on the board first and reads the ring of docks as context.
    for n in ("Slot1", "Slot2"):
        cx, cy = A(n)
        rounded(d, (cx - SLOT_HALF[0] * ppm, cy - SLOT_HALF[1] * ppm,
                    cx + SLOT_HALF[0] * ppm, cy + SLOT_HALF[1] * ppm), BLUE, r=16)

    for n in ("ShortRestToken", "LongRestToken"):
        ring(d, A(n), ORANGE, r=REST_R * ppm)

    for n in ("ButtonSeat1", "ButtonSeat2", "ButtonSeat3"):
        cx, cy = A(n)
        rounded(d, (cx - SEAT_HALF[0] * ppm, cy - SEAT_HALF[1] * ppm,
                    cx + SEAT_HALF[0] * ppm, cy + SEAT_HALF[1] * ppm), GREEN, r=11)

    # grab rod: BoardW * 0.55 long, its mount 30 mm below the bottom edge (PlayTray BuildHandle)
    rx, ry = A("GrabRod")
    hl, hh2 = BOARD_W * 0.55 * 0.5 * ppm, 0.017 * ppm
    rounded(d, (rx - hl, ry - hh2, rx + hl, ry + hh2), CYAN, r=round(hh2))

    # The round readout, engraved into the board's top-right corner: ReadoutBase (0.235, 0.125),
    # which is the strip between the top key seat and the board's own rim.
    # (PlayTray.3.Pose.cs:571, drawn by PlayTray.5.Status.cs:72-108.)
    rox, roy = X(0.230), Y(0.126)
    rounded(d, (rox - 0.047 * ppm, roy - 0.015 * ppm, rox + 0.047 * ppm, roy + 0.015 * ppm),
            PURPLE, w=6, r=8)

    # the docks
    for b, colour in ((DOCK_INITIATIVE, PURPLE), (DOCK_OBJECTIVES, GOLD),
                      (DOCK_ELEMENTS, GOLD), (DOCK_PILES, RED), (DOCK_ACTIVE, RED)):
        rounded(d, box(b), colour, w=5, r=12, halo=False)
    rounded(d, (fx - 2.15 * cw, fy - 1.15 * ch, fx + 2.15 * cw, fy + 1.0 * ch),
            PINK, w=5, r=12, halo=False)

    # ---- the callouts ----------------------------------------------------------------------
    # Drawn LAST, over everything, so a leader crossing a dock plate or the board's rim stays
    # readable and a name never disappears under a marker.
    for key, g in CALLOUT_GEOM.items():
        callout(key, g["colour"], X(g["at"][0]), Y(g["at"][1]), g["align"], g["maxw"],
                lead=[(X(p[0]), Y(p[1])) for p in g["lead"]] if g["lead"] else None,
                rows=g["rows"])

    # ---- the legend ----------------------------------------------------------------------
    rule_y = TOP + block_h + 34
    d.line([(MARGIN, rule_y), (W - MARGIN, rule_y)], fill=RULE, width=2)

    col_x = [56, 616, 1176]
    col_w = 490

    def cell(x, y, colour, title, lines, measure=False):
        yy = y
        for i, ln in enumerate(wrap(d, title, f_title, col_w - 40)):
            if not measure:
                if i == 0:
                    d.rounded_rectangle([x, yy + 6, x + 22, yy + 26], radius=6,
                                        outline=colour, width=6)
                d.text((x + 40, yy), ln, font=f_title, fill=INK)
            yy += 38
        yy += 4
        for line in lines:
            for ln in wrap(d, line, f_body, col_w - 40):
                if not measure:
                    d.text((x + 40, yy), ln, font=f_body, fill=INK_SOFT)
                yy += 32
        return yy

    # Row pitch follows the TALLEST cell in the row, in the language being drawn.
    rows = [txt["cells"][0:3], txt["cells"][3:6], txt["cells"][6:8]]
    y = rule_y + 40
    for row in rows:
        bottom = y
        for c, (colour, title, lines) in enumerate(row):
            bottom = max(bottom, cell(col_x[c], y, colour, title, lines))
        if len(row) == 2:                       # the last row is two cells; the note fills the rest
            ny = y + 6
            for ln in wrap(d, txt["note"], f_note, W - 56 - col_x[2]):
                d.text((col_x[2], ny), ln, font=f_note, fill=INK_SOFT)
                ny += 33
            bottom = max(bottom, ny)
        y = bottom + 34

    out = os.path.join(HERE, "board-%s.png" % lang)
    im = im.crop((0, 0, W, y + 12))
    im.quantize(colors=200, method=Image.FASTOCTREE).save(out, optimize=True)
    print("wrote %s  (%d x %d, %d kB)" % (out, im.width, im.height, os.path.getsize(out) // 1024))


if __name__ == "__main__":
    check_callouts()
    for lang in ("en", "de"):
        build(lang)
