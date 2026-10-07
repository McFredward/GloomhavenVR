#!/usr/bin/env python3
"""Wrap read-only original BattleOverlayCanvas serialization in a test-only UnityFS bank.

No sprite reimport, atlas repack, pixel replacement, or geometry recreation is used.
Only the outer AssetBundle container is manufactured. The895 original Sprite
objects, original SpriteAtlas and original compressed Texture2D stay byte-identical.
The source texture's unchanged stream-offset resolves into the exact original
resS prefix. This generated bank is never packaged with the mod.
"""
import copy, hashlib, json, sys
from pathlib import Path
import UnityPy
from UnityPy.files import BundleFile, SerializedFile
from UnityPy.streams import EndianBinaryReader
from UnityPy.enums import ClassIDType
from UnityPy.files.ObjectReader import ObjectReader
base, out = map(Path, sys.argv[1:]); out.mkdir(parents=True, exist_ok=True)
env = UnityPy.load(str(base/'resources.assets')); native = next(iter(env.files.values()))
atlas = native.objects[10304]; tree = atlas.read_typetree()
assert tree['m_Name'] == 'BattleOverlayCanvas' and len(tree['m_PackedSprites']) ==895
sprites = {p['m_PathID'] for p in tree['m_PackedSprites']}
assert all(p['m_FileID']==0 for p in tree['m_PackedSprites'])
textures = {v['texture']['m_PathID'] for _,v in tree['m_RenderDataMap']}
assert textures == {206}
selected = {**{i:native.objects[i] for i in sprites|textures}, atlas.path_id:atlas}
receipt = {'source':'resources.assets','atlas_path_id':10304,'sprites':len(sprites),
    'members':[], 'source_sha256':hashlib.sha256((base/'resources.assets').read_bytes()).hexdigest(),
    'objects_sha256':{str(i):hashlib.sha256(o.get_raw_data()).hexdigest()for i,o in selected.items()}}
for i in sorted(sprites):
    s=selected[i].read_typetree()
    receipt['members'].append({'path_id':i,'name':s['m_Name'],'rect':s['m_Rect'],
       'pivot':s['m_Pivot'],'border':s['m_Border'],'ppu':s['m_PixelsToUnits'],
       'render_data_key':s['m_RenderDataKey']})
# Use a shipped UnityFS header and actual AssetBundle type grammar; only the
# generated test-container m_Container/preload metadata is different.
wrapper_path=base/'StreamingAssets/aa/StandaloneWindows64/itemconfigs_backgrounds_assets_poisondagger.bundle'
wenv=UnityPy.load(str(wrapper_path)); wrapper=next(iter(wenv.files.values()))
wfile=next(f for f in wrapper.files.values()if isinstance(f,SerializedFile))
container=next(o for o in wfile.objects.values()if o.type.name=='AssetBundle')
ct=container.read_typetree(); ct['m_Name']='npc639-original-battle-atlas'
ct['m_AssetBundleName']=ct['m_Name'];ct['m_Dependencies']=[];ct['m_ExplicitDataLayout']=0
ct['m_PreloadTable']=[{'m_FileID':0,'m_PathID':i}for i in (10304,206)]
ct['m_Container']=[('native/battleoverlaycanvas.spriteatlas',{'preloadIndex':0,
 'preloadSize':2,'asset':{'m_FileID':0,'m_PathID':10304}})]
ct['m_Container'] += [('native/texture',{'preloadIndex':0,'preloadSize':2,'asset':{'m_FileID':0,'m_PathID':206}})]
ct['m_Container'] += [('native/sprite/'+str(i),{'preloadIndex':0,'preloadSize':2,'asset':{'m_FileID':0,'m_PathID':i}})for i in sorted(sprites)]
ct['m_MainAsset']={'preloadIndex':0,'preloadSize':0,'asset':{'m_FileID':0,'m_PathID':0}}
container.save_typetree(ct)
asset_type=copy.copy(container.serialized_type);native.types.append(asset_type)
wrapped=ObjectReader(native,container.reader,1,len(native.types)-1,asset_type,
   container.class_id,ClassIDType.AssetBundle,0,len(container.data),None,None,
   container.data)
native.objects={**selected,1:wrapped};native.script_types=[];native.externals=[]
# Unused game MonoBehaviour type entries reference its scripts even without
# objects. Remove only unused serialization types from this isolated test bank.
used_types=sorted({o.type_id for o in native.objects.values()})
remap={old:new for new,old in enumerate(used_types)}
native.types=[native.types[i]for i in used_types]
for o in native.objects.values():
    o.type_id=remap[o.type_id]
    if native.types[o.type_id].node is None:
        native.types[o.type_id].node=o._get_typetree_node(None)
for typ in native.types:
    if typ.type_dependencies is None:typ.type_dependencies=[]
    for node in typ.node.traverse():
        if node.m_TypeFlags is None:node.m_TypeFlags=1 if node.m_Type=='Array' else 0
        if node.m_RefTypeHash is None:node.m_RefTypeHash=0
native._enable_type_tree=True
native.name='CAB-'+receipt['source_sha256'][:32];native.flags=wfile.flags
stream=selected[206].read().m_StreamData
stream_path=base/stream.path
with stream_path.open('rb')as f: stream_bytes=f.read(stream.offset+stream.size)
receipt['stream_sha256']=hashlib.sha256(stream_bytes).hexdigest()
# The game's unchanged resource Texture2D retains a plain external stream path.
# Unity's async GPU upload resolves it at the editor project root, not inside
# the new UnityFS archive. Preserve that exact prefix as a read-only companion.
assert stream.path=='resources.assets.resS'
(out/stream.path).write_bytes(stream_bytes)
receipt['runtime_stream_companion']=stream.path
receipt['runtime_stream_companion_bytes']=len(stream_bytes)
receipt['runtime_stream_requirement']='Copy/link this exact companion to the Unity project root for actual GPU rendering.'
stream_reader=EndianBinaryReader(stream_bytes);stream_reader.flags=0
wrapper.files={native.name:native,stream.path:stream_reader}
# Raw original object data must survive our new outer container unchanged.
for i,expected in receipt['objects_sha256'].items():assert hashlib.sha256(native.objects[int(i)].get_raw_data()).hexdigest()==expected
payload=wrapper.save('lz4');path=out/'original-battle-atlas.bundle';path.write_bytes(payload)
check=UnityPy.load(str(path)); objects={o.path_id:o for o in check.objects}
for i,expected in receipt['objects_sha256'].items():assert hashlib.sha256(objects[int(i)].get_raw_data()).hexdigest()==expected
receipt['generated_bundle_sha256']=hashlib.sha256(payload).hexdigest();receipt['generated_bundle_bytes']=len(payload)
(out/'provenance.json').write_text(json.dumps(receipt,indent=2)+'\n')
print('Original895-sprite native atlas, all object bytes unchanged: '+str(path),flush=True)
