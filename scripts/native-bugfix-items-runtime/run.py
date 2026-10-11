#!/usr/bin/env python3
"""Verify native item-bar repairs and causal controls in the shipped Unity runtime."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
VARIANTS = (
    ("production", "", "", ""),
    ("no-show-repair", "if (inventoryOwner?.Inventory != null)", "if (inventoryOwner?.Inventory == null)", "fixed first presentation uses the supplied actor before its ownership predicate"),
    ("null-only-show", "if (inventoryOwner?.Inventory != null)", "if (___actor == null && inventoryOwner?.Inventory != null)", "fixed stale non-null presentation uses the incoming owner"),
    ("no-replay-repair", "___actor = owner;", "return;", "fixed hidden replay resolves the action's exact native item and owner"),
    ("null-only-replay", "___actor = owner;", "if (___actor == null) ___actor = owner;", "fixed hidden replay resolves the action's exact native item and owner"),
    ("ignore-vr", "!VRSession.IsRunning", "VRSession.IsRunning && !VRSession.IsRunning", "VR-off first presentation retains the original failure"),
    ("ignore-item-identity", "!owner.Inventory.AllItems.Exists(item => item.NetworkID == token.ItemNetworkID)", "false", "token absent from action actor inventory leaves native handling untouched"),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/native-bugfix-items-runtime")
    parser.add_argument("--case", action="append", choices=[v[0] for v in VARIANTS])
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    source_path = args.source_root / "src/GloomhavenVR/Compat/NativeBugFixes.Items.cs"
    source = source_path.read_text()
    fixture = ROOT / "scripts/native-bugfix-items-runtime"
    managed = args.source_root / "ressources/GH_Data/Managed"
    unity = Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity"))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    variants = [v for v in VARIANTS if not args.case or v[0] in args.case or v[0] == "production"]
    manifest = {"result": str(run / "results.txt"), "managed": str(managed), "cases": []}
    (run / "sources.json").write_text(json.dumps({
        "production_path": str(source_path), "sha256": hashlib.sha256(source.encode()).hexdigest(),
        "native_sha256": {name: hashlib.sha256((managed / name).read_bytes()).hexdigest()
            for name in ("GH.Runtime.dll", "ScenarioRuleLibrary.dll", "PhotonBolt.dll")},
        "external_seams": ["VR state/logging", "FFSNetwork.IsOnline=true without a Bolt session",
            "ShowItems downstream widget creation", "UseItemService downstream native phase/UI queue",
            "visible slot OnPointerDown downstream click", "original element-reservation UI calls",
            "authoritative infusion and its UI update downstream of native replay"],
        "scope": "Original native actor predicates, UIUseItemsBar ShowUsableItems, both ProxyUseItemBonus overloads, inventories, actor-ID resolution and token data execute; no live multiplayer or complete rule execution claim."
    }, indent=2) + "\n")
    for name, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        value = source
        if before:
            assert before in value, "mutation binding drift: " + name
            value = value.replace(before, after)
        (production / "Items.cs").write_text(value)
        project = build / "Items.csproj"
        shutil.copyfile(fixture / "Items.csproj", project)
        assembly = "NativeItemRepairs_" + name.replace("-", "_")
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
        if not dep.name.startswith("NativeItemRepairs_"):
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
