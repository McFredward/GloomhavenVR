using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// One original preview's view of the native model scene. The original Camera
/// renders its own materials, lights and camera-hosted effects into owned eye
/// targets under a strict finally-restored lease. Its Update/animation is never
/// advanced here, and its native target remains the normal game's mono output.
/// </summary>
internal sealed class MenuDepthCharacterSource : IMenuDepthDirectSource, IMenuDepthBoundsSource, IDisposable
{
    private const long MaxPoolBytes = 96L * 1024 * 1024;
    private static readonly int[] ResolutionTiers = { 256, 512, 768, 1024, 1536, 2048 };
    private static readonly Dictionary<Character3DDisplayManager, CalibrationCache> Calibrations = new();
    private static readonly Func<Character3DDisplayManager, object?>? ReadOriginal = CreateOriginalReader();
    private static long _poolBytes;
    private readonly RawImage _image;
    private readonly Camera _camera;
    private readonly Character3DDisplayManager _manager;
    private readonly Transform _holder;
    private readonly Func<Character3DDisplayManager, bool> _readHidden;
    private readonly RenderTexture _nativeTarget;
    private readonly CalibrationCache _calibration;
    private readonly MenuDepthCharacterSurface _surface;
    private RenderTexture? _left, _right, _borrowed;
    private Texture? _restoreTexture;
    private bool _disposed;
    private bool _rendering;
    private int _width, _height;
    private long _pairBytes;
    private float _lastCapture, _lowerSince = -1f, _cropShrinkSince = -1f;
    private int _directBatchFrame = -1;
    private object? _captureIdentity;
    private Rect _captureClip;
    private Rect _imageClip;
    private Vector2 _captureScale = Vector2.one;
    private int _samplingEdge;
    internal int SamplingEdge => _samplingEdge;

    internal MenuDepthCharacterSource(RawImage image, Camera camera,
        Character3DDisplayManager manager, Transform holder, Func<Character3DDisplayManager, bool> readHidden)
    {
        _image = image; _camera = camera; _manager = manager; _holder = holder; _readHidden = readHidden;
        _nativeTarget = camera.targetTexture;
        MenuDepthDisplay.RequireCharacterShader();
        if (!Calibrations.TryGetValue(manager, out CalibrationCache cache))
            Calibrations.Add(manager, cache = new CalibrationCache(camera, holder));
        _calibration = cache; cache.References++;
        _surface = image.gameObject.AddComponent<MenuDepthCharacterSurface>();
        _surface.Configure();
    }

    public Graphic Graphic => _image;
    internal bool BindingAlive => !_disposed && _image != null && _camera != null && _manager != null
        && _holder != null && _nativeTarget != null && (_rendering || ReferenceEquals(_camera.targetTexture, _nativeTarget))
        && (ReferenceEquals(_image.texture, _nativeTarget) || ReferenceEquals(_image.texture, _borrowed));
    public bool IsReady => BindingAlive && _image.isActiveAndEnabled && _image.gameObject.activeInHierarchy
        && _camera.isActiveAndEnabled && _manager.isActiveAndEnabled
        && _holder.gameObject.activeInHierarchy && _nativeTarget.IsCreated()
        && (ReadOriginal == null || ReadOriginal(_manager) != null)
        && !_readHidden(_manager);

    public void PrimeDirectCapture(MenuDepthEye samplingEye)
    {
        AfterCapture();
        if (!IsReady || _rendering) throw new InvalidOperationException("Original character preview cannot prime its UI batch.");
        object original = ReadOriginal?.Invoke(_manager) ?? _holder;
        if (!_calibration.TryGet(original, out Vector3 origin)
            || !MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform,
                _image.uvRect, samplingEye.WorldPosition, origin, out MenuDepthCharacterProjection.View view))
            throw new InvalidOperationException("Original character preview has no valid direct batch frame.");
        Rect clip = SamplingClip(original, view);
        if (MenuDepthDisplay.TryGetEyePositions(samplingEye.HeadCamera, out Vector3 left, out Vector3 right))
        {
            if (MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform, _image.uvRect, left, origin, out var l)) clip = Union(clip, SamplingClip(original, l));
            if (MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform, _image.uvRect, right, origin, out var r)) clip = Union(clip, SamplingClip(original, r));
        }
        SetCaptureClip(original, clip);
        if (_directBatchFrame != Time.frameCount)
        {
            SelectResolution(samplingEye.PixelFootprint);
            _directBatchFrame = Time.frameCount;
        }
        Borrow(EnsureTarget(false), _image.texture);
    }

    public void BeforeDirectCapture(MenuDepthEye eye) => Capture(eye, direct: true);

    public void CancelDirectCapture()
    {
        AfterCapture();
        if (_left == null || !_left.IsCreated()) return;
        RenderTexture? previous = RenderTexture.active;
        try
        {
            // The already-built Canvas retains left's texture identity. A field
            // restore alone cannot give that batch its original mono fallback.
            if (_nativeTarget != null && _nativeTarget.IsCreated()) Graphics.Blit(_nativeTarget, _left);
            else { RenderTexture.active = _left; GL.Clear(true, true, Color.clear); }
            if (_left.useMipMap) _left.GenerateMips();
        }
        finally { RenderTexture.active = previous; }
    }

    public void BeforeCapture(MenuDepthEye eye) => Capture(eye, direct: false);

    private void Capture(MenuDepthEye eye, bool direct)
    {
        AfterCapture();
        if (!IsReady || _rendering) throw new InvalidOperationException("Original character preview is not ready for this capture.");
        object identity = ReadOriginal?.Invoke(_manager) ?? _holder;
        if (!_calibration.TryGet(identity, out Vector3 origin))
            throw new InvalidOperationException("Original character has no completed main actor origin.");
        if (!MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform,
            _image.uvRect, eye.WorldPosition, origin, out MenuDepthCharacterProjection.View view))
            throw new InvalidOperationException("Original character preview has no valid window portal.");
        view = ExpandCapture(identity, view);
        // A native Canvas batch has already retained this frame's left texture
        // identity. Refresh that same surface for either direct eye; only an early
        // batch boundary may resize it. No third surface or late sampler swap.
        if (_directBatchFrame != Time.frameCount) SelectResolution(eye.PixelFootprint);
        RenderTexture target = EnsureTarget(!direct && eye.Right);
        _lastCapture = Time.unscaledTime;
        Texture original = _image.texture;
        RenderOriginal(target, view);
        // A native render callback may replace a pooled preview. Do not overwrite
        // that new original with an eye of the retired character.
        if (!BindingAlive || !ReferenceEquals(_image.texture, original))
            throw new InvalidOperationException("Original character preview changed during its render.");
        _surface.Capture(view, target, _camera.rect, _image.uvRect, _captureClip);
        Borrow(target, original);
    }

    private void Borrow(RenderTexture target, Texture original)
    {
        _restoreTexture = original;
        _borrowed = target;
        _image.texture = target;
        _image.canvasRenderer.SetTexture(target);
        // A nested native scene render may already have rebuilt this Canvas's
        // material using mono state. Refresh that exact original batch material
        // after its new sampling/depth mapping exists, without a global rebuild.
        _image.canvasRenderer.materialCount = 1;
        _image.canvasRenderer.SetMaterial(_image.materialForRendering, 0);
    }

    public void AfterCapture()
    {
        RenderTexture? borrowed = _borrowed;
        _borrowed = null;
        if (_image != null && borrowed != null && ReferenceEquals(_image.texture, borrowed))
        {
            _image.texture = _restoreTexture;
            _image.canvasRenderer.SetTexture(_restoreTexture);
        }
        _restoreTexture = null;
        _surface.EndCapture();
    }

    private RenderTexture EnsureTarget(bool right)
    {
        RenderTexture? target = right ? _right : _left;
        if (target != null && (!target.IsCreated() || target.width != _width || target.height != _height
            || target.graphicsFormat != _nativeTarget.graphicsFormat || target.antiAliasing != _nativeTarget.antiAliasing))
        {
            Release(target);
            if (right) _right = null; else _left = null;
            target = null;
        }
        if (target == null)
        {
            RenderTextureDescriptor descriptor = _nativeTarget.descriptor;
            descriptor.width = _width; descriptor.height = _height;
            descriptor.vrUsage = VRTextureUsage.None;
            descriptor.volumeDepth = 1;
            descriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
            descriptor.depthBufferBits = Math.Max(24, descriptor.depthBufferBits);
            target = new RenderTexture(descriptor)
            {
                name = "GloomhavenVR.MenuDepth.Character." + (right ? "Right" : "Left"),
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = _nativeTarget.filterMode,
                wrapMode = _nativeTarget.wrapMode,
                anisoLevel = _nativeTarget.anisoLevel,
                mipMapBias = _nativeTarget.mipMapBias,
            };
            if (!target.Create()) { Object.Destroy(target); throw new InvalidOperationException("Character eye target creation failed."); }
            RenderTexture? previous = RenderTexture.active;
            try { RenderTexture.active = target; GL.Clear(true, true, Color.clear); }
            finally { RenderTexture.active = previous; }
            if (right) _right = target; else _left = target;
        }
        return target;
    }

    private void SelectResolution(Vector2 footprint)
    {
        float longest = Mathf.Max(_nativeTarget.width, _nativeTarget.height);
        float scale = footprint.x > 0f && footprint.y > 0f
            ? Mathf.Max(footprint.x / (Mathf.Abs(_image.uvRect.width) * _nativeTarget.width),
                footprint.y / (Mathf.Abs(_image.uvRect.height) * _nativeTarget.height)) : 1f;
        float wanted = Mathf.Clamp(longest * scale, ResolutionTiers[0], ResolutionTiers[ResolutionTiers.Length - 1]);
        int current = _samplingEdge;
        bool shapeChanged = current > 0 && (Mathf.CeilToInt(_nativeTarget.width * current / longest * _captureScale.x / 16f) * 16 != _width
            || Mathf.CeilToInt(_nativeTarget.height * current / longest * _captureScale.y / 16f) * 16 != _height);
        // Hold a lower demand for1.5s before shrinking. Head jitter at a tier
        // boundary cannot repeatedly allocate/release native GPU resources.
        if (current > 0 && wanted < current * .55f)
        {
            if (_lowerSince < 0f) _lowerSince = Time.unscaledTime;
            if (Time.unscaledTime - _lowerSince < 1.5f)
            { if (!shapeChanged) return; wanted = current; }
        }
        else
        {
            _lowerSince = -1f;
            if (current > 0 && wanted <= current * 1.12f)
            { if (!shapeChanged) return; wanted = current; }
        }
        long available = MaxPoolBytes - _poolBytes + _pairBytes;
        int selected = 0, width = 0, height = 0; long bytes = 0;
        for (int i = 0; i < ResolutionTiers.Length; i++)
        {
            int edge = ResolutionTiers[i];
            int w = Mathf.Max(16, Mathf.CeilToInt(_nativeTarget.width * edge / longest * _captureScale.x / 16f) * 16);
            int h = Mathf.Max(16, Mathf.CeilToInt(_nativeTarget.height * edge / longest * _captureScale.y / 16f) * 16);
            long cost = PairCost(w, h);
            if (cost > available || w > SystemInfo.maxTextureSize || h > SystemInfo.maxTextureSize) break;
            selected = edge; width = w; height = h; bytes = cost;
            if (edge >= wanted) break;
        }
        if (selected == 0) throw new InvalidOperationException("Character eye pool is fully reserved.");
        if (width == _width && height == _height) return;
        ReleaseTargets();
        _width = width; _height = height; _samplingEdge = selected; _pairBytes = bytes; _poolBytes += bytes;
        _lowerSince = -1f;
    }

    public bool TryGetForegroundBounds(MenuDepthEye eye, out Rect graphicLocalBounds)
    {
        graphicLocalBounds = default;
        if (!IsReady) return false;
        object identity = ReadOriginal?.Invoke(_manager) ?? _holder;
        if (!_calibration.TryGet(identity, out Vector3 origin)
            || !MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform,
                _image.uvRect, eye.WorldPosition, origin, out var view)
            || !ProjectedBounds(identity, view, foregroundOnly: true, out Rect clip)) return false;
        graphicLocalBounds = GraphicRect(clip);
        return true;
    }

    private Rect ImageClip()
    {
        Rect uv = _image.uvRect, viewport = _camera.rect;
        return Rect.MinMaxRect((uv.xMin - viewport.x) / viewport.width * 2f - 1f,
            (uv.yMin - viewport.y) / viewport.height * 2f - 1f,
            (uv.xMax - viewport.x) / viewport.width * 2f - 1f,
            (uv.yMax - viewport.y) / viewport.height * 2f - 1f);
    }

    private Rect GraphicRect(Rect clip)
    {
        Rect uv = _image.uvRect, viewport = _camera.rect, rect = _image.rectTransform.rect;
        float u0 = viewport.x + (clip.xMin + 1f) * .5f * viewport.width;
        float u1 = viewport.x + (clip.xMax + 1f) * .5f * viewport.width;
        float v0 = viewport.y + (clip.yMin + 1f) * .5f * viewport.height;
        float v1 = viewport.y + (clip.yMax + 1f) * .5f * viewport.height;
        return Rect.MinMaxRect(rect.xMin + (u0 - uv.xMin) / uv.width * rect.width,
            rect.yMin + (v0 - uv.yMin) / uv.height * rect.height,
            rect.xMin + (u1 - uv.xMin) / uv.width * rect.width,
            rect.yMin + (v1 - uv.yMin) / uv.height * rect.height);
    }

    private MenuDepthCharacterProjection.View ExpandCapture(object identity, MenuDepthCharacterProjection.View view)
    {
        SetCaptureClip(identity, SamplingClip(identity, view));
        return view.Expanded(_captureClip);
    }

    private Rect SamplingClip(object identity, MenuDepthCharacterProjection.View view)
        => ProjectedBounds(identity, view, foregroundOnly: false, out Rect clip) ? clip : ImageClip();

    private void SetCaptureClip(object identity, Rect desired)
    {
        Rect imageClip = ImageClip();
        if (!ReferenceEquals(identity, _captureIdentity) || imageClip != _imageClip)
        { _captureIdentity = identity; _imageClip = imageClip; _captureClip = desired; _cropShrinkSince = -1f; }
        Vector2 size = Vector2.Max(_captureClip.size, desired.size);
        bool smaller = desired.width < size.x * .75f || desired.height < size.y * .75f;
        if (smaller)
        {
            if (_cropShrinkSince < 0f) _cropShrinkSince = Time.unscaledTime;
            if (Time.unscaledTime - _cropShrinkSince >= 1.5f) { size = desired.size; _cropShrinkSince = -1f; }
        }
        else _cropShrinkSince = -1f;
        // Recentring a sampling crop only changes shader/projection uniforms.
        // Its quantized allocation grows immediately but shrinks only after a
        // sustained change, so small head/bone motion cannot churn GPU targets.
        _captureClip = new Rect(desired.center - size * .5f, size);
        _captureScale = size * .5f;
    }

    private bool ProjectedBounds(object identity, MenuDepthCharacterProjection.View view, bool foregroundOnly, out Rect clip)
    {
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        bool any = false;
        Matrix4x4 projection = view.Projection * view.WorldToCamera;
        Renderer[] renderers = _calibration.SamplingRenderers(identity);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                || (_camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
            Bounds bounds = renderer.bounds; Vector3 lo = bounds.min, hi = bounds.max;
            if (foregroundOnly)
            {
                bool front = false;
                for (int corner = 0; corner < 8 && !front; corner++)
                {
                    Vector4 point = new((corner & 1) == 0 ? lo.x : hi.x,
                        (corner & 2) == 0 ? lo.y : hi.y, (corner & 4) == 0 ? lo.z : hi.z, 1f);
                    front = Vector4.Dot(view.NativePlane, point) * (view.Reversed ? 1f : -1f) > 0f;
                }
                if (!front) continue;
            }
            // Whole world-space renderer AABBs conservatively include their
            // foreground portion and all intersections with the glass plane.
            for (int corner = 0; corner < 8; corner++)
            {
                Vector4 point = projection * new Vector4((corner & 1) == 0 ? lo.x : hi.x,
                    (corner & 2) == 0 ? lo.y : hi.y, (corner & 4) == 0 ? lo.z : hi.z, 1f);
                if (point.w <= 1e-4f) continue;
                float x = point.x / point.w, y = point.y / point.w;
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) continue;
                any = true;
                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
        }
        if (!any) { clip = default; return false; }
        Rect imageClip = ImageClip(); float qx = imageClip.width * .125f, qy = imageClip.height * .125f;
        Vector2 center = new((x0 + x1) * .5f, (y0 + y1) * .5f);
        Vector2 size = new(Mathf.Ceil((x1 - x0) / qx) * qx + qx * 2f,
            Mathf.Ceil((y1 - y0) / qy) * qy + qy * 2f);
        // Crop sampling, never native geometry. A fixed expanded frustum would
        // squander close-view samples on empty portrait pixels at an oblique eye.
        clip = new Rect(center - size * .5f, size);
        return true;
    }

    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin,b.xMin), Mathf.Min(a.yMin,b.yMin),
        Mathf.Max(a.xMax,b.xMax), Mathf.Max(a.yMax,b.yMax));

    private long PairCost(int width, int height)
    {
        long color = Math.Max(4L, (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(_nativeTarget.graphicsFormat));
        long depth = 4L;
        long bytes = width * (long)height * (color + depth) * Math.Max(1, _nativeTarget.antiAliasing) * 2;
        return _nativeTarget.useMipMap ? bytes * 4 / 3 : bytes;
    }

    // Called by the bounded known-consumer maintenance loop, never a scene scan.
    internal void MaintainResources(float now)
    {
        if (!_rendering && _borrowed == null && _pairBytes > 0 && now - _lastCapture > 2f) ReleaseTargets();
    }

    internal RenderTexture? DepthTexture(bool right) => right ? _right : _left;

    private void RenderOriginal(RenderTexture target, MenuDepthCharacterProjection.View view)
    {
        // Keep source-owned image effects and their native configuration. Copying
        // a Camera component alone loses Beautify and other attached callbacks.
        RenderTexture originalTarget = _camera.targetTexture;
        RenderTexture? originalActive = RenderTexture.active;
        Vector3 originalPosition = _camera.transform.position;
        Quaternion originalRotation = _camera.transform.rotation;
        Matrix4x4 originalView = _camera.worldToCameraMatrix;
        Matrix4x4 originalProjection = _camera.projectionMatrix;
        Matrix4x4 originalCulling = _camera.cullingMatrix;
        // Public camera setters make an automatic matrix explicit. Returning
        // only its numeric snapshot would freeze later native FOV/pose changes.
        // Detect the normal generated values before borrowing them; preserve a
        // genuinely different custom matrix exactly instead.
        _camera.ResetWorldToCameraMatrix();
        bool generatedView = Same(originalView, _camera.worldToCameraMatrix);
        if (!generatedView) _camera.worldToCameraMatrix = originalView;
        _camera.ResetProjectionMatrix();
        bool generatedProjection = Same(originalProjection, _camera.projectionMatrix);
        if (!generatedProjection) _camera.projectionMatrix = originalProjection;
        _camera.ResetCullingMatrix();
        bool generatedCulling = Same(originalCulling, _camera.cullingMatrix);
        if (!generatedCulling) _camera.cullingMatrix = originalCulling;
        float originalNear = _camera.nearClipPlane, originalFar = _camera.farClipPlane;
        bool originalOrtho = _camera.orthographic;
        bool originalCullingInverted = GL.invertCulling;
        try
        {
            _rendering = true;
            RenderTexture.active = target;
            GL.Clear(true, true, _camera.backgroundColor);
            _camera.targetTexture = target;
            // The actual native holder (including models and lights) is a child
            // of this Camera in both game scenes. Moving the Camera Transform to
            // an eye also moved the entire cabinet scene with the viewer. Borrow
            // only its view/projection; keep every native scene Transform fixed.
            _camera.orthographic = false;
            _camera.nearClipPlane = view.Near; _camera.farClipPlane = view.Far;
            _camera.worldToCameraMatrix = view.WorldToCamera;
            _camera.projectionMatrix = view.Projection;
            _camera.cullingMatrix = view.Projection * view.WorldToCamera;
            // Rear eyes retain the window's original right/up texture axes.
            // This reflected view needs the opposite winding so the original
            // model's actual back surfaces remain visible through that same UV.
            GL.invertCulling = originalCullingInverted ^ view.Reversed;
            _camera.Render();
        }
        finally
        {
            GL.invertCulling = originalCullingInverted;
            if (_camera != null)
            {
                _camera.targetTexture = originalTarget;
                _camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                _camera.orthographic = originalOrtho;
                _camera.nearClipPlane = originalNear; _camera.farClipPlane = originalFar;
                if (generatedView) _camera.ResetWorldToCameraMatrix(); else _camera.worldToCameraMatrix = originalView;
                if (generatedProjection) _camera.ResetProjectionMatrix(); else _camera.projectionMatrix = originalProjection;
                if (generatedCulling) _camera.ResetCullingMatrix(); else _camera.cullingMatrix = originalCulling;
            }
            RenderTexture.active = originalActive;
            _rendering = false;
            if (_disposed) ReleaseTargets();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        AfterCapture();
        _disposed = true;
        _surface.Retire();
        if (--_calibration.References == 0) Calibrations.Remove(_manager);
        if (!_rendering) ReleaseTargets();
    }

    private void ReleaseTargets()
    {
        Release(_left); Release(_right);
        _left = _right = null;
        _poolBytes -= _pairBytes; _pairBytes = 0; _width = _height = _samplingEdge = 0; _lowerSince = -1f;
    }

    private static Func<Character3DDisplayManager, object?>? CreateOriginalReader()
    {
        FieldInfo? field = typeof(Character3DDisplayManager).GetField("character3D", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null || field.FieldType.IsValueType) return null;
        var method = new DynamicMethod("GloomhavenVR_MenuDepth_ReadOriginalCharacter", typeof(object),
            new[] { typeof(Character3DDisplayManager) }, typeof(MenuDepthCharacterSource), true);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
        return (Func<Character3DDisplayManager, object?>)method.CreateDelegate(typeof(Func<Character3DDisplayManager, object?>));
    }

    private sealed class CalibrationCache
    {
        internal int References;
        private readonly Camera _camera;
        private readonly Transform _holder;
        private readonly Dictionary<object, Vector3> _origins = new();
        private readonly Dictionary<object, Renderer[]> _samplingRenderers = new();
        internal CalibrationCache(Camera camera, Transform holder) { _camera = camera; _holder = holder; }
        internal bool TryGet(object original, out Vector3 result)
        {
            if (_origins.TryGetValue(original, out result)) return true;
            Transform? main = ReferenceEquals(original, _holder) ? _holder : null;
            if (main == null)
            {
                FieldInfo? models = original.GetType().GetField("models", BindingFlags.Instance | BindingFlags.NonPublic);
                if (models?.GetValue(original) is IEnumerable list)
                    foreach (object member in list)
                    {
                        Component? actor = member.GetType().GetProperty("CharacterManager")?.GetValue(member, null) as Component;
                        // Native CreateCharacter appends the main actor before
                        // companions. Its placement root is the authored ground
                        // axis, independent of equipment/animation bounds. Do
                        // not substitute a later companion for a missing main.
                        main = actor != null ? actor.transform : null;
                        break;
                    }
            }
            result = main != null ? _camera.transform.InverseTransformPoint(main.position) : default;
            if (main == null || !MenuDepthProjection.Finite(result)) return false;
            if (_origins.Count >= 32) _origins.Clear();
            _origins.Add(original, result); return true;
        }

        internal Renderer[] SamplingRenderers(object original)
        {
            if (_samplingRenderers.TryGetValue(original, out Renderer[] result)) return result;
            var renderers = new List<Renderer>();
            if (ReferenceEquals(original, _holder)) renderers.AddRange(_holder.GetComponentsInChildren<Renderer>(true));
            else
            {
                FieldInfo? models = original.GetType().GetField("models", BindingFlags.Instance | BindingFlags.NonPublic);
                if (models?.GetValue(original) is IEnumerable list)
                    foreach (object member in list)
                    {
                        Component? actor = member.GetType().GetProperty("CharacterManager")?.GetValue(member, null) as Component;
                        if (actor != null) renderers.AddRange(actor.GetComponentsInChildren<Renderer>(true));
                    }
            }
            result = renderers.ToArray();
            if (_samplingRenderers.Count >= 32) _samplingRenderers.Clear();
            _samplingRenderers.Add(original, result); return result;
        }
    }

    private static void Release(RenderTexture? target)
    {
        if (target == null) return;
        target.Release();
        Object.Destroy(target);
    }

    private static bool Same(Matrix4x4 first, Matrix4x4 second)
    {
        for (int i = 0; i < 16; i++)
            if (Mathf.Abs(first[i] - second[i]) > 1e-5f) return false;
        return true;
    }
}

/// <summary>Transport the native rendered surface depth without replacing its original UI owner.</summary>
internal sealed class MenuDepthCharacterSurface : BaseMeshEffect, IMaterialModifier
{
    private Material? _material, _original;
    private Shader? _sourceShader;
    private bool _retired, _nativeSoftMask;
    private MenuDepthCharacterProjection.View _view;
    private RenderTexture? _depth;
    private Rect _viewport, _uv, _captureClip;
    private bool _capturing;

    internal void Configure() { graphic.SetMaterialDirty(); }

    internal void Capture(MenuDepthCharacterProjection.View view, RenderTexture depth, Rect viewport, Rect uv, Rect captureClip)
    {
        _view = view; _depth = depth; _viewport = viewport; _uv = uv; _captureClip = captureClip; _capturing = true;
        Apply();
    }

    internal void EndCapture() { _capturing = false; Apply(); }

    public Material GetModifiedMaterial(Material original)
    {
        if (_retired) return original;
        Shader? shader = MenuDepthDisplay.CharacterShader;
        if (shader == null) return original;
        if (_material == null || _original != original)
        {
            if (_material != null) Object.Destroy(_material);
            _original = original;
            _material = new Material(shader) { name = "GloomhavenVR.MenuDepth.CharacterSurface", hideFlags = HideFlags.HideAndDontSave };
        }
        _material.CopyPropertiesFromMaterial(original);
        Shader sourceShader = original.shader;
        if (_sourceShader != sourceShader)
        {
            _sourceShader = sourceShader;
            _nativeSoftMask = sourceShader != null && sourceShader.name == "UI/Inverted soft alpha mask (compatible with canvas group)";
        }
        bool nativeSoftMask = _nativeSoftMask;
        _material.SetFloat("_GvrNativeSoftMask", nativeSoftMask ? 1f : 0f);
        _material.SetFloat("_GvrCharacterSrcAlphaBlend", nativeSoftMask ? 5f : 1f);
        // The original compiled character mask multiplies only vertex colour;
        // its material has no _Color uniform. Keep its actual alpha-mask texture
        // and independent _MainTex/_Mask transforms copied above.
        if (nativeSoftMask) _material.SetColor("_Color", Color.white);
        Apply();
        return _material;
    }

    private void Apply()
    {
        if (_material == null) return;
        _material.SetFloat("_GvrCharacterActive", _capturing ? 1f : 0f);
        _material.SetFloat("_GvrCharacterDepthWrite", _capturing ? 1f : 0f);
        if (!_capturing)
        {
            Rect uv = (graphic as RawImage)?.uvRect ?? new Rect(0f, 0f, 1f, 1f);
            _material.SetVector("_ImageUV", new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax));
            return;
        }
        _material.SetTexture("_NativeDepth", _depth!, UnityEngine.Rendering.RenderTextureSubElement.Depth);
        _material.SetMatrix("_NativeInverseVP", (GL.GetGPUProjectionMatrix(_view.Projection, true) * _view.WorldToCamera).inverse);
        _material.SetMatrix("_NativeToGraphic", _view.NativeToGraphic);
        _material.SetMatrix("_GraphicLocalToWorld", graphic.transform.localToWorldMatrix);
        _material.SetVector("_NativePlane", _view.NativePlane);
        _material.SetFloat("_FrontSide", _view.Reversed ? 1f : -1f);
        _material.SetVector("_NativeViewport", new Vector4(_viewport.x, _viewport.y, _viewport.width, _viewport.height));
        _material.SetVector("_ImageUV", new Vector4(_uv.xMin, _uv.yMin, _uv.xMax, _uv.yMax));
        float u0 = _viewport.x + (_captureClip.xMin + 1f) * .5f * _viewport.width;
        float u1 = _viewport.x + (_captureClip.xMax + 1f) * .5f * _viewport.width;
        float v0 = _viewport.y + (_captureClip.yMin + 1f) * .5f * _viewport.height;
        float v1 = _viewport.y + (_captureClip.yMax + 1f) * .5f * _viewport.height;
        Rect rect = graphic.rectTransform.rect;
        _material.SetVector("_CaptureOldUV", new Vector4(u0, v0, u1, v1));
        _material.SetVector("_CaptureRect", new Vector4(rect.xMin + (u0 - _uv.xMin) / _uv.width * rect.width,
            rect.yMin + (v0 - _uv.yMin) / _uv.height * rect.height,
            rect.xMin + (u1 - _uv.xMin) / _uv.width * rect.width,
            rect.yMin + (v1 - _uv.yMin) / _uv.height * rect.height));
        _material.SetVector("_CaptureUVScaleOffset", new Vector4(2f / _captureClip.width, 2f / _captureClip.height,
            _viewport.x * (1f - 2f / _captureClip.width) + _viewport.width * (.5f - (1f + _captureClip.center.x) / _captureClip.width),
            _viewport.y * (1f - 2f / _captureClip.height) + _viewport.height * (.5f - (1f + _captureClip.center.y) / _captureClip.height)));
    }

    public override void ModifyMesh(VertexHelper helper) { }

    internal void Retire()
    {
        _retired = true; _capturing = false;
        if (_material != null) Object.Destroy(_material);
        _material = _original = null;
        if (graphic != null) graphic.SetMaterialDirty();
        Object.Destroy(this);
    }
}
