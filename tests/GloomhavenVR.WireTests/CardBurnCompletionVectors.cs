using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardBurnCompletionVectors
{
    internal static void Run(Harness t)
    {
        t.Case("513: burn completion watermark is additive and sequence scoped");
        var history = new CardBurnCompletionHistory(0, new[] { ((byte)255, 1.5f), ((byte)0, 2f) });
        var bytes = new byte[PresenceSerializer.MaxSize];
        int length = PresenceSerializer.Write(new PresenceState { BurnCompletions = history }, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 4B 0C 00 02 FF 00 00 C0 3F 00 00 00 00 40"), bytes, length,
            "golden75 preserves owner-clock floats and wrapping flight sequences");
        t.True(PresenceSerializer.TryRead(bytes, length, out var state) && state.BurnCompletions != null
            && state.BurnCompletions.Find(255) == 1.5f && state.BurnCompletions.Find(0) == 2f,
            "actual extras decoder carries completion without changing record62");
        t.True(history.Find(1) < 0f, "unrelated sequence cannot borrow completion");
        var payload = new byte[CardBurnCompletionHistory.MaxSize];
        int size = history.Write(payload, 0);
        for (int cut = 0; cut < size; cut++)
            t.True(!CardBurnCompletionHistory.TryRead(payload, 0, cut, out _), "truncated watermark refused " + cut);
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var bad = new CardBurnCompletionHistory(0, new[] { ((byte)0, invalid) });
            t.Equal(0, bad.Write(payload, 0), "invalid source clock is not serialized");
            history.Write(payload, 0); int at = 3; AvatarSerializer.WriteF32(payload, ref at, invalid);
            t.True(!CardBurnCompletionHistory.TryRead(payload, 0, size, out _), "invalid received clock refused");
        }
        foreach (var entries in new[] {
            new[] { ((byte)0, 1f), ((byte)0, 2f) },
            new[] { ((byte)0, 1f), ((byte)255, 2f) },
            new[] { ((byte)240, 1f) } })
            t.Equal(0, new CardBurnCompletionHistory(0, entries).Write(payload, 0), "duplicate/reversed/stale receipt refused");
        var maximum = new (byte, float)[8];
        for (int i = 0; i < maximum.Length; i++) maximum[i] = ((byte)(249 + i), i);
        size = new CardBurnCompletionHistory(0, maximum).Write(payload, 0);
        t.Equal(42, size, "eight retained burns fit44 bytes including TLV");
        t.True(CardBurnCompletionHistory.TryRead(payload, 0, size, out _), "maximum wrapping receipt decodes");
        t.True(4177 <= ExtrasFragments.MaxSnapshotBytes && PresenceSerializer.MaxSize - 4177 >= 257,
            "full combined presence remains bounded with spare record margin");

        NetCardFx.Reset();
        NetCardFx.Report(CardFxAnchor.Slot0, CardFxAnchor.Burnt, 0, new CardFlightSource(3, 0, 0), 7.25f);
        NetCardFx.Report(CardFxAnchor.Slot1, CardFxAnchor.Discard, 0, new CardFlightSource(3, 0, 0), 99f);
        NetCardFx.TryDequeue(out _, out byte burn, out _, out _);
        NetCardFx.TryDequeue(out _, out byte discard, out _, out _);
        t.True(NetCardFx.BurnCompletions.Find(burn) == 7.25f && NetCardFx.BurnCompletions.Find(discard) < 0f,
            "outbox correlates the native completion and never applies it to a discard");
        CardFlightVisibility.ObserveOwnerRelease(3, 0x41, 0, new CardFlightSource(3, 0, 0), 7.25f);
        t.True(CardFlightVisibility.TryPeekOwnerRelease(3, CardFxAnchor.Slot0, new CardFlightSource(3, 0, 0), out _, out float clock)
            && clock == 7.25f, "read-only board can await native pixels without spending its receipt");
        t.True(CardFlightVisibility.TryConsumeOwnerRelease(3, CardFxAnchor.Slot0, new CardFlightSource(3, 0, 0), out _),
            "completed read-only presentation consumes the same receipt once");
        NetCardFx.Reset();
        t.True(NetCardFx.BurnCompletions.Entries.Length == 0, "teardown clears final completion clocks");
    }
}
