#!/usr/bin/env python3
"""Rasterise a board's mesh into ATLAS-SPACE GEOMETRY BUFFERS.

    python3 gen_geobuf.py --dump <style.npz> --style <oak|steel|bronze> --out <dir> [--atlas 2048]

`<style.npz>` is the output of `../tex_uv_dump.py` (triangulated UVs + object-space corner
positions + per-triangle face normals).

WHAT THIS IS FOR
----------------
The ModBuild 274 texture pass authored the FRONT face and nothing else.  It worked by
rendering the board flat-on, generating art at that view, and scattering the result into
the atlas through a UV pass taken from the SAME view.  Every texel no front view can
reach -- the back plate, the rim bevel, the recess walls -- was then filled by
`tex_composite.pushpull_fill`.

For a recess wall that is right: it is one or two texels from its own floor and it really
is made of the same metal.  For the BACK PLATE it is not: the back's island is hundreds of
texels from the nearest authored texel, so push-pull converges to a flat average.  The
user saw exactly that and reported it -- "Die Seiten und die Rückseite die Textur ist
kaputt" -- a featureless grey plate behind, and a smear along the rim.

Measured, per face group, by rasterising the islands of the shipped meshes:

    board  group   tris    surface            atlas cov   texel density
    oak    FRONT   6500    2015.2 cm2 41.8%     18.96%      1.99 tex/mm
    oak    BACK     108    2010.6 cm2 41.7%      0.97%      0.45 tex/mm
    oak    RIM     2012     581.2 cm2 12.1%      3.33%      1.53 tex/mm
    steel  FRONT   5392    2059.2 cm2            17.53%     1.89 tex/mm
    steel  BACK     304    2060.8 cm2             1.10%     0.47 tex/mm
    steel  RIM     1544     546.1 cm2             2.90%     1.49 tex/mm
    bronze FRONT  10476    1888.5 cm2            18.92%     2.04 tex/mm
    bronze BACK     124    2006.7 cm2             1.02%     0.46 tex/mm
    bronze RIM     2092     540.8 cm2             3.82%     1.72 tex/mm

Overlapped texels: ZERO on every board and every group.  So the islands are real and
distinct -- the back and the rim were never mis-unwrapped, they were never AUTHORED.  That
is a different defect from the one the screenshot suggests, and it has a different fix: no
camera pass can reach those faces, so the art has to be addressed by the SURFACE ITSELF.

WHAT IT WRITES  (all at the atlas resolution, all masked to real triangle coverage)
-----------------------------------------------------------------------------------
    pos.npy    (N, N, 3)  float32   object-space position of the surface point at this texel
    nrm.npy    (N, N, 3)  float32   unit surface normal there
    grp.npy    (N, N)     uint8     0 = unmapped, 1 = FRONT, 2 = BACK, 3 = RIM, 4 = INTERIOR
    geobuf.png            preview   group id as colour, for eyeballing the split

Everything is barycentric-interpolated from the triangle that owns the texel, so a texel's
position is the position of the board point that samples it -- the same guarantee the front
lane got from its UV pass, extended to the faces a front view cannot see.  Art placed
through this buffer CANNOT DRIFT, for the same reason: it is addressed by where the surface
is, not by where a picture thinks it is.

WHY THE GROUPS ARE SPLIT THIS WAY
---------------------------------
`thin` is the board's shortest bbox axis (the thickness).  A triangle is FRONT if its
outward normal points along +thin AND its centroid is on the +thin half; BACK is the
mirror of that.  Both tests are needed: the floor of a recess also faces +thin, and it is
not the front plate.  RIM is everything near-perpendicular to the thickness axis whose
centroid lies in the outer 10% of either long axis, i.e. the board's own edge band.
Everything left over is INTERIOR: recess floors and walls, seat rings, the card slots.
"""

import argparse
import os

import numpy as np
from PIL import Image

GROUPS = {0: "unmapped", 1: "FRONT", 2: "BACK", 3: "RIM", 4: "INTERIOR"}
GROUP_COLOUR = {0: (12, 12, 16), 1: (58, 118, 210), 2: (214, 92, 46),
                3: (86, 182, 108), 4: (196, 170, 60)}


def classify(pos, nrm):
    """-> (group id per triangle, thickness axis index).  See the docstring."""
    n = nrm / np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-12)
    flat = pos.reshape(-1, 3)
    lo, hi = flat.min(0), flat.max(0)
    ext = hi - lo
    thin = int(np.argmin(ext))
    mid = (lo + hi) * 0.5
    axial = n[:, thin]
    centre = pos.mean(1)
    off = centre[:, thin] - mid[thin]

    front = (axial > 0.7) & (off > 0)
    back = (axial < -0.7) & (off < 0)
    perp = np.abs(axial) <= 0.5
    outer = np.zeros(len(pos), bool)
    for i in range(3):
        if i != thin:
            outer |= np.abs(centre[:, i] - mid[i]) > 0.45 * ext[i]
    rim = perp & outer

    grp = np.full(len(pos), 4, np.uint8)
    grp[front] = 1
    grp[back] = 2
    grp[rim] = 3
    return grp, thin


def rasterise(tris, pos, nrm, grp, n):
    """Barycentric rasterisation of the UV triangles into atlas-space buffers.

    Triangles are drawn largest-area-first so that where two islands touch, the one that
    actually owns the texel wins rather than whichever came last in the file."""
    P = np.empty_like(tris)
    P[..., 0] = tris[..., 0] * n
    P[..., 1] = (1.0 - tris[..., 1]) * n

    out_pos = np.zeros((n, n, 3), np.float32)
    out_nrm = np.zeros((n, n, 3), np.float32)
    out_grp = np.zeros((n, n), np.uint8)

    area = np.abs((P[:, 1, 0] - P[:, 0, 0]) * (P[:, 2, 1] - P[:, 0, 1]) -
                  (P[:, 2, 0] - P[:, 0, 0]) * (P[:, 1, 1] - P[:, 0, 1]))
    for t in np.argsort(-area):
        a = P[t]
        x0 = max(int(np.floor(a[:, 0].min())), 0)
        x1 = min(int(np.ceil(a[:, 0].max())) + 1, n)
        y0 = max(int(np.floor(a[:, 1].min())), 0)
        y1 = min(int(np.ceil(a[:, 1].max())) + 1, n)
        if x1 <= x0 or y1 <= y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        det = ((a[1, 1] - a[2, 1]) * (a[0, 0] - a[2, 0]) +
               (a[2, 0] - a[1, 0]) * (a[0, 1] - a[2, 1]))
        if abs(det) < 1e-12:
            continue
        l0 = ((a[1, 1] - a[2, 1]) * (X - a[2, 0]) + (a[2, 0] - a[1, 0]) * (Y - a[2, 1])) / det
        l1 = ((a[2, 1] - a[0, 1]) * (X - a[2, 0]) + (a[0, 0] - a[2, 0]) * (Y - a[2, 1])) / det
        l2 = 1.0 - l0 - l1
        # a small negative tolerance closes the one-texel cracks between adjacent
        # triangles that exact edge tests leave behind
        m = (l0 >= -0.002) & (l1 >= -0.002) & (l2 >= -0.002)
        if not m.any():
            continue
        w = np.stack([l0[m], l1[m], l2[m]], -1)
        ys, xs = np.nonzero(m)
        ys = ys + y0
        xs = xs + x0
        out_pos[ys, xs] = (w @ pos[t]).astype(np.float32)
        out_nrm[ys, xs] = nrm[t].astype(np.float32)
        out_grp[ys, xs] = grp[t]
    return out_pos, out_nrm, out_grp


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True, help="npz from ../tex_uv_dump.py")
    ap.add_argument("--style", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--atlas", type=int, default=2048)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    d = np.load(a.dump)
    tris, pos, nrm = d["tris"], d["pos"], d["nrm"]
    grp, thin = classify(pos, nrm)
    n = a.atlas
    P, Nn, G = rasterise(tris, pos, nrm, grp, n)

    np.save(os.path.join(a.out, "pos.npy"), P)
    np.save(os.path.join(a.out, "nrm.npy"), Nn)
    np.save(os.path.join(a.out, "grp.npy"), G)

    prev = np.zeros((n, n, 3), np.uint8)
    for gid, col in GROUP_COLOUR.items():
        prev[G == gid] = col
    Image.fromarray(prev).save(os.path.join(a.out, "geobuf.png"))

    flat = pos.reshape(-1, 3)
    ext = flat.max(0) - flat.min(0)
    print(f"{a.style}: bbox {ext[0]:.4f} x {ext[1]:.4f} x {ext[2]:.4f} m, thickness axis '{'xyz'[thin]}'")
    tot = float((G > 0).mean())
    for gid in (1, 2, 3, 4):
        m = G == gid
        sel = grp == gid
        if not sel.any():
            continue
        e = pos[sel]
        va = e[:, 1] - e[:, 0]
        vb = e[:, 2] - e[:, 0]
        surf = 0.5 * np.linalg.norm(np.cross(va, vb), axis=1).sum()
        dens = np.sqrt(m.sum() / max(surf, 1e-12)) / 1000.0
        print(f"  {GROUPS[gid]:9s} {int(sel.sum()):6d} tris  {surf*1e4:8.1f} cm2  "
              f"{int(m.sum()):8d} texels ({m.mean()*100:5.2f}% of atlas)  {dens:5.2f} tex/mm")
    print(f"  atlas mapped by triangles: {tot*100:.2f}%   -> {a.out}")


if __name__ == "__main__":
    main()
