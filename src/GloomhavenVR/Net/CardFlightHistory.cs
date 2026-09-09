using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

internal sealed class CardFlightEvent
{
    internal readonly byte Sequence, Endpoints, Flags;
    internal readonly CardFlightSource? Source;
    internal CardFlightEvent(byte sequence, byte endpoints, byte flags, CardFlightSource? source)
    { Sequence = sequence; Endpoints = endpoints; Flags = flags; Source = source; }
}

/// <summary>Recent semantic flights survive presence coalescing and packet loss together.
/// Empty initial snapshots establish the sequence before the first actual animation.</summary>
internal sealed class CardFlightHistory
{
    internal const int CountMax = 8, MaxSize = 2 + CountMax * 10;
    internal readonly byte Sequence;
    internal readonly CardFlightEvent[] Events;
    internal CardFlightHistory(byte sequence, CardFlightEvent[] events)
    { Sequence = sequence; Events = (CardFlightEvent[])events.Clone(); }

    internal IEnumerable<CardFlightEvent> Since(byte sequence)
    {
        foreach (CardFlightEvent flight in Events)
            if (NetProtocol.IsNewCardFxSequence(flight.Sequence, sequence))
            { sequence = flight.Sequence; yield return flight; }
    }

    internal int Write(byte[] buffer, int offset)
    {
        int size = 2 + Events.Length * 10;
        if (Events.Length > CountMax || offset < 0 || offset > buffer.Length - size) return 0;
        int at = offset;
        buffer[at++] = Sequence;
        buffer[at++] = (byte)Events.Length;
        foreach (CardFlightEvent flight in Events)
        {
            buffer[at++] = flight.Sequence;
            buffer[at++] = flight.Endpoints;
            buffer[at++] = flight.Flags;
            buffer[at++] = (byte)(flight.Source != null ? 1 : 0);
            AvatarSerializer.WriteI32(buffer, ref at, flight.Source?.ActorId ?? 0);
            buffer[at++] = flight.Source?.Seat ?? 0;
            buffer[at++] = flight.Source?.Count ?? 0;
        }
        return size;
    }

    internal static bool TryRead(byte[] buffer, int offset, int length, out CardFlightHistory? history)
    {
        history = null;
        if (buffer == null || offset < 0 || length < 2 || length > MaxSize || offset > buffer.Length - length) return false;
        int at = offset;
        byte sequence = buffer[at++], count = buffer[at++];
        if (count > CountMax || length != 2 + count * 10) return false;
        var events = new CardFlightEvent[count];
        for (int i = 0; i < count; i++)
        {
            byte seq = buffer[at++], endpoints = buffer[at++], flags = buffer[at++], hasSource = buffer[at++];
            int actor = AvatarSerializer.ReadI32(buffer, ref at);
            byte seat = buffer[at++], seats = buffer[at++];
            if ((endpoints & 15) > (byte)CardFxAnchor.Active || (endpoints >> 4) > (byte)CardFxAnchor.Active
                || (flags & ~CardFlightVisibility.CoveredBurnBit) != 0 || hasSource > 1
                || i > 0 && (byte)(seq - events[i - 1].Sequence) != 1) return false;
            CardFlightSource? source = hasSource == 1 ? new CardFlightSource(actor, seat, seats) : null;
            if (source != null ? !source.Value.Validate() : actor != 0 || seat != 0 || seats != 0) return false;
            events[i] = new CardFlightEvent(seq, endpoints, flags, source);
        }
        if (count > 0 && events[count - 1].Sequence != sequence) return false;
        history = new CardFlightHistory(sequence, events);
        return true;
    }
}
