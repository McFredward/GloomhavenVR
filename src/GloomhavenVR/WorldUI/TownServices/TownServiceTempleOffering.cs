using System;
using GloomhavenVR.Cards;
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
    private static float _approachAt;
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
    internal bool VisitorPresent => !_disposed && TownServiceOfferingPose.VisitorWithin(_station, 1.65f);

    /// <summary>Map-hand focus for the local purse. Head-volume overlap by itself never
    /// takes the item fan away from a visitor at another resident. Once the original
    /// Temple service is actually open, retain its purse through the larger visit radius;
    /// a deliberate revealed hand at the bowl can request that service from elsewhere.</summary>
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
            bool deliberate = WantsPurseAtBowl(station.Root);
            bool templeOpen = GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.Temple
                && station.IsLocalVisitorNear(_purseFocus)
                && TownServiceOfferingPose.VisitorWithin(station.Root, _purseFocus ? 1.65f : 1.4f);
            // A parked transaction at another resident is not a reason to hide the
            // priestess's local hand. The actual destination and palm position elect
            // this fan; each resident's transaction ownership is independent.
            _purseFocus = deliberate || templeOpen && !WantsMerchantFanAtCounter(station.Root);
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
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
            || !TownServiceGrantSync.CanUseImmersive || !TownServiceEnhancementHandoff.Enabled
            || StoryComposite.PointOfNoReturn
            || MapRoomHand.OwnedMerchantCharacter() == null || !TownServicePopulation.Available(2))
        { _approachInside = _purseFocus = false; return; }
        TownServiceStation? station = TownServicePopulation.Acquire(2);
        bool near = station != null && station.IsLocalVisitorNear(_approachInside)
            && TownServiceOfferingPose.VisitorWithin(station.Root, 1.4f);
        if (!near) { _approachInside = false; return; }
        EGuildmasterMode destination = GuildmasterDestinations.CurrentDestinationMode();
        if (destination == EGuildmasterMode.Temple) return;
        // The prior native service can remain selected after its private fan has reset on
        // walking away. Requiring the offhand to reach this bowl before changing that
        // destination stranded the priestess after a merchant visit: no temple controller,
        // hence no purse to take to the bowl. Prefer the physically nearest resident once
        // the visitor reaches this narrower approach volume. A deliberate revealed hand
        // at the bowl still works in an overlap. Neither choice interrupts a parked deal.
        bool foreign = destination != EGuildmasterMode.None;
        bool nearestTemple = (destination is EGuildmasterMode.Merchant or EGuildmasterMode.Enchantress)
            && NearestTempleForHead(station!.Root);
        if (foreign && !WantsPurseAtBowl(station!.Root) && !nearestTemple)
        { _approachInside = false; return; }
        if (foreign) _approachInside = false;
        // The latch describes the small physical APPROACH volume, not the resident's larger
        // attention/exit hysteresis. Build 561 kept it armed through 1.65 m. The three stands are
        // close enough that walking from the priestess to another resident can remain inside that
        // larger radius; after the native temple closed the priestess still reacted, but a later
        // return never generated another approach edge and the ordinary ability fan remained.
        // Rearm as soon as the head leaves the same 1.4 m volume that opens the service. A deliberate
        // close while still standing at the bowl stays closed, while moving to another stand and
        // returning creates one fresh edge. A foreign destination clears the latch separately.
        if (_approachInside || Time.unscaledTime < _approachAt
            || VRHands.Left?.Grabber.Held is VRCard || VRHands.Right?.Grabber.Held is VRCard) return;
        if (foreign && (TownServiceMerchantHandoff.WantsOffering
            || TownServiceEnhancementHandoff.HasCurrentOffering)) return;
        if (Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI
            || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Temple)) return;
        _approachInside = true;
        _approachAt = Time.unscaledTime + .5f;
        // EnterTemple enables native selection mode. If the preceding service cleared its tab,
        // the flat UI immediately selects the first assigned slot. Preserve the exact character
        // the player was looking at across that native mode transition.
        NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
        NewPartyCharacterUI? selectedSlot = display?.SelectedUISlot;
        MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Temple, "approached priestess",
            suppressNativeSound: true);
        if (selectedSlot != null && selectedSlot.State == PartySlotState.Assigned
            && display != null && !ReferenceEquals(display.SelectedUISlot, selectedSlot))
        {
            // Use the original native slot, not a character-id lookup: campaigns can contain
            // equivalent character records and the slot is the identity the player selected.
            selectedSlot.OnClick();
        }
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
            if (templeDistance + tie >= otherDelta.magnitude) return false;
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
        _visited = TownServiceOfferingPose.VisitorWithin(station, 1.65f);
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
        _inspectionNear = selected != null && priest != null
            && priest.IsLocalVisitorNear(_inspectionNear)
            && TownServiceOfferingPose.VisitorWithin(_station, _inspectionNear ? 1.65f : 1.4f)
            && WantsPurseFocus;
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
        _gate.alpha = _visibility;
        _gate.interactable = shown && _visibility >= .99f;
        foreach (TownServiceRitual.Piece piece in _ritual.Pieces)
            piece.SetVisibility(TownServicePursePresentation.Visibility(piece.Token, _visibility));
    }

    private bool ExitIfAway()
    {
        if (!_visited || _window == null || !_window.IsOpen || VRRigDriver.HeadCamera == null
            || TownServiceOfferingPose.VisitorWithin(_station, 1.65f)) return false;
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
        _disposed = true; MapRoomHand.SetTempleInspection(false);
        if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        if (DropFrame != null) UnityEngine.Object.Destroy(DropFrame.gameObject);
    }
}
