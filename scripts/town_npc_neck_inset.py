"""Construct the inside shirt opening from the actual anatomical neck surface.

The inner ring follows the skin's own interpolated weights; the outer seam is
supported by the chest. This replaces empty space inside a collar, without
extruding another visible neck or attaching the garment to the skull.
"""
import math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform


def sew_inner_shirt(bm, body, neck, npc):
    if npc != 'merchant':
        return 0
    neck.data.calc_loop_triangles()
    triangles=[tuple(t.vertices) for t in neck.data.loop_triangles]
    positions=[v.co.copy() for v in neck.data.vertices]
    tree=BVHTree.FromPolygons(positions,triangles,all_triangles=True)
    deform=bm.verts.layers.deform.active;uv=bm.loops.layers.uv.active
    chest=body.vertex_groups['Chest'].index
    skin_groups={group.index:group.name for group in neck.vertex_groups}
    rings=[];count=64
    for row in range(5):
        t=row/4;ring=[]
        for i in range(count):
            angle=2*math.pi*i/count
            direction=Vector((math.sin(angle),-math.cos(angle),0))
            z=1.462-.014*math.cos(angle)
            origin=Vector((0,.020,z))
            point,normal,triangle,distance=tree.ray_cast(origin,direction,.20)
            assert point is not None,('Anatomical neck ring is not closed',npc,i)
            ids=triangles[triangle]
            bary=barycentric_transform(point,*[positions[j] for j in ids],Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
            weights={}
            for vertex_id,amount in zip(ids,bary):
                for group in neck.data.vertices[vertex_id].groups:
                    name=skin_groups[group.group]
                    if name in ('Head','Neck','Chest'):
                        weights[name]=weights.get(name,0)+max(0,amount)*group.weight
            # The hidden skin-side seam overlaps by 0.8 mm. The outer seam
            # extends underneath the original shirt instead of stopping in air.
            inner=point-direction*.0008
            outer=Vector((.113*math.sin(angle),.023-.120*math.cos(angle),1.437-.008*math.cos(angle)))
            co=inner.lerp(outer,t);co.z-=.002*math.sin(math.pi*t)
            vertex=bm.verts.new(co)
            influence=(1-t)**2
            total=sum(weights.values());assert total>.99
            for name,weight in weights.items():vertex[deform][body.vertex_groups[name].index]=weight/total*influence
            vertex[deform][chest]=vertex[deform].get(chest,0)+1-influence
            ring.append(vertex)
        rings.append(ring)
    for row in range(4):
        for i in range(count):
            j=(i+1)%count
            face=bm.faces.new((rings[row][i],rings[row+1][i],rings[row+1][j],rings[row][j]));face.material_index=0;face.smooth=True
            for loop in face.loops:loop[uv].uv=(.567+loop.vert.co.x*.045,.6467+(loop.vert.co.y-.02)*.045)
    return count*5
