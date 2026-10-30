using System;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Parallel off-axis views through the actual window rectangle; no toe-in or gaze movement.</summary>
internal static class MenuDepthProjection
{
    /// <summary>Physical head pixels, limited by an optional intermediate UI raster.</summary>
    internal static Vector2 PixelFootprint(RectTransform graphic, MenuDepthEye eye, Camera? raster = null)
    {
        Rect r = graphic.rect;
        Vector3 bl = graphic.TransformPoint(new Vector3(r.xMin, r.yMin));
        Vector3 tl = graphic.TransformPoint(new Vector3(r.xMin, r.yMax));
        Vector3 tr = graphic.TransformPoint(new Vector3(r.xMax, r.yMax));
        Vector3 br = graphic.TransformPoint(new Vector3(r.xMax, r.yMin));
        Vector2 pixels = HeadPixels(eye, bl, tl, tr, br);
        return raster == null ? pixels : LimitPixels(pixels, RasterPixels(raster, bl, tl, tr, br));
    }

    internal static Vector2 HeadPixels(MenuDepthEye eye, Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br)
    {
        Camera head = eye.HeadCamera;
        if (head == null) return Vector2.zero;
        Camera.StereoscopicEye side = eye.Right ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;
        Matrix4x4 view = head.stereoEnabled ? head.GetStereoViewMatrix(side)
            : Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                * Matrix4x4.TRS(eye.WorldPosition, head.transform.rotation, Vector3.one).inverse;
        Matrix4x4 projection = head.stereoEnabled ? head.GetStereoProjectionMatrix(side) : head.projectionMatrix;
        float width, height;
        if (head.targetTexture != null)
        { width = head.targetTexture.width * head.rect.width; height = head.targetTexture.height * head.rect.height; }
        else if (head.stereoEnabled && XRSettings.enabled)
        {
            float scale = Mathf.Clamp(XRSettings.renderViewportScale, .01f, 1f);
            width = XRSettings.eyeTextureWidth * scale; height = XRSettings.eyeTextureHeight * scale;
        }
        else { width = head.scaledPixelWidth; height = head.scaledPixelHeight; }
        return ProjectPixels(projection * view, width, height, bl, tl, tr, br);
    }

    internal static Vector2 RasterPixels(Camera raster, Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br)
    {
        RenderTexture target = raster.targetTexture;
        if (target == null) return Vector2.zero;
        return ProjectPixels(raster.projectionMatrix * raster.worldToCameraMatrix,
            target.width * raster.rect.width, target.height * raster.rect.height, bl, tl, tr, br);
    }

    internal static Vector2 LimitPixels(Vector2 final, Vector2 intermediate)
        => final.x > 0f && final.y > 0f && intermediate.x > 0f && intermediate.y > 0f
            ? new Vector2(Mathf.Min(final.x, intermediate.x), Mathf.Min(final.y, intermediate.y)) : Vector2.zero;

    private static Vector2 ProjectPixels(Matrix4x4 matrix, float width, float height,
        Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br)
    {
        if (width <= 0f || height <= 0f || float.IsNaN(width) || float.IsInfinity(width)
            || float.IsNaN(height) || float.IsInfinity(height) || !Finite(bl) || !Finite(tl) || !Finite(tr) || !Finite(br))
            return Vector2.zero;
        for (int i = 0; i < 16; i++)
            if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return Vector2.zero;
        int front = Front(matrix, bl) + Front(matrix, tl) + Front(matrix, tr) + Front(matrix, br);
        if (front == 0) return Vector2.zero;
        // A visible near-clipped aperture has no finite full-rectangle extent.
        // Give it the complete visible raster demand, rather than falling back to
        // native1024 exactly when it is largest. Intermediate/source budgets still
        // limit the result; nothing here can request an unbounded target.
        if (front < 4) return new Vector2(width, height);
        if (!ProjectPixel(matrix, width, height, bl, out Vector2 p0)
            || !ProjectPixel(matrix, width, height, tl, out Vector2 p1)
            || !ProjectPixel(matrix, width, height, tr, out Vector2 p2)
            || !ProjectPixel(matrix, width, height, br, out Vector2 p3)) return Vector2.zero;
        return new Vector2(Mathf.Min(width * 2f, Mathf.Max(Vector2.Distance(p0, p3), Vector2.Distance(p1, p2))),
            Mathf.Min(height * 2f, Mathf.Max(Vector2.Distance(p0, p1), Vector2.Distance(p3, p2))));
    }

    private static int Front(Matrix4x4 matrix, Vector3 point)
        => Finite(point) && (matrix * new Vector4(point.x, point.y, point.z, 1f)).w > 1e-5f ? 1 : 0;

    private static bool ProjectPixel(Matrix4x4 matrix, float width, float height, Vector3 point, out Vector2 pixel)
    {
        Vector4 clip = matrix * new Vector4(point.x, point.y, point.z, 1f);
        pixel = Vector2.zero;
        if (!Finite(point) || clip.w <= 1e-5f) return false;
        pixel = new Vector2(clip.x / clip.w * width * .5f, clip.y / clip.w * height * .5f);
        return !float.IsNaN(pixel.x) && !float.IsInfinity(pixel.x) && !float.IsNaN(pixel.y) && !float.IsInfinity(pixel.y);
    }

    /// <summary>
    /// Intersect the eye-to-depth-vertex ray with the original graphic's z=0 plane.
    /// Keep z for depth testing; an unchanged orthographic UI camera supplies exact
    /// native text sampling while registered art receives the same portal parallax.
    /// </summary>
    internal static Vector3 ProjectToPlane(Vector3 point, Vector3 localEye)
    {
        float denominator = point.z - localEye.z;
        if (!Finite(point) || !Finite(localEye) || Mathf.Abs(denominator) < 1e-8f)
            throw new ArgumentException("The menu-depth vertex cannot project from this eye position.");
        float scale = -localEye.z / denominator;
        return new Vector3(localEye.x + (point.x - localEye.x) * scale,
            localEye.y + (point.y - localEye.y) * scale, point.z);
    }

    internal static bool TryBuild(RectTransform rectangle, Vector3 eye,
        out Matrix4x4 view, out Matrix4x4 projection) =>
        TryBuild(rectangle, rectangle.rect, eye, out view, out projection);

    internal static bool TryBuild(Transform plane, Rect frame, Vector3 eye,
        out Matrix4x4 view, out Matrix4x4 projection)
    {
        view = projection = Matrix4x4.identity;
        Vector3 right = plane.TransformVector(Vector3.right).normalized;
        Vector3 up = plane.TransformVector(Vector3.up).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        Vector3 bottomLeft = plane.TransformPoint(new Vector3(frame.xMin, frame.yMin));
        Vector3 bottomRight = plane.TransformPoint(new Vector3(frame.xMax, frame.yMin));
        Vector3 topLeft = plane.TransformPoint(new Vector3(frame.xMin, frame.yMax));
        Vector3 fromEye = bottomLeft - eye;
        float distance = Vector3.Dot(fromEye, forward);
        if (!Finite(eye) || !Finite(right) || !Finite(up) || !Finite(forward)
            || float.IsNaN(distance) || float.IsInfinity(distance)
            || distance <= 1e-5f || frame.width <= 0f || frame.height <= 0f
            || Mathf.Abs(Vector3.Dot(right, up)) > .001f) return false;
        float near = Mathf.Max(1e-5f, distance * .005f);
        float far = Mathf.Max(near * 2f, distance * 8f);
        float left = Vector3.Dot(fromEye, right) * near / distance;
        float bottom = Vector3.Dot(fromEye, up) * near / distance;
        float rightEdge = Vector3.Dot(bottomRight - eye, right) * near / distance;
        float top = Vector3.Dot(topLeft - eye, up) * near / distance;
        view.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, eye)));
        view.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, eye)));
        view.SetRow(2, new Vector4(-forward.x, -forward.y, -forward.z, Vector3.Dot(forward, eye)));
        projection = Matrix4x4.Frustum(left, rightEdge, bottom, top, near, far);
        return true;
    }

    internal static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
