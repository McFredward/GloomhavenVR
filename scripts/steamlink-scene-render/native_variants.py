#!/usr/bin/env python3
"""Copy audited native shader programs; alter only fixed alpha blend factors.

Run with the developer UnityPy interpreter. The original game and base bank are
read-only. No shader decompilation/recompilation, renderer approximation or game
asset replacement is performed.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

import UnityPy

SUFFIX = '.SceneCoverage'
STRAIGHT_ALPHA = '_GHVRSceneCoverageStraightAlpha'
ADDITIVE_ALPHA = '_GHVRSceneCoverageAdditiveAlpha'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def derivative(original):
    assert not original['m_Dependencies'] and not original['m_NonModifiableTextures'], 'External native shader dependencies are not admitted'
    result = copy.deepcopy(original)
    result['m_ParsedForm']['m_Name'] += SUFFIX
    changed = 0
    for sub in result['m_ParsedForm']['m_SubShaders']:
        for entry in sub['m_Passes']:
            assert entry['m_Type'] == 0, 'Grab/use passes require a separate audit'
            state = entry['m_State']
            # rtSeparateBlend is independent MRT state, not the separate
            # RGB/alpha-factor switch. Preserve it and every unused RT exactly.
            assert state['rtSeparateBlend'] is False, 'Independent MRT state is not admitted'
            blend = state['rtBlend0']
            src, dst = blend['srcBlend'], blend['destBlend']
            for field in ['srcBlend', 'destBlend', 'srcBlendAlpha', 'destBlendAlpha', 'blendOp', 'blendOpAlpha']:
                assert blend[field]['name'] == '<noninit>', 'Property-bound native blend state is not admitted'
            assert blend['blendOp']['val'] == 0 and blend['blendOpAlpha']['val'] == 0, 'Only native additive blend operations are audited'
            assert src['val'] in (1, 5) and dst['val'] in (1, 10), 'Unsupported native blend factors'
            # Straight or premultiplied translucency contributes coverage once.
            # Additive light contributes RGB without claiming solid coverage.
            assert (src['val'], dst['val']) in ((5, 10), (1, 1)), 'Only audited native straight/additive programs need this binding'
            assert blend['srcBlendAlpha']['val'] == src['val'] and blend['destBlendAlpha']['val'] == dst['val']
            blend['srcBlendAlpha'] = {'name': STRAIGHT_ALPHA if dst['val'] == 10 else ADDITIVE_ALPHA, 'val': 0.0}
            changed += 1
    assert changed > 0, 'Already-correct native shader does not need a derivative'
    return result


def verify_derivative(original, candidate):
    # Full-tree equality includes native bytecode, program/keyword indices,
    # properties, tags, RGB factors/write mask, stencil, depth, offsets and cull.
    restored = copy.deepcopy(candidate)
    assert restored['m_ParsedForm']['m_Name'] == original['m_ParsedForm']['m_Name'] + SUFFIX
    restored['m_ParsedForm']['m_Name'] = original['m_ParsedForm']['m_Name']
    old_subs, new_subs = original['m_ParsedForm']['m_SubShaders'], restored['m_ParsedForm']['m_SubShaders']
    assert len(old_subs) == len(new_subs)
    for old_sub, new_sub in zip(old_subs, new_subs):
        assert len(old_sub['m_Passes']) == len(new_sub['m_Passes'])
        for old_pass, new_pass in zip(old_sub['m_Passes'], new_sub['m_Passes']):
            old, new = old_pass['m_State']['rtBlend0'], new_pass['m_State']['rtBlend0']
            expected = STRAIGHT_ALPHA if old['destBlend']['val'] == 10 else ADDITIVE_ALPHA
            assert new['srcBlendAlpha'] == {'name': expected, 'val': 0.0}, 'Incorrect independent alpha-only global binding'
            assert new['destBlendAlpha'] == old['destBlendAlpha'], 'Native destination factor changed'
            new['srcBlendAlpha'] = copy.deepcopy(old['srcBlendAlpha'])
    assert restored == original, 'Native derivative changed a non-alpha field'
    assert bytes(candidate['compressedBlob']) == bytes(original['compressedBlob']), 'Original GPU programs must remain byte-identical'


def load_sources(root, manifest):
    environments = {}
    records = []
    for item in manifest['variants']:
        path = root / item['source']
        if path not in environments:
            environments[path] = UnityPy.load(str(path))
            assert all(getattr(value, 'version_engine', None) == '2021.3.5f1'
                       for value in environments[path].files.values()), 'Native source bundle editor mismatch'
        objects = [o for o in environments[path].objects if o.type.name == 'Shader' and o.path_id == item['path_id']]
        assert len(objects) == 1, 'Exact original native Shader is missing: ' + item['name']
        obj = objects[0]
        data = obj.read_typetree()
        assert data['m_ParsedForm']['m_Name'] == item['name'], 'Native shader identity drift'
        assert sha(obj.get_raw_data()) == item['shader_sha256'], 'Native shader object changed: ' + item['name']
        assert sha(bytes(data['compressedBlob'])) == item['program_sha256'], 'Native GPU program changed'
        # Publisher bundles strip the inner unity_version to0.0.0; the actual
        # UnityFS header above retains2021.3.5f1. Keep its version22 Shader schema.
        assert obj.assets_file.header.version == 22, 'Native serialized Shader schema mismatch'
        records.append((item, obj, data))
    return records


def append_native(base, root, manifest):
    env = UnityPy.load(base)
    bundles = [o for o in env.objects if o.type.name == 'AssetBundle']
    assert len(bundles) == 1, 'One original loadable bank is required'
    bundle = bundles[0]
    file = bundle.assets_file
    assert file.unity_version == '2021.3.5f1', 'Base bank editor mismatch'
    shader_type = next(i for i, value in enumerate(file.types) if value.class_id == 48)
    originals = {o.path_id: sha(o.get_raw_data()) for o in env.objects if o.type.name == 'Shader'}
    names = {o.read_typetree()['m_ParsedForm']['m_Name'] for o in env.objects if o.type.name == 'Shader'}
    receipts = []
    container = bundle.read_typetree()
    for item, obj, original in load_sources(root, manifest):
        data = derivative(original)
        name = data['m_ParsedForm']['m_Name']
        assert name not in names, 'Native derivative already present: ' + name
        path_id = int.from_bytes(hashlib.sha256(name.encode()).digest()[:8], 'little', signed=True)
        assert path_id not in file.objects, 'Deterministic Shader path ID collision'
        clone = copy.copy(obj)
        clone.assets_file = file
        clone.path_id = path_id
        clone.type_id = shader_type
        clone.serialized_type = file.types[shader_type]
        clone.save_typetree(data)
        file.objects[path_id] = clone
        pointer = {'m_FileID': 0, 'm_PathID': path_id}
        index = len(container['m_PreloadTable'])
        container['m_PreloadTable'].append(pointer)
        path = 'assets/steamlink/native/' + item['name'].lower().replace('/', '-') + '.shader'
        container['m_Container'].append((path, {'preloadIndex': index, 'preloadSize': 1, 'asset': pointer}))
        receipts.append({'original': item['name'], 'derivative': name, 'path_id': path_id,
                         'program_sha256': item['program_sha256'], 'original_shader_sha256': item['shader_sha256'],
                         'platforms': original['platforms']})
        names.add(name)
    # UnityPy reads the original source stream even after save_typetree. Keep
    # one accumulating container tree, then save once; rereading per insertion
    # would silently drop every preceding new path/preload entry.
    bundle.save_typetree(container)
    result = next(iter(env.files.values())).save(packer='lz4')
    reread = UnityPy.load(result)
    expected = {item['name']: original for item, _, original in load_sources(root, manifest)}
    for obj in reread.objects:
        if obj.type.name != 'Shader':
            continue
        if obj.path_id in originals:
            assert sha(obj.get_raw_data()) == originals[obj.path_id], 'Existing owned Shader object changed during serialization'
            continue
        data = obj.read_typetree()
        name = data['m_ParsedForm']['m_Name']
        assert name.endswith(SUFFIX) and name[:-len(SUFFIX)] in expected
        verify_derivative(expected[name[:-len(SUFFIX)]], data)
    assert len(reread.container) == len(names), 'Complete derivative paths must survive in the actual container: ' + str((len(reread.container), len(names), sorted(reread.container)))
    return result, {'schema': 'native-alpha-derivatives-v1', 'unity': file.unity_version,
                    'bytes': len(result), 'sha256': sha(result), 'original_owned_shaders': len(originals),
                    'native_derivatives': receipts, 'limits': 'Exact native D3D11 programs and non-alpha state retained. Linux equation/object-load checks do not execute those native D3D11 programs or establish headset output.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--base', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads((Path(__file__).parent / 'native-variants.json').read_text())
    data, report = append_native(args.base.read_bytes(), args.root.resolve(), manifest)
    args.output.write_bytes(data)
    args.report.write_text(json.dumps(report, indent=2) + '\n')
    print('PASS exact native alpha derivatives: ' + str(len(report['native_derivatives'])) + '; ' + str(len(data)) + ' bytes')


if __name__ == '__main__':
    main()
