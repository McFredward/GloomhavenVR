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


def expression_method(source, signature):
    start = source.index("    " + signature)
    return source[start:source.index(";", start) + 1]


def purse_visitor_sources(mirror):
    tick = method(mirror, "internal static void TickRemote(")
    start = tick.index("if (secondaryVisitor && !IndependentVisitorModule(received,")
    end = tick.index(") continue;", start) + len(") continue;")
    received = tick[start:end]
    frame = expression_method(mirror, "private static bool IndependentVisitorModule(TownServiceFrame frame,")
    physical = method(mirror, "private static bool PhysicalPurse(")
    address = method(mirror, "private static bool IndependentVisitorModule(byte service,")
    retained = method(mirror, "private static void RetainIndependentVisitorOnly(")
    return tick, received, frame, physical, address, retained


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def inspect_fan_contract(source):
    # Native confirmation focus can turn off input while the visitor still stands
    # at the priestess. The ordinary ability fan must stay suppressed then.
    tick = method(source, "internal void Tick(bool input)")
    if "MapRoomHand.SetTempleInspection(_inspectionNear && !holdingCard);" not in tick \
            or "_near = input && _inspectionNear && !holdingCard;" not in tick \
            or "&& WantsPurseFocus;" not in tick:
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
        "ParticleSystem[] phases = { _blessing, _halo, _falling, _outward };",
        "phase.Simulate(age, true, true, true);",
        "_blessing.Simulate(delta, true, false, true);",
        "_blessing.Pause(true);",
        "phase.randomSeed = 0x475652u + (uint)(i * 917);",
        "if (_ownsBlessing) BuildBlessing();",
        "effect.SetActive(false);",
        "ParticleSystemStopBehavior.StopEmittingAndClear",
        "if (_blessingFinished) continue;",
        "age >= TownServiceActivityMotion.TempleBlessingVisualSeconds",
        "Shader.Find(\"Legacy Shaders/Particles/Additive\")",
    )
    missing = [entry for entry in particle if entry not in marker]
    if missing or "BlessingSpark" in marker or "AddComponent<MeshFilter>()" in marker:
        raise RuntimeError("Temple blessing is not a seeded synchronized particle effect: "
                           + ", ".join(missing))


def inspect_guide_contract(marker, shader):
    guide = method(marker, "private static Material GhostMaterial(Material source)")
    required = ('TownServiceAssets.Shader("townnpc")', 'copy.SetFloat("_TownTransparent", 1f);',
                'copy.SetFloat("_TownVisibility", 1f);', 'copy.SetInt("_ZWrite", 0);',
                'renderQueue = (int)RenderQueue.Transparent')
    if any(entry not in guide for entry in required) or 'Shader.Find("Standard")' in guide \
            or 'Blend [_SrcBlend] [_DstBlend]' not in shader or 'ZWrite [_ZWrite]' not in shader:
        raise RuntimeError("Temple guide must retain owned shared lighting and true non-solid alpha transparency")


def inspect_shared_blessing_contract(root):
    town = root / "src/GloomhavenVR/WorldUI/TownServices"
    ritual = (town / "TownServiceRitual.cs").read_text()
    presentation = (town / "TownServicePresentation.cs").read_text()
    population = (town / "TownServicePopulation.cs").read_text()
    station = (town / "TownServiceStation.cs").read_text()
    marker = (town / "TownServiceTempleBowlMarker.cs").read_text()
    shader = (root / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders/TownNpc.shader").read_text()
    inspect_guide_contract(marker, shader)
    guide_controls = (
        replace_once(marker, 'copy.SetInt("_ZWrite", 0);', 'copy.SetInt("_ZWrite", 1);'),
        replace_once(marker, 'TownServiceAssets.Shader("townnpc")', 'Shader.Find("Standard")'),
    )
    for control in guide_controls:
        try: inspect_guide_contract(control, shader)
        except RuntimeError: pass
        else: raise RuntimeError("Temple guide contract did not reject a source lighting/depth regression")
    print("Temple guide: shared-lighting/alpha source contract and two negative controls passed", flush=True)
    motion = (town / "TownServiceActivityMotion.cs").read_text()
    donate = method(ritual, "private bool Donate(") + method(ritual, "private void CompleteTempleDonation(")
    if ".Bless(" in donate:
        raise RuntimeError("Private ritual plays a duplicate local blessing")
    if "TownServiceMirror.MarkLocalTempleDonationCommitted();" not in donate:
        raise RuntimeError("Temple blessing revision is not bound to the guarded native donation callback")
    required = (
        "TownServiceMirror.SetLocalTempleDonationAvailable(_ritual.TempleDonationAvailable)",
        "resident.TempleBlessing.Observe(true, state.Peer, state.Session,",
        "TownServiceMirror.TryTemplePresentationState(out bool received, out bool anyCanDonate)",
        "resident.Station.SampleTempleBlessing(sourceEpoch, resident.TempleBlessingGeneration,",
        "new TownServiceTempleBowlMarker(root.transform, stationSpace: true)",
    )
    joined = presentation + population + station
    missing = [entry for entry in required if entry not in joined]
    if missing:
        raise RuntimeError("Shared temple blessing seam is incomplete: " + ", ".join(missing))
    inspect_particle_contract(marker)
    if "internal const float TempleBlessingVisualSeconds = 4.20f;" not in motion:
        raise RuntimeError("Temple cover no longer waits for the primary mote's full lifetime")
    gate = (
        "if (!received || owner <= 0 || session == 0) return false;",
        "bool committed = known && unchecked((int)(revision - previous)) > 0;",
        "bool coverUnavailable = unavailable && !blessingVisible;",
        "if (coverUnavailable && !resident.TempleAvailabilityObserved)",
        "resident.TempleUnavailableBlend = 1f;",
        "committedAge / TownServiceActivityMotion.TransitionSeconds",
        "resident.TempleUnavailableBlend = Mathf.MoveTowards(resident.TempleUnavailableBlend,\n                        0f,",
        "TownServiceActivityMotion.ApplyTempleAvailability(ref displayedActivity,\n                    resident.TempleUnavailableBlend <= 0f, resident.TempleUnavailableBlend)",
    )
    compact_population = " ".join(population.split())
    missing_gate = [entry for entry in gate if " ".join(entry.split()) not in compact_population]
    if missing_gate or "resident.TempleUnavailableBlend = 0f" in population:
        raise RuntimeError("Temple blessing/cover continuity gate is incomplete: " + ", ".join(missing_gate))
    cover_controls = (
        replace_once(population,
            "if (coverUnavailable && !resident.TempleAvailabilityObserved)",
            "if (coverUnavailable && false)"),
        replace_once(population,
            "committedAge / TownServiceActivityMotion.TransitionSeconds",
            "Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds"),
        replace_once(population,
            "bool coverUnavailable = unavailable && !blessingVisible;",
            "bool coverUnavailable = unavailable;"),
    )
    for snap_control in cover_controls:
        try:
            missing_snap = [entry for entry in gate if " ".join(entry.split()) not in " ".join(snap_control.split())]
            if missing_snap or "resident.TempleUnavailableBlend = 0f" in snap_control:
                raise RuntimeError("mutated cover snaps")
        except RuntimeError:
            pass
        else:
            raise RuntimeError("Temple cover transition negative control did not fail")
    # The blessing now has a primary system and secondary phases. Mutating the
    # primary alone must still trip the no-polygon contract.
    if marker.count("AddComponent<ParticleSystem>()") < 2:
        raise RuntimeError("Temple blessing lost its primary or secondary particle phase")
    polygon_control = marker.replace("AddComponent<ParticleSystem>()", "AddComponent<MeshFilter>()", 1)
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


def inspect_purse_contract(source, offering, sync, mirror, templates):
    required = (
        "private readonly Func<bool> _available;",
        "bool visible = !_offering || _visible() || Token?.IsMoving == true;",
        "inspect: () => owner._templeOffering?.CanInspectPurse ?? true,",
        "() => TemplePurseVisible(temple, slot)",
        "piece.Source is UITempleShopSlot slot && (piece.NativeAvailable",
        "private bool OfferingEligible(UITempleWindow temple, UITempleShopSlot slot) => _templeOffering?.Available == true",
        "&& TownServiceMirror.CanLocalBeginTransaction(2)",
        "&& TempleQuietAvailable(temple, slot);",
    )
    missing = [entry for entry in required if entry not in source]
    hand = offering[offering.index("internal bool AllowsHand("):offering.index("\n\n", offering.index("internal bool AllowsHand("))]
    if "internal bool CanInspectPurse => _inspectionNear;" not in offering \
            or "CanInspectPurse" not in hand \
            or "TownServiceMirror.CanLocalBeginTransaction" in hand \
            or "Available" in hand:
        missing.append("physical purse hand filter is coupled to donation availability")
    if "Token.PickCollider.enabled = false" in source or "&& _available()" in source[source.index("inspect: () =>"):source.index("zoneHalfWidth:")]:
        missing.append("physical purse pickup is disabled by native row eligibility")
    if "Publish(service == 2 ? \"ritual.purse.held\" : piece.BodyKey," not in sync \
            or "piece.Body, prewarm: service == 2);" not in sync \
            or "if (piece.Token.IsMoving)" not in sync \
            or "PriorityRoots.Add(piece.Body);" not in sync:
        missing.append("physical purse changes its original identity or loses prewarm on pickup")
    # Personal wrist and held bodies survive another visitor's shared UI lease.
    # A cumulative delta can omit Mesh, so the ordinary purse address is admitted
    # provisionally and classified only after expansion. That must not admit a
    # second shared bowl guide/image with the same address.
    tick, received, frame, physical, address, retained = purse_visitor_sources(mirror)
    independent_purse = (
        (tick, "if (secondaryVisitor) RetainIndependentVisitorOnly(entry.Key, standing, session);"),
        (received, '&& !(received.Service == 2 && received.TemplateAddress == "ritual.purse|")) continue;'),
        (frame, '|| frame.Service == 2 && frame.TemplateAddress == "ritual.purse|" && PhysicalPurse(frame.Nodes);'),
        (physical, "if (node.Values.ContainsKey(TownServiceProperty.Mesh)) return true;"),
        (address, 'if (service == 2)\n            return address.StartsWith("ritual.purse.held|", StringComparison.Ordinal)\n'
         '                || address.StartsWith("temple.row|", StringComparison.Ordinal)'),
        (retained, "IndependentVisitorModule(pair.Value.LastFrame, peer, !session.TransactionActive)"),
        (retained, "IndependentVisitorModule(session.Service, pair.Value.Address, TownServiceFrame.ManifestModule, peer, !session.TransactionActive)"),
    )
    # Other production gates may sit between expansion and classification. Require
    # the actual scopes and causal order, not unrelated statements' adjacency.
    ordered = (
        received,
        "TownServiceFrame? expanded = TownServiceDelta.Expand(baseline, received);",
        "TownServiceFrame frame = expanded;",
        "if (secondaryVisitor && !IndependentVisitorModule(frame, entry.Key, !session.TransactionActive)) continue;",
    )
    positions = [tick.find(entry) for entry in ordered]
    if any(entry not in scope for scope, entry in independent_purse) \
            or any(position < 0 for position in positions) or positions != sorted(positions):
        missing.append("secondary temple visitor loses their independent held purse")
    if 'key == "ritual.purse" || key == "ritual.purse.held"' not in templates:
        missing.append("remote held purse has no inert original template")
    if missing:
        raise RuntimeError("Temple purse inspection/drop/synchronization contract failed: " + ", ".join(missing))


def sources(root):
    raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitual.cs").read_text()
    offering_raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleOffering.cs").read_text()
    sync_raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.cs").read_text()
    mirror_raw = (root / "src/GloomhavenVR/Net/TownServices/TownServiceMirror.cs").read_text()
    templates_raw = (root / "src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.cs").read_text()
    inspect_purse_contract(raw, offering_raw, sync_raw, mirror_raw, templates_raw)
    coupled = replace_once(raw, "() => TemplePurseVisible(temple, slot)", "() => TempleVisibleEligible(temple, slot)")
    try:
        inspect_purse_contract(coupled, offering_raw, sync_raw, mirror_raw, templates_raw)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple purse visibility negative control did not fail")
    coupled_inspection = replace_once(raw,
        "inspect: () => owner._templeOffering?.CanInspectPurse ?? true,",
        "inspect: () => (owner._templeOffering?.Available ?? true) && _available(),")
    try:
        inspect_purse_contract(coupled_inspection, offering_raw, sync_raw, mirror_raw, templates_raw)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple purse ineligible-grab negative control did not fail")
    blocked_hand = replace_once(offering_raw,
        "internal bool AllowsHand(VRHand hand) => CanInspectPurse",
        "internal bool AllowsHand(VRHand hand) => Available && TownServiceMirror.CanLocalBeginTransaction(2)")
    try:
        inspect_purse_contract(raw, blocked_hand, sync_raw, mirror_raw, templates_raw)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple purse busy-visitor grab negative control did not fail")
    muted_pose = replace_once(sync_raw,
        'Publish(service == 2 ? "ritual.purse.held" : piece.BodyKey,',
        'Publish(service == 2 && piece.Token.IsHeld ? "ritual.purse.held" : piece.BodyKey,')
    try:
        inspect_purse_contract(raw, offering_raw, muted_pose, mirror_raw, templates_raw)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple purse held-pose publication negative control did not fail")
    elected_only = replace_once(mirror_raw,
        "if (secondaryVisitor && !IndependentVisitorModule(frame, entry.Key, !session.TransactionActive)) continue;",
        "if (secondaryVisitor) continue;")
    try:
        inspect_purse_contract(raw, offering_raw, sync_raw, elected_only, templates_raw)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Secondary visitor purse playback negative control did not fail")
    missing_mesh_delta = replace_once(mirror_raw,
        '&& !(received.Service == 2 && received.TemplateAddress == "ritual.purse|")) continue;',
        ") continue;")
    try:
        inspect_purse_contract(raw, offering_raw, sync_raw, missing_mesh_delta, templates_raw)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Secondary visitor cumulative purse delta negative control did not fail")
    shared_image_as_body = replace_once(mirror_raw,
        '|| frame.Service == 2 && frame.TemplateAddress == "ritual.purse|" && PhysicalPurse(frame.Nodes);',
        '|| frame.Service == 2 && frame.TemplateAddress == "ritual.purse|";')
    try:
        inspect_purse_contract(raw, offering_raw, sync_raw, shared_image_as_body, templates_raw)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Duplicate shared bowl cue negative control did not fail")
    methods = method(raw, "private bool Confirm(") + "\n" + method(raw, "private static bool Click(")
    methods = methods.replace("private bool Confirm(", "internal bool Confirm(")
    start = raw.index("    private bool OfferingEligible(")
    end = raw.index("    private bool Confirm(", start)
    methods += raw[start:end].replace("private bool Donate(", "internal bool Donate(").replace(
        "private void TickPendingTempleDonation(", "internal void TickPendingTempleDonation(")
    text = "using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;using GloomhavenVR.WorldUI;using GloomhavenVR.Core;using GloomhavenVR.Net.TownServices;\n" + \
        "internal sealed class BoundRitual {private readonly Func<bool> _alive;private readonly Func<bool> _sessionAlive;private readonly Func<object?> _context;" + \
        "private readonly HashSet<(string Character,object Blessing)> _submittedOfferings=new();internal FakeTempleOffering _templeOffering=new();private PendingTempleDonation? _pendingTempleDonation;private bool _nativeTempleDonationActive;internal bool TempleGrantStalled {get;private set;}" + \
        "internal BoundRitual(Func<bool> alive,Func<object?> context){_alive=alive;_sessionAlive=alive;_context=context;}" + \
        "internal BoundRitual(Func<bool> alive,Func<bool> sessionAlive,Func<object?> context){_alive=alive;_sessionAlive=sessionAlive;_context=context;}\n" + method(raw, "private sealed class PendingTempleDonation") + "\n" + methods + "\n}"
    guard_path = root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualConfirmationGuard.cs"
    if not guard_path.exists():
        guard_path = Path(__file__).resolve().parent.parent / "src/GloomhavenVR/WorldUI/TownServices/TownServiceRitualConfirmationGuard.cs"
    guard_raw = guard_path.read_text()
    guard = guard_raw[:guard_raw.index("\n[HarmonyPatch")].replace("using HarmonyLib;\n", "")
    inspect_fan_contract(offering_raw)
    coupled = replace_once(offering_raw, "MapRoomHand.SetTempleInspection(_inspectionNear && !holdingCard);",
                           "MapRoomHand.SetTempleInspection(input && _inspectionNear && !holdingCard);")
    try:
        inspect_fan_contract(coupled)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple fan suppression negative control did not fail")
    coupled_focus = replace_once(offering_raw, "&& WantsPurseFocus;", "&& true;")
    try:
        inspect_fan_contract(coupled_focus)
    except RuntimeError:
        pass
    else:
        raise RuntimeError("Temple-to-merchant hand-focus negative control did not fail")
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
    approach += method(offering_raw, "private static bool WantsPurseAtBowl(")
    approach += method(offering_raw, "private static bool NearestTempleForHead(")
    approach += method(offering_raw, "private static bool WantsMerchantFanAtCounter(")
    approach += method(offering_raw, "internal static bool WantsPurseFocus")
    approach_source = "using UnityEngine;using GloomhavenVR.Core;using VRHand=FakeHand;namespace GloomhavenVR.WorldUI { internal static class BoundTempleApproach { private static bool _approachInside,_purseFocus;" + approach + "} }"
    _, received, frame, physical, address, _ = purse_visitor_sources(mirror_raw)
    # Preserve the exact received-frame predicate and production classifiers. Only
    # their surrounding loop/session objects are adapted to explicit parameters.
    received = received.replace("secondaryVisitor", "true").replace("entry.Key", "peer").replace(
        "!session.TransactionActive", "returning").replace("continue;", "return false;")
    visitor_source = ("using System;using GloomhavenVR.Net.TownServices;internal static class BoundTemplePurseVisitor {"
        + "internal static bool AdmitsReceived(TownServiceFrame received,int peer,bool returning) {"
        + received + "return true;}\n" + (frame + physical + address).replace("private static", "internal static") + "}")
    controller_raw = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceTempleController.cs").read_text()
    controller_source = "using ICharacter=FakeCharacter;\n" + controller_raw[:controller_raw.index("\n/// <summary>Keep native book animation")].replace(
        "using HarmonyLib;\n", "").replace("internal static class TownServiceTempleController", "internal static class BoundTempleController")
    proxy_source = "using System;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI { internal static class BoundQuietTempleProxyPresentation {private static UITempleWindow? _reported;" + (
        method(controller_raw, "private static void Prefix(") + method(controller_raw, "private static void Postfix(")
        + method(controller_raw, "private static void Report(")).replace("private static void", "internal static void") + "} }"
    native_path = root / "decompiled/GH.Runtime/UITempleWindow.cs"
    if not native_path.is_file() and (root / "ressources").is_symlink():
        native_path = (root / "ressources").resolve().parent / "decompiled/GH.Runtime/UITempleWindow.cs"
    # Like the onboarding fixture, portable CI has explicit native boundaries. Local evidence
    # replaces this narrow visibility branch with the actual read-only game method body.
    native_proxy = "internal void ProxyBuyBlessing(string targetCharacterID,TempleYML.TempleBlessingDefinition blessing){if(window.IsVisible)BuyBlessing(targetCharacterID,blessing);else service.Buy(targetCharacterID,blessing);}"
    native_origin = "explicit-portable-boundary"
    if native_path.is_file():
        native_raw = native_path.read_text()
        native_start = native_raw.index("\tprivate void ProxyBuyBlessing(string targetCharacterID")
        native_end = native_raw.index("\n\tpublic void EnablePartialHide()", native_start)
        native_proxy = native_raw[native_start:native_end].replace("private void ProxyBuyBlessing", "internal void ProxyBuyBlessing")
        native_origin = "readonly-game-method"
    native_proxy_source = "using System.Linq;using MapRuleLibrary.Party;using MapRuleLibrary.YML.Locations;internal sealed class BoundNativeTempleProxy {private readonly UIWindow window;private readonly FakeTempleService service;private readonly System.Action<string,TempleYML.TempleBlessingDefinition> _visibleBuy;private string audioItemBless=\"native-bless\";internal BoundNativeTempleProxy(UIWindow source,FakeTempleService model,System.Action<string,TempleYML.TempleBlessingDefinition> visibleBuy){window=source;service=model;_visibleBuy=visibleBuy;}private void BuyBlessing(string id,TempleYML.TempleBlessingDefinition blessing)=>_visibleBuy(id,blessing);" + native_proxy + "}"
    bound = {"RitualTransactions.cs": text, "RitualGuard.cs": guard, "TempleExit.cs": exit_source,
             "TempleApproach.cs": approach_source, "TemplePurseVisitor.cs": visitor_source,
             "TempleQuietController.cs": controller_source, "TempleQuietProxy.cs": proxy_source,
             "NativeTempleProxy.cs": native_proxy_source}
    hashes = {"TownServiceRitual.cs": hashlib.sha256(raw.encode()).hexdigest(),
              "TownServiceTempleController.cs": hashlib.sha256(controller_raw.encode()).hexdigest(),
              "native-boundary/UITempleWindow.ProxyBuyBlessing:" + native_origin: hashlib.sha256(native_proxy.encode()).hexdigest(),
              "TownServiceRitualConfirmationGuard.cs": hashlib.sha256(guard_raw.encode()).hexdigest(),
              "TownServiceTempleOffering.cs": hashlib.sha256(offering_raw.encode()).hexdigest(),
              "Net/TownServices/TownServiceMirror.cs": hashlib.sha256(mirror_raw.encode()).hexdigest(),
              "TownServiceSync.cs": hashlib.sha256(sync_raw.encode()).hexdigest(),
              "NativeTemplates.cs": hashlib.sha256(templates_raw.encode()).hexdigest()}
    for name in ("TownServiceFrame.cs", "TownServiceDelta.cs", "TownRackState.cs", "TownCatalogLayout.cs",
                 "TownCatalogBank.cs", "TownCatalogBank.Headers.cs", "TownServiceCodec.cs"):
        native = (root / "src/GloomhavenVR/Net/TownServices" / name).read_text()
        bound[name] = native
        hashes["Net/TownServices/" + name] = hashlib.sha256(native.encode()).hexdigest()
    protocol = (root / "tests/GloomhavenVR.TownServiceTests/ProtocolBoundary.cs").read_text()
    bound["ProtocolBoundary.cs"] = protocol
    hashes["TownServiceTests/ProtocolBoundary.cs"] = hashlib.sha256(protocol.encode()).hexdigest()
    return bound, hashes


def mutations():
    return [
        ("purse-visitor-root-lease", "TemplePurseVisitor.cs", '|| address.StartsWith("temple.row|", StringComparison.Ordinal)', '|| false', "another visitor retains the original labelled wrist purse root"),
        ("purse-visitor-held-lease", "TemplePurseVisitor.cs", 'return address.StartsWith("ritual.purse.held|", StringComparison.Ordinal)', 'return false', "another visitor retains the original held purse body"),
        ("purse-visitor-sparse-received", "TemplePurseVisitor.cs", '&& !(received.Service == 2 && received.TemplateAddress == "ritual.purse|"))', '&& true)', "cumulative purse delta reaches expansion without repeating its native mesh"),
        # Removing the Mesh fence first misclassifies the unexpanded sparse frame;
        # that same fence rejects image-only modules after expansion.
        ("purse-visitor-image-leak", "TemplePurseVisitor.cs", '|| frame.Service == 2 && frame.TemplateAddress == "ritual.purse|" && PhysicalPurse(frame.Nodes);', '|| frame.Service == 2 && frame.TemplateAddress == "ritual.purse|";', "cumulative purse delta reaches expansion without repeating its native mesh"),
        ("temple-commit-release", "RitualTransactions.cs", "TownServiceMirror.SetLocalTransactionActive(2, false);\n        if (!committed)", "/* retain priestess reservation */\n        if (!committed)", "successful donation retires its short native commit reservation before the shared blessing finishes"),
        ("temple-attention-too-narrow", "TempleApproach.cs", "_purseFocus ? 2.6f : 2.4f", "_purseFocus ? 1.65f : 1.4f", "attention-range visitor retains purse when original temple closes"),
        ("temple-native-destination-coupling", "TempleApproach.cs", "_purseFocus = deliberate || templeNear", "_purseFocus = GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.Temple && deliberate || GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.Temple && templeNear", "native five-frame close cannot restore the normal ability fan at priestess"),
        ("temple-quiet-window-open", "TempleQuietController.cs", "temple.Shop.Display(blessings, temple.service);", "window.Show(); temple.Shop.Display(blessings, temple.service);", "quiet temple never opens or activates the flat window"),
        ("temple-quiet-source-hidden-row", "TempleQuietController.cs", "if (!current.gameObject.activeSelf) return false;", "if (current == null) return false;", "a hidden native row ancestor is never exposed by quiet admission"),
        ("temple-quiet-no-counter-clock", "TempleQuietController.cs", "Move(frame);", "/* no clock island */", "only original book counter clocks are active with native flat rendering disabled"),
        ("temple-quiet-ui-every-sample", "TempleQuietController.cs", "if (changed)\n        {", "if (true)\n        {", "standing at the priestess causes no repeated hidden native UI refresh"),
        ("temple-quiet-active-ancestor-only", "RitualTransactions.cs", "button.GetComponentInParent<UITempleShopSlot>(true)", "button.GetComponentInParent<UITempleShopSlot>()", "quiet inactive source selects the original confirmation without opening the old window"),
        ("temple-quiet-inactive-click", "RitualTransactions.cs", "bool clicked = quietTemple", "bool clicked = false", "quiet inactive source selects the original confirmation without opening the old window"),
        ("temple-quiet-native-permission", "TempleQuietController.cs", "|| !temple.service.CanBuy(character.CharacterID, slot.Blessing)", "|| false", "observer without native ownership cannot invoke original selection"),
        ("temple-quiet-proxy-missing", "TempleQuietProxy.cs", "TownServiceTempleController.AnimateProxy(__instance, __state);", "/* no original book animation */", "quiet native proxy commits once and animates original book after invisible window branch"),
        ("temple-quiet-proxy-double-buy", "TempleQuietProxy.cs", "TownServiceTempleController.AnimateProxy(__instance, __state);", "__instance.service.Buy(__instance.character.CharacterID, __instance.service.Blessings[0]);TownServiceTempleController.AnimateProxy(__instance, __state);", "quiet native proxy commits once and animates original book after invisible window branch"),
        ("temple-quiet-proxy-foreign", "TempleQuietProxy.cs", "if (!TownServicePresentation.IsQuietTemple(__instance.GetComponent<UIWindow>())) return;", "/* no quiet context guard */", "retired quiet context cannot animate an unrelated native proxy"),
        ("temple-quiet-proxy-prefix-throw", "TempleQuietProxy.cs", "__state = __instance.service.DevotionLevel;\n        }\n        catch (Exception error) { Report(__instance, error); }", "__state = __instance.service.DevotionLevel;\n        }\n        catch (Exception error) { Report(__instance, error); throw; }", "quiet presentation fault cannot prevent original native donation"),
        ("temple-quiet-proxy-postfix-throw", "TempleQuietProxy.cs", "TownServiceTempleController.AnimateProxy(__instance, __state);\n        }\n        catch (Exception error) { Report(__instance, error); }", "TownServiceTempleController.AnimateProxy(__instance, __state);\n        }\n        catch (Exception error) { Report(__instance, error); throw; }", "quiet presentation fault cannot interrupt completed native donation"),
        ("temple-quiet-proxy-unbounded-warn", "TempleQuietProxy.cs", "if (ReferenceEquals(_reported, temple)) return;", "if (temple == null) return;", "quiet presentation faults warn once per exact native controller"),
        ("temple-quiet-restore-sibling", "TempleQuietController.cs", "Source.SetSiblingIndex(_sibling);", "Source.SetSiblingIndex(0);", "quiet counter lease restores exact original parent sibling rect and nested canvas state"),
        ("temple-close-missing", "TempleExit.cs", "ModalFallback.CloseFloatedWindow(_window);", "", "physical departure closes native temple before visiting another resident"),
        ("repeat-donation", "RitualTransactions.cs", "_submittedOfferings.Add(offering);", "", "a delayed online stock refresh never permits a duplicate donation"),
        ("donation-revision-missing", "RitualTransactions.cs", "TownServiceMirror.MarkLocalTempleDonationCommitted();", "", "shared blessing revision advances only after each native donation callback"),
        ("visitor-departure", "RitualTransactions.cs", "_templeOffering?.VisitorPresent == true && TemplePendingEligible(temple!, slot!)", "TemplePendingEligible(temple!, slot!)", "walking away before native completion cancels the donation"),
        ("modal-input-gate", "RitualTransactions.cs", "_templeOffering?.VisitorPresent == true && TemplePendingEligible(temple!, slot!)", "_templeOffering?.Available == true && TemplePendingEligible(temple!, slot!)", "deliberate purse drop invokes exactly one native payment callback"),
        ("occupied-temple-accepted", "RitualTransactions.cs", "&& TownServiceMirror.CanLocalBeginTransaction(2)", "&& true", "purse parks without running native selection before transaction arbitration"),
        ("unsettled-donation-direct", "RitualTransactions.cs", "if (!TownServiceMirror.LocalTransactionSettled(2)) return;", "", "unsettled transaction claim never enters the native donation callback"),
        ("grant-timeout-disabled", "RitualTransactions.cs", "Time.unscaledTime - pending.Started > 2f", "false", "missing host grant returns the purse and exposes native Temple fallback before selection"),
        ("busy-grant-not-rejected", "RitualTransactions.cs", "bool denied = TownServiceMirror.LocalTransactionDenied(2);", "bool denied = false;", "host Busy returns only this purse without reopening the original window"),
        ("busy-grant-incorrect-fallback", "RitualTransactions.cs", "valid && !denied && (unavailable || timedOut)", "valid && (denied || unavailable || timedOut)", "host Busy returns only this purse without reopening the original window"),
        ("unreachable-grant-no-fallback", "RitualTransactions.cs", "bool unavailable = TownServiceMirror.LocalTransactionUnavailable(2);", "bool unavailable = false;", "unreachable host returns purse and restores the original VR Temple window"),
        ("delayed-validation", "RitualGuard.cs", "_box != null && _valid()", "_box != null", "delayed owner change cancels original transaction"),
        ("delayed-cancel", "RitualGuard.cs", "else cancel?.Invoke();", "else if (!requested) cancel?.Invoke();", "delayed owner change cancels original transaction"),
        ("duplicate-completion", "RitualGuard.cs", "if (_completed) return;", "", "duplicate hidden completion is one shot"),
        ("scope-boundary", "RitualGuard.cs", " || !ReferenceEquals(scope._box, box)", "", "unrelated box retains its native callbacks"),
        ("affordability-race", "RitualTransactions.cs", "Func<bool> stillValid = () => _sessionAlive() && TownServiceMirror.LocalTransactionSettled(2)\n            && pendingEligible()", "Func<bool> stillValid = () => _sessionAlive() && TownServiceMirror.LocalTransactionSettled(2)", "post-selection affordability refused"),
        ("owner-race", "RitualTransactions.cs", "&& ReferenceEquals(context, _context()) && ReferenceEquals(selected, identity());", "&& ReferenceEquals(selected, identity());", "post-selection owner change refused"),
        ("item-race", "RitualTransactions.cs", "&& ReferenceEquals(context, _context()) && ReferenceEquals(selected, identity());", "&& ReferenceEquals(context, _context());", "post-selection selected item change refused"),
        ("existing-prompt", "RitualTransactions.cs", " || box.GetComponent<UIWindow>().IsOpen\n            || !quietTemple", "\n            || !quietTemple", "existing unrelated prompt untouched"),
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
    parser.add_argument("--only-mutation", action="append", metavar="NAME",
                        help="Run production and the named negative control only; may be repeated")
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
        available = mutations()
        selected = set(args.only_mutation or [])
        unknown = selected - {case[0] for case in available}
        if unknown:
            parser.error("Unknown mutation(s): " + ", ".join(sorted(unknown)))
        variants += [case for case in available if not selected or case[0] in selected]
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, _ in variants[1:]:
        if filename not in bound:
            raise RuntimeError(f"Production binding drift: missing {filename} for {name}")
        replace_once(bound[filename], before, after)
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
