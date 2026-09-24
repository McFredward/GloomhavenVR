using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>
/// Retains every native opening for the map scene. Wire snapshots rotate bounded slices;
/// history is not expired while a slower participant can still be reading an earlier message.
/// Sender-local tokens are bound FIFO within a semantic context, never compared across peers.
/// </summary>
internal sealed class MapStoryOpeningLedger
{
    private sealed class Local
    {
        internal readonly WeakReference Subject;
        internal readonly MapStoryOpening State;
        internal readonly Dictionary<int, uint> Bindings = new();
        internal bool TerminalApplied;
        internal int ParticipantCursor;
        internal Local(object subject, MapStoryOpening state) { Subject = new WeakReference(subject); State = state; }
    }
    private readonly List<Local> _local = new();
    private readonly Dictionary<int, SortedDictionary<uint, MapStoryOpening>> _peers = new();
    private readonly Dictionary<int, HashSet<uint>> _bound = new();
    private readonly Dictionary<int, uint> _peerEpoch = new();
    private readonly Dictionary<int, HashSet<uint>> _retiredEpochs = new();
    private readonly uint _epoch;
    private uint _nextToken;
    private int _cursor;
    private Local? _last;
    private bool _bindingDirty;
    internal bool Changed { get; private set; }

    internal MapStoryOpeningLedger(uint epoch = 0)
    {
        _epoch = epoch != 0 ? epoch : unchecked((uint)Guid.NewGuid().GetHashCode());
        if (_epoch == 0) _epoch = 1;
    }

    internal void Open(object subject, uint semanticKey, uint contentKey, byte pageCount, IReadOnlyList<int> peers,
        bool bidirectional = false)
    {
        if (Find(subject) != null || pageCount == 0 || semanticKey == 0 || contentKey == 0) return;
        uint previous = 0;
        foreach (Local prior in _local)
            if (prior.State.SemanticKey == semanticKey) previous = prior.State.Token;
        var state = new MapStoryOpening { Epoch = _epoch, Token = ++_nextToken, PreviousToken = previous,
            SemanticKey = semanticKey, ContentKey = contentKey, PageCount = pageCount,
            Participants = CopyPeers(peers), Bidirectional = bidirectional };
        _local.Add(new Local(subject, state));
        Changed = true;
        _bindingDirty = true;
    }

    internal bool Owns(object? subject) => subject != null && Find(subject) != null;

    /// <summary>Caller must prove the exact native opening remains open after a failed dispatch.</summary>
    internal void RetryTerminal(object subject)
    {
        Local? entry = Find(subject);
        if (entry != null && !entry.State.Finished) entry.TerminalApplied = false;
    }

    internal void Update(object subject, int page, IReadOnlyList<int> peers)
    {
        Local? entry = Find(subject);
        if (entry == null || entry.State.Finished) return;
        byte encoded = page < 0 ? byte.MaxValue : (byte)Math.Min(page, entry.State.PageCount - 1);
        if (encoded != entry.State.Page)
        {
            entry.State.Page = encoded;
            if (entry.State.Bidirectional) ++entry.State.PageRevision;
            Changed = true;
        }
        // A late participant can join a currently open native message. Completed history
        // never acquires new recipients, so an old FINISHED cannot dismiss its new opening.
        for (int i = 0; i < peers.Count; ++i)
        {
            int peer = peers[i];
            if (!Has(entry.State.Participants, peer))
            {
                AddParticipant(entry.State, peer);
                Changed = _bindingDirty = true;
            }
        }
    }

    internal void Finish(object? subject)
    {
        Local? entry = subject != null ? Find(subject) : null;
        if (entry == null || entry.State.Finished) return;
        entry.State.Finished = true;
        Changed = true;
    }

    internal MapStoryOpening[] Sample(object? current)
    {
        var result = new List<MapStoryOpening>();
        Local? active = current != null ? Find(current) : null;
        if (active != null) result.Add(Snapshot(active));
        int visited = 0;
        while (visited++ < _local.Count && result.Count < MapStoryLifecycleCodec.MaxEntries)
        {
            if (_cursor >= _local.Count) _cursor = 0;
            Local next = _local[_cursor++];
            if (next != active) result.Add(Snapshot(next));
        }
        Changed = false;
        return result.ToArray();
    }

    internal void Observe(int sender, MapStoryOpening[] entries)
    {
        if (entries.Length == 0 || entries[0].Epoch == 0) return;
        uint epoch = entries[0].Epoch;
        foreach (MapStoryOpening entry in entries) if (entry.Epoch != epoch) return;
        if (_retiredEpochs.TryGetValue(sender, out HashSet<uint>? retired) && retired.Contains(epoch)) return;
        if (_peerEpoch.TryGetValue(sender, out uint oldEpoch) && oldEpoch != epoch)
            ForgetPeer(sender);
        _peerEpoch[sender] = epoch;
        if (!_peers.TryGetValue(sender, out SortedDictionary<uint, MapStoryOpening>? history))
            _peers[sender] = history = new SortedDictionary<uint, MapStoryOpening>();
        foreach (MapStoryOpening incoming in entries)
        {
            if (!MapStoryLifecycleCodec.Valid(incoming)) continue;
            if (history.TryGetValue(incoming.Token, out MapStoryOpening? prior))
            {
                // One token may never change subjects, even in an out-of-order packet.
                if (prior.SemanticKey != incoming.SemanticKey || prior.ContentKey != incoming.ContentKey
                    || prior.PageCount != incoming.PageCount || prior.PreviousToken != incoming.PreviousToken
                    || prior.Bidirectional != incoming.Bidirectional) continue;
                prior.Finished |= incoming.Finished;
                if (incoming.Page != byte.MaxValue && (prior.Bidirectional
                        ? incoming.PageRevision > prior.PageRevision
                            || (incoming.PageRevision == prior.PageRevision && incoming.Page > prior.Page)
                        : prior.Page == byte.MaxValue || incoming.Page > prior.Page))
                { prior.Page = incoming.Page; prior.PageRevision = incoming.PageRevision; }
                foreach (int id in incoming.Participants)
                    if (!Has(prior.Participants, id)) { AddParticipant(prior, id); _bindingDirty = true; }
            }
            else { history[incoming.Token] = Copy(incoming); _bindingDirty = true; }
        }
    }

    internal void ForgetPeer(int sender)
    {
        if (_peerEpoch.TryGetValue(sender, out uint epoch))
        {
            if (!_retiredEpochs.TryGetValue(sender, out HashSet<uint>? retired))
                _retiredEpochs[sender] = retired = new HashSet<uint>();
            retired.Add(epoch);
            _peerEpoch.Remove(sender);
        }
        _peers.Remove(sender); _bound.Remove(sender);
        foreach (Local local in _local)
        {
            local.Bindings.Remove(sender);
            if (!local.State.Finished) continue;
            int index = Array.IndexOf(local.State.Participants, sender);
            if (index < 0) continue;
            var remaining = new int[local.State.Participants.Length - 1];
            Array.Copy(local.State.Participants, 0, remaining, 0, index);
            Array.Copy(local.State.Participants, index + 1, remaining, index, remaining.Length - index);
            local.State.Participants = remaining;
        }
        _bindingDirty = Changed = true;
    }

    internal int Resolve(object subject, int localId, int localPage, bool nativeOpen)
    {
        Local? current = Find(subject);
        if (current == null || current.State.Finished || current.TerminalApplied || !nativeOpen || localPage < 0)
            return -1;
        Bind(localId);
        int target = localPage;
        uint revision = current.State.PageRevision;
        bool terminal = false;
        foreach (KeyValuePair<int, uint> binding in current.Bindings)
        {
            MapStoryOpening peer = _peers[binding.Key][binding.Value];
            terminal |= peer.Finished;
            if (peer.Page == byte.MaxValue) continue;
            if (!current.State.Bidirectional) target = Math.Max(target, peer.Page);
            else if (peer.PageRevision > revision || (peer.PageRevision == revision && peer.Page > target))
            { target = peer.Page; revision = peer.PageRevision; }
        }
        if (terminal)
        {
            // Armed before the native call: Hide does not reset the final page index,
            // and its synchronous callback can reenter a resolver or open a successor.
            current.TerminalApplied = true;
            return current.State.PageCount;
        }
        if (current.State.Bidirectional)
        {
            // Adopt before the native page callback. The next local sample observes that
            // same page and must not manufacture a new revision while relaying it.
            current.State.Page = (byte)target;
            current.State.PageRevision = revision;
            return target != localPage ? target : -1;
        }
        return target > localPage ? target : -1;
    }

    private void Bind(int localId)
    {
        if (!_bindingDirty) return;
        _bindingDirty = false;
        foreach (KeyValuePair<int, SortedDictionary<uint, MapStoryOpening>> peer in _peers)
        {
            if (!_bound.TryGetValue(peer.Key, out HashSet<uint>? used))
                _bound[peer.Key] = used = new HashSet<uint>();
            foreach (Local local in _local)
            {
                if (local.Bindings.ContainsKey(peer.Key) || !Has(local.State.Participants, peer.Key)) continue;
                foreach (MapStoryOpening remote in peer.Value.Values)
                {
                    if (used.Contains(remote.Token) || !Has(remote.Participants, localId)
                        || remote.SemanticKey != local.State.SemanticKey || remote.ContentKey != local.State.ContentKey
                        || remote.PageCount != local.State.PageCount
                        || remote.Bidirectional != local.State.Bidirectional) continue;
                    if (remote.PreviousToken != 0 && !used.Contains(remote.PreviousToken)
                        && (!peer.Value.TryGetValue(remote.PreviousToken, out MapStoryOpening? predecessor)
                            || Has(predecessor.Participants, localId))) continue;
                    local.Bindings.Add(peer.Key, remote.Token);
                    used.Add(remote.Token);
                    break;
                }
            }
        }
    }

    private Local? Find(object subject)
    {
        if (_last != null && ReferenceEquals(_last.Subject.Target, subject)) return _last;
        foreach (Local entry in _local)
            if (ReferenceEquals(entry.Subject.Target, subject)) return _last = entry;
        return null;
    }
    private static bool Has(int[] peers, int value) => Array.IndexOf(peers, value) >= 0;
    private static int[] CopyPeers(IReadOnlyList<int> peers)
    {
        var result = new int[peers.Count];
        for (int i = 0; i < peers.Count; ++i) result[i] = peers[i];
        return result;
    }
    private static void AddParticipant(MapStoryOpening entry, int peer)
    {
        var result = new int[entry.Participants.Length + 1];
        Array.Copy(entry.Participants, result, entry.Participants.Length);
        result[result.Length - 1] = peer;
        entry.Participants = result;
    }
    private static MapStoryOpening Snapshot(Local local)
    {
        MapStoryOpening snapshot = Copy(local.State);
        int count = local.State.Participants.Length;
        if (count <= MapStoryLifecycleCodec.MaxParticipants) return snapshot;
        // A party has at most three other players at once, but a long opening may see
        // disconnect/rejoin churn with new IDs. Retain those recipients without making
        // the entire record invalid: each immutable snapshot rotates a bounded slice.
        snapshot.Participants = new int[MapStoryLifecycleCodec.MaxParticipants];
        for (int i = 0; i < snapshot.Participants.Length; ++i)
            snapshot.Participants[i] = local.State.Participants[local.ParticipantCursor++ % count];
        return snapshot;
    }
    private static MapStoryOpening Copy(MapStoryOpening entry) => new() {
        Epoch = entry.Epoch, Token = entry.Token, PreviousToken = entry.PreviousToken, SemanticKey = entry.SemanticKey,
        ContentKey = entry.ContentKey, Page = entry.Page, PageCount = entry.PageCount,
        PageRevision = entry.PageRevision, Bidirectional = entry.Bidirectional,
        Finished = entry.Finished, Participants = (int[])entry.Participants.Clone() };
}
