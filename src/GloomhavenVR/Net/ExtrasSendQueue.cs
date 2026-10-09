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
                        : a.Identity is NativeBoardState m && b.Identity is NativeBoardState n ? m.Generation == n.Generation
                        : a.Identity is TownServices.TownServiceFrame v && b.Identity is TownServices.TownServiceFrame u
                            && TownServices.TownServiceSendQueue.SameIdentity(v, u);
    }
    private double _next;
    private ulong _sequence;
    private readonly byte _payloadType;
    private readonly byte _envelopeType;
    private readonly bool _preserveFirst;
    private readonly int _snapshotLimit;
    private readonly ulong _sequenceStride;
    private readonly ulong _counterMask, _fixedMarker;
    private bool _sequenceExhausted;
    internal ulong Sequence => _sequence;
    internal bool HasInFlight => _pages != null;
    internal bool HasPending => _pending.Count > 0 || _first != null || _latest != null;
    internal object? CompletedIdentity { get; private set; }
    private object? _activeIdentity;

    // Two bounded queues may share a module fragment namespace, but never an
    // unfinished assembly. Its next counter must exceed both previous writers.
    internal void AdvanceSequence(ulong previous)
    {
        if (_pages != null) throw new InvalidOperationException("Cannot advance an unfinished presentation.");
        if ((previous & ~_counterMask) != _fixedMarker
            || previous % _sequenceStride != _sequence % _sequenceStride)
            throw new ArgumentException("Different presentation fragment namespace.", nameof(previous));
        if (previous > _sequence) _sequence = previous;
    }

    internal ExtrasSendQueue(ulong sequence, byte payloadType = NetProtocol.MsgExtras,
        byte envelopeType = NetProtocol.MsgExtrasFragments, bool preserveFirst = false,
        int snapshotLimit = ExtrasFragments.MaxSnapshotBytes, ulong sequenceStride = 1,
        ulong counterMask = ulong.MaxValue, ulong fixedMarker = 0)
    {
        if ((fixedMarker & counterMask) != 0 || counterMask == 0 || sequenceStride > counterMask
            || counterMask != ulong.MaxValue && (counterMask & (counterMask + 1)) != 0)
            throw new ArgumentException("Invalid bounded presentation sequence namespace.");
        _counterMask = counterMask; _fixedMarker = fixedMarker;
        _sequence = fixedMarker | (sequence & counterMask);
        _payloadType = payloadType;
        _envelopeType = envelopeType;
        _preserveFirst = preserveFirst;
        _snapshotLimit = snapshotLimit;
        _sequenceStride = sequenceStride;
    }

    internal void Clear() { _pending.Clear(); _pages = null; _first = _latest = null; _page = 0; _next = 0; CompletedIdentity = _activeIdentity = null; }

    internal void Enqueue(byte[] snapshot, int length, object? identity = null)
    {
        if (snapshot == null || length < 6 || length > snapshot.Length
            || length > _snapshotLimit
            || NetPacket.PeekType(snapshot, length) != _payloadType)
            throw new ArgumentException("Invalid presentation snapshot.", nameof(snapshot));
        if (_sequenceExhausted) return;
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

    // Town batching drains exactly the next immutable queued snapshot, preserving the
    // same first/latest transition order as normal per-module fragmentation.
    internal int PendingLength => !HasInFlight && _pending.Count > 0 ? _pending[0].Bytes.Length : 0;
    internal bool TryPeekPending(out byte[]? bytes, out object? identity)
    {
        bytes = null; identity = null; if (PendingLength == 0) return false;
        bytes = _pending[0].Bytes; identity = _pending[0].Identity; return true;
    }
    internal bool TryTakePending(out byte[]? bytes, out object? identity)
    {
        bytes=null;identity=null;if(PendingLength==0)return false;
        bytes=_pending[0].Bytes;identity=_pending[0].Identity;_pending.RemoveAt(0);return true;
    }

    // Only complete cumulative originals may supersede strictly older revisions
    // from this exact source. A genuinely newer delta keeps its named baseline;
    // native animation streams never enter this method.
    internal void SupersedeTownOriginal(TownServices.TownServiceFrame current)
    {
        if (current.BaseSequence != 0) return;
        // A complete newer owner picture replaces older cumulative deltas too.
        // Keeping their dependency after that replacement blocked a new mage
        // offer behind the entire previous fragmented picture. A genuinely newer
        // delta may still name the older baseline; retain that exact dependency.
        for (int i = _pending.Count - 1; i >= 0; i--)
            if (_pending[i].Identity is TownServices.TownServiceFrame older
                && CanSupersedeTownRevision(older, current)
                && (older.BaseSequence != 0 || !HasTownDelta(older.Sequence, current.Sequence)))
                _pending.RemoveAt(i);
        if (_activeIdentity is TownServices.TownServiceFrame active
            && CanSupersedeTownRevision(active, current)
            && (active.BaseSequence != 0 || !HasTownDelta(active.Sequence, current.Sequence)))
        { _pages = null; _page = 0; _activeIdentity = null; }
    }

    internal bool HasTownDelta(ulong baseline, ulong newerThan = 0)
    {
        if (_activeIdentity is TownServices.TownServiceFrame active
            && active.BaseSequence == baseline && active.Sequence > newerThan) return true;
        foreach (Pending pending in _pending)
            if (pending.Identity is TownServices.TownServiceFrame frame
                && frame.BaseSequence == baseline && frame.Sequence > newerThan) return true;
        return false;
    }

    internal static bool CanSupersedeTownRevision(TownServices.TownServiceFrame older, TownServices.TownServiceFrame current) =>
        current.BaseSequence == 0 && older.Sequence < current.Sequence
        && older.VisitorStock == current.VisitorStock && older.PublicCatalog == current.PublicCatalog
        && older.PublicClaim == current.PublicClaim && older.Session == current.Session
        && older.Service == current.Service && older.Module == current.Module;

    internal void PrependTownOriginal(byte[] bytes, TownServices.TownServiceFrame frame, bool preserveInFlight = false)
    {
        if (frame.BaseSequence != 0 || HasInFlight && !preserveInFlight)
            throw new InvalidOperationException("Cannot migrate an unfinished town module or delta.");
        var pending = _pending.ToArray(); _pending.Clear();
        var copy = new byte[bytes.Length]; Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
        _pending.Add(new Pending(copy, frame));
        foreach (Pending item in pending)
        {
            if (item.Identity is TownServices.TownServiceFrame duplicate && duplicate.BaseSequence == 0
                && duplicate.Sequence == frame.Sequence && duplicate.Module == frame.Module
                && duplicate.Session == frame.Session && duplicate.Service == frame.Service) continue;
            PresentationPending.Append(_pending, item, Pending.Same);
        }
    }

    internal static bool CanSupersedeTownOriginal(TownServices.TownServiceFrame older, TownServices.TownServiceFrame current) =>
        older.BaseSequence == 0 && current.BaseSequence == 0 && older.Sequence < current.Sequence
        && older.VisitorStock == current.VisitorStock && older.PublicCatalog == current.PublicCatalog
        && older.PublicClaim == current.PublicClaim && older.Session == current.Session
        && older.Service == current.Service && older.Module == current.Module;

    internal byte[]? Next(double now)
    {
        if (_sequenceExhausted || now < _next) return null;
        if (_pages == null)
        {
            byte[]? next = _pending.Count > 0 ? _pending[0].Bytes : _first ?? _latest;
            if (next == null) return null;
            _activeIdentity = _pending.Count > 0 ? _pending[0].Identity : null;
            if (_pending.Count > 0) _pending.RemoveAt(0);
            else if (_first != null)
            {
                _first = _latest;
                _latest = null;
            }
            else _latest = null;
            // A bounded cosmetic namespace fails once rather than rolling into
            // another lane or aliasing an old fragmented snapshot. Clear cannot
            // reset that counter; reconnect creates a fresh queue and assembler.
            if (_counterMask != ulong.MaxValue && (_sequence & _counterMask) > _counterMask - _sequenceStride)
            {
                _sequenceExhausted = true; Clear();
                throw new InvalidOperationException("Presentation fragment sequence namespace exhausted; reconnect to resume.");
            }
            _sequence = _fixedMarker | unchecked((_sequence + _sequenceStride) & _counterMask);
            _pages = ExtrasFragments.Encode(next, next.Length, _sequence, _payloadType, _envelopeType, _snapshotLimit, compress: true);
            _page = 0;
        }
        byte[] result = _pages[_page++];
        if (_page == _pages.Length) { _pages = null; CompletedIdentity = _activeIdentity; }
        _next = now + 0.05;
        return result;
    }
}

/// <summary>
/// One bounded event per 50 ms across independent presentation streams and the legacy handshake.
/// Small pages share an event; larger pages retain the same cap. Weighted stream turns and
/// round-robin auxiliary slots prevent starvation. A slow frame never causes a catch-up burst.
/// </summary>
internal sealed partial class ExtrasSendScheduler
{
    private readonly ExtrasSendQueue _presence;
    private readonly ExtrasSendQueue _animation;
    private readonly ExtrasSendQueue _plumes;
    private readonly ExtrasSendQueue _board;
    private readonly ExtrasSendQueue _appearance;
    private readonly ExtrasSendQueue _prompt;
    private readonly ExtrasSendQueue _itemAppearance;
    private readonly ExtrasSendQueue _mapTooltip;
    private readonly TownServices.TownServiceSendQueue _town;
    private byte[]? _heldPage;
    private readonly ExtrasSendQueue[] _native = new ExtrasSendQueue[32];
    private readonly byte _animationType;
    private double _next;
    private int _turn, _urgentTownTurn, _urgentTownDebt, _openingTownTurns, _nativeCursor = 8;

    internal ExtrasSendScheduler(ulong sequence, byte animationType, byte animationEnvelope)
    {
        _presence = new ExtrasSendQueue(sequence);
        _town = new TownServices.TownServiceSendQueue(sequence);
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
        if (type == NetProtocol.MsgTownOriginalReceipt) EnqueueOriginalReceipt(snapshot, length);
        else if (type == TownServices.TownServiceCodec.MessageType)
        {
            if (identity is not TownServices.TownServiceFrame town)
            { if (!TownServices.TownServiceCodec.TryRead(snapshot, length, out TownServices.TownServiceFrame? decoded)) return; town = decoded!; }
            _town.Enqueue(snapshot, length, town);
        }
        else if (type == _animationType)
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
        byte[]? result = announcement ?? TakeOriginalReceipt(now);
        bool ordinaryTurn = false;
        // A transaction's first exact original picture is finite. It may borrow
        // at most16 existing page turns to complete within the first second,
        // with one ordinary-stream turn after every four opening pages. This
        // independent finite reservation never adds an event, byte budget, or
        // catch-up send clock, and never waits on a previous offer's repair debt.
        bool openingBackground = _openingTownTurns >= 4;
        if (result == null && !openingBackground)
        {
            result = _town.NextOpening(now);
            if (result != null) _openingTownTurns++;
        }
        // Build614 cold original item/purse/confirmation output otherwise waits
        // through 21 global turns and another three NPC lanes before the first
        // front can exist. Reserve at most one of three page turns for urgent
        // originals, borrowing at most six later town turns for cold originals.
        // Repay each borrowed turn below: sustained priority traffic must never
        // reduce the original streams' 32-second maximum-frame completion share.
        // The event cap,
        // 50 ms clock, immutable asset descriptors and no-catch-up rule stay exact.
        if (result == null && !openingBackground && _urgentTownTurn++ % 3 == 0 && _urgentTownDebt < 6)
        {
            result = _town.NextUrgent(now);
            if (result != null) _urgentTownDebt++;
        }
        // Empty streams cost no turn. With only the original two streams populated this is
        // two animation pages followed by two presence pages; the larger durable burn record
        // must also assemble within the shorter presence lifetime when all streams are busy.
        // The extended native card hierarchy can fill 57 KiB before compression. Give it three
        // turns so even incompressible maximum frames finish within the unchanged 32 s assembly
        // lifetime under full contention. Original item output gets three turns, presence and
        // boards two each, and the native slot pool three. The 864-byte / 50 ms cap is unchanged.
        for (int attempt = 0; result == null && attempt < 21; attempt++)
        {
            int turn = _turn;
            _turn = (_turn + 1) % 21;
            if (openingBackground && turn >= 18) continue;
            if (turn >= 18 && _urgentTownDebt > 0) { _urgentTownDebt--; continue; }
            result = turn >= 18 ? _town.Next(now) : turn < 2 ? _animation.Next(now)
                : turn == 2 || turn == 13 ? _presence.Next(now)
                : turn == 3 ? _plumes.Next(now) : turn == 6 || turn == 15 ? _board.Next(now)
                : turn == 7 || turn == 8 || turn == 10 ? _appearance.Next(now)
                : turn == 11 || turn == 12 || turn == 14 ? _itemAppearance.Next(now)
                : turn == 9 ? _prompt.Next(now) : turn == 17 ? _mapTooltip.Next(now) : NextNative(now);
            ordinaryTurn = result != null && turn < 18;
        }
        if (openingBackground)
        {
            // One genuine ordinary-stream turn after at most four opening pages.
            // There is no debt from a previous offer that can postpone this one.
            if (ordinaryTurn) _openingTownTurns = 0;
            else if (result == null) { result = _town.NextOpening(now); if (result != null) _openingTownTurns = 1; }
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
        _town.Clear();
        ClearOriginalReceipts();
        for (int i = 8; i < _native.Length; i++) _native[i].Clear();
        _next = 0; _turn = _urgentTownTurn = _urgentTownDebt = _openingTownTurns = 0; _nativeCursor = 8;
    }
}
