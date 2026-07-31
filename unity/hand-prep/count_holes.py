#!/usr/bin/env python3
# count_holes.py — count MAGENTA pixels that are INSIDE the hand silhouette of an
# emission render (render_hand.py --modes emis). Background magenta is reachable from the
# image border by a flood fill; anything magenta that is not reachable is a hole you can see
# through, i.e. a violation of acceptance criterion (a).
#
#   python3 unity/hand-prep/count_holes.py <img.png> [<img.png> ...] [--dump out_prefix]
import sys, os
import numpy as np
from PIL import Image
from collections import deque

_a = sys.argv[1:]
args = []
_skip = False
for i, a in enumerate(_a):
    if _skip:
        _skip = False
        continue
    if a == "--dump":
        _skip = True
        continue
    if a.startswith("--"):
        continue
    args.append(a)
DUMP = sys.argv[sys.argv.index("--dump") + 1] if "--dump" in sys.argv else None

for path in args:
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.int16)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    mag = (r > 200) & (g < 60) & (b > 200)
    h, w = mag.shape
    seen = np.zeros_like(mag)
    dq = deque()
    for x in range(w):
        for y in (0, h - 1):
            if mag[y, x] and not seen[y, x]:
                seen[y, x] = 1
                dq.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if mag[y, x] and not seen[y, x]:
                seen[y, x] = 1
                dq.append((y, x))
    while dq:
        y, x = dq.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and mag[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = 1
                dq.append((ny, nx))
    interior = mag & (seen == 0)
    n = int(interior.sum())
    # blob count
    blobs = 0
    vis = np.zeros_like(interior)
    ys, xs = np.nonzero(interior)
    for y0, x0 in zip(ys, xs):
        if vis[y0, x0]:
            continue
        blobs += 1
        vis[y0, x0] = 1
        st = [(y0, x0)]
        while st:
            y, x = st.pop()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < h and 0 <= nx < w and interior[ny, nx] and not vis[ny, nx]:
                        vis[ny, nx] = 1
                        st.append((ny, nx))
    print(f"{os.path.basename(path)}: interior magenta px {n} in {blobs} blobs "
          f"(background magenta {int(mag.sum()) - n})")
    if DUMP and n:
        out = np.asarray(Image.open(path).convert("RGB")).copy()
        out[interior] = (0, 255, 0)
        Image.fromarray(out).save(f"{DUMP}_{os.path.basename(path)}")
