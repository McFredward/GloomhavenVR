"""Turn a generated board image into a shipped ALBEDO ATLAS.

THE PROBLEM THIS SOLVES, measured not assumed. The image model preserved the board's macro
layout but redrew every feature outline 3-9 mm off the mesh's real geometry and at slightly
different radii (edge overlay, block matcher validated against known shifts: it recovers a
planted shift exactly, and it could not find any consistent transform that registers the
generated features onto the mesh ones). The recesses, mouldings, glyphs and seat rings are
REAL GEOMETRY on the mesh, so a painted edge that misses its geometric edge shows as a
doubled edge. Dropping the generated image in as an albedo is therefore not an option.

But only the FEATURE OUTLINES are misregistered. The material itself -- the tarnish blooms,
the grime, the grain, the pitting, the colour story -- is spatially unstructured and does
not need to register with anything. So the composite keeps the material everywhere and
replaces the model's painted structure ONLY where the mesh actually has structure:

    weight   = how much mesh structure is here, from the gradient of the AO pass
    flat     = the generated art with its own feature-scale structure removed
    material = lerp(generated, flat, weight)         -- painted edges gone near mesh edges
    albedo   = material * AO^gamma                   -- the mesh's own relief supplies them

On the open plate weight is ~0 and the generated art passes through untouched; that is most
of the board. Near a moulding or a glyph, weight is ~1 and the edge comes from the geometry.

The board image is then scattered into the atlas through the 16-bit UV pass, and texels no
front view can see (recess walls, board edges, the back) are filled by push-pull dilation
from their neighbours -- which is correct, since the wall of a recess is the same metal as
its floor.
"""
import argparse, numpy as np
from PIL import Image

# ---------------------------------------------------------------- small helpers
def srgb2lin(x):
    x = x / 255.0
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)

def lin2srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055) * 255.0

def boxblur(a, k):
    """Separable box blur, reflect padding. `a` is HxWxC float."""
    if k < 3:
        return a.copy()
    k |= 1
    r = k // 2
    p = np.pad(a, ((r, r), (r, r), (0, 0)), mode='reflect')
    c = np.cumsum(p, 0); c = np.vstack([np.zeros((1,) + c.shape[1:]), c]); a1 = (c[k:] - c[:-k]) / k
    c = np.cumsum(a1, 1); c = np.hstack([np.zeros((c.shape[0], 1) + c.shape[2:]), c])
    return (c[:, k:] - c[:, :-k]) / k

def board_rect(path, thr=25, frac=0.35):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    lum = a.mean(2)
    xs = np.nonzero((lum > thr).mean(0) > frac)[0]
    ys = np.nonzero((lum > thr).mean(1) > frac)[0]
    return a[int(ys.min()):int(ys.max()) + 1, int(xs.min()):int(xs.max()) + 1]

# ---------------------------------------------------------------- the composite
def structure_weight(ao, feat_px):
    """1 where the MESH has a FEATURE, 0 on open plate. From the AO gradient, dilated a
    little so a painted edge sitting a few millimetres off its geometric one is still inside
    the zone that gets flattened.

    THE GRADIENT IS TAKEN ON A SMOOTHED AO, and that matters. The AO pass binds the normal
    map, so it carries the material grain as well as the geometry; taking the gradient raw
    made the weight fire on brush lines and put 45 percent of the board above 0.5, which
    would have flattened the generated material across half the plate. Smoothing first
    leaves only the mouldings, recess borders, engraved glyphs and seat rings."""
    sm = boxblur(ao[..., None], max(3, feat_px // 6))[..., 0]
    gx = np.zeros_like(sm); gy = np.zeros_like(sm)
    gx[:, 1:-1] = sm[:, 2:] - sm[:, :-2]
    gy[1:-1] = sm[2:] - sm[:-2]
    g = np.hypot(gx, gy)
    # dilate by max-pooling over a window, then soften
    r = max(2, feat_px // 2)
    p = np.pad(g, r, mode='edge')
    d = np.zeros_like(g)
    for dy in range(0, 2 * r + 1, max(1, r // 2)):
        for dx in range(0, 2 * r + 1, max(1, r // 2)):
            d = np.maximum(d, p[dy:dy + g.shape[0], dx:dx + g.shape[1]])
    d = boxblur(d[..., None], feat_px | 1)[..., 0]
    s = d / max(np.percentile(d, 99.0), 1e-6)
    return np.clip(s, 0, 1) ** 0.7

def composite_board(gen_rgb, ao, cov, feat_px=41, gamma=0.85, weight_gain=1.0, micro_px=None):
    """gen_rgb, ao, cov all in board space, any resolution."""
    lin = srgb2lin(gen_rgb)
    micro_px = micro_px or max(3, feat_px // 6)

    # ONLY THE MID BAND IS REMOVED, and that is the whole point. A plain box blur at the
    # feature scale takes the model's misplaced outlines away, but it takes the grain and the
    # pitting with them, and the rosettes and the seat rings came out looking moulded in
    # plastic. Rebuilding the flattened version as LOW + HIGH drops exactly the band the
    # misregistered outlines live in and leaves the micro-material everywhere on the board.
    low = boxblur(lin, feat_px)
    high = lin - boxblur(lin, micro_px)
    flat = low + high

    w = np.clip(structure_weight(ao, feat_px) * weight_gain, 0, 1)[..., None]
    material = lin * (1 - w) + flat * w
    relief = np.clip(ao, 0.05, 1.5) ** gamma
    out = material * relief[..., None]
    return out, w[..., 0]

# ---------------------------------------------------------------- board -> atlas
def scatter(board_lin, u, v, cov, N):
    """Average every board pixel into the atlas texel it sampled."""
    xi = np.clip((np.clip(u, 0, 1) * (N - 1) + 0.5).astype(np.int64), 0, N - 1)
    yi = np.clip(((1 - np.clip(v, 0, 1)) * (N - 1) + 0.5).astype(np.int64), 0, N - 1)
    idx = (yi * N + xi)[cov]
    vals = board_lin[cov]
    acc = np.zeros((N * N, 3), np.float64)
    cnt = np.zeros(N * N, np.float64)
    np.add.at(acc, idx, vals)
    np.add.at(cnt, idx, 1.0)
    filled = cnt > 0
    acc[filled] /= cnt[filled][:, None]
    return acc.reshape(N, N, 3).astype(np.float32), filled.reshape(N, N)

def pushpull_fill(img, filled, iters=400):
    """Fill unseen texels from their filled neighbours. A recess wall gets the material of
    the floor beside it, which is what it is made of."""
    out = img.copy(); f = filled.copy()
    for _ in range(iters):
        if f.all():
            break
        s = np.zeros_like(out); c = np.zeros(f.shape, np.float32)
        for dy, dx in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            sh = np.roll(np.roll(out, dy, 0), dx, 1)
            sf = np.roll(np.roll(f.astype(np.float32), dy, 0), dx, 1)
            s += sh * sf[..., None]; c += sf
        new = (~f) & (c > 0)
        out[new] = (s[new] / c[new][..., None])
        f = f | new
    return out, f

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--style', required=True)
    ap.add_argument('--gen', required=True)
    ap.add_argument('--old-albedo', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--feat-px', type=int, default=None,
                    help='feature scale in board pixels; defaults to 20 per 1024 rows')
    ap.add_argument('--passdir', default=None, help='directory of the u16/v16/cover16/ao16 passes')
    ap.add_argument('--gamma', type=float, default=0.85)
    ap.add_argument('--target-L', dest='target_L', type=float, default=None,
                    help='if set, scale the result in LINEAR light so the covered mean L* matches this')
    a = ap.parse_args()

    d = a.passdir or f'init/{a.style}'
    u = np.asarray(Image.open(f'{d}/u16.png')).astype(np.float32) / 65535.0
    v = np.asarray(Image.open(f'{d}/v16.png')).astype(np.float32) / 65535.0
    cov = np.asarray(Image.open(f'{d}/cover16.png')).astype(np.float32) / 65535.0 > 0.5
    ao = np.asarray(Image.open(f'{d}/ao16.png')).astype(np.float32) / 65535.0
    ao[~cov] = 1.0

    BH, BW = ao.shape
    feat_px = a.feat_px if a.feat_px else max(9, int(round(20 * BH / 1024)) | 1)
    gen = board_rect(a.gen)
    gen = np.asarray(Image.fromarray(gen.astype(np.uint8)).resize((BW, BH), Image.LANCZOS)).astype(np.float32)
    print('%-7s generated board resampled to %dx%d, feature scale %d px (%.1f mm)'
          % (a.style, BW, BH, feat_px, feat_px * 640.0 / BW))

    out_lin, w = composite_board(gen, ao, cov, feat_px, a.gamma)

    # LEVEL. The generated art came back much darker than the shipped albedo (steel L* 30.1
    # against 47.8). In game the board is the shipped albedo times a MEASURED 0.675 in linear
    # light, so the atlas level sets what the player sees directly. This is grading, not
    # authoring: one scalar in linear light, so it moves every texel the same way and cannot
    # change contrast, hue or the ratio between any two points.
    if a.target_L is not None:
        def meanL(k):
            Y = np.clip(out_lin[cov] * k, 0, 1).mean(1)
            f = np.where(Y > 0.008856, np.cbrt(Y), 7.787 * Y + 16 / 116)
            return (116 * f - 16).mean()
        lo, hi_ = 0.05, 20.0
        for _ in range(48):
            mid = (lo + hi_) / 2
            if meanL(mid) < a.target_L: lo = mid
            else: hi_ = mid
        k = (lo + hi_) / 2
        print('        level: mean L* %.1f -> %.1f  (linear gain x%.3f)' % (meanL(1.0), meanL(k), k))
        out_lin = out_lin * k
    print('        structure weight: mean %.3f  fraction >0.5 = %.3f (that is how much of the '
          'board takes its edges from the mesh)' % (w.mean(), (w > 0.5).mean()))

    # Dump the board-space composite so it can be LOOKED at before it is projected: this is
    # the picture that shows whether the structure weight over-flattened the material.
    Image.fromarray(np.clip(lin2srgb(out_lin), 0, 255).astype(np.uint8)
                    ).resize((2048, 1024), Image.LANCZOS).save(a.out.replace('.png', '_board.png'))
    Image.fromarray((np.clip(w, 0, 1) * 255).astype(np.uint8)
                    ).resize((2048, 1024), Image.LANCZOS).save(a.out.replace('.png', '_weight.png'))

    old = np.asarray(Image.open(a.old_albedo).convert('RGB')).astype(np.float32)
    N = old.shape[0]
    atlas, filled = scatter(out_lin, u, v, cov, N)
    print('        atlas texels reached by the front view: %.4f' % filled.mean())
    atlas, f2 = pushpull_fill(atlas, filled)
    print('        after push-pull fill: %.4f  (still empty %.5f)' % (f2.mean(), 1 - f2.mean()))
    # anything still empty (there should be almost nothing) keeps the old map, recoloured
    if not f2.all():
        oldlin = srgb2lin(old)
        scale = atlas[f2].mean(0) / np.maximum(oldlin[f2].mean(0), 1e-4)
        atlas[~f2] = oldlin[~f2] * scale
        print('        %d texels no view reached kept the old map recoloured by %s'
              % ((~f2).sum(), np.round(scale, 3)))

    rgb = lin2srgb(atlas)
    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).save(a.out)
    print('        wrote', a.out)

if __name__ == '__main__':
    main()
