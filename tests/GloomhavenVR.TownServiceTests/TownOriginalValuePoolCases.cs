using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Net.TownServices;

internal static class TownOriginalValuePoolCases
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    internal static void Run()
    {
        var signedBuilder = new TownServiceCodec.OriginalValuePoolBuilder();
        var signedOriginals = new List<byte[]>();
        for (ushort id = 1; id <= 2; id++)
        {
            var signed = Frame(id); var pose = Identity(); pose[0] = id == 1 ? 0f : BitConverter.Int32BitsToSingle(int.MinValue);
            signed.Nodes[0].Values[TownServiceProperty.Transform] = new TownServiceValue { Numbers = pose };
            byte[] bytes = TownServiceCodec.Write(signed); signedOriginals.Add(bytes); Check(signedBuilder.TryAdd(bytes), "signed-zero exact original admitted");
        }
        byte[] signedPool = signedBuilder.Write(); Check(TownServiceCodec.TryReadBundle(signedPool, signedPool.Length, out var signedDecoded), "signed-zero original pool decoded");
        for (int i = 0; i < signedOriginals.Count; i++) Check(signedOriginals[i].SequenceEqual(signedDecoded![i]), "positive and negative zero owner bits survive across native modules");
        var originals = new List<byte[]>();
        var builder = new TownServiceCodec.OriginalValuePoolBuilder();
        for (ushort id = 1; id <= 44; id++)
        {
            var frame = Frame(id); frame.Sequence = (ulong)(id + 100); frame.BaseSequence = id % 2 == 0 ? (ulong)id : 0;
            frame.HasCanvasFrame = id % 3 == 0; frame.CanvasRect = new[] { 357.89f, 70f, .5f, .5f };
            frame.CanvasSettings = new[] { 0f, 100f, 1f, 0f, 0f };
            frame.Nodes = new TownServiceNode[26];
            for (int n = 0; n < frame.Nodes.Length; n++)
            {
                var node = new TownServiceNode { Binding = (uint)(n + 1) };
                node.Values.Add(TownServiceProperty.Transform, new TownServiceValue { Numbers = Identity() });
                node.Values.Add(TownServiceProperty.Active, new TownServiceValue { Numbers = new[] { 1f } });
                node.Values.Add(TownServiceProperty.Sibling, new TownServiceValue { Numbers = new[] { (float)n } });
                if (n == 7) node.Values.Add(TownServiceProperty.Graphic, new TownServiceValue { Numbers = new[] { 1f, .7f, .8f, .9f, 1f, 0f, 0f, 1f }, Text = new[] { "original/Äöü/font/material/" + id } });
                frame.Nodes[n] = node;
            }
            byte[] original = TownServiceCodec.Write(frame);
            if (id == 7) original = Append(original, new byte[] { 201, 3, 7, 8, 9, 202, 0 });
            originals.Add(original); Check(builder.TryAdd(original), "44 exact full/delta/native canvas modules fit one shared pool");
        }
        byte[] encoded = builder.Write();
        Check(TownServiceCodec.TryReadBundle(encoded, encoded.Length, out var expanded), "complete exact value pool decoded atomically");
        Check(expanded!.Length == originals.Count, "all declared originals delivered");
        for (int i = 0; i < originals.Count; i++) Check(originals[i].SequenceEqual(expanded[i]), "expanded original retains exact header, values and unknown additive fields");
        for (int length = 0; length < encoded.Length; length++) Check(!TownServiceCodec.TryReadBundle(encoded, length, out _), "every truncated pool is inert");
        Check(!builder.TryAdd(originals[0]), "duplicate module address is excluded from a bundle");
        var wrong = Frame(70); wrong.Session++; Check(!builder.TryAdd(TownServiceCodec.Write(wrong)), "different sessions cannot be co-admitted");
        wrong = Frame(70); wrong.Service = 1; Check(!builder.TryAdd(TownServiceCodec.Write(wrong)), "different services cannot be co-admitted");
        wrong = Frame(70); wrong.Sequence = 0;
        try { TownServiceCodec.Write(wrong); Check(false, "invalid native header rejected"); } catch (InvalidDataException) { Check(true, "invalid native header rejected"); }
        byte[] raw = Body(encoded);
        foreach (int count in new[] { 0, 65, 65535 }) { byte[] bad = (byte[])raw.Clone(); bad[1] = (byte)count; bad[2] = (byte)(count >> 8); Reject(bad, "unbounded frame count rejected before allocation"); }
        foreach (int count in new[] { 4097, 65535 }) { byte[] bad = (byte[])raw.Clone(); bad[3] = (byte)count; bad[4] = (byte)(count >> 8); Reject(bad, "unbounded property-value table rejected before allocation"); }
        byte[] nan = (byte[])raw.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, nan, 7, 4); Reject(nan, "non-finite pooled value rejected");
        using (var input = new MemoryStream(raw))
        using (var r = new BinaryReader(input))
        {
            input.Position = 3; int valueCount = r.ReadUInt16(); int scalarCount = r.ReadUInt16(); input.Position += scalarCount * 4;
            int stringCount = r.ReadUInt16(); for (int t = 0; t < stringCount; t++) { int size = r.ReadUInt16(); input.Position += size; }
            for (int v = 0; v < valueCount; v++)
            {
                int numberCount = r.ReadUInt16(); input.Position += 2 * numberCount; int textCount = r.ReadByte();
                input.Position += textCount * 2;
            }
            int tableOffset = (int)input.Position, nodeCount = r.ReadUInt16(), nodeStart = (int)input.Position;
            byte[] bad = (byte[])raw.Clone(); bad[tableOffset] = 255; bad[tableOffset + 1] = 255; Reject(bad, "unbounded pooled node table rejected before allocation");
            bad = (byte[])raw.Clone(); bad[nodeStart + 6] = 255; bad[nodeStart + 7] = 255; Reject(bad, "foreign shared value index rejected");
            bad = (byte[])raw.Clone(); bad[nodeStart + 8] = bad[nodeStart + 5]; Reject(bad, "duplicate node property is rejected");
            for (int n = 0; n < nodeCount; n++) { input.Position += 4; int properties = r.ReadByte(); input.Position += properties * 3; }
            int firstHeaderLength = r.ReadUInt16(); input.Position += firstHeaderLength + 1; int members = r.ReadUInt16();
            int memberOffset = (int)input.Position;
            bad = (byte[])raw.Clone(); bad[memberOffset] = 255; bad[memberOffset + 1] = 255; Reject(bad, "foreign shared native node index rejected");
            bad = (byte[])raw.Clone(); bad[memberOffset + 2] = bad[memberOffset]; bad[memberOffset + 3] = bad[memberOffset + 1]; Reject(bad, "duplicate native binding cannot enter one original module");
            input.Position += members * 2; r.ReadUInt16(); int secondHeader = (int)input.Position;
            bad = (byte[])raw.Clone(); bad[secondHeader + 30] = 1; bad[secondHeader + 31] = 0; Reject(bad, "two module headers cannot share one native address");
        }
        byte[] mixed = Append(encoded, new byte[] { 78, 1, 1 }); Check(!TownServiceCodec.TryReadBundle(mixed, mixed.Length, out _), "mixed original/pool packet cannot manufacture extra dispatches");
        var huge = Frame(1); huge.Nodes[0].Values[TownServiceProperty.Active].Numbers = new float[1024];
        var limit = new TownServiceCodec.OriginalValuePoolBuilder();
        Check(limit.TryAdd(TownServiceCodec.Write(huge)), "bounded numeric value accepted");
        huge.Module = huge.Template = 2; huge.Nodes[0].Values[TownServiceProperty.Active].Numbers = new float[1025];
        try { TownServiceCodec.Write(huge); Check(false, "numeric value bound remains unchanged"); } catch (InvalidDataException) { Check(true, "numeric value bound remains unchanged"); }
        var bounded = new TownServiceCodec.OriginalValuePoolBuilder(); int admitted = 0;
        for (ushort id = 1; id <= 64; id++)
        {
            var large = Frame(id); large.Nodes[0].Values[TownServiceProperty.Active].Text = new[] { new string('a', 16300), new string('b', 16300) };
            if (bounded.TryAdd(TownServiceCodec.Write(large))) admitted++;
        }
        Check(admitted > 1 && admitted < 64, "decompressed per-picture bytes remain bounded even with highly shared owner values");
        byte[] boundedPool = bounded.Write(); Check(TownServiceCodec.TryReadBundle(boundedPool, boundedPool.Length, out _), "bounded shared values cannot allocate beyond admitted originals");
        Console.WriteLine($"Exact original pool: {checks} assertions;44 original modules={originals.Sum(x=>x.Length)}B -> {encoded.Length}B before transport compression.");
    }
    private static void Reject(byte[] raw, string why) { byte[] packet = Wrap(raw); Check(!TownServiceCodec.TryReadBundle(packet, packet.Length, out _), why); }
    private static byte[] Append(byte[] a, byte[] b) { byte[] output = new byte[a.Length+b.Length]; Array.Copy(a,output,a.Length); Array.Copy(b,0,output,a.Length,b.Length); return output; }
    private static byte[] Body(byte[] packet) { using var output = new MemoryStream(); for(int at=6;at<packet.Length;) {int id=packet[at++], count=packet[at++]; if(id==110)output.Write(packet,at,count);at+=count;}return output.ToArray(); }
    private static byte[] Wrap(byte[] raw) { using var output = new MemoryStream();output.Write(new byte[]{0x31,0x52,0x56,0x47,3,19});for(int at=0;at<raw.Length;){int count=Math.Min(255,raw.Length-at);output.WriteByte(110);output.WriteByte((byte)count);output.Write(raw,at,count);at+=count;}return output.ToArray(); }
    private static float[] Identity() => new[] { 0f,0f,0f,0f,0f,0f,1f,1f,1f,1f };
    internal static TownServiceFrame Frame(ushort id) => new() { Service=3, Session=2, Sequence=1, Module=id, Template=id,
        TemplateAddress="enchant.row|"+id, Structure=919, Visible=true, Pose=Identity(), CanvasPose=Identity(),
        Nodes=new[]{new TownServiceNode{Binding=1,Values={ [TownServiceProperty.Active]=new TownServiceValue{Numbers=new[]{1f}} }}} };
}
