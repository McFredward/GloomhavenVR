# relax_palm.py — Blender headless: iron the FOLD out of a gauntlet palm, in place.
#
# WHY
#   The palm's texture problem is fixed (palm_reunwrap.py + palm_paint.py). What is left is a
#   hard diagonal crease running from the thumb web across the palm — and it is GEOMETRY, not
#   pigment: it is plainly visible in an untextured clay render, where no texture is involved
#   at all. It comes from prepare_hand.py's collapse decimator, which strips the flattest
#   regions hardest and leaves a fold where two collapsed fans meet.
#
# WHAT IT DOES
#   Taubin lambda/mu low-pass over the palm patch, at full strength in its interior and ramped
#   to zero over RAMP rings as it approaches the pinned rim, so the treated area blends into the
#   untouched surroundings with no step. Taubin rather than plain Laplacian because the
#   alternating shrink/expand pair keeps the volume: a Laplacian strong enough to remove this
#   fold would visibly deflate the palm.
#
#   The patch is the SAME region palm_reunwrap.py re-unwrapped, selected by the same rule, so
#   the two agree by construction: faces that (a) face the palm, (b) sit above the cuff rim,
#   (c) are not skinned to a finger bone. Its boundary vertices are PINNED, which is what keeps
#   the MCP creases, the cuff rim and the back of the hand untouched.
#
#   UVs are per-loop and untouched — moving a vertex does not move its texel, so the new palm
#   island stays exactly as painted.
#
# RIG SAFETY (asserted, not assumed; the run fails rather than ship a broken hand)
#   bone count and names, every Anchor_* head/tail within ANCHOR_TOL mm, hand extent within
#   EXTENT_TOL mm, and every vertex still carrying a normalised weight set.
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/relax_palm.py -- \
#       <in.fbx> [out.fbx] [--iters 40] [--lambda 0.6] [--mu -0.63] [--ramp 4]
#   Omitting <out.fbx> rewrites <in.fbx> in place.
#
# AFTER RUNNING: rebuild the AssetBundle with the 2021.3.5f1 editor (see prebuilt/README.md).
import bpy, bmesh, sys, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC = pos[0]
DST = pos[1] if len(pos) > 1 else SRC


def getopt(name, default):
    return float(argv[argv.index(name) + 1]) if name in argv else default


ITERS = int(getopt("--iters", 40))
LAM = getopt("--lambda", 0.60)
MU = getopt("--mu", -0.63)          # |mu| > lambda -> pass-band keeps the volume
RAMP = getopt("--ramp", 4.0)        # rings of blend-in from the pinned rim
NY_MIN = getopt("--ny", 0.15)       # face must face the palm at least this much
ZLOW = getopt("--zlow", -78.0)      # mm, world: the cuff rim
ANCHOR_TOL = 0.001                  # mm
EXTENT_TOL = 0.05                   # mm


def log(*a):
    print("[relax_palm]", *a)


def smoothstep(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


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

bones0 = {b.name: (MW @ b.head_local, MW @ b.tail_local) for b in arm.data.bones}
co0 = np.array([list(MW @ v.co) for v in me.vertices])
log(f"loaded {SRC}: {len(me.vertices)} verts, {len(me.polygons)} faces, {len(bones0)} bones")

# ------------------------------------------------------------------ region select
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

sel = np.zeros(len(bm.faces), bool)
for f in bm.faces:
    ny = -(n3 @ f.normal).normalized().y
    cz = (MW @ f.calc_center_median()).z * 1000.0
    ffw = max(fw[v.index] for v in f.verts)
    sel[f.index] = ny > NY_MIN and cz > ZLOW and ffw < 0.5
log(f"palm patch: {int(sel.sum())}/{len(bm.faces)} faces")
if not sel.any():
    raise SystemExit("relax_palm: the palm patch is empty — check --ny / --zlow")

inside = np.zeros(len(bm.verts), bool)
touching_outside = np.zeros(len(bm.verts), bool)
for f in bm.faces:
    for v in f.verts:
        if sel[f.index]:
            inside[v.index] = True
        else:
            touching_outside[v.index] = True
# Pinned: the patch rim (a vertex shared with an unselected face) and any mesh boundary.
for v in bm.verts:
    if any(e.is_boundary for e in v.link_edges):
        touching_outside[v.index] = True
movable_mask = inside & ~touching_outside
log(f"vertices: {int(inside.sum())} in the patch, {int(movable_mask.sum())} movable "
    f"({int((inside & touching_outside).sum())} pinned on its rim)")

# ------------------------------------------------------------------ how hard each vertex moves
# NOT by dihedral angle. That was the first try and it failed on the measurement: it caught 180
# vertices, moved 231, and left the worst dihedral in the patch at 103.3 degrees — unchanged.
# The fold is not a single sharp ridge that a per-edge criterion can find; it is a broad shape
# error where two collapsed fans meet, spread over centimetres, with unremarkable angles all
# along it. The palm inside this patch also carries no detail worth protecting — it is a plain
# plate — so the whole patch is smoothed and the weight only ramps in from the PINNED RIM, which
# is what keeps the MCP creases and the cuff from developing a step of their own.
rings = np.full(len(bm.verts), -1, np.int64)
frontier = [bm.verts[i] for i in np.nonzero(inside & touching_outside)[0]]
for v in frontier:
    rings[v.index] = 0
while frontier:
    nxt = []
    for v in frontier:
        for e in v.link_edges:
            o = e.other_vert(v)
            if movable_mask[o.index] and rings[o.index] < 0:
                rings[o.index] = rings[v.index] + 1
                nxt.append(o)
    frontier = nxt
w = np.zeros(len(bm.verts))
for i in np.nonzero(movable_mask)[0]:
    r = rings[i]
    w[i] = smoothstep(r / RAMP) if r >= 0 else 1.0
hot = int((w > 0.05).sum())
log(f"weight: {hot} vertices above 0.05, full strength from ring {int(RAMP)} inward "
    f"(deepest ring {int(rings.max())})")

# The number this script exists to reduce, measured before and after over the same edge set.
def worst_dihedral():
    worst = 0.0
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        if not (movable_mask[e.verts[0].index] or movable_mask[e.verts[1].index]):
            continue
        a, b = e.link_faces
        if a.normal.length > 0 and b.normal.length > 0:
            worst = max(worst, math.degrees(a.normal.angle(b.normal)))
    return worst


before_worst = worst_dihedral()

# ------------------------------------------------------------------ Taubin low-pass
movable = [bm.verts[i] for i in np.nonzero(w > 0.01)[0]]
start = {v: v.co.copy() for v in movable}
for _ in range(ITERS):
    for k in (LAM, MU):
        moves = []
        for v in movable:
            nb = [e.other_vert(v) for e in v.link_edges]
            if len(nb) < 3:
                continue
            c = sum((n.co for n in nb), Vector()) / len(nb)
            moves.append((v, v.co + (c - v.co) * (k * w[v.index])))
        for v, co in moves:
            v.co = co
if movable:
    disp = sorted((v.co - start[v]).length for v in movable)
    log(f"Taubin {ITERS}x(l={LAM}, m={MU}) over {len(movable)} verts: "
        f"median move {disp[len(disp)//2]*1000:.2f} mm, p95 {disp[int(0.95*len(disp))]*1000:.2f} mm, "
        f"max {disp[-1]*1000:.2f} mm")

after = worst_dihedral()
log(f"worst dihedral inside the patch: {before_worst:.1f}° -> {after:.1f}°")

bm.to_mesh(me)
bm.free()
me.update()

# ------------------------------------------------------------------ rig safety
bones1 = {b.name: (MW @ b.head_local, MW @ b.tail_local) for b in arm.data.bones}
if set(bones0) != set(bones1):
    raise SystemExit("relax_palm: bone set changed — aborting")
drift = max(max((bones0[n][0] - bones1[n][0]).length, (bones0[n][1] - bones1[n][1]).length)
            for n in bones0) * 1000.0
if drift > ANCHOR_TOL:
    raise SystemExit(f"relax_palm: bones moved {drift:.4f} mm (> {ANCHOR_TOL} mm) — aborting")

co1 = np.array([list(MW @ v.co) for v in me.vertices])
ext0 = co0.max(0) - co0.min(0)
ext1 = co1.max(0) - co1.min(0)
dext = np.abs(ext1 - ext0).max() * 1000.0
if dext > EXTENT_TOL:
    raise SystemExit(f"relax_palm: hand extent changed by {dext:.3f} mm (> {EXTENT_TOL} mm) — aborting")

bad = sum(1 for v in me.vertices
          if not v.groups or abs(sum(g.weight for g in v.groups) - 1.0) > 0.02)
if bad:
    raise SystemExit(f"relax_palm: {bad} vertices lost their normalised weight set — aborting")
log(f"rig OK: {len(bones1)}/{len(bones0)} bones, max bone drift {drift:.6f} mm, "
    f"extent drift {dext:.4f} mm, all {len(me.vertices)} vertices weighted")

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
