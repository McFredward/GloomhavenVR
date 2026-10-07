using System;
using System.Collections;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Keep the original map quest departure validation active for a VR crossplay session.
/// Native quest initialization disables this existing option. If three participants are present and
/// the unready participant leaves, two ready participants remain: the original OnPlayerLeft
/// removes the departed member but returns before its own quorum/controllable-state ACK path.
/// The full ready list then refuses an ordinary ReadyUp(false), even while the original Cancel
/// remains visible (_allPlayersReady was false before the unready departure). A subsequent ready
/// event may additionally hide the toggle. Enable the native initialization option; let the
/// complete original OnPlayerLeft and WaitForStateSyncBeforeProceeding perform the work.
/// No player list, readiness state, ACK, coroutine or game action is supplied by the mod.
/// </summary>
internal static class MapQuestDepartureValidation
{
    private static bool _installed;
    private static UIReadyToggle? _departureToggle;
    private static UIMapMultiplayerController? _departureController;
    private static string? _departureQuest;
    private static ActionPhaseType _departurePhase;
    private static int _explicitInputDepth;
    private static bool _progressAuthorized;
    private static bool _probeFailureReported;

    private static readonly PropertyInfo? PlayersReady = AccessTools.Property(typeof(UIReadyToggle), "PlayersReady");
    private static readonly Type? Registry = AccessTools.TypeByName("FFSNet.PlayerRegistry");
    private static readonly PropertyInfo? MyPlayer = Registry != null ? AccessTools.Property(Registry, "MyPlayer") : null;
    private static readonly PropertyInfo? Participants = Registry != null ? AccessTools.Property(Registry, "Participants") : null;
    private static readonly Type? Player = AccessTools.TypeByName("FFSNet.NetworkPlayer");
    private static readonly PropertyInfo? PlayerId = Player != null ? AccessTools.Property(Player, "PlayerID") : null;

    private static bool InClientQuest(UIReadyToggle toggle) => VRSession.IsRunning
        && FFSNetwork.IsOnline && FFSNetwork.IsClient && !StoryComposite.PointOfNoReturn
        && (ActionProcessor.CurrentPhase == ActionPhaseType.MapHQ
            || ActionProcessor.CurrentPhase == ActionPhaseType.MapAtLinkedScenario)
        && toggle.ReadyUpType == UIReadyToggle.EReadyUpType.Participant
        && toggle.readyUpToggleState == EReadyUpToggleStates.Quests
        && Singleton<UIReadyToggle>.IsInitialized
        && ReferenceEquals(Singleton<UIReadyToggle>.Instance, toggle);

    private static bool TryIsReady(UIReadyToggle toggle, object? player, out bool isReady)
    {
        isReady = false;
        try
        {
            if (player == null || PlayerId?.GetValue(player) is not int id
                || PlayersReady?.GetValue(toggle) is not IEnumerable ready)
                return false;
            foreach (object? entry in ready)
            {
                if (entry == null)
                    continue;
                if (PlayerId.GetValue(entry) is not int other)
                    return false;
                if (other == id)
                    isReady = true;
            }
            return true;
        }
        catch (Exception e)
        {
            // Native getters/collections can fail during teardown. No measured readiness may
            // authorize withdrawal, and a mod probe must not abort the native dispatcher.
            ProbeFailed("readiness membership", e);
            return false;
        }
    }

    private static bool FullLocalReady(UIReadyToggle toggle)
    {
        try
        {
            return PlayersReady?.GetValue(toggle) is ICollection ready
                   && Participants?.GetValue(null) is ICollection participants
                   && participants.Count > 0 && ready.Count >= participants.Count
                   && TryIsReady(toggle, MyPlayer?.GetValue(null), out bool mine) && mine;
        }
        catch (Exception e)
        {
            ProbeFailed("ready participant count", e);
            return false;
        }
    }

    private static UIMapMultiplayerController? Controller() =>
        Singleton<UIMapMultiplayerController>.IsInitialized
            ? Singleton<UIMapMultiplayerController>.Instance : null;

    private static bool CanRecordDeparture(UIReadyToggle toggle) => InClientQuest(toggle)
        && toggle.IsVisible && toggle.IsInteractable && toggle.CanBeToggled && toggle.ToggledOn
        && FullLocalReady(toggle) && Controller()?.HostSelectedQuest != null;

    private static bool MatchesDeparture(UIReadyToggle toggle) =>
        ReferenceEquals(_departureToggle, toggle) && InClientQuest(toggle)
        && ReferenceEquals(Controller(), _departureController)
        && string.Equals(Controller()?.HostSelectedQuest?.ID, _departureQuest, StringComparison.Ordinal)
        && _departureQuest != null && ActionProcessor.CurrentPhase == _departurePhase
        && FullLocalReady(toggle);

    private static void Clear()
    {
        _departureToggle = null;
        _departureController = null;
        _departureQuest = null;
        _explicitInputDepth = 0;
        _progressAuthorized = false;
    }

    private static void ProbeFailed(string seam, Exception error)
    {
        Clear();
        if (_probeFailureReported)
            return;
        _probeFailureReported = true; // bound retries even when the diagnostic logger itself fails
        try
        {
            if (error is TargetInvocationException invocation && invocation.InnerException != null)
                error = invocation.InnerException;
            VRLog.Alert("MapRoom", $"MAP QUEST DEPARTURE PROBE: {seam} failed "
                                   + $"({error.GetType().Name}: {error.Message}). Native handler retained; "
                                   + "departure Cancel authorization cleared.");
        }
        catch (Exception) { } // failure context must never replace native behavior or exceptions
    }

    internal static void Install()
    {
        if (_installed || VRSession.Harmony == null)
            return;
        _installed = true;
        VRSession.Harmony.PatchAll(typeof(InitializeSeam));
        VRSession.Harmony.PatchAll(typeof(PlayerLeftSeam));
        VRSession.Harmony.PatchAll(typeof(ExplicitInputSeam));
        VRSession.Harmony.PatchAll(typeof(ProgressEndSeam));
        VRSession.Harmony.PatchAll(typeof(CancelProgressSeam));
        VRSession.Harmony.PatchAll(typeof(ReadyUpSeam));
        VRSession.Harmony.PatchAll(typeof(ResetSeam));
    }

    [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Initialize))]
    internal static class InitializeSeam
    {
        [HarmonyPrefix]
        private static void BeforeInitialize(ref bool validateReadyUpOnPlayerLeft,
                                              UIReadyToggle.EReadyUpType readyUpType,
                                              EReadyUpToggleStates readyUpToggleState)
        {
            Clear();
            _probeFailureReported = false;
            try
            {
                if (!VRSession.IsRunning || !FFSNetwork.IsOnline
                    || readyUpType != UIReadyToggle.EReadyUpType.Participant
                    || readyUpToggleState != EReadyUpToggleStates.Quests)
                    return;
                ActionPhaseType phase = ActionProcessor.CurrentPhase;
                if (phase != ActionPhaseType.MapHQ && phase != ActionPhaseType.MapAtLinkedScenario)
                    return;
                validateReadyUpOnPlayerLeft = true;
            }
            catch (Exception e)
            {
                // Keep the caller's option unchanged if Bolt/native phase initialization fails.
                ProbeFailed("Initialize", e);
            }
        }
    }

    [HarmonyPatch(typeof(UIReadyToggle), "OnPlayerLeft")]
    internal static class PlayerLeftSeam
    {
        // Native NetworkPlayer.Detached releases its controllables BEFORE OnPlayerLeft, so this
        // seam cannot honestly classify its former participant status. Record only the measured
        // blocked state AFTER the original handler: a full current native ready roster, and the
        // genuine local Cancel visible and enabled. The handler may reveal that Cancel when a
        // ready member leaves, or keep it visible when an unready/spectator member leaves. None
        // grants permission by itself. No participant roster is copied or repaired here.
        [HarmonyPrefix]
        private static void BeforePlayerLeft(UIReadyToggle __instance, out bool __state)
        {
            __state = false;
            try { __state = InClientQuest(__instance); }
            catch (Exception e) { ProbeFailed("OnPlayerLeft prefix", e); }
        }

        [HarmonyPostfix]
        private static void AfterPlayerLeft(UIReadyToggle __instance, bool __state)
        {
            try
            {
                if (!__state || !CanRecordDeparture(__instance))
                    return;
                UIMapMultiplayerController? controller = Controller();
                string? quest = controller?.HostSelectedQuest?.ID;
                ActionPhaseType phase = ActionProcessor.CurrentPhase;
                Clear();
                _departureToggle = __instance;
                _departureController = controller;
                _departureQuest = quest;
                _departurePhase = phase;
            }
            catch (Exception e) { ProbeFailed("OnPlayerLeft postfix", e); }
        }
    }

    [HarmonyPatch(typeof(UIReadyToggle), "InputToggle")]
    internal static class ExplicitInputSeam
    {
        [HarmonyPrefix]
        private static void BeforeExplicitInput(UIReadyToggle __instance, bool isOn, out bool __state)
        {
            __state = false;
            try
            {
                __state = !isOn && MatchesDeparture(__instance)
                          && __instance.IsVisible && __instance.IsInteractable && __instance.CanBeToggled;
                if (__state)
                    _explicitInputDepth++;
            }
            catch (Exception e) { ProbeFailed("InputToggle prefix", e); }
        }

        [HarmonyFinalizer]
        private static Exception? AfterExplicitInput(UIReadyToggle __instance, bool __state, Exception? __exception)
        {
            if (__exception != null)
            {
                Clear();
                return __exception;
            }
            try
            {
                if (__state)
                {
                    _explicitInputDepth = Math.Max(0, _explicitInputDepth - 1);
                    _progressAuthorized = MatchesDeparture(__instance) && __instance.IsProgressingBar;
                }
            }
            catch (Exception e) { ProbeFailed("InputToggle finalizer", e); }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(UIReadyToggle), "OnEndAnimationProgressBar")]
    internal static class ProgressEndSeam
    {
        // A serialized native unready-progress animation can end after InputToggle returns.
        // Retain its exact input authorization until this original completion callback, then
        // expire it. Other ReadyUp callers never inherit the scope, even while progress runs.
        [HarmonyPrefix]
        private static void BeforeProgressEnd(UIReadyToggle __instance, bool isOn, out bool __state)
        {
            __state = false;
            try
            {
                __state = !isOn && _progressAuthorized && MatchesDeparture(__instance);
                _progressAuthorized = false;
                if (__state)
                    _explicitInputDepth++;
            }
            catch (Exception e) { ProbeFailed("progress completion prefix", e); }
        }

        [HarmonyFinalizer]
        private static Exception? AfterProgressEnd(bool __state, Exception? __exception)
        {
            if (__exception != null)
            {
                Clear();
                return __exception;
            }
            if (__state)
                _explicitInputDepth = Math.Max(0, _explicitInputDepth - 1);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(UIReadyToggle), "CancelProgress")]
    internal static class CancelProgressSeam
    {
        [HarmonyPrefix]
        private static void BeforeCancelProgress(UIReadyToggle __instance)
        {
            if (ReferenceEquals(__instance, _departureToggle))
                _progressAuthorized = false;
        }
    }

    [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.ReadyUp), new[] { typeof(bool), typeof(bool) })]
    internal static class ReadyUpSeam
    {
        [HarmonyPrefix]
        private static void BeforeReadyUp(UIReadyToggle __instance, bool toggledOn, ref bool autoValidateUnreadying)
        {
            string? quest;
            try
            {
                if (_departureToggle == null)
                    return;
                if (!MatchesDeparture(__instance))
                {
                    Clear();
                    return;
                }
                if (toggledOn || _explicitInputDepth == 0)
                    return;
                quest = _departureQuest;
                Clear(); // one real authorized withdrawal; never a standing full-roster bypass
            }
            catch (Exception e)
            {
                ProbeFailed("ReadyUp prefix", e);
                return;
            }
            // Mutate only after every native probe succeeded. Logging cannot prevent the
            // original native action or replace an exception thrown by that action.
            autoValidateUnreadying = true;
            try
            {
                VRLog.Note("MapRoom", "MAP QUEST DEPARTURE CANCEL: native quest withdrawal admitted after "
                                      + $"lobby departure for quest '{quest}'; native host "
                                      + "validation and continuation retained. The player pressed the "
                                      + "existing native Cancel; no automatic readiness was sent.");
            }
            catch (Exception) { }
        }
    }

    [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Reset))]
    internal static class ResetSeam
    {
        [HarmonyPostfix]
        private static void AfterReset(UIReadyToggle __instance)
        {
            _probeFailureReported = false;
            if (ReferenceEquals(__instance, _departureToggle))
                Clear();
        }
    }
}
