#!/usr/bin/env python3
"""Verify every packaged environment stream against the committed immutable asset manifest."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import struct
import subprocess
import shutil
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def geometry(data):
    """Validate the complete bounded decoder format, not just its magic/hash."""
    assert data[:5] == b'GHEM1', 'stream decoder version mismatch'
    offset = 5
    def integer():
        nonlocal offset
        value = struct.unpack_from('<i', data, offset)[0]; offset += 4
        return value
    length = integer(); assert 0 <= length <= 1024, 'mesh name bound'
    name = data[offset:offset + length].decode('utf-8'); offset += length
    bounds = struct.unpack_from('<6f', data, offset); offset += 24
    assert all(math.isfinite(value) for value in bounds) and all(value >= 0 for value in bounds[3:]), 'finite native bounds'
    channels = []
    for index in range(12):
        count, width = integer(), integer()
        assert 0 <= count <= 100000 and 2 <= width <= 4, 'vertex channel bound'
        assert index == 0 or count in (0, channels[0][0]), 'complete original vertex attribute slots'
        if index == 0: assert count >= 3 and width == 3, 'three-dimensional positions'
        end = offset + count * width * 4
        assert end <= len(data), 'truncated vertex channel'
        values = data[offset:end]
        assert all(math.isfinite(value[0]) for value in struct.iter_unpack('<f', values)), 'finite vertex channel'
        channels.append((count, width, values)); offset = end
    submeshes = integer(); assert 1 <= submeshes <= 32, 'native submesh bound'
    indices = []
    for _ in range(submeshes):
        count = integer(); assert 0 <= count <= 3000000 and count % 3 == 0, 'triangle index bound'
        end = offset + count * 4; assert end <= len(data), 'truncated triangle indices'
        values = struct.unpack_from('<' + 'i' * count, data, offset)
        assert all(0 <= index < channels[0][0] for index in values), 'triangle index range'
        indices.append(values); offset = end
    assert offset == len(data), 'unexpected geometry trailing bytes'
    return name, bounds, channels, indices


def check_original(entry, data):
    name, bounds, channels, indices = geometry(data)
    signature = entry['signature']
    assert name == signature['name'] and bounds == tuple(signature['bounds']), 'exact native name/bounds'
    assert channels[0][0] == signature['vertices'], 'exact native vertex count'
    assert [len(submesh) for submesh in indices] == signature['indices'], 'exact native original indices'
    return name, bounds, channels, indices


def check_detail(original, reduced):
    name, bounds, channels, indices = reduced
    original_name, original_bounds, original_channels, original_indices = original
    assert name == original_name and bounds == original_bounds, 'detail keeps native identity/bounds'
    assert channels[0][:2] == original_channels[0][:2], 'same-index detail retains every original vertex slot'
    assert channels[1:] == original_channels[1:], 'detail retains exact original normals/tangents/color/UV channels'
    assert len(indices) == len(original_indices), 'detail keeps every native material submesh'
    assert 0 < sum(map(len, indices)) < sum(map(len, original_indices)), 'detail removes actual triangles'
    for source, target in zip(original_indices, indices):
        original_triangles = {tuple(source[index:index + 3]) for index in range(0, len(source), 3)}
        assert all(tuple(target[index:index + 3]) in original_triangles for index in range(0, len(target), 3)), 'detail triangles retain native indices and winding'
    source_positions = tuple(struct.iter_unpack('<fff', original_channels[0][2]))
    positions = tuple(struct.iter_unpack('<fff', channels[0][2]))
    assert source_positions != positions, 'detail changes actual three-dimensional positions'
    for axis in range(3):
        lo, hi = min(value[axis] for value in source_positions), max(value[axis] for value in source_positions)
        assert all(lo <= value[axis] <= hi for value in positions), 'detail remains inside native geometry bounds'
        if hi > lo: assert max(value[axis] for value in positions) > min(value[axis] for value in positions), 'detail never flattens a native geometric axis'


def unity_load(root, output):
    """Load the actual bank and run every stream through the production decoder."""
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output.resolve()))
    project = run / 'unity'
    editor = project / 'Assets/Editor'; editor.mkdir(parents=True)
    settings = project / 'ProjectSettings'; settings.mkdir()
    (settings / 'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    packages = project / 'Packages'; packages.mkdir()
    (packages / 'manifest.json').write_text('{"dependencies":{}}\n')
    shutil.copyfile(root / 'scripts/environment-bank-runtime/Editor/LoadBank.cs', editor / 'LoadBank.cs')
    decoder = root / 'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentMeshStream.cs'
    text = decoder.read_text()
    assert text.count('namespace GloomhavenVR.Core;') == 1, 'production decoder binding drift'
    (editor / decoder.name).write_text(text.replace('namespace GloomhavenVR.Core;', 'namespace GloomhavenVR.Core\n{') + '\n}\n')
    (run / 'source-hashes.json').write_text(json.dumps({'sourceRoot': str(root), 'decoder': hashlib.sha256(decoder.read_bytes()).hexdigest(),
        'loader': hashlib.sha256((editor / 'LoadBank.cs').read_bytes()).hexdigest(),
        'bank': hashlib.sha256((root / 'prebuilt/ghvr-environment.bundle').read_bytes()).hexdigest()}, indent=2) + '\n')
    unity = os.environ.get('UNITY_PATH', str(Path.home() / 'unity-2021.3.5/Editor/Unity'))
    command = [unity, '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'LoadEnvironmentBank.Run',
        '-environmentBank', str(root / 'prebuilt/ghvr-environment.bundle'), '-logFile', str(run / 'unity.log')]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    log = (run / 'unity.log').read_text(errors='replace')
    assert result.returncode == 0 and 'PASS packaged environment bank load:' in log, 'actual Unity bank/production decoder failed; see ' + str(run / 'unity.log')
    for line in log.splitlines():
        if line.startswith(('PASS packaged environment bank load:', 'Shader availability only:')): print(line)
    print('Unity load evidence: ' + str(run))

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--native-sources', type=Path, help='Fresh read-only export directory for independent original/provenance verification')
    parser.add_argument('--unity-load', action='store_true', help='Decode every actual packaged stream in a small game-exact Unity project')
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/environment-bank-load')
    args = parser.parse_args()
    root = args.source_root.resolve()
    python = Path.home() / 'unitypy-venv/bin/python'
    if sys.executable != str(python):
        command = [str(python), str(Path(__file__).resolve()), '--source-root', str(root)]
        if args.native_sources: command += ['--native-sources', str(args.native_sources.resolve())]
        if args.unity_load: command += ['--unity-load', '--output-dir', str(args.output_dir.resolve())]
        subprocess.run(command, check=True)
        return
    import UnityPy
    prepared = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    manifest = json.loads((prepared / 'index.json').read_text())
    preparation = json.loads((root / 'tools/environment-mesh/manifest.json').read_text())
    native = None
    if args.native_sources:
        native_receipt = json.loads((args.native_sources / 'sources.json').read_text())
        assert native_receipt['format'] == 1 and native_receipt['meshes'], 'independent original source census'
        assert native_receipt['ambiguousRejected'] == preparation['ambiguousRejected'], 'same native ambiguity exclusions'
        native = {entry['key']: entry for entry in native_receipt['meshes']}
        assert len(native) == len(native_receipt['meshes']) == len(manifest['entries']), 'complete independent original census'
    receipt = json.loads((root / 'tools/environment-mesh/bank.json').read_text())
    bank = root / 'prebuilt' / receipt['file']
    assert hashlib.file_digest(bank.open('rb'), 'sha256').hexdigest() == receipt['sha256'], 'packaged bank hash drift'
    assert bank.stat().st_size == receipt['bytes'] < 100 * 1024 * 1024, 'bank size bound'
    env = UnityPy.load(str(bank))
    texts = {}
    shaders = []
    for obj in env.objects:
        if obj.type.name == 'TextAsset':
            asset = obj.read()
            data = asset.m_Script
            if isinstance(data, str): data = data.encode('utf-8', errors='surrogateescape')
            assert asset.m_Name not in texts, 'duplicate packed stream identity'
            texts[asset.m_Name] = data
        elif obj.type.name == 'Shader': shaders.append(obj.read().m_ParsedForm.m_Name)
    assert shaders.count('GloomhavenVR/ScenarioCheapTerrain') == 1, 'optional shader packed into independent bank'
    assert env.container['assets/bundle/environments/scenariocheapterrain.shader'].read().m_ParsedForm.m_Name == 'GloomhavenVR/ScenarioCheapTerrain', 'declared shader asset path'
    assert texts.pop('index') == (prepared / 'index.json').read_bytes(), 'runtime index differs from prepared index'
    checked = 0; identities = set(); files = set(); detail_triangles = {0: [0, 0], 50: [0, 0]}
    for entry in manifest['entries']:
        identity = json.dumps(entry['signature'], sort_keys=True, separators=(',', ':'))
        assert entry['key'] == hashlib.sha256(identity.encode()).hexdigest()[:24], 'exact original metadata identity'
        assert identity not in identities and entry['key'] not in preparation['ambiguousRejected'], 'unambiguous native identity'
        identities.add(identity)
        tiers = [variant['tier'] for variant in entry['variants']]
        assert len(set(tiers)) == len(tiers) and 100 in tiers and set(tiers) <= {0, 50, 100}, 'unique tiers with exact fallback'
        original_variant = next(variant for variant in entry['variants'] if variant['tier'] == 100)
        original_data = (prepared / original_variant['file']).read_bytes()
        original = check_original(entry, original_data)
        if native is not None:
            source = native.pop(entry['key'])
            assert source['signature'] == entry['signature'] and source['sources'] == entry['sources'], 'exact independently extracted native provenance'
            assert original_data == (args.native_sources / source['file']).read_bytes(), 'tier100 is byte-exact fresh native geometry'
        for variant in entry['variants']:
            assert variant['file'] == entry['key'] + '-' + str(variant['tier']) + '.bytes' and variant['file'] not in files, 'unique bounded stream identity'
            assert re.fullmatch('[0-9a-f]{64}', variant['sha256']), 'full stream hash'
            files.add(variant['file'])
            data = texts.pop(Path(variant['file']).stem)
            assert data == (prepared / variant['file']).read_bytes(), 'packaged geometry differs from original preparation'
            assert hashlib.sha256(data).hexdigest() == variant['sha256'], 'runtime geometry hash mismatch'
            if variant['tier'] != 100:
                reduced = geometry(data); check_detail(original, reduced)
                counts = detail_triangles[variant['tier']]
                counts[0] += sum(map(len, original[3])) // 3; counts[1] += sum(map(len, reduced[3])) // 3
            checked += 1
    assert not texts, 'unindexed packaged content'
    assert native is None or not native, 'native originals omitted from the package'
    assert len(identities) == preparation['originals'] and checked == preparation['variants'], 'prepared census drift'
    assert checked + 1 == receipt['textAssets'], 'packaged asset count drift'
    print(f'PASS environment bank: {len(manifest["entries"])} exact originals, {checked} immutable streams, shader and package hashes verified')
    print('PASS bounded geometry: same-index 3D detail, original attribute channels/submeshes; triangles ' + str(detail_triangles))
    if args.native_sources: print('PASS independent native source census: every tier100 byte and original bundle/path identity matches')
    if args.unity_load: unity_load(root, args.output_dir)

if __name__ == '__main__': main()
