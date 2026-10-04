#!/usr/bin/env python3
"""Validate actual Android mono/instanced/multiview screen sampler banks.

The real GPU eye-layer fixture lives in quest-video-output. This isolated compiler
fixture checks the production native bank verifier and actual GLSL defect controls;
neither fixture asserts an observed headset outcome.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, default=Path("/home/claw/unity-2021.3.5/Editor/Unity"))
    args = parser.parse_args()
    output = root / ".planning/debug/quest-world-screen-shaders"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    source_root = root / "unity/GloomhavenVR.Quest"
    paths = [
        "Assets/Quest/Resources/QuestWorldScreen.shader",
        "Assets/Quest/Editor/QuestWorldScreenValidation.cs",
        "Assets/Quest/Editor/QuestUiAssetValidation.cs",
    ]
    hashes = {}
    for relative in paths:
        source = source_root / relative
        target = project / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        hashes[relative] = hashlib.sha256(source.read_bytes()).hexdigest()
    shutil.copy2(root / "tests/QuestWorldScreen.Tests/Probe.cs", project / "Assets/Quest/Editor/Probe.cs")
    hashes["tests/QuestWorldScreen.Tests/Probe.cs"] = hashlib.sha256((root / "tests/QuestWorldScreen.Tests/Probe.cs").read_bytes()).hexdigest()
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    print("Quest world-screen GLES evidence: " + str(run), flush=True)
    result = subprocess.run([str(args.unity.resolve()), "-batchmode", "-nographics", "-quit",
        "-buildTarget", "Android", "-projectPath", str(project), "-executeMethod", "QuestWorldScreenProbe.Run",
        "-logFile", str(run / "unity.log")], timeout=180, capture_output=True, text=True)
    (run / "console.log").write_text(result.stdout + result.stderr)
    if result.returncode:
        raise SystemExit("Actual world-screen GLES fixture failed: " + str(run / "unity.log"))
    evidence = json.loads((project / "ProbeOutput/results.json").read_text())
    assert evidence["actualBanks"] == 3 and evidence["actualStages"] == 6 and evidence["defectControls"] == 18
    assert not evidence["hardwareVerified"]
    for relative in paths:
        assert hashlib.sha256((source_root / relative).read_bytes()).hexdigest() == hashes[relative]
    (run / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    print("PASS Quest world-screen GLES: 3 actual banks, 6 stages, 18 defect controls")


if __name__ == "__main__":
    main()
