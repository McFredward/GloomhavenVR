#!/usr/bin/env python3
"""Extract immutable static originals and certify roles using every PCG use."""
import argparse
import collections
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
sys.path.insert(0, str(Path(__file__).resolve().parent))
from roles import classify, family_candidate


def signature(mesh):
    bounds = mesh.m_LocalAABB
    return {'name': mesh.m_Name, 'readable': mesh.m_IsReadable,
            'vertices': mesh.m_VertexData.m_VertexCount,
            'indices': [sub.indexCount for sub in mesh.m_SubMeshes],
            'bounds': [bounds.m_Center.x, bounds.m_Center.y, bounds.m_Center.z,
                       bounds.m_Extent.x, bounds.m_Extent.y, bounds.m_Extent.z]}


def write(mesh, path):
    handler = MeshHandler(mesh)
    handler.process()
    with path.open('wb') as output:
        def ints(*values): output.write(struct.pack('<' + 'i' * len(values), *values))
        def floats(values): output.write(struct.pack('<' + 'f' * len(values), *values))
        def channel(values, width):
            ints(len(values or []), width)
            for row in values or []: floats(tuple(row)[:width])
        output.write(b'GHEM1')
        encoded = mesh.m_Name.encode()
        ints(len(encoded)); output.write(encoded)
        floats(signature(mesh)['bounds'])
        channel(handler.m_Vertices, 3); channel(handler.m_Normals, 3)
        channel(handler.m_Tangents, 4); channel(handler.m_Colors, 4)
        for index in range(8):
            values = getattr(handler, 'm_UV' + str(index))
            channel(values, len(values[0]) if values else 2)
        triangles = handler.get_triangles()
        ints(len(triangles))
        for submesh in triangles:
            flat = [index for triangle in submesh for index in triangle]
            ints(len(flat)); ints(*flat)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def original_uses(environment):
    """Inspect the actual leaf and ancestors, without guessing from the prefab name.

    Local path IDs are scoped to their serialized file. Unity default/external
    meshes cannot accidentally approve a different local object with the same ID.
    """
    cache = {}
    def read(pointer):
        key = (id(pointer.assetsfile), pointer.path_id, pointer.file_id)
        if key not in cache: cache[key] = pointer.read()
        return cache[key]
    def vector(value):
        result = [value.x, value.y, value.z]
        if hasattr(value, 'w'): result.append(value.w)
        return result
    def fact(pointer):
        kind = pointer.type.name
        result = {'type': kind}
        data = read(pointer)
        if kind == 'MonoBehaviour':
            try: result['script'] = read(data.m_Script).m_ClassName
            except (FileNotFoundError, ValueError, AttributeError): result['script'] = '<unresolved>'
        if kind.endswith('Collider'):
            result.update({'enabled': data.m_Enabled, 'isTrigger': data.m_IsTrigger})
            for name in ('m_Center', 'm_Size'):
                if hasattr(data, name): result[name[2:]] = vector(getattr(data, name))
            for name in ('m_Radius', 'm_Height', 'm_Direction', 'm_Convex'):
                if hasattr(data, name): result[name[2:]] = getattr(data, name)
            if hasattr(data, 'm_Mesh'):
                result['meshPathId'] = data.m_Mesh.path_id
                result['meshFileId'] = data.m_Mesh.file_id
        if kind == 'Rigidbody':
            result.update({'isKinematic': data.m_IsKinematic, 'useGravity': data.m_UseGravity})
        if kind == 'Animator':
            result.update({'enabled': data.m_Enabled, 'controllerPathId': data.m_Controller.path_id})
        if kind == 'Light':
            result.update({'enabled': data.m_Enabled, 'lightType': data.m_Type, 'intensity': data.m_Intensity})
        if kind == 'MeshFilter':
            result.update({'meshPathId': data.m_Mesh.path_id, 'meshFileId': data.m_Mesh.file_id})
        return result
    uses = collections.defaultdict(list)
    for reader in environment.objects:
        if reader.type.name != 'MeshFilter': continue
        component = reader.read()
        if component.m_Mesh.file_id != 0: continue
        try: read(component.m_Mesh)
        except (FileNotFoundError, ValueError): continue
        node = read(component.m_GameObject)
        route = []; scripts = set(); components = set(); leaf = []; chain = []
        unresolved = False; visited = set()
        while True:
            route.append(node.m_Name)
            pointers = [slot.component for slot in node.m_Component]
            kinds = [pointer.type.name for pointer in pointers]
            components.update(kinds)
            if len(route) == 1: leaf = sorted(kinds)
            for pointer in pointers:
                if pointer.type.name == 'MonoBehaviour':
                    try: scripts.add(read(read(pointer).m_Script).m_ClassName)
                    except (FileNotFoundError, ValueError, AttributeError): unresolved = True
            transform = next((read(pointer) for pointer in pointers if pointer.type.name == 'Transform'), None)
            chain.append({'name': node.m_Name, 'components': [fact(pointer) for pointer in pointers],
                          'localPosition': vector(transform.m_LocalPosition) if transform else None,
                          'localRotation': vector(transform.m_LocalRotation) if transform else None,
                          'localScale': vector(transform.m_LocalScale) if transform else None})
            if transform is None or not transform.m_Father.path_id: break
            identity = (id(transform.m_Father.assetsfile), transform.m_Father.path_id)
            if identity in visited or len(route) >= 64:
                unresolved = True; break
            visited.add(identity)
            node = read(read(transform.m_Father).m_GameObject)
        key = (id(component.m_Mesh.assetsfile), component.m_Mesh.path_id)
        uses[key].append({'route': list(reversed(route)), 'components': sorted(components),
                         'leafComponents': leaf, 'scripts': sorted(scripts), 'unresolved': unresolved,
                         'meshFilterPathId': reader.path_id, 'chain': list(reversed(chain))})
    return uses


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--only', nargs='*')
    args = parser.parse_args()
    root = args.source_root / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'
    source_folder = root / 'pcg_databases_assets_assets/pcg'
    if not source_folder.is_dir():
        raise SystemExit('Native environment source folder is missing: ' + str(source_folder))
    bundles = [path for path in sorted(source_folder.glob('*.bundle'))
               if not args.only or path.stem in args.only]
    if not bundles:
        raise SystemExit('No native environment bundles match the requested source selection.')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    records = {}; ambiguous = set(); census = []; bundle_receipts = []
    for path in bundles:
        environment = UnityPy.load(str(path))
        bundle_sha = hashlib.sha256(path.read_bytes()).hexdigest()
        bundle_path = path.relative_to(root).as_posix()
        bundle_receipts.append({'path': bundle_path, 'sha256': bundle_sha})
        uses = original_uses(environment)
        for reader in environment.objects:
            if reader.type.name != 'Mesh': continue
            mesh_uses = uses.get((id(reader.assets_file), reader.path_id), [])
            if not mesh_uses: continue
            mesh = reader.read()
            sig = signature(mesh)
            identity = json.dumps(sig, sort_keys=True, separators=(',', ':'))
            key = hashlib.sha256(identity.encode()).hexdigest()[:24]
            provenance = {'path': bundle_path, 'sha256': bundle_sha, 'meshPathId': reader.path_id}
            role, reasons = classify(mesh.m_Name, mesh_uses)
            census.append({'key': key, 'signature': sig, 'source': provenance, 'role': role,
                           'reasons': reasons, 'uses': mesh_uses})
            # Preserve the historical bank's originals for exact-copy consumers.
            # Expand only with certified architecture; protected novel sources stay native.
            legacy = re.search(r'floor|wall|pillar|rock|terrain|cliff|slab|slope|stairs|underfloor', mesh.m_Name, re.I)
            if not legacy and not family_candidate(mesh.m_Name): continue
            if (mesh.m_VertexData.m_VertexCount < 3 or mesh.m_VertexData.m_VertexCount > 100000
                    or mesh.m_BindPose or mesh.m_Shapes.channels
                    or any(sub.topology != 0 for sub in mesh.m_SubMeshes)):
                continue
            raw = args.output_dir / (key + '.bytes')
            sha = write(mesh, raw)
            if key in records:
                if records[key]['sha256'] != sha:
                    ambiguous.add(key); continue
                records[key]['sources'].append(provenance)
                records[key]['uses'].extend(mesh_uses)
                continue
            records[key] = {'key': key, 'file': raw.name, 'sha256': sha, 'signature': sig,
                            'sources': [provenance], 'uses': list(mesh_uses)}
        print('Scanned ' + path.name, flush=True)
    for key in ambiguous:
        records.pop(key, None)
        (args.output_dir / (key + '.bytes')).unlink(missing_ok=True)
    # A mixed safe/protected use of an identical source is not an approved family.
    # Classification here deliberately spans every scanned theme/DLC bundle.
    complete_uses = collections.defaultdict(list)
    for row in census: complete_uses[row['key']].extend(row['uses'])
    for record in records.values():
        record['uses'] = complete_uses[record['key']]
        record['role'], record['roleReasons'] = classify(record['signature']['name'], record['uses'])
    if not records:
        raise SystemExit('Native environment extraction produced no admissible originals; existing prepared assets must remain unchanged.')
    result = {'format': 1, 'unitypy': UnityPy.__version__, 'meshes': list(records.values()),
              'ambiguousRejected': sorted(ambiguous), 'catalog': census, 'bundles': bundle_receipts,
              'completePcgCensus': not args.only}
    (args.output_dir / 'sources.json').write_text(json.dumps(result, indent=2) + '\n')
    print(f'Exact static originals: {len(records)}, ambiguous identities rejected: {len(ambiguous)}; '
          f'roles: {dict(collections.Counter(record["role"] for record in records.values()))}')


if __name__ == '__main__': main()
