#!/usr/bin/env python3
"""Bind full native quiet preparation/masking/release in Unity; game callbacks are explicit boundaries.

This verifies owner lifecycle and exact original callback routing, not native payment
rules, headset appearance or transport. The handoff fixtures keep their original
buy/sell/enhance/cancel/replacement matrices separately.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def sources(root):
    names = ["TownServiceQuietController.cs", "TownServiceWindowMask.cs"]
    bound = {name: (root / "src/GloomhavenVR/WorldUI/TownServices" / name).read_text() for name in names}
    registration = (root / "src/GloomhavenVR/WorldUI/WorldUIModule.cs").read_text()
    # The runtime explicitly invokes private Harmony entry points, so independently
    # prove the shipping module installs them. A correct unused patch is no fix.
    check_registration(registration)
    hashes = {name: hashlib.sha256(value.encode()).hexdigest() for name, value in bound.items()}
    hashes["WorldUIModule.cs"] = hashlib.sha256(registration.encode()).hexdigest()
    return bound, hashes


def check_registration(source):
    patches = ["QuietMerchantBuyRefresh", "QuietMerchantSellRefresh", "QuietEnhancementBuyRefresh", "QuietEnhancementSellRefresh", "QuietTempleProxyPresentation"]
    for name in patches:
        expected = "VRSession.Harmony?.PatchAll(typeof(" + name + "));"
        executable = re.sub(r"//[^\n]*|/\*.*?\*/", "", source, flags=re.S)
        if executable.count(expected) != 1:
            raise RuntimeError("Original quiet callback must be installed once: " + name)
        mutant = source.replace(expected, "/* missing quiet patch */", 1)
        if re.sub(r"//[^\n]*|/\*.*?\*/", "", mutant, flags=re.S).count(expected) != 0:
            raise RuntimeError("Registration causal control failed: " + name)


def mutations():
    return [
        ("flat-show", "TownServiceQuietController.cs", "            _owner = window; _party = party; _character = character;", "            window.Show(); _owner = window; _party = party; _character = character;", "quiet merchant preserves flat window and exact party slot"),
        ("merchant-original-init", "TownServiceQuietController.cs", "                shop.ItemInventory.Init(nativeService, character);", "                /* skipped original inventory init */", "original merchant inventory receives current party and character exactly once"),
        ("merchant-event-release", "TownServiceQuietController.cs", "            if (owner != null && owner.GetComponent<UIShopItemWindow>() is UIShopItemWindow merchant)\n                Invoke(merchant, \"ClearEvents\");", "            /* missing original merchant event teardown */", "merchant release unregisters every exact original bus callback"),
        ("mage-current-party", "TownServiceQuietController.cs", "nativeService == null || !ReferenceEquals(Field(nativeService, \"party\").GetValue(nativeService), party)", "nativeService == null", "mage entry uses the current adventure party after save switch"),
        ("mage-original-slots", "TownServiceQuietController.cs", "            assigned.Add(cards[i], pool[i]);", "            /* omitted native assigned slot */", "every owned ability keeps its original assigned slot callback"),
        ("mage-original-mode", "TownServiceQuietController.cs", "                if (shop.buyButton.isOn) Invoke(shop, \"ShowBuyOptions\");\n                else shop.buyButton.Activate();", "                /* skipped original buy tab and mode callback */", "mage buy mode and original tab are activated without a flat open"),
        ("mage-last-shown", "TownServiceQuietController.cs", "                Field(shop, \"lastShowedCard\").SetValue(shop, null);", "                /* retained old native shown card */", "mage entry clears stale native shown card before its original buy tab callback"),
        ("mage-sell-tab", "TownServiceQuietController.cs", "                shop.sellButton.gameObject.SetActive(nativeService.IsSellAvailable);", "                /* retained old native sell tab */", "mage entry restores original selling and buy-tab availability from its current native service"),
        ("mage-buy-tab", "TownServiceQuietController.cs", "                shop.buyButton.interactable = nativeService.IsSellAvailable;", "                /* retained old native buy tab interactivity */", "mage entry restores original selling and buy-tab availability from its current native service"),
        ("mage-old-points-warning", "TownServiceQuietController.cs", "        display.ShowWarningPoints(false);", "        /* retained previous native points warning */", "original new ability pool clears previous enhancement points warning"),
        ("cleanup-exception-escape", "TownServiceQuietController.cs", "            if (owner != null) ReportProxyFailure(owner, error);", "            if (owner != null) ReportProxyFailure(owner, error); throw;", "native fixture teardown failed"),
        ("source-rect-restore", "TownServiceWindowMask.cs", "            ReparentPreservingLocalRect(_source, _parent);", "            /* dropped native rect restoration */", "merchant release restores exact source parent and local rect geometry"),
        ("proxy-validation", "TownServiceQuietController.cs", "        if (!valid || !ReferenceEquals(_owner, shop.GetComponent<UIWindow>())", "        if (!ReferenceEquals(_owner, shop.GetComponent<UIWindow>())", "invalid native proxy never refreshes or reapplies an enhancement"),
        ("visible-proxy-double-feedback", "TownServiceQuietController.cs", "            || _owner!.IsVisible\n", "", "visible native proxy owns its existing feedback without a duplicate quiet refresh"),
    ]


def main(fixture_dir=None):
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-quiet-controller")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--unity-ui", type=Path)
    parser.add_argument("--no-negative-controls", action="store_true")
    parser.add_argument("--negative-control", action="append", default=[])
    args = parser.parse_args()
    fixture = fixture_dir or repo / "scripts/town-quiet-controller-runtime"
    ui = args.unity_ui or next((path for path in [args.source_root / "unity/GloomhavenVR.Assets/Library/ScriptAssemblies/UnityEngine.UI.dll", args.source_root / "ressources/GH_Data/Managed/UnityEngine.UI.dll"] if path.is_file()), None)
    if not args.unity.is_file() or ui is None:
        parser.error("Unity 2021.3.5 and its real UnityEngine.UI.dll are required")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    bound, hashes = sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    variants = [("production", None, None, None, "")]
    controls = mutations()
    unknown = set(args.negative_control) - {case[0] for case in controls}
    if unknown: parser.error("Unknown controls: " + ", ".join(sorted(unknown)))
    if not args.no_negative_controls:
        variants += [case for case in controls if not args.negative_control or case[0] in args.negative_control]
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    manifest = {"result": str(run / "results.txt"), "cases": []}
    print("Binding complete native quiet controller and real window mask; evidence: " + str(run), flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, content in bound.items():
            if path == filename:
                if content.count(before) != 1: raise RuntimeError("Native source binding drift: " + name)
                content = content.replace(before, after, 1)
            (production / path).write_text(content)
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Interaction.csproj", project)
        assembly = "NativeQuiet_" + name.replace("-", "_")
        compiled = subprocess.run([dotnet, "build", str(project), "--configuration", "Release", "--nologo", "--verbosity", "quiet", f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}", f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}", f"-p:UnityUi={ui.resolve()}"], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(compiled.stdout)
        if compiled.returncode:
            print(compiled.stdout)
            raise SystemExit("FAIL: " + name + " did not compile; not a successful negative control")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print("Compiled " + name, flush=True)
    path = run / "manifest.json"
    path.write_text(json.dumps(manifest, indent=2) + "\n")
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    runner = fixture / "Editor/InteractionRunner.cs"
    if not runner.is_file():
        runner = repo / "scripts/town-quiet-controller-runtime/Editor/InteractionRunner.cs"
    shutil.copyfile(runner, project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    done = subprocess.run([str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project), "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(path), "-logFile", str(run / "unity.log")], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    result = Path(manifest["result"])
    if result.exists(): print(result.read_text(), end="")
    if done.returncode or not result.exists(): raise SystemExit("FAIL: Unity exit " + str(done.returncode) + "; " + str(run / "unity.log"))
    print("PASS full quiet owner lifecycle: " + str(len(variants)) + " variants; " + str(run))


if __name__ == "__main__": main()
