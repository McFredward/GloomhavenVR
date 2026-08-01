using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The single place where the Cards module talks to game code. Every call below was
/// verified against the REAL GH.Runtime.dll / ScenarioRuleLibrary.dll (v1.1.8307.0)
/// with ilspycmd 8.2 (2026-07-15/16); signatures are quoted at each member.
/// Private game members are reachable because the game references are publicized at
/// build time (BepInEx.AssemblyPublicizer, csproj).
///
/// SPIN-WAIT FINDING (CARDS.md §8 / §13, decided from the decompiled bodies):
/// EVERY select/deselect path funnels into the private
/// <c>CardsHandUI.OnCardSelected/OnCardDeselected</c> pair — <c>SelectCard</c>,
/// <c>UnselectCard</c>, <c>ProxySelectCard</c> and <c>AbilityCardUI.ToggleSelect</c>
/// alike — and those two methods contain the main-thread spin-wait
/// (<c>while (id &gt; ScenarioRuleClient.s_SRLLastProcessedMessageID … Thread.Sleep(10))</c>,
/// CardsHandUI.cs:2016-2020 / 2251-2255, cap 1000 ms). There is NO spin-wait-free
/// select entry point; the proxy APIs do not bypass it. Typical cost is one or two
/// 10 ms sleeps (the SRL worker acks quickly), worst case 1 s only if the SRL thread
/// is wedged. Mitigation: all Select/Unselect calls go through
/// <see cref="CardActionQueue"/> — one queued action per frame from a plain Update
/// pump, so the block hits exactly one frame and never runs inside interaction
/// callbacks or event handlers.
/// In contrast, the half-selection commit
/// <c>FullAbilityCard.OnAbilityClick(ActionType, bool, bool)</c> has NO spin-wait:
/// it calls <c>GameState.PlayerSelectedAbilityCardAction</c> (synchronous state set)
/// and <c>Choreographer.Pass()</c> (enqueue only).
/// </summary>
internal static class CardsGameApi
{
    // ---------------------------------------------------------------- hand lookup --

    /// <summary>
    /// The hand the game currently presents (tab-switchable in multi-merc parties).
    /// Verified: <c>public CardsHandUI CurrentHand =&gt; currentHand;</c> and
    /// <c>public CardsHandUI GetActiveHand()</c> (CardsHandManager.cs:137/595).
    /// </summary>
    internal static CardsHandUI? ActiveHand()
    {
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return null;
        CardsHandUI hand = manager.CurrentHand;
        if (hand == null)
            hand = manager.GetActiveHand();
        return hand != null ? hand : null;
    }

    /// <summary>
    /// The hand the game's ACTION-SELECTION click-gate is bound to. During the
    /// <c>ActionSelection</c> phase, <c>FullAbilityCard.OnAbilityClick</c> SILENTLY rejects
    /// every top/bottom click whose card owner is not <c>Choreographer.CurrentActor</c>
    /// (the guard <c>PhaseManager.PhaseType == ActionSelection &amp;&amp;
    /// Choreographer.s_Choreographer.CurrentActor != playerActor</c>, FullAbilityCard.cs:635)
    /// — so the mod MUST present the hand of the ACTING actor, not whatever
    /// <see cref="ActiveHand"/> (<c>CardsHandManager.CurrentHand</c>) happens to point at.
    /// When the turn passes from one character to the next WITHIN action selection (both
    /// hands keep <c>CardHandMode.ActionSelection</c>, so nothing the mod polls changes),
    /// <c>CardsHandManager.CurrentHand</c> can lag the rule engine — leaving the mod docking
    /// the PREVIOUS character's cards; every laser/poke click then hits the guard above and
    /// the SECOND character's action phase DEADLOCKS. Resolving the hand straight from the
    /// authoritative <c>Choreographer.CurrentActor</c> keeps the docked cards clickable.
    /// Non-null only during ActionSelection with a player actor whose hand exists. Verified:
    /// <c>public CActor CurrentActor =&gt; m_CurrentActor</c> (Choreographer.cs:490);
    /// <c>public CardsHandUI GetHand(CPlayerActor)</c> (CardsHandManager.cs:583).
    /// </summary>
    internal static CardsHandUI? ActionSelectionHand()
    {
        if (PhaseManager.PhaseType != CPhase.PhaseType.ActionSelection)
            return null;
        Choreographer c = Choreographer.s_Choreographer;
        if (c == null || !(c.CurrentActor is CPlayerActor player))
            return null;
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return null;
        CardsHandUI hand = manager.GetHand(player);
        return hand != null ? hand : null;
    }

    /// <summary>
    /// Hand-switch watchdog seam (regression ruling, user 2026-08: "Optionsmenü darf das
    /// Spielgeschehen nie beeinflussen" — the options/pause menu must have ZERO influence on
    /// gameplay). Returns the locally-controlled player the initiative track currently has
    /// SELECTED during the card-selection phase whose hand SHOULD therefore be the presented
    /// one but is NOT (<c>CardsHandManager.CurrentHand</c> still points at another character)
    /// — i.e. the game's own edge-triggered portrait-click coupling
    /// (<c>InitiativeTrackPlayerAvatar.Select</c> → <c>SwitchHand</c>,
    /// InitiativeTrackPlayerAvatar.cs:21-28) was swallowed and presentation diverged from
    /// selection. Every gate of that vanilla seam is replicated here so a caller acting on
    /// this can never perform a switch the game itself would have refused:
    /// <list type="bullet">
    /// <item>phase fence: only <c>SelectAbilityCardsOrLongRest</c> — switching between your
    /// characters is legitimate there; the action-phase select guard owns the other phases
    /// (<see cref="IsActionPhaseNonCurrentPlayerSelect"/>);</item>
    /// <item><c>Choreographer.LastMessage</c> gate (StartTurn / ActionSelectionPhaseStart /
    /// CheckForInitiativeAdjustments): on these messages the game deliberately re-selects an
    /// actor WITHOUT switching the hand (e.g. the initiative-adjustment select on every
    /// client, Choreographer.cs:11681) — a switch here would fight the game;</item>
    /// <item>control-ability gate (<c>AbilityEffectManager.IsControlAbilityAffectingActor</c>,
    /// same as the avatar seam);</item>
    /// <item>MP fence: the selected character must be under local control and alive, and its
    /// hand must exist (<c>GetHand</c>).</item>
    /// </list>
    /// Null in the steady state (presentation follows selection) or while any gate holds.
    /// Verified: <c>public CMessageData LastMessage { get; set; }</c> (Choreographer.cs:602),
    /// <c>CMessageData.MessageType</c> members StartTurn/ActionSelectionPhaseStart/
    /// CheckForInitiativeAdjustments (CMessageData.cs:13/188/176),
    /// <c>public bool IsControlAbilityAffectingActor(CActor)</c> (AbilityEffectManager.cs:56),
    /// <c>public void SwitchHand(CPlayerActor)</c> (CardsHandManager.cs:613).
    /// </summary>
    internal static CPlayerActor? SelectionHandDrift()
    {
        if (PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return null;
        if (!(SelectedActor() is CPlayerActor selected) || selected.IsDead)
            return null;
        if (FFSNetwork.IsOnline && !selected.IsUnderMyControl)
            return null;
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return null;
        CardsHandUI current = manager.CurrentHand;
        if (current != null && ReferenceEquals(current.PlayerActor, selected))
            return null; // presentation already follows the selection — the steady state
        if (manager.GetHand(selected) == null)
            return null;
        Choreographer c = Choreographer.s_Choreographer;
        CMessageData? last = c != null ? c.LastMessage : null;
        if (last != null && (last.m_Type == CMessageData.MessageType.StartTurn
                             || last.m_Type == CMessageData.MessageType.ActionSelectionPhaseStart
                             || last.m_Type == CMessageData.MessageType.CheckForInitiativeAdjustments))
            return null; // vanilla suppresses the hand switch on these messages — so do we
        if (Singleton<AbilityEffectManager>.IsInitialized
            && Singleton<AbilityEffectManager>.Instance.IsControlAbilityAffectingActor(selected))
            return null;
        return selected;
    }

    /// <summary>
    /// Drive the game's own hand switch to <paramref name="player"/> — verbatim the call the
    /// 2D portrait-click seam makes (<c>CardsHandManager.SwitchHand</c>, CardsHandManager.cs:613:
    /// re-points <c>currentHand</c>, shows that hand, <c>Choreographer.OnSwitchHand</c>
    /// bookkeeping, then <c>ShowHands()</c> — whose postfix raises <c>HandShown</c> so the VR
    /// driver rebuilds). Local presentation only: no rules state, no network payload — the same
    /// local seam every 2D portrait click uses. Returns true when the switch stuck.
    /// </summary>
    internal static bool SwitchHandTo(CPlayerActor player)
    {
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null || player == null)
            return false;
        manager.SwitchHand(player);
        CardsHandUI now = manager.CurrentHand;
        return now != null && ReferenceEquals(now.PlayerActor, player);
    }

    /// <summary>
    /// Task #5: is it genuinely THIS hand's player's OWN action turn right now — the only
    /// state in which the two played (round) ability cards should be docked on the control
    /// board? During the <c>ActionSelection</c> phase every actor takes its turn in
    /// initiative order, but <c>CardsHandUI.currentMode</c> STAYS
    /// <c>CardHandMode.ActionSelection</c> after the player's own turn ends — it is only
    /// re-driven by the next <c>CardsHandManager.Show(...)</c>, which does NOT run during an
    /// enemy turn (the same stale-mode trap the CardsSelection lock guards, see
    /// <see cref="IsSelectionPhase"/>). So the raw mode keeps the played cards docked all
    /// through the enemy turn (the reported bug: cards still on the board while an ENEMY is
    /// up). This returns true ONLY while the acting actor (<c>Choreographer.CurrentActor</c>,
    /// verified :490) IS this hand's player actor — so the board clears the instant an enemy
    /// (or any other actor) becomes current, and re-docks when the character's own turn comes
    /// round again. The two-character sequential case resolves naturally: each character's own
    /// turn makes <c>CurrentActor</c> its own player, so its own round cards dock — never the
    /// other's. MP-guarded to locally controlled actors (never hijack a remote turn).
    /// </summary>
    internal static bool IsActionTurn(CardsHandUI hand)
    {
        if (hand == null || hand.PlayerActor == null)
            return false;
        Choreographer c = Choreographer.s_Choreographer;
        if (c == null || !(c.CurrentActor is CPlayerActor cur))
            return false;
        if (!ReferenceEquals(cur, hand.PlayerActor))
            return false;
        return !FFSNetwork.IsOnline || cur.IsUnderMyControl;
    }

    /// <summary>
    /// True when we may drive this hand. Verified: <c>public static bool
    /// FFSNetwork.IsOnline</c>; <c>CPlayerActor.IsUnderMyControl</c> (used the same
    /// way throughout CardsHandUI, e.g. RefreshValidCards, CardsHandUI.cs:1635).
    /// </summary>
    internal static bool IsLocalHand(CardsHandUI hand)
    {
        if (hand == null || hand.PlayerActor == null)
            return false;
        return !FFSNetwork.IsOnline || hand.PlayerActor.IsUnderMyControl;
    }

    /// <summary>Verified: <c>public CardHandMode currentMode { get; private set; }</c> (CardsHandUI.cs:214).</summary>
    internal static CardHandMode Mode(CardsHandUI hand) => hand.currentMode;

    /// <summary>
    /// Is the hand's <c>CardsSelection</c> mode genuinely INTERACTIVE right now? The game
    /// only treats a CardsSelection hand as pickable while the scenario phase is
    /// <c>SelectAbilityCardsOrLongRest</c> (or the player is picking cards for an extra
    /// turn) — every selectable/valid path in <c>CardsHandUI.SetMode</c> is gated on
    /// exactly this (CardsHandUI.cs:1516 and :1564). Crucially,
    /// <c>CardsHandUI.currentMode</c> STAYS <c>CardsSelection</c> after the player confirms
    /// (it is only re-driven by the next <c>CardsHandManager.Show(...)</c>, which does not
    /// run during the enemy turn), so the raw mode is STALE and must never be trusted for
    /// interactivity — the hardware repro sat in a stale CardsSelection fan all through the
    /// enemy turn, cards still reclaimable. Verified:
    /// <c>CPhase.PhaseType.SelectAbilityCardsOrLongRest</c> (CPhase.cs:13),
    /// <c>public PhaseType Type</c> (CPhase.cs:32), and
    /// <c>public CAbilityExtraTurn.EExtraTurnType SelectingCardsForExtraTurnOfType</c>
    /// (CPlayerActor.cs:44, publicized).
    /// </summary>
    internal static bool IsSelectionPhase(CardsHandUI hand)
    {
        if (PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return true;
        return hand.PlayerActor != null
               && hand.PlayerActor.SelectingCardsForExtraTurnOfType != CAbilityExtraTurn.EExtraTurnType.None;
    }

    /// <summary>
    /// Fill <paramref name="buffer"/> with the hand's live card widgets.
    /// Verified: <c>private List&lt;AbilityCardUI&gt; cardsUI</c> (CardsHandUI.cs:128,
    /// publicized). No allocation — caller owns the buffer.
    /// </summary>
    internal static void GetCards(CardsHandUI hand, List<AbilityCardUI> buffer)
    {
        buffer.Clear();
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                buffer.Add(cards[i]);
        }
    }

    /// <summary>
    /// How many cards the current modal pick actually expects — the game's
    /// authoritative <c>maxCardsSelected</c> (private <c>CardsHandManager</c> field
    /// CardsHandManager.cs:107, set from the <c>Show(..., maxCardsSelected, ...)</c>
    /// parameter :745; publicized). A single-card burn/avoid-damage pick reports 1, the
    /// two-card burn reports 2. Falls back to 2 when the manager is unavailable or the
    /// field reads 0 (the previous hardcoded cap) so the wanted-slot hint never over- or
    /// under-shoots.
    /// </summary>
    internal static int PickCardsWanted()
    {
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return 2;
        int max = manager.maxCardsSelected;
        return max > 0 ? max : 2;
    }

    /// <summary>
    /// Task #11: is the PICK CONFIRM DialogPopup live for a modal pick flow? The game
    /// opens exactly ONE dialog in the lose/discard pick flows — the
    /// "Karten verbrennen" / "Wähle eine andere Karte" popup shown the moment
    /// <c>selectedCardsUI.Count == maxCardsSelected</c> (<c>OnCardSelected</c> LoseCard
    /// branch, CardsHandUI.cs:2029-2116, <c>UIManager.Instance.dialogPopup.Show(...,
    /// allowHide: false, cancelOption: 1)</c>). While it is open the game marks every
    /// hand widget unselectable (<c>SetSelectable(false)</c>, :2046-2052) — in 2D no
    /// selection change is possible until an option resolves. Verified:
    /// <c>public DialogPopup dialogPopup</c> (UIManager) and
    /// <c>public bool IsOpen()</c> (DialogPopup.cs:426).
    /// </summary>
    internal static bool IsPickConfirmDialogOpen(CardsHandUI hand)
    {
        if (hand == null)
            return false;
        CardHandMode mode = Mode(hand);
        if (mode != CardHandMode.LoseCard && mode != CardHandMode.DiscardCard)
            return false;
        UIManager? ui = UIManager.Instance;
        DialogPopup? popup = ui != null ? ui.dialogPopup : null;
        return popup != null && popup.IsOpen();
    }

    /// <summary>
    /// Task #11 (free swap): press the pick confirm dialog's CANCEL option — the game's
    /// own "Wähle eine andere Karte" ("choose another card") path. Verified:
    /// <c>public void Cancel()</c> (DialogPopup.cs:389) invokes
    /// <c>optionButtons[cancelOption].ExtendedButton.onClick</c>; the LoseCard/
    /// DiscardCard confirm popup is shown with <c>cancelOption: 1</c>
    /// (CardsHandUI.cs:2116), whose callback restores card selectability and runs
    /// <c>DeselectAllCards()</c> + <c>TakeDamagePanel.ToggleVisibility(true)</c>
    /// (CardsHandUI.cs:2099-2115). This is the exact 2D flow of clicking "choose
    /// another card" — LOCAL UI only, the game's own seams handle any network sync.
    /// MUST be queued (<see cref="CardActionQueue"/>): DeselectAllCards funnels into
    /// the spin-wait deselect path.
    /// </summary>
    internal static void CancelPickConfirmDialog()
    {
        UIManager? ui = UIManager.Instance;
        DialogPopup? popup = ui != null ? ui.dialogPopup : null;
        if (popup != null && popup.IsOpen())
            popup.Cancel();
    }

    /// <summary>
    /// EVENT-DISCARD DEADLOCK FIX (pre-scenario "Begegnungen" mali, MP log 2026-07 remote
    /// Player.log:11706ff): press the pick confirm dialog's COMMIT option — the non-cancel
    /// option of the "Karten abwerfen"/"Karten verbrennen" DialogPopup. The popup is shown
    /// by <c>CardsHandUI.OnCardSelected</c> with <c>options[0] = commit, cancelOption: 1</c>
    /// (CardsHandUI.cs:2069-2116); <c>DialogPopup.Show</c> wires each option's
    /// <c>ExtendedButton.onClick</c> to <c>Hide()</c> + the option callback
    /// (DialogPopup.cs:179-186), so invoking that onClick IS the 2D click: UINavigation
    /// restore, selectability restore, then <c>OnLoseCardClick</c> →
    /// <c>Synchronizer.SendGameAction(GameActionType.AbilityDiscardCard/AbilityLoseCard,
    /// …)</c> one per selected card (CardsHandUI.cs:2295-2418) — the GAME's own netcode
    /// carries the outcome; nothing rides the mod's wire. MUST be queued
    /// (<see cref="CardActionQueue"/>): the commit callback runs engine moves.
    /// Fields publicized: <c>optionButtons</c> (DialogPopup.cs:82), <c>cancelOption</c>.
    /// </summary>
    internal static bool ConfirmPickDialog()
    {
        UIManager? ui = UIManager.Instance;
        DialogPopup? popup = ui != null ? ui.dialogPopup : null;
        if (popup == null || !popup.IsOpen())
            return false;
        List<Script.GUI.Popups.InputButton> buttons = popup.optionButtons;
        for (int i = 0; i < buttons.Count; i++)
        {
            if (i == popup.cancelOption)
                continue; // the cancel option is CancelPickConfirmDialog's job
            Script.GUI.Popups.InputButton button = buttons[i];
            if (button == null || !button.gameObject.activeSelf)
                continue;
            ExtendedButton ext = button.ExtendedButton;
            if (ext == null)
                continue;
            ext.onClick.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Live, game-localized label of the pick confirm dialog's commit option
    /// (<paramref name="cancel"/> false — e.g. "Karten abwerfen") or its cancel option
    /// (<paramref name="cancel"/> true — "Wähle eine andere Karte"), read straight off
    /// the option button the 2D player would click (<c>InputButton.ExtendedButton
    /// .buttonText</c>, InputButton.cs / ExtendedButton.cs:16). Null while the dialog
    /// is closed or the option is missing — callers fall back to their own Loc string.
    /// Used for the tray CONFIRM/UNDO keycap labels so the VR affordance carries the
    /// exact wording the game chose for this pick.
    /// </summary>
    internal static string? PickDialogOptionLabel(bool cancel)
    {
        UIManager? ui = UIManager.Instance;
        DialogPopup? popup = ui != null ? ui.dialogPopup : null;
        if (popup == null || !popup.IsOpen())
            return null;
        List<Script.GUI.Popups.InputButton> buttons = popup.optionButtons;
        for (int i = 0; i < buttons.Count; i++)
        {
            if ((i == popup.cancelOption) != cancel)
                continue;
            Script.GUI.Popups.InputButton button = buttons[i];
            if (button == null || !button.gameObject.activeSelf)
                continue;
            TMPro.TextMeshProUGUI? text = button.ExtendedButton != null ? button.ExtendedButton.buttonText : null;
            if (text != null && !string.IsNullOrEmpty(text.text))
                return text.text;
        }
        return null;
    }

    // ------------------------------------------------- item-surrender pick (event mali) --

    /// <summary>
    /// EVENT ITEM-CONSUME/REFRESH pick (pre-scenario "Begegnungen" mali:
    /// <c>ScenarioAbility_ConsumeSmallItem_Self</c> → <c>CAbilityConsumeItemCards</c> →
    /// <c>CSelectRefreshOrConsumeItems_MessageData</c>, Choreographer.cs:5853-5905; also every
    /// mid-scenario refresh/consume item pick): the game's flat UI is
    /// <c>ItemCardRefreshPicker.Show</c> → <c>ItemCardPicker</c>, a UIWindow whose ID is
    /// scene-serialized and NOT in the mod's ModalFallback list — in VR it sat invisible on the
    /// hidden 2D stack while the Choreographer waited in <c>WaitingForItemRefresh</c>: a silent
    /// deadlock, the item twin of the card-discard one. Returns the OPEN picker (null when
    /// closed), plus the affected actor (<c>ItemCardRefreshPicker.actor</c>, publicized) and
    /// whether it is a REFRESH (positive) or CONSUME (malus) pick
    /// (<c>refreshingItems</c>, publicized). The flat game only ever Shows the picker on the
    /// CONTROLLING client (Choreographer.cs:5880 gate), so an open picker is already local;
    /// callers still belt-and-braces on IsUnderMyControl.
    /// Verified: <c>private ItemCardPicker picker</c> (ItemCardRefreshPicker.cs:14),
    /// <c>private UIWindow window</c> (ItemCardPicker.cs:27, set in Awake) — publicized.
    /// </summary>
    internal static ItemCardPicker? OpenItemPicker(out CPlayerActor? actor, out bool refreshing)
    {
        actor = null;
        refreshing = false;
        // Goal-chest forfeit disambiguation (flow 1): ItemRewardLosePicker.Show drives the SAME
        // scene-serialized ItemCardPicker window (ItemRewardLosePicker.cs:15/54), so "window
        // open" alone cannot say whose flow is live — while the Choreographer waits in
        // WaitingForLoseGoalChestRewardSelection the window belongs to the forfeit flow and
        // this accessor must stand down (ItemCardRefreshPicker.actor would be a STALE actor
        // from an earlier refresh, misfiring the surrender pump/banner on the wrong content).
        if (LoseRewardFlowActive())
            return null;
        ItemCardRefreshPicker rp = Singleton<ItemCardRefreshPicker>.Instance;
        ItemCardPicker? picker = rp != null ? rp.picker : null;
        UnityEngine.UI.UIWindow? win = picker != null ? picker.window : null;
        if (picker == null || win == null || !win.IsOpen)
            return null;
        actor = rp!.actor as CPlayerActor;
        refreshing = rp.refreshingItems;
        return picker;
    }

    /// <summary>
    /// The hand of the actor an OPEN item consume/refresh pick demands from — non-null only
    /// while the picker is up for a locally-controlled player. Mirrors
    /// <see cref="ActionSelectionHand"/>: at scenario start no <c>CardsHandManager.Show</c>
    /// has run yet, so <c>CurrentHand</c> may be null/stale — the mod must present THIS
    /// actor's hand (tray, piles, item fan) for the surrender flow to have a surface at all.
    /// </summary>
    internal static CardsHandUI? ItemPickHand()
    {
        ItemCardPicker? picker = OpenItemPicker(out CPlayerActor? actor, out _);
        if (picker == null || actor == null)
            return null;
        if (FFSNetwork.IsOnline && !actor.IsUnderMyControl)
            return null;
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI hand = manager != null ? manager.GetHand(actor) : null!;
        return hand != null ? hand : null;
    }

    /// <summary>Is this inventory item among the picker's filtered candidates? (<c>cardSlots</c>
    /// holds exactly the items <c>ItemCardRefreshPicker.Show</c> filtered in — publicized,
    /// ItemCardPicker.cs:31.)</summary>
    internal static bool IsItemPickCandidate(ItemCardPicker picker, CItem item) =>
        picker != null && item != null && picker.cardSlots.ContainsKey(item);

    /// <summary>How many items the pick demands (<c>itemsToSelect</c>, capped to the candidate
    /// count by Show — publicized, ItemCardPicker.cs:35).</summary>
    internal static int ItemPickWanted(ItemCardPicker picker) => picker.itemsToSelect;

    /// <summary>How many are currently selected (<c>itemsSelected</c>, publicized).</summary>
    internal static int ItemPickSelectedCount(ItemCardPicker picker) => picker.itemsSelected.Count;

    /// <summary>The picker reports the full selection (<c>AreAllItemsSelected</c>, public).</summary>
    internal static bool ItemPickReady(ItemCardPicker picker) => picker.AreAllItemsSelected;

    /// <summary>
    /// SELECT an item through the picker's OWN slot seam — exactly the 2D click:
    /// <c>ItemCardPickerSlot.ToggleSelectSlot</c> (public, ItemCardPickerSlot.cs:81; guards
    /// selectability and blocks toggles once full). When the selection is already FULL, the
    /// oldest selected item is first deselected via the slot's public <c>Deselect</c>
    /// (ItemCardPickerSlot.cs:113 — the same call the picker's own overflow rule makes,
    /// ItemCardPicker.cs:186) so a drop always swaps like the flat overflow would. Pure UI-side
    /// selection state; nothing is committed or networked until <see cref="ConfirmItemPick"/>.
    /// </summary>
    internal static bool ItemPickSelect(ItemCardPicker picker, CItem item)
    {
        if (picker == null || item == null
            || !picker.cardSlots.TryGetValue(item, out ItemCardPickerSlot slot) || slot == null)
            return false;
        if (slot.Selected)
            return true; // already selected — idempotent
        if (picker.AreAllItemsSelected)
        {
            List<CItem> selected = picker.GetCurrentSelectedItems();
            if (selected.Count > 0
                && picker.cardSlots.TryGetValue(selected[0], out ItemCardPickerSlot oldest)
                && oldest != null)
                oldest.Deselect();
        }
        slot.ToggleSelectSlot();
        return slot.Selected;
    }

    /// <summary>DESELECT an item via the slot's public <c>Deselect</c> (grab-back-out seam).</summary>
    internal static bool ItemPickDeselect(ItemCardPicker picker, CItem item)
    {
        if (picker == null || item == null
            || !picker.cardSlots.TryGetValue(item, out ItemCardPickerSlot slot) || slot == null
            || !slot.Selected)
            return false;
        slot.Deselect();
        return true;
    }

    /// <summary>
    /// COMMIT the item pick — the exact tail of the flat confirm button:
    /// <c>ItemCardRefreshPicker.ConfirmSelectedCards</c> (private, publicized;
    /// ItemCardRefreshPicker.cs:47-77) runs <c>Inventory.UseItem</c> (consume) /
    /// <c>ReactivateItem</c> (refresh) per selected item, sends the game's own
    /// <c>Synchronizer.SendGameAction(GameActionType.ConsumeItem/RefreshItem, …,
    /// ItemsToken(networkIDs))</c> online (peers replay via ProxyConsumeItems/
    /// ProxyRefreshItems), hides the picker and releases the Choreographer
    /// (<c>SetChoreographerState(Play)</c> + <c>ScenarioRuleClient.StepComplete</c>).
    /// Guarded on the picker being open with the FULL selection — the same availability
    /// the 2D confirm option enforces (<c>UpdateConfirmAvailable</c>, ItemCardPicker.cs:132).
    /// </summary>
    internal static bool ConfirmItemPick()
    {
        ItemCardRefreshPicker rp = Singleton<ItemCardRefreshPicker>.Instance;
        ItemCardPicker? picker = rp != null ? rp.picker : null;
        UnityEngine.UI.UIWindow? win = picker != null ? picker.window : null;
        if (picker == null || win == null || !win.IsOpen || !picker.AreAllItemsSelected)
            return false;
        rp!.ConfirmSelectedCards();
        return true;
    }

    /// <summary>The picker's own game-localized hint title (e.g. the GUI_CONSUME_ITEMS_TITLE
    /// format with the demanded slot type, ItemCardRefreshPicker.cs:41-44); null/empty when
    /// absent — callers fall back to a Loc.Mod string. Publicized <c>hintTitle</c>.</summary>
    internal static string? ItemPickHintTitle(ItemCardPicker picker) =>
        !string.IsNullOrEmpty(picker.hintTitle) ? picker.hintTitle : null;

    /// <summary>The picker's own game-localized hint MESSAGE — the body text the flat HelpBox
    /// shows while the selection is incomplete (e.g. GUI_CHOOSE_ITEM_TO_LOSE, or the MP
    /// wait-for-host tip on a non-deciding client). Publicized <c>hintMessage</c>
    /// (ItemCardPicker.cs:49, stored by Show).</summary>
    internal static string? ItemPickHintMessage(ItemCardPicker picker) =>
        !string.IsNullOrEmpty(picker.hintMessage) ? picker.hintMessage : null;

    // ------------------------------------------------- goal-chest "lose 1 item reward" (flow 1) --

    /// <summary>
    /// Is the Choreographer parked in the goal-chest reward-forfeit wait
    /// (<c>WaitingForLoseGoalChestRewardSelection</c>, set at Choreographer.cs:5924 right after
    /// <c>Singleton&lt;ItemRewardLosePicker&gt;.Instance.Show()</c>)? THE discriminator between the
    /// two owners of the ONE scene-serialized ItemCardPicker window: ItemCardRefreshPicker
    /// (refresh/consume demand, <see cref="OpenItemPicker"/>) and ItemRewardLosePicker (this
    /// flow) both Show the SAME UIWindow, so "window open" alone cannot attribute the pick.
    /// </summary>
    private static bool LoseRewardFlowActive()
    {
        Choreographer c = Choreographer.s_Choreographer;
        return c != null && c.m_WaitState != null
               && c.m_WaitState.m_State
                  == Choreographer.ChoreographerStateType.WaitingForLoseGoalChestRewardSelection;
    }

    /// <summary>
    /// GOAL-CHEST "lose 1 item reward" pick (flow 1 — the KNOWN silent deadlock):
    /// <c>CLoseGoalChestRewardChoice_MessageData</c> → Choreographer.cs:5911-5935 →
    /// <c>ItemRewardLosePicker.Show</c> → the same invisible ItemCardPicker window the
    /// refresh flow uses, with the Choreographer waiting forever in
    /// <c>WaitingForLoseGoalChestRewardSelection</c> and every 2D commit hidden in VR.
    /// Returns the OPEN picker while exactly that flow is live (see
    /// <see cref="LoseRewardFlowActive"/>); <paramref name="canSelect"/> mirrors the game's
    /// own decision gate (<c>ItemRewardLosePicker.CanSelect</c>, public: HOST-only online —
    /// guests watch, <c>ProxyItemRewardLose</c> replays the host's commit on them).
    /// </summary>
    internal static ItemCardPicker? OpenLoseRewardPicker(out bool canSelect)
    {
        canSelect = false;
        if (!LoseRewardFlowActive())
            return null;
        ItemRewardLosePicker rp = Singleton<ItemRewardLosePicker>.Instance;
        ItemCardPicker? picker = rp != null ? rp.picker : null;
        UnityEngine.UI.UIWindow? win = picker != null ? picker.window : null;
        if (picker == null || win == null || !win.IsOpen)
            return null;
        canSelect = rp!.CanSelect;
        return picker;
    }

    /// <summary>
    /// The REWARD items the forfeit pick chooses from — goal-chest reward <c>CItem</c>s
    /// (<c>itemsToChooseFrom</c>, publicized, ItemRewardLosePicker.cs:23; built by Show from
    /// <c>ScenarioManager.CurrentScenarioState.GoalChestRewards</c>), NOT anyone's inventory.
    /// The item fan presents THESE while the flow is live (the fan's inventory source is
    /// wrong here — the actor never owned the forfeited item). Null outside the flow;
    /// read-only, in the picker's own index order (the commit maps selection back by index).
    /// </summary>
    internal static List<CItem>? LoseRewardItems()
    {
        if (!LoseRewardFlowActive())
            return null;
        ItemRewardLosePicker rp = Singleton<ItemRewardLosePicker>.Instance;
        return rp != null ? rp.itemsToChooseFrom : null;
    }

    /// <summary>
    /// A locally-controlled hand to ANCHOR the forfeit pick's surfaces (tray, item fan) —
    /// the flow has NO owning actor (the party forfeits a shared goal-chest reward), so any
    /// local hand serves: the presented ActiveHand when it is local, else the first local
    /// hand. Non-null only while the deciding client (host/offline) has the picker open —
    /// mirrors <see cref="ItemPickHand"/>'s role in CardsDriver.CurrentHand: without this
    /// the demand could arrive during a REMOTE actor's turn and no local surface would
    /// exist to present the pick on (the deadlock would survive the whole feature).
    /// </summary>
    internal static CardsHandUI? LoseRewardPickHand()
    {
        ItemCardPicker? picker = OpenLoseRewardPicker(out bool canSelect);
        if (picker == null || !canSelect)
            return null;
        CardsHandUI? active = ActiveHand();
        if (active != null && IsLocalHand(active))
            return active;
        CardsHandManager manager = CardsHandManager.Instance;
        List<CardsHandUI>? hands = manager != null ? manager.CardHandsUI : null;
        if (hands == null)
            return null;
        for (int i = 0; i < hands.Count; i++)
        {
            CardsHandUI hand = hands[i];
            if (hand != null && IsLocalHand(hand))
                return hand;
        }
        return null;
    }

    /// <summary>
    /// COMMIT the forfeit — the exact tail of the flat confirm option:
    /// <c>ItemRewardLosePicker.ConfirmSelectedRewardItems</c> (private, publicized;
    /// ItemRewardLosePicker.cs:66-86) removes each chosen <c>Reward</c> from every goal-chest
    /// <c>RewardGroup</c>, sends the game's own <c>Synchronizer.SendGameAction(
    /// GameActionType.LoseItemReward, …, IndexToken(indices))</c> online (guests replay via
    /// <c>ProxyItemRewardLose</c>), hides the picker and releases the Choreographer
    /// (<c>SetChoreographerState(Play)</c> + <c>ScenarioRuleClient.StepComplete</c>, :88-95).
    /// Guarded like the flat confirm availability: picker open, FULL selection, and this
    /// client may decide (<c>CanSelect</c>) — zero wire changes, the game networks everything.
    /// </summary>
    internal static bool ConfirmLoseRewardPick()
    {
        ItemCardPicker? picker = OpenLoseRewardPicker(out bool canSelect);
        if (picker == null || !canSelect || !picker.AreAllItemsSelected)
            return false;
        Singleton<ItemRewardLosePicker>.Instance.ConfirmSelectedRewardItems();
        return true;
    }

    // ------------------------------------------------- floating-panel decisions (flows 2-4) --

    /// <summary>
    /// DOOM picker state (flows 2a/2b): is the game's <c>UIAbilityCardPicker</c> panel shown
    /// (doom-slot replace, Choreographer.cs:10932; transfer dooms, :11017 — a plain serialized
    /// <c>window</c> GameObject, NOT a UIWindow, so neither ModalFallback nor the DecisionDock
    /// registry ever sees it: in VR it sat invisible while
    /// <c>GameState.WaitingForMercenarySpecialMechanicSlotChoice</c> early-returned EVERY
    /// ability Perform — the hard SRL gate). The WorldUI DoomPickerSurface floats it pokeable;
    /// this accessor feeds the board banner. <paramref name="selected"/>/<paramref name="wanted"/>
    /// mirror the picker's own <c>selectedOptions.Count</c>/<c>optionsToSelect</c> (publicized).
    /// </summary>
    internal static bool DoomPickerState(out int selected, out int wanted)
    {
        selected = 0;
        wanted = 0;
        UIAbilityCardPicker p = Singleton<UIAbilityCardPicker>.Instance;
        if (p == null || p.window == null || !p.window.activeSelf)
            return false;
        selected = p.selectedOptions != null ? p.selectedOptions.Count : 0;
        wanted = p.optionsToSelect;
        return true;
    }

    /// <summary>
    /// DISTRIBUTE-POINTS panel state (flows 3/4): which <c>UIScenarioDistributePointsManager</c>
    /// popup is shown — the SELECT popup ("which hero burns a card to prevent this damage",
    /// Choreographer.cs:12078, HOST-decided; the game's SRL worker thread SPIN-WAITS in
    /// GameState.cs:1191 until <c>SelectedPlayerToAvoidDamage</c> runs) or the ASSIGN popup
    /// (redistribute damage/health, :11761, caster-controlled). Both popups are plain
    /// <c>window</c> GameObjects invisible in VR; the WorldUI DistributePointsSurface floats
    /// the shown one pokeable. <paramref name="title"/> is the popup's own game-localized
    /// title text (the ask); <paramref name="assign"/> distinguishes the two popups.
    /// All widget gating (host-only select, IsUnderMyControl assign) lives in the game's own
    /// services (<c>CanAddPointsTo</c>/<c>CanRemovePointsFrom</c>) — the mod adds none.
    /// </summary>
    internal static bool DistributePanelState(out string? title, out bool assign)
    {
        title = null;
        assign = false;
        UIScenarioDistributePointsManager m = Singleton<UIScenarioDistributePointsManager>.Instance;
        if (m == null)
            return false;
        UIDistributePointsPopup? popup =
            m.distributePointsAssignPopup != null && m.distributePointsAssignPopup.IsShown
                ? m.distributePointsAssignPopup
            : m.distributePointsSelectPopup != null && m.distributePointsSelectPopup.IsShown
                ? m.distributePointsSelectPopup
            : null;
        if (popup == null)
            return false;
        assign = ReferenceEquals(popup, m.distributePointsAssignPopup);
        title = popup.titleText != null ? popup.titleText.text : null;
        return true;
    }

    /// <summary>
    /// The pile(s) the current modal pick draws its candidates from — the game's own
    /// <c>selectableCardTypes</c> (private, publicized; stored by
    /// <c>CardsHandUI.UpdateView</c>, CardsHandUI.cs:580, from the
    /// <c>CardsHandManager.Show(..., selectableCardType, ...)</c> parameter — Discarded
    /// for the two-card burn, Hand for the one-card burn). Used to keep the ELIGIBLE
    /// fan visible while the confirm popup has flipped every widget unselectable.
    /// </summary>
    internal static bool IsPickEligible(CardsHandUI hand, AbilityCardUI widget)
    {
        if (hand == null || widget == null)
            return false;
        List<CardPileType> piles = hand.selectableCardTypes;
        if (piles == null || piles.Count == 0)
            return false;
        for (int i = 0; i < piles.Count; i++)
        {
            CardPileType pile = piles[i];
            if (pile == CardPileType.Any || pile == widget.CardType)
                return true;
        }
        return false;
    }

    // ---------------------------------------------------- selection (spin-wait path) --

    /// <summary>
    /// Select a card into the round (full pipeline: engine Hand→Round move, initiative
    /// bookkeeping, ready gating, network). MUST be called via
    /// <see cref="CardActionQueue"/> (contains the OnCardSelected spin-wait).
    /// Verified: <c>public void SelectCard(CAbilityCard card)</c> (CardsHandUI.cs:2604,
    /// 74 B IL) → <c>AbilityCardUI.OnClick(bool ignoreHiglight)</c>.
    /// </summary>
    internal static void SelectCard(CardsHandUI hand, CAbilityCard card) => hand.SelectCard(card);

    /// <summary>
    /// Verified: <c>public void UnselectCard(CAbilityCard card)</c> (CardsHandUI.cs:2619).
    /// Spin-wait path (OnCardDeselected) — queue it.
    /// </summary>
    internal static void UnselectCard(CardsHandUI hand, CAbilityCard card) => hand.UnselectCard(card);

    /// <summary>
    /// Toggle the long-rest pseudo-card (CardID −1, spawned into every hand,
    /// CardsHandUI.cs:1798). <c>SelectCard(CAbilityCard)</c> cannot address it (its
    /// AbilityCard is null), so we go through
    /// <c>public AbilityCardUI GetCard(int cardID)</c> (CardsHandUI.cs:2644) +
    /// <c>public void OnClick(bool ignoreHiglight)</c> (AbilityCardUI.cs:329).
    /// The long-rest branch of OnCardSelected has no engine move → no spin-wait,
    /// but it is queued anyway for ordering.
    /// </summary>
    internal static void ToggleLongRest(CardsHandUI hand)
    {
        AbilityCardUI longRest = hand.GetCard(-1);
        if (longRest != null)
            longRest.OnClick(ignoreHiglight: true);
    }

    /// <summary>Verified: <c>public bool IsLongRestSelected()</c> (CardsHandUI.cs:2662).</summary>
    internal static bool IsLongRestSelected(CardsHandUI hand) => hand.IsLongRestSelected();

    /// <summary>
    /// The authoritative long-rest flag: set the moment long rest is committed in
    /// selection (<c>OnCardSelected</c> long-rest branch sets
    /// <c>CharacterClass.LongRest = true</c>, CardsHandUI.cs:1953) and cleared inside
    /// <c>GameState.PlayerLongRested</c> once the burn resolves (GameState.cs:2569).
    /// A <c>CardHandMode.LoseCard</c> while this is true is unambiguously the long-rest
    /// "lose one card" step (vs an avoid-damage / discard pick). Verified:
    /// <c>public bool LongRest</c> (CCharacterClass.cs:156, publicized).
    /// </summary>
    internal static bool IsLongResting(CardsHandUI hand) =>
        hand.PlayerActor != null && hand.PlayerActor.CharacterClass.LongRest;

    /// <summary>
    /// True once a long rest has RESOLVED this round (heal +2 applied, chosen card
    /// burnt) — <c>GameState.PlayerLongRested</c> sets <c>HasLongRested = true</c>
    /// (GameState.cs:2545) and it is cleared on the next round
    /// (CCharacterClass.cs:1183). Verified: <c>public bool HasLongRested</c>
    /// (CCharacterClass.cs:168, publicized).
    /// </summary>
    internal static bool HasLongRested(CardsHandUI hand) =>
        hand.PlayerActor != null && hand.PlayerActor.CharacterClass.HasLongRested;

    /// <summary>
    /// The hand of a long-rester whose OWN TURN is currently running with the rest
    /// still unresolved — the exact state in which the vanilla game waits for TWO 2D
    /// widgets the VR player cannot reach (long-rest stuck bug, log build 0a2767928
    /// line 2340 ff.). Vanilla turn-start for a long-rester
    /// (Choreographer.cs:3952-4005) does NOT open the LoseCard step directly; it
    /// (1) shows the 2D <c>LongRestConfirmationButton</c> (a hand-fan pseudo-card
    ///     toggle) via <c>CardsHandManager.ShowLongRestConfirmation</c> — this is the
    ///     <c>Show(actor, ActionSelection, None, None)</c> that produced the failing
    ///     round's "mode=ActionSelection, halves=0" state, and
    /// (2) parks the ReadyButton INACTIVE (<c>Toggle(active:false,
    ///     EREADYBUTTONCONTINUE, "PERFORM LONG REST")</c> → <c>SetActive(false)</c>,
    ///     ButtonOnBlockingPanel.cs:26) with the LoseCard-opening alternative action
    ///     queued on it (<c>QueueAlternativeAction</c>).
    /// Only toggling (1) re-activates (2) (the confirmation callback re-runs
    /// <c>readyButton.Toggle(confirmed, EREADYBUTTONNA…)</c>), and only clicking (2)
    /// runs the queued action: <c>OnSelectingHealFocus(2)</c> +
    /// <c>CardsHandManager.Show(actor, CardHandMode.LoseCard, Any, Discarded, 1)</c> +
    /// <c>UINavigation.Enter(ScenarioStateTag.LoseCard)</c> — the game's own path to
    /// "discarded cards selectable, pick one to lose"; the heal +2 and item refresh
    /// then resolve natively in <c>GameState.PlayerLongRested</c> when the chosen
    /// card commits. Both widgets sit in the hidden 2D stack, so the VR mod must
    /// drive them (see <see cref="TryAdvanceLongRestTurn"/>).
    /// Null unless ALL of: the current turn actor is a player with
    /// <c>LongRest &amp;&amp; !HasLongRested</c>, the selection phase is over (guards
    /// against a stale <c>CurrentActor</c> from the previous round), and the actor is
    /// locally driveable. Resolves the hand straight off the acting player
    /// (<c>CardsHandManager.GetHand</c>) so a lagging <c>CurrentHand</c> hand-off can
    /// never hide the pending rest.
    /// </summary>
    internal static CardsHandUI? LongRestTurnHand()
    {
        if (!(CurrentTurnActor() is CPlayerActor player))
            return null;
        CCharacterClass cc = player.CharacterClass;
        if (!cc.LongRest || cc.HasLongRested)
            return null;
        if (PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return null;
        if (FFSNetwork.IsOnline && !player.IsUnderMyControl)
            return null;
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return null;
        CardsHandUI hand = manager.GetHand(player);
        return hand != null ? hand : null;
    }

    /// <summary>
    /// Advance the pending long rest ONE game-side step on the long-rester's own turn
    /// (see <see cref="LongRestTurnHand"/> for the vanilla flow being driven). Called
    /// re-armed every tick (throttled) by <c>CardsDriver.PumpLongRestTurn</c> — NOT
    /// edge-detected — so a missed/reverted transition is simply retried:
    /// - ReadyButton parked inactive → run the game's own confirmation-toggle handler
    ///   (<c>LongRestConfirmationButton.Toggle(true)</c> → its <c>onToggled</c>
    ///   callback re-activates the ReadyButton; no spin-wait — the long-rest branch
    ///   has no engine move);
    /// - ReadyButton active + interactable → <see cref="ClickReady"/> →
    ///   <c>OnClickInternal</c> pops the queued alternative action → the game itself
    ///   opens the LoseCard burn step (heal focus + discarded-selectable fan).
    /// HARD GATE on the armed state: the ReadyButton must hold a queued alternative
    /// action in <c>EREADYBUTTONCONTINUE</c> — the exact arm Choreographer's
    /// long-rest turn-start leaves behind — so this can never fire some unrelated
    /// queued action (doors, movement confirms live in other states/turns). The
    /// no-discards edge case resolves through the SAME queued action
    /// (<c>PlayerLongRested(null)</c> directly, no LoseCard step).
    /// Returns true when a step was actually performed (logged by the caller).
    /// </summary>
    internal static bool TryAdvanceLongRestTurn(CardsHandUI hand, out string step)
    {
        step = "";
        if (hand == null || !ReferenceEquals(hand, LongRestTurnHand()))
            return false;
        if (Mode(hand) == CardHandMode.LoseCard)
            return false; // burn step already live — the pick flow owns it from here
        ReadyButton? ready = Ready();
        if (ready == null || ready.ActionsQueue.Count == 0
            || ready.buttonState != ReadyButton.EButtonState.EREADYBUTTONCONTINUE)
            return false;

        if (!CanConfirm())
        {
            // Step 1: the 2D confirmation toggle (unreachable in VR). Toggle(true) is
            // the game's OWN click handler for that pseudo-card (LongRestConfirmation-
            // Button.cs: SetSelected + onToggled + the online replication branch), so
            // the Choreographer callback re-activates the ReadyButton exactly as a 2D
            // click would.
            LongRestConfirmationButton confirm =
                CardsHandManager.Instance != null ? CardsHandManager.Instance.LongRestConfirmationButton : null!;
            if (confirm == null || !confirm.IsVisible || confirm.isSelected)
                return false;
            confirm.Toggle(selected: true);
            step = "long-rest confirmation toggled (game's own LongRestConfirmationButton.Toggle handler) — ReadyButton re-arming";
            return true;
        }

        // Step 2: the armed "PERFORM LONG REST" ReadyButton. ClickReady mirrors the 2D
        // OnClick guard then runs OnClickInternal — the queued alternative action opens
        // the game's own LoseCard burn step (or resolves directly when no discards).
        if (!ClickReady())
            return false;
        step = "'PERFORM LONG REST' ReadyButton clicked (game's queued turn action) — LoseCard burn step opening";
        return true;
    }

    /// <summary>
    /// Is this CAbilityCard currently one of the round (played) cards?
    /// Verified: <c>public List&lt;CAbilityCard&gt; RoundAbilityCards</c> (get-only,
    /// CCharacterClass — PATCH-TARGETS §3.2).
    /// </summary>
    internal static bool IsInRound(CardsHandUI hand, CAbilityCard card) =>
        hand.PlayerActor != null && hand.PlayerActor.CharacterClass.RoundAbilityCards.Contains(card);

    // ------------------------------------------------------------------ initiative --

    /// <summary>
    /// Verified: <c>public CAbilityCard InitiativeAbilityCard</c> (get-only) on
    /// CCharacterClass; null while fewer than 1 card selected or long rest.
    /// </summary>
    internal static CAbilityCard? InitiativeCard(CardsHandUI hand) =>
        hand.PlayerActor?.CharacterClass.InitiativeAbilityCard;

    /// <summary>
    /// Swap which selected card leads initiative — the same entry the 2D initiative
    /// badge uses. Verified: <c>public void SwapInitiative()</c>
    /// (AbilityCardUI.cs:809, 508 B IL): guarded on phase ==
    /// SelectAbilityCardsOrLongRest &amp;&amp; RoundAbilityCards.Count == 2 &amp;&amp;
    /// RoundAbilityCards.Contains(abilityCard); swaps Initiative/SubInitiative,
    /// reverses RoundAbilityCards, updates the track, networks — it acts on the
    /// CharacterClass of the card's own hand, so calling it on either round card of
    /// the local hand is correct. (Fallback path
    /// <c>InitiativeTrackPlayerAvatar.SwapInitiative()</c> has the identical body
    /// minus the per-card guard, InitiativeTrackPlayerAvatar.cs:73.)
    /// </summary>
    internal static bool SwapInitiative(CardsHandUI hand)
    {
        CPlayerActor? actor = hand.PlayerActor;
        if (actor == null || actor.CharacterClass.RoundAbilityCards.Count != 2)
            return false;
        CAbilityCard roundCard = actor.CharacterClass.RoundAbilityCards[0];
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].AbilityCard == roundCard)
            {
                cards[i].SwapInitiative();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Ready-state read (the physical Ready button itself is Phase-3c):
    /// verified extension <c>public static bool IsCardSelectionReady(this CPlayerActor)</c>
    /// (CPlayerActorExtensions.cs:5) — true when 2 cards or long rest are committed.
    /// </summary>
    internal static bool IsSelectionReady(CardsHandUI hand) =>
        hand.PlayerActor != null && hand.PlayerActor.IsCardSelectionReady();

    /// <summary>
    /// Task #4b GATE: true when, in the plain <c>SelectAbilityCardsOrLongRest</c> phase,
    /// the player can NO LONGER assemble the required TWO round cards and must rest
    /// instead — so a fan→slot placement must be refused. Exact rule mirrored from the
    /// game: readiness requires <c>RoundAbilityCards.Count &gt;= 2 || LongRest</c>
    /// (<c>IsCardSelectionReady</c>, CPlayerActorExtensions.cs:5); with
    /// <c>HandAbilityCards.Count + RoundAbilityCards.Count &lt; 2</c> that is
    /// unreachable — the SAME hand+round&lt;2 formula the rule engine uses for the
    /// exhaustion check (CCharacterClass.cs:1766). The 2D UI never physically blocks
    /// clicking the last hand card (<c>AbilityCardUI.CanSelectInScenario</c> only caps
    /// at 2 selected, AbilityCardUI.cs:1227) — it merely leaves READY disabled — but in
    /// VR a slotted card LOOKS committed, and a subsequent short rest docks its
    /// sacrifice display into the same recess (the reported overlap glitch), so the
    /// placement itself is gated. Extra-turn card picks
    /// (<c>SelectingCardsForExtraTurnOfType != None</c>) run their own
    /// <c>maxCardsSelected</c> count through the same mode and are exempt, as is any
    /// non-selection phase.
    /// </summary>
    internal static bool MustRestInsteadOfPlay(CardsHandUI hand)
    {
        CPlayerActor? actor = hand.PlayerActor;
        if (actor == null || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return false;
        if (actor.SelectingCardsForExtraTurnOfType != CAbilityExtraTurn.EExtraTurnType.None)
            return false; // extra-turn picks have their own wanted count — never gate them
        return actor.CharacterClass.HandAbilityCards.Count
             + actor.CharacterClass.RoundAbilityCards.Count < 2;
    }

    /// <summary>Playable-pool size for the refuse log line (hand + already-selected round cards).</summary>
    internal static int PlayableCardCount(CardsHandUI hand) =>
        hand.PlayerActor == null ? 0
            : hand.PlayerActor.CharacterClass.HandAbilityCards.Count
            + hand.PlayerActor.CharacterClass.RoundAbilityCards.Count;

    /// <summary>
    /// Task #4 (slot overlays must match EXACTLY what can be placed): how many MORE
    /// ability cards the current CardsSelection round genuinely wants placed — the
    /// number of slot overlays that may glow. Derived from the same authoritative
    /// state the game's own gates read:
    /// <list type="bullet">
    /// <item>Normal selection: <c>min(2 - RoundAbilityCards.Count,
    /// HandAbilityCards.Count)</c> — the round needs two cards
    /// (<c>IsCardSelectionReady</c>, CPlayerActorExtensions.cs:5) and only hand cards
    /// can still be placed; 0 when <see cref="MustRestInsteadOfPlay"/> (the last-card
    /// gate refuses every placement, so NO slot may advertise one — the hardware
    /// repro: ONE hand card left, placement blocked, yet both overlays glowed).</item>
    /// <item>Extra-turn picks (<c>SelectingCardsForExtraTurnOfType != None</c>): the
    /// game counts <c>selectedCardsUI</c> against <c>maxCardsSelected</c>
    /// (CardsHandUI.cs:1935), so the remainder of exactly that pair.</item>
    /// </list>
    /// Never negative. The driver additionally caps at the physically EMPTY slots.
    /// </summary>
    internal static int SelectionCardsStillWanted(CardsHandUI hand)
    {
        CPlayerActor? actor = hand.PlayerActor;
        if (actor == null)
            return 0;
        if (actor.SelectingCardsForExtraTurnOfType != CAbilityExtraTurn.EExtraTurnType.None)
        {
            int remaining = PickCardsWanted() - hand.selectedCardsUI.Count;
            return remaining > 0 ? remaining : 0;
        }
        if (MustRestInsteadOfPlay(hand))
            return 0;
        CCharacterClass cc = actor.CharacterClass;
        int required = 2 - cc.RoundAbilityCards.Count;
        int placeable = cc.HandAbilityCards.Count;
        int want = required < placeable ? required : placeable;
        return want > 0 ? want : 0;
    }

    /// <summary>
    /// Overlay gate (user reports B/C): is COMMITTING cards into the round IMPOSSIBLE
    /// right now because a game-level blocker is up — so NO yellow slot overlay
    /// ("wanted slot" pulse or snap/telegraph glow) may be shown? The overlays must
    /// only ever appear while a card can actually be placed. Signals, all read from
    /// the GAME's own state (never the mod's WorldUI):
    /// <list type="bullet">
    /// <item>Victory/defeat ("Sieg"/"Niederlage") results window —
    /// <c>Singleton&lt;UIResultsManager&gt;.Instance.IsShown</c>, which is true from the
    /// moment the Choreographer QUEUES the 1.5 s-delayed Show (<c>IsShown =&gt;
    /// myWindow.IsOpen || m_QueuedShow</c>, UIResultsManager.cs:46-56; shown via
    /// <c>Show(1.5f, EndGame, EResult.Win/Lose)</c>, Choreographer.cs:12837/:12909) —
    /// the overlays go dark before the window even fades in. The Guildmaster results
    /// screen is covered by the SAME check: <c>UINewAdventureResultsManager :
    /// UIResultsManager</c> registers into the same singleton (<c>Implementation =&gt;
    /// (UINewAdventureResultsManager)Singleton&lt;UIResultsManager&gt;.Instance</c>,
    /// UINewAdventureResultsManager.cs:20/65).</item>
    /// <item>Rule-engine scenario end — <c>ActionProcessor.CurrentPhase ==
    /// ActionPhaseType.ScenarioEnded</c> (FFSNet/ActionProcessor.cs:54,
    /// FFSNet/ActionPhaseType.cs) — the engine's own terminal phase, belt-and-braces
    /// for any frame gap around the window (e.g. after it closes into rewards).</item>
    /// <item>Narrator/story dialog (scenario-intro storytelling etc.) —
    /// <c>Singleton&lt;StoryController&gt;.Instance.IsVisible</c> (<c>=&gt;
    /// window.IsVisible</c>, StoryController.cs:85) plus the static
    /// <c>StoryController.DisplayDelayInEffect</c> that bridges a CLevelMessage's
    /// pre-display delay (the show coroutine holds the flag through its
    /// WaitForSecondsRealtime before the window opens, StoryController.cs:163-174) so
    /// the overlays cannot flash during a delayed intro message either.</item>
    /// </list>
    /// This is the exact blocker trio (minus the ESC menu) the game itself gates its
    /// scenario base buttons with (BaseButtons.cs:86: <c>UIResultsManager.IsShown ||
    /// StoryController.IsVisible || ESCMenu.IsOpen</c>). The ESC/pause menu is
    /// deliberately NOT included: the mod's explicit requirement keeps cards fully
    /// interactive while the reachable pause/options family is open (see the modal
    /// input-block note in CardsDriver.TickInteractionsAndStatus), so placement — and
    /// therefore its telegraph — stays live there. Multiplayer: every signal is a
    /// LOCAL-client read (results window / story dialog are per-client UI;
    /// <c>ActionProcessor.CurrentPhase</c> is the locally processed shared rule
    /// state), so the gate follows the local player's view, matching the
    /// IsUnderMyControl-style local gating used elsewhere. Read-only and cheap
    /// (singleton field reads); <paramref name="reason"/> is a constant string, only
    /// meaningful when true (callers log edge-triggered, throttled).
    /// </summary>
    internal static bool IsCardCommitBlocked(out string reason)
    {
        UIResultsManager results = Singleton<UIResultsManager>.Instance;
        if (results != null && results.IsShown)
        {
            reason = "scenario results window (Sieg/Niederlage) open or queued (UIResultsManager.IsShown)";
            return true;
        }
        if (FFSNet.ActionProcessor.CurrentPhase == FFSNet.ActionPhaseType.ScenarioEnded)
        {
            reason = "rule engine in terminal phase (ActionProcessor.CurrentPhase == ScenarioEnded)";
            return true;
        }
        StoryController story = Singleton<StoryController>.Instance;
        if (story != null && story.IsVisible)
        {
            reason = "narrator/story dialog visible (StoryController.IsVisible)";
            return true;
        }
        if (StoryController.DisplayDelayInEffect)
        {
            reason = "narrator/story message pending its display delay (StoryController.DisplayDelayInEffect)";
            return true;
        }
        reason = "";
        return false;
    }

    /// <summary>
    /// Current scenario round (0 while no scenario state exists). Verified:
    /// <c>public ScenarioState m_CurrentState</c> (Choreographer.cs:421) with
    /// <c>public int RoundNumber { get; set; }</c> (ScenarioState.cs:68) — the exact
    /// value the game feeds <c>PhaseBannerHandler.ShowStartRound</c>
    /// (Choreographer.cs:3443), i.e. the number the "Runde N" banner shows.
    /// </summary>
    internal static int RoundNumber()
    {
        Choreographer c = Choreographer.s_Choreographer;
        return c != null && c.m_CurrentState != null ? c.m_CurrentState.RoundNumber : 0;
    }

    // ------------------------------------------------------------------------ rests --

    /// <summary>
    /// Trigger the short-rest flow exactly like clicking the 2D widget:
    /// verified <c>public void MouseClick()</c> → <c>Select(!isSelected)</c> →
    /// game-owned yes/no dialog → on confirm <c>CardsHandUI.PerformShortRest</c>
    /// (ShortRest.cs:202/208, CardsHandUI.cs:719). Respects
    /// <c>button.interactable</c> + validity gating (ShortRest.Select early-outs).
    /// Field verified: <c>private ShortRest shortRest</c> (CardsHandUI.cs:140, publicized).
    /// </summary>
    internal static void ToggleShortRest(CardsHandUI hand)
    {
        ShortRest rest = hand.shortRest;
        if (rest != null)
            rest.MouseClick();
    }

    /// <summary>
    /// Short rest possible right now? Mirrors the 2D gating
    /// (<c>CardsHandUI.UpdateShortRest</c>, CardsHandUI.cs:709:
    /// <c>SetInteractable(!shortRested &amp;&amp; DiscardedAbilityCards.Count &gt; 1)</c>)
    /// plus the phase check.
    /// </summary>
    internal static bool CanShortRest(CardsHandUI hand)
    {
        if (hand.PlayerActor == null || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return false;
        ShortRest rest = hand.shortRest;
        return rest != null && rest.IsInteractable;
    }

    /// <summary>Verified: <c>public bool IsShortRestSelected()</c> → shortRest.IsSelected (CardsHandUI.cs:2671).</summary>
    internal static bool IsShortRestSelected(CardsHandUI hand)
    {
        ShortRest rest = hand.shortRest;
        return rest != null && rest.IsSelected;
    }

    // ShortRestWidget() is REMOVED — it returned the game's real short-rest widget RectTransform
    // for docking, and had no callers. Root cause, and it is the same for the removed ReadyWidget
    // and UndoWidget: WorldUI/Surfaces/TrayControlDockSurface docks NOTHING any more (`_controls`
    // is Array.Empty and ShortRestDocked / ContinueDocked / ContinueVisible / UndoDocked are all
    // hardcoded false), so nobody asks a native widget for its RectTransform. The mod draws every
    // one of these controls itself. If docking is ever revived, revive these with it — and keep
    // the alpha check the Ready one carried: ReadyButton.IsVisibility is alpha > 0, and docking an
    // alpha-0 button installs an INVISIBLE click-catcher on the board.

    /// <summary>
    /// The active hand's short-rest CONFIRMATION dialog (test #24 item 5:
    /// <see cref="Surfaces.DecisionDockSurface"/> docks its Yes/No row on the control
    /// board so the "Bist du sicher?" prompt is pressable in VR instead of appearing
    /// mislocated next to the 2D button and deadlocking). It is a
    /// <c>YesNoDialog</c> (RequireComponent(UIWindow), serialized <c>yesButton</c>/
    /// <c>noButton</c> ExtendedButtons) instantiated lazily by <c>ShortRest.Init</c>
    /// (ShortRest.cs:94-98) as a child of the HUD <c>dialogHolder</c> and reused —
    /// reached via <c>CardsHandUI.shortRest</c> → <c>ShortRest.yesNoDialog</c> (both
    /// publicized). Null until the game built it; not gated on the window being open
    /// (the dock consults the window's own IsOpen). Its Yes/No fire the game's own
    /// callbacks (Select(false) + shortRestAction → ShortRestConfirmed; No cancels),
    /// ShortRest.cs:100-119.
    /// </summary>
    internal static YesNoDialog? ShortRestDialog()
    {
        CardsHandUI? hand = ActiveHand();
        ShortRest? rest = hand != null ? hand.shortRest : null;
        return rest != null ? rest.yesNoDialog : null;
    }

    /// <summary>
    /// The card the game is currently SHORT-RESTING away — the RANDOM discard-pile
    /// sacrifice (never the <c>ImprovedShortRest</c> pick, which routes through
    /// CardHandMode.LoseCard and leaves this null). Non-null from the moment
    /// <c>PerformShortRest</c> picks it (<c>shortRestLostCardID =
    /// ScenarioRNG.Next(DiscardedAbilityCards.Count)</c>, CardsHandUI.cs:752) until
    /// <c>FinalizeShortRest</c> nulls it (CardsHandUI.cs:973); on REDRAW
    /// <c>PerformFinalShortRest</c> re-points it at the alternate card
    /// (<c>shortRestAlternateLostCardID</c>, CardsHandUI.cs:886), so the VALUE CHANGES
    /// mid-choice. Verified: <c>public CAbilityCard ShortRestedCard =&gt;
    /// _shortRestedCard;</c> (CardsHandUI.cs:260, publicized backing field).
    /// </summary>
    internal static CAbilityCard? ShortRestedCard(CardsHandUI hand) =>
        hand != null ? hand.ShortRestedCard : null;

    /// <summary>
    /// The live widget of the currently short-rested card — its 2D face lives in the
    /// docked burn/redraw <c>DialogPopup</c>, and VR re-adopts it to lay the card at
    /// the board centre. Mirrors the game's own private <c>CardsHandUI.GetCardUI</c>
    /// (scan <c>cardsUI</c> for the AbilityCardUI whose AbilityCard is the sacrifice —
    /// <c>cardsUI</c> holds one widget per card of EVERY pile, CardsHandUI.cs:128).
    /// Null until the widget exists / after it is recycled.
    /// </summary>
    internal static AbilityCardUI? ShortRestedCardWidget(CardsHandUI hand)
    {
        CAbilityCard? lost = ShortRestedCard(hand);
        if (lost == null)
            return null;
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].AbilityCard == lost)
                return cards[i];
        }
        return null;
    }

    /// <summary>
    /// Is a short rest mid-choice — the random sacrifice picked and its burn/redraw
    /// choice up? True exactly while <see cref="ShortRestedCard"/> is non-null: the
    /// game sets it synchronously right before it shows the DialogPopup
    /// (<c>UIManager.Instance.dialogPopup.Show(GetCardUI(lostCard)…)</c>,
    /// CardsHandUI.cs:826/850) and nulls it synchronously inside
    /// <c>FinalizeShortRest</c> once an option commits — so there is no frame where
    /// the flag is set without the choice being live. The DialogPopup itself is docked
    /// separately (DecisionDockSurface, other worker); this is the signal to lay the
    /// sacrificed card physically at the board centre. DISPLAY-ONLY — the docked
    /// buttons commit burn/redraw.
    /// </summary>
    internal static bool IsShortRestChoosing(CardsHandUI hand) =>
        hand != null && hand.ShortRestedCard != null;

    /// <summary>
    /// Long-rest availability at selection time: the pseudo-card is selectable only
    /// with &gt;1 discarded card (SetMode's <c>longRestAvailable</c> argument,
    /// CardsHandUI.cs:590) — mirrored here for token dimming.
    /// </summary>
    internal static bool CanLongRest(CardsHandUI hand)
    {
        if (hand.PlayerActor == null || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return false;
        return hand.PlayerActor.CharacterClass.DiscardedAbilityCards.Count > 1;
    }

    // -------------------------------------------------------------- half selection --

    /// <summary>
    /// Commit a card half — IDENTICAL to the 2D top/bottom button path: the prefab
    /// UnityEvent shim <c>OnAbilityClick(bool isTopAbility)</c> (16 B, never patch)
    /// forwards to exactly this call with <c>isProxyAction: false</c>. Verified:
    /// <c>public void OnAbilityClick(CBaseCard.ActionType abilityType, bool
    /// isProxyAction, bool checkValid = true)</c> (FullAbilityCard.cs:606, 882 B IL).
    /// With <c>checkValid: true</c> it runs all UI-level guards (IsInteractable,
    /// isValid, phase, Round-pile) — we deliberately do NOT use
    /// <c>ProxySelectCardAction</c>, whose <c>checkValid: false</c> skips validity.
    /// No spin-wait inside (see class remarks) — safe to call directly from a poke.
    /// </summary>
    internal static void PlayHalf(FullAbilityCard card, CBaseCard.ActionType actionType) =>
        card.OnAbilityClick(actionType, isProxyAction: false, checkValid: true);

    /// <summary>
    /// The same validity query the UI buttons render from. Verified:
    /// <c>public bool IsInteractable(CBaseCard.ActionType actionType, bool
    /// considerSelection = true)</c> (FullAbilityCard.cs:157) and the private
    /// <c>bool isValid</c> field (FullAbilityCard.cs:85, publicized) that
    /// OnAbilityClick checks.
    /// </summary>
    internal static bool IsHalfPlayable(FullAbilityCard card, CBaseCard.ActionType actionType)
    {
        if (!card.isValid)
            return false;
        return card.IsInteractable(actionType, considerSelection: true);
    }

    /// <summary>
    /// The action-selection phase machine. Verified:
    /// <c>public static CardsActionControlller s_Instance</c>,
    /// <c>public Phase GetPhase()</c>, fields <c>topCard/bottomCard</c> (private,
    /// publicized) — CardsActionControlller.cs:26/202/30-32.
    /// </summary>
    internal static CardsActionControlller.Phase ActionPhase()
    {
        CardsActionControlller controller = CardsActionControlller.s_Instance;
        return controller != null ? controller.GetPhase() : CardsActionControlller.Phase.None;
    }

    /// <summary>The two full cards the phase machine was initialized with (may be null).</summary>
    internal static void GetActionCards(out FullAbilityCard? first, out FullAbilityCard? second)
    {
        first = null;
        second = null;
        CardsActionControlller controller = CardsActionControlller.s_Instance;
        if (controller == null)
            return;
        first = controller.topCard != null ? controller.topCard : null;
        second = controller.bottomCard != null ? controller.bottomCard : null;
    }

    /// <summary>
    /// Change-gate signature of the whole ACTION-SELECTION context (second-character
    /// deadlock fix). Folds the ACTING actor identity (<see cref="CurrentTurnActor"/>), the
    /// phase machine's current card pair (<c>CardsActionControlller.topCard/bottomCard</c>)
    /// and its <c>Phase</c> into one int. The driver polls this so a SAME-mode turn hand-off
    /// (char 1 → char 2, both <c>CardHandMode.ActionSelection</c> — which the raw mode poll
    /// cannot see, since <c>currentMode</c> does not change) forces a rebuild that re-docks
    /// the NEW actor's cards. 0 outside ActionSelection.
    /// </summary>
    internal static int ActionSelectionSignature()
    {
        if (PhaseManager.PhaseType != CPhase.PhaseType.ActionSelection)
            return 0;
        int sig = 17;
        CActor? cur = CurrentTurnActor();
        sig = sig * 31 + (cur != null ? cur.GetHashCode() : 0);
        GetActionCards(out FullAbilityCard? first, out FullAbilityCard? second);
        sig = sig * 31 + (first != null ? first.GetInstanceID() : 0);
        sig = sig * 31 + (second != null ? second.GetInstanceID() : 0);
        sig = sig * 31 + (int)ActionPhase();
        return sig;
    }

    /// <summary>The player that owns a full card. Verified: <c>private CPlayerActor
    /// playerActor</c> (FullAbilityCard.cs:77, publicized) — set in <c>Init</c> (:397).</summary>
    internal static CPlayerActor? CardOwner(FullAbilityCard full) => full.playerActor;

    /// <summary>
    /// One-line diagnostic of the ActionSelection click-gate for a docked card — the exact
    /// inputs <c>FullAbilityCard.OnAbilityClick</c> tests before it will commit a half
    /// (FullAbilityCard.cs:618-644): the card's OWNER vs <c>Choreographer.CurrentActor</c>
    /// (the deadlock's smoking gun — a mismatch means the mod docked the wrong/previous
    /// character's cards), per-half interactability, validity and pile. Built only on a state
    /// change (never per frame). Fields publicized: <c>playerActor</c> (:77), <c>isValid</c>
    /// (:85), <c>cardPile</c> (:68).
    /// </summary>
    internal static string DescribeActionGate(FullAbilityCard full)
    {
        CActor? cur = CurrentTurnActor();
        CPlayerActor? owner = full.playerActor;
        bool match = owner != null && ReferenceEquals(owner, cur);
        return $"card='{full.AbilityCard?.Name ?? "?"}' owner={(owner != null ? ActorLabel(owner) : "null")} " +
               $"currentActor={(cur != null ? ActorLabel(cur) : "null")} owner==current={match} " +
               $"valid={full.isValid} topInteractable={full.IsInteractable(CBaseCard.ActionType.TopAction, considerSelection: false)} " +
               $"bottomInteractable={full.IsInteractable(CBaseCard.ActionType.BottomAction, considerSelection: false)} " +
               $"pile={full.cardPile} phase={PhaseManager.PhaseType} actionPhase={ActionPhase()}";
    }

    // ------------------------------------------------------------- confirm / undo --

    /// <summary>
    /// The game's Ready button — the single confirm entry for the whole scenario loop
    /// (its 16 <c>EButtonState</c> values cover end-selection/end-turn/pass/confirm-
    /// targets/-movement/-item/open-door/recover-card, see
    /// .planning/research/CONTROLBOARD.md §1). Verified:
    /// <c>public ReadyButton readyButton</c> (Choreographer.cs:150).
    /// </summary>
    private static ReadyButton? Ready()
    {
        Choreographer c = Choreographer.s_Choreographer;
        return c != null && c.readyButton != null ? c.readyButton : null;
    }

    /// <summary>Verified: <c>public UndoButton m_UndoButton</c> (Choreographer.cs:187).</summary>
    private static UndoButton? Undo()
    {
        Choreographer c = Choreographer.s_Choreographer;
        return c != null && c.m_UndoButton != null ? c.m_UndoButton : null;
    }

    /// <summary>
    /// Can the Ready/confirm path fire right now? Mirrors EXACTLY the guard at the
    /// top of <c>ReadyButton.OnClick</c> (ReadyButton.cs:190):
    /// <c>ButtonComponent.enabled &amp;&amp; !warningMask.gameObject.activeSelf &amp;&amp;
    /// readyButton.interactable</c>. Deliberately NO visibility gate (test #14): the
    /// game's own OnClick never checks <c>IsVisibility</c>, and while VR hides the 2D
    /// UI stack the ReadyButton's canvasGroup alpha can sit at 0 — the old alpha
    /// check made the tray CONFIRM permanently dead even though the click path was
    /// fully functional.
    /// </summary>
    internal static bool CanConfirm()
    {
        ReadyButton? b = Ready();
        if (b == null || !b.gameObject.activeInHierarchy)
            return false;
        return b.ButtonComponent != null && b.ButtonComponent.enabled
               && (b.warningMask == null || !b.warningMask.gameObject.activeSelf)
               && b.IsInteractable;
    }

    /// <summary>
    /// One-line diagnostic of every CanConfirm gate input — built ONLY on a rejected
    /// press (event-driven, never per-frame). Inputs mirror ReadyButton.OnClick's
    /// guard (ReadyButton.cs:190) plus the game's visibility read
    /// (<c>IsVisibility =&gt; canvasGroup.alpha &gt; 0</c>, ReadyButton.cs:93) and
    /// <c>buttonState</c> (ReadyButton.cs:69, publicized) for context.
    /// </summary>
    internal static string DescribeConfirmGate()
    {
        ReadyButton? b = Ready();
        if (b == null)
            return "readyButton=null (no Choreographer.readyButton)";
        return $"active={b.gameObject.activeInHierarchy} " +
               $"buttonComponent={(b.ButtonComponent != null ? b.ButtonComponent.enabled.ToString() : "null")} " +
               $"warningMask={(b.warningMask != null && b.warningMask.gameObject.activeSelf)} " +
               $"interactable={b.IsInteractable} " +
               $"state={b.buttonState} " +
               $"alpha={(b.canvasGroup != null ? b.canvasGroup.alpha.ToString("F2") : "null")}";
    }

    /// <summary>
    /// Fire the exact click path of the 2D Ready button. We call
    /// <c>OnClickInternal</c> (ReadyButton.cs:245) after replicating OnClick's guard
    /// rather than <c>OnClick</c> (ReadyButton.cs:187) — OnClick branches into the
    /// gamepad long-press handler when <c>InputManager.GamePadInUse</c>, which
    /// expects a held-button release we cannot deliver. OnClickInternal runs the
    /// full dispatch: online <c>Synchronizer.SendGameAction(ConfirmAction…)</c>,
    /// queued alternative actions, then <c>ScenarioRuleClient.StepComplete()</c>
    /// (state ≥ CONTINUE) or <c>Choreographer.Pass()</c> → <c>ScenarioRuleClient.
    /// Pass()</c> (END-SELECTION/END-TURN/PASS) — ReadyButton.cs:317-332.
    /// </summary>
    internal static bool ClickReady()
    {
        if (!CanConfirm())
            return false;
        Ready()!.OnClickInternal();
        return true;
    }

    /// <summary>
    /// Live localized label of the Ready button (phase-correct: "End selection",
    /// "Confirm", …). Verified: <c>private TextMeshProUGUI buttonText</c>
    /// (ReadyButton.cs:41, publicized).
    /// </summary>
    internal static string ConfirmLabel()
    {
        ReadyButton? b = Ready();
        if (b != null && b.buttonText != null && !string.IsNullOrEmpty(b.buttonText.text))
            return b.buttonText.text;
        return Localize("GUI_CONFIRM", "Confirm");
    }

    /// <summary>
    /// Undo availability — mirrors EXACTLY the guard of <c>UndoButton.OnClick</c>
    /// (UndoButton.cs:94: <c>m_UndoButton.interactable</c>). No visibility gate
    /// (test #14, same reasoning as <see cref="CanConfirm"/>: the 2D stack is hidden
    /// in VR, so the canvasGroup alpha is not a functional signal). Fields publicized.
    /// </summary>
    internal static bool CanUndo()
    {
        UndoButton? u = Undo();
        if (u == null || !u.gameObject.activeInHierarchy)
            return false;
        return u.m_UndoButton != null && u.m_UndoButton.interactable;
    }

    /// <summary>Diagnostic counterpart of <see cref="CanUndo"/> — built only on a rejected press.</summary>
    internal static string DescribeUndoGate()
    {
        UndoButton? u = Undo();
        if (u == null)
            return "undoButton=null (no Choreographer.m_UndoButton)";
        return $"active={u.gameObject.activeInHierarchy} " +
               $"interactable={(u.m_UndoButton != null ? u.m_UndoButton.interactable.ToString() : "null")} " +
               $"alpha={(u.canvasGroup != null ? u.canvasGroup.alpha.ToString("F2") : "null")}";
    }

    /// <summary>
    /// Fire the 2D Undo button's own click path. Verified: <c>public void
    /// OnClick(bool networkActionIfOnline = true)</c> (UndoButton.cs:94) — self-
    /// guarded on interactability, routes to <c>ScenarioRuleClient.Undo(...)</c> or
    /// <c>ClearTargets()</c> (UndoButton.cs:108ff) and networks UndoAction online.
    /// </summary>
    internal static bool ClickUndo()
    {
        UndoButton? u = Undo();
        if (u == null || !CanUndo())
            return false;
        u.OnClick();
        return true;
    }

    /// <summary>Verified: <c>private TextMeshProUGUI m_ButtonText</c> (UndoButton.cs:39, publicized).</summary>
    internal static string UndoLabel()
    {
        UndoButton? u = Undo();
        if (u != null && u.m_ButtonText != null && !string.IsNullOrEmpty(u.m_ButtonText.text))
            return u.m_ButtonText.text;
        return Localize("GUI_UNDO", "Undo");
    }

    // ------------------------------------------------ ready state (confirm/revoke) --

    /// <summary>
    /// The multiplayer ready toggle — the game's ONLY revocable confirm (test #19).
    /// During ONLINE card selection the 2D UI shows this Toggle INSTEAD of the
    /// ReadyButton (Choreographer.cs:12564 <c>InitializeReadyToggleForCardSelection</c>
    /// vs the offline ReadyButton branch :12591); clicking it while readied
    /// UN-readies via <c>UnreadyPlayer</c> → <c>Synchronizer.SendGameAction(
    /// GameActionType.UnreadyPlayer…)</c> (UIReadyToggle.cs:740/751-760).
    /// OFFLINE there is NO confirmed-waiting state at all: END SELECTION commits the
    /// whole party at once and the round starts immediately
    /// (<c>ReadyButton.OnClickInternal</c> → <c>Choreographer.Pass</c>) — nothing
    /// exists to revoke, so every "confirmed" read below is false offline by design;
    /// the deliberate-dwell + accident guard (PlayTray, test #19) carry the offline
    /// protection. Verified: <c>public class UIReadyToggle :
    /// Singleton&lt;UIReadyToggle&gt;</c> (UIReadyToggle.cs:19).
    /// </summary>
    private static UIReadyToggle? ReadyToggle()
    {
        UIReadyToggle t = Singleton<UIReadyToggle>.Instance;
        return t != null ? t : null;
    }

    /// <summary>
    /// Can the ready toggle take a click right now (online card selection)?
    /// Verified: <c>public bool IsInteractable =&gt; _interactable</c>
    /// (UIReadyToggle.cs:134); the phase gate mirrors the game's card-selection
    /// toggle init (the toggle instance is reused for rewards/map loadout too —
    /// the tray must only drive it during selection).
    /// </summary>
    internal static bool ReadyToggleAvailable()
    {
        if (!FFSNetwork.IsOnline || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return false;
        UIReadyToggle? t = ReadyToggle();
        return t != null && t.gameObject.activeInHierarchy && t.IsInteractable;
    }

    private static bool _soloHostRescueLogged;

    /// <summary>
    /// SOLO-HOST card-selection rescue (bug #5b). When a host is online with no other
    /// players, the game deactivates the single-player ReadyButton AND forces the MP
    /// ready toggle UNINTERACTABLE — either at card-selection start
    /// (<c>Choreographer</c> OnShow solo branch: <c>flag19 = AllPlayers.Count == 1</c> →
    /// <c>UIReadyToggle.SetInteractable(false)</c>, Choreographer.cs:12569-12572) or when
    /// the user starts hosting mid-selection (<c>OnSwitchedToMultiplayer</c>:
    /// <c>readyButton.Toggle(false)</c> + <c>SetInteractable(false)</c>,
    /// Choreographer.cs:14501-14504). The toggle is then re-enabled ONLY when another
    /// player connects (<c>OnPlayerConnected</c>/<c>OnUserEnter</c> →
    /// <c>InitiativeTrack.CheckRoundAbilityCardsOrLongRestSelected()</c>,
    /// Choreographer.cs:14524/14537) or on a fresh card (de)select event
    /// (<c>CardsHandUI</c> calls the same method, CardsHandUI.cs:1952/2022/2228/2264).
    /// A solo host who finished selecting BEFORE hosting therefore has NO confirm
    /// affordance and the round cannot advance — the reported blocking bug.
    ///
    /// This replicates EXACTLY the re-enable the connect handler runs: it calls the
    /// game's own idempotent <c>CheckRoundAbilityCardsOrLongRestSelected()</c>, which
    /// online sets <c>UIReadyToggle.SetInteractable(IsCardSelectionReady())</c>
    /// (InitiativeTrack.cs:797-799) — the toggle becomes interactable IFF every owned
    /// actor has a valid selection (the game's own gate). No network action is sent
    /// here; once interactable the tray CONFIRM surfaces via
    /// <see cref="ReadyToggleAvailable"/> and a deliberate press routes through the
    /// game's normal <c>UIReadyToggle.ReadyUp</c> path. For a single participant
    /// <c>WaitForStateSyncBeforeProceeding</c> self-completes (no other participant to
    /// await, UIReadyToggle.cs:691) → <c>ReadyProceed</c> → <c>Proceed</c>.
    ///
    /// Strictly scoped: no-op unless ONLINE + HOST + exactly one player (solo) +
    /// SelectAbilityCardsOrLongRest + the toggle is currently stuck non-interactable.
    /// With ≥2 players it never fires, so normal multiplayer is untouched.
    /// </summary>
    internal static void EnsureSoloHostSelectionCommittable()
    {
        if (!FFSNetwork.IsOnline || !FFSNetwork.IsHost
            || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return;
        if (FFSNet.PlayerRegistry.AllPlayers == null || FFSNet.PlayerRegistry.AllPlayers.Count != 1)
            return;
        UIReadyToggle? t = ReadyToggle();
        if (t == null || !t.gameObject.activeInHierarchy || t.IsInteractable)
            return; // already usable (or absent) — nothing to rescue

        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
            return;
        // The game's own re-enable — the exact call OnPlayerConnected makes. Idempotent
        // and change-gated internally (UIReadyToggle.SetInteractable no-ops when the
        // value is unchanged), so running it each tick while stuck is cheap and only
        // flips the toggle the moment every owned actor's selection is valid.
        track.CheckRoundAbilityCardsOrLongRestSelected();
        if (!_soloHostRescueLogged && t.IsInteractable)
        {
            _soloHostRescueLogged = true;
            VRLog.Info("Cards", "Solo-host rescue (#5b): re-enabled the MP ready toggle for a " +
                                "solo online host (no other players) via the game's own " +
                                "CheckRoundAbilityCardsOrLongRestSelected — tray CONFIRM now commits selection.");
        }
        else if (t.IsInteractable == false)
        {
            // Selection not yet valid for every owned actor; allow the log to fire once
            // it becomes committable.
            _soloHostRescueLogged = false;
        }
    }

    /// <summary>
    /// Has this hand's player CONFIRMED card selection — the exact state the 2D
    /// ready toggle shows? Readiness is per PLAYER in the game's model
    /// (<c>PlayersReady</c> keys by NetworkPlayer, UIReadyToggle.cs:131), so every
    /// hand under my control mirrors MY toggle: <c>public bool ToggledOn =&gt;
    /// _isOn</c> (UIReadyToggle.cs:136) — flipped synchronously by
    /// <c>ReadyUpPlayer/UnreadyPlayer</c> via <c>SetIsOnWithoutNotify</c>
    /// (UIReadyToggle.cs:651/785). Hands not under my control are never
    /// tray-confirmable. Always false offline (see <see cref="ReadyToggle"/>).
    /// </summary>
    internal static bool IsConfirmed(CardsHandUI hand)
    {
        if (!FFSNetwork.IsOnline || hand.PlayerActor == null
            || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
            return false;
        UIReadyToggle? t = ReadyToggle();
        return t != null && t.gameObject.activeInHierarchy
               && hand.PlayerActor.IsUnderMyControl && t.ToggledOn;
    }

    /// <summary>
    /// Ready-up or REVOKE through the game's own toggle path — the exact tail of
    /// the 2D click: <c>InputToggle(isOn)</c> → <c>ReadyUp(isOn)</c>
    /// (UIReadyToggle.cs:398/610). ReadyUp is fully self-guarded (offline no-op,
    /// all-ready count guard, <c>IsReadyUpForbidden</c>/<c>canReadyUp</c> callback
    /// on ready-up, UIReadyToggle.cs:615-632) and routes revokes through
    /// <c>UnreadyPlayer</c> → <c>GameActionType.UnreadyPlayer</c> — never a
    /// synthetic state write. Returns whether the game accepted, resolved from
    /// <c>ToggledOn</c> afterwards (flipped synchronously for the local player).
    /// </summary>
    internal static bool SetReady(bool ready)
    {
        UIReadyToggle? t = ReadyToggle();
        if (t == null || t.ToggledOn == ready)
            return false;
        t.ReadyUp(ready);
        return t.ToggledOn == ready;
    }

    /// <summary>
    /// Resolved ready state for the confirm/revoke logs (test #19). Allocates —
    /// log path only, never per frame.
    /// </summary>
    internal static string DescribeReadyState()
    {
        if (FFSNetwork.IsOnline)
        {
            UIReadyToggle? t = ReadyToggle();
            return t != null
                ? $"online: toggledOn={t.ToggledOn}, playersReady={t.PlayersReady.Count}, " +
                  $"interactable={t.IsInteractable}"
                : "online: no ready toggle";
        }
        ReadyButton? b = Ready();
        return b != null
            ? $"offline: readyButton={b.buttonState}, interactable={b.IsInteractable}"
            : "offline: no ready button";
    }

    // --------------------------------------------------------------- localization --

    /// <summary>
    /// Localize a game term with an English fallback for keys that may not exist.
    /// Verified: <c>public static bool GLOOM.LocalizationManager.TryGetTranslation(
    /// string Term, out string Translation, …)</c> (GLOOM/LocalizationManager.cs:47)
    /// wrapping I2 Loc; terms are matched case-insensitively.
    /// </summary>
    internal static string Localize(string key, string fallback)
    {
        try
        {
            if (GLOOM.LocalizationManager.TryGetTranslation(key, out string translation)
                && !string.IsNullOrEmpty(translation))
                return translation;
        }
        catch (System.Exception)
        {
            // I2 source not loaded yet (main menu bootstrap) — fallback below.
        }
        return fallback;
    }

    // ------------------------------------------------------------ initiative order --

    // GetInitiativeOrder(List<CActor>) is REMOVED — no callers. It filled a caller-owned buffer
    // with the round's actors in ACTING order by walking InitiativeTrack.actorsUI BACKWARDS. Keep
    // that direction if it is ever needed again: the game sorts actorsUI ascending on
    // GetOrderPriority() = 100 - Initiative(), so the LIST runs acts-LAST -> acts-FIRST, and the
    // 2D track only looks right because it re-inserts with SetAsFirstSibling(). Reading the list
    // forwards gives reverse turn order and looks plausible.

    /// <summary>
    /// The actor whose turn is running. Verified: <c>public CActor CurrentActor =&gt;
    /// m_CurrentActor</c> (Choreographer.cs:490, field :209).
    /// </summary>
    internal static CActor? CurrentTurnActor()
    {
        Choreographer c = Choreographer.s_Choreographer;
        return c != null ? c.CurrentActor : null;
    }

    /// <summary>
    /// Display label for any actor. Players: <c>public string CharacterName</c>
    /// (CPlayerActor.cs:22). Enemies/others: localized class name via
    /// <c>CClass.LocKey</c> (CClass.cs:244) — the exact pattern the game's stat
    /// panel uses (<c>LocalizationManager.GetTranslation(monsterClass.LocKey)</c>,
    /// ActorStatPanel.cs:650-656). <c>CActor.Class</c> verified (CActor.cs:386).
    /// May allocate — call only on order change, never per frame.
    /// </summary>
    internal static string ActorLabel(CActor actor)
    {
        if (actor is CPlayerActor player && !string.IsNullOrEmpty(player.CharacterName))
            return player.CharacterName;
        CClass klass = actor.Class;
        if (klass != null && !string.IsNullOrEmpty(klass.LocKey))
        {
            string name = Localize(klass.LocKey, string.Empty);
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        return actor.GetPrefabName();
    }

    // ------------------------------------------------------- take-damage selection --

    /// <summary>
    /// Task #6: the character that is the subject of an OPEN take-damage decision — the actor
    /// being attacked, resolved to the <see cref="CPlayerActor"/> the initiative track can
    /// select (and the WristHud shows). <c>TakeDamagePanel.Show(actorBeingAttacked, …)</c>
    /// stores the attacked actor (private <c>actorBeingAttacked</c>, publicized —
    /// TakeDamagePanel.cs:97/219) and opens its UIWindow (<c>public bool IsOpen =&gt;
    /// myWindow.IsOpen</c>, :165); <c>ResetAndHide</c> nulls it (:1058). A summon maps to its
    /// <c>Summoner</c> (the selectable hero, CHeroSummonActor.cs:116); a player maps to itself;
    /// anything else (a damaged enemy) yields null. Null unless the panel is OPEN. MULTIPLAYER:
    /// the remote-player variant runs through <c>ShowOtherPlayer</c>, which HIDES the window
    /// (<c>myWindow.Hide(instant: true)</c>, :1133 → <c>IsOpen</c> false), so a remote client's
    /// decision never resolves here; the caller additionally gates on IsUnderMyControl.
    /// Verified: <c>Singleton&lt;TakeDamagePanel&gt;.Instance</c>.
    /// </summary>
    internal static CPlayerActor? TakeDamageSubject()
    {
        TakeDamagePanel panel = Singleton<TakeDamagePanel>.Instance;
        if (panel == null || !panel.IsOpen)
            return null;
        CActor attacked = panel.actorBeingAttacked;
        if (attacked is CPlayerActor player)
            return player;
        if (attacked is CHeroSummonActor summon)
            return summon.Summoner;
        return null;
    }

    /// <summary>
    /// Task #6 MP guard: does THIS client own the open take-damage decision? Mirrors the
    /// game's own control test <c>TakeDamagePanel.ThisPlayerHasTakeDamageControl</c>
    /// (TakeDamagePanel.cs:133 — true offline, else the attacked/cards actor's
    /// IsUnderMyControl / host-for-enemy). Combined with the caller's own IsUnderMyControl
    /// check so selection is never driven for a remote player's decision. Null-safe.
    /// </summary>
    internal static bool TakeDamageIsLocalDecision()
    {
        TakeDamagePanel panel = Singleton<TakeDamagePanel>.Instance;
        return panel != null && panel.IsOpen && panel.ThisPlayerHasTakeDamageControl;
    }

    /// <summary>
    /// Task #6: the attacked character we MAY drive selection to — <see cref="TakeDamageSubject"/>
    /// gated to the LOCAL player's own controlled actors so a remote player's decision (or a
    /// host-owned enemy damage) never hijacks this client's selection. Null unless an open
    /// take-damage decision is under this client's control for a locally controlled hero.
    /// </summary>
    internal static CPlayerActor? DrivableTakeDamageSubject()
    {
        CPlayerActor? subject = TakeDamageSubject();
        if (subject == null)
            return null;
        if (FFSNetwork.IsOnline && !subject.IsUnderMyControl)
            return null;
        return TakeDamageIsLocalDecision() ? subject : null;
    }

    /// <summary>
    /// The actor the game currently has SELECTED on the initiative track, or null. Verified:
    /// <c>public InitiativeTrackActorBehaviour SelectedActor()</c> (InitiativeTrack.cs:527) →
    /// <c>public CActor Actor</c> (InitiativeTrackActorBehaviour.cs:33).
    /// </summary>
    internal static CActor? SelectedActor()
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
            return null;
        InitiativeTrackActorBehaviour beh = track.SelectedActor();
        return beh != null ? beh.Actor : null;
    }

    /// <summary>
    /// Task #6: drive the game's SELECTED actor to <paramref name="actor"/> through the
    /// initiative track's OWN selection path — the exact state change a 2D click on that
    /// character's initiative avatar makes. We locate the actor's behaviour in the track's own
    /// <c>actorsUI</c> (private, publicized — InitiativeTrack.cs:73) and call
    /// <c>public void Select(InitiativeTrackActorBehaviour)</c> (InitiativeTrack.cs:332):
    /// deselect the old, select the new, world-highlight it and <c>SmartFocus</c> the camera.
    /// We deliberately use this overload rather than <c>Select(CPlayerActor)</c> (:352), whose
    /// extra per-avatar button-interactability gate can reject the drive while a MODAL damage
    /// decision is up. The camera <c>SmartFocus(…, pauseDuringTransition: true)</c> is safe in
    /// VR — the rig owns the head pose, <c>CameraController.LateUpdate</c> is prefix-skipped,
    /// and <c>CameraArrivalGuard</c> completes/unpauses any focal-follow transition every frame
    /// (Board/CameraArrivalGuard.cs). Still honours <c>IsSelectable</c> (false only in
    /// <c>MonsterClassesSelectAbilityCards</c> / an improved-long-rest edge, InitiativeTrack.cs:114)
    /// — returns false then. NOT a rules mutation: pure UI selection, the same seam the game
    /// exposes to a click. Returns true when the selection now reflects <paramref name="actor"/>.
    /// </summary>
    internal static bool SelectActor(CActor actor)
    {
        if (actor == null)
            return false;
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null || !track.IsSelectable)
            return false;
        InitiativeTrackActorBehaviour current = track.SelectedActor();
        if (current != null && ReferenceEquals(current.Actor, actor))
            return true; // already selected — nothing to do (no needless re-focus)
        List<InitiativeTrackActorBehaviour> actors = track.actorsUI;
        for (int i = 0; i < actors.Count; i++)
        {
            InitiativeTrackActorBehaviour beh = actors[i];
            if (beh != null && ReferenceEquals(beh.Actor, actor))
            {
                track.Select(beh);
                InitiativeTrackActorBehaviour now = track.SelectedActor();
                return now != null && ReferenceEquals(now.Actor, actor);
            }
        }
        return false;
    }

    // ------------------------------------------------ action-phase select guard --

    /// <summary>
    /// USER-BUG (action-phase deadlock): during the <c>ActionSelection</c> phase the docked
    /// control-board cards belong to <c>Choreographer.CurrentActor</c>, and the game SILENTLY
    /// refuses every card half whose owner is not that actor
    /// (<c>PhaseType == ActionSelection &amp;&amp; CurrentActor != playerActor</c>,
    /// FullAbilityCard.cs:635). If the player laser-clicks ANOTHER of their characters' initiative
    /// avatars, the human-click seam re-points the SELECTED actor (and the mod re-docks that other
    /// actor's cards), whose halves are then unclickable — so NO action can be chosen and the
    /// action board DEADLOCKS (the exact reported bug).
    ///
    /// Returns true when such a select must be REJECTED like an enemy click. All of:
    /// <list type="bullet">
    /// <item>the clicked actor is a <see cref="CPlayerActor"/> (enemies fall through — the game
    /// plays its own invalid feedback and handles them);</item>
    /// <item>we are in the action phase (<c>ActionSelection</c> or <c>Action</c>) — the ONLY phase
    /// where re-pointing the docked cards deadlocks; during card <c>SelectAbilityCardsOrLongRest</c>
    /// switching between your characters is legitimate and never guarded;</item>
    /// <item>a player's own turn is actually running (<c>CurrentActor</c> is a player), and the
    /// clicked actor is a DIFFERENT player than the acting one (re-selecting the acting actor is
    /// fine);</item>
    /// <item>MP: it is genuinely the local player's own action turn and the clicked character is
    /// one they control — a remote turn / remotely-controlled portrait falls through so the game's
    /// own networked handling is never disturbed.</item>
    /// </list>
    /// Read-only. Deliberately NOT reached by the mod's own programmatic drive
    /// (<see cref="SelectActor"/> / take-damage) — that calls <c>InitiativeTrack.Select</c>
    /// DIRECTLY, whereas this guard only sits on the HUMAN avatar-click seam
    /// (<c>InitiativeTrackPlayerAvatar.OnClick</c>), so last round's attacked-actor select still
    /// works. Verified: <c>public CActor CurrentActor</c> (Choreographer.cs:490),
    /// <c>CPhase.PhaseType.ActionSelection/Action</c> (CPhase.cs:16-17),
    /// <c>CPlayerActor.IsUnderMyControl</c>.
    /// </summary>
    internal static bool IsActionPhaseNonCurrentPlayerSelect(CActor? clicked)
    {
        if (!(clicked is CPlayerActor clickedPlayer))
            return false; // enemies / non-players — the game handles those
        CPhase.PhaseType phase = PhaseManager.PhaseType;
        if (phase != CPhase.PhaseType.ActionSelection && phase != CPhase.PhaseType.Action)
            return false; // only the action phase deadlocks; card selection may switch chars
        Choreographer c = Choreographer.s_Choreographer;
        if (c == null || !(c.CurrentActor is CPlayerActor current))
            return false; // no player is acting — nothing to protect
        if (ReferenceEquals(clickedPlayer, current))
            return false; // re-selecting the acting actor itself is fine
        if (FFSNetwork.IsOnline && (!current.IsUnderMyControl || !clickedPlayer.IsUnderMyControl))
            return false; // never interfere with a remote turn / remote portrait
        return true;
    }

    /// <summary>
    /// Reject a non-current player select during the action phase (see
    /// <see cref="IsActionPhaseNonCurrentPlayerSelect"/>): play the game's OWN invalid-click SFX —
    /// <c>UIInfoTools.InvalidOptionAudioItem</c> (its backing field
    /// <c>generalAudioButtonProfile.nonInteractableMouseDownAudioItem</c>, UIInfoTools.cs:467), the
    /// very sound <c>FullAbilityCard.OnAbilityClick</c> plays when it refuses an illegal card click
    /// (FullAbilityCard.cs:625) and the shared UI "invalid option" cue used across the game's shops,
    /// perks and waypoints — routed through <c>AudioControllerUtils.PlaySound(id, optional: true)</c>,
    /// which itself validates the id via <c>AudioController.IsValidAudioID</c>
    /// (AudioControllerUtils.cs:17). The caller's Harmony prefix then SKIPS the original select, so
    /// the current actor stays selected and the action board stays usable. Returns true (rejected)
    /// for call-site readability.
    /// </summary>
    internal static bool RejectActionPhaseSelect(CActor clicked)
    {
        UIInfoTools tools = UIInfoTools.Instance;
        if (tools != null)
            AudioControllerUtils.PlaySound(tools.InvalidOptionAudioItem, optional: true);
        VRLog.Info("Cards", $"Action-phase select REJECTED: '{ActorLabel(clicked)}' is not the acting actor " +
            $"'{(CurrentTurnActor() is CActor cur ? ActorLabel(cur) : "?")}' — kept current selected, " +
            "played the game's invalid-click SFX (docked-card owner-lock deadlock guard).");
        return true;
    }

    // ------------------------------------------------- MP ownership select guard --

    /// <summary>
    /// MP test item #8a: the ownership guard is armed ONLY in a real multiplayer session —
    /// FFSNet online (<c>FFSNetwork.IsOnline</c>, the same flag every other MP branch in this
    /// codebase keys on) with MORE THAN ONE participant. Participant count is read through
    /// <see cref="NetPlayerActors.LocalStableIndex"/> (reflection-safe: returns total 1 when
    /// offline / netcode absent / reflection incomplete), so single-player, offline scenarios
    /// and a solo-hosted lobby behave byte-identically to an unmodded select — every guarded
    /// seam below bails out here before touching anything.
    /// </summary>
    internal static bool OwnershipGuardActive()
    {
        if (!FFSNetwork.IsOnline)
            return false;
        NetPlayerActors.LocalStableIndex(out int total);
        return total > 1;
    }

    /// <summary>
    /// MP test item #8a: true when a HUMAN select of <paramref name="clicked"/> must be refused
    /// because the character is assigned to ANOTHER player. Predicate: the guard is active
    /// (<see cref="OwnershipGuardActive"/>) and the clicked actor is a <see cref="CPlayerActor"/>
    /// that is NOT under local control — <c>CActor.IsUnderMyControl</c> (CActor.cs:751), the
    /// exact ownership flag <c>CharacterManager.OnControlAssigned/OnControlReleased</c>
    /// (CharacterManager.cs:483/492) maintains from FFSNet control assignment, and the flag the
    /// game itself consults for MP turn control (e.g. Choreographer.cs:1849). Enemies and
    /// non-player actors fall through — the game handles those itself. Read-only: never mutates
    /// ownership or any game state.
    /// </summary>
    internal static bool IsForeignControlledSelect(CActor? clicked)
    {
        return clicked is CPlayerActor player
               && !player.IsUnderMyControl
               && OwnershipGuardActive();
    }

    /// <summary>
    /// Refuse a select of another player's character (see
    /// <see cref="IsForeignControlledSelect"/>): play the game's OWN denied cue —
    /// <c>UIInfoTools.InvalidOptionAudioItem</c> via
    /// <c>AudioControllerUtils.PlaySound(id, optional: true)</c>, the identical hook
    /// <see cref="RejectActionPhaseSelect"/> already uses (the sound
    /// <c>FullAbilityCard.OnAbilityClick</c> plays on an illegal card click,
    /// FullAbilityCard.cs:625). The caller's Harmony prefix then SKIPS the original select, so
    /// the local selection is untouched. Returns true (rejected) for call-site readability.
    /// </summary>
    internal static bool RejectForeignSelect(CActor clicked)
    {
        UIInfoTools tools = UIInfoTools.Instance;
        if (tools != null)
            AudioControllerUtils.PlaySound(tools.InvalidOptionAudioItem, optional: true);
        VRLog.Info("Cards", $"Select REJECTED: '{ActorLabel(clicked)}' is controlled by ANOTHER player " +
            "— kept local selection, played the game's invalid-click SFX (MP ownership guard).");
        return true;
    }

    // ---------------------------------------------------------------- card piles --

    /// <summary>
    /// Discard-pile size of the hand's character. Verified: <c>public
    /// List&lt;CAbilityCard&gt; DiscardedAbilityCards =&gt; m_DiscardedAbilityCards;</c>
    /// (CCharacterClass.cs:93) — the exact list <c>CardsHandUI.UpdateCards</c>
    /// classifies widgets against (CardsHandUI.cs:1326).
    /// </summary>
    internal static int DiscardedCount(CardsHandUI hand) =>
        hand.PlayerActor != null ? hand.PlayerActor.CharacterClass.DiscardedAbilityCards.Count : 0;

    /// <summary>
    /// Burnt-pile size: lost + permanently lost — the same union the 2D hand shows
    /// under its single "burnt" header (<c>CardsHandUI.UpdateCards</c> routes BOTH
    /// <c>LostAbilityCards</c> and <c>PermanentlyLostAbilityCards</c> to
    /// <c>burntHeader.Show()</c>, CardsHandUI.cs:1333/1340). Lists verified:
    /// CCharacterClass.cs:104/95.
    /// </summary>
    internal static int BurntCount(CardsHandUI hand)
    {
        if (hand.PlayerActor == null)
            return 0;
        CCharacterClass klass = hand.PlayerActor.CharacterClass;
        return klass.LostAbilityCards.Count + klass.PermanentlyLostAbilityCards.Count;
    }

    /// <summary>
    /// Fill <paramref name="buffer"/> with the live widgets of one pile, in the
    /// authoritative pile order. Source of truth is the MODEL
    /// (<c>CCharacterClass.Discarded/Lost/PermanentlyLostAbilityCards</c>) — the
    /// exact membership test the 2D hand uses to tag its widgets
    /// (CardsHandUI.UpdateCards, CardsHandUI.cs:1326-1340; CardsHandPreviewUI.cs:282)
    /// — resolved to widgets through <c>cardsUI</c>, which holds one AbilityCardUI
    /// per card of EVERY pile (CardsHandUI.cs:128; the FullCardHandViewer re-parents
    /// these same widgets instead of instantiating, CardsHandUI.cs:345). Read-only:
    /// no game state is touched. No allocation — caller owns the buffer.
    /// </summary>
    internal static void GetPileWidgets(CardsHandUI hand, bool burnt, List<AbilityCardUI> buffer)
    {
        buffer.Clear();
        if (hand.PlayerActor == null)
            return;
        CCharacterClass klass = hand.PlayerActor.CharacterClass;
        if (burnt)
        {
            AppendPileWidgets(hand, klass.LostAbilityCards, buffer);
            AppendPileWidgets(hand, klass.PermanentlyLostAbilityCards, buffer);
        }
        else
        {
            AppendPileWidgets(hand, klass.DiscardedAbilityCards, buffer);
        }
    }

    private static void AppendPileWidgets(CardsHandUI hand, List<CAbilityCard> pile, List<AbilityCardUI> buffer)
    {
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < pile.Count; i++)
        {
            for (int j = 0; j < cards.Count; j++)
            {
                if (cards[j] != null && cards[j].AbilityCard == pile[i])
                {
                    buffer.Add(cards[j]);
                    break;
                }
            }
        }
    }

    // -------------------------------------------------------------- active cards --

    /// <summary>
    /// Fill <paramref name="buffer"/> with the live widgets of the ACTIVE pile — the
    /// character's currently-active ability cards (round-long or persistent). Mirrors
    /// <see cref="GetPileWidgets"/> but sourced from the active pile: the exact
    /// membership test the 2D hand uses to tag its widgets is
    /// <c>AbilityCardUI.CardType == CardPileType.Active</c> (the filter
    /// <c>CardsHandUI.GetActiveAbilityCards</c> uses, CardsHandUI.cs:1285-1288), which
    /// tracks the model list <c>CCharacterClass.ActivatedAbilityCards</c>. Resolved
    /// straight off <c>cardsUI</c> (one AbilityCardUI per card of EVERY pile) so no LINQ
    /// list is allocated. Read-only: no game state is touched. No allocation — caller
    /// owns the buffer.
    /// </summary>
    internal static void GetActivePileWidgets(CardsHandUI hand, List<AbilityCardUI> buffer)
    {
        buffer.Clear();
        if (hand.PlayerActor == null)
            return;
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            AbilityCardUI card = cards[i];
            if (card != null && card.AbilityCard != null && card.CardType == CardPileType.Active)
                buffer.Add(card);
        }
    }

    // ActiveCount(CardsHandUI) is REMOVED — no callers. Careful with a bare-name grep here: it
    // returned 4 hits, 3 of them Net.RemoteBoardContent.ActiveCount, an UNRELATED property that
    // counts infused elements. GetActivePileWidgets below is the live way to read the ACTIVE pile.

    /// <summary>
    /// Which action HALVES of an active ability <paramref name="card"/> are the source
    /// of a live bonus right now (feature 6 highlight). Resolves the caster's active
    /// bonuses — <c>CCharacterClass.FindCasterActiveBonuses(actor)</c>
    /// (CCharacterClass.cs:723, the exact list PersistentAbilitiesUI reads,
    /// PersistentAbilitiesUI.cs:135) — and maps each bonus whose <c>BaseCard</c> is this
    /// card to its half via <c>CAbilityCard.GetAbilityActionType(bonus.Ability)</c>
    /// (CAbilityCard.cs:98 → <c>ActionType.TopAction</c>/<c>BottomAction</c>/<c>NA</c>).
    /// A whole-card / unresolvable (NA) active bonus highlights BOTH halves, and a card
    /// that is active but whose bonuses do not resolve to a half falls back to the whole
    /// card — so an active card is never left un-highlighted. Read-only. Allocates the
    /// bonus list (game-side) — call on the change-gated active rebuild, not per frame.
    /// </summary>
    internal static void GetActiveHalves(CardsHandUI hand, CAbilityCard card, out bool top, out bool bottom)
    {
        top = false;
        bottom = false;
        CPlayerActor? actor = hand.PlayerActor;
        if (actor == null || card == null)
            return;
        List<CActiveBonus> bonuses = actor.CharacterClass.FindCasterActiveBonuses(actor);
        for (int i = 0; i < bonuses.Count; i++)
        {
            CActiveBonus bonus = bonuses[i];
            if (bonus == null || !ReferenceEquals(bonus.BaseCard, card))
                continue;
            CBaseCard.ActionType type = card.GetAbilityActionType(bonus.Ability);
            if (type == CBaseCard.ActionType.TopAction)
                top = true;
            else if (type == CBaseCard.ActionType.BottomAction)
                bottom = true;
            else
            {
                top = true; // whole-card / NA bonus → highlight the whole card
                bottom = true;
            }
        }
        if (!top && !bottom) // active card with no resolvable half → highlight the whole card
        {
            top = true;
            bottom = true;
        }
    }

    // ---------------------------------------------------------------------- misc --

    /// <summary>Verified: <c>public static Choreographer s_Choreographer</c> — alive only inside a scenario.</summary>
    internal static bool InScenario => Choreographer.s_Choreographer != null;

    /// <summary>
    /// Log helper for diagnostics: card display name via
    /// <c>public string CardName</c> (AbilityCardUI.cs:26).
    /// </summary>
    internal static string CardName(AbilityCardUI card) => card.CardName ?? "?";
}
