using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// MP WALL-FADE SYNC — the wall-fade driver's side of wire record 17 (user request
/// 2026-08-07: "Optional (schaltbar): die Wall-Fades der Mitspieler sollen synchronisiert
/// werden"). Two halves:
///
/// SENDER (<see cref="WallSegmentFade.SampleFadedWallKeys"/>): the set of segments whose
/// fade DECISION is currently ON (gate columns included; doorway segments never reach
/// State and arch pieces are not segments, so the never-fades set can never ride), as
/// cross-machine stable keys. KEY DERIVATION (documented on
/// <c>NetProtocol.ExtIdWallFades</c>): FNV-1a-32 over
/// <c>"{anchorName}|{roomLabel}|{qx}|{qz}"</c> — the anchor name is
/// generation-deterministic ('Wall 2') or carries a replicated GUID
/// ('ThickDoor : (guid)'), the room label is the round-4 CMap identity (replicated
/// MapGuid), and qx/qz quantize the ANCHOR TRANSFORM's world XZ to 0.5 wu (the scenario
/// world sits at identical coordinates on every client — the mod moves only the VR rig;
/// the anchor transform is used instead of the segment AABB because the AABB grows with
/// stacked adoption and regen churn). Keys are recomputed each rescan and cached per
/// segment.
///
/// RECEIVER (<see cref="WallSegmentFade.SetPeerFadedWalls"/> + the decision-loop
/// composition in Tick): each peer's set is stored with a last-seen time; the effective
/// fade target of a segment becomes max(local decision, any live peer set containing its
/// key) — composed at the TARGET so the same ramp (tau 0.12s), the same delivery
/// (native MPB / toggle-native / body / stacked / corner / gate) and the same
/// restore-on-unfade run, making a synced fade visually identical to a local one.
/// Dwell-free by design: the deciding peer already dwelled. Gated by the receiver's
/// <c>[WallFade] SyncPeerFades</c>; a key that resolves to no local segment is silently
/// ignored (never a wrong wall). A packet WITHOUT the record empties that peer's set
/// (the peer really has no faded walls); packet GAPS produce no call at all and are
/// bridged by <see cref="FadeDriver.PeerFadeLingerSeconds"/> so loss cannot flicker a
/// wall back. Doorway segments are exempt from remote fades too — they never fade
/// anywhere, on any machine.
///
/// MULTIPLAYER CONTRACT: the record itself is broadcast by NetAvatarDriver regardless of
/// the local toggle (receiver-side setting; documented choice — no renegotiation when one
/// player toggles mid-session). Nothing here writes game state; peers' visuals change
/// only through the same local-rendering paths every other fade uses.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>Cross-machine stable wire key (record 17), recomputed each rescan;
        /// 0 = unkeyable (anchor dead) — never sampled, never matched.</summary>
        public uint WireKey;
        /// <summary>Last remote-fade contribution (edge logging only).</summary>
        public bool RemoteFadeActive;
    }

    /// <summary>Sender API for <c>NetAvatarDriver</c>: fill <paramref name="dest"/> with the
    /// keys of every wall whose fade decision is currently ON, sorted ascending
    /// (deterministic wire bytes). Returns the count (≤ dest.Length); logs (throttled) when
    /// the cap truncates. Safe before install / after teardown (returns 0).</summary>
    internal static int SampleFadedWallKeys(uint[] dest)
    {
        FadeDriver? driver = _driver;
        return driver == null ? 0 : driver.SampleFadedKeys(dest);
    }

    /// <summary>Receiver API for <c>RemoteAvatar</c>: replace <paramref name="playerId"/>'s
    /// synced wall set (null/0 = the peer has no faded walls). Safe before install.</summary>
    internal static void SetPeerFadedWalls(int playerId, uint[]? keys, int count)
    {
        _driver?.SetPeerFades(playerId, keys, count);
    }

    private sealed partial class FadeDriver
    {
        /// <summary>How long a peer's synced set stays live past its last packet. Extras
        /// ride at 5 Hz, so 1s bridges several dropped packets without letting a
        /// disconnected peer's fades linger noticeably.</summary>
        private const float PeerFadeLingerSeconds = 1.0f;

        private sealed class PeerFades
        {
            public readonly HashSet<uint> Keys = new();
            public float LastSeen;
        }

        private readonly Dictionary<int, PeerFades> _peerFades = new();
        private readonly List<uint> _keySampleScratch = new();
        private float _nextTruncateLog;

        // ---- key derivation -----------------------------------------------------------------

        /// <summary>Recompute every segment's wire key (called at the end of Rescan, after
        /// room association — the room label is part of the key).</summary>
        private void ComputeWireKeys()
        {
            foreach (Segment seg in _segments.Values)
            {
                seg.WireKey = 0;
                Component? anchor = seg.Anchor;
                if (anchor == null)
                    continue;
                string roomLabel = seg.RoomIndex >= 0 && seg.RoomIndex < _roomLabels.Count
                    ? _roomLabels[seg.RoomIndex]
                    : "?";
                Vector3 p = anchor.transform.position;
                int qx = Mathf.RoundToInt(p.x * 2f);
                int qz = Mathf.RoundToInt(p.z * 2f);
                seg.WireKey = Fnv1a32Key($"{anchor.name}|{roomLabel}|{qx}|{qz}");
            }
        }

        /// <summary>FNV-1a, 32-bit, over UTF-16 code units — deterministic across machines
        /// and runs (the <c>NetFigures.StableActorId</c> discipline; string.GetHashCode is
        /// process-randomizable). Zero is remapped so 0 keeps meaning "unkeyable".</summary>
        private static uint Fnv1a32Key(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s)
            {
                h ^= (byte)c;
                h *= 16777619u;
                h ^= (byte)(c >> 8);
                h *= 16777619u;
            }
            return h == 0 ? 1u : h;
        }

        // ---- sender -------------------------------------------------------------------------

        internal int SampleFadedKeys(uint[] dest)
        {
            _keySampleScratch.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.State || seg.DoorRoot != null || seg.WireKey == 0)
                    continue;
                _keySampleScratch.Add(seg.WireKey);
            }
            if (_keySampleScratch.Count > dest.Length)
            {
                float now = Time.unscaledTime;
                if (now >= _nextTruncateLog)
                {
                    _nextTruncateLog = now + 10f;
                    VRLog.Warn(Name,
                        $"WALL-FADE SYNC: {_keySampleScratch.Count} faded wall(s) exceed the "
                        + $"{dest.Length}-key wire cap — peers see the first {dest.Length} "
                        + "(sorted). A scene fading this many walls at once is worth a look.");
                }
            }
            _keySampleScratch.Sort();
            int n = Mathf.Min(_keySampleScratch.Count, dest.Length);
            for (int k = 0; k < n; k++)
                dest[k] = _keySampleScratch[k];
            return n;
        }

        // ---- receiver -----------------------------------------------------------------------

        internal void SetPeerFades(int playerId, uint[]? keys, int count)
        {
            if (!_peerFades.TryGetValue(playerId, out PeerFades? peer))
            {
                if (keys == null || count <= 0)
                    return; // nothing before, nothing now
                peer = new PeerFades();
                _peerFades[playerId] = peer;
            }
            peer.Keys.Clear();
            if (keys != null)
            {
                int n = Mathf.Min(count, keys.Length);
                for (int k = 0; k < n; k++)
                    peer.Keys.Add(keys[k]);
            }
            peer.LastSeen = Time.unscaledTime;
        }

        /// <summary>Does any LIVE peer set (last packet within the linger) contain this
        /// wall's key? Gated by the receiver's [WallFade] SyncPeerFades. The contributing
        /// peer id is reported for the edge log (first match wins).</summary>
        private bool RemoteWantsFade(Segment seg, float now, out int peerId)
        {
            peerId = 0;
            if (seg.WireKey == 0 || _peerFades.Count == 0 || !WallFadeTuning.SyncPeer)
                return false;
            foreach (KeyValuePair<int, PeerFades> kv in _peerFades)
            {
                if (now - kv.Value.LastSeen <= PeerFadeLingerSeconds
                    && kv.Value.Keys.Contains(seg.WireKey))
                {
                    peerId = kv.Key;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Edge log for peer-driven fades (a purely remote fade never flips
        /// seg.State, so LogStateFlip stays silent — this line is its counterpart).</summary>
        private void LogRemoteFadeEdge(Segment seg, bool remote, int peerId)
        {
            if (remote == seg.RemoteFadeActive)
                return;
            seg.RemoteFadeActive = remote;
            if (seg.State)
                return; // locally faded anyway — the local flip log covers it
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            VRLog.Info(Name, remote
                ? $"PEER-SYNC fade ON '{wall}' (player {peerId}'s wall fade, record 17 — "
                  + "same ramp/delivery as a local fade, dwell-free)"
                : $"PEER-SYNC fade OFF '{wall}' (no live peer fades it any more).");
        }
    }
}
