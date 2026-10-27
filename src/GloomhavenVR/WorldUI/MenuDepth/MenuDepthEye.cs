using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>One viewer's eye in the actual shared world frame, never a zoom-derived pose.</summary>
internal readonly struct MenuDepthEye
{
    internal readonly bool Right;
    internal readonly Vector3 WorldPosition;
    internal readonly Camera HeadCamera;
    internal readonly bool ProjectToGraphicPlane;
    // Displayed graphic pixels after any intermediate UI capture ceiling. This
    // is sampled in the physical head frame BEFORE a desktop eye maps to UI units.
    // Zero means unavailable; source providers retain their native-size fallback.
    internal readonly Vector2 PixelFootprint;

    internal MenuDepthEye(bool right, Vector3 worldPosition, Camera headCamera, bool projectToGraphicPlane = false,
        Vector2 pixelFootprint = default)
    {
        Right = right;
        WorldPosition = worldPosition;
        HeadCamera = headCamera;
        ProjectToGraphicPlane = projectToGraphicPlane;
        PixelFootprint = pixelFootprint;
    }
}
