"""tex_common.py -- shared numeric toolkit for the board texture pipeline.

Dependency floor VERIFIED in this environment before anything was designed around it:
    python 3.14.3, numpy 2.4.2, PIL/Pillow 12.2.0.   NO scipy, NO cv2.
Everything below (tileable noise, exact Euclidean distance transform, texture
dilation) is therefore implemented here in numpy only.

Conventions used by the whole pipeline:
  * Images are float arrays in [0,1], shape (N, N) for scalar maps and (N, N, 3)
    for colour, with row 0 = TOP of the image (PIL/PNG order).
  * UV space is Blender/Unity convention: origin BOTTOM-left, v up. The single
    conversion point is uv_to_px() -- nothing else may flip y.
  * Albedo is authored and stored in sRGB (that is how a texture artist works and
    how Unity reads _MainTex). Normal and the packed MR map are linear/non-colour.
"""

import json
import math
import os

import numpy as np
from PIL import Image

N_DEFAULT = 2048
BIG = 1.0e20


# --------------------------------------------------------------------------
# small helpers
# --------------------------------------------------------------------------

def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, dtype=np.float64) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def norm01(a):
    a = np.asarray(a, dtype=np.float64)
    lo, hi = float(a.min()), float(a.max())
    return np.zeros_like(a) if hi - lo < 1e-12 else (a - lo) / (hi - lo)


def frac(a):
    return a - np.floor(a)


# --------------------------------------------------------------------------
# tileable noise
# --------------------------------------------------------------------------

def spectral_noise(n, seed, beta=1.8, aniso=1.0, lowcut=1.0, highcut=None):
    """Seamlessly TILEABLE fractal noise, unit variance, zero mean.

    Built by spectral synthesis: white noise -> FFT -> radial power-law filter
    -> inverse FFT. Because the DFT basis is periodic the result wraps exactly,
    which is what a diffusion model cannot give you and is the whole reason the
    material bases here are procedural.

    aniso > 1 attenuates high horizontal frequencies, i.e. it STRETCHES the
    features along +x (the grain direction). aniso < 1 stretches along +y.
    """
    rg = np.random.default_rng(seed)
    white = rg.standard_normal((n, n))
    spec = np.fft.rfft2(white)

    fy = np.fft.fftfreq(n) * n
    fx = np.fft.rfftfreq(n) * n
    fy2, fx2 = np.meshgrid(fy, fx, indexing="ij")
    rad = np.sqrt((fx2 * float(aniso)) ** 2 + (fy2 / float(aniso)) ** 2)
    rad[0, 0] = 1.0

    amp = rad ** (-beta * 0.5)
    amp[rad < lowcut] = 0.0
    if highcut is not None:
        amp[rad > highcut] = 0.0
    amp[0, 0] = 0.0

    out = np.fft.irfft2(spec * amp, s=(n, n))
    out -= out.mean()
    sd = out.std()
    return out / sd if sd > 1e-12 else out


def ridge(a):
    """Turn a signed field into a ridged (absolute-valued, inverted) one."""
    return 1.0 - np.abs(a) / (np.abs(a).max() + 1e-12)


# --------------------------------------------------------------------------
# exact Euclidean distance transform (Felzenszwalb & Huttenlocher 2012)
# vectorised across the perpendicular axis, with nearest-source INDEX tracking
# --------------------------------------------------------------------------

def _edt1d(f):
    """f: (R, N) float. Returns (d, arg) with
         d[r,q]   = min_p ( f[r,p] + (q-p)^2 )
         arg[r,q] = the winning p.
    The inner parabola-stack loop is sequential in q but vectorised over r."""
    f = np.ascontiguousarray(f, dtype=np.float64)
    R, N = f.shape
    rows = np.arange(R)
    v = np.zeros((R, N), dtype=np.int64)
    z = np.empty((R, N + 1), dtype=np.float64)
    z[:, 0] = -BIG
    z[:, 1] = BIG
    k = np.zeros(R, dtype=np.int64)

    for q in range(1, N):
        fq = f[:, q] + float(q * q)
        vk = v[rows, k]
        s = (fq - (f[rows, vk] + vk.astype(np.float64) ** 2)) / (2.0 * (q - vk))
        bad = s <= z[rows, k]
        while bad.any():
            k = np.where(bad, k - 1, k)
            vk = v[rows, k]
            s_new = (fq - (f[rows, vk] + vk.astype(np.float64) ** 2)) / (2.0 * (q - vk))
            s = np.where(bad, s_new, s)
            bad = bad & (s <= z[rows, k])
        k = k + 1
        v[rows, k] = q
        z[rows, k] = s
        z[rows, k + 1] = BIG

    d = np.empty((R, N), dtype=np.float64)
    arg = np.empty((R, N), dtype=np.int64)
    k = np.zeros(R, dtype=np.int64)
    for q in range(N):
        adv = z[rows, k + 1] < q
        while adv.any():
            k = np.where(adv, k + 1, k)
            adv = z[rows, k + 1] < q
        vk = v[rows, k]
        d[:, q] = (q - vk) ** 2 + f[rows, vk]
        arg[:, q] = vk
    return d, arg


def edt(mask, want_index=False):
    """Exact Euclidean distance (in pixels) from every texel to the nearest
    True texel of `mask`. Texels inside the mask get 0.
    With want_index=True also returns (iy, ix) of that nearest True texel."""
    mask = np.asarray(mask, dtype=bool)
    f = np.where(mask, 0.0, BIG)
    d1, a1 = _edt1d(f)                            # along x, per row
    d2, a2 = _edt1d(np.ascontiguousarray(d1.T))   # along y, per column
    dist2 = d2.T
    if not want_index:
        return np.sqrt(np.maximum(dist2, 0.0))
    iy = np.ascontiguousarray(a2.T)
    ix = np.take_along_axis(a1, iy, axis=0)       # a1[iy[r,c], c]
    return np.sqrt(np.maximum(dist2, 0.0)), iy, ix


def dilate_fill(arrays, mask, limit=None):
    """Texture padding / island dilation.

    Every texel OUTSIDE `mask` is overwritten with the value of the nearest
    texel INSIDE it. This is the standard bleed that stops bilinear filtering
    and mip generation from pulling a foreign island's colour across a seam.
    `arrays` is a list of (N,N) or (N,N,C) float arrays, all filled coherently
    from the SAME source texel so albedo / normal / MR never disagree.
    Returns the distance field so the caller can report actual bleed reach."""
    mask = np.asarray(mask, dtype=bool)
    if not mask.any():
        raise ValueError("dilate_fill: mask is empty -- nothing to bleed from")
    dist, iy, ix = edt(mask, want_index=True)
    outside = ~mask
    if limit is not None:
        outside = outside & (dist <= limit)
    sy, sx = iy[outside], ix[outside]
    for a in arrays:
        a[outside] = a[sy, sx]
    return dist


# --------------------------------------------------------------------------
# normals from height
# --------------------------------------------------------------------------

def normal_from_height(height, strength=1.0):
    """Tangent-space normal map from a height field, wrapped with np.roll so a
    tileable height stays tileable. Returns (N,N,3) in [0,1], OpenGL/Unity
    convention (+Y up in texture space, i.e. green points UP the image)."""
    h = np.asarray(height, dtype=np.float64)

    def sh(dy, dx):
        return np.roll(np.roll(h, dy, axis=0), dx, axis=1)

    gx = ((sh(-1, -1) + 2 * sh(0, -1) + sh(1, -1)) -
          (sh(-1, 1) + 2 * sh(0, 1) + sh(1, 1))) * 0.25
    gy = ((sh(-1, -1) + 2 * sh(-1, 0) + sh(-1, 1)) -
          (sh(1, -1) + 2 * sh(1, 0) + sh(1, 1))) * 0.25
    nx = gx * strength
    ny = gy * strength
    nz = np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    out = np.empty(h.shape + (3,), dtype=np.float64)
    out[..., 0] = nx * inv * 0.5 + 0.5
    out[..., 1] = ny * inv * 0.5 + 0.5
    out[..., 2] = nz * inv * 0.5 + 0.5
    return out


def box_blur(a, radius):
    """Separable wrapped box blur via cumulative sums (cheap at 2048^2)."""
    a = np.asarray(a, dtype=np.float64)
    k = int(radius) * 2 + 1
    out = a
    for axis in (0, 1):
        n = out.shape[axis]
        pad = np.concatenate([out, out, out], axis=axis)
        cs = np.cumsum(pad, axis=axis)
        cs = np.concatenate([np.zeros_like(np.take(cs, [0], axis=axis)), cs], axis=axis)
        idx_hi = np.arange(n, 2 * n) + radius + 1
        idx_lo = np.arange(n, 2 * n) - radius
        hi = np.take(cs, idx_hi, axis=axis)
        lo = np.take(cs, idx_lo, axis=axis)
        out = (hi - lo) / k
    return out


def cavity(height, radius=9, gain=1.0):
    """View- and light-INDEPENDENT cavity/ambient term: how far below its local
    average a texel sits. This is the ONLY height-derived term allowed to touch
    albedo -- a directional derivative (a fake highlight) is not, because the
    user's complaint is exactly that baked lighting fights the real lighting."""
    h = np.asarray(height, dtype=np.float64)
    blur = box_blur(h, radius)
    return np.clip((blur - h) * gain, 0.0, 1.0)


def cavity_multi(height, radii=(3, 11, 40), gain=1.0, weights=None):
    """Multi-scale cavity.

    A single small radius only sees the EDGE of a carving: the flat interior of a
    wide recessed plateau is level with its own local average, so it reads as
    uncavitied and the ornament shows up as a thin outline and nothing else. The
    first compositor run did exactly that -- the rosette was a ghost. Summing
    several radii gives the wide recess its interior back, which is what a baked
    AO would have shown, and is still light-direction-free."""
    h = np.asarray(height, dtype=np.float64)
    if weights is None:
        weights = [1.0] * len(radii)
    tot = float(sum(weights))
    acc = np.zeros_like(h)
    for r, w in zip(radii, weights):
        acc += (w / tot) * np.clip((box_blur(h, int(r)) - h) * gain, 0.0, 1.0)
    return np.clip(acc, 0.0, 1.0)


# --------------------------------------------------------------------------
# palette lock
# --------------------------------------------------------------------------

class Palette:
    """A short, ordered list of sRGB anchor colours. Every albedo texel in the
    style is produced by indexing THIS ramp with a scalar, so the style cannot
    acquire a colour nobody chose -- the 'controlled palette' half of 'cleaner'."""

    def __init__(self, name, stops):
        self.name = name
        self.stops = [(float(p), np.array(c, dtype=np.float64) / 255.0) for p, c in stops]
        self.stops.sort(key=lambda s: s[0])

    def map(self, t):
        t = np.clip(np.asarray(t, dtype=np.float64), 0.0, 1.0)
        out = np.empty(t.shape + (3,), dtype=np.float64)
        ps = [s[0] for s in self.stops]
        n = len(self.stops)
        for i in range(n - 1):
            p0, c0 = self.stops[i]
            p1, c1 = self.stops[i + 1]
            last = (i == n - 2)
            m = (t >= p0) & (t <= p1) if last else (t >= p0) & (t < p1)
            if not m.any():
                continue
            k = ((t[m] - p0) / max(p1 - p0, 1e-9))[..., None]
            out[m] = c0[None, :] * (1.0 - k) + c1[None, :] * k
        out[t < ps[0]] = self.stops[0][1]
        out[t > ps[-1]] = self.stops[-1][1]
        return out

    def hexes(self):
        return ["#%02X%02X%02X" % tuple(int(round(v * 255)) for v in c) for _, c in self.stops]


# --------------------------------------------------------------------------
# UV <-> pixel
# --------------------------------------------------------------------------

def uv_to_px(u, v, n):
    """UV (origin bottom-left, v up) -> pixel (origin top-left, row down).
    THE ONLY y-flip in the pipeline."""
    return u * n, (1.0 - v) * n


def region_rect_px(reg, n):
    """A contract region dict -> integer pixel box (x0, y0, x1, y1), top-left
    origin, half-open. Uses floor/ceil so a region never loses a texel."""
    x0, y_bot = uv_to_px(reg["u0"], reg["v0"], n)
    x1, y_top = uv_to_px(reg["u1"], reg["v1"], n)
    return (int(math.floor(x0)), int(math.floor(y_top)),
            int(math.ceil(x1)), int(math.ceil(y_bot)))


# --------------------------------------------------------------------------
# io
# --------------------------------------------------------------------------

def save_rgb(path, arr):
    a = np.clip(np.asarray(arr, dtype=np.float64), 0.0, 1.0)
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    Image.fromarray((a * 255.0 + 0.5).astype(np.uint8), "RGB").save(path, optimize=True)
    return path


def save_gray(path, arr):
    a = np.clip(np.asarray(arr, dtype=np.float64), 0.0, 1.0)
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    Image.fromarray((a * 255.0 + 0.5).astype(np.uint8), "L").save(path, optimize=True)
    return path


def load_gray(path):
    return np.asarray(Image.open(path).convert("L"), dtype=np.float64) / 255.0


def load_rgba(path):
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float64) / 255.0


def read_json(path):
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def write_json(path, obj):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(obj, fh, indent=2)
    return path


# --------------------------------------------------------------------------
# self-test -- VERIFY THE INSTRUMENT BEFORE TRUSTING ITS NUMBER
# --------------------------------------------------------------------------

def _brute_edt(mask):
    ys, xs = np.nonzero(mask)
    h, w = mask.shape
    gy, gx = np.mgrid[0:h, 0:w]
    d = np.full((h, w), np.inf)
    for y, x in zip(ys, xs):
        d = np.minimum(d, np.sqrt((gy - y) ** 2.0 + (gx - x) ** 2.0))
    return d


def selftest():
    ok = True

    rg = np.random.default_rng(7)
    for trial in range(6):
        m = rg.random((37, 41)) < 0.04
        if not m.any():
            continue
        a = edt(m)
        b = _brute_edt(m)
        err = float(np.max(np.abs(a - b)))
        good = err < 1e-9
        ok &= good
        print(f"  edt vs brute force, random trial {trial}: max err {err:.3e}  "
              f"{'OK' if good else 'FAIL'}")

    m = rg.random((53, 59)) < 0.05
    d, iy, ix = edt(m, want_index=True)
    gy, gx = np.mgrid[0:53, 0:59]
    dd = np.sqrt((gy - iy) ** 2.0 + (gx - ix) ** 2.0)
    err = float(np.max(np.abs(dd - d)))
    good = err < 1e-9 and bool(m[iy, ix].all())
    ok &= good
    print(f"  edt index consistency: max err {err:.3e}, "
          f"all sources in mask={bool(m[iy, ix].all())}  {'OK' if good else 'FAIL'}")

    n = 256
    f = spectral_noise(n, 11, beta=2.0)
    seam = float(np.mean(np.abs(f[0, :] - f[-1, :])))
    inner = float(np.mean(np.abs(f[1:, :] - f[:-1, :])))
    good = seam < inner * 1.25
    ok &= good
    print(f"  noise wrap seam: seam step {seam:.4f} vs interior step {inner:.4f}  "
          f"{'OK' if good else 'FAIL'}")

    a = rg.random((32, 32))
    bb = box_blur(a, 2)
    ref = np.zeros_like(a)
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            ref += np.roll(np.roll(a, dy, 0), dx, 1)
    ref /= 25.0
    err = float(np.max(np.abs(bb - ref)))
    good = err < 1e-9
    ok &= good
    print(f"  box_blur vs direct mean: max err {err:.3e}  {'OK' if good else 'FAIL'}")

    _, y_top = uv_to_px(0.5, 1.0, 2048)
    _, y_bot = uv_to_px(0.5, 0.0, 2048)
    good = abs(y_top - 0.0) < 1e-9 and abs(y_bot - 2048.0) < 1e-9
    ok &= good
    print(f"  uv_to_px: v=1 -> row {y_top}, v=0 -> row {y_bot}  {'OK' if good else 'FAIL'}")

    # dilate_fill must actually replace every outside texel with a mask texel value
    lab = np.zeros((64, 64))
    lab[10:20, 10:20] = 1.0
    lab[40:50, 40:50] = 2.0
    msk = lab > 0
    arr = lab.copy()
    dilate_fill([arr], msk)
    good = bool(np.all((arr == 1.0) | (arr == 2.0)))
    ok &= good
    print(f"  dilate_fill leaves no unfilled texel: {good}  {'OK' if good else 'FAIL'}")

    print("SELFTEST", "PASS" if ok else "FAIL")
    return ok


if __name__ == "__main__":
    raise SystemExit(0 if selftest() else 1)
