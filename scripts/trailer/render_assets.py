#!/usr/bin/env python3
"""Render cinematic plates from the shipped mod meshes, never modify the sources.

Run with Blender 4.2:
  blender -b --python scripts/trailer/render_assets.py -- --output /private/render

This is a trailer staging tool. PBR lighting is intentionally distinct from the
runtime's HeadUnlit/BoardLit shading. Geometry, UVs and albedo remain original.
"""
import argparse
import hashlib
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


SHOTS = ("mask_hero", "hands_table", "party_ensemble", "asset_reference")
USED = set()
ASSET_REPORT = []


def args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, default=Path(__file__).resolve().parents[2]
                        / "unity/GloomhavenVR.Assets/Assets/Bundle")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--shots", default=",".join(SHOTS))
    parser.add_argument("--width", type=int, default=1920)
    parser.add_argument("--samples", type=int, default=128)
    parser.add_argument("--card-texture", type=Path)
    parser.add_argument("--save-blend", action="store_true")
    tail = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    result = parser.parse_args(tail)
    result.assets = result.assets.resolve()
    result.output = result.output.resolve()
    result.shots = result.shots.split(",")
    if any(s not in SHOTS for s in result.shots):
        parser.error("unknown shot")
    result.output.mkdir(parents=True, exist_ok=True)
    return result


def texture(path, data=False):
    path = Path(path).resolve()
    if not path.is_file():
        raise FileNotFoundError(path)
    USED.add(path)
    image = bpy.data.images.load(str(path), check_existing=True)
    image.colorspace_settings.name = "Non-Color" if data else "sRGB"
    return image


def material(name, albedo=None, color=(.1, .1, .1, 1), metallic=0,
             roughness=.55, normal=None, mrs=None, emission=None, box=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    shader = tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Specular IOR Level"].default_value = .33
    shader.inputs["Emission Strength"].default_value = .025 if albedo else 0
    if albedo:
        tex = tree.nodes.new("ShaderNodeTexImage")
        tex.image = texture(albedo)
        if box:
            tex.projection = 'BOX'
            tex.projection_blend = .25
            coordinates = tree.nodes.new("ShaderNodeTexCoord")
            tree.links.new(coordinates.outputs["Generated"], tex.inputs["Vector"])
        tree.links.new(tex.outputs["Color"], shader.inputs["Base Color"])
        tree.links.new(tex.outputs["Color"], shader.inputs["Emission Color"])
        if emission:
            # Only already-painted cyan/purple runes emit. No extra symbols or
            # lines are authored; all emission is masked by the original albedo.
            separate = tree.nodes.new("ShaderNodeSeparateColor")
            tree.links.new(tex.outputs["Color"], separate.inputs["Color"])
            first, second, suppress = ((1, 2, 0) if emission == "cyan" else (2, 0, 1))
            subtract = tree.nodes.new("ShaderNodeMath")
            subtract.operation = 'SUBTRACT'
            tree.links.new(separate.outputs[first], subtract.inputs[0])
            tree.links.new(separate.outputs[suppress], subtract.inputs[1])
            limit = tree.nodes.new("ShaderNodeMath")
            limit.operation = 'GREATER_THAN'
            limit.inputs[1].default_value = .12 if emission == "cyan" else .10
            tree.links.new(subtract.outputs[0], limit.inputs[0])
            gain = tree.nodes.new("ShaderNodeMath")
            gain.operation = 'MULTIPLY_ADD'
            gain.inputs[1].default_value = 1.9
            gain.inputs[2].default_value = .035
            tree.links.new(limit.outputs[0], gain.inputs[0])
            tree.links.new(gain.outputs[0], shader.inputs["Emission Strength"])
    if normal:
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = texture(normal, True)
        norm = tree.nodes.new("ShaderNodeNormalMap")
        norm.inputs["Strength"].default_value = .28
        tree.links.new(node.outputs["Color"], norm.inputs["Color"])
        tree.links.new(norm.outputs["Normal"], shader.inputs["Normal"])
    if mrs:
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = texture(mrs, True)
        sep = tree.nodes.new("ShaderNodeSeparateColor")
        tree.links.new(node.outputs["Color"], sep.inputs["Color"])
        tree.links.new(sep.outputs[0], shader.inputs["Metallic"])
        tree.links.new(sep.outputs[1], shader.inputs["Roughness"])
    return mat


def import_asset(config, rel, mat, name, location, euler=(0, 0, 0),
                 scale=1, curl=0, center=True):
    src = config.assets / rel
    USED.add(src)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(src))
    imported = set(bpy.data.objects) - before
    for obj in imported:
        if obj.type == 'ARMATURE' and curl:
            for finger in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
                angles = (25, 45, 60) if finger == "Thumb" else (75, 95, 65)
                for joint, angle in zip(("Root", "Mid", "Tip"), angles):
                    bone = obj.pose.bones.get(f"Anchor_{finger}_{joint}")
                    if bone:
                        bone.rotation_mode = 'QUATERNION'
                        bone.rotation_quaternion = Quaternion((1, 0, 0), math.radians(angle * curl))
    bpy.context.view_layer.update()
    graph = bpy.context.evaluated_depsgraph_get()
    meshes = []
    for obj in imported:
        if obj.type != 'MESH':
            continue
        # Bake the import matrix and the original rig deformation into a staging
        # copy. The shipped file is read only; no remeshing or subdivision occurs.
        evaluated = obj.evaluated_get(graph)
        mesh = bpy.data.meshes.new_from_object(evaluated, depsgraph=graph)
        mesh.transform(obj.matrix_world)
        staged = bpy.data.objects.new(name + "_" + obj.name, mesh)
        bpy.context.scene.collection.objects.link(staged)
        mesh.materials.clear()
        mesh.materials.append(mat)
        meshes.append(staged)
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    points = [vertex.co for obj in meshes for vertex in obj.data.vertices]
    lower = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    upper = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    pivot = (lower + upper) / 2 if center else Vector((0, 0, 0))
    rotation = (Matrix.Rotation(math.radians(euler[2]), 4, 'Z')
                @ Matrix.Rotation(math.radians(euler[1]), 4, 'Y')
                @ Matrix.Rotation(math.radians(euler[0]), 4, 'X'))
    transform = Matrix.Translation(Vector(location)) @ rotation @ Matrix.Scale(scale, 4)
    for obj in meshes:
        obj.data.transform(Matrix.Translation(-pivot))
        obj.matrix_world = transform
    ASSET_REPORT.append({"shot": bpy.context.scene.name, "source": str(src),
                         "staging_name": name, "mesh_count": len(meshes),
                         "bounds_metres": [list(lower), list(upper)],
                         "staging_pivot": list(pivot), "location": list(location),
                         "euler_degrees_xyz": list(euler), "scale": scale,
                         "runtime_joint_curl": curl})
    return meshes


def cube(name, location, size, mat, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if mat:
        obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new("Set dressing edge wear", 'BEVEL')
        modifier.width = bevel
        modifier.segments = 3
        obj.modifiers.new("Set dressing normals", 'WEIGHTED_NORMAL')
    return obj


def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def area(name, position, target, color, energy, size=.5):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.color, data.shape, data.size = energy, color, 'DISK', size
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = position
    look_at(obj, target)
    return obj


def camera(position, target, lens=60, focus=None, fstop=2.8):
    data = bpy.data.cameras.new("Cinematic camera")
    data.lens = lens
    obj = bpy.data.objects.new("Cinematic camera", data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = position
    look_at(obj, target)
    bpy.context.scene.camera = obj
    if focus is not None:
        data.dof.use_dof = True
        data.dof.focus_distance = (obj.location - Vector(focus)).length
        data.dof.aperture_fstop = fstop
        data.dof.aperture_blades = 8
    return obj


def reset(config, shot):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.name = shot
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.eevee.taa_render_samples = config.samples
    scene.render.resolution_x = config.width
    scene.render.resolution_y = round(config.width * 9 / 16)
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.render.film_transparent = shot == 'asset_reference'
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = -.15
    scene.world = bpy.data.worlds.new("Dark ambient")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (.013, .021, .032, 1)
    bg.inputs["Strength"].default_value = .22
    # Restrained bloom carries existing runes and candles without veiling detail.
    scene.use_nodes = True
    tree = scene.node_tree
    tree.nodes.clear()
    layers = tree.nodes.new("CompositorNodeRLayers")
    glare = tree.nodes.new("CompositorNodeGlare")
    glare.glare_type, glare.quality, glare.threshold = 'FOG_GLOW', 'HIGH', 1.5
    glare.size, glare.mix = 7, -.94
    output = tree.nodes.new("CompositorNodeComposite")
    tree.links.new(layers.outputs["Image"], glare.inputs["Image"])
    tree.links.new(glare.outputs["Image"], output.inputs["Image"])
    return scene


def mask_mat(config, index):
    return material(f"Original mask {index}, cinematic response",
                    config.assets / f"Head/Mask_{index}_albedo.png",
                    metallic=.42 if index == 0 else .07,
                    roughness=.48 if index == 0 else .62,
                    emission="purple" if index == 1 else None)


def hand_mat(config, style):
    base = config.assets / f"Hands/{style}"
    normal = Path(str(base) + "_normal.png")
    mrs = Path(str(base) + "_mrs.png")
    return material(f"Original {style}, cinematic response", Path(str(base) + "_albedo.png"),
                    metallic=.36 if style == "VRHandPlate" else .08,
                    roughness=.51, normal=normal if normal.exists() else None,
                    mrs=mrs if mrs.exists() else None,
                    emission="cyan" if style == "VRHandArcane" else None)


def tavern(config, extent=2):
    stone = material("Native monastery stone on staging masonry",
                     config.assets / "Environments/Imported/Textures/monastery_stone_floor_alb.jpg",
                     roughness=.82, box=True)
    wood = material("Native wooden crate texture on staging table",
                    config.assets / "Environments/Imported/Textures/wooden_crate_01_alb.jpg",
                    roughness=.73, box=True)
    bronze = material("Staging dark bronze", color=(.075, .035, .012, 1), metallic=.8)
    cube("Cinematic tavern tabletop", (0, .08, -.10), (extent, extent * .8, .17), wood, .013)
    # Source textures on deliberately out-of-focus trailer set dressing. This
    # geometry does not claim to be the game's authored room prefab.
    random.seed(418)
    for row in range(6):
        for col in range(9):
            x = (col - 4) * .28 + (.14 if row % 2 else 0)
            cube(f"Backdrop masonry {row}_{col}", (x, 2.0, row * .17 + .02),
                 (.27, .16, .165), stone, .006)
    wax = material("Staging candle wax", color=(.28, .16, .072, 1), roughness=.7)
    flame = material("Staging practical flame", color=(.6, .1, .007, 1))
    shader = flame.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Emission Color"].default_value = (1, .17, .01, 1)
    shader.inputs["Emission Strength"].default_value = 7
    for x, y, h in ((-.44, .52, .15), (.61, .68, .24), (-.66, .87, .29)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=.026, depth=h, location=(x, y, h / 2))
        bpy.context.object.data.materials.append(wax)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=.012,
                                           location=(x, y, h + .012))
        bpy.context.object.scale = (.55, .55, 1.4)
        bpy.context.object.data.materials.append(flame)
        data = bpy.data.lights.new("Candle practical", 'POINT')
        data.color, data.energy, data.shadow_soft_size = (1, .28, .045), 3.5, .055
        obj = bpy.data.objects.new("Candle practical", data)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = (x, y, h + .03)
    return bronze


def lighting(target=(0, 0, .2), group=False):
    gain = 1.8 if group else 1
    area("Warm left key", (-.7, -.5, .85), target, (1, .65, .36), 20 * gain, .65)
    area("Cyan right fill", (.7, -.1, .45), target, (.17, .65, 1), 13 * gain, .7)
    area("Warm rim", (-.25, .65, .75), target, (1, .38, .12), 35 * gain, .45)
    area("Soft face lift", (0, -1, .2), target, (.5, .68, 1), 3 * gain, .85)


def board(config, location=(0, .06, .015), scale=1):
    base = config.assets / "Table/PlayTray"
    mat = material("Original play tray, cinematic response", Path(str(base) + "_albedo.png"),
                   normal=Path(str(base) + "_normal.png"),
                   mrs=Path(str(base) + "_mrs.png"), roughness=.65)
    return import_asset(config, "Table/PlayTray_prepped.fbx", mat, "Original tray",
                        location, euler=(180, 0, 0), scale=scale)


def native_cards(config, position=(.03, -.015, .06)):
    if not config.card_texture:
        return
    art = material("Exact native card artwork", config.card_texture, roughness=.68)
    # Fixed UV plane: no invented names, values, typography or pseudo-text.
    for i in range(3):
        x, y, z = position
        obj = cube("Native artwork card backing", (x + (i - 1) * .036, y + i * .008, z + i * .003),
                   (.061, .09, .0012), material("Parchment card edge", color=(.12, .077, .035, 1)), .001)
        obj.rotation_euler.z = math.radians((i - 1) * -13)
        vertices = [(-.03, -.044, .0008), (.03, -.044, .0008),
                    (.03, .044, .0008), (-.03, .044, .0008)]
        mesh = bpy.data.meshes.new("Card art plane")
        mesh.from_pydata(vertices, [], [(0, 1, 2, 3)])
        mesh.uv_layers.new()
        for loop, uv in zip(mesh.uv_layers.active.data, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop.uv = uv
        face = bpy.data.objects.new("Unchanged native card texture", mesh)
        bpy.context.scene.collection.objects.link(face)
        face.matrix_world = obj.matrix_world.copy()
        mesh.materials.append(art)


def mask_hero(config):
    tavern(config)
    import_asset(config, "Head/Mask_0.fbx", mask_mat(config, 0), "Steel mask hero",
                 (.015, 0, .31), euler=(0, -4, -10), scale=1.08)
    lighting((0, 0, .3))
    camera((.16, -.90, .395), (.012, -.015, .31), lens=62,
           focus=(.012, -.09, .31), fstop=8)


def hands_table(config):
    tavern(config)
    board(config, location=(0, .08, .005))
    import_asset(config, "Hands/VRHandArcane_L_rig.fbx", hand_mat(config, "VRHandArcane"),
                 "Arcane left original hand", (-.18, -.13, .14), euler=(68, -12, -25),
                 scale=.75, curl=.16)
    import_asset(config, "Hands/VRHandPlate_R_rig.fbx", hand_mat(config, "VRHandPlate"),
                 "Plate right original hand", (.24, .09, .18), euler=(76, 8, 25),
                 scale=.74, curl=.18)
    native_cards(config)
    lighting((0, -.02, .13))
    camera((.52, -.69, .48), (.01, -.01, .09), lens=59,
           focus=(-.10, -.075, .10), fstop=4)


def party_ensemble(config):
    tavern(config, extent=2.5)
    board(config, location=(0, -.12, .005), scale=1.25)
    for index, pos, yaw in ((0, (-.31, .18, .47), -12),
                            (1, (0, .12, .52), 0), (2, (.31, .18, .47), 12)):
        import_asset(config, f"Head/Mask_{index}.fbx", mask_mat(config, index),
                     f"Original party mask {index}", pos, euler=(0, 0, yaw), scale=1.12)
    for style, side, pos, yaw in (("VRHandPlate", "R", (-.22, .09, .15), -18),
                                 ("VRHandArcane", "L", (.14, -.02, .17), 22),
                                 ("VRHand", "R", (.37, .17, .17), 12)):
        import_asset(config, f"Hands/{style}_{side}_rig.fbx", hand_mat(config, style),
                     f"Original party {style}", pos, euler=(75, 0, yaw), scale=.56, curl=.15)
    native_cards(config, position=(0, -.22, .065))
    lighting((0, .09, .32), group=True)
    camera((.05, -1.62, .56), (0, .10, .30), lens=52,
           focus=(0, .12, .48), fstop=5.6)


def asset_reference(config):
    for i, x in enumerate((-.32, 0, .32)):
        import_asset(config, f"Head/Mask_{i}.fbx", mask_mat(config, i),
                     f"Original reference mask {i}", (x, 0, .32), euler=(0, 0, -10))
    for style, x in zip(("VRHand", "VRHandPlate", "VRHandArcane"), (-.32, 0, .32)):
        import_asset(config, f"Hands/{style}_L_rig.fbx", hand_mat(config, style),
                     f"Original reference hand {style}", (x, 0, -.05), scale=.65)
    lighting((0, 0, .1), group=True)
    cam = camera((.06, -1.5, .24), (0, 0, .15), lens=50)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 1.14


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    config = args()
    outputs = []
    for shot in config.shots:
        scene = reset(config, shot)
        globals()[shot](config)
        image = config.output / f"{shot}.png"
        scene.render.filepath = str(image)
        if config.save_blend:
            blend = config.output / f"{shot}.blend"
            bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
            outputs.append(blend)
        bpy.ops.render.render(write_still=True)
        outputs.append(image)
        print(f"PLATE_READY {image}", flush=True)
    sources = sorted(USED)
    manifest = {"tool": str(Path(__file__).resolve()), "tool_sha256": sha(__file__),
                "blender_version": bpy.app.version_string, "engine": "BLENDER_EEVEE_NEXT",
                "width": config.width, "height": round(config.width * 9 / 16),
                "samples": config.samples,
                "presentation": "Cinematic original-mesh staging with adapted PBR, not gameplay capture",
                "sources": [{"path": str(p), "bytes": p.stat().st_size, "sha256": sha(p)} for p in sources],
                "asset_instances": ASSET_REPORT,
                "outputs": [{"path": str(p), "bytes": p.stat().st_size, "sha256": sha(p)} for p in outputs]}
    (config.output / 'render-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__':
    main()
