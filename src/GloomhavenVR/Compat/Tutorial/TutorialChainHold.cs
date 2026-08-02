using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary.CustomLevels;

namespace GloomhavenVR.Compat;

/// <summary>
/// SEQUENCING GATE FOR THE MOD-OWNED TUTORIAL STEP (hardware round 2, 2026-08-02).
///
/// THE DEFECT THIS FIXES: <see cref="TutorialGrabStep"/> shows its "pick the mini up" strip when
/// the scripted laser/portrait step (<c>HT_10</c>) is dismissed — but the game opened its NEXT
/// scripted window in the SAME frame, so both stood in the room at once and the player was told
/// two things simultaneously. Proven from the sources plus the hardware log:
/// <list type="number">
/// <item><c>HT_10</c>'s DISMISS trigger and <c>TB_11</c>'s DISPLAY trigger are the SAME UIEvent,
///   <c>InitiativeAvatarHovered actor='Bandit Guard'</c> (flow dump entries [17] and [18],
///   .planning/debug/LogOutput.log:438-441).</item>
/// <item><c>LevelEventsController.ProcessEvent</c> handles that ONE event in ONE call: it first
///   hides the displayed help text (LevelEventsController.cs:447-453 — which is what arms us, via
///   <c>MessageWasDismissed</c>) and then, further down the very same method, walks
///   <c>m_MessagesToShow</c> and calls <c>ShowLevelMessage</c> on everything the event triggers
///   (LevelEventsController.cs:456-469) — i.e. <c>TB_11</c>, synchronously.</item>
/// <item>The hardware log shows exactly that adjacency: "hint DISMISSED 'HT_10'" → our arm line →
///   "hint SHOWN 'TB_11' (FixedLowerRight)" (LogOutput.log:1559-1561), and our own strip only
///   appears ~2 s later (LogOutput.log:1591) — on top of an already-open <c>TB_11</c>.</item>
/// </list>
/// So the window held here is <c>TB_11</c> (layout FixedLowerRight, pages
/// <c>TUTORIAL_2_TEXT_011_1..3</c> — "Beim Drüberfahren siehst du die Absichten…"), the one the
/// user saw open in parallel. It is a BOX message, not a help text, which is why the old
/// "yield when the help-text QUEUE fills" logic could never have caught it: the two layouts use
/// two independent queues in <c>LevelMessagesUIHandler</c> and never contend.
///
/// THE HOLD POINT: <c>LevelEventsController.ShowLevelMessage(CLevelMessage)</c>
/// (LevelEventsController.cs:846) — the single funnel through which EVERY scripted message
/// reaches a window, for all three layouts (StoryDialog / HelpText / box), from both callers
/// (<c>ProcessEvent</c> and <c>CompleteLevel</c>). A prefix here captures the message and returns
/// false; on release we call the very same method again with the very same message, so the game
/// does its own showing, in its own order, one beat later.
///
/// WHY THIS POINT AND NOT THE HANDLER: everything that makes a message "happen" lives DOWNSTREAM
/// of it — <c>MessageWasDisplayed</c> (interactability profile, <c>Main.Pause3DWorld</c>, camera
/// profile; LevelEventsController.cs:943) is raised by the handler's display coroutine, so a
/// message held here has not paused, re-profiled or moved anything yet. Nothing is dropped and
/// nothing is reordered: held messages replay FIFO through the same entry point.
///
/// WHY THE CONTROLLER'S BOOKKEEPING IS UNDISTURBED: <c>ProcessEvent</c> moves the message from
/// <c>m_MessagesToShow</c> to <c>m_AlreadyShownMessages</c> around the <c>ShowLevelMessage</c>
/// call, so that happens identically whether we hold or not — no trigger is consumed, delayed or
/// re-evaluated by us. The chain BEHIND the held message stalls on its own accord because it is
/// data-driven: <c>HT_11_1</c> waits on <c>LevelMessageDismissed ctxId='TB_11'</c> (flow dump
/// [19]), an event that can only exist once <c>TB_11</c> has actually been displayed and closed.
/// No scripted message in the tutorial names <c>TB_11</c> in a <c>needsPlayed</c> prerequisite, so
/// the "already shown" flag being set early cannot release anything ahead of time.
///
/// ANTI-BRICK: the level-completion messages are NEVER held. <c>CompleteLevel</c> shows the
/// scenario-won/lost message and then arms <c>m_ActionForNextMessageDismissal</c> to END the level
/// on its dismissal (LevelEventsController.cs:906-934) — holding one would strand the scenario.
/// They are identified exactly the way <c>CompleteLevel</c> itself finds them: a UI-event display
/// trigger of type <c>ScenarioWon</c>(30) / <c>ScenarioLost</c>(31) (UIEvent.cs:37-38). Beyond
/// that the hold has no lifetime of its own — <see cref="TutorialGrabStep"/> owns every release,
/// including the 5-minute ceiling.
///
/// Reflection-guarded like <see cref="WallFadeDisable"/>: if the type or the method cannot be
/// resolved, <see cref="TargetMethod"/> returns null, Harmony patches nothing, and the tutorial
/// chain runs bit-for-bit vanilla (the extra VR step then still shows, just without sequencing).
/// Single-player tutorial only, zero wire traffic: nothing here is networked and the whole class
/// is inert unless <see cref="TutorialGrabStep"/> engages it inside a tutorial scenario.
/// </summary>
[HarmonyPatch]
internal static class TutorialChainHold
{
    private const string ControllerTypeName = "LevelEventsController";
    private const string ShowMethodName = "ShowLevelMessage";

    /// <summary><c>UIEvent.EUIEventType.ScenarioWon</c> / <c>ScenarioLost</c> (UIEvent.cs:37-38) —
    /// the display triggers <c>CompleteLevel</c> looks for. Messages carrying them end the
    /// scenario when dismissed and are therefore never eligible for the hold.</summary>
    private const int ScenarioWonTriggerInt = 30;
    private const int ScenarioLostTriggerInt = 31;

    /// <summary>Scripted messages captured while the hold is engaged, in arrival order. Replayed
    /// FIFO on release. In the tutorial this only ever holds <c>TB_11</c>: the chain behind it is
    /// data-driven and cannot advance until <c>TB_11</c> is displayed and dismissed.</summary>
    private static readonly List<CLevelMessage> HeldMessages = new(2);

    /// <summary>The controller that wanted to show the held messages — replaying on a different
    /// (or dead) controller would inject a previous level's messages into the current one.</summary>
    private static LevelEventsController? _controller;

    private static MethodInfo? _showLevelMessage;
    private static bool _engaged;
    private static bool _degraded;
    private static bool _replaying;

    /// <summary>True while scripted messages are being withheld. Read by
    /// <see cref="TutorialGrabStep"/> only — nothing else may engage or release the hold.</summary>
    internal static bool Engaged => _engaged;

    /// <summary>True when the patch resolved its target and a hold is therefore possible at all.
    /// False means a game update moved the method: the VR step then runs unsequenced instead of
    /// silently pretending to hold the chain.</summary>
    internal static bool Available => !_degraded && _showLevelMessage != null;

    /// <summary>
    /// Harmony asks this for the method to patch. Resolving by NAME (and returning null on any
    /// miss) is what degrades the whole gate to a strict no-op on a renamed/removed target.
    /// </summary>
    private static MethodBase? TargetMethod()
    {
        try
        {
            Type? controller = AccessTools.TypeByName(ControllerTypeName);
            if (controller == null)
                return Degrade($"type not found: {ControllerTypeName}");

            MethodInfo? show = AccessTools.Method(
                controller, ShowMethodName, new[] { typeof(CLevelMessage) });
            if (show == null)
                return Degrade($"method not found: {ControllerTypeName}.{ShowMethodName}(CLevelMessage)");

            _showLevelMessage = show;
            return show;
        }
        catch (Exception e)
        {
            return Degrade($"resolution threw: {e.Message}");
        }
    }

    /// <summary>
    /// Runs INSTEAD of the game's show when the message is eligible for the hold; otherwise falls
    /// straight through. Any throw fails OPEN (the message is shown) and permanently degrades the
    /// gate — a sequencing nicety must never be able to swallow a tutorial window.
    /// </summary>
    private static bool Prefix(LevelEventsController __instance, CLevelMessage message)
    {
        try
        {
            return !ShouldHold(__instance, message);
        }
        catch (Exception e)
        {
            Degrade($"hold test threw: {e.GetType().Name}: {e.Message}");
            FailOpen();
            return true;
        }
    }

    private static bool ShouldHold(LevelEventsController controller, CLevelMessage message)
    {
        if (!_engaged || _replaying || message == null)
            return false;
        // Kill-switch / context: the gate may only act inside a live single-player tutorial.
        if (!Plugin.TutorialVRAdapt.Value || !TutorialVR.IsTutorialActive)
            return false;
        // NEVER the scenario-won/lost message — dismissing it is what ends the level.
        if (message.DisplayTrigger != null
            && message.DisplayTrigger.IsUIEventTypeTrigger
            && (message.DisplayTrigger.EventTriggerTypeInt == ScenarioWonTriggerInt
                || message.DisplayTrigger.EventTriggerTypeInt == ScenarioLostTriggerInt))
            return false;

        _controller = controller;
        HeldMessages.Add(message);
        VRLog.Info("Tutorial", $"VR step HOLD: scripted message '{message.MessageName}' "
            + $"({message.LayoutType}) is withheld at LevelEventsController.{ShowMethodName} — the "
            + "tutorial waits here until the player has taken a figure into their hand. Nothing is "
            + "dropped: it is re-issued through the game's own show path on release "
            + $"(withheld: {HeldMessages.Count}).");
        return true;
    }

    /// <summary>
    /// ENGAGE — called by <see cref="TutorialGrabStep"/> the moment its step becomes pending, i.e.
    /// from inside the very <c>MessageWasDismissed</c> that <c>ProcessEvent</c> raises BEFORE it
    /// walks <c>m_MessagesToShow</c>. That ordering is what lets the same event's follow-up
    /// message be caught (see the class doc). Returns false when the patch degraded.
    /// </summary>
    internal static bool Engage(string why)
    {
        if (!Available)
        {
            VRLog.Warn("Tutorial", "VR step sequencing unavailable — the scripted-message hold "
                + $"point could not be patched. {why} will be shown WITHOUT holding the chain back.");
            return false;
        }
        if (_engaged)
            return true;
        _engaged = true;
        VRLog.Info("Tutorial", $"VR step HOLD ENGAGED — {why}. Every scripted level message the "
            + "tutorial tries to show from now on is withheld (the scenario-won/lost message "
            + "excepted) until the step is satisfied or a failsafe fires.");
        return true;
    }

    /// <summary>
    /// RELEASE — hand the withheld messages back to the game, in arrival order, through the same
    /// entry point they were taken from. The reason is logged with their names so the next
    /// hardware log proves the sequencing end to end.
    /// </summary>
    internal static void Release(string reason)
    {
        if (!_engaged)
            return;
        _engaged = false;

        LevelEventsController? controller = _controller;
        _controller = null;
        if (HeldMessages.Count == 0)
        {
            VRLog.Info("Tutorial", $"VR step HOLD RELEASED — {reason}. Nothing had been withheld; "
                + "the scripted chain never asked for a window while the step was pending.");
            return;
        }

        var replay = HeldMessages.ToArray();
        HeldMessages.Clear();
        string names = string.Join(", ", Array.ConvertAll(replay, m => $"'{m.MessageName}'"));

        // The level the messages belong to must still be the live one — otherwise they are stale
        // and re-issuing them would inject a dead level's windows into whatever runs now.
        bool levelAlive = controller != null && LevelEventsController.s_EventsControllerActive;
        if (!levelAlive || _showLevelMessage == null)
        {
            VRLog.Warn("Tutorial", $"VR step HOLD RELEASED — {reason} — but the level that queued "
                + $"{names} is gone (the scenario ended), so they are dropped with it. The scripted "
                + "chain of the CURRENT context is untouched.");
            return;
        }

        VRLog.Info("Tutorial", $"VR step HOLD RELEASED — {reason}. Re-issuing {names} through the "
            + $"game's own LevelEventsController.{ShowMethodName}, in the order it asked for them.");
        _replaying = true;
        try
        {
            foreach (CLevelMessage message in replay)
                _showLevelMessage.Invoke(controller, new object[] { message });
        }
        catch (Exception e)
        {
            // Nothing left to fall back on: the messages are already out of our hands and the
            // game's own path threw. Log loudly and degrade so we never hold anything again.
            Degrade($"replay threw: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            _replaying = false;
        }
    }

    /// <summary>
    /// DISCARD — a NEW scenario started while messages were withheld. They belong to the previous
    /// level and must not be replayed into this one; the previous level is over, so dropping them
    /// changes nothing that still exists.
    /// </summary>
    internal static void Discard(string reason)
    {
        if (!_engaged && HeldMessages.Count == 0)
            return;
        int n = HeldMessages.Count;
        FailOpen();
        VRLog.Warn("Tutorial", $"VR step HOLD DISCARDED — {reason}. {n} withheld message(s) "
            + "belonged to the previous level and were dropped with it.");
    }

    /// <summary>Drop the hold state without replaying (used by the error paths and by
    /// <see cref="Discard"/>). Never touches the game.</summary>
    private static void FailOpen()
    {
        _engaged = false;
        _controller = null;
        HeldMessages.Clear();
    }

    /// <summary>Log the first failure and thereafter stay silent; returns null for TargetMethod.</summary>
    private static MethodBase? Degrade(string reason)
    {
        if (!_degraded)
        {
            _degraded = true;
            VRLog.Warn("Tutorial", $"scripted-message hold disabled — {reason}. The tutorial's "
                + "message chain is left vanilla (the extra VR step then runs unsequenced).");
        }
        return null;
    }
}
