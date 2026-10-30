using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GloomhavenVR.WorldUI.MenuDepth;

internal static class Program
{
    private const string GoldenHex = "474d44310100200003000200030000000c00000090f3f8230c000000080000000000ffff0080002000c03930";
    private static int _checks, _refusals;

    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(label);
    }

    private static void Near(float actual, double expected, string label) => Check(Math.Abs(actual - expected) < .000002, label);

    private static MenuDepthHeightfield Positive(byte[] data, string label)
    {
        try { return MenuDepthHeightfield.Read(data); }
        catch (Exception error) { throw new InvalidOperationException(label + ": " + error.GetType().Name); }
    }

    private static void Refuse(byte[] data, string label)
    {
        _refusals++;
        try { MenuDepthHeightfield.Read(data); }
        catch (InvalidDataException) { Check(true, label); return; }
        throw new InvalidOperationException(label);
    }

    private static void InvalidSample(MenuDepthHeightfield field, float u, float v)
    {
        _refusals++;
        try { field.Sample(u, v); }
        catch (ArgumentOutOfRangeException) { Check(true, "nonfinite UV refused"); return; }
        throw new InvalidOperationException("nonfinite UV refused");
    }

    // Independent fixture encoder: golden identity bytes are fixed above, and this
    // helper only constructs dimension/refusal variants, never an expected decoder.
    private static byte[] Shape(int width, int height, int sourceWidth = 12, int sourceHeight = 8)
    {
        var bytes = new byte[32 + width * height * 2];
        "GMD1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), 32);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)height);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)(bytes.Length - 32));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), ZeroPayloadCrc(bytes.Length - 32));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)sourceWidth);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)sourceHeight);
        return bytes;
    }

    private static uint ZeroPayloadCrc(int count)
    {
        // Use a small table oracle, independently from the production bit-at-a-time reader.
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) == 0 ? value >> 1 : 0xedb88320u ^ (value >> 1);
            table[i] = value;
        }
        uint crc = uint.MaxValue;
        for (int i = 0; i < count; i++) crc = table[crc & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static byte[] Changed(byte[] original, int offset, uint value, int size = 4)
    {
        var copy = (byte[])original.Clone();
        if (size == 2) BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(offset), (ushort)value);
        else BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static void Golden()
    {
        byte[] bytes = Convert.FromHexString(GoldenHex);
        MenuDepthHeightfield field = Positive(bytes, "portable golden decode");
        Check(field.Width == 3 && field.Height == 2, "portable golden field dimensions");
        Check(field.SourceWidth == 12 && field.SourceHeight == 8, "portable golden native source dimensions");
        Near(field.Sample(0, 0), 0, "bottom-left sample remains original bottom-left");
        Near(field.Sample(.5f, 0), 1, "bottom-middle high value means near");
        Near(field.Sample(1, 0), 32768d / 65535, "bottom-right ushort little endian");
        Near(field.Sample(0, 1), 8192d / 65535, "top-left sample retains native vertical orientation");
        Near(field.Sample(.5f, 1), 49152d / 65535, "top-middle sample");
        Near(field.Sample(1, 1), 12345d / 65535, "top-right sample");
        Near(field.Sample(.25f, .5f), (0d + 65535 + 8192 + 49152) / (4 * 65535), "bilinear interior combines four original corners");
        Near(field.Sample(.5f, .5f), (65535d + 49152) / (2 * 65535), "bilinear exact column midpoint");
        Near(field.Sample(-9, -4), 0, "finite UV clamp retains bottom-left");
        Near(field.Sample(7, 3), 12345d / 65535, "finite UV clamp retains top-right");
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        { InvalidSample(field, value, .5f); InvalidSample(field, .5f, value); }
        bytes[32] = 255;
        Near(field.Sample(0, 0), 0, "parsed native samples do not retain a mutable input-buffer lease");
        foreach ((int w, int h) in new[] { (1, 1), (1, 2), (2, 1), (512, 1), (1, 512), (512, 512) })
        {
            MenuDepthHeightfield accepted = Positive(Shape(w, h, 1, 16384), "valid one-axis and bounded maximum fields accepted");
            Check(accepted.Width == w && accepted.Height == h && accepted.SourceWidth == 1 && accepted.SourceHeight == 16384,
                "valid source dimensions retain exact scalar metadata");
            Near(accepted.Sample(.5f, .5f), 0, "degenerate-axis interpolation remains defined");
        }
    }

    private static void Malformed()
    {
        byte[] golden = Convert.FromHexString(GoldenHex);
        for (int length = 0; length < golden.Length; length++) Refuse(golden.AsSpan(0, length).ToArray(), "every incomplete field is refused");
        Refuse(Changed(golden, 0, 0), "unknown magic is refused");
        Refuse(Changed(golden, 4, 2, 2), "unknown version is refused");
        Refuse(Changed(golden, 6, 33, 2), "wrong header size is refused");
        Refuse(Changed(golden, 12, 1), "lost inverse-depth flags are refused");
        Refuse(Changed(golden, 12, 2), "lost bottom-left flags are refused");
        Refuse(Changed(golden, 12, 7), "unknown format flags are refused");
        Refuse(Shape(0, 2), "zero field axis is refused");
        Refuse(Shape(2, 0), "zero field axis is refused");
        Refuse(Shape(513, 2), "field width above512 is refused");
        Refuse(Shape(2, 513), "field height above512 is refused");
        Refuse(Changed(golden, 8, ushort.MaxValue, 2), "unchecked huge dimensions are refused before allocation");
        Refuse(Changed(golden, 24, 0), "zero native source size is refused");
        Refuse(Changed(golden, 24, 16385), "native source width above16384 is refused");
        Refuse(Changed(golden, 28, 16385), "native source height above16384 is refused");
        Refuse(Changed(golden, 28, uint.MaxValue), "overflow native source metadata is refused");
        Refuse(Changed(golden, 16, 11), "wrong payload length is refused");
        Refuse(Changed(golden, 16, uint.MaxValue), "overflow payload metadata is refused");
        Refuse(Changed(golden, 20, 0), "wrong payload checksum is refused");
        var corruption = (byte[])golden.Clone(); corruption[33] ^= 128;
        Refuse(corruption, "actual payload corruption is refused by CRC");
        var trailing = new byte[golden.Length + 1]; golden.CopyTo(trailing, 0);
        Refuse(trailing, "trailing bytes are refused");
    }

    private static void Corpus(string assets)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "manifest.json")));
        JsonElement root = doc.RootElement;
        Check(root.GetProperty("status").GetString() == "complete" && root.GetProperty("format").GetString() == "GMD1", "actual complete shipping manifest");
        var expected = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (JsonElement image in root.GetProperty("images").EnumerateArray())
        {
            string file = image.GetProperty("depth_file").GetString()!;
            Check(expected.Add(file) && Path.GetFileName(file) == file && file.EndsWith(".gmd", StringComparison.Ordinal), "actual source filename remains bounded and unique");
            byte[] bytes = File.ReadAllBytes(Path.Combine(assets, file));
            Check(bytes.Length == image.GetProperty("depth_bytes").GetInt32(), "actual delivered file length");
            Check(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == image.GetProperty("depth_sha256").GetString(), "actual delivered field digest");
            MenuDepthHeightfield field = MenuDepthHeightfield.Read(bytes);
            JsonElement shape = image.GetProperty("depth_dimensions"), source = image.GetProperty("source_dimensions");
            Check(field.Width == shape[0].GetInt32() && field.Height == shape[1].GetInt32(), "actual declared field dimensions");
            Check(field.SourceWidth == source[0].GetInt32() && field.SourceHeight == source[1].GetInt32(), "actual native coordinate dimensions");
            int w = field.Width, h = field.Height;
            double At(int x, int y) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32 + 2 * (x + y * w))) / 65535d;
            Near(field.Sample(0, 0), At(0, 0), "actual bottom-left corner witness");
            Near(field.Sample(1, 0), At(w - 1, 0), "actual bottom-right corner witness");
            Near(field.Sample(0, 1), At(0, h - 1), "actual top-left corner witness");
            Near(field.Sample(1, 1), At(w - 1, h - 1), "actual top-right corner witness");
            // At exactly the centre the weights are equal. This parity-aware mean
            // is independent of the production arbitrary-UV bilinear implementation.
            int x0 = (w - 1) / 2, x1 = w / 2, y0 = (h - 1) / 2, y1 = h / 2;
            double center = (At(x0, y0) + At(x1, y0) + At(x0, y1) + At(x1, y1)) / 4;
            Near(field.Sample(.5f, .5f), center, "actual original bilinear center witness");
            total += bytes.Length;
        }
        Check(expected.Count == 515, "all515 actual native heightfields execute through production C#");
        foreach (string path in Directory.GetFiles(assets, "*.gmd")) Check(expected.Remove(Path.GetFileName(path)), "no unexpected corpus file");
        Check(expected.Count == 0, "every actual file is present");
        Console.WriteLine($"Actual production reader: 515 fields / {total} bytes; four corners and bilinear centre each.");
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected actual Story asset directory.");
            Golden(); Malformed(); Corpus(args[0]);
            Console.WriteLine($"Menu depth C# data: {_checks} assertions passed; {_refusals} malformed/nonfinite refusals.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }
}
