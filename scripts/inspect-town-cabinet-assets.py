#!/usr/bin/env python3
"""Measure the actual exported cabinet meshes and physical controls in Blender."""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--directory", type=Path, required=True)
parser.add_argument("--report", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
results = {}
for part in ("merchant", "merchant_cabinet_sculpt", "merchant_cassette",
             "merchant_button", "merchant_crank", "merchant_shutter_leaf"):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str((args.directory / f"{part}.fbx").resolve()))
    objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    triangles = sum(sum(max(1,len(p.vertices)-2) for p in o.data.polygons) for o in objects)
    materials = sorted({m.name for o in objects for m in o.data.materials if m})
    records = []
    for o in objects:
        corners = [o.matrix_world @ Vector(corner) for corner in o.bound_box]
        records.append({"name":o.name, "triangles":sum(max(1,len(p.vertices)-2) for p in o.data.polygons),
                        "bounds":[[min(v[i] for v in corners),max(v[i] for v in corners)]
                                  for i in range(3)]})
    results[part] = {"triangles":triangles,"mesh_renderers":len(objects),
                     "materials":materials,"meshes":records}
results["deployed_presentation"] = {
    "triangles_including_six_buttons": sum(results[p]["triangles"] for p in
        ("merchant","merchant_cabinet_sculpt","merchant_cassette","merchant_crank",
         "merchant_shutter_leaf")) + 6*results["merchant_button"]["triangles"],
    "renderer_upper_bound_before_batching": sum(results[p]["mesh_renderers"] for p in
        ("merchant","merchant_cabinet_sculpt","merchant_cassette","merchant_crank",
         "merchant_shutter_leaf")) + 6*results["merchant_button"]["mesh_renderers"],
    "unique_materials": sorted(set().union(*(set(results[p]["materials"]) for p in
        ("merchant","merchant_cabinet_sculpt","merchant_cassette","merchant_button",
         "merchant_crank","merchant_shutter_leaf")))),
}
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(results,indent=2)+"\n")
print("CABINET_ASSET_METRICS",json.dumps({part:(v["triangles"],v["mesh_renderers"])
      for part,v in results.items() if part!="deployed_presentation"}),
      "deployed",results["deployed_presentation"])
