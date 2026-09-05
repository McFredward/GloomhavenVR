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

        // ---- PEER FADE SET census (2026-09-05, user item 7 — fackel.jpg) ----------------------
        // The numbers behind LogPeerFadeSet. Set sizes only: the per-wall UNIT cannot differ
        // between the two sources by construction (see that method's header), so the SET is the
        // only thing that can, and it is exactly what the 24-key cap was silently shrinking.

        /// <summary>Walls the local decision offered to record 17 at the last sample, and how
        /// many of them fitted the cap. Written by <see cref="SampleFadedKeys"/>.</summary>
        private int _wireSampled;
        private int _wireSent;

        /// <summary>Per-tick, reset by <see cref="BeginPeerFadeCensus"/>: walls whose fade this
        /// tick comes from a peer (and from no local decision), the total membership of those
        /// walls, and the same pair for locally-decided walls.</summary>
        private int _peerDrivenWalls;
        private int _peerDrivenUnit;
        private int _localDrivenWalls;
        private int _localDrivenUnit;
        private int _peerWidestUnit;
        private string _peerWidestWall = "-";
        private int _localWidestUnit;
        private string _localWidestWall = "-";

        /// <summary>Walls whose LATCH WARN was withheld this tick because a live peer fade is the
        /// only thing holding them — the designed state of this feature, not a latch. See
        /// <c>WatchLatch</c>; reported here so the suppression can never go unaccounted.</summary>
        private int _peerHoldsWithoutLocalCoverage;

        /// <summary>Has ANY peer set ever arrived? The falsifier's own term: a session where no
        /// teammate ever faded a wall must not read like a session where the sets agree.</summary>
        private bool _peerFadeEverSeen;

        /// <summary>Scratch for the resolution count — built only on a printing pass.</summary>
        private readonly HashSet<uint> _peerKeyScratch = new();
        private readonly HashSet<uint> _segKeyScratch = new();

        private int _peerSetSig = int.MinValue + 1;
        private float _nextPeerSetLog;
        /// <summary>Longest silence the PEER FADE SET line may keep. Past this it prints whether
        /// or not anything moved, so "no peer ever faded a wall" and "the instrument stopped"
        /// stay distinguishable (the held-instrument lesson).</summary>
        private const float PeerSetHeartbeatSeconds = 30f;

        // ---- key derivation -----------------------------------------------------------------

        /// <summary>Recompute every segment's wire key (called at the end of Rescan, after
        /// room association — the room label is part of the key).</summary>
        private void ComputeWireKeys()
        {
            foreach (Segment seg in _live.Segments.Values)
            {
                seg.WireKey = 0;
                Component? anchor = seg.Anchor;
                if (anchor == null)
                    continue;
                string roomLabel = seg.RoomIndex >= 0 && seg.RoomIndex < _live.RoomLabels.Count
                    ? _live.RoomLabels[seg.RoomIndex]
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
            foreach (Segment seg in _live.Segments.Values)
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
                    // PROMOTED Warn -> Alert on 2026-09-05, and the tier was the reason this
                    // shipped for 160 builds. Warn is the DEBUG tier: the co-player runs a
                    // shipped build, so on HIS machine this line — the one that says his own
                    // faded walls are not reaching anybody — could never print. It fired 50
                    // times on the dev host and 131 times on the co-player's log at the one log
                    // level that happened to show it, every one of them reading 41/42/44 walls
                    // against a 24-key cap. Truncation is not a note: the walls that fall off
                    // are the highest FNV keys, an FNV key is stable for the whole scenario, so
                    // the SAME walls never fade on any peer for the whole session.
                    // HW-VERIFY
                    VRLog.Alert(Name,
                        $"WALL-FADE SYNC TRUNCATED: {_keySampleScratch.Count} faded wall(s) "
                        + $"exceed the {dest.Length}-key wire cap — peers see the first "
                        + $"{dest.Length} (sorted ascending by key), and the "
                        + $"{_keySampleScratch.Count - dest.Length} with the HIGHEST keys never "
                        + "reach any peer at all. Keys are stable for the scenario, so this is "
                        + "the same walls every packet, not a rotating sample: those walls stay "
                        + "solid on every teammate's screen no matter how long the causer looks "
                        + "behind them (user 2026-09-05, fackel.jpg). If this line is present "
                        + "the cap needs raising or the record needs paging — it is NOT a "
                        + "'busy scene' note.");
                }
            }
            _keySampleScratch.Sort();
            int n = Mathf.Min(_keySampleScratch.Count, dest.Length);
            for (int k = 0; k < n; k++)
                dest[k] = _keySampleScratch[k];
            _wireSampled = _keySampleScratch.Count;
            _wireSent = n;
            return n;
        }

        // ---- receiver -----------------------------------------------------------------------

        internal void SetPeerFades(int playerId, uint[]? keys, int count)
        {
            if (keys != null && count > 0)
                _peerFadeEverSeen = true;
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
        /// seg.State, so LogStateFlip stays silent — this line is its counterpart).
        ///
        /// <para>ModBuild 284, audited and CLEARED, same as <c>LogGateLiftEdge</c>:
        /// <c>seg.RemoteFadeActive</c> is written here and read NOWHERE else in the subsystem —
        /// it is this line's own edge latch, not a decision input. KEPT because it is the only
        /// per-wall evidence that record 17 arrived and was applied; a peer sync that quietly
        /// stopped delivering would otherwise look identical to a teammate whose walls happen
        /// not to be faded, and that distinction cannot be made from a hardware log without
        /// this line.</para></summary>
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

        // ---- PEER FADE SET census -------------------------------------------------------------

        /// <summary>Per-tick reset, called from the tick beside <c>BeginPerWallCensus</c>.</summary>
        private void BeginPeerFadeCensus()
        {
            _peerDrivenWalls = 0;
            _peerDrivenUnit = 0;
            _localDrivenWalls = 0;
            _localDrivenUnit = 0;
            _peerWidestUnit = 0;
            _peerWidestWall = "-";
            _localWidestUnit = 0;
            _localWidestWall = "-";
            _peerHoldsWithoutLocalCoverage = 0;
        }

        /// <summary>Every piece this segment would take with it if it faded: its own wall
        /// renderers plus all five dressing lanes. This is the "unit" the report speaks of, and
        /// it is a property of the SEGMENT — neither source can change it.</summary>
        private static int UnitSize(Segment seg) =>
            seg.Renderers.Count + seg.Body.Count + seg.Stacked.Count + seg.Mounted.Count
            + seg.Foliage.Count + seg.Siblings.Count + seg.UnitDressing.Count;

        /// <summary>Book one segment's contribution, called from the decision loop right after the
        /// effective target is composed. <paramref name="remoteFade"/> is the peer term of that
        /// composition; a wall the local decision ALSO fades is booked local, because that is the
        /// source whose picture the peer term could not be responsible for.</summary>
        private void NotePeerFadeUnit(Segment seg, bool remoteFade)
        {
            if (seg.DoorRoot != null)
                return;
            int unit = UnitSize(seg);
            if (seg.State)
            {
                _localDrivenWalls++;
                _localDrivenUnit += unit;
                if (unit > _localWidestUnit)
                {
                    _localWidestUnit = unit;
                    _localWidestWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                }
            }
            else if (remoteFade)
            {
                _peerDrivenWalls++;
                _peerDrivenUnit += unit;
                if (unit > _peerWidestUnit)
                {
                    _peerWidestUnit = unit;
                    _peerWidestWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                }
            }
        }

        /// <summary>
        /// PEER FADE SET (2026-09-05, user item 7: "Die Fackel in dem Szenario faded nicht mit weg
        /// wenn die Wand wegen dem Mitspieler faded … Prüfe dieses Verhalten ob es noch andere
        /// assets betreffen kann"). The answer-bearing line for the MP half of wall fading.
        ///
        /// <para>WHY IT MEASURES THE SET AND NOT THE UNIT. A peer's fade is composed at the fade
        /// TARGET — <c>target = (seg.State || remoteFade || gateLift) ? 1 : 0</c> in
        /// WallSegmentFade.cs — so from that statement onward the ramp, <c>Apply</c> and all five
        /// dressing lanes run over the SAME segment object with the same lists. A per-wall unit
        /// size therefore cannot differ between the two sources by construction, and the line
        /// prints both anyway (widest wall, and the totals) so that claim is falsifiable rather
        /// than asserted. What CAN differ, and what was actually wrong, is WHICH WALLS the peer
        /// path ever hears about: record 17 carried 24 keys while both clients were fading 41-44
        /// walls, and the sender sends the LOWEST 24 keys, so ~40 % of the causer's walls — the
        /// same ones every packet, for the whole scenario — never reached a teammate. The
        /// wall-torch sconce in fackel.jpg was one of them.</para>
        ///
        /// <para>FALSIFIER. "No peer ever faded a wall" and "the two sets now agree" must not read
        /// alike, so a session that has never received a record-17 set says so in words and its
        /// zeros are explicitly NOT a comparison. A session where the fix worked reads
        /// <c>sampled N, sent N, dropped 0</c> with <c>unresolved 0</c>. The fix is INERT if
        /// <c>dropped</c> is still non-zero (raise the cap or page the record), and the defect has
        /// MOVED rather than gone if <c>dropped 0</c> stands beside a non-zero
        /// <c>unresolved</c> — that would be key derivation disagreeing across machines, which is
        /// a different bug in the same record.</para>
        /// </summary>
        private void LogPeerFadeSet(float now)
        {
            // Live peers and the distinct keys they are holding up right now.
            _peerKeyScratch.Clear();
            int livePeers = 0;
            foreach (KeyValuePair<int, PeerFades> kv in _peerFades)
            {
                if (now - kv.Value.LastSeen > PeerFadeLingerSeconds)
                    continue;
                livePeers++;
                foreach (uint k in kv.Value.Keys)
                    _peerKeyScratch.Add(k);
            }
            int inKeys = _peerKeyScratch.Count;
            int dropped = Mathf.Max(0, _wireSampled - _wireSent);
            int sig = (_wireSampled * 397) ^ (_wireSent * 31) ^ (inKeys * 7)
                      ^ (_peerDrivenWalls * 131) ^ (livePeers * 17)
                      ^ (_peerHoldsWithoutLocalCoverage * 3);
            bool heartbeat = now >= _nextPeerSetLog;
            if (sig == _peerSetSig && !heartbeat)
                return;
            _peerSetSig = sig;
            _nextPeerSetLog = now + PeerSetHeartbeatSeconds;

            // Resolution is asked ONLY on a printing pass — it walks the segment table.
            int resolved = 0;
            if (inKeys > 0)
            {
                _segKeyScratch.Clear();
                foreach (Segment s in _live.Segments.Values)
                {
                    if (s.WireKey != 0)
                        _segKeyScratch.Add(s.WireKey);
                }
                foreach (uint k in _peerKeyScratch)
                {
                    if (_segKeyScratch.Contains(k))
                        resolved++;
                }
                _segKeyScratch.Clear();
            }
            _peerKeyScratch.Clear();

            string outbound =
                $"OUT sampled {_wireSampled} wall(s), sent {_wireSent} (cap "
                + $"{Net.NetProtocol.WallFadesMaxKeys}), DROPPED BY THE CAP {dropped}";
            string inbound = _peerFadeEverSeen
                ? $"IN {livePeers} live peer(s), {inKeys} distinct key(s), resolved {resolved}, "
                  + $"UNRESOLVED {inKeys - resolved}"
                : "IN NO PEER FADE SET HAS EVER ARRIVED THIS SESSION — no record 17 was ever "
                  + "received, so the inbound zeros below are 'nothing to compare', NOT 'the "
                  + "sets agree'";
            // HW-VERIFY
            VRLog.Note(Name,
                $"PEER FADE SET: {outbound}; {inbound}. "
                + $"Peer-driven walls now {_peerDrivenWalls}, their units {_peerDrivenUnit} "
                + $"piece(s) (widest '{_peerWidestWall}' {_peerWidestUnit}); locally-decided "
                + $"walls {_localDrivenWalls}, their units {_localDrivenUnit} piece(s) (widest "
                + $"'{_localWidestWall}' {_localWidestUnit}). A UNIT is renderers + body + "
                + "stacked shell + mounted + foliage + siblings + prop-unit dressing, and it is "
                + "a property of the SEGMENT: the peer term is composed at the fade TARGET, so "
                + "both sources drive the identical list and a per-wall unit difference between "
                + "them is impossible — the SET above is the only thing that can differ, and it "
                + "is what was wrong (24-key cap against 41-44 faded walls, user 2026-09-05 "
                + $"fackel.jpg). {_peerHoldsWithoutLocalCoverage} wall(s) are held by a peer "
                + "while this client's own coverage reads OFF, which is this feature WORKING and "
                + "is why LATCH WARN no longer counts them.");
        }
    }
}
