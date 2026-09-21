#!/usr/bin/env python3
"""Bake fitted anatomical face assets and replace only the previous facial shell.

Blender 4.2 authoring. Body actions and proportions remain unchanged; explicit
neckline/garment support repairs are recorded per LOD. Facial loops are subdivided coherently across every expression; teeth
and tongue keep their authored topology rather than quadrupling invisible molars.
"""
import argparse, hashlib, json, math, shutil, sys
from pathlib import Path
import bpy, bmesh
import numpy as np
from mathutils import Matrix, Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from town_npc_necklines import repair as repair_neckline
import town_npc_hand_integration as hand_assets


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
    for group in source.vertex_groups:
        new=obj.vertex_groups.new(name=group.name)
        for index,old in enumerate(ids):
            for weight in source.data.vertices[old].groups:
                if weight.group==group.index:new.add([index],weight.weight,'REPLACE')
    if source.data.shape_keys:
        for old in source.data.shape_keys.key_blocks:
            key=obj.shape_key_add(name=old.name)
            for v,i in zip(key.data,ids):v.co=old.data[i].co
    return obj



def native_neck_faces(body):
    """Select only the original exposed neck skin, behind the blouse and chain.

    The bounded patch retains its native geometry. Cloth is neutral linen and
    the gold chain has a much lower blue/green ratio than this warm skin patch.
    """
    image=next(n.image for n in body.data.materials[0].node_tree.nodes
               if n.type=='TEX_IMAGE' and n.image)
    rgba=np.asarray(image.pixels[:]).reshape(image.size[1],image.size[0],4)
    uv=body.data.uv_layers.active;result=set()
    for polygon in body.data.polygons:
        if polygon.material_index!=0:continue
        x,y,z=polygon.center
        if not(1.375<z<1.445 and abs(x)<.074 and -.105<y<.025):continue
        co=sum((uv.data[i].uv for i in polygon.loop_indices),Vector((0,0)))/len(polygon.loop_indices)
        r,g,b=rgba[max(0,min(image.size[1]-1,int(co.y*image.size[1]))),max(0,min(image.size[0]-1,int(co.x*image.size[0]))),:3]
        if r>.25 and r>g*1.12 and b>g*.67:result.add(polygon.index)
    return result


def evaluated_shapes(source,level,name):
    source.hide_viewport=False
    modifier=source.modifiers.new('Coherent facial loop subdivision','SUBSURF');modifier.levels=level;modifier.render_levels=level;modifier.uv_smooth='NONE'
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
    # Evaluated meshes retain deform indices, but their object group definitions
    # must be restored in the same order for subdivision-interpolated weights.
    for group in source.vertex_groups:obj.vertex_groups.new(name=group.name)
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
    for uv_name in [uv.name for uv in head.data.uv_layers]:
        if uv_name!='FaceAtlas':head.data.uv_layers.remove(head.data.uv_layers[uv_name])
    head.data.uv_layers['FaceAtlas'].active_render=True
    return mat


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--prototype',type=Path,required=True);p.add_argument('--rig',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--name',required=True);p.add_argument('--baked-head',type=Path);p.add_argument('--face-only',action='store_true');p.add_argument('--hands',type=Path);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(a.baked_head or a.prototype/'face-prototype.blend'));head=bpy.data.objects['Face']
    for key in head.data.shape_keys.key_blocks:key.value=0
    # The template includes the actual lower neck/clavicle loops; no boundary
    # extrusion or detached neck-cover geometry is needed.
    if a.baked_head:
        shutil.copyfile(a.baked_head.parent/'face_albedo.png',a.output/'face_albedo.png')
        face_material=head.data.materials[0]
    else:
        face_material=bake(head,a.output)
        bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'face-baked.blend'))
    if a.face_only:return
    facial=subset(head,lambda p:p.material_index<2,'AnatomicalFace');oral=subset(head,lambda p:p.material_index>=2,'OralAnatomy');bpy.data.objects.remove(head,do_unlink=True)
    eyes=[o for o in bpy.context.scene.objects if o.name.startswith('Eye')]
    for obj in list(bpy.context.scene.objects):
        if obj not in eyes+[facial,oral]:bpy.data.objects.remove(obj,do_unlink=True)
    with bpy.data.libraries.load(str(a.rig),link=False)as(source,target):
        target.objects=[n for n in source.objects if n=='Skeleton'or n.startswith('LOD')]
        target.actions=[n for n in source.actions if n in ('Idle','Greeting','Gesture','ReturnToIdle')]
    for action in target.actions:action.use_fake_user=True
    for obj in target.objects:bpy.context.collection.objects.link(obj)
    rig=next(o for o in target.objects if o.type=='ARMATURE');rig.animation_data.action=None;rig.data.pose_position='REST'
    hands=hand_contract=None
    if a.hands:
        hands,hand_contract=hand_assets.load(rig,a.hands,a.name)
        bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
        # Head rotation belongs at the occipital joint, not below the mandible.
        # The old 1.51 m pivot pulled the nape into a long column when bowing.
        rig.data.edit_bones['Head'].head.z=1.565
        rig.data.edit_bones['Neck'].tail.z=1.565
        bpy.ops.object.mode_set(mode='OBJECT')
    # Geometry is in metres in Blender; retain authored weights and actions exactly.
    records=[]
    for level in range(3):
        body=next(o for o in target.objects if o.name.startswith('LOD'+str(level)+'_'))
        bm=bmesh.new();bm.from_mesh(body.data);remove=[f for f in bm.faces if not body.data.materials[f.material_index].name.startswith('TownBody')];bmesh.ops.delete(bm,geom=remove,context='FACES');bm.to_mesh(body.data);bm.free()
        body.data.update()
        if a.name=='priestess':
            selected=native_neck_faces(body)
            bm=bmesh.new();bm.from_mesh(body.data);bm.faces.ensure_lookup_table()
            bmesh.ops.delete(bm,geom=[bm.faces[i] for i in selected],context='FACES')
            bm.to_mesh(body.data);bm.free();body.data.update()
        # The old skin cut must precede garment boundary reconstruction/lining.
        neckline=repair_neckline(body,a.name);print('NECKLINE_REPAIR',a.name,level,neckline)
        hand_repair=hand_assets.remove_generated_shell(body,rig,hand_contract) if hands else None
        part=evaluated_shapes(facial,1 if level==0 else 0,'FaceLOD'+str(level));teeth=evaluated_shapes(oral,0,'OralLOD'+str(level));join([part,teeth],part)
        body_matrix=body.matrix_world.copy();body.parent=None;body.matrix_world=body_matrix
        original_uv=body.data.uv_layers.active.name
        for uv_name in [uv.name for uv in body.data.uv_layers]:
            if uv_name!=original_uv:body.data.uv_layers.remove(body.data.uv_layers[uv_name])
        body.data.uv_layers[original_uv].name='FaceAtlas';body.data.uv_layers['FaceAtlas'].active_render=True
        body.modifiers.clear()
        for key in part.data.shape_keys.key_blocks:body.shape_key_add(name=key.name)
        hand_part=hand_assets.lod_copy(hands,level) if hands else None
        part=join([body,part]+([hand_part] if hand_part else []),body);part.name='LOD'+str(level)+'_0'
        # Stable two-slot runtime contract: original costume, new baked face/oral atlas.
        old_materials=list(part.data.materials);bodymat=next(m for m in old_materials if m.name.startswith('TownBody'));indices=[0 if old_materials[p.material_index].name.startswith('TownBody') else (2 if old_materials[p.material_index].name.startswith('TownHands') else 1) for p in part.data.polygons];part.data.materials.clear();part.data.materials.append(bodymat);part.data.materials.append(face_material)
        if hands:part.data.materials.append(hands.data.materials[0])
        for poly,index in zip(part.data.polygons,indices):poly.material_index=index
        # A second UV channel carries independent template-region membership
        # through FBX UV splits. It is validation metadata, never a rendered UV.
        contract=part.data.uv_layers.new(name='RigContract')
        marker={g.index:g.name for g in part.vertex_groups if g.name in ('ContractSkull','ContractJaw')}
        for loop in part.data.loops:
            values={marker[w.group]:w.weight for w in part.data.vertices[loop.vertex_index].groups if w.group in marker}
            contract.data[loop.index].uv=(values.get('ContractSkull',0),values.get('ContractJaw',0))
        part.data.uv_layers['FaceAtlas'].active_render=True
        part.parent=rig;modifier=part.modifiers.new('Station skeleton','ARMATURE');modifier.object=rig;modifier.use_vertex_groups=True
        records.append({'handRepair':hand_repair,'necklineRepair':neckline,'name':part.name,'vertices':len(part.data.vertices),'triangles':sum(len(p.vertices)-2 for p in part.data.polygons),'shapes':[k.name for k in part.data.shape_keys.key_blocks][1:]})
    bpy.data.objects.remove(facial,do_unlink=True);bpy.data.objects.remove(oral,do_unlink=True)
    if hands:bpy.data.objects.remove(hands,do_unlink=True)
    # Optical frames are authored explicitly; Unity attaches these existing pivots
    # to Head while still in bind pose, before sampling station animation.
    rotation=Matrix.Rotation(math.pi/2,4,'X')
    for pivot in [o for o in eyes if o.type=='EMPTY']:
        for child in pivot.children:child.data.transform(rotation.inverted())
        pivot.rotation_euler=(math.pi/2,0,0)
        for child in pivot.children:
            bm=bmesh.new();bm.from_mesh(child.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(child.data);bm.free();child.data.update()
            if child.name.endswith('Globe'):
                limit=max(v.co.z for v in child.data.vertices)*.82
                assert all(p.center.dot(p.normal)>0 for p in child.data.polygons if p.center.z<limit),'Sclera normals must face outwards'
            else:assert sum(v.normal.z for v in child.data.vertices)>0,'Cornea must face optical +Z'
    rig.data.pose_position='POSE';rig.animation_data.action=next((x for x in bpy.data.actions if x.name=='Idle'),None)
    bpy.context.scene.frame_set(1);bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'rig-source.blend'))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        if obj.type in ('MESH','ARMATURE','EMPTY'):obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(a.output/(a.name+'_rig.fbx')),use_selection=True,object_types={'MESH','ARMATURE','EMPTY'},add_leaf_bones=False,bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='STRIP')
    (a.output/'facial-rig.json').write_text(json.dumps({'name':a.name,'lods':records,'prototypeSha256':hashlib.sha256((a.prototype/'face-prototype.blend').read_bytes()).hexdigest(),'bakedHeadSha256':hashlib.sha256(a.baked_head.read_bytes()).hexdigest() if a.baked_head else None,'rigSha256':hashlib.sha256(a.rig.read_bytes()).hexdigest(),'sourceScriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'eyes':json.loads((a.prototype/'landmarks.json').read_text())['eyes']},indent=2)+'\n')

if __name__=='__main__':main()
