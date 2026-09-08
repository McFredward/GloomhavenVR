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
    private readonly int _snapshotLimit;
    private readonly ulong _sequenceStride;

    internal ExtrasSendQueue(ulong sequence, byte payloadType = NetProtocol.MsgExtras,
        byte envelopeType = NetProtocol.MsgExtrasFragments, bool preserveFirst = false,
        int snapshotLimit = ExtrasFragments.MaxSnapshotBytes, ulong sequenceStride = 1)
    {
        _sequence = sequence;
        _payloadType = payloadType;
        _envelopeType = envelopeType;
        _preserveFirst = preserveFirst;
        _snapshotLimit = snapshotLimit;
        _sequenceStride = sequenceStride;
    }

    internal void Clear() { _pages = null; _first = _latest = null; _page = 0; _next = 0; }

    internal void Enqueue(byte[] snapshot, int length)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length
            || length > _snapshotLimit
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
            _sequence += _sequenceStride;
            _pages = ExtrasFragments.Encode(next, next.Length, _sequence, _payloadType, _envelopeType, _snapshotLimit);
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
    private readonly ExtrasSendQueue _plumes;
    private readonly ExtrasSendQueue _board;
    private byte[]? _heldPage;
    private readonly ExtrasSendQueue[] _native = new ExtrasSendQueue[32];
    private readonly byte _animationType;
    private double _next;
    private int _turn, _nativeCursor = 8;

    internal ExtrasSendScheduler(ulong sequence, byte animationType, byte animationEnvelope)
    {
        _presence = new ExtrasSendQueue(sequence);
        _animation = new ExtrasSendQueue(sequence, animationType, animationEnvelope, preserveFirst: true);
        _plumes = new ExtrasSendQueue(sequence, NetProtocol.MsgCardPlume, NetProtocol.MsgCardPlumeFragments,
            preserveFirst: true, snapshotLimit: CardPlumeCodec.MaxSize);
        _board = new ExtrasSendQueue(sequence, NetProtocol.MsgNativeBoard, NetProtocol.MsgNativeBoardFragments,
            preserveFirst: true, snapshotLimit: 12288);
        for (int slot = 8; slot < _native.Length; slot++)
            _native[slot] = new ExtrasSendQueue((sequence & ~31UL) | (uint)slot,
                NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments,
                preserveFirst: true, snapshotLimit: NativeUseBarPacket.MaxSize, sequenceStride: 32);
        _animationType = animationType;
    }

    internal void Enqueue(byte[] snapshot, int length, int nativeSlot = -1)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        int type = NetPacket.PeekType(snapshot, length);
        if (type == _animationType) _animation.Enqueue(snapshot, length);
        else if (type == NetProtocol.MsgNativeBoard) _board.Enqueue(snapshot, length);
        else if (type == NetProtocol.MsgCardPlume) _plumes.Enqueue(snapshot, length);
        else if (type == NetProtocol.MsgNativeUseBar && nativeSlot >= 8 && nativeSlot < 32)
            _native[nativeSlot].Enqueue(snapshot, length);
        else _presence.Enqueue(snapshot, length);
    }

    internal byte[]? Next(double now, byte[]? announcement = null)
    {
        if (now < _next) return null;
        byte[]? result = TakeNext(now, announcement);
        if (result != null) _next = now + 0.05;
        return result;
    }

    internal byte[]? NextBatch(double now, byte[]? announcement = null)
    {
        if (now < _next) return null;
        if (announcement != null) { _next = now + .05; return announcement; }
        byte[]? first = _heldPage ?? TakeNext(now);
        _heldPage = null;
        if (first == null) return null;
        var pages = new System.Collections.Generic.List<byte[]>(8) { first };
        int length = 9 + first.Length;
        while (length + 8 <= PresentationBatch.MaxSize && pages.Count < 32)
        {
            byte[]? next = TakeNext(now);
            if (next == null) break;
            if (length + 2 + next.Length > PresentationBatch.MaxSize) { _heldPage = next; break; }
            pages.Add(next); length += 2 + next.Length;
        }
        _next = now + .05;
        return pages.Count == 1 ? first : PresentationBatch.Write(pages);
    }

    private byte[]? TakeNext(double now, byte[]? announcement = null)
    {
        byte[]? result = announcement;
        // Empty streams cost no turn. With only the original two streams populated this is
        // still exactly two animation pages followed by one waiting presence page.
        for (int attempt = 0; result == null && attempt < 7; attempt++)
        {
            int turn = _turn;
            _turn = (_turn + 1) % 7;
            result = turn < 2 ? _animation.Next(now)
                : turn == 2 ? _presence.Next(now)
                : turn == 3 ? _plumes.Next(now) : turn == 6 ? _board.Next(now) : NextNative(now);
        }
        return result;
    }

    private byte[]? NextNative(double now)
    {
        for (int i = 0; i < 24; i++)
        {
            int slot = _nativeCursor;
            _nativeCursor = _nativeCursor == 31 ? 8 : _nativeCursor + 1;
            byte[]? page = _native[slot].Next(now);
            if (page != null) return page;
        }
        return null;
    }

    internal void Clear()
    {
        _presence.Clear(); _animation.Clear(); _plumes.Clear(); _board.Clear(); _heldPage = null;
        for (int i = 8; i < _native.Length; i++) _native[i].Clear();
        _next = 0; _turn = 0; _nativeCursor = 8;
    }
}
