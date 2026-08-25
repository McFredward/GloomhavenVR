# gen_backshot.py -- a BACK-face station with a controlled raking light, for A/B pictures.
#
#   blender --background --factory-startup --python gen_backshot.py -- \
#       <fbx> <albedo> <normal|-> <out.png> [flat|rake] [elev_deg] [azim_deg]
#
# WHY A NEW STATION.  gen_render.py's three lights are placed relative to the board's FRONT
# axis, so in its `back` mode the key sits behind the board and the back plate is lit by one
# 90-energy fill.  That is fine for "is anything painted here", which is the question
# ModBuild 275 asked it, and useless for "does this relief catch the light", which is the
# question this round asks.  Relief is a directional effect: judged under a light that does
# not rake it, geometry and a normal map and a flat plate all look the same.
#
# ONE SUN, at a stated elevation above the back plane, plus a dim ambient so the shadow side
# is not black.  A sun rather than an area light because its direction is the same at every
# point of a 640 mm board, which is what makes two renders comparable: an area light 0.6 m
# from a 0.64 m board rakes the middle and front-lights the corners.
#
# WHAT THIS STATION IS NOT.  It is a Principled BSDF, not `BoardLit`, so it is an ITERATION
# LOOP and an A/B, never a verdict.  Its light is also deliberately harsher than the game's
# baked key.  The only calibrated picture of what ships comes out of
# Assets/Editor/PreviewBoard.cs against the built bundle.  Both are used this round and they
# answer different questions: this one shows what CHANGED between two meshes under identical
# light, that one shows what the player will actually see.
import bpy, sys, math, os
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
fbx, alb, nrm, out = a[0], a[1], a[2], a[3]
mode = a[4] if len(a) > 4 else "rake"
elev = float(a[5]) if len(a) > 5 else (11.0 if mode == "rake" else 55.0)
azim = float(a[6]) if len(a) > 6 else 20.0

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in bpy.context.scene.objects:
    if o.type == 'EMPTY':
        bpy.data.objects.remove(o, do_unlink=True)

pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (mn + mx) / 2
size = mx - mn
ext = [size.x, size.y, size.z]
thin = ext.index(min(ext))
big = max(ext)
basis = [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))]
axis = basis[thin]
o1, o2 = basis[(thin + 1) % 3], basis[(thin + 2) % 3]
lng, srt = (o1, o2) if ext[(thin + 1) % 3] >= ext[(thin + 2) % 3] else (o2, o1)

# THE BACK is the -axis end.  Taken from the geometry rather than assumed: the decorated
# face carries the frame ornaments, so it is the end with the larger extent from the centre.
front_span = max((p - ctr).dot(axis) for p in pts)
back_span = -min((p - ctr).dot(axis) for p in pts)
back = -axis if front_span >= back_span else axis

mat = bpy.data.materials.new("BackShot")
mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
if os.path.exists(alb):
    img = bpy.data.images.load(alb)
    n = mat.node_tree.nodes.new("ShaderNodeTexImage")
    n.image = img
    mat.node_tree.links.new(bsdf.inputs["Base Color"], n.outputs["Color"])
if nrm != "-" and os.path.exists(nrm):
    img = bpy.data.images.load(nrm)
    img.colorspace_settings.name = 'Non-Color'
    n = mat.node_tree.nodes.new("ShaderNodeTexImage")
    n.image = img
    nm = mat.node_tree.nodes.new("ShaderNodeNormalMap")
    mat.node_tree.links.new(nm.inputs["Color"], n.outputs["Color"])
    mat.node_tree.links.new(bsdf.inputs["Normal"], nm.outputs["Normal"])
bsdf.inputs["Roughness"].default_value = 0.55
for o in meshes:
    o.data.materials.clear()
    o.data.materials.append(mat)

cam_d = bpy.data.cameras.new("C")
cam_d.type = 'ORTHO'
cam_d.ortho_scale = big * 1.06
cam = bpy.data.objects.new("C", cam_d)
bpy.context.scene.collection.objects.link(cam)
d = big * 2.0
if mode == "rake":
    loc = ctr + back * d * 0.94 + lng * d * 0.16 + srt * d * 0.28
    cam_d.ortho_scale = big * 1.46
elif mode == "edge":
    # Grazing along the board's SHORT axis so the 30-40 mm rim fills the frame, tipped a
    # little toward the back so the back relief's own silhouette breaks the edge line.
    loc = ctr + srt * d + back * d * 0.30
    cam_d.ortho_scale = big * 1.12
else:
    loc = ctr + back * d
cam.location = loc
cam.rotation_euler = (ctr - loc).normalized().to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam

# ONE SUN at (elev, azim) measured in the board's own back-facing frame.
e, z = math.radians(elev), math.radians(azim)
ldir = (back * math.sin(e) + lng * math.cos(e) * math.cos(z) + srt * math.cos(e) * math.sin(z))
ld = bpy.data.lights.new("SUN", 'SUN')
ld.energy = 4.2
ld.angle = math.radians(1.5)          # a hard-ish sun: soft shadows hide small relief
sun = bpy.data.objects.new("SUN", ld)
bpy.context.scene.collection.objects.link(sun)
sun.location = ctr + ldir * d
sun.rotation_euler = (-ldir).to_track_quat('-Z', 'Y').to_euler()

w = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.38, 0.44, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.22

sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x = 1600
sc.render.resolution_y = 900
sc.render.filepath = out
sc.view_settings.view_transform = 'Standard'      # no filmic: an A/B needs a linear ladder
bpy.ops.render.render(write_still=True)
print("BACKSHOT %s mode=%s elev=%.1f azim=%.1f -> %s" % (os.path.basename(fbx), mode, elev, azim, out))
