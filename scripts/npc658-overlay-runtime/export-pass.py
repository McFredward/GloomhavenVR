#!/usr/bin/env python3
"""Read exact native FlexFrame draw states; game references stay read-only."""
import hashlib
import json
from pathlib import Path
import sys
import UnityPy
root, output = map(Path, sys.argv[1:])
shader_file = root / 'ressources/GH_Data/sharedassets1.assets'
material_file = root / 'ressources/GH_Data/sharedassets4.assets'
shader_object = next(obj for obj in UnityPy.load(str(shader_file)).objects if obj.path_id == 874)
material_object = next(obj for obj in UnityPy.load(str(material_file)).objects if obj.path_id == 18)
shader = shader_object.read_typetree()
material = material_object.read_typetree()
assert shader['m_ParsedForm']['m_Name'] == 'GUI/GUI_FlexFrame_Shd'
assert material['m_Name'] == 'GUI_New_Enhancement_Frame_Highlight'
assert material['m_Shader']['m_PathID'] == 874
subshader = shader['m_ParsedForm']['m_SubShaders'][0]
state = subshader['m_Passes'][0]['m_State']
flags = {name: state[name]['val'] for name in ('zTest', 'zWrite', 'culling', 'offsetFactor', 'offsetUnits')}
flags.update(srcBlend=state['rtBlend0']['srcBlend']['val'], destBlend=state['rtBlend0']['destBlend']['val'])
assert flags == {'zTest': 4.0, 'zWrite': 0.0, 'culling': 0.0, 'offsetFactor': 0.0, 'offsetUnits': 0.0, 'srcBlend': 5.0, 'destBlend': 1.0}, flags
assert dict(subshader['m_Tags']['tags'])['QUEUE'] == 'Transparent'
assert subshader['m_Passes'][0]['m_Platforms'] == [4]
output.write_text(json.dumps({'shader': [shader_file.name, 874], 'material': [material_file.name, 18],
    'draw_states': flags, 'platforms': shader['platforms'], 'material_properties': material['m_SavedProperties'],
    'sha256': {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in (shader_file, material_file)},
    'boundary': 'Native serialized hierarchy/sprites/properties/vertex colors and exact native D3D11 pass depth/blend flags. The GL fragment adapter only draws an edge to observe occlusion; it does not reproduce original angular/noise pixels or certify headset rendering.'}, indent=2) + '\n')
