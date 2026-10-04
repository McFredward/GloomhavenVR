#!/usr/bin/env python3
"""Exercise private-only VR card geometry suppression against shipped FullAbilityCard."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
VARIANTS = (
    ("production", "", "", ""),
    ("skip-disabled", "=> !AdoptedCardLayout.OwnsGeometry(__instance);", "=> true;", "adopted VR geometry avoids flat transform writes and repair churn"),
    ("ignore-inactive-owner", "includeInactive: true", "includeInactive: false", "adopted VR geometry avoids flat transform writes and repair churn"),
    ("stale-parent", "return owner != null && owner.HasAdoptedFace && ReferenceEquals(owner.FullCard, face);", "return true;", "stale adopted registry yields native dialog geometry immediately"),
    ("mismatched-face", "&& ReferenceEquals(owner.FullCard, face)", "", "mismatched original face ownership retains native geometry"),
    ("ignore-vr-off", "!VRSession.IsRunning || ", "", "flat and VR-off modes retain native geometry"),
    ("ignore-module-off", "!HandSuppression.Active || ", "", "cards module teardown retains native geometry"),
    ("hide-native-failure", "if (face!.ViewSettings == null)", "if (face!.ViewSettings == null && !VRSession.IsRunning)", "invalid native settings preserve the original failure rather than hiding it"),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/native-card-layout-runtime")
    parser.add_argument("--case", action="append", choices=[v[0] for v in VARIANTS])
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    source_path = args.source_root / "src/GloomhavenVR/Cards/Patches/AdoptedCardLayoutPatches.cs"
    source = source_path.read_text()
    module = (args.source_root / "src/GloomhavenVR/Cards/CardsModule.cs").read_text()
    for patch in ("FullAbilityCard_UpdateScale_AdoptedLayout", "FullAbilityCard_UpdatePosition_AdoptedLayout"):
        assert "VRSession.Harmony?.PatchAll(typeof(" + patch + "));" in module, "production patch registration"
    fixture = ROOT / "scripts/native-card-layout-runtime"
    managed = args.source_root / "ressources/GH_Data/Managed"
    unity = Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity"))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    variants = [v for v in VARIANTS if not args.case or v[0] in args.case or v[0] == "production"]
    (run / "sources.json").write_text(json.dumps({
        "production_path": str(source_path), "sha256": hashlib.sha256(source.encode()).hexdigest(),
        "registration_sha256": hashlib.sha256(module.encode()).hexdigest(),
        "native_sha256": hashlib.sha256((managed / "GH.Runtime.dll").read_bytes()).hexdigest(),
        "external_seams": ["VR mode, hand-suppression state and adoption registry/VRCard ownership; actual shipped transform writers, settings, events and Unity hierarchy are executed."],
    }, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "managed": str(managed), "cases": []}
    for name, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        value = source
        if before:
            assert before in value, "mutation binding drift: " + name
            value = value.replace(before, after)
        (production / "Layout.cs").write_text(value)
        project = build / "Layout.csproj"
        shutil.copyfile(fixture / "Layout.csproj", project)
        assembly = "NativeCardLayout_" + name.replace("-", "_")
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production),
            "-p:UnityManaged=" + str(unity.parent / "Data/Managed"), "-p:GameManaged=" + str(managed)], capture_output=True, text=True)
        (build / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + "\nCompilation failure is not a passing negative control")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    dependencies = run / "dependencies"
    dependencies.mkdir()
    for dep in (run / variants[0][0] / "bin/Release/netstandard2.1").glob("*.dll"):
        if not dep.name.startswith("NativeCardLayout_"):
            shutil.copyfile(dep, dependencies / dep.name)
    manifest["dependencies"] = str(dependencies)
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(ROOT / "scripts/desktop-render-runtime/Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    command = [str(unity), "-batchmode", "-nographics", "-projectPath", str(project), "-executeMethod", "InteractionRunner.Start",
        "-interactionManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    try:
        result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    finally:
        for cache in ("Library", "Temp"):
            shutil.rmtree(project / cache, ignore_errors=True)
    report = Path(manifest["result"])
    if report.is_file():
        print(report.read_text(), end="")
    (run / "unity-exit-code.txt").write_text(str(result.returncode) + "\n")
    if result.returncode or not report.is_file():
        raise SystemExit("FAIL: Unity run; see " + str(run / "unity.log"))
    print("PASS:", len(variants), "complete production/negative variants; evidence:", run)


if __name__ == "__main__":
    main()
