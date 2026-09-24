using System;
using System.Collections.Generic;
using System.Globalization;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;

namespace GloomhavenVR.Net;

/// <summary>
/// Native message edges, including synchronous A-finished/B-open transitions that a frame
/// poll cannot see. Records 19/21 continue to carry the original window pose; record 83 owns
/// pages and completion, addressed to the native message occurrence on each participant.
/// </summary>
internal static class MapStoryLifecycle
{
    private static MapStoryOpeningLedger _ledger = new();
    private static readonly List<int> Peers = new();
    private static int _localPlayerId;
    private static WeakReference? _failedSubject;
    private static float _retryAfter;
    private static MapStoryController? Controller => Singleton<MapStoryController>.IsInitialized
        ? Singleton<MapStoryController>.Instance : null;
    private static StoryController? ScenarioController => Singleton<StoryController>.IsInitialized
        ? Singleton<StoryController>.Instance : null;
    private static bool Active => SharedWindows.ParticipatesHere(SharedWindowKind.ScenarioStory);
    private static bool MapActive => SharedWindows.ParticipatesHere(SharedWindowKind.MapStory);
    internal static bool SendDue => Active && _ledger.Changed;

    internal static bool OwnsCurrent => Controller?.m_CurrentMessage != null
        && _ledger.Owns(Controller.m_CurrentMessage);

    internal static void PoseOpening(in PresenceState presence, uint contentKey, out uint epoch, out uint token)
    {
        epoch = token = 0;
        MapStoryOpening[]? openings = presence.HasMapStoryLifecycle ? presence.MapStoryLifecycleEntries : null;
        // Sample places the current native opening first, ahead of rotating history.
        // Capture from the SAME presence as the legacy pose, never from a later packet.
        if (openings == null || openings.Length == 0 || openings[0].Finished
            || openings[0].ContentKey != contentKey) return;
        epoch = openings[0].Epoch;
        token = openings[0].Token;
    }

    internal static bool MatchesPose(bool map, int sender, uint epoch, uint token)
    {
        StoryController.DialogInfo? message = map ? Controller?.m_CurrentMessage : ScenarioController?.m_CurrentMessage;
        return message != null && _ledger.MatchesPose(message, sender, _localPlayerId, epoch, token);
    }

    private static IReadOnlyList<int> Participants()
    {
        VersionGuard.CollectContinuationPeers(Peers, _localPlayerId);
        return Peers;
    }

    internal static void NativeOpened(MapStoryController controller, MapStoryController.MapDialogInfo message)
    {
        if (!MapActive || message == null || !ReferenceEquals(controller, Controller)) return;
        uint content = RemoteStorySync.HashDialog(message.DialogPages);
        int count = message.DialogPages?.Count ?? 0;
        if (content == 0 || count == 0 || count > byte.MaxValue) return;
        uint semantic = SharedMapRunIdentity.Add(SharedMapRunIdentity.Key, "map-story");
        // controller.messageTrigger is overwritten when a successor is merely queued.
        // It is not provenance of this message, especially when VR joins an existing box.
        semantic = SharedMapRunIdentity.Add(semantic, message.MapMsg?.ID);
        semantic = SharedMapRunIdentity.Add(semantic, content.ToString("X8", CultureInfo.InvariantCulture));
        // Wealth messages intentionally reuse the same text. Their public prosperity level
        // identifies which unlock this occurrence announces, without translated text or RNG.
        if (message.DialogPages![0]?.text == "GUI_WEALTH_LEVEL_UNLOCKES_ITEMS" && AdventureState.MapState != null)
            semantic = SharedMapRunIdentity.Add(semantic,
                CMapParty.CalculateProsperityLevel(AdventureState.MapState.MapParty.ProsperityXP)
                    .ToString(CultureInfo.InvariantCulture));
        _ledger.Open(message, semantic == 0 ? 1u : semantic, content, (byte)count, Participants());
    }

    internal static void NativeOpened(StoryController controller, StoryController.DialogInfo message)
    {
        if (!Active || message == null || !ReferenceEquals(controller, ScenarioController)) return;
        uint content = RemoteStorySync.HashDialog(message.DialogPages);
        int count = message.DialogPages?.Count ?? 0;
        if (content == 0 || count == 0 || count > byte.MaxValue) return;
        uint semantic = SharedMapRunIdentity.Add(SharedMapRunIdentity.Key, "scenario-story");
        semantic = SharedMapRunIdentity.Add(semantic, message.LevelMsg?.MessageName);
        semantic = SharedMapRunIdentity.Add(semantic, content.ToString("X8", CultureInfo.InvariantCulture));
        _ledger.Open(message, semantic == 0 ? 1u : semantic, content, (byte)count, Participants());
    }

    internal static void NativeFinished(StoryController.DialogInfo? message)
    {
        // The success postfix retains the exact old object even when OnFinishShow has
        // already opened its successor. A thrown native callback is not a completion.
        if (message != null && _ledger.Owns(message))
        {
            _ledger.Update(message, (message.DialogPages?.Count ?? 1) - 1, Participants());
            if (_ledger.Finish(message))
                VRLog.Note("Net", $"STORY CONTINUATION COMPLETED: content=0x{RemoteStorySync.HashDialog(message.DialogPages):X8}, " +
                    $"pages={message.DialogPages?.Count ?? 0}; native finish callback returned successfully.");
        }
    }

    private static void Refresh()
    {
        if (!Active) return;
        MapStoryController? controller = Controller;
        if (MapActive && controller?.m_CurrentMessage is MapStoryController.MapDialogInfo message)
        {
            if (!_ledger.Owns(message)) NativeOpened(controller, message); // VR enabled after native Show.
            _ledger.Update(message, controller.dialogBox?.currentDialogIndex ?? -1, Participants());
        }
        StoryController? scenario = ScenarioController;
        if (scenario?.window != null && scenario.window.IsOpen && scenario.m_CurrentMessage != null)
        {
            if (!_ledger.Owns(scenario.m_CurrentMessage)) NativeOpened(scenario, scenario.m_CurrentMessage);
            _ledger.Update(scenario.m_CurrentMessage, scenario.dialogBox?.currentDialogIndex ?? -1, Participants());
        }
    }

    internal static void Sample(ref PresenceState presence, int localPlayerId)
    {
        _localPlayerId = localPlayerId;
        if (!Active) return;
        Refresh();
        object? current = MapActive ? Controller?.m_CurrentMessage : null;
        current ??= ScenarioController?.window?.IsOpen == true ? ScenarioController.m_CurrentMessage : null;
        MapStoryOpening[] snapshot = _ledger.Sample(current);
        presence.HasMapStoryLifecycle = snapshot.Length != 0;
        presence.MapStoryLifecycleEntries = snapshot;
    }

    internal static void Observe(int sender, in PresenceState presence, int localPlayerId)
    {
        _localPlayerId = localPlayerId;
        if (sender <= 0 || sender == localPlayerId || !presence.HasMapStoryLifecycle
            || presence.MapStoryLifecycleEntries == null) return;
        _ledger.Observe(sender, presence.MapStoryLifecycleEntries);
    }

    internal static void Resolve()
    {
        if (!Active || _localPlayerId <= 0) return;
        Refresh();
        MapStoryController? controller = Controller;
        if (MapActive && controller?.window != null)
            ResolveBox(controller.m_CurrentMessage, controller.dialogBox, controller.window, true);
        StoryController? scenario = ScenarioController;
        if (scenario?.window != null && !StoryController.DisplayDelayInEffect)
            ResolveBox(scenario.m_CurrentMessage, scenario.dialogBox, scenario.window, false);
    }

    private static void ResolveBox(StoryController.DialogInfo? message, UICharacterStoryBox? box,
        UnityEngine.UI.UIWindow window, bool map)
    {
        if (message == null || box == null) return;
        if (ReferenceEquals(_failedSubject?.Target, message) && UnityEngine.Time.unscaledTime < _retryAfter) return;
        int target = _ledger.Resolve(message, _localPlayerId, box.currentDialogIndex, window.IsOpen);
        if (target < 0) return;
        try { box.ShowLine(target); }
        catch (Exception error)
        {
            StoryController.DialogInfo? current = map ? Controller?.m_CurrentMessage : ScenarioController?.m_CurrentMessage;
            bool sameOpening = ReferenceEquals(current, message) && window.IsOpen;
            if (sameOpening) _ledger.RetryTerminal(message);
            if (!ReferenceEquals(_failedSubject?.Target, message))
                VRLog.Warn("Net", $"STORY CONTINUATION FAILED: native page {target}, sameOpening={sameOpening}; "
                    + $"{error.GetType().Name}: {error.Message}");
            _failedSubject = new WeakReference(message);
            _retryAfter = UnityEngine.Time.unscaledTime + 1f;
            return;
        }
        VRLog.Debug("Net", $"MAP STORY LIFECYCLE: applied native page {target}; opening provenance matched.");
    }

    internal static void Reset()
    {
        _ledger = new MapStoryOpeningLedger();
        _localPlayerId = 0;
        Peers.Clear();
        _failedSubject = null;
        _retryAfter = 0f;
    }

    internal static void ForgetPeer(int playerId) => _ledger.ForgetPeer(playerId);
}

[HarmonyPatch(typeof(StoryController), "ShowImmediately")]
internal static class StoryController_ShowImmediately_LifecyclePatch
{
    private static void Postfix(StoryController __instance, StoryController.DialogInfo message)
        => Desync.DispatchGuard.Run("StoryLifecycle.NativeOpened", () => MapStoryLifecycle.NativeOpened(__instance, message));
}

[HarmonyPatch(typeof(StoryController), "OnFinishShow")]
internal static class StoryController_OnFinishShow_LifecyclePatch
{
    private static void Prefix(StoryController __instance, out StoryController.DialogInfo? __state)
        => __state = __instance.m_CurrentMessage;
    private static void Postfix(StoryController.DialogInfo? __state)
        => Desync.DispatchGuard.Run("StoryLifecycle.NativeFinished", () => MapStoryLifecycle.NativeFinished(__state));
}

[HarmonyPatch(typeof(MapStoryController), "ShowImmediately")]
internal static class MapStoryController_ShowImmediately_LifecyclePatch
{
    private static void Postfix(MapStoryController __instance, MapStoryController.MapDialogInfo message)
        => Desync.DispatchGuard.Run("StoryLifecycle.NativeOpened", () => MapStoryLifecycle.NativeOpened(__instance, message));
}

[HarmonyPatch(typeof(MapStoryController), "OnFinishShow")]
internal static class MapStoryController_OnFinishShow_LifecyclePatch
{
    private static void Prefix(MapStoryController __instance, out StoryController.DialogInfo? __state)
        => __state = __instance.m_CurrentMessage;
    private static void Postfix(StoryController.DialogInfo? __state)
        => Desync.DispatchGuard.Run("StoryLifecycle.NativeFinished", () => MapStoryLifecycle.NativeFinished(__state));
}
