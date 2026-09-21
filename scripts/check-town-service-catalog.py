#!/usr/bin/env python3
"""Compile complete physical merchant catalog, drawer, native transaction and teardown cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-service-catalog-runtime/Boundaries.cs.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def method(source, signature):
    start = source.index("    " + signature)
    end = source.index("\n    }", start) + len("\n    }")
    return source[start:end]


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def sources(root):
    base = root / "src/GloomhavenVR/WorldUI/TownServices"
    names = ["TownServiceCatalog.cs", "TownServiceMerchantRows.cs", "TownServiceMerchantTransaction.cs", "TownServiceMerchantDrawer.cs", "TownServiceMerchantZone.cs", "TownServiceCatalogPreview.cs", "TownServiceWindowMask.cs"]
    bound = {name: (base / name).read_text() for name in names}
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def mutations():
    return [
        ("cap", "TownServiceCatalog.cs", "foreach(var row in _backend.Rows)", "foreach(var row in _backend.Rows.GetRange(0, Math.Min(6,_backend.Rows.Count)))", "all 164 stock and 164 owned entries persist without pagination"),
        ("held-relocation", "TownServiceCatalog.cs", "if (sample.IsMoving) return false", "if (sample.IsMoving && _disposed) return false", "held or returning sample prevents station relocation"),
        ("drawer-relocation", "TownServiceCatalog.cs", "if(drawer.Moving)return false", "if(drawer.Moving && _disposed)return false", "moving drawer prevents workspace relocation"),
        ("closed-pick", "TownServiceCatalog.cs", "inspect: () => drawer.Accessible", "inspect: () => true", "closed opaque drawers prevent picking through cabinet"),
        ("partial-pull", "TownServiceMerchantDrawer.cs", "Travel = .78f", "Travel = .48f", "full pull clears native countertop back rows"),
        ("close-held", "TownServiceMerchantDrawer.cs", "if(_hand==null&&_mayClose())_target=0f", "if(_hand==null)_target=0f", "drawer cannot close on held card"),
        ("context-race", "TownServiceMerchantTransaction.cs", "if (!stillCurrent() || !Eligible(inventory, item, selling)\n            || !confirmation.IsActive", "if (!Eligible(inventory, item, selling)\n            || !confirmation.IsActive", "context race never confirms native callback"),
        ("confirmation-owner", "TownServiceMerchantTransaction.cs", "if (confirmation == null || confirmation.IsActive) return false;", "if (confirmation == null) return false;", "unrelated pending confirmation retained"),
        ("sell-identity", "TownServiceMerchantTransaction.cs", "return inventory.service.GetItemsToSell(inventory.character).Contains(item)", "return true", "stale owned item is ineligible"),
        ("restore", "TownServiceCatalog.cs", "_inventory.transform.SetParent(_nativeHome,false);", "_inventory.transform.SetParent(_nativeWrapper.transform,false);", "opt out restores hidden native inventory hierarchy"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-service-catalog")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--unity-ui", type=Path, help="Real UnityEngine.UI.dll (never metadata-only RefAsm)")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-service-catalog-runtime"
    ui_candidates = [
        args.source_root / "unity/GloomhavenVR.Assets/Library/ScriptAssemblies/UnityEngine.UI.dll",
        args.source_root / "ressources/GH_Data/Managed/UnityEngine.UI.dll",
    ]
    ui = args.unity_ui or next((path for path in ui_candidates if path.is_file()), None)
    if not args.unity.is_file() or ui is None:
        parser.error("Unity 2021.3.5 and a real UnityEngine.UI.dll are required; pass --unity / --unity-ui")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    bound, hashes = sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
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
            (production / path).write_text(text)
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Interaction.csproj", project)
        assembly = "TownInteraction_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "--configuration", "Release", "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}", f"-p:UnityUi={ui.resolve()}"]
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
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    log = run / "unity.log"
    command = [str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(log)]
    completed = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    result = Path(manifest["result"])
    if result.exists():
        print(result.read_text(), end="")
    if completed.returncode or not result.exists():
        print(f"FAIL: Unity exit {completed.returncode}; log: {log}")
        raise SystemExit(1)
    print(f"PASS: {len(variants)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
