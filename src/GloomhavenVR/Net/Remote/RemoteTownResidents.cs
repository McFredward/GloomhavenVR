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
    internal static int AuthorPlayer { get; private set; }

    internal static void Observe(int player, in PresenceState presence)
    {
        if (!presence.HasTownResidents || !presence.TownResidents.Active)
        { Peers.Remove(player); RemoteTownFaces.Forget(player); return; }
        if (!Peers.TryGetValue(player, out Peer? peer))
        {
            if (Peers.Count >= 8) return;
            RemoteTownFaces.Forget(player); // A new presence lifetime must not inherit pre-join fast packets.
            Peers[player] = peer = new Peer();
        }
        if (presence.HasTownFace) RemoteTownFaces.ObservePresence(player, in presence.TownFace);
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
        AuthorPlayer = selected != null ? author : local;
        state = selected != null ? selected.State : default;
        elapsed = selected != null ? Mathf.Max(0f, now - selected.Received) : 0f;
        return selected != null;
    }

    internal static void Sample(ref PresenceState presence)
    {
        if (!MapRoomDriver.Active) return;
        presence.HasTownResidents = true;
        presence.TownResidents = TownServicePopulation.Published;
        presence.HasTownFace = TownServicePopulation.IsFaceAuthor;
        presence.TownFace = TownServicePopulation.PublishedFaces;
    }

    internal static void Forget(int player) => Peers.Remove(player);
    internal static void Reset() => Peers.Clear();
}
