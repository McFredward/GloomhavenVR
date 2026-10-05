using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownCatalogWarmVectors
{
    internal static void Run(Harness t)
    {
        LiteralHeader(t);
        RejectMalformed(t);
        ExactPropertyPatch(t);
        HeaderBounds(t);
        QueueAssemblyAndFairness(t);
    }

    // Independently specified little-endian row: uint packet length225, original
    // visible1, zero content basis, GVR3/Town19, TLV78 header,85 stamp,86 claim.
    // It includes every external canvas and parent/alpha field, without Nodes.
    private static byte[] LiteralRaw() => Hex.Bytes(@"
        E1 00 00 00 01 00 00 00 00 00 00 00 00 31 52 56 47 03 13 4E C1 02 01 63
        00 00 00 14 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 0C 00 03 00 0B
        00 69 74 65 6D 2E 36 32 32 30 30 7C 04 03 02 01 00 0A 00 07 00 00 00 A4
        70 BD 3E 00 00 A0 3F 00 00 20 40 00 00 00 00 80 3F 00 00 00 40 00 00 40
        40 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F CD CC CC 3D CD CC 4C
        3E 9A 99 99 3E 01 00 00 80 40 00 00 A0 40 00 00 C0 40 00 00 00 00 00 00
        00 00 00 00 00 00 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00
        A0 42 00 00 F0 42 00 00 00 3F 00 00 00 3F 00 00 C8 42 00 00 00 00 00 00
        00 00 00 00 80 3F 00 00 00 00 FD FF FF FF 00 00 00 00 00 00 00 00 55 0E
        02 0A 00 00 00 04 00 00 00 00 CD CC 0C 3F 56 06 01 02 03 00 00 00
        ");

    private static void LiteralHeader(Harness t)
    {
        t.Case("Independent additive103 author headers preserve the complete original metadata");
        var root = HeaderRoot();
        byte[] bankOnly = TownServiceCodec.Write(root);
        root.CatalogBank!.Headers = new[] { Header(12) }; root.CatalogBank.HeaderBaseKeys = new ulong[1];
        byte[] payload = root.CatalogBank.WriteHeaders(root), expected = LiteralRaw();
        byte[] inflated = Inflate(payload);
        t.Wire(expected, inflated, inflated.Length, "independent literal includes parent alpha, sample/age, pose, external canvas and rack alpha");
        t.Wire(Hex.Bytes("4A B6 08 2B"), payload[^4..], 4, "independent CRC32 literal of the238-byte author row");
        byte[] full = TownServiceCodec.Write(root);
        t.Wire(bankOnly, full, bankOnly.Length, "unchanged102 v1 bytes precede additive103");
        // A hand-authored RFC1951 stored block makes the decoder literal
        // independent of the production optimal compressor and its version.
        byte[] literal = StoredPayload(expected, 1);
        t.Wire(Hex.Bytes("01 01 00 EE 00 00 00 F7 00 00 00 01 EE 00 11 FF"), literal[..16], 16,
            "103 v1/count/raw238/packed247/stored block lengths have independent literal bytes");
        byte[] independentPacket = Append(bankOnly, literal);
        t.True(TownServiceCodec.TryRead(independentPacket, independentPacket.Length, out var decoded),
            "reader admits an independently authored compressed103 row");
        if (decoded == null) return;
        var received = decoded.CatalogBank!.Headers[0];
        t.Equal(.37f, received.ParentAlpha, "current parent alpha is authored");
        t.Equal(.55f, received.RackMember!.Alpha, "current native child alpha is authored");
        t.Equal(-3, received.CanvasSortingOrder, "original external canvas sorting is complete");
        t.Equal(6f, received.CanvasPose[2], "current external canvas world pose is complete");
        t.Equal(100f, received.CanvasSettings[0], "original pixel scaling remains complete");
        t.True(received.Visible && received.HasCanvasFrame && received.BaseSequence == 0 && received.Nodes.Length == 0,
            "visibility is restored explicitly without fabricated original node values");
    }

    private static void RejectMalformed(Harness t)
    {
        t.Case("103 rejects malformed, recursive, reordered or foreign rows before any original admission");
        var root = HeaderRoot(); byte[] legacy = TownServiceCodec.Write(root), raw = LiteralRaw();
        byte[] valid = StoredPayload(raw, 1);
        void Reject(string name, byte[] payload)
        {
            byte[] packet = Append(legacy, payload);
            t.True(!TownServiceCodec.TryRead(packet, packet.Length, out _), name);
        }
        byte[] Change(int at, byte value) { byte[] changed = (byte[])valid.Clone(); changed[at] = value; return changed; }
        Reject("unknown103 version", Change(0, 2));
        Reject("missing member header", Change(1, 0));
        Reject("header count differs from102 refs", Change(1, 2));
        Reject("declared raw header bomb", With32(valid, 3, TownCatalogBank.MaxRawHeaderBytes + 1));
        Reject("packed header bound", With32(valid, 7, TownCatalogBank.MaxPackedHeaderBytes + 1));
        Reject("declared raw length cannot understate inflation", With32(valid, 3, raw.Length - 1));
        Reject("corrupt header CRC", Change(valid.Length - 1, (byte)(valid[^1] ^ 1)));
        Reject("truncated deflate/CRC", valid[..^1]);
        for (int length = 0; length < raw.Length; length += 11)
            Reject("truncated author row" + length, StoredPayload(raw[..length], 1));
        byte[] ChangedRaw(int at, byte value) { var copy = (byte[])raw.Clone(); copy[at] = value; return copy; }
        Reject("invalid original visible bit", StoredPayload(ChangedRaw(4, 2), 1));
        Reject("foreign module header", StoredPayload(ChangedRaw(41, 13), 1));
        Reject("foreign session header", StoredPayload(ChangedRaw(23, 98), 1));
        Reject("foreign service header", StoredPayload(ChangedRaw(22, 2), 1));
        Reject("foreign public claim", StoredPayload(ChangedRaw(raw.Length - 4, 4), 1));
        var nonfinite = (byte[])raw.Clone(); Array.Copy(Hex.Bytes("00 00 C0 7F"), 0, nonfinite, 80, 4);
        Reject("nonfinite original pose", StoredPayload(nonfinite, 1));
        var basis = (byte[])raw.Clone(); basis[5] = 1;
        Reject("a property basis requires an explicit source BaseSequence", StoredPayload(basis, 1));
        byte[] child = raw[13..];
        foreach (byte record in new byte[] { 102, 103 })
        {
            byte[] nested = child.Concat(new byte[] { record, 1, 1 }).ToArray();
            Reject("nested" + record + " rejected before nested parsing/inflation", StoredPayload(Row(nested, true, 0), 1));
        }
        Reject("trailing decoded row data", StoredPayload(raw.Concat(new byte[] { 0 }).ToArray(), 1));
        var two = HeaderRoot(); two.Rack!.Members = new[] { new TownRackMember(12, 0, false), new TownRackMember(13, 0, false) };
        two.CatalogBank!.Members = new[] { new TownCatalogBankMember(12, 1), new TownCatalogBankMember(13, 2) };
        byte[] first = (byte[])child.Clone(), second = (byte[])child.Clone(); second[28] = 13;
        byte[] pair = Row(second, true, 0).Concat(Row(first, true, 0)).ToArray(), prefix = TownServiceCodec.Write(two);
        byte[] reversed = Append(prefix, StoredPayload(pair, 2));
        t.True(!TownServiceCodec.TryRead(reversed, reversed.Length, out _), "row ordering must exactly match the sorted immutable bank refs");
        byte[] noBank = TownServiceCodec.Write(new TownServiceFrame { PublicCatalog = true, PublicClaim = 3, Service = 1, Session = 99, Sequence = 1,
            Module = 10, Template = 2, Visible = false, Pose = IdentityPose() });
        byte[] orphan = Append(noBank, valid);
        t.True(!TownServiceCodec.TryRead(orphan, orphan.Length, out _), "103 requires a complete matching102 bank");
    }

    private static void ExactPropertyPatch(Harness t)
    {
        t.Case("Author headers carry exact native property changes against an explicit genuine original key");
        TownServiceFrame before = Header(12); before.Nodes = new[] { new TownServiceNode { Binding = 7 } };
        before.Nodes[0].Values.Add(TownServiceProperty.Canvas, new TownServiceValue { Numbers = new[] { 0f, 100f, 1f } });
        before.Nodes[0].Values.Add(TownServiceProperty.Mesh, new TownServiceValue { Numbers = new[] { 1f, 1f, .2f, .3f } });
        before.Nodes[0].Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
        var after = TownServiceDelta.Copy(before); after.Sequence++; after.Nodes[0].Values[TownServiceProperty.Canvas].Numbers[0] = 1f;
        after.Nodes[0].Values[TownServiceProperty.Mesh].Numbers[1] = 0f; after.Pose[0] = 7f; after.ParentAlpha = .8f;
        var root = HeaderRoot(); root.CatalogBank!.Updates = new[] { after };
        root.CatalogBank.Members[0] = new(12, TownCatalogBank.ContentKey(after));
        var reference = TownCatalogClock.Create(root, new Dictionary<ushort, TownServiceFrame> { [12] = before });
        t.Equal(TownCatalogBank.ContentKey(before), reference.CatalogBank!.HeaderBaseKeys[0], "explicit content basis names the exact complete captured original");
        var patch = reference.CatalogBank.Headers[0];
        t.True(patch.Nodes.Length == 1 && patch.Nodes[0].Values.Count == 2 && !patch.Nodes[0].Values.ContainsKey(TownServiceProperty.Active),
            "existing cumulative delta semantics transmit only actual changed original properties");
        t.Equal(before.Sequence, patch.BaseSequence, "source delta declares its real complete baseline sequence");
        byte[] bytes = TownServiceCodec.Write(reference);
        t.True(TownServiceCodec.TryRead(bytes, bytes.Length, out var wire), "bounded exact property row admits through additive103");
        if (wire == null) return;
        TownServiceFrame expanded = TownServiceDelta.Expand(before, wire.CatalogBank!.Headers[0])!;
        t.Equal(TownCatalogBank.ContentKey(after), TownCatalogBank.ContentKey(expanded), "reconstructed exact nodes match the authoritative unchanged102 target key");
        t.Equal(7f, expanded.Pose[0], "new owner pose never comes from stale immutable node storage");
        t.Equal(.8f, expanded.ParentAlpha, "new owner parent alpha never comes from stale node storage");
        t.Equal(1f, expanded.Nodes[0].Values[TownServiceProperty.Active].Numbers[0], "unchanged original properties survive exactly");
        var huge = TownServiceDelta.Copy(reference);
        huge.CatalogBank!.Headers[0].Nodes[0].Values[TownServiceProperty.TmpText] = new TownServiceValue { Text = new[] { new string('a', TownServiceFrame.MaxBytes) } };
        t.True(Throws(() => TownServiceCodec.Write(huge)), "oversize native patch fails explicitly and cannot become partial ready data");
    }

    private static void HeaderBounds(Harness t)
    {
        t.Case("103 writer enforces independent raw and packed bounds without exposing a partial table");
        TownServiceFrame Table(int count, int chars, bool noise)
        {
            var root = HeaderRoot(); var headers = new TownServiceFrame[count];
            for (int i = 0; i < count; i++)
            {
                var header = Header((ushort)(12 + i)); header.BaseSequence = 1;
                header.Nodes = new[] { new TownServiceNode { Binding = 7 } };
                string text = noise ? RandomText(chars, 622 + i) : new string('a', chars);
                header.Nodes[0].Values.Add(TownServiceProperty.TmpText, new TownServiceValue {
                    Text = Enumerable.Range(0, (chars + 16383) / 16384).Select(n => text.Substring(n * 16384, Math.Min(16384, chars - n * 16384))).ToArray() });
                headers[i] = header;
            }
            root.Rack!.Members = headers.Select(h => new TownRackMember(h.Module, 0, false)).ToArray();
            root.CatalogBank!.Members = headers.Select(h => new TownCatalogBankMember(h.Module, 123)).ToArray();
            root.CatalogBank.Headers = headers; root.CatalogBank.HeaderBaseKeys = Enumerable.Repeat(456UL, count).ToArray(); return root;
        }
        var nearPacked = Table(1, 18500, true); byte[] valid = nearPacked.CatalogBank!.WriteHeaders(nearPacked);
        t.True(BitConverter.ToUInt32(valid, 7) > TownCatalogBank.MaxPackedHeaderBytes * .9,
            "genuine valid header packet exercises over90percent of the packed bound");
        byte[] wire = TownServiceCodec.Write(nearPacked);
        t.True(TownServiceCodec.TryRead(wire, wire.Length, out _), "valid near-cap exact patch table roundtrips");
        var overPacked = Table(1, 22000, true);
        t.True(Throws(() => overPacked.CatalogBank!.WriteHeaders(overPacked)), "incompressible oversize headers fail explicitly");
        var nearRaw = Table(11, 32700, false); valid = nearRaw.CatalogBank!.WriteHeaders(nearRaw);
        t.True(BitConverter.ToUInt32(valid, 3) > TownCatalogBank.MaxRawHeaderBytes * .9, "valid original rows exercise over90percent of the raw bound");
        wire = TownServiceCodec.Write(nearRaw);
        t.True(TownServiceCodec.TryRead(wire, wire.Length, out _), "bounded large exact original patch table is admitted atomically");
        var overRaw = Table(12, 32700, false);
        t.True(Throws(() => overRaw.CatalogBank!.WriteHeaders(overRaw)), "a highly compressible raw oversize table still fails before compression");
    }

    private static void QueueAssemblyAndFairness(Harness t)
    {
        t.Case("Actual lane arbitration finishes a fragmented clock, preserves background turns and full loss repair");
        var root = HeaderRoot(); var member = Header(12); member.Nodes = new[] { new TownServiceNode { Binding = 7 } };
        member.Nodes[0].Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Text = new[] { RandomText(14000, 1) } });
        root.Nodes = new[] { new TownServiceNode { Binding = 1 } };
        root.Nodes[0].Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Text = new[] { RandomText(5500, 2) } });
        root.CatalogBank!.Updates = new[] { member }; root.CatalogBank.Members[0] = new(12, TownCatalogBank.ContentKey(member));
        root.HighPriority = true;
        var background = new TownServiceFrame { Service = 1, Session = 99, PublicCatalog = true, PublicClaim = 3,
            Sequence = 1, Module = 40, Template = 1, Visible = true, Pose = IdentityPose(), Nodes = new[] { new TownServiceNode { Binding = 1 } } };
        background.Nodes[0].Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Text = new[] { RandomText(16000, 3) } });
        foreach (bool backgroundAtStart in new[] { false, true })
        {
            var queue = new TownServiceLaneSendQueue(65536); var receiver = new TownServiceFragments();
            byte[] complete = TownServiceCodec.Write(root); queue.Enqueue(complete, complete.Length, root);
            byte[] bg = TownServiceCodec.Write(background);
            if (backgroundAtStart) queue.Enqueue(bg, bg.Length, background);
            int clockPages = 0, fullPages = 0, bgPages = 0, warmAt = -1, fullAt = -1, bgAt = -1;
            int previousClock = -1, worstClockGap = 0;
            for (int tick = 0; tick < 600 && (warmAt < 0 || fullAt < 0 || bgAt < 0); tick++)
            {
                if (!backgroundAtStart && tick == 8) queue.Enqueue(bg, bg.Length, background);
                double now = tick / 20d + tick * 1e-9; byte[]? page = queue.Next(now); if (page == null) continue;
                int stream = TownServiceFragments.Stream(page, page.Length);
                if (stream == root.Module) { clockPages++; if (previousClock >= 0) worstClockGap = Math.Max(worstClockGap, tick - previousClock); previousClock = tick; }
                else if (stream == TownServiceFrame.BundleStream) bgPages++; else fullPages++;
                byte[]? done = receiver.Accept(2, page, page.Length, now); if (done == null) continue;
                byte[][] frames = TownServiceCodec.TryReadBundle(done, done.Length, out var parts) ? parts! : new[] { done };
                foreach (byte[] packet in frames)
                {
                    if (!TownServiceCodec.TryRead(packet, packet.Length, out var frame)) throw new Exception("Actual completed queue packet is invalid.");
                    if (frame!.Module == root.Module) { if (frame.CatalogBank!.Updates.Length == 0) warmAt = tick; else fullAt = tick; }
                    else if (frame.Module == background.Module) bgAt = tick;
                }
            }
            t.True(clockPages > 2 && warmAt >= 0 && warmAt < fullAt, "a multi-fragment clock completes before retained full repair with background=" + backgroundAtStart);
            t.True(worstClockGap <= 2 && bgPages > 0 && bgAt >= 0 && fullPages > 0, "every third background share survives while urgent clock assembly finishes");
            t.True(fullAt < 600 && bgAt < 600, "complete repair and background both assemble inside the unchanged120-second lifetime");
        }
    }

    private static TownServiceFrame HeaderRoot()
    {
        var root = new TownServiceFrame { PublicCatalog = true, PublicClaim = 3, Service = 1, Session = 99, Sequence = 30,
            Module = 10, Template = 2, TemplateAddress = "merchant.rack|original", Structure = 3, Visible = true, Pose = IdentityPose(),
            Rack = new TownRackState { Cassette = true, Crank = 11, Turn = 4, Members = new[] { new TownRackMember(12, 0, false) } },
            Nodes = new[] { new TownServiceNode { Binding = 1 } } };
        root.CatalogBank = new TownCatalogBank { Prepared = true, Members = new[] { new TownCatalogBankMember(12, 0x0123456789ABCDEFUL) } };
        return root;
    }
    private static TownServiceFrame Header(ushort id) => new()
    {
        PublicCatalog = true, PublicClaim = 3, Service = 1, Session = 99, Sequence = 20, Module = id,
        Template = 3, TemplateAddress = "item.62200|", Structure = 0x01020304, Visible = true,
        ParentModule = 10, ParentBinding = 7, ParentAlpha = .37f, SampleTime = 1.25f, SessionAge = 2.5f,
        Pose = new[] { 1f, 2f, 3f, 0f, 0f, 0f, 1f, .1f, .2f, .3f }, HasCanvasFrame = true,
        CanvasPose = new[] { 4f, 5f, 6f, 0f, 0f, 0f, 1f, 1f, 1f, 1f }, CanvasRect = new[] { 80f, 120f, .5f, .5f },
        CanvasSettings = new[] { 100f, 0f, 0f, 1f, 0f }, CanvasSortingOrder = -3,
        RackMember = new TownRackStamp { Rack = 10, Turn = 4, Alpha = .55f }
    };
    private static float[] IdentityPose() => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f };
    private static byte[] Row(byte[] packet, bool visible, ulong key)
    { using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes); writer.Write((uint)packet.Length); writer.Write(visible); writer.Write(key); writer.Write(packet); return bytes.ToArray(); }
    private static byte[] StoredPayload(byte[] raw, ushort count)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        writer.Write((byte)1); writer.Write(count); writer.Write((uint)raw.Length); writer.Write((uint)(raw.Length + 9));
        writer.Write((byte)1); writer.Write((ushort)raw.Length); writer.Write((ushort)~raw.Length); writer.Write(raw);
        uint crc = uint.MaxValue; foreach (byte value in raw) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
        writer.Write(~crc); return bytes.ToArray();
    }
    private static byte[] Inflate(byte[] payload)
    { using var bytes = new MemoryStream(payload, 11, payload.Length - 15); using var deflate = new DeflateStream(bytes, CompressionMode.Decompress); using var raw = new MemoryStream(); deflate.CopyTo(raw); return raw.ToArray(); }
    private static byte[] Append(byte[] prefix, byte[] payload)
    { using var bytes = new MemoryStream(); bytes.Write(prefix, 0, prefix.Length); for (int at = 0; at < payload.Length;) { int n = Math.Min(255, payload.Length - at); bytes.WriteByte(103); bytes.WriteByte((byte)n); bytes.Write(payload, at, n); at += n; } return bytes.ToArray(); }
    private static byte[] With32(byte[] input, int at, int value)
    { var copy = (byte[])input.Clone(); for (int i = 0; i < 4; i++) copy[at + i] = (byte)(value >> (8 * i)); return copy; }
    private static bool Throws(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    private static string RandomText(int size, int seed) { var random = new Random(seed); return new string(Enumerable.Range(0, size).Select(_ => (char)random.Next(32, 127)).ToArray()); }
}
