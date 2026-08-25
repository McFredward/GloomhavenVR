# Blender: flat-on ORTHOGRAPHIC render of a control board, AMBIENT LIGHT ONLY.
#
# WHY ambient only. This image becomes the init frame for an img2img pass whose output
# becomes an ALBEDO. Any directional key baked into the init frame would be baked into the
# albedo and then lit a second time by BoardLit. A uniform white world gives albedo x
# ambient-occlusion: the recesses, mouldings and engraved glyphs read (so the model can see
# the object), and no light has a direction (so nothing is double-lit later).
#
# Also writes uv.exr — the per-pixel atlas UV — so the generated image can be projected
# back into the atlas exactly, with no guesswork about which region a pixel belongs to.
#
#   blender -b --factory-startup -P init_render.py -- <fbx> <albedo> <normal> <outdir> <W> <H>
import bpy, sys, os, math
a = sys.argv[sys.argv.index('--')+1:]
fbx, albedo, normal, outdir, W, H = a[0], a[1], a[2], a[3], int(a[4]), int(a[5])
os.makedirs(outdir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
print('MESHES', [o.name for o in objs])
for o in objs:
    o.rotation_euler = (0, 0, 0); o.location = (0, 0, 0); o.scale = (1, 1, 1)
bpy.context.view_layer.update()

# Board bounds in world space, after the identity pose above.
import mathutils
mn = mathutils.Vector((1e9, 1e9, 1e9)); mx = mathutils.Vector((-1e9, -1e9, -1e9))
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(c)
        for i in range(3):
            mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
size = mx - mn; ctr = (mx + mn) / 2
print('BOUNDS min %s max %s size %s' % (tuple(mn), tuple(mx), tuple(size)))

scn = bpy.context.scene
scn.render.resolution_x = W; scn.render.resolution_y = H
scn.render.resolution_percentage = 100
scn.render.film_transparent = True

cam_d = bpy.data.cameras.new('C'); cam_d.type = 'ORTHO'
# The decorated face points -Z (contract), so the camera sits on -Z looking toward +Z.
cam = bpy.data.objects.new('C', cam_d); scn.collection.objects.link(cam); scn.camera = cam
# THE FBX LIES IN THE XZ PLANE AFTER IMPORT, NOT XY: measured bounds are
# 0.640 (x) x 0.034 (y) x 0.320 (z), so the thickness axis is Y and the decorated face
# looks along -Y or +Y. The contract's "face toward -Z" is written in the Unity frame;
# Blender's FBX importer converts Y-up to Z-up and the plate lands in XZ. Which of the
# two faces is the decorated one is decided by SIDE below and checked by eye.
SIDE = os.environ.get('BOARD_FACE', '-y')
if SIDE == '-y':
    cam.location = (ctr.x, mn.y - 1.0, ctr.z)
    cam.rotation_euler = (math.radians(90), 0, 0)
else:
    cam.location = (ctr.x, mx.y + 1.0, ctr.z)
    cam.rotation_euler = (math.radians(-90), 0, 0)
cam_d.ortho_scale = size.x
print('ORTHO SCALE', cam_d.ortho_scale, 'face', SIDE, 'for size', tuple(size))

def clear_mats():
    for o in objs:
        o.data.materials.clear()

def make_mat(name, build):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    build(nt, out)
    return m

def assign(m):
    for o in objs:
        o.data.materials.clear(); o.data.materials.append(m)

# ---------------- PASS 1: UV, as raw float, no shading, no colour management ----------
def build_uv(nt, out):
    e = nt.nodes.new('ShaderNodeEmission')
    uv = nt.nodes.new('ShaderNodeUVMap')
    nt.links.new(uv.outputs['UV'], e.inputs['Color'])
    nt.links.new(e.outputs['Emission'], out.inputs['Surface'])
assign(make_mat('UVPASS', build_uv))
scn.render.engine = 'CYCLES'
scn.cycles.samples = 1
scn.cycles.use_denoising = False
scn.view_settings.view_transform = 'Standard'
scn.view_settings.look = 'None'; scn.view_settings.exposure = 0; scn.view_settings.gamma = 1
scn.render.image_settings.file_format = 'OPEN_EXR'
scn.render.image_settings.color_depth = '32'
scn.render.image_settings.color_mode = 'RGBA'
scn.render.filepath = os.path.join(outdir, 'uv.exr')
bpy.ops.render.render(write_still=True)
print('WROTE uv.exr')

# ---------------- PASS 2: albedo x ambient occlusion, uniform white world -------------
world = bpy.data.worlds.new('W'); scn.world = world
world.use_nodes = True
bg = world.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (1, 1, 1, 1); bg.inputs['Strength'].default_value = 1.0

def build_lit(nt, out):
    d = nt.nodes.new('ShaderNodeBsdfDiffuse')
    t = nt.nodes.new('ShaderNodeTexImage')
    t.image = bpy.data.images.load(albedo); t.image.colorspace_settings.name = 'sRGB'
    t.interpolation = 'Closest'
    nt.links.new(t.outputs['Color'], d.inputs['Color'])
    nm = nt.nodes.new('ShaderNodeNormalMap')
    tn = nt.nodes.new('ShaderNodeTexImage')
    tn.image = bpy.data.images.load(normal); tn.image.colorspace_settings.name = 'Non-Color'
    nt.links.new(tn.outputs['Color'], nm.inputs['Color'])
    nt.links.new(nm.outputs['Normal'], d.inputs['Normal'])
    nt.links.new(d.outputs['BSDF'], out.inputs['Surface'])
assign(make_mat('LIT', build_lit))
scn.cycles.samples = 96
scn.cycles.use_denoising = True
scn.render.image_settings.file_format = 'PNG'
scn.render.image_settings.color_depth = '16'
scn.render.filepath = os.path.join(outdir, 'ambient.png')
bpy.ops.render.render(write_still=True)
print('WROTE ambient.png')
