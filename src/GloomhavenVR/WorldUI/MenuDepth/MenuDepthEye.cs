using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>One viewer's eye in the actual shared world frame, never a zoom-derived pose.</summary>
internal readonly struct MenuDepthEye
{
    internal readonly bool Right;
    internal readonly Vector3 WorldPosition;
    internal readonly Camera HeadCamera;

    internal MenuDepthEye(bool right, Vector3 worldPosition, Camera headCamera)
    {
        Right = right;
        WorldPosition = worldPosition;
        HeadCamera = headCamera;
    }
}
