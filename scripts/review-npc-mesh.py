#!/usr/bin/env python3
"""Offline GLB inspection and studio evidence; run with Blender 4.2 --python.

See .planning/research/TOWN-SERVICES-MESH-REVIEW.md. Input is never modified.
"""

import argparse
import hashlib
import html
import json
import math
from pathlib import Path
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
import numpy as np


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--up-axis", choices=("auto", "x", "y", "z", "-x", "-y", "-z"), default="auto")
    parser.add_argument("--front-yaw-deg", type=float, default=0,
                        help="Camera yaw around normalized +Z; 0 looks from -Y, 90 from +X.")
    parser.add_argument("--resolution", type=int, default=960)
    parser.add_argument("--samples", type=int, default=32)
    parser.add_argument("--threads", type=int, default=8)
    parser.add_argument("--face-height", type=float, default=0.87,
                        help="Face crop centre as a fraction of normalized height.")
    parser.add_argument("--hands-height", type=float, default=0.54,
                        help="Hands crop centre as a fraction of normalized height.")
    parser.add_argument("--hands-offset", type=float, default=0.43,
                        help="Each hand crop horizontal offset as a fraction of projected figure width.")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if not args.input.is_file() or args.input.suffix.lower() != ".glb":
        parser.error("--input must name an existing .glb file")
    if min(args.resolution, args.samples, args.threads) < 1:
        parser.error("resolution, samples and threads must be positive")
    args.input = args.input.resolve()
    args.output_dir = args.output_dir.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    return args


def bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    if not points:
        raise ValueError("The imported GLB has no mesh bounds")
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return lo, hi


def bounds_record(objects):
    lo, hi = bounds(objects)
    return {"min": list(lo), "max": list(hi), "dimensions": list(hi - lo)}


def mesh_record(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    unseen = set(bm.verts)
    components = []
    while unseen:
        stack = [unseen.pop()]
        count = 0
        while stack:
            vertex = stack.pop()
            count += 1
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other in unseen:
                    unseen.remove(other)
                    stack.append(other)
        components.append(count)
    diagonal = Vector(obj.dimensions).length
    area_epsilon = max(diagonal * diagonal * 1e-12, 1e-20)
    degenerate = 0
    for tri in mesh.loop_triangles:
        a, b, c = (obj.matrix_world @ mesh.vertices[i].co for i in tri.vertices)
        if (b - a).cross(c - a).length * 0.5 <= area_epsilon:
            degenerate += 1
    result = {
        "object": obj.name, "mesh": mesh.name, "vertices": len(mesh.vertices),
        "triangles": len(mesh.loop_triangles), "polygons": len(mesh.polygons),
        "edges": len(mesh.edges), "indexed_components": len(components),
        "largest_component_vertex_counts": sorted(components, reverse=True)[:20],
        "boundary_edges": sum(e.is_boundary for e in bm.edges),
        "nonmanifold_edges_including_boundary": sum(not e.is_manifold for e in bm.edges),
        "wire_edges": sum(e.is_wire for e in bm.edges),
        "degenerate_triangles": degenerate, "degenerate_area_threshold": area_epsilon,
        "uv_layers": [layer.name for layer in mesh.uv_layers],
        "shape_keys": [] if mesh.shape_keys is None else [k.name for k in mesh.shape_keys.key_blocks],
        "vertex_groups": len(obj.vertex_groups),
        "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
        "modifiers": [{"name": mod.name, "type": mod.type} for mod in obj.modifiers],
        "world_bounds": bounds_record([obj]),
    }
    bm.free()
    return result


def material_record(material):
    result = {"name": material.name, "use_nodes": material.use_nodes,
              "diffuse_color": list(material.diffuse_color), "images": [], "principled": []}
    if material.use_nodes:
        for node in material.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                result["images"].append(node.image.name)
            if node.type == "BSDF_PRINCIPLED":
                values = {}
                for name in ("Base Color", "Metallic", "Roughness", "Alpha"):
                    socket = node.inputs.get(name)
                    value = socket.default_value
                    values[name] = {"linked": socket.is_linked,
                                    "value": list(value) if hasattr(value, "__len__") else value}
                result["principled"].append(values)
    return result


def normalize(objects, meshes, requested_up):
    lo, hi = bounds(meshes)
    dimensions = hi - lo
    # glTF Y-up imports as Blender Z-up. Prefer that contract unless the bounds
    # strongly disagree; widest-axis inference is only a standing-humanoid heuristic.
    inferred = "z" if dimensions.z >= max(dimensions) * 0.75 else "xyz"[max(range(3), key=lambda i: dimensions[i])]
    up = inferred if requested_up == "auto" else requested_up
    axis = Vector((0, 0, 0))
    axis["xyz".index(up[-1])] = -1 if up.startswith("-") else 1
    rotation = axis.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()
    root = bpy.data.objects.new("REVIEW_Normalization_Only", None)
    bpy.context.scene.collection.objects.link(root)
    for obj in objects:
        if obj.parent is None:
            world = obj.matrix_world.copy()
            obj.parent = root
            obj.matrix_world = world
    root.matrix_world = rotation
    bpy.context.view_layer.update()
    lo, hi = bounds(meshes)
    if hi.z - lo.z <= 1e-8:
        raise ValueError("Mesh has no usable height after up-axis selection")
    scale = 1.75 / (hi.z - lo.z)
    offset = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)) * scale
    root.matrix_world = Matrix.Translation(offset) @ Matrix.Scale(scale, 4) @ rotation
    bpy.context.view_layer.update()
    return {"glTF_spec_up": "+Y", "Blender_import_up": "+Z", "chosen_imported_up": up,
            "selection": "bounds heuristic" if requested_up == "auto" else "explicit override",
            "uniform_scale": scale, "review_height_m": 1.75,
            "transform": [list(row) for row in root.matrix_world],
            "normalized_bounds": bounds_record(meshes)}


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def studio(args, imported):
    scene = bpy.context.scene
    # A supplied GLB can contain cameras/lights. Keep them in the packed review
    # but exclude its lighting so all candidates share one inspection condition.
    for obj in imported:
        if obj.type == "LIGHT":
            obj.hide_render = True
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = args.samples
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = args.threads
    scene.render.resolution_x = args.resolution
    scene.render.resolution_y = args.resolution
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = 0
    scene.world = bpy.data.worlds.new("REVIEW_Neutral_World")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.36, 0.39, 0.43, 1)
    background.inputs["Strength"].default_value = 0.6
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -0.006))
    floor = bpy.context.object
    floor.name = "REVIEW_Studio_Floor"
    material = bpy.data.materials.new("REVIEW_Matte_Grey")
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.22, 0.245, 0.28, 1)
    bsdf.inputs["Roughness"].default_value = 0.85
    floor.data.materials.append(material)
    for name, position, energy, size in (
        ("Key", (-3, -4, 4.5), 650, 4),
        ("Fill", (3, -1.5, 2.7), 500, 3.5),
        ("Back", (0, 3, 3.5), 700, 3),
    ):
        data = bpy.data.lights.new("REVIEW_" + name, "AREA")
        data.energy, data.shape, data.size = energy, "DISK", size
        obj = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(obj)
        obj.location = position
        aim(obj, (0, 0, 0.9))
    scene.unit_settings.system = "METRIC"
    return scene


def render_views(args, scene, meshes):
    lo, hi = bounds(meshes)
    span = max(1.75, hi.x - lo.x, hi.y - lo.y) * 1.14
    angle = math.radians(args.front_yaw_deg)
    camera_right = Vector((math.cos(angle), math.sin(angle), 0))
    projected_width = abs(camera_right.x) * (hi.x - lo.x) + abs(camera_right.y) * (hi.y - lo.y)
    specs = [
        ("01_front", "Front", 0, 0.875, span, 0, 0),
        ("02_side", "Side", 90, 0.875, span, 0, 0),
        ("03_back", "Back", 180, 0.875, span, 0, 0),
        ("04_three_quarter", "Three-quarter", 35, 0.875, span, 0.25, 0),
        ("05_face", "Face (height heuristic)", 0, 1.75 * args.face_height, 0.55, 0, 0),
        ("06_face_three_quarter", "Face three-quarter", 35, 1.75 * args.face_height, 0.55, 0, 0),
        ("07_hand_screen_left", "Hand screen-left (position heuristic)", 0,
         1.75 * args.hands_height, 0.44, 0, -projected_width * args.hands_offset),
        ("08_hand_screen_right", "Hand screen-right (position heuristic)", 0,
         1.75 * args.hands_height, 0.44, 0, projected_width * args.hands_offset),
    ]
    result = []
    for stem, label, yaw, height, ortho_scale, elevation, horizontal in specs:
        data = bpy.data.cameras.new("REVIEW_" + stem)
        data.type, data.ortho_scale, data.clip_end = "ORTHO", ortho_scale, 100
        camera = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(camera)
        angle = math.radians(yaw + args.front_yaw_deg)
        target = Vector((0, 0, height)) + camera_right * horizontal
        camera.location = target + Vector((math.sin(angle) * 5, -math.cos(angle) * 5, elevation))
        aim(camera, target)
        scene.camera = camera
        scene.render.filepath = str(args.output_dir / (stem + ".png"))
        print("NPC_REVIEW_RENDER", stem, flush=True)
        bpy.ops.render.render(write_still=True)
        result.append({"file": stem + ".png", "label": label,
                       "yaw_degrees": yaw + args.front_yaw_deg,
                       "target": list(target), "target_height": height, "ortho_scale": ortho_scale})
    scene.camera = bpy.data.objects["REVIEW_04_three_quarter"]
    return result


def contact_sheet(args, views):
    # Image datablocks supply decoded scene-linear pixels. save_render applies the
    # ordinary display transform exactly once; use Standard, not AgX a second time.
    tile = min(args.resolution, 480)
    canvas = np.ones((tile * 2, tile * 4, 4), dtype=np.float32)
    for index, view in enumerate(views):
        image = bpy.data.images.load(str(args.output_dir / view["file"]), check_existing=False)
        image.scale(tile, tile)
        pixels = np.empty(tile * tile * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        row, col = 1 - index // 4, index % 4
        canvas[row * tile:(row + 1) * tile, col * tile:(col + 1) * tile] = pixels.reshape(tile, tile, 4)
        bpy.data.images.remove(image)
    sheet = bpy.data.images.new("REVIEW_Contact_Sheet", width=tile * 4, height=tile * 2)
    sheet.pixels.foreach_set(canvas.reshape(-1))
    scene = bpy.context.scene
    old_transform, old_look = scene.view_settings.view_transform, scene.view_settings.look
    scene.view_settings.view_transform, scene.view_settings.look = "Standard", "None"
    sheet.save_render(str(args.output_dir / "contact-sheet.png"), scene=scene)
    scene.view_settings.view_transform, scene.view_settings.look = old_transform, old_look
    bpy.data.images.remove(sheet)
    cards = "\n".join(f'<figure><a href="{v["file"]}"><img src="{v["file"]}"></a>'
                      f'<figcaption>{html.escape(v["label"])}</figcaption></figure>' for v in views)
    (args.output_dir / "index.html").write_text(
        '<!doctype html><meta charset="utf-8"><title>NPC mesh review</title>'
        '<style>body{background:#242930;color:#eee;font:16px sans-serif;margin:24px}'
        'main{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}'
        'figure{margin:0}img{width:100%}figcaption{padding:8px}a{color:#ccdfff}</style>'
        '<h1>NPC mesh review</h1><p>Review normalization: 1.75 m. '
        'Face and hand crops use height heuristics. '
        '<a href="report.json">Geometry and material report</a></p><main>' + cards + '</main>',
        encoding="utf-8")


def main():
    args = arguments()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(args.input))
    objects = list(bpy.context.scene.objects)
    meshes = [obj for obj in objects if obj.type == "MESH"]
    bpy.context.view_layer.update()
    materials = {slot.material for obj in meshes for slot in obj.material_slots if slot.material}
    report = {
        "input": str(args.input), "input_sha256": hashlib.sha256(args.input.read_bytes()).hexdigest(),
        "input_bytes": args.input.stat().st_size, "blender_version": bpy.app.version_string,
        "measurement_scope": "Imported base mesh, before review normalization; object instances counted separately.",
        "bounds_before_normalization": bounds_record(meshes),
        "meshes": [mesh_record(obj) for obj in meshes],
        "materials": [material_record(mat) for mat in sorted(materials, key=lambda mat: mat.name)],
        "textures": [{"name": im.name, "width": im.size[0], "height": im.size[1],
                      "channels": im.channels, "source": im.source, "packed": bool(im.packed_file),
                      "colorspace": im.colorspace_settings.name} for im in bpy.data.images],
        "armatures": [{"object": obj.name, "bones": len(obj.data.bones),
                       "bone_names": [bone.name for bone in obj.data.bones]}
                      for obj in objects if obj.type == "ARMATURE"],
        "actions": [{"name": action.name, "frame_range": list(action.frame_range)}
                    for action in bpy.data.actions],
        "warnings": ["Front direction and face/hand centres require visual confirmation.",
                     "Indexed components and boundaries include legitimate UV/material seams and separate accessories.",
                     "No automatic verdict on self-intersections, likeness, hands, rig suitability or VR performance.",
                     "Bounds include all imported mesh objects; posed/animated evaluation is not a rig validation."],
    }
    report["totals"] = {key: sum(record[key] for record in report["meshes"])
                        for key in ("vertices", "triangles", "indexed_components", "degenerate_triangles",
                                    "boundary_edges", "nonmanifold_edges_including_boundary")}
    report["normalization"] = normalize(objects, meshes, args.up_axis)
    report["front_yaw_degrees"] = args.front_yaw_deg
    report["render"] = {"engine": "CYCLES", "device": "CPU", "samples": args.samples,
                        "resolution": args.resolution, "view_transform": "AgX"}
    scene = studio(args, objects)
    report_path = args.output_dir / "report.json"
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    report["views"] = render_views(args, scene, meshes)
    contact_sheet(args, report["views"])
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output_dir / "review.blend"))
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("NPC_REVIEW_COMPLETE", json.dumps(report["totals"]), str(args.output_dir), flush=True)


if __name__ == "__main__":
    main()
