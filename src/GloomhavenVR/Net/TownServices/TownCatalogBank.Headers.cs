using System;
using System.IO;
using System.IO.Compression;

namespace GloomhavenVR.Net.TownServices;

internal sealed partial class TownCatalogBank
{
    internal const int MaxRawHeaderBytes = 384 * 1024, MaxPackedHeaderBytes = 16 * 1024;
    internal const int MaxHeaderPayloadBytes = 11 + MaxPackedHeaderBytes;

    internal byte[] WriteHeaders(TownServiceFrame root)
    {
        Validate(root);
        if (Headers.Length == 0) return Array.Empty<byte>();
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Utf8, true))
            for (int i = 0; i < Headers.Length; i++)
            {
                TownServiceFrame header = Headers[i];
                TownServiceFrame stripped = TownServiceDelta.Retain(header); stripped.Visible = false;
                byte[] packet = TownServiceCodec.Write(stripped);
                if (raw.Length + 13 + packet.Length > MaxRawHeaderBytes)
                    throw new InvalidDataException("Original catalog headers exceed the raw bound.");
                writer.Write((uint)packet.Length); writer.Write(header.Visible); writer.Write(HeaderBaseKeys[i]); writer.Write(packet);
            }
        byte[] originals = raw.ToArray();
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true)) deflate.Write(originals, 0, originals.Length);
        using (var writer = new BinaryWriter(compressed, Utf8, true)) writer.Write(Checksum(originals));
        if (compressed.Length > MaxPackedHeaderBytes) throw new InvalidDataException("Original catalog headers exceed the packed bound.");
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Utf8, true))
        { writer.Write((byte)1); writer.Write((ushort)Headers.Length); writer.Write((uint)originals.Length); writer.Write((uint)compressed.Length); writer.Write(compressed.ToArray()); }
        return payload.ToArray();
    }

    internal void ReadHeaders(byte[] bytes, TownServiceFrame root)
    {
        if (bytes.Length < 16 || bytes.Length > MaxHeaderPayloadBytes) throw new InvalidDataException("Invalid catalog header payload.");
        using var payload = new MemoryStream(bytes, false); using var reader = new BinaryReader(payload, Utf8, true);
        byte version = reader.ReadByte(); int count = reader.ReadUInt16();
        uint rawLength = reader.ReadUInt32(), packedLength = reader.ReadUInt32();
        if (version != 1 || !Prepared || Updates.Length != 0 || count == 0 || count != Members.Length
            || rawLength < count * 21 || rawLength > MaxRawHeaderBytes || packedLength < 5
            || packedLength > MaxPackedHeaderBytes || packedLength != payload.Length - payload.Position)
            throw new InvalidDataException("Invalid bounded catalog header block.");
        byte[] packed = reader.ReadBytes((int)packedLength), raw = new byte[(int)rawLength];
        using (var compressed = new MemoryStream(packed, 0, packed.Length - 4, false))
        using (var deflate = new DeflateStream(compressed, CompressionMode.Decompress))
        {
            int at = 0;
            while (at < raw.Length)
            { int n = deflate.Read(raw, at, raw.Length - at); if (n == 0) throw new InvalidDataException("Truncated original catalog headers."); at += n; }
            if (deflate.ReadByte() != -1) throw new InvalidDataException("Catalog header inflation exceeds its bound.");
        }
        uint checksum = 0; for (int i = 0; i < 4; i++) checksum |= (uint)packed[packed.Length - 4 + i] << (8 * i);
        if (Checksum(raw) != checksum) throw new InvalidDataException("Corrupt original catalog headers.");
        using var originals = new MemoryStream(raw, false); using var records = new BinaryReader(originals, Utf8, true);
        var headers = new TownServiceFrame[count];
        var bases = new ulong[count];
        for (int i = 0; i < count; i++)
        {
            uint length = records.ReadUInt32(); byte visible = records.ReadByte();
            bases[i] = records.ReadUInt64();
            if (visible > 1 || length < 8 || length > TownServiceFrame.MaxBytes || length > originals.Length - originals.Position)
                throw new InvalidDataException("Invalid original catalog header length.");
            byte[] packet = records.ReadBytes((int)length);
            // Explicitly reject both nested bank and nested header tables before parsing.
            if (!TownServiceCodec.TryReadCore(packet, packet.Length, out var header, allowBank: false)
                || header!.Visible) throw new InvalidDataException("Invalid stripped original catalog header.");
            header.Visible = visible != 0; headers[i] = header;
        }
        if (originals.Position != originals.Length) throw new InvalidDataException("Trailing original catalog header data.");
        Headers = headers; HeaderBaseKeys = bases; Validate(root);
    }
}
