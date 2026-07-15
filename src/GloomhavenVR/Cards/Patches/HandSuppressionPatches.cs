using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards.Patches;

/// <summary>
/// 2D-hand suppression (Phase 3b).
///
/// SUPPRESSION STRATEGY — decided from the decompiled bodies (see CardsGameApi header
/// for verification method):
///
/// <c>CardsHandManager.Show</c> is NOT prefix-skippable. Its body mixes visuals with
/// bookkeeping the whole card pipeline depends on:
///   - <c>SwitchHand</c> → <c>Choreographer.OnSwitchHand</c> (turn/actor bookkeeping),
///   - <c>CardsHandUI.UpdateView → SetMode</c> → per-card selectability, validity,
///     the long-rest gate AND <c>CardsActionControlller.Init(top, bottom, …)</c> —
///     the half-selection phase machine is seeded HERE,
///   - push/pop fields (<c>m_PushPopCardHandMode</c>…) that Choreographer reads to
///     restore the hand after interrupts.
/// Skipping Show would leave AbilityCardUI.OnClick guards (<c>isSelectable</c>,
/// <c>currentMode</c>) stale, breaking SelectCard/UnselectCard and the whole VR flow.
///
/// So Show always runs and we suppress the VISUAL only: the hand lives inside one
/// <c>UIWindow</c> (<c>private UIWindow window</c>, CardsHandManager.cs:49,
/// publicized; UIWindow carries a required CanvasGroup it fades on Show/Hide —
/// verified <c>[RequireComponent(typeof(CanvasGroup))]</c>, UIWindow.cs:14).
/// While suppression is active, <see cref="HandSuppression.Tick"/> (called every
/// frame by CardsDriver) forces that CanvasGroup to alpha 0 / blocksRaycasts false —
/// re-asserted per frame because UIWindow's own show-tween writes alpha 1.
/// State flags (window.IsOpen = VisualState) are untouched, so
/// <c>CardsHandManager.IsShown</c> and everything reading it behaves exactly as in 2D.
///
/// EXCEPTION — game confirmation dialogs: the short/long-rest flows re-parent
/// <c>UIManager.Instance.dialogPopup</c> under <c>CardsHandManager.Instance.transform</c>
/// (CardsHandUI.cs:867/2117). If that lands inside the suppressed window subtree the
/// dialog would be invisible; Tick detects an OPEN dialog under the suppressed root
/// and lifts the suppression while it shows (P3b intentionally routes confirmations
/// through the game's own dialogs; P3c makes them world-space).
///
/// The postfixes below only (a) lazily arm suppression when a scenario hand appears
/// and (b) raise <see cref="CardsSignals.HandShown"/> so the VR layer rebuilds.
/// Both target methods verified in the real DLL (PATCH-TARGETS §1.4 + corrections #7):
/// the 3 Show overloads chain: single-pile (:734) forwards to the List overload
/// (:739); the active-hand overload (:813) and ShowCoroutine (:838, iterator) do NOT
/// pass through :739 — but every path ends in the private <c>ShowHands()</c>
/// (:645, non-iterator, called from SwitchHand/Show/ShowCoroutine/Update), which is
/// therefore the single reliable "hand became visible" postfix. The :739 postfix is
/// kept additionally because only it carries the (playerActor, mode) payload.
/// </summary>
[HarmonyPatch(typeof(CardsHandManager), nameof(CardsHandManager.Show),
    typeof(CPlayerActor), typeof(CardHandMode), typeof(CardPileType), typeof(List<CardPileType>),
    typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(CardsHandUI.CardActionsCommand),
    typeof(bool), typeof(bool), typeof(Action<AbilityCardUI>), typeof(Func<CAbilityCard, bool>))]
internal static class CardsHandManager_ShowList_Patch
{
    private static void Postfix(CPlayerActor playerActor, CardHandMode mode)
    {
        HandSuppression.Arm();
        CardsSignals.RaiseHandShown(playerActor, mode);
    }
}

/// <summary>
/// Active-hand overload — verified: <c>public void Show(CardHandMode mode,
/// CardPileType filterType, List&lt;CardPileType&gt; selectableCardTypes, int
/// maxCardsSelected = 0, bool fadeUnselectableCards = false, bool
/// allowFullCardPreview = true, bool allowFullDeckPreview = true)</c>
/// (CardsHandManager.cs:813). Updates ALL hands (round-start selection).
/// </summary>
[HarmonyPatch(typeof(CardsHandManager), nameof(CardsHandManager.Show),
    typeof(CardHandMode), typeof(CardPileType), typeof(List<CardPileType>),
    typeof(int), typeof(bool), typeof(bool), typeof(bool))]
internal static class CardsHandManager_ShowAll_Patch
{
    private static void Postfix(CardHandMode mode)
    {
        HandSuppression.Arm();
        CardsSignals.RaiseHandShown(null, mode);
    }
}

/// <summary>
/// The single visual choke point (covers SwitchHand, ShowCoroutine completion and the
/// Update() restore path). Verified: <c>private void ShowHands()</c>
/// (CardsHandManager.cs:645) — plain method (not an iterator), &gt;20 B IL.
/// </summary>
[HarmonyPatch(typeof(CardsHandManager), "ShowHands")]
internal static class CardsHandManager_ShowHands_Patch
{
    private static void Postfix(CardsHandManager __instance)
    {
        HandSuppression.Arm();
        CardsSignals.RaiseHandShown(__instance.ActivePlayer, __instance.m_PushPopCardHandMode);
    }
}

/// <summary>Per-frame visual suppression state (driven by <see cref="CardsDriver"/>).</summary>
internal static class HandSuppression
{
    private static bool _armed;
    private static bool _lifted;

    /// <summary>Master switch, owned by CardsModule (true while VR/dev cards run).</summary>
    internal static bool Active { get; set; }

    /// <summary>Called from Show postfixes: a scenario hand exists, start enforcing.</summary>
    internal static void Arm() => _armed = true;

    /// <summary>
    /// Enforce alpha-0 on the hand window each frame; lift while a game dialog lives
    /// inside the suppressed subtree. Cheap: two component lookups on cached
    /// references, no allocations.
    /// </summary>
    internal static void Tick()
    {
        if (!Active || !_armed)
            return;

        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
        {
            _armed = false; // scenario gone; window destroyed with it.
            return;
        }

        // Verified fields: private UIWindow window (CardsHandManager.cs:49);
        // UIWindow.m_CanvasGroup (UIWindow.cs:125) — both publicized.
        UnityEngine.UI.UIWindow window = manager.window;
        if (window == null)
            return;
        CanvasGroup group = window.m_CanvasGroup;
        if (group == null)
            group = window.GetComponent<CanvasGroup>();
        if (group == null)
            return;

        // Dialog rescue: PerformShortRest / lose-card flows re-parent the popup under
        // CardsHandManager.transform. If it ends up inside the window subtree, keep
        // the window visible while the dialog is open.
        bool lift = false;
        UIManager uiManager = UIManager.Instance;
        if (uiManager != null && uiManager.dialogPopup != null && uiManager.dialogPopup.IsOpen()
            && uiManager.dialogPopup.transform.IsChildOf(window.transform))
        {
            lift = true;
        }

        if (lift)
        {
            if (!_lifted)
            {
                _lifted = true;
                group.alpha = 1f;
                group.blocksRaycasts = true;
                VRLog.Debug("Cards", "Hand suppression lifted (game dialog inside hand window).");
            }
            return;
        }

        if (_lifted)
        {
            _lifted = false;
            VRLog.Debug("Cards", "Hand suppression re-applied (dialog closed).");
        }

        if (group.alpha != 0f)
            group.alpha = 0f;
        if (group.blocksRaycasts)
            group.blocksRaycasts = false;
    }

    /// <summary>Undo (module shutdown / hot reload): give the window back to the game.</summary>
    internal static void Restore()
    {
        _lifted = false;
        if (!_armed)
            return;
        _armed = false;

        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return;
        UnityEngine.UI.UIWindow window = manager.window;
        if (window == null)
            return;
        CanvasGroup group = window.GetComponent<CanvasGroup>();
        if (group != null && window.IsOpen)
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }
    }
}

/// <summary>Module-internal signals raised by the patches (main thread).</summary>
internal static class CardsSignals
{
    /// <summary>The game (re)presented a hand: (player — null for the all-hands overload, mode).</summary>
    internal static event Action<CPlayerActor?, CardHandMode>? HandShown;

    /// <summary>A CardsHandUI is being destroyed — restore adopted faces NOW (before pool recycle).</summary>
    internal static event Action<CardsHandUI>? HandDestroying;

    /// <summary>A single card widget is about to be recycled.</summary>
    internal static event Action<AbilityCardUI>? CardRecycling;

    internal static void RaiseHandShown(CPlayerActor? player, CardHandMode mode)
    {
        try
        {
            HandShown?.Invoke(player, mode);
        }
        catch (Exception ex)
        {
            VRLog.Error("Cards", $"HandShown subscriber threw: {ex}");
        }
    }

    internal static void RaiseHandDestroying(CardsHandUI hand)
    {
        try
        {
            HandDestroying?.Invoke(hand);
        }
        catch (Exception ex)
        {
            VRLog.Error("Cards", $"HandDestroying subscriber threw: {ex}");
        }
    }

    internal static void RaiseCardRecycling(AbilityCardUI card)
    {
        try
        {
            CardRecycling?.Invoke(card);
        }
        catch (Exception ex)
        {
            VRLog.Error("Cards", $"CardRecycling subscriber threw: {ex}");
        }
    }

    internal static void Clear()
    {
        HandShown = null;
        HandDestroying = null;
        CardRecycling = null;
    }
}
