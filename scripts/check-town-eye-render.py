#!/usr/bin/env python3
"""Build D3D eye shaders and render a focused GL lighting/dissolve fixture.

This isolates shader behavior; use check-town-eye-windows.py on the generated and
final shipping bundles. Two offset camera views are not a hardware stereo test.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, default=Path("/tmp/town-eye-render"))
parser.add_argument("--unity", type=Path, default=Path("/home/claw/unity-2021.3.5/Editor/Unity"))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
args.output.mkdir(parents=True, exist_ok=True)
project = Path(tempfile.mkdtemp(prefix="run-", dir=args.output.resolve()))
assets = project / "Assets"
(assets / "Editor").mkdir(parents=True)
source = root / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices"
for name in ("TownEye.shader", "TownCornea.shader", "TownEyeLighting.cginc"):
    shutil.copy(source / "Shaders" / name, assets / name)
for name in ("brown_eye.png", "green_eye.png"):
    shutil.copy(source / "Textures" / name, assets / name)
shutil.copy(root / "scripts/town-eye-runtime/ValidateTownEyes.cs", assets / "Editor")
(assets / "BrokenEye.shader").write_text((assets / "TownEye.shader").read_text()
    .replace('"GloomhavenVR/TownEye"', '"Regression/BrokenEye"')
    .replace('"TownEyeLighting.cginc"', '"BrokenEyeLighting.cginc"'))
(assets / "BrokenEyeLighting.cginc").write_text((assets / "TownEyeLighting.cginc").read_text()
    .replace("output.vertexLights = 1;", "output.vertexLights = 0;"))
(project / "Packages").mkdir()
(project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
    "com.unity.modules." + name: "1.0.0" for name in ("assetbundle", "imageconversion", "physics")}}))
(project / "ProjectSettings").mkdir()
(project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
result = subprocess.run(["xvfb-run", "-a", str(args.unity), "-batchmode", "-projectPath", str(project),
    "-executeMethod", "ValidateTownEyes.Run", "-logFile", str(project / "unity.log")])
report = project / "evidence/result.txt"
if result.returncode or not report.exists():
    raise SystemExit(f"Eye shader probe failed: {project / 'unity.log'}")
print(report.read_text().strip())
print(f"Evidence: {project}")
subprocess.run(["uv", "run", str(root / "scripts/check-town-eye-windows.py"),
    str(project / "Build/eyes.bundle")], check=True)
