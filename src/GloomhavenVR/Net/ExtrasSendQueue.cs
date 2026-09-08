using System;

namespace GloomhavenVR.Net;

/// <summary>
/// Finish a complete snapshot before moving to waiting data. Presence keeps only the latest
/// waiting state. Native animation also retains the first waiting state, so a short show cannot
/// lose its initial pose before the sender's next turn. Both policies have bounded memory.
/// </summary>
internal sealed class ExtrasSendQueue
{
    private byte[][]? _pages;
    private int _page;
    private byte[]? _first;
    private byte[]? _latest;
    private double _next;
    private ulong _sequence;
    private readonly byte _payloadType;
    private readonly byte _envelopeType;
    private readonly bool _preserveFirst;

    internal ExtrasSendQueue(ulong sequence, byte payloadType = NetProtocol.MsgExtras,
        byte envelopeType = NetProtocol.MsgExtrasFragments, bool preserveFirst = false)
    {
        _sequence = sequence;
        _payloadType = payloadType;
        _envelopeType = envelopeType;
        _preserveFirst = preserveFirst;
    }

    internal void Clear() { _pages = null; _first = _latest = null; _page = 0; _next = 0; }

    internal void Enqueue(byte[] snapshot, int length)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length
            || length > ExtrasFragments.MaxSnapshotBytes
            || NetPacket.PeekType(snapshot, length) != _payloadType)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        var copy = new byte[length];
        Buffer.BlockCopy(snapshot, 0, copy, 0, length);
        if (_preserveFirst && _first == null) _first = copy;
        else _latest = copy;
    }

    internal byte[]? Next(double now)
    {
        if (now < _next) return null;
        if (_pages == null)
        {
            byte[]? next = _first ?? _latest;
            if (next == null) return null;
            if (_first != null)
            {
                _first = _latest;
                _latest = null;
            }
            else _latest = null;
            _pages = ExtrasFragments.Encode(next, next.Length, ++_sequence, _payloadType, _envelopeType);
            _page = 0;
        }
        byte[] result = _pages[_page++];
        if (_page == _pages.Length) _pages = null;
        _next = now + 0.05;
        return result;
    }
}

/// <summary>
/// One bounded event per 50 ms across both presentation streams and the legacy handshake.
/// Native motion gets two turns, then waiting presence gets one. A slow frame never causes a
/// catch-up burst, and a stream with no data never wastes the other stream's turn.
/// </summary>
internal sealed class ExtrasSendScheduler
{
    private readonly ExtrasSendQueue _presence;
    private readonly ExtrasSendQueue _animation;
    private readonly byte _animationType;
    private double _next;
    private int _animationTurns;

    internal ExtrasSendScheduler(ulong sequence, byte animationType, byte animationEnvelope)
    {
        _presence = new ExtrasSendQueue(sequence);
        _animation = new ExtrasSendQueue(sequence, animationType, animationEnvelope, preserveFirst: true);
        _animationType = animationType;
    }

    internal void Enqueue(byte[] snapshot, int length)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        if (NetPacket.PeekType(snapshot, length) == _animationType) _animation.Enqueue(snapshot, length);
        else _presence.Enqueue(snapshot, length);
    }

    internal byte[]? Next(double now, byte[]? announcement = null)
    {
        if (now < _next) return null;
        byte[]? result = announcement;
        if (result == null)
        {
            if (_animationTurns < 2) result = _animation.Next(now);
            if (result != null) _animationTurns++;
            else
            {
                result = _presence.Next(now);
                if (result != null) _animationTurns = 0;
                else
                {
                    result = _animation.Next(now);
                    if (result != null) _animationTurns = Math.Min(2, _animationTurns + 1);
                }
            }
        }
        if (result != null) _next = now + 0.05;
        return result;
    }

    internal void Clear()
    {
        _presence.Clear();
        _animation.Clear();
        _next = 0;
        _animationTurns = 0;
    }
}
