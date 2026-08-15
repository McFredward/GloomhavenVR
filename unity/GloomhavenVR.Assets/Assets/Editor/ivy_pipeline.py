#!/usr/bin/env python3
"""ivy_alb.png — the cellar's climbing ivy, keyed out of ambientCG LeafSet017.

    python3 Assets/Editor/ivy_pipeline.py <LeafSet017_2K-PNG.zip> \
        Assets/Bundle/Environments/Imported/Textures/ivy_alb.png

SOURCE AND LICENCE (also recorded in Environments/License.md, which is the file
that has to be right):
    ambientCG "Leaf Set 017" — https://ambientcg.com/a/LeafSet017 — an Atlas
    asset, creationMethod PBRPhotogrammetry, tagged {ivy, leaf, leaves, set,
    vine}, released 2020-05-10. CC0 1.0 Universal, stated at
    https://docs.ambientcg.com/license/ : "All ambientCG assets are provided
    under the Creative Commons CC0 1.0 Universal License", with explicit
    permission to "include the raw files in your project, for example a video
    game" and no attribution requirement. Verified 2026-08-15.

WHY THIS ASSET. Poly Haven has no ivy at all (its whole model and texture
indexes were queried for ivy/vine/creeper/climb: one hit, `wine_bottles_01`,
tagged "vineyard"). ambientCG has two ivy sets, 017 and 029; 017 is the one
tagged "vine" as well, and its six sprigs are LEAF CLUSTERS ON A STEM rather
than single detached leaves, which is what a climbing card needs — a card
carrying one leaf reads as one leaf stuck to a wall at any size.

WHAT THIS SCRIPT DOES, and every step is here because of the same trap the
fungus atlas documents: a background texel must never weight into an edge texel.
  1. reads Color + Opacity out of the zip (the raw download is NOT committed);
  2. finds the six sprigs by connected-component analysis of the opacity mask,
     rejecting the nine 1-4 px specks that come with it;
  3. resizes 2048 -> 1024 PREMULTIPLIED, so a transparent texel contributes
     nothing to the average that makes its opaque neighbour;
  4. un-premultiplies, then fills the transparent region by nearest-opaque
     bleed, so mip generation cannot pull a black texel into a leaf edge;
  5. prints the six sub-rects as a C# Rect[] literal, normalised, for
     BuildEnvironmentRooms.IvyCards.

It needs only python3 + numpy + Pillow, deliberately (the repo's other two
pipelines have the same dependency set and no OpenCV — the keying here is a
mask that shipped with the asset, so nothing has to be segmented).
"""
import sys
import zipfile
from collections import deque

import numpy as np
from PIL import Image

MIN_PX = 1000          # a real sprig is ~200k px; the specks are 1-4


def components(mask):
    h, w = mask.shape
    seen = np.zeros((h, w), bool)
    out = []
    for y in range(h):
        for x in range(w):
            if mask[y, x] and not seen[y, x]:
                q = deque([(y, x)])
                seen[y, x] = True
                y0 = y1 = y
                x0 = x1 = x
                n = 0
                while q:
                    cy, cx = q.popleft()
                    n += 1
                    y0, y1 = min(y0, cy), max(y1, cy)
                    x0, x1 = min(x0, cx), max(x1, cx)
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            q.append((ny, nx))
                if n >= MIN_PX:
                    out.append((x0, y0, x1, y1, n))
    return out


def bleed(rgb, a, rounds=64):
    """Nearest-opaque fill of the transparent region, one dilation per round."""
    out = rgb.copy().astype(np.float32)
    known = a > 0.004
    for _ in range(rounds):
        if known.all():
            break
        acc = np.zeros_like(out)
        cnt = np.zeros(a.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            s = np.roll(np.roll(out, dy, 0), dx, 1)
            k = np.roll(np.roll(known, dy, 0), dx, 1).astype(np.float32)
            acc += s * k[..., None]
            cnt += k
        fill = (~known) & (cnt > 0)
        out[fill] = acc[fill] / cnt[fill][..., None]
        known = known | fill
    return out


def main(zip_path, out_path):
    z = zipfile.ZipFile(zip_path)
    col = np.asarray(Image.open(z.open("LeafSet017_2K-PNG_Color.png")).convert("RGB"),
                     np.float32) / 255.0
    opa = np.asarray(Image.open(z.open("LeafSet017_2K-PNG_Opacity.png")).convert("L"),
                     np.float32) / 255.0

    comps = components(opa > 0.5)
    comps.sort(key=lambda c: (c[1] // 700, c[0]))
    if len(comps) != 6:
        raise SystemExit(f"expected 6 ivy sprigs, found {len(comps)} — the asset changed")

    # premultiplied 2:1 box downsample
    pm = col * opa[..., None]
    h, w = opa.shape
    pm = pm.reshape(h // 2, 2, w // 2, 2, 3).mean((1, 3))
    al = opa.reshape(h // 2, 2, w // 2, 2).mean((1, 3))
    rgb = np.where(al[..., None] > 1e-4, pm / np.maximum(al[..., None], 1e-4), 0.0)
    rgb = bleed(rgb, al)

    out = np.clip(np.concatenate([rgb, al[..., None]], 2) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(out, "RGBA").save(out_path, optimize=True)

    n = h // 2
    print(f"{out_path}: {n}x{n} RGBA8, coverage {(al > 0.5).mean() * 100:.1f}%, "
          f"{((al > 0.004) & (al < 0.996)).mean() * 100:.2f}% partially transparent")
    print("        private static readonly Rect[] IvyCards =")
    print("        {")
    for x0, y0, x1, y1, _ in comps:
        # Unity's Rect on a texture has +V UP; the atlas is read top-down.
        fx0, fx1 = x0 / w, (x1 + 1) / w
        fy0, fy1 = 1.0 - (y1 + 1) / h, 1.0 - y0 / h
        print(f"            new Rect({fx0:.4f}f, {fy0:.4f}f, "
              f"{fx1 - fx0:.4f}f, {fy1 - fy0:.4f}f),")
    print("        };")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
