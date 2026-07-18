# render_anchors.py — top-down render of a prepped board with its named anchor empties marked.
# Usage: blender --background --python render_anchors.py -- <prepped.glb> <out.png>
# Reads the six named empties baked into the prepped GLB, drops a bright emissive marker + coloured
# ring at each, and renders the decorated (-Z) face so anchor placement can be eyeballed.
import bpy, sys, math

argv = sys.argv[sys.argv.index("--")+1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
empties = {o.name.split('.')[0]: o for o in bpy.context.scene.objects if o.type == 'EMPTY'}

xs, ys, zs = [], [], []
for o in meshes:
    for v in o.data.vertices:
        w = o.matrix_world @ v.co; xs.append(w.x); ys.append(w.y); zs.append(w.z)
minx, maxx = min(xs), max(xs); miny, maxy = min(ys), max(ys); minz = min(zs)
cx, cy = (minx+maxx)/2, (miny+maxy)/2; w, h = maxx-minx, maxy-miny

COLORS = {  # RGBA emissive
    "Slot1": (0.1, 0.6, 1.0), "Slot2": (0.1, 1.0, 0.5),
    "ShortRestToken": (1.0, 0.8, 0.1), "LongRestToken": (1.0, 0.45, 0.1),
    "ConfirmButton": (0.2, 1.0, 0.2), "UndoButton": (1.0, 0.3, 0.3),
}
for name, e in empties.items():
    col = COLORS.get(name, (1, 1, 1))
    p = e.matrix_world.translation
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.016, location=(p.x, p.y, minz - 0.02))
    s = bpy.context.active_object
    m = bpy.data.materials.new(name+"_m"); m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Emission Color"].default_value = (*col, 1)
    bsdf.inputs["Emission Strength"].default_value = 5.0
    bsdf.inputs["Base Color"].default_value = (*col, 1)
    s.data.materials.append(m)

cam_data = bpy.data.cameras.new("Cam"); cam_data.type = 'ORTHO'
cam_data.ortho_scale = max(w, h) * 1.08
cam = bpy.data.objects.new("Cam", cam_data); bpy.context.scene.collection.objects.link(cam)
cam.location = (cx, cy, minz - 0.6); cam.rotation_euler = (math.radians(180), 0, 0)
bpy.context.scene.camera = cam
sun_data = bpy.data.lights.new("Sun", 'SUN'); sun_data.energy = 3.0
sun = bpy.data.objects.new("Sun", sun_data); bpy.context.scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(180), 0, 0)
world = bpy.data.worlds.new("W"); world.use_nodes = True
world.node_tree.nodes["Background"].inputs[1].default_value = 1.1
bpy.context.scene.world = world
scn = bpy.context.scene
scn.render.engine = 'BLENDER_EEVEE_NEXT'
scn.render.resolution_x = 1024
scn.render.resolution_y = int(1024 * (h / w)) if w > 0 else 1024
scn.render.filepath = out
bpy.ops.render.render(write_still=True)
print("MARKED", list(empties.keys()), "WROTE", out)
