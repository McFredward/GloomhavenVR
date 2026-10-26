using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Parallel off-axis views through the actual window rectangle; no toe-in or gaze movement.</summary>
internal static class MenuDepthProjection
{
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
        if (distance <= 1e-5f || frame.width <= 0f || frame.height <= 0f
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
}
