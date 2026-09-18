using System;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>Measured Guildmaster furniture layout in the map seat's right/forward frame.</summary>
internal static class GuildmasterRoomLayout
{
    internal readonly struct Rail
    {
        internal readonly float X, Z, Cap, Pitch, GroupPitch;
        internal readonly int Columns;
        internal Rail(float x, float z, float cap, float pitch, int columns, float groupPitch = 0f)
        { X = x; Z = z; Cap = cap; Pitch = pitch; Columns = columns; GroupPitch = groupPitch; }
        internal void Position(int index, out float x, out float z)
        { x = X + index % Columns * Pitch; z = Z - index / Columns * Pitch; }
    }

    // Keep the map surfaces in their own right-hand vertical pair, centred alongside actions.
    // Group counts come from the native HUD's declared row classification, never scan order.
    internal static void GroupPosition(Rail rail, int group, int index, int actions, int maps,
        out float x, out float z)
    {
        int rows = Math.Max(actions, maps);
        int count = group == 0 ? actions : maps;
        x = rail.X + (group == 1 && actions > 0 ? rail.GroupPitch : 0f);
        z = rail.Z - ((rows - count) * .5f + index) * rail.Pitch;
    }

    internal static bool TryGroupedRail(float left, float right, float near, float far,
        int actions, int maps, float desiredCap, float gapRatio, float groupGapRatio,
        float outerRatio, out Rail rail)
    {
        rail = default;
        int rows = Math.Max(actions, maps);
        bool pair = actions > 0 && maps > 0;
        if (rows <= 0 || right <= left || far <= near || desiredCap <= 0f) return false;
        float cap = Math.Min(desiredCap, Math.Min(
            (right - left) / (outerRatio + (pair ? 1f + groupGapRatio : 0f)),
            (far - near) / (outerRatio + (rows - 1) * (1f + gapRatio))));
        float groupPitch = cap * (1f + groupGapRatio);
        float width = outerRatio * cap + (pair ? groupPitch : 0f);
        rail = new Rail((left + right - width + outerRatio * cap) * .5f,
            far - outerRatio * cap * .5f, cap, cap * (1f + gapRatio), pair ? 2 : 1, groupPitch);
        return cap > 0f;
    }

    internal static Rail FallbackGroupedRail(float mapRight, float mapFar, float knifeFar,
        int actions, int maps, float cap, float gapRatio, float groupGapRatio, float outerRatio)
    {
        int rows = Math.Max(1, Math.Max(actions, maps));
        float radius = outerRatio * cap * .5f;
        float pitch = cap * (1f + gapRatio);
        float farCentre = Math.Max(mapFar - radius, knifeFar + radius + (rows - 1) * pitch);
        return new Rail(mapRight + radius, farCentre, cap, pitch,
            actions > 0 && maps > 0 ? 2 : 1, cap * (1f + groupGapRatio));
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

    // A mathematically positive sliver is not an accessible physical button. Half the normal
    // 55 mm face is the smallest supported layout; narrower fits use full-size fallback caps.
    internal static bool HasUsableCap(Rail rail, float desiredCap) => rail.Cap >= desiredCap * .5f;

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
