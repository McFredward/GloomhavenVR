#!/usr/bin/env python3
"""Inspect actual shipped binary container and surviving programs, not shader declarations."""
import hashlib
import json
from pathlib import Path
import struct
import sys

import UnityPy

bundle, license_path, output = map(Path, sys.argv[1:])
data = bundle.read_bytes()
checks = 0


def check(condition, label):
    global checks
    checks += 1
    assert condition, label


check(data[:8] == b'UnityFS\0' and struct.unpack('>I', data[8:12])[0] == 7,
      'Game runtime requires UnityFS format7')
check(data[18:].split(b'\0', 1)[0] == b'2021.3.5f1', 'Game-exact editor version')
env = UnityPy.load(str(bundle))
types = [obj.type.name for obj in env.objects]
check(types.count('AssetBundle') == 1 and types.count('Shader') == 1,
      'Actual AssetBundle container and shader must survive build module stripping')
check(set(env.container) == {'assets/stereovideo/stereovideoui.shader', 'assets/stereovideo/unity-license.txt'},
      'Runtime load paths and embedded copyright notice')
shader = next(obj.read_typetree() for obj in env.objects if obj.type.name == 'Shader')
check(shader['platforms'] == [4, 15, 18], 'Actual D3D11/OpenGLCore/Vulkan program banks')
check(all(all(value > 0 for value in row) for row in shader['compressedLengths']),
      'All shader program banks carry nonempty compressed bytecode')
parsed = shader['m_ParsedForm']
check(parsed['m_Name'] == 'GloomhavenVR/StereoVideoUI', 'Runtime shader name')
keywords = parsed['m_KeywordNames']
programs = parsed['m_SubShaders'][0]['m_Passes'][0]
expected = {frozenset(), frozenset({'UNITY_UI_CLIP_RECT'}),
            frozenset({'UNITY_UI_ALPHACLIP'}), frozenset({'UNITY_UI_CLIP_RECT', 'UNITY_UI_ALPHACLIP'})}
actual = {}
# GLCore and SPIR-V programs are serialized as combined programs in progVertex;
# D3D11 has separate vertex15/fragment17 stages. Check actual retained variants.
for stage, kind in [('progVertex', 15), ('progFragment', 17), ('progVertex', 6), ('progVertex', 25)]:
    signatures = {frozenset(keywords[i] for i in p['m_KeywordIndices'])
                  for p in programs[stage]['m_SubPrograms'] if p['m_GpuProgramType'] == kind}
    check(signatures == expected, 'All four original UI clip/alpha variants survive for ' + str((stage, kind)))
    actual[stage + ':' + str(kind)] = [sorted(value) for value in sorted(signatures, key=lambda value: str(sorted(value)))]
notice = next(obj.read().m_Script for obj in env.objects if obj.type.name == 'TextAsset')
if isinstance(notice, bytes):
    notice = notice.decode('utf-8')
check(notice == license_path.read_text(), 'Embedded copyright and permission notice match primary source')
output.write_text(json.dumps({'assertions': checks, 'bundle': str(bundle),
    'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data),
    'unity': '2021.3.5f1', 'format': 7, 'container_objects': types,
    'platforms': shader['platforms'], 'retained_ui_variants': actual,
    'limits': 'Binary metadata establishes compiled program presence. GPU fixture separately runs the same Windows bundle on Linux GLCore/Vulkan; it does not execute D3D11 or headset eye routing.'}, indent=2) + '\n')
print('PASS actual stereo shader bundle: ' + str(checks) + ' metadata assertions')
