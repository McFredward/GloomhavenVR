#!/usr/bin/env python3
"""gen_uvcheck.py -- prove the atlas layout instead of asserting it.

Two products, both cheap and both worth having:

  1. `<style>_uvcheck.png`  -- the UV layout rasterised at the real atlas resolution.
     Every pixel counts how many faces cover it, so ANY overlap shows up as a hard number
     ("overlapping texels: N"), not as something to squint at.  Islands are drawn in their
     region's colour, and the six contract regions are outlined, so a mis-assigned face is
     visible as the wrong colour in the wrong rectangle.

  2. `<style>_checker.png`  -- a numbered check atlas to feed straight back through
     gen_render.py.  Each region gets its own hue plus a numbered checker; every symbol
     from the uv json gets a white cross-hair.  Render the board with it and the seat
     glyph cross-hairs must land in the seats, the slot roses in the card recesses, and so
     on.  That is the step that catches "the JSON says one thing and the mesh does another".

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_uvcheck.py -- <style> [<style> ...]
"""

import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
TABLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")
OUT = os.path.join(HERE, "out")
FBX = {"oak": "PlayTray_prepped.fbx",
       "steel": "PlayTray_9capjqp6.fbx",
       "bronze": "PlayTray_16vm268h.fbx"}

HUE = {
    "face":         (0.16, 0.42, 0.78),
    "frame":        (0.86, 0.55, 0.13),
    "slot_floor":   (0.22, 0.70, 0.36),
    "rest_pads":    (0.80, 0.24, 0.32),
    "button_seats": (0.62, 0.32, 0.80),
    "sides":        (0.40, 0.42, 0.46),
}

# 5x7 block digits: enough to number the regions on the check atlas without a font file.
GLYPH = {
    "0": ["11111", "10001", "10001", "10001", "10001", "10001", "11111"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["11111", "00001", "00001", "11111", "10000", "10000", "11111"],
    "3": ["11111", "00001", "00001", "11111", "00001", "00001", "11111"],
    "4": ["10001", "10001", "10001", "11111", "00001", "00001", "00001"],
    "5": ["11111", "10000", "10000", "11111", "00001", "00001", "11111"],
    "6": ["11111", "10000", "10000", "11111", "10001", "10001", "11111"],
}


def raster(uvs, tris, n, mask=None):
    """Rasterise UV triangles at n x n.

    NOTE ON THE INSTRUMENT ITSELF: the first version of this counted coverage PER TRIANGLE
    and reported 241 616 "overlapping texels" on the oak board -- 18 % of everything it
    covered.  That was the rasteriser, not the mesh: a pixel that lands exactly on the edge
    two triangles share passes the barycentric test in BOTH, so every internal edge in a
    12 000-triangle mesh printed a one-pixel line of false overlap.  The question is whether
    two ISLANDS overlap, so islands are rasterised one at a time into a boolean mask (union
    inside an island, so shared edges cost nothing) and the masks are summed."""
    out = np.zeros((n, n), bool) if mask is None else mask
    for (a, b, c) in tris:
        p0, p1, p2 = uvs[a], uvs[b], uvs[c]
        x0 = max(0, int(math.floor(min(p0[0], p1[0], p2[0]) * n)))
        x1 = min(n - 1, int(math.ceil(max(p0[0], p1[0], p2[0]) * n)))
        y0 = max(0, int(math.floor(min(p0[1], p1[1], p2[1]) * n)))
        y1 = min(n - 1, int(math.ceil(max(p0[1], p1[1], p2[1]) * n)))
        if x1 < x0 or y1 < y0:
            continue
        xs = (np.arange(x0, x1 + 1) + 0.5) / n
        ys = (np.arange(y0, y1 + 1) + 0.5) / n
        px, py = np.meshgrid(xs, ys)
        d = ((p1[1] - p2[1]) * (p0[0] - p2[0]) + (p2[0] - p1[0]) * (p0[1] - p2[1]))
        if abs(d) < 1e-14:
            continue
        w0 = ((p1[1] - p2[1]) * (px - p2[0]) + (p2[0] - p1[0]) * (py - p2[1])) / d
        w1 = ((p2[1] - p0[1]) * (px - p2[0]) + (p0[0] - p2[0]) * (py - p2[1])) / d
        w2 = 1.0 - w0 - w1
        m = (w0 >= -1e-9) & (w1 >= -1e-9) & (w2 >= -1e-9)
        if not m.any():
            continue
        out[y0:y1 + 1, x0:x1 + 1] |= m
    return out


def write_png(path, rgb):
    import zlib
    import struct
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(h))

    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xffffffff)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 6))
           + chunk(b"IEND", b""))
    open(path, "wb").write(png)


def stamp(img, text, x, y, scale, col):
    for k, ch in enumerate(text):
        g = GLYPH.get(ch)
        if not g:
            continue
        for r, row in enumerate(g):
            for c, bit in enumerate(row):
                if bit != "1":
                    continue
                yy = y + r * scale
                xx = x + (k * 6 + c) * scale
                img[max(0, yy):yy + scale, max(0, xx):xx + scale] = col


def main():
    import bpy
    import bmesh
    args = sys.argv[sys.argv.index("--") + 1:]
    styles = args or ["oak", "steel", "bronze"]
    for style in styles:
        doc = json.load(open(os.path.join(OUT, "%s_uv.json" % style)))
        n = doc["atlas"]
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=os.path.join(TABLE, FBX[style]))
        ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        uvl = bm.loops.layers.uv.active
        bm.faces.ensure_lookup_table()
        uvs, face_tris = [], []
        for f in bm.faces:
            idx = []
            for lp in f.loops:
                idx.append(len(uvs))
                uvs.append((lp[uvl].uv.x, lp[uvl].uv.y))
            face_tris.append([(idx[0], idx[k], idx[k + 1]) for k in range(1, len(idx) - 1)])

        # union-find the faces into real UV islands: two faces joined across an edge only
        # when BOTH of that edge's vertices carry the same UV in both faces (i.e. no seam)
        parent = list(range(len(bm.faces)))

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        for e in bm.edges:
            if len(e.link_faces) != 2:
                continue
            fa, fb = e.link_faces
            ok = True
            for v in e.verts:
                ua = next(lp[uvl].uv for lp in fa.loops if lp.vert is v)
                ub = next(lp[uvl].uv for lp in fb.loops if lp.vert is v)
                if abs(ua.x - ub.x) > 1e-6 or abs(ua.y - ub.y) > 1e-6:
                    ok = False
                    break
            if ok:
                ra, rb = find(fa.index), find(fb.index)
                if ra != rb:
                    parent[ra] = rb

        groups = {}
        for fi in range(len(bm.faces)):
            groups.setdefault(find(fi), []).append(fi)
        bm.free()

        # One island-ID image, then the padding is measured ON THE PIXELS.
        #
        # The first attempt measured island BOUNDING BOXES against each other and reported a
        # -245 px "gap".  That was the instrument again: the frame band is a RING, and the
        # forty ornament islands are deliberately packed inside its bbox's empty middle, so
        # box-vs-box says they interpenetrate while not one texel is shared.  Boxes cannot
        # answer this question; pixels can.
        cover = np.zeros((n, n), np.int32)
        ids = np.full((n, n), -1, np.int32)
        for gi, fis in enumerate(groups.values()):
            m = np.zeros((n, n), bool)
            for fi in fis:
                raster(uvs, face_tris[fi], n, m)
            cover += m
            ids[m] = gi
        overlap = int((cover > 1).sum())

        # Smallest Chebyshev distance at which two DIFFERENT islands meet.  Shifting the id
        # image over the whole 17x17 neighbourhood is 289 whole-atlas comparisons, which is
        # a few seconds and needs no scipy.
        min_gap = None          # None == nothing found within the 8 px search radius
        for k in range(1, 9):
            hit = False
            for dy in range(-k, k + 1):
                for dx in range(-k, k + 1):
                    if max(abs(dx), abs(dy)) != k:
                        continue
                    a = ids[max(0, dy):n + min(0, dy), max(0, dx):n + min(0, dx)]
                    b = ids[max(0, -dy):n + min(0, -dy), max(0, -dx):n + min(0, -dx)]
                    if bool(((a >= 0) & (b >= 0) & (a != b)).any()):
                        hit = True
                        break
                if hit:
                    break
            if hit:
                min_gap = k - 1
                break
        gap_txt = (">= 8 px (nothing within the search radius)" if min_gap is None
                   else "%d px  <-- BELOW THE CONTRACT FLOOR" % min_gap)
        used = int((cover > 0).sum())
        outside = sum(1 for u, v in uvs if u < -1e-6 or u > 1 + 1e-6 or v < -1e-6 or v > 1 + 1e-6)

        # which contract region does each covered pixel fall in?
        img = np.zeros((n, n, 3), np.uint8)
        img[:, :] = (18, 18, 22)
        ys, xs = np.nonzero(cover > 0)
        for name, r in doc["regions"].items():
            col = np.array([int(255 * c) for c in HUE[name]], np.uint8)
            sel = ((xs >= r["u0"] * n) & (xs < r["u1"] * n)
                   & (ys >= r["v0"] * n) & (ys < r["v1"] * n))
            img[ys[sel], xs[sel]] = col
        oy, ox = np.nonzero(cover > 1)
        img[oy, ox] = (255, 0, 0)
        # region outlines
        for name, r in doc["regions"].items():
            x0, x1 = int(r["u0"] * n), int(r["u1"] * n) - 1
            y0, y1 = int(r["v0"] * n), int(r["v1"] * n) - 1
            img[y0:y1, x0:x0 + 2] = (240, 240, 240)
            img[y0:y1, x1 - 1:x1 + 1] = (240, 240, 240)
            img[y0:y0 + 2, x0:x1] = (240, 240, 240)
            img[y1 - 1:y1 + 1, x0:x1] = (240, 240, 240)
        write_png(os.path.join(OUT, "%s_uvcheck.png" % style), img[::-1].copy())

        # ---------------------------------------------------------- check atlas --
        chk = np.zeros((n, n, 3), np.uint8)
        chk[:, :] = (12, 12, 14)
        gx, gy = np.meshgrid(np.arange(n), np.arange(n))
        checker = (((gx // 64) + (gy // 64)) % 2).astype(np.float32) * 0.30 + 0.55
        fine = (((gx // 8) + (gy // 8)) % 2).astype(np.float32) * 0.12 + 0.94
        for k, (name, r) in enumerate(sorted(doc["regions"].items())):
            x0, x1 = int(r["u0"] * n), int(r["u1"] * n)
            y0, y1 = int(r["v0"] * n), int(r["v1"] * n)
            base = np.array(HUE[name], np.float32)
            tile = checker[y0:y1, x0:x1] * fine[y0:y1, x0:x1]
            chk[y0:y1, x0:x1] = np.clip(tile[..., None] * base * 255.0, 0, 255).astype(np.uint8)
            stamp(chk, str(k + 1), x0 + 24, y0 + 24, 8, (255, 255, 255))
        for s in doc["symbols"]:
            cx, cy = int(s["u"] * n), int(s["v"] * n)
            rr = max(6, int(s["size_uv"] * n / 2))
            chk[max(0, cy - 3):cy + 3, max(0, cx - rr):cx + rr] = (255, 255, 255)
            chk[max(0, cy - rr):cy + rr, max(0, cx - 3):cx + 3] = (255, 255, 255)
        write_png(os.path.join(OUT, "%s_checker.png" % style), chk[::-1].copy())

        print("[uvcheck] %-6s  %d UV islands  covered %d px (%.1f%% of the atlas)  "
              "OVERLAPPING TEXELS %d  uvs outside 0..1: %d  min island gap %s"
              % (style, len(groups), used, 100.0 * used / (n * n), overlap, outside, gap_txt))
        for name, r in doc["regions"].items():
            sel = ((xs >= r["u0"] * n) & (xs < r["u1"] * n)
                   & (ys >= r["v0"] * n) & (ys < r["v1"] * n))
            # Smallest distance from any covered texel to the wall of its own rectangle.
            # The contract's rectangles abut, so this margin IS half the inter-region
            # gutter; the texture-lane compositor needs >= 12 px of it.
            if int(sel.sum()):
                rx0, rx1 = r["u0"] * n, r["u1"] * n
                ry0, ry1 = r["v0"] * n, r["v1"] * n
                mx = min(int((xs[sel] - rx0).min()), int((rx1 - 1 - xs[sel]).max() and
                                                         (rx1 - 1 - xs[sel]).min()))
                my = min(int((ys[sel] - ry0).min()), int((ry1 - 1 - ys[sel]).min()))
                marg = min(mx, my)
            else:
                marg = -1
            print("           region %-13s %6d px  (%.1f%% of its rect)  margin to rect wall %d px%s"
                  % (name, int(sel.sum()),
                     100.0 * int(sel.sum()) / max(1, (r["u1"] - r["u0"]) * (r["v1"] - r["v0"]) * n * n),
                     marg, "" if marg >= 12 else "   <-- BELOW THE 12 px COMPOSITOR MARGIN"))


if __name__ == "__main__":
    main()
