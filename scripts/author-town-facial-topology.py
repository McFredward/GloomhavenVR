#!/usr/bin/env python3
"""Fit CC0 HM08 anatomical facial loops to reviewed NPC portrait landmarks.

Blender 4.2 offline authoring only. Uses mesh/target data, never upstream addon code.
The fitted surface has native orbital pockets and an oral cavity; eye geometry is
separate. Original provider meshes, portraits and existing costume rigs stay read-only.
"""
import argparse, gzip, hashlib, json, math, sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

# Source image pixels are measured on the approved 718px frontal references.
# The vertical samples pin actual anatomical loops to the corresponding portrait
# features; fitting the photographic eye to an unrelated closed shell is not used.
RAW_HEIGHT = [5.80,6.16,6.608,6.676,6.75,6.91,7.28415,7.51,8.4913]
PROFILES = {
 'merchant': dict(base=1.440, lo=(-.10915041,-.12370944,-.01239385), hi=(.10918231,.12368525,.30980915),
                  bounds=(132,18,592,703), center=362, pixelsPerX=260, depthScale=.100, depthOffset=.056,
                  pixelsY=[709,630,449,428,411,347,260,210,18]),
 'priestess': dict(base=1.445, lo=(-.085303247,-.108154021,-.00000448), hi=(.08529919,.10801458,.27995017),
                  bounds=(156,26,559,699), center=357.5, pixelsPerX=249, depthScale=.085, depthOffset=.012,
                  pixelsY=[709,565,482,464,451,397,294,249,26]),
 'enchantress': dict(base=1.450, lo=(-.1006966,-.116575,.00008495), hi=(.10079506,.11644621,.26988822),
                  bounds=(77,12,651,708), center=374.5, pixelsPerX=242, depthScale=.083, depthOffset=-.006,
                  pixelsY=[710,571,497,477,459,408,313,270,12]),
}
SHAPES = {
 'BlinkLeft': {'eye-left-closure':1}, 'BlinkRight': {'eye-right-closure':1},
 'JawOpen': {'mouth-open':1}, 'MouthWide': {'mouth-retraction':.8},
 'MouthRound': {'mouth-pursing':.8}, 'Smile': {'mouth-corner-puller':.6},
 'BrowRaise': {'eyebrows-left-up':.5,'eyebrows-right-up':.5},
}


def parse_obj(path):
    vertices=[]; faces=[]; groups=[]; group=''
    for line in path.read_text().splitlines():
        fields=line.split()
        if not fields: continue
        if fields[0]=='v': vertices.append(tuple(map(float,fields[1:4])))
        elif fields[0]=='g': group=fields[1]
        elif fields[0]=='f':
            faces.append([int(f.split('/')[0])-1 for f in fields[1:]])
            groups.append(group)
    return np.asarray(vertices),faces,groups


def portrait_coordinates(raw,profile):
    px=profile['center']+raw[:,0]*profile['pixelsPerX']
    py=np.interp(raw[:,1],RAW_HEIGHT,profile['pixelsY'])
    return np.column_stack((px,py))


def fit(raw,name):
    p=PROFILES[name]; uv=portrait_coordinates(raw,p); x0,y0,x1,y1=p['bounds']
    width=p['hi'][0]-p['lo'][0]; height=p['hi'][2]-p['lo'][2]
    x=(uv[:,0]-p['center'])/(x1-x0)*width
    z=p['lo'][2]+(y1-uv[:,1])/(y1-y0)*height
    if name=='priestess': z=np.interp(z,[0,.055,.102,.141,.175,.200,.28],[0,.055,.094,.133,.164,.191,.28])
    if name=='enchantress': z=np.interp(z,[0,.065,.095,.130,.169,.205,.27],[0,.065,.092,.128,.165,.205,.27])
    y=p['depthOffset']-raw[:,2]*p['depthScale']
    # Preserve the existing hood envelope at the scalp and neck contact below it.
    if name!='merchant':
        scalp=np.clip((z-.19)/.065,0,1)
        x*=1-.14*scalp; y=p['depthOffset']+(y-p['depthOffset'])*(1-.15*scalp)
        z-=.012*scalp
    return np.column_stack((x,y,z+p['base']))


def material(name,color,rough=.65):
    m=bpy.data.materials.new(name);m.use_nodes=True
    s=m.node_tree.nodes.get('Principled BSDF');s.inputs['Base Color'].default_value=(*color,1)
    s.inputs['Roughness'].default_value=rough
    return m


def head_mesh(raw,faces,groups,name,refs):
    chosen=[f for f,g in zip(faces,groups)if g=='body'and min(raw[f,1])>5.80]
    ids=sorted({i for f in chosen for i in f});remap={old:new for new,old in enumerate(ids)}
    mesh=bpy.data.meshes.new('FacialLoops');mesh.from_pydata(fit(raw[ids],name).tolist(),[],[[remap[i]for i in f]for f in chosen]);mesh.update()
    obj=bpy.data.objects.new('Face',mesh);bpy.context.collection.objects.link(obj)
    for p in mesh.polygons:p.use_smooth=True
    uv=mesh.uv_layers.new(name='FrontReference');pixels=portrait_coordinates(raw[ids],PROFILES[name])
    for loop in mesh.loops: uv.data[loop.index].uv=(pixels[loop.vertex_index,0]/718,1-pixels[loop.vertex_index,1]/718)
    m=material('TownFace',(.65,.4,.3));nodes=m.node_tree.nodes;links=m.node_tree.links
    image=bpy.data.images.load(str(refs/'front.png'));tex=nodes.new('ShaderNodeTexImage');tex.image=image;tex.extension='EXTEND'
    links.new(tex.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color']);mesh.materials.append(m)
    obj.shape_key_add(name='Basis')
    return obj,ids


def add_shapes(obj,ids,raw,name,data):
    for shape,recipes in SHAPES.items():
        posed=raw.copy()
        for target,weight in recipes.items():
            for line in gzip.open(data/'targets/expression/units/caucasian'/(target+'.target.gz'),'rt'):
                f=line.split()
                if not f or f[0].startswith('#'):continue
                posed[int(f[0])]+=np.array(list(map(float,f[1:4])))*weight
        key=obj.shape_key_add(name=shape)
        for v,co in zip(key.data,fit(posed[ids],name)):v.co=co


def eyes(raw,name,data):
    groups=json.loads((data/'mesh_metadata/basemesh_vertex_groups.json').read_text());out={}
    for side in ('l','r'):
        indices=[i for a,b in groups['joint-'+side+'-eye']for i in range(a,b+1)]
        centre=raw[indices].mean(0);fitted=fit(np.array([centre]),name)[0]
        # Fit the same source anatomical globe instead of placing a disc over skin.
        corners=fit(np.array([centre+[.14,0,0],centre+[0,.14,0],centre+[0,0,.146]]),name)-fitted
        scale=np.array([abs(corners[0,0]),abs(corners[2,1]),abs(corners[1,2])])
        bpy.ops.mesh.primitive_uv_sphere_add(segments=48,ring_count=24,radius=1,location=fitted)
        eye=bpy.context.object;eye.name='EyeLeft'if side=='l'else'EyeRight';eye.scale=scale
        for p in eye.data.polygons:p.use_smooth=True
        eye.data.materials.append(material('Sclera',(.72,.70,.64),.2))
        out[eye.name]={'centerBlender':fitted.tolist(),'radiiBlender':scale.tolist()}
    return out


def render(output,obj):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.render.resolution_x=800;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("ReviewWorld");scene.world.color=(.12,.12,.12)
    for loc,energy,size in [((-1,-2,3),130,1.6),((1,-.5,2),45,1.2)]:
        d=bpy.data.lights.new('Review','AREA');d.energy=energy;d.shape='DISK';d.size=size;l=bpy.data.objects.new('Review',d);scene.collection.objects.link(l);l.location=loc;l.rotation_euler=(Vector((0,0,1.6))-l.location).to_track_quat('-Z','Y').to_euler()
    c=bpy.data.cameras.new('FaceReview');c.type='ORTHO';c.ortho_scale=.37;camera=bpy.data.objects.new('FaceReview',c);scene.collection.objects.link(camera);scene.camera=camera
    for pose in ('neutral','blink','jaw'):
        for key in obj.data.shape_keys.key_blocks:key.value=0
        if pose=='blink':obj.data.shape_keys.key_blocks['BlinkLeft'].value=1;obj.data.shape_keys.key_blocks['BlinkRight'].value=1
        if pose=='jaw':obj.data.shape_keys.key_blocks['JawOpen'].value=1
        for view,pos in [('front',(0,-2,1.6)),('oblique',(.8,-1.7,1.6))]:
            camera.location=pos;camera.rotation_euler=(Vector((0,-.015,1.6))-camera.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(output/(pose+'-'+view+'.png'));bpy.ops.render.render(write_still=True)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--data',type=Path,required=True);p.add_argument('--references',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--name',choices=PROFILES,required=True);p.add_argument('--render',action='store_true');a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True);raw,faces,groups=parse_obj(a.data/'3dobjs/base.obj');obj,ids=head_mesh(raw,faces,groups,a.name,a.references);add_shapes(obj,ids,raw,a.name,a.data);eye=eyes(raw,a.name,a.data)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True);mod=obj.modifiers.new('Anatomical loop subdivision','SUBSURF');mod.levels=2;mod.render_levels=2
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'face-prototype.blend'))
    (a.output/'landmarks.json').write_text(json.dumps({'name':a.name,'eyes':eye,'headVertices':len(ids),'headQuads':len(obj.data.polygons),'shapes':list(SHAPES)},indent=2)+'\n')
    if a.render:render(a.output,obj)

if __name__=='__main__':main()
