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
    private readonly System.Collections.Generic.List<Pending> _pending = new(4);
    private sealed class Pending
    {
        internal readonly byte[] Bytes;
        internal readonly object Identity;
        internal Pending(byte[] bytes, object identity) { Bytes = bytes; Identity = identity; }
        internal static bool Same(Pending a, Pending b) =>
            a.Identity is NativeUseBarSnapshot x && b.Identity is NativeUseBarSnapshot y
                ? PresentationPending.SameNativeIdentity(x, y)
                : a.Identity is UseBarAnimationSnapshot p && b.Identity is UseBarAnimationSnapshot q
                    ? PresentationPending.SameBonusIdentity(p, q)
                    : a.Identity is NativeDecisionPromptSnapshot h && b.Identity is NativeDecisionPromptSnapshot j
                        ? NativeDecisionPromptSnapshot.SameIdentity(h, j)
                    : a.Identity is CardAppearanceSnapshot c && b.Identity is CardAppearanceSnapshot d
                        ? CardAppearanceSnapshot.SameIdentity(c, d)
                    : a.Identity is ItemAppearanceSnapshot e && b.Identity is ItemAppearanceSnapshot f
                        ? ItemAppearanceSnapshot.SameIdentity(e, f)
                    : a.Identity is MapButtonTooltipSnapshot s && b.Identity is MapButtonTooltipSnapshot t
                        ? MapButtonTooltipSnapshot.SameIdentity(s, t)
                        : a.Identity is NativeBoardState m && b.Identity is NativeBoardState n && m.Generation == n.Generation;
    }
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

    internal void Clear() { _pending.Clear(); _pages = null; _first = _latest = null; _page = 0; _next = 0; }

    internal void Enqueue(byte[] snapshot, int length, object? identity = null)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length
            || length > _snapshotLimit
            || NetPacket.PeekType(snapshot, length) != _payloadType)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        var copy = new byte[length];
        Buffer.BlockCopy(snapshot, 0, copy, 0, length);
        if (identity != null)
        {
            PresentationPending.Append(_pending, new Pending(copy, identity), Pending.Same);
            return;
        }
        if (_preserveFirst && _first == null) _first = copy;
        else _latest = copy;
    }

    internal byte[]? Next(double now)
    {
        if (now < _next) return null;
        if (_pages == null)
        {
            byte[]? next = _pending.Count > 0 ? _pending[0].Bytes : _first ?? _latest;
            if (next == null) return null;
            if (_pending.Count > 0) _pending.RemoveAt(0);
            else if (_first != null)
            {
                _first = _latest;
                _latest = null;
            }
            else _latest = null;
            _sequence += _sequenceStride;
            _pages = ExtrasFragments.Encode(next, next.Length, _sequence, _payloadType, _envelopeType, _snapshotLimit, compress: true);
            _page = 0;
        }
        byte[] result = _pages[_page++];
        if (_page == _pages.Length) _pages = null;
        _next = now + 0.05;
        return result;
    }
}

/// <summary>
/// One bounded event per 50 ms across independent presentation streams and the legacy handshake.
/// Small pages share an event; larger pages retain the same cap. Weighted stream turns and
/// round-robin auxiliary slots prevent starvation. A slow frame never causes a catch-up burst.
/// </summary>
internal sealed class ExtrasSendScheduler
{
    private readonly ExtrasSendQueue _presence;
    private readonly ExtrasSendQueue _animation;
    private readonly ExtrasSendQueue _plumes;
    private readonly ExtrasSendQueue _board;
    private readonly ExtrasSendQueue _appearance;
    private readonly ExtrasSendQueue _prompt;
    private readonly ExtrasSendQueue _itemAppearance;
    private readonly ExtrasSendQueue _mapTooltip;
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
            preserveFirst: true, snapshotLimit: NativeBoardCodec.MaxSize);
        _appearance = new ExtrasSendQueue(sequence, NetProtocol.MsgCardAppearance, NetProtocol.MsgCardAppearanceFragments,
            preserveFirst: true, snapshotLimit: CardAppearanceCodec.MaxSize);
        _itemAppearance = new ExtrasSendQueue(sequence, NetProtocol.MsgItemAppearance, NetProtocol.MsgItemAppearanceFragments,
            preserveFirst: true, snapshotLimit: ItemAppearanceCodec.MaxSize);
        _mapTooltip = new ExtrasSendQueue(sequence, NetProtocol.MsgMapButtonTooltip, NetProtocol.MsgMapButtonTooltipFragments,
            preserveFirst: true, snapshotLimit: MapButtonTooltipCodec.MaxSize);
        _prompt = new ExtrasSendQueue(sequence, NetProtocol.MsgNativeDecisionPrompt, NetProtocol.MsgNativeDecisionPromptFragments,
            preserveFirst: true, snapshotLimit: NativeDecisionPromptCodec.MaxSize);
        for (int slot = 8; slot < _native.Length; slot++)
            _native[slot] = new ExtrasSendQueue((sequence & ~31UL) | (uint)slot,
                NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments,
                preserveFirst: true, snapshotLimit: NativeUseBarPacket.MaxSize, sequenceStride: 32);
        _animationType = animationType;
    }

    internal void Enqueue(byte[] snapshot, int length, int nativeSlot = -1, object? identity = null)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        int type = NetPacket.PeekType(snapshot, length);
        if (type == _animationType)
        {
            if (identity is not UseBarAnimationSnapshot && UseBarAnimationCodec.TryRead(snapshot, length, out UseBarAnimationSnapshot? animation))
                identity = animation;
            _animation.Enqueue(snapshot, length, identity);
        }
        else if (type == NetProtocol.MsgNativeBoard)
        {
            if (identity is not NativeBoardState && NativeBoardCodec.TryRead(snapshot, length, out NativeBoardState? board)) identity = board;
            _board.Enqueue(snapshot, length, identity);
        }
        else if (type == NetProtocol.MsgNativeDecisionPrompt)
        {
            if (identity is not NativeDecisionPromptSnapshot && NativeDecisionPromptCodec.TryRead(snapshot, length, out NativeDecisionPromptSnapshot? prompt)) identity = prompt;
            _prompt.Enqueue(snapshot, length, identity);
        }
        else if (type == NetProtocol.MsgCardAppearance)
        {
            if (identity is not CardAppearanceSnapshot && CardAppearanceCodec.TryRead(snapshot, length, out CardAppearanceSnapshot? appearance)) identity = appearance;
            _appearance.Enqueue(snapshot, length, identity);
        }
        else if (type == NetProtocol.MsgItemAppearance)
        {
            if (identity is not ItemAppearanceSnapshot && ItemAppearanceCodec.TryRead(snapshot, length, out ItemAppearanceSnapshot? appearance)) identity = appearance;
            _itemAppearance.Enqueue(snapshot, length, identity);
        }
        else if (type == NetProtocol.MsgMapButtonTooltip)
        {
            if (identity is not MapButtonTooltipSnapshot && MapButtonTooltipCodec.TryRead(snapshot, length, out MapButtonTooltipSnapshot? tooltip)) identity = tooltip;
            _mapTooltip.Enqueue(snapshot, length, identity);
        }
        else if (type == NetProtocol.MsgCardPlume) _plumes.Enqueue(snapshot, length);
        else if (type == NetProtocol.MsgNativeUseBar && nativeSlot >= 8 && nativeSlot < 32)
        {
            if (identity is not NativeUseBarSnapshot && NativeUseBarPacket.TryRead(snapshot, length, out NativeUseBarSnapshot? native)) identity = native;
            _native[nativeSlot].Enqueue(snapshot, length, identity);
        }
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
        // two animation pages followed by two presence pages; the larger durable burn record
        // must also assemble within the shorter presence lifetime when all streams are busy.
        // The extended native card hierarchy can fill 57 KiB before compression. Give it three
        // turns so even incompressible maximum frames finish within the unchanged 32 s assembly
        // lifetime under full contention. Original item output gets three turns, presence and
        // boards two each, and the native slot pool three. The 864-byte / 50 ms cap is unchanged.
        for (int attempt = 0; result == null && attempt < 18; attempt++)
        {
            int turn = _turn;
            _turn = (_turn + 1) % 18;
            result = turn < 2 ? _animation.Next(now)
                : turn == 2 || turn == 13 ? _presence.Next(now)
                : turn == 3 ? _plumes.Next(now) : turn == 6 || turn == 15 ? _board.Next(now)
                : turn == 7 || turn == 8 || turn == 10 ? _appearance.Next(now)
                : turn == 11 || turn == 12 || turn == 14 ? _itemAppearance.Next(now)
                : turn == 9 ? _prompt.Next(now) : turn == 17 ? _mapTooltip.Next(now) : NextNative(now);
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
        _presence.Clear(); _animation.Clear(); _plumes.Clear(); _board.Clear(); _appearance.Clear(); _prompt.Clear(); _itemAppearance.Clear(); _mapTooltip.Clear(); _heldPage = null;
        for (int i = 8; i < _native.Length; i++) _native[i].Clear();
        _next = 0; _turn = 0; _nativeCursor = 8;
    }
}
