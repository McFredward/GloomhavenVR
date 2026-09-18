using System;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>Measured Guildmaster furniture layout in the map seat's right/forward frame.</summary>
internal static class GuildmasterRoomLayout
{
    internal readonly struct Rail
    {
        internal readonly float X, Z, Cap, Pitch;
        internal readonly int Columns;
        internal Rail(float x, float z, float cap, float pitch, int columns)
        { X = x; Z = z; Cap = cap; Pitch = pitch; Columns = columns; }
        internal void Position(int index, out float x, out float z)
        { x = X + index % Columns * Pitch; z = Z - index / Columns * Pitch; }
    }

    // The outer glow/socket matters too: a centre on the tabletop is not a supported button.
    internal static bool TryRail(float left, float right, float near, float far, int count,
        float desiredCap, float gapRatio, float outerRatio, out Rail rail)
    {
        rail = default;
        if (count <= 0 || right <= left || far <= near || desiredCap <= 0f) return false;
        float best = 0f;
        for (int columns = 1; columns <= count; columns++)
        {
            int rows = (count + columns - 1) / columns;
            float cap = Math.Min(desiredCap, Math.Min(
                (right - left) / (outerRatio + (columns - 1) * (1f + gapRatio)),
                (far - near) / (outerRatio + (rows - 1) * (1f + gapRatio))));
            if (cap <= best) continue;
            best = cap;
            float pitch = cap * (1f + gapRatio);
            float width = outerRatio * cap + (columns - 1) * pitch;
            rail = new Rail((left + right - width + outerRatio * cap) * .5f,
                far - outerRatio * cap * .5f, cap, pitch, columns);
        }
        return best > 0f;
    }

    internal static Rail FallbackRail(float mapRight, float mapFar, float knifeFar, int count,
        float cap, float gapRatio, float outerRatio)
    {
        int columns = Math.Min(2, Math.Max(1, count));
        int rows = (Math.Max(1, count) + columns - 1) / columns;
        float radius = outerRatio * cap * .5f;
        float pitch = cap * (1f + gapRatio);
        float farCentre = Math.Max(mapFar - radius, knifeFar + radius + (rows - 1) * pitch);
        return new Rail(mapRight + radius, farCentre, cap, pitch, columns);
    }

    internal static float GroundFloor(float furnitureBottom, float scale) => furnitureBottom + .025f * scale;
}
