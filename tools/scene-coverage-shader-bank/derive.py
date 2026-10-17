#!/usr/bin/env python3
"""Offline OWNED-fixture shader-bank derivation; no game/native admission."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import sys
sys.dont_write_bytecode = True
SOURCE_NAME = 'CoverageBank/Original'
DERIVED_NAME = 'CoverageBank/Derived'
DERIVED_BANK = 'owned-coverage.bundle'
MAXIMUM_BYTES = 4 * 1024 * 1024


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    # Preserve float signs and complete parsed state; no loose numeric equality.
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()


def need(ok, message):
    if not ok:
        raise ValueError(message)


def load(path, expected=None):
    import UnityPy
    path = Path(path)
    need(path.is_file() and not path.is_symlink(), 'regular bounded input required')
    raw = path.read_bytes()
    need(0 < len(raw) <= MAXIMUM_BYTES, 'bundle extent refused')
    if expected is not None:
        need(len(expected) == 64 and digest(raw) == expected, 'source bundle pin mismatch')
    env = UnityPy.load(raw)
    need(len(env.files) == 1, 'one owned bundle required')
    bundle = next(iter(env.files.values()))
    need(bundle.signature == 'UnityFS' and len(bundle.files) == 1, 'one closed CAB required')
    cab, serialized = next(iter(bundle.files.items()))
    need(cab.startswith('CAB-') and len(env.assets) == 1 and not serialized.externals, 'external dependency refused')
    objects = list(serialized.objects.values())
    need(len(objects) == 2 and sorted(o.type.name for o in objects) == ['AssetBundle', 'Shader'], 'only fixture shader and bundle allowed')
    shader = next(o for o in objects if o.type.name == 'Shader')
    bank = next(o for o in objects if o.type.name == 'AssetBundle')
    return raw, bundle, serialized, cab, shader, bank


def tree(source):
    shader, bank = source[-2:]
    return shader.read_typetree(), bank.read_typetree()


def no_dependencies(shader, bank):
    parsed = shader['m_ParsedForm']
    need(not shader['m_Dependencies'] and not shader['m_NonModifiableTextures'] and
         not parsed['m_Dependencies'] and not bank['m_Dependencies'], 'shader/bank dependency refused')
    need(not bank['m_IsStreamedSceneAssetBundle'] and not bank['m_SceneHashes'], 'scene bundle refused')
    need(shader['platforms'] == [18] and bool(shader['compressedBlob']), 'owned Vulkan compiled program required')


def literal(state, key, wanted):
    value = state[key]
    need(value == {'val': float(wanted), 'name': '<noninit>'}, 'literal pass state refused: ' + key)


def source_contract(shader, bank):
    no_dependencies(shader, bank)
    parsed = shader['m_ParsedForm']
    need(parsed['m_Name'] == SOURCE_NAME and bank['m_Name'] == 'owned-original.bundle' and
         bank['m_AssetBundleName'] == 'owned-original.bundle', 'owned fixture identity required')
    need(len(parsed['m_SubShaders']) == 1, 'single owned subshader required')
    passes = parsed['m_SubShaders'][0]['m_Passes']
    need(len(passes) == 1 and passes[0]['m_Type'] == 0, 'single original direct pass required')
    state = passes[0]['m_State']
    need(state['m_Name'] == 'OWNED_OVER' and not state['rtSeparateBlend'], 'owned over state required')
    for key, wanted in [('srcBlend', 5), ('destBlend', 10), ('srcBlendAlpha', 5),
                        ('destBlendAlpha', 10), ('blendOp', 0), ('blendOpAlpha', 0), ('colMask', 15)]:
        literal(state['rtBlend0'], key, wanted)
    for key, wanted in [('zTest', 8), ('zWrite', 0), ('culling', 0)]:
        literal(state, key, wanted)


def expected_trees(shader, bank):
    changed = copy.deepcopy(shader)
    changed['m_ParsedForm']['m_Name'] = DERIVED_NAME
    blend = changed['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['m_State']['rtBlend0']
    blend['srcBlendAlpha']['val'] = 1.0
    # Destination alpha was already OneMinusSrcAlpha and stays byte-semantically identical.
    renamed = copy.deepcopy(bank)
    renamed['m_Name'] = renamed['m_AssetBundleName'] = DERIVED_BANK
    return changed, renamed


def verify(source_path, derived_path, source_sha256):
    source = load(source_path, source_sha256)
    original, original_bank = tree(source)
    source_contract(original, original_bank)
    derived = load(derived_path)
    changed, changed_bank = tree(derived)
    no_dependencies(changed, changed_bank)
    need(derived[3] == 'CAB-' + digest(source[0] + b'GHVR_OWNED_COVERAGE_BANK/1')[:32] and
         derived[3] != source[3], 'derived CAB identity refused')
    need(source[-2].path_id == derived[-2].path_id and source[-1].path_id == derived[-1].path_id,
         'local object addresses changed')
    expected, bank = expected_trees(original, original_bank)
    need(canonical(changed) == canonical(expected), 'compiled program or unapproved shader state changed')
    need(canonical(changed_bank) == canonical(bank), 'unapproved bundle/dependency metadata changed')
    need(source[2].unity_version == derived[2].unity_version and
         source[2].target_platform == derived[2].target_platform, 'serialized version/platform changed')
    blob = bytes(original['compressedBlob'])
    return {'schema': 'owned-shader-bank-proof-v1', 'source_sha256': digest(source[0]),
            'derived_sha256': digest(derived[0]), 'source_cab': source[3], 'derived_cab': derived[3],
            'compiled_blob_sha256': digest(blob), 'compiled_blob_bytes': len(blob),
            'complete_programs_and_other_shader_state_equal': True,
            'only_alpha_factor': {'source': 5, 'derived': 1, 'destination': 10},
            'source_immutable': Path(source_path).read_bytes() == source[0],
            'game_shader_admitted': False, 'runtime_adoption': False, 'windows_d3d11_proven': False}


def derive(source_path, derived_path, source_sha256):
    source_path, derived_path = Path(source_path), Path(derived_path)
    need(source_path.resolve() != derived_path.resolve() and not derived_path.exists() and not derived_path.is_symlink(),
         'new output required; never overwrite source or existing output')
    source = load(source_path, source_sha256)
    original, original_bank = tree(source)
    source_contract(original, original_bank)
    changed, bank = expected_trees(original, original_bank)
    source[-2].save_typetree(changed)
    source[-1].save_typetree(bank)
    cab = 'CAB-' + digest(source[0] + b'GHVR_OWNED_COVERAGE_BANK/1')[:32]
    source[2].name = cab
    source[1].files = {cab: source[2]}
    raw = source[1].save(packer='none')
    need(len(raw) <= MAXIMUM_BYTES, 'derived output extent refused')
    with derived_path.open('xb') as stream:
        stream.write(raw)
    return verify(source_path, derived_path, source_sha256)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--source-sha256', required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    result = derive(args.source, args.output, args.source_sha256)
    args.report.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, sort_keys=True))


if __name__ == '__main__':
    main()
