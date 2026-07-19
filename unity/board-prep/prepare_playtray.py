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

import bpy, sys, math, os

# ---- flags you may need to flip after a visual check (open the output in Blender) ----
TARGET_WIDTH_M   = 0.64     # longest footprint edge -> metres (contract: 0.64 x ~0.32)
TARGET_TRIS      = 20000    # decimation target (VR: a static prop; 15-25k is plenty)
FLIP_TOP_FACE    = False    # set True if the DECORATED face ends up pointing +Z instead of -Z
UPRIGHT_FROM_YUP = True     # most GLBs export Y-up; True rotates -90deg X so the board lies in XY
TEX_MAX          = 2048     # downscale every image to this max edge (4K -> 2K)
# NOTE — DO NOT re-add a geometry "watertight"/weld pass here. An earlier round ported
# make_watertight() (global shell weld + iterative holes_fill + a fan-cap of the hollow max-Z BACK
# band) into this prep and applied it to Steel (9capjqp6) + Bronze (16vm268h). The back fan-cap
# produced flipped/degenerate faces that rendered as large BLACK stripes in-headset under BoardLit
# (inward normals) — the Steel board came out "völlig ruiniert". It was REVERTED. The mesh holes are
# handled purely at RENDER time by two-sided rendering: BuildBoard.cs sets the BoardLit material
# _Cull=0 for every board (BoardLit's VFACE path lights the backface), which fills every backface
# "hole" with the wall behind it — the exact approach that fixed Oak, with ZERO geometry risk.
# Prefer a minor see-through over ANY black stripe; never weld/cap the geometry.


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

# NOTE: no geometry hole-closing pass here on purpose (see the top-of-file note). Mesh backface
# "holes" are handled at render time via BoardLit _Cull=0 (two-sided) in BuildBoard.cs, exactly like
# Oak — no shell weld, no back fan-cap (that produced black stripes and was reverted).

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
