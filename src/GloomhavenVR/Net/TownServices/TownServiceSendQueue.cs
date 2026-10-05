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
    private readonly Dictionary<ushort, ExtrasSendQueue> _clocks = new();
    private readonly HashSet<ushort> _catalogOriginals = new();
    private readonly Dictionary<ushort, TownServiceFrame> _catalogBases = new();
    private int _clockCursor;
    private bool _clockRepairDue;
    private int _cursor;
    private int _priorityCursor, _priorityTurns;
    private ushort? _normalActive, _priorityActive;
    private readonly HashSet<ushort> _priority = new();
    private readonly HashSet<ushort> _coldPriority = new();
    private byte[]? _manifestBytes;
    private TownServiceFrame? _manifestFrame;
    private TownServiceFrame? _sentManifest;
    private double _nextManifest;
    private readonly ExtrasSendQueue _bundle;
    private readonly ExtrasSendQueue _urgentBundle;
    private readonly List<TownServiceFrame> _bundleFrames = new();
    private readonly List<TownServiceFrame> _urgentBundleFrames = new();
    private readonly Dictionary<ushort, byte[]> _bundleBytes = new();
    private readonly HashSet<ushort> _promotedBundle = new();
    private uint _session;
    private byte _service;
    internal TownServiceLaneSendQueue(ulong seed)
    {
        _seed = seed;
        _bundle = new ExtrasSendQueue((seed & ~65535UL) | TownServiceFrame.BundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
            counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: seed & TownServiceFragments.StockLaneMarker);
        _urgentBundle = new ExtrasSendQueue((seed & ~65535UL) | TownServiceFrame.UrgentBundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
            counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: seed & TownServiceFragments.StockLaneMarker);
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
                TownServiceCodec.FragmentType, preserveFirst: true, snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
                counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: _seed & TownServiceFragments.StockLaneMarker);
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
                if (id == TownServiceFrame.ManifestModule || Array.BinarySearch(frame.Modules, id) >= 0 || _catalogOriginals.Contains(id)) continue;
                _sequences[id] = _queues[id].Sequence; _queues[id].Clear(); _queues.Remove(id); _order.RemoveAt(i); _priority.Remove(id); _coldPriority.Remove(id);
                if (_clocks.TryGetValue(id, out var clock)) { _sequences[id] = Math.Max(_sequences[id], clock.Sequence); clock.Clear(); _clocks.Remove(id); }
            }
        }
        else
        {
            if (frame.PublicCatalog && !frame.VisitorStock && frame.RackMember != null && !frame.RackMember.Detached)
            {
                _catalogOriginals.Add(frame.Module);
                if (frame.BaseSequence == 0 && !_catalogBases.ContainsKey(frame.Module))
                    _catalogBases[frame.Module] = frame;
            }
            if (frame.PublicCatalog && frame.BaseSequence == 0 && frame.CatalogBank?.Prepared == true)
            {
                if (!_clocks.TryGetValue(frame.Module, out var clock) && _clocks.Count < 2)
                {
                    clock = new ExtrasSendQueue(queue.Sequence, TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
                        snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
                        counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: _seed & TownServiceFragments.StockLaneMarker);
                    _clocks.Add(frame.Module, clock);
                }
                if (clock != null)
                {
                    TownServiceFrame reference = TownCatalogClock.Create(frame, _catalogBases);
                    foreach (TownServiceFrame current in frame.CatalogBank.Updates)
                        if (!_catalogBases.ContainsKey(current.Module)) _catalogBases[current.Module] = current;
                    // Latest waiting clock, never four obsolete full keyframes. This
                    // queue does not invoke local original-delivery completion.
                    try { byte[] referenceBytes = TownServiceCodec.Write(reference); clock.Enqueue(referenceBytes, referenceBytes.Length); }
                    catch (System.IO.InvalidDataException) { /* A large changed original uses the unchanged full repair. */ }
                }
            }
            if (frame.HighPriority)
            {
                _priority.Add(frame.Module);
                if (frame.BaseSequence == 0) _coldPriority.Add(frame.Module);
                // A just-grabbed card may have its first baseline inside a cold bundle.
                // Deliver that exact immutable dependency urgently before its pose delta.
                if (_bundleBytes.TryGetValue(frame.Module, out byte[]? baselineBytes) && _promotedBundle.Add(frame.Module))
                    foreach (TownServiceFrame baseline in _bundleFrames)
                        if (baseline.Module == frame.Module && baseline.BaseSequence == 0)
                        { _coldPriority.Add(frame.Module); queue.Enqueue(baselineBytes, baselineBytes.Length, baseline); break; }
            }
            else { _priority.Remove(frame.Module); _coldPriority.Remove(frame.Module); }
            queue.Enqueue(bytes, length, frame);
        }
    }
    internal byte[]? NextUrgent(double now)
    {
        // Only a first immutable original may borrow an early global turn.
        // Continuous pose/hover deltas already have the independent numeric lane;
        // letting them borrow forever would steal the cold cabinet's fair share.
        bool waiting = _coldPriority.Count > 0 && (_urgentBundle.HasInFlight || _urgentBundle.HasPending);
        foreach (ushort id in _coldPriority)
            if (_queues.TryGetValue(id, out var queue) && (queue.HasPending || queue.HasInFlight)) { waiting = true; break; }
        if (!waiting) return null;
        // Early turns borrow the existing town share rather than add bandwidth.
        // They must retain this lane's two-urgent/one-background arbitration too;
        // otherwise repaying every scheduled town turn would starve a cold rack.
        return Next(now);
    }
    private byte[]? NextManifest(double now)
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
        return null;
    }
    internal byte[]? Next(double now)
    {
        byte[]? census = NextManifest(now);
        if (census != null) return census;
        // Two urgent turns, then one background turn. A held face cannot wait behind
        // thousands of catalog rows; the full catalog cannot starve behind a held card.
        bool normalFirst = _priorityTurns >= 2;
        // A reference clock consumes an ordinary urgent town turn. The next
        // urgent turn belongs to full repair, and every third still to background.
        if (!normalFirst && !_clockRepairDue)
        {
            byte[]? clock = TakeClock(now);
            if (clock != null) { _clockRepairDue = true; _priorityTurns++; return clock; }
        }
        byte[]? selected = normalFirst ? Take(now, false) : Take(now, true);
        if (selected != null) { _priorityTurns = normalFirst ? 0 : _priorityTurns + 1; if (!normalFirst) _clockRepairDue = false; return selected; }
        // An oversize full root uses this same module namespace directly. Finish
        // its interrupted clock before trying background; otherwise a nonempty
        // dormant bank could keep that assembly blocked indefinitely.
        if (!normalFirst)
        {
            selected = TakeClock(now, unfinishedOnly: true);
            if (selected != null) { _priorityTurns++; return selected; }
        }
        selected = Take(now, normalFirst);
        if (selected != null) _priorityTurns = normalFirst ? _priorityTurns + 1 : 0;
        if (selected != null && normalFirst) _clockRepairDue = false;
        if (selected == null)
        {
            selected = TakeClock(now);
            if (selected != null) { _clockRepairDue = true; _priorityTurns = normalFirst ? 0 : _priorityTurns + 1; }
        }
        return selected;
    }
    private byte[]? TakeClock(double now, bool unfinishedOnly = false)
    {
        for (int i = 0; i < _order.Count; i++)
        {
            if (_clockCursor >= _order.Count) _clockCursor = 0;
            ushort id = _order[_clockCursor++];
            if (!_clocks.TryGetValue(id, out var clock) || !_queues.TryGetValue(id, out var complete) || complete.HasInFlight) continue;
            if (unfinishedOnly && !clock.HasInFlight) continue;
            if (!clock.HasInFlight) clock.AdvanceSequence(complete.Sequence);
            byte[]? page = clock.Next(now);
            if (page != null) return page;
        }
        return null;
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
        byte[]? batch = TakeBundle(now, urgent);
        ExtrasSendQueue bundleQueue = urgent ? _urgentBundle : _bundle;
        if (batch != null || bundleQueue.HasInFlight || bundleQueue.HasPending) return batch;
        for (int i = 0; i < _order.Count; i++)
        {
            int cursor = urgent ? _priorityCursor : _cursor;
            if (cursor >= _order.Count) cursor = 0;
            ushort id = _order[cursor++];
            if (urgent) _priorityCursor = cursor; else _cursor = cursor;
            if (id == TownServiceFrame.ManifestModule || _priority.Contains(id) != urgent) continue;
            if (id == (urgent ? _normalActive : _priorityActive)) continue;
            ExtrasSendQueue queue = _queues[id];
            if (_clocks.TryGetValue(id, out var clock))
            {
                if (clock.HasInFlight) continue;
                if (!queue.HasInFlight) queue.AdvanceSequence(clock.Sequence);
            }
            byte[]? page = queue.Next(now);
            if (page == null) continue;
            Completed(queue);
            if (urgent) _priorityActive = id; else _normalActive = id;
            return page;
        }
        return null;
    }
    private byte[]? TakeBundle(double now, bool urgent)
    {
        ExtrasSendQueue bundle = urgent ? _urgentBundle : _bundle;
        List<TownServiceFrame> frames = urgent ? _urgentBundleFrames : _bundleFrames;
        if (!bundle.HasInFlight && !bundle.HasPending)
        {
            var bytes = new List<byte[]>(); frames.Clear();
            if (!urgent) { _bundleBytes.Clear(); _promotedBundle.Clear(); }
            int size = 2;
            // Group neighboring native output before compression so repeated TMP styles,
            // material tables and card bodies cost once per bounded snapshot. Never wait
            // for a fuller batch: a lone changed module can leave on this same turn.
            for (int i=0; i<_order.Count && bytes.Count<TownServiceCodec.MaxBundleFrames; i++)
            {
                int cursor = urgent ? _priorityCursor : _cursor;
                if (cursor >= _order.Count) cursor = 0; ushort id = _order[cursor++];
                if (urgent) _priorityCursor = cursor; else _cursor = cursor;
                if (id == TownServiceFrame.ManifestModule || _priority.Contains(id) != urgent
                    || id == (urgent ? _normalActive : _priorityActive)) continue;
                ExtrasSendQueue queue=_queues[id];int length=queue.PendingLength;
                if(length==0)continue;
                if(size+2+length>58000){if(urgent)_priorityCursor--;else _cursor--;break;}
                if(!queue.TryTakePending(out byte[]? packet,out object? identity))continue;
                bytes.Add(packet!);size+=2+length;
                if(identity is TownServiceFrame frame){frames.Add(frame);if(!urgent)_bundleBytes[id]=packet!;}
            }
            if(bytes.Count==0)return null;
            byte[] container=TownServiceCodec.WriteBundle(bytes);
            bundle.Enqueue(container,container.Length);
        }
        byte[]? page=bundle.Next(now);
        if(page!=null&&!bundle.HasInFlight)
        { foreach(TownServiceFrame frame in frames) { if (frame.BaseSequence == 0) _coldPriority.Remove(frame.Module); TownServiceDelivery.Completed?.Invoke(frame); } frames.Clear();
          if(!urgent){_bundleBytes.Clear();_promotedBundle.Clear();} }
        return page;
    }
    private void Completed(ExtrasSendQueue queue)
    {
        if (!queue.HasInFlight && queue.CompletedIdentity is TownServiceFrame frame)
        { if (frame.BaseSequence == 0) _coldPriority.Remove(frame.Module); TownServiceDelivery.Completed?.Invoke(frame); }
    }
    private static bool SameCensus(TownServiceFrame a, TownServiceFrame b)
    {
        if (a.VisitorStock != b.VisitorStock || a.PublicCatalog != b.PublicCatalog || a.PublicClaim != b.PublicClaim || a.Session != b.Session || a.Service != b.Service || a.Visible != b.Visible || a.Modules.Length != b.Modules.Length) return false;
        for (int i = 0; i < a.Modules.Length; i++) if (a.Modules[i] != b.Modules[i]) return false;
        return true;
    }
    internal void Clear()
    { foreach (var pair in _queues) { _sequences[pair.Key] = pair.Value.Sequence; pair.Value.Clear(); }
        foreach (var pair in _clocks) { _sequences[pair.Key] = Math.Max(_sequences.TryGetValue(pair.Key, out var sequence) ? sequence : 0, pair.Value.Sequence); pair.Value.Clear(); }
        _clocks.Clear(); _catalogOriginals.Clear(); _catalogBases.Clear(); _clockCursor = 0; _clockRepairDue = false;
        _bundle.Clear(); _urgentBundle.Clear(); _bundleFrames.Clear(); _urgentBundleFrames.Clear(); _bundleBytes.Clear(); _promotedBundle.Clear(); _queues.Clear(); _order.Clear(); _priority.Clear(); _coldPriority.Clear(); _cursor = _priorityCursor = _priorityTurns = 0;
        _normalActive = _priorityActive = null; _manifestBytes = null; _manifestFrame = _sentManifest = null; _nextManifest = 0; }
    internal static bool SameIdentity(TownServiceFrame a, TownServiceFrame b) => a.VisitorStock == b.VisitorStock && a.PublicCatalog == b.PublicCatalog && a.PublicClaim == b.PublicClaim && a.Session == b.Session
        && a.Service == b.Service && a.Module == b.Module && a.Template == b.Template && a.TemplateAddress == b.TemplateAddress && a.Structure == b.Structure
        && a.Visible == b.Visible && (a.BaseSequence == 0 ? a.Sequence : a.BaseSequence) == (b.BaseSequence == 0 ? b.Sequence : b.BaseSequence);
}

/// <summary>Private service, public cabinet and visitor stock retain independent module queues. All still
/// consume the same bounded global presentation budget; fragment sequence namespaces cannot
/// collide even when all lanes use the same module IDs and session number.</summary>
internal sealed class TownServiceSendQueue
{
    private readonly TownServiceLaneSendQueue _private, _public, _stock;
    private readonly ExtrasSendQueue _voice, _stockVoice;
    private readonly Queue<(byte[] Bytes, TownServiceFrame Frame)> _voicePending = new(), _stockVoicePending = new();
    private uint _voiceSession, _stockVoiceSession;
    private bool _stockVoiceTurn;
    private byte _voiceService;
    private int _laneTurn;
    internal TownServiceSendQueue(ulong seed)
    { _private = new TownServiceLaneSendQueue(seed & ~(131071UL | TownServiceFragments.StockLaneMarker));
      _public = new TownServiceLaneSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | 65536UL);
      _stock = new TownServiceLaneSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | TownServiceFragments.StockLaneMarker);
      _voice = new ExtrasSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | TownServiceFrame.VoiceModule,
          TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
          snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
          counterMask: TownServiceFragments.StockLaneMarker - 1);
      _stockVoice = new ExtrasSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | TownServiceFrame.VoiceModule,
          TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
          snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
          counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: TownServiceFragments.StockLaneMarker); }
    internal void Enqueue(byte[] bytes, int length, TownServiceFrame frame)
    {
        if (!frame.PublicCatalog && !frame.VisitorStock && (_voiceSession != frame.Session || _voiceService != frame.Service))
        { _voice.Clear(); _voicePending.Clear(); _voiceSession = frame.Session; _voiceService = frame.Service; }
        if (frame.Module == TownServiceFrame.VoiceModule)
        {
            if (frame.PublicCatalog || frame.TemplateAddress != TownServiceFrame.VoiceAddress
                || frame.Session == 0 || frame.Sequence == 0 || frame.BaseSequence != 0) return;
            Queue<(byte[] Bytes, TownServiceFrame Frame)> voicePending = frame.VisitorStock ? _stockVoicePending : _voicePending;
            if (frame.VisitorStock && _stockVoiceSession != frame.Session)
            { _stockVoice.Clear(); _stockVoicePending.Clear(); _stockVoiceSession = frame.Session; }
            if (voicePending.Count >= 16) return;
            byte[] copy = new byte[length]; Buffer.BlockCopy(bytes, 0, copy, 0, length);
            voicePending.Enqueue((copy, frame));
            return;
        }
        (frame.VisitorStock ? _stock : frame.PublicCatalog ? _public : _private).Enqueue(bytes, length, frame);
    }
    internal byte[]? Next(double now)
    {
        if (!_voice.HasPending && !_voice.HasInFlight && _voicePending.Count > 0)
        {
            var eventPacket = _voicePending.Dequeue();
            _voice.Enqueue(eventPacket.Bytes, eventPacket.Bytes.Length);
        }
        if (!_stockVoice.HasPending && !_stockVoice.HasInFlight && _stockVoicePending.Count > 0)
        {
            var eventPacket = _stockVoicePending.Dequeue();
            _stockVoice.Enqueue(eventPacket.Bytes, eventPacket.Bytes.Length);
        }
        _stockVoiceTurn = !_stockVoiceTurn;
        byte[]? voice = _stockVoiceTurn ? _stockVoice.Next(now) ?? _voice.Next(now)
            : _voice.Next(now) ?? _stockVoice.Next(now);
        if (voice != null) return voice;
        _laneTurn = (_laneTurn + 1) % 3;
        return _laneTurn == 0 ? _private.Next(now) ?? _public.Next(now) ?? _stock.Next(now)
            : _laneTurn == 1 ? _public.Next(now) ?? _stock.Next(now) ?? _private.Next(now)
            : _stock.Next(now) ?? _private.Next(now) ?? _public.Next(now);
    }
    // Cold visible originals get a bounded direct turn in the existing global
    // scheduler. The public, private and stock lanes retain equal arbitration;
    // this never bypasses their manifests or starts a separate send clock.
    internal byte[]? NextUrgent(double now)
    {
        _laneTurn = (_laneTurn + 1) % 3;
        return _laneTurn == 0 ? _private.NextUrgent(now) ?? _public.NextUrgent(now) ?? _stock.NextUrgent(now)
            : _laneTurn == 1 ? _public.NextUrgent(now) ?? _stock.NextUrgent(now) ?? _private.NextUrgent(now)
            : _stock.NextUrgent(now) ?? _private.NextUrgent(now) ?? _public.NextUrgent(now);
    }
    internal void Clear()
    { _private.Clear(); _public.Clear(); _stock.Clear(); _voice.Clear(); _stockVoice.Clear();
        _voicePending.Clear(); _stockVoicePending.Clear(); _voiceSession = _stockVoiceSession = 0;
        _voiceService = 0; _stockVoiceTurn = false; }
    internal static bool SameIdentity(TownServiceFrame a, TownServiceFrame b) => TownServiceLaneSendQueue.SameIdentity(a,b);
}
