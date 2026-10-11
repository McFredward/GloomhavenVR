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
    extend_wrist_skin(hands, rig, contract)
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
            bone_name=(name[:-3]+'3.'+side) if name.endswith(('Tip','Pad')) else 'Hand.'+side
            marker['TownHandBone']=bone_name
            definition=contract['joints'][bone_name]
            forward=(Vector(definition['tail'])-Vector(definition['head'])).normalized()
            palm=Vector(definition['palmNormal']);palm=(palm-forward*palm.dot(forward)).normalized()
            hinge=forward.cross(palm).normalized()
            marker.rotation_euler=Matrix((hinge,forward,palm)).transposed().to_euler()
    return hands,contract


def extend_wrist_skin(hands, rig, contract):
    """Continue the real anatomical wrist into the sleeve during palm rotation.

    The former 14 mm sleeve overlap moved out of the source cut when the palm
    turned upwards. The proximal skin must belong to the forearm, while the
    hand can pronate independently; closing a sleeve with an unrelated disk
    would merely hide that missing anatomy.
    """
    bm=bmesh.new();bm.from_mesh(hands.data)
    deform=bm.verts.layers.deform.active;uv=bm.loops.layers.uv.active
    for side in ('L','R'):
        wrist=Vector(contract['joints']['Hand.'+side]['head'])
        direction=(Vector(contract['joints']['Hand.'+side]['tail'])-wrist).normalized()
        arm=rig.data.bones['Forearm.'+side]
        arm_direction=(arm.tail_local-arm.head_local).normalized()
        alignment=direction.rotation_difference(arm_direction)
        forearm=hands.vertex_groups['Forearm.'+side].index
        # Only the anatomical cuff is an open boundary; finger/nail topology
        # remains untouched. The two hands are separate connected components.
        edges=[edge for edge in bm.edges if edge.is_boundary and all(
            -.06<(v.co-wrist).dot(direction)<-.005 and
            ((v.co-wrist)-direction*(v.co-wrist).dot(direction)).length<.06
            for v in edge.verts)]
        assert len(edges)>=8, ('Anatomical wrist boundary missing',side,len(edges))
        originals={v for edge in edges for v in edge.verts}
        skin_uv={vertex:vertex.link_loops[0][uv].uv.copy() for vertex in originals}
        original_position={vertex:vertex.co.copy() for vertex in originals}
        original_vertex={vertex:vertex for vertex in originals}
        edge_uv={frozenset(edge.verts):{loop.vert:loop[uv].uv.copy()
                  for loop in edge.link_faces[0].loops if loop.vert in edge.verts}
                 for edge in edges}
        # Match the actual source forearm at the shared cuff rather than
        # allowing residual Hand weights to rotate the hidden skin seam away.
        for vertex in bm.verts:
            delta=vertex.co-wrist;along=delta.dot(direction)
            if not(-.06<along<-.002 and (delta-direction*along).length<.06):continue
            t=max(0,min(1,(-.002-along)/.022));t=t*t*(3-2*t)
            weights=vertex[deform]
            for group in list(weights.keys()):weights[group]*=1-t
            weights[forearm]=weights.get(forearm,0)+t
        ring=edges
        for row in range(4):
            previous={v for edge in ring for v in edge.verts}
            extruded=bmesh.ops.extrude_edge_only(bm,edges=ring)
            newverts=[item for item in extruded['geom'] if isinstance(item,bmesh.types.BMVert)]
            newset=set(newverts)
            for vertex in newverts:
                source=min(previous,key=lambda v:(v.co-vertex.co).length_squared)
                skin_uv[vertex]=skin_uv[source].copy()
                original_position[vertex]=original_position[source]
                original_vertex[vertex]=original_vertex[source]
                offset=original_position[vertex]-wrist
                depth=-offset.dot(direction)
                radial=offset+direction*depth
                # The forearm is not collinear with the fitted hand. Follow
                # its real bone centreline, tapering inside the sleeve rather
                # than letting a straight wrist extrusion pierce the cloth.
                t=(row+1)/4
                rotation=Matrix.Identity(3).to_quaternion().slerp(alignment,t)
                centre=wrist-arm_direction*(depth+.0175*(row+1))
                vertex.co=centre+(rotation@radial)*(1-.30*t)
                vertex[deform].clear();vertex[deform][forearm]=1
            for face in [item for item in extruded['geom'] if isinstance(item,bmesh.types.BMFace)]:
                face.material_index=0;face.smooth=True
                # Keep each original UV seam per corner. Taking one UV per
                # vertex would span unrelated atlas islands at split seams and
                # paint dark stripes onto an otherwise continuous wrist.
                source_uv=edge_uv[frozenset(original_vertex[v] for v in face.verts)]
                for loop in face.loops:loop[uv].uv=source_uv[original_vertex[loop.vert]]
            ring=[item for item in extruded['geom'] if isinstance(item,bmesh.types.BMEdge)
                  and all(v in newset for v in item.verts)]
            assert len(ring)==len(edges), ('Wrist extension lost topology',side,row)
        # The hidden proximal end has real skin topology too; the sleeve can
        # never reveal an open hole through a skin tube in an extreme pose.
        caps=bmesh.ops.holes_fill(bm,edges=ring,sides=0)['faces']
        for face in caps:
            face.material_index=0;face.smooth=True
            sample=next(iter(skin_uv.values()))
            for loop in face.loops:loop[uv].uv=sample
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    for vertex in bm.verts:
        total=sum(vertex[deform].values())
        assert .999<total<1.001, ('Wrist weights must remain normalized',total)
    bm.to_mesh(hands.data);bm.free();hands.data.update()


def remove_generated_shell(body, rig, contract):
    bm=bmesh.new();bm.from_mesh(body.data);before=len(bm.faces)
    for side in ('L','R'):
        bone=rig.data.bones['Hand.'+side]
        wrist=Vector(contract['joints']['Hand.'+side]['head'])
        forward=(bone.tail_local-bone.head_local).normalized()
        plane=wrist-forward*.020
        deform=bm.verts.layers.deform.active
        hand_groups={group.index for group in body.vertex_groups if group.name.endswith('.'+side) and
                     (group.name.startswith('Hand.') or any(group.name.startswith(digit) for digit in ('Thumb','Index','Middle','Ring','Little')))}
        region=[]
        for face in bm.faces:
            inside=False
            for vertex in face.verts:
                offset=vertex.co-wrist;along=offset.dot(forward)
                radial=(offset-forward*along).length
                if (along>-.055 and radial<.090) or sum(vertex[deform].get(group,0) for group in hand_groups)>.025:
                    inside=True;break
            if inside:region.append(face)
        edges={e for f in region for e in f.edges};vertices={v for f in region for v in f.verts}
        bmesh.ops.bisect_plane(bm,geom=region+list(edges)+list(vertices),dist=.000001,
                              plane_co=plane,plane_no=forward,clear_outer=True,clear_inner=False)
    bm.to_mesh(body.data);after=len(bm.faces);bm.free();body.data.update()
    return {'removedSourceFaces':before-after,'cuffCutMeters':-.020}


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
