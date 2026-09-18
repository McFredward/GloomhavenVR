using System;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>Stable planar ownership for overlapping visible map icons, shared by ray and finger.</summary>
internal static class MapIconPickGeometry
{
    internal static bool PlaneDistance(float originY, float directionY, float planeY, out float distance)
    {
        distance = 0f;
        if (Math.Abs(directionY) < 0.000001f) return false;
        distance = (planeY - originY) / directionY;
        return distance >= 0f && !float.IsInfinity(distance) && !float.IsNaN(distance);
    }

    internal static bool Contains(float localX, float localZ, float width, float depth) =>
        width > 0f && depth > 0f && Math.Abs(localX) <= width * 0.5f && Math.Abs(localZ) <= depth * 0.5f;

    internal static float Score(float deltaX, float deltaZ) => deltaX * deltaX + deltaZ * deltaZ;

    internal static bool Prefer(float score, int key, float bestScore, int bestKey) =>
        score < bestScore || (score == bestScore && key < bestKey);
}
