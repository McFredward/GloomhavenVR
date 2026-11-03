#!/usr/bin/env python3
"""Read only the two original initiative entry roots; never save native assets."""
import hashlib
import json
from pathlib import Path
import sys
import UnityPy

base, output = map(Path, sys.argv[1:3])
source = base / 'sharedassets6.assets'
environment = UnityPy.load(str(source))
objects = {entry.path_id: entry for entry in environment.objects}
rows = []
for kind, game_object in [('player', 1632), ('enemy', 1105)]:
    original = objects[game_object]
    tree = original.read_typetree()
    pointer = tree['m_Component'][0]['component']
    assert pointer['m_FileID'] == 0
    root = objects[pointer['m_PathID']]
    assert root.type.name == 'RectTransform'
    geometry = root.read_typetree()
    rows.append({'kind': kind, 'name': tree['m_Name'], 'game_object_path_id': game_object,
                 'root_path_id': root.path_id, 'root': geometry,
                 'game_object_sha256': hashlib.sha256(original.get_raw_data()).hexdigest(),
                 'root_sha256': hashlib.sha256(root.get_raw_data()).hexdigest()})
    assert geometry['m_LocalPosition']['z'] == 0, 'Native entry plane is no longer zero'
    assert geometry['m_LocalScale'] == {'x': 1, 'y': 1, 'z': 1}, 'Native entry scale changed'
output.write_text(json.dumps({'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                              'unitypy': UnityPy.__version__, 'roots': rows}, indent=2) + '\n')
