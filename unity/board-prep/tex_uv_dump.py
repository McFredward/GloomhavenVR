"""tex_uv_dump.py -- Blender-side helper for tex_seams.py.

Dumps a mesh's triangulated UV coordinates so the seam checker can rasterise the
REAL islands instead of assuming the intended layout.

    blender --background --factory-startup --python tex_uv_dump.py -- <fbx> <out.npz>
"""

import sys

import bpy
import bmesh
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith(".fbx"):
    bpy.ops.import_scene.fbx(filepath=src)
elif src.lower().endswith(".obj"):
    bpy.ops.wm.obj_import(filepath=src)
else:
    bpy.ops.import_scene.gltf(filepath=src)

tris = []
for ob in bpy.data.objects:
    if ob.type != "MESH" or not ob.data.uv_layers:
        continue
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    uv = bm.loops.layers.uv.active
    for f in bm.faces:
        tris.append([[l[uv].uv.x, l[uv].uv.y] for l in f.loops])
    bm.free()

arr = np.asarray(tris, dtype=np.float64)
np.savez_compressed(dst, tris=arr)
print(f"UVDUMP {dst} tris={len(arr)} "
      f"u=[{arr[..., 0].min():.4f},{arr[..., 0].max():.4f}] "
      f"v=[{arr[..., 1].min():.4f},{arr[..., 1].max():.4f}]")
