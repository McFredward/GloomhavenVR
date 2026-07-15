using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;

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
    /// Verified: <c>public int Initiative { get; private set; }</c> (CBaseAbilityCard.cs:9).
    /// Long rest initiative is 99 by game rule.
    /// </summary>
    internal static int InitiativeValue(CAbilityCard card) => card.Initiative;

    /// <summary>
    /// Ready-state read (the physical Ready button itself is Phase-3c):
    /// verified extension <c>public static bool IsCardSelectionReady(this CPlayerActor)</c>
    /// (CPlayerActorExtensions.cs:5) — true when 2 cards or long rest are committed.
    /// </summary>
    internal static bool IsSelectionReady(CardsHandUI hand) =>
        hand.PlayerActor != null && hand.PlayerActor.IsCardSelectionReady();

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

    // ---------------------------------------------------------------------- misc --

    /// <summary>Verified: <c>public static Choreographer s_Choreographer</c> — alive only inside a scenario.</summary>
    internal static bool InScenario => Choreographer.s_Choreographer != null;

    /// <summary>
    /// Log helper for diagnostics: card display name via
    /// <c>public string CardName</c> (AbilityCardUI.cs:26).
    /// </summary>
    internal static string CardName(AbilityCardUI card) => card.CardName ?? "?";
}
