using System;
using System.Text;

namespace GloomhavenVR.Net;

/// <summary>Rendered original mandatory-damage Image. Record 55 contains no card identity.</summary>
internal sealed class NativeDecisionHighlightState
{
    internal const int NameBytesMax = 64;
    internal float SampleTime;
    internal float[] Rect = new float[18];
    internal float[] Colors = new float[8];
    internal byte Flags, ImageType, FillMethod, FillOrigin;
    internal float FillAmount, PixelsPerUnitMultiplier = 1f, ReferencePixelsPerUnit = 100f;
    internal string SpriteName = string.Empty, TextureName = string.Empty;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal bool Validate()
    {
        if (!Finite(SampleTime) || SampleTime < 0f || Rect == null || Rect.Length != 18
            || Colors == null || Colors.Length != 8 || (Flags & ~63) != 0 || ImageType > 3
            || FillMethod > 4 || FillOrigin > 3 || !Finite(FillAmount) || FillAmount < 0f || FillAmount > 1f
            || !Finite(PixelsPerUnitMultiplier) || PixelsPerUnitMultiplier <= 0f
            || !Finite(ReferencePixelsPerUnit) || ReferencePixelsPerUnit <= 0f) return false;
        foreach (float value in Rect) if (!Finite(value)) return false;
        foreach (float value in Colors) if (!Finite(value)) return false;
        float norm = Rect[14] * Rect[14] + Rect[15] * Rect[15] + Rect[16] * Rect[16] + Rect[17] * Rect[17];
        if (!Finite(norm) || Math.Abs(norm - 1f) > .01f) return false;
        return NameValid(SpriteName) && NameValid(TextureName)
            && (SpriteName.Length == 0) == (TextureName.Length == 0);
    }

    private static bool NameValid(string name)
    {
        if (name == null || name.IndexOf('\0') >= 0) return false;
        try { return Utf8.GetByteCount(name) <= NameBytesMax; }
        catch (EncoderFallbackException) { return false; }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal NativeDecisionHighlightState Snapshot()
    {
        if (!Validate()) throw new ArgumentException("invalid original decision highlight");
        var copy = (NativeDecisionHighlightState)MemberwiseClone();
        copy.Rect = (float[])Rect.Clone(); copy.Colors = (float[])Colors.Clone();
        return copy;
    }

    internal static bool SamePicture(NativeDecisionHighlightState? a, NativeDecisionHighlightState? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Flags != b.Flags || a.ImageType != b.ImageType
            || a.FillMethod != b.FillMethod || a.FillOrigin != b.FillOrigin || a.FillAmount != b.FillAmount
            || a.PixelsPerUnitMultiplier != b.PixelsPerUnitMultiplier || a.ReferencePixelsPerUnit != b.ReferencePixelsPerUnit
            || a.SpriteName != b.SpriteName || a.TextureName != b.TextureName) return false;
        for (int i = 0; i < 18; i++) if (a.Rect[i] != b.Rect[i]) return false;
        for (int i = 0; i < 8; i++) if (a.Colors[i] != b.Colors[i]) return false;
        return true;
    }
}

/// <summary>Exact record-55 payload; the containing Presence writer owns its TLV header.</summary>
internal static class NativeDecisionHighlightCodec
{
    internal const int MaxSize = 254;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static int Write(NativeDecisionHighlightState state, byte[] buffer, int offset = 0)
    {
        if (state == null || !state.Validate()) return 0;
        byte[] sprite = Utf8.GetBytes(state.SpriteName), texture = Utf8.GetBytes(state.TextureName);
        int length = 126 + sprite.Length + texture.Length;
        if (buffer == null || offset < 0 || offset > buffer.Length - length) return 0;
        int at = offset;
        void Float(float value)
        { unsafe { uint bits = *(uint*)&value; buffer[at++] = (byte)bits; buffer[at++] = (byte)(bits >> 8); buffer[at++] = (byte)(bits >> 16); buffer[at++] = (byte)(bits >> 24); } }
        Float(state.SampleTime);
        foreach (float value in state.Rect) Float(value);
        foreach (float value in state.Colors) Float(value);
        buffer[at++] = state.Flags; buffer[at++] = state.ImageType;
        buffer[at++] = state.FillMethod; buffer[at++] = state.FillOrigin;
        Float(state.FillAmount); Float(state.PixelsPerUnitMultiplier); Float(state.ReferencePixelsPerUnit);
        buffer[at++] = (byte)sprite.Length; Array.Copy(sprite, 0, buffer, at, sprite.Length); at += sprite.Length;
        buffer[at++] = (byte)texture.Length; Array.Copy(texture, 0, buffer, at, texture.Length); at += texture.Length;
        return at - offset;
    }

    internal static bool TryRead(byte[] buffer, int offset, int length, out NativeDecisionHighlightState? state)
    {
        state = null;
        if (buffer == null || offset < 0 || length < 126 || length > MaxSize || offset > buffer.Length - length) return false;
        try
        {
            int at = offset, end = offset + length;
            float Float()
            {
                if (at > end - 4) throw new ArgumentException();
                uint bits = (uint)(buffer[at] | buffer[at + 1] << 8 | buffer[at + 2] << 16 | buffer[at + 3] << 24); at += 4;
                unsafe { return *(float*)&bits; }
            }
            var parsed = new NativeDecisionHighlightState { SampleTime = Float() };
            for (int i = 0; i < 18; i++) parsed.Rect[i] = Float();
            for (int i = 0; i < 8; i++) parsed.Colors[i] = Float();
            parsed.Flags = buffer[at++]; parsed.ImageType = buffer[at++];
            parsed.FillMethod = buffer[at++]; parsed.FillOrigin = buffer[at++];
            parsed.FillAmount = Float(); parsed.PixelsPerUnitMultiplier = Float(); parsed.ReferencePixelsPerUnit = Float();
            string Name()
            {
                if (at >= end) throw new ArgumentException();
                int count = buffer[at++];
                if (count > NativeDecisionHighlightState.NameBytesMax || count > end - at) throw new ArgumentException();
                string name = Utf8.GetString(buffer, at, count); at += count; return name;
            }
            parsed.SpriteName = Name(); parsed.TextureName = Name();
            if (at != end || !parsed.Validate()) return false;
            state = parsed; return true;
        }
        catch (ArgumentException) { return false; }
    }
}
