using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Public native end-results presentation, never an instruction to the rules engine.
/// Author is the original content author; a pose-only sender may be a different admitted mover.</summary>
internal sealed class ScenarioResultsState
{
    internal uint Key;
    internal ulong Revision, BaseRevision;
    internal float SampleTime;
    internal bool Visible, HasPresentation = true;
    internal RigPose Pose = new() { Rotation = Quaternion.identity };
    internal float Size = 1f, Reference = 1f;
    internal int Author;
    internal Vector3 Origin;
    internal Quaternion Rotation = Quaternion.identity;
    internal float Scale = 1f;
    internal Vector2 FrameSize = new(1920f, 1080f);
    internal SharedWindowMoveEntry? Move;
    internal ScenarioResultsPart[] Parts = Array.Empty<ScenarioResultsPart>();
}

internal sealed class ScenarioResultsPart
{
    internal ushort Id, ParentId;
    internal uint ParentBinding, Structure;
    internal string Address = string.Empty;
    internal TownServiceNode[] Nodes = Array.Empty<TownServiceNode>();
}

/// <summary>Independent GVR1 message29 / fragments30. TLV125 carries a complete immutable
/// picture, with exact number/string/value pools shared across all parts. No NPC lane/header,
/// texture bytes, previous packet dictionary, gameplay state or callback crosses this codec.</summary>
internal static class ScenarioResultsCodec
{
    internal const byte MessageType = 29, FragmentType = 30, RecordId = 125;
    internal const int MaxBytes = 60000, MaxRawBytes = 1024 * 1024, MaxParts = 64;
    private const int MaxNodes = 256, MaxValues = 4096, MaxStrings = 4096, MaxNumbers = 16384,
        MaxReferences = 65536, MaxTextBytes = 16384;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static byte[] Write(ScenarioResultsState state)
    {
        Validate(state);
        var pools = new Pools();
        foreach (ScenarioResultsPart part in state.Parts)
        {
            pools.String(part.Address);
            foreach (TownServiceNode node in part.Nodes)
                for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                    if (node.Values.TryGetValue(key, out TownServiceValue? value)) pools.Value(value);
        }
        using var raw = new LimitedStream(MaxRawBytes);
        using (var writer = new BinaryWriter(raw, Utf8, true))
        {
            writer.Write((byte)2);
            writer.Write((byte)((state.Visible ? 1 : 0) | (state.Move.HasValue ? 2 : 0) | (state.HasPresentation ? 4 : 0)));
            writer.Write(state.Key); writer.Write(state.Revision); writer.Write(state.BaseRevision); writer.Write(state.SampleTime); writer.Write(state.Author);
            Vector(writer, state.Pose.Position); QuaternionValue(writer, state.Pose.Rotation);
            writer.Write(state.Size); writer.Write(state.Reference);
            Vector(writer, state.Origin); QuaternionValue(writer, state.Rotation); writer.Write(state.Scale);
            writer.Write(state.FrameSize.x); writer.Write(state.FrameSize.y);
            if (state.Move is SharedWindowMoveEntry move) WriteMove(writer, move);
            writer.Write((ushort)pools.Numbers.Count); foreach (float number in pools.Numbers) writer.Write(number);
            writer.Write((ushort)pools.Strings.Count); foreach (string text in pools.Strings) WriteText(writer, text);
            writer.Write((ushort)pools.Values.Count);
            foreach (TownServiceValue value in pools.Values)
            {
                writer.Write((ushort)value.Numbers.Length);
                foreach (float number in value.Numbers) writer.Write(pools.NumberIndex[Bits(number)]);
                writer.Write((byte)value.Text.Length);
                foreach (string text in value.Text) writer.Write(pools.StringIndex[text]);
            }
            writer.Write((byte)state.Parts.Length);
            foreach (ScenarioResultsPart part in state.Parts)
            {
                writer.Write(part.Id); writer.Write(part.ParentId); writer.Write(part.ParentBinding);
                writer.Write(pools.StringIndex[part.Address]); writer.Write(part.Structure); writer.Write((ushort)part.Nodes.Length);
                foreach (TownServiceNode node in part.Nodes)
                {
                    writer.Write(node.Binding); writer.Write((byte)node.Values.Count);
                    for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                        if (node.Values.TryGetValue(key, out TownServiceValue? value))
                        { writer.Write((byte)key); writer.Write(pools.ValueIndex[value]); }
                }
            }
        }
        byte[] original = raw.ToArray(), stored = original; byte compression = 0;
        // Tiny pose/tombstone packets retain a simple deterministic raw grammar. Larger
        // complete pictures use ordinary Deflate only when it actually reduces the packet.
        if (original.Length >= 1024)
        {
            using var packed = new LimitedStream(MaxRawBytes);
            using (var deflate = new DeflateStream(packed, System.IO.Compression.CompressionLevel.Fastest, true))
                deflate.Write(original, 0, original.Length);
            if (packed.Length < original.Length) { stored = packed.ToArray(); compression = 1; }
        }
        int payload = 6 + stored.Length;
        int size = checked(6 + payload + 2 * ((payload + 254) / 255));
        if (size > MaxBytes) throw new InvalidDataException("Results picture exceeds the bounded independent stream.");
        var result = new byte[size];
        result[0] = 0x31; result[1] = 0x52; result[2] = 0x56; result[3] = 0x47; result[4] = 3; result[5] = MessageType;
        var body = new byte[payload]; body[0] = 1; body[1] = compression;
        uint length = (uint)original.Length;
        for (int i = 0; i < 4; i++) body[2 + i] = (byte)(length >> (8 * i));
        Buffer.BlockCopy(stored, 0, body, 6, stored.Length);
        for (int at = 6, offset = 0; offset < body.Length;)
        {
            int count = Math.Min(255, body.Length - offset); result[at++] = RecordId; result[at++] = (byte)count;
            Buffer.BlockCopy(body, offset, result, at, count); offset += count; at += count;
        }
        return result;
    }

    internal static bool TryRead(byte[] packet, int length, out ScenarioResultsState? state)
    {
        state = null;
        if (packet == null || length < 8 || length > packet.Length || length > MaxBytes
            || packet[0] != 0x31 || packet[1] != 0x52 || packet[2] != 0x56 || packet[3] != 0x47
            || packet[4] != 3 || packet[5] != MessageType) return false;
        try
        {
            using var payload = new LimitedStream(MaxBytes);
            for (int at = 6; at < length;)
            {
                if (at > length - 2) return false;
                byte tag = packet[at++]; int count = packet[at++];
                if (count > length - at) return false;
                if (tag == RecordId)
                { if (count == 0) return false; payload.Write(packet, at, count); }
                at += count;
            }
            byte[] envelope = payload.ToArray();
            if (envelope.Length < 7 || envelope[0] != 1 || envelope[1] > 1) return false;
            uint rawCount = (uint)(envelope[2] | envelope[3] << 8 | envelope[4] << 16 | envelope[5] << 24);
            if (rawCount == 0 || rawCount > MaxRawBytes) return false;
            byte[] raw;
            if (envelope[1] == 0)
            {
                if (envelope.Length - 6 != rawCount) return false;
                raw = new byte[(int)rawCount]; Buffer.BlockCopy(envelope, 6, raw, 0, raw.Length);
            }
            else
            {
                using var input = new ExactDeflateInput(envelope, 6, envelope.Length - 6);
                using var decoded = new LimitedStream((int)rawCount);
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress, true))
                {
                    var buffer = new byte[4096];
                    while (decoded.Length < rawCount)
                    {
                        int count = deflate.Read(buffer, 0, (int)Math.Min(buffer.Length, rawCount - decoded.Length));
                        if (count == 0) return false; decoded.Write(buffer, 0, count);
                    }
                    if (deflate.ReadByte() != -1 || input.ReadPastEnd || input.Position != input.Length) return false;
                }
                raw = decoded.ToArray();
            }
            using var stream = new MemoryStream(raw, false); using var reader = new BinaryReader(stream, Utf8, true);
            if (reader.ReadByte() != 2) return false;
            byte flags = reader.ReadByte(); if ((flags & ~7) != 0) return false;
            var picture = new ScenarioResultsState {
                Visible = (flags & 1) != 0, HasPresentation = (flags & 4) != 0,
                Key = reader.ReadUInt32(), Revision = reader.ReadUInt64(), BaseRevision = reader.ReadUInt64(), SampleTime = reader.ReadSingle(), Author = reader.ReadInt32(),
                Pose = new RigPose { Position = ReadVector(reader), Rotation = ReadQuaternion(reader) },
                Size = reader.ReadSingle(), Reference = reader.ReadSingle(), Origin = ReadVector(reader), Rotation = ReadQuaternion(reader), Scale = reader.ReadSingle(),
                FrameSize = new Vector2(reader.ReadSingle(), reader.ReadSingle()) };
            if ((flags & 2) != 0) picture.Move = ReadMove(reader);
            int countNumbers = reader.ReadUInt16(); if (countNumbers > MaxNumbers) return false;
            var numbers = new float[countNumbers]; for (int i = 0; i < numbers.Length; i++) { numbers[i] = reader.ReadSingle(); Finite(numbers[i]); }
            int countStrings = reader.ReadUInt16(); if (countStrings > MaxStrings) return false;
            var strings = new string[countStrings]; for (int i = 0; i < strings.Length; i++) strings[i] = ReadText(reader);
            int countValues = reader.ReadUInt16(); if (countValues > MaxValues) return false;
            var values = new TownServiceValue[countValues];
            for (int i = 0; i < values.Length; i++)
            {
                int n = reader.ReadUInt16(); if (n > 1024) return false;
                var value = new TownServiceValue { Numbers = new float[n] };
                for (int j = 0; j < n; j++) { int index = reader.ReadUInt16(); if (index >= numbers.Length) return false; value.Numbers[j] = numbers[index]; }
                int t = reader.ReadByte(); if (t > TownServiceFrame.MaxProperties) return false;
                value.Text = new string[t];
                for (int j = 0; j < t; j++) { int index = reader.ReadUInt16(); if (index >= strings.Length) return false; value.Text[j] = strings[index]; }
                values[i] = value;
            }
            int countParts = reader.ReadByte(); if (countParts > MaxParts) return false;
            picture.Parts = new ScenarioResultsPart[countParts]; int references = 0;
            for (int i = 0; i < picture.Parts.Length; i++)
            {
                var part = new ScenarioResultsPart { Id = reader.ReadUInt16(), ParentId = reader.ReadUInt16(), ParentBinding = reader.ReadUInt32() };
                int address = reader.ReadUInt16(); if (address >= strings.Length) return false;
                part.Address = strings[address]; part.Structure = reader.ReadUInt32();
                int nodes = reader.ReadUInt16(); if (nodes > MaxNodes) return false;
                part.Nodes = new TownServiceNode[nodes];
                for (int j = 0; j < nodes; j++)
                {
                    var node = new TownServiceNode { Binding = reader.ReadUInt32() }; int properties = reader.ReadByte();
                    references += properties; if (properties > TownServiceProperty.Last || references > MaxReferences) return false;
                    for (int v = 0; v < properties; v++)
                    {
                        ushort property = reader.ReadByte(); int index = reader.ReadUInt16();
                        if (property == 0 || property > TownServiceProperty.Last || index >= values.Length || node.Values.ContainsKey(property)) return false;
                        node.Values.Add(property, values[index]);
                    }
                    part.Nodes[j] = node;
                }
                picture.Parts[i] = part;
            }
            if (stream.Position != stream.Length) return false;
            Validate(picture); state = picture; return true;
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is OverflowException)
        { return false; }
    }

    internal static void Validate(ScenarioResultsState state)
    {
        if (state == null || state.Key == 0 || state.Revision == 0 || state.Author <= 0 || state.Parts == null || state.Parts.Length > MaxParts)
            throw new InvalidDataException("Invalid results identity or part count.");
        Finite(state.SampleTime); if (state.SampleTime < 0) throw new InvalidDataException("Invalid results sample time.");
        Finite(state.Pose.Position); Unit(state.Pose.Rotation); Finite(state.Origin); Unit(state.Rotation);
        Positive(state.Size); Positive(state.Reference); Positive(state.Scale); Positive(state.FrameSize.x); Positive(state.FrameSize.y);
        if (state.BaseRevision > state.Revision) throw new InvalidDataException("Results delta precedes its named complete baseline.");
        if (!state.HasPresentation && (state.BaseRevision != 0 || state.Parts.Length != 0 || !state.Move.HasValue)) throw new InvalidDataException("Pose-only results require an ordered move and no content.");
        if (state.Move is SharedWindowMoveEntry move && !ValidMove(move, state)) throw new InvalidDataException("Invalid results move context.");
        var parts = new Dictionary<ushort, ScenarioResultsPart>(); int references = 0;
        foreach (ScenarioResultsPart part in state.Parts)
        {
            if (part == null || part.Id == 0 || parts.ContainsKey(part.Id) || part.Structure == 0 || string.IsNullOrEmpty(part.Address)
                || part.Nodes == null || part.Nodes.Length > MaxNodes || state.BaseRevision == 0 && part.Nodes.Length == 0 || TextBytes(part.Address) > MaxTextBytes)
                throw new InvalidDataException("Invalid results part.");
            parts.Add(part.Id, part); var bindings = new HashSet<uint>();
            foreach (TownServiceNode node in part.Nodes)
            {
                if (node == null || node.Binding == 0 || !bindings.Add(node.Binding) || node.Values.Count > TownServiceProperty.Last)
                    throw new InvalidDataException("Invalid results node.");
                references += node.Values.Count; if (references > MaxReferences) throw new InvalidDataException("Too many results references.");
                foreach (var property in node.Values)
                {
                    TownServiceValue value = property.Value;
                    if (property.Key == 0 || property.Key > TownServiceProperty.Last || value == null || value.Numbers == null
                        || value.Numbers.Length > 1024 || value.Text == null || value.Text.Length > TownServiceFrame.MaxProperties)
                        throw new InvalidDataException("Invalid results property.");
                    foreach (float number in value.Numbers) Finite(number);
                    foreach (string text in value.Text) if (text == null || TextBytes(text) > MaxTextBytes) throw new InvalidDataException("Invalid results text.");
                }
            }
        }
        if (state.HasPresentation && state.Visible && !parts.ContainsKey(1)) throw new InvalidDataException("Visible results require the original root part1.");
        foreach (ScenarioResultsPart part in state.Parts)
        {
            if (part.Id == 1)
            { if (part.ParentId != 0 || part.ParentBinding != 0) throw new InvalidDataException("Invalid results root binding."); continue; }
            if (part.ParentId == 0 || !parts.TryGetValue(part.ParentId, out ScenarioResultsPart? parent) || part.ParentBinding == 0)
                throw new InvalidDataException("Missing results parent.");
            bool binding = state.BaseRevision != 0;
            if (!binding) foreach (TownServiceNode node in parent.Nodes) if (node.Binding == part.ParentBinding) { binding = true; break; }
            if (!binding) throw new InvalidDataException("Unknown results parent binding.");
            ushort at = part.Id;
            for (int depth = 0; at != 1; depth++)
            {
                if (depth >= state.Parts.Length || !parts.TryGetValue(at, out ScenarioResultsPart? ancestor) || ancestor.ParentId == 0)
                    throw new InvalidDataException("Cyclic or disconnected results parts.");
                at = ancestor.ParentId;
            }
        }
    }

    private static bool ValidMove(SharedWindowMoveEntry move, ScenarioResultsState state) => move.Kind == 7
        && move.Phase <= SharedWindowMovePhase.Automatic && move.ContentKey == state.Key
        && move.OpeningAuthor == state.Author && move.Opening != 0 && move.Sequence != 0
        && (move.Phase == SharedWindowMovePhase.Initial ? move.Claim == 0 : move.Claim != 0)
        && (move.Phase == SharedWindowMovePhase.Automatic ? (move.ParentClaim == 0 ? move.ParentAuthor == 0 : move.ParentAuthor > 0)
            : move.ParentClaim == 0 && move.ParentAuthor == 0);
    private static void WriteMove(BinaryWriter w, SharedWindowMoveEntry m)
    { w.Write(m.Kind); w.Write((byte)m.Phase); w.Write(m.ContentKey); w.Write(m.OpeningAuthor); w.Write(m.Opening); w.Write(m.Claim); w.Write(m.Sequence); w.Write(m.ParentClaim); w.Write(m.ParentAuthor); }
    private static SharedWindowMoveEntry ReadMove(BinaryReader r) => new() { Kind = r.ReadByte(), Phase = (SharedWindowMovePhase)r.ReadByte(), ContentKey = r.ReadUInt32(), OpeningAuthor = r.ReadInt32(), Opening = r.ReadUInt32(), Claim = r.ReadUInt32(), Sequence = r.ReadUInt32(), ParentClaim = r.ReadUInt32(), ParentAuthor = r.ReadInt32() };
    private static void Vector(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    private static void QuaternionValue(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    private static Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static Quaternion ReadQuaternion(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static void Finite(float v) { if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Non-finite results value."); }
    private static void Finite(Vector3 v) { Finite(v.x); Finite(v.y); Finite(v.z); }
    private static void Positive(float v) { Finite(v); if (v <= 0) throw new InvalidDataException("Nonpositive results geometry."); }
    private static void Unit(Quaternion q)
    { Finite(q.x); Finite(q.y); Finite(q.z); Finite(q.w); double n = q.x * (double)q.x + q.y * (double)q.y + q.z * (double)q.z + q.w * (double)q.w; if (Math.Abs(n - 1) > .01) throw new InvalidDataException("Nonunit results rotation."); }
    private static int TextBytes(string text)
    { try { return Utf8.GetByteCount(text); } catch (EncoderFallbackException ex) { throw new InvalidDataException("Invalid results UTF16 source text.", ex); } }
    private static void WriteText(BinaryWriter w, string text) { byte[] bytes = Utf8.GetBytes(text); w.Write((ushort)bytes.Length); w.Write(bytes); }
    private static string ReadText(BinaryReader r)
    { int n = r.ReadUInt16(); if (n > MaxTextBytes || n > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("Truncated results text."); return Utf8.GetString(r.ReadBytes(n)); }
    [StructLayout(LayoutKind.Explicit)] private struct FloatBits { [FieldOffset(0)] internal float Number; [FieldOffset(0)] internal int Value; }
    private static int Bits(float value) => new FloatBits { Number = value }.Value;
    private sealed class ExactValueComparer : IEqualityComparer<TownServiceValue>
    {
        internal static readonly ExactValueComparer Instance = new();
        public bool Equals(TownServiceValue? a, TownServiceValue? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Numbers.Length != b.Numbers.Length || a.Text.Length != b.Text.Length) return false;
            for (int i = 0; i < a.Numbers.Length; i++) if (Bits(a.Numbers[i]) != Bits(b.Numbers[i])) return false;
            for (int i = 0; i < a.Text.Length; i++) if (!string.Equals(a.Text[i], b.Text[i], StringComparison.Ordinal)) return false;
            return true;
        }
        public int GetHashCode(TownServiceValue value)
        { int hash = 17; foreach (float n in value.Numbers) hash = unchecked(hash * 31 + Bits(n)); foreach (string t in value.Text) hash = unchecked(hash * 31 + StringComparer.Ordinal.GetHashCode(t)); return hash; }
    }
    internal static bool SameValue(TownServiceValue a, TownServiceValue b) => ExactValueComparer.Instance.Equals(a, b);
    private sealed class Pools
    {
        internal readonly List<float> Numbers = new(); internal readonly Dictionary<int, ushort> NumberIndex = new();
        internal readonly List<string> Strings = new(); internal readonly Dictionary<string, ushort> StringIndex = new(StringComparer.Ordinal);
        internal readonly List<TownServiceValue> Values = new(); internal readonly Dictionary<TownServiceValue, ushort> ValueIndex = new(ExactValueComparer.Instance);
        internal void String(string value)
        { if (StringIndex.ContainsKey(value)) return; if (Strings.Count >= MaxStrings) throw new InvalidDataException("Results string pool exhausted."); StringIndex.Add(value, (ushort)Strings.Count); Strings.Add(value); }
        internal void Value(TownServiceValue value)
        {
            if (ValueIndex.ContainsKey(value)) return;
            if (Values.Count >= MaxValues) throw new InvalidDataException("Results value pool exhausted.");
            ValueIndex.Add(value, (ushort)Values.Count); Values.Add(value);
            foreach (float number in value.Numbers)
                if (!NumberIndex.ContainsKey(Bits(number)))
                { if (Numbers.Count >= MaxNumbers) throw new InvalidDataException("Results number pool exhausted."); NumberIndex.Add(Bits(number), (ushort)Numbers.Count); Numbers.Add(number); }
            foreach (string text in value.Text) String(text);
        }
    }
    private sealed class LimitedStream : Stream
    {
        private readonly MemoryStream _stream = new();
        private readonly int _limit; internal LimitedStream(int limit) => _limit = limit;
        internal byte[] ToArray() => _stream.ToArray();
        public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => _stream.Length;
        public override long Position { get => _stream.Position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        { if (count < 0 || Position > _limit - count) throw new InvalidDataException("Results stream bound exceeded."); _stream.Write(buffer, offset, count); }
        public override void WriteByte(byte value)
        { if (Position >= _limit) throw new InvalidDataException("Results stream bound exceeded."); _stream.WriteByte(value); }
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override void Flush() => _stream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _stream.Dispose(); base.Dispose(disposing); }
    }
    /// <summary>A one-byte input prevents DeflateStream read-ahead from silently swallowing
    /// trailing wire data. A request past the exact compressed end also rejects truncated streams.
    /// Compose Stream rather than inheriting MemoryStream: newer Span overloads must route
    /// through this same byte-array boundary on both .NET and the game's Mono framework.</summary>
    private sealed class ExactDeflateInput : Stream
    {
        private readonly MemoryStream _stream;
        internal bool ReadPastEnd;
        internal ExactDeflateInput(byte[] data, int offset, int length) => _stream = new MemoryStream(data, offset, length, false);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Position { get => _stream.Position; set => throw new NotSupportedException(); }
        public override long Length => _stream.Length;
        public override int Read(byte[] buffer, int offset, int count)
        { int read = _stream.Read(buffer, offset, Math.Min(count, 1)); if (count > 0 && read == 0) ReadPastEnd = true; return read; }
        public override int ReadByte() { int value = _stream.ReadByte(); if (value < 0) ReadPastEnd = true; return value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _stream.Dispose(); base.Dispose(disposing); }
    }

}
