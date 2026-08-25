# render_fbx.py -- lit three-quarter + front render of a board FBX with explicit textures.
# blender --background --factory-startup --python render_fbx.py -- <fbx> <albedo> <normal|-> <out.png> [mode]
import bpy, sys, math, os
from mathutils import Vector

a = sys.argv[sys.argv.index("--")+1:]
fbx, alb, nrm, out = a[0], a[1], a[2], a[3]
mode = a[4] if len(a) > 4 else "front"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']

# world bounds
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (mn + mx) / 2
size = mx - mn
# thin axis = the board's normal
ext = [size.x, size.y, size.z]
thin = ext.index(min(ext))
big = max(ext)

mat = bpy.data.materials.new("BoardPreview"); mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
def tex(path, cs, sock, noncolor=False):
    if path == "-" or not os.path.exists(path): return None
    img = bpy.data.images.load(path)
    if noncolor: img.colorspace_settings.name = 'Non-Color'
    n = mat.node_tree.nodes.new("ShaderNodeTexImage"); n.image = img
    return n
na = tex(alb, 'sRGB', None)
if na: mat.node_tree.links.new(bsdf.inputs["Base Color"], na.outputs["Color"])
nn = tex(nrm, 'Non-Color', None, noncolor=True)
if nn:
    nm = mat.node_tree.nodes.new("ShaderNodeNormalMap")
    mat.node_tree.links.new(nm.inputs["Color"], nn.outputs["Color"])
    mat.node_tree.links.new(bsdf.inputs["Normal"], nm.outputs["Normal"])
bsdf.inputs["Roughness"].default_value = 0.55
for o in meshes:
    o.data.materials.clear(); o.data.materials.append(mat)

# camera
cam_d = bpy.data.cameras.new("C"); cam_d.type = 'ORTHO'; cam_d.ortho_scale = big * 1.12
cam = bpy.data.objects.new("C", cam_d); bpy.context.scene.collection.objects.link(cam)
axis = [Vector((1,0,0)), Vector((0,1,0)), Vector((0,0,1))][thin]
d = big * 2.0
other = [Vector((1,0,0)), Vector((0,1,0)), Vector((0,0,1))]
o1 = other[(thin+1) % 3]; o2 = other[(thin+2) % 3]
# The two long axes, longest first, so "edge" always looks along the short one.
lng, srt = (o1, o2) if ext[(thin+1) % 3] >= ext[(thin+2) % 3] else (o2, o1)
if mode == "front":
    loc = ctr + axis * d
    # aim back at centre
elif mode == "back":
    # BACK and EDGE exist because a front-only station cannot show a defect on the
    # faces it never points at, and one shipped: the back plate and the rim bevel had
    # no authored material at all until ModBuild 275.
    loc = ctr - axis * d
elif mode == "backquarter":
    loc = ctr - axis*d*0.85 + lng*d*0.30 + srt*d*0.42
    cam_d.ortho_scale = big * 1.25
elif mode == "edge":
    # Grazing along the board's own short axis, so the rim fills the frame. A 35 mm
    # band on a 640 mm board is a few pixels in any other shot.
    loc = ctr - srt*d - axis*d*0.22
    cam_d.ortho_scale = big * 1.06
else:  # three-quarter
    loc = ctr + axis*d*0.85 + o1*d*0.30 + o2*d*0.42
    cam_d.ortho_scale = big * 1.25
cam.location = loc
dirv = (ctr - loc).normalized()
cam.rotation_euler = dirv.to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam

# three-point light
def key(l, e, sz):
    ld = bpy.data.lights.new("L", 'AREA'); ld.energy = e; ld.size = sz
    ob = bpy.data.objects.new("L", ld); bpy.context.scene.collection.objects.link(ob)
    ob.location = l
    ob.rotation_euler = (ctr - Vector(l)).normalized().to_track_quat('-Z','Y').to_euler()
key((ctr + axis*d*0.9 + Vector((0.5,0.5,0.5))*d*0.5)[:], 260, big*1.4)
key((ctr + axis*d*0.8 - Vector((0.7,0.2,0.4))*d*0.6)[:], 120, big*1.6)
key((ctr - axis*d*0.4 + Vector((0.1,0.9,0.2))*d*0.7)[:], 90, big*1.2)
w = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.05,0.05,0.06,1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.6

sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x = 1400; sc.render.resolution_y = 900
sc.render.film_transparent = False
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("WROTE", out)
