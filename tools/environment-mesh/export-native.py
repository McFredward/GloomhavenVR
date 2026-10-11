#!/usr/bin/env python3
"""Extract immutable static environment originals; game bundles are read-only."""
import argparse, hashlib, json, re, struct
from pathlib import Path
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

def signature(mesh):
    b=mesh.m_LocalAABB
    return {'name':mesh.m_Name,'readable':mesh.m_IsReadable,'vertices':mesh.m_VertexData.m_VertexCount,
        'indices':[s.indexCount for s in mesh.m_SubMeshes],
        'bounds':[b.m_Center.x,b.m_Center.y,b.m_Center.z,b.m_Extent.x,b.m_Extent.y,b.m_Extent.z]}

def write(mesh,path):
    h=MeshHandler(mesh); h.process()
    with path.open('wb') as out:
        def ints(*v): out.write(struct.pack('<'+'i'*len(v),*v))
        def floats(v): out.write(struct.pack('<'+'f'*len(v),*v))
        def channel(values,width):
            ints(len(values or []),width)
            for row in values or []: floats(tuple(row)[:width])
        out.write(b'GHEM1'); encoded=mesh.m_Name.encode(); ints(len(encoded));out.write(encoded)
        floats(signature(mesh)['bounds']);channel(h.m_Vertices,3);channel(h.m_Normals,3)
        channel(h.m_Tangents,4);channel(h.m_Colors,4)
        for i in range(8):
            values=getattr(h,'m_UV'+str(i));channel(values,len(values[0]) if values else 2)
        triangles=h.get_triangles();ints(len(triangles))
        for sub in triangles:
            flat=[i for t in sub for i in t];ints(len(flat));ints(*flat)
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    p=argparse.ArgumentParser();p.add_argument('--source-root',type=Path,required=True);p.add_argument('--output-dir',type=Path,required=True);p.add_argument('--only',nargs='*');a=p.parse_args()
    root=a.source_root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'
    source_folder=root/'pcg_databases_assets_assets/pcg'
    if not source_folder.is_dir():
        raise SystemExit('Native environment source folder is missing: '+str(source_folder))
    bundles=[path for path in sorted(source_folder.glob('*.bundle')) if not a.only or path.stem in a.only]
    if not bundles:
        raise SystemExit('No native environment bundles match the requested source selection.')
    a.output_dir.mkdir(parents=True,exist_ok=True)
    records={};ambiguous=set(); skipped=[]
    for path in bundles:
        env=UnityPy.load(str(path));bundle_sha=hashlib.sha256(path.read_bytes()).hexdigest()
        static={o.read().m_Mesh.path_id for o in env.objects if o.type.name=='MeshFilter'}
        for o in env.objects:
            if o.type.name!='Mesh' or o.path_id not in static:continue
            m=o.read(); name=m.m_Name
            if not re.search(r'floor|wall|pillar|rock|terrain|cliff|slab|slope|stairs|underfloor',name,re.I):continue
            if m.m_VertexData.m_VertexCount<3 or m.m_VertexData.m_VertexCount>100000 or m.m_BindPose or m.m_Shapes.channels or any(s.topology!=0 for s in m.m_SubMeshes):continue
            sig=signature(m);identity=json.dumps(sig,sort_keys=True,separators=(',',':'));key=hashlib.sha256(identity.encode()).hexdigest()[:24]
            raw=a.output_dir/(key+'.bytes');sha=write(m,raw)
            provenance={'path':path.relative_to(root).as_posix(),'sha256':bundle_sha,'meshPathId':o.path_id}
            if key in records:
                if records[key]['sha256']!=sha:ambiguous.add(key);continue
                records[key]['sources'].append(provenance);continue
            records[key]={'key':key,'file':raw.name,'sha256':sha,'signature':sig,'sources':[provenance]}
        print('Scanned '+path.name,flush=True)
    for key in ambiguous:
        records.pop(key,None);(a.output_dir/(key+'.bytes')).unlink(missing_ok=True)
    if not records:
        raise SystemExit('Native environment extraction produced no admissible originals; existing prepared assets must remain unchanged.')
    result={'format':1,'unitypy':UnityPy.__version__,'meshes':list(records.values()),'ambiguousRejected':sorted(ambiguous)}
    (a.output_dir/'sources.json').write_text(json.dumps(result,indent=2)+'\n')
    print(f'Exact static originals: {len(records)}, ambiguous identities rejected: {len(ambiguous)}')
if __name__=='__main__':main()
