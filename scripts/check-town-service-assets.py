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
shutil.copy(root / 'unity/GloomhavenVR.Assets/Assets/Editor/BuildTownServices.cs', assets / 'Editor')
# Bind the production slot equations; the mesh check must catch layout/asset drift,
# not compare a fixture's hard-coded idea of where the stock ought to be.
layout_path = root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantCounter.cs'
layout_source = layout_path.read_text()
layout_start = layout_source.index('internal static class TownServiceMerchantLayout')
layout_end = layout_source.index('\n/// <summary>An authored', layout_start)
(assets / 'Editor/TownServiceMerchantLayout.cs').write_text(
    'using UnityEngine;\nnamespace GloomhavenVR.WorldUI {\n' + layout_source[layout_start:layout_end] + '\n}\n')
# Compile unchanged physical factory methods against the actual final prefab. The unused
# TMP font argument is substituted with object; the crank deliberately contains no UI.
drawer = (root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantDrawer.cs').read_text()
def method(signature):
    start = drawer.index('    ' + signature)
    end = drawer.index('\n    }', start) + 6
    return drawer[start:end].replace('TMP_Text?', 'object')
methods = [method(signature) for signature in (
    'internal static GameObject Authored(string name)',
    'internal static GameObject CreateHousingTemplate()',
    'internal static bool MerchantLanternClears(Bounds cabinetPart, Vector3 seat)')]
methods.append('    ' + next(line.strip() for line in drawer.splitlines()
    if line.strip().startswith('internal static Vector3 MerchantLanternSeat(')))
methods.append('    ' + next(line.strip() for line in drawer.splitlines()
    if line.strip().startswith('internal static GameObject CreateTemplate(')).replace('TMP_Text?', 'object'))
(assets / 'Editor/TownServiceRackFactories.cs').write_text(
    'using System; using UnityEngine; using TMPro; using GloomhavenVR.Net.TownServices; namespace GloomhavenVR.WorldUI { '
    'internal static class TownServiceAssets { internal static GameObject Current; internal static GameObject Prefab(string name) => Current; } '
    'internal static class TownServiceMerchantDrawer {\n' + '\n'.join(methods) + '\n} }')
motion = (root / 'src/GloomhavenVR/Net/TownServices/TownCassetteMotion.cs').read_text()
(assets / 'Editor/TownCassetteMotion.cs').write_text(motion.replace(
    'namespace GloomhavenVR.Net.TownServices;', 'namespace GloomhavenVR.Net.TownServices {') + '\n}\n')
(project / 'Packages').mkdir()
dependencies = {'com.unity.modules.' + name: '1.0.0' for name in ['animation', 'assetbundle', 'imageconversion', 'physics', 'audio']}
dependencies.update({'com.unity.ugui': '1.0.0', 'com.unity.textmeshpro': '3.0.6'})
(project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}))
(project / 'ProjectSettings').mkdir()
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
control = (root / 'scripts/town-service-asset-runtime/TownNpc539.shader').read_bytes()
(assets / 'OldTownShader.shader').write_bytes(control.replace(b'GloomhavenVR/TownNpc', b'GloomhavenVR/TownNpc539'))
validation_sources = [root / path for path in (
    'scripts/check-town-service-assets.py', 'scripts/town-service-asset-runtime/ValidateTownAssets.cs',
    'scripts/author-town-facial-topology.py', 'scripts/assemble-town-facial-rig.py',
    'scripts/author-town-eyelid-clearance.py', 'scripts/town_npc_eyelid_clearance.py',
    'scripts/author-town-leg-weights.py',
    'scripts/author-town-npc-hands.py', 'scripts/town_npc_hand_integration.py', 'scripts/town_npc_neck_inset.py',
    'scripts/author-town-furniture.py',
    'src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantCounter.cs',
    'src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantDrawer.cs',
    'src/GloomhavenVR/WorldUI/TownServices/TownServiceCatalog.cs',
    'scripts/town_npc_necklines.py', 'scripts/town_npc_garment_weights.py', 'scripts/town_npc_cloth_edges.py',
    'unity/GloomhavenVR.Assets/Assets/Editor/BuildTownServices.cs')]
inputs = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
          for p in list(source.rglob('*')) + validation_sources if p.is_file()}
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
