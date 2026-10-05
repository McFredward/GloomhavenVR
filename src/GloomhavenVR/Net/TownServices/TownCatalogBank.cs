using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Atomic original-content dependencies of one public cabinet clock. References
/// reuse complete original snapshots; changed snapshots travel inside the same packet.</summary>
internal sealed partial class TownCatalogBank
{
    // Actual native ItemCard captures exceed512KiB before price/body modules. Keep
    // inflation bounded without excluding the measured48-card original aggregate.
    internal const int MaxRawUpdateBytes = 4 * 1024 * 1024;
    internal const int MaxPackedUpdateBytes = 55 * 1024;
    internal const int MaxPayloadBytes = 14 + 10 * TownRackState.MaxMembers + MaxPackedUpdateBytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly uint[] CrcTable = CreateCrcTable();
    private EncodingCache _encoding = new();
    internal bool Prepared;
    internal TownCatalogBankMember[] Members = Array.Empty<TownCatalogBankMember>();
    internal TownServiceFrame[] Updates = Array.Empty<TownServiceFrame>();
    // Additive TLV103 reference clocks retain the current owner's original headers,
    // while every native node/property still comes from an exact content key.
    internal TownServiceFrame[] Headers = Array.Empty<TownServiceFrame>();
    internal ulong[] HeaderBaseKeys = Array.Empty<ulong>();

    internal TownCatalogBank Copy()
    {
        var result = new TownCatalogBank { Prepared = Prepared, Members = (TownCatalogBankMember[])Members.Clone(),
            Updates = new TownServiceFrame[Updates.Length], Headers = new TownServiceFrame[Headers.Length], HeaderBaseKeys = (ulong[])HeaderBaseKeys.Clone(), _encoding = _encoding };
        for (int i = 0; i < Updates.Length; i++) result.Updates[i] = TownServiceDelta.Copy(Updates[i]);
        for (int i = 0; i < Headers.Length; i++) result.Headers[i] = TownServiceDelta.Copy(Headers[i]);
        return result;
    }

    /// <summary>Published sampler nodes are immutable. Copy their headers/arrays without
    /// cloning every original property dictionary for each rack clock sample.</summary>
    internal TownCatalogBank Retain()
    {
        var result = new TownCatalogBank { Prepared = Prepared, Members = (TownCatalogBankMember[])Members.Clone(),
            Updates = new TownServiceFrame[Updates.Length], Headers = new TownServiceFrame[Headers.Length], HeaderBaseKeys = (ulong[])HeaderBaseKeys.Clone(), _encoding = _encoding };
        for (int i = 0; i < Updates.Length; i++) result.Updates[i] = TownServiceDelta.Retain(Updates[i]);
        for (int i = 0; i < Headers.Length; i++) result.Headers[i] = TownServiceDelta.Retain(Headers[i]);
        return result;
    }

    /// <summary>Canonical immutable native content, including the original external canvas
    /// and parent binding. Absolute pose, visibility, clock and lane remain separate.</summary>
    [ThreadStatic] private static MemoryStream? _contentScratch;

    internal static ulong ContentKey(TownServiceFrame complete)
    {
        if (complete == null || complete.BaseSequence != 0 || complete.CatalogBank != null
            || complete.Module >= TownServiceFrame.VoiceModule || complete.Nodes == null || complete.Nodes.Length == 0)
            throw new InvalidDataException("A catalog key requires a complete original module.");
        TownServiceCodec.Validate(complete);
        using var sha = SHA256.Create();
        // BinaryWriter emits thousands of tiny writes for a native card. Hashing each
        // four-byte write through CryptoStream dominated the final NPC publisher. Stage
        // the identical canonical bytes in a reusable thread-local buffer, then hash once.
        MemoryStream bytes = _contentScratch ??= new MemoryStream(32768);
        bytes.SetLength(0); bytes.Position = 0;
        {
            using (var writer = new BinaryWriter(bytes, Utf8, true))
            {
                writer.Write((byte)1); writer.Write(complete.Template); WriteText(writer, complete.TemplateAddress);
                writer.Write(complete.Structure); writer.Write(complete.ParentBinding); writer.Write(complete.HasCanvasFrame);
                foreach (float value in complete.CanvasRect) writer.Write(value);
                foreach (float value in complete.CanvasSettings) writer.Write(value);
                writer.Write(complete.CanvasSortingOrder); writer.Write(complete.CanvasSortingLayer);
                writer.Write((ushort)complete.Nodes.Length);
                foreach (TownServiceNode node in complete.Nodes)
                {
                    writer.Write(node.Binding); writer.Write((byte)node.Values.Count);
                    for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                    {
                        if (!node.Values.TryGetValue(key, out TownServiceValue? value)) continue;
                        writer.Write(key); writer.Write((ushort)value.Numbers.Length);
                        // The unchanged value pool compares floats numerically: +0/-0
                        // can share one entry. Hash that same canonical wire value.
                        foreach (float number in value.Numbers) writer.Write(number == 0f ? 0f : number);
                        writer.Write((byte)value.Text.Length);
                        foreach (string text in value.Text) WriteText(writer, text);
                    }
                }
            }
        }
        byte[] digest = sha.ComputeHash(bytes.GetBuffer(), 0, checked((int)bytes.Length));
        if (bytes.Capacity > MaxRawUpdateBytes) { bytes.Dispose(); _contentScratch = null; }
        ulong result = 0;
        for (int i = 0; i < 8; i++) result |= (ulong)digest[i] << (8 * i);
        return result == 0 ? 1UL : result; // Zero is reserved for an unknown original.
    }

    internal void Validate(TownServiceFrame root)
    {
        if (root == null || !root.PublicCatalog || root.VisitorStock || root.Service != 1
            || root.Module >= TownServiceFrame.VoiceModule || root.Rack == null || root.RackMember != null
            || root.TemplateAddress == null || !root.TemplateAddress.StartsWith("merchant.rack|", StringComparison.Ordinal)
            || Members == null || Updates == null || Headers == null || HeaderBaseKeys == null || Members.Length > TownRackState.MaxMembers
            || Updates.Length > Members.Length)
            throw new InvalidDataException("Catalog content belongs to a public merchant rack.");
        root.Rack.Validate(root.Module);
        if (Members.Length != root.Rack.Members.Length)
            throw new InvalidDataException("Catalog references must name exactly the rack dependencies.");
        var refs = new Dictionary<ushort, ulong>();
        for (int i = 0; i < Members.Length; i++)
        {
            TownCatalogBankMember member = Members[i];
            if (member.Id >= TownServiceFrame.VoiceModule || member.ContentKey == 0
                || member.Id != root.Rack.Members[i].Id || i > 0 && member.Id <= Members[i - 1].Id)
                throw new InvalidDataException("Invalid original catalog content reference.");
            refs.Add(member.Id, member.ContentKey);
        }
        var updates = new Dictionary<ushort, TownServiceFrame>();
        bool headers = Headers.Length != 0;
        if (headers && (!Prepared || Updates.Length != 0 || Headers.Length != Members.Length || HeaderBaseKeys.Length != Headers.Length)
            || !headers && HeaderBaseKeys.Length != 0)
            throw new InvalidDataException("Reference clocks need every current original header.");
        TownServiceFrame[] records = headers ? Headers : Updates;
        for (int i = 0; i < records.Length; i++)
        {
            TownServiceFrame update = records[i];
            if (update == null || update.CatalogBank != null || (!headers && update.BaseSequence != 0)
                || !update.PublicCatalog || update.VisitorStock || update.Service != root.Service
                || update.Session != root.Session || update.PublicClaim != root.PublicClaim
                || update.Module >= TownServiceFrame.VoiceModule || update.Rack != null
                || update.RackMember == null || update.RackMember.Rack != root.Module
                || update.RackMember.Turn != root.Rack.Turn || update.RackMember.Detached
                || i > 0 && update.Module <= records[i - 1].Module
                || !refs.TryGetValue(update.Module, out ulong expected))
                throw new InvalidDataException("Invalid complete original catalog update.");
            int slot = Array.FindIndex(root.Rack.Members, member => member.Id == update.Module);
            if (root.Rack.Members[slot].Detached || update.RackMember.Page != root.Rack.Members[slot].Page
                || update.ParentModule != TownServiceFrame.ManifestModule && update.ParentModule != root.Module
                    && !refs.ContainsKey(update.ParentModule))
                throw new InvalidDataException("Catalog update is outside its original rack hierarchy.");
            if (headers)
            {
                if (HeaderBaseKeys[i] == 0 ? update.Nodes.Length != 0 || update.BaseSequence != 0 : update.BaseSequence == 0)
                    throw new InvalidDataException("Reference headers require an explicit original patch base.");
                var probe = TownServiceDelta.Retain(update); probe.Visible = false;
                TownServiceCodec.Validate(probe);
            }
            else if (ContentKey(update) != expected)
                throw new InvalidDataException("Original catalog content key mismatch.");
            updates.Add(update.Module, update);
        }
        // Cached parents are resolved by the receiver. A cycle already present entirely
        // in this packet is never a valid original transform hierarchy.
        foreach (TownServiceFrame update in records)
        {
            var path = new HashSet<ushort>();
            TownServiceFrame current = update;
            while (updates.TryGetValue(current.ParentModule, out TownServiceFrame? parent))
            {
                if (!path.Add(current.Module)) throw new InvalidDataException("Cyclic catalog original hierarchy.");
                current = parent;
            }
        }
    }

    internal byte[] Write(TownServiceFrame root)
    {
        Validate(root);
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Utf8, true))
            foreach (TownServiceFrame update in Updates)
            {
                byte[] packet = TownServiceCodec.Write(update);
                if (raw.Length + 4 + packet.Length > MaxRawUpdateBytes)
                    throw new InvalidDataException("Original catalog updates exceed the raw bank bound.");
                writer.Write((uint)packet.Length); writer.Write(packet);
            }
        byte[] originals = raw.ToArray(), packed = Array.Empty<byte>();
        if (Updates.Length != 0)
        {
            // A heartbeat changes the outer rack clock, not its immutable original bank.
            // Compare every actual serialized byte, including dynamic original headers,
            // before reusing the private block; no mutable frame aliases enter the cache.
            EncodedBlock? encoded = _encoding.Block;
            if (encoded != null && SameBytes(encoded.Originals, originals)) packed = encoded.Packed;
            else
            {
                using var compressed = new MemoryStream();
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                    deflate.Write(originals, 0, originals.Length);
                using (var writer = new BinaryWriter(compressed, Utf8, true)) writer.Write(Checksum(originals));
                if (compressed.Length > MaxPackedUpdateBytes)
                    throw new InvalidDataException("Original catalog updates exceed the packed bank bound.");
                packed = compressed.ToArray();
                _encoding.Block = new EncodedBlock(originals, packed);
            }
        }
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Utf8, true))
        {
            writer.Write((byte)1); writer.Write(Prepared); writer.Write((ushort)Members.Length);
            foreach (TownCatalogBankMember member in Members) { writer.Write(member.Id); writer.Write(member.ContentKey); }
            writer.Write((ushort)Updates.Length); writer.Write((uint)originals.Length); writer.Write((uint)packed.Length);
            writer.Write(packed);
        }
        return bytes.ToArray();
    }

    internal static TownCatalogBank Read(byte[] bytes, TownServiceFrame root)
    {
        if (bytes == null || bytes.Length < 14 || bytes.Length > MaxPayloadBytes)
            throw new InvalidDataException("Invalid bounded catalog content payload.");
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, Utf8, true);
        if (reader.ReadByte() != 1) throw new InvalidDataException("Unknown catalog content version.");
        byte prepared = reader.ReadByte();
        int count = reader.ReadUInt16();
        if (prepared > 1 || count > TownRackState.MaxMembers || stream.Length - stream.Position < count * 10 + 10)
            throw new InvalidDataException("Invalid catalog content references.");
        var result = new TownCatalogBank { Prepared = prepared != 0, Members = new TownCatalogBankMember[count] };
        for (int i = 0; i < count; i++) result.Members[i] = new(reader.ReadUInt16(), reader.ReadUInt64());
        int updates = reader.ReadUInt16();
        uint rawLength = reader.ReadUInt32(), packedLength = reader.ReadUInt32();
        if (updates > count || rawLength > MaxRawUpdateBytes || packedLength > MaxPackedUpdateBytes
            || packedLength != stream.Length - stream.Position
            || (updates == 0 ? rawLength != 0 || packedLength != 0 : rawLength < updates * 12 || packedLength < 5))
            throw new InvalidDataException("Invalid bounded catalog update block.");
        result.Validate(root); // Check the public lane and exact reference set before inflating.
        if (updates == 0) return result;
        byte[] packed = reader.ReadBytes((int)packedLength);
        var raw = new byte[(int)rawLength];
        using (var compressed = new MemoryStream(packed, 0, packed.Length - 4, false))
        using (var deflate = new DeflateStream(compressed, CompressionMode.Decompress))
        {
            int offset = 0;
            while (offset < raw.Length)
            {
                int read = deflate.Read(raw, offset, raw.Length - offset);
                if (read == 0) throw new InvalidDataException("Truncated original catalog updates.");
                offset += read;
            }
            if (deflate.ReadByte() != -1) throw new InvalidDataException("Catalog inflation exceeds its declared bound.");
        }
        uint checksum = 0;
        for (int i = 0; i < 4; i++) checksum |= (uint)packed[packed.Length - 4 + i] << (8 * i);
        if (Checksum(raw) != checksum) throw new InvalidDataException("Corrupt original catalog update block.");
        using var originals = new MemoryStream(raw, false);
        using var frames = new BinaryReader(originals, Utf8, true);
        result.Updates = new TownServiceFrame[updates];
        for (int i = 0; i < updates; i++)
        {
            uint length = frames.ReadUInt32();
            if (length < 8 || length > TownServiceFrame.MaxBytes || length > originals.Length - originals.Position)
                throw new InvalidDataException("Invalid complete catalog original length.");
            byte[] packet = frames.ReadBytes((int)length);
            if (!TownServiceCodec.TryReadCore(packet, packet.Length, out TownServiceFrame? frame, allowBank: false))
                throw new InvalidDataException("Invalid or nested catalog original.");
            result.Updates[i] = frame!;
        }
        if (originals.Position != originals.Length) throw new InvalidDataException("Trailing catalog original data.");
        result.Validate(root); // Nothing is returned until every original passes its content key and stamp.
        result._encoding.Block = new EncodedBlock(raw, packed);
        return result;
    }

    private static void WriteText(BinaryWriter writer, string text)
    { byte[] value = Utf8.GetBytes(text); writer.Write((ushort)value.Length); writer.Write(value); }
    private static uint Checksum(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        return ~crc;
    }
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB88320u : 0u);
            table[i] = value;
        }
        return table;
    }
    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private sealed class EncodedBlock
    {
        internal readonly byte[] Originals, Packed;
        internal EncodedBlock(byte[] originals, byte[] packed) { Originals = originals; Packed = packed; }
    }
    // Copies made before the first encode also share the private cache. Replacing
    // its immutable block never changes any snapshot header/node/property value.
    private sealed class EncodingCache { internal EncodedBlock? Block; }
}

internal readonly struct TownCatalogBankMember
{
    internal readonly ushort Id;
    internal readonly ulong ContentKey;
    internal TownCatalogBankMember(ushort id, ulong contentKey) { Id = id; ContentKey = contentKey; }
}
