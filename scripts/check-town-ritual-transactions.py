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


def inspect_fan_contract(source):
    # Native confirmation focus can turn off input while the visitor still stands
    # at the priestess. The ordinary ability fan must stay suppressed then.
    tick = method(source, "internal void Tick(bool input)")
    if "MapRoomHand.SetTempleInspection(_inspectionNear && !holdingCard);" not in tick \
            or "_near = input && _inspectionNear && !holdingCard;" not in tick:
        raise RuntimeError("Temple fan suppression is coupled to transient native input")
    if "bool shown = _inspectionNear && hand != null && hand.HasPose && hand.Grabber.Held == null" not in tick \
            or "&& (CardsConfig.RevealAlways || hand.PalmGate.IsOpen);" not in tick:
        raise RuntimeError("Temple purse does not use the ordinary card-fan wrist presentation")
    if "bool shown = Available" in tick or "&& _near &&" in tick:
        raise RuntimeError("Temple purse wrist presentation is coupled to payment eligibility")


def inspect_particle_contract(marker):
    particle = (
        "AddComponent<ParticleSystem>()",
        "ParticleSystemRenderMode.Billboard",
        "BuildParticleTexture()",
        "_blessing.Simulate(age, true, false, true)",
        "_blessing.randomSeed = 0x475652u",
        "if (_ownsBlessing) BuildBlessing();",
        "effect.SetActive(false);",
        "ParticleSystemStopBehavior.StopEmittingAndClear",
        "Shader.Find(\"Legacy Shaders/Particles/Additive\")",
    )
    missing = [entry for entry in particle if entry not in marker]
    if missing or "BlessingSpark" in marker or "AddComponent<MeshFilter>()" in marker:
        raise RuntimeError("Temple blessing is not a seeded synchronized particle effect: "
                           + ", ".join(missing))


def inspect_shared_blessing_contract(root):
    town = root / "src/GloomhavenVR/WorldUI/TownServices"
    ritual = (town / "TownServiceRitual.cs").read_text()
    presentation = (town / "TownServicePresentation.cs").read_text()
    population = (town / "TownServicePopulation.cs").read_text()
    station = (town / "TownServiceStation.cs").read_text()
    marker = (town / "TownServiceTempleBowlMarker.cs").read_text()
    donate = method(ritual, "private bool Donate(")
    if ".Bless(" in donate:
        raise RuntimeError("Private ritual plays a duplicate local blessing")
    required = (
        "TownServiceMirror.SetLocalTempleDonationAvailable(_ritual.TempleDonationAvailable)",
        "resident.TempleBlessing.Observe(received, owner, session, known, available, revision)",
        "resident.Station.PlayTempleBlessing(transitionAge)",
        "new TownServiceTempleBowlMarker(root.transform, stationSpace: true)",
    )
    joined = presentation + population + station
    missing = [entry for entry in required if entry not in joined]
    if missing:
        raise RuntimeError("Shared temple blessing seam is incomplete: " + ", ".join(missing))
    inspect_particle_contract(marker)
    gate = (
        "if (!received) return false; // Keep the baseline across transient close/reopen gaps.",
        "bool play = sameSession && known && !available && advanced;",
        "resident.TempleUnavailableBlend = Mathf.MoveTowards(resident.TempleUnavailableBlend,",
        "unavailable ? 1f : 0f,\n                    Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds",
        "TownServiceActivityMotion.ApplyTempleAvailability(ref displayedActivity,\n                    !unavailable, resident.TempleUnavailableBlend)",
    )
    missing_gate = [entry for entry in gate if entry not in population]
    if missing_gate or "resident.TempleUnavailableBlend = 0f" in population:
        raise RuntimeError("Temple blessing/cover continuity gate is incomplete: " + ", ".join(missing_gate))
    snap_control = replace_once(population,
        "unavailable ? 1f : 0f,\n                    Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds",
        "unavailable ? 1f : 0f,\n                    unavailable ? Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds : 1f")
    try:
        missing_snap = [entry for entry in gate if entry not in snap_control]
        if missing_snap or "resident.TempleUnavailableBlend = 0f" in snap_control:
            raise RuntimeError("mutated cover snaps")
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple cover-pop negative control did not fail")
    polygon_control = replace_once(marker, "AddComponent<ParticleSystem>()", "AddComponent<MeshFilter>()")
    try:
        inspect_particle_contract(polygon_control)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple blessing polygon negative control did not fail")
    autoplay_control = replace_once(marker, "effect.SetActive(false);", "effect.SetActive(true);")
    try:
        inspect_particle_contract(autoplay_control)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple blessing play-on-create negative control did not fail")


def inspect_purse_contract(source):
    required = (
        "private readonly Func<bool> _available;",
        "bool visible = !_offering || _visible() || Token?.IsMoving == true;",
        "if (_offering && !_available() && !Token.IsMoving) Token.PickCollider.enabled = false;",
        "() => TemplePurseVisible(temple, slot)",
        "if (piece.Source is UITempleShopSlot && piece.NativeAvailable)",
    )
    missing = [entry for entry in required if entry not in source]
    if missing:
        raise RuntimeError("Temple purse visibility is still coupled to payment eligibility: " + ", ".join(missing))


def sources(root):
    raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitual.cs").read_text()
    inspect_purse_contract(raw)
    coupled = replace_once(raw, "() => TemplePurseVisible(temple, slot)", "() => TempleVisibleEligible(temple, slot)")
    try:
        inspect_purse_contract(coupled)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple purse visibility negative control did not fail")
    methods = method(raw, "private bool Confirm(") + "\n" + method(raw, "private static bool Click(")
    methods = methods.replace("private bool Confirm(", "internal bool Confirm(")
    start = raw.index("    private bool OfferingEligible(")
    end = raw.index("    private bool Confirm(", start)
    methods += raw[start:end].replace("private bool Donate(", "internal bool Donate(")
    text = "using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;using GloomhavenVR.WorldUI;using GloomhavenVR.Core;\n" + \
        "internal sealed class BoundRitual {private readonly Func<bool> _alive;private readonly Func<bool> _sessionAlive;private readonly Func<object?> _context;" + \
        "private readonly HashSet<(string Character,object Blessing)> _submittedOfferings=new();internal FakeTempleOffering _templeOffering=new();" + \
        "internal BoundRitual(Func<bool> alive,Func<object?> context){_alive=alive;_sessionAlive=alive;_context=context;}" + \
        "internal BoundRitual(Func<bool> alive,Func<bool> sessionAlive,Func<object?> context){_alive=alive;_sessionAlive=sessionAlive;_context=context;}\n" + methods + "\n}"
    guard_path = root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualConfirmationGuard.cs"
    if not guard_path.exists():
        guard_path = Path(__file__).resolve().parent.parent / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualConfirmationGuard.cs"
    guard_raw = guard_path.read_text()
    guard = guard_raw[:guard_raw.index("\n[HarmonyPatch")].replace("using HarmonyLib;\n", "")
    offering_raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleOffering.cs").read_text()
    inspect_fan_contract(offering_raw)
    coupled = replace_once(offering_raw, "MapRoomHand.SetTempleInspection(_inspectionNear && !holdingCard);",
                           "MapRoomHand.SetTempleInspection(input && _inspectionNear && !holdingCard);")
    try:
        inspect_fan_contract(coupled)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple fan suppression negative control did not fail")
    coupled_visibility = replace_once(offering_raw,
        "&& (CardsConfig.RevealAlways || hand.PalmGate.IsOpen);",
        "&& Available;")
    try:
        inspect_fan_contract(coupled_visibility)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple purse visibility negative control did not fail")
    exit_method = method(offering_raw, "private bool ExitIfAway(").replace("private bool ExitIfAway(", "internal bool ExitIfAway(")
    exit_source = "using UnityEngine;using GloomhavenVR.WorldUI;internal sealed class BoundTempleExit {" + \
        "internal bool _visited=true,_near=true,_inspectionNear=true,Available=true;internal UIWindow _window;internal Transform _station;internal TownServiceRitual _ritual=new();" + \
        "internal BoundTempleExit(UIWindow window,Transform station){_window=window;_station=station;}" + exit_method + "}"
    pose_raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceOfferingPose.cs").read_text()
    exit_source += "internal static class TownServiceOfferingPose {" + method(pose_raw, "internal static bool VisitorWithin(") + "}"
    approach = method(offering_raw, "internal static void TickApproach()")
    approach_source = "using UnityEngine;namespace GloomhavenVR.WorldUI { internal static class BoundTempleApproach { private static bool _approachInside;private static float _approachAt;" + approach + "} }"
    return {"RitualTransactions.cs": text, "RitualGuard.cs": guard, "TempleExit.cs": exit_source, "TempleApproach.cs": approach_source}, {"TownServiceRitual.cs": hashlib.sha256(raw.encode()).hexdigest(), "TownServiceRitualConfirmationGuard.cs": hashlib.sha256(guard_raw.encode()).hexdigest(), "TownServiceTempleOffering.cs": hashlib.sha256(offering_raw.encode()).hexdigest()}


def mutations():
    return [
        ("temple-approach-hysteresis", "TempleApproach.cs", "TownServiceOfferingPose.VisitorWithin(station.Root, 1.4f)", "TownServiceOfferingPose.VisitorWithin(station.Root, _approachInside ? 1.65f : 1.4f)", "return from larger attention radius creates a fresh priestess approach"),
        ("merchant-approach-latch", "TempleApproach.cs", "if (destination != EGuildmasterMode.None) _approachInside = false;", "if (destination != EGuildmasterMode.None) _approachInside = true;", "blocked foreign service cannot preserve a stale temple latch"),
        ("temple-character-restore", "TempleApproach.cs", "selectedSlot.OnClick();", "if (selectedSlot.State == PartySlotState.Empty) selectedSlot.OnClick();", "temple entry preserves the exact previously selected native slot"),
        ("temple-close-missing", "TempleExit.cs", "ModalFallback.CloseFloatedWindow(_window);", "", "physical departure closes native temple before visiting another resident"),
        ("repeat-donation", "RitualTransactions.cs", "_submittedOfferings.Add(offering);", "", "a delayed online stock refresh never permits a duplicate donation"),
        ("visitor-departure", "RitualTransactions.cs", "_templeOffering?.VisitorPresent == true && TemplePendingEligible(temple, slot)", "TemplePendingEligible(temple, slot)", "walking away before native completion cancels the donation"),
        ("modal-input-gate", "RitualTransactions.cs", "_templeOffering?.VisitorPresent == true && TemplePendingEligible(temple, slot)", "_templeOffering?.Available == true && TemplePendingEligible(temple, slot)", "native modal input lock does not invalidate its own donation"),
        ("delayed-validation", "RitualGuard.cs", "_box != null && _valid()", "_box != null", "delayed owner change cancels original transaction"),
        ("delayed-cancel", "RitualGuard.cs", "else cancel?.Invoke();", "else if (!requested) cancel?.Invoke();", "delayed owner change cancels original transaction"),
        ("duplicate-completion", "RitualGuard.cs", "if (_completed) return;", "", "duplicate hidden completion is one shot"),
        ("scope-boundary", "RitualGuard.cs", " || !ReferenceEquals(scope._box, box)", "", "unrelated box retains its native callbacks"),
        ("affordability-race", "RitualTransactions.cs", "Func<bool> stillValid = () => _sessionAlive() && pendingEligible()", "Func<bool> stillValid = () => _sessionAlive()", "post-selection affordability refused"),
        ("owner-race", "RitualTransactions.cs", "&& ReferenceEquals(context, _context()) && ReferenceEquals(selected, identity());", "&& ReferenceEquals(selected, identity());", "post-selection owner change refused"),
        ("item-race", "RitualTransactions.cs", "&& ReferenceEquals(context, _context()) && ReferenceEquals(selected, identity());", "&& ReferenceEquals(context, _context());", "post-selection selected item change refused"),
        ("existing-prompt", "RitualTransactions.cs", " || box.GetComponent<UIWindow>().IsOpen || !button.IsInteractable()", " || !button.IsInteractable()", "existing unrelated prompt untouched"),
        ("ownership", "RitualTransactions.cs", "bool created = owns &&", "bool created =", "unowned new callback not confirmed"),
        ("stale-prompt", "RitualTransactions.cs", "if (created) box.Hide();", "if (created) { }", "own stale prompt cancelled through native lifecycle"),
        ("transient-row-lock", "RitualTransactions.cs", "if (!valid || !created)", "if (!valid || !button.IsInteractable() || !created)", "native row lock during confirmation is not a cancellation"),
        ("flat-button-dependency", "RitualTransactions.cs", "box.OnConfirm();", "Click(box.confirmButton);", "one native confirmation succeeds"),
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
    inspect_shared_blessing_contract(args.source_root)
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
