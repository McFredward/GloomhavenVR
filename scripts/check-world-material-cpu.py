#!/usr/bin/env python3
"""Compare complete actual WorldMaterial.PreCull CPU paths in one Unity/Mono process.

No native-call replacement/observer runs in timed regions. Both-eyes work, actual
Unity objects and source hashes are retained; this is CPU evidence, not Frame FPS.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import random
import shutil
import statistics
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
WORKLOADS = ["representative", "representative-mpb", "representative-wall-mpb", "representative-slot-mpb", "representative-options-debug", "representative-mutations",
             "shared", "shared-two-slot-mpb", "shared-four-slot-mpb", "many-two-slot", "unique", "unique-two-slot-mpb",
             "shared-effect-refused", "unique-effect-refused", "unsupported", "unsupported-two-slot-mpb",
             "shared-standard-two-slot-mpb", "shared-deep", "representative-nested", "excluded", "off"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--baseline-ref", default="d47fa87414c61736076540a4a27db5963f3bcd93")
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/frame650-proof/cpu")
    parser.add_argument("--unity", type=Path, default=Path("/home/claw/unity-2021.3.5/Editor/Unity"))
    parser.add_argument("--workload", action="append")
    parser.add_argument("--rounds", type=int, default=15)
    parser.add_argument("--frames", type=int, default=24)
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = ROOT / "tests/world-material-cpu"
    runtime = ROOT / "tests/world-material-runtime"
    spec = importlib.util.spec_from_file_location("world_runtime", ROOT / "scripts/check-world-material-runtime.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    relative = [str(p.relative_to(root)) for p in sorted((root / "src/GloomhavenVR/Core/Perf").glob("WorldMaterialBudget*.cs"))]
    relative += ["src/GloomhavenVR/Core/Perf/ScenarioEnvironmentBudget.CameraBoundary.cs"]
    baseline_sha = subprocess.check_output(["git", "-C", str(ROOT), "rev-parse", args.baseline_ref], text=True).strip()
    baseline_paths = subprocess.check_output(["git", "-C", str(ROOT), "ls-tree", "-r", "--name-only", baseline_sha,
                                              "src/GloomhavenVR/Core/Perf"], text=True).splitlines()
    baseline_paths = [p for p in baseline_paths if Path(p).name.startswith("WorldMaterialBudget") or p.endswith("ScenarioEnvironmentBudget.CameraBoundary.cs")]
    source_texts = {"baseline": {Path(p).name: subprocess.check_output(["git", "-C", str(ROOT), "show", baseline_sha + ":" + p], text=True) for p in baseline_paths},
                    "candidate": {Path(p).name: (root / p).read_text() for p in relative}}
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    native_hashes = {lane: {name: hashlib.sha256(text.encode()).hexdigest() for name, text in sources.items()} for lane, sources in source_texts.items()}
    prop_path = root / "src/GloomhavenVR/Board/FigureGrab/PropGrab.cs"
    prop_text = prop_path.read_text()
    prop_methods = [module.pure_method(prop_text, signature) for signature in ("internal static bool OwnsRendererOf(Transform? t)", "internal static void CopyVisualRoots(List<GameObject> destination)")]
    prop_source = "using System;using System.Collections.Generic;using UnityEngine;namespace GloomhavenVR.Board.FigureGrab {internal static partial class PropGrab {\n" + "\n".join(prop_methods) + "\n}}"
    boundaries = (runtime / "Boundaries.cs").read_text()
    # The complete native methods stay unchanged. Logging/timing are explicit
    # model boundaries in both lanes; no allocating observers distort the path.
    boundaries = boundaries.replace("return new Quiet();", "return QuietInstance;")
    boundaries = boundaries.replace("bool Missing,Throw=false,AttachmentPixels;", "bool Missing=false,Throw=false,AttachmentPixels=false;")
    boundaries = boundaries.replace("PropCopies,RegistryVisits,MeshReads;", "PropCopies=0,RegistryVisits=0,MeshReads;")
    boundaries = boundaries.replace("internal static readonly Dictionary<string,int> Counts=new();", "private static readonly Quiet QuietInstance=new();\n        internal static readonly Dictionary<string,int> Counts=new();")
    deps = [Path.home() / ".nuget/packages" / relative for relative in ("harmonyx/2.7.0/lib/net45/0Harmony.dll", "monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll", "monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll", "mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll")]
    inputs = [root / p for p in relative] + [prop_path, runtime / "Boundaries.cs", runtime / "Native.shader", runtime / "Bridge.shader", runtime / "World.csproj", Path(__file__).resolve()] + sorted(p for p in fixture.rglob("*") if p.is_file()) + deps
    hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    manifest = {"output": str(run / "samples.jsonl"), "variants": [], "workloads": args.workload or WORKLOADS, "rounds": args.rounds, "frames": args.frames}
    (run / "inputs.json").write_text(json.dumps({"baselineCommit": baseline_sha, "nativeSources": native_hashes, "inputs": hashes, "design": "Complete PreCull direct delegate; actual native APIs; interleaved warmed same-process batches; both eyes per frame; no observer substitutions", "limits": ["Native controllers/config/PerfMonitor remain explicit fixture models.", "Workload topology/materials approximate supplied counts, not original game scenes.", "CPU/allocations only; no GPU, headset FPS or multiplayer acceptance."]}, indent=2) + "\n")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    for lane, sources in source_texts.items():
        case = run / lane
        production = case / "production"
        production.mkdir(parents=True)
        for name, text in sources.items():
            (production / name).write_text(text)
        (production / "PropGrab.VisualRoots.cs").write_text(prop_source)
        (production / "Boundaries.cs").write_text(boundaries)
        shutil.copyfile(runtime / "World.csproj", case / "World.csproj")
        assembly = "WorldCpu_" + lane
        command = [dotnet, "build", str(case / "World.csproj"), "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production), "-p:UnityManaged=" + str(args.unity.parent / "Data/Managed"), "-p:HarmonyPath=" + str(deps[0])]
        result = subprocess.run(command, capture_output=True, text=True)
        (case / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + "\nCompilation failure is not performance evidence")
        manifest["variants"].append({"name": lane, "dll": str(case / "bin/Release/netstandard2.1" / (assembly + ".dll"))})
    (run / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    unity = run / "unity"
    (unity / "Assets/Editor").mkdir(parents=True)
    (unity / "Packages").mkdir()
    (unity / "ProjectSettings").mkdir()
    shutil.copyfile(fixture / "Editor/WorldCpuRunner.cs", unity / "Assets/Editor/WorldCpuRunner.cs")
    shutil.copyfile(runtime / "Bridge.shader", unity / "Assets/Bridge.shader")
    native = (runtime / "Native.shader").read_text()
    for shader in ("Amp_Basic_N_MRAO", "Fixture/UnreviewedWorld"):
        (unity / "Assets" / (shader.replace("/", "_") + ".shader")).write_text(native.replace("Amp_Basic_N_MRAO", shader, 1))
    for dependency in deps:
        shutil.copyfile(dependency, unity / "Assets" / dependency.name)
    (unity / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (unity / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    command = [str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(unity), "-executeMethod", "WorldCpuRunner.Start", "-worldCpuManifest", str(run / "manifest.json"), "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    try:
        result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=600)
    finally:
        for cache in ("Library", "Temp"):
            shutil.rmtree(unity / cache, ignore_errors=True)
    changed = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs if hashlib.sha256(p.read_bytes()).hexdigest() != hashes[str(p)]}
    (run / "source-stability.json").write_text(json.dumps({"unchanged": not changed, "changed": changed}, indent=2) + "\n")
    if changed:
        raise SystemExit("Source changed during benchmark: " + str(run))
    (run / "unity-exit-code.txt").write_text(str(result.returncode) + "\n")
    log = (run / "unity.log").read_text()
    if result.returncode or "Shader error in " in log or not (run / "samples.jsonl").is_file():
        raise SystemExit("Unity benchmark FAIL: " + str(run / "unity.log"))
    records = [json.loads(line) for line in (run / "samples.jsonl").read_text().splitlines() if line.startswith('{')]
    rows = [row for row in records if "variant" in row]
    allocation_valid = all(row["values"]["threadAllocatedPositive"] >= 65536 for row in records if "allocationProbe" in row)
    summary = []
    for workload in manifest["workloads"]:
        pair = {}
        for lane in source_texts:
            samples = [row for row in rows if row["variant"] == lane and row["workload"] == workload]
            if len(samples) != args.rounds:
                raise SystemExit("Incomplete samples for " + workload + " " + lane)
            durations = sorted(value for row in samples for value in row["frameMilliseconds"])
            pair[lane] = {"medianMsPerFrame": statistics.median(row["elapsedMilliseconds"] / row["frames"] for row in samples),
                          "medianBytesPerFrame": statistics.median(row["allocatedBytes"] / row["frames"] for row in samples) if allocation_valid else None,
                          "medianMonoHeapDeltaPerFrame": statistics.median(row["monoHeapDelta"] / row["frames"] for row in samples),
                          "collections": sum(row["collections"] for row in samples),
                          "frameMedianMs": statistics.median(durations), "frameP95Ms": durations[int(.95 * (len(durations) - 1))], "samples": len(samples)}
        change = (pair["candidate"]["medianMsPerFrame"] / pair["baseline"]["medianMsPerFrame"] - 1) * 100
        by_round = {lane: {row["round"]: row["elapsedMilliseconds"] / row["frames"] for row in rows
                          if row["variant"] == lane and row["workload"] == workload} for lane in source_texts}
        ratios = [(by_round["candidate"][index] / by_round["baseline"][index] - 1) * 100 for index in range(args.rounds)]
        generator = random.Random(650)
        boot = sorted(statistics.median(generator.choices(ratios, k=len(ratios))) for _ in range(5000))
        summary.append({"workload": workload, "lanes": pair, "candidateChangePercent": change,
                        "pairedMedianChangePercent": statistics.median(ratios),
                        "pairedBootstrap95Percent": [boot[int(.025 * len(boot))], boot[int(.975 * len(boot))]],
                        "pairedCandidateWins": sum(value < 0 for value in ratios), "allocationApiValid": allocation_valid})
        print(f'{workload:38} {pair["baseline"]["medianMsPerFrame"]:8.4f} -> {pair["candidate"]["medianMsPerFrame"]:8.4f} ms/frame ({change:+.2f}%); heapdelta/frame {pair["baseline"]["medianMonoHeapDeltaPerFrame"]:.0f} -> {pair["candidate"]["medianMonoHeapDeltaPerFrame"]:.0f}; allocationAPI={allocation_valid}')
    (run / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print("MEASURED CPU ONLY: " + str(run))


if __name__ == "__main__":
    main()
