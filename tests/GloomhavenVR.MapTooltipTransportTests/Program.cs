using System;
using System.Linq;
using GloomhavenVR.WorldUI.MapRoom;

namespace GloomhavenVR.Net;

internal sealed partial class NetAvatarDriver
{
    private static int _checks;
    private static void Check(bool value, string reason)
    {
        _checks++;
        if (!value) throw new Exception(reason);
    }
    private static byte[] Picture(byte cap = 1) => new byte[] { 1, cap, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static NetAvatarDriver Fresh()
    {
        MapButtonTooltipPresentation.ResetFixture();
        return new NetAvatarDriver();
    }
    private bool Queue(int sender, float time, byte[]? payload)
    {
        byte[] buffer = new byte[MapButtonTooltipCodec.MaxSize];
        int length = MapButtonTooltipCodec.Write(new MapButtonTooltipSnapshot(time, payload), buffer);
        return QueueMapButtonTooltip(sender, buffer, length);
    }
    private static void Main()
    {
        SendCadence(); StationaryHold(); ReceiveOrdering(); BoundedBursts(); Lifecycle(); FaultIsolation();
        Console.WriteLine($"Map tooltip transport: {_checks} production-driver assertions passed.");
    }

    private static void SendCadence()
    {
        var d = Fresh();
        d.TickMapButtonTooltipSend(10);
        Check(d._transport.Sent.Count == 1 && d._transport.Sent[0].Payload == null,
            "first sample explicitly announces hidden state");
        for (int i = 1; i < 200; i++) d.TickMapButtonTooltipSend(10 + i * .01f);
        Check(d._transport.Sent.Count == 1, "unchanged hidden state must not send before refresh");
        d.TickMapButtonTooltipSend(12);
        Check(d._transport.Sent.Count == 2, "hidden heartbeat must repair a lost clear");
        byte[] picture = Picture();
        MapButtonTooltipPresentation.Current = picture;
        d.TickMapButtonTooltipSend(12.01f);
        Check(d._transport.Sent.Count == 3 && d._transport.Sent[2].Payload!.SequenceEqual(picture),
            "appearance sends immediately with native output intact");
        for (int i = 1; i < 49; i++) d.TickMapButtonTooltipSend(12.01f + i * .01f);
        Check(d._transport.Sent.Count == 3, "unchanged visible state must not send before refresh");
        d.TickMapButtonTooltipSend(12.52f);
        Check(d._transport.Sent.Count == 4, "visible heartbeat serves late peers");
        MapButtonTooltipPresentation.Current = Picture(2);
        d.TickMapButtonTooltipSend(12.53f);
        Check(d._transport.Sent.Count == 5, "new immutable picture must bypass heartbeat delay");
        MapButtonTooltipPresentation.Current = null;
        d.TickMapButtonTooltipSend(12.54f);
        Check(d._transport.Sent.Count == 6 && d._transport.Sent[5].Payload == null,
            "hover exit must send an immediate explicit disappearance");
        MapButtonTooltipPresentation.Current = Picture();
        d._transport.Throw = true;
        try { d.TickMapButtonTooltipSend(13); }
        catch (InvalidOperationException) { }
        d._transport.Throw = false;
        d.TickMapButtonTooltipSend(13.01f);
        Check(d._transport.Sent.Count >= 7 && d._transport.Sent.Last().SampleTime == 13.01f
            && d._transport.Sent.Last().Payload!.SequenceEqual(MapButtonTooltipPresentation.Current!),
            "failed transport must not consume changed state");
    }

    private static void StationaryHold()
    {
        var d = Fresh();
        byte[] stationary = Picture();
        byte[] moved = Picture();
        moved[2] = 32; // A geometry change belonging to the same stable native cap.
        MapButtonTooltipPresentation.Current = stationary;
        d.TickMapButtonTooltipSend(0);
        d.TickMapButtonTooltipSend(.25f);
        d.TickMapButtonTooltipSend(.49f);
        Check(d._transport.Sent.Count == 1, "stationary hold must stay silent before the heartbeat");
        MapButtonTooltipPresentation.Current = moved;
        d.TickMapButtonTooltipSend(.5f);
        Check(d._transport.Sent.Count == 3,
            "movement after a stationary gap must send the last stable sample before the changed sample");
        var hold = d._transport.Sent[1];
        var movement = d._transport.Sent[2];
        Check(hold.SampleTime == .49f && hold.Payload!.SequenceEqual(stationary),
            "hold endpoint must use the last observed unchanged frame, not the old send time");
        Check(movement.SampleTime == .5f && movement.Payload!.SequenceEqual(moved),
            "new native geometry must retain its actual changed frame time");
        Check(MapButtonTooltipSnapshot.SameIdentity(hold, movement),
            "hold endpoint and movement must remain one native cap interpolation identity");
        foreach (var sample in d._transport.Sent)
            d.Queue(2, sample.SampleTime, sample.Payload);
        d.ApplyMapButtonTooltip(); d.ApplyMapButtonTooltip(); d.ApplyMapButtonTooltip();
        var applied = MapButtonTooltipPresentation.Applied;
        Check(applied.Count == 3 && applied[1].Time == .49f && applied[2].Time == .5f,
            "receive and apply must preserve the hold endpoint immediately before movement");
        Check(applied[1].Payload!.SequenceEqual(stationary) && applied[2].Payload!.SequenceEqual(moved),
            "movement must not be interpolated over the preceding stationary half-second");
        int sent = d._transport.Sent.Count;
        MapButtonTooltipPresentation.Current = Picture(2);
        d.TickMapButtonTooltipSend(.51f);
        Check(d._transport.Sent.Count == sent + 1,
            "adjacent native samples must not insert redundant hold endpoints");
    }

    private static void ReceiveOrdering()
    {
        var d = Fresh();
        byte[] picture = Picture();
        Check(d.Queue(2, 10, picture), "valid picture accepted");
        Check(d.Queue(2, 10, null), "duplicate timestamp is handled without applying");
        Check(d.Queue(2, 9, null), "older timestamp is handled without applying");
        Check(d._pendingMapTooltips[2].Count == 1, "duplicate and out-of-order packets cannot replace pending picture");
        d.ApplyMapButtonTooltip();
        Check(MapButtonTooltipPresentation.Applied.Count == 1
            && MapButtonTooltipPresentation.Applied[0].Payload!.SequenceEqual(picture),
            "newest accepted picture is applied intact");
        d.Queue(2, 8, null); d.Queue(2, 10, null); d.ApplyMapButtonTooltip();
        Check(MapButtonTooltipPresentation.Applied.Count == 1,
            "stale packets remain rejected after pending queue drains");
        d.Queue(2, 11, null); d.ApplyMapButtonTooltip();
        Check(MapButtonTooltipPresentation.Applied.Count == 2
            && MapButtonTooltipPresentation.Applied[1].Payload == null,
            "strictly newer disappearance is accepted");
        byte[] invalid = new byte[20];
        Check(!d.QueueMapButtonTooltip(2, invalid, invalid.Length), "malformed picture rejected");
        Check(d._lastMapTooltipTime[2] == 11, "malformed packet cannot poison source timestamp");
    }

    private static void BoundedBursts()
    {
        var d = Fresh();
        byte[] picture = Picture();
        for (int sender = 1; sender <= 20; sender++) d.Queue(sender, 1, picture);
        Check(d._pendingMapTooltips.Count == 8 && d._lastMapTooltipTime.Count == 8,
            "receive peer memory must remain bounded");
        for (int i = 2; i <= 1000; i++)
        {
            d.Queue(1, i, picture);
            Check(d._pendingMapTooltips[1].Count <= 4, "each sender has a bounded pending burst");
        }
        Check(d._pendingMapTooltips[1][0].SampleTime == 1,
            "burst coalescing retains the first visible sample");
        d.Queue(1, 1001, null);
        byte[] reopened = Picture(2);
        for (int time = 1002; time <= 1100; time++) d.Queue(1, time, reopened);
        Check(d._pendingMapTooltips[1].Any(s => s.Payload == null),
            "clear survives a rapid close and reopen burst");
        Check(d._pendingMapTooltips[1].Last().SampleTime == 1100,
            "latest reopened picture survives burst coalescing");
        for (int i = 0; i < 4; i++) d.ApplyMapButtonTooltip();
        var seen = MapButtonTooltipPresentation.Applied.Where(s => s.Sender == 1).ToArray();
        int clear = Array.FindIndex(seen, s => s.Payload == null);
        Check(clear > 0 && clear < seen.Length - 1 && seen[seen.Length - 1].Time == 1100,
            "rendered sequence preserves visible then clear then reopen");
        d.ForgetMapButtonTooltip(2);
        d.Queue(20, 1, picture);
        Check(d._pendingMapTooltips.ContainsKey(20), "forgotten peer immediately frees receive capacity");
    }

    private static void Lifecycle()
    {
        var d = Fresh();
        d.Queue(7, 1000, Picture()); d.ApplyMapButtonTooltip();
        d.Queue(7, 1001, null);
        d.ForgetMapButtonTooltip(7);
        Check(!d._pendingMapTooltips.ContainsKey(7) && !d._lastMapTooltipTime.ContainsKey(7)
            && MapButtonTooltipPresentation.Removed.Contains(7),
            "forget clears pending data, source time and rendered peer output");
        d.Queue(7, 1, Picture()); d.ApplyMapButtonTooltip();
        Check(MapButtonTooltipPresentation.Applied.Last().Time == 1,
            "reconnected sender may restart its source clock");
        MapButtonTooltipPresentation.Current = Picture(); d.TickMapButtonTooltipSend(100);
        d.ResetMapButtonTooltip();
        Check(d._pendingMapTooltips.Count == 0 && d._lastMapTooltipTime.Count == 0
            && MapButtonTooltipPresentation.Resets == 1,
            "session reset clears every peer and presentation cache");
        d.TickMapButtonTooltipSend(.1f);
        Check(d._transport.Sent.Count == 2 && d._transport.Sent.Last().Payload == null,
            "new session immediately announces hidden state despite old send deadline");
        d.Queue(7, .1f, Picture()); d.ApplyMapButtonTooltip();
        Check(MapButtonTooltipPresentation.Applied.Count == 1,
            "new session accepts source timestamps below old session watermark");
    }

    private static void FaultIsolation()
    {
        var d = Fresh();
        d.Queue(2, 1, Picture()); d.Queue(3, 1, Picture(2));
        MapButtonTooltipPresentation.ThrowSender = 2;
        d.ApplyMapButtonTooltip();
        Check(d._errors.Count == 1 && MapButtonTooltipPresentation.Applied.Any(s => s.Sender == 3),
            "one peer rendering failure must not starve other peers");
        Check(d._pendingMapTooltips[2].Count == 0,
            "failed sample must be consumed rather than retried every frame");
        MapButtonTooltipPresentation.ThrowTick = true;
        bool continued = false;
        try { d.ApplyMapButtonTooltip(); continued = true; }
        catch (InvalidOperationException) { }
        Check(continued && d._errors.Count == 2,
            "tooltip tick failure must not abort subsequent native presentation");
    }
}
