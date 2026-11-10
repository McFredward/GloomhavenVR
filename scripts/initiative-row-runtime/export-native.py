#!/usr/bin/env python3
"""Read only the two original initiative entry roots; never save native assets."""
import hashlib
import json
from pathlib import Path
import sys
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

base, output = map(Path, sys.argv[1:3])
source = base / 'sharedassets6.assets'
environment = UnityPy.load(str(source))
generator = TypeTreeGenerator('2021.3.5f1')
generator.load_local_dll_folder(str(base / 'Managed'))
environment.typetree_generator = generator
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
    # The native click handler is nested on ActorContent; its Header is an Image
    # whose authored rect extends beyond the entry layout root.
    avatar = objects[9648 if kind == 'player' else 8775].read_typetree()['avatarButton']
    button = objects[avatar['m_PathID']].read_typetree()
    button_go = objects[button['m_GameObject']['m_PathID']].read_typetree()
    button_rect = objects[button_go['m_Component'][0]['component']['m_PathID']]
    header = objects[5477 if kind == 'player' else 5262]
    rows.append({'kind': kind, 'name': tree['m_Name'], 'game_object_path_id': game_object,
                 'root_path_id': root.path_id, 'root': geometry,
                 'button_path_id': avatar['m_PathID'], 'button_rect': button_rect.read_typetree(),
                 'header': header.read_typetree(),
                 'header_sha256': hashlib.sha256(header.get_raw_data()).hexdigest(),
                 'game_object_sha256': hashlib.sha256(original.get_raw_data()).hexdigest(),
                 'root_sha256': hashlib.sha256(root.get_raw_data()).hexdigest()})
    assert geometry['m_LocalPosition']['z'] == 0, 'Native entry plane is no longer zero'
    assert geometry['m_LocalScale'] == {'x': 1, 'y': 1, 'z': 1}, 'Native entry scale changed'
output.write_text(json.dumps({'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                              'unitypy': UnityPy.__version__, 'roots': rows}, indent=2) + '\n')
