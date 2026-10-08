#!/usr/bin/env python3
"""Prepare private original environment streams and same-index 3D detail tiers.

Native bundles are read-only. Vertex clustering fixes every original open boundary,
retains all native attribute slots/indices and removes collapsed triangles. It runs
only offline, never in the mod or on a game source object.
"""
import argparse, collections, gzip, hashlib, json, math, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools/environment-mesh'))
from geometry import parse, simplify, footprint
from roles import architectural_ornament

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT);p.add_argument('--output-dir',type=Path);p.add_argument('--skip-extract',action='store_true');p.add_argument('--only',nargs='*');a=p.parse_args()
    root=a.source_root.resolve();work=(a.output_dir or root/'.planning/debug/environment-mesh-generation').resolve();native=work/'native';native.mkdir(parents=True,exist_ok=True)
    if not a.skip_extract:
        cmd=[str(Path.home()/'unitypy-venv/bin/python'),str(root/'tools/environment-mesh/export-native.py'),'--source-root',str(root),'--output-dir',str(native)]
        if a.only:cmd+=['--only']+a.only
        subprocess.run(cmd,check=True)
    sources=json.loads((native/'sources.json').read_text())
    if sources.get('format')!=1 or not sources.get('meshes'):
        raise SystemExit('Native environment receipt has no admissible originals; existing prepared assets remain unchanged.')
    target=work/'prepared' if a.only else root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes';target.mkdir(parents=True,exist_ok=True)
    entries=[];receipts=[];current=set(); ornaments=[]; role_counts=collections.Counter()
    for source in sources['meshes']:
        data=(native/source['file']).read_bytes();assert hashlib.sha256(data).hexdigest()==source['sha256']
        role=source.get('role', 'none')
        ornament=architectural_ornament(source['signature']['name'],source.get('uses',[]))
        # Source roles are about retained tile architecture, including vertical
        # UnderFloor side skirts. A zero horizontal footprint is valid for such
        # parts; their exact mesh remains available for floor batching, while the
        # stricter floor derivative certificate declines horizontal simplification.
        floor_facts={}
        if role=='floor':
            vertices,subs,_,_=parse(data)
            heights,_=footprint(vertices,subs)
            floor_facts={'footprintGrid':18,'sourceFootprintSamples':sum(math.isfinite(height[1]) for height in heights)}
        source['floor']=floor_facts
        role_counts[role]+=1
        evidence={'role':role,'reasons':source.get('roleReasons',[]),'uses':source.get('uses',[]),'floor':floor_facts}
        evidence_sha=hashlib.sha256(json.dumps(evidence,sort_keys=True,separators=(',',':')).encode()).hexdigest()
        variants=[]
        for tier in (100,50,0):
            generated=data;receipt=None
            if tier!=100:
                generated,receipt=simplify(data,tier,role)
                if generated is None:continue
            filename=source['key']+'-'+str(tier)+'.bytes';(target/filename).write_bytes(generated);current.add(filename)
            variants.append({'tier':tier,'file':filename,'sha256':hashlib.sha256(generated).hexdigest()})
            if receipt:receipts.append(dict(key=source['key'],name=source['signature']['name'],**receipt))
        entries.append({'key':source['key'],'signature':source['signature'],'sources':source['sources'],
            'role':role,'ornament':ornament,'roleEvidenceSha256':evidence_sha,'variants':variants})
        if ornament:ornaments.append({'key':source['key'],'signature':source['signature'],
            'sources':source['sources'],'uses':source.get('uses',[]),'collisionPolicy':'retained-core-or-explicit-native-tile-wall-required'})
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
    catalog={'format':1,'completePcgCensus':sources.get('completePcgCensus',False),
        'bundles':sources.get('bundles',[]),'meshes':sources.get('catalog',[]),
        'certified':[{**entry,'roleReasons':source.get('roleReasons',[]),'uses':source.get('uses',[]),'floor':source.get('floor',{})}
            for entry,source in zip(entries,sources['meshes'])]}
    report={'format':1,'source':'read-only pcg database MeshFilter originals','unitypy':sources['unitypy'],'originals':len(entries),'variants':sum(len(e['variants']) for e in entries),'ambiguousRejected':sources['ambiguousRejected'],'roles':dict(role_counts),'ornaments':len(ornaments),'completePcgCensus':sources.get('completePcgCensus',False),'sourceBundles':len(sources.get('bundles',[])),'detailReceipts':receipts,'assetBytes':sum((target/f).stat().st_size for f in current)}
    (work/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
    if not a.only:
        (root/'tools/environment-mesh/manifest.json').write_text(json.dumps(report,indent=2)+'\n')
        catalog_bytes=(json.dumps(catalog,separators=(',',':'))+'\n').encode()
        (root/'tools/environment-mesh/catalog.json.gz').write_bytes(gzip.compress(catalog_bytes,mtime=0))
        (root/'tools/environment-mesh/catalog.json').unlink(missing_ok=True)
        (root/'tools/environment-mesh/ornaments.json').write_text(json.dumps({'format':1,'entries':ornaments},separators=(',',':'))+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('detailReceipts','ambiguousRejected')},indent=2))
if __name__=='__main__':main()
