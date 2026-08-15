"""Measure the bookshelf spark fade off the ModBuild 152 preview series.

Two renders of the SAME thirteen instants: `shelfsparks` with the fade live and
`shelfsparks-baseline` with _TipUse.z forced to 0, i.e. exactly what shipped.
Everything else in both frames is identical by construction (pinned emitter
seeds, restart-from-zero Simulate, the same clock), so the per-pixel difference
IS the two spark populations and nothing else.
"""
import struct, zlib, sys, os

def read_png(path):
    d = open(path, 'rb').read()
    assert d[:8] == b'\x89PNG\r\n\x1a\n'
    i, idat, w, h, bd, ct = 8, b'', 0, 0, 0, 0
    while i < len(d):
        ln = struct.unpack('>I', d[i:i+4])[0]; typ = d[i+4:i+8]
        if typ == b'IHDR':
            w, h, bd, ct = struct.unpack('>IIBB', d[i+8:i+18])
        elif typ == b'IDAT':
            idat += d[i+8:i+8+ln]
        i += 12 + ln
    assert bd == 8 and ct in (2, 6), (bd, ct)
    ch = 3 if ct == 2 else 4
    raw = zlib.decompress(idat)
    out = bytearray(w*h*ch); stride = w*ch; prev = bytearray(stride); p = 0
    for y in range(h):
        f = raw[p]; p += 1
        line = bytearray(raw[p:p+stride]); p += stride
        if f == 1:
            for x in range(ch, stride): line[x] = (line[x] + line[x-ch]) & 255
        elif f == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                a = line[x-ch] if x >= ch else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x-ch] if x >= ch else 0
                b = prev[x]; c = prev[x-ch] if x >= ch else 0
                pp = a + b - c
                pa, pb, pc = abs(pp-a), abs(pp-b), abs(pp-c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        out[y*stride:(y+1)*stride] = line; prev = line
    return w, h, ch, out

HERE = os.path.dirname(os.path.abspath(__file__))
A = os.path.join(HERE, 'debug', 'shelfsparks')            # the fade, live
B = os.path.join(HERE, 'debug', 'shelfsparks-baseline')   # _TipUse.z = 0, i.e. as shipped
Z = os.path.join(HERE, 'debug', 'shelfsparks-off')        # both populations gated off entirely
PH = ['00', '08', '12', '16', '18', '20', '30', '45', '62', '70', '80', '90', '100']
DUR = 26.002

# The exact factor the shader is asked to apply, recomputed here from
# EnvShelfTip.cginc's own constants so that the pixels can be checked against
# something rather than merely looked at. Alpha, not energy: EnvParticleAdd
# premodulates (c.rgb *= c.a) and then blends SrcAlpha One, so what a photograph
# of the additive contribution measures is alpha SQUARED.
import math
U0, K, PHI0B, INVSPAN = 0.0087268678, 3.8599751733, 0.0349165850, 0.6510926427
FALL, LAND, ARC, RISE, BAMP = 0.18, 0.024, 0.204, 0.62, 0.021
sat = lambda x: min(1.0, max(0.0, x))
def arc(s):
    x = sat(s / FALL)
    n = sat((4*math.atan(U0*math.exp(K*x)) - PHI0B) * INVSPAN)
    w = sat((s - FALL) / LAND)
    return sat(n - BAMP*4*w*(1-w))
def upright(ph):
    q = sat((ph - RISE) / (1 - RISE))
    return 1.0 - arc(min(ph, ARC) * (1 - q))

def energy(p, q, ch, a, b):
    """Sum of the POSITIVE difference b - a. The sparks are additive, so
    whatever they touch is brighter in b; a signed sum would let the renderer's
    own noise cancel against the signal."""
    tot = 0; lit = 0
    for i in range(0, len(a), ch):
        d = (max(b[i]-a[i], 0) + max(b[i+1]-a[i+1], 0) + max(b[i+2]-a[i+2], 0))
        if d > 6:
            tot += d; lit += 1
    return tot, lit

# THE FLOOR. Two Unity processes rendering the identical draw do not produce the
# identical PNG: shelf-spark-floor.py finds 20 pixels that differ between the
# fade-live and as-shipped runs at phase 0.000 and 1.000, where the fade factor
# is EXACTLY 1.0 and the two are the same draw on the same data. It is the same
# handful of fixed screen positions every time. So this much "spark" is measured
# even where there is provably none, and it is subtracted rather than argued
# away — 1350 is the median of the four phases at which the bookcase is flat on
# the floor and the population is collapsed.
FLOOR = 1350

print(f"{'phase':>6} {'t(s)':>7} {'deg':>6} | {'as shipped':>10} {'with fade':>10}"
      f" {'drawn':>7} | {'alpha':>7} {'alpha^2':>8} {'px':>5}")
print(f"{'':>6} {'':>7} {'':>6} | {'(energy)':>10} {'(energy)':>10}"
      f" {'share':>7} | {'exact':>7} {'exact':>8} {'':>5}")
for tag in PH:
    ph = 1.0 if tag == '100' else float(tag) / 100.0
    f = lambda d: os.path.join(d, f'env_cellar_HauntShelfRide_pride{tag}.png')
    w, h, ch, a = read_png(f(A))
    _, _, _, b = read_png(f(B))
    _, _, _, z = read_png(f(Z))
    full, _ = energy(z, b, ch, z, b)     # the population as it ships, vs no population
    left, npx = energy(z, a, ch, z, a)   # ...and what the fade leaves of it
    full = max(full - FLOOR, 0); left = max(left - FLOOR, 0)
    share = (left / full) if full > 0 else float('nan')
    exp = upright(ph)
    deg = 90.0 * (1.0 - exp)
    # `share` is measured on GAMMA-ENCODED frames over a lit background, so it is
    # a monotone proxy for the drawn energy and not a calibrated alpha; the exact
    # factor is the closed form beside it. What the pixels are being asked to
    # settle is the SHAPE — untouched standing, monotonically down through the
    # topple, nothing at all from the arrival to the righting, and back.
    print(f"{ph:6.3f} {ph*DUR:7.2f} {deg:6.2f} | {full:10d} {left:10d}"
          f" {share:7.3f} | {exp:7.3f} {exp*exp:8.3f} {npx:5d}")
