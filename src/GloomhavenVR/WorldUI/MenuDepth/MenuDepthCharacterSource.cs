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
internal sealed class MenuDepthCharacterSource : IMenuDepthDirectSource, IMenuDepthBoundsSource, IMenuDepthHeadBoundsSource, IMenuDepthRaycastSource, IDisposable
{
    private const long MaxPoolBytes = 96L * 1024 * 1024;
    private static readonly int[] ResolutionTiers = { 256, 512, 768, 1024, 1536, 2048 };
    private static readonly Dictionary<Character3DDisplayManager, CalibrationCache> Calibrations = new();
    private static readonly Func<Character3DDisplayManager, object?>? ReadOriginal = CreateOriginalReader();
    private static long _poolBytes, _headPoolBytes;
    private static readonly Dictionary<MenuDepthCharacterSource, long> HeadScratchReservations = new();
    private readonly RawImage _image;
    private readonly Camera _camera;
    private readonly Character3DDisplayManager _manager;
    private readonly Transform _holder;
    private readonly Func<Character3DDisplayManager, bool> _readHidden;
    private readonly RenderTexture _nativeTarget;
    private readonly List<Renderer> _shadowRenderers = new(32);
    private readonly List<Renderer> _headBoundsRenderers = new(32);
    private readonly List<Light> _shadowLights = new(8);
    private readonly List<(Renderer Renderer, UnityEngine.Rendering.ShadowCastingMode Casting, bool Receive, bool ForceOff)> _rendererShadows = new(32);
    private readonly List<(Light Light, LightShadows Shadows)> _lightShadows = new(8);
    private readonly CalibrationCache _calibration;
    private readonly Dictionary<Renderer, RayMesh> _rayMeshes = new();
    private object? _rayIdentity, _headBoundsIdentity;
    private readonly Dictionary<Renderer, RayWitness> _rayWitnesses = new();
    private readonly List<RayWitness> _rayWitnessCandidates = new(2);
    private readonly HashSet<Renderer> _rayMembers = new(), _rayPending = new();
    private object? _rayRenderedIdentity, _rayWitnessIdentity;
    private int _rayRenderedFrame = -1;
    private bool _rayCollecting;
    private int _rayBakes, _rayTriangles;
    private static int _rayFailures;
    internal int RayBakes => _rayBakes;
    internal int RayTriangles => _rayTriangles;
    internal int RayVisibleMembers => _rayMembers.Count;
    internal int RayReceiptFrame => _rayRenderedFrame;
    private readonly MenuDepthCharacterSurface _surface;
    private readonly BoundsClipper _boundsClipper = new();
    private RenderTexture? _left, _right, _borrowed;
    private Texture? _restoreTexture;
    private bool _disposed;
    private bool _rendering;
    private int _width, _height;
    private long _pairBytes;
    private float _lastCapture, _lowerSince = -1f;
    private int _directBatchFrame = -1, _pairFrame = -1;
    private Rect _captureClip;
    private Vector2 _captureScale = Vector2.one;
    private int _samplingEdge, _demandEdge;
    private bool _headTargets;
    internal static Func<RawImage, object, float>? YawProvider { get; set; }
    internal object? ReadyIdentity
    {
        get
        {
            if (!BindingAlive || !_image.isActiveAndEnabled || !_image.gameObject.activeInHierarchy
                || !_camera.isActiveAndEnabled || !_manager.isActiveAndEnabled
                || !_holder.gameObject.activeInHierarchy || !_nativeTarget.IsCreated() || _readHidden(_manager)) return null;
            return ReadOriginal == null ? _holder : ReadOriginal(_manager);
        }
    }
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
    public Transform RayRoot => _image.transform;
    internal bool BindingAlive => !_disposed && _image != null && _surface != null && _camera != null && _manager != null
        && _holder != null && _nativeTarget != null && (_rendering || ReferenceEquals(_camera.targetTexture, _nativeTarget))
        && (ReferenceEquals(_image.texture, _nativeTarget) || ReferenceEquals(_image.texture, _borrowed));
    public bool IsReady => ReadyIdentity != null;

    public void PrimeDirectCapture(MenuDepthEye samplingEye)
    {
        AfterCapture();
        object original = ReadyIdentity ?? throw new InvalidOperationException("Original character preview cannot prime its UI batch.");
        if (_rendering) throw new InvalidOperationException("Original character preview cannot prime its UI batch.");
        if (!_calibration.TryGet(original, out Vector3 origin)
            || !MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform,
                _image.uvRect, samplingEye.WorldPosition, origin, out MenuDepthCharacterProjection.View view, allowCoplanar: samplingEye.HasWorldToHeadClip))
            throw new InvalidOperationException("Original character preview has no valid direct batch frame.");
        if (samplingEye.HasWorldToHeadClip && samplingEye.HeadCamera != null)
        {
            SelectHeadResolution(MenuDepthHeadRaster.Size(samplingEye.HeadCamera));
            _directBatchFrame = Time.frameCount;
            Borrow(EnsureTarget(false), _image.texture);
            return;
        }
        Rect clip = SamplingClip(original, view, samplingEye);
        if (samplingEye.HeadCamera != null && MenuDepthDisplay.TryGetEyePositions(samplingEye.HeadCamera, out Vector3 left, out Vector3 right))
        {
            if (MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform, _image.uvRect, left, origin, out var l)) clip = Union(clip, SamplingClip(original, l, new MenuDepthEye(false, left, samplingEye.HeadCamera)));
            if (MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform, _image.uvRect, right, origin, out var r)) clip = Union(clip, SamplingClip(original, r, new MenuDepthEye(true, right, samplingEye.HeadCamera)));
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
        object identity = ReadyIdentity ?? throw new InvalidOperationException("Original character preview is not ready for this capture.");
        if (_rendering) throw new InvalidOperationException("Original character preview is not ready for this capture.");
        if (!_calibration.TryGet(identity, out Vector3 origin))
            throw new InvalidOperationException("Original character has no completed main actor origin.");
        bool headRaster = (MenuDepthHeadRaster.Active || direct) && eye.HasWorldToHeadClip;
        if (!MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform,
            _image.uvRect, eye.WorldPosition, origin, out MenuDepthCharacterProjection.View view, allowCoplanar: headRaster))
            throw new InvalidOperationException("Original character preview has no valid window portal.");
        float yaw = YawProvider?.Invoke(_image, identity) ?? 0f;
        if (yaw != 0f)
        {
            Vector3 ground = _camera.transform.TransformPoint(origin);
            Vector3 up = _camera.cameraToWorldMatrix.MultiplyVector(Vector3.up).normalized;
            Matrix4x4 rotation = Matrix4x4.Translate(ground) * Matrix4x4.Rotate(Quaternion.AngleAxis(yaw, up)) * Matrix4x4.Translate(-ground);
            view = MenuDepthCharacterProjection.Rotated(view, rotation);
        }
        if (!headRaster && _headTargets) ReleaseTargets();
        if (headRaster)
        {
            view = MenuDepthCharacterProjection.Head(view, _image.rectTransform, eye);
            SelectHeadResolution(MenuDepthHeadRaster.Active ? MenuDepthHeadRaster.Pixels : MenuDepthHeadRaster.Size(eye.HeadCamera));
        }
        else view = ExpandCapture(identity, view, eye);
        // A native Canvas batch has already retained this frame's left texture
        // identity. Refresh that same surface for either direct eye; only an early
        // batch boundary may resize it. No third surface or late sampler swap.
        if (!headRaster && _directBatchFrame != Time.frameCount && _pairFrame != Time.frameCount)
        { SelectResolution(eye.PixelFootprint); _pairFrame = Time.frameCount; }
        RenderTexture target = EnsureTarget(!headRaster && !direct && eye.Right);
        _lastCapture = Time.unscaledTime;
        Texture original = _image.texture;
        RenderOriginal(target, view, identity, headRaster);
        // A native render callback may replace a pooled preview. Do not overwrite
        // that new original with an eye of the retired character.
        if (!BindingAlive || !ReferenceEquals(_image.texture, original))
            throw new InvalidOperationException("Original character preview changed during its render.");
        _surface.Capture(view, target, headRaster ? new Rect(0f, 0f, 1f, 1f) : _camera.rect, _image.uvRect,
            headRaster ? new Rect(-1f, -1f, 2f, 2f) : _captureClip, headRaster, EyeGraphic(eye.WorldPosition, headRaster));
        Borrow(target, original);
    }

    private Vector3 EyeGraphic(Vector3 position, bool headRaster)
    {
        Vector3 local = _image.transform.InverseTransformPoint(position);
        // Original ScreenSpaceCamera units may be hundreds/thousands of pixels.
        // Converting that same glass to world space can leave sub-millipixel Z
        // error at an exactly coplanar eye. Pick the authored plane once instead
        // of alternating the front/back aperture classification across frames.
        float epsilon = Mathf.Max(_image.rectTransform.rect.width, _image.rectTransform.rect.height) * 1e-6f;
        if (headRaster && Mathf.Abs(local.z) <= epsilon) local.z = 0f;
        return local;
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
        if (target != null && (_headTargets
            ? !MenuDepthHeadRaster.Matches(target, _nativeTarget.descriptor, new Vector2Int(_width, _height))
            : !target.IsCreated() || target.width != _width || target.height != _height
                || target.graphicsFormat != _nativeTarget.graphicsFormat || target.antiAliasing != _nativeTarget.antiAliasing))
        {
            Release(target);
            if (right) _right = null; else _left = null;
            target = null;
        }
        if (target == null)
        {
            RenderTextureDescriptor descriptor = _headTargets
                ? MenuDepthHeadRaster.Descriptor(_nativeTarget.descriptor, new Vector2Int(_width, _height)) : _nativeTarget.descriptor;
            descriptor.width = _width; descriptor.height = _height;
            descriptor.vrUsage = VRTextureUsage.None;
            descriptor.volumeDepth = 1;
            descriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
            descriptor.depthBufferBits = Math.Max(24, descriptor.depthBufferBits);
            target = new RenderTexture(descriptor)
            {
                name = "GloomhavenVR.MenuDepth.Character." + (right ? "Right" : "Left"),
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = _headTargets ? FilterMode.Point : _nativeTarget.filterMode,
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

    private void SelectHeadResolution(Vector2Int pixels)
    {
        int width = pixels.x, height = pixels.y;
        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            throw new InvalidOperationException("The actual head raster exceeds the graphics texture limit.");
        long bytes = MenuDepthHeadRaster.SingleBytes(_nativeTarget.descriptor, pixels);
        long held = _headTargets ? _pairBytes : 0;
        long largest = bytes;
        foreach (long reserved in HeadScratchReservations.Values) largest = Math.Max(largest, reserved);
        long budget = MenuDepthHeadRaster.Budget(largest, MenuDepthHeadRaster.SceneSlots, 96L * 1024 * 1024);
        if ((!_headTargets && HeadScratchReservations.Count >= MenuDepthHeadRaster.SceneSlots)
            || bytes > budget - _headPoolBytes + held)
            throw new InvalidOperationException("The actual head raster exceeds the native-scene scratch budget.");
        if (!_headTargets || width != _width || height != _height || bytes != _pairBytes
            || (_left != null && !MenuDepthHeadRaster.Matches(_left, _nativeTarget.descriptor, pixels)))
        {
            ReleaseTargets(); _headTargets = true; _width = width; _height = height;
            _pairBytes = bytes; _headPoolBytes += bytes; HeadScratchReservations[this] = bytes;
        }
        _samplingEdge = _demandEdge = Mathf.Max(width, height);
    }

    private void SelectResolution(Vector2 footprint)
    {
        float longest = Mathf.Max(_nativeTarget.width, _nativeTarget.height);
        float scale = footprint.x > 0f && footprint.y > 0f
            ? Mathf.Max(footprint.x / (Mathf.Abs(_image.uvRect.width) * _nativeTarget.width),
                footprint.y / (Mathf.Abs(_image.uvRect.height) * _nativeTarget.height)) : 1f;
        float wanted = Mathf.Clamp(longest * scale, ResolutionTiers[0], ResolutionTiers[ResolutionTiers.Length - 1]);
        int current = _demandEdge;
        bool shrink = false;
        if (current > 0 && wanted < current * .55f)
        {
            if (_lowerSince < 0f) _lowerSince = Time.unscaledTime;
            shrink = Time.unscaledTime - _lowerSince >= 1.5f;
            if (!shrink) wanted = current;
        }
        else
        {
            _lowerSince = -1f;
            if (current > 0 && wanted <= current * 1.12f) wanted = current;
        }
        int selected = ResolutionTiers[ResolutionTiers.Length - 1];
        for (int i = 0; i < ResolutionTiers.Length; i++)
            if (ResolutionTiers[i] >= wanted) { selected = ResolutionTiers[i]; break; }

        // Build729: crop coordinates are per-eye sampling uniforms, not resource
        // identities. The old16px crop-driven resizes destroyed both targets in
        // the middle of a stereo pair and let an offscreen near-plane AABB consume
        // the complete96MiB pool at even its minimum tier. Grow private capacity
        // in bounded power-of-two buckets; reuse it across recentering, clipped
        // crop changes and either eye. A genuinely sustained lower pixel demand
        // or idle maintenance can release capacity. Never destroy a usable pair
        // just because a larger crop cannot reserve another allocation.
        int limit = Mathf.Min(2048, SystemInfo.maxTextureSize);
        int width = Capacity(_nativeTarget.width / longest * selected * _captureScale.x, limit);
        int height = Capacity(_nativeTarget.height / longest * selected * _captureScale.y, limit);
        if (!shrink && _width > 0)
        { width = Math.Max(width, _width); height = Math.Max(height, _height); }
        long available = MaxPoolBytes - _poolBytes + _pairBytes;
        while (PairCost(width, height) > available && (width > 16 || height > 16))
        {
            if (width >= height && width > 16) width /= 2;
            else height /= 2;
        }
        if (PairCost(width, height) > available) throw new InvalidOperationException("Character eye pool is fully reserved.");
        if (_width > 0 && !shrink && (width < _width || height < _height))
        { width = _width; height = _height; }
        if (width != _width || height != _height)
        {
            ReleaseTargets();
            _width = width; _height = height; _pairBytes = PairCost(width, height); _poolBytes += _pairBytes;
        }
        _demandEdge = selected;
        // Report the admitted aperture density honestly when a very oblique or
        // near-clipped visible crop reaches the fixed allocation/sample budget.
        // Returning to a normal crop recovers full density on the same targets.
        _samplingEdge = Mathf.Min(selected, Mathf.FloorToInt(Mathf.Min(
            _width * longest / (_nativeTarget.width * _captureScale.x),
            _height * longest / (_nativeTarget.height * _captureScale.y))));
    }

    private static int Capacity(float required, int limit)
    {
        if (float.IsNaN(required) || required <= 16f) return 16;
        if (float.IsInfinity(required) || required >= limit) return limit;
        return Mathf.Min(limit, Mathf.NextPowerOfTwo(Mathf.CeilToInt(required)));
    }

    public bool TryGetForegroundBounds(MenuDepthEye eye, out Rect graphicLocalBounds)
    {
        graphicLocalBounds = default;
        if (!IsReady) return false;
        object identity = ReadOriginal?.Invoke(_manager) ?? _holder;
        if (!_calibration.TryGet(identity, out Vector3 origin)
            || !MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform,
                _image.uvRect, eye.WorldPosition, origin, out var view)
            || !ProjectedBounds(identity, view, eye, foregroundOnly: true, out Rect clip)) return false;
        graphicLocalBounds = GraphicRect(clip);
        return true;
    }

    public bool TryGetHeadBounds(MenuDepthEye eye, out Rect clipBounds)
    {
        clipBounds = default;
        object? identity = ReadyIdentity;
        if (identity == null || !_calibration.TryGet(identity, out Vector3 origin)
            || !MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform, _image.uvRect,
                eye.WorldPosition, origin, out var view, allowCoplanar: true)) return false;
        float angle = YawProvider?.Invoke(_image, identity) ?? 0f;
        if (angle != 0f)
        {
            Vector3 anchor = _camera.transform.TransformPoint(origin);
            Vector3 up = _camera.cameraToWorldMatrix.MultiplyVector(Vector3.up).normalized;
            view = MenuDepthCharacterProjection.Rotated(view, Matrix4x4.Translate(anchor)
                * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, up)) * Matrix4x4.Translate(-anchor));
        }
        Matrix4x4 clip = eye.WorldToHeadClip * _image.transform.localToWorldMatrix * view.NativeToGraphic;
        // Equipment may appear after the original actor completed. A cached
        // renderer census must not clip that new native foreground artwork.
        _holder.GetComponentsInChildren(true, _headBoundsRenderers);
        foreach (Transform actor in _calibration.ModelRoots(identity))
        {
            if (actor == null || actor.IsChildOf(_holder)) continue;
            actor.GetComponentsInChildren(true, _shadowRenderers);
            foreach (Renderer renderer in _shadowRenderers)
                if (!_headBoundsRenderers.Contains(renderer)) _headBoundsRenderers.Add(renderer);
        }
        _headBoundsIdentity = identity;
        bool any = false;
        foreach (Renderer renderer in _headBoundsRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                || (_camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
            Rect bounds = MenuDepthHeadRaster.ClipBounds(clip, renderer.bounds);
            clipBounds = any ? MenuDepthHeadRaster.Union(clipBounds, bounds) : bounds;
            any = true;
        }
        return any;
    }

    public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out MenuDepthRayHit hit)
    {
        hit = default;
        object? identity = ReadyIdentity;
        if (identity == null || _rendering || !ReferenceEquals(identity, _rayRenderedIdentity)
            || _rayRenderedFrame < Time.frameCount - 1 || !_calibration.TryGet(identity, out Vector3 nativeOrigin)
            || !MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform, _image.uvRect,
                origin, nativeOrigin, out var view, allowCoplanar: true)) return false;
        float yaw = YawProvider?.Invoke(_image, identity) ?? 0f;
        if (yaw != 0f)
        {
            Vector3 ground = _camera.transform.TransformPoint(nativeOrigin);
            Vector3 up = _camera.cameraToWorldMatrix.MultiplyVector(Vector3.up).normalized;
            view = MenuDepthCharacterProjection.Rotated(view, Matrix4x4.Translate(ground)
                * Matrix4x4.Rotate(Quaternion.AngleAxis(yaw, up)) * Matrix4x4.Translate(-ground));
        }
        if (!ReferenceEquals(_rayIdentity, identity))
        { ReleaseRayMeshes(); _rayIdentity = identity; }
        Matrix4x4 nativeToPhysical = _image.transform.localToWorldMatrix * view.NativeToGraphic;
        Matrix4x4 inverse = nativeToPhysical.inverse;
        Vector3 nativeRayOrigin = inverse.MultiplyPoint3x4(origin);
        Vector3 nativeDirection = inverse.MultiplyVector(direction);
        var ray = new Ray(nativeRayOrigin, nativeDirection.normalized);
        float nativePerWorld = nativeDirection.magnitude;
        if (!(nativePerWorld > 1e-8f)) return false;
        // Reuse the renderer census already maintained by the real head-bound
        // path. Before its first draw, the original identity's cached discovery
        // provides the same native model members. Never search the scene here.
        IReadOnlyList<Renderer> renderers = ReferenceEquals(_headBoundsIdentity, identity)
            ? _headBoundsRenderers : _calibration.SamplingRenderers(identity);
        bool any = false;
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff
                || !_rayMembers.Contains(renderer)
                || renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                || (_camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                || !renderer.bounds.IntersectRay(ray, out float entry) || entry >= maxDistance * nativePerWorld) continue;
            if (!_rayMeshes.TryGetValue(renderer, out RayMesh? mesh))
            { mesh = new RayMesh(); _rayMeshes.Add(renderer, mesh); }
            try
            {
                if (!mesh.TryHit(renderer, nativeRayOrigin, nativeDirection, maxDistance, out float distance,
                    ref _rayBakes, ref _rayTriangles)) continue;
                Vector3 physical = origin + direction * distance;
                Vector3 local = _image.transform.InverseTransformPoint(physical);
                Rect rect = _image.rectTransform.rect;
                // Foreground ink may escape the old RawImage aperture. Its
                // existing native rotation receiver still owns the input;
                // clamp only its logical event coordinate, never the beam.
                Vector3 input = _image.transform.TransformPoint(new Vector3(Mathf.Clamp(local.x, rect.xMin + .001f, rect.xMax - .001f),
                    Mathf.Clamp(local.y, rect.yMin + .001f, rect.yMax - .001f), 0f));
                float side = _image.transform.InverseTransformPoint(origin).z;
                bool foreground = Mathf.Abs(side) <= 1e-5f || local.z * side > 0f;
                hit = new MenuDepthRayHit(_image, physical, input, distance, preferGraphic: true,
                    unclippedForeground: foreground);
                maxDistance = distance; any = true;
            }
            catch (Exception error)
            {
                if (mesh.Refused) continue;
                mesh.Refused = true;
                int count = ++_rayFailures;
                if (count <= 3 || (count & (count - 1)) == 0)
                    GloomhavenVR.Core.VRLog.Warn("WorldUI", "MENU DEPTH RAY MESH unavailable for native renderer '"
                        + renderer.name + "': " + error.GetType().Name + ": " + error.Message + "; occurrence=" + count + ".");
            }
        }
        return any;
    }

    private void ReleaseRayMeshes()
    {
        foreach (RayMesh mesh in _rayMeshes.Values) mesh.Dispose();
        _rayMeshes.Clear(); _rayIdentity = null;
    }

    /// <summary>No collider/BVH rebuild. A potentially hit live skin is baked once per input frame.</summary>
    private sealed class RayMesh : IDisposable
    {
        private Mesh? _source, _baked;
        private int _frame = -1, _vertexCount, _submeshCount;
        private uint _indexCount;
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new(), _submesh = new();
        internal bool Refused;

        internal bool TryHit(Renderer renderer, Vector3 origin, Vector3 direction, float maxDistance,
            out float distance, ref int bakes, ref int triangles)
        {
            distance = 0f;
            Mesh? source = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (source == null || source.vertexCount == 0) return false;
            uint indexCount = 0;
            for (int s = 0; s < source.subMeshCount; s++) indexCount += source.GetIndexCount(s);
            bool changed = source != _source || source.vertexCount != _vertexCount
                || source.subMeshCount != _submeshCount || indexCount != _indexCount;
            if (changed)
            { _source = source; _vertexCount = source.vertexCount; _submeshCount = source.subMeshCount;
                _indexCount = indexCount; _frame = -1; _triangles.Clear(); Refused = false; }
            if (Refused) return false;
            Mesh readable = source;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                _baked ??= new Mesh { name = "GloomhavenVR.MenuDepthRaySkin", hideFlags = HideFlags.HideAndDontSave };
                if (_frame != Time.frameCount)
                {
                    skinned.BakeMesh(_baked, false); bakes++;
                    _baked.GetVertices(_vertices); _frame = Time.frameCount;
                }
                readable = _baked;
            }
            else if (changed) source.GetVertices(_vertices);
            if (_triangles.Count == 0)
                for (int submesh = 0; submesh < readable.subMeshCount; submesh++)
                {
                    if (readable.GetTopology(submesh) != MeshTopology.Triangles) continue;
                    readable.GetTriangles(_submesh, submesh); _triangles.AddRange(_submesh);
                }
            Matrix4x4 inverse = renderer.transform.worldToLocalMatrix;
            Vector3 localOrigin = inverse.MultiplyPoint3x4(origin), localDirection = inverse.MultiplyVector(direction);
            bool any = false;
            for (int i = 0; i + 2 < _triangles.Count; i += 3)
            {
                triangles++;
                if (!MenuDepthRayGeometry.Triangle(localOrigin, localDirection, _vertices[_triangles[i]],
                    _vertices[_triangles[i + 1]], _vertices[_triangles[i + 2]], maxDistance,
                    out float next, out _)) continue;
                distance = next; maxDistance = next; any = true;
            }
            return any;
        }

        public void Dispose()
        {
            if (_baked != null) Object.Destroy(_baked);
            _baked = null; _source = null; _vertices.Clear(); _triangles.Clear(); _submesh.Clear();
        }
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

    private MenuDepthCharacterProjection.View ExpandCapture(object identity, MenuDepthCharacterProjection.View view, MenuDepthEye eye)
    {
        SetCaptureClip(identity, SamplingClip(identity, view, eye));
        return view.Expanded(_captureClip);
    }

    private Rect SamplingClip(object identity, MenuDepthCharacterProjection.View view, MenuDepthEye eye)
        => ProjectedBounds(identity, view, eye, foregroundOnly: false, out Rect clip) ? clip : ImageClip();

    private void SetCaptureClip(object identity, Rect desired)
    {
        // Sampling uniforms follow the admitted visible region immediately.
        // Allocation hysteresis belongs to capacity, not this crop: retaining a
        // formerly huge near-plane crop made normal views stay blurred for1.5s.
        _captureClip = desired;
        _captureScale = desired.size * .5f;
    }

    private bool ProjectedBounds(object identity, MenuDepthCharacterProjection.View view, MenuDepthEye eye, bool foregroundOnly, out Rect clip)
    {
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        bool any = false;
        Matrix4x4 projection = view.Projection * view.WorldToCamera;
        _boundsClipper.Configure(view, eye, _image.transform.localToWorldMatrix, foregroundOnly);
        Renderer[] renderers = _calibration.SamplingRenderers(identity);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                || (_camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
            _boundsClipper.Encapsulate(renderer.bounds, projection, ref any, ref x0, ref x1, ref y0, ref y1);
        }
        if (!any) { clip = default; return false; }
        Rect imageClip = ImageClip(); float qx = imageClip.width * .125f, qy = imageClip.height * .125f;
        Vector2 center = new((x0 + x1) * .5f, (y0 + y1) * .5f);
        Vector2 size = new(Mathf.Ceil((x1 - x0) / qx) * qx + qx * 2f,
            Mathf.Ceil((y1 - y0) / qy) * qy + qy * 2f);
        clip = new Rect(center - size * .5f, size);
        return true;
    }

    // Build729: projecting unsliced AABB corners through w admitted points
    // outside the real eye's viewport, and corners behind its near plane. A
    // moving eye could request an enormous crop, exhaust even the minimum eye
    // tier, and make the complete window fall back to mono. Clip the conservative
    // closed volume first, including the new cap faces; merely skipping negative
    // w corners or clipping the six old faces loses near/frustum-contained cases.
    // Scratch storage is private, fixed-size and reused for every known renderer.
    private sealed class BoundsClipper
    {
        // Six box faces plus at most eight clipping caps give at most14
        // faces/24 distinct vertices. Raw cap hits occur twice at shared edges;
        // keep64 slots, including these duplicates and numerical near-coplanar
        // edges, rather than assuming exact vertex deduplication at world origin.
        private const int MaxFaces = 16, MaxVertices = 64;
        private Vector3[] _a = new Vector3[MaxFaces * MaxVertices], _b = new Vector3[MaxFaces * MaxVertices];
        private int[] _countsA = new int[MaxFaces], _countsB = new int[MaxFaces];
        private readonly Vector3[] _cap = new Vector3[MaxVertices];
        private readonly Vector4[] _planes = new Vector4[8];
        private int _planeCount;
        private static readonly int[] Corners = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 4, 6, 2, 1, 3, 7, 5, 0, 1, 5, 4, 2, 6, 7, 3 };

        internal void Configure(MenuDepthCharacterProjection.View view, MenuDepthEye eye, Matrix4x4 graphicToWorld, bool foreground)
        {
            _planeCount = 0;
            Vector4 near = -view.WorldToCamera.GetRow(2); near.w -= view.Near;
            AddPlane(near);
            if (foreground) AddPlane(view.NativePlane * (view.Reversed ? 1f : -1f));
            Matrix4x4 head;
            bool visible = eye.HasWorldToHeadClip;
            if (visible) head = eye.WorldToHeadClip;
            else if (!eye.ProjectToGraphicPlane && eye.HeadCamera != null)
            {
                Camera camera = eye.HeadCamera;
                Camera.StereoscopicEye side = eye.Right ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;
                Matrix4x4 headView = camera.stereoEnabled ? camera.GetStereoViewMatrix(side)
                    : Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye.WorldPosition, camera.transform.rotation, Vector3.one).inverse;
                head = (camera.stereoEnabled ? camera.GetStereoProjectionMatrix(side) : camera.projectionMatrix) * headView;
                visible = true;
            }
            else head = Matrix4x4.identity; // No physical viewport was supplied for a native-screen eye.
            if (!visible) return;
            Matrix4x4 nativeToHead = head * graphicToWorld * view.NativeToGraphic;
            Vector4 w = nativeToHead.GetRow(3);
            for (int axis = 0; axis < 3; axis++)
            { AddPlane(w + nativeToHead.GetRow(axis)); AddPlane(w - nativeToHead.GetRow(axis)); }
        }

        private void AddPlane(Vector4 plane)
        {
            float length = new Vector3(plane.x, plane.y, plane.z).magnitude;
            if (length > 1e-8f) _planes[_planeCount++] = plane / length;
        }

        internal void Encapsulate(Bounds bounds, Matrix4x4 projection, ref bool any, ref float x0, ref float x1, ref float y0, ref float y1)
        {
            Vector3 lo = bounds.min, hi = bounds.max, boundsCenter = bounds.center, extents = bounds.extents;
            float magnitude = Mathf.Max(1f, Mathf.Max(MaxAbs(lo), MaxAbs(hi)));
            float epsilon = Mathf.Max(1e-6f, magnitude * 2.384185791e-7f), epsilonSquared = epsilon * epsilon;
            int cuttingPlanes = 0;
            for (int plane = 0; plane < _planeCount; plane++)
            {
                Vector4 p = _planes[plane];
                float distance = Side(p, boundsCenter);
                float radius = Mathf.Abs(p.x) * extents.x + Mathf.Abs(p.y) * extents.y + Mathf.Abs(p.z) * extents.z;
                if (distance + radius < -epsilon) return;
                if (distance - radius < epsilon) cuttingPlanes |= 1 << plane;
            }
            // Most native body/equipment renderers are entirely in the real eye
            // frustum. Preserve their original eight-corner cost. Wholly outside
            // bounds stop before touching scratch; only intersecting planes run
            // polygon clipping, without per-call arrays or hierarchy searches.
            if (cuttingPlanes == 0)
            {
                for (int corner = 0; corner < 8; corner++)
                    Include(new Vector3((corner & 1) == 0 ? lo.x : hi.x,
                        (corner & 2) == 0 ? lo.y : hi.y, (corner & 4) == 0 ? lo.z : hi.z), projection,
                        ref any, ref x0, ref x1, ref y0, ref y1);
                return;
            }
            int faces = 6;
            for (int face = 0; face < faces; face++)
            {
                _countsA[face] = 4;
                for (int vertex = 0; vertex < 4; vertex++)
                {
                    int corner = Corners[face * 4 + vertex];
                    _a[face * MaxVertices + vertex] = new Vector3((corner & 1) == 0 ? lo.x : hi.x,
                        (corner & 2) == 0 ? lo.y : hi.y, (corner & 4) == 0 ? lo.z : hi.z);
                }
            }
            for (int plane = 0; plane < _planeCount && faces > 0; plane++)
            {
                if ((cuttingPlanes & (1 << plane)) == 0) continue;
                Vector4 p = _planes[plane]; int nextFaces = 0, capCount = 0;
                for (int face = 0; face < faces; face++)
                {
                    int count = _countsA[face], written = 0;
                    Vector3 previous = _a[face * MaxVertices + count - 1]; float previousSide = Side(p, previous);
                    for (int vertex = 0; vertex < count; vertex++)
                    {
                        Vector3 current = _a[face * MaxVertices + vertex]; float currentSide = Side(p, current);
                        if ((previousSide >= 0f) != (currentSide >= 0f))
                        {
                            Vector3 intersection = previous + (current - previous) * (previousSide / (previousSide - currentSide));
                            _b[nextFaces * MaxVertices + written++] = intersection;
                            bool duplicate = false;
                            for (int i = 0; i < capCount; i++) duplicate |= (_cap[i] - intersection).sqrMagnitude < epsilonSquared;
                            if (!duplicate) _cap[capCount++] = intersection;
                        }
                        if (currentSide >= 0f) _b[nextFaces * MaxVertices + written++] = current;
                        previous = current; previousSide = currentSide;
                    }
                    if (written >= 3) _countsB[nextFaces++] = written;
                }
                if (capCount >= 3)
                {
                    Vector3 center = Vector3.zero; for (int i = 0; i < capCount; i++) center += _cap[i]; center /= capCount;
                    Vector3 normal = new(p.x, p.y, p.z), axis = (_cap[0] - center).normalized, second = Vector3.Cross(normal, axis);
                    // At most twelve cut edges for this clipped box. Insertion
                    // sort avoids a comparer/delegate allocation in each eye.
                    for (int i = 1; i < capCount; i++)
                    {
                        Vector3 item = _cap[i]; float angle = Angle(item - center, axis, second); int j = i - 1;
                        while (j >= 0 && Angle(_cap[j] - center, axis, second) > angle) { _cap[j + 1] = _cap[j]; j--; }
                        _cap[j + 1] = item;
                    }
                    Array.Copy(_cap, 0, _b, nextFaces * MaxVertices, capCount); _countsB[nextFaces++] = capCount;
                }
                Vector3[] points = _a; _a = _b; _b = points; int[] counts = _countsA; _countsA = _countsB; _countsB = counts;
                faces = nextFaces;
            }
            for (int face = 0; face < faces; face++) for (int vertex = 0; vertex < _countsA[face]; vertex++)
                Include(_a[face * MaxVertices + vertex], projection, ref any, ref x0, ref x1, ref y0, ref y1);
        }
        private static void Include(Vector3 point, Matrix4x4 projection, ref bool any, ref float x0, ref float x1, ref float y0, ref float y1)
        {
            Vector4 projected = projection * new Vector4(point.x, point.y, point.z, 1f);
            if (projected.w <= 1e-8f) return;
            float x = projected.x / projected.w, y = projected.y / projected.w;
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return;
            any = true; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        private static float MaxAbs(Vector3 point) => Mathf.Max(Mathf.Abs(point.x), Mathf.Max(Mathf.Abs(point.y), Mathf.Abs(point.z)));
        private static float Side(Vector4 plane, Vector3 point) => plane.x * point.x + plane.y * point.y + plane.z * point.z + plane.w;
        private static float Angle(Vector3 point, Vector3 axis, Vector3 second) => Mathf.Atan2(Vector3.Dot(point, second), Vector3.Dot(point, axis));
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

    private void SuppressPreviewShadows(object original)
    {
        // Build737: the maintainer explicitly permits shadowless window figures
        // after all native heroes showed unstable, view-dependent shadow patches.
        // Keep the original lit materials, colours and light intensities. Suppress
        // only these preview actors and their owned lights during the borrowed
        // Camera.Render; never change scene/global quality or gameplay actors.
        // Fresh local membership includes pooled/replaced renderer births, unlike
        // the bounds cache. A native actor can also live outside the camera holder.
        SuppressPreviewShadows(_holder);
        foreach (Transform root in _calibration.ModelRoots(original))
            if (root != null && !root.IsChildOf(_holder)) SuppressPreviewShadows(root);
    }

    private void SuppressPreviewShadows(Transform root)
    {
        root.GetComponentsInChildren(true, _shadowRenderers);
        for (int i = 0; i < _shadowRenderers.Count; i++)
        {
            Renderer renderer = _shadowRenderers[i];
            if (renderer != null && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                && !_rayWitnesses.ContainsKey(renderer))
            {
                _rayWitnessCandidates.Clear(); renderer.GetComponents(_rayWitnessCandidates);
                RayWitness? witness = null;
                for (int w = 0; w < _rayWitnessCandidates.Count; w++)
                    if (_rayWitnessCandidates[w] != null && !_rayWitnessCandidates[w].Retired)
                    { witness = _rayWitnessCandidates[w]; break; }
                witness ??= renderer.gameObject.AddComponent<RayWitness>();
                witness.Bind(this, renderer); _rayWitnesses.Add(renderer, witness);
            }
            if (renderer == null || (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off
                && !renderer.receiveShadows)) continue;
            _rendererShadows.Add((renderer, renderer.shadowCastingMode, renderer.receiveShadows, renderer.forceRenderingOff));
            // Off normally draws colour; an authored shadow-only proxy must not
            // become a newly visible mesh when its shadow pass is suppressed.
            if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                renderer.forceRenderingOff = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        root.GetComponentsInChildren(true, _shadowLights);
        for (int i = 0; i < _shadowLights.Count; i++)
        {
            Light light = _shadowLights[i];
            if (light == null || light.shadows == LightShadows.None) continue;
            // The authored GUI3D Forward camera has its own shadowed directional
            // and point lights. Disabling their maps also covers custom native
            // shaders without replacing or editing any original material.
            _lightShadows.Add((light, light.shadows));
            light.shadows = LightShadows.None;
        }
    }

    private void RestorePreviewShadows()
    {
        for (int i = _lightShadows.Count - 1; i >= 0; i--)
        {
            var state = _lightShadows[i];
            if (state.Light != null) state.Light.shadows = state.Shadows;
        }
        for (int i = _rendererShadows.Count - 1; i >= 0; i--)
        {
            var state = _rendererShadows[i];
            if (state.Renderer == null) continue;
            state.Renderer.shadowCastingMode = state.Casting;
            state.Renderer.receiveShadows = state.Receive;
            state.Renderer.forceRenderingOff = state.ForceOff;
        }
        _lightShadows.Clear(); _rendererShadows.Clear();
        _shadowLights.Clear(); _shadowRenderers.Clear();
    }

    private void RenderOriginal(RenderTexture target, MenuDepthCharacterProjection.View view, object original, bool headRaster = false)
    {
        // Keep source-owned image effects and their native configuration. Copying
        // a Camera component alone loses Beautify and other attached callbacks.
        RenderTexture originalTarget = _camera.targetTexture;
        Rect originalViewport = _camera.rect;
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
            if (!ReferenceEquals(_rayWitnessIdentity, original))
            { ReleaseRayWitnesses(); _rayWitnessIdentity = original; }
            SuppressPreviewShadows(original);
            RenderTexture.active = target;
            GL.Clear(true, true, _camera.backgroundColor);
            _camera.targetTexture = target;
            if (headRaster) _camera.rect = new Rect(0f, 0f, 1f, 1f);
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
            GL.invertCulling = originalCullingInverted ^ view.Reflected;
            _rayPending.Clear(); _rayCollecting = true; _rayRenderedFrame = -1;
            _camera.Render();
            _rayMembers.Clear();
            foreach (Renderer renderer in _rayPending) _rayMembers.Add(renderer);
            _rayRenderedIdentity = original; _rayRenderedFrame = Time.frameCount;
        }
        finally
        {
            _rayCollecting = false; _rayPending.Clear();
            RestorePreviewShadows();
            GL.invertCulling = originalCullingInverted;
            if (_camera != null)
            {
                _camera.targetTexture = originalTarget;
                _camera.rect = originalViewport;
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
        _disposed = true;
        try { AfterCapture(); }
        finally
        {
            try { _surface.Retire(); }
            finally
            {
                ReleaseRayMeshes();
                ReleaseRayWitnesses();
                if (--_calibration.References == 0) Calibrations.Remove(_manager);
                if (!_rendering) ReleaseTargets();
                if (_image != null) _image.SetMaterialDirty();
            }
        }
    }

    private void ReleaseTargets()
    {
        Release(_left); Release(_right);
        _left = _right = null;
        if (_headTargets) { _headPoolBytes -= _pairBytes; HeadScratchReservations.Remove(this); }
        else _poolBytes -= _pairBytes;
        _headTargets = false; _pairBytes = 0; _width = _height = _samplingEdge = _demandEdge = 0; _lowerSince = -1f; _pairFrame = -1;
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

    private void ReleaseRayWitnesses()
    {
        foreach (RayWitness witness in _rayWitnesses.Values)
            if (witness != null) witness.Unbind(this);
        _rayWitnesses.Clear(); _rayMembers.Clear(); _rayPending.Clear();
        _rayRenderedIdentity = _rayWitnessIdentity = null; _rayRenderedFrame = -1;
    }

    /// <summary>Receipt of this source's actual culling, including native Unity LOD selection.</summary>
    private sealed class RayWitness : MonoBehaviour
    {
        private readonly List<(MenuDepthCharacterSource Source, Renderer Renderer)> _owners = new(1);
        internal bool Retired { get; private set; }
        internal void Bind(MenuDepthCharacterSource source, Renderer renderer)
        {
            for (int i = 0; i < _owners.Count; i++)
                if (ReferenceEquals(_owners[i].Source, source)) return;
            _owners.Add((source, renderer));
        }
        internal void Unbind(MenuDepthCharacterSource source)
        {
            for (int i = _owners.Count-1; i >= 0; i--)
                if (ReferenceEquals(_owners[i].Source, source)) _owners.RemoveAt(i);
            if (_owners.Count == 0) { Retired = true; Object.Destroy(this); }
        }
        private void OnWillRenderObject()
        {
            Camera current = Camera.current;
            for (int i = 0; i < _owners.Count; i++)
            {
                var owner = _owners[i];
                if (owner.Source._rayCollecting && owner.Source._camera == current)
                    owner.Source._rayPending.Add(owner.Renderer);
            }
        }
    }

    private sealed class CalibrationCache
    {
        internal int References;
        private readonly Camera _camera;
        private readonly Transform _holder;
        private readonly Dictionary<object, Vector3> _origins = new();
        private readonly Dictionary<object, Renderer[]> _samplingRenderers = new();
        private readonly Dictionary<object, Transform[]> _modelRoots = new();
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

        internal Transform[] ModelRoots(object original)
        {
            if (_modelRoots.TryGetValue(original, out Transform[] result)) return result;
            var roots = new List<Transform>();
            if (ReferenceEquals(original, _holder)) roots.Add(_holder);
            else
            {
                FieldInfo? models = original.GetType().GetField("models", BindingFlags.Instance | BindingFlags.NonPublic);
                if (models?.GetValue(original) is IEnumerable list)
                    foreach (object member in list)
                    {
                        Component? actor = member.GetType().GetProperty("CharacterManager")?.GetValue(member, null) as Component;
                        if (actor != null) roots.Add(actor.transform);
                    }
            }
            result = roots.ToArray();
            if (_modelRoots.Count >= 32) _modelRoots.Clear();
            _modelRoots.Add(original, result); return result;
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
    private bool _capturing, _headRaster;
    private Vector3 _eyeGraphic;

    internal void Configure() { graphic.SetMaterialDirty(); }

    internal void Capture(MenuDepthCharacterProjection.View view, RenderTexture depth, Rect viewport, Rect uv, Rect captureClip, bool headRaster = false, Vector3 eyeGraphic = default)
    {
        _headRaster = headRaster; _eyeGraphic = eyeGraphic;
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
        // A pooled native RawImage can destroy this effect before its managed
        // source is retired. Never enter BaseMeshEffect's native getter then.
        if (_material == null || this == null) return;
        Graphic current = graphic;
        if (current == null) return;
        _material.SetFloat("_GvrCharacterActive", _capturing ? 1f : 0f);
        _material.SetFloat("_GvrCharacterHeadRaster", _capturing && _headRaster ? 1f : 0f);
        _material.SetVector("_GvrCharacterEyeGraphic", _eyeGraphic);
        Rect originalRect = current.rectTransform.rect;
        _material.SetVector("_GvrCharacterGraphicRect", new Vector4(originalRect.xMin, originalRect.yMin, originalRect.width, originalRect.height));
        _material.SetFloat("_GvrCharacterDepthWrite", _capturing ? 1f : 0f);
        if (!_capturing)
        {
            Rect uv = (current as RawImage)?.uvRect ?? new Rect(0f, 0f, 1f, 1f);
            _material.SetVector("_ImageUV", new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax));
            return;
        }
        _material.SetTexture("_NativeDepth", _depth!, UnityEngine.Rendering.RenderTextureSubElement.Depth);
        Matrix4x4 gpu = GL.GetGPUProjectionMatrix(_view.Projection, true);
        if (SystemInfo.graphicsUVStartsAtTop) gpu.SetRow(1, -gpu.GetRow(1));
        _material.SetMatrix("_NativeInverseVP", (gpu * _view.WorldToCamera).inverse);
        _material.SetMatrix("_NativeToGraphic", _view.NativeToGraphic);
        _material.SetMatrix("_GraphicLocalToWorld", current.transform.localToWorldMatrix);
        Vector4 nativePlane = _view.NativePlane;
        // Reconstructing a24-bit native scene depth must not classify its exact
        // glass plane as foreground because of sub-millimetre float roundoff.
        // The unchanged common metric makes this1e-4-native-unit tolerance far
        // below a final head pixel; actual protruding geometry stays unrestricted.
        if (_headRaster) nativePlane.w += (_view.Reversed ? -1f : 1f) * .0001f;
        _material.SetVector("_NativePlane", nativePlane);
        _material.SetFloat("_FrontSide", _view.Reversed ? 1f : -1f);
        _material.SetVector("_NativeViewport", new Vector4(_viewport.x, _viewport.y, _viewport.width, _viewport.height));
        _material.SetVector("_ImageUV", new Vector4(_uv.xMin, _uv.yMin, _uv.xMax, _uv.yMax));
        float u0 = _viewport.x + (_captureClip.xMin + 1f) * .5f * _viewport.width;
        float u1 = _viewport.x + (_captureClip.xMax + 1f) * .5f * _viewport.width;
        float v0 = _viewport.y + (_captureClip.yMin + 1f) * .5f * _viewport.height;
        float v1 = _viewport.y + (_captureClip.yMax + 1f) * .5f * _viewport.height;
        Rect rect = current.rectTransform.rect;
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
        if (this == null) return;
        if (graphic != null) graphic.SetMaterialDirty();
        Object.Destroy(this);
    }
}
