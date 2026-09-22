using System;
using System.IO;

namespace GloomhavenVR.Net;

/// <summary>One atomic native map-button picture, or an explicit disappearance.</summary>
internal sealed class MapButtonTooltipSnapshot
{
    internal readonly float SampleTime;
    // Immutable output owned by the presentation sampler; the transport never edits it.
    internal readonly byte[]? Payload;
    internal MapButtonTooltipSnapshot(float sampleTime, byte[]? payload)
    {
        if (float.IsNaN(sampleTime) || float.IsInfinity(sampleTime) || sampleTime < 0
            || payload != null && (payload.Length == 0 || payload.Length > MapButtonTooltipCodec.MaxPayload))
            throw new ArgumentException("Invalid map button tooltip snapshot.");
        SampleTime = sampleTime;
        Payload = payload;
    }

    internal static bool SameIdentity(MapButtonTooltipSnapshot a, MapButtonTooltipSnapshot b)
    {
        if (a.Payload == null || b.Payload == null) return a.Payload == b.Payload;
        // The native presentation schema starts with version and stable cap key. Switching
        // caps is an identity boundary even when both pictures happen within one send turn.
        return a.Payload.Length >= 2 && b.Payload.Length >= 2
            && a.Payload[0] == b.Payload[0] && a.Payload[1] == b.Payload[1];
    }
}

/// <summary>Message 23, additive TLV 82 pages. IDs 78–81 and messages 19–22 are reserved
/// by the separate NPC feature branch. Existing packet grammars remain unchanged.</summary>
internal static class MapButtonTooltipCodec
{
    internal const int MaxPayload = 16384;
    private const int PageBytes = 253;
    internal const int MaxSize = 6 + 4 + MaxPayload + ((4 + MaxPayload + PageBytes - 1) / PageBytes) * 4;

    internal static int Write(MapButtonTooltipSnapshot snapshot, byte[] buffer)
    {
        int count = 4 + (snapshot.Payload?.Length ?? 0);
        int pages = (count + PageBytes - 1) / PageBytes;
        int length = 6 + count + pages * 4;
        if (buffer == null || buffer.Length < length) return 0;
        var raw = new byte[count];
        using (var stream = new MemoryStream(raw))
        using (var writer = new BinaryWriter(stream)) writer.Write(snapshot.SampleTime);
        if (snapshot.Payload != null) Buffer.BlockCopy(snapshot.Payload, 0, raw, 4, snapshot.Payload.Length);
        int at = 0;
        AvatarSerializer.WriteU32(buffer, ref at, NetProtocol.Magic);
        buffer[at++] = NetProtocol.Version;
        buffer[at++] = NetProtocol.MsgMapButtonTooltip;
        for (int page = 0, offset = 0; page < pages; page++)
        {
            int size = Math.Min(PageBytes, count - offset);
            buffer[at++] = NetProtocol.ExtIdMapButtonTooltip;
            buffer[at++] = (byte)(size + 2);
            buffer[at++] = (byte)page;
            buffer[at++] = (byte)pages;
            Buffer.BlockCopy(raw, offset, buffer, at, size);
            at += size; offset += size;
        }
        return at;
    }

    internal static bool TryRead(byte[] buffer, int length, out MapButtonTooltipSnapshot? snapshot)
    {
        snapshot = null;
        if (buffer == null || length < 14 || length > buffer.Length || length > MaxSize
            || NetPacket.PeekType(buffer, length) != NetProtocol.MsgMapButtonTooltip) return false;
        using var raw = new MemoryStream();
        int at = 6, next = 0, pages = 0;
        while (at < length)
        {
            if (length - at < 2) return false;
            int id = buffer[at++], size = buffer[at++];
            if (size > length - at) return false;
            if (id == NetProtocol.ExtIdMapButtonTooltip)
            {
                if (size < 3 || buffer[at] != next || buffer[at + 1] == 0) return false;
                if (next == 0) pages = buffer[at + 1];
                if (buffer[at + 1] != pages || next >= pages
                    || next < pages - 1 && size != 255 || raw.Length + size - 2 > 4 + MaxPayload) return false;
                raw.Write(buffer, at + 2, size - 2);
                next++;
            }
            at += size;
        }
        if (next != pages || pages == 0 || raw.Length < 4) return false;
        raw.Position = 0;
        using var reader = new BinaryReader(raw);
        float time = reader.ReadSingle();
        if (float.IsNaN(time) || float.IsInfinity(time) || time < 0) return false;
        byte[]? payload = raw.Length == 4 ? null : reader.ReadBytes((int)raw.Length - 4);
        snapshot = new MapButtonTooltipSnapshot(time, payload);
        return true;
    }
}
