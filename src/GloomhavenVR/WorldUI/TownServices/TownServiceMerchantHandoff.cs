using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Party;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace GloomhavenVR.WorldUI;

/// <summary>The resident's palm is a transaction request, never payment. Original native
/// confirmation remains the only way to commit buying or selling an inspected card.</summary>
internal static class TownServiceMerchantHandoff
{
    private static TownServiceStation? _station;
    private static Transform? _palm, _seat, _zone;
    private static CanvasGroup? _zoneGate;
    private static TownServiceOfferFeedback? _feedback;
    private static TMP_Text? _caption;
    private static CMapCharacter? _character;
    private static ItemsPile? _fan;
    private static readonly List<CItem> Items = new();
    private static uint _itemRevision;
    private static float _nextItems, _started, _pendingUntil, _nextCommitAt;
    private static bool _near, _resetting;
    private static CItem? _pending;
    private static bool _selling;
    private static CItem? _tradeItem;
    private static bool _tradeSelling;
    private static int _tradeBaseline;
    private static readonly HashSet<CItem> TradeOwnedBefore = new();
    private static float _tradeUntil;
    private static bool _tradePressed;
    private static bool _decisionConfirmed, _decisionCancelled;
    private static int _decisionRetries;
    private static UIItemConfirmationBox? _tradeBox;
    private static UnityAction? _tradeListener, _cancelListener;
    private static uint _pendingSession;
    private static uint _lastPendingTimeoutSession;
    private static Action? _ourConfirmation;
    private static ItemsPile.ItemChip? _offeredChip;
    private static TownServiceToken? _offeredStock;
    private static TownServiceOfferingCard? _offering;
    private static CMapParty? _party;
    private static ShopService? _shop;
    private static CItem? _eligibilityItem;
    private static bool _eligibilitySelling, _eligibilityResult;
    private static float _eligibilityUntil;
    private static TownVoiceReaction? _inspectedReaction;
    private static int _inspectedFrame;
    private static float _inspectedUntil;
    private static float _offerRetryUntil, _nextOfferFailureWarning;
    internal static bool WantsOffering => Active && _near && (_offeredChip != null || _offeredStock != null
        || HeldOwned(VRHands.Left) || HeldOwned(VRHands.Right) || TownServiceCatalog.HeldOfferAvailable);
    // A visitor's proximity is not a transaction. Other town services may keep their
    // own local fan and native destination until a card actually occupies this palm.
    internal static bool HasParkedOffer => _offeredChip != null || _offeredStock != null
        || _pending != null;
    internal static bool CanReclaim(TownServiceToken token) => Active && _near && ReferenceEquals(token, _offeredStock)
        && OwnsPendingDecision;
    internal static bool IsParkedStock(TownServiceToken token) => ReferenceEquals(token, _offeredStock)
        || TownServiceCardFlights.IsPendingStock(token);
    internal static bool CanReclaim(ItemsPile.ItemChip chip) => Active && _near && ReferenceEquals(chip, _offeredChip)
        && OwnsPendingDecision;
    private static bool OwnsPendingDecision => _pending != null || _ourConfirmation != null
        && Singleton<UIItemConfirmationBox>.Instance is UIItemConfirmationBox box && box.IsActive
        && ReferenceEquals(box._onConfirmedCallback, _ourConfirmation);
    internal static bool Active => _fan != null && _character != null && !_resetting;
    internal static uint Session { get; private set; }
    internal static float SessionAge => Mathf.Max(0f, Time.unscaledTime - _started);
    internal static Transform? StationRoot => _station?.Root;
    internal static Transform? Zone => _zone;
    internal static ItemsPile.ItemChip? PreparedPurchase => _fan?.PreparedMerchantPurchase;
    internal static IReadOnlyList<ItemsPile.ItemChip> OwnedChips => _fan != null
        ? _fan.InspectionChips : Array.Empty<ItemsPile.ItemChip>();

    internal static void Tick()
    {
        if (_resetting) return;
        TownServiceCardFlights.Tick();
        FlushStockInspectionReaction();
        TownServiceCatalog.CanOffer = CanOffer;
        TownServiceCatalog.Offer = Offer;
        TownServiceCatalog.InOfferingZone = InOfferingZone;
        TownServiceCatalog.RetainOffer = RetainStock;
        CMapCharacter? selected = MapRoomHand.OwnedMerchantCharacter();
        EGuildmasterMode mode = TownServiceQuietController.InteractionMode;
        bool context = MapRoomDriver.Active && WorldUIConfig.ImmersiveTownServices.Value
            && TownServiceGrantSync.CanUseImmersive
            && TownServiceEnhancementHandoff.Enabled && !StoryComposite.PointOfNoReturn
            && !TownServicePresentation.NativeFallbackFor(1)
            && selected != null && TownServicePopulation.Available(1)
            && (mode == EGuildmasterMode.None || mode == EGuildmasterMode.Merchant
                || mode == EGuildmasterMode.Temple)
            && !TownServiceEnhancementHandoff.WantsAbilityFan
            && !TownServiceTempleOffering.WantsPurseFocus;
        if (!context) { Reset(); return; }
        TownServiceStation? station = TownServicePopulation.Acquire(1);
        if (!ReferenceEquals(station, _station))
        {
            ResetSession(); _station = station; _palm = null; _near = false;
        }
        // The resident already turns toward a visitor at 2.4 m. Keep the private
        // presentation session alive throughout that same volume: stock inspection
        // can start at the cabinet's outer edge, and the visitor's reaction must
        // have a session to relay to the elected face author there. The palm's
        // own containment test still governs actual card placement.
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
        TickTradeOutcome();
        TickPending();
        TickConfirmation();
    }

    internal static void LateTick()
    {
        if (!Active || _station == null) return;
        // Another station can commit its native hand mode after this owner's Tick.
        // Retire the old item fan before final publication on that same frame.
        if (TownServiceQuietController.InteractionMode == EGuildmasterMode.Enchantress
            || TownServiceEnhancementHandoff.WantsAbilityFan || TownServiceTempleOffering.WantsPurseFocus)
        { Reset(); return; }
        MapRoomHand.SetMerchantInspection(true); // A native buy/sell refresh never restores the ordinary ability fan here.
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
            _feedback = new TownServiceOfferFeedback(_zoneGate, _zone);
            VRLayers.Apply(_seat.gameObject);
        }
        TownServiceOfferingPose.Place(_seat, _palm, _station.Root, SessionAge);
        _offering?.Tick();
        VRHand? ownedHand = NearestHeldOwned(_seat);
        bool heldOwned = ownedHand != null;
        // Cabinet card eligibility is supplied by the same predicate through its release host.
        bool heldStock = TownServiceCatalog.TryHeldOffer(_seat.position, out Vector3 stockPosition,
            out VRHand? stockHand, out bool stockSelling);
        Vector3 ownedPosition = heldOwned ? ((ItemsPile.ItemChip)ownedHand!.Grabber.Held!).transform.position : default;
        bool useOwned = heldOwned && (!heldStock || (ownedPosition - _seat.position).sqrMagnitude
            <= (stockPosition - _seat.position).sqrMagnitude);
        bool retryFeedback = Time.unscaledTime < _offerRetryUntil
            && !heldOwned && !heldStock && !HasParkedOffer;
        _caption!.text = Loc.Mod(retryFeedback ? "town_merchant_retry"
            : useOwned || stockSelling ? "town_merchant_sell" : "town_merchant_buy");
        // Active membership also carries the owner's near/offer intent to the shared resident
        // author. A parked card keeps that membership with alpha zero: no second overlay.
        _zone!.gameObject.SetActive(WantsOffering || retryFeedback);
        bool show = _offering == null && _offeredStock == null && (heldOwned || heldStock);
        VRHand? holder = useOwned ? ownedHand : stockHand;
        Vector3 position = useOwned ? ownedPosition : stockPosition;
        Vector3 local = show ? _seat.InverseTransformPoint(position) : Vector3.zero;
        _feedback!.Tick(show, holder, show ? local.magnitude : float.MaxValue,
            show && TownServiceOfferingPose.Contains(_seat, position), .43f);
        _feedback.Paint(show, retryFeedback);
    }

    private static bool HeldOwned(VRHand? hand) => hand != null && hand.Grabber.Held is ItemsPile.ItemChip chip
        && ReferenceEquals(chip.Owner, _fan) && chip.Item != null && CanOffer(chip.Item, true);

    private static VRHand? NearestHeldOwned(Transform seat)
    {
        VRHand? left = HeldOwned(VRHands.Left) ? VRHands.Left : null;
        VRHand? right = HeldOwned(VRHands.Right) ? VRHands.Right : null;
        if (left == null) return right;
        if (right == null) return left;
        Vector3 leftPoint = ((ItemsPile.ItemChip)left.Grabber.Held!).transform.position;
        Vector3 rightPoint = ((ItemsPile.ItemChip)right.Grabber.Held!).transform.position;
        return (leftPoint - seat.position).sqrMagnitude <= (rightPoint - seat.position).sqrMagnitude
            ? left : right;
    }

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
    internal static void StockInspected(CItem item, bool available)
    {
        // Inspection is always allowed, including an exhausted shelf or an item beyond
        // this character's purse. The native shop still refuses an invalid purchase;
        // speech explains that refusal at the first physical pickup, not after a dead drop.
        // Stock inspection does not own the merchant's private fan or transaction.
        // A player can hold this original card while the mage or temple has focus.
        if (item == null || !StockInspectionNear()) return;
        ShopService? shop = Shop();
        if (shop == null) return;
        CMapCharacter? selected = MapRoomHand.OwnedMerchantCharacter();
        _inspectedReaction = !available || selected != null
            && !shop.GetItemsToBuy(selected).Exists(candidate => candidate.ID == item.ID)
            ? TownVoiceReaction.MerchantSoldOut
            : selected != null && !shop.IsAffordable(item, selected)
                ? TownVoiceReaction.MerchantUnaffordable : TownVoiceReaction.MerchantOffer;
        _inspectedFrame = Time.frameCount;
        _inspectedUntil = Time.unscaledTime + 3f;
    }
    private static bool StockInspectionNear() => MapRoomDriver.Active
        && WorldUIConfig.ImmersiveTownServices.Value && TownServiceGrantSync.CanUseImmersive
        && TownServiceEnhancementHandoff.Enabled && !StoryComposite.PointOfNoReturn
        && !TownServicePresentation.NativeFallbackFor(1) && TownServicePopulation.Available(1)
        && TownServicePopulation.Acquire(1)?.IsLocalVisitorNear(true) == true;

    private static void FlushStockInspectionReaction()
    {
        if (_inspectedReaction is not TownVoiceReaction reaction) return;
        if (Time.unscaledTime > _inspectedUntil || !StockInspectionNear())
        { _inspectedReaction = null; return; }
        // Lifted stock publishes its independent typed membership in LateTick.
        // Retry this cosmetic request until that membership exists, never open a
        // private interaction merely to make a shared pickup line audible.
        if (Time.frameCount > _inspectedFrame && TownServiceVoice.RequestStockReaction(reaction))
            _inspectedReaction = null;
    }
    private static bool Eligible(CItem item, bool selling, bool cached)
    {
        EGuildmasterMode mode = TownServiceQuietController.InteractionMode;
        if (!Active || item == null || !item.Tradeable
            || !ReferenceEquals(MapRoomHand.OwnedMerchantCharacter(), _character)
            || !TownServiceMirror.CanLocalBeginTransaction(1)) return false;
        if (mode != EGuildmasterMode.Merchant
            && (mode != EGuildmasterMode.None && mode != EGuildmasterMode.Temple
                && mode != EGuildmasterMode.Enchantress
                || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Merchant)
                || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI
                || mode == EGuildmasterMode.Enchantress
                && TownServicePresentation.Ritual?.Handoff?.Card != null
                || mode == EGuildmasterMode.Temple
                && TownServicePresentation.Ritual?.HasParkedTempleOffer == true)) return false;
        UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
        bool ownsConfirmation = confirmation != null && confirmation.IsActive && _ourConfirmation != null
            && ReferenceEquals(confirmation._onConfirmedCallback, _ourConfirmation);
        if (confirmation != null && confirmation.IsActive && !ownsConfirmation) return false;
        CItem? current = _pending ?? _tradeItem;
        bool replacing = current != null && (!ReferenceEquals(current, item)
            || (_pending != null ? _selling : _tradeSelling) != selling);
        if (current != null && !replacing) return false;
        if (current == null && (_offering != null || _offeredStock != null)) return false;
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
    private static void OnOwnedRelease(ItemsPile.ItemChip chip, Vector3 world, VRHand hand)
    {
        if (_resetting || _seat == null || chip.Item == null || !ReferenceEquals(chip.Owner, _fan)
            || !Offer(chip.Item, true, world)) return;
        // The inspection fan passes through the actual releasing hand. Haptics belong to
        // the accepted offer edge, including a buy-to-sell or sell-to-sell replacement;
        // proximity alone cannot confirm that the native transaction accepted the card.
        if (hand.HasPose) hand.SendHaptic(HapticPreset.ClickPulse);
        _offeredChip = chip;
        chip.TownOffering = true; chip.TownOfferingReclaimed = Reclaim;
        chip.CancelReleaseGlide();
        chip.SetGrabStrip(float.MaxValue); chip.ClearHandSuppressed();
        // Both sources occupy the same physical palm card: cabinet tokens are presented at
        // 1.5 times their authored counter width, so derive the owned card's local scale from
        // that world width instead of preserving its larger wrist-reading scale.
        float stationScale = Mathf.Max(.0001f, Mathf.Abs((_station?.Root ?? _seat).lossyScale.x));
        float worldWidth = TownServiceMerchantLayout.CardWidth * stationScale * 1.5f;
        float scale = worldWidth / Mathf.Max(.0001f, chip.FaceWidth * Mathf.Abs(_seat.lossyScale.x));
        _offering = new TownServiceOfferingCard(chip.transform, _seat, scale);
    }
    internal static bool Offer(CItem item, bool selling, Vector3 world)
    {
        if (!Eligible(item, selling, cached: false) || !InOfferingZone(world)) return false;
        uint session = Session;
        EGuildmasterMode mode = TownServiceQuietController.InteractionMode;
        if (mode != EGuildmasterMode.Merchant)
        {
            if (mode != EGuildmasterMode.None && mode != EGuildmasterMode.Temple
                && mode != EGuildmasterMode.Enchantress
                || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Merchant)
                || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI
                || mode == EGuildmasterMode.Enchantress
                && TownServicePresentation.Ritual?.Handoff?.Card != null
                || mode == EGuildmasterMode.Temple
                && TownServicePresentation.Ritual?.HasParkedTempleOffer == true) return false;
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            NewPartyCharacterUI? selectedSlot = display?.SelectedUISlot;
            if (!TownServiceQuietController.Request(1)) return false;
            if (TownServiceQuietController.InteractionMode != EGuildmasterMode.Merchant)
                return false;
            // Native destination changes can select the first character. A physical
            // offering belongs to the exact original slot the visitor was inspecting.
            if (selectedSlot != null && selectedSlot.State == PartySlotState.Assigned
                && display != null && !ReferenceEquals(display.SelectedUISlot, selectedSlot))
                selectedSlot.OnClick();
        }
        if (_pending != null || _tradeItem != null || _offering != null || _offeredStock != null)
        {
            // Validate the incoming card first, then retire the complete old transaction before
            // exposing the replacement. This is one release-stack ownership transfer: the old
            // physical card resumes its canonical source and the native prompt is cancelled,
            // while the new card becomes the sole pending decision below. Cancellation can run
            // native callbacks, so recheck session ownership before accepting the replacement.
            WithdrawForReplacement();
            if (!Active || Session != session || !ReferenceEquals(MapRoomHand.OwnedMerchantCharacter(), _character))
                return false;
        }
        _pending = item; _selling = selling; _pendingSession = Session;
        _pendingUntil = Time.unscaledTime + 3f;
        _nextCommitAt = 0f; _decisionRetries = 0;
        // The native shop toggle is synchronous once its inventory is initialized. Install
        // the original decision on this release edge when possible, before the physical
        // card can finish settling on the palm. If the window is still coming up, the
        // ordinary bounded TickPending retry retains the single offer.
        TickPending();
        return ReferenceEquals(_pending, item) || ReferenceEquals(_tradeItem, item) && _tradeSelling == selling;
    }
    private static void TickPending()
    {
        if (_pending == null) return;
        if (!PendingCurrent()) { _pending = null; ReleaseOffering(); return; }
        if (Time.unscaledTime > _pendingUntil)
        {
            // The original game has not produced an actionable confirmation. Do not leave
            // a physical card in the palm without controls. One report per visit is enough
            // to diagnose this for ordinary players without logging every retry frame.
            if (_lastPendingTimeoutSession != Session)
            {
                _lastPendingTimeoutSession = Session;
                Core.VRLog.Warn("TownServices", "Merchant offer could not open its native confirmation within 3 s; returning the card to its source.");
            }
            _pending = null; ReleaseOffering(); return;
        }
        if (TownServiceMirror.LocalTransactionDenied(1))
        {
            // Another visitor acquired this NPC first. Keep the original shop
            // untouched and return only our physical card to its source.
            _pending = null; ReleaseOffering(); return;
        }
        if (Time.unscaledTime < _nextCommitAt) return;
        // A card may be parked before its private reservation reaches the other
        // clients. Never open the native purchase/sale callback while another
        // visitor could still win this NPC's bounded claim window. The physical
        // card and original item stay pending; cancellation/timeout returns it.
        if (!TownServiceMirror.LocalTransactionSettled(1)) return;
        UIShopItemWindow? window = Singleton<UIGuildmasterHUD>.Instance?.shopWindow;
        if (window == null || !TownServiceQuietController.IsOpen(window.GetComponent<UIWindow>(), 1)) return;
        UIShopItemInventory inventory = window.ItemInventory;
        if (inventory == null || inventory.service == null || !ReferenceEquals(inventory.character, _character)) return;
        CItem item = _pending;
        bool opened = TownServiceMerchantTransaction.Commit(inventory, item, _selling, PendingCurrent);
        if (opened)
        {
            _pending = null;
            UIItemConfirmationBox? box = Singleton<UIItemConfirmationBox>.Instance;
            OwnConfirmation(box);
            if (box != null && _seat != null) TownServicePalmConfirmation.Begin(box, _seat);
            _tradeItem = item; _tradeSelling = _selling;
            _tradeBaseline = ItemCount(_character, item);
            TradeOwnedBefore.Clear();
            if (_character != null)
                foreach (CItem owned in _character.AllCharacterItems) TradeOwnedBefore.Add(owned);
            _tradeUntil = 0f;
            _tradePressed = false;
            if (Core.VRLog.WantsDebug)
                Core.VRLog.Debug("TownServices", "Merchant native confirmation opened: session="
                    + Session + " side=" + (_selling ? "sell" : "buy") + ".");
            DetachTradeListener();
            if (box?.confirmButton != null)
            {
                _tradeBox = box;
                _tradeListener = () => { if (ReferenceEquals(_tradeBox, box) && _tradeItem != null) _tradePressed = true; };
                box.confirmButton.onClick.AddListener(_tradeListener);
                if (box.cancelButton != null)
                {
                    // The native button starts UIWindow.Hide before its cancellation callback
                    // runs at the end of the fade. TickConfirmation can observe the closed
                    // window in that gap and mistake an explicit cancel for a lost prompt,
                    // reopening it twice. Capture the intent on this same click and start
                    // the physical return immediately; native OnCancel still owns the dialog.
                    _cancelListener = () =>
                    {
                        if (!ReferenceEquals(_tradeBox, box) || _tradeItem == null) return;
                        _decisionCancelled = true;
                        ReleaseOffering();
                    };
                    box.cancelButton.onClick.AddListener(_cancelListener);
                }
            }
            // The item identity already tells us which side of the counter this prompt belongs
            // to. Select that family now so the resident addresses a buyer and seller differently.
            TownServiceVoice.RequestReaction(1,
                _selling ? TownVoiceReaction.MerchantSell : TownVoiceReaction.MerchantBuy);
        }
        else
        {
            // Build 572 stock-to-owned swap: the old prompt is already retired when the
            // native sell tab is rebuilt. A temporary row, input or window transition
            // refusal must not throw away the new physical card in that same frame.
            // Keep the one pending decision until the bounded offer deadline; Commit
            // rechecks native stock, ownership and the exact selectable on each attempt.
            _nextCommitAt = Time.unscaledTime + .2f;
        }
    }
    private static void OwnConfirmation(UIItemConfirmationBox? box)
    {
        _ourConfirmation = null;
        _decisionConfirmed = _decisionCancelled = false;
        if (box == null || box._onConfirmedCallback == null) return;
        Action nativeConfirm = box._onConfirmedCallback;
        Action? nativeCancel = box._onCancelCallback;
        Action owned = null!;
        owned = () =>
        {
            // The host lease may expire while the original confirmation remains open.
            // Check again at its callback, not only when the card first opened it.
            if (!TownServiceMirror.LocalTransactionSettled(1))
            {
                _decisionCancelled = true;
                nativeCancel?.Invoke();
                return;
            }
            if (ReferenceEquals(_ourConfirmation, owned))
            {
                _decisionConfirmed = true;
                _tradePressed = true;
                // The result deadline begins at the player's actual decision, never at
                // the moment a card was set down for an arbitrarily long inspection.
                _tradeUntil = Time.unscaledTime + 8f;
            }
            nativeConfirm();
        };
        box._onConfirmedCallback = owned;
        box._onCancelCallback = () =>
        {
            if (ReferenceEquals(_ourConfirmation, owned)) _decisionCancelled = true;
            nativeCancel?.Invoke();
        };
        _ourConfirmation = owned;
    }
    private static void RetainStock(TownServiceToken token)
    {
        // A synchronous native decision has already moved _pending to _tradeItem by the
        // time the catalog transfers its physical token. Both states own that exact card.
        if ((_pending == null && _tradeItem == null) || _seat == null) return;
        _offeredStock = token;
        token.ParkOffering(_seat, Reclaim);
        CItem? stock = _pending ?? _tradeItem;
        if (stock != null && !(_pending != null ? _selling : _tradeSelling) && token.PhysicalRoot != null)
            _fan?.PrepareMerchantPurchase(stock, token.PhysicalRoot);
    }

    private static void TickConfirmation()
    {
        if (_pending != null || _ourConfirmation == null) return;
        if (!_decisionConfirmed && !TownServiceMirror.LocalTransactionSettled(1)) { Reclaim(); return; }
        UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
        UIWindow? nativeWindow = confirmation?.GetComponent<UIWindow>();
        if (confirmation != null && confirmation.IsActive
            && ReferenceEquals(confirmation._onConfirmedCallback, _ourConfirmation))
        {
            if (nativeWindow != null && nativeWindow.IsOpen)
            {
                // The native dialog can outlive a converted surface: Unity may retire its
                // detached child panel during a shop refresh while leaving the transaction
                // itself open. Rebind from the authoritative callback every frame. Begin is
                // idempotent while all four original controls remain alive.
                if (_seat != null) TownServicePalmConfirmation.Begin(confirmation, _seat);
                return;
            }
            if (nativeWindow != null && nativeWindow.IsVisible) return;
            // UIWindowManager.Escape and other native owners can call UIWindow.Hide
            // directly. That leaves UIItemConfirmationBox.IsActive true forever,
            // although its underlying window has closed. The old IsActive-only
            // check retained this decision and blocked the next offer. Reconcile
            // the wrapper once the hide animation finishes, then treat the direct
            // close as cancellation. The build-572 log shows the second native
            // window closing; it does not identify which Hide caller did it.
            confirmation.Hide();
            _decisionCancelled = true;
        }
        if (confirmation != null && !confirmation.IsActive
            && nativeWindow?.IsVisible == true) return;
        // Native inventory RefreshView calls UIItemConfirmationBox.Hide without a
        // confirm or cancel callback. Retry only that still-owned card, at most
        // twice. Explicit native cancellation and confirmation never reopen it.
        if (!_decisionConfirmed && !_decisionCancelled && _tradeItem != null
            && _decisionRetries < 2 && PendingCurrent()
            && (confirmation == null || !confirmation.IsActive))
        {
            _pending = _tradeItem; _selling = _tradeSelling; _pendingSession = Session;
            _pendingUntil = Time.unscaledTime + 3f;
            _nextCommitAt = Time.unscaledTime + .2f;
            _tradeItem = null; _ourConfirmation = null; _decisionRetries++;
            DetachTradeListener();
            Core.VRLog.Warn("TownServices", "Merchant confirmation closed without a native decision; retrying the retained offer ("
                + _decisionRetries + "/2).");
            return;
        }
        _ourConfirmation = null;
        if (!_decisionConfirmed) _tradeItem = null;
        DetachTradeListener();
        // A confirm press is a request. Keep the exact parked original until the
        // native inventory confirms which ownership boundary actually completed.
        // Returning it here made a sold item fly to its former owner's wrist.
        if (_decisionConfirmed && _tradeItem != null) return;
        ReleaseOffering();
    }

    private static void DetachTradeListener()
    {
        if (_tradeBox?.confirmButton != null && _tradeListener != null)
            _tradeBox.confirmButton.onClick.RemoveListener(_tradeListener);
        if (_tradeBox?.cancelButton != null && _cancelListener != null)
            _tradeBox.cancelButton.onClick.RemoveListener(_cancelListener);
        _tradeBox = null; _tradeListener = _cancelListener = null;
    }

    private static int ItemCount(CMapCharacter? character, CItem item)
    {
        if (character == null) return 0;
        int count = 0;
        foreach (CItem owned in character.AllCharacterItems)
            if (owned != null && owned.ID == item.ID) count++;
        return count;
    }

    private static void TickTradeOutcome()
    {
        CItem? item = _tradeItem;
        if (item == null) return;
        // The eight-second deadline is for observing a *confirmed* network transaction,
        // not the lifetime of an unconfirmed card in the merchant's palm. Expiring the
        // latter left the physical card parked but severed its swap/cancel ownership.
        if (_decisionConfirmed && Time.unscaledTime > _tradeUntil)
        {
            TownServiceCardFlights.WarnUnresolvedOutcome();
            _tradeItem = null; DetachTradeListener(); ReleaseOffering(); return;
        }
        int count = ItemCount(_character, item);
        if (!_tradePressed || (_tradeSelling ? count >= _tradeBaseline : count <= _tradeBaseline)) return;
        bool sold = _tradeSelling;
        CItem? purchased = null;
        if (!sold && _character != null)
            foreach (CItem owned in _character.AllCharacterItems)
                if (owned.ID == item.ID && !TradeOwnedBefore.Contains(owned)) { purchased = owned; break; }
        // Use native ownership, never the confirmation button's visual state.
        // Cancel, swap, refusal and timeout still follow ReleaseOffering below.
        CompleteTradePresentation(sold, purchased);
        _nextItems = 0f; // the next inspection Tick must observe the authoritative new ownership
        _tradeItem = null;
        TradeOwnedBefore.Clear();
        DetachTradeListener();
        // The contextual line ran when the confirmation opened. Repeating the same family after
        // its inventory mutation sounds like a duplicated response, so completion stays silent.
    }

    private static void CompleteTradePresentation(bool sold, CItem? purchased)
    {
        ItemsPile.ItemChip? chip = _offeredChip; _offeredChip = null;
        TownServiceToken? stock = _offeredStock; _offeredStock = null;
        _offering = null;
        TownServiceMirror.SetLocalTransactionActive(1, false);
        if (_fan != null) TownServiceCardFlights.Complete(_fan, chip, stock, sold, purchased,
            _palm != null ? _palm.position : chip != null ? chip.transform.position : Vector3.zero);
        else stock?.ReturnOffering();
    }

    private static void Reclaim()
    {
        Action? callback = _ourConfirmation; _ourConfirmation = null; _pending = null;
        _tradeItem = null; _decisionConfirmed = _decisionCancelled = false; DetachTradeListener();
        // Withdraw the display before the native cancellation can reenter teardown. A card
        // already adopted by a hand is never reparented or flown out of that hand.
        ReleaseOffering();
        UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
        if (callback != null && confirmation != null && confirmation.IsActive
            && ReferenceEquals(confirmation._onConfirmedCallback, callback)) confirmation.OnCancel();
    }

    /// <summary>A missing host response cannot turn a physical merchant offer into a flat
    /// shop while immersive mode is enabled. Return only this visitor's card, release its
    /// grant claim, and leave the native controller masked so the next offer can retry.</summary>
    internal static void AbortUnavailable() => AbortFailedOffer("transaction coordinator did not answer");
    internal static void AbortUnpresentableConfirmation() => AbortFailedOffer("native confirmation controls could not be presented");

    private static void AbortFailedOffer(string reason)
    {
        if (!HasParkedOffer) return;
        Reclaim();
        _offerRetryUntil = Time.unscaledTime + 2.5f;
        if (Time.unscaledTime >= _nextOfferFailureWarning)
        {
            _nextOfferFailureWarning = Time.unscaledTime + 30f;
            Core.VRLog.Warn("TownServices", "Merchant " + reason
                + "; returned the offered card and kept the immersive stand active for retry.");
        }
    }

    private static void WithdrawForReplacement()
    {
        Action? callback = _ourConfirmation;
        _ourConfirmation = null; _pending = null; _tradeItem = null;
        _decisionConfirmed = _decisionCancelled = false;
        DetachTradeListener();
        ReleaseOffering(preserveReservation: true);
        UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
        if (callback != null && confirmation != null && confirmation.IsActive
            && ReferenceEquals(confirmation._onConfirmedCallback, callback)) confirmation.OnCancel();
    }

    private static void ReleaseOffering(bool preserveReservation = false)
    {
        _fan?.CancelPreparedMerchantPurchase();
        ItemsPile.ItemChip? chip = _offeredChip; _offeredChip = null;
        TownServiceToken? stock = _offeredStock; _offeredStock = null;
        _offering = null;
        // Occupation follows the actual card, not a native fade or the delayed
        // inventory result after a purchase. An atomic replacement keeps this
        // visitor's reservation; all terminal returns release it on this stack.
        if (!preserveReservation) TownServiceMirror.SetLocalTransactionActive(1, false);
        if (chip != null)
        {
            chip.TownOffering = false; chip.TownOfferingReclaimed = null;
            _fan?.ResumeInspection(chip);
        }
        stock?.ReturnOffering();
    }

    private static bool PendingCurrent() => Active && Session == _pendingSession
        && ReferenceEquals(MapRoomHand.OwnedMerchantCharacter(), _character)
        && TownServiceQuietController.InteractionMode == EGuildmasterMode.Merchant;

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
        bool committed = _decisionConfirmed && _tradeItem != null && _character != null && _fan != null;
        if (committed)
        {
            TownServiceCardFlights.RetainCommitted(_fan!, _offeredChip, _offeredStock,
                _character!, _tradeItem!, _tradeSelling, _tradeBaseline, TradeOwnedBefore,
                _palm != null ? _palm.position : Vector3.zero, StationRoot);
            _offeredChip = null; _offeredStock = null; _offering = null;
            TownServiceMirror.SetLocalTransactionActive(1, false);
        }
        else ReleaseOffering();
        ItemsPile? fan = _fan; _fan = null;
        Transform? seat = _seat; _seat = _zone = null; _zoneGate = null; _caption = null; _feedback = null;
        _pending = null; _tradeItem = null; _decisionConfirmed = _decisionCancelled = false;
        _decisionRetries = 0; DetachTradeListener(); _eligibilityItem = null;
        _offerRetryUntil = 0f;
        Items.Clear(); _character = null;
        try
        {
            UIItemConfirmationBox? confirmation = Singleton<UIItemConfirmationBox>.Instance;
            if (!committed && ownedConfirmation != null && confirmation != null && confirmation.IsActive
                && ReferenceEquals(confirmation._onConfirmedCallback, ownedConfirmation)) confirmation.OnCancel();
        }
        finally
        {
            try { if (!committed && fan != null) TownServiceCardFlights.Retain(fan); }
            finally
            {
                TradeOwnedBefore.Clear();
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
