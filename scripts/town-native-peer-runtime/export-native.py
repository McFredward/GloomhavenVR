#!/usr/bin/env python3
"""Read original UIInfoTools sprite slots and FX textures without changing game assets."""
import hashlib, json, sys, struct
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
base, out = map(Path, sys.argv[1:]); out.mkdir(parents=True, exist_ok=True)
generator=TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(base/'Managed'))
loaded={}
def env(name):
    if name not in loaded:
        loaded[name]=UnityPy.load(str(base/name)); loaded[name].typetree_generator=generator
    return loaded[name]
def obj(name, path): return next(x for x in env(name).objects if x.path_id==path)
source=obj('level1',11386); tree=source.read_typetree()
assert struct.unpack_from('<iq',source.get_raw_data(),16)==(1,845) and tree['Poisoned']['Name']=='Poisoned'
def sprite(slot, pointer):
    name=Path(source.assets_file.externals[pointer['m_FileID']-1].path).name
    native=obj(name,pointer['m_PathID']); native.read().image.save(out/(slot+'.png'))
    return {'slot':slot,'file':name,'path':native.path_id,'sha256':hashlib.sha256(native.get_raw_data()).hexdigest()}
slots=[sprite('Poisoned',tree['Poisoned']['Icon']),sprite('Wounded',tree['Wounded']['Icon']),sprite('Target',tree['Target'])]
texture=None
for name in ['resources.assets']+[p.name for p in sorted(base.glob('sharedassets*.assets'))]:
    for native in env(name).objects:
        if native.type.name=='Texture2D':
            data=native.read()
            if data.m_Name=='T_rect_frame_mask_card_wide':
                data.image.save(out/'effect-mask.png'); texture={'file':name,'path':native.path_id,'sha256':hashlib.sha256(native.get_raw_data()).hexdigest()};break
    if texture:break
assert texture, 'Original effect mask not found'
(out/'provenance.json').write_text(json.dumps({'unitypy':UnityPy.__version__,'uiInfoTools':['level1',11386],'sprites':slots,'effect-mask':texture,
 'source_sha256':{k:hashlib.sha256((base/k).read_bytes()).hexdigest() for k in loaded}},indent=2)+'\n')
print('Exported original native icons and effect mask: '+str(out))
