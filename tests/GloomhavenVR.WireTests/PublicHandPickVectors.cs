using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class PublicHandPickVectors
{
    internal static void Run(Harness t)
    {
        t.Case("761:124 public hand-pick original pool provenance is bounded and byte-exact");
        var left = new PublicHandPickState.Seat(0x0c0b0a09, 4, 12);
        var right = new PublicHandPickState.Seat(0x100f0e0d, 0x8001, 7);
        var picture = new PublicHandPickState(0x04030201, 0x08070605, 9, 5, 3, 3, left, right);
        var bytes = new byte[PresenceSerializer.MaxSize];
        int size = PresenceSerializer.Write(new PresenceState { PublicHandPick = picture }, bytes);
        var tail = new byte[33]; Array.Copy(bytes, size - tail.Length, tail, 0, tail.Length);
        t.Wire(Hex.Bytes("7C 1F 01 02 03 04 05 06 07 08 09 00 00 00 05 03 03 09 0A 0B 0C 04 00 0C 00 0D 0E 0F 10 01 80 07 00"), tail, 33,
            "124 golden binds both physical seats to their original pool without changing39");
        t.True(PresenceSerializer.TryRead(bytes, size, out var decoded) && decoded.PublicHandPickSeen
            && decoded.PublicHandPick?.SamePicture(picture) == true && decoded.PublicHandPick.Revision == picture.Revision,
            "full presence independently decodes124");
        t.Equal(31, picture.PayloadBytes, "maximum payload is31 bytes");
        t.True(12731 <= ExtrasFragments.MaxSnapshotBytes, "new documented maximum fits unchanged12800 reassembly bound");
        t.True(PublicHandPickState.TryRead(tail, 2, 31, out var direct) && direct?.SamePicture(picture) == true,
            "direct decoder accepts the independent golden");
        for (int length = 0; length < 31; length++)
            t.True(!PublicHandPickState.TryRead(tail, 2, length, out _), "truncated124 cannot publish a partial original " + length);
        t.True(!PublicHandPickState.TryRead(tail, -1, 31, out _), "negative byte offset refused");
        t.True(!PublicHandPickState.TryRead(tail, int.MaxValue, 31, out _), "overflowing byte offset refused");
        t.True(!new PublicHandPickState(0, 1, 0, 1, 0, 0).Valid, "revision zero refused");
        t.True(!new PublicHandPickState(1, 1, -1, 1, 0, 0).Valid, "negative native round refused");
        t.True(!new PublicHandPickState(1, 0, 0, 1, 1, 1, left).Valid, "provenance without actor refused");
        t.True(!new PublicHandPickState(1, 1, 0, 1, 0, 1, left).Valid, "named source outside occupied seats refused");
        t.True(!new PublicHandPickState(1, 1, 0, 1, 1, 0, left).Valid, "hidden provenance cannot accompany anonymous occupancy");
        t.True(!new PublicHandPickState.Seat(1, 7, 7).Valid, "source seat equals count refused");

        t.Case("761:124 anonymous choices and explicit release carry no provenance");
        var anonymous = new PublicHandPickState(5, 1, 0, 1, 3, 0);
        size = PresenceSerializer.Write(new PresenceState { PublicHandPick = anonymous }, bytes);
        var empty = new byte[17]; Array.Copy(bytes, size - 17, empty, 0, 17);
        t.Wire(Hex.Bytes("7C 0F 05 00 00 00 01 00 00 00 00 00 00 00 01 03 00"), empty, 17,
            "anonymous snapshot contains only actor/phase/revision/occupancy");
        var release = new PublicHandPickState(6, 0, 0, 0, 0, 0);
        size = PresenceSerializer.Write(new PresenceState { PublicHandPick = release }, bytes);
        t.True(PresenceSerializer.TryRead(bytes, size, out decoded) && decoded.PublicHandPickSeen
            && decoded.PublicHandPick?.Occupied == 0, "all-zero picture remains an explicit ordered release");
        int legacySize = PresenceSerializer.Write(default, bytes);
        t.True(PresenceSerializer.TryRead(bytes, legacySize, out decoded) && !decoded.PublicHandPickSeen,
            "legacy absence is distinct from an explicit124 release");
        size = PresenceSerializer.Write(new PresenceState { PublicHandPick = picture }, bytes);
        bytes[size - 33] = 254;
        t.True(PresenceSerializer.TryRead(bytes, size, out decoded) && !decoded.PublicHandPickSeen,
            "unknown reader skips the whole new TLV by its original length grammar");

        t.Case("761:124 owner receipt rejects reordering, slot reuse and scenario/rejoin stale state");
        var inbox = new PublicHandPickInbox(); object scenario = new();
        inbox.Observe(true, picture, scenario);
        t.True(ReferenceEquals(inbox.Current(scenario), picture), "first exact owner picture accepted");
        inbox.Observe(true, new PublicHandPickState(picture.Revision - 1, 1, 0, 1, 0, 0), scenario);
        t.True(ReferenceEquals(inbox.Current(scenario), picture), "old packet cannot replace current seats");
        inbox.Observe(true, new PublicHandPickState(picture.Revision, 1, 0, 1, 0, 0), scenario);
        t.True(ReferenceEquals(inbox.Current(scenario), picture), "same revision cannot reuse another actor or slot");
        var tombstone = new PublicHandPickState(picture.Revision + 1, 0, 0, 0, 0, 0);
        inbox.Observe(true, tombstone, scenario); inbox.Observe(true, picture, scenario);
        t.True(ReferenceEquals(inbox.Current(scenario), tombstone), "late named packet cannot resurrect cleared seats");
        inbox.Observe(false, null, scenario);
        t.True(ReferenceEquals(inbox.Current(scenario), tombstone), "unrelated legacy packet cannot undo the ordered release");
        object nextScenario = new();
        t.True(inbox.Current(nextScenario) == null, "changed scenario never reuses old pool originals");
        inbox.Observe(false, null, nextScenario);
        inbox.Observe(true, picture, nextScenario);
        t.True(inbox.Current(nextScenario) == null, "previous scenario sequence cannot become the new scenario picture");
        var nextPicture = new PublicHandPickState(picture.Revision + 2, 1, 0, 1, 3, 0);
        inbox.Observe(true, nextPicture, nextScenario);
        t.True(ReferenceEquals(inbox.Current(nextScenario), nextPicture), "new scenario accepts the next owner revision");
        var rejoined = new PublicHandPickInbox();
        t.True(rejoined.Current(scenario) == null, "new peer inbox has no inherited original");
        t.True(PublicHandPickState.Newer(1, uint.MaxValue) && !PublicHandPickState.Newer(uint.MaxValue, 1),
            "wrapping revision retains monotonic half-range order");
        inbox.Observe(true, null, nextScenario);
        t.True(inbox.Current(nextScenario) == null, "malformed known124 clears an old named picture");
    }
}
