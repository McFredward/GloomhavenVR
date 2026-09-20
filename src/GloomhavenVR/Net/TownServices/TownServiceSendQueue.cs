using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Independent bounded module queues inside the existing global event budget.</summary>
internal sealed class TownServiceSendQueue
{
    private readonly Dictionary<ushort, ExtrasSendQueue> _queues = new();
    private readonly List<ushort> _order = new();
    private readonly ulong _seed;
    private readonly Dictionary<ushort, ulong> _sequences = new();
    private int _cursor;
    private uint _session;
    private byte _service;
    internal TownServiceSendQueue(ulong seed) { _seed = seed; }
    internal void Enqueue(byte[] bytes, int length, TownServiceFrame frame)
    {
        if (_session != frame.Session || _service != frame.Service)
        { Clear(); _session = frame.Session; _service = frame.Service; }
        if (!_queues.TryGetValue(frame.Module, out ExtrasSendQueue? queue))
        {
            if (_queues.Count >= TownServiceFrame.MaxModules + 1) return;
            ulong sequence = _sequences.TryGetValue(frame.Module, out ulong previous) ? previous : (_seed & ~65535UL) | frame.Module;
            queue = new ExtrasSendQueue(sequence, TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType, preserveFirst: true, snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 65536);
            _queues.Add(frame.Module, queue); _order.Add(frame.Module);
        }
        queue.Enqueue(bytes, length, frame);
        if (frame.Module == TownServiceFrame.ManifestModule)
        {
            // Obsolete row queues must not keep the last session's catalog in flight forever.
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                ushort id = _order[i];
                if (id == TownServiceFrame.ManifestModule || Array.BinarySearch(frame.Modules, id) >= 0) continue;
                _sequences[id] = _queues[id].Sequence; _queues[id].Clear(); _queues.Remove(id); _order.RemoveAt(i);
            }
        }
    }
    internal byte[]? Next(double now)
    {
        if (_queues.TryGetValue(TownServiceFrame.ManifestModule, out ExtrasSendQueue? manifest))
        { byte[]? page = manifest.Next(now); if (page != null) return page; }
        for (int i = 0; i < _order.Count; i++)
        {
            if (_cursor >= _order.Count) _cursor = 0;
            ExtrasSendQueue queue = _queues[_order[_cursor++]];
            byte[]? page = queue.Next(now); if (page != null) return page;
        }
        return null;
    }
    internal void Clear()
    { foreach (var pair in _queues) { _sequences[pair.Key] = pair.Value.Sequence; pair.Value.Clear(); }
        _queues.Clear(); _order.Clear(); _cursor = 0; }
    internal static bool SameIdentity(TownServiceFrame a, TownServiceFrame b) => a.Session == b.Session
        && a.Service == b.Service && a.Module == b.Module && a.Template == b.Template && a.Structure == b.Structure
        && a.Visible == b.Visible && (a.BaseSequence == 0 ? a.Sequence : a.BaseSequence) == (b.BaseSequence == 0 ? b.Sequence : b.BaseSequence);
}
