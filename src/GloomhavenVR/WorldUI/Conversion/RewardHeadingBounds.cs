using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The native reward announcement can draw beyond its authored text box. Build 510's hardware
/// image gegenstand.jpg cuts off both ends of the German item heading, while its capture log
/// incorrectly says no content draws outside the 690-pixel host. RectTransform corners measure
/// the layout box, not TMP's rendered glyphs. Include the original heading's live glyph bounds
/// in capture/chrome measurement without changing its font, wrapping, reveal or authored layout.
/// Other text keeps its existing measurement; native masks still intersect this expanded draw
/// rectangle at the caller, using their original layout rectangle rather than the glyph union.
/// </summary>
internal static class RewardHeadingBounds
{
    internal static Rect Expand(RectTransform host, Graphic? graphic, Rect layout)
    {
        if (graphic is not TMP_Text text || !text.enabled || !text.gameObject.activeInHierarchy
            || string.IsNullOrEmpty(text.text) || !Singleton<UIRewardsManager>.IsInitialized)
            return layout;
        UIRewardsManager? manager = Singleton<UIRewardsManager>.Instance;
        if (manager == null || !ReferenceEquals(manager.rewardAnnouncementText, text))
            return layout;

        // Read the existing mesh only: rebuilding TMP here changes native animation timing and
        // can allocate materials in a measurement pass. No mesh yet means the layout is the
        // only trustworthy extent; the normal capture cadence will see the completed mesh.
        Bounds glyphs = text.textBounds;
        Vector3 lo = glyphs.min;
        Vector3 hi = glyphs.max;
        if (!Finite(lo.x) || !Finite(lo.y) || !Finite(hi.x) || !Finite(hi.y)
            || hi.x - lo.x <= 0.0001f || hi.y - lo.y <= 0.0001f)
            return layout;

        float left = layout.xMin, right = layout.xMax;
        float bottom = layout.yMin, top = layout.yMax;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 point = host.InverseTransformPoint(text.transform.TransformPoint(
                new Vector3((corner & 1) == 0 ? lo.x : hi.x,
                            (corner & 2) == 0 ? lo.y : hi.y, 0f)));
            if (!Finite(point.x) || !Finite(point.y)) return layout;
            left = Mathf.Min(left, point.x);
            right = Mathf.Max(right, point.x);
            bottom = Mathf.Min(bottom, point.y);
            top = Mathf.Max(top, point.y);
        }
        return Rect.MinMaxRect(left, bottom, right, top);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
