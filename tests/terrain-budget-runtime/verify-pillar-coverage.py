#!/usr/bin/env python3
"""Independently read every pillar definition admitted by private terrain."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import UnityPy

NAMES = ('EN_CR_Pillar_Thin', 'EN_CR_Pillar_Large', 'EN_CR_Pillar_Large_02',
         'CV_Pillar_Generic_01', 'CV_Pillar_Generic_02', 'CR_INT_Stone_Pillar_04',
         'CR_INT_Wooden_Int_Pillar_Single', 'CR_INT_Wooden_Hut_Pillars_01',
         'CR_INT_Wooden_Hut_Pillars_02', 'CR_INT_Wooden_Hut_Pillars_03')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args(); root = args.source_root.resolve(); output = args.output_dir.resolve()
    prepared = root/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    index = json.loads((prepared/'index.json').read_text())
    entries = {entry['signature']['name']: entry for entry in index['entries'] if entry['signature']['name'] in NAMES}
    assert len(entries) == len(NAMES), 'all ten admitted pillar definitions exist in the verified immutable bank'
    spec = importlib.util.spec_from_file_location('native_export', root/'tools/environment-mesh/export-native.py')
    exporter = importlib.util.module_from_spec(spec); spec.loader.exec_module(exporter)
    bundles = {}; results = []
    for name in NAMES:
        entry = entries[name]; source = entry['sources'][0]; relative = source['path']
        original = root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'/relative
        if relative not in bundles:
            env = UnityPy.load(str(original))
            bundles[relative] = (hashlib.sha256(original.read_bytes()).hexdigest(),
                                 {obj.path_id: obj.read() for obj in env.objects if obj.type.name == 'Mesh'})
        sha, meshes = bundles[relative]; assert sha == source['sha256'], 'immutable native bundle digest'
        mesh = meshes[source['meshPathId']]; assert exporter.signature(mesh) == entry['signature'], 'independent complete native pillar metadata'
        raw = output/(entry['key']+'-pillar-native.bytes'); exporter.write(mesh, raw)
        exact = next(tier for tier in entry['variants'] if tier['tier'] == 100)
        coarse = next(tier for tier in entry['variants'] if tier['tier'] == 0)
        assert raw.read_bytes() == (prepared/exact['file']).read_bytes(), 'every exact native pillar channel/index byte matches'
        raw.unlink()
        for tier in (exact, coarse):
            assert hashlib.sha256((prepared/tier['file']).read_bytes()).hexdigest() == tier['sha256'], 'complete exact/coarse pillar stream digest'
        results.append({'name': name, 'exactPath': str(prepared/exact['file']), 'coarsePath': str(prepared/coarse['file']),
                        'sourceTriangles': sum(entry['signature']['indices'])//3, 'nativeBundle': str(original), 'nativeBundleSha256': sha,
                        'nativeMeshPathId': source['meshPathId'], 'bankSourcePaths': [p['path'] for p in entry['sources']]})
    (output/'native-pillar-coverage.json').write_text(json.dumps({'entries': results,
        'limits': ['Definition provenance, not procedural scene-instance census or current game LOD ownership.']}, indent=2)+'\n')
    print('PASS independent native pillar coverage: all 10 admitted definitions; exact channel/index bytes and coarse digests verified')

if __name__ == '__main__': main()
