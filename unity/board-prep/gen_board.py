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


def rrect(cx, cy, w, h, r, nc, nx, ny):
    """Rounded rectangle, CCW seen from +Z, with a FIXED vertex count of 2*nx+2*ny+4*nc.

    The fixed count is what makes an inset trivial: `rrect(cx, cy, w-2d, h-2d, r-d, ...)`
    has a 1:1 vertex correspondence with the original, so a ring bridge between them is
    pure quads with no bridging heuristics involved."""
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

    def run(px, py, qx, qy, n):
        for i in range(n):
            t = i / n
            pts.append((px + (qx - px) * t, py + (qy - py) * t))

    run(x0 + r, y0, x1 - r, y0, nx)                       # bottom
    arc(x1 - r, y0 + r, -math.pi / 2, 0.0)                # BR
    run(x1, y0 + r, x1, y1 - r, ny)                       # right
    arc(x1 - r, y1 - r, 0.0, math.pi / 2)                 # TR
    run(x1 - r, y1, x0 + r, y1, nx)                       # top
    arc(x0 + r, y1 - r, math.pi / 2, math.pi)             # TL
    run(x0, y1 - r, x0, y0 + r, ny)                       # left
    arc(x0 + r, y0 + r, math.pi, 1.5 * math.pi)           # BL
    return pts


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
        sil = lambda d: rrect(0, 0, LONG - 2 * d, SHORT - 2 * d, c["corner_r"] - d, nc, nx, ny)

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
        low = self.ring(sil(0.0), -(T - ed))
        self.bridge(widest, low, side_isl)
        bot = self.ring(sil(0.0018), -T)
        self.bridge(low, bot, side_isl)
        self.strip_uv(side_isl, [widest, low, bot], chunk=0.32)
        back = self.new_island("planar", "sides", 0.30, flip_y=True)
        self.fill([bot], back)

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
