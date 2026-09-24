#!/usr/bin/env python3
"""Compile the open merchant counter, actual pickup, native transaction and teardown cases and run them inside Unity 2021.3.5.

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
    names = ["TownServiceCatalog.cs", "TownServiceCatalogCategory.cs", "TownServiceMerchantRows.cs", "TownServiceMerchantTransaction.cs", "TownServiceMerchantDrawer.cs", "TownServiceMerchantCounter.cs", "TownServiceMerchantZone.cs", "TownServiceCatalogPreview.cs", "TownServiceWindowMask.cs", "TownServiceToken.cs", "TownServiceOfferingPose.cs"]
    bound = {name: (base / name).read_text() for name in names}
    bound["TownRackState.cs"] = (root / "src/GloomhavenVR/Net/TownServices/TownRackState.cs").read_text()
    bound["TownCassetteMotion.cs"] = (root / "src/GloomhavenVR/Net/TownServices/TownCassetteMotion.cs").read_text()
    bound["ItemCardHold.cs"] = (root / "src/GloomhavenVR/Cards/ItemCardHold.cs").read_text()
    bound["UiScrollFocus.cs"] = (root / "src/GloomhavenVR/Hands/Interact/UiScrollFocus.cs").read_text()
    bound["CardGripPose.cs"] = (root / "src/GloomhavenVR/Cards/CardGripPose.cs").read_text()
    vr = (root / "src/GloomhavenVR/Cards/VRCard.cs").read_text()
    constant = next(line.strip() for line in vr.splitlines() if 'internal const float PinchGripFraction =' in line)
    sweep = (root / "src/GloomhavenVR/Cards/FanSweep.cs").read_text()
    interface = sweep[sweep.index('internal interface IFanSweepTarget'):sweep.index('\n}', sweep.index('internal interface IFanSweepTarget')) + 2]
    bound["ItemContracts.cs"] = 'using UnityEngine; namespace GloomhavenVR.Cards { internal static class VRCard { ' + constant + ' }\n' + interface + '\n}'
    contact = (root / "src/GloomhavenVR/Cards/Driver/CardsDriver.3.Laser.cs").read_text()
    signatures = ("internal static void StandDownForItemFanContact", "private static bool TryContactInChips",
        "private struct ContactGeometry", "private static bool TryHandContact(VRHand hand, ItemsPile.ItemChip?",
        "private static bool TryHandContactRect", "private static bool TryProbeRect")
    constants = "\n".join(line for line in contact.splitlines() if "private const float Contact" in line)
    bound["ItemContact.cs"] = "using System.Collections.Generic; using UnityEngine; using GloomhavenVR.Hands; using GloomhavenVR.Hands.Interact; namespace GloomhavenVR.Cards; internal static partial class CardsDriver {\n" + constants + "\n" + "\n".join(method(contact, signature) for signature in signatures) + "\n}"
    chip = (root / "src/GloomhavenVR/Cards/Piles/ItemsPile.cs").read_text()
    chip_start = chip.index("        internal bool TryGetFaceRect")
    chip_end = chip.index("\n        }", chip_start) + len("\n        }")
    bound["ChipContact.cs"] = "using UnityEngine; namespace GloomhavenVR.Cards; internal partial class ItemsPile { internal partial class ItemChip {\n" + chip[chip_start:chip_end] + "\n}}"
    public = (base / "TownServicePublicMerchant.cs").read_text()
    register = next(line for line in public.splitlines() if "UiScrollFocus.PhysicalHoverProbe = ProbeScrollHover;" in line)
    unregister = next(line for line in public.splitlines() if "UiScrollFocus.PhysicalHoverProbe == ProbeScrollHover" in line)
    bound["CabinetProbe.cs"] = "using GloomhavenVR.Hands; using GloomhavenVR.Hands.Interact; using GloomhavenVR.WorldUI.MapRoom; namespace GloomhavenVR.WorldUI; internal static partial class TownServicePublicMerchant { private static TownServiceCatalog? _catalog;\n" + method(public, "private static void ProbeScrollHover") + "\ninternal static void RegisterProbe(TownServiceCatalog catalog) { _catalog = catalog;\n" + register + "\n} internal static void DetachProbe() {\n" + unregister + "\n} }"
    hashes = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}
    return bound, hashes


def mutations():
    return [
        ("roller-static-holders", "TownCassetteMotion.cs", "direction == 0 || progress <= 0f", "direction != 0 || progress <= 0f", "scroll direction moves visible rows vertically in the requested direction"),
        ("roller-wrong-direction", "TownCassetteMotion.cs", "row * RowPitch - direction * RollerLength", "row * RowPitch + direction * RollerLength", "scroll direction moves visible rows vertically in the requested direction"),
        ("roller-no-fold", "TownCassetteMotion.cs", "Quaternion.Euler(angle * Mathf.Rad2Deg, 0f, 0f)", "Quaternion.identity", "every original card face is folded behind its opaque holder at page replacement"),
        ("hidden-page-count", "TownServiceMerchantDrawer.cs", "PageCount > 1 ? opacity : 0f", "0f", "additional stock pages are discoverable without hover"),
        ("late-first-hover", "UiScrollFocus.cs", "PhysicalHoverProbe?.Invoke(hand);", "", "first consumer observes cabinet focus before the presentation tick"),
        ("leaked-hover-probe", "CabinetProbe.cs", "UiScrollFocus.PhysicalHoverProbe = null;", "{ /* deliberately retain the old probe */ }", "disposed public stock unregisters its hover probe"),
        ("parked-takeback-disabled", "TownServiceToken.cs", "&& (_inspect?.Invoke() ?? true)", "&& _offering == null && (_inspect?.Invoke() ?? true)", "parked original stock card retains take-back input while native confirmation is open"),
        ("parked-authority-keeps-prompt", "TownServiceToken.cs", "reclaim?.Invoke();", "", "authority loss cancels a parked stock confirmation through its callback"),
        ("contact-from-a-distance", "ItemContact.cs", "ContactSlabHalfDepthMeters = 0.015f", "ContactSlabHalfDepthMeters = 0.15f", "owned fan uses the normal physical contact slab at every scale"),
        ("world-space-held-pose", "ItemCardHold.cs", "card.localPosition = Vector3.Lerp(card.localPosition, position, t);", "card.position = Vector3.Lerp(card.position, position, t);", "grip pose has no positional trailing while the wrist moves and rotates"),
        ("scroll-without-flight-claim", "TownServiceMerchantDrawer.cs", "UiScrollFocus.NoteScrollHover(hand, _housing, nameof(TownServiceMerchantDrawer));", "", "first consumer observes cabinet focus before the presentation tick"),
        ("scroll-through-ui", "TownServiceMerchantDrawer.cs", "|| hand.RayUgui.HasHit && hand.RayUgui.HitDistance < distance - epsilon", "", "cabinet scroll respects ui before consuming locomotion"),
        ("reverse-scroll-is-forward", "TownServiceMerchantDrawer.cs", "(direction > 0 ? 1 : _availablePages - 1)", "1", "stick up returns to the previous stock page"),
        ("overlapping-stock", "TownServiceMerchantCounter.cs", "ColumnPitch = .18f", "ColumnPitch = .07f", "physical card faces never overlap their adjacent column"),
        ("npc-workspace", "TownServiceMerchantDrawer.cs", "new Vector3(-.95f, .25f, .035f)", "new Vector3(0f, .25f, .035f)", "all stock cards clear the NPC ledger and transaction workspace"),
        ("constructor-rollback", "TownServiceCatalog.cs", "catch { Dispose(); throw; }\n    }\n    internal void SetVisibility", "catch { throw; }\n    }\n    internal void SetVisibility", "constructor failure restores native inventory ownership"),
        ("hidden-stock", "TownServiceCatalog.cs", "internal bool Exposed => Current && (Sample.IsMoving || Page == Rack.Page);", "internal bool Exposed => Current;", "only active category and page are exposed; source identity remains alive"),
        ("stale-prompt", "TownServiceMerchantTransaction.cs", "if (created) confirmation.OnCancel();", "if (created) { }", "own stale item prompt cancelled through native lifecycle"),
        ("cap", "TownServiceCatalog.cs", "foreach(var row in _backend.Rows)", "foreach(var row in _backend.Rows.GetRange(0, Math.Min(6,_backend.Rows.Count)))", "all 164 stock identities remain available in the persistent cabinet"),
        ("held-relocation", "TownServiceCatalog.cs", "if (sample.IsMoving) return false", "if (sample.IsMoving && _disposed) return false", "held or returning sample prevents station relocation"),
        ("held-return", "TownServiceToken.cs", "_physical.localPosition = _homePosition;", "_physical.localPosition = Vector3.zero;", "cancel restores the original counter pose"),
        ("context-race", "TownServiceMerchantTransaction.cs", "if (!stillCurrent() || !Eligible(inventory, item, selling)\n            || !created", "if (!Eligible(inventory, item, selling)\n            || !created", "context race never confirms native callback"),
        ("confirmation-owner", "TownServiceMerchantTransaction.cs", "if (confirmation == null || confirmation.IsActive) return false;", "if (confirmation == null) return false;", "unrelated pending confirmation retained"),
        ("sell-identity", "TownServiceMerchantTransaction.cs", "return inventory.service.GetItemsToSell(inventory.character).Contains(item)", "return true", "stale owned item is ineligible"),
        ("held-rack", "TownServiceCatalog.cs", "() => !_entries.Exists(entry => entry.Sample.IsMoving)", "() => true", "held merchandise prevents rack motion"),
        ("early-tray-swap", "TownServiceMerchantDrawer.cs", "progress >= .5f", "progress >= .01f", "card identity is retained while outgoing front is visible"),
        ("automatic-confirm", "TownServiceMerchantTransaction.cs", "// The player makes the final purchase/sale decision", "ExecuteEvents.Execute(confirmation.confirmButton.gameObject, pointer, ExecuteEvents.pointerClickHandler);\n        // The player makes the final purchase/sale decision", "offering opens confirmation without spending"),
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
