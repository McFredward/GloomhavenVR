using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class MotionSlot
    {
        internal TownServiceMotionEntry Entry = null!;
        internal bool Dirty = true;
        internal float SentAt = float.NegativeInfinity;
        internal ulong ReceivedSequence;
        internal float SampleTime, ReceivedAt, DirtySince;
    }
    private sealed class SourceMotion
    {
        internal TownServiceFrame? Previous;
        internal float NextCanvasRefresh;
        internal readonly Dictionary<TownServiceMotionKey, MotionSlot> Slots = new();
    }
    private sealed class PeerMotion
    {
        internal readonly Dictionary<TownServiceMotionKey, MotionSlot> Slots = new();
        internal ulong CueSequence;
    }
    private sealed class RemoteMotion
    {
        internal TownServiceFrame? Author;
        internal TownServiceFrame? Merged;
        internal readonly List<MotionSlot> Slots = new();
    }
    private static readonly Dictionary<LocalModule, SourceMotion> MotionSources = new();
    private static readonly Dictionary<int, PeerMotion> MotionPeers = new();
    private sealed class MotionHandReference { internal VRHand Hand = null!; internal bool FollowsRotation; }
    private static readonly Dictionary<Transform, MotionHandReference> MotionHands = new();
    private static readonly List<LocalModule> MotionSourceRemoval = new();
    private static readonly List<MotionSlot> MotionWaiting = new();
    private static readonly List<TownServiceMotionKey> MotionRemoval = new();
    private static readonly Dictionary<RemoteModule, RemoteMotion> MotionRemoteFrames = new();
    private static readonly List<RemoteModule> MotionFrameRemoval = new();
    private static float _nextMotionSend;
    private static ulong _motionSequence;
    private static int _motionCursor;
    private static uint _motionCommitSession, _motionCommitRevision;
    private static float _nextMotionCommit;
    private static TownServiceMotionEntry? _motionCue;
    private static float _nextMotionCue;
    private static uint _motionLifetime = 1;
    private static float _motionDiagnosticAt;
    private static int _motionSent, _motionSentBytes, _motionReceived, _motionReceivedBytes;
    internal static bool FastMotionCaptureEnabled;

    // Exact publisher provenance only. Physical grabs are also found through the
    // existing GrabAnchor ancestry; there is no observer-local nearest-hand guess.
    internal static void RegisterMotionHand(Transform source, VRHand? hand, bool followsRotation = true)
    { if (source == null) return; if (hand == null) MotionHands.Remove(source);
      else MotionHands[source] = new MotionHandReference { Hand = hand, FollowsRotation = followsRotation }; }

    internal static void CaptureMotion(Action<byte[], int, object?> send)
    {
        float now = Time.unscaledTime;
        if (now < _nextMotionSend) return;
        _nextMotionSend = now + TownServiceMotionCodec.SendInterval;
        MotionSourceRemoval.Clear();
        foreach (var pair in MotionSources) MotionSourceRemoval.Add(pair.Key);
        MotionWaiting.Clear();
        CaptureMotionLane(PrivateLane, 0, now); CaptureMotionLane(PublicLane, 1, now); CaptureMotionLane(StockLane, 2, now);
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
        if (MotionWaiting.Count == 0 && commit == null && !sendCue) return;
        var packet = new TownServiceMotionPacket { Sequence = ++_motionSequence, SampleTime = now };
        if (packet.Sequence == 0) { _motionSequence = ulong.MaxValue; return; }
        int size = 21;
        if (commit != null) { packet.Entries.Add(commit); size += TownServiceMotionCodec.EntryBytes(commit); }
        if (sendCue) { packet.Entries.Add(cue); size += TownServiceMotionCodec.EntryBytes(cue);
          _motionCue = cue; _nextMotionCue = now + TownServiceMotionCodec.Heartbeat; }
        // Latest numeric state, never a queue of obsolete poses. Round-robin hot
        // bindings retain a finite turn even during a four-player catalog preload.
        int count = MotionWaiting.Count;
        for (int i = 0; i < count && packet.Entries.Count < TownServiceMotionCodec.MaxEntries; i++)
        {
            if (_motionCursor >= count) _motionCursor = 0;
            MotionSlot slot = MotionWaiting[_motionCursor++];
            int bytes = TownServiceMotionCodec.EntryBytes(slot.Entry);
            if (size + bytes > TownServiceMotionCodec.MaxBytes) continue;
            packet.Entries.Add(slot.Entry); size += bytes; slot.Dirty = false; slot.SentAt = now;
        }
        if (packet.Entries.Count == 0) return;
        byte[] bytesPacket = TownServiceMotionCodec.Write(packet); send(bytesPacket, bytesPacket.Length, packet);
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
            // Artwork contains the initial complete native state. Fast numbers add
            // only bindings which actually change, plus the small live root pose.
            if (previous == null || previous.Structure != frame.Structure || previous.Session != frame.Session
                || previous.PublicClaim != frame.PublicClaim)
                source.Slots.Clear();
            TownServiceMotionEntry root = MotionHeader(frame, laneId, 1);
            root.ParentModule = frame.ParentModule; root.Binding = frame.ParentBinding;
            root.ParentAlpha = frame.ParentAlpha; root.Visible = frame.Visible;
            root.Pose = (float[])frame.Pose.Clone(); root.HasCanvasFrame = frame.HasCanvasFrame;
            root.HasCanvasUpdate = previous == null || now >= source.NextCanvasRefresh || previous.HasCanvasFrame != frame.HasCanvasFrame
                || !SameNumbers(previous.CanvasPose, frame.CanvasPose) || !SameNumbers(previous.CanvasRect, frame.CanvasRect)
                || !SameNumbers(previous.CanvasSettings, frame.CanvasSettings)
                || previous.CanvasSortingLayer != frame.CanvasSortingLayer || previous.CanvasSortingOrder != frame.CanvasSortingOrder;
            if (root.HasCanvasUpdate) source.NextCanvasRefresh = now + TownServiceMotionCodec.Heartbeat;
            root.CanvasPose = (float[])frame.CanvasPose.Clone(); root.CanvasRect = (float[])frame.CanvasRect.Clone();
            root.CanvasSettings = (float[])frame.CanvasSettings.Clone(); root.CanvasSortingLayer = frame.CanvasSortingLayer;
            root.CanvasSortingOrder = frame.CanvasSortingOrder;
            VRHand? hand = MotionHand(module.Binding.Root, out bool followsRotation);
            if (hand != null && hand.HasPose)
            {
                followsRotation &= !module.Address.StartsWith("ritual.purse", StringComparison.Ordinal);
                root.Hand = (byte)((hand.Side == HandSide.Left ? 1 : 2) + (followsRotation ? 0 : 2));
                root.Pose = ReadPose(module.Binding.Root, hand.Rig.Root);
                if (!followsRotation)
                { Quaternion rotation = Quaternion.Inverse(lane.SharedFrame.rotation) * module.Binding.Root.rotation;
                  root.Pose[3] = rotation.x; root.Pose[4] = rotation.y; root.Pose[5] = rotation.z; root.Pose[6] = rotation.w; }
            }
            if (hand != null || module.HighPriority || previous != null && (!SameNumbers(previous.Pose, frame.Pose)
                || previous.ParentAlpha != frame.ParentAlpha || previous.Visible != frame.Visible
                || previous.ParentModule != frame.ParentModule || previous.ParentBinding != frame.ParentBinding
                || !SameNumbers(previous.CanvasPose, frame.CanvasPose)
                || !SameNumbers(previous.CanvasRect, frame.CanvasRect)
                || !SameNumbers(previous.CanvasSettings, frame.CanvasSettings))) UpdateMotionSlot(source, root);
            if (previous != null && previous.Structure == frame.Structure && previous.Nodes.Length == frame.Nodes.Length)
                for (int n = 0; n < frame.Nodes.Length; n++)
                    foreach (var property in frame.Nodes[n].Values)
                    {
                        if ((!TownServiceFastNumbers.IsFast(property.Key) && !TownServiceFastNumbers.IsMaterial(property.Key))
                            || !previous.Nodes[n].Values.TryGetValue(property.Key, out TownServiceValue? before)
                            || SameNumbers(before.Numbers, property.Value.Numbers)) continue;
                        // The root Transform is authored by its original world-frame
                        // pose; never apply the native pixel-root transform twice.
                        if (n == 0 && property.Key == TownServiceProperty.Transform) continue;
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
                        entry.Numbers = (float[])property.Value.Numbers.Clone(); UpdateMotionSlot(source, entry);
                    }
            source.Previous = frame;
            foreach (MotionSlot slot in source.Slots.Values)
                if (slot.Dirty || now - slot.SentAt >= TownServiceMotionCodec.Heartbeat)
                    MotionWaiting.Add(slot);
        }
    }
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
    private static TownServiceMotionEntry MotionHeader(TownServiceFrame frame, byte lane, byte kind) => new()
    { Kind = kind, Lane = lane, Service = frame.Service, Session = frame.Session, PublicClaim = frame.PublicClaim,
      Module = frame.Module, Structure = frame.Structure };
    private static bool SameNumbers(float[] a, float[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static bool SameMotion(TownServiceMotionEntry a, TownServiceMotionEntry b) => a.Kind == b.Kind
        && a.Session == b.Session && a.Structure == b.Structure && a.PublicClaim == b.PublicClaim
        && a.ParentModule == b.ParentModule && a.Binding == b.Binding && a.Property == b.Property && a.Offset == b.Offset
        && a.ParentAlpha == b.ParentAlpha && a.Visible == b.Visible && a.Hand == b.Hand
        && a.HasCanvasFrame == b.HasCanvasFrame && a.HasCanvasUpdate == b.HasCanvasUpdate && a.CanvasSortingOrder == b.CanvasSortingOrder
        && a.CanvasSortingLayer == b.CanvasSortingLayer && SameNumbers(a.Pose, b.Pose)
        && SameNumbers(a.Numbers, b.Numbers) && SameNumbers(a.CanvasPose, b.CanvasPose)
        && SameNumbers(a.CanvasRect, b.CanvasRect) && SameNumbers(a.CanvasSettings, b.CanvasSettings);
    private static void UpdateMotionSlot(SourceMotion source, TownServiceMotionEntry entry)
    {
        if (!source.Slots.TryGetValue(entry.Key, out MotionSlot? slot))
        { source.Slots[entry.Key] = new MotionSlot { Entry = entry, DirtySince = Time.unscaledTime }; return; }
        if (SameMotion(slot.Entry, entry)) return;
        slot.Entry = entry; if (!slot.Dirty) slot.DirtySince = Time.unscaledTime; slot.Dirty = true;
    }

    internal static bool ReceiveMotion(int peer, TownServiceMotionPacket packet)
    {
        if (peer <= 0 || packet == null) return false;
        if (!MotionPeers.TryGetValue(peer, out PeerMotion? state))
        { if (MotionPeers.Count >= 8) return false; state = new PeerMotion(); MotionPeers.Add(peer, state); }
        foreach (TownServiceMotionEntry entry in packet.Entries)
        {
            if (entry.Kind == 3)
            { ObserveTempleDonationCommit(peer, entry.Session, entry.Revision, entry.CommitAge); continue; }
            if (entry.Kind == 5)
            { if (packet.Sequence <= state.CueSequence) continue; state.CueSequence = packet.Sequence;
              TownServiceSharedCue.ObserveVisitor(peer, entry.Session, entry.CueReady, entry.CueStrength);
              if (entry.HasSharedCue) TownServiceSharedCue.ObserveShared(peer, entry.SharedCueReady, entry.SharedCueStrength, entry.SharedGuideOwner); continue; }
            if (state.Slots.TryGetValue(entry.Key, out MotionSlot? old) && packet.Sequence <= old.ReceivedSequence) continue;
            if (state.Slots.Count >= 3 * TownServiceFrame.MaxModules * 4 && !state.Slots.ContainsKey(entry.Key)) continue;
            // A compact pose preserves the last full canvas update; repeating it at
            // one-second heartbeats also recovers a lost independent event.
            if (entry.Kind == 1 && !entry.HasCanvasUpdate && old != null && old.Entry.Kind == 1
                && old.Entry.Session == entry.Session && old.Entry.Structure == entry.Structure
                && old.Entry.PublicClaim == entry.PublicClaim && old.Entry.HasCanvasUpdate)
            { TownServiceMotionEntry canvas = old.Entry; entry.HasCanvasUpdate = true; entry.HasCanvasFrame = canvas.HasCanvasFrame;
              entry.CanvasPose = canvas.CanvasPose; entry.CanvasRect = canvas.CanvasRect; entry.CanvasSettings = canvas.CanvasSettings;
              entry.CanvasSortingOrder = canvas.CanvasSortingOrder; entry.CanvasSortingLayer = canvas.CanvasSortingLayer; }
            state.Slots[entry.Key] = new MotionSlot { Entry = entry, ReceivedSequence = packet.Sequence,
                SampleTime = packet.SampleTime, ReceivedAt = Time.unscaledTime };
        }
        if (VRLog.WantsDebug) { _motionReceived++; _motionReceivedBytes += 21;
          foreach (TownServiceMotionEntry entry in packet.Entries) _motionReceivedBytes += TownServiceMotionCodec.EntryBytes(entry); }
        return true;
    }

    internal static void ApplyRemoteMotion(float now)
    {
        foreach (RemoteMotion frame in MotionRemoteFrames.Values) frame.Slots.Clear();
        foreach (var pair in MotionPeers)
        {
            PeerMotion peer = pair.Value; MotionRemoval.Clear();
            foreach (var slotPair in peer.Slots)
            {
                MotionSlot slot = slotPair.Value; TownServiceMotionEntry entry = slot.Entry;
                if (now - slot.ReceivedAt > NetProtocol.StaleTimeoutSeconds)
                { MotionRemoval.Add(slotPair.Key); continue; }
                int key = pair.Key;
                if (entry.Lane == 1) key = -key;
                else if (entry.Lane == 2 && !TryStockPeerKey(key, out key)) continue;
                if (!Sessions.TryGetValue(key, out TownServiceSessionInfo? session) || !session.Active
                    || session.Session != entry.Session || session.Service != entry.Service
                    || session.PublicClaim != entry.PublicClaim || Array.BinarySearch(session.Modules, entry.Module) < 0
                    || !Remote.TryGetValue(key, out Dictionary<ushort, RemoteModule>? modules)
                    || !modules.TryGetValue(entry.Module, out RemoteModule? module) || !module.Alive
                    || module.LastFrame == null || module.LastFrame.Structure != entry.Structure
                    || slot.SampleTime < module.LastFrame.SampleTime) continue;
                if (!MotionRemoteFrames.TryGetValue(module, out RemoteMotion? composed))
                { composed = new RemoteMotion(); MotionRemoteFrames.Add(module, composed); }
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
            foreach (MotionSlot slot in composed.Slots) changed |= slot.Dirty;
            MotionSlot? root = null;
            foreach (MotionSlot slot in composed.Slots) if (slot.Entry.Kind == 1) root = slot;
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
                    module.Binding.Validate(frame, Assets);
                    module.Motion.BeforeApply(now); module.Binding.Apply(frame, Assets);
                    if (root != null) ApplyMotionRoot(module, root.Entry);
                    module.Motion.AfterApply(now, TownServiceMotionCodec.SendInterval);
                    composed.Author = module.LastFrame; composed.Merged = frame;
                    foreach (MotionSlot slot in composed.Slots) slot.Dirty = false;
                }
                catch (Exception error) { Report("fast motion module " + module.LastFrame!.Module, error); }
            }
            // A held root consumes the same approved interpolated rig holder on
            // every render frame, rather than replaying an old shared-world pose.
            if (root != null && root.Entry.Hand != 0) ApplyMotionRoot(module, root.Entry, continuousHand: true);
        }
        foreach (RemoteModule removed in MotionFrameRemoval) MotionRemoteFrames.Remove(removed);
        MotionDiagnostics(now);
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

    private static void ApplyMotionRoot(RemoteModule module, TownServiceMotionEntry entry, bool continuousHand = false)
    {
        int owner = 0, key = 0;
        foreach (var pair in Remote)
            if (pair.Value.TryGetValue(entry.Module, out RemoteModule? candidate) && ReferenceEquals(candidate, module))
            { key = pair.Key; owner = RealPeer(key); break; }
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
        TownServiceFrame authored = TownServiceDelta.Retain(module.LastFrame!);
        authored.ParentAlpha = entry.ParentAlpha; authored.Visible = entry.Visible; authored.Pose = entry.Pose;
        if (entry.HasCanvasUpdate)
        { authored.HasCanvasFrame = entry.HasCanvasFrame; authored.CanvasPose = entry.CanvasPose;
          authored.CanvasRect = entry.CanvasRect; authored.CanvasSettings = entry.CanvasSettings;
          authored.CanvasSortingLayer = entry.CanvasSortingLayer; authored.CanvasSortingOrder = entry.CanvasSortingOrder; }
        if (authored.HasCanvasFrame && module.AddedCanvas != null) ApplyCanvasFrame(module, authored, shared);
        Transform root = module.AddedCanvas != null && !authored.HasCanvasFrame ? module.Host.transform : module.Binding.Root;
        root.position = mount.TransformPoint(Position(entry.Pose));
        if (!continuousHand || entry.Hand <= 2)
            root.rotation = (entry.Hand > 2 ? shared.rotation : mount.rotation) * Rotation(entry.Pose);
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
    { MotionSources.Clear(); MotionPeers.Clear(); MotionHands.Clear(); MotionWaiting.Clear(); MotionRemoteFrames.Clear(); TownServiceSharedCue.Reset();
      MotionSourceRemoval.Clear(); MotionRemoval.Clear(); _nextMotionSend = 0f; _motionCursor = 0;
      _motionCommitSession = _motionCommitRevision = 0; _nextMotionCommit = 0f;
      _motionCue = null; _nextMotionCue = 0f;
      _motionDiagnosticAt = 0f; _motionSent = _motionSentBytes = _motionReceived = _motionReceivedBytes = 0;
      if (_motionLifetime != uint.MaxValue) _motionLifetime++; }
}
