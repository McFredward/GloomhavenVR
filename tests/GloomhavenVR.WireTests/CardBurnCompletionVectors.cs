using System;
using GloomhavenVR.Net;
namespace GloomhavenVR.WireTests;
internal static class CardBurnCompletionVectors
{
    private static CardBurnCompletion Entry(byte sequence = 1, ushort seat = 0, int actor = 3, float time = 7.25f)
        => new(sequence, 0x41, CardBurnCompletion.RecentFlightBit, time, actor, actor, seat, 32, 0, 0);
    internal static void Run(Harness t)
    {
        t.Case("513: durable native completion preserves exact original provenance");
        var history = new CardBurnCompletionHistory(1, new[] { Entry() });
        var bytes = new byte[PresenceSerializer.MaxSize];
        int length = PresenceSerializer.Write(new PresenceState { BurnCompletions = history }, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 4B 17 01 01 01 41 02 00 00 E8 40 03 00 00 00 03 00 00 00 00 00 20 00 00 00"), bytes, length,
            "golden75 carries LE native clock and immutable original alongside legacy source");
        t.True(PresenceSerializer.TryRead(bytes, length, out var state) && state.BurnCompletions != null
            && state.BurnCompletions.Entries[0].Time == 7.25f && state.BurnCompletions.Entries[0].PoolCount == 32,
            "actual extras decoder carries native completion identity");
        t.True(history.Covers(1, 0x41, 0, new CardFlightSource(3, 0, 0)), "exact legacy event covered by durable receipt");
        t.True(!history.Covers(1, 0x42, 0, new CardFlightSource(3, 0, 0))
            && !history.Covers(1, 0x41, 0, new CardFlightSource(4, 0, 0))
            && !history.Covers(2, 0x41, 0, new CardFlightSource(3, 0, 0)), "same sequence cannot borrow another origin or actor's completion");
        var payload = new byte[CardBurnCompletionHistory.MaxSize];
        int size = history.Write(payload, 0);
        for (int cut = 0; cut < size - 2; cut++)
            t.True(!CardBurnCompletionHistory.TryRead(payload, 2, cut, out _), "truncated durable record refused " + cut);
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            t.Equal(0, new CardBurnCompletionHistory(1, new[] { Entry(time: invalid) }).Write(payload, 0), "invalid source clock is not serialized");
            history.Write(payload, 0); int at = 7; AvatarSerializer.WriteF32(payload, ref at, invalid);
            t.True(!CardBurnCompletionHistory.TryRead(payload, 2, size - 2, out _), "invalid received clock refused");
        }
        t.Equal(0, new CardBurnCompletionHistory(1, new[] { Entry(), Entry(sequence: 2) }).Write(payload, 0), "duplicate immutable original refused");
        t.True(new CardBurnCompletionHistory(1, new[] { Entry(), Entry(seat: 1) }).Write(payload, 0) > 0,
            "wrapped equal sequence never collapses distinct originals");
        var maximum = new CardBurnCompletion[128];
        for (int i = 0; i < maximum.Length; i++) maximum[i] = Entry((byte)i, (ushort)(i % 32), 3 + i / 32);
        var full = new CardBurnCompletionHistory(127, maximum);
        t.Equal(2732, full.Write(payload, 0), "128 originals use eleven bounded75 records");
        length = PresenceSerializer.Write(new PresenceState { BurnCompletions = full }, bytes);
        t.True(PresenceSerializer.TryRead(bytes, length, out state) && state.BurnCompletions?.Entries.Length == 128,
            "actual decoder merges every page without dropping original provenance");
        t.True(CardBurnCompletionHistory.Merge(history, history) == null, "duplicate pages cannot create ambiguous completion");
        t.True(CardBurnCompletionHistory.Merge(history, new CardBurnCompletionHistory(2, new[] { Entry(seat: 1) })) == null,
            "mixed generations cannot merge");
        t.True(6865 <= ExtrasFragments.MaxSnapshotBytes && PresenceSerializer.MaxSize - 6865 >= 257,
            "full presence remains bounded with largest-record margin");
        NetCardFx.Reset();
        NetCardFx.Report(CardFxAnchor.Slot0, CardFxAnchor.Burnt, 0, new CardFlightSource(3, 0, 0), 7.25f);
        t.True(NetCardFx.BurnCompletions.Entries.Length == 0, "missing native capture never fabricates a causal receipt");
        var capture = new CardAppearanceState { ActorId = 3, SourceActorId = 3, PoolCount = 32 };
        NetCardFx.NoteBurnCompletionCapture(capture, 7.25f);
        NetCardFx.Report(CardFxAnchor.Slot0, CardFxAnchor.Burnt, 0, new CardFlightSource(3, 0, 0), 7.25f);
        capture = new CardAppearanceState { ActorId = 3, SourceActorId = 3, PoolSeat = 1, PoolCount = 32 };
        NetCardFx.NoteBurnCompletionCapture(capture, 7.25f);
        NetCardFx.Report(CardFxAnchor.Slot0, CardFxAnchor.Burnt, 0, new CardFlightSource(3, 0, 0), 7.25f);
        while (NetCardFx.TryDequeue(out _, out _, out _, out _)) { }
        foreach (var entry in NetCardFx.BurnCompletions.Entries)
            t.True(NetCardFx.BurnCompletions.Covers(entry.Sequence, entry.Endpoints, entry.FlightFlags, entry.Source),
                "Recent exact native receipt correlates its live legacy event");
        NetCardFx.HistoryAt(double.MaxValue);
        foreach (var entry in NetCardFx.BurnCompletions.Entries)
            t.True(!NetCardFx.BurnCompletions.Covers(entry.Sequence, entry.Endpoints, entry.FlightFlags, entry.Source),
                "Expired legacy correlation cannot suppress a future wrapped event");
        for (int i = 0; i < 255; i++)
        {
            NetCardFx.Report(CardFxAnchor.Slot0, CardFxAnchor.Discard);
            NetCardFx.TryDequeue(out _, out _, out _, out _);
        }
        NetCardFx.Report(CardFxAnchor.Slot0, CardFxAnchor.Burnt, 0, new CardFlightSource(3, 0, 0), 99f);
        NetCardFx.TryDequeue(out byte wrappedEndpoints, out byte wrappedSequence, out byte wrappedFlags, out var wrappedSource);
        t.True(!NetCardFx.BurnCompletions.Covers(wrappedSequence, wrappedEndpoints, wrappedFlags, wrappedSource),
            "Wrapped same-slot burn without native capture retains its legacy release path");
        t.True(NetCardFx.BurnCompletions.Entries.Length == 2, "paired same-frame originals survive expiry of the two-second flight history");
        NetCardFx.ForgetBurnCompletion(capture);
        t.True(NetCardFx.BurnCompletions.Entries.Length == 1 && NetCardFx.BurnCompletions.Entries[0].PoolSeat == 0,
            "recovery retires only its exact original receipt");
        object first = new(), second = new();
        var source = new CardFlightSource(3, 0, 0);
        CardFlightVisibility.ObserveOwnerRelease(3, 0x41, 0, source, 7.25f, first);
        CardFlightVisibility.ObserveOwnerRelease(3, 0x41, 0, source, 8f, second);
        t.True(CardFlightVisibility.TryPeekOwnerRelease(3, CardFxAnchor.Slot0, source, out _, out float clock, second)
            && clock == 8f, "second same-slot original cannot consume the first original's receipt");
        t.True(!CardFlightVisibility.TryConsumeOwnerRelease(3, CardFxAnchor.Slot0, source, out _, new object()), "unrelated original cannot borrow the slot's receipt");
        CardFlightVisibility.CancelOwnerReleaseBefore(first, 7f);
        t.True(CardFlightVisibility.TryPeekOwnerRelease(3, CardFxAnchor.Slot0, source, out _, out _, first), "An old recovery sample cannot cancel a newer burn");
        CardFlightVisibility.CancelOwnerReleaseBefore(first, 7.3f);
        t.True(!CardFlightVisibility.TryPeekOwnerRelease(3, CardFxAnchor.Slot0, source, out _, out _, first), "recovery cancels stale original receipt before reuse");
        t.True(CardFlightVisibility.TryConsumeOwnerRelease(3, CardFxAnchor.Slot0, source, out _, second), "completed exact original consumes once");
        var progressIdentity = new CardAppearanceState {ActorId=5,SourceActorId=5,PoolSeat=2,PoolCount=32};
        NetCardFx.NoteBurnProgress(progressIdentity, 30f, running:true);
        var progressEntry = Array.Find(NetCardFx.BurnCompletions.Entries, e => e.ActorId == 5);
        t.True(progressEntry.InProgress && progressEntry.Endpoints == 0 && progressEntry.Valid(),
            "Native running admission has immutable provenance and no invented flight origin");
        length = PresenceSerializer.Write(new PresenceState {BurnCompletions=NetCardFx.BurnCompletions}, bytes);
        t.True(PresenceSerializer.TryRead(bytes,length,out state) && state.BurnCompletions != null
            && Array.Exists(state.BurnCompletions.Entries,e=>e.ActorId==5 && e.InProgress),
            "Actual75 decoder preserves progress independently of terminal release");
        t.True(!NetCardFx.BurnCompletions.Covers(0,0x41,0,new CardFlightSource(5,0,0)),
            "Progress cannot swallow a legacy flight as completed");
        t.True(NetCardFx.CompleteBurnWithoutFlight(progressIdentity,31f),"Actual owner completion can retain a durable no-flight terminal");
        var noFlight=Array.Find(NetCardFx.BurnCompletions.Entries,e=>e.ActorId==5);
        t.True(noFlight.NoFlightCompleted && noFlight.Endpoints==0 && noFlight.Valid(),"No-flight terminal has no invented origin or legacy correlation");
        length=PresenceSerializer.Write(new PresenceState {BurnCompletions=NetCardFx.BurnCompletions},bytes);
        t.True(PresenceSerializer.TryRead(bytes,length,out state) && state.BurnCompletions != null
            && Array.Exists(state.BurnCompletions.Entries,e=>e.ActorId==5&&e.NoFlightCompleted),"Actual codec preserves coalesced offscreen completion");
        t.True(!NetCardFx.BurnCompletions.Covers(0,0,0,new CardFlightSource(5,0,0)),"No-flight receipt cannot correlate or swallow a legacy flight");
        NetCardFx.ForgetBurnCompletion(progressIdentity);
        t.True(!Array.Exists(NetCardFx.BurnCompletions.Entries,e=>e.ActorId==5),
            "Unadopted native completion clears admission without inventing a terminal flight");
        t.True(BurnReleasePolicy.RetireWithoutFlight(true,false,false,false),"Observed offscreen completion retires without an invented flight");
        t.True(!BurnReleasePolicy.RetireWithoutFlight(true,true,false,false),"Running owner progress still owns the original presentation");
        t.True(!BurnReleasePolicy.RetireWithoutFlight(true,false,true,false),"Actual owner release keeps the native flight path");
        t.True(!BurnReleasePolicy.RetireWithoutFlight(true,false,false,true),"A running native proxy cannot be retired early");
        t.True(!BurnReleasePolicy.RetireWithoutFlight(false,false,false,false),"Absence of progress is not evidence of completion");
        NetCardFx.Reset(); CardFlightVisibility.Reset();
        t.True(NetCardFx.BurnCompletions.Entries.Length == 0, "teardown clears completed provenance");
    }
}
