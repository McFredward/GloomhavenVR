# render_obj.py — Blender headless render of a POSED hand OBJ produced by fbx_skin.py.
#
# The verification path deliberately never imports the rigged FBX into Blender: the posed
# vertices/normals are computed from the raw FBX by fbx_skin.py, so nothing Blender does to
# bone rolls or custom normals can flatter or fake the result.
#
# Coordinates are the FBX ones: +Y along the fingers, +Z out of the palm, -Z = back of hand.
# Views: back (the user's screenshot angle), palm, tips (down the fingers — the view that
# shows SPLAY), side (thumb-side profile — shows curl depth).
#
# RUN:
#   /home/claw/blender-4.2/blender --background --python render_obj.py -- \
#       <in.obj> <albedo.png> <outdir> <tag> [--views back,palm,tips,side] [--res 900]
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC, TEX, OUTDIR, TAG = pos[0], pos[1], pos[2], pos[3]


def opt(n, d):
    return argv[argv.index(n) + 1] if n in argv else d


VIEWS = opt("--views", "back,palm,tips,side").split(",")
RES = int(opt("--res", "900"))
os.makedirs(OUTDIR, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=SRC, forward_axis='Y', up_axis='Z')
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
ob.matrix_world.identity()

mat = bpy.data.materials.new("hand")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 0.55
if os.path.exists(TEX):
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(TEX)
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
mat.use_backface_culling = False
ob.data.materials.clear()
ob.data.materials.append(mat)

# lights: key + fill + rim, so silhouette and depth both read
for loc, energy in (((0.35, -0.2, 0.45), 260.0), ((-0.4, -0.1, 0.3), 120.0),
                    ((0.0, 0.5, -0.4), 160.0)):
    lt = bpy.data.lights.new("l", 'POINT')
    lt.energy = energy
    lo = bpy.data.objects.new("l", lt)
    lo.location = Vector(loc) + Vector((0, 0.13, 0))
    bpy.context.collection.objects.link(lo)

world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.06, 0.07, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
bpy.context.scene.world = world

pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr, size = (lo + hi) * 0.5, (hi - lo)
rad = max(size) * 0.75

cam_d = bpy.data.cameras.new("c")
cam = bpy.data.objects.new("c", cam_d)
bpy.context.collection.objects.link(cam)
bpy.context.scene.camera = cam

DIRS = {
    # name: (direction the camera sits in, up vector)
    "back": (Vector((0, 0, -1)), Vector((0, 1, 0))),
    "palm": (Vector((0, 0, 1)), Vector((0, 1, 0))),
    "tips": (Vector((0, 1, 0.12)).normalized(), Vector((0, 0, 1))),
    "side": (Vector((1, 0, 0)), Vector((0, 1, 0))),
    "backq": (Vector((0.6, -0.25, -0.75)).normalized(), Vector((0, 1, 0))),
}

scn = bpy.context.scene
scn.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in \
    [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
scn.render.resolution_x = scn.render.resolution_y = RES
scn.render.film_transparent = False

for v in VIEWS:
    d, up = DIRS[v]
    dist = rad * 3.2
    loc = ctr + d * dist
    cam.location = loc
    z = (loc - ctr).normalized()
    x = up.cross(z).normalized()
    y = z.cross(x)
    cam.matrix_world = __import__("mathutils").Matrix((
        (x.x, y.x, z.x, loc.x), (x.y, y.y, z.y, loc.y), (x.z, y.z, z.z, loc.z), (0, 0, 0, 1)))
    cam_d.lens = 55
    scn.render.filepath = os.path.join(OUTDIR, f"{TAG}_{v}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[render] {scn.render.filepath}")
