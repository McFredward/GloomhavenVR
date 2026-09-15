using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace GloomhavenVR.WorldUI;
// Executes the extracted production union over the same bounded hierarchy as the ink walk.
internal static partial class CanvasConversion
{
    private static readonly Dictionary<int, int> ClipperMemo = new(), AuthoredOffsetMemo = new();
    private static readonly List<Graphic> HitGraphicScratch = new();
    private static bool TryGetVisibleHostRect(ConvertedPanel panel, Graphic g, out Vector2 min, out Vector2 max)
    {
        Rect r = ((RectTransform)g.transform).rect;
        min = new Vector2(r.xMin, r.yMin); max = new Vector2(r.xMax, r.yMax);
        return g.enabled && g.gameObject.activeInHierarchy && !g.canvasRenderer.cull
            && g.color.a * g.canvasRenderer.GetInheritedAlpha() >= FitMinAlpha;
    }
    internal static bool MeasureDrawn(ConvertedPanel panel, out Rect rect, bool includeHint) =>
        TryMeasureDrawnUnion(panel, panel.Target, panel.HostRect.rect, out rect, out _, out _, out _, includeHint);
}
