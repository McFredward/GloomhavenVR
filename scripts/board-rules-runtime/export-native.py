#!/usr/bin/env python3
"""Recover exact original native rules widgets/font from read-only GH_Data, no prior export needed."""
import hashlib
import json
from pathlib import Path
import sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.classes import PPtr

base, out = map(Path, sys.argv[1:3]); out.mkdir(parents=True, exist_ok=True)
generator = TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(base / 'Managed'))
loaded = {}
def env(name):
    if name not in loaded:
        loaded[name] = UnityPy.load(str(base / name)); loaded[name].typetree_generator = generator
    return loaded[name]
def obj(name, path): return next(o for o in env(name).objects if o.path_id == path)
def tree(name, path): return obj(name, path).read_typetree()
container = tree('level6', 110); rect = tree('level6', 3251); layout = tree('level6', 5416)
reference = tree('level6', 5415)['ScenarioModifierPrefab']
assert reference == {'m_FileID': 2, 'm_PathID': 491}, 'Native source binding changed; refusing fabricated fixture'
row = tree('sharedassets6.assets', 491); row_rect = tree('sharedassets6.assets', 5067)
text = tree('sharedassets6.assets', 9216)
assert text['m_fontAsset'] == {'m_FileID': 4, 'm_PathID': 10356}
font = tree('resources.assets', 10356); material = tree('resources.assets', 141)
assert font['m_AtlasTextures'] == [{'m_FileID': 0, 'm_PathID': 440}]
atlas = obj('resources.assets', 440).read().image
atlas.save(out / 'atlas.png')
button = obj('resources.assets', 4136).read()
button.image.save(out / 'button.png')
props = material['m_SavedProperties']
(out/'material-properties.json').write_text(json.dumps({'floats':[{'name':n,'value':v} for n,v in props['m_Floats']],
    'colors':[{'name':n,'value':v} for n,v in props['m_Colors']], 'keywords':material['m_ValidKeywords']}))
(out/'button-metrics.json').write_text(json.dumps({'border':tree('resources.assets',4136)['m_Border']}))
# Unity's JSON uses instance IDs instead of serialized-file pointers. Recover all native values
# and explicitly rebind only the same native font/atlas/material references in the editor importer.
def pointers(value):
    if isinstance(value, dict):
        if set(value) == {'m_FileID', 'm_PathID'}: return {'instanceID': 0}
        return {key: pointers(val) for key, val in value.items() if key not in ('m_GameObject', 'm_Script')}
    if isinstance(value, list): return [pointers(val) for val in value]
    return value
for name, data in [('container', container), ('rect', rect), ('layout', layout), ('row', row), ('row-rect', row_rect), ('text', text), ('font', font), ('material', material)]:
    (out / (name + '.json')).write_text(json.dumps(pointers(data), ensure_ascii=False, indent=2))
provenance = {'format': 'native-rules-fixture1', 'unitypy': UnityPy.__version__, 'paths': {
    'container': ['level6',110,3251,5415,5416], 'row': ['sharedassets6.assets',491,5067,8661,2917,9216],
    'font': ['resources.assets',10356,141,440]}, 'source_sha256': {name: hashlib.sha256((base/name).read_bytes()).hexdigest() for name in loaded},
    'raw_components_sha256': {f'{name}:{path}': hashlib.sha256(obj(name,path).get_raw_data()).hexdigest() for name,path in [('level6',5416),('sharedassets6.assets',9216),('resources.assets',10356)]},
    'atlas_rgba_sha256': hashlib.sha256(atlas.tobytes()).hexdigest(),
    'glyphs': len(font['m_GlyphTable']), 'characters':len(font['m_CharacterTable'])}
(out/'provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
print('Recovered original native rules UI: '+str(out))
