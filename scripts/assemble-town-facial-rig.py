#!/usr/bin/env python3
"""Bake fitted anatomical face assets and replace only the previous facial shell.

Blender 4.2 authoring. Costume geometry, existing weights and body actions remain
unchanged. Facial loops are subdivided coherently across every expression; teeth
and tongue keep their authored topology rather than quadrupling invisible molars.
"""
import argparse, hashlib, json, math, sys
from pathlib import Path
import bpy, bmesh
import numpy as np
from mathutils import Matrix, Vector


def subset(source, predicate, name):
    polygons=[p for p in source.data.polygons if predicate(p)]
    ids=sorted({v for p in polygons for v in p.vertices});mapping={v:i for i,v in enumerate(ids)}
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([source.data.vertices[i].co[:]for i in ids],[],[[mapping[i]for i in p.vertices]for p in polygons]);mesh.update()
    for mat in source.data.materials:mesh.materials.append(mat)
    for p,old in zip(mesh.polygons,polygons):p.material_index=old.material_index;p.use_smooth=True
    for old in source.data.uv_layers:
        uv=mesh.uv_layers.new(name=old.name)
        for p,orig in zip(mesh.polygons,polygons):
            for new_loop,old_loop in zip(p.loop_indices,orig.loop_indices):uv.data[new_loop].uv=old.data[old_loop].uv
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    if source.data.shape_keys:
        for old in source.data.shape_keys.key_blocks:
            key=obj.shape_key_add(name=old.name)
            for v,i in zip(key.data,ids):v.co=old.data[i].co
    return obj


def evaluated_shapes(source,level,name):
    source.hide_viewport=False
    modifier=source.modifiers.new('Coherent facial loop subdivision','SUBSURF');modifier.levels=level;modifier.render_levels=level
    deps=bpy.context.evaluated_depsgraph_get();shapes={};mesh=None
    for i,key in enumerate(source.data.shape_keys.key_blocks):
        for other in source.data.shape_keys.key_blocks:other.value=0
        if i:key.value=1
        source.data.update();bpy.context.view_layer.update();deps.update()
        current=bpy.data.meshes.new_from_object(source.evaluated_get(deps),preserve_all_data_layers=True,depsgraph=deps)
        if mesh is None:mesh=current
        else:
            shapes[key.name]=[v.co.copy()for v in current.vertices];bpy.data.meshes.remove(current)
    for key in source.data.shape_keys.key_blocks:key.value=0
    source.modifiers.remove(modifier)
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);obj.shape_key_add(name='Basis')
    for name,coords in shapes.items():
        key=obj.shape_key_add(name=name)
        for v,co in zip(key.data,coords):v.co=co
    return obj


def join(parts,active):
    bpy.ops.object.select_all(action='DESELECT')
    for part in parts:part.select_set(True)
    bpy.context.view_layer.objects.active=active;bpy.ops.object.join();return active


def bake(head,output):
    head.modifiers.clear();bpy.ops.object.select_all(action='DESELECT');head.select_set(True);bpy.context.view_layer.objects.active=head
    head.data.uv_layers.new(name='FaceAtlas');head.data.uv_layers.active_index=len(head.data.uv_layers)-1
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(68),island_margin=.006,scale_to_bounds=True);bpy.ops.object.mode_set(mode='OBJECT')
    image=bpy.data.images.new('TownFaceAtlas',width=4096,height=4096,alpha=False)
    for mat in head.data.materials:
        node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.nodes.active=node
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True;scene.render.bake.margin=16
    bpy.ops.object.bake(type='DIFFUSE');image.filepath_raw=str(output/'face_albedo.png');image.file_format='PNG';image.save()
    mat=bpy.data.materials.new('TownFace543');mat.use_nodes=True;node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.links.new(node.outputs[0],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color']);mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.65
    # Preserve the facial/inner partition until separate subdivision is complete.
    for i in range(len(head.data.materials)):head.data.materials[i]=mat
    for uv in list(head.data.uv_layers):
        if uv.name!='FaceAtlas':head.data.uv_layers.remove(uv)
    return mat


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--prototype',type=Path,required=True);p.add_argument('--rig',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--name',required=True);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(a.prototype/'face-prototype.blend'));head=bpy.data.objects['Face']
    for key in head.data.shape_keys.key_blocks:key.value=0
    face_material=bake(head,a.output)
    facial=subset(head,lambda p:p.material_index<2,'AnatomicalFace');oral=subset(head,lambda p:p.material_index>=2,'OralAnatomy');bpy.data.objects.remove(head,do_unlink=True)
    eyes=[o for o in bpy.context.scene.objects if o.name.startswith('Eye')]
    for obj in list(bpy.context.scene.objects):
        if obj not in eyes+[facial,oral]:bpy.data.objects.remove(obj,do_unlink=True)
    with bpy.data.libraries.load(str(a.rig),link=False)as(source,target):
        target.objects=[n for n in source.objects if n=='Skeleton'or n.startswith('LOD')]
        target.actions=[n for n in source.actions if n in ('Idle','Greeting','Gesture','ReturnToIdle')]
    for obj in target.objects:bpy.context.collection.objects.link(obj)
    rig=next(o for o in target.objects if o.type=='ARMATURE');rig.animation_data.action=None;rig.data.pose_position='REST'
    # Geometry is in metres in Blender; retain authored weights and actions exactly.
    records=[]
    for level in range(3):
        body=next(o for o in target.objects if o.name.startswith('LOD'+str(level)+'_'))
        bm=bmesh.new();bm.from_mesh(body.data);remove=[f for f in bm.faces if not body.data.materials[f.material_index].name.startswith('TownBody')];bmesh.ops.delete(bm,geom=remove,context='FACES');bm.to_mesh(body.data);bm.free()
        part=evaluated_shapes(facial,1 if level==0 else 0,'FaceLOD'+str(level));teeth=evaluated_shapes(oral,0,'OralLOD'+str(level));join([part,teeth],part)
        for group_name in ('Head','Neck'):part.vertex_groups.new(name=group_name)
        for v in part.data.vertices:
            weight=max(0,min(1,(v.co.z-1.48)/.07));weight=weight*weight*(3-2*weight)
            part.vertex_groups['Head'].add([v.index],weight,'REPLACE');part.vertex_groups['Neck'].add([v.index],1-weight,'REPLACE')
        body_matrix=body.matrix_world.copy();body.parent=None;body.matrix_world=body_matrix
        original_uv=body.data.uv_layers.active
        for uv in list(body.data.uv_layers):
            if uv!=original_uv:body.data.uv_layers.remove(uv)
        original_uv.name='FaceAtlas'
        part=join([part,body],part);part.name='LOD'+str(level)+'_0'
        # Stable two-slot runtime contract: original costume, new baked face/oral atlas.
        old_materials=list(part.data.materials);bodymat=next(m for m in old_materials if m.name.startswith('TownBody'));indices=[0 if old_materials[p.material_index].name.startswith('TownBody')else 1 for p in part.data.polygons];part.data.materials.clear();part.data.materials.append(bodymat);part.data.materials.append(face_material)
        for poly,index in zip(part.data.polygons,indices):poly.material_index=index
        part.parent=rig;modifier=part.modifiers.new('Station skeleton','ARMATURE');modifier.object=rig;modifier.use_vertex_groups=True
        records.append({'name':part.name,'vertices':len(part.data.vertices),'triangles':sum(len(p.vertices)-2 for p in part.data.polygons),'shapes':[k.name for k in part.data.shape_keys.key_blocks][1:]})
    bpy.data.objects.remove(facial,do_unlink=True);bpy.data.objects.remove(oral,do_unlink=True)
    # Optical frames are authored explicitly; Unity attaches these existing pivots
    # to Head while still in bind pose, before sampling station animation.
    rotation=Matrix.Rotation(math.pi/2,4,'X')
    for pivot in [o for o in eyes if o.type=='EMPTY']:
        for child in pivot.children:child.data.transform(rotation.inverted())
        pivot.rotation_euler=(math.pi/2,0,0)
        for child in pivot.children:
            bm=bmesh.new();bm.from_mesh(child.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(child.data);bm.free();child.data.update()
    rig.data.pose_position='POSE';rig.animation_data.action=next((x for x in bpy.data.actions if x.name=='Idle'),None)
    bpy.context.scene.frame_set(1);bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'rig-source.blend'))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        if obj.type in ('MESH','ARMATURE','EMPTY'):obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(a.output/(a.name+'_rig.fbx')),use_selection=True,object_types={'MESH','ARMATURE','EMPTY'},add_leaf_bones=False,bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='STRIP')
    (a.output/'facial-rig.json').write_text(json.dumps({'name':a.name,'lods':records,'prototypeSha256':hashlib.sha256((a.prototype/'face-prototype.blend').read_bytes()).hexdigest(),'rigSha256':hashlib.sha256(a.rig.read_bytes()).hexdigest(),'sourceScriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'eyes':json.loads((a.prototype/'landmarks.json').read_text())['eyes']},indent=2)+'\n')

if __name__=='__main__':main()
