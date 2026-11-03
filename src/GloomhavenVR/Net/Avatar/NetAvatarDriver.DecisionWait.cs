using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.WorldUI.Surfaces;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private readonly DecisionWaitClock _decisionWait = new();
    private readonly HashSet<int> _waitingPlayers = new();
    private float _nextDecisionWait;
    private int _delayedDecisionPlayer;
    private static readonly PropertyInfo? ReadyPlayers = AccessTools.Property(typeof(UIReadyToggle), "PlayersReady");
    private static readonly System.Type? RegistryType = AccessTools.TypeByName("FFSNet.PlayerRegistry");
    private static readonly PropertyInfo? Participants = RegistryType == null ? null : AccessTools.Property(RegistryType, "Participants");
    private static readonly PropertyInfo? AllPlayers = RegistryType == null ? null : AccessTools.Property(RegistryType, "AllPlayers");

    /// <summary>Read existing native readiness and existing decision-attribution records.
    /// No new command, private card identity or network message is introduced.</summary>
    internal static int DelayedDecisionPlayer() => DelayedDecisionPlayer(Time.unscaledTime);
    private static int DelayedDecisionPlayer(float now)
    {
        NetAvatarDriver? driver = _instance;
        if (driver == null || !driver.isActiveAndEnabled || NetSession.FlatNetMode
            || !FFSNetwork.IsOnline || !driver._transport.IsOnline) return 0;
        if (now < driver._nextDecisionWait) return driver._delayedDecisionPlayer;
        driver._nextDecisionWait = now + 0.25f;
        driver._waitingPlayers.Clear();
        int context = (int)PhaseManager.PhaseType;
        try
        {
            UIReadyToggle? toggle = Singleton<UIReadyToggle>.IsInitialized ? Singleton<UIReadyToggle>.Instance : null;
            if (toggle != null && (toggle.IsVisible || toggle.ShouldBeVisible) && !toggle.IsProgressingBar
                && ReadyPlayers?.GetValue(toggle) is IList ready
                && (toggle.ReadyUpType == UIReadyToggle.EReadyUpType.Participant ? Participants : AllPlayers)
                    ?.GetValue(null) is IEnumerable participants)
            {
                foreach (object participant in participants)
                {
                    int id = NetPlayerActors.PlayerIdOf(participant);
                    if (id > 0 && !ready.Contains(participant)) driver._waitingPlayers.Add(id);
                }
                context = unchecked(context * 397 ^ toggle.GetInstanceID());
            }
            else
            {
                UseBarsSurface.SampleDecisionAttribution(out int actor, out bool pending, out _);
                if (pending && actor != 0) driver._waitingPlayers.Add(driver._transport.LocalPlayerId);
                foreach (KeyValuePair<int, RemoteAvatar> peer in driver._avatars)
                    if (peer.Value.HasDecisionAttribution && peer.Value.DecisionPending
                        && peer.Value.DecisionActorId != 0) driver._waitingPlayers.Add(peer.Key);
                // A new character's decision is a new wait even inside the same native phase.
                context = unchecked(context * 397 ^ actor);
                foreach (KeyValuePair<int, RemoteAvatar> peer in driver._avatars)
                    if (peer.Value.DecisionPending)
                        context ^= unchecked(peer.Key * 397 ^ peer.Value.DecisionActorId);
            }
        }
        catch { driver._waitingPlayers.Clear(); } // unavailable readiness is no accusation
        int candidate = 0;
        if (driver._waitingPlayers.Count == 1)
            foreach (int id in driver._waitingPlayers) candidate = id;
        driver._delayedDecisionPlayer = driver._decisionWait.Tick(candidate, context, now);
        return driver._delayedDecisionPlayer;
    }
}
