using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Compact native buttons have no spare column inside their original painted plate.
/// Keep all original widgets in place and put only our mark beyond their full union.</summary>
internal static class SharedWindowBadgeLayout
{
    internal static bool TryCompactSeat(Rect host, Rect ink, bool hasInk, float size, float gap,
        out Vector2 seat)
    {
        seat = default;
        if (host.height >= size * 2f + gap) return false;
        float right = hasInk ? Mathf.Max(host.xMax, ink.xMax) : host.xMax;
        float top = hasInk ? Mathf.Max(host.yMax, ink.yMax) : host.yMax;
        seat = new Vector2(right + gap + size * 0.5f, top - size * 0.5f);
        return true;
    }
}
