#!/usr/bin/env python3
"""Render clay, unlit color and experimental alpha from an offline NPC review blend.

Run Blender -b review.blend --python-exit-code 1 --python this-script --
--output-dir PATH. Never overwrites the review blend or source GLB.
"""

import argparse
import json
from pathlib import Path
import sys

import bmesh
import bpy
import numpy as np


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--samples", type=int, default=16)
    parser.add_argument("--resolution", type=int, default=768)
    modes = ("clay", "unlit_basecolor", "experimental_alpha", "clay_recalculated_normals")
    parser.add_argument("--modes", nargs="+", choices=modes, default=modes)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    args.output_dir.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.cycles.samples = args.samples
    scene.render.resolution_x = scene.render.resolution_y = args.resolution
    imported = [obj for obj in scene.objects if obj.type == "MESH" and not obj.name.startswith("REVIEW_")]
    originals = {obj.name: list(obj.data.materials) for obj in imported}
    materials = {material for slots in originals.values() for material in slots if material}
    images = {node.image for material in materials if material.use_nodes
              for node in material.node_tree.nodes if node.type == "TEX_IMAGE" and node.image}
    report = {"review_blend": bpy.data.filepath, "textures": [], "materials": [],
              "interpretation": "Alpha-linked renders are diagnostic experiments, not a validated export repair."}
    for image in sorted(images, key=lambda item: item.name):
        pixels = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        pixels = pixels.reshape(-1, 4)
        alpha = pixels[:, 3]
        dark = np.max(pixels[:, :3], axis=1) < 0.01
        report["textures"].append({"name": image.name, "dimensions": list(image.size),
                                   "alpha_mode": image.alpha_mode,
                                   "alpha_quantiles": np.quantile(alpha, [0, .01, .1, .5, .9, .99, 1]).tolist(),
                                   "pixels_alpha_below_099": int(np.count_nonzero(alpha < .99)),
                                   "pixels_alpha_below_050": int(np.count_nonzero(alpha < .5)),
                                   "dark_pixels": int(np.count_nonzero(dark)),
                                   "dark_pixels_alpha_below_050": int(np.count_nonzero(dark & (alpha < .5)))})
    for material in sorted(materials, key=lambda item: item.name):
        report["materials"].append({"name": material.name,
                                    "links": [[link.from_node.name, link.from_socket.name,
                                               link.to_node.name, link.to_socket.name]
                                              for link in material.node_tree.links]})
    (args.output_dir / "diagnosis.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for mode in args.modes:
        original_meshes = {}
        if mode == "clay_recalculated_normals":
            for obj in imported:
                original_meshes[obj.name] = obj.data
                obj.data = obj.data.copy()
                bm = bmesh.new()
                bm.from_mesh(obj.data)
                bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
                bm.to_mesh(obj.data)
                bm.free()
                # Zero custom vectors explicitly request automatically computed
                # normals in Blender 4.2; None is not accepted by this RNA method.
                obj.data.normals_split_custom_set([(0.0, 0.0, 0.0)] * len(obj.data.loops))
                obj.data.update()
        replacements = {}
        for original in materials:
            material = original.copy()
            material.name = "DIAG_" + mode + "_" + original.name
            nodes, links = material.node_tree.nodes, material.node_tree.links
            bsdf = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
            output = next(node for node in nodes if node.type == "OUTPUT_MATERIAL")
            color_links = list(bsdf.inputs["Base Color"].links)
            if mode.startswith("clay"):
                for socket in bsdf.inputs:
                    for link in list(socket.links):
                        links.remove(link)
                bsdf.inputs["Base Color"].default_value = (0.4, 0.4, 0.4, 1)
                bsdf.inputs["Metallic"].default_value = 0
                bsdf.inputs["Roughness"].default_value = .7
                bsdf.inputs["Alpha"].default_value = 1
            elif mode == "unlit_basecolor":
                emission = nodes.new("ShaderNodeEmission")
                if color_links:
                    links.new(color_links[0].from_socket, emission.inputs["Color"])
                else:
                    emission.inputs["Color"].default_value = bsdf.inputs["Base Color"].default_value
                links.new(emission.outputs[0], output.inputs["Surface"])
            elif color_links and color_links[0].from_node.type == "TEX_IMAGE":
                links.new(color_links[0].from_node.outputs["Alpha"], bsdf.inputs["Alpha"])
            replacements[original] = material
        for obj in imported:
            for index, material in enumerate(originals[obj.name]):
                obj.data.materials[index] = replacements.get(material, material)
        for camera_name, label in (("REVIEW_05_face", "face"), ("REVIEW_04_three_quarter", "full")):
            scene.camera = bpy.data.objects[camera_name]
            scene.render.filepath = str(args.output_dir / (mode + "_" + label + ".png"))
            print("NPC_MATERIAL_DIAG", mode, label, flush=True)
            bpy.ops.render.render(write_still=True)
        for obj in imported:
            for index, material in enumerate(originals[obj.name]):
                obj.data.materials[index] = material
        for material in replacements.values():
            bpy.data.materials.remove(material)
        for obj in imported:
            if obj.name in original_meshes:
                diagnostic_mesh = obj.data
                obj.data = original_meshes[obj.name]
                bpy.data.meshes.remove(diagnostic_mesh)
    print("NPC_MATERIAL_DIAG_COMPLETE", str(args.output_dir), flush=True)


if __name__ == "__main__":
    main()
