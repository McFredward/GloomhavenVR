using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Record122 atomically accompanies unchanged recent62 and durable75 records.
/// A native active list may already have removed a dismissed card before the first staged118
/// packet reaches an observer. Its original pool and one actual launch pose therefore travel
/// with the event; terminal burn origins survive the recent history's two-second expiry.
/// Each independently length-delimited page has a complete cohort header. Missing/duplicate
/// pages, changed sequence/counts and invalid members expose no partial dismissal context.</summary>
internal sealed class CardFlightOrigins
{
    internal const int HeaderBytes = 7, RecentEntryBytes = 41, DurableEntryBytes = 33;
    internal const int RecentPageEntries = 6, DurablePageEntries = 7;
    internal const int MaxRecentPages = 2, MaxDurablePages = 19;
    internal const int MaxSize = 8 * RecentEntryBytes + MaxRecentPages * (HeaderBytes + 2)
        + 128 * DurableEntryBytes + MaxDurablePages * (HeaderBytes + 2); // 4741

    private byte _sequence, _scopeMask;
    private readonly CardDismissFlightOrigin?[][] _origins = new CardDismissFlightOrigin?[2][];
    private readonly int[] _nativeCounts = { -1, -1 };
    private readonly int[] _pageCounts = new int[2];
    private readonly uint[] _pagesSeen = new uint[2];

    internal byte Sequence => _sequence;
    internal bool NeedsRecent => (_scopeMask & 1) != 0;
    internal bool NeedsDurable => (_scopeMask & 2) != 0;

    internal static CardFlightOrigins? Capture(CardFlightHistory history, CardBurnCompletionHistory completions)
    {
        bool any = false;
        foreach (var entry in history.Events) any |= entry.DismissOrigin != null;
        foreach (var entry in completions.Entries) any |= entry.DismissOrigin != null;
        if (!any) return null;
        var result = new CardFlightOrigins { _sequence = history.Sequence };
        var canonical = CanonicalCompletions(completions);
        for (int scope = 0; scope < 2; scope++)
        {
            int length = scope == 0 ? history.Events.Length : completions.Entries.Length;
            var entries = new CardDismissFlightOrigin?[length]; int count = 0;
            for (int i = 0; i < length; i++)
            {
                entries[i] = scope == 0 ? history.Events[i].DismissOrigin : canonical[i].DismissOrigin;
                if (entries[i] != null) count++;
            }
            if (count == 0) continue;
            result._scopeMask |= (byte)(1 << scope);
            result._origins[scope] = entries; result._nativeCounts[scope] = length;
            result._pageCounts[scope] = (count + PerPage(scope) - 1) / PerPage(scope);
            result._pagesSeen[scope] = (1u << result._pageCounts[scope]) - 1;
        }
        return result._scopeMask == 0 ? null : result;
    }

    internal bool Complete
    {
        get
        {
            if (_scopeMask == 0 || (_scopeMask & ~3) != 0) return false;
            for (int scope = 0; scope < 2; scope++)
                if ((_scopeMask & (1 << scope)) != 0 && (_nativeCounts[scope] < 0
                    || _pagesSeen[scope] != (1u << _pageCounts[scope]) - 1)) return false;
            return true;
        }
    }

    internal bool Matches(in PresenceState state)
    {
        if (!Complete) return false;
        var canonical = state.BurnCompletions != null ? CanonicalCompletions(state.BurnCompletions) : Array.Empty<CardBurnCompletion>();
        for (int scope = 0; scope < 2; scope++)
        {
            if ((_scopeMask & (1 << scope)) == 0) continue;
            if (scope == 0 ? state.FlightHistory == null || state.FlightHistory.Sequence != _sequence
                || state.FlightHistory.Events.Length != _nativeCounts[scope]
                : state.BurnCompletions == null || state.BurnCompletions.Sequence != _sequence
                || state.BurnCompletions.Entries.Length != _nativeCounts[scope]) return false;
            for (int i = 0; i < _nativeCounts[scope]; i++)
            {
                CardDismissFlightOrigin? origin = _origins[scope][i];
                if (origin == null) continue;
                byte endpoints; CardFlightSource? source;
                if (scope == 0)
                {
                    var flight = state.FlightHistory!.Events[i]; endpoints = flight.Endpoints; source = flight.Source;
                    if (!origin.Valid) return false;
                }
                else
                {
                    var completion = canonical[i];
                    if (completion.InProgress || completion.NoFlightCompleted || !origin.GeometryValid) return false;
                    endpoints = completion.Endpoints; source = completion.Source;
                    if (origin.SourceActorId != 0 && (origin.SourceActorId != completion.SourceActorId
                        || origin.PoolSeat != completion.PoolSeat || origin.PoolCount != completion.PoolCount)) return false;
                }
                if ((endpoints & 15) != (byte)CardFxAnchor.Active
                    || (NetCardFx.To(endpoints) != CardFxAnchor.Discard && NetCardFx.To(endpoints) != CardFxAnchor.Burnt)
                    || !source.HasValue || !source.Value.Validate() || source.Value.Count == 0) return false;
            }
        }
        if ((_scopeMask & 3) == 3)
            for (int i = 0; i < _nativeCounts[0]; i++)
            {
                var origin = _origins[0][i];
                if (origin == null) continue;
                var flight = state.FlightHistory!.Events[i];
                for (int index = 0; index < canonical.Length; index++)
                {
                    var completion = canonical[index];
                    if ((completion.Flags & CardBurnCompletion.RecentFlightBit) == 0
                        || completion.Sequence != flight.Sequence || completion.Endpoints != flight.Endpoints
                        || !flight.Source.HasValue || completion.ActorId != flight.Source.Value.ActorId
                        || completion.Seat != flight.Source.Value.Seat || completion.Count != flight.Source.Value.Count) continue;
                    var durable = _origins[1][index];
                    if (durable == null || origin.SourceActorId != completion.SourceActorId
                        || origin.PoolSeat != completion.PoolSeat || origin.PoolCount != completion.PoolCount
                        || !SameGeometry(origin, durable)) return false;
                }
            }
        return true;
    }

    internal bool Apply(ref PresenceState state)
    {
        if (!Matches(in state)) return false;
        if ((_scopeMask & 1) != 0)
        {
            var history = state.FlightHistory!;
            var entries = new CardFlightEvent[history.Events.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var old = history.Events[i]; var origin = _origins[0][i];
                entries[i] = new CardFlightEvent(old.Sequence, old.Endpoints, old.Flags, old.Source, origin);
                if (state.HasCardFx && old.Sequence == state.FxSeq && old.Endpoints == state.FxEndpoints
                    && state.FlightSource.HasValue && old.Source.HasValue
                    && state.FlightSource.Value.ActorId == old.Source.Value.ActorId
                    && state.FlightSource.Value.Seat == old.Source.Value.Seat
                    && state.FlightSource.Value.Count == old.Source.Value.Count)
                    state.FxDismissOrigin = origin;
            }
            state.FlightHistory = new CardFlightHistory(history.Sequence, entries);
        }
        if ((_scopeMask & 2) != 0)
        {
            var history = state.BurnCompletions!;
            var canonical = CanonicalCompletions(history);
            var indices = new Dictionary<(int, int, ushort, ushort), int>();
            for (int i = 0; i < canonical.Length; i++) indices[canonical[i].Key] = i;
            var entries = new CardBurnCompletion[history.Entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var old = history.Entries[i]; var origin = _origins[1][indices[old.Key]];
                if (origin != null) origin = new CardDismissFlightOrigin(old.SourceActorId, old.PoolSeat,
                    old.PoolCount, origin.BerthPose, origin.Width);
                entries[i] = new CardBurnCompletion(old.Sequence, old.Endpoints, old.Flags, old.Time,
                    old.ActorId, old.SourceActorId, old.PoolSeat, old.PoolCount, old.Seat, old.Count, origin);
            }
            state.BurnCompletions = new CardBurnCompletionHistory(history.Sequence, entries);
        }
        return true;
    }

    internal int Write(byte[] buffer, int offset, out byte recordCount)
    {
        recordCount = 0;
        if (!Complete) return 0;
        int size = 0;
        for (int scope = 0; scope < 2; scope++)
        {
            if ((_scopeMask & (1 << scope)) == 0) continue;
            int count = 0;
            foreach (var origin in _origins[scope])
                if (origin != null)
                {
                    if (scope == 0 ? !origin.Valid : !origin.GeometryValid) return 0;
                    count++;
                }
            if (count == 0 || (count + PerPage(scope) - 1) / PerPage(scope) != _pageCounts[scope]) return 0;
            size += count * EntryBytes(scope) + _pageCounts[scope] * (HeaderBytes + 2);
        }
        if (offset < 0 || offset > buffer.Length - size) return 0;
        int at = offset;
        for (int scope = 0; scope < 2; scope++)
        {
            if ((_scopeMask & (1 << scope)) == 0) continue;
            int index = 0;
            for (int page = 0; page < _pageCounts[scope]; page++)
            {
                int start = at;
                buffer[at++] = NetProtocol.ExtIdCardFlightOrigins; at++;
                buffer[at++] = _sequence; buffer[at++] = _scopeMask; buffer[at++] = (byte)scope;
                buffer[at++] = (byte)_nativeCounts[scope]; buffer[at++] = (byte)page;
                buffer[at++] = (byte)_pageCounts[scope]; int countAt = at++;
                byte count = 0;
                while (index < _nativeCounts[scope] && count < PerPage(scope))
                {
                    int target = index++; var origin = _origins[scope][target];
                    if (origin == null) continue;
                    buffer[at++] = (byte)target;
                    if (scope == 0)
                    {
                        AvatarSerializer.WriteI32(buffer, ref at, origin.SourceActorId);
                        Put16(buffer, ref at, origin.PoolSeat); Put16(buffer, ref at, origin.PoolCount);
                    }
                    WriteGeometry(buffer, ref at, origin); count++;
                }
                buffer[countAt] = count; buffer[start + 1] = (byte)(at - start - 2); recordCount++;
            }
        }
        return at - offset;
    }

    internal static bool ReadPage(byte[] buffer, int offset, int length, ref CardFlightOrigins? context)
    {
        if (buffer == null || offset < 0 || length < HeaderBytes || offset > buffer.Length - length) return false;
        int at = offset;
        byte sequence = buffer[at++], mask = buffer[at++], scope = buffer[at++], native = buffer[at++],
            page = buffer[at++], pages = buffer[at++], count = buffer[at++];
        if (mask == 0 || (mask & ~3) != 0 || scope > 1 || (mask & (1 << scope)) == 0
            || native == 0 || native > (scope == 0 ? CardFlightHistory.CountMax : CardBurnCompletionHistory.CountMax)
            || pages == 0 || pages > (scope == 0 ? MaxRecentPages : MaxDurablePages) || page >= pages
            || count == 0 || count > PerPage(scope) || page + 1 < pages && count != PerPage(scope)
            || length < HeaderBytes + count * EntryBytes(scope)) return false;
        var read = context ?? new CardFlightOrigins { _sequence = sequence, _scopeMask = mask };
        if (read._sequence != sequence || read._scopeMask != mask
            || read._nativeCounts[scope] >= 0 && (read._nativeCounts[scope] != native || read._pageCounts[scope] != pages)
            || (read._pagesSeen[scope] & (1u << page)) != 0) return false;
        var additions = new CardDismissFlightOrigin?[native];
        for (int i = 0; i < count; i++)
        {
            int index = buffer[at++];
            if (index >= native || additions[index] != null || read._origins[scope]?[index] != null) return false;
            int actor = scope == 0 ? AvatarSerializer.ReadI32(buffer, ref at) : 0;
            ushort seat = scope == 0 ? Get16(buffer, ref at) : (ushort)0;
            ushort population = scope == 0 ? Get16(buffer, ref at) : (ushort)0;
            var origin = ReadGeometry(buffer, ref at, actor, seat, population);
            if (scope == 0 ? !origin.Valid : !origin.GeometryValid) return false;
            additions[index] = origin;
        }
        if (read._origins[scope] == null) read._origins[scope] = new CardDismissFlightOrigin?[native];
        for (int i = 0; i < native; i++) if (additions[i] != null) read._origins[scope][i] = additions[i];
        read._nativeCounts[scope] = native; read._pageCounts[scope] = pages; read._pagesSeen[scope] |= 1u << page;
        context = read; return true;
    }

    private static int PerPage(int scope) => scope == 0 ? RecentPageEntries : DurablePageEntries;
    private static int EntryBytes(int scope) => scope == 0 ? RecentEntryBytes : DurableEntryBytes;
    // Record75's pages have no ordinal. Existing immutable unique keys establish the
    // companion's index independently of TLV page order; record75 itself is unchanged.
    private static CardBurnCompletion[] CanonicalCompletions(CardBurnCompletionHistory history)
    {
        var entries = (CardBurnCompletion[])history.Entries.Clone();
        Array.Sort(entries, (a, b) =>
        {
            int order = a.ActorId.CompareTo(b.ActorId);
            if (order == 0) order = a.SourceActorId.CompareTo(b.SourceActorId);
            if (order == 0) order = a.PoolSeat.CompareTo(b.PoolSeat);
            if (order == 0) order = a.PoolCount.CompareTo(b.PoolCount);
            return order;
        });
        return entries;
    }
    private static bool SameGeometry(CardDismissFlightOrigin a, CardDismissFlightOrigin b)
    {
        var p = a.BerthPose.Position; var r = b.BerthPose.Position;
        var q = a.BerthPose.Rotation; var s = b.BerthPose.Rotation;
        return p.x == r.x && p.y == r.y && p.z == r.z && q.x == s.x && q.y == s.y
            && q.z == s.z && q.w == s.w && a.Width == b.Width;
    }
    private static void Put16(byte[] b, ref int at, ushort value) { b[at++] = (byte)value; b[at++] = (byte)(value >> 8); }
    private static ushort Get16(byte[] b, ref int at) => (ushort)(b[at++] | b[at++] << 8);
    private static void WriteGeometry(byte[] b, ref int at, CardDismissFlightOrigin origin)
    {
        var p = origin.BerthPose.Position; var q = origin.BerthPose.Rotation;
        AvatarSerializer.WriteF32(b, ref at, p.x); AvatarSerializer.WriteF32(b, ref at, p.y); AvatarSerializer.WriteF32(b, ref at, p.z);
        AvatarSerializer.WriteF32(b, ref at, q.x); AvatarSerializer.WriteF32(b, ref at, q.y);
        AvatarSerializer.WriteF32(b, ref at, q.z); AvatarSerializer.WriteF32(b, ref at, q.w);
        AvatarSerializer.WriteF32(b, ref at, origin.Width);
    }
    private static CardDismissFlightOrigin ReadGeometry(byte[] b, ref int at, int actor, ushort seat, ushort count)
    {
        var p = new UnityEngine.Vector3(AvatarSerializer.ReadF32(b, ref at), AvatarSerializer.ReadF32(b, ref at), AvatarSerializer.ReadF32(b, ref at));
        var q = new UnityEngine.Quaternion(AvatarSerializer.ReadF32(b, ref at), AvatarSerializer.ReadF32(b, ref at),
            AvatarSerializer.ReadF32(b, ref at), AvatarSerializer.ReadF32(b, ref at));
        return new CardDismissFlightOrigin(actor, seat, count, new RigPose { Position = p, Rotation = q }, AvatarSerializer.ReadF32(b, ref at));
    }
}
