using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Map a viewer through one fixed preview rectangle into the original scene.
/// Every actor uses one native-unit metric and its authored main-actor ground
/// axis; no equipment envelope, individual height or first eye establishes fit.
/// </summary>
internal static class MenuDepthCharacterProjection
{
    // Build726: native CharacterManager roots are unit-scale ground origins.
    // The actual short/tall body meshes differ (e.g. Mindthief1.23, Brute2.26).
    // One2.4-unit frame retains those proportions and leaves room above the tall
    // body; the native Height field is an overhead-UI estimate, not a fit metric.
    private const float CommonSpan = 2.4f;

    internal readonly struct View
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly Matrix4x4 WorldToCamera;
        internal readonly Matrix4x4 Projection;
        internal readonly float Near, Far;
        internal readonly bool Reversed;
        internal View(Vector3 position, Quaternion rotation, Matrix4x4 view, Matrix4x4 projection,
            float near, float far, bool reversed)
        { Position = position; Rotation = rotation; WorldToCamera = view; Projection = projection;
            Near = near; Far = far; Reversed = reversed; }
    }

    internal static bool TryBuild(Camera source, Transform holder, RectTransform rectangle,
        Rect uv, Vector3 eye, Vector3 stableMainOrigin, out View result)
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
        float signedDistance = -Vector3.Dot(relativeEye, physicalForward);
        if (!MenuDepthProjection.Finite(relativeEye) || Mathf.Abs(signedDistance) <= 1e-5f) return false;

        Matrix4x4 originalView = source.worldToCameraMatrix;
        Matrix4x4 cameraToWorld = originalView.inverse;
        Vector3 right = cameraToWorld.MultiplyVector(Vector3.right).normalized;
        Vector3 up = cameraToWorld.MultiplyVector(Vector3.up).normalized;
        Vector3 forward = cameraToWorld.MultiplyVector(Vector3.back).normalized;
        // Character3D.models[0] is the original main actor, before companions.
        // Remove only its native camera-fit translation. Keep the complete scene's
        // authored scale, facing, equipment, companion offsets and animation.
        float nativePerUnit = Mathf.Max(CommonSpan / (physicalWidth * .82f), CommonSpan / (physicalHeight * .75f));
        Vector3 croppedCenter = source.transform.TransformPoint(stableMainOrigin) + up * (CommonSpan * .5f);
        float u = (uv.center.x - viewport.x) / viewport.width;
        float v = (uv.center.y - viewport.y) / viewport.height;
        float width = physicalWidth * nativePerUnit * viewport.width / uv.width;
        float height = physicalHeight * nativePerUnit * viewport.height / uv.height;
        Vector3 center = croppedCenter - right * ((u - .5f) * width) - up * ((v - .5f) * height);
        Vector3 position = croppedCenter + (right * Vector3.Dot(relativeEye, physicalRight)
            + up * Vector3.Dot(relativeEye, physicalUp) + forward * Vector3.Dot(relativeEye, physicalForward)) * nativePerUnit;
        bool reversed = signedDistance < 0f;
        Vector3 facing = reversed ? -forward : forward;
        float distance = Vector3.Dot(center - position, facing);
        if (distance <= 1e-4f || !MenuDepthProjection.Finite(position)) return false;
        float near = Mathf.Max(1e-5f, Mathf.Min(source.nearClipPlane, distance * .2f));
        float far = Mathf.Max(near * 2f, source.farClipPlane + distance);
        Vector3 offset = center - position;
        float left = (Vector3.Dot(offset, right) - width * .5f) * near / distance;
        float bottom = (Vector3.Dot(offset, up) - height * .5f) * near / distance;
        float rightEdge = left + width * near / distance;
        float top = bottom + height * near / distance;
        Matrix4x4 view = Matrix4x4.identity;
        // Fixed right/up are also the native RawImage UV axes on the back. Rear
        // facing therefore reflects winding; the source owns/restores that draw
        // state. Flipping the right axis instead would mirror the 3D cabinet.
        view.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, position)));
        view.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, position)));
        view.SetRow(2, new Vector4(-facing.x, -facing.y, -facing.z, Vector3.Dot(facing, position)));
        result = new View(position, Quaternion.LookRotation(facing, up), view,
            Matrix4x4.Frustum(left, rightEdge, bottom, top, near, far), near, far, reversed);
        return true;
    }
}
