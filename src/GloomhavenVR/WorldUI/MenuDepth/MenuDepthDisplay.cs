using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Owns one window's additional right capture and its eye-selecting display material.
/// Both eyes use the same original UI hierarchy, filtering and input canvas. Only the
/// private capture camera is borrowed; every native-content lease is restored in finally.
/// </summary>
internal sealed class MenuDepthDisplay : IDisposable
{
    internal const string BundleResource = "GloomhavenVR.MenuDepth.Rendering.bundle";
    private const string DisplayShaderPath = "assets/menudepth/menudepthdisplay.shader";
    private const string DepthShaderPath = "assets/menudepth/menudepthgraphic.shader";
    private const string CharacterShaderPath = "assets/menudepth/menudepthcharacter.shader";
    private static readonly int EyeId = Shader.PropertyToID("_GvrMenuDepthEye");
    private static readonly int RightTextureId = Shader.PropertyToID("_RightTex");
    private static readonly Stack<int> PreviousEyes = new(4);
    private static AssetBundle? _bundle;
    private static Shader? _displayShader;
    private static Shader? _depthShader;
    private static Shader? _characterShader;
    private static string? _loadFailure;
    private static Camera? _capturingCamera;
    private static int _liveDisplays;
    private static int _currentEye;

    private readonly RawImage _image;
    private readonly Material _originalMaterial;
    private readonly Material _material;
    private readonly RenderTexture _rightCapture;
    private readonly RenderTexture? _rightMip;
    private bool _disposed;
    private bool _rendering;
    private bool _disposeRequested;

    internal static Shader? DepthShader
    {
        get { TryLoad(out _); return _depthShader; }
    }

    internal static Shader? CharacterShader
    {
        get { TryLoad(out _); return _characterShader; }
    }

    internal static Shader RequireCharacterShader()
    {
        if (!TryLoad(out string reason) || _characterShader == null) throw new InvalidOperationException(reason);
        return _characterShader;
    }

    internal static bool IsCapturing(Camera camera) => ReferenceEquals(camera, _capturingCamera);

    internal static bool TryGetEyePositions(Camera head, out Vector3 left, out Vector3 right)
    {
        left = right = Vector3.zero;
        if (head == null || !head.stereoEnabled) return false;
        left = head.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyPoint3x4(Vector3.zero);
        right = head.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.MultiplyPoint3x4(Vector3.zero);
        return MenuDepthProjection.Finite(left) && MenuDepthProjection.Finite(right) && (left - right).sqrMagnitude > 0f;
    }

    internal static Material CreateDisplayMaterial(Texture left, Texture right)
    {
        if (!TryLoad(out string reason)) throw new InvalidOperationException(reason);
        var material = new Material(_displayShader!)
        { name = "GloomhavenVR.MenuDepth.Display", hideFlags = HideFlags.HideAndDontSave };
        material.mainTexture = left;
        material.SetTexture(RightTextureId, right);
        AcquireEyeCallbacks();
        return material;
    }

    internal static void ReleaseDisplayMaterial(Material material)
    {
        UnityEngine.Object.Destroy(material);
        if (--_liveDisplays == 0)
        {
            Camera.onPreRender -= BeforeCamera; Camera.onPostRender -= AfterCamera;
            PreviousEyes.Clear(); ApplyEye(0);
        }
    }

    private static void AcquireEyeCallbacks()
    {
        if (_liveDisplays++ != 0) return;
        PreviousEyes.Clear(); ApplyEye(0);
        Camera.onPreRender += BeforeCamera;
        Camera.onPostRender += AfterCamera;
    }

    internal MenuDepthDisplay(RawImage image, RenderTexture leftCapture, RenderTexture leftDisplay)
    {
        if (!TryLoad(out string reason)) throw new InvalidOperationException(reason);
        _image = image;
        _originalMaterial = image.material;
        _material = CreateDisplayMaterial(leftDisplay, leftDisplay);
        _material.CopyPropertiesFromMaterial(_originalMaterial);
        _rightCapture = new RenderTexture(leftCapture.descriptor)
        {
            name = "GloomhavenVR.MenuDepth.RightCapture", hideFlags = HideFlags.HideAndDontSave,
            filterMode = leftCapture.filterMode, wrapMode = TextureWrapMode.Clamp,
        };
        try
        {
            if (!_rightCapture.Create()) throw new InvalidOperationException("Right menu-depth capture target was refused.");
            if (!ReferenceEquals(leftCapture, leftDisplay))
            {
                _rightMip = new RenderTexture(leftDisplay.descriptor)
                {
                    name = "GloomhavenVR.MenuDepth.RightDisplay", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = leftDisplay.filterMode, wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = leftDisplay.anisoLevel, mipMapBias = leftDisplay.mipMapBias,
                };
                if (!_rightMip.Create() || !_rightMip.useMipMap || _rightMip.mipmapCount <= 1)
                    throw new InvalidOperationException("Right menu-depth mip target was refused.");
            }
        }
        catch
        {
            Release(_rightCapture); Release(_rightMip); ReleaseDisplayMaterial(_material);
            throw;
        }
    }

    internal bool Matches(RenderTexture leftCapture, RenderTexture leftDisplay) => !_disposed
        && _rightCapture.IsCreated() && _rightCapture.width == leftCapture.width
        && _rightCapture.height == leftCapture.height && _rightCapture.antiAliasing == leftCapture.antiAliasing
        && (ReferenceEquals(leftCapture, leftDisplay) ? _rightMip == null
            : _rightMip != null && _rightMip.IsCreated() && _rightMip.width == leftDisplay.width
                && _rightMip.height == leftDisplay.height);

    /// <summary>The supplied eyes are actual XR observations, or explicit test inputs.</summary>
    internal void Render(Camera capture, RectTransform plane, Rect frame, RenderTexture leftCapture,
        RenderTexture leftDisplay, MenuDepthEye leftEye, MenuDepthEye rightEye)
    {
        if (!Matches(leftCapture, leftDisplay)) throw new InvalidOperationException("Menu-depth target pair changed before capture.");
        if (_capturingCamera != null) throw new InvalidOperationException("A nested private menu capture is not owned.");
        CameraState original = new(capture);
        RenderTexture? active = RenderTexture.active;
        _capturingCamera = capture;
        _rendering = true;
        try
        {
            CaptureEye(capture, plane, frame, leftCapture, leftDisplay, leftEye);
            CaptureEye(capture, plane, frame, _rightCapture, _rightMip ?? _rightCapture, rightEye);
            if (_disposeRequested) throw new OperationCanceledException("The original menu window closed during its eye capture.");
            _material.SetTexture(RightTextureId, _rightMip ?? _rightCapture);
            // Bind only after BOTH fresh eyes exist. Native mono capture remains the
            // fallback on every earlier allocation/preparation/decoder failure.
            _image.material = _material;
            // This first bind occurs in head pre-cull, after uGUI's normal rebuild.
            // Update the owned renderer immediately rather than waiting one frame
            // for RawImage.SetMaterialDirty to reach its native draw batch.
            _image.canvasRenderer.materialCount = 1;
            _image.canvasRenderer.SetMaterial(_material, 0);
            _image.canvasRenderer.SetTexture(leftDisplay);
        }
        finally
        {
            try { MenuDepthSources.AfterCapture(); }
            finally
            {
                try { original.Restore(capture); }
                finally
                {
                    _capturingCamera = null; _rendering = false; RenderTexture.active = active;
                    if (_disposeRequested) Dispose();
                }
            }
        }
    }

    private void CaptureEye(Camera capture, RectTransform plane, Rect frame,
        RenderTexture target, RenderTexture display, MenuDepthEye eye)
    {
        if (!capture.orthographic || !MenuDepthProjection.Finite(eye.WorldPosition))
            throw new InvalidOperationException("The original UI camera or tracked eye is unavailable.");
        // Keep the native orthographic projection for every ordinary glyph/control.
        // Sources analytically project their depth vertices onto their original
        // graphic plane. Character sources render their own actual 3D eye views.
        float planeDistance = Vector3.Dot(plane.TransformPoint(new Vector3(frame.center.x, frame.center.y))
            - capture.transform.position, capture.transform.forward);
        float relief = Mathf.Max(frame.width * Mathf.Abs(plane.lossyScale.x), frame.height * Mathf.Abs(plane.lossyScale.y)) * .05f;
        capture.nearClipPlane = Mathf.Max(1e-5f, Mathf.Min(capture.nearClipPlane, planeDistance - relief));
        capture.farClipPlane = Mathf.Max(capture.farClipPlane, planeDistance + relief);
        capture.targetTexture = target;
        Matrix4x4 originalView = capture.worldToCameraMatrix;
        Vector3 planePoint = plane.TransformPoint(new Vector3(frame.center.x, frame.center.y));
        Vector3 normal = Vector3.Cross(plane.TransformVector(Vector3.right), plane.TransformVector(Vector3.up)).normalized;
        if (Vector3.Dot(eye.WorldPosition - planePoint, normal) > 0f)
        {
            // The carrier is viewed from the rear. Keep the exact native XY
            // projection/glyph raster, but order physical surfaces from that
            // side of the glass instead of the private camera's front side.
            Vector4 z = originalView.GetRow(2);
            float atPlane = Vector4.Dot(z, new Vector4(planePoint.x, planePoint.y, planePoint.z, 1f));
            capture.worldToCameraMatrix = MatrixWithDepthSide(originalView, z, atPlane);
        }
        MenuDepthSources.BeforeCapture(plane,
            new MenuDepthEye(eye.Right, eye.WorldPosition, eye.HeadCamera, projectToGraphicPlane: true), capture);
        try
        {
            if (_disposeRequested || !target.IsCreated())
                throw new OperationCanceledException("The original menu window closed before its eye draw.");
            Canvas.ForceUpdateCanvases();
            capture.Render();
            if (!ReferenceEquals(target, display))
            {
                Graphics.Blit(target, display);
                display.GenerateMips();
            }
        }
        finally { MenuDepthSources.AfterCapture(); capture.worldToCameraMatrix = originalView; }
    }

    private static Matrix4x4 MatrixWithDepthSide(Matrix4x4 view, Vector4 z, float atPlane)
    {
        z = -z; z.w += 2f * atPlane; view.SetRow(2, z); return view;
    }

    private readonly struct CameraState
    {
        private readonly Vector3 _localPosition, _localScale;
        private readonly Quaternion _localRotation;
        private readonly bool _ortho;
        private readonly float _near, _far, _size, _aspect, _fov;
        private readonly RenderTexture? _target;
        private readonly Matrix4x4 _view, _projection;

        internal CameraState(Camera camera)
        {
            _localPosition = camera.transform.localPosition; _localRotation = camera.transform.localRotation;
            _localScale = camera.transform.localScale; _ortho = camera.orthographic;
            _near = camera.nearClipPlane; _far = camera.farClipPlane; _size = camera.orthographicSize;
            _aspect = camera.aspect; _fov = camera.fieldOfView; _target = camera.targetTexture;
            _view = camera.worldToCameraMatrix; _projection = camera.projectionMatrix;
        }

        internal void Restore(Camera camera)
        {
            if (camera == null) return;
            camera.transform.localPosition = _localPosition; camera.transform.localRotation = _localRotation;
            camera.transform.localScale = _localScale; camera.orthographic = _ortho;
            camera.nearClipPlane = _near; camera.farClipPlane = _far; camera.orthographicSize = _size;
            camera.aspect = _aspect; camera.fieldOfView = _fov; camera.targetTexture = _target;
            // Owned panel cameras normally use automatic matrices. Restoring those by
            // assigning their old numeric values would freeze the next drag/resize.
            camera.ResetWorldToCameraMatrix(); camera.ResetProjectionMatrix();
            if (!Same(_view, camera.worldToCameraMatrix)) camera.worldToCameraMatrix = _view;
            if (!Same(_projection, camera.projectionMatrix)) camera.projectionMatrix = _projection;
        }

        private static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
            return true;
        }
    }

    private static bool TryLoad(out string reason)
    {
        if (_displayShader != null && _depthShader != null && _characterShader != null
            && _displayShader.isSupported && _depthShader.isSupported && _characterShader.isSupported)
        { reason = string.Empty; return true; }
        if (_loadFailure != null) { reason = _loadFailure; return false; }
        try
        {
            using Stream? stream = typeof(MenuDepthDisplay).Assembly.GetManifestResourceStream(BundleResource);
            if (stream == null) throw new InvalidDataException("Embedded menu-depth rendering bundle is missing.");
            using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            _bundle = AssetBundle.LoadFromMemory(buffer.ToArray());
            if (_bundle == null) throw new InvalidDataException("Menu-depth rendering bundle could not load.");
            _displayShader = _bundle.LoadAsset<Shader>(DisplayShaderPath);
            _depthShader = _bundle.LoadAsset<Shader>(DepthShaderPath);
            _characterShader = _bundle.LoadAsset<Shader>(CharacterShaderPath);
            if (_displayShader == null || _depthShader == null || _characterShader == null
                || !_displayShader.isSupported || !_depthShader.isSupported || !_characterShader.isSupported)
                throw new InvalidDataException("Menu-depth UI shaders are unsupported on this graphics API.");
            reason = string.Empty; return true;
        }
        catch (Exception error)
        {
            _displayShader = _depthShader = _characterShader = null;
            if (_bundle != null) _bundle.Unload(true);
            _bundle = null; _loadFailure = reason = error.Message;
            return false;
        }
    }

    private static void BeforeCamera(Camera camera)
    {
        PreviousEyes.Push(_currentEye);
        ApplyEye(camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? 1 : 0);
    }
    private static void AfterCamera(Camera camera) => ApplyEye(PreviousEyes.Count > 0 ? PreviousEyes.Pop() : 0);
    private static void ApplyEye(int eye) { _currentEye = eye; Shader.SetGlobalFloat(EyeId, eye); }

    private static void Release(RenderTexture? texture)
    {
        if (texture == null) return;
        texture.Release(); UnityEngine.Object.Destroy(texture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_rendering) { _disposeRequested = true; return; }
        _disposed = true;
        if (_image != null && _image.material == _material)
        {
            _image.material = _originalMaterial;
            _image.canvasRenderer.materialCount = 1;
            _image.canvasRenderer.SetMaterial(_originalMaterial, 0);
            _image.canvasRenderer.SetTexture(_image.mainTexture);
        }
        Release(_rightCapture); Release(_rightMip); ReleaseDisplayMaterial(_material);
    }

    internal static void Shutdown()
    {
        if (_liveDisplays != 0) return; // Original native materials must be restored first.
        _displayShader = _depthShader = _characterShader = null; _loadFailure = null;
        if (_bundle != null) _bundle.Unload(true);
        _bundle = null;
    }
}
