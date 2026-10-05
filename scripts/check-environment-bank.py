#!/usr/bin/env python3
"""Verify every packaged environment stream against the committed immutable asset manifest."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    args = parser.parse_args()
    root = args.source_root.resolve()
    python = Path.home() / 'unitypy-venv/bin/python'
    if sys.executable != str(python):
        subprocess.run([str(python), str(Path(__file__).resolve()), '--source-root', str(root)], check=True)
        return
    import UnityPy
    prepared = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/EnvironmentMeshes'
    manifest = json.loads((prepared / 'index.json').read_text())
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
    checked = 0
    for entry in manifest['entries']:
        assert any(variant['tier'] == 100 for variant in entry['variants']), 'missing exact original fallback'
        for variant in entry['variants']:
            data = texts.pop(Path(variant['file']).stem)
            assert data == (prepared / variant['file']).read_bytes(), 'packaged geometry differs from original preparation'
            assert hashlib.sha256(data).hexdigest() == variant['sha256'], 'runtime geometry hash mismatch'
            assert data[:5] == b'GHEM1', 'stream decoder version mismatch'
            checked += 1
    assert not texts, 'unindexed packaged content'
    assert checked + 1 == receipt['textAssets'], 'packaged asset count drift'
    print(f'PASS environment bank: {len(manifest["entries"])} exact originals, {checked} immutable streams, shader and package hashes verified')

if __name__ == '__main__': main()
