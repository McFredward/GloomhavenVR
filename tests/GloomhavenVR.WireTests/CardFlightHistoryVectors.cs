using System;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardFlightHistoryVectors
{
    internal static void Run(Harness t)
    {
        t.Case("489: coalesced flight history recovers paired flights exactly once");
        var history = new CardFlightHistory(0, new[] {
            new CardFlightEvent(255, 0x24, 0, new CardFlightSource(0x01020304, 1, 2)),
            new CardFlightEvent(0, 0x34, 1, null) });
        var buffer = new byte[CardFlightHistory.MaxSize];
        int size = history.Write(buffer, 0);
        t.Wire(Hex.Bytes("00 02 FF 24 00 01 04 03 02 01 01 02 00 34 01 00 00 00 00 00 00 00"), buffer, size,
            "golden correlated endpoints, privacy and former actor seat");
        t.True(CardFlightHistory.TryRead(buffer, 0, size, out var read), "history decodes");
        t.True(read!.Since(254).Count() == 2 && read.Since(255).Count() == 1 && !read.Since(0).Any(),
            "packet loss, wrap and duplicates preserve exactly once playback");
        for (int cut = 0; cut < size; cut++)
            t.True(!CardFlightHistory.TryRead(buffer, 0, cut, out _), "truncated flight history " + cut);
        buffer[12] = 4;
        t.True(!CardFlightHistory.TryRead(buffer, 0, size, out _), "nonconsecutive history cannot replay arbitrary old flights");
        var presence = new byte[PresenceSerializer.MaxSize];
        int n = PresenceSerializer.Write(new PresenceState { FlightHistory = history }, presence);
        t.True(PresenceSerializer.TryRead(presence, n, out var decoded) && decoded.FlightHistory != null && decoded.FlightHistory.Events.Length == 2,
            "actual extras carries both flights although the legacy prefix holds only one");
        NetCardFx.Reset();
        byte baseline = NetCardFx.History.Sequence;
        t.True(NetCardFx.History.Events.Length == 0, "initial heartbeat establishes sequence without fabricated animation");
        NetCardFx.Report(CardFxAnchor.Active, CardFxAnchor.Discard, source: new CardFlightSource(11, 0, 2));
        NetCardFx.Report(CardFxAnchor.Active, CardFxAnchor.Burnt, 1, new CardFlightSource(11, 1, 2));
        NetCardFx.TryDequeue(out _, out _, out _, out _); NetCardFx.TryDequeue(out _, out _, out _, out _);
        t.True(NetCardFx.History.Since(baseline).Count() == 2
            && NetCardFx.History.Events[1].Source!.Value.Seat == 1, "outbox cannot detach provenance when coalescing");
        byte latest = NetCardFx.History.Sequence;
        double afterExpiry = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency + 3.0;
        CardFlightHistory expired = NetCardFx.HistoryAt(afterExpiry);
        t.True(expired.Events.Length == 0 && expired.Sequence == latest,
            "old unseen flights expire without resetting the replay baseline");
        NetCardFx.Reset();
    }
}
