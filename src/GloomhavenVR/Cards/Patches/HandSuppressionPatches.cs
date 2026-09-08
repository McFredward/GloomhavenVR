using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
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
/// and (b) raise <see cref="VREvents.HandShown"/> so the VR layer rebuilds.
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
        VREvents.Raise(new HandShownEvent(playerActor, mode));
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
        VREvents.Raise(new HandShownEvent(null, mode));
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
    // ISOLATED (ModBuild 334) — and the ONLY one of the three Show postfixes that needs it.
    // The other two hand VREvents.Raise a literal and are covered by VREvents.Invoke's own
    // subscriber guard; this one reads two members off the live CardsHandManager first, and
    // CardsHandManager receives 13 of the game's ~121 network actions, whose dispatch turns any
    // exception into the player-facing "Desynchronization occurred". See NET-ACTION-SURFACE.md.
    private static void Postfix(CardsHandManager __instance)
        => Net.Desync.DispatchGuard.Run("CardsHandManager_ShowHands_Patch", () =>
        {
            HandSuppression.Arm();
            if (__instance == null)
                return;
            VREvents.Raise(new HandShownEvent(__instance.ActivePlayer, __instance.m_PushPopCardHandMode));
        }, "Cards");
}

/// <summary>Per-frame visual suppression state (driven by <see cref="CardsDriver"/>).</summary>
internal static class HandSuppression
{
    private static bool _armed;
    private static bool _lifted;
    private static int _burnCount;

    /// <summary>
    /// TASK #10: BurnActive is held for a short TAIL past the last burn registration so the
    /// per-frame flicker of the FX detection never drops it. BurnCardFx's per-card effect
    /// detection FLICKERS on/off between frames — the card's <c>CardEffects</c> FXTask is
    /// transiently unreadable while its face is re-adopted, which produces the per-frame
    /// "Burn/ghost effect playing" spam in the log and oscillated this ref-count
    /// (BeginBurn/EndBurn every frame). Refreshing the hold on every Begin AND End keeps
    /// BurnActive solid across that flicker.
    /// </summary>
    private static float _burnHoldUntil = float.NegativeInfinity;

    /// <summary>Seconds BurnActive stays true past the last burn registration (task #10 tail hold).</summary>
    private const float BurnTailSeconds = 0.5f;

    /// <summary>
    /// TASK #5 (residual end-of-burn flash): a short-rest / burn-redraw LEAVES the game
    /// UI-locked (<see cref="VRMode.ModalUI"/>) for a short, VARIABLE resolution window AFTER
    /// the burn FX — and its 0.5 s tail — have already ended (the sacrificed card is removed,
    /// the piles settle, THEN <c>UIManager.ToggleLockUI</c> releases). During that trailing lock
    /// nothing is converted/floated, so <c>FlatScreen.WantVisible</c>'s ModalUI catch-all raises
    /// the full desktop-mirror quad for a few frames — the flash the fixed wall-clock tail could
    /// not cover, because the lock routinely outlasts 0.5 s. So once a burn is seen we ALSO latch
    /// BurnActive across the trailing lock: it stays true while the mode is still ModalUI and
    /// self-releases the instant the mode leaves ModalUI (lock cleared → the burn flow is truly
    /// over). Burns that never enter ModalUI (a card lost/discarded from the hand plays in
    /// CardSelection/HalfSelection) clear the latch on the very first post-tail read, so it can
    /// never linger there. A hard cap is a final stuck-on guard should the mode somehow never
    /// leave ModalUI.
    /// </summary>
    private static bool _burnModalLatch;

    /// <summary>Absolute cap on the ModalUI latch past the last burn registration (stuck-on guard).</summary>
    private static float _burnLatchCapUntil = float.NegativeInfinity;

    /// <summary>Longest the trailing-ModalUI latch may hold past the last burn registration.</summary>
    private const float BurnModalLatchCapSeconds = 6f;

    /// <summary>Master switch, owned by CardsModule (true while VR/dev cards run).</summary>
    internal static bool Active { get; set; }

    /// <summary>Called from Show postfixes: a scenario hand exists, start enforcing.</summary>
    internal static void Arm() => _armed = true;

    /// <summary>
    /// True while at least one card burn/lose/discard animation is playing (ref-counted
    /// from <see cref="BurnCardFx"/>, one registration per burning card). ITEM 2: the flat
    /// 2D burn dissolve/flame is the game's screen-space uGUI on its hand canvas; the burn
    /// -confirm dialog below normally LIFTS suppression, un-hiding that canvas, and
    /// <c>FlatScreen</c> then mirrors the burning card into the VR modal quad. While a burn
    /// is active we keep the lift DOWN so nothing composites onto FlatScreen — the
    /// world-space smoke plume (bounded by <see cref="BurnCardFx"/>) still signals the burn.
    /// TASK #5: past the wall-clock tail, keep suppressing across the burn's trailing UI-lock
    /// until the game actually leaves ModalUI (self-releasing — see <see cref="_burnModalLatch"/>).
    /// </summary>
    internal static bool BurnActive
    {
        get
        {
            if (_burnCount > 0)
                return true;
            float now = Time.unscaledTime;
            if (now < _burnHoldUntil)
                return true;
            // Trailing UI-lock latch (task #5): the burn FX + its 0.5 s tail are over, but a
            // short-rest / burn-redraw leaves the game in ModalUI a beat longer while it
            // resolves. Hold until the lock releases so the empty ModalUI catch-all in
            // FlatScreen.WantVisible never flashes; clear the instant the mode is no longer
            // ModalUI (or at the hard cap) so suppression can never stick on.
            if (_burnModalLatch)
            {
                if (now < _burnLatchCapUntil
                    && VRModeStateMachine.CurrentMode == VRMode.ModalUI)
                    return true;
                _burnModalLatch = false;
            }
            return false;
        }
    }

    /// <summary>Register/unregister a live burn animation (balanced by BurnCardFx). Both edges
    /// refresh the tail hold + arm the trailing-ModalUI latch so neither a per-frame flip-flop of
    /// the detection nor the post-FX UI-lock resolution ever drops BurnActive.</summary>
    internal static void BeginBurn()
    {
        _burnCount++;
        RefreshBurnHold();
    }

    internal static void EndBurn()
    {
        if (_burnCount > 0)
            _burnCount--;
        RefreshBurnHold(); // hold past a flip-flop / the true end + across the trailing UI-lock
    }

    /// <summary>Refresh both the wall-clock tail and the trailing-ModalUI latch window.</summary>
    private static void RefreshBurnHold()
    {
        float now = Time.unscaledTime;
        _burnHoldUntil = now + BurnTailSeconds;
        _burnModalLatch = true;
        _burnLatchCapUntil = now + BurnModalLatchCapSeconds;
    }

    /// <summary>
    /// Enforce alpha-0 on the hand window each frame; lift while a game dialog lives
    /// inside the suppressed subtree. Cheap: two component lookups on cached
    /// references, no allocations.
    /// </summary>
    internal static void Tick()
    {
        // ModBuild 351: the burn-commit hang watch rides this per-frame call because it is the
        // only Cards tick that is guaranteed to run while a damage prompt is open. It is armed
        // only by an actual lose/burn commit and disarms itself, so it costs one bool test here.
        // DELIBERATELY ABOVE the bail-out below: a hang must still be named when the hand
        // suppression is not armed.
        BurnCommitWatch.Tick();

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
        // ITEM 2: while a burn animation plays, do NOT lift — otherwise the game's flat
        // screen-space burning card leaks onto the FlatScreen modal mirror. The confirm
        // itself happens BEFORE the burn effect starts, so the dialog is still visible
        // when the player commits; only the post-confirm burn stays flat-suppressed.
        bool lift = false;
        UIManager uiManager = UIManager.Instance;
        if (!BurnActive
            && uiManager != null && uiManager.dialogPopup != null && uiManager.dialogPopup.IsOpen()
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
        _burnCount = 0;
        _burnHoldUntil = float.NegativeInfinity;
        _burnModalLatch = false;
        _burnLatchCapUntil = float.NegativeInfinity;
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

/// <summary>
/// Module-internal signals raised by the patches (main thread). P5: <c>HandShown</c>
/// moved to the shared bus (<see cref="VREvents.HandShown"/>, MISSION A.3) so other
/// modules can observe hand presentation; the pool-safety signals below stay
/// module-local (their payloads are Cards-implementation details).
/// </summary>
internal static class CardsSignals
{
    /// <summary>A CardsHandUI is being destroyed — restore adopted faces NOW (before pool recycle).</summary>
    internal static event Action<CardsHandUI>? HandDestroying;

    /// <summary>A single card widget is about to be recycled.</summary>
    internal static event Action<AbilityCardUI>? CardRecycling;

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
        HandDestroying = null;
        CardRecycling = null;
    }
}
