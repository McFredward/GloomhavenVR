# probe_masks.py — import a Hunyuan head GLB, decimate lightly, render 6 axis views
# so we can identify which way the FACE points and which way is UP (Blender coords).
# Usage: blender --background --python probe_masks.py -- <src.glb> <out_prefix>
# Produces <out_prefix>_{posX,negX,posY,negY,posZ,negZ}.png (camera sits on that axis,
# looking toward the origin) + prints world bounds/dims.
import bpy, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--")+1:]
src, prefix = argv[0], argv[1]

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
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# light decimate just to speed the render
tris = sum(len(p.vertices)-2 for p in obj.data.polygons)
if tris > 30000:
    m = obj.modifiers.new("dec", 'DECIMATE'); m.ratio = 30000.0/tris
    bpy.ops.object.modifier_apply(modifier=m.name)

# center at origin
bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
obj.location = (0,0,0)
bpy.ops.object.transform_apply(location=True)

# world bounds
xs=[]; ys=[]; zs=[]
for v in obj.data.vertices:
    w = obj.matrix_world @ v.co
    xs.append(w.x); ys.append(w.y); zs.append(w.z)
minb = Vector((min(xs),min(ys),min(zs))); maxb = Vector((max(xs),max(ys),max(zs)))
dims = maxb - minb
ctr = (minb+maxb)/2
R = max(dims) * 1.1
print(f"PROBE bounds min={tuple(round(c,3) for c in minb)} max={tuple(round(c,3) for c in maxb)} dims={tuple(round(c,3) for c in dims)}")

# world lighting (bright ambient so texture reads regardless of normals)
world = bpy.data.worlds.new("W"); world.use_nodes = True
world.node_tree.nodes["Background"].inputs[1].default_value = 1.6
bpy.context.scene.world = world

scn = bpy.context.scene
scn.render.engine = 'BLENDER_EEVEE_NEXT'
scn.render.resolution_x = 640; scn.render.resolution_y = 640
scn.render.film_transparent = False

cam_data = bpy.data.cameras.new("Cam"); cam_data.type = 'ORTHO'
cam_data.ortho_scale = R
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam

# a sun that follows the camera
sun_data = bpy.data.lights.new("Sun", 'SUN'); sun_data.energy = 2.5
sun = bpy.data.objects.new("Sun", sun_data)
bpy.context.scene.collection.objects.link(sun)

dist = max(dims)*2.5
views = {
    "posX": (Vector(( dist,0,0)), (math.radians(90), 0, math.radians(90))),
    "negX": (Vector((-dist,0,0)), (math.radians(90), 0, math.radians(-90))),
    "posY": (Vector((0, dist,0)), (math.radians(90), 0, math.radians(180))),
    "negY": (Vector((0,-dist,0)), (math.radians(90), 0, 0)),
    "posZ": (Vector((0,0, dist)), (0, 0, 0)),
    "negZ": (Vector((0,0,-dist)), (math.radians(180), 0, 0)),
}
for name,(pos,rot) in views.items():
    cam.location = ctr + pos
    cam.rotation_euler = rot
    sun.location = ctr + pos
    # point sun roughly toward origin along -pos
    sun.rotation_euler = rot
    scn.render.filepath = f"{prefix}_{name}.png"
    bpy.ops.render.render(write_still=True)
    print("WROTE", scn.render.filepath)
