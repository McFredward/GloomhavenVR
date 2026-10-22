using System;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

/// <summary>Independent record120 grammar. The candidate author is the authenticated transport
/// sender; the payload contains its counter and the exact parent(counter,author) only. The
/// nonwrapping shared clock requires a nonzero child counter strictly above its parent.</summary>
internal static class SharedQuestSelectionVersionVectors
{
    private static byte[] MapPacket() => Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 14 10 01 07 44 33 22 11 09 88 77 66 55 00 00 00 00 00");
    private static byte[] VersionRecord() => Hex.Bytes("78 0C 05 06 07 08 01 02 03 04 09 0A 0B 0C");
    private static byte[] VersionPacket()
    {
        byte[] prefix = MapPacket(), record = VersionRecord();
        byte[] packet = new byte[prefix.Length + record.Length];
        Array.Copy(prefix, packet, prefix.Length); Array.Copy(record, 0, packet, prefix.Length, record.Length);
        packet[10] = 2;
        return packet;
    }
    private static PresenceState Map() => new() { HasMapRoom = true, MapRoomFlags = 1,
        MapRoomSurfaceStamp = 7, MapRoomPickKey = 0x11223344, MapRoomSelectStamp = 9, MapRoomSelectKey = 0x55667788 };
    private static PresenceState Versioned()
    {
        PresenceState state = Map(); state.HasMapSelectionVersion = true;
        state.MapSelectionRevision = 0x08070605; state.MapSelectionParent = 0x04030201;
        state.MapSelectionParentAuthor = 0x0C0B0A09; return state;
    }

    internal static void Run(Harness t)
    {
        Literal(t);
        Malformed(t);
        Absence(t);
        Atomic(t);
    }

    private static void Literal(Harness t)
    {
        t.Case("shared quest120: exact candidate and parent version bytes");
        var bytes = new byte[PresenceSerializer.MaxSize];
        int size = PresenceSerializer.Write(Versioned(), bytes);
        t.Wire(VersionPacket(), bytes, size, "120/12 is candidateU32LE, parentU32LE, parentAuthorI32LE after unchanged map20");
        t.Equal(43, size, "map prefix29 plus atomic14-byte version record");
        t.Equal(12, NetProtocol.SharedQuestSelectionVersionBytes, "candidate sender identity is not duplicated inside the12-byte codec payload");
        t.True(PresenceSerializer.TryRead(VersionPacket(), VersionPacket().Length, out var read)
            && read.HasMapRoom && read.HasMapSelectionVersion && read.MapSelectionRevision == 0x08070605
            && read.MapSelectionParent == 0x04030201 && read.MapSelectionParentAuthor == 0x0C0B0A09,
            "independent golden bytes preserve all unsigned counters and the signed parent author");
        var root = Versioned(); root.MapSelectionRevision = 1; root.MapSelectionParent = 0; root.MapSelectionParentAuthor = 0;
        size = PresenceSerializer.Write(root, bytes);
        byte[] expected = MapPacket().Concat(Hex.Bytes("78 0C 01 00 00 00 00 00 00 00 00 00 00 00")).ToArray(); expected[10] = 2;
        t.Wire(expected, bytes, size, "a root proposal explicitly names absent parent(0,0)");
        foreach (uint counter in new uint[] { 2, 255, 256, 0x80000000, uint.MaxValue })
        foreach (int author in new[] { 1, 4, int.MaxValue })
        {
            var state = Versioned(); state.MapSelectionRevision = counter; state.MapSelectionParent = counter - 1;
            state.MapSelectionParentAuthor = author;
            size = PresenceSerializer.Write(state, bytes);
            t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasMapSelectionVersion
                && read.MapSelectionRevision == counter && read.MapSelectionParent == counter - 1
                && read.MapSelectionParentAuthor == author, "nonzero unsigned counter endpoints and positive parent authors survive: " + counter + "/" + author);
        }
    }

    private static void Malformed(Harness t)
    {
        t.Case("shared quest120: malformed ancestry cannot become a shared selection event");
        int recordAt = MapPacket().Length;
        for (int payload = 0; payload < 12; payload++)
        {
            byte[] packet = VersionPacket().Take(recordAt + 2 + payload).ToArray(); packet[recordAt + 1] = (byte)payload;
            t.True(!PresenceSerializer.TryRead(packet, packet.Length, out var read) || !read.HasMapSelectionVersion,
                "truncated version payload cannot supply a partial parent tuple: " + payload);
        }
        for (int length = 0; length < VersionPacket().Length; length++)
            t.True(!PresenceSerializer.TryRead(VersionPacket(), length, out var read) || !read.HasMapSelectionVersion,
                "short enclosing packet cannot publish a complete version: " + length);
        foreach (var fields in new[] {
            Hex.Bytes("00 00 00 00 05 06 07 08 09 0A 0B 0C"), // absent candidate
            Hex.Bytes("01 00 00 00 00 00 00 00 01 00 00 00"), // root with invented parent author
            Hex.Bytes("02 00 00 00 01 00 00 00 00 00 00 00"), // parent counter with absent author
            Hex.Bytes("02 00 00 00 01 00 00 00 FF FF FF FF"), // negative parent author
            Hex.Bytes("02 00 00 00 01 00 00 00 00 00 00 80"), // signed minimum parent author
            Hex.Bytes("01 00 00 00 01 00 00 00 01 00 00 00"), // self-parent counter
            Hex.Bytes("01 00 00 00 02 00 00 00 01 00 00 00"), // backward parent counter
            Hex.Bytes("01 00 00 00 FF FF FF FF 01 00 00 00"), // wrapped child is forbidden
        })
        {
            byte[] packet = VersionPacket(); Array.Copy(fields, 0, packet, recordAt + 2, fields.Length);
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out var read) && read.HasMapRoom
                && !read.HasMapSelectionVersion && read.MapSelectionRevision == 0
                && read.MapSelectionParent == 0 && read.MapSelectionParentAuthor == 0,
                "zero candidate or inconsistent parent tuple is skipped without retaining partial ancestry");
            t.Equal(0x55667788u, read.MapRoomSelectKey, "malformed optional version cannot erase the original selected quest key");
        }
        byte[] unknown = VersionPacket(); unknown[recordAt] = 250;
        t.True(PresenceSerializer.TryRead(unknown, unknown.Length, out var absent) && absent.HasMapRoom
            && !absent.HasMapSelectionVersion, "unknown additive version record keeps the legacy map selection");
        byte[] future = VersionPacket().Concat(new byte[] { 0xCC }).ToArray(); future[recordAt + 1] = 13;
        t.True(PresenceSerializer.TryRead(future, future.Length, out var extended) && extended.HasMapSelectionVersion
            && extended.MapSelectionRevision == 0x08070605 && extended.MapSelectionParentAuthor == 0x0C0B0A09,
            "a future record tail preserves the known12-byte version prefix");

        byte[] standalone = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01").Concat(VersionRecord()).ToArray();
        t.True(PresenceSerializer.TryRead(standalone, standalone.Length, out var noMap) && !noMap.HasMapRoom
            && !noMap.HasMapSelectionVersion && noMap.MapSelectionRevision == 0
            && noMap.MapSelectionParent == 0 && noMap.MapSelectionParentAuthor == 0,
            "ancestry without its same-packet map record is discarded after the full TLV walk");
        byte[] reordered = new byte[VersionPacket().Length];
        Array.Copy(VersionPacket(), reordered, 11); Array.Copy(VersionRecord(), 0, reordered, 11, 14);
        Array.Copy(MapPacket(), 11, reordered, 25, 18);
        t.True(PresenceSerializer.TryRead(reordered, reordered.Length, out var ordered) && ordered.HasMapRoom
            && ordered.HasMapSelectionVersion && ordered.MapSelectionRevision == 0x08070605,
            "same-packet map dependency is checked after parsing rather than relying on record order");
    }

    private static void Absence(Harness t)
    {
        t.Case("shared quest120: absent or invalid writer keeps legacy bytes");
        var bytes = new byte[PresenceSerializer.MaxSize];
        int size = PresenceSerializer.Write(default, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, size, "ordinary idle packet remains exactly8 bytes");
        var notInMap = Versioned(); notInMap.HasMapRoom = false;
        size = PresenceSerializer.Write(notInMap, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, size,
            "invalid version without a map cannot create an empty extension tail");
        foreach (var invalid in new (bool enabled, uint revision, uint parent, int author)[] { (false, 1u, 0u, 0),
            (true, 0u, 0u, 0), (true, 0u, 1u, 1), (true, 1u, 0u, 1),
            (true, 2u, 1u, 0), (true, 2u, 1u, -1), (true, 2u, 1u, int.MinValue),
            (true, 1u, 1u, 1), (true, 1u, 2u, 1), (true, 1u, uint.MaxValue, 1) })
        {
            var state = Map(); state.HasMapSelectionVersion = invalid.enabled;
            state.MapSelectionRevision = invalid.revision; state.MapSelectionParent = invalid.parent;
            state.MapSelectionParentAuthor = invalid.author;
            size = PresenceSerializer.Write(state, bytes);
            t.Wire(MapPacket(), bytes, size, "invalid/disabled ancestry leaves original map20 byte-identical");
        }
        var versioned = Versioned(); size = PresenceSerializer.Write(versioned, bytes);
        byte[] stripped = bytes.Take(size - 14).ToArray(); stripped[10] = 1;
        t.Wire(MapPacket(), stripped, stripped.Length, "stripping additive120 restores the exact old map20 packet");
        t.True(PresenceSerializer.TryRead(MapPacket(), MapPacket().Length, out var read) && read.HasMapRoom
            && !read.HasMapSelectionVersion && read.MapSelectionRevision == 0
            && read.MapSelectionParent == 0 && read.MapSelectionParentAuthor == 0,
            "a later legacy packet cannot inherit ancestry from a preceding versioned snapshot");
    }

    private static void Atomic(Harness t)
    {
        t.Case("shared quest120: content and causal coordinates share one bounded snapshot");
        var bytes = new byte[PresenceSerializer.MaxSize];
        var first = Versioned(); first.MapSelectionRevision = 255; first.MapSelectionParent = 254;
        first.MapSelectionParentAuthor = 4; first.MapRoomSelectStamp = 0;
        var second = first; second.MapSelectionRevision = 256;
        second.MapSelectionParent = 255; second.MapSelectionParentAuthor = 4; second.MapRoomSelectKey = 0x11223344;
        int size1 = PresenceSerializer.Write(first, bytes); byte[] packet1 = bytes.Take(size1).ToArray();
        int size2 = PresenceSerializer.Write(second, bytes); byte[] packet2 = bytes.Take(size2).ToArray();
        t.True(PresenceSerializer.TryRead(packet1, packet1.Length, out var a)
            && PresenceSerializer.TryRead(packet2, packet2.Length, out var b)
            && a.MapRoomSelectStamp == b.MapRoomSelectStamp && a.MapSelectionRevision == 255
            && b.MapSelectionRevision == 256 && b.MapSelectionParent == 255
            && a.MapRoomSelectKey == 0x55667788 && b.MapRoomSelectKey == 0x11223344,
            "distinct causal revisions survive the old byte-stamp alias without mixing quest content");
        var reassembly = new ExtrasFragments();
        byte[][] fragments = ExtrasFragments.Encode(packet2, packet2.Length, 120);
        byte[]? combined = reassembly.Accept(4, fragments[0], fragments[0].Length, 0);
        t.True(combined != null && PresenceSerializer.TryRead(combined, combined.Length, out var parsed)
            && parsed.HasMapSelectionVersion && parsed.MapSelectionRevision == 256
            && parsed.MapSelectionParent == 255 && parsed.MapSelectionParentAuthor == 4
            && parsed.MapRoomSelectKey == 0x11223344,
            "the complete snapshot from transport peer4 retains original content plus exact parent tuple");
        t.True(packet2.Length <= 8000, "map content and causal version remain below the unchanged transport cap");
    }
}
