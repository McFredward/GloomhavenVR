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
internal sealed class MenuDepthCharacterSource : IMenuDepthSampledSource, IDisposable
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
    private RenderTexture? _left, _right, _borrowed;
    private Texture? _restoreTexture;
    private bool _disposed;
    private bool _rendering;
    private int _width, _height;
    private long _pairBytes;
    private float _lastCapture, _lowerSince = -1f;

    internal MenuDepthCharacterSource(RawImage image, Camera camera,
        Character3DDisplayManager manager, Transform holder, Func<Character3DDisplayManager, bool> readHidden)
    {
        _image = image; _camera = camera; _manager = manager; _holder = holder; _readHidden = readHidden;
        _nativeTarget = camera.targetTexture;
        if (!Calibrations.TryGetValue(manager, out CalibrationCache cache))
            Calibrations.Add(manager, cache = new CalibrationCache(camera, holder));
        _calibration = cache; cache.References++;
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

    public void BeforeCapture(MenuDepthEye eye)
    {
        AfterCapture();
        if (!IsReady || _rendering) throw new InvalidOperationException("Original character preview is not ready for this capture.");
        if (!_calibration.TryGet(ReadOriginal?.Invoke(_manager) ?? _holder, out Bounds modelBounds))
            throw new InvalidOperationException("Original character has no authored mesh envelope.");
        if (!MenuDepthCharacterProjection.TryBuild(_camera, _holder, _image.rectTransform,
            _image.uvRect, eye.WorldPosition, modelBounds, out MenuDepthCharacterProjection.View view))
            throw new InvalidOperationException("Original character preview has no valid front-facing portal.");
        SelectResolution(eye.PixelFootprint);
        RenderTexture target = EnsureTarget(eye.Right);
        _lastCapture = Time.unscaledTime;
        Texture original = _image.texture;
        RenderOriginal(target, view);
        // A native render callback may replace a pooled preview. Do not overwrite
        // that new original with an eye of the retired character.
        if (!BindingAlive || !ReferenceEquals(_image.texture, original))
            throw new InvalidOperationException("Original character preview changed during its render.");
        _restoreTexture = original;
        _borrowed = target;
        _image.texture = target;
        _image.canvasRenderer.SetTexture(target);
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
        int current = Mathf.Max(_width, _height);
        // Hold a lower demand for1.5s before shrinking. Head jitter at a tier
        // boundary cannot repeatedly allocate/release native GPU resources.
        if (current > 0 && wanted < current * .55f)
        {
            if (_lowerSince < 0f) _lowerSince = Time.unscaledTime;
            if (Time.unscaledTime - _lowerSince < 1.5f) return;
        }
        else
        {
            _lowerSince = -1f;
            if (current > 0 && wanted <= current * 1.12f) return;
        }
        long available = MaxPoolBytes - _poolBytes + _pairBytes;
        int selected = 0, width = 0, height = 0; long bytes = 0;
        for (int i = 0; i < ResolutionTiers.Length; i++)
        {
            int edge = ResolutionTiers[i];
            int w = Mathf.Max(16, Mathf.RoundToInt(_nativeTarget.width * edge / longest));
            int h = Mathf.Max(16, Mathf.RoundToInt(_nativeTarget.height * edge / longest));
            long cost = PairCost(w, h);
            if (cost > available) break;
            selected = edge; width = w; height = h; bytes = cost;
            if (edge >= wanted) break;
        }
        if (selected == 0) throw new InvalidOperationException("Character eye pool is fully reserved.");
        if (width == _width && height == _height) return;
        ReleaseTargets();
        _width = width; _height = height; _pairBytes = bytes; _poolBytes += bytes;
        _lowerSince = -1f;
    }

    private long PairCost(int width, int height)
    {
        long color = Math.Max(4L, (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(_nativeTarget.graphicsFormat));
        long depth = _nativeTarget.depth > 0 ? 4L : 0L;
        long bytes = width * (long)height * (color + depth) * Math.Max(1, _nativeTarget.antiAliasing) * 2;
        return _nativeTarget.useMipMap ? bytes * 4 / 3 : bytes;
    }

    // Called by the bounded known-consumer maintenance loop, never a scene scan.
    internal void MaintainResources(float now)
    {
        if (!_rendering && _borrowed == null && _pairBytes > 0 && now - _lastCapture > 2f) ReleaseTargets();
    }

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
            _camera.Render();
        }
        finally
        {
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
        if (--_calibration.References == 0) Calibrations.Remove(_manager);
        if (!_rendering) ReleaseTargets();
    }

    private void ReleaseTargets()
    {
        Release(_left); Release(_right);
        _left = _right = null;
        _poolBytes -= _pairBytes; _pairBytes = 0; _width = _height = 0;
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
        private readonly Dictionary<object, Bounds> _bounds = new();
        private readonly HashSet<object> _unresolvedRigs = new();
        internal CalibrationCache(Camera camera, Transform holder) { _camera = camera; _holder = holder; }
        internal bool TryGet(object original, out Bounds result)
        {
            if (_bounds.TryGetValue(original, out result)) return true;
            if (_unresolvedRigs.Contains(original)) { result = default; return false; }
            bool any = false; result = default;
            if (ReferenceEquals(original, _holder))
            { if (!Collect(_holder, ref any, ref result)) { RejectRig(original); return false; } } // explicit fixture/unknown-runtime fallback
            else
            {
                FieldInfo? models = original.GetType().GetField("models", BindingFlags.Instance | BindingFlags.NonPublic);
                if (models?.GetValue(original) is IEnumerable list)
                    foreach (object member in list)
                    {
                        Component? actor = member.GetType().GetProperty("CharacterManager")?.GetValue(member, null) as Component;
                        if (actor != null && !Collect(actor.transform, ref any, ref result)) { RejectRig(original); return false; }
                    }
            }
            if (!any || result.size.x <= 1e-4f || result.size.y <= 1e-4f) return false;
            if (_bounds.Count + _unresolvedRigs.Count >= 32) { _bounds.Clear(); _unresolvedRigs.Clear(); }
            _bounds.Add(original, result); return true;
        }
        private void RejectRig(object original)
        {
            if (_bounds.Count + _unresolvedRigs.Count >= 32) { _bounds.Clear(); _unresolvedRigs.Clear(); }
            _unresolvedRigs.Add(original);
        }
        private bool Collect(Transform root, ref bool any, ref Bounds result)
        {
            // Authored local envelopes remain stable across animation. World
            // Renderer.bounds and per-frame hierarchy walks are deliberately absent.
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            var bones = new Dictionary<Transform, Matrix4x4>();
            if (!BuildBones(renderers, root, bones)) return false;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds local;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) local = skinned.localBounds;
                else if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() is MeshFilter filter && filter.sharedMesh != null) local = filter.sharedMesh.bounds;
                else continue;
                Matrix4x4 transform = AuthoredTransform(renderer.transform, root, bones);
                Vector3 lo = local.min, hi = local.max;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 point = transform.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? lo.x : hi.x,
                        (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
                    if (!any) { result = new Bounds(point, Vector3.zero); any = true; } else result.Encapsulate(point);
                }
            }
            return true;
        }

        private readonly struct SkinBinding
        {
            internal readonly SkinnedMeshRenderer Skin;
            internal readonly Transform[] Bones;
            internal readonly Matrix4x4[] Poses;
            internal SkinBinding(SkinnedMeshRenderer skin)
            { Skin = skin; Bones = skin.bones; Poses = skin.sharedMesh.bindposes; }
        }

        private bool BuildBones(Renderer[] renderers, Transform root, Dictionary<Transform, Matrix4x4> bones)
        {
            var imported = new HashSet<Transform>();
            var pending = new List<SkinBinding>();
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is SkinnedMeshRenderer skin) || skin.sharedMesh == null
                    || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var binding = new SkinBinding(skin); pending.Add(binding);
                foreach (Transform bone in binding.Bones) if (bone != null) imported.Add(bone);
            }
            // Native DoEquip also attaches skinned weapon prefabs to body bones.
            // Resolve that dependency before their own bind maps, independently
            // of renderer/sibling enumeration order. Never seed body bones from
            // a weapon's current animated parent pose.
            while (pending.Count != 0)
            {
                bool progress = false;
                for (int index = pending.Count - 1; index >= 0; index--)
                {
                    SkinBinding binding = pending[index]; bool waiting = false;
                    for (Transform? parent = binding.Skin.transform.parent; parent != null && parent != root && parent != root.parent; parent = parent.parent)
                        if (imported.Contains(parent)) { waiting = !bones.ContainsKey(parent); break; }
                    if (waiting) continue;
                    Matrix4x4 skinToCamera = AuthoredTransform(binding.Skin.transform, root, bones);
                    for (int i = 0; i < Math.Min(binding.Bones.Length, binding.Poses.Length); i++)
                        if (binding.Bones[i] != null && !bones.ContainsKey(binding.Bones[i]))
                            bones.Add(binding.Bones[i], skinToCamera * binding.Poses[i].inverse);
                    pending.RemoveAt(index); progress = true;
                }
                if (!progress) return false; // An unresolved rig keeps native mono; never cache an animated approximation.
            }
            return true;
        }

        private Matrix4x4 AuthoredTransform(Transform renderer, Transform root, Dictionary<Transform, Matrix4x4> bones)
        {
            for (Transform? ancestor = renderer; ancestor != null && ancestor != root.parent; ancestor = ancestor.parent)
                if (bones.TryGetValue(ancestor, out Matrix4x4 bind))
                    return bind * ancestor.worldToLocalMatrix * renderer.localToWorldMatrix;
            return _camera.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
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
