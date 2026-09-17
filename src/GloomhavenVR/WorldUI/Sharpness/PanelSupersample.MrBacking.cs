using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    private static readonly Vector3[] BackingCaptureCorners = new Vector3[4];

    // This is the actual displayed footprint, not the requested/held capture union or host
    // layout. Before capture engages, and for directly rendered remote clones, there is no
    // external crop. Never invent a host-rect crop which would cut legitimate native overflow.
    internal static bool TryGetBackingCaptureRect(ConvertedPanel panel, out Rect frame)
    {
        frame = default;
        RectTransform? host = panel.HostRect;
        if (host == null) return false;
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry entry = Entries[i];
            if (!ReferenceEquals(entry.Panel, panel)) continue;
            if (entry.DisplayRect == null || entry.DisplayGo == null || !entry.DisplayGo.activeInHierarchy
                || entry.DisplayCanvas == null || !entry.DisplayCanvas.isActiveAndEnabled
                || entry.DisplayImage == null || !entry.DisplayImage.enabled)
                return false;
            entry.DisplayRect.GetWorldCorners(BackingCaptureCorners);
            float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity;
            float x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = host.InverseTransformPoint(BackingCaptureCorners[corner]);
                if (float.IsNaN(point.x) || float.IsNaN(point.y)
                    || float.IsInfinity(point.x) || float.IsInfinity(point.y)) return false;
                x0 = Mathf.Min(x0, point.x); y0 = Mathf.Min(y0, point.y);
                x1 = Mathf.Max(x1, point.x); y1 = Mathf.Max(y1, point.y);
            }
            frame = Rect.MinMaxRect(x0, y0, x1, y1);
            return frame.width > 0f && frame.height > 0f;
        }
        return false;
    }
}
