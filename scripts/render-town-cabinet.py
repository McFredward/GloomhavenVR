#!/usr/bin/env python3
"""Render the actual authored merchant FBX for front and side review."""
import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--furniture", type=Path, required=True)
parser.add_argument("--sculpt", type=Path)
parser.add_argument("--sculpt-texture", type=Path)
parser.add_argument("--wood-texture", type=Path)
parser.add_argument("--hardware-textures", type=Path)
parser.add_argument("--populate", action="store_true")
parser.add_argument("--preview-icons", action="store_true",
                    help="Show dimensionally correct proxy glyphs; native UIInfoTools sprites load only in game")
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--view", choices=("front", "side", "under", "close"), default="front")
options = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(options.furniture.resolve()))
if options.sculpt:
    bpy.ops.import_scene.fbx(filepath=str(options.sculpt.resolve()))
if options.populate:
    furniture_dir = options.furniture.resolve().parent
    def import_mechanism(filename, shift):
        before=set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(furniture_dir / filename))
        added=set(bpy.data.objects)-before
        for obj in added:
            if obj.parent not in added:
                obj.location += Vector(shift)
        return added
    # The carved fascia is recessed in the middle. The production category
    # roots use .079, .105 and .093 m depths across the six sockets.
    button_depths=(.079,.105,.105,.105,.105,.093)
    button=import_mechanism("merchant_button.fbx", (1.25, -button_depths[0], .936))
    for category in range(1, 6):
        for original in button:
            if original.type != "MESH": continue
            copy=original.copy();copy.data=original.data.copy()
            bpy.context.collection.objects.link(copy)
            copy.location.x-=category*.12
            copy.location.y-=button_depths[category]-button_depths[0]
    if options.preview_icons:
        icon_material=bpy.data.materials.new("IconPlacementProxy")
        icon_material.diffuse_color=(.88,.73,.46,1)
        glyph_rotation=Matrix(((-1,0,0),(0,0,1),(0,1,0))).to_euler()
        for category,glyph in enumerate(("H","A","B","1H","2H","S")):
            shape=bpy.data.curves.new("OriginalSlotIcon placement proxy", "FONT")
            shape.body=glyph
            shape.size=.029 if len(glyph)>1 else .036
            shape.align_x="CENTER"
            shape.align_y="CENTER"
            shape.extrude=.0003
            shape.materials.append(icon_material)
            icon=bpy.data.objects.new("Proxy only; runtime uses original game sprite",shape)
            bpy.context.collection.objects.link(icon)
            icon.location=(1.25-category*.12,-button_depths[category]+.021,.936)
            icon.rotation_euler=glyph_rotation
    import_mechanism("merchant_crank.fbx", (.57,-.11,1.22))
    import_mechanism("merchant_cassette.fbx", (.95,-.035,1.22))
    preview=bpy.data.materials.new("OriginalItemPreview")
    preview.diffuse_color=(.65,.53,.34,1)
    for row in range(3):
        for col in range(4):
            x=.95-(col-1.5)*.18
            z=1.22-.17+row*.17
            y=-.012
            w,h=.14,.112
            card=bpy.data.meshes.new("NativeCardPreview")
            card.from_pydata([(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),
                              (x+w/2,y,z+h/2),(x-w/2,y,z+h/2)],[],[(0,1,2,3)])
            obj=bpy.data.objects.new("Native card seat preview only",card)
            bpy.context.collection.objects.link(obj)
            card.materials.append(preview)

for material in bpy.data.materials:
    finish_name = material.name.split(".")[0]
    if finish_name.endswith("DarkWood"):
        material.diffuse_color = (.22, .095, .041, 1)
    elif finish_name.endswith("Brass"):
        material.diffuse_color = (.39, .24, .08, 1)
    elif finish_name.endswith("ForgedIron"):
        material.diffuse_color = (.07, .065, .06, 1)
    elif finish_name.endswith("Leather"):
        material.diffuse_color = (.09, .035, .02, 1)
    elif finish_name.endswith("CabinetSculpt"):
        material.diffuse_color = (1, 1, 1, 1)
    material.use_nodes = True
    material.node_tree.nodes.clear()
    node = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    output = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
    material.node_tree.links.new(node.outputs["BSDF"], output.inputs["Surface"])
    if node:
        node.inputs["Base Color"].default_value = material.diffuse_color
        if options.sculpt_texture and finish_name.endswith("CabinetSculpt"):
            texture = material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image = bpy.data.images.load(str(options.sculpt_texture.resolve()))
            material.node_tree.links.new(texture.outputs["Color"], node.inputs["Base Color"])
        elif options.hardware_textures and any(finish_name.endswith(suffix) for suffix in
                                                ("DarkWood", "Brass", "ForgedIron")):
            kind = ("wood" if finish_name.endswith("DarkWood") else
                    "brass" if finish_name.endswith("Brass") else "iron")
            texture = material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image = bpy.data.images.load(str((options.hardware_textures /
                ("merchant_hardware_" + kind + ".png")).resolve()))
            material.node_tree.links.new(texture.outputs["Color"], node.inputs["Base Color"])
        elif options.wood_texture and finish_name.endswith("DarkWood"):
            texture = material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image = bpy.data.images.load(str(options.wood_texture.resolve()))
            tint = material.node_tree.nodes.new("ShaderNodeMixRGB")
            tint.blend_type = "MULTIPLY"
            tint.inputs[0].default_value = 1
            tint.inputs[2].default_value = (.82, .58, .42, 1)
            material.node_tree.links.new(texture.outputs["Color"], tint.inputs[1])
            material.node_tree.links.new(tint.outputs["Color"], node.inputs["Base Color"])

ground = bpy.data.meshes.new("FloorMesh")
ground.from_pydata([(-2, -2, 0), (4, -2, 0), (4, 3, 0), (-2, 3, 0)], [], [(0, 1, 2, 3)])
ground.update()
floor = bpy.data.objects.new("Floor", ground)
bpy.context.collection.objects.link(floor)
if options.view == "under": floor.hide_render = True
material = bpy.data.materials.new("FloorGrey")
material.diffuse_color = (.18, .18, .18, 1)
material.use_nodes = True
material.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = material.diffuse_color
ground.materials.append(material)

target = Vector((.95, -.26, 1.05))
def area(name, location, power, size):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = power
    data.shape = "DISK"
    data.size = size
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()

area("Softbox", (2.5, 2.1, 3), 300, 4)
area("Fill", (-1, 1.5, 1.8), 180, 3)
area("Rim", (2, -2, 3), 200, 2)
camera_data = bpy.data.cameras.new("ReviewCamera")
camera = bpy.data.objects.new("ReviewCamera", camera_data)
bpy.context.collection.objects.link(camera)
camera.location = ((2.15, 3.0, 1.94) if options.view == "front" else
                   (3.35, -.24, 1.86) if options.view == "side" else
                   (2.2, 2.15, .20) if options.view == "under" else
                   (.95, 1.5, 1.65))
camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
camera_data.type = "ORTHO"
camera_data.ortho_scale = 1.65 if options.view == "close" else 2.55
bpy.context.scene.camera = camera

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 24
scene.render.resolution_x = 1200
scene.render.resolution_y = 1200
scene.render.resolution_percentage = 100
scene.world.color = (.18, .18, .18)
scene.view_settings.view_transform = "AgX"
options.output.parent.mkdir(parents=True, exist_ok=True)
scene.render.filepath = str(options.output.resolve())
bpy.ops.render.render(write_still=True)
print("CABINET_RENDER", options.output)
