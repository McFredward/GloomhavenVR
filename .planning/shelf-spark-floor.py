"""The harness's own cross-process floor, so the residue above can be named.

At phase 0.000 and 1.000 the fade factor is EXACTLY 1.0, so `shelfsparks` and
`shelfsparks-baseline` are the same draw with the same data and must be the same
picture. Anything that differs there is the floor two separate Unity processes
leave behind, and if it sits at the same pixels as the residue at the down
phases then the residue is the floor and not a surviving spark.
"""
import os
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        'shelf-spark-measure.py')).read()
ns = {}
exec(src[:src.index('HERE = ')], ns)
read_png = ns['read_png']
HERE = os.path.dirname(os.path.abspath(__file__))
for tag in ['00', '100']:
    A = os.path.join(HERE, 'debug', 'shelfsparks', f'env_cellar_HauntShelfRide_pride{tag}.png')
    B = os.path.join(HERE, 'debug', 'shelfsparks-baseline', f'env_cellar_HauntShelfRide_pride{tag}.png')
    w, h, ch, a = read_png(A)
    _, _, _, b = read_png(B)
    pts = []
    for y in range(h):
        for x in range(w):
            i = (y*w + x)*ch
            d = sum(abs(a[i+k]-b[i+k]) for k in range(3))
            if d > 6: pts.append((x, y, d))
    print(f"phase .{tag}: fade-live vs as-shipped differ in {len(pts)} px"
          f"{'' if not pts else ': ' + str(sorted(pts, key=lambda p: -p[2])[:8])}")
