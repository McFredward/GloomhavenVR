using System;
using System.IO;

namespace GloomhavenVR.Net;

/// <summary>Atomic per-slot record 50. Ordered internal TLV pages keep each payload within one
/// byte; transport fragmentation operates on the complete encoded snapshot independently.</summary>
internal static class NativeUseBarCodec
{
    internal const int MaxSize = 2048;
    internal const byte Record = 50;
    private const int ChunkSize = 253;

    internal static int Write(NativeUseBarSnapshot snapshot, byte[] buffer)
    {
        if (snapshot == null || buffer == null) return 0;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            w.Write(snapshot.SampleTime); w.Write(snapshot.Bar); w.Write(snapshot.Slot); w.Write(snapshot.State != null);
            if (snapshot.State != null)
            {
                NativeUseBarState s = snapshot.State;
                if (!s.Validate()) return 0;
                w.Write(s.ActorId); w.Write((byte)s.ModelKind); w.Write(s.ModelId.ToByteArray());
                w.Write(s.ModelIndex); w.Write(s.ModelCount); w.Write(s.SlotIdentity);
                WriteWidget(w, s.WidgetState); Bytes(w, s.InfuseIcons);
                w.Write((byte)s.Augments.Length);
                foreach (NativeUseBarAugment a in s.Augments)
                { w.Write(a.IconSymbol); w.Write(a.ConsumeSymbol); w.Write(a.Option); w.Write(a.Flags); Floats(w, a.ConsumePosition); }
                w.Write((byte)(s.HighlightColors.Length / 4)); Floats(w, s.HighlightColors);
                w.Write(s.PreviewOption); w.Write(s.PreviewVisible); w.Write((byte)s.Animations.Length);
                foreach (NativeUseBarAnimationEntry a in s.Animations)
                { w.Write(a.AnimatorIndex); w.Write(a.Value.SettingIndex); w.Write((byte)a.Value.Kind); Floats(w, a.Value.Values); }
            }
        }
        byte[] raw = stream.ToArray();
        int pages = (raw.Length + ChunkSize - 1) / ChunkSize;
        int size = raw.Length + 4 * pages;
        if (size > MaxSize || size > buffer.Length) return 0;
        int at = 0;
        for (int page = 0, offset = 0; page < pages; page++)
        {
            int count = Math.Min(ChunkSize, raw.Length - offset);
            buffer[at++] = Record; buffer[at++] = (byte)(count + 2);
            buffer[at++] = (byte)page; buffer[at++] = (byte)pages;
            Buffer.BlockCopy(raw, offset, buffer, at, count); at += count; offset += count;
        }
        return at;
    }

    internal static bool TryRead(byte[] buffer, int length, out NativeUseBarSnapshot? snapshot)
    {
        snapshot = null;
        if (buffer == null || length < 11 || length > buffer.Length || length > MaxSize) return false;
        try
        {
            using var stream = new MemoryStream();
            int at = 0, expected = 0, total = -1;
            while (at < length)
            {
                if (length - at < 4 || buffer[at++] != Record) return false;
                int payload = buffer[at++], page = buffer[at++], pages = buffer[at++];
                if (payload < 3 || payload > length - at + 2 || page != expected++ || pages == 0
                    || total >= 0 && pages != total || page >= pages || page < pages - 1 && payload != 255) return false;
                total = pages;
                stream.Write(buffer, at, payload - 2); at += payload - 2;
            }
            if (expected != total) return false;
            stream.Position = 0;
            using var r = new BinaryReader(stream);
            float time = r.ReadSingle(); byte bar = r.ReadByte(), slot = r.ReadByte();
            bool present = Bool(r); NativeUseBarState? state = null;
            if (present)
            {
                state = new NativeUseBarState { Bar = bar, Slot = slot, ActorId = r.ReadInt32(),
                    ModelKind = (NativeUseBarModelKind)r.ReadByte(), ModelId = new Guid(Exact(r, 16)),
                    ModelIndex = r.ReadUInt16(), ModelCount = r.ReadUInt16(), SlotIdentity = r.ReadUInt16(),
                    WidgetState = ReadWidget(r), InfuseIcons = Bytes(r, 32) };
                state.Augments = new NativeUseBarAugment[Count(r, 32)];
                for (int i = 0; i < state.Augments.Length; i++)
                    state.Augments[i] = new NativeUseBarAugment { IconSymbol = r.ReadByte(), ConsumeSymbol = r.ReadByte(),
                        Option = r.ReadByte(), Flags = r.ReadByte(), ConsumePosition = Floats(r, 3) };
                state.HighlightColors = Floats(r, Count(r, 32) * 4);
                state.PreviewOption = r.ReadByte(); state.PreviewVisible = Bool(r);
                state.Animations = new NativeUseBarAnimationEntry[Count(r, 16)];
                for (int i = 0; i < state.Animations.Length; i++)
                {
                    byte animator = r.ReadByte(), setting = r.ReadByte(); var kind = (UseBarAnimationKind)r.ReadByte();
                    int components = UseBarAnimationValue.Components(kind);
                    if (components == 0) return false;
                    state.Animations[i] = new NativeUseBarAnimationEntry { AnimatorIndex = animator,
                        Value = new UseBarAnimationValue { SettingIndex = setting, Kind = kind, Values = Floats(r, components) } };
                }
            }
            if (stream.Position != stream.Length) return false;
            snapshot = new NativeUseBarSnapshot(time, bar, slot, state); return true;
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is OverflowException)
        { return false; }
    }
    private static void WriteWidget(BinaryWriter w, UseBarWidgetState s)
    {
        w.Write(s.Slot); w.Write(s.Flags); w.Write(s.SlotAlpha); Bytes(w, s.ConsumeIcons);
        w.Write((byte)s.InlineOptions.Length);
        for (int i = 0; i < s.InlineOptions.Length; i++)
        { w.Write(s.InlineSlots[i]); w.Write(s.InlineOptions[i]); if (s.InlineOptions[i] == 255) w.Write(s.InlineNumbers[i]); }
        w.Write(s.ElementStates); Bytes(w, s.OptionStates);
    }
    private static UseBarWidgetState ReadWidget(BinaryReader r)
    {
        var s = new UseBarWidgetState { Slot = r.ReadByte(), Flags = r.ReadByte(), SlotAlpha = r.ReadByte(), ConsumeIcons = Bytes(r, 32) };
        int count = Count(r, 32); s.InlineSlots = new byte[count]; s.InlineOptions = new byte[count]; s.InlineNumbers = new short[count];
        for (int i = 0; i < count; i++)
        { s.InlineSlots[i] = r.ReadByte(); s.InlineOptions[i] = r.ReadByte(); if (s.InlineOptions[i] == 255) s.InlineNumbers[i] = r.ReadInt16(); }
        s.ElementStates = Exact(r, 6); s.OptionStates = Bytes(r, 32); return s;
    }
    private static bool Bool(BinaryReader r)
    { byte value = r.ReadByte(); if (value > 1) throw new IOException("Invalid boolean."); return value != 0; }
    private static int Count(BinaryReader r, int max)
    { int count = r.ReadByte(); if (count > max) throw new IOException("Count exceeds native prefab bound."); return count; }
    private static byte[] Exact(BinaryReader r, int count)
    { byte[] bytes = r.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException(); return bytes; }
    private static byte[] Bytes(BinaryReader r, int max) => Exact(r, Count(r, max));
    private static void Bytes(BinaryWriter w, byte[] bytes) { w.Write((byte)bytes.Length); w.Write(bytes); }
    private static void Floats(BinaryWriter w, float[] values) { foreach (float value in values) w.Write(value); }
    private static float[] Floats(BinaryReader r, int count)
    { var values = new float[count]; for (int i = 0; i < count; i++) values[i] = r.ReadSingle(); return values; }
}
