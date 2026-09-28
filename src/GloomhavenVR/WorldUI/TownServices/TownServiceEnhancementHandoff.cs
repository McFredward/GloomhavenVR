using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net.TownServices;
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
    internal static bool TryPhysicalCardHeight(out float height)
    {
        VRCard? card = _current != null && !_current._disposed ? _current.Card : null;
        height = card != null ? CardsConfig.CardHeight * Mathf.Abs(card.transform.lossyScale.x) : 0f;
        return height > .0001f;
    }
    private static float _approachSearchAt;
    private static float _approachRetryAt;
    private static Transform? _approachPalm;
    private static VRCard? _approachCard;
    private static bool _headInside, _cardInside, _pendingApproach, _magePreferredInside;
    private static bool _abilityFanFocused;
    /// <summary>The local fan hand is deliberately aimed at this resident. This affects
    /// only the player's hand contents; other nearby residents retain their own attention.</summary>
    internal static bool WantsAbilityFan => RefreshAbilityFanFocus();
    /// <summary>The authored offering pose may extend only with a visible native
    /// palm mark or a real parked card. Gaze attention alone has no destination.</summary>
    internal static bool HasVisibleCue => _current != null && !_current._disposed
        && _current._window != null && _current._window.IsOpen
        && (_current._zoneGate.alpha > 0f || _current.Card != null)
        && TownServicePresentation.Active
        && TownServicePresentation.Service == 3;
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
    private readonly TMP_Text _zoneLabel;
    private readonly TownServiceOfferFeedback _feedback;
    private readonly Transform _station;
    private Transform? _palm;
    private float _palmSearchAt;
    private CAbilityCard? _model;
    private bool _disposed, _confirmationSeen, _cueRecorded, _lastCueShown, _labelReady = true;
    private bool _claimPending;
    private float _claimDeadline;
    private bool _stalledCueReported;
    private float _cueHiddenSince = -1f;
    internal bool NativeOfferStalled => !_disposed && _cueHiddenSince >= 0f
        && Time.unscaledTime - _cueHiddenSince >= 3f;
    private float _offeredHeight;
    internal VRCard? Card { get; private set; }
    internal AbilityCardUI? NativeSource { get; private set; }
    internal AbilityCardUI? NativeHighlightedCard => _shop != null ? _shop.cardHolder.Card : null;
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
        _zoneLabel = label;
        _zoneGate = Zone.GetComponent<CanvasGroup>(); _zoneGate.alpha = 0f;
        _feedback = new TownServiceOfferFeedback(_zoneGate, Zone);
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

    /// <summary>The offered card is reclaimable by hand, never by laser. A laser on an
    /// original enhancement-area button belongs to that button; this narrow check also
    /// prevents the same near-hand trigger from grabbing the card behind that button.</summary>
    internal static bool TryNativeAreaCanvas(Canvas canvas, out VRCard? offered)
    {
        offered = null;
        TownServiceEnhancementHandoff? current = _current;
        if (current == null || current.Card == null || current._claimPending || !current.Ready) return false;
        TownServiceRitual? ritual = TownServicePresentation.Ritual;
        if (ritual == null || !ReferenceEquals(ritual.Handoff, current)) return false;
        foreach (TownServiceSurface surface in ritual.Surfaces)
            if (surface.Id == 11 && ReferenceEquals(surface.Panel.HostCanvas, canvas))
            { offered = current.Card; return true; }
        return false;
    }

    internal static bool TryNativeArea(Canvas canvas, GameObject hit, out VRCard? offered)
    {
        offered = null;
        if (hit == null || !TryNativeAreaCanvas(canvas, out VRCard? card)) return false;
        UIEnhancementButtonHighlight? area = hit.GetComponentInParent<UIEnhancementButtonHighlight>();
        if (area == null || !area.isActiveAndEnabled || area.Ability == null) return false;
        offered = card;
        return true;
    }
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
        && !_shop._isConfirmationBoxOpened && _alive() && _input()
        && (Card != null || TownServiceMirror.CanLocalBeginTransaction(3));

    internal static void TickApproach()
    {
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value || !Enabled
            || !TownServicePopulation.Available(3))
        {
            _headInside = _cardInside = _pendingApproach = _magePreferredInside = _abilityFanFocused = false;
            _approachCard = null;
            return;
        }
        if (_approachPalm == null && Time.unscaledTime >= _approachSearchAt)
        {
            _approachSearchAt = Time.unscaledTime + .5f;
            TownServiceStation? station = TownServicePopulation.Acquire(3);
            _approachPalm = station != null ? FindPalm(station.Root) : null;
        }
        Transform? palm = _current?._palm ?? _approachPalm;
        if (palm == null)
        { _headInside = _cardInside = _pendingApproach = _magePreferredInside = _abilityFanFocused = false; return; }
        Camera? head = VRRigDriver.HeadCamera;
        bool abilityFanFocused = RefreshAbilityFanFocus();
        // Leaving is the same native destination exit as its former X, including selection
        // and confirmation cleanup. Returning a card alone left an empty service open forever.
        if (_current != null && _current._window.IsOpen && head != null && !NearVisitor(palm, head.transform.position, 1.8f))
        {
            TownServiceEnhancementHandoff current = _current;
            current.Return();
            ModalFallback.CloseFloatedWindow(current._window);
        }
        if (head == null || !NearVisitor(palm, head.transform.position, 1.8f))
            _headInside = false;
        bool headEntered = head != null && !_headInside && NearVisitor(palm, head.transform.position, 1.4f);
        if (headEntered) _headInside = true;
        VRCard? held = HeldOwnedCard(VRHands.Left) ?? HeldOwnedCard(VRHands.Right);
        if (held != _approachCard) { _approachCard = held; _cardInside = false; }
        if (held == null || !Near(palm, held.transform.position, 1.05f)) _cardInside = false;
        bool cardEntered = held != null && !_cardInside && Near(palm, held.transform.position, .85f);
        if (cardEntered) _cardInside = true;
        // The entry edge may arrive before native cards, the rail, or an unrelated
        // modal confirmation becomes ready. Retain it for a bounded retry, but hand
        // the native destination from another idle town resident only when the
        // visitor focuses the ability fan or brings an owned card to her palm.
        // A head-only overlap must leave the other resident usable.
        if ((headEntered || cardEntered) && !StoryComposite.PointOfNoReturn)
        {
            if (!_pendingApproach && VRLog.WantsDebug)
                VRLog.Debug("WorldUI", "TOWN ENHANCEMENT approach pending: destination="
                    + GuildmasterDestinations.CurrentDestinationMode()
                    + " heldCard=" + cardEntered + " head=" + headEntered + ".");
            _pendingApproach = true;
        }
        EGuildmasterMode destination = GuildmasterDestinations.CurrentDestinationMode();
        // A resident can look at the visitor while another stand is active. Do not
        // replace that stand on a head-only overlap. The fan hand entering this
        // resident's workspace is the local choice; a held card near her palm is
        // an even stronger explicit choice. Neither gate suppresses either NPC.
        bool magePreferred = abilityFanFocused || cardEntered;
        if (magePreferred && !_magePreferredInside)
            _pendingApproach = true;
        _magePreferredInside = magePreferred;
        if (!_headInside && !_cardInside || StoryComposite.PointOfNoReturn) _pendingApproach = false;
        if (destination == EGuildmasterMode.Enchantress)
        {
            // A held card can enter the palm volume after the head opened this native
            // destination. That is not a second visit. Keeping it pending reopened the
            // shop after an explicit close while the visitor was still standing here.
            _pendingApproach = false;
            return;
        }
        if (!_pendingApproach || Core.Events.VRModeStateMachine.CurrentMode == Core.Events.VRMode.ModalUI)
            return;
        // Build 580 hardware: Merchant remained the destination at the enchantress's
        // palm. A deliberate ability-fan focus must select the native enhancement
        // service; passive gaze leaves other nearby resident interactions intact.
        // Preserve non-service destinations (trainer/story/etc.).
        if (destination != EGuildmasterMode.None && destination != EGuildmasterMode.Merchant
            && destination != EGuildmasterMode.Temple) return;
        if (destination != EGuildmasterMode.None && !magePreferred) return;
        UIItemConfirmationBox? tradeConfirmation = Singleton<UIItemConfirmationBox>.Instance;
        // IsActive can remain set after the original window has closed. Only a live
        // native confirmation may defer a new resident visit.
        if (tradeConfirmation != null && tradeConfirmation.IsActive
            && tradeConfirmation.GetComponent<UIWindow>() is UIWindow tradeWindow && tradeWindow.IsOpen) return;
        if (destination == EGuildmasterMode.Merchant && TownServiceMerchantHandoff.WantsOffering) return;
        // A new physical entry gets its first attempt immediately. While waiting for cards
        // or the native rail, sample those more expensive predicates at 10 Hz instead of
        // traversing the map fan and rail every VR frame.
        float now = Time.realtimeSinceStartup;
        if (!headEntered && !cardEntered && now < _approachRetryAt) return;
        _approachRetryAt = now + .1f;
        // The item fan and temple purse both mask the owned ability fan. Relinquish
        // only this player's current hand mode after physical Mage focus wins;
        // other residents and visitors retain their independent presentation.
        // Rebuild is synchronous before the native card census below.
        if (destination == EGuildmasterMode.Merchant && magePreferred)
            MapRoomHand.SetMerchantInspection(false);
        if (destination == EGuildmasterMode.Temple && magePreferred)
            MapRoomHand.SetTempleInspection(false);
        if (!HasOwnedMapCard() || !MapRoomDriver.CanVisitTownService(EGuildmasterMode.Enchantress)) return;
        NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
        NewPartyCharacterUI? selectedSlot = display?.SelectedUISlot;
        if (MapRoomDriver.PressGuildmasterMode(EGuildmasterMode.Enchantress,
            cardEntered ? "owned card offered to enchantress" : "approached enchantress",
            suppressNativeSound: true))
        {
            _pendingApproach = false;
            // Changing native guildmaster destinations can select the first assigned
            // character. Keep the exact native slot the visitor was inspecting.
            if (selectedSlot != null && selectedSlot.State == PartySlotState.Assigned
                && display != null && !ReferenceEquals(display.SelectedUISlot, selectedSlot))
                selectedSlot.OnClick();
        }
    }

    private static bool HasOwnedMapCard()
    {
        var cards = CardsDriver.OffScenarioFanCards;
        if (cards == null) return false;
        for (int i = 0; i < cards.Count; i++)
            if (MapRoomHand.TryOwnedTownCard(cards[i], out _, out _)) return true;
        return false;
    }

    private static bool RefreshAbilityFanFocus()
    {
        if (!MapRoomDriver.Active || !WorldUIConfig.ImmersiveTownServices.Value || !Enabled
            || StoryComposite.PointOfNoReturn || !TownServicePopulation.Available(3))
            return _abilityFanFocused = false;
        TownServiceStation? mage = TownServicePopulation.Acquire(3);
        Camera? head = VRRigDriver.HeadCamera;
        VRHand? fanHand = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        Transform? wrist = fanHand?.Rig.PalmCenter;
        if (mage == null || head == null || MapRoomHand.OwnedMerchantCharacter() == null
            || fanHand == null || !fanHand.HasPose || wrist == null
            || fanHand.Grabber.Held != null && fanHand.Grabber.Held is not VRCard
            || !NearVisitor(mage.Root, head.transform.position, 1.8f)
            || !NearVisitor(mage.Root, wrist.position, 1.05f))
            return _abilityFanFocused = false;
        Vector3 mageDelta = wrist.position - mage.Root.position;
        mageDelta.y = 0f;
        float mageDistance = mageDelta.magnitude;
        float tie = .12f * Mathf.Max(.01f, Mathf.Abs(mage.Root.lossyScale.x));
        for (byte service = 1; service <= 2; service++)
        {
            if (!TownServicePopulation.Available(service)) continue;
            TownServiceStation? other = TownServicePopulation.Acquire(service);
            if (other == null) continue;
            Vector3 otherDelta = wrist.position - other.Root.position;
            otherDelta.y = 0f;
            float otherDistance = otherDelta.magnitude;
            if (_abilityFanFocused ? mageDistance > otherDistance + tie
                : mageDistance + tie >= otherDistance) return _abilityFanFocused = false;
        }
        return _abilityFanFocused = true;
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

    // Resident attention is measured on the floor plane. Measuring the HMD's full height
    // against the palm instead made the NPC extend her arm while the shop remained closed.
    private static bool NearVisitor(Transform frame, Vector3 position, float distance)
    {
        Vector3 local = frame.InverseTransformPoint(position);
        local.y = 0f;
        return local.sqrMagnitude <= distance * distance;
    }

    /// <summary>Resolve the player's one native destination in the narrow Temple/Mage
    /// overlap. This never controls whether either NPC looks at or speaks to visitors.</summary>
    internal static bool PrefersEnchantress(Vector3 visitor, EGuildmasterMode destination)
    {
        if (!TownServicePopulation.Available(2)) return true;
        TownServiceStation? mage = TownServicePopulation.Acquire(3);
        TownServiceStation? temple = TownServicePopulation.Acquire(2);
        if (mage == null) return false;
        if (temple == null || ReferenceEquals(mage, temple)) return true;
        Vector3 fromMage = visitor - mage.Root.position;
        Vector3 fromTemple = visitor - temple.Root.position;
        fromMage.y = fromTemple.y = 0f;
        float mageDistance = fromMage.magnitude;
        float templeDistance = fromTemple.magnitude;
        float tie = .12f * Mathf.Max(.01f, Mathf.Abs(mage.Root.lossyScale.x));
        // Hysteresis applies only when one of these two residents already owns
        // the native destination. Merchant/None has no incumbent in this pair:
        // biasing that tie toward Temple can leave the nearer enchantress with
        // an outstretched hand but no native placement cue at the midpoint.
        return destination == EGuildmasterMode.Enchantress
            ? mageDistance <= templeDistance + tie
            : destination == EGuildmasterMode.Temple
                ? mageDistance + tie < templeDistance
                : mageDistance <= templeDistance;
    }

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
        bool ready = Ready, hasPalm = _palm != null;
        VRCard? leftHeld = VRHands.Left?.Grabber.Held as VRCard;
        VRCard? rightHeld = VRHands.Right?.Grabber.Held as VRCard;
        bool leftEligible = OfferableHeld(leftHeld), rightEligible = OfferableHeld(rightHeld);
        VRHand? holder = leftEligible ? VRHands.Left : rightEligible ? VRHands.Right : null;
        if (leftEligible && rightEligible)
            holder = (leftHeld!.transform.position - _seat.position).sqrMagnitude
                <= (rightHeld!.transform.position - _seat.position).sqrMagnitude ? VRHands.Left : VRHands.Right;
        VRCard? held = holder?.Grabber.Held as VRCard;
        bool nativeOpen = _window != null && _window.IsOpen;
        bool availableCard = hasPalm && nativeOpen
            && !_shop._isConfirmationBoxOpened && (held != null || HasAvailableOwnedCard());
        // When the hand carries a card, the preview must describe THAT card. A different
        // valid card elsewhere in the fan cannot make an invalid held card look droppable.
        bool replacement = Card == null || held != null;
        bool heldEligible = leftHeld == null && rightHeld == null || held != null;
        bool candidate = availableCard && replacement && heldEligible && !_claimPending;
        bool showCue = ready && candidate;
        // The build-575 headset still found an outstretched but blank hand. Its Debug log
        // shows native-open/input-false during workspace relocation, and the previous
        // preview expired after 350 ms even if that relocation or native row loading did
        // not. Keep a neutral noninteractive locator for the entire wait, including when
        // the owned physical card exists before its native row is populated. Never send
        // haptics or accept a drop until the original slot AND input gates are ready.
        bool provisionalCard = leftHeld != null || rightHeld != null
            ? leftHeld != null && ValidOwner(leftHeld) || rightHeld != null && ValidOwner(rightHeld)
            : HasPotentialOwnedCard();
        bool preview = !showCue && hasPalm && nativeOpen && !_shop._isConfirmationBoxOpened
            && (Card == null || leftHeld != null || rightHeld != null) && provisionalCard
            && TownServiceSync.LocalOwnsInteraction(3, TownServicePresentation.Session)
            && Core.Events.VRModeStateMachine.CurrentMode != Core.Events.VRMode.ModalUI;
        Vector3 local = held != null ? _seat.InverseTransformPoint(held.transform.position) : Vector3.zero;
        float distance = held != null ? local.magnitude : float.MaxValue;
        _feedback.Tick(showCue && held != null, holder, distance,
            held != null && TownServiceOfferingPose.Contains(_seat, held.transform.position), .43f);
        _feedback.Paint(showCue, preview);
        if (_labelReady != showCue)
        { _labelReady = showCue; _zoneLabel.text = showCue ? Loc.Mod("town_enchant_card") : string.Empty; }
        if (preview && !showCue)
        {
            if (_cueHiddenSince < 0f) _cueHiddenSince = Time.unscaledTime;
            if (!_stalledCueReported && VRLog.WantsDebug && Time.unscaledTime - _cueHiddenSince >= 1f)
            {
                _stalledCueReported = true;
                VRLog.Debug("WorldUI", "TOWN ENHANCEMENT waiting for native offer: session="
                    + TownServicePresentation.Session + " nativeOpen=" + nativeOpen + " alive=" + _alive()
                    + " input=" + _input() + " nativeSlot=" + availableCard
                    + " owner=" + TownServiceSync.LocalOwnsInteraction(3, TownServicePresentation.Session)
                    + " offered=" + (Card != null) + ".");
            }
        }
        else _cueHiddenSince = -1f;
        if (VRLog.WantsDebug && (!_cueRecorded || _lastCueShown != showCue))
        {
            _cueRecorded = true; _lastCueShown = showCue;
            VRLog.Debug("WorldUI", "TOWN ENHANCEMENT cue " + (showCue ? "shown" : "hidden")
                + ": nativeOpen=" + (_window != null && _window.IsOpen)
                + " input=" + _input() + " palm=" + hasPalm
                + " eligibleOwnedCard=" + availableCard + " replacement=" + replacement
                + " offered=" + (Card != null) + " preview=" + preview + ".");
        }
        if (Card == null)
        {
            // Native character changes can automatically select their first card. Until a real
            // offering arrives, no remote stock proxy may silently choose on the player's behalf.
            if (_shop != null && _shop.selectedCard != null) ClearNativeSelection();
            return;
        }
        if (_claimPending)
        {
            // The card itself starts the resident-specific multiplayer claim. Native
            // selection must wait until that claim settles; otherwise two simultaneous
            // visitors could both invoke the original enhancement callback. Keep the
            // original card parked and reclaimable while waiting, then return it if
            // the claim never becomes ours. No gameplay callback runs on timeout.
            if (!ValidOwner(Card) || !_alive() || _palm == null || _shop == null
                || _window == null || !_window.IsOpen || Time.unscaledTime >= _claimDeadline)
            { Return(); return; }
            if (!TownServiceMirror.LocalTransactionSettled(3)) return;
            UIEnhanceCardSlot? pendingSlot = FindAvailableSlot(_model);
            if (pendingSlot == null) { Return(); return; }
            try { pendingSlot.Select(); }
            catch (Exception e)
            {
                VRLog.Warn("WorldUI", "TOWN ENHANCEMENT: native selection failed after the offering claim: " + e.Message);
                Return(); return;
            }
            if (_shop.selectedCard == null || !SameCard(_shop.selectedCard.AbilityCard, _model))
            { Return(); return; }
            NativeSource = _shop.selectedCard;
            NativeSlot = pendingSlot;
            AbilityCardUI? selected = _shop.cardHolder.Card;
            if (selected != null)
                (selected.GetComponent<TownServiceNativeEnhancementCardMask>()
                    ?? selected.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>()).Mask();
            _claimPending = false;
        }
        if (!ValidOwner(Card) || !_alive() || _palm == null || _shop == null || _window == null || !_window.IsOpen
            || _shop.selectedCard == null || !SameCard(_shop.selectedCard.AbilityCard, _model)
            || VRRigDriver.HeadCamera != null && !NearVisitor(_seat, VRRigDriver.HeadCamera.transform.position, 2.25f))
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

    private bool HasPotentialOwnedCard()
    {
        var cards = CardsDriver.OffScenarioFanCards;
        if (cards == null || !Enabled) return false;
        for (int i = 0; i < cards.Count; i++)
            if (ValidOwner(cards[i])) return true;
        return false;
    }

    private bool OfferableHeld(VRCard? card)
    {
        return card != null && !ReferenceEquals(card, Card) && ValidOwner(card)
            && MapRoomHand.TryOwnedTownCard(card, out _, out CAbilityCard? model)
            && FindAvailableSlot(model) != null;
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
        VRCard? previous = target.Card;
        try
        {
            bool accepted = target.Offer(card);
            if (!accepted && VRLog.WantsDebug && card != null && target._station != null
                && Near(target._station, card.transform.position, .85f))
                VRLog.Debug("WorldUI", "TOWN ENHANCEMENT release refused: " + target.Refusal(card));
            return accepted;
        }
        catch (Exception ex)
        {
            // Let the driver's ordinary inspection-release path reclaim the physical card
            // when the first native callback rejects it. During a replacement, the existing
            // physical offer retains ownership; a failed second selection must not evict it.
            if (previous == null) target.Detach();
            VRLog.Warn("WorldUI", "TOWN ENHANCEMENT: original card selection refused; returning the card: " + ex.Message);
            return false;
        }
    }

    private string Refusal(VRCard card)
    {
        if (!Ready) return "native shop or interaction is not ready";
        if (_palm == null) return "resident palm was not found";
        if (card.IsHeld) return "card still belongs to a hand";
        if (ReferenceEquals(Card, card)) return "card is already offered";
        if (!TownServiceOfferingPose.Contains(_seat, card.transform.position)) return "card missed palm volume";
        if (!ValidOwner(card)) return "card owner differs from native shop character";
        if (!MapRoomHand.TryOwnedTownCard(card, out _, out CAbilityCard? model)) return "card is not in owned map loadout";
        if (FindAvailableSlot(model) == null) return "no enabled native card slot matches the offered card";
        return "native card selection did not accept the physical offering";
    }

    private bool Offer(VRCard card)
    {
        if (!Ready || _palm == null || card == null || card.IsHeld || ReferenceEquals(Card, card)
            || !TownServiceOfferingPose.Contains(_seat, card.transform.position) || !ValidOwner(card)
            || !MapRoomHand.TryOwnedTownCard(card, out _, out var model)) return false;
        UIEnhanceCardSlot? found = FindAvailableSlot(model);
        if (found == null) return false;
        if (_claimPending) return false;
        if (Card == null && !TownServiceMirror.LocalTransactionSettled(3))
            return ParkAwaitingClaim(card, model!, found);
        VRCard? existing = Card;
        UIEnhanceCardSlot? existingSlot = NativeSlot;
        // Original selection changes only the candidate; gold and enhancements still require
        // the original rune confirmation path. Recheck after callbacks may rebuild the pool.
        try { found.Select(); }
        catch
        {
            RestoreSelection(existing, existingSlot);
            throw;
        }
        if (!Ready || !ValidOwner(card) || _shop.selectedCard == null
            || !SameCard(_shop.selectedCard.AbilityCard, model))
        { RestoreSelection(existing, existingSlot); return false; }
        // The native selection creates its pooled duplicate immediately. Suppress
        // that print in the same release callback, before the first rendered frame;
        // the original animated enhancement-area buttons remain active above it.
        AbilityCardUI? highlighted = _shop.cardHolder.Card;
        if (highlighted != null)
            (highlighted.GetComponent<TownServiceNativeEnhancementCardMask>()
                ?? highlighted.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>()).Mask();
        // A second valid card replaces the first one in the same physical palm. The native
        // selection is changed before presentation ownership moves, so a rejected callback
        // cannot steal either card. Once accepted, the displaced original takes the ordinary
        // fan-return flight and remains published through that flight; the replacement is
        // adopted in this same release stack, leaving no empty or duplicate palm frame.
        VRCard? displaced = existing;
        ReturnPresentation? displacedPresentation = displaced != null && _model != null
            ? new ReturnPresentation(displaced, _model.ID, _station) : null;
        if (displaced != null) displaced.Grabbed -= OnGrabbed;
        Reclaimed.Remove(card);
        Card = card; NativeSource = _shop.selectedCard; NativeSlot = found; _model = model;
        CardFan.Current?.Remove(card);
        card.Grabbed += OnGrabbed;
        SeatPhysicalCard(card);
        if (displaced != null && !displaced.IsHeld && displacedPresentation != null)
            BeginReturn(displacedPresentation);
        // The old post-offer reaction used invitation clips ("Give me your card")
        // after this exact card had entered her hand. Inspect lines describe the
        // native choice now visible on the card instead.
        TownServiceVoice.RequestReaction(3, TownVoiceReaction.EnchantressInspect);
        VRLog.Debug("WorldUI", "TOWN ENHANCEMENT: actual owned hand card offered to resident palm.");
        return true;
    }

    private void SeatPhysicalCard(VRCard card)
    {
        // Preserve the physical reading size instead of inheriting the resident's model scale.
        // A 0.9 local scale made the ability card a postage stamp on scaled map residents.
        float handScale = VRHands.Primary?.WorldScale ?? Mathf.Abs(card.transform.lossyScale.x);
        float size = CardsConfig.InspectScale.Value * handScale / Mathf.Max(.0001f, Mathf.Abs(_seat.lossyScale.x));
        _offeredHeight = CardsConfig.CardHeight * CardsConfig.InspectScale.Value * handScale;
        card.SetHome(_seat, Vector3.zero, Quaternion.identity, size);
        card.SetHandPopSuppressed(false);
        card.ResetColliderRegion();
        card.Grabbable = true; card.InspectOnly = true; card.AllowsGateHand = true;
    }

    private bool ParkAwaitingClaim(VRCard card, CAbilityCard model, UIEnhanceCardSlot slot)
    {
        Reclaimed.Remove(card);
        Card = card; NativeSource = slot.AbilityCard; NativeSlot = slot; _model = model;
        _claimPending = true;
        _claimDeadline = Time.unscaledTime + 3f;
        CardFan.Current?.Remove(card);
        card.Grabbed += OnGrabbed;
        SeatPhysicalCard(card);
        VRLog.Debug("WorldUI", "TOWN ENHANCEMENT: physical card parked while resident claim settles.");
        return true;
    }

    private void RestoreSelection(VRCard? existing, UIEnhanceCardSlot? slot)
    {
        if (existing == null || slot == null || !ValidOwner(existing) || slot.Selectable == null
            || !slot.gameObject.activeInHierarchy || !slot.Selectable.IsActive() || !slot.Selectable.IsInteractable()) return;
        try { slot.Select(); }
        catch { /* The physical original remains reclaimable; Tick returns it if native recovery failed. */ }
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
        Card = null; NativeSource = null; NativeSlot = null; _model = null;
        _claimPending = _confirmationSeen = false;
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
