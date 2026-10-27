using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Original orthographic UI cameras can have different near/far planes and canvas
/// distances. Share one plane-relative depth metric while retaining their exact XY
/// projection and native Canvas state. The surrounding stack clears once per eye.
/// </summary>
internal sealed class MenuDepthNativeUiScope : IDisposable
{
    private static readonly Dictionary<Camera, Canvas?> Canvases = new();
    private readonly Camera _camera;
    private readonly Matrix4x4 _projection, _culling, _view;
    private readonly float _near, _far;
    private readonly CameraClearFlags _clear;
    private readonly bool _automaticProjection, _automaticCulling, _automaticView;
    private bool _disposed;

    internal static Canvas? FindCanvas(Camera camera, IReadOnlyList<Graphic> graphics)
    {
        for (int i = 0; i < graphics.Count; i++)
        {
            Graphic graphic = graphics[i];
            Canvas? canvas = graphic != null && graphic.canvas != null ? graphic.canvas.rootCanvas : null;
            if (Matches(canvas, camera)) { Canvases[camera] = canvas; return canvas; }
        }
        if (Canvases.TryGetValue(camera, out Canvas? known) && Matches(known, camera)) return known;
        // One cold lookup per newly observed camera. Real graphics arriving later
        // take the event-maintained registry path above, never a recurring census.
        if (Canvases.ContainsKey(camera)) return null;
        Canvas? found = null;
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            if (Matches(canvas, camera)) { found = canvas; break; }
        Canvases[camera] = found;
        return found;
    }

    private static bool Matches(Canvas? canvas, Camera camera) => canvas != null
        && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == camera;

    internal static void Clear() => Canvases.Clear();

    internal MenuDepthNativeUiScope(Camera camera, Canvas? canvas, bool reversed = false)
    {
        _camera = camera; _clear = camera.clearFlags; _near = camera.nearClipPlane; _far = camera.farClipPlane;
        _view = camera.worldToCameraMatrix;
        camera.ResetWorldToCameraMatrix(); _automaticView = Same(camera.worldToCameraMatrix, _view);
        camera.worldToCameraMatrix = _view;
        _projection = camera.projectionMatrix;
        camera.ResetProjectionMatrix(); _automaticProjection = Same(camera.projectionMatrix, _projection);
        camera.projectionMatrix = _projection;
        _culling = camera.cullingMatrix;
        camera.ResetCullingMatrix(); _automaticCulling = Same(camera.cullingMatrix, _culling);
        camera.cullingMatrix = _culling;
        try
        {
            camera.clearFlags = CameraClearFlags.Nothing;
            if (canvas == null || !camera.orthographic) return;
            // All native UI cameras use the same complete-screen height metric.
            // A normalized depth unit is the geometric mean of both display axes, independent of the
            // native camera's arbitrary distance, clipping planes or UI pixel scale.
            float scale = Mathf.Sqrt(Mathf.Abs(_projection.m00 * _projection.m11)
                * camera.rect.width * camera.rect.height) * .5f;
            float plane = camera.worldToCameraMatrix.MultiplyPoint(canvas.transform.position).z;
            if (scale <= 1e-8f || float.IsNaN(plane) || float.IsInfinity(plane))
                throw new InvalidOperationException("The native UI plane has no finite common depth metric.");
            // A custom projection Z row makes Unity derive a different ScreenSpaceCamera
            // Canvas basis during Camera.Render. Real ortho clipping planes retain the
            // native projection/layout contract while sharing this depth metric.
            float distance = -plane, halfSpan = 1f / scale;
            camera.nearClipPlane = distance - halfSpan;
            camera.farClipPlane = distance + halfSpan;
            camera.ResetProjectionMatrix();
            if (!_automaticProjection)
            {
                Matrix4x4 adjusted = camera.projectionMatrix;
                adjusted.SetRow(0, _projection.GetRow(0)); adjusted.SetRow(1, _projection.GetRow(1));
                adjusted.SetRow(3, _projection.GetRow(3)); camera.projectionMatrix = adjusted;
            }
            if (reversed)
            {
                Matrix4x4 reflected = _view;
                reflected.SetRow(2, -_view.GetRow(2) + new Vector4(0f, 0f, 0f, 2f * plane));
                camera.worldToCameraMatrix = reflected;
            }
            camera.cullingMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;
        }
        catch { Dispose(); throw; }
    }

    private static bool Same(Matrix4x4 left, Matrix4x4 right)
    {
        for (int i = 0; i < 16; i++) if (Mathf.Abs(left[i] - right[i]) > 1e-6f) return false;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_camera == null) return;
        _camera.clearFlags = _clear;
        _camera.nearClipPlane = _near; _camera.farClipPlane = _far;
        if (_automaticView) _camera.ResetWorldToCameraMatrix(); else _camera.worldToCameraMatrix = _view;
        if (_automaticProjection) _camera.ResetProjectionMatrix(); else _camera.projectionMatrix = _projection;
        if (_automaticCulling) _camera.ResetCullingMatrix(); else _camera.cullingMatrix = _culling;
    }
}
