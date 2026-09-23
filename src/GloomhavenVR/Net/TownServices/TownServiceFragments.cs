using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Each sender/module gets its own loss-safe assembler, within the existing datagram cap.</summary>
internal sealed class TownServiceFragments
{
    // One background snapshot is allowed to finish while held-card updates preempt it.
    // At the unchanged globally saturated event cap this needs more than 32 seconds.
    internal const double AssemblyLifetime = 120;
    private readonly Dictionary<long, ExtrasFragments> _streams = new();
    internal byte[]? Accept(int sender, byte[] packet, int length, double now)
    {
        int stream = Stream(packet, length);
        if (sender <= 0 || stream < 0) return null;
        long key = ((long)sender << 16) | (ushort)stream;
        if (!_streams.TryGetValue(key, out ExtrasFragments? assembler))
        {
            if (_streams.Count >= 8 * (TownServiceFrame.MaxModules + 1)) return null;
            assembler = new ExtrasFragments(TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
                TownServiceFrame.MaxBytes, AssemblyLifetime);
            _streams.Add(key, assembler);
        }
        byte[]? result = assembler.Accept(sender, packet, length, now);
        if (result == null) return null;
        if (stream == TownServiceFrame.BundleStream) return TownServiceCodec.TryReadBundle(result, result.Length, out _) ? result : null;
        if (!TownServiceCodec.TryRead(result, result.Length, out TownServiceFrame? frame) || frame!.Module != stream) return null;
        if (frame.Module == TownServiceFrame.ManifestModule)
        {
            var removed = new List<long>();
            foreach (long candidate in _streams.Keys)
                if (candidate >> 16 == sender && (ushort)candidate != TownServiceFrame.ManifestModule && (ushort)candidate != TownServiceFrame.BundleStream
                    && Array.BinarySearch(frame.Modules, (ushort)candidate) < 0) removed.Add(candidate);
            foreach (long candidate in removed) { _streams[candidate].Clear(); _streams.Remove(candidate); }
        }
        return result;
    }
    internal void Forget(int sender)
    {
        var keys = new List<long>();
        foreach (long key in _streams.Keys) if (key >> 16 == sender) keys.Add(key);
        foreach (long key in keys) { _streams[key].Clear(); _streams.Remove(key); }
    }
    internal void Clear() { foreach (ExtrasFragments assembler in _streams.Values) assembler.Clear(); _streams.Clear(); }
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
