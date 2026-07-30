# refine_shell.py — Blender headless: adaptive refinement of the COARSE PATCHES left on a
# rigged hand FBX (VRHandPlate_*_rig.fbx / VRHandArcane_*_rig.fbx), in place.
#
# WHY
#   prepare_hand.py decimates the raw Hunyuan mesh to a ~18k-tri budget with a COLLAPSE
#   decimator. Collapse is curvature-driven: it strips the flattest, least-detailed regions
#   hardest — which on an armoured gauntlet is exactly the PALM PLATE and the CUFF BAND.
#   rig_hand.py's watertight pass then holes_fill's whatever the collapse tore open, which
#   adds a few more oversized triangles in the same place.
#   Measured on the shipped VRHandPlate_R_rig.fbx (9 660 tris, mean edge 5.8 mm): the palm
#   carried single SMOOTH-SHADED triangles with 70.6 / 63.6 / 54.0 / 47.4 / 37.9 mm edges —
#   a third of the hand's 190 mm length in one facet. Smooth-shading a facet that large
#   cannot hide it: the player sees hard triangle creases, and the affine UV map over such a
#   triangle spreads its texels 4-15x thinner than the neighbouring shell (measured
#   uvArea/faceArea 0.16-0.66 against a region median of 2.41), which reads as a stretched,
#   smeared texture. Both symptoms are ONE cause: polygon density, not hard normals (all the
#   offending faces are use_smooth=1) and not non-uniform scale (every transform is scale 1).
#
# WHAT IT DOES
#   Two steps, both confined to the coarse patches by a per-vertex COARSENESS weight:
#     w(v) = smoothstep of (mean incident edge length / TARGET_EDGE), so w = 0 wherever the
#     shell is already at or below the target density (fingers, knuckle plates, cuff studs —
#     all their detail is untouched) and w -> 1 on the palm/cuff plates. It is measured on
#     the ORIGINAL topology and rides along as a BMesh vertex layer, so the vertices born
#     during subdivision inherit an interpolated weight and the region has no seam.
#   1. Adaptive LINEAR subdivision of every non-boundary edge longer than TARGET_EDGE
#      (repeat until converged). Linear, not the subdivide operator's "smooth" offset: this
#      shell is a welded AI mesh whose vertex normals are locally wild, and riding them
#      turns every decimation sliver into a spike (render-verified — it looked far worse).
#   2. Taubin lambda/mu low-pass over the subdivided mesh, each vertex's step scaled by w.
#      Taubin alternates a shrinking Laplacian pass with an expanding one, so the plates get
#      genuinely ROUNDED instead of merely re-tessellated, with no volume loss and no
#      spikes; it also files down the pre-existing decimation slivers inside the region.
#   - UVs: subdivision interpolates them linearly, and the offending faces are affinely
#     mapped, so the mapping is bit-for-bit the same function — no NEW distortion is added.
#   - Skin weights: BMesh interpolates the deform layer, so new verts get blended weights.
#     The script asserts every vertex ends up with a normalised weight set.
#   - Open wrist rim: boundary edges are EXCLUDED from subdivision and boundary vertices are
#     PINNED during smoothing (the cuff stump must stay the ragged open loop rig_hand.py
#     deliberately leaves — see make_watertight step 3).
#   - Custom split normals are dropped first (they would be stale after a topology change);
#     the per-face use_smooth flags survive and drive shading, which is what the FBX
#     exporter's mesh_smooth_type='FACE' writes and Unity's normalImportMode=Import reads.
#
# WHY IT RUNS ON THE FBX AND NOT IN THE PIPELINE
#   The prepped intermediates (ressources/hands/*_prepped.glb) are gitignored and no longer
#   present, and the raw Hunyuan GLBs are gone with them, so rig_hand.py cannot be re-run.
#   The exported FBX is the surviving source of truth. A no-op import/export round trip was
#   verified lossless against it first: 19/19 bones, max bone head/tail drift 1.05e-4 mm,
#   bbox drift < 1e-5 mm, per-group weight-sum drift 0.000000 — so FBX-level surgery is safe.
#   Export settings below are copied verbatim from rig_hand.py:export_fbx().
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/refine_shell.py -- \
#       <in.fbx> [out.fbx] [target_edge_mm]
#   Omitting <out.fbx> rewrites <in.fbx> in place. Default target edge 8 mm.
#
# AFTER RUNNING: the AssetBundle must be rebuilt (scripts/build-bundles.sh) — the mod loads
# the hands from gloomhavenvr.bundle, not from the FBX.

import bpy, bmesh, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC = argv[0]
DST = argv[1] if len(argv) > 1 else SRC
TARGET_EDGE = (float(argv[2]) if len(argv) > 2 else 8.0) / 1000.0   # metres

MAX_PASSES = 6          # adaptive passes; converges well before this in practice
TAUBIN_ITERS = 20       # lambda/mu pairs
TAUBIN_LAMBDA = 0.55
TAUBIN_MU = -0.58       # |mu| > lambda -> pass-band keeps the volume, no shrink
COARSE_KNEE = 1.5       # w hits 1.0 at KNEE x TARGET_EDGE mean incident edge length


def log(*a):
    print("[refine_shell]", *a)


def edge_stats(bm):
    ls = sorted(e.calc_length() for e in bm.edges)
    return ls[len(ls) // 2], ls[-1]


def smoothstep01(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


def refine(o):
    me = o.data

    # Stale custom split normals would survive the topology change as garbage; the mesh has
    # zero sharp edges, so the per-face use_smooth flags carry all the shading intent.
    if me.has_custom_normals:
        bpy.context.view_layer.objects.active = o
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
        log("cleared stale custom split normals (0 sharp edges — use_smooth carries shading)")

    tris0 = sum(len(p.vertices) - 2 for p in me.polygons)
    verts0 = len(me.vertices)

    bm = bmesh.new()
    bm.from_mesh(me)
    med0, max0 = edge_stats(bm)
    over0 = sum(1 for e in bm.edges if e.calc_length() > TARGET_EDGE and not e.is_boundary)
    log(f"before: {verts0} verts, {tris0} tris, median edge {med0*1000:.2f} mm, "
        f"longest {max0*1000:.1f} mm, {over0} interior edges over {TARGET_EDGE*1000:.0f} mm")

    bm.verts.ensure_lookup_table()

    # --- coarseness weight on the ORIGINAL topology; interpolated onto the new vertices ---
    cw = bm.verts.layers.float.new("coarse")
    for v in bm.verts:
        le = [e.calc_length() for e in v.link_edges]
        mean = sum(le) / len(le) if le else 0.0
        v[cw] = smoothstep01((mean / TARGET_EDGE - 1.0) / (COARSE_KNEE - 1.0))
    hot = sum(1 for v in bm.verts if v[cw] > 0.05)
    log(f"coarseness: {hot}/{len(bm.verts)} vertices sit in a patch coarser than "
        f"{TARGET_EDGE*1000:.0f} mm and will be refined; the rest is left untouched")

    for p in range(MAX_PASSES):
        long_e = [e for e in bm.edges
                  if not e.is_boundary and e.calc_length() > TARGET_EDGE]
        if not long_e:
            break
        # smooth=0 -> pure linear split. See header: riding the vertex normals spikes.
        bmesh.ops.subdivide_edges(bm, edges=long_e, cuts=1, use_grid_fill=True)
        bm.verts.ensure_lookup_table()
        log(f"  pass {p+1}: split {len(long_e)} edges -> {len(bm.verts)} verts")

    # --- Taubin lambda/mu low-pass, scaled per vertex by the coarseness weight ----------
    movable = [v for v in bm.verts
               if v[cw] > 0.01 and not any(e.is_boundary for e in v.link_edges)]
    start = {v: v.co.copy() for v in movable}
    for _ in range(TAUBIN_ITERS):
        for k in (TAUBIN_LAMBDA, TAUBIN_MU):
            moves = []
            for v in movable:
                nb = [e.other_vert(v) for e in v.link_edges]
                if len(nb) < 3:
                    continue
                c = sum((n.co for n in nb), Vector()) / len(nb)
                moves.append((v, v.co + (c - v.co) * (k * v[cw])))
            for v, co in moves:
                v.co = co
    if movable:
        disp = sorted((v.co - start[v]).length for v in movable)
        log(f"  Taubin {TAUBIN_ITERS}x(l={TAUBIN_LAMBDA}, m={TAUBIN_MU}) over {len(movable)} "
            f"verts: median move {disp[len(disp)//2]*1000:.2f} mm, "
            f"p95 {disp[int(0.95*len(disp))]*1000:.2f} mm, max {disp[-1]*1000:.2f} mm")

    med1, max1 = edge_stats(bm)
    bm.verts.layers.float.remove(cw)
    bm.to_mesh(me)
    bm.free()
    me.update()

    tris1 = sum(len(p.vertices) - 2 for p in me.polygons)
    log(f"after:  {len(me.vertices)} verts (+{len(me.vertices)-verts0}), {tris1} tris "
        f"(+{tris1-tris0}, {100.0*tris1/tris0-100:+.0f} %), median edge {med1*1000:.2f} mm, "
        f"longest {max1*1000:.1f} mm")

    # Skinning guard: every vertex must still carry a normalised weight set.
    bad = 0
    for v in me.vertices:
        s = sum(g.weight for g in v.groups)
        if not v.groups or abs(s - 1.0) > 0.02:
            bad += 1
    if bad:
        raise SystemExit(f"refine_shell: {bad} vertices lost their skin weights — aborting")
    log(f"skin guard: all {len(me.vertices)} vertices carry a normalised weight set")
    return len(me.vertices)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = [x for x in bpy.data.objects if x.type == 'ARMATURE'][0]
meshes = [x for x in bpy.data.objects if x.type == 'MESH']
log(f"loaded {SRC}: armature '{arm.name}' ({len(arm.data.bones)} bones), "
    f"{len(meshes)} mesh(es)")

for mo in meshes:
    log(f"--- {mo.name} ---")
    refine(mo)

bpy.ops.object.select_all(action='DESELECT')
for mo in meshes:
    mo.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
# Settings copied verbatim from rig_hand.py:export_fbx() — see its comments for why each
# one matters (notably apply_scale_options='FBX_SCALE_ALL', which keeps every transform at
# scale 1 and pushes the unit conversion into the FBX UnitScaleFactor).
bpy.ops.export_scene.fbx(
    filepath=DST,
    use_selection=True,
    object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    axis_forward='-Z',
    axis_up='Y',
    bake_space_transform=True,
    apply_scale_options='FBX_SCALE_ALL',
    path_mode='STRIP',
    embed_textures=False,
    mesh_smooth_type='FACE',
    use_armature_deform_only=False,
    bake_anim=False,
)
log("exported", DST)
