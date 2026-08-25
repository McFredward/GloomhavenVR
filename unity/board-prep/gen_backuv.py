"""gen_backuv.py -- give the control board's BACK FACE a UV island you can actually paint into.

    /home/claw/blender-4.2/blender -b --factory-startup \
        --python unity/board-prep/gen_backuv.py -- <fbx> <style> [--out <path>] [--dry-run]

    style is one of  oak | steel | bronze  (labelling and a filename sanity check only)

WHY THIS EXISTS
---------------
The user reported that the boards' back and sides look untextured. They are not untextured -- they
are UNDER-textured, and the atlas measurement says so. Rasterising every face group's real UV
island into the 2048^2 atlas (Blender dump via tex_uv_dump.py, per-triangle areas and face normals)
gives:

    board   group   tris    surface              atlas coverage   texel density
    oak     FRONT   6500    2015.2 cm2 (41.8%)   18.96 %          1.99 tex/mm
    oak     BACK     108    2010.6 cm2 (41.7%)    0.97 %          0.45 tex/mm   <--
    oak     RIM     2012     581.2 cm2 (12.1%)    3.33 %          1.53 tex/mm
    steel   FRONT   5392    2059.2 cm2           17.53 %          1.89 tex/mm
    steel   BACK     304    2060.8 cm2            1.10 %          0.47 tex/mm   <--
    steel   RIM     1544     546.1 cm2            2.90 %          1.49 tex/mm
    bronze  FRONT  10476    1888.5 cm2           18.92 %          2.04 tex/mm
    bronze  BACK     124    2006.7 cm2            1.02 %          0.46 tex/mm   <--
    bronze  RIM     2092     540.8 cm2            3.82 %          1.72 tex/mm

The back plate carries roughly 42 % of the board's surface on roughly 1 % of the atlas: 4.4x lower
LINEAR texel density than the front, which is why any art put there dissolves into mush. There are
zero overlapped texels anywhere and about 75 % of each atlas is unused, so the fix is not to steal
texels from the front -- it is to stop wasting the empty ones.

(One correction to that table, measured here: for STEEL the "BACK 304 tris / 2060.8 cm2" row is a
normal-threshold classification that swept 204 rim-CHAMFER triangles into the back group. Those
chamfer faces are part of the rim islands and already sit at 1.398 tex/mm. The flat back PLATE --
the thing this script moves -- is 100 tris / 2012.6 cm2 on steel, 108 / 2010.6 on oak, 124 / 2006.7
on bronze. Oak and bronze match the table exactly; only steel's row was inflated.)

WHAT IT CHANGES, AND WHAT IT MUST NOT
-------------------------------------
Only the UV coordinates of the loops on the flat back plate. Everything else in the FBX -- vertex
positions, loop indices, polygon sizes, per-loop split normals (Unity is told to IMPORT them, not
recalculate them), and the ten anchor EMPTY transforms the hand-tuned VR config is measured from --
travels through a Blender import/export round trip untouched. That is a claim, so `gen_uvdiff.py`
measures it field by field afterwards; this script is not trusted on its own word.

THE BACK PLATE is identified GEOMETRICALLY, never from the UVs: polygons whose normal points along
-Y (n.y < -0.9) and all of whose vertices lie on the mesh's minimum-Y plane. The script then checks
that this set is EXACTLY one complete UV island -- no more, no less. That check is the safety
interlock: because the plate is a whole island, moving it creates no new UV seam and destroys none,
and no loop outside it can be affected. If a future board breaks that assumption the script aborts
rather than silently shearing a shared island in half.

HOW THE NEW ISLAND IS PLACED
----------------------------
1. Every UV triangle that is NOT on the back plate is rasterised into a 2048^2 coverage map, using
   the project's own rasteriser (texel-centre point sampling, row axis flipped exactly once, same
   as tex_seams.rasterise_uv) so the texel counts are comparable to what the texture lane reports.
2. The plate is a flat rectangle, so its unwrap is a plain planar projection onto the board's two
   long axes (object X = 636.4 mm, object Z = 316.4 mm). The only free parameters are the island's
   size, its orientation, and where it sits.
3. A binary search finds the LARGEST island of the plate's aspect ratio that fits anywhere in the
   free space with at least GUTTER texels of clear atlas on all four sides, trying both landscape
   (long axis along U, the orientation the shipped island uses) and portrait (long axis along V).
4. Among the fitting positions the script keeps pushing the required clearance up until only a
   handful of positions survive, then takes the lowest row / lowest column of those. That centres
   the island in its hole instead of jamming it against a neighbour, and it is deterministic.

Steps 1-4 read only faces OUTSIDE the back plate, so a second run of the script sees exactly the
same coverage map and computes exactly the same rectangle: it is idempotent, and re-running it on
its own output is a no-op.

WHAT IS ACHIEVABLE, AND WHAT IS NOT
-----------------------------------
The brief asked for ~1.6 tex/mm, i.e. about a 1024 x 512 texel island. That does not fit. The atlas
is 74-78 % unused but heavily FRAGMENTED: measured with an exact free-rectangle search at an 8
texel gutter, the largest hole that will take the plate's 2.011:1 aspect is

    oak     382 x 768 tx  ->  1.207 tex/mm
    steel   394 x 792 tx  ->  1.245 tex/mm
    bronze  374 x 752 tx  ->  1.182 tex/mm

all of them portrait, all of them in the lower half of the atlas. That is a 2.6-2.8x LINEAR gain
(7-8x the texels) over the shipped 0.42-0.46 tex/mm, and it is the ceiling for a move that is
allowed to touch one island and nothing else. Reaching 1.6 tex/mm needs a full repack of the front
and rim islands, which is a different and much riskier job.
"""

import os
import shutil
import sys
import tempfile
from collections import defaultdict

import bpy
import numpy as np

N_ATLAS = 2048          # the boards' albedo/normal/mrs atlases are all 2048^2
GUTTER = 8              # minimum clear texels between the new island and every other island
MAX_CLEARANCE = 96      # how far the centring search is allowed to push the clearance
STYLE_FILES = {
    "oak": "PlayTray_prepped.fbx",
    "steel": "PlayTray_9capjqp6.fbx",
    "bronze": "PlayTray_16vm268h.fbx",
}


# ---------------------------------------------------------------- rasteriser

def rasterise(tris, n):
    """Fill every UV triangle into an n x n boolean coverage map.

    Texel-centre point sampling with the row axis flipped exactly once -- identical to
    tex_seams.rasterise_uv, so the texel counts here mean the same thing they do there.
    """
    cov = np.zeros((n, n), dtype=bool)
    if len(tris) == 0:
        return cov
    px = tris[..., 0] * n
    py = (1.0 - tris[..., 1]) * n
    for i in range(px.shape[0]):
        x0, x1, x2 = px[i]
        y0, y1, y2 = py[i]
        xa = max(0, int(np.floor(min(x0, x1, x2))) - 1)
        xb = min(n, int(np.ceil(max(x0, x1, x2))) + 1)
        ya = max(0, int(np.floor(min(y0, y1, y2))) - 1)
        yb = min(n, int(np.ceil(max(y0, y1, y2))) + 1)
        if xb <= xa or yb <= ya:
            continue
        yy, xx = np.mgrid[ya:yb, xa:xb]
        xx = xx + 0.5
        yy = yy + 0.5
        d = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(d) < 1e-12:
            continue
        a = ((y1 - y2) * (xx - x2) + (x2 - x1) * (yy - y2)) / d
        b = ((y2 - y0) * (xx - x2) + (x0 - x2) * (yy - y2)) / d
        c = 1.0 - a - b
        cov[ya:yb, xa:xb] |= (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
    return cov


def summed_area(cov):
    ii = np.zeros((cov.shape[0] + 1, cov.shape[1] + 1), dtype=np.int64)
    ii[1:, 1:] = np.cumsum(np.cumsum(cov.astype(np.int64), 0), 1)
    return ii


def free_positions(ii, n, w, h, pad):
    """Top-left corners (row, col) of every w x h window whose pad-expanded box is fully free."""
    w2, h2 = w + 2 * pad, h + 2 * pad
    if w2 > n or h2 > n:
        return np.empty((0, 2), dtype=np.int64)
    s = ii[h2:, w2:] - ii[:-h2, w2:] - ii[h2:, :-w2] + ii[:-h2, :-w2]
    return np.argwhere(s == 0)


# ---------------------------------------------------------------- back plate

def find_back_plate(me):
    """The flat back plate, geometrically: -Y facing polys with every vertex on the min-Y plane."""
    ymin = float(min(v.co.y for v in me.vertices))
    polys = [i for i, p in enumerate(me.polygons)
             if p.normal.y < -0.9
             and all(abs(me.vertices[vi].co.y - ymin) < 1e-6 for vi in p.vertices)]
    if not polys:
        raise SystemExit("no back plate found: no -Y facing polygon lies wholly on the min-Y plane")
    return polys, ymin


def uv_island_of(me, seed_polys):
    """The complete UV island(s) reachable from seed_polys.

    Two polygons are in the same island when they share a mesh vertex AND their UVs agree there.
    """
    uv = me.uv_layers[0].data
    parent = list(range(len(me.polygons)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    buckets = defaultdict(list)
    for pi, poly in enumerate(me.polygons):
        for li in poly.loop_indices:
            key = (me.loops[li].vertex_index,
                   round(uv[li].uv[0], 6), round(uv[li].uv[1], 6))
            buckets[key].append(pi)
    for ps in buckets.values():
        r0 = find(ps[0])
        for q in ps[1:]:
            rq = find(q)
            if rq != r0:
                parent[rq] = r0
                r0 = find(r0)

    roots = {find(p) for p in seed_polys}
    return sorted(p for p in range(len(me.polygons)) if find(p) in roots), len(
        {find(p) for p in range(len(me.polygons))})


# ---------------------------------------------------------------- main

def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    if len(argv) < 2:
        raise SystemExit("usage: ... gen_backuv.py -- <fbx> <style> [--out <path>] [--dry-run]")
    src, style = argv[0], argv[1].lower()
    dry = "--dry-run" in argv
    dst = src
    if "--out" in argv:
        dst = argv[argv.index("--out") + 1]

    if style not in STYLE_FILES:
        raise SystemExit(f"unknown style {style!r}; expected one of {sorted(STYLE_FILES)}")
    if os.path.basename(src) != STYLE_FILES[style]:
        print(f"NOTE  {os.path.basename(src)} is not the usual file for style {style} "
              f"({STYLE_FILES[style]}) -- continuing anyway")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(meshes) != 1:
        raise SystemExit(f"expected exactly 1 mesh object, found {len(meshes)}")
    ob = meshes[0]
    me = ob.data
    uv = me.uv_layers[0].data

    print(f"BACKUV {style}  {os.path.basename(src)}  mesh {ob.name}  "
          f"v={len(me.vertices)} l={len(me.loops)} p={len(me.polygons)}")

    # --- the back plate, and the interlock ---------------------------------------------------
    plate, ymin = find_back_plate(me)
    island, n_islands = uv_island_of(me, plate)
    if island != plate:
        raise SystemExit(
            f"ABORT: the back plate ({len(plate)} polys) is not exactly one whole UV island "
            f"(the island(s) it belongs to hold {len(island)} polys). Moving it would shear a "
            f"shared island; refusing.")
    plate_set = set(plate)
    n_loops = sum(me.polygons[i].loop_total for i in plate)
    n_tris = sum(me.polygons[i].loop_total - 2 for i in plate)
    area_m2 = float(sum(me.polygons[i].area for i in plate))
    px = [me.vertices[vi].co.x for i in plate for vi in me.polygons[i].vertices]
    pz = [me.vertices[vi].co.z for i in plate for vi in me.polygons[i].vertices]
    x0, x1, z0, z1 = min(px), max(px), min(pz), max(pz)
    long_mm, short_mm = (x1 - x0) * 1000.0, (z1 - z0) * 1000.0
    print(f"  back plate: {len(plate)} polys / {n_tris} tris / {n_loops} loops, one whole UV island "
          f"of {n_islands}, y = {ymin:.5f}")
    print(f"  plate bbox {long_mm:.2f} x {short_mm:.2f} mm (aspect {long_mm / short_mm:.4f}), "
          f"surface {area_m2 * 1e4:.2f} cm2")

    def isl_uv_area(polys):
        tot = 0.0
        for i in polys:
            pts = [tuple(uv[li].uv) for li in me.polygons[i].loop_indices]
            a = 0.0
            for k in range(len(pts)):
                ax, ay = pts[k]
                bx, by = pts[(k + 1) % len(pts)]
                a += ax * by - bx * ay
            tot += abs(a) * 0.5
        return tot

    before_tex = isl_uv_area(plate) * N_ATLAS * N_ATLAS
    print(f"  BEFORE: {before_tex:.0f} texels, {100.0 * before_tex / (N_ATLAS ** 2):.3f} % of atlas, "
          f"density {(before_tex / (area_m2 * 1e6)) ** 0.5:.3f} tex/mm")

    # --- free space, computed from everything EXCEPT the plate ------------------------------
    tris = []
    for pi, poly in enumerate(me.polygons):
        if pi in plate_set:
            continue
        pts = [tuple(uv[li].uv) for li in poly.loop_indices]
        for k in range(1, len(pts) - 1):
            tris.append([pts[0], pts[k], pts[k + 1]])
    cov = rasterise(np.asarray(tris, dtype=np.float64), N_ATLAS)
    ii = summed_area(cov)
    print(f"  atlas occupied by the other {n_islands - 1} islands: {cov.sum()} texels "
          f"({100.0 * cov.sum() / (N_ATLAS ** 2):.2f} %)")

    # --- biggest island of the plate's aspect that fits, either way up ----------------------
    def size_for(long_tx, portrait):
        short_tx = max(1, int(round(long_tx * short_mm / long_mm)))
        return (short_tx, long_tx) if portrait else (long_tx, short_tx)   # (w, h)

    best = None
    for portrait in (False, True):
        lo, hi = 0, N_ATLAS - 2 * GUTTER          # lo always fits (0), hi maybe not
        while lo < hi:
            mid = (lo + hi + 1) // 2
            w, h = size_for(mid, portrait)
            if len(free_positions(ii, N_ATLAS, w, h, GUTTER)):
                lo = mid
            else:
                hi = mid - 1
        if lo <= 0:
            print(f"  {'portrait' if portrait else 'landscape'}: nothing fits")
            continue
        w, h = size_for(lo, portrait)
        dens = lo / long_mm
        print(f"  {'portrait' if portrait else 'landscape'}: biggest fit {w} x {h} tx "
              f"-> {dens:.3f} tex/mm")
        if best is None or dens > best[0]:
            best = (dens, w, h, portrait)
    if best is None:
        raise SystemExit("ABORT: no free rectangle can hold the back plate at any size")
    dens, w, h, portrait = best

    # --- centre it in its hole: push the clearance up until few positions remain ------------
    pad = GUTTER
    pos = free_positions(ii, N_ATLAS, w, h, pad)
    while pad < MAX_CLEARANCE:
        nxt = free_positions(ii, N_ATLAS, w, h, pad + 1)
        if len(nxt) == 0:
            break
        pad, pos = pad + 1, nxt
    # Among the survivors take the median row, then the median column within it. That parks the
    # island in the middle of its hole rather than against one wall, and picking medians (not a
    # mean) keeps the choice on an actually-valid position and keeps it deterministic.
    uy = np.unique(pos[:, 0])
    ry = int(uy[len(uy) // 2])
    ux = np.sort(pos[pos[:, 0] == ry][:, 1])
    rx = int(ux[len(ux) // 2])
    ry0, rx0 = ry + pad, rx + pad
    print(f"  placed {w} x {h} tx at row y[{ry0}:{ry0 + h}] col x[{rx0}:{rx0 + w}]  "
          f"({'portrait' if portrait else 'landscape'}, clearance {pad} texels, "
          f"median of {len(pos)} position(s) tied at that clearance)")

    # --- write the UVs ----------------------------------------------------------------------
    # Rows run downward from v = 1, so row r <-> v = 1 - r/N.
    #   landscape: u from object +X, v from object +Z  (the shipped island's sense)
    #   portrait : u from object +Z, v from object -X  (a +90 deg rotation, determinant +1,
    #              so art painted into the rect is not mirrored when seen from behind the board)
    changed = 0
    for pi in plate:
        for li in me.polygons[pi].loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            fx = (v.x - x0) / (x1 - x0)
            fz = (v.z - z0) / (z1 - z0)
            if portrait:
                col = rx0 + fz * w
                row = ry0 + fx * h
            else:
                col = rx0 + fx * w
                row = ry0 + (1.0 - fz) * h
            nu, nv = col / N_ATLAS, 1.0 - row / N_ATLAS
            # UVs are stored as float32; compare at storage precision so a second run of this
            # script honestly reports "nothing moved" instead of float64-vs-float32 noise.
            if tuple(uv[li].uv) != (float(np.float32(nu)), float(np.float32(nv))):
                changed += 1
            uv[li].uv = (nu, nv)
    me.update()
    print(f"  rewrote {changed} of {n_loops} back-plate UV loops "
          f"({'no change -- already packed' if changed == 0 else 'moved'})")

    after_tex = isl_uv_area(plate) * N_ATLAS * N_ATLAS
    print(f"  AFTER : {after_tex:.0f} texels, {100.0 * after_tex / (N_ATLAS ** 2):.3f} % of atlas, "
          f"density {(after_tex / (area_m2 * 1e6)) ** 0.5:.3f} tex/mm "
          f"({(after_tex / before_tex) ** 0.5:.2f}x linear)")
    aniso = (h / long_mm) / (w / short_mm) if portrait else (w / long_mm) / (h / short_mm)
    print(f"  anisotropy {abs(aniso - 1.0) * 100.0:.3f} % (integer texel rect vs exact plate aspect)")

    corners = {
        "(-X,-Z)": (0.0, 0.0), "(+X,-Z)": (1.0, 0.0),
        "(-X,+Z)": (0.0, 1.0), "(+X,+Z)": (1.0, 1.0),
    }
    for name, (fx, fz) in corners.items():
        col = rx0 + (fz if portrait else fx) * w
        row = ry0 + (fx if portrait else (1.0 - fz)) * h
        print(f"  corner {name} -> texel (x={col:.0f}, y={row:.0f})")

    if dry:
        print("  --dry-run: nothing written")
        return

    for o in bpy.data.objects:
        o.select_set(True)
    fd, tmp = tempfile.mkstemp(suffix=".fbx", dir=os.path.dirname(os.path.abspath(dst)))
    os.close(fd)
    # apply_scale_options='FBX_SCALE_UNITS' IS LOAD-BEARING AND WAS GOT WRONG ONCE.
    #
    # The shipped board FBXes carry UnitScaleFactor = 100 (centimetres). Exporting with
    # apply_unit_scale=True (or with the exporter's FBX_SCALE_NONE default) bakes the unit
    # into the coordinates and writes UnitScaleFactor = 1.0 instead. Blender reads both
    # files back identically -- it normalises the unit on import -- so a Blender-level
    # bit-identity check CANNOT SEE THE DIFFERENCE and passes every field.
    #
    # Unity can see it. With the unchanged .meta (useFileScale), the first attempt at this
    # repack re-imported at a different scale and BoardBuilder rewrote every anchor
    # override in all three prefabs by a factor of 100 -- 0.23360015 became 0.0023359999 --
    # plus a quaternion sign flip from the changed transform decomposition. That is what a
    # UV-only edit must never do, and the proof harness of the day reported ALL PASS.
    #
    # FBX_SCALE_UNITS puts the scene's unit scale into the FBX unit scale where it started.
    # Verified against the shipped files: UnitScaleFactor stays 100, and the null round
    # trip is bit-identical on vertices, loops, polygons, UVs, split normals AND anchor
    # LOCAL transforms -- check 3b, which apply_unit_scale=True failed with a 1.39e-17 m
    # residue on two anchors of oak and bronze. That residue was this setting, not the edit.
    bpy.ops.export_scene.fbx(filepath=tmp, use_selection=True, apply_unit_scale=False,
                             apply_scale_options='FBX_SCALE_UNITS', global_scale=1.0,
                             add_leaf_bones=False, bake_space_transform=False,
                             mesh_smooth_type='OFF', use_mesh_modifiers=False, path_mode='COPY')
    shutil.move(tmp, dst)
    print(f"  wrote {dst}")


if __name__ == "__main__":
    main()
