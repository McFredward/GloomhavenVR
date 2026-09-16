using System;

namespace GloomhavenVR.Net;

/// <summary>Record75 ties each native burn completion to its existing flight sequence.
/// Times use the owner's card-appearance clock; record62 and card privacy stay unchanged.</summary>
internal sealed class CardBurnCompletionHistory
{
    internal const int MaxSize = 2 + CardFlightHistory.CountMax * 5;
    internal readonly byte Sequence;
    internal readonly (byte Sequence, float Time)[] Entries;
    internal CardBurnCompletionHistory(byte sequence, (byte Sequence, float Time)[] entries)
    { Sequence = sequence; Entries = ((byte Sequence, float Time)[])entries.Clone(); }
    internal float Find(byte sequence)
    {
        foreach (var entry in Entries) if (entry.Sequence == sequence) return entry.Time;
        return -1f;
    }
    private bool Valid()
    {
        if (Entries.Length > CardFlightHistory.CountMax) return false;
        for (int i = 0; i < Entries.Length; i++)
        {
            var entry = Entries[i];
            if (float.IsNaN(entry.Time) || float.IsInfinity(entry.Time) || entry.Time < 0f
                || (byte)(Sequence - entry.Sequence) >= CardFlightHistory.CountMax) return false;
            if (i > 0 && !NetProtocol.IsNewCardFxSequence(entry.Sequence, Entries[i - 1].Sequence)) return false;
        }
        return true;
    }
    internal int Write(byte[] buffer, int offset)
    {
        int length = 2 + Entries.Length * 5;
        if (!Valid() || offset < 0 || offset > buffer.Length - length) return 0;
        int at = offset;
        buffer[at++] = Sequence; buffer[at++] = (byte)Entries.Length;
        foreach (var entry in Entries)
        { buffer[at++] = entry.Sequence; AvatarSerializer.WriteF32(buffer, ref at, entry.Time); }
        return length;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out CardBurnCompletionHistory? history)
    {
        history = null;
        if (offset < 0 || length < 2 || length > MaxSize || offset > buffer.Length - length) return false;
        int at = offset; byte sequence = buffer[at++], count = buffer[at++];
        if (count > CardFlightHistory.CountMax || length != 2 + count * 5) return false;
        var entries = new (byte Sequence, float Time)[count];
        for (int i = 0; i < count; i++)
            entries[i] = (buffer[at++], AvatarSerializer.ReadF32(buffer, ref at));
        var candidate = new CardBurnCompletionHistory(sequence, entries);
        if (!candidate.Valid()) return false;
        history = candidate; return true;
    }
}
