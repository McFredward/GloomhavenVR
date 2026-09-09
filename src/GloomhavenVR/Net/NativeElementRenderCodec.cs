using System;
using System.IO;
using System.IO.Compression;

namespace GloomhavenVR.Net;

/// <summary>Additive canonical record53 pages, appended to the unchanged complete record52 body.
/// Both records publish atomically as one message10 snapshot. No setting-kind domain is changed.</summary>
internal static class NativeElementRenderCodec
{
    internal const byte RecordId = 53;
    internal const int BodyMax = 26000;
    private const int Chunk = 240;

    internal static int Write(NativeElementRenderState[] elements, byte[] output, int at)
    {
        if (elements == null || elements.Length != 6 || output == null || at < 0 || at > output.Length)
            throw new ArgumentException("Invalid native element render write.");
        foreach (NativeElementRenderState element in elements)
            if (element == null || !element.Validate()) throw new ArgumentException("Invalid native element hierarchy.");
        var body = new byte[BodyMax]; int p = 0; body[p++] = 6;
        foreach (NativeElementRenderState element in elements)
        {
            body[p++] = (byte)element.Nodes.Length;
            foreach (NativeElementRenderNode node in element.Nodes)
            {
                body[p++] = node.Parent; body[p++] = node.Sibling;
                AvatarSerializer.WriteU32(body, ref p, node.Binding); U16(body, ref p, node.Flags);
                Floats(body, ref p, node.Geometry);
                if ((node.Flags & NativeElementRenderNode.Graphic) != 0) Floats(body, ref p, node.Color);
                if ((node.Flags & NativeElementRenderNode.Renderer) != 0) Floats(body, ref p, node.RendererColor);
                if ((node.Flags & NativeElementRenderNode.Group) != 0) AvatarSerializer.WriteF32(body, ref p, node.GroupAlpha);
                if ((node.Flags & NativeElementRenderNode.Image) != 0)
                { AvatarSerializer.WriteF32(body, ref p, node.Fill); U16(body, ref p, node.Sprite); U16(body, ref p, node.OverrideSprite); }
                if ((node.Flags & NativeElementRenderNode.Raw) != 0) Floats(body, ref p, node.Uv);
                if ((node.Flags & NativeElementRenderNode.Text) != 0) AvatarSerializer.WriteF32(body, ref p, node.FontSize);
            }
        }
        byte[] compressed;
        using (var memory = new MemoryStream())
        {
            using (var stream = new DeflateStream(memory, CompressionLevel.Fastest, true)) stream.Write(body, 0, p);
            compressed = memory.ToArray();
        }
        bool deflate = compressed.Length < p;
        var payload = new byte[3 + (deflate ? compressed.Length : p)]; int head = 0;
        payload[head++] = deflate ? (byte)1 : (byte)0; U16(payload, ref head, p);
        Buffer.BlockCopy(deflate ? compressed : body, 0, payload, 3, payload.Length - 3);
        for (int offset = 0; offset < payload.Length; offset += Chunk)
        {
            int count = Math.Min(Chunk, payload.Length - offset);
            output[at++] = RecordId; output[at++] = (byte)(count + 4);
            U16(output, ref at, payload.Length); U16(output, ref at, offset);
            Buffer.BlockCopy(payload, offset, output, at, count); at += count;
        }
        return at;
    }

    internal static bool TryRead(byte[] input, int length, ref int at, out NativeElementRenderState[]? elements)
    {
        elements = null; byte[]? payload = null; int filled = 0;
        while (at < length)
        {
            if (at + 6 > length || input[at++] != RecordId) return false;
            int size = input[at++];
            if (size <= 4 || size > Chunk + 4 || at + size > length) return false;
            int total = U16(input, ref at), offset = U16(input, ref at), count = size - 4;
            if (total < 4 || total > BodyMax + 3 || offset != filled || offset + count > total
                || count != Math.Min(Chunk, total - offset)) return false;
            payload ??= new byte[total];
            if (payload.Length != total) return false;
            Buffer.BlockCopy(input, at, payload, filled, count); at += count; filled += count;
        }
        if (payload == null || filled != payload.Length) return false;
        try
        {
            int header = 1, rawLength = U16(payload, ref header);
            if (payload[0] > 1 || rawLength < 7 || rawLength > BodyMax) return false;
            var raw = new byte[rawLength];
            if (payload[0] == 0)
            {
                if (payload.Length != rawLength + 3) return false;
                Buffer.BlockCopy(payload, 3, raw, 0, rawLength);
            }
            else
            {
                using var source = new NativeBoardCodec.ExactDeflateInput(payload, 3, payload.Length - 3);
                using var inflater = new DeflateStream(source, CompressionMode.Decompress);
                int read = 0;
                while (read < rawLength)
                {
                    int amount = inflater.Read(raw, read, rawLength - read); if (amount == 0) return false; read += amount;
                }
                if (inflater.ReadByte() != -1 || source.Position != source.Length) return false;
            }
            int p = 0; if (raw[p++] != 6) return false;
            var result = new NativeElementRenderState[6];
            for (int e = 0; e < 6; e++)
            {
                Need(raw, p, 1); int count = raw[p++];
                if (count == 0 || count > NativeElementRenderState.NodesMax) return false;
                var element = result[e] = new NativeElementRenderState { Nodes = new NativeElementRenderNode[count] };
                for (int i = 0; i < count; i++)
                {
                    Need(raw, p, 8);
                    var node = element.Nodes[i] = new NativeElementRenderNode { Parent = raw[p++], Sibling = raw[p++],
                        Binding = AvatarSerializer.ReadU32(raw, ref p), Flags = (ushort)U16(raw, ref p) };
                    node.Geometry = Floats(raw, ref p, (node.Flags & NativeElementRenderNode.Rect) != 0 ? 18 : 10);
                    if ((node.Flags & NativeElementRenderNode.Graphic) != 0) node.Color = Floats(raw, ref p, 4);
                    if ((node.Flags & NativeElementRenderNode.Renderer) != 0) node.RendererColor = Floats(raw, ref p, 4);
                    if ((node.Flags & NativeElementRenderNode.Group) != 0) node.GroupAlpha = Float(raw, ref p);
                    if ((node.Flags & NativeElementRenderNode.Image) != 0)
                    { node.Fill = Float(raw, ref p); Need(raw, p, 4); node.Sprite = (ushort)U16(raw, ref p); node.OverrideSprite = (ushort)U16(raw, ref p); }
                    if ((node.Flags & NativeElementRenderNode.Raw) != 0) node.Uv = Floats(raw, ref p, 4);
                    if ((node.Flags & NativeElementRenderNode.Text) != 0) node.FontSize = Float(raw, ref p);
                }
                if (!element.Validate()) return false;
            }
            if (p != raw.Length) return false;
            elements = result; return true;
        }
        catch (ArgumentException) { return false; }
        catch (IndexOutOfRangeException) { return false; }
        catch (IOException) { return false; }
    }
    private static void Floats(byte[] body, ref int at, float[] values)
    { foreach (float value in values) AvatarSerializer.WriteF32(body, ref at, value); }
    private static float[] Floats(byte[] body, ref int at, int count)
    { var values = new float[count]; for (int i = 0; i < count; i++) values[i] = Float(body, ref at); return values; }
    private static float Float(byte[] body, ref int at) { Need(body, at, 4); return AvatarSerializer.ReadF32(body, ref at); }
    private static void Need(byte[] body, int at, int count)
    { if (at < 0 || count > body.Length - at) throw new ArgumentException("Truncated native hierarchy."); }
    private static void U16(byte[] buffer, ref int at, int value) { buffer[at++] = (byte)value; buffer[at++] = (byte)(value >> 8); }
    private static int U16(byte[] buffer, ref int at) { int value = buffer[at] | buffer[at + 1] << 8; at += 2; return value; }
}
