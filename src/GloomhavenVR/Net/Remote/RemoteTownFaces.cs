using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Only the resident authority's samples can drive the shared faces. The high-rate
/// stream never elects a second author; presence79 owns readiness and withdrawal.</summary>
internal static class RemoteTownFaces
{
    private sealed class Peer
    {
        internal TownFaceState Previous, Latest;
        internal float Received, Span;
        internal bool HasSample, HasHistory, Suspended;
        private readonly uint[] _retired = new uint[4];
        private int _retireAt;
        internal bool Retired(uint epoch)
        { for (int n = 0; n < _retired.Length; n++) if (_retired[n] == epoch && epoch != 0) return true; return false; }
        internal void Retire(uint epoch)
        {
            if (epoch == 0 || Retired(epoch)) return;
            _retired[_retireAt] = epoch; _retireAt = (_retireAt + 1) % _retired.Length;
        }
    }
    private static readonly Dictionary<int, Peer> Peers = new();
    internal static bool Newer(uint next, uint previous) => unchecked((int)(next - previous)) > 0;
    internal static void ObservePresence(int player, in TownFaceState state)
    {
        if (player <= 0 || !TownFaceCodec.Valid(in state)) return;
        if (!state.Active) { Forget(player); return; }
        if (!Peers.TryGetValue(player, out Peer? peer))
        {
            if (Peers.Count >= 8)
            {
                int oldest = 0; float oldestAt = float.PositiveInfinity;
                foreach (var pair in Peers)
                    if ((!pair.Value.HasSample || Time.unscaledTime - pair.Value.Received > NetProtocol.StaleTimeoutSeconds)
                        && pair.Value.Received < oldestAt) { oldest = pair.Key; oldestAt = pair.Value.Received; }
                if (oldest == 0) return;
                Peers.Remove(oldest);
            }
            Peers[player] = peer = new Peer { Latest = state };
        }
        else if (peer.Latest.Epoch != state.Epoch)
        {
            if (peer.Retired(state.Epoch)) return;
            peer.Retire(peer.Latest.Epoch);
            peer.Previous = default; peer.Latest = state; peer.HasSample = false; peer.HasHistory = false; peer.Span = 0f;
        }
        if (peer.HasHistory && (!Newer(state.Sequence, peer.Latest.Sequence) || state.Clock < peer.Latest.Clock)) return;
        peer.Suspended = false;
        Observe(player, in state);
    }
    internal static void Observe(int player, in TownFaceState state)
    {
        if (!state.Active || !TownFaceCodec.Valid(in state)
            || !Peers.TryGetValue(player, out Peer? peer) || peer.Suspended || peer.Latest.Epoch != state.Epoch) return;
        if (peer.HasHistory && (!Newer(state.Sequence, peer.Latest.Sequence) || state.Clock < peer.Latest.Clock)) return;
        peer.Span = peer.HasSample ? Mathf.Clamp(state.Clock - peer.Latest.Clock, 1f / 90f, .15f) : 0f;
        peer.Previous = peer.HasSample ? peer.Latest : state;
        peer.Latest = state; peer.Received = Time.unscaledTime; peer.HasSample = true; peer.HasHistory = true;
    }
    internal static bool Sample(int author, out TownFaceState state, out float elapsed)
    {
        state = default; elapsed = 0f;
        if (!Peers.TryGetValue(author, out Peer? peer) || !peer.HasSample) return false;
        elapsed = Mathf.Max(0f, Time.unscaledTime - peer.Received);
        if (elapsed > NetProtocol.StaleTimeoutSeconds) return false;
        state = peer.Latest;
        float t = peer.Span <= 0f ? 1f : Mathf.Clamp01(elapsed / peer.Span);
        for (int n = 0; n < 3; n++) state.Set(n, TownServiceFaceMotion.Interpolate(peer.Previous.At(n), state.At(n), t));
        // Expressions have their own continuous clock. Interpolation must never freeze a blink
        // or turn a short utterance into stale, indefinitely held mouth geometry after loss.
        state.Clock += elapsed;
        return true;
    }
    internal static bool TrySeed(out TownFaceState state, out int author, out float elapsed)
    {
        int selected = 0; float latest = float.NegativeInfinity;
        foreach (var pair in Peers)
            if (pair.Value.HasSample && pair.Value.Received > latest
                && Time.unscaledTime - pair.Value.Received <= NetProtocol.StaleTimeoutSeconds)
            { selected = pair.Key; latest = pair.Value.Received; }
        author = selected;
        return Sample(selected, out state, out elapsed);
    }
    internal static void Forget(int player)
    {
        // A >3s network stall also invokes Forget. Suspend without retiring a still-running
        // authority's epoch; only a newer presence may resume it. Fast packets alone cannot
        // revive it, and acceptance of a genuinely different epoch retires the old one.
        if (!Peers.TryGetValue(player, out Peer? peer)) return;
        peer.HasSample = false; peer.Suspended = true; peer.Previous = default;
    }
    internal static void Reset() => Peers.Clear();
}
