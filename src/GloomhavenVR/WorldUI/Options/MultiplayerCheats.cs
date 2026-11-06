using System;
using System.IO;
using System.Linq;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;
using MapRuleLibrary.State;
using MapRuleLibrary.YML.Quest;
using ScenarioRuleLibrary;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Host-only test commands run inside the ORIGINAL reliable action queue, not the
/// cosmetic side channel. Native GameActionEvent compresses ActionTypeID to7bits;
/// native0..121 leaves127 available in this game version. Recheck the real roster
/// and pending joins at EXECUTION, immediately before native FinishProcessingAction
/// forwards synchronously. A failed host admission marks the original action invalid,
/// so it neither mutates state nor reaches an unmodded/late-joining peer.
/// </summary>
internal static class MultiplayerCheats
{
    internal const int NativeAction = 127;
    private static ulong _nextSequence, _appliedSequence, _gateRoster;
    private static int _epoch = -1, _gateEpoch = -1;
    private static WeakReference<CMapState>? _map, _gateMap;
    private static WeakReference<ScenarioState>? _scenario;
    private static MultiplayerCheatCommand? _pending;
    private static NativeCheatQueue.Cohort? _pendingCohort, _gateCohort;

    internal static void NoteVersionReceipt(int sender, ushort build) => NativeCheatQueue.NoteVersionReceipt(sender, build);

    internal static string UnavailableReason()
    {
        ResetContext();
        if (!VROptionsTab.CheatsEnabledNow) return "cheat_mp_disabled";
        if (!FFSNetwork.IsOnline) return "";
        if (!FFSNetwork.IsHost) return "cheat_mp_host";
        if (NetSession.FlatNetMode) return "cheat_mp_peers";
        if (NativeAction > 127 || Enum.IsDefined(typeof(GameActionType), NativeAction)) return "cheat_mp_version";
        if (!TryRoster(out _)) return "cheat_mp_peers";
        if (FFSNetwork.HasDesynchronized || _pending != null || !ActionProcessor.ReadyToProcessNextAction || ActionProcessor.IsProcessing
            || ActionProcessor.CurrentState.StateType != ActionProcessorStateType.ProcessFreely
            || ActionProcessor.ActionQueue.Count != 0 || SceneController.Instance == null
            || SceneController.Instance.IsLoading || SceneController.Instance.ScenarioIsLoading
            || GameState.WaitingForPlayerToSelectDamageResponse || GameState.WaitingForPlayerActorToAvoidDamageResponse
            || (Singleton<StoryController>.IsInitialized && Singleton<StoryController>.Instance.IsVisible)
            || NativeRuleOutputBusy())
            return "cheat_mp_busy";
        return "";
    }

    internal static bool TryQueue(MultiplayerCheatKind kind, string character = "", bool partyGold = false, bool enabled = false, string characterName = "")
    {
        if (!FFSNetwork.IsOnline || UnavailableReason().Length != 0 || AdventureState.MapState == null || !TryRoster(out ulong roster)) return false;
        var map = AdventureState.MapState;
        var command = new MultiplayerCheatCommand { Kind = kind, Sequence = ++_nextSequence, Roster = roster,
            MapSeed = map.Seed, Mode = (int)map.DLLMode, Character = character, CharacterName = characterName, PartyGold = partyGold, Enabled = enabled };
        if (kind == MultiplayerCheatKind.Reveal)
        {
            ScenarioState? scenario = ScenarioManager.CurrentScenarioState;
            if (scenario == null) return false;
            command.Scenario = scenario.ID; command.ScenarioSeed = scenario.Seed;
        }
        if (!CanApply(command)) return false;
        byte[] bytes = command.Write();
        if (kind == MultiplayerCheatKind.Unlock)
        {
            // Storage is optional test infrastructure, not part of the ordered
            // model commit. An unexpected backup failure must precede enqueue.
            try { VRLog.Info("WorldUI", "MULTIPLAYER CHEAT: unlock backup: " + VROptionsTab.BackUpAdventureSave()); }
            catch (Exception ex) { VRLog.Warn("WorldUI", "MULTIPLAYER CHEAT: backup failed before queue: " + ex.Message); return false; }
        }
        _pendingCohort = NativeCheatQueue.CaptureCohort();
        if (_pendingCohort == null) return false;
        _pending = command;
        try
        {
            // TargetNONE deliberately avoids trapping an obsolete cheat at a phase
            // deadline. Exact map/scenario identity is checked before actual execution.
            NativeCheatQueue.Enqueue(bytes);
            Report("MULTIPLAYER CHEAT: queued " + kind + " through the native action processor.");
            return true;
        }
        catch { _pending = null; throw; }
    }

    internal static bool Execute(GameAction action)
    {
        ResetContext();
        if (!FFSNetwork.IsOnline || FFSNetwork.HasDesynchronized || action.PlayerID != PlayerRegistry.HostPlayerID
            || !MultiplayerCheatCommand.TryRead(action.CustomBinaryData, out var command)) return false;
        if (FFSNetwork.IsHost)
        {
            // Only an action originated by this exact host click can mutate here;
            // a client cannot manufacture a command by claiming PlayerID1.
            bool admitted = !FFSNetwork.HasDesynchronized && VROptionsTab.CheatsEnabledNow && !NetSession.FlatNetMode && _pending != null
                && command!.Sequence == _pending.Sequence && action.ExecuteLocallyFirst
                && NativeCheatQueue.CohortUnchanged(_pendingCohort)
                && SameBytes(action.CustomBinaryData, _pending.Write())
                && TryRoster(out ulong roster) && roster == command.Roster && CanApply(command)
                && (command.Kind is not (MultiplayerCheatKind.Gold or MultiplayerCheatKind.Unlock) || SaveData.Instance != null);
            _pending = null;
            if (!admitted) { Report("MULTIPLAYER CHEAT: rejected before native forwarding; session or permission changed."); return false; }
        }
        else if (command!.Sequence <= _appliedSequence) return true;
        else if (!CanApply(command))
            // An accepted host action arriving at a genuinely different native
            // world is not a harmless private refusal. Let the ORIGINAL processor
            // report the desync rather than silently continue with different state.
            throw new InvalidOperationException("Accepted host cheat does not match the current native world.");
        if (command!.Sequence <= _appliedSequence) return true;
        Apply(command);
        _appliedSequence = command.Sequence;
        Report("MULTIPLAYER CHEAT: applied " + command.Kind + " through the native action processor.");
        return true;
    }

    /// <summary>Wait at the original pre-dequeue seam, like native ConfirmAction.
    /// This never sets processingAction, clears queues or fabricates a phase.</summary>
    internal static bool WaitForReveal(GameAction action)
    {
        if (!FFSNetwork.IsOnline || action.ActionTypeID != NativeAction || action.PlayerID != PlayerRegistry.HostPlayerID
            || !MultiplayerCheatCommand.TryRead(action.CustomBinaryData, out var command)
            || command!.Kind != MultiplayerCheatKind.Reveal || !CanApply(command)) return false;
        if (FFSNetwork.IsHost && (!VROptionsTab.CheatsEnabledNow || NetSession.FlatNetMode
            || _pending == null || !SameBytes(action.CustomBinaryData, _pending.Write())
            || !TryRoster(out ulong roster) || roster != command.Roster)) return false;
        return NativeRuleOutputBusy();
    }

    internal static bool IsCanonicalHostAction(GameAction action) => action.ActionTypeID == NativeAction
        && !Enum.IsDefined(typeof(GameActionType), NativeAction) && action.PlayerID == PlayerRegistry.HostPlayerID
        && MultiplayerCheatCommand.TryRead(action.CustomBinaryData, out _);

    internal static bool IsOwnedPendingHostAction(GameAction action) => FFSNetwork.IsHost
        && action.ExecuteLocallyFirst && IsCanonicalHostAction(action) && _pending != null
        && SameBytes(action.CustomBinaryData, _pending.Write());

    internal static void RetireUnforwarded(GameAction action)
    {
        // ExecuteLocallyFirst is never populated by the received native event
        // constructor, so this cannot withdraw an accepted receiver commit.
        if (!IsOwnedPendingHostAction(action)) return;
        if (_pending != null && MultiplayerCheatCommand.TryRead(action.CustomBinaryData, out var command)
            && command!.Sequence == _pending.Sequence) { _pending = null; _pendingCohort = null; }
        Report("MULTIPLAYER CHEAT: cancelled before native dequeue because the action permit changed.");
    }

    private static bool NativeRuleOutputBusy() =>
        // Match the game's pre-dequeue ConfirmAction exemption: a damage response
        // must remain able to drain, rather than wait behind its own queued choice.
        (ScenarioRuleClient.IsProcessingOrMessagesQueued && !GameState.WaitingForPlayerToSelectDamageResponse
            && !GameState.WaitingForPlayerActorToAvoidDamageResponse)
        || (Choreographer.s_Choreographer != null && Choreographer.s_Choreographer.isActiveAndEnabled
            && Choreographer.s_Choreographer.m_MessageQueue.Count > 0);

    internal static bool SharedRequirementsIdentityCurrent
    {
        get
        {
            ResetContext();
            return _gateMap != null && Matches(_gateMap, AdventureState.MapState)
                && _gateEpoch == VersionGuard.SessionEpoch && RosterFingerprint(false) == _gateRoster
                && NativeCheatQueue.CohortUnchanged(_gateCohort, requireStable: false);
        }
    }
    internal static bool SharedRequirementsAllowed => SharedRequirementsIdentityCurrent
        && TryRoster(out ulong roster) && roster == _gateRoster;

    private static void ResetContext()
    {
        CMapState? map = AdventureState.MapState; ScenarioState? scenario = ScenarioManager.CurrentScenarioState;
        if (_epoch == VersionGuard.SessionEpoch && Matches(_map, map) && Matches(_scenario, scenario)) return;
        _epoch = VersionGuard.SessionEpoch;
        _map = map == null ? null : new WeakReference<CMapState>(map);
        _scenario = scenario == null ? null : new WeakReference<ScenarioState>(scenario);
        _pending = null; _pendingCohort = null; _appliedSequence = 0;
    }
    private static bool Matches<T>(WeakReference<T>? reference, T? current) where T : class =>
        reference == null ? current == null : reference.TryGetTarget(out T? prior) && ReferenceEquals(prior, current);

    private static void Report(string message)
    {
        try { VRLog.Note("WorldUI", message); }
        catch { /* A diagnostic sink must never split native forwarding. */ }
    }
    private static bool SameBytes(byte[] a, byte[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

    private static bool TryRoster(out ulong signature)
    {
        signature = 0;
        if (!NativeCheatQueue.ReadRoster(out int[] players, out int local)) return false;
        foreach (int player in players)
            if (player != local && !NativeCheatQueue.HasCurrentVersion(player)) return false;
        signature = RosterFingerprint(); return signature != 0;
    }
    private static ulong RosterFingerprint(bool requireStable = true)
    {
        if (!NativeCheatQueue.ReadRoster(out int[] players, out _, requireStable)) return 0;
        ulong hash = 14695981039346656037UL;
        foreach (int id in players)
        { hash ^= unchecked((uint)id); hash *= 1099511628211UL; }
        return hash;
    }

    private static bool CanApply(MultiplayerCheatCommand command)
    {
        CMapState? map = AdventureState.MapState;
        if (map?.MapParty == null || map.Seed != command.MapSeed || (int)map.DLLMode != command.Mode) return false;
        if (command.Kind == MultiplayerCheatKind.Reveal)
        {
            var scenario = ScenarioManager.CurrentScenarioState;
            return scenario != null && scenario.ID == command.Scenario && scenario.Seed == command.ScenarioSeed
                && ScenarioManager.Scenario != null
                && ScenarioManager.Scenario.CurrentScenarioResult == SEventActorFinishedScenario.EScenarioResult.None;
        }
        // Native map phase is authoritative, independently of 2D/3D presentation
        // and a stale scenario object retained during a scene transition.
        if (ActionProcessor.CurrentPhase != ActionPhaseType.MapHQ
            && ActionProcessor.CurrentPhase != ActionPhaseType.MapAtLinkedScenario) return false;
        if (command.Kind == MultiplayerCheatKind.Gold)
            return command.PartyGold == (map.GoldMode == EGoldMode.PartyGold)
                && map.MapParty.CheckCharacters.Count(c => c.CharacterID == command.Character && c.CharacterName == command.CharacterName) == 1;
        return command.Kind == MultiplayerCheatKind.Unlock || command.Kind == MultiplayerCheatKind.Requirements;
    }

    private static void Apply(MultiplayerCheatCommand command)
    {
        CMapState map = AdventureState.MapState;
        switch (command.Kind)
        {
            case MultiplayerCheatKind.Gold:
                if (command.PartyGold) map.MapParty.ModifyPartyGold(100, useGoldModifier: false);
                else map.MapParty.CheckCharacters.Single(c => c.CharacterID == command.Character && c.CharacterName == command.CharacterName).ModifyGold(100, useGoldModifier: false);
                break;
            case MultiplayerCheatKind.Unlock:
                foreach (var quest in map.AllQuests.ToList())
                {
                    if (quest?.Quest == null || quest.Quest.Type == EQuestType.Job) continue;
                    if (quest.QuestState == MapRuleLibrary.MapState.CQuestState.EQuestState.Blocked) quest.LockQuest();
                    quest.UnlockQuest();
                }
                // This is a presentation rebuild, not DebugShowAllScenarios:
                // the latter leaves a private requirements-bypass flag enabled.
                try
                {
                    if (Singleton<MapChoreographer>.IsInitialized)
                        Singleton<MapChoreographer>.Instance.RefreshAllMapLocations(null, save: false);
                }
                catch (Exception ex) { Report("MULTIPLAYER CHEAT: original map refresh failed after unlock: " + ex.Message); }
                break;
            case MultiplayerCheatKind.Reveal: DebugMenu.RevealAllRooms(); break;
            case MultiplayerCheatKind.Requirements:
                Patches.ScenarioGateCheat.SetFromSharedHost(command.Enabled);
                _gateMap = new WeakReference<CMapState>(map); _gateEpoch = VersionGuard.SessionEpoch; _gateRoster = command.Roster; _gateCohort = NativeCheatQueue.CaptureCohort(requireStable: false);
                break;
            default: throw new InvalidDataException("Unknown cheat operation.");
        }
        // Only the session's real save owner writes the campaign file. Receiving
        // clients apply the same native model APIs without writing private saves.
        if (FFSNetwork.IsHost && command.Kind is MultiplayerCheatKind.Gold or MultiplayerCheatKind.Unlock)
        {
            try { SaveData.Instance.SaveCurrentAdventureData(); }
            catch (Exception ex)
            {
                // Do not stop native forwarding after an admitted model change.
                // The original save method already reports ordinary IO failures;
                // this also covers a thrown plugin/storage callback at its boundary.
                try { VRLog.Warn("WorldUI", "MULTIPLAYER CHEAT: save failed after the shared model commit: " + ex.Message); }
                catch { /* Diagnostic failure cannot split the ordered commit. */ }
            }
        }
    }
}
