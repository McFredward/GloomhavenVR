"""Repair the open costume cuts around the fitted town NPC heads (Blender only).

Keep the original costume atlas. Remove obsolete disconnected 542 neck covers,
round the actual cut boundary and turn its edge inward as a sewn lining. The
lining occupies the inside of the existing garment, not a visible neck cylinder.
"""
import bmesh
import numpy as np
from mathutils import Vector


def repair(body, npc):
    bm=bmesh.new();bm.from_mesh(body.data)
    if npc!='merchant':
        # Skin fragments retained in the former colour-based head cut do not
        # belong to the hood/hair. Remove only the pale skin inside the new neck
        # footprint; preserve dark/purple hair and the actual cloth rim.
        mat=body.data.materials[0]
        shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        images=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
        image=next((image for image in images if 'basecolor' in image.name.lower()),images[0])
        rgba=np.asarray(image.pixels[:]).reshape(image.size[1],image.size[0],4)
        uv_layer=bm.loops.layers.uv.active;discard=[]
        for face in bm.faces:
            x,y,z=face.calc_center_median()
            if not(1.40<z<1.62 and abs(x)<.085 and y<.015):continue
            co=sum((loop[uv_layer].uv for loop in face.loops),Vector((0,0)))/len(face.loops)
            r,g,b=rgba[max(0,min(image.size[1]-1,int(co.y*image.size[1]))),max(0,min(image.size[0]-1,int(co.x*image.size[0]))),:3]
            if r>.32 and g>.25 and b>.20 and r>g*1.02 and r<b*1.9:discard.append(face)
        bmesh.ops.delete(bm,geom=discard,context='FACES')
    seen=set();remove=[]
    for vertex in bm.verts:
        if vertex in seen:continue
        queue=[vertex];seen.add(vertex);component=[]
        while queue:
            v=queue.pop();component.append(v)
            for edge in v.link_edges:
                other=edge.other_vert(v)
                if other not in seen:seen.add(other);queue.append(other)
        # The former neck cylinder and priestess wimple are detached components.
        # Neither exists in the original game portrait. Tiny cut-face islands and
        # loose decimator vertices in the same region are also obsolete.
        if len(component)<2000 and min(v.co.z for v in component)>1.35:
            remove.extend(component)
    bmesh.ops.delete(bm,geom=remove,context='VERTS')
    cut={v for e in bm.edges if e.is_boundary for v in e.verts
         if v.co.z>1.35 and abs(v.co.x)<.19}
    # Merge sub-millimetre source cuts before making a real, thin cloth hem.
    bmesh.ops.remove_doubles(bm,verts=list(cut),dist=.0012)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.00008)
    cut={v for e in bm.edges if e.is_boundary for v in e.verts if v.co.z>1.35 and abs(v.co.x)<.19}
    # Smooth only the cut itself, preserving nearby garment folds and the body.
    for _ in range(8):
        positions={v:v.co.lerp(sum((e.other_vert(v).co for e in v.link_edges if e.is_boundary),Vector())/
                    sum(1 for e in v.link_edges if e.is_boundary),.45)
                   for v in cut if sum(1 for e in v.link_edges if e.is_boundary)==2}
        for v,co in positions.items():v.co=co
    faces=[face for face in bm.faces if face.calc_center_median().z>1.35]
    edge_count=sum(1 for e in bm.edges if e.is_boundary and all(v in cut for v in e.verts))
    bmesh.ops.solidify(bm,geom=faces,thickness=-.003)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(body.data);bm.free();body.data.update()
    return {'removedDetachedVertices':len(remove),'linedEdges':edge_count}
