using System;
using System.Collections.Generic;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Original merchant and enhancement controllers without opening their flat windows
/// or changing the map destination/party selection. Only their transaction source widgets are
/// prepared; payments and enhancements still run through the game's original confirmations.</summary>
internal static class TownServiceQuietController
{
    private static byte _requested;
    private static UIWindow? _owner;
    private static object? _party, _character;
    private static TownServiceWindowMask? _sourceMask;
    private static Transform? _sourceFrame;
    private static RectTransform? _source;
    private static bool _sourceActive;
    private static ControllerChangedEvent? _ownershipChanged;
    private static readonly PropertyInfo? SupplementaryToken = AccessTools.Property(typeof(GameAction), "SupplementaryDataToken");
    private static float _nextPointsSample;
    private static int _points = -1, _capacity = -1;
    internal static byte RequestedService
    {
        get
        {
            if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value
                || !TownServiceGrantSync.CanUseImmersive || !TownServiceEnhancementHandoff.Enabled
                || StoryComposite.PointOfNoReturn || MapRoomHand.OwnedMerchantCharacter() == null)
                return _requested = 0;
            if (_requested == 1 && !TownServiceMerchantHandoff.Active
                || _requested == 3 && !TownServiceEnhancementHandoff.HasCurrentOffering
                && !TownServiceEnhancementHandoff.KeepsQuietVisit
                && (TownServicePopulation.Acquire(3) is not TownServiceStation mage
                    || !mage.IsLocalVisitorNear(true))) return _requested = 0;
            return _requested;
        }
    }
    internal static EGuildmasterMode InteractionMode
    {
        get
        {
            byte requested = RequestedService;
            if (requested == 1) return EGuildmasterMode.Merchant;
            if (requested == 3) return EGuildmasterMode.Enchantress;
            UIWindow? current = TownServicePresentation.Window;
            if (current != null && TownServicePresentation.IsQuietTemple(current)) return EGuildmasterMode.Temple;
            return GuildmasterDestinations.CurrentDestinationMode();
        }
    }
    internal static bool IsOpen(UIWindow? window, byte service) => window != null
        && (window.IsOpen || TownServicePresentation.IsQuietController(window, service));
    internal static void EndRequest(byte service) { if (_requested == service) _requested = 0; }
    internal static bool IsSourceBoundary(Transform source) => ReferenceEquals(source, _sourceFrame)
        || _source != null && ReferenceEquals(source, _source.parent)
        || _owner != null && ReferenceEquals(source, _owner.transform);
    internal static bool OwnsWindow(UIWindow window) => _owner != null
        && (ReferenceEquals(window, _owner) || _source != null && window.transform.IsChildOf(_source));
    internal static bool OriginalVisible(Transform source, UIWindow window, byte service)
    {
        if (!TownServicePresentation.IsQuietController(window, service)
            || !ReferenceEquals(_owner, window)) return false;
        for (Transform? current = source; current != null; current = current.parent)
        {
            if (IsSourceBoundary(current)) return true;
            if (!current.gameObject.activeSelf) return false;
        }
        return false;
    }

    internal static bool Request(byte service)
    {
        if (service != 1 && service != 3 || !MapRoomDriver.Active
            || !WorldUIConfig.ImmersiveTownServices.Value || !TownServiceGrantSync.CanUseImmersive
            || !TownServiceEnhancementHandoff.Enabled || StoryComposite.PointOfNoReturn
            || MapRoomHand.OwnedMerchantCharacter() == null || !TownServicePopulation.Available(service)) return false;
        EGuildmasterMode destination = GuildmasterDestinations.CurrentDestinationMode();
        if (destination != EGuildmasterMode.None && destination != EGuildmasterMode.Merchant
            && destination != EGuildmasterMode.Temple && destination != EGuildmasterMode.Enchantress) return false;
        if (_requested != service && (TownServiceMerchantHandoff.HasParkedOffer
            || TownServiceEnhancementHandoff.HasCurrentOffering)) return false;
        if (TownServicePresentation.Ritual?.HasTemplePurseInHand == true
            || TownServicePresentation.Ritual?.HasParkedTempleOffer == true) return false;
        _requested = service;
        return true;
    }

    internal static bool Prepare(UIWindow window, byte service)
    {
        CMapCharacter? character = MapRoomHand.OwnedMerchantCharacter();
        NewPartyCharacterUI? slot = NewPartyDisplayUI.PartyDisplay?.SelectedUISlot;
        if (character == null || slot == null || slot.State != PartySlotState.Assigned
            || slot.Service?.CharacterID != character.CharacterID) return false;
        object party = AdventureState.MapState.MapParty;
        if (ReferenceEquals(_owner, window) && ReferenceEquals(_party, party)
            && ReferenceEquals(_character, character))
        {
            if (service == 3 && Time.unscaledTime >= _nextPointsSample
                && window.GetComponent<UINewEnhancementWindow>() is UINewEnhancementWindow mage)
            {
                _nextPointsSample = Time.unscaledTime + .25f;
                int points = mage.character.GetFreeEnhancementSlots();
                int capacity = AdventureState.MapState.HeadquartersState.EnhancementSlots;
                if (points != _points || capacity != _capacity)
                { mage.CardsDisplay.UpdateEnhancementPoints(); _points = points; _capacity = capacity; }
            }
            return true;
        }
        if (_owner != null) Release();
        try
        {
            _owner = window; _party = party; _character = character;
            if (service == 1)
            {
                UIShopItemWindow shop = window.GetComponent<UIShopItemWindow>();
                if (shop == null || shop.ItemInventory == null) { Release(); return false; }
                Invoke(shop, "ClearEvents");
                var nativeService = new ShopService(AdventureState.MapState.MapParty,
                    item => shop.OnUpdateNewPartyItemNotification?.Invoke(item));
                Field(shop, "service").SetValue(shop, nativeService);
                ActivateSource(shop.ItemInventory.transform);
                shop.ItemInventory.Init(nativeService, character);
                RegisterMerchantEvents(shop);
                nativeService.RegisterOnRemovedNewItemFlag(shop.ItemInventory.RefreshSellNewItemNotification);
            }
            else if (service == 3)
            {
                UINewEnhancementWindow shop = window.GetComponent<UINewEnhancementWindow>();
                if (shop == null || shop.CardsDisplay == null) { Release(); return false; }
                // Native entry bookkeeping is deliberate owner-side service use. The original
                // implementation owns its new-stock/save semantics; observers never run it.
                if (Field(shop, "shopService").GetValue(shop) is MapPartyEnhancementShopService nativeService)
                    nativeService.OnEnterShop();
                shop.character = slot.Service;
                shop.enhancementShop.Clear();
                shop.OnSelectedCardToEnhance(null);
                Field(shop, "previousSelectedCard").SetValue(shop, null);
                // Keep the original mode transition and tab value coherent. OnSelectedSlot
                // reads mode, while the visible native options retain their original state.
                shop.buyButton.Activate();
                ActivateSource(shop.CardsDisplay.transform);
                PrepareOriginalCardSlots(shop);
                MethodInfo method = Method(shop, "OnControllableOwnershipChanged");
                _ownershipChanged = (ControllerChangedEvent)Delegate.CreateDelegate(typeof(ControllerChangedEvent), shop, method);
                ControllableRegistry.OnControllerChanged += _ownershipChanged;
            }
            else { Release(); return false; }
            if (VRLog.WantsDebug)
                VRLog.Info("TownServices", "Quiet native service prepared: service=" + service
                    + " character=" + character.CharacterID + " flatWindowOpen=" + window.IsOpen);
            return true;
        }
        catch
        {
            Release();
            throw;
        }
    }

    private static void ActivateSource(Transform source)
    {
        _source = (RectTransform)source;
        _sourceActive = source.gameObject.activeSelf;
        _sourceMask = new TownServiceWindowMask(_source);
        _sourceFrame = new GameObject("GloomhavenVR.TownService.NativeTransactionSource").transform;
        _sourceMask.DetachFromPanel(_sourceFrame);
        // Only original callback sources need an active hierarchy. No service UIWindow,
        // party column, portrait, illustration or old card-list animation is activated.
        source.gameObject.SetActive(true);
    }

    private static void PrepareOriginalCardSlots(UINewEnhancementWindow shop)
    {
        UIPartyCharacterEnhancementAbilityCardsDisplay display = shop.CardsDisplay;
        Field(display, "characterData").SetValue(display, shop.character);
        Action<AbilityCardUI> selected = card => shop.OnSelectedCardToEnhance(card);
        Field(display, "onAbilityCardSelected").SetValue(display, selected);
        var assigned = (Dictionary<CAbilityCard, UIEnhanceCardSlot>)Field(display, "assignedSlots").GetValue(display)!;
        assigned.Clear();
        var pool = display.slotsPool;
        var prefab = (UIEnhanceCardSlot)Field(display, "slotPrefab").GetValue(display)!;
        List<CAbilityCard> cards = shop.character.GetOwnedAbilityCards();
        HelperTools.NormalizePool(ref pool, prefab.gameObject, display.abilityCardsPanel.content, cards.Count);
        Field(display, "slotsPool").SetValue(display, pool);
        Field(display, "selectedCard").SetValue(display, null);
        Action<AbilityCardUI> onSelected = (Action<AbilityCardUI>)Delegate.CreateDelegate(
            typeof(Action<AbilityCardUI>), display, Method(display, "OnSelectedCard"));
        for (int i = 0; i < cards.Count; i++)
        {
            // Keep original slot Init/Select and the original assigned-slot dictionary used by
            // purchase/proxy callbacks. There is no replicated enhancement/shop implementation.
            pool[i].Init(cards[i], shop.character, onSelected, null, true);
            assigned.Add(cards[i], pool[i]);
        }
        display.UpdateEnhancementPoints();
    }

    private static void RegisterMerchantEvents(UIShopItemWindow shop)
    {
        object bus = Singleton<MapChoreographer>.Instance.EventBuss;
        foreach (string callback in new[] { "OnItemBound", "OnGoldUpdate", "OnItemEquipped", "OnItemUnbound", "OnItemUnequipped" })
        {
            string registration = callback == "OnGoldUpdate" ? "RegisterToOnGoldChanged"
                : "RegisterToOnCharacter" + callback.Substring(2);
            MethodInfo register = Method(bus, registration);
            Delegate handler = Delegate.CreateDelegate(register.GetParameters()[0].ParameterType, shop, Method(shop, callback));
            register.Invoke(bus, new object[] { handler });
        }
    }

    internal static void RefreshProxyEnhancement(UINewEnhancementWindow shop, GameAction action, bool valid, bool added)
    {
        // The native proxy commits the same validated game action while the window is closed,
        // but intentionally skips its UI refresh. Refresh only this owner's quiet original
        // sources after that successful callback; never rerun payment or another player's UI.
        if (!valid || !ReferenceEquals(_owner, shop.GetComponent<UIWindow>())
            || !TownServicePresentation.IsQuietController(_owner!, 3)
            || SupplementaryToken?.GetValue(action, null) is not EnhancementToken token
            || shop.character == null) return;
        var assigned = (Dictionary<CAbilityCard, UIEnhanceCardSlot>)Field(shop.CardsDisplay, "assignedSlots")
            .GetValue(shop.CardsDisplay)!;
        foreach (CAbilityCard card in assigned.Keys)
        {
            if (card.ID != token.CardID) continue;
            bool selected = shop.selectedCard != null && shop.selectedCard.AbilityCard.ID == card.ID;
            if (added) shop.CardsDisplay.OnAddedEnhancement(card, selected);
            else shop.CardsDisplay.OnRemovedEnhancement(card, selected);
            if (selected) Method(shop, "RefreshSelectionAfterEnhance").Invoke(shop, new object[] { shop.selectedCard! });
            return;
        }
    }

    internal static void Release()
    {
        UIWindow? owner = _owner; _owner = null;
        if (_ownershipChanged != null) ControllableRegistry.OnControllerChanged -= _ownershipChanged;
        _ownershipChanged = null;
        if (owner != null && owner.GetComponent<UIShopItemWindow>() is UIShopItemWindow merchant)
            Invoke(merchant, "ClearEvents");
        if (owner != null && owner.GetComponent<UINewEnhancementWindow>() is UINewEnhancementWindow mage)
        {
            mage.OnSelectedCardToEnhance(null);
            mage.CardsDisplay.Deselect();
        }
        // Native callback sources return to their exact original hierarchy and active state.
        if (_source != null) _source.gameObject.SetActive(_sourceActive);
        _sourceMask?.Dispose(); _sourceMask = null; _source = null;
        if (_sourceFrame != null) UnityEngine.Object.Destroy(_sourceFrame.gameObject);
        _sourceFrame = null; _party = _character = null;
        _nextPointsSample = 0f; _points = _capacity = -1;
    }
    internal static void Reset() { Release(); _requested = 0; }
    private static FieldInfo Field(object owner, string name) => AccessTools.Field(owner.GetType(), name)
        ?? throw new MissingFieldException(owner.GetType().FullName, name);
    private static MethodInfo Method(object owner, string name) => AccessTools.Method(owner.GetType(), name)
        ?? throw new MissingMethodException(owner.GetType().FullName, name);
    private static void Invoke(object owner, string name) => Method(owner, name).Invoke(owner, null);
}

[HarmonyPatch(typeof(UINewEnhancementWindow), nameof(UINewEnhancementWindow.ProxyBuyEnhancement))]
internal static class QuietEnhancementBuyRefresh
{
    private static void Postfix(UINewEnhancementWindow __instance, GameAction action, bool actionValid)
        => TownServiceQuietController.RefreshProxyEnhancement(__instance, action, actionValid, added: true);
}

[HarmonyPatch(typeof(UINewEnhancementWindow), nameof(UINewEnhancementWindow.ProxySellEnhancement))]
internal static class QuietEnhancementSellRefresh
{
    private static void Postfix(UINewEnhancementWindow __instance, GameAction action, bool actionValid)
        => TownServiceQuietController.RefreshProxyEnhancement(__instance, action, actionValid, added: false);
}
