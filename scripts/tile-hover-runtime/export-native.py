"""Recover original Hex selection mesh and BearTrap collider without changing game files."""
import hashlib
import json
from pathlib import Path
import sys
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

asset, output = map(Path, sys.argv[1:])
environment = UnityPy.load(str(asset))
objects = {obj.path_id: obj for obj in environment.objects}


def component(game_object, kind):
    for entry in objects[game_object].read().m_Component:
        reference = entry.component if hasattr(entry, 'component') else entry[1]
        obj = objects[reference.path_id]
        if obj.type.name == kind:
            return obj.path_id, obj.read_typetree()
    raise ValueError(f'Missing original {kind} on {game_object}')


named = {obj.read().m_Name: obj.path_id for obj in environment.objects if obj.type.name == 'GameObject'}
hex_id, trap_id = named['Hex'], named['BearTrap']
mesh_collider_id, mesh_collider = component(hex_id, 'MeshCollider')
box_id, box = component(hex_id, 'BoxCollider')
trap_box_id, trap_box = component(trap_id, 'BoxCollider')
mesh_id = mesh_collider['m_Mesh']['m_PathID']
mesh = MeshHandler(objects[mesh_id].read()); mesh.process()
assert mesh_collider['m_Enabled'] and mesh_collider['m_Convex'] and not box['m_Enabled']
data = {
    'vertices': [dict(zip(('x', 'y', 'z'), vertex)) for vertex in mesh.m_Vertices],
    'triangles': [index for submesh in mesh.get_triangles() for triangle in submesh for index in triangle],
    'trapSize': trap_box['m_Size'], 'trapCenter': trap_box['m_Center'],
    'pitch': box['m_Size']['x'], 'hexBoxEnabled': box['m_Enabled'], 'hexConvex': mesh_collider['m_Convex'],
}
output.write_text(json.dumps(data, indent=2) + '\n')
output.with_name('native-provenance.json').write_text(json.dumps({
    'source': str(asset), 'sha256': hashlib.file_digest(asset.open('rb'), 'sha256').hexdigest(),
    'hexGameObject': hex_id, 'hexMeshCollider': mesh_collider_id, 'hexMesh': mesh_id,
    'hexDisabledBoxCollider': box_id, 'trapGameObject': trap_id, 'trapBoxCollider': trap_box_id,
    'nativeDataSha256': hashlib.sha256(output.read_bytes()).hexdigest(),
    'boundary': 'Original serialized mesh vertices/triangles and collider settings are reconstructed in actual Unity 2021.3.5; native gameplay and tooltip windows are not run.',
}, indent=2) + '\n')
