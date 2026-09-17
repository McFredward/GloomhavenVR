using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Invalidate a modal's cached backing when an extremal contributor disappears or
/// its actual capture changes. Four witnesses keep this independent of window complexity;
/// the grab bar's slower sampling and monotone geometry remain unchanged.</summary>
internal sealed class MrBackingSampleWatch
{
    private Graphic? _top, _bottom, _left, _right;
    private bool _known, _captured;
    private Rect _capture;
    private int _confirmAt = -1;

    internal void Remember(ConvertedPanel panel, PanelInkBounds.Ink ink)
    {
        _top = ink.MrTop; _bottom = ink.MrBottom;
        _left = ink.MrLeft; _right = ink.MrRight;
        _captured = PanelSupersample.TryGetBackingCaptureRect(panel, out _capture);
        _known = true;
    }

    internal bool NeedsSample(ConvertedPanel panel, int frame, int confirmationStride)
    {
        if (!_known) return false;
        bool captured = PanelSupersample.TryGetBackingCaptureRect(panel, out Rect capture);
        bool changed = captured != _captured || (captured && !Same(capture, _capture))
            || Hidden(_top, panel.Target) || Hidden(_bottom, panel.Target)
            || Hidden(_left, panel.Target) || Hidden(_right, panel.Target);
        if (changed)
        {
            // MR layout confirms geometry on independent samples. Do not make its second
            // sample wait for the grab bar's sixty-frame verification cadence.
            _confirmAt = frame + confirmationStride;
            return true;
        }
        if (_confirmAt >= 0 && frame >= _confirmAt)
        {
            _confirmAt = -1;
            return true;
        }
        return false;
    }

    private static bool Hidden(Graphic? graphic, Transform? root) => !ReferenceEquals(graphic, null)
        && MrBackingVisibility.GraphicAlpha(graphic, root) < CanvasConversion.FitMinAlpha;

    private static bool Same(Rect a, Rect b) => Mathf.Abs(a.xMin-b.xMin) < 0.001f
        && Mathf.Abs(a.xMax-b.xMax) < 0.001f && Mathf.Abs(a.yMin-b.yMin) < 0.001f
        && Mathf.Abs(a.yMax-b.yMax) < 0.001f;

    internal void Reset()
    {
        _known = false;
        _confirmAt = -1;
        _top = _bottom = _left = _right = null;
    }
}
