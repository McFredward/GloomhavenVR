#!/usr/bin/env python3
"""Independently bind representative base-game/DLC room geometry to original bundles."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import UnityPy


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, required=True)
    parser.add_argument('--bank-root', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args()
    root = args.source_root.resolve(); bank = args.bank_root.resolve(); output = args.output_dir.resolve()
    prepared = bank/'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    index = json.loads((prepared/'index.json').read_text())
    candidates = [entry for entry in index['entries'] if entry.get('role') in ('floor', 'structure')]
    assert sum(entry['role'] == 'floor' for entry in candidates) >= 100, 'whole-game certified floor catalog required'
    bundles = sorted({source['path'] for entry in candidates for source in entry['sources']},
                     key=lambda path: ('dlc' not in path, path))
    selection = []; selected = set()
    for role, limit in (('floor', 16), ('structure', 8)):
        for relative in bundles:
            matching = sorted((entry for entry in candidates if entry['role'] == role and entry['key'] not in selected
                               and any(source['path'] == relative for source in entry['sources'])),
                              key=lambda entry: sum(entry['signature']['indices']), reverse=True)
            if not matching: continue
            entry = matching[0]; selected.add(entry['key']); selection.append((entry, relative))
            if sum(item['role'] == role for item, _ in selection) >= limit: break
    assert len(selection) == 24 and any('dlc' in path for _, path in selection) and any('dlc' not in path for _, path in selection), 'base-game and DLC representatives'
    dedicated = ('CR_ST_FloorShelf_Stone_Wood_Shelf', 'CR_ST_FloorShelf_Stone_Wood',
                 'CV_Floor_HexOutline_Rock_02', 'CV_Floor_HexOutline_Rock_03', 'CV_Floor_HexOutline_Rock_04',
                 'TERRAIN_DU_Rubble_Floor', 'TERRAIN_DU_Thorns_Floor')
    for name in dedicated:
        entry = next(entry for entry in candidates if entry['signature']['name'] == name)
        if entry['key'] not in selected:
            selection.append((entry, entry['sources'][0]['path'])); selected.add(entry['key'])

    spec = importlib.util.spec_from_file_location('room_native_export', root/'tools/environment-mesh/export-native.py')
    exporter = importlib.util.module_from_spec(spec); spec.loader.exec_module(exporter)
    cache = {}; results = []; output.mkdir(parents=True, exist_ok=True)
    for entry, relative in selection:
        path = root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'/relative
        if relative not in cache:
            environment = UnityPy.load(str(path))
            cache[relative] = (hashlib.sha256(path.read_bytes()).hexdigest(),
                {obj.path_id: obj for obj in environment.objects if obj.type.name == 'Mesh'})
        digest, originals = cache[relative]
        source = next(value for value in entry['sources'] if value['path'] == relative)
        assert digest == source['sha256'], 'native source bundle provenance'
        native = originals[source['meshPathId']].read()
        assert exporter.signature(native) == entry['signature'], 'independent full original native metadata'
        assert not native.m_BindPose and not native.m_Shapes.channels, 'no native actor skin or blend shapes'
        raw = output/(entry['key']+'-native.bytes'); exporter.write(native, raw)
        exact = next(variant for variant in entry['variants'] if variant['tier'] == 100)
        coarse = next((variant for variant in entry['variants'] if variant['tier'] == 0), exact)
        assert raw.read_bytes() == (prepared/exact['file']).read_bytes(), 'all exact original vertex/channel/index bytes'
        raw.unlink()
        for variant in (exact, coarse):
            assert hashlib.sha256((prepared/variant['file']).read_bytes()).hexdigest() == variant['sha256'], 'prepared stream digest'
        results.append({'name': entry['signature']['name'], 'role': entry['role'], 'key': entry['key'],
            'sourcePath': str(path), 'sourceSha256': digest, 'nativeMeshPathId': source['meshPathId'],
            'exactPath': str(prepared/exact['file']), 'coarsePath': str(prepared/coarse['file']),
            'exactSha256': exact['sha256'], 'coarseSha256': coarse['sha256'],
            'sourceTriangles': sum(entry['signature']['indices'])//3})
    receipt = {'indexPath': str(prepared/'index.json'), 'indexSha256': hashlib.sha256((prepared/'index.json').read_bytes()).hexdigest(),
               'entries': results, 'limits': ['Representative runtime coverage is not a complete hardware rendering census.',
               'Complete authored role classification and footprint certificates belong to the separately checked all-game asset catalog.']}
    (output/'room-native-coverage.json').write_text(json.dumps(receipt, indent=2)+'\n')
    print('PASS independent native room coverage: 16 floor + representative and dedicated structural identities, original bundle/channel bytes and base-game/DLC streams verified')


if __name__ == '__main__': main()
