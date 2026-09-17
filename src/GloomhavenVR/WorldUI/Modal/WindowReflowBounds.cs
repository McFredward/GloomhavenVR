using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Opening-time occupancy is the displayed picture, never transparent layout or ray
/// catchment. Read the same native geometry, alpha, masks and capture crop as MR backgrounds,
/// without requiring MR mode or mutating native controls.</summary>
internal static class WindowReflowBounds
{
    internal static bool TryRead(ConvertedPanel panel, out Rect bounds, out bool pending)
    {
        bool measured = PanelInkBounds.TryMeasure(panel, out PanelInkBounds.Ink ink,
            contentRoot: panel.FitContentRoot, backingGeometry: true);
        bounds = ink.Rect;
        pending = ink.PendingPaint > 0;
        return measured && ink.Valid && !ink.Truncated;
    }
}
