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
    // The real viewer frustum expressed in the coordinates used by this eye.
    // Desktop mapping supplies its native-world -> physical-window transform;
    // physical sources use the actual tracked eye directly.
    internal readonly bool HasWorldToHeadClip;
    internal readonly Matrix4x4 WorldToHeadClip;

    internal MenuDepthEye(bool right, Vector3 worldPosition, Camera headCamera, bool projectToGraphicPlane = false,
        Vector2 pixelFootprint = default, Matrix4x4? worldToHeadClip = null)
    {
        Right = right;
        WorldPosition = worldPosition;
        HeadCamera = headCamera;
        ProjectToGraphicPlane = projectToGraphicPlane;
        PixelFootprint = pixelFootprint;
        HasWorldToHeadClip = worldToHeadClip.HasValue || headCamera != null;
        WorldToHeadClip = worldToHeadClip ?? (headCamera != null
            ? HeadClip(right, worldPosition, headCamera) : Matrix4x4.identity);
    }

    internal static Matrix4x4 HeadClip(bool right, Vector3 physicalPosition, Camera head)
    {
        Camera.StereoscopicEye side = right ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;
        Matrix4x4 view = head.stereoEnabled ? head.GetStereoViewMatrix(side)
            : Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                * Matrix4x4.TRS(physicalPosition, head.transform.rotation, Vector3.one).inverse;
        Matrix4x4 projection = head.stereoEnabled ? head.GetStereoProjectionMatrix(side) : head.projectionMatrix;
        return projection * view;
    }
}
