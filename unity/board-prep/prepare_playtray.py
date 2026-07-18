# prepare_playtray.py — Blender headless prep for the control-board GLB → bundle-ready PlayTray.
#
# Turns the raw AI-generated board GLB (Hunyuan3D/TRELLIS/Replicate output) into an asset
# that satisfies the mod's bundle contract (unity/GloomhavenVR.Assets/Assets/Bundle/Table/README.md):
#   - decimated to a VR-sane tri count
#   - real scale 0.64 x 0.32 m (1 unit = 1 m)
#   - oriented: board lying in local XY, thin axis = Z, decorated TOP face toward -Z, body z>0, pivot centred
#   - textures downscaled to 2K
#   - six named empty child anchors: Slot1, Slot2, ShortRestToken, LongRestToken, ConfirmButton, UndoButton
#
# RUN (from a normal terminal, Blender 3.x/4.x installed):
#   blender --background --python prepare_playtray.py -- \
#       "<path/to/replicate-prediction-....glb>" "<path/to/PlayTray_prepped.glb>"
#
# Then eyeball the two flags below in Blender GUI once (open the prepped GLB) and re-run if a face/flip is wrong.

import bpy, bmesh, sys, math, os
from mathutils import Vector

# ---- flags you may need to flip after a visual check (open the output in Blender) ----
TARGET_WIDTH_M   = 0.64     # longest footprint edge -> metres (contract: 0.64 x ~0.32)
TARGET_TRIS      = 20000    # decimation target (VR: a static prop; 15-25k is plenty)
FLIP_TOP_FACE    = False    # set True if the DECORATED face ends up pointing +Z instead of -Z
UPRIGHT_FROM_YUP = True     # most GLBs export Y-up; True rotates -90deg X so the board lies in XY
TEX_MAX          = 2048     # downscale every image to this max edge (4K -> 2K)
# Watertight hole-fill (ported from hand-prep/rig_hand.py make_watertight). AI board meshes are
# fragmented shells with genuine GAPS (esp. the deep 16vm268h/Bronze board's hollow back), which
# two-sided rendering (_Cull=0) CANNOT fill — only masks culled thin walls. Welding shells + filling
# torn boundaries + capping the hollow BACK closes the see-through. The front (decorated, z~0) face
# gets only gentle holes_fill: a torn hole gets a floor (good), a properly-modelled closed recess has
# no boundary edges so it is untouched (the card slots are NOT filled). Only the BACK band is fan-capped.
WATERTIGHT       = True
WT_MERGE         = 0.0005   # global shell-seam weld distance (m)
WT_PASSES        = 3        # boundary-close iterations
WT_BWELD         = 0.0018   # boundary-only weld distance (m) to pull ragged rims together
WT_BACK_BAND     = 0.020    # fan-cap boundary edges within this of the BACK (max-Z / hollow interior)


def make_watertight(o):
    """Weld the fragmented AI board shell into one connected, hole-closed surface (in place).
    Adapted from the hand rig: the hand fan-caps the min-Z wrist stump; the BOARD fan-caps the
    max-Z hollow BACK, leaving the decorated front face (z~0) to gentle holes_fill only."""
    me = o.data

    def _bnd():
        bm = bmesh.new(); bm.from_mesh(me)
        nb = sum(1 for e in bm.edges if e.is_boundary)
        bm.free(); return nb

    before = _bnd()
    # 1) global weld of coincident shell seams
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=WT_MERGE)
    bm.to_mesh(me); bm.free(); me.update()

    # 2) iterative boundary-close passes (floors torn holes incl. front-face gaps; a closed recess
    #    has no boundary edges so it is left alone)
    for _ in range(WT_PASSES):
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.holes_fill(bm, edges=bm.edges, sides=0)
        bnd = [v for v in bm.verts if any(e.is_boundary for e in v.link_edges)]
        if bnd:
            bmesh.ops.remove_doubles(bm, verts=bnd, dist=WT_BWELD)
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        bm.to_mesh(me); bm.free(); me.update()

    # 3) fan-cap the hollow BACK (max-Z). After orientation the decorated face sits at z~0 and the
    #    body extends to +Z; a deep board (Bronze) is hollow at the back, the see-through path. The
    #    rim is a ragged near-loop, so we fan-cap it (hub vertex + a tri per rim edge). ONLY edges in
    #    the back band are used, so the front decorated face and its recesses are never touched.
    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    zmax = max(v.co.z for v in bm.verts)
    band = zmax - WT_BACK_BAND
    back_edges = [e for e in bm.edges
                  if e.is_boundary and e.verts[0].co.z > band and e.verts[1].co.z > band]
    if back_edges:
        rim_verts = {v for e in back_edges for v in e.verts}
        centroid = sum((v.co for v in rim_verts), Vector()) / len(rim_verts)
        hub = bm.verts.new(centroid)
        made = 0
        for e in back_edges:
            try:
                bm.faces.new((e.verts[0], e.verts[1], hub)); made += 1
            except ValueError:
                pass
        print(f"watertight: back fan-cap over {len(back_edges)} rim edges ({made} tris)")
    bm.to_mesh(me); bm.free(); me.update()

    # 4) consistent outward normals
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free(); me.update()
    print(f"watertight: boundary edges {before} -> {_bnd()}, verts now {len(me.vertices)}")


argv = sys.argv[sys.argv.index("--")+1:]
src, dst = argv[0], argv[1]

# --- clean scene, import ---
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not meshes:
    raise SystemExit("no mesh in GLB")
# join all mesh parts into one
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
board = bpy.context.view_layer.objects.active
board.name = "PlayTrayBoard"

# apply any import transform (Y-up node rotation etc.) so we work in world space
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# --- orient to the contract: board in XY plane, thin axis = Z ---
if UPRIGHT_FROM_YUP:
    board.rotation_euler[0] = math.radians(-90)   # Y-up -> Z-up: board lies in XY, thickness on Z
    bpy.ops.object.transform_apply(rotation=True)

# --- decimate to target tri count ---
tris = sum(len(p.vertices)-2 for p in board.data.polygons)  # fan estimate
ratio = min(1.0, TARGET_TRIS / max(1, tris))
if ratio < 0.98:
    m = board.modifiers.new("dec", 'DECIMATE'); m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=m.name)

# --- centre pivot, scale to real size ---
bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
board.location = (0, 0, 0)
dims = board.dimensions
longest = max(dims.x, dims.y)
s = TARGET_WIDTH_M / longest
board.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)

# --- put the board BODY at z>0 and the decorated top face toward -Z ---
# after Z-up the top face is +Z; the contract wants it at -Z, so flip 180 about X,
# then push the body up so the min-z (now the back) sits at z>=0.
board.rotation_euler[0] = math.radians(180)
bpy.ops.object.transform_apply(rotation=True)
if FLIP_TOP_FACE:
    board.rotation_euler[1] = math.radians(180)
    bpy.ops.object.transform_apply(rotation=True)
minz = min((board.matrix_world @ v.co).z for v in board.data.vertices)
board.location.z -= minz          # body now spans z: 0 .. thickness (top face at z~0 facing -Z)
bpy.ops.object.transform_apply(location=True)

# --- close the see-through holes (weld shells + fill torn boundaries + cap the hollow back) ---
# Runs AFTER orientation so the back band (max-Z) is the hollow interior; the decorated front (z~0)
# and its recesses are left to gentle holes_fill only. See make_watertight docstring.
if WATERTIGHT:
    make_watertight(board)

# --- downscale textures ---
for img in bpy.data.images:
    if img.size[0] > TEX_MAX or img.size[1] > TEX_MAX:
        img.scale(min(TEX_MAX, img.size[0]), min(TEX_MAX, img.size[1]))

# --- add the six named anchor empties on the card plane (z=0). Per-board layouts measured from
# an offscreen render of each prepped board's decorated (-Z) face (render_board.py). The mod finds
# anchors by NAME anywhere; BuildBoard.cs then projects each onto the real recess surface. The board
# frame is derived as u=Slot1->Slot2 (long axis), s=LongRest->ShortRest (+Y short axis), n=uxs — so
# Slot1 must be the local -X slot and ShortRest.y>LongRest.y on every board, regardless of which X
# side the rest/pad clusters sit on.
ANCHOR_SETS = {
    # board A (original oak) — the historical default layout.
    "oak": {
        "Slot1":         (-0.093, 0.00, 0.0), "Slot2":         ( 0.101, 0.00, 0.0),
        "ShortRestToken":(-0.255, 0.055, 0.0),"LongRestToken": (-0.255,-0.055, 0.0),
        "ConfirmButton": ( 0.255, 0.055, 0.0),"UndoButton":    ( 0.255,-0.055, 0.0),
    },
    # 9capjqp6 (0.64x0.369): 2 central card slots; square button pads on -X; emblem panel on +X (rest).
    "9capjqp6": {
        "Slot1":         (-0.089, 0.000, 0.0),"Slot2":         ( 0.073, 0.000, 0.0),
        "ConfirmButton": (-0.221, 0.037, 0.0),"UndoButton":    (-0.221,-0.051, 0.0),
        "ShortRestToken":( 0.211, 0.080, 0.0),"LongRestToken": ( 0.211,-0.080, 0.0),
    },
    # 16vm268h (0.64x0.218): 2 central parchment slots; stacked pads on -X; round dial recess on +X (rest).
    "16vm268h": {
        "Slot1":         (-0.080, 0.000, 0.0),"Slot2":         ( 0.083, 0.000, 0.0),
        "ConfirmButton": (-0.219, 0.025, 0.0),"UndoButton":    (-0.219,-0.030, 0.0),
        "ShortRestToken":( 0.226, 0.040, 0.0),"LongRestToken": ( 0.226,-0.040, 0.0),
    },
}
board_key = argv[2] if len(argv) > 2 else "oak"
anchors = ANCHOR_SETS.get(board_key, ANCHOR_SETS["oak"])
print("ANCHORSET", board_key, "->", {k: tuple(round(c,3) for c in v) for k, v in anchors.items()})
for name, pos in anchors.items():
    e = bpy.data.objects.new(name, None); e.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(e)
    e.parent = board; e.location = pos

# --- export ---
bpy.ops.object.select_all(action='SELECT')
# GLB (for inspection / glTFast pipelines)
bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', export_apply=True,
                          export_yup=True, use_selection=True)
# FBX (Unity 2021.3 imports this NATIVELY — the companion project has no glTF importer,
# same path the hands use). Unity-friendly axes; textures export next to the .fbx.
fbx = os.path.splitext(dst)[0] + ".fbx"
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         axis_forward='-Z', axis_up='Y', object_types={'MESH', 'EMPTY'},
                         path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE')
print("WROTE", dst, "and", fbx, "| tris~", TARGET_TRIS, "size", tuple(round(x,3) for x in board.dimensions))
