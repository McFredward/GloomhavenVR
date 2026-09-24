using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Owned purse presentation replaces only the map hand, never the character's
/// inventory. Native temple rows still own every available blessing and transaction.</summary>
internal sealed class TownServiceTempleOffering : IDisposable
{
    private static bool _approachInside;
    private static float _approachAt;
    private readonly TownServiceRitual _ritual;
    private readonly UITempleWindow _temple;
    private readonly Transform _station;
    private readonly CanvasGroup _gate;
    private bool _near, _disposed;
    private float _visibility;
    internal Transform Root { get; }
    internal bool Available { get; private set; }

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
        if (_approachInside || Time.unscaledTime < _approachAt
            || VRHands.Left?.Grabber.Held is VRCard || VRHands.Right?.Grabber.Held is VRCard) return;
        _approachInside = true;
        if (GuildmasterDestinations.CurrentDestinationMode() != EGuildmasterMode.None
            || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI
            || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Temple)) return;
        _approachAt = Time.unscaledTime + .5f;
        MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Temple, "approached priestess");
    }

    internal TownServiceTempleOffering(TownServiceRitual ritual, UITempleWindow temple, Transform station)
    {
        _ritual = ritual; _temple = temple; _station = station;
        Root = new GameObject("GloomhavenVR.Temple.OfferingPurses").transform;
        Root.SetParent(ritual.Root, false);
        _gate = Root.gameObject.AddComponent<CanvasGroup>();
        _gate.alpha = 0f; _gate.blocksRaycasts = false;
    }

    internal void Tick(bool input)
    {
        if (_disposed) return;
        var selected = MapRoomHand.OwnedMerchantCharacter();
        TownServiceStation? priest = TownServicePopulation.Acquire(2);
        _near = input && selected != null && _temple.character != null
            && VRHands.Left?.Grabber.Held is not VRCard && VRHands.Right?.Grabber.Held is not VRCard
            && selected.CharacterID == _temple.character.CharacterID && priest != null
            && priest.IsLocalVisitorNear(_near)
            && TownServiceOfferingPose.VisitorWithin(_station, _near ? 1.65f : 1.4f);
        MapRoomHand.SetTempleInspection(_near);
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
            Root.position = hand.Rig.PalmCenter.position + hand.Rig.PalmCenter.up * (.045f * hand.WorldScale);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; MapRoomHand.SetTempleInspection(false);
        if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
    }
}
