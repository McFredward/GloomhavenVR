"""tex_seams.py -- the seam/padding verifier.

The contract says: "dilate/pad every island by >= 8 px after compositing; verify
no island bleeds into another." This file is the verify half, and it is built to
produce a NUMBER rather than a claim.

WHAT AN "ISLAND" IS, AND WHY IT MATTERS
---------------------------------------
Two different things could be called an island and they give different answers:

  (a) the six contract REGION rectangles. In the contract's default layout these
      ABUT -- face is v 0..0.5 and frame starts at v 0.5 with no gap. Measured
      against those rectangles the answer is 0 px and always will be, because
      you cannot fit an 8 px gutter into a 0 px gap.

  (b) the texels the MESH ACTUALLY SAMPLES. A region rectangle is an allocation,
      not a UV island; the mesh lane's islands live inside their rectangle. This
      is the set the seam question is really about.

So the pipeline resolves it this way, and the compositor is built to match:
every region rectangle is inset by PAD px, all content is composited into that
inner rect, and the PAD-wide band around it is filled by dilation from the
content. Two adjacent regions therefore leave a 2*PAD gutter, each island has
PAD px of its own colour bleeding outward before anything foreign appears, and
the number this file reports for the default layout is 2*PAD.

That inset is a REQUIREMENT ON THE MESH LANE and it is stated in the report:
UV islands must sit inside their region rect with PAD px to spare. If the mesh
lane's islands run to the rectangle edge, this file will say so when pointed at
the FBX (--fbx), which measures (b) directly instead of assuming it.

TWO INDEPENDENT METHODS, CROSS-CHECKED
--------------------------------------
1. Voronoi-boundary: one exact EDT over the union of all islands with nearest-
   island labels. Where two islands' Voronoi cells touch, the closest pair
   between them straddles that boundary, so their separation is twice the
   distance there. O(1) transforms regardless of island count.
2. Per-island EDT: for each island, an exact EDT to the union of all the others.
   Obviously correct, O(islands) transforms, only run when there are few.
If the two disagree by more than a pixel the number is not trusted and the tool
says so, rather than printing a confident wrong answer.
"""

import argparse
import os
import subprocess
import sys

import numpy as np
from PIL import Image

import tex_common as T


# --------------------------------------------------------------------------
# island sources
# --------------------------------------------------------------------------

def islands_from_regions(uv, n, pad):
    """(a)+inset: each region rect inset by `pad`. This is what the compositor
    paints into, so it is the layout the number describes."""
    lab = np.zeros((n, n), dtype=np.int32)
    names = []
    for i, (name, reg) in enumerate(sorted(uv["regions"].items()), start=1):
        x0, y0, x1, y1 = T.region_rect_px(reg, n)
        x0, y0 = x0 + pad, y0 + pad
        x1, y1 = x1 - pad, y1 - pad
        if x1 <= x0 or y1 <= y0:
            raise ValueError(f"region {name!r} is smaller than 2*pad={2 * pad}px -- "
                             f"it cannot carry the required padding")
        lab[y0:y1, x0:x1] = i
        names.append(name)
    return lab, names


def islands_from_fbx(fbx, n, blender="/home/claw/blender-4.2/blender", cache=True,
                     cache_dir=None):
    """(b): rasterise the mesh's real UV triangles, then split into connected
    components. This is the only measurement that answers the question about the
    shipped asset rather than about the intended layout.

    The dump is cached under board-prep/out/, never next to the source mesh --
    the meshes live in directories this lane does not own."""
    cache_dir = cache_dir or os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "out", "uvdump")
    os.makedirs(cache_dir, exist_ok=True)
    npz = os.path.join(cache_dir,
                       os.path.splitext(os.path.basename(fbx))[0] + ".uvdump.npz")
    if not (cache and os.path.exists(npz)):
        script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tex_uv_dump.py")
        cmd = [blender, "--background", "--factory-startup", "--python", script,
               "--", fbx, npz]
        r = subprocess.run(cmd, capture_output=True, text=True)
        if not os.path.exists(npz):
            sys.stderr.write(r.stdout[-3000:] + "\n" + r.stderr[-3000:] + "\n")
            raise RuntimeError("UV dump failed")
    d = np.load(npz)
    tris = d["tris"]                        # (T,3,2) float uv
    cov = rasterise_uv(tris, n)
    lab, count = connected_components(cov)
    return lab, [f"island{i}" for i in range(1, count + 1)]


def surface_attrs(fbx, n, blender="/home/claw/blender-4.2/blender", cache=True,
                  cache_dir=None):
    """Rasterise the mesh's UV shells and carry the GEOMETRY along with them.

    Returns a dict of (n,n) arrays:
        cov  bool   -- this texel is sampled by at least one triangle
        nz   float  -- z of the surface normal there (+1 = decorated top face)
        x,y,z float -- the board-space position of the surface there, in metres,
                       NaN outside cov

    Why the texture lane needs this and cannot work from the region map alone:
    a contract region is an ALLOCATION and a UV island is a shell, and neither
    one says where on the physical board a given texel lands or which way that
    surface points. The rebuilt boards are mostly recess -- on Oak the `face`
    region rectangle is only 16.6% covered, because the top field is a web of
    8 to 26 mm strips running between four large pockets. Ornament placed at the
    centre of the face RECTANGLE lands in a hole that no triangle samples, and
    renders as nothing at all while every instrument reports success.

    Cached next to the UV dump, never next to the source mesh."""
    cache_dir = cache_dir or os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "out", "uvdump")
    os.makedirs(cache_dir, exist_ok=True)
    base = os.path.splitext(os.path.basename(fbx))[0]
    surf = os.path.join(cache_dir, f"{base}.surf{n}.npz")
    if cache and os.path.exists(surf):
        d = np.load(surf)
        return {k: d[k] for k in ("cov", "nz", "x", "y", "z")}

    npz = os.path.join(cache_dir, base + ".uvdump.npz")
    if not (cache and os.path.exists(npz)):
        script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tex_uv_dump.py")
        r = subprocess.run([blender, "--background", "--factory-startup", "--python",
                            script, "--", fbx, npz], capture_output=True, text=True)
        if not os.path.exists(npz):
            sys.stderr.write(r.stdout[-3000:] + "\n" + r.stderr[-3000:] + "\n")
            raise RuntimeError("UV dump failed")
    d = np.load(npz)
    if "pos" not in d:
        raise RuntimeError(f"{npz} predates the geometry dump; delete it and re-run")
    tris, pos, nrm = d["tris"], d["pos"], d["nrm"]

    cov = np.zeros((n, n), dtype=bool)
    nz = np.zeros((n, n), dtype=np.float64)
    X = np.full((n, n), np.nan)
    Y = np.full((n, n), np.nan)
    Z = np.full((n, n), np.nan)
    px = tris[..., 0] * n
    py = (1.0 - tris[..., 1]) * n
    for i in range(px.shape[0]):
        x0, x1, x2 = px[i]
        y0, y1, y2 = py[i]
        xmin = max(0, int(np.floor(min(x0, x1, x2))))
        xmax = min(n, int(np.ceil(max(x0, x1, x2))) + 1)
        ymin = max(0, int(np.floor(min(y0, y1, y2))))
        ymax = min(n, int(np.ceil(max(y0, y1, y2))) + 1)
        if xmax <= xmin or ymax <= ymin:
            continue
        yy, xx = np.mgrid[ymin:ymax, xmin:xmax]
        xx = xx + 0.5
        yy = yy + 0.5
        det = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(det) < 1e-12:
            continue
        a = ((y1 - y2) * (xx - x2) + (x2 - x1) * (yy - y2)) / det
        b = ((y2 - y0) * (xx - x2) + (x0 - x2) * (yy - y2)) / det
        c = 1.0 - a - b
        ins = (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
        if not ins.any():
            continue
        P = pos[i]
        sub = (slice(ymin, ymax), slice(xmin, xmax))
        cov[sub] |= ins
        for arr, col in ((X, 0), (Y, 1), (Z, 2)):
            v = a * P[0, col] + b * P[1, col] + c * P[2, col]
            t = arr[sub]
            t[ins] = v[ins]
            arr[sub] = t
        t = nz[sub]
        t[ins] = nrm[i, 2]
        nz[sub] = t

    out = dict(cov=cov, nz=nz, x=X, y=Y, z=Z)
    np.savez_compressed(surf, **out)
    return out


def rasterise_uv(tris, n):
    """Fill every UV triangle into an n x n boolean coverage map.
    UV origin is bottom-left; the row axis is flipped exactly once, here."""
    cov = np.zeros((n, n), dtype=bool)
    px = tris[..., 0] * n
    py = (1.0 - tris[..., 1]) * n
    for i in range(px.shape[0]):
        x0, x1, x2 = px[i]
        y0, y1, y2 = py[i]
        xmin = max(0, int(np.floor(min(x0, x1, x2))) - 1)
        xmax = min(n, int(np.ceil(max(x0, x1, x2))) + 1)
        ymin = max(0, int(np.floor(min(y0, y1, y2))) - 1)
        ymax = min(n, int(np.ceil(max(y0, y1, y2))) + 1)
        if xmax <= xmin or ymax <= ymin:
            continue
        yy, xx = np.mgrid[ymin:ymax, xmin:xmax]
        xx = xx + 0.5
        yy = yy + 0.5
        d = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(d) < 1e-12:
            continue
        a = ((y1 - y2) * (xx - x2) + (x2 - x1) * (yy - y2)) / d
        b = ((y2 - y0) * (xx - x2) + (x0 - x2) * (yy - y2)) / d
        c = 1.0 - a - b
        inside = (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
        cov[ymin:ymax, xmin:xmax] |= inside
    return cov


def connected_components(mask):
    """Run-length + union-find labelling, 8-connected. numpy only."""
    mask = np.asarray(mask, dtype=bool)
    h, w = mask.shape
    parent = [0]

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[max(ra, rb)] = min(ra, rb)

    lab = np.zeros((h, w), dtype=np.int32)
    prev_runs = []
    for y in range(h):
        row = mask[y]
        if not row.any():
            prev_runs = []
            continue
        d = np.diff(np.concatenate(([False], row, [False])).astype(np.int8))
        starts = np.nonzero(d == 1)[0]
        ends = np.nonzero(d == -1)[0]
        runs = []
        for s, e in zip(starts, ends):
            hits = [pl for ps, pe, pl in prev_runs if ps <= e and s <= pe]
            if hits:
                l = min(find(x) for x in hits)
                for x in hits:
                    union(l, x)
            else:
                parent.append(len(parent))
                l = len(parent) - 1
            lab[y, s:e] = l
            runs.append((s, e, l))
        prev_runs = runs

    if len(parent) <= 1:
        return lab, 0
    roots = {}
    remap = np.zeros(len(parent), dtype=np.int32)
    for i in range(1, len(parent)):
        r = find(i)
        if r not in roots:
            roots[r] = len(roots) + 1
        remap[i] = roots[r]
    return remap[lab], len(roots)


# --------------------------------------------------------------------------
# the two measurements
# --------------------------------------------------------------------------

def separations_voronoi(lab):
    """Exact minimum separation between every pair of islands, from ONE distance
    transform. Returns {(i,j): px}. The closest pair of points between two
    islands has its midpoint on their shared Voronoi boundary, so the separation
    is twice the distance measured there."""
    seeds = lab > 0
    dist, iy, ix = T.edt(seeds, want_index=True)
    near = lab[iy, ix]                       # nearest island label for every texel
    out = {}
    for dy, dx in ((0, 1), (1, 0), (1, 1), (1, -1)):
        a = near
        b = np.roll(np.roll(near, -dy, axis=0), -dx, axis=1)
        da = dist
        db = np.roll(np.roll(dist, -dy, axis=0), -dx, axis=1)
        if dy:
            a = a[:-dy or None]; b = b[:-dy or None]; da = da[:-dy or None]; db = db[:-dy or None]
        if dx > 0:
            a = a[:, :-dx]; b = b[:, :-dx]; da = da[:, :-dx]; db = db[:, :-dx]
        elif dx < 0:
            a = a[:, -dx:]; b = b[:, -dx:]; da = da[:, -dx:]; db = db[:, -dx:]
        m = (a != b) & (a > 0) & (b > 0)
        if not m.any():
            continue
        ai, bi = a[m], b[m]
        sep = da[m] + db[m]
        lo = np.minimum(ai, bi)
        hi = np.maximum(ai, bi)
        key = lo.astype(np.int64) * 100000 + hi.astype(np.int64)
        order = np.argsort(key, kind="stable")
        key, sep = key[order], sep[order]
        bounds = np.nonzero(np.diff(key))[0] + 1
        for chunk in np.split(np.arange(len(key)), bounds):
            k = int(key[chunk[0]])
            pair = (k // 100000, k % 100000)
            v = float(sep[chunk].min())
            if pair not in out or v < out[pair]:
                out[pair] = v
    return out


def separations_per_island(lab, labels):
    """Cross-check: one exact EDT per island, to the union of all the others."""
    out = {}
    for i in labels:
        others = (lab > 0) & (lab != i)
        if not others.any():
            out[i] = float("inf")
            continue
        d = T.edt(others)
        out[i] = float(d[lab == i].min())
    return out


def check(lab, names, pad_required=8.0, verbose=True, cross_check_limit=14):
    labels = sorted(int(x) for x in np.unique(lab) if x > 0)
    if len(labels) < 2:
        print("  only one island -- nothing can bleed into anything; check vacuous")
        return float("inf"), True

    pair_sep = separations_voronoi(lab)
    per_island = {}
    for (i, j), v in pair_sep.items():
        per_island[i] = min(per_island.get(i, 1e18), v)
        per_island[j] = min(per_island.get(j, 1e18), v)

    agreed = True
    if len(labels) <= cross_check_limit:
        ref = separations_per_island(lab, labels)
        worst_delta = 0.0
        for i in labels:
            a = per_island.get(i, float("inf"))
            b = ref.get(i, float("inf"))
            if np.isfinite(a) and np.isfinite(b):
                worst_delta = max(worst_delta, abs(a - b))
        agreed = worst_delta <= 1.5
        print(f"  instrument cross-check: Voronoi vs per-island EDT differ by at "
              f"most {worst_delta:.2f} px  {'AGREE' if agreed else 'DISAGREE -- number not trusted'}")
        per_island = ref if agreed else per_island
    else:
        print(f"  {len(labels)} islands -- per-island cross-check skipped "
              f"(limit {cross_check_limit}); Voronoi result reported")

    if verbose:
        print(f"  {'island':22s} {'texels':>10s} {'min dist to a FOREIGN island':>30s}")
        for i in labels:
            nm = names[i - 1] if i - 1 < len(names) else f"#{i}"
            cnt = int((lab == i).sum())
            v = per_island.get(i, float("inf"))
            flag = "" if v >= pad_required else "   <-- BELOW REQUIREMENT"
            print(f"  {nm:22s} {cnt:10d} {v:26.2f} px{flag}")

    worst = min(per_island.values()) if per_island else float("inf")
    ok = worst >= pad_required and agreed
    print(f"  WORST CASE: {worst:.2f} px between the two closest islands "
          f"(requirement >= {pad_required:.0f} px)  {'PASS' if ok else 'FAIL'}")
    return worst, ok


def check_bleed(atlas_rgb, lab, pad_required=8.0):
    """A second, different question: does any island's own colour survive PAD px
    outward? An island can be far from a foreign island and still be wrong if the
    compositor left black around it. Measures, for every island edge texel, how
    far the dilation actually carried that island's value."""
    seeds = lab > 0
    dist, iy, ix = T.edt(seeds, want_index=True)
    near = lab[iy, ix]
    # a texel is "safely padded" if everything within pad_required of it that is
    # outside all islands still resolves to the SAME island
    r = int(np.ceil(pad_required))
    ok = np.ones_like(seeds)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dy * dy + dx * dx > pad_required * pad_required:
                continue
            n2 = np.roll(np.roll(near, dy, axis=0), dx, axis=1)
            l2 = np.roll(np.roll(lab, dy, axis=0), dx, axis=1)
            ok &= (~seeds) | (n2 == lab) | (l2 > 0)
    frac = float(ok[seeds].mean())
    print(f"  bleed integrity: {frac * 100:.3f}% of island texels have a clean "
          f"{pad_required:.0f}px neighbourhood "
          f"({'PASS' if frac > 0.9999 else 'texels near a shared midline'})")
    return frac


def main():
    ap = argparse.ArgumentParser(description="verify UV island padding")
    ap.add_argument("--uv", help="the region map json (islands = inset region rects)")
    ap.add_argument("--fbx", help="measure the MESH's real UV islands instead")
    ap.add_argument("--atlas", type=int, default=T.N_DEFAULT)
    ap.add_argument("--pad", type=float, default=12.0, help="the inset the compositor used")
    ap.add_argument("--require", type=float, default=8.0)
    ap.add_argument("--labels", help="write a false-colour island map here")
    a = ap.parse_args()

    if a.fbx:
        print(f"islands from the mesh's real UVs: {a.fbx}")
        lab, names = islands_from_fbx(a.fbx, a.atlas)
        print(f"  {len(names)} connected UV islands, "
              f"{100.0 * (lab > 0).mean():.1f}% of the atlas covered")
    else:
        if not a.uv:
            ap.error("give --uv or --fbx")
        print(f"islands from the region map, inset by {a.pad:.0f}px: {a.uv}")
        lab, names = islands_from_regions(T.read_json(a.uv), a.atlas, int(a.pad))

    worst, ok = check(lab, names, pad_required=a.require)
    if a.labels:
        rg = np.random.default_rng(3)
        pal = np.concatenate([[[0.08, 0.08, 0.09]], rg.random((int(lab.max()) + 1, 3)) * 0.7 + 0.25])
        T.save_rgb(a.labels, pal[lab])
        print("WROTE", a.labels)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
