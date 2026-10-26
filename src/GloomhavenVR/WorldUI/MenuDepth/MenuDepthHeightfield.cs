using System;
using System.IO;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Original-image inverse depth, prepared offline. No model or inference runs in the game.</summary>
internal sealed class MenuDepthHeightfield
{
    internal readonly int Width;
    internal readonly int Height;
    internal readonly int SourceWidth;
    internal readonly int SourceHeight;
    private readonly ushort[] _samples;

    private MenuDepthHeightfield(int width, int height, int sourceWidth, int sourceHeight, ushort[] samples)
    {
        Width = width; Height = height; SourceWidth = sourceWidth; SourceHeight = sourceHeight; _samples = samples;
    }

    internal static MenuDepthHeightfield Read(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length < 32 || bytes[0] != 'G' || bytes[1] != 'M' || bytes[2] != 'D' || bytes[3] != '1'
            || U16(bytes, 4) != 1 || U16(bytes, 6) != 32 || U32(bytes, 12) != 3)
            throw new InvalidDataException("Unsupported menu depth header.");
        int width = U16(bytes, 8), height = U16(bytes, 10);
        uint sourceWidth = U32(bytes, 24), sourceHeight = U32(bytes, 28);
        if (width < 1 || height < 1 || width > 512 || height > 512
            || sourceWidth < 1 || sourceHeight < 1 || sourceWidth > 16384 || sourceHeight > 16384)
            throw new InvalidDataException("Invalid menu depth dimensions.");
        int payload = checked(width * height * 2);
        if (U32(bytes, 16) != payload || bytes.Length != 32 + payload
            || Crc(bytes, 32) != U32(bytes, 20))
            throw new InvalidDataException("Menu depth payload is incomplete or corrupt.");
        var samples = new ushort[width * height];
        for (int i = 0; i < samples.Length; i++) samples[i] = (ushort)U16(bytes, 32 + i * 2);
        return new MenuDepthHeightfield(width, height, (int)sourceWidth, (int)sourceHeight, samples);
    }

    /// <summary>UV0 addresses the unchanged native Texture2D, bottom-left origin. One is nearer.</summary>
    internal float Sample(float u, float v)
    {
        if (float.IsNaN(u) || float.IsInfinity(u) || float.IsNaN(v) || float.IsInfinity(v))
            throw new ArgumentOutOfRangeException(nameof(u), "Depth UVs must be finite.");
        float x = Math.Max(0f, Math.Min(1f, u)) * (Width - 1);
        float y = Math.Max(0f, Math.Min(1f, v)) * (Height - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        float tx = x - x0, ty = y - y0;
        float bottom = _samples[y0 * Width + x0] * (1f - tx) + _samples[y0 * Width + x1] * tx;
        float top = _samples[y1 * Width + x0] * (1f - tx) + _samples[y1 * Width + x1] * tx;
        return (bottom * (1f - ty) + top * ty) / 65535f;
    }

    private static int U16(byte[] bytes, int at) => bytes[at] | bytes[at + 1] << 8;
    private static uint U32(byte[] bytes, int at) => (uint)(bytes[at] | bytes[at + 1] << 8
        | bytes[at + 2] << 16 | bytes[at + 3] << 24);
    private static uint Crc(byte[] bytes, int start)
    {
        uint value = uint.MaxValue;
        for (int i = start; i < bytes.Length; i++)
        {
            value ^= bytes[i];
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0u);
        }
        return ~value;
    }
}
