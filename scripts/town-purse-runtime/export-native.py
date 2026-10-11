"""Read the exact original Treasure.Bay purse without writing game references."""
import hashlib
import json
from pathlib import Path
import sys

import UnityPy

root, output = map(Path, sys.argv[1:3])
bundle = root / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/pcg_databases_assets_assets/pcg/pcg_treasure.asset.bundle'
env = UnityPy.load(str(bundle))
for obj in env.objects:
    if obj.type.name != 'GameObject':
        continue
    game_object = obj.read()
    if game_object.m_Name != 'CR_ST_Shelf_KitchenItems_Bag_01 (3)':
        continue
    components = {pair.component.type.name: pair.component for pair in game_object.m_Component}
    transform = components['Transform'].read_typetree()
    mesh_ref = components['MeshFilter'].read().m_Mesh
    mesh = mesh_ref.read()
    vertices, triangles = [], []
    for line in mesh.export().splitlines():
        parts = line.split()
        if not parts:
            continue
        if parts[0] == 'v':
            # UnityPy's OBJ export reflects X; undo it for the Unity runtime mesh.
            vertices.append({'x': -float(parts[1]), 'y': float(parts[2]), 'z': float(parts[3])})
        elif parts[0] == 'f':
            face = [int(index.split('/')[0]) - 1 for index in parts[1:]]
            for at in range(1, len(face) - 1):
                triangles.extend((face[0], face[at + 1], face[at]))
    if not vertices or not triangles:
        raise SystemExit('Original purse mesh is empty; a cube substitute is not evidence.')
    output.write_text(json.dumps({'position': transform['m_LocalPosition'],
        'rotation': transform['m_LocalRotation'], 'scale': transform['m_LocalScale'],
        'vertices': vertices, 'triangles': triangles}, separators=(',', ':')) + '\n')
    output.with_suffix('.provenance.json').write_text(json.dumps({
        'bundle': str(bundle), 'bundle_sha256': hashlib.sha256(bundle.read_bytes()).hexdigest(),
        'game_object': game_object.m_Name, 'game_object_path_id': obj.path_id,
        'mesh': mesh.m_Name, 'mesh_path_id': mesh_ref.path_id,
        'vertices': len(vertices), 'triangles': len(triangles) // 3,
        'data_sha256': hashlib.sha256(output.read_bytes()).hexdigest()}, indent=2) + '\n')
    break
else:
    raise SystemExit('The original Treasure.Bay purse is missing; native provenance cannot be skipped.')
