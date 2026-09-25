using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
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

    internal bool AllowsHand(VRHand hand) => Available
        && hand != (VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left);

    internal static void TickApproach()
    {
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
            || !TownServiceEnhancementHandoff.Enabled || StoryComposite.PointOfNoReturn
            || MapRoomHand.OwnedMerchantCharacter() == null || !TownServicePopulation.Available(2))
        { _approachInside = false; return; }
        TownServiceStation? station = TownServicePopulation.Acquire(2);
        bool near = station != null && station.IsLocalVisitorNear(_approachInside)
            && TownServiceOfferingPose.VisitorWithin(station.Root, _approachInside ? 1.65f : 1.4f);
        if (!near) { _approachInside = false; return; }
        // Another service can take the native destination while the visitor remains in
        // this radius. Keep its latch clear so returning from that service can approach
        // the priestess again; a deliberate close of Temple itself still stays latched.
        EGuildmasterMode destination = GuildmasterDestinations.CurrentDestinationMode();
        if (destination != EGuildmasterMode.None && destination != EGuildmasterMode.Temple)
        { _approachInside = false; return; }
        if (_approachInside || Time.unscaledTime < _approachAt
            || VRHands.Left?.Grabber.Held is VRCard || VRHands.Right?.Grabber.Held is VRCard) return;
        if (destination != EGuildmasterMode.None
            || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI
            || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Temple)) return;
        _approachInside = true;
        _approachAt = Time.unscaledTime + .5f;
        MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Temple, "approached priestess");
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
        _inspectionNear = selected != null && _temple.character != null
            && selected.CharacterID == _temple.character.CharacterID && priest != null
            && priest.IsLocalVisitorNear(_inspectionNear)
            && TownServiceOfferingPose.VisitorWithin(_station, _inspectionNear ? 1.65f : 1.4f);
        // Native modal focus temporarily disables ritual input. That must not rebuild
        // the ordinary ability-card fan over the physical purse and shared bowl.
        // A card already held by a hand retains its normal return path first.
        MapRoomHand.SetTempleInspection(_inspectionNear && !holdingCard);
        _near = input && _inspectionNear && !holdingCard;
        if (_near) _visited = true;
        VRHand? hand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        bool held = false;
        foreach (TownServiceRitual.Piece piece in _ritual.Pieces)
        {
            held |= piece.Token.IsHeld;
            if (!_near) piece.Token.CancelInspection();
        }
        Available = _near && hand != null && hand.HasPose;
        bool shown = Available && hand!.Grabber.Held == null
            && (CardsConfig.RevealAlways || hand.PalmGate.IsOpen || held);
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
            piece.SetVisibility(piece.Token.IsHeld ? 1f : _visibility);
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
