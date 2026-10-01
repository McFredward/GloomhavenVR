#!/usr/bin/env python3
"""Render production town shaders and real actor meshes across competing-lamp boundaries.

Uses Unity 2021.3.5/GL and the matching Linux source-review bundle. Windows/D3D headset
output remains a hardware check. No game data or production bundles are modified.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--bundle', required=True, type=Path)
parser.add_argument('--output', type=Path, default=root / '.planning/debug/town-lighting')
parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
project = Path(tempfile.mkdtemp(prefix='lighting-', dir=args.output.resolve()))
editor = project / 'Assets/Editor'
editor.mkdir(parents=True)
def member(source, signature):
    start = source.index(signature); opening = source.index('{', start); depth = 1; end = opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end]

for source in (root / 'scripts/town-lighting-runtime').glob('*.cs'):
    shutil.copy(source, editor)
for name in ('TownServiceLightList', 'TownServiceLighting'):
    source = (root / f'src/GloomhavenVR/WorldUI/TownServices/{name}.cs').read_text()
    # The fixture runs in Editor mode. Queue the native Destroy boundary explicitly;
    # production disposal/registration remains unchanged and is checked before flush.
    source = source.replace('UnityEngine.Object.Destroy(', 'TownLightingEditorLifetime.Destroy(')
    source = source.replace('namespace GloomhavenVR.WorldUI;', 'namespace GloomhavenVR.WorldUI {') + '\n}\n'
    (editor / f'{name}.cs').write_text('#nullable enable\n' + source)
types = (root / 'src/GloomhavenVR/Net/TownActivityState.cs').read_text().split('/// <summary>Additive81', 1)[0]
types = types.replace('namespace GloomhavenVR.Net;', 'namespace GloomhavenVR.Net {') + '\n}\n'
(editor / 'ActivityTypes.cs').write_text('#pragma warning disable CS0649\n' + types)
marker_path = root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleBowlMarker.cs'
ghost = member(marker_path.read_text(), 'private static Material GhostMaterial(Material source)').replace('private static', 'internal static', 1)
(editor / 'ActualGuideMaterial.cs').write_text('using System; using UnityEngine; using UnityEngine.Rendering; namespace GloomhavenVR.WorldUI { internal static class ActualGuideMaterial { ' + ghost + ' } }')
binding_path = root / 'src/GloomhavenVR/Net/TownServices/TownServiceBinding.cs'
capture = member(binding_path.read_text(), 'private static bool ReadMesh(NodeCache cache, TownServiceAssets assets)')
(editor / 'ActualMeshCapture.cs').write_text('using System;using System.IO;using System.Collections.Generic;using UnityEngine;namespace GloomhavenVR.Net.TownServices {internal static class ActualMeshCapture { private sealed class NodeCache { internal MeshRenderer Mesh;internal readonly List<Material> MeshMaterials=new List<Material>();internal readonly List<TownServiceValue> MeshValues=new List<TownServiceValue>(); } ' + capture + ' internal static TownServiceValue Capture(MeshRenderer mesh,TownServiceAssets assets) { var cache=new NodeCache{Mesh=mesh};ReadMesh(cache,assets);return cache.MeshValues[0]; } } }')
material_path = root / 'src/GloomhavenVR/Net/TownServices/TownServiceMaterial.cs'
material = material_path.read_text().replace('namespace GloomhavenVR.Net.TownServices;', 'namespace GloomhavenVR.Net.TownServices {') + '\n}\n'
(editor / 'ActualMaterial.cs').write_text('#nullable enable\n' + material)
frame_path = root / 'src/GloomhavenVR/Net/TownServices/TownServiceFrame.cs'
frame = frame_path.read_text()
(editor / 'ActualMaterialValue.cs').write_text('#nullable enable\nusing System;using System.Collections.Generic;namespace GloomhavenVR.Net.TownServices {' + member(frame,'internal sealed class TownServiceValue\n') + member(frame,'internal sealed class TownServiceValueComparer') + '\n}')
shutil.copytree(root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders', project / 'Assets/Shaders')
shutil.copy(root / 'scripts/town-lighting-runtime/TownNpc548.shader', project / 'Assets/Shaders')
(project / 'Packages').mkdir()
(project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {
    'com.unity.modules.' + module: '1.0.0' for module in ['animation', 'assetbundle', 'imageconversion', 'physics']}}))
(project / 'ProjectSettings').mkdir()
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
(project / 'bundle-path.txt').write_text(str(args.bundle.resolve()))
inputs = list((root / 'scripts/town-lighting-runtime').glob('*')) + list(
    (root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders').glob('*')) + [
        root / f'src/GloomhavenVR/WorldUI/TownServices/{name}.cs' for name in ('TownServiceLightList', 'TownServiceLighting')]
inputs.append(root / 'src/GloomhavenVR/Net/TownActivityState.cs')
inputs += [marker_path, binding_path, material_path, frame_path]
(project / 'source-hashes.json').write_text(json.dumps({str(path.relative_to(root)): hashlib.sha256(
    path.read_bytes()).hexdigest() for path in inputs if path.is_file()}, indent=2))
result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project),
                         '-executeMethod', 'ValidateTownLighting.Run', '-logFile', str(project / 'unity.log')])
report = project / 'result.txt'
if result.returncode or not report.exists():
    raise SystemExit(f'Town lighting failed: {project / "unity.log"}')
print(report.read_text().strip())
print(f'Evidence: {project}')
