using System;
using GloomhavenVR.Core;
using ScenarioRuleLibrary.CustomLevels;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Compat;

/// <summary>
/// LEVEL-MESSAGE STUCK-STATE HEAL — the tutorial-deadlock-#2 insurance (belt AND
/// suspenders; the belt is the ModalFallback poll's ground-truth + debounce fix).
///
/// THE RACE THIS HEALS (hardware log 2026-08-02, TB_2_1→TB_2_2 handover): the game's
/// <c>LevelMessageUILayoutGroup</c> keeps a STATIC <c>IsShown</c> flag whose reset is an
/// end-of-frame coroutine armed by <c>HideWindow()</c> (LevelMessageUILayoutGroup.cs:93-99)
/// that never checks whether a NEW <c>Show()</c> ran in between. A dismiss-button press on
/// a scripted message shows the next one SYNCHRONOUSLY in the same frame
/// (<c>HideCurrentlyShownBoxMessage</c> → <c>ShowNextBoxMessage</c> →
/// <c>StartCoroutine(DisplayMessageInWindowAfterDelay)</c> runs straight to
/// <c>window.Show()</c> when DisplayDelay=0, LevelMessagesUIHandler.cs:153-203), so the
/// fresh <c>IsShown=true</c> is clobbered at frame end: the flag reads false forever while
/// the game is waiting on a box whose dismiss button nobody can reach — the tutorial's
/// strictly dismiss-chained hint chain is then dead (TB_3's display trigger is TB_2_2's
/// LevelMessageDismissed and can never fire). The flag is also SHARED across both group
/// instances (box + helptext), so either group's hide can strand the other.
///
/// DETECTION (level-triggered, per tick): a current message the game itself reports as
/// displayed (<c>m_CurrentlyDisplayed*MessageInfo</c> set, publicized) whose dismiss is a
/// BUTTON (<c>DismissTrigger.IsTriggeredByDismiss</c> — only those deadlock; event-dismissed
/// messages close from gameplay regardless), while no display delay is pending AND the
/// shown-state is broken (<c>IsShown</c> false OR the group's own window not open) — held
/// continuously for <see cref="StuckSecondsToHeal"/> so show/hide transitions and the
/// legitimate 1-frame handover flicker never trip it.
///
/// HEAL: re-show the CURRENT message through the game's own path —
/// <c>group.Show(info.Message, info.OnClosedPressedAction)</c>, byte-for-byte the call
/// <c>DisplayMessageInWindowAfterDelay</c> makes (LevelMessagesUIHandler.cs:202) — which
/// re-inits the layout, re-registers the dismiss action and sets <c>IsShown=true</c>. No
/// game DATA is touched, no triggers are re-fired (<c>MessageWasDisplayed</c> lives in the
/// caller coroutine, not in <c>Show</c>), and when the window is actually still open the
/// inner <c>window.Show()</c> no-ops (UIWindow.Show guards on the current visual state) —
/// the heal then only repairs the flag + layout. Throttled, error-latched, loud Info log.
///
/// SCOPE: any scripted level (<c>LevelEventsController.s_EventsControllerActive</c>), not
/// just tutorials — the stuck predicate is unreachable in a healthy flow (after a genuine
/// dismiss the current message is nulled or replaced, LevelMessagesUIHandler.cs:115-161),
/// and non-tutorial scripted levels run the identical dismiss→show handover, so they carry
/// the identical race. MP-safe: level-message display is purely local UI, the re-shown
/// dismiss action is the same local delegate the game registered — zero wire traffic.
/// Kill-switch: rides <c>[Compat] TutorialVRAdapt</c> (the tutorial-flow feature family).
/// </summary>
internal static class LevelMessageHeal
{
    /// <summary>How long the stuck predicate must hold continuously before healing —
    /// generously above any show/hide fade (UIWindow default 0.1 s) and the poll debounce.</summary>
    private const float StuckSecondsToHeal = 1.0f;

    /// <summary>Min spacing between two heals — a heal that does not stick (predicate
    /// re-trips) must re-fire slowly enough to stay readable in the log, never per frame.</summary>
    private const float HealCooldownSeconds = 3f;

    private static bool _disabledByError;
    private static float _boxStuckSince = -1f;
    private static float _helpStuckSince = -1f;
    private static float _cooldownUntil;
    private static int _heals;

    /// <summary>
    /// Per-frame service (WorldUI driver, TickGuard-wrapped). Cold path outside scripted
    /// levels: two static reads. TickGuard already isolates a throw, but the error latch
    /// makes a persistent failure disarm PERMANENTLY instead of re-throwing every 10 s.
    /// </summary>
    internal static void Tick()
    {
        if (_disabledByError || !Plugin.TutorialVRAdapt.Value)
            return;
        if (!LevelEventsController.s_EventsControllerActive)
        {
            _boxStuckSince = _helpStuckSince = -1f;
            return;
        }
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            // TutorialVR-pattern latch: a heal bug must never flood the log or destabilize
            // the level-message UI it exists to protect.
            _disabledByError = true;
            VRLog.Error("Tutorial", "level-message heal threw and is disabled for this session: "
                + $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void TickCore()
    {
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (handler == null || handler.DisplayDelayInEffect)
        {
            // A delayed next message legitimately sits "current but not shown" — never stuck.
            _boxStuckSince = _helpStuckSince = -1f;
            return;
        }
        HealGroup(handler.m_CurrentlyDisplayedBoxMessageInfo,
            handler.LevelMessageBoxLayoutGroup, ref _boxStuckSince, "box");
        HealGroup(handler.m_CurrentlyDisplayedHelpTextMessageInfo,
            handler.LevelMessageHelpTextLayoutGroup, ref _helpStuckSince, "helptext");
    }

    private static void HealGroup(LevelMessagesUIHandler.MessageInfo? info,
        LevelMessageUILayoutGroup? group, ref float stuckSince, string kind)
    {
        CLevelMessage? msg = info?.Message;
        UIWindow? window = group != null ? group.window : null;
        bool stuck = msg != null && window != null
                     && msg.DismissTrigger != null && msg.DismissTrigger.IsTriggeredByDismiss
                     && (!LevelMessageUILayoutGroup.IsShown || !window.IsOpen);
        if (!stuck)
        {
            stuckSince = -1f;
            return;
        }
        float now = Time.unscaledTime;
        if (stuckSince < 0f)
        {
            stuckSince = now;
            return;
        }
        if (now - stuckSince < StuckSecondsToHeal || now < _cooldownUntil)
            return;

        _cooldownUntil = now + HealCooldownSeconds;
        stuckSince = -1f; // re-measure from scratch — a stick that heals never re-fires
        _heals++;
        bool wasOpen = window!.IsOpen;
        // The game's own show path (LevelMessagesUIHandler.cs:202): same message, same
        // registered dismiss action, no autoclose (the original call passes none either).
        group!.Show(msg!, info!.OnClosedPressedAction);
        VRLog.Info("Tutorial", $"HEAL #{_heals}: scripted {kind} message '{msg!.MessageName}' was "
            + $"logically ACTIVE with a dismiss button but not shown for {StuckSecondsToHeal:0.0}s+ "
            + $"(IsShown={LevelMessageUILayoutGroup.IsShown} after heal, window was "
            + $"{(wasOpen ? "OPEN with a stale IsShown=false (the hide→show flag clobber)" : "closed (show swallowed)")}) — "
            + "re-shown via the game's own LevelMessageUILayoutGroup.Show; the dismiss chain is live again.");
    }
}
