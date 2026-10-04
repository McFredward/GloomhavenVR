#!/usr/bin/env python3
"""Compile the two original UI shaders in a small isolated Unity Android fixture.

The prepared project remains read-only. Evidence records actual GLES banks and
production validation/negative controls; this does not assert headset pixels.
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
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--prepared-project", type=Path, required=True)
    args = parser.parse_args()
    source = args.prepared_project.resolve()
    if source.is_symlink() or not (source / "QuestStartupEvidence/original-ui-assets.json").is_file():
        raise SystemExit("A prepared private Quest UI source receipt is required.")
    output = root / ".planning/debug/quest-ui-shader-tests"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    (project / "Assets/Quest/Editor").mkdir(parents=True)
    (project / "ProjectSettings").mkdir()
    (project / "Packages").mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    receipt = json.loads((source / "QuestStartupEvidence/original-ui-assets.json").read_text())
    before = {}
    for row in receipt["shaders"]:
        for relative, expected in ((row["assetPath"], row["sourceSha256"]), (row["assetPath"] + ".meta", row["metaSha256"])):
            if not relative.startswith("Assets/Shader/") or ".." in Path(relative).parts:
                raise SystemExit("Unsafe private UI source path.")
            path = source / relative
            content = path.read_bytes()
            if path.is_symlink() or hashlib.sha256(content).hexdigest() != expected:
                raise SystemExit("Private UI source differs from its receipt.")
            before[relative] = content
            target = project / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
    target_receipt = project / "QuestStartupEvidence/original-ui-assets.json"
    target_receipt.parent.mkdir()
    shutil.copy2(source / "QuestStartupEvidence/original-ui-assets.json", target_receipt)
    production = root / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestUiAssetValidation.cs"
    shutil.copy2(production, project / "Assets/Quest/Editor/QuestUiAssetValidation.cs")
    shutil.copy2(root / "tests/QuestUiShaders.Tests/Probe.cs", project / "Assets/Quest/Editor/Probe.cs")
    print("Quest UI shader evidence: " + str(run), flush=True)
    result = subprocess.run([str(args.unity.resolve()), "-batchmode", "-nographics", "-quit", "-buildTarget", "Android",
                             "-projectPath", str(project), "-executeMethod", "QuestUiShaderProbe.Run", "-logFile", str(run / "unity.log")],
                            timeout=180, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    (run / "console.log").write_text(result.stdout)
    if result.returncode != 0:
        raise SystemExit("Actual GLES fixture failed; see " + str(run / "unity.log"))
    evidence = json.loads((project / "ProbeOutput/results.json").read_text())
    evidence["productionSourceSha256"] = hashlib.sha256(production.read_bytes()).hexdigest()
    evidence["preparedSourceUntouched"] = all((source / relative).read_bytes() == content for relative, content in before.items())
    if (not evidence["preparedSourceUntouched"] or evidence["actualCompiledBanks"] != 3 or
            evidence["actualCompiledStages"] != 6 or evidence["negativeControls"] != 39 or
            not all(case["passed"] for case in evidence["cases"])):
        raise SystemExit("Actual GLES fixture evidence is incomplete.")
    (run / "results.json").write_text(json.dumps(evidence, indent=2) + "\n")
    print("PASS Quest UI shaders: actual banks=3, stages=6, negative controls=39", flush=True)


if __name__ == "__main__":
    main()
