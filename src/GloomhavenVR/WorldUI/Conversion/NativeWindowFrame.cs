using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Keep native full-screen window layout in its authored design frame while floated.</summary>
internal static class NativeWindowFrame
{
    internal static Vector2 Resolve(RectTransform target)
    {
        // Build760 paired hardware: the same party window entered conversion at
        // 2580x1080 on the ultrawide host and1920x1080 on its peer. Its nested
        // creator stretches to that frame, including opposite-edge quest cards.
        // Fixing only the outer host later cannot undo that layout difference.
        // The native scaler's serialized design resolution is independent of the
        // desktop/window mode; fixed-size cards and nested members keep their
        // own authored rectangles. Read before reparenting hides the native root.
        if (target == null || target.GetComponent<UIWindow>() == null) return Vector2.zero;
        Canvas? canvas = target.GetComponentInParent<Canvas>();
        Canvas? root = canvas != null ? canvas.rootCanvas : null;
        CanvasScaler? scaler = root != null ? root.GetComponent<CanvasScaler>() : null;
        if (root == null || !(root.transform is RectTransform frame) || scaler == null
            || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return Vector2.zero;
        Vector2 design = scaler.referenceResolution;
        if (!Valid(design)) return Vector2.zero;
        Vector2 size = target.rect.size;
        bool stretches = (target.anchorMin - Vector2.zero).sqrMagnitude < 1e-8f
            && (target.anchorMax - Vector2.one).sqrMagnitude < 1e-8f
            && Mathf.Abs(target.sizeDelta.x) <= 64f && Mathf.Abs(target.sizeDelta.y) <= 64f;
        bool fills = Mathf.Abs(size.x - frame.rect.width) <= 64f
            && Mathf.Abs(size.y - frame.rect.height) <= 64f;
        return stretches || fills ? design : Vector2.zero;
    }

    internal static bool Valid(Vector2 size) => size.x >= 64f && size.y >= 64f
        && !float.IsNaN(size.x) && !float.IsInfinity(size.x)
        && !float.IsNaN(size.y) && !float.IsInfinity(size.y);

    internal static bool Repin(RectTransform target, Vector2 design)
    {
        if (!Valid(design) || (target.rect.size - design).sqrMagnitude <= .25f) return false;
        // Convert/ReassertConversionFrame collapse anchors before this operation.
        target.sizeDelta = design;
        return true;
    }
}
