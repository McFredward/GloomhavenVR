using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact;

/// <summary>A canvas frame is layout, not solid geometry. Painted UI and live transparent
/// input targets at the ray's intersection can occlude another window. Disabled visible
/// controls still paint and still occlude.</summary>
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
            if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvasRenderer.cull)
                continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, screen, camera)) continue;
            // Unity can migrate the registry entry to an ancestor when a nested canvas is
            // disabled. That bookkeeping must not resurrect the hidden native UI subtree.
            Canvas logicalCanvas = graphic.GetComponentInParent<Canvas>();
            if (logicalCanvas != null && !logicalCanvas.isActiveAndEnabled) continue;
            // Native mask and CanvasGroup filters apply even to decorative graphics. Their
            // raycastTarget flag is deliberately irrelevant: visible paper is still a surface.
            if (!graphic.Raycast(screen, camera)) continue;
            float inheritedAlpha = graphic.canvasRenderer.GetAlpha()
                * graphic.canvasRenderer.GetInheritedAlpha();
            if (inheritedAlpha < .01f) continue;
            bool painted = graphic.color.a * inheritedAlpha >= .01f;
            if (painted && graphic is TMPro.TMP_Text text && string.IsNullOrWhiteSpace(text.text)) painted = false;
            if (painted && graphic is Text legacy && string.IsNullOrWhiteSpace(legacy.text)) painted = false;
            if (painted) return true;

            // Build 580 regression: the native story box advances from an invisible full-area
            // click target, and every mod close X uses an alpha-zero HitPlane under a visible
            // button. The first visible-ink filter correctly removed ghost layout rectangles,
            // but it also removed these REAL controls before GraphicRaycaster could see them.
            // A transparent image is only a surface when it is an enabled raycast target with a
            // live native pointer handler; a decorative or empty transparent graphic stays air.
            // The enclosing host hit rect and the materialise gate are checked by both laser
            // and poke drivers before this predicate is reached.
            if (graphic.raycastTarget && HasLivePointerHandler(graphic.gameObject))
                return true;
        }
        return false;
    }

    private static bool HasLivePointerHandler(GameObject target)
    {
        GameObject? handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target)
            ?? ExecuteEvents.GetEventHandler<IPointerDownHandler>(target)
            ?? ExecuteEvents.GetEventHandler<IBeginDragHandler>(target);
        if (handler == null) return false;
        Selectable selectable = handler.GetComponent<Selectable>();
        return selectable == null || selectable.IsActive() && selectable.IsInteractable();
    }
}
