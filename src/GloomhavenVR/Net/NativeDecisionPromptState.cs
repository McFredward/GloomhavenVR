using System;
using System.IO;
using System.Text;

namespace GloomhavenVR.Net;

internal sealed class NativeDecisionPromptText
{
    internal const int TextBytesMax = 1024;
    internal float[] Rect = new float[18], Colors = new float[8], Font = new float[8], Margin = new float[4];
    internal ushort Flags, Style;
    internal float Alpha = 1f;
    internal byte GroupFlags;
    internal int Alignment;
    internal byte Overflow;
    internal string Text = string.Empty;
    internal bool Validate() => NativeDecisionPromptState.RectValid(Rect) && NativeDecisionPromptState.Values(Colors, 8)
        && NativeDecisionPromptState.Values(Font, 8) && NativeDecisionPromptState.Values(Margin, 4)
        && !float.IsNaN(Alpha) && !float.IsInfinity(Alpha) && Alpha >= 0 && Alpha <= 1 && (GroupFlags & ~3) == 0
        && (Flags & ~31) == 0 && Overflow <= 7 && Alignment >= 0 && Alignment <= 65535
        && Font[0] >= 0 && Font[1] >= 0 && Font[2] >= Font[1] && Font[7] >= 0 && Font[7] <= 1000
        && Text != null && Text.IndexOf('\0') < 0 && NativeDecisionPromptState.Utf8.GetByteCount(Text) <= TextBytesMax;
    internal NativeDecisionPromptText Snapshot() => new() { Rect = (float[])Rect.Clone(), Colors = (float[])Colors.Clone(),
        Font = (float[])Font.Clone(), Margin = (float[])Margin.Clone(), Flags = Flags, Style = Style,
        Alignment = Alignment, Overflow = Overflow, Text = Text, Alpha = Alpha, GroupFlags = GroupFlags };
}

internal sealed class NativeDecisionPromptLine
{
    internal float[] Rect = new float[18];
    internal float Alpha = 1f;
    internal byte Flags = 1;
    internal NativeDecisionPromptText Tip = new(), Warning = new();
    internal bool Validate() => NativeDecisionPromptState.RectValid(Rect) && !float.IsNaN(Alpha)
        && !float.IsInfinity(Alpha) && Alpha >= 0 && Alpha <= 1 && (Flags & ~7) == 0
        && Tip != null && Warning != null && Tip.Validate() && Warning.Validate();
    internal NativeDecisionPromptLine Snapshot() => new() { Rect = (float[])Rect.Clone(), Alpha = Alpha,
        Flags = Flags, Tip = Tip.Snapshot(), Warning = Warning.Snapshot() };
}

/// <summary>Actual original HelpBox output; text is privacy-filtered before entering this DTO.</summary>
internal sealed class NativeDecisionPromptState
{
    internal const int MaxLines = 4;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal float[] Rect = new float[18];
    internal float Alpha = 1f, ReferencePixelsPerUnit = 100f;
    internal float[] Frame = new float[6]; // owner fit width/height, parent width/height, fit padding x/y
    internal byte Flags = 1;
    internal NativeDecisionPromptLine[] Lines = Array.Empty<NativeDecisionPromptLine>();
    internal bool Validate()
    {
        try
        {
            if (!RectValid(Rect) || !Values(Frame, 6) || Array.Exists(Frame, f => f < 0) || float.IsNaN(Alpha) || float.IsInfinity(Alpha) || Alpha < 0 || Alpha > 1
                || float.IsNaN(ReferencePixelsPerUnit) || float.IsInfinity(ReferencePixelsPerUnit)
                || ReferencePixelsPerUnit <= 0 || (Flags & ~7) != 0 || Lines == null
                || Lines.Length == 0 || Lines.Length > MaxLines) return false;
            foreach (var line in Lines) if (line == null || !line.Validate()) return false;
            return true;
        }
        catch (EncoderFallbackException) { return false; }
    }
    internal static bool Values(float[] values, int count)
    {
        if (values == null || values.Length != count) return false;
        foreach (float f in values) if (float.IsNaN(f) || float.IsInfinity(f)) return false;
        return true;
    }
    internal static bool RectValid(float[] rect)
    {
        if (!Values(rect, 18)) return false;
        double norm = (double)rect[14] * rect[14] + (double)rect[15] * rect[15]
            + (double)rect[16] * rect[16] + (double)rect[17] * rect[17];
        return Math.Abs(norm - 1) <= .01;
    }
    internal NativeDecisionPromptState Snapshot()
    {
        var copy = new NativeDecisionPromptState { Rect = (float[])Rect.Clone(), Alpha = Alpha,
            ReferencePixelsPerUnit = ReferencePixelsPerUnit, Frame = (float[])Frame.Clone(), Flags = Flags, Lines = new NativeDecisionPromptLine[Lines.Length] };
        for (int i = 0; i < Lines.Length; i++) copy.Lines[i] = Lines[i].Snapshot();
        return copy;
    }
}

internal sealed class NativeDecisionPromptSnapshot
{
    internal readonly float SampleTime;
    internal readonly int ActorId;
    internal readonly NativeDecisionPromptState? State;
    internal NativeDecisionPromptSnapshot(float time, int actorId, NativeDecisionPromptState? state)
    {
        if (float.IsNaN(time) || float.IsInfinity(time) || time < 0 || (state == null ? actorId != 0 : actorId == 0 || !state.Validate()))
            throw new ArgumentException("invalid native decision prompt");
        SampleTime = time; ActorId = actorId; State = state?.Snapshot();
    }
    internal static bool SameIdentity(NativeDecisionPromptSnapshot a, NativeDecisionPromptSnapshot b)
    {
        if (a.ActorId != b.ActorId || (a.State?.Lines.Length ?? 0) != (b.State?.Lines.Length ?? 0)) return false;
        for (int i = 0; i < (a.State?.Lines.Length ?? 0); i++)
            if (a.State!.Lines[i].Tip.Text != b.State!.Lines[i].Tip.Text
                || a.State.Lines[i].Warning.Text != b.State.Lines[i].Warning.Text) return false;
        return true;
    }
}

/// <summary>Complete GVR1 message14 containing ordered record63 pages; body never truncates text.</summary>
internal static class NativeDecisionPromptCodec
{
    internal const int MaxSize = 4096;
    private const byte Record = 63, Message = 14;
    internal static int Write(NativeDecisionPromptSnapshot snapshot, byte[] buffer)
    {
        if (snapshot == null || buffer == null) return 0;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, NativeDecisionPromptState.Utf8, true))
        {
            w.Write(snapshot.SampleTime); w.Write(snapshot.ActorId); w.Write(snapshot.State != null);
            if (snapshot.State != null)
            {
                NativeDecisionPromptState s = snapshot.State;
                if (!s.Validate()) return 0;
                Floats(w, s.Rect); Floats(w, s.Frame); w.Write(s.Alpha); w.Write(s.ReferencePixelsPerUnit); w.Write(s.Flags);
                w.Write((byte)s.Lines.Length);
                foreach (NativeDecisionPromptLine line in s.Lines)
                { Floats(w, line.Rect); w.Write(line.Alpha); w.Write(line.Flags); Text(w, line.Tip); Text(w, line.Warning); }
            }
        }
        byte[] raw = stream.ToArray();
        int pages = (raw.Length + 252) / 253, size = 6 + raw.Length + pages * 4;
        if (size > MaxSize || size > buffer.Length) return 0;
        buffer[0] = 0x31; buffer[1] = 0x52; buffer[2] = 0x56; buffer[3] = 0x47; buffer[4] = 3; buffer[5] = Message;
        int at = 6;
        for (int page = 0, offset = 0; page < pages; page++)
        {
            int count = Math.Min(253, raw.Length - offset);
            buffer[at++] = Record; buffer[at++] = (byte)(count + 2); buffer[at++] = (byte)page; buffer[at++] = (byte)pages;
            Buffer.BlockCopy(raw, offset, buffer, at, count); at += count; offset += count;
        }
        return at;
    }
    private static void Floats(BinaryWriter w, float[] values) { foreach (float f in values) w.Write(f); }
    private static void Text(BinaryWriter w, NativeDecisionPromptText t)
    {
        Floats(w, t.Rect); Floats(w, t.Colors); Floats(w, t.Font); Floats(w, t.Margin);
        w.Write(t.Alpha); w.Write(t.GroupFlags); w.Write(t.Flags); w.Write(t.Style); w.Write(t.Alignment); w.Write(t.Overflow);
        byte[] bytes = NativeDecisionPromptState.Utf8.GetBytes(t.Text); w.Write((ushort)bytes.Length); w.Write(bytes);
    }
    internal static bool TryRead(byte[] buffer, int length, out NativeDecisionPromptSnapshot? snapshot)
    {
        snapshot = null;
        if (buffer == null || length < 19 || length > MaxSize || length > buffer.Length
            || buffer[0] != 0x31 || buffer[1] != 0x52 || buffer[2] != 0x56 || buffer[3] != 0x47
            || buffer[4] != 3 || buffer[5] != Message) return false;
        try
        {
            using var stream = new MemoryStream();
            int at = 6, expected = 0, total = -1;
            while (at < length)
            {
                if (at + 2 > length) return false;
                int id = buffer[at++], count = buffer[at++];
                if (at + count > length) return false;
                if (id != Record) { at += count; continue; }
                if (count < 3 || buffer[at] != expected || buffer[at + 1] == 0
                    || total >= 0 && buffer[at + 1] != total) return false;
                total = buffer[at + 1];
                if (expected >= total || expected < total - 1 && count != 255) return false;
                expected++; stream.Write(buffer, at + 2, count - 2); at += count;
            }
            if (expected != total) return false;
            stream.Position = 0;
            using var r = new BinaryReader(stream, NativeDecisionPromptState.Utf8, true);
            float time = r.ReadSingle(); int actor = r.ReadInt32(); byte has = r.ReadByte();
            if (has > 1) return false;
            NativeDecisionPromptState? state = null;
            if (has == 1)
            {
                state = new NativeDecisionPromptState { Rect = Floats(r, 18), Frame = Floats(r, 6), Alpha = r.ReadSingle(),
                    ReferencePixelsPerUnit = r.ReadSingle(), Flags = r.ReadByte() };
                int n = r.ReadByte(); if (n == 0 || n > NativeDecisionPromptState.MaxLines) return false;
                state.Lines = new NativeDecisionPromptLine[n];
                for (int i = 0; i < n; i++) state.Lines[i] = new NativeDecisionPromptLine { Rect = Floats(r, 18),
                    Alpha = r.ReadSingle(), Flags = r.ReadByte(), Tip = Text(r), Warning = Text(r) };
            }
            if (stream.Position != stream.Length) return false;
            snapshot = new NativeDecisionPromptSnapshot(time, actor, state); return true;
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is OverflowException) { return false; }
    }
    private static float[] Floats(BinaryReader r, int count)
    { var values = new float[count]; for (int i = 0; i < count; i++) values[i] = r.ReadSingle(); return values; }
    private static NativeDecisionPromptText Text(BinaryReader r)
    {
        var result = new NativeDecisionPromptText { Rect = Floats(r, 18), Colors = Floats(r, 8), Font = Floats(r, 8),
            Margin = Floats(r, 4), Alpha = r.ReadSingle(), GroupFlags = r.ReadByte(), Flags = r.ReadUInt16(), Style = r.ReadUInt16(), Alignment = r.ReadInt32(), Overflow = r.ReadByte() };
        int count = r.ReadUInt16(); if (count > NativeDecisionPromptText.TextBytesMax) throw new InvalidDataException("native prompt text exceeds bound");
        byte[] bytes = r.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException();
        result.Text = NativeDecisionPromptState.Utf8.GetString(bytes); return result;
    }
}
