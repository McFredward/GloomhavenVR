#!/usr/bin/env python3
"""Measure actual standalone scope discovery with real 14k-component Unity scenes.

Compare current Scope.Scan with the frozen B621 production snapshot, not merely
its generic collector. Exercise exact typed queries, inactive/late/DontSave and
DDOL owners, imported prefabs, additive unload and future subclasses. Desktop CPU
and negative controls do not establish Android timing or headset hitch removal.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--production-only", action="store_true", help="Measure the actual production case while iterating a failure; omits defect controls.")
    args = parser.parse_args()
    runtime = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime"
    sources = {name: runtime / name for name in ("QuestScopeObjects.cs", "QuestSceneObjects.cs", "QuestGameScope.cs")}
    sources["QuestText.cs"] = ROOT / "src/GloomhavenVR/Core/Loc/QuestText.cs"
    original = {name: path.read_text() for name, path in sources.items()}
    controls = [
        ("broad-14338-census-restored", "Resources.FindObjectsOfTypeAll(target)",
         "Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour))", "native exact-type queries transfer only rare candidates among 14k behaviours"),
        ("inactive-owners-lost", "Resources.FindObjectsOfTypeAll(target)",
         "UnityEngine.Object.FindObjectsOfType(target, false)", "actual inactive scope owner retained"),
        ("dontsave-owners-lost", "Resources.FindObjectsOfTypeAll(target)",
         "UnityEngine.Object.FindObjectsOfType(target, true)", "actual DontSave scope owner retained"),
        ("prefab-assets-included", "|| !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded", "",
         "actual imported scope prefab remains untouched"),
        ("future-subclasses-bound", "component.GetType() != target", "false", "actual future native subclass remains outside exact scope"),
        ("persistent-owners-lost", "component.GetType() != target",
         'component.GetType() != target || component.gameObject.scene.name == "DontDestroyOnLoad"', "actual DDOL scope owner retained"),
        ("phased-native-queries-burst", "now + (index + 1d) / deadlines.Length", "now + 1d", "normal production discovery tick has one atomic native type query"),
        ("known-family-cadence-dropped", "deadlines[due] += 1d;", "deadlines[due] += 2d;", "normal production discovery tick has one atomic native type query"),
        ("forward-jump-resynchronizes-burst", "deadlines[due] += Math.Floor(now - deadlines[due]) + 1d;", "deadlines[due] = now + 1d;", "forward-jump recovery retains staggered normal native queries"),
        ("catchup-families-dropped", "pending < targets.Length", "pending < 1", "forward clock jump catches up all seven families once"),
    ]
    if args.production_only: controls = []
    output = ROOT / ".planning/debug/quest-scope-discovery"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    print("Quest real Unity targeted scope evidence: " + str(run), flush=True)
    fixture = ROOT / "tests/QuestScopeDiscovery.Tests"
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
    game = ROOT / "ressources/GH_Data/Managed"
    manifest = {"result": str(run / "results.txt"), "cases": []}
    for name, before, after, expected in [("production", "", "", "")] + controls:
        case = run / name
        production = case / "production"
        production.mkdir(parents=True)
        for filename, text in original.items():
            if filename == "QuestScopeObjects.cs" and before:
                if text.count(before) != 1: raise RuntimeError("Mutation binding drift: " + name)
                text = text.replace(before, after)
            (production / filename).write_text(text)
        project = case / "Runtime.csproj"
        shutil.copyfile(fixture / "Runtime.csproj", project)
        assembly = "QuestScopeDiscovery_" + name.replace("-", "_")
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production),
            "-p:UnityManaged=" + str(unity.parent / "Data/Managed"), "-p:GameManaged=" + str(game)], capture_output=True, text=True)
        (case / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + "Compilation cannot pass a negative control.")
        manifest["cases"].append({"name": name, "dll": str(case / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    project = run / "unity"
    editor = project / "Assets/Editor"; components = project / "Assets/Runtime"
    editor.mkdir(parents=True); components.mkdir()
    for case in manifest["cases"]:
        imported = components / Path(case["dll"]).name; shutil.copyfile(case["dll"], imported); case["dll"] = str(imported)
    shutil.copyfile(game / "UnityEngine.UI.dll", components / "UnityEngine.UI.dll")
    shutil.copyfile(ROOT / "tests/QuestSceneDiscovery.Tests/Editor/SceneDiscoveryRunner.cs", editor / "SceneDiscoveryRunner.cs")
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"; manifest_path.write_text(json.dumps(manifest, indent=2))
    (run / "source-hashes.json").write_text(json.dumps({"sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in sources.items()},
        "b621ScopeSnapshot": hashlib.sha256((fixture / "B621Scope.cs").read_bytes()).hexdigest(),
        "boundary": "Actual 14k MonoBehaviour production Scope.Scan CPU and live/inactive/DontSave/prefab/additive/DDOL/delayed boundaries; no Android timing claim."}, indent=2))
    command = [str(unity), "-batchmode", "-force-glcore", "-projectPath", str(project), "-executeMethod", "SceneDiscoveryRunner.Start",
        "-interactionManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"): command = ["xvfb-run", "-a"] + command
    environment = os.environ.copy(); environment["QUEST_DISCOVERY_CPU_REPORT"] = str(run / "cpu.txt")
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300, env=environment)
    report = Path(manifest["result"])
    if report.is_file(): print(report.read_text(), end="", flush=True)
    if result.returncode or not report.is_file(): raise SystemExit("FAIL real Unity targeted scope fixture: " + str(run / "unity.log"))
    if (run / "cpu.txt").is_file(): print((run / "cpu.txt").read_text(), end="", flush=True)
    for cache in ("Library", "Temp"): shutil.rmtree(project / cache, ignore_errors=True)
    print("PASS production targeted scope and " + str(len(controls)) + " defect controls")


if __name__ == "__main__":
    main()
