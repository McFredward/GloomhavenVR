using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Current visibility of graphics admitted by the existing ink sample. Geometry may be cached;
/// a native hide, empty label, alpha change or disabled canvas may not leave an opaque plate behind.
/// No tree walk, delayed clear or independent animation clock runs on the presentation path.</summary>
internal sealed class MrBackingVisibility
{
    internal List<Graphic> Witnesses { get; } = new(16);

    /// <summary>The measured owner/scope. A native widget reparented elsewhere no longer backs it.</summary>
    internal Transform? Root { get; set; }

    internal bool VisibleNow => AlphaNow >= CanvasConversion.FitMinAlpha;

    internal float AlphaNow
    {
        get
        {
            float alpha = 0f;
            for (int i = 0; i < Witnesses.Count; i++)
            {
                alpha = Mathf.Max(alpha, GraphicAlpha(Witnesses[i], Root));
                if (alpha >= 1f) return 1f;
            }
            return alpha;
        }
    }

    // The modal owner also probes its four measured extrema before reusing a cached extent.
    // Keep the same native visibility policy without scanning every witness to invalidate it.
    internal static float GraphicAlpha(Graphic? graphic, Transform? root)
    {
        if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
            return 0f;
        if (root != null && !ReferenceEquals(graphic.transform, root)
            && !graphic.transform.IsChildOf(root)) return 0f;
        if (graphic is TMP_Text tmp && string.IsNullOrWhiteSpace(tmp.text)) return 0f;
        if (graphic is Text text && string.IsNullOrWhiteSpace(text.text)) return 0f;
        Canvas? canvas = graphic.canvas;
        CanvasRenderer? renderer = graphic.canvasRenderer;
        if (canvas == null || !canvas.isActiveAndEnabled || renderer == null || renderer.cull)
            return 0f;
        float current = graphic.color.a * renderer.GetInheritedAlpha() * renderer.GetAlpha();
        return float.IsNaN(current) ? 0f : Mathf.Clamp01(current);
    }

    internal void Reset()
    {
        Witnesses.Clear();
        Root = null;
    }
}
