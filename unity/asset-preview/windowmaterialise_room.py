# windowmaterialise_room.py - photograph the window materialise/dematerialise effect AS REAL
# GEOMETRY IN A ROOM, with Blender 4.2 / BLENDER_EEVEE_NEXT.
#
# WHY THIS EXISTS. The first design painted the debris onto a flat quad in the window's own plane
# and the user called it "eher ein 2D-Effekt". It is now a few hundred tetrahedral shards flying
# THROUGH the room, so the deliverable is no longer a dead-on strip: a dead-on strip of real 3D
# debris and a dead-on strip of a decal look identical. What separates them is parallax and
# occlusion, so this script renders
#   - a clean dead-on strip (no room)                       front_<dir>_%04d.png
#   - an orbited view with occluders in front of AND behind
#     the window plane                                      oblique_<dir>_%04d.png
#   - a parallel stereo pair at one instant                 stereo_L.png / stereo_R.png
#   - a wide "lean sideways" pair at the same instant       parallax_A.png / parallax_B.png
#
# NOTHING FACES THE CAMERA. The user ruled the effect must not be bound to head movement, so the
# shards are plain world-space geometry and the window is a fixed plane; every camera here is just
# a different place to stand, and the geometry is byte-identical in all of them.
#
# RUN (windowmaterialise_preview.py writes sim/ and win/ first):
#   xvfb-run -a /home/claw/blender-4.2/blender --background \
#       --python unity/asset-preview/windowmaterialise_room.py -- render/windowmaterialise
import json
import math
import os
import sys
import time

import bpy
import mathutils
import numpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "render/windowmaterialise")
SIM = os.path.join(OUT, "sim")
FRAMES = os.path.join(OUT, "frames")

RES_X, RES_Y = 1100, 740
SAMPLES = 24
# 22 mm, NOT the 30 mm this script was first specified with, and the reason is a measurement rather
# than taste: 30 mm at 0.90 m frames 1.08 m of the window plane, and the debris reaches x = +0.88 m,
# so from about vanish f30 the cloud was clipped at the right border of the clean front strip. A
# strip that cannot show where the debris went is not a strip of this effect. 22 mm frames ~1.47 m
# and holds the whole flight. The DISTANCE stays at the real 0.90 m reading distance; widening the
# lens rather than backing the camera off keeps the window at its true angular size.
LENS_MM = 22.0
# Smoke-test knob only: WM_ROOM_STRIDE=8 renders every 8th animation frame so the whole pipeline
# (including the stereo/parallax pairs at the end) can be exercised in seconds. Default 1 = all.
STRIDE = max(1, int(os.environ.get("WM_ROOM_STRIDE", "1")))

# --- the shipped unlit shard shader, verbatim -------------------------------------------------
#   n = normalize(world normal); L = normalize(_KeyDir);
#   d = dot(n, L);
#   shade = _Ambient + _Key*saturate(d) + _Fill*saturate(-d);
#   rgb   = _Tint.rgb * shade * perShardShade;
# _KeyDir is (0.42, 0.78, -0.46) in UNITY world coords; Unity's +y (up) is Blender's +z and Unity's
# +z (forward/away) is Blender's +y, so in THESE axes it is (0.42, -0.46, 0.78).
TINT = (0.80, 0.74, 0.62)
AMBIENT, KEY, FILL = 0.34, 0.78, 0.20
_kd = (0.42, -0.46, 0.78)
_kl = math.sqrt(sum(c * c for c in _kd))
KEYDIR = tuple(c / _kl for c in _kd)

# --- camera rig -------------------------------------------------------------------------------
# Azimuth 0 is dead-on from -y; positive azimuth walks to the RIGHT, which is the side the wind
# blows the debris toward, so the occluders and the depth cameras are on the same side as the flow.
OBLIQUE = dict(az=52.0, el=14.0, dist=1.15, target=(0.10, 0.0, 0.0))
STEREO = dict(az=22.0, el=10.0, dist=0.95, target=(0.10, 0.0, 0.0))
IPD_M = 0.063
PARALLAX_AZ = 17.5      # +-0.28 m laterally at 0.95 m => 0.56 m apart
PARALLAX = dict(el=10.0, dist=0.95, target=(0.0, 0.0, 0.0))
# The stereo and parallax pairs are one instant of the vanish, as a fraction of its duration.
#
# 0.30, NOT the 0.45 this script was first specified with, and the measurement is the reason: the
# ELEMENT front now completes in the first ElementSpan = 0.58 of the duration, so by k = 0.45 the
# window's own dissolve is 77 % done (mean texture alpha 0.09) and by 0.50 it is gone entirely. A
# pair shot there proves debris-against-the-ROOM, which is a real depth proof but a different
# sentence from the one these pairs exist to say. At 0.30 the window is present and half dissolved,
# and the same shard cluster sits over visibly different parts of its content between A and B --
# which is the picture that makes the point. Override with WM_PAIR_T.
PAIR_T = float(os.environ.get("WM_PAIR_T", "0.30"))


def fail(msg):
    raise SystemExit("windowmaterialise_room: " + msg)


def load_inputs():
    meta_path = os.path.join(SIM, "meta.json")
    if not os.path.exists(meta_path):
        fail("missing %s - run windowmaterialise_preview.py first" % meta_path)
    meta = json.load(open(meta_path))
    faces_path = os.path.join(SIM, "faces.npy")
    if not os.path.exists(faces_path):
        fail("missing %s" % faces_path)
    faces = numpy.load(faces_path)
    data = {}
    for d in ("appear", "vanish"):
        vp = os.path.join(SIM, "%s_verts.npy" % d)
        sp = os.path.join(SIM, "%s_shade.npy" % d)
        for p in (vp, sp):
            if not os.path.exists(p):
                fail("missing %s" % p)
        verts = numpy.load(vp)
        shade = numpy.load(sp)
        if verts.shape[1] != shade.shape[0]:
            fail("%s: %d verts but %d shade values" % (d, verts.shape[1], shade.shape[0]))
        data[d] = (verts, shade)
    for d, dd in meta["directions"].items():
        for fr in dd["frames"]:
            wp = os.path.join(OUT, fr["window"])
            if not os.path.exists(wp):
                fail("missing window frame %s" % wp)
    return meta, faces, data


# --- scene ------------------------------------------------------------------------------------
def new_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x = RES_X
    sc.render.resolution_y = RES_Y
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"   # the sim is already display-referred sRGB
    sc.view_settings.look = "None"
    sc.eevee.taa_render_samples = SAMPLES
    # Screen-space GI at 24 samples is pure grain on the big flat wall, and it buys nothing here -
    # the room only has to read as solid geometry. Off, and cheaper.
    if hasattr(sc.eevee, "use_raytracing"):
        sc.eevee.use_raytracing = False
    # The default light_threshold (0.01) computes an influence RADIUS for the key and cuts it off
    # dead - on a 10 m wall that draws a visible arc across the render. Drop it so the falloff runs
    # off the edge of frame instead.
    if hasattr(sc.eevee, "light_threshold"):
        sc.eevee.light_threshold = 0.0005
    # More shadow rays, fewer TAA samples spent resolving speckle in the crate's shadow.
    if hasattr(sc.eevee, "shadow_ray_count"):
        sc.eevee.shadow_ray_count = 4
    if hasattr(sc.eevee, "shadow_step_count"):
        sc.eevee.shadow_step_count = 8
    if hasattr(sc.eevee, "shadow_resolution_scale"):
        sc.eevee.shadow_resolution_scale = 1.0
    world = bpy.data.worlds.new("w")
    sc.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.012, 0.013, 0.017, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    return sc


def set_transparency(mat):
    # Blender 4.2 EEVEE Next replaced blend_method with surface_render_method. We need DITHERED
    # (stochastic) and NOT blended: a blended surface writes no depth and is composited over the
    # opaque pass, which would draw the window OVER every shard including the ones in FRONT of it -
    # i.e. it would destroy the exact thing these renders exist to prove. Dithered alpha writes
    # depth, so the sort is correct, and 24 TAA samples resolve the dither (the window's alpha is
    # near-binary anyway).
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"
    if hasattr(mat, "blend_method"):
        try:
            mat.blend_method = "BLEND"
        except (TypeError, AttributeError):
            pass
    if hasattr(mat, "use_transparent_shadow"):
        mat.use_transparent_shadow = False


def make_window(sc, first_img_path, w_m, h_m):
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0, 0, 0))
    ob = bpy.context.object
    ob.name = "window"
    # SCALE IN XY *BEFORE* ROTATING. The plane is still in XY here, so height belongs on the Y
    # component; putting it on Z scales a zero axis and every frame renders 1.0 m tall. The old
    # script recorded that trap after hitting it.
    ob.scale = (w_m, h_m, 1.0)
    ob.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(scale=True, rotation=True)
    ob.visible_shadow = False

    mat = bpy.data.materials.new("window")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    mix = nt.nodes.new("ShaderNodeMixShader")
    trans = nt.nodes.new("ShaderNodeBsdfTransparent")
    emit = nt.nodes.new("ShaderNodeEmission")
    tex = nt.nodes.new("ShaderNodeTexImage")
    img = bpy.data.images.load(first_img_path)
    img.colorspace_settings.name = "sRGB"
    tex.image = img
    tex.interpolation = "Cubic"
    tex.extension = "CLIP"
    # EMISSION, not a BSDF: the shipped window is unlit, and a lit preview is a picture of a
    # different effect. The image's ALPHA drives the transparent mix so shards behind the window
    # show through wherever an element has already dissolved.
    nt.links.new(tex.outputs["Color"], emit.inputs["Color"])
    nt.links.new(tex.outputs["Alpha"], mix.inputs["Fac"])
    nt.links.new(trans.outputs["BSDF"], mix.inputs[1])
    nt.links.new(emit.outputs["Emission"], mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], out.inputs["Surface"])
    set_transparency(mat)
    ob.data.materials.append(mat)
    return ob, tex


def make_shard_material():
    mat = bpy.data.materials.new("shards")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    geo = nt.nodes.new("ShaderNodeNewGeometry")

    dot_k = nt.nodes.new("ShaderNodeVectorMath")
    dot_k.operation = "DOT_PRODUCT"
    dot_f = nt.nodes.new("ShaderNodeVectorMath")
    dot_f.operation = "DOT_PRODUCT"
    dot_k.inputs[1].default_value = KEYDIR
    dot_f.inputs[1].default_value = tuple(-c for c in KEYDIR)
    nt.links.new(geo.outputs["Normal"], dot_k.inputs[0])
    nt.links.new(geo.outputs["Normal"], dot_f.inputs[0])

    sat_k = nt.nodes.new("ShaderNodeMath")
    sat_k.operation = "MAXIMUM"
    sat_f = nt.nodes.new("ShaderNodeMath")
    sat_f.operation = "MAXIMUM"
    sat_k.inputs[1].default_value = 0.0
    sat_f.inputs[1].default_value = 0.0
    nt.links.new(dot_k.outputs["Value"], sat_k.inputs[0])
    nt.links.new(dot_f.outputs["Value"], sat_f.inputs[0])

    mul_k = nt.nodes.new("ShaderNodeMath")
    mul_k.operation = "MULTIPLY"
    mul_f = nt.nodes.new("ShaderNodeMath")
    mul_f.operation = "MULTIPLY"
    mul_k.inputs[1].default_value = KEY
    mul_f.inputs[1].default_value = FILL
    nt.links.new(sat_k.outputs["Value"], mul_k.inputs[0])
    nt.links.new(sat_f.outputs["Value"], mul_f.inputs[0])

    add1 = nt.nodes.new("ShaderNodeMath")
    add1.operation = "ADD"
    add2 = nt.nodes.new("ShaderNodeMath")
    add2.operation = "ADD"
    nt.links.new(mul_k.outputs["Value"], add1.inputs[0])
    nt.links.new(mul_f.outputs["Value"], add1.inputs[1])
    nt.links.new(add1.outputs["Value"], add2.inputs[0])
    add2.inputs[1].default_value = AMBIENT          # shade = ambient + key*sat(d) + fill*sat(-d)

    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = "shade"
    tint = nt.nodes.new("ShaderNodeVectorMath")
    tint.operation = "MULTIPLY"
    tint.inputs[1].default_value = TINT
    nt.links.new(vcol.outputs["Color"], tint.inputs[0])   # tint * perShardShade

    scale = nt.nodes.new("ShaderNodeVectorMath")
    scale.operation = "SCALE"
    nt.links.new(tint.outputs["Vector"], scale.inputs[0])
    nt.links.new(add2.outputs["Value"], scale.inputs["Scale"])   # ... * shade

    nt.links.new(scale.outputs["Vector"], emit.inputs["Color"])
    emit.inputs["Strength"].default_value = 1.0
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"
    return mat


def build_shards(name, verts, faces, shade, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts.tolist(), [], faces.tolist())
    me.update()
    col = me.color_attributes.new("shade", "FLOAT_COLOR", "POINT")
    flat = numpy.empty((shade.shape[0], 4), dtype=numpy.float32)
    flat[:, 0] = shade
    flat[:, 1] = shade
    flat[:, 2] = shade
    flat[:, 3] = 1.0
    col.data.foreach_set("color", flat.ravel())
    me.materials.append(mat)
    # shade_flat: the shards are faceted tetrahedra, and the shipped shader uses the FACE normal.
    for p in me.polygons:
        p.use_smooth = False
    return me


def grey(name, rgb, rough=0.85):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def make_room():
    """Occluders. These ARE the deliverable: something between the camera and the window plane and
    something behind it, so a shard that is 'in front' and a shard that is 'behind' become
    distinguishable instead of two sprites on one quad."""
    obs = []

    # table top - the window floats above it, debris flies over it
    bpy.ops.mesh.primitive_plane_add(size=8.0, location=(0.0, 0.30, -0.34))
    t = bpy.context.object
    t.name = "table"
    t.data.materials.append(grey("table", (0.245, 0.200, 0.150)))
    obs.append(t)

    # back wall at y = +0.60, upright, filling the frame
    bpy.ops.mesh.primitive_plane_add(size=10.0, location=(0.0, 0.60, 0.6))
    w = bpy.context.object
    w.name = "wall"
    w.rotation_euler = (math.radians(90), 0, 0)
    w.data.materials.append(grey("wall", (0.205, 0.200, 0.215)))
    obs.append(w)

    # stone column IN FRONT of the window plane, on the side the wind blows toward. Debris passing
    # x=+0.40 at y=-0.24 must disappear behind it.
    bpy.ops.mesh.primitive_cylinder_add(radius=0.055, depth=1.30, location=(0.40, -0.24, 0.31),
                                        vertices=48)
    c = bpy.context.object
    c.name = "column"
    c.data.materials.append(grey("column", (0.42, 0.405, 0.375)))
    bpy.ops.object.shade_smooth()
    obs.append(c)

    # crate BEHIND the window plane - debris that went through/behind the window is hidden by it.
    #
    # MEASURED, NOT ASSUMED. The brief put this at y=+0.34. From the oblique camera a box there
    # occludes 5 shard-frames out of 16005 in the vanish and 0 out of 6402 in the appear: the
    # debris cloud's own y never reaches past it, so every ray that clears the crate has already
    # left the debris behind. Pulling it to y=+0.16 (front face still a clear +0.07 BEHIND the
    # window plane, so it is still an unambiguously-behind occluder) takes that to 542 and 64.
    # Re-run the count if the sim's wind is retuned; the number is what matters, not the position.
    bpy.ops.mesh.primitive_cube_add(size=0.18, location=(0.26, 0.16, 0.12))
    k = bpy.context.object
    k.name = "crate"
    k.rotation_euler = (0, 0, math.radians(11))
    k.data.materials.append(grey("crate", (0.335, 0.245, 0.160)))
    obs.append(k)

    # ONE area light. The ROOM may be lit; the shards and the window may not, and they are not -
    # both are Emission and ignore this entirely.
    pos = mathutils.Vector((0.38, -1.30, 1.18))
    bpy.ops.object.light_add(type="AREA", location=pos)
    lt = bpy.context.object
    lt.name = "key"
    lt.data.energy = 70.0
    lt.data.size = 0.35   # small: a 0.9 m source at 1.7 m is all penumbra, and penumbra is noise at 24 samples
    # An area light emits along its local -Z. Hand-rolled euler angles for this got the sign of the
    # tilt wrong on the first pass and lit the ceiling instead of the room - every occluder came
    # out a black silhouette and the depth proof read as nothing at all. to_track_quat cannot make
    # that mistake.
    lt.rotation_euler = (mathutils.Vector((0.0, 0.0, 0.0)) - pos).to_track_quat("-Z", "Y").to_euler()
    return obs, lt


def make_camera(name):
    cd = bpy.data.cameras.new(name)
    cd.lens = LENS_MM
    cam = bpy.data.objects.new(name, cd)
    bpy.context.collection.objects.link(cam)
    return cam


def place_orbit(cam, az_deg, el_deg, dist, target, shift_right=0.0):
    """Camera on a sphere around `target`. az 0 = dead-on from -y, positive az walks right,
    positive el walks up. The rotation (90-el, 0, az) is exactly the look-at for that point, so a
    `shift_right` offset moves the camera WITHOUT re-aiming it - a PARALLEL pair, no toe-in."""
    az, el = math.radians(az_deg), math.radians(el_deg)
    d = (math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))
    right = (math.cos(az), math.sin(az), 0.0)
    cam.location = tuple(target[i] + dist * d[i] + shift_right * right[i] for i in range(3))
    cam.rotation_euler = (math.radians(90.0 - el_deg), 0.0, az)


def render_to(sc, cam, path):
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    t0 = time.time()
    meta, faces, data = load_inputs()
    os.makedirs(FRAMES, exist_ok=True)

    panel = meta["panel"]
    sc = new_scene()
    first_win = os.path.join(OUT, meta["directions"]["appear"]["frames"][0]["window"])
    win_ob, win_tex = make_window(sc, first_win, panel["w_m"], panel["h_m"])
    shard_mat = make_shard_material()
    occluders, _light = make_room()

    shard_ob = bpy.data.objects.new("shards", bpy.data.meshes.new("shards_empty"))
    bpy.context.collection.objects.link(shard_ob)
    shard_ob.visible_shadow = False

    cam_front = make_camera("front")
    cam_front.location = (0.0, -0.90, 0.0)
    cam_front.rotation_euler = (math.radians(90), 0.0, 0.0)
    cam_obl = make_camera("oblique")
    place_orbit(cam_obl, OBLIQUE["az"], OBLIQUE["el"], OBLIQUE["dist"], OBLIQUE["target"])

    def set_occluders(visible):
        for o in occluders:
            o.hide_render = not visible

    def swap_window(rel_path):
        old = win_tex.image
        img = bpy.data.images.load(os.path.join(OUT, rel_path))
        img.colorspace_settings.name = "sRGB"
        win_tex.image = img
        if old is not None:
            bpy.data.images.remove(old)

    def swap_shards(name, verts, shade):
        old_me = shard_ob.data
        shard_ob.data = build_shards(name, verts, faces, shade, shard_mat)
        if old_me is not None and old_me.users == 0:
            bpy.data.meshes.remove(old_me)

    written = []
    n_render = 0
    for direction in ("appear", "vanish"):
        verts, shade = data[direction]
        frames = meta["directions"][direction]["frames"]
        picked = [fr for j, fr in enumerate(frames) if j % STRIDE == 0 or j == len(frames) - 1]
        for fr in picked:
            i = fr["f"]
            swap_window(fr["window"])
            swap_shards("shards_%s_%04d" % (direction, i), verts[i], shade)

            set_occluders(False)
            p = os.path.join(FRAMES, "front_%s_%04d.png" % (direction, i))
            render_to(sc, cam_front, p)
            written.append(p)
            n_render += 1

            set_occluders(True)
            p = os.path.join(FRAMES, "oblique_%s_%04d.png" % (direction, i))
            render_to(sc, cam_obl, p)
            written.append(p)
            n_render += 1
        print("  [%s] %d of %d frames done (%.1f s elapsed)"
              % (direction, len(picked), len(frames), time.time() - t0))

    # --- the one instant used for the stereo and parallax pairs --------------------------------
    vanish_s = meta["directions"]["vanish"]["seconds"]
    want_t = PAIR_T * vanish_s
    vframes = meta["directions"]["vanish"]["frames"]
    pick = min(vframes, key=lambda fr: abs(fr["t"] - want_t))
    verts, shade = data["vanish"]
    swap_window(pick["window"])
    swap_shards("shards_pair", verts[pick["f"]], shade)
    set_occluders(True)

    cam_pair = make_camera("pair")
    for eye, dx in (("L", -IPD_M / 2.0), ("R", IPD_M / 2.0)):
        place_orbit(cam_pair, STEREO["az"], STEREO["el"], STEREO["dist"], STEREO["target"],
                    shift_right=dx)
        p = os.path.join(FRAMES, "stereo_%s.png" % eye)
        render_to(sc, cam_pair, p)
        written.append(p)
        n_render += 1

    for tag, az in (("A", -PARALLAX_AZ), ("B", +PARALLAX_AZ)):
        place_orbit(cam_pair, az, PARALLAX["el"], PARALLAX["dist"], PARALLAX["target"])
        p = os.path.join(FRAMES, "parallax_%s.png" % tag)
        render_to(sc, cam_pair, p)
        written.append(p)
        n_render += 1
    lat = 2.0 * PARALLAX["dist"] * math.cos(math.radians(PARALLAX["el"])) * \
        math.sin(math.radians(PARALLAX_AZ))

    # The captions on the composed pairs have to state the real rig, and windowmaterialise_compose
    # has no way to know it. Hand it over rather than duplicating the constants in two files.
    pairs_path = os.path.join(FRAMES, "pairs.json")
    json.dump({
        "instant": {"direction": "vanish", "frame": pick["f"], "t": pick["t"],
                    "fraction_of_vanish": PAIR_T, "vanish_s": vanish_s,
                    "k": pick["k"], "element_progress": pick["element_progress"]},
        "stereo": {"ipd_m": IPD_M, "parallel": True, "az_deg": STEREO["az"],
                   "el_deg": STEREO["el"], "dist_m": STEREO["dist"]},
        "parallax": {"lateral_separation_m": lat, "half_offset_m": lat / 2.0,
                     "az_deg": PARALLAX_AZ, "el_deg": PARALLAX["el"],
                     "dist_m": PARALLAX["dist"]},
    }, open(pairs_path, "w"), indent=1)
    written.append(pairs_path)

    dt = time.time() - t0
    print("")
    print("=== windowmaterialise_room ===================================================")
    print("out            %s" % FRAMES)
    print("resolution     %dx%d, EEVEE_NEXT, %d TAA samples, Standard view transform"
          % (RES_X, RES_Y, SAMPLES))
    print("window         %.2f x %.2f m emissive plane at y=0, alpha-cut from win/*.png"
          % (panel["w_m"], panel["h_m"]))
    print("shards         %d shards / %d tris / %d verts per frame, unlit shipped shader"
          % (meta["shards"], faces.shape[0], data["appear"][0].shape[1]))
    print("front_*        cam (0,-0.90,0) -> +y, %.0f mm, occluders HIDDEN   (%d+%d frames)"
          % (LENS_MM, len(meta["directions"]["appear"]["frames"]),
             len(meta["directions"]["vanish"]["frames"])))
    print("oblique_*      az %.0f deg / el %.0f deg / %.2f m about (%.2f,0,0), occluders VISIBLE"
          % (OBLIQUE["az"], OBLIQUE["el"], OBLIQUE["dist"], OBLIQUE["target"][0]))
    print("stereo_L/R     parallel pair, IPD %.3f m, az %.0f deg / %.2f m, vanish f%d t=%.3f s"
          " (%.2f x vanish, window elem_progress %.2f)"
          % (IPD_M, STEREO["az"], STEREO["dist"], pick["f"], pick["t"], PAIR_T,
             pick["element_progress"]))
    print("parallax_A/B   %.3f m apart laterally (az %+.1f/%+.1f deg), same instant, aimed at centre"
          % (lat, -PARALLAX_AZ, PARALLAX_AZ))
    print("occluders      table z=-0.34 | wall y=+0.60 | column x=+0.40 y=-0.24 (IN FRONT) |"
          " crate x=+0.26 y=+0.16 z=+0.12 (BEHIND, front face y=+0.07)")
    print("renders        %d images in %.1f s (%.2f s each)" % (n_render, dt, dt / max(n_render, 1)))
    print("files          %d written, first/last:" % len(written))
    print("               %s" % written[0])
    print("               %s" % written[-1])
    print("==============================================================================")


main()
