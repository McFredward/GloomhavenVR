using System;
using System.Collections.Generic;
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
    internal static bool HasCurrentOffering => _current != null && !_current._disposed
        && _current.Card != null && _current._window != null && _current._window.IsOpen
        && TownServicePresentation.Active && TownServicePresentation.Service == 3;
    private static float _approachSearchAt;
    private static Transform? _approachPalm;
    private static VRCard? _approachCard;
    private static bool _headInside, _cardInside;
    internal static bool Enabled => WorldUIConfig.MapRoomHand == null ? Defaults.MapRoomHand : WorldUIConfig.MapRoomHand.Value;
    private static System.Runtime.CompilerServices.ConditionalWeakTable<VRCard, ReturnPresentation> Reclaimed = new();
    private static bool _hasReclaimed;
    private static readonly List<ReturnPresentation> Returns = new(2);
    /// <summary>The actual card remains mirrorable through its full return, independently of
    /// native pooled widgets and the ritual window's lifetime.</summary>
    internal sealed class ReturnPresentation
    {
        internal readonly VRCard Card;
        internal readonly int CardId;
        internal readonly Transform StationRoot;
        internal readonly uint Session;
        private readonly float _started, _sessionAge;
        internal bool Started;
        internal float SessionAge => _sessionAge + Mathf.Max(0f, Time.unscaledTime - _started);
        internal Transform? Face => Card != null ? Card.GetComponentInChildren<FullAbilityCard>(true)?.transform : null;
        internal Transform? Body => Card != null ? Card.transform.Find("Visual/Backing") : null;
        internal ReturnPresentation(VRCard card, int cardId, Transform station)
        {
            Card = card; CardId = cardId; StationRoot = station;
            Session = TownServicePresentation.Session; _sessionAge = TownServicePresentation.SessionAge;
            _started = Time.unscaledTime;
        }
    }
    internal static IReadOnlyList<ReturnPresentation> Returning
    { get { PruneReturns(); return Returns; } }

    private static void PruneReturns()
    {
        if (!MapRoomDriver.Active || !CardsDriver.OffScenarioFanActive)
        {
            Returns.Clear();
            if (_hasReclaimed) { Reclaimed = new(); _hasReclaimed = false; }
            return;
        }
        for (int i = Returns.Count - 1; i >= 0; i--)
        {
            ReturnPresentation entry = Returns[i];
            if (entry.Card == null || entry.Card.IsHeld
                || entry.Started && !entry.Card.IsFlying && !entry.Card.IsVanishing)
                Returns.RemoveAt(i);
        }
    }
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
    private bool _disposed, _confirmationSeen;
    private float _offeredHeight;
    internal VRCard? Card { get; private set; }
    internal AbilityCardUI? NativeSource { get; private set; }
    internal UIEnhanceCardSlot? NativeSlot { get; private set; }
    internal Transform Zone { get; }
    internal Transform Seat => _seat;
    internal Transform? Face => Card != null ? Card.GetComponentInChildren<FullAbilityCard>(true)?.transform : null;

    internal TownServiceEnhancementHandoff(UINewEnhancementWindow shop, Transform station,
        Func<bool> alive, Func<bool> input)
    {
        _shop = shop; _window = shop.GetComponent<UIWindow>(); _station = station; _alive = alive; _input = input;
        _current?.Dispose();
        _seat = new GameObject("GloomhavenVR.TownService.OfferingPalm").transform;
        _seat.SetParent(station, false);
        Zone = TownServiceMerchantZone.CreateTemplate(shop.GetComponentInChildren<TMP_Text>(true)).transform;
        Zone.SetParent(_seat, false); Zone.localPosition = Vector3.zero; Zone.localRotation = Quaternion.identity;
        ((RectTransform)Zone).sizeDelta = new Vector2(170f, 240f);
        ((RectTransform)Zone.Find("Border")).sizeDelta = new Vector2(170f, 240f);
        TMP_Text label = Zone.Find("Caption").GetComponent<TMP_Text>();
        label.text = Loc.Mod("town_enchant_card"); label.fontSize = 26f;
        label.rectTransform.sizeDelta = new Vector2(150f, 100f);
        _zoneGate = Zone.GetComponent<CanvasGroup>(); _zoneGate.alpha = 0f;
        VRLayers.Apply(_seat.gameObject);
        _current = this;
        ClearNativeSelection();
    }

    internal static bool IsParked(VRCard card)
    {
        if (_current != null && ReferenceEquals(_current.Card, card)) return true;
        PruneReturns();
        foreach (ReturnPresentation entry in Returns) if (ReferenceEquals(entry.Card, card)) return true;
        return false;
    }
    internal static bool CanReclaim(VRCard card) => _current != null && ReferenceEquals(_current.Card, card) && _current.ReclaimReady
        && MapRoomHand.TryOwnedTownCard(card, out _, out _);
    internal static bool ReturnReclaimed(VRCard card)
    {
        if (!Reclaimed.TryGetValue(card, out ReturnPresentation presentation)) return false;
        Reclaimed.Remove(card);
        BeginReturn(presentation);
        return true;
    }
    private bool ReclaimReady => !_disposed && _shop != null && _window != null && _window.IsOpen && _alive()
        && (!_shop._isConfirmationBoxOpened || TownServicePalmConfirmation.OwnsCurrent(
            Singleton<UIEnhancementConfirmationBox>.Instance?.GetComponent<UIWindow>()));
    private bool Ready => !_disposed && _shop != null && _window != null && _window.IsOpen
        && !_shop._isConfirmationBoxOpened && _alive() && _input();

    internal static void TickApproach()
    {
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value || !Enabled
            || !TownServicePopulation.Available(3))
        { _headInside = _cardInside = false; _approachCard = null; return; }
        if (_approachPalm == null && Time.unscaledTime >= _approachSearchAt)
        {
            _approachSearchAt = Time.unscaledTime + .5f;
            TownServiceStation? station = TownServicePopulation.Acquire(3);
            _approachPalm = station != null ? FindPalm(station.Root) : null;
        }
        Transform? palm = _current?._palm ?? _approachPalm;
        if (palm == null) return;
        Camera? head = VRRigDriver.HeadCamera;
        // Leaving is the same native destination exit as its former X, including selection
        // and confirmation cleanup. Returning a card alone left an empty service open forever.
        if (_current != null && _current._window.IsOpen && head != null && !Near(palm, head.transform.position, 1.8f))
        {
            TownServiceEnhancementHandoff current = _current;
            current.Return();
            ModalFallback.CloseFloatedWindow(current._window);
        }
        if (head == null || !Near(palm, head.transform.position, 1.8f)) _headInside = false;
        bool headEntered = head != null && !_headInside && Near(palm, head.transform.position, 1.4f);
        if (headEntered) _headInside = true;
        VRCard? held = HeldOwnedCard(VRHands.Left) ?? HeldOwnedCard(VRHands.Right);
        if (held != _approachCard) { _approachCard = held; _cardInside = false; }
        if (held == null || !Near(palm, held.transform.position, 1.05f)) _cardInside = false;
        bool cardEntered = held != null && !_cardInside && Near(palm, held.transform.position, .85f);
        if (cardEntered) _cardInside = true;
        // Consume proximity edges even while blocked. Closing another service or explicitly
        // closing this one never reopens it merely because the player is still standing here.
        if (!headEntered && !cardEntered || !HasOwnedMapCard()
            || GuildmasterDestinations.CurrentDestinationMode() != EGuildmasterMode.None
            || StoryComposite.PointOfNoReturn
            || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI
            || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Enchantress)) return;
        MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Enchantress,
            cardEntered ? "owned card offered to enchantress" : "approached enchantress",
            suppressNativeSound: true);
    }

    private static bool HasOwnedMapCard()
    {
        var cards = CardsDriver.OffScenarioFanCards;
        if (cards == null) return false;
        for (int i = 0; i < cards.Count; i++)
            if (MapRoomHand.TryOwnedTownCard(cards[i], out _, out _)) return true;
        return false;
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
        if (_palm != null && _station != null)
        {
            TownServiceOfferingPose.Place(_seat, _palm, _station, TownServicePresentation.SessionAge, _offeredHeight * .5f);
            // Authored activity markers carry station units, not imported bone scale.
            _seat.localScale = Vector3.one;
        }
        _zoneGate.alpha = Ready && _palm != null && Card == null && HasAvailableOwnedCard() ? 1f : 0f;
        if (Card == null)
        {
            // Native character changes can automatically select their first card. Until a real
            // offering arrives, no remote stock proxy may silently choose on the player's behalf.
            if (_shop != null && _shop.selectedCard != null) ClearNativeSelection();
            return;
        }
        if (!ValidOwner(Card) || !_alive() || _palm == null || _shop == null || !_window.IsOpen
            || _shop.selectedCard == null || !SameCard(_shop.selectedCard.AbilityCard, _model)
            || VRRigDriver.HeadCamera != null && !Near(_seat, VRRigDriver.HeadCamera.transform.position, 2.25f))
            Return();
        else
        {
            if (NativeSource != _shop.selectedCard || NativeSlot == null)
            {
                NativeSource = _shop.selectedCard;
                NativeSlot = null;
                foreach (UIEnhanceCardSlot slot in _shop.CardsDisplay.slotsPool)
                    if (slot != null && slot.AbilityCard == NativeSource) { NativeSlot = slot; break; }
            }
            UIEnhancementConfirmationBox? box = Singleton<UIEnhancementConfirmationBox>.Instance;
            if (!_shop._isConfirmationBoxOpened) _confirmationSeen = false;
            if (!_confirmationSeen && _shop._isConfirmationBoxOpened && box != null && box.GetComponent<UIWindow>().IsOpen)
            {
                _confirmationSeen = true;
                TownServicePalmConfirmation.Begin(box, _seat);
            }
        }
    }

    internal void LateTick()
    {
        if (!_disposed && _palm != null)
            TownServiceOfferingPose.Place(_seat, _palm, _station, TownServicePresentation.SessionAge, _offeredHeight * .5f);
    }

    private bool ValidOwner(VRCard card) => MapRoomHand.TryOwnedTownCard(card, out var owner, out _)
        && _shop != null && _shop.character != null && owner != null
        && _shop.character.CharacterID == owner.CharacterID;

    // The map fan resolves its model from CharacterClassManager while the native enhancement
    // list obtains the character's owned cards through its service. They normally share the
    // class pool, but a native list refresh can replace its card wrapper/model instance.
    // Owner + card ID is the stable address; reference equality would silently remove every
    // palm offer in that case even though the displayed owned card is still the same card.
    private static bool SameCard(CAbilityCard? a, CAbilityCard? b) => a != null && b != null && a.ID == b.ID;

    private bool HasAvailableOwnedCard()
    {
        var cards = CardsDriver.OffScenarioFanCards;
        if (cards == null || !Enabled) return false;
        for (int i = 0; i < cards.Count; i++)
        {
            VRCard card = cards[i];
            if (!ValidOwner(card) || !MapRoomHand.TryOwnedTownCard(card, out _, out var model)) continue;
            if (FindAvailableSlot(model) != null) return true;
        }
        return false;
    }

    private UIEnhanceCardSlot? FindAvailableSlot(CAbilityCard? model)
    {
        if (model == null) return null;
        foreach (UIEnhanceCardSlot slot in _shop.CardsDisplay.slotsPool)
            if (slot != null && slot.gameObject.activeInHierarchy && slot.AbilityCard != null
                && SameCard(slot.AbilityCard.AbilityCard, model) && slot.Selectable != null
                && slot.Selectable.IsActive() && slot.Selectable.IsInteractable()) return slot;
        return null;
    }

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
            || !TownServiceOfferingPose.Contains(_seat, card.transform.position) || !ValidOwner(card)
            || !MapRoomHand.TryOwnedTownCard(card, out _, out var model)) return false;
        UIEnhanceCardSlot? found = FindAvailableSlot(model);
        if (found == null) return false;
        // Original selection changes only the candidate; gold and enhancements still require
        // the original rune confirmation path. Recheck after callbacks may rebuild the pool.
        found.Select();
        if (!Ready || !ValidOwner(card) || _shop.selectedCard == null
            || !SameCard(_shop.selectedCard.AbilityCard, model)) return false;
        Reclaimed.Remove(card);
        Card = card; NativeSource = _shop.selectedCard; NativeSlot = found; _model = model;
        CardFan.Current?.Remove(card);
        card.Grabbed += OnGrabbed;
        // Preserve the physical reading size instead of inheriting the resident's model scale.
        // A 0.9 local scale made the ability card a postage stamp on scaled map residents.
        float handScale = VRHands.Primary?.WorldScale ?? Mathf.Abs(card.transform.lossyScale.x);
        float size = CardsConfig.InspectScale.Value * handScale / Mathf.Max(.0001f, Mathf.Abs(_seat.lossyScale.x));
        _offeredHeight = CardsConfig.CardHeight * CardsConfig.InspectScale.Value * handScale;
        card.SetHome(_seat, Vector3.zero, Quaternion.identity, size);
        card.SetHandPopSuppressed(false);
        card.ResetColliderRegion();
        card.Grabbable = true; card.InspectOnly = true; card.AllowsGateHand = true;
        VRLog.Debug("WorldUI", "TOWN ENHANCEMENT: actual owned hand card offered to resident palm.");
        return true;
    }

    private void OnGrabbed(VRCard card, VRHand hand)
    {
        if (!ReferenceEquals(Card, card)) return;
        Reclaimed.Remove(card);
        if (_model != null)
        { Reclaimed.Add(card, new ReturnPresentation(card, _model.ID, _station)); _hasReclaimed = true; }
        CancelConfirmation(); Detach(); ClearNativeSelection();
        CardsDriver.RequestRebuild();
    }

    private VRCard? Detach()
    {
        VRCard? card = Card;
        Card = null; NativeSource = null; NativeSlot = null; _model = null; _confirmationSeen = false;
        if (card != null) card.Grabbed -= OnGrabbed;
        return card;
    }

    private void ClearNativeSelection()
    {
        if (_shop == null || _window == null || !_window.IsOpen) return;
        _shop.DeselectCurrentCard();
        _shop.OnSelectedCardToEnhance(null);
    }

    private void CancelConfirmation()
    {
        UIEnhancementConfirmationBox? box = Singleton<UIEnhancementConfirmationBox>.Instance;
        if (box != null) TownServicePalmConfirmation.CancelOwned(box.GetComponent<UIWindow>());
    }

    private void Return()
    {
        CancelConfirmation();
        ReturnPresentation? presentation = Card != null && _model != null
            ? new ReturnPresentation(Card, _model.ID, _station) : null;
        VRCard? card = Detach();
        ClearNativeSelection();
        if (card != null && !card.IsHeld && presentation != null) BeginReturn(presentation);
    }

    private static void BeginReturn(ReturnPresentation presentation)
    {
        Returns.Add(presentation);
        try { CardsDriver.ReturnTownOffering(presentation.Card, () => Returns.Remove(presentation)); }
        finally { presentation.Started = true; PruneReturns(); }
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
