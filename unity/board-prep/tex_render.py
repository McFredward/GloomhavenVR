"""tex_render.py -- preview stations for the board textures. TWO of them.

    # THE ONE THAT SHIPS -- reproduces BoardLit.shader, node for line
    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/tex_render.py -- --shipped \
        --tex unity/GloomhavenVR.Assets/Assets/Bundle/Table --style oak \
        --fbx unity/GloomhavenVR.Assets/Assets/Bundle/Table/PlayTray_prepped.fbx \
        --view flat --out unity/board-prep/out/renders/oak_shipped.png

    # the PBR instrument -- Principled BSDF, calibrated against a grey card
    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/tex_render.py -- \
        --tex unity/board-prep/out/tex --style oak \
        --out unity/board-prep/out/renders/oak_material.png

WHICH ONE ANSWERS WHICH QUESTION
--------------------------------
`--shipped` answers "what will the user SEE". It is BoardLit and nothing else:
diffuse-only lambert against two BAKED world directions plus an ambient floor,
then an additive Blinn-Phong against the same two directions. No environment,
no image-based lighting, no Principled BSDF, no auto-exposure. Every number it
prints is directly comparable with a Unity render of the same prefab, and the
docstring of `shipped_material()` states the correspondence term by term.

The default (Principled) path answers "are these maps good PBR DATA". That is
still a real question -- a roughness map that is wrong is wrong under any
shader -- but it is NOT the question "does the board look right", and four
rounds of this rebuild were judged by the wrong one. Measured, on the material
that shipped: the game binds `_MRSMap` in the BoardLit packing (R=metallic,
G=roughness) and the Principled path here binds `<base>_mr.png` in the glTF ORM
packing (R=occlusion, G=roughness, B=metallic). They are not the same file and
they are not the same packing. `--shipped` reads `<base>_mrs.png`; the
Principled path reads `<base>_mr.png`; neither silently substitutes the other.

WHY THE PRINCIPLED PATH STILL CALIBRATES AGAINST A GREY CARD
------------------------------------------------------------
gen_render.py IS OVEREXPOSED, measured: its three area lights are 260/120/90 W
aimed at a 0.64 m object with a 0.6-strength world on top, and the finished oak
atlas comes back with its lit surface at median 0.87. Every material looks like
white plastic through it. The card in the corner of the Principled render is
what proves that station is not doing the same thing. `--shipped` has no card
because it has no exposure to get wrong: the shader's numbers ARE the exposure.
"""

import math
import os
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]


def arg(name, default=None):
    if name in argv:
        return argv[argv.index(name) + 1]
    return default


def farg(name, default):
    return float(arg(name, default))


SHIPPED = "--shipped" in argv
TEX = arg("--tex", "unity/board-prep/out/tex")
STYLE = arg("--style", "oak")
OUT = arg("--out", f"unity/board-prep/out/renders/{STYLE}_material.png")
REGION = arg("--region", "face")
VIEW = arg("--view", "flat")
GREY_TARGET = 0.466            # sRGB value of an 18% linear grey card

# ---- BoardLit's shipped values. Defaults are read off PlayTray*.mat, which is
# the file the bundle carries; every one is overridable so a proposed material
# change can be rendered before it is made.
AMBIENT = farg("--ambient", 0.5)
LIGHT_BOOST = farg("--light-boost", 0.85)
FILL_WEIGHT = farg("--fill-weight", 0.35)      # hard-coded 0.35 in the shader
NORMAL_STRENGTH = farg("--normal-strength", 1.0)
SPEC_STRENGTH = farg("--spec-strength", 0.85)
ROUGH_FLOOR = farg("--rough-floor", 0.08)      # BoardLit's own max(0.08, mr.g)

# BoardLit's two baked studio directions, IN UNITY WORLD SPACE, copied verbatim
# from the shader source. Never a scene light -- see the shader's header.
KEY_U = (0.35, 0.85, -0.45)
FILL_U = (-0.55, 0.35, 0.30)

# Unity's camera poses from Assets/Editor/PreviewBoard.cs Shoot(), so a
# `--shipped` render can be held against a Unity render of the same prefab
# without a framing argument in the middle of it. (pos, lookat, w, h), Unity.
VIEWS = {
    "flat":  ((0.0, 0.0, -0.98), (0.0, 0.0, 0.0), 1800, 1000),
    "rake":  ((0.20, 0.26, -0.55), (0.06, -0.01, 0.0), 1600, 1000),
    "seats": ((0.225, 0.0, -0.30), (0.225, 0.0, 0.0), 1000, 1200),
}
UNITY_FOV_Y = 35.0             # PreviewBoard sets Camera.fieldOfView = 35 (vertical)
UNITY_CLEAR = (0.08, 0.08, 0.09)   # PreviewBoard's Camera.backgroundColor, sRGB
BG_EXCLUDE = 0.16              # a pixel is BOARD if max(RGB) > this. Unity's
                               # clear colour is (0.08,0.08,0.09); this render's
                               # is black. Either way the board clears it.

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
# UNITY -> BLENDER, once, here, and nowhere else.
#
# BoardLit's key and fill are UNITY world vectors and the shader dots them
# against a UNITY world normal. Blender is Z-up and right-handed where Unity is
# Y-up and left-handed, so a render that drops those two triples into a Blender
# scene unconverted is lighting the board from somewhere else and cannot be
# compared with anything.
#
# The map used is the coordinate PERMUTATION (x, y, z)_unity -> (x, z, y)_blender.
# It is a transposition, so its determinant is -1, which is exactly what turns a
# left-handed frame into a right-handed one -- the picture comes out the same way
# round as Unity's rather than mirrored. And because it permutes every vector
# identically, it preserves every dot product in the shader by construction: the
# lighting term is numerically the same number Unity computes, not an analogue
# of it.
# --------------------------------------------------------------------------

def u2b(v):
    """A Unity vector/point in Blender coordinates."""
    return Vector((v[0], v[2], v[1]))


KEY = u2b(KEY_U).normalized()
FILL = u2b(FILL_U).normalized()


# --------------------------------------------------------------------------
# geometry: the real board, or a slab at the board's real size with known UVs
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
FBX = arg("--fbx")

if FBX:
    # Render the atlas onto the REAL BOARD. In `--shipped` mode this is the only
    # honest option: the shading term is dot(N, key), and a slab has one normal
    # where the board has recess walls, fillets and a moulding.
    bpy.ops.import_scene.fbx(filepath=FBX)
    meshes = [o for o in sc.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit(f"{FBX} imported no mesh -- the station would be aimed at nothing")
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (lo + hi) / 2
    ext = hi - lo
    for o in meshes:
        o.location = (o.location.x - ctr.x, o.location.y - ctr.y, o.location.z - ctr.z)
    board = meshes[0]
    BOARD_W, BOARD_H = float(ext.x), float(ext.y)

    # WHICH SIDE IS DECORATED -- measured, and the measurement is stated,
    # because two earlier versions of this got it wrong in ways that were very
    # easy to explain away.
    #
    # VERSION 1 flipped the object's LOCAL z scale when the bounding box looked
    # lopsided. Its test was (hi.z - ctr.z) < (ctr.z - lo.z), which is the same
    # quantity on both sides algebraically and is therefore decided by float
    # rounding; and the FBX arrives already rotated 90 deg about X, so local z
    # is world MINUS Y and scaling it by -1 mirrored the board top to bottom.
    # It rendered with the short-rest glyph on the long-rest pad.
    #
    # VERSION 2 (this file, one hour ago) picked the side with MORE FLAT AREA,
    # on the argument that recesses add area. Measured on PlayTray_prepped.fbx:
    # +Z 2015 cm2 against -Z 2011 cm2 -- a 0.2% margin, i.e. a coin toss. It is
    # a coin toss because a recess REPLACES top-plane area with floor area of
    # very nearly the same size; the walls are what is added, and they are not
    # flat-facing so neither total counts them.
    #
    # WHAT ACTUALLY SEPARATES THE TWO SIDES is that one of them has pockets cut
    # INTO it. So: of the area facing +Z, how much sits BELOW the topmost plane;
    # of the area facing -Z, how much sits ABOVE the bottom-most one. Same mesh:
    # +Z 1905 cm2 recessed against -Z 0 cm2. That is not a margin, it is a
    # verdict, and both numbers are printed so a wrong call is visible in the
    # log rather than only in a picture somebody then has to disbelieve.
    up = down = up_recessed = down_recessed = 0.0
    for o in meshes:
        mw = o.matrix_world
        rot = mw.to_quaternion()
        me = o.data
        for p in me.polygons:
            nz = (rot @ p.normal).z
            cz = (mw @ p.center).z
            if nz > 0.7:
                up += p.area
                if cz < hi.z - 0.002:
                    up_recessed += p.area
            elif nz < -0.7:
                down += p.area
                if cz > lo.z + 0.002:
                    down_recessed += p.area
    print(f"PREVIEW mesh {os.path.basename(FBX)}: {len(meshes)} object(s), extents "
          f"{ext.x:.3f} x {ext.y:.3f} x {ext.z:.3f} m; flat area +Z {up * 1e4:.0f} cm2 "
          f"(of which {up_recessed * 1e4:.0f} cm2 recessed) vs -Z {down * 1e4:.0f} cm2 "
          f"(of which {down_recessed * 1e4:.0f} cm2 recessed)")
else:
    board = slab("Board", BOARD_W, BOARD_H, REGIONS.get(REGION, REGIONS["face"]))
    meshes = [board]


if SHIPPED and FBX:
    # Lay the board the way the bundle contract says it sits: decorated face
    # toward Unity -Z, i.e. Blender -Y, with the viewer in front of it.
    #
    # THE ROTATION GOES ON A PARENT EMPTY, and that is not tidiness. Writing
    # `o.rotation_euler = (90 deg, 0, 0)` on the imported object -- which the
    # first draft of this block did -- is wrong twice over. The FBX importer has
    # ALREADY set exactly that value to convert Y-up to Z-up, so the assignment
    # was a no-op: the board stayed edge-on to the camera and rendered as a
    # 0.64 x 0.036 m strip covering 4.2% of the frame while still reporting an
    # oak-coloured mean, which is exactly the kind of number that gets believed.
    # And even a correct DELTA there would rotate about the object's own origin,
    # which is not where the centring above put the board's middle.
    #
    # A parent at the world origin rotates the already-centred board about the
    # point it was centred on, and composes with the importer's conversion
    # instead of overwriting it. It is a RIGID ROTATION -- it cannot mirror a
    # glyph, which is the failure mode the note above records.
    decorated_plus_z = up_recessed >= down_recessed
    deg = 90.0 if decorated_plus_z else -90.0
    print(f"PREVIEW decorated side reads as {'+Z' if decorated_plus_z else '-Z'} "
          f"(the side with the pockets cut into it: {up_recessed * 1e4:.0f} vs "
          f"{down_recessed * 1e4:.0f} cm2 recessed); rotating the board "
          f"{deg:+.0f} deg about world X so it faces the camera at Unity -Z")
    pivot = bpy.data.objects.new("BoardPivot", None)
    sc.collection.objects.link(pivot)
    for o in meshes:
        if o.parent is None:
            o.parent = pivot
            o.matrix_parent_inverse = pivot.matrix_world.inverted()
    pivot.rotation_euler = (math.radians(deg), 0.0, 0.0)
    bpy.context.view_layer.update()

    # ...and PROVE it landed, rather than asserting it. After the rotation the
    # decorated face must point at -Y and the board must present its 0.640 x
    # 0.320 m face to the camera, not its 0.036 m edge.
    pts2 = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    e2 = (max(p.x for p in pts2) - min(p.x for p in pts2),
          max(p.y for p in pts2) - min(p.y for p in pts2),
          max(p.z for p in pts2) - min(p.z for p in pts2))
    print(f"PREVIEW after the rotation the board presents {e2[0]:.3f} x {e2[2]:.3f} m "
          f"to the camera and is {e2[1]:.3f} m deep")
    if e2[1] > min(e2[0], e2[2]):
        raise SystemExit("PREVIEW the board is edge-on to the camera -- the "
                         "orientation is wrong and nothing measured off this "
                         "render would mean anything")


# the 18% grey card, same plane, to the side. It is what calibrates the exposure
# AND what proves in the output that the calibration held. The shipped station
# has no exposure to calibrate, so it does not get one -- and a card in the shot
# would land inside the board-pixel mask and corrupt every statistic below.
CARD = 0.10
card = None
if not SHIPPED:
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


alb = img(os.path.join(TEX, f"{BASE}_albedo.png"), False)
nrm = img(os.path.join(TEX, f"{BASE}_normal.png"), True)
PACK = "mrs" if SHIPPED else "mr"
mr = img(os.path.join(TEX, f"{BASE}_{PACK}.png"), True)
missing = [n for n, v in (("albedo", alb), ("normal", nrm), (PACK, mr)) if v is None]
if alb is None:
    raise SystemExit(f"no albedo at {TEX}/{BASE}_albedo.png -- nothing to look at")
if missing:
    print(f"PREVIEW NOTE missing maps: {missing}")
if SHIPPED and mr is None:
    # Refuse rather than render a plausible lie. Without _mrs.png the specular
    # term is a guess, and a guessed highlight is precisely the thing four
    # rounds of this rebuild were judged against.
    raise SystemExit(f"--shipped needs {TEX}/{BASE}_mrs.png (BoardLit _MRSMap: "
                     f"R=metallic, G=roughness). Not rendering without it.")

mat = bpy.data.materials.new("BoardMat")
mat.use_nodes = True
nt = mat.node_tree


def tex_node(image, label):
    n = nt.nodes.new("ShaderNodeTexImage")
    n.image = image
    n.label = label
    return n


def vmath(op, a=None, b=None, scale=None):
    n = nt.nodes.new("ShaderNodeVectorMath")
    n.operation = op
    if a is not None:
        (nt.links.new(n.inputs[0], a) if hasattr(a, "node")
         else n.inputs[0].default_value.__setitem__(slice(None), a))
    if b is not None:
        (nt.links.new(n.inputs[1], b) if hasattr(b, "node")
         else n.inputs[1].default_value.__setitem__(slice(None), b))
    if scale is not None:
        if hasattr(scale, "node"):
            nt.links.new(n.inputs["Scale"], scale)
        else:
            n.inputs["Scale"].default_value = scale
    return n


def smath(op, a=None, b=None, c=None):
    n = nt.nodes.new("ShaderNodeMath")
    n.operation = op
    for i, v in ((0, a), (1, b), (2, c)):
        if v is None:
            continue
        if hasattr(v, "node"):
            nt.links.new(n.inputs[i], v)
        else:
            n.inputs[i].default_value = v
    return n


def shipped_material():
    """BoardLit.shader, as a node graph. Line for line:

        fixed4 alb  = tex2D(_MainTex, uv) * _Color;
        float3 nt   = UnpackNormal(tex2D(_BumpMap, uv));  nt.xy *= _NormalStrength;
        float3 N    = normalize(tangent-space transform of nt);
        float  lit  = saturate(dot(N,key))*_LightBoost + saturate(dot(N,fill))*0.35;
        float3 col  = alb.rgb * (_Ambient + lit);
        if (_SpecStrength > 0) {
            float  rough = max(0.08, _MRSMap.g);
            float  power = exp2((1-rough)*9 + 1);
            float3 f0    = lerp(0.04, alb.rgb, _MRSMap.r);
            float3 V     = normalize(_WorldSpaceCameraPos - wp);
            col += f0 * ( pow(saturate(dot(N,normalize(key+V))), power)*saturate(dot(N,key))*_LightBoost
                        + pow(saturate(dot(N,normalize(fill+V))),power)*saturate(dot(N,fill))*0.35 )
                      * _SpecStrength;
        }

    Correspondences that are easy to get wrong and are therefore stated:
      * UnpackNormal + the tangent transform IS Blender's Normal Map node. Both
        are OpenGL convention (+Y up in texture space); the maps this pipeline
        writes are unit length, so Unity reconstructing z and Blender reading it
        give the same vector.
      * `_WorldSpaceCameraPos - wp`, normalized, IS the Geometry node's
        `Incoming` for a camera ray.
      * the result goes into an EMISSION shader, so the render engine adds
        nothing of its own. There is no light and no world in this scene; a
        pixel is the shader's arithmetic and nothing else.
      * albedo is sampled as sRGB and the frame is written through the Standard
        view transform, which is the same sRGB-in / linear-math / sRGB-out that
        Unity does with an sRGB _MainTex and an 8-bit target.
    """
    na = tex_node(alb, "albedo (sRGB)")
    # _Color is (1,1,1,1) on all three PlayTray*.mat, so `alb * _Color` is the
    # identity and there is no tint node here. If a mat ever carries a tint this
    # is where it goes -- and the render would be wrong until it does.
    a_col = na.outputs["Color"]

    nn = tex_node(nrm, "normal (linear)")
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nmap.inputs["Strength"].default_value = NORMAL_STRENGTH
    nt.links.new(nmap.inputs["Color"], nn.outputs["Color"])
    N = nmap.outputs["Normal"]

    nm = tex_node(mr, "MRS (linear)")
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(sep.inputs["Color"], nm.outputs["Color"])
    metal, rough_raw = sep.outputs["Red"], sep.outputs["Green"]

    geo = nt.nodes.new("ShaderNodeNewGeometry")
    V = geo.outputs["Incoming"]

    ndk = smath("MAXIMUM", vmath("DOT_PRODUCT", N, tuple(KEY)).outputs["Value"], 0.0)
    ndf = smath("MAXIMUM", vmath("DOT_PRODUCT", N, tuple(FILL)).outputs["Value"], 0.0)

    lit = smath("ADD",
                smath("MULTIPLY", ndk.outputs[0], LIGHT_BOOST).outputs[0],
                smath("MULTIPLY", ndf.outputs[0], FILL_WEIGHT).outputs[0])
    shade = smath("ADD", lit.outputs[0], AMBIENT)
    col = vmath("SCALE", a_col, scale=shade.outputs[0])

    if SPEC_STRENGTH > 0.0:
        rough = smath("MAXIMUM", rough_raw, ROUGH_FLOOR)
        power = smath("POWER", 2.0,
                      smath("MULTIPLY_ADD", smath("SUBTRACT", 1.0, rough.outputs[0]).outputs[0],
                            9.0, 1.0).outputs[0])
        lobe = []
        for d, ndl, w in ((KEY, ndk, LIGHT_BOOST), (FILL, ndf, FILL_WEIGHT)):
            H = vmath("NORMALIZE", vmath("ADD", tuple(d), V).outputs["Vector"])
            nh = smath("MAXIMUM", vmath("DOT_PRODUCT", N, H.outputs["Vector"]).outputs["Value"], 0.0)
            t = smath("POWER", nh.outputs[0], power.outputs[0])
            t = smath("MULTIPLY", t.outputs[0], ndl.outputs[0])
            lobe.append(smath("MULTIPLY", t.outputs[0], w))
        spec_scalar = smath("ADD", lobe[0].outputs[0], lobe[1].outputs[0])
        # f0 = lerp(0.04, albedo, metal), done as 0.04 + (albedo - 0.04)*metal so
        # no Mix node's socket ordering can silently reverse it.
        f0 = vmath("ADD",
                   vmath("SCALE",
                         vmath("SUBTRACT", a_col, (0.04, 0.04, 0.04)).outputs["Vector"],
                         scale=metal).outputs["Vector"],
                   (0.04, 0.04, 0.04))
        spec = vmath("SCALE", f0.outputs["Vector"], scale=spec_scalar.outputs[0])
        spec = vmath("SCALE", spec.outputs["Vector"], scale=SPEC_STRENGTH)
        col = vmath("ADD", col.outputs["Vector"], spec.outputs["Vector"])

    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = 1.0
    nt.links.new(em.inputs["Color"], col.outputs["Vector"])
    out = nt.nodes["Material Output"]
    nt.links.new(out.inputs["Surface"], em.outputs["Emission"])
    for n in list(nt.nodes):
        if n.type == "BSDF_PRINCIPLED":
            nt.nodes.remove(n)


def principled_material():
    bsdf = nt.nodes["Principled BSDF"]
    na = tex_node(alb, "albedo")
    nt.links.new(bsdf.inputs["Base Color"], na.outputs["Color"])
    if nrm is not None:
        nn = tex_node(nrm, "normal")
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nmap.inputs["Color"], nn.outputs["Color"])
        nt.links.new(bsdf.inputs["Normal"], nmap.outputs["Normal"])
    if mr is not None:
        nm = tex_node(mr, "ORM")
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(sep.inputs["Color"], nm.outputs["Color"])
        # glTF ORM packing -- R = occlusion, G = roughness, B = metallic. This is
        # NOT BoardLit's packing and this path is NOT what ships.
        nt.links.new(bsdf.inputs["Roughness"], sep.outputs["Green"])
        nt.links.new(bsdf.inputs["Metallic"], sep.outputs["Blue"])
    else:
        bsdf.inputs["Roughness"].default_value = 0.55


(shipped_material if SHIPPED else principled_material)()

for o in meshes:
    o.data.materials.clear()
    o.data.materials.append(mat)

if card is not None:
    cmat = bpy.data.materials.new("GreyCard")
    cmat.use_nodes = True
    cb = cmat.node_tree.nodes["Principled BSDF"]
    cb.inputs["Base Color"].default_value = (0.18, 0.18, 0.18, 1.0)   # 18% LINEAR
    cb.inputs["Roughness"].default_value = 0.60
    cb.inputs["Metallic"].default_value = 0.0
    cb.inputs["Specular IOR Level"].default_value = 0.5
    card.data.materials.append(cmat)


# --------------------------------------------------------------------------
# camera + lights
# --------------------------------------------------------------------------

sc.render.image_settings.file_format = "PNG"
sc.render.film_transparent = False
# Standard, NOT Filmic/AgX. In the shipped station a tone curve would mean the
# render is not the shader; in the Principled one it would make the grey-card
# calibration meaningless.
sc.view_settings.view_transform = "Standard"
sc.view_settings.look = "None"
sc.view_settings.exposure = 0.0

if SHIPPED:
    pos_u, look_u, RW, RH = VIEWS.get(VIEW, VIEWS["flat"])
    pos, look = u2b(pos_u), u2b(look_u)
    cd = bpy.data.cameras.new("C")
    cd.type = "PERSP"
    # Unity's Camera.fieldOfView is VERTICAL. Blender's default sensor fit is
    # AUTO, which fits the LONGER axis -- horizontal for a 1800x1000 frame -- so
    # leaving it would silently render a wider shot than Unity's.
    cd.sensor_fit = "VERTICAL"
    cd.angle_y = math.radians(UNITY_FOV_Y)
    cd.clip_start = 0.01
    cam = bpy.data.objects.new("C", cd)
    sc.collection.objects.link(cam)
    cam.location = pos
    # up = Unity +Y = Blender +Z, which is what to_track_quat tracks to.
    cam.rotation_euler = (look - pos).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam

    # NO LIGHTS. The surface is an Emission shader carrying the shader's own
    # arithmetic and nothing in this scene may add to it. (The world below CAN
    # only light a diffuse surface, and there is none: an Emission shader
    # receives nothing.)
    #
    # THE BACKGROUND IS UNITY'S CLEAR COLOUR, and that is a measurement fix, not
    # decoration. PreviewBoard sets Camera.backgroundColor = (0.08,0.08,0.09).
    # With a BLACK background here the board's antialiased rim blends toward 0
    # instead of toward 0.08, and the rim pixels that survive the max(RGB)>0.16
    # board mask are exactly the darkest pixels in the frame -- which is a
    # statistic about the BACKGROUND wearing a board's name. Measured: steel has
    # no dark texel anywhere in its albedo, and this station still reported
    # min 0.145 against Unity's 0.325 until the clear colour matched.
    w = bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    bg = np.array(UNITY_CLEAR, dtype=np.float64)
    bg = np.where(bg <= 0.04045, bg / 12.92, ((bg + 0.055) / 1.055) ** 2.4)
    w.node_tree.nodes["Background"].inputs[0].default_value = (*bg, 1.0)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.0

    sc.render.engine = "CYCLES"
    sc.cycles.samples = 64          # emission is noiseless; these are AA samples,
    sc.cycles.use_denoising = False  # and they stand in for Unity's mip filtering
    sc.cycles.use_adaptive_sampling = False
    sc.render.resolution_x = RW
    sc.render.resolution_y = RH
else:
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


# --------------------------------------------------------------------------
# render + read back
# --------------------------------------------------------------------------

LUMA = np.array([0.2126, 0.7152, 0.0722])


def render_to(path):
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


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


os.makedirs(os.path.dirname(OUT) or ".", exist_ok=True)

def erode(mask, r):
    """Binary erosion by a (2r+1)^2 square, in numpy. Used on the board mask
    before any statistic is taken off it -- see the note at the call site."""
    m = np.asarray(mask, dtype=bool)
    for _ in range(int(r)):
        m = (m & np.roll(m, 1, 0) & np.roll(m, -1, 0)
             & np.roll(m, 1, 1) & np.roll(m, -1, 1))
    return m


if SHIPPED:
    render_to(OUT)
    px = read_png(OUT)[:, :, :3]
    mx = px.max(axis=2)
    # THE MASK IS ERODED, and that is the difference between a statistic about
    # the board and a statistic about the mask's own threshold.
    #
    # Unity's PreviewBoard shot has no MSAA, so every pixel there is either
    # entirely board or entirely clear colour and `min` is a real material
    # extreme. Cycles antialiases, so the board's rim is a ramp from the clear
    # colour up to the material; a rim pixel about 15% covered lands just above
    # max(RGB) > 0.16 and gets counted, and its darkest channel then IS the
    # reported minimum. Measured, before this erosion: steel came back with
    # min 0.141 against Unity's 0.325, from an albedo whose DARKEST palette stop
    # is 0x5F646B (0.373 sRGB) -- i.e. the number was arithmetically impossible
    # for the material and was reporting the background.
    #
    # Two texels of erosion removes the ramp. WHAT IT CANNOT SEE: any genuinely
    # dark feature that is only one or two pixels from the silhouette, e.g. the
    # very outermost dentil of the moulding. That is a handful of pixels out of
    # half a million and it is a loss of extremes only -- the mean and the
    # percentiles below are unaffected either way.
    board_px = erode(mx > BG_EXCLUDE, 2)
    if not board_px.any():
        print("PREVIEW SHIPPED: no pixel above the background threshold -- the "
              "station is aimed at nothing, do not judge anything from this render")
    else:
        b = px[board_px]
        p05, p95 = np.percentile(mx[board_px], (5, 95))
        print(f"PREVIEW SHIPPED {STYLE} {VIEW}: board pixels {board_px.mean() * 100:.1f}% "
              f"of the frame ({int(board_px.sum())} px)")
        print(f"PREVIEW SHIPPED {STYLE} {VIEW} sRGB mean "
              f"[{b[:, 0].mean():.3f} {b[:, 1].mean():.3f} {b[:, 2].mean():.3f}]  "
              f"min {b.min():.3f}  max {b.max():.3f}  "
              f"p95-p05 of max(RGB) = {p95 - p05:.3f}")
        lin = srgb_to_linear(b) @ LUMA
        print(f"PREVIEW SHIPPED {STYLE} {VIEW} linear luma mean {lin.mean():.3f} "
              f"p05 {np.percentile(lin, 5):.3f} p50 {np.percentile(lin, 50):.3f} "
              f"p95 {np.percentile(lin, 95):.3f}  "
              f"range {np.percentile(lin, 95) / max(np.percentile(lin, 5), 1e-9):.2f}x  "
              f"clipped {100 * (mx[board_px] >= 0.999).mean():.2f}%")
    print("WROTE", OUT)
else:
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
    # Blender's `exposure` scales LINEAR light while the card reads out through
    # the sRGB encode, so a one-shot correction computed in the wrong space
    # lands short -- the first version of this did exactly that and reported
    # 0.539 for a 0.466 target while claiming it had corrected. Converging on
    # the number cannot make that mistake, whichever space the arithmetic is in.
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
