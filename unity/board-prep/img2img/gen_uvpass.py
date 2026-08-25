# UV pass as TWO 16-bit GREYSCALE PNGs.
#
# WHY NOT ONE RGBA PNG: PIL cannot read a 16-bit RGBA PNG and downconverts it to 8 bits
# WITHOUT AN ERROR -- Blender reported 'DEPTH IS 16' and the array still came back uint8.
# 8-bit UV is 1/255, which at a 2048 atlas is 8 texels of quantisation. A 16-bit BW PNG is
# mode 'I;16', which PIL does read, so u and v go in separate files: 1/65535, i.e. 1/32 of
# a texel.
import bpy, sys, os, math, mathutils
a=sys.argv[sys.argv.index('--')+1:]
fbx, outdir, W, H = a[0], a[1], int(a[2]), int(a[3])
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
scn.render.film_transparent=False          # opaque: alpha is carried by the coverage pass
cd=bpy.data.cameras.new('C'); cd.type='ORTHO'; cd.ortho_scale=size.x
cam=bpy.data.objects.new('C',cd); scn.collection.objects.link(cam); scn.camera=cam
cam.location=(ctr.x, mx.y+1.0, ctr.z); cam.rotation_euler=(math.radians(-90),0,0)
scn.render.engine='CYCLES'; scn.cycles.samples=1; scn.cycles.use_denoising=False
scn.render.filter_size=0.01
scn.world=bpy.data.worlds.new('W'); scn.world.use_nodes=True
scn.world.node_tree.nodes['Background'].inputs['Strength'].default_value=0.0

def emit(idx):
    m=bpy.data.materials.new('E%d'%idx); m.use_nodes=True; nt=m.node_tree; nt.nodes.clear()
    out=nt.nodes.new('ShaderNodeOutputMaterial'); e=nt.nodes.new('ShaderNodeEmission')
    if idx<2:
        uv=nt.nodes.new('ShaderNodeUVMap'); sep=nt.nodes.new('ShaderNodeSeparateXYZ')
        nt.links.new(uv.outputs['UV'], sep.inputs['Vector'])
        nt.links.new(sep.outputs['XY'[idx]], e.inputs['Color'])
    else:
        e.inputs['Color'].default_value=(1,1,1,1)      # coverage
    nt.links.new(e.outputs['Emission'], out.inputs['Surface'])
    for o in objs: o.data.materials.clear(); o.data.materials.append(m)

for idx,name in ((0,'u16'),(1,'v16'),(2,'cover16')):
    emit(idx)
    s=scn.render.image_settings
    s.file_format='PNG'; s.color_mode='BW'
    s.color_management='OVERRIDE'
    s.view_settings.view_transform='Raw'; s.view_settings.look='None'
    s.color_depth='16'
    scn.render.filepath=os.path.join(outdir,name+'.png')
    bpy.ops.render.render(write_still=True)
    print('WROTE',name,'depth',s.color_depth)
