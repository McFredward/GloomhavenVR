#!/usr/bin/env python3
"""Inspect actual Android bank/container/program metadata; never claim headset pixels."""
import hashlib
import json
from pathlib import Path
import struct
import sys

import UnityPy

bank, output = map(Path, sys.argv[1:])
data = bank.read_bytes()
checks = 0


def require(value, label):
    global checks
    checks += 1
    assert value, label


require(data[:8] == b'UnityFS\0' and struct.unpack('>I', data[8:12])[0] == 7, 'UnityFS format7')
require(data[18:].split(b'\0', 1)[0] == b'2021.3.5f1', 'Exact Unity2021.3.5f1 bank')
env = UnityPy.load(data)
objects = list(env.objects)
require(sorted(obj.type.name for obj in objects) == ['AssetBundle', 'Shader'], 'Exactly one actual bank container and shader')
require(next(obj.read_typetree()['m_Name'] for obj in objects if obj.type.name == 'AssetBundle') == 'ghvr-quest-loading.bundle', 'Exact runtime bank identity')
require(set(env.container) == {'assets/questloading/questloadingspinner.shader'}, 'Exact runtime asset path')
require(all(int(obj.assets_file.target_platform) == 13 for obj in objects), 'Actual serialized Android target13')
shader = next(obj.read_typetree() for obj in objects if obj.type.name == 'Shader')
require(shader['platforms'] == [18], 'Actual Vulkan-only compiled programs')
require(bool(shader['compressedBlob']) and all(all(value > 0 for value in row) for row in shader['compressedLengths']), 'Nonempty actual compiled bytecode')
parsed = shader['m_ParsedForm']
require(parsed['m_Name'] == 'GloomhavenVR/QuestLoadingSpinner', 'Exact runtime shader identity')
passes = [p for sub in parsed['m_SubShaders'] for p in sub['m_Passes'] if p['m_Type'] == 0]
require(len(passes) == 1, 'Only the owned foreground pass')
state = passes[0]['m_State']
require(state['zTest']['val'] == 8 and state['zWrite']['val'] == 0 and state['culling']['val'] == 0, 'Always depth test; no depth write; two-sided')
require(state['rtBlend0']['srcBlend']['val'] == 1 and state['rtBlend0']['destBlend']['val'] == 10, 'Premultiplied One/OneMinusSrcAlpha blend')
keywords = parsed['m_KeywordNames']
programs = passes[0]['progVertex']['m_SubPrograms']
variants = [{keywords[index] for index in program['m_KeywordIndices']} for program in programs if program['m_GpuProgramType'] == 25]
require(bool(variants), 'Actual SPIR-V programs survive')
require(all(not names for names in variants), 'Current Quest MultiPass uses the retained mono SPIR-V program for each eye')
result = {'assertions': checks, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
          'unityVersion': '2021.3.5f1', 'target': 'Android', 'graphicsApi': 'Vulkan',
          'assetPath': 'assets/questloading/questloadingspinner.shader', 'shaderName': parsed['m_Name'],
          'variants': [sorted(names) for names in variants], 'stereoRenderingPath': 'MultiPass',
          'limits': 'Serialized Android target and compiled Vulkan program metadata only; no Android GPU/XR pixel acceptance.'}
output.write_text(json.dumps(result, indent=2) + '\n')
print('PASS loading shader bank: ' + str(checks) + ' actual metadata assertions')
