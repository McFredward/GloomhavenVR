#!/usr/bin/env python3
"""ORIENTATION-ANISOTROPY GATE for the control-board texture pack.

    python3 tex_aniso.py --check                      # report the installed maps
    python3 tex_aniso.py --check --with-reference     # ... beside the known-bad 271 pack
    python3 tex_aniso.py --selftest                   # the floor and the sensitivity, from scratch
    python3 tex_aniso.py --calibrate --geo <dir>      # rebuild the patch set (needs gen_geobuf output)

WHY THIS EXISTS, AND WHY IT DOES NOT ENFORCE ANYTHING
-----------------------------------------------------
ModBuild 274's boards were first rejected with: "alle boards auf dem Bild bestehen aus
vielen solchen horizontalen Streifen.  WO kommt das her?  Das sieht nicht gut aus".  All
three boards striped in the SAME direction, which three unrelated materials cannot do by
coincidence -- it is a property of the pipeline, and the numbers confirm it: the
pre-rework pack runs 6-13x isotropic and the shipped pack still runs 2-6x.

*** THAT COMPLAINT WAS THEN WITHDRAWN, AND HOW IT AROSE IS THE POINT OF THIS FILE. ***
The user checked the same build on hardware: "Ich hab es nun im Spiel geprueft und da sehe
ich diese Streifen nicht!  Es war also ein Renderfehler vom Vergleichsbild."  The picture
he had judged was `boards_shader_rake.png`, a station shot lit from a deliberately RAKING
angle to make relief legible -- and raking light is precisely the condition that maximises
directional relief.  The anisotropy is REAL in the data; it does not read at the angle and
the lighting the player actually has.

So this file MEASURES and REPORTS; it does not gate.  Anything it prints is a property of
the maps under a viewing condition the player does not occupy, and no contrast, material
character or dynamic range should ever be traded to bring a number here down.  Its worth
is as a REGRESSION guard: if one of these numbers jumps, that is worth looking at, because
the same defect at a larger amplitude WOULD read.  The thresholds are therefore set from
what shipped and reads fine, not from any aesthetic target.

WHAT IT MEASURES
----------------
The power spectrum of a square patch, binned into 18 bins over 180 degrees.  1.00x is a
perfectly isotropic field.  The angle reported is the direction the STRIPES RUN (the
spectrum's peak is perpendicular to that, and the conversion is done here).

FOUR THINGS THAT WERE WRONG IN EARLIER VERSIONS OF THIS TEST, ALL MEASURED
-------------------------------------------------------------------------
1.  AN AXIS-ONLY TEST CANNOT SEE A DIAGONAL.  The first attempt compared row-mean
    variance against column-mean variance and reported "no horizontal banding, 0.07x"
    on a board that visibly has it.  Any orientation test must bin the full 180 deg.

2.  MEASURING A NORMAL MAP'S GREYSCALE MEASURES THE GRADIENT OPERATOR.  A normal map's
    RGB is (dh/du, dh/dv, nz) up to sign, so its luminance is a DIRECTIONAL derivative
    of the height along the fixed vector (0.299, 0.587).  Fed a PERFECTLY ISOTROPIC
    height field, that statistic reads 2.14-2.38x -- so a "under 2.0x" bar measured
    that way is unreachable by any relief whatsoever.  Verified with synthetic
    isotropic fields at three spectral slopes.  The unbiased statistic for a normal map
    is the SLOPE MAGNITUDE sqrt(su^2+sv^2), which reads 1.10-1.18x on the same input
    and still reads 7-9x on a planted stripe field.  That is what is gated here.

3.  MOST OF THE ATLAS IS NOT THE BOARD.  Only ~20% of each 2048^2 atlas is covered by
    triangles; the rest is push-pull dilation fill, which is streaky by construction.
    There is NO fully-covered 256^2 patch anywhere in any of the three atlases, and the
    512^2 "face patch" used to report the first round of these numbers is only 43-47%
    board.  Patches here are 128^2 (85 mm of board), fully covered, and chosen for
    FLATNESS from the mesh's own AO pass -- so a straight moulding, which is
    legitimately a straight line, never lands in the sample.  The patch set is a
    property of the MESH, identical for every map of a style, so no map can steer it.

4.  A PLAIN ANNULUS MEASURES THE RADIAL POWER LAW.  On a 128^2 patch a steep but
    perfectly ISOTROPIC field (beta=4) reads 2.77x, purely because its energy sits in
    the few lowest shells where each orientation bin holds a handful of samples.  Each
    frequency is therefore divided by the mean energy of its own radial shell before
    binning.  After that the floor is 1.15-1.19x median / 1.33x max and is INDEPENDENT
    of beta (1.18 at beta 1, 2, 3 and 4 alike), while a field built the way
    tex_common.spectral_noise builds one still reads 3.5x at aniso=2 and 14.1x at
    aniso=60.  Both numbers are reproduced by --selftest.

Board space is NOT used, and that is deliberate: resampling a 2048^2 atlas up to the
4096x2048 board pass is a ~4.3x nearest-neighbour magnification, and it plants a 95 deg
artifact that reads 8.5x on an isotropic input.  The atlas texel grid is the measurement
grid, so nothing the instrument does can plant a direction.

THE THRESHOLDS
--------------
`aniso_baseline.json` records what each field measured on the build the user accepted on
hardware.  A field is flagged only when it exceeds its own recorded baseline by more than
`--tolerance` (default 1.5x).  There is no absolute bar, and an earlier draft of this file
carried one (2.00x) that was worse than useless: measured the way that draft measured a
normal map -- on its greyscale -- 2.00x is BELOW the instrument's own 2.2x floor for a
perfectly isotropic relief, so no relief whatsoever could ever have met it.
"""

import argparse
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

Image.MAX_IMAGE_PIXELS = None

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
TABLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")
PATCHES_JSON = os.path.join(HERE, "aniso_patches.json")

# The pre-rework pack, as a KNOWN-BAD reference row.  A gate that has never printed a
# failure has not been shown to work, so the gate carries its own positive control.
REFERENCE_REV = "f9d9985e"

STYLES = {
    "oak":    "PlayTray",
    "steel":  "PlayTray_9capjqp6",
    "bronze": "PlayTray_16vm268h",
}

BASELINE_JSON = os.path.join(HERE, "aniso_baseline.json")
TOLERANCE = 1.5          # flag a field only if it exceeds its own baseline by this factor
PATCH = 128
NBINS = 18


# ---------------------------------------------------------------------------
# the statistic
# ---------------------------------------------------------------------------

def orientation(patch, nbins=NBINS):
    """(stripe angle deg, peak/isotropic ratio) of a radially whitened power spectrum."""
    p = np.asarray(patch, np.float64)
    p = p - p.mean()
    n = min(p.shape)
    p = p[:n, :n]
    w = np.hanning(n)[:, None] * np.hanning(n)[None, :]
    F = np.abs(np.fft.fftshift(np.fft.fft2(p * w))) ** 2
    c = n // 2
    y, x = np.mgrid[-c:n - c, -c:n - c]
    r = np.hypot(x, y)
    # drop DC and the lighting/story block, and the corners past Nyquist
    m = (r > max(3.0, n / 32.0)) & (r < c)
    ri = np.rint(r).astype(int)
    nr = int(ri.max()) + 2
    shell = (np.bincount(ri[m], F[m], minlength=nr) /
             np.maximum(np.bincount(ri[m], minlength=nr), 1))
    W = F / np.maximum(shell[ri], 1e-30)
    ang = (np.degrees(np.arctan2(y, x)) + 180.0) % 180.0
    idx = (ang[m] / (180.0 / nbins)).astype(int) % nbins
    h = np.zeros(nbins)
    ct = np.zeros(nbins)
    np.add.at(h, idx, W[m])
    np.add.at(ct, idx, 1.0)
    h = h / np.maximum(ct, 1.0)
    h = h / h.mean()
    k = int(np.argmax(h))
    freq_ang = (k + 0.5) * (180.0 / nbins)
    return (freq_ang + 90.0) % 180.0, float(h[k])       # stripes run perpendicular


# ---------------------------------------------------------------------------
# the fields.  Each map contributes the field the EYE reads, not its raw bytes.
# ---------------------------------------------------------------------------

def srgb_to_linear(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def read_rgb(path):
    return np.asarray(Image.open(path).convert("RGB"), np.float64) / 255.0


def fields_of(kind, rgb01):
    """[(label, 2-D field, gated?)] for one map."""
    if kind == "albedo":
        lin = srgb_to_linear(rgb01)
        lum = 0.2126 * lin[..., 0] + 0.7152 * lin[..., 1] + 0.0722 * lin[..., 2]
        # chroma carries its own structure -- bronze's verdigris is nearly isoluminant,
        # so a luminance-only test is blind to exactly the band bronze stripes in.
        mx = rgb01.max(-1)
        mn = rgb01.min(-1)
        chroma = mx - mn
        return [("albedo.lum", lum, True), ("albedo.chroma", chroma, True)]
    if kind == "normal":
        n = rgb01 * 2.0 - 1.0
        nz = np.maximum(n[..., 2], 1e-3)
        su, sv = n[..., 0] / nz, n[..., 1] / nz
        grey = 0.299 * rgb01[..., 0] + 0.587 * rgb01[..., 1] + 0.114 * rgb01[..., 2]
        # |slope| is gated; the greyscale is printed for continuity with the numbers
        # that opened this round, and carries a ~2.2x floor of its own (see docstring).
        return [("normal.slope", np.hypot(su, sv), True),
                ("normal.grey*", grey, False)]
    if kind == "mrs":
        return [("mrs.metallic", rgb01[..., 0], True),
                ("mrs.roughness", rgb01[..., 1], True)]
    raise SystemExit("unknown map kind " + kind)


# ---------------------------------------------------------------------------
# the patch set -- a property of the MESH
# ---------------------------------------------------------------------------

def calibrate(geodir, size=PATCH, want=60, group=1):
    """Pick fully-covered, FLAT atlas patches of one face group from `gen_geobuf.py`.

    The geometry buffer is used rather than a camera pass because it is exact, and because
    it covers faces no camera reaches.  FLATNESS is judged from the SURFACE NORMAL, so a
    moulding edge or a recess wall -- legitimately a straight line, and never a stripe --
    cannot land in the sample.  The patch set is a property of the MESH alone, identical
    for every map of a style, so no map can steer its own score.

    Written once and committed; the mesh and its UVs are locked and on the wire-test
    harness, so this only needs rerunning if they ever change."""
    out = {}
    for style in STYLES:
        d = os.path.join(geodir, style)
        grp = np.load(os.path.join(d, "grp.npy"))
        nrm = np.load(os.path.join(d, "nrm.npy")).astype(np.float64)
        n = grp.shape[0]
        cov = grp == group
        im = Image.fromarray((cov * 255).astype(np.uint8))
        cov = np.asarray(im.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5))) > 127
        base = nrm[grp == group].mean(0) if (grp == group).any() else np.array([0.0, 0.0, 1.0])
        base = base / max(np.linalg.norm(base), 1e-9)
        dev = 1.0 - np.abs(nrm @ base)
        dev = np.where(grp == group, dev, 1.0)
        grad = np.asarray(Image.fromarray(np.clip(dev * 255, 0, 255).astype(np.uint8))
                          .filter(ImageFilter.GaussianBlur(2)), np.float64) / 255.0

        def block(z):
            I = np.cumsum(np.cumsum(np.pad(z, ((1, 0), (1, 0))), 0), 1)
            return (I[size:, size:] - I[:-size, size:] - I[size:, :-size] + I[:-size, :-size]) / (size * size)

        score = np.where(block(cov.astype(np.float64)) > 0.998, block(grad), np.inf)
        flat = score.ravel()
        picks = []
        for f in np.argsort(flat):
            if not np.isfinite(flat[f]):
                break
            y, x = divmod(int(f), score.shape[1])
            if all(abs(y - py) >= size // 2 or abs(x - px) >= size // 2 for py, px in picks):
                picks.append([y, x])
            if len(picks) >= want:
                break
        out[style] = picks
        print(f"  {style:7s} {len(picks)} flat fully-covered {size}px patches")
    meta = dict(size=size, atlas=n, group=group, patches=out,
                note="atlas-space patches; fully covered by triangles of the named face "
                     "group and flat by the mesh's own surface normal. Regenerate only "
                     "if the mesh or its UVs change.")
    with open(PATCHES_JSON, "w") as fh:
        json.dump(meta, fh, indent=1)
    print("wrote", PATCHES_JSON)


def load_patches():
    if not os.path.exists(PATCHES_JSON):
        raise SystemExit("no patch set: run --calibrate --passdir <dir> first")
    with open(PATCHES_JSON) as fh:
        return json.load(fh)


# ---------------------------------------------------------------------------
# scoring
# ---------------------------------------------------------------------------

def score_map(path, kind, picks, size):
    rgb = read_rgb(path)
    rows = []
    for label, field, gated in fields_of(kind, rgb):
        vals, angs = [], []
        for y, x in picks:
            p = field[y:y + size, x:x + size]
            if p.shape != (size, size) or np.std(p) < 1e-7:
                continue
            a, r = orientation(p)
            vals.append(r)
            angs.append(a)
        if not vals:
            rows.append((label, gated, None, None, None, None, 0))
            continue
        vals = np.array(vals)
        angs = np.array(angs)
        k = int(np.argmax(vals))
        # modal stripe direction across patches
        cnt = np.bincount((angs / (180.0 / NBINS)).astype(int) % NBINS, minlength=NBINS)
        mode = (int(np.argmax(cnt)) + 0.5) * (180.0 / NBINS)
        rows.append((label, gated, float(np.median(vals)), float(np.percentile(vals, 90)),
                     float(vals[k]), float(mode), len(vals)))
    return rows


def reference_pack(tmpdir):
    os.makedirs(tmpdir, exist_ok=True)
    got = {}
    for style, base in STYLES.items():
        got[style] = {}
        for kind in ("albedo", "normal", "mrs"):
            name = f"{base}_{kind}.png"
            dst = os.path.join(tmpdir, name)
            if not os.path.exists(dst):
                blob = subprocess.run(
                    ["git", "-C", REPO, "show",
                     f"{REFERENCE_REV}:unity/GloomhavenVR.Assets/Assets/Bundle/Table/{name}"],
                    capture_output=True)
                if blob.returncode != 0:
                    return None
                with open(dst, "wb") as fh:
                    fh.write(blob.stdout)
            got[style][kind] = dst
    return got


def load_baseline():
    if not os.path.exists(BASELINE_JSON):
        return None
    with open(BASELINE_JSON) as fh:
        return json.load(fh)


def run_check(mapdir, with_reference, tolerance, write_baseline=False):
    """Report every field of every map.  Flags a REGRESSION against the recorded baseline;
    there is deliberately no absolute bar -- see the module docstring."""
    meta = load_patches()
    size = meta["size"]
    base = load_baseline()
    fresh = {}
    flagged = []
    ref = None
    if with_reference:
        ref = reference_pack(os.path.join(HERE, ".aniso_ref"))
        if ref is None:
            print("  (reference pack unavailable -- not a git checkout?)")

    print(f"ORIENTATION ANISOTROPY -- {size}px flat fully-covered atlas patches, "
          f"{NBINS} bins over 180 deg")
    print("  1.00x = isotropic.  Instrument floor 1.15-1.19x median (--selftest).")
    print("  REPORT ONLY. Raking light exaggerates every number here; the user checked the")
    print("  shipped pack on hardware and could not see it. Flags are regressions against")
    print(f"  aniso_baseline.json by more than {tolerance:.2f}x, not aesthetic failures.")
    print("  'angle' is the direction the stripes RUN. 0 deg = along the atlas u axis.\n")
    hdr = (f"  {'map / field':26s} {'median':>8s} {'p90':>8s} {'worst':>8s} "
           f"{'angle':>7s} {'base':>8s}  n")
    for style, base_name in STYLES.items():
        picks = [tuple(p) for p in meta["patches"][style]]
        print(f"--- {style} ({len(picks)} patches) ---")
        print(hdr)
        for kind in ("albedo", "normal", "mrs"):
            path = os.path.join(mapdir, f"{base_name}_{kind}.png")
            if not os.path.exists(path):
                print(f"  {kind:26s}   MISSING {path}")
                continue
            for label, gated, med, p90, worst, mode, n in score_map(path, kind, picks, size):
                if med is None:
                    print(f"  {label:26s} {'constant':>8s}")
                    continue
                key = f"{style}/{label}"
                fresh[key] = round(med, 3)
                prev = (base or {}).get("fields", {}).get(key)
                mark = ""
                if gated and prev:
                    if med > prev * tolerance:
                        mark = "  REGRESSED"
                        flagged.append((key, med, prev))
                    else:
                        mark = "  ok"
                bs = f"{prev:8.2f}" if prev else f"{'-':>8s}"
                print(f"  {label:26s} {med:8.2f} {p90:8.2f} {worst:8.2f} {mode:7.1f} {bs}  {n}{mark}")
                if ref:
                    for rl, rg, rm, rp, rw, rmo, rn in score_map(ref[style][kind], kind, picks, size):
                        if rl == label:
                            print(f"    {'^ pre-rework 271':26s} {rm:8.2f} {rp:8.2f} {rw:8.2f} "
                                  f"{rmo:7.1f} {'':8s}  {rn}   (reference)")
        print()

    if write_baseline:
        with open(BASELINE_JSON, "w") as fh:
            json.dump(dict(note="Measured on the build the user accepted on hardware. "
                                "Values are the MEDIAN over the patch set. Report only: a "
                                "rise here is a regression to look at, never a bar to tune to.",
                           tolerance=TOLERANCE, fields=fresh), fh, indent=1, sort_keys=True)
        print(f"wrote baseline -> {BASELINE_JSON} ({len(fresh)} fields)")
        return 0
    if base is None:
        print("no aniso_baseline.json -- run once with --write-baseline to record one")
        return 0
    if flagged:
        print(f"REGRESSED -- {len(flagged)} field(s) more than {tolerance:.2f}x over baseline:")
        for k, m, b in flagged:
            print(f"  {k:34s} {m:.2f}x  (baseline {b:.2f}x)")
        return 1
    print("OK -- no field is more than "
          f"{tolerance:.2f}x over its recorded baseline")
    return 0


# ---------------------------------------------------------------------------
# selftest -- the floor and the sensitivity, both reproduced from scratch
# ---------------------------------------------------------------------------

def _iso(n, beta, seed):
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.fftfreq(n)[None, :]
    r = np.hypot(fx, fy)
    r[0, 0] = 1e-9
    return np.real(np.fft.ifft2((r ** (-beta / 2.0)) * np.exp(1j * rng.uniform(0, 2 * np.pi, (n, n)))))


def _aniso(n, deg, aniso, beta, seed):
    """Built exactly the way tex_common.spectral_noise builds one."""
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(n)[:, None] * n
    fx = np.fft.fftfreq(n)[None, :] * n
    t = np.radians(deg)
    a = fx * np.cos(t) + fy * np.sin(t)
    b = -fx * np.sin(t) + fy * np.cos(t)
    r = np.hypot(a * aniso, b / aniso)
    r[0, 0] = 1.0
    return np.real(np.fft.ifft2((r ** (-beta / 2.0)) * np.exp(1j * rng.uniform(0, 2 * np.pi, (n, n)))))


def selftest():
    n, sz = 512, PATCH
    picks = [(y, x) for y in range(0, n - sz, sz) for x in range(0, n - sz, sz)]
    bad = 0
    print("FLOOR -- perfectly isotropic fields must read near 1.0x at EVERY spectral slope")
    for beta in (1.0, 2.0, 3.0, 4.0):
        v = [orientation(_iso(n, beta, 10 + i)[y:y + sz, x:x + sz])[1]
             for i in range(3) for y, x in picks]
        med, mx = float(np.median(v)), float(max(v))
        ok = mx < 1.6
        bad += not ok
        print(f"  beta={beta:.0f}  median {med:5.2f}x  max {mx:5.2f}x   {'ok' if ok else 'FAIL'}")

    print("\nOPERATOR BIAS -- the same isotropic relief, read three ways")
    h = _iso(n, 2.0, 77)
    gu = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 4.0
    gv = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 4.0
    nz = 1.0 / np.sqrt(gu * gu + gv * gv + 1.0)
    rgb = np.stack([gu * nz, gv * nz, nz], -1) * 0.5 + 0.5
    for label, field, want in (
            ("height itself", h, True),
            ("normal SLOPE MAGNITUDE (gated)", np.hypot(gu, gv), True),
            ("normal GREYSCALE (biased, not gated)",
             0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2], False)):
        v = [orientation(field[y:y + sz, x:x + sz])[1] for y, x in picks]
        med = float(np.median(v))
        ok = (med < 1.6) == want
        bad += not ok
        print(f"  {label:38s} {med:5.2f}x   {'ok' if ok else 'FAIL'}"
              + ("" if want else "   <- an absolute 2.0x bar would sit BELOW this floor"))

    print("\nSENSITIVITY -- a planted stripe field must be seen, and its angle recovered")
    for deg in (0.0, 15.0, 95.0):
        for a in (2.0, 8.0, 60.0):
            f = _aniso(n, deg, a, 2.0, 5)
            res = [orientation(f[y:y + sz, x:x + sz]) for y, x in picks]
            med = float(np.median([r for _, r in res]))
            ang = float(np.median([g for g, _ in res]))
            # _aniso attenuates high frequencies along the `deg` axis, which STRETCHES
            # the features along `deg` in space -- so the stripes run at `deg` itself.
            want_ang = deg % 180.0
            ok = med > 2.5 and min(abs(ang - want_ang), 180 - abs(ang - want_ang)) <= 10.0
            bad += not ok
            print(f"  planted deg={deg:5.1f} aniso={a:5.1f}  {med:6.2f}x at {ang:5.1f} deg"
                  f"  (expect {want_ang:5.1f})   {'ok' if ok else 'FAIL'}")
    print("\n" + ("SELFTEST FAILED" if bad else "SELFTEST OK"))
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--calibrate", action="store_true")
    ap.add_argument("--geo", default=None, help="gen_geobuf.py output root (per-style subdirs)")
    ap.add_argument("--group", type=int, default=1, help="face group to sample: 1=FRONT, 2=BACK, 3=RIM")
    ap.add_argument("--maps", default=TABLE)
    ap.add_argument("--with-reference", action="store_true")
    ap.add_argument("--tolerance", type=float, default=TOLERANCE)
    ap.add_argument("--write-baseline", action="store_true")
    a = ap.parse_args()
    if a.selftest:
        sys.exit(selftest())
    if a.calibrate:
        if not a.geo:
            raise SystemExit("--calibrate needs --geo")
        calibrate(a.geo, group=a.group)
        return
    sys.exit(run_check(a.maps, a.with_reference, a.tolerance, a.write_baseline))


if __name__ == "__main__":
    main()
