using System;
using System.Collections.Generic;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMotionCodec
{
    // TLV115 contains a bounded private representation of the complete97/113
    // packet. Restore every original byte before normal native validation. Kind129
    // exists only inside that representation, never in the original97 grammar.
    private const byte CompactReturnRootKind = 129;

    private readonly struct RootPackingKey : IEquatable<RootPackingKey>
    {
        private readonly byte _lane, _service;
        private readonly uint _session, _claim, _structure;
        private readonly ushort _module;
        internal RootPackingKey(byte lane, byte service, uint session, uint claim, ushort module, uint structure)
        { _lane = lane; _service = service; _session = session; _claim = claim; _module = module; _structure = structure; }
        internal RootPackingKey Member(ushort module, uint structure) => new(_lane, _service, _session, _claim, module, structure);
        public bool Equals(RootPackingKey other) => _lane == other._lane && _service == other._service
            && _session == other._session && _claim == other._claim && _module == other._module && _structure == other._structure;
        public override bool Equals(object? other) => other is RootPackingKey key && Equals(key);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _lane * 31 + _service;
                hash = hash * 31 + (int)_session;
                hash = hash * 31 + (int)_claim;
                hash = hash * 31 + _module;
                return hash * 31 + (int)_structure;
            }
        }
    }
    private sealed class RootPackingRecord
    {
        internal readonly byte Id;
        internal readonly byte[] Body;
        internal RootPackingRecord(byte id, byte[] body) { Id = id; Body = body; }
    }
    private sealed class RootPackingChild
    {
        internal readonly int Cohort, Member, Pose;
        internal readonly byte[] Body;
        internal RootPackingChild(int cohort, int member, byte[] body, int pose)
        { Cohort = cohort; Member = member; Body = body; Pose = pose; }
    }
    private static ushort RootU16(byte[] bytes, int at) => (ushort)(bytes[at] | bytes[at + 1] << 8);
    private static uint RootU32(byte[] bytes, int at) => unchecked((uint)(bytes[at] | bytes[at + 1] << 8
        | bytes[at + 2] << 16 | bytes[at + 3] << 24));
    private static RootPackingKey RootKey(byte[] bytes) => new(bytes[1], bytes[2], RootU32(bytes, 3),
        RootU32(bytes, 7), RootU16(bytes, 11), RootU32(bytes, 13));

    private static void RootVarUInt(BinaryWriter writer, uint value)
    {
        while (value >= 128) { writer.Write((byte)(value | 128)); value >>= 7; }
        writer.Write((byte)value);
    }
    private static uint ReadRootVarUInt(BinaryReader reader)
    {
        uint value = 0;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            byte next = reader.ReadByte();
            if (shift == 28 && next > 15) throw new InvalidDataException("Return-root XOR exceeds 32 bits.");
            value |= (uint)(next & 127) << shift;
            if (next >= 128) continue;
            if (shift != 0 && next == 0) throw new InvalidDataException("Noncanonical return-root XOR.");
            return value;
        }
        throw new InvalidDataException("Return-root XOR exceeds 32 bits.");
    }
    private static bool OriginalRootShape(byte[] bytes)
    {
        if (bytes.Length < 70 || bytes[0] != 1 || bytes[69] > 1) return false;
        int expected = 70;
        if (bytes[69] != 0)
        {
            if (bytes.Length < 71 || bytes[70] > 1) return false;
            expected += 1 + (bytes[70] == 0 ? 0 : 85);
        }
        return bytes.Length == expected;
    }

    // All identities come from this one packet. Parent, alpha, visibility, hand,
    // canvas flags and the complete optional85-byte canvas recipe remain literal.
    // Only duplicated identity fields and exact IEEE pose XOR words are compacted;
    // no floating arithmetic, hierarchy reconstruction or cross-packet cache exists.
    private static bool TryPackReturnRoots(byte[] original, bool inverse, out byte[] transformed)
    {
        transformed = original;
        if (original.Length < 23 || original.Length > MaxExpandedBytes || original[5] != MessageType
            || original[6] != RecordId || original[7] != 13 || original[8] != 0) return false;
        var records = new List<RootPackingRecord>();
        var cohorts = new List<byte[]>();
        byte[]? assembling = null;
        int filled = 0, logical = 0;
        for (int at = 21; at < original.Length;)
        {
            if (original.Length - at < 2) return false;
            byte record = original[at++];
            int count = original[at++];
            if (count == 0 || count > original.Length - at) return false;
            var body = new byte[count];
            Array.Copy(original, at, body, 0, count);
            records.Add(new RootPackingRecord(record, body));
            if (record == CardReturnCohortRecordId)
            {
                if (count <= 4) return false;
                int length = RootU16(body, 0), offset = RootU16(body, 2);
                if (length < 192 || length > MaxExpandedBytes || offset != filled) return false;
                assembling ??= new byte[length];
                if (assembling.Length != length || count - 4 > length - filled) return false;
                Array.Copy(body, 4, assembling, filled, count - 4);
                filled += count - 4;
                if (filled == length)
                {
                    if (assembling[0] != 10 || ++logical > MaxExpandedEntries) return false;
                    cohorts.Add(assembling);
                    assembling = null; filled = 0;
                }
            }
            else if (assembling != null || ++logical > MaxExpandedEntries) return false;
            at += count;
        }
        if (assembling != null) return false;
        var children = new Dictionary<RootPackingKey, RootPackingChild>();
        var references = new Dictionary<(int, int), RootPackingChild>();
        for (int groupIndex = 0; groupIndex < cohorts.Count; groupIndex++)
        {
            byte[] body = cohorts[groupIndex];
            if (body.Length < 140) return false;
            int members = body[138];
            if (members < 1 || members > 64 || body.Length < 140 + members * 6) return false;
            int parts = body[139 + members * 6], begin = 140 + members * 6;
            if (parts < 1 || parts > members || body.Length != begin + parts * 46) return false;
            RootPackingKey group = RootKey(body);
            int previous = -1;
            var memberKeys = new HashSet<(ushort, uint)>();
            for (int i = 0; i < members; i++)
                if (!memberKeys.Add((RootU16(body, 139 + i * 6), RootU32(body, 141 + i * 6)))) return false;
            for (int i = 0; i < parts; i++)
            {
                int part = begin + i * 46, index = body[part];
                if (index <= previous || index >= members) return false;
                previous = index;
                int member = 139 + index * 6;
                RootPackingKey key = group.Member(RootU16(body, member), RootU32(body, member + 2));
                var child = new RootPackingChild(groupIndex, index, body, part + 6);
                if (children.ContainsKey(key) || references.ContainsKey((groupIndex, index))) return false;
                children.Add(key, child); references.Add((groupIndex, index), child);
            }
        }
        int changed = 0;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(original, 0, 21); // Preserve the original97 source clock exactly.
        foreach (RootPackingRecord record in records)
        {
            byte[] body = record.Body;
            if (!inverse && record.Id == RecordId && body[0] == 1)
            {
                if (!OriginalRootShape(body)) return false;
                if (children.TryGetValue(RootKey(body), out RootPackingChild? child))
                {
                    using var compact = new MemoryStream(); using var part = new BinaryWriter(compact);
                    part.Write(CompactReturnRootKind); part.Write((byte)child.Cohort); part.Write((byte)child.Member);
                    part.Write(body, 17, 12);
                    ushort mask = 0;
                    var words = new uint[10];
                    for (int i = 0; i < words.Length; i++)
                    {
                        words[i] = RootU32(body, 29 + i * 4) ^ RootU32(child.Body, child.Pose + i * 4);
                        if (words[i] != 0) mask |= (ushort)(1 << i);
                    }
                    part.Write(mask);
                    foreach (uint word in words) if (word != 0) RootVarUInt(part, word);
                    part.Write(body, 69, body.Length - 69);
                    body = compact.ToArray(); changed++;
                }
            }
            else if (inverse && record.Id == RecordId && body[0] == CompactReturnRootKind)
            {
                if (body.Length < 18 || !references.TryGetValue((body[1], body[2]), out RootPackingChild? child)) return false;
                ushort mask = RootU16(body, 15);
                if ((mask & ~1023) != 0) return false;
                using var source = new MemoryStream(body, false); using var reader = new BinaryReader(source);
                source.Position = 17;
                using var restored = new MemoryStream(); using var part = new BinaryWriter(restored);
                part.Write((byte)1); part.Write(child.Body, 1, 10); part.Write(child.Body, 139 + child.Member * 6, 6);
                part.Write(body, 3, 12);
                for (int i = 0; i < 10; i++)
                {
                    uint xor = (mask & (1 << i)) == 0 ? 0 : ReadRootVarUInt(reader);
                    if ((mask & (1 << i)) != 0 && xor == 0) return false;
                    part.Write(RootU32(child.Body, child.Pose + i * 4) ^ xor);
                }
                part.Write(body, (int)source.Position, body.Length - (int)source.Position);
                body = restored.ToArray();
                if (!OriginalRootShape(body)) return false;
                changed++;
            }
            if (body.Length > 255) return false;
            writer.Write(record.Id); writer.Write((byte)body.Length); writer.Write(body);
            if (stream.Length > MaxExpandedBytes) return false;
        }
        transformed = stream.ToArray();
        return changed > 0;
    }
}
