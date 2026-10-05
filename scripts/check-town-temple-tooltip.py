#!/usr/bin/env python3
"""Bind native temple counter template preparation and prove topology in Unity 2021.3.5.

The native HUD/reference provider is an explicit fixture boundary. Actual template
shape selection, original counter-child construction, freeze/prune/partition,
neutralization and binding identity compile from production, with a causal control.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-temple-tooltip")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location("mirror_binding", root / "scripts/check-town-service-mirror.py")
    helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
    bound, hashes = helper.sources(root)
    native = root / "src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.TempleTooltip.cs"
    original = native.read_text()
    # The source provider is the native HUD boundary; all template methods are
    # otherwise unchanged. The production Parts dispatch is verified separately.
    bound["TempleTooltip623Production.cs"] = original.replace(
        "internal static partial class NativeTemplates", "internal static partial class LazyTemplateProbe", 1)
    hashes[str(native.relative_to(root))] = hashlib.sha256(native.read_bytes()).hexdigest()
    (run / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    variants = [("production", None), ("counter-pool-omitted", "count")]
    manifest = {"result": str(run / "results.txt"), "evidence": str(run), "suite": "temple-tooltip623", "cases": []}
    fixture = root / "scripts/town-service-mirror-runtime"
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    for name, mutation in variants:
        output = run / name
        production = output / "production"; production.mkdir(parents=True)
        for filename, text in bound.items():
            if mutation and filename == "TempleTooltip623Production.cs":
                before = "for (int i = 0; i < count; i++)"
                if text.count(before) != 1: raise RuntimeError("Native counter construction mutation drift")
                text = text.replace(before, "for (int i = 0; i < 0; i++)", 1)
            (production / filename).write_text(text)
        assembly = "TempleTooltip623_" + name.replace("-", "_")
        project = output / "Mirror.csproj"; shutil.copyfile(fixture / "Mirror.csproj", project)
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
            "-p:DefineConstants=TEMPLE_TOOLTIP623", f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}",
            f"-p:UnityUi={root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'}",
            f"-p:UnityTmp={root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll'}"],
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        (output / "build.log").write_text(result.stdout)
        if result.returncode: print(result.stdout); raise SystemExit("FAIL compilation: " + name)
        manifest["cases"].append({"name": name, "dll": str(output / "bin/Release/netstandard2.1" / (assembly + ".dll")),
            "expected": "native temple tooltip retains the exact owner's dynamic counter topology" if mutation else ""})
    project = run / "unity-project"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    runner = (fixture / "Editor/MirrorRunner.cs").read_text().replace('type.GetMethod("Run")', 'type.GetMethod("RunTempleTooltip623")')
    (project / "Assets/Editor/MirrorRunner.cs").write_text(runner)
    path = run / "manifest.json"; path.write_text(json.dumps(manifest, indent=2) + "\n")
    print("Evidence: " + str(run), flush=True)
    result = subprocess.run(["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-buildTarget", "Linux64",
        "-projectPath", str(project), "-executeMethod", "MirrorRunner.Start", "-mirrorManifest", str(path),
        "-logFile", str(run / "unity.log")], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    evidence = Path(manifest["result"])
    if evidence.exists(): print(evidence.read_text(), end="")
    for directory in ("Library", "Temp", "Logs"): shutil.rmtree(project / directory, ignore_errors=True)
    for case in manifest["cases"]:
        dll = Path(case["dll"])
        for file in dll.parent.glob("*"):
            if file.suffix in (".dll", ".pdb", ".xml") and file.stem != dll.stem: file.unlink()
        shutil.rmtree(run / case["name"] / "obj", ignore_errors=True)
    if result.returncode or not evidence.exists(): raise SystemExit("FAIL native temple tooltip; see " + str(run))


if __name__ == "__main__": main()
