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
    pose = path.with_name("TownServiceOfferingPose.cs")
    feedback = path.with_name("TownServiceOfferFeedback.cs")
    mask = path.with_name("TownServiceNativeEnhancementCardMask.cs")
    bound = {path.name: source, pose.name: pose.read_text(), feedback.name: feedback.read_text(),
             mask.name: mask.read_text()}
    poke = (root / "src/GloomhavenVR/Hands/Interact/PokeInteractor.cs").read_text()
    bound["PokeInteractor.cs"] = poke
    card = (root / "src/GloomhavenVR/Cards/VRCard.cs").read_text()
    start = card.index("    public override bool CanGrab =>")
    gate = card[start:card.index(";", start) + 1]
    grab = (root / "src/GloomhavenVR/Hands/Interact/ProximityGrabber.cs").read_text()
    force = method(grab, "public bool ForceGrab(")
    bound["ActualGrabRoute.cs"] = "using System; using GloomhavenVR.Hands; using GloomhavenVR.Hands.Interact; namespace GloomhavenVR.Cards { public partial class VRCard { " + gate + " } } namespace GloomhavenVR.Hands { public partial class Holder { " + force + " } }"
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def check_presentation_bridge(root):
    """The resident must acquire both native windows before replacing their hand surfaces."""
    source = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServicePresentation.cs").read_text()
    tick = source[source.index("    private static void TickCore()"):
                  source.index("    private static ConvertedPanel? FindContext(")]
    def has_temple_approach(body):
        mage = body.find("TownServiceEnhancementHandoff.TickApproach();")
        temple = body.find("TownServiceTempleOffering.TickApproach();")
        return mage >= 0 and temple > mage

    if not has_temple_approach(tick):
        raise RuntimeError("Native temple approach is not polled beside enchantress approach")
    property_body = source[source.index("internal static bool UsesImmersiveEnhancement =>"):
                           source.index(";", source.index("internal static bool UsesImmersiveEnhancement =>"))]
    def has_mage_ownership(body):
        return "_enhancementListMask != null" in body and "Service == 3" in body

    if not has_mage_ownership(property_body):
        raise RuntimeError("Enchantress native card list is released before immersive ownership is established")
    # Each checker must reject the defect it guards. An assertion that also accepts
    # its own planted regression would provide no useful integration evidence.
    if has_temple_approach(tick.replace("TownServiceTempleOffering.TickApproach();", "", 1)):
        raise RuntimeError("Temple approach negative control was accepted")
    if has_mage_ownership(property_body.replace("_enhancementListMask != null", "true", 1)):
        raise RuntimeError("Mage ownership negative control was accepted")


def mutations():
    name = "TownServiceEnhancementHandoff.cs"
    return [
        ("walkaway-window", name, "ModalFallback.CloseFloatedWindow(current._window);", "", "walking away closes empty native service through its existing exit path"),
        ("tiny-offer", name, "card.SetHome(_seat, Vector3.zero, Quaternion.identity, size);", "card.SetHome(_seat, Vector3.zero, Quaternion.identity, .90f);", "offered mage card preserves tracked reading size across independent resident scale"),
        ("reclaim-modal", name, "&& _current.ReclaimReady", "&& _current.Ready", "actual routed grab reclaims mage card through owned native confirmation"),
        ("flat-card", name, "card.SetHome(_seat, Vector3.zero, Quaternion.identity, size);", "card.SetHome(_seat, Vector3.zero, Quaternion.Euler(75f, 0f, 0f), size);", "offered ability card is upright over the palm"),
        ("owner", name, "|| !TownServiceOfferingPose.Contains(_seat, card.transform.position) || !ValidOwner(card)", "|| !TownServiceOfferingPose.Contains(_seat, card.transform.position)", "foreign native character refuses offering"),
        ("distance", name, "|| !TownServiceOfferingPose.Contains(_seat, card.transform.position)", "", "distant release refuses offering"),
        ("native-disabled", name, "&& slot.Selectable.IsActive() && slot.Selectable.IsInteractable()", "&& slot.Selectable.IsActive()", "foreign disabled or pending native selection never advertises a palm drop"),
        ("owner-race", name, "if (!Ready || !ValidOwner(card) || _shop.selectedCard == null", "if (!Ready || _shop.selectedCard == null", "native callback owner race refuses offering"),
        ("return", name, "if (card != null && !card.IsHeld && presentation != null) BeginReturn(presentation);", "if (card != null && !card.IsHeld && presentation != null) CardsDriver.RequestRebuild();", "walking away returns original card"),
        ("return-life", name, "finally { presentation.Started = true; PruneReturns(); }", "finally { presentation.Started = true; Returns.Remove(presentation); PruneReturns(); }", "return presentation survives ritual disposal with actual face and fixed identity"),
        ("reclaim", name, "Detach(); ClearNativeSelection();", "Detach();", "manual reclaim clears native options"),
        ("startup", name, "if (!ValidOwner(Card) || !_alive()", "if (!ValidOwner(Card) || !_alive() || !_input()", "opening input fade retains offering"),
        ("preview-commit", name, "&& !_shop._isConfirmationBoxOpened && _alive() && _input();", "&& !_shop._isConfirmationBoxOpened && _alive();", "first opening shows neutral palm locator while native input remains blocked"),
        ("head-proximity", name, "bool headEntered = head != null && !_headInside && NearVisitor(palm, head.transform.position, 1.4f);", "bool headEntered = false;", "head proximity opens original service without a held card"),
        ("deferred-approach", name, "if (!_pendingApproach || GuildmasterDestinations.CurrentDestinationMode()", "if (!_pendingApproach || !headEntered && !cardEntered || GuildmasterDestinations.CurrentDestinationMode()", "pending approach opens when original native rail becomes ready"),
        ("bounded-retry", name, "if (!headEntered && !cardEntered && now < _approachRetryAt) return;", "if (now < -1f) return;", "pending native rail retries are rate-limited between frames"),
        ("head-edge", name, "if (headEntered) _headInside = true;", "", "head proximity opens original service without a held card"),
        ("card-edge", name, "if (cardEntered) _cardInside = true;", "", "owned card approach opens through original native visit"),
        ("explicit-close", name, "suppressNativeSound: true)) _pendingApproach = false;", "suppressNativeSound: true)) { }", "explicit close remains closed while card stays near"),
        ("other-service", name, "&& GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.None", "&& true", "closing another service while near does not take over"),
        ("modal", name, "|| Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI", "", "modal confirmation prevents proximity opening"),
        ("empty-palm", name, "bool heldEligible = leftHeld == null && rightHeld == null || held != null;", "bool heldEligible = held != null;", "empty ready palm advertises an owned offering without requiring a held card"),
        ("model-refresh", name, "a.ID == b.ID", "ReferenceEquals(a, b)", "same owned card ID survives a native enhancement-list model refresh"),
        ("occupied-swap", name, "|| card == null || card.IsHeld || ReferenceEquals(Card, card)", "|| card == null || card.IsHeld || Card != null", "second valid owned card atomically swaps into enchantress palm"),
        ("swap-rollback", name, "catch\n        {\n            RestoreSelection(existing, existingSlot);\n            throw;\n        }", "catch\n        {\n            throw;\n        }", "rejected replacement preserves prior enchantress card atomically"),
        ("native-duplicate", name,
         "if (highlighted != null)\n            (highlighted.GetComponent<TownServiceNativeEnhancementCardMask>()\n                ?? highlighted.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>()).Mask();",
         "if (highlighted != null) { }",
         "same-frame handoff hides only duplicate art and reacts to the accepted offer"),
        ("offer-voice", name,
         "TownServiceVoice.RequestReaction(3, TownVoiceReaction.EnchantressInspect);", "",
         "same-frame handoff hides only duplicate art and reacts to the accepted offer"),
        ("wrong-area-canvas", name,
         "if (!onCardSurface) return false;", "if (!onCardSurface && canvas == null) return false;",
         "unrelated canvas and non-ability card print never steal the physical reclaim trigger"),
        ("stale-area-selection", name,
         "if (current == null || current.Card == null || !current.Ready || hit == null)",
         "if (current == null || current.Card == null || hit == null)",
         "native confirmation closes the area-selection laser gate"),
        ("squeezed-native-frame", "TownServiceNativeEnhancementCardMask.cs",
         "_nativeFrame.localScale = Vector3.one;", "_nativeFrame.localScale = _frameScale;",
         "world-space full-card frame occupies the card rather than a squeezed vertical strip and cannot steal native clicks"),
        ("native-frame-resquash", "TownServiceNativeEnhancementCardMask.cs",
         "private void LateUpdate()\n    {\n        if (_masked) AlignNativeEffects();\n    }",
         "private void LateUpdate()\n    {\n        if (!_masked) AlignNativeEffects();\n    }",
         "native flat animation cannot resquash the physical frame before render"),
        ("aura-render-squeeze", "TownServiceNativeEnhancementCardMask.cs",
         "scale.x *= Mathf.Clamp(diameter / width, .025f, 40f);", "scale.x *= 1f;",
         "actual submitted enchantress aura ink is round and preserves the native pulse size"),
        ("aura-group-instead-of-ink", "TownServiceNativeEnhancementCardMask.cs",
         "return ink;", "return branch;",
         "actual submitted enchantress aura ink is round and preserves the native pulse size"),
        ("aura-native-root-size", "TownServiceNativeEnhancementCardMask.cs",
         "bool physicalNow = TownServiceEnhancementHandoff.TryPhysicalCardHeight(out float cardHeight);",
         "bool physicalNow = false; float cardHeight = 0f;",
         "rendered native aura follows the actual physical card, not the 325x450 highlighter root"),
        ("aura-input-occlusion", "TownServiceNativeEnhancementCardMask.cs",
         "_effectRaycast.Add(graphic.raycastTarget);\n                graphic.raycastTarget = false;",
         "_effectRaycast.Add(graphic.raycastTarget);\n                graphic.raycastTarget = true;",
         "original aura cannot intercept a grip-held fingertip or laser press on the native enhancement area"),
        ("physical-grip-gate", "PokeInteractor.cs",
         "private bool PressAllowed => _hand.GripPressed && _hand.Grabber.Held == null;",
         "private bool PressAllowed => true;",
         "physical fingertip without grip cannot select the native ability area"),
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
    check_presentation_bridge(args.source_root)
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
