using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Borrow native UI depth/raster without changing its accepted layout or input geometry.</summary>
internal sealed class MenuDepthNativeUiScope : IDisposable
{
    private sealed class Family
    {
        internal Canvas? Primary;
        internal readonly List<Canvas> Roots = new(8);
    }
    private static readonly Dictionary<Camera, Family> Families = new();
    private static Canvas? _changingCanvas;
    private readonly Camera _camera;
    private readonly Matrix4x4 _projection, _culling, _view;
    private readonly CameraClearFlags _clear;
    private readonly float _near, _far;
    private readonly Rect _rect;
    private readonly RenderTexture? _target;
    private readonly bool _automaticProjection, _automaticCulling, _automaticView;
    private readonly List<CanvasState> _frozen = new(8);
    private bool _disposed;

    // Only the exact synchronous notifications caused by our own mode setter.
    // Native births, enable/parent/destroy callbacks and unknown Graphics still
    // pass through the observer; the caller also checks its known-Graphic set.
    internal static bool IsOwnHierarchyChange(Graphic graphic) => _changingCanvas != null
        && graphic != null && graphic.transform.IsChildOf(_changingCanvas.transform);

    internal static Canvas? FindCanvas(Camera camera, IReadOnlyList<Graphic> graphics)
    {
        if (!Families.TryGetValue(camera, out Family? family))
        { family = new Family(); Families.Add(camera, family); Census(camera, family); }
        for (int i = 0; i < graphics.Count; i++)
        {
            Graphic graphic = graphics[i];
            Canvas? canvas = graphic != null && graphic.canvas != null ? graphic.canvas.rootCanvas : null;
            if (!Matches(canvas, camera)) continue;
            if (!family.Roots.Contains(canvas!)) Census(camera, family); // a genuinely new registered root
            family.Primary = canvas; return canvas;
        }
        if (Matches(family.Primary, camera)) return family.Primary;
        // True null is the completed negative cold lookup. A retired/reassigned
        // positive wrapper is a new lifetime edge and gets exactly one new lookup.
        if (!ReferenceEquals(family.Primary, null)) Census(camera, family);
        return family.Primary;
    }

    private static void Census(Camera camera, Family family)
    {
        family.Roots.Clear(); family.Primary = null;
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            if (canvas.isRootCanvas && Matches(canvas, camera))
            { family.Roots.Add(canvas); family.Primary ??= canvas; }
    }
    private static bool Matches(Canvas? canvas, Camera camera) => canvas != null
        && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == camera;
    internal static void Clear() => Families.Clear();

    internal MenuDepthNativeUiScope(Camera camera, Canvas? canvas, bool reversed = false)
    {
        _camera = camera; _clear = camera.clearFlags; _near = camera.nearClipPlane; _far = camera.farClipPlane;
        _rect = camera.rect; _target = camera.targetTexture;
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
            // The geometric display metric matches TryMap's sqrt(xScale*yScale),
            // including native partial viewports and non-native physical aspect.
            float scale = Mathf.Sqrt(Mathf.Abs(_projection.m00 * _projection.m11)
                * camera.rect.width * camera.rect.height) * .5f;
            float plane = _view.MultiplyPoint(canvas.transform.position).z;
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

    /// <summary>
    /// One extended raster for the existing render. Ready-source eye mapping and
    /// native layout flush occur before this call, in the original camera domain.
    /// </summary>
    internal void ExpandCapture(RenderTexture target, Rect frame, IReadOnlyList<Graphic> graphics)
    {
        if (frame == new Rect(0f, 0f, 1f, 1f)) { _camera.targetTexture = target; return; }
        if (!_camera.orthographic || frame.width <= 0f || frame.height <= 0f)
            throw new InvalidOperationException("The native UI carrier has no finite orthographic raster.");
        FindCanvas(_camera, graphics);
        if (Families.TryGetValue(_camera, out Family? family))
            for (int i = 0; i < family.Roots.Count; i++)
            {
                Canvas canvas = family.Roots[i];
                if (!Matches(canvas, _camera) || !canvas.isActiveAndEnabled) continue;
                var state = new CanvasState(canvas); _frozen.Add(state); state.Freeze();
            }
        if (_frozen.Count == 0) throw new InvalidOperationException("No original UI Canvas can preserve its raster basis.");
        Matrix4x4 projection = _camera.projectionMatrix;
        // Original camera.rect is an authored portion of the complete UI image.
        // Fold it into the enlarged projection, retaining that exact old pixel
        // domain while allowing foreground geometry beyond its former frustum.
        float sx = _rect.width / frame.width, sy = _rect.height / frame.height;
        float ox = (2f * (_rect.x - frame.x) + _rect.width) / frame.width - 1f;
        float oy = (2f * (_rect.y - frame.y) + _rect.height) / frame.height - 1f;
        projection.SetRow(0, projection.GetRow(0) * sx + projection.GetRow(3) * ox);
        projection.SetRow(1, projection.GetRow(1) * sy + projection.GetRow(3) * oy);
        _camera.targetTexture = target; _camera.rect = new Rect(0f, 0f, 1f, 1f);
        _camera.projectionMatrix = projection;
        _camera.cullingMatrix = projection * _camera.worldToCameraMatrix;
    }

    private sealed class CanvasState
    {
        private readonly Canvas _canvas;
        private readonly RectTransform _rect;
        private readonly CanvasScaler? _scaler;
        private readonly bool _scalerEnabled;
        private readonly RenderMode _mode;
        private readonly Camera? _worldCamera;
        private readonly float _distance, _factor, _pixels;
        private readonly Vector2 _min, _max, _pivot, _size;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        internal CanvasState(Canvas canvas)
        {
            _canvas = canvas; _rect = (RectTransform)canvas.transform; _scaler = canvas.GetComponent<CanvasScaler>();
            _scalerEnabled = _scaler != null && _scaler.enabled; _mode = canvas.renderMode; _worldCamera = canvas.worldCamera;
            _distance = canvas.planeDistance; _factor = canvas.scaleFactor; _pixels = canvas.referencePixelsPerUnit;
            _min = _rect.anchorMin; _max = _rect.anchorMax; _pivot = _rect.pivot; _size = _rect.sizeDelta;
            _position = _rect.localPosition; _scale = _rect.localScale; _rotation = _rect.localRotation;
        }
        internal void Freeze()
        {
            if (_scalerEnabled) _scaler!.enabled = false;
            SetMode(RenderMode.WorldSpace); RestoreRect();
            _canvas.scaleFactor = _factor; _canvas.referencePixelsPerUnit = _pixels;
        }
        private void SetMode(RenderMode mode)
        {
            Canvas? prior = _changingCanvas; _changingCanvas = _canvas;
            try { _canvas.renderMode = mode; }
            finally { _changingCanvas = prior; }
        }
        private void RestoreRect()
        {
            _rect.anchorMin = _min; _rect.anchorMax = _max; _rect.pivot = _pivot; _rect.sizeDelta = _size;
            _rect.localPosition = _position; _rect.localRotation = _rotation; _rect.localScale = _scale;
        }
        internal void Restore()
        {
            if (_canvas == null) return;
            SetMode(_mode); _canvas.worldCamera = _worldCamera; _canvas.planeDistance = _distance; RestoreRect();
            if (_scaler != null) _scaler.enabled = _scalerEnabled;
            _canvas.scaleFactor = _factor; _canvas.referencePixelsPerUnit = _pixels;
        }
    }

    private static bool Same(Matrix4x4 left, Matrix4x4 right)
    { for (int i = 0; i < 16; i++) if (Mathf.Abs(left[i] - right[i]) > 1e-6f) return false; return true; }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try
        {
            if (_camera != null)
            {
                _camera.targetTexture = _target; _camera.rect = _rect; _camera.clearFlags = _clear;
                _camera.nearClipPlane = _near; _camera.farClipPlane = _far;
                if (_automaticView) _camera.ResetWorldToCameraMatrix(); else _camera.worldToCameraMatrix = _view;
                if (_automaticProjection) _camera.ResetProjectionMatrix(); else _camera.projectionMatrix = _projection;
                if (_automaticCulling) _camera.ResetCullingMatrix(); else _camera.cullingMatrix = _culling;
            }
        }
        finally { for (int i = _frozen.Count - 1; i >= 0; i--) _frozen[i].Restore(); _frozen.Clear(); }
    }
}
