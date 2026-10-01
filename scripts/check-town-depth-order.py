#!/usr/bin/env python3
"""Run town props against the production window/furniture distance ladder in real Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(text, signature):
    start = text.index("    " + signature)
    return text[start:text.index("\n    }", start) + 6]


def sources(root):
    base = root / "src/GloomhavenVR/WorldUI"
    furniture = (base / "Conversion/CanvasConversion.9.Furniture.cs").read_text()
    order = (base / "Conversion/CanvasConversion.8.Order.cs").read_text()
    declarations = furniture[furniture.index("    private const int FurnitureBandWidth"):
                             furniture.index("    /// <summary>\n    /// Register a mod-owned transparent")]
    interface = furniture[furniture.index("internal interface IFurnitureOrderAnchor"):
                          furniture.index("internal static partial class CanvasConversion")]
    constants = []
    for name in ("PanelOrderBase", "PanelOrderStep", "PanelOrderMaxRank", "OrderSwapStableFrames", "OrderDiagMinIntervalSeconds"):
        import re
        found = re.search(r"    (?:internal|private) const (?:int|float) " + name + r" = [^;]+;", order)
        if not found:
            raise RuntimeError("Production ladder declaration drift: " + name)
        constants.append(found.group())
    signatures = (
        "internal static void RegisterFurniture(IFurnitureOrderAnchor anchor, Renderer renderer, int offset)",
        "internal static void RegisterFurniture(IFurnitureOrderAnchor anchor, Canvas canvas, int offset)",
        "internal static void ReassertFurniture(IFurnitureOrderAnchor anchor)",
        "private static FurnitureGroup GroupFor(IFurnitureOrderAnchor anchor)",
        "private static void AddFurniture(FurnitureGroup group, Renderer? renderer, Canvas? canvas,",
        "private static void TickFurnitureOrder(Vector3 eye)",
        "private static void ApplyFurnitureOrder(FurnitureGroup group)",
        "private static void PruneFurniture()", "private static void DropFurnitureEntry(FurnitureGroup group, int index)",
        "private static void LogFurnitureOrder(FurnitureGroup group, int previousRank, float dist)")
    start = furniture.index("    private static int FurnitureBandBase")
    band = furniture[start:furniture.index(";", start) + 1]
    scaffold = """using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
namespace GloomhavenVR.WorldUI;
internal sealed class ConvertedPanel { internal IFurnitureOrderAnchor? OrderCluster = null; internal float OrderDistance; }
"""
    scaffold += interface + "internal static partial class CanvasConversion {\n" + declarations
    scaffold += "\n".join(constants) + "\n" + band + "\n"
    scaffold += """private static readonly List<ConvertedPanel> OrderedPanels = new();
private static float OrderSwapMargin => .02f;
internal static void Panels(params float[] distances) { OrderedPanels.Clear(); foreach(float d in distances) OrderedPanels.Add(new ConvertedPanel {OrderDistance=d}); }
internal static void Tick(Vector3 eye) => TickFurnitureOrder(eye);
// This unrelated free-plate helper is referenced by the original XML comments only.
internal static int OrderAboveDistanceAndClusters(Vector3 eye, Vector3 point) => 0;
internal static int Groups => FurnitureGroups.Count;
internal static int Entries => FurnitureGroups.Count == 0 ? 0 : FurnitureGroups[0].Entries.Count;
"""
    scaffold += "\n".join(method(furniture, signature) for signature in signatures) + "\n}\n"
    return {"Ladder.cs": scaffold, "TownServiceDepthOrder.cs": (base / "TownServices/TownServiceDepthOrder.cs").read_text()}, {
        "CanvasConversion.9.Furniture.cs": hashlib.sha256(furniture.encode()).hexdigest(),
        "CanvasConversion.8.Order.cs": hashlib.sha256(order.encode()).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/town-depth-order")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    bound, hashes = sources(args.source_root)
    hashes.update({name: hashlib.sha256(value.encode()).hexdigest() for name, value in bound.items()})
    (run / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    variants = [("production", "", "", ""),
        ("purse-unranked", "CanvasConversion.RegisterFurniture(anchor, renderer, 0);", "{ } // omitted purse", "near purse ranks above the farther character window"),
        ("inscriptions-unranked", "CanvasConversion.RegisterFurniture(anchor, canvas, 1);", "{ } // omitted inscriptions", "near inscriptions rank above the farther character window"),
        ("reversed-distance", "p.OrderDistance > dist + OrderSwapMargin", "p.OrderDistance < dist - OrderSwapMargin", "near purse ranks above the farther character window")]
    cases = []
    fixture = ROOT / "scripts/town-depth-order-runtime"
    for name, before, after, expected in variants:
        build = run / name; production = build / "production"; production.mkdir(parents=True)
        for file, content in bound.items():
            if before and before in content:
                if content.count(before) != 1: raise RuntimeError("Mutation binding drift: " + name)
                content = content.replace(before, after, 1)
            (production / file).write_text(content)
        project = build / "Depth.csproj"
        shutil.copyfile(ROOT / "scripts/town-visitor-motion-runtime/Motion.csproj", project)
        assembly = "TownDepth_" + name.replace("-", "_")
        result = subprocess.run([(shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")), "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production),
            "-p:UnityManaged=" + str(args.unity.parent / "Data/Managed"),
            "-p:UnityUi=" + str(args.source_root / "ressources/GH_Data/Managed/UnityEngine.UI.dll")], capture_output=True, text=True)
        (build / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + "\nCompilation failure cannot pass a mutation control")
        cases.append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    manifest = run / "manifest.json"
    manifest.write_text(json.dumps({"result": str(run / "results.txt"), "cases": cases}, indent=2))
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True); (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    shutil.copyfile(ROOT / "scripts/scenario-scenery-runtime/Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    result = subprocess.run([str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project), "-executeMethod", "InteractionRunner.Start",
        "-interactionManifest", str(manifest), "-logFile", str(run / "unity.log")], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    report = run / "results.txt"
    if report.is_file(): print(report.read_text(), end="")
    if result.returncode or not report.is_file(): raise SystemExit("FAIL Unity run: " + str(run / "unity.log"))
    shutil.rmtree(project)
    for name, *_ in variants:
        for cache in ("bin", "obj"): shutil.rmtree(run / name / cache, ignore_errors=True)
    print("PASS: production town depth and three source-bound mutation controls; " + str(run))


if __name__ == "__main__":
    main()
