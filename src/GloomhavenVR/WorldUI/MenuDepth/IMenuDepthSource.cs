using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// An existing native graphic with deliberate spatial content. Eye rendering must only
/// borrow presentation state; native callbacks, animation clocks and input remain original.
/// </summary>
internal interface IMenuDepthSource
{
    Graphic Graphic { get; }
    bool IsReady { get; }
    void BeforeCapture(MenuDepthEye eye);
    void AfterCapture();
}

/// <summary>One explicitly approved native hint outside the unchanged window frame.</summary>
internal interface IMenuDepthWindowAnnotationSource : IMenuDepthSource
{
    RectTransform AnnotationRect { get; }
}

/// <summary>An explicitly owned native window member whose scene/root must remain independent.
/// Its original graphics belong only to this exact capture owner, never a neighbouring window.</summary>
internal interface IMenuDepthOwnedWindowSource : IMenuDepthWindowAnnotationSource
{
    Transform CaptureOwner { get; }
}

/// <summary>A source that renders into its own sampled texture and needs a per-consumer pixel demand.</summary>
internal interface IMenuDepthSampledSource : IMenuDepthSource { }

/// <summary>
/// Original printed surfaces carried by a raised native UI object. Colour and
/// text remain native; the compositor uses these exact authored silhouettes to
/// compare a foreground model fragment against the object's physical plane.
/// </summary>
internal interface IMenuDepthOcclusionSource : IMenuDepthSource
{
    IReadOnlyList<Graphic> DepthGraphics { get; }
}

/// <summary>
/// Conservative foreground footprint on the original Graphic's local z=0 plane.
/// Capture carriers may include this transparent overspill without changing the
/// native layout, input rectangle or synchronized world-window geometry.
/// </summary>
internal interface IMenuDepthBoundsSource : IMenuDepthSource
{
    bool TryGetForegroundBounds(MenuDepthEye eye, out Rect graphicLocalBounds);
}

/// <summary>Conservative original spatial ink bounds in the supplied eye's full head clip frame.</summary>
internal interface IMenuDepthHeadBoundsSource : IMenuDepthSource
{
    bool TryGetHeadBounds(MenuDepthEye eye, out Rect clipBounds);
}

/// <summary>Prime a stable native UI batch texture, then refresh its pixels for each actual head eye.</summary>
internal interface IMenuDepthDirectSource : IMenuDepthSampledSource
{
    void PrimeDirectCapture(MenuDepthEye samplingEye);
    void BeforeDirectCapture(MenuDepthEye eye);
    void CancelDirectCapture();
}

/// <summary>One actual displayed surface and its unchanged native UI coordinate.</summary>
internal readonly struct MenuDepthRayHit
{
    internal readonly Graphic Graphic;
    internal readonly Transform NativeRoot;
    internal readonly Vector3 WorldPoint, NativePoint;
    internal readonly float Distance;
    internal readonly bool PreferGraphic;
    internal readonly bool UnclippedForeground;
    internal MenuDepthRayHit(Graphic graphic, Vector3 worldPoint, Vector3 nativePoint, float distance,
        bool preferGraphic = false, Transform? nativeRoot = null, bool unclippedForeground = false)
    { Graphic = graphic; NativeRoot = nativeRoot != null ? nativeRoot : graphic.transform;
        WorldPoint = worldPoint; NativePoint = nativePoint; Distance = distance; PreferGraphic = preferGraphic;
        UnclippedForeground = unclippedForeground; }
}

/// <summary>Read the existing physical geometry; never retain a capture's borrowed transforms.</summary>
internal interface IMenuDepthRaycastSource : IMenuDepthSource
{
    Transform RayRoot { get; }
    bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out MenuDepthRayHit hit);
}

/// <summary>Double-sided native triangle intersection. Transform the ray once, retaining world t.</summary>
internal static class MenuDepthRayGeometry
{
    internal static bool Triangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
        float maxDistance, out float distance, out Vector3 barycentric)
    {
        distance = 0f; barycentric = default;
        Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(direction, ac);
        float determinant = Vector3.Dot(ab, p);
        if (Mathf.Abs(determinant) <= 1e-10f) return false;
        float inverse = 1f / determinant;
        Vector3 offset = origin - a;
        float u = Vector3.Dot(offset, p) * inverse;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(offset, ab);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < 0f || u + v > 1f) return false;
        distance = Vector3.Dot(ac, q) * inverse;
        if (!(distance > 1e-6f) || !(distance < maxDistance)) return false;
        barycentric = new Vector3(1f - u - v, u, v);
        return true;
    }
}
