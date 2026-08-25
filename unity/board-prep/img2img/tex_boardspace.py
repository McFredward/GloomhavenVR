#!/usr/bin/env python3
"""Reconstruct a board's FRONT-FACE albedo in BOARD SPACE from its shipped atlas.

    python3 tex_boardspace.py --geo <geobufdir> --albedo <atlas albedo> \
        --out <2048x1024 png> [--report <json>]

WHY THIS EXISTS
---------------
`tex_backfill.py` needs a `--front-board` image: the front face's albedo laid out in the
board's own (long, short) coordinates, which it walks inward from each rim texel to build
the rim blend's front term.  Round 3 handed it `tex_composite`'s `_board.png`
intermediate.  That file is in `out/`, which is gitignored, and it is gone -- so the round
that had to repaint the rim (ModBuild 280, the side relief) could not reproduce its input.

Regenerating it would mean re-running the whole front composite, which is exactly the
thing this round must not touch.  So the front board image is taken from the SHIPPED ATLAS
instead, through the mesh's own geobuf: every FRONT and INTERIOR texel already carries the
object-space position of the surface point that samples it, so scattering the atlas colour
to (x, y) reproduces the front face as the player actually sees it today.

THAT IS BETTER THAN THE ORIGINAL INPUT, NOT A FALLBACK, and for a reason the rim blend's
own docstring states: at t = 0 the blend's first term is `front(p)`, "the very texel its
front neighbour has".  Reading the front term back out of the shipped atlas makes that
sentence exactly true instead of approximately true -- the two terms are now the same
bytes, not two renderings of the same intent.

WHY INTERIOR IS INCLUDED.  `gen_geobuf` calls a recess floor INTERIOR, not FRONT, but a
recess floor is part of the picture a front camera sees and `tex_composite`'s board image
had it.  Dropping it would leave 2.0-5.0 cm^2 holes in the middle of the board for
push-pull to invent, and the rim's inward walk crosses the left rest panel on every style.

The unseen remainder -- the board's own rounded corners outside the front silhouette, and
whatever the two groups do not cover -- is filled by `tex_composite.pushpull_fill`, the
same dilation the front pass uses, and the coverage fraction is REPORTED rather than
assumed: if it ever drops far below the ~99 % this measures, the rim is being built out of
invented texels and the number says so.

WHY THE SCATTER IS 1024x512 AND NOT THE 2048x1024 IT RETURNS.  The atlas carries the front
face at 1.89-2.05 texels/mm; a 2048x1024 board raster is 3.2 px/mm, so scattering straight
into it leaves 63 % of the pixels EMPTY -- measured, oak 0.3726 covered -- in a regular
lattice that push-pull then turns into a nearest-neighbour upsample of the front albedo.
Scattering at the resolution the data actually has (1.6 px/mm, oak 0.8914 covered, holes
one pixel wide) and resampling up is the same information without the lattice.  Measured
across the ladder: 2048 0.373, 1536 0.543, 1280 0.686, 1024 0.891, 896 0.985.  It never
reaches 1.000 at any resolution and that is correct rather than a defect -- the outer
~3 mm of the board is RIM, which is the thing being painted, not an input to it.
"""

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tex_composite import pushpull_fill        # noqa: E402  (same directory, by design)

Image.MAX_IMAGE_PIXELS = None

PLATE_W, PLATE_H = 2048, 1024
GRP_FRONT, GRP_BACK, GRP_RIM, GRP_INT = 1, 2, 3, 4


def run(a):
    pos = np.load(os.path.join(a.geo, "pos.npy")).astype(np.float64)
    grp = np.load(os.path.join(a.geo, "grp.npy"))
    alb = np.asarray(Image.open(a.albedo).convert("RGB"), np.float32)

    mapped = grp > 0
    flat = pos[mapped]
    lo, hi = flat.min(0), flat.max(0)
    ext = hi - lo
    thin = int(np.argmin(ext))
    rest = [i for i in range(3) if i != thin]
    lng, srt = (rest[0], rest[1]) if ext[rest[0]] >= ext[rest[1]] else (rest[1], rest[0])

    sel = (grp == GRP_FRONT) | (grp == GRP_INT)
    # tex_backfill's own board coordinates: U along the long axis, V flipped so row 0 is
    # the +short edge.  Identical formulas, so a rim texel's inward walk lands on the same
    # pixel this scatter wrote.
    U = (pos[..., lng] - lo[lng]) / max(ext[lng], 1e-9)
    V = 1.0 - (pos[..., srt] - lo[srt]) / max(ext[srt], 1e-9)

    sw, sh = a.scatter, a.scatter // 2
    xi = np.clip(np.rint(U[sel] * (sw - 1)).astype(np.int64), 0, sw - 1)
    yi = np.clip(np.rint(V[sel] * (sh - 1)).astype(np.int64), 0, sh - 1)
    idx = yi * sw + xi
    acc = np.zeros((sw * sh, 3), np.float64)
    cnt = np.zeros(sw * sh, np.float64)
    np.add.at(acc, idx, alb[sel].astype(np.float64))
    np.add.at(cnt, idx, 1.0)
    seen = cnt > 0
    acc[seen] /= cnt[seen][:, None]
    img = acc.reshape(sh, sw, 3).astype(np.float32)
    filled = seen.reshape(sh, sw)
    cov = float(filled.mean())

    out, _ = pushpull_fill(img, filled)
    im = Image.fromarray(np.clip(np.rint(out), 0, 255).astype(np.uint8))
    if (sw, sh) != (PLATE_W, PLATE_H):
        im = im.resize((PLATE_W, PLATE_H), Image.LANCZOS)
    im.save(a.out)

    stats = {"style": a.style, "front_texels": int((grp == GRP_FRONT).sum()),
             "interior_texels": int((grp == GRP_INT).sum()),
             "scatter": [sw, sh],
             "board_px_covered": int(filled.sum()),
             "board_px_coverage": round(cov, 5),
             "long_axis": "xyz"[lng], "short_axis": "xyz"[srt],
             "thickness_axis": "xyz"[thin], "out": a.out}
    print(json.dumps(stats, indent=1))
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(stats, fh, indent=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--style", required=True)
    ap.add_argument("--geo", required=True)
    ap.add_argument("--albedo", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default=None)
    ap.add_argument("--scatter", type=int, default=1024,
                    help="board-space scatter width; see the docstring for the ladder")
    run(ap.parse_args())


if __name__ == "__main__":
    main()
