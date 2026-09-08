using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>
/// Bounded transport envelopes for complete, unchanged presentation snapshots. Bolt does not
/// fragment oversized events. Each stream uses its own envelope and assembler, so reordering
/// cannot overwrite a newer state with an older, finally completed snapshot.
/// </summary>
internal sealed class ExtrasFragments
{
    internal const int MaxSnapshotBytes = 4096;
    internal const int MaxDatagramBytes = 864;
    internal const int ChunkBytes = 200;
    private const int MetadataBytes = 12; // sequence:u64, snapshot length:u16, offset:u16
    private const int MaxPeers = 8;
    // Independent cosmetic streams can wait behind 24 auxiliary slots, other presentation
    // pages and the shared handshake. At low frame cadence this legitimately exceeds five
    // seconds. Memory stays bounded by MaxPeers and snapshotLimit; gameplay timeouts do not change.
    internal const double PresentationAssemblyLifetime = 32;
    private readonly double _assemblyLifetime;
    private readonly Dictionary<int, Pending> _peers = new();
    private readonly byte _payloadType;
    private readonly byte _envelopeType;
    private readonly int _snapshotLimit;

    internal ExtrasFragments(byte payloadType = NetProtocol.MsgExtras,
        byte envelopeType = NetProtocol.MsgExtrasFragments, int snapshotLimit = MaxSnapshotBytes,
        double assemblyLifetime = 5)
    {
        _payloadType = payloadType;
        _envelopeType = envelopeType;
        if (double.IsNaN(assemblyLifetime) || double.IsInfinity(assemblyLifetime) || assemblyLifetime <= 0)
            throw new ArgumentOutOfRangeException(nameof(assemblyLifetime));
        _snapshotLimit = snapshotLimit;
        _assemblyLifetime = assemblyLifetime;
    }

    private sealed class Pending
    {
        internal ulong Sequence;
        internal byte[]? Bytes;
        internal bool[]? Received;
        internal int Count;
        internal double Started;
    }

    internal void Clear() => _peers.Clear();
    internal void Forget(int sender) => _peers.Remove(sender);

    internal static byte[][] Encode(byte[] snapshot, int length, ulong sequence,
        byte payloadType = NetProtocol.MsgExtras, byte envelopeType = NetProtocol.MsgExtrasFragments,
        int snapshotLimit = MaxSnapshotBytes)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length
            || length > snapshotLimit || NetPacket.PeekType(snapshot, length) != payloadType)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        int chunkCount = (length + ChunkBytes - 1) / ChunkBytes;
        var packets = new byte[(chunkCount + 3) / 4][];
        int offset = 0;
        for (int page = 0; page < packets.Length; page++)
        {
            int chunks = Math.Min(4, chunkCount - page * 4);
            int dataLength = Math.Min(chunks * ChunkBytes, length - offset);
            var packet = new byte[6 + chunks * (2 + MetadataBytes) + dataLength];
            Buffer.BlockCopy(snapshot, 0, packet, 0, 6);
            packet[5] = envelopeType;
            int p = 6;
            for (int c = 0; c < chunks; c++)
            {
                int n = Math.Min(ChunkBytes, length - offset);
                packet[p++] = NetProtocol.ExtIdExtrasFragment;
                packet[p++] = (byte)(MetadataBytes + n);
                for (int b = 0; b < 8; b++) packet[p++] = (byte)(sequence >> (8 * b));
                packet[p++] = (byte)length; packet[p++] = (byte)(length >> 8);
                packet[p++] = (byte)offset; packet[p++] = (byte)(offset >> 8);
                Buffer.BlockCopy(snapshot, offset, packet, p, n);
                offset += n; p += n;
            }
            packets[page] = packet;
        }
        return packets;
    }

    /// <summary>Returns a complete snapshot once. Partial, stale, duplicate or invalid data is inert.</summary>
    internal byte[]? Accept(int sender, byte[] packet, int length, double now)
    {
        if (packet == null || length < 6 || length > packet.Length || length > MaxDatagramBytes
            || NetPacket.PeekType(packet, length) != _envelopeType)
            return null;
        // Validate the entire datagram before mutating any assembly. All chunks in a datagram
        // must identify the same snapshot; offsets and lengths are canonical and nonoverlapping.
        ulong sequence = 0;
        int total = 0;
        bool found = false;
        for (int p = 6; p < length;)
        {
            if (p + 2 > length) return null;
            int type = packet[p++], n = packet[p++];
            if (p + n > length) return null;
            if (type == NetProtocol.ExtIdExtrasFragment)
            {
                if (n <= MetadataBytes) return null;
                ulong s = ReadSequence(packet, p);
                int t = packet[p + 8] | packet[p + 9] << 8;
                int offset = packet[p + 10] | packet[p + 11] << 8;
                if (t < 6 || t > _snapshotLimit || offset >= t || offset % ChunkBytes != 0
                    || n - MetadataBytes != Math.Min(ChunkBytes, t - offset)) return null;
                if (found && (s != sequence || t != total)) return null;
                sequence = s; total = t; found = true;
            }
            p += n;
        }
        if (!found) return null;
        if (!_peers.TryGetValue(sender, out Pending? state))
        {
            if (_peers.Count >= MaxPeers) return null;
            state = new Pending();
            _peers.Add(sender, state);
        }
        else
        {
            if (sequence < state.Sequence) return null;
            if (sequence == state.Sequence && state.Bytes == null) return null;
            if (sequence == state.Sequence && (state.Bytes!.Length != total
                || now - state.Started > _assemblyLifetime))
            {
                state.Bytes = null; state.Received = null;
                return null;
            }
        }
        if (state.Bytes == null || sequence > state.Sequence)
        {
            state.Sequence = sequence;
            state.Bytes = new byte[total];
            state.Received = new bool[(total + ChunkBytes - 1) / ChunkBytes];
            state.Count = 0;
            state.Started = now;
        }
        for (int p = 6; p < length;)
        {
            int type = packet[p++], n = packet[p++];
            if (type == NetProtocol.ExtIdExtrasFragment)
            {
                int offset = packet[p + 10] | packet[p + 11] << 8;
                int index = offset / ChunkBytes;
                if (!state.Received![index])
                {
                    Buffer.BlockCopy(packet, p + MetadataBytes, state.Bytes, offset, n - MetadataBytes);
                    state.Received[index] = true;
                    state.Count++;
                }
                else
                {
                    // Conflicting duplicates invalidate this generation rather than splicing data.
                    for (int b = 0; b < n - MetadataBytes; b++)
                        if (state.Bytes[offset + b] != packet[p + MetadataBytes + b])
                        { state.Bytes = null; state.Received = null; return null; }
                }
            }
            p += n;
        }
        if (state.Count != state.Received!.Length) return null;
        byte[] complete = state.Bytes;
        state.Bytes = null; state.Received = null;
        return NetPacket.PeekType(complete, complete.Length) == _payloadType ? complete : null;
    }

    /// <summary>Message8 assigns the low five opaque sequence bits to its native slot stream.
    /// Validate routing metadata before choosing an assembler; Accept validates the whole page.</summary>
    internal static int NativeSlotStream(byte[] packet, int length)
    {
        if (packet == null || length < 20 || length > packet.Length || length > MaxDatagramBytes
            || NetPacket.PeekType(packet, length) != NetProtocol.MsgNativeUseBarFragments) return -1;
        int stream = -1;
        for (int p = 6; p < length;)
        {
            if (p + 2 > length) return -1;
            int type = packet[p++], n = packet[p++];
            if (p + n > length) return -1;
            if (type == NetProtocol.ExtIdExtrasFragment)
            {
                if (n <= MetadataBytes) return -1;
                int next = (int)(ReadSequence(packet, p) & 31);
                if (next < 8 || (stream >= 0 && next != stream)) return -1;
                stream = next;
            }
            p += n;
        }
        return stream;
    }

    private static ulong ReadSequence(byte[] packet, int p)
    {
        ulong sequence = 0;
        for (int b = 0; b < 8; b++) sequence |= (ulong)packet[p + b] << (8 * b);
        return sequence;
    }
}
