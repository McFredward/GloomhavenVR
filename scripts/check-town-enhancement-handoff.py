#!/usr/bin/env python3
"""Exercise the production owned-card enhancement handoff inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-enhancement-handoff-runtime/Boundaries.cs.
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
    path = root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceEnhancementHandoff.cs"
    source = path.read_text()
    return {path.name: source}, {path.name: hashlib.sha256(source.encode()).hexdigest()}


def mutations():
    name = "TownServiceEnhancementHandoff.cs"
    return [
        ("owner", name, "|| !Near(_seat, card.transform.position, .28f) || !ValidOwner(card)", "|| !Near(_seat, card.transform.position, .28f)", "foreign native character refuses offering"),
        ("distance", name, "|| !Near(_seat, card.transform.position, .28f)", "", "distant release refuses offering"),
        ("native-disabled", name, "&& slot.Selectable.IsActive() && slot.Selectable.IsInteractable()", "&& slot.Selectable.IsActive()", "foreign disabled or pending native selection never advertises a palm drop"),
        ("owner-race", name, "if (!Ready || !ValidOwner(card) || _shop.selectedCard == null", "if (!Ready || _shop.selectedCard == null", "native callback owner race refuses offering"),
        ("return", name, "if (card != null && !card.IsHeld && presentation != null) BeginReturn(presentation);", "if (card != null && !card.IsHeld && presentation != null) CardsDriver.RequestRebuild();", "walking away returns original card"),
        ("return-life", name, "finally { presentation.Started = true; PruneReturns(); }", "finally { presentation.Started = true; Returns.Remove(presentation); PruneReturns(); }", "return presentation survives ritual disposal with actual face and fixed identity"),
        ("reclaim", name, "Detach(); ClearNativeSelection();", "Detach();", "manual reclaim clears native options"),
        ("startup", name, "if (!ValidOwner(Card) || !_alive()", "if (!ValidOwner(Card) || !_alive() || !_input()", "opening input fade retains offering"),
        ("head-proximity", name, "bool headEntered = head != null && !_headInside && Near(palm, head.transform.position, 1.4f);", "bool headEntered = false;", "head proximity opens original service without a held card"),
        ("head-close", name, "if (headEntered) _headInside = true;", "", "explicit close remains closed while head stays near"),
        ("card-close", name, "if (cardEntered) _cardInside = true;", "", "explicit close remains closed while card stays near"),
        ("other-service", name, "GuildmasterDestinations.CurrentDestinationMode() != EGuildmasterMode.None", "GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.Enchantress", "proximity never takes over another open service"),
        ("modal", name, "|| Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI", "", "modal confirmation prevents proximity opening"),
        ("empty-palm", name, "&& Card == null && HasAvailableOwnedCard()", "&& Card == null && HeldOwnedCard(VRHands.Left) != null && HasAvailableOwnedCard()", "empty ready palm advertises an owned offering without requiring a held card"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-enhancement-handoff")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--unity-ui", type=Path, help="Real UnityEngine.UI.dll (never metadata-only RefAsm)")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-enhancement-handoff-runtime"
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
