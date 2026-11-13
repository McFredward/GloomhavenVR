#!/usr/bin/env python3
"""Exercise actual Quest diagnostic materials, timing, lifecycle and restore in an isolated Unity editor."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--localization-source", type=Path, default=root / "src/GloomhavenVR/Core/Loc/QuestText.cs")
    args = parser.parse_args()
    output = root / ".planning/debug/quest-probe-diagnostics"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    runtime = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestProbeDiagnostics.cs"
    shader = root / "unity/GloomhavenVR.Quest/Assets/Quest/Shaders/QuestDiagnosticAlbedo.shader"
    production = runtime.read_text()
    cases = [("production", production, "")]
    controls = [
        ("lost-texture", 'pair.albedo.SetTexture("_MainTex", source.GetTexture(property));', 'pair.albedo.SetTexture("_MainTex", null);', "albedo mode retains original texture object"),
        ("lost-uv", 'pair.albedo.SetTextureScale("_MainTex", source.GetTextureScale(property));', 'pair.albedo.SetTextureScale("_MainTex", Vector2.one);', "albedo mode retains original UV scale and offset"),
        ("lost-tint", 'pair.albedo.SetColor("_Color", Tint(source));', 'pair.albedo.SetColor("_Color", Color.white);', "albedo mode retains original tint"),
        ("paused-resume", 'animators[i].speed = animationPaused ? 0f : animatorSpeeds[i];', 'animators[i].speed = 0f;', "animation resume restores each original speed"),
        ("median-p95", 'count * .95', 'count * .50', "frame p95 uses nearest-rank ninety-fifth percentile"),
        ("lost-material-restore", 'renderer.renderer.sharedMaterials = renderer.original;', 'renderer.renderer.sharedMaterials = renderer.lit;', "diagnostic destruction restores original material references"),
        ("lost-provenance", 'inputKey = inputKey, page = page,', 'inputKey = "unknown", page = page,', "persisted snapshot retains actual build provenance"),
    ]
    for name, before, after, expected in controls:
        if production.count(before) != 1:
            raise RuntimeError("Mutation binding drift: " + name)
        cases.append((name, production.replace(before, after, 1), expected))
    manifest = {"output": str(run), "cases": []}
    for index, (name, contents, expected) in enumerate(cases):
        namespace = "GloomhavenVR.Quest.DiagnosticsFixture" + str(index)
        directory = project / "Assets" / name
        directory.mkdir()
        (directory / "QuestProbeDiagnostics.cs").write_text(contents.replace("namespace GloomhavenVR.Quest", "namespace " + namespace, 1))
        manifest["cases"].append({"name": name, "ns": namespace, "expected": expected})
    shutil.copyfile(shader, project / "Assets/QuestDiagnosticAlbedo.shader")
    shutil.copyfile(args.localization_source, project / "Assets/QuestText.cs")
    shutil.copyfile(root / "tests/quest-probe-diagnostics/Editor/QuestProbeDiagnosticsRunner.cs", project / "Assets/Editor/QuestProbeDiagnosticsRunner.cs")
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
        "com.unity.ugui": "1.0.0", "com.unity.modules.animation": "1.0.0",
        "com.unity.modules.physics": "1.0.0", "com.unity.modules.xr": "1.0.0",
        "com.unity.modules.jsonserialize": "1.0.0", "com.unity.modules.ui": "1.0.0",
        "com.unity.modules.imgui": "1.0.0", "com.unity.modules.imageconversion": "1.0.0"}}, indent=2) + "\n")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    (run / "source-hashes.json").write_text(json.dumps({str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in [runtime, shader, args.localization_source]}, indent=2) + "\n")
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    print("Quest diagnostic evidence: " + str(run), flush=True)
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(project),
               "-executeMethod", "QuestProbeDiagnosticsRunner.Run", "-questDiagnosticsManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    results = run / "results.txt"
    if results.exists():
        print(results.read_text(), end="")
    if result.returncode or not results.exists():
        raise SystemExit("FAIL Quest diagnostic Unity exit " + str(result.returncode) + "; see " + str(run / "unity.log"))
    print("PASS Quest diagnostic runtime and 7 defect controls")


if __name__ == "__main__":
    main()
