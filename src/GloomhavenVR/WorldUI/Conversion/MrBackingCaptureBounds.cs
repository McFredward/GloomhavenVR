using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Clip each admitted piece before unioning: an entirely cropped outlier must not
/// stretch the backing across empty space between itself and the displayed native picture.</summary>
internal static class MrBackingCaptureBounds
{
    internal static bool Intersect(Rect a, Rect b, out Rect result)
    {
        result = Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
            Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
        return result.width > 0f && result.height > 0f;
    }

    internal static bool RecordAndClip(Rect raw, List<Rect>? pieces, bool captured, Rect frame, out Rect visible)
    {
        pieces?.Add(raw);
        visible = raw;
        return !captured || Intersect(raw, frame, out visible);
    }

    internal static bool Union(IReadOnlyList<Rect> pieces, bool captured, Rect frame, out Rect bounds)
    {
        bounds = default;
        bool any = false;
        for (int i = 0; i < pieces.Count; i++)
        {
            Rect part = pieces[i];
            if (captured && !Intersect(part, frame, out part)) continue;
            if (part.width <= 0f || part.height <= 0f) continue;
            bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, part.xMin), Mathf.Min(bounds.yMin, part.yMin),
                Mathf.Max(bounds.xMax, part.xMax), Mathf.Max(bounds.yMax, part.yMax)) : part;
            any = true;
        }
        return any;
    }
}
