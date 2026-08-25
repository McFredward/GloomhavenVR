#!/usr/bin/env python3
"""gen_board.py -- procedural author of the three GloomhavenVR control boards.

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_board.py -- [oak|steel|bronze|all]

WHY PROCEDURAL
--------------
The three shipped boards are decimations of an AI photogrammetry mesh: 1288-1639 hole
loops each, more boundary edges than faces, thousands of loose verts.  They are not
repairable.  A control board is a slab with recesses -- authored geometry, not scanned
geometry -- so this file authors it.

THE AXIS CONVENTION (measured off the shipped FBXs, do not "fix" it)
-------------------------------------------------------------------
Authoring happens in Blender with

    +X  board LONG axis, buttons on +X, rest pads on -X
    +Y  board SHORT axis, ShortRestToken on +Y, LongRestToken on -Y
    +Z  board NORMAL, the DECORATED face is at z = 0 and the body hangs at z <= 0.

The FBX exporter's Z-up -> Y-up conversion turns that into FBX/Unity space

    X_fbx = x_blender      (long)
    Y_fbx = z_blender      (normal; decorated face at MAX Y_fbx)
    Z_fbx = -y_blender     (short)

which is byte-for-byte the convention of all three shipped FBXs (verified by ray-casting
their faces: PlayTray_prepped's flat back is at local Y = 0.005, its recess floors and
rim are at local Y = 0.012..0.027, i.e. the decorated face is the MAX-Y end).  It is
also what `BuildBoard.cs` asserts at line 175: `n = cross(Slot2-Slot1, ShortRest-LongRest)`
points out of the UNDECORATED BACK.  With Slot1 at -X, Slot2 at +X, ShortRest at +Y and
LongRest at -Y, that cross product lands on -Y_fbx = the back.  BuildBoard then re-derives
the prefab-local orientation itself (decorated normal -> -Z, long -> +X), which is the
"decorated face toward -Z" the board contract talks about: that sentence describes the
PREFAB space, not the Blender authoring space.  See the report.

LAYOUT (identical silhouette for all three, per-style trim)
-----------------------------------------------------------
    +----------------------------------------------------------------+
    |  frame band (+ style ornaments welded into it)                  |
    |  +--------+   +-------------+  +-------------+   +--------+     |
    |  | O  pad |   |  card slot  |  |  card slot  |   | seat 1 |     |
    |  |        |   |     1       |  |      2      |   | seat 2 |     |
    |  | O  pad |   |             |  |             |   | seat 3 |     |
    |  +--------+   +-------------+  +-------------+   +--------+     |
    +----------------------------------------------------------------+

The left rest panel and the right button console are the SAME rounded-rect sub-panel,
recessed to the same depth with the same edge profile; the pads and the seats are then
cut into their floors with the same profile again.  That is what makes the three button
seats read as designed in rather than punched in.

OUTPUT
------
  unity/GloomhavenVR.Assets/Assets/Bundle/Table/PlayTray_prepped.fbx    (oak)
  unity/GloomhavenVR.Assets/Assets/Bundle/Table/PlayTray_9capjqp6.fbx   (steel)
  unity/GloomhavenVR.Assets/Assets/Bundle/Table/PlayTray_16vm268h.fbx   (bronze)
  unity/board-prep/out/<style>_uv.json                                   (texture lane)
"""

import bpy
import bmesh
import json
import math
import os
import sys

from mathutils import Vector

# --------------------------------------------------------------------------- constants --

LONG = 0.640          # contract: long edge exactly 0.640 m
SHORT = 0.320         # contract: short edge exactly 0.320 m
ATLAS = 2048
# Two different distances, and conflating them is what the contract did.
#
# REGION_INSET is the margin between a shell and the wall of its atlas RECTANGLE.  The
# contract's six rectangles ABUT (face ends at v=0.5, frame starts at v=0.5), so the gutter
# between two regions is whatever the two sides inset by and nothing else -- measured
# against the rectangles themselves it is 0 px and always would be.  The texture lane's
# compositor insets every region by 12 px and dilation-fills the band, which needs the mesh
# lane to keep 12 px clear too: 13.3 px here, so the finished gutter is ~25 px.
#
# ISLAND_PAD is the gap between two shells inside one region -- the contract's ">= 8 px at
# 2048^2" (8/2048 = 0.0039).  10.2 px, with the margin measured back out of the rasterised
# atlas rather than assumed.
REGION_INSET = 0.0065   # 13.3 px at 2048
ISLAND_PAD = 0.0050     # 10.2 px at 2048
UV_PAD = ISLAND_PAD     # historical name, used for island-to-island spacing
INNER_FILL = 0.90     # rest pads and button seats take the same share of their panel

# ---------------------------------------------- the back and side RELIEF (ModBuild 278) --
#
# "Auf den Texturen sind Schrauben und Halzplatten etc zu sehen, also eigentlich
#  3-dimensionale Objekte.  Sie werden aber flach nur auf der Textur dargstellt.  Ich
#  moechte, dass du das Mesh fuer die Seiten und Rueckseite an die Textur anpasst, so wie
#  du es auch fuer die Vorderseite bereits sehr erfolgreich gemacht hast, um das Board noch
#  realistischer zu machen."
#
# The ModBuild 276 art (unity/board-prep/out/board_back_<style>.png) paints straps, rivets,
# nails and cast ribs onto a plate that is ONE FLAT N-GON.  Its relief lives entirely in a
# normal map derived from that same art (img2img/tex_backfill.py), and a normal map has no
# silhouette, no occlusion and no stereo parallax -- which is exactly the "flach" he saw
# through a headset.  Everything below turns the features he NAMED into geometry.
#
# WHERE THE NUMBERS COME FROM, and why no registration step is needed.
# tex_backfill places the back art at each texel's own BOARD COORDINATES:
#
#       x = (u - 0.5) * LONG        y = (0.5 - v) * SHORT
#
# with (u, v) normalised inside plate_rect(art) resampled to 2048 x 1024.  That map was
# MEASURED, not assumed: re-running tex_backfill's own BACK assignment through it
# reproduces the shipped atlas's BACK texels exactly -- mean |err| 0.00 of 255 and
# r = +1.0000 -- while a planted +3 % shift in u gives 17.30/255 and r = +0.18, and a
# flipped v gives 16.35/255 and r = +0.24.  Both controls fire, so the null result is a
# measurement and not a tautology.
#
# So a feature measured in PLATE PIXELS is built in metres with no fitting, no homography
# and no block matcher.  Every figure in BACK_ART below is a measurement off those three
# images at 1 px = 0.3125 mm.  The instrument for the dome-like features (nails, rivets) is
# a vertical derivative-of-Gaussian matched filter with a lobe-midpoint centre estimator,
# validated on planted domes -- 5 of 5 recovered, residual 1.73 +/- 0.10 px -- against a
# NULL input that returns nothing.  1.73 px is 0.54 mm, which is 0.65 atlas texels at the
# back's 1.21 tex/mm: below the resolution of the map the art is written into.
#
# WHY EVERY BACK WALL IS A STRAIGHT CHAMFER OF AT MOST 40 DEGREES, and why that is not
# taste.  Two independent constraints, both measured:
#
#   1. img2img/gen_geobuf.py calls a triangle BACK when its normal is within
#      acos(0.7) = 45.6 deg of the thickness axis.  A steeper wall lands in INTERIOR, which
#      tex_backfill never paints and tex_composite's pushpull_fill would smear.
#   2. The whole back is ONE island under ONE top-down projection.  A vertical wall has
#      ZERO area under that projection: a degenerate UV island, which no atlas can feed and
#      gen_uvcheck cannot measure.
#
# fillet() is therefore the WRONG profile here even though it is the right one on the
# front: it is a quarter ellipse starting at the pole, so its FIRST step is nearly vertical
# (69.7 deg at n=3, w=2.2 mm, d=1.6 mm).  A straight chamfer has one slope by construction
# and that slope is checkable, which back_stack() asserts on every ring it builds.
MAX_BACK_SLOPE_DEG = 40.0
BACK_PX_M = LONG / 2048.0        # 0.3125 mm; SHORT / 1024 is the same number (2:1 aspect)


def back_x(col):
    """Plate column -> board x in metres."""
    return (col / 2048.0 - 0.5) * LONG


def back_y(row):
    """Plate row -> board y in metres.  Row 0 is the art's TOP row, which is +SHORT/2."""
    return (0.5 - row / 1024.0) * SHORT


def back_m(n):
    """A length in plate pixels -> metres."""
    return n * BACK_PX_M


def ellipse(cx, cy, a, b, n):
    """CCW ellipse with a fixed vertex count.  ell_gen's inset shrinks both semi-axes by
    the same ABSOLUTE amount, which is what a chamfer of constant width does to an
    outline -- an ellipse offset is not an ellipse, but at 1.6 mm on a 4.2 mm semi-axis the
    difference is 0.05 mm and the vertex-count stability is worth far more than that."""
    return [(cx + a * math.cos(2 * math.pi * i / n), cy + b * math.sin(2 * math.pi * i / n))
            for i in range(n)]


def ell_gen(cx, cy, a, b, n):
    return lambda d: ellipse(cx, cy, max(a - d, 3.0e-4), max(b - d, 3.0e-4), n)


# STEEL's back art came back filling the whole 3:2 frame instead of the 2:1 pad, so it is
# resampled 1.324x in the long direction (img2img/README.md, "THE 3:2 TRAP").  Its rivets
# are therefore painted as ~8.4 x 6.6 mm ELLIPSES, not circles, and the geometry matches
# the art AS MAPPED rather than the art as generated -- the mapping is what the player sees.
BACK_ART = {
    "oak": {
        # Two iron straps with four forged square nails each.  Strap edges are the dark
        # shadow lines in the plate's own column profile; nail centres are the matched
        # filter's.  Left strap 203..339 px, right 1701..1842 px -- NOT symmetric, and the
        # 1.7 mm difference is kept rather than tidied away, because the art is the truth
        # the geometry has to register to.
        # (x0_px, x1_px, y0_px, y1_px, corner_r_m)
        "straps": [(203.0, 339.0, 16.0, 1008.0, 0.0050),
                   (1701.0, 1842.0, 16.0, 1008.0, 0.0050)],
        "strap_h": 0.0020, "strap_run": 0.0026, "strap_seg": 2, "strap_seg_xy": (3, 1, 14),
        "nails": [(271.0, 107.0), (271.0, 398.0), (271.0, 650.0), (271.0, 911.0),
                  (1775.0, 114.0), (1775.0, 402.0), (1775.0, 653.0), (1775.0, 917.0)],
        "nail_w": 0.0125, "nail_r": 0.0022,
        "nail_h": 0.0010, "nail_run": 0.0012, "nail_seg": 2, "nail_seg_xy": (2, 1, 1),
        # The three PLANK SEAMS at rows 246 / 493 / 739 stay in the normal map, on purpose.
        # The art paints them 1.6 mm wide.  A wall no steeper than MAX_BACK_SLOPE_DEG can
        # then carry at most 0.8 * tan(40) = 0.67 mm of depth, and the two walls meet in a
        # V with no floor at all -- while widening the slot enough to carry real depth
        # would put a 3 mm groove where the art paints 1.6 mm, i.e. a doubled edge by
        # construction.  A groove has no silhouette and no occlusion either, so the two
        # things geometry buys over a normal map are both absent.  This is the one feature
        # where the map is the better instrument, and it already carries it.
        "seam_rows": [246.0, 493.0, 739.0],
    },
    "steel": {
        # A riveted plate: the border band and the two vertical straps are ONE continuous
        # frame at the plate's own surface, and the three fields between them are recessed.
        #
        # THE FIRST VERSION MODELLED THE STRAPS AS SEPARATE RAISED BARS on the floor of one
        # big recess, and it was wrong in a way the mesh reported rather than the eye: a bar
        # inset 3 px from the recess OUTLINE still crosses the recess FLOOR, which sits a
        # further panel_run = 2.0 mm in, so its footprint was not a hole in the surface it
        # was welded into -- 10 boundary edges, 2 hole loops, 2 non-manifold edges on steel.
        # Three fields is also the better reading of the art: the straps have the band's
        # colour and the band's rivets, because they ARE the band.
        # (x0_px, x1_px, y0_px, y1_px, corner_r_m)
        "fields": [(52.0, 355.0, 38.0, 989.0, 0.0080),
                   (459.0, 1589.0, 38.0, 989.0, 0.0080),
                   (1693.0, 1994.0, 38.0, 989.0, 0.0080)],
        "panel_d": 0.0015, "panel_run": 0.0020, "panel_seg": 2, "panel_seg_xy": (4, 12, 13),
        "rivet_a": 0.0042, "rivet_b": 0.0033, "rivet_n": 10,
        "rivet_h": 0.0013, "rivet_run": 0.0016, "rivet_seg": 2,
        "rivets_band": (
            [(x, 21.0) for x in (40.9, 150.8, 262.7, 363.1, 467.0, 567.0, 671.3, 772.5,
                                 876.1, 976.7, 1078.7, 1181.8, 1285.0, 1382.6, 1488.0,
                                 1589.0, 1688.3, 1793.8, 1895.9, 2005.6)] +
            [(x, 1005.0) for x in (39.6, 150.3, 262.2, 363.6, 465.0, 568.8, 670.0, 773.3,
                                   876.5, 977.5, 1079.8, 1181.7, 1284.9, 1386.8, 1486.2,
                                   1589.7, 1692.2, 1794.2, 1897.9, 2006.9)] +
            [(28.7, y) for y in (92.6, 168.1, 244.3, 320.2, 399.0, 476.2, 552.4, 632.5,
                                 708.0, 787.1, 865.8, 941.9)] +
            [(2018.3, y) for y in (92.0, 168.1, 247.5, 321.6, 399.7, 477.8, 555.6, 633.8,
                                   708.9, 786.5, 865.3, 943.0)]),
        "rivets_strap": ([(410.0, y) for y in (123.0, 273.5, 431.7, 584.7, 739.0, 897.2)] +
                         [(1639.8, y) for y in (123.0, 273.5, 431.7, 584.7, 739.0, 897.2)]),
    },
    "bronze": {
        # A sand-cast reverse: three recessed panels divided by cast stiffening ribs, the
        # ribs being what is LEFT of the plate surface between them.  No rivets in the art.
        "panels": [(89.0, 614.0, 75.0, 955.0, 0.0120),
                   (671.0, 1375.0, 75.0, 955.0, 0.0120),
                   (1426.0, 1956.0, 75.0, 955.0, 0.0120)],
        "panel_d": 0.0015, "panel_run": 0.0024, "panel_seg": 2, "panel_seg_xy": (4, 10, 12),
    },
}

# ------------------------------------------------ the SIDE relief (ModBuild 280) --
#
# "Ich will auch die Seiten, Aufgabe daher noch nicht fertig.  Mach damit weiter."
#
# ModBuild 279 answered half of his request: the BACKS' painted ironwork became geometry
# and the sides were left as one flat vertical wall between two small chamfers.  This is
# the other half.
#
# WHAT A SIDE IS, AND WHY IT IS NOT A THIRD DECORATED FACE.  A board side is the EDGE of
# the construction the front and the back already establish, so the design is read off
# those two and not invented.  Measured off the ModBuild 276 back plates
# (unity/board-prep/out/board_back_<style>.png), at 1 px = 0.3125 mm:
#
#   oak     four planks running the LONG axis, three seams, and TWO CROSS BATTENS that
#           run from plate edge to plate edge -- y 16..1008 px of 1024, i.e. +-155.0 mm
#           against a back-plate half-width of 158.2 mm.  The battens stop 3.2 mm short,
#           which is inside the back's own bottom chamfer: they REACH THE RIM.
#   steel   a riveted plate whose recessed fields span y 38..989 px = +-148.4 mm, so a
#           9.8 mm BORDER BAND runs unbroken all the way round between them and the rim.
#           The two vertical straps butt into that band and never reach the side.
#   bronze  three cast panels at y 75..955 px = +-126.6 mm, so a 31.6 mm BORDER RIB runs
#           unbroken all the way round.  The two inner stiffening ribs butt into it.
#
# So exactly one style has a back feature that crosses its rim, and it is oak.  That is a
# measurement, not a preference, and it is why only oak gets discrete side features.
#
# WHY THE RELIEF IS CUT IN AND NEVER STANDS PROUD.  BOARD-CONTRACT.md fixes the long edge
# at exactly 0.640 m and the short edge at 0.320 m, and `BoardBuilder` reads those extents
# to place the seats, the pads and the docks.  A rivet head standing 1 mm off the side
# would move them.  Every ring below therefore steps INWARD from the 0.640 x 0.320
# silhouette, and the features that must read as proud -- oak's battens -- are the places
# where the surrounding field is cut BACK and the feature stays flush with the nominal
# silhouette.  The bounding box is unchanged by construction.
#
# WHY EVERY SIDE STEP IS AT MOST MAX_RIM_TILT_DEG OFF VERTICAL, and this one is not taste
# either.  img2img/gen_geobuf.py calls a triangle RIM when its normal is within
# acos(0.5) = 60 deg of the board PLANE, i.e. no more than 30 deg off the thickness
# direction; steeper than that and an upward-facing step lands in FRONT (which the front
# pass painted from a camera that never saw it) and a downward-facing one lands in BACK
# (which tex_backfill paints with the BACK PLATE ART at that texel's board coordinates --
# the back's planks stretched onto a rim step).  A 45 deg chamfer, the obvious profile,
# sits at axial = 0.707 and lands in exactly those two wrong groups.  So a side step
# spends z, not inset: 3.0 mm of rebate costs 5.7 mm of the band's height, which on a
# 20-26 mm band is most of the budget.  The outcome is checked from the other end as well:
# after this edit gen_geobuf's FRONT and INTERIOR texel counts are UNCHANGED on all three
# boards, so nothing leaked out of the RIM group.
#
# WHAT IS *NOT* GEOMETRY HERE, and the numbers behind each:
#
#   oak's three plank seams (rows 246/493/739, painted 1.6 mm wide).  On the back they
#     stayed in the normal map because a <=40 deg wall cannot carry depth across 1.6 mm.
#     On the side the wall limit is different but the resolution is not: the silhouette
#     carries 26 samples along a 600 mm run and 13 along a 280 mm one, so placing a 2 mm
#     groove needs ~1 mm perimeter resolution -- ny ~ 320, about 7 000 extra triangles on
#     its own -- and a groove wide enough to be affordable (5-6 mm) would meet the back's
#     1.6 mm painted seam at the arris as a step three times too wide.  That is the same
#     doubled-edge argument that kept them out of the back, reached independently.
#
#   steel's rivets.  They are the feature he NAMED, so this is the one that had to be
#     argued rather than assumed -- and the FIRST argument against them was wrong, which is
#     worth writing down.  It was a triangle budget: 8.4 mm domes at a 32 mm pitch need
#     ~6.4 mm of perimeter resolution, i.e. nx ~ 96, and that is ~8 700 triangles on a board
#     already at 15 716 of 24 000.  But that costing assumes UNIFORM densification, and
#     `cuts_x` below does not densify uniformly -- 40 rivets at four cuts each is ~160
#     vertices per ring, about 2 200 triangles, comfortably affordable.  So the budget does
#     NOT decide this.
#     What decides it is EVIDENCE: unlike oak's battens, nothing in the steel art puts a
#     rivet on the rim.  The border band is what meets the side and the rivets sit on its
#     FACE, 6.6-9.0 mm in.  What the shipped side DOES show is a row of painted rivet
#     ghosts, and those are a sampling artefact of tex_backfill's rim walk, measured in
#     ModBuild 278.  Building 40 real rivets to match them would be adapting the object to
#     the artefact instead of the texture to the object -- which is the same call the
#     ModBuild 278 record made, reached here from the opposite direction.
#
# Every entry is (delta_inset, delta_z, mod_inset) in metres, walked from `widest` -- the
# true 0.640 x 0.320 ring at the bottom of the front edge roll-over -- down to `low`, the
# last ring before the back chamfer.  `mod_inset` is the inset the MASKED vertices take on
# that ring (None = follow the base), so 0.0 holds a crossing feature at the nominal
# silhouette while the field around it is cut back.  The deltas are asserted to sum to the
# band's exact height, so a mistuned profile fails the build instead of moving the back
# plate, and every step is asserted against MAX_RIM_TILT_DEG.
#
# THE REBATE IS 3.0 mm AND THE STEPS RUN AT 27.8 DEGREES, ONE MARGIN OFF THE LIMIT, and
# that is the second try.  The first shipped 2.0 mm at 24 degrees and it was measured
# through the real shader before being replaced: a rail and a rebate floor have the SAME
# surface normal -- both are vertical walls -- so the only thing that separates them
# tonally is the step between them, and against BoardLit's two baked directions a 2 mm step
# was a quiet band flat-on.  A cavity term was the obvious remedy and it was BUILT
# (gen_rimao.py bakes real AO into atlas space) and then NOT USED, because the bake says
# the rebate does not occlude: oak's rails come back 0.9996 against 0.9861 in the rebate,
# a 1.4 % difference.  A shallow open groove genuinely is not a cavity, and darkening it
# anyway would be painting shading the geometry does not produce.  So the depth was spent
# instead, which is the term that does change the picture.
MAX_RIM_TILT_DEG = 28.0

SIDE_ART = {
    # A laminated timber edge: the frame stock's 3 mm facing rail, a 5.6 mm plank core set
    # back 3 mm, a 3 mm base rail -- and the two battens crossing the core flush, which is
    # what an iron-strapped plank board looks like from the side.
    "oak": {
        "profile": [(0.0000, -0.0030, None),       # facing rail
                    (0.0030, -0.0057, 0.0),        # step in, 27.8 deg off vertical
                    (0.0000, -0.0056, 0.0),        # the plank core
                    (-0.0030, -0.0057, None),      # step back out
                    (0.0000, -0.0030, None)],      # base rail
        # plate columns 203..339 and 1701..1842 -> board x, verbatim off the art.  Not
        # symmetric, and the 1.7 mm difference is kept for the same reason BACK_ART keeps
        # it: the art is the truth the geometry registers to.
        "cross_x": [(back_x(203.0), back_x(339.0)), (back_x(1701.0), back_x(1842.0))],
        "cross_ramp": 0.0012,
    },
    # Two plate edges sandwiching a recessed core: the front plate's 4 mm rail, a 7.0 mm
    # edge band set back 3 mm, and the back plate's 4 mm rail -- which is the 9.8 mm border
    # band of the back art, seen edge-on.
    "steel": {
        "profile": [(0.0000, -0.0040, None),
                    (0.0030, -0.0057, None),
                    (0.0000, -0.0070, None),
                    (-0.0030, -0.0057, None),
                    (0.0000, -0.0040, None)],
        "cross_x": [],
        "cross_ramp": 0.0012,
    },
    # A sand casting: both mould halves draft away from a PARTING LINE, and the parting
    # line is the widest point.  `widest` is pinned at inset 0 by the front roll-over, so
    # the crown is pinned there too and the drafts run inward from both.
    "bronze": {
        # Six rings, one more than the other two, and the extra one is the assert's doing:
        # a five-ring version ended 2.0 mm inside the silhouette and `side_stack`'s
        # interlock caught that the back chamfer would then have to step OUTWARD, which
        # folds the back's top-down UV projection.  Bringing the flange back to full width
        # before the chamfer is both the fix and the better casting: three full-width bands
        # separated by two drafted grooves, with the parting crown in the middle.
        "profile": [(0.0020, -0.0040, None),       # upper draft, 26.6 deg
                    (-0.0020, -0.0040, None),      # up to the parting crown
                    (0.0000, -0.0016, None),       # the crown band
                    (0.0020, -0.0040, None),       # lower draft
                    (-0.0020, -0.0040, None),      # back out to the flange
                    (0.0000, -0.0024, None)],      # the flange
        "cross_x": [],
        "cross_ramp": 0.0012,
    },
}

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TABLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")
OUT = os.path.join(REPO, "unity", "board-prep", "out")

# The contract's default atlas region map.  Kept verbatim: the `face` rect is 2048x1024,
# exactly the board's 2:1 aspect, so the top surface maps 1:1 into it with no distortion.
REGIONS = {
    "face":         (0.00, 0.00, 1.00, 0.50),
    "frame":        (0.00, 0.50, 0.50, 0.75),
    "slot_floor":   (0.50, 0.50, 0.75, 0.75),
    "rest_pads":    (0.75, 0.50, 1.00, 0.75),
    "button_seats": (0.00, 0.75, 0.50, 1.00),
    "sides":        (0.50, 0.75, 1.00, 1.00),
}
REGION_KIND = {
    "face":         "decorated top face",
    "frame":        "outer frame + corner brackets",
    "slot_floor":   "card recess floors",
    "rest_pads":    "the two round rest pads",
    "button_seats": "the THREE button recesses",
    "sides":        "edges + back",
}

# ------------------------------------------------------------------------ style tables --

STYLES = {
    # --------------------------------------------------------------- OAK: warm timber --
    "oak": {
        "fbx": "PlayTray_prepped.fbx",
        "thickness": 0.0300,
        "corner_r": 0.0200,          # softly worn silhouette
        "edge": (0.0035, 0.0035, 3),  # (inset, drop, segments) -> rolled-over timber edge
        "frame_w": 0.0240,
        "frame_r": 0.0130,
        "field_drop": 0.0020,
        "field_chamfer": (0.0035, 0.0020, 2),
        "seg": (8, 26, 13),          # silhouette (corner, long run, short run)
        # feature edge profiles: (bead_w, bead_h) then (fillet_w, fillet_d, segments)
        "bead": None,                # oak has no raised bead around its recesses
        "fillet": (0.0032, 0.0018, 3),
        "small_fillet": (0.0022, 0.0013, 2),
        "ledge": None,
        "depth_card": 0.0060,
        "depth_panel": 0.0038,
        "depth_pad": 0.0036,
        "depth_seat": 0.0040,
        "orn_h": 0.0036,
        "orn_bevel": 0.0016,
        "orn_seg": 3,
        "ornaments": "oak",
    },
    # -------------------------------------------------------- STEEL: plated, riveted --
    "steel": {
        "fbx": "PlayTray_9capjqp6.fbx",
        "thickness": 0.0300,
        "corner_r": 0.0120,          # harder silhouette
        "edge": (0.0018, 0.0018, 1),  # one hard chamfer
        "frame_w": 0.0210,
        "frame_r": 0.0060,
        "field_drop": 0.0022,
        "field_chamfer": (0.0016, 0.0022, 1),
        "seg": (6, 26, 13),
        "bead": None,
        "fillet": (0.0014, 0.0014, 1),   # single machined chamfer
        "small_fillet": (0.0011, 0.0011, 1),
        "ledge": (0.0030, 0.0018),       # counterbore: step in 3 mm after an 1.8 mm wall
        "depth_card": 0.0060,
        "depth_panel": 0.0040,
        "depth_pad": 0.0038,
        "depth_seat": 0.0042,
        "orn_h": 0.0028,
        "orn_bevel": 0.0010,
        "orn_seg": 1,
        "ornaments": "steel",
    },
    # ------------------------------------------------ BRONZE: cast, rounded, beaded --
    "bronze": {
        "fbx": "PlayTray_16vm268h.fbx",
        "thickness": 0.0320,
        "corner_r": 0.0300,          # cast, rounder
        "edge": (0.0060, 0.0060, 4),
        "frame_w": 0.0260,
        "frame_r": 0.0180,
        "field_drop": 0.0028,
        "field_chamfer": (0.0045, 0.0028, 3),
        "seg": (12, 26, 13),
        "bead": (0.0034, 0.0028),    # raised ornamental border around every recess
        "fillet": (0.0040, 0.0026, 3),
        "small_fillet": (0.0028, 0.0018, 3),
        "ledge": None,
        "depth_card": 0.0062,
        "depth_panel": 0.0042,
        "depth_pad": 0.0040,
        "depth_seat": 0.0044,
        "orn_h": 0.0040,
        "orn_bevel": 0.0028,
        "orn_seg": 4,
        "ornaments": "bronze",
    },
}

# ----------------------------------------------------------------------- 2D outlines --


def rrect(cx, cy, w, h, r, nc, nx, ny, cuts_x=()):
    """Rounded rectangle, CCW seen from +Z, with a FIXED vertex count of
    2*nx + 2*ny + 4*nc + 2*len(cuts_x).

    The fixed count is what makes an inset trivial: `rrect(cx, cy, w-2d, h-2d, r-d, ...)`
    has a 1:1 vertex correspondence with the original, so a ring bridge between them is
    pure quads with no bridging heuristics involved.

    `cuts_x` inserts an extra vertex at an ABSOLUTE board x on each of the two LONG runs,
    which is how the side relief puts a crisp edge where oak's cross battens meet the rim
    without densifying the whole silhouette.  It survives an inset for a reason worth
    writing down: the run spans `x0 + r` to `x1 - r` = `-(W/2 - d) + (R - d)`, and the two
    d terms cancel, so BOTH ENDS OF EVERY STRAIGHT RUN ARE INDEPENDENT OF THE INSET.  The
    uniform samples therefore sit at the same absolute coordinates at every inset, a cut at
    an absolute x keeps its parameter exactly, and a per-vertex mask indexed by position is
    valid for the whole ring stack.  `assert_side_cuts()` measures that claim rather than
    trusting it."""
    r = max(r, 0.0025)   # never collapse a corner to a point: an inset past the radius
                         # mitred bronze's cast bead into a visible diagonal crease
    r = min(r, w / 2 - 1e-5, h / 2 - 1e-5)
    x0, x1 = cx - w / 2, cx + w / 2
    y0, y1 = cy - h / 2, cy + h / 2
    pts = []

    def arc(ax, ay, a0, a1):
        for i in range(nc):
            t = a0 + (a1 - a0) * (i + 0.5) / nc
            pts.append((ax + r * math.cos(t), ay + r * math.sin(t)))

    def run(px, py, qx, qy, n, xcuts=()):
        ts = [i / n for i in range(n)]
        if xcuts and abs(qx - px) > 1e-9:
            for xc in xcuts:
                t = (xc - px) / (qx - px)
                assert 1e-6 < t < 1.0 - 1e-6, (
                    "side cut at x=%.5f falls outside the straight run %.5f..%.5f -- a cut "
                    "that leaves the run changes the vertex count with the inset" % (xc, px, qx))
                ts.append(t)
            ts.sort()
        for t in ts:
            pts.append((px + (qx - px) * t, py + (qy - py) * t))

    run(x0 + r, y0, x1 - r, y0, nx, cuts_x)               # bottom
    arc(x1 - r, y0 + r, -math.pi / 2, 0.0)                # BR
    run(x1, y0 + r, x1, y1 - r, ny)                       # right
    arc(x1 - r, y1 - r, 0.0, math.pi / 2)                 # TR
    run(x1 - r, y1, x0 + r, y1, nx, cuts_x)               # top
    arc(x0 + r, y1 - r, math.pi / 2, math.pi)             # TL
    run(x0, y1 - r, x0, y0 + r, ny)                       # left
    arc(x0 + r, y0 + r, math.pi, 1.5 * math.pi)           # BL
    return pts


def assert_side_cuts(sil, cuts, min_gap=0.0006):
    """The two claims rrect's cuts rest on, MEASURED on the insets this build uses.

    (1) A ring at any inset has the same vertex count, and (2) every straight-run vertex
    sits at the same absolute coordinate at every inset, so a per-index mask taken off
    sil(0.0) means the same board point on every ring.  A silent violation of either would
    not crash -- it would shear the mask one vertex sideways on some rings, which is a
    batten with a staircase edge, and nothing downstream would report it.

    The third check is the one a count-stability proof does not cover: two vertices closer
    than `min_gap` make a sliver quad with a sliver of arclength UV, which is the failure
    mode strip_uv's own docstring records from the first atlas dump."""
    if not cuts:
        return
    base = sil(0.0)
    n = len(base)
    for d in (0.0005, 0.0010, 0.0018, 0.0020, 0.0030):
        ring = sil(d)
        assert len(ring) == n, ("silhouette vertex count %d at inset %.4f but %d at 0 -- a "
                                "cut left its run" % (len(ring), d, n))
        for i in range(n):
            if abs(base[i][1]) > SHORT / 2 - 1e-6 or abs(base[i][0]) > LONG / 2 - 1e-6:
                assert abs(ring[i][0] - base[i][0]) < 1e-9 or \
                       abs(ring[i][1] - base[i][1]) < 1e-9, \
                    "straight-run vertex %d moved in BOTH axes between insets 0 and %.4f" % (i, d)
    worst, worst_i = 1e9, -1
    for i in range(n):
        j = (i + 1) % n
        g = math.hypot(base[j][0] - base[i][0], base[j][1] - base[i][1])
        if g < worst:
            worst, worst_i = g, i
    assert worst >= min_gap, (
        "silhouette vertices %d and %d are %.3f mm apart (limit %.3f): a cut landed on top "
        "of a uniform sample and the side wall gets a sliver quad"
        % (worst_i, (worst_i + 1) % n, worst * 1000.0, min_gap * 1000.0))
    return worst


def circle(cx, cy, r, n):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n))
            for i in range(n)]


def rr_gen(cx, cy, w, h, r, nc, nx, ny):
    """Return f(d) -> outline inset by d, vertex-count-stable."""
    return lambda d: rrect(cx, cy, w - 2 * d, h - 2 * d, r - d, nc, nx, ny)


def circ_gen(cx, cy, r, n):
    return lambda d: circle(cx, cy, r - d, n)


def fillet(w, d, n):
    """Quarter-ellipse edge profile as a list of (delta_inset, delta_z) steps, z going DOWN."""
    out, pi, pz = [], 0.0, 0.0
    for i in range(1, n + 1):
        t = i / n * math.pi / 2
        ins = w * (1.0 - math.cos(t))
        dz = -d * math.sin(t)
        out.append((ins - pi, dz - pz))
        pi, pz = ins, dz
    return out


def bead(w, h):
    """Raised ornamental bead: up and over, then back down to the starting level, consuming
    exactly `w` of inset in total.

    The first version's four steps summed to 2*w.  On bronze that overshot the frame band's
    inner ring by 10 mm, so the profile walked PAST it and the field chamfer then expanded
    back outwards -- a fold in a top-down UV projection, which the atlas check caught as
    15 163 overlapping texels.  Keep the four fractions summing to 1.0."""
    return [(w * 0.22, h * 0.8), (w * 0.28, h * 0.2), (w * 0.28, -h * 0.2), (w * 0.22, -h * 0.8)]


# ----------------------------------------------------------------------- the builder --


class Island:
    """One UV island.  `kind` is 'planar' (top-down x/y in metres) or 'strip' (arclength
    across / depth down, in metres).  `weight` is the relative texel density -- 0.3 on the
    back cap says "this face is never seen, give it a third of the pixels"."""

    __slots__ = ("kind", "category", "faces", "uv", "weight", "flip_y", "bbox", "xform",
                 "hole")

    def __init__(self, kind, category, weight=1.0, flip_y=False):
        self.kind = kind
        self.category = category
        self.faces = []
        self.uv = {}            # strip only: (BMFace, BMVert) -> (u, v).  Keyed by the
                                # LOOP, not the vertex: a wrapped strip needs two
                                # different u at the column where its rows split.
        self.weight = weight
        self.flip_y = flip_y
        self.bbox = None
        self.xform = None       # (scale, u_off, v_off) once packed
        self.hole = None        # RAW-unit free rectangle inside this island (a ring's
                                # middle), where the category's other islands are packed

    def raw(self, face, vert):
        if self.kind == "planar":
            return (vert.co.x, -vert.co.y if self.flip_y else vert.co.y)
        return self.uv[(face, vert)]


class Board:
    def __init__(self, style, cfg):
        self.style = style
        self.cfg = cfg
        self.bm = bmesh.new()
        self.islands = []
        self.anchors = {}       # name -> (x, y, z)
        self.symbols = []       # dicts for the uv json
        self.face_island = {}   # BMFace -> Island
        self.metrics = {}       # feature sizes, reported so the cap tuning can be dialled

    # -------------------------------------------------------------- low-level helpers --

    def ring(self, pts, z):
        """Create a closed vertex loop with its edges."""
        vs = [self.bm.verts.new((p[0], p[1], z)) for p in pts]
        for i in range(len(vs)):
            a, b = vs[i], vs[(i + 1) % len(vs)]
            if self.bm.edges.get((a, b)) is None:
                self.bm.edges.new((a, b))
        return vs

    def bridge(self, a, b, island):
        """Quad ring between two equal-length loops."""
        n = len(a)
        assert len(b) == n
        for i in range(n):
            j = (i + 1) % n
            try:
                f = self.bm.faces.new((a[i], a[j], b[j], b[i]))
            except ValueError:
                continue        # duplicate face (degenerate zero-height step) -- skip
            island.faces.append(f)
            self.face_island[f] = island

    def fill(self, boundary_loops, island):
        """Constrained fill of a planar region: loops[0] is the outer boundary, the rest
        are holes.  Triangulated, then joined back into quads where possible."""
        edges = []
        for loop in boundary_loops:
            for i in range(len(loop)):
                e = self.bm.edges.get((loop[i], loop[(i + 1) % len(loop)]))
                if e is not None:
                    edges.append(e)
        res = bmesh.ops.triangle_fill(self.bm, use_beauty=True, use_dissolve=False,
                                      edges=edges, normal=Vector((0.0, 0.0, 1.0)))
        made = [g for g in res["geom"] if isinstance(g, bmesh.types.BMFace)]
        jr = bmesh.ops.join_triangles(self.bm, faces=made,
                                      angle_face_threshold=3.14, angle_shape_threshold=3.14,
                                      cmp_seam=False, cmp_sharp=False,
                                      cmp_uvs=False, cmp_materials=False)
        faces = [f for f in made if f.is_valid] + [f for f in jr["faces"] if f.is_valid]
        out, seen = [], set()
        for f in faces:
            if f in seen or not f.is_valid:
                continue
            seen.add(f)
            out.append(f)
            island.faces.append(f)
            self.face_island[f] = island
        return out

    def new_island(self, kind, category, weight=1.0, flip_y=False):
        isl = Island(kind, category, weight, flip_y)
        self.islands.append(isl)
        return isl

    def strip_uv(self, island, loops, chunk=0.150, gap=0.012):
        """Arclength/depth UVs for a stack of corresponding loops, wrapped into rows of
        `chunk` metres so a 1.9 m long, 6 mm tall wall packs as a compact block instead of a
        hairline island nothing can allocate pixels to.

        The row is chosen PER FACE, not per vertex.  Per vertex, the one quad that straddles
        a row boundary got u = row_len on one side and u = 0 on the other, so it was stretched
        as a diagonal right across the whole block -- clearly visible as a zig-zag ribbon in
        the first atlas dump, and a handful of smeared faces on every recess wall."""
        base = loops[0]
        n = len(base)
        arc = [0.0]
        for i in range(n):
            arc.append(arc[-1] + (base[(i + 1) % n].co - base[i].co).length)
        total = arc[-1]
        depth = [0.0]
        for k in range(1, len(loops)):
            dd = sum((loops[k][i].co - loops[k - 1][i].co).length for i in range(n)) / n
            depth.append(depth[-1] + dd)
        height = depth[-1] if depth[-1] > 1e-6 else 1e-6
        # Each wrapped row is its own UV island, so the gap between rows has to clear the
        # contract's 8 px at 2048 in the SMALLEST-scaled region this strip can land in.
        # Measured: 4 mm of row gap came out at 6 px in slot_floor.  12 mm clears it, and a
        # strip that is taller than that gets a proportional gap instead.
        gap = max(gap, height * 1.2)
        rows = max(1, int(math.ceil(total / chunk)))
        row_len = total / rows

        pos = {}
        for k, loop in enumerate(loops):
            for i, v in enumerate(loop):
                pos[v] = (k, i)
        for f in island.faces:
            cols = set(pos[v][1] for v in f.verts)
            wrap = (0 in cols and (n - 1) in cols and n > 2)
            col = (n - 1) if wrap else min(cols)
            r = min(rows - 1, int(arc[col] / row_len)) if row_len > 0 else 0
            for lp in f.loops:
                k, i = pos[lp.vert]
                u = total if (wrap and i == 0) else arc[i]
                island.uv[(f, lp.vert)] = (u - r * row_len, depth[k] + r * (height + gap))
        return rows, total, height

    # ------------------------------------------------------------------- feature build --

    def profile_stack(self, gen, z0, steps, category, weight=1.0, strip=True,
                      island=None, chunk=0.150):
        """Walk a ring stack: for each (delta_inset, delta_z) create the next loop and
        bridge it to the previous.  Returns (loops, final_inset, final_z)."""
        if island is None:
            island = self.new_island("strip" if strip else "planar", category, weight)
        d, z = 0.0, z0
        loops = [self.ring(gen(0.0), z)]
        for (di, dz) in steps:
            assert di >= -1e-12, "a ring stack must never step OUTWARD: top-down UV folds"
            if abs(di) < 1e-7 and abs(dz) < 1e-7:
                continue
            d += di
            z += dz
            nxt = self.ring(gen(d), z)
            self.bridge(loops[-1], nxt, island)
            loops.append(nxt)
        if strip:
            self.strip_uv(island, loops, chunk=chunk)
        return island, loops, d, z

    def recess(self, gen, z0, depth, category, holes=None, small=False, no_bead=False,
               weight=1.0, chunk=0.150):
        """A pocket cut into a surface at z0.  Returns (top_loop, floor_loops, floor_z)."""
        c = self.cfg
        steps = []
        if c["bead"] and not no_bead:
            bw, bh = c["bead"]
            if small:
                bw, bh = bw * 0.7, bh * 0.7
            steps += bead(bw, bh)
        fw, fd, fn = c["small_fillet"] if small else c["fillet"]
        steps += fillet(fw, fd, fn)
        used = fd
        if c["ledge"] and not small:
            lw, lz = c["ledge"]
            steps.append((0.0, -lz))            # straight wall
            steps.append((lw, 0.0))             # machined counterbore step
            used += lz
        rest = depth - used
        if rest > 1e-5:
            steps.append((0.0, -rest))
        isl, loops, d, z = self.profile_stack(gen, z0, steps, category, weight=weight,
                                              strip=True, chunk=chunk)
        return loops[0], loops[-1], z, d, isl

    def raised(self, gen, z0, category, weight=1.0, h_scale=1.0):
        """A raised ornament WELDED into the surface at z0: its footprint is a hole in that
        surface's fill, so nothing interpenetrates and there is no hidden bottom cap to
        unwrap.  Returns (base_loop, cap_loop, cap_z); the caller fills the cap, optionally
        with the footprints of smaller ornaments sitting ON it as holes."""
        c = self.cfg
        h = c["orn_h"] * h_scale
        bv, n = min(c["orn_bevel"], h * 0.75), c["orn_seg"]
        steps = [(0.0, h - bv)] if h > bv else []
        for (di, dz) in fillet(bv, bv, n):
            steps.append((di, -dz))             # invert: this profile climbs
        isl, loops, d, z = self.profile_stack(gen, z0, steps, category,
                                              weight=weight, strip=True, chunk=0.100)
        return loops[0], loops[-1], z

    # ------------------------------------------------------------------- back relief --

    # --------------------------------------------------------------- the side relief --

    def side_mask(self, pts, art):
        """Per-vertex weight, 1.0 exactly where a BACK feature crosses the rim.

        A masked vertex is held at the NOMINAL 0.640 x 0.320 silhouette while its
        neighbours are cut back, so the feature reads as proud without a single vertex
        leaving the contract footprint.  Indexed by position taken off `sil(0.0)`, which is
        legitimate because rrect's straight runs span the same absolute coordinates at
        every inset (see rrect's docstring) -- so index i means the same board point on
        every ring of the stack."""
        ramp = art["cross_ramp"]
        out = []
        for (x, y) in pts:
            v = 0.0
            if abs(y) > SHORT / 2 - 1e-6:       # the two LONG straight runs only
                for (x0, x1) in art["cross_x"]:
                    if x0 <= x <= x1:
                        v = 1.0
                    elif x0 - ramp <= x < x0:
                        v = max(v, (x - (x0 - ramp)) / ramp)
                    elif x1 < x <= x1 + ramp:
                        v = max(v, ((x1 + ramp) - x) / ramp)
            out.append(v)
        return out

    def side_stack(self, sil, art, widest, z0, band_h, island):
        """Walk SIDE_ART's profile from `widest` down to the last ring before the back
        chamfer.  Returns (loops, last_loop, last_inset).

        Pure ring bridges: the profile never changes the vertex count, so the whole side
        stays quads and the strip UV stays one island.  Two asserts carry the design:
        every step is checked against MAX_RIM_TILT_DEG so gen_geobuf keeps calling these
        faces RIM, and the profile's total drop is checked against the band's real height
        so a mistuned table fails the build instead of moving the back plate."""
        mask = self.side_mask(sil(0.0), art)
        assert abs(sum(dz for (_, dz, _) in art["profile"]) + band_h) < 1e-9, (
            "side profile drops %.5f m but the band between the front roll-over and the "
            "back chamfer is %.5f m" % (-sum(dz for (_, dz, _) in art["profile"]), band_h))
        loops, d, z = [widest], 0.0, z0
        worst = 0.0
        for (di, dz, mod) in art["profile"]:
            tilt = math.degrees(math.atan2(abs(di), abs(dz))) if abs(dz) > 1e-12 else 90.0
            assert tilt <= MAX_RIM_TILT_DEG + 1e-9, (
                "side step at %.1f deg off vertical exceeds %.1f: gen_geobuf classifies a "
                "rim triangle only within acos(0.5) = 60 deg of the board plane, and a "
                "steeper step lands in FRONT (never painted by the front camera) or BACK "
                "(painted with the back PLATE art at its own board coordinates)"
                % (tilt, MAX_RIM_TILT_DEG))
            worst = max(worst, tilt)
            d += di
            z += dz
            if mod is not None and any(mask):
                # `mod` is the inset the MASKED vertices take -- 0.0 means "hold this
                # vertex at the nominal 0.640 x 0.320 silhouette while its neighbours are
                # cut back", which is how a feature reads as proud without one vertex
                # leaving the contract footprint.
                a, b = sil(mod), sil(d)
                pts = [(a[i][0] * mask[i] + b[i][0] * (1.0 - mask[i]),
                        a[i][1] * mask[i] + b[i][1] * (1.0 - mask[i]))
                       for i in range(len(a))]
            else:
                pts = sil(d)
            nxt = self.ring(pts, z)
            self.bridge(loops[-1], nxt, island)
            loops.append(nxt)
        self.side_metrics = {
            "rings": len(art["profile"]),
            "rebate_mm": round(max(abs(sum(di for (di, _, _) in art["profile"][:k + 1]))
                                   for k in range(len(art["profile"]))) * 1000.0, 2),
            "worst_step_deg": round(worst, 2),
            "cross_features": len(art["cross_x"]),
            "cross_texels_flush": int(sum(1 for v in mask if v > 0.999)),
            "band_mm": round(band_h * 1000.0, 2),
        }
        return loops, loops[-1], d

    def back_stack(self, gen, z0, height, run, seg, island, into=False):
        """One STRAIGHT-chamfered feature stepped out of (or into) the back plate.

        Returns (base_loop, cap_loop, cap_z).  The base loop is meant to be handed to the
        host surface's fill() as a hole and the cap loop to be filled in turn, so the
        feature is WELDED in: nothing interpenetrates, there is no hidden underside, and the
        whole back stays a single closed shell whose top-down projection tiles exactly once.

        `into=False` steps AWAY from the board (out the back, z decreasing); `into=True`
        steps into it.  The slope assert is the load-bearing part -- see MAX_BACK_SLOPE_DEG.
        """
        slope = math.degrees(math.atan2(height, run))
        assert slope <= MAX_BACK_SLOPE_DEG + 1e-9, (
            "back relief wall at %.1f deg exceeds %.1f: gen_geobuf would classify it "
            "INTERIOR instead of BACK (tex_backfill never paints INTERIOR) and its "
            "top-down UV footprint would collapse" % (slope, MAX_BACK_SLOPE_DEG))
        sgn = 1.0 if into else -1.0
        d, z = 0.0, z0
        loops = [self.ring(gen(0.0), z)]
        for _ in range(seg):
            d += run / seg
            z += sgn * height / seg
            nxt = self.ring(gen(d), z)
            self.bridge(loops[-1], nxt, island)
            loops.append(nxt)
        self.back_slope_deg = max(getattr(self, "back_slope_deg", 0.0), slope)
        return loops[0], loops[-1], z

    def back_rect(self, art, z0, island, key_h, key_run, key_seg, key_xy, rect, into=False):
        """A rounded-rect strap / panel from a (x0_px, x1_px, y0_px, y1_px, r) art figure."""
        x0, x1, y0, y1, r = rect
        cx = (back_x(x0) + back_x(x1)) * 0.5
        cy = (back_y(y0) + back_y(y1)) * 0.5
        g = rr_gen(cx, cy, back_m(x1 - x0), back_m(y1 - y0), r, *key_xy)
        return self.back_stack(g, z0, art[key_h], art[key_run], art[key_seg], island,
                               into=into)

    def back_relief(self, plate_ring, z0, island):
        """Build the style's back relief and CLOSE the plate with fill().

        The relief and the plate share one planar top-down projection, so the back is still
        exactly ONE UV island, its raw bounding box is unchanged, and the atlas packing of
        every other region comes out bit-identical.  That is deliberate: it means this
        round does not repack the atlas, and the FRONT / RIM / INTERIOR texels of the nine
        shipped maps keep the values ModBuild 276 wrote into them.
        """
        art = BACK_ART.get(self.style)
        self.back_slope_deg = 0.0
        if art is None:
            self.fill([plate_ring], island)
            return
        holes, caps, feats = [], 0, {}

        if self.style == "oak":
            for k, rect in enumerate(art["straps"]):
                base, cap, cz = self.back_rect(art, z0, island, "strap_h", "strap_run",
                                               "strap_seg", art["strap_seg_xy"], rect)
                holes.append(base)
                nail_holes = []
                for (nx, ny) in art["nails"]:
                    if not (rect[0] <= nx <= rect[1]):
                        continue
                    g = rr_gen(back_x(nx), back_y(ny), art["nail_w"], art["nail_w"],
                               art["nail_r"], *art["nail_seg_xy"])
                    nb, nc, ncz = self.back_stack(g, cz, art["nail_h"], art["nail_run"],
                                                  art["nail_seg"], island)
                    nail_holes.append(nb)
                    self.fill([nc], island)
                    caps += 1
                self.fill([cap] + nail_holes, island)
                caps += 1
            feats = {"straps": len(art["straps"]), "nails": len(art["nails"]),
                     "seams_in_normal_map": len(art["seam_rows"])}

        elif self.style == "steel":
            rects = []
            for rect in art["fields"]:
                ptop, pfloor, pz = self.back_rect(art, z0, island, "panel_d", "panel_run",
                                                  "panel_seg", art["panel_seg_xy"], rect,
                                                  into=True)
                holes.append(ptop)
                self.fill([pfloor], island)
                caps += 1
                x0, x1, y0, y1, _ = rect
                rects.append((back_x(x0), back_y(y1), back_x(x1), back_y(y0)))
            moved, worst = 0, 0.0
            for (rx, ry) in art["rivets_band"] + art["rivets_strap"]:
                cx, cy = back_x(rx), back_y(ry)
                for r in rects:
                    cx, cy, d = self.push_clear(cx, cy, art["rivet_a"], art["rivet_b"],
                                                r, 0.0005)
                    if d > 0:
                        moved += 1
                        worst = max(worst, d)
                        print("[gen_board]        rivet at plate col %.1f row %.1f pushed "
                              "%.2f mm clear of a recessed field" % (rx, ry, d * 1000.0))
                holes.append(self._back_rivet(art, cx, cy, z0, island))
            feats = {"cast_panels": len(art["fields"]),
                     "rivets": len(art["rivets_band"]) + len(art["rivets_strap"]),
                     "rivets_clamped": moved, "worst_clamp_mm": round(worst * 1000.0, 2)}

        else:   # bronze
            for rect in art["panels"]:
                ptop, pfloor, pz = self.back_rect(art, z0, island, "panel_d", "panel_run",
                                                  "panel_seg", art["panel_seg_xy"], rect,
                                                  into=True)
                holes.append(ptop)
                self.fill([pfloor], island)
                caps += 1
            feats = {"cast_panels": len(art["panels"])}

        self.fill([plate_ring] + holes, island)
        self.back_metrics = dict(feats, caps=caps + 1,
                                 max_wall_deg=round(self.back_slope_deg, 2))

    @staticmethod
    def push_clear(cx, cy, ax, ay, rect, gap):
        """Push (cx, cy) until an ax x ay footprint clears `rect` (x0, y0, x1, y1) by `gap`.

        The steel border rivets are measured off the art, and four of them are painted
        straddling the line where the inner panel begins: the corner rivets at plate columns
        40.9 / 2005.6 / 39.6 / 2006.9 overlap the panel outline by 0.17 to 0.73 mm.  A
        footprint that crosses the boundary of the surface it is welded into is not a hole
        in that surface, and bmesh's constrained fill produced exactly what that predicts --
        2 hole loops, 10 boundary edges and 2 non-manifold edges on steel.

        This moves the rivet along whichever axis needs the least travel, and gen_board
        PRINTS every move it makes.  A silent clamp would be a lie about where the art is;
        a printed one is a measurement of how far the mesh had to disagree with it.
        """
        x0, y0, x1, y1 = rect
        bx0, bx1 = cx - ax - gap, cx + ax + gap
        by0, by1 = cy - ay - gap, cy + ay + gap
        if bx1 <= x0 or bx0 >= x1 or by1 <= y0 or by0 >= y1:
            return cx, cy, 0.0
        opts = [(x0 - bx1, 0.0), (x1 - bx0, 0.0), (0.0, y0 - by1), (0.0, y1 - by0)]
        dx, dy = min(opts, key=lambda d: abs(d[0]) + abs(d[1]))
        return cx + dx, cy + dy, math.hypot(dx, dy)

    def _back_rivet(self, art, cx, cy, z0, island):
        g = ell_gen(cx, cy, art["rivet_a"], art["rivet_b"], art["rivet_n"])
        base, cap, cz = self.back_stack(g, z0, art["rivet_h"], art["rivet_run"],
                                        art["rivet_seg"], island)
        self.fill([cap], island)
        return base

    # ------------------------------------------------------------------------- assemble --

    def build(self):
        c = self.cfg
        nc, nx, ny = c["seg"]
        ew, ed, en = c["edge"]
        fw, fr = c["frame_w"], c["frame_r"]
        T = c["thickness"]

        # ------------------------------------------------------------------ silhouette --
        # sil(d) is the board outline inset by d; the vertex count is constant, so any two
        # of these bridge to a pure quad ring.
        # The cuts are the batten edges of SIDE_ART, so the crisp edge where a back feature
        # crosses the rim costs four vertices per run instead of the ~7x silhouette
        # densification a uniform sampling would need to place a 43 mm feature to 1 mm.
        art_s = SIDE_ART[self.style]
        cuts = tuple(x for (x0, x1) in art_s["cross_x"]
                     for x in (x0 - art_s["cross_ramp"], x0, x1, x1 + art_s["cross_ramp"]))
        sil = lambda d: rrect(0, 0, LONG - 2 * d, SHORT - 2 * d, c["corner_r"] - d,
                              nc, nx, ny, cuts)
        assert_side_cuts(sil, cuts)

        # The TOP EDGE ROLL-OVER lives in the `frame` island with a plain top-down
        # projection, so it is UV-continuous with the frame band and the board's most
        # visible silhouette edge carries no seam.
        frame_isl = self.new_island("planar", "frame", 1.0)
        top_ring = self.ring(sil(ew), 0.0)
        prev, d, z = top_ring, ew, 0.0
        for (di, dz) in fillet(ew, ed, en):
            d -= di
            z += dz
            nxt = self.ring(sil(d), z)
            self.bridge(prev, nxt, frame_isl)
            prev = nxt
        widest = prev                                    # the true 0.640 x 0.320 ring

        # ------------------------------------------------------------- side wall + back --
        side_isl = self.new_island("strip", "sides", 1.0)
        side_loops, low, low_d = self.side_stack(sil, art_s, widest, -ed, T - 2 * ed, side_isl)
        assert low_d < 0.0018 + 1e-9, (
            "the side profile ends %.4f mm inside the silhouette, past the back chamfer's "
            "own 1.8 mm -- the chamfer would step OUTWARD and fold the back's top-down UV"
            % (low_d * 1000.0))
        bot = self.ring(sil(0.0018), -T)
        self.bridge(low, bot, side_isl)
        self.strip_uv(side_isl, side_loops + [bot], chunk=0.32)
        # The weight stays 0.30 and it no longer means what its docstring says.  The back's
        # island is REPACKED afterwards by gen_backuv.py into a free rectangle of the atlas
        # (1.21 tex/mm, ModBuild 276), so this number only decides how much of the `sides`
        # region the side-wall strip gets to keep -- it is not the back's shipped density.
        # Leaving it alone is what keeps the packing of every other region bit-identical.
        back = self.new_island("planar", "sides", 0.30, flip_y=True)
        self.back_relief(bot, -T, back)

        # --------------------------------------------------------------- the frame band --
        # Outermost is a FLAT annulus at z = 0 -- that is where the style's ornaments are
        # welded in, as holes in its fill.  Whatever profile the style wants (a machined
        # step, a cast bead) comes AFTER it, on the way down to the field.
        flat_w, post = self.frame_profile(fw)
        mid_gen = lambda dd: rrect(0, 0, LONG - 2 * (ew + flat_w) - 2 * dd,
                                   SHORT - 2 * (ew + flat_w) - 2 * dd,
                                   max(c["corner_r"] - ew - flat_w, 0.0012) - dd, nc, nx, ny)
        mid_ring = self.ring(mid_gen(0.0), 0.0)
        # (top_ring -> mid_ring is NOT bridged: it is filled, with the ornaments as holes)

        fin_gen = lambda dd: rrect(0, 0, LONG - 2 * (ew + fw) - 2 * dd,
                                   SHORT - 2 * (ew + fw) - 2 * dd, fr - dd, nc, nx, ny)
        assert abs(sum(di for di, _ in post) - (fw - flat_w)) < 1e-9, (
            "frame profile insets %.5f but the band has %.5f to give -- a profile that walks "
            "past the band inner ring folds the top-down UV projection back on itself"
            % (sum(di for di, _ in post), fw - flat_w))
        prev, dd, zz = mid_ring, 0.0, 0.0
        for (di, dz) in post:
            dd += di
            zz += dz
            nxt = self.ring(fin_gen(0.0) if abs(dd - (fw - flat_w)) < 1e-6
                            else mid_gen(dd), zz)
            self.bridge(prev, nxt, frame_isl)
            prev = nxt
        frame_in = prev if post else mid_ring

        # ------------------------------------------------------- frame -> field chamfer --
        face_isl = self.new_island("planar", "face", 1.0)
        cw, cd, cn = c["field_chamfer"]
        d, z, prev = 0.0, zz, frame_in
        for (di, dz) in fillet(cw, cd, cn):
            d += di
            z += dz
            nxt = self.ring(fin_gen(d), z)
            self.bridge(prev, nxt, face_isl)
            prev = nxt
        drop = c["field_drop"] - cd
        if drop > 1e-5:
            nxt = self.ring(fin_gen(d), z - drop)
            self.bridge(prev, nxt, face_isl)
            prev = nxt
            z -= drop
        field_loop, field_z = prev, z
        hx = (LONG / 2) - ew - fw - d
        hy = (SHORT / 2) - ew - fw - d

        # ---------------------------------------------------------------------- features --
        m = 0.005                                   # field margin around a sub-panel
        panel_w = 0.114
        panel_h = 2 * hy - 2 * m
        panel_x = hx - m - panel_w / 2
        gutter = 0.010
        card_zone = 2 * (hx - m - panel_w - gutter)
        centre_gap = 0.016
        card_w = (card_zone - centre_gap) / 2
        card_h = min(panel_h, card_w * 1.5)
        slot_x = card_w / 2 + centre_gap / 2

        seg_card = (6, 14, 20)
        seg_panel = (6, 10, 22)
        seg_seat = (5, 8, 7)
        r_card = {"oak": 0.011, "steel": 0.005, "bronze": 0.016}[self.style]
        r_panel = {"oak": 0.010, "steel": 0.005, "bronze": 0.015}[self.style]
        r_seat = {"oak": 0.008, "steel": 0.004, "bronze": 0.011}[self.style]

        holes = []

        # -- two card recesses --------------------------------------------------------
        for sgn, tag in ((-1, 1), (1, 2)):
            g = rr_gen(sgn * slot_x, 0.0, card_w, card_h, r_card, *seg_card)
            top, floor, fz, dtot, _ = self.recess(g, field_z, c["depth_card"], "slot_floor",
                                                  chunk=0.14)
            holes.append(top)
            fl = self.new_island("planar", "slot_floor", 1.0)
            self.fill([floor], fl)
            self.anchors["Slot%d" % tag] = (sgn * slot_x, 0.0, fz)
            self.symbols.append({"name": "slot_rose_%d" % tag, "region": "slot_floor",
                                 "pt": (sgn * slot_x, 0.0), "island": fl,
                                 "size_m": min(card_w, card_h) * 0.62})

        # -- left rest panel with two round pads ---------------------------------------
        gp = rr_gen(-panel_x, 0.0, panel_w, panel_h, r_panel, *seg_panel)
        ptop, pfloor, pz, pdt, _ = self.recess(gp, field_z, c["depth_panel"], "rest_pads",
                                               chunk=0.14)
        holes.append(ptop)
        pm = 0.006
        usable_w = panel_w - 2 * pdt - 2 * pm
        usable_h = panel_h - 2 * pdt - 2 * pm
        pad_r = min(usable_w * INNER_FILL, usable_h / 2 - 0.008) / 2
        pad_holes = []
        for k, nm in enumerate(("LongRestToken", "ShortRestToken")):   # k=0 lower, k=1 upper
            cy = -usable_h / 2 + (usable_h / 2) * (k + 0.5)
            g = circ_gen(-panel_x, cy, pad_r, 40)
            top, floor, fz, _, _ = self.recess(g, pz, c["depth_pad"], "rest_pads",
                                               small=True, chunk=0.10)
            pad_holes.append(top)
            fl = self.new_island("planar", "rest_pads", 1.1)
            self.fill([floor], fl)
            self.anchors[nm] = (-panel_x, cy, fz)
            self.symbols.append({"name": "rest_glyph_%s" % ("long" if k == 0 else "short"),
                                 "region": "rest_pads", "pt": (-panel_x, cy), "island": fl,
                                 "size_m": pad_r * 1.5})
        usable_h_pads = usable_h / 2
        pfl = self.new_island("planar", "rest_pads", 0.9)
        self.fill([pfloor] + pad_holes, pfl)

        # -- right button console with THREE seats -------------------------------------
        # Same sub-panel, same depth, same edge profile as the rest panel on the left, and
        # the three seats are cut into its floor exactly the way the two pads are cut into
        # the left one.  That is what makes them read as designed in.
        gc = rr_gen(panel_x, 0.0, panel_w, panel_h, r_panel, *seg_panel)
        ctop, cfloor, cz, cdt, _ = self.recess(gc, field_z, c["depth_panel"], "button_seats",
                                               chunk=0.14)
        holes.append(ctop)
        usable_w = panel_w - 2 * cdt - 2 * pm
        usable_h = panel_h - 2 * cdt - 2 * pm
        seat_pitch = usable_h / 3
        seat_h = seat_pitch - 0.0078
        seat_w = min(usable_w * INNER_FILL, seat_h * 1.15)
        seat_holes = []
        for k in range(3):
            cy = usable_h / 2 - seat_pitch * (k + 0.5)                # k = 0 is the TOP seat
            g = rr_gen(panel_x, cy, seat_w, seat_h, r_seat, *seg_seat)
            top, floor, fz, _, _ = self.recess(g, cz, c["depth_seat"], "button_seats",
                                               small=True, chunk=0.10)
            seat_holes.append(top)
            fl = self.new_island("planar", "button_seats", 1.1)
            self.fill([floor], fl)
            self.anchors["ButtonSeat%d" % (k + 1)] = (panel_x, cy, fz)
            self.symbols.append({"name": "seat_glyph_%d" % (k + 1), "region": "button_seats",
                                 "pt": (panel_x, cy), "island": fl,
                                 "size_m": min(seat_w, seat_h) * 0.66})
        cfl = self.new_island("planar", "button_seats", 0.9)
        self.fill([cfloor] + seat_holes, cfl)

        # Legacy spellings ship as SECONDARY empties at the identical positions, so one FBX
        # assembles against every DLL: BoardAnchors' alias table is
        # ButtonSeat1|ConfirmButton, ButtonSeat2|UndoButton, ButtonSeat3|SkipButton, and the
        # pre-28ce64c0 readers that only know ConfirmButton/UndoButton still find their two.
        self.anchors["ConfirmButton"] = self.anchors["ButtonSeat1"]
        self.anchors["UndoButton"] = self.anchors["ButtonSeat2"]
        self.anchors["SkipButton"] = self.anchors["ButtonSeat3"]

        # -- ornaments welded into the flat frame annulus ------------------------------
        orn_holes = self.ornaments(ew, fw, flat_w)

        # -- the two big fills, last: every opening is known by now ---------------------
        self.fill([field_loop] + holes, face_isl)
        self.fill([top_ring, mid_ring] + orn_holes, frame_isl)
        # The free rectangle the ornament islands are packed into is bounded by the frame
        # island's INNERMOST ring, not by the flat annulus: on bronze the cast bead is part
        # of the same island and runs 10 mm further in than the annulus does, so a hole
        # measured off the annulus put forty ornaments on top of it (22 overlapping texels).
        frame_isl.hole = (-(LONG / 2 - ew - fw) + 0.005, -(SHORT / 2 - ew - fw) + 0.005,
                          (LONG / 2 - ew - fw) - 0.005, (SHORT / 2 - ew - fw) - 0.005)

        self.metrics = {
            "card_recess_m": [round(card_w, 4), round(card_h, 4)],
            "card_depth_m": c["depth_card"],
            "rest_pad_diameter_m": round(pad_r * 2, 4),
            "rest_pad_pitch_m": round(usable_h_pads, 4),
            "button_seat_m": [round(seat_w, 4), round(seat_h, 4)],
            "button_seat_pitch_m": round(seat_pitch, 4),
            "button_seat_depth_m": round(c["depth_panel"] + c["depth_seat"], 4),
            "field_half_m": [round(hx, 4), round(hy, 4)],
            "frame_band_w_m": round(fw, 4),
            "back_relief": getattr(self, "back_metrics", {}),
            "side_relief": getattr(self, "side_metrics", {}),
        }
        self.symbols.append({"name": "centre_rose", "region": "face",
                             "pt": (0.0, 0.0), "island": face_isl,
                             "size_m": centre_gap * 0.9})
        self.symbols.append({"name": "field_corner_ref", "region": "face",
                             "pt": (-hx + 0.004, hy - 0.004), "island": face_isl,
                             "size_m": 0.02})

    def frame_profile(self, fw):
        """(width of the FLAT outer annulus, steps from there down/in to the field edge)."""
        if self.style == "bronze":
            bw, bh = 0.0100, 0.0034          # cast ornamental bead on the inner half
            return fw - bw, bead(bw, bh)
        if self.style == "steel":
            lip = 0.0042                     # machined inner lip, 0.9 mm proud
            return fw - lip, [(0.0, -0.0009), (lip, 0.0)]
        return fw, []                        # oak: one flat timber band

    def ornaments(self, ew, fw, flat_w):
        """Per-style trim.  Every piece is WELDED into the flat frame annulus (its footprint
        is a hole in that annulus' fill), and every stud sits ON its plate the same way, so
        no two footprints overlap and there is no interpenetration anywhere on the board."""
        c = self.cfg
        holes = []

        o_out_x = LONG / 2 - ew                      # flat annulus, outer edge
        o_out_y = SHORT / 2 - ew
        o_in_x = LONG / 2 - ew - flat_w              # flat annulus, inner edge
        o_in_y = SHORT / 2 - ew - flat_w
        bx = (o_out_x + o_in_x) / 2                  # band centreline
        by = (o_out_y + o_in_y) / 2
        bwid = flat_w - 0.0055                       # leave a ~2.75 mm lip either side
        cxr = LONG / 2 - c["corner_r"]               # corner arc centre
        cyr = SHORT / 2 - c["corner_r"]
        o_r = max(c["corner_r"] - ew, 0.0012)        # flat annulus outer corner radius

        # How far along a straight run an ornament of half-width bwid/2 may reach before the
        # corner arc curves in under it.  Computed, not guessed: this is what kept the oak
        # board's corner bars 16 mm outside the 0.640 m silhouette on the first run.
        dy = (by + bwid / 2) - cyr
        run_x = cxr + math.sqrt(max(o_r ** 2 - dy ** 2, 0.0)) - 0.0015
        dx = (bx + bwid / 2) - cxr
        run_y = cyr + math.sqrt(max(o_r ** 2 - dx ** 2, 0.0)) - 0.0015

        def plate(cx, cy, w, h, r, seg, studs=()):
            g = rr_gen(cx, cy, w, h, r, *seg)
            base, cap, capz = self.raised(g, 0.0, "frame", 1.0)
            holes.append(base)
            sub = []
            for (sx, sy, sr, sn) in studs:
                gs = circ_gen(sx, sy, sr, sn)
                sb, sc, scz = self.raised(gs, capz, "frame", 1.3, h_scale=0.55)
                sub.append(sb)
                isl = self.new_island("planar", "frame", 1.4)
                self.fill([sc], isl)
            isl = self.new_island("planar", "frame", 1.1)
            self.fill([cap] + sub, isl)

        def stud(cx, cy, r, n=12):
            g = circ_gen(cx, cy, r, n)
            base, cap, capz = self.raised(g, 0.0, "frame", 1.3, h_scale=0.62)
            holes.append(base)
            isl = self.new_island("planar", "frame", 1.4)
            self.fill([cap], isl)

        st = c["ornaments"]
        if st == "oak":
            # Iron corner brackets: a long bar wrapping the corner along the top/bottom run
            # plus a shorter bar down the side run, each with a forged peg -- and a pegged
            # centre strap top and bottom.  Timber board, iron hardware.
            lx, ly = 0.074, 0.052
            pr = min(0.0030, bwid / 2 - 0.0022)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    cxb = sx * (run_x - lx / 2)
                    plate(cxb, sy * by, lx, bwid, 0.0028, (3, 6, 2),
                          studs=[(sx * (run_x - 0.011), sy * by, pr, 10),
                                 (cxb - sx * (lx / 2 - 0.011), sy * by, pr, 10)])
                    run_y2 = by - bwid / 2 - 0.0018
                    cyb = sy * (run_y2 - ly / 2)
                    plate(sx * bx, cyb, bwid, ly, 0.0028, (3, 2, 5),
                          studs=[(sx * bx, cyb, pr, 10)])
            for sy in (-1, 1):
                plate(0.0, sy * by, 0.132, bwid, 0.0028, (3, 10, 2),
                      studs=[(-0.052, sy * by, pr, 10), (0.052, sy * by, pr, 10)])
        elif st == "steel":
            # Riveted plating: corner plates and a centre plate on every run, with a rivet
            # line marching down the long runs between them.
            rr = min(0.0026, bwid / 2 - 0.0018)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    cxb = sx * (run_x - 0.034)
                    plate(cxb, sy * by, 0.066, bwid, 0.0014, (2, 6, 2),
                          studs=[(sx * (run_x - 0.008), sy * by, rr, 10),
                                 (cxb - sx * 0.025, sy * by, rr, 10)])
                    run_y2 = by - bwid / 2 - 0.0016
                    cyb = sy * (run_y2 - 0.026)
                    plate(sx * bx, cyb, bwid, 0.050, 0.0014, (2, 2, 5),
                          studs=[(sx * bx, cyb, rr, 10)])
            for sy in (-1, 1):
                plate(0.0, sy * by, 0.092, bwid, 0.0014, (2, 8, 2),
                      studs=[(-0.036, sy * by, rr, 10), (0.036, sy * by, rr, 10)])
                for k in (-1, 1):
                    for t in (0.070, 0.104, 0.138, 0.172):
                        stud(k * t, sy * by, rr, 10)
        else:
            # Bronze: cast bosses, no plating -- the ornament IS the border bead, and the
            # bosses are the casting's fixing points.
            br = min(0.0062, bwid / 2)
            sr = min(0.0044, bwid / 2 - 0.0012)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    stud(sx * (run_x - br - 0.001), sy * by, br, 20)
                    stud(sx * bx, sy * (by - bwid / 2 - 0.0034 - br), br, 20)
                    stud(sx * (run_x - 0.050), sy * by, sr, 14)
            for sy in (-1, 1):
                stud(0.0, sy * by, min(0.0080, bwid / 2), 24)
                for t in (0.058, 0.106, 0.154):
                    stud(-t, sy * by, sr, 14)
                    stud(t, sy * by, sr, 14)
        return holes

    # ------------------------------------------------------------------------ UV pack --

    def pack(self):
        """Give every island a raw bbox, then fit each category's islands into that
        category's atlas rectangle with a single shelf packer and a single scale."""
        for isl in self.islands:
            us, vs = [], []
            for f in isl.faces:
                for v in f.verts:
                    u, w = isl.raw(f, v)
                    us.append(u)
                    vs.append(w)
            if not us:
                isl.bbox = (0.0, 0.0, 0.0, 0.0)
                continue
            isl.bbox = (min(us), min(vs), max(us) - min(us), max(vs) - min(vs))

        by_cat = {}
        for isl in self.islands:
            if isl.bbox[2] <= 0 or isl.bbox[3] <= 0:
                continue
            by_cat.setdefault(isl.category, []).append(isl)

        for cat, isls in by_cat.items():
            u0, v0, u1, v1 = REGIONS[cat]
            rw, rh = (u1 - u0) - 2 * REGION_INSET, (v1 - v0) - 2 * REGION_INSET

            # A category may have ONE "primary" island that is a RING -- the frame band is a
            # 0.640 x 0.320 rectangle whose middle is empty.  Packing its 40-odd ornament
            # islands next to it would halve the band's resolution for nothing; they go in
            # its hole instead, which is 936 x 424 px of otherwise dead atlas.
            primary = next((i for i in isls if i.hole is not None), None)
            rest = [i for i in isls if i is not primary]
            if primary is not None:
                s = min(rw / (primary.bbox[2] * primary.weight),
                        rh / (primary.bbox[3] * primary.weight))
                primary.xform = (s * primary.weight,
                                 u0 + REGION_INSET - primary.bbox[0] * s * primary.weight,
                                 v0 + REGION_INSET - primary.bbox[1] * s * primary.weight)
                ps, pu, pv = primary.xform
                hx0, hy0, hx1, hy1 = primary.hole
                sub_w = (hx1 - hx0) * ps - 2 * ISLAND_PAD
                sub_h = (hy1 - hy0) * ps - 2 * ISLAND_PAD
                base_u = hx0 * ps + pu + ISLAND_PAD
                base_v = hy0 * ps + pv + ISLAND_PAD
            else:
                rest = isls
                sub_w, sub_h = rw, rh
                base_u, base_v = u0 + REGION_INSET, v0 + REGION_INSET
            if not rest:
                continue

            lo, hi, best = 1e-4, 1000.0, None
            for _ in range(52):
                mid = (lo + hi) / 2
                res = self._shelf(rest, mid, sub_w, sub_h)
                if res is None:
                    hi = mid
                else:
                    lo = mid
                    best = res
            assert best is not None, "UV packing failed for category %s" % cat
            scale, placed = best
            for isl, (px, py) in placed:
                isl.xform = (scale * isl.weight,
                             base_u + px - isl.bbox[0] * scale * isl.weight,
                             base_v + py - isl.bbox[1] * scale * isl.weight)

        uvl = self.bm.loops.layers.uv.verify()
        for f in self.bm.faces:
            isl = self.face_island.get(f)
            if isl is None or isl.xform is None:
                for lp in f.loops:
                    lp[uvl].uv = (0.0, 0.0)
                continue
            s, du, dv = isl.xform
            for lp in f.loops:
                u, v = isl.raw(f, lp.vert)
                lp[uvl].uv = (u * s + du, v * s + dv)

    @staticmethod
    def _shelf(isls, scale, rw, rh):
        """Shelf-pack islands (tallest first) at the given scale; None if they don't fit."""
        items = sorted(isls, key=lambda i: -i.bbox[3] * i.weight)
        placed, x, y, shelf_h = [], 0.0, 0.0, 0.0
        for isl in items:
            w = isl.bbox[2] * scale * isl.weight
            h = isl.bbox[3] * scale * isl.weight
            if w > rw or h > rh:
                return None
            if x + w > rw:
                y += shelf_h + UV_PAD
                x, shelf_h = 0.0, 0.0
            if y + h > rh:
                return None
            placed.append((isl, (x, y)))
            x += w + UV_PAD
            shelf_h = max(shelf_h, h)
        return scale, placed

    def symbol_uv(self, sym):
        isl = sym["island"]
        if isl.xform is None:
            return None
        s, du, dv = isl.xform
        x, y = sym["pt"]
        return (x * s + du, y * s + dv, sym["size_m"] * s)


# ------------------------------------------------------------------------- scene / io --


def emit(style):
    cfg = STYLES[style]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0

    b = Board(style, cfg)
    b.build()

    bm = b.bm
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    b.pack()

    me = bpy.data.meshes.new("PlayTrayBoardMesh")
    bm.to_mesh(me)
    bm.free()
    me.uv_layers[0].name = "UVMap"   # parity with the shipped FBXs; Unity takes channel 0
                                     # whatever it is called, but a diff should not show noise
    mat = bpy.data.materials.new("PlayTrayBoard")
    mat.use_nodes = True
    me.materials.append(mat)
    ob = bpy.data.objects.new("PlayTrayBoard", me)
    bpy.context.scene.collection.objects.link(ob)

    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.shade_smooth()
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(31.0))
    ob.select_set(False)

    order = ["Slot1", "Slot2", "ShortRestToken", "LongRestToken",
             "ButtonSeat1", "ButtonSeat2", "ButtonSeat3",
             "ConfirmButton", "UndoButton", "SkipButton"]
    for name in order:
        p = b.anchors[name]
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = 'PLAIN_AXES'
        e.empty_display_size = 0.012
        e.location = p
        bpy.context.scene.collection.objects.link(e)
        e.parent = ob

    # --------------------------------------------------------------- the uv json --
    regions = {}
    for k, (u0, v0, u1, v1) in REGIONS.items():
        regions[k] = {"u0": u0, "v0": v0, "u1": u1, "v1": v1, "kind": REGION_KIND[k]}
    # per-category texel density and the affine map for the top-down islands, so the
    # texture lane can paint in BOARD METRES instead of guessing at pixels
    maps = []
    for isl in b.islands:
        if isl.kind != "planar" or isl.xform is None or not isl.faces:
            continue
        s, du, dv = isl.xform
        maps.append({"region": isl.category, "projection": "top_down_xy",
                     "flip_y": isl.flip_y,
                     "u_of_x": [round(s, 6), round(du, 6)],
                     "v_of_y": [round(s, 6), round(dv, 6)],
                     "px_per_m": round(s * ATLAS, 1),
                     "uv_bbox": [round(isl.bbox[0] * s + du, 6), round(isl.bbox[1] * s + dv, 6),
                                 round((isl.bbox[0] + isl.bbox[2]) * s + du, 6),
                                 round((isl.bbox[1] + isl.bbox[3]) * s + dv, 6)]})
    syms = []
    for sym in b.symbols:
        r = b.symbol_uv(sym)
        if r is None:
            continue
        syms.append({"name": sym["name"], "u": round(r[0], 6), "v": round(r[1], 6),
                     "size_uv": round(r[2], 6), "region": sym["region"]})
    doc = {
        "style": style,
        "atlas": ATLAS,
        "board_m": {"long": LONG, "short": SHORT, "thickness": cfg["thickness"]},
        "padding_px": round(UV_PAD * ATLAS, 1),
        "regions": regions,
        "symbols": syms,
        "maps": maps,
        "anchors_blender_xyz": {k: [round(v, 5) for v in b.anchors[k]] for k in order},
        "feature_metrics_m": b.metrics,
    }
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "%s_uv.json" % style), "w") as fh:
        json.dump(doc, fh, indent=2)

    # ------------------------------------------------------------------- export --
    path = os.path.join(TABLE, cfg["fbx"])
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=False, apply_unit_scale=True, global_scale=1.0,
        apply_scale_options='FBX_SCALE_ALL', object_types={'MESH', 'EMPTY'},
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, bake_anim=False, path_mode='COPY',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True)
    # FBX_SCALE_ALL + apply_unit_scale is the pairing that reproduces the shipped files'
    # node transform exactly: root scale (1,1,1), root rotation (90,0,0), anchor positions in
    # METRES.  Measured, because the alternatives look identical in Blender: FBX_SCALE_NONE
    # writes the metre->centimetre unit conversion into the node instead, and the board comes
    # back with a 0.01 root scale and anchors at 8.4 instead of 0.084.  BuildBoard sets a
    # node's rotation and position but never its scale, so that 0.01 would have survived
    # into the prefab.
    #
    # bake_space_transform=True is not cosmetic either.  Without it the exporter leaves the Z-up ->
    # Y-up correction as a -90 deg X rotation ON THE ROOT NODE and writes vertex data in
    # Blender orientation.  The three SHIPPED boards do the opposite -- identity node, Y-up
    # data -- and `PlayTray.prefab` carries a m_LocalRotation OVERRIDE on exactly that node.
    # An override beats whatever the FBX says, so a board whose correction lived on the node
    # would import rotated by 90 deg against a prefab that was never regenerated.  Baking it
    # reproduces the shipped file's structure exactly and takes that failure off the table.

    tri = sum(len(p.vertices) - 2 for p in me.polygons)
    quads = sum(1 for p in me.polygons if len(p.vertices) == 4)
    for k in sorted(b.metrics):
        print("[gen_board]        metric %-22s %s" % (k, b.metrics[k]))
    print("[gen_board] %-6s -> %s  faces=%d quads=%d (%.0f%%) tris=%d  dims=%s"
          % (style, cfg["fbx"], len(me.polygons), quads,
             100.0 * quads / max(1, len(me.polygons)), tri,
             tuple(round(x, 4) for x in ob.dimensions)))
    for name in order:
        print("[gen_board]        anchor %-15s blender=(%+.4f,%+.4f,%+.4f)"
              % (name, *b.anchors[name]))
    return doc


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    which = args[0] if args else "all"
    styles = list(STYLES) if which == "all" else [which]
    for s in styles:
        emit(s)


if __name__ == "__main__":
    main()

# ---------------------------------------------------------------------------------------------
# PROVENANCE, added when this file was RESCUED (2026-08-25, ModBuild 271).
#
# This script authored the three shipped boards in commit 3682a037 — and that commit contained
# ONLY the three FBXes. The 1045 lines above lived in a throwaway agent worktree and were one
# `git worktree prune` away from being the deleted sole copy of the boards' entire source.
#
# It was verified before being committed rather than assumed to be the right version: re-run
# against the shipped assets it reproduces every geometric figure exactly —
#
#     oak     verts 5950  faces 6192  tris 11896   dims (0.640, 0.0356, 0.320)
#     steel   verts 4986  faces 5286  tris  9968   dims (0.640, 0.0343, 0.320)
#     bronze  verts 9792  faces 10055 tris 19580   dims (0.640, 0.0354, 0.320)
#     all three: 0 boundary edges, 0 non-manifold edges, 0 loose verts
#
# The FBXes are NOT byte-identical across runs — the exporter stamps a creation time into the
# header — so `md5sum` is the wrong acceptance test for this script and `gen_stats.py` is the
# right one. The committed FBXes were left in place for exactly that reason: a rebuild would have
# churned 850 KB of binary to change a timestamp.
