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
        Rect uv, Vector3 eye, Bounds stableModelBounds, out View result)
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
        // Build725 explicitly adjusts presentation: the native camera's old
        // framing/offset is not the desired physical cabinet size. Bounds come
        // from authored local renderer envelopes once per completed native model
        // identity, including companions; never from animated world bounds or a
        // viewer's first head pose. Native scene transforms remain untouched.
        Vector3 min = stableModelBounds.min, max = stableModelBounds.max;
        Bounds model = default;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 world = source.transform.TransformPoint(new Vector3((corner & 1) == 0 ? min.x : max.x,
                (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z)) - originalPosition;
            Vector3 point = new Vector3(Vector3.Dot(world, right), Vector3.Dot(world, up), Vector3.Dot(world, forward));
            if (corner == 0) model = new Bounds(point, Vector3.zero); else model.Encapsulate(point);
        }
        if (model.size.x <= 1e-4f || model.size.y <= 1e-4f || !Finite(model.center)) return false;
        float nativePerUnit = Mathf.Max(model.size.x / (physicalWidth * .82f), model.size.y / (physicalHeight * .75f));
        float scaleX = nativePerUnit, scaleY = nativePerUnit;
        float aperture = Mathf.Min(physicalWidth, physicalHeight);
        // At most14% of the aperture is occupied in depth; only a few percent
        // project in front of the glass. This is a fixed affine scene mapping,
        // not camera following or a flattened/fixed stereo pair.
        float scaleZ = Mathf.Max(nativePerUnit, model.size.z / (aperture * .14f));
        float protrusion = Mathf.Min(aperture * .035f, model.size.z / scaleZ * .30f);
        float planeDistance = model.min.z + protrusion * scaleZ;
        Vector3 croppedCenter = originalPosition + right * model.center.x + up * model.center.y + forward * planeDistance;
        // RawImage can crop or stretch the original render. Keep that authored
        // sampling and source camera viewport, rather than cropping the portal twice.
        float u = (uv.center.x - viewport.x) / viewport.width;
        float v = (uv.center.y - viewport.y) / viewport.height;
        float width = physicalWidth * scaleX * viewport.width / uv.width;
        float height = physicalHeight * scaleY * viewport.height / uv.height;
        Vector3 center = croppedCenter - right * ((u - .5f) * width) - up * ((v - .5f) * height);
        Vector3 position = croppedCenter + right * (Vector3.Dot(relativeEye, physicalRight) * scaleX)
            + up * (Vector3.Dot(relativeEye, physicalUp) * scaleY)
            + forward * (Vector3.Dot(relativeEye, physicalForward) * scaleZ);
        float distance = Vector3.Dot(center - position, forward);
        if (distance <= 1e-4f || !Finite(position)) return false;
        float frontDistance = model.min.z - Vector3.Dot(position - originalPosition, forward);
        float near = Mathf.Max(1e-5f, Mathf.Min(source.nearClipPlane, Mathf.Min(distance, Mathf.Max(1e-4f, frontDistance)) * .2f));
        float far = Mathf.Max(near * 2f, source.farClipPlane + distance);
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
