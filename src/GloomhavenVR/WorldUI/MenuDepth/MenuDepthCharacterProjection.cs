using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Map a viewer's position relative to one actual preview rectangle into the
/// original model scene. The authored camera/holder define a common virtual
/// scene; no opening is calibrated from the first viewer's head or saved zoom.
/// </summary>
internal static class MenuDepthCharacterProjection
{
    internal readonly struct View
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly Matrix4x4 WorldToCamera;
        internal readonly Matrix4x4 Projection;
        internal readonly float Near, Far;
        internal View(Vector3 position, Quaternion rotation, Matrix4x4 view, Matrix4x4 projection, float near, float far)
        { Position = position; Rotation = rotation; WorldToCamera = view; Projection = projection; Near = near; Far = far; }
    }

    internal static bool TryBuild(Camera source, Transform holder, RectTransform rectangle,
        Rect uv, Vector3 eye, out View result)
    {
        result = default;
        Rect rect = rectangle.rect;
        Rect viewport = source.rect;
        if (rect.width <= 0f || rect.height <= 0f || uv.width <= 0f || uv.height <= 0f
            || viewport.width <= 0f || viewport.height <= 0f) return false;
        Vector3 physicalRight = rectangle.TransformVector(Vector3.right);
        Vector3 physicalUp = rectangle.TransformVector(Vector3.up);
        float physicalWidth = rect.width * physicalRight.magnitude;
        float physicalHeight = rect.height * physicalUp.magnitude;
        if (physicalWidth <= 1e-5f || physicalHeight <= 1e-5f) return false;
        physicalRight.Normalize(); physicalUp.Normalize();
        if (Mathf.Abs(Vector3.Dot(physicalRight, physicalUp)) > .001f) return false;
        Vector3 physicalForward = Vector3.Cross(physicalRight, physicalUp).normalized;
        Vector3 physicalCenter = rectangle.TransformPoint(new Vector3(rect.center.x, rect.center.y));
        Vector3 relativeEye = eye - physicalCenter;
        float physicalDistance = -Vector3.Dot(relativeEye, physicalForward);
        if (physicalDistance <= 1e-5f) return false;

        Matrix4x4 originalView = source.worldToCameraMatrix;
        Matrix4x4 cameraToWorld = originalView.inverse;
        Vector3 originalPosition = cameraToWorld.MultiplyPoint(Vector3.zero);
        Vector3 right = cameraToWorld.MultiplyVector(Vector3.right).normalized;
        Vector3 up = cameraToWorld.MultiplyVector(Vector3.up).normalized;
        Vector3 forward = cameraToWorld.MultiplyVector(Vector3.back).normalized;
        float holderDistance = Vector3.Dot(holder.position - originalPosition, forward);
        // The source holder is an authored, stable scene anchor. Animated renderer
        // bounds would make the scale breathe and differ between participants.
        float planeDistance = holderDistance * .85f;
        Matrix4x4 originalProjection = source.projectionMatrix;
        if (planeDistance <= 1e-4f || originalProjection.m00 <= 1e-5f || originalProjection.m11 <= 1e-5f) return false;
        float width = 2f * (source.orthographic ? 1f : planeDistance) / originalProjection.m00;
        float height = 2f * (source.orthographic ? 1f : planeDistance) / originalProjection.m11;
        float lensX = source.orthographic ? -originalProjection.m03 / originalProjection.m00
            : originalProjection.m02 * planeDistance / originalProjection.m00;
        float lensY = source.orthographic ? -originalProjection.m13 / originalProjection.m11
            : originalProjection.m12 * planeDistance / originalProjection.m11;
        Vector3 center = originalPosition + forward * planeDistance + right * lensX + up * lensY;
        // RawImage can crop or stretch the original render. Keep that authored
        // sampling and source camera viewport, rather than cropping the portal twice.
        float u = (uv.center.x - viewport.x) / viewport.width;
        float v = (uv.center.y - viewport.y) / viewport.height;
        float croppedWidth = width * uv.width / viewport.width;
        float croppedHeight = height * uv.height / viewport.height;
        float scaleX = croppedWidth / physicalWidth;
        float scaleY = croppedHeight / physicalHeight;
        float scaleZ = Mathf.Sqrt(scaleX * scaleY);
        Vector3 croppedCenter = center + right * ((u - .5f) * width) + up * ((v - .5f) * height);
        Vector3 position = croppedCenter + right * (Vector3.Dot(relativeEye, physicalRight) * scaleX)
            + up * (Vector3.Dot(relativeEye, physicalUp) * scaleY)
            + forward * (Vector3.Dot(relativeEye, physicalForward) * scaleZ);
        float distance = Vector3.Dot(center - position, forward);
        if (distance <= 1e-4f || !Finite(position)) return false;
        float near = Mathf.Max(1e-5f, Mathf.Min(source.nearClipPlane, distance * .2f));
        float far = Mathf.Max(near * 2f, source.farClipPlane + Mathf.Max(0f, distance - planeDistance));
        Vector3 offset = center - position;
        float left = (Vector3.Dot(offset, right) - width * .5f) * near / distance;
        float bottom = (Vector3.Dot(offset, up) - height * .5f) * near / distance;
        float rightEdge = left + width * near / distance;
        float top = bottom + height * near / distance;
        Matrix4x4 view = Matrix4x4.identity;
        view.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, position)));
        view.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, position)));
        view.SetRow(2, new Vector4(-forward.x, -forward.y, -forward.z, Vector3.Dot(forward, position)));
        result = new View(position, Quaternion.LookRotation(forward, up), view,
            Matrix4x4.Frustum(left, rightEdge, bottom, top, near, far), near, far);
        return true;
    }

    private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
