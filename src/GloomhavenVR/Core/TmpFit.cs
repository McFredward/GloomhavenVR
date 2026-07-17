using TMPro;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// World-space TextMeshPro sizing helper (P8, hardware test #12): every mod-created
/// 3D TMP label is constrained to the plate/button/badge it sits on.
///
/// Background: 3D TMP world metrics are fontSize / 10 ≈ line height in local meters.
/// Several board labels were authored with font sizes LARGER than their container
/// (e.g. the initiative badge: fontSize 1.2 = 0.12 m line on a 0.042 m disc), and
/// localized strings (CONFIRM/UNDO/REST mirror the game's own localization) vary
/// wildly in length — with auto-sizing off, TMP happily renders past the rect, so
/// text overflowed and clipped across neighboring elements (test #12).
///
/// Policy applied here: the rect matches the visible plate, auto-size fits the text
/// INTO that rect (cap ≈ 65 % of the container height per line, so ascenders and
/// descenders never touch the plate edge), long strings shrink and/or wrap instead
/// of spilling, and past the readability floor the text truncates — text is always
/// fully inside its plate.
/// </summary>
internal static class TmpFit
{
    /// <summary>Auto-size floor (font-size units, ≈8 mm line): below this, truncate.</summary>
    private const float MinFontSize = 0.08f;

    /// <summary>Font-size cap per meter of container height (≈65 % line fill).</summary>
    private const float CapPerMeterHeight = 6.5f;

    /// <summary>
    /// Constrain a world-space TMP label to a width × height box (local meters).
    /// <paramref name="maxFontSize"/> is the preferred size for short strings; it is
    /// always clamped to the height-derived cap. <paramref name="wrap"/> = false for
    /// single-line labels (numbers, badges) — auto-size then fits the line to the
    /// box width instead of wrapping.
    /// </summary>
    internal static void Fit(TMP_Text tmp, float widthMeters, float heightMeters,
        float maxFontSize = 0f, bool wrap = true)
    {
        tmp.rectTransform.sizeDelta = new Vector2(widthMeters, heightMeters);
        float cap = heightMeters * CapPerMeterHeight;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = maxFontSize > 0f ? Mathf.Min(maxFontSize, cap) : cap;
        tmp.fontSizeMin = MinFontSize;
        tmp.enableWordWrapping = wrap;
        tmp.overflowMode = TextOverflowModes.Truncate;
    }
}
