using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.MapRoom;
using TMPro;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Loan the owner's real map card to the resident's palm. Selection stays native;
/// taking the card back or leaving cancels the selection and never purchases anything.</summary>
internal sealed class TownServiceEnhancementHandoff : IDisposable
{
    private static TownServiceEnhancementHandoff? _current;
    private static float _approachAt;
    private static float _approachSearchAt;
    private static Transform? _approachPalm;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VRCard, object> Reclaimed = new();
    private readonly UINewEnhancementWindow _shop;
    private readonly UIWindow _window;
    private readonly Func<bool> _alive;
    private readonly Func<bool> _input;
    private readonly Transform _seat;
    private readonly CanvasGroup _zoneGate;
    private readonly Transform _station;
    private Transform? _palm;
    private float _palmSearchAt;
    private CAbilityCard? _model;
    private bool _disposed;
    internal VRCard? Card { get; private set; }
    internal AbilityCardUI? NativeSource { get; private set; }
    internal Transform Zone { get; }
    internal Transform? Face => Card != null ? Card.GetComponentInChildren<FullAbilityCard>(true)?.transform : null;

    internal TownServiceEnhancementHandoff(UINewEnhancementWindow shop, Transform station,
        Func<bool> alive, Func<bool> input)
    {
        _shop = shop; _window = shop.GetComponent<UIWindow>(); _station = station; _alive = alive; _input = input;
        _current?.Dispose();
        _seat = new GameObject("GloomhavenVR.TownService.OfferingPalm").transform;
        _seat.SetParent(station, false);
        Zone = TownServiceMerchantZone.CreateTemplate(shop.GetComponentInChildren<TMP_Text>(true)).transform;
        Zone.SetParent(_seat, false); Zone.localPosition = new Vector3(0f, .014f, 0f);
        ((RectTransform)Zone).sizeDelta = new Vector2(240f, 170f);
        ((RectTransform)Zone.Find("Border")).sizeDelta = new Vector2(240f, 170f);
        TMP_Text label = Zone.Find("Caption").GetComponent<TMP_Text>();
        label.text = Loc.Mod("town_enchant_card"); label.fontSize = 26f;
        label.rectTransform.sizeDelta = new Vector2(220f, 70f);
        _zoneGate = Zone.GetComponent<CanvasGroup>(); _zoneGate.alpha = 0f;
        VRLayers.Apply(_seat.gameObject);
        _current = this;
        ClearNativeSelection();
    }

    internal static bool IsParked(VRCard card) => _current != null && ReferenceEquals(_current.Card, card);
    internal static bool CanReclaim(VRCard card) => IsParked(card) && _current!.Ready
        && MapRoomHand.TryOwnedTownCard(card, out _, out _);
    internal static bool ReturnReclaimed(VRCard card)
    {
        if (!Reclaimed.Remove(card)) return false;
        CardsDriver.ReturnTownOffering(card);
        return true;
    }
    private bool Ready => !_disposed && _shop != null && _window != null && _window.IsOpen
        && !_shop._isConfirmationBoxOpened && _alive() && _input();

    internal static void TickApproach()
    {
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
            || StoryComposite.PointOfNoReturn || !TownServicePopulation.Available(3)
            || Time.unscaledTime < _approachAt
            || GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.Enchantress) return;
        VRCard? held = HeldOwnedCard(VRHands.Left) ?? HeldOwnedCard(VRHands.Right);
        if (held == null) return;
        if (_approachPalm == null && Time.unscaledTime >= _approachSearchAt)
        {
            _approachSearchAt = Time.unscaledTime + .5f;
            TownServiceStation? station = TownServicePopulation.Acquire(3);
            _approachPalm = station != null ? FindPalm(station.Root) : null;
        }
        Transform? palm = _approachPalm;
        if (palm == null || !Near(palm, held.transform.position, .85f)
            || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Enchantress)) return;
        _approachAt = Time.unscaledTime + 1f;
        MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Enchantress, "owned card offered to enchantress");
    }

    private static VRCard? HeldOwnedCard(VRHand? hand) => hand != null && hand.HasPose
        && hand.Grabber.Held is VRCard card && MapRoomHand.TryOwnedTownCard(card, out _, out _) ? card : null;

    private static Transform? FindPalm(Transform root)
    {
        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == "ActivityOfferingPalm") return node;
        return null;
    }

    private static bool Near(Transform frame, Vector3 position, float distance) =>
        frame.InverseTransformPoint(position).sqrMagnitude <= distance * distance;

    internal void Tick()
    {
        if (_disposed) return;
        if (_palm == null && _station != null && Time.unscaledTime >= _palmSearchAt)
        { _palmSearchAt = Time.unscaledTime + .5f; _palm = FindPalm(_station); }
        if (_palm != null)
        {
            _seat.SetPositionAndRotation(_palm.position, _palm.rotation);
            // Authored activity markers carry station units, not imported bone scale.
            _seat.localScale = Vector3.one;
        }
        _zoneGate.alpha = Ready && _palm != null && Card == null
            && (HeldOwnedCard(VRHands.Left) != null || HeldOwnedCard(VRHands.Right) != null) ? 1f : 0f;
        if (Card == null)
        {
            // Native character changes can automatically select their first card. Until a real
            // offering arrives, no remote stock proxy may silently choose on the player's behalf.
            if (_shop != null && _shop.selectedCard != null) ClearNativeSelection();
            return;
        }
        if (!ValidOwner(Card) || !_alive() || _palm == null || _shop == null || !_window.IsOpen
            || _shop.selectedCard == null || _shop.selectedCard.AbilityCard != _model
            || VRRigDriver.HeadCamera != null && !Near(_seat, VRRigDriver.HeadCamera.transform.position, 2.25f))
            Return();
        else NativeSource = _shop.selectedCard;
    }

    private bool ValidOwner(VRCard card) => MapRoomHand.TryOwnedTownCard(card, out var owner, out _)
        && _shop != null && _shop.character != null && owner != null
        && _shop.character.CharacterID == owner.CharacterID;

    internal static bool TryOffer(VRCard card)
    {
        TownServiceEnhancementHandoff? target = _current;
        if (target == null) return false;
        try { return target.Offer(card); }
        catch (Exception ex)
        {
            // Let the driver's ordinary inspection-release path reclaim the physical card
            // even when a native callback rejects it. No pending purchase was created here.
            target.Detach();
            VRLog.Warn("WorldUI", "TOWN ENHANCEMENT: original card selection refused; returning the card: " + ex.Message);
            return false;
        }
    }

    private bool Offer(VRCard card)
    {
        if (!Ready || Card != null || _palm == null || card == null || card.IsHeld
            || !Near(_seat, card.transform.position, .28f) || !ValidOwner(card)
            || !MapRoomHand.TryOwnedTownCard(card, out _, out var model)) return false;
        UIEnhanceCardSlot? found = null;
        foreach (UIEnhanceCardSlot slot in _shop.CardsDisplay.slotsPool)
            if (slot != null && slot.gameObject.activeInHierarchy && slot.AbilityCard != null
                && slot.AbilityCard.AbilityCard == model && slot.Selectable != null
                && slot.Selectable.IsActive() && slot.Selectable.IsInteractable()) { found = slot; break; }
        if (found == null) return false;
        // Original selection changes only the candidate; gold and enhancements still require
        // the original rune confirmation path. Recheck after callbacks may rebuild the pool.
        found.Select();
        if (!Ready || !ValidOwner(card) || _shop.selectedCard == null
            || _shop.selectedCard.AbilityCard != model) return false;
        Reclaimed.Remove(card);
        Card = card; NativeSource = _shop.selectedCard; _model = model;
        CardFan.Current?.Remove(card);
        card.Grabbed += OnGrabbed;
        card.SetHome(_seat, new Vector3(0f, .035f, 0f), Quaternion.Euler(75f, 0f, 0f), .90f);
        card.Grabbable = true; card.InspectOnly = true; card.AllowsGateHand = true;
        VRLog.Debug("WorldUI", "TOWN ENHANCEMENT: actual owned hand card offered to resident palm.");
        return true;
    }

    private void OnGrabbed(VRCard card, VRHand hand)
    {
        if (!ReferenceEquals(Card, card)) return;
        Reclaimed.Remove(card); Reclaimed.Add(card, new object());
        Detach(); ClearNativeSelection();
        CardsDriver.RequestRebuild();
    }

    private VRCard? Detach()
    {
        VRCard? card = Card;
        Card = null; NativeSource = null; _model = null;
        if (card != null) card.Grabbed -= OnGrabbed;
        return card;
    }

    private void ClearNativeSelection()
    {
        if (_shop == null || _window == null || !_window.IsOpen) return;
        _shop.DeselectCurrentCard();
        _shop.OnSelectedCardToEnhance(null);
    }

    private void Return()
    {
        VRCard? card = Detach();
        ClearNativeSelection();
        if (card != null && !card.IsHeld) CardsDriver.ReturnTownOffering(card);
    }

    internal Transform? CloneOf(Transform original)
    {
        Transform? source = NativeSource != null && NativeSource.fullAbilityCard != null
            ? NativeSource.fullAbilityCard.transform : null;
        Transform? face = Face;
        if (source == null || face == null || original == null) return null;
        if (original == source) return face;
        if (!original.IsChildOf(source)) return null;
        return MapNode(source, face, original);
    }

    private static Transform? MapNode(Transform source, Transform clone, Transform original)
    {
        if (original == source) return clone;
        Transform? parent = MapNode(source, clone, original.parent);
        int index = original.GetSiblingIndex();
        if (parent == null || index >= parent.childCount) return null;
        Transform candidate = parent.GetChild(index);
        // Printed ability rows can share names; structural identity avoids mapping both
        // enhancement stickers onto the first same-named Transform.Find result.
        return candidate.name == original.name ? candidate : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Return(); _disposed = true;
        if (ReferenceEquals(_current, this)) _current = null;
        if (_seat != null) UnityEngine.Object.Destroy(_seat.gameObject);
    }
}
