using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class ResultsPresentationVectors
{
    private static ScenarioResultsState Empty() => new() { Key = 0x04030201, Revision = 0x0c0b0a0908070605UL, Author = 0x100f0e0d };
    private static ScenarioResultsState Picture()
    {
        var state = Empty(); state.Visible = true;
        var material = new TownServiceValue { Numbers = new[] { 0f, -0f, 1f, 3.5f }, Text = new[] { "Original/NativeShader", "native/material", "textures/original" } };
        var root = new TownServiceNode { Binding = 0x11 }; root.Values.Add(TownServiceProperty.Material, material);
        var child = new TownServiceNode { Binding = 0x22 }; child.Values.Add(TownServiceProperty.Material,
            new TownServiceValue { Numbers = material.Numbers.ToArray(), Text = material.Text.ToArray() });
        child.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Text = new[] { "Märchen – 日本語\nowner text" } });
        state.Parts = new[] {
            new ScenarioResultsPart { Id = 1, Structure = 0x11223344, Address = "results.root", Nodes = new[] { root } },
            new ScenarioResultsPart { Id = 2, ParentId = 1, ParentBinding = 0x11, Structure = 0x55667788, Address = "results.child", Nodes = new[] { child } } };
        return state;
    }
    private static bool Rejected(Action action)
    { try { action(); return false; } catch (InvalidDataException) { return true; } }
    private static byte[] Envelope(byte[] packet)
    {
        using var output = new MemoryStream();
        for (int at = 6; at < packet.Length;)
        { byte tag = packet[at++]; int n = packet[at++]; if (tag == 125) output.Write(packet, at, n); at += n; }
        return output.ToArray();
    }
    private static byte[] Frame(byte[] envelope)
    {
        using var output = new MemoryStream(); output.Write(Hex.Bytes("31 52 56 47 03 1D"));
        for (int at = 0; at < envelope.Length;)
        { int n = Math.Min(255, envelope.Length - at); output.WriteByte(125); output.WriteByte((byte)n); output.Write(envelope, at, n); at += n; }
        return output.ToArray();
    }
    private static byte[] Raw(byte[] raw)
    {
        var envelope = new byte[raw.Length + 6]; envelope[0] = 1;
        for (int i = 0; i < 4; i++) envelope[2 + i] = (byte)(raw.Length >> (8 * i));
        Buffer.BlockCopy(raw, 0, envelope, 6, raw.Length); return Frame(envelope);
    }
    private static void Invalid(Harness t, string why, Action<ScenarioResultsState> mutation)
    { var state = Picture(); mutation(state); t.True(Rejected(() => ScenarioResultsCodec.Write(state)), why); }

    internal static void Run(Harness t)
    {
        t.Case("761: independent results125 hidden picture is a byte-exact metadata packet");
        var empty = Empty(); byte[] encoded = ScenarioResultsCodec.Write(empty);
        byte[] golden = Hex.Bytes(@"
            31 52 56 47 03 1D 7D 77 01 00 71 00 00 00 // GVR1,msg29,TLV125,raw envelope113
            02 04                                     // grammar2,presentation=true,hidden,no move
            01 02 03 04 05 06 07 08 09 0A 0B 0C       // key/revision
            00 00 00 00 00 00 00 00                   // complete baseline,not a delta
            00 00 00 00 0D 0E 0F 10                   // time0,original content author
            00 00 00 00 00 00 00 00 00 00 00 00       // pose origin
            00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F // pose unit rotation
            00 00 80 3F 00 00 80 3F                   // size/reference
            00 00 00 00 00 00 00 00 00 00 00 00       // captured frame origin
            00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 3F // frame unit rotation
            00 00 80 3F 00 00 F0 44 00 00 87 44       // frame scale1,frame1920x1080
            00 00 00 00 00 00 00                      // empty number/string/value pools,parts
        ");
        t.Wire(golden, encoded, encoded.Length, "metadata-only tombstone has no NPC78 or hidden gameplay command");
        t.True(ScenarioResultsCodec.TryRead(golden, golden.Length, out var read) && read != null
            && read.Key == empty.Key && read.Author == empty.Author && read.Revision == empty.Revision
            && read.HasPresentation && !read.Visible && read.Parts.Length == 0, "independent golden decodes exact content identity");
        t.Equal(127, encoded.Length, "small hidden packet stays raw and deterministic");
        for (int length = 0; length < encoded.Length; length++)
            t.True(!ScenarioResultsCodec.TryRead(encoded, length, out _), "truncated metadata packet rejected at" + length);
        var unknown = encoded.Concat(Hex.Bytes("FA 02 AB CD")).ToArray();
        t.True(ScenarioResultsCodec.TryRead(unknown, unknown.Length, out _), "unknown additive record skipped by its independent length");
        var extraRaw = Envelope(encoded).Skip(6).Concat(new byte[] { 0 }).ToArray(); var extraPacket = Raw(extraRaw);
        t.True(!ScenarioResultsCodec.TryRead(extraPacket, extraPacket.Length, out _), "unconsumed complete-body byte rejected");
        var emptyChunk = Hex.Bytes("31 52 56 47 03 1D 7D 00");
        t.True(!ScenarioResultsCodec.TryRead(emptyChunk, emptyChunk.Length, out _), "zero-length known chunk is not a complete picture");
        var wrongMessage = encoded.ToArray(); wrongMessage[5] = 19;
        t.True(!ScenarioResultsCodec.TryRead(wrongMessage, wrongMessage.Length, out _), "NPC message19 cannot become results metadata");

        t.Case("761: exact native parts, geometry, pooled material and text are one atomic picture");
        var picture = Picture(); picture.Pose.Position = new Vector3(1, 2, 3); picture.Origin = new Vector3(-4, 5, -6);
        picture.Scale = .00000001f; picture.Size = .0000001f; picture.Reference = 2;
        picture.FrameSize = new Vector2(3440, 1935);
        encoded = ScenarioResultsCodec.Write(picture);
        t.True(ScenarioResultsCodec.TryRead(encoded, encoded.Length, out read) && read != null, "complete native-part picture decodes");
        t.True(read!.Parts.Length == 2 && read.Parts[0].Id == 1 && read.Parts[1].ParentId == 1 && read.Parts[1].ParentBinding == 0x11
            && read.Parts[1].Address == "results.child" && read.Parts[1].Structure == 0x55667788, "parts preserve original structure/address/parent binding/order");
        t.True(read.Pose.Position.x == 1 && read.Pose.Position.z == 3 && read.Origin.x == -4 && read.Origin.y == 5
            && read.Scale == picture.Scale && read.Size == picture.Size && read.Reference == 2
            && read.FrameSize.x == 3440 && read.FrameSize.y == 1935, "captured full geometry/frame dimensions preserve tiny positive zoom without camera ECALLs");
        var material0 = read.Parts[0].Nodes[0].Values[TownServiceProperty.Material];
        var material1 = read.Parts[1].Nodes[0].Values[TownServiceProperty.Material];
        t.True(ReferenceEquals(material0, material1), "exact equivalent materials are pooled across different parts");
        t.True(BitConverter.SingleToInt32Bits(material0.Numbers[1]) == unchecked((int)0x80000000)
            && BitConverter.SingleToInt32Bits(material0.Numbers[0]) == 0, "float pool preserves both IEEE signed-zero values");
        t.Equal("Märchen – 日本語\nowner text", read.Parts[1].Nodes[0].Values[TownServiceProperty.TmpText].Text[0], "owner UTF8 caption retained without observer localization");
        var rawPicture = Envelope(encoded).Skip(6).ToArray(); byte[] pattern = Encoding.UTF8.GetBytes("Märchen");
        int textAt = Find(rawPicture, pattern); t.True(textAt >= 0 && Envelope(encoded)[1] == 0, "small native picture fixture exposes independent raw UTF8 field");
        rawPicture[textAt] = 0xff; var invalidUtf8 = Raw(rawPicture);
        t.True(!ScenarioResultsCodec.TryRead(invalidUtf8, invalidUtf8.Length, out _), "invalid UTF8 rejected rather than replacement-character content");
        var incoming = Envelope(encoded).Skip(6).ToArray(); var layout = Layout(incoming);
        ReaderBad(t, incoming, 2, new byte[4], "zero incoming content key");
        ReaderBad(t, incoming, 6, new byte[8], "zero incoming content revision");
        ReaderBad(t, incoming, 26, new byte[4], "zero incoming content author");
        ReaderBad(t, incoming, 22, Hex.Bytes("00 00 C0 7F"), "nonfinite incoming sample time");
        ReaderBad(t, incoming, 42, new byte[16], "zero incoming pose rotation");
        ReaderBad(t, incoming, layout.Parts[1], Hex.Bytes("01 00"), "duplicate incoming part ID");
        ReaderBad(t, incoming, layout.Parts[1] + 2, Hex.Bytes("07 00"), "missing incoming parent");
        ReaderBad(t, incoming, layout.Parts[1] + 4, Hex.Bytes("55 00 00 00"), "missing incoming parent binding");
        ReaderBad(t, incoming, layout.Parts[0] + 10, new byte[4], "zero incoming structure");
        ReaderBad(t, incoming, layout.Parts[1] + 8, Hex.Bytes("FF FF"), "out-of-pool incoming native address");
        ReaderBad(t, incoming, layout.Parts[1] + 14, Hex.Bytes("01 01"), "oversized incoming node count");
        ReaderBad(t, incoming, layout.Nodes[1], new byte[4], "zero incoming node binding");
        ReaderBad(t, incoming, layout.Nodes[1] + 4, new byte[] { 255 }, "oversized incoming property count");
        ReaderBad(t, incoming, layout.Properties[0] + 1, Hex.Bytes("FF FF"), "out-of-pool incoming value reference");
        ReaderBad(t, incoming, layout.Properties[2], new[] { incoming[layout.Properties[1]] }, "duplicate incoming property key");
        var cyclicIncoming = incoming.ToArray(); cyclicIncoming[layout.Parts[1] + 2] = 2; cyclicIncoming[layout.Parts[1] + 4] = 0x22;
        byte[] cyclicPacket = Raw(cyclicIncoming);
        t.True(!ScenarioResultsCodec.TryRead(cyclicPacket, cyclicPacket.Length, out _), "cyclic incoming parent graph is refused atomically");
        // Unused but otherwise valid pooled values make a valid body exceed1MiB. This independent
        // wire construction proves the decoder's limit, not merely the writer refusing to make it.
        using var oversizedBody = new MemoryStream();
        oversizedBody.Write(incoming, 0, layout.PartCountAt);
        for (int value = 0; value < 600; value++)
        { oversizedBody.WriteByte(0); oversizedBody.WriteByte(4); oversizedBody.Write(new byte[2048]); oversizedBody.WriteByte(0); }
        oversizedBody.Write(incoming, layout.PartCountAt, incoming.Length - layout.PartCountAt);
        byte[] tooLargeBody = oversizedBody.ToArray(); int poolCount = U16(incoming, layout.ValueCountAt) + 600;
        tooLargeBody[layout.ValueCountAt] = (byte)poolCount; tooLargeBody[layout.ValueCountAt + 1] = (byte)(poolCount >> 8);
        byte[] forbidden = Compressed(tooLargeBody);
        t.True(tooLargeBody.Length > 1024 * 1024 && forbidden.Length < 60000, "independent valid expanded-pool fixture fits the outer envelope while exceeding logical bound");
        t.True(!ScenarioResultsCodec.TryRead(forbidden, forbidden.Length, out _), "otherwise-valid expanded value pool over1MiB is rejected before inflation");


        t.Case("761: independent pose-only register does not impersonate a content author");
        var pose = Empty(); pose.HasPresentation = false;
        pose.Move = new SharedWindowMoveEntry { Kind = 7, Phase = SharedWindowMovePhase.Held,
            ContentKey = pose.Key, OpeningAuthor = pose.Author, Opening = 4, Claim = 5, Sequence = 6 };
        encoded = ScenarioResultsCodec.Write(pose);
        t.True(ScenarioResultsCodec.TryRead(encoded, encoded.Length, out read) && !read!.HasPresentation
            && read.Parts.Length == 0 && read.Move?.Kind == 7 && read.Move?.Claim == 5, "participant pose-only row binds exact author/key/opening without carrying UI content");
        var noMove = Empty(); noMove.HasPresentation = false;
        t.True(Rejected(() => ScenarioResultsCodec.Write(noMove)), "pose-only packet without ordered move refused");
        pose.Parts = Picture().Parts; t.True(Rejected(() => ScenarioResultsCodec.Write(pose)), "pose-only packet cannot carry foreign content"); pose.Parts = Array.Empty<ScenarioResultsPart>();
        var wrong = pose.Move!.Value; wrong.ContentKey++; pose.Move = wrong;
        t.True(Rejected(() => ScenarioResultsCodec.Write(pose)), "move cannot borrow another content key");
        wrong.ContentKey = pose.Key; wrong.OpeningAuthor++; pose.Move = wrong;
        t.True(Rejected(() => ScenarioResultsCodec.Write(pose)), "move cannot borrow another opening author");
        wrong.OpeningAuthor = pose.Author; wrong.Kind = 1; pose.Move = wrong;
        t.True(Rejected(() => ScenarioResultsCodec.Write(pose)), "other shared window kind cannot enter results lane");

        t.Case("761: malformed identity, original graph and geometry never publish a partial picture");
        Invalid(t, "zero content key refused", s => s.Key = 0);
        Invalid(t, "zero revision refused", s => s.Revision = 0);
        Invalid(t, "zero content author refused", s => s.Author = 0);
        Invalid(t, "nonfinite sample time refused", s => s.SampleTime = float.NaN);
        Invalid(t, "negative sample time refused", s => s.SampleTime = -1);
        Invalid(t, "nonfinite position refused", s => s.Pose.Position.x = float.PositiveInfinity);
        Invalid(t, "zero rotation refused", s => s.Rotation = default);
        Invalid(t, "nonunit pose refused", s => s.Pose.Rotation.w = 2);
        Invalid(t, "zero frame scale refused", s => s.Scale = 0);
        Invalid(t, "zero reference refused", s => s.Reference = 0);
        Invalid(t, "zero frame width refused", s => s.FrameSize.x = 0);
        Invalid(t, "visible picture without original root refused", s => s.Parts = s.Parts.Skip(1).ToArray());
        Invalid(t, "duplicate part identity refused", s => s.Parts[1].Id = 1);
        Invalid(t, "root parent assignment refused", s => s.Parts[0].ParentId = 2);
        Invalid(t, "root binding assignment refused", s => s.Parts[0].ParentBinding = 17);
        Invalid(t, "missing parent refused", s => s.Parts[1].ParentId = 7);
        Invalid(t, "missing parent-node binding refused", s => s.Parts[1].ParentBinding = 55);
        Invalid(t, "empty native part address refused", s => s.Parts[1].Address = "");
        Invalid(t, "zero original structure refused", s => s.Parts[1].Structure = 0);
        Invalid(t, "duplicate original node binding refused", s => s.Parts[1].Nodes = new[] { s.Parts[1].Nodes[0], s.Parts[1].Nodes[0] });
        Invalid(t, "zero original node binding refused", s => s.Parts[1].Nodes[0].Binding = 0);
        Invalid(t, "unknown property cannot acquire a callback", s => s.Parts[0].Nodes[0].Values.Add(65535, new TownServiceValue()));
        Invalid(t, "nonfinite property refused", s => s.Parts[0].Nodes[0].Values[TownServiceProperty.Material].Numbers[0] = float.NaN);
        Invalid(t, "native scalar-array bound preserved", s => s.Parts[0].Nodes[0].Values[TownServiceProperty.Material].Numbers = new float[1025]);
        Invalid(t, "native text-array bound preserved", s => s.Parts[0].Nodes[0].Values[TownServiceProperty.Material].Text = new string[255]);
        Invalid(t, "invalid source UTF16 refused before encoding", s => s.Parts[1].Address = "bad\ud800");
        Invalid(t, "native UTF8 field bound preserved", s => s.Parts[0].Nodes[0].Values[TownServiceProperty.Material].Text = new[] { new string('x', 16385) });
        var cycle = Picture(); cycle.Parts[1].ParentId = 3; cycle.Parts[1].ParentBinding = 0x33;
        cycle.Parts = cycle.Parts.Concat(new[] { new ScenarioResultsPart { Id = 3, ParentId = 2, ParentBinding = 0x22,
            Structure = 1, Address = "results.cycle", Nodes = new[] { new TownServiceNode { Binding = 0x33 } } } }).ToArray();
        t.True(Rejected(() => ScenarioResultsCodec.Write(cycle)), "cyclic parent graph is bounded and refused");
        var hiddenRaw = Envelope(ScenarioResultsCodec.Write(Empty())).Skip(6).ToArray();
        foreach (int offset in new[] { 106, 108, 110 })
        {
            var malformed = hiddenRaw.ToArray(); malformed[offset] = malformed[offset + 1] = 255; byte[] packet = Raw(malformed);
            t.True(!ScenarioResultsCodec.TryRead(packet, packet.Length, out _), "oversized declared pool rejected before allocations at" + offset);
        }
        var tooMany = hiddenRaw.ToArray(); tooMany[112] = 65; var badParts = Raw(tooMany);
        t.True(!ScenarioResultsCodec.TryRead(badParts, badParts.Length, out _), "oversized declared part count rejected");

        t.Case("761: cumulative property updates use one exact complete baseline, never a cascade");
        var basis = Picture(); basis.Revision = 10; var current = Picture(); current.Revision = 11;
        current.Parts[1].Nodes[0].Values[TownServiceProperty.TmpText] = new TownServiceValue { Text = new[] { "changed native result" } };
        current.Pose.Position = new Vector3(7, 8, 9); current.FrameSize = new Vector2(2500, 1800);
        t.True(ScenarioResultsDelta.Compatible(basis, current), "identical original topology admits cumulative native properties");
        var delta = ScenarioResultsDelta.Create(basis, current);
        t.True(delta != null && delta.BaseRevision == 10 && delta.Revision == 11 && delta.Parts.Length == 2
            && delta.Parts[0].Nodes.Length == 0 && delta.Parts[1].Nodes.Length == 1
            && delta.Parts[1].Nodes[0].Values.Count == 1, "sparse delta retains every part header but only changed native property");
        byte[] deltaBytes = ScenarioResultsCodec.Write(delta!);
        t.True(ScenarioResultsCodec.TryRead(deltaBytes, deltaBytes.Length, out var receivedDelta)
            && receivedDelta!.BaseRevision == 10 && receivedDelta.Parts[0].Nodes.Length == 0,
            "independent125 codec carries sparse nodes only with explicit baseline revision");
        var expanded = ScenarioResultsDelta.Expand(basis, receivedDelta!);
        t.True(expanded != null && expanded.BaseRevision == 0 && expanded.Revision == 11
            && expanded.Parts[0].Nodes.Length == 1 && expanded.Parts[1].Nodes[0].Values[TownServiceProperty.TmpText].Text[0] == "changed native result"
            && expanded.Pose.Position.x == 7 && expanded.FrameSize.x == 2500,
            "exact baseline expands complete original content and current full geometry headers");
        t.Equal("Märchen – 日本語\nowner text", basis.Parts[1].Nodes[0].Values[TownServiceProperty.TmpText].Text[0], "expansion never mutates immutable baseline");
        var newest = Picture(); newest.Revision = 12;
        newest.Parts[1].Nodes[0].Values[TownServiceProperty.TmpText] = new TownServiceValue { Text = new[] { "latest after lost intermediate sample" } };
        var direct = ScenarioResultsDelta.Create(basis, newest);
        var afterLost = ScenarioResultsDelta.Expand(basis, direct!);
        t.True(afterLost != null && afterLost.Parts[1].Nodes[0].Values[TownServiceProperty.TmpText].Text[0] == "latest after lost intermediate sample",
            "missing or reordered intermediate delta cannot lose a cumulative native update");
        t.True(ScenarioResultsDelta.Expand(null, delta!) == null, "unknown baseline never yields partial roots or guessed values");
        t.True(ScenarioResultsDelta.Expand(expanded, direct!) == null, "a prior expanded sample cannot impersonate named baseline10");
        t.True(!ScenarioResultsDelta.Compatible(delta, newest) && ScenarioResultsDelta.Create(delta, newest) == null,
            "a sparse packet can never be the next baseline");
        var wrongBasis = Picture(); wrongBasis.Revision = 9;
        t.True(ScenarioResultsDelta.Expand(wrongBasis, delta!) == null, "same topology with different baseline revision refused");
        wrongBasis.Revision = 10; wrongBasis.Author++;
        t.True(ScenarioResultsDelta.Expand(wrongBasis, delta!) == null, "baseline content author cannot migrate");
        wrongBasis.Author = basis.Author; wrongBasis.Key++;
        t.True(ScenarioResultsDelta.Expand(wrongBasis, delta!) == null, "baseline opening key cannot migrate");
        var incompatible = Picture(); incompatible.Revision = 11; incompatible.Parts[1].ParentBinding = 0x22;
        t.True(!ScenarioResultsDelta.Compatible(basis, incompatible), "changed parent identity forces a fresh complete picture");
        incompatible = Picture(); incompatible.Revision = 11; incompatible.Parts[1].Nodes[0].Values.Remove(TownServiceProperty.TmpText);
        t.True(ScenarioResultsDelta.Create(basis, incompatible) == null, "removed original property forces a fresh complete picture");
        var injected = ScenarioResultsDelta.Create(basis, current)!;
        injected.Parts[1].Nodes[0].Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
        t.True(ScenarioResultsDelta.Expand(basis, injected) == null, "incoming delta cannot add a property outside exact original topology");
        injected = ScenarioResultsDelta.Create(basis, current)!; injected.Parts[1].Nodes[0].Binding = 0x99;
        t.True(ScenarioResultsDelta.Expand(basis, injected) == null, "incoming delta cannot create an unknown node");
        var signChanged = Picture(); signChanged.Revision = 11;
        signChanged.Parts[0].Nodes[0].Values[TownServiceProperty.Material].Numbers[0] = -0f;
        var exactSign = ScenarioResultsDelta.Expand(basis, ScenarioResultsDelta.Create(basis, signChanged)!);
        t.True(exactSign != null && BitConverter.SingleToInt32Bits(exactSign.Parts[0].Nodes[0].Values[TownServiceProperty.Material].Numbers[0]) == int.MinValue,
            "generic native diff's signed-zero equivalence cannot drop results wire bits");
        var emptyFullPart = Picture(); emptyFullPart.Parts[0].Nodes = Array.Empty<TownServiceNode>();
        t.True(Rejected(() => ScenarioResultsCodec.Write(emptyFullPart)), "empty node list cannot impersonate a complete visible original");
        var pendingBinding = ScenarioResultsDelta.Create(basis, current)!; pendingBinding.Parts[1].ParentBinding = 0x99;
        t.True(ScenarioResultsCodec.TryRead(ScenarioResultsCodec.Write(pendingBinding), ScenarioResultsCodec.Write(pendingBinding).Length, out _)
            && ScenarioResultsDelta.Expand(basis, pendingBinding) == null,
            "codec defers parent-node existence for sparse headers but expansion requires exact original binding");
        var emptyChange = Picture(); emptyChange.Revision = 11;
        var noProperties = ScenarioResultsDelta.Create(basis, emptyChange)!;
        t.True(noProperties.Parts.All(p => p.Nodes.Length == 0) && ScenarioResultsDelta.Expand(basis, noProperties) != null,
            "unchanged property picture remains an empty cumulative update with complete headers");

        t.Case("761: large complete graph uses shared value pools and bounded atomic compression");
        var large = Large(64, 256); encoded = ScenarioResultsCodec.Write(large); byte[] envelope = Envelope(encoded);
        t.True(encoded.Length <= 60000 && envelope[1] == 1, "complete64-part graph fits unchanged U16 fragment envelope through value pooling and Deflate");
        t.True(ScenarioResultsCodec.TryRead(encoded, encoded.Length, out read) && read!.Parts.Length == 64 && read.Parts[63].Nodes.Length == 256,
            "entire large picture arrives atomically with all parts/nodes");
        t.True(ReferenceEquals(read!.Parts[0].Nodes[0].Values[TownServiceProperty.Material], read.Parts[63].Nodes[255].Values[TownServiceProperty.Material]),
            "large native material repeated16384times is stored once rather than expanded per original");
        for (int missing = 1; missing <= Math.Min(16, envelope.Length - 6); missing++)
        {
            byte[] cut = Frame(envelope.Take(envelope.Length - missing).ToArray());
            t.True(!ScenarioResultsCodec.TryRead(cut, cut.Length, out _), "truncated compressed block rejected missing" + missing);
        }
        var trailing = Frame(envelope.Concat(new byte[] { 123 }).ToArray());
        t.True(!ScenarioResultsCodec.TryRead(trailing, trailing.Length, out _), "Deflate read-ahead cannot consume trailing compressed-wire data");
        var bombEnvelope = envelope.ToArray(); bombEnvelope[2] = 1; bombEnvelope[3] = 0; bombEnvelope[4] = 16; bombEnvelope[5] = 0;
        var bomb = Frame(bombEnvelope); t.True(!ScenarioResultsCodec.TryRead(bomb, bomb.Length, out _), "declared decompressed body over1MiB refused before inflation");
        var shortEnvelope = envelope.ToArray(); shortEnvelope[2] = 1; shortEnvelope[3] = shortEnvelope[4] = shortEnvelope[5] = 0;
        var overflow = Frame(shortEnvelope); t.True(!ScenarioResultsCodec.TryRead(overflow, overflow.Length, out _), "inflation cannot exceed declared output bound");
        var random = new System.Random(761); string Noise(int count) => new(Enumerable.Range(0, count).Select(_ => (char)random.Next(32, 127)).ToArray());
        var incompressible = Picture(); incompressible.Parts[0].Nodes[0].Values[TownServiceProperty.Material].Text = Enumerable.Range(0, 8).Select(_ => Noise(12000)).ToArray();
        t.True(Rejected(() => ScenarioResultsCodec.Write(incompressible)), "legitimate but oversized packed picture is refused without truncating native content");
        var excessive = Large(3, 200);
        int v = 0; foreach (ScenarioResultsPart part in excessive.Parts) foreach (TownServiceNode node in part.Nodes)
        { var numbers = Enumerable.Range(0, 1024).Select(i => (float)i).ToArray(); numbers[0] = ++v + 2000; node.Values[TownServiceProperty.Material] = new TownServiceValue { Numbers = numbers }; }
        t.True(Rejected(() => ScenarioResultsCodec.Write(excessive)), "uncompressed logical body over1MiB refused even if repeated scalars would compress");
    }
    private static ScenarioResultsState Large(int parts, int nodes)
    {
        var state = Empty(); state.Visible = true;
        string[] material = Enumerable.Range(0, 64).Select(i => "shader-property-" + i + "-" + new string('x', 200)).ToArray();
        state.Parts = Enumerable.Range(0, parts).Select(p => new ScenarioResultsPart {
            Id = (ushort)(p + 1), ParentId = (ushort)(p == 0 ? 0 : 1), ParentBinding = p == 0 ? 0u : 1u,
            Structure = (uint)(p + 1), Address = "results.part." + p,
            Nodes = Enumerable.Range(0, nodes).Select(i => {
                var node = new TownServiceNode { Binding = (uint)(i + 1) };
                node.Values.Add(TownServiceProperty.Material, new TownServiceValue { Numbers = new[] { 0f, 1f }, Text = material.ToArray() });
                return node;
            }).ToArray() }).ToArray();
        return state;
    }
    private sealed class Offsets
    {
        internal int ValueCountAt, PartCountAt;
        internal readonly List<int> Parts = new(), Nodes = new(), Properties = new();
    }
    private static int U16(byte[] data, int at) => data[at] | data[at + 1] << 8;
    private static Offsets Layout(byte[] raw)
    {
        var result = new Offsets(); int at = 106 + ((raw[1] & 2) != 0 ? 30 : 0);
        int n = U16(raw, at); at += 2 + 4 * n;
        int strings = U16(raw, at); at += 2;
        for (int i = 0; i < strings; i++) { int bytes = U16(raw, at); at += 2 + bytes; }
        result.ValueCountAt = at; int values = U16(raw, at); at += 2;
        for (int i = 0; i < values; i++) { int numbers = U16(raw, at); at += 2 + numbers * 2; int text = raw[at++]; at += text * 2; }
        result.PartCountAt = at; int parts = raw[at++];
        for (int i = 0; i < parts; i++)
        {
            result.Parts.Add(at); int nodes = U16(raw, at + 14); at += 16;
            for (int j = 0; j < nodes; j++)
            { result.Nodes.Add(at); int properties = raw[at + 4]; at += 5;
                for (int v = 0; v < properties; v++) { result.Properties.Add(at); at += 3; } }
        }
        if (at != raw.Length) throw new InvalidDataException("Independent vector layout differs from declared grammar.");
        return result;
    }
    private static void ReaderBad(Harness t, byte[] original, int at, byte[] replacement, string why)
    {
        byte[] raw = original.ToArray(); Buffer.BlockCopy(replacement, 0, raw, at, replacement.Length); byte[] packet = Raw(raw);
        t.True(!ScenarioResultsCodec.TryRead(packet, packet.Length, out var state) && state == null, why + " cannot publish a partial picture");
    }
    private static byte[] Compressed(byte[] raw)
    {
        using var body = new MemoryStream(); body.WriteByte(1); body.WriteByte(1);
        using (var writer = new BinaryWriter(body, Encoding.UTF8, true)) writer.Write((uint)raw.Length);
        using (var compression = new DeflateStream(body, System.IO.Compression.CompressionLevel.Fastest, true)) compression.Write(raw);
        return Frame(body.ToArray());
    }
    private static int Find(byte[] bytes, byte[] pattern)
    { for (int at = 0; at <= bytes.Length - pattern.Length; at++) if (bytes.Skip(at).Take(pattern.Length).SequenceEqual(pattern)) return at; return -1; }
}
