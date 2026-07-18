# render_board.py — headless top-down render of a prepped board's decorated (-Z) face.
# Usage: blender --background --python render_board.py -- <prepped.glb> <out.png>
# The prepped board lies in XY, thin axis Z, decorated top face toward -Z, body z in [0, thickness].
# So we look from -Z toward +Z at the decorated face, orthographic, framing the footprint.
import bpy, sys, math

argv = sys.argv[sys.argv.index("--")+1:]
src, out = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
# world bounds
xs, ys, zs = [], [], []
for o in meshes:
    for v in o.data.vertices:
        w = o.matrix_world @ v.co
        xs.append(w.x); ys.append(w.y); zs.append(w.z)
minx, maxx = min(xs), max(xs); miny, maxy = min(ys), max(ys); minz, maxz = min(zs), max(zs)
cx, cy = (minx+maxx)/2, (miny+maxy)/2
w, h = maxx-minx, maxy-miny

# camera: orthographic, at -Z looking toward +Z (at the decorated face)
cam_data = bpy.data.cameras.new("Cam"); cam_data.type = 'ORTHO'
cam_data.ortho_scale = max(w, h) * 1.08
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
cam.location = (cx, cy, minz - 0.5)
cam.rotation_euler = (0, 0, 0)   # looks down -Z by default; we want to look +Z, so rotate 180 about X
cam.rotation_euler = (math.radians(180), 0, 0)
bpy.context.scene.camera = cam

# lights: a sun from the viewer side
sun_data = bpy.data.lights.new("Sun", 'SUN'); sun_data.energy = 3.0
sun = bpy.data.objects.new("Sun", sun_data)
bpy.context.scene.collection.objects.link(sun)
sun.location = (cx, cy, minz - 1.0); sun.rotation_euler = (math.radians(180), 0, 0)
# fill ambient via world
world = bpy.data.worlds.new("W"); world.use_nodes = True
world.node_tree.nodes["Background"].inputs[1].default_value = 1.2
bpy.context.scene.world = world

scn = bpy.context.scene
scn.render.engine = 'BLENDER_EEVEE_NEXT'
scn.render.resolution_x = 1024
scn.render.resolution_y = int(1024 * (h / w)) if w > 0 else 1024
scn.render.film_transparent = False
scn.render.filepath = out
bpy.ops.render.render(write_still=True)
# print a coordinate legend so anchor positions can be read off the image
print(f"LEGEND board footprint local X:[{minx:.3f},{maxx:.3f}] Y:[{miny:.3f},{maxy:.3f}] "
      f"center=({cx:.3f},{cy:.3f}) size=({w:.3f},{h:.3f}) | image left=+X? camera looks +Z, "
      f"so on the image: local +X is LEFT, local +Y is UP (mirror on X).")
print("WROTE", out)
