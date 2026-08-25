"""gen_uvdiff.py -- prove that two board FBXes differ in the BACK FACE UVs and in nothing else.

    /home/claw/blender-4.2/blender -b --factory-startup \
        --python unity/board-prep/gen_uvdiff.py -- <reference.fbx> <candidate.fbx>

WHY THIS EXISTS. `gen_backuv.py` rewrites the UV coordinates of one island inside a shipped FBX by
importing it into Blender and exporting it again. Every other field in that file -- 5-6k vertex
positions, 20-24k loop vertex indices, 5-6k polygon sizes, the per-loop split normals Unity is told
to IMPORT rather than recalculate (`normalImportMode: 0` in the .fbx.meta), and the ten anchor
EMPTY transforms the whole VR interaction layer is measured from -- travels through that round trip
as collateral. "The exporter is lossless" is a claim, and a claim about a shipped asset that the
hand-tuned anchor config is measured FROM has to be a MEASUREMENT.

So this file measures it. It loads both FBXes through the identical import path and compares, field
by field, as raw bytes:

  1. vertex positions               (float32, N x 3)
  2. loop vertex indices + polygon loop_total
  3. every EMPTY's location / rotation_euler / scale / matrix_world
  4. per-loop split normals
  5. UV loops: how many changed, and whether the changed set is EXACTLY the back face's loop set

and then, on the candidate alone:

  6. island overlap -- every UV island rasterised into a 2048^2 ownership map; a texel claimed by
     two different islands is a bleed between two surfaces and is reported as a failure. The
     rasteriser is the project's own (`tex_seams.rasterise_uv`): texel-centre point sampling with
     the row axis flipped exactly once, so the count here is comparable to the atlas numbers the
     texture lane already reports.
  7. the back island's texel density and atlas coverage, plus its minimum separation in texels from
     every other island (the gutter).

THE BACK FACE is identified GEOMETRICALLY, never from the UVs -- the UVs are the thing under test.
It is the set of polygons whose normal points along -Y (n.y < -0.9) and all of whose vertices sit
on the mesh's minimum-Y plane: the flat back plate. On all three shipped boards that set is also
exactly one complete UV island, which is what makes it safe to move (no island boundary is created
or destroyed), and `gen_backuv.py` asserts that before it writes anything.

Exit code is 0 only when checks 1-6 and 8 all pass.
"""

import os
import struct
import sys

import bpy
import numpy as np

N_ATLAS = 2048
GUTTER_MIN = 8


# ---------------------------------------------------------------- loading

def load(path):
    """Import one FBX and pull every field we compare into plain numpy / tuples."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(meshes) != 1:
        raise SystemExit(f"{path}: expected exactly 1 mesh object, found {len(meshes)}")
    ob = meshes[0]
    me = ob.data

    nv, nl, np_ = len(me.vertices), len(me.loops), len(me.polygons)
    co = np.empty(nv * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    lvi = np.empty(nl, dtype=np.int32)
    me.loops.foreach_get("vertex_index", lvi)
    lt = np.empty(np_, dtype=np.int32)
    me.polygons.foreach_get("loop_total", lt)
    ls = np.empty(np_, dtype=np.int32)
    me.polygons.foreach_get("loop_start", ls)

    if not me.uv_layers:
        raise SystemExit(f"{path}: mesh has no UV layer")
    uv = np.empty(nl * 2, dtype=np.float32)
    me.uv_layers[0].data.foreach_get("uv", uv)

    nrm = np.empty(nl * 3, dtype=np.float32)
    try:
        me.corner_normals.foreach_get("vector", nrm)
    except (AttributeError, RuntimeError):
        nrm = None

    empties = {}
    for o in bpy.data.objects:
        if o.type != "EMPTY":
            continue
        empties[o.name] = (
            tuple(o.location), tuple(o.rotation_euler), tuple(o.scale),
            tuple(tuple(r) for r in o.matrix_world),
            o.parent.name if o.parent else None,
        )

    # Geometric back face: -Y-facing polys in the back half of the board.
    #
    # This used to be "-Y facing AND every vertex on the min-Y plane", which described the
    # back exactly while the back was one flat n-gon.  ModBuild 278 gave it relief, so the
    # min-Y plane is now whatever stands proudest -- the nail caps on oak -- and that test
    # returned 40 of 635 polys and reported the back plate as 480 x 263 mm of a 636 x 316 mm
    # face.  A half-measured back face makes checks 5 and 7 measure something that is not
    # the back face, which is worse than not measuring it.  The normal threshold is 0.5 and
    # not 0.9 because the relief's own chamfer walls run to MAX_BACK_SLOPE_DEG = 40 deg
    # (cos 40 = 0.766); the mid-plane test is what keeps the rim's back chamfer out, whose
    # -Y component reaches -0.457 on oak.
    ymin = float(min(v.co.y for v in me.vertices))
    ymax = float(max(v.co.y for v in me.vertices))
    ymid = 0.5 * (ymin + ymax)
    back_polys = [i for i, p in enumerate(me.polygons)
                  if p.normal.y < -0.5
                  and all(me.vertices[vi].co.y < ymid for vi in p.vertices)]
    back_loops = np.zeros(nl, dtype=bool)
    for i in back_polys:
        back_loops[ls[i]:ls[i] + lt[i]] = True

    plate_area = float(sum(me.polygons[i].area for i in back_polys))          # m^2
    bx = [me.vertices[vi].co.x for i in back_polys for vi in me.polygons[i].vertices]
    bz = [me.vertices[vi].co.z for i in back_polys for vi in me.polygons[i].vertices]

    return dict(path=path, name=ob.name, nv=nv, nl=nl, npoly=np_, co=co, lvi=lvi, lt=lt, ls=ls,
                uv=uv, nrm=nrm, empties=empties, back_polys=back_polys, back_loops=back_loops,
                plate_area=plate_area, plate_bbox=(min(bx), max(bx), min(bz), max(bz)),
                uv_layer_name=me.uv_layers[0].name, mats=[s.name for s in ob.material_slots])


# ---------------------------------------------------------------- rasteriser
# Same texel-centre point sampling and single row flip as tex_seams.rasterise_uv, so the texel
# counts printed here are comparable to the atlas numbers the texture lane already reports.

def rasterise(tris, n, out, value):
    px = tris[..., 0] * n
    py = (1.0 - tris[..., 1]) * n
    hits = 0
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
        ins = (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
        if not ins.any():
            continue
        sub = out[ya:yb, xa:xb]
        sub[ins] = value
        out[ya:yb, xa:xb] = sub
        hits += int(ins.sum())
    return hits


def uv_islands(d):
    """Union-find over polygons: connected when they share a mesh vertex AND the UV there agrees."""
    from collections import defaultdict
    parent = list(range(d["npoly"]))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    buckets = defaultdict(list)
    for pi in range(d["npoly"]):
        s, t = int(d["ls"][pi]), int(d["lt"][pi])
        for li in range(s, s + t):
            key = (int(d["lvi"][li]), round(float(d["uv"][2 * li]), 6), round(float(d["uv"][2 * li + 1]), 6))
            buckets[key].append(pi)
    for ps in buckets.values():
        r0 = find(ps[0])
        for q in ps[1:]:
            rq = find(q)
            if rq != r0:
                parent[rq] = r0
                r0 = find(r0)
    groups = defaultdict(list)
    for pi in range(d["npoly"]):
        groups[find(pi)].append(pi)
    return list(groups.values())


def poly_tris(d, pi):
    s, t = int(d["ls"][pi]), int(d["lt"][pi])
    pts = [(float(d["uv"][2 * li]), float(d["uv"][2 * li + 1])) for li in range(s, s + t)]
    return [[pts[0], pts[i], pts[i + 1]] for i in range(1, len(pts) - 1)]


def uv_area(d, pi):
    s, t = int(d["ls"][pi]), int(d["lt"][pi])
    pts = [(float(d["uv"][2 * li]), float(d["uv"][2 * li + 1])) for li in range(s, s + t)]
    a = 0.0
    for i in range(len(pts)):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % len(pts)]
        a += x1 * y2 - x2 * y1
    return abs(a) * 0.5


# ---------------------------------------------------------------- checks

def eqbytes(a, b):
    if a is None or b is None:
        return a is None and b is None
    return a.shape == b.shape and a.dtype == b.dtype and a.tobytes() == b.tobytes()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    if len(argv) < 2:
        raise SystemExit("usage: ... gen_uvdiff.py -- <reference.fbx> <candidate.fbx>")
    ref, cand = argv[0], argv[1]

    A = load(ref)
    B = load(cand)
    ok = {}

    print(f"REF  {os.path.basename(ref)}   mesh {A['name']}  v={A['nv']} l={A['nl']} p={A['npoly']}")
    print(f"CAND {os.path.basename(cand)}  mesh {B['name']}  v={B['nv']} l={B['nl']} p={B['npoly']}")

    # ---- 1 vertex positions
    ok[1] = A["nv"] == B["nv"] and eqbytes(A["co"], B["co"])
    print(f"[{'PASS' if ok[1] else 'FAIL'}] 1  vertex positions bit-identical "
          f"({A['nv']} verts, {A['co'].nbytes} bytes)")
    if not ok[1] and A["nv"] == B["nv"]:
        bad = np.flatnonzero(A["co"] != B["co"])
        print(f"        {bad.size} differing floats, worst |d| = {np.abs(A['co'] - B['co']).max():.3e}")

    # ---- 2 topology
    t_ok = (A["nl"] == B["nl"] and A["npoly"] == B["npoly"]
            and eqbytes(A["lvi"], B["lvi"]) and eqbytes(A["lt"], B["lt"]))
    ok[2] = t_ok
    print(f"[{'PASS' if ok[2] else 'FAIL'}] 2  loop vertex indices + polygon sizes bit-identical "
          f"({A['nl']} loops, {A['npoly']} polys)")

    # ---- 3 anchor empties.  matrix_world is the load-bearing one -- it is the pose the VR
    # interaction layer actually reads.  The local TRS is reported separately because Blender's
    # FBX round trip normalises sub-float32 residue in it (see the note below check 3a).
    names_ok = sorted(A["empties"]) == sorted(B["empties"])
    mw_bad, trs_bad, worst = [], [], 0.0
    if names_ok:
        for n in A["empties"]:
            a, b = A["empties"][n], B["empties"][n]
            if a[3] != b[3] or a[4] != b[4]:
                mw_bad.append(n)
            if a[0:3] != b[0:3]:
                trs_bad.append(n)
                for u, v in zip(a[0] + a[1] + a[2], b[0] + b[1] + b[2]):
                    worst = max(worst, abs(u - v))
    ok[3] = names_ok and not mw_bad and not trs_bad
    print(f"[{'PASS' if names_ok and not mw_bad else 'FAIL'}] 3a anchor EMPTY matrix_world + parenting "
          f"bit-identical ({len(A['empties'])} empties: {', '.join(sorted(A['empties'])) or '-'})")
    for n in mw_bad:
        print(f"        {n}: {A['empties'][n][3]} -> {B['empties'][n][3]}")
    print(f"[{'PASS' if names_ok and not trs_bad else 'FAIL'}] 3b anchor EMPTY local location/rotation/scale "
          f"bit-identical ({len(trs_bad)} of {len(A['empties'])} differ, worst |d| = {worst:.3e} m)")
    for n in trs_bad:
        print(f"        {n}: loc {A['empties'][n][0]} -> {B['empties'][n][0]}")

    # ---- 3c per-loop split normals (Unity imports these; it does not recalculate them --
    # normalImportMode: 0 in the .fbx.meta)
    n_ok = eqbytes(A["nrm"], B["nrm"])
    print(f"[{'PASS' if n_ok else 'FAIL'}] 3c per-loop split normals bit-identical")
    if not n_ok and A["nrm"] is not None and B["nrm"] is not None and A["nrm"].shape == B["nrm"].shape:
        bad = np.flatnonzero(A["nrm"] != B["nrm"])
        print(f"        {bad.size} differing floats, worst |d| = {np.abs(A['nrm'] - B['nrm']).max():.3e}")
    ok["3c"] = n_ok

    # ---- 4 / 5 UVs
    if A["nl"] != B["nl"]:
        ok[4] = ok[5] = False
        print("[FAIL] 4  loop count differs; UV comparison impossible")
    else:
        pair = (A["uv"] != B["uv"]).reshape(-1, 2).any(axis=1)
        backA = A["back_loops"]
        backB = B["back_loops"]
        same_back_set = eqbytes(backA, backB)
        outside = int(np.count_nonzero(pair & ~backA))
        changed = int(np.count_nonzero(pair))
        nback = int(np.count_nonzero(backA))
        ok[4] = same_back_set and outside == 0
        ok[5] = same_back_set and changed == nback
        print(f"[{'PASS' if ok[4] else 'FAIL'}] 4  UV loops OUTSIDE the back face bit-identical "
              f"({A['nl'] - nback} loops, {outside} changed)")
        print(f"[{'PASS' if ok[5] else 'FAIL'}] 5  changed UV loops = back face loop count "
              f"(changed {changed}, back face {nback}, back polys {len(A['back_polys'])})")
        if not same_back_set:
            print("        the geometric back face is NOT the same loop set in both files")

    # ---- 6 island overlap in the candidate
    isl = uv_islands(B)
    own = np.full((N_ATLAS, N_ATLAS), -1, dtype=np.int32)
    overlap = np.zeros((N_ATLAS, N_ATLAS), dtype=bool)
    backset = set(B["back_polys"])
    back_idx = -1
    for k, ps in enumerate(isl):
        if backset and backset <= set(ps):
            back_idx = k
        tris = np.asarray([t for pi in ps for t in poly_tris(B, pi)], dtype=np.float64)
        mine = np.zeros((N_ATLAS, N_ATLAS), dtype=bool)
        rasterise(tris, N_ATLAS, mine, True)
        clash = mine & (own >= 0) & (own != k)
        overlap |= clash
        own[mine] = k
    n_over = int(overlap.sum())
    ok[6] = n_over == 0
    print(f"[{'PASS' if ok[6] else 'FAIL'}] 6  island overlap: {n_over} overlapped texels "
          f"across {len(isl)} islands at {N_ATLAS}^2")

    # ---- gutter: how close does the back island come to any other island?
    if back_idx >= 0:
        back_mask = own == back_idx
        others = (own >= 0) & ~back_mask
        sep = None
        for r in range(0, GUTTER_MIN + 4):
            grown = grow(back_mask, r)
            if (grown & others).any():
                sep = r
                break
        gut = f"{sep} texel(s)" if sep is not None else f">= {GUTTER_MIN + 4} texels"
        gok = sep is None or sep >= GUTTER_MIN
        print(f"[{'PASS' if gok else 'FAIL'}] 6b back island gutter to the nearest other island: {gut} "
              f"(required >= {GUTTER_MIN})")
        ok["6b"] = gok

        # ---- 7 density + coverage
        tex = float(sum(uv_area(B, pi) for pi in B["back_polys"])) * N_ATLAS * N_ATLAS
        area_mm2 = B["plate_area"] * 1e6
        ys, xs = np.nonzero(back_mask)
        print(f"       7  BACK island: {back_mask.sum()} texels rasterised, {tex:.0f} texels analytic, "
              f"surface {area_mm2:.0f} mm^2")
        print(f"       7  density {(tex / area_mm2) ** 0.5:.3f} tex/mm   "
              f"atlas coverage {100.0 * tex / (N_ATLAS * N_ATLAS):.3f} %")
        print(f"       7  rect (texels, row 0 = v=1) y[{ys.min()}:{ys.max() + 1}] x[{xs.min()}:{xs.max() + 1}]  "
              f"= {xs.max() + 1 - xs.min()} x {ys.max() + 1 - ys.min()}")
        x0, x1, z0, z1 = B["plate_bbox"]
        print(f"       7  plate {(x1 - x0) * 1000:.2f} x {(z1 - z0) * 1000:.2f} mm")

    # ---- 8 FBX UnitScaleFactor -- THE CHECK EVERY OTHER CHECK HERE IS BLIND TO
    #
    # Checks 1-7 all read the mesh THROUGH BLENDER, and Blender normalises the file's unit
    # scale on import. So two FBXes that differ only in UnitScaleFactor read back
    # bit-identical in every field above, and this comparator reported ALL PASS on a pair
    # that Unity imported at a factor of 100 apart: BoardBuilder rewrote every anchor
    # override in all three prefabs (-0.23360015 -> -0.0023359999) plus a quaternion sign
    # flip, from what was supposed to be a UV-only edit. The shipped boards carry
    # UnitScaleFactor 100; exporting with apply_unit_scale=True writes 1.0. Export with
    # apply_unit_scale=False and apply_scale_options='FBX_SCALE_UNITS'.
    #
    # This one is read from the FILE BYTES on purpose. A check that goes through the same
    # importer as the thing it is checking cannot see what that importer normalises.
    def unit_scale(path):
        blob = open(path, "rb").read()
        i = blob.find(b"UnitScaleFactor")
        if i < 0:
            return None
        seg = blob[i:i + 80]
        vals = [struct.unpack("<d", seg[j:j + 8])[0] for j in range(len(seg) - 8)]
        cand = [v for v in vals if 0.0001 < abs(v) < 100000 and abs(v - round(v)) < 1e-9]
        return cand[0] if cand else None

    us_ref, us_cand = unit_scale(ref), unit_scale(cand)
    uok = (us_ref is not None and us_ref == us_cand)
    print(f"[{'PASS' if uok else 'FAIL'}] 8  FBX UnitScaleFactor preserved: "
          f"reference {us_ref} -> candidate {us_cand}"
          + ("" if uok else "   <- Unity will re-import at a DIFFERENT SCALE; "
                            "export with apply_scale_options='FBX_SCALE_UNITS'"))
    ok[8] = uok

    hard = [k for k in (1, 2, 3, "3c", 4, 5, 6, "6b", 8) if k in ok and not ok[k]]
    print(f"RESULT {os.path.basename(cand)}: {'ALL PASS' if not hard else 'FAILED ' + str(hard)}")
    if hard:
        sys.exit(1)


def grow(mask, r):
    """Chebyshev dilation by r via a box prefix sum -- no scipy in Blender's python."""
    if r <= 0:
        return mask.copy()
    n = mask.shape[0]
    ii = np.zeros((n + 1, n + 1), dtype=np.int64)
    ii[1:, 1:] = np.cumsum(np.cumsum(mask.astype(np.int64), 0), 1)
    a = np.clip(np.arange(n) - r, 0, n)
    b = np.clip(np.arange(n) + r + 1, 0, n)
    s = (ii[np.ix_(b, b)] - ii[np.ix_(a, b)] - ii[np.ix_(b, a)] + ii[np.ix_(a, a)])
    return s > 0


if __name__ == "__main__":
    main()
