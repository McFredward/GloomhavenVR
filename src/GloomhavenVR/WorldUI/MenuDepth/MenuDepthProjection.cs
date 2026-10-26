using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Parallel off-axis views through the actual window rectangle; no toe-in or gaze movement.</summary>
internal static class MenuDepthProjection
{
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
