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
