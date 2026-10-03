#!/usr/bin/env python3
"""Read exact script identities/conditional fields from the original Drake hierarchy."""
import json
from pathlib import Path
import sys
import UnityPy

bundle, output = map(Path, sys.argv[1:])
environment = UnityPy.load(str(bundle))


def path_for(game_object):
    transform = next(pair.component.read() for pair in game_object.m_Component
                     if pair.component.type.name == 'Transform')
    names = [game_object.m_Name]
    while transform.m_Father.m_PathID:
        transform = transform.m_Father.read()
        names.append(transform.m_GameObject.read().m_Name)
    return '/'.join(reversed(names))


records = []
for obj in environment.objects:
    if obj.type.name != 'MonoBehaviour': continue
    data = obj.read()
    if not data.m_Script.m_PathID or not data.m_GameObject.m_PathID: continue
    script = data.m_Script.read()
    path = path_for(data.m_GameObject.read())
    prefix = next((candidate for candidate in ('MO_SpittingDrake_PR', 'Spitting Drake')
                   if path == candidate or path.startswith(candidate + '/')), None)
    if prefix is None: continue
    tree = obj.read_typetree()
    scalars = {key: value for key, value in tree.items()
               if isinstance(value, (bool, int, float, str)) and key not in ('m_Name', 'm_Enabled')}
    records.append({'assembly': script.m_AssemblyName.removesuffix('.dll'),
                    'type': (script.m_Namespace + '.' if script.m_Namespace else '') + script.m_ClassName,
                    'path': path.removeprefix(prefix).lstrip('/'),
                    'original_root': prefix,
                    'fields': [{'name': key, 'value': str(value)} for key, value in scalars.items()],
                    'enabled': bool(tree['m_Enabled'])})
output.write_text(json.dumps({'components': records}, indent=2) + '\n')
if len(records) < 8: raise SystemExit('Original native component inventory unexpectedly incomplete')
