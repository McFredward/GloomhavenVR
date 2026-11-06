using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private readonly Dictionary<int, List<ScenarioResultsState>> _pendingResults = new();
    private readonly Dictionary<int, ulong> _resultsRevisions = new();
    private readonly List<int> _resultsPeers = new(4);
    private readonly ScenarioResultsPresentation _resultsPresentation = new();
    private ScenarioResultsState? _sentResults, _resultsSourceBasis;
    private int _resultsPublishingAuthor, _resultsPeerFingerprint;
    private readonly Dictionary<int, List<ScenarioResultsState>> _resultsBases = new();
    private readonly Dictionary<int, ScenarioResultsState> _resultsLatestData = new();
    private readonly Dictionary<int, ulong> _resultsQueued = new();
    private ulong _resultsRevision;
    private float _nextResultsCapture, _nextResultsRefresh;

    private static void OnResultsWindowVisibility(WindowVisibilityEvent change)
    {
        ScenarioResultsPose.ObserveWindowVisibility(change.Window, change.Shown);
        SharedWindowScrollSync.ObserveWindowVisibility(change.Window, change.Shown);
    }

    private void TickResultsSend(float now)
    {
        if (now < _nextResultsCapture) return;
        _nextResultsCapture = now + 1f / NetProtocol.SendRateHz;
        int local = _transport.LocalPlayerId;
        int author = SharedWindowSpawnAuthority.AuthorId;
        if (author <= 0) return;
        bool publisher = local == author;
        if (publisher && _resultsPublishingAuthor != author)
        {
            // A former observer must release its native draw lease BEFORE it
            // becomes the common publisher, otherwise it would sample its own zero alpha.
            _resultsPresentation.Reset(); _resultsSourceBasis = _sentResults = null;
            _resultsPeerFingerprint = 0;
        }
        _resultsPublishingAuthor = publisher ? author : 0;
        if (publisher) ScenarioResultsPose.BeginAuthorOpening();
        // An otherwise flat party still needs a movable native VR window. Only
        // packet work waits for another compatible presenter, never opening authority.
        if (ScenarioResultsPose.Key == 0 && !(publisher && !ScenarioResultsPose.NativeVisible
            && _sentResults != null && _sentResults.HasPresentation)) return;
        VersionGuard.CollectContinuationPeers(_resultsPeers, local);
        if (_resultsPeers.Count == 0) return;
        _resultsPeers.Sort();
        int peerFingerprint = 17;
        foreach (int peer in _resultsPeers) peerFingerprint = unchecked(peerFingerprint * 31 + peer);
        var state = new ScenarioResultsState { SampleTime = now,
            HasPresentation = publisher, Visible = ScenarioResultsPose.NativeVisible };
        if (!ScenarioResultsPose.Capture(state))
        {
            if (!publisher || state.Visible || _sentResults == null) return;
            state = CopyResultsPose(_sentResults, now);
            state.Visible = false;
        }
        if (publisher)
        {
            if (!state.Visible)
            {
                if (_sentResults == null) return;
                state.FrameSize = _sentResults.FrameSize;
            }
            if (state.Visible) _resultsPresentation.Capture(state);
            if (state.Visible && state.Parts.Length == 0) return;
        }
        else
        {
            if (!state.Visible || !SharedWindowMoves.HasLocalClaim(SharedWindowKind.Results)
                || !state.Move.HasValue) return;
            if (ScenarioResultsNative.TryGetConverted(out ConvertedPanel? panel) && panel != null)
                state.FrameSize = panel.HostRect.rect.size;
        }
        bool changed = !SameResults(_sentResults, state);
        bool newParticipant = peerFingerprint != _resultsPeerFingerprint;
        if (!changed && !newParticipant && now < _nextResultsRefresh) return;
        if (++_resultsRevision == 0) ++_resultsRevision;
        state.Revision = _resultsRevision;
        ScenarioResultsState wire = state;
        if (publisher && state.Visible && !newParticipant
            && ScenarioResultsDelta.Compatible(_resultsSourceBasis, state))
            wire = ScenarioResultsDelta.Create(_resultsSourceBasis!, state) ?? state;
        byte[] bytes = ScenarioResultsCodec.Write(wire);
        if (_transport is FfsNetTransport ffs)
        { if (!ffs.TrySendResults(bytes, bytes.Length, wire)) return; }
        else _transport.Send(bytes, bytes.Length, wire);
        if (publisher && wire.BaseRevision == 0) _resultsSourceBasis = state.Visible ? state : null;
        _sentResults = state; _resultsPeerFingerprint = peerFingerprint;
        _nextResultsRefresh = now + .5f;
    }

    private static ScenarioResultsState CopyResultsPose(ScenarioResultsState source, float now) => new()
    {
        Key = source.Key, Author = source.Author, SampleTime = now, HasPresentation = source.HasPresentation,
        Visible = source.Visible, Pose = source.Pose, Size = source.Size, Reference = source.Reference,
        Origin = source.Origin, Rotation = source.Rotation, Scale = source.Scale,
        Move = source.Move, FrameSize = source.FrameSize
    };

    private static bool SameResults(ScenarioResultsState? a, ScenarioResultsState b)
    {
        if (a == null || a.Key != b.Key || a.Author != b.Author || a.Visible != b.Visible
            || a.HasPresentation != b.HasPresentation || a.FrameSize != b.FrameSize
            || !a.Pose.Position.Equals(b.Pose.Position) || !a.Pose.Rotation.Equals(b.Pose.Rotation)
            || a.Size != b.Size || a.Reference != b.Reference || a.Origin != b.Origin
            || a.Rotation != b.Rotation || a.Scale != b.Scale || a.Parts.Length != b.Parts.Length) return false;
        // Sequence heartbeats are ordering, not a reason to serialize a static widget tree.
        if (a.Move.HasValue != b.Move.HasValue) return false;
        if (a.Move.HasValue)
        {
            SharedWindowMoveEntry x = a.Move.Value, y = b.Move!.Value;
            if (!x.SameOpening(in y) || x.Claim != y.Claim || x.Phase != y.Phase
                || x.ParentClaim != y.ParentClaim || x.ParentAuthor != y.ParentAuthor) return false;
        }
        for (int i = 0; i < a.Parts.Length; i++)
        {
            ScenarioResultsPart x = a.Parts[i], y = b.Parts[i];
            if (x.Id != y.Id || x.ParentId != y.ParentId || x.ParentBinding != y.ParentBinding
                || x.Address != y.Address || x.Structure != y.Structure || x.Nodes.Length != y.Nodes.Length) return false;
            for (int n = 0; n < x.Nodes.Length; n++)
            {
                var p = x.Nodes[n]; var q = y.Nodes[n];
                if (ReferenceEquals(p, q)) continue;
                if (p.Binding != q.Binding || p.Values.Count != q.Values.Count) return false;
                foreach (var pair in p.Values)
                    if (!q.Values.TryGetValue(pair.Key, out var value) || !pair.Value.Same(value)) return false;
            }
        }
        return true;
    }

    private bool QueueResults(int sender, byte[] bytes, int length)
    {
        if (sender <= 0 || !ScenarioResultsCodec.TryRead(bytes, length, out ScenarioResultsState? state)) return false;
        if (state!.Author != SharedWindowSpawnAuthority.AuthorId
            || state.HasPresentation && sender != state.Author) return true;
        bool newer = !_resultsRevisions.TryGetValue(sender, out ulong previous) || state.Revision > previous;
        if (!newer && !(state.HasPresentation && state.Visible && state.BaseRevision == 0)) return true;
        if (newer) _resultsRevisions[sender] = state.Revision;
        if (!state.HasPresentation)
        { if (newer) QueueResultsReady(sender, state); return true; }
        if (state.Visible && state.BaseRevision == 0)
        {
            if (!_resultsBases.TryGetValue(sender, out var bases))
            { if (_resultsBases.Count >= 8) return true; _resultsBases.Add(sender, bases = new List<ScenarioResultsState>(2)); }
            if (!bases.Exists(b => b.Key == state.Key && b.Revision == state.Revision))
            {
                bases.Add(state); bases.Sort((a, b) => b.Revision.CompareTo(a.Revision));
                if (bases.Count > 2) bases.RemoveAt(bases.Count - 1);
            }
        }
        if (newer) _resultsLatestData[sender] = state;
        if (!_resultsLatestData.TryGetValue(sender, out ScenarioResultsState? latest)) return true;
        ScenarioResultsState? complete = latest;
        if (latest.BaseRevision != 0)
        {
            ScenarioResultsState? basis = null;
            if (_resultsBases.TryGetValue(sender, out var bases))
                basis = bases.Find(b => b.Key == latest.Key && b.Author == latest.Author && b.Revision == latest.BaseRevision);
            complete = ScenarioResultsDelta.Expand(basis, latest);
        }
        if (complete != null) QueueResultsReady(sender, complete);
        return true;
    }

    private void QueueResultsReady(int sender, ScenarioResultsState state)
    {
        if (_resultsQueued.TryGetValue(sender, out ulong queued) && state.Revision <= queued) return;
        if (!_pendingResults.TryGetValue(sender, out var queue))
        {
            if (_pendingResults.Count >= 8) return;
            _pendingResults.Add(sender, queue = new List<ScenarioResultsState>(1));
        }
        // All sparse dependencies expanded BEFORE coalescing. The complete latest
        // picture is independent of every skipped intermediate animation sample.
        queue.Clear(); queue.Add(state); _resultsQueued[sender] = state.Revision;
    }

    private void ApplyResults()
    {
        if (ScenarioResultsPose.ObserveNativeBoundary(out int retiredAuthor, out uint retiredKey))
            _resultsPresentation.RetireOpening(retiredAuthor, retiredKey);
        // Original native content is never driven by received presentation. Only this
        // detached, inert picture and the existing shared grab frame consume the stream.
        foreach (var pair in _pendingResults)
        {
            if (pair.Value.Count == 0) continue;
            try
            {
                ScenarioResultsState state = pair.Value[0];
                pair.Value.RemoveAt(0);
                if (!ScenarioResultsPose.Observe(pair.Key, state)) continue;
                if (state.HasPresentation && pair.Key == state.Author) _resultsPresentation.Apply(state);
            }
            catch (Exception error) { LogPhaseError("Apply native results presentation", error); }
        }
        ScenarioResultsPose.Tick();
        _resultsPresentation.Tick();
    }

    private void ResetResults()
    {
        _pendingResults.Clear(); _resultsRevisions.Clear(); _resultsPeers.Clear();
        _resultsBases.Clear(); _resultsLatestData.Clear(); _resultsQueued.Clear();
        _resultsPresentation.Reset(); ScenarioResultsPose.Reset();
        _resultsSourceBasis = _sentResults = null; _resultsPublishingAuthor = _resultsPeerFingerprint = 0;
        _nextResultsCapture = _nextResultsRefresh = 0;
    }
}
