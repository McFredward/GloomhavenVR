#!/usr/bin/env python3
"""Add an exact native card shader with only ZWrite enabled to the mod bundle.

The game's read-only bundle is an input. The derived shader keeps every original
compiled program, property, keyword, dependency and other render state. Runtime
selects its existing UNITY_UI_ALPHACLIP variant only during a raised-card loan.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

SOURCE_SHA = '15b6ac4f91858306e3c6136efbdcf89c6118486dd2191eb27c3c1e9b92018102'
SHADER_ID = 7314317428712670795
SHADER_SHA = 'fbd49fffa5bb88c6a448a659d87f41b6d2507f37ddd5def25afde34c00f9bb49'
PROGRAM_SHA = 'c516fa367bcb4e9a9190c490386e31cf380bff6a3c793bfe60896ea9b3c2d4ef'
DERIVED_ID = -7314317428712670795
DERIVED_NAME = 'GloomhavenVR/MenuDepthNativeCard'
ASSET = 'assets/menudepth/menudepthnativecard.shader'


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def canonical(tree):
    return json.dumps(tree, sort_keys=True, separators=(',', ':'), allow_nan=False)


def derive(native_path, input_path, output_path):
    import UnityPy
    raw = native_path.read_bytes()
    assert sha(raw) == SOURCE_SHA, 'Exact original card bundle required'
    native = next(o for o in UnityPy.load(raw).objects if o.path_id == SHADER_ID)
    assert sha(native.get_raw_data()) == SHADER_SHA, 'Original shader pin changed'
    tree = native.read_typetree()
    assert tree['platforms'] == [4], 'Audited Direct3D11 programs required'
    assert not tree['m_Dependencies'] and not tree['m_NonModifiableTextures']
    assert not tree['m_ParsedForm']['m_Dependencies']
    assert sha(bytes(tree['compressedBlob'])) == PROGRAM_SHA
    assert tree['m_ParsedForm']['m_Name'] == 'GUI/AbilityCard_Shd'
    sub = tree['m_ParsedForm']['m_SubShaders']
    assert len(sub) == 1 and len(sub[0]['m_Passes']) == 1
    state = sub[0]['m_Passes'][0]['m_State']
    assert state['m_Name'] == 'Default'
    assert state['zWrite'] == {'val': 0.0, 'name': '<noninit>'}
    changed = copy.deepcopy(tree)
    changed['m_ParsedForm']['m_Name'] = DERIVED_NAME
    changed['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['m_State']['zWrite']['val'] = 1.0
    original_mod = input_path.read_bytes()
    env = UnityPy.load(original_mod)
    bundle = next(iter(env.files.values()))
    assert len(env.assets) == 1
    serialized = env.assets[0]
    assert not serialized.externals and DERIVED_ID not in serialized.objects
    original_objects = {i: o.get_raw_data() for i, o in serialized.objects.items()}
    bank = next(o for o in env.objects if o.type.name == 'AssetBundle')
    bank_tree = bank.read_typetree()
    assert not bank_tree['m_Dependencies'] and not bank_tree['m_IsStreamedSceneAssetBundle']
    assert ASSET not in dict(bank_tree['m_Container'])
    # Clone only a Shader reader's matching Unity2021.3 type metadata. Its entire
    # serialized payload is replaced with the exactly audited native shader tree.
    reader = copy.copy(next(o for o in env.objects if o.type.name == 'Shader'))
    reader.path_id = DERIVED_ID
    reader.save_typetree(changed)
    serialized.objects[DERIVED_ID] = reader
    pointer = {'m_FileID': 0, 'm_PathID': DERIVED_ID}
    index = len(bank_tree['m_PreloadTable'])
    bank_tree['m_PreloadTable'].append(pointer)
    bank_tree['m_Container'].append((ASSET, {'preloadIndex': index, 'preloadSize': 1, 'asset': pointer}))
    bank.save_typetree(bank_tree)
    # A distinct CAB prevents the native bundle and the mod variant sharing an
    # archive identity. Other mod objects and native program bytes remain exact.
    cab = 'CAB-' + sha(original_mod + b'GHVR_NATIVE_CARD_DEPTH/1')[:32]
    serialized.name = cab
    bundle.files = {cab: serialized}
    output_path.write_bytes(bundle.save(packer='lz4'))
    checked = UnityPy.load(output_path.read_bytes())
    actual = {o.path_id: o for o in checked.objects}
    assert canonical(actual[DERIVED_ID].read_typetree()) == canonical(changed)
    for i, previous in original_objects.items():
        if i != bank.path_id:
            assert actual[i].get_raw_data() == previous, 'An existing mod object changed'
    assert canonical(actual[bank.path_id].read_typetree()) == canonical(bank_tree)
    assert native_path.read_bytes() == raw and input_path.read_bytes() == original_mod
    return {'schema': 'menu-depth-native-card-v1', 'source_bundle_sha256': SOURCE_SHA,
        'source_shader_path_id': SHADER_ID, 'source_shader_sha256': SHADER_SHA,
        'compiled_blob_sha256': PROGRAM_SHA, 'compiled_blob_bytes': len(tree['compressedBlob']),
        'programs_properties_keywords_dependencies_and_other_states_equal': True,
        'state_change': {'pass': 'Default', 'zWrite': [0, 1]},
        'identity_change': {'shader': ['GUI/AbilityCard_Shd', DERIVED_NAME], 'cab': cab},
        'asset': ASSET, 'native_platforms': ['Direct3D11'],
        'alpha_clip': {'existing_keyword': 'UNITY_UI_ALPHACLIP', 'discard_below': .001,
            'fragment_without_rect_sha256': 'f5dc6f7117ea343cae14bc91ba0d071ded4ab4ada8ca87223849429fbf7bbe6d',
            'fragment_with_rect_sha256': '21d65e110d37abb6f15e76a0c89dec8a2394dcfa1fa8776ffd50b5fac64c717a'},
        'input_mod_bundle_sha256': sha(original_mod), 'bundle_sha256': sha(output_path.read_bytes()),
        'bundle_bytes': output_path.stat().st_size,
        'native_d3d11_pixels_verified': False, 'source_immutable': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-bundle', required=True, type=Path)
    parser.add_argument('--rendering-bundle', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    args = parser.parse_args()
    paths = [p.resolve() for p in (args.native_bundle, args.rendering_bundle, args.output, args.report)]
    assert len(set(paths)) == 4 and not args.output.exists() and not args.report.exists(), 'New distinct outputs required'
    result = derive(args.native_bundle, args.rendering_bundle, args.output)
    args.report.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
