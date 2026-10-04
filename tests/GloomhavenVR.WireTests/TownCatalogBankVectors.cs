using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.WireTests;

internal static class TownCatalogBankVectors
{
    internal static void Run(Harness t)
    {
        LiteralReferencesAndContent(t);
        CompleteOriginalsAndDelta(t);
        RejectMalformedAndForeign(t);
        Bounds(t);
        FragmentAtomicityAndHeartbeat(t);
    }

    private static void LiteralReferencesAndContent(Harness t)
    {
        t.Case("TLV102 references are additive and original content has a canonical literal key");
        TownServiceFrame root = Root();
        root.Rack!.Members = new[] { new TownRackMember(12, 0, false), new TownRackMember(19, 0, false) };
        byte[] legacy = TownServiceCodec.Write(root);
        root.CatalogBank = new TownCatalogBank { Prepared = true, Members = new[] {
            new TownCatalogBankMember(12, 0x0102030405060708UL), new TownCatalogBankMember(19, 0x8877665544332211UL) } };
        byte[] packet = TownServiceCodec.Write(root);
        // Independent reference-only grammar: no inflater or child codec participates in this literal.
        byte[] golden = Hex.Bytes("66 22 01 01 02 00 0C 00 08 07 06 05 04 03 02 01 13 00 11 22 33 44 55 66 77 88 00 00 00 00 00 00 00 00 00 00");
        t.Wire(legacy, packet, legacy.Length, "all previous module/rack/mechanism bytes remain unchanged");
        t.Wire(golden, packet.Skip(legacy.Length).ToArray(), packet.Length - legacy.Length,
            "independent102 version/ready/ushort IDs/64-bit keys/empty update lengths");
        byte[] independentlyAuthored = legacy.Concat(golden).ToArray();
        t.True(TownServiceCodec.TryRead(independentlyAuthored, independentlyAuthored.Length, out var received)
            && received!.CatalogBank!.Prepared && received.CatalogBank.Members[1].ContentKey == 0x8877665544332211UL,
            "receiver accepts the independent literal bank");
        root.CatalogBank.Prepared = false;
        packet = TownServiceCodec.Write(root);
        t.True(TownServiceCodec.TryRead(packet, packet.Length, out received) && !received!.CatalogBank!.Prepared,
            "unprepared is an explicit state, never an implicit ready acknowledgement");

        TownServiceFrame original = Original(12);
        // SHA256 of the independently specified little-endian content grammar begins
        // da70fd5b833afb21; it includes the original canvas/binding and logical node values.
        const ulong key = 0x21FB3A835BFD70DAUL;
        t.Equal(key, TownCatalogBank.ContentKey(original), "independent SHA256 content-key literal");
        TownServiceFrame moved = TownServiceDelta.Copy(original);
        moved.Visible = false; moved.Pose[0] = 8f; moved.Sequence = 22; moved.SampleTime = 7f;
        moved.SessionAge = 9f; moved.PublicClaim = 99; moved.Session = 17; moved.Module = 20;
        moved.ParentAlpha = .4f; moved.CanvasPose[0] = 6f;
        moved.RackMember = new TownRackStamp { Rack = 9, Page = 257, Turn = 4, Alpha = .1f };
        t.Equal(key, TownCatalogBank.ContentKey(moved), "pose/page/clock/epoch/lane headers do not invalidate original content");
        var reordered = new TownServiceNode { Binding = 7 };
        foreach (var pair in original.Nodes[0].Values.Reverse()) reordered.Values.Add(pair.Key, pair.Value);
        moved = TownServiceDelta.Copy(original); moved.Nodes = new[] { reordered };
        t.Equal(key, TownCatalogBank.ContentKey(moved), "dictionary insertion order cannot change canonical original properties");
        foreach (ushort property in new[] { TownServiceProperty.Active, TownServiceProperty.Renderer, TownServiceProperty.TmpText })
        {
            moved = TownServiceDelta.Copy(original); moved.Nodes[0].Values[property].Numbers[0] += .25f;
            t.True(TownCatalogBank.ContentKey(moved) != key, "native property remains immutable content: " + property);
        }
        moved = TownServiceDelta.Copy(original); moved.Nodes[0].Values[TownServiceProperty.TmpText].Text[0] += "!";
        t.True(TownCatalogBank.ContentKey(moved) != key, "original face/label text participates in content identity");
        moved = TownServiceDelta.Copy(original); moved.Template++;
        t.True(TownCatalogBank.ContentKey(moved) != key, "template identity participates in content identity");
        moved = TownServiceDelta.Copy(original); moved.TemplateAddress += "changed";
        t.True(TownCatalogBank.ContentKey(moved) != key, "native template address participates in content identity");
        moved = TownServiceDelta.Copy(original); moved.Structure++;
        t.True(TownCatalogBank.ContentKey(moved) != key, "original binding structure participates in content identity");
        foreach (Action<TownServiceFrame> mutate in new Action<TownServiceFrame>[] {
            f => f.HasCanvasFrame = true, f => f.CanvasRect[0]++, f => f.CanvasRect[2] += .1f,
            f => f.CanvasSettings[0]++, f => f.CanvasSettings[4]++, f => f.CanvasSortingOrder++,
            f => f.CanvasSortingLayer++, f => { f.ParentModule = 10; f.ParentBinding = 7; } })
        {
            moved = TownServiceDelta.Copy(original); mutate(moved);
            t.True(TownCatalogBank.ContentKey(moved) != key, "external clipping/softness/sorting and parent binding remain original content");
        }
        moved = TownServiceDelta.Copy(original); moved.ParentModule = 10; moved.ParentBinding = 7;
        ulong parentKey = TownCatalogBank.ContentKey(moved); moved.ParentModule = 11;
        t.Equal(parentKey, TownCatalogBank.ContentKey(moved), "session-specific parent ID is resolved by original binding provenance");
        var signedZero = Original(12);
        var positive = new TownServiceNode { Binding = 8 };
        positive.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 0f } });
        var negative = new TownServiceNode { Binding = 9 };
        negative.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { BitConverter.Int32BitsToSingle(int.MinValue) } });
        signedZero.Nodes = signedZero.Nodes.Concat(new[] { positive, negative }).ToArray();
        var zeroBank = WithUpdates(signedZero);
        byte[] zeroPacket = TownServiceCodec.Write(zeroBank);
        t.True(TownServiceCodec.TryRead(zeroPacket, zeroPacket.Length, out var zeros)
            && zeros!.CatalogBank!.Updates[0].Nodes.Length == 3,
            "numeric signed-zero pooling cannot reject a complete original bank after decoding");
    }

    private static void CompleteOriginalsAndDelta(Harness t)
    {
        t.Case("A clock retains complete changed original modules and deep-copied cumulative bank headers");
        TownServiceFrame root = WithUpdates(Original(12), Original(19));
        root.CatalogBank!.Updates[1].ParentModule = 12;
        root.CatalogBank.Updates[1].ParentBinding = 7;
        root.CatalogBank.Members[1] = new(19, TownCatalogBank.ContentKey(root.CatalogBank.Updates[1]));
        byte[] packet = TownServiceCodec.Write(root);
        t.True(TownServiceCodec.TryRead(packet, packet.Length, out var received), "entire original bank admits together");
        t.Equal(2, received!.CatalogBank!.Updates.Length, "both original snapshots arrive inside one rack packet");
        for (int i = 0; i < root.CatalogBank.Updates.Length; i++)
        {
            byte[] expected = TownServiceCodec.Write(root.CatalogBank.Updates[i]);
            byte[] actual = TownServiceCodec.Write(received.CatalogBank.Updates[i]);
            t.Wire(expected, actual, actual.Length, "complete native module byte grammar survives inner compression");
        }
        TownServiceFrame copy = TownServiceDelta.Copy(root);
        object block = CachedBlock(root.CatalogBank)!;
        t.True(block != null && ReferenceEquals(block, CachedBlock(copy.CatalogBank!)),
            "copy retains the same private immutable encoded block");
        copy.Rack!.Elapsed = .2f;
        TownServiceCodec.Write(copy);
        t.True(ReferenceEquals(block, CachedBlock(copy.CatalogBank!)), "clock-only heartbeat does not recompress original content");
        copy.CatalogBank!.Updates[0].Pose[0] += .1f;
        byte[] movedPacket = TownServiceCodec.Write(copy);
        t.True(!ReferenceEquals(block, CachedBlock(copy.CatalogBank!)), "changed original pose invalidates encoded bytes even with the same content key");
        t.True(TownServiceCodec.TryRead(movedPacket, movedPacket.Length, out var movedOriginal)
            && movedOriginal!.CatalogBank!.Updates[0].Pose[0] == .1f,
            "encoded cache cannot replay an older original pose under an unchanged content key");
        copy.CatalogBank!.Updates[0].Nodes[0].Values[TownServiceProperty.TmpText].Text[0] = "changed copy";
        t.Equal("Original Ä shield", root.CatalogBank.Updates[0].Nodes[0].Values[TownServiceProperty.TmpText].Text[0],
            "bank copy owns nested original value arrays");
        TownServiceFrame retained = TownServiceDelta.Retain(root);
        t.True(ReferenceEquals(retained.CatalogBank!.Updates[0].Nodes[0], root.CatalogBank.Updates[0].Nodes[0])
            && !ReferenceEquals(retained.CatalogBank.Updates[0], root.CatalogBank.Updates[0]),
            "frequent clock header retains published immutable nodes with independent original headers");
        retained.CatalogBank.Updates[0].Pose[0] = 2f;
        t.Equal(0f, root.CatalogBank.Updates[0].Pose[0], "retained bank cannot mutate the original pose header");
        TownServiceFrame cold = WithUpdates(Original(12));
        TownServiceFrame firstCopy = TownServiceDelta.Retain(cold);
        TownServiceCodec.Write(firstCopy);
        t.True(CachedBlock(cold.CatalogBank!) != null && ReferenceEquals(CachedBlock(cold.CatalogBank!), CachedBlock(firstCopy.CatalogBank!)),
            "first encoding through a retained clock also populates the original immutable bank cache");
        TownServiceFrame next = TownServiceDelta.Copy(root); next.Sequence++;
        next.Rack!.Elapsed = .4f;
        TownServiceFrame delta = TownServiceDelta.Create(root, next)!;
        t.True(delta.BaseSequence == root.Sequence && delta.CatalogBank!.Updates.Length == 2,
            "cumulative rack delta preserves atomic changed originals");
        packet = TownServiceCodec.Write(delta);
        t.True(TownServiceCodec.TryRead(packet, packet.Length, out var wireDelta), "atomic bank can ride the existing root delta grammar");
        TownServiceFrame expanded = TownServiceDelta.Expand(root, wireDelta!)!;
        t.True(expanded.CatalogBank!.Prepared && expanded.CatalogBank.Updates.Length == 2
            && expanded.Rack!.Elapsed == .4f, "real delta expansion preserves the bank and current rack clock");
        // A bank is a required-original envelope, not a relaxation of existing bundle limits.
        t.Equal(32, TownServiceCodec.MaxBundleFrames, "existing bundle maximum remains32");
        byte[] child = TownServiceCodec.Write(root.CatalogBank.Updates[0]);
        t.True(Throws(() => TownServiceCodec.WriteBundle(Enumerable.Repeat(child, 33).ToArray())),
            "33 ordinary bundled frames still reject");
    }

    private static void RejectMalformedAndForeign(Harness t)
    {
        t.Case("Malformed or foreign originals never expose a partial catalog bank");
        TownServiceFrame root = WithUpdates(Original(12), Original(19));
        byte[] legacy = WithoutBank(root), payload = root.CatalogBank!.Write(root);
        int updatesAt = 4 + root.CatalogBank.Members.Length * 10;
        var mutations = new List<(string, byte[])>();
        byte[] Change(int at, byte value) { var result = (byte[])payload.Clone(); result[at] = value; return result; }
        mutations.Add(("unknown bank version", Change(0, 2)));
        mutations.Add(("invalid prepared bit", Change(1, 2)));
        mutations.Add(("wrong reference ID", Change(4, 13)));
        mutations.Add(("duplicate references", Change(14, 12)));
        var reversed = (byte[])payload.Clone();
        Array.Copy(payload, 4, reversed, 14, 10); Array.Copy(payload, 14, reversed, 4, 10);
        mutations.Add(("reordered references", reversed));
        mutations.Add(("bad original key", Change(6, (byte)(payload[6] ^ 1))));
        mutations.Add(("too many updates", Change(updatesAt, 3)));
        var oversize = (byte[])payload.Clone(); Put32(oversize, updatesAt + 2, TownCatalogBank.MaxRawUpdateBytes + 1);
        mutations.Add(("declared decompression bomb", oversize));
        var understated = (byte[])payload.Clone(); Put32(understated, updatesAt + 2, BitConverter.ToInt32(payload, updatesAt + 2) - 1);
        mutations.Add(("inflation exceeds declared length", understated));
        var overpacked = (byte[])payload.Clone(); Put32(overpacked, updatesAt + 6, TownCatalogBank.MaxPackedUpdateBytes + 1);
        mutations.Add(("declared packed overflow", overpacked));
        var badChecksum = (byte[])payload.Clone(); badChecksum[^1] ^= 1;
        mutations.Add(("corrupt compressed update checksum", badChecksum));
        byte[] bomb = IndependentPayload(root.CatalogBank, new[] { new byte[TownCatalogBank.MaxRawUpdateBytes * 2] });
        Put32(bomb, updatesAt + 2, TownCatalogBank.MaxRawUpdateBytes);
        mutations.Add(("compressed output exceeds the fixed maximum buffer", bomb));
        mutations.Add(("truncated compressed updates", payload[..^1]));
        mutations.Add(("trailing compressed bytes", payload.Concat(new byte[] { 0 }).ToArray()));
        foreach ((string reason, byte[] bytes) in mutations)
        {
            byte[] bad = Append(legacy, bytes);
            t.True(!TownServiceCodec.TryRead(bad, bad.Length, out var rejected) && rejected == null, reason + " is wholly inert");
        }
        byte[] valid = TownServiceCodec.Write(root);
        for (int at = legacy.Length + 1; at < valid.Length; at++)
            t.True(!TownServiceCodec.TryRead(valid, at, out _), "every truncated atomic rack packet rejects: " + at);

        foreach (Action<TownServiceFrame> mutate in new Action<TownServiceFrame>[] {
            f => f.Session++, f => f.PublicClaim++, f => { f.PublicCatalog = false; f.PublicClaim = 0; },
            f => { f.Service = 2; f.PublicCatalog = false; f.PublicClaim = 0; }, f => f.BaseSequence = 1,
            f => { f.ParentModule = 41; f.ParentBinding = 7; },
            f => f.RackMember!.Rack++, f => f.RackMember!.Page++, f => f.RackMember!.Turn++,
            f => f.RackMember!.Detached = true })
        {
            TownServiceFrame changed = TownServiceDelta.Copy(root.CatalogBank.Updates[1]); mutate(changed);
            // Keep the unchanged first original valid. The second must invalidate the entire bank.
            byte[] foreign;
            try { foreign = TownServiceCodec.Write(changed); }
            catch (InvalidDataException)
            {
                t.True(Throws(() => { var b = root.CatalogBank.Copy(); b.Updates[1] = changed; b.Write(root); }),
                    "writer rejects an invalid original lane/header");
                continue;
            }
            byte[] bad = Append(legacy, IndependentPayload(root.CatalogBank, new[] {
                TownServiceCodec.Write(root.CatalogBank.Updates[0]), foreign }));
            t.True(!TownServiceCodec.TryRead(bad, bad.Length, out var rejected) && rejected == null,
                "foreign original lane/session/claim/stamp/parent or sparse baseline rejects the whole bank");
        }
        byte[] one = TownServiceCodec.Write(root.CatalogBank.Updates[0]);
        byte[] two = TownServiceCodec.Write(root.CatalogBank.Updates[1]);
        foreach (byte[][] children in new[] { new[] { two, one }, new[] { one, one }, new[] { one, two.Concat(new byte[] { 0 }).ToArray() } })
        {
            byte[] bad = Append(legacy, IndependentPayload(root.CatalogBank, children));
            t.True(!TownServiceCodec.TryRead(bad, bad.Length, out var rejected) && rejected == null,
                "reordered/duplicate/truncated original grammar admits no valid first child");
        }
        byte[] nonfinite = (byte[])two.Clone();
        int number = Find(nonfinite, Hex.Bytes("00 00 10 40")); // Unique2.25 original TMP value.
        t.True(number >= 0, "non-finite control binds the actual original property bytes");
        Array.Copy(Hex.Bytes("00 00 C0 7F"), 0, nonfinite, number, 4);
        byte[] invalidNumber = Append(legacy, IndependentPayload(root.CatalogBank, new[] { one, nonfinite }));
        t.True(!TownServiceCodec.TryRead(invalidNumber, invalidNumber.Length, out _), "non-finite nested original property rejects");

        // The production nested entry point refuses102 at the TLV walk, before any
        // bank inflater. This remains true even for an otherwise valid bank root.
        t.True(!TownServiceCodec.TryReadCore(valid, valid.Length, out _, allowBank: false), "explicit nested parser disallows a valid102 bank");
        byte[] nested = Append(two, new byte[] { 1 });
        byte[] recursive = Append(legacy, IndependentPayload(root.CatalogBank, new[] { one, nested }));
        t.True(!TownServiceCodec.TryRead(recursive, recursive.Length, out _), "nested102 is rejected before recursive bank decode");
        var cycle = root.CatalogBank.Copy();
        cycle.Updates[0].ParentModule = 19; cycle.Updates[0].ParentBinding = 7;
        cycle.Updates[1].ParentModule = 12; cycle.Updates[1].ParentBinding = 7;
        cycle.Members[0] = new(12, TownCatalogBank.ContentKey(cycle.Updates[0]));
        cycle.Members[1] = new(19, TownCatalogBank.ContentKey(cycle.Updates[1]));
        t.True(Throws(() => cycle.Write(root)), "new original hierarchy cannot contain a cycle");
        var detached = TownServiceDelta.Copy(root); detached.Rack!.Members[1] = new TownRackMember(19, 0, true);
        t.True(Throws(() => TownServiceCodec.Write(detached)), "a changed bank slot cannot claim an actually detached held module");
        var privateRoot = TownServiceDelta.Copy(root); privateRoot.PublicCatalog = false; privateRoot.PublicClaim = 0;
        t.True(Throws(() => TownServiceCodec.Write(privateRoot)), "bank root cannot use the visitor-private lane");
    }

    private static void Bounds(Harness t)
    {
        t.Case("Original bank limits reject overflow explicitly and retain legal near-bound packets");
        TownServiceFrame refs = Root();
        refs.Rack!.Members = Enumerable.Range(12, TownRackState.MaxMembers).Select(id => new TownRackMember((ushort)id, 0, false)).ToArray();
        refs.CatalogBank = new TownCatalogBank { Prepared = true, Members = refs.Rack.Members.Select(m => new TownCatalogBankMember(m.Id, 1)).ToArray() };
        byte[] packet = TownServiceCodec.Write(refs);
        t.True(TownServiceCodec.TryRead(packet, packet.Length, out var maximum) && maximum!.CatalogBank!.Members.Length == 384,
            "all384 legal dependencies survive without truncation");
        refs.Rack.Members = refs.Rack.Members.Concat(new[] { new TownRackMember(500, 0, false) }).ToArray();
        refs.CatalogBank.Members = refs.CatalogBank.Members.Concat(new[] { new TownCatalogBankMember(500, 1) }).ToArray();
        t.True(Throws(() => TownServiceCodec.Write(refs)), "385 references fail explicitly");

        var originals = new List<TownServiceFrame>();
        var random = new Random(102);
        TownServiceFrame? previous = null;
        int previousPacked = 0;
        for (ushort id = 12; id < 36; id++)
        {
            var original = Original(id);
            var numbers = new float[1024];
            var bits = new byte[4];
            for (int i = 0; i < numbers.Length; i++)
            {
                random.NextBytes(bits); bits[3] &= 0x7E; // Finite random bit patterns, not repetitive text padding.
                numbers[i] = BitConverter.ToSingle(bits, 0);
            }
            original.Nodes[0].Values[TownServiceProperty.Mesh] = new TownServiceValue { Numbers = numbers };
            originals.Add(original);
            TownServiceFrame next = WithUpdates(originals.ToArray());
            byte[] bank;
            try { bank = next.CatalogBank!.Write(next); }
            catch (InvalidDataException)
            {
                t.True(previous != null && previousPacked > TownCatalogBank.MaxPackedUpdateBytes - 6000,
                    "valid incompressible originals reached the packed bound");
                t.True(Throws(() => TownServiceCodec.Write(next)), "incompressible overflow fails rather than omitting originals");
                break;
            }
            previous = next;
            int at = 4 + next.CatalogBank!.Members.Length * 10;
            previousPacked = BitConverter.ToInt32(bank, at + 6);
        }
        t.True(previous != null && previousPacked > TownCatalogBank.MaxPackedUpdateBytes - 6000,
            "near-cap fixture is meaningfully close to the actual packed limit");
        packet = TownServiceCodec.Write(previous!);
        t.True(TownServiceCodec.TryRead(packet, packet.Length, out var near)
            && near!.CatalogBank!.Updates.Length == previous!.CatalogBank!.Updates.Length,
            "legal near-cap packet preserves every complete original");
        Console.WriteLine("catalog-bank synthetic bound: packed=" + previousPacked + "/" + TownCatalogBank.MaxPackedUpdateBytes
            + " outer=" + packet.Length + "/" + TownServiceFrame.MaxBytes + " originals=" + previous!.CatalogBank!.Updates.Length);

        originals.Clear();
        for (ushort id = 12; id < 142; id++)
        {
            var original = Original(id);
            original.Nodes[0].Values[TownServiceProperty.Mesh] = new TownServiceValue { Numbers = new float[1024] };
            originals.Add(original);
        }
        TownServiceFrame rawOverflow = WithUpdates(originals.ToArray());
        t.True(Throws(() => TownServiceCodec.Write(rawOverflow)), "highly compressible originals still obey the raw inflation bound");
        TownServiceFrame rawNear = WithUpdates(originals.Take(118).ToArray());
        packet = TownServiceCodec.Write(rawNear);
        t.True(TownServiceCodec.TryRead(packet, packet.Length, out var rawReceived)
            && rawReceived!.CatalogBank!.Updates.Length == 118, "legal near-raw-bound originals remain complete");
    }

    private static void FragmentAtomicityAndHeartbeat(Harness t)
    {
        t.Case("Lost/reordered bank fragments cannot publish children and a fresh heartbeat recovers the entire packet");
        TownServiceFrame root = WithUpdates(Original(12), Original(19));
        // Different complete original labels prevent this from being a tiny single datagram.
        for (int i = 0; i < root.CatalogBank!.Updates.Length; i++)
        {
            root.CatalogBank.Updates[i].Nodes[0].Values[TownServiceProperty.TmpText].Text[0] = RandomText(12000, 102 + i);
            root.CatalogBank.Members[i] = new(root.CatalogBank.Updates[i].Module, TownCatalogBank.ContentKey(root.CatalogBank.Updates[i]));
        }
        byte[] raw = TownServiceCodec.Write(root);
        ulong sequence = 2UL << 17 | 1UL << 16 | root.Module;
        byte[][] pages = ExtrasFragments.Encode(raw, raw.Length, sequence, TownServiceCodec.MessageType,
            TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: false);
        t.True(pages.Length > 2, "actual compressed native original bank spans multiple bounded datagrams");
        var receiver = new TownServiceFragments();
        int missing = pages.Length / 2;
        for (int i = pages.Length - 1; i >= 0; i--)
        {
            if (i == missing) continue;
            t.True(receiver.Accept(2, pages[i], pages[i].Length, .1) == null, "missing bank fragment releases no root or child");
        }
        byte[] truncated = pages[missing][..^1];
        t.True(receiver.Accept(2, truncated, truncated.Length, .2) == null, "truncated bank fragment cannot complete the originals");
        // No peer ACK is inferred from sending the final original datagram. Retain and
        // resend the same atomic bank with a fresh sequence, as runtime heartbeats do.
        root.Sequence++;
        byte[] heartbeat = TownServiceCodec.Write(root);
        pages = ExtrasFragments.Encode(heartbeat, heartbeat.Length, sequence + (1UL << 17), TownServiceCodec.MessageType,
            TownServiceCodec.FragmentType, TownServiceFrame.MaxBytes, compress: false);
        byte[]? completed = null;
        for (int i = pages.Length - 1; i >= 0; i--)
        {
            byte[]? next = receiver.Accept(2, pages[i], pages[i].Length, .3);
            if (i > 0) t.True(next == null, "reordered heartbeat remains atomic until all its fragments arrive");
            completed = next ?? completed;
        }
        t.True(completed != null && TownServiceCodec.TryRead(completed, completed.Length, out var fresh)
            && fresh!.CatalogBank!.Updates.Length == 2, "retained heartbeat recovers both exact original snapshots together");
        if (completed != null) t.Wire(heartbeat, completed, completed.Length, "fresh atomic packet carries unchanged required native content");
    }

    private static TownServiceFrame Root() => new()
    {
        PublicCatalog = true, PublicClaim = 3, Service = 1, Session = 99, Sequence = 10,
        Module = 10, Template = 2, TemplateAddress = "merchant.rack|original", Structure = 3, Visible = true,
        Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f },
        Rack = new TownRackState { Cassette = true, Crank = 11, Turn = 4 },
        Nodes = new[] { new TownServiceNode { Binding = 1 } }
    };
    private static TownServiceFrame Original(ushort id)
    {
        var node = new TownServiceNode { Binding = 7 };
        node.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Numbers = new[] { 2.25f }, Text = new[] { "Original Ä shield" } });
        node.Values.Add(TownServiceProperty.Renderer, new TownServiceValue { Numbers = new[] { 1f, .5f } });
        node.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
        return new TownServiceFrame { PublicCatalog = true, PublicClaim = 3, Service = 1, Session = 99, Sequence = 2,
            Module = id, Template = 3, TemplateAddress = "merchant.card.face|original", Structure = 0x01020304,
            Visible = true, Nodes = new[] { node }, Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
    }
    private static TownServiceFrame WithUpdates(params TownServiceFrame[] originals)
    {
        TownServiceFrame root = Root();
        root.Rack!.Members = originals.Select(f => new TownRackMember(f.Module, 0, false)).ToArray();
        root.CatalogBank = new TownCatalogBank { Prepared = true, Updates = originals,
            Members = originals.Select(f => new TownCatalogBankMember(f.Module, TownCatalogBank.ContentKey(f))).ToArray() };
        foreach (TownServiceFrame original in originals)
            original.RackMember = new TownRackStamp { Rack = root.Module, Page = 0, Turn = root.Rack.Turn };
        return root;
    }
    private static byte[] WithoutBank(TownServiceFrame root)
    { TownServiceFrame copy = TownServiceDelta.Copy(root); copy.CatalogBank = null; return TownServiceCodec.Write(copy); }
    private static byte[] Append(byte[] original, byte[] bank)
    {
        using var bytes = new MemoryStream(); bytes.Write(original, 0, original.Length);
        for (int at = 0; at < bank.Length;)
        {
            int count = Math.Min(255, bank.Length - at); bytes.WriteByte(102); bytes.WriteByte((byte)count);
            bytes.Write(bank, at, count); at += count;
        }
        return bytes.ToArray();
    }
    // Independent malformed-wire builder; does not ask the production bank writer to
    // accept the very invalid originals the receiver is required to reject.
    private static byte[] IndependentPayload(TownCatalogBank bank, byte[][] children)
    {
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, true))
            foreach (byte[] packet in children) { writer.Write((uint)packet.Length); writer.Write(packet); }
        byte[] originals = raw.ToArray();
        uint crc = uint.MaxValue;
        foreach (byte value in originals)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = crc >> 1 ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        }
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true)) deflate.Write(originals, 0, originals.Length);
        using (var writer = new BinaryWriter(packed, Encoding.UTF8, true)) writer.Write(~crc);
        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, Encoding.UTF8, true))
        {
            writer.Write((byte)1); writer.Write(bank.Prepared); writer.Write((ushort)bank.Members.Length);
            foreach (var member in bank.Members) { writer.Write(member.Id); writer.Write(member.ContentKey); }
            writer.Write((ushort)children.Length); writer.Write((uint)originals.Length); writer.Write((uint)packed.Length);
            writer.Write(packed.ToArray());
        }
        return result.ToArray();
    }
    private static int Find(byte[] bytes, byte[] pattern)
    {
        for (int i = 0; i <= bytes.Length - pattern.Length; i++)
            if (pattern.Select((value, at) => value == bytes[i + at]).All(equal => equal)) return i;
        return -1;
    }
    private static void Put32(byte[] bytes, int at, int value)
    { for (int i = 0; i < 4; i++) bytes[at + i] = (byte)(value >> (8 * i)); }
    private static bool Throws(Action action)
    { try { action(); return false; } catch (InvalidDataException) { return true; } }
    private static string RandomText(int length, int seed)
    { var random = new Random(seed); return new string(Enumerable.Range(0, length).Select(_ => (char)random.Next(32, 127)).ToArray()); }
    private static object? CachedBlock(TownCatalogBank bank)
    {
        object cache = typeof(TownCatalogBank).GetField("_encoding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bank)!;
        return cache.GetType().GetField("Block", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache);
    }
}
