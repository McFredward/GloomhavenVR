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
RAW_HEIGHT = [5.80,6.16,6.527,6.615,6.696,6.91,7.28415,7.51,8.4913]
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
 'LidUpLeft': {}, 'LidDownLeft': {}, 'LidUpRight': {}, 'LidDownRight': {},
}


def parse_obj(path):
    vertices=[]; faces=[]; groups=[]; uv=[]; face_uv=[]; group=''
    for line in path.read_text().splitlines():
        fields=line.split()
        if not fields: continue
        if fields[0]=='v': vertices.append(tuple(map(float,fields[1:4])))
        elif fields[0]=='vt': uv.append(tuple(map(float,fields[1:3])))
        elif fields[0]=='g': group=fields[1]
        elif fields[0]=='f':
            faces.append([int(f.split('/')[0])-1 for f in fields[1:]])
            groups.append(group)
            face_uv.append([uv[int(f.split('/')[1])-1] for f in fields[1:]])
    return np.asarray(vertices),faces,groups,face_uv


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


def head_mesh(raw,faces,groups,face_uv,name,refs,data):
    chosen_indices=[i for i,(f,g) in enumerate(zip(faces,groups))if g=='body'and min(raw[f,1])>5.80]
    chosen=[faces[i]for i in chosen_indices]
    ids=sorted({i for f in chosen for i in f});remap={old:new for new,old in enumerate(ids)}
    mesh=bpy.data.meshes.new('FacialLoops');mesh.from_pydata(fit(raw[ids],name).tolist(),[],[[remap[i]for i in f]for f in chosen]);mesh.update()
    obj=bpy.data.objects.new('Face',mesh);bpy.context.collection.objects.link(obj)
    for p in mesh.polygons:p.use_smooth=True
    pixels=portrait_coordinates(raw[ids],PROFILES[name]);profile=PROFILES[name]
    bounds_side={'merchant':(66,12,633,699),'priestess':(111,25,610,700),'enchantress':(34,6,638,717)}[name]
    bounds_back={'merchant':(116,13,607,679),'priestess':(156,29,561,692),'enchantress':(77,13,649,710)}[name]
    eye_y={'merchant':260,'priestess':294,'enchantress':313}[name]
    eye_width={'merchant':43,'priestess':34,'enchantress':35}[name]
    closed=posed_source(posed_source(raw,'BlinkLeft',data),'BlinkRight',data)
    closed_pixels=portrait_coordinates(closed[ids],profile)
    for label in ('front','left','back'):
        uv=mesh.uv_layers.new(name='Reference_'+label)
        for loop in mesh.loops:
            index=loop.vertex_index;x,y,z=raw[ids[index]];px,py=pixels[index]
            if label=='front':
                # Skin loops never sample the photographic pupil/sclera. UVs on
                # the upper/lower orbital rim stay in the corresponding lid skin
                # when the original closure target unfolds that skin over the globe.
                eye_x=profile['center']+(.30775 if x>0 else -.30775)*profile['pixelsPerX']
                dx=(px-eye_x)/eye_width
                if abs(dx)<1 and abs(py-eye_y)<22:
                    curve=math.sqrt(max(0,1-dx*dx));top=eye_y-16*curve;bottom=eye_y+13*curve
                    if top-7<py<bottom+2:
                        if y>=7.28415:
                            # Map a real two-dimensional skin patch below the eye
                            # through the closed-lid coordinates. Collapsing every
                            # rim UV onto one scanline creates shutter-like bands.
                            py=max(eye_y+16*curve,eye_y+29+(closed_pixels[index,1]-eye_y)*.8)
                        else:py=max(bottom+3,py)
            elif label=='left':
                x0,y0,x1,y1=bounds_side;px=x0+(z+.391)/(1.6807+.391)*(x1-x0)
                py=y0+(py-profile['bounds'][1])/(profile['bounds'][3]-profile['bounds'][1])*(y1-y0)
            else:
                x0,y0,x1,y1=bounds_back;px=x0+(.95-x)/1.90*(x1-x0)
                py=y0+(py-profile['bounds'][1])/(profile['bounds'][3]-profile['bounds'][1])*(y1-y0)
            uv.data[loop.index].uv=(px/718,1-py/718)
    weights=mesh.color_attributes.new(name='ProjectionWeights',type='FLOAT_COLOR',domain='CORNER')
    for loop in mesh.loops:
        x,y,z=raw[ids[loop.vertex_index]];angle=abs(math.atan2(x,z-.65))
        front=max(0,min(1,(math.radians(80)-angle)/math.radians(30)))
        back=max(0,min(1,(angle-math.radians(115))/math.radians(35)))
        weights.data[loop.index].color=(front,back,0,1)
    m=material('TownFace',(.65,.4,.3));nodes=m.node_tree.nodes;links=m.node_tree.links;textures={}
    for label in ('front','left','back'):
        image=bpy.data.images.load(str(refs/(label+'.png')));tex=nodes.new('ShaderNodeTexImage');tex.image=image;tex.extension='EXTEND';uv=nodes.new('ShaderNodeUVMap');uv.uv_map='Reference_'+label;links.new(uv.outputs[0],tex.inputs[0]);textures[label]=tex
    colors=nodes.new('ShaderNodeVertexColor');colors.layer_name='ProjectionWeights';split=nodes.new('ShaderNodeSeparateColor');links.new(colors.outputs[0],split.inputs[0])
    front=nodes.new('ShaderNodeMixRGB');links.new(split.outputs[0],front.inputs[0]);links.new(textures['left'].outputs[0],front.inputs[1]);links.new(textures['front'].outputs[0],front.inputs[2])
    back=nodes.new('ShaderNodeMixRGB');links.new(split.outputs[1],back.inputs[0]);links.new(front.outputs[0],back.inputs[1]);links.new(textures['back'].outputs[0],back.inputs[2]);links.new(back.outputs[0],nodes.get('Principled BSDF').inputs['Base Color']);mesh.materials.append(m)
    mesh.materials.append(material('TownOralCavity',(.045,.008,.012),.50))
    for p,source in zip(mesh.polygons,chosen_indices):
        if max(t[1]for t in face_uv[source])<.137 and np.mean(raw[faces[source],1])<7.05:p.material_index=1
    obj.shape_key_add(name='Basis')
    return obj,ids


def posed_source(raw,shape,data):
    posed=raw.copy()
    for target,weight in SHAPES[shape].items():
        for line in gzip.open(data/'targets/expression/units/caucasian'/(target+'.target.gz'),'rt'):
            f=line.split()
            if not f or f[0].startswith('#'):continue
            posed[int(f[0])]+=np.array(list(map(float,f[1:4])))*weight
    if shape.startswith('Lid'):
        side=1 if shape.endswith('Left')else-1;centre=np.array([side*.30775,7.28415,1.24535])
        delta=raw-centre;weight=np.clip(1-abs(delta[:,0])/.20,0,1)*np.clip(1-abs(delta[:,1])/.22,0,1)*np.clip((raw[:,2]-1.22)/.11,0,1)
        weight*=np.clip((raw[:,0]*side)/.1,0,1)
        angle=math.radians(11 if 'Up'in shape else-11)*weight
        posed[:,1]=centre[1]+delta[:,1]*np.cos(angle)+delta[:,2]*np.sin(angle)
        posed[:,2]=centre[2]+delta[:,2]*np.cos(angle)-delta[:,1]*np.sin(angle)
    return posed


def add_shapes(obj,ids,raw,name,data):
    for shape,recipes in SHAPES.items():
        posed=posed_source(raw,shape,data)
        key=obj.shape_key_add(name=shape)
        for v,co in zip(key.data,fit(posed[ids],name)):v.co=co


def oral_accessories(raw,name,data,head):
    parts=[head]
    for kind,stem,texture in [('teeth','teeth_base','teeth.png'),('tongue','tongue01','tongue01_diffuse.png')]:
        folder=data/kind/stem;source,faces,_,face_uv=parse_obj(folder/(stem+'.obj'))
        lines=(folder/(stem+'.mhclo')).read_text().splitlines();start=next(i for i,l in enumerate(lines)if l.startswith('verts '))+1
        records=[list(map(float,l.split()))for l in lines[start:start+len(source)]]
        def mapped(base):
            result=[]
            for r in records:
                if len(r)==1:result.append(base[int(r[0])])
                else:result.append(base[np.asarray(r[:3],dtype=int)].T@np.asarray(r[3:6])+np.asarray(r[6:9]))
            return fit(np.asarray(result),name)
        mesh=bpy.data.meshes.new(kind);mesh.from_pydata(mapped(raw).tolist(),[],faces);mesh.update()
        obj=bpy.data.objects.new(kind,mesh);bpy.context.collection.objects.link(obj)
        uv=mesh.uv_layers.new(name='OralTexture')
        for p,coords in zip(mesh.polygons,face_uv):
            p.use_smooth=True
            for loop,co in zip(p.loop_indices,coords):uv.data[loop].uv=co
        mat=material('Town'+kind.title(),(.5,.25,.20),.4);tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(folder/texture));mapping=mat.node_tree.nodes.new('ShaderNodeUVMap');mapping.uv_map='OralTexture';mat.node_tree.links.new(mapping.outputs[0],tex.inputs[0]);mat.node_tree.links.new(tex.outputs[0],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color']);mesh.materials.append(mat)
        obj.shape_key_add(name='Basis')
        for shape in SHAPES:
            key=obj.shape_key_add(name=shape)
            for v,co in zip(key.data,mapped(posed_source(raw,shape,data))):v.co=co
        parts.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for part in parts:part.select_set(True)
    bpy.context.view_layer.objects.active=head;bpy.ops.object.join()


def eyes(raw,name,data):
    groups=json.loads((data/'mesh_metadata/basemesh_vertex_groups.json').read_text());out={}
    eye_mat=material('TownEye',(.72,.70,.64),.28);tex=eye_mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(data/'eyes/materials'/('green_eye.png'if name=='enchantress'else'brown_eye.png')));eye_mat.node_tree.links.new(tex.outputs[0],eye_mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    cornea_mat=material('TownCornea',(.97,.99,1),.04);bsdf=cornea_mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Transmission Weight'].default_value=1;bsdf.inputs['IOR'].default_value=1.376
    for side in ('l','r'):
        indices=[i for a,b in groups['joint-'+side+'-eye']for i in range(a,b+1)];centre=raw[indices].mean(0);fitted=fit(np.array([centre]),name)[0]
        corners=fit(np.array([centre+[.14,0,0],centre+[0,.14,0],centre+[0,0,.146]]),name)-fitted
        scale=np.array([abs(corners[0,0]),abs(corners[2,1]),abs(corners[1,2])]);label='EyeLeft'if side=='l'else'EyeRight'
        pivot=bpy.data.objects.new(label,None);bpy.context.collection.objects.link(pivot);pivot.location=fitted
        verts=[];uvs=[];faces=[];segments=40
        # Front optical surface is negative Blender Y. The sclera terminates at
        # the limbus; a recessed iris annulus and deep pupil have actual depth.
        theta=.45;iris_radius=math.sin(theta);edge_depth=-math.cos(theta)
        rings=[(math.sin(t),-math.cos(t),'sclera')for t in np.linspace(theta,math.pi,19)]
        rings += [(r,edge_depth+.045*(1-r/iris_radius),'iris')for r in np.linspace(iris_radius,.13,5)]
        rings += [(.13,edge_depth+.10,'pupil'),(0,edge_depth+.10,'pupil')]
        for radius,depth,region in rings:
            for j in range(segments):
                angle=j*2*math.pi/segments;x=radius*math.cos(angle);z=radius*math.sin(angle)
                verts.append((x*scale[0],depth*scale[1],z*scale[2]))
                if region=='pupil':uvs.append((.704,.703))
                else:uvs.append((.704+x/iris_radius*.116,.703+z/iris_radius*.116))
        def connect(a,b):
            for j in range(segments):faces.append((a*segments+j,a*segments+(j+1)%segments,b*segments+(j+1)%segments,b*segments+j))
        for i in range(18):connect(i,i+1)
        for i in range(19,len(rings)-1):connect(i,i+1)
        # Separate coincident limbus ring joins both physical surfaces exactly.
        mesh=bpy.data.meshes.new(label+'Globe');mesh.from_pydata(verts,[],faces);mesh.update();uv=mesh.uv_layers.new(name='EyeAtlas')
        for loop in mesh.loops:uv.data[loop.index].uv=uvs[loop.vertex_index]
        obj=bpy.data.objects.new(label+'Globe',mesh);bpy.context.collection.objects.link(obj);obj.parent=pivot;mesh.materials.append(eye_mat)
        for polygon in mesh.polygons:polygon.use_smooth=True
        verts=[];faces=[]
        for i,t in enumerate(np.linspace(0,theta,9)):
            for j in range(segments):
                angle=j*2*math.pi/segments;verts.append((math.sin(t)*math.cos(angle)*scale[0],(-math.cos(t)-.002)*scale[1],math.sin(t)*math.sin(angle)*scale[2]))
        for i in range(8):
            for j in range(segments):faces.append((i*segments+j,(i+1)*segments+j,(i+1)*segments+(j+1)%segments,i*segments+(j+1)%segments))
        mesh=bpy.data.meshes.new(label+'Cornea');mesh.from_pydata(verts,[],faces);mesh.update();obj=bpy.data.objects.new(label+'Cornea',mesh);bpy.context.collection.objects.link(obj);obj.parent=pivot;mesh.materials.append(cornea_mat)
        for polygon in mesh.polygons:polygon.use_smooth=True
        out[label]={'centerBlender':fitted.tolist(),'radiiBlender':scale.tolist(),'opticalForwardBlender':[0,-1,0]}
    return out


def render(output,obj):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.render.resolution_x=800;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("ReviewWorld");scene.world.color=(.12,.12,.12)
    for loc,energy,size in [((-1,-2,3),130,1.6),((1,-.5,2),45,1.2)]:
        d=bpy.data.lights.new('Review','AREA');d.energy=energy;d.shape='DISK';d.size=size;l=bpy.data.objects.new('Review',d);scene.collection.objects.link(l);l.location=loc;l.rotation_euler=(Vector((0,0,1.6))-l.location).to_track_quat('-Z','Y').to_euler()
    c=bpy.data.cameras.new('FaceReview');c.type='ORTHO';c.ortho_scale=.37;camera=bpy.data.objects.new('FaceReview',c);scene.collection.objects.link(camera);scene.camera=camera
    for pose in ('neutral','blink','jaw','clay'):
        for key in obj.data.shape_keys.key_blocks:key.value=0
        if pose=='blink':obj.data.shape_keys.key_blocks['BlinkLeft'].value=1;obj.data.shape_keys.key_blocks['BlinkRight'].value=1
        if pose=='jaw':obj.data.shape_keys.key_blocks['JawOpen'].value=1
        if pose=='clay':
            obj.data.materials[0]=material('Clay',(.4,.4,.4),.7)
        for view,pos in [('front',(0,-2,1.6)),('oblique',(.8,-1.7,1.6))]:
            camera.location=pos;camera.rotation_euler=(Vector((0,-.015,1.6))-camera.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(output/(pose+'-'+view+'.png'));bpy.ops.render.render(write_still=True)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--data',type=Path,required=True);p.add_argument('--references',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--name',choices=PROFILES,required=True);p.add_argument('--render',action='store_true');a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True);raw,faces,groups,face_uv=parse_obj(a.data/'3dobjs/base.obj');
    for line in gzip.open(a.data/'targets/expression/units/caucasian/mouth-compression.target.gz','rt'):
        f=line.split()
        if f and not f[0].startswith('#'):raw[int(f[0])]+=np.array(list(map(float,f[1:4])))*.85
    obj,ids=head_mesh(raw,faces,groups,face_uv,a.name,a.references,a.data);add_shapes(obj,ids,raw,a.name,a.data);oral_accessories(raw,a.name,a.data,obj);eye=eyes(raw,a.name,a.data)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True);mod=obj.modifiers.new('Anatomical loop subdivision','SUBSURF');mod.levels=2;mod.render_levels=2
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'face-prototype.blend'))
    (a.output/'landmarks.json').write_text(json.dumps({'name':a.name,'eyes':eye,'headVertices':len(ids),'headQuads':len(obj.data.polygons),'shapes':list(SHAPES)},indent=2)+'\n')
    if a.render:render(a.output,obj)

if __name__=='__main__':main()
