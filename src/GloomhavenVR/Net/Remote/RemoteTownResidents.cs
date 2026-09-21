using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>One cosmetic author for all permanent residents, including between visits.
/// Joining, opting out and leaving never open another player's native service controller.</summary>
internal static class RemoteTownResidents
{
    private sealed class Peer
    { internal TownResidentsState State; internal float Received; }
    private static readonly Dictionary<int, Peer> Peers = new();

    internal static void Observe(int player, in PresenceState presence)
    {
        if (!presence.HasTownResidents || !presence.TownResidents.Active)
        { Peers.Remove(player); return; }
        if (!Peers.TryGetValue(player, out Peer? peer))
        {
            if (Peers.Count >= 8) return;
            Peers[player] = peer = new Peer();
        }
        peer.State = presence.TownResidents; peer.Received = Time.unscaledTime;
    }

    internal static bool TryAuthor(out TownResidentsState state, out float elapsed)
    {
        int local = NetPlayerActors.LocalPlayerId();
        int author = WorldUIConfig.ImmersiveTownServices.Value && local > 0 ? local : int.MaxValue;
        Peer? selected = null;
        float now = Time.unscaledTime;
        foreach (var pair in Peers)
            if (pair.Key > 0 && pair.Key < author && now - pair.Value.Received <= NetProtocol.StaleTimeoutSeconds)
            { author = pair.Key; selected = pair.Value; }
        state = selected != null ? selected.State : default;
        elapsed = selected != null ? Mathf.Max(0f, now - selected.Received) : 0f;
        return selected != null;
    }

    internal static void Sample(ref PresenceState presence)
    {
        if (!MapRoomDriver.Active) return;
        presence.HasTownResidents = true;
        presence.TownResidents = TownServicePopulation.Published;
    }

    internal static void Forget(int player) => Peers.Remove(player);
    internal static void Reset() => Peers.Clear();
}
