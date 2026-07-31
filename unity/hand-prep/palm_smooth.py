# palm_smooth.py — Blender headless: take the hard diagonal FOLD and the facet star out of the
# Plate gauntlet's PALM shading, without moving a single vertex.
#
# WHY THIS AND NOT ANOTHER VERTEX FILTER
#   Five earlier attempts all moved vertices (adaptive subdivision, Taubin, Laplacian clamped and
#   soft, a degree-4 height field) and all failed. They were aimed at the wrong data. This FBX
#   ships CUSTOM SPLIT NORMALS — 28 974 per-loop normals, inherited from the original Hunyuan
#   mesh before prepare_hand.py's collapse decimator went through it. They differ from Blender's
#   own auto-smooth normals by 16.3 deg on average and up to 176 deg, and they, not the vertex
#   positions, are what the shader lights. On the crisp back of the hand that is exactly right —
#   they are the plate creases. On the palm they are a fossil of the decimator's big flat fans,
#   and no amount of moving vertices changes them.
#     PROOF: clear the custom normals, render clay, and the palm is smooth and the fold is nearly
#     gone — with every vertex still exactly where it was.
#
# WHAT IT DOES
#   1. Weighs every vertex by how much it belongs to the palm — how far it faces the palm, how
#      far it sits above the cuff rim, how little it is skinned to a finger. All three are
#      SMOOTH ramps, so the patch has no boundary to leave a step at.
#      (A topological patch rim was tried first and is not usable on this mesh: 1080 non-manifold
#      edges mean 619 of the palm's 716 vertices also belong to some face outside the patch, so
#      "rim" swallowed the patch.)
#   2. Rebuilds the normals there: area-weighted vertex normals, then several passes of SPATIAL
#      (not edge-neighbour) smoothing of the normal field itself. Spatial matters — the shell is
#      non-manifold and locally split, and only a radius-based neighbourhood makes two shells
#      that abut on the palm agree on a normal, which is what makes their seam stop showing.
#      Neighbours must face the same way (dot > FACE_GATE) so the palm never averages with the
#      back of the hand through the thickness of the plate.
#   3. Writes the result back as custom split normals, blended with the shipped ones by that
#      weight. Every loop with weight 0 keeps its shipped normal BIT-IDENTICALLY.
#   4. Optionally (--relax) also relaxes the vertex POSITIONS with a biharmonic (thin-plate)
#      solve. Off by default: the shading fix alone does the job, and not moving vertices is the
#      only way to be sure the rig, the silhouette and the skin weights are untouched.
#
# RIG SAFETY (asserted, not assumed; the run aborts rather than ship a broken hand)
#   bone count and names, every bone head/tail within ANCHOR_TOL mm, hand extent within
#   EXTENT_TOL mm, every vertex still carrying a normalised weight set, and vertex/loop counts
#   unchanged so the UVs still line up with the atlas.
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/palm_smooth.py -- \
#       <in.fbx> [out.fbx] [--iters 8] [--radius 7] [--relax 0.0] [--zlow -78]
#
# AFTER RUNNING: rebuild the AssetBundle with the game-exact editor (2021.3.5f1) — the mod loads
# the hands from gloomhavenvr.bundle, not from the FBX.
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palm_region import palm_weight   # noqa: E402  the shared definition of "this is the palm"

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC = pos[0]
DST = pos[1] if len(pos) > 1 else SRC


def getopt(name, default):
    return float(argv[argv.index(name) + 1]) if name in argv else default


RADIUS = getopt("--radius", 11.0)   # mm, spatial smoothing kernel for the normal field
ITERS = int(getopt("--iters", 20))
FACE_GATE = getopt("--gate", 0.3)   # neighbours must face roughly the same way
RELAX = getopt("--relax", 0.0)
ZLOW = getopt("--zlow", -78.0)      # mm, world: the cuff rim — passed to palm_region
ANCHOR_TOL = 0.001                  # mm
EXTENT_TOL = 0.05                   # mm


def log(*a):
    print("[palm_smooth]", *a)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if len(meshes) != 1:
    raise SystemExit(f"expected one mesh, got {len(meshes)}")
ob = meshes[0]
me = ob.data
MW = ob.matrix_world
n3 = MW.to_3x3()
if not me.has_custom_normals:
    raise SystemExit("palm_smooth: this FBX carries no custom split normals — wrong input?")

bones0 = {b.name: (MW @ b.head_local, MW @ b.tail_local) for b in arm.data.bones}
co0 = np.array([list(MW @ v.co) for v in me.vertices])
N0 = np.array([list(l.vector) for l in me.corner_normals])
N0 /= np.maximum(np.linalg.norm(N0, axis=1, keepdims=True), 1e-20)
nloops = len(me.loops)
log(f"loaded {SRC}: {len(me.vertices)} verts, {len(me.polygons)} faces, {nloops} loops, "
    f"{len(bones0)} bones")

FINGER = [g.index for g in ob.vertex_groups
          if any(k in g.name for k in ("Index", "Middle", "Ring", "Pinky", "Thumb"))]
fw = np.zeros(len(me.vertices))
for i, v in enumerate(me.vertices):
    fw[i] = sum(g.weight for g in v.groups if g.group in FINGER)

bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
bm.faces.ensure_lookup_table()
bm.edges.ensure_lookup_table()

# ------------------------------------------------------------------ optional position relax
if RELAX > 0.0:
    sel = np.zeros(len(bm.faces), bool)
    for f in bm.faces:
        ny = -(n3 @ f.normal).normalized().y
        cz = (MW @ f.calc_center_median()).z * 1000.0
        sel[f.index] = ny > 0.15 and cz > ZLOW and max(fw[v.index] for v in f.verts) < 0.5
    inside = np.zeros(len(bm.verts), bool)
    outside = np.zeros(len(bm.verts), bool)
    for f in bm.faces:
        (inside if sel[f.index] else outside)[[v.index for v in f.verts]] = True
    free = inside & ~outside
    fidx = np.nonzero(free)[0]
    if len(fidx) < 20:
        raise SystemExit(f"palm_smooth: only {len(fidx)} free vertices for --relax")
    loc = set(int(i) for i in fidx)
    for _ in range(2):
        for i in list(loc):
            for e in bm.verts[i].link_edges:
                loc.add(e.other_vert(bm.verts[i]).index)
    loc = sorted(loc)
    lp = {g: k for k, g in enumerate(loc)}
    L = np.zeros((len(loc), len(loc)))
    for g in loc:
        nb = [e.other_vert(bm.verts[g]).index for e in bm.verts[g].link_edges]
        nb = [x for x in nb if x in lp]
        if not nb:
            continue
        L[lp[g], lp[g]] = 1.0
        for x in nb:
            L[lp[g], lp[x]] -= 1.0 / len(nb)
    A = L.T @ L
    X = np.array([list(bm.verts[g].co) for g in loc])
    fm = np.array([free[g] for g in loc])
    solu = np.linalg.solve(A[np.ix_(fm, fm)] + np.eye(int(fm.sum())) * 1e-9,
                           -A[np.ix_(fm, ~fm)] @ X[~fm])
    d = np.linalg.norm(solu - X[fm], axis=1) * 1000.0
    log(f"biharmonic relax on {int(fm.sum())} vertices, blend {RELAX}: "
        f"median {np.median(d):.3f} mm, p95 {np.percentile(d, 95):.3f} mm, max {d.max():.3f} mm")
    k = 0
    for j, g in enumerate(loc):
        if fm[j]:
            bm.verts[g].co = bm.verts[g].co.lerp(Vector(solu[k]), RELAX)
            k += 1
    bm.normal_update()

# ------------------------------------------------------------------ where is the palm
NRM = n3.inverted().transposed()
NRMi = NRM.inverted()
w, P, NW, vn = palm_weight(ob, bm, bpy.context.evaluated_depsgraph_get(), log=log, zlow=ZLOW)
if (w > 0.99).sum() < 50:
    raise SystemExit("palm_smooth: the palm weight found almost nothing — check the ramps")

# ------------------------------------------------------------------ smooth the normal field
kd = KDTree(len(bm.verts))
for i, p in enumerate(P):
    kd.insert(Vector(p), i)
kd.balance()
nbrs = [[] for _ in range(len(bm.verts))]
act = np.nonzero(w > 0.005)[0]
for i in act:
    for _, j, dd in kd.find_range(Vector(P[i]), RADIUS):
        if j != i and float(NW[i] @ NW[j]) > FACE_GATE:
            nbrs[i].append((j, math.exp(-(dd * dd) / (2 * (RADIUS / 2) ** 2))))
log(f"spatial kernel r={RADIUS:.0f} mm: {np.mean([len(nbrs[i]) for i in act]):.1f} neighbours "
    f"per palm vertex on average")

field = NW.copy()
for _ in range(ITERS):
    upd = field.copy()
    for i in act:
        if not nbrs[i]:
            continue
        acc = field[i].copy()
        tot = 1.0
        for j, g in nbrs[i]:
            acc += field[j] * g
            tot += g
        upd[i] = acc / tot
    field = upd / np.maximum(np.linalg.norm(upd, axis=1, keepdims=True), 1e-20)


def spread(nf):
    sub = nf[np.nonzero(w > 0.5)[0]]
    m = sub.mean(0)
    m /= max(np.linalg.norm(m), 1e-20)
    return np.degrees(np.arccos(np.clip(sub @ m, -1, 1)))


s0, s1 = spread(NW), spread(field)
log(f"{ITERS} spatial passes. Palm normal spread about its own mean: "
    f"p50 {np.median(s0):.1f} -> {np.median(s1):.1f} deg, "
    f"p95 {np.percentile(s0, 95):.1f} -> {np.percentile(s1, 95):.1f} deg")

bm.to_mesh(me)
bm.free()
me.update()

# back to object space for the custom-normal write
FLD = np.array([list((NRMi @ Vector(n)).normalized()) for n in field])

N1 = N0.copy()
lv = np.array([l.vertex_index for l in me.loops])
lw = w[lv]
touched = lw > 0.0
N1[touched] = N0[touched] * (1.0 - lw[touched, None]) + FLD[lv[touched]] * lw[touched, None]
N1 /= np.maximum(np.linalg.norm(N1, axis=1, keepdims=True), 1e-20)
me.normals_split_custom_set(N1.tolist())
ang = np.degrees(np.arccos(np.clip((N0 * N1).sum(1), -1, 1)))
log(f"custom normals: {int((ang > 0.001).sum())}/{nloops} loops changed, "
    f"{nloops - int((ang > 0.001).sum())} bit-identical; "
    f"swing p50 {np.median(ang[ang > 0.001]):.2f} p95 {np.percentile(ang[ang > 0.001], 95):.2f} "
    f"max {ang.max():.2f} deg")

# ------------------------------------------------------------------ rig safety
bones1 = {b.name: (MW @ b.head_local, MW @ b.tail_local) for b in arm.data.bones}
if set(bones0) != set(bones1):
    raise SystemExit("palm_smooth: bone set changed — aborting")
drift = max(max((bones0[n][0] - bones1[n][0]).length, (bones0[n][1] - bones1[n][1]).length)
            for n in bones0) * 1000.0
if drift > ANCHOR_TOL:
    raise SystemExit(f"palm_smooth: bones moved {drift:.4f} mm — aborting")
co1 = np.array([list(MW @ v.co) for v in me.vertices])
if co1.shape != co0.shape or len(me.loops) != nloops:
    raise SystemExit("palm_smooth: vertex/loop count changed — aborting")
dext = np.abs((co1.max(0) - co1.min(0)) - (co0.max(0) - co0.min(0))).max() * 1000.0
if dext > EXTENT_TOL:
    raise SystemExit(f"palm_smooth: hand extent changed by {dext:.3f} mm — aborting")
bad = sum(1 for v in me.vertices
          if not v.groups or abs(sum(g.weight for g in v.groups) - 1.0) > 0.02)
if bad:
    raise SystemExit(f"palm_smooth: {bad} vertices lost their normalised weight set — aborting")
vmove = np.linalg.norm(co1 - co0, axis=1) * 1000.0
log(f"rig OK: {len(bones1)} bones, drift {drift:.6f} mm, extent {dext:.4f} mm, "
    f"{int((vmove > 1e-6).sum())} vertices moved (max {vmove.max():.4f} mm)")

# ------------------------------------------------------------------ export
bpy.ops.object.select_all(action='DESELECT')
ob.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
# Settings copied verbatim from rig_hand.py:export_fbx().
bpy.ops.export_scene.fbx(
    filepath=DST, use_selection=True, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
    axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    apply_scale_options='FBX_SCALE_ALL', path_mode='STRIP', embed_textures=False,
    mesh_smooth_type='FACE', use_armature_deform_only=False, bake_anim=False,
)
log("exported", DST)
