using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The native16:9 design viewport, independent of the desktop's aspect/window mode.</summary>
internal static class NativeUiViewport
{
    // Native CanvasScaler reads Canvas.renderingDisplaySize from the camera's
    // target. Keep that target in the same16:9 frame as the physical VR glass.
    // Preserve at least the prior capture's pixels on EACH axis: an ultrawide
    //3440x1440 desktop therefore yields3440x1935, never a lower-quality1920 RT.
    // The existing target lifetime deliberately survives desktop mode changes;
    // changing the monitor must not reallocate or relayout a standing VR window.
    internal static Vector2Int Raster(int width, int height)
    {
        int blocks = Mathf.CeilToInt(Mathf.Max(1280f, Mathf.Max(width, height * (16f / 9f))) / 16f);
        return new Vector2Int(blocks * 16, blocks * 9);
    }
}
