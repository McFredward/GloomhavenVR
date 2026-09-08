using System;
using System.IO;
using System.IO.Compression;

namespace GloomhavenVR.Net;

/// <summary>Complete message10 frame; canonical record52 pages carry one bounded body. Transport
/// message11 reassembly is separate. No partial body or malformed tail can publish a frame.</summary>
internal static class NativeBoardCodec
{
    internal const int MaxSize = 12288;
    internal const byte MessageType = 10, FragmentType = 11, RecordId = 52;
    private const int BodyMax = 11000, ChunkBytes = 240;

    internal static int Write(NativeBoardState state, byte[] buffer)
    {
        if (state == null || buffer == null || buffer.Length < MaxSize) throw new ArgumentException("Invalid native board buffer.");
        // Revalidate publication arrays at the wire boundary; no truncated fallback state.
        _ = new NativeBoardState(state.SampleTime, state.InitiativeDepthPixels, state.Generation, state.Elements, state.Frame);
        var body = new byte[BodyMax]; int at = 0;
        AvatarSerializer.WriteF32(body, ref at, state.SampleTime);
        AvatarSerializer.WriteF32(body, ref at, state.InitiativeDepthPixels);
        AvatarSerializer.WriteU32(body, ref at, state.Generation);
        body[at++] = (byte)state.Elements.Length;
        foreach (float value in state.Frame) AvatarSerializer.WriteF32(body, ref at, value);
        foreach (NativeElementState element in state.Elements)
        {
            body[at++] = element.Flags; body[at++] = element.Sibling;
            foreach (float value in element.Rect) AvatarSerializer.WriteF32(body, ref at, value);
            foreach (NativeElementGraphic value in element.Graphics) WriteGraphic(body, ref at, value);
            body[at++] = (byte)element.Effects.Length;
            foreach (NativeElementGraphic value in element.Effects) WriteGraphic(body, ref at, value);
            foreach (UseBarAnimationValue[] set in element.Animations)
            {
                body[at++] = (byte)set.Length;
                foreach (UseBarAnimationValue value in set)
                {
                    body[at++] = value.SettingIndex; body[at++] = (byte)value.Kind;
                    foreach (float number in value.Values) AvatarSerializer.WriteF32(body, ref at, number);
                }
            }
        }
        int rawLength = at;
        byte[] packed;
        using (var compressed = new MemoryStream())
        {
            using (var stream = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                stream.Write(body, 0, rawLength);
            packed = compressed.ToArray();
        }
        bool useDeflate = packed.Length < rawLength;
        int total = 3 + (useDeflate ? packed.Length : rawLength), output = 0;
        var payload = new byte[total]; int header = 0;
        payload[header++] = useDeflate ? (byte)1 : (byte)0;
        U16(payload, ref header, rawLength);
        Buffer.BlockCopy(useDeflate ? packed : body, 0, payload, header, total - header);
        AvatarSerializer.WriteU32(buffer, ref output, NetProtocol.Magic);
        buffer[output++] = NetProtocol.Version; buffer[output++] = MessageType;
        for (int offset = 0; offset < total; offset += ChunkBytes)
        {
            int count = Math.Min(ChunkBytes, total - offset);
            buffer[output++] = RecordId; buffer[output++] = (byte)(count + 4);
            U16(buffer, ref output, total); U16(buffer, ref output, offset);
            Buffer.BlockCopy(payload, offset, buffer, output, count); output += count;
        }
        return output;
    }

    internal static bool TryRead(byte[] buffer, int length, out NativeBoardState? state)
    {
        state = null;
        if (buffer == null || length < 15 || length > buffer.Length || length > MaxSize) return false;
        int at = 0;
        if (AvatarSerializer.ReadU32(buffer, ref at) != NetProtocol.Magic
            || buffer[at++] != NetProtocol.Version || buffer[at++] != MessageType) return false;
        byte[]? body = null; int filled = 0;
        while (at < length)
        {
            if (at + 6 > length || buffer[at++] != RecordId) return false;
            int bytes = buffer[at++];
            if (bytes <= 4 || bytes > ChunkBytes + 4 || at + bytes > length) return false;
            int total = U16(buffer, ref at), offset = U16(buffer, ref at), count = bytes - 4;
            if (total < 4 || total > BodyMax + 3 || offset != filled || offset + count > total
                || count != Math.Min(ChunkBytes, total - offset)) return false;
            if (body == null) body = new byte[total];
            if (body.Length != total) return false;
            Buffer.BlockCopy(buffer, at, body, filled, count); at += count; filled += count;
        }
        if (body == null || filled != body.Length) return false;
        try
        {
            int header = 1;
            int rawLength = U16(body, ref header);
            if (rawLength < 61 || rawLength > BodyMax || body[0] > 1) return false;
            var raw = new byte[rawLength];
            if (body[0] == 0)
            {
                if (body.Length != rawLength + 3) return false;
                Buffer.BlockCopy(body, 3, raw, 0, rawLength);
            }
            else
            {
                using var input = new MemoryStream(body, 3, body.Length - 3, writable: false);
                using var inflater = new DeflateStream(input, CompressionMode.Decompress);
                int read = 0;
                while (read < rawLength)
                {
                    int countRead = inflater.Read(raw, read, rawLength - read);
                    if (countRead == 0) return false;
                    read += countRead;
                }
                if (inflater.ReadByte() != -1) return false; // bounded expansion: stop after one extra byte
            }
            body = raw;
            int p = 0;
            float time = F32(body, ref p), depth = F32(body, ref p);
            Need(body, p, 5); uint generation = AvatarSerializer.ReadU32(body, ref p);
            int count = body[p++]; if (count != 0 && count != 6) return false;
            var frame = new float[12];
            for (int f = 0; f < frame.Length; f++) frame[f] = F32(body, ref p);
            var elements = new NativeElementState[count];
            for (int i = 0; i < count; i++)
            {
                Need(body, p, 2);
                var element = new NativeElementState { Flags = body[p++], Sibling = body[p++],
                    Graphics = new NativeElementGraphic[NativeElementState.GraphicCount],
                    Animations = new UseBarAnimationValue[NativeElementState.AnimationCount][] };
                for (int r = 0; r < element.Rect.Length; r++) element.Rect[r] = F32(body, ref p);
                for (int g = 0; g < element.Graphics.Length; g++) element.Graphics[g] = ReadGraphic(body, ref p);
                Need(body, p, 1); int fx = body[p++]; if (fx > NativeElementState.FxMax) return false;
                element.Effects = new NativeElementGraphic[fx];
                for (int f = 0; f < fx; f++) element.Effects[f] = ReadGraphic(body, ref p);
                for (int a = 0; a < element.Animations.Length; a++)
                {
                    Need(body, p, 1); int settings = body[p++]; if (settings > NativeElementState.SettingsMax) return false;
                    var values = element.Animations[a] = new UseBarAnimationValue[settings];
                    for (int k = 0; k < settings; k++)
                    {
                        Need(body, p, 2);
                        var value = new UseBarAnimationValue { SettingIndex = body[p++], Kind = (UseBarAnimationKind)body[p++] };
                        int n = UseBarAnimationValue.Components(value.Kind); if (n == 0) return false;
                        value.Values = new float[n];
                        for (int c = 0; c < n; c++) value.Values[c] = F32(body, ref p);
                        values[k] = value;
                    }
                }
                elements[i] = element;
            }
            if (p != body.Length) return false;
            state = new NativeBoardState(time, depth, generation, elements, frame); return true;
        }
        catch (ArgumentException) { return false; }
        catch (IndexOutOfRangeException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
    }
    private static void WriteGraphic(byte[] buffer, ref int at, NativeElementGraphic value)
    {
        buffer[at++] = value.Flags;
        AvatarSerializer.WriteF32(buffer, ref at, value.R); AvatarSerializer.WriteF32(buffer, ref at, value.G);
        AvatarSerializer.WriteF32(buffer, ref at, value.B); AvatarSerializer.WriteF32(buffer, ref at, value.A);
        AvatarSerializer.WriteF32(buffer, ref at, value.Fx);
    }
    private static NativeElementGraphic ReadGraphic(byte[] buffer, ref int at)
    {
        Need(buffer, at, 21);
        return new NativeElementGraphic { Flags = buffer[at++], R = F32(buffer, ref at), G = F32(buffer, ref at),
            B = F32(buffer, ref at), A = F32(buffer, ref at), Fx = F32(buffer, ref at) };
    }
    private static float F32(byte[] buffer, ref int at) { Need(buffer, at, 4); return AvatarSerializer.ReadF32(buffer, ref at); }
    private static void Need(byte[] buffer, int at, int count)
    { if (at < 0 || count > buffer.Length - at) throw new ArgumentException("Truncated native board body."); }
    private static void U16(byte[] buffer, ref int at, int value) { buffer[at++] = (byte)value; buffer[at++] = (byte)(value >> 8); }
    private static int U16(byte[] buffer, ref int at) { int value = buffer[at] | buffer[at + 1] << 8; at += 2; return value; }
}
