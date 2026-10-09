using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class MotionSlot : TownServiceMotionPending
    {
        internal ulong ReceivedSequence;
        internal float SampleTime, ReceivedAt, DirtySince;
        internal bool VisibilityTransition;
        internal CardReturnClock? ReturnClock;
    }
    private sealed class CardReturnClock
    {
        private readonly float _offset;
        private TownServiceMotionEntry _current;
        private TownServiceMotionEntry? _pending;
        private float _sampleTime, _pendingSampleTime, _rate = 1f, _pendingRate = 1f, _renderedProgress = float.NegativeInfinity;
        private float _observedSampleTime, _observedAge;
        internal float CurrentSampleTime => _sampleTime;
        internal CardReturnClock(TownServiceMotionEntry entry, float sampleTime, float offset)
        { _current = entry; _sampleTime = _observedSampleTime = sampleTime;
          _observedAge = ReturnProgress(entry); _offset = offset; }
        internal void Observe(TownServiceMotionEntry entry, float sampleTime, float receivedAt)
        {
            // One native return has one source-to-observer clock mapping. Restarting
            // from each arrival turns ordinary transport jitter into visible reversals.
            // The merchant's exponential sampler is a rolling current-pose/remaining-
            // lifetime receipt; preserve that exact source receipt and extrapolate it
            // to the retained source time rather than inventing another easing curve.
            // VRCard advances by min(ownerDelta,.05). Its authored age can
            // therefore progress slower than wall time at low FPS or a hitch.
            // Retain the rate of exact source receipts, without changing the
            // native easing/duration or rewinding an already rendered phase.
            float rate = _rate;
            if (sampleTime > _observedSampleTime)
            {
                rate = Mathf.Clamp01((ReturnProgress(entry) - _observedAge) / (sampleTime - _observedSampleTime));
                _observedSampleTime = sampleTime; _observedAge = ReturnProgress(entry);
            }
            if (sampleTime + _offset <= receivedAt)
            { _current = entry; _sampleTime = sampleTime; _rate = rate; _pending = null; }
            else { _pending = entry; _pendingSampleTime = sampleTime; _pendingRate = rate; }
        }
        internal TownServiceMotionEntry Current(float now, out float age)
        {
            // A faster later packet can describe a source instant still ahead of
            // this flight's rendered clock. Keep the previous exact receipt until
            // that instant; never jump forward and then hold at a negative age.
            if (_pending != null && _pendingSampleTime + _offset <= now)
            { _current = _pending; _sampleTime = _pendingSampleTime; _rate = _pendingRate; _pending = null; }
            float progress = ReturnProgress(_current) + Mathf.Max(0f, now - _sampleTime - _offset) * _rate;
            progress = Mathf.Max(progress, _renderedProgress); _renderedProgress = progress;
            age = _current.Numbers[0] + progress - ReturnProgress(_current);
            return _current;
        }
        // Exponential receipts may carry increasing age or decreasing remaining
        // duration. Age minus duration retains progress for both wire shapes.
        private static float ReturnProgress(TownServiceMotionEntry entry) =>
            entry.Numbers[2] == 1f ? entry.Numbers[0] - entry.Numbers[1] : entry.Numbers[0];
    }
    private sealed class SourceMotion
    {
        internal TownServiceFrame? Previous;
        internal ulong HandRevision;
        internal bool Live, VisibleFan;
        internal bool TerminalCardReturn;
        internal CardReturnSampler? ReturnSample;
        internal VRHand? ReturnHand;
        internal readonly Dictionary<TownServiceMotionKey, MotionSlot> Slots = new();
    }
    private sealed class PeerMotion
    {
        internal readonly Dictionary<TownServiceMotionKey, MotionSlot> Slots = new();
        internal readonly Dictionary<TownServiceMotionKey, ReturnCohortAssembly> ReturnCohorts = new();
        internal ulong CueSequence, MerchantReadySequence;
        internal bool HasReturnOffset;
        internal float ReturnOffset, ReturnReportAt;
    }
    private sealed class RemoteMotion
    {
        internal TownServiceFrame? Author;
        internal TownServiceFrame? Merged;
        internal int Owner;
        internal ulong HandSequence;
        internal ulong OfferedRootSequence;
        internal float OfferedSampleTime = -1f, OfferedStarted, OfferedDuration;
        internal Vector3 OfferedFrom, OfferedTarget, OfferedScaleFrom, OfferedScaleTarget;
        internal Quaternion OfferedRotationFrom, OfferedRotationTarget;
        internal float LastSampleTime = -1f, HandStarted, HandDuration;
        internal bool HadCardReturn;
        internal Vector3 HandFrom, CanvasFrom, HandTarget, CanvasTarget;
        internal readonly List<MotionSlot> Slots = new();
    }
    private static readonly Dictionary<LocalModule, SourceMotion> MotionSources = new();
    private static readonly Dictionary<int, PeerMotion> MotionPeers = new();
    private sealed class MotionHandReference { internal VRHand Hand = null!; internal bool FollowsRotation; }
    private static readonly Dictionary<Transform, MotionHandReference> MotionHands = new();
    private static readonly HashSet<Transform> MotionOfferings = new();
    private static readonly Dictionary<Transform, TownServiceToken> MotionReturns = new();
    internal delegate bool CardReturnSampler(Transform source, Transform shared, VRHand? hand,
        out uint revision, out float[] values);
    private sealed class CardReturnReference
    { internal CardReturnSampler Sample = null!; internal VRHand? Hand; }
    private static readonly Dictionary<Transform, CardReturnReference> CardReturns = new();
    internal static void RegisterCardReturn(Transform source, CardReturnSampler sample, VRHand? hand = null)
    {
        if (source == null) return;
        if (!CardReturns.TryGetValue(source, out CardReturnReference? value))
            CardReturns.Add(source, value = new CardReturnReference());
        value.Sample = sample; value.Hand = hand;
    }
    private static CardReturnReference? CardReturn(Transform source)
    {
        for (Transform? node = source; node != null; node = node.parent)
            if (CardReturns.TryGetValue(node, out CardReturnReference? reference)) return reference;
        return null;
    }
    private static readonly HashSet<LocalModule> MotionSourceRemoval = new();
    private static readonly List<TownServiceMotionPending> MotionWaiting = new(), MotionLive = new(), MotionVisibleFan = new();
    private static readonly List<Transform> MotionHandRemoval = new();
    private static float _nextMotionHandCleanup;
    private static readonly List<TownServiceMotionKey> MotionRemoval = new();
    private static readonly Dictionary<RemoteModule, RemoteMotion> MotionRemoteFrames = new();
    private static readonly List<RemoteModule> MotionFrameRemoval = new();
    private static float _nextMotionSend;
    private static ulong _motionSequence;
    private static int _motionCursor, _motionLiveCursor, _motionVisibleCursor;
    private static uint _motionCommitSession, _motionCommitRevision;
    private static float _nextMotionCommit;
    private static TownServiceMotionEntry? _motionCue;
    private static float _nextMotionCue;
    private static TownServiceMotionEntry? _motionMerchantReady;
    private static float _nextMotionMerchantReady;
    private static uint _motionLifetime = 1;
    private static ulong _motionHandRevision;
    private static float _motionDiagnosticAt;
    private static int _motionSent, _motionSentBytes, _motionReceived, _motionReceivedBytes;
    internal static bool FastMotionCaptureEnabled;

    // Exact publisher provenance only. Physical grabs are also found through the
    // existing GrabAnchor ancestry; there is no observer-local nearest-hand guess.
    internal static void RegisterMotionHand(Transform source, VRHand? hand, bool followsRotation = true)
    {
        if (source == null) return;
        if (MotionHands.TryGetValue(source, out MotionHandReference? existing))
        {
            if (ReferenceEquals(existing.Hand, hand) && existing.FollowsRotation == followsRotation) return;
            MotionHands.Remove(source); _motionHandRevision++;
        }
        if (hand == null) return;
        MotionHands[source] = new MotionHandReference { Hand = hand, FollowsRotation = followsRotation };
        _motionHandRevision++;
    }

    // The source knows whether an owned item is parked in an NPC palm. Its
    // address alone cannot distinguish that role from a hidden prewarmed fan.
    internal static void RegisterMotionOffering(Transform source, bool offering)
    {
        if (source == null) return;
        if (offering ? MotionOfferings.Add(source) : MotionOfferings.Remove(source)) _motionHandRevision++;
    }

    internal static void RegisterMotionReturn(Transform source, TownServiceToken token)
    {
        if (source != null) MotionReturns[source] = token;
    }

    private static TownServiceToken? MotionReturn(Transform source)
    {
        for (Transform? node = source; node != null; node = node.parent)
            if (MotionReturns.TryGetValue(node, out TownServiceToken? token)) return token;
        return null;
    }

    internal static void CaptureMotion(Action<byte[], int, object?> send)
    {
        float now = Time.unscaledTime;
        if (now < _nextMotionSend) return;
        _nextMotionSend = now + TownServiceMotionCodec.SendInterval;
        if (now >= _nextMotionHandCleanup)
        {
            _nextMotionHandCleanup = now + 5f; MotionHandRemoval.Clear();
            foreach (var pair in MotionHands) if (pair.Key == null) MotionHandRemoval.Add(pair.Key!);
            foreach (Transform gone in MotionHandRemoval) MotionHands.Remove(gone);
            MotionHandRemoval.Clear();
            foreach (Transform source in MotionOfferings) if (source == null) MotionHandRemoval.Add(source!);
            foreach (Transform gone in MotionHandRemoval) MotionOfferings.Remove(gone);
            MotionHandRemoval.Clear();
            foreach (var pair in MotionReturns) if (pair.Key == null) MotionHandRemoval.Add(pair.Key!);
            foreach (Transform gone in MotionHandRemoval) MotionReturns.Remove(gone);
            MotionHandRemoval.Clear();
            foreach (Transform source in CardReturns.Keys) if (source == null) MotionHandRemoval.Add(source!);
            foreach (Transform gone in MotionHandRemoval) CardReturns.Remove(gone);
        }
        MotionSourceRemoval.Clear();
        foreach (var pair in MotionSources) MotionSourceRemoval.Add(pair.Key);
        MotionWaiting.Clear(); MotionLive.Clear(); MotionVisibleFan.Clear();
        CaptureMotionLane(PrivateLane, 0, now); CaptureMotionLane(PublicLane, 1, now); CaptureMotionLane(StockLane, 2, now);
        CollectOfferedFrameMotion(PrivateLane, now);
        foreach (LocalModule gone in MotionSourceRemoval) MotionSources.Remove(gone);
        TownServiceMotionEntry? commit = null;
        if (TryLocalTempleDonationCommit(out uint session, out uint revision, out float age)
            && (session != _motionCommitSession || revision != _motionCommitRevision || now >= _nextMotionCommit))
        { commit = new TownServiceMotionEntry { Kind = 3, Service = 2, Session = session, Revision = revision, CommitAge = age };
          _motionCommitSession = session; _motionCommitRevision = revision; _nextMotionCommit = now + TownServiceMotionCodec.Heartbeat; }
        var cue = new TownServiceMotionEntry { Kind = 5, Service = 3, Session = _motionLifetime,
            CueReady = TownServiceSharedCue.LocalReady, CueStrength = TownServiceSharedCue.LocalStrength,
            HasSharedCue = TownServicePopulation.IsFaceAuthor, SharedCueReady = TownServiceSharedCue.PublishedReady,
            SharedCueStrength = TownServiceSharedCue.PublishedStrength, SharedGuideOwner = TownServiceSharedCue.PublishedGuideOwner, Property = 31 };
        bool sendCue = _motionCue == null || now >= _nextMotionCue || cue.Session != _motionCue.Session
            || cue.CueReady != _motionCue.CueReady || cue.CueStrength != _motionCue.CueStrength
            || cue.HasSharedCue != _motionCue.HasSharedCue || cue.SharedCueReady != _motionCue.SharedCueReady
            || cue.SharedCueStrength != _motionCue.SharedCueStrength || cue.SharedGuideOwner != _motionCue.SharedGuideOwner;
        // The common NPC pose depends on visitor intent, never on transmitting a
        // personal pre-drop guide. This numeric affordance survives cold artwork.
        var merchantReady = new TownServiceMotionEntry { Kind = 6, Service = 1, Session = _motionLifetime,
            CueReady = TownServiceMerchantHandoff.WantsOffering };
        bool sendMerchantReady = _motionMerchantReady == null || now >= _nextMotionMerchantReady
            || merchantReady.Session != _motionMerchantReady.Session || merchantReady.CueReady != _motionMerchantReady.CueReady;
        if (MotionWaiting.Count == 0 && MotionLive.Count == 0 && MotionVisibleFan.Count == 0
            && commit == null && !sendCue && !sendMerchantReady) return;
        var packet = new TownServiceMotionPacket { Sequence = ++_motionSequence, SampleTime = now };
        if (packet.Sequence == 0) { _motionSequence = ulong.MaxValue; return; }
        if (commit != null) packet.Entries.Add(commit);
        if (sendCue) { packet.Entries.Add(cue);
          _motionCue = cue; _nextMotionCue = now + TownServiceMotionCodec.Heartbeat; }
        if (sendMerchantReady) { packet.Entries.Add(merchantReady);
          _motionMerchantReady = merchantReady; _nextMotionMerchantReady = now + TownServiceMotionCodec.Heartbeat; }
        // Dirty live controls have their own finite turn. Cold prewarmed fan
        // heartbeats retain bounded progress and cannot delay a press or scroll.
        byte[] bytesPacket = TownServiceMotionBudget.FillPacked(packet, MotionLive, MotionVisibleFan, MotionWaiting,
            ref _motionLiveCursor, ref _motionVisibleCursor, ref _motionCursor, now);
        if (bytesPacket.Length == 0) return;
        send(bytesPacket, bytesPacket.Length, packet);
        if (VRLog.WantsDebug) { _motionSent++; _motionSentBytes += bytesPacket.Length; MotionDiagnostics(now); }
    }

    private static void CaptureMotionLane(LocalLane lane, byte laneId, float now)
    {
        if (!lane.Active || lane.SharedFrame == null) return;
        foreach (LocalModule module in lane.Modules.Values)
        {
            TownServiceFrame? frame = module.Last;
            if (frame == null || module.Binding.Root == null || module.Baseline == null) continue;
            MotionSourceRemoval.Remove(module);
            if (!MotionSources.TryGetValue(module, out SourceMotion? source))
            { source = new SourceMotion(); MotionSources.Add(module, source); }
            TownServiceFrame? previous = source.Previous;
            TownServiceMotionEntry? cardFlight = null;
            source.Slots.TryGetValue(MotionHeader(frame, laneId, 8).Key, out MotionSlot? previousCardFlight);
            CardReturnReference? returningCard = frame.Service != 2 && laneId != 1
                ? CardReturn(module.Binding.Root) : null;
            bool sameNativeReturn = returningCard != null && Equals(source.ReturnSample, returningCard.Sample)
                && ReferenceEquals(source.ReturnHand, returningCard.Hand)
                && returningCard.Sample.Target is Component currentNative && currentNative != null
                && NativeReturnAvailable(currentNative)
                && module.Binding.Root.IsChildOf(currentNative.transform)
                && (returningCard.Hand == null || currentNative.transform.IsChildOf(returningCard.Hand.Rig.Root));
            bool revokedReturn = source.TerminalCardReturn && !sameNativeReturn;
            if (revokedReturn) source.TerminalCardReturn = false;
            if (returningCard != null
                && returningCard.Sample(module.Binding.Root, lane.SharedFrame, returningCard.Hand,
                    out uint cardRevision, out float[] cardNumbers))
            {
                cardFlight = MotionHeader(frame, laneId, 8);
                cardFlight.Hand = returningCard.Hand != null && cardNumbers[2] == 1f
                    ? (byte)(returningCard.Hand.Side == HandSide.Left ? 3 : 4) : (byte)0;
                cardFlight.Revision = cardRevision; cardFlight.Numbers = cardNumbers;
                source.TerminalCardReturn = false;
                source.ReturnSample = returningCard.Sample; source.ReturnHand = returningCard.Hand;
            }
            // The immutable header is intentionally frozen during a return. The
            // actual native terminal frame must still cross the numeric lane;
            // otherwise its stale Kind1 heartbeat pulls the settled print/body
            // back toward their last in-flight pose. Publish the final physical
            // root and every child together once, then resume fresh ordinary
            // roots. This retains no native callback or transaction ownership.
            bool endedCardReturn = cardFlight == null && previousCardFlight != null;
            if (endedCardReturn && source.TerminalCardReturn && previousCardFlight!.Dirty)
                cardFlight = previousCardFlight.Entry;
            else if (endedCardReturn && !source.TerminalCardReturn && sameNativeReturn && returningCard?.Sample.Target is Component native
                && native != null && NativeReturnFinished(previousCardFlight!.Entry)
                && NativeReturnAvailable(native)
                && ValidMotionScale(native.transform.lossyScale))
            {
                TownServiceMotionEntry prior = previousCardFlight.Entry;
                cardFlight = MotionHeader(frame, laneId, 8);
                cardFlight.Hand = prior.Hand; cardFlight.Revision = prior.Revision;
                float duration = prior.Numbers[1];
                Transform physical = native.transform;
                cardFlight.Numbers = TownCardReturnMotion.Capture(module.Binding.Root, physical, lane.SharedFrame,
                    cardFlight.Hand != 0 ? returningCard.Hand : null, duration, duration,
                    (byte)prior.Numbers[2], prior.Numbers[3], physical.localToWorldMatrix, physical.rotation,
                    physical.localToWorldMatrix, physical.rotation, Vector3.zero);
                source.TerminalCardReturn = true;
            }
            if (cardFlight == null) source.Slots.Remove(MotionHeader(frame, laneId, 8).Key);
            if (MotionReturn(module.Binding.Root)?.HasReturnMotion != true)
                source.Slots.Remove(MotionHeader(frame, laneId, 7).Key);
            if (ReferenceEquals(previous, frame) && source.HandRevision == _motionHandRevision
                && MotionReturn(module.Binding.Root)?.HasReturnMotion != true && cardFlight == null && !endedCardReturn && !revokedReturn)
            {
                foreach (MotionSlot slot in source.Slots.Values)
                    if (slot.Dirty || now - slot.SentAt >= TownServiceMotionCodec.Heartbeat) AddMotionWaiting(slot, source);
                continue;
            }
            // Artwork contains the initial complete native state. Fast numbers add
            // only bindings which actually change, plus the small live root pose.
            if (previous == null || previous.Structure != frame.Structure || previous.Session != frame.Session
                || previous.PublicClaim != frame.PublicClaim)
                source.Slots.Clear();
            // Prepared originals were deliberately hidden while offered. A first
            // exact return clock must carry the live native root visibility/mount,
            // even when artwork capture is still retrying that module. Reusing
            // its last prepared header parks the printed front until a slower
            // repair arrives. Read only the original numeric root, in its actual
            // lane; no artwork, visibility guess or native continuation is added.
            TownServiceFrame rootFrame = frame;
            if (cardFlight != null || endedCardReturn || revokedReturn)
            {
                rootFrame = new TownServiceFrame { ParentModule = TownServiceFrame.ManifestModule,
                    Visible = module.Binding.Root.gameObject.activeInHierarchy,
                    Pose = ReadPose(module.Binding.Root, lane.SharedFrame) };
                using (new LaneScope(lane))
                { ReadParent(module, rootFrame); ReadCanvasFrame(module.Binding.Root, rootFrame); }
            }
            TownServiceMotionEntry root = MotionHeader(frame, laneId, 1);
            root.ParentModule = rootFrame.ParentModule; root.Binding = rootFrame.ParentBinding;
            root.ParentAlpha = rootFrame.ParentAlpha; root.Visible = rootFrame.Visible;
            root.Pose = rootFrame.Pose; root.HasCanvasFrame = rootFrame.HasCanvasFrame;
            root.CanvasPose = rootFrame.CanvasPose; root.CanvasRect = rootFrame.CanvasRect;
            root.CanvasSettings = rootFrame.CanvasSettings; root.CanvasSortingLayer = rootFrame.CanvasSortingLayer;
            root.CanvasSortingOrder = rootFrame.CanvasSortingOrder;
            VRHand? hand = MotionHand(module.Binding.Root, out bool followsRotation);
            // StockSync prepares a merchant return against its actual destination
            // hand without making that card a held prop. That verified native
            // sampler must also anchor the matching numeric root. Otherwise the
            // first clock has Hand3/4 but its root has Hand0: the atomic budget
            // rejects every pair and observers fall back to sampled artwork.
            if (hand == null && returningCard?.Hand != null
                && (cardFlight != null && cardFlight.Hand != 0 || source.TerminalCardReturn))
            { hand = returningCard.Hand; followsRotation = false; }
            if (hand != null && hand.HasPose)
            {
                followsRotation &= !module.Address.StartsWith("ritual.purse", StringComparison.Ordinal);
                root.Hand = (byte)((hand.Side == HandSide.Left ? 1 : 2) + (followsRotation ? 0 : 2));
                root.Pose = ReadMotionHandPose(module.Binding.Root, hand);
                if (!followsRotation)
                { Quaternion rotation = Quaternion.Inverse(lane.SharedFrame.rotation) * module.Binding.Root.rotation;
                  root.Pose[3] = rotation.x; root.Pose[4] = rotation.y; root.Pose[5] = rotation.z; root.Pose[6] = rotation.w; }
                Canvas? canvas = module.Binding.Root.GetComponentInParent<Canvas>(true);
                if (root.HasCanvasFrame && canvas != null && ReferenceEquals(MotionHand(canvas.transform, out _), hand))
                { root.CanvasOnHand = true; root.CanvasPose = ReadMotionHandPose(canvas.transform, hand);
                  if (!followsRotation) { Quaternion rotation = Quaternion.Inverse(lane.SharedFrame.rotation) * canvas.transform.rotation;
                    root.CanvasPose[3] = rotation.x; root.CanvasPose[4] = rotation.y; root.CanvasPose[5] = rotation.z; root.CanvasPose[6] = rotation.w; } }
            }
            TownServiceMotionEntry? flight = null;
            if (frame.Service == 2 && hand != null && root.Hand > 2
                && MotionReturn(module.Binding.Root) is TownServiceToken returning
                && returning.TryReturnMotion(module.Binding.Root, hand, lane.SharedFrame,
                    out uint returnRevision, out float[] returnNumbers))
            {
                flight = MotionHeader(frame, laneId, 7); flight.Hand = root.Hand;
                flight.Revision = returnRevision; flight.Numbers = returnNumbers;
                UpdateMotionSlot(source, flight);
            }
            if (cardFlight != null) UpdateMotionSlot(source, cardFlight);
            source.Live = flight != null || cardFlight != null || LiveMotion(module.Address, root.Hand) || MotionOffering(module.Binding.Root);
            source.VisibleFan = root.Hand is 3 or 4 && root.Visible && root.ParentAlpha > 0f;
            source.Slots.TryGetValue(root.Key, out MotionSlot? priorRoot);
            root.HasCanvasUpdate = priorRoot == null || priorRoot.Dirty && priorRoot.Entry.HasCanvasUpdate || now - priorRoot.SentAt >= TownServiceMotionCodec.Heartbeat
                || priorRoot.Entry.HasCanvasFrame != root.HasCanvasFrame || priorRoot.Entry.CanvasOnHand != root.CanvasOnHand
                || !SameNumbers(priorRoot.Entry.CanvasPose, root.CanvasPose)
                || !SameNumbers(priorRoot.Entry.CanvasRect, root.CanvasRect)
                || !SameNumbers(priorRoot.Entry.CanvasSettings, root.CanvasSettings)
                || priorRoot.Entry.CanvasSortingOrder != root.CanvasSortingOrder || priorRoot.Entry.CanvasSortingLayer != root.CanvasSortingLayer;
            if (cardFlight != null || endedCardReturn || revokedReturn || hand != null || module.HighPriority || previous != null && (!SameNumbers(previous.Pose, frame.Pose)
                || previous.ParentAlpha != frame.ParentAlpha || previous.Visible != frame.Visible
                || previous.ParentModule != frame.ParentModule || previous.ParentBinding != frame.ParentBinding
                || !SameNumbers(previous.CanvasPose, frame.CanvasPose)
                || !SameNumbers(previous.CanvasRect, frame.CanvasRect)
                || !SameNumbers(previous.CanvasSettings, frame.CanvasSettings))) UpdateMotionSlot(source, root);
            // The budget admits a new exact return together with its matching root, even
            // when that original mount has not moved since its already warmed baseline.
            if (cardFlight != null && source.Slots.TryGetValue(cardFlight.Key, out MotionSlot? returnClock)
                && returnClock.AdmittedReturnRevision != cardFlight.Revision
                && source.Slots.TryGetValue(root.Key, out MotionSlot? returnRoot)) returnRoot.Dirty = true;
            if (previous != null && previous.Structure == frame.Structure && previous.Nodes.Length == frame.Nodes.Length)
                for (int n = 0; n < frame.Nodes.Length; n++)
                    foreach (var property in frame.Nodes[n].Values)
                    {
                        if ((!TownServiceFastNumbers.IsFast(property.Key) && !TownServiceFastNumbers.IsMaterial(property.Key))
                            || !previous.Nodes[n].Values.TryGetValue(property.Key, out TownServiceValue? before)
                            || SameNumbers(before.Numbers, property.Value.Numbers)) continue;
                        // The header is the only world pose author. A native rect root's
                        // anchor/pivot/extent can still change independently of that pose;
                        // transfer that layout while the receiver ignores its local pose.
                        if (n == 0 && property.Key == TownServiceProperty.Transform
                            && (property.Value.Numbers.Length != 18 || SameRootLayout(before.Numbers, property.Value.Numbers))) continue;
                        if (TownServiceFastNumbers.IsMaterial(property.Key))
                        {
                            if (before.Numbers.Length != property.Value.Numbers.Length) continue;
                            for (int offset = 0; offset < before.Numbers.Length; offset++)
                            {
                                if (!TownServiceFastNumbers.MaterialNumber(offset) || before.Numbers[offset] == property.Value.Numbers[offset]) continue;
                                TownServiceMotionEntry number = MotionHeader(frame, laneId, 4);
                                number.Binding = frame.Nodes[n].Binding; number.Property = property.Key;
                                number.Offset = (ushort)offset; number.Numbers = new[] { property.Value.Numbers[offset] };
                                UpdateMotionSlot(source, number);
                            }
                            continue;
                        }
                        TownServiceMotionEntry entry = MotionHeader(frame, laneId, 2);
                        entry.Binding = frame.Nodes[n].Binding; entry.Property = property.Key;
                        entry.Numbers = property.Value.Numbers;
                        if (n == 0 && property.Key == TownServiceProperty.Transform)
                        {
                            // Layout owns only the rect fields. Canonical pose values keep
                            // equality/backpressure independent of the absolute root lane.
                            entry.Numbers = (float[])entry.Numbers.Clone();
                            Array.Clear(entry.Numbers, 0, 10);
                            entry.Numbers[6] = entry.Numbers[7] = entry.Numbers[8] = entry.Numbers[9] = 1f;
                        }
                        UpdateMotionSlot(source, entry);
                    }
            if ((cardFlight != null || endedCardReturn) && module.Binding.Root is RectTransform liveRect
                && frame.Nodes[0].Values.TryGetValue(TownServiceProperty.Transform, out TownServiceValue? originalRect))
            {
                // Native ItemChip reclaim canonicalizes its pooled face pivot
                // before flight. TRS alone cannot locate its printed geometry:
                // an old pivot shifts the ink while the root itself is correct.
                // Sample the actual root layout even when immutable artwork is
                // quiet, and stage it with that part's existing return receipt.
                var layout = MotionHeader(frame, laneId, 2);
                layout.Binding = frame.Nodes[0].Binding; layout.Property = TownServiceProperty.Transform;
                layout.Numbers = new float[18];
                layout.Numbers[6] = layout.Numbers[7] = layout.Numbers[8] = layout.Numbers[9] = 1f;
                layout.Numbers[10] = liveRect.anchorMin.x; layout.Numbers[11] = liveRect.anchorMin.y;
                layout.Numbers[12] = liveRect.anchorMax.x; layout.Numbers[13] = liveRect.anchorMax.y;
                layout.Numbers[14] = liveRect.pivot.x; layout.Numbers[15] = liveRect.pivot.y;
                layout.Numbers[16] = liveRect.rect.width; layout.Numbers[17] = liveRect.rect.height;
                bool changedLayout = !SameRootLayout(originalRect.Numbers, layout.Numbers)
                    || source.Slots.TryGetValue(layout.Key, out MotionSlot? oldLayout)
                        && !SameRootLayout(oldLayout.Entry.Numbers, layout.Numbers);
                if (changedLayout) UpdateMotionSlot(source, layout);
                if (source.Slots.TryGetValue(layout.Key, out MotionSlot? returnLayout))
                { returnLayout.ReturnLayout = true; if (cardFlight != null) returnLayout.Dirty = true; }
            }
            source.Previous = frame; source.HandRevision = _motionHandRevision;
            foreach (MotionSlot slot in source.Slots.Values)
                if (slot.Dirty || now - slot.SentAt >= TownServiceMotionCodec.Heartbeat)
                    AddMotionWaiting(slot, source);
        }
    }
    private static bool NativeReturnFinished(TownServiceMotionEntry entry) => entry.Numbers.Length == 38
        && (entry.Numbers[2] == 1f ? entry.Numbers[1] <= TownServiceMotionCodec.SendInterval * 1.5f
            : entry.Numbers[0] >= entry.Numbers[1] - TownServiceMotionCodec.SendInterval * 1.5f);
    private static bool NativeReturnAvailable(Component native) => native is VRCard card ? card.Holder == null
        : native is ItemsPile.ItemChip chip && chip.Holder == null && !chip.TownOffering;
    private static bool SameRootLayout(float[] before, float[] after)
    {
        if (before.Length != 18 || after.Length != 18) return false;
        for (int i = 10; i < 18; i++) if (before[i] != after[i]) return false;
        return true;
    }

    private static void AddMotionWaiting(MotionSlot slot, SourceMotion source) =>
        (slot.Dirty && source.Live ? MotionLive : slot.Dirty && (source.VisibleFan || slot.VisibilityTransition) ? MotionVisibleFan : MotionWaiting).Add(slot);

    private static bool MotionOffering(Transform source)
    {
        for (Transform? node = source; node != null; node = node.parent)
            if (MotionOfferings.Contains(node)) return true;
        return false;
    }

    private static bool LiveMotion(string address, byte hand) => hand is 1 or 2
        || address.StartsWith("ritual.purse.held|", StringComparison.Ordinal)
        || address.StartsWith("merchant.heldstock", StringComparison.Ordinal)
        || address.StartsWith("merchant.category.", StringComparison.Ordinal)
        || address.StartsWith("merchant.crank|", StringComparison.Ordinal)
        || address.StartsWith("enchant.", StringComparison.Ordinal)
        || address.StartsWith("item.confirm", StringComparison.Ordinal)
        || address.StartsWith("enhance.confirm", StringComparison.Ordinal)
        || address.StartsWith("face.", StringComparison.Ordinal)
        || address.StartsWith("map.cardbody", StringComparison.Ordinal);

    private static VRHand? MotionHand(Transform source, out bool followsRotation)
    {
        followsRotation = true;
        for (Transform? node = source; node != null; node = node.parent)
            if (MotionHands.TryGetValue(node, out MotionHandReference? reference) && reference.Hand != null)
            { followsRotation = reference.FollowsRotation; return reference.Hand; }
        VRHand? left = VRHands.Left, right = VRHands.Right;
        if (left != null && source.IsChildOf(left.Rig.GrabAnchor)) return left;
        if (right != null && source.IsChildOf(right.Rig.GrabAnchor)) return right;
        return null;
    }
    private static float[] ReadMotionHandPose(Transform source, VRHand hand)
    {
        // Local Rig.Root includes the owner's visual hand-style scale. RemoteAvatar's
        // interpolated holder carries its position/rotation but only WorldScale; the
        // style scale lives on a separate visual child. Using InverseTransformPoint
        // on Rig.Root therefore enlarged both purse offsets and size by 1/.62 for
        // Plate/Arcane hands in Build620. Attachment sockets are already compensated.
        // Author against that same compensated holder frame for every style/scale.
        Transform root = hand.Rig.Root;
        float scale = Mathf.Max(.0001f, Mathf.Abs(hand.WorldScale));
        Quaternion inverse = Quaternion.Inverse(root.rotation);
        Vector3 position = inverse * (source.position - root.position) / scale;
        Quaternion rotation = inverse * source.rotation;
        Vector3 size = source.lossyScale / scale;
        return new[] { position.x, position.y, position.z, rotation.x, rotation.y,
            rotation.z, rotation.w, size.x, size.y, size.z };
    }
    private static TownServiceMotionEntry MotionHeader(TownServiceFrame frame, byte lane, byte kind) => new()
    { Kind = kind, Lane = lane, Service = frame.Service, Session = frame.Session, PublicClaim = frame.PublicClaim,
      Module = frame.Module, Structure = frame.Structure };
    private static bool SameNumbers(float[] a, float[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static bool SameMotion(TownServiceMotionEntry a, TownServiceMotionEntry b) => a.Kind == b.Kind
        && a.Session == b.Session && a.Structure == b.Structure && a.PublicClaim == b.PublicClaim
        && a.ParentModule == b.ParentModule && a.Binding == b.Binding && a.Property == b.Property && a.Offset == b.Offset
        && a.ParentAlpha == b.ParentAlpha && a.Visible == b.Visible && a.Hand == b.Hand && a.Revision == b.Revision
        && a.OfferedModule == b.OfferedModule && a.OfferedStructure == b.OfferedStructure && a.OfferedBinding == b.OfferedBinding
        && a.OfferedLocalScale == b.OfferedLocalScale
        && a.HasCanvasFrame == b.HasCanvasFrame && a.CanvasOnHand == b.CanvasOnHand && a.CanvasSortingOrder == b.CanvasSortingOrder
        && a.CanvasSortingLayer == b.CanvasSortingLayer && SameNumbers(a.Pose, b.Pose)
        && SameNumbers(a.Numbers, b.Numbers) && SameNumbers(a.CanvasPose, b.CanvasPose)
        && SameNumbers(a.CanvasRect, b.CanvasRect) && SameNumbers(a.CanvasSettings, b.CanvasSettings);
    private static void UpdateMotionSlot(SourceMotion source, TownServiceMotionEntry entry)
    {
        if (!source.Slots.TryGetValue(entry.Key, out MotionSlot? slot))
        { source.Slots[entry.Key] = new MotionSlot { Entry = entry, DirtySince = Time.unscaledTime }; return; }
        if (SameMotion(slot.Entry, entry))
        {
            if (entry.Kind == 1 && entry.HasCanvasUpdate) slot.Entry = entry;
            // A sampled native pause is meaningful clock data, even though its
            // age/pose did not change. Publish it at the unchanged15-Hz cadence.
            if (entry.Kind == 8) { if (!slot.Dirty) slot.DirtySince = Time.unscaledTime; slot.Dirty = true; }
            return;
        }
        bool visibilityChanged = entry.Kind == 1
            && (slot.Entry.Visible != entry.Visible || slot.Entry.ParentAlpha != entry.ParentAlpha);
        slot.VisibilityTransition = visibilityChanged || slot.Dirty && slot.VisibilityTransition;
        slot.Entry = entry; if (!slot.Dirty) slot.DirtySince = Time.unscaledTime; slot.Dirty = true;
    }

    internal static bool ReceiveMotion(int peer, TownServiceMotionPacket packet)
    {
        if (peer <= 0 || packet == null) return false;
        if (!MotionPeers.TryGetValue(peer, out PeerMotion? state))
        { if (MotionPeers.Count >= 8) return false; state = new PeerMotion(); MotionPeers.Add(peer, state); }
        foreach (TownServiceMotionEntry entry in packet.Entries)
        {
            if (entry.Kind == 10)
            { ReceiveReturnCohort(peer, state, packet, entry); continue; }
            if (entry.Kind == 3)
            { ObserveTempleDonationCommit(peer, entry.Session, entry.Revision, entry.CommitAge); continue; }
            if (entry.Kind == 5)
            { if (packet.Sequence <= state.CueSequence) continue; state.CueSequence = packet.Sequence;
              TownServiceSharedCue.ObserveVisitor(peer, entry.Session, entry.CueReady, entry.CueStrength);
              if (entry.HasSharedCue) TownServiceSharedCue.ObserveShared(peer, entry.SharedCueReady, entry.SharedCueStrength, entry.SharedGuideOwner); continue; }
            if (entry.Kind == 6)
            {
                if (packet.Sequence <= state.MerchantReadySequence) continue;
                state.MerchantReadySequence = packet.Sequence;
                TownServiceSharedCue.ObserveMerchantVisitor(peer, entry.Session, entry.CueReady);
                continue;
            }
            if (StageReturnRoot(state, packet, entry)) continue;
            if (state.Slots.TryGetValue(entry.Key, out MotionSlot? old) && packet.Sequence <= old.ReceivedSequence) continue;
            CancelReturnCohorts(peer, state, packet, entry);
            if (state.Slots.Count >= 3 * TownServiceFrame.MaxModules * 4 && !state.Slots.ContainsKey(entry.Key)) continue;
            // A compact pose preserves the last full canvas update; repeating it at
            // one-second heartbeats also recovers a lost independent event.
            if (entry.Kind == 1 && !entry.HasCanvasUpdate && old != null && old.Entry.Kind == 1
                && old.Entry.Session == entry.Session && old.Entry.Structure == entry.Structure
                && old.Entry.PublicClaim == entry.PublicClaim && old.Entry.HasCanvasUpdate)
            { TownServiceMotionEntry canvas = old.Entry; entry.HasCanvasUpdate = true; entry.HasCanvasFrame = canvas.HasCanvasFrame; entry.CanvasOnHand = canvas.CanvasOnHand;
              entry.CanvasPose = canvas.CanvasPose; entry.CanvasRect = canvas.CanvasRect; entry.CanvasSettings = canvas.CanvasSettings;
              entry.CanvasSortingOrder = canvas.CanvasSortingOrder; entry.CanvasSortingLayer = canvas.CanvasSortingLayer; }
            float receivedAt = Time.unscaledTime;
            CardReturnClock? returnClock = null;
            if (entry.Kind == 8)
            {
                bool sameReturn = old?.ReturnClock != null && old.Entry.Session == entry.Session
                    && old.Entry.Service == entry.Service && old.Entry.PublicClaim == entry.PublicClaim
                    && old.Entry.Structure == entry.Structure && old.Entry.Revision == entry.Revision
                    && old.Entry.Hand == entry.Hand;
                returnClock = sameReturn ? old!.ReturnClock
                    : new CardReturnClock(entry, packet.SampleTime, entry.HasReturnVisibility ? entry.CohortOffset : ReturnOffset(state, packet.SampleTime, receivedAt));
                if (sameReturn) returnClock!.Observe(entry, packet.SampleTime, receivedAt);
                else if (VRLog.WantsDebug && receivedAt >= state.ReturnReportAt)
                {
                    state.ReturnReportAt = receivedAt + 5f;
                    VRLog.Debug("TownMotion", $"Native return clock peer={peer} lane={entry.Lane} module={entry.Module}"
                        + $" revision={entry.Revision} curve={entry.Numbers[2]:F0} age={entry.Numbers[0]:F3}s"
                        + $" lifetime={entry.Numbers[1]:F3}s source={packet.SampleTime:F3}s offset={state.ReturnOffset:F3}s.");
                }
            }
            state.Slots[entry.Key] = new MotionSlot { Entry = entry, ReceivedSequence = packet.Sequence,
                SampleTime = packet.SampleTime, ReceivedAt = receivedAt, ReturnClock = returnClock };
        }
        if (VRLog.WantsDebug) { _motionReceived++; _motionReceivedBytes += 21;
          foreach (TownServiceMotionEntry entry in packet.Entries) _motionReceivedBytes += TownServiceMotionCodec.EntryBytes(entry); }
        return true;
    }

    private static float ReturnOffset(PeerMotion peer, float sourceTime, float now)
    {
        // A native card's detached front/body can start on separate datagrams.
        // Their child geometry remains separate, but all live returns from this
        // source owner share one clock mapping. Revision IDs alone are not a
        // global card identity and must never merge two cards' native receipts.
        if (peer.HasReturnOffset)
            foreach (MotionSlot slot in peer.Slots.Values)
                if (slot.Entry.Kind == 8 && LiveCardReturn(slot, now)) return peer.ReturnOffset;
        peer.HasReturnOffset = true;
        return peer.ReturnOffset = now - sourceTime;
    }

    internal static void ApplyRemoteMotion(float now)
    {
        ActivateReturnCohorts(now);
        foreach (RemoteMotion frame in MotionRemoteFrames.Values) frame.Slots.Clear();
        foreach (var pair in MotionPeers)
        {
            PeerMotion peer = pair.Value; MotionRemoval.Clear();
            foreach (var slotPair in peer.Slots)
            {
                MotionSlot slot = slotPair.Value; TownServiceMotionEntry entry = slot.Entry;
                if (now - slot.ReceivedAt > NetProtocol.StaleTimeoutSeconds)
                { MotionRemoval.Add(slotPair.Key); continue; }
                bool liveCardReturn = entry.Kind == 8 && LiveCardReturn(slot, now);
                bool liveReturnRoot = entry.Kind == 1 && entry.HasReturnVisibility
                    && peer.Slots.TryGetValue(new TownServiceMotionKey(8, entry.Lane, entry.Module, 0, 0, 0), out MotionSlot? exactFlight)
                    && exactFlight.Entry.HasReturnVisibility && exactFlight.ReceivedSequence == slot.ReceivedSequence
                    && exactFlight.SampleTime == slot.SampleTime && LiveCardReturn(exactFlight, now);
                int key = pair.Key;
                if (entry.Lane == 1) key = -key;
                else if (entry.Lane == 2 && !TryStockPeerKey(key, out key)) continue;
                if (!Sessions.TryGetValue(key, out TownServiceSessionInfo? session) || !session.Active
                    || session.Session != entry.Session || session.Service != entry.Service
                    || session.PublicClaim != entry.PublicClaim || Array.BinarySearch(session.Modules, entry.Module) < 0
                    || !Remote.TryGetValue(key, out Dictionary<ushort, RemoteModule>? modules)
                    || !modules.TryGetValue(entry.Module, out RemoteModule? module) || !module.Alive
                    || module.LastFrame == null || module.LastFrame.Structure != entry.Structure
                    // The exact print affinity is not present in an artwork
                    // header. A newer identical-original heartbeat cannot erase
                    // this still-current independent geometric registration.
                    // The same native return can outlive a newer caption/artwork
                    // sample. Its validated endpoints/revision are independent
                    // of those property timestamps, just like print affinity.
                    // Session, structure, census and its bounded lifetime still
                    // guard it; a true withdrawal never retains a visible clone.
                    || entry.Kind != 9 && !liveCardReturn && !liveReturnRoot && slot.SampleTime < module.LastFrame.SampleTime) continue;
                if (entry.Kind == 2 && entry.Property == TownServiceProperty.Transform
                    && entry.Binding == module.Binding.Bindings[0]
                    && StagedReturnLayout(pair.Key, module, slot.SampleTime, now)) continue;
                if (!MotionRemoteFrames.TryGetValue(module, out RemoteMotion? composed))
                { composed = new RemoteMotion(); MotionRemoteFrames.Add(module, composed); }
                composed.Owner = RealPeer(key);
                composed.Slots.Add(slot);
            }
            foreach (TownServiceMotionKey key in MotionRemoval) peer.Slots.Remove(key);
        }
        MotionFrameRemoval.Clear();
        foreach (var pair in MotionRemoteFrames)
        {
            RemoteModule module = pair.Key; RemoteMotion composed = pair.Value;
            if (!module.Alive || composed.Slots.Count == 0)
            { MotionFrameRemoval.Add(module); continue; }
            bool changed = !ReferenceEquals(composed.Author, module.LastFrame);
            foreach (MotionSlot slot in composed.Slots) changed |= slot.Dirty && slot.Entry.Kind != 9;
            MotionSlot? root = null, flight = null;
            foreach (MotionSlot slot in composed.Slots)
            { if (slot.Entry.Kind == 1) root = slot; else if (slot.Entry.Kind is 7 or 8) flight = slot; }
            if (flight?.Entry.HasReturnVisibility == true
                && (root == null || root.ReceivedSequence != flight.ReceivedSequence || root.SampleTime != flight.SampleTime)) root = null;
            if (PendingReturnModule(composed.Owner, module)) continue;
            if (changed)
            {
                try
                {
                    // Compose all changed original properties before one binding pass;
                    // applying a property at a time restored its siblings' old numbers.
                    TownServiceFrame frame = TownServiceDelta.Copy(module.LastFrame!);
                    foreach (MotionSlot slot in composed.Slots)
                    { frame.SampleTime = Mathf.Max(frame.SampleTime, slot.SampleTime);
                      if (slot.Entry.Kind is 2 or 4) PatchMotionProperty(frame, slot.Entry); }
                    if (root != null) PatchMotionRootFrame(frame, root.Entry);
                    if (flight?.Entry.HasReturnVisibility == true)
                    { frame.Visible = flight.Entry.Visible; frame.ParentAlpha = flight.Entry.ParentAlpha; }
                    module.Binding.Validate(frame, Assets);
                    float sampleInterval = composed.LastSampleTime >= 0f && frame.SampleTime > composed.LastSampleTime
                        ? frame.SampleTime - composed.LastSampleTime : TownServiceMotionCodec.SendInterval;
                    module.Motion.Tick(now);
                    PrepareHandMotion(module, composed, root, now, sampleInterval);
                    PrepareOfferedRootMotion(module, composed, root, now);
                    module.Motion.BeforeApply(now);
                    module.Binding.Apply(frame, Assets);
                    if (flight?.Entry.HasReturnVisibility == true)
                    { module.Host.GetComponent<CanvasGroup>().alpha = frame.ParentAlpha; module.Host.SetActive(frame.Visible); }
                    if (root != null) ApplyMotionRoot(module, root.Entry, composed, frame, now);
                    module.Motion.AfterApply(now, sampleInterval, sparseFan: root != null && root.Entry.Hand > 2, sourceSampleTime: frame.SampleTime);
                    if (composed.HadCardReturn && flight == null && root != null)
                        module.Motion.AdoptExternalRootPose(applyTarget: true);
                    TownServiceDepthOrder.Refresh(module.Host.transform);
                    composed.LastSampleTime = frame.SampleTime;
                    composed.Author = module.LastFrame; composed.Merged = frame;
                    foreach (MotionSlot slot in composed.Slots) slot.Dirty = false;
                }
                catch (Exception error) { Report("fast motion module " + module.LastFrame!.Module, error); }
            }
            // A held root consumes the same approved interpolated rig holder on
            // every render frame, rather than replaying an old shared-world pose.
            if (root != null && root.Entry.Hand != 0 && composed.Merged != null
                && root.Entry.Visible && root.Entry.ParentAlpha > 0f && module.Host.activeInHierarchy)
            {
                ApplyMotionRoot(module, root.Entry, composed, composed.Merged, now, continuousHand: true);
                if (flight != null && flight.Entry.Kind == 7 && flight.Entry.Hand == root.Entry.Hand)
                    ApplyReturnMotion(module, flight, composed, composed.Merged, now);
            }
            // Native hover/selection and ring pulses are independent child samples.
            // They must never restart the physical offered card's facing tween.
            if (root != null && composed.Merged != null && composed.OfferedRootSequence != 0
                && root.Entry.Visible && root.Entry.ParentAlpha > 0f && module.Host.activeInHierarchy)
                ApplyOfferedRootMotion(module, composed, composed.Merged, now);
            // Card returns use the same actual local easing, endpoint and original child
            // geometry every render frame. They cannot depend on a held-hand root branch:
            // cabinet and ability returns are explicitly shared-map anchored.
            if (flight != null && flight.Entry.Kind == 8 && composed.Merged != null
                && (root == null || root.Entry.Hand == flight.Entry.Hand)
                && composed.Merged.Visible && composed.Merged.ParentAlpha > 0f
                && module.Host.activeInHierarchy)
                ApplyCardReturnMotion(module, flight, composed, composed.Merged, now);
            composed.HadCardReturn = flight?.Entry.Kind == 8;
            // This continuous path writes only rig transforms. The registered
            // furniture anchor already measures those live transforms in its own
            // distance tick; reassert native sorting only after binding above.
        }
        ApplyOfferedFrames(now);
        RestorePendingReturnPictures();
        foreach (RemoteModule removed in MotionFrameRemoval) MotionRemoteFrames.Remove(removed);
        MotionDiagnostics(now);
    }

    private static void ApplyReturnMotion(RemoteModule module, MotionSlot sample, RemoteMotion motion,
        TownServiceFrame authored, float now)
    {
        TownServiceMotionEntry entry = sample.Entry;
        float age = entry.Numbers[0] + Mathf.Max(0f, now - sample.ReceivedAt);
        if (age > entry.Numbers[1] + .25f) return;
        if (!NetAvatarDriver.TryGetTownMotionHand(motion.Owner, (byte)((entry.Hand - 1) & 1),
                out Transform? hand) || hand == null) return;
        Transform? shared = SharedFrameForRemote?.Invoke(motion.Owner); if (shared == null) return;
        Transform root = module.AddedCanvas != null && !authored.HasCanvasFrame
            ? module.Host.transform : module.Binding.Root;
        TownServiceReturnMotion.Apply(root, hand, shared, entry.Numbers, age);
        if (module.AddedCanvas != null && !authored.HasCanvasFrame) NormalizeDetachedRoot(module);
    }

    private static void ApplyCardReturnMotion(RemoteModule module, MotionSlot sample, RemoteMotion motion,
        TownServiceFrame authored, float now)
    {
        TownServiceMotionEntry entry = CardReturnSample(sample, now, out float age);
        if (age > entry.Numbers[1] + .25f) return;
        Transform? shared = SharedFrameForRemote?.Invoke(motion.Owner); if (shared == null) return;
        Transform holder = shared;
        if (entry.Hand != 0)
        {
            if (!NetAvatarDriver.TryGetTownMotionHand(motion.Owner, (byte)((entry.Hand - 1) & 1),
                out Transform? hand) || hand == null) return;
            holder = hand;
        }
        Transform root = module.AddedCanvas != null && !authored.HasCanvasFrame
            ? module.Host.transform : module.Binding.Root;
        TownCardReturnMotion.Apply(root, holder, shared, entry.Hand, entry.Numbers, age);
        if (module.AddedCanvas != null && !authored.HasCanvasFrame) NormalizeDetachedRoot(module);
        module.Motion.AdoptExternalRootPose();
    }

    private static TownServiceMotionEntry CardReturnSample(MotionSlot sample, float now, out float age)
    {
        if (sample.ReturnClock != null) return sample.ReturnClock.Current(now, out age);
        age = sample.Entry.Numbers[0] + Mathf.Max(0f, now - sample.ReceivedAt);
        return sample.Entry;
    }

    private static bool LiveCardReturn(MotionSlot sample, float now)
    {
        TownServiceMotionEntry entry = CardReturnSample(sample, now, out float age);
        return age <= entry.Numbers[1] + .25f;
    }

    private static TownServiceFrame? EffectiveRemoteFrame(RemoteModule module) =>
        MotionRemoteFrames.TryGetValue(module, out RemoteMotion? composed)
            && ReferenceEquals(composed.Author, module.LastFrame) ? composed.Merged ?? module.LastFrame : module.LastFrame;

    private static void PatchMotionRootFrame(TownServiceFrame frame, TownServiceMotionEntry entry)
    {
        frame.ParentModule = entry.ParentModule; frame.ParentBinding = entry.Binding;
        frame.ParentAlpha = entry.ParentAlpha; frame.Visible = entry.Visible; frame.Pose = entry.Pose;
        if (!entry.HasCanvasUpdate) return;
        frame.HasCanvasFrame = entry.HasCanvasFrame; frame.CanvasPose = entry.CanvasPose;
        frame.CanvasRect = entry.CanvasRect; frame.CanvasSettings = entry.CanvasSettings;
        frame.CanvasSortingLayer = entry.CanvasSortingLayer; frame.CanvasSortingOrder = entry.CanvasSortingOrder;
    }

    private static void PrepareHandMotion(RemoteModule module, RemoteMotion motion, MotionSlot? root, float now, float interval)
    {
        if (root == null) return;
        if (root.Entry.Hand < 3) { motion.HandSequence = 0; return; }
        if (motion.HandSequence == root.ReceivedSequence) return;
        if (!NetAvatarDriver.TryGetTownMotionHand(motion.Owner, (byte)((root.Entry.Hand - 1) & 1), out Transform? hand) || hand == null) return;
        // The rendered holder may move between events, and the native world tween
        // restores its previous target before binding. Preserve the actual current
        // relative interpolation instead of sampling either world transform.
        float blend = motion.HandDuration > 0f ? Mathf.Clamp01((now - motion.HandStarted) / motion.HandDuration) : 1f;
        Vector3 target = Position(root.Entry.Pose);
        Vector3 canvasTarget = root.Entry.HasCanvasFrame ? Position(root.Entry.CanvasPose) : Vector3.zero;
        motion.HandFrom = motion.HandSequence == 0 ? target : Vector3.LerpUnclamped(motion.HandFrom, motion.HandTarget, blend);
        motion.CanvasFrom = motion.HandSequence == 0 ? canvasTarget : Vector3.LerpUnclamped(motion.CanvasFrom, motion.CanvasTarget, blend);
        motion.HandTarget = target; motion.CanvasTarget = canvasTarget;
        motion.HandDuration = motion.HandSequence == 0 ? 0f : Mathf.Clamp(interval * 1.1f, 1f / 90f, 1.5f);
        motion.HandSequence = root.ReceivedSequence; motion.HandStarted = now;
    }

    private static void PrepareOfferedRootMotion(RemoteModule module, RemoteMotion motion, MotionSlot? sample, float now)
    {
        if (sample == null || sample.Entry.Hand != 0
            || sample.Entry.ParentModule != TownServiceFrame.ManifestModule
            || module.LastFrame!.Service != 3
            || !(module.Address.StartsWith("face.", StringComparison.Ordinal)
                || module.Address.StartsWith("enchant.holder", StringComparison.Ordinal)))
        { motion.OfferedRootSequence = 0; return; }
        if (motion.OfferedRootSequence == sample.ReceivedSequence) return;
        Transform? shared = SharedFrameForRemote?.Invoke(motion.Owner);
        if (shared == null || !ValidMotionScale(shared.lossyScale)) return;
        Transform target = module.AddedCanvas != null && !module.LastFrame.HasCanvasFrame
            ? module.Host.transform : module.Binding.Root;
        if (!ValidMotionScale(target.lossyScale)) return;
        float blend = motion.OfferedDuration > 0f
            ? Mathf.Clamp01((now - motion.OfferedStarted) / motion.OfferedDuration) : 1f;
        motion.OfferedFrom = motion.OfferedRootSequence == 0 ? shared.InverseTransformPoint(target.position)
            : Vector3.LerpUnclamped(motion.OfferedFrom, motion.OfferedTarget, blend);
        motion.OfferedRotationFrom = motion.OfferedRootSequence == 0 ? Quaternion.Inverse(shared.rotation) * target.rotation
            : Quaternion.SlerpUnclamped(motion.OfferedRotationFrom, motion.OfferedRotationTarget, blend);
        motion.OfferedScaleFrom = motion.OfferedRootSequence == 0
            ? DivideMotionScale(target.lossyScale, shared.lossyScale)
            : Vector3.LerpUnclamped(motion.OfferedScaleFrom, motion.OfferedScaleTarget, blend);
        motion.OfferedTarget = Position(sample.Entry.Pose);
        motion.OfferedRotationTarget = Rotation(sample.Entry.Pose);
        motion.OfferedScaleTarget = Scale(sample.Entry.Pose);
        float interval = motion.OfferedSampleTime >= 0f && sample.SampleTime > motion.OfferedSampleTime
            ? sample.SampleTime - motion.OfferedSampleTime : TownServiceMotionCodec.SendInterval;
        motion.OfferedDuration = Mathf.Clamp(interval * 1.1f, 1f / 90f, .25f);
        motion.OfferedStarted = now; motion.OfferedSampleTime = sample.SampleTime;
        motion.OfferedRootSequence = sample.ReceivedSequence;
    }

    private static Vector3 DivideMotionScale(Vector3 value, Vector3 divisor) => new(
        value.x / divisor.x, value.y / divisor.y, value.z / divisor.z);

    private static bool ValidMotionScale(Vector3 scale) => ValidMotionScaleAxis(scale.x)
        && ValidMotionScaleAxis(scale.y) && ValidMotionScaleAxis(scale.z);
    private static bool ValidMotionScaleAxis(float value) => !float.IsNaN(value) && !float.IsInfinity(value)
        && Mathf.Abs(value) >= .0000001f && Mathf.Abs(value) <= 100000f;

    private static void ApplyOfferedRootMotion(RemoteModule module, RemoteMotion motion, TownServiceFrame authored, float now)
    {
        Transform? shared = SharedFrameForRemote?.Invoke(motion.Owner);
        if (shared == null || !ValidMotionScale(shared.lossyScale)) return;
        Transform root = module.AddedCanvas != null && !authored.HasCanvasFrame ? module.Host.transform : module.Binding.Root;
        if (root.parent == null || !ValidMotionScale(root.parent.lossyScale)) return;
        float blend = Mathf.Clamp01((now - motion.OfferedStarted) / motion.OfferedDuration);
        root.position = shared.TransformPoint(Vector3.LerpUnclamped(motion.OfferedFrom, motion.OfferedTarget, blend));
        root.rotation = shared.rotation * Quaternion.SlerpUnclamped(motion.OfferedRotationFrom, motion.OfferedRotationTarget, blend);
        root.localScale = DivideMotionScale(Vector3.Scale(shared.lossyScale,
            Vector3.LerpUnclamped(motion.OfferedScaleFrom, motion.OfferedScaleTarget, blend)), root.parent.lossyScale);
        if (module.AddedCanvas != null && !authored.HasCanvasFrame) NormalizeDetachedRoot(module);
    }

    private static void ApplyMotionRoot(RemoteModule module, TownServiceMotionEntry entry, RemoteMotion motion,
        TownServiceFrame authored, float now, bool continuousHand = false)
    {
        int owner = motion.Owner, key = owner;
        if (entry.Lane == 1) key = -key;
        else if (entry.Lane == 2 && !TryStockPeerKey(owner, out key)) return;
        if (owner == 0 || !Remote.TryGetValue(key, out Dictionary<ushort, RemoteModule>? modules)) return;
        Transform? shared = SharedFrameForRemote?.Invoke(owner); if (shared == null) return;
        Transform mount = shared;
        if (entry.Hand != 0)
        { if (!NetAvatarDriver.TryGetTownMotionHand(owner, (byte)((entry.Hand - 1) & 1), out Transform? hand) || hand == null) return; mount = hand; }
        else if (entry.ParentModule != TownServiceFrame.ManifestModule)
        {
            if (!modules.TryGetValue(entry.ParentModule, out RemoteModule? parent) || !parent.Alive) return;
            int index = Array.IndexOf(parent.Binding.Bindings, entry.Binding); if (index < 0) return;
            mount = parent.Binding.Nodes[index];
        }
        // Materialization may temporarily collapse a shared/canvas parent. Do
        // not divide through a singular frame or overwrite a last valid pose.
        Transform selectedRoot = module.AddedCanvas != null && !authored.HasCanvasFrame ? module.Host.transform : module.Binding.Root;
        if (!ValidMotionScale(shared.lossyScale) || !ValidMotionScale(mount.lossyScale)
            || selectedRoot.parent == null
            || authored.HasCanvasFrame && !ValidMotionScale(Scale(authored.CanvasPose))) return;
        float blend = motion.HandDuration > 0f ? Mathf.Clamp01((now - motion.HandStarted) / motion.HandDuration) : 1f;
        if (authored.HasCanvasFrame && module.AddedCanvas != null)
        {
            if (!continuousHand)
            { ApplyCanvasFrame(module, authored, entry.CanvasOnHand ? mount : shared);
              if (entry.CanvasOnHand && entry.Hand > 2) module.Host.transform.rotation = shared.rotation * Rotation(entry.CanvasPose); }
            else if (entry.CanvasOnHand)
            { module.Host.transform.position = mount.TransformPoint(entry.Hand > 2
                    ? Vector3.LerpUnclamped(motion.CanvasFrom, Position(entry.CanvasPose), blend) : Position(entry.CanvasPose));
              if (entry.Hand <= 2) module.Host.transform.rotation = mount.rotation * Rotation(entry.CanvasPose); }
        }
        // A prepared purchase's real canvas starts collapsed. Its valid authored
        // canvas update must restore that parent before the root's scale division
        // can be validated; checking the old parent first stranded its front at0.
        if (selectedRoot.parent == null || !ValidMotionScale(selectedRoot.parent.lossyScale)) return;
        module.Binding.ApplyRootLayout(authored, module.AddedCanvas != null && !authored.HasCanvasFrame);
        Transform root = module.AddedCanvas != null && !authored.HasCanvasFrame ? module.Host.transform : module.Binding.Root;
        root.position = mount.TransformPoint(continuousHand && entry.Hand > 2
            ? Vector3.LerpUnclamped(motion.HandFrom, Position(entry.Pose), blend) : Position(entry.Pose));
        if (!continuousHand || entry.Hand <= 2)
            root.rotation = (entry.Hand > 2 ? shared.rotation : mount.rotation) * Rotation(entry.Pose);
        // Match baseline detached-root normalization defensively. The original world pose
        // belongs to the host, while its native child stays in that one frame. Current
        // producers omit root-local Transform samples; never apply that pose a second time.
        if (module.AddedCanvas != null && !authored.HasCanvasFrame) NormalizeDetachedRoot(module);
        if (continuousHand) return;
        Vector3 world = Vector3.Scale(mount.lossyScale, Scale(entry.Pose)); Vector3 parentScale = root.parent.lossyScale;
        root.localScale = new Vector3(world.x / parentScale.x, world.y / parentScale.y, world.z / parentScale.z);
        module.Host.GetComponent<CanvasGroup>().alpha = entry.ParentAlpha;
        // Explicit owner visibility may close a fully constructed original. Fast
        // packets never supersede rack masks, membership, stock-duplicate masks.
        if (!entry.Visible) module.Host.SetActive(false);
        else if (!module.StockMasked && module.LastFrame!.RackMember == null) module.Host.SetActive(true);
    }

    private static void PatchMotionProperty(TownServiceFrame frame, TownServiceMotionEntry entry)
    {
        foreach (TownServiceNode node in frame.Nodes)
            if (node.Binding == entry.Binding && node.Values.TryGetValue(entry.Property, out TownServiceValue? before))
            {
                float[] numbers = entry.Numbers;
                if (entry.Kind == 4)
                { if (entry.Offset + numbers.Length > before.Numbers.Length) return;
                  numbers = (float[])before.Numbers.Clone(); Array.Copy(entry.Numbers, 0, numbers, entry.Offset, entry.Numbers.Length); }
                node.Values[entry.Property] = new TownServiceValue { Numbers = numbers, Text = before.Text }; return;
            }
    }
    private static void MotionDiagnostics(float now)
    {
        if (!VRLog.WantsDebug || now < _motionDiagnosticAt) return;
        _motionDiagnosticAt = now + 5f;
        if (_motionSent == 0 && _motionReceived == 0) return;
        int dirty = 0, hands = 0, unresolved = 0; float oldest = 0f;
        foreach (SourceMotion source in MotionSources.Values)
            foreach (MotionSlot slot in source.Slots.Values)
            { if (slot.Dirty) { dirty++; oldest = Mathf.Max(oldest, now - slot.DirtySince); }
              if (slot.Entry.Kind == 1 && slot.Entry.Hand != 0) hands++; }
        foreach (PeerMotion peer in MotionPeers.Values)
            foreach (MotionSlot slot in peer.Slots.Values) if (slot.Dirty) unresolved++;
        VRLog.Debug("TownMotion", $"sent={_motionSent}/{_motionSentBytes}B received={_motionReceived}/{_motionReceivedBytes}B dirty={dirty} oldest={oldest:F3}s handRoots={hands} unresolved={unresolved}");
        _motionSent = _motionSentBytes = _motionReceived = _motionReceivedBytes = 0;
    }

    internal static void ForgetRemoteMotion(int peer) { MotionPeers.Remove(peer); TownServiceSharedCue.Forget(peer); }
    internal static void ResetMotionNetwork()
    { ResetReturnCohorts(); MotionSources.Clear(); MotionPeers.Clear(); MotionHands.Clear(); MotionOfferings.Clear(); ClearOfferedFrames(); ActiveOfferedFrames.Clear(); DeadOfferedFrames.Clear(); OfferedRemoteMotion.Clear(); DeadOfferedRemoteMotion.Clear(); OfferedApplyOrder.Clear(); _offeredDiagnosticAt = 0f; MotionReturns.Clear(); CardReturns.Clear(); MotionWaiting.Clear(); MotionLive.Clear(); MotionVisibleFan.Clear(); MotionRemoteFrames.Clear(); TownServiceSharedCue.Reset();
      MotionSourceRemoval.Clear(); MotionRemoval.Clear(); _nextMotionSend = 0f; _motionCursor = _motionLiveCursor = _motionVisibleCursor = 0;
      _motionCommitSession = _motionCommitRevision = 0; _nextMotionCommit = 0f;
      _motionCue = null; _nextMotionCue = 0f;
      _motionMerchantReady = null; _nextMotionMerchantReady = 0f;
      MotionHandRemoval.Clear(); _nextMotionHandCleanup = 0f;
      _motionDiagnosticAt = 0f; _motionSent = _motionSentBytes = _motionReceived = _motionReceivedBytes = 0;
      if (_motionLifetime != uint.MaxValue) _motionLifetime++; }
}
