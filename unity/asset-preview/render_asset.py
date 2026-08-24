# render_asset.py — Blender headless: photograph ANY bundled mod asset the way the GAME draws it.
#
# WHY THIS EXISTS AND WHY IT IS NOT render_hand.py. unity/hand-prep/render_hand.py reproduces the
# same shader, but everything around it is a hand: it frames the palm at 62 % of the bbox height,
# it names its views "palm/back/thumb/cuff", and it hides armatures because a rigged hand has one.
# The README needs the same photograph of a MASK and of a CONTROL BOARD, neither of which has a
# palm. This script keeps the shading verbatim and replaces the framing with an auto-fit, so one
# tool covers hands, masks, boards and anything else that arrives as FBX + albedo.
#
# THE SHADING IS THE GAME'S ARITHMETIC, NOT A LOOK-ALIKE. The shipped material is
# GloomhavenVR/BoardLit with _Ambient 0.5 and _LightBoost 0.85 — pure Lambert against two BAKED
# world-space directions plus an ambient floor, with no specular term at all:
#
#     shade = 0.5 + saturate(N.key) * 0.85 + saturate(N.fill) * 0.35
#     key   = normalize( 0.35,  0.85, -0.45)      fill = normalize(-0.55, 0.35, 0.30)
#
# (Unity is Y-up left-handed, Blender Z-up right-handed, so (x, y, z)_unity -> (x, z, y)_blender.)
# It is built as EMISSION of albedo x shade so no renderer lighting model gets a say. A render that
# used Blender's own Principled BSDF would put a glossy sheen on metal the player never sees, and
# this project has already shipped one asset decision made against a preview that lied.
#
# TRANSPARENT FILM BY DEFAULT. The README shows these on both GitHub themes, so the alpha is kept
# and the compositing is done afterwards — see docs/img/README.md for why a flattened PNG is what
# actually ships.
#
# RUN:
#   /home/claw/blender-4.2/blender --background --python unity/asset-preview/render_asset.py -- \
#       <in.fbx> <albedo.png> <out.png> [--normal <normal.png>] [--res 900] [--yaw 35] [--pitch 18]
#       [--cull] [--margin 1.06] [--scale 0.42] [--focus-z 0.10]
import bpy, sys, os, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC, TEX, OUT = pos[0], pos[1], pos[2]


def opt(name, default):
    return argv[argv.index(name) + 1] if name in argv else default


RES = int(opt("--res", "900"))
YAW = float(opt("--yaw", "35"))
PITCH = float(opt("--pitch", "18"))
MARGIN = float(opt("--margin", "1.06"))
# A FAMILY MUST SHARE ONE SCALE OR THE STRIP LIES. Auto-fit sizes each asset to the frame, so
# three hands photographed separately come out the same size on screen while being different
# sizes in the room — and the strip then says "these are interchangeable" about geometry it has
# just normalised away. Pass --scale to pin the whole family to one ortho width.
FIXED_SCALE = float(opt("--scale", "0"))
# FRAME ON THE THING, NOT ON THE BOUNDING BOX. A plate gauntlet is 333 mm tall because 150 mm of
# that is CUFF, while the leather glove stops at the wrist — so a shared scale centred on the box
# renders the glove tiny beside two forearms. All three hands are the same 183 mm wrist-to-
# fingertip (measured, ModBuild 243), which is the span a reader is actually comparing, so
# --focus-z pins the frame centre to that span instead.
FOCUS_Z = opt("--focus-z", "")
NORMAL = opt("--normal", "")
CULL = "--cull" in argv

GAME_KEY = (0.35, -0.45, 0.85)
GAME_FILL = (-0.55, 0.30, 0.35)
GAME_KEY = tuple(c / math.sqrt(sum(v * v for v in GAME_KEY)) for c in GAME_KEY)
GAME_FILL = tuple(c / math.sqrt(sum(v * v for v in GAME_FILL)) for c in GAME_FILL)


def log(*a):
    print("[asset]", *a)
    sys.stdout.flush()


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath(SRC))

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if not meshes:
    raise RuntimeError(f"no mesh in {SRC}")
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE':
        o.hide_render = True

# ---- AUTO-FRAME over EVERY mesh, not just the first. A control board is several meshes (slab,
#      keycaps, readout) and framing on one of them would crop the others out of the picture.
pts = []
for ob in meshes:
    mw = ob.matrix_world
    pts += [mw @ Vector(c) for c in ob.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (lo + hi) * 0.5
size = hi - lo
log(f"{os.path.basename(SRC)}: {len(meshes)} mesh(es), bbox mm "
    f"{[round(v * 1000, 1) for v in size]}, centre {[round(v * 1000, 1) for v in ctr]}")

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x = scene.render.resolution_y = RES
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.film_transparent = True          # composited later, per README theme
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.eevee.taa_render_samples = 128
if hasattr(scene.eevee, "use_raytracing"):
    scene.eevee.use_raytracing = False

# ---- The camera direction, and why it is ORTHOGRAPHIC. A perspective lens would foreshorten the
#      far end of a 0.64 m control board and make two boards photographed side by side look like
#      different sizes. Ortho keeps a strip of assets comparable, which is the whole point here.
yaw = math.radians(YAW)
pitch = math.radians(PITCH)
d = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
d.normalize()

cam_data = bpy.data.cameras.new("cam")
cam_data.type = 'ORTHO'
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
if FOCUS_Z != "":
    ctr = Vector((ctr.x, ctr.y, float(FOCUS_Z)))
    log(f"focus centre pinned to z={float(FOCUS_Z):+.4f}")
cam.location = ctr + d * (size.length * 3.0 + 1.0)
cam.rotation_mode = 'QUATERNION'
cam.rotation_quaternion = (-d).to_track_quat('-Z', 'Y')

# FIT THE ORTHO SCALE TO THE BOX AS THE CAMERA SEES IT, not to a bbox axis: an asset viewed from
# 35 degrees presents a diagonal, and fitting to size.x would clip the corners off every board.
#
# THE view_layer.update() IS LOAD-BEARING, not tidiness. Assigning `location` and
# `rotation_quaternion` does NOT refresh `matrix_world` — Blender defers that to the next
# dependency-graph evaluation. Without this line `cam.matrix_world` is still IDENTITY here, the
# "camera-space" extents below are silently WORLD x and y, and the fit comes out right only for
# assets whose widest world axis happens to be the one the camera sees. That is exactly how the
# first run of this script framed the boards correctly and cut the fingertips off all three hands:
# the hands' long axis is world Z, which an identity matrix never looks at.
bpy.context.view_layer.update()
inv = cam.matrix_world.inverted()
cx = [(inv @ p).x for p in pts]
cy = [(inv @ p).y for p in pts]
cam_data.ortho_scale = (FIXED_SCALE if FIXED_SCALE > 0.0 else
                        max(max(cx) - min(cx), max(cy) - min(cy)) * MARGIN)
log(f"ortho scale {cam_data.ortho_scale:.4f} at yaw {YAW:.0f} pitch {PITCH:.0f}")

albedo = bpy.data.images.load(os.path.abspath(TEX))
albedo.colorspace_settings.name = 'sRGB'
normal_img = None
if NORMAL and os.path.exists(NORMAL):
    normal_img = bpy.data.images.load(os.path.abspath(NORMAL))
    normal_img.colorspace_settings.name = 'Non-Color'
    log(f"normal map: {os.path.basename(NORMAL)}")

mat = bpy.data.materials.new("m_game")
mat.use_nodes = True
mat.use_backface_culling = CULL
mat.blend_method = 'BLEND'
nt = mat.node_tree
nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial")

tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = albedo

geo = nt.nodes.new("ShaderNodeNewGeometry")
normal_src = geo.outputs["Normal"]
if normal_img is not None:
    ntex = nt.nodes.new("ShaderNodeTexImage")
    ntex.image = normal_img
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
    normal_src = nmap.outputs["Normal"]

shade = None
for vec, gain in ((GAME_KEY, 0.85), (GAME_FILL, 0.35)):
    dot = nt.nodes.new("ShaderNodeVectorMath")
    dot.operation = 'DOT_PRODUCT'
    dot.inputs[1].default_value = vec
    nt.links.new(normal_src, dot.inputs[0])
    cl = nt.nodes.new("ShaderNodeMath")
    cl.operation = 'MAXIMUM'
    cl.inputs[1].default_value = 0.0
    nt.links.new(dot.outputs["Value"], cl.inputs[0])
    sc = nt.nodes.new("ShaderNodeMath")
    sc.operation = 'MULTIPLY'
    sc.inputs[1].default_value = gain
    nt.links.new(cl.outputs[0], sc.inputs[0])
    if shade is None:
        shade = sc
    else:
        ad = nt.nodes.new("ShaderNodeMath")
        ad.operation = 'ADD'
        nt.links.new(shade.outputs[0], ad.inputs[0])
        nt.links.new(sc.outputs[0], ad.inputs[1])
        shade = ad

amb = nt.nodes.new("ShaderNodeMath")
amb.operation = 'ADD'
amb.inputs[1].default_value = 0.5              # BoardLit _Ambient
nt.links.new(shade.outputs[0], amb.inputs[0])

mul = nt.nodes.new("ShaderNodeMixRGB")
mul.blend_type = 'MULTIPLY'
mul.inputs["Fac"].default_value = 1.0
nt.links.new(tex.outputs["Color"], mul.inputs["Color1"])
nt.links.new(amb.outputs[0], mul.inputs["Color2"])

em = nt.nodes.new("ShaderNodeEmission")
em.inputs["Strength"].default_value = 1.0
nt.links.new(mul.outputs["Color"], em.inputs["Color"])

# The albedo's own alpha decides what exists — a mask cut-out has holes, and filling them would be
# inventing geometry the player never sees.
mix = nt.nodes.new("ShaderNodeMixShader")
transp = nt.nodes.new("ShaderNodeBsdfTransparent")
nt.links.new(tex.outputs["Alpha"], mix.inputs["Fac"])
nt.links.new(transp.outputs[0], mix.inputs[1])
nt.links.new(em.outputs[0], mix.inputs[2])
nt.links.new(mix.outputs[0], out.inputs[0])

for ob in meshes:
    ob.data.materials.clear()
    ob.data.materials.append(mat)

# No lamps and a black world: every photon in this picture comes from the emission above.
for o in list(bpy.data.objects):
    if o.type == 'LIGHT':
        bpy.data.objects.remove(o, do_unlink=True)
if scene.world is None:
    scene.world = bpy.data.worlds.new("w")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.0

os.makedirs(os.path.dirname(os.path.abspath(OUT)) or ".", exist_ok=True)
scene.render.filepath = os.path.abspath(OUT)
bpy.ops.render.render(write_still=True)
log("wrote", OUT)
