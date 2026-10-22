using System;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>Independent record121 bytes. A captured window render reference is neither the
/// author's coordinate zoom in119 nor the observer's current scene scale.</summary>
internal static class SharedWindowScaleContextVectors
{
    private static byte[] OneRecord() => Hex.Bytes("79 05 01 00 00 46 43"); // kind1,198
    private static byte[] SixRecord() => Hex.Bytes("79 19 3F 00 00 80 3F 00 00 00 40 00 00 80 40 00 00 00 41 00 00 46 43 1F 85 37 41");
    private static SharedWindowScaleContext One(int kind = 1, float reference = 198)
    {
        var context = new SharedWindowScaleContext(); context.Set(kind, reference); return context;
    }
    private static SharedWindowScaleContext Six()
    {
        var context = new SharedWindowScaleContext();
        // Deliberately inserted out of wire order; insertion order is not a protocol field.
        context.Set(6, 11.47f); context.Set(3, 4); context.Set(1, 1);
        context.Set(5, 198); context.Set(4, 8); context.Set(2, 2); return context;
    }
    private static byte[] Packet(params byte[][] records)
    {
        byte[] bytes = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 00");
        Array.Resize(ref bytes, bytes.Length + records.Sum(r => r.Length));
        bytes[10] = (byte)records.Length; int at = 11;
        foreach (byte[] record in records) { Array.Copy(record, 0, bytes, at, record.Length); at += record.Length; }
        return bytes;
    }

    internal static void Run(Harness t)
    {
        Literal(t); Malformed(t); Atomic(t); Cohort(t); Legacy(t); Fragmented(t);
    }

    private static void Literal(Harness t)
    {
        t.Case("shared scales121: literal mask and ascending captured IEEE references");
        t.Equal((byte)121, NetProtocol.ExtIdSharedWindowScaleContext, "record121 is additive after unchanged119/120");
        t.Equal(25, SharedWindowScaleContext.MaxPayloadBytes, "one mask and six float32 references bound the payload");
        var bytes = new byte[27]; int at = 0;
        t.True(SharedWindowScaleContext.Write(bytes, ref at, One()), "single captured reference writes");
        t.Wire(OneRecord(), bytes, at, "kind1 reference198 is literal IEEE little endian");
        at = 0; t.True(SharedWindowScaleContext.Write(bytes, ref at, Six()), "all six kinds write atomically");
        t.Wire(SixRecord(), bytes, at, "six references follow kind order despite reverse/interleaved Set calls");
        t.True(SharedWindowScaleContext.TryRead(SixRecord(), 2, 25, out var read) && read.Valid,
            "independent six-kind bytes parse without a writer-generated expectation");
        foreach (var pair in new[] { (1, 1f), (2, 2f), (3, 4f), (4, 8f), (5, 198f), (6, 11.47f) })
            t.True(read.Has(pair.Item1) && read.Reference(pair.Item1) == pair.Item2,
                "literal reference stays attached to its kind: " + pair.Item1);
        for (int kind = -1; kind <= 8; kind++)
        {
            var single = One(kind);
            bool defined = kind >= 1 && kind <= 6;
            t.Equal(defined, single.Valid, "only registered kinds can create a valid context: " + kind);
            t.Equal(defined, single.Has(kind), "unknown kind cannot alias another mask bit: " + kind);
            t.Equal(defined ? 198f : 0f, single.Reference(kind), "absent reference has no implicit scene-scale fallback");
            if (!defined) continue;
            at = 0; SharedWindowScaleContext.Write(bytes, ref at, single);
            byte[] literal = Hex.Bytes("79 05 00 00 00 46 43"); literal[2] = (byte)(1 << (kind - 1));
            t.Wire(literal, bytes, at, "a single kind uses its own bit and no absent placeholders");
        }
        foreach (float value in new[] { .0001f, 1000000f })
        {
            var endpoint = One(6, value); at = 0;
            t.True(endpoint.Valid && SharedWindowScaleContext.Write(bytes, ref at, endpoint)
                && SharedWindowScaleContext.TryRead(bytes, 2, 5, out read) && read.Reference(6) == value,
                "both finite inclusive render-reference endpoints remain representable");
        }
        var repeated = One(); repeated.Set(1, 11.47f); at = 0;
        SharedWindowScaleContext.Write(bytes, ref at, repeated);
        t.Wire(Hex.Bytes("79 05 01 1F 85 37 41"), bytes, at, "replacing a kind replaces its single captured float");
        var fields = typeof(SharedWindowScaleContext).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        t.True(fields.Length == 7 && fields.Count(f => f.FieldType == typeof(float)) == 6
            && fields.Count(f => f.FieldType == typeof(byte)) == 1,
            "context storage is exactly six floats and a byte, without an allocated collection");
        bytes = new byte[PresenceSerializer.MaxSize];
        int size = PresenceSerializer.Write(new PresenceState { WindowScales = One() }, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 79 05 01 00 00 46 43"), bytes, size,
            "complete scale-only snapshot has the exact original presence framing");
        t.True(PresenceSerializer.TryRead(Packet(OneRecord()), 18, out var state)
            && state.WindowScales.HasValue && state.WindowScales.Value.Reference(1) == 198,
            "presence reader installs the optional references from the same packet");
    }

    private static void Malformed(Harness t)
    {
        t.Case("shared scales121: bad masks, missing members and invalid floats are skipped");
        foreach (byte mask in new byte[] { 0, 64, 65, 128, 255 })
        {
            byte[] record = OneRecord(); record[2] = mask;
            t.True(!SharedWindowScaleContext.TryRead(record, 2, 5, out _), "unknown or empty mask is invalid: " + mask);
            byte[] packet = Packet(record);
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out var state) && !state.WindowScales.HasValue,
                "invalid mask cannot install partial scale state in presence");
        }
        for (int length = 0; length < 25; length++)
            t.True(!SharedWindowScaleContext.TryRead(SixRecord(), 2, length, out _),
                "six-kind mask requires every advertised float: " + length);
        foreach (var bounds in new[] { (at: -1, length: 25), (at: 3, length: 25), (at: 2, length: 26), (at: 0, length: -1) })
            t.True(!SharedWindowScaleContext.TryRead(SixRecord(), bounds.at, bounds.length, out _),
                "reader rejects invalid actual-buffer boundaries");
        foreach (byte[] invalid in new[] {
            Hex.Bytes("00 00 00 00"), Hex.Bytes("00 00 00 80"), Hex.Bytes("00 00 80 BF"),
            Hex.Bytes("00 00 C0 7F"), Hex.Bytes("00 00 80 7F"), Hex.Bytes("00 00 80 FF"),
            Hex.Bytes("AC C5 27 37"), Hex.Bytes("10 24 74 49") })
        for (int kind = 1; kind <= 6; kind++)
        {
            byte[] record = SixRecord(); Array.Copy(invalid, 0, record, 3 + 4 * (kind - 1), 4);
            t.True(!SharedWindowScaleContext.TryRead(record, 2, 25, out var rejected) && !rejected.Valid,
                "every member rejects zero, negative, nonfinite or out-of-range reference: " + kind);
            // The following original story must survive a malformed optional121.
            byte[] packet = Packet(record, Hex.Bytes("13 07 01 02 05 EF BE AD DE"));
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out var state)
                && !state.WindowScales.HasValue && state.HasStorySync && state.StoryKey == 0xDEADBEEF,
                "one invalid scale rejects the cohort without swallowing the next legacy record");
        }
        byte[] missing = OneRecord(); missing[2] = 3;
        t.True(!SharedWindowScaleContext.TryRead(missing, 2, 5, out _), "one float cannot satisfy a two-kind mask");
        byte[] future = SixRecord().Concat(Hex.Bytes("CC DD EE")).ToArray(); future[1] += 3;
        t.True(SharedWindowScaleContext.TryRead(future, 2, 28, out var extended) && extended.Reference(6) == 11.47f,
            "known mask and complete prefix accept a future length-delimited tail");
        byte[] extendedPacket = Packet(future);
        t.True(PresenceSerializer.TryRead(extendedPacket, extendedPacket.Length, out var futureState)
            && futureState.WindowScales.HasValue, "presence walker accepts the extended121 prefix too");
        byte[] unknown = Packet(OneRecord()); unknown[11] = 250;
        t.True(PresenceSerializer.TryRead(unknown, unknown.Length, out var absent) && !absent.WindowScales.HasValue,
            "an unknown additive record does not become a scale context");
        byte[] full = Packet(SixRecord());
        for (int length = 11; length < full.Length; length++)
            t.True(!PresenceSerializer.TryRead(full, length, out var state) || !state.WindowScales.HasValue,
                "truncating the complete snapshot never exposes a partial reference cohort");
    }

    private static void Atomic(Harness t)
    {
        t.Case("shared scales121: writer bounds and invalid cohorts are atomic");
        foreach (float value in new[] { 0f, -0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, .00001f, 1000001f })
        foreach (int kind in new[] { 1, 3, 6 })
        {
            var invalid = Six(); invalid.Set(kind, value);
            byte[] bytes = Enumerable.Repeat((byte)0xCC, 50).ToArray(); int at = 3;
            t.True(!invalid.Valid && !SharedWindowScaleContext.Write(bytes, ref at, invalid)
                && at == 3 && bytes.All(b => b == 0xCC), "invalid member cannot partially write a scale cohort");
            int size = PresenceSerializer.Write(new PresenceState { WindowScales = invalid }, bytes);
            t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, size,
                "invalid optional reference creates no empty extension tail");
        }
        foreach (byte mask in new byte[] { 0, 64, 255 })
        {
            var invalid = One(); invalid.Mask = mask;
            byte[] bytes = Enumerable.Repeat((byte)0xCC, 50).ToArray(); int at = 3;
            t.True(!SharedWindowScaleContext.Write(bytes, ref at, invalid) && at == 3 && bytes.All(b => b == 0xCC),
                "invalid or empty writer mask is rejected before any write");
        }
        for (int capacity = 0; capacity < 27; capacity++)
        {
            byte[] bytes = Enumerable.Repeat((byte)0xCC, capacity).ToArray(); int at = 0;
            t.True(!SharedWindowScaleContext.Write(bytes, ref at, Six()) && at == 0 && bytes.All(b => b == 0xCC),
                "undersized output cannot expose a record prefix: " + capacity);
        }
        foreach (int start in new[] { -1, 1, int.MaxValue })
        {
            byte[] bytes = Enumerable.Repeat((byte)0xCC, 27).ToArray(); int at = start;
            t.True(!SharedWindowScaleContext.Write(bytes, ref at, Six()) && at == start && bytes.All(b => b == 0xCC),
                "out-of-bounds cursor cannot alter the buffer");
        }
        for (int capacity = 11; capacity < 38; capacity++)
        {
            byte[] bytes = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
            int size = PresenceSerializer.Write(new PresenceState { WindowScales = Six() }, bytes);
            t.True(size == 11 && bytes[10] == 0 && bytes.Skip(11).All(b => b == 0xCC),
                "capacity-limited presence omits the entire121 record: " + capacity);
        }
    }

    private static void Cohort(Harness t)
    {
        t.Case("shared scales121: captured references remain distinct from coordinate and observer zoom");
        var sourceFrame = new SharedWindowPoseContext { Origin = new Vector3(1, 2, 3), Yaw = 90, Scale = 3 };
        var relative = new RigPose { Position = new Vector3(2, 1, 0), Rotation = Quaternion.identity };
        var bytes = new byte[PresenceSerializer.MaxSize];
        foreach (float reference in new[] { 198f, 11.47f })
        {
            var state = new PresenceState { HasStorySync = true, StoryFlags = 3, StoryKey = 42,
                StoryPose = relative, WindowPoseContext = sourceFrame, WindowScales = One(1, reference) };
            int size = PresenceSerializer.Write(state, bytes);
            t.True(PresenceSerializer.TryRead(bytes, size, out var read) && read.WindowScales.HasValue
                && read.WindowPoseContext.HasValue && read.WindowScales.Value.Reference(1) == reference
                && read.WindowPoseContext.Value.Scale == 3,
                "same-packet render reference survives independently of coordinate scale: " + reference);
            t.True(read.WindowPoseContext.GetValueOrDefault().ToWorld(read.StoryPose, out var world)
                && (world.Position - new Vector3(1, 5, -3)).sqrMagnitude < .000001f,
                "render-reference differences do not change author-frame pose reconstruction");
            t.True(!read.WindowScales.GetValueOrDefault().Has(2) && read.WindowScales.GetValueOrDefault().Reference(2) == 0,
                "a posed kind cannot borrow another window's captured reference");
        }
        int withoutScales = PresenceSerializer.Write(new PresenceState { WindowPoseContext = sourceFrame }, bytes);
        t.True(PresenceSerializer.TryRead(bytes, withoutScales, out var independent)
            && independent.WindowPoseContext.HasValue && !independent.WindowScales.HasValue,
            "a later packet with a coordinate frame cannot inherit earlier captured references");
        int idle = PresenceSerializer.Write(default, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, idle, "absent121 leaves original idle bytes unchanged");
    }

    private static void Legacy(Harness t)
    {
        t.Case("shared scales121: old19/21/119/120 bytes retain exact grammar");
        var state = new PresenceState { HasStorySync = true, StoryFlags = 1, StoryPage = 2,
            StoryPageCount = 5, StoryKey = 0xDEADBEEF, WindowScales = One() };
        var bytes = new byte[PresenceSerializer.MaxSize]; int size = PresenceSerializer.Write(state, bytes);
        t.Wire(Packet(Hex.Bytes("13 07 01 02 05 EF BE AD DE"), OneRecord()), bytes, size,
            "legacy story19 bytes precede additive121 unchanged");
        state = new PresenceState { HasSharedWindow = true, SharedWindowCount = 1,
            SharedWindowEntries = new[] { new SharedWindowEntry { Kind = 2, Flags = 1, Page = 255, ContentKey = 0x04030201 } },
            WindowScales = One(2) };
        size = PresenceSerializer.Write(state, bytes);
        t.Wire(Packet(Hex.Bytes("15 09 01 02 01 FF 00 01 02 03 04"), Hex.Bytes("79 05 02 00 00 46 43")), bytes, size,
            "legacy map21 bytes precede their exact matching-kind reference");
        state = new PresenceState { HasMapRoom = true, MapRoomFlags = 1, MapRoomSurfaceStamp = 7,
            MapRoomPickKey = 0x11223344, MapRoomSelectStamp = 9, MapRoomSelectKey = 0x55667788,
            HasMapSelectionVersion = true, MapSelectionRevision = 0x08070605, MapSelectionParent = 0x04030201,
            MapSelectionParentAuthor = 0x0C0B0A09, WindowPoseContext = new SharedWindowPoseContext {
                Host = true, Initial = 0x25, Origin = new Vector3(1, -2, 3), Yaw = 90, Scale = 2 }, WindowScales = Six() };
        size = PresenceSerializer.Write(state, bytes);
        t.Wire(Packet(Hex.Bytes("14 10 01 07 44 33 22 11 09 88 77 66 55 00 00 00 00 00"),
            Hex.Bytes("77 16 01 25 00 00 80 3F 00 00 00 C0 00 00 40 40 00 00 B4 42 00 00 00 40"),
            Hex.Bytes("78 0C 05 06 07 08 01 02 03 04 09 0A 0B 0C"), SixRecord()), bytes, size,
            "independent119 and120 golden records remain byte-exact beside121");
    }

    private static void Fragmented(Harness t)
    {
        t.Case("shared scales121: the full encoded worst case stays below bounded12800");
        t.Equal(12772, PresenceSerializer.MaxSize, "allocation includes257 spare bytes after actual12515 maximum");
        t.Equal(12800, ExtrasFragments.MaxSnapshotBytes, "transport reassembly capacity is unchanged");
        foreach (int size in new[] { 38, 12515, 8000 })
        {
            byte[] snapshot = new byte[size]; byte[] original = Packet(SixRecord()); Array.Copy(original, snapshot, original.Length);
            byte[][] pages = ExtrasFragments.Encode(snapshot, size, (ulong)size); var receiver = new ExtrasFragments(); byte[]? complete = null;
            for (int index = pages.Length - 1; index >= 0; index--)
            {
                byte[]? result = receiver.Accept(2, pages[index], pages[index].Length, 0);
                if (index > 0) t.True(result == null, "partial fragments cannot expose any captured render reference");
                else complete = result;
            }
            t.True(complete != null, "complete reference cohort reassembles at encoded length " + size);
            if (complete != null) t.Wire(snapshot, complete, complete.Length, "fragmentation preserves the entire121 cohort byte-exactly");
        }
    }
}
