using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Independent bounded module queues inside the existing global event budget.</summary>
internal sealed class TownServiceLaneSendQueue
{
    private readonly Dictionary<ushort, ExtrasSendQueue> _queues = new();
    private readonly List<ushort> _order = new();
    private readonly ulong _seed;
    private readonly Dictionary<ushort, ulong> _sequences = new();
    private int _cursor;
    private int _priorityCursor, _priorityTurns;
    private ushort? _normalActive, _priorityActive;
    private readonly HashSet<ushort> _priority = new();
    private byte[]? _manifestBytes;
    private TownServiceFrame? _manifestFrame;
    private TownServiceFrame? _sentManifest;
    private double _nextManifest;
    private readonly ExtrasSendQueue _bundle;
    private readonly List<TownServiceFrame> _bundleFrames = new();
    private readonly Dictionary<ushort, byte[]> _bundleBytes = new();
    private readonly HashSet<ushort> _promotedBundle = new();
    private uint _session;
    private byte _service;
    internal TownServiceLaneSendQueue(ulong seed)
    {
        _seed = seed;
        _bundle = new ExtrasSendQueue((seed & ~65535UL) | TownServiceFrame.BundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072);
    }
    internal void Enqueue(byte[] bytes, int length, TownServiceFrame frame)
    {
        if (_session != frame.Session || _service != frame.Service)
        { Clear(); _session = frame.Session; _service = frame.Service; }
        if (!_queues.TryGetValue(frame.Module, out ExtrasSendQueue? queue))
        {
            if (_queues.Count >= TownServiceFrame.MaxModules + 1) return;
            ulong sequence = _sequences.TryGetValue(frame.Module, out ulong previous) ? previous : (_seed & ~65535UL) | frame.Module;
            queue = new ExtrasSendQueue(sequence, TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType, preserveFirst: true, snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072);
            _queues.Add(frame.Module, queue); _order.Add(frame.Module);
        }
        if (frame.Module == TownServiceFrame.ManifestModule)
        {
            // Keep one newest census, not four repeated 8 KiB heartbeats in front of
            // every item. The current fragmented census still finishes atomically.
            _manifestBytes = new byte[length]; Buffer.BlockCopy(bytes, 0, _manifestBytes, 0, length);
            _manifestFrame = frame;
            // Obsolete row queues must not keep the last session's catalog in flight forever.
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                ushort id = _order[i];
                if (id == TownServiceFrame.ManifestModule || Array.BinarySearch(frame.Modules, id) >= 0) continue;
                _sequences[id] = _queues[id].Sequence; _queues[id].Clear(); _queues.Remove(id); _order.RemoveAt(i); _priority.Remove(id);
            }
        }
        else
        {
            if (frame.HighPriority)
            {
                _priority.Add(frame.Module);
                // A just-grabbed card may have its first baseline inside a cold bundle.
                // Deliver that exact immutable dependency urgently before its pose delta.
                if (_bundleBytes.TryGetValue(frame.Module, out byte[]? baselineBytes) && _promotedBundle.Add(frame.Module))
                    foreach (TownServiceFrame baseline in _bundleFrames)
                        if (baseline.Module == frame.Module && baseline.BaseSequence == 0)
                        { queue.Enqueue(baselineBytes, baselineBytes.Length, baseline); break; }
            }
            else _priority.Remove(frame.Module);
            queue.Enqueue(bytes, length, frame);
        }
    }
    internal byte[]? Next(double now)
    {
        if (_queues.TryGetValue(TownServiceFrame.ManifestModule, out ExtrasSendQueue? manifest))
        {
            if (!manifest.HasInFlight && _manifestFrame != null && _manifestBytes != null
                && (now >= _nextManifest || _sentManifest == null || !SameCensus(_sentManifest, _manifestFrame)))
            {
                manifest.Enqueue(_manifestBytes, _manifestBytes.Length, _manifestFrame);
                _sentManifest = _manifestFrame; _manifestBytes = null; _manifestFrame = null;
                _nextManifest = now + (_sentManifest.Visible ? 5 : .5);
            }
            byte[]? page = manifest.Next(now);
            if (page != null) return page;
        }
        // Two urgent turns, then one background turn. A held face cannot wait behind
        // thousands of catalog rows; the full catalog cannot starve behind a held card.
        bool normalFirst = _priorityTurns >= 2;
        byte[]? selected = normalFirst ? Take(now, false) : Take(now, true);
        if (selected != null) { _priorityTurns = normalFirst ? 0 : _priorityTurns + 1; return selected; }
        selected = Take(now, normalFirst);
        if (selected != null) _priorityTurns = normalFirst ? _priorityTurns + 1 : 0;
        return selected;
    }
    private byte[]? Take(double now, bool urgent)
    {
        ushort? active = urgent ? _priorityActive : _normalActive;
        if (active.HasValue && _queues.TryGetValue(active.Value, out ExtrasSendQueue? running) && running.HasInFlight)
        {
            byte[]? page = running.Next(now);
            if (page != null) { Completed(running); return page; }
            return null;
        }
        if (urgent) _priorityActive = null; else _normalActive = null;
        if (!urgent)
        {
            byte[]? batch = TakeBundle(now);
            if (batch != null || _bundle.HasInFlight || _bundle.HasPending) return batch;
        }
        for (int i = 0; i < _order.Count; i++)
        {
            int cursor = urgent ? _priorityCursor : _cursor;
            if (cursor >= _order.Count) cursor = 0;
            ushort id = _order[cursor++];
            if (urgent) _priorityCursor = cursor; else _cursor = cursor;
            if (id == TownServiceFrame.ManifestModule || _priority.Contains(id) != urgent) continue;
            if (id == (urgent ? _normalActive : _priorityActive)) continue;
            ExtrasSendQueue queue = _queues[id];
            byte[]? page = queue.Next(now);
            if (page == null) continue;
            Completed(queue);
            if (urgent) _priorityActive = id; else _normalActive = id;
            return page;
        }
        return null;
    }
    private byte[]? TakeBundle(double now)
    {
        if (!_bundle.HasInFlight && !_bundle.HasPending)
        {
            var bytes = new List<byte[]>(); _bundleFrames.Clear(); _bundleBytes.Clear(); _promotedBundle.Clear(); int size = 2;
            // Group neighboring native output before compression so repeated TMP styles,
            // material tables and card bodies cost once per bounded snapshot. Never wait
            // for a fuller batch: a lone changed module can leave on this same turn.
            for (int i=0; i<_order.Count && bytes.Count<TownServiceCodec.MaxBundleFrames; i++)
            {
                if (_cursor>=_order.Count) _cursor=0; ushort id=_order[_cursor++];
                if(id==TownServiceFrame.ManifestModule||_priority.Contains(id)||id==_priorityActive)continue;
                ExtrasSendQueue queue=_queues[id];int length=queue.PendingLength;
                if(length==0)continue;
                if(size+2+length>58000){_cursor--;break;}
                if(!queue.TryTakePending(out byte[]? packet,out object? identity))continue;
                bytes.Add(packet!);size+=2+length;
                if(identity is TownServiceFrame frame){_bundleFrames.Add(frame);_bundleBytes[id]=packet!;}
            }
            if(bytes.Count==0)return null;
            byte[] container=TownServiceCodec.WriteBundle(bytes);
            _bundle.Enqueue(container,container.Length);
        }
        byte[]? page=_bundle.Next(now);
        if(page!=null&&!_bundle.HasInFlight)
        { foreach(TownServiceFrame frame in _bundleFrames)TownServiceDelivery.Completed?.Invoke(frame);_bundleFrames.Clear();_bundleBytes.Clear();_promotedBundle.Clear(); }
        return page;
    }
    private static void Completed(ExtrasSendQueue queue)
    {
        if (!queue.HasInFlight && queue.CompletedIdentity is TownServiceFrame frame)
            TownServiceDelivery.Completed?.Invoke(frame);
    }
    private static bool SameCensus(TownServiceFrame a, TownServiceFrame b)
    {
        if (a.PublicCatalog != b.PublicCatalog || a.PublicClaim != b.PublicClaim || a.Session != b.Session || a.Service != b.Service || a.Visible != b.Visible || a.Modules.Length != b.Modules.Length) return false;
        for (int i = 0; i < a.Modules.Length; i++) if (a.Modules[i] != b.Modules[i]) return false;
        return true;
    }
    internal void Clear()
    { foreach (var pair in _queues) { _sequences[pair.Key] = pair.Value.Sequence; pair.Value.Clear(); }
        _bundle.Clear(); _bundleFrames.Clear(); _bundleBytes.Clear(); _promotedBundle.Clear(); _queues.Clear(); _order.Clear(); _priority.Clear(); _cursor = _priorityCursor = _priorityTurns = 0;
        _normalActive = _priorityActive = null; _manifestBytes = null; _manifestFrame = _sentManifest = null; _nextManifest = 0; }
    internal static bool SameIdentity(TownServiceFrame a, TownServiceFrame b) => a.PublicCatalog == b.PublicCatalog && a.PublicClaim == b.PublicClaim && a.Session == b.Session
        && a.Service == b.Service && a.Module == b.Module && a.Template == b.Template && a.TemplateAddress == b.TemplateAddress && a.Structure == b.Structure
        && a.Visible == b.Visible && (a.BaseSequence == 0 ? a.Sequence : a.BaseSequence) == (b.BaseSequence == 0 ? b.Sequence : b.BaseSequence);
}

/// <summary>Private service and public cabinet retain independent module queues. Both still
/// consume the same bounded global presentation budget; fragment sequence namespaces cannot
/// collide even when both lanes use the same module IDs and session number.</summary>
internal sealed class TownServiceSendQueue
{
    private readonly TownServiceLaneSendQueue _private, _public;
    private readonly ExtrasSendQueue _voice;
    private readonly Queue<(byte[] Bytes, TownServiceFrame Frame)> _voicePending = new();
    private uint _voiceSession;
    private byte _voiceService;
    private bool _publicTurn;
    internal TownServiceSendQueue(ulong seed)
    { _private = new TownServiceLaneSendQueue(seed & ~131071UL);
      _public = new TownServiceLaneSendQueue((seed & ~131071UL) | 65536UL);
      _voice = new ExtrasSendQueue((seed & ~131071UL) | TownServiceFrame.VoiceModule,
          TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
          snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072); }
    internal void Enqueue(byte[] bytes, int length, TownServiceFrame frame)
    {
        if (!frame.PublicCatalog && (_voiceSession != frame.Session || _voiceService != frame.Service))
        { _voice.Clear(); _voicePending.Clear(); _voiceSession = frame.Session; _voiceService = frame.Service; }
        if (frame.Module == TownServiceFrame.VoiceModule)
        {
            if (!TownServiceVoiceRelayCodec.TryRead(frame, out _) || _voicePending.Count >= 16) return;
            byte[] copy = new byte[length]; Buffer.BlockCopy(bytes, 0, copy, 0, length);
            _voicePending.Enqueue((copy, frame));
            return;
        }
        (frame.PublicCatalog ? _public : _private).Enqueue(bytes, length, frame);
    }
    internal byte[]? Next(double now)
    {
        if (!_voice.HasPending && !_voice.HasInFlight && _voicePending.Count > 0)
        {
            var eventPacket = _voicePending.Dequeue();
            _voice.Enqueue(eventPacket.Bytes, eventPacket.Bytes.Length);
        }
        byte[]? voice = _voice.Next(now);
        if (voice != null) return voice;
        _publicTurn = !_publicTurn;
        return (_publicTurn ? _public : _private).Next(now) ?? (_publicTurn ? _private : _public).Next(now);
    }
    internal void Clear()
    { _private.Clear(); _public.Clear(); _voice.Clear(); _voicePending.Clear(); _voiceSession = 0; _voiceService = 0; }
    internal static bool SameIdentity(TownServiceFrame a, TownServiceFrame b) => TownServiceLaneSendQueue.SameIdentity(a,b);
}
