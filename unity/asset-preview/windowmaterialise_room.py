# windowmaterialise_room.py - put the effect in the ROOM, at the size and distance it is actually
# seen at, with Blender 4.2 / BLENDER_EEVEE_NEXT.
#
# WHY THIS EXISTS BESIDE windowmaterialise_preview.py. The flat strips answer "what does the field
# do"; they do not answer "how big is this in front of my face, and does the debris actually leave
# the window". A floated window is drawn at CanvasScaleMm 1.0 = 0.35 mm per uGUI pixel
# (WorldUIConfig.cs:613), so a 1600x1032 window is 0.56 m x 0.36 m and sits around 0.9 m away -
# roughly a laptop screen at arm's length. This puts the composited frames on a plane at exactly
# that size and photographs them from a head-height camera with a VR-ish field of view.
#
# AND IT RENDERS A STEREO PAIR. Not because two eye images can PROVE the absence of rivalry - they
# cannot, and stereo_audit.txt is the deliverable that settles that question - but because a
# reviewer is entitled to see both eyes of one instant and check that the debris sits on the same
# places OF THE PANEL in each. A screen-space dissolve would put it in the same places of the
# SCREEN instead, which is the thing the two eyes then fight about.
#
# RUN (windowmaterialise_preview.py writes the frames and room.json first):
#   /home/claw/blender-4.2/blender --background --python unity/asset-preview/windowmaterialise_room.py \
#       -- <outdir>
import json
import math
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "render/windowmaterialise")
ROOM = os.path.join(OUT, "room")

PIXEL_M = 0.00035          # CanvasScaleMm 1.0, WorldUIConfig.cs:613
HOST_PX_W = 1600           # a mid-sized floated window
DISTANCE_M = 0.90
IPD_M = 0.063
RES_X = 1000


def make_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x = RES_X
    sc.render.resolution_y = int(RES_X * 0.66)
    sc.render.image_settings.file_format = "PNG"
    sc.view_settings.view_transform = "Standard"   # the frames are already display-referred sRGB
    w = bpy.data.worlds.new("w")
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.018, 0.020, 0.026, 1)
    return sc


def make_panel(img_path, width_m, height_m):
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0, 0, 0))
    ob = bpy.context.object
    # SCALE BEFORE ROTATING MEANS THE PLANE IS STILL IN XY. Putting height on the Z component here
    # scaled a zero axis and rendered every frame 1.0 m tall (and cropped) - the first Blender pass
    # did exactly that.
    ob.scale = (width_m, height_m, 1.0)
    ob.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(scale=True, rotation=True)

    mat = bpy.data.materials.new("panel")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(img_path)
    tex.image.colorspace_settings.name = "sRGB"
    tex.interpolation = "Cubic"
    tex.extension = "CLIP"
    # EMISSION of the already-composited frame. Putting a lighting model on top of it would
    # photograph a different effect than the one that ships - the same rule render_asset.py states.
    nt.links.new(tex.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    ob.data.materials.append(mat)
    return ob


def make_camera(dx):
    cd = bpy.data.cameras.new("cam")
    cd.lens = 26.0
    cam = bpy.data.objects.new("cam", cd)
    bpy.context.collection.objects.link(cam)
    cam.location = (dx, -DISTANCE_M, 0.0)
    cam.rotation_euler = (math.radians(90), 0, 0)
    bpy.context.scene.camera = cam
    return cam


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    meta_path = os.path.join(ROOM, "room.json")
    if not os.path.exists(meta_path):
        raise SystemExit("no %s - run windowmaterialise_preview.py first" % meta_path)
    meta = json.load(open(meta_path))
    pad_l, pad_r, pad_d, pad_u = meta["pad"]

    # The image is the PADDED quad, so the panel occupies 1/(1+pad_l+pad_r) of its width. Sizing the
    # image by that factor is what makes the WINDOW come out at its real 0.56 m, rather than the
    # quad, which is what a naive "fit the image to the panel size" would have photographed.
    panel_w = HOST_PX_W * PIXEL_M
    img_w = panel_w * (1.0 + pad_l + pad_r)
    img_h = img_w * meta["image_px"][1] / float(meta["image_px"][0])
    print("panel %.3f x %.3f m; padded quad %.3f x %.3f m at %.2f m"
          % (panel_w, panel_w * meta["panel_px"][1] / meta["panel_px"][0], img_w, img_h, DISTANCE_M))

    for entry in meta["frames"]:
        name, stereo = entry["file"], entry.get("stereo", False)
        eyes = (("L", -IPD_M / 2), ("R", IPD_M / 2)) if stereo else ((None, 0.0),)
        for eye, dx in eyes:
            make_scene()
            make_panel(os.path.join(ROOM, name), img_w, img_h)
            make_camera(dx)
            stem = os.path.splitext(name)[0]
            render(os.path.join(OUT, "room_%s%s.png" % (stem, "_%s" % eye if eye else "")))
            print("  rendered", stem, eye or "")


main()
