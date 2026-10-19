#!/usr/bin/env python3
"""Verify the actual compiled bank, including its loadable AssetBundle container."""
import hashlib
import json
from pathlib import Path
import struct
import sys

import UnityPy


def verify(bank, manifest):
    data = bank.read_bytes()
    checks = 0
    def require(value, label):
        nonlocal checks
        checks += 1
        assert value, label
    require(data[:8] == b'UnityFS\0' and struct.unpack('>I', data[8:12])[0] == 7, 'UnityFS format 7')
    require(data[18:].split(b'\0', 1)[0] == b'2021.3.5f1', 'Exact game editor version')
    env = UnityPy.load(data)
    types = [obj.type.name for obj in env.objects]
    entries = json.loads(manifest.read_text())['variants']
    names = {'Hidden/GloomhavenVR/SteamLinkSceneCoverage'}
    names.update(entry['variant_shader'] for entry in entries)
    require(types.count('AssetBundle') == 1, 'Actual loadable AssetBundle object is required, not shader objects alone')
    require(types.count('Shader') == len(names) and len(types) == len(names) + 1, 'Only the exact owned shader inventory')
    require(len(env.container) == len(names), 'All runtime paths survive in the actual container')
    observed = set()
    states = {}
    for obj in env.objects:
        if obj.type.name != 'Shader': continue
        shader = obj.read_typetree()
        parsed = shader['m_ParsedForm']
        name = parsed['m_Name']
        require(name in names and name not in observed, 'Unique expected shader identity: ' + name)
        observed.add(name)
        require(shader['platforms'] == [4, 15, 18], 'D3D11, GLCore and Vulkan programs: ' + name)
        require(shader['compressedBlob'] and all(all(v > 0 for v in row) for row in shader['compressedLengths']),
                'Nonempty actual compiled program banks: ' + name)
        passes = [p['m_State'] for sub in parsed['m_SubShaders'] for p in sub['m_Passes'] if p['m_Type'] == 0]
        states[name] = [{'pass': state['m_Name'], 'zwrite': state['zWrite'], 'ztest': state['zTest'],
            'blend': state['rtBlend0']} for state in passes]
        if name == 'Hidden/GloomhavenVR/SteamLinkSceneCoverage':
            require([p['m_Name'] for p in passes] == ['RESET', 'STAMP', 'BACKDROP'], 'Actual depth and owned backdrop coverage pass order')
            require([p['zTest']['val'] for p in passes] == [8, 5, 8], 'Always reset, logical Greater depth and exact depthless backdrop')
            require(all(p['rtBlend0']['colMask']['val'] == 1 and p['zWrite']['val'] == 0 for p in passes),
                    'All coverage draws write alpha only, never depth/RGB')
        if name.endswith(('/Overlay.SceneCoverage', '/BoardLit.SceneCoverage', '/TownNpc.SceneCoverage', '/WaterVR.SceneCoverage')):
            color_passes = [p for p in passes if p['rtBlend0']['colMask']['val'] != 0]
            require(color_passes and all(p['rtBlend0']['srcBlendAlpha']['name'] == '_SceneCoverageSrcAlpha'
                and p['rtBlend0']['destBlendAlpha']['name'] == '_SceneCoverageDstAlpha' for p in color_passes),
                'Dynamic derivatives retain explicit alpha-only state controls: ' + name)
    require(observed == names, 'Complete compiled inventory')
    return {'assertions': checks, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data),
        'objects': types, 'compiled_states': states,
        'scope': 'Actual container and compiled metadata; pixel execution is a separate test.'}


if __name__ == '__main__':
    bank, manifest, output = map(Path, sys.argv[1:])
    result = verify(bank, manifest)
    output.write_text(json.dumps(result, indent=2) + '\n')
    print('PASS actual scene bank: ' + str(result['assertions']) + ' assertions')
