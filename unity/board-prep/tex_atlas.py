"""tex_atlas.py -- the atlas compositor.

Reads unity/board-prep/out/<style>_uv.json (the mesh lane's half of the contract),
lays the procedural material bases into the region rectangles, carves the
decorative motifs at the symbols[] entries, pads every island, and writes the
three 2048^2 maps under the filenames the mod resolves boards by.

THE PADDING SCHEME, because it is the part the user called out
--------------------------------------------------------------
The contract's default region map has the six rectangles ABUTTING: face runs to
v=0.5 and frame starts at v=0.5. There is no gutter to pad into. So:

    every region rectangle is INSET by PAD px;
    all content is composited into that inner rect;
    the PAD-wide band around it is filled by dilation FROM the content.

Two neighbouring regions therefore leave a 2*PAD gutter, each side owns PAD px of
its own colour, and nothing foreign is reachable inside PAD px of any island.
The inner rect is the island. The mesh lane must keep its UV shells inside it --
that requirement is in the report, and tex_seams.py --fbx measures whether it
held.

WHERE THE ORNAMENT GOES, AND WHY IT IS NOT DERIVED FROM THE REGION RECTANGLES
-----------------------------------------------------------------------------
The first version of this file placed ornament at fractions of the six region
RECTANGLES -- a rosette at the centre of `face`, brackets at the corners of
`frame`. On the rebuilt boards that is wrong, and wrong in the silent direction.
Measured on the shipped meshes at a 2048 atlas:

    region         rect covered by real triangles     widest inscribed circle
    face                       16.6%                   46 px  (~27 mm)
    frame                      38.0%                   20 px  (~13 mm)
    slot_floor                 59.8%                  106 px  (~74 mm)
    rest_pads                  26.0%                   65 px  (~40 mm)
    button_seats               22.0%                   66 px  (~33 mm)

The face is not a field. It is a web of 8-26 mm strips running between four
large pockets -- two card recesses, a rest-token pocket and a button pocket --
and the centre of the `face` rectangle is the middle of a 12 mm bridge. A
560 px hero rosette placed there would have been carved into texels no triangle
samples, and would have rendered as nothing while every check reported success.

So placement is done in BOARD METRES. `<style>_uv.json` carries a `maps[]` block
with the affine `u_of_x` / `v_of_y` and `px_per_m` for every island, and a
`symbols[]` block naming the anchors the mesh lane authored; between them a
position in millimetres on the board converts to an exact texel. Every placement
is then FITTED against the surface the mesh actually has there (tex_seams.
surface_attrs gives per-texel board x/y/z and normal), and one that cannot be
made legible is DROPPED and said so, rather than stamped into a wall.

The roomiest decorated surface on these boards is a card recess floor, so that
is where the hero rosette went. That is a consequence of the geometry, not a
preference.

THE ALBEDO RULE
---------------
Motifs are carved, not painted. A motif contributes to HEIGHT; the height drives
the normal map and a light-independent cavity term. The only thing that reaches
albedo is that cavity, applied as a palette-locked darkening -- dirt in a groove,
which is real. No directional derivative ever touches albedo, so nothing in these
textures implies a light direction that the game's lighting then contradicts.
"""

import argparse
import copy
import os
import shutil

import numpy as np
from PIL import Image

import tex_bases as B
import tex_common as T
import tex_seams as S
import tex_symbols as SY


PAD = 12          # inset per region edge -> 2*PAD gutter between neighbours
MIN_PAD = 8       # the contract's floor


def lobe_half_angle(rough):
    """BoardLit's specular half-angle, in degrees, for a given roughness: the
    normal tilt at which the highlight falls to half its peak.

        power = exp2((1-rough)*9 + 1);  half-angle = acos(0.5 ** (1/power))

    This is the ONLY number that says what a roughness value means in the shader
    that ships, so the floor below is chosen against it rather than against a
    PBR intuition borrowed from a Principled render."""
    power = 2.0 ** ((1.0 - float(rough)) * 9.0 + 1.0)
    return float(np.degrees(np.arccos(0.5 ** (1.0 / power))))


# THE ROUGHNESS FLOOR IN THE SHIPPED PACK, and it is a stereo-safety decision
# before it is a look decision.
#
# BoardLit's own floor is max(0.08, mr.g) -- a 2.7 deg half-angle, which is a
# mirror. The board is a 0.64 m slab held about 40 cm from the face, filling a
# large solid angle of BOTH eyes, and a lobe narrower than the normal map's own
# per-texel wobble turns that wobble into a per-eye sparkle. That is this
# project's recurring stereo-rivalry defect and it has shipped before.
#
# Measured, on the installed normal maps -- the angle between neighbouring
# texels' normals, which is the thing a narrow lobe converts into flicker:
#
#     style   mean step (v / u)     p95 step (v / u)    tilt from flat (mean)
#     oak      11.22 / 4.17 deg     40.64 / 20.85       13.20 deg
#     steel     4.68 / 3.33 deg     10.15 /  6.68        5.20 deg
#     bronze    1.56 / 1.01 deg      2.25 /  1.42        2.59 deg
#
# and the lobe half-angles those have to survive:
#
#     rough 0.08 -> 2.71 deg    rough 0.35 -> 6.27 deg    rough 0.50 -> 10.00 deg
#     rough 0.32 -> 5.72 deg    rough 0.42 -> 7.87 deg    rough 0.60 -> 13.63 deg
#
# 0.42 is chosen, giving 7.87 deg. It clears steel's p95 single-texel step along
# u (6.68 deg) and is 1.7x its mean (4.68 deg). Steel is the case that matters:
# it is 99.3% metallic, so its f0 is the ALBEDO and not a dielectric 0.04, and
# it carries the most single-texel-only normal energy of the three (1-texel vs
# 4-texel-downsampled mean |grad|: oak 1.08x, steel 1.39x, bronze 0.67x).
#
# WHY ERR BROAD RATHER THAN SHARP. _SpecStrength is baked into the material
# inside the bundle and has no config dial, so "too sparkly" costs a full bundle
# rebuild and a hardware round to walk back, while "too soft" is a sheen that
# still reads as metal. And a broad lobe is not a smaller highlight here: this
# Blinn-Phong has NO energy normalisation, so narrowing the lobe does not raise
# the peak -- f0 is the peak at every roughness -- it only shrinks the area that
# receives anything.
#
# WHAT THIS FLOOR CANNOT DO, stated because it bounds every claim about the
# specular below. With the shader's baked key and a viewer in front of the
# board, the half-vector sits about 32 deg off the flat face's normal, so the
# flat face gets essentially no highlight at any roughness under ~0.85. The
# specular on these boards is a BEVEL AND MOULDING term. It cannot rescue a flat
# albedo, and the steel fix below does not ask it to.
MRS_ROUGH_FLOOR = 0.42

# The material's own micro-relief must sit well BELOW the carved ornament, or
# the normal map is a field of grain noise with a rosette lost inside it --
# which is exactly what the first composited normal map was. Measured: oak's
# raw height swings about -0.60..+0.40 (the open pores dominate) against an
# ornament depth of 0.55, i.e. the grain was as loud as the carving.
BASE_HEIGHT = 0.26
ORNAMENT_NORMAL_STRENGTH = 11.0

# ...and the right amount of micro-relief is not the same for all three.
# Oak's height detail is low-frequency (growth bands), so it survives a
# strong normal. The two metals carry their detail at 10-20 texels, and at
# oak's setting both lit renders came back as sandpaper: steel as a dense
# salt-and-pepper, bronze as hammered gold leaf. A metal's micro-structure
# belongs in ROUGHNESS, which is why these two are scaled down hard rather
# than removed.
STYLE_HEIGHT = {"oak": 1.00, "steel": 0.30, "bronze": 0.38}

# A motif smaller than this at a 2048 atlas is not ornament, it is a smudge:
# the AI stencils carry interior filigree that needs 3-4 texels a stroke, and
# below ~34 px a rosette's inner ring closes up into a disc. A placement that
# cannot be fitted at least this large is dropped and reported.
MIN_MOTIF_PX = 34          # at a 2048 atlas; scaled by min_motif_px(n)

# How much of a motif's ink is allowed to fall outside the top-facing surface it
# is being carved into. Not zero: an antialiased rim overlapping the last texel
# of a fillet is invisible. One percent is about one texel of a 100 px motif's
# perimeter.
SPILL_MAX = 0.01

# Which of the two delivered sheets each style takes each motif from, and why.
# Both sheets came back good; they are in deliberately different ornamental
# hands, so the choice is editorial and is stated rather than defaulted.
#
#   oak    -- the medieval guild woodcut (sheet_a). Carved oak furniture in this
#             idiom is European joinery ornament; the scrollwork corner bracket
#             with its volute and leaf curls is exactly a chisel-cut motif.
#   bronze -- the Norse/Celtic interlace (sheet_b). Interlace is a METALWORK
#             idiom before it is anything else -- it comes off cast and chased
#             bronze and silver -- and the plaited bands read as raised chasing
#             when they are carved rather than drawn.
#   steel  -- the plainer variants. A brushed steel plate is a machined object;
#             it gets the guild hand's simplest motifs and takes `bracket_alt`
#             from sheet_b for its corner, because that is the one severe,
#             untooled L on either sheet: two tapered arms and a square stud.
#             (sheet_a's own bracket_alt cell came back as a hammer instead of a
#             bracket -- the one cell of eighteen that missed its brief -- so
#             sheet_b covers it, which is the two-sheet argument being cashed.)
STYLE_MOTIFS = {
    "oak": dict(sheet="a", roles={}),
    "steel": dict(sheet="a", roles={"corner_bracket": ("bracket_alt", "b")}),
    "bronze": dict(sheet="b", roles={}),
}

# The Identity table of BOARD-CONTRACT.md. These names are load-bearing: the mod
# resolves boards by them. Nothing here may be renamed by this lane.
STYLE_FILES = {
    "oak":    dict(base="PlayTray",           fbx="PlayTray_prepped.fbx"),
    "steel":  dict(base="PlayTray_9capjqp6",  fbx="PlayTray_9capjqp6.fbx"),
    "bronze": dict(base="PlayTray_16vm268h",  fbx="PlayTray_16vm268h.fbx"),
}

BUNDLE_DIR = "unity/GloomhavenVR.Assets/Assets/Bundle/Table"

# The contract's DEFAULT layout, used verbatim when the mesh lane has not yet
# written out/<style>_uv.json. Loudly announced when it is used, because a
# texture composited against a guessed layout is not a finished texture.
DEFAULT_UV = {
    "style": None,
    "atlas": 2048,
    "regions": {
        "face":         {"u0": 0.0,  "v0": 0.0,  "u1": 1.0, "v1": 0.5,  "kind": "decorated top face"},
        "frame":        {"u0": 0.0,  "v0": 0.5,  "u1": 0.5, "v1": 0.75, "kind": "outer frame + corner brackets"},
        "slot_floor":   {"u0": 0.5,  "v0": 0.5,  "u1": 0.75, "v1": 0.75, "kind": "card recess floors"},
        "rest_pads":    {"u0": 0.75, "v0": 0.5,  "u1": 1.0, "v1": 0.75, "kind": "the two round rest pads"},
        "button_seats": {"u0": 0.0,  "v0": 0.75, "u1": 0.5, "v1": 1.0,  "kind": "the THREE button recesses"},
        "sides":        {"u0": 0.5,  "v0": 0.75, "u1": 1.0, "v1": 1.0,  "kind": "edges + back"},
    },
    "symbols": [
        {"name": "centre_rose", "u": 0.5, "v": 0.25, "size_uv": 0.18, "region": "face"},
    ],
}

REGION_NAMES = tuple(DEFAULT_UV["regions"].keys())


def load_uv(style, path=None, out_dir="unity/board-prep/out"):
    """Load the mesh lane's region map, or fall back to the contract default."""
    p = path or os.path.join(out_dir, f"{style}_uv.json")
    if os.path.exists(p):
        uv = T.read_json(p)
        src = p
        missing = [r for r in REGION_NAMES if r not in uv.get("regions", {})]
        if missing:
            raise ValueError(f"{p}: region map is missing contract regions {missing}")
        extra = [r for r in uv["regions"] if r not in REGION_NAMES]
        if extra:
            print(f"  NOTE {p} adds non-contract regions {extra}; they are composited "
                  f"with the 'sides' treatment")
    else:
        uv = copy.deepcopy(DEFAULT_UV)
        uv["style"] = style
        src = "the BOARD-CONTRACT.md DEFAULT layout"
        print(f"  !! {p} does not exist -- the mesh lane has not written its half of")
        print(f"  !! the contract yet. Compositing against {src}.")
        print(f"  !! The maps this produces exercise the pipeline; they are NOT final,")
        print(f"  !! because the regions may not be where the mesh's UVs actually are.")
    return uv, src


# --------------------------------------------------------------------------
# placement
# --------------------------------------------------------------------------

class Placement:
    __slots__ = ("name", "cx", "cy", "w", "h", "depth", "rot", "flip", "region", "derived")

    def __init__(self, name, cx, cy, w, h, depth, region, rot=0, flip=None, derived=True):
        self.name, self.cx, self.cy = name, cx, cy
        self.w, self.h = int(w), int(h)
        self.depth, self.region = depth, region
        self.rot, self.flip, self.derived = rot, flip, derived


# --------------------------------------------------------------------------
# board-metre placement
#
# `<style>_uv.json` gives, per UV island, an affine from board metres to UV:
#     u = u_of_x[0] * x + u_of_x[1]
#     v = v_of_y[0] * y + v_of_y[1]
# with u_of_x[0] == v_of_y[0] == px_per_m / atlas. Everything below works in
# metres and converts once, at the end.
# --------------------------------------------------------------------------

HALF_LONG = 0.320          # the contract's board half-extents, in metres
HALF_SHORT = 0.160
RUN_PITCH = 0.020          # 32 repeats along the long edge, 16 along the short:
                           # an exact divisor of both, so a running ornament
                           # closes at every corner by construction


def uv_of_board(mp, x, y):
    su, ou = mp["u_of_x"]
    sv, ov = mp["v_of_y"]
    return su * x + ou, sv * y + ov


def board_of_uv(mp, u, v):
    su, ou = mp["u_of_x"]
    sv, ov = mp["v_of_y"]
    return (u - ou) / su, (v - ov) / sv


def map_at(uv, region, u, v):
    """The island map whose UV bbox contains (u, v). Islands of one region do
    not overlap, so at most one answers; None means the point is in the region
    rectangle but on no island, which is itself the answer to a placement
    question."""
    for mp in uv.get("maps", []):
        if mp.get("region") != region:
            continue
        b = mp["uv_bbox"]
        if b[0] - 1e-6 <= u <= b[2] + 1e-6 and b[1] - 1e-6 <= v <= b[3] + 1e-6:
            return mp
    return None


# What to do with each name the mesh lane writes into symbols[]. The json is
# authoritative about WHERE; this table is the texture lane's half of the
# contract -- which motif goes to that anchor, how deep, and where a stated
# anchor is deliberately not used as one.
JSON_SYMBOL_PLAN = {
    "slot_rose_1":      dict(motif="centre_rose", depth=-0.55, rot=0),
    "slot_rose_2":      dict(motif="centre_rose", depth=-0.55, rot=0),
    "rest_glyph_short": dict(motif="rest_short", depth=-0.60, rot=0),
    "rest_glyph_long":  dict(motif="rest_long", depth=-0.60, rot=0),
    "seat_glyph_1":     dict(motif="seat_bezel", depth=-0.50, rot=0),
    "seat_glyph_2":     dict(motif="seat_bezel", depth=-0.50, rot=0),
    "seat_glyph_3":     dict(motif="seat_bezel", depth=-0.50, rot=0),
    # The mesh lane names this anchor `centre_rose` and it is exactly the board
    # centre, but the board centre is the middle of the 12 mm bridge between the
    # two card recesses: the anchor's own size_uv is 14.4 mm and the largest
    # circle that fits there is 28 px at a 2048 atlas. A rosette at 28 px is a
    # disc. The POSITION is honoured and the motif is the one that survives it.
    "centre_rose":      dict(motif="pip", depth=-0.40, rot=0,
                             why="board centre is a 12 mm bridge; a rosette "
                                 "cannot be legible there, a lozenge can"),
    # A reference marker, not a placement: it names the corner of the top field
    # with a nominal 20 mm size, but the field web is 8 mm wide at that corner.
    # Used below as a reference for where the field corner IS.
    "field_corner_ref": dict(motif=None,
                             why="reference marker; the web is 8 mm wide there"),
}


def min_motif_px(n):
    return max(8, int(round(MIN_MOTIF_PX * n / 2048.0)))


def px_per_m(mp, n):
    """The island's texels per metre AT THIS ATLAS SIZE. maps[] states it for
    2048, which is the contract's size; a --size 1024 preview must not report
    millimetres that are twice what they are."""
    return mp["px_per_m"] * n / 2048.0


def _band(v, lo, hi, w):
    """A raised band for lo <= v <= hi with a w-wide smooth shoulder."""
    return T.smoothstep(lo, lo + w, v) * (1.0 - T.smoothstep(hi - w, hi, v))


class Surface:
    """The mesh's real UV footprint, per texel, plus the derived room fields the
    planner needs. Everything here is measured off the shipped FBX."""

    def __init__(self, attrs, uv, n, pad):
        self.n = n
        self.cov = attrs["cov"]
        self.nz = attrs["nz"]
        self.x = np.nan_to_num(attrs["x"])
        self.y = np.nan_to_num(attrs["y"])
        self.z = np.nan_to_num(attrs["z"])
        self.rects = {k: T.region_rect_px(v, n) for k, v in uv["regions"].items()}
        self.top = {}
        self.room = {}
        for name, (x0, y0, x1, y1) in self.rects.items():
            box = np.zeros((n, n), dtype=bool)
            box[y0 + pad:y1 - pad, x0 + pad:x1 - pad] = True
            m = box & self.cov & (self.nz > 0.9)
            self.top[name] = m
            self.room[name] = T.edt(~m) if m.any() else np.zeros((n, n))

    def fits(self, region, cx, cy):
        """Radius in texels of the largest circle centred there that stays on
        the region's top-facing surface. 0 means the point is not on it."""
        n = self.n
        px, py = int(round(cx)), int(round(cy))
        if not (0 <= px < n and 0 <= py < n):
            return 0.0
        if not self.top[region][py, px]:
            return 0.0
        return float(self.room[region][py, px])

    def roomiest(self, region, pred, taken=None, sep=2.2):
        """The point of the region's top face with the most room, restricted to
        `pred` (a boolean array in board terms) and at least `sep` * radius away
        from anything already taken."""
        work = np.where(self.top[region] & pred, self.room[region], 0.0)
        if taken is not None:
            work = np.where(taken, 0.0, work)
        i = int(np.argmax(work))
        py, px = divmod(i, self.n)
        return px, py, float(work[py, px])


def _suppress(taken, n, px, py, r):
    yy, xx = np.mgrid[max(0, py - int(r)):min(n, py + int(r) + 1),
                      max(0, px - int(r)):min(n, px + int(r) + 1)]
    taken[max(0, py - int(r)):min(n, py + int(r) + 1),
          max(0, px - int(r)):min(n, px + int(r) + 1)] |= (
              (xx - px) ** 2 + (yy - py) ** 2 <= r * r)


def fit_size(surf, region, name, cx, cy, want, symbols_dir, seed, rot=0):
    """Shrink a placement until at most SPILL_MAX of its ink falls off the
    surface it is being carved into. Returns (size, spill) or (0, 1.0) if it
    cannot be made to fit at MIN_MOTIF_PX."""
    mask = surf.top[region]
    size = int(want)
    floor = min_motif_px(surf.n)
    for _ in range(10):
        if size < floor:
            return 0, 1.0
        h, cov = SY.get_symbol(name, symbols_dir, px=size, seed=seed)
        cov, _dummy = (np.rot90(cov, rot), None) if rot else (cov, None)
        sh, sw = cov.shape
        x0 = int(round(cx - sw / 2.0))
        y0 = int(round(cy - sh / 2.0))
        n = surf.n
        dx0, dy0 = max(x0, 0), max(y0, 0)
        dx1, dy1 = min(x0 + sw, n), min(y0 + sh, n)
        if dx1 <= dx0 or dy1 <= dy0:
            return 0, 1.0
        sub = cov[dy0 - y0:dy1 - y0, dx0 - x0:dx1 - x0]
        on = mask[dy0:dy1, dx0:dx1]
        total = float(cov.sum())
        spill = 1.0 - float((sub * on).sum()) / max(total, 1e-9)
        if spill <= SPILL_MAX:
            return size, spill
        size = int(size * 0.88)
    return 0, 1.0


def plan_placements(style, uv, surf, n, pad, symbols_dir, seed, notes):
    """The ornament plan, in board metres, fitted to the surface that is there.

    Two sources, in this order:
      1. every entry of the json `symbols[]` block, through JSON_SYMBOL_PLAN;
         the mesh lane owns WHERE, this file owns WHAT and HOW DEEP.
      2. derived placements for the surfaces the json is silent about, found by
         asking the surface itself where there is room -- not by taking
         fractions of a region rectangle, which is what put a 560 px rosette in
         a hole on the first pass.
    """
    out = []
    used = {}

    # ---- 1. the json anchors
    for e in uv.get("symbols", []):
        nm = e["name"]
        plan = JSON_SYMBOL_PLAN.get(nm)
        if plan is None:
            notes.append(f"    symbols[] names {nm!r}, which this lane has no plan "
                         f"for; skipped")
            continue
        motif = plan["motif"]
        region = e.get("region")
        u, v = float(e["u"]), float(e["v"])
        mp = map_at(uv, region, u, v)
        cx, cy = T.uv_to_px(u, v, n)
        if motif is None:
            bx, by = board_of_uv(mp, u, v) if mp else (float("nan"),) * 2
            notes.append(f"    {nm} at ({bx * 1000:+.0f},{by * 1000:+.0f}) mm: not a "
                         f"placement -- {plan['why']}")
            continue
        want = int(round(float(e.get("size_uv", 0.05)) * n))
        size, spill = fit_size(surf, region, motif, cx, cy, want, symbols_dir, seed,
                               rot=plan.get("rot", 0))
        bx, by = board_of_uv(mp, u, v) if mp else (float("nan"),) * 2
        mm = (size / px_per_m(mp, n) * 1000.0) if (mp and size) else 0.0
        if not size:
            notes.append(f"    {nm} -> {motif}: DROPPED, cannot be fitted at "
                         f"{min_motif_px(n)}px on the surface at "
                         f"({bx * 1000:+.0f},{by * 1000:+.0f}) mm")
            continue
        out.append(Placement(motif, cx, cy, size, size, plan["depth"], region,
                             rot=plan.get("rot", 0), derived=False))
        used.setdefault(region, []).append((int(cx), int(cy), size))
        notes.append(f"    {nm} -> {motif}: ({bx * 1000:+.0f},{by * 1000:+.0f}) mm, "
                     f"{size}px = {mm:.1f} mm"
                     f"{'' if size == want else f' (asked {want}px, fitted down)'}"
                     f", spill {spill * 100:.2f}%")

    # ---- 2. the face web, found by measurement
    #
    # The json is silent about the top field because there is no single anchor
    # to name: the web is what is left over between four pockets. So ask the
    # surface. `roomiest` returns the point of the face with the largest
    # inscribed circle inside a predicate; the predicates below are board-space
    # descriptions of the three places worth decorating, and each one is
    # verified to have found something before anything is stamped.
    if "face" in surf.top and surf.top["face"].any():
        X, Y = surf.x, surf.y
        taken = np.zeros((n, n), dtype=bool)
        for px, py, sz in used.get("face", []):
            _suppress(taken, n, px, py, sz * 0.75)

        def place(motif, pred, depth, rot=0, label="", over=1.0):
            cx, cy, r = surf.roomiest("face", pred, taken)
            if r < min_motif_px(n) / 2.0:
                notes.append(f"    face/{label or motif}: no spot with room for "
                             f"{min_motif_px(n)}px (best {2 * r:.0f}px), dropped")
                return
            size, spill = fit_size(surf, "face", motif, cx, cy, int(2 * r * over),
                                   symbols_dir, seed, rot=rot)
            if not size:
                notes.append(f"    face/{label or motif}: fit failed at "
                             f"({X[cy, cx] * 1000:+.0f},{Y[cy, cx] * 1000:+.0f}) mm, dropped")
                return
            out.append(Placement(motif, cx, cy, size, size, depth, "face",
                                 rot=rot, derived=True))
            _suppress(taken, n, cx, cy, size * 0.75)
            notes.append(f"    face/{label or motif} -> {motif}: "
                         f"({X[cy, cx] * 1000:+.0f},{Y[cy, cx] * 1000:+.0f}) mm, "
                         f"{size}px, spill {spill * 100:.2f}%")

        mid = np.abs(X) < 0.060
        place("centre_rose", mid & (Y > 0.030), -0.50, label="top margin rose")
        place("maker_mark", mid & (Y < -0.030), -0.45, label="bottom margin mark")
        # the four re-entrant corners of the web, where the outer margin meets a
        # bridge between two pockets: an L bracket has a corner to sit in there
        for i, (sx, sy) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
            q = ((X * sx) > 0.100) & ((X * sx) < 0.250) & ((Y * sy) > 0.050)
            # a corner bracket fills its square corner to corner rather than
            # inscribing a circle in it, so it starts from the inscribed
            # diameter times sqrt(2) and shrinks from there
            place("corner_bracket", q, +0.38, rot=[0, 3, 1, 2][i], over=1.41,
                  label=f"corner {'LR'[sx > 0]}{'BT'[sy > 0]}")

    # ---- 3. the card recess floors get the rosette and NOTHING else.
    # A corner tick in each recess was in the first plan and is not here: the
    # recess floor is under a card for most of the game, and what shows when it
    # is empty should be one clean ornament, not one ornament plus eight ticks
    # fighting it at 24 mm.
    return out


# --------------------------------------------------------------------------
# board-space ornament -- authored as a function of position on the BOARD
#
# A running border is a tiling problem, and the pipeline's standing answer to a
# tiling problem is "procedural, and never AI". These two go one better than a
# tiled strip: they are evaluated per texel from the board coordinate the mesh
# actually has there, so they follow the real outline, close at every corner,
# and cannot be stamped into the wrong island because they never leave the
# island's own texels.
# --------------------------------------------------------------------------

def board_projection(surf, n, span=2 * HALF_LONG):
    """Index arrays that sample a BOARD-SPACE material field per atlas texel,
    plus the weight with which that sample should be trusted.

    WHY THE MATERIAL IS NOT GENERATED IN ATLAS SPACE ANY MORE
    --------------------------------------------------------
    It used to be: each of the six region rectangles got its own build of the
    material at its own `detail` setting, filling the rectangle. That is right
    for six unrelated surfaces and wrong for six views of one object. The
    islands are unwrapped at wildly different scales -- on Oak the face runs at
    3455 texels per metre, the frame at 1517 and a card recess floor at 1437 --
    so the same "10 growth rings across the field" came out at 2.4x the
    frequency on the face as on the recess floor next to it, with a hard change
    of grain at every island boundary. The first lit render of the rebuilt oak
    board showed it plainly: the button pocket read as a separate, finer plank
    glued into the board, and the recess floors as two more.

    A plank does not do that. Cut a recess into oak and the grain carries
    straight on, one scale, one direction, only lower. So the material is now
    built once over a square metre-space domain covering the whole board and
    sampled by each texel's real board position -- which tex_seams.surface_attrs
    already measures. Islands stop existing as far as the material is concerned.

    The weight falls off on surfaces that are not facing up, because a top-down
    projection says nothing useful about a vertical wall: it smears one line of
    the field down the whole wall. Those texels keep the atlas-space build,
    which is what the projection is blended against."""
    x = np.nan_to_num(surf.x)
    y = np.nan_to_num(surf.y)
    gx = np.clip(((x + span * 0.5) / span * n).astype(np.int64), 0, n - 1)
    gy = np.clip(((span * 0.5 - y) / span * n).astype(np.int64), 0, n - 1)
    w = T.smoothstep(0.35, 0.78, np.abs(surf.nz)) * surf.cov
    return gy, gx, w


def scatter_to_board(field, gy, gx, mask, n):
    """Atlas-space scalar -> board space, keeping the largest contributor where
    several atlas texels land on the same board texel (the top face and the
    back of the board project to exactly the same x,y). Used to carry the
    carvings' cavity into the board-space material build, which is what lets
    bronze's patina know where the carvings are.

    THE HOLES ARE FILLED, and that fill is a bug fix rather than a polish.
    A scatter leaves every destination cell that no source texel happened to
    land on at exactly zero. Measured on the bronze board at a 2048 atlas:
    910,878 source texels land in 726,921 distinct cells, and inside the band
    the board actually occupies only 34.7% of the cells receive anything --
    65.3% are holes, with a mean distance of 1.22 px to the nearest filled one.

    So the cavity field the patina was reading was two-thirds ZERO in a
    single-texel salt-and-pepper pattern, and any threshold applied to it
    inherits that pattern exactly. That is a per-texel stipple manufactured by
    the resampling, and it is invisible to a coverage fraction and to a cavity
    enrichment ratio -- both of which passed. The nearest-source fill is the
    same dilation the atlas padding already uses, and it is the correct
    resampling: a destination cell with no sample of its own takes the value of
    the nearest cell that has one."""
    out = np.zeros((n, n), dtype=np.float64)
    np.maximum.at(out, (gy[mask], gx[mask]), np.asarray(field)[mask])
    hit = np.zeros((n, n), dtype=bool)
    hit[gy[mask], gx[mask]] = True
    if hit.any() and not hit.all():
        T.dilate_fill([out], hit)
    return out


def frame_relief(surf, n):
    """A moulding on the frame ring: two fillets running parallel to the board
    outline with a bead row between them."""
    ax, ay = np.abs(surf.x), np.abs(surf.y)
    d = np.minimum(HALF_LONG - ax, HALF_SHORT - ay)      # metres in from the edge
    along_y = (HALF_LONG - ax) < (HALF_SHORT - ay)
    t = np.where(along_y, surf.y, surf.x)
    w = 0.0012
    h = (_band(d, 0.0035, 0.0070, w) * 1.00
         + _band(d, 0.0180, 0.0215, w) * 0.75)
    ph = T.frac(t / RUN_PITCH + 0.5)
    bead = T.smoothstep(0.20, 0.30, ph) * (1.0 - T.smoothstep(0.70, 0.80, ph))
    h = h + 0.85 * bead * _band(d, 0.0095, 0.0155, w)
    return h * T.smoothstep(0.50, 0.88, surf.nz)


def side_relief(surf, n):
    """A dentil strip on the board's outer wall, on the same pitch as the frame
    moulding so the two read as one object seen from two sides."""
    wall = 1.0 - T.smoothstep(0.35, 0.70, np.abs(surf.nz))   # 1 on a vertical wall
    along_y = np.abs(surf.x) > np.abs(surf.y) * 1.6
    t = np.where(along_y, surf.y, surf.x)
    ph = T.frac(t / RUN_PITCH + 0.5)
    tooth = T.smoothstep(0.24, 0.32, ph) * (1.0 - T.smoothstep(0.68, 0.76, ph))
    zb = _band(surf.z, -0.0215, -0.0090, 0.0012)
    rail = (_band(surf.z, -0.0060, -0.0035, 0.0008) * 0.8
            + _band(surf.z, -0.0265, -0.0240, 0.0008) * 0.8)
    return (tooth * zb + rail) * wall


# --------------------------------------------------------------------------
# compositing
# --------------------------------------------------------------------------

def _cav(height, n):
    """Occlusion for this atlas. Three radii scaled to the atlas so a wide
    carved plateau darkens in its INTERIOR and not just along its rim."""
    k = n / 2048.0
    radii = (max(2, int(4 * k)), max(4, int(14 * k)), max(8, int(48 * k)))
    return T.cavity_multi(height, radii=radii, gain=2.6, weights=(1.0, 1.3, 1.1))


def _oriented(h, cov, rot, flip):
    if rot:
        h = np.rot90(h, rot)
        cov = np.rot90(cov, rot)
    if flip == "x":
        h, cov = h[:, ::-1], cov[:, ::-1]
    elif flip == "y":
        h, cov = h[::-1], cov[::-1]
    return np.ascontiguousarray(h), np.ascontiguousarray(cov)


def stamp(height, place, clip_box, symbols_dir, seed, report):
    """Carve one motif into the height field, clipped to its region's inner rect.

    A motif that would cross a region boundary is CLIPPED, not moved, and the
    clipping is reported -- a symbol silently spilling into the neighbouring
    island is precisely the seam failure this pipeline exists to prevent."""
    want = (place.w, place.h) if place.w != place.h else place.w
    h, cov = SY.get_symbol(place.name, symbols_dir, px=want, seed=seed)
    h, cov = _oriented(h, cov, place.rot, place.flip)
    sh, sw = h.shape

    x0 = int(round(place.cx - sw / 2.0))
    y0 = int(round(place.cy - sh / 2.0))
    cx0, cy0, cx1, cy1 = clip_box
    dx0, dy0 = max(x0, cx0), max(y0, cy0)
    dx1, dy1 = min(x0 + sw, cx1), min(y0 + sh, cy1)
    if dx1 <= dx0 or dy1 <= dy0:
        report.append(f"    {place.name} in {place.region}: entirely outside its region, dropped")
        return 0.0
    sub_h = h[dy0 - y0:dy1 - y0, dx0 - x0:dx1 - x0]
    sub_c = cov[dy0 - y0:dy1 - y0, dx0 - x0:dx1 - x0]
    lost = 1.0 - (sub_c.sum() / max(cov.sum(), 1e-9))
    if lost > 0.005:
        report.append(f"    {place.name} in {place.region}: CLIPPED, "
                      f"{lost * 100:.1f}% of the motif fell outside the padded region")
    tgt = height[dy0:dy1, dx0:dx1]
    height[dy0:dy1, dx0:dx1] = tgt * (1.0 - sub_c) + (tgt + place.depth * sub_h) * sub_c
    return lost


def style_motifs(style, motif_root, out_root):
    """Assemble the per-style motif directory from the two sheets.

    Copies the chosen hand's file for every motif into out_root/motifs_<style>/,
    applying STYLE_MOTIFS' per-role substitutions, so the compositor downstream
    only ever sees one directory and does not have to know a sheet exists."""
    spec = STYLE_MOTIFS[style]
    src = {"a": os.path.join(motif_root, "motifs_a"),
           "b": os.path.join(motif_root, "motifs_b")}
    dst = os.path.join(out_root, f"motifs_{style}")
    os.makedirs(dst, exist_ok=True)
    chosen = {}
    for nm in SY.AI_NAMES:
        want, sheet = spec["roles"].get(nm, (nm, spec["sheet"]))
        ok = False
        for suffix in (".png", SY.MASK_SUFFIX):
            p = os.path.join(src[sheet], want + suffix)
            if os.path.exists(p):
                shutil.copyfile(p, os.path.join(dst, nm + suffix))
                ok = True
        if ok:
            chosen[nm] = f"{want}@sheet_{sheet}"
    return dst, chosen


def build_style(style, uv, n=T.N_DEFAULT, seed=20260825, symbols_dir=None,
                pad=PAD, normal_strength=ORNAMENT_NORMAL_STRENGTH, surf=None,
                fbx=None):
    """Composite one board. Returns (albedo, normal, mr, island_labels, notes)."""
    notes = []
    rects = {k: T.region_rect_px(v, n) for k, v in uv["regions"].items()}

    content = np.zeros((n, n), dtype=bool)
    labels = np.zeros((n, n), dtype=np.int32)
    region_order = sorted(rects)
    inner = {}
    for i, name in enumerate(region_order, start=1):
        x0, y0, x1, y1 = rects[name]
        ix0, iy0, ix1, iy1 = x0 + pad, y0 + pad, x1 - pad, y1 - pad
        if ix1 - ix0 < 4 * pad or iy1 - iy0 < 4 * pad:
            raise ValueError(f"region {name!r} is {x1 - x0}x{y1 - y0}px, too small to "
                             f"carry a {pad}px inset on each edge")
        inner[name] = (ix0, iy0, ix1, iy1)
        content[iy0:iy1, ix0:ix1] = True
        labels[iy0:iy1, ix0:ix1] = i

    albedo = np.zeros((n, n, 3), dtype=np.float64)
    height = np.zeros((n, n), dtype=np.float64)
    rough = np.zeros((n, n), dtype=np.float64)
    metal = np.zeros((n, n), dtype=np.float64)

    def paint(cavity_bias=None, surf=None):
        """The base material.

        Two builds, blended per texel: one projected from BOARD space, which is
        the one that matters and is what makes the grain continuous across every
        island (see board_projection), and one in atlas space per region, which
        the projection falls back to on walls the top-down projection cannot
        describe. The per-region tone/roughness/height offsets still apply to
        both -- a recess floor really is a little darker and a little rougher
        than the face above it, because it is shaded and unhandled -- but the
        GRAIN no longer changes scale or direction at a region boundary."""
        proj = None
        if surf is not None:
            gy, gx, w = board_projection(surf, n)
            cb = None
            if cavity_bias is not None:
                cb = scatter_to_board(cavity_bias, gy, gx,
                                      surf.cov & (np.abs(surf.nz) > 0.35), n)
            fb = B.build(style, n, seed, orient="along", detail=1.0, cavity_bias=cb,
                         tag="board")
            proj = (fb, gy, gx, w[..., None], w)
        for name in region_order:
            tr = B.REGION_TREATMENT.get(name, B.REGION_TREATMENT["sides"])
            f = B.build(style, n, seed, orient=tr["orient"], detail=tr["detail"],
                        cavity_bias=cavity_bias)
            ix0, iy0, ix1, iy1 = inner[name]
            sl = (slice(iy0, iy1), slice(ix0, ix1))
            a, h, r, m = f.albedo[sl], f.height[sl], f.rough[sl], f.metal[sl]
            if proj is not None:
                fb, gy, gx, w3, w1 = proj
                sy, sx = gy[sl], gx[sl]
                a = T.lerp(a, fb.albedo[sy, sx], w3[sl])
                h = T.lerp(h, fb.height[sy, sx], w1[sl])
                r = T.lerp(r, fb.rough[sy, sx], w1[sl])
                m = T.lerp(m, fb.metal[sy, sx], w1[sl])
            albedo[sl] = a
            height[sl] = h * tr["height"] * BASE_HEIGHT * STYLE_HEIGHT.get(style, 1.0)
            rough[sl] = np.clip(r + tr["rough"], 0.05, 0.98)
            metal[sl] = m
            if tr["tone"]:
                # tone offsets stay INSIDE the palette: darken toward the ramp's
                # own dark end rather than multiplying toward black
                k = abs(tr["tone"])
                dark = B.PALETTES[style][0].map(np.zeros(1))[0]
                albedo[sl] = albedo[sl] * (1.0 - k) + dark[None, None, :] * k
        return None

    # ---- the mesh's real surface. Without it there is neither an honest
    # placement (a region rectangle does not say which of its texels a triangle
    # samples) nor a continuous material (an island does not say where on the
    # board it is).
    if surf is None:
        if fbx is None or not os.path.exists(fbx or ""):
            raise ValueError(
                f"{style}: no FBX to measure the surface from. Placement in board "
                f"metres needs the mesh's real UV footprint; compositing against "
                f"region rectangles alone put a 560 px rosette in a hole last time. "
                f"Pass --fbx-dir at a directory holding {STYLE_FILES[style]['fbx']}.")
        surf = Surface(S.surface_attrs(fbx, n), uv, n, pad)
    paint(surf=surf)
    for rn in region_order:
        m = surf.top.get(rn)
        if m is None:
            continue
        rx = rects[rn]
        area = max(1, (rx[3] - rx[1]) * (rx[2] - rx[0]))
        notes.append(f"    surface {rn:13s}: {int(m.sum()):7d} top-facing texels "
                     f"({100.0 * m.sum() / area:5.1f}% of its rectangle), widest "
                     f"inscribed circle {2 * surf.room[rn].max():.0f}px")

    # ---- the ornament plan, once
    places = plan_placements(style, uv, surf, n, pad, symbols_dir, seed, notes)

    def carve(report=None):
        """Everything that is added to the base material's height, in one place.

        It has to be one place because bronze paints TWICE -- the second time
        with the carvings' own cavity feeding the patina -- and paint() ASSIGNS
        height rather than adding to it. The first version of this had the
        board-space running ornament applied before the bronze re-paint and the
        stamps applied after, so bronze silently lost its frame moulding and its
        edge dentils: 513 kB of normal map against steel's 5.5 MB, which is what
        a flat map compresses to."""
        for rn, fn, amp in (("frame", frame_relief, 0.42), ("sides", side_relief, 0.30)):
            if rn not in inner:
                continue
            ix0, iy0, ix1, iy1 = inner[rn]
            sl = (slice(iy0, iy1), slice(ix0, ix1))
            r = fn(surf, n)[sl] * surf.cov[sl]
            height[sl] = height[sl] + amp * r
            if report is not None:
                report.append(
                    f"    {rn}: board-space running ornament, pitch "
                    f"{RUN_PITCH * 1000:.0f} mm ({2 * HALF_LONG / RUN_PITCH:.0f} "
                    f"repeats on the long edge, {2 * HALF_SHORT / RUN_PITCH:.0f} on "
                    f"the short), covering {100.0 * (r > 0.02).mean():.1f}% of the "
                    f"region rectangle")
        for p in sorted(places, key=lambda q: q.region or ""):
            stamp(height, p, inner.get(p.region, (0, 0, n, n)), symbols_dir, seed,
                  report if report is not None else [])

    carve(notes)
    notes.append(f"    {len(places)} motifs placed "
                 f"({sum(1 for p in places if not p.derived)} at json symbols[] "
                 f"anchors, {sum(1 for p in places if p.derived)} found by measuring "
                 f"the surface)")

    # ---- bronze only: let the verdigris find the carvings, which is where it
    # actually collects. This is a real mechanism, not a look, and it is why the
    # bronze board's ornament does not read as a decal sitting on top.
    cav = _cav(height, n)
    if style == "bronze":
        paint(cavity_bias=cav, surf=surf)
        carve()
        cav = _cav(height, n)
        pat = B.last_patina_coverage(style)
        if pat is not None:
            notes.append(f"    bronze: patina covers {pat * 100:.1f}% of the atlas; "
                         f"cavity enrichment under it {B.last_patina_in_cavity():.2f}x "
                         f"(1.00x would mean the patina ignores the relief)")
        # ...and the two numbers those two CANNOT give. A coverage fraction and a
        # ratio of means both passed while the board came back covered in hard
        # black pepper: neither has a term for spatial frequency or edge
        # hardness, which is the whole defect. See tex_bases._patina_shape for
        # what these measure and what they still cannot see.
        shapes = B.last_patina_shape(style)
        if shapes:
            notes.append(f"    bronze patina SHAPE "
                         f"(1 texel = {shapes[0]['texel_mm']:.2f} mm):")
            for sh in shapes:
                lab = "crust" if sh["thresh"] >= 0.5 else "visible"
                for z in ("flat", "cavity"):
                    notes.append(
                        f"      {lab:7s} >{sh['thresh']:.2f}  {z:6s} covers "
                        f"{sh[z + '_cover'] * 100:5.2f}% of that zone; mean feature width "
                        f"{sh[z + '_width_px']:5.1f} px ({sh[z + '_width_mm']:5.2f} mm); "
                        f"{sh[z + '_edge_frac'] * 100:5.1f}% within 3 texels of a shoreline")
            notes.append(f"      shoreline ramps over {shapes[0]['shore_px']:.1f} texels "
                         f"({shapes[0]['shore_mm']:.2f} mm) -- 1 texel would be a hard "
                         f"threshold, i.e. an aliased edge")

    # ---- the ONLY height-derived term allowed into albedo: light-independent
    # cavity, applied as a palette-locked darkening.
    dark = {s: B.PALETTES[s][0].map(np.zeros(1))[0] for s in B.PALETTES}[style]
    k = (cav * 0.55)[..., None]
    albedo = albedo * (1.0 - k) + dark[None, None, :] * k
    rough = np.clip(rough + cav * 0.10, 0.05, 0.98)

    normal = T.normal_from_height(height, strength=normal_strength)
    ao = np.clip(1.0 - cav * 0.85, 0.0, 1.0)

    # ---- TWO PACKED MAPS, because there are two consumers and they disagree
    # about the channel order. Writing one file and hoping is how four rounds of
    # this rebuild were judged against a map the game never bound.
    #
    #   <base>_mr.png   glTF ORM: R = occlusion, G = roughness, B = metallic.
    #                   Read by tex_render.py's Principled path. NOT installed --
    #                   nothing in the bundle binds it (see tex_build.INSTALL_SUFFIXES).
    #   <base>_mrs.png  BoardLit _MRSMap: R = metallic, G = roughness, B = 0.
    #                   This is the one the shipped material samples.
    mr = np.stack([ao, np.clip(rough, 0, 1), np.clip(metal, 0, 1)], axis=-1)
    mrs = np.stack([np.clip(metal, 0, 1),
                    np.clip(np.maximum(rough, MRS_ROUGH_FLOOR), 0, 1),
                    np.zeros_like(rough)], axis=-1)
    notes.append(f"    MRS pack (BoardLit R=metal G=rough B=0): metal mean "
                 f"{mrs[..., 0].mean():.3f}, rough mean {mrs[..., 1].mean():.3f} "
                 f"(floor {MRS_ROUGH_FLOOR:.2f} raised {100.0 * (rough < MRS_ROUGH_FLOOR).mean():.1f}% "
                 f"of texels; narrowest lobe half-angle {lobe_half_angle(MRS_ROUGH_FLOOR):.1f} deg)")

    # ---- PADDING. Every texel outside the islands takes the value of the
    # nearest island texel, for all four maps coherently.
    dist = T.dilate_fill([albedo, normal, mr, mrs], content)
    notes.append(f"    padding: {int((~content).sum())} texels outside the islands "
                 f"filled from the nearest island; the widest fill ran "
                 f"{dist[~content].max():.0f}px")

    return albedo, normal, mr, mrs, labels, region_order, notes


# --------------------------------------------------------------------------
# driver
# --------------------------------------------------------------------------

def write_style(style, out_dir, albedo, normal, mr, mrs):
    base = STYLE_FILES[style]["base"]
    paths = [
        T.save_rgb(os.path.join(out_dir, f"{base}_albedo.png"), albedo),
        T.save_rgb(os.path.join(out_dir, f"{base}_normal.png"), normal),
        T.save_rgb(os.path.join(out_dir, f"{base}_mr.png"), mr),
        T.save_rgb(os.path.join(out_dir, f"{base}_mrs.png"), mrs),
    ]
    return paths


def main():
    ap = argparse.ArgumentParser(description="composite the board texture atlases")
    ap.add_argument("--styles", default="oak,steel,bronze")
    ap.add_argument("--out", default="unity/board-prep/out/tex")
    ap.add_argument("--uv-dir", default="unity/board-prep/out")
    ap.add_argument("--motifs", default="unity/board-prep/out",
                    help="directory holding motifs_a/ and motifs_b/, the two "
                         "processed sheets; the per-style set is assembled from "
                         "them by STYLE_MOTIFS")
    ap.add_argument("--symbols", default=None,
                    help="override: use ONE motif directory for every style "
                         "instead of assembling per style")
    ap.add_argument("--fbx-dir", default=BUNDLE_DIR,
                    help="where the boards are, for measuring the real UV surface")
    ap.add_argument("--size", type=int, default=T.N_DEFAULT)
    ap.add_argument("--seed", type=int, default=20260825)
    ap.add_argument("--pad", type=int, default=PAD)
    ap.add_argument("--no-check", action="store_true")
    a = ap.parse_args()

    if a.pad < MIN_PAD:
        ap.error(f"--pad {a.pad} is below the contract floor of {MIN_PAD}px")

    os.makedirs(a.out, exist_ok=True)
    worst_all = float("inf")
    all_ok = True
    for style in a.styles.split(","):
        style = style.strip()
        print(f"\n=== {style} ===")
        uv, src = load_uv(style, out_dir=a.uv_dir)
        print(f"  region map: {src}")
        if a.symbols:
            sym, chosen = a.symbols, {}
        else:
            sym, chosen = style_motifs(style, a.motifs, a.motifs)
        if chosen:
            print(f"  motifs: {sym}")
            for k in sorted(chosen):
                print(f"    {k:16s} <- {chosen[k]}")
        fbx = os.path.join(a.fbx_dir, STYLE_FILES[style]["fbx"])
        alb, nrm, mr, mrs, labels, names, notes = build_style(
            style, uv, n=a.size, seed=a.seed, symbols_dir=sym, pad=a.pad, fbx=fbx)
        for ln in notes:
            print(ln)
        for p in write_style(style, a.out, alb, nrm, mr, mrs):
            print(f"  WROTE {p}  {os.path.getsize(p)} bytes")
        if not a.no_check:
            print(f"  seam check (islands = region rects inset by {a.pad}px):")
            worst, ok = S.check(labels, names, pad_required=MIN_PAD)
            S.check_bleed(alb, labels, pad_required=MIN_PAD)
            worst_all = min(worst_all, worst)
            all_ok &= ok

    if not a.no_check:
        print(f"\nWORST SEAM SEPARATION ACROSS ALL STYLES: {worst_all:.2f} px "
              f"(contract floor {MIN_PAD} px) -- {'PASS' if all_ok else 'FAIL'}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
