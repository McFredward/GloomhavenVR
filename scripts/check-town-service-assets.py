#!/usr/bin/env python3
"""Render the actual town assets with environment and physical stand lighting in isolated Unity.

The shipping bundle targets Windows/D3D. This Linux check builds the same sources for
Linux/GL; it does not claim to validate a headset's D3D/stereo output. The original
build-540 studio-light shader is retained as a negative-control fixture.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path)
parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = (args.output or root / '.planning/debug/town-service-assets').resolve()
output.mkdir(parents=True, exist_ok=True)
project = Path(tempfile.mkdtemp(prefix='town-assets-', dir=output))
assets = project / 'Assets'
source = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices'
shutil.copytree(source, assets / 'Bundle/TownServices')
(assets / 'Editor').mkdir()
shutil.copy(root / 'scripts/town-service-asset-runtime/ValidateTownAssets.cs', assets / 'Editor')
(project / 'Packages').mkdir()
(project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {
    'com.unity.modules.' + name: '1.0.0' for name in ['animation', 'assetbundle', 'imageconversion', 'physics']}}))
(project / 'ProjectSettings').mkdir()
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
control = (root / 'scripts/town-service-asset-runtime/TownNpc539.shader').read_bytes()
(assets / 'OldTownShader.shader').write_bytes(control.replace(b'GloomhavenVR/TownNpc', b'GloomhavenVR/TownNpc539'))
inputs = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
          for p in source.rglob('*') if p.is_file()}
(project / 'source-hashes.json').write_text(json.dumps(inputs, indent=2) + '\n')
evidence = project / 'evidence'
command = ['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project),
           '-executeMethod', 'ValidateTownAssets.BuildAndRun', '-townEvidence', str(evidence),
           '-logFile', str(project / 'unity.log')]
result = subprocess.run(command, cwd=root)
report = evidence / 'result.txt'
if result.returncode or not report.exists():
    raise SystemExit(f'Town asset rendering failed; inspect {project / "unity.log"}')
print(report.read_text().strip())
print(f'Evidence: {evidence}')
