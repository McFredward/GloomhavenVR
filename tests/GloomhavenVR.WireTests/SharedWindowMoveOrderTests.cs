using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class SharedWindowMoveOrderTests
{
    private static SharedWindowMoveEntry Row(byte kind = 4) => new()
    {
        Kind = kind, Phase = SharedWindowMovePhase.Held, ContentKey = 0x11223344,
        OpeningAuthor = 2, Opening = 0x55667788, Claim = 0x01020304, Sequence = 0xA1B2C3D4,
    };

    internal static void Run(Harness t)
    {
        t.Case("shared window order123: independent bytes and actual presence codec");
        var bytes = new byte[PresenceSerializer.MaxSize];
        var state = new PresenceState { WindowMoves = new SharedWindowMoveOrder(new[] { Row() }) };
        int length = PresenceSerializer.Write(in state, bytes);
        byte[] expected = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 7B 1F 01 04 01 44 33 22 11 02 00 00 00 88 77 66 55 04 03 02 01 D4 C3 B2 A1 00 00 00 00 00 00 00 00");
        t.Wire(expected, bytes, length, "golden123 fixes count, kind, phase and seven little-endian32 fields independently");
        t.True(PresenceSerializer.TryRead(bytes, length, out PresenceState read)
            && read.WindowMoves != null && read.WindowMoves.TryGet(4, 0x11223344, out SharedWindowMoveEntry row)
            && row.OpeningAuthor == 2 && row.Opening == 0x55667788
            && row.Claim == 0x01020304 && row.Sequence == 0xA1B2C3D4 && row.Phase == SharedWindowMovePhase.Held,
            "actual presence reader retains complete opening, claim and sample identity");
        for (int cut = 0; cut < expected.Length; cut++)
        {
            bool valid = PresenceSerializer.TryRead(expected, cut, out read);
            t.True(!valid || read.WindowMoves == null, "cut123 cannot manufacture a complete companion " + cut);
        }

        t.Case("shared window order123: malformed fields and duplicate kinds reject as a unit");
        foreach (int offset in new[] { 14, 15, 16, 20, 24, 28, 32 })
        {
            byte[] invalid = (byte[])expected.Clone();
            int count = offset >= 16 ? 4 : 1;
            Array.Clear(invalid, offset, count);
            if (offset == 15) invalid[offset] = 255;
            t.True(PresenceSerializer.TryRead(invalid, invalid.Length, out read) && read.WindowMoves == null,
                "invalid123 field rejects the whole companion at " + offset);
        }
        var duplicate = new SharedWindowMoveOrder(new[] { Row(), Row() });
        int at = 0;
        t.True(!SharedWindowMoveOrder.Write(bytes, ref at, duplicate) && at == 0,
            "duplicate kinds never partially write a companion");
        byte[] two = new byte[1 + 60]; two[0] = 2;
        Array.Copy(expected, 14, two, 1, 30); Array.Copy(expected, 14, two, 31, 30);
        t.True(!SharedWindowMoveOrder.TryRead(two, 0, two.Length, out _),
            "reader independently refuses duplicate kinds");
        byte[] repeated = new byte[expected.Length + 33];
        Array.Copy(expected, repeated, expected.Length); repeated[10] = 2;
        Array.Copy(expected, 11, repeated, expected.Length, 33);
        t.True(PresenceSerializer.TryRead(repeated, repeated.Length, out read)
            && read.WindowMoveOrderRecordSeen && read.WindowMoves == null,
            "duplicate123 records cannot lend a second ordering identity to one pose snapshot");

        t.Case("shared window order123: bounded maximum and capacity never change legacy bytes");
        var rows = new SharedWindowMoveEntry[6];
        for (byte kind = 1; kind <= 6; kind++) rows[kind - 1] = Row(kind);
        var maximum = new SharedWindowMoveOrder(rows);
        at = 0;
        t.True(SharedWindowMoveOrder.Write(bytes, ref at, maximum) && at == 183 && bytes[1] == 181 && bytes[2] == 6,
            "all six kinds occupy exactly183 bytes including TLV header");
        t.True(SharedWindowMoveOrder.TryRead(bytes, 2, 181, out SharedWindowMoveOrder? decoded)
            && decoded != null && decoded.Entries.Length == 6, "maximum complete payload decodes all six kinds");
        for (int capacity = 0; capacity < 183; capacity++)
        {
            int cursor = 0;
            t.True(!SharedWindowMoveOrder.Write(new byte[capacity], ref cursor, maximum) && cursor == 0,
                "insufficient123 capacity leaves the cursor untouched " + capacity);
        }
        // The full maximum companion is one snapshot cohort with all six original pose records,
        // their common coordinate source and individual physical references.
        state.WindowMoves = maximum;
        state.HasStorySync = true; state.StoryFlags = 3; state.StoryKey = 0x11223344;
        state.StoryPoseStamp = 17; state.StorySizeCode = 127;
        state.HasSharedWindow = true; state.SharedWindowCount = 3;
        state.SharedWindowEntries = new SharedWindowEntry[3];
        for (byte kind = 1; kind <= 3; kind++) state.SharedWindowEntries[kind - 1] = new SharedWindowEntry
            { Kind = kind, Flags = 3, ContentKey = 0x11223344, PoseStamp = 19, SizeCode = 127 };
        state.HasVideoWindow = true; state.VideoWindow = new VideoWindowState
            { Token = 1, Clip = "Heroes/Doomstalker.mov", Window = new SharedWindowEntry { Flags = 3, ContentKey = 0x11223344, SizeCode = 127 } };
        state.HasRewardWindow = true; state.RewardWindow = new RewardWindowState
            { Window = new SharedWindowEntry { Flags = 3, ContentKey = 0x11223344, SizeCode = 127,
                Frame = NetProtocol.RewardFrameScenario, Pose = new RigPose { Rotation = UnityEngine.Quaternion.identity } } };
        state.WindowPoseContext = new SharedWindowPoseContext { Origin = new UnityEngine.Vector3(1, 2, 3), Scale = 2, Yaw = 90 };
        var references = new SharedWindowScaleContext(); for (int kind = 1; kind <= 6; kind++) references.Set(kind, kind);
        state.WindowScales = references;
        length = PresenceSerializer.Write(in state, bytes);
        t.True(PresenceSerializer.TryRead(bytes, length, out read) && read.WindowMoves != null
            && read.WindowMoves.Entries.Length == 6 && read.HasStorySync && read.HasSharedWindow
            && read.HasVideoWindow && read.HasRewardWindow && read.WindowPoseContext.HasValue && read.WindowScales.HasValue,
            "actual full presence keeps six original poses and119/121/123 together");
        var pages = ExtrasFragments.Encode(bytes, length, 101); var assembler = new ExtrasFragments(); byte[]? complete = null;
        for (int i = pages.Length - 1; i >= 0; i--)
        {
            byte[]? received = assembler.Accept(2, pages[i], pages[i].Length, 0);
            if (i > 0) t.True(received == null, "fragment cohort cannot expose partial pose or authority");
            else complete = received;
        }
        t.True(complete != null && PresenceSerializer.TryRead(complete, complete.Length, out read)
            && read.WindowMoves != null && read.WindowMoves.Entries.Length == 6, "reversed fragment delivery retains atomic six-window pose order");
        t.True(PresenceSerializer.MaxSize == 12955 && ExtrasFragments.MaxSnapshotBytes == 12800
            && PresenceSerializer.MaxSize - (12515 + 183) == 257 && ExtrasFragments.MaxSnapshotBytes - (12515 + 183) == 102,
            "allocation margin and unchanged reassembly cap account for the exact maximum123");
        // Exercise the largest real original writer groups, not only a paper buffer sum:
        // every recent launch and durable completion plus all six new pose-order rows.
        var maximumFactory = typeof(CardDismissOriginVectors).GetMethod("Maximum",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        object saturated = (PresenceState)maximumFactory.Invoke(null, null)!;
        foreach (var field in typeof(PresenceState).GetFields())
        {
            object? value = field.GetValue(state);
            object? empty = field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null;
            if (!Equals(value, empty)) field.SetValue(saturated, value);
        }
        var largestWriters = (PresenceState)saturated;
        int largestLength = PresenceSerializer.Write(in largestWriters, bytes);
        t.True(PresenceSerializer.TryRead(bytes, largestLength, out read) && read.WindowMoves?.Entries.Length == 6
            && read.FlightHistory?.Events.Length == 8 && read.BurnCompletions?.Entries.Length == 128,
            "actual maximum recent/durable writers coexist with every six-window pose order");
        Console.WriteLine("WINDOW123_ACTUAL_WRITER bytes=" + largestLength + " rows=6 recent=8 durable=128");
        // Fill the remaining established upper bound with explicitly unknown, length-delimited
        // records. This is an adversarial full-envelope proof; it does not invent native state.
        const int completeBound = 12698;
        var saturatedBytes = new byte[completeBound]; Array.Copy(bytes, saturatedBytes, largestLength);
        int fill = largestLength;
        while (fill < completeBound)
        {
            int total = Math.Min(257, completeBound - fill);
            if (completeBound - fill - total == 1) total--;
            saturatedBytes[fill++] = 254; saturatedBytes[fill++] = (byte)(total - 2);
            fill += total - 2; saturatedBytes[10]++;
        }
        var saturatedPages = ExtrasFragments.Encode(saturatedBytes, completeBound, 102);
        var saturatedReceiver = new ExtrasFragments(); complete = null;
        for (int i = saturatedPages.Length - 1; i >= 0; i--)
        {
            byte[]? received = saturatedReceiver.Accept(2, saturatedPages[i], saturatedPages[i].Length, 0);
            if (i > 0) t.True(received == null, "full-bound reversed pages expose no partial authority cohort");
            else complete = received;
        }
        t.True(complete != null && complete.Length == completeBound
            && PresenceSerializer.TryRead(complete, complete.Length, out read) && read.WindowMoves?.Entries.Length == 6
            && read.FlightHistory?.Events.Length == 8 && read.BurnCompletions?.Entries.Length == 128,
            "full12698-byte envelope preserves actual largest writers and six-window order atomically");
        state = default;
        state.WindowMoves = null; length = PresenceSerializer.Write(in state, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, length,
            "absent123 retains exact legacy idle extras");
        byte[] badThenVersion = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 02 7B 01 01 03 02 06 02");
        t.True(PresenceSerializer.TryRead(badThenVersion, badThenVersion.Length, out read)
            && read.WindowMoves == null && read.HasModVersion && read.ModBuild == 518,
            "short123 preserves the following unrelated version record");
    }
}
