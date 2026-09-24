#!/usr/bin/env python3
"""Exercise the production merchant palm handoff and owned-item inspection inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-merchant-handoff-runtime/Boundaries.cs.
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
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        if source[end] == "{": depth += 1
        if source[end] == "}": depth -= 1
        end += 1
    return source[start:end]


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def sources(root):
    paths = ["src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantHandoff.cs",
             "src/GloomhavenVR/WorldUI/TownServices/TownServiceOfferingPose.cs",
             "src/GloomhavenVR/Cards/Piles/ItemsPile.Merchant.cs",
             "src/GloomhavenVR/WorldUI/MapRoom/MapRoomHand.5.Merchant.cs"]
    bound = {Path(p).name: (root / p).read_text() for p in paths}
    face = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceFace.cs").read_text()
    attention = method(face, "internal bool IsLocalVisitorNear(bool wasNear)")
    bound["ActualAttention.cs"] = "using UnityEngine; namespace GloomhavenVR.WorldUI { internal class ActualAttention { public Transform _root = null!; public Eye _rig = new(); public class Eye { public Vector3 EyePosition; public Quaternion OpticalRotation = Quaternion.identity; } " + attention + " } }"
    pile = (root / "src/GloomhavenVR/Cards/Piles/ItemsPile.cs").read_text()
    start = pile.index("        public bool AllowsHand(VRHand hand) =>")
    gate = pile[start:pile.index(";", start) + 1]
    bound["ActualItemGate.cs"] = "using System; using GloomhavenVR.Hands; namespace GloomhavenVR.Cards { internal sealed partial class ItemsPile { internal partial class ItemChip { " + gate + " } } }"
    layout = method(pile, "private void Relayout()")
    # Rename only the symbol so the boundary wrapper can count actual production layout calls.
    layout = layout.replace("private void Relayout()", "private void ProductionRelayout()", 1)
    lifecycle = "\n".join(method(pile, signature) for signature in [
        "internal void SetHome(Vector3 pos, Quaternion rot, float scale)",
        "internal void BeginEmerge(Vector3 localConverge, float delay, float spinSign)",
        "internal void BeginCollapse(Vector3 worldConverge, float delay = 0f, float spinSign = 1f)",
        "private void TickEmerge(float dt, Vector3 posTarget, float scaleTarget)",
        "private static float EaseOutBack(float t, float s)"])
    bound["ActualItemLifecycle.cs"] = "using UnityEngine; namespace GloomhavenVR.Cards { internal sealed partial class ItemsPile { " + layout + " internal partial class ItemChip { " + lifecycle + " } } }"
    hashes = {p: hashlib.sha256(s.encode()).hexdigest() for p, s in bound.items()}
    return bound, hashes


def mutations():
    return [
        ("offering-flat", "TownServiceOfferingPose.cs", "facing * Quaternion.Euler(0f, 1.5f * Mathf.Sin(age * .9f), 0f)", "palm.rotation * Quaternion.Euler(90f, 0f, 0f)", "offering overlay is upright over the palm"),
        ("offering-static", "TownServiceOfferingPose.cs", ".006f * Mathf.Sin(age * 1.8f)", "0f", "offering suspension has visible gentle continuous motion"),
        ("offering-retirement", "ItemsPile.Merchant.cs", "!chip.TownOffering &&", "", "closed wrist fan retains actual pending offering"),
        ("remote-owner", "MapRoomHand.5.Merchant.cs", "(!FFSNetwork.IsOnline || character.IsUnderMyControl)", "true", "remote character cannot open an owned-item fan"),
        ("palm-bypass", "TownServiceMerchantHandoff.cs", "!Eligible(item, selling, cached: false) || !InOfferingZone(world)", "!Eligible(item, selling, cached: false)", "release outside palm cannot open merchant"),
        ("inventory-cap", "TownServiceMerchantHandoff.cs", "Items.AddRange(current);", "Items.AddRange(current.GetRange(0, 1));", "all equipped and bound copies become actual inspection cards"),
        ("stale-native", "TownServiceMerchantHandoff.cs", "!ReferenceEquals(inventory.character, _character)", "false", "native inventory for another character cannot receive offer"),
        ("auto-approach", "TownServiceMerchantHandoff.cs", "_nextItems = 0f;", "_nextItems = 0f; MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Merchant, \"mutant\");", "approach never opens a native service"),
        ("return-dropped", "ItemsPile.Merchant.cs", "_inspectionPublished.AddRange(_inspectionRetiring);", "", "closing animation remains published until completion"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-merchant-handoff")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--unity-ui", type=Path, help="Real UnityEngine.UI.dll (never metadata-only RefAsm)")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-merchant-handoff-runtime"
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
        variants += [("historical-census", "ItemsPile.Merchant.cs", None, None, "stable membership never rereads the inventory census")]
    if not args.no_negative_controls:
        variants += mutations()
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if name == "historical-census":
                    text = (fixture / "ItemsPile.Merchant.pre-optimization.fixture").read_text()
                    # Preserve the historical quadratic census, adapting only its new parked-card API.
                    text = text.replace("if (chip.Holder == null && (", "if (chip.Holder == null && !chip.TownOffering && (")
                    text = text.replace("    private void RetireInspectionAt", method(bound["ItemsPile.Merchant.cs"], "internal void ResumeInspection(ItemChip chip)") + "\n    private void RetireInspectionAt")
                    text = text.replace("TickInspection(IReadOnlyList<CItem> items)", "TickInspection(IReadOnlyList<CItem> items, uint revision)")
                    # Compatibility-only field consumed by the unchanged release boundary.
                    text = text.replace("private VRHand? _inspectionGateHand;", "private VRHand? _inspectionGateHand; private bool _inspectionCensusDirty;")
                else:
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
    if log.exists():
        for line in log.read_text(errors="replace").splitlines():
            if line.startswith("MERCHANT_HOST "): print(line)
    if completed.returncode or not result.exists():
        print(f"FAIL: Unity exit {completed.returncode}; log: {log}")
        raise SystemExit(1)
    print(f"PASS: {len(variants)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
