using System;
using System.Linq;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class CardDismissOriginVectors
{
    private static CardDismissFlightOrigin Origin(int actor = 0x01020304, ushort seat = 1, ushort count = 3)
        => new(actor, seat, count, new RigPose { Position = new Vector3(1, -2, 3),
            Rotation = new Quaternion(0, 0, 0, 1) }, 0.5f);

    internal static void Run(Harness t)
    {
        t.Case("dismiss origin: immutable launch sample leaves frozen62/75 bytes unchanged");
        var origin = Origin();
        t.True(origin.Valid, "original pool and one relative launch sample are valid");
        var source = new CardFlightSource(11, 1, 2);
        var tagged = new CardFlightHistory(1, new[] { new CardFlightEvent(1, 0x47, 0, source, origin) });
        var plain = new CardFlightHistory(1, new[] { new CardFlightEvent(1, 0x47, 0, source) });
        var bytes = new byte[CardFlightHistory.MaxSize]; var expected = new byte[bytes.Length];
        int size = tagged.Write(bytes, 0), plainSize = plain.Write(expected, 0);
        t.Wire(expected.Take(plainSize).ToArray(), bytes, size, "transient dismissal never widens frozen ten-byte62 event");
        t.True(CardFlightHistory.TryRead(bytes, 0, size, out var decoded)
            && decoded!.Events[0].DismissOrigin == null, "old62 alone cannot invent dismissal metadata");
        var completion = new CardBurnCompletion(1, 0x47, 0, 7.25f, 11, 0x01020304, 1, 3, 1, 2, origin);
        var untagged = new CardBurnCompletion(1, 0x47, 0, 7.25f, 11, 0x01020304, 1, 3, 1, 2);
        bytes = new byte[CardBurnCompletionHistory.MaxSize]; expected = new byte[bytes.Length];
        size = new CardBurnCompletionHistory(1, new[] { completion }).Write(bytes, 0);
        plainSize = new CardBurnCompletionHistory(1, new[] { untagged }).Write(expected, 0);
        t.Wire(expected.Take(plainSize).ToArray(), bytes, size, "transient dismissal never widens frozen twenty-one-byte75 entry");
        t.True(ReferenceEquals(completion.WithSequence(255).WithRecentFlight(true).WithRecentFlight(false).DismissOrigin, origin),
            "all durable receipt rewrites preserve the exact immutable launch object");
        foreach (int actor in new[] { int.MinValue, -1, 1, int.MaxValue })
            t.True(Origin(actor).Valid, "source actor signed hash remains valid " + actor);
        t.True(!Origin(0).Valid && !Origin(seat: 3).Valid && !Origin(count: 0).Valid,
            "missing pool actor and out-of-population seats are rejected");
        t.True(Origin(seat: 32769).Valid && Origin(seat: 65535, count: 32768).Valid,
            "supply-bank high bit remains distinct from the bounded native pool index");
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var invalidPosition = new CardDismissFlightOrigin(1, 0, 1,
                new RigPose { Position = new Vector3(value, 0, 0), Rotation = Quaternion.identity }, 1);
            var invalidRotation = new CardDismissFlightOrigin(1, 0, 1,
                new RigPose { Rotation = new Quaternion(value, 0, 0, 1) }, 1);
            var invalidWidth = new CardDismissFlightOrigin(1, 0, 1,
                new RigPose { Rotation = Quaternion.identity }, value);
            t.True(!invalidPosition.Valid && !invalidRotation.Valid && !invalidWidth.Valid,
                "each nonfinite launch component is rejected " + value);
        }
        t.True(!new CardDismissFlightOrigin(1, 0, 1, default, 1).Valid,
            "a zero quaternion cannot become a valid origin");
        Outbox(t, origin);
        Codec(t);
    }

    private static void Outbox(Harness t, CardDismissFlightOrigin origin)
    {
        t.Case("dismiss origin: queue coalescing, wrap and durable release retain exact origin");
        NetCardFx.Reset();
        var source = new CardFlightSource(11, 1, 2);
        for (int i = 0; i < 260; i++)
        {
            var selected = i % 2 == 0 ? origin : null;
            NetCardFx.Report(CardFxAnchor.Active, CardFxAnchor.Discard, source: source, dismissOrigin: selected);
            t.True(NetCardFx.TryDequeue(out _, out _, out _, out _), "dense dispatch succeeds across sequence wrap");
            var latest = NetCardFx.History.Events.Last();
            t.True(ReferenceEquals(latest.DismissOrigin, selected), "current sequence retains only its own origin");
            if (i >= 250)
            {
                byte[] packet = Serialize(new PresenceState { FlightHistory = NetCardFx.History,
                    BurnCompletions = NetCardFx.BurnCompletions, FlightOrigins = NetCardFx.FlightOrigins });
                t.True(PresenceSerializer.TryRead(packet, packet.Length, out var mirrored)
                    && mirrored.FlightHistory!.Events.Select(e => e.FromDismissBerth)
                        .SequenceEqual(NetCardFx.History.Events.Select(e => e.FromDismissBerth)),
                    "coalesced actual outbox122 retains each alternating origin through sequence wrap");
            }
        }
        var capture = new CardAppearanceState { ActorId = 11, SourceActorId = origin.SourceActorId,
            PoolSeat = origin.PoolSeat, PoolCount = origin.PoolCount };
        NetCardFx.NoteBurnCompletionCapture(capture, 99);
        NetCardFx.Report(CardFxAnchor.Active, CardFxAnchor.Burnt, source: source, completionTime: 99,
            dismissOrigin: origin);
        NetCardFx.TryDequeue(out _, out _, out _, out _);
        t.True(NetCardFx.BurnCompletions.Entries.Length == 1
            && ReferenceEquals(NetCardFx.BurnCompletions.Entries[0].DismissOrigin, origin),
            "durable release retains launch geometry captured before native source removal");
        double expiredAt = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency + 3;
        t.True(NetCardFx.HistoryAt(expiredAt).Events.Length == 0
            && NetCardFx.BurnCompletions.Entries[0].FromDismissBerth,
            "two-second recent-history expiry does not erase durable dismissal origin");
        byte[] terminalPacket = Serialize(new PresenceState { FlightHistory = NetCardFx.HistoryAt(expiredAt),
            BurnCompletions = NetCardFx.BurnCompletions, FlightOrigins = NetCardFx.FlightOrigins });
        t.True(PresenceSerializer.TryRead(terminalPacket, terminalPacket.Length, out var terminal)
            && terminal.FlightHistory!.Events.Length == 0 && terminal.BurnCompletions!.Entries[0].FromDismissBerth
            && terminal.BurnCompletions.Entries[0].DismissOrigin!.BerthPose.Position.y == -2,
            "actual durable-only122 codec retains exact origin after all recent events expire");
        NetCardFx.Reset();
        t.True(NetCardFx.BurnCompletions.Entries.Length == 0 && NetCardFx.FlightOrigins == null,
            "teardown clears retained origins with their native receipts");
        NetCardFx.NoteBurnCompletionCapture(capture, 99);
        NetCardFx.Report(CardFxAnchor.Active, CardFxAnchor.Burnt, source: source, completionTime: 99,
            dismissOrigin: Origin(actor: 99));
        t.True(!NetCardFx.Pending && NetCardFx.BurnCompletions.Entries.Length == 0,
            "a mismatched immutable original cannot mutate or publish another card's terminal receipt");
        foreach (var endpoints in new[] { (CardFxAnchor.HandFan, CardFxAnchor.Discard),
            (CardFxAnchor.Active, CardFxAnchor.HandFan), (CardFxAnchor.Active, CardFxAnchor.Active) })
        {
            NetCardFx.Report(endpoints.Item1, endpoints.Item2, source: source, dismissOrigin: origin);
            NetCardFx.TryDequeue(out _, out _, out _, out _);
            t.True(!NetCardFx.History.Events.Last().FromDismissBerth,
                "another semantic event cannot borrow dismiss origin " + endpoints);
        }
        NetCardFx.Report(CardFxAnchor.Active, CardFxAnchor.Discard, source: new CardFlightSource(11, 0, 0), dismissOrigin: origin);
        NetCardFx.TryDequeue(out _, out _, out _, out _);
        t.True(!NetCardFx.History.Events.Last().FromDismissBerth, "actor-only origin cannot identify an active card");
        NetCardFx.Reset();
    }

    private static PresenceState Recent()
    {
        var state = new PresenceState { FlightHistory = new CardFlightHistory(1, new[] {
            new CardFlightEvent(1, 0x37, 0, new CardFlightSource(11, 1, 2), Origin()) }),
            BurnCompletions = new CardBurnCompletionHistory(1, Array.Empty<CardBurnCompletion>()) };
        state.FlightOrigins = CardFlightOrigins.Capture(state.FlightHistory, state.BurnCompletions);
        return state;
    }

    private static PresenceState Maximum()
    {
        var flights = new CardFlightEvent[8]; var completions = new CardBurnCompletion[128];
        for (int i = 0; i < 128; i++)
        {
            var origin = new CardDismissFlightOrigin(100, (ushort)i, 128,
                new RigPose { Position = new Vector3(i, -2, 3), Rotation = Quaternion.identity }, 0.5f);
            completions[i] = new CardBurnCompletion((byte)i, 0x47, (byte)(i < 8 ? CardBurnCompletion.RecentFlightBit : 0),
                i, 11, 100, (ushort)i, 128, 0, 1, origin);
            if (i < 8) flights[i] = new CardFlightEvent((byte)i, 0x47, 0, new CardFlightSource(11, 0, 1), origin);
        }
        var state = new PresenceState { FlightHistory = new CardFlightHistory(7, flights),
            BurnCompletions = new CardBurnCompletionHistory(7, completions) };
        state.FlightOrigins = CardFlightOrigins.Capture(state.FlightHistory, state.BurnCompletions);
        return state;
    }

    private static byte[][] Records(byte[] packet)
    {
        var result = new System.Collections.Generic.List<byte[]>();
        for (int at = 11; at < packet.Length;)
        {
            int size = 2 + packet[at + 1]; result.Add(packet.Skip(at).Take(size).ToArray()); at += size;
        }
        return result.ToArray();
    }

    private static byte[] Packet(params byte[][] records)
    {
        byte[] bytes = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 00");
        Array.Resize(ref bytes, bytes.Length + records.Sum(record => record.Length)); bytes[10] = (byte)records.Length;
        int at = 11; foreach (var record in records) { Array.Copy(record, 0, bytes, at, record.Length); at += record.Length; }
        return bytes;
    }

    private static byte[] Serialize(PresenceState state)
    {
        var bytes = new byte[PresenceSerializer.MaxSize]; int size = PresenceSerializer.Write(state, bytes);
        return bytes.Take(size).ToArray();
    }

    private static void Codec(Harness t)
    {
        t.Case("dismiss122: independent exact launch bytes and frozen native records");
        byte[] golden = Hex.Bytes("7A 30 01 01 00 01 00 01 01 00 04 03 02 01 01 00 03 00 "
            + "00 00 80 3F 00 00 00 C0 00 00 40 40 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F 00 00 00 3F");
        var state = Recent(); var output = new byte[CardFlightOrigins.MaxSize];
        int size = state.FlightOrigins!.Write(output, 0, out byte count);
        t.Equal((byte)1, count, "one recent context has one independent length-delimited page");
        t.Wire(golden, output, size, "122 prefix and exact source-pool/IEEE launch geometry are independent literals");
        CardFlightOrigins? read = null;
        t.True(CardFlightOrigins.ReadPage(golden, 2, golden.Length - 2, ref read) && read!.Complete,
            "independent literal can assemble without invoking the writer");
        var untagged = state; untagged.FlightOrigins = null;
        byte[] packet = Packet(Records(Serialize(untagged)).Concat(new[] { golden }).ToArray());
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out var decoded)
            && decoded.FlightHistory!.Events[0].FromDismissBerth
            && decoded.FlightHistory.Events[0].DismissOrigin!.SourceActorId == 0x01020304
            && decoded.FlightHistory.Events[0].DismissOrigin!.BerthPose.Position.y == -2
            && decoded.FlightHistory.Events[0].DismissOrigin!.Width == .5f,
            "missing118 cannot erase the exact dismissed original or actual mid-glide launch");
        var legacy = Records(Serialize(untagged));
        foreach (var record in legacy)
            t.True(Records(Serialize(state)).Any(candidate => candidate.SequenceEqual(record)),
                "additive122 retains every existing61/62/75 record byte-exactly");
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), Serialize(default), 8,
            "ordinary idle players acquire no extension or geometry overhead");

        t.Case("dismiss122: every known malformed cohort is quarantined atomically");
        for (int cut = 0; cut < golden.Length; cut++)
        {
            read = null;
            t.True(!CardFlightOrigins.ReadPage(golden, 2, cut < 2 ? 0 : cut - 2, ref read),
                "truncated origin never assembles " + cut);
        }
        foreach (var pair in new[] { (2, 2), (3, 0), (3, 4), (4, 2), (5, 0), (5, 9),
            (6, 1), (7, 0), (7, 3), (8, 0), (8, 7), (9, 1), (14, 3), (16, 0) })
        {
            var bad = (byte[])golden.Clone(); bad[pair.Item1] = (byte)pair.Item2;
            var malformed = Packet(legacy.Concat(new[] { bad }).ToArray());
            t.True(!PresenceSerializer.TryRead(malformed, malformed.Length, out decoded) && decoded.FlightHistory == null,
                "bad origin cannot escape as runnable unmarked legacy flight at byte " + pair.Item1);
        }
        var duplicate = Packet(legacy.Concat(new[] { golden, golden }).ToArray());
        t.True(!PresenceSerializer.TryRead(duplicate, duplicate.Length, out decoded) && decoded.FlightHistory == null,
            "a duplicate known page poisons rather than restarts its cohort");
        var future = golden.Concat(Hex.Bytes("AA BB CC")).ToArray(); future[1] += 3;
        packet = Packet(legacy.Concat(new[] { future }).ToArray());
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out decoded) && decoded.FlightHistory!.Events[0].FromDismissBerth,
            "a complete recognized prefix accepts a length-delimited future suffix");
        var unknown = (byte[])golden.Clone(); unknown[0] = 250;
        packet = Packet(legacy.Concat(new[] { unknown }).ToArray());
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out decoded) && !decoded.FlightHistory!.Events[0].FromDismissBerth,
            "absent or unknown additive records retain legacy behavior");
        for (int capacity = 0; capacity < golden.Length; capacity++)
        {
            output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
            t.True(state.FlightOrigins!.Write(output, 0, out count) == 0 && count == 0 && output.All(b => b == 0xCC),
                "small writer capacity exposes no page prefix " + capacity);
        }

        t.Case("dismiss122: all recent and durable origins survive independent page order");
        state = Maximum(); output = new byte[CardFlightOrigins.MaxSize];
        size = state.FlightOrigins!.Write(output, 0, out count);
        t.Equal(4741, size, "eight recent plus128 durable exact samples include all21 page headers");
        t.Equal((byte)21, count, "six recent or seven durable entries fit each byte-length page");
        t.Equal(12515, 7774 + CardFlightOrigins.MaxSize, "encoded worst case includes all additive122 page overhead");
        t.Equal(12955, PresenceSerializer.MaxSize, "allocation retains the unchanged257-byte spare-record rule");
        t.Equal(12800, ExtrasFragments.MaxSnapshotBytes, "bounded assembly covers the exact maximum with285 spare");
        packet = Serialize(state);
        var records = Records(packet);
        t.True(PresenceSerializer.TryRead(packet, packet.Length, out decoded)
            && decoded.FlightHistory!.Events.All(e => e.FromDismissBerth)
            && decoded.BurnCompletions!.Entries.All(e => e.FromDismissBerth),
            "complete maximum attaches every marked original and terminal release");
        var reversed = Packet(records.Reverse().ToArray());
        t.True(PresenceSerializer.TryRead(reversed, reversed.Length, out var reordered)
            && reordered.BurnCompletions!.Entries.All(e => e.DismissOrigin!.BerthPose.Position.x == e.PoolSeat),
            "canonical immutable durable keys survive independent75 and122 page reversal");
        foreach (var omission in records.Select((record, index) => (record, index)).Where(pair => pair.record[0] == 122))
        {
            var incomplete = Packet(records.Where((_, index) => index != omission.index).ToArray());
            t.True(!PresenceSerializer.TryRead(incomplete, incomplete.Length, out decoded) && decoded.FlightHistory == null,
                "missing page or complete scope never exposes approximate active-grid flight " + omission.index);
        }
        var contradiction = records.Select(record => (byte[])record.Clone()).ToArray();
        var recentPage = contradiction.First(record => record[0] == 122 && record[4] == 0);
        recentPage[18] = 0; recentPage[19] = 0; recentPage[20] = 0x80; recentPage[21] = 0x3f;
        var conflicting = Packet(contradiction);
        t.True(!PresenceSerializer.TryRead(conflicting, conflicting.Length, out decoded) && decoded.FlightHistory == null,
            "same recent/durable release cannot carry contradictory geometry");
        foreach (int capacity in new[] { 0, 1, 8, 346, 4395, 4740 })
        {
            output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
            t.True(state.FlightOrigins!.Write(output, 0, out count) == 0 && count == 0 && output.All(b => b == 0xCC),
                "maximum context writes all21 pages or none");
        }
    }
}
