using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary.CustomLevels;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// THE CAMERA INTRODUCTION, SKIPPED RATHER THAN RE-TEXTED (user ruling 2026-09-03, verbatim:
/// <i>"Der Textblock 'Du hast gerade alles selbst gemacht … hier ist nichts zu tun' ergibt keinen
/// Sinn — entfernen; das Tutorial soll nahtlos weitergehen."</i>).
///
/// <para>WHAT THE BOXES ARE. The tutorial's camera segment is two scripted BOX messages —
/// <c>TB_2_2</c> (the flat keyboard/WASD page, <c>TUTORIAL_2_TEXT_002_2</c>) and <c>TB_3</c> (the
/// camera box, <c>TUTORIAL_2_TEXT_003</c>) — that the <see cref="ControlsTutorial"/> lesson now runs
/// in FRONT of, and teaches better than, with the player's own hands. Until this class they were
/// still shown, carrying a "you have just done all of this — nothing to do here" line, because the
/// scripted chain needs them DISMISSED: <c>TB_4</c>'s display trigger is
/// <c>LevelMessageDismissed ctxId='TB_3'</c>, and dropping them from the queue would strand the
/// tutorial at the camera step for good.</para>
///
/// <para>HOW THEY ARE SKIPPED WITHOUT WRITING GAME STATE. Each box has its own Continue button,
/// and that button's handler is a public method — <c>LevelMessageUILayout.CloseButtonPressed()</c>
/// (LevelMessageUILayout.cs:183). It is what the button's onClick and the UI_SUBMIT key both call.
/// This class calls that same method for the player the moment the box has been laid out, so
/// everything the player's click would do happens in the player's click's order: the pages are
/// paged through (LevelMessageUILayout.cs:196 — one call per page), the <c>LevelMessage</c>
/// navigation popup state that <c>Init</c> entered is left again (:200-204), the controller input
/// area is torn down, and the handler's own close delegate runs
/// (<c>HideCurrentlyShownBoxMessage</c> → <c>MessageWasDismissed</c> → the game's own
/// <c>LevelMessageDismissed</c> UIEvent, LevelMessagesUIHandler.cs:153-161 /
/// LevelEventsController.cs:979-981). No trigger store is touched and no event is fabricated: the
/// event the chain moves on is the one the box's own dismissal posts.</para>
///
/// <para>WHY THE HOOK IS <c>LevelMessageUILayoutGroup.Show</c> AND NOT <c>MessageWasDisplayed</c>.
/// The handler's display coroutine raises <c>MessageWasDisplayed</c> FIRST and lays the window
/// out SECOND (LevelMessagesUIHandler.cs:201-202). Pressing Continue from the earlier point would
/// dismiss the message and then watch the coroutine open the window on it anyway — a window
/// showing a message the handler no longer tracks, whose button would close whatever came NEXT.
/// <c>Show</c> is the last statement of that coroutine: at its postfix the layout has run
/// <c>Init</c> (button active, popup state entered, pages built) and the window has been asked to
/// open, so the press lands on exactly the state a player's press would find. It also lands
/// BEFORE the first rendered frame: <c>UIWindow.Show</c> only starts a fade from alpha 0, and
/// <c>Hide</c> in the same frame turns it round before anything is drawn.</para>
///
/// <para>THE ONE WAY THE SYNCHRONOUS PRESS CAN BE REFUSED, AND THE RETRY THAT COVERS IT.
/// <c>CloseButtonPressed</c> ignores a press within two frames of its gamepad input area gaining
/// focus (LevelMessageUILayout.cs:190-194, "Skip close button"), and <c>Init</c> enables that area
/// on the same call. Whether that focus lands synchronously depends on the input mode, so the
/// press is verified — did the handler stop reporting this message? — and if not, retried from
/// <see cref="Tick"/> every other frame for up to <see cref="RetryCeilingSeconds"/>. If it never
/// takes, the box is left standing with its Continue button and the VR movement text
/// (<c>tut_vr_move_body</c>, which is what those pages resolve to), and a Warn says so.</para>
///
/// <para>ONLY WHEN THE LESSON ACTUALLY RAN. The boxes are skipped BECAUSE the lesson has just
/// taught their content; a player who switched the lesson off ([Compat] ControlsLesson = false),
/// or whose lesson was abandoned before it opened, has been taught nothing and gets the boxes
/// with the VR movement text instead — the pre-2026-09-03 behaviour. The gate is
/// <see cref="ControlsTutorial.RanThisScenario"/>, set only once the lesson's card is open.</para>
///
/// <para>NEVER a message whose dismissal ends the level: <c>m_ActionForNextMessageDismissal</c>
/// is checked first, the way <see cref="ControlsBox.Show"/> checks it. Never a message that is
/// not dismissed by its own button (<c>IsTriggeredByDismiss</c> false — a box waiting on a game
/// event has no Continue to press, and the mod does not invent one). Never outside a live
/// tutorial. Any throw disarms the class for the session and leaves the box exactly as the game
/// showed it — the failure mode is one extra click for the player, never a stuck chain.</para>
/// </summary>
internal static class TutorialCameraSkip
{
    /// <summary>How long the retry may keep pressing before the box is left to the player.</summary>
    private const float RetryCeilingSeconds = 3f;

    /// <summary>Frames between two presses — the layout's own focus guard is "fewer than 2 frames
    /// since focus", so every other frame is the shortest cadence that can ever get past it.</summary>
    private const int RetryEveryFrames = 2;

    private static CLevelMessage? _pending;
    private static LevelMessageUILayoutGroup? _pendingGroup;
    private static float _pendingSince;
    private static int _lastPressFrame = -1;
    private static int _presses;
    private static bool _disabledByError;

    /// <summary>
    /// A box message has just been laid out and its window asked to open (postfix of
    /// <c>LevelMessageUILayoutGroup.Show</c>). Decide whether it is a camera box the lesson has
    /// covered, and if so press its Continue for the player.
    /// </summary>
    internal static void OnBoxShown(LevelMessageUILayoutGroup group, CLevelMessage? message)
    {
        if (_disabledByError || message == null)
            return;
        try
        {
            if (!ShouldSkip(group, message, out string why))
                return;
            _pending = message;
            _pendingGroup = group;
            _pendingSince = Time.unscaledTime;
            _presses = 0;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("Tutorial", $"Tutorial camera box '{message.MessageName}' "
                + $"(pages {DescribePages(message)}) is being SKIPPED for the player — {why}. Its "
                + "own Continue button is pressed (LevelMessageUILayout.CloseButtonPressed, the "
                + "same path a click takes), so the scripted chain moves on through the box's "
                + "own LevelMessageDismissed event.");
            Press("on display");
        }
        catch (Exception ex)
        {
            Disarm(ex);
        }
    }

    /// <summary>Per frame (from <see cref="ControlsTutorial.Tick"/>): retry a press that was
    /// refused, or notice that the box went away by other means. Cold path: one static read.</summary>
    internal static void Tick()
    {
        if (_pending == null || _disabledByError)
            return;
        try
        {
            LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
            if (handler == null || !ReferenceEquals(handler.CurrentlyDisplayedBoxMessage, _pending))
            {
                // Dismissed — by our press on an earlier frame, or by the player beating us to
                // the button. Either way the chain has moved on.
                Settle("the handler no longer shows it");
                return;
            }
            if (Time.unscaledTime - _pendingSince > RetryCeilingSeconds)
            {
                VRLog.Warn("Tutorial", $"Tutorial camera box '{_pending.MessageName}' could NOT be "
                    + $"skipped: {_presses} Continue press(es) over {RetryCeilingSeconds:0} s and the "
                    + "handler still shows it (its focus guard refused every one, or the layout "
                    + "could not be found). The box is left standing with its own Continue button "
                    + "and the VR movement text — one extra click for the player.");
                Clear();
                return;
            }
            if (Time.frameCount - _lastPressFrame < RetryEveryFrames)
                return;
            Press("retry");
        }
        catch (Exception ex)
        {
            Disarm(ex);
        }
    }

    /// <summary>Scenario boundary — a pending press belongs to the level that just ended.</summary>
    internal static void Reset() => Clear();

    private static bool ShouldSkip(LevelMessageUILayoutGroup group, CLevelMessage message, out string why)
    {
        why = string.Empty;
        if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive)
            return false;
        if (!ControlsTutorial.RanThisScenario)
            return false;
        if (!TutorialHints.IsCameraBox(message))
            return false;
        // A box with no Continue button of its own waits on a game event; there is nothing here to
        // press and the mod does not invent a dismissal. Its pages still carry the VR movement
        // text, and TutorialVR's locomotion bridge completes a camera-event wait.
        if (message.DismissTrigger == null || !message.DismissTrigger.IsTriggeredByDismiss)
            return false;
        if (message.LayoutType == CLevelMessage.ELevelMessageLayoutType.HelpText
            || message.LayoutType == CLevelMessage.ELevelMessageLayoutType.StoryDialog)
            return false;
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        LevelEventsController? controller = LevelEventsController.s_Instance;
        if (handler == null || controller == null)
            return false;
        // Only the BOX window, showing THIS message — the help-text strip has its own group.
        if (!ReferenceEquals(group, handler.LevelMessageBoxLayoutGroup)
            || !ReferenceEquals(handler.CurrentlyDisplayedBoxMessage, message))
            return false;
        // Dismissing a message while this one-shot is armed ENDS the level
        // (LevelEventsController.cs:929). Never on the mod's initiative.
        if (controller.m_ActionForNextMessageDismissal != null)
            return false;
        why = "the VR controls lesson ran in front of it this scenario and taught, with the "
            + "player's own hands, what its flat camera/keyboard text explains";
        return true;
    }

    /// <summary>
    /// Press the box's own Continue — once per page plus one, stopping the moment the handler
    /// stops reporting the message. Bounded by the page count, because each press before the
    /// last page only turns a page (LevelMessageUILayout.cs:196-199).
    /// </summary>
    private static void Press(string when)
    {
        CLevelMessage? message = _pending;
        LevelMessageUILayoutGroup? group = _pendingGroup;
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (message == null || group == null || handler == null)
        {
            Clear();
            return;
        }
        _lastPressFrame = Time.frameCount;
        LevelMessageUILayout? layout = FindLayout(group, message.LayoutType);
        if (layout == null)
        {
            VRLog.Warn("Tutorial", $"Tutorial camera box '{message.MessageName}': the box group has "
                + $"no layout for {message.LayoutType}, so its Continue cannot be pressed. The box "
                + "is left to the player.");
            Clear();
            return;
        }
        int pages = message.Pages != null ? message.Pages.Count : 0;
        int budget = pages + 1;
        int pressed = 0;
        for (int i = 0; i < budget; i++)
        {
            if (!ReferenceEquals(handler.CurrentlyDisplayedBoxMessage, message))
                break;
            layout.CloseButtonPressed();
            pressed++;
        }
        _presses += pressed;
        bool dismissed = !ReferenceEquals(handler.CurrentlyDisplayedBoxMessage, message);
        VRLog.Info("Tutorial", $"Tutorial camera box '{message.MessageName}': Continue pressed "
            + $"{pressed}x ({when}, {pages} page(s)) — "
            + (dismissed ? "dismissed." : "still displayed; will retry."));
        if (dismissed)
            Settle($"{_presses} Continue press(es) took it down");
    }

    private static void Settle(string how)
    {
        string name = _pending?.MessageName ?? "?";
        Clear();
        VRLog.Info("Tutorial", $"Tutorial camera box '{name}' is gone — {how}.");
    }

    private static LevelMessageUILayout? FindLayout(LevelMessageUILayoutGroup group,
        CLevelMessage.ELevelMessageLayoutType type)
    {
        List<LevelMessageUILayoutGroup.UILevelMessagePrefabTuple>? layouts = group.LevelMessageLayouts;
        if (layouts == null)
            return null;
        for (int i = 0; i < layouts.Count; i++)
            if (layouts[i].type == type && layouts[i].uiMessage != null)
                return layouts[i].uiMessage;
        return null;
    }

    private static string DescribePages(CLevelMessage message)
    {
        if (message.Pages == null || message.Pages.Count == 0)
            return "none";
        var parts = new string[message.Pages.Count];
        for (int i = 0; i < parts.Length; i++)
            parts[i] = "'" + (message.Pages[i]?.PageTextKey ?? "?") + "'";
        return string.Join(", ", parts);
    }

    private static void Clear()
    {
        _pending = null;
        _pendingGroup = null;
        _pendingSince = 0f;
        _lastPressFrame = -1;
        _presses = 0;
    }

    private static void Disarm(Exception ex)
    {
        _disabledByError = true;
        string name = _pending?.MessageName ?? "?";
        Clear();
        VRLog.Error("Tutorial", "The tutorial camera-box skip threw and is disabled for this session "
            + $"(box '{name}' is left to the player's own Continue): {ex.GetType().Name}: "
            + $"{ex.Message}\n{ex.StackTrace}");
    }
}

/// <summary>The window has just been laid out on a message and asked to open — the last statement
/// of the handler's display coroutine (LevelMessagesUIHandler.cs:202). See
/// <see cref="TutorialCameraSkip"/> for why this point and not <c>MessageWasDisplayed</c>.</summary>
[HarmonyPatch(typeof(LevelMessageUILayoutGroup), "Show")]
internal static class LevelMessageUILayoutGroup_Show_Patch
{
    private static void Postfix(LevelMessageUILayoutGroup __instance, CLevelMessage message)
        => TutorialCameraSkip.OnBoxShown(__instance, message);
}
