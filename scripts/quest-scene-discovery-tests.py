#!/usr/bin/env python3
"""Exercise standalone scene discovery with actual Unity scene and prefab objects.

The CPU comparison prices the actual production collector and B620's all-assets
route in Unity 2021.3.5f1. It establishes source coverage and local cost, without
claiming a Quest GPU, Android or headset outcome.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    output = ROOT / ".planning/debug/quest-scene-discovery"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    runtime = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestSceneObjects.cs"
    original = runtime.read_text()
    variants = [
        ("production", "", "", ""),
        ("all-assets-census-restored", "result.Clear();",
         "result.Clear(); result.AddRange(Resources.FindObjectsOfTypeAll<T>()); return;",
         "live-scene census excludes imported prefab assets"),
        ("inactive-native-widgets-skipped", "item.GetComponentsInChildren<T>(true, scratch);",
         "item.GetComponentsInChildren<T>(false, scratch);", "actual inactive native widget discovered"),
        ("persistent-roots-lost", "bool listed = false;", "bool listed = true;", "actual persistent owner discovered"),
        ("delayed-spawns-lost", "result.Clear();", "if (result.Count > 0) return; result.Clear();", "actual delayed native widget discovered"),
    ]
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
    fixture = ROOT / "tests/QuestSceneDiscovery.Tests"
    manifest = {"result": str(run / "results.txt"), "cases": []}
    print("Quest real Unity scene discovery evidence: " + str(run), flush=True)
    for name, before, after, expected in variants:
        case = run / name
        production = case / "production"
        production.mkdir(parents=True)
        source = original
        if before:
            if source.count(before) != 1:
                raise RuntimeError("Mutation binding drift: " + name)
            source = source.replace(before, after)
            if name == "all-assets-census-restored":
                # Preserve compilation as well as the intended defect. A
                # constant unconditional return would be a warning-only control.
                source = source.replace("return;\n            scenes.Clear();", "if (result.Count >= 0) return;\n            scenes.Clear();")
        (production / "QuestSceneObjects.cs").write_text(source)
        project = case / "Runtime.csproj"
        shutil.copyfile(fixture / "Runtime.csproj", project)
        assembly = "QuestSceneDiscovery_" + name.replace("-", "_")
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production),
            "-p:UnityManaged=" + str(unity.parent / "Data/Managed")], capture_output=True, text=True)
        (case / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + "Compilation cannot pass a negative control.")
        manifest["cases"].append({"name": name, "dll": str(case / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    project = run / "unity"
    editor = project / "Assets/Editor"
    components = project / "Assets/Runtime"
    editor.mkdir(parents=True); components.mkdir()
    for case in manifest["cases"]:
        imported = components / Path(case["dll"]).name
        shutil.copyfile(case["dll"], imported)
        case["dll"] = str(imported)
    shutil.copyfile(fixture / "Editor/SceneDiscoveryRunner.cs", editor / "SceneDiscoveryRunner.cs")
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2))
    (run / "source-hashes.json").write_text(json.dumps({"runtime": hashlib.sha256(runtime.read_bytes()).hexdigest(),
        "boundary": "Real Unity loaded/additive/persistent/inactive/delayed scenes and imported prefab asset population; CPU measurement, no Android timing claim"}, indent=2))
    command = [str(unity), "-batchmode", "-force-glcore", "-projectPath", str(project),
        "-executeMethod", "SceneDiscoveryRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    environment = os.environ.copy(); environment["QUEST_DISCOVERY_CPU_REPORT"] = str(run / "cpu.txt")
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300, env=environment)
    report = Path(manifest["result"])
    if report.is_file():
        print(report.read_text(), end="", flush=True)
    if result.returncode or not report.is_file():
        raise SystemExit("FAIL real Unity scene discovery fixture: " + str(run / "unity.log"))
    if (run / "cpu.txt").is_file(): print((run / "cpu.txt").read_text(), end="", flush=True)
    for cache in ("Library", "Temp"):
        shutil.rmtree(project / cache, ignore_errors=True)
    print("PASS production scene discovery and " + str(len(variants) - 1) + " defect controls")


if __name__ == "__main__":
    main()
