using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

internal static partial class MapStoryLifecycle
{
    private sealed class ScenarioStart
    {
        internal readonly WeakReference Subject;
        internal readonly uint Semantic, Content;
        internal readonly byte Pages;
        internal bool Finished;
        internal uint LedgerToken;
        internal ScenarioStart(StoryController.DialogInfo subject, uint semantic, uint content, byte pages)
        { Subject = new WeakReference(subject); Semantic = semantic; Content = content; Pages = pages; }
    }

    private static readonly Dictionary<uint, ScenarioStart> ScenarioStarts = new();
    private static WeakReference? _scenarioStartEpisode;

    private static void ClearScenarioStarts()
    {
        ScenarioStarts.Clear();
        _scenarioStartEpisode = null;
    }

    private static bool CurrentScenarioStarts()
    {
        object? episode = SaveData.Instance?.Global?.CurrentGameState == EGameState.Scenario
            ? ScenarioManager.Scenario : null;
        if (episode == null || !ReferenceEquals(_scenarioStartEpisode?.Target, episode))
        {
            ClearScenarioStarts();
            if (episode == null) return false;
            _scenarioStartEpisode = new WeakReference(episode);
        }
        return true;
    }

    private static void RememberScenarioStart(StoryController.DialogInfo subject, uint semantic, uint content, byte pages)
    {
        if (!CurrentScenarioStarts()) return;
        var trigger = subject.LevelMsg?.DisplayTrigger;
        // Exact native ScenarioStart constructor contract (CLevelTrigger): first
        // round/start-of-round SEvent, not a UI/end/room/dismiss-triggered message.
        if (trigger == null || trigger.IsUIEventTypeTrigger || trigger.IsTriggeredByDismiss
            || trigger.EventTriggerTypeInt != 2 || trigger.EventTriggerSubTypeInt != 1
            || trigger.EventTriggerContextTypeInt != 1 || trigger.EventTriggerRound != 1) return;
        if (ScenarioStarts.TryGetValue(semantic, out ScenarioStart? previous)
            && ReferenceEquals(previous.Subject.Target, subject)) return;
        ScenarioStarts[semantic] = new ScenarioStart(subject, semantic, content, pages);
    }

    private static void RememberScenarioStartFinished(StoryController.DialogInfo? subject)
    {
        if (subject == null || !CurrentScenarioStarts()) return;
        foreach (ScenarioStart start in ScenarioStarts.Values)
            if (ReferenceEquals(start.Subject.Target, subject))
                start.Finished = true;
    }

    private static void RememberScenarioStartToken(StoryController.DialogInfo? subject)
    {
        if (subject == null) return;
        foreach (ScenarioStart start in ScenarioStarts.Values)
            if (ReferenceEquals(start.Subject.Target, subject)) start.LedgerToken = _ledger.TokenFor(subject);
    }

    private static void RetainScenarioStartProvenance()
    {
        if (!CurrentScenarioStarts()) return;
        var pending = new List<uint>();
        foreach (var pair in ScenarioStarts)
        {
            // Retire every session-only capability. A still-live pending native
            // subject is only opening provenance, never completion permission.
            // Keep it through a query-triggered session edge: the exact success
            // postfix can still arrive after a synchronous successor has opened.
            pair.Value.LedgerToken = 0;
            if (!pair.Value.Finished && !pair.Value.Subject.IsAlive) pending.Add(pair.Key);
        }
        foreach (uint key in pending) ScenarioStarts.Remove(key);
    }

    private static void EnrollScenarioStartVisitor(int sender, MapStoryOpening[] requests)
    {
        // Build763 paired logs: the host read its intro OFFLINE, then hosted.
        // A late visitor replayed that native intro behind the common reveal gate;
        // its genuine StoryController lock held both card setup and DebugWin in
        // the native action queue. History cannot blindly authorize a new reader.
        // Require the current native host and the exact current player/version
        // receipt already used by ordered native actions (independent of Cheats).
        if (!CurrentScenarioStarts() || requests.Length == 0) return;
        MapStoryOpening request = requests[0]; // Sample puts the actual current native opening first.
        // Ordinary narrative traffic must not pay native player reflection for
        // this rare join-recovery path. All native authority gates still precede
        // creating a ledger occurrence or extending its recipient set.
        if (request.Finished || request.Bidirectional || !ScenarioStarts.TryGetValue(request.SemanticKey, out ScenarioStart? start)
            || !start.Finished || start.Content != request.ContentKey || start.Pages != request.PageCount
            || !_ledger.IsCurrentLiveRequest(sender, _localPlayerId, request)) return;
        if (!Active || !FFSNetwork.IsOnline || !FFSNetwork.IsHost
            || _localPlayerId <= 0 || _localPlayerId != NetPlayerActors.LocalPlayerId()
            || !NativeCheatQueue.HasCurrentVersion(sender) || !Participants().Contains(sender)) return;
        if (start.LedgerToken == 0)
        {
            // Retain only this episode's successful original start, with no
            // recipients until a reciprocal live opening proves its visitor.
            // This bounded proof object carries no native callback or scenario graph.
            _ledger.Open(start, start.Semantic, start.Content, start.Pages, Array.Empty<int>());
            _ledger.Update(start, start.Pages - 1, Array.Empty<int>());
            _ledger.Finish(start);
            start.LedgerToken = _ledger.TokenFor(start);
        }
        if (_ledger.EnrollCompletedScenarioStart(start.LedgerToken, sender, _localPlayerId, request))
            VRLog.Note("Net", $"STORY START LATE VISITOR: player={sender}, content=0x{start.Content:X8}; "
                + "current native scenario-start completion enrolled; visitor runs its own finish callback.");
    }
}
