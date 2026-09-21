#!/usr/bin/env python3
"""Fit the pinned CC0 anatomical hands and publish explicit finger/contact frames.

Offline Blender 4.2 asset authoring. Source geometry, weights and skin textures
remain unmodified; the output has real joint loops rather than deformed generated
finger shards. One atlas is shared by all three residents.
"""
import argparse, hashlib, importlib.util, json, math, sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Matrix, Vector

SKINS = {
    'merchant': 'skins/middleage_caucasian_male/middleage_lightskinned_male_diffuse.png',
    'priestess': 'skins/old_caucasian_female/old_lightskinned_female_diffuse.png',
    'enchantress': 'skins/young_caucasian_female/young_lightskinned_female_diffuse.png',
}
DIGITS = ('Thumb', 'Index', 'Middle', 'Ring', 'Little')


def load_template(path):
    vertices=[]; faces=[]; uv=[]; face_uv=[]; group=''
    for line in path.read_text().splitlines():
        f=line.split()
        if not f: continue
        if f[0]=='v': vertices.append(tuple(map(float,f[1:4])))
        elif f[0]=='vt': uv.append(tuple(map(float,f[1:3])))
        elif f[0]=='g': group=f[1]
        elif f[0]=='f' and group=='body':
            faces.append([int(x.split('/')[0])-1 for x in f[1:]])
            face_uv.append([uv[int(x.split('/')[1])-1] for x in f[1:]])
    return np.asarray(vertices),faces,face_uv


def make_hand(npc, rig, data, vertices, faces, face_uv, metadata, weights):
    def landmark(name):
        ids=[i for a,b in metadata[name] for i in range(a,b+1)]
        return Vector(vertices[ids].mean(0))
    source_wrist=landmark('joint-l-hand')
    forward=(landmark('joint-l-finger-3-1')-source_wrist).normalized()
    across=landmark('joint-l-finger-2-1')-landmark('joint-l-finger-5-1')
    across=(across-forward*across.dot(forward)).normalized()
    dorsal=across.cross(forward).normalized()
    source=Matrix((across,forward,dorsal)).transposed()
    wrist=rig.data.bones['Hand.L'].head_local.copy()
    target_forward=(rig.data.bones['Hand.L'].tail_local-wrist).normalized()
    target_across=Vector((0,-1,0));target_across=(target_across-target_forward*target_across.dot(target_forward)).normalized()
    target_dorsal=target_across.cross(target_forward).normalized()
    target=Matrix((target_across,target_forward,target_dorsal)).transposed()
    scale={'merchant':.104,'priestess':.096,'enchantress':.096}[npc]
    transform=target@source.transposed()
    def fitted(point, side):
        p=wrist+transform@(Vector(point)-source_wrist)*scale
        if side=='R':p.x=-p.x
        return p
    projection=(vertices-np.asarray(source_wrist))@np.asarray(forward)
    chosen=[i for i,f in enumerate(faces) if min(vertices[f,0])>3.5 and min(projection[f])>-.34]
    ids=sorted({i for face in chosen for i in faces[face]});index={old:new for new,old in enumerate(ids)}
    parts=[];contract={'version':1,'npc':npc,'joints':{},'contacts':{},'templateSourceVertexIds':ids}
    template_names={'hand_l':'Hand.L','lowerarm_l':'Forearm.L'}
    for digit,stem in zip(DIGITS,('thumb','index','middle','ring','pinky')):
        for j in range(1,4):template_names[f'{stem}_{j:02d}_l']=f'{digit}{j}.L'
    source_weights={name:dict(weights[name]) for name in template_names}
    for side in ('L','R'):
        coords=[fitted(vertices[i],side) for i in ids]
        polys=[[index[i] for i in faces[f]] for f in chosen]
        if side=='R':polys=[list(reversed(f)) for f in polys]
        mesh=bpy.data.meshes.new(npc+'Hand'+side);mesh.from_pydata(coords,[],polys);mesh.update()
        obj=bpy.data.objects.new(npc+'Hand'+side,mesh);bpy.context.collection.objects.link(obj)
        uv=mesh.uv_layers.new(name='OriginalSkin')
        for polygon,source_face in zip(mesh.polygons,chosen):
            polygon.use_smooth=True
            source_uv=face_uv[source_face] if side=='L' else list(reversed(face_uv[source_face]))
            for loop,point in zip(polygon.loop_indices,source_uv):uv.data[loop].uv=point
        definitions={name.replace('.L','.'+side):obj.vertex_groups.new(name=name.replace('.L','.'+side)) for name in template_names.values()}
        for local,source_id in enumerate(ids):
            values={template_names[n].replace('.L','.'+side):w.get(source_id,0) for n,w in source_weights.items()}
            total=sum(values.values());assert total>.99,(npc,source_id,total)
            for name,value in values.items():
                if value>0:definitions[name].add([local],value/total,'REPLACE')
        palm=-(target_dorsal.copy())
        if side=='R':palm.x=-palm.x
        for digit,d in zip(DIGITS,range(1,6)):
            for j in range(1,4):
                head=fitted(landmark(f'joint-l-finger-{d}-{j}'),side)
                tail=fitted(landmark(f'joint-l-finger-{d}-{j+1}'),side)
                contract['joints'][f'{digit}{j}.{side}']={'head':list(head),'tail':list(tail),'palmNormal':list(palm)}
        contract['joints']['Hand.'+side]={'head':list(fitted(source_wrist,side)),'tail':list(fitted(landmark('joint-l-hand-2'),side)),'palmNormal':list(palm)}
        # Contact points are authored in actor metres, with a shared palm-facing
        # normal. The activity solver can convert them into each bone's frame.
        centre=fitted((source_wrist+landmark('joint-l-finger-3-1'))*.5,side)
        underside=centre+palm*.009
        points={'PalmContact':underside,'PalmCentre':centre}
        for digit,d in zip(DIGITS,range(1,6)):
            points[digit+'Tip']=fitted(landmark(f'joint-l-finger-{d}-4'),side)
        contract['contacts'][side]={name:list(p) for name,p in points.items()}
        parts.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts:obj.select_set(True)
    bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();obj=parts[0];obj.name='Hands_'+npc
    mat=bpy.data.materials.new('SourceHandSkin_'+npc);mat.use_nodes=True
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(data/SKINS[npc]))
    uvnode=mat.node_tree.nodes.new('ShaderNodeUVMap');uvnode.uv_map='OriginalSkin'
    mat.node_tree.links.new(uvnode.outputs[0],tex.inputs[0]);mat.node_tree.links.new(tex.outputs[0],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color']);obj.data.materials.append(mat)
    return obj,contract


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--data',type=Path,required=True);p.add_argument('--rig-root',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    vertices,faces,face_uv=load_template(a.data/'3dobjs/base.obj');metadata=json.loads((a.data/'mesh_metadata/basemesh_vertex_groups.json').read_text());weights=json.loads((a.data/'rigs/weights.game_engine.json').read_text())['weights']
    objects=[];contracts=[]
    for npc in SKINS:
        with bpy.data.libraries.load(str(a.rig_root/npc/'rig-source.blend'),link=False)as(source,target):target.objects=[n for n in source.objects if n=='Skeleton']
        rig=target.objects[0];bpy.context.collection.objects.link(rig)
        obj,contract=make_hand(npc,rig,a.data,vertices,faces,face_uv,metadata,weights);objects.append(obj);contracts.append(contract);bpy.data.objects.remove(rig,do_unlink=True)
    atlas=bpy.data.images.new('TownHands545',width=2048,height=2048,alpha=False)
    for number,obj in enumerate(objects):
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
        uv=obj.data.uv_layers.new(name='HandAtlas');obj.data.uv_layers.active=uv
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(68),island_margin=.015,scale_to_bounds=True);bpy.ops.object.mode_set(mode='OBJECT')
        for loop in uv.data:loop.uv.y=(loop.uv.y*.97+number+.015)/3
        for mat in obj.data.materials:
            node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=atlas;mat.node_tree.nodes.active=node
        scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True;scene.render.bake.margin=8;scene.render.bake.use_clear=number==0
        bpy.ops.object.bake(type='DIFFUSE')
    atlas.filepath_raw=str(a.output/'hands_albedo.png');atlas.file_format='PNG';atlas.save()
    mat=bpy.data.materials.new('TownHands545');mat.use_nodes=True;tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=atlas;mat.node_tree.links.new(tex.outputs[0],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    for obj in objects:
        obj.data.materials.clear();obj.data.materials.append(mat)
        obj.data.uv_layers.remove(obj.data.uv_layers['OriginalSkin']);obj.data.uv_layers['HandAtlas'].active_render=True
    (a.output/'hand-contract.json').write_text(json.dumps({'version':1,'residents':contracts},indent=2)+'\n')
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'hand-source.blend'))

if __name__=='__main__':main()
