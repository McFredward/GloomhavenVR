# palm_reunwrap.py — Blender headless: give the Plate gauntlet's PALM its own, properly
# sized UV island in a grown atlas, without touching a single texel the rest of the hand uses.
#
# WHY
#   VRHandPlate's albedo was baked from the ORIGINAL coarse Hunyuan mesh. On the palm the
#   decimator had left six huge triangles, so the bake wrote one near-flat colour per wedge:
#   the "star" of facets the player sees is PIGMENT, not polygons (proved by an emission-only
#   render — no lights, every pixel literally a texel — where the star is fully present, and
#   by a clay render where it is completely absent). Measured on VRHandPlate_R_rig.fbx:
#     median texel density over the whole hand   3.24 texels/mm
#     the palm patch                             1.07 texels/mm, 13 % of the surface living
#                                                on 0.58 % of the atlas, shredded into 3355
#                                                disconnected UV islands, ~10 texels per face
#   At ten texels per triangle there is no signal left to filter, so every image-space repair
#   (blur, gain, luma match, per-face flatten) failed: two wedges that touch on the hand can
#   sit anywhere in the atlas, so no image neighbourhood contains both.
#
# WHAT THIS DOES
#   1. Selects the palm PLATE geometrically, not by a density threshold (a threshold picks
#      only the worst faces and leaks onto the back of the hand — that regression shipped
#      once). The region is: flood-fill from a texel-starved seed, restricted to faces that
#      (a) face the palm (world -Y), (b) sit above the cuff rim (world z > ZLOW), (c) are not
#      skinned to a finger bone. The rig's own skin weights draw the finger boundary, so the
#      patch stops exactly at the MCP creases and at the cuff rim — both real geometric
#      creases, which is where a UV seam is invisible.
#   2. Rewrites the UV layout: every EXISTING uv is multiplied by 0.5, so in a 4096 atlas
#      whose bottom-left quadrant is a 1:1 copy of the old 2048 one, every untouched island
#      samples exactly the same texels at exactly the same mip. Nothing is resampled, so the
#      back of the hand cannot change.
#   3. Unwraps the palm patch as ONE island (angle-based + minimize_stretch) into the freed
#      three quadrants at a chosen texel density, and reports the density spread and any UV
#      self-overlap.
#   4. Rasterises that island and writes an .npz: for every destination texel, its 3-D point
#      on the mesh and the OLD uv it used to sample. palm_paint.py (plain python, needs PIL)
#      turns that into pixels.
#   L and R get SEPARATE islands (passed in via --origin): the two meshes are mirrors but not
#   identical topology (13014 vs 13016 verts), so a shared island would need a shared unwrap.
#   Atlas space is not scarce — the palm needs ~0.5 Mtexel and 12.6 Mtexel are free.
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/palm_reunwrap.py -- \
#       <in.fbx> <out.fbx> <out.npz> [--origin U V] [--density 4.0] [--oldpx 2048] [--newpx 4096]
#
# AFTER RUNNING: palm_paint.py paints the island, then the AssetBundle must be rebuilt with
# the game-exact editor (/home/claw/unity-2021.3.5/Editor/Unity) — the mod loads the hands
# from gloomhavenvr.bundle, not from the FBX.
import bpy, bmesh, sys, math, os
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, DST, NPZ = argv[0], argv[1], argv[2]
opt = argv[3:]


def getopt(name, default, n=1):
    if name in opt:
        i = opt.index(name)
        v = [float(x) for x in opt[i + 1:i + 1 + n]]
        return v if n > 1 else v[0]
    return default


ORIGIN = getopt("--origin", [0.52, 0.02], 2)
DENSITY = getopt("--density", 4.0)          # texels per mm on the new island
OLDPX = int(getopt("--oldpx", 2048))
NEWPX = int(getopt("--newpx", 4096))
NY_MIN = getopt("--ny", 0.15)               # face must face the palm at least this much
ZLOW = getopt("--zlow", -78.0)              # mm, world: cuff rim
GROW = int(getopt("--grow", 1))

SCALE = OLDPX / float(NEWPX)                # 0.5: old atlas -> bottom-left quadrant


def log(*a):
    print("[palm_reunwrap]", *a)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if len(meshes) != 1:
    raise SystemExit(f"expected one mesh, got {len(meshes)}")
ob = meshes[0]
me = ob.data
MW = ob.matrix_world
log(f"loaded {SRC}: {len(me.vertices)} verts, {len(me.polygons)} faces, "
    f"armature '{arm.name}' ({len(arm.data.bones)} bones)")

# ---------------------------------------------------------------- region select
FINGER = [g.index for g in ob.vertex_groups
          if any(k in g.name for k in ("Index", "Middle", "Ring", "Pinky", "Thumb"))]
fw = np.zeros(len(me.vertices))
for i, v in enumerate(me.vertices):
    fw[i] = sum(g.weight for g in v.groups if g.group in FINGER)

bm = bmesh.new(); bm.from_mesh(me)
uvl = bm.loops.layers.uv.active
bm.faces.ensure_lookup_table()
N = len(bm.faces)
dens = np.zeros(N); area = np.zeros(N); ctrz = np.zeros(N); ny = np.zeros(N); ffw = np.zeros(N)
n3 = MW.to_3x3()
for f in bm.faces:
    a = f.calc_area(); area[f.index] = a
    if a > 0:
        uv = [l[uvl].uv for l in f.loops]
        ua = sum(abs((uv[i] - uv[0]).cross(uv[i + 1] - uv[0])) / 2 for i in range(1, len(uv) - 1))
        dens[f.index] = math.sqrt((ua * OLDPX * OLDPX) / (a * 1e6))
    ctrz[f.index] = (MW @ f.calc_center_median()).z * 1000.0
    ny[f.index] = -(n3 @ f.normal).normalized().y      # the palm faces world -Y
    ffw[f.index] = max(fw[v.index] for v in f.verts)
med = float(np.median(dens[dens > 0]))

nbrs = [[] for _ in range(N)]
for e in bm.edges:
    lf = list(e.link_faces)
    for i in range(len(lf)):
        for j in range(len(lf)):
            if i != j:
                nbrs[lf[i].index].append(lf[j].index)
nbrs = [np.array(sorted(set(x)), np.int64) for x in nbrs]

d = dens.copy()
for _ in range(10):                                     # smooth density over the face graph
    nd = d.copy()
    for i in range(N):
        nb = nbrs[i]
        if len(nb):
            nd[i] = 0.35 * d[i] + 0.65 * (d[nb] * area[nb]).sum() / max(area[nb].sum(), 1e-12)
    d = nd
seed = (d < med * 0.55) & (ny > 0.2) & (ctrz > ZLOW) & (ffw < 0.4)
allowed = (ny > NY_MIN) & (ctrz > ZLOW) & (ffw < 0.5)
sel = np.zeros(N, bool)
stack = list(np.nonzero(seed & allowed)[0])
for i in stack:
    sel[i] = True
while stack:
    f = stack.pop()
    for j in nbrs[f]:
        if allowed[j] and not sel[j]:
            sel[j] = True; stack.append(j)


def dilate(m, k):
    m = m.copy()
    for _ in range(k):
        add = np.zeros(N, bool)
        for i in np.nonzero(m)[0]:
            add[nbrs[i]] = True
        m |= add
    return m


sel = dilate(~dilate(~dilate(sel, 3), 3), GROW)         # close pinholes, then a margin ring
log(f"region: seed {int((seed & allowed).sum())} -> {int(sel.sum())} faces, "
    f"{100 * area[sel].sum() / area.sum():.2f} % of the surface, {area[sel].sum() * 1e6:.0f} mm2; "
    f"old density inside {np.median(dens[sel]):.2f} texels/mm (hand median {med:.2f})")
bm.free()

# --------------------------------------------------- remember the OLD uv, then unwrap
uv_old = np.empty(len(me.loops) * 2, np.float64)
me.uv_layers[0].data.foreach_get("uv", uv_old)
uv_old = uv_old.reshape(-1, 2)

for e in me.edges:
    e.use_seam = False
selmask = sel
for p in me.polygons:
    p.select = bool(selmask[p.index])
for v in me.vertices:
    v.select = False
for e in me.edges:
    e.select = False
# boundary of the region -> seam, so the unwrap makes exactly one island
face_of_edge = {}
for p in me.polygons:
    for ek in p.edge_keys:
        face_of_edge.setdefault(ek, []).append(p.index)
ekey_to_edge = {e.key: e for e in me.edges}
nb_seam = 0
for ek, fs in face_of_edge.items():
    ins = [selmask[f] for f in fs]
    if any(ins) and not all(ins):
        ekey_to_edge[ek].use_seam = True
        nb_seam += 1
log(f"marked {nb_seam} boundary edges as seam")

bpy.context.view_layer.objects.active = ob
ob.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_mode(type='FACE')
bpy.ops.mesh.select_all(action='DESELECT')
bpy.ops.object.mode_set(mode='OBJECT')
for p in me.polygons:
    p.select = bool(selmask[p.index])
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=0.001)
bpy.ops.uv.minimize_stretch(iterations=120)
bpy.ops.object.mode_set(mode='OBJECT')

uv_new = np.empty(len(me.loops) * 2, np.float64)
me.uv_layers[0].data.foreach_get("uv", uv_new)
uv_new = uv_new.reshape(-1, 2)

loop_face = np.empty(len(me.loops), np.int64)
for p in me.polygons:
    for li in p.loop_indices:
        loop_face[li] = p.index
inreg = selmask[loop_face]

# ---------------------------------------------------------- scale the island to DENSITY
isl = uv_new[inreg]
isl = isl - isl.min(0)
uva = 0.0
warea = 0.0
for p in me.polygons:
    if not selmask[p.index]:
        continue
    ls = list(p.loop_indices)
    a = [Vector(uv_new[l]) for l in ls]
    uva += sum(abs((a[i] - a[0]).cross(a[i + 1] - a[0])) / 2 for i in range(1, len(a) - 1))
    warea += p.area
s = DENSITY / (NEWPX * math.sqrt(uva / (warea * 1e6)))
isl = isl * s
w, h = isl.max(0)
log(f"island: {w * NEWPX:.0f} x {h * NEWPX:.0f} texels for {warea * 1e6:.0f} mm2 "
    f"at {DENSITY} texels/mm")
if ORIGIN[0] + w > 1.0 or ORIGIN[1] + h > 1.0:
    raise SystemExit(f"island {w:.3f}x{h:.3f} does not fit at origin {ORIGIN}")
if ORIGIN[0] < SCALE and ORIGIN[1] < SCALE:
    raise SystemExit("island origin lands inside the preserved old-atlas quadrant")
isl = isl + np.array(ORIGIN)

uv_out = uv_old * SCALE
uv_out[inreg] = isl
me.uv_layers[0].data.foreach_set("uv", uv_out.ravel())

# --------------------------------------------------------------------- quality report
newdens = np.zeros(N)
for p in me.polygons:
    if not selmask[p.index]:
        continue
    a = [Vector(uv_out[l]) for l in p.loop_indices]
    ua = sum(abs((a[i] - a[0]).cross(a[i + 1] - a[0])) / 2 for i in range(1, len(a) - 1))
    if p.area > 0:
        newdens[p.index] = math.sqrt((ua * NEWPX * NEWPX) / (p.area * 1e6))
nd = newdens[selmask]
log("new palm density texels/mm: p5 %.2f p25 %.2f median %.2f p75 %.2f p95 %.2f" %
    tuple(np.percentile(nd, [5, 25, 50, 75, 95])))
uv_chk = np.empty(len(me.loops) * 2, np.float64)
me.uv_layers[0].data.foreach_get("uv", uv_chk)
uv_chk = uv_chk.reshape(-1, 2)
want = (uv_old[~inreg] * SCALE).astype(np.float32).astype(np.float64)
if not np.array_equal(uv_chk[~inreg], want):
    raise SystemExit("untouched uv drifted — halving must be bit-exact")
log(f"untouched loops: {int((~inreg).sum())} — every one is exactly old_uv * {SCALE}")

# ------------------------------------------------------------- rasterise the new island
# For each destination texel: the 3-D point it lands on, and the OLD uv that point used.
ys, xs, pos, oldu, fid = [], [], [], [], []
claimed = {}
overlap = 0
for p in me.polygons:
    if not selmask[p.index]:
        continue
    ls = list(p.loop_indices)
    P = np.array([me.vertices[me.loops[l].vertex_index].co[:] for l in ls]) * 1000.0
    U = np.array([uv_out[l] for l in ls])
    O = np.array([uv_old[l] for l in ls])
    px = U[:, 0] * NEWPX
    py = (1.0 - U[:, 1]) * NEWPX
    for i in range(1, len(ls) - 1):
        tri = [0, i, i + 1]
        ax, ay = px[tri[0]], py[tri[0]]
        bx, by = px[tri[1]], py[tri[1]]
        cx, cy = px[tri[2]], py[tri[2]]
        x0 = max(0, int(min(ax, bx, cx)) - 1); x1 = min(NEWPX, int(max(ax, bx, cx)) + 2)
        y0 = max(0, int(min(ay, by, cy)) - 1); y1 = min(NEWPX, int(max(ay, by, cy)) + 2)
        if x1 <= x0 or y1 <= y0:
            continue
        det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(det) < 1e-12:
            continue
        yy, xx = np.mgrid[y0:y1, x0:x1]
        qx = xx + 0.5; qy = yy + 0.5
        l1 = ((by - cy) * (qx - cx) + (cx - bx) * (qy - cy)) / det
        l2 = ((cy - ay) * (qx - cx) + (ax - cx) * (qy - cy)) / det
        l3 = 1.0 - l1 - l2
        # a small negative tolerance keeps the texels straddling a triangle edge — bilinear
        # filtering reaches them anyway, and it stops pinholes along the island's interior
        m = (l1 >= -0.02) & (l2 >= -0.02) & (l3 >= -0.02)
        if not m.any():
            continue
        w1, w2, w3 = l1[m], l2[m], l3[m]
        pp = (np.outer(w1, P[tri[0]]) + np.outer(w2, P[tri[1]]) + np.outer(w3, P[tri[2]]))
        oo = (np.outer(w1, O[tri[0]]) + np.outer(w2, O[tri[1]]) + np.outer(w3, O[tri[2]]))
        ys.append(yy[m]); xs.append(xx[m]); pos.append(pp); oldu.append(oo)
        fid.append(np.full(m.sum(), p.index, np.int64))

ys = np.concatenate(ys); xs = np.concatenate(xs)
pos = np.concatenate(pos); oldu = np.concatenate(oldu); fid = np.concatenate(fid)
key = ys.astype(np.int64) * NEWPX + xs
order = np.argsort(key, kind='stable')
key, ys, xs, pos, oldu, fid = (a[order] for a in (key, ys, xs, pos, oldu, fid))
uniq, first, counts = np.unique(key, return_index=True, return_counts=True)
# Real UV self-overlap (two DISTANT parts of the palm landing on one texel) would ruin the
# island; texels shared by neighbouring triangles are just the edge tolerance. Separate them
# by how far apart in 3-D the claimants are.
far = 0
for i in np.nonzero(counts > 1)[0]:
    a, b = first[i], first[i] + counts[i]
    if np.linalg.norm(pos[a:b] - pos[a], axis=1).max() > 10.0:
        far += 1
ys, xs, pos, oldu, fid = ys[first], xs[first], pos[first], oldu[first], fid[first]
log(f"rasterised {len(ys)} texels ({100.0 * len(ys) / (NEWPX * NEWPX):.2f} % of the new atlas); "
    f"{int((counts > 1).sum())} shared by adjacent triangles, "
    f"{far} by parts of the palm more than 10 mm apart (island self-overlap — must be ~0)")
# Which texels of the OLD atlas are real surface, as opposed to the dilation halo the baker
# smeared between islands? palm_paint.py lifts its detail from healthy plate areas, and a
# halo texel is a blurred dark blob that would be replayed across the palm as a smudge.
cov = np.zeros((OLDPX, OLDPX), bool)
for p in me.polygons:
    ls = list(p.loop_indices)
    U = np.array([uv_old[l] for l in ls])
    qx = U[:, 0] * OLDPX
    qy = (1.0 - U[:, 1]) * OLDPX
    for i in range(1, len(ls) - 1):
        t = [0, i, i + 1]
        ax, ay, bx, by, cx, cy = qx[t[0]], qy[t[0]], qx[t[1]], qy[t[1]], qx[t[2]], qy[t[2]]
        x0 = max(0, int(min(ax, bx, cx))); x1 = min(OLDPX, int(max(ax, bx, cx)) + 2)
        y0 = max(0, int(min(ay, by, cy))); y1 = min(OLDPX, int(max(ay, by, cy)) + 2)
        if x1 <= x0 or y1 <= y0:
            continue
        det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(det) < 1e-12:
            continue
        yy, xx = np.mgrid[y0:y1, x0:x1]
        gx = xx + 0.5; gy = yy + 0.5
        l1 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / det
        l2 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / det
        mm = (l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)
        if mm.any():
            cov[yy[mm], xx[mm]] = True
log(f"old-atlas UV coverage: {100.0 * cov.mean():.1f} % of {OLDPX}x{OLDPX} is real surface")

np.savez_compressed(NPZ, ys=ys, xs=xs, pos=pos, olduv=oldu, fid=fid,
                    cov=np.packbits(cov), covshape=np.array(cov.shape),
                    newpx=np.array([NEWPX]), oldpx=np.array([OLDPX]),
                    scale=np.array([SCALE]))
log("wrote", NPZ)

# --------------------------------------------------------------------------- export
for e in me.edges:
    e.use_seam = False
bpy.ops.object.select_all(action='DESELECT')
ob.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
# Settings copied verbatim from rig_hand.py:export_fbx() / refine_shell.py.
bpy.ops.export_scene.fbx(
    filepath=DST,
    use_selection=True,
    object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL',
    global_scale=1.0,
    bake_space_transform=False,
    mesh_smooth_type='FACE',
    use_mesh_modifiers=False,
    use_armature_deform_only=False,
    bake_anim=False,
    path_mode='COPY',
    embed_textures=False,
    axis_forward='-Z',
    axis_up='Y',
)
log("wrote", DST)
