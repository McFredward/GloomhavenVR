#!/usr/bin/env python3
"""Read original native skinned actor meshes into an explicit lossless geometry stream.

Run with UnityPy installed; game bundles are read-only. No generated geometry is written
into game resources. The companion Unity tool creates derivative meshes in its own project.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


def export_mesh(mesh, target, blend_shapes=False):
    handler = MeshHandler(mesh)
    handler.process()
    triangles = handler.get_triangles()
    with target.open('wb') as out:
        def ints(*values): out.write(struct.pack('<'+'i'*len(values), *values))
        def floats(values): out.write(struct.pack('<'+'f'*len(values), *values))
        def channel(values, width):
            ints(len(values or []), width)
            for row in values or []: floats(tuple(row)[:width])
        out.write(b'GHFM2' if blend_shapes else b'GHFM1')
        name = mesh.m_Name.encode('utf-8'); ints(len(name)); out.write(name)
        for vector in (mesh.m_LocalAABB.m_Center, mesh.m_LocalAABB.m_Extent): floats((vector.x, vector.y, vector.z))
        channel(handler.m_Vertices, 3); channel(handler.m_Normals, 3)
        channel(handler.m_Tangents, 4); channel(handler.m_Colors, 4)
        for index in range(8):
            uv = getattr(handler, 'm_UV'+str(index))
            channel(uv, len(uv[0]) if uv else 2)
        weights = handler.m_BoneWeights or []; indices = handler.m_BoneIndices or []
        ints(len(weights))
        for weight, bones in zip(weights, indices):
            # Original native streams can encode exactly two influences. Pad inactive slots,
            # preserving the source values; the runtime renderer keeps its own skin quality.
            floats(tuple(weight)+(0.0,)*(4-len(weight))); ints(*(tuple(bones)+(0,)*(4-len(bones))))
        ints(len(mesh.m_BindPose))
        for matrix in mesh.m_BindPose: floats([getattr(matrix, f'e{r}{c}') for r in range(4) for c in range(4)])
        ints(len(triangles))
        for sub in triangles:
            flat = [index for triangle in sub for index in triangle]; ints(len(flat)); ints(*flat)
        if blend_shapes:
            shapes = mesh.m_Shapes
            ints(len(shapes.channels))
            for shape in shapes.channels:
                encoded = shape.name.encode('utf-8'); ints(len(encoded)); out.write(encoded)
                ints(shape.frameCount)
                for frame_index in range(shape.frameIndex, shape.frameIndex + shape.frameCount):
                    frame = shapes.shapes[frame_index]
                    floats([shapes.fullWeights[frame_index]]); ints(frame.vertexCount)
                    for vertex in shapes.vertices[frame.firstVertex:frame.firstVertex + frame.vertexCount]:
                        ints(vertex.index)
                        for vector in (vertex.vertex, vertex.normal, vertex.tangent): floats((vector.x, vector.y, vector.z))
    return {'name': mesh.m_Name, 'vertices': len(handler.m_Vertices), 'triangles': sum(map(len, triangles)),
            'submeshes': len(triangles), 'readable': mesh.m_IsReadable,
            'geometry_sha256': hashlib.sha256(target.read_bytes()).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--only', nargs='*', help='Limit bundle stems for a focused proof')
    args = parser.parse_args(); args.output_dir.mkdir(parents=True, exist_ok=True)
    bundles = args.source_root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'
    records = []; seen = set(); skipped = []
    for path in sorted(bundles.glob('*_assets_all.bundle')):
        if not path.name.startswith(('hero_', 'npc_')) or (args.only and path.stem not in args.only): continue
        env = UnityPy.load(str(path)); digest = hashlib.sha256(path.read_bytes()).hexdigest()
        cloth_gameobjects = {obj.read().m_GameObject.path_id for obj in env.objects if obj.type.name == 'Cloth'}
        skins = {obj.read().m_Mesh.path_id for obj in env.objects if obj.type.name == 'SkinnedMeshRenderer'
                 and obj.read().m_GameObject.path_id not in cloth_gameobjects}
        for obj in env.objects:
            if obj.type.name != 'Mesh' or obj.path_id not in skins: continue
            mesh = obj.read()
            # Fine weapons, cloth solver meshes, tiny ornaments and authored FX shells retain
            # original geometry. Bodies/LOD surfaces receive genuine coarse derivatives.
            if mesh.m_VertexData.m_VertexCount < 1000 or mesh.m_Name.startswith(('WP_', 'P_')): continue
            if mesh.m_Shapes.channels:
                skipped.append({'bundle': path.name, 'mesh': mesh.m_Name, 'reason': 'blendshape import unsupported; original retained'})
                continue
            if any(sub.topology != 0 for sub in mesh.m_SubMeshes): continue
            output = args.output_dir/f'{len(records):04d}.mesh'
            record = export_mesh(mesh, output)
            if record['geometry_sha256'] in seen: output.unlink(); continue
            seen.add(record['geometry_sha256'])
            record.update({'file': output.name, 'bundle': path.name, 'bundle_sha256': digest})
            records.append(record)
    (args.output_dir/'sources.json').write_text(json.dumps({'format': 'GHFM1', 'unitypy': UnityPy.__version__,
        'meshes': records, 'excluded': skipped}, indent=2)+'\n')
    print(f'Exported {len(records)} unique original body/LOD meshes, {len(skipped)} explicitly retained blendshape meshes')


if __name__ == '__main__': main()
