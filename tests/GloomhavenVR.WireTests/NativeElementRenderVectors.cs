using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class NativeElementRenderVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native-element-render/additive-independent-golden");
        NativeBoardState legacy = Frame(null);
        byte[] packet = new byte[NativeBoardCodec.MaxSize]; int oldLength = NativeBoardCodec.Write(legacy, packet);
        byte[] old = packet.Take(oldLength).ToArray();
        byte[] raw = GoldenRaw();
        byte[] golden = old.Concat(Pages(raw)).ToArray();
        t.True(NativeBoardCodec.TryRead(golden, golden.Length, out NativeBoardState? decoded)
            && decoded!.RenderElements != null && decoded.RenderElements.Length == 6
            && decoded.RenderElements[5].Nodes[0].Binding == 6, "handwritten53 hierarchy appended to a valid unchanged52 reads atomically");
        NativeBoardState state = Frame(Hierarchy(1, false));
        int length = NativeBoardCodec.Write(state, packet);
        t.True(packet.Take(oldLength).SequenceEqual(old), "adding53 does not change one byte of existing52");
        t.True(Unpack(packet, length, 53).SequenceEqual(raw), "writer's uncompressed53 body matches independent golden fields and float order");
        t.True(NativeBoardCodec.TryRead(old, old.Length, out decoded) && decoded!.RenderElements == null,
            "old52-only messages remain accepted without fabricated hierarchy data");
        t.True(NativeBoardCodec.TryRead(packet, length, out decoded) && decoded!.SamePicture(state), "complete hierarchy round-trip preserves all channels");

        t.Case("native-element-render/full-channels-and-immutability");
        NativeElementRenderState[] hierarchy = Hierarchy(32, true);
        state = Frame(hierarchy);
        NativeBoardState copied = state.CopyWithTime(55f);
        hierarchy[0].Nodes[1].Geometry[6] = 99f;
        hierarchy[0].Nodes[1].RendererColor[3] = 99f;
        t.True(copied.SampleTime == 55f && copied.SamePicture(state)
            && copied.RenderElements![0].Nodes[1].Geometry[6] != 99f
            && copied.RenderElements[0].Nodes[1].RendererColor[3] != 99f,
            "publication and idle predecessor preserve and deep-copy nested geometry and renderer alpha");
        length = NativeBoardCodec.Write(state, packet);
        t.True(Unpack(packet, length, 53).Length == 25351 && length <= NativeBoardCodec.MaxSize,
            "six32-node hierarchies fit the exact maximum25351-byte raw53 and40960-byte message bound");
        t.True(NativeBoardCodec.TryRead(packet, length, out decoded) && decoded!.SamePicture(state),
            "maximal anchors/quaternion/graphic/renderer/group/raw channels survive lossless paging");

        t.Case("native-element-render/component-presence-and-distinct-channels");
        hierarchy = Hierarchy(4, true);
        for (int e = 0; e < 6; e++)
        {
            NativeElementRenderNode image = hierarchy[e].Nodes[1];
            image.Flags = NativeElementRenderNode.Active | NativeElementRenderNode.Rect | NativeElementRenderNode.Graphic
                | NativeElementRenderNode.Image | NativeElementRenderNode.Renderer | NativeElementRenderNode.GraphicEnabled;
            image.GroupAlpha = 0; image.Uv = Array.Empty<float>(); image.Fill = 0.375f;
            image.Sprite = 2; image.OverrideSprite = 5;
            NativeElementRenderNode text = hierarchy[e].Nodes[2];
            text.Flags = NativeElementRenderNode.Active | NativeElementRenderNode.Rect | NativeElementRenderNode.Graphic
                | NativeElementRenderNode.Text | NativeElementRenderNode.Renderer | NativeElementRenderNode.GraphicEnabled;
            text.GroupAlpha = 0; text.Uv = Array.Empty<float>(); text.FontSize = 17.25f;
            NativeElementRenderNode transform = hierarchy[e].Nodes[3];
            transform.Flags = NativeElementRenderNode.Active; transform.Geometry = transform.Geometry.Take(10).ToArray();
            transform.Color = transform.RendererColor = transform.Uv = Array.Empty<float>(); transform.GroupAlpha = 0;
        }
        hierarchy[0].Nodes[0].Flags |= NativeElementRenderNode.GroupEnabled | NativeElementRenderNode.IgnoreParentGroups;
        state = Frame(hierarchy); length = NativeBoardCodec.Write(state, packet);
        t.True(NativeBoardCodec.TryRead(packet, length, out decoded) && decoded!.SamePicture(state),
            "Image, TMP and plain Transform component arities round-trip alongside independent RGBA multipliers");
        NativeElementRenderNode[] channel = decoded!.RenderElements![0].Nodes;
        t.True(channel[1].Sprite == 2 && channel[1].OverrideSprite == 5 && channel[1].Fill == 0.375f,
            "base sprite, override sprite and fill retain three independent owner values");
        t.True(channel[2].FontSize == 17.25f && channel[3].Geometry.Length == 10,
            "TMP font size is exact and non-Rect transforms contain exactly ten geometry values");
        t.True((channel[0].Flags & NativeElementRenderNode.GroupEnabled) != 0
            && (decoded.RenderElements[1].Nodes[0].Flags & NativeElementRenderNode.GroupEnabled) == 0,
            "enabled and disabled CanvasGroups retain the distinction with the same alpha");
        hierarchy[0].Nodes[0].Flags &= unchecked((ushort)~NativeElementRenderNode.GroupEnabled);
        t.True(!Frame(hierarchy).SamePicture(state), "a CanvasGroup enabled-only edge publishes a changed picture");
        foreach (Action<NativeElementRenderNode> mutate in new Action<NativeElementRenderNode>[] {
            n => n.Geometry[9] = 0f, n => n.Geometry[9] = 2f,
            n => n.Geometry = new float[17], n => n.Flags |= NativeElementRenderNode.GroupEnabled,
            n => n.RendererColor = new float[4], n => n.OverrideSprite = 1 })
        {
            NativeElementRenderState[] invalid = Hierarchy(1, false); mutate(invalid[0].Nodes[0]);
            bool refused = false;
            try { NativeElementRenderCodec.Write(invalid, packet, 0); }
            catch (ArgumentException) { refused = true; }
            t.True(refused, "write boundary rejects zero/nonunit rotations, wrong arity and state for absent components");
        }

        t.Case("native-element-render/whole-message-refusal");
        byte[] baseline = GoldenRaw();
        foreach (var mutation in new[] { (At: 0, Value: (byte)5), (At: 1, Value: (byte)33),
            (At: 2, Value: (byte)0), (At: 3, Value: (byte)32), (At: 4, Value: (byte)0),
            (At: 9, Value: (byte)128) })
        {
            byte[] bad = (byte[])baseline.Clone(); bad[mutation.At] = mutation.Value;
            byte[] encoded = old.Concat(Pages(bad)).ToArray();
            t.True(!NativeBoardCodec.TryRead(encoded, encoded.Length, out decoded) && decoded == null,
                "invalid element count, node bound, parent, sibling, identity or flags cannot publish partial52");
        }
        byte[] nan = (byte[])baseline.Clone(); nan[12] = 0xc0; nan[13] = 0x7f;
        byte[] invalidFloat = old.Concat(Pages(nan)).ToArray();
        t.True(!NativeBoardCodec.TryRead(invalidFloat, invalidFloat.Length, out decoded), "nonfinite hierarchy geometry refuses");
        byte[] hidden = old.Concat(Pages(baseline, compressed: true, tail: true)).ToArray();
        t.True(!NativeBoardCodec.TryRead(hidden, hidden.Length, out decoded) && decoded == null, "hidden Deflate tail in53 invalidates the entire frame");
        byte[] bomb = old.Concat(Pages(new byte[26001], compressed: true, rawLength: baseline.Length)).ToArray();
        t.True(!NativeBoardCodec.TryRead(bomb, bomb.Length, out decoded), "53 decompression cannot expand past claimed raw length");
        byte[] canonical = old.Concat(Pages(baseline)).ToArray();
        for (int trim = 1; trim <= 8; trim++)
            t.True(!NativeBoardCodec.TryRead(canonical, canonical.Length - trim, out decoded) && decoded == null,
                "a truncated53 tail cannot expose an otherwise valid52 prefix");
        byte[] reordered = (byte[])canonical.Clone();
        Buffer.BlockCopy(canonical, oldLength + 246, reordered, oldLength, 246);
        Buffer.BlockCopy(canonical, oldLength, reordered, oldLength + 246, 246);
        t.True(!NativeBoardCodec.TryRead(reordered, reordered.Length, out decoded), "reordered53 pages refuse");
        byte[] duplicate = (byte[])canonical.Clone(); duplicate[oldLength + 246 + 4] = 0;
        t.True(!NativeBoardCodec.TryRead(duplicate, duplicate.Length, out decoded), "duplicate53 offset refuses");
        byte[] trailing = canonical.Concat(new byte[] { 53, 5, 1, 0, 0, 0, 0 }).ToArray();
        t.True(!NativeBoardCodec.TryRead(trailing, trailing.Length, out decoded), "extra malformed record after complete53 refuses");
        t.Case("native-element-render/additive-tlv-compatibility");
        foreach (int offset in new[] { 6, oldLength, oldLength + 246, canonical.Length })
        {
            byte[] unknown = canonical.Take(offset).Concat(new byte[] { 240, 3, 52, 53, 255 })
                .Concat(canonical.Skip(offset)).ToArray();
            t.True(NativeBoardCodec.TryRead(unknown, unknown.Length, out decoded) && decoded!.SamePicture(Frame(Hierarchy(1, false))),
                "unknown TLV before/between/after known blocks and between pages preserves the same complete picture");
        }
        byte[] emptyUnknown = old.Concat(new byte[] { 240, 0 }).ToArray();
        t.True(NativeBoardCodec.TryRead(emptyUnknown, emptyUnknown.Length, out decoded) && decoded!.RenderElements == null,
            "zero-length unknown TLV does not invent hierarchy data for a legacy52 frame");
        foreach (byte[] tail in new[] { new byte[] { 240 }, new byte[] { 240, 3, 42 }, old.Skip(6).ToArray(), Pages(baseline) })
        {
            byte[] malformed = canonical.Concat(tail).ToArray();
            t.True(!NativeBoardCodec.TryRead(malformed, malformed.Length, out decoded) && decoded == null,
                "unknown truncated headers/payloads and repeated complete known blocks refuse atomically");
        }
        byte[] rootMismatch = (byte[])baseline.Clone(); rootMismatch[8] = 2; // root inactive, while52 says active
        byte[] disagree = old.Concat(Pages(rootMismatch)).ToArray();
        t.True(!NativeBoardCodec.TryRead(disagree, disagree.Length, out decoded), "52/53 root state disagreement refuses before publication");
    }
    private static NativeBoardState Frame(NativeElementRenderState[]? hierarchy)
    {
        var elements = new NativeElementState[6];
        for (int e = 0; e < 6; e++)
        {
            elements[e] = new NativeElementState { Flags = 1, Sibling = (byte)e,
                Graphics = Enumerable.Range(0, 7).Select(_ => new NativeElementGraphic()).ToArray(),
                Animations = new[] { Array.Empty<UseBarAnimationValue>(), Array.Empty<UseBarAnimationValue>(), Array.Empty<UseBarAnimationValue>() } };
            elements[e].Rect[3] = elements[e].Rect[4] = elements[e].Rect[5] = 1f;
        }
        return new NativeBoardState(2f, 15f, 3, elements, renderElements: hierarchy);
    }
    private static NativeElementRenderState[] Hierarchy(int count, bool full)
    {
        var result = new NativeElementRenderState[6];
        for (int e = 0; e < 6; e++)
        {
            var nodes = new NativeElementRenderNode[count];
            for (int i = 0; i < count; i++)
            {
                var n = nodes[i] = new NativeElementRenderNode { Parent = i == 0 ? (byte)255 : (byte)0,
                    Sibling = (byte)(i == 0 ? e : i - 1), Binding = (uint)(e + 1 + i * 6),
                    Flags = full ? (ushort)367 : (ushort)3 }; // active,rect,graphic,group,raw,renderer,graphicEnabled
                n.Geometry[3] = n.Geometry[4] = n.Geometry[5] = n.Geometry[9] = 1f;
                if (full)
                { n.Color = new[] { 0.3f, 0.6f, 0.9f, 0.7f }; n.RendererColor = new[] { 1f, 0.5f, 1f, 0.125f };
                    n.GroupAlpha = 0.375f; n.Uv = new[] { 0.2f, 0.3f, 0.75f, 0.5f }; }
            }
            result[e] = new NativeElementRenderState { Nodes = nodes };
        }
        return result;
    }
    private static byte[] GoldenRaw()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write((byte)6);
        for (int e = 0; e < 6; e++)
        {
            w.Write((byte)1); w.Write((byte)255); w.Write((byte)e); w.Write((uint)(e + 1)); w.Write((ushort)3);
            for (int g = 0; g < 18; g++) w.Write(g == 3 || g == 4 || g == 5 || g == 9 ? 1f : 0f);
        }
        return stream.ToArray();
    }
    private static byte[] Pages(byte[] raw, bool compressed = false, bool tail = false, int? rawLength = null)
    {
        byte[] data = raw;
        if (compressed)
        {
            using var m = new MemoryStream(); using (var deflate = new DeflateStream(m, CompressionLevel.Fastest, true)) deflate.Write(raw);
            data = m.ToArray(); if (tail) data = data.Concat(new byte[] { 0x42 }).ToArray();
        }
        using var p = new MemoryStream(); using var pw = new BinaryWriter(p);
        pw.Write((byte)(compressed ? 1 : 0)); pw.Write((ushort)(rawLength ?? raw.Length)); pw.Write(data); byte[] payload = p.ToArray();
        using var output = new MemoryStream(); using var w = new BinaryWriter(output);
        for (int at = 0; at < payload.Length; at += 240)
        { int n = Math.Min(240, payload.Length - at); w.Write((byte)53); w.Write((byte)(n + 4)); w.Write((ushort)payload.Length); w.Write((ushort)at); w.Write(payload, at, n); }
        return output.ToArray();
    }
    private static byte[] Unpack(byte[] packet, int length, byte id)
    {
        using var m = new MemoryStream();
        for (int at = 6; at < length; ) { int n = packet[at + 1] - 4; if (packet[at] == id) m.Write(packet, at + 6, n); at += n + 6; }
        byte[] payload = m.ToArray(); if (payload[0] == 0) return payload.Skip(3).ToArray();
        using var source = new MemoryStream(payload, 3, payload.Length - 3); using var inflater = new DeflateStream(source, CompressionMode.Decompress);
        using var raw = new MemoryStream(); inflater.CopyTo(raw); return raw.ToArray();
    }
}
