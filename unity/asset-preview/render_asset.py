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
# GloomhavenVR/BoardLit with _Ambient 0.5 and _LightBoost 0.85 — Lambert against two BAKED
# world-space directions plus an ambient floor:
#
#     shade = 0.5 + saturate(N.key) * 0.85 + saturate(N.fill) * 0.35
#     key   = normalize( 0.35,  0.85, -0.45)      fill = normalize(-0.55, 0.35, 0.30)
#
# ...plus, since ModBuild 248, an OPT-IN Blinn-Phong lobe against those same two directions,
# reached only by passing --mrs. A material with no metallic/roughness pack has _SpecStrength 0 and
# the shader's specular branch does not execute, so leaving --mrs off is not an approximation of
# that case, it IS that case.
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
#       [--mrs <metallic_roughness.png>] [--cull] [--unlit] [--alpha] [--margin 1.06]
#       [--scale 0.42] [--focus-z 0.10]
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
MRS = opt("--mrs", "")           # R = metallic, G = roughness; BoardLit's _MRSMap
CULL = "--cull" in argv          # match the shipped material's _Cull
UNLIT = "--unlit" in argv        # GloomhavenVR/HeadUnlit — the masks
ALPHA_CLIP = "--alpha" in argv   # only for a genuinely cut-out albedo

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
mrs_img = None
if MRS and os.path.exists(MRS):
    mrs_img = bpy.data.images.load(os.path.abspath(MRS))
    log(f"metallic/roughness pack: {os.path.basename(MRS)}")

if NORMAL and os.path.exists(NORMAL):
    normal_img = bpy.data.images.load(os.path.abspath(NORMAL))
    normal_img.colorspace_settings.name = 'Non-Color'
    log(f"normal map: {os.path.basename(NORMAL)}")

mat = bpy.data.materials.new("m_game")
mat.use_nodes = True

# ---- CULL AND BLEND ARE READ OFF THE SHIPPED MATERIAL, NOT GUESSED (ModBuild 247 fix).
#
# THE FIRST CUT OF THIS SCRIPT GOT BOTH WRONG AND THE USER SAW IT: "Die Renderbilder von den
# Masken und Händen sehen kaputt aus ... zB die Finger bei den Händen".
#
#   blend_method = 'BLEND' was the finger fault. Every hand and mask albedo in this bundle is
#   FULLY OPAQUE — alpha is 255 at every one of 2048x2048 texels, measured, on all six — so the
#   Mix-Shader-against-a-Transparent-BSDF this used to build could only ever pass 1.0 and did
#   nothing but put the material in Eevee's BLEND path, where depth writes are off and overlapping
#   geometry sorts per object instead of per pixel. Four fingers in front of a palm is exactly the
#   case that breaks under, and it read as broken fingers because it WAS broken fingers.
#   OPAQUE now, with no alpha term at all; --alpha brings back a CLIP path if a cut-out asset ever
#   needs one, and clip is still not blend.
#
#   Backface culling has to MATCH THE MATERIAL. Every shipped hand material carries `_Cull: 2`
#   (Back) since the artist's plate gauntlet closed the last open shell; every mask material
#   carries `_Cull: 0` (Off), because the mask shells are thin and a backface must not read as a
#   hole. Rendering a hand double-sided draws its inside surfaces through itself.
mat.use_backface_culling = CULL
mat.blend_method = 'CLIP' if ALPHA_CLIP else 'OPAQUE'
nt = mat.node_tree
nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial")

tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = albedo

em = nt.nodes.new("ShaderNodeEmission")
em.inputs["Strength"].default_value = 1.0

if UNLIT:
    # ---- THE MASKS ARE NOT LIT, AND THAT WAS THE SECOND FAULT. They do not run BoardLit at all;
    # they run GloomhavenVR/HeadUnlit, whose whole fragment stage is `albedo * tint`. Its header
    # says why in as many words: the head floats in the light-less VR void and in the mirror, where
    # "any scene-lit shader (Standard, BoardLit's baked rig included) would either render black or
    # add shading the albedo doesn't expect", because that texture already carries its own baked
    # light. Putting BoardLit's Lambert on top of a pre-lit texture is double-shading, and it is why
    # the first mask strip came out dark and muddy against what the player sees.
    nt.links.new(tex.outputs["Color"], em.inputs["Color"])
else:
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
    lit_out = mul.outputs["Color"]

    # ---- SPECULAR (ModBuild 248), and it is here because A PREVIEW THAT OMITS A TERM AGREES
    # WITH EVERY BROKEN BUILD. BoardLit gained an opt-in Blinn-Phong lobe so the plate gauntlet's
    # delivered metallic/roughness maps have a consumer; a strip rendered without it would show
    # the flat grey steel that motivated the shader change in the first place and would keep
    # doing so no matter how the shader was tuned. The arithmetic below is the shader's, verbatim:
    #   rough = max(0.08, mrs.g);  power = exp2((1 - rough) * 9 + 1)
    #   f0    = lerp(0.04, albedo, mrs.r)
    #   spec  = f0 * (pow(saturate(N.Hkey), power) * saturate(N.key) * 0.85
    #                 + pow(saturate(N.Hfill), power) * saturate(N.fill) * 0.35)
    if mrs_img is not None:
        mtex = nt.nodes.new("ShaderNodeTexImage")
        mtex.image = mrs_img
        mtex.image.colorspace_settings.name = 'Non-Color'   # it is data, not colour
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(mtex.outputs["Color"], sep.inputs["Color"])
        metallic, roughness = sep.outputs[0], sep.outputs[1]

        rough = nt.nodes.new("ShaderNodeMath")
        rough.operation = 'MAXIMUM'
        rough.inputs[1].default_value = 0.08
        nt.links.new(roughness, rough.inputs[0])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = 'SUBTRACT'
        inv.inputs[0].default_value = 1.0
        nt.links.new(rough.outputs[0], inv.inputs[1])
        expo = nt.nodes.new("ShaderNodeMath")
        expo.operation = 'MULTIPLY_ADD'
        expo.inputs[1].default_value = 9.0
        expo.inputs[2].default_value = 1.0
        nt.links.new(inv.outputs[0], expo.inputs[0])
        power = nt.nodes.new("ShaderNodeMath")
        power.operation = 'POWER'
        power.inputs[0].default_value = 2.0
        nt.links.new(expo.outputs[0], power.inputs[1])

        # f0 = lerp(dielectric 0.04, albedo, metallic) — metals tint their own highlight.
        f0 = nt.nodes.new("ShaderNodeMixRGB")
        f0.blend_type = 'MIX'
        f0.inputs["Color1"].default_value = (0.04, 0.04, 0.04, 1.0)
        nt.links.new(metallic, f0.inputs["Fac"])
        nt.links.new(tex.outputs["Color"], f0.inputs["Color2"])

        spec = None
        for vec, gain in ((GAME_KEY, 0.85), (GAME_FILL, 0.35)):
            # H = normalize(L + V); Geometry.Incoming is the direction TOWARD the camera.
            half = nt.nodes.new("ShaderNodeVectorMath")
            half.operation = 'ADD'
            half.inputs[1].default_value = vec
            nt.links.new(geo.outputs["Incoming"], half.inputs[0])
            hn = nt.nodes.new("ShaderNodeVectorMath")
            hn.operation = 'NORMALIZE'
            nt.links.new(half.outputs["Vector"], hn.inputs[0])
            nh = nt.nodes.new("ShaderNodeVectorMath")
            nh.operation = 'DOT_PRODUCT'
            nt.links.new(normal_src, nh.inputs[0])
            nt.links.new(hn.outputs["Vector"], nh.inputs[1])
            nhc = nt.nodes.new("ShaderNodeMath")
            nhc.operation = 'MAXIMUM'
            nhc.inputs[1].default_value = 0.0
            nt.links.new(nh.outputs["Value"], nhc.inputs[0])
            lobe = nt.nodes.new("ShaderNodeMath")
            lobe.operation = 'POWER'
            nt.links.new(nhc.outputs[0], lobe.inputs[0])
            nt.links.new(power.outputs[0], lobe.inputs[1])
            # x saturate(N.L) x gain — the same wrap the diffuse pays, so a face turned away
            # from a baked direction cannot grow a highlight out of nothing.
            nl = nt.nodes.new("ShaderNodeVectorMath")
            nl.operation = 'DOT_PRODUCT'
            nl.inputs[1].default_value = vec
            nt.links.new(normal_src, nl.inputs[0])
            nlc = nt.nodes.new("ShaderNodeMath")
            nlc.operation = 'MAXIMUM'
            nlc.inputs[1].default_value = 0.0
            nt.links.new(nl.outputs["Value"], nlc.inputs[0])
            wrap = nt.nodes.new("ShaderNodeMath")
            wrap.operation = 'MULTIPLY'
            nt.links.new(lobe.outputs[0], wrap.inputs[0])
            nt.links.new(nlc.outputs[0], wrap.inputs[1])
            gn = nt.nodes.new("ShaderNodeMath")
            gn.operation = 'MULTIPLY'
            gn.inputs[1].default_value = gain
            nt.links.new(wrap.outputs[0], gn.inputs[0])
            if spec is None:
                spec = gn
            else:
                sa = nt.nodes.new("ShaderNodeMath")
                sa.operation = 'ADD'
                nt.links.new(spec.outputs[0], sa.inputs[0])
                nt.links.new(gn.outputs[0], sa.inputs[1])
                spec = sa

        tint = nt.nodes.new("ShaderNodeMixRGB")
        tint.blend_type = 'MULTIPLY'
        tint.inputs["Fac"].default_value = 1.0
        nt.links.new(f0.outputs["Color"], tint.inputs["Color1"])
        nt.links.new(spec.outputs[0], tint.inputs["Color2"])
        add = nt.nodes.new("ShaderNodeMixRGB")
        add.blend_type = 'ADD'
        add.inputs["Fac"].default_value = 1.0
        nt.links.new(lit_out, add.inputs["Color1"])
        nt.links.new(tint.outputs["Color"], add.inputs["Color2"])
        lit_out = add.outputs["Color"]

    nt.links.new(lit_out, em.inputs["Color"])

if ALPHA_CLIP:
    mix = nt.nodes.new("ShaderNodeMixShader")
    transp = nt.nodes.new("ShaderNodeBsdfTransparent")
    nt.links.new(tex.outputs["Alpha"], mix.inputs["Fac"])
    nt.links.new(transp.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])
else:
    nt.links.new(em.outputs[0], out.inputs[0])

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
