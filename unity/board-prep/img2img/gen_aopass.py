# TRUE cavity/relief pass: WHITE albedo, normal map bound, uniform white world, no lights.
#
# The previous cavity term was ambient/albedo, and a divide can only be as clean as the two
# renders agree on filtering -- it left 0.35-0.62 residual correlation with the albedo's own
# texture, i.e. it smuggled the old streak noise into the term meant to replace it. Rendering
# a white-albedo board removes the divide: what comes out IS the geometry plus the normal
# map's relief, and nothing else.
import bpy, sys, os, math, mathutils
a=sys.argv[sys.argv.index('--')+1:]
fbx, normal, outdir, W, H = a[0], a[1], a[2], int(a[3]), int(a[4])
os.makedirs(outdir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
objs=[o for o in bpy.context.scene.objects if o.type=='MESH']
for o in objs: o.rotation_euler=(0,0,0); o.location=(0,0,0); o.scale=(1,1,1)
bpy.context.view_layer.update()
mn=mathutils.Vector((1e9,)*3); mx=mathutils.Vector((-1e9,)*3)
for o in objs:
    for c in o.bound_box:
        w=o.matrix_world @ mathutils.Vector(c)
        for i in range(3): mn[i]=min(mn[i],w[i]); mx[i]=max(mx[i],w[i])
size=mx-mn; ctr=(mx+mn)/2
scn=bpy.context.scene
scn.render.resolution_x=W; scn.render.resolution_y=H; scn.render.resolution_percentage=100
scn.render.film_transparent=True
cd=bpy.data.cameras.new('C'); cd.type='ORTHO'; cd.ortho_scale=size.x
cam=bpy.data.objects.new('C',cd); scn.collection.objects.link(cam); scn.camera=cam
cam.location=(ctr.x, mx.y+1.0, ctr.z); cam.rotation_euler=(math.radians(-90),0,0)
scn.world=bpy.data.worlds.new('W'); scn.world.use_nodes=True
bg=scn.world.node_tree.nodes['Background']
bg.inputs['Color'].default_value=(1,1,1,1); bg.inputs['Strength'].default_value=1.0
m=bpy.data.materials.new('AO'); m.use_nodes=True; nt=m.node_tree; nt.nodes.clear()
out=nt.nodes.new('ShaderNodeOutputMaterial'); d=nt.nodes.new('ShaderNodeBsdfDiffuse')
d.inputs['Color'].default_value=(1,1,1,1)
nm=nt.nodes.new('ShaderNodeNormalMap'); tn=nt.nodes.new('ShaderNodeTexImage')
tn.image=bpy.data.images.load(normal); tn.image.colorspace_settings.name='Non-Color'
nt.links.new(tn.outputs['Color'], nm.inputs['Color'])
nt.links.new(nm.outputs['Normal'], d.inputs['Normal'])
nt.links.new(d.outputs['BSDF'], out.inputs['Surface'])
for o in objs: o.data.materials.clear(); o.data.materials.append(m)
scn.render.engine='CYCLES'; scn.cycles.samples=int(os.environ.get('AO_SAMPLES','48')); scn.cycles.use_denoising=True
s=scn.render.image_settings
s.file_format='PNG'; s.color_mode='BW'
s.color_management='OVERRIDE'; s.view_settings.view_transform='Raw'; s.view_settings.look='None'
s.color_depth='16'
scn.render.filepath=os.path.join(outdir,'ao16.png')
bpy.ops.render.render(write_still=True)
print('WROTE ao16.png')
