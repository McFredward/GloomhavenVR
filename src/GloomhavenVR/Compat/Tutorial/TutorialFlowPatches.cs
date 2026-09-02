using System;
using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary.CustomLevels;

namespace GloomhavenVR.Compat;

/// <summary>
/// TUTORIAL FLOW DIAGNOSTICS — read-only postfixes that make the tutorial's scripted
/// hint chain VISIBLE in the hardware log (tutorial contexts only, gated by
/// <see cref="TutorialVR.IsTutorialActive"/>).
///
/// WHY: the tutorial's step graph lives in a serialized <c>CCustomLevelData</c> blob
/// inside the game's data package (BinaryFormatter <c>.lvldat</c>, CSRLYML.cs:590) — it
/// is NOT readable from the repo, so which trigger each hint waits on can only be proven
/// on the machine that has the game data. These dumps convert the next tutorial run into
/// that proof: the flow dump at scenario start lists every message with its display AND
/// dismiss trigger (decoded via <see cref="TutorialVR.Describe"/>), and the show/dismiss
/// lines timestamp the chain's progress. This is the test-confidence rule in code form —
/// "read from source at runtime" instead of "inferred".
///
/// It also pins the LOCALIZATION KEYS of every tutorial hint (title + pages), which is
/// what the VR hint-text override (<see cref="TutorialHints"/>, in Tutorial/TutorialHintPatches.cs)
/// keys on — after one
/// hardware run the pattern-matched keys can be promoted to exact entries.
///
/// SAFETY: postfixes only, no game state touched, every body try/caught (a diagnostics
/// throw must never break the tutorial it observes), and the whole class no-ops outside
/// tutorial scenarios.
/// </summary>
[HarmonyPatch(typeof(LevelEventsController), "StartListeningForEvents")]
internal static class LevelEventsController_StartListeningForEvents_Patch
{
    private static void Postfix(LevelEventsController __instance)
    {
        try
        {
            if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive)
                return;

            // Scenario boundary for the controls phase — anything still open belongs to the level
            // that just ended (this postfix is the one point where a new scripted level begins).
            ControlsTutorial.Reset();

            var sb = new StringBuilder(2048);
            List<CLevelMessage>? msgs = __instance.m_MessagesToShow;
            int n = msgs?.Count ?? 0;
            // Does this level open with a story dialogue, and which one? The controls lesson takes
            // the game's tutorial box over at exactly the moment that dialogue is dismissed —
            // which is the display trigger of the first box the tutorial itself would open (flow
            // dump: TB_2_1 waits on LevelMessageDismissed ctxId='TB_1'). Read from the
            // controller's own queue, in queue order and by NAME: the tutorial carries a SECOND
            // StoryDialog at the very end (TB_25), so "any story dialogue" would also match the
            // closing one, and waiting for a dialogue this level never queues is how a lesson
            // silently never runs.
            string? openingDialog = null;
            for (int i = 0; i < n; i++)
                if (msgs![i] != null
                    && msgs[i].LayoutType == CLevelMessage.ELevelMessageLayoutType.StoryDialog)
                {
                    openingDialog = msgs[i].MessageName;
                    break;
                }
            // Queued rather than started: the hands, the asset bundle and the message handler all
            // come up over the first second of a scenario.
            ControlsTutorial.RequestForTutorial(openingDialog);
            sb.Append($"Tutorial flow dump — {n} scripted message(s) queued "
                + "(display trigger ⇒ shows the hint; dismiss trigger ⇒ closes it):");
            for (int i = 0; i < n; i++)
            {
                CLevelMessage? m = msgs![i];
                if (m == null)
                    continue;
                sb.Append($"\n  [{i}] '{m.MessageName}' layout={m.LayoutType}"
                    + $" titleKey='{m.TitleKey}'");
                if (m.Pages != null && m.Pages.Count > 0)
                {
                    sb.Append(" pageKeys=[");
                    for (int p = 0; p < m.Pages.Count; p++)
                        sb.Append((p > 0 ? ", '" : "'") + m.Pages[p]?.PageTextKey + "'");
                    sb.Append(']');
                }
                sb.Append($"\n       display: {TutorialVR.Describe(m.DisplayTrigger)}"
                    + $" | dismiss: {TutorialVR.Describe(m.DismissTrigger)}");
            }
            // Scenario boundary — drop the previous scenario's recorded card references BEFORE
            // the sweep below re-records this one's.
            TutorialCardNames.Reset();
            List<CLevelEvent>? evs = __instance.m_LevelEventsToShow;
            for (int i = 0; i < (evs?.Count ?? 0); i++)
            {
                CLevelEvent? e = evs![i];
                if (e == null)
                    continue;
                sb.Append($"\n  event[{i}] {e.EventType} repeats={e.Repeats}"
                    + (string.IsNullOrEmpty(e.EventResource) ? "" : $" resource='{e.EventResource}'")
                    + $" display: {TutorialVR.Describe(e.DisplayTrigger)}");
                // Some level events NAME a card the VR hints must be able to talk about (the
                // forced short-rest burn). Recording it here needs no extra patch — this dump
                // already walks the queue — and it must happen before the game consumes its own
                // copy (LevelEventsController.RunActionIfShortRestDataPending).
                TutorialCardNames.NoteLevelEvent(e);
            }
            VRLog.Info("Tutorial", sb.ToString());
            TutorialVR.InvalidateWaitCache();
            // Scenario boundary — the mod-owned extra VR step is a once-per-scenario bonus.
            TutorialGrabStep.Reset();
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"flow dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>Timestamps each hint the tutorial shows (name + the trigger that will release
/// it) and re-arms the camera-step scan — a newly shown message may itself be waiting on
/// the camera event to DISMISS.</summary>
[HarmonyPatch(typeof(LevelEventsController), "MessageWasDisplayed")]
internal static class LevelEventsController_MessageWasDisplayed_Patch
{
    private static void Postfix(CLevelMessage messageDisplayed)
    {
        try
        {
            if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive || messageDisplayed == null)
                return;
            TutorialVR.InvalidateWaitCache();
            VRLog.Info("Tutorial", $"hint SHOWN '{messageDisplayed.MessageName}' "
                + $"({messageDisplayed.LayoutType}) — dismiss: "
                + TutorialVR.Describe(messageDisplayed.DismissTrigger));
        }
        catch (Exception)
        {
            // diagnostics only — never let a log line break the tutorial
        }
    }
}

/// <summary>Timestamps each dismissal and re-arms the camera-step scan — the NEXT
/// message's display trigger becomes the active wait the moment this one closes.</summary>
[HarmonyPatch(typeof(LevelEventsController), "MessageWasDismissed")]
internal static class LevelEventsController_MessageWasDismissed_Patch
{
    private static void Postfix(CLevelMessage messageDismissed)
    {
        try
        {
            if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive || messageDismissed == null)
                return;
            TutorialVR.InvalidateWaitCache();
            VRLog.Info("Tutorial", $"hint DISMISSED '{messageDismissed.MessageName}' — "
                + "the next pending display trigger is now the active wait.");
            // THE CONTROLS LESSON'S SLOT. FIRST, because the dismissal it waits for is the
            // tutorial's opening STORY DIALOGUE and it engages the chain hold from inside this
            // very call — before ProcessEvent gets to walk m_MessagesToShow for the follow-up
            // box. It runs at most once per scenario and is inert for every other dismissal.
            ControlsTutorial.NoteMessageDismissed(messageDismissed);
            // ARM the mod-owned follow-up step (no separate Harmony patch needed: this postfix
            // already sees every dismissal). It only records the moment — TutorialGrabStep.Tick
            // does the showing once the handover has settled and the strip is provably free.
            TutorialGrabStep.NoteDismissed(messageDismissed);
        }
        catch (Exception)
        {
            // diagnostics only
        }
    }
}
