using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Each sender/module gets its own loss-safe assembler, within the existing datagram cap.</summary>
internal sealed class TownServiceFragments
{
    // One background snapshot is allowed to finish while held-card updates preempt it.
    // At the unchanged globally saturated event cap this needs more than 32 seconds.
    internal const double AssemblyLifetime = 120;
    internal const ulong StockLaneMarker = 1UL << 63;
    private readonly Dictionary<long, ExtrasFragments> _streams = new();
    private readonly Dictionary<long, double> _lastActivity = new();
    private readonly List<long> _expired = new();
    private double _nextSweep;
    private sealed class OriginalAssemblyTrace
    {
        internal ulong Sequence;
        internal double Began;
        internal int Reports;
    }
    private readonly Dictionary<long, OriginalAssemblyTrace> _originalTraces = new();
    private void BeginOriginalTrace(long key, int stream, byte[] packet, double now)
    {
        if (!GloomhavenVR.Core.VRLog.WantsDebug
            || stream != TownServiceFrame.BundleStream && stream != TownServiceFrame.UrgentBundleStream) return;
        // Stream/Lane have already validated this page's complete metadata. Keep
        // only the first eight assemblies per sender/lane/stream, never a motion log.
        int expected = NetPacket.PeekType(packet, packet.Length) == NetProtocol.MsgPresentationCompression
            ? NetProtocol.ExtIdPresentationCompression : NetProtocol.ExtIdExtrasFragment;
        ulong sequence = 0;
        for (int at = 6; at < packet.Length;)
        {
            int id = packet[at++], length = packet[at++];
            if (id == expected) { for (int b = 0; b < 8; b++) sequence |= (ulong)packet[at + b] << (8 * b); break; }
            at += length;
        }
        if (!_originalTraces.TryGetValue(key, out OriginalAssemblyTrace? trace))
        {
            if (_originalTraces.Count >= 96) return;
            trace = new OriginalAssemblyTrace(); _originalTraces.Add(key, trace);
        }
        if (sequence <= trace.Sequence || trace.Reports >= 8) return;
        trace.Sequence = sequence; trace.Began = now;
    }
    private void FinishOriginalTrace(long key, int sender, int stream, double now, int bytes, int members)
    {
        if (!GloomhavenVR.Core.VRLog.WantsDebug || !_originalTraces.TryGetValue(key, out OriginalAssemblyTrace? trace)
            || trace.Reports >= 8) return;
        trace.Reports++;
        GloomhavenVR.Core.VRLog.Info("TownServices", "Native original bundle assembled: peer=" + sender
            + " stream=" + stream + " sequence=" + trace.Sequence + " members=" + members + " bytes=" + bytes
            + " assembly=" + (now - trace.Began).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "s.");
    }
    internal byte[]? Accept(int sender, byte[] packet, int length, double now)
    {
        int stream = Stream(packet, length);
        if (sender <= 0 || stream < 0 || double.IsNaN(now) || double.IsInfinity(now)) return null;
        if (now >= _nextSweep)
        {
            _nextSweep = now + 1; _expired.Clear();
            foreach (var pair in _lastActivity)
                if (now - pair.Value > AssemblyLifetime) _expired.Add(pair.Key);
            foreach (long expired in _expired)
            { _streams[expired].Clear(); _streams.Remove(expired); _lastActivity.Remove(expired); _originalTraces.Remove(expired); }
        }
        int lane = Lane(packet, length);
        if (lane < 0) return null;
        long key = ((long)sender << 18) | (uint)(stream | lane << 16);
        if (!_streams.TryGetValue(key, out ExtrasFragments? assembler))
        {
            if (_streams.Count >= 24 * (TownServiceFrame.MaxModules + 2)) return null;
            assembler = new ExtrasFragments(TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
                TownServiceFrame.MaxBytes, AssemblyLifetime);
            _streams.Add(key, assembler);
        }
        BeginOriginalTrace(key, stream, packet, now);
        _lastActivity[key] = now;
        byte[]? result = assembler.Accept(sender, packet, length, now);
        if (result == null) return null;
        if (stream == TownServiceFrame.BundleStream || stream == TownServiceFrame.UrgentBundleStream)
        {
            if (!TownServiceCodec.TryReadBundle(result, result.Length, out byte[][]? bundle)) return null;
            bool? publicCatalog = null;
            foreach (byte[] member in bundle!)
            {
                if (!TownServiceCodec.TryRead(member, member.Length, out TownServiceFrame? decoded)
                    || decoded!.VisitorStock != (lane == 2)) return null;
                if (publicCatalog.HasValue && publicCatalog.Value != decoded.PublicCatalog) return null;
                publicCatalog = decoded.PublicCatalog;
            }
            FinishOriginalTrace(key, sender, stream, now, result.Length, bundle!.Length);
            return result;
        }
        if (!TownServiceCodec.TryRead(result, result.Length, out TownServiceFrame? frame)
            || frame!.Module != stream || frame.VisitorStock != (lane == 2)) return null;
        // A delayed census cannot identify the presentation sequence of an incomplete
        // module in another fragment lane. Never prune that assembly by membership;
        // idle expiration and the fixed pool bound reclaim retired lanes safely.
        return result;
    }
    internal void Forget(int sender)
    {
        var keys = new List<long>();
        foreach (long key in _streams.Keys) if (key >> 18 == sender) keys.Add(key);
        foreach (long key in keys) { _streams[key].Clear(); _streams.Remove(key); _lastActivity.Remove(key); _originalTraces.Remove(key); }
    }
    internal void Clear() { foreach (ExtrasFragments assembler in _streams.Values) assembler.Clear(); _streams.Clear(); _lastActivity.Clear(); _expired.Clear(); _originalTraces.Clear(); _nextSweep = 0; }
    // Bit 16 retains the historical private/public prefix. The high sequence bit
    // marks only the additive stock lane; old bit 17 remains a sequence counter.
    // A stock+public combination is invalid, rather than a fourth cosmetic lane.
    private static int Lane(byte[] packet, int length)
    {
        bool compressed = NetPacket.PeekType(packet, length) == NetProtocol.MsgPresentationCompression;
        int lane = -1;
        for (int at = 6; at < length;)
        {
            if (at + 2 > length) return -1;
            int id = packet[at++], count = packet[at++];
            if (at + count > length) return -1;
            if (id == (compressed ? NetProtocol.ExtIdPresentationCompression : NetProtocol.ExtIdExtrasFragment))
            {
                if (count <= (compressed ? 15 : 12)) return -1;
                int next = packet[at + 2] & 1;
                if ((packet[at + 7] & 128) != 0)
                { if (next != 0) return -1; next = 2; }
                if (lane >= 0 && lane != next) return -1;
                lane = next;
            }
            at += count;
        }
        return lane;
    }
    internal static int Stream(byte[] packet, int length)
    {
        if (packet == null || length < 20 || length > packet.Length || length > ExtrasFragments.MaxDatagramBytes) return -1;
        bool compressed = NetPacket.PeekType(packet, length) == NetProtocol.MsgPresentationCompression;
        if (compressed ? ExtrasFragments.CompressedPayloadType(packet, length) != TownServiceCodec.MessageType
            : NetPacket.PeekType(packet, length) != TownServiceCodec.FragmentType) return -1;
        int stream = -1;
        for (int at = 6; at < length;)
        {
            if (at + 2 > length) return -1;
            int id = packet[at++], count = packet[at++];
            if (at + count > length) return -1;
            if (id == (compressed ? NetProtocol.ExtIdPresentationCompression : NetProtocol.ExtIdExtrasFragment))
            {
                if (count <= (compressed ? 15 : 12)) return -1;
                int next = packet[at] | packet[at + 1] << 8;
                if (stream >= 0 && stream != next) return -1;
                stream = next;
            }
            at += count;
        }
        return stream;
    }
}
