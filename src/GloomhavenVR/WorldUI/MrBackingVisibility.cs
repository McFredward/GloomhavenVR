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
                Graphic graphic = Witnesses[i];
                if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)
                    continue;
                if (Root != null && !ReferenceEquals(graphic.transform, Root)
                    && !graphic.transform.IsChildOf(Root)) continue;
                if (graphic is TMP_Text tmp && string.IsNullOrWhiteSpace(tmp.text)) continue;
                if (graphic is Text text && string.IsNullOrWhiteSpace(text.text)) continue;
                Canvas? canvas = graphic.canvas;
                CanvasRenderer? renderer = graphic.canvasRenderer;
                if (canvas == null || !canvas.isActiveAndEnabled || renderer == null || renderer.cull)
                    continue;
                float current = graphic.color.a * renderer.GetInheritedAlpha();
                if (float.IsNaN(current)) continue;
                alpha = Mathf.Max(alpha, Mathf.Clamp01(current));
                if (alpha >= 1f) return 1f;
            }
            return alpha;
        }
    }

    internal void Reset()
    {
        Witnesses.Clear();
        Root = null;
    }
}
