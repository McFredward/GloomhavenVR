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
for source in (root / 'scripts/town-lighting-runtime').glob('*.cs'):
    shutil.copy(source, editor)
for name in ('TownServiceLightList', 'TownServiceLighting'):
    source = (root / f'src/GloomhavenVR/WorldUI/TownServices/{name}.cs').read_text()
    # The fixture runs in Editor mode. Queue the native Destroy boundary explicitly;
    # production disposal/registration remains unchanged and is checked before flush.
    source = source.replace('UnityEngine.Object.Destroy(', 'TownLightingEditorLifetime.Destroy(')
    source = source.replace('namespace GloomhavenVR.WorldUI;', 'namespace GloomhavenVR.WorldUI {') + '\n}\n'
    (editor / f'{name}.cs').write_text('#nullable enable\n' + source)
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
(project / 'source-hashes.json').write_text(json.dumps({str(path.relative_to(root)): hashlib.sha256(
    path.read_bytes()).hexdigest() for path in inputs if path.is_file()}, indent=2))
result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project),
                         '-executeMethod', 'ValidateTownLighting.Run', '-logFile', str(project / 'unity.log')])
report = project / 'result.txt'
if result.returncode or not report.exists():
    raise SystemExit(f'Town lighting failed: {project / "unity.log"}')
print(report.read_text().strip())
print(f'Evidence: {project}')
