#!/usr/bin/env python3
"""Compile production catalog paging, physical layout and teardown cases and run them inside Unity 2021.3.5.

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
    names = ["TownServiceCatalog.cs", "TownServiceCatalogPointer.cs", "TownServiceCatalogPreview.cs", "TownServiceWindowMask.cs", "TownServiceSurface.cs"]
    bound = {name: (base / name).read_text() for name in names}
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def mutations():
    return [
        ("desktop-owned", "TownServiceCatalog.cs", "if (inventory._ownedFilter != null)", "if (true)", "desktop merchant opens without a gamepad Owned filter"),
        ("hover-rebind", "TownServiceCatalog.cs", " || (enter && !Current)", " || !Current", "row rebind retires previous native hover"),
        ("hint-duplicate", "TownServiceCatalogPreview.cs", "_hintMask = new TownServiceWindowMask(rect);", "// mutation leaves duplicate native tooltip visible", "native shared hint is masked while its copy is visible"),
        ("preview-mask", "TownServiceCatalogPreview.cs", "_mount.transform.SetParent(parent, false)", "_mount.transform.SetParent(source.transform.parent, false)", "detail clone escapes hidden native viewport"),
        ("preview-leave", "TownServiceCatalogPreview.cs", "_source.IsShown &&", "true &&", "native hover exit hides copied details"),
        ("preview-context", "TownServiceCatalog.cs", "&& _inventory.itemTooltip.m_ItemCardUI.item.ID == entry.Item.ID", "&& true", "rebound tooltip item cannot appear under new row identity"),
        ("hint-pointer", "TownServiceCatalogPreview.cs", "{ pointerEnter = target.gameObject }", "", "native exact-target tooltip needs pointerEnter"),
        ("hint-enter", "TownServiceCatalogPreview.cs", "target.OnPointerEnter(new PointerEventData(EventSystem.current) { pointerEnter = target.gameObject });", "// mutant omits native inspect", "native rules target entered exactly once"),
        ("hint-owner", "TownServiceCatalogPreview.cs", "tooltip.m_AnchorToTarget == _hintTarget.transform && EventSystem.current != null", "EventSystem.current != null", "ending inspect never hides another native tooltip"),
        ("pool-input", "TownServiceCatalog.cs", "target.Key.raycastTarget = target.Value", "target.Key.raycastTarget = false", "pooled native input flags restored before recycle"),
        ("surface-fade", "TownServiceSurface.cs", "_gate.alpha = alpha * _visibility", "_gate.alpha = alpha", "counter visibility multiplies original native fade"),
        ("scroll-repeat", "TownServiceCatalog.cs", "if (Time.unscaledTime < _nextScrollPage) return;", "if (Time.unscaledTime < -1f) return;", "page rebuild preserves scroll repeat throttle"),
        ("page", "TownServiceCatalog.cs", "SetPage(_page + direction, true)", "SetPage(_page, true)", "physical next page advances entries"),
        ("restore", "TownServiceCatalog.cs", "_scroll.viewport.SetParent(_listHome, false);", "// mutation leaves original viewport in hidden wrapper", "off removes viewport suppression immediately"),
        ("identity", "TownServiceCatalog.cs", "&& ReferenceEquals(Item, RowSource.Item)", "&& true", "rebound row immediately fences stale sample"),
        ("rotation", "TownServiceCatalog.cs", "_display.localRotation = Quaternion.Euler(65f, 0f, 0f)", "_display.localRotation = Quaternion.Euler(90f, 0f, 0f)", "physical card is raised toward the customer"),
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
