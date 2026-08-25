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

import numpy as np
from PIL import Image

import tex_bases as B
import tex_common as T
import tex_seams as S
import tex_symbols as SY


PAD = 12          # inset per region edge -> 2*PAD gutter between neighbours
MIN_PAD = 8       # the contract's floor

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


def derived_placements(uv, n, pad):
    """The ornament the LAYOUT implies, computed from the region rectangles
    themselves so it is correct for whatever the mesh lane writes.

    Anything the mesh lane names in symbols[] overrides the derived placement of
    the same motif in the same region -- the json is authoritative where it
    speaks, and this fills in where it is silent."""
    out = []
    R = {k: T.region_rect_px(v, n) for k, v in uv["regions"].items()}

    def inner(box):
        x0, y0, x1, y1 = box
        return x0 + pad, y0 + pad, x1 - pad, y1 - pad

    # ---- face: hero rosette, punctuation, and a tick at each card-recess corner
    if "face" in R:
        x0, y0, x1, y1 = inner(R["face"])
        w, h = x1 - x0, y1 - y0
        s = int(min(w, h) * 0.42)
        out.append(Placement("centre_rose", (x0 + x1) / 2, (y0 + y1) / 2, s, s, -0.55, "face"))
        pip = int(min(w, h) * 0.075)
        for fx in (0.16, 0.84):
            out.append(Placement("pip", x0 + w * fx, (y0 + y1) / 2, pip, pip, -0.35, "face"))
        tick = int(min(w, h) * 0.11)
        for sx0, sx1 in ((0.055, 0.335), (0.665, 0.945)):
            for i, (fx, fy) in enumerate(((sx0, 0.14), (sx1, 0.14), (sx0, 0.86), (sx1, 0.86))):
                out.append(Placement("slot_corner", x0 + w * fx, y0 + h * fy,
                                     tick, tick, -0.40, "face", rot=[0, 3, 1, 2][i]))

    # ---- frame: tiled border runs top and bottom, four corner brackets, maker mark
    if "frame" in R:
        x0, y0, x1, y1 = inner(R["frame"])
        w, h = x1 - x0, y1 - y0
        run_h = max(8, int(h * 0.155))
        out.append(Placement("border_run", (x0 + x1) / 2, y0 + run_h / 2, w, run_h, +0.34, "frame"))
        out.append(Placement("border_run", (x0 + x1) / 2, y1 - run_h / 2, w, run_h, +0.34, "frame"))
        br = int(min(w, h) * 0.30)
        for i, (fx, fy) in enumerate(((0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0))):
            out.append(Placement("corner_bracket",
                                 x0 + w * fx + br / 2 * (1 if fx < 0.5 else -1),
                                 y0 + h * fy + br / 2 * (1 if fy < 0.5 else -1),
                                 br, br, +0.40, "frame", rot=[0, 3, 1, 2][i]))
        mm = int(min(w, h) * 0.34)
        out.append(Placement("maker_mark", (x0 + x1) / 2, (y0 + y1) / 2, mm, mm, -0.45, "frame"))

    # ---- slot floors: a tick in each corner, nothing else; cards cover this
    if "slot_floor" in R:
        x0, y0, x1, y1 = inner(R["slot_floor"])
        w, h = x1 - x0, y1 - y0
        tick = int(min(w, h) * 0.16)
        for i, (fx, fy) in enumerate(((0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0))):
            out.append(Placement("slot_corner",
                                 x0 + w * fx + tick / 2 * (1 if fx < 0.5 else -1),
                                 y0 + h * fy + tick / 2 * (1 if fy < 0.5 else -1),
                                 tick, tick, -0.30, "slot_floor", rot=[0, 3, 1, 2][i]))

    # ---- rest pads: the two glyphs, one per pad, side by side on the long axis
    if "rest_pads" in R:
        x0, y0, x1, y1 = inner(R["rest_pads"])
        w, h = x1 - x0, y1 - y0
        horiz = w >= h
        s = int((min(w / 2, h) if horiz else min(w, h / 2)) * 0.68)
        for k, nm in enumerate(("rest_short", "rest_long")):
            cx = x0 + w * (0.25 + 0.5 * k) if horiz else (x0 + x1) / 2
            cy = (y0 + y1) / 2 if horiz else y0 + h * (0.25 + 0.5 * k)
            out.append(Placement(nm, cx, cy, s, s, -0.60, "rest_pads"))

    # ---- button seats: THREE bezels (the contract's seven-anchor change)
    if "button_seats" in R:
        x0, y0, x1, y1 = inner(R["button_seats"])
        w, h = x1 - x0, y1 - y0
        horiz = w >= h
        s = int((min(w / 3, h) if horiz else min(w, h / 3)) * 0.80)
        for k in range(3):
            f = (2 * k + 1) / 6.0
            cx = x0 + w * f if horiz else (x0 + x1) / 2
            cy = (y0 + y1) / 2 if horiz else y0 + h * f
            out.append(Placement("seat_bezel", cx, cy, s, s, -0.50, "button_seats"))

    # ---- sides: a tiled dentil strip
    if "sides" in R:
        x0, y0, x1, y1 = inner(R["sides"])
        w, h = x1 - x0, y1 - y0
        run_h = max(8, int(h * 0.22))
        out.append(Placement("edge_dentil", (x0 + x1) / 2, y0 + run_h / 2, w, run_h, +0.28, "sides"))
        out.append(Placement("edge_dentil", (x0 + x1) / 2, y1 - run_h / 2, w, run_h, +0.28, "sides"))

    return out


def json_placements(uv, n, derived):
    """Turn symbols[] into placements, replacing the derived entry of the same
    motif in the same region where one exists."""
    out = list(derived)
    for e in uv.get("symbols", []):
        name = e["name"]
        if name not in SY.SYMBOLS:
            print(f"  NOTE symbols[] names {name!r}, which is not in the symbol spec; "
                  f"skipped")
            continue
        region = e.get("region")
        cx, cy = T.uv_to_px(float(e["u"]), float(e["v"]), n)
        size = e.get("size_uv")
        if size is None:
            w = h = SY.SYMBOLS[name]["px"]
        else:
            if isinstance(size, (list, tuple)):
                w, h = float(size[0]) * n, float(size[1]) * n
            else:
                w = h = float(size) * n
        depth = float(e.get("depth", -0.55))
        out = [p for p in out if not (p.derived and p.name == name and p.region == region)]
        out.append(Placement(name, cx, cy, w, h, depth, region,
                             rot=int(e.get("rot", 0)), derived=False))
    return out


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


def build_style(style, uv, n=T.N_DEFAULT, seed=20260825, symbols_dir=None,
                pad=PAD, normal_strength=ORNAMENT_NORMAL_STRENGTH):
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

    def paint(cavity_bias=None):
        for name in region_order:
            tr = B.REGION_TREATMENT.get(name, B.REGION_TREATMENT["sides"])
            f = B.build(style, n, seed, orient=tr["orient"], detail=tr["detail"],
                        cavity_bias=cavity_bias)
            ix0, iy0, ix1, iy1 = inner[name]
            sl = (slice(iy0, iy1), slice(ix0, ix1))
            albedo[sl] = f.albedo[sl]
            height[sl] = (f.height[sl] * tr["height"] * BASE_HEIGHT
                          * STYLE_HEIGHT.get(style, 1.0))
            rough[sl] = np.clip(f.rough[sl] + tr["rough"], 0.05, 0.98)
            metal[sl] = f.metal[sl]
            if tr["tone"]:
                # tone offsets stay INSIDE the palette: darken toward the ramp's
                # own dark end rather than multiplying toward black
                k = abs(tr["tone"])
                dark = B.PALETTES[style][0].map(np.zeros(1))[0]
                albedo[sl] = albedo[sl] * (1.0 - k) + dark[None, None, :] * k
        return None

    paint()

    # ---- carve the ornament
    places = json_placements(uv, n, derived_placements(uv, n, pad))
    clip_report = []
    for p in sorted(places, key=lambda q: q.region or ""):
        box = inner.get(p.region)
        if box is None:
            box = (0, 0, n, n)
        stamp(height, p, box, symbols_dir, seed, clip_report)
    notes.extend(clip_report)
    notes.append(f"    {len(places)} motifs placed "
                 f"({sum(1 for p in places if not p.derived)} from symbols[], "
                 f"{sum(1 for p in places if p.derived)} derived from the region rects)")

    # ---- bronze only: let the verdigris find the carvings, which is where it
    # actually collects. This is a real mechanism, not a look, and it is why the
    # bronze board's ornament will not read as a decal sitting on top.
    cav = _cav(height, n)
    if style == "bronze":
        paint(cavity_bias=cav * 0.55)
        for p in sorted(places, key=lambda q: q.region or ""):
            stamp(height, p, inner.get(p.region, (0, 0, n, n)), symbols_dir, seed, [])
        cav = _cav(height, n)

    # ---- the ONLY height-derived term allowed into albedo: light-independent
    # cavity, applied as a palette-locked darkening.
    dark = {s: B.PALETTES[s][0].map(np.zeros(1))[0] for s in B.PALETTES}[style]
    k = (cav * 0.55)[..., None]
    albedo = albedo * (1.0 - k) + dark[None, None, :] * k
    rough = np.clip(rough + cav * 0.10, 0.05, 0.98)

    normal = T.normal_from_height(height, strength=normal_strength)
    ao = np.clip(1.0 - cav * 0.85, 0.0, 1.0)
    mr = np.stack([ao, np.clip(rough, 0, 1), np.clip(metal, 0, 1)], axis=-1)

    # ---- PADDING. Every texel outside the islands takes the value of the
    # nearest island texel, for all three maps coherently.
    dist = T.dilate_fill([albedo, normal, mr], content)
    notes.append(f"    padding: {int((~content).sum())} texels outside the islands "
                 f"filled from the nearest island; the widest fill ran "
                 f"{dist[~content].max():.0f}px")

    return albedo, normal, mr, labels, region_order, notes


# --------------------------------------------------------------------------
# driver
# --------------------------------------------------------------------------

def write_style(style, out_dir, albedo, normal, mr):
    base = STYLE_FILES[style]["base"]
    paths = [
        T.save_rgb(os.path.join(out_dir, f"{base}_albedo.png"), albedo),
        T.save_rgb(os.path.join(out_dir, f"{base}_normal.png"), normal),
        T.save_rgb(os.path.join(out_dir, f"{base}_mr.png"), mr),
    ]
    return paths


def main():
    ap = argparse.ArgumentParser(description="composite the board texture atlases")
    ap.add_argument("--styles", default="oak,steel,bronze")
    ap.add_argument("--out", default="unity/board-prep/out/tex")
    ap.add_argument("--uv-dir", default="unity/board-prep/out")
    ap.add_argument("--symbols", default="unity/board-prep/out/symbols",
                    help="processed motif files; missing motifs fall back to the "
                         "procedural stand-in, so the pipeline runs today")
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
        alb, nrm, mr, labels, names, notes = build_style(
            style, uv, n=a.size, seed=a.seed, symbols_dir=a.symbols, pad=a.pad)
        for ln in notes:
            print(ln)
        for p in write_style(style, a.out, alb, nrm, mr):
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
