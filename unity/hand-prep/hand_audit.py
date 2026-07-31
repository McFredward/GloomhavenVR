# hand_audit.py — Blender headless: measure a rigged hand FBX. Read-only.
#
# Reports, on the mesh as it sits on disk:
#   - triangle/vert/face counts, world bbox in mm, edge-length distribution
#   - topology defects: boundary edges (hairline cracks), non-manifold edges, loose verts,
#     zero-area faces, duplicate ("doubled") vertices within a tolerance
#   - the palm patch (same selection rule as relax_palm.py / palm_reunwrap.py) and its
#     FLATNESS: residual of the vertices against a fitted degree-4 height field, which is the
#     fold's signature, plus a plain plane-fit residual
#   - UV texel density over the whole hand and over the palm patch, island count
#
# RUN:
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/hand_audit.py -- \
#       <in.fbx> [--px 2048] [--json out.json]
import bpy, bmesh, sys, json, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC = pos[0]
PX = int(argv[argv.index("--px") + 1]) if "--px" in argv else 2048
JSON_OUT = argv[argv.index("--json") + 1] if "--json" in argv else None
NY_MIN, ZLOW = 0.15, -78.0

out = {"src": SRC}


def log(*a):
    print("[audit]", *a)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
me = ob.data
MW = ob.matrix_world
n3 = MW.to_3x3()

me.calc_loop_triangles()
out["verts"] = len(me.vertices)
out["faces"] = len(me.polygons)
out["tris"] = len(me.loop_triangles)
out["bones"] = len(arm.data.bones)

co = np.array([list(MW @ v.co) for v in me.vertices]) * 1000.0
out["bbox_mm"] = (co.max(0) - co.min(0)).round(2).tolist()

bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
bm.edges.ensure_lookup_table()
bm.faces.ensure_lookup_table()

el = np.array([e.calc_length() for e in bm.edges]) * 1000.0
out["edge_mm"] = {"mean": round(float(el.mean()), 2), "p50": round(float(np.median(el)), 2),
                  "p99": round(float(np.percentile(el, 99)), 2), "max": round(float(el.max()), 2)}

bnd = [e for e in bm.edges if len(e.link_faces) == 1]
nonman = [e for e in bm.edges if len(e.link_faces) > 2]
wire = [e for e in bm.edges if len(e.link_faces) == 0]
loose = [v for v in bm.verts if not v.link_edges]
zero = [f for f in bm.faces if f.calc_area() < 1e-12]
out["boundary_edges"] = len(bnd)
out["nonmanifold_edges"] = len(nonman)
out["wire_edges"] = len(wire)
out["loose_verts"] = len(loose)
out["zero_area_faces"] = len(zero)

# boundary loops: connected components over boundary edges
seen = set()
comps = []
badj = {}
for e in bnd:
    for v in e.verts:
        badj.setdefault(v.index, []).append(e)
for e in bnd:
    if e.index in seen:
        continue
    stack, comp = [e], []
    seen.add(e.index)
    while stack:
        cur = stack.pop()
        comp.append(cur)
        for v in cur.verts:
            for o in badj[v.index]:
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
    comps.append(comp)
out["boundary_components"] = len(comps)
out["boundary_comp_sizes"] = sorted((len(c) for c in comps), reverse=True)[:10]
out["boundary_singletons"] = sum(1 for c in comps if len(c) == 1)
# how big is the biggest hole, in mm of perimeter
out["boundary_perim_mm_max"] = round(
    max((sum(e.calc_length() for e in c) for c in comps), default=0.0) * 1000.0, 2)

# duplicate verts within 0.05 mm
pts = np.array([list(v.co) for v in bm.verts])
key = np.round(pts / 0.00005).astype(np.int64)
uniq, inv, cnt = np.unique(key, axis=0, return_inverse=True, return_counts=True)
out["coincident_vert_groups"] = int((cnt > 1).sum())
out["coincident_verts"] = int(cnt[cnt > 1].sum())

# ------------------------------------------------------------------ palm patch + flatness
FINGER = [g.index for g in ob.vertex_groups
          if any(k in g.name for k in ("Index", "Middle", "Ring", "Pinky", "Thumb"))]
fw = np.zeros(len(me.vertices))
for i, v in enumerate(me.vertices):
    fw[i] = sum(g.weight for g in v.groups if g.group in FINGER)

sel = np.zeros(len(bm.faces), bool)
for f in bm.faces:
    ny = -(n3 @ f.normal).normalized().y
    cz = (MW @ f.calc_center_median()).z * 1000.0
    ffw = max(fw[v.index] for v in f.verts)
    sel[f.index] = ny > NY_MIN and cz > ZLOW and ffw < 0.5
out["palm_faces"] = int(sel.sum())

mean_n = Vector((0, 0, 0))
for f in bm.faces:
    if sel[f.index]:
        mean_n += f.normal * f.calc_area()
mean_n.normalize()
inside = np.zeros(len(bm.verts), bool)
for f in bm.faces:
    if sel[f.index]:
        for v in f.verts:
            inside[v.index] = True
align = np.array([max(0.0, v.normal.dot(mean_n)) for v in bm.verts])
plate = inside & (align > 0.6)
idx = np.nonzero(plate)[0]
out["plate_verts"] = int(len(idx))

if len(idx) > 50:
    P = np.array([list(bm.verts[i].co) for i in idx])
    mean = P.mean(0)
    U, S, Vt = np.linalg.svd(P - mean, full_matrices=False)
    u_ax, v_ax, n_ax = Vt
    uu, vv, nn = (P - mean) @ u_ax, (P - mean) @ v_ax, (P - mean) @ n_ax
    out["plate_plane_resid_mm"] = {
        "rms": round(float(nn.std() * 1000), 3),
        "p99": round(float(np.percentile(np.abs(nn), 99) * 1000), 3),
        "max": round(float(np.abs(nn).max() * 1000), 3)}
    for DEG in (2, 4):
        terms = [(i, j) for i in range(DEG + 1) for j in range(DEG + 1 - i)]
        A = np.column_stack([uu ** i * vv ** j for (i, j) in terms])
        coef, *_ = np.linalg.lstsq(A, nn, rcond=None)
        r = nn - A @ coef
        out[f"plate_poly{DEG}_resid_mm"] = {
            "rms": round(float(r.std() * 1000), 3),
            "p99": round(float(np.percentile(np.abs(r), 99) * 1000), 3),
            "max": round(float(np.abs(r).max() * 1000), 3)}

    # dihedral roughness inside the plate: angle between adjacent face normals
    ang = []
    for e in bm.edges:
        if len(e.link_faces) == 2 and all(sel[f.index] for f in e.link_faces):
            a, b = e.link_faces
            d = max(-1.0, min(1.0, a.normal.dot(b.normal)))
            ang.append(math.degrees(math.acos(d)))
    if ang:
        ang = np.array(ang)
        out["plate_dihedral_deg"] = {"mean": round(float(ang.mean()), 2),
                                     "p95": round(float(np.percentile(ang, 95)), 2),
                                     "p99": round(float(np.percentile(ang, 99)), 2),
                                     "max": round(float(ang.max()), 2)}

# ------------------------------------------------------------------ texel density
uvl = bm.loops.layers.uv.active
if uvl:
    dens_all, dens_palm = [], []
    for f in bm.faces:
        a3 = f.calc_area()
        if a3 <= 0:
            continue
        ls = f.loops
        uv = [Vector(l[uvl].uv) for l in ls]
        a2 = 0.0
        for i in range(1, len(uv) - 1):
            e1, e2 = uv[i] - uv[0], uv[i + 1] - uv[0]
            a2 += abs(e1.x * e2.y - e1.y * e2.x) * 0.5
        d = math.sqrt(max(a2, 1e-20)) * PX / (math.sqrt(a3) * 1000.0)  # texels per mm
        dens_all.append(d)
        if sel[f.index]:
            dens_palm.append(d)
    da, dp = np.array(dens_all), np.array(dens_palm)
    out["texel_per_mm_all"] = {"p50": round(float(np.median(da)), 3),
                              "p10": round(float(np.percentile(da, 10)), 3)}
    if len(dp):
        out["texel_per_mm_palm"] = {"p50": round(float(np.median(dp)), 3),
                                   "p90": round(float(np.percentile(dp, 90)), 3)}
    # UV area fraction of the palm patch
    def uvarea(fs):
        t = 0.0
        for f in fs:
            uv = [Vector(l[uvl].uv) for l in f.loops]
            for i in range(1, len(uv) - 1):
                e1, e2 = uv[i] - uv[0], uv[i + 1] - uv[0]
                t += abs(e1.x * e2.y - e1.y * e2.x) * 0.5
        return t
    out["palm_uv_area_frac"] = round(uvarea([f for f in bm.faces if sel[f.index]]) /
                                     max(uvarea(list(bm.faces)), 1e-12), 5)
    out["palm_surf_area_frac"] = round(
        sum(f.calc_area() for f in bm.faces if sel[f.index]) /
        max(sum(f.calc_area() for f in bm.faces), 1e-12), 5)

    # UV islands over the palm patch (loops connected iff same uv)
    pf = [f for f in bm.faces if sel[f.index]]
    pfset = set(f.index for f in pf)
    parent = {f.index: f.index for f in pf}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for e in bm.edges:
        lf = [f for f in e.link_faces if f.index in pfset]
        if len(lf) != 2:
            continue
        a, b = lf
        def uvof(f):
            return [tuple(round(c, 6) for c in l[uvl].uv) for l in f.loops if l.vert in e.verts]
        if set(uvof(a)) == set(uvof(b)):
            ra, rb = find(a.index), find(b.index)
            if ra != rb:
                parent[ra] = rb
    out["palm_uv_islands"] = len(set(find(f.index) for f in pf))

bm.free()

log(json.dumps(out, indent=2))
if JSON_OUT:
    with open(JSON_OUT, "w") as fh:
        json.dump(out, fh, indent=2)
    log("wrote", JSON_OUT)
