using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GloomhavenVR.Net;

/// <summary>Additive record96 canonical pages, atomic with the unchanged52/53 snapshot. Bounded
/// original rules text/geometry only; no actor/card/quest identity or private goal can enter.</summary>
internal static class NativeBoardRulesCodec
{
    internal const byte RecordId = NetProtocol.ExtIdBoardRules;
    internal const int BodyMax = 60000, Chunk = 240;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static int Write(NativeBoardRulesState state, byte[] output, int at)
    {
        if (!state.Validate()) throw new ArgumentException("Invalid native rules presentation.");
        byte[] raw;
        using (var memory = new MemoryStream())
        {
            using var writer = new BinaryWriter(memory, Utf8, true);
            writer.Write((byte)((state.Visible ? 1 : 0) | (state.Overflow ? 2 : 0) | (state.Expanded ? 4 : 0) | (state.Hover ? 8 : 0)));
            Floats(writer, state.Frame); String(writer, state.Caption); writer.Write((byte)state.Rows.Length);
            foreach (NativeBoardRuleRow row in state.Rows)
            {
                writer.Write((byte)row.Render.Nodes.Length);
                for (int i = 0; i < row.Render.Nodes.Length; i++)
                {
                    NativeElementRenderNode n = row.Render.Nodes[i];
                    writer.Write(n.Parent); writer.Write(n.Sibling); writer.Write(n.Binding); writer.Write(n.Flags);
                    Floats(writer, n.Geometry);
                    if ((n.Flags & NativeElementRenderNode.Graphic) != 0) Floats(writer, n.Color);
                    if ((n.Flags & NativeElementRenderNode.Renderer) != 0) Floats(writer, n.RendererColor);
                    if ((n.Flags & NativeElementRenderNode.Group) != 0) writer.Write(n.GroupAlpha);
                    if ((n.Flags & NativeElementRenderNode.Image) != 0) { writer.Write(n.Fill); writer.Write(n.Sprite); writer.Write(n.OverrideSprite); }
                    if ((n.Flags & NativeElementRenderNode.Raw) != 0) Floats(writer, n.Uv);
                    if ((n.Flags & NativeElementRenderNode.Text) != 0)
                    {
                        writer.Write(n.FontSize); NativeBoardRuleText t = row.Text[i]; String(writer, t.Text);
                        writer.Write(t.Alignment); writer.Write(t.Style); writer.Write(t.Overflow); writer.Write(t.Flags); Floats(writer, t.StyleValues);
                    }
                }
            }
            writer.Flush(); if (memory.Length > BodyMax) throw new ArgumentException("Native rules presentation exceeds bounded record.");
            raw = memory.ToArray();
        }
        byte[] packed;
        using (var memory = new MemoryStream())
        { using (var stream = new DeflateStream(memory, CompressionLevel.Fastest, true)) stream.Write(raw, 0, raw.Length); packed = memory.ToArray(); }
        bool compressed = packed.Length < raw.Length; byte[] data = compressed ? packed : raw;
        var payload = new byte[data.Length + 3]; payload[0] = compressed ? (byte)1 : (byte)0;
        payload[1] = (byte)raw.Length; payload[2] = (byte)(raw.Length >> 8); Buffer.BlockCopy(data, 0, payload, 3, data.Length);
        int required = payload.Length + ((payload.Length + Chunk - 1) / Chunk) * 6;
        if (output == null || at < 0 || required > output.Length - at) throw new ArgumentException("Native rules output is too small.");
        for (int offset = 0; offset < payload.Length; offset += Chunk)
        {
            int count = Math.Min(Chunk, payload.Length - offset); output[at++] = RecordId; output[at++] = (byte)(count + 4);
            output[at++] = (byte)payload.Length; output[at++] = (byte)(payload.Length >> 8);
            output[at++] = (byte)offset; output[at++] = (byte)(offset >> 8);
            Buffer.BlockCopy(payload, offset, output, at, count); at += count;
        }
        return at;
    }
    internal static bool TryRead(byte[] input, int length, out NativeBoardRulesState? state)
    {
        state = null;
        if (input == null || length < 10 || length > input.Length || length > BodyMax + 3 + 6 * ((BodyMax + 3 + Chunk - 1) / Chunk)) return false;
        int at = 0, filled = 0; byte[]? payload = null;
        while (at < length)
        {
            if (at + 6 > length || input[at++] != RecordId) return false;
            int size = input[at++], total = input[at++] | input[at++] << 8, offset = input[at++] | input[at++] << 8;
            int count = size - 4;
            if (count <= 0 || count > Chunk || count > length - at || total < 4 || total > BodyMax + 3
                || offset != filled || offset + count > total || count != Math.Min(Chunk, total - offset)) return false;
            payload ??= new byte[total]; if (payload.Length != total) return false;
            Buffer.BlockCopy(input, at, payload, filled, count); at += count; filled += count;
        }
        if (payload == null || filled != payload.Length || payload[0] > 1) return false;
        int rawSize = payload[1] | payload[2] << 8;
        if (rawSize < 56 || rawSize > BodyMax) return false;
        try
        {
            byte[] raw = new byte[rawSize];
            if (payload[0] == 0)
            { if (payload.Length != rawSize + 3) return false; Buffer.BlockCopy(payload, 3, raw, 0, rawSize); }
            else
            {
                using var source = new NativeBoardCodec.ExactDeflateInput(payload, 3, payload.Length - 3);
                using var inflate = new DeflateStream(source, CompressionMode.Decompress); int read = 0;
                while (read < rawSize) { int n = inflate.Read(raw, read, rawSize - read); if (n == 0) return false; read += n; }
                if (inflate.ReadByte() != -1 || source.Position != source.Length) return false;
            }
            using var memory = new MemoryStream(raw, false); using var reader = new BinaryReader(memory, Utf8);
            byte flags = reader.ReadByte(); if ((flags & ~15) != 0) return false;
            var result = new NativeBoardRulesState { Visible = (flags & 1) != 0, Overflow = (flags & 2) != 0,
                Expanded = (flags & 4) != 0, Hover = (flags & 8) != 0, Frame = Floats(reader, 13), Caption = String(reader) };
            int rows = reader.ReadByte(); if (rows > NativeBoardRulesState.RowsMax) return false;
            result.Rows = new NativeBoardRuleRow[rows];
            for (int r = 0; r < rows; r++)
            {
                int nodes = reader.ReadByte(); if (nodes == 0 || nodes > NativeElementRenderState.NodesMax) return false;
                var row = result.Rows[r] = new NativeBoardRuleRow { Render = new NativeElementRenderState { Nodes = new NativeElementRenderNode[nodes] }, Text = new NativeBoardRuleText[nodes] };
                for (int i = 0; i < nodes; i++)
                {
                    var n = row.Render.Nodes[i] = new NativeElementRenderNode { Parent = reader.ReadByte(), Sibling = reader.ReadByte(), Binding = reader.ReadUInt32(), Flags = reader.ReadUInt16() };
                    n.Geometry = Floats(reader, (n.Flags & NativeElementRenderNode.Rect) != 0 ? 18 : 10);
                    if ((n.Flags & NativeElementRenderNode.Graphic) != 0) n.Color = Floats(reader, 4);
                    if ((n.Flags & NativeElementRenderNode.Renderer) != 0) n.RendererColor = Floats(reader, 4);
                    if ((n.Flags & NativeElementRenderNode.Group) != 0) n.GroupAlpha = reader.ReadSingle();
                    if ((n.Flags & NativeElementRenderNode.Image) != 0) { n.Fill = reader.ReadSingle(); n.Sprite = reader.ReadUInt16(); n.OverrideSprite = reader.ReadUInt16(); }
                    if ((n.Flags & NativeElementRenderNode.Raw) != 0) n.Uv = Floats(reader, 4);
                    var t = row.Text[i] = new NativeBoardRuleText();
                    if ((n.Flags & NativeElementRenderNode.Text) != 0)
                    { n.FontSize = reader.ReadSingle(); t.Text = String(reader); t.Alignment = reader.ReadInt32(); t.Style = reader.ReadInt32(); t.Overflow = reader.ReadInt32(); t.Flags = reader.ReadByte(); t.StyleValues = Floats(reader, 9); }
                }
            }
            if (memory.Position != memory.Length || !result.Validate()) return false; state = result; return true;
        }
        catch (ArgumentException) { return false; }
        catch (IOException) { return false; }
    }
    private static void Floats(BinaryWriter writer, float[] values) { foreach (float f in values) writer.Write(f); }
    private static float[] Floats(BinaryReader reader, int count) { var values = new float[count]; for (int i = 0; i < count; i++) values[i] = reader.ReadSingle(); return values; }
    private static void String(BinaryWriter writer, string value)
    { byte[] bytes = Utf8.GetBytes(value); if (bytes.Length > NativeBoardRulesState.TextBytesMax) throw new ArgumentException("Rules text exceeds bound."); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    private static string String(BinaryReader reader)
    { int size = reader.ReadUInt16(); if (size > NativeBoardRulesState.TextBytesMax) throw new ArgumentException("Rules text exceeds bound."); byte[] bytes = reader.ReadBytes(size); if (bytes.Length != size) throw new EndOfStreamException(); return Utf8.GetString(bytes); }
}
