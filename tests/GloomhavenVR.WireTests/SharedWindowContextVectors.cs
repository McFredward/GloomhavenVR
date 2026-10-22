using System;
using System.Linq;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>Independent record119 bytes and actual author-frame reconstruction. The original
/// pose records retain their grammar; this additive context never reads an observer frame.</summary>
internal static class SharedWindowContextVectors
{
    // Spec-derived IEEE754 little-endian fields: origin(1,-2,3), yaw90, scale2.
    // Initial0x25 names kinds1/3/6. Neither expected array comes from the writer.
    private static byte[] Record() => Hex.Bytes("77 16 01 25 00 00 80 3F 00 00 00 C0 00 00 40 40 00 00 B4 42 00 00 00 40");
    private static byte[] Packet() => Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 77 16 01 25 00 00 80 3F 00 00 00 C0 00 00 40 40 00 00 B4 42 00 00 00 40");
    private static SharedWindowPoseContext Context() => new()
    { Host = true, Initial = 0x25, Origin = new Vector3(1, -2, 3), Yaw = 90, Scale = 2 };
    private static bool Near(Vector3 actual, Vector3 expected) => (actual - expected).sqrMagnitude < .000001f;

    internal static void Run(Harness t)
    {
        Literal(t);
        Malformed(t);
        Legacy(t);
        AuthorFrame(t);
        Fragmented(t);
    }

    private static void Literal(Harness t)
    {
        t.Case("shared context119: literal flags, initial kinds and five IEEE fields");
        var context = Context();
        var record = new byte[24];
        int at = 0;
        t.True(SharedWindowPoseContext.Write(record, ref at, context), "valid original source frame writes");
        t.Wire(Record(), record, at, "record119/22 is host, six-bit opening mask, originXYZ, yaw and scale");
        t.Equal(24, at, "the bounded record occupies24 bytes including its TLV header");
        t.True(SharedWindowPoseContext.TryRead(Record(), 2, 22, out var literal), "the independently specified record parses");
        t.True(literal.Host && literal.Initial == 0x25 && literal.Origin == new Vector3(1, -2, 3)
            && literal.Yaw == 90 && literal.Scale == 2, "every literal field retains its declared offset");
        var bytes = new byte[PresenceSerializer.MaxSize];
        int size = PresenceSerializer.Write(new PresenceState { WindowPoseContext = context }, bytes);
        t.Wire(Packet(), bytes, size, "complete context-only presence grammar including extension count");
        t.Equal(35, size, "8-byte base, 3-byte extension framing and24-byte record total35");
        t.True(PresenceSerializer.TryRead(Packet(), Packet().Length, out var read) && read.WindowPoseContext.HasValue,
            "full packet reader discovers the additive source context");
        for (int kind = -1; kind <= 8; kind++)
        {
            byte expected = kind >= 1 && kind <= 6 ? (byte)(1 << (kind - 1)) : (byte)0;
            t.Equal(expected, SharedWindowPoseContext.KindBit(kind), "only the six registered shared kinds have opening bits: " + kind);
            t.Equal(kind == 1 || kind == 3 || kind == 6, literal.IsInitial(kind), "opening membership follows its own bit: " + kind);
        }
        foreach (byte mask in new byte[] { 0, 1, 2, 4, 8, 16, 32, 63 })
        {
            context.Initial = mask; context.Host = false;
            at = 0;
            t.True(SharedWindowPoseContext.Write(record, ref at, context)
                && record[2] == 0 && record[3] == mask
                && SharedWindowPoseContext.TryRead(record, 2, 22, out var visitor)
                && !visitor.Host && visitor.Initial == mask, "all defined masks and nonhost authors retain exact fields: " + mask);
        }
        foreach (float yaw in new[] { 0f, 360f })
        foreach (float scale in new[] { .0001f, 1000000f })
        { context.Yaw = yaw; context.Scale = scale; t.True(context.Valid, "declared finite yaw/scale endpoints are admitted"); }
    }

    private static void Malformed(Harness t)
    {
        t.Case("shared context119: malformed source frames never become placements");
        byte[] record = Record();
        for (int length = 0; length < 22; length++)
            t.True(!SharedWindowPoseContext.TryRead(record, 2, length, out _), "truncated context payload rejected: " + length);
        t.True(!SharedWindowPoseContext.TryRead(record, -1, 22, out _), "negative payload offset rejected");
        t.True(!SharedWindowPoseContext.TryRead(record, 3, 22, out _), "payload beyond actual buffer rejected");
        foreach (int offset in new[] { 2, 3 })
        foreach (byte invalid in offset == 2 ? new byte[] { 2, 3, 128, 255 } : new byte[] { 64, 128, 255 })
        {
            byte[] corrupt = Record(); corrupt[offset] = invalid;
            t.True(!SharedWindowPoseContext.TryRead(corrupt, 2, 22, out _), "unknown host/opening bits rejected at offset " + offset);
            byte[] packet = Packet(); packet[offset + 11] = invalid;
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out var read) && !read.WindowPoseContext.HasValue,
                "malformed additive context is skipped without corrupting the presence prefix");
        }
        foreach (int offset in new[] { 4, 8, 12, 16, 20 })
        foreach (byte[] invalid in new[] { Hex.Bytes("00 00 C0 7F"), Hex.Bytes("00 00 80 7F"), Hex.Bytes("00 00 80 FF") })
        {
            byte[] corrupt = Record(); Array.Copy(invalid, 0, corrupt, offset, 4);
            t.True(!SharedWindowPoseContext.TryRead(corrupt, 2, 22, out _), "NaN or infinity rejected in every numeric field: " + offset);
        }
        foreach (byte[] invalid in new[] { Hex.Bytes("00 00 00 00"), Hex.Bytes("00 00 80 BF"), Hex.Bytes("00 00 00 80"), Hex.Bytes("00 00 80 3F") })
        {
            // The last literal is a valid scale1 control among the malformed zero/negative cases.
            byte[] corrupt = Record(); Array.Copy(invalid, 0, corrupt, 20, 4);
            bool positiveOne = invalid[3] == 0x3F;
            t.Equal(positiveOne, SharedWindowPoseContext.TryRead(corrupt, 2, 22, out _), "scale must be strictly positive within declared bounds");
        }
        foreach (var field in new[] {
            (offset: 16, bytes: Hex.Bytes("00 00 80 BF")), // yaw-1
            (offset: 16, bytes: Hex.Bytes("00 80 B4 43")), // yaw361
            (offset: 20, bytes: Hex.Bytes("AC C5 27 37")), // scale0.00001
            (offset: 20, bytes: Hex.Bytes("10 24 74 49")), // scale1000001
        })
        {
            byte[] corrupt = Record(); Array.Copy(field.bytes, 0, corrupt, field.offset, 4);
            t.True(!SharedWindowPoseContext.TryRead(corrupt, 2, 22, out _), "finite out-of-range yaw/scale cannot be read as a valid author frame");
        }
        foreach (var invalid in new[] {
            new SharedWindowPoseContext { Initial = 64, Scale = 1 },
            new SharedWindowPoseContext { Origin = new Vector3(float.NaN, 0, 0), Scale = 1 },
            new SharedWindowPoseContext { Origin = new Vector3(0, float.PositiveInfinity, 0), Scale = 1 },
            new SharedWindowPoseContext { Yaw = -1, Scale = 1 },
            new SharedWindowPoseContext { Yaw = 361, Scale = 1 },
            new SharedWindowPoseContext { Yaw = float.NaN, Scale = 1 },
            new SharedWindowPoseContext { Scale = 0 },
            new SharedWindowPoseContext { Scale = -1 },
            new SharedWindowPoseContext { Scale = .00001f },
            new SharedWindowPoseContext { Scale = 1000001 },
        })
        {
            byte[] untouched = Enumerable.Repeat((byte)0xCC, 40).ToArray();
            int at = 3;
            t.True(!SharedWindowPoseContext.Write(untouched, ref at, invalid) && at == 3
                && untouched.All(b => b == 0xCC), "invalid writer is atomic and cannot advance or alter the buffer");
            t.True(!invalid.ToWorld(default, out _), "invalid frame cannot be used for physical placement");
            var idle = new byte[PresenceSerializer.MaxSize];
            int size = PresenceSerializer.Write(new PresenceState { WindowPoseContext = invalid }, idle);
            t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), idle, size,
                "refusing an invalid optional context cannot create an empty extension tail");
        }
        foreach (int start in new[] { -1, 1, 100 })
        {
            byte[] small = new byte[24]; int at = start;
            t.True(!SharedWindowPoseContext.Write(small, ref at, Context()) && at == start,
                "out-of-bounds writer leaves its cursor unchanged");
        }
        for (int length = 11; length < Packet().Length; length++)
            t.True(!PresenceSerializer.TryRead(Packet(), length, out var read) || !read.WindowPoseContext.HasValue,
                "truncated full packet never admits a partial coordinate frame");
        byte[] unknown = Packet(); unknown[11] = 250;
        t.True(PresenceSerializer.TryRead(unknown, unknown.Length, out var absent) && !absent.WindowPoseContext.HasValue,
            "unknown additive record is safely skipped");
        byte[] future = new byte[25]; Array.Copy(record, future, record.Length); future[24] = 0xCC;
        t.True(SharedWindowPoseContext.TryRead(future, 2, 23, out _), "a future length-delimited payload tail preserves the known22-byte prefix");
    }

    private static void Legacy(Harness t)
    {
        t.Case("shared context119: absence and old story/map pose records retain exact bytes");
        var bytes = new byte[PresenceSerializer.MaxSize];
        int size = PresenceSerializer.Write(default, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, size, "idle packets remain pre-context byte-identical");
        var story = new PresenceState { HasStorySync = true, StoryFlags = 1, StoryPage = 2, StoryPageCount = 5, StoryKey = 0xDEADBEEF, StoryInitialPose = true };
        size = PresenceSerializer.Write(story, bytes);
        byte[] expected = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 13 07 01 02 05 EF BE AD DE");
        t.Wire(expected, bytes, size, "local opening metadata cannot alter legacy record19");
        story.WindowPoseContext = Context();
        size = PresenceSerializer.Write(story, bytes);
        byte[] stripped = bytes.Take(size - 24).ToArray(); stripped[10] = 1;
        t.Wire(expected, stripped, stripped.Length, "stripping context119 restores the exact legacy19 packet");
        var entry = new SharedWindowEntry { InitialPose = true, Kind = 2, Flags = 1, Page = 255, ContentKey = 0x04030201 };
        var map = new PresenceState { HasSharedWindow = true, SharedWindowCount = 1, SharedWindowEntries = new[] { entry } };
        size = PresenceSerializer.Write(map, bytes);
        expected = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 15 09 01 02 01 FF 00 01 02 03 04");
        t.Wire(expected, bytes, size, "local opening metadata cannot alter legacy map record21");
        map.WindowPoseContext = Context();
        size = PresenceSerializer.Write(map, bytes);
        stripped = bytes.Take(size - 24).ToArray(); stripped[10] = 1;
        t.Wire(expected, stripped, stripped.Length, "stripping context119 restores the exact legacy21 packet");
        t.True(PresenceSerializer.TryRead(bytes, size, out var read) && read.HasSharedWindow
            && read.WindowPoseContext.HasValue && read.SharedWindowEntries![0].ContentKey == 0x04030201,
            "map identity and the author frame are parsed atomically in one unchanged snapshot");

        // Both old pose-bearing forms are literal too:20-byte pose remains floatXYZ + quatI16.
        var pose = new RigPose { Position = new Vector3(.5f, .25f, -1.5f), Rotation = Quaternion.identity };
        story.StoryFlags = 3; story.StoryPage = 0; story.StoryPageCount = 3; story.StoryKey = 0x00010203;
        story.StoryPoseStamp = 7; story.StorySizeCode = 150; story.StoryPose = pose;
        size = PresenceSerializer.Write(story, bytes);
        stripped = bytes.Take(size - 24).ToArray(); stripped[10] = 1;
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 13 1D 03 00 03 03 02 01 00 07 96 00 00 00 3F 00 00 80 3E 00 00 C0 BF 00 00 00 00 00 00 FF 7F"),
            stripped, stripped.Length, "legacy19 pose offsets remain unchanged beside context119");
        entry.Flags = 3; entry.PoseStamp = 7; entry.SizeCode = 150; entry.Frame = 0; entry.Pose = pose;
        map.SharedWindowEntries = new[] { entry };
        size = PresenceSerializer.Write(map, bytes);
        stripped = bytes.Take(size - 24).ToArray(); stripped[10] = 1;
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 15 20 01 02 03 FF 00 01 02 03 04 07 96 00 00 00 00 3F 00 00 80 3E 00 00 C0 BF 00 00 00 00 00 00 FF 7F"),
            stripped, stripped.Length, "legacy21 pose frame/float/quaternion offsets remain unchanged beside context119");
    }

    private static void AuthorFrame(Harness t)
    {
        t.Case("shared context119: physical pose uses the packet author's origin/yaw/zoom");
        var relative = new RigPose { Position = new Vector3(.5f, 1, -1.5f), Rotation = Quaternion.identity };
        t.True(PresenceSerializer.TryRead(Packet(), Packet().Length, out var read) && read.WindowPoseContext.HasValue,
            "source frame is read from the same snapshot as its relative pose");
        var context = read.WindowPoseContext.GetValueOrDefault();
        t.True(context.ToWorld(relative, out var actual) && Near(actual.Position, new Vector3(-2, 0, 2)),
            "author origin(1,-2,3), yaw90 and zoom2 reconstruct physical world(-2,0,2)");
        t.True(Near(actual.Rotation * Vector3.forward, Vector3.right), "physical orientation follows the author yaw exactly");
        var bytes = new byte[PresenceSerializer.MaxSize];
        int size = PresenceSerializer.Write(new PresenceState { HasStorySync = true, StoryFlags = 3,
            StoryPose = relative, WindowPoseContext = context }, bytes);
        t.True(PresenceSerializer.TryRead(bytes, size, out var cohort) && cohort.HasStorySync
            && cohort.WindowPoseContext.HasValue && cohort.WindowPoseContext.Value.ToWorld(cohort.StoryPose, out var atomic)
            && Near(atomic.Position, new Vector3(-2, 0, 2)), "the received legacy pose and its same-packet author frame reconstruct atomically");
        t.True(PresenceSerializer.TryRead(Hex.Bytes("31 52 56 47 03 01 00 00"), 8, out var idle)
            && !idle.WindowPoseContext.HasValue, "an unrelated later packet cannot inherit the preceding author frame");
        foreach (float observerZoom in new[] { .5f, 1, 17, 80 })
        foreach (float observerYaw in new[] { 0f, 180, 270 })
        {
            var observer = new SharedWindowPoseContext { Origin = new Vector3(100, 50, -25), Yaw = observerYaw, Scale = observerZoom };
            t.True(context.ToWorld(relative, out var shared) && Near(shared.Position, new Vector3(-2, 0, 2)),
                "the same packet reconstructs the same physical pose despite a different observer frame");
            t.True(observer.ToWorld(relative, out var wrong) && !Near(wrong.Position, shared.Position),
                "control: substituting that observer frame reproduces a distinct misplaced window");
        }
        relative.Position = new Vector3(float.PositiveInfinity, 0, 0);
        t.True(!context.ToWorld(relative, out _), "a nonfinite relative endpoint cannot become a valid placement");
    }

    private static void Fragmented(Harness t)
    {
        t.Case("shared context119: complete source snapshot retains the unchanged8000-byte envelope bound");
        const int documentedWorst = 7747;
        t.True(documentedWorst <= 8000, "the documented encoded worst case stays inside the unchanged reassembly capacity");
        t.True(PresenceSerializer.MaxSize >= documentedWorst + 257,
            "allocation-only writer buffer retains a complete257-byte spare record beyond the documented worst case");
        foreach (int length in new[] { Packet().Length, documentedWorst, 8000 })
        {
            byte[] snapshot = new byte[length]; Array.Copy(Packet(), snapshot, Packet().Length);
            byte[][] fragments = ExtrasFragments.Encode(snapshot, snapshot.Length, (ulong)length);
            var receiver = new ExtrasFragments(); byte[]? assembled = null;
            for (int index = fragments.Length - 1; index >= 0; index--)
            {
                t.True(fragments[index].Length <= ExtrasFragments.MaxDatagramBytes, "each bounded source snapshot fragment respects the original event budget");
                byte[]? result = receiver.Accept(2, fragments[index], fragments[index].Length, 0);
                if (index > 0) t.True(result == null, "partial source frames never become complete snapshots");
                else assembled = result;
            }
            t.True(assembled != null, "reordered fragments reconstruct the complete source frame snapshot");
            if (assembled != null) t.Wire(snapshot, assembled, assembled.Length, "full snapshot bytes survive unchanged below the original8000-byte cap");
        }
    }
}
