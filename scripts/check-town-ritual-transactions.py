#!/usr/bin/env python3
"""Compile production ritual confirmation guards and native-callback race cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Native callback/state boundaries are documented in town-ritual-transaction-runtime/Program.cs.
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
    raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitual.cs").read_text()
    methods = method(raw, "private bool Confirm(") + "\n" + method(raw, "private static bool Click(")
    methods = methods.replace("private bool Confirm(", "internal bool Confirm(")
    start = raw.index("    private bool OfferingEligible(")
    end = raw.index("    private bool Confirm(", start)
    methods += raw[start:end].replace("private bool Donate(", "internal bool Donate(")
    text = "using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;using GloomhavenVR.WorldUI;\n" + \
        "internal sealed class BoundRitual {private readonly Func<bool> _alive;private readonly Func<object?> _context;" + \
        "private readonly HashSet<(string Character,object Blessing)> _submittedOfferings=new();internal FakeTempleOffering _templeOffering=new();" + \
        "internal BoundRitual(Func<bool> alive,Func<object?> context){_alive=alive;_context=context;}\n" + methods + "\n}"
    guard_path = root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualConfirmationGuard.cs"
    if not guard_path.exists():
        guard_path = Path(__file__).resolve().parent.parent / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualConfirmationGuard.cs"
    guard_raw = guard_path.read_text()
    guard = guard_raw[:guard_raw.index("\n[HarmonyPatch")].replace("using HarmonyLib;\n", "")
    return {"RitualTransactions.cs": text, "RitualGuard.cs": guard}, {"TownServiceRitual.cs": hashlib.sha256(raw.encode()).hexdigest(), "TownServiceRitualConfirmationGuard.cs": hashlib.sha256(guard_raw.encode()).hexdigest()}


def mutations():
    return [
        ("repeat-donation", "RitualTransactions.cs", "_submittedOfferings.Add(offering);", "", "a delayed online stock refresh never permits a duplicate donation"),
        ("delayed-validation", "RitualGuard.cs", "_box != null && _valid()", "_box != null", "delayed owner change cancels original transaction"),
        ("delayed-cancel", "RitualGuard.cs", "else cancel?.Invoke();", "else if (!requested) cancel?.Invoke();", "delayed owner change cancels original transaction"),
        ("duplicate-completion", "RitualGuard.cs", "if (_completed) return;", "", "duplicate hidden completion is one shot"),
        ("scope-boundary", "RitualGuard.cs", " || !ReferenceEquals(scope._box, box)", "", "unrelated box retains its native callbacks"),
        ("affordability-race", "RitualTransactions.cs", "if (!_alive() || !eligible() || !button.IsActive()", "if (!_alive() || !button.IsActive()", "post-selection affordability refused"),
        ("owner-race", "RitualTransactions.cs", "!ReferenceEquals(context, _context()) || ", "", "post-selection owner change refused"),
        ("item-race", "RitualTransactions.cs", " || !ReferenceEquals(selected, identity())", "", "post-selection selected item change refused"),
        ("existing-prompt", "RitualTransactions.cs", " || box.GetComponent<UIWindow>().IsOpen || !button.IsInteractable()", " || !button.IsInteractable()", "existing unrelated prompt untouched"),
        ("ownership", "RitualTransactions.cs", "bool created = owns &&", "bool created =", "unowned new callback not confirmed"),
        ("stale-prompt", "RitualTransactions.cs", "if (created) box.Hide();", "if (created) { }", "own stale prompt cancelled through native lifecycle"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-ritual-transaction")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--unity-ui", type=Path, help="Real UnityEngine.UI.dll (never metadata-only RefAsm)")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-ritual-transaction-runtime"
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
