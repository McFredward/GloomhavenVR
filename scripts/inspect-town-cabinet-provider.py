#!/usr/bin/env python3
"""Inspect a provider cabinet GLB and render its exact reconstructed geometry."""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(args.source.resolve()))
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if not meshes:
    raise RuntimeError("Provider GLB has no mesh")
corners = [o.matrix_world @ Vector(corner) for o in meshes for corner in o.bound_box]
minimum = [min(p[i] for p in corners) for i in range(3)]
maximum = [max(p[i] for p in corners) for i in range(3)]
center = Vector([(a+b)/2 for a,b in zip(minimum, maximum)])
report = {
    "objects": [{"name": o.name, "faces": len(o.data.polygons),
                 "triangles": sum(max(1, len(p.vertices)-2) for p in o.data.polygons),
                 "vertices": len(o.data.vertices), "uv_layers": len(o.data.uv_layers),
                 "materials": [m.name for m in o.data.materials]}
                for o in meshes],
    "minimum": minimum, "maximum": maximum,
    "images": [{"name": i.name, "width": i.size[0], "height": i.size[1]}
               for i in bpy.data.images if i.size[0] > 0],
}
args.output.mkdir(parents=True, exist_ok=True)
(args.output / "inspection.json").write_text(json.dumps(report, indent=2)+"\n")
print("CABINET_PROVIDER", json.dumps(report))

floor = bpy.data.meshes.new("Review floor")
floor.from_pydata([(-2,-2,minimum[2]-.01),(2,-2,minimum[2]-.01),
                   (2,2,minimum[2]-.01),(-2,2,minimum[2]-.01)], [], [(0,1,2,3)])
floor_obj = bpy.data.objects.new("Review floor", floor)
bpy.context.collection.objects.link(floor_obj)
mat = bpy.data.materials.new("Review background")
mat.diffuse_color = (.14,.14,.14,1)
floor.materials.append(mat)
for name, pos, power, size in (("Key", (2,-2,3), 500, 4),
                               ("Fill", (-2,1,2), 260, 4),
                               ("Rim", (1,2,3), 350, 3)):
    lamp = bpy.data.lights.new(name, "AREA")
    lamp.energy = power
    lamp.size = size
    o = bpy.data.objects.new(name, lamp)
    bpy.context.collection.objects.link(o)
    o.location = center + Vector(pos)
    o.rotation_euler = (center-o.location).to_track_quat("-Z","Y").to_euler()
camera_data = bpy.data.cameras.new("Review camera")
camera = bpy.data.objects.new("Review camera",camera_data)
bpy.context.collection.objects.link(camera)
camera_data.type = "ORTHO"
camera_data.ortho_scale = max(maximum[i]-minimum[i] for i in range(3))*1.45
bpy.context.scene.camera = camera
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 24
scene.render.resolution_x = 1200
scene.render.resolution_y = 1200
scene.view_settings.view_transform = "AgX"
for name, pos in (("front", (0,-2,0.35)), ("rear", (0,2,0.35)),
                  ("angle", (1.3,-1.8,0.6))):
    camera.location = center + Vector(pos)
    camera.rotation_euler = (center-camera.location).to_track_quat("-Z","Y").to_euler()
    scene.render.filepath = str((args.output / f"provider-{name}.png").resolve())
    bpy.ops.render.render(write_still=True)
