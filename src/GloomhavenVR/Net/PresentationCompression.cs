using System;
using System.IO;
using System.IO.Compression;

namespace GloomhavenVR.Net;

/// <summary>Lossless, bounded compression of an entire original presentation packet.</summary>
internal static class PresentationCompression
{
    internal const int MinimumInput = 512, MinimumSaving = 64;

    internal static byte[]? TryCompress(byte[] original, int length, bool optimal = false)
    {
        if (original == null || length < MinimumInput || length > original.Length) return null;
        using var output = new MemoryStream();
        using (var compressor = new DeflateStream(output, optimal ? CompressionLevel.Optimal : CompressionLevel.Fastest, true))
            compressor.Write(original, 0, length);
        // The checksum guards corruption independently of the presentation codec. Its bytes
        // are part of the declared compressed length and must arrive before any UI can apply.
        uint checksum = Checksum(original, length);
        for (int b = 0; b < 4; b++) output.WriteByte((byte)(checksum >> (8 * b)));
        if (output.Length > length - MinimumSaving) return null;
        return output.ToArray();
    }

    internal static byte[]? Expand(byte[] compressed, int expectedLength, int limit, byte payloadType)
    {
        if (compressed == null || compressed.Length < 5 || expectedLength < MinimumInput
            || expectedLength > limit || compressed.Length > expectedLength - MinimumSaving) return null;
        var result = new byte[expectedLength];
        try
        {
            using var input = new MemoryStream(compressed, 0, compressed.Length - 4, false);
            using var decoder = new DeflateStream(input, CompressionMode.Decompress);
            int at = 0;
            while (at < result.Length)
            {
                int n = decoder.Read(result, at, result.Length - at);
                if (n == 0) return null;
                at += n;
            }
            // Never allocate according to the deflate stream. One additional byte detects
            // an expansion bomb even when its prefix has exactly the advertised size.
            if (decoder.ReadByte() != -1) return null;
        }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
        uint expected = 0;
        for (int b = 0; b < 4; b++) expected |= (uint)compressed[compressed.Length - 4 + b] << (8 * b);
        return expected == Checksum(result, result.Length)
            && NetPacket.PeekType(result, result.Length) == payloadType ? result : null;
    }

    private static readonly uint[] CrcTable = CreateCrcTable();
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0u);
            table[i] = value;
        }
        return table;
    }
    private static uint Checksum(byte[] bytes, int length)
    {
        uint crc = 0xffffffff;
        for (int i = 0; i < length; i++)
            crc = CrcTable[(crc ^ bytes[i]) & 255] ^ (crc >> 8);
        return ~crc;
    }
}
