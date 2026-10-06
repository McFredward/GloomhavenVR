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
    shared = path.with_name("TownServiceSharedCue.cs")
    bound = {path.name: source, pose.name: pose.read_text(), feedback.name: feedback.read_text(),
             mask.name: mask.read_text(), shared.name: shared.read_text()}
    poke = (root / "src/GloomhavenVR/Hands/Interact/PokeInteractor.cs").read_text()
    bound["PokeInteractor.cs"] = poke
    card = (root / "src/GloomhavenVR/Cards/VRCard.cs").read_text()
    start = card.index("    public override bool CanGrab =>")
    gate = card[start:card.index(";", start) + 1]
    grab = (root / "src/GloomhavenVR/Hands/Interact/ProximityGrabber.cs").read_text()
    force = "\n".join(method(grab, signature) for signature in (
        "public bool ForceGrab(", "private static bool CanGrabNow(",
        "private static bool AllowsHandNow("))
    bound["ActualGrabRoute.cs"] = "using System; using GloomhavenVR.Hands; using GloomhavenVR.Hands.Interact; namespace GloomhavenVR.Cards { public partial class VRCard { " + gate + " } } namespace GloomhavenVR.Hands { public partial class Holder { " + force + " } }"
    ray = (root / "src/GloomhavenVR/Hands/Interact/RayUguiDriver.cs").read_text()
    arbitration = method(ray, "private static bool OfferedAreaClear(").replace(
        "private static bool OfferedAreaClear(", "internal static bool OfferedAreaClear(", 1)
    bound["NativeAreaOcclusionFixture.cs"] = "namespace GloomhavenVR.Hands.Interact { internal static class NativeAreaOcclusionFixture {\n" + arbitration + "\n} }"
    physical = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServicePhysicalRay.cs").read_text()
    physics_scan = method(physical, "internal static float OtherPhysicsOccludingDistance(")
    scratch = ("    private const int PhysicsScanCapacity = 64;\n"
        "    private static readonly RaycastHit[] PhysicsScanHits = new RaycastHit[PhysicsScanCapacity];\n")
    if scratch not in physical:
        raise RuntimeError("Production physics ray scratch differs from the runtime fixture")
    bound["NativePhysicsOcclusionFixture.cs"] = ("using UnityEngine; using GloomhavenVR.Cards; "
        "namespace GloomhavenVR.WorldUI { internal static class NativePhysicsOcclusionFixture {\n"
        + scratch + physics_scan + "\n} }")
    visual = (root / "src/GloomhavenVR/Hands/Interact/RayInteractor.cs").read_text()
    visual_distance = method(visual, "private static float ResolveVisualHitDistance(").replace(
        "private static float ResolveVisualHitDistance(",
        "internal static float ResolveVisualHitDistance(", 1)
    bound["NativeVisualDistanceFixture.cs"] = ("using UnityEngine; "
        "namespace GloomhavenVR.Hands.Interact { internal static class NativeVisualDistanceFixture {\n"
        + visual_distance + "\n} }")
    import importlib.util
    helper_path = root / "scripts/bind-town-quiet-controller.py"
    spec = importlib.util.spec_from_file_location("quiet_handoff_binding", helper_path)
    helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
    quiet, quiet_hashes = helper.sources(root); bound.update(quiet)
    hashes = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}
    hashes.update(quiet_hashes)
    return bound, hashes


def check_presentation_bridge(root):
    """The resident must acquire both native windows before replacing their hand surfaces."""
    source = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServicePresentation.cs").read_text()
    tick = source[source.index("    private static void TickCore()"):
                  source.index("    private static ConvertedPanel? FindContext(")]
    def has_temple_approach(body):
        mage = body.find("TownServiceEnhancementHandoff.TickApproach();")
        temple = body.find("TownServiceTempleOffering.TickApproach();")
        return mage >= 0 and temple >= 0 and "if (!quietTemple) TownServiceEnhancementHandoff.TickApproach();" in body

    if not has_temple_approach(tick):
        raise RuntimeError("Native temple approach is not polled beside enchantress approach")
    property_body = source[source.index("internal static bool UsesImmersiveEnhancement =>"):
                           source.index(";", source.index("internal static bool UsesImmersiveEnhancement =>"))]
    def has_mage_ownership(body):
        return "_enhancementListVeil != null" in body and "Service == 3" in body

    if not has_mage_ownership(property_body):
        raise RuntimeError("Enchantress native card list is released before immersive ownership is established")
    # Each checker must reject the defect it guards. An assertion that also accepts
    # its own planted regression would provide no useful integration evidence.
    if has_temple_approach(tick.replace("TownServiceTempleOffering.TickApproach();", "", 1)):
        raise RuntimeError("Temple approach negative control was accepted")
    if has_mage_ownership(property_body.replace("_enhancementListVeil != null", "true", 1)):
        raise RuntimeError("Mage ownership negative control was accepted")


def check_laser_bridge(root):
    """The runtime pointer test proves the real GraphicRaycaster hit; bind its
    result to the far-ray arbitration seam without opening other card occlusion."""
    ray = (root / "src/GloomhavenVR/Hands/Interact/RayUguiDriver.cs").read_text()
    ray_state = (root / "src/GloomhavenVR/Hands/Interact/RayInteractor.cs").read_text()
    physical = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServicePhysicalRay.cs").read_text()
    mask = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceNativeEnhancementCardMask.cs").read_text()
    visual = (root / "src/GloomhavenVR/Hands/Interact/RayInteractor.cs").read_text()
    capture = (root / "src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.4.Content.cs").read_text()
    def scoped(ray_text, physical_text, mask_text, state_text, visual_text, capture_text):
        start = ray_text.index("bool nativeAreaThroughOfferedCard = false;")
        end = ray_text.index("// Physics occlusion:", start)
        bridge = ray_text[start:end]
        arbitration = method(ray_text, "private static bool OfferedAreaClear(")
        return ("TryNativeAreaCanvas(best, out VRCard? offered)" in bridge
            and "TryOfferedCardDistance(offered" in bridge
            and "TryNativeArea(best, checkedAreaTop.gameObject" in bridge
            and "ReferenceEquals(areaCard, offered)" in bridge
            and "OfferedAreaClear(bestDist, cardDist" in bridge
            and "_hand.Ray.BoardOccluderDistance" in bridge
            and "OtherOccludingDistance(offered" in bridge
            and "OtherPhysicsOccludingDistance(offered" in bridge
            and "nearestSolid >= card - epsilon" in arbitration
            and "board >= panel - epsilon" in arbitration
            and "otherTownObject >= panel - epsilon" in arbitration
            and "otherPhysics >= panel - epsilon" in arbitration
            and ray_text[end:].count("if (best != null && !nativeAreaThroughOfferedCard") == 2
            and "if (!ReferenceEquals(entries[i].Target, offered)" in physical_text
            and "|| candidate is VRCard card && TownServiceEnhancementHandoff.CanReclaim(card)" in physical_text
            and "collider.transform.IsChildOf(offered.transform)" in physical_text
            and "Physics.RaycastNonAlloc(origin, direction, PhysicsScanHits, maxDistance, mask)" in physical_text
            and "if (count == PhysicsScanHits.Length) return 0f;" in physical_text
            and "return Mathf.Min(ordinary, solidDistance);" in visual_text
            and "SolidOccluderDistance, maxDistance * 0.25f);" in visual_text
            and "TownServiceNativeEnhancementCardMask.TryAuraCaptureBounds(host," in capture_text
            and "float expansion = offeredAura ? 3f : MaxContentExpansion;" in capture_text
            and "_hasCaptureBounds" in mask_text
            and "ignoreReversedGraphics" not in mask_text
            and "BoardOccluderDistance = liveBoard;" in state_text
            and "BoardOccluderDistance = float.PositiveInfinity;" in state_text)
    if not scoped(ray, physical, mask, ray_state, visual, capture):
        raise RuntimeError("Native laser exception is not limited to the live area on its own offered card")
    for index, (broken_ray, broken_physical, broken_mask, broken_state, broken_visual, broken_capture) in enumerate((
        (ray.replace("TryNativeArea(best, checkedAreaTop.gameObject", "TryNativeArea(best, null", 1), physical, mask, ray_state, visual, capture),
        (ray, physical.replace("if (!ReferenceEquals(entries[i].Target, offered)", "if (entries[i].Target == null", 1), mask, ray_state, visual, capture),
        (ray, physical.replace("if (count == PhysicsScanHits.Length) return 0f;", "if (count < 0) return 0f;", 1), mask, ray_state, visual, capture),
        (ray.replace("_hand.Ray.BoardOccluderDistance,", "float.PositiveInfinity,", 1), physical, mask, ray_state, visual, capture),
        (ray.replace("WorldUI.TownServicePhysicalRay.OtherOccludingDistance(offered,", "WorldUI.TownServicePhysicalRay.OccludingDistance(", 1), physical, mask, ray_state, visual, capture),
        (ray.replace("WorldUI.TownServicePhysicalRay.OtherPhysicsOccludingDistance(offered,", "WorldUI.TownServicePhysicalRay.OccludingDistance(", 1), physical, mask, ray_state, visual, capture),
        (ray, physical, mask.replace("_highlighterRect = highlighter != null",
            "if (highlighter != null) highlighter.GetComponentInParent<GraphicRaycaster>().ignoreReversedGraphics = false;\n        _highlighterRect = highlighter != null", 1), ray_state, visual, capture),
        (ray, physical, mask, ray_state.replace("BoardOccluderDistance = liveBoard;", "BoardOccluderDistance = float.PositiveInfinity;", 1), visual, capture),
        (ray, physical, mask, ray_state, visual.replace("SolidOccluderDistance, maxDistance * 0.25f);", "float.PositiveInfinity, maxDistance * 0.25f);", 1), capture),
        (ray.replace("if (best != null && !nativeAreaThroughOfferedCard", "if (best != null && nativeAreaThroughOfferedCard", 1), physical, mask, ray_state, visual, capture),
        (ray, physical, mask, ray_state, visual, capture.replace("TownServiceNativeEnhancementCardMask.TryAuraCaptureBounds(host,", "NoAuraBounds(host,", 1)),
        (ray, physical, mask, ray_state, visual, capture.replace("float expansion = offeredAura ? 3f : MaxContentExpansion;", "float expansion = MaxContentExpansion;", 1)),
    )):
        if scoped(broken_ray, broken_physical, broken_mask, broken_state, broken_visual, broken_capture):
            raise RuntimeError(f"Native laser bridge negative control {index} escaped")


def mutations():
    name = "TownServiceEnhancementHandoff.cs"
    return [
        ("native-print-real-geometry", "TownServiceNativeEnhancementCardMask.cs",
         "        AlignNativePrint();", "        /* retain nominal slab mapping */",
         "native selectable print corners match the adopted face at every card rotation, pivot and inspect scale"),
        ("native-print-pivot-alignment", "TownServiceNativeEnhancementCardMask.cs",
         "physicalCenter - nativeCenter", "Vector3.zero",
         "native selectable print corners match the adopted face at every card rotation, pivot and inspect scale"),
        ("native-print-restore", "TownServiceNativeEnhancementCardMask.cs",
         "_highlighterRect.localScale = _printRootScale;", "/* omit original print geometry restore */",
         "native print ancestor geometry returns to its captured owner when the offer ends"),
        ("native-world-z-phase", "TownServiceNativeEnhancementCardMask.cs",
         "_aura.rotation.eulerAngles.z", "_aura.localRotation.eulerAngles.z",
         "native world-Z animation keeps its original phase in the moving card plane"),
        ("offered-card-presentation-home", name,
         "Transform drawn = Card != null ? Card.transform : _seat;", "Transform drawn = _seat;",
         "native world-Z tween follows the exact offered-card position and normal before publication"),
        ("late-native-card-placement", name,
         "if (surface.Id == 11) surface.Tick(Vector3.zero, Quaternion.identity, 1f);", "if (surface.Id == 11) { }",
         "native world-Z tween follows the exact offered-card position and normal before publication"),
        ("late-native-aura-correction", name,
         "TownServiceNativeEnhancementCardMask.RefreshCurrent();", "/* omit late correction */",
         "native world-Z tween follows the exact offered-card position and normal before publication"),
        ("native-cancel-release", name, "owner.Return();\n                }", "/* retain old offering */\n                }", "original native cancellation runs once and retires the exact parked card and selection"),
        ("native-cancel-callback", name, "try { original?.Invoke(); }", "try { /* omit native cancellation */ }", "original native cancellation runs once and retires the exact parked card and selection"),
        ("native-cancel-reservation", name, "TownServiceMirror.SetLocalTransactionActive(3, false);", "/* leave reservation active */", "native cancellation immediately releases the physical resident reservation"),
        ("native-cancel-replacement", name, "&& ReferenceEquals(owner.Card, offered)\n                    && TownServicePresentation.Active", "&& TownServicePresentation.Active", "a delayed prior cancellation cannot remove or release a replacement offering"),
        ("local-only-palm", name, "_feedback.Paint(showCue, preview);", "_feedback.Paint(showCue, preview); if (!TownServiceMirror.CanShowLocalCue(3)) _zoneGate.alpha = 0f;", "another picture author never hides this visitor's valid local palm cue"),
        ("local-guide-response", "TownServiceSharedCue.cs", "internal static void PaintLocal(CanvasGroup? gate, Transform? zone) { }", "internal static void PaintLocal(CanvasGroup? gate, Transform? zone) { if (zone != null) TownServiceOfferFeedback.PaintInk(zone, zone.Find(\"Border\")?.GetComponent<UnityEngine.UI.Image>(), .81f); }", "another visitor cannot change the actual local guide ink scale or haptics"),
        ("retired-remote-guide", "TownServiceSharedCue.cs", "gate.alpha = 0f; gate.interactable = gate.blocksRaycasts = false;", "gate.alpha = 1f; gate.interactable = gate.blocksRaycasts = true;", "legacy shared guide samples never restore remote pre-drop drawing or occupation"),
        ("readiness-freshness", "TownServiceSharedCue.cs", "if (!visitor.Ready || now - visitor.Received > FreshSeconds) continue;", "if (!visitor.Ready) continue;", "expired visitor readiness cannot leave a phantom global offering pose"),
        ("quiet-window-gate", "QuietControllerFixture.cs", "window.IsOpen || TownServicePresentation.IsQuietController(window, service)", "window.IsOpen", "first opening shows neutral palm locator while native input remains blocked"),
        ("walkaway-window", name, "ModalFallback.CloseFloatedWindow(current._window);", "", "walking away closes empty native service through its existing exit path"),
        ("tiny-offer", name, "card.SetHome(_seat, Vector3.zero, Quaternion.identity, size);", "card.SetHome(_seat, Vector3.zero, Quaternion.identity, .90f);", "offered mage card preserves tracked reading size across independent resident scale"),
        ("reclaim-modal", name, "&& _current.ReclaimReady", "&& _current.Ready", "an existing physical offer remains manually reclaimable while a peer claim changes"),
        ("flat-card", name, "card.SetHome(_seat, Vector3.zero, Quaternion.identity, size);", "card.SetHome(_seat, Vector3.zero, Quaternion.Euler(75f, 0f, 0f), size);", "offered ability card is upright over the palm"),
        ("owner", name, "|| !TownServiceOfferingPose.Contains(_seat, card.transform.position) || !ValidOwner(card)", "|| !TownServiceOfferingPose.Contains(_seat, card.transform.position)", "foreign native character refuses offering"),
        ("distance", name, "|| !TownServiceOfferingPose.Contains(_seat, card.transform.position)", "", "distant release refuses offering"),
        ("native-disabled", name, "&& slot.Selectable.IsActive() && slot.Selectable.IsInteractable()", "&& slot.Selectable.IsActive()", "native row refresh retains a visible locator without pretending the disabled slot accepts a card"),
        ("owner-race", name, "if (!Ready || !ValidOwner(card) || _shop.selectedCard == null", "if (!Ready || _shop.selectedCard == null", "native callback owner race refuses offering"),
        ("return", name, "if (card != null && !card.IsHeld && presentation != null) BeginReturn(presentation);", "if (card != null && !card.IsHeld && presentation != null) CardsDriver.RequestRebuild();", "revoked grant at native callback dispatch closes confirmation without committing or waiting for Tick"),
        ("return-life", name, "finally { presentation.Started = true; PruneReturns(); }", "finally { presentation.Started = true; Returns.Remove(presentation); PruneReturns(); }", "return presentation survives ritual disposal with actual face and fixed identity"),
        ("reclaim", name, "Detach(); ClearNativeSelection();", "Detach();", "manual reclaim clears native options"),
        ("startup", name,
         "if (!ValidOwner(Card) || !_alive() || _palm == null || _station == null || _shop == null || _window == null || !TownServiceQuietController.IsOpen(_window, 3)\n            || _shop.selectedCard",
         "if (!ValidOwner(Card) || !_alive() || !_input() || _palm == null || _station == null || _shop == null || _window == null || !TownServiceQuietController.IsOpen(_window, 3)\n            || _shop.selectedCard",
         "opening input fade retains offering"),
        ("preview-commit", name,
         "&& !_shop._isConfirmationBoxOpened && _alive() && _input()\n        && (Card == null ? TownServiceMirror.CanLocalBeginTransaction(3)\n            : !_claimPending && TownServiceMirror.LocalTransactionSettled(3));",
         "&& !_shop._isConfirmationBoxOpened && _alive()\n        && (Card == null ? TownServiceMirror.CanLocalBeginTransaction(3)\n            : !_claimPending && TownServiceMirror.LocalTransactionSettled(3));",
         "first opening shows neutral palm locator while native input remains blocked"),
        ("head-proximity", name, "bool headEntered = head != null && !_headInside && NearVisitor(approachRoot, head.transform.position, 2.4f);", "bool headEntered = false;", "head proximity opens original service without a held card"),
        ("gaze-reach", name,
         "bool headEntered = head != null && !_headInside && NearVisitor(approachRoot, head.transform.position, 2.4f);",
         "bool headEntered = head != null && !_headInside && NearVisitor(approachRoot, head.transform.position, 1.4f);",
         "intentional fan-hand focus switches the local native destination from merchant to enchantress"),
        ("deferred-approach", name, "if (!_pendingApproach || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI)", "if (!_pendingApproach || !headEntered && !cardEntered || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI)", "intentional fan-hand focus switches the local native destination from merchant to enchantress"),
        ("bounded-retry", name, "if (!headEntered && !cardEntered && now < _approachRetryAt) return;", "if (now < -1f) return;", "pending native rail retries are rate-limited between frames"),
        ("head-edge", name, "if (headEntered) _headInside = true;", "", "head proximity opens original service without a held card"),
        ("card-edge", name, "if (cardEntered) _cardInside = true;", "", "owned card approach requests original callbacks without opening a flat window"),
        ("quiet-flat-native-press", "QuietControllerFixture.cs", "        _requested = service;", "        MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Enchantress, \"mutant\"); _requested = service;", "quiet entry preserves the exact selected slot without selecting it again"),
        ("trade-confirmation", name, "&& tradeConfirmation.GetComponent<UIWindow>() is UIWindow tradeWindow && tradeWindow.IsOpen) return;", "&& false) return;", "a live merchant purchase confirmation is never interrupted by resident approach"),
        ("head-overlap-steal", name,
         "if (!magePreferred) return;",
         "",
         "head-only overlap keeps the nearer idle merchant destination and its item hand intact"),
        ("nearest-resident", name,
         "|| head != null && NearestResidentForHead(approachRoot, head.transform.position);",
         ";",
         "head proximity opens original service without a held card"),
        ("attention-overlap", name,
         "|| (destination == EGuildmasterMode.Merchant || destination == EGuildmasterMode.None)\n                && mageStation != null && mageStation.IsLocalVisitorNear(_magePreferredInside)",
         "",
         "local face attention opens the offering enchantress's card cue despite a nearer idle merchant and averted gaze"),
        ("fan-release", name,
         "MapRoomHand.SetMerchantInspection(false);",
         "{ }",
         "merchant item fan is released after the native enchantress visit succeeds"),
        ("native-ability-census", name,
         "MapRoomHand.HasOwnedTownAbilityCards() : HasOwnedMapCard();",
         "HasOwnedMapCard() : HasOwnedMapCard();",
         "intentional fan-hand focus switches the local native destination from merchant to enchantress"),
        ("temple-purse-release", name,
         "MapRoomHand.SetTempleInspection(false);",
         "{ }",
         "temple purse hand is released after the native enchantress visit succeeds"),
        ("held-merchant-offer", name,
         "if (destination == EGuildmasterMode.Merchant && TownServiceMerchantHandoff.WantsOffering) return;",
         "if (destination == EGuildmasterMode.Temple && TownServiceMerchantHandoff.WantsOffering) return;",
         "parked merchant item is not interrupted by a competing mage hand focus"),
        ("hand-focus-radius", name,
         "|| !NearVisitor(mage.Root, wrist.position, 1.05f)",
         "|| !NearVisitor(mage.Root, wrist.position, 1.05f) || true",
         "fan hand toward the enchantress requests ability cards while both residents remain near"),
        ("incompatible-host", name,
         "|| !TownServiceGrantSync.CanUseImmersive\n            || !TownServicePopulation.Available(3))",
         "|| !TownServicePopulation.Available(3))",
         "an incompatible multiplayer host leaves the original native service path available"),
        ("empty-resident-hand", name,
         "&& (_current._zoneGate.alpha > 0f || _current.Card != null)\n        && TownServicePresentation.Active",
         "&& TownServicePresentation.Active",
         "offering hand remains lowered until a native locator has been painted"),
        ("preference-edge", name,
         "if (magePreferred && !_magePreferredInside)",
         "if (magePreferred)",
         "explicit close remains closed while card stays near"),
        ("other-peer-claim", name,
         "&& (Card == null ? TownServiceMirror.CanLocalBeginTransaction(3)\n            : !_claimPending && TownServiceMirror.LocalTransactionSettled(3));",
         "&& true;",
         "another visitor's parked card blocks this Mage only before local physical offer"),
        ("claim-before-native", name,
         "if (Card == null && !TownServiceMirror.LocalTransactionSettled(3))",
         "if (Card != null && !TownServiceMirror.LocalTransactionSettled(3))",
         "physical card parks and keeps offering hand visible before resident claim settles"),
        ("pending-native-release", name,
         "if (!TownServiceMirror.LocalTransactionSettled(3)) return;",
         "if (_claimDeadline <= 0f) return;",
         "unsettled competing visitor claim cannot invoke original native selection"),
        ("lost-claim-return", name,
         "|| Time.unscaledTime >= _claimDeadline)",
         "|| false)",
         "lost resident claim returns the real card without invoking native selection"),
        ("host-busy-return", name,
         "|| _window == null || !TownServiceQuietController.IsOpen(_window, 3) || TownServiceMirror.LocalTransactionDenied(3)",
         "|| _window == null || !TownServiceQuietController.IsOpen(_window, 3) || false",
         "host Busy returns the parked Mage card immediately without native selection"),
        ("revoked-selected-grant", name,
         "if (TownServiceMirror.LocalTransactionDenied(3)\n            || !TownServiceMirror.LocalTransactionSettled(3))\n        { Return(); return; }",
         "if (TownServiceMirror.LocalTransactionDenied(3))\n        { Return(); return; }",
         "revoked selected grant closes even an unconverted prompt and returns card on Tick"),
        ("fast-confirm-hide", name,
         "else if (_shop._isConfirmationBoxOpened && window.IsOpen)",
         "else if (_shop._isConfirmationBoxOpened && !window.IsOpen)",
         "revoked selected grant closes even an unconverted prompt and returns card on Tick"),
        ("revoked-confirm-click", name,
         "&& TownServiceMirror.LocalTransactionSettled(3))\n            { original(); return; }",
         ")\n            { original(); return; }",
         "revoked grant at native callback dispatch closes confirmation without committing or waiting for Tick"),
        ("flat-confirm-fallback", name,
         "internal static Action GuardNativeConfirmation(Action original)\n    {\n        TownServiceEnhancementHandoff? owner = _current;\n        if (owner == null || !HasCurrentOffering) return original;",
         "internal static Action GuardNativeConfirmation(Action original)\n    {\n        TownServiceEnhancementHandoff? owner = _current;\n        if (owner == null) return original;",
         "an inactive immersive service leaves the original confirmation callback untouched"),
        ("other-service", name,
         "if (destination != EGuildmasterMode.None && destination != EGuildmasterMode.Merchant\n            && destination != EGuildmasterMode.Temple) return;",
         "if (destination != EGuildmasterMode.None) return;",
         "intentional fan-hand focus switches the local native destination from merchant to enchantress"),
        ("modal", name, "|| Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI", "", "modal confirmation prevents proximity opening"),
        ("empty-palm", name, "bool heldEligible = leftHeld == null && rightHeld == null || held != null;", "bool heldEligible = held != null;", "empty ready palm advertises an owned offering without requiring a held card"),
        ("model-refresh", name, "a.ID == b.ID", "ReferenceEquals(a, b)", "physical card starts a new per-resident grant request"),
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
         "if (surface.Id == 11 && ReferenceEquals(surface.Panel.HostCanvas, canvas))",
         "if (surface.Id == 11 || ReferenceEquals(surface.Panel.HostCanvas, canvas))",
         "unrelated canvas and non-ability card print never steal the physical reclaim trigger"),
        ("stale-area-selection", name,
         "if (current == null || current.Card == null || current._claimPending || !current.Ready) return false;",
         "if (current == null || current.Card == null || current._claimPending) return false;",
         "native confirmation closes the area-selection laser gate"),
        ("squeezed-native-frame", "TownServiceNativeEnhancementCardMask.cs",
         "_nativeFrame.localScale = Vector3.one;", "_nativeFrame.localScale = _frameScale;",
         "world-space full-card frame occupies the card rather than a squeezed vertical strip and cannot steal native clicks"),
        ("native-frame-resquash", "TownServiceNativeEnhancementCardMask.cs",
         "private void LateUpdate()\n    {\n        if (_masked) AlignNativeEffects();\n    }",
         "private void LateUpdate()\n    {\n        if (!_masked) AlignNativeEffects();\n    }",
         "native flat animation cannot resquash the physical frame before render"),
        ("aura-render-squeeze", "TownServiceNativeEnhancementCardMask.cs",
         "inkScale.x *= Mathf.Clamp(diameter / width, .025f, 40f);", "inkScale.x *= 1f;",
         "rendered native aura follows the actual physical card, not the 325x450 highlighter root"),
        ("aura-rotated-parent", "TownServiceNativeEnhancementCardMask.cs",
         "_aura.localScale = new Vector3(Mathf.Sign(scale.x) * uniform * parentMean / parentX,",
         "_aura.localScale = new Vector3(Mathf.Sign(scale.x) * uniform,",
         "repeated render callbacks do not change a sheared-parent ring diameter"),
        ("aura-group-instead-of-ink", "TownServiceNativeEnhancementCardMask.cs",
         "return ink;", "return branch;",
         "native aura reserves its full width beyond CardHilight's narrow host for panel capture"),
        ("aura-capture-shrink", "TownServiceNativeEnhancementCardMask.cs",
         "mask._captureBounds = mask._hasCaptureBounds\n                    ? Rect.MinMaxRect(",
         "mask._captureBounds = false\n                    ? Rect.MinMaxRect(",
         "later smaller pulse cannot collapse the reserved capture frame across the cyan ring"),
        ("solid-visual-miss", "NativeVisualDistanceFixture.cs",
         "return Mathf.Min(ordinary, solidDistance);", "return ordinary;",
         "laser ends at offered physical card even when the game physics ray mask excludes it"),
        ("stale-ui-through-card", "NativeVisualDistanceFixture.cs",
         "float ordinary = !float.IsPositiveInfinity(uiDistance) ? uiDistance",
         "if (!float.IsPositiveInfinity(uiDistance)) return uiDistance;\n        float ordinary = !float.IsPositiveInfinity(uiDistance) ? uiDistance",
         "native enhancement area can receive the pointer while its visible laser ends on the physical card"),
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
    parser.add_argument("--only-mutation", action="append", metavar="NAME",
                        help="Run production and the named negative control only; may be repeated")
    args = parser.parse_args()
    check_presentation_bridge(args.source_root)
    check_laser_bridge(args.source_root)
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
        selected = set(args.only_mutation or [])
        available = mutations()
        unknown = selected - {case[0] for case in available}
        if unknown:
            parser.error("Unknown mutation(s): " + ", ".join(sorted(unknown)))
        variants += [case for case in available if not selected or case[0] in selected]
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    # Validate every selected mutation before compiling any assembly. Confirmation
    # and cancellation intentionally share eligibility guards; a mutation must name
    # its exact method instead of silently modifying both or failing after dozens
    # of unrelated builds have already completed.
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
