using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Party;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>The resident's palm is a transaction request, never payment. Original native
/// confirmation remains the only way to commit buying or selling an inspected card.</summary>
internal static class TownServiceMerchantHandoff
{
    private static TownServiceStation? _station;
    private static Transform? _palm, _seat, _zone;
    private static CanvasGroup? _zoneGate;
    private static TMP_Text? _caption;
    private static CMapCharacter? _character;
    private static ItemsPile? _fan;
    private static readonly List<CItem> Items = new();
    private static uint _itemRevision;
    private static float _nextItems, _started, _pendingUntil;
    private static bool _near, _resetting;
    private static CItem? _pending;
    private static bool _selling;
    private static uint _pendingSession;
    private static Action? _ourConfirmation;
    private static ItemsPile.ItemChip? _offeredChip;
    private static TownServiceToken? _offeredStock;
    private static TownServiceOfferingCard? _offering;
    private static Transform? _ownedHome;
    private static CMapParty? _party;
    private static ShopService? _shop;
    private static CItem? _eligibilityItem;
    private static bool _eligibilitySelling, _eligibilityResult;
    private static float _eligibilityUntil;
    internal static bool Active => _fan != null && _character != null && !_resetting;
    internal static uint Session { get; private set; }
    internal static float SessionAge => Mathf.Max(0f, Time.unscaledTime - _started);
    internal static Transform? StationRoot => _station?.Root;
    internal static Transform? Zone => _zone;
    internal static IReadOnlyList<ItemsPile.ItemChip> OwnedChips => _fan != null
        ? _fan.InspectionChips : Array.Empty<ItemsPile.ItemChip>();

    internal static void Tick()
    {
        if (_resetting) return;
        TownServiceCatalog.CanOffer = CanOffer;
        TownServiceCatalog.Offer = Offer;
        TownServiceCatalog.InOfferingZone = InOfferingZone;
        TownServiceCatalog.RetainOffer = RetainStock;
        CMapCharacter? selected = MapRoomHand.OwnedMerchantCharacter();
        EGuildmasterMode mode = GuildmasterDestinations.CurrentDestinationMode();
        bool context = MapRoomDriver.Active && WorldUIConfig.ImmersiveTownServices.Value
            && TownServiceEnhancementHandoff.Enabled && !StoryComposite.PointOfNoReturn
            && selected != null && TownServicePopulation.Available(1)
            && (mode == EGuildmasterMode.None || mode == EGuildmasterMode.Merchant);
        if (!context) { Reset(); return; }
        TownServiceStation? station = TownServicePopulation.Acquire(1);
        if (!ReferenceEquals(station, _station))
        {
            ResetSession(); _station = station; _palm = null; _near = false;
        }
        bool near = _station != null && _station.IsLocalVisitorNear(_near);
        if (!near) { Reset(); return; }
        _near = true;
        if (!ReferenceEquals(_character, selected))
        {
            if (VRHands.Left?.Grabber.Held is VRCard || VRHands.Right?.Grabber.Held is VRCard) return;
            ResetSession(restoreFan: false); _character = selected;
            unchecked { Session++; if (Session == 0) Session++; }
            _started = Time.unscaledTime;
            _fan = ItemsPile.CreateInspection(OnOwnedRelease);
            MapRoomHand.SetMerchantInspection(true);
            _nextItems = 0f;
        }
        if (Time.unscaledTime >= _nextItems)
        {
            _nextItems = Time.unscaledTime + .2f;
            List<CItem> current = selected!.AllCharacterItems;
            bool changed = current.Count != Items.Count;
            for (int i = 0; !changed && i < current.Count; i++) changed = !ReferenceEquals(current[i], Items[i]);
            if (changed)
            { Items.Clear(); Items.AddRange(current); unchecked { _itemRevision++; } }
        }
        _fan!.TickInspection(Items, _itemRevision);
        TickPending();
        TickConfirmation();
    }

    internal static void LateTick()
    {
        if (!Active || _station == null) return;
        _palm ??= Find(_station.Root, "ActivityOfferingPalm");
        if (_palm == null) return;
        if (_seat == null)
        {
            _seat = new GameObject("GloomhavenVR.Merchant.OfferingPalm").transform;
            _seat.SetParent(_station.Root, false);
            TMP_Text? font = Singleton<UIGuildmasterHUD>.Instance?.shopWindow?.GetComponentInChildren<TMP_Text>(true);
            _zone = TownServiceMerchantZone.CreateTemplate(font).transform;
            _zone.SetParent(_seat, false); _zone.localPosition = Vector3.zero; _zone.localRotation = Quaternion.identity;
            ((RectTransform)_zone).sizeDelta = new Vector2(170f, 240f);
            ((RectTransform)_zone.Find("Border")).sizeDelta = new Vector2(170f, 240f);
            _caption = _zone.Find("Caption").GetComponent<TMP_Text>();
            _caption.rectTransform.sizeDelta = new Vector2(150f, 100f);
            _zoneGate = _zone.GetComponent<CanvasGroup>();
            VRLayers.Apply(_seat.gameObject);
        }
        TownServiceOfferingPose.Place(_seat, _palm, _station.Root, SessionAge);
        _offering?.Tick();
        bool heldOwned = HeldOwned(VRHands.Left) || HeldOwned(VRHands.Right);
        // Cabinet card eligibility is supplied by the same predicate through its release host.
        bool heldStock = TownServiceCatalog.HeldOfferAvailable;
        _caption!.text = Loc.Mod(heldOwned ? "town_merchant_sell" : "town_merchant_buy");
        _zoneGate!.alpha = _offering == null && _offeredStock == null && (heldOwned || heldStock) ? 1f : 0f;
    }

    private static bool HeldOwned(VRHand? hand) => hand != null && hand.Grabber.Held is ItemsPile.ItemChip chip
        && ReferenceEquals(chip.Owner, _fan) && chip.Item != null && CanOffer(chip.Item, true);

    internal static bool InOfferingZone(Vector3 world)
    {
        if (!Active || _palm == null || !_near) return false;
        return _seat != null && TownServiceOfferingPose.Contains(_seat, world);
    }
    private static ShopService? Shop()
    {
        CMapParty? party = AdventureState.MapState?.MapParty;
        if (party == null) return null;
        if (!ReferenceEquals(_party, party)) { _party = party; _shop = new ShopService(party, _ => { }); }
        return _shop;
    }
    internal static bool CanOffer(CItem item, bool selling) => Eligible(item, selling, cached: true);
    private static bool Eligible(CItem item, bool selling, bool cached)
    {
        if (!Active || item == null || !item.Tradeable || _pending != null || _offering != null || _offeredStock != null
            || !ReferenceEquals(MapRoomHand.OwnedMerchantCharacter(), _character)) return false;
        UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
        if (confirmation != null && confirmation.IsActive) return false;
        if (cached && ReferenceEquals(_eligibilityItem, item) && _eligibilitySelling == selling
            && Time.unscaledTime < _eligibilityUntil) return _eligibilityResult;
        ShopService? shop = Shop();
        if (shop == null) return false;
        _eligibilityItem = item; _eligibilitySelling = selling;
        _eligibilityUntil = Time.unscaledTime + .12f;
        _eligibilityResult = selling ? shop.GetItemsToSell(_character).Contains(item)
            : shop.IsAffordable(item, _character) && shop.GetItemsToBuy(_character).Exists(candidate => candidate.ID == item.ID);
        return _eligibilityResult;
    }
    private static void OnOwnedRelease(ItemsPile.ItemChip chip, Vector3 world)
    {
        if (_resetting || chip.Item == null || !ReferenceEquals(chip.Owner, _fan)
            || !Offer(chip.Item, true, world) || _seat == null) return;
        _offeredChip = chip; _ownedHome = chip.transform.parent;
        chip.TownOffering = true; chip.TownOfferingReclaimed = Reclaim;
        chip.CancelReleaseGlide();
        float scale = chip.transform.lossyScale.x / Mathf.Max(.0001f, _seat.lossyScale.x);
        _offering = new TownServiceOfferingCard(chip.transform, _seat, scale);
    }
    internal static bool Offer(CItem item, bool selling, Vector3 world)
    {
        if (!Eligible(item, selling, cached: false) || !InOfferingZone(world)) return false;
        EGuildmasterMode mode = GuildmasterDestinations.CurrentDestinationMode();
        if (mode != EGuildmasterMode.Merchant)
        {
            if (mode != EGuildmasterMode.None || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Merchant)
                || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI) return false;
            MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Merchant, "item offered to merchant");
        }
        _pending = item; _selling = selling; _pendingSession = Session;
        _pendingUntil = Time.unscaledTime + 3f;
        return true;
    }
    private static void TickPending()
    {
        if (_pending == null) return;
        if (!PendingCurrent() || Time.unscaledTime > _pendingUntil) { _pending = null; ReleaseOffering(); return; }
        UIShopItemWindow? window = Singleton<UIGuildmasterHUD>.Instance?.shopWindow;
        if (window == null || !window.GetComponent<UIWindow>().IsOpen) return;
        UIShopItemInventory inventory = window.ItemInventory;
        if (inventory == null || inventory.service == null || !ReferenceEquals(inventory.character, _character)) return;
        CItem item = _pending;
        bool opened = TownServiceMerchantTransaction.Commit(inventory, item, _selling, PendingCurrent);
        _pending = null;
        if (opened) _ourConfirmation = Singleton<UIItemConfirmationBox>.Instance?._onConfirmedCallback;
        else ReleaseOffering();
    }
    private static void RetainStock(TownServiceToken token)
    {
        if (_pending == null || _seat == null) return;
        _offeredStock = token;
        token.ParkOffering(_seat, Reclaim);
    }

    private static void TickConfirmation()
    {
        if (_pending != null || _ourConfirmation == null) return;
        UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
        if (confirmation != null && confirmation.IsActive
            && ReferenceEquals(confirmation._onConfirmedCallback, _ourConfirmation)) return;
        _ourConfirmation = null;
        ReleaseOffering();
    }

    private static void Reclaim()
    {
        Action? callback = _ourConfirmation; _ourConfirmation = null; _pending = null;
        // Withdraw the display before the native cancellation can reenter teardown. A card
        // already adopted by a hand is never reparented or flown out of that hand.
        ReleaseOffering();
        UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
        if (callback != null && confirmation != null && confirmation.IsActive
            && ReferenceEquals(confirmation._onConfirmedCallback, callback)) confirmation.OnCancel();
    }

    private static void ReleaseOffering()
    {
        ItemsPile.ItemChip? chip = _offeredChip; _offeredChip = null;
        TownServiceToken? stock = _offeredStock; _offeredStock = null;
        _offering = null;
        if (chip != null)
        {
            chip.TownOffering = false; chip.TownOfferingReclaimed = null;
            if (chip.Holder == null && _ownedHome != null) chip.transform.SetParent(_ownedHome, true);
            _fan?.ResumeInspection(chip);
        }
        _ownedHome = null;
        stock?.ReturnOffering();
    }

    private static bool PendingCurrent() => Active && Session == _pendingSession
        && ReferenceEquals(MapRoomHand.OwnedMerchantCharacter(), _character)
        && GuildmasterDestinations.CurrentDestinationMode() == EGuildmasterMode.Merchant;

    private static Transform? Find(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root) { Transform? found = Find(child, name); if (found != null) return found; }
        return null;
    }
    private static void ResetSession(bool restoreFan = true)
    {
        if (_resetting) return;
        _resetting = true;
        // Withdraw ownership before native callbacks. OnCancel may synchronously raise onHidden,
        // which can reenter teardown; it must not cancel twice or destroy the same fan again.
        Action? ownedConfirmation = _ourConfirmation; _ourConfirmation = null;
        ReleaseOffering();
        ItemsPile? fan = _fan; _fan = null;
        Transform? seat = _seat; _seat = _zone = null; _zoneGate = null; _caption = null;
        _pending = null; _eligibilityItem = null; Items.Clear(); _character = null;
        try
        {
            UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
            if (ownedConfirmation != null && confirmation != null && confirmation.IsActive
                && ReferenceEquals(confirmation._onConfirmedCallback, ownedConfirmation)) confirmation.OnCancel();
        }
        finally
        {
            try { fan?.DestroyInspection(); }
            finally
            {
                if (seat != null) UnityEngine.Object.Destroy(seat.gameObject);
                try { if (restoreFan) MapRoomHand.SetMerchantInspection(false); }
                finally { _resetting = false; }
            }
        }
    }
    internal static void Reset()
    {
        if (_resetting) return;
        ResetSession(); _station = null; _palm = null; _near = false;
        _party = null; _shop = null;
        TownServiceCatalog.CanOffer = null; TownServiceCatalog.Offer = null; TownServiceCatalog.InOfferingZone = null; TownServiceCatalog.RetainOffer = null;
    }
}
