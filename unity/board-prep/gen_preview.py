#!/usr/bin/env python3
"""gen_preview.py -- geometry-only preview of a board FBX.

`gen_render.py` is the contract's shared render tool and stays as it is; this one exists
because a white material under a three-point rig blows the relief out completely -- the
first render of the re-authored oak board showed a flat white slab with hairline outlines
and told me nothing about whether the recesses read.  Here the board is neutral grey, the
key light RAKES across it at a low angle so every recess and every raised ornament throws
a real shadow, and soft shadows are on.

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_preview.py -- <fbx> <out.png> [front|quarter|grazing]
"""

import bpy
import math
import os
import sys

from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
fbx, out = a[0], a[1]
mode = a[2] if len(a) > 2 else "front"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']

pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (mn + mx) / 2
size = mx - mn
ext = [size.x, size.y, size.z]
thin = ext.index(min(ext))
big = max(ext)
axis = [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))][thin]
o1 = [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))][(thin + 1) % 3]
o2 = [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))][(thin + 2) % 3]

mat = bpy.data.materials.new("Grey")
mat.use_nodes = True
b = mat.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.52, 0.50, 0.47, 1.0)
b.inputs["Roughness"].default_value = 0.62
b.inputs["Metallic"].default_value = 0.0
for o in meshes:
    o.data.materials.clear()
    o.data.materials.append(mat)

cam_d = bpy.data.cameras.new("C")
cam_d.type = 'ORTHO'
cam_d.ortho_scale = big * 1.06
cam = bpy.data.objects.new("C", cam_d)
bpy.context.scene.collection.objects.link(cam)
d = big * 2.0
if mode == "front":
    loc = ctr + axis * d
elif mode == "grazing":
    loc = ctr + axis * d * 0.34 + o2 * d * 0.55
    cam_d.ortho_scale = big * 0.62
    ctr = ctr + o2 * big * 0.20
else:
    loc = ctr + axis * d * 0.80 + o1 * d * 0.26 + o2 * d * 0.40
    cam_d.ortho_scale = big * 1.18
cam.location = loc
cam.rotation_euler = (ctr - loc).normalized().to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam


def lamp(loc, energy, size_, kind='AREA'):
    ld = bpy.data.lights.new("L", kind)
    ld.energy = energy
    if kind == 'AREA':
        ld.size = size_
    ob = bpy.data.objects.new("L", ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (ctr - Vector(loc)).normalized().to_track_quat('-Z', 'Y').to_euler()


# KEY: low and off to one side so every 2-6 mm recess casts a shadow across its own floor.
lamp((ctr + axis * d * 0.30 - o1 * d * 0.62 + o2 * d * 0.30)[:], 34, big * 0.22)
# fill from the opposite side, weak, so the shadows stay readable
lamp((ctr + axis * d * 0.55 + o1 * d * 0.45 - o2 * d * 0.25)[:], 9, big * 1.1)

w = bpy.data.worlds.new("W")
bpy.context.scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.06, 0.065, 0.075, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.10

sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE_NEXT'
try:
    sc.eevee.use_raytracing = True
    sc.eevee.use_shadows = True
    sc.eevee.use_shadow_jitter_viewport = True
    sc.eevee.taa_render_samples = 96
except Exception:
    pass
sc.view_settings.look = 'AgX - Medium High Contrast'
sc.render.resolution_x = 1500
sc.render.resolution_y = 950
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("WROTE", out)
