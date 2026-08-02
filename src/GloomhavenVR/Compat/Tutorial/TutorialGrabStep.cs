using System;
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
///
/// WHY IT CAN NEVER DEADLOCK THE SCRIPTED CHAIN — four independent guarantees:
/// <list type="number">
/// <item>It is NOT in <c>LevelEventsController.m_MessagesToShow</c> and its
///   <c>DisplayTrigger</c> is never consulted: the mod shows it directly. The controller's
///   trigger stores are untouched, so no scripted display/dismiss trigger can be consumed,
///   delayed or re-ordered by us.</item>
/// <item>Its <c>DismissTrigger</c> is deliberately unmatchable (<c>EventTriggerTypeInt =
///   int.MaxValue</c>, not a UI-event trigger), so <c>ProcessEvent</c>'s dismiss check against
///   the currently displayed help text can never fire on it — the game will not consume a
///   gameplay event on our behalf either.</item>
/// <item>QUEUE YIELD: the handler shows one help text at a time and queues the rest
///   (<c>m_PendingHelpTextMessages</c>). The moment the game queues its OWN next strip behind
///   ours, we dismiss immediately — so the scripted chain is never held for more than the frame
///   in which it asks.</item>
/// <item>Hard timeout + context checks: the step self-dismisses after
///   <see cref="MaxShownSeconds"/>, on leaving the tutorial context, and on any error (latched,
///   logged). Every exit path goes through the game's own
///   <c>HideCurrentlyShownHelpTextMessage()</c>, i.e. exactly what the message's own close action
///   would do, so the handler's bookkeeping is always left consistent.</item>
/// </list>
/// The only UIEvent our dismissal produces is <c>LevelMessageDismissed</c> carrying our UNIQUE
/// message name (<see cref="MessageName"/>) — no scripted trigger in the tutorial references it
/// (the flow dump's ctxIds are all <c>TB_*</c>/<c>HT_*</c>), so it is inert by construction.
///
/// DISMISSAL THE PLAYER CONTROLS: performing the taught action. <see cref="FigureIntentPeek.Active"/>
/// goes true the instant a held figure drives a track preview — that is the same signal the
/// feature itself runs on, so "the hint closes when you do it" cannot drift away from "it worked".
/// (The help-text strip layout has no close button in the game's prefab — action-dismissed strips
/// never do, HT_10 included — so matching the message it follows means matching that too; the
/// timeout and the queue yield are the fallbacks.)
///
/// SCOPE: single-player tutorial only (<see cref="TutorialVR.IsTutorialActive"/> refuses online
/// sessions outright), fires at most once per scenario, rides the <c>[Compat] TutorialVRAdapt</c>
/// kill-switch. Off ⇒ nothing is ever shown and the tutorial is bit-for-bit vanilla.
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

    /// <summary>Settle time between HT_10's dismissal and our show. The dismissal synchronously
    /// opens the scripted box that follows (TB_11) and posts a queued UIEvent; showing into that
    /// same handover frame would race the handler's own <c>ShowNextHelpText</c> bookkeeping.</summary>
    private const float ArmSettleSeconds = 0.75f;

    /// <summary>Give up arming if the help-text strip never frees up (the scripted chain moved on
    /// in a way we did not anticipate) — the step is a bonus, never a thing that waits forever.</summary>
    private const float ArmGiveUpSeconds = 30f;

    /// <summary>Hard ceiling on how long our strip may occupy the group. Generous enough to read
    /// the box beside it and reach for the mini, short enough that a wedged state self-clears.</summary>
    private const float MaxShownSeconds = 90f;

    private static bool _disabledByError;
    private static bool _doneThisScenario;
    private static float _armedAt = -1f;
    private static CLevelMessage? _message;
    private static float _shownAt = -1f;

    /// <summary>Scenario boundary — called from the flow-dump postfix
    /// (<c>StartListeningForEvents</c>), the one point where a new scripted level begins.</summary>
    internal static void Reset()
    {
        _doneThisScenario = false;
        _armedAt = -1f;
        _message = null;
        _shownAt = -1f;
    }

    /// <summary>
    /// ARM: the scripted strip we attach to was just dismissed (called from the existing
    /// <c>MessageWasDismissed</c> diagnostics postfix — no new Harmony patch). We only record the
    /// moment; the actual show happens from <see cref="Tick"/> once the handover has settled and
    /// the help-text group is provably free.
    /// </summary>
    internal static void NoteDismissed(CLevelMessage? messageDismissed)
    {
        if (_disabledByError || _doneThisScenario || _armedAt >= 0f || _shownAt >= 0f)
            return;
        if (messageDismissed == null || !Plugin.TutorialVRAdapt.Value)
            return;
        if (!string.Equals(messageDismissed.TitleKey, AfterTitleKey, StringComparison.OrdinalIgnoreCase))
            return;
        _armedAt = Time.unscaledTime;
        VRLog.Info("Tutorial", $"'{messageDismissed.MessageName}' (the laser/portrait step) was "
            + "completed — the VR-only follow-up step (hold the enemy mini to see the same turn "
            + "preview) is armed and will show as soon as the help-text strip is free.");
    }

    /// <summary>
    /// Per-frame service (WorldUI driver, TickGuard-wrapped). Cold path: two static reads.
    /// Owns BOTH transitions — arm→shown and shown→dismissed — so every exit is in one place.
    /// </summary>
    internal static void Tick()
    {
        if (_disabledByError || (_armedAt < 0f && _shownAt < 0f))
            return;
        if (!Plugin.TutorialVRAdapt.Value)
        {
            // Kill-switch flipped mid-step: drop our window through the game's own path.
            DismissIfOurs("the tutorial VR adaptation was switched off");
            Reset();
            return;
        }
        try
        {
            TickCore();
        }
        catch (Exception ex)
        {
            // Latch + self-clear: whatever went wrong, the scripted chain must be left alone.
            _disabledByError = true;
            try { DismissIfOurs("the step threw"); } catch (Exception) { /* nothing left to do */ }
            Reset();
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

        if (_shownAt >= 0f)
        {
            TickShown(handler, inTutorial);
            return;
        }

        // ---- armed, waiting for a free strip ------------------------------------------------
        float now = Time.unscaledTime;
        if (!inTutorial)
        {
            Disarm("the tutorial context ended before the step could be shown");
            return;
        }
        if (now - _armedAt >= ArmGiveUpSeconds)
        {
            Disarm($"the help-text strip stayed busy for {ArmGiveUpSeconds:0}s");
            return;
        }
        if (now - _armedAt < ArmSettleSeconds)
            return;
        if (handler!.DisplayDelayInEffect || handler.CurrentlyDisplayedHelpTextMessage != null)
            return; // a scripted strip owns the group — never elbow it aside
        if (FigureIntentPeek.Active)
            return; // already holding one: the hint's own dismiss condition is met, so showing
                    // it now would only flash. Wait for the release and teach it then.
        // Guarded no-op in practice, stated for the record: CompleteLevel arms a one-shot
        // "next dismissal ends the level" action (LevelEventsController.cs:929). We must never
        // be the dismissal that consumes it, so we simply do not show while it is armed.
        if (LevelEventsController.s_Instance == null
            || LevelEventsController.s_Instance.m_ActionForNextMessageDismissal != null)
            return;

        Show(handler);
    }

    private static void TickShown(LevelMessagesUIHandler? handler, bool inTutorial)
    {
        if (!inTutorial || handler == null)
        {
            // Scenario/handler gone — the window went with it; just forget the step.
            Reset();
            return;
        }
        if (!ReferenceEquals(handler.CurrentlyDisplayedHelpTextMessage, _message))
        {
            // Something else owns the strip now. Never dismiss a message that is not ours.
            VRLog.Info("Tutorial", "VR figure-grab step: the help-text strip is no longer ours — "
                + "state dropped without touching the game's message.");
            Reset();
            return;
        }

        if (FigureIntentPeek.Active)
        {
            Dismiss(handler, "the player picked a figure up — the taught action was performed");
            return;
        }
        int queued = handler.m_PendingHelpTextMessages?.Count ?? 0;
        if (queued > 1)
        {
            // The game wants the strip back for its OWN next hint: yield the same frame.
            Dismiss(handler, "the scripted chain queued its next help text behind us");
            return;
        }
        if (Time.unscaledTime - _shownAt >= MaxShownSeconds)
            Dismiss(handler, $"the {MaxShownSeconds:0}s display ceiling was reached");
    }

    private static void Show(LevelMessagesUIHandler handler)
    {
        _message = BuildMessage();
        _armedAt = -1f;
        _shownAt = Time.unscaledTime;
        _doneThisScenario = true;
        // The game's own show path for a scripted HelpText strip (LevelEventsController.cs:865).
        handler.ShowHelpText(_message);
        VRLog.Info("Tutorial", $"VR figure-grab step '{MessageName}' SHOWN through the game's own "
            + "LevelMessagesUIHandler.ShowHelpText — same strip window, same VR float and chain "
            + "pose, action-dismissed (non-blocking). It closes when a figure is picked up, when "
            + "the scripted chain wants the strip back, or after the display ceiling.");
    }

    private static void Dismiss(LevelMessagesUIHandler handler, string reason)
    {
        // The game's own dismissal — identical to what the message's OnClosedPressedAction runs
        // (LevelMessagesUIHandler.cs:163): hides the window, reports the dismissal, shows whatever
        // the game queued behind us. Nothing bespoke, so the handler is never left half-updated.
        handler.HideCurrentlyShownHelpTextMessage();
        VRLog.Info("Tutorial", $"VR figure-grab step dismissed — {reason}.");
        Reset();
    }

    /// <summary>Best-effort teardown for the abnormal exits (kill-switch, throw): only ever
    /// dismisses while the strip provably still shows OUR message.</summary>
    private static void DismissIfOurs(string reason)
    {
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (_shownAt < 0f || handler == null
            || !ReferenceEquals(handler.CurrentlyDisplayedHelpTextMessage, _message))
            return;
        handler.HideCurrentlyShownHelpTextMessage();
        VRLog.Info("Tutorial", $"VR figure-grab step dismissed — {reason}.");
    }

    private static void Disarm(string reason)
    {
        VRLog.Info("Tutorial", $"VR figure-grab step not shown — {reason}. The scripted chain is "
            + "untouched (the step is additive; skipping it changes nothing).");
        Reset();
        _doneThisScenario = true; // do not retry inside the same scenario
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
