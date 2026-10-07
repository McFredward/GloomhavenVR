using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Owned purse presentation replaces only the map hand, never the character's
/// inventory. Native temple rows still own every available blessing and transaction.</summary>
internal sealed class TownServiceTempleOffering : IDisposable
{
    private static bool _approachInside;
    private static bool _purseFocus;
    private readonly TownServiceRitual _ritual;
    private readonly UITempleWindow _temple;
    private readonly UIWindow _window;
    private readonly Transform _station;
    private readonly CanvasGroup _gate;
    private bool _near, _inspectionNear, _disposed, _visited;
    private float _visibility;
    internal Transform Root { get; }
    internal Transform DropFrame { get; }
    internal bool InBowl(Vector3 world) => Available && TownServiceTempleBowl.Contains(DropFrame, world);
    internal bool Available { get; private set; }
    internal bool VisitorPresent => !_disposed && TownServiceOfferingPose.VisitorWithin(_station, 2.6f);

    /// <summary>Map-hand focus for the local purse. Head-volume overlap by itself never
    /// takes the item fan away from a visitor at another resident. The nearest physical
    /// resident and the revealed palm elect this local hand, independently of another
    /// native window's transient destination. Shared NPC gaze does not elect a wrist.</summary>
    internal static bool WantsPurseFocus
    {
        get
        {
            if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
                || !TownServiceGrantSync.CanUseImmersive
                || !TownServicePopulation.Available(2) || StoryComposite.PointOfNoReturn)
            { _purseFocus = false; return false; }
            TownServiceStation? station = TownServicePopulation.Acquire(2);
            if (station == null) { _purseFocus = false; return false; }
            // Once physically picked up or deposited, this very purse still owns its
            // local hand/confirmation presentation. Moving the empty fan hand toward
            // another stand may change focus only after that interaction finishes.
            if (TownServicePresentation.Ritual is TownServiceRitual ritual
                && (ritual.HasTemplePurseInHand || ritual.HasParkedTempleOffer))
            { _purseFocus = true; return true; }
            if (VRHands.Left?.Grabber.Held is VRCard || VRHands.Right?.Grabber.Held is VRCard
                || VRHands.Left?.Grabber.Held is ItemsPile.ItemChip || VRHands.Right?.Grabber.Held is ItemsPile.ItemChip)
            { _purseFocus = false; return false; }
            bool deliberate = WantsPurseAtBowl(station.Root);
            // Wrist ownership is a local spatial choice, not the resident's shared gaze.
            // IsLocalVisitorNear measures from the animated eyeball and its optical
            // rotation. In multiplayer that eye can be looking at another visitor;
            // using it here can reject this visitor on the first reveal and restore
            // ordinary ability cards until the attention/pose changes. Use the stable
            // station footprint, just as the other local handoff approach gates do.
            // The visitor requested a smaller wrist activation area in the build 638 hardware
            // review. Keep this local hysteresis independent of the broader 2.6 m NPC
            // attention/committed-donation gate: an existing held/parked purse retains
            // its transaction above, while merely passing the stand keeps normal cards.
            bool templeNear = TownServiceOfferingPose.VisitorWithin(station.Root,
                _purseFocus ? 1.65f : 1.45f);
            // A parked transaction at another resident is not a reason to hide the
            // priestess's local hand. The actual destination and palm position elect
            // this fan; each resident's transaction ownership is independent.
            _purseFocus = deliberate || templeNear && NearestTempleForHead(station.Root)
                && !WantsMerchantFanAtCounter(station.Root);
            return _purseFocus;
        }
    }

    // A visible purse is an inspectable physical prop even when this character cannot
    // afford a blessing, has already donated, or another visitor owns the priestess's
    // current transaction. Those rules belong to the bowl drop and native callback.
    internal bool CanInspectPurse => _inspectionNear;

    internal bool AllowsHand(VRHand hand) => CanInspectPurse
        && hand != (VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left);

    internal static void TickApproach()
    {
        // Focus is local and physical. Presentation prepares the exact native model without
        // EnterTemple/Window.Show; a foreign flat destination cannot suppress this wrist.
        bool inside = WantsPurseFocus && MapRoomHand.OwnedMerchantCharacter() != null;
        if (inside != _approachInside && VRLog.WantsDebug)
        {
            VRHand? hand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
            Camera? head = VRRigDriver.HeadCamera;
            // BepInEx normally filters LogDebug even when the mod's Debug setting is
            // enabled. Keep this bounded transition at LogInfo behind WantsDebug,
            // so hardware reports contain the actual local wrist election facts.
            VRLog.Info("TownServices", "Temple wrist focus=" + inside
                + " nativeDestination=" + GuildmasterDestinations.CurrentDestinationMode()
                + " ownedCharacter=" + MapRoomHand.OwnedMerchantCharacter()?.CharacterID
                + " frame=" + Time.frameCount
                + " head=" + (head != null ? head.transform.position.ToString("F3") : "none")
                + " palmOpen=" + (hand?.PalmGate.IsOpen == true)
                + " fanHandHeld=" + (hand?.Grabber.Held != null));
        }
        _approachInside = inside;
        MapRoomHand.SetTempleInspection(_approachInside);
    }

    private static bool WantsPurseAtBowl(Transform station)
    {
        VRHand? hand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        if (hand == null || !hand.HasPose || hand.Grabber.Held != null
            || !(CardsConfig.RevealAlways || hand.PalmGate.IsOpen)) return false;
        // Use the same shared physical bowl as the accepted purse drop. A head-only
        // overlap cannot switch services; a hand in front of the altar can.
        Vector3 bowl = station.TransformPoint(TownServiceRitualLayout.Origin + TownServiceTempleBowl.Center);
        float scale = Mathf.Max(.01f, Mathf.Abs(station.lossyScale.x));
        return Vector3.Distance(hand.Rig.PalmCenter.position, bowl) <= .40f * scale;
    }

    private static bool NearestTempleForHead(Transform temple)
    {
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null) return false;
        Vector3 templeDelta = head.transform.position - temple.position;
        templeDelta.y = 0f;
        float templeDistance = templeDelta.magnitude;
        float tie = .12f * Mathf.Max(.01f, Mathf.Abs(temple.lossyScale.x));
        for (byte service = 1; service <= 3; service += 2)
        {
            if (!TownServicePopulation.Available(service)) continue;
            TownServiceStation? other = TownServicePopulation.Acquire(service);
            if (other == null) continue;
            Vector3 otherDelta = head.transform.position - other.Root.position;
            otherDelta.y = 0f;
            if (_purseFocus ? otherDelta.magnitude + tie < templeDistance
                : templeDistance + tie >= otherDelta.magnitude) return false;
        }
        return true;
    }

    private static bool WantsMerchantFanAtCounter(Transform temple)
    {
        VRHand? hand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        if (hand == null || !hand.HasPose || hand.Grabber.Held != null
            || !(CardsConfig.RevealAlways || hand.PalmGate.IsOpen)
            || !TownServicePopulation.Available(1)) return false;
        TownServiceStation? merchant = TownServicePopulation.Acquire(1);
        if (merchant == null) return false;
        Vector3 palm = hand.Rig.PalmCenter.position;
        Vector3 toMerchant = palm - merchant.Root.position;
        Vector3 toTemple = palm - temple.position;
        toMerchant.y = toTemple.y = 0f;
        float scale = Mathf.Max(.01f, Mathf.Abs(merchant.Root.lossyScale.x));
        // Move the free fan hand toward the neighboring physical counter, rather
        // than forcing the visitor to walk outside both overlapping head volumes.
        return toMerchant.magnitude < 1.05f * scale
            && toMerchant.magnitude + .16f * scale < toTemple.magnitude;
    }

    internal TownServiceTempleOffering(TownServiceRitual ritual, UITempleWindow temple, Transform station)
    {
        _ritual = ritual; _temple = temple; _station = station;
        _window = temple.GetComponent<UIWindow>();
        _visited = TownServiceOfferingPose.VisitorWithin(station, 2.6f);
        DropFrame = TownServiceTempleBowl.Create(station);
        Root = new GameObject("GloomhavenVR.Temple.OfferingPurses").transform;
        Root.SetParent(ritual.Root, false);
        _gate = Root.gameObject.AddComponent<CanvasGroup>();
        _gate.alpha = 0f; _gate.blocksRaycasts = false;
    }

    internal void Tick(bool input)
    {
        if (_disposed || ExitIfAway()) return;
        var selected = MapRoomHand.OwnedMerchantCharacter();
        TownServiceStation? priest = TownServicePopulation.Acquire(2);
        bool holdingCard = VRHands.Left?.Grabber.Held is VRCard || VRHands.Right?.Grabber.Held is VRCard;
        _inspectionNear = selected != null && priest != null && WantsPurseFocus;
        // Native modal focus temporarily disables ritual input. That must not rebuild
        // the ordinary ability-card fan over the physical purse and shared bowl.
        // A card already held by a hand retains its normal return path first.
        MapRoomHand.SetTempleInspection(_inspectionNear && !holdingCard);
        _near = input && _inspectionNear && !holdingCard;
        if (_near) _visited = true;
        VRHand? hand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        foreach (TownServiceRitual.Piece piece in _ritual.Pieces)
        {
            if (!_near) piece.Token.CancelInspection();
        }
        Available = _near && hand != null && hand.HasPose;
        // The purse replaces the normal card fan; it is not a permanently visible wrist prop.
        // Keep the exact card-fan reveal gesture, including RevealAlways, while separating that
        // presentation from payment eligibility. An unaffordable/already-used purse therefore
        // still appears whenever the player turns the free palm up, with its original status
        // inscription. Its collider remains usable; only the donation bowl guide and
        // native callback are withheld when that selected character cannot donate.
        bool shown = _inspectionNear && hand != null && hand.HasPose && hand.Grabber.Held == null
            && (CardsConfig.RevealAlways || hand.PalmGate.IsOpen);
        if (hand != null)
        {
            hand.PalmGate.Enabled = true;
            hand.PalmGate.EnterDegrees = CardsConfig.RevealEnterDegrees.Value;
            hand.PalmGate.ExitDegrees = CardsConfig.RevealExitDegrees.Value;
            hand.PalmGate.IgnoreWhenHandBusy = CardsConfig.RevealIgnoreWhenGrabbing.Value;
            // The normalized original bag is 15 cm tall and its root is at its base.
            // Seat that base above the visible palm; the old 7.5 cm offset buried the
            // lower half in the fingers before the player even picked it up.
            Root.position = hand.Rig.PalmCenter.position + hand.Rig.PalmCenter.up * (.12f * hand.WorldScale);
            // A purse is an upright object resting above the palm, not a card billboard.
            Vector3 forward = VRRigDriver.HeadCamera != null
                ? Root.position - VRRigDriver.HeadCamera.transform.position : _station.forward;
            forward.y = 0f;
            Root.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .001f ? forward : _station.forward, Vector3.up);
            float parentScale = Mathf.Max(.0001f, Mathf.Abs(Root.parent.lossyScale.x));
            Root.localScale = Vector3.one * (hand.WorldScale / parentScale);
        }
        _visibility = Mathf.MoveTowards(_visibility, shown ? 1f : 0f, Time.unscaledDeltaTime / .16f);
        bool movingPurse = false;
        foreach (TownServiceRitual.Piece piece in _ritual.Pieces) movingPurse |= piece.Token.IsMoving;
        // A release return is a visible physical flight even when the destination
        // palm is down. Its native inscriptions and remote ParentAlpha must not be
        // suppressed by the closed fan's ancestor CanvasGroup during that flight.
        _gate.alpha = movingPurse ? 1f : _visibility;
        _gate.interactable = shown && _visibility >= .99f;
        foreach (TownServiceRitual.Piece piece in _ritual.Pieces)
            piece.SetVisibility(TownServicePursePresentation.Visibility(piece.Token, _visibility));
    }

    private bool ExitIfAway()
    {
        if (TownServicePresentation.IsQuietTemple(_window)) return false;
        if (!_visited || _window == null || !_window.IsOpen || VRRigDriver.HeadCamera == null
            || TownServiceOfferingPose.VisitorWithin(_station, 2.6f)) return false;
        // Physical departure, not temporary input disablement, is the native destination
        // exit. Cancel before the close callback can dispose this ritual reentrantly.
        Available = _near = _inspectionNear = false;
        MapRoomHand.SetTempleInspection(false);
        foreach (TownServiceRitual.Piece piece in _ritual.Pieces) piece.Token.CancelInspection();
        ModalFallback.CloseFloatedWindow(_window);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Presentation replacement can dispose the old ritual after approach already
        // elected this wrist. A blind false write then rebuilt the ability fan in
        // the middle of the first reveal. Re-evaluate current local context instead;
        // leaving the area, disabling NPCs or reaching story commitment still clears it.
        TickApproach();
        if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        if (DropFrame != null) UnityEngine.Object.Destroy(DropFrame.gameObject);
    }
}
