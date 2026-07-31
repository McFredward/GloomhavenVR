#!/usr/bin/env python3
# palm_crease_metric.py — put a NUMBER on "the palm has a hard fold across it" and on "the palm
# has a star of flat wedges painted on it", straight off the renders, because that is what the
# complaint is about.
#
# A crease is a LINE of high luminance gradient. So: take the palm plate out of a palm-on render
# (a box that excludes the fingers and the cuff), and look at the distribution of the Sobel
# gradient magnitude over it. The p99.5 is the crease: a smooth plate has a low tail, a plate
# with a fold has a high one. Run it on the CLAY render for geometry+shading and on the
# EMISSION render for pigment.
#
#   python3 unity/hand-prep/palm_crease_metric.py <before.png> <after.png> [--box x0 y0 x1 y1]
import sys
import numpy as np
from PIL import Image

BOX = (265, 500, 575, 790)      # the palm PLATE in a 900 px palm-on render: no fingers, no cuff
raw = sys.argv[1:]
args = []
i = 0
while i < len(raw):
    if raw[i] == "--box":
        BOX = tuple(int(v) for v in raw[i + 1:i + 5])
        i += 5
        continue
    if not raw[i].startswith("--"):
        args.append(raw[i])
    i += 1


def grad_stats(path):
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float64)
    x0, y0, x1, y1 = BOX
    sub = im[y0:y1, x0:x1]
    lum = sub @ np.array([0.2126, 0.7152, 0.0722])
    gy, gx = np.gradient(lum)
    g = np.hypot(gx, gy)
    return g


print(f"palm plate box x{BOX[0]}-{BOX[2]} y{BOX[1]}-{BOX[3]}  (gradient of luminance, 0-255)")
for p in args:
    g = grad_stats(p)
    print(f"  {p.split('/')[-1]:34s} mean {g.mean():6.3f}  p95 {np.percentile(g, 95):6.3f}  "
          f"p99.5 {np.percentile(g, 99.5):7.3f}  max {g.max():7.3f}")
