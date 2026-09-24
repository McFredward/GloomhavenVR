#!/usr/bin/env python3
"""Compile production workspace allocation, owner motion, material ownership and teardown cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-service-workspace-runtime/Boundaries.cs.
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
    names = ["TownServiceWorkspace.cs", "TownServicePlacement.cs", "TownServiceGrounding.cs", "TownServiceLayout.cs", "TownServiceMerchantCounter.cs"]
    bound = {name: (base / name).read_text() for name in names}
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def mutations():
    return [
        ("ground-support", "TownServiceGrounding.cs", "support.Apply(furnitureBottom);", "support.Restore();", "ground supports reach their sampled terrain"),
        ("reading-fade", "TownServiceWorkspace.cs", "float layoutYaw = frame.eulerAngles.y;", "float layoutYaw = seat.YawDegrees;", "reading-side changes cannot restart unchanged workspace fade"),
        ("environment-divergence", "TownServiceLayout.cs", "radius = ResidentRadius;", "radius = ResidentRadius + (environment == Environment.Forest ? .15f : 0f);", "mixed environment peers resolve identical station poses"),
        ("parchment-frame", "TownServiceLayout.cs", "GloomhavenVR.Rig.VRRigDriver.YawOnly(parchment.rotation)", "Quaternion.identity", "default MR shares original parchment frame with custom rooms"),
        ("no-relocation-revision", "TownServiceWorkspace.cs", "checked { RelocationRevision++; }", "", "only invisible pose change advances relocation revision"),
        ("counter-ring", "TownServiceLayout.cs", "* new Vector3(0f, 0f, radius);", "* new Vector3(0f, 0f, visitor != 0 ? 1.5f : radius);", "open counter stays outside complete native map table diagonal"),
        ("room-frame", "TownServiceLayout.cs", "Quaternion.Euler(0f, room.eulerAngles.y, 0f)", "Quaternion.identity", "room frame matches canonical parchment yaw"),
        ("roster", "TownServiceWorkspace.cs", "player.Id > 0", "player.Id == local", "extra counter clears all three actual resident envelopes"),
        ("visible-teleport", "TownServiceWorkspace.cs", "RelocationVisibility = 0f; ApplyTarget();", "RelocationVisibility = 1f; ApplyTarget();", "pose change has a fully invisible published frame"),
        ("primary", "TownServiceWorkspace.cs", "_shownPrimary ? 0f : _visibility * RelocationVisibility", "_visibility * RelocationVisibility", "primary duplicate is hidden while extensions are visible"),
        ("fifth", "TownServiceWorkspace.cs", 'if (slot > 3) throw new InvalidOperationException("Merchant workspace roster exceeds four users");', "if (slot > 3) slot = 3;", "unexpected fifth user is rejected instead of overlapping a valid seat"),
        ("materials", "TownServiceWorkspace.cs", "copy = new Material(original)", "copy = original", "each workspace owns its materials"),
        ("held-relocation", "TownServiceWorkspace.cs", "_pending && mayRelocate && !_relocating", "_pending && !_relocating", "held or returning card defers relocation"),
        ("floor", "TownServiceWorkspace.cs", "TownServicePlacement.GroundHeight(room, _target)", "room.position.y", "workspace rests on original sloped floor at its own target"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-service-workspace")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--negative-control", action="append", choices=[case[0] for case in mutations()], help="Run production plus selected negative controls")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    parser.add_argument("--bundle", type=Path, help="Immutable final town bundle for actual mesh envelope validation")
    args = parser.parse_args()
    bundle = (args.bundle or args.source_root / "prebuilt/ghvr-town.bundle").resolve()
    bundle_hash = hashlib.sha256(bundle.read_bytes()).hexdigest()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-service-workspace-runtime"
    if not args.unity.is_file():
        parser.error("Unity 2021.3.5 is required; pass --unity")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    bound, hashes = sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes, "bundle": str(bundle), "bundleSha256": bundle_hash}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        variants += [case for case in mutations() if not args.negative_control or case[0] in args.negative_control]
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                text = replace_once(text, before, after)
            (production / path).write_text(text.replace("Time.unscaledTime", "WorkspaceClock.Now"))
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Workspace.csproj", project)
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
    shutil.copyfile(fixture / "Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    log = run / "unity.log"
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-workspaceBundle", str(bundle), "-logFile", str(log)]
    completed = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    if hashlib.sha256(bundle.read_bytes()).hexdigest() != bundle_hash:
        raise SystemExit("FAIL: town bundle changed during envelope validation")
    result = Path(manifest["result"])
    if result.exists():
        print(result.read_text(), end="")
    if completed.returncode or not result.exists():
        print(f"FAIL: Unity exit {completed.returncode}; log: {log}")
        raise SystemExit(1)
    print(f"PASS: {len(variants)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
