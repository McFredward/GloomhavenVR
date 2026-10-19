#!/usr/bin/env python3
"""Exercise destructive preparation guards and malformed actual native streams."""
import importlib.util
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def main():
    spec = importlib.util.spec_from_file_location('environment_bank_check', ROOT / 'scripts/check-environment-bank.py')
    check = importlib.util.module_from_spec(spec); spec.loader.exec_module(check)
    count = 0
    with tempfile.TemporaryDirectory(prefix='ghvr-environment-guards-') as temporary:
        root = Path(temporary)
        prepared = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
        prepared.mkdir(parents=True)
        (prepared / 'index.json').write_bytes(b'original-index-sentinel')
        (prepared / 'retained.bytes').write_bytes(b'original-stream-sentinel')
        expected = {path.name: path.read_bytes() for path in prepared.iterdir()}
        export = ROOT / 'tools/environment-mesh/export-native.py'
        python = Path.home() / 'unitypy-venv/bin/python'
        result = subprocess.run([str(python), str(export), '--source-root', str(root), '--output-dir', str(root / 'missing-native')], capture_output=True, text=True)
        assert result.returncode != 0 and 'source folder is missing' in result.stderr, 'missing game sources must reject preparation'
        assert not (root / 'missing-native').exists(), 'missing references must not write an apparently successful receipt'
        count += 1
        source = root / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64/pcg_databases_assets_assets/pcg'
        source.mkdir(parents=True)
        result = subprocess.run([str(python), str(export), '--source-root', str(root), '--output-dir', str(root / 'unmatched-native'), '--only', 'absent'], capture_output=True, text=True)
        assert result.returncode != 0 and 'No native environment bundles match' in result.stderr, 'unmatched selection must reject preparation'
        count += 1
        work = root / 'generation'; (work / 'native').mkdir(parents=True)
        (work / 'native/sources.json').write_text(json.dumps({'format': 1, 'meshes': [], 'ambiguousRejected': []}))
        result = subprocess.run([sys.executable, str(ROOT / 'scripts/generate-environment-meshes.py'), '--source-root', str(root),
            '--output-dir', str(work), '--skip-extract'], capture_output=True, text=True)
        assert result.returncode != 0 and 'existing prepared assets remain unchanged' in result.stderr, 'empty cached source receipt must reject generation'
        assert {path.name: path.read_bytes() for path in prepared.iterdir()} == expected, 'failed preparation must preserve every existing original/index byte'
        count += 1

    entries = json.loads((ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes/index.json').read_text())['entries']
    # Use an actual previously reduced pillar and a warm prepared directory:
    # a new generator pass must remove its obsolete streams and meta files.
    pillar = next(entry for entry in entries if entry['signature']['name'] == 'EN_CR_Pillar_Thin')
    prepared_root = ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    exact = next(variant for variant in pillar['variants'] if variant['tier'] == 100)
    with tempfile.TemporaryDirectory(prefix='ghvr-retired-pillar-') as temporary:
        root = Path(temporary); native = root / 'generation/native'; native.mkdir(parents=True)
        (root / 'tools/environment-mesh').mkdir(parents=True)
        target = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'; target.mkdir(parents=True)
        data = (prepared_root / exact['file']).read_bytes()
        (native / (pillar['key'] + '.bytes')).write_bytes(data)
        source = {**pillar, 'file': pillar['key'] + '.bytes', 'sha256': exact['sha256']}
        (native / 'sources.json').write_text(json.dumps({'format': 1, 'meshes': [source],
            'ambiguousRejected': [], 'unitypy': 'source-bound-retirement-probe'}))
        stale = [target / (pillar['key'] + '-' + str(tier) + '.bytes') for tier in (50, 0)]
        for path in stale:
            path.write_bytes(data); path.with_name(path.name + '.meta').write_text('obsolete-meta')
        generator = ROOT / 'scripts/generate-environment-meshes.py'
        arguments = ['--source-root', str(root), '--output-dir', str(root / 'generation'), '--skip-extract']
        result = subprocess.run([sys.executable, str(generator), *arguments], capture_output=True, text=True)
        assert result.returncode == 0, 'retired pillar preparation failed: ' + result.stderr
        packed = json.loads((target / 'index.json').read_text())['entries'][0]
        assert [v['tier'] for v in packed['variants']] == [100], 'retired pillar generator must keep only shared exact geometry'
        assert (target / exact['file']).read_bytes() == data, 'retired pillar exact bytes must remain unchanged'
        assert all(not path.exists() and not path.with_name(path.name + '.meta').exists() for path in stale), 'warm preparation must prune obsolete pillar streams and metadata'
        count += 1
        # The original behavior must be observable, rather than this check
        # merely agreeing with a zero-geometry or malformed-source shortcut.
        text = generator.read_text()
        binding = 'tiers = (100,) if "pillar" in source["signature"]["name"].casefold() else (100,50,0)'
        assert text.count(binding) == 1, 'pillar retirement producer binding drift'
        control = root / 'old-generator.py'; control.write_text(text.replace(binding, 'tiers = (100,50,0)'))
        result = subprocess.run([sys.executable, str(control), *arguments], capture_output=True, text=True)
        assert result.returncode == 0, 'old producer control failed for an unrelated reason: ' + result.stderr
        old = json.loads((target / 'index.json').read_text())['entries'][0]
        assert any(v['tier'] != 100 for v in old['variants']), 'pillar retirement probe must reject the former reduced producer'
        count += 1
    entry = entries[0]
    original = ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes' / next(v['file'] for v in entry['variants'] if v['tier'] == 100)
    data = original.read_bytes(); check.check_original(entry, data); count += 1
    position = 5 + 4 + struct.unpack_from('<i', data, 5)[0] + 24 + 8
    nonfinite = bytearray(data); struct.pack_into('<f', nonfinite, position, float('nan'))
    trailing = data + b'unknown trailing content'
    invalid_indices = bytearray(data); struct.pack_into('<i', invalid_indices, len(data) - 4, entry['signature']['vertices'])
    for altered, diagnostic in ((nonfinite, 'finite vertex channel'), (trailing, 'unexpected geometry trailing bytes'),
                                (invalid_indices, 'triangle index range')):
        try: check.geometry(altered)
        except AssertionError as error:
            assert diagnostic in str(error), 'malformed actual native stream failed for an unrelated reason'; count += 1
        else: raise AssertionError('malformed actual native stream escaped complete validation: ' + diagnostic)
    print('PASS environment preparation/geometry guards: ' + str(count) + ' assertions on actual scripts/native streams')


if __name__ == '__main__': main()
