# render_hand.py — Blender headless: render a rigged hand FBX in the three modes that
# SEPARATE the two failure causes this asset has a history of confusing.
#
#   clay      untextured, lit          -> GEOMETRY only. The fold must vanish here.
#   emis      albedo as emission, no lights, MAGENTA world -> TEXTURE only, and any hole you
#             can see through shows up as magenta. Culling follows the game (the hand material
#             sets _Cull = 0, so both sides draw); --cull renders single-sided instead, which
#             is the stricter test of the shell.
#   lit       albedo + lights          -> what the player sees.
#
# Views: palm, back, thumb three-quarter, cuff three-quarter.
#
# RUN:
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/render_hand.py -- \
#       <in.fbx> <albedo.png> <outdir> <tag> [--modes clay,emis,lit] [--views palm,back,thumb,cuff]
#       [--res 900] [--cull] [--clearnormals]
#
# --clearnormals throws away the FBX's custom split normals before rendering. That one flag is
# what identified this asset's palm fold as a NORMALS problem rather than a geometry problem:
# the fold nearly vanishes with every vertex still exactly where it was.
import bpy, sys, os, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC, TEX, OUTDIR, TAG = pos[0], pos[1], pos[2], pos[3]


def opt(name, default):
    return argv[argv.index(name) + 1] if name in argv else default


MODES = opt("--modes", "clay,emis,lit").split(",")
VIEWS = opt("--views", "palm,back,thumb,cuff").split(",")
RES = int(opt("--res", "900"))
MAGENTA = (1.0, 0.0, 1.0, 1.0)
# The shipped hand material sets _Cull = 0, so the game draws BOTH sides. Default to that;
# --cull renders single-sided, which is the stricter test of the shell itself.
CULL = "--cull" in argv

os.makedirs(OUTDIR, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
if "--clearnormals" in argv:
    bpy.context.view_layer.objects.active = ob
    bpy.ops.mesh.customdata_custom_splitnormals_clear()
    print("[render] custom split normals CLEARED (debug)")
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE':
        o.hide_render = True

# world bbox
mw = ob.matrix_world
pts = [mw @ Vector(c) for c in ob.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (lo + hi) * 0.5
size = hi - lo
print("[render] bbox mm", [round(v * 1000, 1) for v in size], "centre", [round(v * 1000, 1) for v in ctr])

# The palm plate sits above the cuff; frame the hand proper, not the whole cuff.
hand_ctr = Vector((ctr.x, ctr.y, lo.z + size.z * 0.62))
hand_scale = max(size.x, size.z * 0.62) * 1.05
cuff_ctr = Vector((ctr.x, ctr.y, lo.z + size.z * 0.22))
cuff_scale = max(size.x, size.z * 0.34) * 1.05

VIEWDEF = {
    # name: (direction the camera sits in, target, ortho scale)
    "palm":  (Vector((0, -1, 0)), hand_ctr, hand_scale),
    "back":  (Vector((0, 1, 0)), hand_ctr, hand_scale),
    "thumb": (Vector((-0.75, -0.85, 0.15)), hand_ctr, hand_scale),
    "cuff":  (Vector((-0.5, -1.0, -0.25)), cuff_ctr, cuff_scale),
    "palm45": (Vector((0.55, -1.0, 0.25)), hand_ctr, hand_scale),
}

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x = scene.render.resolution_y = RES
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.eevee.taa_render_samples = 64
if hasattr(scene.eevee, "use_raytracing"):
    scene.eevee.use_raytracing = False

cam_data = bpy.data.cameras.new("cam")
cam_data.type = 'ORTHO'
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

img = bpy.data.images.load(os.path.abspath(TEX))
img.colorspace_settings.name = 'sRGB'


def make_mat(mode):
    m = bpy.data.materials.new("m_" + mode)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    if mode == 'clay':
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        bsdf.inputs["Base Color"].default_value = (0.62, 0.62, 0.63, 1)
        bsdf.inputs["Roughness"].default_value = 0.42
        bsdf.inputs["Metallic"].default_value = 0.0
        nt.links.new(bsdf.outputs[0], out.inputs[0])
        m.use_backface_culling = False
    elif mode == 'emis':
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.interpolation = 'Linear'
        em = nt.nodes.new("ShaderNodeEmission")
        em.inputs["Strength"].default_value = 1.0
        nt.links.new(tex.outputs["Color"], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs[0])
        m.use_backface_culling = CULL
    else:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        bsdf.inputs["Roughness"].default_value = 0.42
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        nt.links.new(bsdf.outputs[0], out.inputs[0])
        m.use_backface_culling = CULL
    return m


def set_lights(on):
    for o in list(bpy.data.objects):
        if o.type == 'LIGHT':
            bpy.data.objects.remove(o, do_unlink=True)
    w = bpy.data.worlds.new("w") if not scene.world else scene.world
    scene.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    if on:
        bg.inputs[0].default_value = (0.05, 0.055, 0.07, 1)
        bg.inputs[1].default_value = 1.0
        for name, d, e in (("key", Vector((-0.6, -1, 0.7)), 6.0),
                           ("fill", Vector((0.9, -0.7, 0.1)), 2.4),
                           ("rim", Vector((0.2, 1.0, 0.6)), 3.0),
                           ("under", Vector((0.0, -0.3, -1.0)), 1.6)):
            ld = bpy.data.lights.new(name, 'SUN')
            ld.energy = e
            ld.angle = math.radians(25)
            lo_ = bpy.data.objects.new(name, ld)
            scene.collection.objects.link(lo_)
            lo_.rotation_euler = d.normalized().to_track_quat('-Z', 'Y').to_euler()
    else:
        bg.inputs[0].default_value = MAGENTA
        bg.inputs[1].default_value = 1.0


for mode in MODES:
    ob.data.materials.clear()
    ob.data.materials.append(make_mat(mode))
    set_lights(mode != 'emis')
    for vn in VIEWS:
        d, tgt, sc = VIEWDEF[vn]
        d = d.normalized()
        zc = d
        up = Vector((0, 0, 1)) if abs(zc.z) < 0.95 else Vector((0, 1, 0))
        xc = up.cross(zc).normalized()
        yc = zc.cross(xc).normalized()
        cam.location = tgt + d * 2.0
        cam.matrix_world = Matrix(((xc.x, yc.x, zc.x, cam.location.x),
                                   (xc.y, yc.y, zc.y, cam.location.y),
                                   (xc.z, yc.z, zc.z, cam.location.z),
                                   (0, 0, 0, 1)))
        cam_data.ortho_scale = sc
        scene.render.filepath = os.path.join(OUTDIR, f"{TAG}_{vn}_{mode}.png")
        bpy.ops.render.render(write_still=True)
        print("[render] wrote", scene.render.filepath)
