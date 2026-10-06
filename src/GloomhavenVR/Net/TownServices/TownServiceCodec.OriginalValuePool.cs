using System;
using System.Collections.Generic;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceCodec
{
    // Build629's hardware first picture required44 independently pooled originals,
    // including14 native option rows of12.5 KiB each. Concatenating their individual
    // tables exhausted the58 KiB bundle limit before compression. The observer
    // consequently waited28.557s for one coherent picture. Pool *exact* owner values
    // once across the immutable picture, without consulting localized observer
    // defaults or waiting for a previously delivered asset/baseline dictionary.
    internal const byte OriginalValuePoolRecordId = NetProtocol.ExtIdTownOriginalValuePool;
    internal const int MaxOriginalValuePoolFrames = 64;
    private const int MaxOriginalValuePoolEntries = 4096;
    private const int MaxOriginalValuePoolNodes = 16384;
    private const int MaxOriginalValuePoolReferences = 65536;
    private const int MaxOriginalValuePoolExpansion = 2 * 1024 * 1024;

    /// <summary>A bounded current bundle, with no dependency on an earlier packet.</summary>
    internal sealed class OriginalValuePoolBuilder
    {
        private readonly List<TownServiceFrame> _frames = new();
        private readonly List<byte[]> _headers = new();
        private readonly List<TownServiceValue> _values = new();
        private readonly Dictionary<TownServiceValue, ushort> _indices = new(TownServiceValueComparer.Instance);
        private readonly HashSet<ushort> _modules = new();
        private readonly List<TownServiceNode> _nodeValues = new();
        private readonly Dictionary<TownServiceNode, ushort> _nodeIndices = new(OriginalPoolNodeComparer.Instance);
        private readonly List<float> _numbers = new();
        private readonly Dictionary<float, ushort> _numberIndices = new();
        private readonly List<string> _strings = new();
        private readonly Dictionary<string, ushort> _stringIndices = new(StringComparer.Ordinal);
        private int _bytes = 11, _nodes, _references, _expanded;
        internal int Count => _frames.Count;

        internal bool TryAdd(byte[] packet)
        {
            if (_frames.Count >= MaxOriginalValuePoolFrames || !TryRead(packet, packet.Length, out TownServiceFrame? frame)
                || frame!.Module >= TownServiceFrame.VoiceModule || frame.CatalogBank != null || frame.Rack != null
                || _modules.Contains(frame.Module)) return false;
            if (_frames.Count != 0 && !SameOriginalPoolLane(_frames[0], frame)) return false;
            TownServiceFrame header = TownServiceDelta.Retain(frame);
            header.Visible = false; header.Nodes = Array.Empty<TownServiceNode>();
            byte[] headerBytes = AppendUnknownOriginalRecords(TownServiceCodec.Write(header), packet);
            var added = new HashSet<TownServiceValue>(TownServiceValueComparer.Instance);
            var addedNumbers = new HashSet<float>(); var addedStrings = new HashSet<string>(StringComparer.Ordinal);
            int bytes = 5 + headerBytes.Length + 2 * frame.Nodes.Length, references = 0;
            var addedNodes = new HashSet<TownServiceNode>(OriginalPoolNodeComparer.Instance);
            foreach (TownServiceNode node in frame.Nodes)
            {
                if (!_nodeIndices.ContainsKey(node) && addedNodes.Add(node)) bytes += 5 + node.Values.Count * 3;
                references += node.Values.Count;
                foreach (TownServiceValue value in node.Values.Values)
                    if (!_indices.ContainsKey(value) && !added.Contains(value))
                    {
                        bytes += 3 + value.Numbers.Length * 2 + value.Text.Length * 2;
                        foreach (float number in value.Numbers)
                            if (!_numberIndices.ContainsKey(number) && addedNumbers.Add(number)) bytes += 4;
                        foreach (string text in value.Text)
                            if (!_stringIndices.ContainsKey(text) && addedStrings.Add(text)) bytes += 2 + Utf8.GetByteCount(text);
                        added.Add(value);
                    }
            }
            int raw = _bytes + bytes;
            if (_nodes + frame.Nodes.Length > MaxOriginalValuePoolNodes
                || _references + references > MaxOriginalValuePoolReferences
                || _values.Count + added.Count > MaxOriginalValuePoolEntries
                || _nodeValues.Count + addedNodes.Count > MaxOriginalValuePoolNodes
                || _numbers.Count + addedNumbers.Count > MaxOriginalValuePoolNodes
                || _strings.Count + addedStrings.Count > MaxOriginalValuePoolEntries
                || _expanded + packet.Length > MaxOriginalValuePoolExpansion
                || 6 + raw + 2 * ((raw + 254) / 255) > TownServiceFrame.MaxBytes) return false;
            foreach (TownServiceNode node in frame.Nodes)
                for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                    if (node.Values.TryGetValue(key, out TownServiceValue? value) && !_indices.ContainsKey(value))
                    {
                        _indices.Add(value, checked((ushort)_values.Count)); _values.Add(value);
                        foreach (float number in value.Numbers)
                            if (!_numberIndices.ContainsKey(number)) { _numberIndices.Add(number, checked((ushort)_numbers.Count)); _numbers.Add(number); }
                        foreach (string text in value.Text)
                            if (!_stringIndices.ContainsKey(text)) { _stringIndices.Add(text, checked((ushort)_strings.Count)); _strings.Add(text); }
                    }
            foreach (TownServiceNode node in frame.Nodes)
                if (!_nodeIndices.ContainsKey(node)) { _nodeIndices.Add(node, checked((ushort)_nodeValues.Count)); _nodeValues.Add(node); }
            _frames.Add(frame); _headers.Add(headerBytes); _modules.Add(frame.Module);
            _bytes = raw; _nodes += frame.Nodes.Length; _references += references; _expanded += packet.Length;
            return true;
        }

        internal byte[] Write()
        {
            if (_frames.Count == 0) throw new InvalidDataException("An original-value bundle must contain an original module.");
            using var body = new MemoryStream(_bytes);
            using (var writer = new BinaryWriter(body, Utf8, true))
            {
                writer.Write((byte)1); writer.Write((ushort)_frames.Count); writer.Write((ushort)_values.Count);
                writer.Write((ushort)_numbers.Count); foreach (float number in _numbers) writer.Write(number);
                writer.Write((ushort)_strings.Count); foreach (string text in _strings) WriteText(writer, text);
                foreach (TownServiceValue value in _values)
                {
                    writer.Write((ushort)value.Numbers.Length);
                    foreach (float number in value.Numbers) writer.Write(_numberIndices[number]);
                    writer.Write((byte)value.Text.Length);
                    foreach (string text in value.Text) writer.Write(_stringIndices[text]);
                }
                writer.Write((ushort)_nodeValues.Count);
                foreach (TownServiceNode node in _nodeValues)
                {
                    writer.Write(node.Binding); writer.Write((byte)node.Values.Count);
                    for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                        if (node.Values.TryGetValue(key, out TownServiceValue? value))
                        { writer.Write((byte)key); writer.Write(_indices[value]); }
                }
                for (int i = 0; i < _frames.Count; i++)
                {
                    TownServiceFrame frame = _frames[i]; byte[] header = _headers[i];
                    writer.Write((ushort)header.Length); writer.Write(header); writer.Write(frame.Visible);
                    writer.Write((ushort)frame.Nodes.Length);
                    foreach (TownServiceNode node in frame.Nodes) writer.Write(_nodeIndices[node]);
                }
            }
            byte[] raw = body.ToArray();
            if (raw.Length != _bytes) throw new InvalidDataException("Original-value bundle accounting differs from its encoding.");
            var result = new byte[6 + raw.Length + 2 * ((raw.Length + 254) / 255)];
            result[0] = 0x31; result[1] = 0x52; result[2] = 0x56; result[3] = 0x47; result[4] = 3; result[5] = MessageType;
            for (int at = 6, offset = 0; offset < raw.Length;)
            {
                int count = Math.Min(255, raw.Length - offset);
                result[at++] = OriginalValuePoolRecordId; result[at++] = (byte)count;
                Buffer.BlockCopy(raw, offset, result, at, count); at += count; offset += count;
            }
            return result;
        }
    }

    // Additive fields unknown to this build remain byte-for-byte in their own
    // module header. Pooling is an encoding, never authority to discard future
    // appearance/identity fields. Known fields are validated by the ordinary codec.
    private static byte[] AppendUnknownOriginalRecords(byte[] target, byte[] original)
    {
        using var output = new MemoryStream(); output.Write(target, 0, target.Length);
        for (int at = 6; at < original.Length;)
        {
            byte id = original[at], count = original[at + 1];
            if (id == OriginalValuePoolRecordId || id == BundleRecordId)
                throw new InvalidDataException("Nested town picture grammars are not original headers.");
            bool known = id == RecordId || id == TownRackState.RecordId || id == WorkspaceClothRecordId
                || id == TempleInteractionRecordId || id == TransactionRecordId || id == DonationClockRecordId
                || id == CatalogLayoutRecordId || id == VisitorStockRecordId || id == CatalogBankRecordId
                || id == CatalogHeadersRecordId || id == NativeTemplateStateRecordId || id == VisibleCensusRecordId
                || id == TownCassetteMotion.RecordId || id == TownCassetteMotion.RollerRecordId;
            if (!known) output.Write(original, at, count + 2);
            at += count + 2;
        }
        return output.ToArray();
    }

    private sealed class OriginalPoolNodeComparer : IEqualityComparer<TownServiceNode>
    {
        internal static readonly OriginalPoolNodeComparer Instance = new();
        public bool Equals(TownServiceNode? a, TownServiceNode? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Binding != b.Binding || a.Values.Count != b.Values.Count) return false;
            foreach (var pair in a.Values)
                if (!b.Values.TryGetValue(pair.Key, out TownServiceValue? value)
                    || !TownServiceValueComparer.Instance.Equals(pair.Value, value)) return false;
            return true;
        }
        public int GetHashCode(TownServiceNode node)
        {
            int hash = unchecked((int)node.Binding);
            // Canonical property order makes the hash independent of Dictionary
            // insertion order. Equality still compares every owner float/string.
            for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                if (node.Values.TryGetValue(key, out TownServiceValue? value))
                    hash = unchecked(hash * 397 ^ key ^ TownServiceValueComparer.Instance.GetHashCode(value));
            return hash;
        }
    }

    private static bool SameOriginalPoolLane(TownServiceFrame a, TownServiceFrame b) =>
        a.Service == b.Service && a.Session == b.Session && a.VisitorStock == b.VisitorStock
        && a.PublicCatalog == b.PublicCatalog && a.PublicClaim == b.PublicClaim;

    private static bool HasOriginalValuePool(byte[] packet, int length)
    {
        for (int at = 6; at + 2 <= length;)
        { int id = packet[at++], count = packet[at++]; if (at + count > length) return false;
          if (id == OriginalValuePoolRecordId) return true; at += count; }
        return false;
    }

    private static bool TryReadOriginalValuePool(byte[] packet, int length, out byte[][]? packets)
    {
        packets = null;
        try
        {
            using var body = new MemoryStream();
            for (int at = 6; at < length;)
            {
                if (at + 2 > length) return false;
                int id = packet[at++], count = packet[at++];
                if (at + count > length || count == 0 && id == OriginalValuePoolRecordId) return false;
                // A pool is one atomic picture. Mixing another picture grammar or
                // module records would manufacture hidden extra dispatches.
                if (id == BundleRecordId || id == RecordId) return false;
                if (id == OriginalValuePoolRecordId) body.Write(packet, at, count);
                at += count;
            }
            body.Position = 0;
            using var reader = new BinaryReader(body, Utf8, true);
            if (body.Length < 5 || reader.ReadByte() != 1) return false;
            int frameCount = reader.ReadUInt16(), valueCount = reader.ReadUInt16();
            if (frameCount < 1 || frameCount > MaxOriginalValuePoolFrames || valueCount > MaxOriginalValuePoolEntries) return false;
            int numberCount = reader.ReadUInt16();
            if (numberCount > MaxOriginalValuePoolNodes || 4L * numberCount > body.Length - body.Position) return false;
            var numbers = new float[numberCount];
            for (int i = 0; i < numberCount; i++) { float number = reader.ReadSingle(); Finite(number); numbers[i] = number; }
            int stringCount = reader.ReadUInt16();
            if (stringCount > MaxOriginalValuePoolEntries || 2L * stringCount > body.Length - body.Position) return false;
            var strings = new string[stringCount]; for (int i = 0; i < stringCount; i++) strings[i] = ReadText(reader);
            var values = new TownServiceValue[valueCount];
            for (int i = 0; i < valueCount; i++)
            {
                int count = reader.ReadUInt16();
                if (count > 1024 || 2L * count + 1 > body.Length - body.Position) return false;
                var value = new TownServiceValue { Numbers = new float[count] };
                for (int n = 0; n < count; n++) { int index = reader.ReadUInt16(); if (index >= numbers.Length) return false; value.Numbers[n] = numbers[index]; }
                count = reader.ReadByte(); if (count > TownServiceFrame.MaxProperties || 2L * count > body.Length - body.Position) return false;
                value.Text = new string[count];
                for (int t = 0; t < count; t++) { int index = reader.ReadUInt16(); if (index >= strings.Length) return false; value.Text[t] = strings[index]; }
                values[i] = value;
            }
            int nodeCount = reader.ReadUInt16();
            if (nodeCount > MaxOriginalValuePoolNodes || 5L * nodeCount > body.Length - body.Position) return false;
            var nodeValues = new TownServiceNode[nodeCount]; int tableReferences = 0;
            for (int n = 0; n < nodeCount; n++)
            {
                var node = new TownServiceNode { Binding = reader.ReadUInt32() };
                int propertyCount = reader.ReadByte(); tableReferences += propertyCount;
                if (propertyCount > TownServiceProperty.Last || tableReferences > MaxOriginalValuePoolReferences
                    || 3L * propertyCount > body.Length - body.Position) return false;
                for (int p = 0; p < propertyCount; p++)
                {
                    byte key = reader.ReadByte(); int index = reader.ReadUInt16();
                    if (key == 0 || key > TownServiceProperty.Last || index >= values.Length || node.Values.ContainsKey(key)) return false;
                    node.Values.Add(key, values[index]);
                }
                nodeValues[n] = node;
            }
            var result = new byte[frameCount][]; var modules = new HashSet<ushort>();
            TownServiceFrame? first = null; int nodes = 0, references = 0, expanded = 0;
            for (int i = 0; i < frameCount; i++)
            {
                int count = reader.ReadUInt16();
                if (count < 8 || count > body.Length - body.Position) return false;
                byte[] header = reader.ReadBytes(count);
                if (!TryRead(header, header.Length, out TownServiceFrame? frame) || frame!.Visible || frame.Nodes.Length != 0
                    || frame.Module >= TownServiceFrame.VoiceModule || frame.CatalogBank != null || frame.Rack != null
                    || !modules.Add(frame.Module) || first != null && !SameOriginalPoolLane(first, frame)) return false;
                first ??= frame;
                byte visible = reader.ReadByte(); if (visible > 1) return false; frame.Visible = visible != 0;
                count = reader.ReadUInt16(); nodes += count;
                if (count > TownServiceFrame.MaxNodes || nodes > MaxOriginalValuePoolNodes
                    || 2L * count > body.Length - body.Position) return false;
                frame.Nodes = new TownServiceNode[count];
                for (int n = 0; n < count; n++)
                {
                    int index = reader.ReadUInt16(); if (index >= nodeValues.Length) return false;
                    frame.Nodes[n] = nodeValues[index]; references += nodeValues[index].Values.Count;
                    if (references > MaxOriginalValuePoolReferences) return false;
                }
                // The unchanged codec remains the per-module validator/grammar:
                // lane flags, canvas bounds, asset declarations and delta bases all
                // retain their existing rules, before any observer can see a member.
                result[i] = AppendUnknownOriginalRecords(TownServiceCodec.Write(frame), header);
                if (result[i].Length > TownServiceFrame.MaxBytes) return false;
                expanded += result[i].Length;
                if (expanded > MaxOriginalValuePoolExpansion) return false;
            }
            if (body.Position != body.Length) return false;
            packets = result; return true;
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is OverflowException)
        { return false; }
    }
}
