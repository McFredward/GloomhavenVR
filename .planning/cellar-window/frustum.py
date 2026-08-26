#!/usr/bin/env python3
"""What the cellar's barred window actually subtends, from the play space.

Mirrors BuildEnvironmentRooms.cs: WindowHole, SnappedHole, RevealDepth,
RevealJambSplay, RevealCillFall, CellarPlaySpaceDia.  Eye heights are
PreviewEnvironments.HeadY's answers (seated 1.66 m, standing 2.02 m) plus the
1.05 m crouch the sky patch's own sweep uses.

Run:  python3 .planning/cellar-window/frustum.py
"""
import math

CW, CD, CH, cell = 10.5, 9.0, 3.3, 0.16


def snap(x0, y0, w, h, L, H, c):
    nx = math.ceil(L / c)
    ny = math.ceil(H / c)
    i0 = i1 = j0 = j1 = None
    for j in range(ny):
        for i in range(nx):
            cx = L * (i + 0.5) / nx
            cy = H * (j + 0.5) / ny
            if x0 <= cx < x0 + w and y0 <= cy < y0 + h:
                i0 = i if i0 is None else min(i0, i)
                i1 = i + 1 if i1 is None else max(i1, i + 1)
                j0 = j if j0 is None else min(j0, j)
                j1 = j + 1 if j1 is None else max(j1, j + 1)
    return L * i0 / nx, H * j0 / ny, L * i1 / nx, H * j1 / ny


wx0, wy0, wx1, wy1 = snap(3.20, 2.20, 1.46, 0.79, CW, CH, cell)
wx0 -= CW / 2
wx1 -= CW / 2
hd = CD / 2
D = 0.55
inset = D * 0.185
rise = D * 0.260
ox0, ox1, oy0, oy1 = wx0 + inset, wx1 - inset, wy0 + rise, wy1
oz = hd + D

print("inner  x %.4f..%.4f  y %.4f..%.4f  z %.2f  area %.4f m2"
      % (wx0, wx1, wy0, wy1, hd, (wx1 - wx0) * (wy1 - wy0)))
print("outer  x %.4f..%.4f  y %.4f..%.4f  z %.2f  area %.4f m2"
      % (ox0, ox1, oy0, oy1, oz, (ox1 - ox0) * (oy1 - oy0)))

playR = 6.5 / 2
eyes = []
N = 48
for h in (1.05, 1.66, 2.02):
    for k in range(N):
        a = k / N * 2 * math.pi
        eyes.append((math.sin(a) * playR, h, math.cos(a) * playR))
        eyes.append((math.sin(a) * playR * 0.5, h, math.cos(a) * playR * 0.5))
    eyes.append((0.0, h, 0.0))

M = 40
pairs = []
for e in eyes:
    dz = hd - e[2]
    if dz <= 0:
        continue
    t = (oz - e[2]) / dz
    for a in range(M + 1):
        for b in range(M + 1):
            px = wx0 + (wx1 - wx0) * a / M
            py = wy0 + (wy1 - wy0) * b / M
            dx, dy = px - e[0], py - e[1]
            qx = e[0] + dx * t
            qy = e[1] + dy * t
            if ox0 <= qx <= ox1 and oy0 <= qy <= oy1:
                pairs.append((e, (dx, dy, dz)))
print("eye/direction pairs that clear BOTH openings:", len(pairs))

print("\nWhat the opening subtends, measured beyond the OUTER face (room x, room y):")
for dist in (5, 10, 20, 30, 40, 60, 80, 120):
    xs, ys = [], []
    for e, d in pairs:
        t = (oz + dist - e[2]) / d[2]
        xs.append(e[0] + d[0] * t)
        ys.append(e[1] + d[1] * t)
    print("  %5.0f m : x %8.2f..%8.2f (%7.1f m wide)   y %7.2f..%7.2f   "
          "above outside ground %6.2f..%6.2f m"
          % (dist, min(xs), max(xs), max(xs) - min(xs),
             min(ys), max(ys), min(ys) - oy0, max(ys) - oy0))

azs, els = [], []
for e, d in pairs:
    n = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
    azs.append(math.degrees(math.atan2(d[0], d[2])))
    els.append(math.degrees(math.asin(d[1] / n)))
print("\ndirection hull over every eye: az %.1f..%.1f deg,  el %.1f..%.1f deg"
      % (min(azs), max(azs), min(els), max(els)))
print("solid angle spanned: %.1f x %.1f deg" % (max(azs) - min(azs), max(els) - min(els)))

print("\nPER EYE HEIGHT, the elevation band (is the outside ground EVER in view?):")
for h in (1.05, 1.66, 2.02):
    lo = min(e for (e, d), e2 in [] ) if False else None
    ee = [p for p in pairs if abs(p[0][1] - h) < 1e-6]
    el = []
    for e, d in ee:
        n = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
        el.append(math.degrees(math.asin(d[1] / n)))
    print("  eye %.2f m : el %.2f..%.2f deg   (a NEGATIVE minimum would mean the "
          "outside ground is visible)" % (h, min(el), max(el)))
