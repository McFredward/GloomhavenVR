#!/usr/bin/env python3
"""Compile production-bound interaction cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files. See
.planning/research/TOWN-SERVICES-INTERACTION-VALIDATION.md for fixture boundaries.
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
    base = root / "src/GloomhavenVR"
    paths = {
        "Token.cs": "WorldUI/TownServices/TownServiceToken.cs",
        "PalmConfirmation.cs": "WorldUI/TownServices/TownServicePalmConfirmation.cs",
        "Surface.cs": "WorldUI/TownServices/TownServiceSurface.cs",
        "OfferingPose.cs": "WorldUI/TownServices/TownServiceOfferingPose.cs",
        "WindowMask.cs": "WorldUI/TownServices/TownServiceWindowMask.cs",
        "ConfirmationMask.cs": "WorldUI/TownServices/TownServiceConfirmationMask.cs",
        "Presentation.cs": "WorldUI/TownServices/TownServicePresentation.cs",
        "Handoff.cs": "WorldUI/Modal/ModalFallback.TownServices.cs",
        "Composite.cs": "WorldUI/Modal/ModalFallback.CompositeTransfer.cs",
        "Grabber.cs": "Hands/Interact/ProximityGrabber.cs",
        "Interfaces.cs": "Hands/Interact/IGrabbable.cs",
    }
    raw = {name: (base / path).read_text() for name, path in paths.items()}
    hashes = {paths[name]: hashlib.sha256(text.encode()).hexdigest() for name, text in raw.items()}
    bound = {name: raw[name] for name in ("Token.cs", "OfferingPose.cs", "Presentation.cs", "Handoff.cs", "WindowMask.cs", "ConfirmationMask.cs", "PalmConfirmation.cs", "Surface.cs")}
    bound["ConfirmationMask.cs"] = bound["ConfirmationMask.cs"].replace("Time.unscaledTime", "MaskClock.Now")
    bound["Composite.cs"] = "using System;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback {\n" + method(raw["Composite.cs"], "internal static bool ReleaseForComposite(UIWindow window)") + "\n}\n"
    bound["Grabber.cs"] = "using System;\nusing UnityEngine;\nnamespace GloomhavenVR.Hands.Interact;\ninternal partial class ProximityGrabber {\n" + "\n".join(method(raw["Grabber.cs"], sig) for sig in (
        "private void BeginGrab(", "public bool ForceGrab(", "private bool HealDeadHeld()", "internal void CancelAll()")) + "\n}\n"
    # Bind the held branch of the real Tick as well. Its surrounding election/physics and mode
    # policy are outside this fixture; no release-condition or callback logic is reimplemented.
    tick = method(raw["Grabber.cs"], "internal void Tick()")
    start = tick.index("            bool stillHeld =")
    end = tick.index("\n            return;", start)
    bound["ReleaseTick.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.Hands.Interact;\ninternal partial class ProximityGrabber {\ninternal void ReleaseTick() {\nif (Held == null) return;\n" + tick[start:end] + "\n}\n}\n"
    interfaces = []
    for name in ("IGrabbable", "IGrabHighlight", "IGrabbableHandFilter", "ITriggerOnlyGrabbable", "IGrabCancellation"):
        start = raw["Interfaces.cs"].index("internal interface " + name + "\n")
        end = raw["Interfaces.cs"].index("\n}", start) + 2
        interfaces.append(raw["Interfaces.cs"][start:end])
    bound["Interfaces.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.Hands.Interact;\n" + "\n".join(interfaces)
    for name, path in (("ItemCardHold.cs", "Cards/ItemCardHold.cs"), ("CardGripPose.cs", "Cards/CardGripPose.cs")):
        bound[name] = (base / path).read_text(); hashes[path] = hashlib.sha256(bound[name].encode()).hexdigest()
    vr = (base / "Cards/VRCard.cs").read_text()
    constant = next(line.strip() for line in vr.splitlines() if 'internal const float PinchGripFraction =' in line)
    sweep = (base / "Cards/FanSweep.cs").read_text()
    interface = sweep[sweep.index('internal interface IFanSweepTarget'):sweep.index('\n}', sweep.index('internal interface IFanSweepTarget')) + 2]
    bound["ItemContracts.cs"] = 'using UnityEngine; namespace GloomhavenVR.Cards { internal static class VRCard { ' + constant + ' }\n' + interface + '\n}'
    ritual = (base / "WorldUI/TownServices/TownServiceRitual.cs").read_text()
    inscription = method(ritual, "internal sealed class Inscription : IDisposable")
    bound["Inscription.cs"] = "using System; using GloomhavenVR.Net; using UnityEngine; namespace GloomhavenVR.WorldUI { internal sealed partial class TownServiceRitual { " + inscription + " } }"
    hashes["WorldUI/TownServices/TownServiceRitual.cs"] = hashlib.sha256(ritual.encode()).hexdigest()
    bound["RitualLayout.cs"] = (base / "WorldUI/TownServices/TownServiceRitualLayout.cs").read_text()
    hashes["WorldUI/TownServices/TownServiceRitualLayout.cs"] = hashlib.sha256(bound["RitualLayout.cs"].encode()).hexdigest()
    return bound, hashes


def mutations():
    # Every mutant compiles and must reach the specified runtime assertion. A compile error,
    # unrelated exception or changed source binding cannot count as a rejected negative control.
    return [
        ("purse-return-before-payment", "Token.cs", "_physical.SetParent(_mat, true);", "_physical.SetParent(_homeParent, true);", "accepted purse waits at actual bowl instead of returning to moving hand before native payment"),
        ("purse-restart-completion", "Token.cs", "if (_settlementDecided) return;", "", "confirmed purse sinks and fades once at bowl without restarting on duplicate completion"),
        ("purse-own-hand", "Token.cs", "(_handAllowed?.Invoke(hand) ?? true)", "true", "unowned or unavailable purse cannot be grabbed or donated"),
        ("purse-visible-body", "Token.cs", "_physical.TransformPoint(Vector3.up * .0625f)", "_physical.position", "visible purse body, rather than its hidden base, triggers an intentional bowl donation"),
        ("flat-purse", "Token.cs", "if (_uprightProp)", "if (!_uprightProp)", "physical original follows either tracked hand"),
        ("purse-depth", "Token.cs", "_reachDepth * scale", ".009f * scale", "purse collider encloses its physical depth at each map scale"),
        ("purse-double-scale", "Token.cs", "InverseTransformVector(Vector3.down * (.13f * hand.WorldScale))", "InverseTransformDirection(Vector3.down * (.13f * hand.WorldScale))", "purse hangs below pinch without applying map scale twice"),
        ("dead-inscription-root", "Inscription.cs", "if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);", "UnityEngine.Object.Destroy(_root.gameObject);", "destroyed inscription root can be disposed without blocking native teardown"),
        ("enhancement-icon-outside", "PalmConfirmation.cs", "i == 4 ? -.14f : .075f", "i == 4 ? -.80f : .075f", "all original enhancement confirmation content stays together below the palm"),
        ("parked-reclaim", "Token.cs", "(_offering != null && TownServiceMerchantHandoff.CanReclaim(this))", "false", "actual routed grab reclaims parked stock through owned modal gate"),
        ("palm-front", "PalmConfirmation.cs", "i == 2 ? -.125f : .125f, -.1775f, -.12f", "i == 2 ? -.125f : .125f, -.1775f, -.80f", "all original enhancement confirmation content stays together below the palm"),
        ("palm-cancel-scope", "PalmConfirmation.cs", "internal void Cancel() { if (Open) _cancel(); }", "internal void Cancel() { _cancel(); }", "enhancement withdrawal cancels only the live decision once"),
        ("palm-flat", "Surface.cs", "_counterAnchor.rotation * _anchorRotation", "_counterAnchor.rotation * Quaternion.Euler(90f, 0f, 0f)", "native confirmation is upright independently of palm pitch"),
        ("palm-backing", "Surface.cs", "Panel.MrBackingSuppressed = counterAnchor != null;", "Panel.MrBackingSuppressed = false;", "freestanding original controls have no mixed reality backing"),
        ("confirm-early-unmask", "ConfirmationMask.cs", "!window.IsOpen && !window.IsVisible", "!window.IsOpen", "native onHidden starts fade without exposing confirmation popup"),
        ("confirm-reuse", "ConfirmationMask.cs", "|| !ReferenceEquals(entry.Callback, entry.Identity())", "", "reused prompt with unrelated callback is restored"),
        ("physical-eligible-drop", "Token.cs", "bool eligible = gesture && DropEligible;", "bool eligible = gesture;", "physical drop eligibility identity pose zone and cancellation fence 1"),
        ("narrow-physical-zone", "Token.cs", "Mathf.Abs(point.x) < _zoneHalfWidth", "true", "physical drop eligibility identity pose zone and cancellation fence 8"),
        ("physical-zone-drop", "Token.cs", "WithinDropZone(_mat.InverseTransformPoint(dropPoint) - _zoneCenter)", "InDropZone(Vector3.zero)", "physical drop eligibility identity pose zone and cancellation fence 2"),
        ("physical-tracked-drop", "Token.cs", "bool gesture = _heldTracked && hand.HasPose", "bool gesture = hand.HasPose", "physical drop eligibility identity pose zone and cancellation fence 7"),
        ("held-workspace", "Presentation.cs", "_workspace.Tick(_catalog?.CanRelocate != false && _ritual?.CanRelocate != false);", "_workspace.Tick(true);", "presentation defers workspace relocation while original card is held"),
        ("physical-inspect-permission", "Token.cs", "(IsPhysical || (_button != null && _button.IsActive() && _button.IsInteractable()))", "(_button != null && _button.IsActive() && _button.IsInteractable())", "displayed physical prop remains grabbable with hidden native button"),
        ("physical-no-purchase", "Token.cs", "if (_physical != null)\n        {\n            // A deliberate trigger release", "if (_physical != null && _disposed)\n        {\n            // A deliberate trigger release", "physical drop eligibility identity pose zone and cancellation fence 0"),
        ("mask-wrapper-alpha", "WindowMask.cs", "mask.alpha = 0f;", "mask.alpha = 1f;", "mask suppresses rendering and raycasts on its own wrapper"),
        ("mask-sibling", "WindowMask.cs", "            _source.SetSiblingIndex(_sibling);", "", "mask disposal restores original parent and sibling exactly"),
        ("mask-native-state", "WindowMask.cs", "            _source.SetSiblingIndex(_sibling);", "            _source.SetSiblingIndex(_sibling);\n            _source.GetComponent<CanvasGroup>().alpha = 1f;", "mask disposal preserves current native animation and permissions"),
        ("mask-reparent-owner", "WindowMask.cs", "_source != null && _source.parent == _wrapper", "_source != null", "native reparent is never overwritten during mask disposal"),
        ("window-suppression-fence", "Presentation.cs", "|| (_catalog != null || _ritual != null || _contextMask != null) && _window != null", "|| Active && (_catalog != null || _ritual != null || _contextMask != null) && _window != null", "merchant context suppression survives until explicit rollback"),
        ("manual-tray", "Presentation.cs", "_tray.Root.SetParent(null, true);", "{ }", "manual tray grab detaches before workspace movement"),
        ("option-open", "Presentation.cs", "        if (!WorldUIConfig.ImmersiveTownServices.Value)", "        if (_session == uint.MaxValue)", "rollback releases native window suppression claim"),
        ("option-release", "Presentation.cs", "internal static bool Active => WorldUIConfig.ImmersiveTownServices.Value\n        &&", "internal static bool Active =>", "disabled option immediately fences a held release before next tick"),
        ("option-classic-lifecycle", "Handoff.cs", "        if (window != null && window.IsOpen) TryConvertWindow(window);", "        if (window != null && window.IsOpen) RestoreTownServiceContext(window, Vector3.zero, Quaternion.identity);", "disabled window retains ordinary placement and fitting lifecycle"),
        ("owner", "Presentation.cs", "!ReferenceEquals(owner, _selectionOwner)", "false", "owner switch cancels held selection"),
        ("mode", "Presentation.cs", "mode != _selectionMode", "false", "mode switch cancels held selection"),
        ("committed-card", "Presentation.cs", "!ReferenceEquals(card, _selectionCard)", "false", "committed card switch cancels held selection"),
        ("hover-context", "Presentation.cs", "card = shop.selectedCard != null ? shop.selectedCard.AbilityCard : null;", "card = shop.SowingCard != null ? shop.SowingCard.AbilityCard : null;", "BeginGrab hover preserves committed selection context"),
        ("pool-card", "Token.cs", "if (!ReferenceEquals(_pickedIdentity, _identity()) || !ReferenceEquals(_pickedContext, _contextIdentity())", "if (!ReferenceEquals(_pickedContext, _contextIdentity())", "pooled underlying card switch cancels held selection"),
        ("release-context", "Token.cs", "&& ReferenceEquals(_pickedIdentity, _identity()) && ReferenceEquals(_pickedContext, _contextIdentity())\n            && _source", "&& ReferenceEquals(_pickedIdentity, _identity())\n            && _source", "release rechecks context before next tick"),
        ("release-identity", "Token.cs", "&& ReferenceEquals(_pickedIdentity, _identity()) && ReferenceEquals(_pickedContext, _contextIdentity())\n            && _source", "&& ReferenceEquals(_pickedContext, _contextIdentity())\n            && _source", "release rechecks pooled identity before next tick"),
        ("session-fence", "Presentation.cs", "() => Active && _session == session, SelectionContext);", "() => Active, SelectionContext);", "captured session mismatch cancels held selection"),
        ("cancel-origin", "Grabber.cs", "if (released is IGrabCancellation cancellation) cancellation.OnGrabCancelled(_hand);\n            else released.OnRelease(_hand, Vector3.zero);", "released.OnRelease(_hand, Vector3.zero);", "synthetic cancel never clicks even on trigger-up frame"),
        ("heal-origin", "Grabber.cs", "if (held is IGrabCancellation cancellation) cancellation.OnGrabCancelled(_hand);\n                else held.OnRelease(_hand, Vector3.zero);", "held.OnRelease(_hand, Vector3.zero);", "hand healing uses explicit cancellation even on trigger-up frame"),
        ("hand-filter", "Token.cs", "(!ReferenceEquals(hand.Grabber.Held, this) || _hand == hand)", "true", "cancelled token releases grabber hand ownership"),
        ("tracking-pose", "Token.cs", "bool select = hand.HasPose && hand.TriggerUp", "bool select = hand.TriggerUp", "tracking loss never clicks native selection"),
        ("single-click", "Token.cs", "        CancelHold();\n        if (!select", "        // mutant retains held gesture after dispatch\n        if (!select", "real drop dispatches native button exactly once"),
        ("original-parent", "Handoff.cs", "        if (previous.Target.parent != previous.OriginalParent) return false;", "", "wrong original parent refuses handoff"),
        ("null-parent", "Handoff.cs", "        if (previous.Target.parent != previous.OriginalParent) return false;", "        if (previous.OriginalParent != null && previous.Target.parent != previous.OriginalParent) return false;", "null original parent refuses unrelated parent handoff"),
        ("active-owner", "Handoff.cs", "            if (panel.Target == previous.Target) return false;", "            if (panel.Target == null) return false;", "active conversion owner refuses handoff"),
        ("rollback-lifo", "Presentation.cs", "for (int i = Surfaces.Count - 1; i >= 0; i--)", "for (int i = 0; i < Surfaces.Count; i++)", "section rollback restores native hierarchy in LIFO order"),
        ("parent-first", "Presentation.cs", "        if (_window != null && _context != null && _context.IsAlive)\n            ModalFallback.ReleaseForComposite(_window);", "", "context retires before restoring descendant sections"),
        ("native-hidden", "Presentation.cs", "            window.onHidden.AddListener(OnNativeHidden);", "", "same-frame close reopen retires session"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-service-interaction")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--unity-ui", type=Path, help="Real UnityEngine.UI.dll (never metadata-only RefAsm)")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-service-interaction-runtime"
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
