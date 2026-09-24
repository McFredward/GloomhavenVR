using System;

namespace GloomhavenVR.Net;

/// <summary>One native map narrative opening. Tokens address only their sender's history.</summary>
internal sealed class MapStoryOpening
{
    internal uint Epoch, Token, PreviousToken, SemanticKey, ContentKey;
    internal uint PageRevision;
    internal byte Page = byte.MaxValue, PageCount;
    internal bool Finished, Bidirectional;
    internal int[] Participants = Array.Empty<int>();
}

/// <summary>Additive record 83. Existing record 21 remains unchanged and carries window poses.</summary>
internal static class MapStoryLifecycleCodec
{
    internal const byte RecordId = 83;
    internal const int MaxEntries = 6, MaxParticipants = 3;
    internal const int MaxPayload = 1 + MaxEntries * (28 + 4 * MaxParticipants);

    internal static bool Valid(MapStoryOpening entry)
    {
        if (entry.Epoch == 0 || entry.Token == 0 || entry.PreviousToken >= entry.Token || entry.SemanticKey == 0
            || entry.ContentKey == 0 || entry.PageCount == 0
            || (entry.Page != byte.MaxValue && entry.Page >= entry.PageCount)
            || entry.Participants == null || entry.Participants.Length > MaxParticipants) return false;
        for (int i = 0; i < entry.Participants.Length; ++i)
        {
            if (entry.Participants[i] <= 0) return false;
            for (int j = 0; j < i; ++j)
                if (entry.Participants[j] == entry.Participants[i]) return false;
        }
        return true;
    }

    internal static bool Write(byte[] buffer, ref int offset, MapStoryOpening[]? entries, byte recordId = RecordId)
    {
        if (entries == null || entries.Length == 0 || entries.Length > MaxEntries) return false;
        int length = 1;
        for (int i = 0; i < entries.Length; ++i)
        {
            MapStoryOpening entry = entries[i];
            if (entry == null || !Valid(entry)) return false;
            if (entry.Epoch != entries[0].Epoch) return false;
            for (int j = 0; j < i; ++j) if (entries[j].Token == entry.Token) return false;
            length += 28 + 4 * entry.Participants.Length;
        }
        if (offset < 0 || offset > buffer.Length - length - 2) return false;
        buffer[offset++] = recordId;
        buffer[offset++] = (byte)length;
        buffer[offset++] = (byte)entries.Length;
        foreach (MapStoryOpening entry in entries)
        {
            Put(buffer, ref offset, entry.Epoch);
            Put(buffer, ref offset, entry.Token); Put(buffer, ref offset, entry.PreviousToken);
            Put(buffer, ref offset, entry.SemanticKey); Put(buffer, ref offset, entry.ContentKey);
            Put(buffer, ref offset, entry.PageRevision);
            buffer[offset++] = entry.Page; buffer[offset++] = entry.PageCount;
            buffer[offset++] = (byte)((entry.Finished ? 1 : 0) | (entry.Bidirectional ? 2 : 0));
            buffer[offset++] = (byte)entry.Participants.Length;
            foreach (int peer in entry.Participants) Put(buffer, ref offset, (uint)peer);
        }
        return true;
    }

    internal static bool TryRead(byte[] buffer, int offset, int length, out MapStoryOpening[] entries)
    {
        entries = Array.Empty<MapStoryOpening>();
        if (offset < 0 || length < 29 || length > MaxPayload || offset > buffer.Length - length) return false;
        int end = offset + length;
        int count = buffer[offset++];
        if (count < 1 || count > MaxEntries) return false;
        var result = new MapStoryOpening[count];
        for (int i = 0; i < count; ++i)
        {
            if (end - offset < 28) return false;
            var entry = new MapStoryOpening {
                Epoch = Get(buffer, ref offset), Token = Get(buffer, ref offset), PreviousToken = Get(buffer, ref offset),
                SemanticKey = Get(buffer, ref offset), ContentKey = Get(buffer, ref offset),
                PageRevision = Get(buffer, ref offset),
                Page = buffer[offset++], PageCount = buffer[offset++] };
            byte flags = buffer[offset++];
            int peers = buffer[offset++];
            if (flags > 3 || peers > MaxParticipants || end - offset < peers * 4) return false;
            entry.Finished = (flags & 1) != 0;
            entry.Bidirectional = (flags & 2) != 0;
            entry.Participants = new int[peers];
            for (int n = 0; n < peers; ++n) entry.Participants[n] = unchecked((int)Get(buffer, ref offset));
            if (!Valid(entry)) return false;
            if (i > 0 && entry.Epoch != result[0].Epoch) return false;
            for (int n = 0; n < i; ++n) if (result[n].Token == entry.Token) return false;
            result[i] = entry;
        }
        if (offset != end) return false;
        entries = result;
        return true;
    }

    private static void Put(byte[] buffer, ref int offset, uint value)
    {
        for (int n = 0; n < 4; ++n) buffer[offset++] = (byte)(value >> (n * 8));
    }
    private static uint Get(byte[] buffer, ref int offset)
    {
        uint value = 0;
        for (int n = 0; n < 4; ++n) value |= (uint)buffer[offset++] << (n * 8);
        return value;
    }
}
