#!/usr/bin/env python3
"""Read-only independent native/source-stream proof for ten captured structural definitions."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import UnityPy

NAMES = (
    'CR_INT_Stone_Int_Wall_01', 'CR_INT_Stone_Int_Wall_02',
    'CR_INT_Stone_Int_Wall_03', 'CR_INT_Stone_Int_Wall_04',
    'CR_INT_Stone_Int_Wall_02_Narrow', 'CR_INT_Stone_Pillar_04',
    'CR_INT_Wooden_Int_Pillar_Single', 'CR_INT_Wooden_Hut_Pillars_01',
    'CR_INT_Wooden_Hut_Pillars_02', 'CR_INT_Wooden_Hut_Pillars_03',
)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args()
    root = args.source_root.resolve(); output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    prepared = root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    index = json.loads((prepared/'index.json').read_text())
    entries = {entry['signature']['name']: entry for entry in index['entries']
               if entry['signature']['name'] in NAMES}
    assert len(entries) == len(NAMES), 'ten unambiguous captured structural originals'
    relative = 'pcg_databases_assets_assets/pcg/pcg_city.asset.bundle'
    original = root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'/relative
    bundle_sha = hashlib.sha256(original.read_bytes()).hexdigest()
    env = UnityPy.load(str(original))
    meshes = {obj.path_id: obj.read() for obj in env.objects if obj.type.name == 'Mesh'}
    objects = {obj.path_id: obj.read() for obj in env.objects if obj.type.name == 'GameObject'}
    transforms = {obj.path_id: obj.read() for obj in env.objects if obj.type.name == 'Transform'}
    filters = [obj.read() for obj in env.objects if obj.type.name == 'MeshFilter']
    locations = {}
    for native_filter in filters:
        mesh = meshes.get(native_filter.m_Mesh.path_id)
        if mesh is None or mesh.m_Name not in NAMES: continue
        obj = objects[native_filter.m_GameObject.path_id]
        transform = next(transforms[component.component.path_id] for component in obj.m_Component
                         if component.component.path_id in transforms)
        chain = [obj.m_Name]
        while transform.m_Father.path_id:
            transform = transforms[transform.m_Father.path_id]
            chain.append(objects[transform.m_GameObject.path_id].m_Name)
        locations.setdefault(mesh.m_Name, []).append(chain)
    spec = importlib.util.spec_from_file_location('native_export', root/'tools/environment-mesh/export-native.py')
    exporter = importlib.util.module_from_spec(spec); spec.loader.exec_module(exporter)
    results = []
    for name in NAMES:
        entry = entries[name]
        provenance = next(source for source in entry['sources'] if source['path'] == relative)
        assert provenance['sha256'] == bundle_sha, 'actual immutable original bundle hash'
        mesh = meshes[provenance['meshPathId']]
        assert exporter.signature(mesh) == entry['signature'], 'complete independently read native metadata'
        assert not mesh.m_BindPose and not mesh.m_Shapes.channels, 'static structural source, no actor skin/blend-shape'
        raw = output/(entry['key']+'-native.bytes')
        exporter.write(mesh, raw)
        exact = next(variant for variant in entry['variants'] if variant['tier'] == 100)
        coarse = next(variant for variant in entry['variants'] if variant['tier'] == 0)
        assert raw.read_bytes() == (prepared/exact['file']).read_bytes(), 'every exact original channel/index byte matches native source'
        raw.unlink()
        for variant in (exact, coarse):
            assert hashlib.sha256((prepared/variant['file']).read_bytes()).hexdigest() == variant['sha256'], 'full original/coarse stream digest'
        body = [chain for chain in locations[name] if not any(
            token in part.casefold() for part in chain
            for token in ('underwall', 'underfloor', 'doorway', 'doorframe', 'entrance', '_exit_'))]
        assert body, 'genuine non-foundation/non-door structural prefab use'
        results.append({'name': name, 'key': entry['key'], 'exactPath': str(prepared/exact['file']),
            'coarsePath': str(prepared/coarse['file']), 'exactSha256': exact['sha256'],
            'coarseSha256': coarse['sha256'], 'nativeMeshPathId': provenance['meshPathId'],
            'sourceTriangles': sum(entry['signature']['indices'])//3,
            'nativePrefabReferences': len(locations[name]), 'structuralPrefabReferences': len(body),
            'exampleBodyPath': list(reversed(body[0]))})
    receipt = {'nativeBundle': str(original), 'nativeBundleSha256': bundle_sha,
        'indexSha256': hashlib.sha256((prepared/'index.json').read_bytes()).hexdigest(), 'entries': results,
        'limits': ['Prefab reference counts are authored asset references, not scene instances.',
                   'Captured log names establish occurrence, not a complete renderer census.']}
    (output/'native-coverage.json').write_text(json.dumps(receipt, indent=2)+'\n')
    print('PASS independent native structural coverage: 10 exact definitions, every original channel/index byte and coarse digest verified')

if __name__ == '__main__': main()
