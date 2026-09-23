#!/usr/bin/env python3
"""Compile production room clearance lifecycle and shared station layout cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-service-clearance-runtime/Program.cs.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def sources(root):
    base = root / "src/GloomhavenVR/WorldUI/TownServices"
    names = ["TownServiceRoomClearance.cs", "TownServiceRoomGeometry.cs", "TownServiceLayout.cs"]
    bound = {name: (base / name).read_text() for name in names}
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def mutations():
    return [
        ("tree-shape", "TownServiceRoomGeometry.cs", "_centres[i].x * (factor - 1f)", "_vertices[i].x * (factor - 1f)", "tree translation preserves every triangle edge"),
        ("repeat-expansion", "TownServiceRoomClearance.cs", "if (_room == null || _room.localScale == _last) return;", "if (_room == null) return; if (_room.localScale == _last) { _base = _last; return; }", "repeated updates cannot compound expansion"),
        ("relative-zoom", "TownServiceRoomClearance.cs", "_base *= ratio.y;", "_base = observed;", "relative external zoom cannot apply expansion twice"),
        ("restore", "TownServiceRoomClearance.cs", "if (_room != null) _room.localScale = _base;", "", "reset preserves external zoom before next tick"),
        ("environment-divergence", "TownServiceLayout.cs", "radius = ResidentRadius;", "radius = ResidentRadius + (environment == Environment.Forest ? .2f : 0f);", "environment choice preserves shared layout"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-service-clearance")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    parser.add_argument("--environment-bundle", type=Path, default=repo / "prebuilt/gloomhavenvr.bundle")
    args = parser.parse_args()
    bundle_hash = hashlib.sha256(args.environment_bundle.read_bytes()).hexdigest()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-service-clearance-runtime"
    if not args.unity.is_file():
        parser.error("Unity 2021.3.5 is required; pass --unity")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    bound, hashes = sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes, "environmentBundleSha256": bundle_hash}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        variants += mutations()
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                text = replace_once(text, before, after)
            (production / path).write_text(text.replace("Time.unscaledTime", "ClearanceClock.Now"))
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Clearance.csproj", project)
        assembly = "TownInteraction_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "--configuration", "Release", "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}"]
        compiled = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(compiled.stdout)
        if compiled.returncode:
            print(compiled.stdout)
            raise SystemExit(f"FAIL: {name} did not compile (not a successful negative control)")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print(f"Compiled {name}", flush=True)
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(repo / "scripts/town-service-workspace-runtime/Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    log = run / "unity.log"
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-clearancePoses", str(run / "poses.csv"), "-clearanceBundle", str(args.environment_bundle.resolve()), "-logFile", str(log)]
    completed = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    if hashlib.sha256(args.environment_bundle.read_bytes()).hexdigest() != bundle_hash:
        raise SystemExit("FAIL: environment bundle changed during runtime validation")
    result = Path(manifest["result"])
    if result.exists():
        print(result.read_text(), end="")
    if completed.returncode or not result.exists():
        print(f"FAIL: Unity exit {completed.returncode}; log: {log}")
        raise SystemExit(1)
    print(f"PASS: {len(variants)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
