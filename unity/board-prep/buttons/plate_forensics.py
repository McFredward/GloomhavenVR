#!/usr/bin/env python3
"""ROUND 3, STEP 0 -- what actually separates the ACCEPTED board backs from the REJECTED cap
plates.

THE QUESTION THIS FILE EXISTS TO ANSWER
---------------------------------------
Two rounds of keycap material were rejected with the SAME word -- "einheitlich" -- and round 2
had measured a real improvement first (cap-scale STORY contrast +60 / +71 / +147 %). A metric
that rose while the picture stayed wrong is measuring one term of what the eye sees. Meanwhile
the same generator, in the same repository, produced something the user volunteered praise for:

    "Die Rueckseite der boards gefaellt mir sehr gut!"   -- the board BACKS (ModBuild 276)

So the accepted class and the rejected class are both gpt-image-2 output, both re-based, both
shipped through `BoardLit`. **Something else separates them, and it is measurable rather than a
matter of taste.** This module measures it, with a NULL control and a KNOWN-POSITIVE control,
before anything is generated.

WHAT IS MEASURED, AND IN WHOSE UNITS
------------------------------------
Everything is computed in OBJECT-NORMALISED coordinates: each source is resampled so its SHORT
side is `NORM` texels, whatever its real size, so "a feature a sixth of the frame" means the
same number of texels on a 480 mm board back and on a 53 mm keycap. That is the only way the
two are comparable at all -- and it is also the axis round 2 was working on (feature size as a
fraction of the frame) taken one step further: not how BIG the features are, but whether the
picture has a LAYOUT.

    M1  OCTAVE SPECTRUM      sigma of each band-pass octave (1/2 .. 1/64 of the object),
                             per cent of the mean. Round 2's STORY/GRAIN split, resolved.
    M2  NSI                  non-stationarity index: between-tile contrast / within-tile
                             contrast over an 8x8 grid. A swatch is stationary BY
                             CONSTRUCTION, so this sits at its null value for one.
    M3  REG                  REGISTRATION. The mean luminance as a function of normalised
                             distance to the frame's border, peak-to-trough, per cent of the
                             mean. A rim, a batten, a bezel, a border -- anything that makes
                             WHERE YOU ARE matter -- shows here. A swatch cannot have it:
                             translate a swatch and it is the same swatch.
    M4  COH / GINI           contour coherence (structure tensor at 1/32 of the object) and
                             the Gini coefficient of gradient magnitude. A made object's
                             edges are FEW, LONG and ORIENTED; grain is dense, short and
                             isotropic.

WHAT THESE METRICS CANNOT SEE, stated because round 2's did not state it
------------------------------------------------------------------------
* None of them knows what the picture DEPICTS. A rim drawn in the wrong place scores exactly
  like a rim drawn in the right place. M3 in particular is blind to whether the registration
  agrees with the MESH -- that is what `cap_atlas.zone_report` is for.
* M3 is a border profile only. An object whose structure is a diagonal seam and no border term
  scores near-null on M3 and high on M4; both are "made". They are reported separately for
  that reason and are never summed into a score.
* All four are computed on the ALBEDO. The shipped picture is albedo x state colour x the
  shader's two baked lights; the state colour is a uniform multiply, so it cannot move any of
  these four (they are all mean-relative), but the normal map can and is not modelled here.
* The null control fixes what "zero" means for each metric on a stationary field of the same
  spectrum. It does NOT give a significance test; the error bar is the spread over repeated
  draws and is printed with it.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
PREP = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, PREP)

REPO = os.path.dirname(os.path.dirname(PREP))
OUT = os.path.join(PREP, "out")
BUNDLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")

NORM = 256          # object-normalised short side
OCTAVES = (2, 4, 8, 16, 32, 64)     # 1/2 .. 1/64 of the object's short side
STORY_CUT = 10.0    # coarser than a tenth of the object is STORY (round 2's own cut)

R2_PLATES = {"oak": "keycap2_plate_oak_b.png",
             "steel": "keycap2_plate_steel.png",
             "bronze": "keycap2_plate_bronze_d.png"}
STYLES = ("oak", "steel", "bronze")
ATLAS = {"oak": "KeycapOak", "steel": "KeycapSteel", "bronze": "KeycapBronze"}
NULL_SEEDS = (11, 22, 33, 44, 55)


# ------------------------------------------------------------------------------------------
# helpers
# ------------------------------------------------------------------------------------------
def _lum(rgb):
    return rgb.mean(axis=2) if rgb.ndim == 3 else rgb


def _box1(a, radius, axis):
    if radius < 1:
        return a
    k = 2 * radius + 1
    pad = [(0, 0), (0, 0)]
    pad[axis] = (radius, radius)
    p = np.pad(a, pad, mode="edge")
    c = np.cumsum(p, axis=axis)
    zero = np.zeros_like(np.take(c, [0], axis=axis))
    c = np.concatenate([zero, c], axis=axis)
    n = a.shape[axis]
    hi = np.take(c, np.arange(k, k + n), axis=axis)
    lo = np.take(c, np.arange(0, n), axis=axis)
    return (hi - lo) / float(k)


def _blur(a, sigma):
    """Gaussian, approximated by three box passes, IN FLOAT.

    The first version of this routine went through `PIL.ImageFilter.GaussianBlur`, which
    quantises to 8 bits on the way in and on the way out. On the STRUCTURE TENSOR that is not a
    rounding detail: the tensor's entries are products of gradients, order 1e-4, so 1/255
    quantisation makes `det` negative, drives one eigenvalue below zero and sends the coherence
    ratio to 1e7 -- the instrument's first output reported COH values of 4.4e7 on a field whose
    coherence is bounded by 1. A new instrument's first output is a hypothesis; this one was
    falsified by its own bound.
    """
    if sigma <= 0.0:
        return a
    r = max(1, int(round(sigma * 1.2)))
    out = a
    for _ in range(3):
        out = _box1(_box1(out, r, 0), r, 1)
    return out


def normalise(img, norm=NORM):
    """Resample so the SHORT side is `norm`, aspect preserved. Object-normalised coordinates."""
    h, w = img.shape[:2]
    s = norm / float(min(h, w))
    nw, nh = max(1, int(round(w * s))), max(1, int(round(h * s)))
    mode = "RGB" if img.ndim == 3 else "L"
    im = Image.fromarray((np.clip(img, 0, 1) * 255.0 + 0.5).astype(np.uint8), mode)
    return np.asarray(im.resize((nw, nh), Image.Resampling.LANCZOS), dtype=np.float64) / 255.0


def load(path):
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0


def cell_of(atlas_path, index, grid=(4, 4)):
    """One cell of a keycap atlas. The cell IS the cap's whole footprint -- the mesh UVs are
    planar over it (`CardMesh`: Uv(p) = (p.x/w + 0.5, p.y/h + 0.5)) -- so a cell in
    object-normalised coordinates is the button, edge to edge."""
    a = load(atlas_path)
    cols, rows = grid
    ch, cw = a.shape[0] // rows, a.shape[1] // cols
    r, c = index // cols, index % cols
    return a[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw]


# ------------------------------------------------------------------------------------------
# M1 octave spectrum
# ------------------------------------------------------------------------------------------
def octave_report(lum, norm=NORM):
    m = max(float(lum.mean()), 1e-9)
    total = 100.0 * float(lum.std()) / m
    lo10 = _blur(lum, norm / STORY_CUT / 3.0)
    story = 100.0 * float(lo10.std()) / m
    grain = 100.0 * float((lum - lo10).std()) / m
    bands, prev = [], lum
    for k in OCTAVES:
        lo = _blur(lum, norm / float(k) / 3.0)
        bands.append(100.0 * float((prev - lo).std()) / m)
        prev = lo
    return dict(total=total, story=story, grain=grain, bands=bands)


# ------------------------------------------------------------------------------------------
# M2 non-stationarity
# ------------------------------------------------------------------------------------------
def nsi(lum, tiles=8):
    h, w = lum.shape
    th, tw = h // tiles, w // tiles
    means, sds = [], []
    for r in range(tiles):
        for c in range(tiles):
            t = lum[r * th:(r + 1) * th, c * tw:(c + 1) * tw]
            means.append(float(t.mean()))
            sds.append(float(t.std()))
    return float(np.std(means)) / max(float(np.mean(sds)), 1e-9)


# ------------------------------------------------------------------------------------------
# M3 registration -- the border profile
# ------------------------------------------------------------------------------------------
def registration(lum, bins=24):
    """Mean luminance as a function of normalised distance to the border, peak-to-trough, as a
    per cent of the mean.

    Distance is measured in the frame's OWN units on each axis (so a 3:2 frame is not biased by
    its long side): d = min(x, 1-x, y, 1-y) with x, y in [0, 1]. d = 0 is the very edge, d = 0.5
    the centre.

    A stationary field gives whatever its low-frequency noise happens to average to along these
    contours -- which is NOT zero, and is exactly what the null control measures.
    """
    h, w = lum.shape
    ys = (np.arange(h) + 0.5) / h
    xs = (np.arange(w) + 0.5) / w
    dy = np.minimum(ys, 1.0 - ys)[:, None]
    dx = np.minimum(xs, 1.0 - xs)[None, :]
    d = np.minimum(np.broadcast_to(dy, (h, w)), np.broadcast_to(dx, (h, w)))
    idx = np.clip((d / 0.5 * bins).astype(int), 0, bins - 1)
    prof = np.array([lum[idx == b].mean() if (idx == b).any() else np.nan for b in range(bins)])
    m = max(float(lum.mean()), 1e-9)
    return 100.0 * float(np.nanmax(prof) - np.nanmin(prof)) / m, prof / m


# ------------------------------------------------------------------------------------------
# M4 coherence + gradient Gini
# ------------------------------------------------------------------------------------------
def coherence(lum, norm=NORM):
    s = _blur(lum, norm / 128.0)                 # kill per-texel noise, keep contours
    gy, gx = np.gradient(s)
    w = norm / 32.0                              # structure tensor at 1/32 of the object
    jxx, jyy, jxy = _blur(gx * gx, w), _blur(gy * gy, w), _blur(gx * gy, w)
    tr = jxx + jyy
    disc = np.sqrt(np.maximum((jxx - jyy) ** 2 + 4.0 * jxy * jxy, 0.0))
    # coh = (l1 - l2) / (l1 + l2) = disc / tr, and tr >= disc >= 0 exactly, so this is bounded
    # by 1 by construction -- which is what the first version was not.
    coh = disc / np.maximum(tr, 1e-15)
    mag = np.sqrt(gx * gx + gy * gy)
    weighted = float((coh * mag).sum() / max(mag.sum(), 1e-12))
    flat = np.sort(mag.ravel())
    n = flat.size
    cum = np.cumsum(flat)
    gini = float((n + 1 - 2.0 * (cum.sum() / max(cum[-1], 1e-12))) / n)
    return weighted, gini


# ------------------------------------------------------------------------------------------
# the controls
# ------------------------------------------------------------------------------------------
def null_swatch(seed, beta=1.6, n=NORM):
    """A STATIONARY field with a 1/f^beta spectrum -- a swatch, by construction. Every metric's
    reading on this is what 'no object' looks like; nothing below is believed without it."""
    rng = np.random.default_rng(seed)
    f = np.fft.fftfreq(n)
    r = np.sqrt(f[:, None] ** 2 + f[None, :] ** 2)
    r[0, 0] = 1.0 / n
    amp = r ** (-beta / 2.0)
    ph = np.exp(2j * np.pi * rng.random((n, n)))
    a = np.real(np.fft.ifft2(amp * ph))
    a = (a - a.mean()) / max(a.std(), 1e-9)
    return np.clip(0.55 + 0.11 * a, 0.02, 0.99)


def surrogate(lum, seed):
    """A PHASE-RANDOMISED surrogate of this very image: identical power spectrum, destroyed
    layout.

    This is the null that matters, and it is stronger than `null_swatch` because it is matched
    to the source. Fourier phase carries WHERE things are; amplitude carries how much of each
    scale there is. Randomising the phase and keeping the amplitude leaves a field with the
    SAME octave spectrum -- the same STORY contrast, the same grain, the same everything round
    2 measured -- and no object in it at all.

    So: if a plate's REG and NSI are inside the spread of its own surrogates, then round 2's
    numbers are all it has, and 'einheitlich' is exactly the right word for it.
    """
    rng = np.random.default_rng(seed)
    f = np.fft.fft2(lum - lum.mean())
    amp = np.abs(f)
    # The phase is taken from a REAL white-noise field's own transform, so it is Hermitian by
    # construction and the inverse transform below is real without any energy being discarded.
    ph = np.angle(np.fft.fft2(rng.standard_normal(lum.shape)))
    out = np.real(np.fft.ifft2(amp * np.exp(1j * ph)))
    out = out / max(out.std(), 1e-12) * max(float((lum - lum.mean()).std()), 1e-12)
    return np.clip(out + lum.mean(), 0.0, 1.0)


def surrogate_null(lum, draws=7, seed0=9001):
    """(mean, sd) of each metric over `draws` phase-randomised surrogates of `lum`."""
    ms = [measure(surrogate(lum, seed0 + 137 * k)) for k in range(draws)]
    keys = ("total", "story", "grain", "nsi", "reg", "coh", "gini")
    return ({k: float(np.mean([m[k] for m in ms])) for k in keys},
            {k: float(np.std([m[k] for m in ms])) for k in keys})


def positive_object(seed, n=NORM):
    """A KNOWN-POSITIVE: a drawn OBJECT on the same stationary field -- a bezel band, a rim
    land, two battens and a row of rivets. If the metrics cannot tell this from `null_swatch`
    they are not measuring what they claim to."""
    base = null_swatch(seed, n=n)
    ys = (np.arange(n) + 0.5) / n
    xs = (np.arange(n) + 0.5) / n
    d = np.minimum(np.minimum(ys, 1 - ys)[:, None], np.minimum(xs, 1 - xs)[None, :])
    out = base.copy()
    out = out * np.where(d < 0.060, 0.72, 1.0)                      # outer chamfer, in shadow
    out = out * np.where((d >= 0.060) & (d < 0.105), 1.30, 1.0)     # rim land, burnished
    out = out * np.where((d >= 0.105) & (d < 0.135), 0.66, 1.0)     # inner chamfer, oxide
    out = out * np.where(d >= 0.135, 0.92, 1.0)                     # recessed field
    for cx in (0.34, 0.66):                                         # two battens
        out[:, np.abs(xs - cx) < 0.035] *= 0.80
    rng = np.random.default_rng(seed + 1)
    yy, xx = np.mgrid[0:n, 0:n]
    for k in range(8):                                              # rivets
        cy, cx = int(n * (0.20 + 0.08 * k)), int(n * (0.34 if k % 2 else 0.66))
        rad = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
        out = np.where(rad < n * 0.018, out * 1.35, out)
        out = np.where((rad >= n * 0.018) & (rad < n * 0.026), out * 0.70, out)
    out = out + 0.01 * rng.standard_normal((n, n))
    return np.clip(out, 0.02, 0.99)


# ------------------------------------------------------------------------------------------
# the report
# ------------------------------------------------------------------------------------------
def measure(lum, norm=NORM):
    o = octave_report(lum, norm)
    reg, _ = registration(lum)
    coh, gini = coherence(lum, norm)
    return dict(nsi=nsi(lum), reg=reg, coh=coh, gini=gini, **o)


HEAD = (f"{'source':<44}{'total':>7}{'STORY':>7}{'GRAIN':>7}"
        f"{'NSI':>7}{'REG':>7}{'COH':>7}{'GINI':>7}   octave sigma 1/2..1/64")


def row(name, m):
    b = " ".join(f"{v:4.1f}" for v in m["bands"])
    return (f"{name:<44}{m['total']:7.2f}{m['story']:7.2f}{m['grain']:7.2f}"
            f"{m['nsi']:7.3f}{m['reg']:7.2f}{m['coh']:7.3f}{m['gini']:7.3f}   {b}")


def run(out_dir=OUT, bundle=BUNDLE, dst=None, extra=()):
    lines = []

    def say(t=""):
        print(t)
        lines.append(t)

    say("=" * 132)
    say("PLATE FORENSICS -- the ACCEPTED board backs against the REJECTED cap plates, in "
        "object-normalised coordinates")
    say("=" * 132)
    say("Every source resampled so its SHORT side is 256 texels, so 'a sixth of the frame' is "
        "43 texels in all of them.")
    say("REG is the one a swatch cannot fake: mean luminance against distance-to-border, "
        "peak-to-trough, per cent of the mean.")
    say("")

    say("--- CONTROLS --------------------------------------------------------------------")
    say(HEAD)
    nulls = [measure(null_swatch(s)) for s in NULL_SEEDS]
    for s, mm in zip(NULL_SEEDS, nulls):
        say(row(f"NULL stationary swatch (seed {s})", mm))
    for key in ("story", "nsi", "reg", "coh", "gini"):
        v = np.array([m[key] for m in nulls])
        say(f"    NULL {key.upper():<6} mean {v.mean():7.3f}  sd {v.std():6.3f}"
            "   <- what 'no object' reads")
    say("")
    for s in (11, 22):
        say(row(f"KNOWN-POSITIVE drawn object (seed {s})", measure(positive_object(s))))
    say("")

    say("--- THE ACCEPTED CLASS: the board BACKS as generated (ModBuild 276) --------------")
    say(HEAD)
    backs = {}
    for style in STYLES:
        p = os.path.join(out_dir, f"board_back_{style}.png")
        if not os.path.isfile(p):
            say(f"    MISSING {p}")
            continue
        backs[style] = measure(_lum(normalise(load(p))))
        say(row(f"board_back_{style}.png  (1536x1024)", backs[style]))
    say("")

    say("--- THE REJECTED CLASS: the cap PLATES as generated ------------------------------")
    say(HEAD)
    caps = {}
    for style in STYLES:
        p1 = os.path.join(out_dir, f"keycap_plate_{style}.png")
        if os.path.isfile(p1):
            say(row(f"round 1  keycap_plate_{style}.png", measure(_lum(normalise(load(p1))))))
    for style in STYLES:
        p2 = os.path.join(out_dir, R2_PLATES[style])
        if os.path.isfile(p2):
            caps[style] = measure(_lum(normalise(load(p2))))
            say(row(f"round 2  {R2_PLATES[style]}", caps[style]))
    say("")

    say("--- WHAT THE CAP ACTUALLY WEARS: the SHIPPED atlas cell (= the cap's whole face) --")
    say(HEAD)
    cells = {}
    for style in STYLES:
        p = os.path.join(bundle, f"{ATLAS[style]}_albedo.png")
        if not os.path.isfile(p):
            say(f"    MISSING {p}")
            continue
        cells[style] = measure(_lum(normalise(cell_of(p, 0))))
        say(row(f"{ATLAS[style]}_albedo cell 0 (Plain)", cells[style]))
    p = os.path.join(bundle, "KeycapGrain_albedo.png")
    if os.path.isfile(p):
        say(row("KeycapGrain_albedo (pre-round-1, shared)", measure(_lum(normalise(load(p))))))
    say("")

    if extra:
        say("--- ROUND 3 --------------------------------------------------------------------")
        say(HEAD)
        for name, path in extra:
            if os.path.isfile(path):
                say(row(name, measure(_lum(normalise(load(path))))))
            else:
                say(f"    MISSING {path}")
        say("")

    say("--- THE MATCHED NULL: every source against its OWN phase-randomised surrogate ----")
    say("  Same power spectrum, layout destroyed. z = (source - surrogate mean) / surrogate sd.")
    say("  A source whose REG z is ~0 has NO registration beyond what its spectrum alone")
    say("  produces -- i.e. it is a swatch, whatever its STORY contrast says.")
    say("")
    say(f"  {'source':<44}{'REG':>8}{'sur mean':>10}{'sur sd':>8}{'z':>8}"
        f"{'NSI':>8}{'z':>8}{'COH':>8}{'z':>8}")
    subjects = []
    for style in STYLES:
        p = os.path.join(out_dir, f"board_back_{style}.png")
        if os.path.isfile(p):
            subjects.append((f"ACCEPTED board_back_{style}", _lum(normalise(load(p)))))
    for style in STYLES:
        p = os.path.join(out_dir, R2_PLATES[style])
        if os.path.isfile(p):
            subjects.append((f"REJECTED r2 {R2_PLATES[style][:26]}", _lum(normalise(load(p)))))
    for style in STYLES:
        p = os.path.join(bundle, f"{ATLAS[style]}_albedo.png")
        if os.path.isfile(p):
            subjects.append((f"SHIPPED cell {ATLAS[style]}", _lum(normalise(cell_of(p, 0)))))
    subjects.append(("KNOWN-POSITIVE drawn object", positive_object(11)))
    subjects.append(("NULL stationary swatch", null_swatch(11)))
    for name, path in extra:
        if os.path.isfile(path):
            subjects.append((f"ROUND3 {name}", _lum(normalise(load(path)))))
    zs = {}
    for name, img in subjects:
        m = measure(img)
        mu, sd = surrogate_null(img)
        z = {k: (m[k] - mu[k]) / max(sd[k], 1e-9) for k in ("reg", "nsi", "coh")}
        zs[name] = z
        say(f"  {name:<44}{m['reg']:8.2f}{mu['reg']:10.2f}{sd['reg']:8.2f}{z['reg']:8.1f}"
            f"{m['nsi']:8.3f}{z['nsi']:8.1f}{m['coh']:8.3f}{z['coh']:8.1f}")
    say("")

    if backs and caps:
        say("--- THE SEPARATION ---------------------------------------------------------------")
        say("  'gap closed' is where the caps sit between the null swatch (0 %) and the "
            "accepted backs (100 %).")
        say("")
        for key, label in (("total", "total sigma"), ("story", "STORY sigma"), ("nsi", "NSI"),
                           ("reg", "REG"), ("coh", "COH"), ("gini", "GINI")):
            b = float(np.mean([backs[s][key] for s in backs]))
            c = float(np.mean([caps[s][key] for s in caps]))
            n = float(np.mean([m[key] for m in nulls]))
            gap = (c - n) / (b - n) * 100.0 if abs(b - n) > 1e-9 else float("nan")
            say(f"  {label:<12} null {n:8.3f}   caps {c:8.3f}   backs {b:8.3f}   "
                f"backs/caps x{b / max(c, 1e-9):5.2f}   gap closed {gap:6.1f} %")
        say("")
        nr = np.array([m["reg"] for m in nulls])
        say(f"  NULL REG is {nr.mean():.2f} +- {nr.std():.2f}. A plate whose REG sits in that "
            f"band is a swatch by this instrument's own definition,")
        say("  whatever its STORY contrast says.")

    if dst:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
    return lines


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--bundle", default=BUNDLE)
    ap.add_argument("--dst", default=None)
    a = ap.parse_args()
    run(a.out, a.bundle, a.dst)
