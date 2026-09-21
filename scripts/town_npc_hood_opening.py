"""Cut one continuous inner hood aperture and retain interpolated costume UVs."""
import bmesh
from mathutils import Vector


def repair_hood(bm, npc):
    if npc not in ('priestess','enchantress'):
        return 0
    uv=bm.loops.layers.uv.active;deform=bm.verts.layers.deform.active
    rx,rz,cz=(.118,.160,1.555) if npc=='priestess' else (.124,.160,1.555)
    # The opening only reaches the front/side inner hood. Its intact rear fabric
    # stays in place, so this is not a hole through both layers of the costume.
    def signed(p):return max((p.x/rx)**2+((p.z-cz)/rz)**2-1,(p.y-.115)*25,(1.425-p.z)*25)
    removed=[];shared={};boundary=[];changed=0
    for face in list(bm.faces):
        if max(v.co.z for v in face.verts)<1.425:continue
        loops=list(face.loops);values=[signed(l.vert.co) for l in loops]
        if min(values)>=0:continue
        if max(values)<0:removed.append(face);continue
        poly=[]
        for i,loop in enumerate(loops):
            nxt=loops[(i+1)%len(loops)];a,b=loop.vert,nxt.vert;sa,sb=values[i],values[(i+1)%len(loops)]
            if sa>=0:poly.append((a,loop[uv].uv.copy()))
            if (sa<0)==(sb<0):continue
            lo,hi=0.,1.
            for _ in range(24):
                t=(lo+hi)*.5
                if (signed(a.co.lerp(b.co,t))<0)==(sa<0):lo=t
                else:hi=t
            t=(lo+hi)*.5;key=frozenset((a,b));v=shared.get(key)
            if v is None:
                v=bm.verts.new(a.co.lerp(b.co,t));shared[key]=v;boundary.append(v)
                for group in set(a[deform].keys())|set(b[deform].keys()):v[deform][group]=a[deform].get(group,0)*(1-t)+b[deform].get(group,0)*t
            poly.append((v,loop[uv].uv.lerp(nxt[uv].uv,t)))
        if len(poly)>=3:
            new=bm.faces.new([v for v,_ in poly]);new.material_index=face.material_index;new.smooth=True
            for loop,(_,coord) in zip(new.loops,poly):loop[uv].uv=coord
            changed+=1
        removed.append(face)
    bmesh.ops.delete(bm,geom=removed,context='FACES_ONLY')
    bmesh.ops.remove_doubles(bm,verts=boundary,dist=.00008)
    return {'removedInteriorFaces':len(removed),'clippedBoundaryFaces':changed}
