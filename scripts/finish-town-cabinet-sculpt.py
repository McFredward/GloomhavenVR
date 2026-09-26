#!/usr/bin/env python3
"""Retopologize the empty multiview cabinet around existing gameplay anchors.

The provider model supplies one cohesive carved and textured carcass. Its short
display legs are removed: the station already has metre-height folding trestles.
The original item cards, rolling cassette, six pictogram buttons, crank and
native lantern remain distinct moving objects in the Unity prefab.
"""
import argparse
import json
import sys
from pathlib import Path

import bmesh
import bpy

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--texture", type=Path, required=True)
parser.add_argument("--mask-source", type=Path, required=True)
parser.add_argument("--normal", type=Path, required=True)
parser.add_argument("--report", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(args.source.resolve()))
objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if len(objects) != 1:
    raise RuntimeError(f"Expected one multiview provider mesh, got {len(objects)}")
obj = objects[0]
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mesh = obj.data
source_triangles = len(mesh.polygons)

# Cut the AI model at the timber base. Real metre-scale folding trestles and
# terrain-contact shoes remain in merchant.fbx; stretching short AI legs would
# create ugly, texture-stretched stilts.
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                      plane_co=(0, 0, .176), plane_no=(0, 0, 1), dist=.00001,
                      clear_inner=True, clear_outer=False)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(mesh)
bm.free()
mesh.update()

# Hunyuan front is -Y. The authored Blender furniture faces +Y after conversion
# from the Unity station frame. Cabinet front is 20 mm ahead of the moving cards.
# Native button centres are y=.84 and x=-1.25..-.65 in the station frame.
for vertex in mesh.vertices:
    x,y,z = vertex.co
    vertex.co = (.95 - x*1.185, -.253 - y*1.28,
                 .775 + (z-.176)*1.345)

modifier = obj.modifiers.new("Bounded VR cabinet surface", "DECIMATE")
modifier.ratio = min(1.0, 42000 / max(1, len(mesh.polygons)))
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.modifier_apply(modifier=modifier.name)
obj.name = "merchant_cabinet_sculpt_CabinetSculpt"
mesh.name = obj.name
mesh.materials[0].name = "CabinetSculpt"

images = {
    "texture_pbr_20250901": args.texture,
    "texture_pbr_20250901_metallic-texture_pbr_20250901_roughness": args.mask_source,
    "texture_pbr_20250901_normal": args.normal,
}
for name, destination in images.items():
    source = bpy.data.images.get(name)
    if source is None:
        raise RuntimeError(f"Provider PBR map is missing: {name}")
    source.scale(2048, 2048)
    destination.parent.mkdir(parents=True, exist_ok=True)
    source.filepath_raw = str(destination.resolve())
    source.file_format = "PNG"
    source.save()

args.output.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=str(args.output.resolve()), use_selection=True,
    object_types={"MESH"}, apply_unit_scale=True, axis_forward="-Z", axis_up="Y",
    bake_anim=False, add_leaf_bones=False, mesh_smooth_type="FACE")
report = {"source_triangles": source_triangles, "final_triangles": len(mesh.polygons),
          "vertices": len(mesh.vertices), "uv_layers": len(mesh.uv_layers),
          "material_count": len(mesh.materials),
          "bounds": [[min(v.co[i] for v in mesh.vertices), max(v.co[i] for v in mesh.vertices)]
                     for i in range(3)]}
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, indent=2) + "\n")
print("TOWN_CABINET_SCULPT", json.dumps(report))
