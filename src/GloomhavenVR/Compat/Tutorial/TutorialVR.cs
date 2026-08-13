using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.CustomLevels;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// TUTORIAL VR BRIDGE — completes the tutorial's camera-familiarization step from VR
/// locomotion (mission: the tutorial must be playable END TO END in VR).
///
/// ROOT CAUSE (verified in the decompiled sources, 2026-08):
/// The tutorial is a scripted custom level (<c>TutorialService.StartTutorial</c> →
/// <c>CCustomLevelData</c>) whose hint chain is driven by <c>LevelEventsController</c>:
/// every <c>CLevelMessage</c> displays/dismisses on a <c>CLevelTrigger</c> that matches
/// either engine SEvents or client <c>UIEvent</c>s (LevelEventsController.cs:425-528).
/// Auditing EVERY producer of every <c>UIEvent.EUIEventType</c> shows exactly ONE event
/// the VR mod can never produce:
///
///   <c>CameraRoomButtonPressed</c> — its sole producer is
///   <c>RoomCameraButton.OnClick()</c> (RoomCameraButton.cs:51-55), the flat HUD's
///   room-camera button. In VR the game camera is parked (P1 prefix-skips on
///   <c>CameraController</c>, see <see cref="Rig.CameraController_LateUpdate_Patch"/>) and
///   world-grab locomotion replaces all camera input, so the player never presses it —
///   the tutorial waits forever. This matches the hardware log
///   (.planning/debug/tutorial/LogOutput.log:989): the camera hint box closed and no
///   trigger ever fired again.
///
/// Every OTHER UIEvent producer is reachable through seams the mod already drives —
/// card selection (<c>CardsHandUI.SelectCard</c>/<c>FullAbilityCard.OnAbilityClick</c>,
/// CardsGameApi), confirm/undo/skip/short-rest (docked REAL HUD buttons), tile clicks
/// (<c>Choreographer.TileHandler</c>), and all engine-driven events (RoomRevealed,
/// props, objectives, phase SEvents) which are input-agnostic.
///
/// FIX: when (a) a tutorial scenario is active AND (b) some pending tutorial trigger is
/// actually WAITING on <c>CameraRoomButtonPressed</c> AND (c) the player performs real VR
/// locomotion (world-grab drag / rotate / zoom, snap or smooth turn — reported by
/// <see cref="Rig.WorldGrab"/> / <see cref="Rig.SnapTurn"/> via
/// <see cref="NotifyLocomotion"/>), post the game's OWN completion event:
/// <c>UIEventManager.LogUIEvent(new UIEvent(CameraRoomButtonPressed))</c> — byte-for-byte
/// what <c>RoomCameraButton.OnClick</c> does first. The synthetic event carries the same
/// phase/actor context a real click would (UIEvent ctor reads the live PhaseManager /
/// GameState), so <c>ShouldEventCauseTrigger</c> treats it identically.
///
/// SAFETY / SCOPE:
/// - Zero game-data changes; one additive client event. UNCONDITIONAL since the 2026-08-13
///   user ruling (the [Compat] TutorialVRAdapt kill-switch is gone: its OFF was the vanilla
///   deadlock); scope is the runtime tutorial gate below, not a config flag.
/// - Zero wire traffic: <c>UIEventManager.OnEventLogged</c> has exactly ONE subscriber,
///   <c>LevelEventsController.UIEventLogged</c> (verified repo-wide grep) — UIEvents never
///   serialize to Photon/FFSNet.
/// - MP-safe by triple gate: tutorial game modes are single-player, and
///   <see cref="IsTutorialActive"/> additionally refuses when <c>FFSNetwork.IsOnline</c>.
/// - Posting is demand-driven: we SCAN the controller's own pending trigger lists
///   (publicized <c>m_MessagesToShow</c> etc.) for a camera-event trigger and post only
///   while one exists — outside the tutorial, and in every non-camera tutorial step,
///   locomotion posts nothing at all.
/// - TickGuard-spirit isolation: Notify runs inside WorldGrab/SnapTurn Update; any throw
///   here disarms the bridge permanently for the session (logged once with stack) so a
///   bug can never starve locomotion or flood the log.
/// </summary>
internal static class TutorialVR
{
    // How much REAL locomotion counts as "got familiar with the camera". Deliberately
    // small-but-deliberate: an accidental stick brush stays below all three, a single
    // intentional drag/turn/zoom crosses one. Meters are REAL meters (WorldGrab reports
    // applied motion divided by rig scale), degrees are world-yaw, octaves are log2 scale.
    private const float DragMetersToComplete = 0.30f;
    private const float TurnDegreesToComplete = 15f;
    private const float ScaleOctavesToComplete = 0.10f;

    /// <summary>Pending-trigger rescan interval — the scan walks short lists (a tutorial
    /// has tens of messages), but locomotion notifies every frame, so cache it.</summary>
    private const float WaitRecheckSeconds = 0.5f;

    /// <summary>Min delay between two synthetic posts — a long continuous drag must not
    /// machine-gun events while a played-message prerequisite still holds a trigger back.</summary>
    private const float PostCooldownSeconds = 1.5f;

    private static bool _disabledByError;
    private static float _nextWaitCheck;   // Time.unscaledTime of the next pending-trigger rescan
    private static bool _cameraTriggerWaiting;
    private static float _cooldownUntil;
    private static float _accMeters, _accDegrees, _accOctaves;
    private static int _posts;

    /// <summary>Master gate: the bridge has not tripped its error latch. (The [Compat]
    /// TutorialVRAdapt config half is GONE — user ruling 2026-08-13: its OFF restored the
    /// vanilla camera-step DEADLOCK this bridge exists to break, so it could only strand the
    /// tutorial. The error latch, which disarms on a real fault, is the only gate left.)</summary>
    internal static bool Enabled => !_disabledByError;

    /// <summary>
    /// A tutorial scenario is the LOCAL, OFFLINE context this whole feature is scoped to.
    /// Mirrors the game's own checks (Choreographer.cs:3531 and :12911):
    /// front-end tutorial mode, the guildmaster adventure tutorial, or a map scenario
    /// flagged tutorial/intro. Null-safe over the whole SaveData chain (scene
    /// transitions); refuses online sessions outright (tutorials are single-player —
    /// this is a documented guarantee, not a reachable branch).
    /// </summary>
    internal static bool IsTutorialActive
    {
        get
        {
            try
            {
                if (FFSNetwork.IsOnline)
                    return false;
                GlobalData? g = SaveData.Instance?.Global;
                if (g == null)
                    return false;
                if (g.GameMode == EGameMode.FrontEndTutorial)
                    return true;
                if (g.GameMode == EGameMode.Guildmaster
                    && g.AdventureData?.AdventureMapState?.IsPlayingTutorial == true)
                    return true;
                return g.CurrentAdventureData?.AdventureMapState?.CurrentMapScenarioState
                          ?.IsTutorialOrIntroScenario == true;
            }
            catch (Exception)
            {
                return false; // mid-teardown SaveData — never the tutorial
            }
        }
    }

    /// <summary>Force the next Notify to rescan the pending triggers (called whenever a
    /// tutorial message is shown or dismissed — the wait set only changes there).</summary>
    internal static void InvalidateWaitCache() => _nextWaitCheck = 0f;

    /// <summary>
    /// Locomotion report from the rig (real meters dragged, degrees yawed, octaves of
    /// scale change APPLIED this frame). Called per frame during an active world grab and
    /// on every stick turn — the cold path must stay two static reads.
    /// </summary>
    internal static void NotifyLocomotion(float meters, float degrees, float octaves)
    {
        if (_disabledByError || !LevelEventsController.s_EventsControllerActive)
            return; // cold path: outside scripted levels this is two static reads
        try
        {
            NotifyCore(meters, degrees, octaves);
        }
        catch (Exception ex)
        {
            // TickGuard-spirit: disarm forever, log ONCE with the stack. The tutorial
            // bridge failing must never take world-grab locomotion down with it.
            _disabledByError = true;
            VRLog.Error("Tutorial", "VR camera-step bridge threw and is disabled for this "
                + $"session: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void NotifyCore(float meters, float degrees, float octaves)
    {
        float now = Time.unscaledTime;
        if (now >= _nextWaitCheck)
        {
            _nextWaitCheck = now + WaitRecheckSeconds;
            bool waiting = IsTutorialActive && ScanForCameraTrigger();
            if (waiting != _cameraTriggerWaiting)
            {
                _cameraTriggerWaiting = waiting;
                _accMeters = _accDegrees = _accOctaves = 0f;
                if (waiting)
                    VRLog.Info("Tutorial", "A tutorial trigger is WAITING on the flat room-camera "
                        + "button (CameraRoomButtonPressed) — unreachable in VR; VR locomotion "
                        + $"(drag ≥ {DragMetersToComplete:0.00} m / turn ≥ {TurnDegreesToComplete:0}° "
                        + $"/ zoom ≥ {ScaleOctavesToComplete:0.00} oct) will complete it.");
            }
        }
        if (!_cameraTriggerWaiting || now < _cooldownUntil)
            return;

        _accMeters += Mathf.Abs(meters);
        _accDegrees += Mathf.Abs(degrees);
        _accOctaves += Mathf.Abs(octaves);
        if (_accMeters < DragMetersToComplete
            && _accDegrees < TurnDegreesToComplete
            && _accOctaves < ScaleOctavesToComplete)
            return;

        string reason = _accMeters >= DragMetersToComplete ? $"drag {_accMeters:0.00} m"
            : _accDegrees >= TurnDegreesToComplete ? $"turn {_accDegrees:0}°"
            : $"zoom {_accOctaves:0.00} oct";
        _accMeters = _accDegrees = _accOctaves = 0f;
        _cooldownUntil = now + PostCooldownSeconds;
        _posts++;

        // The game's own seam: identical to RoomCameraButton.OnClick()'s first statement
        // (RoomCameraButton.cs:53). We deliberately do NOT call OnClick itself — its second
        // half moves the parked scenario camera (FocusOnRoom → SetFocalPointGameObject),
        // which in VR would silently re-target the rig/panel anchor point.
        UIEventManager.LogUIEvent(new UIEvent(UIEvent.EUIEventType.CameraRoomButtonPressed));
        InvalidateWaitCache();

        // First post per session at Info (the hardware-log proof line), repeats at Debug —
        // repeats only happen while a played-message prerequisite still gates the trigger.
        string line = $"VR locomotion ({reason}) completed the camera step — posted the game's "
            + $"own CameraRoomButtonPressed UIEvent (#{_posts} this session).";
        if (_posts == 1) VRLog.Info("Tutorial", line);
        else VRLog.Debug("Tutorial", line);
    }

    /// <summary>
    /// Does ANY pending tutorial trigger wait on <c>CameraRoomButtonPressed</c>?
    /// Checks the same four trigger stores <c>LevelEventsController.ProcessEvent</c>
    /// matches events against (LevelEventsController.cs:439-523): the currently shown
    /// box/help message's DISMISS trigger, the pending messages' DISPLAY triggers, the
    /// pending level events, and the custom win/lose objectives. All fields are
    /// publicized game internals — read-only access, no state is touched.
    /// </summary>
    private static bool ScanForCameraTrigger()
    {
        LevelEventsController? lec = LevelEventsController.s_Instance;
        if (lec == null)
            return false;

        LevelMessagesUIHandler? ui = LevelMessagesUIHandler.s_Instance;
        if (ui != null
            && (WaitsOnCamera(ui.CurrentlyDisplayedBoxMessage?.DismissTrigger)
                || WaitsOnCamera(ui.CurrentlyDisplayedHelpTextMessage?.DismissTrigger)))
            return true;

        List<CLevelMessage>? msgs = lec.m_MessagesToShow;
        if (msgs != null)
            for (int i = 0; i < msgs.Count; i++)
                if (msgs[i] != null && WaitsOnCamera(msgs[i].DisplayTrigger))
                    return true;

        List<CLevelEvent>? evs = lec.m_LevelEventsToShow;
        if (evs != null)
            for (int i = 0; i < evs.Count; i++)
                if (evs[i] != null && WaitsOnCamera(evs[i].DisplayTrigger))
                    return true;

        List<Tuple<CObjective_CustomTrigger, int>>? win = lec.m_CustomWinObjectivesToTrigger;
        if (win != null)
            for (int i = 0; i < win.Count; i++)
                if (win[i]?.Item1 != null && WaitsOnCamera(win[i].Item1.CustomTrigger))
                    return true;

        List<Tuple<CObjective_CustomTrigger, int>>? lose = lec.m_CustomLoseObjectivesToTrigger;
        if (lose != null)
            for (int i = 0; i < lose.Count; i++)
                if (lose[i]?.Item1 != null && WaitsOnCamera(lose[i].Item1.CustomTrigger))
                    return true;

        return false;
    }

    private static bool WaitsOnCamera(CLevelTrigger? t) =>
        t != null && t.IsUIEventTypeTrigger && !t.IsTriggeredByDismiss
        && t.EventTriggerTypeInt == (int)UIEvent.EUIEventType.CameraRoomButtonPressed;

    // ---- diagnostics ------------------------------------------------------------------------

    /// <summary>
    /// One-line description of a tutorial trigger for the flow dump — names the UIEvent /
    /// SEvent it waits for plus the context filters ShouldEventCauseTrigger applies
    /// (LevelEventsController.cs:632-771). Raw ints are kept alongside the enum name so a
    /// hardware log stays decodable even if an enum mapping is off.
    /// </summary>
    internal static string Describe(CLevelTrigger? t)
    {
        if (t == null)
            return "none";
        try
        {
            string s;
            if (t.IsTriggeredByDismiss)
                s = "dismiss-button";
            else if (t.IsUIEventTypeTrigger)
                s = $"UIEvent {(UIEvent.EUIEventType)t.EventTriggerTypeInt}({t.EventTriggerTypeInt})";
            else
                s = $"SEvent {(ScenarioRuleLibrary.ESEType)t.EventTriggerTypeInt}({t.EventTriggerTypeInt})"
                    + $" sub={t.EventTriggerSubTypeInt} ctxType={t.EventTriggerContextTypeInt}"
                    + $" ctxSub={t.EventTriggerContextSubTypeInt}";
            if (!string.IsNullOrEmpty(t.EventTriggerContextId))
                s += $" ctxId='{t.EventTriggerContextId}'";
            if (!string.IsNullOrEmpty(t.EventTriggerActorName))
                s += $" actor='{t.EventTriggerActorName}'";
            if (t.EventTriggerRound != 0)
                s += $" round={t.EventTriggerRound}";
            if (t.IsUIEventTypeTrigger && t.EventTriggerContextTypeInt != 9)
                s += $" phase={t.EventTriggerContextTypeInt}";
            if (!string.IsNullOrEmpty(t.EventTriggerPlayedMessageReq))
                s += $" needsPlayed='{t.EventTriggerPlayedMessageReq}'";
            if (!string.IsNullOrEmpty(t.EventTriggerNotPlayedMessageReq))
                s += $" needsNotPlayed='{t.EventTriggerNotPlayedMessageReq}'";
            return s;
        }
        catch (Exception ex)
        {
            return $"<describe failed: {ex.GetType().Name}>";
        }
    }
}
