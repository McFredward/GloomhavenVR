#!/usr/bin/env python3
"""Render the recovered original blur on a genuine Unity Canvas/GrabPass fixture.

Copies owned assets into a private fixture. Native source and source project stay
read-only. Desktop GL pixel evidence does not establish Quest GPU parity.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--prepared-project", type=Path, required=True)
    args = parser.parse_args()
    source = args.prepared_project.resolve()
    receipt = json.loads((source / "QuestStartupEvidence/original-ui-blur.json").read_text())
    output = root / ".planning/debug/quest-ui-blur-tests"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    editor = project / "Assets/Quest/Editor"
    editor.mkdir(parents=True)
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    before = {}
    for relative, expected in ((receipt["assetPath"], receipt["sourceSha256"]), (receipt["assetPath"] + ".meta", receipt["metaSha256"])):
        path = source / relative
        content = path.read_bytes()
        if (not relative.startswith("Assets/Shader/") or ".." in Path(relative).parts or path.is_symlink()
                or hashlib.sha256(content).hexdigest() != expected):
            raise SystemExit("Unsafe or changed prepared original blur asset")
        before[relative] = content
        target = project / relative
        target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(content)
    (project / "QuestStartupEvidence").mkdir()
    shutil.copyfile(source / "QuestStartupEvidence/original-ui-blur.json", project / "QuestStartupEvidence/original-ui-blur.json")
    for name in ("QuestUiAssetValidation.cs", "QuestBlurValidation.cs"):
        shutil.copyfile(root / "unity/GloomhavenVR.Quest/Assets/Quest/Editor" / name, editor / name)
    shutil.copyfile(root / "tests/QuestUiBlur.Tests/Probe.cs", editor / "Probe.cs")
    shutil.copyfile(root / "tests/QuestUiBlur.Tests/NormalSample.shader", project / "Assets/NormalSample.shader")
    program = (project / receipt["assetPath"]).read_text()
    # The actual desktop editor is running with Android's plain-RGB compile
    # policy. A separate fixture-only native bank selects the original D3D
    # R* A branch so its AG arithmetic can be tested without claiming AG is a
    # valid raw texture for the Android RGB importer.
    native = program.replace('#include "UnityCG.cginc"', '#include "UnityCG.cginc"\n        #undef UNITY_NO_DXT5nm')
    (project / "Assets/native-d3d-normal-control.shader").write_text(native.replace('Shader "Custom/SimpleGrabPassBlur"', 'Shader "QuestBlurFixture/native-d3d-normal-control"'))
    for name, old, new in (
            ("vertical-axis-lost", "#pragma fragment vertical", "#pragma fragment horizontal"),
            ("normal-AG-channel-lost", "encodedNormal.r * encodedNormal.a", "encodedNormal.r"),
            ("homogeneous-Z-lost", "offset * input.grab.z", "offset * input.grab.w")):
        if program.count(old) != 1: raise SystemExit("Actual blur defect-control binding drift: " + name)
        basis = native if name == "normal-AG-channel-lost" else program
        mutant = basis.replace(old, new).replace('Shader "Custom/SimpleGrabPassBlur"', 'Shader "QuestBlurFixture/' + name + '"')
        (project / "Assets" / (name + ".shader")).write_text(mutant)
    command = [str(args.unity), "-batchmode", "-force-glcore", "-buildTarget", "Android", "-projectPath", str(project),
               "-executeMethod", "QuestUiBlurProbe.Run", "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"): command = ["xvfb-run", "-a"] + command
    print("Quest original UI blur evidence: " + str(run), flush=True)
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    if result.returncode:
        raise SystemExit("Actual Canvas/GrabPass fixture failed: " + str(run / "unity.log"))
    report = json.loads((project / "ProbeOutput/results.json").read_text())
    report["preparedSourceUntouched"] = all((source / path).read_bytes() == content for path, content in before.items())
    (run / "results.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2), flush=True)
    if not report["preparedSourceUntouched"] or not report["actualCanvasRenderer"] or report["cases"] != 12 or not all(case["passed"] for case in report["samples"]):
        raise SystemExit("Actual Canvas/GrabPass evidence is incomplete")
    for name in ("Library", "Temp"): shutil.rmtree(project / name, ignore_errors=True)


if __name__ == "__main__":
    main()
