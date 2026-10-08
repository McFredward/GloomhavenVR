#!/usr/bin/env python3
"""Readonly original PCG prefab-chain reconstruction for the actual Unity scenery fixture.

The complete census supplies mesh-use chains; exported sibling meshes are originals, not
synthetic bounds/topology. Native controllers are represented by explicit inert component
boundaries in Unity; this does not execute the original game's procedural recipes.
"""
import argparse
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import sys


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--catalog-root', type=Path, required=True)
    p.add_argument('--game-streaming-root', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    tools = a.catalog_root / 'tools/environment-mesh'
    index_path = a.catalog_root / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/index.json'
    index = json.loads(index_path.read_text())
    entries = {entry['key']: entry for entry in index['entries']}
    catalog_path = tools / 'catalog.json.gz'
    catalog = json.loads(gzip.decompress(catalog_path.read_bytes()))
    ornament_path = tools / 'ornaments.json'
    ornaments = json.loads(ornament_path.read_text())
    positive = {entry['key'] for entry in ornaments['entries']}
    assert catalog['completePcgCensus'] and len(catalog['meshes']) == 8864
    groups = {(mesh['source']['path'], use['route'][0]) for mesh in catalog['meshes']
        if mesh['key'] in positive for use in mesh['uses']}
    sys.path.insert(0, str(tools))
    spec = importlib.util.spec_from_file_location('scenery_original_export', tools / 'export-native.py')
    export = importlib.util.module_from_spec(spec); spec.loader.exec_module(export)
    import UnityPy
    environment = {}
    mesh_files = {}
    native_hashes = {}
    a.output.mkdir(parents=True, exist_ok=True)
    facts = []
    for mesh in catalog['meshes']:
        uses = [use for use in mesh['uses'] if (mesh['source']['path'], use['route'][0]) in groups]
        if not uses: continue
        key = mesh['key']
        source = mesh['source']
        source_path = a.game_streaming_root / 'aa/StandaloneWindows64' / source['path']
        if source['path'] not in native_hashes:
            digest = hashlib.sha256(source_path.read_bytes()).hexdigest()
            assert digest == source['sha256'], 'readonly original bundle hash mismatch'
            native_hashes[source['path']] = digest
        if key not in mesh_files:
            entry = entries.get(key)
            if entry:
                exact = next(v for v in entry['variants'] if v['tier'] == 100)
                original_path = index_path.parent / exact['file']
                digest = hashlib.sha256(original_path.read_bytes()).hexdigest()
                assert digest == exact['sha256'] and entry['signature'] == mesh['signature']
            else:
                if source['path'] not in environment:
                    environment[source['path']] = UnityPy.load(str(source_path))
                native = next(obj.read() for obj in environment[source['path']].objects
                    if obj.type.name == 'Mesh' and obj.path_id == source['meshPathId'])
                assert export.signature(native) == mesh['signature'], 'original sibling signature mismatch'
                original_path = a.output / (key + '-original.bytes')
                digest = export.write(native, original_path)
            mesh_files[key] = {'path': str(original_path.resolve()), 'sha256': digest,
                'key': key, 'name': mesh['signature']['name'], 'readable': mesh['signature']['readable']}
        for use in uses:
            facts.append({'source': source['path'], 'root': use['route'][0], 'meshKey': key,
                'ornament': key in positive, 'chain': use['chain'], 'unresolved': use['unresolved']})
            for node in facts[-1]['chain']:
                for component in node['components']:
                    if 'enabled' in component: component['enabled'] = bool(component['enabled'])
    graphs = []
    for source, root in sorted(groups):
        members = [fact for fact in facts if fact['source'] == source and fact['root'] == root]
        # Duplicate paths can denote separate authored instances. Keep exact use identities
        # and distinguish by local poses; no native occurrence is omitted from this proof.
        graphs.append({'source': source, 'root': root, 'members': members})
    result = {'meshes': list(mesh_files.values()), 'graphs': graphs,
        'catalogSha256': hashlib.sha256(catalog_path.read_bytes()).hexdigest(),
        'ornamentsSha256': hashlib.sha256(ornament_path.read_bytes()).hexdigest(),
        'indexSha256': hashlib.sha256(index_path.read_bytes()).hexdigest(),
        'bundles': native_hashes, 'scope': 'readonly original mesh geometry + exact census Transform/component chains; game procedural/lifecycle scripts are inert boundaries'}
    (a.output / 'graphs.json').write_text(json.dumps(result, indent=2) + '\n')
    print('Prepared original graph facts: %d prefab-source contexts, %d exact mesh identities, %d mesh uses, %d bundle hashes' % (len(graphs), len(mesh_files), len(facts), len(native_hashes)))

if __name__ == '__main__': main()
