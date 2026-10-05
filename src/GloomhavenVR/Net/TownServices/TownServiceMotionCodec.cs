using System;
using System.Collections.Generic;
using System.IO;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One rig-cadence numeric packet. It never constructs assets, changes membership or
/// grants gameplay authority. Every property retains its original module/binding affinity.</summary>
internal sealed class TownServiceMotionEntry
{
    internal byte Kind, Lane, Service, Hand;
    internal uint Session, PublicClaim, Structure, Binding, Revision;
    internal ushort Module, ParentModule, Property, Offset;
    internal float ParentAlpha = 1f, CommitAge;
    internal bool Visible, HasCanvasFrame, HasCanvasUpdate, CanvasOnHand, CueReady, HasSharedCue, SharedCueReady;
    internal float CueStrength, SharedCueStrength;
    internal float[] Pose = Array.Empty<float>(), CanvasPose = Array.Empty<float>(),
        CanvasRect = Array.Empty<float>(), CanvasSettings = Array.Empty<float>(), Numbers = Array.Empty<float>();
    internal int CanvasSortingOrder, CanvasSortingLayer, SharedGuideOwner;
    internal TownServiceMotionKey Key => new(Kind, Lane, Module, Kind is 2 or 4 ? Binding : 0, Kind == 5 ? (ushort)31 : Property, Offset);
}

internal readonly struct TownServiceMotionKey : IEquatable<TownServiceMotionKey>
{
    private readonly byte _kind, _lane; private readonly ushort _module, _property, _offset; private readonly uint _binding;
    internal TownServiceMotionKey(byte kind, byte lane, ushort module, uint binding, ushort property, ushort offset)
    { _kind = kind; _lane = lane; _module = module; _binding = binding; _property = property; _offset = offset; }
    public bool Equals(TownServiceMotionKey other) => _kind == other._kind && _lane == other._lane && _module == other._module
        && _binding == other._binding && _property == other._property && _offset == other._offset;
    public override bool Equals(object? other) => other is TownServiceMotionKey key && Equals(key);
    public override int GetHashCode() => unchecked((((((_kind * 31 + _lane) * 31 + _module) * 31 + (int)_binding) * 31 + _property) * 31) + _offset);
}

internal sealed class TownServiceMotionPacket
{
    internal ulong Sequence;
    internal float SampleTime;
    internal readonly List<TownServiceMotionEntry> Entries = new();
}

internal static class TownServiceMotionCodec
{
    internal const int MaxBytes = 864, MaxEntries = 32;
    // Build614: the paired hardware run exhausted the numeric lane (oldest dirty
    // state exceeded six seconds). Lossless packing removes repeated module/header
    // bytes without increasing the actual event size or its 15 Hz cadence. The
    // expanded payload is bounded independently; legacy record97 stays unchanged.
    internal const int MaxExpandedBytes = 8192, MaxExpandedEntries = 128;
    internal const byte PackedRecordId = 98, VisitorReadyRecordId = 99, ReturnRecordId = 106, CardReturnRecordId = 107;
    // Independent message rather than an art fragment: a several-second catalog
    // baseline must never sit in front of a visitor's current hand/hover/scroll.
    internal const byte MessageType = 26, RecordId = 97;
    internal const float SendInterval = 1f / 15f, Heartbeat = 1f;

    // Exact grammar size without streams or serialization allocations on the hot path.
    internal static int EntryBytes(TownServiceMotionEntry entry) => entry.Kind switch
    {
        1 => 72 + (entry.HasCanvasUpdate ? 1 + (entry.HasCanvasFrame ? 85 : 0) : 0),
        2 => 26 + entry.Numbers.Length * 4,
        3 => 17,
        4 => 28 + entry.Numbers.Length * 4,
        5 => 15 + (entry.HasSharedCue ? 9 : 0),
        6 => 10,
        7 => 112,
        8 => 176,
        _ => throw new InvalidDataException("Unknown fast motion kind.")
    };

    internal static byte[] Write(TownServiceMotionPacket packet) => WriteRaw(packet, MaxBytes, MaxEntries);

    internal static byte[]? TryWritePacked(TownServiceMotionPacket packet)
    {
        byte[] raw = WriteRaw(packet, MaxExpandedBytes, MaxExpandedEntries);
        if (raw.Length <= MaxBytes && packet.Entries.Count <= MaxEntries) return raw;
        byte[]? compressed = PresentationCompression.TryCompress(raw, raw.Length);
        if (compressed == null) return null;
        const int partBytes = 248;
        int parts = (compressed.Length + partBytes - 1) / partBytes;
        if (21 + compressed.Length + parts * 9 > MaxBytes) return null;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(raw, 0, 21); // the exact original record97 clock
        for (int at = 0; at < compressed.Length; at += partBytes)
        {
            int count = Math.Min(partBytes, compressed.Length - at);
            writer.Write(PackedRecordId); writer.Write((byte)(7 + count));
            writer.Write((byte)1); writer.Write((ushort)raw.Length);
            writer.Write((ushort)compressed.Length); writer.Write((ushort)at);
            writer.Write(compressed, at, count);
        }
        return stream.ToArray();
    }

    private static byte[] WriteRaw(TownServiceMotionPacket packet, int maxBytes, int maxEntries)
    {
        if (packet.Sequence == 0 || !Finite(packet.SampleTime) || packet.SampleTime < 0f
            || packet.Entries.Count == 0 || packet.Entries.Count > maxEntries)
            throw new InvalidDataException("Invalid fast town motion packet.");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0x47565231u); writer.Write((byte)3); writer.Write(MessageType);
        writer.Write(RecordId); writer.Write((byte)13); writer.Write((byte)0);
        writer.Write(packet.Sequence); writer.Write(packet.SampleTime);
        var keys = new HashSet<TownServiceMotionKey>();
        foreach (TownServiceMotionEntry entry in packet.Entries)
        {
            if (!keys.Add(entry.Key)) throw new InvalidDataException("Duplicate fast town motion entry.");
            using var body = new MemoryStream(); using var part = new BinaryWriter(body);
            WriteEntry(part, entry); byte[] bytes = body.ToArray();
            if (bytes.Length > 255) throw new InvalidDataException("Fast town motion entry exceeds its record.");
            writer.Write(entry.Kind == 6 ? VisitorReadyRecordId : entry.Kind == 7 ? ReturnRecordId : entry.Kind == 8 ? CardReturnRecordId : RecordId);
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }
        if (stream.Length > maxBytes) throw new InvalidDataException("Fast town motion exceeds one bounded event.");
        return stream.ToArray();
    }

    internal static bool TryRead(byte[] bytes, int length, out TownServiceMotionPacket? packet) =>
        TryReadCore(bytes, length, out packet, false);

    private static bool TryReadCore(byte[] bytes, int length, out TownServiceMotionPacket? packet, bool expanded)
    {
        packet = null;
        if (bytes == null || length < 23 || length > bytes.Length || length > (expanded ? MaxExpandedBytes : MaxBytes)
            || bytes[0] != 0x31 || bytes[1] != 0x52 || bytes[2] != 0x56 || bytes[3] != 0x47
            || bytes[4] != 3 || bytes[5] != MessageType) return false;
        try
        {
            using var stream = new MemoryStream(bytes, 6, length - 6, false);
            using var reader = new BinaryReader(stream);
            var result = new TownServiceMotionPacket(); var keys = new HashSet<TownServiceMotionKey>(); bool clock = false;
            byte[]? packed = null; int packedAt = 0, originalLength = 0;
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 2) return false;
                byte id = reader.ReadByte(), count = reader.ReadByte();
                if (count > stream.Length - stream.Position) return false;
                long end = stream.Position + count;
                if (id == PackedRecordId)
                {
                    if (expanded || !clock || result.Entries.Count != 0 || count <= 7 || reader.ReadByte() != 1) return false;
                    int original = reader.ReadUInt16(), total = reader.ReadUInt16(), at = reader.ReadUInt16();
                    if (original < PresentationCompression.MinimumInput || original > MaxExpandedBytes
                        || total < 5 || total > MaxBytes || at != packedAt) return false;
                    if (packed == null) { packed = new byte[total]; originalLength = original; }
                    if (packed.Length != total || originalLength != original || packedAt + count - 7 > total) return false;
                    int readCount = reader.Read(packed, packedAt, count - 7);
                    if (readCount != count - 7) return false;
                    packedAt += readCount;
                    continue;
                }
                if (id != RecordId && id != VisitorReadyRecordId && id != ReturnRecordId && id != CardReturnRecordId) { stream.Position = end; continue; }
                if (count == 0) return false;
                byte kind = reader.ReadByte();
                if ((id == VisitorReadyRecordId) != (kind == 6) || (id == ReturnRecordId) != (kind == 7) || (id == CardReturnRecordId) != (kind == 8)) return false;
                if (kind == 0)
                {
                    if (clock || result.Entries.Count != 0 || count != 13) return false;
                    clock = true; result.Sequence = reader.ReadUInt64(); result.SampleTime = reader.ReadSingle();
                    if (result.Sequence == 0 || !Finite(result.SampleTime) || result.SampleTime < 0f) return false;
                }
                else
                {
                    if (!clock || packed != null || result.Entries.Count >= (expanded ? MaxExpandedEntries : MaxEntries)) return false;
                    TownServiceMotionEntry entry = ReadEntry(reader, kind);
                    if (!keys.Add(entry.Key)) return false;
                    result.Entries.Add(entry);
                }
                if (stream.Position != end) return false;
            }
            if (packed != null)
            {
                if (packedAt != packed.Length) return false;
                byte[]? raw = PresentationCompression.Expand(packed, originalLength, MaxExpandedBytes, MessageType);
                if (raw == null || !TryReadCore(raw, raw.Length, out TownServiceMotionPacket? inner, true)
                    || inner!.Sequence != result.Sequence || inner.SampleTime != result.SampleTime) return false;
                packet = inner; return true;
            }
            if (!clock || result.Entries.Count == 0) return false;
            packet = result; return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OverflowException)
        { return false; }
    }

    private static void WriteEntry(BinaryWriter w, TownServiceMotionEntry e)
    {
        Validate(e); w.Write(e.Kind); w.Write(e.Lane); w.Write(e.Service); w.Write(e.Session);
        if (e.Kind == 6) { w.Write(e.CueReady); return; }
        if (e.Kind == 3) { w.Write(e.Revision); w.Write(e.CommitAge); return; }
        if (e.Kind == 5) { w.Write(e.CueReady); w.Write(e.CueStrength); w.Write(e.HasSharedCue);
          if (e.HasSharedCue) { w.Write(e.SharedCueReady); w.Write(e.SharedCueStrength); w.Write(e.SharedGuideOwner); } return; }
        w.Write(e.PublicClaim); w.Write(e.Module); w.Write(e.Structure);
        if (e.Kind is 7 or 8)
        { w.Write(e.Hand); w.Write(e.Revision); Floats(w, e.Numbers); return; }
        if (e.Kind == 1)
        {
            w.Write(e.ParentModule); w.Write(e.Binding); w.Write(e.ParentAlpha); w.Write(e.Visible);
            w.Write(e.Hand); Floats(w, e.Pose); w.Write(e.HasCanvasUpdate);
            if (e.HasCanvasUpdate) w.Write(e.HasCanvasFrame);
            if (e.HasCanvasUpdate && e.HasCanvasFrame)
            { w.Write(e.CanvasOnHand); Floats(w, e.CanvasPose); Floats(w, e.CanvasRect); Floats(w, e.CanvasSettings);
              w.Write(e.CanvasSortingOrder); w.Write(e.CanvasSortingLayer); }
        }
        else { w.Write(e.Binding); w.Write(e.Property); if (e.Kind == 4) w.Write(e.Offset);
          w.Write((byte)e.Numbers.Length); Floats(w, e.Numbers); }
    }

    private static TownServiceMotionEntry ReadEntry(BinaryReader r, byte kind)
    {
        var e = new TownServiceMotionEntry { Kind = kind, Lane = r.ReadByte(), Service = r.ReadByte(), Session = r.ReadUInt32() };
        if (kind == 6) { e.CueReady = Bool(r); Validate(e); return e; }
        if (kind == 3) { e.Revision = r.ReadUInt32(); e.CommitAge = r.ReadSingle(); Validate(e); return e; }
        if (kind == 5)
        { e.CueReady = Bool(r); e.CueStrength = r.ReadSingle(); e.HasSharedCue = Bool(r);
          if (e.HasSharedCue) { e.SharedCueReady = Bool(r); e.SharedCueStrength = r.ReadSingle(); e.SharedGuideOwner = r.ReadInt32(); }
          Validate(e); return e; }
        e.PublicClaim = r.ReadUInt32(); e.Module = r.ReadUInt16(); e.Structure = r.ReadUInt32();
        if (kind is 7 or 8)
        { e.Hand = r.ReadByte(); e.Revision = r.ReadUInt32(); e.Numbers = Floats(r, kind == 7 ? 22 : 38); Validate(e); return e; }
        if (kind == 1)
        {
            e.ParentModule = r.ReadUInt16(); e.Binding = r.ReadUInt32(); e.ParentAlpha = r.ReadSingle();
            e.Visible = Bool(r); e.Hand = r.ReadByte(); e.Pose = Floats(r, 10); e.HasCanvasUpdate = Bool(r);
            if (e.HasCanvasUpdate) e.HasCanvasFrame = Bool(r);
            if (e.HasCanvasUpdate && e.HasCanvasFrame)
            { e.CanvasOnHand = Bool(r); e.CanvasPose = Floats(r, 10); e.CanvasRect = Floats(r, 4); e.CanvasSettings = Floats(r, 5);
              e.CanvasSortingOrder = r.ReadInt32(); e.CanvasSortingLayer = r.ReadInt32(); }
        }
        else if (kind is 2 or 4)
        { e.Binding = r.ReadUInt32(); e.Property = r.ReadUInt16(); if (kind == 4) e.Offset = r.ReadUInt16(); int n = r.ReadByte();
          if (kind == 2 ? !TownServiceFastNumbers.Valid(e.Property, n) : !TownServiceFastNumbers.IsMaterial(e.Property) || n < 1 || n > 40)
              throw new InvalidDataException("Invalid fast native property.");
          e.Numbers = Floats(r, n); }
        else throw new InvalidDataException("Unknown fast town motion kind.");
        Validate(e); return e;
    }
    private static bool Bool(BinaryReader r)
    { byte v = r.ReadByte(); if (v > 1) throw new InvalidDataException("Invalid motion boolean."); return v != 0; }
    private static void Floats(BinaryWriter w, float[] values) { foreach (float v in values) w.Write(v); }
    private static float[] Floats(BinaryReader r, int n)
    { var values = new float[n]; for (int i = 0; i < n; i++) values[i] = r.ReadSingle(); return values; }
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    private static void CheckFloats(float[] values, int n)
    { if (values.Length != n) throw new InvalidDataException("Invalid fast motion shape.");
      foreach (float v in values) if (!Finite(v)) throw new InvalidDataException("Nonfinite fast motion value."); }
    private static void Validate(TownServiceMotionEntry e)
    {
        if (e.Kind < 1 || e.Kind > 8 || e.Lane > 2 || e.Service < 1 || e.Service > 3 || e.Session == 0
            || e.Lane != 0 && e.Service != 1) throw new InvalidDataException("Invalid fast town motion affinity.");
        if (e.Kind == 3)
        { if (e.Lane != 0 || e.Service != 2 || e.Revision == 0 || !Finite(e.CommitAge) || e.CommitAge < 0f || e.CommitAge > 30f)
              throw new InvalidDataException("Invalid fast donation clock."); return; }
        if (e.Kind == 6)
        { if (e.Lane != 0 || e.Service != 1) throw new InvalidDataException("Invalid merchant visitor readiness."); return; }
        if (e.Kind == 5)
        { if (e.Lane != 0 || e.Service != 3 || !Finite(e.CueStrength) || e.CueStrength < 0f || e.CueStrength > 1f
              || !Finite(e.SharedCueStrength) || e.SharedCueStrength < 0f || e.SharedCueStrength > 1f
              || e.HasSharedCue && (e.SharedCueReady ? e.SharedGuideOwner <= 0 : e.SharedGuideOwner != 0))
              throw new InvalidDataException("Invalid shared mage cue."); return; }
        if (e.Module >= TownServiceFrame.VoiceModule || e.Structure == 0 || e.Lane != 1 && e.PublicClaim != 0)
            throw new InvalidDataException("Invalid fast town module identity.");
        if (e.Kind == 8)
        {
            CheckFloats(e.Numbers, 38);
            if (e.Lane == 1 || e.Service == 2 || e.Hand is not (0 or 3 or 4) || e.Revision == 0
                || e.Numbers[0] < 0f || e.Numbers[0] > 4f || e.Numbers[1] <= 0f || e.Numbers[1] > 1f
                || e.Numbers[2] < 0f || e.Numbers[2] > 3f || e.Numbers[2] != (int)e.Numbers[2]
                || e.Numbers[3] < 0f || e.Numbers[3] > 100f || e.Numbers[27] != 0f)
                throw new InvalidDataException("Invalid original card return clock.");
            for (int i = 0; i < 3; i++)
            {
                int pose = i == 2 ? 28 : 4 + i * 10;
                float q = 0f; for (int n = 3; n <= 6; n++) q += e.Numbers[pose + n] * e.Numbers[pose + n];
                if (q < .99f || q > 1.01f || e.Numbers[pose + 7] <= 0f
                    || e.Numbers[pose + 8] <= 0f || e.Numbers[pose + 9] <= 0f)
                    throw new InvalidDataException("Invalid original card return pose.");
            }
            return;
        }
        if (e.Kind == 7)
        {
            CheckFloats(e.Numbers, 22);
            if (e.Lane != 0 || e.Service != 2 || e.Hand is not (3 or 4) || e.Revision == 0
                || e.Numbers[0] < 0f || e.Numbers[0] > 4f || e.Numbers[1] <= 0f || e.Numbers[1] > 1f)
                throw new InvalidDataException("Invalid purse return clock.");
            for (int at = 2; at <= 12; at += 10)
            {
                float q = 0f; for (int n = 3; n <= 6; n++) q += e.Numbers[at + n] * e.Numbers[at + n];
                if (q < .99f || q > 1.01f || e.Numbers[at + 7] <= 0f
                    || e.Numbers[at + 8] <= 0f || e.Numbers[at + 9] <= 0f)
                    throw new InvalidDataException("Invalid purse return pose.");
            }
            return;
        }
        if (e.Kind == 1)
        {
            CheckFloats(e.Pose, 10);
            if (e.Hand > 4 || e.CanvasOnHand && e.Hand == 0 || !Finite(e.ParentAlpha) || e.ParentAlpha < 0f || e.ParentAlpha > 1f)
                throw new InvalidDataException("Invalid fast root visibility.");
            if (e.HasCanvasUpdate && e.HasCanvasFrame) { CheckFloats(e.CanvasPose, 10); CheckFloats(e.CanvasRect, 4); CheckFloats(e.CanvasSettings, 5); }
        }
        else
        { if (e.Kind == 2 ? !TownServiceFastNumbers.Valid(e.Property, e.Numbers.Length)
              : !TownServiceFastNumbers.IsMaterial(e.Property) || e.Numbers.Length != 1
                || e.Offset > 640 || !TownServiceFastNumbers.MaterialNumber(e.Offset))
              throw new InvalidDataException("Invalid fast numeric property.");
          CheckFloats(e.Numbers, e.Numbers.Length); }
    }
}
