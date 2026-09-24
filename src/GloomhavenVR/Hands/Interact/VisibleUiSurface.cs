using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact;

/// <summary>A canvas frame is layout, not solid geometry. Only painted UI at the ray's
/// intersection can occlude another window. Disabled controls still paint and still occlude.</summary>
internal static class VisibleUiSurface
{
    internal static bool Contains(Canvas canvas, Vector2 screen, Camera? camera)
    {
        if (ContainsOwn(canvas, screen, camera)) return true;
        var nested = UguiPokeSurfaces.NestedOf(canvas);
        if (nested != null)
            foreach (Canvas child in nested)
                if (child != null && child.isActiveAndEnabled && ContainsOwn(child, screen, camera)) return true;
        return false;
    }

    private static bool ContainsOwn(Canvas canvas, Vector2 screen, Camera? camera)
    {
        var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
        for (int i = 0; i < graphics.Count; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvasRenderer.cull
                || graphic.color.a * graphic.canvasRenderer.GetAlpha() * graphic.canvasRenderer.GetInheritedAlpha() < .01f)
                continue;
            if (graphic is TMPro.TMP_Text text && string.IsNullOrWhiteSpace(text.text)) continue;
            if (graphic is Text legacy && string.IsNullOrWhiteSpace(legacy.text)) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, screen, camera)) continue;
            // Unity can migrate the registry entry to an ancestor when a nested canvas is
            // disabled. That bookkeeping must not resurrect the hidden native UI subtree.
            Canvas logicalCanvas = graphic.GetComponentInParent<Canvas>();
            if (logicalCanvas != null && !logicalCanvas.isActiveAndEnabled) continue;
            // Native mask and CanvasGroup filters apply even to decorative graphics. Their
            // raycastTarget flag is deliberately irrelevant: visible paper is still a surface.
            if (graphic.Raycast(screen, camera)) return true;
        }
        return false;
    }
}
