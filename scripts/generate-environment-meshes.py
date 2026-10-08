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
    role=source.get('role','none')
    ornament=architectural_ornament(source['signature']['name'],source.get('uses',[]))
    # Vertical retained floor skirts can have an empty horizontal footprint; only
    # the stricter reduced-floor certificate refuses such simplification.
    floor_facts={}
    if role=='floor':
        vertices,subs,_,_=parse(data);heights,_=footprint(vertices,subs)
        floor_facts={'footprintGrid':18,'sourceFootprintSamples':sum(math.isfinite(height[1]) for height in heights)}
    source['floor']=floor_facts
    evidence={'role':role,'reasons':source.get('roleReasons',[]),'uses':source.get('uses',[]),'floor':floor_facts}
    evidence_sha=hashlib.sha256(json.dumps(evidence,sort_keys=True,separators=(',',':')).encode()).hexdigest()
    variants=[];details=[]
    for tier in (100,50,0):
        filename=source['key']+'-'+str(tier)+'.bytes';path=target/filename
        identity={'format':1,'sourceSha256':source['sha256'],'producerKey':producer_key,'roleEvidenceSha256':evidence_sha,'tier':tier}
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
            if tier!=100:generated,detail=simplify(data,tier,role)
            variant=None
            if generated is not None:
                _publish(path,generated)
                variant={'tier':tier,'file':filename,'sha256':hashlib.sha256(generated).hexdigest()}
            if receipt:
                _publish(receipt,(json.dumps({'identity':identity,'variant':variant,'detail':detail},sort_keys=True)+'\n').encode())
        if variant:variants.append(variant)
        if detail:details.append(dict(key=source['key'],name=source['signature']['name'],**detail))
    return {'key':source['key'],'signature':source['signature'],'sources':source['sources'],'role':role,'ornament':ornament,'roleEvidenceSha256':evidence_sha,'variants':variants},details

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
    entries=[];receipts=[];current=set();ornaments=[];role_counts=collections.Counter()
    for source in sources['meshes']:
        entry,details=prepare_mesh(source,native/source['file'],target)
        entries.append(entry);receipts.extend(details);current.update(v['file'] for v in entry['variants'])
        role_counts[entry['role']]+=1
        if entry['ornament']:ornaments.append({'key':source['key'],'signature':source['signature'],
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
    if not a.only and not a.prepared_dir:
        (root/'tools/environment-mesh/manifest.json').write_text(json.dumps(report,indent=2)+'\n')
        catalog_bytes=(json.dumps(catalog,separators=(',',':'))+'\n').encode()
        (root/'tools/environment-mesh/catalog.json.gz').write_bytes(gzip.compress(catalog_bytes,mtime=0))
        (root/'tools/environment-mesh/catalog.json').unlink(missing_ok=True)
        (root/'tools/environment-mesh/ornaments.json').write_text(json.dumps({'format':1,'entries':ornaments},separators=(',',':'))+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('detailReceipts','ambiguousRejected')},indent=2))
if __name__=='__main__':main()
