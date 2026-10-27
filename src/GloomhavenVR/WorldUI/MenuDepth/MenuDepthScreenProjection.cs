using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Map a real eye through a desktop window into its original screen-space camera's units.</summary>
internal static class MenuDepthScreenProjection
{
    /// <summary>Sample physical glass corners before mapping an eye into native UI units.</summary>
    internal static Vector2 PixelFootprint(Transform screen, Camera original, RectTransform graphic, MenuDepthEye eye)
    {
        Rect r = graphic.rect;
        Vector3 bl = graphic.TransformPoint(new Vector3(r.xMin, r.yMin));
        Vector3 tl = graphic.TransformPoint(new Vector3(r.xMin, r.yMax));
        Vector3 tr = graphic.TransformPoint(new Vector3(r.xMax, r.yMax));
        Vector3 br = graphic.TransformPoint(new Vector3(r.xMax, r.yMin));
        Vector2 raster = MenuDepthProjection.RasterPixels(original, bl, tl, tr, br);
        Vector2 final = MenuDepthProjection.HeadPixels(eye, OnGlass(screen, original, bl), OnGlass(screen, original, tl),
            OnGlass(screen, original, tr), OnGlass(screen, original, br));
        return MenuDepthProjection.LimitPixels(final, raster);
    }

    private static Vector3 OnGlass(Transform screen, Camera original, Vector3 point)
    {
        Vector3 uv = original.WorldToViewportPoint(point); Rect viewport = original.rect;
        return screen.TransformPoint(new Vector3(viewport.x + uv.x * viewport.width - .5f,
            viewport.y + uv.y * viewport.height - .5f));
    }

    internal static bool TryMap(Transform screen, Camera original, RectTransform graphic,
        Vector3 physicalEye, out Vector3 nativeEye)
        => TryMap(screen, original, graphic, physicalEye, out nativeEye, out _);

    internal static bool TryMap(Transform screen, Camera original, RectTransform graphic,
        Vector3 physicalEye, out Vector3 nativeEye, out Matrix4x4 nativeToPhysical, bool allowCoplanar = false)
    {
        nativeEye = Vector3.zero;
        nativeToPhysical = Matrix4x4.identity;
        Rect viewport = original.rect;
        float physicalWidth = screen.TransformVector(Vector3.right).magnitude * viewport.width;
        float physicalHeight = screen.TransformVector(Vector3.up).magnitude * viewport.height;
        float depth = original.WorldToViewportPoint(graphic.TransformPoint(graphic.rect.center)).z;
        if (physicalWidth <= 1e-6f || physicalHeight <= 1e-6f || depth <= 1e-5f) return false;
        Vector3 center = original.ViewportToWorldPoint(new Vector3(.5f, .5f, depth));
        Vector3 horizontal = original.ViewportToWorldPoint(new Vector3(1f, .5f, depth))
            - original.ViewportToWorldPoint(new Vector3(0f, .5f, depth));
        Vector3 vertical = original.ViewportToWorldPoint(new Vector3(.5f, 1f, depth))
            - original.ViewportToWorldPoint(new Vector3(.5f, 0f, depth));
        float xScale = horizontal.magnitude / physicalWidth, yScale = vertical.magnitude / physicalHeight;
        if (xScale <= 1e-6f || yScale <= 1e-6f) return false;
        Vector3 right = horizontal.normalized, up = vertical.normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        Vector3 physicalCenter = screen.TransformPoint(new Vector3(viewport.center.x - .5f, viewport.center.y - .5f));
        Vector3 offset = physicalEye - physicalCenter;
        nativeEye = center + right * (Vector3.Dot(offset, screen.right) * xScale)
            + up * (Vector3.Dot(offset, screen.up) * yScale)
            + forward * (Vector3.Dot(offset, screen.forward) * Mathf.Sqrt(xScale * yScale));
        // Inverse of the same native metric used above, including its geometric
        // mean depth scale. This is not the native UI camera's own field of view.
        Matrix4x4 nativeFrame = Matrix4x4.identity;
        nativeFrame.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, center)));
        nativeFrame.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, center)));
        nativeFrame.SetRow(2, new Vector4(forward.x, forward.y, forward.z, -Vector3.Dot(forward, center)));
        nativeToPhysical = Matrix4x4.TRS(physicalCenter, screen.rotation,
            new Vector3(1f / xScale, 1f / yScale, 1f / Mathf.Sqrt(xScale * yScale))) * nativeFrame;
        return MenuDepthProjection.Finite(nativeEye)
            && (allowCoplanar || Mathf.Abs(Vector3.Dot(center - nativeEye, forward)) > 1e-5f);
    }
}
