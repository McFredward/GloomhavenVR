#!/usr/bin/env python3
"""Prepare private original environment streams and same-index 3D detail tiers.

Native bundles are read-only. Vertex clustering fixes every original open boundary,
retains all native attribute slots/indices and removes collapsed triangles. It runs
only offline, never in the mod or on a game source object.
"""
import argparse, collections, hashlib, json, math, struct, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

def parse(data):
    off=5; length=struct.unpack_from('<i',data,off)[0];off+=4+length+24
    count,width=struct.unpack_from('<ii',data,off);off+=8;position=off
    vertices=[struct.unpack_from('<fff',data,off+12*i) for i in range(count)];off+=count*width*4
    for _ in range(11):
        n,w=struct.unpack_from('<ii',data,off);off+=8+n*w*4
    prefix=off;subs=struct.unpack_from('<i',data,off)[0];off+=4;triangles=[]
    for _ in range(subs):
        n=struct.unpack_from('<i',data,off)[0];off+=4
        indices=struct.unpack_from('<'+'i'*n,data,off);off+=4*n;triangles.append(list(zip(indices[::3],indices[1::3],indices[2::3])))
    assert off==len(data)
    return vertices,triangles,position,prefix

def simplify(data,tier):
    vertices,subs,position,prefix=parse(data)
    # Canonical positions join native UV/normal-split indices for boundary detection;
    # those actual split indices and their original channels still remain in the stream.
    edges=collections.Counter()
    for triangles in subs:
        for a,b,c in triangles:
            for i,j in ((a,b),(b,c),(c,a)):
                va,vb=vertices[i],vertices[j]
                if va!=vb:edges[tuple(sorted((va,vb)))]+=1
    boundary={v for edge,count in edges.items() if count==1 for v in edge}
    lo=[min(v[i] for v in vertices) for i in range(3)];hi=[max(v[i] for v in vertices) for i in range(3)]
    divisions=12 if tier==50 else 4
    groups=collections.defaultdict(set)
    def key(v):return tuple(min(divisions-1,max(0,int((v[i]-lo[i])/(hi[i]-lo[i])*divisions))) if hi[i]>lo[i] else 0 for i in range(3))
    for v in vertices:
        if v not in boundary:groups[key(v)].add(v)
    means={k:tuple(sum(v[i] for v in group)/len(group) for i in range(3)) for k,group in groups.items()}
    moved=[v if v in boundary else means[key(v)] for v in vertices]
    out=bytearray(data[:prefix]);
    for i,v in enumerate(moved):struct.pack_into('<fff',out,position+12*i,*v)
    out.extend(struct.pack('<i',len(subs)));after=0;before=0
    for triangles in subs:
        flat=[];before+=len(triangles)
        for a,b,c in triangles:
            va,vb,vc=moved[a],moved[b],moved[c]
            ab=[vb[i]-va[i] for i in range(3)];ac=[vc[i]-va[i] for i in range(3)]
            cross=(ab[1]*ac[2]-ab[2]*ac[1],ab[2]*ac[0]-ab[0]*ac[2],ab[0]*ac[1]-ab[1]*ac[0])
            if sum(x*x for x in cross)>1e-18:flat.extend((a,b,c))
        after+=len(flat)//3;out.extend(struct.pack('<i',len(flat)));out.extend(struct.pack('<'+'i'*len(flat),*flat))
    if after==0 or after>=before*.98:return None,None
    return bytes(out),{'tier':tier,'sourceTriangles':before,'triangles':after,'sourceVertices':len(vertices),'vertices':len(moved),'fixedBoundaryPositions':len(boundary),'sameIndexMorph':True}

def _publish(path,data):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    temporary=path.with_name(path.name+'.generating')
    temporary.write_bytes(data);temporary.replace(path)

def prepare_mesh(source,native_path,target,*,receipt_dir=None,producer_key=None):
    """Preserve the desktop algorithm while checkpointing each accepted tier."""
    target=Path(target);target.mkdir(parents=True,exist_ok=True)
    native_path=Path(native_path);data=native_path.read_bytes()
    if hashlib.sha256(data).hexdigest()!=source['sha256'] or data[:5]!=b'GHEM1':
        raise ValueError('Native environment stream differs from its source receipt: '+source['key'])
    variants=[];details=[]
    for tier in (100,50,0):
        filename=source['key']+'-'+str(tier)+'.bytes';path=target/filename
        identity={'format':1,'sourceSha256':source['sha256'],'producerKey':producer_key,'tier':tier}
        receipt=Path(receipt_dir)/(source['key']+'-'+str(tier)+'.json') if receipt_dir else None
        prior=json.loads(receipt.read_text()) if receipt and receipt.is_file() else None
        if prior is not None and prior.get('identity')!=identity:
            raise ValueError('Environment derivative ownership changed: '+filename)
        if prior is not None:
            variant=prior['variant'];detail=prior.get('detail')
            if variant is not None:
                if path.is_symlink() or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=variant['sha256']:
                    raise ValueError('Completed environment derivative changed: '+filename)
        else:
            generated=data;detail=None
            if tier!=100:generated,detail=simplify(data,tier)
            variant=None
            if generated is not None:
                _publish(path,generated)
                variant={'tier':tier,'file':filename,'sha256':hashlib.sha256(generated).hexdigest()}
            if receipt:
                _publish(receipt,(json.dumps({'identity':identity,'variant':variant,'detail':detail},sort_keys=True)+'\n').encode())
        if variant:variants.append(variant)
        if detail:details.append(dict(key=source['key'],name=source['signature']['name'],**detail))
    return {'key':source['key'],'signature':source['signature'],'sources':source['sources'],'variants':variants},details

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT);p.add_argument('--game-data',type=Path);p.add_argument('--prepared-dir',type=Path);p.add_argument('--output-dir',type=Path);p.add_argument('--skip-extract',action='store_true');p.add_argument('--only',nargs='*');a=p.parse_args()
    root=a.source_root.resolve();work=(a.output_dir or root/'.planning/debug/environment-mesh-generation').resolve();native=work/'native'
    if a.game_data and not a.prepared_dir:
        raise SystemExit('Owned-game conversion requires --prepared-dir outside the source checkout.')
    target=a.prepared_dir.resolve() if a.prepared_dir else work/'prepared' if a.only else root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    if a.game_data and any(base==path or base in path.parents for base in (root,a.game_data.resolve()) for path in (target,work)):
        raise SystemExit('Owned-game preparation must not write the source checkout or PC game.')
    native.mkdir(parents=True,exist_ok=True)
    if not a.skip_extract:
        python=sys.executable if a.game_data else str(Path.home()/'unitypy-venv/bin/python')
        cmd=[python,str(root/'tools/environment-mesh/export-native.py'),'--source-root',str(root),'--output-dir',str(native)]
        if a.game_data:cmd+=['--game-data',str(a.game_data)]
        if a.only:cmd+=['--only']+a.only
        subprocess.run(cmd,check=True)
    sources=json.loads((native/'sources.json').read_text())
    if sources.get('format')!=1 or not sources.get('meshes'):
        raise SystemExit('Native environment receipt has no admissible originals; existing prepared assets remain unchanged.')
    target.mkdir(parents=True,exist_ok=True)
    entries=[];receipts=[];current=set()
    for source in sources['meshes']:
        entry,details=prepare_mesh(source,native/source['file'],target)
        entries.append(entry);receipts.extend(details);current.update(v['file'] for v in entry['variants'])
    for old in target.glob('*.bytes'):
        if old.name not in current:old.unlink()
    (target/'index.json').write_text(json.dumps({'format':1,'entries':entries},separators=(',',':'))+'\n')
    (target/'.gitattributes').write_text('*.bytes binary\n*.meta whitespace=-blank-at-eol\n')
    for asset in list(target.glob('*.bytes'))+[target/'index.json']:
        meta=asset.with_name(asset.name+'.meta')
        guid=hashlib.md5(('GloomhavenVR.EnvironmentMeshes/'+asset.name).encode()).hexdigest()
        meta.write_text('fileFormatVersion: 2\nguid: '+guid+'\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    for meta in target.glob('*.meta'):
        if not meta.with_name(meta.name[:-5]).is_file():meta.unlink()
    report={'format':1,'source':'read-only pcg database MeshFilter originals','unitypy':sources['unitypy'],'originals':len(entries),'variants':sum(len(e['variants']) for e in entries),'ambiguousRejected':sources['ambiguousRejected'],'detailReceipts':receipts,'assetBytes':sum((target/f).stat().st_size for f in current)}
    (work/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
    if not a.only and not a.prepared_dir:(root/'tools/environment-mesh/manifest.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('detailReceipts','ambiguousRejected')},indent=2))
if __name__=='__main__':main()
