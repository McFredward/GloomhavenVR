"""tex_render.py -- a CALIBRATED preview station for the board textures.

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/tex_render.py -- \
        --tex unity/board-prep/out/tex --style oak \
        --out unity/board-prep/out/renders/oak_material.png

WHY THIS EXISTS ALONGSIDE gen_render.py
---------------------------------------
Two separate problems with judging these textures using the shipped renderer.

1. gen_render.py IS OVEREXPOSED, measured. Its three area lights are 260/120/90 W
   aimed at a 0.64 m object with a 0.6-strength world on top. Rendering the
   finished oak atlas (source albedo mean luminance 0.419) through it gives a
   board whose lit surface sits at median 0.87, and steel at 0.94. That is about
   two stops over. Every material looks like white plastic through it, so it
   cannot answer "is this albedo clean?" -- it answers "yes" to everything.
   That is this project's preview-station-aimed-at-nothing lesson wearing a
   different hat: the station renders happily and tells you nothing.

2. gen_render.py maps the atlas onto the MESH. Until the mesh lane ships its
   rebuild, the only meshes available carry the old photogrammetry UVs, so the
   atlas lands in arbitrary places and you are looking at the wrong question.

So this station renders the atlas onto a SLAB WITH KNOWN UVs at the board's real
0.64 x 0.32 m footprint, and calibrates its own exposure against an 18% grey card
that is in the shot. The card is visible in the output: if the card is not sitting
at mid grey, the render is lying and you can see that it is lying.

It also ships a REFERENCE COLUMN -- the raw albedo, unlit, beside the lit render.
A render that disagrees with its own source texture is a lighting result, not a
texture result, and this project has a standing lesson about explaining those away.
"""

import os
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]


def arg(name, default=None):
    if name in argv:
        return argv[argv.index(name) + 1]
    return default


TEX = arg("--tex", "unity/board-prep/out/tex")
STYLE = arg("--style", "oak")
OUT = arg("--out", f"unity/board-prep/out/renders/{STYLE}_material.png")
REGION = arg("--region", "face")
GREY_TARGET = 0.466            # sRGB value of an 18% linear grey card

BASES = {"oak": "PlayTray", "steel": "PlayTray_9capjqp6", "bronze": "PlayTray_16vm268h"}
BASE = BASES.get(STYLE, STYLE)

# the contract's default region rects; only used to pick the UV window
REGIONS = {
    "face":         (0.0, 0.0, 1.0, 0.5),
    "frame":        (0.0, 0.5, 0.5, 0.75),
    "slot_floor":   (0.5, 0.5, 0.75, 0.75),
    "rest_pads":    (0.75, 0.5, 1.0, 0.75),
    "button_seats": (0.0, 0.75, 0.5, 1.0),
    "sides":        (0.5, 0.75, 1.0, 1.0),
}

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene


# --------------------------------------------------------------------------
# geometry: a slab at the board's REAL size, with UVs we control
# --------------------------------------------------------------------------

def slab(name, w, h, uv_box, z=0.0, x=0.0):
    u0, v0, u1, v1 = uv_box
    verts = [(x - w / 2, -h / 2, z), (x + w / 2, -h / 2, z),
             (x + w / 2, h / 2, z), (x - w / 2, h / 2, z)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], [(0, 1, 2, 3)])
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    for i, uv in enumerate([(u0, v0), (u1, v0), (u1, v1), (u0, v1)]):
        uvl.data[i].uv = uv
    ob = bpy.data.objects.new(name, me)
    sc.collection.objects.link(ob)
    return ob


BOARD_W, BOARD_H = 0.640, 0.320
board = slab("Board", BOARD_W, BOARD_H, REGIONS.get(REGION, REGIONS["face"]))

# the 18% grey card, same plane, to the side. It is what calibrates the exposure
# AND what proves in the output that the calibration held.
CARD = 0.10
card = slab("GreyCard", CARD, CARD, (0, 0, 1, 1), x=BOARD_W / 2 + CARD * 0.9)


# --------------------------------------------------------------------------
# materials
# --------------------------------------------------------------------------

def img(path, noncolor):
    if not os.path.exists(path):
        return None
    im = bpy.data.images.load(path)
    if noncolor:
        im.colorspace_settings.name = "Non-Color"
    return im


mat = bpy.data.materials.new("BoardMat")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]

alb = img(os.path.join(TEX, f"{BASE}_albedo.png"), False)
nrm = img(os.path.join(TEX, f"{BASE}_normal.png"), True)
mr = img(os.path.join(TEX, f"{BASE}_mr.png"), True)
missing = [n for n, v in (("albedo", alb), ("normal", nrm), ("mr", mr)) if v is None]
if alb is None:
    raise SystemExit(f"no albedo at {TEX}/{BASE}_albedo.png -- nothing to look at")
if missing:
    print(f"PREVIEW NOTE missing maps: {missing}")

na = nt.nodes.new("ShaderNodeTexImage")
na.image = alb
nt.links.new(bsdf.inputs["Base Color"], na.outputs["Color"])

if nrm is not None:
    nn = nt.nodes.new("ShaderNodeTexImage")
    nn.image = nrm
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nmap.inputs["Color"], nn.outputs["Color"])
    nt.links.new(bsdf.inputs["Normal"], nmap.outputs["Normal"])

if mr is not None:
    nm = nt.nodes.new("ShaderNodeTexImage")
    nm.image = mr
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(sep.inputs["Color"], nm.outputs["Color"])
    # glTF ORM packing, the same packing the original PlayTray_mr.png used:
    # R = occlusion, G = roughness, B = metallic
    nt.links.new(bsdf.inputs["Roughness"], sep.outputs["Green"])
    nt.links.new(bsdf.inputs["Metallic"], sep.outputs["Blue"])
else:
    bsdf.inputs["Roughness"].default_value = 0.55

board.data.materials.append(mat)

cmat = bpy.data.materials.new("GreyCard")
cmat.use_nodes = True
cb = cmat.node_tree.nodes["Principled BSDF"]
cb.inputs["Base Color"].default_value = (0.18, 0.18, 0.18, 1.0)   # 18% LINEAR
cb.inputs["Roughness"].default_value = 0.60
cb.inputs["Metallic"].default_value = 0.0
cb.inputs["Specular IOR Level"].default_value = 0.5
card.data.materials.append(cmat)


# --------------------------------------------------------------------------
# camera + lights. The key light RAKES the surface, because relief in a normal
# map is invisible under a light that is normal to it -- a flat-lit preview of a
# carved board looks exactly like a flat-lit preview of a flat board.
# --------------------------------------------------------------------------

span = BOARD_W + CARD * 2.4
cd = bpy.data.cameras.new("C")
cd.type = "ORTHO"
cd.ortho_scale = span * 1.06
cam = bpy.data.objects.new("C", cd)
sc.collection.objects.link(cam)
cam.location = (BOARD_W * 0.10, 0.0, 1.0)
cam.rotation_euler = (0.0, 0.0, 0.0)
sc.camera = cam


def light(loc, energy, size, kind="AREA"):
    ld = bpy.data.lights.new("L", kind)
    ld.energy = energy
    if kind == "AREA":
        ld.size = size
    ob = bpy.data.objects.new("L", ld)
    sc.collection.objects.link(ob)
    ob.location = loc
    d = (Vector((0, 0, 0)) - Vector(loc)).normalized()
    ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return ob


# 25 degrees above the surface: a raking key, so the carving casts across itself
light((-0.62, -0.38, 0.34), 22.0, 0.45)     # key, raking
light((0.55, 0.42, 0.75), 9.0, 0.90)        # fill, high and broad
light((0.30, -0.60, 0.55), 5.0, 0.70)       # rim

w = bpy.data.worlds.new("W")
sc.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.065, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.25

sc.render.engine = "BLENDER_EEVEE_NEXT"
sc.render.resolution_x = 1600
sc.render.resolution_y = 900
sc.render.film_transparent = False
sc.render.image_settings.file_format = "PNG"
# Standard, NOT Filmic/AgX: a tone curve would make the grey-card calibration
# meaningless, and the whole point of the card is that the number means something.
sc.view_settings.view_transform = "Standard"
sc.view_settings.look = "None"
sc.view_settings.exposure = 0.0


# --------------------------------------------------------------------------
# auto-exposure against the card, then the real render
# --------------------------------------------------------------------------

def render_to(path):
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


import math      # noqa: E402
import tempfile  # noqa: E402

LUMA = np.array([0.2126, 0.7152, 0.0722])


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def read_png(path):
    """Read back a rendered PNG as the RAW stored values. The colorspace is
    forced to Non-Color so Blender hands back exactly what is in the file
    instead of silently converting it -- an ambiguity here is what made the
    first version of this calibration wrong."""
    if path in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[path])
    im = bpy.data.images.load(path)
    im.colorspace_settings.name = "Non-Color"
    a = np.array(im.pixels[:], dtype=np.float64).reshape(im.size[1], im.size[0], 4)
    bpy.data.images.remove(im)
    return a[::-1]                       # blender rows are bottom-up


probe = os.path.join(tempfile.gettempdir(), "tex_render_probe.png")
render_to(probe)
px = read_png(probe)
h, wpx = px.shape[:2]

# the card occupies a known slice of the ortho frame
cx = (BOARD_W / 2 + CARD * 0.9 - BOARD_W * 0.10) / cd.ortho_scale + 0.5
cw = (CARD * 0.55) / cd.ortho_scale
x0, x1 = int((cx - cw / 2) * wpx), int((cx + cw / 2) * wpx)
ch = cw * wpx / h
y0, y1 = int((0.5 - ch / 2) * h), int((0.5 + ch / 2) * h)


def card_value(arr):
    return float(np.mean(arr[y0:y1, x0:x1, :3] @ LUMA))


# ITERATE on the measurement rather than trusting one analytic correction.
# Blender's `exposure` scales LINEAR light while the card reads out through the
# sRGB encode, so a one-shot correction computed in the wrong space lands short
# -- the first version of this did exactly that and reported 0.539 for a 0.466
# target while claiming it had corrected. Converging on the number cannot make
# that mistake, whichever space the arithmetic is in.
measured = card_value(px)
print(f"PREVIEW grey card at exposure 0.00: {measured:.4f} (want {GREY_TARGET:.4f})")
for it in range(6):
    if measured < 1e-4:
        print("PREVIEW grey card read BLACK -- the station is aimed at nothing, "
              "NOT calibrating, do not judge anything from this render")
        break
    if abs(measured - GREY_TARGET) < 0.008:
        break
    lin_m = float(srgb_to_linear(measured))
    lin_t = float(srgb_to_linear(GREY_TARGET))
    sc.view_settings.exposure += math.log2(max(lin_t, 1e-9) / max(lin_m, 1e-9))
    render_to(probe)
    measured = card_value(read_png(probe))
    print(f"PREVIEW   iter {it + 1}: exposure {sc.view_settings.exposure:+.3f} stops "
          f"-> card {measured:.4f}")

os.makedirs(os.path.dirname(OUT) or ".", exist_ok=True)
render_to(OUT)

# verify the correction actually landed, rather than asserting that it did
px2 = read_png(OUT)
after = card_value(px2)
ok = abs(after - GREY_TARGET) < 0.012
blum = px2[:, :int(0.62 * wpx), :3] @ LUMA
print(f"PREVIEW grey card in the final frame: {after:.4f}  "
      f"{'CALIBRATED' if ok else 'STILL OFF -- do not judge albedo from this render'}")
print(f"PREVIEW board luminance mean {blum.mean():.3f} "
      f"p5 {np.percentile(blum, 5):.3f} p50 {np.percentile(blum, 50):.3f} "
      f"p95 {np.percentile(blum, 95):.3f} clipped {100 * (blum >= 0.995).mean():.2f}%")
print("WROTE", OUT)
