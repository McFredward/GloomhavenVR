"""Integrate authored anatomical hands without retaining generated finger shells."""
import json
from pathlib import Path
import bpy
import bmesh
from mathutils import Matrix, Vector


def load(rig, folder, npc):
    contract=next(row for row in json.loads((folder/'hand-contract.json').read_text())['residents'] if row['npc']==npc)
    with bpy.data.libraries.load(str(folder/'hand-source.blend'),link=False) as (source,target):
        target.objects=['Hands_'+npc]
    hands=target.objects[0];bpy.context.collection.objects.link(hands)
    bpy.context.view_layer.objects.active=rig;rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for name,definition in contract['joints'].items():
        bone=rig.data.edit_bones[name]
        bone.head=definition['head'];bone.tail=definition['tail']
        bone.align_roll(Vector(definition['palmNormal']))
    bpy.ops.object.mode_set(mode='OBJECT')
    # Markers are attached by the Unity builder while the imported rig is in
    # its neutral frame. Their actual skin contact replaces guessed wrist offsets.
    for side,points in contract['contacts'].items():
        for name,point in points.items():
            marker=bpy.data.objects.new(name+'.'+side,None)
            bpy.context.collection.objects.link(marker);marker.location=point
            bone_name=(name[:-3]+'3.'+side) if name.endswith('Tip') else 'Hand.'+side
            marker['TownHandBone']=bone_name
            definition=contract['joints'][bone_name]
            forward=(Vector(definition['tail'])-Vector(definition['head'])).normalized()
            palm=Vector(definition['palmNormal']);palm=(palm-forward*palm.dot(forward)).normalized()
            hinge=forward.cross(palm).normalized()
            marker.rotation_euler=Matrix((hinge,forward,palm)).transposed().to_euler()
    return hands,contract


def remove_generated_shell(body, rig, contract):
    bm=bmesh.new();bm.from_mesh(body.data);before=len(bm.faces)
    for side in ('L','R'):
        bone=rig.data.bones['Hand.'+side]
        wrist=Vector(contract['joints']['Hand.'+side]['head'])
        forward=(bone.tail_local-bone.head_local).normalized()
        plane=wrist+forward*.006
        deform=bm.verts.layers.deform.active
        hand_groups={group.index for group in body.vertex_groups if group.name.endswith('.'+side) and
                     (group.name.startswith('Hand.') or any(group.name.startswith(digit) for digit in ('Thumb','Index','Middle','Ring','Little')))}
        region=[]
        for face in bm.faces:
            inside=False
            for vertex in face.verts:
                offset=vertex.co-wrist;along=offset.dot(forward)
                radial=(offset-forward*along).length
                if (along>-.025 and radial<.090) or sum(vertex[deform].get(group,0) for group in hand_groups)>.025:
                    inside=True;break
            if inside:region.append(face)
        edges={e for f in region for e in f.edges};vertices={v for f in region for v in f.verts}
        bmesh.ops.bisect_plane(bm,geom=region+list(edges)+list(vertices),dist=.000001,
                              plane_co=plane,plane_no=forward,clear_outer=True,clear_inner=False)
    bm.to_mesh(body.data);after=len(bm.faces);bm.free();body.data.update()
    return {'removedSourceFaces':before-after,'cuffCutMeters':.006}


def lod_copy(source, level):
    obj=source.copy();obj.data=source.data.copy();bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    if level==0:
        modifier=obj.modifiers.new('Anatomical joint loops','SUBSURF');modifier.levels=1;modifier.render_levels=1;modifier.uv_smooth='NONE'
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    elif level==2:
        modifier=obj.modifiers.new('Hand distance LOD','DECIMATE');modifier.ratio=.65
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    for vertex in obj.data.vertices:
        weights=sorted(vertex.groups,key=lambda g:g.weight,reverse=True)
        assert sum(group.weight for group in weights)>.99,'Anatomical hand lost skinning weights'
        for group in weights[4:]:obj.vertex_groups[group.group].remove([vertex.index])
        total=sum(group.weight for group in vertex.groups)
        assert total>.80,'Four-weight hand reduction discarded excessive joint influence'
        for group in list(vertex.groups):obj.vertex_groups[group.group].add([vertex.index],group.weight/total,'REPLACE')
    obj.data.uv_layers.active.name='FaceAtlas';obj.data.uv_layers['FaceAtlas'].active_render=True
    return obj
