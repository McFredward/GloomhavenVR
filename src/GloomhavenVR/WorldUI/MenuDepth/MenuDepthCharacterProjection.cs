using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

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
    private static readonly FieldInfo? CreatorName = typeof(UICharacterCreatorWindow).GetField("nameStep", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CreatorQuest = typeof(UICharacterCreatorWindow).GetField("personalQuestStep", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? QuestHelp = typeof(UICharacterCreatorPersonalQuestStep).GetField("helpText", BindingFlags.Instance | BindingFlags.NonPublic);

    // Build752: the shared preview survives creation's page changes, but its
    // centred cabinet frame puts the actor in the native keyboard. Reserve the
    // side of that exact keyboard, and lift the slightly smaller quest preview
    // above that page's authored help line. Only the private presentation frame
    // changes: original UI/actors, native ground pivot and ordinary previews stay.
    private static void CreationLayout(RectTransform rectangle, out Rect fit, out float scale, out float ground, out float horizontalSpan)
    {
        fit = rectangle.rect; scale = 1f; ground = float.NegativeInfinity; horizontalSpan = CommonSpan;
        UICharacterCreatorWindow? creator = rectangle.GetComponentInParent<UICharacterCreatorWindow>(true);
        if (creator == null) return;
        Component? quest = CreatorQuest?.GetValue(creator) as Component;
        if (Open(quest))
        {
            scale = .88f;
            Component? help = QuestHelp?.GetValue(quest) as Component;
            if (help?.transform is RectTransform text)
                ground = InGraphic(rectangle, text).yMax + fit.height * .03f;
            return;
        }
        Component? name = CreatorName?.GetValue(creator) as Component;
        if (!Open(name)) return;
        UIKeyboard? keyboard = name!.GetComponentInChildren<UIKeyboard>(true);
        if (keyboard == null || !(keyboard.transform is RectTransform keys)) return;
        Rect occupied = InGraphic(rectangle, keys);
        float padding = fit.width * .05f;
        float left = occupied.xMin - padding - fit.xMin;
        float right = fit.xMax - occupied.xMax - padding;
        if (Mathf.Max(left, right) <= fit.width * .08f) return;
        // Build753: fitting a square2.4-unit frame into this upright strip made
        // every hero tiny. Use one stable3:5 portrait metric for all name-page
        // actors, retaining native relative heights and the vertical frame.
        // Prefer the left strip when equally free: the authored long scythe
        // points away from the keyboard, while the positive-X bear still fits.
        // This is not an animated equipment envelope or per-character autofit.
        horizontalSpan = CommonSpan * .6f;
        if (left >= right) fit.xMax = occupied.xMin - padding;
        else fit.xMin = occupied.xMax + padding;
    }

    private static bool Open(Component? step)
        => step != null && step.gameObject.activeInHierarchy && step.GetComponent<UIWindow>() is UIWindow window && window.IsOpen;

    private static Rect InGraphic(RectTransform graphic, RectTransform other)
    {
        Rect rect = other.rect;
        Matrix4x4 frame = graphic.worldToLocalMatrix * other.localToWorldMatrix;
        Vector3 a = frame.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMin));
        Vector3 b = frame.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMax));
        Vector3 c = frame.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMin));
        Vector3 d = frame.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMax));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x, c.x, d.x), Mathf.Min(a.y, b.y, c.y, d.y),
            Mathf.Max(a.x, b.x, c.x, d.x), Mathf.Max(a.y, b.y, c.y, d.y));
    }

    internal readonly struct View
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly Matrix4x4 WorldToCamera;
        internal readonly Matrix4x4 Projection;
        internal readonly Matrix4x4 NativeToGraphic;
        internal readonly Vector4 NativePlane;
        internal readonly float Near, Far;
        internal readonly bool Reversed;
        internal readonly bool Reflected;
        internal View(Vector3 position, Quaternion rotation, Matrix4x4 view, Matrix4x4 projection,
            float near, float far, bool reversed, Matrix4x4 nativeToGraphic, Vector4 nativePlane, bool? reflected = null)
        { Position = position; Rotation = rotation; WorldToCamera = view; Projection = projection;
            Near = near; Far = far; Reversed = reversed; Reflected = reflected ?? reversed; NativeToGraphic = nativeToGraphic; NativePlane = nativePlane; }

        internal View Expanded(Rect clip)
        {
            Matrix4x4 projection = Projection;
            projection.SetRow(0, (Projection.GetRow(0) - Projection.GetRow(3) * clip.center.x) * (2f / clip.width));
            projection.SetRow(1, (Projection.GetRow(1) - Projection.GetRow(3) * clip.center.y) * (2f / clip.height));
            return new View(Position, Rotation, WorldToCamera, projection, Near, Far, Reversed, NativeToGraphic, NativePlane, Reflected);
        }
    }

    internal static View Rotated(View original, Matrix4x4 rotation)
    {
        Matrix4x4 view = original.WorldToCamera * rotation;
        return new View(view.inverse.MultiplyPoint3x4(Vector3.zero), original.Rotation,
            view, original.Projection, original.Near, original.Far, original.Reversed,
            original.NativeToGraphic * rotation, rotation.transpose * original.NativePlane, original.Reflected);
    }

    internal static View Head(View portal, RectTransform graphic, MenuDepthEye eye)
    {
        if (!eye.HasWorldToHeadClip || eye.HeadCamera == null)
            throw new System.InvalidOperationException("The actual head raster has no eye projection.");
        Camera head = eye.HeadCamera;
        Matrix4x4 projection = eye.RasterProjection;
        Matrix4x4 scaledView = projection.inverse * eye.WorldToHeadClip * graphic.localToWorldMatrix * portal.NativeToGraphic;
        float sx = scaledView.MultiplyVector(Vector3.right).magnitude;
        float sy = scaledView.MultiplyVector(Vector3.up).magnitude;
        float sz = scaledView.MultiplyVector(Vector3.forward).magnitude;
        float scale = Mathf.Pow(sx * sy * sz, 1f / 3f);
        if (!(scale > 1e-8f)) throw new System.InvalidOperationException("The native head metric is singular.");
        Matrix4x4 view = scaledView;
        for (int row = 0; row < 3; row++) view.SetRow(row, view.GetRow(row) / scale);
        for (int row = 0; row < 4; row++) projection[row, 3] /= scale;
        Vector3 position = view.inverse.MultiplyPoint3x4(Vector3.zero);
        return new View(position, portal.Rotation, view, projection, head.nearClipPlane / scale,
            head.farClipPlane / scale, portal.Reversed, portal.NativeToGraphic, portal.NativePlane, view.determinant > 0f);
    }

    internal static bool TryBuild(Camera source, Transform holder, RectTransform rectangle,
        Rect uv, Vector3 eye, Vector3 stableMainOrigin, out View result, bool allowCoplanar = false)
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
        if (!MenuDepthProjection.Finite(relativeEye) || (!allowCoplanar && Mathf.Abs(signedDistance) <= 1e-5f)) return false;

        Matrix4x4 originalView = source.worldToCameraMatrix;
        Matrix4x4 cameraToWorld = originalView.inverse;
        Vector3 right = cameraToWorld.MultiplyVector(Vector3.right).normalized;
        Vector3 up = cameraToWorld.MultiplyVector(Vector3.up).normalized;
        Vector3 forward = cameraToWorld.MultiplyVector(Vector3.back).normalized;
        // Character3D.models[0] is the original main actor, before companions.
        // Remove only its native camera-fit translation. Keep the complete scene's
        // authored scale, facing, equipment, companion offsets and animation.
        CreationLayout(rectangle, out Rect fit, out float scale, out float ground, out float horizontalSpan);
        float fitWidth = fit.width == rect.width ? physicalWidth : physicalWidth * fit.width / rect.width;
        float fitHeight = fit.height == rect.height ? physicalHeight : physicalHeight * fit.height / rect.height;
        float nativePerUnit = Mathf.Max(horizontalSpan / (fitWidth * .82f), CommonSpan / (fitHeight * .75f)) / scale;
        Vector2 fitCenter = fit.center;
        if (!float.IsNegativeInfinity(ground))
            fitCenter.y = Mathf.Max(fitCenter.y, ground + CommonSpan * .5f / (nativePerUnit * rectangle.TransformVector(Vector3.up).magnitude));
        Vector3 presentationCenter = rectangle.TransformPoint(new Vector3(fitCenter.x, fitCenter.y));
        Vector3 presentationOffset = presentationCenter - physicalCenter;
        Vector3 croppedCenter = source.transform.TransformPoint(stableMainOrigin) + up * (CommonSpan * .5f);
        float u = (uv.center.x - viewport.x) / viewport.width;
        float v = (uv.center.y - viewport.y) / viewport.height;
        float width = physicalWidth * nativePerUnit * viewport.width / uv.width;
        float height = physicalHeight * nativePerUnit * viewport.height / uv.height;
        Vector3 apertureCenter = croppedCenter - (right * Vector3.Dot(presentationOffset, physicalRight)
            + up * Vector3.Dot(presentationOffset, physicalUp)) * nativePerUnit;
        Vector3 center = apertureCenter - right * ((u - .5f) * width) - up * ((v - .5f) * height);
        Vector3 position = apertureCenter + (right * Vector3.Dot(relativeEye, physicalRight)
            + up * Vector3.Dot(relativeEye, physicalUp) + forward * Vector3.Dot(relativeEye, physicalForward)) * nativePerUnit;
        bool reversed = signedDistance < 0f;
        Vector3 facing = reversed ? -forward : forward;
        float distance = Vector3.Dot(center - position, facing);
        if ((!allowCoplanar && distance <= 1e-4f) || !MenuDepthProjection.Finite(position)) return false;
        if (allowCoplanar) distance = Mathf.Max(distance, 1e-4f);
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
        Matrix4x4 nativeFrame = Matrix4x4.identity;
        nativeFrame.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, croppedCenter)));
        nativeFrame.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, croppedCenter)));
        nativeFrame.SetRow(2, new Vector4(forward.x, forward.y, forward.z, -Vector3.Dot(forward, croppedCenter)));
        Matrix4x4 physicalFrame = Matrix4x4.identity;
        physicalFrame.SetColumn(0, new Vector4(physicalRight.x, physicalRight.y, physicalRight.z, 0f) / nativePerUnit);
        physicalFrame.SetColumn(1, new Vector4(physicalUp.x, physicalUp.y, physicalUp.z, 0f) / nativePerUnit);
        physicalFrame.SetColumn(2, new Vector4(physicalForward.x, physicalForward.y, physicalForward.z, 0f) / nativePerUnit);
        physicalFrame.SetColumn(3, new Vector4(presentationCenter.x, presentationCenter.y, presentationCenter.z, 1f));
        result = new View(position, Quaternion.LookRotation(facing, up), view,
            Matrix4x4.Frustum(left, rightEdge, bottom, top, near, far), near, far, reversed,
            rectangle.worldToLocalMatrix * physicalFrame * nativeFrame, nativeFrame.GetRow(2));
        return true;
    }
}
