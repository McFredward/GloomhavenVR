#!/usr/bin/env python3
"""Reuse the read-only native row exporter for the original mage points heading."""
import hashlib
import json
import sys
import struct
from pathlib import Path

root, out = map(Path, sys.argv[1:])
exporter = root / 'scripts/town-first-picture632-runtime/export-native.py'
source = exporter.read_text()
anchor = "visit(obj('sharedassets4.assets',226),-1)"
if source.count(anchor) != 1:
    raise SystemExit('Shared native exporter root binding drift')
source = source.replace(anchor, "visit(obj('level4',2235),-1)", 1).replace(
    "'prefab':['sharedassets4.assets',226]", "'prefab':['level4',2235]")
sys.argv = [str(exporter), str(root / 'ressources/GH_Data'), str(out)]
namespace = {'__name__': '__main__'}
exec(compile(source, str(exporter), 'exec'), namespace)
controller = namespace['obj']('level4', 13391).read_typetree()
header = namespace['obj']('level4', 14852).read_typetree()
assert controller['headerBackground']['m_PathID'] == 14852
assert controller['enhancementPointsText']['m_PathID'] == 10525
assert header['m_Sprite']['m_PathID'] == 0 and header['m_Enabled'] == 1
assert header['m_Color'] == dict(r=1., g=1., b=1., a=1.)
provenance = json.loads((out / 'provenance.json').read_text())
provenance.update(controller=['level4', 13391], header=['level4', 14852], points=['level4', 10525],
                  shared_exporter_sha256=hashlib.sha256(exporter.read_bytes()).hexdigest(),
                  limitation='Exact native Info hierarchy, sprites, colours and TMP appearance; equivalent editor TMP atlas. Conversion staging is explicit; native background filter and Surface choice are production.')
(out / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')

# Import the actual counter's native layout components as well as its graphics.
# The shared row proof does not need these components; this heading does because
# localization and changing point values update preferred text widths at runtime.
layouts = []
for index, node in enumerate(namespace['nodes']):
    game_object = namespace['obj']('level4', node['pathID'])
    for ptr in game_object.read_typetree()['m_Component']:
        component = namespace['resolve'](game_object, ptr['component'])
        if component.type.name != 'MonoBehaviour':
            continue
        data = component.read_typetree()
        if 'm_Padding' in data:
            kind = 1 if 'm_SubtractMarginHorizontal' in data else 2
        elif 'm_IgnoreLayout' in data:
            kind = 3
        elif 'm_text' in data:
            kind = 4
        else:
            continue
        layouts.append((index, kind, data))
with (out / 'native-layout.bin').open('wb') as stream:
    def put(fmt, *values): stream.write(struct.pack('<' + fmt, *values))
    put('H', len(layouts))
    for index, kind, data in layouts:
        put('HB?', index, kind, bool(data['m_Enabled']))
        if kind in (1, 2):
            put('iiiii', *(data['m_Padding']['m_' + key] for key in ('Left', 'Right', 'Top', 'Bottom')), data['m_ChildAlignment'])
            put('f', data['m_Spacing'])
            put('???????', *(bool(data['m_' + key]) for key in ('ChildForceExpandWidth', 'ChildForceExpandHeight', 'ChildControlWidth', 'ChildControlHeight', 'ChildScaleWidth', 'ChildScaleHeight', 'ReverseArrangement')))
            if kind == 1:
                put('????', *(bool(data['m_' + key]) for key in ('SubtractMarginHorizontal', 'SubtractMarginVertical', 'InvertOrder', 'OrderByPriority')))
        elif kind == 3:
            put('?ffffffi', bool(data['m_IgnoreLayout']), *(data['m_' + key] for key in ('MinWidth', 'MinHeight', 'PreferredWidth', 'PreferredHeight', 'FlexibleWidth', 'FlexibleHeight')), data['m_LayoutPriority'])
        else:
            put('??iff', bool(data['m_enableAutoSizing']), bool(data['m_enableWordWrapping']), data['m_overflowMode'], data['m_fontSizeMin'], data['m_fontSizeMax'])
provenance['native_layout_components'] = [dict(node=index, kind=kind) for index, kind, _ in layouts]
provenance['limitation'] = 'Exact native Info hierarchy, original layout components, sprites, colours and TMP appearance; equivalent editor TMP font atlas. Conversion staging is explicit; native background filter and Surface choice are production.'
(out / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
