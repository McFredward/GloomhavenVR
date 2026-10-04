#!/usr/bin/env python3
"""Prove cold original town assets and the native publisher census in Unity 2021.3.5.

The observer project imports no town shaders. A separate editor process builds a
bank from the real shader sources, including their actual include dependencies.
Publication, dynamic pool discovery, masks, capture, codec and playback are bound
from production. Native game catalogue construction remains an explicit boundary.
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


def load_binding(root):
    spec = importlib.util.spec_from_file_location("mirror_binding", root / "scripts/check-town-service-mirror.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    bound, hashes = helper.sources(root)
    base = root / "src/GloomhavenVR"
    bound["BundleShaders.cs"] = (base / "Core/BundleShaders.cs").read_text()
    publisher = (base / "WorldUI/TownServices/TownServiceSync.cs").read_text()
    bound["PublisherDynamic622.cs"] = (
        "using UnityEngine; namespace GloomhavenVR.WorldUI; internal sealed partial class TownServiceSync {\n"
        + helper.method(publisher, "private void CollectDynamic(Transform root)") + "\n}\n")
    templates = (base / "WorldUI/TownServices/NativeTemplates.cs").read_text()
    bound["PhysicalPurseWarm622.cs"] = (
        "namespace GloomhavenVR.WorldUI; internal static partial class LazyTemplateProbe {\n"
        + helper.method(templates, "internal static void PreparePhysicalPurses()")
        + "\ninternal static int FixtureEntries => Entries.Count;\n}\n")
    hashes.update({name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()})
    return bound, hashes


def project_layout(project):
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text(
        '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.assetbundle":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-publisher622")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--no-negative-controls", action="store_true")
    parser.add_argument("--only-mutation")
    parser.add_argument("--reuse-bank", type=Path, help="Reuse a previous bank only when every original shader/include hash still matches")
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = run / "fixture"
    shutil.copytree(repo / "scripts/town-service-mirror-runtime", fixture)
    # Only this profile binds the actual dynamic collector. The ordinary mirror
    # harness retains its existing explicit routing-only publisher boundary.
    lazy = fixture / "LazyTemplate.cs"
    lazy.write_text(lazy.read_text().replace("MoneyBagTemplate=>null", "MoneyBagTemplate=>Publisher622PurseTemplate.Source"))
    boundaries = fixture / "Boundaries.cs"
    text = boundaries.read_text()
    if "static void Error(" not in text:
        text = text.replace("internal static bool WantsDebug => false;",
            "internal static bool WantsDebug => false;\ninternal static void Error(string scope,string message) => Messages.Add(scope+\": \"+message);")
        boundaries.write_text(text)
    (run / "fixture-hashes.json").write_text(json.dumps({
        str(path.relative_to(fixture)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in fixture.rglob("*") if path.is_file()}, indent=2) + "\n")
    bound, hashes = load_binding(root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(root), "sha256": hashes}, indent=2) + "\n")
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        variants += [
            ("pooled-native-offer", "PublisherTick.cs", "handoff.Card != null && handoff.OfferedCardId > 0 && handoff.Face != null",
             "handoff.Card != null && handoff.NativeSource != null && handoff.Face != null",
             "offered physical face publishes without a recycled native source widget"),
            ("coarse-upgrade-row", "PublisherTick.cs", "Publish(key, source, prewarm: enhancementRow);",
             "Publish(key, source);", "every pooled original enhancement row bypasses the coarse viewport census"),
            ("native-window-priority", "PublisherTick.cs", "foreach (TownServiceSurface surface in ritual.Surfaces)\n                {\n                    PriorityRoots.Add(surface.Panel.Target);",
             "foreach (TownServiceSurface surface in ritual.Surfaces)\n                {\n                    // original native surface priority omitted",
             "every actual native enhancement inventory holder and decision is prioritized"),
            ("cabinet-page-priority", "PublisherTick.cs", "PriorityRoots.Add(entry.MountRoot); PriorityRoots.Add(entry.FaceRoot);\n                    PriorityRoots.Add(entry.CardRoot); PriorityRoots.Add(entry.BodyRoot);\n                    if (entry.RowContent != null) PriorityRoots.Add(entry.RowContent);",
             "// actual page priority omitted", "every actual current from and to cabinet page receives original artwork priority"),
            ("temple-row-before-body", "PublisherTick.cs", "Publish(service == 2 && piece.Token.IsHeld ? \"ritual.purse.held\" : piece.BodyKey,\n                        piece.Body);\n                    Publish(piece.Key, piece.Content, piece.Source.transform, piece.CloneOf);",
             "Publish(piece.Key, piece.Content, piece.Source.transform, piece.CloneOf);\n                    Publish(service == 2 && piece.Token.IsHeld ? \"ritual.purse.held\" : piece.BodyKey, piece.Body);",
             "first temple publication registers actual body before original inscription artwork"),
            ("cold-shader-find", "TownServiceAssets.cs", "? BundleShaders.Resolve(name, \"TownServices\", \"Original remote town material is available.\",\n                        \"Original remote town material is pending; retry after its asset bank loads.\")",
             "? Shader.Find(name)", "cold asset resolution loads the original town shader through its bank path"),
            ("cold-held-purse", "PhysicalPurseWarm622.cs", 'EnsureNativeProp("ritual.purse.held");', "// held original warm omitted",
             "native purse arrival freezes both body and held addresses before any visitor publishes"),
            ("late-offer-motion", "TownServiceMirror.Motion.cs", "if (root != null) ApplyMotionRoot(module, root.Entry, composed, frame, now);",
             "// late original motion root omitted", "late original offered face adopts the latest exact owner card position and orientation"),
        ]
    if args.only_mutation:
        matches = [variant for variant in variants if variant[0] == args.only_mutation]
        if len(matches) != 1 or args.only_mutation == "production":
            parser.error("--only-mutation must select a negative control")
        variants = variants[:1] + matches
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    manifest = {"result": str(run / "results.txt"), "evidence": str(run), "suite": "publisher622", "cases": []}
    print(f"Production binding: {root}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"; production.mkdir(parents=True)
        for path, content in bound.items():
            if path == filename:
                if content.count(before) != 1: raise RuntimeError("Production mutation binding drift: " + name)
                content = content.replace(before, after, 1)
            (production / path).write_text(content)
        project = build / "Mirror.csproj"; shutil.copyfile(fixture / "Mirror.csproj", project)
        assembly = "TownPublisher622_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
            "-p:DefineConstants=PUBLISHER622", f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}",
            f"-p:UnityUi={root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'}",
            f"-p:UnityTmp={root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll'}"]
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(result.stdout)
        if result.returncode: print(result.stdout); raise SystemExit("FAIL compilation: " + name)
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print("Compiled " + name, flush=True)

    original_shaders = root / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders"
    shader_hashes = {
        str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in original_shaders.iterdir() if path.suffix in (".shader", ".cginc")}
    (run / "shader-source-hashes.json").write_text(json.dumps(shader_hashes, indent=2) + "\n")
    if args.reuse_bank:
        bank = args.reuse_bank.resolve()
        previous_hashes = json.loads((bank.parent.parent / "shader-source-hashes.json").read_text())
        if previous_hashes != shader_hashes or not bank.is_file():
            raise SystemExit("Refusing a reused bank whose original shader/include sources differ.")
        (run / "reused-bank.json").write_text(json.dumps({"path": str(bank), "sha256": hashlib.sha256(bank.read_bytes()).hexdigest()}, indent=2) + "\n")
    else:
        bank_project = run / "bank-project"; project_layout(bank_project)
        bank_shaders = bank_project / "Assets/Bundle/TownServices/Shaders"
        shutil.copytree(original_shaders, bank_shaders, ignore=shutil.ignore_patterns("*.meta"))
        shutil.copyfile(fixture / "Editor/Publisher622Bank.cs", bank_project / "Assets/Editor/Publisher622Bank.cs")
        bank_output = run / "bank"
        command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-buildTarget", "Linux64", "-projectPath", str(bank_project),
            "-executeMethod", "Publisher622Bank.Build", "-publisher622BankOutput", str(bank_output), "-logFile", str(run / "bank-build.log")]
        result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
        bank = bank_output / "publisher622-town.bundle"
        if result.returncode or not bank.exists(): raise SystemExit(f"FAIL cold bank build: see {run / 'bank-build.log'}")
    observer_project = run / "observer-project"; project_layout(observer_project)
    runner = (fixture / "Editor/MirrorRunner.cs").read_text().replace('type.GetMethod("Run")', 'type.GetMethod("RunPublisher622")')
    (observer_project / "Assets/Editor/MirrorRunner.cs").write_text(runner)
    manifest_path = run / "manifest.json"; manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-buildTarget", "Linux64", "-projectPath", str(observer_project),
        "-executeMethod", "MirrorRunner.Start", "-mirrorManifest", str(manifest_path), "-publisher622Bank", str(bank), "-logFile", str(run / "unity.log")]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    evidence = Path(manifest["result"])
    if evidence.exists(): print(evidence.read_text(), end="")
    # Keep the immutable binaries, sources, hashes, bank and diagnostics. Unity
    # caches and duplicate engine references are rebuildable and disk-heavy.
    for generated in (run / "bank-project", observer_project):
        for directory in ("Library", "Temp", "Logs"):
            shutil.rmtree(generated / directory, ignore_errors=True)
    for case in manifest["cases"]:
        owned = Path(case["dll"])
        for output in owned.parent.glob("*"):
            if output.suffix in (".dll", ".pdb", ".xml") and output.stem != owned.stem: output.unlink()
        shutil.rmtree(run / case["name"] / "obj", ignore_errors=True)
    if result.returncode or not evidence.exists(): raise SystemExit(f"FAIL Unity exit {result.returncode}; see {run / 'unity.log'}")
    print(f"PASS cold original assets and native publisher suite; evidence: {run}")


if __name__ == "__main__": main()
