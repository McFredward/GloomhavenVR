#!/usr/bin/env python3
"""Check the shipped native card shader against the immutable original, without a GPU.

This is independent of tools/menu-depth-native-card/derive.py: compare every
serialized tree leaf, permitting only the parsed shader name and Default.ZWrite.
Private, valid bundle mutants repin their transport hashes before verification,
so a stale checksum cannot masquerade as a semantic negative control. No game
bundle is copied or written, and this check does not certify native D3D11 pixels.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import re
import signal
import struct
import subprocess
import sys
import tempfile
import time

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve()
CHECKOUT = HERE.parents[2]
ASSETS = Path('src/GloomhavenVR/Assets/MenuDepth/Rendering')
NATIVE = Path('ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle')
SOURCE_BUNDLE_SHA = '15b6ac4f91858306e3c6136efbdcf89c6118486dd2191eb27c3c1e9b92018102'
SOURCE_SHADER_ID = 7314317428712670795
SOURCE_SHADER_SHA = 'fbd49fffa5bb88c6a448a659d87f41b6d2507f37ddd5def25afde34c00f9bb49'
PROGRAM_SHA = 'c516fa367bcb4e9a9190c490386e31cf380bff6a3c793bfe60896ea9b3c2d4ef'
SHADER_ID = -7314317428712670795
SHADER_NAME = 'GloomhavenVR/MenuDepthNativeCard'
ASSET_KEY = 'assets/menudepth/menudepthnativecard.shader'
NAME_PATH = ('m_ParsedForm', 'm_Name')
STATE_PATH = ('m_ParsedForm', 'm_SubShaders', 0, 'm_Passes', 0, 'm_State')
WRITE_PATH = STATE_PATH + ('zWrite', 'val')


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def canonical(value) -> str:
    # JSON retains signed floating zero and rejects non-finite values. Unlike
    # Python's ordinary equality it also distinguishes true from the number one.
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False)


def save(path: Path, value) -> None:
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')


def at(tree, path):
    for key in path:
        tree = tree[key]
    return tree


def differences(left, right, path=()):
    """Enumerate the complete tree, including additions, lengths and signed zero."""
    if isinstance(left, dict) and isinstance(right, dict):
        for key in sorted(left.keys() | right.keys()):
            if key not in left or key not in right:
                yield path + (key,)
            else:
                yield from differences(left[key], right[key], path + (key,))
    elif isinstance(left, (list, tuple)) and isinstance(right, (list, tuple)):
        if len(left) != len(right):
            yield path + ('length',)
        for index, (a, b) in enumerate(zip(left, right)):
            yield from differences(a, b, path + (index,))
    elif canonical(left) != canonical(right):
        yield path


class Refusal(ValueError):
    def __init__(self, code, message, paths=()):
        super().__init__(code + ': ' + message)
        self.code, self.paths = code, list(paths)


class Checks:
    def __init__(self):
        self.labels = []

    def check(self, ok, code, message, paths=()):
        if not ok:
            raise Refusal(code, message, paths)
        self.labels.append(message)


def load_bank(UnityPy, data, checks):
    env = UnityPy.load(data)
    checks.check(len(env.files) == 1 and len(env.assets) == 1, 'bundle-layout', 'One serialized mod asset file')
    bundle, asset = next(iter(env.files.values())), env.assets[0]
    banks = [o for o in env.objects if o.type.name == 'AssetBundle']
    checks.check(len(banks) == 1, 'bundle-layout', 'One AssetBundle metadata object')
    return env, bundle, asset, banks[0]


def fragment_pins(native):
    """Address the original alpha-clip DXBC variants from its real program table."""
    from UnityPy.export.ShaderConverter import ShaderSubProgram
    from UnityPy.helpers.CompressionHelper import decompress_lz4
    from UnityPy.streams import EndianBinaryReader
    blob = bytes(native['compressedBlob'])
    segments = [decompress_lz4(blob[o:o + c], d) if c != d else blob[o:o + c]
                for o, c, d in zip(native['offsets'][0], native['compressedLengths'][0], native['decompressedLengths'][0])]
    names, result = native['m_ParsedForm']['m_KeywordNames'], {}
    for sub in at(native, STATE_PATH[:-1])['progFragment']['m_SubPrograms']:
        keywords = [names[i] for i in sub['m_KeywordIndices']]
        if sub['m_ShaderHardwareTier'] != 0 or 'UNITY_UI_ALPHACLIP' not in keywords:
            continue
        offset, length, segment = struct.unpack_from('<III', segments[0], 4 + sub['m_BlobIndex'] * 12)
        if not (0 <= segment < len(segments) and 0 <= offset < offset + length <= len(segments[segment])):
            raise ValueError('Native compiled program escapes its original segment')
        reader = EndianBinaryReader(segments[segment], endian='<')
        reader.Position = offset
        code = bytes(ShaderSubProgram(reader).m_ProgramCode)
        start = code.find(b'DXBC')
        if start < 0 or start + 28 > len(code):
            raise ValueError('Native fragment has no complete DXBC header')
        size = struct.unpack_from('<I', code, start + 24)[0]
        if not (32 <= size <= len(code) - start):
            raise ValueError('Native DXBC length escapes its program')
        key = 'fragment_with_rect_sha256' if 'UNITY_UI_CLIP_RECT' in keywords else 'fragment_without_rect_sha256'
        result[key] = sha(code[start:start + size])
    if len(result) != 2:
        raise ValueError('Both original alpha-clip fragment variants are required')
    return result


def verify(UnityPy, native, native_cab, fragments, data, proof, provenance):
    checks = Checks()
    _, bundle, asset, bank_reader = load_bank(UnityPy, data, checks)
    checks.check(not asset.externals, 'external-dependency', 'No serialized external dependencies')
    bank = bank_reader.read_typetree()
    checks.check(not bank['m_Dependencies'] and not bank['m_IsStreamedSceneAssetBundle'],
                 'bank-dependency', 'No bundle dependencies or streamed scene')
    entries = [row for row in bank['m_Container'] if row[0] == ASSET_KEY]
    checks.check(len(entries) == 1, 'asset-key', 'Exactly one original-derived asset key')
    pointer = entries[0][1]
    checks.check(pointer['asset'] == {'m_FileID': 0, 'm_PathID': SHADER_ID},
                 'asset-identity', 'The native-card asset addresses its own local shader')
    shader = asset.objects.get(SHADER_ID)
    checks.check(shader is not None and shader.type.name == 'Shader',
                 'asset-shader', 'The addressed shader really exists')
    checks.check(pointer['preloadSize'] == 1 and 0 <= pointer['preloadIndex'] < len(bank['m_PreloadTable'])
                 and bank['m_PreloadTable'][pointer['preloadIndex']] == pointer['asset'],
                 'asset-preload', 'The asset preload resolves the same local shader')
    cab = asset.name
    checks.check(re.fullmatch(r'CAB-[0-9a-f]{32}', cab) is not None and cab != native_cab
                 and list(bundle.files) == [cab] and proof['identity_change']['cab'] == cab,
                 'cab-identity', 'Serialized CAB, archive entry and report agree without a native collision')
    tree = shader.read_typetree()
    checks.check(at(tree, NAME_PATH) == SHADER_NAME, 'shader-name', 'The derived shader has its exact runtime name')
    checks.check(at(native, STATE_PATH)['zTest'] == {'val': 0.0, 'name': 'unity_GUIZTestMode'}
                 and canonical(at(tree, STATE_PATH)['zTest']) == canonical(at(native, STATE_PATH)['zTest']),
                 'dynamic-ztest', 'Native property-controlled ZTest remains dynamic and exact')
    changed = list(differences(native, tree))
    unexpected = [p for p in changed if p not in (NAME_PATH, WRITE_PATH)]
    checks.check(not unexpected, 'shader-tree', 'All programs, properties, keywords, dependencies and other state match', unexpected)
    checks.check(set(changed) == {NAME_PATH, WRITE_PATH}
                 and at(native, NAME_PATH) == 'GUI/AbilityCard_Shd'
                 and at(native, WRITE_PATH) == 0.0 and at(tree, WRITE_PATH) == 1.0,
                 'allowed-delta', 'Exactly the parsed name and literal Default.ZWrite differ')
    checks.check(tree['platforms'] == [4] and sha(bytes(tree['compressedBlob'])) == PROGRAM_SHA,
                 'compiled-programs', 'The complete native Direct3D11 compressed program blob is unchanged')
    checks.check(proof['schema'] == 'menu-depth-native-card-v1'
                 and proof['source_bundle_sha256'] == SOURCE_BUNDLE_SHA
                 and proof['source_shader_path_id'] == SOURCE_SHADER_ID
                 and proof['source_shader_sha256'] == SOURCE_SHADER_SHA,
                 'native-provenance', 'Independent original bundle/object pins agree with the report')
    checks.check(proof['compiled_blob_sha256'] == PROGRAM_SHA
                 and proof['compiled_blob_bytes'] == len(bytes(native['compressedBlob']))
                 and proof['native_platforms'] == ['Direct3D11'],
                 'program-provenance', 'Program size, digest and actual platform agree with the report')
    checks.check(proof['programs_properties_keywords_dependencies_and_other_states_equal'] is True
                 and proof['source_immutable'] is True
                 and proof['state_change'] == {'pass': 'Default', 'zWrite': [0, 1]}
                 and at(tree, STATE_PATH)['m_Name'] == 'Default'
                 and proof['identity_change']['shader'] == ['GUI/AbilityCard_Shd', SHADER_NAME]
                 and proof['asset'] == ASSET_KEY,
                 'delta-provenance', 'The report describes only the independently observed changes')
    checks.check(proof['alpha_clip']['existing_keyword'] == 'UNITY_UI_ALPHACLIP'
                 and proof['alpha_clip']['discard_below'] == .001
                 and 'UNITY_UI_ALPHACLIP' in tree['m_ParsedForm']['m_KeywordNames'],
                 'alpha-keyword', 'The selected alpha-clip keyword exists in the unchanged native program contract')
    checks.check(all(proof['alpha_clip'][key] == pin for key, pin in fragments.items()),
                 'alpha-provenance', 'Both reported alpha-clip DXBC digests match independently addressed native fragments')
    checks.check(proof['bundle_sha256'] == provenance['bundle_sha256'] == sha(data)
                 and proof['bundle_bytes'] == provenance['bundle_bytes'] == len(data),
                 'bundle-pin', 'Both provenance files bind the actual delivered bundle bytes')
    checks.check(provenance['unity_version'] == str(asset.unity_version) == '2021.3.5f1'
                 and provenance['target'] == 'StandaloneWindows64'
                 and provenance['native_card_shader']['proof'] == (ASSETS / 'NativeCard.provenance.json').as_posix()
                 and provenance['native_card_shader']['native_program_graphics_apis'] == ['Direct3D11']
                 and type(proof['native_d3d11_pixels_verified']) is bool
                 and provenance['native_card_shader']['d3d11_pixels_verified'] is proof['native_d3d11_pixels_verified'],
                 'platform-provenance', 'Platform and independently limited pixel-proof metadata are consistent')
    return {'checks': len(checks.labels), 'labels': checks.labels, 'allowed_tree_differences': changed,
            'native_shader_sha256': SOURCE_SHADER_SHA, 'derived_shader_sha256': sha(shader.get_raw_data()),
            'compiled_program_sha256': PROGRAM_SHA, 'bundle_sha256': sha(data), 'cab': cab}


def controls(UnityPy, native, native_cab, fragments, original, proof, provenance, folder):
    rows = []
    # Every mutation is serialized and reloaded through the actual file parser.
    # Rebind only transport hashes; native identity/program oracles stay fixed.
    cases = [
        ('program-bit', 'shader-tree', ('compressedBlob',)),
        ('extra-render-state', 'shader-tree', STATE_PATH + ('culling',)),
        ('dynamic-ztest', 'dynamic-ztest', ()),
        ('keyword', 'shader-tree', ('m_ParsedForm', 'm_KeywordNames')),
        ('property', 'shader-tree', ('m_ParsedForm', 'm_PropInfo')),
        ('dependency', 'shader-tree', ('m_Dependencies',)),
        ('shader-identity', 'shader-name', ()),
        ('cab-identity', 'cab-identity', ()),
        ('missing-shader', 'asset-shader', ()),
        ('provenance-digest', 'bundle-pin', ()),
        ('fragment-provenance', 'alpha-provenance', ()),
    ]
    for name, expected_code, expected_path in cases:
        _, bundle, asset, _ = load_bank(UnityPy, original, Checks())
        reader = asset.objects[SHADER_ID]
        tree = reader.read_typetree()
        if name == 'program-bit':
            tree['compressedBlob'][len(tree['compressedBlob']) // 2] ^= 1
        elif name == 'extra-render-state':
            at(tree, STATE_PATH)['culling']['val'] = 2.0
        elif name == 'dynamic-ztest':
            at(tree, STATE_PATH)['zTest'] = {'val': 4.0, 'name': '<noninit>'}
        elif name == 'keyword':
            tree['m_ParsedForm']['m_KeywordNames'].append('UNAPPROVED_CARD_VARIANT')
        elif name == 'property':
            tree['m_ParsedForm']['m_PropInfo']['m_Props'][0]['m_DefValue[0]'] = .25
        elif name == 'dependency':
            tree['m_Dependencies'].append({'m_FileID': 0, 'm_PathID': 1})
        elif name == 'shader-identity':
            tree['m_ParsedForm']['m_Name'] = 'GloomhavenVR/OtherCard'
        elif name == 'cab-identity':
            asset.name = 'CAB-' + '0' * 32
            bundle.files = {asset.name: asset}
        reader.save_typetree(tree)
        if name == 'missing-shader':
            del asset.objects[SHADER_ID]
        data = bundle.save(packer='lz4')
        candidate, metadata = copy.deepcopy(proof), copy.deepcopy(provenance)
        for record in (candidate, metadata):
            record['bundle_sha256'], record['bundle_bytes'] = sha(data), len(data)
        if name == 'provenance-digest':
            candidate['bundle_sha256'] = '0' * 64
        elif name == 'fragment-provenance':
            candidate['alpha_clip']['fragment_with_rect_sha256'] = '0' * 64
        path = folder / name
        path.mkdir()
        (path / 'menu-depth.bundle').write_bytes(data)
        save(path / 'NativeCard.provenance.json', candidate)
        save(path / 'provenance.json', metadata)
        try:
            verify(UnityPy, native, native_cab, fragments, data, candidate, metadata)
        except Refusal as error:
            if error.code != expected_code or (expected_path and not any(
                    tuple(p[:len(expected_path)]) == expected_path for p in error.paths)):
                raise ValueError('Control failed for an unrelated reason: ' + name + ': ' + str(error)) from error
            rows.append({'name': name, 'expected_code': expected_code, 'refusal': str(error),
                         'changed_paths': error.paths, 'bundle_sha256': sha(data),
                         'proof_sha256': sha((path / 'NativeCard.provenance.json').read_bytes()),
                         'provenance_sha256': sha((path / 'provenance.json').read_bytes())})
        else:
            raise ValueError('Corrupted native-card derivative was accepted: ' + name)
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=CHECKOUT, help='Read-only checkout containing the delivered assets')
    parser.add_argument('--output', type=Path, default=CHECKOUT / '.planning/debug/menu-depth-native-card')
    args = parser.parse_args()
    root = args.root.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    log = run / 'completion.log'
    log.write_text('Evidence: ' + str(run) + '\n')
    sys.path.insert(0, str(CHECKOUT / 'scripts'))
    from town_run_retention import cleanup_completed_run
    def interrupted(signum, _frame):
        raise InterruptedError('Interrupted by signal ' + str(signum))
    previous_term = signal.signal(signal.SIGTERM, interrupted)
    started, status, pins = time.monotonic(), 1, {}
    report = {'passed': False, 'scope': 'COMPLETE_NATIVE_SHADER_ASSET_BYTES_ONLY',
              'gpu_executed': False, 'native_d3d11_pixels_verified': False,
              'source_root': str(root), 'subprocesses_reaped': True}
    try:
        import UnityPy
        report['toolchain'] = {'python': sys.version, 'executable': str(Path(sys.executable).resolve()),
                               'UnityPy': importlib.metadata.version('UnityPy')}
        sources, mutants = run / 'sources', run / 'mutants'
        sources.mkdir(); mutants.mkdir()
        paths = [HERE, CHECKOUT / 'scripts/menu-depth-native-card-tests.sh',
                 CHECKOUT / 'scripts/town_run_retention.py', root / NATIVE,
                 root / ASSETS / 'menu-depth.bundle', root / ASSETS / 'NativeCard.provenance.json',
                 root / ASSETS / 'provenance.json', Path(UnityPy.__file__)]
        provenance = json.loads(paths[6].read_text())
        paths.extend(root / row['path'] for row in provenance['sources'])
        for path in dict.fromkeys(paths):
            raw = path.read_bytes()
            pins[str(path)] = {'sha256': sha(raw), 'bytes': len(raw)}
            if path != root / NATIVE:
                (sources / (path.name + '-' + sha(raw)[:12])).write_bytes(raw)
        save(run / 'source-pins.json', pins)
        git = subprocess.Popen(['git', '-C', str(root), 'rev-parse', 'HEAD'], stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, text=True, start_new_session=True)
        try:
            output, error = git.communicate(timeout=10)
        except BaseException:
            if git.poll() is None:
                os.killpg(git.pid, signal.SIGKILL)
            git.communicate()
            raise
        if git.returncode != 0:
            raise ValueError('Cannot pin source revision: ' + error)
        report['source_commit'] = output.strip()
        report['reaped_children'] = [{'pid': git.pid, 'exit_code': git.returncode}]
        native_path = root / NATIVE
        if pins[str(native_path)]['sha256'] != SOURCE_BUNDLE_SHA:
            raise ValueError('The read-only original GUI bundle has an unreviewed identity')
        source = UnityPy.load(str(native_path))
        native_readers = [o for o in source.objects if o.path_id == SOURCE_SHADER_ID and o.type.name == 'Shader']
        if len(native_readers) != 1 or sha(native_readers[0].get_raw_data()) != SOURCE_SHADER_SHA:
            raise ValueError('The original shader object does not match the independent raw-object pin')
        native_reader = native_readers[0]
        native = native_reader.read_typetree()
        (sources / 'native-card.shader.raw').write_bytes(native_reader.get_raw_data())
        save(sources / 'native-card.shader.tree.json', native)
        proof = json.loads((root / ASSETS / 'NativeCard.provenance.json').read_text())
        data = (root / ASSETS / 'menu-depth.bundle').read_bytes()
        fragments = fragment_pins(native)
        report['positive'] = verify(UnityPy, native, native_reader.assets_file.name, fragments, data, proof, provenance)
        report['controls'] = controls(UnityPy, native, native_reader.assets_file.name, fragments, data, proof, provenance, mutants)
        for row in provenance['sources']:
            if pins[str(root / row['path'])]['sha256'] != row['sha256']:
                raise ValueError('Rendering provenance does not bind its declared source: ' + row['path'])
        report['passed'], status = True, 0
    except (Exception, KeyboardInterrupt) as error:
        report['failure'] = type(error).__name__ + ': ' + str(error)
        print(report['failure'], file=sys.stderr)
    finally:
        changed = [name for name, pin in pins.items()
                   if not Path(name).is_file() or sha(Path(name).read_bytes()) != pin['sha256']]
        report['source_hashes_unchanged'] = not changed
        if changed:
            report['passed'], status, report['changed_inputs'] = False, 1, changed
        report['elapsed_seconds'] = round(time.monotonic() - started, 3)
        report['exit_code'] = status
        save(run / 'results.json', report)
        with log.open('a') as stream:
            stream.write(json.dumps({'passed': report['passed'], 'exit_code': status,
                                     'positive_checks': report.get('positive', {}).get('checks', 0),
                                     'controls': len(report.get('controls', []))}) + '\n')
        receipt = cleanup_completed_run('menu-depth-native-card', status, log, CHECKOUT, process_reaped=True)
        save(run / 'cleanup.json', receipt)
        signal.signal(signal.SIGTERM, previous_term)
        artifacts = [run / name for name in ('results.json', 'source-pins.json', 'completion.log', 'cleanup.json')]
        artifacts.extend((run / 'sources').glob('*'))
        artifacts.extend((run / 'mutants').glob('*/*'))
        save(run / 'artifact-pins.json', {p.relative_to(run).as_posix(): sha(p.read_bytes())
                                         for p in sorted(artifacts) if p.is_file()})
        # Preserve only small assets, source/manifest pins and results after the
        # completion API has inspected this generated run. No project cache exists.
        (run / '.keep').touch()
        print('Evidence: ' + str(run))
        print('Artifact manifest SHA256: ' + sha((run / 'artifact-pins.json').read_bytes()))
        print('Native card assets: ' + str(report.get('positive', {}).get('checks', 0))
              + ' checks; ' + str(len(report.get('controls', []))) + ' causal controls; '
              + str(report['elapsed_seconds']) + ' seconds; exit=' + str(status))
    return status


if __name__ == '__main__':
    raise SystemExit(main())
