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
    // Reward pose eligibility adds59 bytes to the previous4074-byte worst case.
    // The envelope grammar/chunk size are unchanged; six bounded datagrams still suffice.
    internal const int MaxSnapshotBytes = 4352;
    internal const int MaxDatagramBytes = 864;
    internal const int ChunkBytes = 200;
    private const int MetadataBytes = 12; // sequence:u64, snapshot length:u16, offset:u16
    private const int CompressedMetadataBytes = 15; // sequence:u64, original type:u8, raw/packed length:u16, offset:u16
    private const int CompressedChunkBytes = 197; // four chunks plus metadata total 862 bytes
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
        internal bool Compressed;
        internal int OriginalLength;
        internal double Started;
    }

    internal void Clear() => _peers.Clear();
    internal void Forget(int sender) => _peers.Remove(sender);

    internal static byte[][] Encode(byte[] snapshot, int length, ulong sequence,
        byte payloadType = NetProtocol.MsgExtras, byte envelopeType = NetProtocol.MsgExtrasFragments,
        int snapshotLimit = MaxSnapshotBytes, bool compress = false)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length
            || length > snapshotLimit || NetPacket.PeekType(snapshot, length) != payloadType)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        if (compress && PresentationCompression.TryCompress(snapshot, length) is byte[] packed)
            return EncodeCompressed(snapshot, length, sequence, payloadType, packed);
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

    private static byte[][] EncodeCompressed(byte[] original, int originalLength, ulong sequence,
        byte payloadType, byte[] packed)
    {
        int chunkCount = (packed.Length + CompressedChunkBytes - 1) / CompressedChunkBytes;
        var packets = new byte[(chunkCount + 3) / 4][];
        int offset = 0;
        for (int page = 0; page < packets.Length; page++)
        {
            int chunks = Math.Min(4, chunkCount - page * 4);
            int dataLength = Math.Min(chunks * CompressedChunkBytes, packed.Length - offset);
            var packet = new byte[6 + chunks * (2 + CompressedMetadataBytes) + dataLength];
            Buffer.BlockCopy(original, 0, packet, 0, 6);
            packet[5] = NetProtocol.MsgPresentationCompression;
            int at = 6;
            for (int c = 0; c < chunks; c++)
            {
                int n = Math.Min(CompressedChunkBytes, packed.Length - offset);
                packet[at++] = NetProtocol.ExtIdPresentationCompression;
                packet[at++] = (byte)(CompressedMetadataBytes + n);
                for (int b = 0; b < 8; b++) packet[at++] = (byte)(sequence >> (8 * b));
                packet[at++] = payloadType;
                packet[at++] = (byte)originalLength; packet[at++] = (byte)(originalLength >> 8);
                packet[at++] = (byte)packed.Length; packet[at++] = (byte)(packed.Length >> 8);
                packet[at++] = (byte)offset; packet[at++] = (byte)(offset >> 8);
                Buffer.BlockCopy(packed, offset, packet, at, n);
                offset += n; at += n;
            }
            packets[page] = packet;
        }
        return packets;
    }

    /// <summary>Returns a complete snapshot once. Partial, stale, duplicate or invalid data is inert.</summary>
    internal byte[]? Accept(int sender, byte[] packet, int length, double now)
    {
        if (packet == null || length < 6 || length > packet.Length || length > MaxDatagramBytes
            || double.IsNaN(now) || double.IsInfinity(now))
            return null;
        int message = NetPacket.PeekType(packet, length);
        bool compressed = message == NetProtocol.MsgPresentationCompression;
        if (compressed ? CompressedPayloadType(packet, length) != _payloadType : message != _envelopeType) return null;
        int record = compressed ? NetProtocol.ExtIdPresentationCompression : NetProtocol.ExtIdExtrasFragment;
        int metadata = compressed ? CompressedMetadataBytes : MetadataBytes;
        int chunkBytes = compressed ? CompressedChunkBytes : ChunkBytes;
        int lengthAt = compressed ? 11 : 8, offsetAt = compressed ? 13 : 10;
        // Validate the entire datagram before mutating any assembly. All chunks in a datagram
        // must identify the same snapshot; offsets and lengths are canonical and nonoverlapping.
        ulong sequence = 0;
        int total = 0;
        int originalLength = 0;
        bool found = false;
        for (int p = 6; p < length;)
        {
            if (p + 2 > length) return null;
            int type = packet[p++], n = packet[p++];
            if (p + n > length) return null;
            if (type == record)
            {
                if (n <= metadata) return null;
                ulong s = ReadSequence(packet, p);
                int t = packet[p + lengthAt] | packet[p + lengthAt + 1] << 8;
                int raw = compressed ? packet[p + 9] | packet[p + 10] << 8 : t;
                int offset = packet[p + offsetAt] | packet[p + offsetAt + 1] << 8;
                if (t < 6 || raw > _snapshotLimit || t > _snapshotLimit || offset >= t || offset % chunkBytes != 0
                    || n - metadata != Math.Min(chunkBytes, t - offset)) return null;
                if (found && (s != sequence || t != total || raw != originalLength)) return null;
                sequence = s; total = t; originalLength = raw; found = true;
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
                || state.Compressed != compressed || state.OriginalLength != originalLength
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
            state.Received = new bool[(total + chunkBytes - 1) / chunkBytes];
            state.Compressed = compressed;
            state.OriginalLength = originalLength;
            state.Count = 0;
            state.Started = now;
        }
        for (int p = 6; p < length;)
        {
            int type = packet[p++], n = packet[p++];
            if (type == record)
            {
                int offset = packet[p + offsetAt] | packet[p + offsetAt + 1] << 8;
                int index = offset / chunkBytes;
                if (!state.Received![index])
                {
                    Buffer.BlockCopy(packet, p + metadata, state.Bytes, offset, n - metadata);
                    state.Received[index] = true;
                    state.Count++;
                }
                else
                {
                    // Conflicting duplicates invalidate this generation rather than splicing data.
                    for (int b = 0; b < n - metadata; b++)
                        if (state.Bytes[offset + b] != packet[p + metadata + b])
                        { state.Bytes = null; state.Received = null; return null; }
                }
            }
            p += n;
        }
        if (state.Count != state.Received!.Length) return null;
        byte[] complete = state.Bytes;
        state.Bytes = null; state.Received = null;
        return compressed ? PresentationCompression.Expand(complete, originalLength, _snapshotLimit, _payloadType)
            : NetPacket.PeekType(complete, complete.Length) == _payloadType ? complete : null;
    }

    /// <summary>Validate every compressed page's routing metadata before selecting an assembler.</summary>
    internal static int CompressedPayloadType(byte[] packet, int length)
    {
        if (packet == null || length < 6 || length > packet.Length || length > MaxDatagramBytes
            || NetPacket.PeekType(packet, length) != NetProtocol.MsgPresentationCompression) return -1;
        int payload = -1, total = 0, rawLength = 0;
        ulong sequence = 0;
        for (int p = 6; p < length;)
        {
            if (p + 2 > length) return -1;
            int type = packet[p++], n = packet[p++];
            if (p + n > length || type != NetProtocol.ExtIdPresentationCompression || n <= CompressedMetadataBytes) return -1;
            int next = packet[p + 8], raw = packet[p + 9] | packet[p + 10] << 8;
            int packed = packet[p + 11] | packet[p + 12] << 8;
            int offset = packet[p + 13] | packet[p + 14] << 8;
            ulong seq = ReadSequence(packet, p);
            if (!PayloadAllowed(next) || raw < PresentationCompression.MinimumInput || packed < 6
                || packed > raw - PresentationCompression.MinimumSaving || offset >= packed
                || offset % CompressedChunkBytes != 0 || n - CompressedMetadataBytes != Math.Min(CompressedChunkBytes, packed - offset)
                || payload >= 0 && (next != payload || packed != total || raw != rawLength || seq != sequence)) return -1;
            payload = next; total = packed; rawLength = raw; sequence = seq;
            p += n;
        }
        return payload;
    }

    private static bool PayloadAllowed(int type) => type == NetProtocol.MsgExtras
        || type == NetProtocol.MsgUseBarAnimation || type == NetProtocol.MsgCardPlume
        || type == NetProtocol.MsgNativeUseBar || type == NetProtocol.MsgNativeBoard
        || type == NetProtocol.MsgCardAppearance || type == NetProtocol.MsgNativeDecisionPrompt;

    /// <summary>Message8 assigns the low five opaque sequence bits to its native slot stream.
    /// Validate routing metadata before choosing an assembler; Accept validates the whole page.</summary>
    internal static int NativeSlotStream(byte[] packet, int length)
    {
        if (packet == null || length < 20 || length > packet.Length || length > MaxDatagramBytes) return -1;
        bool compressed = NetPacket.PeekType(packet, length) == NetProtocol.MsgPresentationCompression;
        if (compressed ? CompressedPayloadType(packet, length) != NetProtocol.MsgNativeUseBar
            : NetPacket.PeekType(packet, length) != NetProtocol.MsgNativeUseBarFragments) return -1;
        int stream = -1;
        for (int p = 6; p < length;)
        {
            if (p + 2 > length) return -1;
            int type = packet[p++], n = packet[p++];
            if (p + n > length) return -1;
            if (type == (compressed ? NetProtocol.ExtIdPresentationCompression : NetProtocol.ExtIdExtrasFragment))
            {
                if (n <= (compressed ? CompressedMetadataBytes : MetadataBytes)) return -1;
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
