"""Repair the open costume cuts around the fitted town NPC heads (Blender only).

Keep the original costume atlas. Remove obsolete disconnected 542 neck covers,
retain a lowered cloth neckline, and turn the actual cut edges inward as a sewn lining. The
lining occupies the inside of the existing garment, not a visible neck cylinder.
"""
import bmesh
import math
import numpy as np
from mathutils import Vector
from town_npc_garment_weights import garment_support
from town_npc_cloth_edges import repair_cloth_edges
from town_npc_neck_inset import sew_inner_shirt


def shirt_band(bm, body, npc):
    """A sewn shirt opening below the jaw; it never follows the skull."""
    # Priestess retains her original blouse opening; no overlay strip is added.
    if npc!='merchant':return 0
    uv=bm.loops.layers.uv.active;deform=bm.verts.layers.deform.active
    chest=body.vertex_groups['Chest'].index
    # Reuse a clean patch from this exact costume's original linen atlas.
    sample=(.5670,.6467) if npc=='merchant' else (.9710,.4420)
    top,drop,rx,ry,cy=(1.486,.020,.066,.070,.024) if npc=='merchant' else (1.445,.029,.083,.074,.016)
    rings=[];n=64
    # A real folded collar has a front opening and two descending points. It is
    # not a closed cylindrical band around the throat.
    for row in range(7):
        t=row/6;ring=[]
        for i in range(n):
            angle=math.radians(16)+(2*math.pi-math.radians(32))*i/(n-1)
            fold=.004*math.sin(math.pi*t)
            front=max(0,math.cos(angle))
            x=(rx+t*.027+fold)*math.sin(angle)
            y=cy-(ry+t*.035+fold)*math.cos(angle)
            z=top-drop*math.cos(angle)-.020*(1-t)-t*(.038+.018*front**4)+.035*math.sin(math.pi*t)
            z+=.0025*math.sin(angle*7)*math.sin(math.pi*t)
            vertex=bm.verts.new((x,y,z));vertex[deform][chest]=1;ring.append(vertex)
        rings.append(ring)
    for row in range(6):
        for i in range(n-1):
            face=bm.faces.new((rings[row][i],rings[row+1][i],rings[row+1][i+1],rings[row][i+1]));face.material_index=0;face.smooth=True
            for loop in face.loops:
                loop[uv].uv=(sample[0]+loop.vert.co.x*.025,sample[1]+(loop.vert.co.z-top)*.025)
    return n*7


def repair(body, npc, neck=None):
    bm=bmesh.new();bm.from_mesh(body.data)
    seen=set();remove=[];removed_components=[]
    for vertex in bm.verts:
        if vertex in seen:continue
        queue=[vertex];seen.add(vertex);component=[]
        while queue:
            v=queue.pop();component.append(v)
            for edge in v.link_edges:
                other=edge.other_vert(v)
                if other not in seen:seen.add(other);queue.append(other)
        faces=set(f for v in component for f in v.link_faces)
        low=[min(v.co[i]for v in component)for i in range(3)]
        high=[max(v.co[i]for v in component)for i in range(3)]
        reason=None
        # These dimensions are the explicit 542 procedural constructors, not a
        # generic "small object above the shoulders" rule. Hair/jewelry islands
        # remain unless they are literally loose vertices with no rendered face.
        if not faces:reason='loose non-rendered vertices'
        elif npc=='merchant' and abs(low[2]-1.44)<.003 and abs(high[2]-1.56)<.004 and max(abs(low[0]),abs(high[0]))<.073 and low[1]>-.039 and high[1]<.089:
            reason='obsolete 542 close_merchant_neckline shell'
        elif npc=='priestess' and abs(low[2]-1.375)<.003 and abs(high[2]-1.542)<.005 and max(abs(low[0]),abs(high[0]))<.119 and low[1]>-.155 and high[1]<.098:
            reason='obsolete 542 continue_priestess_coif shell'
        if reason:
            remove.extend(component)
            if faces:removed_components.append({'reason':reason,'vertices':len(component),'faces':len(faces),'minimum':low,'maximum':high,'materials':sorted({body.data.materials[f.material_index].name for f in faces})})
    bmesh.ops.delete(bm,geom=remove,context='VERTS')
    # Skin fragments retained in the former colour-based head cut do not
    # belong to the hood/hair. Remove only the pale skin inside the new neck
    # footprint; preserve dark/purple hair and the actual cloth rim.
    mat=body.data.materials[0]
    images=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
    image=next((image for image in images if 'basecolor' in image.name.lower()),images[0])
    rgba=np.asarray(image.pixels[:]).reshape(image.size[1],image.size[0],4)
    uv_layer=bm.loops.layers.uv.active;discard=[]
    for face in bm.faces:
        x,y,z=face.calc_center_median()
        eligible=((1.44 if npc=='enchantress' else 1.49)<z<(1.62 if npc=='enchantress' else 1.73) and abs(x)<(.075 if npc=='enchantress' else .135) and y<(.035 if npc=='enchantress' else .075)) if npc!='merchant' else ((z>1.473 and abs(x)<.135 and -.105<y<.14) or (z>1.435 and abs(x)<.055 and -.115<y<.035))
        if not eligible:continue
        co=sum((loop[uv_layer].uv for loop in face.loops),Vector((0,0)))/len(face.loops)
        r,g,b=rgba[max(0,min(image.size[1]-1,int(co.y*image.size[1]))),max(0,min(image.size[0]-1,int(co.x*image.size[0]))),:3]
        skin=r>.32 and g>.25 and b>.20 and r>g*(1.12 if npc=='priestess' and z<1.49 else 1.02) and r<b*1.9
        linen=not(r>g*1.6 and b>g*1.12)
        if (npc!='merchant' and skin) or (npc=='merchant' and linen):discard.append(face)
    bmesh.ops.delete(bm,geom=discard,context='FACES')
    if npc=='merchant':
        # Replace the whole old inner linen opening, not individual dangling
        # fragments. The original bronze clasp projects in front of this cut.
        linen_faces=[]
        for face in bm.faces:
            x,y,z=face.calc_center_median()
            if not(abs(x)<.14 and -.080<y<.145 and z>1.410):continue
            co=sum((loop[uv_layer].uv for loop in face.loops),Vector((0,0)))/len(face.loops)
            r,g,b=rgba[max(0,min(image.size[1]-1,int(co.y*image.size[1]))),max(0,min(image.size[0]-1,int(co.x*image.size[0]))),:3]
            if .70*g<r<1.35*g and b>.68*g:linen_faces.append(face)
        region=linen_faces+list({e for f in linen_faces for e in f.edges})+list({v for f in linen_faces for v in f.verts})
        bmesh.ops.bisect_plane(bm,geom=region,dist=.000001,plane_co=(0,0,1.429),plane_no=(0,0,1),clear_outer=True,clear_inner=False)
    garment_vertices=garment_support(bm,body,npc,rgba,uv_layer)
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
    # The shirt/cape neckline is supported by the torso, not by the skull.
    deform=bm.verts.layers.deform.active
    chest=body.vertex_groups['Chest'].index
    if npc=='merchant':
        for vertex in bm.verts:
            if vertex.co.z>1.4 and abs(vertex.co.x)<.17:
                vertex[deform].clear();vertex[deform][chest]=1
    cloth_edges=repair_cloth_edges(bm,body,npc,rgba,uv_layer)
    added_band=shirt_band(bm,body,npc)
    inner_shirt=sew_inner_shirt(bm,body,neck,npc) if neck else 0
    faces=[face for face in bm.faces if face.calc_center_median().z>1.35]
    edge_count=sum(1 for e in bm.edges if e.is_boundary and all(v in cut for v in e.verts))
    # BMesh solidify uses an unbounded miter at almost coplanar reversed source
    # folds. Preserve connectivity/UV/deform interpolation, but explicitly place
    # each duplicate within the requested thickness of its source vertex.
    bm.normal_update()
    provenance=bm.verts.layers.int.new('NecklineSource')
    source={}
    original=set(bm.verts)
    for i,vertex in enumerate(bm.verts):
        vertex[provenance]=i+1
        source[i+1]=(vertex.co.copy(),vertex.normal.copy())
    bmesh.ops.solidify(bm,geom=faces,thickness=-.003)
    added=0
    for vertex in bm.verts:
        key=vertex[provenance]
        assert key in source, 'Inner garment vertex lost source provenance'
        co,normal=source[key]
        if vertex not in original:
            vertex.co=co-normal*.003
            added+=1
        assert (vertex.co-co).length<=.00301, 'Garment thickness escaped its 3 mm envelope'
    bm.verts.layers.int.remove(provenance)
    deform=bm.verts.layers.deform.active
    assert deform is not None
    for vertex in bm.verts:
        total=sum(vertex[deform].values())
        assert .999<total<1.001, ('Invalid garment skin weights',total,tuple(vertex.co))
    # Preserve the original garment winding; generated inner faces already have
    # the opposite winding from solidify. Global recalculation on overlapping
    # original costume shells can invert otherwise valid outer cloth.
    bm.normal_update()
    bm.to_mesh(body.data);bm.free();body.data.update()
    return {'innerShirtVertices':inner_shirt,'clothEdges':cloth_edges,'removedDetachedVertices':len(remove),'removedComponents':removed_components,'linedEdges':edge_count,'torsoGarmentVertices':garment_vertices,'shirtVertices':added_band,'innerVertices':added,'maximumThicknessMeters':.003}
