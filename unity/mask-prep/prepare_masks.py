# prepare_masks.py — Blender headless prep: a raw Hunyuan3D head "mask" GLB -> a
# bundle-ready, low-poly, unlit-textured head-avatar FBX for the VR embodiment.
#
# Contract (the Unity BuildHeads.cs + runtime worker code against this):
#   - <= ~4k triangles (decimated hard from the ~55 MB Hunyuan mesh)
#   - PIVOT / origin at the EYE MIDPOINT (front-center, upper-middle of the head)
#   - orientation: face -> +Z, up -> +Y in UNITY. We author in Blender as up=+Z,
#     face=-Y (the canonical Hunyuan output); the FBX export axis conversion
#     (axis_up='Y', axis_forward='-Z', bake_space_transform=True) then maps
#     Blender +Z -> Unity +Y and Blender -Y -> Unity +Z, so the face lands on +Z.
#   - real-world scale: ~0.22 m tall (overall bounding height) at scale 1; the
#     receiver multiplies by worldScale, so do NOT pre-bake the diorama scale.
#   - albedo texture retained + written as a loose PNG next to the FBX (BuildHeads
#     builds an unlit material on it, mirroring the hand/board loose-texture pattern).
#
# All three Hunyuan masks probed identical (probe_masks.py): up=+Z, face=-Y in
# Blender. So the per-mask pre-rotation is identity. If a future input differs,
# add its Euler correction to PRE_ROT_EULER below (degrees, applied before export).
#
# RUN:
#   blender --background --python prepare_masks.py -- <src.glb> <out_dir> <index> [render]
#     <out_dir>  e.g. unity/GloomhavenVR.Assets/Assets/Bundle/Head
#     <index>    0/1/2 -> writes Mask_<index>.fbx + Mask_<index>_albedo.png
#     render     optional: also write front + 3/4 verification PNGs to out/

import bpy, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--")+1:]
src      = argv[0]
out_dir  = argv[1]
index    = int(argv[2])
do_render = len(argv) > 3 and argv[3] == "render"

TARGET_TRIS   = 4000     # VR floating head at table/mirror distance; hard budget
TARGET_HEIGHT = 0.22     # metres, overall bounding height (contract 0.20-0.24)
TEX_MAX       = 2048     # albedo downscale cap
# Per-mask Blender pre-rotation (degrees) to reach canonical up=+Z, face=-Y.
# All three current Hunyuan outputs are already canonical -> identity.
PRE_ROT_EULER = {0: (0, 0, 0), 1: (0, 0, 0), 2: (0, 0, 0)}

name = f"Mask_{index}"
os.makedirs(out_dir, exist_ok=True)

# --- clean scene, import ---
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not meshes:
    raise SystemExit("no mesh in GLB")
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.name = name
# apply the glTF import (Y-up) transform so we work in a clean Blender Z-up frame
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# --- per-mask orientation correction (identity for all current inputs) ---
rx, ry, rz = PRE_ROT_EULER.get(index, (0, 0, 0))
if (rx, ry, rz) != (0, 0, 0):
    obj.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
    bpy.ops.object.transform_apply(rotation=True)

# --- decimate hard to the tri budget ---
tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)  # fan estimate
ratio = min(1.0, TARGET_TRIS / max(1, tris))
if ratio < 0.999:
    m = obj.modifiers.new("dec", 'DECIMATE')
    m.decimate_type = 'COLLAPSE'   # preserves UVs / texture mapping
    m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=m.name)
final_tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)

# --- world bounds (canonical: X=width, Y=depth[front=-Y], Z=height[up=+Z]) ---
xs = []; ys = []; zs = []
for v in obj.data.vertices:
    w = obj.matrix_world @ v.co
    xs.append(w.x); ys.append(w.y); zs.append(w.z)
minb = Vector((min(xs), min(ys), min(zs)))
maxb = Vector((max(xs), max(ys), max(zs)))
dims = maxb - minb

# --- EYE MIDPOINT (approximate): X centered, front-recessed in -Y, upper-middle in Z ---
eye = Vector((
    (minb.x + maxb.x) * 0.5,          # symmetric
    minb.y + 0.25 * dims.y,           # toward the front (-Y), recessed from the nose/visor
    minb.z + 0.55 * dims.z,           # upper-middle of the head
))
# put the object origin at the eye midpoint, then move the object so that origin sits
# at the world origin (this becomes the prefab pivot after export)
bpy.context.scene.cursor.location = eye
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
obj.location = (0, 0, 0)
bpy.ops.object.transform_apply(location=True)

# --- scale so overall height (Z) = TARGET_HEIGHT (scales about the eye-point origin) ---
s = TARGET_HEIGHT / dims.z
obj.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)

# --- downscale the albedo texture ---
for img in bpy.data.images:
    if img.size[0] > TEX_MAX or img.size[1] > TEX_MAX:
        img.scale(min(TEX_MAX, img.size[0]), min(TEX_MAX, img.size[1]))

# --- write the albedo as a loose PNG next to the FBX (BuildHeads references it) ---
# Pick the image feeding Base Color; fall back to the largest image.
albedo_img = None
if obj.data.materials:
    for mat in obj.data.materials:
        if not mat or not mat.use_nodes:
            continue
        for n in mat.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                bc = n.inputs.get('Base Color')
                if bc and bc.is_linked:
                    src_node = bc.links[0].from_node
                    if src_node.type == 'TEX_IMAGE' and src_node.image:
                        albedo_img = src_node.image
        if albedo_img:
            break
if albedo_img is None:
    imgs = [im for im in bpy.data.images if im.size[0] > 0]
    if imgs:
        albedo_img = max(imgs, key=lambda im: im.size[0] * im.size[1])

albedo_path = os.path.join(out_dir, f"{name}_albedo.png")
if albedo_img is not None:
    albedo_img.filepath_raw = albedo_path
    albedo_img.file_format = 'PNG'
    albedo_img.save()
    print("WROTE ALBEDO", albedo_path, "size", tuple(albedo_img.size))
else:
    print("WARNING: no albedo image found in", src)

# --- export FBX (Unity 2021.3 imports natively; textures embedded + loose PNG above) ---
fbx = os.path.join(out_dir, f"{name}.fbx")
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=True, axis_forward='-Z', axis_up='Y',
    object_types={'MESH'}, path_mode='STRIP', embed_textures=False,
    mesh_smooth_type='FACE')
# NOTE: textures are NOT embedded in the FBX. Unity imports the model with
# materialImportMode=None and binds the loose Mask_<n>_albedo.png (written above)
# on the unlit HeadUnlit shader — exactly the hand/board loose-texture pattern.
# Embedding would just bloat the committed FBX by ~25 MB with a redundant copy.

print(f"WROTE {fbx} | tris {final_tris} | dims(blender xyz)="
      f"{tuple(round(c,3) for c in obj.dimensions)} | eye_local=0,0,0")

# --- optional verification renders (canonical Blender frame) ---
if do_render:
    out_render = os.path.join(os.path.dirname(os.path.dirname(fbx)), "")  # unused
    render_dir = argv[4] if len(argv) > 4 else "/tmp"
    os.makedirs(render_dir, exist_ok=True)

    world = bpy.data.worlds.new("W"); world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.5
    bpy.context.scene.world = world
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x = 720; scn.render.resolution_y = 720
    scn.render.film_transparent = False

    cam_data = bpy.data.cameras.new("Cam"); cam_data.lens = 50
    cam = bpy.data.objects.new("Cam", cam_data)
    scn.collection.objects.link(cam); scn.camera = cam
    sun_data = bpy.data.lights.new("Sun", 'SUN'); sun_data.energy = 2.0
    sun = bpy.data.objects.new("Sun", sun_data); scn.collection.objects.link(sun)

    d = max(obj.dimensions) * 2.2
    ctr = Vector((0, 0, 0))  # eye at origin; frame around head center
    hc = Vector((0, (minb.y + maxb.y) * 0.5 * s - 0, 0))  # not used

    def look_at(camobj, frm, to):
        import mathutils
        direction = (to - frm)
        camobj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

    # front: camera on -Y (the face side), looking toward +Y
    shots = {
        "front": Vector((0, -d, 0.02)),
        "threeq": Vector((-d * 0.7, -d * 0.75, d * 0.35)),
    }
    head_center = Vector((0, 0, 0))
    for shot, pos in shots.items():
        cam.location = pos
        look_at(cam, pos, head_center)
        sun.location = pos + Vector((0, 0, d))
        look_at(sun, sun.location, head_center)
        scn.render.filepath = os.path.join(render_dir, f"{name}_{shot}.png")
        bpy.ops.render.render(write_still=True)
        print("WROTE RENDER", scn.render.filepath)
