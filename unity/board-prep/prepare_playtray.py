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

# --- downscale textures ---
for img in bpy.data.images:
    if img.size[0] > TEX_MAX or img.size[1] > TEX_MAX:
        img.scale(min(TEX_MAX, img.size[0]), min(TEX_MAX, img.size[1]))

# --- add the six named anchor empties on the card plane (z=0), positioned for a 0.64 x 0.32 board.
# Coordinates are the mod's expected layout (slots centre, rest LEFT, confirm/undo RIGHT). Nudge in
# Blender/Unity so each sits over the matching painted recess — the mod finds them by NAME anywhere.
anchors = {
    "Slot1":         (-0.085, 0.00, 0.0),   # left card slot (initiative slot)
    "Slot2":         ( 0.085, 0.00, 0.0),   # right card slot
    "ShortRestToken":(-0.255, 0.055, 0.0),  # left rest zone, upper pad
    "LongRestToken": (-0.255,-0.055, 0.0),  # left rest zone, lower pad
    "ConfirmButton": ( 0.255, 0.055, 0.0),  # right button pad, upper
    "UndoButton":    ( 0.255,-0.055, 0.0),  # right button pad, lower
}
for name, pos in anchors.items():
    e = bpy.data.objects.new(name, None); e.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(e)
    e.parent = board; e.location = pos

# --- export ---
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', export_apply=True,
                          export_yup=True, use_selection=True)
print("WROTE", dst, "tris~", TARGET_TRIS, "size", tuple(round(x,3) for x in board.dimensions))
