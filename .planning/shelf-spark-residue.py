"""Where are the pixels that still differ once the sparks are supposed to be out?

If the fade works, `shelfsparks` (fade live) and `shelfsparks-off` (both
populations gated off entirely) must be the same picture at every phase where
the bookcase is on the floor. Whatever still differs is either a spark that
survived — which would sit in the plume, above the shelf — or the harness's own
cross-process floor, which is scattered and unrelated to where sparks are.
"""
import os
# read_png only: importing the sibling as a module would run its whole
# measurement, and its name is not an identifier anyway.
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        'shelf-spark-measure.py')).read()
ns = {}
exec(src[:src.index('HERE = ')], ns)
read_png = ns['read_png']

HERE = os.path.dirname(os.path.abspath(__file__))
for tag in ['00', '18', '30', '45', '62']:
    A = os.path.join(HERE, 'debug', 'shelfsparks', f'env_cellar_HauntShelfRide_pride{tag}.png')
    Z = os.path.join(HERE, 'debug', 'shelfsparks-off', f'env_cellar_HauntShelfRide_pride{tag}.png')
    w, h, ch, a = read_png(A)
    _, _, _, z = read_png(Z)
    pts = []
    for y in range(h):
        for x in range(w):
            i = (y*w + x)*ch
            d = (max(a[i]-z[i], 0) + max(a[i+1]-z[i+1], 0) + max(a[i+2]-z[i+2], 0))
            if d > 6: pts.append((x, y, d))
    if not pts:
        print(f"phase .{tag}: identical"); continue
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    print(f"phase .{tag}: {len(pts)} px, x {min(xs)}..{max(xs)}, y {min(ys)}..{max(ys)}, "
          f"max {max(p[2] for p in pts)}, at {sorted(pts, key=lambda p:-p[2])[:6]}")
