using System;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using ScenarioRuleLibrary.CustomLevels;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// ONE EXTRA TUTORIAL STEP (user request 2026-08-02) — right after the tutorial's
/// "point the laser at the enemy portrait to see its turn" strip (HT_10, titleKey
/// <c>TUTORIAL_2_HELP_010</c>), teach the VR-native ALTERNATIVE: picking the enemy mini up
/// shows the same turn preview. The taught behaviour is real, not aspirational — it is
/// implemented by <see cref="FigureIntentPeek"/>, which drives the game's OWN portrait-hover
/// display path from the held-figure registry (see that class for the source proof).
///
/// IT IS A REAL STEP, NOT A NOTE (hardware round 2). A tutorial step that can be talked over by
/// the next scripted window, or that vanishes on a timer while the player is still working out
/// what to do, is not a step. So this one behaves like every scripted step around it:
/// <list type="number">
/// <item>THE CHAIN WAITS. From the moment the step becomes pending, the game's own next scripted
///   window is withheld by <see cref="TutorialChainHold"/> — in the tutorial that is <c>TB_11</c>
///   ("Beim Drüberfahren siehst du die Absichten…"), which the game would otherwise open in the
///   SAME frame that dismisses HT_10 (both hang off one <c>InitiativeAvatarHovered</c> event; the
///   proof is in that class's header). Nothing is dropped or reordered: the held message is
///   re-issued through the game's own show path the instant the hold releases.</item>
/// <item>IT ENDS WHEN THE PLAYER DOES THE THING. The only ordinary way out is
///   <see cref="PlayerHoldsFigure"/> — a figure actually in a hand. ANY figure counts, hero or
///   monster: teaching a grab must never be gated on one specific mini, or a player who picks up
///   the wrong one is stuck being told to do what they just did. There is no queue-based and no
///   short time-based dismissal any more; both existed in round 1 and both were exactly the
///   "it closed although I did nothing" defect the user reported.</item>
/// </list>
///
/// WHY A MOD-OWNED MESSAGE AND NOT A TEXT SWAP: the existing hint machinery
/// (<see cref="TutorialHints"/>) can only REWRITE a scripted message the game already shows.
/// This is an ADDITIONAL step, so it needs a window of its own — and the user has approved
/// that the VR tutorial may add windows where needed.
///
/// HOW IT IS INJECTED — through the game's own public entry point, so the mod's entire window
/// pipeline applies unchanged:
/// <c>LevelMessagesUIHandler.ShowHelpText(CLevelMessage)</c> (LevelMessagesUIHandler.cs:99).
/// That is byte-for-byte the call <c>LevelEventsController.ShowLevelMessageRoutine</c> makes for
/// a scripted HelpText strip (LevelEventsController.cs:865), so our step lands in the SAME
/// help-text group window as the strip it follows and therefore inherits, for free:
/// the VR float and its placement, the shared CHAIN POSE (ModalFallback's rule 2 — it reopens
/// exactly where the previous hint stood), the strip's look, and the NON-BLOCKING
/// classification (<c>ModalFallback.ActionDismissedLevelMessage</c> reads the message's own
/// <c>DismissTrigger.IsTriggeredByDismiss</c> — ours is false, exactly like every action-dismissed
/// scripted strip, so board/cards/hands stay fully live and the taught grab is actually possible).
/// Holding <c>TB_11</c> back matters for that too: <c>TB_11</c> is a BLOCKING box message
/// (hardware log: "Modal commit-block ENGAGED" right after it opened), so with it on screen the
/// room is in a modal state while the strip beside it asks for a two-handed board action.
///
/// WHY IT CAN NEVER DEADLOCK THE SCRIPTED CHAIN:
/// <list type="number">
/// <item>It is NOT in <c>LevelEventsController.m_MessagesToShow</c> and its
///   <c>DisplayTrigger</c> is never consulted: the mod shows it directly. The controller's
///   trigger stores are untouched, so no scripted display/dismiss trigger can be consumed,
///   delayed or re-ordered by us.</item>
/// <item>Its <c>DismissTrigger</c> is deliberately unmatchable (<c>EventTriggerTypeInt =
///   int.MaxValue</c>, not a UI-event trigger), so <c>ProcessEvent</c>'s dismiss check against
///   the currently displayed help text can never fire on it — the game will not consume a
///   gameplay event on our behalf either.</item>
/// <item>FAILSAFES — exactly three since the 2026-08-13 ruling removed the [Compat]
///   TutorialVRAdapt kill-switch (the fourth was "the switch was flipped mid-step"), and every
///   one of them releases the hold as well: the tutorial context ending (scenario left /
///   <c>LevelEventsController</c> inactive / <c>LevelMessagesUIHandler</c> gone / our window no
///   longer on the strip), ANY exception (latched for
///   the session and logged with its stack), and ONE absolute ceiling of
///   <see cref="MaxStepSeconds"/> that logs a loud <c>Warn</c> first. See that field for why it
///   is five minutes and not less.</item>
/// <item>Every exit that involves our window goes through the game's own
///   <c>HideCurrentlyShownHelpTextMessage()</c>, i.e. exactly what the message's own close action
///   would do, so the handler's bookkeeping is always left consistent.</item>
/// </list>
/// The only UIEvent our dismissal produces is <c>LevelMessageDismissed</c> carrying our UNIQUE
/// message name (<see cref="MessageName"/>) — no scripted trigger in the tutorial references it
/// (the flow dump's ctxIds are all <c>TB_*</c>/<c>HT_*</c>), so it is inert by construction.
///
/// SCOPE: single-player tutorial only (<see cref="TutorialVR.IsTutorialActive"/> refuses online
/// sessions outright) and fires at most once per scenario. Outside a tutorial nothing is ever
/// shown or held and the game is bit-for-bit vanilla.
/// </summary>
internal static class TutorialGrabStep
{
    /// <summary>The scripted strip we attach to: "point the laser at the enemy portrait…"
    /// (flow dump entry [17], 'HT_10'). Keyed on the TITLE KEY, not the message name, for the
    /// same reason <see cref="TutorialHints"/> is: the loc key is the stable identity of the
    /// STEP, while message names are per-level scaffolding.</summary>
    private const string AfterTitleKey = "TUTORIAL_2_HELP_010";

    /// <summary>Our message's identity in the game's logs and in the <c>LevelMessageDismissed</c>
    /// event our dismissal posts. Prefixed so it can never collide with a scripted name.</summary>
    internal const string MessageName = "GLOOMHAVENVR_TUT_GRAB_INTENT";

    /// <summary>
    /// The title key we hand the game. The strip's title IS its whole instruction line, and
    /// <see cref="TutorialHints"/> replaces the resolved text for our message by MESSAGE NAME —
    /// so the key itself only has to RESOLVE without an error. A mod-invented key would make
    /// <c>GLOOM.LocalizationManager.GetTranslation</c> log "term not found" and paint
    /// "UNDEFINED &lt;color=red&gt;…" for the frame before our postfix lands; reusing a key the
    /// game certainly ships (the one its own level-message close button uses) keeps the log clean
    /// and the first frame correct. The value is never read by the player.
    /// </summary>
    private const string PlaceholderTitleKey = "GUI_CONTINUE";

    /// <summary>Settle time between HT_10's dismissal and our show. The dismissal is raised from
    /// the middle of <c>ProcessEvent</c>, which then goes on to trigger the follow-up message and
    /// posts a queued UIEvent; showing into that same handover frame would race the handler's own
    /// <c>ShowNextHelpText</c> bookkeeping. The chain is already held at this point, so the wait
    /// costs the player nothing.</summary>
    private const float ArmSettleSeconds = 0.75f;

    /// <summary>
    /// THE ONE ABSOLUTE CEILING (arm → release), and the ONLY time-based exit that exists.
    ///
    /// It is deliberately far longer than "long enough to read a line": while the step is pending
    /// the scripted chain is WAITING on it, so this timer is not a display timeout, it is the
    /// anti-brick backstop for the whole hold. It must never expire on a player who is simply
    /// slow — looking around the room, missing the mini a few times, taking off the headset for a
    /// moment — because expiring is a visible failure of the step. Five minutes is far outside
    /// any plausible "I am doing it" window and still bounded enough that a wedged state (say a
    /// grab feature disabled by its own error latch, so the taught action cannot be performed at
    /// all) self-clears within one sitting instead of ending the tutorial run. It logs a loud
    /// <c>Warn</c> when it fires precisely because reaching it always means something else broke.
    /// </summary>
    private const float MaxStepSeconds = 300f;

    private static bool _disabledByError;
    private static bool _doneThisScenario;
    private static float _startedAt = -1f;   // arm time; also the ceiling's origin
    private static float _shownAt = -1f;
    private static CLevelMessage? _message;

    /// <summary>Pending = armed or shown, and not yet satisfied. While true the scripted chain is
    /// held back (<see cref="TutorialChainHold"/>).</summary>
    private static bool Pending => _startedAt >= 0f;

    /// <summary>
    /// SATISFACTION — the taught action, read from the LOCAL grab registry. ANY held figure
    /// counts. <see cref="HeldFigures"/> is the ground truth (a mini is in a hand);
    /// <see cref="FigureIntentPeek.Active"/> is the narrower "and it is driving a track preview"
    /// signal, which is false for a figure with no initiative-track entry (a summon, an actor mid
    /// track-rebuild). ORing them means the step can be completed with whatever the player
    /// reaches for first — the preview is the reward, not the pass mark.
    /// </summary>
    private static bool PlayerHoldsFigure => HeldFigures.Count > 0 || FigureIntentPeek.Active;

    /// <summary>Scenario boundary — called from the flow-dump postfix
    /// (<c>StartListeningForEvents</c>), the one point where a new scripted level begins. Anything
    /// still withheld belongs to the level that just ended and is dropped with it.</summary>
    internal static void Reset()
    {
        if (TutorialChainHold.Engaged)
            TutorialChainHold.Discard("a new scenario started while the VR figure-grab step was pending");
        ClearState();
        _doneThisScenario = false;
    }

    private static void ClearState()
    {
        _startedAt = -1f;
        _shownAt = -1f;
        _message = null;
    }

    /// <summary>
    /// ARM: the scripted strip we attach to was just dismissed (called from the existing
    /// <c>MessageWasDismissed</c> diagnostics postfix — no new Harmony patch). Two things happen
    /// here and nothing else: the chain hold engages (this runs INSIDE the <c>ProcessEvent</c>
    /// call that is about to trigger the follow-up message, which is the whole reason the
    /// follow-up can be caught), and the moment is recorded. The actual show happens from
    /// <see cref="Tick"/> once the handover has settled and the help-text group is provably free.
    /// </summary>
    internal static void NoteDismissed(CLevelMessage? messageDismissed)
    {
        if (_disabledByError || _doneThisScenario || Pending)
            return;
        if (messageDismissed == null)
            return;
        if (!string.Equals(messageDismissed.TitleKey, AfterTitleKey, StringComparison.OrdinalIgnoreCase))
            return;

        // Already holding a mini? Then the player has performed the taught action before being
        // asked, and the step is complete before it starts. Do NOT hold the chain and do NOT open
        // a window to teach what was just demonstrated.
        if (PlayerHoldsFigure)
        {
            _doneThisScenario = true;
            VRLog.Info("Tutorial", $"'{messageDismissed.MessageName}' (the laser/portrait step) was "
                + "completed while a figure was already in hand — the VR-only follow-up step is "
                + "skipped (it would teach an action the player just performed) and the scripted "
                + "chain continues untouched.");
            return;
        }

        _startedAt = Time.unscaledTime;
        _doneThisScenario = true; // one attempt per scenario, decided here
        TutorialChainHold.Engage($"'{messageDismissed.MessageName}' (the laser/portrait step) was "
            + "completed, so the VR-only follow-up step (hold a figure to see the same turn "
            + "preview) is now pending");
        VRLog.Info("Tutorial", "VR figure-grab step ARMED — it will show as soon as the help-text "
            + "strip is free, and it stays until the player actually takes a figure into their "
            + "hand. The tutorial's next scripted window waits for that.");
    }

    /// <summary>
    /// Per-frame service (WorldUI driver, TickGuard-wrapped). Cold path: two static reads.
    /// Owns BOTH transitions — arm→shown and shown→finished — so every exit is in one place.
    /// </summary>
    internal static void Tick()
    {
        if (_disabledByError || !Pending)
            return;
        // FAILSAFE 2 (the mid-step kill-switch bail) is GONE with [Compat] TutorialVRAdapt
        // itself — user ruling 2026-08-13. The step can no longer be switched off underneath
        // itself; the remaining failsafes (error latch, scenario end, timeout) are unchanged.
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            // FAILSAFE 3 — latch + self-clear: whatever went wrong, the scripted chain must be
            // handed back immediately and never held again this session.
            _disabledByError = true;
            try { Finish("the step threw"); }
            catch (Exception) { TutorialChainHold.Release("the step threw during its own teardown"); }
            VRLog.Error("Tutorial", "VR figure-grab tutorial step threw and is disabled for this "
                + $"session: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void TickCore()
    {
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        bool inTutorial = LevelEventsController.s_EventsControllerActive
                          && TutorialVR.IsTutorialActive
                          && handler != null;

        // FAILSAFE 1 — scenario left / tutorial context lost / handler gone.
        if (!inTutorial)
        {
            Finish("the tutorial context ended (scenario left, or the message handler is gone)");
            return;
        }

        // SATISFACTION — checked before anything else, in both states: a player who grabs a mini
        // during the settle window has done the step and must not be shown a window about it.
        if (PlayerHoldsFigure)
        {
            Finish("the player picked a figure up — the taught action was performed");
            return;
        }

        // FAILSAFE 4 — the one absolute ceiling. Loud, because reaching it means the taught
        // action could not be performed at all (see MaxStepSeconds).
        float now = Time.unscaledTime;
        if (now - _startedAt >= MaxStepSeconds)
        {
            VRLog.Warn("Tutorial", $"VR figure-grab step: NO figure was picked up within "
                + $"{MaxStepSeconds:0}s. That should be impossible for a player who is simply "
                + "taking their time, so treat this as a symptom: check whether figure grab is "
                + "available at all ([FigureGrab] GrabFigures, or its error latch in the log "
                + "above). Releasing the tutorial chain now so the run can continue.");
            Finish($"the {MaxStepSeconds:0}s absolute ceiling was reached (anti-brick backstop)");
            return;
        }

        if (_shownAt < 0f)
        {
            // ---- armed, waiting for a free strip ----------------------------------------------
            if (now - _startedAt < ArmSettleSeconds)
                return;
            if (handler!.DisplayDelayInEffect || handler.CurrentlyDisplayedHelpTextMessage != null)
                return; // a scripted strip owns the group — never elbow it aside
            // Guarded no-op in practice, stated for the record: CompleteLevel arms a one-shot
            // "next dismissal ends the level" action (LevelEventsController.cs:929). We must never
            // be the dismissal that consumes it, so we simply do not show while it is armed.
            if (LevelEventsController.s_Instance == null
                || LevelEventsController.s_Instance.m_ActionForNextMessageDismissal != null)
                return;

            Show(handler);
            return;
        }

        // ---- shown ---------------------------------------------------------------------------
        // FAILSAFE 1 (continued) — our window is no longer the one on the strip. Unreachable while
        // the hold is engaged (every scripted message goes through the held funnel), kept because
        // a step that has lost its window must not keep the chain waiting on it.
        if (!ReferenceEquals(handler!.CurrentlyDisplayedHelpTextMessage, _message))
            Finish("the help-text strip is no longer ours");
    }

    private static void Show(LevelMessagesUIHandler handler)
    {
        _message = BuildMessage();
        _shownAt = Time.unscaledTime;
        // The game's own show path for a scripted HelpText strip (LevelEventsController.cs:865).
        handler.ShowHelpText(_message);
        VRLog.Info("Tutorial", $"VR figure-grab step '{MessageName}' SHOWN through the game's own "
            + "LevelMessagesUIHandler.ShowHelpText — same strip window, same VR float and chain "
            + "pose, action-dismissed (non-blocking). It closes ONLY when a figure is actually "
            + "picked up; the tutorial's next scripted window is held back until then.");
    }

    /// <summary>
    /// THE SINGLE EXIT. Closes our window if it is still ours, releases the chain hold (which
    /// re-issues whatever was withheld, in order), and retires the step for this scenario.
    /// </summary>
    private static void Finish(string reason)
    {
        DismissIfOurs(reason);
        ClearState();
        _doneThisScenario = true;
        TutorialChainHold.Release(reason);
    }

    /// <summary>Only ever dismisses while the strip provably still shows OUR message — the game's
    /// own dismissal path, identical to what the message's <c>OnClosedPressedAction</c> runs
    /// (LevelMessagesUIHandler.cs:163), so the handler is never left half-updated.</summary>
    private static void DismissIfOurs(string reason)
    {
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (_shownAt < 0f || handler == null
            || !ReferenceEquals(handler.CurrentlyDisplayedHelpTextMessage, _message))
            return;
        handler.HideCurrentlyShownHelpTextMessage();
        VRLog.Info("Tutorial", $"VR figure-grab step window closed — {reason}.");
    }

    /// <summary>
    /// The mod-owned message. HelpText layout = the one-line action strip HT_10 used, so it
    /// appears in the same window with the same look. The dismiss trigger is intentionally
    /// UNMATCHABLE (<c>int.MaxValue</c> with <c>IsUIEventTypeTrigger=false</c>): both arms of
    /// <c>ShouldEventCauseTrigger</c> compare the trigger type against the event's type
    /// (LevelEventsController.cs:561/632), so no engine SEvent and no UIEvent can ever dismiss
    /// it behind our back — while <c>IsTriggeredByDismiss=false</c> still classifies it
    /// ACTION-dismissed, which is what keeps it non-blocking in VR. No pages (the strip's title
    /// is the whole line), no pause, no screen background, no camera profile, no interactability
    /// profile with entries — i.e. nothing that could change game or input state.
    /// </summary>
    private static CLevelMessage BuildMessage()
    {
        var message = new CLevelMessage
        {
            MessageName = MessageName,
            LayoutType = CLevelMessage.ELevelMessageLayoutType.HelpText,
            TitleKey = PlaceholderTitleKey,
            DisplayDelay = 0f,
            ShouldPauseGame = false,
            ShowScreenBG = false,
        };
        message.DismissTrigger.IsTriggeredByDismiss = false;
        message.DismissTrigger.IsUIEventTypeTrigger = false;
        message.DismissTrigger.EventTriggerTypeInt = int.MaxValue;
        message.DismissTrigger.EventTriggerSubTypeInt = int.MaxValue;
        return message;
    }
}
