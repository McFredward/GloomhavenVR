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
    private const string NativeCardShaderPath = "assets/menudepth/menudepthnativecard.shader";
    private static readonly int EyeId = Shader.PropertyToID("_GvrMenuDepthEye");
    private static readonly int RightTextureId = Shader.PropertyToID("_RightTex");
    private static readonly int SampleInverseId = Shader.PropertyToID("_GvrHeadSampleInverse");
    private static readonly int SampleEyeId = Shader.PropertyToID("_GvrHeadSampleEye");
    private static readonly int PlanePointId = Shader.PropertyToID("_GvrPlanePoint");
    private static readonly int PlaneNormalId = Shader.PropertyToID("_GvrPlaneNormal");
    private static readonly Stack<int> PreviousEyes = new(4);
    private static readonly List<Graphic> BoundGraphics = new(16);
    private static readonly List<MenuDepthDisplay> HeadDisplays = new(8);
    private static readonly List<IMenuDepthWindowAnnotationSource> WindowAnnotations = new(8);
    private static readonly Matrix4x4[] AnnotationInverses = new Matrix4x4[8];
    private static readonly Vector4[] AnnotationRects = new Vector4[8];
    private static AssetBundle? _bundle;
    private static Shader? _displayShader;
    private static Shader? _headResolveShader;
    private static Shader? _depthShader;
    private static Shader? _characterShader;
    private static Shader? _nativeCardShader;
    private static string? _loadFailure;
    private static Camera? _capturingCamera;
    private static int _liveDisplays;
    private static int _currentEye;

    private readonly RawImage _image;
    private readonly Material _originalMaterial;
    private readonly Material _material;
    private readonly RenderTexture? _rightCapture;
    private RenderTexture? _headLeft, _headRight;
    private MenuDepthHeadCarrier? _headCarrier;
    private Camera? _headCamera;
    internal long HeadBytes { get; private set; }
    internal bool IsHeadRaster => _rightCapture == null;
    internal bool HasHeadPresentation => !_disposed && _headCarrier != null && _headCarrier.HasHeadPresentation;
    private readonly RenderTexture? _rightMip;
    private bool _disposed;
    private bool _rendering;
    private bool _disposeRequested;

    internal static Shader? DepthShader
    {
        get { TryLoad(out _); return _depthShader; }
    }

    internal static Shader HeadResolveShader
    {
        get
        {
            if (!TryLoad(out string reason)) throw new InvalidOperationException(reason);
            if (_headResolveShader == null && _bundle != null)
                _headResolveShader = _bundle.LoadAsset<Shader>("assets/menudepth/menudepthheadresolve.shader");
            return _headResolveShader != null && _headResolveShader.isSupported ? _headResolveShader
                : throw new InvalidOperationException("The menu-depth head antialias resolve shader is unavailable.");
        }
    }

    internal static Shader? CharacterShader
    {
        get { TryLoad(out _); return _characterShader; }
    }

    internal static Shader? NativeCardShader
    {
        get
        {
            if (!TryLoad(out _)) return null;
            if (_nativeCardShader == null && _bundle != null) _nativeCardShader = _bundle.LoadAsset<Shader>(NativeCardShaderPath);
            return _nativeCardShader != null && _nativeCardShader.isSupported ? _nativeCardShader : null;
        }
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
            MenuDepthHeadRaster.ReleaseFilter();
        }
    }

    private static void AcquireEyeCallbacks()
    {
        if (_liveDisplays++ != 0) return;
        PreviousEyes.Clear(); ApplyEye(0);
        Camera.onPreRender += BeforeCamera;
        Camera.onPostRender += AfterCamera;
    }

    internal MenuDepthDisplay(RawImage image, RenderTexture leftCapture, RenderTexture leftDisplay, bool headRaster = false)
    {
        if (!TryLoad(out string reason)) throw new InvalidOperationException(reason);
        _image = image;
        _originalMaterial = image.material;
        _material = CreateDisplayMaterial(leftDisplay, leftDisplay);
        _material.CopyPropertiesFromMaterial(_originalMaterial);
        if (headRaster) return;
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
        && (_rightCapture == null || (_rightCapture.IsCreated() && _rightCapture.width == leftCapture.width
        && _rightCapture.height == leftCapture.height && _rightCapture.antiAliasing == leftCapture.antiAliasing
        && (ReferenceEquals(leftCapture, leftDisplay) ? _rightMip == null
            : _rightMip != null && _rightMip.IsCreated() && _rightMip.width == leftDisplay.width
                && _rightMip.height == leftDisplay.height)));

    internal void PrepareHead(Camera head)
    {
        if (_headCamera == head) _headCarrier?.Draw(head);
    }

    internal static long HeadCost(Camera head, RenderTexture original)
    {
        return MenuDepthHeadRaster.Bytes(original.descriptor, MenuDepthHeadRaster.Size(head));
    }

    internal void RenderHead(Camera capture, RectTransform plane, Rect frame, RenderTexture original,
        MenuDepthEye left, MenuDepthEye right)
    {
        Vector2Int size = MenuDepthHeadRaster.Size(left.HeadCamera);
        if (_headLeft == null || _headRight == null || !MenuDepthHeadRaster.Matches(_headLeft, original.descriptor, size)
            || !MenuDepthHeadRaster.Matches(_headRight, original.descriptor, size))
        {
            if (size.x > SystemInfo.maxTextureSize || size.y > SystemInfo.maxTextureSize)
                throw new InvalidOperationException("The actual UI head raster exceeds the texture limit.");
            Release(_headLeft); Release(_headRight); _headLeft = _headRight = null; HeadBytes = 0;
            var descriptor = MenuDepthHeadRaster.Descriptor(original.descriptor, size);
            var l = new RenderTexture(descriptor) { name = "GloomhavenVR.MenuDepth.HeadLeft", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            var r = new RenderTexture(descriptor) { name = "GloomhavenVR.MenuDepth.HeadRight", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            if (!l.Create() || !r.Create()) { Release(l); Release(r); throw new InvalidOperationException("The actual UI head raster could not be created."); }
            _headLeft = l; _headRight = r;
            HeadBytes = HeadCost(left.HeadCamera, original);
        }
        if (_capturingCamera != null) throw new InvalidOperationException("A nested private head raster is not owned.");
        CameraState state = new(capture); Rect viewport = capture.rect;
        RenderTexture? active = RenderTexture.active;
        _capturingCamera = capture; _rendering = true;
        MenuDepthHeadRaster.FilterLease? filter = null;
        try
        {
            RectInt region = MenuDepthSources.HeadRegion(plane, plane.localToWorldMatrix, frame, left, right);
            filter = MenuDepthHeadRaster.Filter(original.descriptor, size, region, HeadResolveShader);
            MenuDepthEye sampledLeft = filter.Value.Eye(left), sampledRight = filter.Value.Eye(right);
            CaptureHeadEye(capture, plane, filter.Value.Target, sampledLeft, filter.Value.Pixels);
            MenuDepthSources.CollectWindowAnnotations(WindowAnnotations, plane);
            filter.Value.Resolve(_headLeft!, sampledLeft, plane.TransformPoint(new Vector3(frame.center.x, frame.center.y)),
                Vector3.Cross(plane.TransformVector(Vector3.right), plane.TransformVector(Vector3.up)).normalized,
                plane.worldToLocalMatrix, frame, WindowAnnotations);
            ThrowIfHeadOwnerClosed(_headRight!);
            CaptureHeadEye(capture, plane, filter.Value.Target, sampledRight, filter.Value.Pixels);
            MenuDepthSources.CollectWindowAnnotations(WindowAnnotations, plane);
            filter.Value.Resolve(_headRight!, sampledRight, plane.TransformPoint(new Vector3(frame.center.x, frame.center.y)),
                Vector3.Cross(plane.TransformVector(Vector3.right), plane.TransformVector(Vector3.up)).normalized,
                plane.worldToLocalMatrix, frame, WindowAnnotations);
            ThrowIfHeadOwnerClosed(_headRight!);
            SetHeadCapture(_material, _headLeft!, _headRight!, left, right,
                plane.TransformPoint(new Vector3(frame.center.x, frame.center.y)),
                Vector3.Cross(plane.TransformVector(Vector3.right), plane.TransformVector(Vector3.up)).normalized);
            MenuDepthSources.CollectWindowAnnotations(WindowAnnotations, plane);
            SetHeadClip(_material, plane.worldToLocalMatrix, frame, WindowAnnotations);
            _headCamera = left.HeadCamera;
            _headCarrier ??= new MenuDepthHeadCarrier(_image, _material);
            if (!HeadDisplays.Contains(this)) HeadDisplays.Add(this);
            _headCarrier.Draw(_headCamera);
        }
        finally
        {
            try { MenuDepthSources.AfterCapture(); }
            finally
            {
                try
                {
                    state.Restore(capture);
                    if (capture != null) capture.rect = viewport;
                    RenderTexture.active = active;
                }
                finally
                {
                    _capturingCamera = null; _rendering = false;
                    filter?.Dispose();
                    if (_disposeRequested) Dispose();
                }
            }
        }
    }

    internal static void SetHeadCapture(Material material, RenderTexture left, RenderTexture right,
        MenuDepthEye leftEye, MenuDepthEye rightEye, Vector3 point, Vector3 normal)
    {
        material.mainTexture = left; material.SetTexture(RightTextureId, right);
        material.SetTexture("_GvrDepthLeft", left, UnityEngine.Rendering.RenderTextureSubElement.Depth);
        material.SetTexture("_GvrDepthRight", right, UnityEngine.Rendering.RenderTextureSubElement.Depth);
        material.SetFloat("_GvrHeadRaster", 1f);
        material.SetMatrix("_GvrHeadInverseLeft", GpuClip(leftEye).inverse);
        material.SetMatrix("_GvrHeadInverseRight", GpuClip(rightEye).inverse);
        material.SetVector("_GvrHeadEyeLeft", leftEye.WorldPosition); material.SetVector("_GvrHeadEyeRight", rightEye.WorldPosition);
        material.SetVector("_GvrPlanePoint", point); material.SetVector("_GvrPlaneNormal", normal);
    }
    internal static void SetHeadClip(Material material, Matrix4x4 worldToFrame, Rect frame,
        IReadOnlyList<IMenuDepthWindowAnnotationSource> annotations,
        Func<IMenuDepthWindowAnnotationSource, Matrix4x4>? nativeToPhysical = null)
    {
        int count = 0;
        for (int i = 0; i < annotations.Count && count < AnnotationInverses.Length; i++)
        {
            IMenuDepthWindowAnnotationSource source = annotations[i];
            RectTransform rect = source.AnnotationRect;
            if (rect == null || rect.rect.width <= 0f || rect.rect.height <= 0f) continue;
            Matrix4x4 toPhysical = nativeToPhysical?.Invoke(source) ?? Matrix4x4.identity;
            AnnotationInverses[count] = rect.worldToLocalMatrix * toPhysical.inverse;
            Rect r = rect.rect;
            AnnotationRects[count] = new Vector4(r.xMin,r.yMin,r.xMax,r.yMax); count++;
        }
        material.SetMatrix("_GvrHeadGlassInverse", worldToFrame);
        material.SetVector("_GvrHeadGlassRect", new Vector4(frame.xMin,frame.yMin,frame.xMax,frame.yMax));
        material.SetMatrixArray("_GvrHeadAnnotationInverse", AnnotationInverses);
        material.SetVectorArray("_GvrHeadAnnotationRects", AnnotationRects);
        material.SetFloat("_GvrHeadAnnotationCount", count);
        material.SetFloat("_GvrHeadClipActive", 1f);
    }

    internal static void SetHeadSampleClip(Material material, MenuDepthEye sampledEye, Vector3 point, Vector3 normal,
        Matrix4x4 worldToFrame, Rect frame, IReadOnlyList<IMenuDepthWindowAnnotationSource> annotations,
        Func<IMenuDepthWindowAnnotationSource, Matrix4x4>? nativeToPhysical = null)
    {
        // Filter each physical quarter-pixel before combining color. Reusing
        // the final carrier's exact inverse/permission contract prevents an
        // opaque foreground neighbor from admitting unrelated off-window UI.
        material.SetMatrix(SampleInverseId, GpuClip(sampledEye).inverse);
        material.SetVector(SampleEyeId, sampledEye.WorldPosition);
        material.SetVector(PlanePointId, point); material.SetVector(PlaneNormalId, normal);
        SetHeadClip(material, worldToFrame, frame, annotations, nativeToPhysical);
    }

    private static Matrix4x4 GpuClip(MenuDepthEye eye)
    {
        Matrix4x4 projection = eye.RasterProjection;
        Matrix4x4 gpu = GL.GetGPUProjectionMatrix(projection, true);
        // The stored inverse reconstructs canonical sampled RT UVs. D3D-like
        // render targets flip projection Y to retain those UVs; undo only that
        // row before inversion, keeping the native/reversed depth encoding.
        if (SystemInfo.graphicsUVStartsAtTop) gpu.SetRow(1, -gpu.GetRow(1));
        return gpu * projection.inverse * eye.WorldToHeadClip;
    }

    private void CaptureHeadEye(Camera capture, RectTransform plane, RenderTexture target, MenuDepthEye eye, Vector2Int size)
    {
        Matrix4x4 projection = eye.RasterProjection;
        capture.targetTexture = target; capture.rect = new Rect(0,0,1,1);
        capture.orthographic = false; capture.nearClipPlane = eye.HeadCamera.nearClipPlane; capture.farClipPlane = eye.HeadCamera.farClipPlane;
        capture.worldToCameraMatrix = projection.inverse * eye.WorldToHeadClip;
        capture.projectionMatrix = projection; capture.cullingMatrix = eye.WorldToHeadClip;
        RenderTexture.active = target; GL.Clear(true, true, Color.clear);
        using (MenuDepthHeadRaster.Begin(size))
        {
            MenuDepthSources.BeforeCapture(plane,
                new MenuDepthEye(eye.Right, eye.WorldPosition, eye.HeadCamera, false, new Vector2(size.x,size.y), eye.WorldToHeadClip, eye.RasterProjection));
            try
            {
                ThrowIfHeadOwnerClosed(target);
                Canvas.ForceUpdateCanvases();
                ThrowIfHeadOwnerClosed(target);
                capture.Render();
            }
            finally { MenuDepthSources.AfterCapture(); }
        }
    }

    private void ThrowIfHeadOwnerClosed(RenderTexture target)
    {
        if (_disposeRequested || !target.IsCreated())
            throw new OperationCanceledException("The original menu window closed before its head draw.");
    }

    internal static Rect ForegroundFrame(RectTransform plane, Rect frame, MenuDepthEye left, MenuDepthEye right)
    {
        float x0 = frame.xMin, x1 = frame.xMax, y0 = frame.yMin, y1 = frame.yMax;
        MenuDepthSources.CollectReadyForegroundGraphics(BoundGraphics);
        for (int i = 0; i < BoundGraphics.Count; i++)
        {
            Graphic graphic = BoundGraphics[i];
            if (!graphic.transform.IsChildOf(plane)) continue;
            Matrix4x4 toPlane = plane.worldToLocalMatrix * graphic.transform.localToWorldMatrix;
            for (int eye = 0; eye < 2; eye++)
            {
                if (!MenuDepthSources.TryGetForegroundBounds(graphic, eye == 0 ? left : right, out Rect bounds)) continue;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = toPlane.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? bounds.xMin : bounds.xMax,
                        (corner & 2) == 0 ? bounds.yMin : bounds.yMax));
                    if (!MenuDepthProjection.Finite(point)) continue;
                    x0 = Mathf.Min(x0, point.x); x1 = Mathf.Max(x1, point.x);
                    y0 = Mathf.Min(y0, point.y); y1 = Mathf.Max(y1, point.y);
                }
            }
        }
        BoundGraphics.Clear();
        float qx = frame.width * .125f, qy = frame.height * .125f;
        return Rect.MinMaxRect(frame.xMin - Mathf.Ceil((frame.xMin - x0) / qx) * qx,
            frame.yMin - Mathf.Ceil((frame.yMin - y0) / qy) * qy,
            frame.xMax + Mathf.Ceil((x1 - frame.xMax) / qx) * qx,
            frame.yMax + Mathf.Ceil((y1 - frame.yMax) / qy) * qy);
    }

    /// <summary>The supplied eyes are actual XR observations, or explicit test inputs.</summary>
    internal void Render(Camera capture, RectTransform plane, Rect frame, RenderTexture leftCapture,
        RenderTexture leftDisplay, MenuDepthEye leftEye, MenuDepthEye rightEye, Rect? captureFrame = null)
    {
        if (!Matches(leftCapture, leftDisplay)) throw new InvalidOperationException("Menu-depth target pair changed before capture.");
        if (_capturingCamera != null) throw new InvalidOperationException("A nested private menu capture is not owned.");
        Rect sampledFrame = captureFrame ?? ForegroundFrame(plane, frame, leftEye, rightEye);
        CameraState original = new(capture);
        RenderTexture? active = RenderTexture.active;
        _capturingCamera = capture;
        _rendering = true;
        try
        {
            CaptureEye(capture, plane, frame, sampledFrame, leftCapture, leftDisplay, leftEye, out float leftDepth, out float leftScale);
            CaptureEye(capture, plane, frame, sampledFrame, _rightCapture!, _rightMip ?? _rightCapture!, rightEye, out float rightDepth, out float rightScale);
            if (_disposeRequested) throw new OperationCanceledException("The original menu window closed during its eye capture.");
            _material.SetTexture(RightTextureId, _rightMip ?? _rightCapture);
            BindCarrier(plane, sampledFrame);
            Rect originalUv = new((frame.xMin - sampledFrame.xMin) / sampledFrame.width,
                (frame.yMin - sampledFrame.yMin) / sampledFrame.height, frame.width / sampledFrame.width, frame.height / sampledFrame.height);
            Vector3 normal = Vector3.Cross(plane.TransformVector(Vector3.right).normalized, plane.TransformVector(Vector3.up).normalized).normalized;
            SetCaptureWindow(_material, leftCapture, _rightCapture!, originalUv, new Vector2(leftDepth,rightDepth),
                new Vector2(leftScale,rightScale), plane.TransformPoint(new Vector3(frame.center.x,frame.center.y)), normal);
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

    private void CaptureEye(Camera capture, RectTransform plane, Rect frame, Rect sampledFrame,
        RenderTexture target, RenderTexture display, MenuDepthEye eye, out float glassDepth, out float physicalDepthPerRawDepth)
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
        Matrix4x4 originalProjection = capture.projectionMatrix;
        Matrix4x4 originalCulling = capture.cullingMatrix;
        bool generatedCulling = Same(originalCulling, originalProjection * originalView);
        Matrix4x4 toClip = originalProjection * originalView * plane.localToWorldMatrix;
        Vector4 bottom = toClip * new Vector4(sampledFrame.xMin, sampledFrame.yMin, 0, 1);
        Vector4 top = toClip * new Vector4(sampledFrame.xMax, sampledFrame.yMax, 0, 1);
        Rect clip = Rect.MinMaxRect(bottom.x / bottom.w, bottom.y / bottom.w, top.x / top.w, top.y / top.w);
        Matrix4x4 projection = originalProjection;
        projection.SetRow(0, (originalProjection.GetRow(0) - originalProjection.GetRow(3) * clip.center.x) * (2f / clip.width));
        projection.SetRow(1, (originalProjection.GetRow(1) - originalProjection.GetRow(3) * clip.center.y) * (2f / clip.height));
        capture.projectionMatrix = projection;
        Vector3 planePoint = plane.TransformPoint(new Vector3(frame.center.x, frame.center.y));
        Vector3 normal = Vector3.Cross(plane.TransformVector(Vector3.right).normalized, plane.TransformVector(Vector3.up).normalized).normalized;
        if (Vector3.Dot(eye.WorldPosition - planePoint, normal) > 0f)
        {
            // The carrier is viewed from the rear. Keep the exact native XY
            // projection/glyph raster, but order physical surfaces from that
            // side of the glass instead of the private camera's front side.
            Vector4 z = originalView.GetRow(2);
            float atPlane = Vector4.Dot(z, new Vector4(planePoint.x, planePoint.y, planePoint.z, 1f));
            capture.worldToCameraMatrix = MatrixWithDepthSide(originalView, z, atPlane);
        }
        Matrix4x4 gpu = GL.GetGPUProjectionMatrix(capture.projectionMatrix, true) * capture.worldToCameraMatrix;
        glassDepth = RawDepth(gpu * new Vector4(planePoint.x,planePoint.y,planePoint.z,1f));
        Vector3 unitPoint = planePoint + normal;
        float delta = RawDepth(gpu * new Vector4(unitPoint.x,unitPoint.y,unitPoint.z,1f)) - glassDepth;
        if (Mathf.Abs(delta) < 1e-7f) throw new InvalidOperationException("The native UI depth cannot express the physical glass normal.");
        physicalDepthPerRawDepth = 1f / delta;
        if (generatedCulling) capture.ResetCullingMatrix();
        else capture.cullingMatrix = projection * capture.worldToCameraMatrix;
        MenuDepthSources.BeforeCapture(plane,
            new MenuDepthEye(eye.Right, eye.WorldPosition, eye.HeadCamera, projectToGraphicPlane: true,
                worldToHeadClip: eye.HasWorldToHeadClip ? eye.WorldToHeadClip : null, rasterProjection: eye.RasterProjection), capture);
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
        finally
        {
            MenuDepthSources.AfterCapture(); capture.worldToCameraMatrix = originalView;
            capture.projectionMatrix = originalProjection;
            if (generatedCulling) capture.ResetCullingMatrix(); else capture.cullingMatrix = originalCulling;
        }
    }

    internal static void SetCaptureWindow(Material material, RenderTexture left, RenderTexture right, Rect originalGlassUv,
        Vector2 glassDepth, Vector2 physicalDepthPerRawDepth, Vector3 planePoint, Vector3 planeNormal)
    {
        material.SetFloat("_GvrCaptureDepthActive", 1f);
        material.SetTexture("_GvrDepthLeft", left, UnityEngine.Rendering.RenderTextureSubElement.Depth);
        material.SetTexture("_GvrDepthRight", right, UnityEngine.Rendering.RenderTextureSubElement.Depth);
        material.SetVector("_GvrOriginalGlassUV", new Vector4(originalGlassUv.xMin,originalGlassUv.yMin,originalGlassUv.xMax,originalGlassUv.yMax));
        material.SetVector("_GvrGlassDepth", new Vector4(glassDepth.x,glassDepth.y,physicalDepthPerRawDepth.x,physicalDepthPerRawDepth.y));
        material.SetVector("_GvrPlanePoint", planePoint);
        material.SetVector("_GvrPlaneNormal", planeNormal.normalized);
    }

    private static float RawDepth(Vector4 projected)
    {
        float depth = projected.z / projected.w;
        UnityEngine.Rendering.GraphicsDeviceType api = SystemInfo.graphicsDeviceType;
        return api == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore || api == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
            ? depth * .5f + .5f : depth;
    }

    private void BindCarrier(RectTransform plane, Rect frame)
    {
        Matrix4x4 toGraphic = _image.rectTransform.worldToLocalMatrix * plane.localToWorldMatrix;
        Vector3 low = toGraphic.MultiplyPoint3x4(new Vector3(frame.xMin, frame.yMin));
        Vector3 high = toGraphic.MultiplyPoint3x4(new Vector3(frame.xMax, frame.yMax));
        _material.SetFloat("_GvrDisplayActive", 1f);
        _material.SetVector("_GvrDisplayRect", new Vector4(low.x,low.y,high.x,high.y));
        _material.SetMatrix("_GvrDisplayLocalToWorld", _image.rectTransform.localToWorldMatrix);
    }

    private static bool Same(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
        return true;
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
        private readonly Matrix4x4 _view, _projection, _culling;

        internal CameraState(Camera camera)
        {
            _localPosition = camera.transform.localPosition; _localRotation = camera.transform.localRotation;
            _localScale = camera.transform.localScale; _ortho = camera.orthographic;
            _near = camera.nearClipPlane; _far = camera.farClipPlane; _size = camera.orthographicSize;
            _aspect = camera.aspect; _fov = camera.fieldOfView; _target = camera.targetTexture;
            _view = camera.worldToCameraMatrix; _projection = camera.projectionMatrix; _culling = camera.cullingMatrix;
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
            camera.ResetCullingMatrix();
            if (!Same(_culling, camera.cullingMatrix)) camera.cullingMatrix = _culling;
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
        for (int i = 0; i < HeadDisplays.Count; i++)
            if (HeadDisplays[i]._headCamera == camera) HeadDisplays[i]._headCarrier?.Draw(camera);
    }
    private static void AfterCamera(Camera camera)
    {
        for (int i = 0; i < HeadDisplays.Count; i++)
            if (HeadDisplays[i]._headCamera == camera) HeadDisplays[i]._headCarrier?.EndDraw();
        ApplyEye(PreviousEyes.Count > 0 ? PreviousEyes.Pop() : 0);
    }
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
        HeadDisplays.Remove(this); _headCarrier?.Dispose(); _headCarrier = null;
        Release(_headLeft); Release(_headRight); _headLeft = _headRight = null; HeadBytes = 0;
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
        MenuDepthHeadRaster.ReleaseFilter();
        if (_liveDisplays != 0 || MenuDepthHeadRaster.FilterBorrowed) return; // Native programs may still be borrowed by the current draw.
        _displayShader = _depthShader = _characterShader = _headResolveShader = null; _loadFailure = null;
        if (_bundle != null) _bundle.Unload(true);
        _bundle = null;
    }
}
