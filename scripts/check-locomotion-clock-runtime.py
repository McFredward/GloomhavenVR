#!/usr/bin/env python3
"""Run production world grab/turn with native Unity's paused and tracking clocks.

Device samples, modes, configuration and notification sinks are declared ports;
movement, ownership, scroll gates, clocks and transform calculations are production
or native Unity. Old-source controls must fail a behavior assertion, not compile.
This does not establish the cause or absence of a headset-only input freeze.
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
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/locomotion-clock-runtime")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--case", action="append", help="Run a focused subset without repeating unchanged evidence")
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = root / "scripts/locomotion-clock-runtime"
    managed = root / "ressources/GH_Data/Managed"
    paths = ["Rig/WorldGrab.cs", "Rig/SnapTurn.cs", "Rig/ScrollTurnGate.cs", "Rig/Comfort.MotionDiagnostics.cs", "Hands/Interact/UiScrollFocus.cs"]
    sources = {Path(path).name: (root / "src/GloomhavenVR" / path).read_text() for path in paths}
    variants = [("production", "", "", "", "")]
    variants += [
        ("old-drag-clock", "WorldGrab.cs", "PositionSmoothing * Time.unscaledDeltaTime", "PositionSmoothing * Time.deltaTime", "paused one-hand drag remains movable"),
        ("old-two-hand-clock", "WorldGrab.cs", "RotateScaleSmoothing * Time.unscaledDeltaTime", "RotateScaleSmoothing * Time.deltaTime", "paused two-hand rotate and scale remain movable"),
        ("old-turn-clock", "SnapTurn.cs", "ComfortSettings.SmoothTurnSpeed.Value * Time.unscaledDeltaTime", "ComfortSettings.SmoothTurnSpeed.Value * Time.deltaTime", "paused smooth turn remains movable"),
    ]
    if args.case:
        if set(args.case) - {entry[0] for entry in variants}: parser.error("Unknown case")
        variants = [entry for entry in variants if entry[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    archived_fixture = run / "fixture"
    shutil.copytree(fixture, archived_fixture)
    fixture = archived_fixture
    manifest = {"result": str(run / "results.txt"), "cases": []}
    hashes = {}
    for name, filename, old, new, expected in variants:
        build = run / name; production = build / "production"; production.mkdir(parents=True)
        hashes[name] = {}
        for path, source in sources.items():
            if path == filename:
                assert source.count(old) == 1, "Causal source binding drift: " + name
                source = source.replace(old, new, 1)
            (production / path).write_text(source)
            hashes[name][path] = hashlib.sha256(source.encode()).hexdigest()
        project = build / "Clock.csproj"; shutil.copyfile(fixture / "Clock.csproj", project)
        assembly = "Clock_" + name.replace("-", "_")
        result = subprocess.run([shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet"), "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production), "-p:UnityManaged=" + str(args.unity.parent / "Data/Managed"), "-p:GameManaged=" + str(managed.resolve())], capture_output=True, text=True)
        (build / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + "\nCompilation failure is not a causal pass")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    project = run / "unity"
    for name in ("Assets/Editor", "Packages", "ProjectSettings"): (project / name).mkdir(parents=True)
    shutil.copyfile(fixture / "Editor/ClockRunner.cs", project / "Assets/Editor/ClockRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    path = run / "manifest.json"; path.write_text(json.dumps(manifest, indent=2) + "\n")
    (run / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    (run / "fixture-hashes.json").write_text(json.dumps({str(path.relative_to(fixture)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in fixture.rglob("*") if path.is_file()}, indent=2) + "\n")
    print("Evidence: " + str(run), flush=True)
    try:
        result = subprocess.run(["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(project), "-executeMethod", "ClockRunner.Start", "-clockManifest", str(path), "-logFile", str(run / "unity.log")], timeout=180)
    finally:
        shutil.rmtree(project, ignore_errors=True)
    report = Path(manifest["result"])
    if report.is_file(): print(report.read_text(), end="")
    if result.returncode or not report.is_file(): raise SystemExit("FAIL native locomotion clocks: " + str(run / "unity.log"))
    print(("PARTIAL PASS" if args.case else "PASS") + ": native clocks and " + str(len(variants)) + " variants; " + str(run))


if __name__ == "__main__": main()
